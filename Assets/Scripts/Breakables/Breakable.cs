using System.Collections;
using UnityEngine;

namespace Fragile.Breakables
{
    /// <summary>
    /// Objet qui vole en éclats : coup de batte (<see cref="Breaker"/>) assez rapide, ou chute violente.
    /// Tout dépend de l'énergie du coup :
    /// - découpe : 3 niveaux pré-calculés (gros morceaux -> pulvérisé) ;
    /// - vitesse et direction des éclats, effets (éclats 3D, poussière, liquide), son, vibration, mini-ralenti.
    /// Les éclats et l'effet sont préparés dans l'éditeur (Fragile > Rendre cassable) et préchargés : rien de lourd au moment du coup.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Breakable : MonoBehaviour
    {
        [System.Serializable]
        public class ShardSet
        {
            public Mesh[] meshes;
            public Vector3[] offsets; // centre de chaque éclat, repère du maillage
        }

        [Header("Déclenchement")]
        [Tooltip("Vitesse d'impact minimale (m/s) avec un Breaker (la batte).")]
        [SerializeField] float m_BreakSpeedWithBreaker = 1.6f;
        [Tooltip("Vitesse d'impact minimale (m/s) contre n'importe quoi (chute, lancer).")]
        [SerializeField] float m_BreakSpeedAny = 4f;
        [Tooltip("Vitesse d'impact (m/s) considérée comme un coup maximal.")]
        [SerializeField] float m_MaxImpactSpeed = 9f;

        [Header("Éclats (générés par Fragile > Rendre cassable)")]
        [Tooltip("Du plus gros au plus fin : coup mou -> niveau 0, coup violent -> dernier niveau.")]
        [SerializeField] ShardSet[] m_Levels;
        [SerializeField] Material[] m_ShardMaterials;   // matériaux d'origine + intérieur/tranche en dernier
        [SerializeField] Transform m_MeshTransform;
        [SerializeField] float m_Mass = 1.2f;
        [Tooltip("Nombre de plus gros morceaux qui restent au sol après la casse (les miettes disparaissent).")]
        [SerializeField] int m_PiecesLeftOnGround = 7;

        [Header("Effets")]
        [SerializeField] GameObject m_FxPrefab;
        [SerializeField] Color m_ChipColor = new Color(0.95f, 0.95f, 0.97f);
        [SerializeField] Color m_ChipColor2 = new Color(0.16f, 0.26f, 0.75f);
        [SerializeField] bool m_Liquid = true;
        [SerializeField] Color m_LiquidColor = new Color(0.85f, 0.55f, 0.15f, 0.85f);
        [Tooltip("Ralenti maximal à l'impact (secondes, 0 = aucun). Seuls les coups francs le déclenchent.")]
        [SerializeField] float m_HitStop = 0.11f;

        [Header("Son")]
        [Tooltip("Son de casse (bouteilles : verre). Vide = son généré selon la matière.")]
        [SerializeField] AudioClip m_BreakClip;

        public enum Matter { Ceramic, Glass, Wood, Metal, Plastic }

        [Header("Résistance (décor)")]
        [Tooltip("Matière : son des coups et de la casse, étincelles (métal).")]
        [SerializeField] Matter m_Matter = Matter.Ceramic;
        [Tooltip("Nombre de coups francs avant la casse (1 = au premier coup, comme une bouteille). Un coup mou compte moins.")]
        [SerializeField] float m_Hits = 1f;
        [Tooltip("Marques de coups (atlas 2×2, shader Fragile/Impact) : fissures, entailles, bosses.")]
        [SerializeField] Material m_DamageMaterial;
        [SerializeField] Material m_SparkMaterial;
        [Tooltip("Vitesse des éclats : métal lourd < bois < céramique.")]
        [SerializeField] float m_Burst = 1f;

        bool m_Broken;
        float m_Damage, m_LastHit = -10f;
        readonly System.Collections.Generic.List<GameObject> m_Marks = new System.Collections.Generic.List<GameObject>();
        ParticleSystem m_Sparks;
        Coroutine m_Shake;
        static Mesh[] s_MarkQuads;

        /// <summary>Usure de l'objet : 0 = intact, 1 = sur le point de casser.</summary>
        public float damage01 => m_Hits <= 1f ? 0f : Mathf.Clamp01(m_Damage / m_Hits);
        float m_SpawnTime;
        static AudioClip s_SmashClip;
        static bool s_HitStopRunning;
        BreakableSounds m_Sounds;
        float m_LastKnock = -10f;
        BreakableSounds Sounds => m_Sounds ??= BreakableSounds.For(name, m_Matter);

        void Awake()
        {
            m_SpawnTime = Time.time;
            // Sons enregistrés choisis d'après le nom (tabouret, table, bouteille…) et la matière.
            m_Sounds = BreakableSounds.For(name, m_Matter);
            m_Sounds.Preload();
            if (m_Sounds.roll != null && !TryGetComponent<Fragile.Audio.RollingSound>(out _))
                gameObject.AddComponent<Fragile.Audio.RollingSound>().family = m_Sounds.roll;
            // Préchargement : le son et un effet sont prêts avant le premier coup.
            if (m_BreakClip == null && s_SmashClip == null) s_SmashClip = SmashSynth.Create();
            BreakFx.Prewarm(m_FxPrefab, 2);
        }

        /// <summary>Choc trop faible pour abîmer : chute, renversement, coup de batte mou.</summary>
        void Knock(Collision c, float speed, bool byBreaker)
        {
            if (speed < 0.9f || Time.time - m_LastKnock < 0.35f) return;
            if (!byBreaker)
            {
                // Chute ou renversement : c'est cet objet qui bouge. S'il est immobile et qu'on lui lance quelque chose,
                // c'est le projectile qui fait le bruit, pas lui.
                if (!TryGetComponent<Rigidbody>(out var body) || body.isKinematic) return;
                var other = c.rigidbody;
                if (other != null && !other.isKinematic && other.linearVelocity.sqrMagnitude > body.linearVelocity.sqrMagnitude + body.angularVelocity.sqrMagnitude * 0.04f) return;
            }
            m_LastKnock = Time.time;
            var point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            float k = Mathf.InverseLerp(0.9f, m_BreakSpeedAny, speed);
            string family = byBreaker ? Sounds.PickHit() : Sounds.fall;
            if (family != null && Fragile.Audio.SfxBank.Play(family, point, Mathf.Lerp(0.25f, 0.8f, k), Random.Range(0.94f, 1.06f))) return;
            if (Sounds.glass) Fragile.Audio.Sfx.PlayAt(Fragile.Audio.Sfx.CeramicHit, point, Mathf.Lerp(0.15f, 0.45f, k), Random.Range(1.2f, 1.5f));
        }

        void OnCollisionEnter(Collision c)
        {
            if (m_Broken || Time.time - m_SpawnTime < 0.5f) return;
            // Les éclats d'un objet cassé ne cassent pas les autres (pas de réaction en chaîne).
            if (Debris.IsDebris(c)) return;

            var breaker = c.rigidbody != null ? c.rigidbody.GetComponentInParent<Breaker>() : c.collider.GetComponentInParent<Breaker>();
            float speed = c.relativeVelocity.magnitude;
            bool hitByBreaker = breaker != null && speed * breaker.power >= m_BreakSpeedWithBreaker;
            if (!hitByBreaker && speed < m_BreakSpeedAny) { Knock(c, speed, breaker != null); return; }

            float power = breaker != null ? speed * breaker.power : speed;
            var point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            // Direction du coup : mouvement de ce qui frappe (la batte), sinon sens de l'impact.
            var dir = breaker != null && c.rigidbody != null && c.rigidbody.linearVelocity.sqrMagnitude > 0.01f
                ? c.rigidbody.linearVelocity.normalized
                : -c.relativeVelocity.normalized;

            float minSpeed = hitByBreaker ? m_BreakSpeedWithBreaker : m_BreakSpeedAny;
            float energy = Mathf.InverseLerp(minSpeed, m_MaxImpactSpeed, power);
            if (breaker != null) breaker.Haptics(energy);

            // Objet fragile (bouteille, puff) : il casse au premier coup.
            if (m_Hits <= 1f) { Break(point, dir, power, energy); return; }

            // Objet solide : chaque coup l'abîme un peu plus (un coup franc compte plus qu'un coup mou).
            if (Time.time - m_LastHit < 0.15f) return;   // plusieurs contacts du même coup
            m_LastHit = Time.time;
            m_Damage += Mathf.Lerp(0.45f, 1.4f, energy) * (hitByBreaker ? 1f : 0.6f);
            if (m_Damage >= m_Hits) { Break(point, dir, power, Mathf.Max(energy, 0.55f)); return; }

            var normal = c.contactCount > 0 ? c.GetContact(0).normal : -dir;
            var hitter = c.rigidbody != null ? c.rigidbody.worldCenterOfMass : point - dir;
            if (Vector3.Dot(normal, hitter - point) < 0f) normal = -normal;   // vers ce qui frappe
            Damage(point, normal, dir, energy);
        }

        /// <summary>
        /// Balle ou plombs (voir <see cref="Fragile.Weapons.Firearm"/>) : comme un coup, sans collision physique.
        /// Un objet fragile casse aussitôt ; le décor encaisse <paramref name="damage"/> (en « coups francs »).
        /// </summary>
        public void Shot(Vector3 point, Vector3 normal, Vector3 direction, float damage, float energy)
        {
            if (m_Broken) return;
            energy = Mathf.Clamp01(energy);
            float power = Mathf.Lerp(m_BreakSpeedWithBreaker, m_MaxImpactSpeed, energy);
            if (m_Hits <= 1f) { Break(point, direction, power, energy); return; }
            m_LastHit = Time.time;
            m_Damage += damage;
            if (m_Damage >= m_Hits) { Break(point, direction, power, Mathf.Max(energy, 0.55f)); return; }
            Damage(point, normal, direction, energy);
        }

        // ------------------------------------------------------------------ usure (décor)

        /// <summary>Coup encaissé sans casser : marque à l'impact (plus grande avec l'usure), éclats, son, tremblement.</summary>
        void Damage(Vector3 point, Vector3 normal, Vector3 dir, float energy)
        {
            float wear = damage01;
            Crack(point, wear);
            AddMark(point, normal, Mathf.Lerp(0.1f, 0.22f, energy) * Mathf.Lerp(1f, 1.6f, wear));

            var fx = BreakFx.Get(m_FxPrefab);
            if (fx != null)
                fx.Play(new BreakFx.Settings { point = point, direction = normal, energy = Mathf.Lerp(0.04f, 0.2f, energy), chip1 = m_ChipColor, chip2 = m_ChipColor2, liquid = false });
            if (m_Matter == Matter.Metal) Sparks(point, normal, Mathf.RoundToInt(Mathf.Lerp(10f, 40f, energy)));

            var clip = Fragile.Audio.SfxBank.Pick(Sounds.PickHit());
            if (clip == null) clip = m_Matter switch
            {
                Matter.Wood => Fragile.Audio.Sfx.WoodHit,
                Matter.Metal => Fragile.Audio.Sfx.MetalHit,
                Matter.Plastic => Fragile.Audio.Sfx.PlasticHit,
                _ => Fragile.Audio.Sfx.CeramicHit,
            };
            // Plus il est abîmé, plus le son est grave et « fatigué ».
            Fragile.Audio.Sfx.PlayAt(clip, point, Mathf.Lerp(0.5f, 1f, energy), Mathf.Lerp(1.05f, 0.85f, wear) * Random.Range(0.95f, 1.05f));

            if (m_Shake != null) StopCoroutine(m_Shake);
            m_Shake = StartCoroutine(Shake(dir, Mathf.Lerp(0.004f, 0.012f, energy) * Mathf.Lerp(1f, 1.8f, wear)));
        }

        // Objet fissuré : remplacé par ses morceaux (premier niveau d'éclats), encore à leur place, qui se disjoignent.
        Transform m_Cracked;
        Transform[] m_Pieces;
        Vector3[] m_PieceRest, m_PieceAxis;
        float m_LocalSize;

        /// <summary>
        /// Les fissures s'ouvrent : jointures et fentes s'écartent autour de l'impact, d'autant plus que l'objet est usé ;
        /// les morceaux proches du coup basculent un peu. La matière à nu apparaît dans les fentes.
        /// </summary>
        void Crack(Vector3 point, float wear)
        {
            if (m_Levels == null || m_Levels.Length == 0 || m_Levels[0].meshes == null) return;
            var t = m_MeshTransform != null ? m_MeshTransform : transform;
            if (m_Cracked == null)
            {
                var set = m_Levels[0];
                var bounds = new Bounds();
                bool first = true;
                foreach (var r in GetComponentsInChildren<MeshRenderer>())
                {
                    if (r.GetComponentInParent<GlassPane>() != null || r.name == "Surbrillance") continue;
                    var lb = r.bounds; if (first) { bounds = lb; first = false; } else bounds.Encapsulate(lb);
                    r.enabled = false;
                }
                m_LocalSize = Mathf.Max(0.1f, t.InverseTransformVector(bounds.size).magnitude);
                m_Cracked = new GameObject("Fissures").transform;
                m_Cracked.SetParent(t, false);
                m_Pieces = new Transform[set.meshes.Length];
                m_PieceRest = new Vector3[set.meshes.Length];
                m_PieceAxis = new Vector3[set.meshes.Length];
                var rng = new System.Random(name.GetHashCode() ^ transform.position.GetHashCode());
                for (int i = 0; i < set.meshes.Length; i++)
                {
                    if (set.meshes[i] == null) continue;
                    var go = new GameObject("Morceau " + i);
                    go.transform.SetParent(m_Cracked, false);
                    go.transform.localPosition = set.offsets[i];
                    go.AddComponent<MeshFilter>().sharedMesh = set.meshes[i];
                    go.AddComponent<MeshRenderer>().sharedMaterials = m_ShardMaterials;
                    m_Pieces[i] = go.transform;
                    m_PieceRest[i] = set.offsets[i];
                    m_PieceAxis[i] = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f).normalized;
                }
            }

            var local = t.InverseTransformPoint(point);
            float open = Mathf.Pow(wear, 0.8f);   // dès le 1er coup les fentes se voient, les derniers disloquent
            for (int i = 0; i < m_Pieces.Length; i++)
            {
                if (m_Pieces[i] == null) continue;
                var away = m_PieceRest[i] - local;
                float near = 1f / (1f + away.magnitude / (m_LocalSize * 0.25f));
                float gap = m_LocalSize * Mathf.Lerp(0.01f, 0.055f, open) * (0.35f + near);
                // Un peu plus écarté à chaque coup, jamais refermé.
                var target = m_PieceRest[i] + away.normalized * gap;
                if ((target - m_PieceRest[i]).sqrMagnitude > (m_Pieces[i].localPosition - m_PieceRest[i]).sqrMagnitude)
                    m_Pieces[i].localPosition = target;
                m_Pieces[i].localRotation = Quaternion.AngleAxis(Mathf.Lerp(1.5f, 10f, open) * near * 2f, m_PieceAxis[i]);
            }
        }

