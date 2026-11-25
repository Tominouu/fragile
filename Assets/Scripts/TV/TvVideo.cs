using UnityEngine;
using UnityEngine.Video;

namespace Fragile.TV
{
    /// <summary>
    /// Vidéo des télés : glisser un fichier vidéo dans « Video » (objet Télés). Un seul décodage, partagé par tous les écrans.
    /// Sans vidéo (ou le temps qu'elle se prépare), l'image fixe du match reste affichée.
    /// </summary>
    [DisallowMultipleComponent]
    public class TvVideo : MonoBehaviour
    {
        [Tooltip("Fichier vidéo à diffuser (mp4 H.264, 1280x720, 30 i/s conseillé pour le Quest). Vide = image fixe.")]
        [SerializeField] VideoClip m_Video;
        [SerializeField] bool m_Loop = true;
        [Tooltip("Volume du son de la vidéo (0 = muet, pour laisser la place à la platine).")]
        [SerializeField, Range(0f, 1f)] float m_Volume = 0f;
        [Tooltip("Luminosité des écrans.")]
        [SerializeField, Range(0f, 2f)] float m_Brightness = 1f;
        [Tooltip("Cadrage : Fit Outside remplit l'écran (rogne), Fit Inside montre tout (bandes noires).")]
        [SerializeField] VideoAspectRatio m_Framing = VideoAspectRatio.FitOutside;
        [SerializeField] Renderer[] m_Screens = new Renderer[0];

        VideoPlayer m_Player;
        AudioSource m_Audio;
        RenderTexture m_Texture;
        Material m_Material;
        int m_Broken;

        public void SetScreens(Renderer[] screens) => m_Screens = screens;

        void Start()
        {
            if (m_Video == null || m_Screens.Length == 0) return;

            // 720p : les écrans n'affichent pas plus de détail en VR.
            m_Texture = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { name = "Vidéo télés", useMipMap = true, autoGenerateMips = true, anisoLevel = 4 };
            m_Texture.Create();

            m_Player = gameObject.AddComponent<VideoPlayer>();
            m_Player.playOnAwake = false;
            m_Player.source = VideoSource.VideoClip;
            m_Player.clip = m_Video;
            m_Player.isLooping = m_Loop;
            m_Player.renderMode = VideoRenderMode.RenderTexture;
            m_Player.targetTexture = m_Texture;
            m_Player.aspectRatio = m_Framing;
            m_Player.skipOnDrop = true;

            if (m_Video.audioTrackCount > 0)
            {
                // Son placé au centre du bloc de télés.
                var go = new GameObject("Son des télés");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(transform.position.x, m_Screens[0].bounds.center.y, transform.position.z);
                m_Audio = go.AddComponent<AudioSource>();
                m_Audio.playOnAwake = false;
                m_Audio.spatialBlend = 1f; m_Audio.dopplerLevel = 0f; m_Audio.minDistance = 1.5f; m_Audio.maxDistance = 25f;
                m_Audio.volume = m_Volume;
                m_Player.audioOutputMode = VideoAudioOutputMode.AudioSource;
                m_Player.controlledAudioTrackCount = 1;
                m_Player.EnableAudioTrack(0, true);
                m_Player.SetTargetAudioSource(0, m_Audio);
            }
            else m_Player.audioOutputMode = VideoAudioOutputMode.None;

            m_Player.errorReceived += (_, message) => Debug.LogWarning("[Fragile] Vidéo des télés : " + message);
            m_Player.sendFrameReadyEvents = true;
            m_Player.frameReady += OnFirstFrame;
            m_Player.prepareCompleted += p => p.Play();
            m_Player.Prepare();
        }

        /// <summary>Un écran vient d'être détruit : le son baisse d'autant, et la lecture s'arrête avec le dernier.</summary>
        public void ScreenBroken()
        {
            m_Broken++;
            if (m_Broken >= m_Screens.Length && m_Player != null) m_Player.Stop();
        }

        void OnFirstFrame(VideoPlayer player, long frame)
        {
            player.frameReady -= OnFirstFrame;
            player.sendFrameReadyEvents = false;
            // Matériau propre à la partie en cours : celui du projet (image fixe) n'est pas modifié.
            m_Material = new Material(m_Screens[0].sharedMaterial) { name = "Écran vidéo" };
            m_Material.SetTexture("_BaseMap", m_Texture);
            foreach (var s in m_Screens) if (s != null) s.sharedMaterial = m_Material;
        }

        void Update()
        {
            if (m_Audio != null) m_Audio.volume = m_Volume * (1f - m_Broken / (float)Mathf.Max(1, m_Screens.Length));
            if (m_Material != null) m_Material.SetColor("_BaseColor", new Color(m_Brightness, m_Brightness, m_Brightness, 1f));
        }

        void OnDestroy()
        {
            if (m_Texture != null) { m_Texture.Release(); Destroy(m_Texture); }
            if (m_Material != null) Destroy(m_Material);
        }
    }
}
