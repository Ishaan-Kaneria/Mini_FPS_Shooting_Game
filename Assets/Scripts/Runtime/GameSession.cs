using UnityEngine;

/// <summary>
/// The handful of facts that have to survive a scene change: which arena the player
/// picked, and how the last run went.
///
/// Static because a scene load destroys every object in the old scene, so there is
/// nothing left to hang it off. It is deliberately tiny -- an arena name and a run
/// summary -- because anything larger belongs in <see cref="PlayerProfile"/>, which
/// persists properly, or in the scene that owns it.
///
/// Domain reload is disabled in this project, so the reset hook below is not optional:
/// without it, the second time Play is pressed the dashboard would open already
/// holding the previous session's dead run, and <see cref="ReturnedFromRun"/> would
/// send the player straight to a results panel for a game they never played. See the
/// domain reload section of CLAUDE.md.
/// </summary>
public static class GameSession
{
    /// <summary>How a run ended. The dashboard words its summary from this.</summary>
    public enum Outcome { None, Died, Quit }

    /// <summary>Scene name of the arena the player chose, or empty on a cold start.</summary>
    public static string SelectedArena { get; private set; } = "";

    /// <summary>Display name of that arena, for a results line that reads like English.</summary>
    public static string SelectedArenaLabel { get; private set; } = "";

    public static Outcome LastOutcome { get; private set; } = Outcome.None;
    public static int LastWave { get; private set; }
    public static int LastScore { get; private set; }
    public static int LastKills { get; private set; }

    /// <summary>True when the dashboard is being shown after a run rather than on boot.</summary>
    public static bool ReturnedFromRun => LastOutcome != Outcome.None;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState()
    {
        SelectedArena = "";
        SelectedArenaLabel = "";
        LastOutcome = Outcome.None;
        LastWave = 0;
        LastScore = 0;
        LastKills = 0;
    }

    public static void ChooseArena(string sceneName, string label)
    {
        SelectedArena = sceneName ?? "";
        SelectedArenaLabel = string.IsNullOrEmpty(label) ? SelectedArena : label;
    }

    /// <summary>
    /// Banks how a run ended, on the way back to the dashboard. Also folds the run into
    /// the persistent profile, so the two can never disagree about what just happened.
    /// </summary>
    public static void RecordRun(Outcome outcome, int wave, int score, int kills)
    {
        LastOutcome = outcome;
        LastWave = wave;
        LastScore = score;
        LastKills = kills;

        PlayerProfile.RecordRun(SelectedArena, wave, score, kills);
    }

    /// <summary>Forgets the last result, so re-opening the dashboard is not a replay of it.</summary>
    public static void ClearLastRun()
    {
        LastOutcome = Outcome.None;
        LastWave = 0;
        LastScore = 0;
        LastKills = 0;
    }
}
