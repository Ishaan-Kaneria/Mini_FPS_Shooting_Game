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
            // A broken roof stair, step by step: where the wall is, the tread, the navmesh on it, and the volumes there.
            {
                if (Physics.Raycast(new Vector3(-257.8f, 7.2f, -327.3f), Vector3.back, out var wallHit, 20f))
                    sb.AppendLine($"== stair wall: {wallHit.collider.name} at z {wallHit.point.z:0.00}");
                float wz = wallHit.point.z;
                for (float x = -262f; x <= -253f; x += 0.25f)
                {
                    var line = new StringBuilder($"  x {x:0.00}:");
                    foreach (float off in new[] { 0.3f, 0.6f, 0.82f, 1.05f, 1.3f })
                    {
                        float z = wz + off + 0.02f;
                        Physics.Raycast(new Vector3(x, 14f, z), Vector3.down, out var h, 20f);
                        bool nav = NavMesh.SamplePosition(h.point, out var nh, 0.1f, NavMesh.AllAreas);
                        line.Append($" [{off:0.00}] {h.point.y:0.00}{(nav ? "*" : " ")}");
                    }
                    sb.AppendLine(line.ToString());
                }
                foreach (var v in vols)
                {
                    var c = v.transform.TransformPoint(v.center); var e = v.size * 0.5f;
                    if (c.x + e.x > -262f && c.x - e.x < -253f && c.z + e.z > wz - 0.5f && c.z - e.z < wz + 2f)
                        sb.AppendLine($"  volume y {c.y - e.y:0.0}..{c.y + e.y:0.0} x {c.x - e.x:0.0}..{c.x + e.x:0.0} z {c.z - e.z:0.00}..{c.z + e.z:0.00}");
                }
            }
            System.IO.File.WriteAllText("Build/TownProbePoints.txt", sb.ToString());
            Debug.Log("[FPSKit] probe points written");
            FPSKitBatch.Exit(0);
        }

        /// <summary>
        /// Points proved reachable from the start, and the proof of a new one: a complete path from one of the three proved
        /// points nearest it. Legs stay short, inside the search's budget.
        /// </summary>
        sealed class Reach
        {
            readonly List<Vector3> _proved = new List<Vector3>();
            readonly NavMeshPath _path = new NavMeshPath();
            public Reach(Vector3 start) { _proved.Add(start); }

            IEnumerable<Vector3> Nearest(Vector3 t) => _proved.OrderBy(p => (p - t).sqrMagnitude).Take(3);

            public bool Prove(Vector3 t, bool remember = true)
            {
                foreach (var p in Nearest(t))
                    if (NavMesh.CalculatePath(p, t, NavMesh.AllAreas, _path) && _path.status == NavMeshPathStatus.PathComplete)
                    {
                        if (remember) _proved.Add(t);
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
