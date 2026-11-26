using UnityEngine;

namespace Fragile.Grab
{
    /// <summary>
    /// Surface de prise cylindrique (batte, manche, bouteille...). L'axe est réglable (Z pour la batte, Y pour une bouteille debout).
    /// Le rayon le long de l'axe est mesuré sur le maillage, pour que les doigts se posent sur la vraie forme.
    /// </summary>
    [DisallowMultipleComponent]
    public class GripSurface : MonoBehaviour
    {
        [Tooltip("Marge aux extrémités où la main ne peut pas se placer (m).")]
        [SerializeField] float m_EndMargin = 0.04f;
        [Tooltip("Axe long de l'objet, dans son repère local (batte : Z, bouteille debout : Y).")]
        [SerializeField] Vector3 m_LocalAxis = Vector3.forward;
        [Tooltip("Maillages mesurés pour la prise (ex. poignée d'une arme). Vide = tout l'objet. L'axe passe par l'origine de l'objet.")]
        [SerializeField] Transform m_Source;

        [Header("Prise orientée (armes)")]
        [Tooltip("L'objet est toujours tenu de la même façon : sa direction de visée suit celle de la manette.")]
        [SerializeField] bool m_FixedAim;
        [Tooltip("Direction de visée (canon) dans le repère local.")]
        [SerializeField] Vector3 m_AimLocal = Vector3.forward;
        [Tooltip("Haut de l'objet dans le repère local.")]
        [SerializeField] Vector3 m_UpLocal = Vector3.up;

        public bool fixedAim => m_FixedAim;
        /// <summary>Rotation locale dont l'avant est la visée et le haut celui de l'objet.</summary>
        public Quaternion aimLocalRotation => Quaternion.LookRotation(m_AimLocal, m_UpLocal);
        /// <summary>Milieu de la zone saisissable (coordonnée le long de l'axe).</summary>
        public float gripCenterZ { get { if (m_Radius == null) Build(); return (m_ZMin + m_ZMax) / 2f; } }
        public Vector3 localAxis => m_LocalAxis;

        /// <summary>Rotation qui amène l'axe de l'objet sur Z (repère de calcul de la surface).</summary>
        Quaternion ToZ => Quaternion.FromToRotation(m_LocalAxis.sqrMagnitude > 1e-6f ? m_LocalAxis.normalized : Vector3.forward, Vector3.forward);

        public void SetLocalAxis(Vector3 axis) { m_LocalAxis = axis; m_Radius = null; }

        /// <summary>Monde -> repère de la surface (axe sur Z).</summary>
        public Matrix4x4 worldToSurface => Matrix4x4.Rotate(ToZ) * transform.worldToLocalMatrix;

        /// <summary>Point de l'axe à la coordonnée z, en monde.</summary>
        public Vector3 AxisPoint(float z) => transform.TransformPoint(Quaternion.Inverse(ToZ) * new Vector3(0f, 0f, z));

        const int k_Bins = 96;
        float[] m_Radius;
        float m_ZMin, m_ZMax;

        public Vector3 axisOrigin => transform.position;
        public Vector3 axis => transform.TransformDirection(m_LocalAxis).normalized;

        /// <summary>Objet auquel accrocher les mains qui tiennent cette surface (par défaut : celui-ci).</summary>
        public Transform handParent
        {
            get => m_HandParent != null ? m_HandParent : transform;
            set => m_HandParent = value;
        }
        Transform m_HandParent;

        void Awake() => Build();

        public void Build()
        {
            var filters = (m_Source != null ? m_Source : transform).GetComponentsInChildren<MeshFilter>(true);
            m_ZMin = float.MaxValue; m_ZMax = float.MinValue;
            var points = new System.Collections.Generic.List<Vector3>();
            var toZ = ToZ;
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = toZ * transform.InverseTransformPoint(mf.transform.TransformPoint(v));
                    points.Add(p);
                    m_ZMin = Mathf.Min(m_ZMin, p.z); m_ZMax = Mathf.Max(m_ZMax, p.z);
                }
            }

            m_Radius = new float[k_Bins];
            var has = new bool[k_Bins];
            foreach (var p in points)
            {
                int b = Bin(p.z);
                m_Radius[b] = Mathf.Max(m_Radius[b], new Vector2(p.x, p.y).magnitude);
                has[b] = true;
            }

            // Comble les tranches sans sommet par interpolation entre voisines.
            for (int i = 0; i < k_Bins; i++)
            {
                if (has[i]) continue;
                int lo = i - 1; while (lo >= 0 && !has[lo]) lo--;
                int hi = i + 1; while (hi < k_Bins && !has[hi]) hi++;
                if (lo < 0 && hi >= k_Bins) continue;
                if (lo < 0) m_Radius[i] = m_Radius[hi];
                else if (hi >= k_Bins) m_Radius[i] = m_Radius[lo];
                else m_Radius[i] = Mathf.Lerp(m_Radius[lo], m_Radius[hi], (i - lo) / (float)(hi - lo));
            }
        }

        int Bin(float z) => Mathf.Clamp(Mathf.FloorToInt((z - m_ZMin) / (m_ZMax - m_ZMin) * k_Bins), 0, k_Bins - 1);

        /// <summary>Rayon de l'objet à la coordonnée locale z.</summary>
        public float RadiusAt(float z)
        {
            if (m_Radius == null) Build();
            float f = (Mathf.Clamp(z, m_ZMin, m_ZMax) - m_ZMin) / (m_ZMax - m_ZMin) * k_Bins - 0.5f;
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, k_Bins - 1);
            int j = Mathf.Min(i + 1, k_Bins - 1);
            return Mathf.Lerp(m_Radius[i], m_Radius[j], Mathf.Clamp01(f - i));
        }

        /// <summary>Coordonnée z (locale) la plus proche d'un point, limitée à la zone saisissable.</summary>
        public float ClosestGripZ(Vector3 worldPoint)
        {
            if (m_Radius == null) Build();
            float z = (ToZ * transform.InverseTransformPoint(worldPoint)).z;
            return Mathf.Clamp(z, m_ZMin + m_EndMargin, m_ZMax - m_EndMargin);
        }

        /// <summary>Distance signée d'un point à la surface (négative = à l'intérieur).</summary>
        public float SignedDistance(Vector3 worldPoint)
        {
            if (m_Radius == null) Build();
            return SignedDistanceLocal(ToZ * transform.InverseTransformPoint(worldPoint));
        }

        /// <summary>Même chose pour un point déjà exprimé dans le repère de la surface (voir worldToSurface ; plus rapide en boucle).</summary>
        public float SignedDistanceLocal(Vector3 l)
        {
            float radial = Mathf.Sqrt(l.x * l.x + l.y * l.y) - RadiusAt(l.z);
            float axial = Mathf.Max(m_ZMin - l.z, l.z - m_ZMax);
            return Mathf.Max(radial, axial);
        }
    }
}
