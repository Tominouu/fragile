using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Fragile > Ajouter les télés au-dessus du comptoir :
    /// bloc de 4 télés dos à dos suspendu au plafond, au-dessus du centre du comptoir, chacune tournée vers un côté.
    /// Écrans allumés (image de match générée, émissive : aucune lumière en plus).
    /// </summary>
    public static class TvClusterBuilder
    {
        const string Folder = "Assets/Bar/TV";
        const float ScreenW = 1.10f, ScreenH = 0.62f;   // ~50 pouces
        const float Bezel = 0.018f, Depth = 0.055f;
        const float CenterY = 2.27f;                     // bas de l'écran vers 1,95 m
        const float Offset = 0.60f;                      // distance du centre du bloc à chaque écran

        [MenuItem("Fragile/Ajouter les télés au-dessus du comptoir")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Bar", "TV");

            var counter = GameObject.Find("Comptoir");
            var ceiling = GameObject.Find("Ceiling");
            Vector3 center = Vector3.zero; float ceilingY = 3f;
            if (counter != null)
            {
                var rs = counter.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                center = new Vector3(b.center.x, 0f, b.center.z);
            }
            if (ceiling != null) ceilingY = ceiling.GetComponent<Renderer>().bounds.min.y;

            var plastic = Mat("TV_Plastique", new Color(0.04f, 0.04f, 0.045f), 0f, 0.55f);
            var metal = Mat("TV_Metal", new Color(0.12f, 0.12f, 0.13f), 1f, 0.6f);
            var screen = ScreenMaterial();

            var old = GameObject.Find("Télés");
            if (old != null) Undo.DestroyObjectImmediate(old);
            var root = new GameObject("Télés").transform;
            var room = GameObject.Find("Room");
            if (room != null) root.SetParent(room.transform, true);
            root.position = new Vector3(center.x, 0f, center.z);

            // Fixation : mât depuis le plafond + cadre carré.
            float frameY = CenterY + ScreenH / 2f + 0.06f;
            Part(root, "Platine plafond", PrimitiveType.Cylinder, new Vector3(0f, ceilingY - 0.01f, 0f), new Vector3(0.22f, 0.01f, 0.22f), metal, collider: false);
            float poleLen = ceilingY - frameY;
            Part(root, "Mât", PrimitiveType.Cylinder, new Vector3(0f, frameY + poleLen / 2f, 0f), new Vector3(0.06f, poleLen / 2f, 0.06f), metal, collider: false);
            float frame = Offset - Depth - 0.02f;
            Part(root, "Cadre", PrimitiveType.Cube, new Vector3(0f, frameY, 0f), new Vector3(frame * 2f, 0.04f, frame * 2f), metal, collider: false);

            // 4 télés, dos à dos.
            for (int i = 0; i < 4; i++)
            {
                var face = Quaternion.Euler(0f, i * 90f, 0f);
                var tv = new GameObject($"Télé {i + 1}").transform;
                tv.SetParent(root, false);
                tv.localRotation = face;
                tv.localPosition = face * new Vector3(0f, CenterY, Offset);
                // L'écran regarde vers l'extérieur (+Z local de la télé).
                Part(tv, "Boîtier", PrimitiveType.Cube, new Vector3(0f, 0f, -Depth / 2f), new Vector3(ScreenW + Bezel * 2f, ScreenH + Bezel * 2f, Depth), plastic, collider: true);
                var s = Part(tv, "Écran", PrimitiveType.Quad, new Vector3(0f, 0f, 0.006f), new Vector3(ScreenW, ScreenH, 1f), screen, collider: false);
                s.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // le quad regarde vers -Z : on le retourne vers l'extérieur
                Part(tv, "Support", PrimitiveType.Cube, new Vector3(0f, ScreenH / 2f + 0.03f, -Depth - 0.01f), new Vector3(0.18f, 0.08f, 0.03f), metal, collider: false);
            }

            Undo.RegisterCreatedObjectUndo(root.gameObject, "Ajouter les télés");
            Selection.activeGameObject = root.gameObject;
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Fragile] 4 télés suspendues au-dessus du comptoir (centre {center.x:F2}, {center.z:F2}), bas des écrans à {CenterY - ScreenH / 2f:F2} m.");
        }

        static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 localPos, Vector3 scale, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            var col = go.GetComponent<Collider>();
            if (!collider) Object.DestroyImmediate(col);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
            return go;
        }

        static Material Mat(string name, Color c, float metallic, float smoothness)
        {
            string path = $"{Folder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.color = c; m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material ScreenMaterial()
        {
            string texPath = $"{Folder}/Ecran_Match.png";
            File.WriteAllBytes(texPath, MatchImage.Render(1024, 576).EncodeToPNG());
            AssetDatabase.ImportAsset(texPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
            ti.wrapMode = TextureWrapMode.Clamp; ti.anisoLevel = 4; ti.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            // Écran allumé = image affichée à pleine luminosité, quelle que soit la lumière de la pièce : matériau non éclairé
            // (le plus juste pour un écran, et le moins coûteux).
            string path = $"{Folder}/TV_Ecran.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (m == null) { m = new Material(unlit); AssetDatabase.CreateAsset(m, path); }
            m.shader = unlit;
            m.shaderKeywords = new string[0];
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.92f, 1f));
            EditorUtility.SetDirty(m);
            return m;
        }
    }

    /// <summary>Image de match de foot télévisé (vue caméra, terrain en perspective, joueurs, score), dessinée par le code.</summary>
    static class MatchImage
    {
        public static Texture2D Render(int w, int h)
        {
            var px = new Color32[w * h];
            var rng = new System.Random(5);
            int horizon = (int)(h * 0.72f);

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color c;
                    if (y >= horizon)
                    {
                        // Tribunes : foule (points colorés) sous un toit sombre.
                        float t = (y - horizon) / (float)(h - horizon);
                        var crowd = new Color(0.15f + (float)rng.NextDouble() * 0.5f, 0.12f + (float)rng.NextDouble() * 0.3f, 0.15f + (float)rng.NextDouble() * 0.4f);
                        c = Color.Lerp(crowd * 0.8f, new Color(0.05f, 0.05f, 0.07f), Mathf.SmoothStep(0.55f, 0.9f, t));
                    }
                    else
                    {
                        // Pelouse en perspective : bandes de tonte qui se resserrent vers le fond.
                        float depth = y / (float)horizon;                 // 0 = devant, 1 = fond
                        float persp = 1f / (1.2f - depth);
                        bool stripe = (int)(persp * 6f) % 2 == 0;
                        c = stripe ? new Color(0.16f, 0.52f, 0.18f) : new Color(0.13f, 0.45f, 0.15f);
                        c *= Mathf.Lerp(1f, 0.75f, depth);
                        // Lignes : bord du terrain, ligne médiane, rond central.
                        float halfWidth = Mathf.Lerp(w * 0.62f, w * 0.30f, depth);
                        float dx = Mathf.Abs(x - w / 2f);
                        if (Mathf.Abs(dx - halfWidth) < Mathf.Lerp(3f, 1f, depth) || Mathf.Abs(y - horizon * 0.42f) < 2f && dx < halfWidth)
                            c = new Color(0.92f, 0.95f, 0.92f);
                        float ex = (x - w / 2f) / 150f, ey = (y - horizon * 0.42f) / 55f;
                        if (Mathf.Abs(Mathf.Sqrt(ex * ex + ey * ey) - 1f) < 0.025f) c = new Color(0.92f, 0.95f, 0.92f);
                    }
                    px[y * w + x] = c;
                }

            // Joueurs : deux équipes (rouge / bleu clair), plus petits au fond.
            for (int k = 0; k < 16; k++)
            {
                float depth = 0.15f + (float)rng.NextDouble() * 0.75f;
                int py = (int)(depth * horizon);
                float half = Mathf.Lerp(w * 0.55f, w * 0.27f, depth);
                int pxx = (int)(w / 2f + ((float)rng.NextDouble() * 2f - 1f) * half);
                int size = (int)Mathf.Lerp(26f, 9f, depth);
                var shirt = k % 2 == 0 ? new Color32(200, 30, 35, 255) : new Color32(120, 190, 240, 255);
                Rect(px, w, h, pxx - size / 4, py, size / 2, (int)(size * 0.55f), new Color32(20, 20, 20, 255));     // jambes
                Rect(px, w, h, pxx - size / 3, py + (int)(size * 0.55f), (int)(size * 0.66f), (int)(size * 0.7f), shirt); // maillot
                Rect(px, w, h, pxx - size / 6, py + (int)(size * 1.25f), size / 3, size / 3, new Color32(225, 180, 150, 255)); // tête
            }
            // Ballon.
            Rect(px, w, h, w / 2 + 70, (int)(horizon * 0.3f), 8, 8, new Color32(250, 250, 250, 255));

            // Bandeau de score en haut à gauche : "FRG 2-1 BAR  67'".
            int bx = 30, by = h - 70, bh = 40;
            Rect(px, w, h, bx, by, 360, bh, new Color32(15, 18, 30, 235));
            Rect(px, w, h, bx, by, 8, bh, new Color32(200, 30, 35, 255));
            Text(px, w, h, "FRG 2-1 BAR", bx + 18, by + 9, 4, new Color32(245, 245, 245, 255));
            Rect(px, w, h, bx + 365, by, 95, bh, new Color32(240, 200, 40, 255));
            Text(px, w, h, "67'", bx + 380, by + 9, 4, new Color32(20, 20, 20, 255));
            // Logo de chaîne en haut à droite.
            Rect(px, w, h, w - 120, h - 62, 90, 32, new Color32(255, 255, 255, 200));
            Text(px, w, h, "FRG", w - 108, h - 55, 3, new Color32(200, 30, 35, 255));

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        static void Rect(Color32[] px, int w, int h, int x0, int y0, int rw, int rh, Color32 c)
        {
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(h, y0 + rh); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(w, x0 + rw); x++)
                {
                    if (c.a == 255) { px[y * w + x] = c; continue; }
                    var d = px[y * w + x]; float a = c.a / 255f;
                    px[y * w + x] = new Color32((byte)(d.r + (c.r - d.r) * a), (byte)(d.g + (c.g - d.g) * a), (byte)(d.b + (c.b - d.b) * a), 255);
                }
        }

        // Police bitmap 5x7 (seulement les caractères utilisés).
        static readonly System.Collections.Generic.Dictionary<char, string[]> k_Font = new System.Collections.Generic.Dictionary<char, string[]>
        {
            ['F'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" },
            ['R'] = new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" },
            ['G'] = new[] { "01110", "10001", "10000", "10111", "10001", "10001", "01111" },
            ['B'] = new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" },
            ['A'] = new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
            ['1'] = new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
            ['2'] = new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" },
            ['6'] = new[] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" },
            ['7'] = new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
            ['-'] = new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" },
            ['\''] = new[] { "00100", "00100", "01000", "00000", "00000", "00000", "00000" },
            [' '] = new[] { "00000", "00000", "00000", "00000", "00000", "00000", "00000" },
        };

        static void Text(Color32[] px, int w, int h, string s, int x, int y, int scale, Color32 c)
        {
            foreach (var ch in s)
            {
                if (k_Font.TryGetValue(ch, out var g))
                    for (int row = 0; row < 7; row++)
                        for (int col = 0; col < 5; col++)
                            if (g[row][col] == '1') Rect(px, w, h, x + col * scale, y + (6 - row) * scale, scale, scale, c);
                x += 6 * scale;
            }
        }
    }
}
