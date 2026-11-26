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
            // Maillage porté par l'objet lui-même (bouteilles) : pas de visuels séparés. XRI garde alors
            // l'interpolation du Rigidbody, et la main suit l'objet sans à-coup.
            if (TryGetComponent<Renderer>(out _))
            {
                predictedVisualsTransform = null;
                return;
            }

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

        [Tooltip("Déjà tenu : l'autre main ne peut pas le prendre (elle saisit alors la poignée secondaire, ex. la pompe d'une arme).")]
        [SerializeField] bool m_NoHandSwap;

        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            if (m_NoHandSwap && isSelected && !interactorsSelecting.Contains(interactor)) return false;
            return base.IsSelectableBy(interactor);
        }

        public override bool IsHoverableBy(IXRHoverInteractor interactor)
        {
            if (m_NoHandSwap && isSelected && !(interactor is IXRSelectInteractor s && interactorsSelecting.Contains(s))) return false;
            return base.IsHoverableBy(interactor);
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

        // ------------------------------------------------------------------ lancer
        // XRI estime la vitesse de lancer à partir de l'objet (qui suit la main avec un léger retard, et dont la
        // vitesse est lissée) : le lancer paraît mou. Ici on suit directement la main, image par image, et au lâcher
        // on donne à l'objet la vitesse réelle du geste (dont l'effet de fouet : un objet tenu par un bout part plus vite).

        [Header("Lancer")]
        [Tooltip("Durée du geste prise en compte (s). Court = réactif au coup de poignet final.")]
        [SerializeField] float m_ThrowWindow = 0.1f;
        [Tooltip("Petit coup de pouce (les lancers en VR paraissent toujours un peu courts sans retour d'effort).")]
        [SerializeField] float m_ThrowBoost = 1.15f;
        [SerializeField] float m_MaxThrowSpeed = 18f;

        struct Sample { public float time; public Vector3 pos; public Quaternion rot; }
        readonly Dictionary<IXRSelectInteractor, List<Sample>> m_Samples = new Dictionary<IXRSelectInteractor, List<Sample>>();
        bool m_HasRelease;
        Vector3 m_ReleaseVelocity, m_ReleaseAngular;

        void LateUpdate()
        {
            if (!isSelected) return;
            float now = Time.unscaledTime;
            foreach (var interactor in interactorsSelecting)
            {
                if (!m_Samples.TryGetValue(interactor, out var list)) m_Samples[interactor] = list = new List<Sample>(32);
                // Point de la main qui tient l'objet (la manette suivie).
                var t = interactor.transform;
                list.Add(new Sample { time = now, pos = t.position, rot = t.rotation });
                while (list.Count > 0 && now - list[0].time > 0.25f) list.RemoveAt(0);
            }
        }

        /// <summary>Vitesse du geste : pente des moindres carrés sur la fenêtre, en gardant le geste le plus vif (fin de lancer).</summary>
        bool EstimateThrow(List<Sample> s, out Vector3 velocity, out Vector3 angular, out Vector3 handPos)
        {
            velocity = angular = handPos = Vector3.zero;
            if (s == null || s.Count < 3) return false;
            float now = s[s.Count - 1].time;
            handPos = s[s.Count - 1].pos;

            Vector3 Fit(float window)
            {
                int start = s.Count - 1;
                while (start > 0 && now - s[start - 1].time <= window) start--;
                int n = s.Count - start;
                if (n < 2) return Vector3.zero;
                float tm = 0; Vector3 pm = Vector3.zero;
                for (int i = start; i < s.Count; i++) { tm += s[i].time; pm += s[i].pos; }
                tm /= n; pm /= n;
                float den = 0; Vector3 num = Vector3.zero;
                for (int i = start; i < s.Count; i++) { float dt = s[i].time - tm; den += dt * dt; num += dt * (s[i].pos - pm); }
                return den > 1e-8f ? num / den : Vector3.zero;
            }
            var vLong = Fit(m_ThrowWindow);
            var vShort = Fit(m_ThrowWindow * 0.5f);
            velocity = vShort.sqrMagnitude > vLong.sqrMagnitude ? vShort : vLong;

            // Rotation du poignet sur la fin du geste.
            int a = s.Count - 1;
            while (a > 0 && now - s[a - 1].time <= m_ThrowWindow * 0.6f) a--;
            float span = now - s[a].time;
            if (span > 1e-4f)
            {
                (s[s.Count - 1].rot * Quaternion.Inverse(s[a].rot)).ToAngleAxis(out float deg, out Vector3 axis);
                if (deg > 180f) deg -= 360f;
                if (!float.IsNaN(axis.x) && axis.sqrMagnitude > 0.5f) angular = axis * (deg * Mathf.Deg2Rad / span);
            }
            return true;
        }

        protected override void OnSelectExiting(SelectExitEventArgs args)
        {
            // Dernière main qui lâche : on fige la vitesse du geste avant que XRI ne restaure le Rigidbody.
            if (interactorsSelecting.Count == 1 && m_Samples.TryGetValue(args.interactorObject, out var list) &&
                EstimateThrow(list, out var v, out var w, out var handPos) && TryGetComponent<Rigidbody>(out var rb))
            {
                // Effet de fouet : le centre de l'objet est entraîné par la rotation du poignet.
                var lever = rb.worldCenterOfMass - handPos;
                m_ReleaseVelocity = Vector3.ClampMagnitude((v + Vector3.Cross(w, lever)) * m_ThrowBoost, m_MaxThrowSpeed);
                m_ReleaseAngular = w;
                m_HasRelease = true;
            }
            m_Samples.Remove(args.interactorObject);
            base.OnSelectExiting(args);
        }

        protected override void Detach()
        {
            if (!m_HasRelease || !throwOnDetach || !TryGetComponent<Rigidbody>(out var rb) || rb.isKinematic)
            {
                base.Detach();
                return;
            }
            m_HasRelease = false;
            rb.linearVelocity = m_ReleaseVelocity;
            rb.angularVelocity = m_ReleaseAngular;
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
