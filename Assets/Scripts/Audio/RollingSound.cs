using UnityEngine;

namespace Fragile.Audio
{
    /// <summary>
    /// Son d'un objet qui roule par terre (bouteille) : il démarre quand l'objet tourne sur lui-même en touchant
    /// une surface, suit sa vitesse (volume, hauteur) et s'éteint en douceur quand il s'arrête ou décolle.
    /// La source audio n'est créée qu'au premier roulement (des dizaines de bouteilles dans le bar).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RollingSound : MonoBehaviour
    {
        public string family = SfxBank.BouteilleRoulement;

        const float k_MinSpeed = 0.12f, k_MinSpin = 2.5f, k_FullSpeed = 1.6f;

        Rigidbody m_Body;
        AudioSource m_Source;
        float m_LastContact = -10f, m_Volume;

        void Awake() => m_Body = GetComponent<Rigidbody>();

        void OnCollisionStay(Collision c) => m_LastContact = Time.time;
        void OnCollisionEnter(Collision c) => m_LastContact = Time.time;

        void FixedUpdate()
        {
            if (m_Body == null) return;
            if (m_Source == null && (m_Body.isKinematic || m_Body.IsSleeping())) return;

            float speed = m_Body.isKinematic ? 0f : m_Body.linearVelocity.magnitude;
            float spin = m_Body.isKinematic ? 0f : m_Body.angularVelocity.magnitude;
            bool rolling = Time.time - m_LastContact < 0.1f && speed > k_MinSpeed && spin > k_MinSpin;
            float target = rolling ? Mathf.Lerp(0.15f, 0.7f, speed / k_FullSpeed) : 0f;
            m_Volume = Mathf.MoveTowards(m_Volume, target, Time.fixedDeltaTime * (rolling ? 3f : 2.5f));

            if (m_Volume > 0.01f)
            {
                if (m_Source == null) m_Source = MakeSource();
                if (!m_Source.isPlaying)
                {
                    var clip = SfxBank.Pick(family);
                    if (clip == null) { enabled = false; return; }   // banque absente : rien à jouer
                    m_Source.clip = clip;
                    m_Source.Play();
                }
                m_Source.volume = m_Volume;
                m_Source.pitch = Mathf.Lerp(0.85f, 1.15f, Mathf.Clamp01(speed / k_FullSpeed));
            }
            else if (m_Source != null && m_Source.isPlaying) m_Source.Stop();
        }

        AudioSource MakeSource()
        {
            var go = new GameObject("Son roulement");
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            Sfx.Spatialize(src);
            src.playOnAwake = false; src.loop = false; src.volume = 0f;
            return src;
        }
    }
}
