using System.IO;
using System.Linq;
using Fragile.Breakables;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Fragile > Placer les bouteilles du comptoir :
    /// importe les 6 bouteilles exportées de Blender, en fait des préfabs cassables (éclats de verre + liquide),
    /// puis les pose sur le comptoir de la scène aux emplacements choisis dans Blender (placements_comptoir.json).
    /// Relancer remplace les bouteilles (utile après un nouvel export).
    /// </summary>
    public static class CounterBottlesPlacer
    {
        const string Folder = "Assets/Bar/Models/Bouteilles";
        const string RootName = "Bouteilles comptoir";

        [System.Serializable] class Placement { public string type; public float x, y, z, angle; }
        [System.Serializable] class PlacementFile { public Placement[] bottles; }

        // Liquide de chaque bouteille.
        static Color LiquidFor(string type)
        {
            if (type.Contains("Whisky")) return new Color(0.78f, 0.45f, 0.1f, 0.85f);
            if (type.Contains("Korken")) return new Color(0.6f, 0.32f, 0.08f, 0.85f);
            if (type.Contains("Flasque")) return new Color(0.72f, 0.42f, 0.12f, 0.85f);
            if (type.Contains("Coloree")) return new Color(0.5f, 0.05f, 0.12f, 0.85f);   // vin
            if (type.Contains("Bleue")) return new Color(0.85f, 0.9f, 1f, 0.6f);         // gin
            return new Color(0.75f, 0.88f, 1f, 0.55f);                                   // eau
        }

        [MenuItem("Fragile/Placer les bouteilles du comptoir")]
        public static void Place()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            var jsonPath = $"{Folder}/placements_comptoir.json";
            if (!File.Exists(jsonPath)) { Debug.LogWarning("[Fragile] " + jsonPath + " introuvable : exporte d'abord depuis Blender."); return; }
            var file = JsonUtility.FromJson<PlacementFile>(File.ReadAllText(jsonPath));

            // Comptoir de la scène : objet "Comptoir", ou instance du modèle Comptoir.fbx.
            var counter = FindCounter();
            if (counter == null) { Debug.LogWarning("[Fragile] Comptoir introuvable dans la scène (objet « Comptoir »)."); return; }
            var counterColliders = counter.GetComponentsInChildren<Collider>();
            if (counterColliders.Length == 0) { Debug.LogWarning("[Fragile] Le comptoir n'a pas de collider."); return; }

            // 1) Préfabs cassables, un par type.
            var prefabs = file.bottles.Select(b => b.type).Distinct().ToDictionary(t => t, BuildPrefab);

            // 2) Conversion Blender -> Unity : on teste les sens possibles, on garde celui où les bouteilles tombent sur le comptoir.
            Vector3 Map(Placement p, int sx, int sz) => new Vector3(sx * -p.x, p.z, sz * -p.y);
            int bestX = 1, bestZ = 1, bestHits = -1;
            foreach (var sx in new[] { 1, -1 })
                foreach (var sz in new[] { 1, -1 })
                {
                    int hits = file.bottles.Count(p => SurfaceUnder(counter.transform.TransformPoint(Map(p, sx, sz)), counterColliders, out _));
                    if (hits > bestHits) { bestHits = hits; bestX = sx; bestZ = sz; }
                }

            // 3) Pose.
            var old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            var root = new GameObject(RootName).transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Placer les bouteilles");
            int placed = 0, missed = 0;
            foreach (var p in file.bottles)
            {
                if (!prefabs.TryGetValue(p.type, out var prefab) || prefab == null) continue;
                var world = counter.transform.TransformPoint(Map(p, bestX, bestZ));
                if (SurfaceUnder(world, counterColliders, out var surface)) world.y = surface; else missed++;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                go.transform.SetPositionAndRotation(world + Vector3.up * 0.001f, counter.transform.rotation * Quaternion.Euler(0f, -p.angle * bestX * bestZ, 0f));
                placed++;
            }

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Fragile] {placed} bouteilles posées sur le comptoir ({bestHits}/{file.bottles.Length} vérifiées sur la surface" +
                      (missed > 0 ? $", {missed} hors surface : à vérifier" : "") + "). Toutes cassables à la batte.");
        }

        static GameObject FindCounter()
        {
            var named = GameObject.Find("Comptoir");
            if (named != null) return named;
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/Models/Comptoir/Comptoir.fbx");
            foreach (var r in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                    if (PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == fbx) return t.gameObject;
            return null;
        }

        /// <summary>Hauteur de la surface plane du comptoir sous ce point (rayon vertical), si elle existe.</summary>
        static bool SurfaceUnder(Vector3 p, Collider[] colliders, out float y)
        {
            y = 0f;
            var ray = new Ray(p + Vector3.up * 1.5f, Vector3.down);
            float best = float.MaxValue; bool found = false;
            foreach (var c in colliders)
                if (c.Raycast(ray, out var hit, 3f) && hit.normal.y > 0.95f && hit.distance < best)
                { best = hit.distance; y = hit.point.y; found = true; }
            return found && Mathf.Abs(y - p.y) < 0.1f;
        }

        static GameObject BuildPrefab(string type)
        {
            string folder = $"{Folder}/{type}";
            BlenderModelImporter.ImportFolder(folder);
            var fbxPath = Directory.GetFiles(folder, "*.fbx").Select(f => f.Replace('\\', '/')).FirstOrDefault();
            if (fbxPath == null) { Debug.LogWarning("[Fragile] Pas de FBX pour " + type); return null; }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = type;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                if (!mf.TryGetComponent<Collider>(out _)) { var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; mc.convex = true; }

            // Bouteille en verre : les éclats sont en verre (même matériau dedans et sur la tranche), pas en céramique.
            var renderer = go.GetComponentInChildren<MeshRenderer>();
            var glass = renderer.sharedMaterials.FirstOrDefault(m => m != null && m.GetFloat("_Surface") > 0.5f);
            BreakableBuilder.MakeBreakablePublic(go, glass);

            var br = go.GetComponent<Breakable>();
            var so = new SerializedObject(br);
            so.FindProperty("m_Mass").floatValue = 0.9f;
            so.FindProperty("m_LiquidColor").colorValue = LiquidFor(type);
            var tint = glass != null ? glass.color : renderer.sharedMaterial.color;
            so.FindProperty("m_ChipColor").colorValue = new Color(Mathf.Lerp(tint.r, 1f, 0.5f), Mathf.Lerp(tint.g, 1f, 0.5f), Mathf.Lerp(tint.b, 1f, 0.5f));
            so.FindProperty("m_ChipColor2").colorValue = new Color(tint.r, tint.g, tint.b);
            so.ApplyModifiedPropertiesWithoutUndo();
            if (go.TryGetComponent<Rigidbody>(out var rb)) rb.mass = 0.9f;
            GrabbableBuilder.MakeGrabbable(go);

            string prefabPath = $"{folder}/{type}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }
    }
}
