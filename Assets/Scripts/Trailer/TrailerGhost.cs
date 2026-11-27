using System;
using System.Collections;
using System.Collections.Generic;
using Fragile.Audio;
using Fragile.Breakables;
using Fragile.Grab;
using Fragile.Hands;
using Fragile.TV;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using TrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;

namespace Fragile.Trailer
{
    /// <summary>
    /// Le « personnage » du Trailer : pendant l'intro, les mains ne suivent plus les manettes, c'est ce script qui les
    /// bouge (le joueur ne garde que la tête). Il joue la VRAIE partie : les manettes virtuelles sont déplacées, les prises
    /// passent par XRI (StartManualInteraction), donc la main se referme sur l'objet, les lancers et la batte cassent
    /// le décor avec la physique et les règles de Tom (Breakable, Breaker, TvBreakable).
    /// Tout est retrouvé et placé par rapport au tabouret choisi : le comptoir, les verres et bouteilles à portée, la batte.
    ///
    /// Repère « corps » : x = droite, y = haut, z = vers le comptoir / la télé, origine = poitrine.
    /// </summary>
    [DefaultExecutionOrder(-300)]   // avant XRI : les objets tenus suivent les mains dans la même image
    public class TrailerGhost : MonoBehaviour
    {
        class Arm
        {
            public bool Left;
            public Transform Ctrl;
            public ControllerHandPose Hand;
            public Renderer Skin;
            public XRBaseInputInteractor Interactor;
            public Vector3 Pos;                 // repère corps
            public Quaternion Rot = Quaternion.identity;
            public float Trigger = 0.2f, Grip = 0.3f, TriggerT = 0.2f, GripT = 0.3f;
            public IXRSelectInteractable Held;
            public Vector3 World, Velocity;     // position monde (paume) et vitesse, pour pousser les objets
        }

        /// <summary>Appelé à chaque effort (lancer, coup) : le Trailer y joue un souffle.</summary>
        public event Action<float> Effort;
        /// <summary>Appelé quand l'écran regardé encaisse un coup (la foule de la télé se tait).</summary>
        public event Action TvHit;
        /// <summary>Premier éclat (premier coup de poing) : les clients paniquent.</summary>
        public event Action Outburst;
        /// <summary>Coup final : le Trailer coupe au noir à cet instant.</summary>
        public event Action FinalImpact;

        /// <summary>Tremblement de rage des mains (0..1).</summary>
        public float Tremble;

        /// <summary>Direction du corps (vers le comptoir / la télé), à plat.</summary>
        public Vector3 Forward => m_BodyRot * Vector3.forward;

        XROrigin m_Origin;
        TrailerAnchors m_A;
        Arm m_L, m_R;
        Vector3 m_BodyPos;
        Quaternion m_BodyRot;
        bool m_HasCounter;
        float m_Top = -0.3f, m_Edge = 0.45f;    // dessus et bord du comptoir, repère corps
        readonly HashSet<UnityEngine.Object> m_Struck = new HashSet<UnityEngine.Object>();
        readonly List<Behaviour> m_Disabled = new List<Behaviour>();
        readonly HashSet<XRBaseInteractable> m_Used = new HashSet<XRBaseInteractable>();   // déjà lancés / fracassés

        const float PalmRoll = 90f;             // rotation de la manette pour une main à plat (paume vers le bas)

        // ------------------------------------------------------------------ installation

