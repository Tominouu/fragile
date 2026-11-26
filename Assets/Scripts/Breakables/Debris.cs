using System.Collections.Generic;
using UnityEngine;

namespace Fragile.Breakables
{
    /// <summary>
    /// Morceau d'objet cassé.
    /// - Tant qu'il bouge : physique normale.
    /// - Une fois posé SUR UNE VRAIE SURFACE (pas sur un autre débris) : il perd son Rigidbody, son collider est coupé et son
    ///   composant s'arrête : décor purement visuel, gratuit.
    /// - Si ce qui le porte disparaît ou bouge (bouteille cassée, table cassée, miette effacée, objet ramassé), il retrouve
    ///   sa physique et retombe : jamais de morceau suspendu en l'air.
    /// - Les gros morceaux restent au sol ; les miettes disparaissent une fois posées.
    /// - Plafond global : au-delà de <see cref="MaxPersistent"/> morceaux au sol, les plus anciens s'effacent en douceur.
    /// </summary>
    public class Debris : MonoBehaviour
    {
        /// <summary>Nombre maximum de morceaux laissés au sol dans la scène (réglé pour Quest).</summary>
        public static int MaxPersistent = 140;

        static readonly LinkedList<Debris> s_Persistent = new LinkedList<Debris>();
        static readonly HashSet<Debris> s_All = new HashSet<Debris>();
        static readonly List<Debris> s_Watched = new List<Debris>();
        static readonly Collider[] s_Overlap = new Collider[16];
        static readonly RaycastHit[] s_Rays = new RaycastHit[16];

        /// <summary>Tous les morceaux présents dans la scène (pas sur les débris).</summary>
        public static IReadOnlyCollection<Debris> All => s_All;

        /// <summary>Le choc vient-il d'un morceau d'objet cassé ? (un débris ne casse ni ne tue rien)</summary>
        public static bool IsDebris(Collision c)
        {
            if (c.rigidbody != null) return c.rigidbody.GetComponent<Debris>() != null;
            return c.collider != null && c.collider.GetComponent<Debris>() != null;
        }

        const float k_SettleSpeed = 0.06f;
        const float k_SettleTime = 0.35f;
        const float k_MaxMoveTime = 8f;
        const float k_FadeTime = 0.6f;
        const float k_SupportReach = 0.025f;   // écart maximal entre le bas du morceau et ce qui le porte
        const float k_SupportMoved = 0.02f;    // déplacement du support qui fait retomber le morceau

        Rigidbody m_Body;
        Collider m_Collider;
        Renderer m_Renderer;
        bool m_KeepOnGround;
        float m_Spawn, m_StillFor, m_FadeStart = -1f;
        Vector3 m_Scale;
        LinkedListNode<Debris> m_Node;

        // Physique mémorisée pour la rendre si le morceau doit retomber.
        float m_Mass = 0.05f;
        RigidbodyInterpolation m_Interpolation = RigidbodyInterpolation.Interpolate;
        CollisionDetectionMode m_Detection = CollisionDetectionMode.ContinuousSpeculative;

        // Ce sur quoi il est posé.
        Collider m_Support;
        Vector3 m_SupportPos;
        Quaternion m_SupportRot;
        int m_WatchIndex = -1;

