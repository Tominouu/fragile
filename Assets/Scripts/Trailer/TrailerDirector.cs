using System.Collections;
using Fragile.Audio;
using Fragile.Breakables;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace Fragile.Trailer
{
    /// <summary>
    /// Intro du jeu (scène Trailer). La scène Trailer ne contient QUE cet objet : le bar n'est jamais copié.
    /// Au lancement, la VRAIE scène Bar est chargée en plus (additive) : toute modification de Tom dans Bar.unity
    /// apparaît donc automatiquement dans le Trailer. Les points d'accroche (télés, tabourets, XR Origin) sont
    /// retrouvés par leurs noms à chaque lancement (<see cref="TrailerAnchors"/>).
    ///
    /// Déroulé : noir -> assis au comptoir face à une télé, fin de match 1-1, mains posées sur le comptoir
    /// -> occasion sur le poteau -> but adverse -> sifflet final -> le personnage pète un câble (<see cref="TrailerGhost"/> :
    /// le joueur ne contrôle plus que sa tête) : poings sur le comptoir, verres jetés dans la télé, il se lève, balaie le
    /// comptoir, prend la batte et casse tout -> coupure au noir sur le dernier coup -> le Bar est rechargé intact et
    /// rendu au joueur : à lui de tout casser. A / X (ou Espace) : passer l'intro.
    /// </summary>
    public class TrailerDirector : MonoBehaviour
    {
        [Header("Scène")]
        [Tooltip("Nom de la scène du jeu, chargée en plus du Trailer (doit être dans la liste du build).")]
        [SerializeField] string m_BarScene = "Bar";
        [Tooltip("Facultatif : force la place des yeux (position + regard). Vide = meilleur tabouret face à une télé, choisi automatiquement.")]
        [SerializeField] Transform m_ViewPoint;
        [SerializeField] bool m_Skippable = true;

        [Header("Shaders (référencés ici pour être inclus dans le build)")]
        [SerializeField] Shader m_OverlayShader;
        [SerializeField] Shader m_UnlitShader;

        [Header("Match")]
        [SerializeField] string m_HomeName = "FRA";
        [SerializeField] string m_AwayName = "ADV";
        [SerializeField] Color m_HomeColor = new Color(0.78f, 0.08f, 0.1f);
        [SerializeField] Color m_AwayColor = new Color(0.12f, 0.32f, 0.85f);

        [Header("Clients du bar (Fragile > Trailer > Préparer les clients du bar)")]
        [SerializeField] GameObject[] m_Clients = new GameObject[0];
        [SerializeField] RuntimeAnimatorController m_ClientController;
        [Tooltip("Nombre de clients (barman compris). Chacun ≈ 13 k triangles : à surveiller sur Quest.")]
        [SerializeField, Range(0, 30)] int m_MaxClients = 18;

        [Header("Déroulé (secondes depuis la fin du noir)")]
        [SerializeField] float m_FadeIn = 2.5f;
        [SerializeField] float m_ChanceAt = 5f;
        [SerializeField] float m_GoalAt = 12f;
        [SerializeField] float m_EndAt = 17f;
        [Tooltip("Noir entre le dernier coup de batte et le bar rendu au joueur.")]
        [SerializeField] float m_BlackHold = 1.5f;
        [SerializeField] float m_FadeToBar = 2f;

        [Header("Sons (vide = généré par le code)")]
        [SerializeField] AudioClip m_CrowdClip;
        [SerializeField] AudioClip m_WhistleClip;
        [SerializeField] AudioClip m_HeartClip;
        [SerializeField] AudioClip m_BreathClip;
        [SerializeField] AudioClip m_SmashClip;
        [SerializeField, Range(0f, 1f)] float m_CrowdVolume = 0.55f;

        TrailerAnchors m_Anchors;
        XROrigin m_Origin;
        TrailerGhost m_Ghost;
        TrailerCrowd m_Crowd;
        Material m_ScreenMat;
        bool m_KeepScreens;

        FakeMatch m_Match;
        Material m_Overlay;
        GameObject m_OverlayGo;
        AudioSource m_TvCrowd, m_Whistle, m_Heart, m_Breath, m_Fx;
        AudioLowPassFilter m_CrowdFilter;
        InputAction m_SkipAction;
        bool m_Skip, m_Running, m_Final;
        Vector3 m_EndEye, m_EndForward;
        float m_CrowdLevel = 1f, m_Rage;

        static readonly int FadeId = Shader.PropertyToID("_Fade"), VignetteId = Shader.PropertyToID("_Vignette");

        void Awake()
        {
            m_SkipAction = new InputAction("Passer l'intro", InputActionType.Button);
            m_SkipAction.AddBinding("<XRController>/primaryButton");
            m_SkipAction.AddBinding("<Keyboard>/space");
            m_SkipAction.AddBinding("<Keyboard>/escape");
            m_SkipAction.performed += _ => { if (m_Skippable && m_Running) m_Skip = true; };
        }

        void OnEnable() => m_SkipAction?.Enable();
        void OnDisable() => m_SkipAction?.Disable();
        void OnDestroy() { m_SkipAction?.Dispose(); if (m_ScreenMat != null) Destroy(m_ScreenMat); }

        IEnumerator Start()
        {
            // 1) Le vrai bar, chargé à côté (jamais dupliqué).
            yield return LoadBar();
            var bar = SceneManager.GetSceneByName(m_BarScene);
            if (!bar.isLoaded) yield break;

            // 2) Points d'accroche.
            m_Anchors = TrailerAnchors.Find(bar, m_ViewPoint);
            Debug.Log(m_Anchors.Report());
            if (!m_Anchors.Ok)
            {
                Debug.LogWarning("[Fragile] Trailer ignoré : le Bar se lance directement.");
                UnloadTrailer();
                yield break;
            }
            m_Origin = m_Anchors.Origin;

            CreateOverlay();
            SetOverlay(1f, 0f);
            foreach (var b in m_Origin.GetComponentsInChildren<LocomotionProvider>(true)) b.enabled = false;
            HideFirearms(bar);
            CreateMatch();
            CreateAudio();
            m_Running = true;

            // 3) Dans le noir : installation sur le tabouret (le casque peut mettre un moment à donner sa position),
            //    puis le personnage prend la main sur les bras.
            float black = 1.2f;
            while (black > 0f) { Place(); black -= Time.deltaTime; yield return null; }
            m_Ghost = gameObject.AddComponent<TrailerGhost>();
            if (!m_Ghost.Setup(m_Origin, m_Anchors)) { Destroy(m_Ghost); m_Ghost = null; }
            else
            {
                m_Ghost.Effort += Grunt;
                m_Ghost.TvHit += () => { m_KeepScreens = false; StartCoroutine(CrowdCut()); };
                m_Ghost.FinalImpact += () => m_Final = true;
                m_Ghost.Outburst += () => { if (m_Crowd != null) m_Crowd.Panic(HeadPosition); };
            }

            // Le bar plein : clients assis, debout, barman (retrouvés et placés dans le vrai Bar).
            if (m_Clients != null && m_Clients.Length > 0 && m_ClientController != null)
            {
                m_Crowd = gameObject.AddComponent<TrailerCrowd>();
                m_Crowd.Build(bar, m_Anchors, m_Clients, m_ClientController, m_MaxClients, m_UnlitShader);
                m_Crowd.WatchTv(m_Anchors.Screens);
            }
            else Debug.Log("[Fragile] Trailer : pas de clients dans le bar (Fragile > Trailer > Préparer les clients du bar).");

            // 4) Le match.
            float t = 0f;
            bool chance = false, goal = false;
            while (t < m_EndAt && !m_Skip)
            {
                t += Time.deltaTime;
                SetOverlay(1f - Mathf.SmoothStep(0f, 1f, t / m_FadeIn), 0f);
                if (!chance && t >= m_ChanceAt) { chance = true; m_Match.Chance(); StartCoroutine(CrowdSwell(1.9f, 0.6f, 3f)); }
                if (!goal && t >= m_GoalAt)
                {
                    goal = true; m_Match.AwayGoal();
                    if (m_Ghost != null) { m_Ghost.Clench(true); m_Ghost.Tremble = 0.25f; }
                }
                KeepPlaced();
                yield return null;
            }

            if (!m_Skip)
            {
                // 5) Sifflet final : il pète un câble.
                m_Match.EndMatch();
                m_Whistle.Play();
                if (m_Crowd != null) m_Crowd.Stare(HeadPosition);   // tout le monde se tourne vers lui
                yield return Rage();
            }

            // 6) Coupure : noir, le bar est rechargé intact, puis rendu au joueur.
            yield return FadeTo(1f, m_Skip ? 0.4f : 0f);
            foreach (var s in new[] { m_TvCrowd, m_Heart, m_Breath, m_Whistle }) if (s != null) s.Stop();
            m_Running = false;
            // Là où la cinématique s'arrête : le joueur reprendra la main au même endroit, tourné pareil.
            m_EndEye = m_Anchors.Eye;
            m_EndForward = m_Ghost != null ? m_Ghost.Forward : Flat(m_Anchors.LookAt - m_Anchors.Eye);
            if (m_Ghost != null) { Destroy(m_Ghost); m_Ghost = null; }   // ses mains et sa batte disparaissent avec le bar
            if (m_Crowd != null) { Destroy(m_Crowd); m_Crowd = null; }   // plus personne dans la salle quand le joueur prend la main
            float hold = m_Skip ? 0.2f : m_BlackHold;
            yield return ReloadBar(hold);
            float f = 0f;
            while (f < m_FadeToBar) { f += Time.deltaTime; SetOverlay(1f - Mathf.SmoothStep(0f, 1f, f / m_FadeToBar), 0f); yield return null; }
            if (m_OverlayGo != null) Destroy(m_OverlayGo);
            UnloadTrailer();
        }

        // ------------------------------------------------------------------ crise

        IEnumerator Rage()
        {
            m_Breath.Play();
            if (m_Ghost != null) m_Ghost.StartCoroutine(m_Ghost.Rampage());
            else { m_Final = true; if (m_Crowd != null) m_Crowd.Panic(HeadPosition); }

            float t = 0f, nextBeat = 0f, pulse = 0f;
            while (!m_Final && !m_Skip)
            {
                // La colère monte vite puis reste au maximum pendant toute la casse.
                float k = Mathf.Clamp01(t / 4f);
                m_Rage = k;
                float bpm = Mathf.Lerp(80f, 160f, k);
                if (t >= nextBeat)
                {
                    nextBeat = t + 60f / bpm;
                    m_Heart.volume = Mathf.Lerp(0.4f, 0.9f, k);
                    m_Heart.pitch = Mathf.Lerp(0.95f, 1.12f, k);
                    m_Heart.Play();
                    pulse = 1f;
                }
                pulse = Mathf.Max(0f, pulse - Time.deltaTime * 4f);

                // Le son de la télé s'éloigne (effet tunnel), le souffle et le rouge montent ; le centre de la vue
                // reste dégagé : on doit voir ce que le personnage casse.
                m_CrowdLevel = Mathf.Min(m_CrowdLevel, Mathf.Lerp(1f, 0.4f, k));
                m_CrowdFilter.cutoffFrequency = Mathf.Lerp(22000f, 900f, k);
                m_Breath.volume = Mathf.Lerp(0.25f, 0.8f, k);
                m_Breath.pitch = Mathf.Lerp(0.95f, 1.25f, k);
                SetOverlay(0f, Mathf.Lerp(0.2f, 0.55f, k) * (0.75f + 0.25f * pulse));

                t += Time.deltaTime;
                KeepPlaced();
                yield return null;
            }
            if (m_Skip) yield break;

            // Dernier coup de batte : fracas, éclair rouge, noir net.
            m_Fx.pitch = 1f;
            var smash = m_SmashClip != null ? m_SmashClip : SfxBank.Pick(SfxBank.VerreVinCasse);
            if (smash == null) smash = SmashSynth.Create();
            m_Fx.PlayOneShot(smash, 1f);
            var heavy = SfxBank.Pick(SfxBank.ImpactLourd);
            if (heavy != null) m_Fx.PlayOneShot(heavy, 1f);
            Haptic(true, 1f, 0.3f); Haptic(false, 1f, 0.3f);
            m_Overlay.SetColor("_FadeColor", new Color(0.6f, 0.02f, 0.02f, 1f));
            SetOverlay(1f, 1f);
            yield return new WaitForSeconds(0.07f);
            m_Overlay.SetColor("_FadeColor", Color.black);
            SetOverlay(1f, 0f);
        }

        void Grunt(float strength)
        {
            // Souffle d'effort : la respiration repart, plus aiguë et plus forte.
            if (m_Fx == null || m_Breath.clip == null) return;
            m_Fx.pitch = Random.Range(1.25f, 1.45f);
            m_Fx.PlayOneShot(m_Breath.clip, Mathf.Lerp(0.4f, 0.9f, strength));
            Haptic(false, 0.25f * strength, 0.05f);
        }

        IEnumerator CrowdCut()
        {
            // L'écran regardé vient d'exploser : le son du match grésille et se coupe.
            for (float t = 0f; t < 0.25f; t += Time.deltaTime) { m_CrowdLevel = Mathf.Lerp(m_CrowdLevel, 0f, t / 0.25f); yield return null; }
            m_CrowdLevel = 0f;
        }

        IEnumerator CrowdSwell(float peak, float rise, float fall)
        {
            float t = 0f;
            while (t < rise + fall)
            {
                t += Time.deltaTime;
                m_CrowdLevel = t < rise ? Mathf.Lerp(1f, peak, t / rise) : Mathf.Lerp(peak, 1f, (t - rise) / fall);
                yield return null;
            }
            m_CrowdLevel = 1f;
        }

        IEnumerator FadeTo(float target, float duration)
        {
            float start = m_Overlay != null ? m_Overlay.GetFloat(FadeId) : 0f, vig = m_Overlay != null ? m_Overlay.GetFloat(VignetteId) : 0f;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                SetOverlay(Mathf.Lerp(start, target, t / duration), vig);
                yield return null;
            }
            SetOverlay(target, 0f);
        }

        public static void Haptic(bool left, float amplitude, float duration)
        {
            var device = InputDevices.GetDeviceAtXRNode(left ? XRNode.LeftHand : XRNode.RightHand);
            if (device.isValid) device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), duration);
        }

        void Update()
        {
            if (m_TvCrowd != null) m_TvCrowd.volume = m_CrowdVolume * m_CrowdLevel;
        }

        void LateUpdate()
        {
            // La vidéo des télés (TvVideo) remplace le matériau à sa première image : on garde le match à l'écran,
            // jusqu'à ce que l'écran casse (la télé met alors son propre écran mort).
            if (!m_KeepScreens || m_ScreenMat == null) return;
            foreach (var r in m_Anchors.Screens)
                if (r != null && r.sharedMaterial != m_ScreenMat) r.sharedMaterial = m_ScreenMat;
        }

        // ------------------------------------------------------------------ scènes

        IEnumerator LoadBar()
        {
            var bar = SceneManager.GetSceneByName(m_BarScene);
            if (!bar.isLoaded)
            {
                var op = SceneManager.LoadSceneAsync(m_BarScene, LoadSceneMode.Additive);
                if (op == null) { Debug.LogError($"[Fragile] Trailer : scène « {m_BarScene} » absente du build (Fragile > Trailer > Installer la scène Trailer)."); yield break; }
                while (!op.isDone) yield return null;
                bar = SceneManager.GetSceneByName(m_BarScene);
            }
            SceneManager.SetActiveScene(bar);              // éclairage, ciel et brouillard du bar
            LightProbes.TetrahedralizeAsync();
            yield return null;                             // laisse les Start() du bar s'exécuter
        }

        /// <summary>Le personnage a tout cassé : on décharge ce bar-là et on recharge le vrai, intact, pour le joueur.</summary>
        IEnumerator ReloadBar(float minBlack)
        {
            float until = Time.unscaledTime + minBlack;
            m_OverlayGo = null;   // détruit avec la caméra du bar
            m_Overlay = null;
            var bar = SceneManager.GetSceneByName(m_BarScene);
            // Pendant le rechargement il n'y a plus de caméra : un écouteur audio temporaire évite les avertissements.
            var listener = gameObject.AddComponent<AudioListener>();
            SceneManager.SetActiveScene(gameObject.scene);
            if (bar.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(bar);
                while (unload != null && !unload.isDone) yield return null;
            }
            yield return LoadBar();
            Destroy(listener);
            bar = SceneManager.GetSceneByName(m_BarScene);
            m_Origin = null;
            foreach (var root in bar.GetRootGameObjects())
                if ((m_Origin = root.GetComponentInChildren<XROrigin>(true)) != null) break;
            if (m_Origin != null) { CreateOverlay(); SetOverlay(1f, 0f); }
            // Dans le noir, on cale le joueur (le suivi du casque peut bouger pendant le chargement).
            do { PlaceForGameplay(); yield return null; } while (Time.unscaledTime < until);
            PlaceForGameplay();
        }

        /// <summary>
        /// Le joueur reprend la main là où le personnage s'est arrêté (un pas en arrière, pour ne pas être dans le tabouret),
        /// regard vers le comptoir. Seule la position au sol et l'orientation sont imposées : la hauteur reste la sienne.
        /// </summary>
        void PlaceForGameplay()
        {
            if (m_Origin == null || m_Origin.Camera == null || m_EndForward.sqrMagnitude < 1e-4f) return;
            var cam = m_Origin.Camera.transform;
            var fwd = Flat(cam.forward);
            if (fwd.sqrMagnitude > 1e-4f) m_Origin.RotateAroundCameraUsingOriginUp(Vector3.SignedAngle(fwd, m_EndForward, Vector3.up));
            var target = m_EndEye - m_EndForward.normalized * 0.35f;
            var delta = target - cam.position; delta.y = 0f;
            m_Origin.transform.position += delta;
        }

        /// <summary>Pendant l'intro : aucune arme dans le bar (armes à feu, batte), le personnage n'a que ses mains.
        /// Elles sont toutes là en jeu (le Bar est rechargé tel que Tom l'a fait).</summary>
        static void HideFirearms(Scene bar)
        {
            foreach (var f in FindObjectsByType<Fragile.Weapons.Firearm>(FindObjectsInactive.Exclude))
                if (f.gameObject.scene == bar) f.gameObject.SetActive(false);
            foreach (var b in FindObjectsByType<Breaker>(FindObjectsInactive.Exclude))
                if (b.gameObject.scene == bar && b.GetComponent<Fragile.Grab.GripSurface>() != null) b.gameObject.SetActive(false);
        }

        Vector3 HeadPosition => m_Origin != null && m_Origin.Camera != null ? m_Origin.Camera.transform.position : m_Anchors.Eye;

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.zero; }

        void UnloadTrailer()
        {
            m_Running = false;
            // Décharge la scène Trailer (et ce composant) : il ne reste que le Bar.
            if (SceneManager.sceneCount > 1) SceneManager.UnloadSceneAsync(gameObject.scene);
        }

        // ------------------------------------------------------------------ joueur

        void Place()
        {
            var cam = m_Origin.Camera != null ? m_Origin.Camera.transform : null;
            if (cam == null) return;
            m_Origin.MoveCameraToWorldLocation(m_Anchors.Eye);
            var fwd = cam.forward; fwd.y = 0f;
            var want = m_Anchors.LookAt - m_Anchors.Eye; want.y = 0f;
            if (fwd.sqrMagnitude > 1e-4f && want.sqrMagnitude > 1e-4f)
                m_Origin.RotateAroundCameraUsingOriginUp(Vector3.SignedAngle(fwd, want, Vector3.up));
        }

        /// <summary>
        /// Les petits mouvements de tête restent libres (indispensable en VR) ; seul un saut de plus de 45 cm
        /// (recentrage du casque, simulateur qui démarre) remet le joueur à sa place (sans le faire pivoter).
        /// </summary>
        void KeepPlaced()
        {
            var cam = m_Origin != null ? m_Origin.Camera : null;
            if (cam != null && Vector3.Distance(cam.transform.position, m_Anchors.Eye) > 0.45f)
                m_Origin.MoveCameraToWorldLocation(m_Anchors.Eye);
        }

        // ------------------------------------------------------------------ création

        void CreateOverlay()
        {
            m_OverlayGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_OverlayGo.name = "[Trailer] Voile";
            Destroy(m_OverlayGo.GetComponent<Collider>());
            m_OverlayGo.transform.SetParent(m_Origin.Camera.transform, false);
            m_OverlayGo.transform.localScale = Vector3.one * 0.6f;
            m_Overlay = new Material(m_OverlayShader) { name = "Voile du Trailer" };
            var r = m_OverlayGo.GetComponent<Renderer>();
            r.sharedMaterial = m_Overlay;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void SetOverlay(float fade, float vignette)
        {
            if (m_Overlay == null || m_OverlayGo == null) return;
            m_Overlay.SetFloat(FadeId, fade);
            m_Overlay.SetFloat(VignetteId, vignette);
            m_OverlayGo.SetActive(fade > 0.001f || vignette > 0.001f);
        }

        void CreateMatch()
        {
            var go = new GameObject("Faux match");
            go.transform.SetParent(transform, false);   // reste dans la scène Trailer (le Bar est la scène active)
            m_Match = go.AddComponent<FakeMatch>();
            m_Match.HomeName = m_HomeName; m_Match.AwayName = m_AwayName;
            m_Match.HomeColor = m_HomeColor; m_Match.AwayColor = m_AwayColor;
            m_Match.Goal += () => StartCoroutine(CrowdSwell(2.4f, 0.3f, 4f));
            m_Match.Build(m_UnlitShader);

            var video = m_Anchors.TvRoot.GetComponentInChildren<VideoPlayer>();
            if (video != null) video.Pause();

            m_ScreenMat = new Material(m_Anchors.Screens[0].sharedMaterial) { name = "Écran (Trailer)" };
            m_ScreenMat.SetTexture("_BaseMap", m_Match.Texture);
            if (m_ScreenMat.HasProperty("_EmissionMap")) m_ScreenMat.SetTexture("_EmissionMap", m_Match.Texture);
            foreach (var r in m_Anchors.Screens) r.sharedMaterial = m_ScreenMat;
            m_KeepScreens = true;
        }

        void CreateAudio()
        {
            var tvPos = m_Anchors.TvRoot.position;
            tvPos.y = m_Anchors.LookAt.y;
            m_TvCrowd = Source("Foule (télé)", tvPos, m_CrowdClip != null ? m_CrowdClip : TrailerSynth.Crowd(), true, true);
            m_CrowdFilter = m_TvCrowd.gameObject.AddComponent<AudioLowPassFilter>();
            m_CrowdFilter.cutoffFrequency = 22000f;
            m_TvCrowd.Play();
            m_Whistle = Source("Sifflet (télé)", tvPos, m_WhistleClip != null ? m_WhistleClip : TrailerSynth.Whistle(), false, true);
            m_Whistle.volume = 0.7f;
            m_Heart = Source("Cœur", Vector3.zero, m_HeartClip != null ? m_HeartClip : TrailerSynth.Heartbeat(), false, false);
            m_Breath = Source("Souffle", Vector3.zero, m_BreathClip != null ? m_BreathClip : TrailerSynth.Breath(), true, false);
            m_Fx = Source("Fracas", Vector3.zero, null, false, false);
        }

        AudioSource Source(string name, Vector3 pos, AudioClip clip, bool loop, bool spatial)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = loop; s.playOnAwake = false;
            s.spatialBlend = spatial ? 1f : 0f;
            s.minDistance = 2f; s.maxDistance = 20f; s.dopplerLevel = 0f;
            return s;
        }
    }
}
