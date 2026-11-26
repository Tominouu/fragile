using Fragile.Mice;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Fragile > Ajouter les souris : préfab de souris (modèle Poly Haven « street_rat », CC0, allégé à ~3 500 triangles
    /// par Tools/Blender/import_polyhaven.py avec FRAGILE_DECIMATE), trous de souris dans les plinthes (4 murs),
    /// nid qui gère la population, et matériaux du sang (flaque, giclée, gouttelettes ; textures générées).
    /// Relançable : l'objet « Souris » de la scène est reconstruit.
    /// </summary>
    public static class MouseBuilder
    {
        const string Dir = "Assets/Bar/Souris";
        const string FxDir = Dir + "/FX";
        const string Model = Dir + "/Modele/street_rat";
        const float Scale = 1.2f;   // un peu plus grosse qu'en vrai : on la voit trottiner au sol

        // Trous : point sur la plinthe (lambris, 2 cm devant le mur) et direction vers la salle.
        static readonly (Vector3 wall, Vector3 inward)[] Holes =
        {
            (new Vector3(-4.978f, 0f, 5.9f), Vector3.right),     // mur ouest, vers le coin repas
            (new Vector3(4.978f, 0f, -2.9f), Vector3.left),      // mur est, près du guéridon
            (new Vector3(1.0f, 0f, 8.338f), Vector3.back),       // mur nord, sous les briques
            (new Vector3(0.0f, 0f, -4.978f), Vector3.forward),   // mur sud, entre l'étagère et les frigos
        };

        [MenuItem("Fragile/Ajouter les souris")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            Folder(FxDir);
            BlenderModelImporter.ImportFolder(Model);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Model}/street_rat.fbx");
            if (model == null) { Debug.LogError("[Fragile] Modèle absent : " + Model + " (Tools/Blender/import_polyhaven.py street_rat)."); return; }

            var pool = Blood("Sang_Flaque", Tex("Sang_Flaque", 256, PoolPixel), 0.94f);
            var splat = Blood("Sang_Giclee", Tex("Sang_Giclee", 256, SplatPixel), 0.9f);
            var drop = Drop(Tex("Goutte", 32, DropPixel));
            var holeMat = Hole(Tex("Trou", 128, HolePixel));
            var prefab = MousePrefab(model);

            var old = GameObject.Find("Souris");
            if (old != null) Undo.DestroyObjectImmediate(old);
            var root = new GameObject("Souris");
            var holes = new Transform[Holes.Length];
            var quad = QuadMesh();
            for (int i = 0; i < Holes.Length; i++)
            {
                var (wall, inward) = Holes[i];
                // Trou noir en arche sur la plinthe.
                var hole = new GameObject("Trou de souris");
                hole.transform.SetParent(root.transform, false);
                hole.transform.SetPositionAndRotation(wall + Vector3.up * 0.03f + inward * 0.001f, Quaternion.LookRotation(-inward));
                hole.transform.localScale = new Vector3(0.08f, 0.06f, 1f);
                hole.AddComponent<MeshFilter>().sharedMesh = quad;
                var r = hole.AddComponent<MeshRenderer>();
                r.sharedMaterial = holeMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // Point de sortie, juste devant.
                var spawn = new GameObject("Sortie").transform;
                spawn.SetParent(hole.transform, true);
                spawn.SetPositionAndRotation(wall + inward * 0.1f, Quaternion.LookRotation(inward));
                holes[i] = spawn;
            }

            var nest = root.AddComponent<MouseNest>();
            var so = new SerializedObject(nest);
            so.FindProperty("m_Prefab").objectReferenceValue = prefab;
            var hp = so.FindProperty("m_Holes"); hp.arraySize = holes.Length;
            for (int i = 0; i < holes.Length; i++) hp.GetArrayElementAtIndex(i).objectReferenceValue = holes[i];
            so.FindProperty("m_Bounds").vector4Value = new Vector4(-4.9f, 4.9f, -4.9f, 8.27f);
            so.FindProperty("m_PoolMaterial").objectReferenceValue = pool;
            so.FindProperty("m_SplatMaterial").objectReferenceValue = splat;
            so.FindProperty("m_DropMaterial").objectReferenceValue = drop;
            so.ApplyModifiedPropertiesWithoutUndo();

            Undo.RegisterCreatedObjectUndo(root, "Ajouter les souris");
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Fragile] Souris : {Holes.Length} trous, préfab {Dir}/Souris.prefab.");
        }

        static Mouse MousePrefab(GameObject model)
        {
            var root = new GameObject("Souris");
            var visual = new GameObject("Visuel").transform; visual.SetParent(root.transform, false);
            visual.localScale = Vector3.one * Scale;
            var m = (GameObject)PrefabUtility.InstantiatePrefab(model, visual);
            foreach (var r in m.GetComponentsInChildren<MeshRenderer>())
            {
                r.receiveGI = ReceiveGI.LightProbes;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            // Corps (sans la queue) : capsule le long de l'axe Z, museau vers +Z.
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 2; col.radius = 0.019f * Scale; col.height = 0.095f * Scale; col.center = new Vector3(0f, 0.018f, -0.004f) * Scale;
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.mass = 0.03f; rb.linearDamping = 0.3f; rb.angularDamping = 0.6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var mouse = root.AddComponent<Mouse>();
            var so = new SerializedObject(mouse);
            so.FindProperty("m_Visual").objectReferenceValue = visual;
            so.ApplyModifiedPropertiesWithoutUndo();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/Souris.prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<Mouse>();
        }

        // ------------------------------------------------------------------ matériaux

        /// <summary>Sang : rouge vif, transparent sur les bords, très brillant (mouillé).</summary>
        static Material Blood(string name, Texture2D tex, float smoothness)
        {
            var m = Mat(name, "Universal Render Pipeline/Lit");
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", new Color(0.62f, 0.015f, 0.02f, 1f));
            m.SetFloat("_Smoothness", smoothness); m.SetFloat("_Metallic", 0f);
            m.SetFloat("_SpecularHighlights", 1f); m.SetFloat("_EnvironmentReflections", 1f);
            Transparent(m, false);
            return m;
        }

        static Material Drop(Texture2D tex)
        {
            var m = Mat("Sang_Goutte", "Universal Render Pipeline/Particles/Unlit");
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            Transparent(m, false);
            return m;
        }

        static Material Hole(Texture2D tex)
        {
            var m = Mat("Trou", "Universal Render Pipeline/Unlit");
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            Transparent(m, false);
            return m;
        }

        static Material Mat(string name, string shader)
        {
            string path = $"{FxDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find(shader);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Transparent(Material m, bool additive)
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.SrcAlpha));
            m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            // Sans « conserver le spéculaire » : sinon URP passe en prémultiplié et le reflet mouillé dessine tout le carré.
            m.SetFloat("_BlendModePreserveSpecular", 0f);
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetShaderPassEnabled("ShadowCaster", false);
        }

        // ------------------------------------------------------------------ textures générées

        delegate Color Pixel(float u, float v, System.Random rng);

        static Texture2D Tex(string name, int size, Pixel pixel)
        {
            string path = $"{FxDir}/{name}.png";
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var rng = new System.Random(name.GetHashCode());
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, pixel((x + 0.5f) / size, (y + 0.5f) / size, rng));
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Attention : Mathf.SmoothStep(a, b, t) interpole entre a et b ; ce n'est PAS le smoothstep des shaders.
        static float Step(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }
        static float Smooth(float edge, float x, float soft) => 1f - Step(edge - soft, edge + soft, x);

        // Gouttelettes autour des taches (positions fixes, pseudo-aléatoires).
        static readonly Vector3[] PoolDrops = { new(0.86f, 0.55f, 0.05f), new(0.18f, 0.2f, 0.04f), new(0.7f, 0.12f, 0.035f), new(0.12f, 0.72f, 0.05f), new(0.52f, 0.92f, 0.03f), new(0.92f, 0.3f, 0.025f) };

        static Color PoolPixel(float u, float v, System.Random rng)
        {
            // Flaque : contour irrégulier (lobes), bord un peu plus sombre (le sang épais sèche sur le pourtour), gouttelettes.
            float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2f, a = Mathf.Atan2(dy, dx);
            float edge = 0.62f + 0.07f * Mathf.Sin(a * 3f + 0.4f) + 0.05f * Mathf.Sin(a * 5f + 1.7f) + 0.03f * Mathf.Sin(a * 11f);
            float alpha = Smooth(edge, r, 0.012f);
            foreach (var d in PoolDrops) alpha = Mathf.Max(alpha, Smooth(d.z, Vector2.Distance(new Vector2(u, v), new Vector2(d.x, d.y)) * 2f, 0.01f));
            float rim = Mathf.Clamp01(1f - (edge - r) / 0.08f);
            float shade = Mathf.Lerp(1f, 0.62f, rim * rim);
            return new Color(shade, shade, shade, alpha * 0.97f);
        }

        static Color SplatPixel(float u, float v, System.Random rng)
        {
            // Giclée : tache centrale, traînées en étoile et gouttes projetées (plus nombreuses d'un côté).
            float dx = u - 0.45f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2f, a = Mathf.Atan2(dy, dx);
            float core = Smooth(0.26f + 0.05f * Mathf.Sin(a * 7f), r, 0.015f);
            float streak = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 4.5f + Mathf.Sin(a * 2f))), 40f) * Smooth(0.55f + 0.3f * Mathf.Cos(a), r, 0.05f);
            float drops = 0f;
            for (int i = 0; i < 18; i++)
            {
                float ang = i * 2.399f, dist = 0.28f + (i * 37 % 23) / 23f * 0.42f;
                var c = new Vector2(0.45f + Mathf.Cos(ang) * dist * 0.5f * (Mathf.Cos(ang) > 0 ? 1.3f : 0.8f), 0.5f + Mathf.Sin(ang) * dist * 0.5f);
                float size = 0.018f + (i * 13 % 7) * 0.004f;
                drops = Mathf.Max(drops, Smooth(size, Vector2.Distance(new Vector2(u, v), c), 0.004f));
            }
            float alpha = Mathf.Clamp01(Mathf.Max(core, Mathf.Max(streak, drops)));
            return new Color(0.92f, 0.92f, 0.92f, alpha * 0.96f);
        }

        static Color DropPixel(float u, float v, System.Random rng)
        {
            float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            return new Color(1f, 1f, 1f, Smooth(0.75f, r, 0.2f));
        }

        static Color HolePixel(float u, float v, System.Random rng)
        {
            // Arche noire (le bas du quad touche le sol), bord rongé un peu plus clair.
            float du = u - 0.5f;
            float inside = v < 0.45f ? Mathf.Abs(du) / 0.45f : Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.45f)) / 0.45f;
            inside += 0.04f * Mathf.Sin(u * 40f) * Mathf.Sin(v * 33f);
            float alpha = Smooth(0.95f, inside, 0.04f);
            float rim = Mathf.Clamp01((inside - 0.7f) / 0.25f);
            var c = Color.Lerp(new Color(0.01f, 0.01f, 0.01f), new Color(0.16f, 0.1f, 0.06f), rim);
            c.a = alpha;
            return c;
        }

        static Mesh QuadMesh()
        {
            const string path = FxDir + "/Quad_Trou.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m != null) return m;
            m = new Mesh { name = "Quad_Trou" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
