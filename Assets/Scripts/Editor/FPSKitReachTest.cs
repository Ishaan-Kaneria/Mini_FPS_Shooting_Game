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
                    Check(FPSKitThemes.GetOrCreate(name), name, problems, notes);
                }

                if (problems.Count > 0)
                {
                    var report = new StringBuilder();
                    foreach (var problem in problems) report.Append($"\n  - {problem}");

                    Debug.LogError($"[FPSKitBatch] FAILED: enemies would be stranded:{report}{notes}");
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log($"[FPSKitBatch] verify reach passed.{notes}");
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
            var player = GameObject.FindGameObjectWithTag("Player");

            if (player == null)
            {
                problems.Add($"{name}: no Player-tagged object, so there is nothing to path to");
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
                    && path.status == NavMeshPathStatus.PathComplete) continue;

                stranded++;
                worst = hit.position;

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

                problems.Add($"{name}: {stranded} of {on} sampled patches of navmesh " +
                             $"({share:P1}) cannot reach the player -- an enemy placed on one " +
                             $"stands there until the clock ends the level. Mostly on:{top} " +
                             $"(one at {worst})");
            }
            else
            {
                notes.Append($"\n  {name}: {on} navmesh samples, {stranded} stranded ({share:P1})");
            }
        }
    }
}
#endif
