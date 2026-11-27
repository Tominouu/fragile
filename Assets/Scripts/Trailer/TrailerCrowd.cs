using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fragile.Trailer
{
    /// <summary>
    /// Le bar plein pendant le Trailer : des clients (humains générés par Tools/Blender/generate_clients.py) sont assis
    /// au comptoir, aux tables, dans le salon, debout au mange-debout et devant la cible, et un barman derrière le comptoir.
    /// Les places sont retrouvées dans le VRAI Bar par le nom des meubles (comme les télés et les tabourets) : si Tom
    /// déplace le mobilier, les clients suivent. Quand le personnage pète un câble, ils fuient tous par la porte de sortie,
    /// le long d'un champ de distances calculé sur une grille du sol (évite comptoir, tables, joueur). Le bar est vide
    /// quand le joueur prend la main (la scène Bar est de toute façon rechargée sans eux).
    /// </summary>
    public class TrailerCrowd : MonoBehaviour
    {
        struct Spot
        {
            public Vector3 Pos, Forward;
            public bool Seat;
            public int Group;       // même groupe = ils discutent ensemble
            public bool Barman;
        }

        readonly List<BarClient> m_Clients = new List<BarClient>();
        readonly Dictionary<BarClient, int> m_Groups = new Dictionary<BarClient, int>();
        Transform m_Root;
        Vector3 m_RoomCenter;
        float m_FloorY;
        int m_Total, m_Left;
        AudioSource m_Murmur;

        // Sortie.
        Vector3 m_DoorCenter, m_DoorIn;      // centre de la porte (au sol), normale vers l'intérieur
        Transform m_DoorPivot;
        float m_DoorAngle;
        bool m_DoorOpen;

        // Grille de fuite.
        const float Cell = 0.25f;
        Vector3 m_GridMin;
        int m_W, m_H;
        bool[] m_Walk;
        float[] m_Dist;

        public int Count => m_Clients.Count;

        // ------------------------------------------------------------------ installation

        public void Build(Scene bar, TrailerAnchors a, GameObject[] prefabs, RuntimeAnimatorController controller, int max, Shader unlit)
        {
            m_Root = new GameObject("Clients du bar").transform;
            m_Root.SetParent(transform, false);

            var all = new List<Transform>();
            foreach (var r in bar.GetRootGameObjects()) all.AddRange(r.GetComponentsInChildren<Transform>());
            var spots = FindSpots(all, a);
            if (spots.Count == 0) { Debug.LogWarning("[Fragile] Trailer : aucune place trouvée pour les clients."); return; }

            m_RoomCenter = Vector3.zero;
            foreach (var s in spots) m_RoomCenter += s.Pos;
            m_RoomCenter /= spots.Count;
            m_FloorY = FloorAt(a.Eye);

            // La grille est calculée AVANT l'arrivée des clients (ils ne sont pas des obstacles).
            if (FindDoor(all, unlit)) BuildGrid(spots, a);

            // Places les plus proches du joueur d'abord (avec du hasard) : il doit sentir le monde autour de lui.
            var rng = new System.Random(1789);
            var barman = spots.FindIndex(s => s.Barman);
            var order = new List<(Spot s, float k)>();
            foreach (var s in spots) if (!s.Barman) order.Add((s, Vector3.Distance(s.Pos, a.Eye) + (float)rng.NextDouble() * 4f));
            order.Sort((p, q) => p.k.CompareTo(q.k));

            var guests = new List<GameObject>(); GameObject barmanPrefab = null;
            foreach (var p in prefabs)
                if (p != null) { if (p.name.Contains("Barman")) barmanPrefab = p; else guests.Add(p); }
            Shuffle(guests, rng);
            if (guests.Count == 0) return;

            int n = 0;
            if (barman >= 0 && barmanPrefab != null) Spawn(barmanPrefab, spots[barman], controller, n++, rng);
            for (int i = 0; i < order.Count && n < max; i++)
                Spawn(guests[i % guests.Count], order[i].s, controller, n++, rng);

            m_Total = m_Left = m_Clients.Count;
            CreateMurmur();
            Debug.Log($"[Fragile] Trailer : {m_Clients.Count} clients dans le bar ({spots.Count} places trouvées)" +
                      (m_Walk != null ? ", chemin de fuite prêt." : ", ATTENTION : porte de sortie introuvable."));
        }

        void Spawn(GameObject prefab, Spot s, RuntimeAnimatorController controller, int index, System.Random rng)
        {
            var go = Instantiate(prefab, m_Root);
            go.name = prefab.name + (index >= 13 ? " (bis)" : "");
            var c = go.AddComponent<BarClient>();
            c.Init(this, controller, index * 31 + 7);
            float phase = (float)rng.NextDouble();
            if (s.Seat) c.SitOn(s.Pos, s.Forward, phase, s.Group >= 0 && rng.NextDouble() < 0.6);
            else c.StandAt(s.Pos, s.Forward, s.Group >= 0 || s.Barman, phase);
            c.LookWeight = 0.7f;
            c.LookTarget = c.HeadPosition + s.Forward * 2f;
            m_Clients.Add(c);
            m_Groups[c] = s.Group;
        }

        static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
        }

        // ------------------------------------------------------------------ places

        List<Spot> FindSpots(List<Transform> all, TrailerAnchors a)
        {
            var spots = new List<Spot>();
            var supports = new List<Bounds>();       // comptoir, tables : ce qu'on regarde quand on est assis
            foreach (var t in Top(all, "Comptoir", "Table", "Table basse", "Tonneau mange-debout", "Tonneau"))
                if (TryBounds(t, out var b)) supports.Add(b);
            var counter = Top(all, "Comptoir");

            int group = 0;
            // Tabourets (sauf celui du joueur).
            foreach (var t in Top(all, "Tabouret"))
            {
                if (t == a.Stool || !TryBounds(t, out var b)) continue;
                var seat = SeatPoint(t, b);
                spots.Add(new Spot { Pos = seat, Forward = Facing(seat, supports, a.TvRoot != null ? a.TvRoot.position : seat + Vector3.forward), Seat = true, Group = -1 });
            }
            // Fauteuils : chaque table réunit ses deux fauteuils (ils discutent).
            var tables = Top(all, "Table", "Table basse");
            foreach (var t in Top(all, "Fauteuil", "Fauteuil lounge"))
            {
                if (!TryBounds(t, out var b)) continue;
                var seat = SeatPoint(t, b);
                int g = NearestIndex(tables, seat, 2.2f);
                spots.Add(new Spot { Pos = seat, Forward = Facing(seat, supports, m_RoomCenter), Seat = true, Group = g >= 0 ? 100 + g : -1 });
            }
            // Canapé et bancs : deux places côte à côte.
            foreach (var t in Top(all, "Canapé", "Banc"))
            {
                if (!TryBounds(t, out var b)) continue;
                var along = b.size.x >= b.size.z ? Vector3.right : Vector3.forward;
                var center = SeatPoint(t, b);
                var fwd = Vector3.Cross(along, Vector3.up);
                var toTable = Facing(center, supports, Vector3.zero) ;
                if (Vector3.Dot(fwd, toTable) < 0f) fwd = -fwd;
                float half = Mathf.Max(b.size.x, b.size.z) * 0.22f;
                foreach (var s in new[] { -1f, 1f })
                {
                    var p = center + along * (s * half);
                    var seat = SeatPoint(t, new Bounds(p, new Vector3(0.3f, b.size.y, 0.3f)), b.max.y);
                    spots.Add(new Spot { Pos = seat, Forward = fwd, Seat = true, Group = 200 + group });
                }
                group++;
            }
            // Debout autour du mange-debout (deux qui discutent) et devant la cible de fléchettes.
            foreach (var t in Top(all, "Tonneau mange-debout"))
            {
                if (!TryBounds(t, out var b)) continue;
                var c = new Vector3(b.center.x, 0f, b.center.z);
                var dir = Flat(m_RoomCenter - c);
                foreach (var ang in new[] { 60f, -60f })
                {
                    var d = Quaternion.Euler(0f, ang, 0f) * dir;
                    var p = c + d * (Mathf.Max(b.extents.x, b.extents.z) + 0.35f);
                    p.y = FloorAt(p);
                    if (Free(p)) spots.Add(new Spot { Pos = p, Forward = -d, Seat = false, Group = 300 + group });
                }
                group++;
            }
            foreach (var t in Top(all, "Cible de fléchettes"))
            {
                if (!TryBounds(t, out var b)) continue;
                var wallOut = Flat(m_RoomCenter - b.center);
                var p = b.center + wallOut * 2.2f; p.y = FloorAt(p);
                if (Free(p)) spots.Add(new Spot { Pos = p, Forward = -wallOut, Seat = false, Group = -1 });
            }
            // Le barman, au centre du comptoir (à l'intérieur), tourné vers le joueur.
            foreach (var t in counter)
            {
                if (!TryBounds(t, out var b)) continue;
                var p = new Vector3(b.center.x, 0f, b.center.z); p.y = FloorAt(p);
                if (Free(p)) spots.Add(new Spot { Pos = p, Forward = Flat(a.Eye - p), Seat = false, Barman = true, Group = -1 });
            }
            return spots;
        }

        /// <summary>Objets du bar dont le nom est exactement l'un de ceux donnés (le plus haut de chaque branche).</summary>
        static List<Transform> Top(List<Transform> all, params string[] names)
        {
            var res = new List<Transform>();
            foreach (var t in all)
            {
                if (System.Array.IndexOf(names, t.name) < 0 || !t.gameObject.activeInHierarchy) continue;
                bool nested = false;
                for (var p = t.parent; p != null; p = p.parent) if (System.Array.IndexOf(names, p.name) >= 0) { nested = true; break; }
                if (!nested) res.Add(t);
            }
            return res;
        }

        static bool TryBounds(Transform t, out Bounds b)
        {
            b = default; bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        /// <summary>Dessus de l'assise : premier contact en descendant au centre (le dossier et les accoudoirs sont plus hauts).</summary>
        static Vector3 SeatPoint(Transform t, Bounds b, float top = float.NaN)
        {
            float from = (float.IsNaN(top) ? b.max.y : top) + 0.3f;
            var o = new Vector3(b.center.x, from, b.center.z);
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(o, Vector3.down, from + 0.5f, ~0, QueryTriggerInteraction.Ignore))
                if (h.transform.IsChildOf(t) && h.normal.y > 0.6f && h.point.y < b.max.y - 0.02f) best = Mathf.Max(best, h.point.y);
            if (float.IsNegativeInfinity(best))
                foreach (var h in Physics.RaycastAll(o, Vector3.down, from + 0.5f, ~0, QueryTriggerInteraction.Ignore))
                    if (h.transform.IsChildOf(t)) best = Mathf.Max(best, h.point.y);
            return new Vector3(b.center.x, float.IsNegativeInfinity(best) ? b.min.y + b.size.y * 0.45f : best, b.center.z);
        }

        static Vector3 Facing(Vector3 from, List<Bounds> supports, Vector3 fallback)
        {
            float best = 2.2f; Vector3 dir = Flat(fallback - from);
            foreach (var b in supports)
            {
                var p = b.ClosestPoint(from);
                var d = p - from; d.y = 0f;
                if (d.magnitude < 0.05f || d.magnitude >= best) continue;
                best = d.magnitude; dir = d.normalized;
            }
            return dir;
        }

        static int NearestIndex(List<Transform> list, Vector3 p, float max)
        {
            int res = -1; float best = max;
            for (int i = 0; i < list.Count; i++)
            {
                float d = Vector3.Distance(Flat3(list[i].position), Flat3(p));
                if (d < best) { best = d; res = i; }
            }
            return res;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
        static Vector3 Flat3(Vector3 v) { v.y = 0f; return v; }

        static float FloorAt(Vector3 p)
        {
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, 3f, p.z), Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
                if (h.normal.y > 0.8f && h.point.y < 0.3f) best = Mathf.Max(best, h.point.y);
            return float.IsNegativeInfinity(best) ? 0f : best;
        }

        /// <summary>Assez de place pour un corps debout (rien entre 15 cm et 1,7 m sur 30 cm de large).</summary>
        static bool Free(Vector3 floor)
        {
            foreach (var c in Physics.OverlapBox(floor + Vector3.up * 0.92f, new Vector3(0.15f, 0.77f, 0.15f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                if (Blocks(c)) return false;
            return true;
        }

        static bool Blocks(Collider c)
        {
            var rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic) return false;                  // verres, bouteilles : on les bouscule
            if (c.GetComponentInParent<BarClient>() != null) return false;
            return true;
        }

        // ------------------------------------------------------------------ porte et chemin

        bool FindDoor(List<Transform> all, Shader unlit)
        {
            Transform door = null;
            foreach (var t in all) if (t.name == "Porte de sortie") { door = t; break; }
            if (door == null || !TryBounds(door, out var b)) return false;

            // La porte est fine dans l'axe du mur : sa normale est cet axe, orientée vers la salle.
            var axis = b.size.x < b.size.z ? Vector3.right : Vector3.forward;
            if (Vector3.Dot(m_RoomCenter - b.center, axis) < 0f) axis = -axis;
            m_DoorIn = axis;
            m_DoorCenter = new Vector3(b.center.x, FloorAt(b.center + axis * 0.5f), b.center.z);

            // Battant (statique dans le Bar) : on cache l'original et on fait pivoter une copie en boîtes.
            var leafParts = new List<Renderer>();
            foreach (var r in door.GetComponentsInChildren<Renderer>())
                if (r.name != "Montant" && r.name != "Linteau") leafParts.Add(r);
            if (leafParts.Count > 0)
            {
                var lb = leafParts[0].bounds;
                foreach (var r in leafParts) lb.Encapsulate(r.bounds);
                var lateral = Vector3.Cross(Vector3.up, axis);
                var hinge = lb.center - lateral * (Vector3.Dot(lb.extents, new Vector3(Mathf.Abs(lateral.x), 0f, Mathf.Abs(lateral.z))));
                hinge.y = m_DoorCenter.y;
                m_DoorPivot = new GameObject("Porte (Trailer)").transform;
                m_DoorPivot.SetParent(transform, false);
                m_DoorPivot.position = hinge;
                foreach (var r in leafParts)
                {
                    var copy = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(copy.GetComponent<Collider>());
                    copy.name = r.name;
                    copy.transform.SetParent(m_DoorPivot, true);
                    copy.transform.position = r.bounds.center;
                    copy.transform.localScale = r.bounds.size;
                    copy.GetComponent<Renderer>().sharedMaterial = r.sharedMaterial;
                    r.enabled = false;
                    foreach (var col in r.GetComponents<Collider>()) col.enabled = false;
                }
                // Le battant s'ouvre vers l'extérieur (hors de la salle).
                var probe = Quaternion.AngleAxis(100f, Vector3.up) * (lb.center - hinge);
                m_DoorAngle = Vector3.Dot(probe, axis) < 0f ? 100f : -100f;
            }

            // Derrière la porte : un couloir sombre (le mur du bar n'a rien derrière).
            var dark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(dark.GetComponent<Collider>());
            dark.name = "Couloir sombre (Trailer)";
            dark.transform.SetParent(transform, false);
            float width = Mathf.Max(b.size.x, b.size.z) + 0.4f;
            dark.transform.position = m_DoorCenter - axis * 1.0f + Vector3.up * 1.15f;
            dark.transform.rotation = Quaternion.LookRotation(axis, Vector3.up);
            dark.transform.localScale = new Vector3(width, 2.3f, 1.6f);
            var mat = new Material(unlit) { name = "Couloir sombre" };
            mat.SetColor("_BaseColor", new Color(0.01f, 0.01f, 0.012f));
            mat.SetFloat("_Shade", 0f);
            var dr = dark.GetComponent<Renderer>();
            dr.sharedMaterial = mat;
            dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return true;
        }

        void BuildGrid(List<Spot> spots, TrailerAnchors a)
        {
            var bounds = new Bounds(m_DoorCenter, Vector3.one);
            foreach (var s in spots) bounds.Encapsulate(s.Pos);
            bounds.Encapsulate(a.Eye);
            bounds.Expand(new Vector3(2f, 0f, 2f));
            m_GridMin = new Vector3(bounds.min.x, m_FloorY, bounds.min.z);
            m_W = Mathf.CeilToInt(bounds.size.x / Cell);
            m_H = Mathf.CeilToInt(bounds.size.z / Cell);
            m_Walk = new bool[m_W * m_H];
            Physics.SyncTransforms();
            for (int z = 0; z < m_H; z++)
                for (int x = 0; x < m_W; x++)
                    m_Walk[z * m_W + x] = Free(CellCenter(x, z));

            // Le seuil de la porte est toujours praticable (le battant s'ouvre).
            var exit = m_DoorCenter + m_DoorIn * 0.5f;
            for (float d = -0.2f; d <= 0.9f; d += Cell * 0.5f)
                for (float l = -0.3f; l <= 0.3f; l += Cell * 0.5f)
                {
                    var p = m_DoorCenter + m_DoorIn * d + Vector3.Cross(Vector3.up, m_DoorIn) * l;
                    if (ToCell(p, out int cx, out int cz)) m_Walk[cz * m_W + cx] = true;
                }

            // Champ de distances depuis la sortie (Dijkstra, 8 voisins), pénalité près des obstacles : on court au milieu.
            m_Dist = new float[m_W * m_H];
            for (int i = 0; i < m_Dist.Length; i++) m_Dist[i] = float.PositiveInfinity;
            if (!ToCell(exit, out int ex, out int ez)) return;
            var open = new SortedSet<(float, int)>();
            m_Dist[ez * m_W + ex] = 0f;
            open.Add((0f, ez * m_W + ex));
            while (open.Count > 0)
            {
                var cur = open.Min; open.Remove(cur);
                int ci = cur.Item2, cx = ci % m_W, cz = ci / m_W;
                if (cur.Item1 > m_Dist[ci]) continue;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= m_W || nz >= m_H) continue;
                        int ni = nz * m_W + nx;
                        if (!m_Walk[ni]) continue;
                        if (dx != 0 && dz != 0 && (!m_Walk[cz * m_W + nx] || !m_Walk[nz * m_W + cx])) continue;   // pas de coin coupé
                        float cost = (dx != 0 && dz != 0 ? 1.414f : 1f) * (1f + Crowding(nx, nz));
                        float nd = m_Dist[ci] + cost;
                        if (nd < m_Dist[ni]) { m_Dist[ni] = nd; open.Add((nd, ni)); }
                    }
            }
        }

        float Crowding(int x, int z)
        {
            int blocked = 0;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= m_W || nz >= m_H || !m_Walk[nz * m_W + nx]) blocked++;
                }
            return blocked * 0.6f;
        }

        Vector3 CellCenter(int x, int z) => m_GridMin + new Vector3((x + 0.5f) * Cell, 0f, (z + 0.5f) * Cell);

        bool ToCell(Vector3 p, out int x, out int z)
        {
            x = Mathf.FloorToInt((p.x - m_GridMin.x) / Cell);
            z = Mathf.FloorToInt((p.z - m_GridMin.z) / Cell);
            return x >= 0 && z >= 0 && x < m_W && z < m_H;
        }

        /// <summary>Chemin de fuite depuis <paramref name="from"/> : sortie de la place, descente du champ de distances, porte, couloir.</summary>
        List<Vector3> PathFrom(Vector3 from)
        {
            var path = new List<Vector3>();
            if (m_Dist == null) return path;
            // Case de départ : la plus proche qui mène à la sortie (on se dégage de la chaise ou du tabouret).
            int start = -1; float best = float.PositiveInfinity;
            if (ToCell(from, out int sx, out int sz))
                for (int r = 0; r <= 6 && start < 0; r++)
                    for (int dz = -r; dz <= r; dz++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            int x = sx + dx, z = sz + dz;
                            if (x < 0 || z < 0 || x >= m_W || z >= m_H) continue;
                            int i = z * m_W + x;
                            if (!m_Walk[i] || float.IsInfinity(m_Dist[i])) continue;
                            float d = dx * dx + dz * dz;
                            if (d < best) { best = d; start = i; }
                        }
            if (start < 0) return path;

            int cur = start, guard = m_W * m_H;
            var cells = new List<Vector3>();
            while (guard-- > 0)
            {
                cells.Add(CellCenter(cur % m_W, cur / m_W));
                if (m_Dist[cur] <= 0f) break;
                int next = -1; float nd = m_Dist[cur];
                int cx = cur % m_W, cz = cur / m_W;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= m_W || nz >= m_H) continue;
                        int ni = nz * m_W + nx;
                        if (m_Dist[ni] < nd) { nd = m_Dist[ni]; next = ni; }
                    }
                if (next < 0) break;
                cur = next;
            }
            // Lissage : on saute les cases tant que la ligne droite est dégagée.
            path.Add(cells[0]);
            int k = 0;
            while (k < cells.Count - 1)
            {
                int far = k + 1;
                for (int j = cells.Count - 1; j > k + 1; j--)
                    if (Clear(cells[k], cells[j])) { far = j; break; }
                path.Add(cells[far]);
                k = far;
            }
            path.Add(m_DoorCenter + m_DoorIn * 0.1f);
            path.Add(m_DoorCenter - m_DoorIn * 1.2f);
            return path;
        }

        bool Clear(Vector3 a, Vector3 b)
        {
            int steps = Mathf.CeilToInt(Vector3.Distance(a, b) / (Cell * 0.5f));
            for (int i = 1; i < steps; i++)
            {
                if (!ToCell(Vector3.Lerp(a, b, i / (float)steps), out int x, out int z) || !m_Walk[z * m_W + x]) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ pendant le Trailer

        /// <summary>Pendant le match : chacun regarde la télé la plus en face de lui, ou son voisin de table.</summary>
        public void WatchTv(List<Renderer> screens)
        {
            foreach (var c in m_Clients)
            {
                if (c == null) continue;
                Renderer bestScreen = null; float best = -2f;
                foreach (var s in screens)
                {
                    var to = (s.bounds.center - c.HeadPosition).normalized;
                    float f = Vector3.Dot(to, c.transform.forward) + Vector3.Dot(-s.transform.forward, -to) * 0.5f;
                    if (f > best) { best = f; bestScreen = s; }
                }
                var partner = Partner(c);
                bool talk = partner != null && Random.value < 0.6f;
                c.LookTarget = talk ? partner.HeadPosition : bestScreen != null ? bestScreen.bounds.center : c.HeadPosition + c.transform.forward * 3f;
                c.LookWeight = talk ? 0.8f : 0.9f;
            }
        }

        BarClient Partner(BarClient c)
        {
            if (!m_Groups.TryGetValue(c, out int g) || g < 0) return null;
            foreach (var o in m_Clients) if (o != c && o != null && m_Groups.TryGetValue(o, out int og) && og == g) return o;
            return null;
        }

        /// <summary>Le personnage commence à s'énerver : tout le monde se tourne vers lui.</summary>
        public void Stare(Vector3 head)
        {
            foreach (var c in m_Clients)
            {
                if (c == null) continue;
                c.LookTarget = head;
                c.LookWeight = 1f;
            }
        }

        /// <summary>Panique : les plus proches détalent les premiers, les autres suivent par vagues.</summary>
        public void Panic(Vector3 head)
        {
            var rng = new System.Random(42);
            foreach (var c in m_Clients)
            {
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                c.LookTarget = head;
                float dist = Vector3.Distance(c.transform.position, head);
                float delay = Mathf.Clamp(dist * 0.12f, 0f, 0.9f) + (float)rng.NextDouble() * 0.9f;
                float speed = 4.2f + (float)rng.NextDouble() * 1.6f;
                var path = PathFrom(c.transform.position);
                if (path.Count == 0) continue;   // coincé (aucun chemin) : il reste figé à sa place, la tête tournée vers le joueur
                c.Flee(path, delay, speed);
            }
        }

        /// <summary>Séparation entre fuyards : on ne se traverse pas.</summary>
        public Vector3 Separation(BarClient me)
        {
            var push = Vector3.zero;
            foreach (var o in m_Clients)
            {
                if (o == me || o == null || o.State != BarClient.Mode.Fleeing) continue;
                var d = me.transform.position - o.transform.position; d.y = 0f;
                float m = d.magnitude;
                if (m < 0.55f && m > 1e-3f) push += d / m * (0.55f - m) / 0.55f;
            }
            return push;
        }

        public void Exited(BarClient c)
        {
            m_Left--;
            if (!m_DoorOpen && m_DoorPivot != null) StartCoroutine(OpenDoor());
        }

        public void Fell(BarClient c) => m_Left--;

        IEnumerator OpenDoor()
        {
            m_DoorOpen = true;
            var start = m_DoorPivot.rotation;
            var end = Quaternion.AngleAxis(m_DoorAngle, Vector3.up) * start;
            SfxBank_DoorBang(m_DoorCenter);
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
            {
                m_DoorPivot.rotation = Quaternion.Slerp(start, end, 1f - (1f - t) * (1f - t));
                yield return null;
            }
            m_DoorPivot.rotation = end;
        }

        static void SfxBank_DoorBang(Vector3 p)
        {
            if (!Fragile.Audio.SfxBank.Play(Fragile.Audio.SfxBank.ImpactLeger, p, 0.7f, 0.8f))
                Fragile.Audio.SfxBank.Play(Fragile.Audio.SfxBank.ObjetTombe, p, 0.6f);
        }

        /// <summary>Le brouhaha baisse avec la salle qui se vide ; la porte s'ouvre quand le premier fuyard arrive.</summary>
        void Update()
        {
            if (m_Murmur != null)
                m_Murmur.volume = Mathf.MoveTowards(m_Murmur.volume, 0.22f * m_Left / Mathf.Max(1, m_Total), Time.deltaTime * 0.3f);
            if (m_DoorOpen || m_DoorPivot == null) return;
            foreach (var c in m_Clients)
                if (c != null && c.State == BarClient.Mode.Fleeing && Vector3.Distance(Flat3(c.transform.position), Flat3(m_DoorCenter)) < 1.6f)
                {
                    StartCoroutine(OpenDoor());
                    break;
                }
        }

        void OnDestroy()
        {
            // Clients, porte animée, couloir et brouhaha disparaissent avec la foule.
            if (m_Root != null) Destroy(m_Root.gameObject);
            if (m_DoorPivot != null) Destroy(m_DoorPivot.gameObject);
            if (m_Murmur != null) Destroy(m_Murmur.gameObject);
            var dark = transform.Find("Couloir sombre (Trailer)");
            if (dark != null) Destroy(dark.gameObject);
        }

        // ------------------------------------------------------------------ ambiance

        void CreateMurmur()
        {
            // Brouhaha du bar plein (foule générée, assourdie) : il baisse à mesure que la salle se vide.
            var go = new GameObject("Brouhaha du bar");
            go.transform.SetParent(transform, false);
            go.transform.position = m_RoomCenter + Vector3.up * 1.4f;
            m_Murmur = go.AddComponent<AudioSource>();
            m_Murmur.clip = TrailerSynth.Crowd(7f);
            m_Murmur.loop = true;
            m_Murmur.pitch = 0.82f;
            m_Murmur.spatialBlend = 0.6f;
            m_Murmur.minDistance = 4f; m_Murmur.maxDistance = 20f;
            m_Murmur.volume = 0.22f;
            go.AddComponent<AudioLowPassFilter>().cutoffFrequency = 1600f;
            m_Murmur.Play();
        }
    }
}
