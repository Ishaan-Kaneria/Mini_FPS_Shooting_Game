using UnityEngine;

/// <summary>
/// The handful of facts that have to survive a scene change: which arena and level the
/// player picked, and how the last attempt went.
///
/// Static because a scene load destroys every object in the old scene, so there is
/// nothing left to hang it off. It is deliberately tiny -- an arena, a level index and
/// a result -- because anything larger belongs in <see cref="PlayerProfile"/>,
/// <see cref="LevelProgress"/> or <see cref="Wallet"/>, which persist properly, or in
/// the scene that owns it.
///
/// Domain reload is disabled in this project, so the reset hook below is not optional:
/// without it, the second time Play is pressed the dashboard would open already
/// holding the previous session's dead run, and <see cref="ReturnedFromRun"/> would
/// send the player straight to a results panel for a level they never played. See the
/// domain reload section of CLAUDE.md.
/// </summary>
public static class GameSession
{
    /// <summary>Scene name of the arena the player chose, or empty on a cold start.</summary>
    public static string SelectedArena { get; private set; } = "";

    /// <summary>Display name of that arena, for a results line that reads like English.</summary>
    public static string SelectedArenaLabel { get; private set; } = "";

    /// <summary>Zero-based level in that arena's <see cref="LevelSet"/>.</summary>
    public static int SelectedLevel { get; private set; }

    /// <summary>
    /// False until a level has actually been chosen. The distinction matters: index 0 is
    /// a real level, so a manager cannot tell "the dashboard picked level one" from
    /// "nobody picked anything" by looking at the number alone -- and the second case is
    /// pressing Play with an arena open, where the manager's own default should win.
    /// </summary>
    public static bool HasSelectedLevel { get; private set; }

    /// <summary>The last finished level, or a default-valued result on a cold start.</summary>
    public static LevelResult LastResult { get; private set; }

    /// <summary>True when the dashboard is being shown after a level rather than on boot.</summary>
    public static bool ReturnedFromRun { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState()
    {
        SelectedArena = "";
        SelectedArenaLabel = "";
        SelectedLevel = 0;
        HasSelectedLevel = false;
        LastResult = default;
        ReturnedFromRun = false;
    }

    public static void ChooseArena(string sceneName, string label)
    {
        SelectedArena = sceneName ?? "";
        SelectedArenaLabel = string.IsNullOrEmpty(label) ? SelectedArena : label;
    }

    /// <summary>
    /// Picks the level to play next. Called by the level select, and again by the
    /// results screen's Retry and Next Level, which are the same act said differently.
    /// </summary>
    public static void ChooseLevel(int index)
    {
        SelectedLevel = Mathf.Max(0, index);
        HasSelectedLevel = true;
    }

    /// <summary>
    /// Banks how a level ended, on the way back to the dashboard or into the results
    /// screen. Also folds it into the persistent stores, so the three can never
    /// disagree about what just happened: the run counts toward the profile, and a
    /// passing result unlocks the next level.
    /// </summary>
    public static void RecordResult(LevelResult result)
    {
        LastResult = result;
        ReturnedFromRun = true;

        PlayerProfile.RecordRun(result.score, result.killed);

        // The lifetime counters every achievement is measured against. Here rather than at
        // the moment of each kill for the reason the coins are here: an ending that pays
        // is an ending that counts, and a new ending cannot be added later that silently
        // does neither.
        //
        // Flawless and fast are judged here because they are judgements about how the level
        // was played rather than facts it reports. Untouched means exactly zero damage --
        // a threshold would make it arguable -- and time to spare means half the clock,
        // which is generous enough to be reachable on a level the player knows and out of
        // reach on one they are seeing for the first time.
        bool flawless = result.damageTaken <= 0f;
        bool fast = result.timeLimit > 0f && result.TimeRemaining >= result.timeLimit * 0.5f;
        PlayerStats.RecordRun(result, flawless, fast);
        LevelProgress.Record(result.arena, result.levelIndex, result.stars, result.score);

        // The one place coins are ever banked. Every ending routes through here --
        // cleared, timed out, died, walked out -- so there is a single line that can pay
        // and no way for an ending to be added later that silently pays nothing.
        Wallet.Add(result.coins);
    }

    /// <summary>Forgets the last result, so re-opening the dashboard is not a replay of it.</summary>
    public static void ClearLastRun()
    {
        LastResult = default;
        ReturnedFromRun = false;
    }
}
