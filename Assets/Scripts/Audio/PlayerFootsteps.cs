using Fragile.Breakables;
using UnityEngine;

namespace Fragile.Audio
{
    /// <summary>
    /// Pas du joueur : quand il se déplace (marche réelle ou joystick), un pas est joué à chaque foulée.
    /// Sol nu : pas sur le parquet. Morceaux d'objets cassés sous les pieds : pas qui écrase des débris.
    /// Se lance tout seul au chargement de la scène, rien à placer.
    /// </summary>
    public class PlayerFootsteps : MonoBehaviour
    {
        const float k_Stride = 0.6f;        // distance entre deux pas (m)
        const float k_MinSpeed = 0.35f;     // en dessous : le joueur se penche ou regarde autour, il ne marche pas
        const float k_Radius = 0.45f;       // débris pris en compte autour des pieds
        const float k_MinHeight = 0.8f, k_MaxHeight = 2.2f;   // tête au-dessus du sol : debout (pas assis sur le comptoir)

        Transform m_Head;
        Vector2 m_Last;
        float m_Travel;
        bool m_HasLast;
        int m_Mask;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<PlayerFootsteps>() != null) return;
            var go = new GameObject("[Fragile] Pas du joueur");
            DontDestroyOnLoad(go);
            go.AddComponent<PlayerFootsteps>();
            SfxBank.Preload(SfxBank.PasDebris, SfxBank.PasParquet);
        }

        void Awake() => m_Mask = Physics.DefaultRaycastLayers;

        void Update()
        {
            if (m_Head == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                m_Head = cam.transform; m_HasLast = false;
            }

            var p = m_Head.position;
            var xz = new Vector2(p.x, p.z);
            if (!m_HasLast) { m_Last = xz; m_HasLast = true; return; }
            float d = (xz - m_Last).magnitude;
            m_Last = xz;
            if (d > 1f || Time.deltaTime <= 0f) return;                       // téléportation
            if (d / Time.deltaTime < k_MinSpeed) return;
            m_Travel += d;
            if (m_Travel < k_Stride) return;
            m_Travel = 0f;

            if (!Ground(p, out float floor)) return;
            var foot = new Vector3(p.x, floor, p.z);

            int count = 0;
            foreach (var debris in Debris.All)
            {
                if (debris == null) continue;
                var q = debris.transform.position;
                if (q.y - floor > 0.25f) continue;                             // sur le comptoir, pas par terre
                float dx = q.x - p.x, dz = q.z - p.z;
                if (dx * dx + dz * dz <= k_Radius * k_Radius) count++;
            }

            float pitch = Random.Range(0.93f, 1.07f);
            if (count > 0 && SfxBank.Play(SfxBank.PasDebris, foot, Mathf.Lerp(0.35f, 0.85f, count / 6f), pitch)) return;
            SfxBank.Play(SfxBank.PasParquet, foot, Random.Range(0.32f, 0.42f), pitch);
        }

        /// <summary>Sol sous la tête (le joueur et ce qu'il tient ne comptent pas) ; faux s'il n'est pas debout dessus.</summary>
        bool Ground(Vector3 head, out float y)
        {
            y = head.y - 1.6f;
            var hits = Physics.RaycastAll(head, Vector3.down, k_MaxHeight + 0.2f, m_Mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool found = false;
            foreach (var h in hits)
            {
                if (h.normal.y < 0.7f || h.rigidbody != null && !h.rigidbody.isKinematic) continue;
                if (h.collider.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() != null) continue;
                if (h.distance < best) { best = h.distance; y = h.point.y; found = true; }
            }
            return found && best >= k_MinHeight && best <= k_MaxHeight;
        }
    }
}
