using System.Collections;
using System.Collections.Generic;
using Fragile.Audio;
using Fragile.Grab;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fragile.Bar
{
    /// <summary>
    /// Bac de la machine à glaçons : trappe ouverte, on plonge la main (grip) et on ressort avec un glaçon.
    /// </summary>
    public class IceBin : XRBaseInteractable
    {
        [SerializeField] HingedDoor m_Door;
        [SerializeField] GameObject m_IcePrefab;
        [SerializeField] float m_MinDoorAngle = 40f;
        [SerializeField] int m_Max = 12;

        readonly Queue<GameObject> m_Spawned = new Queue<GameObject>();

        public void Setup(HingedDoor door, GameObject ice) { m_Door = door; m_IcePrefab = ice; }

        public override bool IsSelectableBy(IXRSelectInteractor interactor) =>
            base.IsSelectableBy(interactor) && m_IcePrefab != null && (m_Door == null || m_Door.angle >= m_MinDoorAngle);

        public override bool IsHoverableBy(IXRHoverInteractor interactor) =>
            base.IsHoverableBy(interactor) && (m_Door == null || m_Door.angle >= m_MinDoorAngle);

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            StartCoroutine(Hand(args.interactorObject));
        }

        /// <summary>Le glaçon passe dans la main à l'image suivante (on ne change pas de sélection pendant un événement).</summary>
        IEnumerator Hand(IXRSelectInteractor interactor)
        {
            yield return null;
            var t = interactor.GetAttachTransform(this);
            var ice = Instantiate(m_IcePrefab, t.position, Random.rotation);
            Track(ice);
            Sfx.PlayAt(Sfx.Clink, t.position, 0.6f, Random.Range(0.9f, 1.15f));
            if (interactionManager == null) yield break;
            if (isSelected) interactionManager.SelectExit(interactor, (IXRSelectInteractable)this);
            if (ice.TryGetComponent<XRGrabInteractable>(out var grab))
            {
                yield return null;
                interactionManager.SelectEnter(interactor, (IXRSelectInteractable)grab);
            }
        }

        void Track(GameObject go) => VendingMachine.Track(m_Spawned, go, m_Max);
    }
}