        public bool Setup(XROrigin origin, TrailerAnchors anchors)
        {
            m_Origin = origin; m_A = anchors;

            // Le personnage regarde le comptoir dans la direction de la télé.
            var f = anchors.LookAt - anchors.Eye; f.y = 0f;
            m_BodyRot = Quaternion.LookRotation(f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward, Vector3.up);
            m_BodyPos = anchors.Eye - Vector3.up * 0.42f;
            FindCounter();

            foreach (var mm in origin.GetComponentsInChildren<XRInputModalityManager>(true))
            {
                foreach (var go in new[] { mm.leftController, mm.rightController }) if (go != null) go.SetActive(true);
                foreach (var go in new[] { mm.leftHand, mm.rightHand }) if (go != null) go.SetActive(false);
                Disable(mm);
            }

            foreach (var hand in origin.GetComponentsInChildren<ControllerHandPose>(true))
            {
                var arm = MakeArm(hand);
                if (arm == null) continue;
                if (arm.Left) m_L = arm; else m_R = arm;
            }
            if (m_L == null || m_R == null)
            {
                Debug.LogWarning("[Fragile] Trailer : mains introuvables sous le XR Origin, le personnage ne bougera pas.");
                return false;
            }

            foreach (var arm in new[] { m_L, m_R })
            {
                arm.Pos = Rest(arm.Left);
                arm.Rot = PalmDown(arm.Left);
            }
            Apply(0f);
            return true;
        }

        Arm MakeArm(ControllerHandPose hand)
        {
            // La manette = l'ancêtre qui porte le TrackedPoseDriver ; c'est lui qu'on pilote.
            Transform ctrl = null;
            for (var t = hand.transform.parent; t != null && t != m_Origin.transform; t = t.parent)
                if (t.GetComponent<TrackedPoseDriver>() != null) { ctrl = t; break; }
            if (ctrl == null) ctrl = hand.transform.parent;
            if (ctrl == null) return null;

            string n = (hand.name + " " + ctrl.name).ToLowerInvariant();
            var arm = new Arm { Left = n.Contains("left") || n.Contains("gauche"), Ctrl = ctrl, Hand = hand };
            arm.Skin = hand.GetComponentInChildren<SkinnedMeshRenderer>();

            foreach (var d in ctrl.GetComponents<TrackedPoseDriver>()) Disable(d);
            Disable(hand);   // les doigts sont pilotés ici (ApplyPose), plus par les gâchettes

            // Un seul interactor sert aux prises ; les boutons du joueur ne font plus rien.
            // Priorité : interactor proche/lointain du template, sinon direct, sinon n'importe quel interactor à entrée
            // (jamais celui de téléportation s'il y a mieux).
            var all = ctrl.GetComponentsInChildren<XRBaseInteractor>(true);
            int Rank(XRBaseInteractor i) => i is NearFarInteractor ? 0 : i is XRDirectInteractor ? 1 : i is XRBaseInputInteractor && !(i is XRPokeInteractor) ? 2 : 9;
            foreach (var i in all) if (Rank(i) < 9 && (arm.Interactor == null || Rank(i) < Rank(arm.Interactor))) arm.Interactor = (XRBaseInputInteractor)i;
            foreach (var i in all)
            {
                if (i != arm.Interactor) { Disable(i); continue; }
                var input = arm.Interactor;
                input.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.Unused;
                input.activateInput.inputSourceMode = XRInputButtonReader.InputSourceMode.Unused;
                if (input is NearFarInteractor nf) nf.uiPressInput.inputSourceMode = XRInputButtonReader.InputSourceMode.Unused;
            }
            return arm;
        }

        void Disable(Behaviour b)
        {
            if (b == null || !b.enabled) return;
            b.enabled = false;
            m_Disabled.Add(b);
        }

