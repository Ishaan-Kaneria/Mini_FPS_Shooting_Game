using UnityEngine;

/// <summary>
/// How one level went, decided once and then read by everything that has to react to
/// it -- the results screen, the stored progress, the dashboard strip.
///
/// A struct rather than a class, and plain values rather than references, because it
/// outlives the level it describes: it is banked before the results screen is shown
/// and read again on the dashboard after the arena scene has been unloaded.
/// </summary>
public struct LevelResult
{
    /// <summary>What ended the level. The results screen words itself from this.</summary>
    public enum Ending
    {
        /// <summary>Every enemy, and the boss, dead before the clock ran out.</summary>
        Cleared,

        /// <summary>The clock beat the player. Scored on whatever was killed by then.</summary>
        TimeUp,

        /// <summary>The player died. Scored the same way, which is usually a failure.</summary>
        Died,

        /// <summary>
        /// The player left mid-level. It still counts as an attempt in the profile, and
        /// it still cannot unlock anything -- walking out is not a way past a level.
        /// </summary>
        Abandoned
    }

    public string arena;
    public int levelIndex;
    public string levelName;

    public Ending ending;

    /// <summary>0 to 3. Zero is the failure: the level stays locked and has to be replayed.</summary>
    public int stars;

    public int killed;
    public int total;

    /// <summary>
    /// The fraction of the level's weight that was killed, which is what the stars are
    /// actually cut from. Weighted rather than counted, so the boss is worth what the
    /// level says it is worth -- see <see cref="LevelSet.Level.bossWeight"/>.
    /// </summary>
    public float scoreFraction;

    public bool bossKilled;
    public bool hadBoss;

    public float timeTaken;
    public float timeLimit;

    public int score;
    public int headshots;

    /// <summary>Coins this level paid, kills plus stars plus score. Banked by GameSession.</summary>
    public int coins;

    /// <summary>A level with a star is a level passed, and the next one opens.</summary>
    public bool Passed => stars > 0;

    public float TimeRemaining => Mathf.Max(0f, timeLimit - timeTaken);

    /// <summary>
    /// Cuts a weighted score into stars.
    ///
    /// Three is only ever given for clearing the level outright, not for a fraction
    /// that rounds up to one: "kill them all" has to mean all of them, or the top of
    /// the scoreboard is something a player can stumble into.
    /// </summary>
    public static int StarsFor(float fraction, bool cleared, LevelSet.Level level)
    {
        if (cleared) return 3;
        if (level == null) return fraction >= 0.999f ? 3 : fraction >= 0.8f ? 2 : fraction >= 0.5f ? 1 : 0;

        if (fraction >= level.twoStarScore) return 2;
        if (fraction >= level.oneStarScore) return 1;

        return 0;
    }

    /// <summary>The headline on the results screen.</summary>
    public string Title => ending switch
    {
        Ending.Cleared => "LEVEL CLEARED",
        Ending.Died => "YOU WERE KILLED",
        Ending.Abandoned => "LEVEL ABANDONED",
        _ => Passed ? "TIME UP" : "OUT OF TIME"
    };

    /// <summary>One line of plain English under it.</summary>
    public string Summary
    {
        get
        {
            if (ending == Ending.Cleared)
                return $"All {total} down with {TimeRemaining:0}s to spare";

            if (ending == Ending.Abandoned)
                return $"Left after {killed} of {total}";

            string missed = hadBoss && !bossKilled
                ? "the boss is still standing"
                : $"{Mathf.Max(0, total - killed)} still standing";

            return Passed
                ? $"{killed} of {total} down, but {missed}"
                : $"Only {killed} of {total} down - try again";
        }
    }
}
