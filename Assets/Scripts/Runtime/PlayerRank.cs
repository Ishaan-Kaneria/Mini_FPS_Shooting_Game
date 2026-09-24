using UnityEngine;

/// <summary>
/// The player's rank and XP: derived from what they have done, never stored.
///
/// The same rule <see cref="Achievements"/> follows, for the same reasons. XP read off the
/// lifetime counters cannot disagree with them, cannot be farmed by a bug in whatever would
/// have awarded it, and is retroactive for free -- a player with three hundred kills is
/// ranked for them the first time this screen opens.
///
/// <b>What earns XP</b>, each for a reason a player can see:
///   * 10 per kill -- the thing the game is made of;
///   * 100 per star -- a star is progress through the campaign, and the ladder is what
///     the game is for;
///   * 250 per boss -- a boss is most of a level's weight already (LevelResult.StarsFor);
///   * 50 per achievement.
///
/// <b>Ranks</b> are a gentle curve: rank n needs 500 x n x (n-1) / 2 XP in total, so each
/// rank costs 500 more than the one before. Early ranks come within a level or two; later
/// ones take an arena.
///
/// <b>Nothing unlocks at a rank yet.</b> <see cref="UnlockAt"/> is where one would be said,
/// and the dashboard's NEXT REWARD falls back to the next thing in the store while it says
/// nothing -- rather than promising a reward that does not exist.
/// </summary>
public static class PlayerRank
{
    public const int XpPerKill = 10;
    public const int XpPerStar = 100;
    public const int XpPerBoss = 250;
    public const int XpPerAchievement = 50;
    const int Step = 500;

    static readonly string[] Titles =
    {
        "Recruit", "Private", "Specialist", "Corporal", "Sergeant", "Staff Sergeant",
        "Lieutenant", "Captain", "Major", "Colonel", "Commander",
    };

    /// <summary>Total XP, from the lifetime counters.</summary>
    public static int Xp =>
        PlayerProfile.TotalKills * XpPerKill +
        PlayerStats.TotalStars * XpPerStar +
        PlayerStats.BossesKilled * XpPerBoss +
        Achievements.EarnedCount * XpPerAchievement;

    /// <summary>XP needed in total to reach a rank. Rank 1 needs none.</summary>
    public static int XpFor(int rank) => rank <= 1 ? 0 : Step * rank * (rank - 1) / 2;

    public static int RankFor(int xp)
    {
        int rank = 1;
        while (XpFor(rank + 1) <= xp && rank < 99) rank++;
        return rank;
    }

    public static int Rank => RankFor(Xp);

    public static string TitleFor(int rank) => Titles[Mathf.Clamp(rank - 1, 0, Titles.Length - 1)];

    /// <summary>XP into the current rank and XP the current rank spans, for a progress bar.</summary>
    public static void Progress(out int into, out int span)
    {
        int xp = Xp;
        int rank = RankFor(xp);
        into = xp - XpFor(rank);
        span = XpFor(rank + 1) - XpFor(rank);
    }

    /// <summary>What reaching a rank hands over, or null. Nothing does yet.</summary>
    public static string UnlockAt(int rank) => null;
}
