using Fragile.Audio;
using UnityEngine;

namespace Fragile.Weapons
{
    /// <summary>
    /// Douille, cartouche ou chargeur qui tombe. Les sons enregistrés contiennent déjà toute la chute (premier choc et
    /// rebonds) : on en joue une au premier contact avec le décor, puis on se tait jusqu'à sa fin. Un nouveau choc franc
    /// ensuite (douille shootée, cartouche relâchée) rejoue une chute, plus ou moins fort selon la vitesse.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CasingSound : MonoBehaviour
    {
        /// <summary>Brass : petite douille (pistolet). RifleBrass : grosse douille (carabine). Plastic : calibre 12.</summary>
        public enum Matter { Brass, RifleBrass, Plastic, Magazine }

        public Matter matter = Matter.Brass;

        const float k_FirstSpeed = 0.5f;    // premier contact : la chute commence
        const float k_AgainSpeed = 1.2f;    // après la chute : seulement un vrai nouveau choc
        const float k_FullSpeed = 4f;

        float m_QuietUntil = -10f;
        bool m_Dropped;

        /// <summary>Douille réutilisée (pool) : elle retombe comme une neuve.</summary>
        public void ResetBounces() { m_Dropped = false; m_QuietUntil = -10f; }

        void Awake() => SfxBank.Preload(Family);

        string Family => matter switch
        {
            Matter.Plastic => SfxBank.DouillePlastique,
            Matter.Magazine => SfxBank.ObjetTombe,
            // Pistolet et carabine partagent la même chute de douille laiton (l'enregistrement pistolet sonnait faux).
            _ => SfxBank.DouilleCarabine,
        };

        void OnCollisionEnter(Collision c)
        {
            if (Time.time < m_QuietUntil) return;   // la chute en cours contient déjà les rebonds
            float v = c.relativeVelocity.magnitude;
            if (v < (m_Dropped ? k_AgainSpeed : k_FirstSpeed)) return;
            // Une douille qui en touche une autre dans l'air ne compte pas : seulement le décor (sol, comptoir, murs).
            if (c.rigidbody != null && c.rigidbody.TryGetComponent<CasingSound>(out _)) return;
            // Ni l'arme qu'elle frôle en sortant, ni le joueur.
            if (c.collider.GetComponentInParent<Firearm>() != null || c.collider.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() != null) return;

            float k = Mathf.Clamp01((v - k_FirstSpeed) / (k_FullSpeed - k_FirstSpeed));
            float volume = Mathf.Lerp(0.25f, 0.6f, k) * (matter == Matter.Magazine ? 1.2f : 1f);
            float pitch = Random.Range(0.94f, 1.06f);
            var point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            m_Dropped = true;

            var clip = SfxBank.Pick(Family);
            if (clip != null)
            {
                Sfx.PlayAt(clip, point, volume, pitch);
                m_QuietUntil = Time.time + clip.length / pitch * 0.9f;
                return;
            }
            // Banque absente : sons générés d'origine.
            m_QuietUntil = Time.time + 0.4f;
            if (matter == Matter.Plastic || matter == Matter.Magazine) Sfx.PlayAt(Sfx.Clunk, point, volume * 0.6f, matter == Matter.Plastic ? 2.2f : 1.4f);
            else Sfx.PlayAt(Sfx.ShellDrop, point, volume, pitch);
        }
    }
}
