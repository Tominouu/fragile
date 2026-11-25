using UnityEngine;

namespace Fragile.Breakables
{
    /// <summary>
    /// Niveau du liquide d'une bouteille (shader Fragile/Liquide) : plan horizontal dans le monde, qui ondule
    /// quand la bouteille bouge (ressort amorti) puis se calme. Ne calcule rien quand la bouteille est immobile.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    [ExecuteAlways] // visible aussi dans l'éditeur
    public class LiquidFill : MonoBehaviour
    {
        [Tooltip("Remplissage (0 = vide, 1 = plein). Légère variation automatique d'une bouteille à l'autre.")]
        [SerializeField, Range(0f, 1f)] float m_Fill = 0.65f;
        [SerializeField] float m_FillVariation = 0.2f;
        [Header("Ondulation")]
        [SerializeField] float m_WobbleStrength = 0.15f;
        [SerializeField] float m_WobbleFrequency = 7f;
        [SerializeField] float m_WobbleDamping = 3.5f;
        [SerializeField] float m_MaxTilt = 0.45f;

        static readonly int k_FillPoint = Shader.PropertyToID("_FillPoint");
        static readonly int k_FillNormal = Shader.PropertyToID("_FillNormal");

        Renderer m_Renderer;
        MaterialPropertyBlock m_Block;
        Rigidbody m_Body;
        float m_LocalMinY, m_LocalMaxY, m_Level;
        Vector3 m_LocalCenter;
        Vector3 m_LastPos, m_LastVel;
        Vector2 m_Tilt, m_TiltVel;   // inclinaison de la surface (x, z) et sa vitesse
        bool m_Calm;

        void Awake()
        {
            m_Renderer = GetComponent<Renderer>();
            m_Block = new MaterialPropertyBlock();
            m_Body = GetComponentInParent<Rigidbody>();
            var b = GetComponent<MeshFilter>().sharedMesh.bounds;
            m_LocalMinY = b.min.y; m_LocalMaxY = b.max.y; m_LocalCenter = b.center;
            // Chaque bouteille un peu différente (stable : dépend de sa position de départ).
            RecomputeLevel();
            m_LastPos = transform.position;
            Apply();
        }

        void OnValidate()
        {
            if (!TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null) return;
            var b = mf.sharedMesh.bounds;
            m_LocalMinY = b.min.y; m_LocalMaxY = b.max.y; m_LocalCenter = b.center;
            RecomputeLevel(); Apply();
        }

        void RecomputeLevel()
        {
            var p = transform.position;
            float h = Mathf.Abs(Mathf.Sin(p.x * 12.9898f + p.z * 78.233f) * 43758.5453f) % 1f;
            m_Level = Mathf.Clamp01(m_Fill + (h - 0.5f) * 2f * m_FillVariation);
        }

        void LateUpdate()
        {
            if (!Application.isPlaying) { RecomputeLevel(); Apply(); return; } // éditeur : niveau à plat, suit les déplacements
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // En mouvement : physique éveillée, ou objet déplacé autrement (main, téléportation, script).
            bool moving = (m_Body != null && !m_Body.IsSleeping()) || transform.hasChanged;
            transform.hasChanged = false;
            if (!moving && m_Calm) return; // immobile et surface calme : rien à faire

            // Accélération de la bouteille -> la surface penche dans le sens inverse, puis revient (ressort amorti).
            var pos = transform.position;
            var vel = (pos - m_LastPos) / dt;
            // Saut instantané (téléportation, repositionnement) : ce n'est pas un vrai mouvement, on ne fait pas gicler.
            if (vel.sqrMagnitude > 20f * 20f) { m_LastPos = pos; m_LastVel = Vector3.zero; Apply(); return; }
            var acc = (vel - m_LastVel) / dt;
            m_LastPos = pos; m_LastVel = vel;
            var push = new Vector2(-acc.x, -acc.z) * m_WobbleStrength * 0.01f;
            m_TiltVel += push - m_Tilt * (m_WobbleFrequency * m_WobbleFrequency) * dt - m_TiltVel * (m_WobbleDamping * dt);
            m_Tilt += m_TiltVel * dt;
            m_Tilt = Vector2.ClampMagnitude(m_Tilt, m_MaxTilt);

            m_Calm = !moving && m_Tilt.sqrMagnitude < 1e-6f && m_TiltVel.sqrMagnitude < 1e-6f;
            Apply();
        }

        void Apply()
        {
            if (m_Renderer == null) m_Renderer = GetComponent<Renderer>();
            if (m_Block == null) m_Block = new MaterialPropertyBlock();
            if (m_Renderer == null) return;
            // Point du niveau : à la hauteur de remplissage, sur l'axe de la bouteille.
            var local = new Vector3(m_LocalCenter.x, Mathf.Lerp(m_LocalMinY, m_LocalMaxY, m_Level), m_LocalCenter.z);
            var point = transform.TransformPoint(local);
            var normal = new Vector3(m_Tilt.x, 1f, m_Tilt.y).normalized;
            m_Renderer.GetPropertyBlock(m_Block);
            m_Block.SetVector(k_FillPoint, point);
            m_Block.SetVector(k_FillNormal, normal);
            m_Renderer.SetPropertyBlock(m_Block);
        }
    }
}
