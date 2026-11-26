using System.Collections;
using System.Collections.Generic;
using Fragile.Audio;
using Fragile.Breakables;
using UnityEngine;

namespace Fragile.Mice
{
    /// <summary>
    /// Souris qui se balade dans le bar : trottine en longeant les murs, s'arrête renifler, contourne les meubles,
    /// détale vers un trou quand le joueur approche ou au bruit d'un coup de feu / d'une casse.
    /// Elle meurt sous un coup de batte, une bouteille lancée ou une balle ; la réaction dépend de la violence du coup
    /// (petit bond, vol plané au fusil à pompe, écrasée par un coup de batte vers le bas), avec giclée de sang et
    /// une flaque bien visible qui s'étale sous le corps. Le corps disparaît après un moment ; le nid en refait venir.
    /// Animation procédurale (le modèle n'a pas de squelette) : trot, roulis, reniflement.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Mouse : MonoBehaviour, IHittable
    {
        [SerializeField] Transform m_Visual;
        [SerializeField] float m_WalkSpeed = 0.22f;
        [SerializeField] float m_RunSpeed = 1.3f;
        [Tooltip("Distance (à plat) de la tête du joueur qui la fait fuir.")]
        [SerializeField] float m_FearDistance = 1.1f;

        enum State { Wander, Pause, Flee, Dead }

        static readonly List<Mouse> s_All = new List<Mouse>();

        MouseNest m_Nest;
        Rigidbody m_Body;
        Collider m_Collider;
        State m_State = State.Pause;
        Vector3 m_Target, m_Heading;
        float m_Timer, m_Stuck, m_Phase, m_NextSqueak;
        Vector3 m_VisualHome; Quaternion m_VisualHomeRot; Vector3 m_VisualScale;

        public bool dead => m_State == State.Dead;

        /// <summary>Bruit violent (tir, casse) : toutes les souris à portée détalent.</summary>
        public static void ScareAll(Vector3 origin, float radius)
        {
            foreach (var m in s_All)
                if (m != null && !m.dead && (m.transform.position - origin).sqrMagnitude < radius * radius) m.Flee();
        }

        public void Init(MouseNest nest) => m_Nest = nest;

        void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
            m_Collider = GetComponent<Collider>();
            if (m_Visual != null) { m_VisualHome = m_Visual.localPosition; m_VisualHomeRot = m_Visual.localRotation; m_VisualScale = m_Visual.localScale; }
            m_Heading = transform.forward;
            m_Timer = Random.Range(0.3f, 1.5f);
            m_NextSqueak = Time.time + Random.Range(3f, 10f);
        }

        void OnEnable() => s_All.Add(this);
        void OnDisable() => s_All.Remove(this);

        // ------------------------------------------------------------------ déplacement

        void Update()
        {
            if (m_State == State.Dead) return;
            float dt = Time.deltaTime;
            var pos = m_Body.position;

            // Peur du joueur (sa tête, à plat).
            var cam = Camera.main;
            if (cam != null && m_State != State.Flee)
            {
                var d = cam.transform.position - pos; d.y = 0f;
                if (d.magnitude < m_FearDistance) Flee();
            }

            switch (m_State)
            {
                case State.Pause:
                    m_Timer -= dt;
                    if (m_Timer <= 0f) { m_State = State.Wander; m_Target = m_Nest != null ? m_Nest.RandomPoint(pos) : pos + Random.insideUnitSphere; }
                    break;
                case State.Wander:
                case State.Flee:
                    Move(pos, dt, m_State == State.Flee ? m_RunSpeed : m_WalkSpeed);
                    break;
            }

            if (Time.time > m_NextSqueak)
            {
                Sfx.PlayAt(Sfx.Squeak, pos, m_State == State.Flee ? 0.5f : 0.25f, Random.Range(0.85f, 1.2f));
                m_NextSqueak = Time.time + (m_State == State.Flee ? Random.Range(0.6f, 1.5f) : Random.Range(5f, 14f));
            }
            Animate(dt);
        }

