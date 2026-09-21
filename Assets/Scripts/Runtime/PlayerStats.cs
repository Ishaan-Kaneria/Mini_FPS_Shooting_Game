using UnityEngine;

/// <summary>
/// The lifetime tally every achievement is measured against.
///
/// <see cref="PlayerProfile"/> already kept three numbers -- best score, runs, total kills
/// -- which is enough for a dashboard strip and nowhere near enough to say whether somebody
/// has landed a hundred headshots or cleared a level without being touched. This is the
/// rest of it.
///
/// <b>Static accessors straight over PlayerPrefs, and deliberately no cached state</b>, so
/// this needs no <c>SubsystemRegistration</c> hook -- the same reasoning as
/// <see cref="Wallet"/>, <see cref="Loadout"/> and <see cref="LevelProgress"/>. Domain
/// reload is off, so a cached total would survive into the next play session and be wrong,
/// and it would be one more thing to remember to clear. There is nothing here to go stale.
///
/// <b>Every counter is fed from one place</b> -- <see cref="GameSession.RecordResult"/>,
/// which every ending already routes through. Counting at the moment of the kill instead
/// would pay a player who farms a level and quits, and would need a second call site for
/// each ending; this way a new ending cannot silently fail to count.
/// </summary>
public static class PlayerStats
{
    const string P = "stats.";

    static int Get(string key) => PlayerPrefs.GetInt(P + key, 0);

    static void Add(string key, int amount)
    {
        if (amount <= 0) return;
        PlayerPrefs.SetInt(P + key, Get(key) + amount);
    }

    static void Raise(string key, int value)
    {
        if (value > Get(key)) PlayerPrefs.SetInt(P + key, value);
    }

    // ---- combat ----
    public static int Headshots      => Get("headshots");
    public static int BombKills      => Get("bombKills");
    public static int BestCombo      => Get("bestCombo");
    public static int BossesKilled   => Get("bosses");

    // ---- progression ----
    public static int LevelsCleared  => Get("cleared");
    public static int ThreeStarLevels=> Get("threeStar");
    public static int ArenasFinished => Get("arenasDone");

    /// <summary>
    /// Stars across every arena.
    ///
    /// Derived and cached rather than incremented, because stars are not additive:
    /// LevelProgress.Record keeps the best of a level's attempts, so replaying a
    /// two-star level for three adds one star and not three. Refreshed by
    /// <see cref="RefreshFromCatalog"/>, which the dashboard calls on the way in --
    /// which is also the only place these are read.
    /// </summary>
    public static int TotalStars => Get("stars");

    // ---- challenge ----
    public static int FlawlessClears => Get("flawless");
    public static int FastClears     => Get("fast");

    // ---- economy ----
    public static int CoinsEarned    => Get("coinsLifetime");
    public static int ItemsBought    => Get("bought");

    /// <summary>A purchase happened. The one counter the store owns rather than a level.</summary>
    public static void RecordPurchase()
    {
        Add("bought", 1);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Folds one finished level into the lifetime totals.
    ///
    /// <paramref name="flawless"/> and <paramref name="fast"/> are passed in rather than
    /// worked out here because both are judgements about how the level was played that
    /// only the level knows: what counts as untouched, and what counts as time to spare.
    /// </summary>
    public static void RecordRun(LevelResult result, bool flawless, bool fast)
    {
        Add("headshots", result.headshots);
        Add("bombKills", result.bombKills);
        Add("coinsLifetime", result.coins);
        Raise("bestCombo", result.bestCombo);

        if (result.bossKilled) Add("bosses", 1);

        // Cleared means every enemy the level asked for, which is the same bar three
        // stars is held to -- see LevelResult.StarsFor. A level survived on the clock is
        // not a clear and must not count as one.
        bool cleared = result.killed >= result.total && result.total > 0;
        if (cleared)
        {
            Add("cleared", 1);
            if (flawless) Add("flawless", 1);
            if (fast) Add("fast", 1);
        }

        if (result.stars >= 3) Add("threeStar", 1);

        PlayerPrefs.Save();
    }

    /// <summary>
    /// Recomputes the two totals that cannot be counted as they happen.
    ///
    /// Both are properties of the whole ladder rather than of one level: an arena is
    /// finished or it is not, and stars are the best of each level's attempts rather than
    /// the sum of them. Incrementing either would double-count a replay.
    /// </summary>
    public static void RefreshFromCatalog(ArenaCatalog catalog)
    {
        if (catalog == null) return;

        int done = 0;
        int stars = 0;

        foreach (var entry in catalog.arenas)
        {
            int count = entry.LevelCount;
            if (count <= 0) continue;

            stars += LevelProgress.StarsInArena(entry.ProgressKey, count);

            bool all = true;
            for (int i = 0; i < count && all; i++)
                all = LevelProgress.StarsIn(entry.ProgressKey, i) > 0;

            if (all) done++;
        }

        PlayerPrefs.SetInt(P + "arenasDone", done);
        PlayerPrefs.SetInt(P + "stars", stars);
        PlayerPrefs.Save();
    }

    /// <summary>Wipes every lifetime counter. Paired with <see cref="PlayerProfile.Clear"/>.</summary>
    public static void Clear()
    {
        foreach (var key in new[]
                 {
                     "headshots", "bombKills", "bestCombo", "bosses", "cleared",
                     "threeStar", "arenasDone", "stars", "flawless", "fast", "coinsLifetime", "bought",
                 })
            PlayerPrefs.DeleteKey(P + key);

        PlayerPrefs.Save();
    }
}
