using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace Fragile.Weapons
{
    /// <summary>
    /// Visée à deux mains : après le placement normal de l'arme dans la main qui tient la poignée, on la fait pivoter
    /// autour de cette poignée (origine de l'arme) pour que le garde-main / la pompe vienne vers l'autre manette.
    /// À placer après le XRGeneralGrabTransformer dans la liste des transformateurs de l'arme.
    /// </summary>
    public class TwoHandAim : XRBaseGrabTransformer
    {
        Firearm m_Gun;

        void Awake() => m_Gun = GetComponent<Firearm>();

        public override void Process(XRGrabInteractable grabInteractable, XRInteractionUpdateOrder.UpdatePhase updatePhase, ref Pose targetPose, ref Vector3 localScale)
        {
            if (m_Gun == null || !m_Gun.TryGetAimHandle(out var handle, out var anchorLocal)) return;
            var current = targetPose.rotation * anchorLocal;
            var wanted = handle.controllerPosition - targetPose.position;
            if (wanted.sqrMagnitude < 0.08f * 0.08f || current.sqrMagnitude < 1e-6f) return;   // mains l'une contre l'autre
            targetPose.rotation = Quaternion.FromToRotation(current, wanted) * targetPose.rotation;
        }
    }
}
