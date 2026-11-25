using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fragile.Music
{
    /// <summary>
    /// Molette : on la saisit (grip) et on tourne le poignet. Z local = axe, vers l'extérieur.
    /// Le modèle n'est pas un enfant : il est tourné autour de cet axe (le préfab du modèle reste intact).
    /// </summary>
    public class PanelKnob : XRBaseInteractable
    {
        [SerializeField] Transform m_Model;
        [SerializeField, Range(0f, 1f)] float m_Value = 0.6f;
        [Tooltip("Course totale de la molette (degrés).")]
        [SerializeField] float m_Range = 270f;
        [Tooltip("Degrés de molette par degré de poignet (le poignet tourne moins que la molette).")]
        [SerializeField] float m_Sensitivity = 1.8f;
        [SerializeField] UnityEvent<float> m_ValueChanged = new UnityEvent<float>();

        public UnityEvent<float> valueChanged => m_ValueChanged;
        public float value => m_Value;

        Vector3 m_RestPos;      // pose d'origine du modèle, dans le repère de la molette
        Quaternion m_RestRot;
        Quaternion m_LastHand;
        int m_LastTick;

        public void Setup(Transform model, float value) { m_Model = model; m_Value = value; }

        protected override void Awake()
        {
            base.Awake();
            if (m_Model != null)
            {
                m_RestPos = transform.InverseTransformPoint(m_Model.position);
                m_RestRot = Quaternion.Inverse(transform.rotation) * m_Model.rotation;
            }
            m_LastTick = Tick;
        }

        void Start()
        {
            ApplyModel();
            m_ValueChanged.Invoke(m_Value);
        }

        int Tick => Mathf.RoundToInt(m_Value * 20f);

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            m_LastHand = args.interactorObject.transform.rotation;
            FingerTips.Pulse(args.interactorObject, 0.3f, 0.03f);
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic || !isSelected) return;

            var interactor = interactorsSelecting[0];
            var hand = interactor.transform.rotation;
            var delta = hand * Quaternion.Inverse(m_LastHand);
            m_LastHand = hand;

            // Part de la rotation du poignet autour de l'axe de la molette (horaire vu de face = plus fort).
            float twist = 2f * Mathf.Rad2Deg * Mathf.Atan2(Vector3.Dot(new Vector3(delta.x, delta.y, delta.z), transform.forward), delta.w);
            twist = Mathf.DeltaAngle(0f, twist);
            if (Mathf.Abs(twist) < 0.01f) return;
            SetValue(m_Value + twist * m_Sensitivity / m_Range, interactor);
        }

        public void SetValue(float v, IXRInteractor interactor = null)
        {
            v = Mathf.Clamp01(v);
            if (Mathf.Approximately(v, m_Value)) return;
            m_Value = v;
            ApplyModel();
            m_ValueChanged.Invoke(m_Value);
            // Crans : petite vibration tous les 5 %, plus marquée en butée.
            if (Tick == m_LastTick) return;
            m_LastTick = Tick;
            FingerTips.Pulse(interactor, v <= 0f || v >= 1f ? 0.7f : 0.25f, 0.02f);
        }

        void ApplyModel()
        {
            if (m_Model == null) return;
            var turn = Quaternion.AngleAxis((m_Value - 0.5f) * m_Range, Vector3.forward);
            m_Model.SetPositionAndRotation(transform.TransformPoint(turn * m_RestPos), transform.rotation * turn * m_RestRot);
        }
    }
}
