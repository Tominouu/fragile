using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Fragile.Breakables
{
    /// <summary>
    /// Effet de casse réutilisable (préfab construit par Fragile > Rendre cassable) :
    /// éclats de céramique en 3D, poussière, gerbe de liquide avec éclaboussures au sol.
    /// Tout s'adapte à l'énergie du coup (0 = coup mou, 1 = coup violent). Instances mises en commun (pool) :
    /// aucune création d'objet pendant le jeu.
    /// </summary>
    public class BreakFx : MonoBehaviour
    {
        [SerializeField] ParticleSystem m_Chips;
        [SerializeField] ParticleSystem m_Dust;
        [SerializeField] ParticleSystem m_Liquid;
        [SerializeField] float m_Duration = 3.5f;

        GameObject m_Source;

        // ------------------------------------------------------------------ pool

        static readonly Dictionary<GameObject, Stack<BreakFx>> s_Pool = new Dictionary<GameObject, Stack<BreakFx>>();

        public static BreakFx Get(GameObject prefab)
        {
            if (prefab == null) return null;
            if (!s_Pool.TryGetValue(prefab, out var stack)) s_Pool[prefab] = stack = new Stack<BreakFx>();
            BreakFx fx = null;
            while (stack.Count > 0 && fx == null) fx = stack.Pop();
            if (fx == null)
            {
                fx = Instantiate(prefab).GetComponent<BreakFx>();
                fx.m_Source = prefab;
                DontDestroyOnLoad(fx.gameObject);
            }
            fx.gameObject.SetActive(true);
            return fx;
        }

        /// <summary>Prépare des instances à l'avance (évite l'à-coup du premier coup).</summary>
        public static void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null) return;
            if (!s_Pool.TryGetValue(prefab, out var stack)) s_Pool[prefab] = stack = new Stack<BreakFx>();
            while (stack.Count < count)
            {
                var fx = Instantiate(prefab).GetComponent<BreakFx>();
                fx.m_Source = prefab;
                DontDestroyOnLoad(fx.gameObject);
                fx.gameObject.SetActive(false);
                stack.Push(fx);
            }
        }

        void Release()
        {
            foreach (var ps in new[] { m_Chips, m_Dust, m_Liquid })
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            gameObject.SetActive(false);
            if (m_Source != null && s_Pool.TryGetValue(m_Source, out var stack)) stack.Push(this);
            else Destroy(gameObject);
        }

        // ------------------------------------------------------------------ lecture

        public struct Settings
        {
            public Vector3 point, direction;
            public float energy;          // 0..1
            public Color chip1, chip2;
            public bool liquid;
            public Color liquidColor;
        }

        public void Play(Settings s)
        {
            float e = Mathf.Clamp01(s.energy);
            transform.position = s.point;
            var dir = s.direction.sqrMagnitude > 1e-4f ? s.direction.normalized : Vector3.up;

            // Coup mou : projection large, plutôt vers le haut. Coup violent : gerbe serrée dans le sens du coup.
            var aim = Vector3.Slerp(Vector3.up, dir, Mathf.Lerp(0.35f, 0.85f, e)) + Vector3.up * 0.15f;
            var rot = Quaternion.LookRotation(aim.normalized);

            Configure(m_Chips, rot, Mathf.Lerp(75f, 32f, e),
                count: Mathf.RoundToInt(Mathf.Lerp(10f, 55f, e)),
                speed: Mathf.Lerp(0.9f, 6.5f, e),
                size: Mathf.Lerp(1.35f, 0.8f, e),          // coup violent = éclats plus fins
                c1: s.chip1, c2: s.chip2);

            Configure(m_Dust, rot, 70f,
                count: Mathf.RoundToInt(Mathf.Lerp(2f, 14f, e * e)),
                speed: Mathf.Lerp(0.15f, 1.2f, e),
                size: Mathf.Lerp(0.6f, 1.5f, e),
                c1: null, c2: null);

            if (s.liquid && m_Liquid != null)
            {
                // Coup mou : le liquide s'écoule. Coup violent : il gicle.
                var liquidAim = Vector3.Slerp(Vector3.down * 0.3f + Vector3.up * 0.2f, aim, Mathf.Lerp(0.3f, 1f, e));
                Configure(m_Liquid, Quaternion.LookRotation(liquidAim.sqrMagnitude > 1e-4f ? liquidAim.normalized : Vector3.up),
                    Mathf.Lerp(80f, 40f, e),
                    count: Mathf.RoundToInt(Mathf.Lerp(20f, 100f, e)),
                    speed: Mathf.Lerp(0.5f, 5f, e),
                    size: Mathf.Lerp(1.2f, 0.9f, e),
                    c1: s.liquidColor, c2: s.liquidColor * new Color(0.85f, 0.8f, 0.75f, 1f));
            }

            StopAllCoroutines();
            StartCoroutine(ReleaseLater());
        }

        // Plages de base (vitesse, taille) lues dans le préfab : on les multiplie sans jamais les modifier.
        readonly Dictionary<ParticleSystem, Vector4> m_Base = new Dictionary<ParticleSystem, Vector4>();

        void Configure(ParticleSystem ps, Quaternion rot, float cone, int count, float speed, float size, Color? c1, Color? c2)
        {
            if (ps == null) return;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (count <= 0) return;
            var main = ps.main;
            if (!m_Base.TryGetValue(ps, out var b))
                m_Base[ps] = b = new Vector4(main.startSpeed.constantMin, main.startSpeed.constantMax, main.startSize.constantMin, main.startSize.constantMax);
            ps.transform.rotation = rot;
            var shape = ps.shape; shape.angle = cone;
            main.startSpeed = new ParticleSystem.MinMaxCurve(b.x * speed, b.y * speed);
            main.startSize = new ParticleSystem.MinMaxCurve(b.z * size, b.w * size);
            if (c1.HasValue) main.startColor = new ParticleSystem.MinMaxGradient(c1.Value, c2 ?? c1.Value);
            var em = ps.emission;
            em.SetBurst(0, new ParticleSystem.Burst(0f, (short)count));
            ps.Play(false);
        }

        IEnumerator ReleaseLater()
        {
            yield return new WaitForSeconds(m_Duration);
            Release();
        }
    }
}
