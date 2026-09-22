using UnityEngine;

/// <summary>
/// Clears a saved profile that was written before the campaign existed, once, on the
/// first run of a build that has it.
///
/// <b>Why a wipe rather than a migration.</b> The game used to open all six arenas at
/// once; it now runs them as a sequence, each one gated on the child of the one before.
/// A profile from the old build has stars scattered across arenas in no order, which
/// under the new rule means a player is shown zones opened by fights they never had and
/// a story told out of sequence -- and told wrong, because the beats assume the order.
/// There is no arrangement of the old data that answers "where is this player in the
/// story", so the honest answer is to start the story at the start.
///
/// <b>It is not a reset the player can trigger and it never runs twice.</b> The version
/// key is written immediately, so this is dead code on every run after the first. It is
/// keyed on a version rather than on a bool so a later change can wipe again by raising
/// the number, without inventing a second key nobody remembers to check.
///
/// <b>DeleteAll rather than a list of keys.</b> A list is a thing to keep in sync with
/// six classes that each own their own key format, and the one key left off a list is
/// the one that makes the wipe look like it worked while a stale value survives -- an
/// owned gun with no coins to explain it, a star in an arena the story says is sealed.
/// Everything this game stores is progress; there are no settings to protect.
///
/// Holds no state, so no <c>SubsystemRegistration</c> hook and nothing to reset between
/// play sessions -- the same reasoning as <see cref="Wallet"/> and <see cref="Campaign"/>.
/// The hook below is <c>BeforeSceneLoad</c>, which is a different thing: it is not
/// clearing a static, it is making sure this has happened before anything reads a save.
/// </summary>
public static class SaveMigration
{
    /// <summary>
    /// The save format this build expects. Raise it to wipe every profile again on the
    /// next run -- and only for a change where that is genuinely the right answer,
    /// because to a player it is their progress disappearing.
    /// </summary>
    public const int Version = 2;

    const string VersionKey = "FPSKit.Save.Version";

    /// <summary>What the stored profile was written by. 0 on anything pre-campaign.</summary>
    public static int StoredVersion => PlayerPrefs.GetInt(VersionKey, 0);

    /// <summary>True once the profile on disk is the shape this build expects.</summary>
    public static bool UpToDate => StoredVersion >= Version;

    /// <summary>
    /// Runs before the first scene of every play session, with or without a domain
    /// reload, which is the only point early enough: the dashboard reads stars in its
    /// own <c>Start</c>, and a level read before the wipe is a level shown unlocked and
    /// then locked on the next visit.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Apply()
    {
        if (UpToDate) return;

        int was = StoredVersion;

        PlayerPrefs.DeleteAll();
        PlayerPrefs.SetInt(VersionKey, Version);
        PlayerPrefs.Save();

        Debug.Log($"[FPSKit] Save format {was} -> {Version}: the profile was cleared so the " +
                  "campaign starts at its first zone.");
    }
}
