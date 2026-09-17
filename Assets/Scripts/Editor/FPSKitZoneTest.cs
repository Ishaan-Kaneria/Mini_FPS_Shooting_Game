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

            // ---- falling in has to mean something ----
            var kill = UnityEngine.Object.FindAnyObjectByType<KillVolume>();

            if (kill == null)
            {
                problems.Add($"{name}: nothing in the gorge kills anything, so the drop is a shortcut");
            }
            else
            {
                var box = kill.GetComponent<Collider>();

                if (box == null || !box.isTrigger)
                    problems.Add($"{name}: the kill volume's collider is missing or is not a trigger");
                else if (!box.bounds.Contains(new Vector3(GorgeCentre(theme, 0f), waterY, 0f)))
                    problems.Add($"{name}: the kill volume does not cover the water at z=0 " +
                                 $"(it spans {box.bounds})");
            }

            notes.Append($"\n  {name}: {onMesh}/{total} spawn points on the navmesh");
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

            var from = new Vector3(centre + fromOffset, 1f, z);
            var to = new Vector3(centre + toOffset, 1f, z);

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
                problems.Add($"{name}: no complete path {label} ({path.status}), so the far bank " +
                             "cannot be reached and half the level is unreachable");
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
