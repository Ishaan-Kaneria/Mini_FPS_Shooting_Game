using UnityEngine;

/// <summary>
/// The story's memory: who the player said they were at first boot, which of the
/// villain's children are down, which powers the campaign has handed over, and which
/// beats have already been read.
///
/// Written the same way as <see cref="Wallet"/>, <see cref="Loadout"/> and
/// <see cref="LevelProgress"/> -- every member is an accessor straight over PlayerPrefs
/// with nothing cached in a static field. That is deliberate rather than convenient.
/// Domain reload is off in this project, so a cached value would survive into the next
/// play session and go stale, and this is precisely the class nobody would think to
/// clear: a campaign flag left true is a story beat that never plays again. Holding no
/// state means there is nothing to reset, so this has no
/// <c>RuntimeInitializeOnLoadMethod</c> hook and must never grow one.
///
/// <b>The ladder itself is not here.</b> Which zones there are, what order they come in
/// and what each one hands over live on <see cref="CampaignData"/>, for the same reason
/// the level clocks live on a <see cref="LevelSet"/>: a number that decides pacing is
/// content. What is left here is the player's side of it -- what they have done -- and
/// the two rules that read the two together.
/// </summary>
public static class Campaign
{
    /// <summary>
    /// Who the player said they were. The game is first-person, so this buys pronouns,
    /// story art and voice rather than a character model -- which is why it is cheap
    /// enough to ask for at all.
    /// </summary>
    /// <summary>
    /// <c>Unstated</c> is not the same as <c>Unset</c>, and the difference is the whole
    /// reason it exists: unset means the game has not asked yet, so the opening plays;
    /// unstated means the player was asked and declined, so it must never ask again.
    /// Collapsing the two would make "rather not say" a button that reopens the same
    /// screen on the next boot, which reads as the answer not having been taken.
    /// </summary>
    public enum Identity { Unset = 0, Man = 1, Woman = 2, Unstated = 3 }

    // Power ids are permanent keys, exactly like a store item's id: the unlock is filed
    // under the string, so renaming one forgets that the player ever earned it.
    public const string BombPower = "bomb";

    const string IdentityKey = "FPSKit.Campaign.Identity";
    const string PowerPrefix  = "FPSKit.Campaign.Power.";
    const string BeatPrefix   = "FPSKit.Campaign.Beat.";
    const string OpenedPrefix = "FPSKit.Campaign.Opened.";

    static string PowerKey(string id) => PowerPrefix + id;
    static string BeatKey(int index) => BeatPrefix + index;
    static string OpenedKey(int index) => OpenedPrefix + index;

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

    /// <summary>
    /// Fills the pronoun tokens in a piece of story text.
    ///
    /// <b>This is what the opening's question is for.</b> The game asks exactly one thing
    /// about the player and then has to spend it somewhere, or the question was a screen
    /// that changed nothing -- which is worse than not asking. The beats are written in
    /// second person, so what the tokens buy is the handful of places somebody else is
    /// talking *about* the player: a word for what they were at eleven, and the pronouns
    /// the will was written with.
    ///
    /// Unknown tokens are left alone rather than blanked. A line that loses a word is a
    /// line nobody can tell is broken; a line with <c>{whatever}</c> still in it says
    /// exactly what went wrong and where.
    /// </summary>
    public static string Expand(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;

        return text
            .Replace("{child}", ChildNoun)
            .Replace("{Child}", Capitalise(ChildNoun))
            .Replace("{they}", Subject)
            .Replace("{They}", Capitalise(Subject))
            .Replace("{their}", Possessive)
            .Replace("{Their}", Capitalise(Possessive))
            .Replace("{them}", Object_)
            .Replace("{Them}", Capitalise(Object_));
    }

    static string Capitalise(string word)
        => string.IsNullOrEmpty(word) ? word : char.ToUpperInvariant(word[0]) + word.Substring(1);

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

