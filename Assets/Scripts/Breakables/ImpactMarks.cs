using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fragile.Breakables
{
    /// <summary>
    /// Surface qui garde la trace des coups de batte (le marbre du comptoir) : à chaque coup assez fort d'un
    /// <see cref="Breaker"/>, un impact (cratère + fissures) est posé au point de contact, avec éclats de pierre, son et vibration.
    /// Marques = petits quads (shader Fragile/Impact, mélange alpha éclairé par les light probes) recyclés au-delà d'un plafond.
    /// Une marque qui déborderait d'une arête (bord du comptoir) est réduite, ou abandonnée.
    /// </summary>
    public class ImpactMarks : MonoBehaviour
    {
        [SerializeField] Material m_Material;
        [SerializeField] GameObject m_FxPrefab;
        [Tooltip("Vitesse d'impact minimale (m/s, puissance de la batte comprise).")]
        [SerializeField] float m_MinSpeed = 2.2f;
        [SerializeField] float m_MaxSpeed = 9f;
        [Tooltip("Taille de la marque (m) : coup faible -> coup maximal. Le cratère fait environ le quart de la marque.")]
        [SerializeField] Vector2 m_SizeRange = new Vector2(0.14f, 0.28f);
        [SerializeField] int m_MaxMarks = 40;
        [SerializeField] Color m_Chip1 = new Color(0.82f, 0.74f, 0.62f), m_Chip2 = new Color(0.35f, 0.25f, 0.18f);

        static Mesh[] s_Quads;
        static AudioClip s_Knock;
        static Transform s_Root;

        Collider[] m_Colliders;
        readonly Queue<GameObject> m_Marks = new Queue<GameObject>();
        float m_LastMark = -10f;

        public void Setup(Material material, GameObject fx) { m_Material = material; m_FxPrefab = fx; }

        void Awake()
        {
            m_Colliders = GetComponentsInChildren<Collider>();
            if (s_Quads == null) s_Quads = CreateQuads();
            if (s_Knock == null) s_Knock = CreateKnock();
            BreakFx.Prewarm(m_FxPrefab, 1);
        }

        void OnCollisionEnter(Collision c)
        {
            if (c.rigidbody == null || c.contactCount == 0) return;
            var breaker = c.rigidbody.GetComponentInParent<Breaker>();
            if (breaker == null) return;
            float power = c.relativeVelocity.magnitude * breaker.power;
            if (power < m_MinSpeed || Time.time - m_LastMark < 0.08f) return;
            m_LastMark = Time.time;

            float energy = Mathf.InverseLerp(m_MinSpeed, m_MaxSpeed, power);
            var contact = c.GetContact(0);
            var n = contact.normal;
            if (Vector3.Dot(n, c.rigidbody.worldCenterOfMass - contact.point) < 0f) n = -n; // vers la batte

            Mark(contact.point, n, energy);
            breaker.Haptics(energy * 0.8f);
            var fx = BreakFx.Get(m_FxPrefab);
            if (fx != null)
                fx.Play(new BreakFx.Settings { point = contact.point, direction = n, energy = Mathf.Lerp(0.05f, 0.35f, energy), chip1 = m_Chip1, chip2 = m_Chip2, liquid = false });
            PlayKnock(contact.point, energy);
        }

        /// <summary>Pose une marque. energy : 0 = coup juste au seuil, 1 = coup maximal.</summary>
        public void Mark(Vector3 point, Vector3 normal, float energy)
        {
            if (m_Material == null) return;
            float size = Mathf.Lerp(m_SizeRange.x, m_SizeRange.y, energy) * Random.Range(0.85f, 1.15f);
            var rot = Quaternion.LookRotation(normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            // La marque doit reposer à plat sur la surface : sinon on la réduit (près d'une arête), puis on renonce.
            if (!FitsFlat(point, normal, rot, size)) { size *= 0.55f; if (!FitsFlat(point, normal, rot, size)) return; }

            GameObject go;
            if (m_Marks.Count >= m_MaxMarks) go = m_Marks.Dequeue();
            else
            {
                if (s_Root == null) s_Root = new GameObject("Impacts").transform;
                go = new GameObject("Impact");
                go.transform.SetParent(s_Root, false);
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = m_Material;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.BlendProbes;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            go.GetComponent<MeshFilter>().sharedMesh = s_Quads[Random.Range(0, s_Quads.Length)];
            go.transform.SetPositionAndRotation(point + normal * 0.0006f, rot);
            go.transform.localScale = Vector3.one * size;
            m_Marks.Enqueue(go);
        }

        bool FitsFlat(Vector3 point, Vector3 normal, Quaternion rot, float size)
        {
            var right = rot * Vector3.right * size * 0.45f;
            var up = rot * Vector3.up * size * 0.45f;
            foreach (var corner in new[] { right + up, right - up, -right + up, -right - up })
            {
                var ray = new Ray(point + corner + normal * 0.01f, -normal);
                bool hit = false;
                foreach (var col in m_Colliders)
                    if (col.Raycast(ray, out var h, 0.02f) && Mathf.Abs(h.distance - 0.01f) < 0.004f) { hit = true; break; }
                if (!hit) return false;
            }
            return true;
        }

        /// <summary>4 quads (un par case de l'atlas 2×2 de la texture), face vers +Z, centrés.</summary>
        static Mesh[] CreateQuads()
        {
            var quads = new Mesh[4];
            for (int i = 0; i < 4; i++)
            {
                var o = new Vector2(i % 2, i / 2) * 0.5f;
                var m = new Mesh { name = "Impact " + i };
                m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
                m.uv = new[] { o, o + new Vector2(0.5f, 0f), o + new Vector2(0.5f, 0.5f), o + new Vector2(0f, 0.5f) };
                m.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
                m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                m.RecalculateBounds();
                quads[i] = m;
            }
            return quads;
        }

        static void PlayKnock(Vector3 point, float energy)
        {
            var go = new GameObject("Son marbre");
            go.transform.position = point;
            var src = go.AddComponent<AudioSource>();
            src.clip = s_Knock;
            src.spatialBlend = 1f; src.minDistance = 0.6f; src.maxDistance = 20f; src.dopplerLevel = 0f;
            src.volume = Mathf.Lerp(0.35f, 1f, energy); src.pitch = Random.Range(0.9f, 1.12f);
            src.Play();
            Destroy(go, s_Knock.length / src.pitch + 0.2f);
        }

        /// <summary>Choc sur la pierre généré par le code : claquement sec et aigu + petit grésillement d'éclats.</summary>
        static AudioClip CreateKnock()
        {
            const int rate = 44100;
            int n = (int)(rate * 0.3f);
            var d = new float[n];
            var rng = new System.Random(11);
            float hp = 0f, prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float noise = (float)rng.NextDouble() * 2f - 1f;
                hp = 0.85f * (hp + noise - prev); prev = noise;                     // bruit aigu (passe-haut)
                float crack = hp * Mathf.Exp(-t * 55f);
                float tone = (Mathf.Sin(2f * Mathf.PI * 1250f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 2300f * t) * 0.3f) * Mathf.Exp(-t * 35f);
                float grit = (rng.NextDouble() < 0.004 * Mathf.Exp(-t * 10f) ? noise : 0f) * 0.6f;
                d[i] = Mathf.Clamp((crack * 0.8f + tone * 0.5f + grit) * 0.9f, -1f, 1f);
            }
            var clip = AudioClip.Create("Coup sur marbre", n, 1, rate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
