using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Fragile > Aménager le décor du Bar :
    /// pose les appareils de Assets/Bar/Models/Decor (exportés de Blender, « place »: false) dans la pièce.
    /// Tireuse et caisse sur le comptoir, machine à café sur l'étagère du barman, machine à glaçons au sol derrière le bar,
    /// frigos vitrines contre le mur sud (derrière l'ouverture du bar), distributeur de boissons côté clients.
    /// Frigos : porte à charnière qu'on ouvre en saisissant la poignée (<see cref="Fragile.Grab.HingedDoor"/>),
    /// remplis de bouteilles attrapables et cassables (préfabs du comptoir).
    /// Relançable : l'objet Room/Décor est reconstruit. Relancer ensuite « Éclairer le Bar » (objets statiques).
    /// </summary>
    public static class BarDecorBuilder
    {
        const string Folder = "Assets/Bar/Models/Decor";
        const string RootName = "Décor";

        // Le devant des modèles est leur +Z local (−Y dans Blender).
        // centre = centre de l'emprise au sol (x, z) ; rotY = direction du devant ; la base est posée sur la surface trouvée dessous.
        struct Spot { public string model, name; public float x, z, rotY; public Spot(string m, string n, float x, float z, float r) { model = m; name = n; this.x = x; this.z = z; rotY = r; } }

        static readonly Spot[] Spots =
        {
            new Spot("Tireuse_Biere",         "Tireuse à bière",       1.88f, -0.87f, -90f), // étagère est, robinets vers le barman (emplacement choisi par Tom)
            new Spot("Caisse_Enregistreuse",  "Caisse enregistreuse",  2.50f,  0.20f, -90f), // comptoir est, touches vers le barman
            new Spot("Machine_Cafe",          "Machine à café",       -1.82f,  0.30f,  90f), // étagère ouest du barman
            new Spot("Machine_Glacons",       "Machine à glaçons",     1.15f,  1.40f, 180f), // au sol, dos contre l'étagère nord
            new Spot("Refrigerateur_Vitrine", "Frigo vitrine 1",       0.55f, -4.62f,   0f), // mur sud, face à l'ouverture du bar
            new Spot("Refrigerateur_Vitrine", "Frigo vitrine 2",       1.18f, -4.62f,   0f),
            new Spot("Distributeur_Boissons", "Distributeur",          4.58f,  5.00f, -90f), // mur est, côté clients, entre deux appliques
        };

        [MenuItem("Fragile/Aménager le décor du Bar")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            var room = GameObject.Find("Room");
            if (room == null) { Debug.LogError("[Fragile] Pas de « Room » dans la scène : lance d'abord « Construire la pièce du Bar »."); return; }

            foreach (var s in Spots) PrepareModel(s.model);
            SetupEmission();

            var old = room.transform.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var root = new GameObject(RootName).transform;
            root.SetParent(room.transform, false);

            var log = new System.Text.StringBuilder();
            foreach (var s in Spots)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/{s.model}/{s.model}.fbx");
                if (model == null) { Debug.LogWarning($"[Fragile] Modèle absent : {s.model} (exporte-le depuis Blender)."); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
                go.name = s.name;
                go.transform.rotation = Quaternion.Euler(0f, s.rotY, 0f);

                var b = WorldBounds(go);
                float surface = SurfaceBelow(new Vector3(s.x, 0f, s.z), go);
                go.transform.position += new Vector3(s.x - b.center.x, surface - b.min.y, s.z - b.center.z);

                // Objet statique (sauf la porte du frigo, qui bouge).
                var door = go.transform.Find("Porte");
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    if (door != null && r.transform.IsChildOf(door)) continue;
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
                    r.receiveGI = ReceiveGI.Lightmaps;
                }
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);

                if (door != null) SetupFridge(go, door, s.name.EndsWith("2") ? FridgeB : FridgeA);
                else
                {
                    // Collider en boîte (bien moins coûteux qu'un MeshCollider sur Quest).
                    var col = go.AddComponent<BoxCollider>();
                    var lb = LocalBounds(go);
                    col.center = lb.center; col.size = lb.size;
                }
                log.Append($"\n  {s.name} : ({s.x:F2}, {surface:F2}, {s.z:F2})");
            }

            Undo.RegisterCreatedObjectUndo(root.gameObject, "Aménager le décor du Bar");
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Fragile] Décor du Bar posé (relance « Éclairer le Bar » pour les lightmaps) :" + log);
        }

        // ------------------------------------------------------------------ frigos
        // Repère local du frigo (pivot au sol, au centre ; devant = +Z ; charnière à +X, poignée à −X).
        // Le modèle exporté a 2 étagères (au lieu de 4) pour loger des bouteilles de 42 cm : 3 niveaux.
        const string Bottles = "Assets/Bar/Models/Bouteilles";
        static readonly float[] Levels = { 0.15f, 0.63f, 1.10f };
        static readonly Vector2[] Slots = { new Vector2(-0.16f, 0.12f), new Vector2(0f, 0.12f), new Vector2(0.16f, 0.12f), new Vector2(-0.08f, -0.13f), new Vector2(0.08f, -0.13f) };
        static readonly string[][] FridgeA = { new[] { "Eau", "Eau", "Eau", "Eau", "Eau" }, new[] { "Bleue", "Bleue", "Bleue", "Bleue", "Bleue" }, new[] { "Coloree", "Coloree", "Coloree", "Coloree", "Coloree" } };
        static readonly string[][] FridgeB = { new[] { "Korken", "Korken", "Korken", "Korken", "Korken" }, new[] { "Eau", "Korken", "Eau", "Korken", "Eau" }, new[] { "Coloree", "Bleue", "Coloree", "Bleue", "Coloree" } };

        static void SetupFridge(GameObject fridge, Transform door, string[][] content)
        {
            // Caisson : parois et étagères en boîtes (le volume intérieur reste libre pour les bouteilles).
            var walls = new GameObject("Collisions").transform;
            walls.SetParent(fridge.transform, false);
            Box(walls, "Fond", new Vector3(0f, 0.95f, -0.3245f), new Vector3(0.6f, 1.9f, 0.045f));
            Box(walls, "Côté gauche", new Vector3(-0.28f, 0.975f, -0.037f), new Vector3(0.04f, 1.85f, 0.62f));
            Box(walls, "Côté droit", new Vector3(0.28f, 0.975f, -0.037f), new Vector3(0.04f, 1.85f, 0.62f));
            Box(walls, "Dessus", new Vector3(0f, 1.80f, -0.025f), new Vector3(0.6f, 0.2f, 0.644f));
            Box(walls, "Socle", new Vector3(0f, 0.075f, -0.037f), new Vector3(0.6f, 0.15f, 0.62f));
            Box(walls, "Étagère 1", new Vector3(0f, 0.62f, -0.0375f), new Vector3(0.52f, 0.02f, 0.52f));
            Box(walls, "Étagère 2", new Vector3(0f, 1.09f, -0.0375f), new Vector3(0.52f, 0.02f, 0.52f));
            GameObjectUtility.SetStaticEditorFlags(walls.gameObject, 0);

            // Porte : cinématique, saisie par la poignée uniquement.
            GameObjectUtility.SetStaticEditorFlags(door.gameObject, 0);
            var rb = door.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
            // Cadre en 4 montants (la vitre a son propre collider : une fois cassée, on passe la main dans le cadre).
            foreach (var (c, s) in new[] {
                (new Vector3(-0.58f, 0.88f, 0f), new Vector3(0.04f, 1.6f, 0.04f)), (new Vector3(-0.02f, 0.88f, 0f), new Vector3(0.04f, 1.6f, 0.04f)),
                (new Vector3(-0.3f, 0.10f, 0f), new Vector3(0.6f, 0.04f, 0.04f)), (new Vector3(-0.3f, 1.66f, 0f), new Vector3(0.6f, 0.04f, 0.04f)) })
            {
                var bar = door.gameObject.AddComponent<BoxCollider>();
                bar.center = c; bar.size = s;
            }
            SetupGlass(door);
            var handle = door.gameObject.AddComponent<BoxCollider>();
            handle.center = new Vector3(-0.55f, 1.0f, 0.04f); handle.size = new Vector3(0.09f, 0.6f, 0.08f);
            var hinge = door.gameObject.AddComponent<Fragile.Grab.HingedDoor>();
            var so = new SerializedObject(hinge);
            var cols = so.FindProperty("m_Colliders");
            cols.arraySize = 1; cols.GetArrayElementAtIndex(0).objectReferenceValue = handle;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Bouteilles : mêmes préfabs que le comptoir (attrapables, cassables, avec liquide).
            var holder = new GameObject("Bouteilles").transform;
            holder.SetParent(fridge.transform, false);
            var rng = new System.Random(fridge.name.GetHashCode());
            for (int l = 0; l < Levels.Length; l++)
                for (int i = 0; i < Slots.Length; i++)
                {
                    var type = content[l][i];
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Bottles}/Bouteille_{type}/Bouteille_{type}.prefab");
                    if (prefab == null) { Debug.LogWarning("[Fragile] Préfab absent : Bouteille_" + type); continue; }
                    var b = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder);
                    b.transform.localPosition = new Vector3(Slots[i].x, Levels[l] + 0.002f, Slots[i].y);
                    b.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                }

        }

        /// <summary>
        /// Sépare la vitre de la porte (sous-maillage M_Verre) en un objet « Vitre » cassable (<see cref="Fragile.Breakables.GlassPane"/>).
        /// Vitre en vraie transparence (mélange alpha) : le shader Fragile/Verre est opaque (il recopie l'Opaque Texture,
        /// où les bouteilles en verre n'existent pas) et les cacherait. Tout est posé dans la scène : un nouvel import Blender ne l'écrase pas.
        /// </summary>
        static void SetupGlass(Transform door)
        {
            var mf = door.GetComponent<MeshFilter>();
            var mr = door.GetComponent<MeshRenderer>();
            var src = mf.sharedMesh;
            var mats = mr.sharedMaterials;
            int glassIndex = System.Array.FindIndex(mats, m => m != null && m.name == "M_Verre");
            if (glassIndex < 0) { Debug.LogWarning("[Fragile] Porte du frigo : pas de sous-maillage M_Verre."); return; }

            var glassBounds = src.GetSubMesh(glassIndex).bounds;
            var frameMesh = SubMeshes(src, i => i != glassIndex, Vector3.zero, "Frigo_Porte_SansVitre");
            var glassMesh = SubMeshes(src, i => i == glassIndex, glassBounds.center, "Frigo_Vitre_Maillage");

            mf.sharedMesh = frameMesh;
            var keep = new List<Material>();
            for (int i = 0; i < mats.Length; i++) if (i != glassIndex) keep.Add(mats[i]);
            mr.sharedMaterials = keep.ToArray();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var pane = new GameObject("Vitre");
            pane.transform.SetParent(door, false);
            pane.transform.localPosition = glassBounds.center;
            pane.AddComponent<MeshFilter>().sharedMesh = glassMesh;
            var pr = pane.AddComponent<MeshRenderer>();
            pr.sharedMaterial = FridgeGlass();
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var col = pane.AddComponent<BoxCollider>();
            col.size = new Vector3(glassBounds.size.x, glassBounds.size.y, 0.03f); // plus épais que le verre : la batte ne le traverse pas
            var glass = pane.AddComponent<Fragile.Breakables.GlassPane>();
            glass.Setup(new Vector2(glassBounds.size.x, glassBounds.size.y),
                Lit("Frigo_Fissure", new Color(0.95f, 0.98f, 1f, 0.75f)), Lit("Frigo_Eclat", new Color(0.8f, 0.9f, 0.95f, 0.35f)),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/FX/BreakFX.prefab"),
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Bar/Audio/Verre_Casse.wav"));
        }

        /// <summary>Copie des sous-maillages choisis (sommets décalés de -offset), enregistrée en asset.</summary>
        static Mesh SubMeshes(Mesh src, System.Func<int, bool> pick, Vector3 offset, string name)
        {
            string path = $"{Folder}/Refrigerateur_Vitrine/{name}.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m == null) { m = new Mesh(); AssetDatabase.CreateAsset(m, path); }
            m.Clear();
            m.name = name;
            var verts = src.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i] -= offset;
            m.vertices = verts;
            m.normals = src.normals;
            m.tangents = src.tangents;
            m.uv = src.uv;
            var subs = new List<int[]>();
            for (int i = 0; i < src.subMeshCount; i++) if (pick(i)) subs.Add(src.GetIndices(i));
            m.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) m.SetTriangles(subs[i], i);
            m.RecalculateBounds();
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Matériau URP Lit transparent (fissures, éclats de vitre).</summary>
        static Material Lit(string name, Color c)
        {
            string path = $"{Folder}/Refrigerateur_Vitrine/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            MakeTransparent(m, c, 0.9f);
            return m;
        }

        static Material FridgeGlass()
        {
            string path = $"{Folder}/Refrigerateur_Vitrine/Materials/Frigo_Vitre.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            MakeTransparent(m, new Color(0.82f, 0.9f, 0.95f, 0.1f), 0.95f);
            return m;
        }

        static void MakeTransparent(Material m, Color color, float smoothness)
        {
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(m);
        }

        static void Box(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<BoxCollider>();
            c.center = center; c.size = size;
        }

        /// <summary>UV de lightmap pour les objets statiques.</summary>
        static void PrepareModel(string model)
        {
            if (AssetImporter.GetAtPath($"{Folder}/{model}/{model}.fbx") is ModelImporter imp && !imp.generateSecondaryUV)
            {
                imp.generateSecondaryUV = true;
                imp.SaveAndReimport();
            }
        }

        /// <summary>L'addon Blender n'exporte pas l'émission : écrans, LED et néons des frigos sont rallumés ici.</summary>
        static void SetupEmission()
        {
            Emit("Caisse_Enregistreuse", "M_Ecran_Vert", new Color(0.35f, 1f, 0.45f), 1.4f, false);
            Emit("Machine_Glacons", "M_LED_Verte", new Color(0.2f, 1f, 0.3f), 3f, false);
            Emit("Refrigerateur_Vitrine", "M_Eclairage", new Color(0.88f, 0.94f, 1f), 2.2f, true);
            Emit("Distributeur_Boissons", "M_Eclairage", new Color(0.88f, 0.94f, 1f), 2.2f, true);
        }

        static void Emit(string model, string mat, Color c, float intensity, bool baked)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{Folder}/{model}/Materials/{mat}.mat");
            if (m == null) { Debug.LogWarning($"[Fragile] Matériau absent : {model}/{mat}"); return; }
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * intensity);
            // Néons des frigos : leur lueur est précalculée dans les lightmaps ; le reste ne fait que briller.
            m.globalIlluminationFlags = baked ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(m);
        }

        /// <summary>Plus haute surface du comptoir ou du sol sous ce point (ignore bouteilles, batte, etc.).</summary>
        static float SurfaceBelow(Vector3 p, GameObject self)
        {
            float best = 0f;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, 2.5f, p.z), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(self.transform)) continue;
                var n = h.collider.name;
                if ((n == "Comptoir" || n == "Floor") && h.point.y > best) best = h.point.y;
            }
            return best;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static Bounds LocalBounds(GameObject go)
        {
            var root = go.transform;
            bool first = true; var b = new Bounds();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = root.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
                }
            }
            return b;
        }
    }
}
