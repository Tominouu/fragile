using System.Collections;
using System.Collections.Generic;
using Fragile.Audio;
using Fragile.Breakables;
using UnityEngine;

namespace Fragile.Trailer
{
    /// <summary>
    /// Un client du bar pendant le Trailer (humain MakeHuman, rig Humanoid, animations retargetées par Unity).
    /// Assis ou debout, il regarde la télé ou discute ; quand le personnage pète un câble, il tourne la tête vers lui,
    /// se lève et s'enfuit par la porte de sortie en suivant le chemin calculé par <see cref="TrailerCrowd"/>.
    /// Vraie physique : vivant, il n'a qu'une capsule à la racine (aucune physique sur les os : rien ne peut tirer sur le
    /// squelette animé). Touché par un objet lancé assez vite ou par la batte, un ragdoll est construit À CE MOMENT-LÀ
    /// à partir de sa pose (Rigidbody + CharacterJoint sur 11 os) et la physique prend le relais.
    /// En courant, la capsule devient solide et bouscule verres et bouteilles.
    /// États de l'Animator Controller (générés par Fragile > Trailer > Préparer les clients) :
    /// « Assis », « AssisDiscute », « Debout », « Discute », « Fuite », et si disponible « SeLever ».
    /// </summary>
    [DisallowMultipleComponent]
    public class BarClient : MonoBehaviour
    {
        public enum Mode { Seated, Standing, Fleeing, Gone, Ragdoll }

        public Mode State { get; private set; } = Mode.Standing;
        public bool Seated { get; private set; }
        public Vector3 HeadPosition => m_Head != null ? m_Head.position : transform.position + Vector3.up * 1.6f;

        /// <summary>Point regardé (télé, voisin, joueur) et intensité du regard (0..1).</summary>
        public Vector3 LookTarget;
        public float LookWeight;

        static readonly int Assis = Animator.StringToHash("Assis"), Debout = Animator.StringToHash("Debout"),
            Discute = Animator.StringToHash("Discute"), Fuite = Animator.StringToHash("Fuite"),
            SeLever = Animator.StringToHash("SeLever"), AssisDiscute = Animator.StringToHash("AssisDiscute");

        Animator m_Anim;
        Transform m_Head;
        TrailerCrowd m_Crowd;
        float m_Look;
        readonly List<Rigidbody> m_Bones = new List<Rigidbody>();
        Rigidbody m_Body;
        CapsuleCollider m_Capsule;
        Vector3 m_Velocity;
        float m_Speed, m_NextStep;
        Coroutine m_Run;

        // ------------------------------------------------------------------ installation

        public void Init(TrailerCrowd crowd, RuntimeAnimatorController controller, int seed)
        {
            m_Crowd = crowd;
            m_Anim = GetComponent<Animator>();
            if (m_Anim == null) m_Anim = gameObject.AddComponent<Animator>();
            m_Anim.runtimeAnimatorController = controller;
            m_Anim.applyRootMotion = false;
            m_Anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var rng = new System.Random(seed);
            m_Anim.speed = 0.9f + (float)rng.NextDouble() * 0.2f;
            m_Head = m_Anim.isHuman ? m_Anim.GetBoneTransform(HumanBodyBones.Head) : null;
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                r.updateWhenOffscreen = false;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            AddBodyProxy();
        }

        /// <summary>Capsule de détection à la racine (Rigidbody cinématique) : déplacée avec le personnage, elle ne touche jamais aux os.</summary>
        void AddBodyProxy()
        {
            m_Body = gameObject.AddComponent<Rigidbody>();
            m_Body.isKinematic = true;
            m_Body.interpolation = RigidbodyInterpolation.None;
            m_Capsule = gameObject.AddComponent<CapsuleCollider>();
            m_Capsule.center = new Vector3(0f, 0.9f, 0f);
            m_Capsule.height = 1.75f;
            m_Capsule.radius = 0.22f;
            m_Capsule.isTrigger = true;   // assis / debout : détecte sans pousser les verres posés à côté
        }

