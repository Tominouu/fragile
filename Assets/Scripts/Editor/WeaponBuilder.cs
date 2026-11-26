using System.Collections.Generic;
using Fragile.Bar;
using Fragile.Breakables;
using Fragile.Grab;
using Fragile.Weapons;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Fragile > Placer les armes : fabrique les préfabs du pistolet, de la carabine et du fusil à pompe, leurs munitions
    /// (Assets/Bar/Armes) et les pose dans le bar :
    /// - fusil à pompe sur un râtelier sous l'enseigne FRAGILE, boîte de cartouches sur une tablette dessous ;
    /// - carabine sur un râtelier au mur sud, boîte de cartouches sur les caisses dessous ;
    /// - pistolet sur le comptoir près de la caisse, plateau de chargeurs à côté.
    /// Chaque arme : racine = centre de la poignée ; Visuals > Recul > Modèle ; poignée invisible mesurée par GripSurface ;
    /// poignées secondaires (<see cref="GunHandle"/> : pompe, garde-main, culasse, verrou) ; zone de chargement
    /// (<see cref="LoadZone"/>) ; visée à deux mains (<see cref="TwoHandAim"/>).
    /// Modèles : pistolet et carabine Poly Haven (CC0) ; fusil à pompe et cartouche de calibre 12 : Tools/Blender/build_shotgun.py.
    /// Relançable : l'objet « Armes » de la scène est reconstruit.
    /// </summary>
    public static class WeaponBuilder
    {
        const string Dir = "Assets/Bar/Armes";
        const string Models = Dir + "/Modeles";
        const string FxDir = Dir + "/FX";

        /// <summary>Poignée secondaire, dans le repère A (canon +Z, haut +Y, origine du modèle) sauf si <see cref="part"/> est donné.</summary>
        class HandleSpec
        {
            public string name; public GunHandle.Role role;
            public string part;                 // pièce mobile à laquelle accrocher la poignée (sinon le modèle)
            public Vector3 center, axis;        // repère du modèle (ou de la pièce)
            public float radius, halfLength;
        }

        /// <summary>Description d'une arme, dans le repère « A » : canon vers +Z, haut +Y, origine = celle du modèle.</summary>
        class Spec
        {
            public string name, model; public Firearm.Kind kind;
            public Quaternion modelRot;               // modèle -> repère A
            public Vector3 grip, gripAxis; public Vector3 gripHalf; // centre, axe (auriculaire -> index), demi-tailles (côté, longueur, avant-arrière)
            public Vector3 muzzle, eject;
            public string[] remove = new string[0];   // pièces du modèle à retirer (variantes, munitions posées à côté)
            public string action; public Vector3 actionTravel;   // pièce mobile, course dans le repère du modèle
            public Vector3 boltPivot; public string[] boltParts; public float boltLift;   // carabine : verrou qui pivote
            public string magazine; public Vector3 magazinePos;   // pistolet : chargeur dans la poignée (repère du modèle)
            public HandleSpec[] handles = new HandleSpec[0];
            public Vector3 loadCenter, loadSize;      // zone de chargement, repère A
            public int capacity, startLoaded;
            public int slices; public float mass;
            public int projectiles; public float spread, damage, energy, push, cooldown;
            public float kickBack, kickUp, haptic, flash, breakerPower;
            public Vector2 shell;                     // douille cylindrique (0 = maillage de la munition)
        }

        static readonly Spec Pistol = new Spec
        {
            name = "Pistolet", model = "service_pistol", kind = Firearm.Kind.Pistol,
            modelRot = Quaternion.Euler(0f, 90f, 0f),                        // canon du modèle vers −X
            grip = A90(new Vector3(0.018f, -0.012f, 0f)), gripAxis = A90(new Vector3(-0.14f, 0.99f, 0f)), gripHalf = new Vector3(0.015f, 0.038f, 0.019f),
            muzzle = A90(new Vector3(-0.176f, 0.061f, 0f)), eject = A90(new Vector3(-0.01f, 0.072f, 0.016f)),
            remove = new[] { "_b", "magazine_loaded", "bullet" },
            action = "service_pistol_slide_a", actionTravel = new Vector3(0.025f, 0f, 0f),
            magazine = "service_pistol_magazine_empty", magazinePos = new Vector3(0.013f, -0.001f, 0f),
            handles = new[] { new HandleSpec { name = "Culasse (prise)", role = GunHandle.Role.Slide, part = "service_pistol_slide_a",
                center = new Vector3(0.052f, 0.001f, 0f), axis = Vector3.right, radius = 0.016f, halfLength = 0.03f } },
            loadCenter = A90(new Vector3(0.012f, -0.065f, 0f)), loadSize = new Vector3(0.05f, 0.05f, 0.06f),
            capacity = 8, startLoaded = 7,
            slices = 3, mass = 0.95f,
            projectiles = 1, spread = 0.5f, damage = 0.7f, energy = 0.7f, push = 3f, cooldown = 0.12f,
            kickBack = 0.025f, kickUp = 9f, haptic = 0.75f, flash = 0.1f, breakerPower = 0.6f, shell = new Vector2(0.009f, 0.019f),
        };

        static readonly Spec Rifle = new Spec
        {
            name = "Carabine", model = "bolt_action_rifle_7_62", kind = Firearm.Kind.Rifle,
            modelRot = Quaternion.Euler(0f, 90f, 0f),
            grip = A90(new Vector3(0.345f, -0.02f, 0f)), gripAxis = A90(new Vector3(-0.42f, 0.91f, 0f)), gripHalf = new Vector3(0.019f, 0.045f, 0.021f),
            muzzle = A90(new Vector3(-0.598f, 0.045f, 0f)), eject = A90(new Vector3(0.214f, 0.05f, 0.03f)),
            remove = new[] { "bullet" },
            action = "Verrou", actionTravel = new Vector3(0.07f, 0f, 0f),
            boltPivot = new Vector3(0.237f, 0.052f, 0.0025f), boltParts = new[] { "bolt_action_rifle_7_62_bolt_a", "bolt_action_rifle_7_62_bolt_b" }, boltLift = -62f,
            handles = new[]
            {
                new HandleSpec { name = "Garde-main", role = GunHandle.Role.Support, center = new Vector3(-0.13f, 0.022f, 0f), axis = Vector3.right, radius = 0.021f, halfLength = 0.09f },
                // Boule du levier, dans le repère du pivot du verrou.
                new HandleSpec { name = "Levier (prise)", role = GunHandle.Role.Bolt, part = "Verrou", center = new Vector3(-0.012f, -0.028f, 0.0355f),
                    axis = new Vector3(0f, -0.62f, 0.78f), radius = 0.01f, halfLength = 0.016f },
            },
            loadCenter = A90(new Vector3(0.205f, 0.07f, 0f)), loadSize = new Vector3(0.07f, 0.07f, 0.12f),
            capacity = 5, startLoaded = 4,
            slices = 8, mass = 3.6f,
            projectiles = 1, spread = 0.05f, damage = 1.6f, energy = 1f, push = 6f, cooldown = 0.2f,
            kickBack = 0.05f, kickUp = 10f, haptic = 1f, flash = 0.16f, breakerPower = 1f, shell = Vector2.zero,
        };

        static readonly Spec Shotgun = new Spec
        {
            name = "Fusil à pompe", model = "Fusil_Pompe", kind = Firearm.Kind.Shotgun,
            modelRot = Quaternion.Euler(0f, 180f, 0f),                       // canon du modèle vers −Z
            // Repère A = repère Blender (x, z, y) du script build_shotgun.py ; poignée de la crosse anglaise.
            grip = new Vector3(0f, -0.032f, -0.135f), gripAxis = new Vector3(0f, 0.74f, 0.67f), gripHalf = new Vector3(0.016f, 0.04f, 0.024f),
            muzzle = new Vector3(0f, 0f, 0.67f), eject = new Vector3(0.022f, 0.003f, 0.072f),
            action = "Pompe", actionTravel = new Vector3(0f, 0f, 0.09f),
            handles = new[] { new HandleSpec { name = "Pompe (prise)", role = GunHandle.Role.Pump, part = "Pompe",
                center = new Vector3(0f, 0f, -0.03f), axis = Vector3.forward, radius = 0.026f, halfLength = 0.1f } },
            loadCenter = new Vector3(0f, -0.058f, 0.085f), loadSize = new Vector3(0.07f, 0.06f, 0.13f),
            capacity = 6, startLoaded = 5,
            slices = 8, mass = 3.3f,
            projectiles = 8, spread = 3.5f, damage = 0.3f, energy = 0.75f, push = 1.2f, cooldown = 0.2f,
            kickBack = 0.06f, kickUp = 14f, haptic = 1f, flash = 0.2f, breakerPower = 1f, shell = Vector2.zero,
        };

        /// <summary>Modèle à canon vers −X -> repère A (canon vers +Z) : rotation de 90° autour de Y.</summary>
        static Vector3 A90(Vector3 m) => Quaternion.Euler(0f, 90f, 0f) * m;

        [MenuItem("Fragile/Placer les armes")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Fragile] Quitte le mode Play d'abord."); return; }
            Folder(Dir); Folder(FxDir);
            var fx = BuildFx();

            var ammo = BuildAmmo();
            if (ammo.shell == null || ammo.round == null || ammo.mag == null) return;
            var pistol = MakePrefab(Pistol, fx, ammo.mag, ammo.casingPistol);
            var rifle = MakePrefab(Rifle, fx, ammo.round, ammo.casingRifle);
            var shotgun = MakePrefab(Shotgun, fx, ammo.shell, ammo.casingShell);
            if (pistol == null || rifle == null || shotgun == null) return;

            var old = GameObject.Find("Armes");
            if (old != null) Undo.DestroyObjectImmediate(old);
            var root = new GameObject("Armes").transform;

            // Fusil à pompe : râtelier sous l'enseigne néon (mur de briques nord, canon vers l'est), cartouches sur une tablette dessous.
            Rack(root, shotgun, "Râtelier fusil", new Vector3(-1.45f, 1.5f, 8.348f), Vector3.back, Vector3.right, new[] { -0.32f, 0.27f });
            var shelf = WallShelf(root, "Tablette cartouches", new Vector3(-1.45f, 1.16f, 8.348f), Vector3.back, 0.42f, 0.17f);
            Box(root, "Boîte de cartouches (calibre 12)", ammo.shell, shelf + Vector3.up * 0.001f, Quaternion.Euler(0f, 8f, 0f),
                new Vector3(0.16f, 0.07f, 0.1f), new Color(0.62f, 0.1f, 0.07f), "CAL. 12", 5);
            // Carabine : mur sud, au-dessus des caisses (canon vers l'ouest), cartouches sur la caisse du dessus.
            Rack(root, rifle, "Râtelier carabine", new Vector3(2.45f, 1.45f, -5f), Vector3.forward, Vector3.left, new[] { -0.32f, 0.2f });
            Box(root, "Boîte de cartouches (7,62)", ammo.round, OnSurface(new Vector3(2.4f, 0f, -4.72f)), Quaternion.Euler(0f, 168f, 0f),
                new Vector3(0.12f, 0.05f, 0.08f), new Color(0.75f, 0.62f, 0.36f), "7,62 × 54", 4);
            // Pistolet : sur le comptoir, à côté de la caisse ; plateau de chargeurs juste derrière.
            OnCounter(root, pistol, new Vector3(2.5f, 0f, -0.42f), Vector3.back);
            Box(root, "Plateau de chargeurs", ammo.mag, OnSurface(new Vector3(2.52f, 0f, -0.72f)), Quaternion.Euler(0f, 90f, 0f),
                new Vector3(0.2f, 0.035f, 0.12f), new Color(0.12f, 0.1f, 0.09f), "", 3);

            Undo.RegisterCreatedObjectUndo(root.gameObject, "Placer les armes");
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Fragile] Armes placées : fusil à pompe (sous l'enseigne), carabine (mur sud), pistolet (comptoir), et leurs munitions.");
        }

        // ------------------------------------------------------------------ munitions

        struct Ammo { public AmmoItem shell, round, mag; public (Mesh, Material[]) casingShell, casingRifle, casingPistol; }

        static Ammo BuildAmmo()
        {
            var a = new Ammo();
            BlenderModelImporter.ImportFolder($"{Models}/Cartouche_12");
            foreach (var f in new[] { "Cartouche_12", "bolt_action_rifle_7_62", "service_pistol" }) Readable($"{Models}/{f}/{f}.fbx");
            a.shell = MakeAmmo("Cartouche_12", $"{Models}/Cartouche_12/Cartouche_12.fbx", null, AmmoItem.Kind.Shell, 1, 0.045f, out a.casingShell);
            a.round = MakeAmmo("Cartouche_762", $"{Models}/bolt_action_rifle_7_62/bolt_action_rifle_7_62.fbx", "bolt_action_rifle_7_62_bullet_54mm", AmmoItem.Kind.RifleRound, 1, 0.025f, out a.casingRifle);
            a.mag = MakeAmmo("Chargeur_Pistolet", $"{Models}/service_pistol/service_pistol.fbx", "service_pistol_magazine_empty", AmmoItem.Kind.PistolMagazine, 8, 0.12f, out _);
            var brass = AssetDatabase.LoadAssetAtPath<Material>("Assets/Bar/Lighting/Laiton.mat");
            a.casingPistol = (Resources.GetBuiltinResource<Mesh>("Cylinder.fbx"), new[] { brass });
            return a;
        }

        /// <summary>Munition attrapable : modèle (ou une pièce d'un modèle) centré, capsule, prise le long de son grand axe.</summary>
        static AmmoItem MakeAmmo(string name, string fbx, string part, AmmoItem.Kind kind, int rounds, float mass, out (Mesh, Material[]) casing)
        {
            casing = default;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (src == null) { Debug.LogError($"[Fragile] Modèle absent : {fbx}"); return null; }
            var root = new GameObject(name);
            var m = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(m, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            m.name = "Modèle";
            if (part != null)
            {
                var others = new List<GameObject>();
                foreach (Transform c in m.transform) if (c.name != part) others.Add(c.gameObject);
                foreach (var o in others) Object.DestroyImmediate(o);
            }
            m.transform.SetParent(root.transform, false);
            var mf = m.GetComponentInChildren<MeshFilter>();
            casing = (mf.sharedMesh, mf.GetComponent<MeshRenderer>().sharedMaterials);
            // Centre sur l'origine.
            var b = new Bounds(); bool first = true;
            foreach (var r in m.GetComponentsInChildren<MeshRenderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); r.receiveGI = ReceiveGI.LightProbes; }
            m.transform.position -= b.center;
            var s = b.size; int dir = s.x >= s.y && s.x >= s.z ? 0 : s.y >= s.z ? 1 : 2;
            var axis = dir == 0 ? Vector3.right : dir == 1 ? Vector3.up : Vector3.forward;
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = dir; col.height = s[dir]; col.radius = Mathf.Max(0.004f, Mathf.Min(s[(dir + 1) % 3], s[(dir + 2) % 3]) * 0.5f);
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = mass; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var grip = root.AddComponent<GripSurface>();
            var gso = new SerializedObject(grip);
            gso.FindProperty("m_LocalAxis").vector3Value = axis;
            gso.FindProperty("m_EndMargin").floatValue = 0.005f;
            gso.ApplyModifiedPropertiesWithoutUndo();
            var grab = root.AddComponent<HandGripGrabInteractable>();
            grab.selectMode = InteractableSelectMode.Single;
            grab.useDynamicAttach = true; grab.matchAttachPosition = true; grab.matchAttachRotation = true;
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;   // on vise une trappe de quelques centimètres
            grab.throwOnDetach = true;
            var item = root.AddComponent<AmmoItem>();
            var iso = new SerializedObject(item);
            iso.FindProperty("m_Kind").enumValueIndex = (int)kind;
            iso.FindProperty("m_Rounds").intValue = rounds;
            iso.ApplyModifiedPropertiesWithoutUndo();
            var life = root.AddComponent<TimedDespawn>();
            var lso = new SerializedObject(life);
            lso.FindProperty("m_Lifetime").floatValue = 90f;
            lso.ApplyModifiedPropertiesWithoutUndo();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/{name}.prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<AmmoItem>();
        }

        // ------------------------------------------------------------------ armes

        struct Fx { public Material flash, tracer, hole, red, brass; public GameObject dust; public Mesh shell; }

        static GameObject MakePrefab(Spec s, Fx fx, AmmoItem ammo, (Mesh mesh, Material[] mats) casing)
        {
            BlenderModelImporter.ImportFolder($"{Models}/{s.model}");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Models}/{s.model}/{s.model}.fbx");
            if (model == null) { Debug.LogError($"[Fragile] Modèle absent : {Models}/{s.model} (Tools/Blender)."); return null; }

            var root = new GameObject(s.name);
            var visuals = new GameObject("Visuals").transform; visuals.SetParent(root.transform, false);
            var kick = new GameObject("Recul").transform; kick.SetParent(visuals, false);

            var m = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(m, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            m.name = "Modèle";
            m.transform.SetParent(kick, false);
            m.transform.localRotation = s.modelRot;
            m.transform.localPosition = -(s.modelRot * Vector3.zero) - s.grip;
            foreach (var t in m.GetComponentsInChildren<Transform>(true))
                if (t != null && t != m.transform && System.Array.Exists(s.remove, r => t.name.Contains(r))) Object.DestroyImmediate(t.gameObject);
            foreach (var r in m.GetComponentsInChildren<MeshRenderer>())
            {
                r.receiveGI = ReceiveGI.LightProbes;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            // Carabine : pivot du verrou (le levier tourne autour de l'axe du verrou avant de reculer).
            if (s.boltParts != null)
            {
                var pivot = new GameObject("Verrou").transform;
                pivot.SetParent(m.transform, false);
                pivot.localPosition = s.boltPivot;
                foreach (var n in s.boltParts) { var p = FindDeep(m.transform, n); if (p != null) p.SetParent(pivot, true); }
            }
            // Pistolet : chargeur glissé dans la poignée.
            GameObject magVisual = null;
            if (s.magazine != null)
            {
                var mag = FindDeep(m.transform, s.magazine);
                if (mag != null) { mag.localPosition = s.magazinePos; magVisual = mag.gameObject; mag.name = "Chargeur"; }
            }

            var muzzle = new GameObject("Bouche").transform; muzzle.SetParent(kick, false);
            muzzle.localPosition = s.muzzle - s.grip;
            var eject = new GameObject("Éjection").transform; eject.SetParent(kick, false);
            eject.localPosition = s.eject - s.grip;

            SliceColliders(root, m.transform, s.slices);
            var mainColliders = new List<Collider>(root.GetComponents<Collider>());

            // Poignée principale invisible : cylindre elliptique dans l'axe de prise, centré sur l'origine.
            var grip = new GameObject("Poignée (prise)").transform; grip.SetParent(kick, false);
            grip.localRotation = Quaternion.FromToRotation(Vector3.up, s.gripAxis.normalized);
            grip.localScale = new Vector3(s.gripHalf.x * 2f, s.gripHalf.y, s.gripHalf.z * 2f);
            grip.gameObject.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");

            // Zone de chargement.
            var load = new GameObject("Chargement").AddComponent<BoxCollider>();
            load.transform.SetParent(kick, false);
            load.transform.localPosition = s.loadCenter - s.grip;
            load.size = s.loadSize; load.isTrigger = true;
            load.gameObject.AddComponent<LoadZone>();

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = s.mass; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var surface = root.AddComponent<GripSurface>();
            var gso = new SerializedObject(surface);
            gso.FindProperty("m_LocalAxis").vector3Value = s.gripAxis.normalized;
            gso.FindProperty("m_Source").objectReferenceValue = grip;
            gso.FindProperty("m_EndMargin").floatValue = 0.01f;
            gso.FindProperty("m_FixedAim").boolValue = true;
            gso.FindProperty("m_AimLocal").vector3Value = Vector3.forward;
            gso.FindProperty("m_UpLocal").vector3Value = Vector3.up;
            gso.ApplyModifiedPropertiesWithoutUndo();

            var grab = root.AddComponent<HandGripGrabInteractable>();
            grab.selectMode = InteractableSelectMode.Single;
            grab.useDynamicAttach = true;
            grab.matchAttachPosition = true; grab.matchAttachRotation = true;
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;   // visée précise, sans traîne
            grab.throwOnDetach = true;
            grab.predictedVisualsTransform = visuals;
            var grso = new SerializedObject(grab);
            SetColliders(grso, mainColliders);                                     // pas les poignées secondaires
            grso.FindProperty("m_NoHandSwap").boolValue = true;   // l'autre main prend la pompe / le garde-main, pas l'arme
            grso.ApplyModifiedPropertiesWithoutUndo();
            // Placement dans la main, puis visée à deux mains.
            var general = root.AddComponent<XRGeneralGrabTransformer>();
            var aim = root.AddComponent<TwoHandAim>();
            grab.startingSingleGrabTransformers = new List<XRBaseGrabTransformer> { general, aim };

            var breaker = root.AddComponent<Breaker>();
            var bso = new SerializedObject(breaker);
            bso.FindProperty("m_Power").floatValue = s.breakerPower;   // un coup de crosse casse une bouteille
            bso.ApplyModifiedPropertiesWithoutUndo();

            var gun = root.AddComponent<Firearm>();

            // Poignées secondaires (après le Firearm, qu'elles référencent).
            var handles = new List<GunHandle>();
            foreach (var h in s.handles)
            {
                var parent = h.part != null ? FindDeep(m.transform, h.part) : m.transform;
                if (parent == null) { Debug.LogWarning($"[Fragile] {s.name} : pièce « {h.part} » introuvable."); continue; }
                handles.Add(MakeHandle(h, parent, gun));
            }

            var so = new SerializedObject(gun);
            so.FindProperty("m_Kind").enumValueIndex = (int)s.kind;
            so.FindProperty("m_Muzzle").objectReferenceValue = muzzle;
            so.FindProperty("m_Projectiles").intValue = s.projectiles;
            so.FindProperty("m_Spread").floatValue = s.spread;
            so.FindProperty("m_Damage").floatValue = s.damage;
            so.FindProperty("m_Energy").floatValue = s.energy;
            so.FindProperty("m_Push").floatValue = s.push;
            so.FindProperty("m_Cooldown").floatValue = s.cooldown;
            so.FindProperty("m_KickBack").floatValue = s.kickBack;
            so.FindProperty("m_KickUp").floatValue = s.kickUp;
            so.FindProperty("m_Haptic").floatValue = s.haptic;
            so.FindProperty("m_KickRoot").objectReferenceValue = kick;
            so.FindProperty("m_Capacity").intValue = s.capacity;
            so.FindProperty("m_StartLoaded").intValue = s.startLoaded;
            so.FindProperty("m_AmmoPrefab").objectReferenceValue = ammo;
            so.FindProperty("m_MagazineVisual").objectReferenceValue = magVisual;
            var action = FindDeep(m.transform, s.action);
            if (action == null) Debug.LogWarning($"[Fragile] {s.name} : pièce mobile « {s.action} » introuvable.");
            so.FindProperty("m_Action").objectReferenceValue = action;
            so.FindProperty("m_ActionTravel").vector3Value = s.actionTravel;
            so.FindProperty("m_BoltAxis").vector3Value = Vector3.right;
            so.FindProperty("m_BoltLift").floatValue = s.boltLift;
            var hp = so.FindProperty("m_Handles"); hp.arraySize = handles.Count;
            for (int i = 0; i < handles.Count; i++) hp.GetArrayElementAtIndex(i).objectReferenceValue = handles[i];
            so.FindProperty("m_EjectPort").objectReferenceValue = eject;
            so.FindProperty("m_ShellMesh").objectReferenceValue = casing.mesh;
            var shellMats = so.FindProperty("m_ShellMaterials"); shellMats.arraySize = casing.mats.Length;
            for (int i = 0; i < casing.mats.Length; i++) shellMats.GetArrayElementAtIndex(i).objectReferenceValue = casing.mats[i];
            so.FindProperty("m_ShellSize").vector2Value = s.shell;
            so.FindProperty("m_FlashMaterial").objectReferenceValue = fx.flash;
            so.FindProperty("m_FlashSize").floatValue = s.flash;
            so.FindProperty("m_TracerMaterial").objectReferenceValue = fx.tracer;
            so.FindProperty("m_HoleMaterial").objectReferenceValue = fx.hole;
            so.FindProperty("m_DustFx").objectReferenceValue = fx.dust;
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/{FileName(s.name)}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>Poignée secondaire : capsule de collision, cylindre de prise invisible, interactable réservé à l'autre main.</summary>
        static GunHandle MakeHandle(HandleSpec h, Transform parent, Firearm gun)
        {
            var go = new GameObject(h.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = h.center;
            var axis = h.axis.normalized;
            var proxy = new GameObject("Prise").transform; proxy.SetParent(go.transform, false);
            proxy.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
            proxy.localScale = new Vector3(h.radius * 2f, h.halfLength, h.radius * 2f);
            proxy.gameObject.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            var col = go.AddComponent<CapsuleCollider>();
            // Un peu plus grosse que la pièce : la main la trouve avant la carcasse.
            col.radius = h.radius + 0.012f; col.height = h.halfLength * 2f + 0.02f;
            var a = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            col.direction = a.x >= a.y && a.x >= a.z ? 0 : a.y >= a.z ? 1 : 2;
            col.isTrigger = false;
            var surface = go.AddComponent<GripSurface>();
            var gso = new SerializedObject(surface);
            gso.FindProperty("m_LocalAxis").vector3Value = axis;
            gso.FindProperty("m_Source").objectReferenceValue = proxy;
            gso.FindProperty("m_EndMargin").floatValue = 0.008f;
            gso.ApplyModifiedPropertiesWithoutUndo();
            var handle = go.AddComponent<GunHandle>();
            var so = new SerializedObject(handle);
            so.FindProperty("m_Role").enumValueIndex = (int)h.role;
            so.FindProperty("m_Gun").objectReferenceValue = gun;
            so.ApplyModifiedPropertiesWithoutUndo();
            var hso = new SerializedObject(handle);
            SetColliders(hso, new List<Collider> { col });
            hso.ApplyModifiedPropertiesWithoutUndo();
            return handle;
        }

        static void SetColliders(SerializedObject so, List<Collider> cols)
        {
            var p = so.FindProperty("m_Colliders"); p.arraySize = cols.Count;
            for (int i = 0; i < cols.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = cols[i];
        }

        /// <summary>La prise (GripSurface) mesure les sommets du maillage : il doit être lisible.</summary>
        static void Readable(string fbx)
        {
            if (AssetImporter.GetAtPath(fbx) is ModelImporter imp && !imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); }
        }

        static string FileName(string n) => n.Replace(" à ", "_").Replace(' ', '_');

        static Transform FindDeep(Transform t, string name)
        {
            if (name == null) return null;
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }

        /// <summary>Boîtes de collision en tranches le long du canon : épousent la silhouette (crosse, poignée, canon).</summary>
        static void SliceColliders(GameObject root, Transform model, int n)
        {
            var pts = new List<Vector3>();
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
                foreach (var v in mf.sharedMesh.vertices) pts.Add(root.transform.InverseTransformPoint(mf.transform.TransformPoint(v)));
            float z0 = float.MaxValue, z1 = float.MinValue;
            foreach (var p in pts) { z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z); }
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Lerp(z0, z1, i / (float)n), b = Mathf.Lerp(z0, z1, (i + 1) / (float)n);
                bool any = false; var bb = new Bounds();
                foreach (var p in pts)
                    if (p.z >= a && p.z <= b) { if (!any) { bb = new Bounds(p, Vector3.zero); any = true; } else bb.Encapsulate(p); }
                if (!any) continue;
                var c = root.AddComponent<BoxCollider>();
                c.center = new Vector3(bb.center.x, bb.center.y, (a + b) / 2f);
                c.size = new Vector3(Mathf.Max(bb.size.x, 0.01f), Mathf.Max(bb.size.y, 0.01f), b - a);
            }
        }

        // ------------------------------------------------------------------ boîtes de munitions

        /// <summary>Hauteur de la surface sous un point (comptoir, caisse...).</summary>
        static Vector3 OnSurface(Vector3 p)
        {
            Physics.SyncTransforms();
            float top = 0f;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, 2.5f, p.z), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.normal.y < 0.7f || h.collider.GetComponentInParent<Firearm>() != null || h.collider.GetComponentInParent<AmmoBox>() != null) continue;
                top = Mathf.Max(top, h.point.y);
            }
            return new Vector3(p.x, top, p.z);
        }

        /// <summary>Petite tablette murale (planche + deux équerres) ; renvoie le point du dessus, au milieu.</summary>
        static Vector3 WallShelf(Transform root, string name, Vector3 wallPoint, Vector3 inward, float length, float depth)
        {
            var shelf = new GameObject(name).transform; shelf.SetParent(root, false);
            var wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/Bar/Lighting/Corniche_Bois.mat");
            var brass = AssetDatabase.LoadAssetAtPath<Material>("Assets/Bar/Lighting/Laiton.mat");
            var rot = Quaternion.LookRotation(inward);
            var along = Vector3.Cross(Vector3.up, inward).normalized;
            var plank = Prim(shelf, "Planche", PrimitiveType.Cube, wallPoint + inward * (depth / 2f), rot, new Vector3(length, 0.022f, depth), wood);
            foreach (var s in new[] { -1f, 1f })
                Prim(shelf, "Équerre", PrimitiveType.Cube, wallPoint + along * (s * length * 0.35f) + inward * (depth * 0.35f) - Vector3.up * 0.04f,
                    rot * Quaternion.Euler(45f, 0f, 0f), new Vector3(0.012f, 0.012f, depth * 0.75f), brass);
            return wallPoint + inward * (depth / 2f) + Vector3.up * 0.011f;
        }

        /// <summary>Boîte de munitions inépuisable (carton + étiquette + quelques munitions visibles sur le dessus).</summary>
        static void Box(Transform root, string name, AmmoItem ammo, Vector3 bottom, Quaternion rot, Vector3 size, Color color, string label, int shown)
        {
            var box = new GameObject(name);
            box.transform.SetParent(root, false);
            box.transform.SetPositionAndRotation(bottom, rot);
            var mat = BoxMaterial(name, color);
            var body = Prim(box.transform, "Carton", PrimitiveType.Cube, Vector3.zero, Quaternion.identity, Vector3.one, mat);
            body.transform.localPosition = Vector3.up * size.y / 2f; body.transform.localScale = size;
            Object.DestroyImmediate(body.GetComponent<Collider>());
            GameObjectUtility.SetStaticEditorFlags(body, 0);   // la boîte peut tomber (étagère ou caisse cassée)
            var col = box.AddComponent<BoxCollider>(); col.center = Vector3.up * size.y / 2f; col.size = size;
            if (!string.IsNullOrEmpty(label))
            {
                var text = new GameObject("Étiquette"); text.transform.SetParent(box.transform, false);
                text.transform.localPosition = new Vector3(0f, size.y / 2f, -size.z / 2f - 0.001f);
                var tmp = text.AddComponent<TextMeshPro>();
                tmp.text = label; tmp.rectTransform.sizeDelta = new Vector2(size.x * 0.9f, size.y * 0.7f);
                tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.01f; tmp.fontSizeMax = 1f;
                tmp.alignment = TextAlignmentOptions.Center; tmp.fontStyle = FontStyles.Bold; tmp.color = new Color(0.95f, 0.9f, 0.75f);
            }
            // Munitions posées sur le dessus (décor : la boîte en donne autant qu'on veut).
            var mf = ammo.GetComponentInChildren<MeshFilter>(); var mr = mf.GetComponent<MeshRenderer>();
            for (int i = 0; i < shown; i++)
            {
                var v = new GameObject("Munition");
                v.transform.SetParent(box.transform, false);
                v.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = v.AddComponent<MeshRenderer>(); r.sharedMaterials = mr.sharedMaterials; r.receiveGI = ReceiveGI.LightProbes;
                float u = shown > 1 ? i / (float)(shown - 1) - 0.5f : 0f;
                var b = mf.sharedMesh.bounds;
                // Couchée sur le carton : grand axe en travers, posée sur sa face la plus large.
                var longAxis = LongAxis(b.size);
                var thin = ThinAxis(b.size);
                var rotL = Quaternion.LookRotation(Vector3.up, Vector3.right);   // repère : avant = haut, haut = travers
                var lie = Quaternion.Inverse(Quaternion.LookRotation(thin, longAxis)) ;
                v.transform.localRotation = rotL * lie;
                // Rangées en travers (petites munitions) ou côte à côte (chargeurs).
                var offset = b.size[Index(longAxis)] > size.x * 0.4f ? new Vector3(0f, 0f, u * size.z * 0.7f) : new Vector3(u * size.x * 0.75f, 0f, 0f);
                float minY = float.MaxValue;
                for (int k = 0; k < 8; k++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                    minY = Mathf.Min(minY, (v.transform.localRotation * c).y);
                }
                var centered = -(v.transform.localRotation * b.center);
                v.transform.localPosition = new Vector3(centered.x, size.y - minY, centered.z) + offset;
            }
            var rb = box.AddComponent<Rigidbody>(); rb.mass = 0.5f;
            var ab = box.AddComponent<AmmoBox>();
            var so = new SerializedObject(ab);
            so.FindProperty("m_Prefab").objectReferenceValue = ammo;
            so.ApplyModifiedPropertiesWithoutUndo();
            var aso = new SerializedObject(ab);
            SetColliders(aso, new List<Collider> { col });
            aso.ApplyModifiedPropertiesWithoutUndo();
        }

        static Vector3 LongAxis(Vector3 s) => s.x >= s.y && s.x >= s.z ? Vector3.right : s.y >= s.z ? Vector3.up : Vector3.forward;
        static Vector3 ThinAxis(Vector3 s) => s.x <= s.y && s.x <= s.z ? Vector3.right : s.y <= s.z ? Vector3.up : Vector3.forward;
        static int Index(Vector3 axis) => axis.x != 0f ? 0 : axis.y != 0f ? 1 : 2;

        static Material BoxMaterial(string name, Color c)
        {
            string path = $"{FxDir}/Boite_{c.r:0.00}_{c.g:0.00}_{c.b:0.00}.mat".Replace(',', '.');
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ pose dans le décor

        /// <summary>
        /// Râtelier mural : planchette de noyer et deux chevilles en laiton (bout relevé). L'arme y est posée de profil,
        /// immobile (cinématique) tant qu'on ne l'a pas prise (<see cref="Firearm"/> la libère au premier lâcher).
        /// </summary>
        static void Rack(Transform root, GameObject prefab, string name, Vector3 wallPoint, Vector3 inward, Vector3 barrelDir, float[] pegsAlong)
        {
            const float board = 0.02f;
            var gun = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            gun.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(barrelDir, Vector3.up));
            Physics.SyncTransforms();
            var b = WorldBounds(gun);
            var absIn = new Vector3(Mathf.Abs(inward.x), 0f, Mathf.Abs(inward.z));
            float half = Vector3.Dot(b.extents, absIn);
            var muzzle = gun.transform.TransformPoint(MuzzleLocal(gun));
            // Centrée sur le point demandé le long du mur, de profil contre la planchette, canon à la hauteur demandée.
            var move = Vector3.Dot(wallPoint - b.center, barrelDir) * barrelDir
                     + (Vector3.Dot(wallPoint, inward) + board + half + 0.004f - Vector3.Dot(b.center, inward)) * inward
                     + Vector3.up * (wallPoint.y - muzzle.y);
            gun.transform.position += move;
            Physics.SyncTransforms();
            b = WorldBounds(gun);
            SetRacked(gun);

            var rack = new GameObject(name).transform;
            rack.SetParent(root, false);
            var wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/Bar/Lighting/Corniche_Bois.mat");
            var brass = AssetDatabase.LoadAssetAtPath<Material>("Assets/Bar/Lighting/Laiton.mat");
            var wallBase = b.center - (Vector3.Dot(b.center, inward) - Vector3.Dot(wallPoint, inward)) * inward;   // centre ramené sur le mur
            wallBase.y = 0f;
            float lowest = float.MaxValue;
            var pegs = new List<(Vector3 onWall, float y)>();
            foreach (var t in pegsAlong)
            {
                var onWall = wallBase + barrelDir * t;
                var under = onWall + inward * (board + half);
                // Dessous de l'arme à cet endroit : rayon vers le haut sur ses colliders.
                float y = b.min.y;
                foreach (var h in Physics.RaycastAll(new Vector3(under.x, b.min.y - 0.2f, under.z), Vector3.up, 1f, ~0, QueryTriggerInteraction.Ignore))
                    if (h.collider.transform.IsChildOf(gun.transform)) { y = h.point.y; break; }
                pegs.Add((onWall, y));
                lowest = Mathf.Min(lowest, y);
            }
            float span = Mathf.Abs(pegsAlong[1] - pegsAlong[0]);
            var mid = (pegs[0].onWall + pegs[1].onWall) / 2f;
            // Planchette derrière l'arme (de la cheville la plus basse au haut de l'arme).
            float top = b.max.y + 0.02f, bottom = lowest - 0.03f;
            Prim(rack, "Planchette", PrimitiveType.Cube, mid + Vector3.up * ((top + bottom) / 2f) + inward * (board / 2f),
                Quaternion.LookRotation(inward), new Vector3(span + 0.3f, top - bottom, board), wood);
            foreach (var (onWall, y) in pegs)
            {
                float depth = half * 2f + 0.025f;
                Prim(rack, "Cheville", PrimitiveType.Cylinder, onWall + Vector3.up * (y - 0.008f) + inward * (board + depth / 2f),
                    Quaternion.FromToRotation(Vector3.up, inward), new Vector3(0.016f, depth / 2f, 0.016f), brass);
                Prim(rack, "Bout", PrimitiveType.Sphere, onWall + Vector3.up * (y - 0.002f) + inward * (board + depth),
                    Quaternion.identity, Vector3.one * 0.022f, brass);
            }
        }

        static Vector3 MuzzleLocal(GameObject gun)
        {
            var so = new SerializedObject(gun.GetComponent<Firearm>());
            var m = so.FindProperty("m_Muzzle").objectReferenceValue as Transform;
            return m != null ? gun.transform.InverseTransformPoint(m.position) : Vector3.zero;
        }

        static void SetRacked(GameObject gun)
        {
            var so = new SerializedObject(gun.GetComponent<Firearm>());
            so.FindProperty("m_StartRacked").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            gun.GetComponent<Rigidbody>().isKinematic = true;
        }

        /// <summary>Arme posée à plat sur le comptoir (côté droit dessous), canon dans la direction donnée.</summary>
        static void OnCounter(Transform root, GameObject prefab, Vector3 spot, Vector3 barrelDir)
        {
            var gun = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            // Couché sur le flanc : la droite de l'arme (côté éjection) regarde le plafond.
            gun.transform.rotation = Quaternion.LookRotation(barrelDir, Vector3.Cross(barrelDir, Vector3.up));
            gun.transform.position = new Vector3(spot.x, 3f, spot.z);
            Physics.SyncTransforms();
            float top = 0f; string what = "sol";
            foreach (var h in Physics.RaycastAll(new Vector3(spot.x, 2.5f, spot.z), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(gun.transform) || h.normal.y < 0.7f) continue;
                if (h.point.y > top) { top = h.point.y; what = h.collider.name; }
            }
            var b = WorldBounds(gun);
            gun.transform.position += new Vector3(spot.x - b.center.x, top - b.min.y + 0.003f, spot.z - b.center.z);
            Debug.Log($"[Fragile] Pistolet posé sur « {what} » à {top:0.00} m.");
        }

        static GameObject Prim(Transform parent, string name, PrimitiveType type, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.GetComponent<MeshRenderer>().receiveGI = ReceiveGI.LightProbes;
            // Les chevilles portent vraiment l'arme quand elle retombe dessus.
            if (type == PrimitiveType.Sphere) Object.DestroyImmediate(go.GetComponent<Collider>());
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var cols = go.GetComponentsInChildren<Collider>();
            var b = cols[0].bounds;
            foreach (var c in cols) b.Encapsulate(c.bounds);
            return b;
        }

        // ------------------------------------------------------------------ effets (textures générées)

        static Fx BuildFx()
        {
            var fx = new Fx
            {
                hole = Mat("Impact_Balle", Texture("Impact_Balle", 128, HolePixel, true), "Universal Render Pipeline/Unlit", false),
                flash = Mat("Flamme", Texture("Flamme", 128, FlashPixel, false), "Universal Render Pipeline/Particles/Unlit", true),
                tracer = Mat("Tracante", Texture("Tracante", 64, TracerPixel, false), "Universal Render Pipeline/Particles/Unlit", true),
                brass = AssetDatabase.LoadAssetAtPath<Material>("Assets/Bar/Lighting/Laiton.mat"),
                dust = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Bar/FX/BreakFX.prefab"),
                shell = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx"),
            };
            string redPath = $"{FxDir}/Cartouche_Rouge.mat";
            fx.red = AssetDatabase.LoadAssetAtPath<Material>(redPath);
            if (fx.red == null) { fx.red = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(fx.red, redPath); }
            fx.red.SetColor("_BaseColor", new Color(0.62f, 0.06f, 0.05f)); fx.red.SetFloat("_Smoothness", 0.55f);
            EditorUtility.SetDirty(fx.red);
            return fx;
        }

        delegate Color Pixel(float u, float v, System.Random rng);

        static Texture2D Texture(string name, int size, Pixel pixel, bool mip)
        {
            string path = $"{FxDir}/{name}.png";
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var rng = new System.Random(name.GetHashCode());
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, pixel((x + 0.5f) / size, (y + 0.5f) / size, rng));
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.alphaIsTransparency = true; ti.mipmapEnabled = mip; ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Smoothstep des shaders (Mathf.SmoothStep, lui, interpole entre ses deux premiers arguments).
        static float Step(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

        static Color HolePixel(float u, float v, System.Random rng)
        {
            // Trou noir, collerette éclatée (plâtre/bois arraché), fines fissures rayonnantes.
            float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2f, a = Mathf.Atan2(dy, dx);
            float jag = 0.08f * Mathf.Sin(a * 7f) + 0.05f * Mathf.Sin(a * 13f + 1f);
            float hole = 1f - Step(0.16f + jag * 0.5f, 0.24f + jag * 0.5f, r);
            float ring = (1f - Step(0.35f + jag, 0.75f + jag, r)) * (0.55f + 0.3f * (float)rng.NextDouble());
            float crack = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 5f + Mathf.Sin(a * 3f))), 60f) * (1f - Step(0.4f, 0.95f, r));
            float alpha = Mathf.Clamp01(Mathf.Max(hole, Mathf.Max(ring * 0.75f, crack * 0.8f)));
            var c = Color.Lerp(new Color(0.32f, 0.29f, 0.26f), new Color(0.02f, 0.02f, 0.02f), Mathf.Max(hole, crack * 0.6f));
            c.a = alpha;
            return c;
        }

        static Color FlashPixel(float u, float v, System.Random rng)
        {
            // Étoile à branches irrégulières, cœur blanc, bords orangés (rendu additif).
            float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2f, a = Mathf.Atan2(dy, dx);
            float spikes = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 3f)), 8f) * 0.6f + Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 2f + 0.7f)), 14f) * 0.4f;
            float shape = Mathf.Clamp01(1f - r / (0.35f + 0.65f * spikes));
            float core = Mathf.Clamp01(1f - r / 0.25f);
            var c = Color.Lerp(new Color(1f, 0.45f, 0.1f), new Color(1f, 0.95f, 0.75f), core) * (shape * shape + core);
            c.a = 1f;
            return c;
        }

        static Color TracerPixel(float u, float v, System.Random rng)
        {
            // Trait (U le long de la traçante : queue transparente -> tête vive ; V : bord adouci).
            float across = 1f - Mathf.Abs(v - 0.5f) * 2f;
            float k = u * u * across * across;
            var c = new Color(1f, 0.85f, 0.55f) * k;
            c.a = 1f;
            return c;
        }

        static Material Mat(string name, Texture2D tex, string shader, bool additive)
        {
            string path = $"{FxDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find(shader);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