        /// <summary>Trouve le dessus du comptoir devant le tabouret (premier plan horizontal fixe entre 20 et 100 cm sous les yeux).</summary>
        void FindCounter()
        {
            var f = m_BodyRot * Vector3.forward;
            for (float d = 0.2f; d <= 1.0f; d += 0.04f)
            {
                var from = m_A.Eye + f * d + Vector3.up * 0.1f;
                float best = float.NegativeInfinity;
                foreach (var h in Physics.RaycastAll(from, Vector3.down, 1.3f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (Ignored(h.collider)) continue;
                    float drop = m_A.Eye.y - h.point.y;
                    if (drop < 0.2f || drop > 1.0f || h.normal.y < 0.8f) continue;
                    best = Mathf.Max(best, h.point.y);
                }
                if (float.IsNegativeInfinity(best)) continue;
                m_HasCounter = true;
                m_Top = best - m_BodyPos.y;
                m_Edge = d;
                return;
            }
        }

        bool Ignored(Collider c)
        {
            if (m_A.Stool != null && c.transform.IsChildOf(m_A.Stool)) return true;
            if (c.transform.IsChildOf(m_Origin.transform)) return true;
            var rb = c.attachedRigidbody;
            return rb != null && !rb.isKinematic;   // verres, bouteilles : pas le comptoir
        }

        Vector3 Rest(bool left) => m_HasCounter
            ? new Vector3(left ? -0.17f : 0.17f, m_Top + 0.035f, m_Edge + 0.1f)
            : new Vector3(left ? -0.15f : 0.15f, -0.32f, 0.25f);

        static Quaternion PalmDown(bool left) => Quaternion.Euler(12f, left ? 8f : -8f, left ? -PalmRoll : PalmRoll);
        static Quaternion Fist(bool left) => Quaternion.Euler(-10f, left ? 12f : -12f, 0f);

        static Bounds Bounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(t.position, Vector3.one * 0.1f);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------------ pilotage image par image

        Vector3 World(Vector3 body) => m_BodyPos + m_BodyRot * body;
        Vector3 Dir(Vector3 body) => m_BodyRot * body;

        void Update() => Apply(Time.deltaTime);

        void Apply(float dt)
        {
            if (m_L == null) return;
            float time = Time.time;
            foreach (var a in new[] { m_L, m_R })
            {
                // Tremblement de rage : petites secousses rapides, plus fortes quand la colère monte.
                float s = a.Left ? 10f : 20f;
                var shake = new Vector3(Mathf.PerlinNoise(time * 9f, s) - 0.5f, Mathf.PerlinNoise(s, time * 9f) - 0.5f, Mathf.PerlinNoise(time * 9f + s, s)) * (0.012f * Tremble);
                var pos = World(a.Pos + shake);
                var rot = m_BodyRot * a.Rot * Quaternion.Euler(shake * (150f * Tremble));
                a.Ctrl.SetPositionAndRotation(pos, rot);

                var palm = a.Skin != null ? a.Skin.bounds.center : pos;
                if (dt > 0f) a.Velocity = Vector3.Lerp(a.Velocity, (palm - a.World) / dt, 0.6f);
                a.World = palm;
            }
        }

        void LateUpdate()
        {
            if (m_L == null) return;
            float k = 1f - Mathf.Exp(-Time.deltaTime * 14f);
            foreach (var a in new[] { m_L, m_R })
            {
                if (a.Hand.isHolding) continue;
                a.Trigger = Mathf.Lerp(a.Trigger, a.TriggerT, k);
                a.Grip = Mathf.Lerp(a.Grip, a.GripT, k);
                a.Hand.ApplyPose(a.Trigger, a.Grip);
            }
        }

        static void Fingers(Arm a, float trigger, float grip) { a.TriggerT = trigger; a.GripT = grip; }

        // ------------------------------------------------------------------ gestes de base

        static float Smooth(float t) => t * t * (3f - 2f * t);
        static float In(float t) => t * t * t;                       // accélère : un coup
        static float Out(float t) => 1f - (1f - t) * (1f - t);

        IEnumerator Move(Arm a, Vector3 pos, Quaternion rot, float dur, Func<float, float> ease = null)
        {
            ease = ease ?? Smooth;
            var p0 = a.Pos; var r0 = a.Rot;
            for (float t = 0f; t < 1f;)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / Mathf.Max(dur, 1e-3f));
                float e = ease(t);
                a.Pos = Vector3.LerpUnclamped(p0, pos, e);
                a.Rot = Quaternion.SlerpUnclamped(r0, rot, e);
                yield return null;
            }
        }

