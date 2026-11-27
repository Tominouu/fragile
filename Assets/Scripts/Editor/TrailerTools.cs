using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.Animations;
using Fragile.Trailer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Fragile > Trailer > … : la scène Trailer ne contient qu'un objet (TrailerDirector) et charge la vraie scène Bar
    /// au lancement. Aucune de ces commandes n'enregistre Bar.unity : la scène de Tom n'est jamais modifiée.
    /// </summary>
    public static class TrailerTools
    {
        public const string TrailerPath = "Assets/Scenes/Trailer.unity";
        const string BarPath = "Assets/Scenes/Bar.unity";
        const string OverlayShaderPath = "Assets/Trailer/TrailerOverlay.shader";
        const string UnlitShaderPath = "Assets/Trailer/TrailerUnlit.shader";

        [MenuItem("Fragile/Trailer/Installer la scène Trailer", priority = 400)]
        public static void Install()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = SceneManager.GetSceneByPath(TrailerPath);
            bool wasOpen = scene.isLoaded;
            if (!wasOpen)
                scene = File.Exists(TrailerPath)
                    ? EditorSceneManager.OpenScene(TrailerPath, OpenSceneMode.Additive)
                    : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            TrailerDirector director = null;
            foreach (var root in scene.GetRootGameObjects())
                if ((director = root.GetComponentInChildren<TrailerDirector>(true)) != null) break;
            if (director == null)
            {
                var go = new GameObject("Trailer");
                SceneManager.MoveGameObjectToScene(go, scene);
                director = go.AddComponent<TrailerDirector>();
            }

            var so = new SerializedObject(director);
            so.FindProperty("m_OverlayShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(OverlayShaderPath);
            so.FindProperty("m_UnlitShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(UnlitShaderPath);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, TrailerPath);
            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);

            // Build : Trailer en premier (l'application démarre sur l'intro), Bar juste après.
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(s => s.path == TrailerPath);
            list.Insert(0, new EditorBuildSettingsScene(TrailerPath, true));
            if (!list.Exists(s => s.path == BarPath)) list.Add(new EditorBuildSettingsScene(BarPath, true));
            foreach (var s in list) if (s.path == BarPath) s.enabled = true;
            EditorBuildSettings.scenes = list.ToArray();

            Debug.Log($"[Fragile] Scène Trailer prête ({TrailerPath}), première du build. Elle charge « Bar » au lancement : rien n'est copié.");
            if (AssetDatabase.IsValidFolder("Assets/Trailer/Clients")) PrepareClients();
        }

        // ------------------------------------------------------------------ clients du bar

        const string ClientsDir = "Assets/Trailer/Clients", AnimationsDir = "Assets/Trailer/Animations";
        const string ControllerPath = ClientsDir + "/Clients.controller";

        /// <summary>
        /// Branche les clients (modèles de Assets/Trailer/Clients) et leurs animations (Assets/Trailer/Animations) :
        /// construit l'Animator Controller (états Debout, Assis, Discute, Fuite, SeLever) en choisissant les clips par
        /// mots-clés, puis les renseigne dans la scène Trailer.
        /// </summary>
        [MenuItem("Fragile/Trailer/Préparer les clients du bar", priority = 404)]
        public static void PrepareClients()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            var models = new List<GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ClientsDir }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go != null) models.Add(go);
            }
            // Importés avant les réglages Humanoid (TrailerClientsImport) ? On les réimporte.
            bool reimported = false;
            foreach (var m in models)
            {
                var path = AssetDatabase.GetAssetPath(m);
                if (AssetImporter.GetAtPath(path) is ModelImporter mi && mi.animationType != ModelImporterAnimationType.Human)
                { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); reimported = true; }
            }
            if (reimported)
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ClientsDir }))
                    AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            models.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            if (models.Count == 0) { Debug.LogWarning("[Fragile] Aucun client dans " + ClientsDir + " (Tools/Blender/generate_clients.py)."); return; }

            var clips = new List<AnimationClip>();
            if (AssetDatabase.IsValidFolder(AnimationsDir))
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { AnimationsDir }))
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                        if (o is AnimationClip c && !c.name.StartsWith("__preview__") && !clips.Contains(c)) clips.Add(c);
            if (clips.Count == 0)
            {
                Debug.LogWarning("[Fragile] Pas d'animations : dépose les FBX de la Universal Animation Library (Quaternius, CC0) dans " + AnimationsDir + ", puis relance ce menu.");
                return;
            }

            var stand = Pick(clips, new[] { "idle" }, new[] { "loop" }, new[] { "sit", "crouch", "gun", "pistol", "torch", "spell", "sword", "swim", "combat", "fight", "zombie", "crawl", "talk" });
            var sit = Pick(clips, new[] { "sit" }, new[] { "idle", "loop", "chair" }, new[] { "enter", "exit", "stand", "ground", "floor", "talk" });
            var sitTalk = Pick(clips, new[] { "sit", "talk" }, new[] { "loop" }, new[] { "enter", "exit" });
            var talk = Pick(clips, new[] { "talk" }, new[] { "loop" }, new[] { "sit", "phone" }) ?? Pick(clips, new[] { "convers" }, new string[0], new[] { "sit" });
            var run = Pick(clips, new[] { "sprint" }, new[] { "loop", "fwd", "forward" }, new[] { "gun", "left", "right", "back", "stop", "start", "jump" })
                      ?? Pick(clips, new[] { "run" }, new[] { "loop", "fwd", "forward" }, new[] { "gun", "left", "right", "back", "stop", "start", "jump" })
                      ?? Pick(clips, new[] { "jog" }, new[] { "fwd", "forward" }, new[] { "left", "right", "back" });
            var rise = Pick(clips, new[] { "sit" }, new[] { "exit", "stand", "up" }, new[] { "enter", "idle", "loop", "down" });
            if (rise != null && !(rise.name.ToLowerInvariant().Contains("exit") || rise.name.ToLowerInvariant().Contains("stand"))) rise = null;

            var all = string.Join(", ", clips.Select(c => c.name));
            if (stand == null || sit == null || run == null)
            {
                Debug.LogError($"[Fragile] Animations insuffisantes (debout : {Name(stand)}, assis : {Name(sit)}, course : {Name(run)}). Clips trouvés : {all}");
                return;
            }

            if (File.Exists(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var layers = ctrl.layers;
            layers[0].iKPass = true;   // regards (OnAnimatorIK)
            ctrl.layers = layers;
            var sm = ctrl.layers[0].stateMachine;
            sm.defaultState = State(sm, "Debout", stand);
            State(sm, "Assis", sit);
            State(sm, "Discute", talk != null ? talk : stand);
            State(sm, "Fuite", run);
            if (rise != null) State(sm, "SeLever", rise);
            State(sm, "AssisDiscute", sitTalk != null ? sitTalk : sit);
            AssetDatabase.SaveAssets();

            // Renseigne la scène Trailer.
            var scene = SceneManager.GetSceneByPath(TrailerPath);
            bool wasOpen = scene.isLoaded;
            if (!wasOpen)
            {
                if (!File.Exists(TrailerPath)) { Debug.LogWarning("[Fragile] Installe d'abord la scène Trailer."); return; }
                scene = EditorSceneManager.OpenScene(TrailerPath, OpenSceneMode.Additive);
            }
            foreach (var root in scene.GetRootGameObjects())
            {
                var d = root.GetComponentInChildren<TrailerDirector>(true);
                if (d == null) continue;
                var so = new SerializedObject(d);
                var arr = so.FindProperty("m_Clients");
                arr.arraySize = models.Count;
                for (int i = 0; i < models.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = models[i];
                so.FindProperty("m_ClientController").objectReferenceValue = ctrl;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.SaveScene(scene);
            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);

            Debug.Log($"[Fragile] {models.Count} clients prêts. Animations : debout « {Name(stand)} », assis « {Name(sit)} », assis en discutant « {Name(sitTalk ?? sit)} », discute « {Name(talk ?? stand)} », " +
                      $"fuite « {Name(run)} », se lever « {Name(rise)} ». (Tous les clips : {all})");
        }

        static string Name(AnimationClip c) => c != null ? c.name : "—";

        static AnimatorState State(AnimatorStateMachine sm, string name, AnimationClip clip)
        {
            var st = sm.AddState(name);
            st.motion = clip;
            st.writeDefaultValues = true;
            st.iKOnFeet = name == "Debout" || name == "Discute";
            return st;
        }

        /// <summary>Le clip qui contient tous les mots <paramref name="must"/>, aucun de <paramref name="avoid"/>, le plus de <paramref name="prefer"/>.</summary>
        static AnimationClip Pick(List<AnimationClip> clips, string[] must, string[] prefer, string[] avoid)
        {
            AnimationClip best = null; float score = float.NegativeInfinity;
            foreach (var c in clips)
            {
                var n = c.name.ToLowerInvariant();
                if (must.Any(w => !n.Contains(w)) || avoid.Any(w => n.Contains(w))) continue;
                float s = prefer.Count(w => n.Contains(w)) * 10f - n.Length * 0.05f;
                if (s > score) { score = s; best = c; }
            }
            return best;
        }

        [MenuItem("Fragile/Trailer/Vérifier la synchro avec le Bar", priority = 401)]
        public static void Check()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            var bar = OpenBar(out bool opened);
            if (!bar.IsValid()) return;
            var anchors = TrailerAnchors.Find(bar, FindViewPoint());
            if (anchors.Ok) Debug.Log(anchors.Report()); else Debug.LogWarning(anchors.Report());
            if (!File.Exists(TrailerPath)) Debug.LogWarning("[Fragile] Scène Trailer absente : Fragile > Trailer > Installer la scène Trailer.");
            if (opened) EditorSceneManager.CloseScene(bar, true);
        }

        [MenuItem("Fragile/Trailer/Aperçu de la place du joueur", priority = 402)]
        public static void Preview()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            var bar = OpenBar(out bool opened);
            if (!bar.IsValid()) return;
            var anchors = TrailerAnchors.Find(bar, FindViewPoint());
            Debug.Log(anchors.Report());
            if (anchors.Stool == null) return;

            // Cadre la vue de scène exactement depuis les yeux du joueur assis.
            var view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                var tmp = new GameObject("[Trailer] vue") { hideFlags = HideFlags.HideAndDontSave };
                tmp.transform.SetPositionAndRotation(anchors.Eye, Quaternion.LookRotation(anchors.LookAt - anchors.Eye));
                view.AlignViewToObject(tmp.transform);
                Object.DestroyImmediate(tmp);
                view.Repaint();
            }
            Selection.activeTransform = anchors.Stool;
            if (opened)
                Debug.Log("[Fragile] Bar ouvert en aperçu à côté du Trailer : ne l'enregistre pas d'ici (c'est la scène de Tom). " +
                          "Clic droit sur la scène Bar > Remove Scene pour la refermer.");
        }

        [MenuItem("Fragile/Trailer/Jouer le Trailer", priority = 403)]
        public static void Play()
        {
            if (EditorApplication.isPlaying) return;
            if (!File.Exists(TrailerPath)) Install();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(TrailerPath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        static Scene OpenBar(out bool opened)
        {
            opened = false;
            var bar = SceneManager.GetSceneByPath(BarPath);
            if (bar.isLoaded) return bar;
            if (!File.Exists(BarPath)) { Debug.LogError("[Fragile] " + BarPath + " introuvable."); return default; }
            opened = true;
            return EditorSceneManager.OpenScene(BarPath, OpenSceneMode.Additive);
        }

        static Transform FindViewPoint()
        {
            var trailer = SceneManager.GetSceneByPath(TrailerPath);
            if (!trailer.isLoaded) return null;
            foreach (var root in trailer.GetRootGameObjects())
            {
                var d = root.GetComponentInChildren<TrailerDirector>(true);
                if (d == null) continue;
                var p = new SerializedObject(d).FindProperty("m_ViewPoint");
                return p != null ? p.objectReferenceValue as Transform : null;
            }
            return null;
        }
    }
}
