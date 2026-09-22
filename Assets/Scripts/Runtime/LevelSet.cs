using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ordered levels one arena offers, as data rather than as a hard-coded ladder.
///
/// This is the same extension point the rest of the kit uses. An arena is a
/// <see cref="LevelTheme"/> and a scene built from it; the levels played *in* that
/// scene are this asset, and adding, retuning or reordering one never means touching
/// <see cref="LevelManager"/>. The generated sets live in
/// FPSKit_Generated/Levels/Levels_&lt;Theme&gt;.asset and are recreated by
/// FPSKit &gt; Reset Level Sets.
///
/// The levels are entries in a list rather than one asset each, which is how
/// <see cref="ArenaCatalog"/> holds arenas: eight levels across six arenas is
/// forty-eight assets to keep in sync, and the only thing that would buy is the
/// ability to share a level between two arenas, which no level does.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Level Set", fileName = "Levels")]
public class LevelSet : ScriptableObject
{
    /// <summary>
    /// One level: a fixed number of enemies, a clock, and the score needed for each
    /// star. Everything here is per-level on purpose -- a difficulty curve written as
    /// a formula in code is one nobody can bend for a single level that plays badly.
    /// </summary>
    /// <summary>
    /// What a level asks for beyond killing the roster.
    ///
    /// <b>None of these may change what *ends* a level.</b> The clock ends it, always and
    /// without exception -- see the comment on <c>LevelManager.RunLevel</c>. An objective
    /// may only change what *scores*, and may at most delay the early finish that a
    /// cleared roster grants. Written the other way round -- "the level ends when the
    /// zone has been held" -- a single enemy down a hole is a guaranteed softlock, which
    /// is the one failure this kit is built to be incapable of.
    ///
    /// Everything here is therefore either a modifier on how the fight is fought
    /// (<see cref="OneMagazine"/>, <see cref="Blackout"/>) or a second source of weight
    /// beside the kills (<see cref="Hunt"/>, <see cref="Hold"/>,
    /// <see cref="Extraction"/>, <see cref="Disposal"/>).
    /// </summary>
    public enum Objective
    {
        /// <summary>Kill the roster. The original level, and still most of them.</summary>
        Clear = 0,

        /// <summary>No reloading. Kills feed you instead, so accuracy is the resource.</summary>
        OneMagazine = 1,

        /// <summary>One of them runs, is worth most of the level, and is marked.</summary>
        Hunt = 2,

        /// <summary>Stand in a marked place while the fight comes to you.</summary>
        Hold = 3,

        /// <summary>The lights are somebody else's to switch off.</summary>
        Blackout = 4,

        /// <summary>Clear it, then get to the way out before the clock does.</summary>
        Extraction = 5,

        /// <summary>Charges on timers. Reach one to make it safe, or lose clock to it.</summary>
        Disposal = 6
    }

    [Serializable]
    public class Level
    {
        [Header("Identity")]
        [Tooltip("Shown on the level tile and on the banner. Falls back to \"LEVEL n\".")]
        public string displayName;

        [TextArea(2, 3)]
        [Tooltip("One line under the name on the level select tile.")]
        public string brief;

        [Header("The Fight")]
        [Tooltip("Enemies spawned in total. The level is cleared when every one of them " +
                 "is dead -- there is no endless stream, so this number is the whole level.")]
        [Min(1)] public int enemyCount = 8;

        [Tooltip("Seconds on the clock. Strict: when it runs out the level is scored on " +
                 "whatever was killed by then, and a score under the one-star threshold " +
                 "is a failure the player has to replay.")]
        [Min(5f)] public float timeLimit = 40f;

        [Tooltip("How many can be in the arena at once. The rest queue behind them, so " +
                 "this is the difference between a crowd and a procession.")]
        [Min(1)] public int maxAliveAtOnce = 6;

        [Tooltip("Seconds between arrivals while the level is filling up.")]
        [Min(0f)] public float spawnInterval = 0.5f;

        [Tooltip("Seconds of read-the-briefing before the clock starts. The clock must " +
                 "not be running while the player is still reading what the level is.")]
        [Min(0f)] public float briefingTime = 3f;

        [Header("Objective")]
        [Tooltip("What this level asks for beyond the roster. See the enum: none of these " +
                 "may change what ends a level, only what scores.")]
        public Objective objective = Objective.Clear;

        [Tooltip("What the objective is worth against one ordinary enemy, on the same " +
                 "scale bossWeight uses. It is added to the level's total, so a level " +
                 "with an objective has more to earn and the roster alone is no longer " +
                 "three stars -- which is the point of having one.")]
        [Min(0f)] public float objectiveWeight = 6f;

