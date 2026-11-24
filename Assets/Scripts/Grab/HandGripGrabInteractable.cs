using System.Collections.Generic;
using Fragile.Hands;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fragile.Grab
{
    /// <summary>
    /// Grab Interactable qui place l'objet correctement dans la main : l'axe de la <see cref="GripSurface"/>
    /// passe dans le poing, contre la paume, et les doigts se referment sur la surface.
    /// Fonctionne à une ou deux mains (avec "Select Mode = Multiple").
    /// </summary>
    [RequireComponent(typeof(GripSurface))]
    public class HandGripGrabInteractable : XRGrabInteractable
    {
        GripSurface m_Surface;
        readonly Dictionary<IXRSelectInteractor, ControllerHandPose> m_Hands = new Dictionary<IXRSelectInteractor, ControllerHandPose>();

        protected override void Awake()
        {
            EnsureNeutralVisualsRoot();
            base.Awake();
            m_Surface = GetComponent<GripSurface>();
            // Les mains s'accrochent aux visuels : XRI les déplace de façon fluide à chaque image
            // (alors que le Rigidbody n'avance qu'à chaque pas de physique) -> pas de tremblement.
            m_Surface.handParent = predictedVisualsTransform != null ? predictedVisualsTransform : transform;
        }

        /// <summary>
        /// XRI déplace un enfant "visuels" entre deux pas de physique. Si c'est le modèle lui-même (mis à l'échelle),
        /// on l'emballe dans un conteneur neutre (position/rotation nulles, échelle 1) pour pouvoir y accrocher les mains.
        /// </summary>
        void EnsureNeutralVisualsRoot()
        {
            var visuals = predictedVisualsTransform;
            if (visuals != null && visuals.parent == transform &&
                visuals.localPosition == Vector3.zero && visuals.localRotation == Quaternion.identity && visuals.localScale == Vector3.one)
                return;

            var root = new GameObject("Visuals").transform;
            root.SetParent(transform, false);
            if (visuals != null)
            {
                visuals.SetParent(root, true);
            }
            else
            {
                // Pas de visuels désignés : on regroupe les enfants sans collider.
                var children = new List<Transform>();
                foreach (Transform child in transform)
                    if (child != root && child.GetComponentInChildren<Collider>() == null)
                        children.Add(child);
                foreach (var child in children) child.SetParent(root, true);
            }
            predictedVisualsTransform = root;
        }

        protected override void InitializeDynamicAttachPose(IXRSelectInteractor interactor, Transform dynamicAttachTransform)
        {
            var hand = FindHand(interactor);
            if (hand == null || !hand.TryBeginGrab(m_Surface, out Pose handInObject, out Pose handHomeWorld))
            {
                base.InitializeDynamicAttachPose(interactor, dynamicAttachTransform);
                return;
            }
            m_Hands[interactor] = hand;

            // Pose de l'objet pour que la main (à sa place réelle sur la manette) le tienne comme prévu :
            // objet * handInObject = handHomeWorld  =>  objet = handHomeWorld * inverse(handInObject)
            var objRot = handHomeWorld.rotation * Quaternion.Inverse(handInObject.rotation);
            var objPos = handHomeWorld.position - objRot * handInObject.position;

            // Point d'attache de l'objet correspondant au point d'attache de l'interactor dans cette pose.
            var attach = interactor.GetAttachTransform(this);
            var invObjRot = Quaternion.Inverse(objRot);
            dynamicAttachTransform.localPosition = invObjRot * (attach.position - objPos);
            dynamicAttachTransform.localRotation = invObjRot * attach.rotation;
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            if (m_Hands.TryGetValue(args.interactorObject, out var hand))
            {
                m_Hands.Remove(args.interactorObject);
                if (hand != null) hand.EndGrab();
            }
            base.OnSelectExited(args);
        }

        protected override void OnDisable()
        {
            foreach (var hand in m_Hands.Values)
                if (hand != null) hand.EndGrab();
            m_Hands.Clear();
            base.OnDisable();
        }

        static ControllerHandPose FindHand(IXRSelectInteractor interactor)
        {
            // L'interactor est enfant de "Left/Right Controller", tout comme la main affichée.
            var parent = interactor.transform.parent;
            return parent != null ? parent.GetComponentInChildren<ControllerHandPose>() : null;
        }
    }
}
