#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Finds what makes Unity log "Invalid worldAABB. Object is too large or too far away from the origin" (41,298 times in
    /// the editor log of one play session, 2026-10-29, from the moment the desert opened: something saved in the scene,
    /// not something that goes wrong in play). Unity names no object, so every renderer, mesh, light, reflection probe,
    /// particle system, LOD group and collider is checked for bounds that are not finite numbers or are absurdly large.
    ///   Tools/unity-batch.sh FPSKitBatch.FindInvalidBounds [-fpskitTheme "Desert Outpost"]
    /// </summary>
    public static class FPSKitBoundsCheck
    {
        const float Huge = 100000f;     // 100 km: nothing in an arena is near this

        static bool Bad(Bounds b) => !Finite(b.center) || !Finite(b.extents) || b.extents.magnitude > Huge || b.center.magnitude > Huge;
        static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        public static List<string> Check()
        {
            var found = new List<string>();
            void Add(Object o, string what) => found.Add($"{PathOf(o)}: {what}");
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                if (Bad(r.bounds)) Add(r, $"{r.GetType().Name} world bounds {r.bounds.center} +/- {r.bounds.extents} (enabled {r.enabled}, active {r.gameObject.activeInHierarchy})");
                if (r is SkinnedMeshRenderer s && Bad(s.localBounds)) Add(r, $"skinned local bounds {s.localBounds.center} +/- {s.localBounds.extents}");
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && Bad(mf.sharedMesh.bounds)) Add(r, $"mesh '{mf.sharedMesh.name}' bounds {mf.sharedMesh.bounds.center} +/- {mf.sharedMesh.bounds.extents}");
                if (!Finite(r.transform.position) || !Finite(r.transform.lossyScale) || r.transform.lossyScale.magnitude > Huge)
                    Add(r, $"transform at {r.transform.position}, scale {r.transform.lossyScale}");
            }
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
                if (!float.IsFinite(l.range) || (l.type != LightType.Directional && l.range > Huge) || !Finite(l.transform.position)) Add(l, $"light {l.type} range {l.range} at {l.transform.position}");
            foreach (var p in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include))
                if (Bad(p.bounds) || !Finite(p.size) || p.size.magnitude > Huge) Add(p, $"reflection probe size {p.size} bounds {p.bounds.center} +/- {p.bounds.extents}");
            foreach (var g in Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Include))
                if (!float.IsFinite(g.size) || g.size > Huge || !Finite(g.localReferencePoint)) Add(g, $"LOD group size {g.size}, reference point {g.localReferencePoint}");
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include))
                if (c.enabled && c.gameObject.activeInHierarchy && Bad(c.bounds)) Add(c, $"{c.GetType().Name} bounds {c.bounds.center} +/- {c.bounds.extents}");
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include))
            {
                var main = ps.main;
                if (main.startSize.constantMax > Huge || main.startSpeed.constantMax > Huge) Add(ps, $"particle start size {main.startSize.constantMax}, speed {main.startSpeed.constantMax}");
            }
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
                if (t.terrainData != null && (!Finite(t.terrainData.size) || t.terrainData.size.magnitude > Huge)) Add(t, $"terrain size {t.terrainData.size}");
            return found;
        }

        public static void Batch()
        {
            string theme = null;
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-fpskitTheme");
            if (i >= 0 && i + 1 < args.Length) theme = args[i + 1];
            string path = $"Assets/FPSKit_Generated/Scenes/{(theme ?? "Desert Outpost").Replace(" ", "")}.unity";
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var found = Check();
            var sb = new StringBuilder($"[FPSKitBatch] bounds check of {path}: {found.Count} found");
            foreach (var f in found.Take(60)) sb.Append("\n  ").Append(f);
            Debug.Log(sb.ToString());
            FPSKitBatch.Exit(0);
        }

        static string PathOf(Object o)
        {
            var t = o is Component c ? c.transform : o is GameObject g ? g.transform : null;
            if (t == null) return o.name;
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
    }
}
#endif
