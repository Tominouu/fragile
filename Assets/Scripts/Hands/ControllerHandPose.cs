using System.Collections.Generic;
using Fragile.Grab;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Fragile.Hands
{
    /// <summary>
    /// Main affichée à la place de la manette.
    /// - Main libre : les doigts suivent la gâchette (index) et le grip (autres doigts).
    /// - Objet tenu (<see cref="GripSurface"/>) : la main se cale sur l'objet, axe dans le poing,
    ///   et chaque phalange se referme jusqu'à toucher la surface, sans la traverser.
    /// À placer sur la racine du modèle de main, enfant de "Left/Right Controller".
    /// </summary>
    public class ControllerHandPose : MonoBehaviour
    {
        [Header("Entrées (XRI Default Input Actions)")]
        [SerializeField] InputActionReference m_Trigger; // XRI Left/Right Interaction/Activate Value
        [SerializeField] InputActionReference m_Grip;    // XRI Left/Right Interaction/Select Value

        [Header("Main libre : pliage des doigts (degrés par articulation, à fond)")]
        [SerializeField] float m_FingerCurl = 65f;
        [SerializeField] float m_ThumbCurl = 25f;
        [Tooltip("Axe de pliage du pouce (le pouce se replie autour de son Y local).")]
        [SerializeField] Vector3 m_ThumbAxis = Vector3.up;
        [Tooltip("Pliage au repos, manette tenue sans appuyer (0 = main à plat).")]
        [SerializeField, Range(0f, 1f)] float m_RestCurl = 0.25f;
        [Tooltip("Vitesse de lissage des doigts.")]
        [SerializeField] float m_Smoothing = 20f;

        [Header("Prise d'objets")]
        [Tooltip("Où l'objet se pose dans la paume : 0 = talon de la main, 1 = base des doigts.")]
        [SerializeField, Range(0f, 1f)] float m_PalmContact = 0.6f;
        [Tooltip("Épaisseur de chair à cet endroit (m). Mesurée automatiquement sur le maillage au démarrage.")]
        [SerializeField] float m_PalmThickness = 0.013f;
        [Tooltip("Rayon des doigts, pour qu'ils se posent sur la surface sans la traverser (m).")]
        [SerializeField] float m_FingerRadius = 0.0085f;
        [Tooltip("Angles limites par articulation pendant la prise (degrés).")]
        [SerializeField] float m_MinJointAngle = -10f;
        [SerializeField] float m_MaxJointAngle = 100f;
        [Tooltip("Enfoncement de peau toléré dans l'objet (m).")]
        [SerializeField] float m_SkinTolerance = 0.0003f;

        float m_ThumbSign = 1f;         // -1 sur la main miroir : le pouce s'y plie dans l'autre sens
        SkinnedMeshRenderer m_Skin;
        Vector3[] m_MeshVerts;          // copie des données de peau pour le skinning maison
        BoneWeight[] m_Weights;
        Matrix4x4[] m_Bindposes;
        Transform[] m_SkinBones;
        int[] m_VertexLink; // par sommet : doigt*4 + articulation, ou -1 pour la paume

        static readonly string[] k_Fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
        static readonly string[] k_FingerJoints = { "Proximal", "Intermediate", "Distal" };
        // Le métacarpe du pouce sert seulement pendant une prise (pour l'écarter ou le refermer sur l'objet).
        static readonly string[] k_ThumbJoints = { "Metacarpal", "Proximal", "Distal" };

        Transform[][] m_Bones;          // [doigt][articulation] ; 0 = pouce
        Transform[][][] m_Twins;        // os jumeaux de chaque articulation (même nom + ".00x")
        Quaternion[][][] m_TwinBaseRotations;
        Transform[] m_Tips;             // bout de chaque doigt
        Quaternion[][] m_BaseRotations;
        float[][] m_HoldAngles;         // angles de prise calculés
        float m_TriggerValue, m_GripValue;

        // Repères de prise, en espace local de la main (calculés en pose de repos).
        Vector3 m_PalmLocal, m_PalmNormalLocal, m_GripAxisLocal;

        // Place "à la maison" sur la manette.
        Transform m_HomeParent;
        Vector3 m_HomeLocalPosition;
        Quaternion m_HomeLocalRotation;
        GripSurface m_Holding;

        public bool isHolding => m_Holding != null;

        void Awake()
        {
            CacheBones();
            m_HomeParent = transform.parent;
            m_HomeLocalPosition = transform.localPosition;
            m_HomeLocalRotation = transform.localRotation;
        }

        void LateUpdate()
        {
            if (m_Holding != null)
            {
                ApplyAngles(m_HoldAngles);
                return;
            }

            float t = 1f - Mathf.Exp(-m_Smoothing * Time.deltaTime);
            m_TriggerValue = Mathf.Lerp(m_TriggerValue, Read(m_Trigger), t);
            m_GripValue = Mathf.Lerp(m_GripValue, Read(m_Grip), t);
            ApplyPose(m_TriggerValue, m_GripValue);
        }

        static float Read(InputActionReference reference)
        {
            var action = reference != null ? reference.action : null;
            return action != null && action.enabled ? action.ReadValue<float>() : 0f;
        }

        // ------------------------------------------------------------------ Main libre

        /// <summary>Pose main libre (0..1). Public pour pouvoir prévisualiser dans l'éditeur.</summary>
        public void ApplyPose(float trigger, float grip)
        {
            if (m_Bones == null) CacheBones();
            float Curl(float input) => Mathf.Lerp(m_RestCurl, 1f, input);

            SetJoint(0, 0, 0f); // métacarpe du pouce : neutre en main libre
            for (int j = 1; j < m_Bones[0].Length; j++)
                SetJoint(0, j, m_ThumbCurl * Curl(grip));
            Bend(1, m_FingerCurl * Curl(trigger));
            for (int f = 2; f <= 4; f++)
                Bend(f, m_FingerCurl * Curl(grip));
        }

        /// <summary>Remet les os dans leur pose d'origine (utile après une prévisualisation).</summary>
        public void ResetPose()
        {
            if (m_Bones == null) return;
            for (int f = 0; f < m_Bones.Length; f++)
                for (int j = 0; j < m_Bones[f].Length; j++)
                    SetJoint(f, j, 0f);
        }

        void Bend(int finger, float angle)
        {
            for (int j = 0; j < m_Bones[finger].Length; j++)
                SetJoint(finger, j, angle);
        }

        void SetJoint(int finger, int joint, float angle)
        {
            var bone = m_Bones[finger][joint];
            if (bone == null) return;
            var axis = finger == 0 ? m_ThumbAxis * m_ThumbSign : Vector3.right;
            var baseRot = m_BaseRotations[finger][joint];
            var rot = baseRot * Quaternion.AngleAxis(angle, axis);
            bone.localRotation = rot;

            // Os "jumeaux" (ex. L_ThumbDistal.005) : même rotation, exprimée dans l'espace du parent.
            var twins = m_Twins[finger][joint];
            if (twins == null) return;
            var delta = rot * Quaternion.Inverse(baseRot);
            for (int i = 0; i < twins.Length; i++)
                twins[i].localRotation = delta * m_TwinBaseRotations[finger][joint][i];
        }

        void ApplyAngles(float[][] angles)
        {
            for (int f = 0; f < angles.Length; f++)
                for (int j = 0; j < angles[f].Length; j++)
                    SetJoint(f, j, angles[f][j]);
        }

        // ------------------------------------------------------------------ Prise d'objets

        /// <summary>
        /// Calcule la prise de <paramref name="surface"/> depuis la position actuelle de la main,
        /// puis accroche la main à l'objet.
        /// </summary>
        /// <param name="handInObject">Pose de la main dans le repère de l'objet.</param>
        /// <param name="handHomeWorld">Pose réelle de la main sur la manette (là où l'objet doit venir).</param>
        public bool TryBeginGrab(GripSurface surface, out Pose handInObject, out Pose handHomeWorld)
        {
            handInObject = default;
            handHomeWorld = default;
            if (surface == null || m_Holding != null) return false;
            if (m_Bones == null) CacheBones();

            handHomeWorld = new Pose(
                m_HomeParent.TransformPoint(m_HomeLocalPosition),
                m_HomeParent.rotation * m_HomeLocalRotation);

            // 1) Orientation : l'axe des jointures (auriculaire -> index) s'aligne sur l'axe de l'objet.
            var handRot = handHomeWorld.rotation;
            var gripAxis = handRot * m_GripAxisLocal;
            var objAxis = surface.axis;
            if (Vector3.Dot(gripAxis, objAxis) < 0f) objAxis = -objAxis;
            var rot = Quaternion.FromToRotation(gripAxis, objAxis) * handRot;

            // 2) Position : la paume vient contre la surface, au plus près de là où la main a saisi.
            var palmWorld = handHomeWorld.position + handRot * m_PalmLocal;
            float z = surface.ClosestGripZ(palmWorld);
            var center = surface.transform.TransformPoint(new Vector3(0f, 0f, z));
            var normal = rot * m_PalmNormalLocal; // sort de la paume, vers l'objet
            normal = Vector3.ProjectOnPlane(normal, objAxis).normalized;
            float distance = surface.RadiusAt(z) + m_PalmThickness;
            var pos = center - normal * distance - rot * m_PalmLocal;

            // 3) Accroche la main aux visuels de l'objet (elle les suit image par image, même si la physique traîne).
            m_Holding = surface;
            transform.SetParent(surface.handParent, false);
            transform.SetPositionAndRotation(pos, rot);

            // 4) Referme les doigts sur la surface, puis corrige sur la vraie peau.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ComputeWrap(surface);
            ApplyAngles(m_HoldAngles);
            float wrapMs = (float)sw.Elapsed.TotalMilliseconds;
            ResolveSkinPenetration(surface, rot * m_PalmNormalLocal);
            lastGrabTimings = new Vector2(wrapMs, (float)sw.Elapsed.TotalMilliseconds - wrapMs);

            // Sécurité : jamais de position invalide (NaN fait planter le rendu de l'éditeur).
            var p = transform.position;
            if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z))
            {
                Debug.LogWarning("[Fragile] Prise annulée : position de main invalide.");
                EndGrab();
                return false;
            }

            // Pose de la main dans le repère de l'objet (racine physique).
            var objT = surface.transform;
            handInObject = new Pose(
                objT.InverseTransformPoint(transform.position),
                Quaternion.Inverse(objT.rotation) * transform.rotation);
            return true;
        }

        /// <summary>
        /// Vérifie la peau réelle (maillage déformé) : tant qu'un point traverse l'objet,
        /// on rouvre légèrement la phalange concernée, ou on recule la main si c'est la paume.
        /// </summary>
        void ResolveSkinPenetration(GripSurface surface, Vector3 palmNormalWorld)
        {
            if (m_Skin == null || m_VertexLink == null || m_MeshVerts == null) return;
            var open = new bool[m_Bones.Length][];
            for (int f = 0; f < open.Length; f++) open[f] = new bool[m_Bones[f].Length];

            // Seuls les points de peau proches de l'objet peuvent le toucher : on ne surveille qu'eux,
            // et on calcule leur déformation nous-mêmes (bien plus rapide que de déformer toute la main).
            // Les positions sont calculées directement dans le repère de l'objet (matrice os -> objet).
            var skin = new Matrix4x4[m_SkinBones.Length];
            int maxBones = MaxSkinBones();
            var toSurface = surface.transform.worldToLocalMatrix;
            UpdateSkinMatrices(skin, toSurface);
            var watch = new List<int>();
            for (int i = 0; i < m_MeshVerts.Length; i++)
                if (surface.SignedDistanceLocal(SkinVertex(i, skin, maxBones)) < 0.02f) watch.Add(i);

            for (int iter = 0; iter < 80; iter++)
            {
                UpdateSkinMatrices(skin, toSurface);
                bool any = false;
                float palmDepth = 0f;
                for (int f = 0; f < open.Length; f++) System.Array.Clear(open[f], 0, open[f].Length);

                foreach (int i in watch)
                {
                    float d = surface.SignedDistanceLocal(SkinVertex(i, skin, maxBones));
                    if (float.IsNaN(d) || d >= -m_SkinTolerance) continue;
                    any = true;
                    int link = m_VertexLink[i];
                    if (link < 0) { palmDepth = Mathf.Max(palmDepth, -d); continue; }

                    // Le doigt concerné s'ouvre d'un seul mouvement ; s'il est déjà ouvert à fond, on recule la main.
                    int f = link / 4;
                    if (m_Curl[f] > 0f || m_Extra[f] > 0f) open[f][0] = true;
                    else palmDepth = Mathf.Max(palmDepth, -d);
                }
                if (!any) break;

                if (palmDepth > 0f && palmDepth < 0.05f)
                    transform.position -= palmNormalWorld * (palmDepth + 0.0005f);

                for (int f = 0; f < open.Length; f++)
                {
                    if (!open[f][0]) continue;
                    // On retire d'abord le "bonus" du bout, puis on rouvre tout le doigt.
                    if (m_Extra[f] > 0f) m_Extra[f] = Mathf.Max(0f, m_Extra[f] - 3f);
                    else m_Curl[f] = Mathf.Max(0f, m_Curl[f] - 2f);
                    SetFinger(f, m_Curl[f], m_Extra[f]);
                }
            }
        }

        // Répartition du pliage par articulation (base -> bout). Un seul paramètre par doigt :
        // les phalanges se plient ensemble, jamais à l'envers -> pas de forme en "Z".
        static readonly float[] k_ThumbRatios = { 0.35f, 0.8f, 0.6f };
        static readonly float[] k_FingerRatios = { 1.0f, 1.0f, 0.75f };
        float[] m_Curl = new float[5];   // fermeture commune du doigt
        float[] m_Extra = new float[5];  // fermeture supplémentaire du bout (dernière phalange) pour épouser l'objet

        void SetFinger(int f, float curl, float extra)
        {
            var ratios = f == 0 ? k_ThumbRatios : k_FingerRatios;
            int n = m_Bones[f].Length;
            for (int j = 0; j < n; j++)
            {
                float a = curl * ratios[Mathf.Min(j, ratios.Length - 1)];
                if (j == n - 1) a += extra;
                m_HoldAngles[f][j] = a;
                SetJoint(f, j, a);
            }
        }

        void UpdateSkinMatrices(Matrix4x4[] skin, Matrix4x4 space)
        {
            for (int b = 0; b < m_SkinBones.Length; b++)
                skin[b] = m_SkinBones[b] != null ? space * m_SkinBones[b].localToWorldMatrix * m_Bindposes[b] : space;
        }

        /// <summary>
        /// Position monde d'un sommet déformé, calculée comme Unity l'affiche : même nombre d'os par sommet
        /// que le rendu (réglage de qualité "Skin Weights", 2 os en qualité Low), poids renormalisés.
        /// </summary>
        Vector3 SkinVertex(int i, Matrix4x4[] skin, int n)
        {
            var v = m_MeshVerts[i]; var w = m_Weights[i];
            float w0 = w.weight0, w1 = n > 1 ? w.weight1 : 0f, w2 = n > 2 ? w.weight2 : 0f, w3 = n > 3 ? w.weight3 : 0f;
            float total = w0 + w1 + w2 + w3;
            if (total <= 1e-6f) return skin[w.boneIndex0].MultiplyPoint3x4(v); // sommet sans poids : évite une division par zéro (NaN)
            var p = skin[w.boneIndex0].MultiplyPoint3x4(v) * (w0 / total);
            if (w1 > 0f) p += skin[w.boneIndex1].MultiplyPoint3x4(v) * (w1 / total);
            if (w2 > 0f) p += skin[w.boneIndex2].MultiplyPoint3x4(v) * (w2 / total);
            if (w3 > 0f) p += skin[w.boneIndex3].MultiplyPoint3x4(v) * (w3 / total);
            return p;
        }

        int MaxSkinBones()
        {
            var q = m_Skin.quality == SkinQuality.Auto ? (int)QualitySettings.skinWeights : (int)m_Skin.quality;
            return q <= 0 ? 4 : Mathf.Min(q, 4);
        }

        /// <summary>Durées (ms) de la dernière prise : fermeture des doigts, correction sur la peau.</summary>
        public Vector2 lastGrabTimings { get; private set; }

        /// <summary>Relâche l'objet : la main retourne sur la manette.</summary>
        public void EndGrab()
        {
            if (m_Holding == null) return;
            m_Holding = null;
            transform.SetParent(m_HomeParent, false);
            transform.localPosition = m_HomeLocalPosition;
            transform.localRotation = m_HomeLocalRotation;
            ResetPose();
        }

        /// <summary>
        /// Ferme chaque doigt articulation par articulation (de la base vers le bout) :
        /// chaque phalange tourne jusqu'à toucher la surface, puis on passe à la suivante.
        /// </summary>
        void ComputeWrap(GripSurface surface)
        {
            for (int f = 0; f < m_Bones.Length; f++)
            {
                m_Curl[f] = 0f; m_Extra[f] = 0f;
                SetFinger(f, 0f, 0f);

                // 1) Tout le doigt se ferme ensemble jusqu'au premier contact.
                for (float a = 0f; a <= m_MaxJointAngle; a += 1.5f)
                {
                    SetFinger(f, a, 0f);
                    if (FingerPenetrates(f, surface)) break;
                    m_Curl[f] = a;
                }
                // 2) Le bout continue un peu de se refermer s'il a encore de la place.
                for (float e = 0f; e <= 40f; e += 1.5f)
                {
                    SetFinger(f, m_Curl[f], e);
                    if (FingerPenetrates(f, surface)) break;
                    m_Extra[f] = e;
                }
                SetFinger(f, m_Curl[f], m_Extra[f]);
            }
        }

        bool FingerPenetrates(int f, GripSurface surface)
        {
            for (int j = 0; j < m_Bones[f].Length; j++)
                if (m_Bones[f][j] != null && LinkPenetrates(f, j, surface)) return true;
            return false;
        }

        /// <summary>La phalange qui part de l'articulation j (jusqu'à la suivante ou au bout) touche-t-elle la surface ?</summary>
        bool LinkPenetrates(int finger, int joint, GripSurface surface)
        {
            var start = m_Bones[finger][joint].position;
            var end = joint + 1 < m_Bones[finger].Length ? m_Bones[finger][joint + 1].position : m_Tips[finger].position;
            float radius = finger == 4 ? m_FingerRadius * 0.85f : m_FingerRadius;
            for (int s = 1; s <= 6; s++)
            {
                var p = Vector3.Lerp(start, end, s / 6f);
                if (surface.SignedDistance(p) < radius) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ Os

        void CacheBones()
        {
            m_Bones = new Transform[k_Fingers.Length][];
            m_BaseRotations = new Quaternion[k_Fingers.Length][];
            m_HoldAngles = new float[k_Fingers.Length][];
            m_Tips = new Transform[k_Fingers.Length];
            m_Twins = new Transform[k_Fingers.Length][][];
            m_TwinBaseRotations = new Quaternion[k_Fingers.Length][][];
            var all = GetComponentsInChildren<Transform>(true);

            Transform Find(string suffix)
            {
                foreach (var t in all)
                    if (t.name.EndsWith(suffix) && t.name.Length == suffix.Length + 2) // "L_" / "R_"
                        return t;
                return null;
            }

            for (int f = 0; f < k_Fingers.Length; f++)
            {
                var joints = f == 0 ? k_ThumbJoints : k_FingerJoints;
                m_Bones[f] = new Transform[joints.Length];
                m_BaseRotations[f] = new Quaternion[joints.Length];
                m_HoldAngles[f] = new float[joints.Length];
                m_Twins[f] = new Transform[joints.Length][];
                m_TwinBaseRotations[f] = new Quaternion[joints.Length][];
                for (int j = 0; j < joints.Length; j++)
                {
                    var bone = Find(k_Fingers[f] + joints[j]);
                    m_Bones[f][j] = bone;
                    if (bone == null) continue;
                    m_BaseRotations[f][j] = bone.localRotation;

                    var twins = new System.Collections.Generic.List<Transform>();
                    foreach (var t in all)
                        if (t != bone && t.parent == bone.parent && t.name.StartsWith(bone.name + "."))
                            twins.Add(t);
                    if (twins.Count > 0)
                    {
                        m_Twins[f][j] = twins.ToArray();
                        m_TwinBaseRotations[f][j] = new Quaternion[twins.Count];
                        for (int i = 0; i < twins.Count; i++) m_TwinBaseRotations[f][j][i] = twins[i].localRotation;
                    }
                }
                m_Tips[f] = Find(k_Fingers[f] + "Tip");
            }

            // Pour chaque sommet de peau : quelle phalange le porte (os dominant) ?
            m_Skin = GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (m_Skin != null && m_Skin.sharedMesh != null)
            {
                var skinBones = m_Skin.bones;
                var boneLink = new int[skinBones.Length];
                for (int b = 0; b < skinBones.Length; b++)
                {
                    boneLink[b] = -1;
                    if (skinBones[b] == null) continue;
                    string name = skinBones[b].name;
                    for (int f = 0; f < m_Bones.Length; f++)
                        for (int j = 0; j < m_Bones[f].Length; j++)
                            if (m_Bones[f][j] != null && name.StartsWith(m_Bones[f][j].name))
                                boneLink[b] = f * 4 + j;
                    // Os du bout : rattaché à la dernière phalange du doigt.
                    for (int f = 0; f < m_Tips.Length; f++)
                        if (m_Tips[f] != null && name.StartsWith(m_Tips[f].name))
                            boneLink[b] = f * 4 + (m_Bones[f].Length - 1);
                }
                var weights = m_Skin.sharedMesh.boneWeights;
                m_Weights = weights;
                m_MeshVerts = m_Skin.sharedMesh.vertices;
                m_Bindposes = m_Skin.sharedMesh.bindposes;
                m_SkinBones = skinBones;
                m_VertexLink = new int[weights.Length];
                for (int i = 0; i < weights.Length; i++)
                    m_VertexLink[i] = boneLink[weights[i].boneIndex0];
            }

            // Repères de prise, en espace local de la main (pose de repos).
            var wrist = Find("Wrist");
            var palm = Find("Palm");
            var index = Find("IndexProximal");
            var little = Find("LittleProximal");
            if (wrist != null && palm != null && index != null && little != null)
            {
                // Point de contact : entre le talon de la main et la base des doigts (pli de la paume).
                var knuckles = (index.position + little.position) * 0.5f;
                var contact = Vector3.Lerp(palm.position, knuckles, m_PalmContact);
                m_PalmLocal = transform.InverseTransformPoint(contact);
                // Les doigts se plient vers -Y des os : la paume regarde donc -Y du poignet.
                var normalWorld = -wrist.up;
                m_PalmNormalLocal = transform.InverseTransformDirection(normalWorld).normalized;
                var grip = transform.InverseTransformDirection(index.position - little.position);
                m_GripAxisLocal = Vector3.ProjectOnPlane(grip, m_PalmNormalLocal).normalized;
                MeasurePalmThickness(contact, normalWorld);

                // Sens de pliage du pouce : plier doit amener le bout du pouce vers le côté paume.
                var thumbProx = m_Bones[0][1];
                if (thumbProx != null && m_Tips[0] != null)
                {
                    m_ThumbSign = 1f;
                    var before = m_Tips[0].position;
                    var rest = thumbProx.localRotation;
                    thumbProx.localRotation = rest * Quaternion.AngleAxis(15f, m_ThumbAxis);
                    var moved = m_Tips[0].position - before;
                    thumbProx.localRotation = rest;
                    if (Vector3.Dot(moved, normalWorld) < 0f) m_ThumbSign = -1f;
                }
            }
        }

        /// <summary>Mesure la distance os -> peau de la paume sur le maillage (pose de repos).</summary>
        void MeasurePalmThickness(Vector3 contact, Vector3 normal)
        {
            var skin = GetComponentInChildren<SkinnedMeshRenderer>();
            if (skin == null || skin.sharedMesh == null) return;
            var baked = new Mesh();
            skin.BakeMesh(baked, true);
            float best = -1f;
            foreach (var v in baked.vertices)
            {
                var d = skin.transform.TransformPoint(v) - contact;
                float along = Vector3.Dot(d, normal);
                if ((d - normal * along).magnitude < 0.008f && along > best) best = along;
            }
            if (Application.isPlaying) Destroy(baked); else DestroyImmediate(baked);
            if (best > 0.002f) m_PalmThickness = best;
        }
    }
}
