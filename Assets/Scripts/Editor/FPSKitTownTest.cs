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

        static void Check(List<string> problems, StringBuilder notes)
        {
            var player = GameObject.FindWithTag("Player");
            if (player == null) { problems.Add("no player"); return; }
            if (!NavMesh.SamplePosition(player.transform.position, out var home, 4f, NavMesh.AllAreas)) { problems.Add("the player's start is not on the navmesh"); return; }

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

            var path = new NavMeshPath();
            int reached = 0;
            foreach (var (name, at0) in places)
            {
                var at = at0;
                if (at.y == 0f) at.y = Ground(at);
                if (!NavMesh.SamplePosition(at, out var hit, 3f, NavMesh.AllAreas))
                { problems.Add($"{name} at {at} has no navmesh within 3 m"); continue; }
                if (!NavMesh.CalculatePath(home.position, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                {
                    // Where it got to, both ways: the gap is between the two ends.
                    var back = new NavMeshPath();
                    NavMesh.CalculatePath(hit.position, home.position, NavMesh.AllAreas, back);
                    string fwd = path.corners.Length > 0 ? path.corners[path.corners.Length - 1].ToString() : "nowhere";
                    string rev = back.corners.Length > 0 ? back.corners[back.corners.Length - 1].ToString() : "nowhere";
                    problems.Add($"{name} at {hit.position} cannot be reached from the start (path {path.status}; from the start it gets to {fwd}, from there to {rev})");
                    continue;
                }
                reached++;
                notes.Append($"\n  {name}: reached ({PathLength(path):0} m)");
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
                    if (NavMesh.SamplePosition(h.point, out var nh, 0.6f, NavMesh.AllAreas) &&
                        (!NavMesh.CalculatePath(home.position, nh.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete))
                    {
                        bad++;
                        if (islands.Count < 12) islands.Add($"({nh.position.x:0},{nh.position.y:0.0},{nh.position.z:0})");
                    }
                }
            }
            if (roofs > 0 && bad > roofs * 0.03f) problems.Add($"{bad} of {roofs} sampled roof points carry navmesh that cannot be reached (a closed building baked an island)");
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
        }

        /// <summary>Diagnosis: what stands at given points (every collider down a vertical ray, the navmesh near each, the volumes
        /// over it), and character maps of the navmesh round awkward places (R reached from the start, o navmesh not reached).</summary>
        public static void Probe()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var sb = new StringBuilder();
            var player = GameObject.FindWithTag("Player");
            NavMesh.SamplePosition(player.transform.position, out var home, 4f, NavMesh.AllAreas);
            var pts = new[] { new Vector3(208f, 0, 170f), new Vector3(199f, 0, 162f), new Vector3(-257f, 0, -253f), new Vector3(-344f, 0, -323f),
                              new Vector3(-451f, 0, -177f), new Vector3(-256f, 0, -177f), new Vector3(-267f, 0, -177f) };
            var vols = UnityEngine.Object.FindObjectsByType<Unity.AI.Navigation.NavMeshModifierVolume>(FindObjectsSortMode.None);
            foreach (var p in pts)
            {
                sb.AppendLine($"== {p.x},{p.z}");
                foreach (var h in Physics.RaycastAll(new Vector3(p.x, 300f, p.z), Vector3.down, 700f).OrderBy(h => -h.point.y))
                    sb.AppendLine($"  hit {h.collider.name} ({h.collider.transform.parent?.name}) y={h.point.y:0.00}");
                for (float y = -5f; y < 40f; y += 0.5f)
                    if (NavMesh.SamplePosition(new Vector3(p.x, y, p.z), out var nh, 0.3f, NavMesh.AllAreas))
                    { sb.AppendLine($"  navmesh at y={nh.position.y:0.00} ({nh.position.x:0.0},{nh.position.z:0.0})"); y = nh.position.y + 1.5f; }
                foreach (var v in vols)
                {
                    var c = v.transform.TransformPoint(v.center); var e = v.size * 0.5f;
                    if (Mathf.Abs(p.x - c.x) <= e.x && Mathf.Abs(p.z - c.z) <= e.z)
                        sb.AppendLine($"  volume y {c.y - e.y:0.0}..{c.y + e.y:0.0} x {c.x - e.x:0.0}..{c.x + e.x:0.0} z {c.z - e.z:0.0}..{c.z + e.z:0.0}");
                }
            }
            void Map(string name, float x0, float z0, float x1, float z1, float y, float step)
            {
                sb.AppendLine($"== map {name} x {x0}..{x1} (left to right), z {z1}..{z0} (top to bottom), y {y}");
                var path = new NavMeshPath();
                for (float z = z1; z >= z0 - 0.01f; z -= step)
                {
                    var line = new StringBuilder($"{z,7:0.0} ");
                    for (float x = x0; x <= x1 + 0.01f; x += step)
                    {
                        if (!NavMesh.SamplePosition(new Vector3(x, y, z), out var nh, step * 0.5f, NavMesh.AllAreas) || Mathf.Abs(nh.position.y - y) > 1f) { line.Append('#'); continue; }
                        bool ok = NavMesh.CalculatePath(home.position, nh.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                        line.Append(ok ? 'R' : 'o');
                    }
                    sb.AppendLine(line.ToString());
                }
            }
            // Down the keep's west lane to the south-west landing, every 10 cm: the navmesh height found, and whether a
            // path from the lane's middle gets there.
            {
                var mid = new Vector3(312.3f, 28.3f, 368f);
                NavMesh.SamplePosition(mid, out var mh, 1f, NavMesh.AllAreas);
                var pth = new NavMeshPath();
                for (float z = 366f; z >= 361.4f; z -= 0.1f)
                {
                    float expect = z > 363.4f ? 26.2f + (372.6f - z) / 9.2f * 3.905f : 30.1f;
                    bool f = NavMesh.SamplePosition(new Vector3(312.3f, expect, z), out var nh, 0.15f, NavMesh.AllAreas);
                    string st = "-";
                    if (f) { NavMesh.CalculatePath(mh.position, nh.position, NavMesh.AllAreas, pth); st = pth.status.ToString(); }
                    sb.AppendLine($"  z {z:0.00} expect {expect:0.00} navmesh {(f ? nh.position.y.ToString("0.00") + " at z " + nh.position.z.ToString("0.00") : "none")} {st}");
                }
                foreach (var h in Physics.RaycastAll(new Vector3(312.3f, 60f, 363.3f), Vector3.down, 60f).OrderBy(h => -h.point.y))
                    sb.AppendLine($"  above the junction: {h.collider.name} y={h.point.y:0.00}");
                foreach (var h in Physics.RaycastAll(new Vector3(312.3f, 31f, 368f), Vector3.up, 30f).OrderBy(h => h.point.y))
                    sb.AppendLine($"  over the lane: {h.collider.name} y={h.point.y:0.00}");
            }
            System.IO.File.WriteAllText("Build/TownProbePoints.txt", sb.ToString());
            Debug.Log("[FPSKit] probe points written");
            FPSKitBatch.Exit(0);
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

        static float PathLength(NavMeshPath p)
        {
            float l = 0f;
            for (int i = 1; i < p.corners.Length; i++) l += Vector3.Distance(p.corners[i - 1], p.corners[i]);
            return l;
        }
    }
}
#endif