        /// <summary>Assis : les hanches viennent se poser sur l'assise, le buste tourné vers <paramref name="forward"/>.</summary>
        public void SitOn(Vector3 seat, Vector3 forward, float phase, bool talking = false)
        {
            Seated = true;
            State = Mode.Seated;
            transform.rotation = Quaternion.LookRotation(Flat(forward), Vector3.up);
            transform.position = seat;
            m_Anim.Play(talking && Has(AssisDiscute) ? AssisDiscute : Assis, 0, phase);
            m_Anim.Update(0f);
            var hips = m_Anim.isHuman ? m_Anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips != null)
            {
                // Hanches au-dessus du point d'assise, légèrement en retrait (on s'assoit au fond).
                var target = seat - Flat(forward) * 0.04f + Vector3.up * 0.1f;
                transform.position += target - hips.position;
                m_Anim.Update(0f);
            }
        }

        public void StandAt(Vector3 floor, Vector3 forward, bool talking, float phase)
        {
            Seated = false;
            State = Mode.Standing;
            transform.SetPositionAndRotation(floor, Quaternion.LookRotation(Flat(forward), Vector3.up));
            m_Anim.Play(talking && Has(Discute) ? Discute : Debout, 0, phase);
        }

        bool Has(int state) => m_Anim.HasState(0, state);

        // ------------------------------------------------------------------ fuite

        /// <summary>Panique : après <paramref name="delay"/> s, il se lève et court le long de <paramref name="path"/> (monde).</summary>
        public void Flee(List<Vector3> path, float delay, float speed)
        {
            if (State == Mode.Ragdoll || State == Mode.Gone || State == Mode.Fleeing) return;
            State = Mode.Fleeing;
            m_Speed = speed;
            m_Run = StartCoroutine(Run(path, delay));
        }

