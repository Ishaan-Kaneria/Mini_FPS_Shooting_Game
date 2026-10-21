#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// VerifyTown: the desert town holds together as a place to fight in and to live in.
    ///
    ///   * Every famous location later levels will put enemies in is on the navmesh and joined to the player's start by a
    ///     complete path: the mosque's courtyard and prayer hall, the minaret's balcony (up its ramps), the citadel's
    ///     courtyard, wall-walk and keep roof, the palace hall, the souk street, the clock square, the gas station, the
    ///     junkyard and the oasis shore. A stair that stops short, a parapet with no gap, a doorway too narrow, a volume
    ///     over a walkway -- each shows up here as a location nothing can reach.
    ///   * The life is wired: civilian kinds with spots and doors, traffic routes with cars and signals, three cars to
    ///     drive, two technicals with a route, a gun and a gunner.
    ///   * No roof of a closed building is on the navmesh (the trapped-enemy bug), sampled over the buildings' tops.
    ///
    /// Opens the built scene; run after building Desert Outpost. Exits 0 or 1 like every check.
    /// </summary>
    public static class FPSKitTownTest
    {
        const string Scene = "Assets/FPSKit_Generated/Scenes/DesertOutpost.unity";

        public static void VerifyTown()
        {
            var problems = new List<string>();
            var notes = new StringBuilder();
            try
            {
                EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
                Check(problems, notes);
            }
            catch (Exception e) { problems.Add(e.ToString()); }

            if (problems.Count > 0)
            {
                Debug.LogError("[FPSKitBatch] FAILED: the desert town does not hold together:\n  - " + string.Join("\n  - ", problems) + notes);
                FPSKitBatch.Exit(1);
            }
            else
            {
                Debug.Log("[FPSKitBatch] verify town passed." + notes);
                FPSKitBatch.Exit(0);
            }
        }

        /// <summary>
        /// The people and cars are lit as the street round them: every civilian, traffic, parked, drivable and technical
        /// renderer receives shadows and blends the light probes on a lit URP shader; and the probes, where people walk, give
        /// about what the walls beside them take from the sky (they gave two to five times less before the bake was scaled,
        /// FPSKitTownProbes.TownProbeIndirect, and people read dark and cut out).
        /// </summary>
        static void CheckDynamicLighting(List<string> problems, StringBuilder notes)
        {
            int renderers = 0, wrong = 0;
            string first = null;
            var roots = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/FPSKit_Generated/Town" }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.Contains("Civilian_0") || p.Contains("Traffic_") || p.Contains("/Parked/"))
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>).ToList();
            roots.AddRange(UnityEngine.Object.FindObjectsByType<DrivableCar>(FindObjectsSortMode.None).Select(c => c.gameObject));
            roots.AddRange(UnityEngine.Object.FindObjectsByType<TechnicalTruck>(FindObjectsSortMode.None).Select(c => c.gameObject));
            foreach (var go in roots)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer || !r.enabled) continue;
                    renderers++;
                    bool ok = r.receiveShadows && r.lightProbeUsage == UnityEngine.Rendering.LightProbeUsage.BlendProbes &&
                              r.sharedMaterials.All(m => m == null || m.shader.name == "Universal Render Pipeline/Lit" || m.shader.name == "Universal Render Pipeline/Simple Lit");
                    if (ok) continue;
                    wrong++;
                    first ??= $"{go.name}/{r.name} (receive {r.receiveShadows}, probes {r.lightProbeUsage})";
                }
            if (renderers == 0) problems.Add("no civilian or car renderers found to check");
            else if (wrong > 0) problems.Add($"{wrong} of {renderers} civilian/car renderers not lit by the probes on a lit URP shader, e.g. {first}");

            // The probes where people walk against the ambient the walls take.
            float Side(UnityEngine.Rendering.SphericalHarmonicsL2 sh)
            {
                var c = new Color[4];
                sh.Evaluate(new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right }, c);
                return c.Average(x => 0.2126f * x.r + 0.7152f * x.g + 0.0722f * x.b);
            }
            if (LightmapSettings.lightProbes == null || LightmapSettings.lightProbes.count == 0) return;   // reported above
            float ambient = Side(RenderSettings.ambientProbe), probes = 0f;
            var walks = new[] { new Vector2(0f, -100f), new Vector2(11.5f, -60f), new Vector2(11.5f, -200f), new Vector2(135f, -60f),
                                new Vector2(-130f, 20f), new Vector2(-200f, 0f), new Vector2(200f, 25f) };
            foreach (var w in walks)
            {
                float y = Physics.Raycast(new Vector3(w.x, 300f, w.y), Vector3.down, out var hit, 600f) ? hit.point.y : 0f;
                LightProbes.GetInterpolatedProbe(new Vector3(w.x, y + 1.2f, w.y), null, out var sh);
                probes += Side(sh) / walks.Length;
            }
            float ratio = probes / Mathf.Max(0.001f, ambient);
            if (ratio < 0.6f || ratio > 1.4f)
                problems.Add($"light probes where people walk give {ratio:0.00} of the walls' ambient from the side (0.6-1.4 wanted: {probes:0.00} against {ambient:0.00})");
            else notes.AppendLine($"  people and cars: {renderers} renderers on the probes; probes give {ratio:0.00} of the walls' ambient from the side");
        }

        /// <summary>Every collider of the town that a round can strike carries a surface physics material (FPSImpactManager).</summary>
        static void CheckSurfaces(List<string> problems, StringBuilder notes)
        {
            var arena = GameObject.Find("Arena");
            if (arena == null) { problems.Add("no Arena in the scene"); return; }
            int env = LayerMask.NameToLayer("Environment"), total = 0, missing = 0;
            var kinds = new Dictionary<string, int>();
            foreach (var c in arena.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || c is WheelCollider || c is CharacterController || c.CompareTag("Flesh")) continue;
                if (c.gameObject.layer != env && !(c is TerrainCollider)) continue;
                total++;
                if (c.sharedMaterial == null || !c.sharedMaterial.name.StartsWith("Surface_")) { missing++; continue; }
                kinds[c.sharedMaterial.name] = kinds.TryGetValue(c.sharedMaterial.name, out int k) ? k + 1 : 1;
            }
            if (total == 0) problems.Add("no colliders found in the town");
            else if (missing > total * 0.02f) problems.Add($"{missing} of {total} town colliders without a surface physics material");
            else notes.AppendLine($"  surfaces: {total - missing} of {total} colliders ({string.Join(", ", kinds.Select(kv => $"{kv.Value} {kv.Key.Substring(8)}"))})");
        }

        static void Check(List<string> problems, StringBuilder notes)
        {
            var player = GameObject.FindWithTag("Player");
            if (player == null) { problems.Add("no player"); return; }
            if (!NavMesh.SamplePosition(player.transform.position, out var home, 4f, NavMesh.AllAreas)) { problems.Add("the player's start is not on the navmesh"); return; }

            // ---- light probes: baked into the scene's lighting data (one bake in five or so kept none, and the gun,
            // the civilians and the cars went dark) ----
            var group = UnityEngine.Object.FindAnyObjectByType<LightProbeGroup>();
            int placed = group != null ? group.probePositions.Length : 0, baked = LightmapSettings.lightProbes?.count ?? 0;
            if (placed == 0) problems.Add("no light probes placed");
            else if (baked < placed * 0.9f) problems.Add($"light probes not baked: {baked} of {placed} placed");
            else notes.AppendLine($"  light probes: {baked} baked of {placed} placed");
            if (LightmapSettings.lightmaps.Length > 0) problems.Add($"{LightmapSettings.lightmaps.Length} lightmaps baked (probes only wanted)");

            // ---- cover outside the shops (FPSKitTownCover): there, and each piece with a place to crouch beside it on
            // the navmesh, where EnemyBrain.FindCover looks ----
            var covers = UnityEngine.Object.FindObjectsByType<CoverSpot>(FindObjectsSortMode.None);
            int crouchable = 0;
            foreach (var c in covers)
            {
                var t = c.transform;
                if (NavMesh.SamplePosition(t.position - t.right * (c.halfLength + 0.45f), out _, 0.6f, NavMesh.AllAreas) ||
                    NavMesh.SamplePosition(t.position + t.right * (c.halfLength + 0.45f), out _, 0.6f, NavMesh.AllAreas)) crouchable++;
            }
            if (covers.Length < 100) problems.Add($"only {covers.Length} cover spots outside the shops (100 or more wanted)");
            else if (crouchable < covers.Length * 0.9f) problems.Add($"cover with nowhere to crouch beside it on the navmesh: {covers.Length - crouchable} of {covers.Length}");
            else notes.AppendLine($"  cover outside the shops: {covers.Length} spots, {crouchable} with a place to crouch beside them");

            CheckDynamicLighting(problems, notes);
            CheckSurfaces(problems, notes);

            // ---- famous locations ----
            var places = new List<(string name, Vector3 at)>
            {
                ("mosque courtyard", new Vector3(0f, 0f, 225f)),
                ("mosque prayer hall", new Vector3(-20f, 0f, 270f)),
                ("souk street", new Vector3(-130f, 0f, 20f)),
                ("clock square pavement", new Vector3(0f, 0f, 20f - 27f)),
                ("palace hall", new Vector3(-385f, 0f, -12f)),
                ("palace garden", new Vector3(-385f, 0f, -80f)),
                ("citadel courtyard", new Vector3(300f, 0f, 345f)),
                ("gas station forecourt", new Vector3(57f, 0f, -380f)),
                ("junkyard", new Vector3(-275f, 0f, -445f)),
                ("oasis shore", new Vector3(-318f, 0f, 345f - 42f - 6f)),
                ("garage", new Vector3(-281f, 0f, -137f)),
                ("water tower", new Vector3(-196f, 0f, -318f)),
            };
            // Raised places, found from their geometry rather than guessed heights.
            var minaret = GameObject.Find("Arena/Landmarks/Minaret");
            if (minaret != null)
            {
                var b = minaret.GetComponent<Renderer>().bounds;
                places.Add(("minaret balcony", Highest(new Vector3(b.center.x, 0f, b.min.z + 0.9f), b.min.y + 25f, b.min.y + 36f)));
            }
            else problems.Add("no Minaret");
            var citadel = GameObject.Find("Arena/Landmarks/Citadel");
            if (citadel != null)
            {
                places.Add(("citadel wall-walk", Highest(new Vector3(246.5f, 0f, 340f), 0f, 200f)));
                places.Add(("citadel keep roof", Highest(new Vector3(318f, 0f, 368f), 0f, 200f)));
            }
            else problems.Add("no Citadel");
            foreach (var n in new[] { "GrandMosque", "Palace", "ClockTower", "BazaarGate", "Souk" })
                if (GameObject.Find("Arena/Landmarks/" + n) == null) problems.Add($"no landmark {n}");

            // Reachability is proved in short legs. NavMesh.CalculatePath gives up on a long, polygon-dense route (its search
            // has a node budget): from the start, 580 m across the town, it returned PathPartial for the citadel wall-walk
            // and the keep roof while the same paths from the citadel courtyard were complete. So a grid of ground points is
            // proved outward from the start, each from the nearest already proved, and every place is proved from those.
            var reach = new Reach(home.position);
            int hubs = 0, hubsOff = 0;
            foreach (var g in GroundGrid(80f).OrderBy(g => (g - home.position).sqrMagnitude))
                if (reach.Prove(g)) hubs++; else hubsOff++;
            notes.Append($"\n  ground grid: {hubs} points proved from the start ({hubsOff} not: outside the town or inside a solid)");

            int reached = 0;
            foreach (var (name, at0) in places)
            {
                var at = at0;
                if (at.y == 0f) at.y = Ground(at);
                if (!NavMesh.SamplePosition(at, out var hit, 3f, NavMesh.AllAreas))
                { problems.Add($"{name} at {at} has no navmesh within 3 m"); continue; }
                if (!reach.Prove(hit.position))
                {
                    // Where it got to from the nearest proved point, and back: the gap is between the two ends.
                    var (fwd, rev) = reach.Ends(hit.position);
                    problems.Add($"{name} at {hit.position} cannot be reached (from the nearest proved point it gets to {fwd}, from there back to {rev})");
                    continue;
                }
                reached++;
                notes.Append($"\n  {name}: reached");
            }
            notes.Append($"\n  {reached}/{places.Count} famous locations reachable");

            // ---- closed roofs stay off the navmesh ----
            int roofs = 0, bad = 0;
            var islands = new List<string>();
            foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.name.StartsWith("Block_")) continue;
                var b = r.bounds;
                for (int i = 0; i < 40; i++)
                {
                    var p = new Vector3(UnityEngine.Random.Range(b.min.x, b.max.x), b.max.y + 5f, UnityEngine.Random.Range(b.min.z, b.max.z));
                    if (!Physics.Raycast(p, Vector3.down, out var h, 60f)) continue;
                    if (h.collider.GetComponent<MeshRenderer>() != r || h.point.y < Ground(h.point) + 3f) continue;   // only roofs, not yards
                    roofs++;
                    if (NavMesh.SamplePosition(h.point, out var nh, 0.6f, NavMesh.AllAreas) && !reach.Prove(nh.position, remember: false))
                    {
                        bad++;
                        if (islands.Count < 12) islands.Add($"({nh.position.x:0},{nh.position.y:0.0},{nh.position.z:0})");
                    }
                }
            }
            if (roofs > 0 && bad > roofs * 0.01f) problems.Add($"{bad} of {roofs} sampled roof points carry navmesh that cannot be reached (a closed building baked an island)");
            notes.Append($"\n  roofs: {roofs} points sampled, {bad} unreachable islands" + (islands.Count > 0 ? ": " + string.Join(" ", islands) : ""));

            // ---- life ----
            var civ = UnityEngine.Object.FindAnyObjectByType<CivilianDirector>();
            if (civ == null) problems.Add("no CivilianDirector");
            else
            {
                if (civ.prefabs == null || civ.prefabs.Length == 0 || civ.prefabs.Any(p => p == null)) problems.Add("the civilian director has no civilian prefabs");
                if (civ.spots == null || civ.spots.Length < 50) problems.Add($"only {civ.spots?.Length ?? 0} civilian spots");
                int spotsOnMesh = civ.spots?.Count(s => NavMesh.SamplePosition(s, out _, 3f, NavMesh.AllAreas)) ?? 0;
                if (civ.spots != null && spotsOnMesh < civ.spots.Length * 0.8f) problems.Add($"only {spotsOnMesh}/{civ.spots.Length} civilian spots are on the navmesh");
                notes.Append($"\n  civilians: {civ.prefabs?.Length} kinds, {spotsOnMesh}/{civ.spots?.Length} spots on the navmesh, {civ.doors?.Length} doors, budget {civ.maxCivilians}");
            }
            var traffic = UnityEngine.Object.FindAnyObjectByType<TrafficDirector>();
            if (traffic == null) problems.Add("no TrafficDirector");
            else
            {
                if (traffic.routes == null || traffic.routes.Length < 20) problems.Add($"only {traffic.routes?.Length ?? 0} traffic routes");
                if (traffic.carPrefabs == null || traffic.carPrefabs.Length == 0) problems.Add("no traffic car prefabs");
                int signals = traffic.signals?.Length ?? 0;
                if (signals < 3) problems.Add($"only {signals} signalled junctions");
                int stops = traffic.routes?.Sum(r => r.stopIndex?.Length ?? 0) ?? 0;
                notes.Append($"\n  traffic: {traffic.routes?.Length} routes, {stops} stop lines, {signals} signals, budget {traffic.maxCars}");
            }
            var drivable = UnityEngine.Object.FindObjectsByType<DrivableCar>(FindObjectsSortMode.None);
            if (drivable.Length < 3) problems.Add($"only {drivable.Length} drivable cars");
            foreach (var d in drivable)
                if (d.frontLeft == null || d.rearRight == null) problems.Add($"{d.name} has no wheel colliders");
            var techs = UnityEngine.Object.FindObjectsByType<TechnicalTruck>(FindObjectsSortMode.None);
            if (techs.Length < 2) problems.Add($"only {techs.Length} technicals");
            foreach (var t in techs)
                if (t.route == null || t.route.Length < 5 || t.gunner == null || t.muzzle == null) problems.Add($"{t.name} is missing its route, gunner or gun");
            notes.Append($"\n  {drivable.Length} drivable cars, {techs.Length} technicals");

            // What the buildings cost: the chunks (walls, reveals, glass, courses, cornices) and their detail (joinery,
            // dressings, iron; culled past ~180 m).
            long shell = 0, detail = 0;
            foreach (var mf in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (mf.sharedMesh == null) continue;
                if (mf.name.StartsWith("Block_")) shell += mf.sharedMesh.triangles.LongLength / 3;
                else if (mf.name.StartsWith("Detail_")) detail += mf.sharedMesh.triangles.LongLength / 3;
            }
            notes.Append($"\n  buildings: {shell / 1000}k triangles in the chunks, {detail / 1000}k in their detail");
        }

        /// <summary>What the editor renders the town with (batch: FPSKit.EditorTools.FPSKitTownTest.RenderInfo).</summary>
        public static void RenderInfo()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var sb = new StringBuilder();
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            sb.AppendLine($"quality level {QualitySettings.GetQualityLevel()} ({QualitySettings.names[QualitySettings.GetQualityLevel()]}), build target {EditorUserBuildSettings.activeBuildTarget}");
            sb.AppendLine($"pipeline {(rp != null ? AssetDatabase.GetAssetPath(rp) : "none")}: main light shadows {rp?.supportsMainLightShadows}, distance {rp?.shadowDistance}, resolution {rp?.mainLightShadowmapResolution}, cascades {rp?.shadowCascadeCount}");
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) sb.AppendLine($"directional {l.name}: intensity {l.intensity}, shadows {l.shadows}, strength {l.shadowStrength}, euler {l.transform.eulerAngles}, enabled {l.enabled}, mode {l.lightmapBakeType}");
            sb.AppendLine($"RenderSettings.sun {(RenderSettings.sun != null ? RenderSettings.sun.name : "none")}");
            sb.AppendLine($"device {SystemInfo.graphicsDeviceType} '{SystemInfo.graphicsDeviceName}', supportsShadows {SystemInfo.supportsShadows}, " +
                          $"QualitySettings.shadows {QualitySettings.shadows}, batch {Application.isBatchMode}");
            System.IO.File.WriteAllText("Build/TownRenderInfo.txt", sb.ToString());
            FPSKitBatch.Exit(0);
        }

        /// <summary>What the built town costs in memory, by kind (batch: FPSKit.EditorTools.FPSKitTownTest.Memory).</summary>
        public static void Memory()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var sb = new StringBuilder();
            var byKind = new Dictionary<string, (long bytes, int count, long tris)>();
            void Add(string k, long b, long t = 0) { byKind.TryGetValue(k, out var v); byKind[k] = (v.bytes + b, v.count + 1, v.tris + t); }
            var seenMesh = new HashSet<Mesh>();
            foreach (var mf in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || !seenMesh.Add(mesh)) continue;
                string k = mf.name.StartsWith("Block_") ? "mesh: building chunks" : mf.name.StartsWith("Detail_") ? "mesh: building detail"
                         : AssetDatabase.Contains(mesh) ? "mesh: assets (props, cars, palms)" : "mesh: other scene-built";
                Add(k, UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(mesh), mesh.triangles.LongLength / 3);
            }
            foreach (var mc in UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None)) Add("mesh colliders", 0);
            var seenTex = new HashSet<Texture>();
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    foreach (var name in m.GetTexturePropertyNames())
                    {
                        var t = m.GetTexture(name);
                        if (t != null && seenTex.Add(t)) Add("textures (materials)", UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t));
                    }
                }
            foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
                if (t.terrainData != null) Add("terrain data", UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t.terrainData));
            foreach (var t in seenTex.OrderByDescending(t => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t)).Take(25))
                sb.AppendLine($"  tex {UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t) / 1048576f:0.0} MB {t.width}x{t.height} {AssetDatabase.GetAssetPath(t)}");
            // Colliders and objects by the top-level group they sit under.
            var colByGroup = new Dictionary<string, (int n, long tris)>();
            foreach (var mc in UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
            {
                var t = mc.transform; while (t.parent != null && t.parent.parent != null) t = t.parent;
                string g = t.name;
                colByGroup.TryGetValue(g, out var v);
                colByGroup[g] = (v.n + 1, v.tris + (mc.sharedMesh != null ? mc.sharedMesh.triangles.LongLength / 3 : 0));
            }
            foreach (var kv in colByGroup.OrderByDescending(k => k.Value.tris).Take(12)) sb.AppendLine($"  mesh colliders under {kv.Key}: {kv.Value.n}, {kv.Value.tris / 1000}k triangles");
            var byMesh = new Dictionary<string, (int n, long tris)>();
            foreach (var mc in UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None))
            {
                if (mc.sharedMesh == null) continue;
                string k = mc.sharedMesh.name + (mc.convex ? " (convex)" : "");
                byMesh.TryGetValue(k, out var v);
                byMesh[k] = (v.n + 1, v.tris + mc.sharedMesh.triangles.LongLength / 3);
            }
            foreach (var kv in byMesh.OrderByDescending(k => k.Value.tris).Take(15)) sb.AppendLine($"    collider mesh {kv.Key}: {kv.Value.n} x, {kv.Value.tris / 1000}k triangles");
            var goByGroup = new Dictionary<string, int>();
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                var t = go.transform; while (t.parent != null && t.parent.parent != null) t = t.parent;
                goByGroup.TryGetValue(t.name, out int n); goByGroup[t.name] = n + 1;
            }
            foreach (var kv in goByGroup.OrderByDescending(k => k.Value).Take(12)) sb.AppendLine($"  objects under {kv.Key}: {kv.Value}");
            var surf = UnityEngine.Object.FindAnyObjectByType<Unity.AI.Navigation.NavMeshSurface>();
            if (surf != null && surf.navMeshData != null) sb.AppendLine($"  navmesh data: {UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(surf.navMeshData) / 1048576} MB ({AssetDatabase.GetAssetPath(surf.navMeshData)})");
            int parked = 0; Bounds pb = default; string first = "";
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.StartsWith("Parked_"))
                {
                    if (parked++ == 0)
                    {
                        var rs = t.GetComponentsInChildren<Renderer>();
                        pb = rs.Length > 0 ? rs[0].bounds : default;
                        foreach (var r in rs) pb.Encapsulate(r.bounds);
                        first = $"{t.name} at {t.position}, {rs.Length} renderers, mats {string.Join(",", rs.Select(r => r.sharedMaterials.Length))}, verts {string.Join(",", t.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh != null ? f.sharedMesh.vertexCount : -1))}";
                    }
                }
            sb.AppendLine($"  parked cars: {parked}; first: {first}, bounds size {pb.size}");
            int gos = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Length;
            sb.AppendLine($"GameObjects: {gos}");
            foreach (var kv in byKind.OrderByDescending(k => k.Value.bytes))
                sb.AppendLine($"{kv.Key}: {kv.Value.bytes / 1048576} MB, {kv.Value.count} items, {kv.Value.tris / 1000}k triangles");
            sb.AppendLine($"total allocated: {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576} MB, reserved {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576} MB");
            System.IO.File.WriteAllText("Build/TownMemory.txt", sb.ToString());
            Debug.Log("[FPSKit] memory written");
            FPSKitBatch.Exit(0);
        }

        /// <summary>
        /// Diagnosis for a failure here (batch: <c>FPSKit.EditorTools.FPSKitTownTest.Probe</c>, writes Build/TownProbePoints.txt).
        /// Height maps of the navmesh round given places: per 0.5 m cell the highest navmesh, its height band as a letter
        /// (a = ground, then one letter per 1.5 m), upper case when it is proved reachable from the start; '.' = none.
        /// A sealed yard reads as lower-case ground, a stair cut part way as upper case giving way to lower case.
        /// </summary>
        public static void Probe()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var sb = new StringBuilder();
            var player = GameObject.FindWithTag("Player");
            NavMesh.SamplePosition(player.transform.position, out var home, 4f, NavMesh.AllAreas);
            var reach = new Reach(home.position);
            foreach (var g in GroundGrid(80f).OrderBy(g => (g - home.position).sqrMagnitude)) reach.Prove(g);

            void HeightMap(string name, float x0, float z0, float x1, float z1)
            {
                sb.AppendLine($"== height map {name} x {x0}..{x1}, z {z1}..{z0} (top to bottom), 0.5 m cells");
                for (float z = z1; z >= z0 - 0.01f; z -= 0.5f)
                {
                    var line = new StringBuilder($"{z,7:0.0} ");
                    for (float x = x0; x <= x1 + 0.01f; x += 0.5f)
                    {
                        float g = Ground(new Vector3(x, 0f, z));
                        Vector3 best = Vector3.zero; bool any = false;
                        for (float y = g - 1f; y < g + 14f; y += 0.4f)
                            if (NavMesh.SamplePosition(new Vector3(x, y, z), out var nh, 0.25f, NavMesh.AllAreas) && Mathf.Abs(nh.position.x - x) < 0.2f && Mathf.Abs(nh.position.z - z) < 0.2f)
                            { if (!any || nh.position.y > best.y) best = nh.position; any = true; }
                        if (!any) { line.Append('.'); continue; }
                        char c = (char)('a' + Mathf.Clamp(Mathf.FloorToInt((best.y - g + 0.75f) / 1.5f), 0, 25));
                        line.Append(reach.Prove(best, remember: false) ? char.ToUpper(c) : c);
                    }
                    sb.AppendLine(line.ToString());
                }
            }
            ReachImage(reach, "Build/TownReach.png");
            System.IO.File.WriteAllText("Build/TownProbePoints.txt", sb.ToString());
            Debug.Log("[FPSKit] probe written");
            FPSKitBatch.Exit(0);
        }

        /// <summary>
        /// The whole town at 2 m a pixel, from the ground up to 4 m: white where the navmesh is proved reachable, red where
        /// it is not (a stranded patch), dark where there is none. Proved outward from the start, each cell from its
        /// neighbours already proved, so the red is exactly what VerifyReach would count.
        /// </summary>
        static void ReachImage(Reach reach, string file)
        {
            const int N = 500; const float Cell = 2f, Half = 500f;
            var tex = new Texture2D(N, N, TextureFormat.RGB24, false);
            var cells = new List<(int i, int j, Vector3 p)>();
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    tex.SetPixel(i, j, new Color(0.12f, 0.1f, 0.09f));
                    var q = new Vector3(-Half + (i + 0.5f) * Cell, 0f, -Half + (j + 0.5f) * Cell);
                    q.y = Ground(q) + 1.5f;
                    if (NavMesh.SamplePosition(q, out var h, 2.5f, NavMesh.AllAreas) && Mathf.Abs(h.position.x - q.x) < 1f && Mathf.Abs(h.position.z - q.z) < 1f)
                        cells.Add((i, j, h.position));
                }
            foreach (var (i, j, p) in cells.OrderBy(c => (c.p - reach.Start).sqrMagnitude))
                tex.SetPixel(i, j, reach.Prove(p, remember: i % 4 == 0 && j % 4 == 0) ? new Color(0.92f, 0.9f, 0.85f) : new Color(0.9f, 0.1f, 0.1f));
            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        }

        /// <summary>
        /// Points proved reachable from the start, and the proof of a new one: a complete path from one of the three proved
        /// points nearest it. Legs stay short, inside the search's budget.
        /// </summary>
        sealed class Reach
        {
            readonly List<Vector3> _proved = new List<Vector3>();
            readonly NavMeshPath _path = new NavMeshPath();
            readonly Dictionary<Vector2Int, List<Vector3>> _cells = new Dictionary<Vector2Int, List<Vector3>>();
            public readonly Vector3 Start;
            public Reach(Vector3 start) { Start = start; Add(start); }

            static Vector2Int CellOf(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / 30f), Mathf.FloorToInt(p.z / 30f));
            void Add(Vector3 p)
            {
                _proved.Add(p);
                var c = CellOf(p);
                if (!_cells.TryGetValue(c, out var l)) _cells[c] = l = new List<Vector3>();
                l.Add(p);
            }

            /// <summary>The three proved points nearest <paramref name="t"/>: from its own and the neighbouring 30 m cells, or from all.</summary>
            IEnumerable<Vector3> Nearest(Vector3 t)
            {
                var c = CellOf(t);
                var near = new List<Vector3>();
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                        if (_cells.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var l)) near.AddRange(l);
                return (near.Count > 0 ? near : _proved).OrderBy(p => (p - t).sqrMagnitude).Take(3);
            }

            public bool Prove(Vector3 t, bool remember = true)
            {
                foreach (var p in Nearest(t))
                    if (NavMesh.CalculatePath(p, t, NavMesh.AllAreas, _path) && _path.status == NavMeshPathStatus.PathComplete)
                    {
                        if (remember) Add(t);
                        return true;
                    }
                return false;
            }

            public (string fwd, string rev) Ends(Vector3 t)
            {
                var p = Nearest(t).First();
                NavMesh.CalculatePath(p, t, NavMesh.AllAreas, _path);
                string fwd = _path.corners.Length > 0 ? _path.corners[_path.corners.Length - 1].ToString() : "nowhere";
                NavMesh.CalculatePath(t, p, NavMesh.AllAreas, _path);
                string rev = _path.corners.Length > 0 ? _path.corners[_path.corners.Length - 1].ToString() : "nowhere";
                return (fwd, rev);
            }
        }

        /// <summary>Navmesh on the ground (not a roof) at every node of a square grid over the town.</summary>
        static IEnumerable<Vector3> GroundGrid(float step)
        {
            for (float x = -480f; x <= 480f; x += step)
                for (float z = -480f; z <= 480f; z += step)
                {
                    var p = new Vector3(x, 0f, z);
                    p.y = Ground(p);
                    if (NavMesh.SamplePosition(p, out var h, step * 0.4f, NavMesh.AllAreas) && h.position.y < Ground(h.position) + 1.5f)
                        yield return h.position;
                }
        }

        static float Ground(Vector3 p)
        {
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, 400f, p.z), Vector3.down, 900f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider is TerrainCollider) best = Mathf.Max(best, h.point.y);
            return float.IsInfinity(best) ? 0f : best;
        }

        /// <summary>The highest surface under a point between two heights (a walkway on top of a wall, a roof).</summary>
        static Vector3 Highest(Vector3 p, float minY, float maxY)
        {
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, maxY, p.z), Vector3.down, maxY - minY, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => -h.point.y))
                return h.point + Vector3.up * 0.1f;
            return new Vector3(p.x, minY, p.z);
        }
    }
}
#endif