    /// <summary>
    /// Whether the child holding this zone is down.
    ///
    /// <b>Derived from the player's stars, never stored.</b> A child is the last level of
    /// their zone, so "is this child down" and "has that level been passed" are the same
    /// question, and storing a second answer to it is how the two come to disagree -- a
    /// flag written on one ending and not another, or written and then lost when a save
    /// is cleared, leaves a campaign that thinks a sibling is dead and a ladder that
    /// thinks they are alive. Same reasoning as <see cref="Achievements"/>, which reads
    /// thresholds against the live counters rather than recording that one was met.
    ///
    /// The cost is that this is only as good as <see cref="LevelProgress"/>, which is the
    /// point: there is one record of what the player has beaten.
    /// </summary>
    public static bool ChildDefeated(CampaignData data, int zoneIndex)
    {
        var zone = data != null ? data.ZoneAt(zoneIndex) : null;
        if (zone == null || zone.siblingIndex < 0 || zone.FinalLevelIndex < 0) return false;

        return LevelProgress.StarsIn(zone.ProgressKey, zone.FinalLevelIndex) > 0;
    }

    /// <summary>How many of the campaign's children are down.</summary>
    public static int ChildrenDown(CampaignData data)
    {
        if (data == null) return 0;

        int count = 0;
        for (int i = 0; i < data.ZoneCount; i++)
            if (ChildDefeated(data, i)) count++;

        return count;
    }

    // ---- the beats already read -------------------------------------------------

    /// <summary>
    /// Whether this zone's closing beat has already been shown.
    ///
    /// This is the one thing here that genuinely has to be stored: whether a child is
    /// down is a fact about the world and can be derived, but whether the player has
    /// *read about it* is a fact about the player and nothing else knows it. Kept, never
    /// taken back, like a best star.
    /// </summary>
    public static bool BeatSeen(int zoneIndex)
        => zoneIndex >= 0 && PlayerPrefs.GetInt(BeatKey(zoneIndex), 0) == 1;

    /// <summary>Records a beat as read. Returns true only on the call that marked it.</summary>
    public static bool MarkBeatSeen(int zoneIndex)
    {
        if (zoneIndex < 0 || BeatSeen(zoneIndex)) return false;

        PlayerPrefs.SetInt(BeatKey(zoneIndex), 1);
        PlayerPrefs.Save();
        return true;
    }

    /// <summary>
    /// Whether the player has been shown what this zone is, on the way into it.
    ///
    /// Separate from <see cref="BeatSeen"/> because they are opposite ends of a zone: an
    /// opening is read before it is played and a beat after it is won, and a player who
    /// has done one has not necessarily done the other -- somebody can walk into a zone,
    /// read the opening, lose every level and never earn the beat.
    /// </summary>
    public static bool OpeningSeen(int zoneIndex)
        => zoneIndex >= 0 && PlayerPrefs.GetInt(OpenedKey(zoneIndex), 0) == 1;

    /// <summary>Records an opening as read. True only on the call that marked it.</summary>
    public static bool MarkOpeningSeen(int zoneIndex)
    {
        if (zoneIndex < 0 || OpeningSeen(zoneIndex)) return false;

        PlayerPrefs.SetInt(OpenedKey(zoneIndex), 1);
        PlayerPrefs.Save();
        return true;
    }

    // ---- the gate ----------------------------------------------------------------

    /// <summary>
    /// Whether the player may enter a zone.
    ///
    /// <b>One line, one place</b>, exactly like <see cref="LevelProgress.IsUnlocked"/>
    /// and for the same reason: the dashboard, the launcher and anything built later all
    /// have to ask the same method, because a second opinion about whether something is
    /// unlocked is a second answer. The first zone is always open; every zone after it
    /// needs the child of the zone before it to be down.
    ///
    /// A zone the campaign does not list, or a campaign asset that is missing entirely,
    /// is <b>open</b>. That is deliberate: this kit is meant to run in arenas it did not
    /// build, and a gate that fails closed would make a project with no campaign asset a
    /// game with one playable arena and no way to find out why.
    /// </summary>
    public static bool ZoneUnlocked(CampaignData data, int zoneIndex)
    {
        if (data == null || zoneIndex <= 0) return true;
        if (zoneIndex >= data.ZoneCount) return true;

        // A zone nobody holds cannot gate the one after it -- otherwise a zone added
        // without a sibling would seal the rest of the campaign behind a fight that
        // does not exist.
        var previous = data.ZoneAt(zoneIndex - 1);
        if (previous == null || previous.siblingIndex < 0) return ZoneUnlocked(data, zoneIndex - 1);

        return ChildDefeated(data, zoneIndex - 1);
    }

