/// <summary>
/// An editor-only switch that opens every zone and every level, so a late arena can be
/// played without first clearing the whole campaign in front of it.
///
/// Both gates ask it -- <see cref="Campaign.ZoneUnlocked"/> and
/// <see cref="LevelProgress.IsUnlocked"/> -- rather than writing stars into the save,
/// because written stars are a real save that stays behind after testing, marks
/// children as defeated and hands out the powers they carry. This changes what the
/// gates <i>answer</i> and nothing that is stored, so switching it off puts the player
/// exactly where they were.
///
/// It lives in EditorPrefs, so it is a preference of whoever is at the keyboard rather
/// than of the repo, and it compiles out of every player build. It is ignored in batch
/// mode: EditorPrefs are shared between the editor and a headless run on the same
/// machine, so leaving it on would silently fail every check that asserts a level is
/// still locked. No static is held, so there is nothing to reset between play sessions.
///
/// Toggled with <b>FPSKit > Debug > Unlock All Zones</b>.
/// </summary>
public static class DebugUnlock
{
    public const string PrefKey = "FPSKit.Debug.UnlockAll";

    /// <summary>True only in the editor, outside batch mode, with the menu item checked.</summary>
    public static bool Active
    {
        get
        {
#if UNITY_EDITOR
            return !UnityEngine.Application.isBatchMode
                && UnityEditor.EditorPrefs.GetBool(PrefKey, false);
#else
            return false;
#endif
        }
    }
}
