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
    /// Checks that an open-zone arena is a place an agent can actually move around: the
    /// gorge blocks, the bridges join, the spawn points are on the ground and the water
    /// is lethal.
    ///
    /// Every one of these fails silently in the game. A gorge that does not block is a
    /// river enemies walk across, which looks like the level is broken and reads, in
    /// every log, as nothing at all. Banks that are not joined is worse and quieter: the
    /// far half of the map becomes unreachable, the leash discards whatever spawns over
    /// there, the level replaces it, and the player fights a smaller level than the one
    /// they were given, scored against the one they were not.
    ///
    /// The strongest assertion here is the ratio. A path straight across the gorge and a
    /// path that detours to a bridge both come back PathComplete, so "can it get there"
    /// proves nothing on its own -- the gorge is only really an obstacle if going around
    /// it costs something, and the cost is the whole reason the bridge is worth holding.
    ///
    /// Edit mode only: the NavMesh is baked into the scene, so none of this needs play
    /// mode and the whole check takes seconds.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyZone
    /// </summary>
    public static class FPSKitZoneTest
    {
        /// <summary>How much longer a crossing away from a bridge has to be, at least.</summary>
        const float DetourRatio = 1.4f;

        public static void VerifyZone()
        {
            var problems = new List<string>();
            var notes = new StringBuilder();
            int zones = 0;

            try
            {
                foreach (var name in FPSKitThemes.Names)
                {
                    var theme = FPSKitThemes.GetOrCreate(name);
                    if (theme == null || !theme.openZone) continue;

                    string path = $"Assets/FPSKit_Generated/Scenes/{name.Replace(" ", "")}.unity";

                    if (!System.IO.File.Exists(path))
                    {
                        problems.Add($"{name}: {path} does not exist, so the open zone has never been built");
                        continue;
                    }

                    zones++;
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    Check(theme, name, problems, notes);
                }

                if (zones == 0)
                    notes.Append("\n  no theme has openZone set, so there was nothing to check");

                if (problems.Count > 0)
                {
                    var report = new StringBuilder();
                    foreach (var problem in problems) report.Append($"\n  - {problem}");

                    Debug.LogError($"[FPSKitBatch] FAILED: the open zone does not hold together:{report}{notes}");
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log($"[FPSKitBatch] verify zone passed: {zones} open zone(s) checked.{notes}");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: {e}{notes}");
                EditorApplication.Exit(1);
            }
        }

        // ==================================================================
        static void Check(LevelTheme theme, string name, List<string> problems, StringBuilder notes)
        {
            float half = theme.arenaSize * 0.5f;
            float edge = theme.hazardWidth * 0.5f;

            // ---- the player does not start in the water ----
            var player = GameObject.FindGameObjectWithTag("Player");

            if (player == null)
            {
                problems.Add($"{name}: no Player in the scene");
                return;
            }

            float startOffset = Mathf.Abs(player.transform.position.x -
                                          GorgeCentre(theme, player.transform.position.z));

            if (startOffset < edge + 4f)
                problems.Add($"{name}: the player starts {startOffset:0} m from the middle of a " +
                             $"{theme.hazardWidth:0} m gorge, which is in it");

            // ---- every spawn point is somewhere an enemy can stand ----
            var spawns = GameObject.Find("SpawnPoints");
            int onMesh = 0, total = 0;

            if (spawns != null)
            {
                foreach (Transform point in spawns.transform)
                {
                    total++;
                    if (NavMesh.SamplePosition(point.position, out _, 6f, NavMesh.AllAreas)) onMesh++;
                }
            }

            if (total == 0) problems.Add($"{name}: no spawn points");
            else if (onMesh < total)
                problems.Add($"{name}: {total - onMesh} of {total} spawn points are off the navmesh, " +
                             "so the level has fewer directions to arrive from than it was built with");

            // ---- the water is not walkable ----
            float waterY = -theme.hazardDepth + 2f;
            var wet = new Vector3(GorgeCentre(theme, 0f), waterY, 0f);

            if (NavMesh.SamplePosition(wet, out var found, 4f, NavMesh.AllAreas) &&
                Mathf.Abs(found.position.y - waterY) < 4f)
                problems.Add($"{name}: the navmesh reaches the water at {found.position}, so enemies " +
                             "will walk into the river instead of round to a bridge");

            // ---- the crossing is real, and so is the detour ----
            float far = half * 0.42f;

            float atBridge = Path(theme, name, "at a bridge", -far, far, half * 0.62f, problems, notes);
            float away = Path(theme, name, "away from one", -far, far, 0f, problems, notes);

            if (atBridge > 0f && away > 0f)
            {
                float ratio = away / atBridge;

                if (ratio < DetourRatio)
                    problems.Add($"{name}: crossing away from a bridge costs only {ratio:0.00}x the " +
                                 $"crossing at one, so the gorge is not an obstacle -- either the " +
                                 "water is walkable or there are too many crossings");
                else
                    notes.Append($"\n  {name}: going round costs {ratio:0.00}x going over");
            }

            // ---- falling in has to mean something, and only falling in ----
            CheckKillVolume(theme, name, waterY, problems, notes);

            // ---- and nothing standing on the sand may be hollow underneath ----
            CheckRocksAreBuried(name, problems, notes);

            notes.Append($"\n  {name}: {onMesh}/{total} spawn points on the navmesh");
        }

        /// <summary>The names of the things in an open zone that are cut off flat underneath.</summary>
        static readonly string[] RockNames = { "Rock", "Butte", "Outcrop", "RockSpine", "Rubble" };

        /// <summary>
        /// Every rock has to be buried, and the reason is that every rock is hollow.
        ///
        /// <b>This is the worst-feeling bug this arena has had.</b> A boulder and a butte
        /// are both closed shells cut off flat underneath, which is exactly right on a
        /// plane -- put the cut a little below the ground and it is buried all the way
        /// round. On a dune field the ground under a forty-metre butte varies by ten
        /// metres, and the builder was pinning the cut to the height at the rock's
        /// <i>centre</i>. Downhill of that, the floor of the shell stands clear of the
        /// sand.
        ///
        /// What that leaves is a doorway into the inside of a closed mesh, and the inside
        /// of a closed mesh is not drawn at all -- every face of it is a backface. So the
        /// player walks in through a gap they can see, the view fills with rock at angles
        /// that correspond to nothing, and they are wedged inside geometry that is not
        /// rendered and cannot be climbed. Ishaan's report was "domes are half opened and
        /// when I go in it I get trapped instantly", which is an exact description of it
        /// and matches nothing in any log.
        ///
        /// <para>
        /// Measured against <c>bounds.min.y</c> -- the flat cut itself -- and not by
        /// raycasting upwards from the sand, which was the first attempt and measures the
        /// wrong thing: a ray up from inside a properly buried butte leaves through the
        /// underside of a weathering ledge seven metres above, and reports seven metres
        /// of air under a rock that is buried six metres deep.
        /// </para>
        /// </summary>
        static void CheckRocksAreBuried(string name, List<string> problems, StringBuilder notes)
        {
            /// <summary>Metres of the flat cut allowed to stand clear of the sand.</summary>
            const float Hole = 0.5f;

            int rocks = 0, hollow = 0;
            float worst = float.NegativeInfinity;
            var worstAt = Vector3.zero;
            string worstName = null;

            foreach (var collider in UnityEngine.Object.FindObjectsByType<MeshCollider>(
                         FindObjectsSortMode.None))
            {
                bool interesting = false;
                foreach (var n in RockNames) if (collider.name == n) { interesting = true; break; }
                if (!interesting) continue;

                var bounds = collider.bounds;
                rocks++;
                bool open = false;

                for (int s = 0; s < 16 && !open; s++)
                {
                    float angle = s * Mathf.PI * 2f / 16f;

                    // Well inside the rim: a rock lying on a slope legitimately undercuts
                    // at its very edge, and that is not a way in.
                    float x = bounds.center.x + Mathf.Cos(angle) * bounds.extents.x * 0.55f;
                    float z = bounds.center.z + Mathf.Sin(angle) * bounds.extents.z * 0.55f;

                    float sand = float.NegativeInfinity;

                    foreach (var hit in Physics.RaycastAll(new Vector3(x, 400f, z), Vector3.down,
                                                           900f, ~0, QueryTriggerInteraction.Ignore))
                        if (hit.collider != null && hit.collider.CompareTag("Sand") &&
                            hit.point.y > sand)
                            sand = hit.point.y;

                    if (float.IsInfinity(sand)) continue;

                    float gap = bounds.min.y - sand;

                    if (gap > worst)
                    {
                        worst = gap;
                        worstAt = new Vector3(x, sand, z);
                        worstName = $"{collider.name}\" centred at {bounds.center} size {bounds.size}, " +
                                    $"its flat cut at {bounds.min.y:0.0} over sand at {sand:0.0}";
                    }

                    if (gap > Hole) { hollow++; open = true; }
                }
            }

            if (rocks == 0) return;

            if (hollow > 0)
                problems.Add($"{name}: {hollow} of {rocks} rocks are cut off above the sand somewhere " +
                             $"under them (worst: a \"{worstName}). Every one of these is hollow, so " +
                             "that gap is a doorway into a shell with no visible walls and no way back " +
                             $"out. First one at {worstAt}");
            else
                notes.Append($"\n  {name}: {rocks} rocks, every flat cut buried " +
                             $"(the shallowest by {-worst:0.0} m)");
        }

        /// <summary>
        /// The river has to be lethal everywhere along its length, and lethal nowhere
        /// else.
        ///
        /// Both halves are load-bearing and the second half is the one that was wrong.
        /// The trigger used to be a single axis-aligned box spanning the river plus its
        /// whole meander, with its lid at a fixed depth below zero -- which on a dune
        /// field, whose basins go thirteen metres under datum, put four thousand square
        /// metres of ordinary walkable sand inside an instant-kill volume. Nothing about
        /// that is visible: there is no water there, no edge and no fall, and the player
        /// simply dies on open ground on their way to the river.
        ///
        /// So this raycasts the built arena and fails if anything a player could stand
        /// on is inside the trigger. Measured against the collider rather than against
        /// the generator's arithmetic, because the question is about the volume the game
        /// actually runs, and a repeat of the maths would repeat the mistake with it.
        /// </summary>
        static void CheckKillVolume(LevelTheme theme, string name, float waterY,
                                    List<string> problems, StringBuilder notes)
        {
            var kill = UnityEngine.Object.FindAnyObjectByType<KillVolume>();

            if (kill == null)
            {
                problems.Add($"{name}: nothing in the gorge kills anything, so the drop is a shortcut");
                return;
            }

            var boxes = kill.GetComponents<Collider>();

            if (boxes.Length == 0)
            {
                problems.Add($"{name}: the kill volume has no collider at all");
                return;
            }

            foreach (var box in boxes)
                if (!box.isTrigger)
                {
                    problems.Add($"{name}: one of the kill volume's {boxes.Length} colliders is " +
                                 "solid rather than a trigger, so it is a wall in the river");
                    break;
                }

            // ---- lethal all the way along ----
            float half = theme.arenaSize * 0.5f;
            int uncovered = 0;
            float firstGap = 0f;

            for (float z = -half; z <= half; z += 12f)
            {
                var at = new Vector3(GorgeCentre(theme, z), waterY, z);

                bool covered = false;
                foreach (var box in boxes) if (box.bounds.Contains(at)) { covered = true; break; }

                if (covered) continue;
                if (uncovered == 0) firstGap = z;
                uncovered++;
            }

            if (uncovered > 0)
                problems.Add($"{name}: the water is not lethal at {uncovered} latitudes -- the first " +
                             $"at z={firstGap:0} -- so the river can be waded at those points");

            // ---- and lethal nowhere anybody is meant to be ----
            //
            // "Meant to be" is the navmesh, plus the sand. The floor of the canyon is
            // ground too and it is inside the trigger on purpose -- that is what the
            // trigger is for -- so the question cannot be "is there anything under this
            // ray". It is whether the level itself says somebody may stand here: the bed,
            // the talus and the bench are all held off the bake by NoStanding for
            // exactly this reason, and the sand is checked as well because a patch of
            // dune nobody happened to bake is still somewhere a player will walk.
            int lethalGround = 0;
            var worst = Vector3.zero;
            float deepest = 0f;

            for (float x = -half; x <= half; x += 4f)
            for (float z = -half; z <= half; z += 4f)
            {
                if (!Physics.Raycast(new Vector3(x, 400f, z), Vector3.down, out var hit, 900f,
                                     ~0, QueryTriggerInteraction.Ignore)) continue;

                bool standable = hit.collider != null && hit.collider.CompareTag("Sand");

                if (!standable)
                    standable = NavMesh.SamplePosition(hit.point, out var onMesh, 1.5f, NavMesh.AllAreas)
                             && Mathf.Abs(onMesh.position.y - hit.point.y) < 1.5f;

                if (!standable) continue;

                // Where a player standing here would have their chest. Feet alone would
                // miss somebody wading in a trigger whose lid is above their knees.
                var chest = hit.point + Vector3.up * 0.9f;

                foreach (var box in boxes)
                {
                    if (!box.bounds.Contains(chest)) continue;

                    lethalGround++;

                    float inside = box.bounds.max.y - hit.point.y;
                    if (inside > deepest) { deepest = inside; worst = hit.point; }
                    break;
                }
            }

            if (lethalGround > 0)
                problems.Add($"{name}: {lethalGround} sampled patches of standable ground are inside " +
                             $"the kill volume (worst {deepest:0.0} m under its lid, at {worst}), so " +
                             "the level kills the player on ground that looks like anywhere else");
            else
                notes.Append($"\n  {name}: kill volume is {boxes.Length} slices, and no standable " +
                             "ground is inside any of them");
        }

        /// <summary>
        /// Bank to bank at one latitude. Returns the path length, or -1 when it failed --
        /// which is itself recorded as a problem, because an unreachable far bank is the
        /// worst of the failures this is looking for.
        /// </summary>
        static float Path(LevelTheme theme, string name, string label, float fromOffset,
                          float toOffset, float z, List<string> problems, StringBuilder notes)
        {
            float centre = GorgeCentre(theme, z);
            float half = theme.arenaSize * 0.5f;

            // Kept inside the arena, because the offsets are measured from the river and
            // the river is not down the middle of the map.
            //
            // With hazardOffset at 92 and the meander swinging another 30 east, the east
            // bank is barely eighty metres wide at some latitudes -- so a probe placed a
            // fixed ninety-four metres out from the water lands past the boundary ridge
            // and inside the rock. What that reports is "the far bank cannot be reached
            // and half the level is unreachable", which is the loudest thing this file
            // can say and, there, entirely an artefact of where the probe was put. The
            // margin clears the ridge, whose blocks reach about eighteen metres inboard
            // of the arena edge.
            float Bank(float offset)
                => Mathf.Clamp(centre + offset, -half * 0.82f, half * 0.82f);

            var from = OnGround(new Vector3(Bank(fromOffset), 1f, z));
            var to = OnGround(new Vector3(Bank(toOffset), 1f, z));

            if (!NavMesh.SamplePosition(from, out var a, 14f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(to, out var b, 14f, NavMesh.AllAreas))
            {
                problems.Add($"{name}: one end of the \"{label}\" crossing is not on the navmesh at all");
                return -1f;
            }

            var path = new NavMeshPath();
            NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path);

            if (path.status != NavMeshPathStatus.PathComplete)
            {
                // Where it gave up, not just that it did. A partial path is the navmesh
                // saying "this far and no further", and the corner it stops at is the
                // obstacle -- without it the only way to find out which of four hundred
                // generated objects is in the way is to guess.
                var stopped = path.corners.Length > 0
                    ? path.corners[path.corners.Length - 1].ToString()
                    : "nowhere at all";

                problems.Add($"{name}: no complete path {label} ({path.status}): from {a.position} " +
                             $"({Standing(a.position)}) towards {b.position} ({Standing(b.position)}), " +
                             $"the route runs out at {stopped} after {path.corners.Length} corners " +
                             "-- so the far bank cannot be reached and half the level is unreachable");
                return -1f;
            }

            float length = 0f;
            for (int i = 1; i < path.corners.Length; i++)
                length += Vector3.Distance(path.corners[i - 1], path.corners[i]);

            notes.Append($"\n  {name}: {label} -- {length:0} m walked for " +
                         $"{Vector3.Distance(a.position, b.position):0} m of gap");

            return length;
        }

        /// <summary>
        /// Drops a probe point onto whatever is under it.
        ///
        /// These probes used to be taken at a flat <c>y = 1</c>, which is correct in an
        /// arena whose ground is a floor and wrong the moment it is a heightfield:
        /// <c>NavMesh.SamplePosition</c> searches a radius in three dimensions, and a
        /// dune field with thirty metres of relief in it puts the sand seventeen metres
        /// below the probe in the basins. The search then fails not because the bank is
        /// unreachable but because the test was looking for it in the sky -- which is
        /// reported as the far half of the level being cut off, and is the most alarming
        /// message this file can produce.
        ///
        /// Falls back to the point it was given when nothing is under it, so a genuinely
        /// missing bank still fails.
        /// </summary>
        /// <summary>
        /// Names whatever is standing over a point, root object first.
        ///
        /// A partial path says a point is walled in and says nothing about what by, and
        /// an open-zone arena has several hundred generated objects in it. This turns
        /// four hundred candidates into one name.
        /// </summary>
        static string Standing(Vector3 at)
        {
            var hits = Physics.RaycastAll(new Vector3(at.x, 400f, at.z), Vector3.down, 800f);
            if (hits.Length == 0) return "nothing above it";

            var names = new List<string>();

            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;

                var root = hit.collider.transform.root;
                string label = root == hit.collider.transform
                    ? hit.collider.name
                    : $"{root.name}/{hit.collider.name}";

                if (!names.Contains(label)) names.Add(label);
            }

            return names.Count == 0 ? "nothing above it" : "under " + string.Join(", ", names);
        }

        static Vector3 OnGround(Vector3 probe)
        {
            var hits = Physics.RaycastAll(new Vector3(probe.x, 400f, probe.z), Vector3.down, 800f);
            if (hits.Length == 0) return probe;

            // The *lowest* hit, not the first. The first is whatever is standing on the
            // ground here -- the cap of a rock, the deck of a vantage, the top of a
            // compound wall -- and a probe put a metre above that is a probe testing
            // whether you can walk across the map from the summit of a boulder. The
            // bottom of the stack is the sand.
            var lowest = hits[0].point;
            foreach (var hit in hits) if (hit.point.y < lowest.y) lowest = hit.point;

            return lowest + Vector3.up;
        }

        /// <summary>
        /// The gorge's centre line. Kept in step with FPSKitSceneBuilder.GorgeCentreAt by
        /// hand rather than shared, so that a change to the shape of the river has to be
        /// made in two places and the test cannot silently follow the code it is testing.
        /// </summary>
        static float GorgeCentre(LevelTheme theme, float z)
            => theme.hazardOffset
             + Mathf.Sin(z / 90f) * theme.hazardMeander
             + Mathf.Sin(z / 37f + 1.7f) * theme.hazardMeander * 0.35f;
    }
}
#endif
