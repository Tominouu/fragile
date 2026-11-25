using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Importe les modèles exportés par l'addon Blender "Fragile - Export vers Unity"
    /// (dossiers contenant un fragile_export.json) : textures, matériaux URP, collider, placement dans la scène.
    /// Menu : Fragile > Importer les modèles Blender.
    /// - Les modèles sont placés à leur position Blender (origine Blender = origine Unity), sous "Modèles Blender".
    /// - Un modèle déjà présent dans la scène n'est pas re-placé (tes déplacements sont conservés) ; ses matériaux sont mis à jour.
    /// </summary>
    public static class BlenderModelImporter
    {
        const string ManifestName = "fragile_export.json";
        const string RootName = "Modèles Blender";

        [System.Serializable] class Manifest { public string objectName; public string fbx; public bool collider; public bool place = true; public MaterialEntry[] materials; }
        [System.Serializable]
        class MaterialEntry
        {
            public string name, baseColor, normal, roughness, metallicMap;
            public float[] tiling, color;
            public float metallic, smoothness;
            public bool transparent;
        }

        [MenuItem("Fragile/Importer les modèles Blender")]
        public static void ImportAll()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play avant d'importer."); return; }

            var manifests = Directory.GetFiles("Assets", ManifestName, SearchOption.AllDirectories);
            if (manifests.Length == 0) { Debug.LogWarning("[Fragile] Aucun modèle exporté depuis Blender (fragile_export.json) dans Assets."); return; }

            int placed = 0, updated = 0;
            foreach (var file in manifests)
            {
                var folder = Path.GetDirectoryName(file).Replace('\\', '/');
                var m = JsonUtility.FromJson<Manifest>(File.ReadAllText(file));
                if (m == null || string.IsNullOrEmpty(m.fbx)) continue;
                if (ImportOne(folder, m)) placed++; else updated++;
            }

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Fragile] Modèles Blender : {placed} placé(s) dans la scène « {scene.name} », {updated} mis à jour.");
        }

        /// <summary>Importe un seul dossier exporté (textures, matériaux) ; utilisé aussi par d'autres outils.</summary>
        public static void ImportFolder(string folder)
        {
            var file = $"{folder}/{ManifestName}";
            if (!File.Exists(file)) { Debug.LogWarning("[Fragile] Pas de " + ManifestName + " dans " + folder); return; }
            var m = JsonUtility.FromJson<Manifest>(File.ReadAllText(file));
            if (m != null) ImportOne(folder, m);
        }

        /// <returns>true si le modèle a été ajouté à la scène, false s'il y était déjà.</returns>
        static bool ImportOne(string folder, Manifest m)
        {
            // Nom de fichier réel sur le disque (Windows ignore la casse, l'AssetDatabase non).
            var onDisk = Directory.GetFiles(folder, "*.fbx").Select(p => Path.GetFileName(p))
                .FirstOrDefault(f => string.Equals(f, m.fbx, System.StringComparison.OrdinalIgnoreCase)) ?? m.fbx;
            string fbxPath = $"{folder}/{onDisk}";
            AssetDatabase.ImportAsset(fbxPath);

            // Matériaux URP.
            var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            if (importer == null) { Debug.LogWarning("[Fragile] FBX introuvable : " + fbxPath); return false; }
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.importCameras = false; importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.SaveAndReimport();

            var matFolder = $"{folder}/Materials";
            if (!AssetDatabase.IsValidFolder(matFolder)) AssetDatabase.CreateFolder(folder, "Materials");
            var sourceMats = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Material>().ToList();
            foreach (var e in m.materials ?? new MaterialEntry[0])
            {
                var mat = BuildMaterial(folder, matFolder, e);
                var src = sourceMats.FirstOrDefault(s => s.name == e.name) ?? sourceMats.FirstOrDefault(s => Normalize(s.name) == Normalize(e.name));
                if (src != null) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), src.name), mat);
                else Debug.LogWarning($"[Fragile] {m.objectName} : matériau « {e.name} » absent du FBX.");
            }
            importer.SaveAndReimport();

            // Modèle destiné à être placé par un autre outil (ex. bouteilles du comptoir) : pas de placement ici.
            if (!m.place) return false;

            // Placement (une seule fois) : si ce modèle est déjà quelque part dans la scène, on n'en ajoute pas un autre.
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            foreach (var r in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                    if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) && PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == model)
                        return false;

            var root = GameObject.Find(RootName) ?? new GameObject(RootName);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            go.name = m.objectName;
            if (m.collider)
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    GameObjectUtility.SetStaticEditorFlags(mf.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
                }
            Undo.RegisterCreatedObjectUndo(go, "Importer " + m.objectName);
            return true;
        }

        static Material BuildMaterial(string folder, string matFolder, MaterialEntry e)
        {
            string path = $"{matFolder}/{SafeName(e.name)}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }

            var c = e.color != null && e.color.Length >= 3 ? new Color(e.color[0], e.color[1], e.color[2], e.color.Length > 3 ? e.color[3] : 1f) : Color.white;
            mat.color = c;
            mat.SetFloat("_Metallic", e.metallic);
            mat.SetFloat("_Smoothness", e.smoothness);
            var tiling = e.tiling != null && e.tiling.Length >= 2 ? new Vector2(e.tiling[0], e.tiling[1]) : Vector2.one;

            if (!string.IsNullOrEmpty(e.baseColor))
            {
                var tex = Texture($"{folder}/{e.baseColor}", normal: false, srgb: true);
                mat.SetTexture("_BaseMap", tex);
                mat.SetTextureScale("_BaseMap", tiling);
            }
            if (!string.IsNullOrEmpty(e.normal))
            {
                mat.SetTexture("_BumpMap", Texture($"{folder}/{e.normal}", normal: true, srgb: false));
                mat.EnableKeyword("_NORMALMAP");
            }
            if (!string.IsNullOrEmpty(e.metallicMap))
            {
                // URP lit le métal dans R ; la brillance (alpha) reste la valeur fixe ci-dessus.
                mat.SetTexture("_MetallicGlossMap", Texture($"{folder}/{e.metallicMap}", normal: false, srgb: false));
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_Metallic", 1f);
            }
            if (!string.IsNullOrEmpty(e.roughness)) Texture($"{folder}/{e.roughness}", normal: false, srgb: false); // gardée pour plus tard
            SetTransparent(mat, e.transparent);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Verre : URP Lit transparent, teinté, très brillant, reflets conservés.</summary>
        public static void SetTransparent(Material mat, bool on)
        {
            if (!on)
            {
                mat.SetFloat("_Surface", 0f); mat.SetFloat("_ZWrite", 1f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = -1; mat.SetOverrideTag("RenderType", "Opaque");
                return;
            }
            // Shader de verre du projet (Fresnel, reflets, éclats de lumière) ; URP Lit transparent en secours.
            var glass = Shader.Find("Fragile/Verre");
            if (glass != null)
            {
                var tint = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                var tex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : null;
                var scale = mat.HasProperty("_BaseMap") ? mat.GetTextureScale("_BaseMap") : Vector2.one;
                mat.shader = glass;
                mat.SetColor("_BaseColor", new Color(tint.r, tint.g, tint.b, 0.12f));
                mat.SetTexture("_BaseMap", tex); mat.SetTextureScale("_BaseMap", scale);
                mat.shaderKeywords = new string[0];
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                return;
            }
            var c = mat.color; c.a = 0.35f; mat.color = c;
            mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_BlendModePreserveSpecular", 1f); // les reflets restent visibles sur le verre
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_BLENDMODE_PRESERVE_SPECULAR_LIGHTING");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.SetOverrideTag("RenderType", "Transparent");
        }

        static Texture2D Texture(string path, bool normal, bool srgb)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (!normal) ti.sRGBTexture = srgb;
                ti.maxTextureSize = 2048;
                ti.anisoLevel = 4;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static string Normalize(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        static string SafeName(string s) { var r = new string(s.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_').ToArray()).Trim('_'); return r.Length > 0 ? r : "Materiau"; }
    }
}
