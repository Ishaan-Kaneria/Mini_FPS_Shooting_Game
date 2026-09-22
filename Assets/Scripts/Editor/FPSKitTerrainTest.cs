#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Measures the ground of a dune arena: that it is there, that it has a shape, and
    /// that the shape is one a player and a vehicle can both cross.
    ///
    /// Terrain is the one thing in this kit where every failure looks like success. A
    /// heightfield that came out flat still builds, still bakes, still passes VerifyZone
    /// and is still a perfectly playable level -- it is just not a desert, and nothing
    /// anywhere says so. A heightfield that came out too steep is worse: the navmesh
    /// bakes with holes in it, enemies spawned beyond them are discarded by the leash on
    /// arrival, and the player fights a smaller level than the one they are scored
    /// against. And a heightfield that came out *jagged* is invisible on foot and
    /// unusable under a wheel.
    ///
    /// So there are three assertions and they pull against each other on purpose:
    ///
    ///   <b>relief</b>  -- the ground has to go up and down by enough to hide a person,
    ///                     or the dunes are decoration;
    ///   <b>gradient</b> -- and never by more than a vehicle can climb, measured at the
    ///                     scale a vehicle feels;
    ///   <b>chatter</b> -- and the change in gradient from one step to the next has to
    ///                     stay small, which is the thing smooth vertex normals hide
    ///                     completely and a collider does not.
    ///
    /// Measured by raycasting the built scene rather than by re-running the generator,
    /// so what is checked is the collider the game actually uses -- including every
    /// structure standing on it, which a repeat of the maths would miss.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyTerrain
    /// </summary>
    public static class FPSKitTerrainTest
    {
        /// <summary>Metres between samples. About a wheelbase: the scale a car feels a bump at.</summary>
        const float Step = 1.5f;

        /// <summary>Lines sampled across the arena, and samples along each.</summary>
        const int Lines = 24;
        const int Samples = 260;

        /// <summary>The ground must rise and fall by at least this much, or there are no dunes.</summary>
        const float MinRelief = 9f;

        /// <summary>Steepest gradient allowed at <see cref="Step"/> scale, as a fraction of the samples.</summary>
        const float MaxSlopeDegrees = 30f;
        const float SlopeTolerance = 0.005f;

        /// <summary>Biggest change in gradient allowed between one step and the next.</summary>
        const float MaxChatterDegrees = 14f;
        const float ChatterTolerance = 0.005f;

        public static void VerifyTerrain()
        {
            var problems = new List<string>();
            var notes = new StringBuilder();

            try
            {
                foreach (var name in FPSKitThemes.Names)
                {
                    var theme = FPSKitThemes.GetOrCreate(name);
                    // Any layout that builds a heightfield, not only the open zone.
                    //
                    // Written as `!theme.openZone` this skipped the park and then skipped
                    // the frozen field -- two arenas whose entire floor is the thing this
                    // check exists to measure, passing because they were never looked at.
                    // A test that quietly examines one arena out of three is worse than
                    // one that fails, because it goes green.
                    if (theme == null || theme.duneHeight <= 0f) continue;
                    if (!theme.openZone && !theme.parkZone && !theme.snowZone) continue;

                    string path = $"Assets/FPSKit_Generated/Scenes/{name.Replace(" ", "")}.unity";

                    if (!System.IO.File.Exists(path))
                    {
                        problems.Add($"{name}: {path} does not exist, so the terrain has never been built");
                        continue;
                    }

                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    Check(theme, name, problems, notes);
                }

                if (problems.Count > 0)
                {
                    var report = new StringBuilder();
                    foreach (var problem in problems) report.Append($"\n  - {problem}");

                    Debug.LogError($"[FPSKitBatch] FAILED: the ground is not what it should be:{report}{notes}");
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log($"[FPSKitBatch] verify terrain passed.{notes}");
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

            // Only a layout that builds a river has a river to step around. Kept at zero
            // elsewhere, or a snow field would have a 55m strip down the middle of it
            // excluded from the sweep for a gorge that was never cut -- the same field
            // BuildDuneField itself had to be told to stop reading.
            float clear = theme.openZone ? theme.hazardWidth * 0.5f + 30f : 0f;

            // <b>The heightfield itself, not everything wearing its tag.</b>
            //
            // Filtering on the tag was right while only the ground carried it. On the
            // frozen field the drifts, the igloos and the windbreaks are all tagged Snow
            // too -- correctly, because that is what they are made of and what they should
            // sound like -- so a tag filter put the side of an igloo into a sweep that is
            // measuring ground, and reported 67-degree slopes and 81-degree gradient
            // changes in an arena whose terrain is relaxed to 26. The same trap the
            // comment below records for surface normals, one layer along: what makes
            // something the ground is not what it is made of, it is that it *is* the
            // ground.
            var chunks = new HashSet<Collider>();

            foreach (var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>(
                         FindObjectsSortMode.None))
            {
                if (filter == null || filter.gameObject.name != "Dune") continue;

                var collider = filter.GetComponent<Collider>();
                if (collider != null) chunks.Add(collider);
            }

            if (chunks.Count == 0)
            {
                problems.Add($"{name}: no terrain chunks found, so nothing was measured");
                return;
            }

            int hits = 0, misses = 0, runs = 0;
            float low = float.MaxValue, high = float.MinValue;

            float worstSlope = 0f, worstChatter = 0f;
            int steep = 0, rough = 0, measured = 0;

            var worstSlopeAt = Vector3.zero;
            var worstChatterAt = Vector3.zero;

            for (int line = 0; line < Lines; line++)
            {
                // Lines in both directions, so a dune field whose crests run one way
                // cannot be sampled along its own ridges and come back flat.
                bool alongX = line % 2 == 0;
                float across = Mathf.Lerp(-half * 0.9f, half * 0.9f, line / (float)(Lines - 1));

                float previousHeight = float.NaN;
                float previousSlope = float.NaN;

                runs++;

                for (int i = 0; i < Samples; i++)
                {
                    float along = -Samples * Step * 0.5f + i * Step;
                    if (Mathf.Abs(along) > half * 0.92f) { previousHeight = float.NaN; continue; }

                    float x = alongX ? along : across;
                    float z = alongX ? across : along;

                    // The canyon is meant to be a hole. Sampling across it would report
                    // its wall as the steepest ground in the level, which it is, and
                    // which is the entire point of it.
                    if (clear > 0f && Mathf.Abs(x - GorgeCentre(theme, z)) < clear)
                    {
                        previousHeight = float.NaN;
                        previousSlope = float.NaN;
                        continue;
                    }

                    if (!Physics.Raycast(new Vector3(x, 400f, z), Vector3.down, out var hit, 800f))
                    {
                        misses++;
                        previousHeight = float.NaN;
                        previousSlope = float.NaN;
                        continue;
                    }

                    hits++;
                    low = Mathf.Min(low, hit.point.y);
                    high = Mathf.Max(high, hit.point.y);

                    // Only the sand itself. Anything standing on it -- a boulder, a
                    // crate, a fence rail -- is a step up off the ground and not the
                    // ground being rough, and a run of samples that walks over a rock
                    // reports the rock's own sides as seventy-degree terrain.
                    //
                    // Tested by tag rather than by the surface normal, which was the
                    // first attempt and does not work: the top of a boulder is as
                    // near-level as a dune is, so half the rocks in the arena passed the
                    // filter and each one contributed two enormous steps.
                    if (hit.collider == null || !chunks.Contains(hit.collider))
                    {
                        previousHeight = float.NaN;
                        previousSlope = float.NaN;
                        continue;
                    }

                    if (!float.IsNaN(previousHeight))
                    {
                        float slope = Mathf.Atan2(Mathf.Abs(hit.point.y - previousHeight), Step)
                                    * Mathf.Rad2Deg;

                        measured++;

                        if (slope > worstSlope) { worstSlope = slope; worstSlopeAt = hit.point; }
                        if (slope > MaxSlopeDegrees) steep++;

                        float signed = Mathf.Atan2(hit.point.y - previousHeight, Step) * Mathf.Rad2Deg;

                        if (!float.IsNaN(previousSlope))
                        {
                            float chatter = Mathf.Abs(signed - previousSlope);

                            if (chatter > worstChatter) { worstChatter = chatter; worstChatterAt = hit.point; }
                            if (chatter > MaxChatterDegrees) rough++;
                        }

                        previousSlope = signed;
                    }

                    previousHeight = hit.point.y;
                }
            }

            if (hits == 0)
            {
                problems.Add($"{name}: nothing was under any of {Lines * Samples} downward rays, " +
                             "so the arena has no ground at all");
                return;
            }

            if (misses > hits * 0.02f)
                problems.Add($"{name}: {misses} of {hits + misses} rays found nothing under them, " +
                             "so the ground has holes in it that a player would fall through");

            float relief = high - low;

            if (relief < MinRelief)
                problems.Add($"{name}: the ground rises and falls by only {relief:0.0} m across the " +
                             $"whole arena, under the {MinRelief:0} m a dune field needs to change how " +
                             "anything plays -- it will read as a flat plane with a texture on it");

            if (measured > 0 && steep > measured * SlopeTolerance)
                problems.Add($"{name}: {steep} of {measured} steps are steeper than {MaxSlopeDegrees:0} " +
                             $"degrees (worst {worstSlope:0.0} at {worstSlopeAt}), which is past what a " +
                             "vehicle can climb and close to what the navmesh will bake");

            if (measured > 0 && rough > measured * ChatterTolerance)
                problems.Add($"{name}: the gradient changes by more than {MaxChatterDegrees:0} degrees " +
                             $"between adjacent steps {rough} times out of {measured} (worst " +
                             $"{worstChatter:0.0} at {worstChatterAt}), so the surface is jagged at " +
                             "wheel scale however smooth its shading looks");

            // The worst positions are printed on a pass as well as on a failure. A run
            // that squeaks under the tolerance is a run with a handful of bad steps still
            // in it, and "where" is the only question worth asking next -- without it the
            // report says the ground is fine and quietly averages away the one place it
            // is not.
            notes.Append($"\n  {name}: relief {relief:0.0} m ({low:0.0} to {high:0.0}), " +
                         $"{measured} steps over {runs} lines, {misses} misses" +
                         $"\n  {name}: worst slope {worstSlope:0.0} deg at {worstSlopeAt} " +
                         $"({steep} over {MaxSlopeDegrees:0}), worst gradient change " +
                         $"{worstChatter:0.0} deg at {worstChatterAt} ({rough} over " +
                         $"{MaxChatterDegrees:0})");
        }

        /// <summary>
        /// The river's centre line, kept in step with FPSKitSceneBuilder.GorgeCentreAt by
        /// hand for the same reason FPSKitZoneTest keeps its own copy: a test that shared
        /// the formula could not notice the formula changing.
        /// </summary>
        static float GorgeCentre(LevelTheme theme, float z)
            => theme.hazardOffset
             + Mathf.Sin(z / 90f) * theme.hazardMeander
             + Mathf.Sin(z / 37f + 1.7f) * theme.hazardMeander * 0.35f;
    }
}
#endif