        void Move(Vector3 pos, float dt, float speed)
        {
            var to = m_Target - pos; to.y = 0f;
            if (to.magnitude < 0.12f)
            {
                if (m_State == State.Flee && m_Nest != null && m_Nest.IsHole(m_Target)) { m_Nest.Hide(this); return; }
                m_State = State.Pause; m_Timer = Random.Range(0.4f, 3f);
                return;
            }
            // Cap : tourne vers la cible, vite quand elle fuit.
            var want = to.normalized;
            float turn = (m_State == State.Flee ? 720f : 300f) * dt;
            m_Heading = Vector3.RotateTowards(m_Heading, want, turn * Mathf.Deg2Rad, 0f);
            m_Heading.y = 0f; m_Heading.Normalize();

            // Obstacle devant (meuble, mur, pieds du joueur) : on tourne.
            float look = speed * dt + 0.07f;
            if (Blocked(pos, m_Heading, look, out var normal))
            {
                var away = Vector3.ProjectOnPlane(normal, Vector3.up).normalized;
                var side = Vector3.Cross(Vector3.up, away) * (Random.value < 0.5f ? 1f : -1f);
                m_Heading = (side * 0.8f + away * 0.6f).normalized;
                m_Stuck += dt;
                if (m_Stuck > 0.8f) { m_Target = m_Nest != null ? m_Nest.RandomPoint(pos) : pos - m_Heading; m_Stuck = 0f; }
                if (Blocked(pos, m_Heading, look, out _)) { m_Body.MoveRotation(Quaternion.LookRotation(m_Heading)); return; }
            }
            else m_Stuck = Mathf.Max(0f, m_Stuck - dt);

            var next = pos + m_Heading * speed * dt;
            if (m_Nest != null) next = m_Nest.Clamp(next);
            next.y = Ground(next, pos.y);
            m_Body.MovePosition(next);
            m_Body.MoveRotation(Quaternion.LookRotation(m_Heading));
            m_Phase += dt * Mathf.Lerp(9f, 20f, Mathf.InverseLerp(m_WalkSpeed, m_RunSpeed, speed));
        }

        bool Blocked(Vector3 pos, Vector3 dir, float distance, out Vector3 normal)
        {
            normal = Vector3.zero;
            foreach (var h in Physics.SphereCastAll(pos + Vector3.up * 0.035f, 0.026f, dir, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == m_Collider || h.normal.y > 0.55f || h.distance <= 0f) continue;
                if (h.rigidbody != null && h.rigidbody.GetComponent<Mouse>() != null) continue;
                normal = h.normal; return true;
            }
            return false;
        }

