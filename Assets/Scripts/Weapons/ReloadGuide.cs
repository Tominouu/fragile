using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fragile.Weapons
{
    /// <summary>
    /// Aide à la manipulation des armes : quand l'arme tenue ne peut pas tirer, la pièce à actionner (pompe, culasse,
    /// levier du verrou) pulse en surbrillance ambrée et une flèche animée montre le geste (tirer, pousser, relever,
    /// rabattre). Pistolet sans chargeur : silhouette lumineuse du chargeur dans la poignée, flèche vers le haut,
    /// et le plateau de chargeurs le plus proche s'allume. Ajoutée automatiquement par <see cref="Firearm"/>.
    /// </summary>
    [RequireComponent(typeof(Firearm))]
    public class ReloadGuide : MonoBehaviour
    {
        static readonly Color Amber = new Color(1.4f, 0.75f, 0.15f, 1f);
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static Material s_Highlight;
        static Mesh s_Arrow;

        Firearm m_Gun;
        Material m_ArrowMat;
        Transform m_ArrowT;
        MeshRenderer m_ArrowR;
        readonly Dictionary<Transform, List<MeshRenderer>> m_Overlays = new Dictionary<Transform, List<MeshRenderer>>();
        List<MeshRenderer> m_Shown;
        GameObject m_Ghost;
        AmmoBox m_Box;
        MaterialPropertyBlock m_Block;
        float m_Phase;

        void Awake()
        {
            m_Gun = GetComponent<Firearm>();
            m_Block = new MaterialPropertyBlock();
            if (s_Highlight == null) s_Highlight = Resources.Load<Material>("Surbrillance");
        }

        void OnDisable() => Hide();

        void LateUpdate()
        {
            var step = m_Gun.GetGuide(out var part, out var handle, out var dir);
            if (step == GuideStep() || part == null) { Hide(); return; }

            m_Phase = (m_Phase + Time.deltaTime * 1.4f) % 1f;
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 6f);

            // Surbrillance de la pièce : seulement tant qu'on ne la tient pas (on invite à la saisir).
            bool grabbing = handle != null && handle.held;
            List<MeshRenderer> overlays = null;
            if (step == Firearm.GuideStep.InsertMagazine) overlays = GhostMagazine(part);
            else if (!grabbing) overlays = OverlaysOf(part);
            Show(overlays);
            if (overlays != null) foreach (var r in overlays) Tint(r, Amber * pulse, ColorId);

            // Pistolet sans chargeur : le plateau de chargeurs s'allume aussi.
            Box(step == Firearm.GuideStep.InsertMagazine, pulse);

            Arrow(step, part, dir);
        }

        static Firearm.GuideStep GuideStep() => Firearm.GuideStep.None;

        // ------------------------------------------------------------------ surbrillance

        List<MeshRenderer> OverlaysOf(Transform part)
        {
            if (m_Overlays.TryGetValue(part, out var list)) return list;
            list = new List<MeshRenderer>();
            if (s_Highlight != null)
                foreach (var r in part.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.name == "Surbrillance" || r.name == "Aide" || !r.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null) continue;
                    list.Add(Overlay(r.transform, mf.sharedMesh));
                }
            m_Overlays[part] = list;
            return list;
        }

        MeshRenderer Overlay(Transform parent, Mesh mesh)
        {
            var go = new GameObject("Aide");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mats = new Material[mesh.subMeshCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = s_Highlight;
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            go.SetActive(false);
            return mr;
        }

        /// <summary>Silhouette du chargeur absent, à sa place dans la poignée.</summary>
        List<MeshRenderer> GhostMagazine(Transform magazine)
        {
            if (m_Ghost == null && magazine.TryGetComponent<MeshFilter>(out var mf) && s_Highlight != null)
            {
                var r = Overlay(magazine.parent, mf.sharedMesh);
                r.transform.SetLocalPositionAndRotation(magazine.localPosition, magazine.localRotation);
                r.transform.localScale = magazine.localScale;
                m_Ghost = r.gameObject;
                m_Overlays[r.transform] = new List<MeshRenderer> { r };
            }
            return m_Ghost != null ? m_Overlays[m_Ghost.transform] : null;
        }

        void Show(List<MeshRenderer> list)
        {
            if (m_Shown == list) { if (list != null) foreach (var r in list) if (r != null && !r.gameObject.activeSelf) r.gameObject.SetActive(true); return; }
            if (m_Shown != null) foreach (var r in m_Shown) if (r != null) r.gameObject.SetActive(false);
            m_Shown = list;
            if (list != null) foreach (var r in list) if (r != null) r.gameObject.SetActive(true);
        }

        void Tint(Renderer r, Color c, int id)
        {
            if (r == null) return;
            r.GetPropertyBlock(m_Block);
            m_Block.SetColor(id, c);
            r.SetPropertyBlock(m_Block);
        }

        void Box(bool on, float pulse)
        {
            if (on && m_Box == null)
            {
                float best = 6f;
                foreach (var b in FindObjectsByType<AmmoBox>(FindObjectsSortMode.None))
                {
                    if (b.ammoKind != AmmoItem.Kind.PistolMagazine) continue;
                    float d = Vector3.Distance(b.transform.position, transform.position);
                    if (d < best) { best = d; m_Box = b; }
                }
            }
            if (m_Box == null) return;
            var list = OverlaysOf(m_Box.transform);
            foreach (var r in list) { if (r == null) continue; r.gameObject.SetActive(on); if (on) Tint(r, Amber * pulse, ColorId); }
            if (!on) m_Box = null;
        }

        // ------------------------------------------------------------------ flèche

        void Arrow(Firearm.GuideStep step, Transform part, Vector3 dir)
        {
            if (m_ArrowT == null) BuildArrow();
            if (m_ArrowT == null) return;
            var cam = Camera.main;
            // Point de départ : à côté de la pièce, du côté où on la voit (dessous pour la pompe, dessus pour la culasse...).
            var bounds = Bounds(part, step);
            var up = transform.up; var right = transform.right;
            // Décalages dans le repère de l'arme (la boîte englobante du monde grossit quand l'arme penche).
            Vector3 anchor = step switch
            {
                Firearm.GuideStep.InsertMagazine => bounds.center - up * 0.1f,
                Firearm.GuideStep.LiftUp or Firearm.GuideStep.LowerDown => bounds.center + right * 0.06f + up * 0.01f,
                _ when m_Gun.kind == Firearm.Kind.Pistol => bounds.center + up * 0.045f,
                _ when m_Gun.kind == Firearm.Kind.Rifle => bounds.center + right * 0.05f + up * 0.04f,
                _ => bounds.center - up * 0.06f,
            };
            const float travel = 0.045f;
            var pos = anchor + dir * (m_Phase * travel - travel * 0.5f);
            var toCam = cam != null ? cam.transform.position - pos : -transform.forward;
            var facing = Vector3.ProjectOnPlane(toCam, dir);
            if (facing.sqrMagnitude < 1e-6f) facing = Vector3.ProjectOnPlane(right, dir);
            m_ArrowT.SetPositionAndRotation(pos, Quaternion.LookRotation(facing.normalized, dir));
            // Apparition / disparition le long du geste.
            float a = Mathf.Sin(m_Phase * Mathf.PI);
            m_ArrowMat.SetColor(BaseColorId, Amber * (0.9f * a));
            m_ArrowT.gameObject.SetActive(true);
        }

        Bounds Bounds(Transform part, Firearm.GuideStep step)
        {
            if (step == Firearm.GuideStep.InsertMagazine && m_Ghost != null && m_Ghost.TryGetComponent<Renderer>(out var g)) return g.bounds;
            bool any = false; var b = new Bounds(part.position, Vector3.zero);
            foreach (var r in part.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.name == "Aide") continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        void BuildArrow()
        {
            var src = m_Gun.guideMaterialSource;
            if (src == null) return;
            m_ArrowMat = new Material(src) { name = "Flèche d'aide" };
            m_ArrowMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (s_Arrow == null) s_Arrow = ArrowMesh();
            var go = new GameObject("Flèche d'aide");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = s_Arrow;
            m_ArrowR = go.AddComponent<MeshRenderer>();
            m_ArrowR.sharedMaterial = m_ArrowMat;
            m_ArrowR.shadowCastingMode = ShadowCastingMode.Off; m_ArrowR.receiveShadows = false;
            m_ArrowT = go.transform;
            go.SetActive(false);
        }

        /// <summary>Flèche plate (vers +Y, dans le plan XY), deux chevrons pour qu'on la lise de loin.</summary>
        static Mesh ArrowMesh()
        {
            var v = new List<Vector3>(); var t = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 }); }
            void Chevron(float y, float w, float h, float thick)
            {
                // Deux branches d'un « ^ ».
                Quad(new Vector3(-w, y, 0f), new Vector3(0f, y + h, 0f), new Vector3(0f, y + h + thick, 0f), new Vector3(-w, y + thick, 0f));
                Quad(new Vector3(0f, y + h, 0f), new Vector3(w, y, 0f), new Vector3(w, y + thick, 0f), new Vector3(0f, y + h + thick, 0f));
            }
            Chevron(-0.016f, 0.021f, 0.015f, 0.007f);
            Chevron(0.005f, 0.021f, 0.015f, 0.007f);
            var m = new Mesh { name = "Flèche d'aide" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            var n = new Vector3[v.Count]; for (int i = 0; i < n.Length; i++) n[i] = Vector3.forward;
            m.normals = n;
            m.RecalculateBounds();
            return m;
        }

        void Hide()
        {
            Show(null);
            if (m_Box != null) Box(false, 0f);
            if (m_ArrowT != null) m_ArrowT.gameObject.SetActive(false);
        }

        void OnDestroy() { if (m_ArrowMat != null) Destroy(m_ArrowMat); }
    }
}
