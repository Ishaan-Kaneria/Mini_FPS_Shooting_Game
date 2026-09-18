using UnityEngine;

/// <summary>
/// Every number the on-screen controls are tuned by, in one asset.
///
/// It is a ScriptableObject for the same reason <see cref="ControlSettings"/> is: touch
/// feel is not one setting, it is a dozen that only make sense together, and a player
/// who finds the controls too fast needs one place to change rather than six components
/// to hunt through a scene for. It is also what makes a settings screen possible later
/// without any of these components learning about UI.
///
/// The defaults are written for a phone held in two hands, thumbs on the glass. That is
/// the only posture this game is played in on a touch device, and tuning for it beats
/// tuning for a compromise between it and a tablet on a desk.
/// </summary>
[CreateAssetMenu(fileName = "TouchProfile", menuName = "FPSKit/Touch Profile")]
public class TouchProfile : ScriptableObject
{
    // ==================================================================
    [Header("Look")]
    [Tooltip("Multiplier on thumb drag, after it has been corrected for screen density. " +
             "1 is the tuned default; the correction is what makes it mean the same on " +
             "every phone, and this is what a player would move in a settings screen.")]
    [Range(0.2f, 3f)] public float lookSensitivity = 1f;

    [Tooltip("How much slower the view turns while aiming down sights. Under 1 on " +
             "purpose: sights exist to make a small correction, and a scope that turns " +
             "at hip-fire speed overshoots every time.")]
    [Range(0.2f, 1f)] public float adsSensitivity = 0.6f;

    [Tooltip("Smoothing on the look, 0 for none.\n\n" +
             "0 is the default and should usually stay there. Smoothing adds latency to " +
             "the one control the player is judging the game by, and what it buys -- " +
             "hiding jitter in the touch stream -- is worth less than what it costs. It " +
             "is here because a small amount genuinely helps on a cheap digitiser.")]
    [Range(0f, 0.5f)] public float lookSmoothing;

    [Tooltip("Ignore drags smaller than this, in millimetres of thumb travel, so a thumb " +
             "resting on the glass does not creep the view.")]
    [Range(0f, 1f)] public float lookDeadZoneMm = 0.15f;

    // ==================================================================
    [Header("Move stick")]
    [Tooltip("Thumb travel from the centre of the stick to full push, in millimetres. " +
             "About 9mm is one comfortable thumb roll -- far enough that fine movement " +
             "is possible, close enough that a full sprint does not need the thumb to " +
             "leave the glass.")]
    [Range(4f, 20f)] public float stickRadiusMm = 9f;

    [Tooltip("Below this fraction of a full push the stick reads as centred.")]
    [Range(0f, 0.5f)] public float stickDeadZone = 0.15f;

    [Tooltip("Push the stick past this fraction to sprint, so running needs no button " +
             "at all. This is how every competitive mobile shooter does it, and the " +
             "reason is thumbs: the sprint button and the look area want the same one, " +
             "so a sprint that costs a button press is a sprint taken while blind.")]
    [Range(0.5f, 1f)] public float sprintPush = 0.85f;

    [Tooltip("Once a push-to-sprint has engaged, keep sprinting until the stick returns " +
             "to centre. Without it, easing off slightly to steer drops the sprint and " +
             "the player spends a chase flicking in and out of it.")]
    public bool sprintLatches = true;

    // ==================================================================
    [Header("Fire")]
    [Tooltip("Keep firing while the thumb slides off the fire button, and let that slide " +
             "turn the view.\n\n" +
             "This is the single most important setting here. Without it the right thumb " +
             "cannot shoot and aim at the same time, so every fight is a choice between " +
             "firing at where the enemy was and tracking them without shooting. With it, " +
             "press and drag is one continuous gesture -- which is what a mouse has " +
             "always been and what a phone otherwise has no answer for.")]
    public bool dragFire = true;

    [Tooltip("Tapping the look area fires.\n\n" +
             "Off by default and it should usually stay off wherever a fire button " +
             "exists: the look surface is most of the screen, so every tap that misses " +
             "ADS or JUMP by a few pixels becomes a shot. It is here for a one-thumb " +
             "layout, where it is the only way to fire at all.")]
    public bool tapToFire;

    // ==================================================================
    [Header("Buttons")]
    [Tooltip("The smallest a touch target may be on the real screen, in millimetres. " +
             "Below about 9mm the miss rate climbs sharply, and a missed FIRE button " +
             "in a firefight is indistinguishable from the game ignoring the player.")]
    [Range(6f, 14f)] public float minButtonMm = 9f;

    [Tooltip("How visible the buttons are at rest. Low enough to see the game through " +
             "them, high enough to find without hunting.")]
    [Range(0.05f, 1f)] public float buttonOpacity = 1f;

    // ==================================================================
    [Header("Aim assist")]
    [Tooltip("Help the crosshair stay on a target.\n\n" +
             "Not a concession -- it is what makes a thumb a viable aiming device at " +
             "all. A mouse resolves about a hundredth of a degree; a thumb on glass, " +
             "moving a contact patch the size of the target, resolves perhaps a degree. " +
             "Without assist the player is not being tested on aim, they are being " +
             "tested on a motor task nobody can do, and the game reads as unfair rather " +
             "than as hard. Every console and mobile shooter has this; the ones that " +
             "feel best are the ones that hide it well.\n\n" +
             "Touch only. A player on a mouse gets none of it.")]
    public bool aimAssist = true;

    [Tooltip("How far off centre a target can be and still be assisted, in degrees. " +
             "Small on purpose: this should help the shot you were already taking, not " +
             "find shots for you.")]
    [Range(0f, 15f)] public float assistAngle = 6f;

    [Tooltip("How much the look slows while the crosshair is on a target. This is the " +
             "half players never notice and feel the most -- it buys time to settle the " +
             "shot without ever moving the view somewhere they did not point it.")]
    [Range(0.2f, 1f)] public float assistSlowdown = 0.55f;

    [Tooltip("How fast the view is drawn toward a target, in degrees per second, at the " +
             "centre of the cone. Kept low: this is adhesion, which helps track someone " +
             "already in the crosshair, not magnetism that snaps onto them.\n\n" +
             "It only applies while the player is already turning or firing, so it can " +
             "never take the camera off somebody standing still.")]
    [Range(0f, 60f)] public float assistPull = 18f;

    [Tooltip("Targets further away than this are left alone -- at distance the assist " +
             "would be doing the aiming rather than helping with it.")]
    [Range(10f, 120f)] public float assistRange = 55f;

    // ==================================================================
    /// <summary>
    /// The look multiplier that applies right now, sights included.
    ///
    /// Read rather than pushed, so nothing has to be told when the player starts aiming
    /// -- there is no state to get out of step, and a weapon swapped mid-aim cannot
    /// leave the sensitivity on the wrong setting.
    /// </summary>
    public float LookScaleFor(bool aimingDownSights)
        => lookSensitivity * (aimingDownSights ? adsSensitivity : 1f);
}
