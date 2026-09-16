using UnityEngine;

/// <summary>
/// Where the on-screen controls put their state, and where PlayerMotor and Weapon
/// read it from. Keeping it static means touch input folds into the existing
/// keyboard path without either side knowing about the other.
///
/// Edge-triggered actions (jump, reload) are queued and consumed exactly once, so
/// a tap cannot be missed or double-counted.
/// </summary>
public static class MobileInput
{
    /// <summary>True while on-screen controls exist in the scene.</summary>
    public static bool Active;

    public static Vector2 Move;      // -1..1 per axis, from the joystick
    public static bool Aim;
    public static bool Sprint;
    public static bool Crouch;

    /// <summary>
    /// True while anything on screen is asking for fire.
    ///
    /// Two independent controls can ask, and they overlap: the fire button while it is
    /// held, and the look area for a moment after a tap lands on it. They shared one
    /// bool, so whichever finished last won -- a tap expiring switched off a fire button
    /// that was still held down, and letting go of the button cut a tap short. Each owns
    /// its own flag now and this reports either, so neither can answer for the other.
    /// </summary>
    public static bool Fire => _fireButton || _fireTap;

    static bool _fireButton;
    static bool _fireTap;

    static Vector2 _lookDelta;       // screen pixels accumulated since last read
    static bool _jumpQueued;
    static bool _reloadQueued;

    /// <summary>Held state of the on-screen fire button.</summary>
    public static void SetFireButton(bool held) => _fireButton = held;

    /// <summary>Set while a tap on the look area still counts as a trigger pull.</summary>
    public static void SetFireTap(bool active) => _fireTap = active;

    public static void AddLook(Vector2 pixels) => _lookDelta += pixels;

    /// <summary>Reads and clears. Only PlayerMotor should call this.</summary>
    public static Vector2 ConsumeLook()
    {
        var delta = _lookDelta;
        _lookDelta = Vector2.zero;
        return delta;
    }

    public static void QueueJump() => _jumpQueued = true;

    public static bool ConsumeJump()
    {
        bool queued = _jumpQueued;
        _jumpQueued = false;
        return queued;
    }

    public static void QueueReload() => _reloadQueued = true;

    public static bool ConsumeReload()
    {
        bool queued = _reloadQueued;
        _reloadQueued = false;
        return queued;
    }

    /// <summary>
    /// Static state survives play-mode restarts in the editor, so the touch UI
    /// clears it on enable and disable.
    /// </summary>
    /// <summary>
    /// Clears Active as well as the per-frame state. Domain reload is disabled here, so
    /// a session that had the touch layer up leaves Active true forever: the next run
    /// takes the mobile path, skips locking the cursor, and mouse look stops working
    /// on a desktop scene that has no on-screen controls at all.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState()
    {
        Active = false;
        Reset();
    }

    public static void Reset()
    {
        Move = Vector2.zero;
        _lookDelta = Vector2.zero;
        Aim = Sprint = Crouch = false;
        _fireButton = _fireTap = false;
        _jumpQueued = _reloadQueued = false;
    }
}
