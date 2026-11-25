#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace Fragile.XR
{
    /// <summary>
    /// Dans l'éditeur uniquement : lance le simulateur XR (clavier/souris) seulement si aucun casque ne démarre.
    /// Casque branché (Quest Link / Air Link) -> suivi réel de la tête, pas de simulateur.
    /// La session OpenXR met 1 à 2 s à démarrer après le Play : on attend avant de décider.
    /// </summary>
    class SimulatorIfNoHeadset : MonoBehaviour
    {
        const string SimulatorPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.6.1/XR Interaction Simulator/XR Interaction Simulator.prefab";
        const float MaxWaitSeconds = 4f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var go = new GameObject("[Fragile] Simulator If No Headset") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<SimulatorIfNoHeadset>();
        }

        IEnumerator Start()
        {
            // Aucun runtime XR chargé (pas de Link, pas de casque) : inutile d'attendre.
            bool loaderActive = XRGeneralSettings.Instance != null
                                && XRGeneralSettings.Instance.Manager != null
                                && XRGeneralSettings.Instance.Manager.activeLoader != null;

            float end = Time.realtimeSinceStartup + (loaderActive ? MaxWaitSeconds : 0f);
            while (Time.realtimeSinceStartup < end)
            {
                if (HeadsetRunning())
                {
                    Debug.Log("[Fragile] Casque détecté : simulateur XR désactivé.");
                    Destroy(gameObject);
                    yield break;
                }
                yield return null;
            }

            if (HeadsetRunning())
            {
                Debug.Log("[Fragile] Casque détecté : simulateur XR désactivé.");
            }
            else
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPrefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning("[Fragile] Pas de casque et simulateur introuvable : " + SimulatorPrefabPath);
                }
                else
                {
                    var sim = Instantiate(prefab);
                    sim.name = prefab.name;
                    DontDestroyOnLoad(sim);
                    Debug.Log("[Fragile] Aucun casque : simulateur XR lancé (clavier/souris).");
                }
            }
            Destroy(gameObject);
        }

        static bool HeadsetRunning()
        {
            if (XRSettings.isDeviceActive) return true;
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays)
                if (d.running)
                    return true;
            return false;
        }
    }
}
#endif
