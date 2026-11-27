using Fragile.Audio;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fragile.Darts
{
    /// <summary>
    /// Fléchette libre : lancée à la main (lancer du geste réel de HandGripGrabInteractable), elle vole pointe en avant et
    /// se plante dans une <see cref="DartBoard"/> si elle arrive de face ; ailleurs elle rebondit et tombe. Plantée, elle est
    /// tenue par une attache (pas cinématique : XRI la rendrait figée au lâcher) ; on la retire à la main et on la relance.
    /// Pointe = +Z local, origine à l'arrière (empennage).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Dart : MonoBehaviour
    {
        [Tooltip("Distance de l'origine à la pointe, le long de +Z.")]
        [SerializeField] float m_Length = 0.155f;
        [Tooltip("Enfoncement de la pointe dans la cible.")]
        [SerializeField] float m_Penetration = 0.014f;

        Rigidbody m_Body;
        XRGrabInteractable m_Grab;
        Collider[] m_Colliders;
        float m_ReleaseTime = -10f;
        bool m_Flying;
        Vector3 m_LastVelocity;
        Quaternion m_LastRotation;
        FixedJoint m_Joint;
        DartBoard m_Board;
        Collider m_IgnoredBoard;

        bool isHeld => m_Grab != null && m_Grab.isSelected;

        /// <summary>Une fléchette a-t-elle causé ce choc ? (elle n'abîme pas le décor : voir Breakable)</summary>
        public static bool IsDart(Collision c) =>
            c.rigidbody != null ? c.rigidbody.GetComponent<Dart>() != null : c.collider != null && c.collider.GetComponentInParent<Dart>() != null;

        void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
            m_Grab = GetComponent<XRGrabInteractable>();
            m_Colliders = GetComponentsInChildren<Collider>();
            if (m_Grab != null)
            {
                m_Grab.selectEntered.AddListener(_ => { Unstick(); m_Flying = false; });
                m_Grab.selectExited.AddListener(_ =>
                {
                    m_ReleaseTime = Time.time;
                    // Retirée de la cible : elle peut de nouveau la toucher.
                    if (m_IgnoredBoard != null) { foreach (var c in m_Colliders) Physics.IgnoreCollision(c, m_IgnoredBoard, false); m_IgnoredBoard = null; }
                });
            }
        }

        void FixedUpdate()
        {
            // Cible cassée ou disparue : la fléchette plantée retombe.
            if (m_Joint != null && (m_Board == null || !m_Board.isActiveAndEnabled)) Unstick();
            if (m_Joint != null || m_Body.isKinematic) return;

            var v = m_Body.linearVelocity;
            // Lâchée assez vite : c'est un lancer (la vitesse du geste est donnée au lâcher).
            if (!m_Flying && !isHeld && Time.time - m_ReleaseTime < 0.15f && v.magnitude > 1.6f) m_Flying = true;
            if (!m_Flying) return;

            // Vol stable pointe en avant (lest à l'avant, empennage à l'arrière).
            if (v.sqrMagnitude > 1f)
            {
                var target = Quaternion.LookRotation(v, transform.up);
                m_Body.MoveRotation(Quaternion.Slerp(m_Body.rotation, target, 1f - Mathf.Exp(-14f * Time.fixedDeltaTime)));
                m_Body.angularVelocity *= 0.8f;
            }
            else m_Flying = false;
            m_LastVelocity = v;
            m_LastRotation = m_Body.rotation;
        }

        void OnCollisionEnter(Collision c)
        {
            if (!m_Flying || c.contactCount == 0) return;
            m_Flying = false;
            var contact = c.GetContact(0);
            var forward = m_LastRotation * Vector3.forward;
            var board = c.collider.GetComponentInParent<DartBoard>();
            if (board != null && m_LastVelocity.magnitude > 1.5f && Vector3.Dot(forward, -contact.normal) > 0.25f)
            {
                Stick(board, c.collider, contact.point, forward);
                return;
            }
            SfxBank.Play(SfxBank.ImpactLeger, contact.point, 0.25f, Random.Range(1.5f, 1.8f));
        }

        void Stick(DartBoard board, Collider boardCollider, Vector3 point, Vector3 forward)
        {
            m_Board = board;
            m_IgnoredBoard = boardCollider;
            foreach (var c in m_Colliders) Physics.IgnoreCollision(c, boardCollider, true);   // la pointe est dans la cible
            m_Body.linearVelocity = Vector3.zero; m_Body.angularVelocity = Vector3.zero;
            m_Body.rotation = m_LastRotation;
            m_Body.position = point + forward * m_Penetration - forward * m_Length;
            transform.SetPositionAndRotation(m_Body.position, m_Body.rotation);
            m_Joint = gameObject.AddComponent<FixedJoint>();
            m_Joint.connectedBody = boardCollider.attachedRigidbody;   // null = le monde (cible fixe)
            // « Toc » sec de la pointe dans le sisal.
            if (!SfxBank.Play(SfxBank.ImpactLeger, point, 0.55f, Random.Range(1.7f, 2f)))
                Sfx.PlayAt(Sfx.Clunk, point, 0.4f, 2.2f);
        }

        void Unstick()
        {
            if (m_Joint != null) { Destroy(m_Joint); m_Joint = null; }
            m_Board = null;
            if (!isHeld && m_IgnoredBoard != null)
            {
                foreach (var c in m_Colliders) Physics.IgnoreCollision(c, m_IgnoredBoard, false);
                m_IgnoredBoard = null;
            }
        }
    }
}
