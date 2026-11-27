using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Fragile > Trailer > Diagnostiquer les clients : anime chaque client hors scène (scène de prévisualisation) avec
    /// les clips du contrôleur (debout, assis, fuite), vérifie que la peau suit le squelette (aucun sommet à plus de
    /// 1,3 m des hanches = pas de membre étiré) et enregistre un rendu de chaque pose dans Temp/TrailerDiag.
    /// Se lance aussi tout seul après une recompilation si le fichier Temp/fragile_trailer_diag existe.
    /// </summary>
    [InitializeOnLoad]
    public static class TrailerClientsDiagnostic
    {
        const string Flag = "Temp/fragile_trailer_diag";
        const string OutDir = "Temp/TrailerDiag";

        static TrailerClientsDiagnostic()
        {
            if (!File.Exists(Flag)) return;
            File.Delete(Flag);
            EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) Run(); };
        }

        [MenuItem("Fragile/Trailer/Diagnostiquer les clients", priority = 405)]
        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            var report = new StringBuilder();
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Trailer/Clients/Clients.controller");
            var clips = new List<(string, AnimationClip)>();
            if (ctrl != null)
                foreach (var st in ctrl.layers[0].stateMachine.states)
                    if (st.state.motion is AnimationClip c) clips.Add((st.state.name, c));
            if (clips.Count == 0) report.AppendLine("Pas de Clients.controller : diagnostic en pose de repos seulement.");
            clips.Insert(0, ("Repos", null));

            int bad = 0, total = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Trailer/Clients" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                report.AppendLine($"== {model.name} (import {(imp != null ? imp.animationType.ToString() : "?")})");
                foreach (var (state, clip) in clips)
                {
                    total++;
                    try
                    {
                        float worst = Check(model, clip, Path.Combine(OutDir, $"{model.name}_{state}.png"), out string detail);
                        bool ok = worst < 1.3f;
                        if (!ok) bad++;
                        report.AppendLine($"  {state,-12} {(ok ? "OK " : "ÉTIRÉ")} sommet le plus loin des hanches : {worst:F2} m {detail}");
                    }
                    catch (System.Exception e)
                    {
                        bad++;
                        report.AppendLine($"  {state,-12} ERREUR {e.GetType().Name} : {e.Message}");
                    }
                }
            }
            report.Insert(0, $"Diagnostic des clients : {total - bad}/{total} poses correctes.\n");
            File.WriteAllText(Path.Combine(OutDir, "rapport.txt"), report.ToString());
            if (bad == 0) Debug.Log("[Fragile] " + report); else Debug.LogWarning("[Fragile] " + report);
        }

        static float Check(GameObject model, AnimationClip clip, string png, out string detail)
        {
            detail = "";
            var scene = EditorSceneManager.NewPreviewScene();
            PlayableGraph graph = default;
            try
            {
                var go = (GameObject)Object.Instantiate(model);
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var anim = go.GetComponent<Animator>();
                var arm = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.LeftUpperArm) : null;
                var restArm = arm != null ? arm.localRotation : Quaternion.identity;
                bool sampled = false;
                if (clip != null && anim != null)
                {
                    anim.Rebind();
                    graph = PlayableGraph.Create("Diagnostic");
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var output = AnimationPlayableOutput.Create(graph, "Sortie", anim);
                    var play = AnimationClipPlayable.Create(graph, clip);
                    play.SetApplyFootIK(false);
                    output.SetSourcePlayable(play);
                    play.SetTime(clip.length * 0.4f);
                    graph.Play();
                    graph.Evaluate(0f);
                    sampled = arm != null && Quaternion.Angle(arm.localRotation, restArm) > 0.5f;
                    if (!sampled)
                    {
                        // Secours : échantillonnage de l'éditeur.
                        AnimationMode.StartAnimationMode();
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(go, clip, clip.length * 0.4f);
                        AnimationMode.EndSampling();
                        sampled = arm != null && Quaternion.Angle(arm.localRotation, restArm) > 0.5f;
                    }
                }
                var hips = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : go.transform;
                float worst = 0f; string worstName = "";
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var baked = new Mesh();
                    smr.BakeMesh(baked, true);
                    var m = smr.transform.localToWorldMatrix;
                    foreach (var v in baked.vertices)
                    {
                        float d = Vector3.Distance(m.MultiplyPoint3x4(v), hips.position);
                        if (d > worst) { worst = d; worstName = smr.name; }
                    }
                    Object.DestroyImmediate(baked);
                }
                float top = 0f;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y);
                detail = $"({worstName}, hanches à {hips.position.y:F2} m / {top:F2} m)" + (clip != null && !sampled ? " [animation NON appliquée]" : "");
                if (clip == null && (hips.position.y < top * 0.4f || hips.position.y > top * 0.7f)) worst = 99f;   // squelette décalé
                // Orientation : le personnage doit regarder vers +Z (l'avant de Unity), sinon il s'assoit dos au comptoir.
                if (anim != null && anim.isHuman)
                {
                    var right = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg).position - anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
                    var facing = Vector3.Cross(right, Vector3.up);
                    if (facing.z < 0.3f * facing.magnitude) { worst = 98f; detail += $" [regarde vers ({facing.normalized.x:F1}, {facing.normalized.z:F1}) au lieu de +Z : à l'envers]"; }
                }
                Render(scene, png, hips.position);
                if (clip == null)
                {
                    // Gros plan du visage + relevé des matériaux tels qu'importés.
                    var head = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
                    if (head != null) Render(scene, png.Replace(".png", "_visage.png"), head.position, true);
                    var sb = new StringBuilder();
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null) { sb.AppendLine($"    {r.name} : AUCUN matériau"); continue; }
                            var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                            var col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.clear;
                            sb.AppendLine($"    {r.name,-40} {m.shader.name} surface={Get(m, "_Surface")} clip={Get(m, "_AlphaClip")} cutoff={Get(m, "_Cutoff")} " +
                                          $"couleur=({col.r:F2},{col.g:F2},{col.b:F2},{col.a:F2}) texture={(tex != null ? tex.name : "AUCUNE")} file={m.renderQueue}");
                        }
                    detail += "\n" + sb.ToString().TrimEnd();
                }
                return worst;
            }
            finally
            {
                if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                if (graph.IsValid()) graph.Destroy();
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static string Get(Material m, string p) => m.HasProperty(p) ? m.GetFloat(p).ToString("0.##") : "-";

        static void Render(Scene scene, string png, Vector3 focus, bool face = false)
        {
            foreach (var old in scene.GetRootGameObjects()) if (old.name == "Lumière" || old.name == "Caméra") Object.DestroyImmediate(old);
            var lightGo = new GameObject("Lumière");
            SceneManager.MoveGameObjectToScene(lightGo, scene);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(35f, 150f, 0f);

            var camGo = new GameObject("Caméra");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.25f, 0.25f, 0.28f);
            cam.fieldOfView = face ? 20f : 35f;
            cam.nearClipPlane = 0.05f;
            if (face)
            {
                cam.transform.position = focus + new Vector3(0.15f, 0.08f, 0.75f);
                cam.transform.LookAt(focus + Vector3.up * 0.06f);
            }
            else
            {
                cam.transform.position = new Vector3(1.6f, 1.3f, 3.2f);
                cam.transform.LookAt(new Vector3(0f, Mathf.Max(0.8f, focus.y), 0f));
            }

            var rt = new RenderTexture(512, 640, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(png, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
