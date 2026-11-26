using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fragile.Weapons
{
    /// <summary>Munition qu'on tient à la main : cartouche de calibre 12, cartouche de carabine, chargeur de pistolet.</summary>
    public class AmmoItem : MonoBehaviour
    {
        public enum Kind { Shell, RifleRound, PistolMagazine }

        [SerializeField] Kind m_Kind;
        [Tooltip("Chargeur : nombre de cartouches dedans.")]
        [SerializeField] int m_Rounds = 1;

        public Kind kind => m_Kind;
        public int rounds { get => m_Rounds; set => m_Rounds = value; }
        public bool consumed { get; private set; }

        void Awake()
        {
            // Bruit quand elle tombe (éjectée, lâchée, chargeur qui glisse de l'arme).
            if (!TryGetComponent<Rigidbody>(out _)) return;
            if (!TryGetComponent<CasingSound>(out var snd)) snd = gameObject.AddComponent<CasingSound>();
            snd.matter = m_Kind switch { Kind.Shell => CasingSound.Matter.Plastic, Kind.PistolMagazine => CasingSound.Matter.Magazine, _ => CasingSound.Matter.RifleBrass };
        }

        /// <summary>Tenue en main : seule une munition qu'on présente soi-même entre dans l'arme
        /// (pas celle qu'on vient d'éjecter, ni un chargeur qui tombe).</summary>
        public bool held => TryGetComponent<XRBaseInteractable>(out var grab) && grab.isSelected;

        /// <summary>Introduite dans l'arme : la main la lâche et elle disparaît.</summary>
        public void Consume()
        {
            if (consumed) return;
            consumed = true;
            if (TryGetComponent<XRBaseInteractable>(out var grab) && grab.isSelected && grab.interactionManager != null)
                grab.interactionManager.CancelInteractableSelection((UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)grab);
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }
}
