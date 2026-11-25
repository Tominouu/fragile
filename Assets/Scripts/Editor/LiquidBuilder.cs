using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fragile.Breakables;
using UnityEditor;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Fragile > Remplir les bouteilles : ajoute un liquide dans chaque bouteille en verre (préfabs du comptoir).
    /// Le volume est la paroi de verre rentrée de quelques millimètres ; le shader Fragile/Liquide le coupe au niveau voulu.
    /// </summary>
    public static class LiquidBuilder
    {
        const string BottlesFolder = "Assets/Bar/Models/Bouteilles";
        const string MatFolder = "Assets/Bar/FX/Liquides";
        const float Inset = 0.003f;

        struct Liquid { public string name; public Color body, surface; public float fill; }

        static Liquid For(string type)
        {
            if (type.Contains("Whisky")) return new Liquid { name = "Whisky", body = new Color(0.75f, 0.42f, 0.1f, 0.8f), surface = new Color(0.92f, 0.62f, 0.28f, 0.85f), fill = 0.6f };
            if (type.Contains("Korken")) return new Liquid { name = "Rhum", body = new Color(0.55f, 0.27f, 0.07f, 0.85f), surface = new Color(0.75f, 0.42f, 0.15f, 0.9f), fill = 0.7f };
            if (type.Contains("Coloree")) return new Liquid { name = "Vin", body = new Color(0.32f, 0.02f, 0.07f, 0.9f), surface = new Color(0.45f, 0.06f, 0.1f, 0.92f), fill = 0.65f };
            // Liquides clairs : presque invisibles dans la masse, on voit surtout leur surface.
            if (type.Contains("Bleue")) return new Liquid { name = "Gin", body = new Color(0.45f, 0.6f, 0.72f, 0.06f), surface = new Color(0.6f, 0.72f, 0.82f, 0.14f), fill = 0.7f };
            return new Liquid { name = "Eau", body = new Color(0.45f, 0.6f, 0.72f, 0.06f), surface = new Color(0.6f, 0.72f, 0.82f, 0.14f), fill = 0.75f };
        }

        [MenuItem("Fragile/Remplir les bouteilles")]
        public static void FillAll()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            var glassShader = Shader.Find("Fragile/Verre");
            var liquidShader = Shader.Find("Fragile/Liquide");
            if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder("Assets/Bar/FX", "Liquides");
            var report = new List<string>();

            foreach (var path in Directory.GetFiles(BottlesFolder, "*.prefab", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                var type = Path.GetFileNameWithoutExtension(path);
                var mf = root.GetComponent<MeshFilter>();
                var mr = root.GetComponent<MeshRenderer>();
                int glassSub = mr != null ? System.Array.FindIndex(mr.sharedMaterials, m => m != null && m.shader == glassShader) : -1;
                var old = root.transform.Find("Liquide");
                if (old != null) Object.DestroyImmediate(old.gameObject);

                if (mf == null || glassSub < 0) { report.Add($"{type} : pas de verre, pas de liquide"); PrefabUtility.UnloadPrefabContents(root); continue; }

                var liquid = For(type);
                var mesh = BuildInnerMesh(mf.sharedMesh, glassSub, Inset, $"{Path.GetDirectoryName(path).Replace('\\', '/')}/{type}_Liquide.asset");
                var mat = GetMaterial(liquid, liquidShader);

                var go = new GameObject("Liquide");
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                var fill = go.AddComponent<LiquidFill>();
                var so = new SerializedObject(fill);
                so.FindProperty("m_Fill").floatValue = liquid.fill;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                report.Add($"{type} : {liquid.name}, {mesh.triangles.Length / 3} triangles");
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Fragile] Bouteilles remplies :\n" + string.Join("\n", report));
        }

        /// <summary>Copie du sous-maillage de verre, rentrée vers l'intérieur.</summary>
        static Mesh BuildInnerMesh(Mesh src, int sub, float inset, string assetPath)
        {
            var verts = src.vertices; var normals = src.normals;
            var tris = src.GetTriangles(sub);
            var map = new Dictionary<int, int>();
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>(tris.Length);
            foreach (var i in tris)
            {
                if (!map.TryGetValue(i, out int j))
                {
                    j = v.Count; map[i] = j;
                    v.Add(verts[i] - normals[i] * inset);
                    n.Add(normals[i]);
                }
                t.Add(j);
            }
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            bool created = mesh == null;
            if (created) mesh = new Mesh();
            mesh.Clear();
            mesh.name = Path.GetFileNameWithoutExtension(assetPath);
            mesh.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, assetPath); else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        static Material GetMaterial(Liquid l, Shader shader)
        {
            string path = $"{MatFolder}/Liquide_{l.name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader;
            mat.SetColor("_Color", l.body);
            mat.SetColor("_SurfaceColor", l.surface);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 10;
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