        void AddMark(Vector3 point, Vector3 normal, float size)
        {
            if (m_DamageMaterial == null) return;
            if (s_MarkQuads == null) s_MarkQuads = MarkQuads();
            GameObject go;
            if (m_Marks.Count >= 10) { go = m_Marks[0]; m_Marks.RemoveAt(0); }
            else
            {
                go = new GameObject("Marque de coup");
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = m_DamageMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
            go.GetComponent<MeshFilter>().sharedMesh = s_MarkQuads[Random.Range(0, s_MarkQuads.Length)];
            // Marque dans le monde (pas enfant : l'objet peut être mis à l'échelle de façon non uniforme).
            go.transform.SetPositionAndRotation(point + normal * 0.003f, Quaternion.LookRotation(normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
            go.transform.localScale = Vector3.one * size;
            m_Marks.Add(go);
        }

        void Sparks(Vector3 point, Vector3 normal, int count)
        {
            if (m_SparkMaterial == null) return;
            if (m_Sparks == null)
            {
                var go = new GameObject("Étincelles");
                go.SetActive(false);
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.playOnAwake = false; main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.009f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.45f), new Color(1f, 0.6f, 0.2f));
                main.gravityModifier = 1f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 80;
                var em = ps.emission; em.enabled = false;
                var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 50f; shape.radius = 0.01f;
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = m_SparkMaterial;
                r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.03f; r.lengthScale = 1.5f;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.SetActive(true);
                m_Sparks = ps;
            }
            m_Sparks.transform.SetPositionAndRotation(point, Quaternion.LookRotation(normal));
            m_Sparks.Emit(count);
        }

        /// <summary>Petit tremblement sous le coup (l'objet n'est pas statique pour pouvoir bouger).</summary>
        IEnumerator Shake(Vector3 dir, float amplitude)
        {
            var rest = transform.position;
            for (float t = 0f; t < 0.2f; t += Time.deltaTime)
            {
                float a = amplitude * (1f - t / 0.2f);
                transform.position = rest + (dir * Mathf.Sin(t * 90f) + Random.insideUnitSphere * 0.3f) * a;
                yield return null;
            }
            transform.position = rest;
            m_Shake = null;
        }

        static Mesh[] MarkQuads()
        {
            var quads = new Mesh[4];
            for (int i = 0; i < 4; i++)
            {
                var o = new Vector2(i % 2, i / 2) * 0.5f;
                var m = new Mesh { name = "Marque " + i };
                m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
                m.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
                m.uv = new[] { o, o + new Vector2(0.5f, 0f), o + new Vector2(0.5f, 0.5f), o + new Vector2(0f, 0.5f) };
                m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                m.RecalculateBounds();
                quads[i] = m;
            }
            return quads;
        }

        /// <summary>Casse l'objet. energy : 0 = juste au seuil, 1 = coup maximal.</summary>
        public void Break(Vector3 point, Vector3 direction, float power, float energy)
        {
            if (m_Broken) return;
            m_Broken = true;
            energy = Mathf.Clamp01(energy);

            var inherited = GetComponent<Rigidbody>().linearVelocity;
            SpawnShards(point, direction, power, energy, inherited);

            var fx = BreakFx.Get(m_FxPrefab);
            if (fx != null)
                fx.Play(new BreakFx.Settings
                {
                    // Décor (bois, métal) : peu d'éclats projetés, surtout de la poussière ; le verre gicle.
                    point = point, direction = direction, energy = energy * Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(m_Burst)),
                    chip1 = m_ChipColor, chip2 = m_ChipColor2, liquid = m_Liquid, liquidColor = m_LiquidColor
                });

            PlaySound(point, energy);
            if (m_Matter == Matter.Metal) Sparks(point, -direction, 60);
            if (m_HitStop > 0f && energy > 0.3f && !s_HitStopRunning)
                StaticRunner.Run(HitStop(m_HitStop * Mathf.Lerp(0.45f, 1f, energy)));

            // Les marques de coups et les étincelles ne sont pas des enfants : on les retire avec l'objet
            // (les étincelles finissent leur gerbe avant).
            foreach (var mark in m_Marks) if (mark != null) Destroy(mark);
            m_Marks.Clear();
            if (m_Sparks != null) Destroy(m_Sparks.gameObject, 1f);

            Fragile.Mice.Mouse.ScareAll(point, 3f);   // fracas : les souris voisines détalent
            ReleaseHands();
            gameObject.SetActive(false);
            Destroy(gameObject, 0.1f);
        }

