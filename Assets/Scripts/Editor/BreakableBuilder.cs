using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fragile.Breakables;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Fragile > Rendre cassable (sélection) :
    /// - pré-découpe le maillage en éclats (cellules de Voronoï), chaque éclat ayant une épaisseur (paroi de céramique) ;
    /// - transforme l'objet en objet physique cassable (<see cref="Breakable"/>) ;
    /// - s'assure que la batte peut casser (<see cref="Breaker"/>).
    /// </summary>
    public static class BreakableBuilder
    {
        static readonly int[] LevelCounts = { 5, 12, 22 }; // coup mou -> coup violent
        const string FxFolder = "Assets/Bar/FX";
        const string BatPrefab = "Assets/Bar/Batte.prefab";

        [MenuItem("Fragile/Rendre cassable (sélection)")]
        public static void MakeSelectionBreakable()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            var targets = Selection.gameObjects.Where(g => g.scene.IsValid()).ToArray();
            if (targets.Length == 0) { Debug.LogWarning("[Fragile] Sélectionne un objet de la scène (ex. puff) puis relance."); return; }

            var fxPrefab = BreakFxBuilder.GetOrBuild(GetParticleMaterial(), FxFolder);
            var innerMat = GetInnerMaterial();
            foreach (var go in targets) { MakeBreakable(go, fxPrefab, innerMat); GrabbableBuilder.MakeGrabbable(go); }
            EnsureBatBreaker();

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Fragile/Rendre cassable (sélection)", true)]
        static bool Validate() => Selection.activeGameObject != null;

        /// <summary>Rend un objet cassable (utilisable par d'autres outils, ex. bouteilles du comptoir).</summary>
        public static void MakeBreakablePublic(GameObject go, Material innerOverride = null)
        {
            var fxPrefab = BreakFxBuilder.GetOrBuild(GetParticleMaterial(), FxFolder);
            MakeBreakable(go, fxPrefab, innerOverride != null ? innerOverride : GetInnerMaterial());
        }

        static void MakeBreakable(GameObject go, GameObject fxPrefab, Material innerMat)
        {
            var mf = go.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) { Debug.LogWarning($"[Fragile] {go.name} : pas de maillage."); return; }
            var mesh = mf.sharedMesh;
            EnsureReadable(mesh);
            mesh = mf.sharedMesh;

            // 1) Éclats.
            var renderer = mf.GetComponent<MeshRenderer>();
            float maxExtent = mesh.bounds.size.magnitude;
            float thickness = Mathf.Clamp(maxExtent * 0.015f, 0.002f, 0.008f);
            var levels = new List<List<Shard>>();
            for (int l = 0; l < LevelCounts.Length; l++)
                levels.Add(Fracture(mesh, LevelCounts[l], thickness, go.name.GetHashCode() + l * 7919));

            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(mesh))?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets")) folder = "Assets/Bar";
            string assetPath = $"{folder}/{SafeName(go.name)}_Eclats.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null) AssetDatabase.DeleteAsset(assetPath);
            bool first = true;
            for (int l = 0; l < levels.Count; l++)
                for (int i = 0; i < levels[l].Count; i++)
                {
                    levels[l][i].mesh.name = $"{go.name} niveau {l} éclat {i}";
                    if (first) { AssetDatabase.CreateAsset(levels[l][i].mesh, assetPath); first = false; }
                    else AssetDatabase.AddObjectToAsset(levels[l][i].mesh, assetPath);
                }
            AssetDatabase.ImportAsset(assetPath);

            // 2) Physique : Rigidbody + collider convexe.
            Undo.RegisterFullObjectHierarchyUndo(go, "Rendre cassable");
            foreach (var mc in go.GetComponentsInChildren<MeshCollider>()) mc.convex = true;
            if (go.GetComponentInChildren<Collider>() == null)
            {
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh; mc.convex = true;
            }
            foreach (var t in go.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, (StaticEditorFlags)0); // un objet qui bouge ne peut pas être statique
            if (!go.TryGetComponent<Rigidbody>(out var rb)) rb = go.AddComponent<Rigidbody>();
            rb.mass = 1.2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // 3) Composant cassable.
            if (!go.TryGetComponent<Breakable>(out var br)) br = go.AddComponent<Breakable>();
            var so = new SerializedObject(br);
            var levelsProp = so.FindProperty("m_Levels"); levelsProp.arraySize = levels.Count;
            for (int l = 0; l < levels.Count; l++)
            {
                var lp = levelsProp.GetArrayElementAtIndex(l);
                var meshes = lp.FindPropertyRelative("meshes"); meshes.arraySize = levels[l].Count;
                var offsets = lp.FindPropertyRelative("offsets"); offsets.arraySize = levels[l].Count;
                for (int i = 0; i < levels[l].Count; i++)
                {
                    meshes.GetArrayElementAtIndex(i).objectReferenceValue = levels[l][i].mesh;
                    offsets.GetArrayElementAtIndex(i).vector3Value = levels[l][i].offset;
                }
            }
            var mats = renderer.sharedMaterials.ToList();
            while (mats.Count < mesh.subMeshCount) mats.Add(mats.Count > 0 ? mats[mats.Count - 1] : innerMat);
            mats = mats.Take(mesh.subMeshCount).ToList();
            mats.Add(innerMat);
            var matProp = so.FindProperty("m_ShardMaterials"); matProp.arraySize = mats.Count;
            for (int i = 0; i < mats.Count; i++) matProp.GetArrayElementAtIndex(i).objectReferenceValue = mats[i];
            so.FindProperty("m_MeshTransform").objectReferenceValue = mf.transform;
            so.FindProperty("m_FxPrefab").objectReferenceValue = fxPrefab;
            so.ApplyModifiedProperties();

            Debug.Log($"[Fragile] {go.name} est cassable : {string.Join(" / ", levels.Select(x => x.Count))} éclats selon la force du coup, {thickness * 1000f:F1} mm d'épaisseur ({assetPath}).");
        }

        static void EnsureBatBreaker()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BatPrefab) == null) return;
            var root = PrefabUtility.LoadPrefabContents(BatPrefab);
            if (!root.TryGetComponent<Breaker>(out _)) root.AddComponent<Breaker>();
            PrefabUtility.SaveAsPrefabAsset(root, BatPrefab);
            PrefabUtility.UnloadPrefabContents(root);
        }

        static void EnsureReadable(Mesh mesh)
        {
            if (mesh.isReadable) return;
            var path = AssetDatabase.GetAssetPath(mesh);
            if (AssetImporter.GetAtPath(path) is ModelImporter mi) { mi.isReadable = true; mi.SaveAndReimport(); }
        }

        // ------------------------------------------------------------------ matériaux

        static Material GetInnerMaterial()
        {
            EnsureFolder();
            string path = $"{FxFolder}/Ceramique_Interieur.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.color = new Color(0.93f, 0.92f, 0.89f);
            mat.SetFloat("_Smoothness", 0.15f);
            mat.SetFloat("_Cull", 0f); // double face : tranches et intérieur visibles de partout
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material GetParticleMaterial()
        {
            EnsureFolder();
            string texPath = $"{FxFolder}/Particule_Rond.png";
            if (!File.Exists(texPath))
            {
                const int s = 64;
                var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float d = new Vector2(x - s / 2f + 0.5f, y - s / 2f + 0.5f).magnitude / (s / 2f);
                        float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.55f, 1f, d));
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                tex.Apply();
                File.WriteAllBytes(texPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath);
                var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
                ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
            }
            string path = $"{FxFolder}/Particules_Casse.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            // Transparent, mélange alpha.
            mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.SetOverrideTag("RenderType", "Transparent");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(FxFolder)) AssetDatabase.CreateFolder("Assets/Bar", "FX");
        }

        static string SafeName(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());

        // ------------------------------------------------------------------ découpe

        struct V
        {
            public Vector3 p, n; public Vector2 uv;
            public static V Lerp(V a, V b, float t) => new V { p = Vector3.Lerp(a.p, b.p, t), n = Vector3.Lerp(a.n, b.n, t).normalized, uv = Vector2.Lerp(a.uv, b.uv, t) };
        }

        struct Tri { public V a, b, c; public int sub; }

        public struct Shard { public Mesh mesh; public Vector3 offset; }

        /// <summary>Découpe en cellules de Voronoï autour de points tirés sur la surface ; chaque éclat reçoit une épaisseur.</summary>
        public static List<Shard> Fracture(Mesh src, int count, float thickness, int seed)
        {
            var verts = src.vertices;
            var normals = src.normals;
            if (normals == null || normals.Length != verts.Length) { var tmp = Object.Instantiate(src); tmp.RecalculateNormals(); normals = tmp.normals; Object.DestroyImmediate(tmp); }
            var uvs = src.uv; if (uvs == null || uvs.Length != verts.Length) uvs = new Vector2[verts.Length];

            var tris = new List<Tri>();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var idx = src.GetTriangles(s);
                for (int i = 0; i < idx.Length; i += 3)
                    tris.Add(new Tri { a = MakeV(idx[i]), b = MakeV(idx[i + 1]), c = MakeV(idx[i + 2]), sub = s });
            }
            V MakeV(int i) => new V { p = verts[i], n = normals[i], uv = uvs[i] };

            // Points de départ : sur la surface (pondérés par l'aire), un peu rentrés.
            var rng = new System.Random(seed);
            var areas = tris.Select(t => Vector3.Cross(t.b.p - t.a.p, t.c.p - t.a.p).magnitude * 0.5f).ToArray();
            float total = areas.Sum();
            var seeds = new List<Vector3>();
            for (int k = 0; k < count; k++)
            {
                float r = (float)rng.NextDouble() * total; int ti = 0;
                for (; ti < areas.Length - 1 && r > areas[ti]; ti++) r -= areas[ti];
                float u = (float)rng.NextDouble(), v = (float)rng.NextDouble();
                if (u + v > 1f) { u = 1f - u; v = 1f - v; }
                var t = tris[ti];
                var p = t.a.p + (t.b.p - t.a.p) * u + (t.c.p - t.a.p) * v;
                var n = (t.a.n + t.b.n + t.c.n).normalized;
                seeds.Add(p - n * thickness * 2f);
            }

            var result = new List<Shard>();
            for (int i = 0; i < seeds.Count; i++)
            {
                var cell = tris;
                for (int j = 0; j < seeds.Count && cell.Count > 0; j++)
                {
                    if (i == j) continue;
                    var n = (seeds[j] - seeds[i]).normalized;
                    float d = Vector3.Dot(n, (seeds[i] + seeds[j]) * 0.5f);
                    cell = Clip(cell, n, d);
                }
                if (cell.Count < 4) continue;
                result.Add(BuildShard(cell, src.subMeshCount, thickness));
            }
            return result;
        }

        /// <summary>Garde la partie des triangles du côté dot(n,p) <= d.</summary>
        static List<Tri> Clip(List<Tri> input, Vector3 n, float d)
        {
            var output = new List<Tri>(input.Count);
            var poly = new List<V>(6);
            var next = new List<V>(6);
            foreach (var t in input)
            {
                float da = Vector3.Dot(n, t.a.p) - d, db = Vector3.Dot(n, t.b.p) - d, dc = Vector3.Dot(n, t.c.p) - d;
                if (da <= 0 && db <= 0 && dc <= 0) { output.Add(t); continue; }
                if (da > 0 && db > 0 && dc > 0) continue;

                poly.Clear(); poly.Add(t.a); poly.Add(t.b); poly.Add(t.c);
                next.Clear();
                for (int k = 0; k < poly.Count; k++)
                {
                    var cur = poly[k]; var nxt = poly[(k + 1) % poly.Count];
                    float dc0 = Vector3.Dot(n, cur.p) - d, dn0 = Vector3.Dot(n, nxt.p) - d;
                    if (dc0 <= 0) next.Add(cur);
                    if ((dc0 <= 0) != (dn0 <= 0)) next.Add(V.Lerp(cur, nxt, dc0 / (dc0 - dn0)));
                }
                for (int k = 1; k + 1 < next.Count; k++)
                    output.Add(new Tri { a = next[0], b = next[k], c = next[k + 1], sub = t.sub });
            }
            return output;
        }

        /// <summary>Paroi extérieure (matériaux d'origine) + paroi intérieure décalée + tranches sur les bords coupés.</summary>
        static Shard BuildShard(List<Tri> cell, int subCount, float thickness)
        {
            var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>();
            var subs = new List<int>[subCount + 1];
            for (int s = 0; s <= subCount; s++) subs[s] = new List<int>();
            int inner = subCount;

            int Add(Vector3 p, Vector3 n, Vector2 u) { pos.Add(p); nrm.Add(n); uv.Add(u); return pos.Count - 1; }

            // Arêtes de bord : présentes une seule fois (positions soudées).
            var edgeCount = new Dictionary<(long, long), int>();
            var edgeDir = new Dictionary<(long, long), (V, V)>();
            long Key(Vector3 p) { long x = (long)Mathf.Round(p.x * 1e5f), y = (long)Mathf.Round(p.y * 1e5f), z = (long)Mathf.Round(p.z * 1e5f); return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L); }
            void Edge(V a, V b)
            {
                long ka = Key(a.p), kb = Key(b.p); if (ka == kb) return;
                var k = ka < kb ? (ka, kb) : (kb, ka);
                edgeCount[k] = edgeCount.TryGetValue(k, out int c) ? c + 1 : 1;
                edgeDir[k] = (a, b);
            }

            foreach (var t in cell)
            {
                // Extérieur.
                subs[t.sub].Add(Add(t.a.p, t.a.n, t.a.uv)); subs[t.sub].Add(Add(t.b.p, t.b.n, t.b.uv)); subs[t.sub].Add(Add(t.c.p, t.c.n, t.c.uv));
                // Intérieur (décalé vers l'intérieur, ordre inversé).
                subs[inner].Add(Add(t.a.p - t.a.n * thickness, -t.a.n, Vector2.zero));
                subs[inner].Add(Add(t.c.p - t.c.n * thickness, -t.c.n, Vector2.zero));
                subs[inner].Add(Add(t.b.p - t.b.n * thickness, -t.b.n, Vector2.zero));
                Edge(t.a, t.b); Edge(t.b, t.c); Edge(t.c, t.a);
            }

            // Tranches.
            foreach (var kv in edgeCount)
            {
                if (kv.Value != 1) continue;
                var (a, b) = edgeDir[kv.Key];
                var ai = a.p - a.n * thickness; var bi = b.p - b.n * thickness;
                var n = Vector3.Cross(b.p - a.p, ai - a.p).normalized;
                int i0 = Add(a.p, n, Vector2.zero), i1 = Add(b.p, n, Vector2.zero), i2 = Add(bi, n, Vector2.zero), i3 = Add(ai, n, Vector2.zero);
                subs[inner].AddRange(new[] { i0, i2, i1, i0, i3, i2 });
            }

            // Recentrage : chaque éclat tourne autour de son propre centre.
            var b0 = new Bounds(pos[0], Vector3.zero); foreach (var p in pos) b0.Encapsulate(p);
            var center = b0.center;
            for (int i = 0; i < pos.Count; i++) pos[i] -= center;

            var mesh = new Mesh { indexFormat = pos.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(pos); mesh.SetNormals(nrm); mesh.SetUVs(0, uv);
            mesh.subMeshCount = subCount + 1;
            for (int s = 0; s <= subCount; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateBounds();
            return new Shard { mesh = mesh, offset = center };
        }
    }
}
