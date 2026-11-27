using System.Collections.Generic;
using Fragile.Darts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Fragile > Installer les fléchettes : 3 fléchettes (rouge, bleue, jaune) à disposition sur un support sur pied, près
    /// d'une ligne de lancer peinte au sol (2,37 m de la cible, distance officielle). Pas de mode de jeu : on les prend si
    /// on veut, elles se plantent dans la cible (<see cref="DartBoard"/>) et se retirent à la main.
    /// À relancer après « Meubler le Bar » (la cible est recréée).
    /// </summary>
    public static class DartsBuilder
    {
        const string Folder = "Assets/Bar/Flechettes";
        const string RootName = "Fléchettes";
        const float ThrowDistance = 2.37f;

        [MenuItem("Fragile/Installer les fléchettes")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            Transform board = null;
            var furniture = GameObject.Find("Room/Mobilier");
            if (furniture != null) foreach (Transform t in furniture.transform) if (t.name == "Cible de fléchettes") board = t;
            if (board == null) { Debug.LogError("[Fragile] Cible de fléchettes introuvable (Fragile > Meubler le Bar)."); return; }
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Bar", "Flechettes");

            foreach (var name in new[] { RootName, "Jeu de fléchettes" })
            {
                var old = GameObject.Find(name);
                if (old != null) Undo.DestroyObjectImmediate(old);
            }
            if (!board.TryGetComponent<DartBoard>(out _)) Undo.AddComponent<DartBoard>(board.gameObject);
            var root = new GameObject(RootName).transform;

            // Face de la cible : avant du modèle (+Z local), centre du maillage.
            var mesh = board.GetComponent<MeshFilter>().sharedMesh;
            var faceLocal = new Vector3(mesh.bounds.center.x, mesh.bounds.center.y, mesh.bounds.max.z);
            var face = board.TransformPoint(faceLocal);
            var toRoom = Vector3.ProjectOnPlane(board.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, -toRoom);    // droite du joueur face à la cible

            // Ligne de lancer : pieds du joueur, regard vers la cible. Un peu décalée si un meuble gêne.
            var spot = new GameObject("Ligne de lancer").transform;
            spot.SetParent(root, false);
            spot.position = FreeSpot(new Vector3(face.x, 0f, face.z) + toRoom * ThrowDistance, right);
            spot.rotation = Quaternion.LookRotation(-toRoom, Vector3.up);
            var line = GameObject.CreatePrimitive(PrimitiveType.Cube);   // ligne de lancer peinte au sol (oche)
            line.name = "Oche";
            Object.DestroyImmediate(line.GetComponent<Collider>());
            line.transform.SetParent(spot, false);
            line.transform.localPosition = new Vector3(0f, 0.002f, -0.12f);
            line.transform.localScale = new Vector3(0.6f, 0.003f, 0.04f);
            line.GetComponent<Renderer>().sharedMaterial = Lit("Oche", new Color(0.85f, 0.78f, 0.55f), 0f, 0.3f);

            // Support : plateau sur pied, à gauche du joueur, à hauteur de main.
            var stand = BuildStand(root, spot.position - right * 0.45f + toRoom * 0.05f, spot.rotation, out var slots);

            // Fléchettes, posées sur le support.
            var dartMesh = SaveMesh(DartMesh(), $"{Folder}/Flechette_Maillage.asset");
            var colors = new[] { ("Rouge", new Color(0.8f, 0.08f, 0.06f)), ("Bleue", new Color(0.08f, 0.25f, 0.85f)), ("Jaune", new Color(0.95f, 0.75f, 0.1f)) };
            for (int i = 0; i < colors.Length; i++)
            {
                var prefab = DartPrefab(colors[i].Item1, colors[i].Item2, dartMesh);
                var d = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                d.transform.SetPositionAndRotation(slots[i].position, slots[i].rotation);
            }

            Undo.RegisterCreatedObjectUndo(root.gameObject, "Fléchettes");
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Fragile] Fléchettes installées : ligne à {ThrowDistance} m de la cible ({spot.position.x:F2}, {spot.position.z:F2}).");
        }

        /// <summary>Place libre pour un joueur (colonne de 50 cm) : la place voulue, sinon la plus proche sur le côté.</summary>
        static Vector3 FreeSpot(Vector3 wanted, Vector3 side)
        {
            Physics.SyncTransforms();
            foreach (float s in new[] { 0f, -0.1f, 0.1f, -0.2f, 0.2f, -0.3f, 0.3f })
            {
                var p = wanted + side * s;
                if (!Physics.CheckBox(p + Vector3.up * 1f, new Vector3(0.25f, 0.85f, 0.25f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) return p;
            }
            return wanted;
        }

        static Transform BuildStand(Transform root, Vector3 foot, Quaternion facing, out Transform[] slots)
        {
            var wood = Lit("Support_Bois", new Color(0.32f, 0.18f, 0.09f), 0f, 0.35f);
            var black = Lit("Support_Metal", new Color(0.05f, 0.05f, 0.05f), 0.8f, 0.5f);
            var stand = new GameObject("Support à fléchettes").transform;
            stand.SetParent(root, false);
            stand.SetPositionAndRotation(foot, facing);
            const float top = 1.0f, w = 0.14f, l = 0.26f, lip = 0.018f;
            Box(stand, "Pied", new Vector3(0f, top / 2f, 0f), new Vector3(0.03f, top, 0.03f), black, true);
            Box(stand, "Socle", new Vector3(0f, 0.01f, 0f), new Vector3(0.26f, 0.02f, 0.26f), black, true);
            Box(stand, "Plateau", new Vector3(0f, top, 0f), new Vector3(w, 0.02f, l), wood, true);
            Box(stand, "Rebord G", new Vector3(-w / 2f, top + lip / 2f, 0f), new Vector3(0.012f, lip + 0.02f, l), wood, true);
            Box(stand, "Rebord D", new Vector3(w / 2f, top + lip / 2f, 0f), new Vector3(0.012f, lip + 0.02f, l), wood, true);
            Box(stand, "Rebord avant", new Vector3(0f, top + lip / 2f, l / 2f), new Vector3(w, lip + 0.02f, 0.012f), wood, true);
            Box(stand, "Rebord arrière", new Vector3(0f, top + lip / 2f, -l / 2f), new Vector3(w, lip + 0.02f, 0.012f), wood, true);
            // Places : fléchettes couchées côte à côte, pointe vers la cible (avant du support).
            slots = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                var s = new GameObject("Place " + (i + 1)).transform;
                s.SetParent(stand, false);
                s.localPosition = new Vector3((i - 1) * 0.038f, top + 0.01f + 0.0045f, -0.0775f);
                s.localRotation = Quaternion.identity;
                slots[i] = s;
            }
            return stand;
        }

        static void Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        // ------------------------------------------------------------------ fléchette

        static GameObject DartPrefab(string color, Color c, Mesh mesh)
        {
            var go = new GameObject("Flechette_" + color);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[]
            {
                Lit("Flechette_Pointe", new Color(0.8f, 0.8f, 0.82f), 1f, 0.75f),
                Lit("Flechette_Corps", new Color(0.35f, 0.35f, 0.38f), 1f, 0.55f),
                Lit("Flechette_Tige", new Color(0.04f, 0.04f, 0.04f), 0f, 0.6f),
                Lit("Flechette_Ailette_" + color, c, 0f, 0.4f),
            };
            var col = go.AddComponent<CapsuleCollider>();
            col.direction = 2; col.radius = 0.004f; col.height = 0.155f; col.center = new Vector3(0f, 0f, 0.0775f);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.022f;
            rb.linearDamping = 0.05f; rb.angularDamping = 1.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.automaticCenterOfMass = false;
            rb.centerOfMass = new Vector3(0f, 0f, 0.095f);   // lest du corps en tungstène, vers la pointe
            GrabbableBuilder.MakeGrabbable(go);
            var grip = new SerializedObject(go.GetComponent<Fragile.Grab.GripSurface>());
            grip.FindProperty("m_LocalAxis").vector3Value = Vector3.forward;
            grip.FindProperty("m_EndMargin").floatValue = 0.01f;
            grip.ApplyModifiedPropertiesWithoutUndo();
            go.AddComponent<Fragile.Grab.InteractableHighlight>();
            go.AddComponent<Dart>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Folder}/{go.name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>Fléchette de 15,5 cm le long de +Z (origine à l'arrière) : pointe, corps, tige, ailettes croisées.</summary>
        static Mesh DartMesh()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
            var subs = new[] { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
            void Lathe(Vector2[] p, int sub)   // (rayon, z)
            {
                const int seg = 12;
                for (int i = 0; i < p.Length - 1; i++)
                {
                    var a = p[i]; var b = p[i + 1]; var d = b - a; if (d.sqrMagnitude < 1e-12f) continue;
                    var nn = new Vector2(-d.y, d.x).normalized;    // vers l'extérieur (on avance en z croissant)
                    if (nn.x < 0f) nn = -nn;
                    int start = v.Count;
                    for (int s = 0; s <= seg; s++)
                    {
                        float ang = s * Mathf.PI * 2f / seg; var dir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                        var normal = (dir * nn.x + Vector3.forward * nn.y).normalized;
                        v.Add(dir * a.x + Vector3.forward * a.y); n.Add(normal); uv.Add(new Vector2(s / (float)seg, 0f));
                        v.Add(dir * b.x + Vector3.forward * b.y); n.Add(normal); uv.Add(new Vector2(s / (float)seg, 1f));
                    }
                    for (int s = 0; s < seg; s++)
                    {
                        int i0 = start + s * 2;
                        Tri(subs[sub], i0, i0 + 2, i0 + 1); Tri(subs[sub], i0 + 1, i0 + 2, i0 + 3);
                    }
                }
            }
            void Tri(List<int> t, int a, int b, int c)
            {
                var face = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                if (face.sqrMagnitude < 1e-16f) return;
                if (Vector3.Dot(face, n[a] + n[b] + n[c]) < 0f) { t.Add(a); t.Add(c); t.Add(b); } else { t.Add(a); t.Add(b); t.Add(c); }
            }
            Lathe(new[] { new Vector2(0f, 0.026f), new Vector2(0.0019f, 0.026f), new Vector2(0.0019f, 0.063f) }, 2);                       // tige
            Lathe(new[] { new Vector2(0.0019f, 0.063f), new Vector2(0.0031f, 0.065f), new Vector2(0.0036f, 0.073f), new Vector2(0.0036f, 0.107f),
                          new Vector2(0.0029f, 0.113f), new Vector2(0.0013f, 0.116f) }, 1);                                                // corps
            Lathe(new[] { new Vector2(0.0013f, 0.116f), new Vector2(0.0011f, 0.150f), new Vector2(0f, 0.155f) }, 0);                       // pointe
            // Ailettes : deux plaques croisées, double face.
            foreach (var axis in new[] { Vector3.right, Vector3.up })
                foreach (float side in new[] { 1f, -1f })
                {
                    var o = axis * side;
                    var q = new[] { o * 0.0019f + Vector3.forward * 0.036f, o * 0.0019f + Vector3.forward * 0.004f, o * 0.017f + Vector3.forward * 0.0f, o * 0.017f + Vector3.forward * 0.022f };
                    var normal = Vector3.Cross(axis, Vector3.forward);
                    foreach (var nn in new[] { normal, -normal })
                    {
                        int k = v.Count;
                        foreach (var p in q) { v.Add(p); n.Add(nn); uv.Add(new Vector2(p.z * 20f, side)); }
                        Tri(subs[3], k, k + 1, k + 2); Tri(subs[3], k, k + 2, k + 3);
                    }
                }
            var m = new Mesh { subMeshCount = 4 };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
            for (int i = 0; i < 4; i++) m.SetTriangles(subs[i], i);
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ outils

        static Material Lit(string name, Color c, float metallic, float smoothness)
        {
            string path = $"{Folder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mesh SaveMesh(Mesh m, string path)
        {
            m.name = System.IO.Path.GetFileNameWithoutExtension(path);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(m, path); return m; }
            existing.Clear();
            existing.SetVertices(m.vertices); existing.SetNormals(m.normals); existing.SetUVs(0, m.uv);
            existing.subMeshCount = m.subMeshCount;
            for (int i = 0; i < m.subMeshCount; i++) existing.SetTriangles(m.GetTriangles(i), i);
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
