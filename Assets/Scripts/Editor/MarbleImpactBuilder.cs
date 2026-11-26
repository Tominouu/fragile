using System.Collections.Generic;
using System.IO;
using Fragile.Breakables;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Fragile > Rendre le marbre marquable : les coups de batte sur le comptoir y laissent des impacts.
    /// Génère la texture des impacts (4 variantes : cratère clair de pierre écrasée, rebord sombre, fissures en étoile ;
    /// couleur + alpha),
    /// le matériau (shader Fragile/Impact) et ajoute <see cref="ImpactMarks"/> au comptoir.
    /// </summary>
    public static class MarbleImpactBuilder
    {
        const string Folder = "Assets/Bar/FX/Impacts";
        const int Cell = 256;

        [MenuItem("Fragile/Rendre le marbre marquable")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            var counter = GameObject.Find("Comptoir");
            if (counter == null) { Debug.LogError("[Fragile] Comptoir introuvable dans la scène."); return; }
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Bar/FX", "Impacts");

            var tex = BuildTexture();
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{Folder}/Impact_Marbre.mat");
            if (mat == null) { mat = new Material(Shader.Find("Fragile/Impact")); AssetDatabase.CreateAsset(mat, $"{Folder}/Impact_Marbre.mat"); }
            mat.shader = Shader.Find("Fragile/Impact");
            mat.SetTexture("_BaseMap", tex);
            mat.SetFloat("_Strength", 1f);
            mat.SetFloat("_Brightness", 1.3f);
            EditorUtility.SetDirty(mat);

            // Composant neuf : les réglages par défaut du script s'appliquent (tailles, seuils).
            var old = counter.GetComponent<ImpactMarks>();
            if (old != null) Undo.DestroyObjectImmediate(old);
            var marks = Undo.AddComponent<ImpactMarks>(counter);
            marks.Setup(mat, AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/FX/BreakFX.prefab"));
            EditorUtility.SetDirty(marks);

            AssetDatabase.SaveAssets();
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Fragile] Marbre du comptoir marquable : les coups de batte y laissent des impacts.");
        }

        static Texture2D BuildTexture()
        {
            int size = Cell * 2;
            var px = new Color[size * size];
            var rng = new System.Random(2026);
            for (int cell = 0; cell < 4; cell++)
            {
                var c = Variant(rng);
                int ox = (cell % 2) * Cell, oy = (cell / 2) * Cell;
                for (int y = 0; y < Cell; y++)
                    for (int x = 0; x < Cell; x++)
                        px[(oy + y) * size + ox + x] = c(new Vector2((x + 0.5f) / Cell * 2f - 1f, (y + 0.5f) / Cell * 2f - 1f));
            }

            string path = $"{Folder}/Impacts_Marbre.png";
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.SetPixels(px); t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = true;
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Une variante d'impact : fonction (coordonnées -1..1) -> couleur (gris 0,5 = neutre) + alpha.</summary>
        static System.Func<Vector2, Color> Variant(System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            float radius = Mathf.Lerp(0.2f, 0.28f, R());
            var phases = new float[4]; var amps = new float[4];
            for (int i = 0; i < 4; i++) { phases[i] = R() * 6.283f; amps[i] = Mathf.Lerp(0.04f, 0.14f, R()) / (i + 1); }
            float Edge(float a) { float r = 1f; for (int i = 0; i < 4; i++) r += amps[i] * Mathf.Sin(a * (i + 2) + phases[i]); return radius * r; }

            // Fissures : lignes brisées partant du cratère, avec quelques branches.
            var segs = new List<(Vector2 a, Vector2 b, float w0, float w1)>();
            int rays = 5 + rng.Next(4);
            for (int k = 0; k < rays; k++)
            {
                float ang = (k + R() * 0.6f) / rays * 6.283f;
                float len = Mathf.Lerp(0.4f, 0.92f, R());
                var p = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Edge(ang) * 0.9f;
                int steps = 6;
                for (int s = 0; s < steps; s++)
                {
                    ang += (R() - 0.5f) * 0.6f;
                    var q = p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (len / steps);
                    float w0 = Mathf.Lerp(0.04f, 0.012f, s / (float)steps), w1 = Mathf.Lerp(0.04f, 0.012f, (s + 1f) / steps);
                    segs.Add((p, q, w0, w1));
                    if (s == 2 && R() < 0.6f)
                    {
                        float ba = ang + (R() < 0.5f ? 1f : -1f) * Mathf.Lerp(0.4f, 0.9f, R());
                        var b0 = q;
                        for (int j = 0; j < 3; j++)
                        {
                            ba += (R() - 0.5f) * 0.5f;
                            var b1 = b0 + new Vector2(Mathf.Cos(ba), Mathf.Sin(ba)) * len * 0.09f;
                            segs.Add((b0, b1, Mathf.Lerp(0.02f, 0.008f, j / 3f), Mathf.Lerp(0.02f, 0.008f, (j + 1) / 3f)));
                            b0 = b1;
                        }
                    }
                    p = q;
                }
            }
            int seed = rng.Next();
            var crushed = new Color(0.9f, 0.87f, 0.8f);

            return uv =>
            {
                float d = uv.magnitude;
                float ang = Mathf.Atan2(uv.y, uv.x);
                float edge = Edge(ang);
                // Composition « par-dessus » en couleurs prémultipliées.
                float a = 0f; Color pre = Color.clear;
                void Over(Color c, float alpha) { alpha = Mathf.Clamp01(alpha); pre = c * alpha + pre * (1f - alpha); a = alpha + a * (1f - alpha); }

                // Halo : la pierre est un peu meurtrie autour du choc.
                if (d < edge * 2.3f) Over(new Color(0.1f, 0.08f, 0.07f), 0.35f * Mathf.Clamp01(1f - (d - edge) / (edge * 1.3f)));
                // Fissures.
                float crack = 0f;
                foreach (var s in segs)
                {
                    var ab = s.b - s.a; float t = Mathf.Clamp01(Vector2.Dot(uv - s.a, ab) / ab.sqrMagnitude);
                    float dist = (uv - (s.a + ab * t)).magnitude, w = Mathf.Lerp(s.w0, s.w1, t);
                    crack = Mathf.Max(crack, Mathf.Clamp01(1f - dist / w));
                }
                // Fissure : lèvres blanchâtres (marbre écrasé, lisible même sur la pierre sombre), cœur sombre.
                if (crack > 0f) Over(crushed * 0.85f, 0.85f * Mathf.SmoothStep(0f, 1f, crack * 1.6f));
                if (crack > 0.55f) Over(new Color(0.05f, 0.045f, 0.04f), Mathf.SmoothStep(0f, 1f, (crack - 0.55f) / 0.45f));
                // Cratère : pierre écrasée, claire et granuleuse ; rebord sombre.
                if (d < edge)
                {
                    float grain = Hash(uv * 90f, seed);
                    var c = crushed * Mathf.Lerp(0.8f, 1.15f, grain);
                    if (grain > 0.93f) c = new Color(0.22f, 0.2f, 0.18f);             // petits trous
                    Over(c, 0.97f * Mathf.Clamp01((edge - d) / 0.03f));
                }
                float rim = Mathf.Clamp01(1f - Mathf.Abs(d - edge) / 0.03f);
                if (rim > 0f) Over(new Color(0.08f, 0.07f, 0.06f), rim * 0.85f);
                // Bord de la case : rien.
                a *= Mathf.Clamp01((0.98f - d) / 0.08f);
                var col = a > 1e-4f ? new Color(pre.r, pre.g, pre.b) / Mathf.Max(a, 1e-4f) : new Color(0.5f, 0.5f, 0.5f);
                col.a = a;
                return col;
            };
        }

        static float Hash(Vector2 p, int seed)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }
    }
}
