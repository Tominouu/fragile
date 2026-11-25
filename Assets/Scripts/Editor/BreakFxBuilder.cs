using Fragile.Breakables;
using UnityEditor;
using UnityEngine;

namespace Fragile.EditorTools
{
    /// <summary>
    /// Construit le préfab d'effet de casse (Assets/Bar/FX/BreakFX.prefab), réutilisé par tous les objets cassables.
    /// Les valeurs de base sont "unitaires" : BreakFx les multiplie à l'impact selon l'énergie du coup.
    /// Réglé pour Quest : peu de particules, collisions en basse qualité, éclats opaques (pas de surcoût de transparence).
    /// </summary>
    public static class BreakFxBuilder
    {
        public static GameObject GetOrBuild(Material softParticle, string folder)
        {
            var chipMesh = GetChipMesh(folder);
            var chipMat = GetChipMaterial(folder);

            var root = new GameObject("BreakFX");
            var fx = root.AddComponent<BreakFx>();

            var chips = MakeSystem(root.transform, "Éclats");
            SetupChips(chips, chipMesh, chipMat);
            var dust = MakeSystem(root.transform, "Poussière");
            SetupDust(dust, softParticle);
            var liquid = MakeSystem(root.transform, "Liquide");
            var splash = MakeSystem(liquid.transform, "Éclaboussures");
            SetupLiquid(liquid, splash, softParticle);

            var so = new SerializedObject(fx);
            so.FindProperty("m_Chips").objectReferenceValue = chips;
            so.FindProperty("m_Dust").objectReferenceValue = dust;
            so.FindProperty("m_Liquid").objectReferenceValue = liquid;
            so.ApplyModifiedPropertiesWithoutUndo();

            string path = $"{folder}/BreakFX.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static ParticleSystem MakeSystem(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.duration = 0.1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission; em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)10) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 50f; shape.radius = 0.03f;
            return ps;
        }

        static Gradient FadeOut(float alpha = 1f, float holdUntil = 0.6f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(alpha, holdUntil), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        // Éclats de céramique : vrais petits morceaux 3D éclairés, qui tournoient et rebondissent.
        static void SetupChips(ParticleSystem ps, Mesh chip, Material mat)
        {
            var main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1f);        // x multiplicateur d'énergie
            main.startSize = new ParticleSystem.MinMaxCurve(0.007f, 0.022f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 1.3f;
            main.maxParticles = 70;

            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-12f, 12f); rot.y = new ParticleSystem.MinMaxCurve(-12f, 12f); rot.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
            // Une fois au sol ils ralentissent : rotation amortie par la collision (dampen) + friction de l'air.
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.drag = 0.4f; lim.multiplyDragByParticleSize = false;

            // Disparition : ils rétrécissent à la fin (pas de transparence = moins coûteux).
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)));

            var col = ps.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World;
            col.mode = ParticleSystemCollisionMode.Collision3D; col.quality = ParticleSystemCollisionQuality.Low;
            col.bounce = new ParticleSystem.MinMaxCurve(0.2f, 0.45f); col.dampen = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            col.maxCollisionShapes = 64; col.radiusScale = 0.6f;

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = chip;
            r.alignment = ParticleSystemRenderSpace.World;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // Poussière : petit nuage clair qui s'étale, freine dans l'air et se dissipe. Pas de collision (inutile, coûteux).
        static void SetupDust(ParticleSystem ps, Material soft)
        {
            var main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.32f), new Color(0.92f, 0.92f, 0.96f, 0.22f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.02f;
            main.maxParticles = 20;
            var shape = ps.shape; shape.angle = 70f;
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.drag = 3f;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.8f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = FadeOut(1f, 0.2f);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = soft; r.sortMode = ParticleSystemSortMode.None;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // Liquide : gouttes étirées par la vitesse, qui meurent en touchant une surface et y font de petites éclaboussures.
        static void SetupLiquid(ParticleSystem ps, ParticleSystem splash, Material soft)
        {
            var main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.55f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.016f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
            main.gravityModifier = 1f;
            main.maxParticles = 110;
            var shape = ps.shape; shape.angle = 55f; shape.radius = 0.02f;
            var col = ps.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World;
            col.mode = ParticleSystemCollisionMode.Collision3D; col.quality = ParticleSystemCollisionQuality.Low;
            col.bounce = 0f; col.lifetimeLoss = 1f; col.maxCollisionShapes = 64; col.radiusScale = 0.5f;
            var cl = ps.colorOverLifetime; cl.enabled = true; cl.color = FadeOut(1f, 0.85f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = soft;
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.035f; r.lengthScale = 1.3f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Éclaboussures au point d'impact de chaque goutte.
            var sm = splash.main;
            sm.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.9f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.003f, 0.008f);
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
            sm.gravityModifier = 1f;
            sm.maxParticles = 220;
            var sem = splash.emission; sem.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)2, (short)4) });
            var ssh = splash.shape; ssh.shapeType = ParticleSystemShapeType.Hemisphere; ssh.radius = 0.004f;
            var scl = splash.colorOverLifetime; scl.enabled = true; scl.color = FadeOut(1f, 0.5f);
            var sr = splash.GetComponent<ParticleSystemRenderer>();
            sr.sharedMaterial = soft; sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var sub = ps.subEmitters; sub.enabled = true;
            sub.AddSubEmitter(splash, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritColor);
        }

        // ------------------------------------------------------------------ ressources

        /// <summary>Petit éclat irrégulier (prisme à base polygonale), taille ~1 : la taille réelle vient de la particule.</summary>
        static Mesh GetChipMesh(string folder)
        {
            string path = $"{folder}/Eclat_Particule.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            var rng = new System.Random(3);
            var outline = new Vector2[5];
            for (int i = 0; i < outline.Length; i++)
            {
                float a = i / (float)outline.Length * Mathf.PI * 2f + (float)rng.NextDouble() * 0.6f;
                float r = 0.3f + (float)rng.NextDouble() * 0.25f;
                outline[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 0.75f);
            }
            const float h = 0.12f;
            var v = new System.Collections.Generic.List<Vector3>();
            var t = new System.Collections.Generic.List<int>();
            void Face(Vector3 a, Vector3 b, Vector3 c) { int s = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(s); t.Add(s + 1); t.Add(s + 2); }
            for (int i = 1; i + 1 < outline.Length; i++)
            {
                Face(new Vector3(outline[0].x, outline[0].y, h), new Vector3(outline[i + 1].x, outline[i + 1].y, h), new Vector3(outline[i].x, outline[i].y, h));
                Face(new Vector3(outline[0].x, outline[0].y, -h), new Vector3(outline[i].x, outline[i].y, -h), new Vector3(outline[i + 1].x, outline[i + 1].y, -h));
            }
            for (int i = 0; i < outline.Length; i++)
            {
                var a = outline[i]; var b = outline[(i + 1) % outline.Length];
                Face(new Vector3(a.x, a.y, h), new Vector3(b.x, b.y, h), new Vector3(b.x, b.y, -h));
                Face(new Vector3(a.x, a.y, h), new Vector3(b.x, b.y, -h), new Vector3(a.x, a.y, -h));
            }
            var mesh = new Mesh { name = "Éclat particule" };
            mesh.SetVertices(v); mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Material GetChipMaterial(string folder)
        {
            string path = $"{folder}/Particules_Eclats.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Simple Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 0f);                 // opaque
            mat.SetFloat("_Smoothness", 0.65f);           // céramique émaillée
            mat.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
