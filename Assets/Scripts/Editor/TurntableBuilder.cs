using System.Collections.Generic;
using System.Linq;
using Fragile.Music;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Fragile > Animer la platine vinyle : branche le bouton marche/arrêt, la molette de volume, le disque,
    /// le bras, l'égaliseur et le son sur le modèle de platine de la scène. Relançable (repart à neuf).
    /// </summary>
    public static class TurntableBuilder
    {
        const string Root = "Vinyl_Record_Player_Retro_Turntable_002";
        const string Part = "Vinyl Record Player Retro Turntable.";
        const string Folder = "Assets/Bar/Vinyle";
        const string Song = "Assets/Bar/Audio/What_To_Do_AndME_Remix.mp3";

        // Panneau incliné à 45°, dans le repère de la platine : T monte le long du panneau, N en sort.
        static readonly Vector3 N = new Vector3(0f, 1f, 1f).normalized, T = new Vector3(0f, 1f, -1f).normalized;

        [MenuItem("Fragile/Animer la platine vinyle")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            var root = GameObject.Find(Root);
            if (root == null) { Debug.LogError($"[Fragile] « {Root} » introuvable dans la scène."); return; }
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Bar", "Vinyle");
            var rt = root.transform;

            Transform P(string id) => rt.Find(Part + id);
            var body = P("002"); var record = P("003"); var knob = P("004"); var buttons = P("006"); var arm = P("007");

            // Repart à neuf.
            var oldTable = root.GetComponent<Turntable>();
            if (oldTable != null) Object.DestroyImmediate(oldTable);
            foreach (var old in new[] { rt.Find("Interaction"), record.Find("Étiquette"), knob.Find("Repère") })
                if (old != null) Object.DestroyImmediate(old.gameObject);

            var inter = Child("Interaction", rt, Vector3.zero, Quaternion.identity);
            var panelRot = Quaternion.LookRotation(N, T);

            // Corps : on peut poser des objets dessus.
            var bb = LocalBounds(rt, body);
            var bodyCol = Child("Collision", inter, Vector3.zero, Quaternion.identity).gameObject.AddComponent<BoxCollider>();
            bodyCol.center = bb.center - new Vector3(0f, 0.005f, 0.03f);
            bodyCol.size = bb.size - new Vector3(0f, 0.015f, 0.06f);

            // ---------------- boutons : le maillage fusionné est coupé en trois touches
            var steel = buttons.GetComponent<MeshRenderer>().sharedMaterial;
            buttons.GetComponent<MeshRenderer>().enabled = false;
            var ledOff = Lit("Bouton_Veille", new Color(0.55f, 0.12f, 0.06f), new Color(0.55f, 0.07f, 0.03f));
            var ledOn = Lit("Bouton_Marche", new Color(0.15f, 0.6f, 0.2f), new Color(0.12f, 1.0f, 0.25f));
            var caps = SplitButtons(rt, buttons);
            PanelButton play = null; Renderer led = null;
            for (int i = 0; i < caps.Count; i++)
            {
                var (mesh, b) = caps[i];
                float w = b.size.x, h = b.size.y / 0.7071f; // dimensions dans le plan du panneau
                var btn = Child("Bouton " + (i + 1), inter, b.center, panelRot);
                var cap = Child("Touche", btn, Vector3.zero, Quaternion.identity);
                cap.SetPositionAndRotation(buttons.position, buttons.rotation);
                cap.localScale = Vector3.one * (buttons.lossyScale.x / btn.lossyScale.x);
                cap.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = cap.gameObject.AddComponent<MeshRenderer>();
                r.sharedMaterial = i == 0 ? ledOff : steel;
                if (i != 0) continue;   // une seule touche active pour l'instant : celle à côté de l'afficheur
                var col = btn.gameObject.AddComponent<BoxCollider>();
                col.size = new Vector3(w, h, 0.02f);
                play = btn.gameObject.AddComponent<PanelButton>();
                play.Setup(cap, new Vector2(w * 0.5f + 0.006f, h * 0.5f + 0.006f), 0.005f);
                play.colliders.Add(col);
                led = r;
            }

            // ---------------- molette de volume (la première)
            var kb = LocalBounds(rt, knob);
            var knobT = Child("Molette volume", inter, kb.center, panelRot);
            var sphere = knobT.gameObject.AddComponent<SphereCollider>();
            sphere.radius = 0.034f;
            var volume = knobT.gameObject.AddComponent<PanelKnob>();
            volume.Setup(knob, 0.6f);
            volume.colliders.Add(sphere);
            var mark = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            mark.name = "Repère";
            Object.DestroyImmediate(mark.GetComponent<Collider>());
            mark.SetParent(knobT, false);
            mark.localPosition = new Vector3(0f, 0.018f, Points(rt, knob).Max(p => Vector3.Dot(p - kb.center, N)));
            mark.localScale = new Vector3(0.004f, 0.018f, 0.002f);
            mark.GetComponent<MeshRenderer>().sharedMaterial = Lit("Repere", new Color(0.8f, 0.12f, 0.06f), Color.black);
            mark.SetParent(knob, true);

            // ---------------- disque : étiquette imprimée, pour voir qu'il tourne
            var rb = LocalBounds(rt, record);
            var label = Child("Étiquette", record, Vector3.zero, Quaternion.identity);
            label.rotation = rt.rotation;
            label.position = rt.TransformPoint(new Vector3(rb.center.x, rb.max.y + 0.0006f, rb.center.z));
            label.localScale = Vector3.one * (rt.lossyScale.x / record.lossyScale.x);
            label.gameObject.AddComponent<MeshFilter>().sharedMesh = LabelMesh(0.010f, 0.074f);
            label.gameObject.AddComponent<MeshRenderer>().sharedMaterial = LabelMaterial();

            // ---------------- bras : pivot = l'extrémité arrière, tête = l'autre bout
            var armPts = Points(rt, arm);
            float zMin = armPts.Min(p => p.z), zMax = armPts.Max(p => p.z);
            var pivot = Center(armPts.Where(p => p.z < zMin + 0.06f).ToList());
            var head = Center(armPts.Where(p => p.z > zMax - 0.05f).ToList());
            var center = rb.center;
            float L = Flat(head - pivot).magnitude, D = Flat(center - pivot).magnitude, R = rb.extents.x;
            // Angle (bras / centre du disque) qui amène la tête à une distance donnée du centre.
            float Open(float groove) => Mathf.Acos(Mathf.Clamp((L * L + D * D - groove * groove) / (2f * L * D), -1f, 1f)) * Mathf.Rad2Deg;
            float rest = Vector3.SignedAngle(Flat(center - pivot), Flat(head - pivot), Vector3.up);
            float sign = Mathf.Sign(rest);
            float outer = sign * Open(R * 0.93f) - rest, inner = sign * Open(R * 0.38f) - rest;

            // ---------------- égaliseur : une barre lumineuse dans chacune des 7 fentes (graves à gauche)
            var bars = new Transform[7];
            for (int i = 0; i < 7; i++)
            {
                float x = -3.2705f - i * 0.0227f;
                var holder = Child("Barre " + (i + 1), inter, new Vector3(x, 0f, 0f) + T * -0.024f + N * 0.169f, panelRot);
                var q = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.name = "Lumière";
                q.SetParent(holder, false);
                q.localPosition = new Vector3(0f, 0.5f, 0f);
                holder.localScale = new Vector3(0.0085f, 0.043f, 0.003f);
                var r = q.GetComponent<MeshRenderer>();
                r.sharedMaterial = Glow("Egaliseur_" + (i + 1), Color.Lerp(new Color(0.15f, 1.3f, 0.6f), new Color(1.5f, 0.55f, 0.1f), i / 6f));
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bars[i] = holder;
            }

            // ---------------- son
            var clip = ImportSong();
            var music = Source("Musique", inter, center, clip);
            var crackle = Source("Craquements", inter, center, null);

            var table = root.AddComponent<Turntable>();
            table.Setup(music, crackle, record, arm, pivot, head, outer, inner, bars, led, ledOff, ledOn);
            UnityEventTools.AddPersistentListener(play.pressed, table.Toggle);
            UnityEventTools.AddPersistentListener(volume.valueChanged, table.SetVolume);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            Debug.Log($"[Fragile] Platine animée : {caps.Count} touches, bras {outer:F1}° -> {inner:F1}° (L={L:F3}, D={D:F3}, R={R:F3}), son « {(clip != null ? clip.name : "aucun")} ».");
        }

        // ------------------------------------------------------------------ géométrie

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static List<Vector3> Points(Transform root, Transform part) =>
            part.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v => root.InverseTransformPoint(part.TransformPoint(v))).ToList();

        static Bounds BoundsOf(IList<Vector3> pts)
        {
            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);
            return b;
        }

        static Bounds LocalBounds(Transform root, Transform part) => BoundsOf(Points(root, part));
        static Vector3 Center(List<Vector3> pts) => BoundsOf(pts).center;

        /// <summary>Sépare les trois touches (groupes de sommets le long de x), de la gauche vers la droite vu de face.</summary>
        static List<(Mesh, Bounds)> SplitButtons(Transform root, Transform part)
        {
            var src = part.GetComponent<MeshFilter>().sharedMesh;
            var v = src.vertices; var nrm = src.normals; var uv = src.uv; var tan = src.tangents; var tri = src.triangles;
            var local = Points(root, part);
            float x0 = local.Min(p => p.x), x1 = local.Max(p => p.x);
            var result = new List<(Mesh, Bounds)>();
            for (int k = 0; k < 3; k++)
            {
                float hi = Mathf.Lerp(x1, x0, k / 3f), lo = Mathf.Lerp(x1, x0, (k + 1) / 3f);
                var map = new Dictionary<int, int>(); var t = new List<int>();
                for (int i = 0; i < tri.Length; i += 3)
                {
                    float x = (local[tri[i]].x + local[tri[i + 1]].x + local[tri[i + 2]].x) / 3f;
                    if (x > hi || x <= lo) continue;
                    for (int j = 0; j < 3; j++)
                    {
                        int id = tri[i + j];
                        if (!map.TryGetValue(id, out int n)) map[id] = n = map.Count;
                        t.Add(n);
                    }
                }
                var order = map.OrderBy(m => m.Value).Select(m => m.Key).ToArray();
                var mesh = LoadMesh($"{Folder}/Touche_{k + 1}.asset");
                mesh.SetVertices(order.Select(o => v[o]).ToList());
                mesh.SetNormals(order.Select(o => nrm[o]).ToList());
                if (uv.Length == v.Length) mesh.SetUVs(0, order.Select(o => uv[o]).ToList());
                if (tan.Length == v.Length) mesh.SetTangents(order.Select(o => tan[o]).ToList());
                mesh.SetTriangles(t, 0);
                mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);
                result.Add((mesh, BoundsOf(order.Select(o => local[o]).ToList())));
            }
            return result;
        }

        static Mesh LabelMesh(float r0, float r1)
        {
            const int seg = 48;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                foreach (float r in new[] { r0, r1 }) { v.Add(d * r); uv.Add(new Vector2(0.5f + d.x * r / r1 * 0.5f, 0.5f + d.z * r / r1 * 0.5f)); }
                if (i == seg) continue;
                int k = i * 2;
                t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
            }
            var m = LoadMesh($"{Folder}/Etiquette_Maillage.asset");
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.SetNormals(Enumerable.Repeat(Vector3.up, v.Count).ToList());
            m.RecalculateBounds();
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mesh LoadMesh(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m == null) { m = new Mesh(); AssetDatabase.CreateAsset(m, path); }
            m.Clear();
            return m;
        }

        // ------------------------------------------------------------------ matériaux et son

        static Material Mat(string name, string shader)
        {
            string path = $"{Folder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Lit(string name, Color c, Color emission)
        {
            var m = Mat(name, "Universal Render Pipeline/Lit");
            m.color = c; m.SetFloat("_Smoothness", 0.5f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetColor("_EmissionColor", emission);
            return m;
        }

        static Material Glow(string name, Color c)
        {
            var m = Mat(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", c);
            return m;
        }

        static Material LabelMaterial()
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGB24, false);
            var blue = new Color(0.10f, 0.17f, 0.42f); var cream = new Color(0.93f, 0.86f, 0.70f); var red = new Color(0.78f, 0.16f, 0.10f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, w = (y + 0.5f) / n * 2f - 1f, r = Mathf.Sqrt(u * u + w * w);
                    var c = w > 0.1f ? cream : blue;                                  // deux moitiés : la rotation se lit de loin
                    if (Mathf.Abs(w - 0.1f) < 0.05f) c = red;
                    if (w > 0.35f && w < 0.5f && Mathf.Abs(u) < 0.4f) c = blue;        // « titre »
                    if (w < -0.3f && w > -0.36f && Mathf.Abs(u) < 0.45f) c = cream;    // « lignes »
                    if (w < -0.45f && w > -0.5f && Mathf.Abs(u) < 0.3f) c = cream;
                    if (r > 0.9f) c = red;
                    if (r < 0.2f) c = cream;
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            string path = $"{Folder}/Etiquette.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var m = Mat("Etiquette", "Universal Render Pipeline/Lit");
            m.color = Color.white;
            m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            m.SetFloat("_Smoothness", 0.25f);
            return m;
        }

        static AudioClip ImportSong()
        {
            if (AssetImporter.GetAtPath(Song) is AudioImporter imp)
            {
                var s = imp.defaultSampleSettings;
                s.loadType = AudioClipLoadType.Streaming;       // long morceau : lu au fil de l'eau, presque pas de mémoire
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.6f;
                s.preloadAudioData = false;
                imp.defaultSampleSettings = s;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(Song);
        }

        static AudioSource Source(string name, Transform parent, Vector3 pos, AudioClip clip)
        {
            var s = Child(name, parent, pos, Quaternion.identity).gameObject.AddComponent<AudioSource>();
            s.clip = clip;
            s.playOnAwake = false;
            s.loop = true;
            s.spatialBlend = 0.85f;          // situé dans la pièce, mais remplit un peu le bar
            s.spread = 60f;
            s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = 1.5f; s.maxDistance = 30f;
            return s;
        }

        static Transform Child(string name, Transform parent, Vector3 pos, Quaternion rot)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos; t.localRotation = rot;
            return t;
        }
    }
}