        IEnumerator Both(IEnumerator a, IEnumerator b)
        {
            var other = StartCoroutine(b);
            yield return a;
            yield return other;
        }

        static IEnumerator Wait(float s) { for (float t = 0f; t < s; t += Time.deltaTime) yield return null; }

        /// <summary>Coup de poing (marteau) sur le comptoir : les verres sautent.</summary>
        IEnumerator Slam(Arm a, float lateral, float strength)
        {
            float side = a.Left ? -1f : 1f;
            Fingers(a, 1f, 1f);
            yield return Move(a, new Vector3(side * lateral, m_Top + 0.32f, m_Edge + 0.08f), Fist(a.Left), 0.28f, Out);
            Effort?.Invoke(0.6f);
            yield return Move(a, new Vector3(side * lateral, m_Top + 0.045f, m_Edge + 0.1f), Fist(a.Left), 0.08f, In);
            var p = World(new Vector3(side * lateral, m_Top, m_Edge + 0.1f));
            if (!SfxBank.Play(SfxBank.MarbreCoup, p, strength)) SfxBank.Play(SfxBank.ImpactLourd, p, strength);
            TrailerDirector.Haptic(a.Left, strength, 0.12f);
            Fragile.Mice.Mouse.ScareAll(p, 6f);
            foreach (var c in Physics.OverlapSphere(p, 0.7f, ~0, QueryTriggerInteraction.Ignore))
            {
                var rb = c.attachedRigidbody;
                if (rb == null || rb.isKinematic || IsHeld(rb)) continue;
                float fall = 1f - Mathf.Clamp01(Vector3.Distance(rb.position, p) / 0.7f);
                rb.AddForce((Vector3.up * UnityEngine.Random.Range(1.2f, 2.2f) + UnityEngine.Random.insideUnitSphere * 0.4f) * (fall * strength), ForceMode.VelocityChange);
                rb.AddTorque(UnityEngine.Random.insideUnitSphere * 6f * fall, ForceMode.VelocityChange);
            }
            yield return Move(a, new Vector3(side * lateral, m_Top + 0.12f, m_Edge + 0.1f), Fist(a.Left), 0.12f, Out);
        }

        static bool IsHeld(Rigidbody rb) => rb.TryGetComponent<XRBaseInteractable>(out var i) && i.isSelected;

        /// <summary>Approche la main d'un objet et le saisit avec XRI (la main se referme, l'objet est vraiment tenu).</summary>
        IEnumerator Grab(Arm a, XRBaseInteractable target, GripSurface surface, Quaternion rot, float dur)
        {
            Fingers(a, 0f, 0f);
            var grip = surface != null ? surface.AxisPoint(surface.gripCenterZ) : target.transform.position;
            // Décalage paume / manette mesuré sur la main actuelle.
            var palmInCtrl = Quaternion.Inverse(a.Ctrl.rotation) * (a.World - a.Ctrl.position);
            var ctrlWorld = grip - m_BodyRot * rot * palmInCtrl;
            var body = Quaternion.Inverse(m_BodyRot) * (ctrlWorld - m_BodyPos);
            yield return Move(a, body + Vector3.up * 0.12f, rot, dur * 0.6f);
            yield return Move(a, body, rot, dur * 0.4f, Out);
            if (a.Interactor == null) yield break;
            a.Interactor.StartManualInteraction((IXRSelectInteractable)target);
            if (a.Interactor.IsSelecting((IXRSelectInteractable)target)) a.Held = target;
            yield return Wait(0.12f);
        }

        void Release(Arm a)
        {
            if (a.Held == null || a.Interactor == null) return;
            a.Interactor.EndManualInteraction();
            a.Held = null;
            Fingers(a, 0.1f, 0.1f);
        }

