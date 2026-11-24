using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Construit la pièce du bar (sol, murs, plafond) dans Assets/Scenes/Bar.unity.
    /// Menu : Fragile > Construire la pièce du Bar. Relancer reconstruit la pièce à neuf.
    /// </summary>
    public static class BarRoomBuilder
    {
        const string ScenePath = "Assets/Scenes/Bar.unity";
        const string ArtFolder = "Assets/Bar";
        const string RoomName = "Room";

        const float Size = 10f;       // intérieur : 10 x 10 m
        const float Height = 3f;      // hauteur sous plafond
        const float Thickness = 0.1f; // épaisseur sol / murs / plafond

        [MenuItem("Fragile/Construire la pièce du Bar")]
        public static void Build()
        {
            if (!OpenBarScene())
                return;

            EnsureFolder("Assets", "Bar");
            EnsureFolder(ArtFolder, "Materials");
            EnsureFolder(ArtFolder, "Textures");

            // Texture couvrant 2 x 2 m, répétée 5 fois sur 10 m.
            var floorMat = GetOrCreateMaterial("Floor_Wood", Color.white, GetOrCreatePlankTexture(), new Vector2(5f, 5f), 0.3f);
            var wallMat = GetOrCreateMaterial("Wall", new Color(0.85f, 0.82f, 0.76f), null, Vector2.one, 0.1f);
            var ceilingMat = GetOrCreateMaterial("Ceiling", new Color(0.93f, 0.93f, 0.91f), null, Vector2.one, 0.05f);

            var old = GameObject.Find(RoomName);
            if (old != null)
                Undo.DestroyObjectImmediate(old);

            var room = new GameObject(RoomName).transform;

            float outer = Size + 2f * Thickness;
            float half = Size / 2f + Thickness / 2f;

            var floor = CreatePart("Floor", room, new Vector3(0f, -Thickness / 2f, 0f), new Vector3(outer, Thickness, outer), floorMat);
            MakeTeleportable(floor);
            CreatePart("Ceiling", room, new Vector3(0f, Height + Thickness / 2f, 0f), new Vector3(outer, Thickness, outer), ceilingMat);
            CreatePart("Wall_North", room, new Vector3(0f, Height / 2f, half), new Vector3(outer, Height, Thickness), wallMat);
            CreatePart("Wall_South", room, new Vector3(0f, Height / 2f, -half), new Vector3(outer, Height, Thickness), wallMat);
            CreatePart("Wall_East", room, new Vector3(half, Height / 2f, 0f), new Vector3(Thickness, Height, Size), wallMat);
            CreatePart("Wall_West", room, new Vector3(-half, Height / 2f, 0f), new Vector3(Thickness, Height, Size), wallMat);

            // Pièce fermée : la lumière directionnelle n'entre plus, on éclaire depuis le plafond.
            var lightGo = new GameObject("Ceiling Light");
            lightGo.transform.SetParent(room, false);
            lightGo.transform.localPosition = new Vector3(0f, Height - 0.3f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 12f;
            light.intensity = 2f;
            light.color = new Color(1f, 0.92f, 0.8f);
            light.shadows = LightShadows.Soft;

            Undo.RegisterCreatedObjectUndo(room.gameObject, "Construire la pièce du Bar");
            Selection.activeGameObject = room.gameObject;

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Fragile] Pièce du bar construite : {Size} x {Size} m, {Height} m sous plafond.");
        }

        const string XROriginPrefabPath = "Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Hands Variant.prefab";
        const string HandsPermissionsPrefabPath = "Assets/VRTemplateAssets/Prefabs/Setup/Hands Permissions Manager.prefab";

        [MenuItem("Fragile/Ajouter le XR Origin au Bar")]
        public static void AddXROrigin()
        {
            if (!OpenBarScene())
                return;

            // Le XR Origin a sa propre caméra : l'ancienne Main Camera ferait doublon.
            var oldCamera = GameObject.Find("Main Camera");
            if (oldCamera != null && PrefabUtility.GetCorrespondingObjectFromSource(oldCamera) == null)
                Undo.DestroyObjectImmediate(oldCamera);

            AddPrefabOnce(XROriginPrefabPath, Vector3.zero);
            AddPrefabOnce(HandsPermissionsPrefabPath, Vector3.zero);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[Fragile] XR Origin ajouté à la scène Bar.");
        }

        static void AddPrefabOnce(string prefabPath, Vector3 position)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[Fragile] Prefab introuvable : {prefabPath}");
                return;
            }

            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(root) == prefab)
                {
                    Debug.Log($"[Fragile] {prefab.name} est déjà dans la scène, ignoré.");
                    return;
                }
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = position;
            Undo.RegisterCreatedObjectUndo(instance, $"Ajouter {prefab.name}");
            Selection.activeGameObject = instance;
        }

        static bool OpenBarScene()
        {
            if (EditorSceneManager.GetActiveScene().path == ScenePath)
                return true;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return true;
        }

        /// <summary>
        /// Zone de téléportation, réglée comme dans SampleScene : layer d'interaction "Teleport"
        /// (celui que vise le Teleport Interactor du XR Origin), orientation gardée = direction du joystick.
        /// </summary>
        static void MakeTeleportable(GameObject go)
        {
            var area = go.AddComponent<TeleportationArea>();
            area.interactionLayers = InteractionLayerMask.GetMask("Teleport");
            area.matchOrientation = MatchOrientation.None;
            area.matchDirectionalInput = true;
            area.teleportTrigger = BaseTeleportationInteractable.TeleportTrigger.OnSelectExited;
        }

        static GameObject CreatePart(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); // garde son BoxCollider
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
            return go;
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
                AssetDatabase.CreateFolder(parent, name);
        }

        static Material GetOrCreateMaterial(string name, Color color, Texture2D texture, Vector2 tiling, float smoothness)
        {
            string path = $"{ArtFolder}/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.color = color;
            mat.mainTexture = texture;
            mat.mainTextureScale = tiling;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Génère une texture de parquet simple : 10 lames de 20 cm sur 2 m, joints décalés.</summary>
        static Texture2D GetOrCreatePlankTexture()
        {
            string path = $"{ArtFolder}/Textures/Floor_Planks.png";
            if (!File.Exists(path))
            {
                const int res = 1024;
                const int planks = 10;
                const int gap = 3;
                int plankWidth = res / planks;
                int segment = res / 2; // 2 morceaux de lame par répétition (1 m chacun)

                var rng = new System.Random(42);
                var baseColor = new Color(0.58f, 0.40f, 0.25f);
                var gapColor = new Color(0.22f, 0.14f, 0.08f);
                var pixels = new Color[res * res];

                for (int p = 0; p < planks; p++)
                {
                    int joint = rng.Next(0, res);
                    float[] tint = { 0.85f + (float)rng.NextDouble() * 0.3f, 0.85f + (float)rng.NextDouble() * 0.3f };

                    for (int x = p * plankWidth; x < (p + 1) * plankWidth; x++)
                    {
                        for (int y = 0; y < res; y++)
                        {
                            int rel = (y - joint + res) % res;
                            int seg = rel / segment;
                            bool isGap = x - p * plankWidth < gap || rel % segment < gap;

                            Color c;
                            if (isGap)
                            {
                                c = gapColor;
                            }
                            else
                            {
                                // Veinage étiré dans le sens de la lame.
                                float grain = Mathf.PerlinNoise(x * 0.09f + p * 13.7f, y * 0.005f + seg * 7.1f);
                                float fine = Mathf.PerlinNoise(x * 0.6f + p * 3.1f, y * 0.02f);
                                c = baseColor * tint[seg] * (0.82f + 0.25f * grain + 0.08f * fine);
                                c.a = 1f;
                            }
                            pixels[y * res + x] = c;
                        }
                    }
                }

                var tex = new Texture2D(res, res, TextureFormat.RGB24, false);
                tex.SetPixels(pixels);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);

                AssetDatabase.ImportAsset(path);
                if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                {
                    importer.wrapMode = TextureWrapMode.Repeat;
                    importer.anisoLevel = 4;
                    importer.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
