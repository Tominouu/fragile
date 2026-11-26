using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fragile.Mice
{
    /// <summary>
    /// Sang : gerbe de gouttelettes à l'impact, giclée collée sur le mur / le sol derrière la cible, et flaque
    /// bien visible qui s'étale sous le corps (rouge vif, mouillée : très brillante). Flaques et giclées recyclées.
    /// Matériaux fournis par <see cref="MouseNest"/> (générés par Fragile > Ajouter les souris).
    /// </summary>
    public static class BloodFx
    {
        static Material s_Pool, s_Splat, s_Drop;
        static Mesh s_Quad;
        static ParticleSystem s_Spray;
        static readonly Queue<GameObject> s_Decals = new Queue<GameObject>();
        static int s_Layer;
        const int MaxDecals = 40;

        public static void Setup(Material pool, Material splat, Material drop)
        {
            s_Pool = pool; s_Splat = splat; s_Drop = drop;
        }

        /// <summary>Gerbe de gouttelettes. amount : 0 (petit coup) à 1 (coup de fusil à bout portant).</summary>
        public static void Spray(Vector3 point, Vector3 direction, float amount)
        {
            if (s_Drop == null) return;
            if (s_Spray == null) s_Spray = BuildSpray();
            amount = Mathf.Clamp01(amount);
            var t = s_Spray.transform;
            t.SetPositionAndRotation(point, Quaternion.LookRotation(direction.sqrMagnitude > 1e-6f ? direction : Vector3.up));
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            var main = s_Spray.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f + amount, 2.2f + amount * 3.5f);
            s_Spray.Emit(emit, Mathf.RoundToInt(Mathf.Lerp(25f, 140f, amount)));
        }

        /// <summary>Giclée sur la première surface derrière l'impact (mur, sol, comptoir).</summary>
        public static void Splat(Vector3 point, Vector3 direction, float size, Transform ignore)
        {
            if (s_Splat == null) return;
            foreach (var h in Sorted(Physics.RaycastAll(point, direction.normalized, 2.5f, ~0, QueryTriggerInteraction.Ignore)))
            {
                if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
                if (h.rigidbody != null && !h.rigidbody.isKinematic) continue;   // pas sur une bouteille qui roule
                Decal(s_Splat, h.point, h.normal, size, 0.15f, h.collider.transform);
                return;
            }
        }

        /// <summary>Flaque qui s'étale sous un point (sur le sol ou ce qu'il y a dessous).</summary>
        public static void Pool(Vector3 point, float diameter, Transform ignore)
        {
            if (s_Pool == null) return;
            foreach (var h in Sorted(Physics.RaycastAll(point + Vector3.up * 0.15f, Vector3.down, 1.5f, ~0, QueryTriggerInteraction.Ignore)))
            {
                if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
                if (h.rigidbody != null && !h.rigidbody.isKinematic) continue;
                if (h.normal.y < 0.6f) continue;
                Decal(s_Pool, h.point, h.normal, diameter, 2.8f, h.collider.transform);
                return;
            }
        }

        static IEnumerable<RaycastHit> Sorted(RaycastHit[] hits)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            return hits;
        }

        static void Decal(Material mat, Vector3 point, Vector3 normal, float size, float growTime, Transform surface)
        {
            if (s_Quad == null) s_Quad = QuadMesh();
            GameObject go;
            if (s_Decals.Count >= MaxDecals) { go = s_Decals.Dequeue(); if (go == null) go = NewDecal(); }
            else go = NewDecal();
            // Chaque nouvelle tache un peu plus haut que la précédente : pas de scintillement quand elles se chevauchent.
            s_Layer = (s_Layer + 1) % 12;
            go.transform.SetParent(null);
            go.transform.SetPositionAndRotation(point + normal * (0.002f + s_Layer * 0.0004f),
                Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
            go.transform.localScale = Vector3.one * 0.001f;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            go.SetActive(true);
            // Suit les surfaces qui bougent (portes, tiroirs, objets du décor), pas les murs (cubes étirés).
            var ls = surface.lossyScale;
            if (!surface.gameObject.isStatic && Mathf.Abs(ls.x - ls.y) < 0.01f && Mathf.Abs(ls.y - ls.z) < 0.01f) go.transform.SetParent(surface, true);
            var grow = go.GetComponent<BloodDecal>();
            grow.Play(size, growTime);
            s_Decals.Enqueue(go);
        }

        static GameObject NewDecal()
        {
            var go = new GameObject("Sang");
            go.AddComponent<MeshFilter>().sharedMesh = s_Quad;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            go.AddComponent<BloodDecal>();
            Object.DontDestroyOnLoad(go);
            return go;
        }

        static Mesh QuadMesh()
        {
            var m = new Mesh { name = "Tache" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.tangents = new[] { new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1), new Vector4(1, 0, 0, -1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }

        static ParticleSystem BuildSpray()
        {
            var go = new GameObject("Gerbe de sang");
            Object.DontDestroyOnLoad(go);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.02f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.01f, 0.01f), new Color(0.85f, 0.04f, 0.03f));
            main.gravityModifier = 1.3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 28f; shape.radius = 0.01f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.7f, 0.7f, 0.7f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = s_Drop;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.02f; r.lengthScale = 1.2f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }
    }
}