        // ------------------------------------------------------------------ la crise

        /// <summary>Pendant le match : mains posées sur le comptoir, qui se serrent quand ça tourne mal.</summary>
        public void Clench(bool on)
        {
            if (m_L == null) return;
            foreach (var a in new[] { m_L, m_R }) Fingers(a, on ? 1f : 0.2f, on ? 1f : 0.3f);
        }

        /// <summary>La crise, jouée en entier : poings sur le comptoir, verres jetés sur la télé, debout, balayage, batte.</summary>
        public IEnumerator Rampage()
        {
            if (m_L == null) { FinalImpact?.Invoke(); yield break; }

            // 1) Les poings se serrent, ça tremble.
            Clench(true);
            for (float t = 0f; t < 1.6f; t += Time.deltaTime) { Tremble = Mathf.Lerp(0.2f, 1f, t / 1.6f); yield return null; }

            // 2) Deux coups de poing sur le comptoir, puis les deux ensemble.
            if (m_HasCounter)
            {
                StartCoroutine(OutburstSoon(0.3f));
                yield return Slam(m_R, 0.17f, 0.8f);
                yield return Slam(m_L, 0.17f, 0.9f);
                yield return Both(Slam(m_R, 0.15f, 1f), Slam(m_L, 0.15f, 1f));
                yield return Wait(0.25f);
            }
            else Outburst?.Invoke();

            // 3) Ce qui traîne devant lui part dans la télé.
            var thrown = 0;
            foreach (var item in Throwables())
            {
                if (thrown >= 2) break;
                yield return Throw(m_R, item.Item1, item.Item2);
                thrown++;
            }

            // 4) Il se lève.
            yield return StandUp(0.15f, 0.12f, 0.7f);

            // 5) Revers de la main gauche : tout ce qui est sur le comptoir vole.
            if (m_HasCounter) yield return Sweep(m_L);


            // 6) À mains nues jusqu'au bout : bouteilles fracassées, coups de poing, coup final.
            yield return BareHands();
        }

        IEnumerator OutburstSoon(float delay)
        {
            yield return Wait(delay);   // au moment où le poing s'abat
            Outburst?.Invoke();
        }

        List<(XRBaseInteractable, GripSurface)> Throwables()
        {
            var list = new List<(XRBaseInteractable, GripSurface, float)>();
            foreach (var g in FindObjectsByType<HandGripGrabInteractable>(FindObjectsInactive.Exclude))
            {
                if (g.gameObject.scene != m_Origin.gameObject.scene || g.GetComponent<Breaker>() != null) continue;
                if (m_Used.Contains(g)) continue;
                if (g.GetComponent<Breakable>() == null || g.GetComponentInParent<Fragile.Weapons.Firearm>() != null || g.GetComponent<Fragile.Darts.Dart>() != null) continue;
                var local = Quaternion.Inverse(m_BodyRot) * (g.transform.position - m_BodyPos);
                if (local.z < 0.1f || local.x < -0.25f || new Vector2(local.x, local.z).magnitude > 0.85f) continue;
                if (local.y < m_Top - 0.05f || local.y > m_Top + 0.5f) continue;
                list.Add((g, g.GetComponent<GripSurface>(), new Vector2(local.x - 0.2f, local.z - m_Edge).magnitude));
            }
            list.Sort((p, q) => p.Item3.CompareTo(q.Item3));
            var res = new List<(XRBaseInteractable, GripSurface)>();
            foreach (var e in list) res.Add((e.Item1, e.Item2));
            return res;
        }

