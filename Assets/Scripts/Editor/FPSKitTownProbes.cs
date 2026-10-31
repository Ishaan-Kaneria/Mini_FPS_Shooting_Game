#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Light probes for the desert town's moving things -- civilians, traffic, the player's body and gun -- so they take
    /// the ambient light of where they stand: the open avenue's sky, the darker air of a narrow street, the warm light
    /// thrown back off a sunlit pavement into the shade.
    ///
    /// Probes hold nothing until they are baked, so the town ends with a bake of **probes only**: the buildings, roads
    /// and landmarks contribute to it (they block the sky and bounce the sun) but receive their own light from the
    /// ambient as before (no lightmaps, no lightmap memory, nothing new at run time). The sun is Mixed with Baked Indirect:
    /// its direct light and shadows stay real-time, and only what it bounces goes into the probes.
    ///
    /// Placed densest where light changes: at doorways, and where the sun meets the shade along a street (found by
    /// casting towards the sun every two metres); every eight metres on the pavements of the avenue and the boulevard,
    /// sixteen on their carriageways, at every civilian spot, and a forty-metre grid over the rest of the town so
    /// nothing is lit from a probe a hundred metres away. None inside a building (a probe walled in on all sides and
    /// roofed would be black, and bleed onto anything outside the wall).
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static int _townProbeCount;
        /// <summary>
        /// The probe bake's indirect strength (see BakeTownProbes); VerifyTown checks the probes against the ambient. 2.75 when
        /// the pavers' albedo was 0.10 and the sand's 0.18; with the ground at its real 0.25-0.32 (the realism pass, step 1)
        /// the same 2.75 gave people 1.9 of the walls' ambient, most of it bounced up off the street (from below 1.70, sun
        /// off 0.69; the old ground 0.14-0.29). The bake is linear in it (0.62 at 2.75, 0.22 at 1.0): 1.15 brings the sides
        /// back to about 0.8 of the walls' ambient, 2026-10-29. Only where the bake starts: it then calibrates itself to
        /// TownProbeRatio (the same code baked 0.34 on 2026-10-30).
        /// </summary>
        private const float TownProbeIndirect = 1.15f;
        /// <summary>What the bake aims the people's probes at, from the side, against the walls' ambient (VerifyTown: 0.6-1.4).</summary>
        private const float TownProbeRatio = 0.8f;

        private static void PlaceTownLightProbes(Transform root)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var points = new List<Vector3>();
            var cells = new HashSet<Vector3Int>();
            int transitions = 0, doors = 0, spots = 0;
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional && l.shadows != LightShadows.None);
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.up;

            bool Add(Vector3 p, float merge = 1.2f)
            {
                var c = Vector3Int.FloorToInt(p / merge);
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                    if (cells.Contains(c + new Vector3Int(x, y, z))) return false;
                if (Physics.CheckSphere(p, 0.15f, ~0, QueryTriggerInteraction.Ignore)) return false;     // inside something
                if (Enclosed(p)) return false;
                cells.Add(c);
                points.Add(p);
                return true;
            }
            void Column(Vector2 xz, float low, float high, float merge = 1.2f)
            {
                float g = GroundHeightAt(xz.x, xz.y);
                if (RoadAt(xz) != null) g = Mathf.Max(g, TownRoadY(xz.x, xz.y));
                Add(new Vector3(xz.x, g + low, xz.y), merge);
                Add(new Vector3(xz.x, g + high, xz.y), merge);
            }
            bool Sunlit(Vector2 xz)
            {
                float g = GroundHeightAt(xz.x, xz.y);
                return !Physics.Raycast(new Vector3(xz.x, g + 1f, xz.y), toSun, 200f, ~0, QueryTriggerInteraction.Ignore);
            }

            // The avenue and the boulevard: pavements every 8 m, carriageway every 16 m, and wherever the sun meets the shade.
            foreach (var road in _townRoads.Where(r => r.name == "AvenueSouth" || r.name == "AvenueNorth" || r.name == "Boulevard"))
            {
                Vector2 At(float along, float across) => road.AlongX ? new Vector2(along, road.Fixed + across) : new Vector2(road.Fixed + across, along);
                float pave = road.Half + 2.5f;
                var lines = new[] { (-pave, 8f), (pave, 8f), (-road.Half * 0.5f, 16f), (road.Half * 0.5f, 16f) };
                foreach (var (across, step) in lines)
                {
                    bool? last = null;
                    for (float a = road.Min; a <= road.Max; a += 2f)
                    {
                        var xz = At(a, across);
                        if (Mathf.Repeat(a - road.Min, step) < 2f) Column(xz, 0.7f, 2.4f);
                        bool lit = Sunlit(xz);
                        if (last.HasValue && last.Value != lit)
                        {
                            // A shadow's edge: probes either side of it, a metre and a half apart.
                            Column(At(a - 1.5f, across), 0.7f, 2.4f, 0.8f);
                            Column(xz, 0.7f, 2.4f, 0.8f);
                            Column(At(a + 1.5f, across), 0.7f, 2.4f, 0.8f);
                            transitions++;
                        }
                        last = lit;
                    }
                }
            }
            // Every doorway (just outside it), and every place a civilian stands.
            foreach (var d in _townDoors) { int before = points.Count; Column(new Vector2(d.x, d.z), 1.0f, 2.4f, 0.9f); if (points.Count > before) doors++; }
            foreach (var s in CivilianSpots()) { int before = points.Count; Column(new Vector2(s.x, s.z), 0.9f, 2.2f, 3f); if (points.Count > before) spots++; }
            // A sparse grid over the rest, so a car on a side street is not lit from a probe on the avenue.
            for (float x = -480f; x <= 480f; x += 40f)
                for (float z = -480f; z <= 480f; z += 40f)
                    Column(new Vector2(x, z), 1.5f, 7f, 12f);

            var go = new GameObject("TownLightProbes");
            go.transform.SetParent(root, false);
            var group = go.AddComponent<LightProbeGroup>();
            group.probePositions = points.ToArray();      // the group sits at the origin, unscaled: world positions
            _townProbeCount = points.Count;
            Debug.Log($"[FPSKit] town light probes: {points.Count} ({transitions} sun/shade edges, {doors} doorways, {spots} civilian spots) in {clock.Elapsed.TotalSeconds:0.0}s");
        }

        /// <summary>Roofed, with walls close on every side: inside a building.</summary>
        private static bool Enclosed(Vector3 p)
        {
            if (!Physics.Raycast(p, Vector3.up, 30f, ~0, QueryTriggerInteraction.Ignore)) return false;
            foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                if (!Physics.Raycast(p, d, 25f, ~0, QueryTriggerInteraction.Ignore)) return false;
            return true;
        }

        /// <summary>Batch: the saved desert scene's light baked again, the people's probes and the adaptive probe volumes
        /// (FPSKitBatch.BakeDesertProbes; UNITY_GRAPHICS=1, -force-vulkan).</summary>
        public static void BakeSavedTownProbes() => BakeTownProbes(default, FPSKitTownGI.DesertScene);

        /// <summary>
        /// Bakes the town's probes (and nothing else) after the scene is saved, then saves it again. The geometry that
        /// shades and bounces light -- building chunks, landmarks, roads, the edge's structures -- is marked Contribute GI
        /// and set to take its own indirect light from probes but not to use them (so it keeps the ambient it has always
        /// had, and gets no lightmap). CPU lightmapper, so it runs in a headless build.
        /// </summary>
        private static void BakeTownProbes(UnityEngine.SceneManagement.Scene scene, string path)
        {
            // Always the saved scene, opened again (about 12 s). Baked in the build's own copy of it, the people's probes came
            // out at 0.34 of the walls' ambient; the same scene saved, opened and baked gave 0.80 at the same strength
            // (2026-10-30/31; the light from below was intact in the second). Everything after works on a saved scene too.
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var placed = GameObject.Find("TownLightProbes")?.GetComponent<LightProbeGroup>();
            _townProbeCount = placed != null ? placed.probePositions.Length : 0;
            if (_townProbeCount == 0) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int contributors = 0;
            // Nothing else takes part, or it gets a lightmap: the terrain is created static (every flag) and some prop
            // prefabs are saved static, and the first bake made two lightmaps of them.
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                if ((flags & StaticEditorFlags.ContributeGI) != 0) GameObjectUtility.SetStaticEditorFlags(go, flags & ~StaticEditorFlags.ContributeGI);
            }
            foreach (var groupName in new[] { "Buildings", "Landmarks", "Roads", "Edge" })
            {
                var g = GameObject.Find("Arena/" + groupName);
                if (g == null) continue;
                foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || AssetDatabase.Contains(mf.sharedMesh)) continue;   // props and pack models stay out
                    if (r.name.StartsWith("Detail_")) continue;                                                      // window joinery: nothing to bounce
                    var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags | StaticEditorFlags.ContributeGI);
                    r.receiveGI = ReceiveGI.LightProbes;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    contributors++;
                }
            }
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) l.lightmapBakeType = l.shadows != LightShadows.None ? LightmapBakeType.Mixed : LightmapBakeType.Realtime;

            string settingsPath = path.Replace(".unity", "_Lighting.lighting");
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(settingsPath);
            if (settings == null) { settings = new LightingSettings { name = "TownLighting" }; AssetDatabase.CreateAsset(settings, settingsPath); }
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.autoGenerate = false;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            settings.directSampleCount = 32;
            settings.indirectSampleCount = 128;
            settings.environmentSampleCount = 128;
            settings.maxBounces = 2;
            // The probes light the moving things; the static ones (walls, roads) take the sky ambient whole, unshaded by
            // the street. At the bake's own strength the probes gave a civilian or a car two to five times less light than
            // the wall beside it (measured 2026-10-21: probe sides 0.17 against the ambient's 0.56), so people read dark and
            // cut out. Scaled so the probes' sides average about 0.8 of the walls' ambient (2.75: 0.46), the sky-facing top
            // a little over it; the probes' own gradient -- a shaded street darker than a sunlit one -- is kept.
            settings.indirectScale = TownProbeIndirect;
            settings.lightProbeSampleCountMultiplier = 2f;
            settings.ao = false;
            settings.lightmapResolution = 1f;
            EditorUtility.SetDirty(settings);
            Lightmapping.lightingSettings = settings;

            // The street reflection probes are realtime and refresh themselves on load, so they are kept out of the bake.
            // Left in, the bake captured them too -- headless, with no graphics device, into blank cubemaps
            // (ReflectionProbe-N.exr) -- and the lighting data handed those to the probes in play: everything inside
            // their boxes, the gun included, drew flat white (2026-10-21). Any such capture left from an earlier bake goes.
            var reflection = Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).Where(p => p.enabled).ToList();
            foreach (var p in reflection) p.enabled = false;
            bool ok;
            // The classic probes are baked by a pipeline that uses them: Low (High and Medium use adaptive probe volumes,
            // and with those on, a bake leaves light probe groups out). FPSKitTownGI bakes the volumes after.
            int quality = QualitySettings.GetQualityLevel();
            int low = System.Array.IndexOf(QualitySettings.names, "Low");
            if (low >= 0) QualitySettings.SetQualityLevel(low, true);
            try
            {
                ok = Lightmapping.Bake();
                // Now and then (one build in five on 2026-10-21) the bake finishes and keeps no probes at all; the gun, the
                // civilians and the cars then had no light but the sun. Baked again from a clear cache, it keeps them.
                for (int again = 1; again <= 2 && (LightmapSettings.lightProbes?.count ?? 0) < _townProbeCount * 0.9f; again++)
                {
                    Debug.LogWarning($"[FPSKit] town light probes: the bake kept {LightmapSettings.lightProbes?.count ?? 0} of {_townProbeCount}; " +
                                     $"clearing the GI cache and baking again ({again})");
                    Lightmapping.Clear();
                    Lightmapping.ClearDiskCache();
                    ok = Lightmapping.Bake();
                }
                // Then to its level. Rebuilds of the same code baked the people's sides at 1.9, 0.8 and 0.34 of the walls'
                // ambient (2026-10-30), and the bake is linear in its indirect strength, so one more bake at the strength
                // scaled by what it missed lands it (twice at most). The strength is set per bake, not kept.
                for (int pass = 1; pass <= 2 && ok && (LightmapSettings.lightProbes?.count ?? 0) > 0; pass++)
                {
                    float measured = FPSKitTownTest.PeopleProbeRatio(out float s0, out float a0, out float b0);
                    if (measured >= TownProbeRatio - 0.1f && measured <= TownProbeRatio + 0.1f) break;
                    float was = settings.indirectScale;
                    settings.indirectScale = Mathf.Clamp(was * TownProbeRatio / Mathf.Max(0.05f, measured), 0.25f, 6f);
                    Debug.Log($"[FPSKit] town light probes: people's sides {measured:0.00} of the walls' ambient ({s0:0.00} against {a0:0.00}, " +
                              $"from below {b0:0.00} of the ambient's) at indirect {was:0.00}; baking again at {settings.indirectScale:0.00} ({pass})");
                    EditorUtility.SetDirty(settings);
                    ok = Lightmapping.Bake();
                }
            }
            finally { foreach (var p in reflection) p.enabled = true; QualitySettings.SetQualityLevel(quality, true); }
            if ((LightmapSettings.lightProbes?.count ?? 0) == 0) Debug.LogError("[FPSKit] town light probes: none baked (VerifyTown fails on it)");
            string dataFolder = path.Substring(0, path.Length - ".unity".Length);
            if (AssetDatabase.IsValidFolder(dataFolder))
                foreach (var guid in AssetDatabase.FindAssets("ReflectionProbe-", new[] { dataFolder }))
                    AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            var probes = LightmapSettings.lightProbes;
            float ratio = FPSKitTownTest.PeopleProbeRatio(out float sides, out float ambient, out float below);
            Debug.Log($"[FPSKit] town light probes baked: {(ok ? "done" : "FAILED")}, {probes?.count ?? 0} probes, {contributors} contributing meshes, " +
                      $"{LightmapSettings.lightmaps.Length} lightmaps (0 wanted), people's sides {ratio:0.00} of the walls' ambient ({sides:0.00} against {ambient:0.00}, " +
                      $"from below {below:0.00} of the ambient's) at indirect {settings.indirectScale:0.00}, {clock.Elapsed.TotalSeconds:0}s");
            EditorSceneManager.SaveScene(scene, path);
            // Then the real bounced light, for High and Medium (Docs/DESIGN_NOTES.md "Real light: adaptive probe volumes").
            // FPSKIT_SKIP_APV=1: leave the adaptive probe bake to FPSKitBatch.BakeDesertLight on the saved scene. Baked in the
            // same editor as the build, the build's 5.6 GB and the baker's together were killed by earlyoom (2026-10-27).
            if (System.Environment.GetEnvironmentVariable("FPSKIT_SKIP_APV") == "1") Debug.Log("[FPSKit] town GI: skipped (FPSKIT_SKIP_APV); bake it with FPSKitBatch.BakeDesertLight");
            else if (!FPSKitTownGI.BakeNow(scene, path)) Debug.LogError("[FPSKit] town GI: the adaptive probe volume bake failed");
            else Debug.Log($"[FPSKit] town light probes after the adaptive probe bake: people's sides {FPSKitTownTest.PeopleProbeRatio(out sides, out ambient):0.00} of the walls' ambient ({sides:0.00} against {ambient:0.00})");
            // FPSKIT_SKIP_OCCLUSION=1: a re-bake of the probes alone keeps the occlusion data it has (210 s, 22 MB rewritten).
            if (System.Environment.GetEnvironmentVariable("FPSKIT_SKIP_OCCLUSION") != "1")
                FPSKitTownOcclusion.Bake();     // what the streets' buildings hide is not drawn
            EditorSceneManager.SaveScene(scene, path);
        }
    }
}
#endif
