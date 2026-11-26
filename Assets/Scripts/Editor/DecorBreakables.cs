using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fragile.Breakables;
using TMPro;
using UnityEditor;
using UnityEngine;
using Matter = Fragile.Breakables.Breakable.Matter;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Rend cassable un objet du décor (mobilier, appareils, platine) avec <see cref="Breakable"/>, à la manière de sa matière :
    /// - bois : longues échardes épaisses (découpe étirée dans le sens de la plus grande longueur), entailles ;
    /// - métal : quelques gros morceaux cabossés et lourds, étincelles, bosses ;
    /// - plastique : morceaux trapus, fissures blanches ; céramique : éclats fins, fêlures.
    /// Il faut plusieurs coups (selon l'objet) : chacun laisse une marque, jusqu'à la casse.
    /// Toutes les pièces sont fusionnées en un maillage pré-découpé (3 niveaux), partagé entre objets identiques (même clé).
    /// L'objet a un Rigidbody cinématique : il ne bouge pas et garde ses lightmaps.
    /// </summary>
    public static class DecorBreakables
    {
        const string Folder = "Assets/Bar/Casse";
        static readonly Dictionary<string, (Breakable.ShardSet[] levels, Material[] mats)> s_Cache = new Dictionary<string, (Breakable.ShardSet[], Material[])>();

        struct Style
        {
            public int[] counts; public float thickMin, thickMax, stretch, crumple, burst, toughness, density;
            /// <summary>Par niveau : (part du volume au-delà de laquelle une pièce est fendue, nombre de morceaux).</summary>
            public (float share, int pieces)[] split;
        }

        static Style StyleOf(Matter m) => m switch
        {
            // burst faible : les morceaux tombent lourdement au lieu d'exploser comme du verre.
            // Bois : dès le premier niveau, plateaux, montants et coussins sont fendus (3 à 5 morceaux) :
            // ce sont ces fentes qui s'ouvrent coup après coup.
            Matter.Wood => new Style { counts = new[] { 4, 8, 14 }, thickMin = 0.015f, thickMax = 0.04f, stretch = 0.35f, crumple = 0f, burst = 0.35f, toughness = 2.4f, density = 180f,
                split = new[] { (0.06f, 4), (0.04f, 5), (0.02f, 6) } },
            Matter.Metal => new Style { counts = new[] { 3, 5, 8 }, thickMin = 0.01f, thickMax = 0.02f, stretch = 1f, crumple = 0.018f, burst = 0.25f, toughness = 2.8f, density = 260f,
                split = new[] { (0.35f, 2), (0.18f, 3), (0.08f, 4) } },
            Matter.Plastic => new Style { counts = new[] { 3, 6, 10 }, thickMin = 0.008f, thickMax = 0.015f, stretch = 1f, crumple = 0.005f, burst = 0.5f, toughness = 2.2f, density = 120f,
                split = new[] { (0.2f, 2), (0.12f, 3), (0.06f, 4) } },
            _ => new Style { counts = new[] { 6, 12, 20 }, thickMin = 0.004f, thickMax = 0.01f, stretch = 1f, crumple = 0f, burst = 1f, toughness = 1.8f, density = 400f },
        };

        /// <summary>À appeler au début d'un passage d'outil : les éclats seront recalculés (modèles peut-être modifiés).</summary>
        public static void ResetCache() => s_Cache.Clear();

        /// <param name="key">Identifiant partagé par les objets identiques (ex. nom du modèle).</param>
        /// <param name="hits">Nombre de coups francs avant la casse (1 = au premier coup).</param>
        /// <param name="dynamic">Objet libre (tombe si son support casse), sinon immobile.</param>
        public static void Make(GameObject go, string key, Matter matter, float hits, bool dynamic = false, bool liquid = false, Color? liquidColor = null)
        {
            var style = StyleOf(matter);
            if (!s_Cache.TryGetValue(key, out var data))
            {
                data = Build(go, key, matter, style);
                if (data.levels == null) return;
                s_Cache[key] = data;
            }

            if (go.GetComponentInChildren<Collider>() == null)
            {
                var col = go.AddComponent<BoxCollider>();
                var b = LocalBounds(go); col.center = b.center; col.size = Vector3.Max(b.size, Vector3.one * 0.02f);
            }
            if (!go.TryGetComponent<Rigidbody>(out var rb)) rb = go.AddComponent<Rigidbody>();
            var size = Vector3.Scale(LocalBounds(go).size, go.transform.lossyScale);
            rb.mass = Mathf.Clamp(size.x * size.y * size.z * style.density, 0.3f, 60f);
            rb.isKinematic = !dynamic;
            rb.interpolation = dynamic ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            rb.collisionDetectionMode = dynamic ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.Discrete;
            // Il tremble sous les coups : pas de « Batching Static » (il garderait sa place). Il reste éclairé par les lightmaps.
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, dynamic ? 0 : flags & ~StaticEditorFlags.BatchingStatic);
            }

            var br = go.GetComponent<Breakable>() ?? go.AddComponent<Breakable>();
            var so = new SerializedObject(br);
            var levelsProp = so.FindProperty("m_Levels"); levelsProp.arraySize = data.levels.Length;
            for (int l = 0; l < data.levels.Length; l++)
            {
                var lp = levelsProp.GetArrayElementAtIndex(l);
                var meshes = lp.FindPropertyRelative("meshes"); meshes.arraySize = data.levels[l].meshes.Length;
                var offsets = lp.FindPropertyRelative("offsets"); offsets.arraySize = data.levels[l].offsets.Length;
                for (int i = 0; i < data.levels[l].meshes.Length; i++)
                {
                    meshes.GetArrayElementAtIndex(i).objectReferenceValue = data.levels[l].meshes[i];
                    offsets.GetArrayElementAtIndex(i).vector3Value = data.levels[l].offsets[i];
                }
            }
            var matProp = so.FindProperty("m_ShardMaterials"); matProp.arraySize = data.mats.Length;
            for (int i = 0; i < data.mats.Length; i++) matProp.GetArrayElementAtIndex(i).objectReferenceValue = data.mats[i];
            so.FindProperty("m_MeshTransform").objectReferenceValue = go.transform;
            so.FindProperty("m_FxPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/FX/BreakFX.prefab");
            so.FindProperty("m_Mass").floatValue = rb.mass;
            so.FindProperty("m_Matter").enumValueIndex = (int)matter;
            so.FindProperty("m_Hits").floatValue = hits;
            so.FindProperty("m_Burst").floatValue = style.burst;
            so.FindProperty("m_DamageMaterial").objectReferenceValue = hits > 1f ? DamageMaterial(matter) : null;
            so.FindProperty("m_SparkMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Bar/TV/TV_Etincelle.mat");
            // Seule la batte (ou un objet lancé très fort) abîme le décor.
            so.FindProperty("m_BreakSpeedWithBreaker").floatValue = style.toughness;
            so.FindProperty("m_BreakSpeedAny").floatValue = dynamic ? 3.5f : 11f;
            so.FindProperty("m_MaxImpactSpeed").floatValue = style.toughness + 7f;
            // Les débris du décor restent au sol (les pièces d'un meuble ne disparaissent pas comme des miettes de verre).
            so.FindProperty("m_PiecesLeftOnGround").intValue = matter == Matter.Ceramic ? 6 : 40;
            so.FindProperty("m_HitStop").floatValue = 0.08f;
            so.FindProperty("m_Liquid").boolValue = liquid;
            if (liquidColor.HasValue) so.FindProperty("m_LiquidColor").colorValue = liquidColor.Value;
            var (c1, c2) = Chips(matter);
            so.FindProperty("m_ChipColor").colorValue = c1;
            so.FindProperty("m_ChipColor2").colorValue = c2;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ découpe

        static (Breakable.ShardSet[] levels, Material[] mats) Build(GameObject go, string key, Matter matter, Style style)
        {
            // 1) Toutes les pièces visibles fusionnées, dans le repère de l'objet.
            var combine = new List<CombineInstance>();
            var mats = new List<Material>();
            var toRoot = go.transform.worldToLocalMatrix;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.GetComponent<TMP_Text>() != null || r.GetComponentInParent<GlassPane>() != null) continue;
                if (!r.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                for (int s = 0; s < mesh.subMeshCount && s < r.sharedMaterials.Length; s++)
                {
                    combine.Add(new CombineInstance { mesh = mesh, subMeshIndex = s, transform = toRoot * r.localToWorldMatrix });
                    mats.Add(r.sharedMaterials[s]);
                }
            }
            if (combine.Count == 0) { Debug.LogWarning($"[Fragile] {go.name} : rien à casser."); return (null, null); }
            var merged = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            merged.CombineMeshes(combine.ToArray(), false, true);
            if (merged.normals == null || merged.normals.Length != merged.vertexCount) merged.RecalculateNormals();

            var bs = merged.bounds.size;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Bar", "Casse");

            // Bois, métal, plastique : l'objet se démonte en ses vraies pièces (pieds, assise, panneaux, porte...),
            // les plus grosses fendues en quelques morceaux épais. Pas de pluie d'éclats comme du verre.
            if (matter != Matter.Ceramic)
            {
                var pieceLevels = PieceLevels(merged, key, style);
                Object.DestroyImmediate(merged);
                mats.Add(InnerMaterial(matter));
                return (pieceLevels, mats.ToArray());
            }

            // Céramique : éclats (découpe de Voronoï), comme un vrai pot.
            var bs2 = bs;
            var stretch = Vector3.one;
            if (style.stretch < 1f)
            {
                int axis = bs.x >= bs.y && bs.x >= bs.z ? 0 : bs.y >= bs.z ? 1 : 2;
                stretch[axis] = style.stretch;
            }
            var work = Transformed(merged, stretch);
            float thickness = Mathf.Clamp(bs2.magnitude * 0.025f, style.thickMin, style.thickMax);

            string path = $"{Folder}/{key}_Eclats.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            var levels = new Breakable.ShardSet[style.counts.Length];
            bool first = true;
            var inv = new Vector3(1f / stretch.x, 1f / stretch.y, 1f / stretch.z);
            for (int l = 0; l < style.counts.Length; l++)
            {
                var shards = BreakableBuilder.Fracture(work, style.counts[l], thickness, key.GetHashCode() + l * 7919);
                var meshes = new Mesh[shards.Count]; var offsets = new Vector3[shards.Count];
                for (int i = 0; i < shards.Count; i++)
                {
                    // Retour aux vraies proportions, puis cabossage (métal, plastique).
                    var m = shards[i].mesh;
                    var off = Vector3.Scale(shards[i].offset, inv);
                    Untransform(m, inv);
                    if (style.crumple > 0f) Crumple(m, off, style.crumple * Mathf.Clamp(bs.magnitude, 0.3f, 2f), key.GetHashCode() + i);
                    m.name = $"{key} niveau {l} éclat {i}";
                    meshes[i] = m; offsets[i] = off;
                    if (first) { AssetDatabase.CreateAsset(m, path); first = false; }
                    else AssetDatabase.AddObjectToAsset(m, path);
                }
                levels[l] = new Breakable.ShardSet { meshes = meshes, offsets = offsets };
            }
            AssetDatabase.ImportAsset(path);
            Object.DestroyImmediate(merged); Object.DestroyImmediate(work);
            mats.Add(InnerMaterial(matter));
            return (levels, mats.ToArray());
        }

        // ------------------------------------------------------------------ casse en pièces

        const int MaxPieces = 36;   // pièces d'origine (îlots) gardées séparées
        const int MaxShards = 60;   // morceaux par niveau, fentes comprises

        /// <summary>
        /// Trois niveaux de casse à partir des pièces de l'objet (îlots de maillage) : niveau 0 = pièces entières
        /// (les très grosses fendues en 2), niveau 1 = grosses pièces en 3, niveau 2 = pièces moyennes en 4.
        /// Le niveau 0 sert aussi à montrer les fissures qui s'ouvrent au fil des coups.
        /// </summary>
        static Breakable.ShardSet[] PieceLevels(Mesh merged, string key, Style style)
        {
            var islands = Islands(merged);
            float total = islands.Sum(i => i.vol);
            string path = $"{Folder}/{key}_Eclats.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            bool first = true;
            void Save(Mesh m) { if (first) { AssetDatabase.CreateAsset(m, path); first = false; } else AssetDatabase.AddObjectToAsset(m, path); }

            var split = style.split ?? new[] { (0.35f, 2), (0.18f, 3), (0.08f, 4) };   // (part du volume, morceaux)
            var levels = new Breakable.ShardSet[split.Length];
            for (int l = 0; l < split.Length; l++)
            {
                var meshes = new List<Mesh>(); var offsets = new List<Vector3>();
                // Au plus MaxShards morceaux (Quest) : on fend d'abord les plus grosses pièces.
                int budget = MaxShards - islands.Count;
                var order = Enumerable.Range(0, islands.Count).OrderByDescending(k => islands[k].vol).ToList();
                var splitThis = new HashSet<int>();
                foreach (int k in order)
                    if (islands[k].vol / total > split[l].Item1 && islands[k].tris > 12 && budget >= split[l].Item2 - 1)
                    { splitThis.Add(k); budget -= split[l].Item2 - 1; }
                for (int i = 0; i < islands.Count; i++)
                {
                    var isl = islands[i];
                    if (splitThis.Contains(i))
                    {
                        float thick = Mathf.Clamp(isl.mesh.bounds.size.magnitude * 0.03f, style.thickMin, style.thickMax);
                        foreach (var s in BreakableBuilder.Fracture(isl.mesh, split[l].Item2, thick, key.GetHashCode() + l * 7919 + i))
                        {
                            if (style.crumple > 0f) Crumple(s.mesh, s.offset + isl.offset, style.crumple, i * 31 + l);
                            meshes.Add(s.mesh); offsets.Add(s.offset + isl.offset);
                        }
                    }
                    else
                    {
                        var m = Object.Instantiate(isl.mesh);
                        AddEmptyInner(m);
                        if (style.crumple > 0f && l > 0) Crumple(m, isl.offset, style.crumple * 0.5f, i * 31 + l);
                        meshes.Add(m); offsets.Add(isl.offset);
                    }
                }
                for (int i = 0; i < meshes.Count; i++) { meshes[i].name = $"{key} niveau {l} pièce {i}"; Save(meshes[i]); }
                levels[l] = new Breakable.ShardSet { meshes = meshes.ToArray(), offsets = offsets.ToArray() };
            }
            AssetDatabase.ImportAsset(path);
            foreach (var isl in islands) Object.DestroyImmediate(isl.mesh);
            return levels;
        }

        /// <summary>Sous-maillage vide en dernier (la matière « intérieure » des éclats), pour aligner les matériaux.</summary>
        static void AddEmptyInner(Mesh m)
        {
            var subs = new List<int[]>();
            for (int s = 0; s < m.subMeshCount; s++) subs.Add(m.GetTriangles(s));
            m.subMeshCount = subs.Count + 1;
            for (int s = 0; s < subs.Count; s++) m.SetTriangles(subs[s], s);
            m.SetTriangles(new int[0], subs.Count);
        }

        /// <summary>
        /// Pièces de l'objet : groupes de triangles reliés (sommets soudés par position). Les plus petites sont
        /// rattachées à leur voisine la plus proche (au plus <see cref="MaxPieces"/>). Chaque pièce est recentrée.
        /// </summary>
        static List<(Mesh mesh, Vector3 offset, float vol, int tris)> Islands(Mesh src)
        {
            var v = src.vertices; var n = src.normals; var uv = src.uv;
            if (uv == null || uv.Length != v.Length) uv = new Vector2[v.Length];
            var parent = Enumerable.Range(0, v.Length).ToArray();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }

            var weld = new Dictionary<(int, int, int), int>();
            for (int i = 0; i < v.Length; i++)
            {
                var k = (Mathf.RoundToInt(v[i].x * 2000f), Mathf.RoundToInt(v[i].y * 2000f), Mathf.RoundToInt(v[i].z * 2000f));
                if (weld.TryGetValue(k, out int j)) Union(i, j); else weld[k] = i;
            }
            var tris = new List<int[]>();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var t = src.GetTriangles(s); tris.Add(t);
                for (int i = 0; i < t.Length; i += 3) { Union(t[i], t[i + 1]); Union(t[i], t[i + 2]); }
            }

            // Triangles de chaque groupe.
            var groups = new Dictionary<int, List<(int sub, int a, int b, int c)>>();
            for (int s = 0; s < tris.Count; s++)
                for (int i = 0; i < tris[s].Length; i += 3)
                {
                    int r = Find(tris[s][i]);
                    if (!groups.TryGetValue(r, out var list)) groups[r] = list = new List<(int, int, int, int)>();
                    list.Add((s, tris[s][i], tris[s][i + 1], tris[s][i + 2]));
                }
            var parts = groups.Values.ToList();
            Bounds BoundsOf(List<(int sub, int a, int b, int c)> p)
            {
                var b = new Bounds(v[p[0].a], Vector3.zero);
                foreach (var t in p) { b.Encapsulate(v[t.a]); b.Encapsulate(v[t.b]); b.Encapsulate(v[t.c]); }
                return b;
            }
            float Vol(Bounds b) => Mathf.Max(b.size.x, 0.01f) * Mathf.Max(b.size.y, 0.01f) * Mathf.Max(b.size.z, 0.01f);

            // Trop de pièces (vis, feuilles...) : la plus petite rejoint la plus proche.
            var bounds = parts.Select(BoundsOf).ToList();
            while (parts.Count > MaxPieces)
            {
                int small = 0;
                for (int i = 1; i < parts.Count; i++) if (Vol(bounds[i]) < Vol(bounds[small])) small = i;
                int near = -1; float best = float.MaxValue;
                for (int i = 0; i < parts.Count; i++)
                {
                    if (i == small) continue;
                    float d = (bounds[i].ClosestPoint(bounds[small].center) - bounds[small].center).sqrMagnitude;
                    if (d < best) { best = d; near = i; }
                }
                parts[near].AddRange(parts[small]); var nb = bounds[near]; nb.Encapsulate(bounds[small]); bounds[near] = nb;
                parts.RemoveAt(small); bounds.RemoveAt(small);
            }

            var result = new List<(Mesh, Vector3, float, int)>();
            for (int p = 0; p < parts.Count; p++)
            {
                var center = bounds[p].center;
                var map = new Dictionary<int, int>();
                var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pu = new List<Vector2>();
                var subs = new List<int>[src.subMeshCount];
                for (int s = 0; s < subs.Length; s++) subs[s] = new List<int>();
                int Map(int i)
                {
                    if (map.TryGetValue(i, out int j)) return j;
                    j = pv.Count; map[i] = j; pv.Add(v[i] - center); pn.Add(n[i]); pu.Add(uv[i]);
                    return j;
                }
                foreach (var t in parts[p]) { subs[t.sub].Add(Map(t.a)); subs[t.sub].Add(Map(t.b)); subs[t.sub].Add(Map(t.c)); }
                var m = new Mesh { indexFormat = pv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(pv); m.SetNormals(pn); m.SetUVs(0, pu);
                m.subMeshCount = subs.Length;
                for (int s = 0; s < subs.Length; s++) m.SetTriangles(subs[s], s);
                m.RecalculateBounds();
                result.Add((m, center, Vol(bounds[p]), parts[p].Count));
            }
            return result;
        }

        /// <summary>Copie du maillage, coordonnées multipliées par s (normales corrigées).</summary>
        static Mesh Transformed(Mesh src, Vector3 s)
        {
            var m = Object.Instantiate(src);
            var v = m.vertices; var n = m.normals;
            var inv = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
            for (int i = 0; i < v.Length; i++) { v[i] = Vector3.Scale(v[i], s); n[i] = Vector3.Scale(n[i], inv).normalized; }
            m.vertices = v; m.normals = n; m.RecalculateBounds();
            return m;
        }

        static void Untransform(Mesh m, Vector3 inv)
        {
            var s = new Vector3(1f / inv.x, 1f / inv.y, 1f / inv.z);
            var v = m.vertices; var n = m.normals;
            for (int i = 0; i < v.Length; i++) { v[i] = Vector3.Scale(v[i], inv); n[i] = Vector3.Scale(n[i], s).normalized; }
            m.vertices = v; m.normals = n; m.RecalculateBounds();
        }

        /// <summary>Tôle cabossée : déformation douce et irrégulière le long des normales.</summary>
        static void Crumple(Mesh m, Vector3 offset, float amount, int seed)
        {
            var v = m.vertices; var n = m.normals;
            float ox = (seed % 97) * 0.37f, oy = (seed % 89) * 0.53f;
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i] + offset;
                float d = Mathf.PerlinNoise(p.x * 9f + p.z * 4f + ox, p.y * 9f + oy) - 0.5f;
                d += (Mathf.PerlinNoise(p.z * 23f + oy, p.x * 23f + ox) - 0.5f) * 0.35f;
                v[i] += n[i] * d * amount;
            }
            m.vertices = v; m.RecalculateNormals(); m.RecalculateBounds();
        }

        static (Color, Color) Chips(Matter m) => m switch
        {
            // Copeaux clairs (bois brut) : des copeaux trop sombres ressemblaient à des trous.
            Matter.Wood => (new Color(0.78f, 0.6f, 0.4f), new Color(0.55f, 0.38f, 0.22f)),
            Matter.Metal => (new Color(0.75f, 0.75f, 0.78f), new Color(0.45f, 0.45f, 0.48f)),
            Matter.Plastic => (new Color(0.85f, 0.82f, 0.75f), new Color(0.3f, 0.3f, 0.3f)),
            _ => (new Color(0.92f, 0.9f, 0.86f), new Color(0.45f, 0.32f, 0.22f)),
        };

        /// <summary>Matière visible dans les cassures (bois clair, métal brut, plastique, céramique/terre).</summary>
        static Material InnerMaterial(Matter m)
        {
            string path = $"{Folder}/Interieur_{m}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            var (c, smooth, metal) = m switch
            {
                Matter.Wood => (new Color(0.66f, 0.47f, 0.29f), 0.15f, 0f),
                Matter.Metal => (new Color(0.55f, 0.55f, 0.57f), 0.55f, 1f),
                Matter.Plastic => (new Color(0.8f, 0.78f, 0.72f), 0.3f, 0f),
                _ => (new Color(0.9f, 0.88f, 0.84f), 0.15f, 0f),
            };
            mat.SetColor("_BaseColor", c); mat.SetFloat("_Smoothness", smooth); mat.SetFloat("_Metallic", metal);
            mat.SetFloat("_Cull", 0f); // double face : tranches visibles de partout
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ marques de coups

        /// <summary>Matériau des marques de coups d'une matière (texture générée : 4 variantes en atlas 2×2).</summary>
        static Material DamageMaterial(Matter m)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Bar", "Casse");
            string texPath = $"{Folder}/Degats_{m}.png", matPath = $"{Folder}/Degats_{m}.mat";
            if (!File.Exists(texPath))
            {
                const int cell = 256, size = cell * 2;
                var px = new Color[size * size];
                var rng = new System.Random((int)m * 131 + 7);
                for (int k = 0; k < 4; k++)
                {
                    var f = MarkPainter(m, rng);
                    int ox = (k % 2) * cell, oy = (k / 2) * cell;
                    for (int y = 0; y < cell; y++)
                        for (int x = 0; x < cell; x++)
                            px[(oy + y) * size + ox + x] = f(new Vector2((x + 0.5f) / cell * 2f - 1f, (y + 0.5f) / cell * 2f - 1f));
                }
                var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
                t.SetPixels(px); t.Apply();
                File.WriteAllBytes(texPath, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(texPath);
                var imp = (TextureImporter)AssetImporter.GetAtPath(texPath);
                imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.SaveAndReimport();
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Fragile/Impact")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = Shader.Find("Fragile/Impact");
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            mat.SetFloat("_Strength", 1f);
            mat.SetFloat("_Brightness", 1.2f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Dessin d'une marque (coordonnées −1..1 -> couleur + alpha), selon la matière.</summary>
        static System.Func<Vector2, Color> MarkPainter(Matter m, System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            // Lignes brisées (fissures, rayures, fentes).
            var segs = new List<(Vector2 a, Vector2 b, float w)>();
            void Polyline(Vector2 start, float ang, float len, int steps, float w0, float w1, float jitter)
            {
                var p = start;
                for (int s = 0; s < steps; s++)
                {
                    ang += (R() - 0.5f) * jitter;
                    var q = p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (len / steps);
                    segs.Add((p, q, Mathf.Lerp(w0, w1, s / (float)steps)));
                    p = q;
                }
            }
            float Dist(Vector2 uv, out float w)
            {
                float best = 9f; w = 0.01f;
                foreach (var s in segs)
                {
                    var ab = s.b - s.a; float t = Mathf.Clamp01(Vector2.Dot(uv - s.a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    float d = (uv - (s.a + ab * t)).magnitude;
                    if (d / s.w < best / w) { best = d; w = s.w; }
                }
                return best;
            }

            switch (m)
            {
                case Matter.Wood:
                {
                    // Entaille : bois clair mis à nu, allongée (fil du bois), fentes sombres le long du fil, échardes.
                    float len = Mathf.Lerp(0.55f, 0.85f, R()), wid = Mathf.Lerp(0.14f, 0.22f, R());
                    for (int k = 0; k < 3; k++) Polyline(new Vector2(-len * 0.9f, (R() - 0.5f) * wid * 1.6f), (R() - 0.5f) * 0.15f, len * 1.8f, 8, 0.03f, 0.012f, 0.2f);
                    return uv =>
                    {
                        float gouge = Mathf.Clamp01(1f - (uv.x * uv.x / (len * len) + uv.y * uv.y / (wid * wid)));
                        float grain = Mathf.PerlinNoise(uv.x * 3f, uv.y * 40f);
                        float d = Dist(uv, out float w); float split = Mathf.Clamp01(1f - d / w);
                        var raw = new Color(0.78f, 0.6f, 0.4f) * Mathf.Lerp(0.85f, 1.1f, grain);
                        var c = Color.Lerp(raw, new Color(0.08f, 0.05f, 0.03f), split);
                        float a = Mathf.Max(Mathf.SmoothStep(0f, 1f, gouge * 2.5f) * 0.95f, split * 0.9f);
                        c.a = a * Mathf.Clamp01((0.98f - uv.magnitude) / 0.1f);
                        return c;
                    };
                }
                case Matter.Metal:
                {
                    // Bosse : creux sombre, reflet clair en croissant sur un bord, rayures.
                    float rx = Mathf.Lerp(0.35f, 0.5f, R()), ry = rx * Mathf.Lerp(0.6f, 0.9f, R());
                    float la = R() * 6.28f; var light = new Vector2(Mathf.Cos(la), Mathf.Sin(la));
                    for (int k = 0; k < 4; k++) { float a0 = R() * 6.28f; Polyline(new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 0.15f, a0 + (R() - 0.5f) * 0.6f, Mathf.Lerp(0.4f, 0.8f, R()), 3, 0.012f, 0.004f, 0.15f); }
                    return uv =>
                    {
                        float e = Mathf.Sqrt(uv.x * uv.x / (rx * rx) + uv.y * uv.y / (ry * ry));
                        float dent = Mathf.Clamp01(1f - e);
                        float rim = Mathf.Clamp01(1f - Mathf.Abs(e - 0.85f) / 0.18f) * Mathf.Clamp01(Vector2.Dot(uv.normalized, light));
                        float d = Dist(uv, out float w); float scratch = Mathf.Clamp01(1f - d / w);
                        var c = new Color(0.08f, 0.08f, 0.09f);
                        float a = Mathf.SmoothStep(0f, 1f, dent) * 0.7f;
                        if (rim > a) { c = new Color(0.9f, 0.9f, 0.92f); a = rim * 0.8f; }
                        if (scratch > a) { c = new Color(0.8f, 0.8f, 0.82f); a = scratch * 0.8f; }
                        c.a = a * Mathf.Clamp01((0.98f - uv.magnitude) / 0.1f);
                        return c;
                    };
                }
                case Matter.Plastic:
                {
                    // Fissures de contrainte blanches en étoile, petit trou sombre au centre.
                    int n = 5 + rng.Next(4);
                    for (int k = 0; k < n; k++) Polyline(Vector2.zero, (k + R() * 0.6f) / n * 6.28f, Mathf.Lerp(0.45f, 0.9f, R()), 5, 0.025f, 0.006f, 0.5f);
                    return uv =>
                    {
                        float d = Dist(uv, out float w); float crack = Mathf.Clamp01(1f - d / w);
                        float hole = Mathf.Clamp01(1f - uv.magnitude / 0.08f);
                        var c = hole > 0.1f ? new Color(0.06f, 0.06f, 0.06f) : new Color(0.95f, 0.95f, 0.93f);
                        c.a = Mathf.Max(crack * 0.9f, Mathf.SmoothStep(0f, 1f, hole * 2f)) * Mathf.Clamp01((0.98f - uv.magnitude) / 0.1f);
                        return c;
                    };
                }
                default:
                {
                    // Fêlures fines et sombres, petit éclat clair au centre.
                    int n = 4 + rng.Next(3);
                    for (int k = 0; k < n; k++) Polyline(Vector2.zero, (k + R() * 0.7f) / n * 6.28f, Mathf.Lerp(0.5f, 0.95f, R()), 7, 0.012f, 0.004f, 0.7f);
                    return uv =>
                    {
                        float d = Dist(uv, out float w); float crack = Mathf.Clamp01(1f - d / w);
                        float chip = Mathf.Clamp01(1f - uv.magnitude / 0.1f);
                        var c = chip > crack ? new Color(0.95f, 0.93f, 0.9f) : new Color(0.08f, 0.07f, 0.06f);
                        c.a = Mathf.Max(crack * 0.85f, Mathf.SmoothStep(0f, 1f, chip * 2f) * 0.9f) * Mathf.Clamp01((0.98f - uv.magnitude) / 0.1f);
                        return c;
                    };
                }
            }
        }

        static Bounds LocalBounds(GameObject go)
        {
            var root = go.transform; bool first = true; var b = new Bounds();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
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
