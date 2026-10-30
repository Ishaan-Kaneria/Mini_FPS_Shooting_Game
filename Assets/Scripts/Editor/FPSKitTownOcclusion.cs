#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Occlusion culling for the desert town: what a street's buildings hide is not drawn. Measured 2026-10-30
    /// (FrameCostProbe, Medium): everything past 150 m cost 8.1 ms of a 26 ms frame, the building blocks 6.4, the landmarks
    /// and the edge 5.4 -- most of it out of sight behind the street's own walls. The occluders are the town's own shells
    /// (the building blocks, the landmarks', the edge's structures, the terrain); props, people and cars only hide nothing
    /// and are hidden (a crate is a poor occluder and made the bake slow). Run on the saved scene after it is built
    /// (FPSKitSceneBuilder.BakeTownProbes) or alone: FPSKitBatch.BakeDesertOcclusion.
    /// </summary>
    public static class FPSKitTownOcclusion
    {
        /// <summary>Metres: the smallest thing that hides anything (a house is 6+, a wall 3+).</summary>
        const float SmallestOccluder = 3f;
        /// <summary>Metres: the smallest gap seen through (a doorway's width is 1.8, a window 0.8).</summary>
        const float SmallestHole = 0.5f;

        public static bool Bake()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var arena = GameObject.Find("Arena");
            if (arena == null) { Debug.LogWarning("[FPSKit] occlusion: no Arena"); return false; }
            int occluders = 0, occludees = 0;
            foreach (var r in arena.GetComponentsInChildren<Renderer>(true))
            {
                var go = r.gameObject;
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                if ((flags & (StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic)) == 0) continue;
                bool shell = IsShell(r);
                var want = shell ? flags | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic
                                 : (flags & ~StaticEditorFlags.OccluderStatic) | StaticEditorFlags.OccludeeStatic;
                if (want != flags) GameObjectUtility.SetStaticEditorFlags(go, want);
                if (shell) occluders++; else occludees++;
            }
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, GameObjectUtility.GetStaticEditorFlags(t.gameObject) | StaticEditorFlags.OccluderStatic);
            StaticOcclusionCulling.smallestOccluder = SmallestOccluder;
            StaticOcclusionCulling.smallestHole = SmallestHole;
            StaticOcclusionCulling.backfaceThreshold = 100f;
            bool ok = StaticOcclusionCulling.Compute();
            // (StaticOcclusionCulling.umbraDataSize reads 0 here; the data is the scene's OcclusionCullingData.asset, 22 MB for
            // the desert, written when the scene is saved.)
            Debug.Log($"[FPSKit] occlusion: {(ok ? "baked" : "FAILED")}, {occluders} occluders, {occludees} static occludees (the rest are culled as moving things), {clock.Elapsed.TotalSeconds:0} s");
            return ok;
        }

        /// <summary>The town's own shells: the building blocks and their facade detail, the landmarks' and the edge's
        /// structures (generated meshes, not props).</summary>
        static bool IsShell(Renderer r)
        {
            if (!(r is MeshRenderer)) return false;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || AssetDatabase.Contains(mf.sharedMesh)) return false;   // props and pack models
            if (r.name.StartsWith("Detail_")) return false;                                                    // window joinery: full of holes
            string path = "";
            for (var t = r.transform; t != null; t = t.parent) path = t.name + "/" + path;
            return path.Contains("/Buildings/") || path.Contains("/Landmarks/") || path.Contains("/Edge/");
        }
    }
}
#endif
