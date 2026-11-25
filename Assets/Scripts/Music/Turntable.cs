using UnityEngine;

namespace Fragile.Music
{
    /// <summary>
    /// Platine vinyle : un bouton lance ou arrête, une molette règle le volume.
    /// Le disque prend de la vitesse, le bras se pose sur le sillon et avance avec le morceau,
    /// l'afficheur suit la musique ; à l'arrêt, le son ralentit comme un vrai plateau qu'on freine.
    /// </summary>
    public class Turntable : MonoBehaviour
    {
        [Header("Son")]
        [SerializeField] AudioSource m_Music;
        [SerializeField] AudioSource m_Crackle;
        [SerializeField, Range(0f, 1f)] float m_MaxVolume = 0.9f;

        [Header("Disque")]
        [SerializeField] Transform m_Record;
        [SerializeField] float m_Rpm = 33.33f;
        [Tooltip("Léger voile du disque (degrés).")]
        [SerializeField] float m_Wobble = 0.25f;

        [Header("Bras (repère de la platine)")]
        [SerializeField] Transform m_Arm;
        [SerializeField] Vector3 m_ArmPivot;
        [SerializeField] Vector3 m_ArmHead;
        [SerializeField] float m_OuterAngle, m_InnerAngle;
        [SerializeField] float m_PlayLift = 0.9f;
        [SerializeField] float m_MoveLift = 4f;

        [Header("Afficheur et voyant")]
        [SerializeField] Transform[] m_Bars = new Transform[0];
        [SerializeField] Renderer m_Led;
        [SerializeField] Material m_LedOff, m_LedOn;

        enum Phase { Stopped, Lifting, Moving, Lowering, Playing, Braking, Returning }

        Phase m_Phase = Phase.Stopped;
        bool m_On;
        float m_Speed;              // 0..1 : vitesse du plateau
        float m_Angle;              // rotation du disque
        float m_ArmAngle, m_Lift;   // bras
        float m_Volume = 0.6f, m_VolumeShown = -10f;
        bool m_Started;
        Quaternion m_RecordRest;
        Vector3 m_ArmRestPos; Quaternion m_ArmRestRot;
        readonly float[] m_Spectrum = new float[512];
        readonly float[] m_Levels = new float[16];
        float m_Peak = 0.01f;
        float[] m_BarHeights;

        public bool isOn => m_On;

        public void Setup(AudioSource music, AudioSource crackle, Transform record, Transform arm, Vector3 pivot, Vector3 head,
            float outer, float inner, Transform[] bars, Renderer led, Material ledOff, Material ledOn)
        {
            m_Music = music; m_Crackle = crackle; m_Record = record; m_Arm = arm; m_ArmPivot = pivot; m_ArmHead = head;
            m_OuterAngle = outer; m_InnerAngle = inner; m_Bars = bars; m_Led = led; m_LedOff = ledOff; m_LedOn = ledOn;
        }

        void Awake()
        {
            m_RecordRest = m_Record.localRotation;
            m_ArmRestPos = transform.InverseTransformPoint(m_Arm.position);
            m_ArmRestRot = Quaternion.Inverse(transform.rotation) * m_Arm.rotation;
            m_BarHeights = new float[m_Bars.Length];
            for (int i = 0; i < m_Bars.Length; i++) m_BarHeights[i] = m_Bars[i].localScale.y;
            m_Crackle.clip = CreateCrackle();
            m_Crackle.loop = true;
            SetBars();
        }

        /// <summary>Bouton marche / arrêt.</summary>
        public void Toggle()
        {
            m_On = !m_On;
            if (m_Led != null) m_Led.sharedMaterial = m_On ? m_LedOn : m_LedOff;
            // Redémarrage pendant le freinage : le bras n'a pas quitté le disque.
            if (m_On) m_Phase = m_Phase == Phase.Braking ? Phase.Playing : Phase.Lifting;
            else m_Phase = m_Phase == Phase.Playing ? Phase.Braking : Phase.Returning;
        }

