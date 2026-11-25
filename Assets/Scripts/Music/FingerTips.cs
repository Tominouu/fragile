using System.Collections.Generic;
using Fragile.Hands;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fragile.Music
{
    /// <summary>Bouts d'index des mains affichées (manettes) et des mains suivies (hand tracking).</summary>
    static class FingerTips
    {
        public struct Tip { public Transform point; public HapticImpulsePlayer haptics; public ControllerHandPose hand; }

        static readonly List<Tip> s_Tips = new List<Tip>();
        static float s_NextSearch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { s_Tips.Clear(); s_NextSearch = 0f; }

        public static List<Tip> All
        {
            get
            {
                // Recherche rare : les mains existent dès le départ, mais le hand tracking peut arriver plus tard.
                if (Time.unscaledTime >= s_NextSearch)
                {
                    s_NextSearch = Time.unscaledTime + 3f;
                    s_Tips.Clear();
                    foreach (var hand in Object.FindObjectsByType<ControllerHandPose>(FindObjectsInactive.Exclude))
                        if (hand.indexTip != null)
                            s_Tips.Add(new Tip { point = hand.indexTip, hand = hand, haptics = hand.GetComponentInParent<HapticImpulsePlayer>() });
                    foreach (var poke in Object.FindObjectsByType<XRPokeInteractor>(FindObjectsInactive.Exclude))
                        if (poke.transform.parent != null && poke.transform.parent.GetComponentInChildren<ControllerHandPose>(true) == null)
                            s_Tips.Add(new Tip { point = poke.attachTransform });
                }
                return s_Tips;
            }
        }

        public static void Pulse(IXRInteractor interactor, float amplitude, float duration)
        {
            var player = interactor != null ? interactor.transform.GetComponentInParent<HapticImpulsePlayer>() : null;
            if (player != null) player.SendHapticImpulse(amplitude, duration);
        }
    }
}
