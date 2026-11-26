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
    /// <summary>Machine à café : chaque bouton lance un café sur son groupe (pompe, filet de café, vapeur).</summary>
    public class CoffeeMachine : MonoBehaviour
    {
        [SerializeField] LiquidStream[] m_Streams = new LiquidStream[0];
        [SerializeField] float m_Duration = 6f;

        readonly HashSet<int> m_Busy = new HashSet<int>();
        AudioSource m_Pump;

        public void Setup(LiquidStream[] streams) { m_Streams = streams; }

        void Awake() => m_Pump = Sfx.Loop(transform, Sfx.Pump);

        public void Brew(int side)
        {
            if (side < 0 || side >= m_Streams.Length || m_Busy.Contains(side)) return;
            Sfx.PlayAt(Sfx.Click, m_Streams[side].transform.position, 0.5f, 0.8f);
            StartCoroutine(Run(side));
        }

        IEnumerator Run(int side)
        {
            m_Busy.Add(side);
            m_Pump.volume = 0.35f; if (!m_Pump.isPlaying) m_Pump.Play();
            yield return new WaitForSeconds(0.8f);
            var s = m_Streams[side];
            for (float t = 0f; t < m_Duration; t += Time.deltaTime)
            {
                // Le café coule d'abord en filet fin, puis ralentit (« queue de souris »).
                s.flow = t < 0.6f ? t / 0.6f : Mathf.Lerp(1f, 0.35f, (t - 0.6f) / (m_Duration - 0.6f));
                yield return null;
            }
            s.flow = 0f;
            Sfx.PlayAt(Sfx.Steam, transform.position + Vector3.up * 0.3f, 0.35f);
            m_Busy.Remove(side);
            if (m_Busy.Count == 0) m_Pump.Stop();
        }
    }
}