        /// <summary>Molette de volume (0..1).</summary>
        public void SetVolume(float v)
        {
            if (Time.timeSinceLevelLoad > 1f) m_VolumeShown = Time.time;
            m_Volume = v;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            // Plateau : montée en ~1,2 s, freinage en ~1 s.
            m_Speed = Mathf.MoveTowards(m_Speed, m_On ? 1f : 0f, dt / (m_On ? 1.2f : 1f));
            UpdateArm(dt);
            UpdateSound();
            if (m_Speed > 0f)
            {
                m_Angle = Mathf.Repeat(m_Angle + m_Rpm * 6f * m_Speed * dt, 360f);
                // Horaire vu de dessus ; l'axe de voile tourne avec le disque.
                m_Record.localRotation = m_RecordRest * Quaternion.AngleAxis(m_Angle, Vector3.up) * Quaternion.AngleAxis(m_Wobble, Vector3.right);
            }
            SetBars();
        }

        float GrooveAngle
        {
            get
            {
                var clip = m_Music.clip;
                float t = m_Started && clip != null && clip.length > 0f ? m_Music.time / clip.length : 0f;
                return Mathf.Lerp(m_OuterAngle, m_InnerAngle, t);
            }
        }

        void UpdateArm(float dt)
        {
            float lift = m_Lift, angle = m_ArmAngle;
            switch (m_Phase)
            {
                case Phase.Stopped: return;
                case Phase.Lifting:
                    lift = m_MoveLift;
                    if (Mathf.Abs(m_Lift - lift) < 0.05f) m_Phase = Phase.Moving;
                    break;
                case Phase.Moving:
                    angle = GrooveAngle;
                    if (Mathf.Abs(Mathf.DeltaAngle(m_ArmAngle, angle)) < 0.05f) m_Phase = Phase.Lowering;
                    break;
                case Phase.Lowering:
                    lift = m_PlayLift;
                    if (Mathf.Abs(m_Lift - lift) < 0.05f) m_Phase = Phase.Playing;
                    break;
                case Phase.Playing:
                    angle = GrooveAngle; lift = m_PlayLift;
                    break;
                case Phase.Braking:
                    if (m_Speed <= 0f) m_Phase = Phase.Returning;
                    break;
                case Phase.Returning:
                    // Lever, revenir, reposer.
                    if (Mathf.Abs(m_ArmAngle) > 0.05f) { lift = m_MoveLift; if (Mathf.Abs(m_Lift - lift) < 0.3f) angle = 0f; }
                    else { angle = 0f; lift = 0f; if (m_Lift <= 0.01f) m_Phase = Phase.Stopped; }
                    break;
            }
            m_Lift = Mathf.MoveTowards(m_Lift, lift, 7f * dt);
            float gap = Mathf.Abs(Mathf.DeltaAngle(m_ArmAngle, angle));
            m_ArmAngle = Mathf.MoveTowardsAngle(m_ArmAngle, angle, Mathf.Clamp(gap * 4f, 1.5f, 26f) * dt);

            // Tourne autour du pivot du bras, puis se lève autour de l'axe horizontal perpendiculaire au bras.
            var yaw = Quaternion.AngleAxis(m_ArmAngle, Vector3.up);
            var reach = Vector3.ProjectOnPlane(yaw * (m_ArmHead - m_ArmPivot), Vector3.up).normalized;
            float bob = m_Phase == Phase.Playing ? Mathf.Sin(m_Angle * Mathf.Deg2Rad) * 0.06f : 0f; // suit le voile du disque
            var q = Quaternion.AngleAxis(m_Lift + bob, Vector3.Cross(reach, Vector3.up)) * yaw;
            m_Arm.SetPositionAndRotation(
                transform.TransformPoint(m_ArmPivot + q * (m_ArmRestPos - m_ArmPivot)),
                transform.rotation * (q * m_ArmRestRot));
        }

