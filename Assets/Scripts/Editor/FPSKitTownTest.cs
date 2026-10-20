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
                { problems.Add($"{name} at {hit.position} cannot be reached from the start (path {path.status})"); continue; }
                reached++;
                notes.Append($"\n  {name}: reached ({PathLength(path):0} m)");
            }
            notes.Append($"\n  {reached}/{places.Count} famous locations reachable");

            // ---- closed roofs stay off the navmesh ----
            int roofs = 0, bad = 0;
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
                        bad++;
                }
            }
            if (roofs > 0 && bad > roofs * 0.03f) problems.Add($"{bad} of {roofs} sampled roof points carry navmesh that cannot be reached (a closed building baked an island)");
            notes.Append($"\n  roofs: {roofs} points sampled, {bad} unreachable islands");

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
