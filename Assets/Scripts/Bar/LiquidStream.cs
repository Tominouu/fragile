using Fragile.Audio;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fragile.Bar
{
    /// <summary>
    /// Filet de liquide (bière, café) qui coule vers le bas depuis cet objet, avec son d'écoulement.
    /// flow : 0 = fermé, 1 = plein débit. Particules étirées qui tombent jusqu'à la surface en dessous (hauteur mesurée).
    /// </summary>
    public class LiquidStream : MonoBehaviour
    {
        [SerializeField] Color m_Color = new Color(0.95f, 0.65f, 0.15f, 0.9f);
        [SerializeField] Color m_Foam = new Color(1f, 0.97f, 0.88f, 0.9f);
        [SerializeField] float m_Width = 0.012f;
        [SerializeField] float m_Rate = 140f;
        [SerializeField] Material m_Material;
        [SerializeField] float m_Volume = 0.6f;
        [Tooltip("Famille de sons enregistrés jouée en boucle (ex. Biere_Ecoulement). Vide ou absente : écoulement généré.")]
        [SerializeField] string m_LoopFamily = "";

        ParticleSystem m_Particles;
        AudioSource m_Sound;
        float m_Flow;

        public void Setup(Color color, Color foam, float width, Material material, string loopFamily = "")
        {
            m_Color = color; m_Foam = foam; m_Width = width; m_Material = material; m_LoopFamily = loopFamily;
        }

        void Awake()
        {
            m_Particles = Build();
            var clip = string.IsNullOrEmpty(m_LoopFamily) ? null : SfxBank.Pick(m_LoopFamily);
            m_Sound = Sfx.Loop(transform, clip != null ? clip : Sfx.Pour);
        }

        public float flow
        {
            get => m_Flow;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(value, m_Flow)) return;
                m_Flow = value;
                var e = m_Particles.emission;
                e.rateOverTime = m_Rate * value;
                if (value > 0f && !m_Particles.isPlaying) m_Particles.Play();
                if (value > 0f && !m_Sound.isPlaying) m_Sound.Play();
                m_Sound.volume = m_Volume * Mathf.Sqrt(value);
                m_Sound.pitch = Mathf.Lerp(0.8f, 1.05f, value);
                if (value <= 0f) { m_Sound.Stop(); }
            }
        }

        ParticleSystem Build()
        {
            // Hauteur de chute jusqu'à la surface en dessous : durée de vie des gouttes.
            float fall = Physics.Raycast(transform.position, Vector3.down, out var hit, 2f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 0.3f;
            float speed = 0.35f;
            float life = (-speed + Mathf.Sqrt(speed * speed + 2f * 9.81f * fall)) / 9.81f;

            var go = new GameObject("Filet");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false; main.loop = true;
            main.startLifetime = life;
            main.startSpeed = speed;
            main.startSize = new ParticleSystem.MinMaxCurve(m_Width * 0.8f, m_Width * 1.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(m_Color, Color.Lerp(m_Color, m_Foam, 0.3f));
            main.gravityModifier = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var em = ps.emission; em.rateOverTime = 0f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 1.5f; shape.radius = m_Width * 0.2f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = m_Material;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f; r.lengthScale = 2.5f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(true);
            return ps;
        }
    }
}