        float Ground(Vector3 p, float current)
        {
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 0.12f, Vector3.down, 0.4f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider == m_Collider || h.normal.y < 0.7f) continue;
                if (h.rigidbody != null && !h.rigidbody.isKinematic) continue;   // pas sur une bouteille
                // Pas sur le dos d'une autre souris (elles grimpaient l'une sur l'autre à l'infini).
                if (h.rigidbody != null && h.rigidbody.GetComponent<Mouse>() != null) continue;
                // Ni sur un meuble : elles restent au sol (une marche de quelques centimètres au plus).
                if (h.point.y > current + 0.06f) continue;
                if (h.point.y > best) best = h.point.y;
            }
            return float.IsNegativeInfinity(best) ? current : best;
        }

        public void Flee()
        {
            if (m_State == State.Dead) return;
            m_State = State.Flee;
            m_Target = m_Nest != null ? m_Nest.NearestHole(m_Body.position) : m_Body.position - m_Heading;
            m_NextSqueak = Time.time;
        }

        void Animate(float dt)
        {
            if (m_Visual == null) return;
            bool moving = m_State == State.Wander || m_State == State.Flee;
            if (moving)
            {
                // Trot : petits bonds, roulis, le corps ondule.
                float s = Mathf.Sin(m_Phase * Mathf.PI * 2f);
                m_Visual.localPosition = m_VisualHome + Vector3.up * Mathf.Abs(s) * 0.004f;
                m_Visual.localRotation = m_VisualHomeRot * Quaternion.Euler(s * 3f, Mathf.Sin(m_Phase * Mathf.PI) * 6f, s * 4f);
            }
            else
            {
                // Arrêt : reniflement (petits coups de tête), respiration.
                float t = Time.time;
                m_Visual.localPosition = m_VisualHome + Vector3.up * (0.0012f * Mathf.Sin(t * 7f));
                m_Visual.localRotation = m_VisualHomeRot * Quaternion.Euler(-3f + 3f * Mathf.Sin(t * 13f), 10f * Mathf.Sin(t * 1.7f), 0f);
            }
        }

        // ------------------------------------------------------------------ coups

        void OnCollisionEnter(Collision c)
        {
            if (c.rigidbody == null) return;
            float speed = c.relativeVelocity.magnitude;
            var breaker = c.rigidbody.GetComponentInParent<Breaker>();
            float power = breaker != null ? speed * breaker.power : speed * 0.5f;
            if (power < (breaker != null ? 0.8f : 1.4f)) return;
            var point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            // Sens du coup au moment du choc (la vitesse de la batte, elle, a déjà rebondi).
            // relativeVelocity = vitesse de l'autre objet par rapport à la souris (vérifié en jeu : batte vers le bas -> y < 0).
            var dir = c.relativeVelocity.normalized;
            if (dead) { Bleed(point, dir, 0.3f); return; }
            // Coup de batte vers le bas sur une souris au sol : écrasée.
            bool squash = breaker != null && dir.y < -0.55f;
            Kill(point, dir, power * (breaker != null ? 1.1f : 0.8f), squash);
        }

        /// <summary>Balle ou plombs (voir Firearm) : plus la poussée est forte, plus elle vole loin et saigne.</summary>
        public void Hit(Vector3 point, Vector3 direction, float energy, float push)
        {
            float speed = Mathf.Clamp(push * 1.4f, 2.5f, 15f);
            if (dead) { Bleed(point, direction, Mathf.InverseLerp(2f, 13f, speed) * 0.6f); return; }
            Kill(point, direction, speed, false);
        }

        void Kill(Vector3 point, Vector3 direction, float speed, bool squash)
        {
            m_State = State.Dead;
            float strength = Mathf.InverseLerp(1.5f, 13f, speed);
            if (m_Visual != null) { m_Visual.localPosition = m_VisualHome; m_Visual.localRotation = m_VisualHomeRot; }

            if (squash)
            {
                // Aplatie sur place.
                if (m_Visual != null) m_Visual.localScale = Vector3.Scale(m_VisualScale, new Vector3(1.3f, 0.3f, 1.12f));
                m_Body.isKinematic = true;
                strength = Mathf.Max(strength, 0.7f);
            }
            else
            {
                m_Body.isKinematic = false;
                m_Body.useGravity = true;
                m_Body.interpolation = RigidbodyInterpolation.Interpolate;
                m_Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var v = Vector3.ProjectOnPlane(direction, Vector3.up).normalized * speed + Vector3.up * speed * 0.35f;
                if (direction.y > 0.3f) v += direction * speed * 0.3f;
                m_Body.linearVelocity = Vector3.ClampMagnitude(v, 15f);
                m_Body.angularVelocity = Random.onUnitSphere * Mathf.Lerp(8f, 30f, strength);
            }

            Bleed(point, direction, strength);
            Sfx.PlayAt(Sfx.MouseDeath, point, 0.7f, Random.Range(0.9f, 1.15f));
            Sfx.PlayAt(Sfx.Splat, point, Mathf.Lerp(0.5f, 1f, strength), Random.Range(0.85f, 1.1f));
            ScareAll(point, 2.5f);
            StartCoroutine(Death(strength, squash));
        }

        void Bleed(Vector3 point, Vector3 direction, float strength)
        {
            BloodFx.Spray(point, -direction * 0.3f + direction + Vector3.up * 0.4f, strength);
            if (strength > 0.35f) BloodFx.Splat(point, direction, Mathf.Lerp(0.18f, 0.5f, strength), transform);
        }

        IEnumerator Death(float strength, bool squash)
        {
            // Flaque sous le corps dès qu'il est retombé (ou tout de suite s'il est écrasé).
            float still = 0f, waited = 0f;
            while (!squash && waited < 3f && still < 0.25f)
            {
                still = m_Body.linearVelocity.magnitude < 0.1f ? still + Time.deltaTime : 0f;
                waited += Time.deltaTime;
                yield return null;
            }
            BloodFx.Pool(transform.position, Mathf.Lerp(0.4f, 0.75f, strength) * (squash ? 1.2f : 1f), transform);
            yield return new WaitForSeconds(30f);
            // Le corps disparaît (la flaque reste encore un moment).
            var start = transform.localScale;
            for (float t = 0f; t < 1f; t += Time.deltaTime) { transform.localScale = start * (1f - t); yield return null; }
            if (m_Nest != null) m_Nest.Forget(this);
            Destroy(gameObject);
        }
    }
}
