using System;
using UnityEngine;

/// <summary>
/// The player's settings: interface scale, quality, frame rate, and how each kind of input
/// feels. One accessor per setting, straight over PlayerPrefs.
///
/// <b>Nothing is cached</b>, for the reason <see cref="Wallet"/> gives: a static holding a
/// copy is one more thing to clear between play sessions with domain reload off, and the
/// one nobody would think to. PlayerPrefs is already an in-memory table, so a read is cheap.
///
/// <see cref="Changed"/> is raised on every write so whatever applies a setting -- the
/// canvas scalers, the quality tier, the touch layout -- can react while the settings
/// screen is still open, rather than on the next scene load.
///
/// Defaults are chosen per device where the right answer differs: a phone starts on Low
/// quality at 60fps, a PC on High. <see cref="QualityTier"/> is -1 until the first launch
/// has picked one, so the pick is made once and a player's choice is never overridden.
/// </summary>
public static class GameSettings
{
    public enum Quality { Low = 0, Medium = 1, High = 2 }
    public enum AimMode { Hold = 0, Tap = 1 }

    const string Prefix = "settings.";

    /// <summary>Raised after any setting is written, with its key.</summary>
    public static event Action<string> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Changed = null;

    // ==================================================================
    // Display.
    // ==================================================================

    /// <summary>Interface scale, 0.8 to 1.3. Multiplies every canvas the game draws.</summary>
    public static float UiScale
    {
        get => Mathf.Clamp(GetFloat("uiScale", 1f), 0.8f, 1.3f);
        set => SetFloat("uiScale", Mathf.Clamp(value, 0.8f, 1.3f));
    }

    /// <summary>The quality tier, or -1 before the first launch has chosen one.</summary>
    public static int QualityTier
    {
        get => PlayerPrefs.GetInt(Prefix + "quality", -1);
        set => SetInt("quality", Mathf.Clamp(value, 0, 2));
    }

    /// <summary>
    /// 30 or 60. A phone that holds 30 runs cooler and lasts longer; a PC ignores this for
    /// vsync, which already paces to the display.
    /// </summary>
    public static int FrameRate
    {
        get => PlayerPrefs.GetInt(Prefix + "fps", 60) == 30 ? 30 : 60;
        set => SetInt("fps", value <= 30 ? 30 : 60);
    }

    /// <summary>Battery saver: 30fps, a lower resolution floor, no post-processing.</summary>
    public static bool BatterySaver
    {
        get => GetBool("battery", false);
        set => SetBool("battery", value);
    }

    // ==================================================================
    // Look.
    // ==================================================================

    /// <summary>Thumb look speed, as a multiplier on the density-corrected default.</summary>
    public static float TouchSensitivity
    {
        get => Mathf.Clamp(GetFloat("touchSens", 1f), 0.2f, 3f);
        set => SetFloat("touchSens", Mathf.Clamp(value, 0.2f, 3f));
    }

    /// <summary>Right-stick look speed, as a multiplier on the tuned turn rate.</summary>
    public static float GamepadSensitivity
    {
        get => Mathf.Clamp(GetFloat("padSens", 1f), 0.2f, 3f);
        set => SetFloat("padSens", Mathf.Clamp(value, 0.2f, 3f));
    }

    /// <summary>Stick travel ignored around the centre, 0.05 to 0.4 of a full push.</summary>
    public static float StickDeadzone
    {
        get => Mathf.Clamp(GetFloat("deadzone", 0.15f), 0.05f, 0.4f);
        set => SetFloat("deadzone", Mathf.Clamp(value, 0.05f, 0.4f));
    }

    /// <summary>
    /// The exponent on right-stick look, 1 to 3. 1 is linear; higher gives finer control
    /// near the centre and the same top speed at full push.
    /// </summary>
    public static float StickCurve
    {
        get => Mathf.Clamp(GetFloat("curve", 1.8f), 1f, 3f);
        set => SetFloat("curve", Mathf.Clamp(value, 1f, 3f));
    }

    public static bool InvertY
    {
        get => GetBool("invertY", false);
        set => SetBool("invertY", value);
    }

    // ==================================================================
    // Aim assist -- gamepad and touch. A mouse never gets any.
    // ==================================================================

    public static bool AimAssist
    {
        get => GetBool("assist", true);
        set => SetBool("assist", value);
    }

    /// <summary>0 to 1. 1 is the tuned strength; 0 is none even with the toggle on.</summary>
    public static float AimAssistStrength
    {
        get => Mathf.Clamp01(GetFloat("assistStrength", 1f));
        set => SetFloat("assistStrength", Mathf.Clamp01(value));
    }

    // ==================================================================
    // Touch.
    // ==================================================================

    /// <summary>Fire on its own while the crosshair is on an enemy.</summary>
    public static bool AutoFire
    {
        get => GetBool("autoFire", false);
        set => SetBool("autoFire", value);
    }

    /// <summary>Turn the view by tilting the device, on top of the thumb.</summary>
    public static bool Gyro
    {
        get => GetBool("gyro", false);
        set => SetBool("gyro", value);
    }

