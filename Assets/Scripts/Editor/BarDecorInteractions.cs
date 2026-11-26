using System.Collections.Generic;
using System.Linq;
using Fragile.Bar;
using Fragile.Grab;
using Fragile.Music;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Rend interactifs les appareils posés par <see cref="BarDecorBuilder"/> (pièces mobiles séparées par
    /// Tools/Blender/export_interactif.py) : leviers de la tireuse, touches et tiroir de la caisse, boutons de la machine à café,
    /// trappe et bac de la machine à glaçons, clavier du distributeur (canettes). Repère des modèles : devant = +Z local.
    /// </summary>
    public static class BarDecorInteractions
    {
        const string Folder = "Assets/Bar/Models/Decor";

        public static void Setup(GameObject model, string type)
        {
            switch (type)
            {
                case "Tireuse_Biere": Tap(model.transform); break;
                case "Caisse_Enregistreuse": Register(model.transform); break;
                case "Machine_Cafe": Coffee(model.transform); break;
                case "Machine_Glacons": Ice(model.transform); break;
                case "Distributeur_Boissons": Vending(model.transform); break;
            }
        }

        /// <summary>Matériau de surbrillance (dans Resources : son shader est inclus dans le build).</summary>
        public static void EnsureHighlightMaterial()
        {
            const string dir = "Assets/Bar/Resources";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Bar", "Resources");
            string path = $"{dir}/Surbrillance.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Fragile/Surbrillance")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("Fragile/Surbrillance");
            // Blanc bleuté : ressort sur l'éclairage chaud du bar et sur les surfaces claires (touches crème).
            m.SetColor("_Color", new Color(0.5f, 0.82f, 1f) * 1.3f);
            m.SetFloat("_Rim", 1.8f);
            m.SetFloat("_Fill", 0.25f);
            m.SetFloat("_Pulse", 0.35f);
            m.SetFloat("_Inflate", 0.0015f);
            EditorUtility.SetDirty(m);
        }

        // ------------------------------------------------------------------ tireuse

        static void Tap(Transform root)
        {
            var levers = new List<PullLever>(); var streams = new List<LiquidStream>();
            foreach (var n in new[] { "Levier_G", "Levier_D" })
            {
                var lever = root.Find(n);
                if (lever == null) continue;
                AddBox(lever);
                var pull = lever.gameObject.AddComponent<PullLever>();
                levers.Add(pull);
                // Bec verseur sous le levier.
                var spout = new GameObject("Bec " + n.Substring(7)).transform;
                spout.SetParent(root, false);
                spout.localPosition = lever.localPosition + new Vector3(0f, -0.098f, 0f);
                var s = spout.gameObject.AddComponent<LiquidStream>();
                s.Setup(new Color(0.88f, 0.56f, 0.12f, 0.85f), new Color(1f, 0.96f, 0.85f, 0.95f), 0.011f, StreamMaterial());
                streams.Add(s);
            }
            root.gameObject.AddComponent<BeerTap>().Setup(levers.ToArray(), streams.ToArray());
        }

        // ------------------------------------------------------------------ caisse

        static void Register(Transform root)
        {
            var register = root.gameObject.AddComponent<CashRegister>();
            foreach (Transform key in root.Cast<Transform>().Where(t => t.name.StartsWith("Touche_")).ToList())
            {
                var button = Button(root, key, root.up);
                if (key.name == "Touche_Total") UnityEventTools.AddVoidPersistentListener(button.pressed, register.Total);
                else
                {
                    var rc = key.name.Substring(7).Split('_');
                    int digit = (int.Parse(rc[0]) * 5 + int.Parse(rc[1]) + 1) % 10;
                    UnityEventTools.AddIntPersistentListener(button.pressed, register.Key, digit);
                }
                EditorUtility.SetDirty(button);
            }

            SlidingDrawer drawer = null;
            var tiroir = root.Find("Tiroir");
            if (tiroir != null)
            {
                var col = tiroir.gameObject.AddComponent<BoxCollider>();   // façade et poignée seulement
                col.center = new Vector3(0f, 0.042f, 0.006f); col.size = new Vector3(0.38f, 0.06f, 0.025f);
                drawer = tiroir.gameObject.AddComponent<SlidingDrawer>();
            }

            // Afficheur : chiffres sur l'écran vert (face avant de la tête d'affichage).
            var display = new GameObject("Afficheur");
            display.transform.SetParent(root, false);
            display.transform.localPosition = new Vector3(0f, 0.33f, -0.1505f);
            display.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // le texte se lit depuis l'avant (+Z)
            var tmp = display.AddComponent<TextMeshPro>();
            tmp.rectTransform.sizeDelta = new Vector2(0.15f, 0.034f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.05f; tmp.fontSizeMax = 0.3f;
            tmp.alignment = TextAlignmentOptions.MidlineRight;
            tmp.color = new Color(0.02f, 0.14f, 0.04f);
            tmp.text = "0.00";
            register.Setup(tmp, drawer, null);
        }

        // ------------------------------------------------------------------ machine à café

        static void Coffee(Transform root)
        {
            var machine = root.gameObject.AddComponent<CoffeeMachine>();
            var streams = new List<LiquidStream>();
            int i = 0;
            foreach (var n in new[] { "Bouton_G", "Bouton_D" })
            {
                var part = root.Find(n);
                if (part == null) continue;
                var button = Button(root, part, root.forward);
                UnityEventTools.AddIntPersistentListener(button.pressed, machine.Brew, i++);
                EditorUtility.SetDirty(button);
                // Sortie du café : sous le groupe, à l'aplomb du bouton.
                var spout = new GameObject("Groupe " + n.Substring(7)).transform;
                spout.SetParent(root, false);
                spout.localPosition = new Vector3(part.localPosition.x, 0.232f, 0.127f);
                var s = spout.gameObject.AddComponent<LiquidStream>();
                s.Setup(new Color(0.24f, 0.11f, 0.04f, 0.95f), new Color(0.62f, 0.4f, 0.2f, 0.95f), 0.006f, StreamMaterial());
                streams.Add(s);
            }
            machine.Setup(streams.ToArray());
        }

        // ------------------------------------------------------------------ machine à glaçons

        static void Ice(Transform root)
        {
            var flap = root.Find("Trappe");
            if (flap == null) return;
            // Charnière horizontale en bas, poignée en haut : la trappe bascule vers l'avant (vers soi).
            // On oriente un pivot pour que son Y local soit l'axe de la charnière, la poignée vers son −X (en haut)
            // et l'ouverture vers son +Z (l'avant). HingedDoor fait le reste.
            var hinge = new GameObject("Charnière trappe").transform;
            hinge.SetParent(root, false);
            hinge.localPosition = flap.localPosition;
            hinge.localRotation = Quaternion.LookRotation(Vector3.forward, Vector3.right);
            flap.SetParent(hinge, true);
            AddBox(flap);
            var rb = hinge.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
            var door = hinge.gameObject.AddComponent<HingedDoor>();
            var so = new SerializedObject(door);
            so.FindProperty("m_MaxAngle").floatValue = 85f;       // butée : presque à l'horizontale
            so.FindProperty("m_SelfClose").floatValue = 80f;      // le joint la rappelle quand elle est presque fermée
            so.FindProperty("m_SelfCloseZone").floatValue = 15f;
            so.FindProperty("m_MagnetZone").floatValue = 4f;
            so.FindProperty("m_Friction").floatValue = 2.5f;
            so.FindProperty("m_Bounce").floatValue = 0.1f;
            so.FindProperty("m_DoorMass").floatValue = 3f;
            so.FindProperty("m_Width").floatValue = 0.3f;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Bac : on y plonge la main pour prendre un glaçon (trappe ouverte).
            var bin = new GameObject("Bac à glaçons");
            bin.transform.SetParent(root, false);
            bin.transform.localPosition = new Vector3(0f, 0.62f, 0.3f);
            var bc = bin.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.38f, 0.26f, 0.08f);
            bin.AddComponent<IceBin>().Setup(door, IcePrefab());
        }

        // ------------------------------------------------------------------ distributeur

        static void Vending(Transform root)
        {
            var machine = root.gameObject.AddComponent<VendingMachine>();
            for (int i = 1; i <= 9; i++)
            {
                var key = root.Find("Touche_" + i);
                if (key == null) continue;
                var button = Button(root, key, root.forward);
                UnityEventTools.AddIntPersistentListener(button.pressed, machine.Vend, i - 1);
                EditorUtility.SetDirty(button);
            }
            // Trappe de sortie (bas de la façade) : la canette tombe juste devant.
            machine.Setup(CanPrefabs(), new Vector3(0.1f, 0.2f, 0.43f));
        }

        // ------------------------------------------------------------------ outils

        /// <summary>
        /// Bouton enfoncé au doigt (<see cref="PanelButton"/>) : objet placé sur la touche, son +Z = normale de la touche ;
        /// la touche devient sa partie mobile.
        /// </summary>
        static PanelButton Button(Transform root, Transform key, Vector3 normalWorld)
        {
            var go = new GameObject("Bouton " + key.name);
            go.transform.SetParent(root, false);
            go.transform.position = key.position;
            go.transform.rotation = Quaternion.LookRotation(normalWorld, Mathf.Abs(Vector3.Dot(normalWorld, root.up)) > 0.9f ? root.forward : root.up);
            key.SetParent(go.transform, true);

            // Dimensions de la touche dans le repère du bouton.
            var mesh = key.GetComponent<MeshFilter>().sharedMesh;
            var b = mesh.bounds; Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = go.transform.InverseTransformPoint(key.TransformPoint(c));
                min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            }
            var half = (max - min) * 0.5f;
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(half.x * 2f + 0.006f, half.y * 2f + 0.006f, 0.03f);
            var button = go.AddComponent<PanelButton>();
            button.Setup(key, new Vector2(half.x + 0.004f, half.y + 0.004f), half.z);
            return button;
        }

        static void AddBox(Transform t)
        {
            var mf = t.GetComponent<MeshFilter>();
            var col = t.gameObject.AddComponent<BoxCollider>();
            if (mf != null && mf.sharedMesh != null) { col.center = mf.sharedMesh.bounds.center; col.size = mf.sharedMesh.bounds.size + Vector3.one * 0.01f; }
        }

        static Material StreamMaterial()
        {
            string path = "Assets/Bar/FX/Jet_Liquide.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Bar/FX/Particule_Rond.png"));
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ objets créés en jeu : glaçon, canettes

        static GameObject IcePrefab()
        {
            string dir = $"{Folder}/Machine_Glacons";
            var mesh = SaveMesh(Cube(0.028f, 0.004f), $"{dir}/Glacon_Maillage.asset");
            var mat = LitMaterial($"{dir}/Materials/Glacon.mat", new Color(0.86f, 0.94f, 1f, 0.55f), 0.95f, transparent: true);
            var go = new GameObject("Glaçon");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<BoxCollider>().size = Vector3.one * 0.028f;
            var rb = go.AddComponent<Rigidbody>(); rb.mass = 0.02f; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            GrabbableBuilder.MakeGrabbable(go);
            SetDespawn(go.AddComponent<TimedDespawn>(), 30f);
            go.AddComponent<ImpactSound>();
            go.AddComponent<InteractableHighlight>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{dir}/Glacon.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject[] CanPrefabs()
        {
            string dir = $"{Folder}/Distributeur_Boissons";
            var mesh = SaveMesh(Can(0.033f, 0.122f, 20), $"{dir}/Canette_Maillage.asset");
            var top = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/Materials/M_Canette_Argent.mat");
            var list = new List<GameObject>();
            foreach (var color in new[] { "Rouge", "Bleue", "Verte", "Orange", "Argent" })
            {
                var body = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/Materials/M_Canette_{color}.mat");
                var go = new GameObject("Canette " + color);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { body, top };
                var cap = go.AddComponent<CapsuleCollider>();
                cap.direction = 1; cap.radius = 0.033f; cap.height = 0.122f; cap.center = Vector3.zero;
                var rb = go.AddComponent<Rigidbody>(); rb.mass = 0.35f; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.angularDamping = 2f; rb.linearDamping = 0.2f;   // une canette couchée ne roule pas à l'infini
                GrabbableBuilder.MakeGrabbable(go);
                SetDespawn(go.AddComponent<TimedDespawn>(), 120f);
                var snd = go.AddComponent<ImpactSound>();
                var sso = new SerializedObject(snd); sso.FindProperty("m_Ice").boolValue = false; sso.ApplyModifiedPropertiesWithoutUndo();
                go.AddComponent<InteractableHighlight>();
                list.Add(PrefabUtility.SaveAsPrefabAsset(go, $"{dir}/Canette_{color}.prefab"));
                Object.DestroyImmediate(go);
            }
            return list.ToArray();
        }

        static void SetDespawn(TimedDespawn d, float seconds)
        {
            var so = new SerializedObject(d); so.FindProperty("m_Lifetime").floatValue = seconds; so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Mesh SaveMesh(Mesh m, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(m, path); return m; }
            existing.Clear();
            existing.vertices = m.vertices; existing.normals = m.normals; existing.uv = m.uv;
            existing.subMeshCount = m.subMeshCount;
            for (int i = 0; i < m.subMeshCount; i++) existing.SetTriangles(m.GetTriangles(i), i);
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static Material LitMaterial(string path, Color c, float smoothness, bool transparent)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smoothness); m.SetFloat("_Metallic", 0f);
            if (transparent)
            {
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Cube aux arêtes chanfreinées (glaçon), centré.</summary>
        static Mesh Cube(float size, float bevel)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            float h = size * 0.5f, i = h - bevel;
            // 6 faces (réduites) + 12 chanfreins + 8 coins : on approxime par les 6 faces et 12 chanfreins.
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                // Ordre des sommets donné vers l'intérieur : on retourne les triangles (face visible vers l'extérieur).
                var nn = -Vector3.Cross(b - a, c - a).normalized; int k = v.Count;
                v.AddRange(new[] { a, b, c, d }); n.AddRange(new[] { nn, nn, nn, nn });
                t.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
            }
            foreach (var ax in new[] { Vector3.right, Vector3.up, Vector3.forward, Vector3.left, Vector3.down, Vector3.back })
            {
                var u = Mathf.Abs(ax.y) > 0.5f ? Vector3.right : Vector3.up; var w = Vector3.Cross(ax, u);
                var c0 = ax * h;
                Quad(c0 - u * i - w * i, c0 - u * i + w * i, c0 + u * i + w * i, c0 + u * i - w * i);
                // chanfrein vers +u
                Quad(c0 + u * i - w * i, c0 + u * i + w * i, ax * i + u * h + w * i, ax * i + u * h - w * i);
            }
            var m = new Mesh { name = "Glaçon" };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>Canette : corps (sous-maillage 0) + dessus/dessous (1), centrée, axe Y.</summary>
        static Mesh Can(float r, float height, int seg)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
            var body = new List<int>(); var caps = new List<int>();
            float h = height * 0.5f, rim = r * 0.82f, neck = 0.008f;
            for (int s = 0; s <= seg; s++)
            {
                float a = s / (float)seg * Mathf.PI * 2f; var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(d * r + Vector3.up * (-h + neck)); n.Add(d); uv.Add(new Vector2(s / (float)seg, 0f));
                v.Add(d * r + Vector3.up * (h - neck)); n.Add(d); uv.Add(new Vector2(s / (float)seg, 1f));
            }
            for (int s = 0; s < seg; s++) { int k = s * 2; body.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 }); }
            foreach (float sign in new[] { 1f, -1f })
            {
                int centre = v.Count; v.Add(Vector3.up * sign * h); n.Add(Vector3.up * sign); uv.Add(new Vector2(0.5f, 0.5f));
                int ring = v.Count;
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f; var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    v.Add(d * rim + Vector3.up * sign * h); n.Add((Vector3.up * sign + d * 0.3f).normalized); uv.Add(new Vector2(0.5f + d.x * 0.5f, 0.5f + d.z * 0.5f));
                }
                int shoulder = v.Count;
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f; var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    v.Add(d * r + Vector3.up * sign * (h - neck)); n.Add((d + Vector3.up * sign).normalized); uv.Add(new Vector2(0.5f + d.x * 0.5f, 0.5f + d.z * 0.5f));
                }
                for (int s = 0; s < seg; s++)
                {
                    if (sign > 0f)
                    {
                        caps.AddRange(new[] { centre, ring + s + 1, ring + s });
                        caps.AddRange(new[] { ring + s, ring + s + 1, shoulder + s + 1, ring + s, shoulder + s + 1, shoulder + s });
                    }
                    else
                    {
                        caps.AddRange(new[] { centre, ring + s, ring + s + 1 });
                        caps.AddRange(new[] { ring + s, shoulder + s + 1, ring + s + 1, ring + s, shoulder + s, shoulder + s + 1 });
                    }
                }
            }
            var m = new Mesh { name = "Canette" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
            m.subMeshCount = 2; m.SetTriangles(body, 0); m.SetTriangles(caps, 1);
            m.RecalculateBounds();
            return m;
        }
    }
}
