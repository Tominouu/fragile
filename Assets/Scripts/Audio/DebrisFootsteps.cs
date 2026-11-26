using Fragile.Breakables;
using UnityEngine;

namespace Fragile.Audio
{
    /// <summary>
    /// Pas sur les débris : quand le joueur se déplace (marche réelle ou déplacement au joystick) et que des morceaux
    /// d'objets cassés jonchent le sol sous lui, un pas qui écrase des débris est joué à chaque foulée.
    /// Se lance tout seul au chargement de la scène, rien à placer.
    /// </summary>
    public class DebrisFootsteps : MonoBehaviour
    {
        const float k_Stride = 0.6f;        // distance entre deux pas (m)
        const float k_MinSpeed = 0.35f;     // en dessous : le joueur se penche ou regarde autour, il ne marche pas
        const float k_Radius = 0.45f;       // débris pris en compte autour des pieds
        const float k_MinBelowHead = 0.6f;  // les débris doivent être au sol, pas sur le comptoir

        Transform m_Head;
        Vector2 m_Last;
        float m_Travel;
        bool m_HasLast;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<DebrisFootsteps>() != null) return;
            var go = new GameObject("[Fragile] Pas sur débris");
            DontDestroyOnLoad(go);
            go.AddComponent<DebrisFootsteps>();
            SfxBank.Preload(SfxBank.PasDebris);
        }

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

            int count = 0; float floor = 0f; float best = float.MaxValue;
            foreach (var debris in Debris.All)
            {
                if (debris == null) continue;
                var q = debris.transform.position;
                if (p.y - q.y < k_MinBelowHead) continue;
                float dx = q.x - p.x, dz = q.z - p.z, r2 = dx * dx + dz * dz;
                if (r2 > k_Radius * k_Radius) continue;
                count++;
                if (r2 < best) { best = r2; floor = q.y; }
            }
            if (count == 0) return;
            SfxBank.Play(SfxBank.PasDebris, new Vector3(p.x, floor, p.z), Mathf.Lerp(0.35f, 0.85f, count / 6f), Random.Range(0.92f, 1.08f));
        }
    }
}
