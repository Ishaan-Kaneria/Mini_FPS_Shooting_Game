using UnityEngine;

/// <summary>
/// What the dashboard says about a level before it is played: which one is next, how hard
/// it is, and what it is worth. All of it read from the level set and the save, so the
/// CURRENT MISSION card cannot describe a level the game would not actually run.
/// </summary>
public static class Missions
{
    public enum Difficulty { Easy, Medium, Hard }

    /// <summary>
    /// The level to offer next in an arena: the first unlocked level with no stars; if every
    /// unlocked level has some, the first without three; if all are three-starred, the last.
    /// </summary>
    public static int NextLevel(ArenaCatalog.Entry entry)
    {
        if (entry == null || entry.LevelCount <= 0) return 0;
        string key = entry.ProgressKey;
        int count = entry.LevelCount;
        for (int i = 0; i < count; i++)
            if (LevelProgress.IsUnlocked(key, i) && LevelProgress.StarsIn(key, i) == 0) return i;
        for (int i = 0; i < count; i++)
            if (LevelProgress.IsUnlocked(key, i) && LevelProgress.StarsIn(key, i) < 3) return i;
        return count - 1;
    }

    /// <summary>
    /// A level's difficulty from its roster step -- the single number FPSKitLevels drives
    /// the whole difficulty curve with, already offset by the arena's place in the campaign.
    /// Up to 5 is the opening roster, 6-10 the local threats, above that everything (a boss
    /// counts one step).
    /// </summary>
    public static Difficulty ForLevel(LevelSet.Level level)
    {
        if (level == null) return Difficulty.Easy;
        int step = level.rosterStep + (level.hasBoss ? 1 : 0);
        return step <= 5 ? Difficulty.Easy : step <= 10 ? Difficulty.Medium : Difficulty.Hard;
    }

    /// <summary>
    /// An arena's rating, by where it sits in the campaign: the first two zones Easy, the next
    /// two Medium, the rest Hard. The ladders inside every arena climb from easy to hard, so the
    /// thing that tells arenas apart is where the curve starts -- which FPSKitLevels sets from
    /// the same campaign position. Outside the campaign, the middle of the ladder decides.
    /// </summary>
    public static Difficulty ForArena(ArenaCatalog.Entry entry, CampaignData campaign)
    {
        int zone = campaign != null && entry != null ? campaign.IndexOfArena(entry.ProgressKey) : -1;
        if (zone >= 0) return zone < 2 ? Difficulty.Easy : zone < 4 ? Difficulty.Medium : Difficulty.Hard;
        var set = entry != null ? entry.levels : null;
        if (set == null || set.Count == 0) return Difficulty.Easy;
        return ForLevel(set.levels[set.Count / 2]);
    }

    public static string Label(Difficulty d) => d.ToString().ToUpperInvariant();

    public static Color ColorFor(UITheme t, Difficulty d) => d switch
    {
        Difficulty.Easy => t.success,
        Difficulty.Medium => t.info,
        _ => t.danger,
    };

    /// <summary>
    /// The most coins a level can pay, at the rates the arena's director pays them: every
    /// kill, and every star not yet earned (stars pay once, as the ladder records the best).
    /// Headshots and score bonuses are left out, so it is a promise the level can keep.
    /// </summary>
    public static int CoinsAvailable(ArenaCatalog.Entry entry, int index)
    {
        var level = LevelAt(entry, index);
        if (level == null) return 0;
        int kills = level.enemyCount + (level.hasBoss ? 1 : 0);
        return kills * GameDirector.DefaultCoinsPerKill + StarsAvailable(entry, index) * GameDirector.DefaultCoinsPerStar;
    }

    public static int StarsAvailable(ArenaCatalog.Entry entry, int index)
        => entry == null ? 0 : Mathf.Max(0, 3 - LevelProgress.StarsIn(entry.ProgressKey, index));

    public static LevelSet.Level LevelAt(ArenaCatalog.Entry entry, int index)
    {
        var set = entry != null ? entry.levels : null;
        if (set == null || index < 0 || index >= set.Count) return null;
        return set.levels[index];
    }

    /// <summary>What a level's objective is called on a card: "HOLD THE GROUND", "BLACKOUT".</summary>
    public static string ObjectiveTitle(LevelSet.Objective o) => o switch
    {
        LevelSet.Objective.OneMagazine => "ONE MAGAZINE",
        LevelSet.Objective.Hunt => "HUNT",
        LevelSet.Objective.Hold => "HOLD THE GROUND",
        LevelSet.Objective.Blackout => "BLACKOUT",
        LevelSet.Objective.Extraction => "EXTRACTION",
        LevelSet.Objective.Disposal => "DISPOSAL",
        _ => "CLEAR THE ARENA",
    };

    // ---- the three stars, in words ------------------------------------------------

    /// <summary>
    /// What each star asks for on this level, one line per star, read off the same numbers
    /// <see cref="LevelResult.StarsFor"/> cuts with -- so the card that promises a star and
    /// the screen that awards it cannot disagree.
    ///
    /// One and two stars are a share of the level's <i>weight</i>, not of its head count:
    /// the boss and any objective are worth more than one enemy, which
    /// <see cref="WeightNote"/> spells out underneath. Three is all of it, and the task, before
    /// the clock -- the one result a fraction cannot buy.
    /// </summary>
    public static string[] StarConditions(LevelSet.Level level)
    {
        if (level == null) return new[] { "", "", "" };
        string everything = UIText.Row(
            $"KILL ALL {level.enemyCount}",
            level.hasBoss ? "THE BOSS" : "",
            level.ObjectiveWeight > 0f ? ObjectiveTask(level.objective) : "");
        return new[]
        {
            $"DOWN {Percent(level.oneStarScore)} OF THE LEVEL",
            $"DOWN {Percent(level.twoStarScore)} OF THE LEVEL",
            everything.Replace(UIText.Separator, ", ") + " IN TIME",
        };
    }

    /// <summary>
    /// How the level is weighed, when it is not simply a head count: "EACH ENEMY 1 · BOSS 6 ·
    /// OBJECTIVE 5". Empty for a plain clear, where the percentages already mean enemies.
    /// </summary>
    public static string WeightNote(LevelSet.Level level)
    {
        if (level == null || (!level.hasBoss && level.ObjectiveWeight <= 0f)) return "";
        return UIText.Row("EACH ENEMY 1",
                          level.hasBoss ? $"BOSS {level.bossWeight:0.#}" : "",
                          level.ObjectiveWeight > 0f ? $"{ObjectiveTask(level.objective)} {level.ObjectiveWeight:0.#}" : "");
    }

    /// <summary>The task a weighted objective adds, as a short imperative.</summary>
    public static string ObjectiveTask(LevelSet.Objective o) => o switch
    {
        LevelSet.Objective.Hunt => "CATCH THE RUNNER",
        LevelSet.Objective.Hold => "HOLD THE GROUND",
        LevelSet.Objective.Extraction => "REACH THE WAY OUT",
        LevelSet.Objective.Disposal => "REACH A CHARGE",
        _ => "",
    };

    static string Percent(float fraction) => Mathf.RoundToInt(fraction * 100f) + "%";
}
