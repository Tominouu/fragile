using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Génère des mains "réalistes" à partir des modèles Android XR des XR Hands :
    /// - copie du mesh avec des UV séparant paume (moitié gauche) et dos (moitié droite) ;
    /// - texture de peau peinte procéduralement (ongles, plis, bouts des doigts rosés, variations).
    /// Résultat dans Assets/Hands/. Menu : Fragile > Générer les mains réalistes.
    /// </summary>
    public static class RealisticHandBuilder
    {
        const string SourceFolder = "Assets/Samples/XR Hands/1.9.0/HandVisualizer/Models/";
        const string OutFolder = "Assets/Hands";
        const int TexSize = 1024;

        static readonly Color k_Skin = new Color(0.86f, 0.64f, 0.52f);
        static readonly Color k_SkinPalm = new Color(0.90f, 0.66f, 0.56f);
        static readonly Color k_SkinTip = new Color(0.88f, 0.55f, 0.48f);
        static readonly Color k_Crease = new Color(0.66f, 0.44f, 0.36f);
        static readonly Color k_Nail = new Color(0.92f, 0.72f, 0.68f);
        static readonly Color k_NailEdge = new Color(0.97f, 0.91f, 0.86f);

        [MenuItem("Fragile/Générer les mains réalistes")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(OutFolder))
                AssetDatabase.CreateFolder("Assets", "Hands");

            foreach (var side in new[] { "Left", "Right" })
                BuildSide(side);

            AssetDatabase.SaveAssets();
            Debug.Log("[Fragile] Mains réalistes générées dans " + OutFolder);
        }

        struct Finger
        {
            public Vector3 distal, tip, up; // en espace mesh
        }

        static void BuildSide(string side)
        {
            string p = side.Substring(0, 1) + "_";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(SourceFolder + side + "HandAndroidXR.fbx");
            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            var src = smr.sharedMesh;

            // Positions/axes des os en espace mesh (via les bindposes).
            var boneIndex = new Dictionary<string, int>();
            for (int i = 0; i < smr.bones.Length; i++) boneIndex[smr.bones[i].name] = i;
            var bind = src.bindposes;
            var allTransforms = model.GetComponentsInChildren<Transform>(true);
            System.Func<string, Matrix4x4> boneToMesh = n =>
            {
                if (boneIndex.TryGetValue(p + n, out int bi))
                    return bind[bi].inverse;
                // Os non skinné (ex. *_Tip) : on passe par la hiérarchie du modèle, qui est en pose de bind.
                foreach (var t in allTransforms)
                    if (t.name == p + n)
                        return smr.transform.worldToLocalMatrix * t.localToWorldMatrix;
                throw new System.Exception("Os introuvable : " + p + n);
            };

            var fingers = new List<Finger>();
            foreach (var f in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                var d = boneToMesh(f + "Distal");
                fingers.Add(new Finger
                {
                    distal = d.MultiplyPoint3x4(Vector3.zero),
                    tip = boneToMesh(f + "Tip").MultiplyPoint3x4(Vector3.zero),
                    up = d.MultiplyVector(Vector3.up).normalized,
                });
            }
            var joints = new List<Vector3>();
            foreach (var f in new[] { "Index", "Middle", "Ring", "Little" })
                foreach (var j in new[] { "Proximal", "Intermediate", "Distal" })
                    joints.Add(boneToMesh(f + j).MultiplyPoint3x4(Vector3.zero));
            foreach (var j in new[] { "ThumbProximal", "ThumbDistal" })
                joints.Add(boneToMesh(j).MultiplyPoint3x4(Vector3.zero));

            // --- Mesh : sépare paume / dos dans les UV en dupliquant les sommets partagés.
            var verts = src.vertices; var normals = src.normals; var uvs = src.uv;
            var weights = src.boneWeights; var tris = src.triangles;
            var nVerts = new List<Vector3>(); var nNormals = new List<Vector3>(); var nUvs = new List<Vector2>();
            var nWeights = new List<BoneWeight>(); var nTris = new int[tris.Length];
            var remap = new Dictionary<long, int>();
            var isBack = new List<bool>();

            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 fn = normals[tris[t]] + normals[tris[t + 1]] + normals[tris[t + 2]];
                bool back = fn.y > 0f; // dos de la main = +Y (la paume regarde -Y)
                // Le pouce est tourné sur le côté : son "dos" (côté ongle) suit l'orientation de sa dernière phalange.
                var centroid = (verts[tris[t]] + verts[tris[t + 1]] + verts[tris[t + 2]]) / 3f;
                if (OnFinger(centroid, fingers[0], out _, out _))
                    back = Vector3.Dot(fn, fingers[0].up) > 0f;
                for (int k = 0; k < 3; k++)
                {
                    int vi = tris[t + k];
                    long key = vi * 2L + (back ? 1 : 0);
                    if (!remap.TryGetValue(key, out int ni))
                    {
                        ni = nVerts.Count;
                        remap[key] = ni;
                        nVerts.Add(verts[vi]); nNormals.Add(normals[vi]); nWeights.Add(weights[vi]);
                        var uv = uvs[vi];
                        // UV d'origine : u ∈ [0, 0.62]. Paume -> [0, 0.5], dos -> [0.5, 1].
                        nUvs.Add(new Vector2(uv.x / 0.64f * 0.5f + (back ? 0.5f : 0f), uv.y));
                        isBack.Add(back);
                    }
                    nTris[t + k] = ni;
                }
            }

            // Mise à jour en place via l'API Mesh (pas CopySerialized : avec lui, les SkinnedMeshRenderer gardent
            // l'ancienne taille de maillage en mémoire graphique et cessent d'afficher la main).
            string meshPath = $"{OutFolder}/{side}Hand_Realistic.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            bool created = mesh == null;
            if (created) mesh = new Mesh();
            mesh.Clear();
            mesh.name = side + "Hand_Realistic";
            mesh.indexFormat = nVerts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(nVerts); mesh.SetNormals(nNormals); mesh.SetUVs(0, nUvs);
            mesh.boneWeights = nWeights.ToArray(); mesh.bindposes = bind;
            mesh.SetTriangles(nTris, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, meshPath);
            else EditorUtility.SetDirty(mesh);

            // Les renderers déjà dans la scène doivent repartir des nouvelles données.
            foreach (var r in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
                if (r.sharedMesh == mesh) { r.sharedMesh = null; r.sharedMesh = mesh; }

            // --- Texture : rasterise chaque triangle en UV et peint selon la position 3D.
            var px = new Color[TexSize * TexSize];
            var filled = new bool[TexSize * TexSize];
            for (int t = 0; t < nTris.Length; t += 3)
            {
                int a = nTris[t], b = nTris[t + 1], c = nTris[t + 2];
                Raster(nUvs[a], nUvs[b], nUvs[c], (w0, w1, w2, x, y) =>
                {
                    var pos = nVerts[a] * w0 + nVerts[b] * w1 + nVerts[c] * w2;
                    var nrm = (nNormals[a] * w0 + nNormals[b] * w1 + nNormals[c] * w2).normalized;
                    int idx = y * TexSize + x;
                    px[idx] = Paint(pos, nrm, isBack[a], fingers, joints);
                    filled[idx] = true;
                });
            }
            Dilate(px, filled, 6); // évite les coutures sombres aux bords des îlots UV

            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGB24, true);
            tex.SetPixels(px); tex.Apply();
            string texPath = $"{OutFolder}/{side}Hand_Skin.png";
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
            importer.wrapMode = TextureWrapMode.Clamp; importer.anisoLevel = 4; importer.SaveAndReimport();

            // --- Matériau
            string matPath = $"{OutFolder}/{side}Hand_Skin.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            mat.color = Color.white;
            mat.SetFloat("_Smoothness", 0.42f);
            mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 1f);
            EditorUtility.SetDirty(mat);
        }

        /// <summary>Le point est-il sur la dernière phalange (zone du bout et de l'ongle) de ce doigt ?</summary>
        static bool OnFinger(Vector3 pos, Finger f, out float along, out float dist)
        {
            Vector3 axis = f.tip - f.distal; float len = axis.magnitude; axis /= len;
            along = Vector3.Dot(pos - f.distal, axis) / len;
            dist = (pos - (f.distal + axis * along * len)).magnitude;
            return along >= -0.2f && along <= 1.4f && dist <= 0.016f;
        }

        static Color Paint(Vector3 pos, Vector3 nrm, bool back, List<Finger> fingers, List<Vector3> joints)
        {
            Color col = back ? k_Skin : k_SkinPalm;

            // Bouts des doigts plus rosés + ongles.
            foreach (var f in fingers)
            {
                Vector3 axis = f.tip - f.distal; float len = axis.magnitude; axis /= len;
                float along = Vector3.Dot(pos - f.distal, axis) / len; // 0 = articulation distale, 1 = bout
                Vector3 radial = pos - (f.distal + axis * along * len);
                float dist = radial.magnitude;
                if (along < -0.2f || along > 1.4f || dist > 0.016f) continue;

                float tipT = Mathf.InverseLerp(0.1f, 1.0f, along);
                col = Color.Lerp(col, k_SkinTip, tipT * 0.55f);

                // Ongle : uniquement sur le dos du doigt (faces "dos" + surface orientée vers le dessus),
                // de ~35 % de la phalange jusqu'un peu avant le bout. Le bout lui-même reste en peau :
                // ses faces basculent entre paume et dos dans la texture, ce qui donnait des triangles.
                if (back && along > 0.32f && along < 1.0f)
                {
                    Vector3 radialDir = dist > 1e-5f ? radial / dist : f.up;
                    float facing = Vector3.Dot(radialDir, f.up);
                    float side = Mathf.Sqrt(Mathf.Max(0f, 1f - facing * facing)); // 0 au centre de l'ongle
                    float width = facing > 0f ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.5f, side)) : 0f;
                    float onTop = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.65f, Vector3.Dot(nrm, f.up)));
                    float fromBase = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.32f, 0.42f, along));
                    float beforeTip = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.0f, 0.93f, along));
                    float nailMask = width * onTop * fromBase * beforeTip;

                    // Bord libre blanc : fine bande au bout de l'ongle, en dégradé.
                    Color nail = Color.Lerp(k_Nail, k_NailEdge, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.84f, 0.94f, along)));
                    // fin liseré plus sombre au bord de l'ongle
                    float rim = Mathf.Clamp01(1f - Mathf.Abs(nailMask - 0.5f) * 2f);
                    nail = Color.Lerp(nail, k_Crease, rim * 0.3f);
                    col = Color.Lerp(col, nail, nailMask);
                }
            }

            // Plis aux articulations (dos : rides fines ; paume : un pli net).
            foreach (var j in joints)
            {
                float d = Vector3.Distance(pos, j);
                if (d > 0.014f) continue;
                float ring = 1f - Mathf.InverseLerp(0.004f, 0.014f, d);
                if (back)
                {
                    // Rides courtes perpendiculaires au doigt plutôt que des cercles.
                    float wrinkle = Mathf.Abs(Mathf.Sin((pos.z - j.z) * 1100f)) * ring * ring;
                    col = Color.Lerp(col, k_Crease, wrinkle * 0.12f);
                }
                else
                {
                    col = Color.Lerp(col, k_Crease, ring * ring * 0.3f);
                }
            }

            // Variations subtiles de peau.
            float n1 = Mathf.PerlinNoise(pos.x * 90f + 13f, pos.z * 90f + pos.y * 40f);
            float n2 = Mathf.PerlinNoise(pos.x * 400f + 7f, pos.z * 400f);
            col *= 0.94f + 0.08f * n1 + 0.03f * n2;
            col.a = 1f;
            return col;
        }

        static void Raster(Vector2 a, Vector2 b, Vector2 c, System.Action<float, float, float, int, int> plot)
        {
            Vector2 A = a * TexSize, B = b * TexSize, C = c * TexSize;
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.x, Mathf.Min(B.x, C.x))));
            int maxX = Mathf.Min(TexSize - 1, Mathf.CeilToInt(Mathf.Max(A.x, Mathf.Max(B.x, C.x))));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.y, Mathf.Min(B.y, C.y))));
            int maxY = Mathf.Min(TexSize - 1, Mathf.CeilToInt(Mathf.Max(A.y, Mathf.Max(B.y, C.y))));
            float area = (B.x - A.x) * (C.y - A.y) - (C.x - A.x) * (B.y - A.y);
            if (Mathf.Abs(area) < 1e-8f) return;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 P = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = ((B.x - P.x) * (C.y - P.y) - (C.x - P.x) * (B.y - P.y)) / area;
                    float w1 = ((C.x - P.x) * (A.y - P.y) - (A.x - P.x) * (C.y - P.y)) / area;
                    float w2 = 1f - w0 - w1;
                    if (w0 < -0.01f || w1 < -0.01f || w2 < -0.01f) continue;
                    plot(w0, w1, w2, x, y);
                }
        }

        static void Dilate(Color[] px, bool[] filled, int passes)
        {
            for (int pass = 0; pass < passes; pass++)
            {
                var next = (bool[])filled.Clone();
                for (int y = 0; y < TexSize; y++)
                    for (int x = 0; x < TexSize; x++)
                    {
                        int i = y * TexSize + x;
                        if (filled[i]) continue;
                        Color sum = Color.clear; int n = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if (xx < 0 || yy < 0 || xx >= TexSize || yy >= TexSize) continue;
                                int j = yy * TexSize + xx;
                                if (filled[j]) { sum += px[j]; n++; }
                            }
                        if (n > 0) { px[i] = sum / n; next[i] = true; }
                    }
                System.Array.Copy(next, filled, filled.Length);
            }
        }
    }
}
