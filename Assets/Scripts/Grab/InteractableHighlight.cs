using System.Collections.Generic;
using Fragile.Breakables;
using Fragile.Music;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fragile.Grab
{
    /// <summary>
    /// Surbrillance d'un objet interactif (bouteille, batte, bouton, poignée...) quand une main peut le saisir ou l'actionner :
    /// liseré lumineux (shader Fragile/Surbrillance) dessiné par-dessus ses maillages. Éteinte pendant qu'on le tient.
    /// Ajoutée automatiquement à tous les interactables de la scène au lancement (et aux objets créés ensuite qui l'appellent).
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractableHighlight : MonoBehaviour
    {
        static Material s_Material;
        static readonly List<Material> s_Mats = new List<Material>();

        XRBaseInteractable m_Interactable;
        readonly List<GameObject> m_Overlays = new List<GameObject>();
        bool m_Built, m_On;

        /// <summary>Ajoute la surbrillance à un interactable (sans doublon).</summary>
        public static void Ensure(XRBaseInteractable interactable)
        {
            if (interactable != null && !interactable.TryGetComponent<InteractableHighlight>(out _))
                interactable.gameObject.AddComponent<InteractableHighlight>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            AddToAll();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene s, LoadSceneMode m) => AddToAll();

        static void AddToAll()
        {
            foreach (var i in Object.FindObjectsByType<XRBaseInteractable>(FindObjectsInactive.Include)) Ensure(i);
        }

        void Awake() => m_Interactable = GetComponent<XRBaseInteractable>();

        void OnEnable()
        {
            if (m_Interactable == null) return;
            m_Interactable.hoverEntered.AddListener(OnChanged);
            m_Interactable.hoverExited.AddListener(OnChanged);
            m_Interactable.selectEntered.AddListener(OnChanged);
            m_Interactable.selectExited.AddListener(OnChanged);
        }

        void OnDisable()
        {
            if (m_Interactable != null)
            {
                m_Interactable.hoverEntered.RemoveListener(OnChanged);
                m_Interactable.hoverExited.RemoveListener(OnChanged);
                m_Interactable.selectEntered.RemoveListener(OnChanged);
                m_Interactable.selectExited.RemoveListener(OnChanged);
            }
            Show(false);
        }

        void OnChanged(BaseInteractionEventArgs _) => Show(m_Interactable.isHovered && !m_Interactable.isSelected && m_Interactable.enabled);

        void Show(bool on)
        {
            if (on == m_On) return;
            m_On = on;
            if (on && !m_Built) Build();
            foreach (var o in m_Overlays) if (o != null) o.SetActive(on);
        }

        /// <summary>Calque de surbrillance sur chaque maillage visible de l'objet (même maillage, tous les sous-maillages).</summary>
        void Build()
        {
            m_Built = true;
            // Matériau dans Resources : son shader est ainsi inclus dans le build.
            if (s_Material == null) s_Material = Resources.Load<Material>("Surbrillance");
            if (s_Material == null) return;
            foreach (var r in Targets())
            {
                if (!r.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null) continue;
                var go = new GameObject("Surbrillance");
                go.transform.SetParent(r.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var mr = go.AddComponent<MeshRenderer>();
                s_Mats.Clear();
                for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) s_Mats.Add(s_Material);
                mr.SetSharedMaterials(s_Mats);
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                go.SetActive(false);
                m_Overlays.Add(go);
            }
        }

        /// <summary>Maillages qui représentent l'objet : les siens (sauf vitres, éclats, autres interactables et maillages statiques fusionnés).</summary>
        IEnumerable<MeshRenderer> Targets()
        {
            Transform root = transform;
            if (m_Interactable is PanelKnob knob && knob.model != null) root = knob.model;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.isPartOfStaticBatch || r.GetComponentInParent<GlassPane>() != null) continue;
                var owner = r.GetComponentInParent<XRBaseInteractable>(true);
                if (owner != null && owner != m_Interactable && root == transform) continue;
                yield return r;
            }
        }
    }
}
