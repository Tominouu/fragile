using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fragile.Weapons
{
    /// <summary>
    /// Boîte de munitions inépuisable : la saisir met une munition dans la main (cartouche, chargeur).
    /// La boîte elle-même ne bouge pas avec la main.
    /// </summary>
    public class AmmoBox : XRBaseInteractable
    {
        [SerializeField] AmmoItem m_Prefab;
        [Tooltip("Durée de vie d'une munition posée quelque part (s).")]
        [SerializeField] float m_Lifetime = 90f;

        public AmmoItem.Kind? ammoKind => m_Prefab != null ? m_Prefab.kind : (AmmoItem.Kind?)null;

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            if (m_Prefab != null) StartCoroutine(HandOver(args.interactorObject));
        }

        IEnumerator HandOver(IXRSelectInteractor interactor)
        {
            // La main lâche la boîte et prend une munition neuve, posée à l'endroit de la prise.
            yield return null;
            if (interactionManager == null || interactor == null) yield break;
            if (interactorsSelecting.Contains(interactor)) interactionManager.SelectExit(interactor, (IXRSelectInteractable)this);
            var attach = interactor.GetAttachTransform(this);
            var item = Instantiate(m_Prefab, attach.position, attach.rotation);
            if (item.TryGetComponent<Fragile.Bar.TimedDespawn>(out var life)) life.enabled = true;
            yield return null;   // l'interactable s'enregistre auprès du gestionnaire
            if (item != null && item.TryGetComponent<XRBaseInteractable>(out var grab))
                interactionManager.SelectEnter(interactor, (IXRSelectInteractable)grab);
        }
    }
}
