using UnityEngine;

/// <summary>
/// How far the player has got in each arena: the stars earned on every level, and
/// therefore which levels are unlocked.
///
/// Static accessors over PlayerPrefs, for the same reason <see cref="PlayerProfile"/>
/// is: there is no state to go stale, nothing to reset between play sessions, and a
/// star written here is on disk before the results screen has finished animating. That
/// matters on the web, where the tab can be closed at any moment and there is no
/// shutdown to hook.
///
/// The unlock rule is one line and lives here rather than in the menu: level 1 of an
/// arena is always open, and every level after it needs at least one star on the one
/// before. Beating a level therefore opens exactly one door, and a level failed is a
/// level the player has to come back to.
/// </summary>
public static class LevelProgress
{
    /// <summary>Stars stored per arena and level. Absent means never passed.</summary>
    static string StarsKey(string arena, int index) => $"FPSKit.Level.{arena}.{index}.Stars";

    /// <summary>Best score on one level, so a tile can show what beating it was worth.</summary>
    static string ScoreKey(string arena, int index) => $"FPSKit.Level.{arena}.{index}.Score";

    /// <summary>Fastest clear of one level, in seconds. Only a clear is timed.</summary>
    static string TimeKey(string arena, int index) => $"FPSKit.Level.{arena}.{index}.BestTime";

    /// <summary>Stars on one level, 0 through 3. 0 means it has never been passed.</summary>
    public static int StarsIn(string arena, int index)
        => string.IsNullOrEmpty(arena) || index < 0
            ? 0
            : Mathf.Clamp(PlayerPrefs.GetInt(StarsKey(arena, index), 0), 0, 3);

    public static int BestScoreIn(string arena, int index)
        => string.IsNullOrEmpty(arena) || index < 0
            ? 0
            : PlayerPrefs.GetInt(ScoreKey(arena, index), 0);

    /// <summary>Fastest clear in seconds, or 0 when the level has never been cleared.</summary>
    public static float BestTimeIn(string arena, int index)
        => string.IsNullOrEmpty(arena) || index < 0
            ? 0f
            : PlayerPrefs.GetFloat(TimeKey(arena, index), 0f);

    /// <summary>
    /// True when the player is allowed into this level. The first is always open; the
    /// rest need a star on the level before, which is what makes the ladder a ladder.
    ///
    /// <b>A level the player already has stars on is open whatever is below it.</b> Saves
    /// from before the gate held, and saves played with the editor's Unlock All switch,
    /// have stars scattered up a ladder with gaps under them; locking a level somebody has
    /// already beaten would take away something they earned.
    /// </summary>
    public static bool IsUnlocked(string arena, int index)
    {
        if (index <= 0 || DebugUnlock.Active) return true;
        return StarsIn(arena, index - 1) > 0 || StarsIn(arena, index) > 0;
    }

    /// <summary>
    /// Banks a finished level, keeping the best result rather than the latest. Replaying
    /// a level the player already three-starred must never be able to take stars away --
    /// that would make practising a punishment.
    /// </summary>
    public static void Record(string arena, int index, int stars, int score, float clearTime = 0f)
    {
        if (string.IsNullOrEmpty(arena) || index < 0) return;

        stars = Mathf.Clamp(stars, 0, 3);

        if (stars > StarsIn(arena, index)) PlayerPrefs.SetInt(StarsKey(arena, index), stars);
        if (score > BestScoreIn(arena, index)) PlayerPrefs.SetInt(ScoreKey(arena, index), score);

        float best = BestTimeIn(arena, index);
        if (clearTime > 0f && (best <= 0f || clearTime < best)) PlayerPrefs.SetFloat(TimeKey(arena, index), clearTime);

        PlayerPrefs.Save();
    }

    /// <summary>Index of the furthest level the player may enter. 0 on a fresh profile.</summary>
    public static int HighestUnlocked(string arena, int count)
    {
        int highest = 0;

        for (int i = 1; i < count; i++)
        {
            if (!IsUnlocked(arena, i)) break;
            highest = i;
        }

        return highest;
    }

    /// <summary>Stars earned across one arena, for the card on the dashboard.</summary>
    public static int StarsInArena(string arena, int count)
    {
        int total = 0;
        for (int i = 0; i < count; i++) total += StarsIn(arena, i);
        return total;
    }

    /// <summary>Levels of an arena that have been passed at least once.</summary>
    public static int LevelsCleared(string arena, int count)
    {
        int cleared = 0;
        for (int i = 0; i < count; i++) if (StarsIn(arena, i) > 0) cleared++;
        return cleared;
    }
}
