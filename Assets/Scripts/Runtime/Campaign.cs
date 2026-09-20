using UnityEngine;

/// <summary>
/// The story's memory: who the player said they were at first boot, which of the
/// villain's children are down, and which powers the campaign has handed over.
///
/// Written the same way as <see cref="Wallet"/>, <see cref="Loadout"/> and
/// <see cref="LevelProgress"/> -- every member is an accessor straight over PlayerPrefs
/// with nothing cached in a static field. That is deliberate rather than convenient.
/// Domain reload is off in this project, so a cached value would survive into the next
/// play session and go stale, and this is precisely the class nobody would think to
/// clear: a campaign flag left true is a story beat that never plays again. Holding no
/// state means there is nothing to reset, so this has no
/// <c>RuntimeInitializeOnLoadMethod</c> hook and must never grow one.
/// </summary>
public static class Campaign
{
    /// <summary>
    /// Who the player said they were. The game is first-person, so this buys pronouns,
    /// story art and voice rather than a character model -- which is why it is cheap
    /// enough to ask for at all.
    /// </summary>
    public enum Identity { Unset = 0, Man = 1, Woman = 2 }

    // Power ids are permanent keys, exactly like a store item's id: the unlock is filed
    // under the string, so renaming one forgets that the player ever earned it.
    public const string BombPower = "bomb";

    /// <summary>
    /// Total stars needed before the story hands over the first bomb.
    ///
    /// Eight levels at three stars is 24 per arena and three arenas are open at the
    /// start, so this is a quarter of what is on the table -- enough that the player has
    /// played all three and not so much that they have to finish any of them.
    ///
    /// This lives here only until the children exist. Every gate in the campaign belongs
    /// on a <c>CampaignData</c> asset with the rest of the ladder, for the same reason
    /// the level clocks live on a <c>LevelSet</c> and the prices on a
    /// <c>StoreCatalog</c>: a number that decides pacing is content, not code.
    /// </summary>
    public const int BombStarGate = 18;

    const string IdentityKey = "FPSKit.Campaign.Identity";
    const string PowerPrefix  = "FPSKit.Campaign.Power.";
    const string ChildPrefix  = "FPSKit.Campaign.Child.";

    static string PowerKey(string id) => PowerPrefix + id;
    static string ChildKey(int index) => ChildPrefix + index;

    // ---- identity -------------------------------------------------------------

