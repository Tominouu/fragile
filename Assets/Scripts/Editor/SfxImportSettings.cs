using UnityEditor;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Réglages d'import des bruitages (Assets/Bar/Resources/Sons) : mono (ils sont joués en 3D), Vorbis,
    /// décompressés au chargement pour les sons courts (aucun coût CPU au moment de jouer), compressés en mémoire
    /// pour les longs. Appliqués automatiquement à chaque nouveau fichier.
    /// </summary>
    class SfxImportSettings : AssetPostprocessor
    {
        const string Folder = "Assets/Bar/Resources/Sons/";

        public override uint GetVersion() => 2;

        /// <summary>Durée d'un WAV d'après son en-tête (avant que Unity ne l'ait importé).</summary>
        static float Seconds(string path)
        {
            try
            {
                using var f = System.IO.File.OpenRead(path);
                using var r = new System.IO.BinaryReader(f);
                if (new string(r.ReadChars(4)) != "RIFF") return 0f;
                r.ReadInt32(); r.ReadChars(4);
                int byteRate = 0;
                while (f.Position + 8 <= f.Length)
                {
                    string id = new string(r.ReadChars(4)); int size = r.ReadInt32();
                    if (id == "fmt ") { r.ReadInt16(); r.ReadInt16(); r.ReadInt32(); byteRate = r.ReadInt32(); f.Position += size - 12; }
                    else if (id == "data") return byteRate > 0 ? size / (float)byteRate : 0f;
                    else f.Position += size + (size & 1);
                }
            }
            catch (System.Exception) { }
            return 0f;
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(Folder)) return;
            var imp = (AudioImporter)assetImporter;
            imp.forceToMono = true;
            imp.loadInBackground = false;
            imp.ambisonic = false;

            var s = imp.defaultSampleSettings;
            // Sons longs (jet de bière, fissure, roulement) : gardés compressés, décodés à la lecture.
            s.loadType = Seconds(assetPath) > 3f ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;
            s.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            s.sampleRateOverride = 44100;
            s.preloadAudioData = true;
            imp.defaultSampleSettings = s;
        }

        [MenuItem("Fragile/Sons/Réappliquer les réglages d'import")]
        static void Reimport()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Folder.TrimEnd('/') }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            Debug.Log("[Fragile] Réglages d'import des sons réappliqués.");
        }
    }
}