    /// <summary>Whether the arena filed under this progress key may be entered.</summary>
    public static bool ArenaUnlocked(CampaignData data, string progressKey)
    {
        if (data == null) return true;

        int index = data.IndexOfArena(progressKey);
        return index < 0 || ZoneUnlocked(data, index);
    }

    /// <summary>
    /// What the player has to do to open a locked zone, in the words the card shows.
    /// Empty when the zone is already open.
    /// </summary>
    public static string LockNote(CampaignData data, string progressKey)
    {
        if (data == null) return "";

        int index = data.IndexOfArena(progressKey);
        if (index <= 0 || ZoneUnlocked(data, index)) return "";

        var previous = data.ZoneAt(index - 1);
        var holder = data.HolderOf(previous);

        string where = previous != null && !string.IsNullOrWhiteSpace(previous.displayName)
            ? previous.displayName.ToUpperInvariant()
            : "THE ZONE BEFORE";

        return holder != null && !string.IsNullOrWhiteSpace(holder.displayName)
            ? $"LOCKED  -  FINISH {where}"
            : $"LOCKED  -  CLEAR {where}";
    }

    // ---- stars across the whole campaign ---------------------------------------

    /// <summary>
    /// Every star the player holds, in every arena. The per-arena ladder is
    /// <see cref="LevelProgress"/>'s business; this is the total, which the dashboard
    /// reports and the achievements are measured against.
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

    // ---- the one place a power may be granted -----------------------------------

    /// <summary>
    /// Brings the campaign's unlocks up to date and reports the id of anything granted
    /// on this call, or null.
    ///
    /// Called on every return to the dashboard, so it has to be cheap and idempotent,
    /// and it is the only thing that may hand a power over -- the same rule
    /// <see cref="LevelProgress.IsUnlocked"/> follows, and for the same reason.
    ///
    /// It walks the zones rather than counting stars, because the campaign is a sequence
    /// now: a power is what a child was carrying, and it is handed over when that child
    /// goes down. A run of zones is walked rather than only the last one finished, so a
    /// profile restored from elsewhere, or one whose grant was interrupted by a closed
    /// tab, catches up on the next visit instead of losing the power for good.
    /// </summary>
    public static string RefreshUnlocks(CampaignData data, ArenaCatalog arenas, StoreCatalog store)
    {
        if (data == null) return null;

        string granted = null;

        for (int i = 0; i < data.ZoneCount; i++)
        {
            var zone = data.ZoneAt(i);
            if (zone == null || string.IsNullOrWhiteSpace(zone.grantsPower)) continue;
            if (!ChildDefeated(data, i)) continue;

            if (!GrantPower(zone.grantsPower)) continue;

            // The bomb the story hands over is named by the catalogue rather than here,
            // so that which explosive the player is given stays a content decision.
            if (zone.grantsPower == BombPower && store != null)
            {
                var bomb = store.StoryBomb;
                if (bomb != null)
                {
                    Loadout.GrantBomb(bomb.id);
                    Loadout.SelectBomb(bomb.id);
                }
            }

            granted ??= zone.grantsPower;
        }

        return granted;
    }

    /// <summary>
    /// Wipes the campaign. Sits beside <see cref="PlayerProfile.Clear"/>, and is a
    /// subset of what <see cref="SaveMigration"/> does on a version change.
    /// </summary>
    public static void Clear(int zones = 8)
    {
        PlayerPrefs.DeleteKey(IdentityKey);
        PlayerPrefs.DeleteKey(PowerKey(BombPower));

        for (int i = 0; i < zones; i++)
        {
            PlayerPrefs.DeleteKey(BeatKey(i));
            PlayerPrefs.DeleteKey(OpenedKey(i));
        }

        PlayerPrefs.Save();
    }
}
