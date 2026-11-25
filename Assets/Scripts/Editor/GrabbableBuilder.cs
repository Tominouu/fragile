using System.IO;
using System.Linq;
using Fragile.Breakables;
using Fragile.Grab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Fragile > Rendre les bouteilles attrapables :
    /// tous les objets cassables (bouteilles du comptoir, puffs…) deviennent attrapables à la main,
    /// avec la même prise que la batte (doigts posés sur la vraie forme), axe vertical.
    /// </summary>
    public static class GrabbableBuilder
    {
        const string BottlesFolder = "Assets/Bar/Models/Bouteilles";

        [MenuItem("Fragile/Rendre les bouteilles attrapables")]
        public static void MakeAllGrabbable()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            int prefabs = 0, sceneObjects = 0;

            // 1) Préfabs des bouteilles du comptoir.
            if (Directory.Exists(BottlesFolder))
                foreach (var path in Directory.GetFiles(BottlesFolder, "*.prefab", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')))
                {
                    var root = PrefabUtility.LoadPrefabContents(path);
                    if (MakeGrabbable(root)) prefabs++;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    PrefabUtility.UnloadPrefabContents(root);
                }

            // 2) Objets cassables de la scène (puffs, etc.) qui ne viennent pas de ces préfabs.
            foreach (var br in Object.FindObjectsByType<Breakable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.RegisterFullObjectHierarchyUndo(br.gameObject, "Rendre attrapable");
                if (MakeGrabbable(br.gameObject)) sceneObjects++;
            }

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Fragile] Attrapables à la main : {prefabs} type(s) de bouteille + {sceneObjects} objet(s) de la scène.");
        }

        /// <returns>true si des composants ont été ajoutés.</returns>
        public static bool MakeGrabbable(GameObject go)
        {
            if (go.GetComponentInParent<Breaker>() != null) return false; // la batte a déjà sa prise
            if (!go.TryGetComponent<Rigidbody>(out _)) return false;
            bool added = false;

            if (!go.TryGetComponent<GripSurface>(out var grip)) { grip = go.AddComponent<GripSurface>(); added = true; }
            var gso = new SerializedObject(grip);
            gso.FindProperty("m_LocalAxis").vector3Value = Vector3.up; // bouteille debout
            gso.FindProperty("m_EndMargin").floatValue = 0.03f;
            gso.ApplyModifiedPropertiesWithoutUndo();

            if (!go.TryGetComponent<XRGrabInteractable>(out var grab)) { grab = go.AddComponent<HandGripGrabInteractable>(); added = true; }
            grab.selectMode = InteractableSelectMode.Multiple;   // à deux mains aussi
            grab.useDynamicAttach = true;
            grab.matchAttachPosition = true; grab.matchAttachRotation = true;
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking; // tenue en main, elle cogne vraiment (et peut casser)
            grab.throwOnDetach = true;
            grab.predictedVisualsTransform = null;
            EditorUtility.SetDirty(go);
            return added;
        }
    }
}