        /// <summary>
        /// Les mains qui tiennent l'objet y sont rattachées : on les rend à leur manette AVANT de désactiver l'objet
        /// (sinon elles seraient détruites avec lui ; Unity interdit de les déplacer pendant la désactivation).
        /// </summary>
        void ReleaseHands()
        {
            if (TryGetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>(out var grab) && grab.isSelected && grab.interactionManager != null)
                grab.interactionManager.CancelInteractableSelection((UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)grab);
            // Sécurité : toute main encore rattachée retourne sur sa manette.
            foreach (var hand in GetComponentsInChildren<Fragile.Hands.ControllerHandPose>(true))
                hand.EndGrab();
        }

        // ------------------------------------------------------------------ éclats

        void SpawnShards(Vector3 point, Vector3 dir, float power, float energy, Vector3 inherited)
        {
            if (m_Levels == null || m_Levels.Length == 0) return;
            var set = m_Levels[Mathf.Clamp(Mathf.FloorToInt(energy * m_Levels.Length), 0, m_Levels.Length - 1)];
            if (set == null || set.meshes == null || set.meshes.Length == 0) return;

            var t = m_MeshTransform != null ? m_MeshTransform : transform;
            var root = new GameObject(name + " (éclats)").transform;
            float totalVolume = 0f;
            foreach (var m in set.meshes) if (m != null) totalVolume += Volume(m);

            // Un coup mou fait surtout tomber les morceaux ; un coup violent les projette.
            float push = Mathf.Lerp(0.15f, 0.8f, energy) * m_Burst;
            float burst = Mathf.Lerp(0.3f, 2f, energy) * m_Burst;
            float spin = Mathf.Lerp(3f, 20f, energy) * Mathf.Lerp(0.5f, 1f, m_Burst);

            // Colliders des objets attrapables autour de la casse (à ignorer par les morceaux).
            var nearbyInteractables = new System.Collections.Generic.List<Collider>();
            foreach (var c in Physics.OverlapSphere(point, 3f, ~0, QueryTriggerInteraction.Ignore))
                if (c.attachedRigidbody != null && c.attachedRigidbody.gameObject != gameObject &&
                    c.attachedRigidbody.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>() != null)
                    nearbyInteractables.Add(c);

            // Les plus gros morceaux restent au sol ; les autres (miettes) disparaîtront une fois posés.
            var order = new System.Collections.Generic.List<int>();
            for (int i = 0; i < set.meshes.Length; i++) if (set.meshes[i] != null) order.Add(i);
            order.Sort((a, b) => Volume(set.meshes[b]).CompareTo(Volume(set.meshes[a])));
            var keep = new System.Collections.Generic.HashSet<int>();
            for (int k = 0; k < order.Count && k < m_PiecesLeftOnGround; k++) keep.Add(order[k]);

            for (int i = 0; i < set.meshes.Length; i++)
            {
                var mesh = set.meshes[i];
                if (mesh == null) continue;
                var go = new GameObject("Éclat " + i);
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(t.TransformPoint(set.offsets[i]), t.rotation);
                go.transform.localScale = t.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = m_ShardMaterials;

                var col = go.AddComponent<BoxCollider>();
                col.center = mesh.bounds.center; col.size = Vector3.Max(mesh.bounds.size, Vector3.one * 0.004f);
                // En vol, les morceaux ignorent les objets attrapables (bouteilles, batte, puffs) : ils ne les poussent pas
                // et ne les cassent pas. Ils rebondissent toujours sur le décor (sol, comptoir, murs).
                foreach (var other in nearbyInteractables) Physics.IgnoreCollision(col, other, true);

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = Mathf.Max(0.02f, m_Mass * Volume(mesh) / Mathf.Max(1e-6f, totalVolume));
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                // Les morceaux proches de l'impact partent plus vite et plus dans le sens du coup.
                var fromImpact = go.transform.position - point;
                float near = 1f / (1f + fromImpact.magnitude * 8f);
                rb.linearVelocity = inherited
                    + dir * power * Random.Range(0.6f, 1f) * push * (0.4f + near)
                    + fromImpact.normalized * Random.Range(0.5f, 1f) * burst
                    + Random.insideUnitSphere * 0.3f * (0.5f + energy)
                    + Vector3.up * Random.Range(0.1f, 0.8f) * energy;
                rb.angularVelocity = Random.insideUnitSphere * spin;

                go.AddComponent<Debris>().Init(keep.Contains(i));
            }
        }