    public static float GyroSensitivity
    {
        get => Mathf.Clamp(GetFloat("gyroSens", 1f), 0.2f, 3f);
        set => SetFloat("gyroSens", Mathf.Clamp(value, 0.2f, 3f));
    }

    /// <summary>Hold the aim button to aim down sights, or tap it to toggle. Tap by default, which is what the button always did.</summary>
    public static AimMode TouchAimMode
    {
        get => PlayerPrefs.GetInt(Prefix + "aimMode", 1) == 1 ? AimMode.Tap : AimMode.Hold;
        set => SetInt("aimMode", (int)value);
    }

    /// <summary>Mirror the touch layout: stick on the right, buttons on the left.</summary>
    public static bool LeftHanded
    {
        get => GetBool("leftHanded", false);
        set => SetBool("leftHanded", value);
    }

    /// <summary>How visible the on-screen buttons are at rest, 0.25 to 1.</summary>
    public static float TouchOpacity
    {
        get => Mathf.Clamp(GetFloat("touchOpacity", 0.6f), 0.25f, 1f);
        set => SetFloat("touchOpacity", Mathf.Clamp(value, 0.25f, 1f));
    }

    // ---- controls, video, audio, HUD ----------------------------------------------

    /// <summary>Mouse look speed, a multiple of the rig's own, 0.2 to 3.</summary>
    public static float MouseSensitivity
    {
        get => Mathf.Clamp(GetFloat("mouseSens", 1f), 0.2f, 3f);
        set => SetFloat("mouseSens", Mathf.Clamp(value, 0.2f, 3f));
    }

    /// <summary>Horizontal-ish field of view in degrees, 70 to 110. Aiming down sights still zooms from it.</summary>
    public static float FieldOfView
    {
        get => Mathf.Clamp(GetFloat("fov", 75f), 70f, 110f);
        set => SetFloat("fov", Mathf.Clamp(Mathf.Round(value), 70f, 110f));
    }

    /// <summary>Wait for the display's refresh on a desktop. Phones and browsers pace themselves.</summary>
    public static bool VSync
    {
        get => GetBool("vsync", true);
        set => SetBool("vsync", value);
    }

    public static float MasterVolume
    {
        get => Mathf.Clamp01(GetFloat("volMaster", 1f));
        set => SetFloat("volMaster", Mathf.Clamp01(value));
    }

    public static float MusicVolume
    {
        get => Mathf.Clamp01(GetFloat("volMusic", 0.8f));
        set => SetFloat("volMusic", Mathf.Clamp01(value));
    }

    public static float SfxVolume
    {
        get => Mathf.Clamp01(GetFloat("volSfx", 1f));
        set => SetFloat("volSfx", Mathf.Clamp01(value));
    }

    /// <summary>
    /// The in-game HUD's panels, as a multiple of their size, 0.7 to 1.3. Separate from the
    /// interface scale, which is every menu too, and multiplied with each panel's own size
    /// from the HUD layout.
    /// </summary>
    public static float HudScale
    {
        get => Mathf.Clamp(GetFloat("hudScale", 1f), 0.7f, 1.3f);
        set => SetFloat("hudScale", Mathf.Clamp(value, 0.7f, 1.3f));
    }

    /// <summary>The minimap turns so the player's forward is up (true), or keeps north up and turns the arrow.</summary>
    public static bool MinimapRotates
    {
        get => GetBool("minimapRotates", true);
        set => SetBool("minimapRotates", value);
    }

    // ==================================================================

    /// <summary>Puts every setting back to its default.</summary>
    public static void ResetAll()
    {
        foreach (var key in new[]
                 {
                     "uiScale", "quality", "fps", "battery", "touchSens", "padSens", "deadzone", "curve",
                     "invertY", "assist", "assistStrength", "autoFire", "gyro", "gyroSens", "aimMode",
                     "leftHanded", "touchOpacity",
                     "mouseSens", "fov", "vsync", "volMaster", "volMusic", "volSfx", "hudScale", "minimapRotates",
                 })
            PlayerPrefs.DeleteKey(Prefix + key);
        // The HUD layout is a setting like the rest and goes with them; the named layouts the
        // player saved are theirs, not settings, and stay.
        HudLayout.ForgetAllForms();
        KeyBindings.ResetAll();
        PlayerPrefs.Save();
        Changed?.Invoke("*");
    }

    static float GetFloat(string key, float fallback) => PlayerPrefs.GetFloat(Prefix + key, fallback);
    static bool GetBool(string key, bool fallback) => PlayerPrefs.GetInt(Prefix + key, fallback ? 1 : 0) != 0;

    static void SetFloat(string key, float value)
    {
        PlayerPrefs.SetFloat(Prefix + key, value);
        Changed?.Invoke(key);
    }

    static void SetInt(string key, int value)
    {
        PlayerPrefs.SetInt(Prefix + key, value);
        Changed?.Invoke(key);
    }

    static void SetBool(string key, bool value) => SetInt(key, value ? 1 : 0);
}
