using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Fragile > Meubler le Bar : habille la pièce avec le mobilier de Assets/Bar/Models/Mobilier
    /// (modèles Poly Haven, CC0, importés par Tools/Blender/import_polyhaven.py) :
    /// tabourets autour du comptoir, coin repas (tables, fauteuils, suspensions), coin salon, plantes, tonneaux,
    /// et aux murs : enseigne néon, cadres, cible de fléchettes, horloge.
    /// Tout est statique (lightmaps). Relançable : l'objet Room/Mobilier est reconstruit. Relancer « Éclairer le Bar » après.
    /// </summary>
    public static class BarFurnitureBuilder
    {
        const string Folder = "Assets/Bar/Models/Mobilier";
        const string RootName = "Mobilier";
        // Murs intérieurs (voir « Construire la pièce du Bar »).
        const float WallW = -5f, WallE = 5f, WallS = -5f, WallN = 8.36f, Ceiling = 3f;

        enum Kind { Floor, OnTop, Wall, Hanging }

        struct Item
        {
            public string model, name, artwork; public Kind kind; public float x, y, z, rotY; public Vector3 scale;
            /// <param name="artwork">Cadre : image personnelle à la place de l'œuvre d'origine (Assets/Bar/Tableaux).</param>
            public Item(string model, string name, Kind kind, float x, float z, float rotY, Vector3 scale, float y = 0f, string artwork = null)
            { this.model = model; this.name = name; this.kind = kind; this.x = x; this.z = z; this.rotY = rotY; this.scale = scale; this.y = y; this.artwork = artwork; }
        }

        static Vector3 S(float s) => Vector3.one * s;
        static readonly Vector3 Stool = new Vector3(1.1f, 1.6f, 1.1f);   // tabouret bas étiré en tabouret de bar (74 cm)

        static List<Item> Layout()
        {
            var l = new List<Item>();
            // Tabourets côté clients du comptoir (rotation variée : posés à la main).
            float[] north = { -2.2f, -1.1f, 0f, 1.1f, 2.2f };
            for (int i = 0; i < north.Length; i++) l.Add(new Item("metal_stool_02", "Tabouret", Kind.Floor, north[i], 3.36f, i * 37f, Stool));
            foreach (var z in new[] { 1.6f, 0.4f, -0.8f }) l.Add(new Item("metal_stool_02", "Tabouret", Kind.Floor, -3.23f, z, z * 50f, Stool));
            foreach (var z in new[] { 1.4f, -0.4f }) l.Add(new Item("metal_stool_02", "Tabouret", Kind.Floor, 3.23f, z, z * 70f, Stool));

            // Coin repas : 3 tables, 2 fauteuils chacune, plante au centre.
            var tables = new[] { new Vector2(-3.0f, 5.7f), new Vector2(-0.35f, 7.4f), new Vector2(2.2f, 5.6f) };
            for (int i = 0; i < tables.Length; i++)
            {
                var t = tables[i];
                l.Add(new Item("round_wooden_table_01", "Table", Kind.Floor, t.x, t.y, i * 25f, S(0.75f)));
                l.Add(new Item("modern_arm_chair_01", "Fauteuil", Kind.Floor, t.x - 0.85f, t.y + (i - 1) * 0.08f, 90f + (i * 7f - 6f), S(1f)));
                l.Add(new Item("modern_arm_chair_01", "Fauteuil", Kind.Floor, t.x + 0.85f, t.y - (i - 1) * 0.08f, -90f + (i * 5f - 4f), S(1f)));
                l.Add(new Item("potted_plant_04", "Plante de table", Kind.OnTop, t.x, t.y, i * 60f, S(1f)));
                // Suspension au-dessus : la même que celles du bar, posée par « Éclairer le Bar ».
            }

            // Coin salon (angle nord-est).
            l.Add(new Item("coffee_table_round_01", "Table basse", Kind.Floor, 3.75f, 7.45f, 0f, S(0.7f)));
            l.Add(new Item("mid_century_lounge_chair", "Fauteuil lounge", Kind.Floor, 2.7f, 7.4f, 100f, S(1f)));
            l.Add(new Item("mid_century_lounge_chair", "Fauteuil lounge", Kind.Floor, 3.8f, 6.35f, -10f, S(1f)));

            // Grandes plantes dans les angles, tonneaux au sud-ouest.
            foreach (var p in new[] { new Vector2(4.55f, 7.9f), new Vector2(-4.55f, 7.9f), new Vector2(4.5f, -4.5f) })
                l.Add(new Item("potted_plant_04", "Plante", Kind.Floor, p.x, p.y, p.x * 30f, S(3.5f)));
            l.Add(new Item("wine_barrel_01", "Tonneau", Kind.Floor, -4.5f, -4.5f, 20f, S(1f)));
            l.Add(new Item("wine_barrel_01", "Tonneau", Kind.Floor, -3.65f, -4.55f, -35f, S(1f)));
            l.Add(new Item("wine_barrel_01", "Tonneau", Kind.Floor, -4.55f, -3.65f, 70f, S(1f)));

            // Murs, entre les appliques (à 1,64 m, tous les 3,33 m).
            l.Add(new Item("hanging_picture_frame_02", "Cadre", Kind.Wall, 1.66f, WallN, 180f, S(1.4f), 1.75f));
            l.Add(new Item("hanging_picture_frame_02", "Cadre", Kind.Wall, -1.66f, WallS, 0f, S(1.4f), 1.75f));
            // Photos de l'équipe (zone d'image du cadre : U 0,16 → 0,84 d'une texture carrée, voir Assets/Bar/Tableaux).
            l.Add(new Item("hanging_picture_frame_01", "Tableau casque VR", Kind.Wall, WallW, 1.68f, 90f, S(1.3f), 1.7f, "Tableau_Casque_VR"));
            l.Add(new Item("hanging_picture_frame_01", "Tableau portrait", Kind.Wall, WallE, -1.65f, -90f, S(1.3f), 1.7f, "Tableau_Portrait"));
            l.Add(new Item("dartboard", "Cible de fléchettes", Kind.Wall, WallW, 5.0f, 90f, S(1f), 1.73f));
            l.Add(new Item("wall_clock", "Horloge", Kind.Wall, WallE, 1.68f, -90f, S(1.6f), 2.05f));
            return l;
        }

        [MenuItem("Fragile/Meubler le Bar")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            var room = GameObject.Find("Room");
            if (room == null) { Debug.LogError("[Fragile] Pas de « Room » dans la scène."); return; }

            var old = room.transform.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var root = new GameObject(RootName).transform;
            root.SetParent(room.transform, false);

            var prepared = new HashSet<string>();
            int placed = 0;
            foreach (var it in Layout())
            {
                if (prepared.Add(it.model)) Prepare(it.model);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/{it.model}/{it.model}.fbx");
                if (model == null) { Debug.LogWarning($"[Fragile] Modèle absent : {it.model} (Tools/Blender/import_polyhaven.py)."); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
                go.name = it.name;
                if (it.artwork != null) SetArtwork(go, it.artwork);
                go.transform.localScale = it.scale;
                go.transform.rotation = Quaternion.Euler(0f, it.rotY, 0f);
                Place(go, it);
                Finish(go, it.kind);
                Physics.SyncTransforms();   // les objets suivants (plantes de table) se posent sur celui-ci
                placed++;
            }
            BuildNeon(root);

            Undo.RegisterCreatedObjectUndo(root.gameObject, "Meubler le Bar");
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Fragile] Bar meublé : {placed} objets + enseigne néon (relance « Éclairer le Bar »).");
        }

        static void Prepare(string model)
        {
            BlenderModelImporter.ImportFolder($"{Folder}/{model}");
            // Vitres (cadres, horloge) : verre transparent simple. Importées opaques, elles cachaient l'image ;
            // le shader Fragile/Verre (réfraction) serait inutilement coûteux pour une vitre plate au mur.
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { $"{Folder}/{model}/Materials" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || !m.name.EndsWith("_glass")) continue;
                m.shader = Shader.Find("Universal Render Pipeline/Lit");
                m.SetTexture("_BaseMap", null);
                m.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.08f));
                m.SetFloat("_Smoothness", 0.95f); m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                m.SetShaderPassEnabled("ShadowCaster", false);
                EditorUtility.SetDirty(m);
            }
            if (AssetImporter.GetAtPath($"{Folder}/{model}/{model}.fbx") is ModelImporter imp && !imp.generateSecondaryUV)
            {
                imp.generateSecondaryUV = true;
                imp.SaveAndReimport();
            }
        }

        static void Place(GameObject go, Item it)
        {
            var b = Bounds(go);
            var pos = go.transform.position;
            switch (it.kind)
            {
                case Kind.Floor:
                case Kind.OnTop:
                    float surface = SurfaceBelow(new Vector3(it.x, 0f, it.z), go);
                    pos += new Vector3(it.x - b.center.x, surface - b.min.y, it.z - b.center.z);
                    break;
                case Kind.Hanging:
                    pos += new Vector3(it.x - b.center.x, Ceiling - b.max.y, it.z - b.center.z);
                    break;
                case Kind.Wall:
                    // Collé au mur : le dos de l'objet touche le mur.
                    var face = go.transform.forward;
                    float depth = Mathf.Abs(Vector3.Dot(b.size, new Vector3(Mathf.Abs(face.x), 0f, Mathf.Abs(face.z))));
                    var onWall = new Vector3(it.x, it.y, it.z) + face * (depth * 0.5f + 0.004f);
                    pos += onWall - b.center;
                    break;
            }
            go.transform.position = pos;
        }

        static void Finish(GameObject go, Kind kind)
        {
            bool small = kind == Kind.Wall || kind == Kind.OnTop || kind == Kind.Hanging;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
                // Petits objets : éclairés par les light probes (pas de place prise dans les lightmaps).
                r.receiveGI = small ? ReceiveGI.LightProbes : ReceiveGI.Lightmaps;
            }
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            if (kind == Kind.Floor || kind == Kind.OnTop)
            {
                var col = go.AddComponent<BoxCollider>();
                var lb = LocalBounds(go);
                col.center = lb.center; col.size = lb.size;
            }
        }

        /// <summary>Remplace l'œuvre d'un cadre par une image de Assets/Bar/Tableaux (matériau propre, posé dans la scène).</summary>
        static void SetArtwork(GameObject frame, string image)
        {
            const string dir = "Assets/Bar/Tableaux";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{image}.png");
            if (tex == null) { Debug.LogWarning($"[Fragile] Image absente : {dir}/{image}.png"); return; }
            if (AssetImporter.GetAtPath($"{dir}/{image}.png") is TextureImporter ti && ti.maxTextureSize != 1024)
            {
                ti.maxTextureSize = 1024; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
            }
            string path = $"{dir}/{image}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.25f); mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            foreach (var r in frame.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name.EndsWith("_artwork")) mats[i] = mat;
                r.sharedMaterials = mats;
            }
        }

        /// <summary>Enseigne « FRAGILE » au néon (texte lumineux sur un panneau sombre) + lueur précalculée sur le mur.</summary>
        static void BuildNeon(Transform root)
        {
            var sign = new GameObject("Enseigne néon").transform;
            sign.SetParent(root, false);
            sign.position = new Vector3(-1.66f, 2.3f, WallN);
            sign.rotation = Quaternion.Euler(0f, 180f, 0f);   // regarde vers le sud (dans la pièce)

            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Panneau";
            board.transform.SetParent(sign, false);
            board.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            board.transform.localScale = new Vector3(1.75f, 0.42f, 0.03f);
            Object.DestroyImmediate(board.GetComponent<Collider>());
            board.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Bar/TV/TV_Plastique.mat");
            GameObjectUtility.SetStaticEditorFlags(board, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);

            var text = new GameObject("Texte");
            text.transform.SetParent(sign, false);
            text.transform.localPosition = new Vector3(0f, 0f, 0.04f);
            text.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // TextMeshPro se lit depuis son −Z
            var tmp = text.AddComponent<TextMeshPro>();
            tmp.text = "FRAGILE";
            tmp.rectTransform.sizeDelta = new Vector2(1.6f, 0.36f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.1f; tmp.fontSizeMax = 4f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.characterSpacing = 12f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.fontSharedMaterial = NeonMaterial(tmp.font.material);

            var glow = new GameObject("Lueur néon").AddComponent<Light>();
            glow.transform.SetParent(sign, false);
            glow.transform.localPosition = new Vector3(0f, 0f, -0.35f);
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.25f, 0.6f);
            glow.intensity = 1.6f; glow.range = 2.6f;
            glow.lightmapBakeType = LightmapBakeType.Baked;
            glow.shadows = LightShadows.Soft;
        }

        static Material NeonMaterial(Material fontMaterial)
        {
            string path = $"{Folder}/Neon_Fragile.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(fontMaterial); AssetDatabase.CreateAsset(m, path); }
            m.shader = fontMaterial.shader;
            m.CopyPropertiesFromMaterial(fontMaterial);
            m.SetColor("_FaceColor", new Color(1f, 0.55f, 0.82f));
            m.EnableKeyword("GLOW_ON");
            m.SetColor("_GlowColor", new Color(1f, 0.12f, 0.55f, 0.9f));
            m.SetFloat("_GlowOffset", 0f); m.SetFloat("_GlowInner", 0.2f); m.SetFloat("_GlowOuter", 0.7f); m.SetFloat("_GlowPower", 0.65f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ outils

        static float SurfaceBelow(Vector3 p, GameObject self)
        {
            float best = 0f;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, 2.6f, p.z), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(self.transform) || h.collider.attachedRigidbody != null) continue;  // pas sur une bouteille
                if (h.normal.y > 0.7f && h.point.y > best) best = h.point.y;
            }
            return best;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static Bounds LocalBounds(GameObject go)
        {
            var root = go.transform; bool first = true; var b = new Bounds();
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