        static float Volume(Mesh m) { var s = m.bounds.size; return Mathf.Max(1e-6f, s.x * s.y * s.z); }

        // ------------------------------------------------------------------ son

        void PlaySound(Vector3 point, float energy)
        {
            // Coup violent : un peu plus grave et plus "gros" ; coup mou : plus aigu, plus fin.
            float volume = Mathf.Lerp(0.45f, 1f, energy);
            float pitch = Mathf.Lerp(1.12f, 0.92f, energy) * Random.Range(0.96f, 1.04f);

            // Sons enregistrés : plusieurs couches jouées ensemble (la première est la principale).
            bool played = false;
            foreach (var layer in Sounds.brk)
            {
                var c = Fragile.Audio.SfxBank.Pick(layer.family);
                if (c == null) continue;
                // Les longues couches (jet de bière) ne sont pas accélérées : seul le fracas suit la violence du coup.
                Emit(c, point, volume * layer.volume, c.length > 3f ? 1f : pitch);
                played = true;
            }
            if (played) return;

            if (m_BreakClip == null && s_SmashClip == null) s_SmashClip = SmashSynth.Create();
            var clip = m_BreakClip != null ? m_BreakClip : m_Matter switch
            {
                Matter.Wood => Fragile.Audio.Sfx.WoodBreak,
                Matter.Metal => Fragile.Audio.Sfx.MetalBreak,
                Matter.Plastic => Fragile.Audio.Sfx.PlasticBreak,
                _ => s_SmashClip,
            };
            Emit(clip, point, volume, pitch);
        }

