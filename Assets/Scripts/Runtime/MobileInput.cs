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
    public static bool Fire;
    public static bool Aim;
    public static bool Sprint;
    public static bool Crouch;

    static Vector2 _lookDelta;       // screen pixels accumulated since last read
    static bool _jumpQueued;
    static bool _reloadQueued;

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
    public static void Reset()
    {
        Move = Vector2.zero;
        _lookDelta = Vector2.zero;
        Fire = Aim = Sprint = Crouch = false;
        _jumpQueued = _reloadQueued = false;
    }
}
