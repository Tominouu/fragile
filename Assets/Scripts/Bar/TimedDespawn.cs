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
    /// <summary>Objet temporaire (glaçon, canette) : fond ou disparaît après un moment s'il n'est pas tenu.</summary>
    public class TimedDespawn : MonoBehaviour
    {
        [SerializeField] float m_Lifetime = 40f;
        [SerializeField] bool m_Shrink = true;
        float m_Born;
        XRBaseInteractable m_Grab;

        void Start() { m_Born = Time.time; m_Grab = GetComponent<XRBaseInteractable>(); }

        void Update()
        {
            if (m_Grab != null && m_Grab.isSelected) { m_Born = Time.time; return; }
            float age = Time.time - m_Born;
            if (m_Shrink && age > m_Lifetime - 3f) transform.localScale = Vector3.one * Mathf.Clamp01((m_Lifetime - age) / 3f);
            if (age >= m_Lifetime) Destroy(gameObject);
        }
    }
}
