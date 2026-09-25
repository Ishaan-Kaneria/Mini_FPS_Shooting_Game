using UnityEngine;

/// <summary>
/// Applies the settings that belong to no one object: master and effects volume (through
/// the listener, which scales every source), and frame pacing (vsync). Subscribed once per
/// play session; <see cref="GameSettings.Changed"/> is cleared between sessions, so this
/// holds no state of its own.
///
/// Music is the exception to the listener: the menu's music source sets
/// <c>ignoreListenerVolume</c> and applies master times music itself (see UISounds), so
/// "effects" and "music" are exact without an AudioMixer -- which Unity cannot create from code.
/// </summary>
public static class SettingsApplier
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        GameSettings.Changed += OnChanged;
        Apply();
    }

    static void OnChanged(string key)
    {
        Apply();
        if (key == "vsync" || key == "*") QualityTiers.ApplyFrameRate();
        if (key == "hudScale" || key == "*") HudLayout.Preview(HudLayout.Shown);
    }

    static void Apply() => AudioListener.volume = GameSettings.MasterVolume * GameSettings.SfxVolume;
}
