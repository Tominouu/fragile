using Fragile.Audio;
using Fragile.Music;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fragile.Grab
{
    /// <summary>
    /// Tiroir coulissant (tiroir-caisse) : on le saisit et il suit la main le long de +Z local (vers l'avant).
    /// Lâché, il glisse encore un peu ; il cogne en butée. Open() l'éjecte (caisse : touche Total).
    /// </summary>
    public class SlidingDrawer : XRBaseInteractable
    {
        [SerializeField] float m_Travel = 0.2f;
        [SerializeField] float m_Friction = 6f;

        Vector3 m_ClosedLocalPos;
        float m_Offset, m_Velocity, m_GrabOffset;

        public float openAmount => m_Offset / m_Travel;

        protected override void Awake()
        {
            base.Awake();
            m_ClosedLocalPos = transform.localPosition;
        }

        float HandOffset(Vector3 world)
        {
            var parent = transform.parent;
            var local = parent != null ? parent.InverseTransformPoint(world) : world;
            return Vector3.Dot(local - m_ClosedLocalPos, transform.localRotation * Vector3.forward);
        }

        /// <summary>Éjecte le tiroir (vitesse en m/s).</summary>
        public void Open(float speed = 0.9f)
        {
            if (isSelected) return;
            m_AutoOpen = speed;
            Sfx.PlayAt(Sfx.Drawer, transform.position, 0.7f);
        }

        float m_AutoOpen;

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            m_GrabOffset = m_Offset - HandOffset(args.interactorObject.GetAttachTransform(this).position);
            FingerTips.Pulse(args.interactorObject, 0.3f, 0.03f);
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic) return;
            float dt = Time.deltaTime, before = m_Offset, next;
            if (m_AutoOpen > 0f && !isSelected)
            {
                // Éjection : sort d'un coup jusqu'en butée, en ralentissant à la fin.
                float left = m_Travel - before;
                next = before + Mathf.Max(0.05f, Mathf.Min(m_AutoOpen, left * 8f)) * dt;
                m_Velocity = 0f;
                if (next >= m_Travel) { m_AutoOpen = 0f; next = m_Travel; Sfx.PlayAt(Sfx.Clunk, transform.position, 0.3f, 1.6f); }
                m_Offset = next;
                transform.localPosition = m_ClosedLocalPos + transform.localRotation * Vector3.forward * m_Offset;
                return;
            }
            if (isSelected)
            {
                m_AutoOpen = 0f;
                next = Mathf.Clamp(HandOffset(interactorsSelecting[0].GetAttachTransform(this).position) + m_GrabOffset, 0f, m_Travel);
                m_Velocity = dt > 0f ? Mathf.Lerp(m_Velocity, (next - before) / dt, 0.4f) : 0f;
            }
            else
            {
                if (Mathf.Abs(m_Velocity) < 0.002f) { m_Velocity = 0f; return; }
                m_Velocity *= Mathf.Exp(-m_Friction * dt);
                next = before + m_Velocity * dt;
            }
            if (next <= 0f || next >= m_Travel)
            {
                if (Mathf.Abs(m_Velocity) > 0.15f) Sfx.PlayAt(Sfx.Clunk, transform.position, Mathf.Clamp01(Mathf.Abs(m_Velocity)) * 0.4f, 1.6f);
                next = Mathf.Clamp(next, 0f, m_Travel);
                m_Velocity = 0f;
            }
            m_Offset = next;
            transform.localPosition = m_ClosedLocalPos + transform.localRotation * Vector3.forward * m_Offset;
        }
    }
}
