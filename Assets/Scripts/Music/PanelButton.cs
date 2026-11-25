using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fragile.Music
{
    /// <summary>
    /// Bouton physique : on l'enfonce avec le bout de l'index (ou on le « saisit » avec le grip).
    /// Z local = sortie du bouton ; la touche affichée s'enfonce sous le doigt.
    /// </summary>
    public class PanelButton : XRBaseInteractable
    {
        [SerializeField] Transform m_Cap;
        [SerializeField] Vector2 m_HalfSize = new Vector2(0.026f, 0.022f);
        [SerializeField] float m_CapHeight = 0.006f;
        [SerializeField] float m_Travel = 0.005f;
        [SerializeField] UnityEvent m_Pressed = new UnityEvent();

        public UnityEvent pressed => m_Pressed;

        Vector3 m_CapRest;
        float m_Depth, m_LastPress = -10f;
        bool m_Down;

        public void Setup(Transform cap, Vector2 halfSize, float capHeight)
        {
            m_Cap = cap; m_HalfSize = halfSize; m_CapHeight = capHeight;
        }

        protected override void Awake()
        {
            base.Awake();
            if (m_Cap != null) m_CapRest = m_Cap.localPosition;
        }

        void Update()
        {
            float target = 0f;
            FingerTips.Tip presser = default;
            foreach (var tip in FingerTips.All)
            {
                if (tip.point == null || !tip.point.gameObject.activeInHierarchy || (tip.hand != null && tip.hand.isHolding)) continue;
                var p = transform.InverseTransformPoint(tip.point.position);
                // Marge sous la touche : un doigt rapide peut la traverser entre deux images.
                if (Mathf.Abs(p.x) > m_HalfSize.x || Mathf.Abs(p.y) > m_HalfSize.y || p.z < -0.05f) continue;
                // Le bout de l'os est environ 6 mm sous la peau du doigt.
                float depth = m_CapHeight + 0.006f - p.z;
                if (depth > target) { target = depth; presser = tip; }
            }
            target = Mathf.Clamp(target, 0f, m_Travel);
            if (isSelected || Time.time - m_LastPress < 0.12f) target = m_Travel;

            m_Depth = target > m_Depth ? target : Mathf.MoveTowards(m_Depth, target, 0.04f * Time.deltaTime);
            if (m_Cap != null) m_Cap.position = m_Cap.parent.TransformPoint(m_CapRest) - transform.forward * m_Depth;

            if (!m_Down && m_Depth >= m_Travel * 0.8f)
            {
                m_Down = true;
                if (presser.haptics != null) presser.haptics.SendHapticImpulse(0.6f, 0.04f);
                Press();
            }
            else if (m_Down && m_Depth < m_Travel * 0.3f) m_Down = false;
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            FingerTips.Pulse(args.interactorObject, 0.6f, 0.04f);
            m_Down = true;
            Press();
        }

        void Press()
        {
            if (Time.time - m_LastPress < 0.4f) return; // pas de double appui
            m_LastPress = Time.time;
            m_Pressed.Invoke();
        }
    }
}
