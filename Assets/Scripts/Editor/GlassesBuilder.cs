using System.Collections.Generic;
using Fragile.Breakables;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Fragile > Poser les verres : verres à boire générés (solides de révolution) — pinte, verre à whisky, verre à vin,
    /// coupe à cocktail, shooter — cassables et attrapables comme les bouteilles, vides ou servis (liquide Fragile/Liquide).
    /// Ils sont posés là où un bar les mettrait : devant chaque tabouret du comptoir, une rangée de shooters, des pintes
    /// propres retournées près de la tireuse, des verres servis sur les tables. Chaque place est vérifiée (surface sous
    /// le verre, rien qui le chevauche) et décalée de quelques centimètres si besoin.
    /// </summary>
    public static class GlassesBuilder
    {
        const string Folder = "Assets/Bar/Models/Verres";
        const string RootName = "Verres";
        const int Segments = 24;

        class Type
        {
            public string name; public Vector2[] section; public float mass;
            public int liquidFrom;      // index du premier point de la paroi intérieure dans la coupe
            public string liquid; public float fill;
        }

        // Coupes (rayon, hauteur) en mètres : du centre du pied vers l'extérieur, la paroi extérieure, le bord, puis la
        // paroi intérieure jusqu'à l'axe. Le liquide suit la paroi intérieure.
        static readonly Type[] Types =
        {
            new Type { name = "Pinte", mass = 0.45f, liquid = "Biere", fill = 0.88f, liquidFrom = 5, section = new[] {
                new Vector2(0f, 0f), new Vector2(0.029f, 0f), new Vector2(0.030f, 0.004f), new Vector2(0.042f, 0.150f),
                new Vector2(0.0395f, 0.150f), new Vector2(0.0275f, 0.009f), new Vector2(0f, 0.009f) } },
            new Type { name = "Whisky", mass = 0.32f, liquid = "Whisky", fill = 0.35f, liquidFrom = 5, section = new[] {
                new Vector2(0f, 0f), new Vector2(0.039f, 0f), new Vector2(0.040f, 0.004f), new Vector2(0.041f, 0.088f),
                new Vector2(0.0385f, 0.088f), new Vector2(0.0365f, 0.017f), new Vector2(0f, 0.017f) } },
            new Type { name = "Shot", mass = 0.09f, liquid = "Shot", fill = 0.8f, liquidFrom = 5, section = new[] {
                new Vector2(0f, 0f), new Vector2(0.020f, 0f), new Vector2(0.021f, 0.003f), new Vector2(0.024f, 0.060f),
                new Vector2(0.0215f, 0.060f), new Vector2(0.017f, 0.015f), new Vector2(0f, 0.015f) } },
            new Type { name = "Vin", mass = 0.18f, liquid = "Vin", fill = 0.45f, liquidFrom = 13, section = new[] {
                new Vector2(0f, 0f), new Vector2(0.034f, 0f), new Vector2(0.034f, 0.002f), new Vector2(0.008f, 0.006f),
                new Vector2(0.0045f, 0.012f), new Vector2(0.0045f, 0.085f), new Vector2(0.012f, 0.092f), new Vector2(0.030f, 0.105f),
                new Vector2(0.040f, 0.125f), new Vector2(0.042f, 0.145f), new Vector2(0.039f, 0.175f), new Vector2(0.036f, 0.195f),
                new Vector2(0.0345f, 0.195f), new Vector2(0.0375f, 0.175f), new Vector2(0.0405f, 0.145f), new Vector2(0.0385f, 0.126f),
                new Vector2(0.028f, 0.108f), new Vector2(0.010f, 0.097f), new Vector2(0f, 0.096f) } },
            new Type { name = "Cocktail", mass = 0.17f, liquid = "Cocktail", fill = 0.7f, liquidFrom = 9, section = new[] {
                new Vector2(0f, 0f), new Vector2(0.035f, 0f), new Vector2(0.035f, 0.002f), new Vector2(0.008f, 0.006f),
                new Vector2(0.004f, 0.012f), new Vector2(0.004f, 0.085f), new Vector2(0.008f, 0.089f), new Vector2(0.058f, 0.150f),
                new Vector2(0.0555f, 0.150f), new Vector2(0.006f, 0.0915f), new Vector2(0f, 0.091f) } },
        };

        struct Spot
        {
            public string type; public bool full, upsideDown; public Vector3 pos; public float maxY;
            public Spot(string type, bool full, float x, float z, float maxY = 2f, bool upsideDown = false)
            { this.type = type; this.full = full; pos = new Vector3(x, 0f, z); this.maxY = maxY; this.upsideDown = upsideDown; }
        }

        /// <summary>Places voulues (x, z) ; la hauteur vient de la surface trouvée dessous (maxY : la plus haute acceptée).</summary>
        static List<Spot> Layout()
        {
            var l = new List<Spot>();
            // Comptoir (plateau à 1,15 m) : un verre servi devant chaque tabouret, côté client.
            string[] north = { "Pinte", "Cocktail", "Pinte", "Whisky", "Vin" };
            float[] nx = { -2.2f, -1.1f, 0f, 1.1f, 2.2f };
            for (int i = 0; i < nx.Length; i++) l.Add(new Spot(north[i], true, nx[i] + 0.12f, 2.80f));
            l.Add(new Spot("Pinte", true, -2.68f, 1.6f));
            l.Add(new Spot("Shot", true, -2.70f, 0.47f)); l.Add(new Spot("Shot", true, -2.66f, 0.36f));
            l.Add(new Spot("Whisky", true, -2.68f, -0.8f));
            l.Add(new Spot("Vin", true, 2.68f, 1.45f));
            l.Add(new Spot("Pinte", false, 2.68f, -0.45f));
            // Tournée de shooters alignés au bout sud-ouest du comptoir.
            for (int i = 0; i < 5; i++) l.Add(new Spot("Shot", true, -2.35f + i * 0.075f, -2.80f));
            // Derrière le bar (plan de travail bas, 0,82 m) : pintes propres retournées près de la tireuse,
            // verres à vin et à whisky retournés de l'autre côté.
            for (int r = 0; r < 2; r++)
                for (int i = 0; i < 4; i++) l.Add(new Spot("Pinte", false, 2.22f + r * 0.1f, -0.2f + i * 0.1f, 1f, true));
            for (int i = 0; i < 4; i++) l.Add(new Spot("Vin", false, -2.25f, -1.2f + i * 0.1f, 1f, true));
            for (int i = 0; i < 3; i++) l.Add(new Spot("Whisky", false, -2.15f, -1.15f + i * 0.1f, 1f, true));
            // Coin repas : un verre par fauteuil, la table entière boit la même chose (ou presque).
            l.Add(new Spot("Vin", true, -3.30f, 5.72f, 1f)); l.Add(new Spot("Vin", true, -2.70f, 5.66f, 1f));
            l.Add(new Spot("Pinte", true, -0.65f, 7.40f, 1f)); l.Add(new Spot("Pinte", true, -0.05f, 7.40f, 1f));
            l.Add(new Spot("Cocktail", true, 1.90f, 5.62f, 1f)); l.Add(new Spot("Whisky", true, 2.50f, 5.58f, 1f));
            // Salon : deux cocktails sur la table basse. Tonneau mange-debout : deux pintes. Guéridon : un whisky.
            l.Add(new Spot("Cocktail", true, 3.62f, 7.40f, 1f)); l.Add(new Spot("Cocktail", true, 3.88f, 7.50f, 1f));
            l.Add(new Spot("Pinte", true, 3.45f, -3.40f, 1.2f)); l.Add(new Spot("Pinte", true, 3.66f, -3.52f, 1.2f));
            l.Add(new Spot("Whisky", true, 4.62f, -1.80f, 1f));
            return l;
        }

        [MenuItem("Fragile/Poser les verres")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Bar/Models", "Verres");
            var glass = GlassMaterial();
            var prefabs = new Dictionary<string, GameObject>();
            var heights = new Dictionary<string, float>();
            foreach (var t in Types)
            {
                prefabs[t.name] = BuildPrefab(t, glass, false);
                prefabs[t.name + "+"] = BuildPrefab(t, glass, true);
                heights[t.name] = t.section[t.liquidFrom - 1].y;
            }

            var old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            Physics.SyncTransforms();
            var root = new GameObject(RootName).transform;
            int placed = 0; var skipped = new List<string>();
            foreach (var s in Layout())
            {
                var prefab = prefabs[s.type + (s.full ? "+" : "")];
                float radius = RadiusOf(s.type) + 0.006f;
                if (!FindPlace(s, radius, heights[s.type], out var pos)) { skipped.Add($"{s.type} ({s.pos.x:F2}, {s.pos.z:F2})"); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                float yaw = Random.Range(0f, 360f);
                if (s.upsideDown) go.transform.SetPositionAndRotation(pos + Vector3.up * (heights[s.type] + 0.001f), Quaternion.Euler(180f, yaw, 0f));
                else go.transform.SetPositionAndRotation(pos + Vector3.up * 0.001f, Quaternion.Euler(0f, yaw, 0f));
                Physics.SyncTransforms();   // le verre suivant le voit
                placed++;
            }
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Poser les verres");
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Fragile] {placed} verres posés." + (skipped.Count > 0 ? " Pas de place pour : " + string.Join(", ", skipped) : ""));
        }

        static float RadiusOf(string type)
        {
            foreach (var t in Types) if (t.name == type) { float r = 0f; foreach (var p in t.section) r = Mathf.Max(r, p.x); return r; }
            return 0.04f;
        }

        /// <summary>Surface plane sous la place voulue (ou à quelques centimètres), sans rien qui chevauche le verre.</summary>
        static bool FindPlace(Spot s, float radius, float height, out Vector3 pos)
        {
            pos = default;
            for (int ring = 0; ring <= 4; ring++)
            {
                int count = ring == 0 ? 1 : 8;
                for (int k = 0; k < count; k++)
                {
                    float a = k * Mathf.PI * 2f / count;
                    var p = s.pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (ring * 0.025f);
                    if (!Physics.Raycast(new Vector3(p.x, s.maxY + 0.05f, p.z), Vector3.down, out var hit, s.maxY + 0.1f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (hit.normal.y < 0.95f || hit.point.y < 0.2f) continue;                       // pas le sol, pas une pente
                    if (hit.rigidbody != null && !hit.rigidbody.isKinematic) continue;               // pas sur une bouteille
                    var center = hit.point + Vector3.up * (height * 0.5f + 0.004f);
                    if (Physics.CheckBox(center, new Vector3(radius, height * 0.5f, radius), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                    // Le pied entier repose sur la surface (pas au bord du plateau).
                    bool supported = true;
                    for (int e = 0; e < 4 && supported; e++)
                    {
                        float b = e * Mathf.PI * 0.5f;
                        var edge = hit.point + new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * radius + Vector3.up * 0.02f;
                        supported = Physics.Raycast(edge, Vector3.down, out var h2, 0.05f, ~0, QueryTriggerInteraction.Ignore) && Mathf.Abs(h2.point.y - hit.point.y) < 0.005f;
                    }
                    if (!supported) continue;
                    pos = hit.point;
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ préfabs

        static GameObject BuildPrefab(Type t, Material glass, bool full)
        {
            string dir = $"{Folder}/{t.name}";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Folder, t.name);
            var mesh = SaveMesh(Lathe(t.section, 0, t.section.Length - 1, 0f), $"{dir}/Verre_{t.name}_Maillage.asset");

            var go = new GameObject("Verre_" + t.name + (full ? "_Servi" : ""));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = glass;
            var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = true;

            Color liquidColor = default;
            if (full)
            {
                var (body, surface) = LiquidColors(t.liquid);
                liquidColor = body;
                // Liquide : paroi intérieure légèrement rentrée, du fond jusqu'au bord ; LiquidFill le coupe au niveau.
                var inner = new List<Vector2>();
                for (int i = t.section.Length - 1; i >= t.liquidFrom - 1; i--) inner.Add(t.section[i]);
                var lmesh = SaveMesh(Lathe(inner.ToArray(), 0, inner.Count - 1, 0.0008f), $"{dir}/Verre_{t.name}_Liquide.asset");
                var liq = new GameObject("Liquide");
                liq.transform.SetParent(go.transform, false);
                liq.AddComponent<MeshFilter>().sharedMesh = lmesh;
                var lr = liq.AddComponent<MeshRenderer>();
                lr.sharedMaterial = LiquidMaterial(t.liquid, body, surface);
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                var fill = liq.AddComponent<LiquidFill>();
                var fso = new SerializedObject(fill);
                fso.FindProperty("m_Fill").floatValue = t.fill;
                fso.FindProperty("m_FillVariation").floatValue = 0.08f;
                fso.ApplyModifiedPropertiesWithoutUndo();
            }

            BreakableBuilder.MakeBreakablePublic(go, glass);
            var br = go.GetComponent<Breakable>();
            var so = new SerializedObject(br);
            so.FindProperty("m_Mass").floatValue = t.mass;
            so.FindProperty("m_Liquid").boolValue = full;
            if (full) so.FindProperty("m_LiquidColor").colorValue = liquidColor;
            so.FindProperty("m_ChipColor").colorValue = new Color(0.92f, 0.95f, 0.97f);
            so.FindProperty("m_ChipColor2").colorValue = new Color(0.75f, 0.82f, 0.86f);
            so.FindProperty("m_PiecesLeftOnGround").intValue = 4;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.GetComponent<Rigidbody>().mass = t.mass;
            GrabbableBuilder.MakeGrabbable(go);
            var grip = new SerializedObject(go.GetComponent<Fragile.Grab.GripSurface>());
            grip.FindProperty("m_EndMargin").floatValue = 0.01f;   // petits objets : presque toute la hauteur est saisissable
            grip.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{dir}/{go.name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        static (Color body, Color surface) LiquidColors(string liquid) => liquid switch
        {
            "Biere" => (new Color(0.78f, 0.42f, 0.04f, 0.92f), new Color(0.97f, 0.88f, 0.62f, 0.92f)),   // ambrée, mousse crème en surface
            "Vin" => (new Color(0.32f, 0.02f, 0.07f, 0.9f), new Color(0.45f, 0.06f, 0.1f, 0.92f)),
            "Whisky" => (new Color(0.75f, 0.42f, 0.1f, 0.8f), new Color(0.92f, 0.62f, 0.28f, 0.85f)),
            "Cocktail" => (new Color(0.95f, 0.35f, 0.45f, 0.7f), new Color(1f, 0.62f, 0.68f, 0.85f)),
            _ => (new Color(0.55f, 0.75f, 0.35f, 0.6f), new Color(0.75f, 0.9f, 0.55f, 0.8f)),            // shooter vert
        };

        static Material LiquidMaterial(string name, Color body, Color surface)
        {
            string path = $"Assets/Bar/FX/Liquides/Liquide_Verre_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Fragile/Liquide")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_Color", body);
            mat.SetColor("_SurfaceColor", surface);
            mat.renderQueue = 2500;   // avant le verre transparent : visible à travers la réfraction
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Verre clair : copie du verre des bouteilles d'eau (shader Fragile/Verre).</summary>
        static Material GlassMaterial()
        {
            string path = $"{Folder}/Verre_Clair.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            var src = AssetDatabase.LoadAssetAtPath<Material>("Assets/Bar/Models/Bouteilles/Bouteille_Eau/Materials/bottle_glass_mat.mat");
            mat = src != null ? new Material(src) : new Material(Shader.Find("Fragile/Verre"));
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ------------------------------------------------------------------ maillages

        /// <summary>
        /// Solide de révolution autour de Y d'une coupe (rayon, hauteur), points from..to. Chaque segment a ses propres
        /// sommets (arêtes nettes au bord et au pied) ; la normale pointe à gauche du sens de parcours (vers l'extérieur
        /// si la coupe va du pied au bord par l'extérieur). inset : décalage vers l'intérieur de la coupe (liquide).
        /// </summary>
        static Mesh Lathe(Vector2[] sec, int from, int to, float inset)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            float along = 0f;
            for (int i = from; i < to; i++)
            {
                var a = sec[i]; var b = sec[i + 1];
                var d = (b - a); float len = d.magnitude; if (len < 1e-6f) continue;
                var nn = new Vector2(d.y, -d.x) / len;                     // normale de la coupe (r, y)
                a -= nn * inset; b -= nn * inset;
                a.x = Mathf.Max(0f, a.x); b.x = Mathf.Max(0f, b.x);
                int start = v.Count;
                for (int s = 0; s <= Segments; s++)
                {
                    float ang = s * Mathf.PI * 2f / Segments;
                    var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    var normal = (dir * nn.x + Vector3.up * nn.y).normalized;
                    v.Add(dir * a.x + Vector3.up * a.y); n.Add(normal); uv.Add(new Vector2(s / (float)Segments, along));
                    v.Add(dir * b.x + Vector3.up * b.y); n.Add(normal); uv.Add(new Vector2(s / (float)Segments, along + len));
                }
                along += len;
                for (int s = 0; s < Segments; s++)
                {
                    int i0 = start + s * 2, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
                    AddTri(v, n, tri, i0, i2, i1); AddTri(v, n, tri, i1, i2, i3);
                }
            }
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Triangle tourné vers la normale de ses sommets (sens de rotation vérifié, pas supposé).</summary>
        static void AddTri(List<Vector3> v, List<Vector3> n, List<int> tri, int a, int b, int c)
        {
            var face = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (face.sqrMagnitude < 1e-14f) return;                        // triangle dégénéré (sur l'axe)
            if (Vector3.Dot(face, n[a] + n[b] + n[c]) < 0f) { tri.Add(a); tri.Add(c); tri.Add(b); }
            else { tri.Add(a); tri.Add(b); tri.Add(c); }
        }

        static Mesh SaveMesh(Mesh m, string path)
        {
            m.name = System.IO.Path.GetFileNameWithoutExtension(path);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(m, path); return m; }
            existing.Clear();
            existing.SetVertices(m.vertices); existing.SetNormals(m.normals); existing.SetUVs(0, m.uv);
            existing.SetTriangles(m.triangles, 0);
            existing.RecalculateTangents(); existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
