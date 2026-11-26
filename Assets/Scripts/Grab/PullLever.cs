using Fragile.Music;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fragile.Grab
{
    /// <summary>
    /// Levier à ressort (robinet de tireuse) : on le saisit (grip) et on le tire vers soi. Pivot de l'objet à la base
    /// du levier, levier vers +Y local ; tirer vers +Z le fait pivoter autour de X. Lâché, il revient au repos.
    /// valueChanged : 0 (repos) à 1 (tiré à fond).
    /// </summary>
    public class PullLever : XRBaseInteractable
    {
        [SerializeField] float m_MaxAngle = 40f;
        [Tooltip("Retour au repos (°/s) quand on lâche.")]
        [SerializeField] float m_ReturnSpeed = 220f;
        [SerializeField] UnityEvent<float> m_ValueChanged = new UnityEvent<float>();

        public UnityEvent<float> valueChanged => m_ValueChanged;
        public float value => m_Angle / m_MaxAngle;

        Quaternion m_RestLocalRot;
        float m_Angle, m_GrabOffset;

        protected override void Awake()
        {
            base.Awake();
            m_RestLocalRot = transform.localRotation;
        }

        float HandAngle(Vector3 world)
        {
            var parent = transform.parent;
            var local = parent != null ? parent.InverseTransformPoint(world) : world;
            var d = Quaternion.Inverse(m_RestLocalRot) * (local - transform.localPosition);
            return Mathf.Atan2(d.z, d.y) * Mathf.Rad2Deg;
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            m_GrabOffset = m_Angle - HandAngle(args.interactorObject.GetAttachTransform(this).position);
            FingerTips.Pulse(args.interactorObject, 0.3f, 0.03f);
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic) return;
            float target;
            if (isSelected)
            {
                float a = HandAngle(interactorsSelecting[0].GetAttachTransform(this).position) + m_GrabOffset;
                if (float.IsNaN(a)) return;
                target = Mathf.Clamp(a, 0f, m_MaxAngle);
                if (target >= m_MaxAngle && m_Angle < m_MaxAngle - 0.5f) FingerTips.Pulse(interactorsSelecting[0], 0.4f, 0.03f);
            }
            else target = Mathf.MoveTowards(m_Angle, 0f, m_ReturnSpeed * Time.deltaTime);
            SetAngle(target);
        }

        void SetAngle(float a)
        {
            if (Mathf.Approximately(a, m_Angle)) return;
            m_Angle = a;
            transform.localRotation = m_RestLocalRot * Quaternion.Euler(m_Angle, 0f, 0f);
            m_ValueChanged.Invoke(value);
        }
    }
}
