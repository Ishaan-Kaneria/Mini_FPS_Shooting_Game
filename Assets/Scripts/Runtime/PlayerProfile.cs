using UnityEngine;

/// <summary>
/// Everything the dashboard knows about the person playing, stored in PlayerPrefs.
///
/// Deliberately a set of static accessors over PlayerPrefs rather than a cached
/// object: there is no state to go stale, nothing to reset between play sessions, and
/// a value written here is on disk before the next scene loads. That matters on the
/// web, where the tab can be closed at any moment and there is no shutdown to hook.
///
/// This holds the totals that are true across every arena. What the player has managed
/// in one *place* is <see cref="LevelProgress"/>, which is keyed by arena and level and
/// is the thing the unlock chain is cut from -- keeping the two apart is what stopped
/// this file growing a per-arena key of its own.
/// </summary>
public static class PlayerProfile
{
    const string BestScoreKey = "FPSKit.BestScore";
    const string RunsKey = "FPSKit.Runs";
    const string KillsKey = "FPSKit.TotalKills";
    const string NameKey = "FPSKit.PlayerName";

    public static int BestScore => PlayerPrefs.GetInt(BestScoreKey, 0);

    /// <summary>Levels attempted, however they ended. A measure of time spent, not skill.</summary>
    public static int Runs => PlayerPrefs.GetInt(RunsKey, 0);

    public static int TotalKills => PlayerPrefs.GetInt(KillsKey, 0);

    /// <summary>
    /// What to call the player. Not asked for anywhere yet -- the dashboard just shows
    /// it -- but it is the one piece of "user information" that is genuinely theirs, so
    /// it is stored rather than hard-coded into the panel.
    /// </summary>
    public static string Name
    {
        get
        {
            string stored = PlayerPrefs.GetString(NameKey, "");
            return string.IsNullOrWhiteSpace(stored) ? "OPERATIVE" : stored;
        }
        set
        {
            PlayerPrefs.SetString(NameKey, value ?? "");
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Folds one finished attempt into the totals. Called once, from
    /// <see cref="GameSession.RecordResult"/>, so there is a single place an attempt can
    /// be counted and no way to count it twice.
    /// </summary>
    public static void RecordRun(int score, int kills)
    {
        PlayerPrefs.SetInt(RunsKey, Runs + 1);
        PlayerPrefs.SetInt(KillsKey, TotalKills + Mathf.Max(0, kills));

        if (score > BestScore) PlayerPrefs.SetInt(BestScoreKey, score);

        PlayerPrefs.Save();
    }

    /// <summary>Wipes the totals. The per-arena ladders are cleared separately.</summary>
    public static void Clear()
    {
        PlayerPrefs.DeleteKey(BestScoreKey);
        PlayerPrefs.DeleteKey(RunsKey);
        PlayerPrefs.DeleteKey(KillsKey);

        PlayerPrefs.Save();
    }
}
