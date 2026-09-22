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
    /// Opens every built arena and asks who fights in it.
    ///
    /// <b>The failure this exists for is silence.</b> The roster is stamped into the
    /// scene by the builder, so a theme whose roster was never filled in, or a build run
    /// before <c>ResetThemes</c>, produces an arena that plays exactly as it always did
    /// -- every archetype in the project, six zones a skybox apart -- with nothing
    /// logged and every other check green. The whole point of the change is invisible
    /// from inside the game unless somebody has played all six and noticed they feel the
    /// same, which is the thing a player notices and a test never does.
    ///
    /// So it asserts the three things that would each make the change a no-op: that an
    /// arena's roster is its theme's rather than the whole project's, that it contains
    /// the enemy that zone is supposed to introduce, and that no two arenas have the same
    /// roster as each other.
    ///
    /// No play mode and no graphics -- it opens scenes and reads components, so it is
    /// cheap enough to run beside the compile check.
    /// </summary>
    public static class FPSKitRosterTest
    {
        public static void VerifyRosters()
        {
            var problems = new StringBuilder();
            var notes = new StringBuilder();

            try
            {
                int everything = FPSKitEnemyRoster.Names.Length;
                var seen = new Dictionary<string, string>();

                foreach (var themeName in FPSKitThemes.Names)
                {
                    string scene = $"Assets/FPSKit_Generated/Scenes/{themeName.Replace(" ", "")}.unity";

                    if (!System.IO.File.Exists(scene))
                    {
                        problems.Append($"\n  - {themeName}: {scene} does not exist; run " +
                                        "FPSKitBatch.BuildAllThemes");
                        continue;
                    }

                    EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);

                    var manager = UnityEngine.Object.FindAnyObjectByType<LevelManager>();

                    if (manager == null)
                    {
                        problems.Append($"\n  - {themeName}: the scene has no LevelManager");
                        continue;
                    }

                    var types = manager.enemyTypes;

                    if (types == null || types.Length == 0)
                    {
                        problems.Append($"\n  - {themeName}: no roster at all, so the arena spawns " +
                                        "nothing and the clock runs out on an empty level");
                        continue;
                    }

                    var names = new List<string>();
                    foreach (var type in types)
                        if (type != null && type.archetype != null)
                            names.Add(type.archetype.displayName);

                    names.Sort();
                    string key = string.Join(",", names);

                    // Every archetype in the project is the old behaviour, exactly.
                    if (names.Count >= everything)
                        problems.Append($"\n  - {themeName}: carries all {names.Count} archetypes, " +
                                        "which is the roster this change was supposed to replace " +
                                        "-- run FPSKitBatch.ResetThemes, then BuildAllThemes");

                    int zone = FPSKitCampaign.IndexOfTheme(themeName);

                    if (zone >= 0 && zone < FPSKitEnemyRoster.ZoneDebut.Length)
                    {
                        string debut = FPSKitEnemyRoster.ZoneDebut[zone];

                        if (!names.Contains(debut))
                            problems.Append($"\n  - {themeName}: does not field its own \"{debut}\", " +
                                            "so the one enemy that makes this arena itself is not " +
                                            "in it");
                    }

                    string holder = FPSKitCampaign.SiblingFor(themeName);

                    if (!string.IsNullOrEmpty(holder) && !names.Contains(holder))
                        problems.Append($"\n  - {themeName}: {holder} holds this zone and is not in " +
                                        "its roster, so the last level has no boss to spawn");

                    if (seen.TryGetValue(key, out string twin))
                        problems.Append($"\n  - {themeName} and {twin} field exactly the same " +
                                        "enemies, so one of them is the other with a different sky");
                    else
                        seen[key] = themeName;

                    notes.Append($"\n  {themeName,-22} {names.Count} types: {string.Join(", ", names)}");
                }

                if (problems.Length > 0)
                {
                    Debug.LogError($"[FPSKitBatch] verify rosters FAILED:{problems}\n{notes}");
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log($"[FPSKitBatch] verify rosters passed: {FPSKitThemes.Names.Length} arenas, " +
                          $"each with its own roster.{notes}");

                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }
    }
}
#endif
