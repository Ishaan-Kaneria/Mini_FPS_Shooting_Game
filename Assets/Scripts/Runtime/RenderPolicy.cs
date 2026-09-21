using UnityEngine;

/// <summary>
/// Picks the quality tier a browser gets, which is the one platform that cannot be given
/// one in the project settings.
///
/// <b>Every WebGL player was being handed the Mobile tier.</b> The per-platform default
/// in <c>QualitySettings</c> is a single entry per platform and WebGL's is 0, which is
/// the tier written to be cheap: render scale below one and no MSAA. That is a defensible
/// answer for a phone and the wrong one for the laptop browser most people play in, and
/// nothing on screen says which tier you got -- it reads as the game being soft, so the
/// player looks for a texture setting that does not exist.
///
/// A browser is the one place where the machine is not known until the game is running,
/// so the choice belongs at runtime. The reach is what decides it, not the user agent:
/// see <see cref="WebDevice.IsTouchOnly"/> for why an iPad defeats every other test.
///
/// It runs before the first scene loads and holds no state, so there is nothing to clear
/// between play sessions -- the reason <see cref="Wallet"/> and <see cref="LevelProgress"/>
/// have no reset hook either. Compiled into the player only: calling
/// <c>QualitySettings.SetQualityLevel</c> in the editor would write the project's current
/// tier to disk as a side effect of pressing Play.
/// </summary>
public static class RenderPolicy
{
    /// <summary>The tier a machine with a pointer and a real GPU should get.</summary>
    const string DesktopTier = "PC";

    /// <summary>The tier a phone or tablet should get.</summary>
    const string HandheldTier = "Mobile";

#if UNITY_WEBGL && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplyOnWeb()
    {
        string wanted = WebDevice.IsTouchOnly ? HandheldTier : DesktopTier;

        int level = IndexOf(wanted);
        if (level < 0 || level == QualitySettings.GetQualityLevel()) return;

        // False: the pipeline asset for the new tier has to be swapped in now rather
        // than on the next frame, or the first scene renders through the old one.
        QualitySettings.SetQualityLevel(level, true);
    }

    static int IndexOf(string tier)
    {
        var names = QualitySettings.names;

        for (int i = 0; i < names.Length; i++)
            if (string.Equals(names[i], tier, System.StringComparison.OrdinalIgnoreCase))
                return i;

        return -1;
    }
#endif
}
