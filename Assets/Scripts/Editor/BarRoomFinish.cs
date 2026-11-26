using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Habillage de la pièce, appelé par « Meubler le Bar » : lambris en bois sombre à mi-hauteur sur tous les murs,
    /// mur de briques au nord (derrière l'enseigne), poutres apparentes au plafond, porte de sortie (mur ouest)
    /// avec son panneau « SORTIE », tapis sous le coin salon et le canapé.
    /// Textures Poly Haven (CC0) dans Assets/Bar/Textures/PolyHaven. Les maillages (UV en mètres, donc textures
    /// à la bonne échelle quelle que soit la taille) sont enregistrés dans Assets/Bar/Models/Mobilier/Habillage.
    /// Rien de tout ça n'est cassable : ce sont les murs et le plafond.
    /// </summary>
    public static class BarRoomFinish
    {
        const string TexDir = "Assets/Bar/Textures/PolyHaven";
        const string MeshDir = "Assets/Bar/Models/Mobilier/Habillage";
        const float WallW = -5f, WallE = 5f, WallS = -5f, WallN = 8.36f, Ceiling = 3f;
        const float WainscotH = 1.0f;                 // hauteur du lambris
        public const float DoorZ = -1.66f;            // porte au milieu de la travée ouest (entre deux appliques)
        const float DoorW = 0.9f, DoorH = 2.1f;

        public static void Build(Transform parent)
        {
            var root = new GameObject("Habillage").transform;
            root.SetParent(parent, false);

            var planks = Textured("Lambris", "dark_wooden_planks", 0.35f, new Color(0.85f, 0.78f, 0.72f));
            var brick = Textured("Brique", "red_brick_03", 0.15f, new Color(0.92f, 0.86f, 0.82f));
            var rug = Textured("Tapis", "quatrefoil_jacquard_fabric", 0.05f, new Color(0.75f, 0.35f, 0.3f));
            var beam = Textured("Poutre", "dark_wooden_planks", 0.2f, new Color(0.55f, 0.45f, 0.38f));
            var trim = AssetDatabase.LoadAssetAtPath<Material>("Assets/Bar/Lighting/Corniche_Bois.mat") ?? beam;
            var brass = AssetDatabase.LoadAssetAtPath<Material>("Assets/Bar/Lighting/Laiton.mat");

            // --- Lambris (panneaux + cimaise) : 2 cm d'épaisseur, contre chaque mur ; ouvert à la porte.
            const float t = 0.02f;
            float zMid = (WallS + WallN) / 2f, lenNS = WallN - WallS;
            var wainscot = new GameObject("Lambris").transform; wainscot.SetParent(root, false);
            void Panel(Vector3 c, Vector3 s)
            {
                Box(wainscot, "Lambris", c, s, planks, 1.2f);
                // Cimaise : baguette en haut du lambris.
                var rail = new Vector3(s.x > s.z ? s.x : 0.035f, 0.045f, s.z > s.x ? s.z : 0.035f);
                var inward = s.x > s.z ? new Vector3(0f, 0f, c.z > zMid ? -1f : 1f) : new Vector3(c.x > 0f ? -1f : 1f, 0f, 0f);
                Box(wainscot, "Cimaise", new Vector3(c.x, WainscotH, c.z) + inward * 0.012f, rail, trim, 1f);
            }
            Panel(new Vector3(0f, WainscotH / 2f, WallN - t / 2f), new Vector3(WallE - WallW, WainscotH, t));
            Panel(new Vector3(0f, WainscotH / 2f, WallS + t / 2f), new Vector3(WallE - WallW, WainscotH, t));
            Panel(new Vector3(WallE - t / 2f, WainscotH / 2f, zMid), new Vector3(t, WainscotH, lenNS));
            float d0 = DoorZ - DoorW / 2f - 0.06f, d1 = DoorZ + DoorW / 2f + 0.06f;
            Panel(new Vector3(WallW + t / 2f, WainscotH / 2f, (WallS + d0) / 2f), new Vector3(t, WainscotH, d0 - WallS));
            Panel(new Vector3(WallW + t / 2f, WainscotH / 2f, (d1 + WallN) / 2f), new Vector3(t, WainscotH, WallN - d1));

            // --- Mur de briques au nord, au-dessus du lambris (derrière l'enseigne néon).
            Box(root, "Mur de briques", new Vector3(0f, (WainscotH + Ceiling) / 2f, WallN - 0.006f), new Vector3(WallE - WallW, Ceiling - WainscotH, 0.012f), brick, 2f);

            // --- Poutres apparentes (est-ouest). Positions choisies entre les suspensions, la platine des télés et le ventilateur.
            var beams = new GameObject("Poutres").transform; beams.SetParent(root, false);
            foreach (var z in new[] { -3.4f, -1.55f, 0.3f, 2.15f, 4.0f, 5.85f, 7.7f })
                Box(beams, "Poutre", new Vector3(0f, Ceiling - 0.08f, z), new Vector3(WallE - WallW, 0.16f, 0.14f), beam, 1.5f);

            // --- Tapis (sans collider : on se téléporte et on pose les meubles à travers).
            Box(root, "Tapis salon", new Vector3(3.45f, 0.005f, 7.25f), new Vector3(2.2f, 0.01f, 1.7f), rug, 1f);
            Box(root, "Tapis canapé", new Vector3(-3.0f, 0.005f, 7.45f), new Vector3(2.1f, 0.01f, 1.35f), rug, 1f);

            BuildDoor(root, planks, trim, brass);
        }

        static void BuildDoor(Transform root, Material planks, Material trim, Material brass)
        {
            var door = new GameObject("Porte de sortie").transform; door.SetParent(root, false);
            float x = WallW;
            // Chambranle.
            Box(door, "Montant", new Vector3(x + 0.04f, (DoorH + 0.08f) / 2f, DoorZ - DoorW / 2f - 0.04f), new Vector3(0.08f, DoorH + 0.08f, 0.08f), trim, 1f);
            Box(door, "Montant", new Vector3(x + 0.04f, (DoorH + 0.08f) / 2f, DoorZ + DoorW / 2f + 0.04f), new Vector3(0.08f, DoorH + 0.08f, 0.08f), trim, 1f);
            Box(door, "Linteau", new Vector3(x + 0.04f, DoorH + 0.04f, DoorZ), new Vector3(0.08f, 0.08f, DoorW + 0.16f), trim, 1f);
            // Battant (avec deux panneaux moulurés) et poignée.
            Box(door, "Battant", new Vector3(x + 0.025f, DoorH / 2f, DoorZ), new Vector3(0.04f, DoorH, DoorW), planks, 1f);
            Box(door, "Panneau haut", new Vector3(x + 0.05f, 1.48f, DoorZ), new Vector3(0.012f, 0.95f, 0.64f), trim, 1f);
            Box(door, "Panneau bas", new Vector3(x + 0.05f, 0.5f, DoorZ), new Vector3(0.012f, 0.7f, 0.64f), trim, 1f);
            Box(door, "Plaque", new Vector3(x + 0.05f, 1.0f, DoorZ + 0.36f), new Vector3(0.008f, 0.2f, 0.05f), brass, 1f);
            Box(door, "Poignée", new Vector3(x + 0.09f, 1.03f, DoorZ + 0.32f), new Vector3(0.025f, 0.025f, 0.13f), brass, 1f);
            Box(door, "Barre", new Vector3(x + 0.07f, 1.03f, DoorZ + 0.36f), new Vector3(0.05f, 0.022f, 0.022f), brass, 1f);
            // Une porte fermée ne se traverse pas (le mur derrière a déjà son collider).

            // Panneau lumineux « SORTIE » au-dessus.
            var sign = new GameObject("Panneau SORTIE").transform;
            sign.SetParent(door, false);
            sign.position = new Vector3(x + 0.03f, DoorH + 0.3f, DoorZ);
            sign.rotation = Quaternion.Euler(0f, 90f, 0f);                // +Z local vers la pièce (+X)
            Box(sign, "Boîtier", new Vector3(0f, 0f, 0f), new Vector3(0.4f, 0.15f, 0.05f), ExitMaterial(), 1f, local: true);
            var text = new GameObject("Texte"); text.transform.SetParent(sign, false);
            text.transform.localPosition = new Vector3(0f, 0f, 0.027f);
            text.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // TextMeshPro se lit depuis son −Z
            var tmp = text.AddComponent<TextMeshPro>();
            tmp.text = "SORTIE"; tmp.rectTransform.sizeDelta = new Vector2(0.36f, 0.11f);
            tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.05f; tmp.fontSizeMax = 2f;
            tmp.alignment = TextAlignmentOptions.Center; tmp.fontStyle = FontStyles.Bold; tmp.characterSpacing = 8f;
            tmp.color = new Color(0.92f, 1f, 0.94f);
        }

        // ------------------------------------------------------------------ matériaux

        static Material Textured(string name, string tex, float smoothness, Color tint)
        {
            string path = $"Assets/Bar/Materials/Habillage_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseMap", Tex($"{TexDir}/{tex}/{tex}_diff.jpg", false));
            m.SetColor("_BaseColor", tint);
            var nor = Tex($"{TexDir}/{tex}/{tex}_nor.jpg", true);
            if (nor != null) { m.SetTexture("_BumpMap", nor); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", smoothness); m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material ExitMaterial()
        {
            const string path = "Assets/Bar/Materials/Habillage_Sortie.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            var green = new Color(0.05f, 0.75f, 0.3f);
            m.SetColor("_BaseColor", green);
            m.SetColor("_EmissionColor", green * 2.2f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            m.SetFloat("_Smoothness", 0.6f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D Tex(string path, bool normal)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType != type || ti.maxTextureSize != 1024)
                {
                    ti.textureType = type; ti.maxTextureSize = 1024; ti.wrapMode = TextureWrapMode.Repeat;
                    ti.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ boîtes à UV en mètres

        static readonly Dictionary<string, Mesh> s_Meshes = new Dictionary<string, Mesh>();

        /// <param name="tile">Mètres couverts par une répétition de la texture.</param>
        static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat, float tile, bool local = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (local) go.transform.localPosition = center; else go.transform.position = center;
            go.AddComponent<MeshFilter>().sharedMesh = BoxMesh(size, tile);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            bool big = size.x * size.y + size.y * size.z + size.x * size.z > 0.5f;
            r.receiveGI = big ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
            // Les surfaces plates contre les murs ne projettent rien d'utile (et le mur de briques couperait la directionnelle).
            if (size.x < 0.03f || size.z < 0.03f || size.y < 0.03f) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic
                | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        static Mesh BoxMesh(Vector3 s, float tile)
        {
            string key = $"Boite_{s.x:0.###}x{s.y:0.###}x{s.z:0.###}_{tile:0.##}".Replace(',', '.');
            if (s_Meshes.TryGetValue(key, out var cached) && cached != null) return cached;
            string path = $"{MeshDir}/{key}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) { s_Meshes[key] = mesh; return mesh; }

            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            var h = s / 2f;
            void Face(Vector3 normal, Vector3 u, Vector3 w, float lu, float lw)
            {
                int i = v.Count;
                var c = Vector3.Scale(normal, h);
                var du = u * (lu / 2f); var dw = w * (lw / 2f);
                v.Add(c - du - dw); v.Add(c + du - dw); v.Add(c + du + dw); v.Add(c - du + dw);
                for (int k = 0; k < 4; k++) n.Add(normal);
                uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(lu / tile, 0f)); uv.Add(new Vector2(lu / tile, lw / tile)); uv.Add(new Vector2(0f, lw / tile));
                tri.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
            // u × w = −normal pour que l'ordre (i, i+2, i+1) soit vu de face par Unity.
            Face(Vector3.forward, Vector3.left, Vector3.up, s.x, s.y);
            Face(Vector3.back, Vector3.right, Vector3.up, s.x, s.y);
            Face(Vector3.right, Vector3.forward, Vector3.up, s.z, s.y);
            Face(Vector3.left, Vector3.back, Vector3.up, s.z, s.y);
            Face(Vector3.up, Vector3.right, Vector3.forward, s.x, s.z);
            Face(Vector3.down, Vector3.left, Vector3.forward, s.x, s.z);

            mesh = new Mesh { name = key };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            Unwrapping.GenerateSecondaryUVSet(mesh);
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder("Assets/Bar/Models/Mobilier", "Habillage");
            AssetDatabase.CreateAsset(mesh, path);
            s_Meshes[key] = mesh;
            return mesh;
        }

        public static void ResetCache() => s_Meshes.Clear();
    }
}
