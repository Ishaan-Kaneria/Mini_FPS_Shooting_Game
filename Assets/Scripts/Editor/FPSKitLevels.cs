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
        public const int LevelsPerArena = 32;

        /// <summary>
        /// How many levels an arena's ladder holds.
        ///
        /// Eight everywhere except the last zone, which is three. <b>A finale is not a
        /// ladder.</b> The Auger House is a house: the player arrives having cleared six
        /// arenas, and asking them for another eight rungs of the same curve before the
        /// last fight would turn the ending into a chore with a cutscene at the end of
        /// it. Three is a way in, a room to get through, and Marit.
        /// </summary>
        /// <summary>
        /// Thirty-two in every arena, the finale included: the arenas are big, and eight
        /// two-minute levels never took a player far enough from where they started to see
        /// more than a corner of one.
        /// </summary>
        public static int LevelsFor(string themeName) => LevelsPerArena;

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
                      $"to {LevelsPerArena} levels each, {LevelsFor(FPSKitThemes.FinaleName)} " +
                      "in the finale.");
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
        // Every level is a fixed crowd against a strict clock, now long enough to use
        // the arena: two and a half minutes at the bottom of every ladder, climbing to the
        // arena's longest (LongestClock: six minutes in the first arena, fifteen by the
        // sixth). The clock is chosen and the crowd follows from it at about five seconds
        // an enemy, arriving in waves from one side of the map at a time and from further
        // out as the ladder climbs; the objective comes round several times in a long
        // level, each time somewhere new. Toughness, aggression and the mix move with the
        // level's place on its ladder (t), so the curve has the same shape at 32 rungs as
        // it had at 8. The arena index shifts the whole ladder, as before.
        // ==================================================================

        /// <summary>Every arena's first level: two and a half minutes.</summary>
        const float FirstClock = 150f;

        /// <summary>
        /// The longest level in an arena, by where the arena sits in the campaign: six
        /// minutes in the first, reaching fifteen by the sixth, which the finale keeps.
        /// </summary>
        static float LongestClock(int arenaIndex) => Mathf.Lerp(360f, 900f, Mathf.Clamp01(arenaIndex / 5f));

        /// <summary>
        /// Seconds of clock per enemy. About five: a long level is a fight across the map,
        /// with objectives to walk to, not a queue at the door. A little tighter up the
        /// ladder, and in the finale.
        /// </summary>
        static float SecondsPerEnemy(float t, bool finale) => Mathf.Lerp(5.2f, 4.6f, t) - (finale ? 0.4f : 0f);

        /// <summary>
        /// How often an objective comes round in a level of this length: a runner or a
        /// hold site every two and a half minutes, a charge every forty seconds, a recon
        /// point every seventy-five.
        /// </summary>
        static int StagesFor(LevelSet.Objective objective, float clock) => objective switch
        {
            LevelSet.Objective.Hunt => Mathf.Clamp(Mathf.RoundToInt(clock / 150f), 1, 6),
            LevelSet.Objective.Hold => Mathf.Clamp(Mathf.RoundToInt(clock / 150f), 1, 6),
            LevelSet.Objective.Disposal => Mathf.Clamp(Mathf.RoundToInt(clock / 40f), 4, 20),
            LevelSet.Objective.Recon => Mathf.Clamp(Mathf.RoundToInt(clock / 75f), 2, 12),
            _ => 1,
        };

        static void Configure(LevelSet set, string themeName, int arenaIndex)
        {
            set.name = $"Levels_{SafeName(themeName)}";
            set.arenaScene = SafeName(themeName);

            // Written by the campaign and stamped here, because this is the asset the
            // arena already holds -- see FPSKitCampaign.StakesFor. An arena outside the
            // campaign gets nothing and the HUD shows the countdown as it always did.
            set.stakes = FPSKitCampaign.StakesFor(themeName);
            int count = LevelsFor(themeName);
            set.levels = new List<LevelSet.Level>(count);

            var bosses = BossRoster();

            // How many levels so far have asked for an objective. The rotation below is
            // keyed on this rather than on the level index, and the difference is not
            // cosmetic: the boss slots are not evenly spaced, so at eight levels an
            // index-keyed rotation of six comes back round and hands two levels of the
            // same ladder the same objective while leaving another one unused.
            int fought = 0;

            bool finale = themeName == FPSKitThemes.FinaleName;

            for (int i = 0; i < count; i++)
            {
                int number = i + 1;

                // Where this level sits on its ladder, 0 at the bottom and 1 at the top.
                // Everything that used to step per level is a function of this, so the
                // curve has the same shape at 32 levels as it had at 8.
                float t = i / (float)Mathf.Max(1, count - 1);

                // A boss every third level, and always on the last one -- the top of a
                // ladder has to be something rather than one more of the same.
                bool boss = number % 3 == 0 || number == count;

                // The finale opens where an ordinary ladder ends and climbs from there.
                // The player arrives having cleared six arenas; starting them back at six
                // hostiles would say the last zone is easier than the one before it.
                // The clock is chosen and the crowd follows from it: two and a half minutes
                // at the bottom of every ladder, rising (slowly at first) to the arena's
                // longest, rounded to five seconds so the tile reads cleanly.
                float clock = Mathf.Round(Mathf.Lerp(FirstClock, LongestClock(arenaIndex), Mathf.Pow(t, 1.25f)) / 5f) * 5f;
                int enemies = Mathf.Max(6, Mathf.RoundToInt((clock - 20f) / SecondsPerEnemy(t, finale)));

                var objective = ObjectiveFor(fought, boss, arenaIndex);
                if (!boss) fought++;

                bool last = i == count - 1;
                string holder = FPSKitCampaign.SiblingFor(themeName);

                var level = new LevelSet.Level
                {
                    displayName = last && !string.IsNullOrEmpty(holder)
                        ? holder.ToUpperInvariant()
                        : LevelName(themeName, number, boss, objective),

                    brief = Brief(themeName, i, count, enemies, boss, objective, StagesFor(objective, clock), last, holder),

                    // Named on the level rather than taken from the archetype, so the
                    // banner when the boss walks in says who it is rather than which
                    // chassis it was built on. A level with none falls back to the
                    // archetype exactly as before.
                    bossName = last && !string.IsNullOrEmpty(holder)
                        ? holder
                        : boss ? FPSKitCampaign.LieutenantFor(themeName, number / 3) : "",

                    objective = objective,

                    // Worth about a fifth of a mid-ladder roster, so an objective ignored
                    // costs a star and an objective done is most of the difference
                    // between two and three. Scaled with the level so it keeps that share
                    // as the crowd grows rather than becoming a rounding error by level 8.
                    objectiveWeight = Mathf.Round(Mathf.Max(4f, enemies * 0.45f)),
                    objectiveStages = StagesFor(objective, clock),

                    enemyCount = enemies,
                    timeLimit = clock,

                    // The crowd on screen grows more slowly than the crowd in total, so
                    // a later level is a longer fight rather than an unwinnable one.
                    // And they arrive thicker. Twelve at once against the fourteen an
                    // eighth-rung level tops out at is what "waves" means here: the
                    // arena is never not full for the whole of the last fight.
                    maxAliveAtOnce = Mathf.RoundToInt(finale ? Mathf.Lerp(9f, 18f, t) : Mathf.Lerp(5f, 14f, t) + arenaIndex * 0.3f),
                    spawnInterval = Mathf.Lerp(0.8f, 0.3f, t),

                    // Waves from one side of the map at a time, and from further out as the
                    // ladder climbs, so a long level is fought across the arena. The short
                    // levels at the bottom keep the continuous stream.
                    waveSize = clock < 200f ? 0 : Mathf.RoundToInt((finale ? Mathf.Lerp(9f, 18f, t) : Mathf.Lerp(5f, 14f, t)) * 1.6f),
                    waveRest = clock < 200f ? 0f : Mathf.Lerp(8f, 18f, t),
                    spawnReach = Mathf.Lerp(1f, 2.2f, t),

                    // Long enough to read the mission and the stakes as the card animates
                    // in; the first level has the most to take in. The clock does not run
                    // during it.
                    briefingTime = 9f,

                    hasBoss = boss,

                    // The last rung is the person who holds the zone; the ones before it
                    // are the generic two.
                    bossArchetype = !boss
                        ? null
                        : last && !string.IsNullOrEmpty(holder)
                            ? FPSKitEnemyRoster.GetOrCreate(holder)
                            : BossFor(number, bosses),
                    // About a ninth of the level, so leaving the boss alive still costs a star
                    // on a level of a hundred and fifty.
                    bossWeight = Mathf.Round(Mathf.Max(5f, enemies * 0.12f)),
                    bossHealthMultiplier = 1f + 2.4f * t,

                    healthMultiplier = 1f + 1.0f * t + 0.05f * arenaIndex,
                    damageMultiplier = 1f + 0.6f * t + 0.03f * arenaIndex,

                    // Capped well under the player's walk of 5.6 m/s. See the combat
                    // rules in CLAUDE.md: disengaging has to stay possible.
                    speedMultiplier = Mathf.Min(1.35f, 1f + 0.25f * t + 0.02f * arenaIndex),

                    aggression = Mathf.Clamp01(t * 0.9f + 0.04f * arenaIndex),

                    rosterStep = RosterStep(Mathf.RoundToInt(t * 7f)) + arenaIndex,

                    // Eight spare magazines everywhere but One Magazine, which starts dry and is
                    // fed by kills. The drop chance is on top of each enemy's own.
                    // More spare for a longer level: a fifteen-minute fight is well over a
                    // thousand rounds, and the drops alone would leave long gaps dry.
                    reserveMagazines = objective == LevelSet.Objective.OneMagazine ? 0 : 8 + Mathf.RoundToInt(clock / 45f),
                    ammoDropChance = 0.12f,

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

            // The generic two only. Every Auger is a Boss-role archetype now, so a sweep
            // for "anything with the boss role" would hand rung three of the Warehouse
            // to Tove -- who is standing at rung eight of the same ladder. Killing her
            // as a warm-up for herself is not a difficulty curve, it is a continuity
            // error the player meets before the story has said her name.
            foreach (var name in FPSKitEnemyRoster.GenericBossNames)
            {
                var archetype = FPSKitEnemyRoster.GetOrCreate(name);
                if (archetype != null && archetype.role == EnemyArchetype.Role.Boss)
                    bosses.Add(archetype);
            }

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

        /// <summary>
        /// What each level asks for beyond its roster.
        ///
        /// Three rules, and all three are about what a ladder feels like rather than
        /// about the objectives themselves:
        ///
        /// <b>A boss level is always a plain clear.</b> The boss is the level -- it is
        /// most of the weight by design -- and asking the player to also hold ground or
        /// carry a charge across the arena during it is two levels fighting each other
        /// for the same clock.
        ///
        /// <b>The first level of the campaign is a plain clear too.</b> Whatever else is
        /// true of a first level, it has to teach the thing every other level is built
        /// on, and it cannot do that while also introducing a rule.
        ///
        /// <b>The rotation is offset by the arena.</b> Without that, the fourth level of
        /// all six zones is the same objective, and a player who has finished one arena
        /// has seen the whole ladder of the next one before starting it.
        /// </summary>
        static LevelSet.Objective ObjectiveFor(int fought, bool boss, int arenaIndex)
        {
            if (boss) return LevelSet.Objective.Clear;
            if (fought == 0 && arenaIndex == 0) return LevelSet.Objective.Clear;

            var rotation = new[]
            {
                LevelSet.Objective.Hunt,
                LevelSet.Objective.OneMagazine,
                LevelSet.Objective.Hold,
                LevelSet.Objective.Blackout,
                LevelSet.Objective.Extraction,
                LevelSet.Objective.Disposal,
                LevelSet.Objective.Recon
            };

            return rotation[(fought + arenaIndex) % rotation.Length];
        }

        static string LevelName(string themeName, int number, bool boss, LevelSet.Objective objective)
        {
            if (boss)
            {
                string named = FPSKitCampaign.LieutenantFor(themeName, number / 3);
                return string.IsNullOrEmpty(named) ? $"BOSS {number / 3}" : named.ToUpperInvariant();
            }

            // Named for what it asks, because the name is on the tile the player is
            // choosing from and "NO COVER" says nothing that "BLACKOUT" does not say
            // better. The plain clears keep the old names, which is most of what they
            // were ever for.
            switch (objective)
            {
                case LevelSet.Objective.Hunt: return "THE RUNNER";
                case LevelSet.Objective.OneMagazine: return "ONE MAGAZINE";
                case LevelSet.Objective.Hold: return "HOLD THE LINE";
                case LevelSet.Objective.Blackout: return "BLACKOUT";
                case LevelSet.Objective.Extraction: return "WAY OUT";
                case LevelSet.Objective.Disposal: return "DISPOSAL";
                case LevelSet.Objective.Recon: return "RECON";
            }

            string[] names = { "FIRST CONTACT", "PUSHBACK", "OVERRUN", "NO COVER", "LAST STAND" };
            return names[(number - 1) % names.Length];
        }

        /// <summary>
        /// The line under the name on the tile, and the first thing on screen when a
        /// level starts. It says what the level wants, not how it was generated.
        /// </summary>
        static string Brief(string themeName, int index, int count, int enemies, bool boss,
                            LevelSet.Objective objective, int stages, bool last, string holder)
        {
            // Where the player is, then what the level wants. Four rungs of story for
            // eight levels, so the arena is saying something different at the bottom of
            // the ladder and at the top without anybody writing forty-eight lines that
            // could each be wrong about the level they sit on.
            int rung = last ? 3 : index < count * 3 / 8 ? 0 : index < count * 6 / 8 ? 1 : 2;
            string story = FPSKitCampaign.StoryClause(themeName, rung);

            string task = Task(enemies, boss, objective, stages, last, holder);

            return string.IsNullOrEmpty(story) ? task : $"{story} {task}";
        }

        static string Task(int enemies, bool boss, LevelSet.Objective objective, int stages, bool last,
                           string holder)
        {
            if (last && !string.IsNullOrEmpty(holder))
                return $"{enemies} between you and {holder.Split(' ')[0]}.";

            if (boss) return $"{enemies} hostiles and a boss. The boss is most of the score.";

            switch (objective)
            {
                case LevelSet.Objective.Hunt:
                    return stages > 1
                        ? $"{enemies} hostiles. {stages} of them run, one at a time -- catch each."
                        : $"{enemies} hostiles. One of them runs -- that one is the level.";

                case LevelSet.Objective.OneMagazine:
                    return $"{enemies} hostiles and one magazine, nothing spare. Kills drop ammunition.";

                case LevelSet.Objective.Hold:
                    return stages > 1
                        ? $"{enemies} hostiles. Hold {stages} marked grounds across the map, one after another."
                        : $"{enemies} hostiles. Stand on the marked ground and keep standing.";

                case LevelSet.Objective.Blackout:
                    return $"{enemies} hostiles, and the lights are not yours.";

                case LevelSet.Objective.Extraction:
                    return $"{enemies} hostiles. Clear them, then reach the way out.";

                case LevelSet.Objective.Disposal:
                    return $"{enemies} hostiles and {stages} charges. Reach a charge or lose the clock.";
 
                case LevelSet.Objective.Recon:
                    return $"{enemies} hostiles. Reach {stages} marked points across the map.";
            }

            return $"{enemies} hostiles. Clear them for three stars.";
        }

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
