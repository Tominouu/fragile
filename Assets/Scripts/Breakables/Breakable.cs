using System.Collections;
using UnityEngine;

namespace Fragile.Breakables
{
    /// <summary>
    /// Objet qui vole en éclats : coup de batte (<see cref="Breaker"/>) assez rapide, ou chute violente.
    /// Tout dépend de l'énergie du coup :
    /// - découpe : 3 niveaux pré-calculés (gros morceaux -> pulvérisé) ;
    /// - vitesse et direction des éclats, effets (éclats 3D, poussière, liquide), son, vibration, mini-ralenti.
    /// Les éclats et l'effet sont préparés dans l'éditeur (Fragile > Rendre cassable) et préchargés : rien de lourd au moment du coup.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Breakable : MonoBehaviour
    {
        [System.Serializable]
        public class ShardSet
        {
            public Mesh[] meshes;
            public Vector3[] offsets; // centre de chaque éclat, repère du maillage
        }

        [Header("Déclenchement")]
        [Tooltip("Vitesse d'impact minimale (m/s) avec un Breaker (la batte).")]
        [SerializeField] float m_BreakSpeedWithBreaker = 1.6f;
        [Tooltip("Vitesse d'impact minimale (m/s) contre n'importe quoi (chute, lancer).")]
        [SerializeField] float m_BreakSpeedAny = 4f;
        [Tooltip("Vitesse d'impact (m/s) considérée comme un coup maximal.")]
        [SerializeField] float m_MaxImpactSpeed = 9f;

        [Header("Éclats (générés par Fragile > Rendre cassable)")]
        [Tooltip("Du plus gros au plus fin : coup mou -> niveau 0, coup violent -> dernier niveau.")]
        [SerializeField] ShardSet[] m_Levels;
        [SerializeField] Material[] m_ShardMaterials;   // matériaux d'origine + intérieur/tranche en dernier
        [SerializeField] Transform m_MeshTransform;
        [SerializeField] float m_Mass = 1.2f;
        [Tooltip("Nombre de plus gros morceaux qui restent au sol après la casse (les miettes disparaissent).")]
        [SerializeField] int m_PiecesLeftOnGround = 7;

        [Header("Effets")]
        [SerializeField] GameObject m_FxPrefab;
        [SerializeField] Color m_ChipColor = new Color(0.95f, 0.95f, 0.97f);
        [SerializeField] Color m_ChipColor2 = new Color(0.16f, 0.26f, 0.75f);
        [SerializeField] bool m_Liquid = true;
        [SerializeField] Color m_LiquidColor = new Color(0.85f, 0.55f, 0.15f, 0.85f);
        [Tooltip("Ralenti maximal à l'impact (secondes, 0 = aucun). Seuls les coups francs le déclenchent.")]
        [SerializeField] float m_HitStop = 0.11f;

        bool m_Broken;
        float m_SpawnTime;
        static AudioClip s_SmashClip;
        static bool s_HitStopRunning;

        void Awake()
        {
            m_SpawnTime = Time.time;
            // Préchargement : le son et un effet sont prêts avant le premier coup.
            if (s_SmashClip == null) s_SmashClip = SmashSynth.Create();
            BreakFx.Prewarm(m_FxPrefab, 2);
        }

        void OnCollisionEnter(Collision c)
        {
            if (m_Broken || Time.time - m_SpawnTime < 0.5f) return;

            var breaker = c.rigidbody != null ? c.rigidbody.GetComponentInParent<Breaker>() : c.collider.GetComponentInParent<Breaker>();
            float speed = c.relativeVelocity.magnitude;
            bool hitByBreaker = breaker != null && speed * breaker.power >= m_BreakSpeedWithBreaker;
            if (!hitByBreaker && speed < m_BreakSpeedAny) return;

            float power = breaker != null ? speed * breaker.power : speed;
            var point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            // Direction du coup : mouvement de ce qui frappe (la batte), sinon sens de l'impact.
            var dir = breaker != null && c.rigidbody != null && c.rigidbody.linearVelocity.sqrMagnitude > 0.01f
                ? c.rigidbody.linearVelocity.normalized
                : -c.relativeVelocity.normalized;

            float minSpeed = hitByBreaker ? m_BreakSpeedWithBreaker : m_BreakSpeedAny;
            Break(point, dir, power, Mathf.InverseLerp(minSpeed, m_MaxImpactSpeed, power));
            if (breaker != null) breaker.Haptics(Mathf.InverseLerp(minSpeed, m_MaxImpactSpeed, power));
        }

        /// <summary>Casse l'objet. energy : 0 = juste au seuil, 1 = coup maximal.</summary>
        public void Break(Vector3 point, Vector3 direction, float power, float energy)
        {
            if (m_Broken) return;
            m_Broken = true;
            energy = Mathf.Clamp01(energy);

            var inherited = GetComponent<Rigidbody>().linearVelocity;
            SpawnShards(point, direction, power, energy, inherited);

            var fx = BreakFx.Get(m_FxPrefab);
            if (fx != null)
                fx.Play(new BreakFx.Settings
                {
                    point = point, direction = direction, energy = energy,
                    chip1 = m_ChipColor, chip2 = m_ChipColor2, liquid = m_Liquid, liquidColor = m_LiquidColor
                });

            PlaySound(point, energy);
            if (m_HitStop > 0f && energy > 0.3f && !s_HitStopRunning)
                StaticRunner.Run(HitStop(m_HitStop * Mathf.Lerp(0.45f, 1f, energy)));

            ReleaseHands();
            gameObject.SetActive(false);
            Destroy(gameObject, 0.1f);
        }

        /// <summary>
        /// Les mains qui tiennent l'objet y sont rattachées : on les rend à leur manette AVANT de désactiver l'objet
        /// (sinon elles seraient détruites avec lui ; Unity interdit de les déplacer pendant la désactivation).
        /// </summary>
        void ReleaseHands()
        {
            if (TryGetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>(out var grab) && grab.isSelected && grab.interactionManager != null)
                grab.interactionManager.CancelInteractableSelection((UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)grab);
            // Sécurité : toute main encore rattachée retourne sur sa manette.
            foreach (var hand in GetComponentsInChildren<Fragile.Hands.ControllerHandPose>(true))
                hand.EndGrab();
        }

        // ------------------------------------------------------------------ éclats

        void SpawnShards(Vector3 point, Vector3 dir, float power, float energy, Vector3 inherited)
        {
            if (m_Levels == null || m_Levels.Length == 0) return;
            var set = m_Levels[Mathf.Clamp(Mathf.FloorToInt(energy * m_Levels.Length), 0, m_Levels.Length - 1)];
            if (set == null || set.meshes == null || set.meshes.Length == 0) return;

            var t = m_MeshTransform != null ? m_MeshTransform : transform;
            var root = new GameObject(name + " (éclats)").transform;
            float totalVolume = 0f;
            foreach (var m in set.meshes) if (m != null) totalVolume += Volume(m);

            // Un coup mou fait surtout tomber les morceaux ; un coup violent les projette.
            float push = Mathf.Lerp(0.15f, 0.8f, energy);
            float burst = Mathf.Lerp(0.3f, 2f, energy);
            float spin = Mathf.Lerp(3f, 20f, energy);

            // Colliders des objets attrapables autour de la casse (à ignorer par les morceaux).
            var nearbyInteractables = new System.Collections.Generic.List<Collider>();
            foreach (var c in Physics.OverlapSphere(point, 3f, ~0, QueryTriggerInteraction.Ignore))
                if (c.attachedRigidbody != null && c.attachedRigidbody.gameObject != gameObject &&
                    c.attachedRigidbody.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>() != null)
                    nearbyInteractables.Add(c);

            // Les plus gros morceaux restent au sol ; les autres (miettes) disparaîtront une fois posés.
            var order = new System.Collections.Generic.List<int>();
            for (int i = 0; i < set.meshes.Length; i++) if (set.meshes[i] != null) order.Add(i);
            order.Sort((a, b) => Volume(set.meshes[b]).CompareTo(Volume(set.meshes[a])));
            var keep = new System.Collections.Generic.HashSet<int>();
            for (int k = 0; k < order.Count && k < m_PiecesLeftOnGround; k++) keep.Add(order[k]);

            for (int i = 0; i < set.meshes.Length; i++)
            {
                var mesh = set.meshes[i];
                if (mesh == null) continue;
                var go = new GameObject("Éclat " + i);
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(t.TransformPoint(set.offsets[i]), t.rotation);
                go.transform.localScale = t.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = m_ShardMaterials;

                var col = go.AddComponent<BoxCollider>();
                col.center = mesh.bounds.center; col.size = Vector3.Max(mesh.bounds.size, Vector3.one * 0.004f);
                // En vol, les morceaux ignorent les objets attrapables (bouteilles, batte, puffs) : ils ne les poussent pas
                // et ne les cassent pas. Ils rebondissent toujours sur le décor (sol, comptoir, murs).
                foreach (var other in nearbyInteractables) Physics.IgnoreCollision(col, other, true);

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = Mathf.Max(0.02f, m_Mass * Volume(mesh) / Mathf.Max(1e-6f, totalVolume));
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                // Les morceaux proches de l'impact partent plus vite et plus dans le sens du coup.
                var fromImpact = go.transform.position - point;
                float near = 1f / (1f + fromImpact.magnitude * 8f);
                rb.linearVelocity = inherited
                    + dir * power * Random.Range(0.6f, 1f) * push * (0.4f + near)
                    + fromImpact.normalized * Random.Range(0.5f, 1f) * burst
                    + Random.insideUnitSphere * 0.3f * (0.5f + energy)
                    + Vector3.up * Random.Range(0.1f, 0.8f) * energy;
                rb.angularVelocity = Random.insideUnitSphere * spin;

                go.AddComponent<Debris>().Init(keep.Contains(i));
            }
        }

        static float Volume(Mesh m) { var s = m.bounds.size; return Mathf.Max(1e-6f, s.x * s.y * s.z); }

        // ------------------------------------------------------------------ son

        void PlaySound(Vector3 point, float energy)
        {
            if (s_SmashClip == null) s_SmashClip = SmashSynth.Create();
            var go = new GameObject("Son casse");
            go.transform.position = point;
            var src = go.AddComponent<AudioSource>();
            src.clip = s_SmashClip;
            src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Logarithmic; src.minDistance = 0.6f; src.maxDistance = 25f;
            src.volume = Mathf.Lerp(0.45f, 1f, energy);
            // Coup violent : un peu plus grave et plus "gros" ; coup mou : plus aigu, plus fin.
            src.pitch = Mathf.Lerp(1.12f, 0.92f, energy) * Random.Range(0.96f, 1.04f);
            src.dopplerLevel = 0f;
            src.Play();
            Destroy(go, s_SmashClip.length / src.pitch + 0.2f);
        }

        // ------------------------------------------------------------------ ralenti

        static IEnumerator HitStop(float seconds)
        {
            s_HitStopRunning = true;
            float previous = Time.timeScale;
            Time.timeScale = 0.15f;
            yield return new WaitForSecondsRealtime(seconds);
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.08f)
            {
                Time.timeScale = Mathf.Lerp(0.15f, previous, t);
                yield return null;
            }
            Time.timeScale = previous;
            s_HitStopRunning = false;
        }

        /// <summary>Lance une coroutine qui survit à la destruction de l'objet cassé.</summary>
        class StaticRunner : MonoBehaviour
        {
            static StaticRunner s_Instance;
            public static void Run(IEnumerator routine)
            {
                if (s_Instance == null)
                {
                    var go = new GameObject("[Fragile] Breakable Runner") { hideFlags = HideFlags.HideAndDontSave };
                    DontDestroyOnLoad(go);
                    s_Instance = go.AddComponent<StaticRunner>();
                }
                s_Instance.StartCoroutine(routine);
            }
        }
    }
}
