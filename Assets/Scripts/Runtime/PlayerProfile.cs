using UnityEngine;

/// <summary>
/// Everything the dashboard knows about the person playing, stored in PlayerPrefs.
///
/// Deliberately a set of static accessors over PlayerPrefs rather than a cached
/// object: there is no state to go stale, nothing to reset between play sessions, and
/// a value written here is on disk before the next scene loads. That matters on the
/// web, where the tab can be closed at any moment and there is no shutdown to hook.
///
/// The keys are prefixed and versioned by name so that adding a stat later never
/// collides with the two <see cref="GameDirector"/> already owns.
/// </summary>
public static class PlayerProfile
{
    const string BestWaveKey = "FPSKit.BestWave";
    const string BestScoreKey = "FPSKit.BestScore";
    const string RunsKey = "FPSKit.Runs";
    const string KillsKey = "FPSKit.TotalKills";
    const string NameKey = "FPSKit.PlayerName";

    /// <summary>Per-arena best wave, so each card can show what you managed there.</summary>
    static string ArenaBestKey(string arena) => $"FPSKit.ArenaBest.{arena}";

    public static int BestWave => PlayerPrefs.GetInt(BestWaveKey, 0);
    public static int BestScore => PlayerPrefs.GetInt(BestScoreKey, 0);
    public static int Runs => PlayerPrefs.GetInt(RunsKey, 0);
    public static int TotalKills => PlayerPrefs.GetInt(KillsKey, 0);

    /// <summary>Best wave reached in one arena. 0 means it has never been played.</summary>
    public static int BestWaveIn(string arena)
        => string.IsNullOrEmpty(arena) ? 0 : PlayerPrefs.GetInt(ArenaBestKey(arena), 0);

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
    /// Folds one finished run into the totals. Called once, from
    /// <see cref="GameSession.RecordRun"/>, so there is a single place a run can be
    /// counted and no way to count it twice.
    /// </summary>
    public static void RecordRun(string arena, int wave, int score, int kills)
    {
        PlayerPrefs.SetInt(RunsKey, Runs + 1);
        PlayerPrefs.SetInt(KillsKey, TotalKills + Mathf.Max(0, kills));

        if (wave > BestWave) PlayerPrefs.SetInt(BestWaveKey, wave);
        if (score > BestScore) PlayerPrefs.SetInt(BestScoreKey, score);

        if (!string.IsNullOrEmpty(arena) && wave > BestWaveIn(arena))
            PlayerPrefs.SetInt(ArenaBestKey(arena), wave);

        PlayerPrefs.Save();
    }

    /// <summary>Wipes the profile. Offered on the dashboard, behind a confirm.</summary>
    public static void Clear(params string[] arenas)
    {
        PlayerPrefs.DeleteKey(BestWaveKey);
        PlayerPrefs.DeleteKey(BestScoreKey);
        PlayerPrefs.DeleteKey(RunsKey);
        PlayerPrefs.DeleteKey(KillsKey);

        if (arenas != null)
            foreach (var arena in arenas)
                if (!string.IsNullOrEmpty(arena)) PlayerPrefs.DeleteKey(ArenaBestKey(arena));

        PlayerPrefs.Save();
    }
}
