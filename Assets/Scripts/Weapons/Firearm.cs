using System.Collections;
using System.Collections.Generic;
using Fragile.Audio;
using Fragile.Breakables;
using Fragile.Grab;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fragile.Weapons
{
    /// <summary>
    /// Arme à feu (pistolet, carabine à verrou, fusil à pompe), avec un vrai cycle de munitions manipulé à deux mains :
    /// - fusil à pompe : cartouches glissées une à une dans la trappe sous la carcasse (magasin tubulaire), la main
    ///   d'appui tient la pompe : la ramener en arrière éjecte, la renvoyer en avant charge la chambre ;
    /// - carabine : levier du verrou relevé puis tiré (éjection), cartouches introduites par la fenêtre verrou ouvert,
    ///   verrou repoussé puis rabattu (chargement) ; l'autre main peut aussi tenir le garde-main pour viser ;
    /// - pistolet : chargeur éjecté au bouton principal (A / X) de la main qui le tient, chargeur neuf glissé dans la
    ///   poignée, culasse tirée par l'autre main ; elle reste ouverte après la dernière cartouche.
    /// Tir par lancer de rayons : casse les bouteilles, abîme puis casse le décor (<see cref="Breakable.Shot"/>),
    /// fissure vitres et télés, marque le marbre, pousse les objets, laisse des impacts aux murs. Flamme, traçantes,
    /// recul, vibrations, douilles. Construit par Fragile > Placer les armes.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class Firearm : MonoBehaviour
    {
        public enum Kind { Pistol, Rifle, Shotgun }
        enum Chamber { Empty, Live, Spent }

        [SerializeField] Kind m_Kind = Kind.Pistol;
        [Tooltip("Posée sur un râtelier : immobile jusqu'à ce qu'on la prenne (elle devient libre au premier lâcher).")]
        [SerializeField] bool m_StartRacked;

        [Header("Tir")]
        [Tooltip("Bouche du canon : le tir part selon son axe Z.")]
        [SerializeField] Transform m_Muzzle;
        [SerializeField] int m_Projectiles = 1;
        [Tooltip("Dispersion (degrés, demi-angle).")]
        [SerializeField] float m_Spread = 0.4f;
        [SerializeField] float m_Range = 60f;
        [Tooltip("Dégâts infligés au décor par projectile, en « coups de batte francs ».")]
        [SerializeField] float m_Damage = 0.6f;
        [Tooltip("Violence de l'impact (0..1) : taille de la casse et des effets.")]
        [SerializeField, Range(0f, 1f)] float m_Energy = 0.7f;
        [Tooltip("Poussée sur les objets libres (N·s par projectile).")]
        [SerializeField] float m_Push = 2f;
        [Tooltip("Temps minimal entre deux tirs (s).")]
        [SerializeField] float m_Cooldown = 0.18f;

        [Header("Recul")]
        [SerializeField] float m_KickBack = 0.03f;
        [SerializeField] float m_KickUp = 8f;
        [SerializeField] float m_KickReturn = 14f;
        [SerializeField, Range(0f, 1f)] float m_Haptic = 0.8f;
        [Tooltip("Pièce qui recule avec l'arme (la main y est accrochée pendant la prise).")]
        [SerializeField] Transform m_KickRoot;

        [Header("Munitions")]
        [Tooltip("Magasin / chargeur inépuisable : il faut quand même armer (pompe, verrou, culasse), mais plus recharger.")]
        [SerializeField] bool m_InfiniteAmmo = true;
        [Tooltip("Pistolet : le bouton A/X de la main qui le tient éjecte le chargeur. Désactivé : plus de plateau de chargeurs dans le Bar.")]
        [SerializeField] bool m_MagazineEject = false;
        [Tooltip("Magasin tubulaire / magasin interne ; ignoré pour le pistolet (c'est le chargeur qui compte).")]
        [SerializeField] int m_Capacity = 6;
        [Tooltip("Cartouches dans le magasin (ou le chargeur) au départ, en plus de celle de la chambre.")]
        [SerializeField] int m_StartLoaded = 5;
        [Tooltip("Munition qu'on introduit, et qui ressort si on éjecte une cartouche intacte (ou un chargeur).")]
        [SerializeField] AmmoItem m_AmmoPrefab;
        [Tooltip("Pistolet : chargeur visible dans la poignée.")]
        [SerializeField] GameObject m_MagazineVisual;

        [Header("Mécanisme")]
        [Tooltip("Pièce mobile : culasse, pompe, ou pivot du verrou.")]
        [SerializeField] Transform m_Action;
        [Tooltip("Déplacement local de la pièce, mécanisme ouvert.")]
        [SerializeField] Vector3 m_ActionTravel = new Vector3(0f, 0f, -0.025f);
        [Tooltip("Carabine : rotation du verrou pour le déverrouiller (axe local, degrés).")]
        [SerializeField] Vector3 m_BoltAxis = Vector3.right;
        [SerializeField] float m_BoltLift;
        [Tooltip("Hauteur dont on relève la main pour déverrouiller (m).")]
        [SerializeField] float m_LiftDistance = 0.035f;
        [SerializeField] GunHandle[] m_Handles;
        [SerializeField] Transform m_EjectPort;
        [SerializeField] Mesh m_ShellMesh;
        [SerializeField] Material[] m_ShellMaterials;
        [Tooltip("Douille cylindrique (diamètre, longueur) ; (0, 0) = maillage à sa taille réelle.")]
        [SerializeField] Vector2 m_ShellSize = new Vector2(0.01f, 0.02f);

        [Header("Effets")]
        [SerializeField] Material m_FlashMaterial;
        [SerializeField] float m_FlashSize = 0.12f;
        [SerializeField] Material m_TracerMaterial;
        [SerializeField] Material m_HoleMaterial;
        [SerializeField] GameObject m_DustFx;

        XRGrabInteractable m_Grab;
        Collider[] m_OwnColliders;
        float m_NextShot;
        Vector3 m_KickPos; Quaternion m_KickRot = Quaternion.identity;
        Vector3 m_KickHomePos; Quaternion m_KickHomeRot;
        Transform m_Flash;
        readonly List<LineRenderer> m_Tracers = new List<LineRenderer>();

        // État des munitions et du mécanisme.
        Chamber m_Chamber = Chamber.Live;
        int m_Loaded;
        bool m_HasMag = true, m_WasOpen, m_SlideLocked, m_Blowback;
        float m_Open, m_Lift;
        bool m_BoltLifted;
        Vector3 m_ActionHome; Quaternion m_ActionHomeRot;
        Vector3 m_TravelRoot; float m_TravelLen;   // course du mécanisme dans le repère de l'arme
        GunHandle m_AimHandle, m_ActionHandle;
        Vector3 m_AimAnchor, m_ActionGrabLocal;
        float m_ActionGrabOpen, m_ActionGrabLift, m_LastDryFire;

        static readonly Queue<GameObject> s_Holes = new Queue<GameObject>();
        static readonly Queue<GameObject> s_Shells = new Queue<GameObject>();
        static Mesh s_Quad;
        static InputAction s_EjectLeft, s_EjectRight;
        const int MaxHoles = 60, MaxShells = 16;

        public XRGrabInteractable grab => m_Grab != null ? m_Grab : m_Grab = GetComponent<XRGrabInteractable>();
        public Kind kind => m_Kind;

        void Awake()
        {
            m_Grab = GetComponent<XRGrabInteractable>();
            m_OwnColliders = GetComponentsInChildren<Collider>(true);
            if (m_KickRoot != null) { m_KickHomePos = m_KickRoot.localPosition; m_KickHomeRot = m_KickRoot.localRotation; }
            if (m_Action != null)
            {
                m_ActionHome = m_Action.localPosition; m_ActionHomeRot = m_Action.localRotation;
                var travel = transform.InverseTransformVector(m_Action.parent.TransformVector(m_ActionTravel));
                m_TravelLen = travel.magnitude; m_TravelRoot = m_TravelLen > 0f ? travel / m_TravelLen : Vector3.back;
            }
            m_Loaded = m_StartLoaded;
            if (!TryGetComponent<ReloadGuide>(out _)) gameObject.AddComponent<ReloadGuide>();
            BuildFlash();
            BreakFx.Prewarm(m_DustFx, 3);
            // Sons fabriqués d'avance : pas d'à-coup au premier tir.
            SfxBank.Preload(ShotFamily);
            if (m_Kind == Kind.Shotgun) SfxBank.Preload(SfxBank.PompeArriere, SfxBank.PompeAvant);
            if (m_Kind == Kind.Rifle) SfxBank.Preload(SfxBank.VerrouReleve, SfxBank.VerrouTire, SfxBank.VerrouPousse, SfxBank.VerrouRabattu); _ = ShotClip; _ = Sfx.BulletImpact; _ = Sfx.ShellDrop; _ = Sfx.ActionOpen; _ = Sfx.ActionClose; _ = Sfx.DryFire; _ = Sfx.LoadRound;
            if (m_Kind == Kind.Pistol && s_EjectLeft == null)
            {
                s_EjectLeft = new InputAction("Éjecter chargeur G", InputActionType.Button, "<XRController>{LeftHand}/primaryButton");
                s_EjectRight = new InputAction("Éjecter chargeur D", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
                s_EjectLeft.Enable(); s_EjectRight.Enable();
            }
        }

        void Start()
        {
            // La main suit le recul : elle est accrochée à la pièce qui recule (créée dans les visuels de l'arme).
            if (m_KickRoot != null && TryGetComponent<GripSurface>(out var grip)) grip.handParent = m_KickRoot;
        }

        void OnEnable()
        {
            grab.activated.AddListener(OnActivated);
            grab.selectExited.AddListener(OnReleased);
        }

        void OnDisable()
        {
            grab.activated.RemoveListener(OnActivated);
            grab.selectExited.RemoveListener(OnReleased);
            if (m_Flash != null) m_Flash.gameObject.SetActive(false);
        }

        void OnActivated(ActivateEventArgs args) => Fire();

        void OnReleased(SelectExitEventArgs args)
        {
            if (m_Grab.isSelected) return;
            // Plus de main sur la poignée : l'autre main lâche aussi la pompe / le garde-main.
            if (m_Handles != null) foreach (var h in m_Handles) if (h != null) h.ForceRelease();
            // XRI vient de rendre au Rigidbody son état d'avant la prise (cinématique, sur le râtelier) : on libère l'arme.
            if (!m_StartRacked) return;
            m_StartRacked = false;
            if (TryGetComponent<Rigidbody>(out var rb)) rb.isKinematic = false;
        }

        string ShotFamily => m_Kind switch { Kind.Rifle => SfxBank.TirRifle, Kind.Shotgun => SfxBank.TirShotgun, _ => SfxBank.TirRevolver };
        /// <summary>Détonation enregistrée (variante au hasard), sinon la détonation générée.</summary>
        AudioClip ShotClip
        {
            get
            {
                var clip = SfxBank.Pick(ShotFamily);
                return clip != null ? clip : m_Kind switch { Kind.Rifle => Sfx.RifleShot, Kind.Shotgun => Sfx.ShotgunShot, _ => Sfx.PistolShot };
            }
        }

        bool Ready => m_Chamber == Chamber.Live && m_Open < 0.05f && !m_Blowback && (m_Kind != Kind.Rifle || m_Lift < 0.05f);

        /// <summary>Tire (si une cartouche est chambrée et le mécanisme fermé), sinon « clic ».</summary>
        public void Fire()
        {
            if (Time.time < m_NextShot || m_Muzzle == null) return;
            if (!Ready)
            {
                if (Time.time - m_LastDryFire > 0.25f) { Sfx.PlayAt(Sfx.DryFire, transform.position, 0.6f); m_LastDryFire = Time.time; Haptics(0.15f, 0.02f); }
                return;
            }
            m_NextShot = Time.time + m_Cooldown;
            m_Chamber = Chamber.Spent;

            var origin = m_Muzzle.position;
            var forward = m_Muzzle.forward;
            var hits = new Dictionary<Component, (Vector3 point, Vector3 normal, Vector3 dir, int count)>();
            for (int i = 0; i < m_Projectiles; i++)
            {
                var dir = Spread(forward, m_Spread);
                var end = origin + dir * m_Range;
                if (Cast(origin, dir, out var hit))
                {
                    end = hit.point;
                    Impact(hit, dir, hits);
                }
                Tracer(i, origin, end);
            }
            // Tous les plombs qui touchent le même objet : un seul coup, plus fort (un seul son, une seule marque).
            foreach (var kv in hits)
            {
                var (point, normal, dir, count) = kv.Value;
                float energy = Mathf.Clamp01(m_Energy * Mathf.Lerp(1f, 1.6f, (count - 1) / 7f));
                switch (kv.Key)
                {
                    case IHittable h: h.Hit(point, dir, energy, m_Push * count); break;
                    case Breakable b: b.Shot(point, normal, dir, m_Damage * count, energy); break;
                    case GlassPane g: g.Hit(point, dir, Mathf.Max(energy, 0.6f)); break;
                    case Fragile.TV.TvBreakable tv: tv.Hit(point, dir, energy); break;
                }
            }

            Sfx.PlayAt(ShotClip, origin, 1f, Random.Range(0.96f, 1.04f));
            Fragile.Mice.Mouse.ScareAll(origin, 8f);   // les souris détalent au bruit
            StartCoroutine(Flash());
            Kick();
            Haptics(m_Haptic, m_Kind == Kind.Pistol ? 0.08f : 0.16f);
            // Pistolet semi-automatique : la culasse recule toute seule (éjection), et reste ouverte si c'était la dernière.
            if (m_Kind == Kind.Pistol) StartCoroutine(Blowback());
        }

        static Vector3 Spread(Vector3 forward, float halfAngle)
        {
            if (halfAngle <= 0f) return forward;
            var r = Random.insideUnitCircle * Mathf.Tan(halfAngle * Mathf.Deg2Rad);
            var rot = Quaternion.LookRotation(forward);
            return (forward + rot * new Vector3(r.x, r.y, 0f)).normalized;
        }

        bool Cast(Vector3 origin, Vector3 dir, out RaycastHit best)
        {
            best = default; float bestDist = float.MaxValue; bool found = false;
            foreach (var h in Physics.RaycastAll(origin, dir, m_Range, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.distance >= bestDist || System.Array.IndexOf(m_OwnColliders, h.collider) >= 0) continue;
                // Pas sur les mains du joueur ni sur ce qu'il tient dans l'autre main.
                var grab = h.collider.GetComponentInParent<XRBaseInteractable>();
                if (grab != null && grab.isSelected && grab.gameObject != gameObject) continue;
                if (h.collider.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() != null) continue;
                best = h; bestDist = h.distance; found = true;
            }
            return found;
        }

        void Impact(RaycastHit hit, Vector3 dir, Dictionary<Component, (Vector3, Vector3, Vector3, int)> hits)
        {
            var col = hit.collider;
            Component target = col.GetComponentInParent<IHittable>() as Component;
            if (target == null) target = col.GetComponentInParent<Breakable>();
            if (target == null) target = col.GetComponent<GlassPane>() ?? col.GetComponentInParent<GlassPane>();
            if (target == null) target = col.GetComponentInParent<Fragile.TV.TvBreakable>();
            if (target != null)
            {
                if (hits.TryGetValue(target, out var h)) hits[target] = (h.Item1, h.Item2, h.Item3, h.Item4 + 1);
                else hits[target] = (hit.point, hit.normal, dir, 1);
            }

            var body = hit.rigidbody;
            if (body != null && !body.isKinematic) body.AddForceAtPosition(dir * m_Push, hit.point, ForceMode.Impulse);

            var marks = col.GetComponentInParent<ImpactMarks>();
            if (marks != null) marks.Mark(hit.point, hit.normal, m_Energy * 0.6f);
            else if (target == null && (body == null || body.isKinematic)) Hole(hit);

            if (target == null)
            {
                var fx = BreakFx.Get(m_DustFx);
                if (fx != null)
                    fx.Play(new BreakFx.Settings { point = hit.point, direction = hit.normal, energy = 0.05f,
                        chip1 = new Color(0.55f, 0.5f, 0.45f), chip2 = new Color(0.3f, 0.26f, 0.22f), liquid = false });
                if (m_Projectiles == 1 || Random.value < 0.25f) Sfx.PlayAt(Sfx.BulletImpact, hit.point, 0.6f, Random.Range(0.8f, 1.2f));
            }
        }

        // ------------------------------------------------------------------ impacts sur les murs

        void Hole(RaycastHit hit)
        {
            if (m_HoleMaterial == null) return;
            if (s_Quad == null) s_Quad = QuadMesh();
            GameObject go;
            if (s_Holes.Count >= MaxHoles) { go = s_Holes.Dequeue(); if (go == null) go = NewHole(); }
            else go = NewHole();
            go.SetActive(true);
            // Suit ce qui bouge (portes, tiroirs) ; pas les murs (cubes étirés : l'impact y serait déformé).
            var ls = hit.collider.transform.lossyScale;
            bool uniform = Mathf.Abs(ls.x - ls.y) < 0.01f && Mathf.Abs(ls.y - ls.z) < 0.01f;
            go.transform.SetParent(uniform && !hit.collider.gameObject.isStatic ? hit.collider.transform : null, true);
            go.transform.SetPositionAndRotation(hit.point + hit.normal * 0.002f,
                Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
            float s = (m_Kind == Kind.Shotgun ? 0.035f : 0.05f) * Random.Range(0.8f, 1.2f);
            go.transform.localScale = Vector3.one * s / (go.transform.parent != null ? ls.x : 1f);
            go.GetComponent<MeshRenderer>().sharedMaterial = m_HoleMaterial;
            s_Holes.Enqueue(go);
        }

        static GameObject NewHole()
        {
            var go = new GameObject("Impact de balle");
            go.AddComponent<MeshFilter>().sharedMesh = s_Quad;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        static Mesh QuadMesh()
        {
            var m = new Mesh { name = "Impact" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ flamme, traçantes

        void BuildFlash()
        {
            if (m_FlashMaterial == null || m_Muzzle == null) return;
            if (s_Quad == null) s_Quad = QuadMesh();
            m_Flash = new GameObject("Flamme").transform;
            m_Flash.SetParent(m_Muzzle, false);
            // Trois quads croisés : vue de face (étoile) et de profil (langue de feu).
            for (int i = 0; i < 3; i++)
            {
                var q = new GameObject("Quad").transform;
                q.SetParent(m_Flash, false);
                q.localRotation = i == 0 ? Quaternion.identity : Quaternion.Euler(0f, 90f, i == 1 ? 0f : 90f);
                q.localPosition = i == 0 ? Vector3.zero : new Vector3(0f, 0f, 0.5f);
                q.localScale = i == 0 ? Vector3.one : new Vector3(1.6f, 1f, 1f);
                q.gameObject.AddComponent<MeshFilter>().sharedMesh = s_Quad;
                var r = q.gameObject.AddComponent<MeshRenderer>();
                r.sharedMaterial = m_FlashMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            m_Flash.gameObject.SetActive(false);
        }

        IEnumerator Flash()
        {
            if (m_Flash == null) yield break;
            m_Flash.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            m_Flash.localScale = Vector3.one * m_FlashSize * Random.Range(0.8f, 1.2f);
            m_Flash.gameObject.SetActive(true);
            yield return null;
            yield return null;
            m_Flash.gameObject.SetActive(false);
        }

        void Tracer(int index, Vector3 from, Vector3 to)
        {
            if (m_TracerMaterial == null) return;
            while (m_Tracers.Count <= index)
            {
                var lr = new GameObject("Traçante").AddComponent<LineRenderer>();
                lr.transform.SetParent(transform, false);
                lr.useWorldSpace = true; lr.positionCount = 2;
                lr.sharedMaterial = m_TracerMaterial;
                lr.widthMultiplier = m_Kind == Kind.Shotgun ? 0.004f : 0.006f;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.enabled = false;
                m_Tracers.Add(lr);
            }
            StartCoroutine(ShowTracer(m_Tracers[index], from, to));
        }

        IEnumerator ShowTracer(LineRenderer lr, Vector3 from, Vector3 to)
        {
            // Un trait lumineux bref, qui part de la bouche : on « voit » où part le coup.
            var d = to - from;
            float len = Mathf.Min(d.magnitude, 6f);
            for (float t = 0f; t < 0.05f; t += Time.deltaTime)
            {
                float k = t / 0.05f;
                var head = from + d * Mathf.Clamp01(k * 1.6f);
                lr.SetPosition(0, Vector3.MoveTowards(head, from, len));
                lr.SetPosition(1, head);
                lr.enabled = true;
                yield return null;
            }
            lr.enabled = false;
        }

        // ------------------------------------------------------------------ recul, vibrations

        void Kick()
        {
            m_KickPos += new Vector3(0f, 0f, -m_KickBack);
            m_KickRot = Quaternion.Euler(-m_KickUp * Random.Range(0.85f, 1.15f), Random.Range(-1.5f, 1.5f), 0f) * m_KickRot;
        }

        void LateUpdate()
        {
            if (m_KickRoot == null) return;
            float k = 1f - Mathf.Exp(-m_KickReturn * Time.deltaTime);
            m_KickPos = Vector3.Lerp(m_KickPos, Vector3.zero, k);
            m_KickRot = Quaternion.Slerp(m_KickRot, Quaternion.identity, k);
            // Recul autour de la poignée (origine de l'arme) : le canon se relève.
            m_KickRoot.localRotation = m_KickRot * m_KickHomeRot;
            m_KickRoot.localPosition = m_KickRot * m_KickHomePos + m_KickPos;
        }

        void Haptics(float amplitude, float duration)
        {
            if (!m_Grab.isSelected) return;
            foreach (var interactor in m_Grab.interactorsSelecting)
            {
                var player = interactor.transform.GetComponentInParent<HapticImpulsePlayer>();
                if (player != null) player.SendHapticImpulse(amplitude, duration);
            }
        }

        // ------------------------------------------------------------------ deuxième main

        /// <summary>Poignée d'appui (garde-main, pompe) tenue : l'arme vise vers elle (voir <see cref="TwoHandAim"/>).</summary>
        public bool TryGetAimHandle(out GunHandle handle, out Vector3 anchorLocal)
        {
            handle = m_AimHandle; anchorLocal = m_AimAnchor;
            return handle != null && handle.held && grab.isSelected;
        }

        public void OnHandleGrabbed(GunHandle h)
        {
            var local = transform.InverseTransformPoint(h.controllerPosition);
            if (h.aims) { m_AimHandle = h; m_AimAnchor = local; }
            if (h.role != GunHandle.Role.Support)
            {
                m_ActionHandle = h; m_ActionGrabLocal = local;
                m_ActionGrabOpen = m_Open; m_ActionGrabLift = m_Lift;
            }
            HapticsTo(h.interactor, 0.2f, 0.03f);
        }

        public void OnHandleReleased(GunHandle h)
        {
            if (m_AimHandle == h) m_AimHandle = null;
            if (m_ActionHandle != h) return;
            m_ActionHandle = null;
            // Culasse du pistolet lâchée : le ressort la renvoie (même si elle était restée ouverte).
            if (h.role == GunHandle.Role.Slide) m_SlideLocked = false;
        }

        void Update()
        {
            if (m_Action == null) return;
            if (m_ActionHandle != null && m_ActionHandle.held)
            {
                var d = transform.InverseTransformPoint(m_ActionHandle.controllerPosition) - m_ActionGrabLocal;
                float back = m_TravelLen > 0f ? Vector3.Dot(d, m_TravelRoot) / m_TravelLen : 0f;
                switch (m_ActionHandle.role)
                {
                    case GunHandle.Role.Pump:
                        SetOpen(m_ActionGrabOpen + back);
                        break;
                    case GunHandle.Role.Slide:
                        // On ne pousse pas une culasse : seul le recul compte, le ressort fait le reste.
                        SetOpen(Mathf.Max(m_SlideLocked ? 1f : 0f, m_ActionGrabOpen + back));
                        break;
                    case GunHandle.Role.Bolt:
                        // Verrou : d'abord relever le levier (verrou fermé), puis tirer / pousser (levier relevé), puis rabattre.
                        if (m_Open <= 0.02f)
                        {
                            m_Lift = Mathf.Clamp01(m_ActionGrabLift + d.y / m_LiftDistance);
                            if (m_Lift >= 0.98f) { m_Lift = 1f; SetOpen(m_ActionGrabOpen + back); }
                            else { m_ActionGrabOpen = 0f; m_ActionGrabLocal.z = transform.InverseTransformPoint(m_ActionHandle.controllerPosition).z; }
                        }
                        else
                        {
                            m_Lift = 1f;
                            SetOpen(m_ActionGrabOpen + back);
                            // Verrou revenu en avant : le levier peut se rabattre à partir de là.
                            if (m_Open <= 0.02f) { m_ActionGrabLift = 1f; m_ActionGrabLocal.y = transform.InverseTransformPoint(m_ActionHandle.controllerPosition).y; }
                        }
                        break;
                }
            }
            else if (m_Kind == Kind.Pistol && !m_Blowback && !m_SlideLocked && m_Open > 0f)
            {
                SetOpen(m_Open - Time.deltaTime * 14f);   // ressort de rappel
            }
            if (m_Kind == Kind.Rifle) BoltLeverSounds();
            ApplyAction();

            if (m_MagazineEject && m_Kind == Kind.Pistol && m_HasMag && grab.isSelected && EjectPressed()) EjectMagazine();
        }

        /// <summary>Volume du verrou de la carabine : l'enregistrement est bien plus fort que la détonation, on le ramène sous elle.</summary>
        const float BoltVolume = 0.3f;

        /// <summary>Carabine : levier du verrou relevé (armement) puis rabattu (verrouillage).</summary>
        void BoltLeverSounds()
        {
            if (!m_BoltLifted && m_Lift >= 0.98f)
            {
                m_BoltLifted = true;
                SfxBank.Play(SfxBank.VerrouReleve, transform.position, BoltVolume, Random.Range(0.97f, 1.03f));
                if (m_ActionHandle != null) HapticsTo(m_ActionHandle.interactor, 0.25f, 0.03f);
            }
            else if (m_BoltLifted && m_Lift <= 0.05f)
            {
                m_BoltLifted = false;
                SfxBank.Play(SfxBank.VerrouRabattu, transform.position, BoltVolume, Random.Range(0.97f, 1.03f));
                if (m_ActionHandle != null) HapticsTo(m_ActionHandle.interactor, 0.3f, 0.03f);
            }
        }

        bool EjectPressed()
        {
            foreach (var i in grab.interactorsSelecting)
            {
                var a = i.handedness == UnityEngine.XR.Interaction.Toolkit.Interactors.InteractorHandedness.Left ? s_EjectLeft : s_EjectRight;
                if (a != null && a.WasPressedThisFrame()) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ mécanisme

        /// <summary>Position du mécanisme (0 fermé, 1 ouvert). Bout de course arrière : éjection ; avant : chargement.</summary>
        void SetOpen(float v)
        {
            v = Mathf.Clamp01(v);
            if (v >= 0.95f && !m_WasOpen) { m_WasOpen = true; Opened(); }
            else if (v <= 0.05f && m_WasOpen) { m_WasOpen = false; Closed(); }
            m_Open = v;
        }

        void Opened()
        {
            if (m_Chamber == Chamber.Spent) Eject();
            else if (m_Chamber == Chamber.Live) EjectLive();
            m_Chamber = Chamber.Empty;
            // Fusil à pompe et carabine : sons enregistrés (pompe tirée, verrou tiré) ; le son généré reste pour le pistolet.
            string family = m_Kind == Kind.Shotgun ? SfxBank.PompeArriere : m_Kind == Kind.Rifle ? SfxBank.VerrouTire : null;
            if (family == null || !SfxBank.Play(family, transform.position, m_Kind == Kind.Rifle ? BoltVolume : 0.9f, Random.Range(0.97f, 1.03f)))
                Sfx.PlayAt(Sfx.ActionOpen, transform.position, 0.7f, m_Kind == Kind.Pistol ? 1.3f : 1f);
            if (m_ActionHandle != null) HapticsTo(m_ActionHandle.interactor, 0.35f, 0.04f);
        }

        void Closed()
        {
            if (m_Chamber == Chamber.Empty && CanFeed) { m_Chamber = Chamber.Live; if (!m_InfiniteAmmo) m_Loaded--; }
            string family = m_Kind == Kind.Shotgun ? SfxBank.PompeAvant : m_Kind == Kind.Rifle ? SfxBank.VerrouPousse : null;
            if (family == null || !SfxBank.Play(family, transform.position, m_Kind == Kind.Rifle ? BoltVolume : 0.9f, Random.Range(0.97f, 1.03f)))
                Sfx.PlayAt(Sfx.ActionClose, transform.position, 0.8f, m_Kind == Kind.Pistol ? 1.3f : 1f);
            if (m_ActionHandle != null) HapticsTo(m_ActionHandle.interactor, 0.45f, 0.05f);
        }

        // ------------------------------------------------------------------ aide à la manipulation

        public enum GuideStep { None, Pull, Push, LiftUp, LowerDown, InsertMagazine }

        /// <summary>
        /// Prochain geste à faire pour pouvoir tirer (voir <see cref="ReloadGuide"/>) : la pièce à actionner, la poignée
        /// qui la tient et la direction du geste, dans le repère du monde. None si l'arme est prête ou pas tenue.
        /// </summary>
        public GuideStep GetGuide(out Transform part, out GunHandle handle, out Vector3 direction)
        {
            part = m_Action; handle = null; direction = Vector3.zero;
            if (!grab.isSelected || m_Blowback || m_Action == null) return GuideStep.None;
            if (m_Kind == Kind.Pistol && !m_HasMag) { part = m_MagazineVisual != null ? m_MagazineVisual.transform : null; direction = transform.up; return GuideStep.InsertMagazine; }
            if (Ready) return GuideStep.None;

            var role = m_Kind == Kind.Shotgun ? GunHandle.Role.Pump : m_Kind == Kind.Rifle ? GunHandle.Role.Bolt : GunHandle.Role.Slide;
            if (m_Handles != null) foreach (var h in m_Handles) if (h != null && h.role == role) handle = h;
            var back = transform.TransformDirection(m_TravelRoot);
            switch (m_Kind)
            {
                case Kind.Pistol:
                    if (m_WasOpen) return GuideStep.None;   // culasse tirée : il suffit de la lâcher
                    direction = back; return GuideStep.Pull;
                case Kind.Shotgun:
                    if (m_WasOpen) { direction = -back; return GuideStep.Push; }
                    direction = back; return GuideStep.Pull;
                default:   // carabine : relever, tirer, pousser, rabattre
                    if (m_WasOpen) { direction = -back; return GuideStep.Push; }
                    if (m_Chamber == Chamber.Live) { direction = -transform.up; return GuideStep.LowerDown; }
                    if (m_Lift < 0.98f) { direction = transform.up; return GuideStep.LiftUp; }
                    direction = back; return GuideStep.Pull;
            }
        }

        /// <summary>Matériau de départ pour les flèches d'aide (additif, déjà inclus dans le build).</summary>
        public Material guideMaterialSource => m_TracerMaterial;

        /// <summary>Une cartouche peut monter dans la chambre (pistolet : seulement avec un chargeur en place).</summary>
        bool CanFeed => (m_Kind != Kind.Pistol || m_HasMag) && (m_InfiniteAmmo || m_Loaded > 0);

        void ApplyAction()
        {
            if (m_Action == null) return;
            m_Action.localPosition = m_ActionHome + m_ActionTravel * m_Open;
            if (m_BoltLift != 0f) m_Action.localRotation = m_ActionHomeRot * Quaternion.AngleAxis(m_BoltLift * m_Lift, m_BoltAxis);
        }

        IEnumerator Blowback()
        {
            m_Blowback = true;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.035f) { SetOpen(t); ApplyAction(); yield return null; }
            SetOpen(1f);
            if (CanFeed)
                for (float t = 1f; t > 0f; t -= Time.deltaTime / 0.04f) { SetOpen(t); ApplyAction(); yield return null; }
            else m_SlideLocked = true;   // dernière cartouche : culasse ouverte
            if (!m_SlideLocked) SetOpen(0f);
            ApplyAction();
            m_Blowback = false;
        }

        // ------------------------------------------------------------------ chargement

        /// <summary>Munition présentée à la zone de chargement : prise si l'arme peut l'accepter.</summary>
        public void TryLoad(AmmoItem item)
        {
            switch (m_Kind)
            {
                case Kind.Shotgun when item.kind == AmmoItem.Kind.Shell && m_Loaded < m_Capacity:
                case Kind.Rifle when item.kind == AmmoItem.Kind.RifleRound && m_Open > 0.85f && m_Loaded < m_Capacity:
                    m_Loaded++;
                    item.Consume();
                    Sfx.PlayAt(Sfx.LoadRound, transform.position, 0.7f, Random.Range(0.95f, 1.08f));
                    Haptics(0.2f, 0.03f);
                    break;
                case Kind.Pistol when item.kind == AmmoItem.Kind.PistolMagazine && !m_HasMag:
                    m_HasMag = true;
                    m_Loaded = item.rounds;
                    item.Consume();
                    if (m_MagazineVisual != null) m_MagazineVisual.SetActive(true);
                    Sfx.PlayAt(Sfx.ActionClose, transform.position, 0.6f, 1.6f);
                    Haptics(0.3f, 0.04f);
                    break;
            }
        }

        void EjectMagazine()
        {
            m_HasMag = false;
            if (m_MagazineVisual != null)
            {
                m_MagazineVisual.SetActive(false);
                if (m_AmmoPrefab != null)
                {
                    var t = m_MagazineVisual.transform;
                    var mag = Instantiate(m_AmmoPrefab, t.position, t.rotation);
                    mag.rounds = m_Loaded;
                    if (mag.TryGetComponent<Rigidbody>(out var rb))
                        rb.linearVelocity = -t.up * 1.2f + (TryGetComponent<Rigidbody>(out var own) ? own.linearVelocity : Vector3.zero);
                }
            }
            m_Loaded = 0;
            Sfx.PlayAt(Sfx.ActionOpen, transform.position, 0.6f, 1.5f);
        }

        // ------------------------------------------------------------------ éjection, douilles

        /// <summary>Cartouche intacte éjectée (mécanisme ouvert sans tirer) : une vraie munition, qu'on peut ramasser.</summary>
        void EjectLive()
        {
            if (m_AmmoPrefab == null || m_EjectPort == null || m_Kind == Kind.Pistol && m_AmmoPrefab.kind == AmmoItem.Kind.PistolMagazine) { Eject(); return; }
            var round = Instantiate(m_AmmoPrefab, m_EjectPort.position, m_EjectPort.rotation);
            if (round.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.linearVelocity = m_EjectPort.right * 1.4f + m_EjectPort.up * 1.2f;
                rb.angularVelocity = Random.insideUnitSphere * 12f;
            }
        }

        void Eject()
        {
            if (m_EjectPort == null || m_ShellMesh == null) return;
            GameObject go;
            if (s_Shells.Count >= MaxShells && (go = s_Shells.Dequeue()) != null) { }
            else go = NewShell();
            go.transform.SetParent(null);
            go.transform.SetPositionAndRotation(m_EjectPort.position, m_EjectPort.rotation * Quaternion.Euler(90f, 0f, 0f));
            go.GetComponent<MeshFilter>().sharedMesh = m_ShellMesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = m_ShellMaterials;
            var col = go.GetComponent<CapsuleCollider>();
            if (m_ShellSize.x > 0f)
            {
                go.transform.localScale = new Vector3(m_ShellSize.x, m_ShellSize.y * 0.5f, m_ShellSize.x);
                col.direction = 1; col.center = Vector3.zero; col.radius = 0.5f; col.height = 2f;
            }
            else
            {
                // Maillage à sa taille réelle (cartouche du fusil, de la carabine) : capsule le long de son plus grand axe.
                go.transform.localScale = Vector3.one;
                var b = m_ShellMesh.bounds; var s = b.size;
                col.direction = s.x >= s.y && s.x >= s.z ? 0 : s.y >= s.z ? 1 : 2;
                col.center = b.center;
                col.height = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                col.radius = Mathf.Min(s.x, Mathf.Min(s.y, s.z)) * 0.5f;
            }
            go.SetActive(true);
            var rb = go.GetComponent<Rigidbody>();
            rb.position = go.transform.position; rb.rotation = go.transform.rotation;
            var v = (m_EjectPort.right * Random.Range(1.6f, 2.4f) + m_EjectPort.up * Random.Range(1.2f, 1.8f) - m_EjectPort.forward * 0.3f);
            if (m_Kind != Kind.Pistol) v *= 0.6f;   // éjection à la main : plus molle
            var holder = GetComponent<Rigidbody>();
            rb.linearVelocity = v + (holder != null && !holder.isKinematic ? holder.linearVelocity : Vector3.zero);
            rb.angularVelocity = Random.insideUnitSphere * 25f;
            s_Shells.Enqueue(go);
            // Chute enregistrée jouée au premier contact : petite douille (pistolet), grosse (carabine), plastique (calibre 12).
            var snd = go.GetComponent<CasingSound>();
            snd.matter = m_Kind == Kind.Shotgun ? CasingSound.Matter.Plastic : m_Kind == Kind.Rifle ? CasingSound.Matter.RifleBrass : CasingSound.Matter.Brass;
            snd.ResetBounces();
        }

        void HapticsTo(UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor interactor, float amplitude, float duration)
        {
            if (interactor == null) return;
            var player = interactor.transform.GetComponentInParent<HapticImpulsePlayer>();
            if (player != null) player.SendHapticImpulse(amplitude, duration);
        }

        static GameObject NewShell()
        {
            var go = new GameObject("Douille");
            go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<CapsuleCollider>();
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.02f; rb.linearDamping = 0.1f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            go.AddComponent<CasingSound>();
            return go;
        }
    }
}