        public void Init(bool keepOnGround)
        {
            m_KeepOnGround = keepOnGround;
            s_All.Add(this);
            m_Body = GetComponent<Rigidbody>();
            m_Collider = GetComponent<Collider>();
            m_Renderer = GetComponent<Renderer>();
            m_Spawn = Time.time;
            m_Scale = transform.localScale;
            // Les débris ne projettent pas d'ombre : invisible à l'œil, coûteux sur Quest.
            if (m_Renderer != null) m_Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void FixedUpdate()
        {
            if (m_Body == null || m_FadeStart >= 0f) return;
            bool still = m_Body.IsSleeping() ||
                         (m_Body.linearVelocity.sqrMagnitude < k_SettleSpeed * k_SettleSpeed && m_Body.angularVelocity.sqrMagnitude < 0.25f);
            m_StillFor = still ? m_StillFor + Time.fixedDeltaTime : 0f;
            bool timeout = Time.time - m_Spawn > k_MaxMoveTime;
            if (m_StillFor < k_SettleTime && !timeout) return;

            var support = FindSupport();
            if (support != null) { Settle(support); return; }
            if (timeout) { DropToSurface(); return; }
            // Immobile mais posé sur un autre débris encore en mouvement : on continue la physique, on réessaiera.
            m_StillFor = k_SettleTime * 0.5f;
        }

        /// <summary>Ce qui porte le morceau : une surface juste sous lui, qui n'est pas un autre débris.</summary>
        Collider FindSupport()
        {
            var b = m_Renderer != null ? m_Renderer.bounds : new Bounds(transform.position, Vector3.one * 0.01f);
            var center = new Vector3(b.center.x, b.min.y - k_SupportReach * 0.5f, b.center.z);
            var half = new Vector3(Mathf.Max(0.003f, b.extents.x * 0.7f), k_SupportReach * 0.5f + 0.004f, Mathf.Max(0.003f, b.extents.z * 0.7f));
            int n = Physics.OverlapBoxNonAlloc(center, half, s_Overlap, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            Collider best = null;
            for (int i = 0; i < n; i++)
            {
                var c = s_Overlap[i];
                if (c == m_Collider || c.GetComponent<Debris>() != null) continue;
                if (c.attachedRigidbody == null) return c;   // décor statique : le support le plus sûr
                best = c;
            }
            return best;
        }

        /// <summary>Bloqué trop longtemps sans support (coincé sur d'autres débris) : posé directement sur la surface en dessous.</summary>
        void DropToSurface()
        {
            var b = m_Renderer != null ? m_Renderer.bounds : new Bounds(transform.position, Vector3.zero);
            int n = Physics.RaycastNonAlloc(b.center, Vector3.down, s_Rays, 5f, ~0, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            Collider best = null;
            Vector3 point = default;
            for (int i = 0; i < n; i++)
            {
                var h = s_Rays[i];
                if (h.collider == m_Collider || h.collider.GetComponent<Debris>() != null || h.distance >= bestDist) continue;
                bestDist = h.distance; best = h.collider; point = h.point;
            }
            if (best == null) { FadeOut(); return; }   // rien en dessous (hors de la pièce)
            transform.position += Vector3.up * (point.y - b.min.y);
            Settle(best);
        }

        void Settle(Collider support)
        {
            // Plus de physique ni de collision : le morceau devient un simple décor visuel,
            // que rien ne heurte (mains, bouteilles, batte) et qui ne coûte plus aucun calcul.
            if (m_Body != null)
            {
                m_Mass = m_Body.mass; m_Interpolation = m_Body.interpolation; m_Detection = m_Body.collisionDetectionMode;
                Destroy(m_Body);
                m_Body = null;
            }
            if (m_Collider != null) m_Collider.enabled = false;
            if (!m_KeepOnGround) { FadeOut(); return; }
            enabled = false; // plus d'Update/FixedUpdate tant qu'il ne doit pas s'effacer ou retomber

            m_Support = support;
            m_SupportPos = support.transform.position;
            m_SupportRot = support.transform.rotation;
            m_WatchIndex = s_Watched.Count;
            s_Watched.Add(this);
            Watcher.Ensure();

            m_Node = s_Persistent.AddLast(this);
            while (s_Persistent.Count > MaxPersistent)
            {
                var oldest = s_Persistent.First.Value;
                s_Persistent.RemoveFirst();
                oldest.m_Node = null;
                oldest.FadeOut();
            }
        }

        /// <summary>Le support a disparu, a été désactivé ou a bougé.</summary>
        bool SupportLost()
        {
            if (m_Support == null || !m_Support.enabled || !m_Support.gameObject.activeInHierarchy) return true;
            var t = m_Support.transform;
            return (t.position - m_SupportPos).sqrMagnitude > k_SupportMoved * k_SupportMoved ||
                   Quaternion.Angle(t.rotation, m_SupportRot) > 3f;
        }

        /// <summary>Rend la physique au morceau : il retombe et se reposera plus bas.</summary>
        void Wake()
        {
            Unwatch();
            if (m_Node != null) { s_Persistent.Remove(m_Node); m_Node = null; }
            if (m_FadeStart >= 0f || m_Body != null) return;
            if (m_Collider != null) m_Collider.enabled = true;
            m_Body = gameObject.AddComponent<Rigidbody>();
            m_Body.mass = m_Mass;
            m_Body.interpolation = m_Interpolation;
            m_Body.collisionDetectionMode = m_Detection;
            m_Spawn = Time.time;
            m_StillFor = 0f;
            enabled = true;
        }

        void Unwatch()
        {
            if (m_WatchIndex < 0) return;
            int last = s_Watched.Count - 1;
            if (m_WatchIndex < last) { var moved = s_Watched[last]; s_Watched[m_WatchIndex] = moved; moved.m_WatchIndex = m_WatchIndex; }
            s_Watched.RemoveAt(last);
            m_WatchIndex = -1;
            m_Support = null;
        }

        public void FadeOut()
        {
            if (m_FadeStart >= 0f) return;
            m_FadeStart = Time.time;
            Unwatch();
            if (m_Collider != null) m_Collider.enabled = false;
            enabled = true;
        }

        void Update()
        {
            if (m_FadeStart < 0f) return;
            float t = (Time.time - m_FadeStart) / k_FadeTime;
            if (t >= 1f) { Destroy(gameObject); return; }
            transform.localScale = m_Scale * (1f - t * t);
        }

        void OnDestroy()
        {
            s_All.Remove(this);
            Unwatch();
            if (m_Node != null) { s_Persistent.Remove(m_Node); m_Node = null; }
            // Le conteneur des éclats disparaît avec son dernier morceau.
            var parent = transform.parent;
            if (parent != null && parent.childCount <= 1 && parent.name.EndsWith("(éclats)") && Application.isPlaying)
                Destroy(parent.gameObject);
        }

        /// <summary>
        /// Surveille les morceaux posés, quelques-uns par image (coût négligeable même avec 140 morceaux) :
        /// celui dont le support a disparu ou bougé retombe.
        /// </summary>
        class Watcher : MonoBehaviour
        {
            const int k_PerFrame = 12;
            static Watcher s_Instance;
            int m_Cursor;

            public static void Ensure()
            {
                if (s_Instance != null) return;
                var go = new GameObject("[Fragile] Debris Watcher") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                s_Instance = go.AddComponent<Watcher>();
            }

            void Update()
            {
                int count = Mathf.Min(s_Watched.Count, k_PerFrame);
                for (int k = 0; k < count; k++)
                {
                    if (s_Watched.Count == 0) return;
                    if (m_Cursor >= s_Watched.Count) m_Cursor = 0;
                    var d = s_Watched[m_Cursor++];
                    if (d == null) continue;
                    if (d.SupportLost()) d.Wake();
                }
            }
        }
    }
}