        void UpdateSound()
        {
            bool needleDown = m_Phase == Phase.Playing || m_Phase == Phase.Braking;
            float gain = m_Volume * m_Volume * m_MaxVolume; // au carré : réglage naturel à l'oreille

            if (needleDown && m_Speed > 0.02f)
            {
                if (!m_Music.isPlaying)
                {
                    if (m_Started) m_Music.UnPause(); else { m_Music.Play(); m_Started = true; }
                }
                if (!m_Crackle.isPlaying) m_Crackle.Play();
                m_Music.pitch = m_Crackle.pitch = Mathf.Max(0.02f, m_Speed);
                m_Music.volume = gain * Mathf.Clamp01(m_Speed * 2f);
                m_Crackle.volume = gain * 0.12f * m_Speed;
            }
            else
            {
                if (m_Music.isPlaying) m_Music.Pause();
                if (m_Crackle.isPlaying) m_Crackle.Pause();
            }
        }

        void SetBars()
        {
            int n = m_Bars.Length;
            if (n == 0) return;
            bool volume = Time.time - m_VolumeShown < 1.2f;
            bool live = m_Music.isPlaying;
            if (live && !volume)
            {
                m_Music.GetSpectrumData(m_Spectrum, 0, FFTWindow.Hanning);
                m_Peak = Mathf.Max(m_Peak * Mathf.Exp(-0.3f * Time.deltaTime), 1e-4f);
            }
            for (int i = 0; i < n; i++)
            {
                float target = 0f;
                if (volume) target = Mathf.Clamp01(m_Volume * n - i);   // jauge de volume pendant le réglage
                else if (live)
                {
                    // Bandes logarithmiques, des graves aux aigus.
                    int a = Mathf.Max(1, (int)Mathf.Pow(2f, 1f + i * 1.1f) - 1), b = Mathf.Min(511, (int)Mathf.Pow(2f, 1f + (i + 1) * 1.1f));
                    float sum = 0f;
                    for (int k = a; k <= b; k++) sum += m_Spectrum[k];
                    sum *= 1f + i * 0.5f;                                  // compense les aigus, plus faibles
                    m_Peak = Mathf.Max(m_Peak, sum);
                    target = Mathf.Pow(Mathf.Clamp01(sum / m_Peak), 0.6f);  // niveau relatif : indépendant du volume
                }
                m_Levels[i] = target > m_Levels[i] ? Mathf.Lerp(m_Levels[i], target, 1f - Mathf.Exp(-30f * Time.deltaTime))
                                                   : Mathf.MoveTowards(m_Levels[i], target, 2.2f * Time.deltaTime);
                var s = m_Bars[i].localScale;
                s.y = m_BarHeights[i] * Mathf.Max(0.001f, m_Levels[i]);
                m_Bars[i].localScale = s;
            }
        }

        /// <summary>Craquements de vinyle générés par le code (boucle).</summary>
        static AudioClip CreateCrackle()
        {
            const int rate = 22050, length = rate * 4;
            var d = new float[length];
            var rng = new System.Random(11);
            float lp = 0f;
            for (int i = 0; i < length; i++)
            {
                lp += ((float)rng.NextDouble() * 2f - 1f - lp) * 0.25f;
                d[i] = lp * 0.05f;
            }
            for (int k = 0; k < 70; k++)
            {
                int at = rng.Next(0, length - 200);
                float amp = 0.15f + 0.6f * Mathf.Pow((float)rng.NextDouble(), 3f);
                for (int i = 0; i < 160; i++) d[at + i] += ((float)rng.NextDouble() * 2f - 1f) * amp * Mathf.Exp(-i / 22f);
            }
            var clip = AudioClip.Create("Craquements vinyle", length, 1, rate, false);
            clip.SetData(d, 0);
            return clip;
        }
    }
}
