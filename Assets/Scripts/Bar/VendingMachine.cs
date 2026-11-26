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
    /// Distributeur : une touche fait tomber une canette (moteur, puis « clonk ») devant la trappe du bas.
    /// Les canettes s'attrapent et se lancent ; au-delà de quelques-unes, les plus anciennes disparaissent.
    /// </summary>
    public class VendingMachine : MonoBehaviour
    {
        [SerializeField] GameObject[] m_Cans = new GameObject[0];
        [SerializeField] Vector3 m_Outlet = new Vector3(0.1f, 0.22f, 0.43f);
        [SerializeField] int m_Max = 6;

        readonly Queue<GameObject> m_Spawned = new Queue<GameObject>();
        bool m_Busy;

        public void Setup(GameObject[] cans, Vector3 outletLocal) { m_Cans = cans; m_Outlet = outletLocal; }

        public void Vend(int index)
        {
            Sfx.PlayAt(Sfx.Beep, transform.TransformPoint(m_Outlet + Vector3.up * 1f), 0.3f);
            if (m_Busy || m_Cans.Length == 0) return;
            StartCoroutine(Run(index));
        }

        IEnumerator Run(int index)
        {
            m_Busy = true;
            Sfx.PlayAt(Sfx.Motor, transform.TransformPoint(m_Outlet + Vector3.up * 0.8f), 0.5f);
            yield return new WaitForSeconds(0.85f);
            var prefab = m_Cans[index % m_Cans.Length];
            var pos = transform.TransformPoint(m_Outlet);
            var can = Instantiate(prefab, pos, transform.rotation * Quaternion.Euler(0f, 0f, 90f));
            if (can.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.linearVelocity = transform.forward * 0.35f + Vector3.down * 0.2f;
                rb.angularVelocity = transform.right * Random.Range(-2f, 2f);
            }
            if (!SfxBank.Play(SfxBank.ObjetTombe, pos, 0.8f, Random.Range(0.95f, 1.05f))) Sfx.PlayAt(Sfx.Clunk, pos, 0.9f);
            Track(m_Spawned, can, m_Max);
            yield return new WaitForSeconds(0.3f);
            m_Busy = false;
        }

        /// <summary>Garde au plus max objets créés : retire les plus anciens qui ne sont pas tenus en main.</summary>
        public static void Track(Queue<GameObject> list, GameObject go, int max)
        {
            list.Enqueue(go);
            int guard = list.Count;
            while (list.Count > max && guard-- > 0)
            {
                var old = list.Dequeue();
                if (old == null) continue;
                if (old.TryGetComponent<XRBaseInteractable>(out var i) && i.isSelected) { list.Enqueue(old); continue; }
                Destroy(old);
            }
        }
    }
}
