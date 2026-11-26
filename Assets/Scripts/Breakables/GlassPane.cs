using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fragile.Breakables
{
    /// <summary>
    /// Vitre plane cassable (porte de frigo, vitrine). Repère : la vitre est dans le plan XY local, centrée, +Z = côté extérieur.
    /// Coup modéré : fissures en étoile à l'impact. Dégâts cumulés suffisants : la vitre vole en éclats (morceaux de verre,
    /// poussière, son) et laisse le passage (on peut passer la main dans le cadre).
    /// Les coups sont transmis par l'objet qui porte le Rigidbody (ex. <see cref="Fragile.Grab.HingedDoor"/>).
    /// </summary>
    public class GlassPane : MonoBehaviour
    {
        [SerializeField] Vector2 m_Size = new Vector2(0.52f, 1.52f);
        [Tooltip("Vitesse d'impact minimale (m/s, batte comprise) pour abîmer la vitre.")]
        [SerializeField] float m_MinSpeed = 1.6f;
        [Tooltip("Vitesse d'impact (m/s) d'un coup maximal.")]
        [SerializeField] float m_MaxSpeed = 8f;
        [Tooltip("Dégâts cumulés (un coup maximal = 1) au-delà desquels la vitre éclate.")]
        [SerializeField] float m_Health = 0.55f;
        [SerializeField] Material m_CrackMaterial, m_ShardMaterial;
        [SerializeField] GameObject m_FxPrefab;
        [SerializeField] AudioClip m_GlassClip;

        float m_Damage, m_LastHit = -10f;
        int m_CrackCount;
        bool m_Broken;
        readonly List<GameObject> m_CrackObjects = new List<GameObject>();

        public bool broken => m_Broken;
        public Collider paneCollider { get; private set; }

        public void Setup(Vector2 size, Material crack, Material shard, GameObject fx, AudioClip glass)
        {
            m_Size = size; m_CrackMaterial = crack; m_ShardMaterial = shard; m_FxPrefab = fx; m_GlassClip = glass;
        }

        void Awake()
        {
            paneCollider = GetComponent<Collider>();
            if (m_GlassClip != null) m_GlassClip.LoadAudioData();
            BreakFx.Prewarm(m_FxPrefab, 1);
        }

        /// <summary>Choc reçu (appelé par le porteur du Rigidbody). Renvoie true si la vitre a encaissé le coup.</summary>
        public bool Hit(Collision c)
        {
            if (m_Broken || c.rigidbody == null || c.rigidbody.GetComponent<Debris>() != null) return false;
            float speed = c.relativeVelocity.magnitude;
            var breaker = c.rigidbody.GetComponentInParent<Breaker>();
            float power = breaker != null ? speed * breaker.power : speed;
            if (power < m_MinSpeed || Time.time - m_LastHit < 0.12f) return false;

            float energy = Mathf.InverseLerp(m_MinSpeed, m_MaxSpeed, power);
            if (breaker != null) breaker.Haptics(energy);
            var point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            Hit(point, -c.relativeVelocity.normalized, energy);
            return true;
        }

        public void Hit(Vector3 point, Vector3 direction, float energy)
        {
            if (m_Broken) return;
            m_LastHit = Time.time;
            m_Damage += Mathf.Lerp(0.2f, 1f, energy);
            var local = transform.InverseTransformPoint(point);
            var p = new Vector2(Mathf.Clamp(local.x, -m_Size.x * 0.46f, m_Size.x * 0.46f), Mathf.Clamp(local.y, -m_Size.y * 0.46f, m_Size.y * 0.46f));
            var onPane = transform.TransformPoint(new Vector3(p.x, p.y, 0f));

            if (m_Damage >= m_Health) { Shatter(p, onPane, direction, energy); return; }
            AddCracks(p, energy);
            PlaySound(m_GlassClip, onPane, Mathf.Lerp(0.25f, 0.5f, energy), 1.45f);
            Burst(onPane, direction, 0.1f);
        }

        void Shatter(Vector2 p, Vector3 onPane, Vector3 direction, float energy)
        {
            m_Broken = true;
            foreach (var go in m_CrackObjects) if (go != null) Destroy(go);
            m_CrackObjects.Clear();
            if (TryGetComponent<Renderer>(out var r)) r.enabled = false;
            if (paneCollider != null) paneCollider.enabled = false;

            SpawnShards(p, direction, energy);
            Burst(onPane, direction, Mathf.Lerp(0.4f, 1f, energy));
            PlaySound(m_GlassClip, onPane, 1f, Random.Range(0.95f, 1.1f));
        }

        // ------------------------------------------------------------------ fissures et éclats

        const int k_Rays = 10;
        static readonly float[] k_Rings = { 0.03f, 0.08f, 0.16f, 0.28f, 0.45f, 0.7f, 1.1f };

        Vector2[,] Web(Vector2 p, float reach)
        {
            var v = new Vector2[k_Rings.Length, k_Rays];
            float start = Random.value * Mathf.PI * 2f;
            for (int k = 0; k < k_Rays; k++)
            {
                float baseAngle = start + (k + Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / k_Rays;
                for (int r = 0; r < k_Rings.Length; r++)
                {
                    float a = baseAngle + Random.Range(-0.1f, 0.1f);
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    v[r, k] = p + d * Mathf.Min(k_Rings[r] * reach * Random.Range(0.8f, 1.2f), EdgeDistance(p, d));
                }
            }
            return v;
        }

        float EdgeDistance(Vector2 p, Vector2 d)
        {
            float x = Mathf.Abs(d.x) < 1e-5f ? 1e5f : (Mathf.Sign(d.x) * m_Size.x * 0.5f - p.x) / d.x;
            float y = Mathf.Abs(d.y) < 1e-5f ? 1e5f : (Mathf.Sign(d.y) * m_Size.y * 0.5f - p.y) / d.y;
            return Mathf.Max(0f, Mathf.Min(x, y));
        }

        bool AtEdge(Vector2 q) => Mathf.Abs(q.x) > m_Size.x * 0.499f || Mathf.Abs(q.y) > m_Size.y * 0.499f;

        void AddCracks(Vector2 p, float energy)
        {
            if (m_CrackCount >= 5 || m_CrackMaterial == null) return;
            m_CrackCount++;
            var web = Web(p, Mathf.Lerp(0.6f, 1.1f, energy));
            int rings = Mathf.RoundToInt(Mathf.Lerp(3f, k_Rings.Length, Mathf.Clamp01(energy * 1.5f)));
            var verts = new List<Vector3>(); var tris = new List<int>();
            const float z = 0.0062f; // juste devant et derrière la vitre (épaisseur 1 cm)

            void Line(Vector2 a, Vector2 b, float width)
            {
                if ((b - a).sqrMagnitude < 1e-6f) return;
                var n = (b - a).normalized; n = new Vector2(-n.y, n.x) * width * 0.5f;
                foreach (float side in new[] { z, -z })
                {
                    int i = verts.Count;
                    foreach (var q in new[] { a - n, a + n, b + n, b - n }) verts.Add(new Vector3(q.x, q.y, side));
                    tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3, i, i + 2, i + 1, i, i + 3, i + 2 });
                }
            }
            for (int k = 0; k < k_Rays; k++)
            {
                Line(p, web[0, k], 0.004f);
                for (int r = 0; r < rings; r++)
                {
                    if (r + 1 < rings) Line(web[r, k], web[r + 1, k], Mathf.Lerp(0.0035f, 0.0018f, r / (float)rings));
                    bool onEdge = AtEdge(web[r, k]) || AtEdge(web[r, (k + 1) % k_Rays]);
                    if (!onEdge && Random.value < 0.75f) Line(web[r, k], web[r, (k + 1) % k_Rays], 0.002f);
                }
            }
            var mesh = new Mesh { name = "Fissures vitre" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
            var go = new GameObject("Fissures");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m_CrackMaterial; mr.shadowCastingMode = ShadowCastingMode.Off;
            m_CrackObjects.Add(go);
        }

        void SpawnShards(Vector2 p, Vector3 direction, float energy)
        {
            var web = Web(p, 1.6f);
            var root = new GameObject(name + " (éclats)").transform;
            // Les éclats ne poussent ni ne cassent les bouteilles du frigo ; ils ignorent aussi la porte.
            var ignore = new List<Collider>();
            var body = GetComponentInParent<Rigidbody>();
            if (body != null) ignore.AddRange(body.GetComponentsInChildren<Collider>(true));
            foreach (var c in Physics.OverlapSphere(transform.position, 1.6f, ~0, QueryTriggerInteraction.Ignore))
                if (c.attachedRigidbody != null && c.attachedRigidbody.GetComponent<Debris>() == null) ignore.Add(c);

            // Les éclats partent dans le sens du coup (dedans ou dehors).
            float side = Vector3.Dot(direction, transform.forward) >= 0f ? 1f : -1f;
            int count = 0;
            for (int r = 0; r < k_Rings.Length - 1; r++)
                for (int k = 0; k < k_Rays; k++)
                {
                    if (count >= 26 || (r > 1 && Random.value > 0.7f)) continue;
                    int next = (k + 1) % k_Rays;
                    var poly = new[] { web[r, k], web[r + 1, k], web[r + 1, next], web[r, next] };
                    var center = (poly[0] + poly[1] + poly[2] + poly[3]) / 4f;
                    float area = 0f;
                    for (int i = 0; i < 4; i++) { var a = poly[i] - center; var b = poly[(i + 1) % 4] - center; area += (a.x * b.y - a.y * b.x) * 0.5f; }
                    if (Mathf.Abs(area) < 0.0003f) continue;
                    count++;

                    var go = new GameObject("Éclat de vitre " + count);
                    go.transform.SetParent(root, false);
                    go.transform.SetPositionAndRotation(transform.TransformPoint(new Vector3(center.x, center.y, 0f)), transform.rotation);
                    var mesh = Prism(poly, center, area > 0f);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = m_ShardMaterial; mr.shadowCastingMode = ShadowCastingMode.Off;
                    var col = go.AddComponent<BoxCollider>();
                    col.center = mesh.bounds.center; col.size = mesh.bounds.size;
                    foreach (var o in ignore) if (o != null) Physics.IgnoreCollision(col, o, true);
                    var rb = go.AddComponent<Rigidbody>();
                    rb.mass = 0.05f;
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    var away = (center - p).normalized;
                    rb.linearVelocity = transform.forward * side * Random.Range(0.3f, Mathf.Lerp(1f, 2.6f, energy))
                        + transform.TransformDirection(new Vector3(away.x, away.y, 0f)) * Random.Range(0.1f, 0.8f);
                    rb.angularVelocity = Random.insideUnitSphere * 8f;
                    go.AddComponent<Debris>().Init(count <= 10);
                }
        }

        /// <summary>Plaque de verre de 4 mm suivant le contour.</summary>
        static Mesh Prism(Vector2[] poly, Vector2 center, bool ccw)
        {
            const float h = 0.002f;
            int n = poly.Length;
            var v = new List<Vector3>(); var t = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(i); t.Add(i + 1); t.Add(i + 2); }
            for (int i = 0; i < n; i++)
            {
                var a = poly[i] - center; var b = poly[(i + 1) % n] - center;
                if (!ccw) (a, b) = (b, a);
                Vector3 af = new Vector3(a.x, a.y, h), bf = new Vector3(b.x, b.y, h), ab = new Vector3(a.x, a.y, -h), bb = new Vector3(b.x, b.y, -h);
                Tri(new Vector3(0f, 0f, h), af, bf);
                Tri(new Vector3(0f, 0f, -h), bb, ab);
                Tri(af, ab, bf); Tri(bf, ab, bb);
            }
            var m = new Mesh { name = "Éclat de vitre" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ effets

        void Burst(Vector3 point, Vector3 direction, float energy)
        {
            var fx = BreakFx.Get(m_FxPrefab);
            if (fx != null)
                fx.Play(new BreakFx.Settings
                {
                    point = point, direction = direction, energy = energy,
                    chip1 = new Color(0.85f, 0.93f, 0.97f), chip2 = new Color(0.6f, 0.72f, 0.78f), liquid = false
                });
        }

        static void PlaySound(AudioClip clip, Vector3 point, float volume, float pitch)
        {
            if (clip == null) return;
            var go = new GameObject("Son vitre");
            go.transform.position = point;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 1f; src.minDistance = 0.8f; src.maxDistance = 25f; src.dopplerLevel = 0f;
            src.volume = volume; src.pitch = pitch;
            src.Play();
            Destroy(go, clip.length / pitch + 0.2f);
        }
    }
}