        IEnumerator Throw(Arm a, XRBaseInteractable item, GripSurface surface)
        {
            Clench(false);
            Fingers(m_L, 1f, 1f);
            m_Used.Add(item);
            yield return Grab(a, item, surface, Quaternion.Euler(0f, -10f, 0f), 0.45f);
            if (a.Held == null) yield break;
            // Armé derrière l'épaule...
            yield return Move(a, new Vector3(0.28f, 0.42f, -0.12f), Quaternion.Euler(-60f, -15f, 0f), 0.4f);
            yield return Wait(0.08f);
            Effort?.Invoke(1f);
            // ... et lancé de toutes ses forces vers l'écran.
            yield return Move(a, new Vector3(0.12f, 0.35f, 0.5f), Quaternion.Euler(20f, -5f, 0f), 0.11f, In);
            var rb = item.GetComponent<Rigidbody>();
            Release(a);
            var target = m_A.LookAt;
            if (rb != null)
            {
                float flight = Vector3.Distance(rb.position, target) / 12f;
                var v = (target - rb.position) / flight - 0.5f * Physics.gravity * flight;
                for (int i = 0; i < 3; i++) { yield return new WaitForFixedUpdate(); if (rb != null) { rb.isKinematic = false; rb.linearVelocity = v; rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 8f; } }
                yield return Wait(flight + 0.05f);
                // Raté (l'objet est encore entier) : la télé encaisse quand même, l'objet éclate.
                var tv = m_A.Screen != null ? m_A.Screen.GetComponentInParent<TvBreakable>() : null;
                if (item != null && item.gameObject.activeInHierarchy)
                {
                    if (tv != null) tv.Hit(target, (target - m_A.Eye).normalized, 1f);
                    var br = item.GetComponent<Breakable>();
                    if (br != null) br.Shot(item.transform.position, -v.normalized, v.normalized, 1f, 1f);
                }
                TvHit?.Invoke();
            }
            yield return Move(a, Rest(false) + Vector3.up * 0.1f, Fist(false), 0.35f);
            Fingers(a, 1f, 1f);
        }

