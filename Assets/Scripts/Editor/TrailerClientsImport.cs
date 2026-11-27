using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Réglages d'import des clients du Trailer (Assets/Trailer/Clients, générés par Tools/Blender/generate_clients.py)
    /// et de leurs animations (Assets/Trailer/Animations, Quaternius Universal Animation Library, CC0) :
    /// rig Humanoid (les animations passent d'un squelette à l'autre), textures réduites pour le Quest,
    /// cheveux / sourcils / cils en découpe alpha.
    /// </summary>
    public class TrailerClientsImport : AssetPostprocessor
    {
        public const string ClientsFolder = "Assets/Trailer/Clients/";
        public const string AnimationsFolder = "Assets/Trailer/Animations/";

        static readonly string[] HairWords = { "hair", "eyebrow", "eyelash", "afro", "bob", "short0", "long0", "ponytail", "braid", "bun", "glasses" };
        static readonly string[] LoopWords = { "idle", "sit", "talk", "run", "jog", "sprint", "walk", "loop" };

        // Changer ce numéro réimporte les modèles et animations concernés (réglages ci-dessous modifiés).
        public override uint GetVersion() => 4;

        // Après le convertisseur FBX d'URP : nos matériaux remplacent entièrement les siens.
        public override int GetPostprocessOrder() => 1000;

        void OnPreprocessModel()
        {
            var importer = (ModelImporter)assetImporter;
            if (assetPath.StartsWith(ClientsFolder))
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = false;
                importer.importBlendShapes = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                importer.meshCompression = ModelImporterMeshCompression.Medium;
                importer.isReadable = false;
            }
            else if (assetPath.StartsWith(AnimationsFolder))
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importBlendShapes = false;
                importer.importCameras = false;
                importer.importLights = false;
            }
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(AnimationsFolder)) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                var n = c.name.ToLowerInvariant();
                bool loop = false;
                foreach (var w in LoopWords) if (n.Contains(w)) loop = true;
                if (n.Contains("enter") || n.Contains("exit") || n.Contains("stand_up") || n.Contains("standup") || n.Contains("death")) loop = false;
                c.loopTime = loop;
                c.loopPose = loop;
                // Le déplacement est fait par le script : l'animation reste sur place.
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                // Orientation calée sur le CORPS, pas sur la racine d'origine : le squelette Quaternius (convention Unreal)
                // est tourné de 180°, et les personnages se retrouvaient dos au comptoir.
                c.keepOriginalOrientation = false;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ClientsFolder)) return;
            var importer = (TextureImporter)assetImporter;
            var n = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            if (n.Contains("normal") || n.Contains("norm") || n.EndsWith("_nm"))
                importer.textureType = TextureImporterType.NormalMap;
            importer.maxTextureSize = 1024;
            importer.mipmapEnabled = true;
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 512;
            android.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(android);
        }

        /// <summary>
        /// Matériaux des clients construits ici, sans dépendre de la conversion automatique (qui pouvait laisser une opacité
        /// nulle : cheveux invisibles) : URP Lit, opaque, couleur blanche à 100 %, texture et normal map du FBX.
        /// Cheveux, sourcils, cils : découpe alpha double face. Yeux : brillants.
        /// </summary>
        void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
        {
            if (!assetPath.StartsWith(ClientsFolder)) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return;
            material.shader = shader;
            var n = description.materialName.ToLowerInvariant();
            bool hair = false;
            foreach (var w in HairWords) if (n.Contains(w)) hair = true;
            bool eye = n.Contains("low-poly") || n.Contains("high-poly") || n.EndsWith("eye") || n.Contains("_eye");

            Texture baseMap = null, normal = null;
            foreach (var key in new[] { "DiffuseColor", "Diffuse", "BaseColor", "base_color" })
                if (baseMap == null && description.TryGetProperty(key, out TexturePropertyDescription t) && t.texture != null) baseMap = t.texture;
            foreach (var key in new[] { "NormalMap", "Bump", "normal_map" })
                if (normal == null && description.TryGetProperty(key, out TexturePropertyDescription t) && t.texture != null) normal = t.texture;

            material.SetColor("_BaseColor", Color.white);
            if (baseMap != null) material.SetTexture("_BaseMap", baseMap);
            if (normal != null) { material.SetTexture("_BumpMap", normal); material.EnableKeyword("_NORMALMAP"); material.SetFloat("_BumpScale", 0.8f); }
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", eye ? 0.75f : hair ? 0.3f : 0.18f);

            // Opaque partout...
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", 1f);
            material.SetFloat("_DstBlend", 0f);
            material.SetFloat("_ZWrite", 1f);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetShaderPassEnabled("DepthOnly", true);
            material.SetShaderPassEnabled("ShadowCaster", true);
            material.SetOverrideTag("RenderType", hair ? "TransparentCutout" : "Opaque");
            // ... avec découpe alpha pour les poils.
            material.SetFloat("_AlphaClip", hair ? 1f : 0f);
            material.SetFloat("_Cutoff", 0.35f);
            if (hair) material.EnableKeyword("_ALPHATEST_ON"); else material.DisableKeyword("_ALPHATEST_ON");
            material.SetFloat("_Cull", hair ? 0f : 2f);
            material.renderQueue = hair ? (int)UnityEngine.Rendering.RenderQueue.AlphaTest : (int)UnityEngine.Rendering.RenderQueue.Geometry;
            if (baseMap == null) Debug.LogWarning($"[Fragile] {Path.GetFileName(assetPath)} : matériau « {description.materialName} » sans texture.");
        }
    }
}
