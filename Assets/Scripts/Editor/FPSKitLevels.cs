#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Generates the <see cref="LevelSet"/> assets -- one ladder of levels per arena.
    ///
    /// This mirrors <see cref="FPSKitEnemyRoster"/> and <see cref="FPSKitThemes"/> on
    /// purpose: content lives in assets, not in code. A ninth level is an entry added to
    /// a set in the Inspector, and a whole new ladder is a duplicated asset -- neither
    /// needs a line of this file, and neither needs the scene rebuilt, because
    /// <see cref="LevelManager"/> reads the set at runtime and the level select clones a
    /// tile per entry.
    ///
    /// What is written here is only the *starting* curve. Like the enemy roster, an
    /// existing asset is left alone by <see cref="GetOrCreate"/>, so tuning done in the
    /// Inspector survives every rebuild -- and, exactly as with the roster, that means a
    /// number changed in <see cref="Configure"/> does not reach the assets the game reads
    /// until <see cref="ResetAll"/> is run. FPSKitBatch.ResetLevelSets is the way in.
    /// </summary>
    public static class FPSKitLevels
    {
        public const string LevelFolder = "Assets/FPSKit_Generated/Levels";

        /// <summary>
        /// Levels per arena. Eight is a ladder you can see the top of from the bottom:
        /// long enough that unlocking means something, short enough that a player who
        /// likes one arena can finish it.
        /// </summary>
        public const int LevelsPerArena = 8;

        // ==================================================================
        [MenuItem("FPSKit/Create Level Sets", false, 44)]
        public static void CreateAllMenu()
        {
            var sets = GetOrCreateAll();

            EditorUtility.DisplayDialog("FPSKit Levels",
                $"{sets.Count} level sets are ready in:\n{LevelFolder}\n\n" +
                "Select one to retune any level's enemy count, clock, boss or star " +
                "thresholds. The dashboard reads them through the arena catalog, so " +
                "run FPSKit > Build Dashboard after changing how many levels an arena " +
                "has.", "OK");

            EditorUtility.FocusProjectWindow();
            if (sets.Count > 0) Selection.activeObject = sets[0];
        }

        [MenuItem("FPSKit/Reset Level Sets to Defaults", false, 45)]
        public static void ResetAllMenu()
        {
            if (!EditorUtility.DisplayDialog("Reset levels",
                "Re-applies the built-in curve to every level set.\n\n" +
                "Any tuning you did in the Inspector will be lost. Stars the player has " +
                "already earned are not touched.", "Reset them", "Cancel")) return;

            ResetAll();
        }

        /// <summary>
        /// Re-stamps the built-in curve onto every set, with no dialog. See the class
        /// comment for why this has to exist separately from GetOrCreate.
        /// </summary>
        public static void ResetAll()
        {
            for (int i = 0; i < FPSKitThemes.Names.Length; i++)
            {
                // ArenaIndexOf, not i: the loop is over the themes as they were built
                // and the curve is shifted by where the arena sits in the campaign.
                // Passing i here was the same bug as reading the wrong list, written in
                // the one place that does not call the helper.
                var set = GetOrCreate(FPSKitThemes.Names[i]);
                Configure(set, FPSKitThemes.Names[i], ArenaIndexOf(FPSKitThemes.Names[i]));
                EditorUtility.SetDirty(set);
            }

            AssetDatabase.SaveAssets();

            Debug.Log($"<color=lime>[FPSKit]</color> {FPSKitThemes.Names.Length} level sets reset " +
                      $"to {LevelsPerArena} levels each.");
        }

        public static List<LevelSet> GetOrCreateAll()
        {
            EnsureFolders();

            var result = new List<LevelSet>();
            foreach (var themeName in FPSKitThemes.Names) result.Add(GetOrCreate(themeName));

            AssetDatabase.SaveAssets();
            return result;
        }

        /// <summary>
        /// The set for one arena, created on first use and then left alone. Named from
        /// the theme so the scene builder can find an arena's ladder without being told.
        /// </summary>
        public static LevelSet GetOrCreate(string themeName)
        {
            EnsureFolders();

            string path = $"{LevelFolder}/Levels_{SafeName(themeName)}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<LevelSet>(path);
            if (existing != null) return existing;

            var set = ScriptableObject.CreateInstance<LevelSet>();
            Configure(set, themeName, ArenaIndexOf(themeName));

            AssetDatabase.CreateAsset(set, path);
            return set;
        }

        /// <summary>
        /// How far into the campaign this arena is, which is what the difficulty curve
        /// below is shifted by.
        ///
        /// <b>The campaign's order, not the theme list's.</b> The themes are listed in
        /// the order they were built, which means nothing to a player; the campaign is
        /// the order they are played in. Shifted by the wrong one, the third zone a
        /// player reaches is tuned as though it were the second -- a dip in the
        /// difficulty curve that compiles, builds and ships, and reads as one arena being
        /// oddly easy rather than as a wrong index.
        ///
        /// Falls back to the theme list for an arena the campaign does not use, which is
        /// any arena added without being written into the story.
        /// </summary>
        static int ArenaIndexOf(string themeName)
        {
            int inCampaign = FPSKitCampaign.IndexOfTheme(themeName);
            if (inCampaign >= 0) return inCampaign;

            for (int i = 0; i < FPSKitThemes.Names.Length; i++)
                if (FPSKitThemes.Names[i] == themeName) return i;

            return 0;
        }

        // ==================================================================
        // The curve
        //
        // Every level is the same shape -- a fixed crowd against a strict clock -- so
        // the difficulty is in four numbers moving together: more enemies, less time
        // each, tougher bodies and a nastier mix. The clock is the one that has to be
        // right: it is derived from the enemy count rather than chosen, because a time
        // limit picked by feel is either free at the bottom of the ladder or impossible
        // at the top, and neither is a difficulty curve.
        //
        // The arena index shifts the whole ladder. The six arenas are all open from the
        // start, so without it the sixth is the first one played again with a different
        // skybox; with it, the later arenas are somewhere to go once the earlier ones
        // stop being a fight.
        // ==================================================================

        /// <summary>Seconds of clock per enemy, before the flat allowance.</summary>
        const float SecondsPerEnemy = 3.4f;

        /// <summary>Flat seconds every level gets, which is mostly walking to the fight.</summary>
        const float BaseSeconds = 12f;

        /// <summary>And what a boss is worth on the clock, over and above its escort.</summary>
        const float BossSeconds = 16f;

        static void Configure(LevelSet set, string themeName, int arenaIndex)
        {
            set.name = $"Levels_{SafeName(themeName)}";
            set.arenaScene = SafeName(themeName);

            // Written by the campaign and stamped here, because this is the asset the
            // arena already holds -- see FPSKitCampaign.StakesFor. An arena outside the
            // campaign gets nothing and the HUD shows the countdown as it always did.
            set.stakes = FPSKitCampaign.StakesFor(themeName);
            set.levels = new List<LevelSet.Level>(LevelsPerArena);

            var bosses = BossRoster();

            for (int i = 0; i < LevelsPerArena; i++)
            {
                int number = i + 1;

                // A boss every third level, and always on the last one -- the top of a
                // ladder has to be something rather than one more of the same.
                bool boss = number % 3 == 0 || number == LevelsPerArena;

                int enemies = 6 + 3 * i;

                float clock = Mathf.Round(enemies * SecondsPerEnemy + BaseSeconds
                                          + (boss ? BossSeconds : 0f));

                var level = new LevelSet.Level
                {
                    displayName = LevelName(number, boss),
                    brief = Brief(number, enemies, boss),

                    enemyCount = enemies,
                    timeLimit = clock,

                    // The crowd on screen grows more slowly than the crowd in total, so
                    // a later level is a longer fight rather than an unwinnable one.
                    maxAliveAtOnce = 4 + i,
                    spawnInterval = Mathf.Lerp(0.8f, 0.3f, i / (float)(LevelsPerArena - 1)),

                    // The first level has more to read on it than the rest.
                    briefingTime = number == 1 ? 4.5f : 3f,

                    hasBoss = boss,
                    bossArchetype = boss ? BossFor(number, bosses) : null,
                    bossWeight = 4f + 0.5f * number,
                    bossHealthMultiplier = 1f + 0.3f * (number / 3),

                    healthMultiplier = 1f + 0.10f * i + 0.05f * arenaIndex,
                    damageMultiplier = 1f + 0.06f * i + 0.03f * arenaIndex,

                    // Capped well under the player's walk of 5.6 m/s. See the combat
                    // rules in CLAUDE.md: disengaging has to stay possible.
                    speedMultiplier = Mathf.Min(1.35f, 1f + 0.03f * i + 0.02f * arenaIndex),

                    aggression = Mathf.Clamp01(i / (float)(LevelsPerArena - 1) * 0.9f
                                               + 0.04f * arenaIndex),

                    rosterStep = RosterStep(i) + arenaIndex,

                    twoStarScore = 0.8f,
                    oneStarScore = 0.5f
                };

                set.levels.Add(level);
            }
        }

        /// <summary>
        /// Which enemies are in the mix. The roster gates its variants at steps 1, 2, 3,
        /// 4, 7 and 9, so the ladder steps past them rather than through them: eight
        /// levels crawling one step at a time would meet the last two variants only on
        /// the final level, and the mix would stop changing halfway up.
        /// </summary>
        static int RosterStep(int index)
        {
            int[] steps = { 1, 3, 4, 6, 7, 9, 10, 12 };
            return index < steps.Length ? steps[index] : 12 + (index - steps.Length + 1) * 2;
        }

        /// <summary>
        /// The bosses in roster order. Taken from the archetype assets rather than by
        /// name so a project that renamed or added one still gets a boss, and so the
        /// generator cannot name an asset that does not exist.
        /// </summary>
        static List<EnemyArchetype> BossRoster()
        {
            var bosses = new List<EnemyArchetype>();

            foreach (var archetype in FPSKitEnemyRoster.GetOrCreateAll())
                if (archetype != null && archetype.role == EnemyArchetype.Role.Boss)
                    bosses.Add(archetype);

            if (bosses.Count == 0)
                Debug.LogWarning("[FPSKit] No archetype has the Boss role, so the boss levels " +
                                 "will have to fall back at runtime. Run FPSKit > Reset Enemy " +
                                 "Archetypes to Defaults.");

            return bosses;
        }

        /// <summary>
        /// Walks up the boss list as the ladder climbs, so the third boss level is not
        /// the first one again. Clamped rather than wrapped: meeting the easier boss
        /// again after the harder one would read as a mistake.
        /// </summary>
        static EnemyArchetype BossFor(int number, List<EnemyArchetype> bosses)
        {
            if (bosses.Count == 0) return null;

            int rank = Mathf.Max(0, number / 3 - 1);
            return bosses[Mathf.Min(rank, bosses.Count - 1)];
        }

        static string LevelName(int number, bool boss)
        {
            if (boss) return $"BOSS {number / 3}";

            string[] names =
            {
                "FIRST CONTACT", "PUSHBACK", "OVERRUN", "NO COVER",
                "HOLD THE LINE", "THINNING OUT", "LAST STAND"
            };

            return names[(number - 1) % names.Length];
        }

        static string Brief(int number, int enemies, bool boss)
            => boss
                ? $"{enemies} hostiles and a boss. The boss is most of the score."
                : number == 1
                    ? $"{enemies} hostiles. Kill them all before the clock runs out."
                    : $"{enemies} hostiles. Clear them for three stars.";

        // ==================================================================
        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/FPSKit_Generated"))
                AssetDatabase.CreateFolder("Assets", "FPSKit_Generated");

            if (!AssetDatabase.IsValidFolder(LevelFolder))
                AssetDatabase.CreateFolder("Assets/FPSKit_Generated", "Levels");
        }

        static string SafeName(string value) => value.Replace(" ", "");
    }
}
#endif
