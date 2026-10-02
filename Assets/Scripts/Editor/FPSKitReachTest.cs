#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Checks that the navmesh an arena bakes is navmesh an enemy can actually use:
    /// that almost all of it is joined to where the player stands, and that the places
    /// the level fights in are among them.
    ///
    /// <b>This is the check the kit was missing, and it was missing the one failure that
    /// cannot be seen.</b> Every other test here asks whether something exists -- the
    /// banks are joined, the water is lethal, the buttons are clickable. Nothing asked
    /// whether the ground an enemy is put on leads anywhere. A NavMeshSurface bakes
    /// wherever an agent fits and never asks whether one could arrive: the floor inside
    /// a sealed shed, the roof of a container, the cap of a bund, the four metres of
    /// yard outside the fence and the deck of a walkway whose stair faces the wrong way
    /// all bake perfectly good walkable ground, joined to nothing.
    ///
    /// <para>
    /// What that costs is a level that quietly does not end. <c>LevelManager</c> places
    /// a spawn by sampling the navmesh near the player, the sample lands on an island,
    /// and the enemy stands in a room for the rest of the round -- no error, no log, no
    /// movement. The player hunts an arena that sounds occupied and is not, the clock
    /// runs out, and they are scored against kills that were never available. When this
    /// test was first written, <b>two thirds of the industrial site could not reach the
    /// player</b>, and every build check in the repo was green.
    /// </para>
    ///
    /// <para>
    /// It is run against the built scene rather than against the generator, because the
    /// bake is the only place the answer exists, and it asks for a <i>complete</i> path
    /// rather than for a sample: <see cref="NavMesh.SamplePosition"/> answers "is there
    /// walkable ground near here", which every one of those islands says yes to.
    /// </para>
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyReach
    /// </summary>
    public static class FPSKitReachTest
    {
        /// <summary>Metres between samples across the arena.</summary>
        const float Step = 6f;

        /// <summary>
        /// The share of sampled navmesh allowed to be unreachable.
        ///
        /// Not zero, and deliberately. A hollow between three boulders, a dead end behind
        /// a chimney and the inside of a ring of crates are all real places that a real
        /// arena has, they are all a few square metres, and none of them is a bug -- the
        /// spawner refuses them at runtime because it asks the same question this does.
        /// What the threshold is for is the other kind: a whole district, a building, or
        /// everything outside a fence, which is percent rather than fractions of one.
        /// </summary>
        const float Tolerance = 0.02f;

        public static void VerifyReach()
        {
            var problems = new List<string>();
            var notes = new StringBuilder();

            try
            {
                foreach (var name in FPSKitThemes.Names)
                {
                    string path = $"Assets/FPSKit_Generated/Scenes/{name.Replace(" ", "")}.unity";

                    if (!System.IO.File.Exists(path))
                    {
                        problems.Add($"{name}: {path} does not exist, so it has never been built");
                        continue;
                    }

                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    var theme = FPSKitThemes.GetOrCreate(name);
                    Check(theme, name, problems, notes);
                    if (theme.parkZone && !FPSKitSceneBuilder.RealisticFairgroundEnabled) CheckFairgroundDecks(name, problems, notes);
                }

                if (problems.Count > 0)
                {
                    var report = new StringBuilder();
                    foreach (var problem in problems) report.Append($"\n  - {problem}");

                    Debug.LogError($"[FPSKitBatch] FAILED: enemies would be stranded:{report}{notes}");
                    FPSKitBatch.Exit(1);
                    return;
                }

                Debug.Log($"[FPSKitBatch] verify reach passed.{notes}");
                FPSKitBatch.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: {e}{notes}");
                FPSKitBatch.Exit(1);
            }
        }

        // ==================================================================
        /// <summary>
        /// The fairground's raised, walkable surfaces: the hall gallery and stage, the coaster
        /// station, the carousel and bandstand floors, the balcony of the haunted house, the
        /// big top's seating, the footbridge and the container roofs.
        ///
        /// <b>The ground-level probe above cannot see any of them.</b> It samples the navmesh
        /// from 1.5m up with a three-metre reach, so a gallery six metres up is not in its set
        /// and a gallery with no stair to it passes. This asks the question directly: for each
        /// deck, is there navmesh on its top, and can it path to the player? A stair that stops
        /// short, a landing a centimetre low or a deck wrongly held off the bake each fail here.
        /// </summary>
        static void CheckFairgroundDecks(string name, List<string> problems, StringBuilder notes)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null || !NavMesh.SamplePosition(player.transform.position, out var home, 20f, NavMesh.AllAreas)) return;

            var path = new NavMeshPath();
            var decks = new[]
            {
                "GalleryS_West", "GalleryS_Mid", "GalleryS_Raw", "GalleryN", "GalleryN_Raw", "GalleryW", "Stage",
                "StationDeck", "CarouselDeck", "BandstandFloor", "Balcony", "BridgeDeck", "ContainerRoof"
            };

            int checkedCount = 0;
            var seen = new HashSet<string>();

            foreach (var collider in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (System.Array.IndexOf(decks, collider.name) < 0) continue;

                var b = collider.bounds;

                // Down onto the collider itself from above its middle, so the height is the deck's and
                // not that of a rail or a canopy standing on it; and off-centre where the middle is a
                // drum or a pole.
                var at = new Vector3(b.center.x, b.max.y + 2f, b.center.z);
                if (collider.name == "CarouselDeck") at.x += 7.45f;
                if (!collider.Raycast(new Ray(at, Vector3.down), out var ray, 8f)) continue;

                var top = ray.point + Vector3.up * 0.05f;
                seen.Add(collider.name);
                checkedCount++;

                if (!NavMesh.SamplePosition(top, out var hit, 0.9f, NavMesh.AllAreas) || Mathf.Abs(hit.position.y - ray.point.y) > 0.4f)
                {
                    problems.Add($"{name}: \"{collider.name}\" at {b.center} has no navmesh on its top surface (y {ray.point.y:0.00}), " +
                                 "so nothing can stand on it -- it was held off the bake or is too steep");
                    continue;
                }

                if (!NavMesh.CalculatePath(hit.position, home.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    problems.Add($"{name}: \"{collider.name}\" at {b.center} is an island -- its navmesh cannot path to the player. " +
                                 "Its stair stops short of the deck, lands off its height, or is not joined to the ground");
            }

            // The tent's raked seating is one mesh whose bounds centre is the ring on the floor, so
            // it is sampled at the middle of its fourth tier in all four blocks instead.
            var tent = GameObject.Find("Arena/Rides/BigTop");
            if (tent != null)
                for (int block = 0; block < 4; block++)
                {
                    float a = (45f + 90f * block) * Mathf.Deg2Rad;
                    var p = tent.transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 16.6f + Vector3.up * 1.25f;
                    checkedCount++;

                    if (!NavMesh.SamplePosition(p, out var hit, 0.8f, NavMesh.AllAreas))
                        problems.Add($"{name}: block {block} of the big top's seating has no navmesh on its fourth tier at {p}");
                    else if (!NavMesh.CalculatePath(hit.position, home.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                        problems.Add($"{name}: block {block} of the big top's seating is an island at {p}");
                }

            notes.Append($"\n  {name}: {checkedCount} raised deck(s) checked ({seen.Count} kinds), all must be baked and reachable");
        }

        // ==================================================================
        static void Check(LevelTheme theme, string name, List<string> problems, StringBuilder notes)
        {
            var player = GameObject.FindGameObjectWithTag("Player");

            if (player == null)
            {
                problems.Add($"{name}: no Player-tagged object, so there is nothing to path to");
                return;
            }

            // <b>Standing on something, before anything else.</b>
            //
            // This is not a navigation question and it is here because nothing else asks
            // it. The builder places the player by asking the terrain how high the ground
            // is, and the terrain is a static that only the two arenas which build one
            // ever clear -- so a walled arena built after the desert in the same editor
            // session took the desert's spawn height and put the player twelve metres
            // under its own floor. Four of the six arenas shipped that way. The scene
            // builds, the navmesh bakes, and the check below still passed, because
            // thirteen metres is inside its twenty-metre tolerance.
            if (!Physics.Raycast(player.transform.position + Vector3.up * 0.5f, Vector3.down,
                                 out var floor, 3.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                problems.Add($"{name}: the player starts at {player.transform.position} with nothing " +
                             "under them within 3.5 m -- they are not standing on the arena, they are " +
                             "under it or above it, and the first thing the level does is drop them");
                return;
            }

            if (!NavMesh.SamplePosition(player.transform.position, out var home, 20f, NavMesh.AllAreas))
            {
                problems.Add($"{name}: the player starts more than 20 m from any navmesh, so the " +
                             "level has nowhere to spawn anything at all");
                return;
            }

            float half = theme.arenaSize * 0.5f;
            var path = new NavMeshPath();

            int on = 0, stranded = 0;
            var worst = Vector3.zero;
            var blamed = new Dictionary<string, int>();
            var where = new List<Vector3>();

            // <b>Where the stranded ground is, not just one sample of it.</b> One coordinate
            // says almost nothing: a thin ring round the boundary and a whole quadrant cut
            // off from the rest print the same single point, and the two want completely
            // different fixes. The extent and the centre tell them apart at a glance, and
            // the reachable extent beside them says whether the *player* is the island.
            var badMin = new Vector2(float.MaxValue, float.MaxValue);
            var badMax = new Vector2(float.MinValue, float.MinValue);
            var badSum = Vector2.zero;

            var okMin = new Vector2(float.MaxValue, float.MaxValue);
            var okMax = new Vector2(float.MinValue, float.MinValue);

            for (float x = -half; x <= half; x += Step)
            for (float z = -half; z <= half; z += Step)
            {
                // Sampled from just above the ground, not from the sky: the search radius
                // is measured from the point given, so a probe dropped from sixty metres
                // up finds nothing anywhere and the whole test passes on an empty set.
                if (!NavMesh.SamplePosition(new Vector3(x, 1.5f, z), out var hit, 3f, NavMesh.AllAreas))
                    continue;

                on++;

                if (NavMesh.CalculatePath(hit.position, home.position, NavMesh.AllAreas, path)
                    && path.status == NavMeshPathStatus.PathComplete)
                {
                    okMin = Vector2.Min(okMin, new Vector2(hit.position.x, hit.position.z));
                    okMax = Vector2.Max(okMax, new Vector2(hit.position.x, hit.position.z));
                    continue;
                }

                stranded++;
                worst = hit.position;
                where.Add(hit.position);

                var flat = new Vector2(hit.position.x, hit.position.z);
                badMin = Vector2.Min(badMin, flat);
                badMax = Vector2.Max(badMax, flat);
                badSum += flat;

                // What it is standing on, so a failure names the thing to fix rather than
                // a coordinate.
                if (Physics.Raycast(hit.position + Vector3.up * 0.6f, Vector3.down, out var under,
                                    4f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var t = under.collider.transform;
                    string what = t.parent != null ? $"{t.parent.name}/{t.name}" : t.name;
                    blamed.TryGetValue(what, out int n);
                    blamed[what] = n + 1;
                }
            }

            if (on == 0)
            {
                problems.Add($"{name}: no navmesh was found anywhere in the arena, so nothing can " +
                             "move at all");
                return;
            }

            float share = stranded / (float)on;

            if (share > Tolerance)
            {
                var top = new StringBuilder();
                foreach (var pair in blamed)
                    if (pair.Value > 2) top.Append($" {pair.Key} x{pair.Value};");

                var centre = badSum / Mathf.Max(1, stranded);

                problems.Add($"{name}: {stranded} of {on} sampled patches of navmesh " +
                             $"({share:P1}) cannot reach the player -- an enemy placed on one " +
                             $"stands there until the clock ends the level. Mostly on:{top} " +
                             $"(one at {worst})" +
                             $"\n      stranded spans x {badMin.x:0} to {badMax.x:0}, " +
                             $"z {badMin.y:0} to {badMax.y:0}, centred ({centre.x:0}, {centre.y:0})" +
                             $"\n      reachable spans x {okMin.x:0} to {okMax.x:0}, " +
                             $"z {okMin.y:0} to {okMax.y:0}" +
                             $"\n      player at ({home.position.x:0}, {home.position.z:0})");
            }
            else
            {
                var blame = new StringBuilder();
                foreach (var pair in blamed) blame.Append($" {pair.Key} x{pair.Value};");

                notes.Append($"\n  {name}: {on} navmesh samples, {stranded} stranded ({share:P1}), " +
                             $"player standing on \"{floor.collider.name}\"" +
                             (stranded > 0 ? $"; stranded on:{blame}" : ""));

                // A handful of strays are listed with their coordinates, so each can be found and read.
                if (stranded > 0 && stranded <= 40)
                    foreach (var p in where) notes.Append($"\n      stray at ({p.x:0}, {p.y:0.0}, {p.z:0})");
            }
        }
    }
}
#endif
