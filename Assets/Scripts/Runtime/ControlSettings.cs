using UnityEngine;

/// <summary>
/// Every key binding in one asset. Change them here and both the player and the
/// weapon pick it up -- nothing is hard-coded.
///
/// Defaults: arrows move, Space fires, double-tap Space sprints, left click jumps.
/// Set the Alt Move keys to W/A/S/D to bring WASD back alongside the arrows.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Control Settings", fileName = "Controls")]
public class ControlSettings : ScriptableObject
{
    public enum Preset
    {
        /// <summary>What every shooter uses. Arrows work as well as WASD.</summary>
        StandardFPS,

        /// <summary>Arrows move, mouse shoots. Keyboard hand never leaves the arrows.</summary>
        ArrowsAndMouse,

        /// <summary>Arrows move, Space fires, double-tap Space sprints, click jumps.</summary>
        ArrowsAndSpace
    }

    [Header("Movement")]
    public KeyCode moveForward = KeyCode.UpArrow;
    public KeyCode moveBack = KeyCode.DownArrow;
    public KeyCode moveLeft = KeyCode.LeftArrow;
    public KeyCode moveRight = KeyCode.RightArrow;

    [Header("Alternate Movement (both sets work at once)")]
    public KeyCode altForward = KeyCode.W;
    public KeyCode altBack = KeyCode.S;
    public KeyCode altLeft = KeyCode.A;
    public KeyCode altRight = KeyCode.D;

    [Header("Actions")]
    public KeyCode fire = KeyCode.Mouse0;
    public KeyCode jump = KeyCode.Space;
    public KeyCode aim = KeyCode.Mouse1;
    public KeyCode reload = KeyCode.R;
    public KeyCode crouch = KeyCode.LeftControl;
    public KeyCode altCrouch = KeyCode.C;

    [Header("Equipment")]
    [Tooltip("Held to aim a bomb, released to throw it. Held rather than tapped because " +
             "the aim is the feature -- see BombThrower. The aim key doubles as the " +
             "range lock while this is down, so there is no third binding for it.")]
    public KeyCode bomb = KeyCode.G;

    [Tooltip("Drinks an energy drink off the belt. F is where every shooter puts its " +
             "one-off item, and it is nowhere near the movement keys.")]
    public KeyCode useItem = KeyCode.F;

    [Header("Sprint")]
    [Tooltip("Sprint by double-tapping the key below rather than holding one down.")]
    public bool sprintByDoubleTap;

    [Tooltip("Held down to sprint when double-tap is off; double-tapped when it is on.")]
    public KeyCode sprintKey = KeyCode.LeftShift;

    [Tooltip("Maximum seconds between the two taps.")]
    public float doubleTapWindow = 0.3f;

    [Tooltip("Sprint ends as soon as you stop moving.")]
    public bool sprintEndsWhenNotAdvancing = true;

    [Tooltip("Require movement before the sprint key does anything. Off, which means " +
             "holding the key while standing still already counts as sprinting, so the " +
             "first step you take is at full speed.\n\n" +
             "On, the key is dead until you are already moving, and a player who holds " +
             "it and then walks off spends the first moment at walking pace for no " +
             "reason they can see.")]
    public bool sprintNeedsMovement;

    [Tooltip("The sprint key on its own runs forward. On, which is what a player means " +
             "when they press it: the key is a run, and holding it with no direction " +
             "key down carries the player forward at sprint speed.\n\n" +
             "Off, the key only changes how fast the movement keys move you, so " +
             "pressing it alone does nothing visible and reads as a dead key. A " +
             "direction key always wins while one is held, so this only ever fills in " +
             "for no direction at all.")]
    public bool sprintDrivesForward = true;

    [Header("Keyboard Look (optional, leave None for mouse-only)")]
    public KeyCode lookLeft = KeyCode.None;
    public KeyCode lookRight = KeyCode.None;
    public KeyCode lookUp = KeyCode.None;
    public KeyCode lookDown = KeyCode.None;

    // ------------------------------------------------------------------
    public static bool Held(KeyCode primary, KeyCode alternate = KeyCode.None)
    {
        if (primary != KeyCode.None && Input.GetKey(primary)) return true;
        return alternate != KeyCode.None && Input.GetKey(alternate);
    }

    public static bool Pressed(KeyCode primary, KeyCode alternate = KeyCode.None)
    {
        if (primary != KeyCode.None && Input.GetKeyDown(primary)) return true;
        return alternate != KeyCode.None && Input.GetKeyDown(alternate);
    }

    public bool MoveForwardHeld => Held(moveForward, altForward);
    public bool MoveBackHeld => Held(moveBack, altBack);
    public bool MoveLeftHeld => Held(moveLeft, altLeft);
    public bool MoveRightHeld => Held(moveRight, altRight);
    public bool CrouchHeld => Held(crouch, altCrouch);

    /// <summary>Runtime fallback so nothing breaks when no asset is assigned.</summary>
    public static ControlSettings CreateDefault()
    {
        var settings = CreateInstance<ControlSettings>();
        settings.name = "Default Controls";
        return settings;
    }

    /// <summary>Overwrites every binding with one of the built-in schemes.</summary>
    public void ApplyPreset(Preset preset)
    {
        // Shared across all schemes.
        moveForward = KeyCode.UpArrow;
        moveBack = KeyCode.DownArrow;
        moveLeft = KeyCode.LeftArrow;
        moveRight = KeyCode.RightArrow;
        reload = KeyCode.R;
        crouch = KeyCode.LeftControl;
        altCrouch = KeyCode.C;
        aim = KeyCode.Mouse1;
        bomb = KeyCode.G;
        useItem = KeyCode.F;
        sprintEndsWhenNotAdvancing = true;
        sprintNeedsMovement = false;
        sprintDrivesForward = true;
        lookLeft = lookRight = lookUp = lookDown = KeyCode.None;

        switch (preset)
        {
            case Preset.StandardFPS:
                altForward = KeyCode.W;
                altBack = KeyCode.S;
                altLeft = KeyCode.A;
                altRight = KeyCode.D;
                fire = KeyCode.Mouse0;
                jump = KeyCode.Space;
                sprintByDoubleTap = false;
                sprintKey = KeyCode.LeftShift;
                break;

            case Preset.ArrowsAndMouse:
                altForward = altBack = altLeft = altRight = KeyCode.None;
                fire = KeyCode.Mouse0;
                jump = KeyCode.Space;
                sprintByDoubleTap = false;
                sprintKey = KeyCode.LeftShift;
                break;

            case Preset.ArrowsAndSpace:
                altForward = altBack = altLeft = altRight = KeyCode.None;
                fire = KeyCode.Space;
                jump = KeyCode.Mouse0;
                sprintByDoubleTap = true;
                sprintKey = KeyCode.Space;
                break;
        }
    }
}
