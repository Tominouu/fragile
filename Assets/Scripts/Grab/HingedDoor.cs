using Fragile.Breakables;
using Fragile.Music;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fragile.Grab
{
    /// <summary>
    /// Porte à charnière « graissée » (frigo, placard). On saisit la poignée (grip) et la porte suit la main ;
    /// lâchée, elle garde son élan (peu de frottement), rebondit en butée, revient d'elle-même près de la fermeture
    /// (frigo légèrement incliné) et l'aimant du joint la plaque avec un bruit sourd.
    /// Un coup (batte, objet lancé) la pousse ; une <see cref="GlassPane"/> enfant reçoit les coups portés sur la vitre.
    /// Pivot de l'objet sur la charnière, axe = Y local. Fermée, la poignée est côté −X ; un angle positif ouvre vers +Z.
    /// Rigidbody cinématique : la porte pousse les bouteilles sans les traverser, vitesse limitée pour ne pas les casser.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class HingedDoor : XRBaseInteractable
    {
        [SerializeField] float m_MaxAngle = 105f;
        [Tooltip("Vitesse maximale (°/s). Bord à 60 cm : 250°/s ≈ 2,6 m/s, sous le seuil de casse des bouteilles.")]
        [SerializeField] float m_MaxSpeed = 250f;

        [Header("Charnière graissée")]
        [Tooltip("Frottement de la charnière (1/s) : faible = la porte file longtemps.")]
        [SerializeField] float m_Friction = 0.8f;
        [Tooltip("Rappel vers la fermeture (°/s²) dans les derniers degrés (frigo incliné vers l'arrière).")]
        [SerializeField] float m_SelfClose = 60f;
        [SerializeField] float m_SelfCloseZone = 35f;
        [Tooltip("Aimant du joint : attraction (°/s²) dans les derniers degrés.")]
        [SerializeField] float m_Magnet = 900f;
        [SerializeField] float m_MagnetZone = 6f;
        [Tooltip("Rebond en butée d'ouverture (0 = mort, 1 = élastique).")]
        [SerializeField] float m_Bounce = 0.15f;
        [SerializeField] float m_DoorMass = 7f;
        [SerializeField] float m_Width = 0.6f;

        static AudioClip s_Thud;

        Rigidbody m_Body;
        GlassPane m_Glass;
        Quaternion m_ClosedLocalRot;
        Vector3 m_HingeLocalPos;
        float m_Angle, m_Velocity, m_HandTarget, m_GrabOffset;

        public float angle => m_Angle;

        protected override void Awake()
        {
            base.Awake();
            m_Body = GetComponent<Rigidbody>();
            m_Body.isKinematic = true;
            m_Body.interpolation = RigidbodyInterpolation.Interpolate;
            m_ClosedLocalRot = transform.localRotation;
            m_HingeLocalPos = transform.localPosition;
            m_Glass = GetComponentInChildren<GlassPane>();
            if (s_Thud == null) s_Thud = CreateThud();
        }

        /// <summary>Angle de la main autour de la charnière, dans le repère de la porte fermée.</summary>
        float HandAngle(Vector3 world)
        {
            var parent = transform.parent;
            var local = parent != null ? parent.InverseTransformPoint(world) : world;
            var d = Quaternion.Inverse(m_ClosedLocalRot) * (local - m_HingeLocalPos);
            return Mathf.Atan2(d.z, -d.x) * Mathf.Rad2Deg;
        }

        Vector3 HandPoint(IXRInteractor interactor) => interactor.GetAttachTransform(this).position;

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            m_GrabOffset = m_Angle - HandAngle(HandPoint(args.interactorObject));
            m_HandTarget = m_Angle;
            FingerTips.Pulse(args.interactorObject, 0.35f, 0.04f);
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic || !isSelected) return;
            float a = HandAngle(HandPoint(interactorsSelecting[0])) + m_GrabOffset;
            if (float.IsNaN(a)) return;
            float clamped = Mathf.Clamp(a, 0f, m_MaxAngle);
            if ((clamped <= 0f && m_HandTarget > 0.5f) || (clamped >= m_MaxAngle && m_HandTarget < m_MaxAngle - 0.5f))
                FingerTips.Pulse(interactorsSelecting[0], 0.5f, 0.03f);
            m_HandTarget = clamped;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            float before = m_Angle;

            if (isSelected)
            {
                // Tenue : la porte suit la main ; on mesure sa vitesse pour qu'elle garde l'élan au lâcher.
                m_Angle = Mathf.MoveTowards(m_Angle, m_HandTarget, m_MaxSpeed * dt);
                m_Velocity = Mathf.Lerp(m_Velocity, (m_Angle - before) / dt, 0.5f);
            }
            else
            {
                if (m_Angle <= 0f && m_Velocity <= 0f) { m_Velocity = 0f; return; } // fermée, au repos
                float acc = -m_SelfClose * Mathf.Clamp01(1f - m_Angle / m_SelfCloseZone);
                if (m_Angle < m_MagnetZone) acc -= m_Magnet * (1f - m_Angle / m_MagnetZone);
                m_Velocity += acc * dt;
                m_Velocity *= Mathf.Exp(-m_Friction * dt);
                m_Velocity = Mathf.Clamp(m_Velocity, -m_MaxSpeed, m_MaxSpeed);
                m_Angle += m_Velocity * dt;

                if (m_Angle <= 0f)
                {
                    // Joint aimanté : pas de rebond, un « poc » sourd d'autant plus fort que la porte arrive vite.
                    if (m_Velocity < -15f) PlayThud(Mathf.InverseLerp(15f, 220f, -m_Velocity), 1f);
                    m_Angle = 0f; m_Velocity = 0f;
                }
                else if (m_Angle >= m_MaxAngle)
                {
                    if (m_Velocity > 30f) PlayThud(Mathf.InverseLerp(30f, 250f, m_Velocity) * 0.6f, 1.5f);
                    m_Angle = m_MaxAngle;
                    m_Velocity = -m_Velocity * m_Bounce;
                }
            }

            if (Mathf.Approximately(before, m_Angle)) return;
            var parent = transform.parent;
            var localRot = m_ClosedLocalRot * Quaternion.Euler(0f, m_Angle, 0f);
            m_Body.MoveRotation(parent != null ? parent.rotation * localRot : localRot);
            m_Body.MovePosition(parent != null ? parent.TransformPoint(m_HingeLocalPos) : m_HingeLocalPos);
        }

        void OnCollisionEnter(Collision c)
        {
            var rb = c.rigidbody;
            if (rb == null || rb.isKinematic || rb.GetComponent<Debris>() != null || c.contactCount == 0) return;
            var contact = c.GetContact(0);
            var breaker = rb.GetComponentInParent<Breaker>();
            // Seuls les vrais coups comptent (batte, objet lancé) : pas les bouteilles que la porte pousse.
            if (breaker == null && rb.linearVelocity.magnitude < 1.5f) return;

            if (m_Glass != null && contact.thisCollider == m_Glass.paneCollider) m_Glass.Hit(c);
            if (isSelected) return;

            // Le coup fait tourner la porte : moment de l'impulsion autour de la charnière / inertie de la porte.
            var strike = -c.relativeVelocity * Mathf.Min(rb.mass, 3f);
            var lever = contact.point - transform.position;
            float inertia = m_DoorMass * m_Width * m_Width / 3f;
            float dw = Vector3.Dot(Vector3.Cross(lever, strike), transform.up) / inertia * Mathf.Rad2Deg;
            m_Velocity = Mathf.Clamp(m_Velocity + dw, -m_MaxSpeed, m_MaxSpeed);
        }

        void PlayThud(float volume, float pitch)
        {
            var go = new GameObject("Son porte");
            go.transform.position = transform.TransformPoint(new Vector3(-m_Width, 1f, 0f));
            var src = go.AddComponent<AudioSource>();
            src.clip = s_Thud;
            src.spatialBlend = 1f; src.minDistance = 0.6f; src.maxDistance = 15f; src.dopplerLevel = 0f;
            src.volume = Mathf.Lerp(0.25f, 0.9f, volume); src.pitch = pitch * Random.Range(0.94f, 1.06f);
            src.Play();
            Destroy(go, s_Thud.length / src.pitch + 0.2f);
        }

        /// <summary>Fermeture de frigo générée par le code : choc sourd grave + petit souffle du joint.</summary>
        static AudioClip CreateThud()
        {
            const int rate = 44100;
            int n = (int)(rate * 0.35f);
            var d = new float[n];
            var rng = new System.Random(7);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float body = Mathf.Sin(2f * Mathf.PI * (62f + 40f * Mathf.Exp(-t * 30f)) * t) * Mathf.Exp(-t * 18f);
                float knock = Mathf.Sin(2f * Mathf.PI * 190f * t) * Mathf.Exp(-t * 60f) * 0.5f;
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.08f;           // souffle filtré
                float seal = lp * Mathf.Exp(-t * 25f) * 0.9f;
                d[i] = Mathf.Clamp((body + knock + seal) * 0.85f, -1f, 1f);
            }
            var clip = AudioClip.Create("Fermeture porte", n, 1, rate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