        IEnumerator StandUp(float up, float back, float dur)
        {
            var f = m_BodyRot * Vector3.forward;
            var total = Vector3.up * up - f * back;
            var done = Vector3.zero;
            for (float t = 0f; t < 1f;)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / dur);
                var step = total * Smooth(t) - done;
                done += step;
                m_Origin.transform.position += step;
                m_BodyPos += step;
                m_A.Eye += step;
                // Les mains restent où elles sont dans le monde : on compense dans le repère du corps.
                var comp = Quaternion.Inverse(m_BodyRot) * step;
                m_L.Pos -= comp; m_R.Pos -= comp;
                yield return null;
            }
            m_Top -= up; m_Edge += back;
            yield return Both(Move(m_L, Rest(true) + Vector3.up * 0.15f, Fist(true), 0.3f), Move(m_R, Rest(false) + Vector3.up * 0.15f, Fist(false), 0.3f));
        }

        IEnumerator Sweep(Arm a)
        {
            Fingers(a, 0.1f, 0.15f);
            // Main ouverte loin à droite sur le comptoir, puis grand revers vers la gauche.
            var rot = Quaternion.Euler(10f, -70f, 0f);
            yield return Move(a, new Vector3(0.45f, m_Top + 0.22f, m_Edge + 0.25f), rot, 0.35f);
            yield return Move(a, new Vector3(0.45f, m_Top + 0.07f, m_Edge + 0.22f), rot, 0.12f);
            Effort?.Invoke(0.9f);
            var pushed = new HashSet<Rigidbody>();
            var start = a.Pos;
            var end = new Vector3(-0.75f, m_Top + 0.07f, m_Edge + 0.3f);
            for (float t = 0f; t < 1f;)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / 0.38f);
                a.Pos = Vector3.Lerp(start, end, Smooth(t));
                a.Rot = Quaternion.Slerp(rot, Quaternion.Euler(10f, -110f, 0f), t);
                foreach (var c in Physics.OverlapSphere(a.World, 0.16f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var rb = c.attachedRigidbody;
                    if (rb == null || rb.isKinematic || IsHeld(rb) || !pushed.Add(rb)) continue;
                    rb.linearVelocity = a.Velocity * 1.4f + Vector3.up * 1.2f;
                    rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 10f;
                }
                yield return null;
            }
            yield return Move(a, Rest(true) + Vector3.up * 0.2f, Fist(true), 0.3f, Out);
            Fingers(a, 1f, 1f);
        }

        // ------------------------------------------------------------------ à mains nues

        /// <summary>La fin de la crise, sans aucune arme : bouteilles fracassées, coups de poing, double coup final.</summary>
        IEnumerator BareHands()
        {
            if (!m_HasCounter) { yield return Slam(m_R, 0.1f, 1f); FinalImpact?.Invoke(); yield break; }

            // Ce qui reste à portée : saisi et fracassé sur le marbre, main droite puis main gauche.
            int smashed = 0;
            foreach (var item in Throwables())
            {
                if (smashed >= 2) break;
                yield return SmashOnCounter(smashed % 2 == 0 ? m_R : m_L, item.Item1, item.Item2);
                smashed++;
            }

            // Coups de poing dans tout ce qui tient encore debout devant lui.
            int punches = 0;
            foreach (var target in PunchTargets())
            {
                if (punches >= 4) break;
                yield return Punch(punches % 2 == 0 ? m_R : m_L, target);
                punches++;
            }
            if (punches == 0) { yield return Slam(m_R, 0.12f, 1f); yield return Slam(m_L, 0.12f, 1f); }

            // Le dernier : poings joints levés au-dessus de la tête, abattus sur le comptoir. Coupure à l'impact.
            Fingers(m_R, 1f, 1f); Fingers(m_L, 1f, 1f);
            yield return Both(Move(m_R, new Vector3(0.07f, m_Top + 0.95f, m_Edge - 0.05f), Quaternion.Euler(-75f, 0f, 25f), 0.5f),
                              Move(m_L, new Vector3(-0.07f, m_Top + 0.95f, m_Edge - 0.05f), Quaternion.Euler(-75f, 0f, -25f), 0.5f));
            yield return Wait(0.2f);
            Effort?.Invoke(1f);
            yield return Both(Move(m_R, new Vector3(0.05f, m_Top + 0.06f, m_Edge + 0.12f), Fist(false), 0.13f, In),
                              Move(m_L, new Vector3(-0.05f, m_Top + 0.06f, m_Edge + 0.12f), Fist(true), 0.13f, In));
            FinalImpact?.Invoke();
        }

        /// <summary>Saisit une bouteille (ou un verre), la lève et l'abat : lâchée juste au-dessus du marbre, elle éclate pour de vrai.</summary>
        IEnumerator SmashOnCounter(Arm a, XRBaseInteractable item, GripSurface surface)
        {
            m_Used.Add(item);
            var other = a == m_R ? m_L : m_R;
            Fingers(other, 1f, 1f);
            yield return Grab(a, item, surface, Quaternion.Euler(0f, a.Left ? 10f : -10f, 0f), 0.4f);
            if (a.Held == null) yield break;
            float side = a.Left ? -1f : 1f;
            var b = Bounds(item.transform);
            float clearance = b.size.y + 0.04f;   // la main s'arrête assez haut pour que l'objet ne touche pas avant d'être lâché

            yield return Move(a, new Vector3(side * 0.24f, m_Top + clearance + 0.55f, m_Edge - 0.02f), Quaternion.Euler(-70f, 0f, 0f), 0.35f);
            yield return Wait(0.08f);
            Effort?.Invoke(0.9f);
            yield return Move(a, new Vector3(side * 0.2f, m_Top + clearance, m_Edge + 0.16f), Quaternion.Euler(25f, 0f, 0f), 0.11f, In);

            var rb = item.GetComponent<Rigidbody>();
            Release(a);
            for (int i = 0; i < 2; i++)
            {
                yield return new WaitForFixedUpdate();
                if (rb != null) { rb.isKinematic = false; rb.linearVelocity = Dir(new Vector3(0f, -7f, 0.6f)); }
            }
            TrailerDirector.Haptic(a.Left, 0.9f, 0.12f);
            var p = World(new Vector3(side * 0.2f, m_Top, m_Edge + 0.16f));
            yield return Wait(0.12f);
            var br = item != null ? item.GetComponent<Breakable>() : null;
            if (br != null && br.gameObject.activeInHierarchy) br.Shot(p, Vector3.up, Vector3.down, 1f, 1f);   // sécurité
            Fragile.Mice.Mouse.ScareAll(p, 6f);
            yield return Move(a, Rest(a.Left) + Vector3.up * 0.12f, Fist(a.Left), 0.3f, Out);
            Fingers(a, 1f, 1f);
        }

        /// <summary>Objets cassables encore debout à portée de poing (petits : verres, bouteilles, décor de comptoir).</summary>
        List<Breakable> PunchTargets()
        {
            var list = new List<(Breakable, float)>();
            foreach (var br in FindObjectsByType<Breakable>(FindObjectsInactive.Exclude))
            {
                if (br.gameObject.scene != m_Origin.gameObject.scene || br.GetComponent<Breaker>() != null) continue;
                var bb = Bounds(br.transform);
                if (bb.size.magnitude > 0.8f) continue;
                var local = Quaternion.Inverse(m_BodyRot) * (bb.center - m_BodyPos);
                if (local.z < 0.15f || Mathf.Abs(local.x) > 0.7f || new Vector2(local.x, local.z).magnitude > 0.85f) continue;
                if (local.y < m_Top - 0.05f || local.y > m_Top + 0.6f) continue;
                list.Add((br, new Vector2(local.x, local.z - m_Edge).magnitude));
            }
            list.Sort((p, q) => p.Item2.CompareTo(q.Item2));
            var res = new List<Breakable>();
            foreach (var e in list) res.Add(e.Item1);
            return res;
        }

        /// <summary>Coup de poing armé puis lancé dans la cible : elle casse (physique ou, sinon, coup garanti).</summary>
        IEnumerator Punch(Arm a, Breakable target)
        {
            if (target == null || !target.gameObject.activeInHierarchy) yield break;
            Fingers(a, 1f, 1f);
            var c = Bounds(target.transform).center;
            var local = Quaternion.Inverse(m_BodyRot) * (c - m_BodyPos);
            var dir = new Vector3(local.x * 0.4f, -0.1f, 1f).normalized;
            yield return Move(a, local - dir * 0.35f + Vector3.up * 0.05f, Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, a.Left ? -90f : 90f), 0.22f, Out);
            Effort?.Invoke(0.7f);
            yield return Move(a, local - dir * 0.04f, Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, a.Left ? -90f : 90f), 0.09f, In);
            if (target != null && target.gameObject.activeInHierarchy)
            {
                var w = Dir(dir);
                target.Shot(c - w * 0.03f, -w, w, 1.2f, 0.85f);
                if (target.TryGetComponent<Rigidbody>(out var rb) && !rb.isKinematic) rb.linearVelocity = w * 4f + Vector3.up;
            }
            if (!SfxBank.Play(SfxBank.ImpactLeger, c, 0.8f)) SfxBank.Play(SfxBank.ObjetTombe, c, 0.6f);
            TrailerDirector.Haptic(a.Left, 0.7f, 0.08f);
            yield return Move(a, Rest(a.Left) + Vector3.up * 0.15f, Fist(a.Left), 0.2f, Out);
        }

        IEnumerator EnsureBroken(Breakable b, Vector3 point, Vector3 dir)
        {
            yield return Wait(0.06f);
            if (b != null && b.gameObject.activeInHierarchy)
                b.Shot(point, -dir, dir, 1.5f, 0.9f);
        }
    }
}
