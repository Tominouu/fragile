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
    /// <summary>Tireuse : chaque levier ouvre son robinet, le débit suit l'angle du levier.</summary>
    public class BeerTap : MonoBehaviour
    {
        [SerializeField] PullLever[] m_Levers = new PullLever[0];
        [SerializeField] LiquidStream[] m_Streams = new LiquidStream[0];

        public void Setup(PullLever[] levers, LiquidStream[] streams) { m_Levers = levers; m_Streams = streams; }

        void Update()
        {
            for (int i = 0; i < m_Levers.Length && i < m_Streams.Length; i++)
                if (m_Levers[i] != null && m_Streams[i] != null)
                    m_Streams[i].flow = Mathf.InverseLerp(0.2f, 0.9f, m_Levers[i].value);
        }
    }
}
