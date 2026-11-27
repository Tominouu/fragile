using System.Collections.Generic;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fragile.Trailer
{
    /// <summary>
    /// Ce dont le Trailer a besoin dans la scène Bar, retrouvé À CHAQUE LANCEMENT (jamais de copie ni de référence figée) :
    /// le XR Origin, les écrans des télés, les tabourets, et la meilleure place assise face à un écran.
    /// Si Tom déplace le comptoir, les télés ou les tabourets, le Trailer suit tout seul.
    /// Seuls les NOMS comptent : « Télés » (ou le composant TvVideo), « Écran », « Tabouret ».
    /// </summary>
    public class TrailerAnchors
    {
        public XROrigin Origin;
        public Transform TvRoot;
        public readonly List<Renderer> Screens = new List<Renderer>();
        public readonly List<Transform> Stools = new List<Transform>();

        public Transform Stool;        // tabouret choisi
        public Renderer Screen;        // écran regardé depuis ce tabouret
        public Vector3 Eye;            // position des yeux, assis
        public Vector3 LookAt;         // centre de l'écran regardé

        public readonly List<string> Warnings = new List<string>();
        public bool Ok => Origin != null && Screens.Count > 0 && Stool != null;

        const string TvRootName = "Télés", ScreenName = "Écran", StoolName = "Tabouret";

        /// <summary>Hauteur des yeux au-dessus de l'assise d'un tabouret (adulte assis).</summary>
        public static float SeatedEye = 0.78f;
        /// <summary>Distance écran/yeux idéale (m) : l'écran de 50" remplit bien le regard sans être trop près.</summary>
        public static float IdealDistance = 2.6f;

        public static TrailerAnchors Find(Scene bar, Transform forcedViewPoint = null)
        {
            var a = new TrailerAnchors();
            if (!bar.IsValid() || !bar.isLoaded) { a.Warnings.Add("La scène Bar n'est pas chargée."); return a; }

            var all = new List<Transform>();
            foreach (var root in bar.GetRootGameObjects()) all.AddRange(root.GetComponentsInChildren<Transform>(true));

            // XR Origin du joueur.
            foreach (var root in bar.GetRootGameObjects())
            {
                a.Origin = root.GetComponentInChildren<XROrigin>(true);
                if (a.Origin != null) break;
            }
            if (a.Origin == null) a.Warnings.Add("Aucun XR Origin dans le Bar (Fragile > Ajouter le XR Origin au Bar).");

            // Télés : le composant TvVideo de préférence, sinon l'objet « Télés ».
            foreach (var t in all)
                if (t.GetComponent<Fragile.TV.TvVideo>() != null) { a.TvRoot = t; break; }
            if (a.TvRoot == null)
                foreach (var t in all)
                    if (t.name == TvRootName) { a.TvRoot = t; break; }
            if (a.TvRoot == null) a.Warnings.Add("Télés introuvables (objet « Télés » ou composant TvVideo).");
            else
                foreach (var r in a.TvRoot.GetComponentsInChildren<Renderer>(true))
                    if (r.name == ScreenName) a.Screens.Add(r);
            if (a.TvRoot != null && a.Screens.Count == 0) a.Warnings.Add("Aucun renderer « Écran » sous les télés.");

            // Tabourets : le plus haut objet de chaque branche dont le nom commence par « Tabouret ».
            foreach (var t in all)
            {
                if (!t.name.StartsWith(StoolName) || (Application.isPlaying && !t.gameObject.activeInHierarchy)) continue;
                bool nested = false;
                for (var p = t.parent; p != null; p = p.parent) if (p.name.StartsWith(StoolName)) { nested = true; break; }
                if (!nested) a.Stools.Add(t);
            }
            if (a.Stools.Count == 0) a.Warnings.Add("Aucun « Tabouret » dans le Bar.");

            if (forcedViewPoint != null)
            {
                a.Eye = forcedViewPoint.position;
                a.Screen = ClosestFacingScreen(a, a.Eye);
                a.LookAt = a.Screen != null ? a.Screen.bounds.center : a.Eye + forcedViewPoint.forward;
                a.Stool = forcedViewPoint;
                return a;
            }

            Pick(a);
            return a;
        }

        /// <summary>Note chaque couple (tabouret, écran) : l'écran doit faire face, être visible et à bonne distance.</summary>
        static void Pick(TrailerAnchors a)
        {
            if (a.Screens.Count == 0 || a.Stools.Count == 0) return;
            Physics.SyncTransforms();
            float best = float.NegativeInfinity;
            foreach (var stool in a.Stools)
            {
                if (!TryBounds(stool, out var b)) continue;
                var eye = new Vector3(b.center.x, b.max.y + SeatedEye, b.center.z);
                foreach (var screen in a.Screens)
                {
                    var c = screen.bounds.center;
                    var n = ScreenNormal(a, screen);
                    var to = eye - c;
                    float dist = to.magnitude;
                    float facing = Vector3.Dot(n, to / Mathf.Max(dist, 1e-3f));
                    if (facing < 0.35f || dist < 1.2f || dist > 6f) continue;
                    if (!Visible(a, eye, c)) continue;
                    float score = facing * 1.5f - Mathf.Abs(dist - IdealDistance) * 0.35f;
                    if (score > best) { best = score; a.Stool = stool; a.Screen = screen; a.Eye = eye; a.LookAt = c; }
                }
            }
            if (a.Stool == null) a.Warnings.Add("Aucun tabouret ne voit un écran de face (tous cachés ou mal orientés).");
        }

        static Renderer ClosestFacingScreen(TrailerAnchors a, Vector3 eye)
        {
            Renderer res = null; float best = float.NegativeInfinity;
            foreach (var s in a.Screens)
            {
                var to = (eye - s.bounds.center).normalized;
                float f = Vector3.Dot(ScreenNormal(a, s), to);
                if (f > best) { best = f; res = s; }
            }
            return res;
        }

        /// <summary>Normale du côté visible : le quad regarde vers l'extérieur du bloc de télés.</summary>
        static Vector3 ScreenNormal(TrailerAnchors a, Renderer screen)
        {
            var n = -screen.transform.forward;
            if (a.TvRoot != null)
            {
                var outward = screen.bounds.center - a.TvRoot.position; outward.y = 0f;
                if (outward.sqrMagnitude > 1e-4f && Vector3.Dot(n, outward) < 0f) n = -n;
            }
            return n;
        }

        static bool Visible(TrailerAnchors a, Vector3 eye, Vector3 target)
        {
            var d = target - eye;
            foreach (var h in Physics.RaycastAll(eye, d.normalized, d.magnitude - 0.05f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (a.TvRoot != null && h.transform.IsChildOf(a.TvRoot)) continue;
                if (a.Origin != null && h.transform.IsChildOf(a.Origin.transform)) continue;
                return false;   // comptoir, poutre, autre meuble entre les yeux et l'écran
            }
            return true;
        }

        static bool TryBounds(Transform t, out Bounds b)
        {
            b = default; bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        public string Report()
        {
            var s = new StringBuilder();
            s.AppendLine(Ok ? "[Fragile] Trailer synchronisé avec le Bar." : "[Fragile] Trailer : il manque des éléments dans le Bar.");
            s.AppendLine($"  XR Origin : {(Origin != null ? Origin.name : "—")}");
            s.AppendLine($"  Écrans de télé : {Screens.Count}   Tabourets : {Stools.Count}");
            if (Stool != null)
                s.AppendLine($"  Place choisie : {Path(Stool)}  yeux ({Eye.x:F2}, {Eye.y:F2}, {Eye.z:F2}), écran à {Vector3.Distance(Eye, LookAt):F2} m");
            foreach (var w in Warnings) s.AppendLine("  ⚠ " + w);
            return s.ToString();
        }

        static string Path(Transform t)
        {
            var p = t.name;
            for (var q = t.parent; q != null; q = q.parent) p = q.name + "/" + p;
            return p;
        }
    }
}