    /// <summary>Who the player chose to be, or <c>Unset</c> until the opening asks.</summary>
    public static Identity Who
    {
        get => (Identity)PlayerPrefs.GetInt(IdentityKey, (int)Identity.Unset);
        set
        {
            PlayerPrefs.SetInt(IdentityKey, (int)value);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// False only on a genuinely fresh profile. This is what decides whether the opening
    /// plays, so it must not be confused with "has no stars": somebody who played before
    /// the story existed has progress and no identity, and they get asked once.
    /// </summary>
    public static bool HasIdentity => Who != Identity.Unset;

    /// <summary>"he", "she", or "they" while the game has not been told.</summary>
    public static string Subject => Who switch
    {
        Identity.Man => "he",
        Identity.Woman => "she",
        _ => "they"
    };

    /// <summary>"his", "her", or "their".</summary>
    public static string Possessive => Who switch
    {
        Identity.Man => "his",
        Identity.Woman => "her",
        _ => "their"
    };

    /// <summary>"him", "her", or "them".</summary>
    public static string Object_ => Who switch
    {
        Identity.Man => "him",
        Identity.Woman => "her",
        _ => "them"
    };

    /// <summary>What the opening calls the player as a child: "boy", "girl", "child".</summary>
    public static string ChildNoun => Who switch
    {
        Identity.Man => "boy",
        Identity.Woman => "girl",
        _ => "child"
    };

    // ---- powers ---------------------------------------------------------------

    /// <summary>Whether the campaign has handed this power over yet.</summary>
    public static bool HasPower(string id)
        => !string.IsNullOrEmpty(id) && PlayerPrefs.GetInt(PowerKey(id), 0) == 1;

    /// <summary>
    /// Hands a power over. Idempotent, because every caller is allowed to be careless
    /// about how often it runs -- the dashboard refreshes on every return from a level.
    /// Returns true only on the call that actually granted it, which is what a
    /// "you have unlocked..." screen keys off.
    /// </summary>
    public static bool GrantPower(string id)
    {
        if (string.IsNullOrEmpty(id) || HasPower(id)) return false;

        PlayerPrefs.SetInt(PowerKey(id), 1);
        PlayerPrefs.Save();
        return true;
    }

    // ---- the villain's children -----------------------------------------------

    /// <summary>Whether this child has been put down.</summary>
    public static bool ChildDefeated(int index)
        => index >= 0 && PlayerPrefs.GetInt(ChildKey(index), 0) == 1;

    /// <summary>Records a child as beaten. Kept, never taken back, like a best star.</summary>
    public static void RecordChildDefeated(int index)
    {
        if (index < 0) return;

        PlayerPrefs.SetInt(ChildKey(index), 1);
        PlayerPrefs.Save();
    }

    /// <summary>How many of <paramref name="total"/> children are down.</summary>
    public static int ChildrenDown(int total)
    {
        int count = 0;
        for (int i = 0; i < total; i++)
            if (ChildDefeated(i)) count++;

        return count;
    }

    // ---- stars across the whole campaign ---------------------------------------

    /// <summary>
    /// Every star the player holds, in every arena. The per-arena ladder is
    /// <see cref="LevelProgress"/>'s business; the campaign gates on the total, because
    /// a child is a reason to go back and finish an arena rather than to grind the one
    /// in front of you.
    /// </summary>
    public static int TotalStars(ArenaCatalog catalog)
    {
        if (catalog == null) return 0;

        int stars = 0;
        foreach (var arena in catalog.arenas)
        {
            if (arena == null || arena.levels == null) continue;
            stars += LevelProgress.StarsInArena(arena.ProgressKey, arena.LevelCount);
        }

        return stars;
    }

    /// <summary>Every star that could be held in the arenas the player can reach.</summary>
    public static int MaxStars(ArenaCatalog catalog)
    {
        if (catalog == null) return 0;

        int stars = 0;
        foreach (var arena in catalog.arenas)
        {
            if (arena == null || arena.levels == null) continue;
            stars += arena.LevelCount * 3;
        }

        return stars;
    }

    // ---- the one gate ----------------------------------------------------------

    /// <summary>
    /// Brings star-gated unlocks up to date and reports the id of anything granted on
    /// this call, or null. Called on every return to the dashboard, so it has to be
    /// cheap and idempotent, and it is the only thing that may hand a power over on the
    /// strength of a star count -- the same rule <see cref="LevelProgress.IsUnlocked"/>
    /// follows, and for the same reason: a second opinion about whether something is
    /// unlocked is a second answer.
    /// </summary>
    public static string RefreshUnlocks(ArenaCatalog arenas, StoreCatalog store)
    {
        if (arenas == null) return null;
        if (TotalStars(arenas) < BombStarGate) return null;
        if (!GrantPower(BombPower)) return null;

        // The bomb the story hands over is named by the catalogue rather than here, so
        // that which explosive the player is given stays a content decision.
        var bomb = store != null ? store.StoryBomb : null;
        if (bomb != null)
        {
            Loadout.GrantBomb(bomb.id);
            Loadout.SelectBomb(bomb.id);
        }

        return BombPower;
    }

    /// <summary>Wipes the campaign. Sits beside <see cref="PlayerProfile.Clear"/>.</summary>
    public static void Clear(int children = 8)
    {
        PlayerPrefs.DeleteKey(IdentityKey);
        PlayerPrefs.DeleteKey(PowerKey(BombPower));

        for (int i = 0; i < children; i++)
            PlayerPrefs.DeleteKey(ChildKey(i));

        PlayerPrefs.Save();
    }
}
