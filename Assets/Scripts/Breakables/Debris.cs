using System.Collections.Generic;
using UnityEngine;

namespace Fragile.Breakables
{
    /// <summary>
    /// Morceau d'objet cassé.
    /// - Tant qu'il bouge : physique normale.
    /// - Une fois posé : il perd son Rigidbody et son collider, et son composant s'arrête : décor purement visuel, gratuit.
    /// - Les gros morceaux restent au sol ; les miettes disparaissent une fois posées.
    /// - Plafond global : au-delà de <see cref="MaxPersistent"/> morceaux au sol, les plus anciens s'effacent en douceur.
    /// </summary>
    public class Debris : MonoBehaviour
    {
        /// <summary>Nombre maximum de morceaux laissés au sol dans la scène (réglé pour Quest).</summary>
        public static int MaxPersistent = 140;

        static readonly LinkedList<Debris> s_Persistent = new LinkedList<Debris>();

        const float k_SettleSpeed = 0.06f;
        const float k_SettleTime = 0.35f;
        const float k_MaxMoveTime = 8f;
        const float k_FadeTime = 0.6f;

        Rigidbody m_Body;
        bool m_KeepOnGround;
        float m_Spawn, m_StillFor, m_FadeStart = -1f;
        Vector3 m_Scale;
        LinkedListNode<Debris> m_Node;

        public void Init(bool keepOnGround)
        {
            m_KeepOnGround = keepOnGround;
            m_Body = GetComponent<Rigidbody>();
            m_Spawn = Time.time;
            m_Scale = transform.localScale;
            // Les débris ne projettent pas d'ombre : invisible à l'œil, coûteux sur Quest.
            if (TryGetComponent<Renderer>(out var r)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void FixedUpdate()
        {
            if (m_Body == null || m_FadeStart >= 0f) return;
            bool still = m_Body.IsSleeping() ||
                         (m_Body.linearVelocity.sqrMagnitude < k_SettleSpeed * k_SettleSpeed && m_Body.angularVelocity.sqrMagnitude < 0.25f);
            m_StillFor = still ? m_StillFor + Time.fixedDeltaTime : 0f;
            if (m_StillFor >= k_SettleTime || Time.time - m_Spawn > k_MaxMoveTime) Settle();
        }

        void Settle()
        {
            // Plus de physique ni de collision : le morceau devient un simple décor visuel,
            // que rien ne heurte (mains, bouteilles, batte) et qui ne coûte plus aucun calcul.
            Destroy(m_Body);
            m_Body = null;
            if (TryGetComponent<Collider>(out var c)) Destroy(c);
            if (!m_KeepOnGround) { FadeOut(); return; }
            enabled = false; // plus d'Update/FixedUpdate tant qu'il ne doit pas s'effacer

            m_Node = s_Persistent.AddLast(this);
            while (s_Persistent.Count > MaxPersistent)
            {
                var oldest = s_Persistent.First.Value;
                s_Persistent.RemoveFirst();
                oldest.m_Node = null;
                oldest.FadeOut();
            }
        }

        public void FadeOut()
        {
            if (m_FadeStart >= 0f) return;
            m_FadeStart = Time.time;
            if (TryGetComponent<Collider>(out var c)) c.enabled = false;
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
            if (m_Node != null) { s_Persistent.Remove(m_Node); m_Node = null; }
            // Le conteneur des éclats disparaît avec son dernier morceau.
            var parent = transform.parent;
            if (parent != null && parent.childCount <= 1 && parent.name.EndsWith("(éclats)") && Application.isPlaying)
                Destroy(parent.gameObject);
        }
    }
}
