using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fragile.Breakables
{
    /// <summary>
    /// Objet capable de casser les <see cref="Breakable"/> (ex. la batte).
    /// Plus le coup est rapide, plus la casse est violente ; vibre dans les manettes qui le tiennent.
    /// </summary>
    [DisallowMultipleComponent]
    public class Breaker : MonoBehaviour
    {
        [Tooltip("Multiplie la vitesse d'impact (une batte frappe plus fort qu'une main).")]
        [SerializeField] float m_Power = 1.2f;

        public float power => m_Power;

        XRBaseInteractable m_Interactable;

        void Awake() => m_Interactable = GetComponent<XRBaseInteractable>();

        /// <summary>Vibration dans chaque manette qui tient l'objet.</summary>
        public void Haptics(float strength01)
        {
            if (m_Interactable == null || !m_Interactable.isSelected) return;
            foreach (var interactor in m_Interactable.interactorsSelecting)
            {
                var player = interactor.transform.GetComponentInParent<HapticImpulsePlayer>();
                if (player != null)
                    player.SendHapticImpulse(Mathf.Lerp(0.5f, 1f, strength01), Mathf.Lerp(0.08f, 0.22f, strength01));
            }
        }
    }
}
