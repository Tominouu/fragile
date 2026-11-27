using System.Collections;
using System.Collections.Generic;
using Fragile.Audio;
using Fragile.Grab;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fragile.Bar
{
    /// <summary>
    /// Glaçon : petit tintement à chaque choc. Canette : elle tombe, s'écrase sous un coup violent (batte, lancer),
    /// et une balle la perce (jet qui siffle et la suit dans sa course).
    /// </summary>
    public class ImpactSound : MonoBehaviour, Fragile.Breakables.IHittable
    {
        [SerializeField] bool m_Ice = true;
        float m_Last;
        bool m_Punctured;

        const float k_CrushSpeed = 4.5f;

        /// <summary>Glaçon qui tinte : son enregistré (famille Glacon), sinon le tintement généré.</summary>
        public static void PlayIce(Vector3 point, float volume, float pitch)
        {
            if (!SfxBank.Play(SfxBank.Glacon, point, volume, pitch)) Sfx.PlayAt(Sfx.Clink, point, volume, pitch);
        }

        void Awake()
        {
            if (m_Ice) SfxBank.Preload(SfxBank.Glacon);
            if (!m_Ice) SfxBank.Preload(SfxBank.ObjetTombe, SfxBank.CannetteEcrasee, SfxBank.CannettePercee);
        }

        void OnCollisionEnter(Collision c)
        {
            float v = c.relativeVelocity.magnitude;
            if (v < 0.6f || Time.time - m_Last < 0.08f) return;
            m_Last = Time.time;
            if (m_Ice) { PlayIce(transform.position, Mathf.Clamp01(v / 4f) * 0.5f, Random.Range(0.9f, 1.2f)); return; }

            var point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            if (v >= k_CrushSpeed && SfxBank.Play(SfxBank.CannetteEcrasee, point, Mathf.Lerp(0.6f, 1f, (v - k_CrushSpeed) / 5f), Random.Range(0.95f, 1.08f))) return;
            if (SfxBank.Play(SfxBank.ObjetTombe, point, Mathf.Clamp01(v / 4f) * 0.55f, Random.Range(1.15f, 1.35f))) return;
            Sfx.PlayAt(Sfx.Clunk, transform.position, Mathf.Clamp01(v / 4f) * 0.35f, Random.Range(1.6f, 2f));
        }

        /// <summary>Touchée par une balle.</summary>
        public void Hit(Vector3 point, Vector3 direction, float energy, float push)
        {
            if (m_Ice) { PlayIce(point, 0.6f, Random.Range(0.8f, 1f)); return; }
            SfxBank.Play(SfxBank.CannetteEcrasee, point, 0.8f, Random.Range(1f, 1.15f));
            if (m_Punctured) return;
            m_Punctured = true;
            SfxBank.PlayAttached(SfxBank.CannettePercee, transform, 0.7f, Random.Range(0.95f, 1.05f));
        }
    }
}
