using Fragile.Grab;
using Fragile.Hands;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fragile.Weapons
{
    /// <summary>
    /// Deuxième prise d'une arme, pour l'autre main : garde-main (visée à deux mains), pompe (visée + armement),
    /// culasse du pistolet, levier de verrou de la carabine. Saisissable seulement quand l'arme est déjà tenue
    /// par l'autre main. La main se referme sur la <see cref="GripSurface"/> de la poignée et suit la pièce
    /// (elle recule avec la pompe) ; la vraie manette, elle, pilote la pièce (voir <see cref="Firearm"/>).
    /// </summary>
    [RequireComponent(typeof(GripSurface))]
    public class GunHandle : XRBaseInteractable
    {
        public enum Role { Support, Pump, Slide, Bolt }

        [SerializeField] Role m_Role = Role.Support;
        [SerializeField] Firearm m_Gun;

        GripSurface m_Surface;
        ControllerHandPose m_Hand;

        public Role role => m_Role;
        /// <summary>La poignée oriente-t-elle l'arme (visée à deux mains) ?</summary>
        public bool aims => m_Role == Role.Support || m_Role == Role.Pump;
        public bool held => isSelected;
        /// <summary>Pièce à allumer au survol (pompe, culasse, verrou) ; aucune pour le garde-main (fondu dans le modèle).</summary>
        public Transform highlightRoot => m_Role == Role.Support ? null : transform.parent;
        /// <summary>Position réelle de la manette qui tient la poignée.</summary>
        public Vector3 controllerPosition => isSelected ? interactorsSelecting[0].transform.position : transform.position;
        public Quaternion controllerRotation => isSelected ? interactorsSelecting[0].transform.rotation : transform.rotation;
        public IXRSelectInteractor interactor => isSelected ? interactorsSelecting[0] : null;

        protected override void Awake()
        {
            base.Awake();
            m_Surface = GetComponent<GripSurface>();
            if (m_Gun == null) m_Gun = GetComponentInParent<Firearm>();
        }

        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            var grab = m_Gun != null ? m_Gun.grab : null;
            return base.IsSelectableBy(interactor) && grab != null && grab.isSelected && !grab.interactorsSelecting.Contains(interactor)
                   && (!isSelected || interactorsSelecting.Contains(interactor));
        }

        public override bool IsHoverableBy(IXRHoverInteractor interactor)
        {
            var grab = m_Gun != null ? m_Gun.grab : null;
            return base.IsHoverableBy(interactor) && grab != null && grab.isSelected
                   && !(interactor is IXRSelectInteractor s && grab.interactorsSelecting.Contains(s));
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            var parent = args.interactorObject.transform.parent;
            m_Hand = parent != null ? parent.GetComponentInChildren<ControllerHandPose>() : null;
            if (m_Hand != null && !m_Hand.TryBeginGrab(m_Surface, out _, out _)) m_Hand = null;
            m_Gun.OnHandleGrabbed(this);
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            if (m_Hand != null) { m_Hand.EndGrab(); m_Hand = null; }
            m_Gun.OnHandleReleased(this);
            base.OnSelectExited(args);
        }

        protected override void OnDisable()
        {
            if (m_Hand != null) { m_Hand.EndGrab(); m_Hand = null; }
            base.OnDisable();
        }

        /// <summary>Lâche la poignée (l'arme vient d'être lâchée par l'autre main).</summary>
        public void ForceRelease()
        {
            if (isSelected && interactionManager != null) interactionManager.CancelInteractableSelection((IXRSelectInteractable)this);
        }
    }
}
