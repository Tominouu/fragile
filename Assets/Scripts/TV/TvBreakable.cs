using System.Collections;
using System.Collections.Generic;
using Fragile.Breakables;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fragile.TV
{
    /// <summary>
    /// Télé destructible, en trois temps selon la violence des coups reçus (objets lancés, batte) :
    /// 1) l'écran se fissure à l'endroit de l'impact et grésille ; 2) il explose : écran noir, éclats, étincelles ;
    /// 3) la télé se décroche et tombe. Un coup très violent saute une étape.
    /// Les dégâts s'additionnent : plusieurs petits jets finissent par la détruire.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TvBreakable : MonoBehaviour
    {
        [Header("Résistance")]
        [Tooltip("Vitesse d'impact minimale (m/s) pour abîmer la télé.")]
        [SerializeField] float m_MinSpeed = 2f;
        [Tooltip("Vitesse d'impact (m/s) considérée comme un coup maximal.")]
        [SerializeField] float m_MaxSpeed = 9f;
        [Tooltip("Dégâts cumulés (un coup maximal = 1) au-delà desquels l'écran explose.")]
        [SerializeField] float m_ScreenHealth = 0.6f;
        [Tooltip("Dégâts cumulés au-delà desquels la télé se décroche.")]
        [SerializeField] float m_MountHealth = 1.3f;
        [SerializeField] float m_Mass = 9f;

        [Header("Écran")]
        [SerializeField] Renderer m_Screen;
        [SerializeField] Vector2 m_Size = new Vector2(1.10f, 0.62f);
        [SerializeField] Material m_DeadMaterial, m_CrackMaterial, m_ShardMaterial, m_SparkMaterial;

        [Header("Effets")]
        [SerializeField] GameObject m_FxPrefab;
        [SerializeField] AudioClip m_GlassClip;

        static AudioClip s_Thud;

        Rigidbody m_Body;
        TvVideo m_Video;
        ParticleSystem m_Sparks;
        float m_Damage, m_LastHit = -10f, m_LastThud = -10f;
        int m_CrackCount;
        bool m_Dead, m_Fallen;
        Coroutine m_Flicker;

        public void Setup(Renderer screen, Vector2 size, Material dead, Material crack, Material shard, Material spark, GameObject fx, AudioClip glass)
        {
            m_Screen = screen; m_Size = size; m_DeadMaterial = dead; m_CrackMaterial = crack; m_ShardMaterial = shard;
            m_SparkMaterial = spark; m_FxPrefab = fx; m_GlassClip = glass;
        }

        void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
            m_Body.isKinematic = true;
            m_Video = GetComponentInParent<TvVideo>();
            // Préchargement : rien à fabriquer au moment du coup.
            if (s_Thud == null) s_Thud = SmashSynth.Create();
            Fragile.Audio.SfxBank.Preload(Fragile.Audio.SfxBank.ImpactLourd, Fragile.Audio.SfxBank.VerreVinCasse, Fragile.Audio.SfxBank.ShotCasse);
            if (m_GlassClip != null) m_GlassClip.LoadAudioData();
            m_Sparks = CreateSparks();
        }

        void Start()
        {
            // Les boîtiers se touchent dans les coins : ils ne doivent pas se repousser quand l'un d'eux tombe.
            if (transform.parent == null) return;
            var mine = GetComponentsInChildren<Collider>();
            foreach (var other in transform.parent.GetComponentsInChildren<TvBreakable>())
                if (other != this)
                    foreach (var c in other.GetComponentsInChildren<Collider>())
                        foreach (var m in mine) Physics.IgnoreCollision(m, c, true);
        }

        void OnCollisionEnter(Collision c)
        {
            float speed = c.relativeVelocity.magnitude;
            var point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;

            if (m_Fallen && (c.rigidbody == null || c.rigidbody.isKinematic))
            {
                // Choc au sol ou sur le comptoir.
                if (speed > 1.2f && Time.time - m_LastThud > 0.25f)
                {
                    m_LastThud = Time.time;
                    float e = Mathf.InverseLerp(1.2f, 7f, speed);
                    Thud(point, Mathf.Lerp(0.3f, 0.9f, e));
                    if (speed > 3f) Burst(point, Vector3.up, e * 0.6f);
                }
                return;
            }

            // Les éclats (bouteilles, télés) ne comptent pas comme des coups.
            if (c.rigidbody == null || c.rigidbody.GetComponent<Debris>() != null) return;
            if (speed < m_MinSpeed || Time.time - m_LastHit < 0.15f) return;

            var breaker = c.rigidbody.GetComponentInParent<Breaker>();
            float power = breaker != null ? speed * breaker.power : speed;
            float energy = Mathf.InverseLerp(m_MinSpeed, m_MaxSpeed, power);
            if (breaker != null) breaker.Haptics(energy);
            Hit(point, -c.relativeVelocity.normalized, energy);
        }

        /// <summary>Encaisse un coup. energy : 0 = juste au seuil, 1 = coup maximal.</summary>
        public void Hit(Vector3 point, Vector3 direction, float energy)
        {
            m_LastHit = Time.time;
            m_Damage += Mathf.Lerp(0.15f, 1f, energy);

            var local = transform.InverseTransformPoint(point);
            var p = new Vector2(Mathf.Clamp(local.x, -m_Size.x * 0.47f, m_Size.x * 0.47f), Mathf.Clamp(local.y, -m_Size.y * 0.47f, m_Size.y * 0.47f));
            var onScreen = transform.TransformPoint(new Vector3(p.x, p.y, 0.01f));

            if (m_Fallen)
            {
                m_Body.AddForceAtPosition(direction * Mathf.Lerp(3f, 14f, energy), point, ForceMode.Impulse);
                Thud(point, 0.5f);
                return;
            }

            if (!m_Dead)
            {
                AddCracks(p, energy);
                if (m_Damage >= m_ScreenHealth) Die(p, onScreen, energy);
                else
                {
                    PlaySound(m_GlassClip, onScreen, Mathf.Lerp(0.3f, 0.55f, energy), 1.35f);
                    Burst(onScreen, transform.forward, 0.12f);
                    if (m_Flicker != null) StopCoroutine(m_Flicker);
                    m_Flicker = StartCoroutine(Flicker());
                }
            }
            else
            {
                m_Sparks.transform.position = onScreen;
                m_Sparks.Emit(25);
                PlaySound(m_GlassClip, onScreen, 0.35f, 1.2f);
                AddCracks(p, energy);
            }

            if (m_Damage >= m_MountHealth) Fall(direction, point, energy);
        }

        // ------------------------------------------------------------------ étapes

        /// <summary>Image qui saute quelques instants après un choc.</summary>
        IEnumerator Flicker()
        {
            float end = Time.time + 0.7f;
            while (Time.time < end && !m_Dead)
            {
                m_Screen.enabled = !m_Screen.enabled;
                yield return new WaitForSeconds(Random.Range(0.03f, 0.11f));
            }
            m_Screen.enabled = !m_Dead;
        }

        void Die(Vector2 p, Vector3 onScreen, float energy)
        {
            m_Dead = true;
            if (m_Flicker != null) StopCoroutine(m_Flicker);
            m_Screen.enabled = false;
            if (m_Video != null) m_Video.ScreenBroken();

            // Dalle éteinte, à la place de l'image.
            var dead = new GameObject("Écran mort");
            dead.transform.SetParent(transform, false);
            dead.transform.localPosition = m_Screen.transform.localPosition;
            dead.transform.localRotation = m_Screen.transform.localRotation;
            dead.transform.localScale = m_Screen.transform.localScale;
            dead.AddComponent<MeshFilter>().sharedMesh = m_Screen.GetComponent<MeshFilter>().sharedMesh;
            dead.AddComponent<MeshRenderer>().sharedMaterial = m_DeadMaterial;

            SpawnShards(p, energy);
            Burst(onScreen, transform.forward, Mathf.Lerp(0.4f, 1f, energy));
            // Écran brisé : même fracas de verre que les bouteilles.
            if (!BreakableSounds.PlayGlassBreak(onScreen, 1f, Random.Range(0.95f, 1.05f)))
                PlaySound(m_GlassClip, onScreen, 1f, Random.Range(0.8f, 0.9f));
            StartCoroutine(Sparkle(onScreen));
        }

        /// <summary>Gerbes d'étincelles, de plus en plus espacées.</summary>
        IEnumerator Sparkle(Vector3 onScreen)
        {
            var local = transform.InverseTransformPoint(onScreen);
            m_Sparks.transform.localPosition = local;
            m_Sparks.Emit(60);
            for (int i = 0; i < 6; i++)
            {
                yield return new WaitForSeconds(Random.Range(0.15f, 0.5f) + i * 0.25f);
                m_Sparks.Emit(Random.Range(8, 26));
            }
        }

        void Fall(Vector3 direction, Vector3 point, float energy)
        {
            m_Fallen = true;
            m_LastThud = Time.time;
            transform.SetParent(null, true);
            m_Body.mass = m_Mass;
            m_Body.isKinematic = false;
            m_Body.interpolation = RigidbodyInterpolation.Interpolate;
            m_Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // Bascule vers l'avant, entraînée par le coup.
            m_Body.linearVelocity = transform.forward * 0.8f + direction * Mathf.Lerp(0.1f, 0.6f, energy);
            m_Body.angularVelocity = transform.right * Random.Range(1f, 2.2f) + transform.forward * Random.Range(-1f, 1f);
            m_Sparks.Emit(40);
            Thud(point, 0.6f);
        }

        // ------------------------------------------------------------------ fissures et éclats

        const int k_Rays = 11;
        static readonly float[] k_Rings = { 0.025f, 0.07f, 0.15f, 0.28f, 0.48f, 0.8f, 1.4f };

        /// <summary>Toile d'araignée autour de l'impact : sommets [anneau, rayon], bornés à l'écran.</summary>
        Vector2[,] Web(Vector2 p, float reach)
        {
            var v = new Vector2[k_Rings.Length, k_Rays];
            float start = Random.value * Mathf.PI * 2f;
            for (int k = 0; k < k_Rays; k++)
            {
                float baseAngle = start + (k + Random.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / k_Rays;
                for (int r = 0; r < k_Rings.Length; r++)
                {
                    float a = baseAngle + Random.Range(-0.09f, 0.09f); // fissures brisées
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

        void AddCracks(Vector2 p, float energy)
        {
            if (m_CrackCount >= 5) return;
            float z = 0.0085f + m_CrackCount * 0.0004f;
            m_CrackCount++;
            var web = Web(p, Mathf.Lerp(0.7f, 1.2f, energy));
            int rings = Mathf.RoundToInt(Mathf.Lerp(3f, k_Rings.Length, Mathf.Clamp01(energy * 1.5f)));
            var verts = new List<Vector3>(); var tris = new List<int>();

            void Line(Vector2 a, Vector2 b, float width)
            {
                if ((b - a).sqrMagnitude < 1e-6f) return;
                var n = (b - a).normalized; n = new Vector2(-n.y, n.x) * width * 0.5f;
                int i = verts.Count;
                foreach (var q in new[] { a - n, a + n, b + n, b - n }) verts.Add(new Vector3(q.x, q.y, z));
                // Deux faces : visible quel que soit le sens.
                tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3, i, i + 2, i + 1, i, i + 3, i + 2 });
            }
            for (int k = 0; k < k_Rays; k++)
            {
                Line(p, web[0, k], 0.005f);
                for (int r = 0; r < rings; r++)
                {
                    if (r + 1 < rings) Line(web[r, k], web[r + 1, k], Mathf.Lerp(0.0045f, 0.0024f, r / (float)rings));
                    bool onEdge = AtEdge(web[r, k]) || AtEdge(web[r, (k + 1) % k_Rays]);
                    if (!onEdge && Random.value < 0.8f) Line(web[r, k], web[r, (k + 1) % k_Rays], 0.0026f);
                }
            }
            var mesh = new Mesh { name = "Fissures" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
            var go = new GameObject("Fissures");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m_CrackMaterial; mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        bool AtEdge(Vector2 q) => Mathf.Abs(q.x) > m_Size.x * 0.499f || Mathf.Abs(q.y) > m_Size.y * 0.499f;

        void SpawnShards(Vector2 p, float energy)
        {
            var web = Web(p, 1f);
            var root = new GameObject(name + " (éclats)").transform;
            // Les éclats ne doivent ni pousser ni casser les objets posés sur le comptoir.
            var ignore = new List<Collider>(GetComponentsInChildren<Collider>());
            foreach (var c in Physics.OverlapSphere(transform.position, 4f, ~0, QueryTriggerInteraction.Ignore))
                if (c.attachedRigidbody != null && c.attachedRigidbody.GetComponent<Debris>() == null) ignore.Add(c);

            int count = 0;
            for (int r = 0; r < k_Rings.Length - 2; r++)
                for (int k = 0; k < k_Rays; k++)
                {
                    if (count >= 24 || Random.value > 0.6f) continue;
                    int next = (k + 1) % k_Rays;
                    var poly = new[] { web[r, k], web[r + 1, k], web[r + 1, next], web[r, next] };
                    var center = (poly[0] + poly[1] + poly[2] + poly[3]) / 4f;
                    float area = 0f;
                    for (int i = 0; i < 4; i++) { var a = poly[i] - center; var b = poly[(i + 1) % 4] - center; area += (a.x * b.y - a.y * b.x) * 0.5f; }
                    if (Mathf.Abs(area) < 0.0004f) continue;
                    count++;

                    var go = new GameObject("Éclat " + count);
                    go.transform.SetParent(root, false);
                    go.transform.SetPositionAndRotation(transform.TransformPoint(new Vector3(center.x, center.y, 0.02f)), transform.rotation);
                    var mesh = Prism(poly, center, area > 0f);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = m_ShardMaterial;
                    var col = go.AddComponent<BoxCollider>();
                    col.center = mesh.bounds.center; col.size = mesh.bounds.size;
                    foreach (var o in ignore) if (o != null) Physics.IgnoreCollision(col, o, true);
                    var rb = go.AddComponent<Rigidbody>();
                    rb.mass = 0.04f;
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    var away = (center - p).normalized;
                    rb.linearVelocity = transform.forward * Random.Range(0.4f, Mathf.Lerp(1.2f, 3f, energy))
                        + transform.TransformDirection(new Vector3(away.x, away.y, 0f)) * Random.Range(0.2f, 1f) + Vector3.up * Random.Range(0f, 0.6f);
                    rb.angularVelocity = Random.insideUnitSphere * 9f;
                    go.AddComponent<Debris>().Init(count <= 9);
                }
        }

        /// <summary>Plaque de verre de 4 mm suivant le contour (les maillages sont petits et détruits avec l'éclat).</summary>
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
                // Unity : une face est visible dans le sens horaire.
                Tri(new Vector3(0f, 0f, h), af, bf);
                Tri(new Vector3(0f, 0f, -h), bb, ab);
                Tri(af, ab, bf); Tri(bf, ab, bb);
            }
            var m = new Mesh { name = "Éclat écran" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ effets et sons

        void Burst(Vector3 point, Vector3 direction, float energy)
        {
            var fx = BreakFx.Get(m_FxPrefab);
            if (fx != null)
                fx.Play(new BreakFx.Settings
                {
                    point = point, direction = direction, energy = energy,
                    chip1 = new Color(0.12f, 0.13f, 0.15f), chip2 = new Color(0.55f, 0.6f, 0.65f), liquid = false
                });
        }

        ParticleSystem CreateSparks()
        {
            var go = new GameObject("Étincelles");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false; main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 4.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.010f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.45f), new Color(0.75f, 0.9f, 1f));
            main.gravityModifier = 0.9f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 55f; shape.radius = 0.04f;
            var fade = ps.colorOverLifetime; fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.35f, 0.08f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m_SparkMaterial;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.03f; r.lengthScale = 1.5f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            go.SetActive(true);
            return ps;
        }

        /// <summary>Choc du boîtier (coup, chute, sol) : impact lourd enregistré, sinon le son généré.</summary>
        static void Thud(Vector3 point, float volume)
        {
            if (!Fragile.Audio.SfxBank.Play(Fragile.Audio.SfxBank.ImpactLourd, point, volume, Random.Range(0.85f, 1f)))
                PlaySound(s_Thud, point, volume, 0.6f);
        }

        static void PlaySound(AudioClip clip, Vector3 point, float volume, float pitch)
        {
            if (clip == null) return;
            var go = new GameObject("Son télé");
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