        IEnumerator Run(List<Vector3> path, float delay)
        {
            for (float t = 0f; t < delay; t += Time.deltaTime) yield return null;
            if (State == Mode.Ragdoll) yield break;

            if (Seated)
            {
                if (Random.value < 0.45f && !SfxBank.Play(SfxBank.ChaiseTombe, transform.position, 0.5f, Random.Range(0.9f, 1.1f)))
                    SfxBank.Play(SfxBank.ObjetTombe, transform.position, 0.4f);
                if (Has(SeLever))
                {
                    m_Anim.CrossFadeInFixedTime(SeLever, 0.15f);
                    for (float t = 0f; t < 0.55f; t += Time.deltaTime) yield return null;
                }
                Seated = false;
            }
            if (m_Capsule != null) m_Capsule.isTrigger = false;   // en courant, il bouscule ce qu'il touche
            m_Anim.CrossFadeInFixedTime(Fuite, 0.2f);
            m_Anim.speed = m_Speed / 5f;   // cadence des pas à la vitesse réelle

            int i = 0;
            var pos = transform.position;
            // On garde la hauteur du sol du chemin (pas celle de l'assise).
            while (i < path.Count && State == Mode.Fleeing)
            {
                var target = path[i];
                var to = target - pos; to.y = 0f;
                float step = m_Speed * Time.deltaTime;
                if (to.magnitude <= Mathf.Max(step, 0.2f) && i < path.Count - 1) { i++; continue; }
                var dir = to.normalized + m_Crowd.Separation(this) * 0.8f;
                dir.y = 0f;
                if (dir.sqrMagnitude > 1e-4f) dir.Normalize();
                var next = pos + dir * Mathf.Min(step, to.magnitude);
                next.y = Mathf.MoveTowards(pos.y, target.y, 2.5f * Time.deltaTime);
                m_Velocity = (next - pos) / Mathf.Max(Time.deltaTime, 1e-4f);
                pos = next;
                transform.position = pos;
                if (dir.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir, Vector3.up), 1f - Mathf.Exp(-Time.deltaTime * 10f));
                Footstep();
                if (i == path.Count - 1 && to.magnitude < 0.15f) break;
                yield return null;
            }
            if (State != Mode.Fleeing) yield break;
            State = Mode.Gone;
            m_Crowd.Exited(this);
            gameObject.SetActive(false);
        }

        void Footstep()
        {
            if (Time.time < m_NextStep) return;
            m_NextStep = Time.time + 0.3f / Mathf.Max(0.5f, m_Speed / 5f);
            var cam = Camera.main;
            if (cam != null && (cam.transform.position - transform.position).sqrMagnitude > 64f) return;   // au-delà de 8 m
            SfxBank.Play(SfxBank.PasParquet, transform.position, 0.35f, Random.Range(0.9f, 1.15f));
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        // ------------------------------------------------------------------ regard

        void OnAnimatorIK(int layer)
        {
            if (State == Mode.Ragdoll || m_Anim == null) return;
            m_Look = Mathf.MoveTowards(m_Look, State == Mode.Fleeing ? LookWeight * 0.4f : LookWeight, Time.deltaTime * 1.5f);
            // Yeux fixés à la tête (voir generate_clients.py) : c'est la tête et le buste qui suivent le regard.
            m_Anim.SetLookAtWeight(m_Look, 0.25f, 0.9f, 0f, 0.5f);
            m_Anim.SetLookAtPosition(LookTarget);
        }

        // ------------------------------------------------------------------ physique

        /// <summary>Construit le ragdoll à partir de la pose actuelle (appelé seulement quand la personne tombe).</summary>
        void BuildRagdoll()
        {
            if (!m_Anim.isHuman) return;
            Transform B(HumanBodyBones b) => m_Anim.GetBoneTransform(b);
            var hips = B(HumanBodyBones.Hips);
            var chest = B(HumanBodyBones.Chest) ?? B(HumanBodyBones.Spine);
            var head = B(HumanBodyBones.Head);
            var lul = B(HumanBodyBones.LeftUpperLeg); var rul = B(HumanBodyBones.RightUpperLeg);
            if (hips == null || chest == null || head == null || lul == null || rul == null) return;

            var hipsRb = Bone(hips, null, 12f, Capsule(hips, lul.position, rul.position, 0.12f));
            var chestRb = Bone(chest, hipsRb, 14f, Capsule(chest, chest.position, (B(HumanBodyBones.Neck) ?? head).position, 0.14f));
            Bone(head, chestRb, 5f, Sphere(head, head.position + Vector3.up * 0.09f, 0.11f));
            Limb(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, chestRb, 2.5f, 0.05f, out var lua);
            Limb(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, lua, 1.5f, 0.04f, out _);
            Limb(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, chestRb, 2.5f, 0.05f, out var rua);
            Limb(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, rua, 1.5f, 0.04f, out _);
            Limb(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, hipsRb, 8f, 0.08f, out var lul2);
            Limb(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, lul2, 4f, 0.06f, out _);
            Limb(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, hipsRb, 8f, 0.08f, out var rul2);
            Limb(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, rul2, 4f, 0.06f, out _);

            // Pas de collisions entre les membres d'une même personne.
            var cols = new List<Collider>();
            foreach (var rb in m_Bones) cols.AddRange(rb.GetComponents<Collider>());
            for (int a = 0; a < cols.Count; a++)
                for (int b = a + 1; b < cols.Count; b++)
                    Physics.IgnoreCollision(cols[a], cols[b], true);
        }

        void Limb(HumanBodyBones from, HumanBodyBones to, Rigidbody parent, float mass, float radius, out Rigidbody rb)
        {
            rb = null;
            var a = m_Anim.GetBoneTransform(from);
            var b = m_Anim.GetBoneTransform(to);
            if (a == null || b == null || parent == null) return;
            rb = Bone(a, parent, mass, Capsule(a, a.position, b.position, radius));
        }

        Rigidbody Bone(Transform t, Rigidbody parent, float mass, Collider col)
        {
            var rb = t.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            if (parent != null)
            {
                var j = t.gameObject.AddComponent<CharacterJoint>();
                j.connectedBody = parent;
                j.enableProjection = true;
                j.lowTwistLimit = new SoftJointLimit { limit = -35f };
                j.highTwistLimit = new SoftJointLimit { limit = 35f };
                j.swing1Limit = new SoftJointLimit { limit = 40f };
                j.swing2Limit = new SoftJointLimit { limit = 25f };
            }
            m_Bones.Add(rb);
            return rb;
        }

        static Collider Capsule(Transform t, Vector3 a, Vector3 b, float radius)
        {
            var c = t.gameObject.AddComponent<CapsuleCollider>();
            var la = t.InverseTransformPoint(a);
            var lb = t.InverseTransformPoint(b);
            var d = lb - la;
            var ad = new Vector3(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z));
            c.direction = ad.x > ad.y && ad.x > ad.z ? 0 : ad.y > ad.z ? 1 : 2;
            float scale = Mathf.Max(1e-4f, t.lossyScale.x);
            c.center = (la + lb) * 0.5f;
            c.radius = radius / scale;
            c.height = d.magnitude + c.radius * 2f;
            return c;
        }

        static Collider Sphere(Transform t, Vector3 center, float radius)
        {
            var c = t.gameObject.AddComponent<SphereCollider>();
            c.center = t.InverseTransformPoint(center);
            c.radius = radius / Mathf.Max(1e-4f, t.lossyScale.x);
            return c;
        }

        void OnTriggerEnter(Collider other)
        {
            var rb = other.attachedRigidbody;
            if (rb == null || rb.isKinematic) return;   // meubles, décor : on ne fait rien (et pas de calcul de point)
            HitBy(rb, SafePoint(other, transform.position + Vector3.up));
        }

        /// <summary>ClosestPoint n'accepte que les colliders simples ou les maillages convexes.</summary>
        static Vector3 SafePoint(Collider c, Vector3 from)
        {
            bool ok = c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider mc && mc.convex);
            return ok ? c.ClosestPoint(from) : c.bounds.ClosestPoint(from);
        }
        void OnCollisionEnter(Collision c) => HitBy(c.rigidbody, c.contactCount > 0 ? c.GetContact(0).point : transform.position + Vector3.up);

        /// <summary>Un objet lancé assez vite (ou la batte) le fait tomber pour de vrai.</summary>
        void HitBy(Rigidbody other, Vector3 point)
        {
            if (State == Mode.Ragdoll || State == Mode.Gone || other == null || other.isKinematic) return;
            if (other.GetComponent<Debris>() != null || other.transform.IsChildOf(transform)) return;
            bool bat = other.GetComponentInParent<Breaker>() != null;
            float speed = other.linearVelocity.magnitude;
            if (other.mass < 0.15f || speed < (bat ? 2.5f : 4f)) return;
            Collapse(point, other.linearVelocity * Mathf.Min(other.mass, 3f) * (bat ? 3f : 1.5f));
        }

        /// <summary>Le corps s'effondre : l'animation s'arrête, le ragdoll est construit dans la pose actuelle et part avec l'élan.</summary>
        public void Collapse(Vector3 point, Vector3 impulse)
        {
            if (State == Mode.Ragdoll) return;
            State = Mode.Ragdoll;
            if (m_Run != null) StopCoroutine(m_Run);
            m_Anim.enabled = false;                       // la pose de cette image reste figée...
            if (m_Capsule != null) Destroy(m_Capsule);
            if (m_Body != null) Destroy(m_Body);
            BuildRagdoll();                               // ... et devient physique
            Rigidbody nearest = null; float best = float.MaxValue;
            foreach (var rb in m_Bones)
            {
                rb.linearVelocity = m_Velocity;
                float d = (rb.worldCenterOfMass - point).sqrMagnitude;
                if (d < best) { best = d; nearest = rb; }
            }
            if (nearest != null) nearest.AddForceAtPosition(impulse, point, ForceMode.Impulse);
            m_Crowd.Fell(this);
        }
    }
}
