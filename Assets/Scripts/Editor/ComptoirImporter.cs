using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Configure le comptoir exporté de Blender (Assets/Bar/Models/Comptoir/Comptoir.fbx) et le place dans la scène Bar.
    /// - recrée ses 4 matériaux en URP (or, marbre, carrelage, bois) avec les textures extraites du .blend ;
    /// - collider de maillage pour qu'on ne le traverse pas ;
    /// - posé au sol, centré dans la pièce.
    /// Menu : Fragile > Importer le comptoir. Relancer remplace le comptoir existant.
    /// </summary>
    public static class ComptoirImporter
    {
        const string Folder = "Assets/Bar/Models/Comptoir";
        const string FbxPath = Folder + "/Comptoir.fbx";
        const string ScenePath = "Assets/Scenes/Bar.unity";

        [MenuItem("Fragile/Importer le comptoir")]
        public static void Import()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play avant d'importer le comptoir."); return; }

            // 1) Textures : normal maps en type Normal, rugosités en données linéaires.
            foreach (var name in new[] { "Marbre", "Carrelage", "Bois" })
            {
                SetupTexture($"{Folder}/{name}_Normal.jpg", normal: true, srgb: false);
                SetupTexture($"{Folder}/{name}_Roughness.jpg", normal: false, srgb: false);
            }

            // 2) Matériaux URP.
            var gold = MakeMaterial("Comptoir_Or", null, null, null, Vector2.one, new Color(1f, 0.76f, 0.34f), metallic: 1f, smoothness: 0.72f);
            var marble = MakeMaterial("Comptoir_Marbre", "Marbre_BaseColor.png", "Marbre_Normal.jpg", "Marbre_Roughness.jpg", Vector2.one, Color.white, 0f, 0.6f);
            var tile = MakeMaterial("Comptoir_Carrelage", "Carrelage_BaseColor.jpg", "Carrelage_Normal.jpg", "Carrelage_Roughness.jpg", new Vector2(3f, 3f), Color.white, 0f, 0.5f);
            var wood = MakeMaterial("Comptoir_Bois", "Bois_BaseColor.jpg", "Bois_Normal.jpg", "Bois_Roughness.jpg", Vector2.one, Color.white, 0f, 0.55f);

            // 3) Import du modèle : tangentes calculées par Unity, matériaux remplacés par les nôtres.
            var importer = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.importCameras = false; importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.SaveAndReimport();
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<Material>())
            {
                string n = sub.name.ToLowerInvariant();
                var target = n.Contains("gold") ? gold : n.Contains("marble") ? marble : n.Contains("tile") ? tile : n.Contains("wood") ? wood : null;
                if (target != null) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), sub.name), target);
                else Debug.LogWarning("[Fragile] Matériau du comptoir non reconnu : " + sub.name);
            }
            importer.SaveAndReimport();

            // 4) Placement dans la scène Bar.
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            var old = GameObject.Find("Comptoir");
            if (old != null) Undo.DestroyObjectImmediate(old);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = "Comptoir";
            var room = GameObject.Find("Room");
            if (room != null) go.transform.SetParent(room.transform, true);

            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                GameObjectUtility.SetStaticEditorFlags(mf.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
            }

            // Centré dans la pièce, posé sur le sol.
            var renderers = go.GetComponentsInChildren<Renderer>();
            var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
            go.transform.position += new Vector3(-b.center.x, -b.min.y, -b.center.z);
            Undo.RegisterCreatedObjectUndo(go, "Importer le comptoir");
            Selection.activeGameObject = go;

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Fragile] Comptoir importé : {b.size.x:F2} x {b.size.z:F2} m, {b.size.y:F2} m de haut, centré dans la pièce.");
        }

        static void SetupTexture(string path, bool normal, bool srgb)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (!normal) ti.sRGBTexture = srgb;
            ti.maxTextureSize = 2048;
            ti.anisoLevel = 4;
            ti.SaveAndReimport();
        }

        static Material MakeMaterial(string name, string baseMap, string normalMap, string roughMap, Vector2 tiling, Color color, float metallic, float smoothness)
        {
            string path = $"{Folder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.color = color;
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            if (baseMap != null)
            {
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/{baseMap}"));
                mat.SetTextureScale("_BaseMap", tiling);
            }
            if (normalMap != null)
            {
                mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/{normalMap}"));
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }
            // URP Lit n'a pas de carte de rugosité séparée : on garde une brillance moyenne, réglable dans l'Inspector.
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
