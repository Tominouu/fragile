using System.Collections.Generic;
using UnityEngine;

namespace Fragile.Mice
{
    /// <summary>
    /// Population de souris du bar : les fait sortir des trous dans les plinthes, garde leur nombre constant
    /// (une souris tuée ou rentrée dans un trou est remplacée après un moment), fournit des destinations
    /// (souvent le long des murs, comme les vraies) et les trous où fuir. Construit par Fragile > Ajouter les souris.
    /// </summary>
    public class MouseNest : MonoBehaviour
    {
        [SerializeField] Mouse m_Prefab;
        [Tooltip("Trous : position devant le trou, axe Z vers la salle.")]
        [SerializeField] Transform[] m_Holes;
        [SerializeField] int m_Population = 6;
        [SerializeField] Vector2 m_RespawnDelay = new Vector2(5f, 12f);
        [Tooltip("Zone où elles se promènent (x min, x max, z min, z max).")]
        [SerializeField] Vector4 m_Bounds = new Vector4(-4.85f, 4.85f, -4.85f, 8.2f);

        [Header("Sang")]
        [SerializeField] Material m_PoolMaterial;
        [SerializeField] Material m_SplatMaterial;
        [SerializeField] Material m_DropMaterial;

        readonly List<Mouse> m_Mice = new List<Mouse>();
        readonly List<float> m_Pending = new List<float>();

        void Awake() => BloodFx.Setup(m_PoolMaterial, m_SplatMaterial, m_DropMaterial);

        void Start()
        {
            // Premières sorties échelonnées.
            for (int i = 0; i < m_Population; i++) m_Pending.Add(Time.time + 0.5f + i * 0.8f);
        }

        void Update()
        {
            for (int i = m_Pending.Count - 1; i >= 0; i--)
            {
                if (Time.time < m_Pending[i]) continue;
                m_Pending.RemoveAt(i);
                Spawn();
            }
        }

        void Spawn()
        {
            if (m_Prefab == null || m_Holes == null || m_Holes.Length == 0) return;
            var hole = m_Holes[Random.Range(0, m_Holes.Length)];
            var mouse = Instantiate(m_Prefab, hole.position, hole.rotation, transform);
            mouse.Init(this);
            m_Mice.Add(mouse);
        }

        /// <summary>Souris rentrée dans un trou : elle ressortira (d'un autre trou) un peu plus tard.</summary>
        public void Hide(Mouse mouse)
        {
            Forget(mouse);
            Destroy(mouse.gameObject);
        }

        /// <summary>Souris disparue (rentrée ou morte puis effacée) : une autre la remplacera.</summary>
        public void Forget(Mouse mouse)
        {
            if (!m_Mice.Remove(mouse)) return;
            m_Pending.Add(Time.time + Random.Range(m_RespawnDelay.x, m_RespawnDelay.y));
        }

        public Vector3 Clamp(Vector3 p) => new Vector3(Mathf.Clamp(p.x, m_Bounds.x, m_Bounds.y), p.y, Mathf.Clamp(p.z, m_Bounds.z, m_Bounds.w));

        /// <summary>Destination : le plus souvent le long d'un mur, parfois à travers la salle.</summary>
        public Vector3 RandomPoint(Vector3 from)
        {
            const float margin = 0.12f;
            float x = Random.Range(m_Bounds.x + margin, m_Bounds.y - margin), z = Random.Range(m_Bounds.z + margin, m_Bounds.w - margin);
            if (Random.value < 0.65f)
            {
                switch (Random.Range(0, 4))
                {
                    case 0: x = m_Bounds.x + margin; break;
                    case 1: x = m_Bounds.y - margin; break;
                    case 2: z = m_Bounds.z + margin; break;
                    default: z = m_Bounds.w - margin; break;
                }
            }
            return new Vector3(x, from.y, z);
        }

        public Vector3 NearestHole(Vector3 p)
        {
            Transform best = null; float d = float.MaxValue;
            if (m_Holes != null)
                foreach (var h in m_Holes)
                {
                    float dd = (h.position - p).sqrMagnitude;
                    if (dd < d) { d = dd; best = h; }
                }
            return best != null ? best.position : p;
        }

        public bool IsHole(Vector3 p)
        {
            if (m_Holes != null) foreach (var h in m_Holes) if ((h.position - p).sqrMagnitude < 0.01f) return true;
            return false;
        }
    }
}
