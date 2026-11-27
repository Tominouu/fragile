using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Fragile.Trailer
{
    /// <summary>
    /// Faux match de foot diffusé sur les télés du bar pendant le Trailer, entièrement généré (aucune image ni vidéo sous droits).
    /// Un petit stade est construit très loin sous la scène (hors de vue du joueur) et filmé par une caméra « réalisateur télé »
    /// dans une RenderTexture 640x360. Le jeu est simulé simplement (passes, pressing, interceptions) ;
    /// les moments forts sont joués à la demande : <see cref="Chance"/> (poteau de l'équipe du joueur),
    /// <see cref="AwayGoal"/> (but adverse) et <see cref="EndMatch"/> (coup de sifflet final).
    /// </summary>
    public class FakeMatch : MonoBehaviour
    {
        const float HalfLength = 52.5f, HalfWidth = 34f, GoalHalf = 3.66f, GoalHeight = 2.44f;
        static readonly Vector3 StageOrigin = new Vector3(0f, -2000f, 0f);   // bien au-delà du plan lointain de la caméra du joueur

        public RenderTexture Texture { get; private set; }
        public event Action Goal;

        public string HomeName = "FRA", AwayName = "ADV";
        public Color HomeColor = new Color(0.78f, 0.08f, 0.1f), AwayColor = new Color(0.12f, 0.32f, 0.85f);
        public int HomeScore = 1, AwayScore = 1;
        [Tooltip("Secondes de match par seconde réelle : le Trailer couvre la fin du temps réglementaire en quelques secondes.")]
        public float ClockSpeed = 14f;
        public float Clock = 88f * 60f + 20f;

        enum Mode { Play, Scripted, Celebrate, Ended }

        class Player
        {
            public Transform T;
            public bool Home, Keeper;
            public float Depth, Lateral, Seed;
            public Vector3? Forced;
            public float Speed = 7f;
            public Vector3 Pos { get => T.localPosition; set => T.localPosition = value; }
        }

        Shader m_Shader;
        Transform m_Stage;
        Camera m_Cam;
        readonly List<Player> m_Players = new List<Player>();
        Transform m_Ball;
        Mode m_Mode = Mode.Play;
        bool m_Started;

        // Ballon : au pied d'un joueur, ou en vol entre deux points.
        Player m_Holder;
        bool m_Flying;
        Vector3 m_From, m_To;
        float m_FlightT, m_FlightDur, m_Arc;
        Player m_Receiver;
        float m_HoldUntil;

        // Réalisation.
        Vector3 m_CamLook;
        float m_Zoom = 1f, m_ZoomTarget = 1f;

        TextMeshPro m_ScoreText, m_ClockText, m_BannerText;
        GameObject m_Banner;
        Material m_BannerMat;
        readonly System.Random m_Rng = new System.Random(2026);
        float R => (float)m_Rng.NextDouble();

        // ------------------------------------------------------------------ construction

        public void Build(Shader unlit)
        {
            m_Shader = unlit;
            m_Stage = new GameObject("Stade (faux match)").transform;
            m_Stage.SetParent(transform, false);
            m_Stage.position = StageOrigin;

            BuildPitch();
            BuildStands();
            BuildGoals();
            BuildPlayers();

            m_Ball = Prim(PrimitiveType.Sphere, "Ballon", Mat(Color.white, 0.4f)).transform;
            m_Ball.localScale = Vector3.one * 0.7f;   // exagéré : lisible sur une télé à 2,5 m

            Texture = new RenderTexture(640, 360, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { name = "Match télé", useMipMap = true, autoGenerateMips = true, anisoLevel = 4, antiAliasing = 2 };
            Texture.Create();

            var camGo = new GameObject("Caméra télé");
            camGo.transform.SetParent(m_Stage, false);
            m_Cam = camGo.AddComponent<Camera>();
            m_Cam.targetTexture = Texture;
            m_Cam.fieldOfView = 30f;
            m_Cam.nearClipPlane = 0.1f;
            m_Cam.farClipPlane = 220f;
            m_Cam.clearFlags = CameraClearFlags.SolidColor;
            m_Cam.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
            m_Cam.allowHDR = false;
            m_Cam.allowMSAA = true;
            BuildOverlay(camGo.transform);

            GiveBall(Closest(Vector3.zero, true));
            m_CamLook = m_Ball.localPosition;
            m_Started = true;
        }

        void BuildPitch()
        {
            const int ppm = 10;                                // pixels par mètre
            const float marginX = 4f, marginZ = 4f;
            float sizeX = HalfLength * 2f + marginX * 2f, sizeZ = HalfWidth * 2f + marginZ * 2f;
            int w = Mathf.RoundToInt(sizeX * ppm), h = Mathf.RoundToInt(sizeZ * ppm);
            var px = new Color32[w * h];
            var rng = new System.Random(4);
            Color32 a = new Color32(52, 120, 46, 255), b = new Color32(62, 138, 54, 255), outside = new Color32(40, 92, 38, 255);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float wx = x / (float)ppm - sizeX / 2f, wz = y / (float)ppm - sizeZ / 2f;
                    bool inside = Mathf.Abs(wx) <= HalfLength && Mathf.Abs(wz) <= HalfWidth;
                    var c = inside ? (Mathf.FloorToInt((wx + HalfLength) / 7.5f) % 2 == 0 ? a : b) : outside;
                    int n = rng.Next(-6, 7);
                    px[y * w + x] = new Color32((byte)Mathf.Clamp(c.r + n, 0, 255), (byte)Mathf.Clamp(c.g + n, 0, 255), (byte)Mathf.Clamp(c.b + n, 0, 255), 255);
                }

            var white = new Color32(235, 240, 235, 255);
            void Dot(float wx, float wz)
            {
                int cx = Mathf.RoundToInt((wx + sizeX / 2f) * ppm), cy = Mathf.RoundToInt((wz + sizeZ / 2f) * ppm);
                for (int dy = -1; dy <= 0; dy++)
                    for (int dx = -1; dx <= 0; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = white;
                    }
            }
            void Segment(float x0, float z0, float x1, float z1)
            {
                int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(z1 - z0)) * ppm * 2f) + 1;
                for (int i = 0; i <= steps; i++) Dot(Mathf.Lerp(x0, x1, i / (float)steps), Mathf.Lerp(z0, z1, i / (float)steps));
            }
            void Box(float x0, float z0, float x1, float z1) { Segment(x0, z0, x1, z0); Segment(x1, z0, x1, z1); Segment(x1, z1, x0, z1); Segment(x0, z1, x0, z0); }
            void Circle(float cx, float cz, float r)
            {
                int steps = Mathf.CeilToInt(2f * Mathf.PI * r * ppm * 2f);
                for (int i = 0; i < steps; i++) { float t = i * 2f * Mathf.PI / steps; Dot(cx + Mathf.Cos(t) * r, cz + Mathf.Sin(t) * r); }
            }

            Box(-HalfLength, -HalfWidth, HalfLength, HalfWidth);
            Segment(0f, -HalfWidth, 0f, HalfWidth);
            Circle(0f, 0f, 9.15f);
            foreach (var s in new[] { -1f, 1f })
            {
                float gl = s * HalfLength;
                Box(gl, -20.16f, gl - s * 16.5f, 20.16f);
                Box(gl, -9.16f, gl - s * 5.5f, 9.16f);
                Dot(gl - s * 11f, 0f); Dot(gl - s * 11f + 0.1f, 0.1f);
            }
            Dot(0f, 0f); Dot(0.1f, 0.1f);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Terrain", anisoLevel = 8, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(true, true);

            var m = Mat(Color.white, 0f, tex);
            var pitch = Prim(PrimitiveType.Quad, "Terrain", m).transform;
            pitch.localRotation = Quaternion.Euler(90f, 0f, 0f);
            pitch.localScale = new Vector3(sizeX, sizeZ, 1f);
        }

        void BuildStands()
        {
            // Tribunes : pixels de couleurs = spectateurs vus de loin.
            var rng = new System.Random(8);
            int w = 512, h = 64;
            var px = new Color32[w * h];
            Color32[] shirts = { new Color32(200, 30, 35, 255), new Color32(230, 230, 230, 255), new Color32(30, 60, 160, 255), new Color32(40, 40, 45, 255), new Color32(220, 180, 60, 255), new Color32(90, 90, 100, 255) };
            for (int i = 0; i < px.Length; i++)
            {
                bool seat = rng.NextDouble() < 0.12;
                var c = seat ? new Color32(28, 30, 36, 255) : shirts[rng.Next(shirts.Length)];
                float dim = 0.45f + 0.35f * (float)rng.NextDouble();
                px[i] = new Color32((byte)(c.r * dim), (byte)(c.g * dim), (byte)(c.b * dim), 255);
            }
            var crowd = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Foule", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            crowd.SetPixels32(px); crowd.Apply(true, true);
            var mat = Mat(Color.white, 0f, crowd);

            const float slope = 26f, tilt = 35f;
            float up = Mathf.Cos(tilt * Mathf.Deg2Rad) * slope / 2f + 0.6f, back = Mathf.Sin(tilt * Mathf.Deg2Rad) * slope / 2f;
            void Stand(string name, float yaw, float dist, float width)
            {
                var q = Prim(PrimitiveType.Quad, name, mat).transform;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                q.localRotation = rot * Quaternion.Euler(tilt, 0f, 0f);
                q.localPosition = rot * new Vector3(0f, up, dist + back);
                q.localScale = new Vector3(width, slope, 1f);
            }
            Stand("Tribune face", 0f, HalfWidth + 5f, 140f);
            Stand("Tribune but est", 90f, HalfLength + 6f, 90f);
            Stand("Tribune but ouest", -90f, HalfLength + 6f, 90f);

            // Panneaux publicitaires le long de la ligne de touche d'en face.
            var ads = new Texture2D(16, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            for (int i = 0; i < 16; i++) ads.SetPixel(i, 0, i % 4 == 0 ? new Color(0.9f, 0.15f, 0.15f) : i % 4 == 2 ? new Color(0.95f, 0.95f, 0.95f) : new Color(0.08f, 0.08f, 0.12f));
            ads.Apply(false, true);
            var board = Prim(PrimitiveType.Quad, "Panneaux", Mat(Color.white, 0f, ads)).transform;
            board.localPosition = new Vector3(0f, 0.5f, HalfWidth + 2.5f);
            board.localScale = new Vector3(110f, 1f, 1f);
            board.GetComponent<Renderer>().sharedMaterial.SetTextureScale("_BaseMap", new Vector2(6f, 1f));
        }

        void BuildGoals()
        {
            var white = Mat(new Color(0.95f, 0.95f, 0.95f), 0.2f);
            var net = Mat(new Color(0.75f, 0.78f, 0.8f), 0f);
            foreach (var s in new[] { -1f, 1f })
            {
                float x = s * HalfLength;
                Box("Poteau", white, new Vector3(x, GoalHeight / 2f, -GoalHalf), new Vector3(0.15f, GoalHeight, 0.15f));
                Box("Poteau", white, new Vector3(x, GoalHeight / 2f, GoalHalf), new Vector3(0.15f, GoalHeight, 0.15f));
                Box("Barre", white, new Vector3(x, GoalHeight, 0f), new Vector3(0.15f, 0.15f, GoalHalf * 2f + 0.15f));
                Box("Filet", net, new Vector3(x + s * 2f, GoalHeight / 2f, 0f), new Vector3(0.03f, GoalHeight, GoalHalf * 2f));
                Box("Filet haut", net, new Vector3(x + s * 1f, GoalHeight, 0f), new Vector3(2f, 0.03f, GoalHalf * 2f));
            }
        }

        void BuildPlayers()
        {
            // 4-4-2 : profondeur (0 = sa ligne de but, 1 = celle d'en face) et position latérale (m).
            var slots = new (float d, float z)[]
            {
                (0.02f, 0f),
                (0.22f, -22f), (0.2f, -8f), (0.2f, 8f), (0.22f, 22f),
                (0.42f, -24f), (0.4f, -8f), (0.4f, 8f), (0.42f, 24f),
                (0.6f, -7f), (0.6f, 7f),
            };
            var skin = Mat(new Color(0.85f, 0.65f, 0.5f), 0.3f);
            foreach (bool home in new[] { true, false })
            {
                var shirt = Mat(home ? HomeColor : AwayColor, 0.35f);
                var keeper = Mat(home ? new Color(0.95f, 0.85f, 0.1f) : new Color(0.2f, 0.8f, 0.3f), 0.35f);
                for (int i = 0; i < slots.Length; i++)
                {
                    var root = new GameObject(home ? $"Domicile {i + 1}" : $"Extérieur {i + 1}").transform;
                    root.SetParent(m_Stage, false);
                    root.localScale = Vector3.one * 1.3f;   // lisibles sur un petit écran
                    var body = Prim(PrimitiveType.Capsule, "Corps", i == 0 ? keeper : shirt, root).transform;
                    body.localPosition = new Vector3(0f, 0.85f, 0f);
                    body.localScale = new Vector3(0.7f, 0.85f, 0.7f);
                    var head = Prim(PrimitiveType.Sphere, "Tête", skin, root).transform;
                    head.localPosition = new Vector3(0f, 1.86f, 0f);
                    head.localScale = Vector3.one * 0.34f;

                    var p = new Player { T = root, Home = home, Keeper = i == 0, Depth = slots[i].d, Lateral = slots[i].z, Seed = R * 100f };
                    p.Pos = SlotPosition(p, Vector3.zero, home);
                    m_Players.Add(p);
                }
            }
        }

        void BuildOverlay(Transform cam)
        {
            // Habillage télé : score en haut à gauche, bandeau au centre (BUT !, TERMINÉ). Champ fixe de 30° : à 1 m,
            // l'image fait 0,536 m de haut et 0,953 m de large.
            var dark = Mat(new Color(0.05f, 0.06f, 0.1f), 0f);
            var clockBg = Mat(new Color(0.85f, 0.85f, 0.88f), 0f);
            Panel(cam, dark, new Vector3(-0.33f, 0.222f, 1f), new Vector2(0.24f, 0.05f));
            Panel(cam, clockBg, new Vector3(-0.165f, 0.222f, 1f), new Vector2(0.09f, 0.05f));
            m_ScoreText = Text(cam, new Vector3(-0.33f, 0.222f, 0.995f), new Vector2(0.22f, 0.042f), Color.white);
            m_ClockText = Text(cam, new Vector3(-0.165f, 0.222f, 0.995f), new Vector2(0.08f, 0.04f), new Color(0.05f, 0.06f, 0.1f));

            m_BannerMat = Mat(AwayColor, 0f);
            m_Banner = Panel(cam, m_BannerMat, new Vector3(0f, -0.13f, 1f), new Vector2(0.62f, 0.12f));
            m_BannerText = Text(cam, new Vector3(0f, -0.13f, 0.995f), new Vector2(0.58f, 0.1f), Color.white);
            ShowBanner(null, Color.clear);
        }

        GameObject Panel(Transform parent, Material m, Vector3 pos, Vector2 size)
        {
            var q = Prim(PrimitiveType.Quad, "Panneau", m, parent);
            q.transform.localPosition = pos;
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            return q;
        }

        static TextMeshPro Text(Transform parent, Vector3 pos, Vector2 size, Color color)
        {
            var go = new GameObject("Texte");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshPro>();
            t.rectTransform.sizeDelta = size;
            go.transform.localPosition = pos;
            t.enableAutoSizing = true;
            t.fontSizeMin = 0.01f;
            t.fontSizeMax = 20f;
            t.alignment = TextAlignmentOptions.Center;
            t.fontStyle = FontStyles.Bold;
            t.color = color;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.text = "";
            return t;
        }

        void ShowBanner(string text, Color color)
        {
            bool on = !string.IsNullOrEmpty(text);
            m_Banner.SetActive(on);
            m_BannerText.gameObject.SetActive(on);
            if (!on) return;
            m_BannerMat.SetColor("_BaseColor", color);
            m_BannerText.text = text;
        }

        Material Mat(Color c, float shade, Texture tex = null)
        {
            var m = new Material(m_Shader) { name = "Match" };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Shade", shade);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            return m;
        }

        GameObject Prim(PrimitiveType type, string name, Material m, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent != null ? parent : m_Stage, false);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go;
        }

        void Box(string name, Material m, Vector3 pos, Vector3 size)
        {
            var t = Prim(PrimitiveType.Cube, name, m).transform;
            t.localPosition = pos;
            t.localScale = size;
        }

        // ------------------------------------------------------------------ moments forts

        /// <summary>Grosse occasion de l'équipe du joueur : centre, frappe... sur le poteau.</summary>
        public void Chance() { if (m_Mode == Mode.Play) StartCoroutine(ChanceRoutine()); }

        /// <summary>Contre adverse et but : l'équipe du joueur perd.</summary>
        public void AwayGoal() { if (m_Mode != Mode.Ended) { StopAllCoroutines(); StartCoroutine(AwayGoalRoutine()); } }

        /// <summary>Coup de sifflet final : bandeau « TERMINÉ », les joueurs s'arrêtent.</summary>
        public void EndMatch()
        {
            StopAllCoroutines();
            m_Mode = Mode.Ended;
            m_ZoomTarget = 1f;
            ShowBanner($"TERMINÉ   {HomeName} {HomeScore} - {AwayScore} {AwayName}", new Color(0.08f, 0.09f, 0.14f));
            foreach (var p in m_Players)
            {
                p.Speed = p.Home ? 0.8f : 2.5f;
                p.Forced = p.Pos + new Vector3(R * 6f - 3f, 0f, R * 6f - 3f);
            }
        }

        IEnumerator ChanceRoutine()
        {
            m_Mode = Mode.Scripted;
            m_ZoomTarget = 0.72f;
            var winger = Pick(true, 0.42f, 24f);
            var striker = Pick(true, 0.6f, -7f);
            // Le ballon file sur l'aile, centre en retrait, reprise... poteau.
            yield return KickTo(winger, new Vector3(30f, 0f, 22f), 1.1f, 1.2f);
            winger.Forced = new Vector3(40f, 0f, 25f);
            yield return Dribble(winger, 1.0f);
            striker.Forced = new Vector3(41f, 0f, -2f);
            yield return KickTo(striker, new Vector3(41f, 0f, -2f), 0.9f, 3.5f);
            yield return new WaitForSeconds(0.25f);
            yield return Fly(new Vector3(HalfLength, 1.3f, GoalHalf), 0.42f, 0.6f);          // poteau !
            yield return Fly(new Vector3(44f, 0f, 15f), 0.6f, 1.6f);
            var defender = Closest(m_Ball.localPosition, false);
            GiveBall(defender);
            ReleaseForced();
            m_ZoomTarget = 1f;
            m_Mode = Mode.Play;
        }

        IEnumerator AwayGoalRoutine()
        {
            m_Mode = Mode.Scripted;
            ReleaseForced();
            m_ZoomTarget = 0.8f;
            var mid = Pick(false, 0.4f, 8f);
            var striker = Pick(false, 0.6f, -7f);
            mid.Forced = new Vector3(-12f, 0f, 6f);
            striker.Forced = new Vector3(-30f, 0f, -6f);
            striker.Speed = 9f;
            yield return KickTo(mid, new Vector3(-12f, 0f, 6f), 1.0f, 1.5f);
            mid.Forced = new Vector3(-22f, 0f, 4f);
            yield return Dribble(mid, 1.1f);
            striker.Forced = new Vector3(-38f, 0f, -5f);
            yield return KickTo(striker, new Vector3(-38f, 0f, -5f), 0.85f, 0.5f);
            m_ZoomTarget = 0.62f;
            striker.Forced = new Vector3(-41f, 0f, -4f);
            yield return Dribble(striker, 0.5f);
            // Frappe croisée, le gardien plonge trop tard.
            var keeper = m_Players.Find(p => p.Home && p.Keeper);
            keeper.Forced = new Vector3(-HalfLength + 1.2f, 0f, -1f);
            yield return Fly(new Vector3(-HalfLength - 0.6f, 0.9f, 2.4f), 0.45f, 0.4f);
            yield return Fly(new Vector3(-HalfLength - 1.8f, 0.2f, 2.2f), 0.2f, 0f);         // au fond des filets
            AwayScore++;
            Goal?.Invoke();
            m_Mode = Mode.Celebrate;
            ShowBanner($"BUT !   {AwayName}", AwayColor);
            var corner = new Vector3(-HalfLength + 4f, 0f, HalfWidth - 3f);
            foreach (var p in m_Players)
            {
                if (p.Home) { p.Speed = 1f; p.Forced = p.Pos + new Vector3(R * 2f - 1f, 0f, R * 2f - 1f); }
                else if (!p.Keeper) { p.Speed = 8f; p.Forced = corner + new Vector3(R * 6f - 3f, 0f, R * 4f - 2f); }
            }
            yield return new WaitForSeconds(3f);
            ShowBanner(null, Color.clear);
            m_ZoomTarget = 1f;
        }

        Player Pick(bool home, float depth, float lateral)
        {
            Player best = null; float d = float.MaxValue;
            foreach (var p in m_Players)
            {
                if (p.Home != home) continue;
                float s = Mathf.Abs(p.Depth - depth) * 100f + Mathf.Abs(p.Lateral - lateral);
                if (s < d) { d = s; best = p; }
            }
            return best;
        }

        void ReleaseForced() { foreach (var p in m_Players) { p.Forced = null; p.Speed = 7f; } }

        IEnumerator KickTo(Player receiver, Vector3 target, float duration, float arc)
        {
            receiver.Forced = target;
            StartFlight(target, duration, arc, receiver);
            while (m_Flying) yield return null;
        }

        IEnumerator Fly(Vector3 target, float duration, float arc)
        {
            StartFlight(target, duration, arc, null);
            while (m_Flying) yield return null;
        }

        IEnumerator Dribble(Player p, float seconds)
        {
            GiveBall(p);
            m_HoldUntil = float.MaxValue;
            yield return new WaitForSeconds(seconds);
        }

        // ------------------------------------------------------------------ simulation

        void GiveBall(Player p)
        {
            m_Holder = p;
            m_Flying = false;
            m_Receiver = null;
            m_HoldUntil = Time.time + 0.5f + R * 1.1f;
        }

        void StartFlight(Vector3 to, float duration, float arc, Player receiver)
        {
            m_From = m_Ball.localPosition;
            m_To = to;
            m_FlightT = 0f;
            m_FlightDur = Mathf.Max(0.05f, duration);
            m_Arc = arc;
            m_Receiver = receiver;
            m_Holder = null;
            m_Flying = true;
        }

        void Update()
        {
            if (!m_Started) return;
            float dt = Time.deltaTime;
            if (m_Mode != Mode.Ended) Clock += dt * ClockSpeed;

            UpdateBall(dt);
            if (m_Mode == Mode.Play) PlayAI();
            UpdatePlayers(dt);
            UpdateCamera(dt);
            UpdateHud();
        }

        void UpdateBall(float dt)
        {
            if (m_Flying)
            {
                m_FlightT += dt / m_FlightDur;
                float t = Mathf.Clamp01(m_FlightT);
                var p = Vector3.Lerp(m_From, m_To, t);
                p.y = Mathf.Lerp(m_From.y, m_To.y, t) + m_Arc * 4f * t * (1f - t);
                m_Ball.localPosition = p + Vector3.up * 0.45f;
                if (t >= 1f)
                {
                    m_Flying = false;
                    if (m_Receiver != null) GiveBall(m_Receiver);
                }
            }
            else if (m_Holder != null)
            {
                var dir = m_Holder.Home ? Vector3.right : Vector3.left;
                var target = m_Holder.Pos + dir * 0.9f + Vector3.up * 0.45f;
                m_Ball.localPosition = Vector3.Lerp(m_Ball.localPosition, target, 1f - Mathf.Exp(-dt * 12f));
            }
        }

        void PlayAI()
        {
            if (m_Flying || m_Holder == null || Time.time < m_HoldUntil) return;
            var h = m_Holder;
            float dir = h.Home ? 1f : -1f;
            Player target = null; float best = float.NegativeInfinity;
            foreach (var p in m_Players)
            {
                if (p.Home != h.Home || p == h || p.Keeper) continue;
                var d = p.Pos - h.Pos;
                float dist = d.magnitude;
                if (dist < 7f || dist > 32f) continue;
                if (p.Pos.x * dir > 36f) continue;                       // pas de tir hors scénario
                float score = Vector3.Dot(d.normalized, Vector3.right * dir) * 0.8f + R;
                if (score > best) { best = score; target = p; }
            }
            if (target == null) { m_HoldUntil = Time.time + 0.4f; return; }

            var to = target.Pos + (h.Home ? Vector3.right : Vector3.left) * 2f;
            to.x = Mathf.Clamp(to.x, -HalfLength + 3f, HalfLength - 3f);
            to.z = Mathf.Clamp(to.z, -HalfWidth + 2f, HalfWidth - 2f);
            // Interception : un adversaire proche de l'arrivée récupère parfois le ballon.
            var thief = Closest(to, !h.Home);
            var receiver = (thief != null && (thief.Pos - to).magnitude < 5f && R < 0.3f) ? thief : target;
            StartFlight(to, Vector3.Distance(h.Pos, to) / 19f, R < 0.25f ? 2.5f : 0.3f, receiver);
        }

        Vector3 SlotPosition(Player p, Vector3 ball, bool inPossession)
        {
            float dir = p.Home ? 1f : -1f;
            float x = Mathf.Lerp(-HalfLength, HalfLength, p.Home ? p.Depth : 1f - p.Depth);
            float z = p.Lateral;
            if (p.Keeper)
                return new Vector3(x, 0f, Mathf.Clamp(ball.z * 0.12f, -2.5f, 2.5f));
            x += Mathf.Clamp(ball.x * 0.55f, -24f, 24f) + (inPossession ? dir * 6f : -dir * 3f);
            z += ball.z * 0.25f;
            float t = Time.time * 0.3f;
            x += (Mathf.PerlinNoise(p.Seed, t) - 0.5f) * 5f;
            z += (Mathf.PerlinNoise(t, p.Seed) - 0.5f) * 5f;
            return new Vector3(Mathf.Clamp(x, -HalfLength + 1f, HalfLength - 1f), 0f, Mathf.Clamp(z, -HalfWidth + 1f, HalfWidth - 1f));
        }

        void UpdatePlayers(float dt)
        {
            var ball = m_Ball.localPosition; ball.y = 0f;
            bool homeHasBall = m_Holder != null ? m_Holder.Home : (m_Receiver != null ? m_Receiver.Home : true);
            var presserHome = Closest(ball, true);
            var presserAway = Closest(ball, false);

            foreach (var p in m_Players)
            {
                Vector3 target;
                if (p.Forced.HasValue) target = p.Forced.Value;
                else if (m_Mode == Mode.Play && p == m_Holder) target = p.Pos + (p.Home ? Vector3.right : Vector3.left) * 4f;
                else if (m_Mode == Mode.Play && p == m_Receiver) target = m_To;
                else if (m_Mode == Mode.Play && !p.Keeper && (p == presserHome && !homeHasBall || p == presserAway && homeHasBall)) target = ball;
                else target = SlotPosition(p, ball, p.Home == homeHasBall);

                var pos = p.Pos;
                var next = Vector3.MoveTowards(pos, target, p.Speed * dt);
                var vel = (next - pos) / Mathf.Max(dt, 1e-4f);
                p.Pos = next;
                var look = vel.sqrMagnitude > 0.2f ? vel : ball - next;
                look.y = 0f;
                if (look.sqrMagnitude > 1e-3f)
                    p.T.localRotation = Quaternion.Slerp(p.T.localRotation, Quaternion.LookRotation(look), 1f - Mathf.Exp(-dt * 8f));
                // Petit rebond de course.
                var body = p.T.GetChild(0);
                float run = Mathf.Clamp01(vel.magnitude / 7f);
                body.localPosition = new Vector3(0f, 0.85f + Mathf.Abs(Mathf.Sin(Time.time * 11f + p.Seed)) * 0.08f * run, 0f);
            }
        }

        Player Closest(Vector3 point, bool home)
        {
            Player best = null; float d = float.MaxValue;
            foreach (var p in m_Players)
            {
                if (p.Home != home || p.Keeper) continue;
                float s = (p.Pos - point).sqrMagnitude;
                if (s < d) { d = s; best = p; }
            }
            return best;
        }

        void UpdateCamera(float dt)
        {
            var b = m_Ball.localPosition;
            var target = new Vector3(Mathf.Clamp(b.x, -40f, 40f), 0f, Mathf.Clamp(b.z * 0.4f, -10f, 10f));
            m_CamLook = Vector3.Lerp(m_CamLook, target, 1f - Mathf.Exp(-dt * 2.2f));
            m_Zoom = Mathf.Lerp(m_Zoom, m_ZoomTarget, 1f - Mathf.Exp(-dt * 1.5f));
            m_Cam.transform.localPosition = m_CamLook + new Vector3(0f, 24f, -48f) * m_Zoom;
            m_Cam.transform.LookAt(m_Cam.transform.parent.TransformPoint(m_CamLook));
        }

        int m_LastSecond = -1;
        void UpdateHud()
        {
            int s = Mathf.FloorToInt(Clock);
            if (s == m_LastSecond) return;
            m_LastSecond = s;
            m_ScoreText.text = $"{HomeName}  {HomeScore} - {AwayScore}  {AwayName}";
            int min = s / 60, sec = s % 60;
            m_ClockText.text = min >= 90 ? $"90+{min - 89}'" : $"{min:00}:{sec:00}";
        }

        void OnDestroy()
        {
            if (Texture != null) { Texture.Release(); Destroy(Texture); }
        }
    }
}