        [Header("Boss")]
        [Tooltip("Adds a boss on top of the count above. It arrives with the level rather " +
                 "than at the end, so the fight is the crowd and the boss together.")]
        public bool hasBoss;

        [Tooltip("Which boss. Empty falls back to whatever Boss-role archetype the " +
                 "roster offers, which is only ever a fallback -- a level that says it " +
                 "has a boss and then has none is the worst of both.")]
        public EnemyArchetype bossArchetype;

        [Tooltip("What the boss is worth against one ordinary enemy when the stars are " +
                 "counted. At 5 it is a fifth of a twenty-enemy level on its own, which " +
                 "is the point: the boss is the level, and leaving it alive costs a star " +
                 "however much of the escort was cleared.")]
        [Min(1f)] public float bossWeight = 5f;

        [Tooltip("Extra health on the boss, on top of its archetype. Later bosses are " +
                 "meant to be walls rather than the first one wearing a bigger number.")]
        [Min(0.1f)] public float bossHealthMultiplier = 1f;

        [Header("Difficulty")]
        [Tooltip("How hard the enemies themselves are. These multiply the archetype, " +
                 "which is what keeps a level's difficulty a level's business.")]
        [Min(0.1f)] public float healthMultiplier = 1f;

        [Min(0.1f)] public float damageMultiplier = 1f;

        [Tooltip("Capped against the player's walk on purpose -- see the combat rules in " +
                 "CLAUDE.md. Disengaging has to stay possible.")]
        [Min(0.1f)] public float speedMultiplier = 1f;

        [Range(0f, 1f)]
        [Tooltip("How aggressive the enemies are tuned: tighter cooldowns, harder " +
                 "circling, straighter shooting.")]
        public float aggression = 0.15f;

        [Tooltip("Which archetypes are in the mix. The roster gates its variants by " +
                 "unlock step, and this is the number they are measured against -- so a " +
                 "step of 1 is the opening roster and a step of 10 is all of it.")]
        [Min(1)] public int rosterStep = 1;

        [Header("Stars")]
        [Tooltip("Fraction of the level's weight that has to be killed for two stars. " +
                 "Three is only ever awarded for clearing the level outright.")]
        [Range(0.1f, 1f)] public float twoStarScore = 0.8f;

        [Tooltip("And for one star, which is also the pass mark: below this the level " +
                 "is failed and stays locked.")]
        [Range(0.05f, 1f)] public float oneStarScore = 0.5f;

        /// <summary>The name to print, given the level's position in the set.</summary>
        public string Label(int index)
            => string.IsNullOrWhiteSpace(displayName) ? $"LEVEL {index + 1}" : displayName;

        /// <summary>
        /// Everything that has to die, counted the way the stars count it: each ordinary
        /// enemy is worth one and the boss is worth <see cref="bossWeight"/>.
        /// </summary>
        public float TotalWeight => enemyCount + (hasBoss ? bossWeight : 0f) + ObjectiveWeight;

        /// <summary>
        /// What the objective adds to the level's weight, or zero for the objectives
        /// that are modifiers rather than tasks.
        ///
        /// <b>A modifier must be worth nothing.</b> "No reloading" and "the lights are
        /// off" make the same roster harder; they do not add anything to kill or reach.
        /// Weighting them would mean a player who beat a harder level scored a fraction
        /// of it for free, and -- worse -- that the weight could never be earned at all,
        /// so a perfectly played blackout would cap at two stars.
        /// </summary>
        public float ObjectiveWeight => objective switch
        {
            Objective.Hunt => objectiveWeight,
            Objective.Hold => objectiveWeight,
            Objective.Extraction => objectiveWeight,
            Objective.Disposal => objectiveWeight,
            _ => 0f
        };
    }

    [Tooltip("The arena these levels are played in, as it appears in Build Settings. " +
             "Written by the level generator and used to key stored progress, so two " +
             "arenas never share an unlock chain.")]
    public string arenaScene;

    [TextArea(2, 4)]
    [Tooltip("What the clock actually is in this arena, and what happens to somebody " +
             "still inside when it runs out. Shown under the countdown at the start of " +
             "every level here.\n\n" +
             "It is on the ladder rather than on the campaign because this is the asset " +
             "the arena already holds -- LevelManager reads it directly, so the line " +
             "costs no reference in the scene and no arena has to be rebuilt to change " +
             "it. The campaign generator is what writes it.")]
    public string stakes;

    public List<Level> levels = new List<Level>();

    public int Count => levels != null ? levels.Count : 0;

    /// <summary>The level at an index, or null when the index is off the end.</summary>
    public Level At(int index)
        => levels != null && index >= 0 && index < levels.Count ? levels[index] : null;
}