        static void Emit(AudioClip clip, Vector3 point, float volume, float pitch)
        {
            var go = new GameObject("Son casse");
            go.transform.position = point;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Logarithmic; src.minDistance = 0.6f; src.maxDistance = 25f;
            src.volume = volume;
            src.pitch = pitch;
            src.dopplerLevel = 0f;
            src.Play();
            Destroy(go, clip.length / src.pitch + 0.2f);
        }

        // ------------------------------------------------------------------ ralenti

        static IEnumerator HitStop(float seconds)
        {
            s_HitStopRunning = true;
            float previous = Time.timeScale;
            Time.timeScale = 0.15f;
            yield return new WaitForSecondsRealtime(seconds);
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.08f)
            {
                Time.timeScale = Mathf.Lerp(0.15f, previous, t);
                yield return null;
            }
            Time.timeScale = previous;
            s_HitStopRunning = false;
        }

        /// <summary>Lance une coroutine qui survit à la destruction de l'objet cassé.</summary>
        class StaticRunner : MonoBehaviour
        {
            static StaticRunner s_Instance;
            public static void Run(IEnumerator routine)
            {
                if (s_Instance == null)
                {
                    var go = new GameObject("[Fragile] Breakable Runner") { hideFlags = HideFlags.HideAndDontSave };
                    DontDestroyOnLoad(go);
                    s_Instance = go.AddComponent<StaticRunner>();
                }
                s_Instance.StartCoroutine(routine);
            }
        }
    }
}
