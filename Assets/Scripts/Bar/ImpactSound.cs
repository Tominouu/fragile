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
    /// <summary>Petit tintement quand un glaçon ou une canette heurte quelque chose.</summary>
    public class ImpactSound : MonoBehaviour
    {
        [SerializeField] bool m_Ice = true;
        float m_Last;

        void OnCollisionEnter(Collision c)
        {
            float v = c.relativeVelocity.magnitude;
            if (v < 0.6f || Time.time - m_Last < 0.08f) return;
            m_Last = Time.time;
            Sfx.PlayAt(m_Ice ? Sfx.Clink : Sfx.Clunk, transform.position, Mathf.Clamp01(v / 4f) * (m_Ice ? 0.5f : 0.35f), m_Ice ? Random.Range(0.9f, 1.2f) : Random.Range(1.6f, 2f));
        }
    }
}
