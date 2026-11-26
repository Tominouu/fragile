using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Fragile > Éclairer le Bar : éclairage complet de la pièce, entièrement précalculé (lightmaps + light probes + sonde de réflexion).
    /// Suspensions au-dessus du comptoir, bandeaux LED sous le plateau, corniche lumineuse, appliques murales, lueur des télés.
    /// Seule lumière temps réel : une directionnelle faible (Mixed / Subtractive) pour le modelé et l'ombre des objets mobiles.
    /// Relancer reconstruit l'éclairage à neuf, puis relance le calcul.
    /// </summary>
    public static class BarLightingBuilder
    {
        const string Folder = "Assets/Bar/Lighting";
        const string RootName = "Éclairage";

        // Comptoir : faces extérieures à x = ±2,63 / z = ±2,76, plateau débordant jusqu'à ±2,88 / ±3,01, ouverture au sud entre x = 0,4 et 1,6.
        const float LedX = 2.70f, LedZ = 2.83f, LedY = 1.085f;
        const float PendantY = 2.02f;   // bas de l'abat-jour

        static readonly Color k_Warm = new Color(1f, 0.84f, 0.66f);      // ~2700 K : suspensions, appliques
        static readonly Color k_Amber = new Color(1f, 0.62f, 0.30f);     // LED du comptoir
        static readonly Color k_Cove = new Color(1f, 0.72f, 0.46f);      // corniche
        static readonly Color k_Tv = new Color(0.45f, 0.85f, 0.75f);     // lueur des écrans

        static Transform s_Root;
        static readonly Dictionary<Material, List<CombineInstance>> s_Parts = new Dictionary<Material, List<CombineInstance>>();

        [MenuItem("Fragile/Éclairer le Bar")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            var room = GameObject.Find("Room");
            if (room == null) { Debug.LogError("[Fragile] Objet « Room » introuvable : ouvre Bar.unity."); return; }
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Bar", "Lighting");
            Lightmapping.Cancel();

            // Les luminaires se calent sur les dimensions réelles de la pièce (les murs peuvent être déplacés en scène).
            float xMin = BoundsOf("Wall_West").max.x, xMax = BoundsOf("Wall_East").min.x;
            float zMin = BoundsOf("Wall_South").max.z, zMax = BoundsOf("Wall_North").min.z;
            float top = BoundsOf("Ceiling").min.y;

            var old = room.transform.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var oldLight = GameObject.Find("Ceiling Light");
            if (oldLight != null) Undo.DestroyObjectImmediate(oldLight);

            s_Parts.Clear();
            s_Root = new GameObject(RootName).transform;
            s_Root.SetParent(room.transform, false);
            s_Root.position = Vector3.zero;

            var brass = Lit("Laiton", new Color(0.72f, 0.50f, 0.22f), 1f, 0.72f);
            var black = Lit("Metal_Noir", new Color(0.03f, 0.028f, 0.026f), 0.6f, 0.45f);
            var wood = Lit("Corniche_Bois", new Color(0.13f, 0.075f, 0.045f), 0f, 0.35f);
            var bulb = Glow("Ampoule", new Color(2.6f, 1.9f, 1.1f));
            var inner = Glow("Interieur_Abat_Jour", new Color(1.0f, 0.60f, 0.24f));
            var led = Glow("Led", new Color(1.9f, 1.0f, 0.36f));

            Pendants(top, brass, black, bulb, inner);
            CounterLeds(led);
            Cove(xMin, xMax, zMin, zMax, top, wood);
            Sconces(xMin, xMax, zMin, zMax, brass, bulb);
            // Plus de spots ronds encastrés au plafond (retirés à la demande) : le coin repas est éclairé par
            // les suspensions en laiton posées au-dessus des tables (voir Pendants).
            TvGlow();
            BuildFixtureMeshes();

            SetupStatics(room);
            SetupKeyLight();
            SetupProbes(xMin, xMax, zMin, zMax, top);
            SetupPipelines();
            SetupLightmapper();

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Lightmapping.bakeCompleted -= OnBaked;
            Lightmapping.bakeCompleted += OnBaked;
            Lightmapping.BakeAsync();
            Debug.Log("[Fragile] Éclairage construit, calcul des lightmaps lancé.");
        }

        static void OnBaked()
        {
            Lightmapping.bakeCompleted -= OnBaked;
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Fragile] Éclairage calculé : {LightmapSettings.lightmaps.Length} lightmap(s).");
        }

        // ------------------------------------------------------------------ luminaires

        /// <summary>
        /// Suspensions dômes en laiton : deux par côté du comptoir (flaques de lumière sur le marbre et les bouteilles)
        /// et une au-dessus de chaque table du mobilier. Lancer « Meubler le Bar » avant pour avoir celles des tables.
        /// </summary>
        static void Pendants(float top, Material brass, Material black, Material bulb, Material inner)
        {
            var dome = Lathe(new[] { new Vector2(0.030f, 0.17f), new Vector2(0.045f, 0.16f), new Vector2(0.055f, 0.12f), new Vector2(0.10f, 0.085f), new Vector2(0.138f, 0.04f), new Vector2(0.15f, 0f) }, false);
            var inside = Lathe(new[] { new Vector2(0.026f, 0.150f), new Vector2(0.050f, 0.115f), new Vector2(0.096f, 0.081f), new Vector2(0.134f, 0.037f), new Vector2(0.15f, 0f) }, true);
            var positions = new List<Vector3>();
            foreach (float s in new[] { -1f, 1f })
                foreach (float a in new[] { -1.25f, 1.25f })
                {
                    positions.Add(new Vector3(s * 2.38f, PendantY, a));
                    positions.Add(new Vector3(a, PendantY, s * 2.55f));
                }
            // Les mêmes au-dessus des tables du coin repas (posées par Fragile > Meubler le Bar).
            var furniture = GameObject.Find("Mobilier");
            if (furniture != null)
                foreach (Transform t in furniture.transform)
                    if (t.name == "Table" && t.TryGetComponent<Collider>(out var col))
                        positions.Add(new Vector3(col.bounds.center.x, PendantY, col.bounds.center.z));
            foreach (var p in positions)
            {
                Add(dome, p, Quaternion.identity, Vector3.one, brass);
                Add(inside, p, Quaternion.identity, Vector3.one, inner);
                Add(Prim(PrimitiveType.Sphere), p + Vector3.up * 0.045f, Quaternion.identity, Vector3.one * 0.07f, bulb);
                float h = top - (p.y + 0.17f);
                Add(Prim(PrimitiveType.Cylinder), p + Vector3.up * (0.17f + h / 2f), Quaternion.identity, new Vector3(0.008f, h / 2f, 0.008f), black);
                Add(Prim(PrimitiveType.Cylinder), new Vector3(p.x, top - 0.012f, p.z), Quaternion.identity, new Vector3(0.11f, 0.012f, 0.11f), brass);
                Spot("Suspension", p + Vector3.up * 0.03f, Vector3.down, k_Warm, 7f, 120f, 25f, 6f);
            }
        }

        /// <summary>Bandeaux LED ambrés sous le débord du plateau : lumière rasante sur les façades capitonnées.</summary>
        static void CounterLeds(Material led)
        {
            Strip(new Vector3(-LedX, LedY, -2.7f), new Vector3(-LedX, LedY, 2.7f), Vector3.left, led);
            Strip(new Vector3(LedX, LedY, -2.7f), new Vector3(LedX, LedY, 2.7f), Vector3.right, led);
            Strip(new Vector3(-2.6f, LedY, LedZ), new Vector3(2.6f, LedY, LedZ), Vector3.forward, led);
            Strip(new Vector3(-2.6f, LedY, -LedZ), new Vector3(0.35f, LedY, -LedZ), Vector3.back, led);
            Strip(new Vector3(1.7f, LedY, -LedZ), new Vector3(2.6f, LedY, -LedZ), Vector3.back, led);
        }

        static void Strip(Vector3 a, Vector3 b, Vector3 outward, Material led)
        {
            var dir = (b - a).normalized; float len = (b - a).magnitude; var mid = (a + b) / 2f;
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            Add(Prim(PrimitiveType.Cube), mid + Vector3.up * 0.008f, rot, new Vector3(0.014f, 0.01f, len), led);
            Area("LED comptoir", mid + outward * 0.03f, (Vector3.down - outward * 0.25f).normalized, dir, new Vector2(len, 0.05f), k_Amber, 12f);
        }

        /// <summary>Corniche en bois sombre tout autour de la pièce, LED cachée derrière : lèche le plafond et le haut des murs.</summary>
        static void Cove(float x0, float x1, float z0, float z1, float top, Material wood)
        {
            const float inset = 0.22f, h = 0.12f, t = 0.03f;
            float y = top - 0.26f;
            float cx = (x0 + x1) / 2f, cz = (z0 + z1) / 2f, lx = x1 - x0, lz = z1 - z0;
            void Side(Vector3 wallMid, Vector3 inward, float len)
            {
                var along = Vector3.Cross(Vector3.up, inward).normalized;
                var rot = Quaternion.LookRotation(along, Vector3.up);
                Box("Corniche", wallMid + inward * inset, rot, new Vector3(t, h, len), wood);                                     // bandeau
                Box("Corniche", wallMid + inward * (inset / 2f) - Vector3.up * (h / 2f), rot, new Vector3(inset, 0.02f, len), wood); // tablette
                Area("LED corniche", wallMid + inward * 0.12f - Vector3.up * 0.02f, (Vector3.up + inward * 0.35f).normalized, along, new Vector2(len - 0.5f, 0.06f), k_Cove, 20f);
            }
            Side(new Vector3(x0, y, cz), Vector3.right, lz);
            Side(new Vector3(x1, y, cz), Vector3.left, lz);
            Side(new Vector3(cx, y, z0), Vector3.forward, lx);
            Side(new Vector3(cx, y, z1), Vector3.back, lx);
        }

        /// <summary>Appliques cylindriques haut/bas : cônes de lumière réguliers sur les murs.</summary>
        static void Sconces(float x0, float x1, float z0, float z1, Material brass, Material bulb)
        {
            const float y = 1.75f, off = 0.10f;
            void One(Vector3 p, Vector3 inward)
            {
                p += inward * off;
                Add(Prim(PrimitiveType.Cylinder), p, Quaternion.identity, new Vector3(0.085f, 0.11f, 0.085f), brass);
                Add(Prim(PrimitiveType.Cube), p - inward * off * 0.6f, Quaternion.LookRotation(inward), new Vector3(0.05f, 0.09f, off), brass);
                foreach (float s in new[] { -1f, 1f })
                {
                    Add(Prim(PrimitiveType.Cylinder), p + Vector3.up * s * 0.1105f, Quaternion.identity, new Vector3(0.068f, 0.001f, 0.068f), bulb);
                    Spot("Applique", p + Vector3.up * s * 0.115f, (Vector3.up * s - inward * 0.12f).normalized, k_Warm, 1.9f, 78f, 30f, 4f);
                }
            }
            int nz = Mathf.Max(2, Mathf.RoundToInt((z1 - z0) / 3.3f)), nx = Mathf.Max(2, Mathf.RoundToInt((x1 - x0) / 3.3f));
            for (int i = 0; i < nz; i++)
            {
                float z = Mathf.Lerp(z0, z1, (i + 0.5f) / nz);
                One(new Vector3(x0, y, z), Vector3.right); One(new Vector3(x1, y, z), Vector3.left);
            }
            for (int i = 0; i < nx; i++)
            {
                float x = Mathf.Lerp(x0, x1, (i + 0.5f) / nx);
                One(new Vector3(x, y, z0), Vector3.forward); One(new Vector3(x, y, z1), Vector3.back);
            }
        }

        /// <summary>Lueur froide des écrans : contrepoint coloré aux tons chauds.</summary>
        static void TvGlow()
        {
            var tvs = GameObject.Find("Télés");
            if (tvs == null) return;
            foreach (Transform tv in tvs.transform)
                if (tv.name.StartsWith("Télé "))
                    Area("Lueur télé", tv.position + tv.forward * 0.03f, (tv.forward - Vector3.up * 0.2f).normalized, tv.right, new Vector2(1.1f, 0.6f), k_Tv, 1.1f);
        }

        // ------------------------------------------------------------------ réglages

        static void SetupStatics(GameObject room)
        {
            // Le comptoir a besoin d'UV de lightmap.
            var counter = GameObject.Find("Comptoir");
            if (counter != null)
            {
                var mesh = counter.GetComponentInChildren<MeshFilter>().sharedMesh;
                if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh)) is ModelImporter imp && !imp.generateSecondaryUV)
                {
                    imp.generateSecondaryUV = true;
                    imp.SaveAndReimport();
                }
                foreach (var r in counter.GetComponentsInChildren<MeshRenderer>())
                {
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, GameObjectUtility.GetStaticEditorFlags(r.gameObject) | StaticEditorFlags.ContributeGI);
                    r.receiveGI = ReceiveGI.Lightmaps;
                    r.scaleInLightmap = 1.6f;
                }
            }
            // Les matériaux de verre convertis depuis Lit transparent ont leur passe d'ombre coupée.
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Bar" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || m.shader.name != "Fragile/Verre" || m.GetShaderPassEnabled("ShadowCaster")) continue;
                m.SetShaderPassEnabled("ShadowCaster", true);
                EditorUtility.SetDirty(m);
            }
            // La directionnelle « du dessus » doit entrer dans la pièce.
            foreach (var n in new[] { "Ceiling", "Wall_North", "Wall_South", "Wall_East", "Wall_West" })
            {
                var go = GameObject.Find(n);
                if (go != null) go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            foreach (var r in room.GetComponentsInChildren<MeshRenderer>())
                if ((GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.ContributeGI) != 0)
                    r.receiveGI = ReceiveGI.Lightmaps;
        }

        static void SetupKeyLight()
        {
            var go = GameObject.Find("Directional Light");
            if (go == null) { go = new GameObject("Directional Light"); go.AddComponent<Light>(); }
            var l = go.GetComponent<Light>();
            l.type = LightType.Directional;
            l.lightmapBakeType = LightmapBakeType.Mixed;
            l.color = new Color(1f, 0.90f, 0.78f);
            l.intensity = 0.55f;
            l.bounceIntensity = 0f;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.9f;
            l.shadowAngle = 35f;   // pénombre large au bake (pas de coupure nette de la corniche sur les murs)
            go.transform.rotation = Quaternion.Euler(70f, 25f, 0f);
            RenderSettings.sun = l;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.02f, 0.016f, 0.013f);
            RenderSettings.subtractiveShadowColor = new Color(0.10f, 0.07f, 0.05f);
            RenderSettings.fog = false;
            RenderSettings.reflectionIntensity = 1f;
        }

        static void SetupProbes(float x0, float x1, float z0, float z1, float top)
        {
            var go = new GameObject("Light Probes");
            go.transform.SetParent(s_Root, false);
            var group = go.AddComponent<LightProbeGroup>();
            var pts = new List<Vector3>();
            int nx = Mathf.CeilToInt((x1 - x0 - 0.8f) / 1.2f), nz = Mathf.CeilToInt((z1 - z0 - 0.8f) / 1.2f);
            foreach (float y in new[] { 0.15f, 0.95f, 1.35f, 1.9f, top - 0.35f })
                for (int i = 0; i <= nx; i++)
                    for (int k = 0; k <= nz; k++)
                    {
                        var p = new Vector3(Mathf.Lerp(x0 + 0.4f, x1 - 0.4f, i / (float)nx), y, Mathf.Lerp(z0 + 0.4f, z1 - 0.4f, k / (float)nz));
                        if (!Physics.CheckSphere(p, 0.05f, ~0, QueryTriggerInteraction.Ignore)) pts.Add(p);
                    }
            group.probePositions = pts.ToArray();

            var probe = Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Exclude).FirstOrDefault();
            if (probe == null) { probe = new GameObject("Sonde de réflexion").AddComponent<ReflectionProbe>(); probe.transform.SetParent(s_Root, false); }
            probe.mode = ReflectionProbeMode.Baked;
            probe.transform.position = new Vector3(0f, 1.75f, 0f);
            probe.center = new Vector3((x0 + x1) / 2f, top / 2f, (z0 + z1) / 2f) - probe.transform.position;
            probe.size = new Vector3(x1 - x0 + 0.2f, top + 0.2f, z1 - z0 + 0.2f);
            probe.boxProjection = true;
            probe.hdr = true;
            probe.resolution = 256;
            probe.intensity = 1f;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = Color.black;
        }

        /// <summary>Projection en boîte de la sonde (reflets à leur place dans la pièce) sur les deux réglages URP.</summary>
        static void SetupPipelines()
        {
            // Le niveau « Low » (éditeur et Quest) coupait toutes les ombres.
            if (QualitySettings.shadows == UnityEngine.ShadowQuality.Disable) QualitySettings.shadows = UnityEngine.ShadowQuality.All;
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets/Settings" }))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                var so = new SerializedObject(asset);
                so.FindProperty("m_ReflectionProbeBoxProjection").boolValue = true;
                so.FindProperty("m_MixedLightingSupported").boolValue = true;
                var dist = so.FindProperty("m_ShadowDistance");
                if (dist.floatValue < 4f) dist.floatValue = 4f;   // ombres des bouteilles sur le comptoir voisin
                var res = so.FindProperty("m_MainLightShadowmapResolution");
                if (res.intValue > 2048) res.intValue = 2048;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
        }

        static void SetupLightmapper()
        {
            string path = $"{Folder}/Bar_Lightmapping.lighting";
            var s = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (s == null) { s = new LightingSettings(); AssetDatabase.CreateAsset(s, path); }
            s.bakedGI = true;
            s.realtimeGI = false;
            s.mixedBakeMode = MixedLightingMode.Subtractive;
            s.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            s.directionalityMode = LightmapsMode.CombinedDirectional;   // garde le relief des normal maps (capitonnage, parquet)
            s.lightmapResolution = 40f;
            s.lightmapMaxSize = 1024;
            s.lightmapPadding = 4;
            s.lightmapCompression = LightmapCompression.HighQuality;
            s.directSampleCount = 512;
            s.indirectSampleCount = 2048;
            s.environmentSampleCount = 64;
            s.maxBounces = 4;
            s.indirectScale = 0.85f;
            s.ao = true; s.aoMaxDistance = 0.4f; s.aoExponentDirect = 0.3f; s.aoExponentIndirect = 1f;
            s.filteringMode = LightingSettings.FilterMode.Auto;
            EditorUtility.SetDirty(s);
            Lightmapping.lightingSettings = s;
        }

        // ------------------------------------------------------------------ outils

        static Bounds BoundsOf(string name) => GameObject.Find(name).GetComponent<Renderer>().bounds;

        static Light NewLight(string name, Vector3 pos, Color c, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(s_Root, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.color = c; l.intensity = intensity;
            l.shadows = LightShadows.Soft;
            l.lightmapBakeType = LightmapBakeType.Baked;
            return l;
        }

        static void Spot(string name, Vector3 pos, Vector3 dir, Color c, float intensity, float outer, float innerAngle, float range)
        {
            var l = NewLight(name, pos, c, intensity);
            l.type = LightType.Spot;
            l.spotAngle = outer; l.innerSpotAngle = innerAngle; l.range = range;
            l.shapeRadius = 0.05f;
            l.transform.rotation = Quaternion.FromToRotation(Vector3.forward, dir);
        }

        static void Area(string name, Vector3 pos, Vector3 dir, Vector3 along, Vector2 size, Color c, float intensity)
        {
            var l = NewLight(name, pos, c, intensity);
            l.type = LightType.Rectangle;
            l.areaSize = size;                       // x = le long de « along »
            l.range = 8f;
            l.lightmapBakeType = LightmapBakeType.Baked;
            l.transform.rotation = Quaternion.LookRotation(dir, Vector3.Cross(dir, along));
        }

        static void Box(string name, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(s_Root, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.scaleInLightmap = 1f;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
        }

        static Mesh Prim(PrimitiveType t) => Resources.GetBuiltinResource<Mesh>(t + ".fbx");

        static void Add(Mesh m, Vector3 p, Quaternion r, Vector3 s, Material mat)
        {
            if (!s_Parts.TryGetValue(mat, out var list)) s_Parts[mat] = list = new List<CombineInstance>();
            list.Add(new CombineInstance { mesh = m, transform = Matrix4x4.TRS(p, r, s) });
        }

        /// <summary>Un seul maillage par matériau : tous les luminaires coûtent quelques appels de rendu.</summary>
        static void BuildFixtureMeshes()
        {
            foreach (var kv in s_Parts)
            {
                string path = $"{Folder}/Luminaires_{kv.Key.name}.asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
                mesh.Clear();
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);

                var go = new GameObject("Luminaires " + kv.Key.name);
                go.transform.SetParent(s_Root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = kv.Key;
                r.shadowCastingMode = ShadowCastingMode.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ReflectionProbeStatic);
            }
        }

        /// <summary>Solide de révolution autour de Y (profil de haut en bas).</summary>
        static Mesh Lathe(Vector2[] profile, bool inward)
        {
            const int seg = 24;
            var v = new List<Vector3>(); var tri = new List<int>(); var uv = new List<Vector2>();
            for (int i = 0; i < profile.Length; i++)
                for (int s = 0; s <= seg; s++)
                {
                    float a = s * Mathf.PI * 2f / seg;
                    v.Add(new Vector3(Mathf.Cos(a) * profile[i].x, profile[i].y, Mathf.Sin(a) * profile[i].x));
                    uv.Add(new Vector2(s / (float)seg, i / (float)(profile.Length - 1)));
                }
            for (int i = 0; i < profile.Length - 1; i++)
                for (int s = 0; s < seg; s++)
                {
                    int a = i * (seg + 1) + s, b = a + 1, c = a + seg + 1, d = c + 1;
                    tri.AddRange(inward ? new[] { a, c, b, b, c, d } : new[] { a, b, c, b, d, c });
                }
            var m = new Mesh();
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateTangents();
            return m;
        }

        static Material GetMat(string name, string shader)
        {
            string path = $"{Folder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Lit(string name, Color c, float metallic, float smoothness)
        {
            var m = GetMat(name, "Universal Render Pipeline/Lit");
            m.color = c; m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        static Material Glow(string name, Color c)
        {
            var m = GetMat(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", c);
            return m;
        }
    }
}
