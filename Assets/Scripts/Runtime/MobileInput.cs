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
    public static bool Crouch;

    /// <summary>
    /// True while anything on screen is asking to sprint.
    ///
    /// Two controls can ask, and they overlap the same way the two fire sources do: the
    /// sprint button while it is toggled on, and the move stick while it is pushed past
    /// <see cref="TouchProfile.sprintPush"/>. Sharing one bool would mean whichever
    /// wrote last won -- easing the stick back to steer would switch off a sprint the
    /// player had toggled deliberately, and letting go of the stick would clear the
    /// button's state with no way to see why.
    /// </summary>
    public static bool Sprint => _sprintButton || _sprintStick;

    static bool _sprintButton;
    static bool _sprintStick;

    /// <summary>Toggled state of the on-screen sprint button.</summary>
    public static void SetSprintButton(bool on) => _sprintButton = on;

    /// <summary>Set while the move stick is pushed far enough to mean "run".</summary>
    public static void SetSprintStick(bool on) => _sprintStick = on;

    /// <summary>
    /// Held while the on-screen bomb button is down. A held flag rather than a queued
    /// tap, because aiming a bomb *is* holding the key -- releasing it is the throw, so
    /// a one-shot tap could not express it at all.
    /// </summary>
    public static bool BombAim;

    /// <summary>
    /// True while anything on screen is asking for fire.
    ///
    /// Two independent controls can ask, and they overlap: the fire button while it is
    /// held, and the look area for a moment after a tap lands on it. They shared one
    /// bool, so whichever finished last won -- a tap expiring switched off a fire button
    /// that was still held down, and letting go of the button cut a tap short. Each owns
    /// its own flag now and this reports either, so neither can answer for the other.
    /// </summary>
    public static bool Fire => _fireButton || _fireHolds > 0 || _fireTap || _autoFire;

    // How many on-screen fire buttons are held. A count rather than a flag because the
    // layout can add a second fire button: with one bool, lifting either thumb switched the
    // gun off while the other was still pressing.
    static int _fireHolds;

    /// <summary>One fire button pressed (true) or released (false).</summary>
    public static void PressFire(bool down) => _fireHolds = Mathf.Max(0, _fireHolds + (down ? 1 : -1));

    static bool _fireButton;
    static bool _fireTap;
    static bool _autoFire;

    static Vector2 _lookDelta;       // screen pixels accumulated since last read
    static bool _jumpQueued;
    static bool _reloadQueued;
    static bool _useItemQueued;

    /// <summary>
    /// A touch player has no Escape key, so without this there is no way to pause --
    /// and therefore no way to reach the dashboard or leave a run at all.
    /// </summary>
    static bool _pauseQueued;

    /// <summary>Held state of the on-screen fire button.</summary>
    public static void SetFireButton(bool held) => _fireButton = held;

    /// <summary>Set while a tap on the look area still counts as a trigger pull.</summary>
    public static void SetFireTap(bool active) => _fireTap = active;

    /// <summary>Set by <see cref="TouchLayout"/> while auto-fire has an enemy under the crosshair.</summary>
    public static void SetAutoFire(bool active) => _autoFire = active;

    public static void AddLook(Vector2 pixels) => _lookDelta += pixels;

    /// <summary>Reads and clears. Only PlayerMotor should call this.</summary>
    public static Vector2 ConsumeLook()
    {
        var delta = _lookDelta;
        _lookDelta = Vector2.zero;
        return delta;
    }

    public static void QueuePause() => _pauseQueued = true;

    /// <summary>Reads and clears. Only GameDirector should call this.</summary>
    public static bool ConsumePause()
    {
        bool queued = _pauseQueued;
        _pauseQueued = false;
        return queued;
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

    public static void QueueUseItem() => _useItemQueued = true;

    /// <summary>Reads and clears. Only ConsumableBelt should call this.</summary>
    public static bool ConsumeUseItem()
    {
        bool queued = _useItemQueued;
        _useItemQueued = false;
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
        Aim = Crouch = BombAim = false;
        _sprintButton = _sprintStick = false;
        _fireButton = _fireTap = _autoFire = false;
        _fireHolds = 0;
        _jumpQueued = _reloadQueued = _pauseQueued = _useItemQueued = false;
    }
}
