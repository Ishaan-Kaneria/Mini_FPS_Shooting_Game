using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// First-person movement and mouse look on a CharacterController.
/// All keys come from a ControlSettings asset, so nothing here is hard-coded.
///
/// Look uses the same maths as Valorant/CS: degrees turned equals mouse counts
/// times sensitivity times 0.022, read raw with no smoothing or acceleration.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMotor : MonoBehaviour
{
    /// <summary>The Valorant/CS constant: degrees of turn per mouse count at sensitivity 1.</summary>
    public const float DegreesPerCount = 0.022f;

    [Header("Wiring")]
    public Transform cameraHolder;

    [Tooltip("Every key binding. Leave empty to use the built-in defaults: " +
             "arrows move, Space fires, double-tap Space sprints, left click jumps.")]
    public ControlSettings controls;

    [Header("Look")]
    [Tooltip("Same scale as Valorant and CS2, but most players coming from CoD or " +
             "casual shooters want 3-6 here. Raise it until a comfortable swipe " +
             "turns you a full 180.")]
    public float sensitivity = 4f;

    [Tooltip("Look sensitivity while aiming. 1 = unchanged. CoD sits around 0.75.")]
    [Range(0.1f, 1f)] public float adsSensitivityMultiplier = 0.8f;

    [Tooltip("Read the mouse straight from the Input System. The legacy Mouse X/Y axes are " +
             "smoothed and scaled by the Input Manager, which is what makes look feel sluggish. " +
             "Only turn this off if the Input System package is removed.")]
    public bool rawMouseInput = true;

    [Tooltip("Only used when rawMouseInput is off. Converts the legacy axis back into mouse counts.")]
    public float legacyAxisScale = 10f;

    [Tooltip("Degrees of turn per pixel of thumb drag on a touch screen. " +
             "0.2 is a comfortable default; mobile shooters run 0.15 to 0.3.")]
    public float touchSensitivity = 0.2f;

    public float pitchClamp = 89f;
    public bool invertY;
    public bool lockCursor = true;

    [Header("Keyboard Look")]
    [Tooltip("Degrees per second the look keys turn you. Bind them in Control Settings; " +
             "by default the arrows move instead of looking, so this is unused.")]
    public float keyboardYawSpeed = 260f;
    public float keyboardPitchSpeed = 180f;

    [Header("Speeds")]
    /// <summary>
    /// Multiplier on whatever speed the player would otherwise be moving at. Set by
    /// ConsumableBelt while an energy drink is running and put back to 1 when it ends.
    ///
    /// A plain float rather than a stack of modifiers: one thing boosts speed, and a
    /// modifier system for one caller is a system with nothing to be right about. It is
    /// also why ConsumableBelt assigns rather than multiplies -- two drinks reset the
    /// clock instead of compounding into a player nothing can catch.
    /// </summary>
    [System.NonSerialized] public float SpeedMultiplier = 1f;

    public float walkSpeed = 5.6f;
    public float sprintSpeed = 8.2f;
    public float crouchSpeed = 2.8f;

    [Tooltip("Metres per second squared, as a real acceleration. 55 reaches walking " +
             "speed in about a tenth of a second -- the near-instant starts of " +
             "Valorant/CS with just enough ramp to feel like weight. Drop toward 18 " +
             "for a heavier, Battlefield-style push-off.")]
    public float groundAcceleration = 55f;

    [Tooltip("Air control. Well below ground acceleration on purpose: being able to " +
             "change direction freely mid-jump is what makes a shooter feel floaty.")]
    public float airAcceleration = 16f;

    [Tooltip("Metres per second squared of braking when you let go of the stick.")]
    public float friction = 50f;

    [Header("Jump and Gravity")]
    [Tooltip("Apex height in metres. Real shooters sit near 0.9; this is deliberately " +
             "higher so you can clear crates and cover.")]
    public float jumpHeight = 1.5f;

    [Tooltip("Stronger than real gravity (-9.81) on purpose -- it makes the arc snappy " +
             "instead of floaty. Raising jumpHeight without this feels like the moon.")]
    public float gravity = -24f;

    [Tooltip("2 gives a double jump, which is how you reach the raised platforms without ramps.")]
    [Min(1)] public int maxJumps = 2;

    public float coyoteTime = 0.12f;
    public float jumpBuffer = 0.12f;

    [Header("Crouch")]
    public float standHeight = 1.8f;
    public float crouchHeight = 1.15f;
    public float crouchLerpSpeed = 11f;
    public float eyeOffsetFromTop = 0.15f;

    [Header("Head Bob")]
    public float bobFrequency = 9f;
    public float bobAmplitude = 0.045f;
    public float bobLerpSpeed = 10f;

    [Tooltip("Metres the camera dips on a hard landing. Small: this is a punctuation " +
             "mark on the jump, not a stumble.")]
    public float landDipAmount = 0.12f;

    public float landDipRecovery = 6f;

    [Tooltip("Impact speed below which a touchdown is ignored. isGrounded flickers off " +
             "for a frame on slopes and floor seams, and without a floor here every seam " +
             "would fire a landing thump and reset the footstep rhythm.")]
    public float landImpactThreshold = 4.5f;

    [Header("Camera Shake")]
    [Tooltip("Degrees of shake at full strength. Weapon fire and damage both feed this.")]
    public float shakeMagnitude = 1.6f;

    [Tooltip("How fast a shake impulse dies away. Higher is snappier.")]
    public float shakeDecay = 4.5f;

    public float shakeFrequency = 22f;

    [Header("Fall Damage")]
    [Tooltip("Off by default. The arena has double jumps and low platforms, so falls are " +
             "normally the player's own business -- turn this on for a level with real drops.")]
    public bool fallDamage;

    [Tooltip("Impact speed you can take for free, in metres per second.")]
    public float safeFallSpeed = 16f;

    [Tooltip("Damage per metre-per-second above the safe speed.")]
    public float fallDamagePerSpeed = 4f;

    [Header("Footsteps (optional)")]
    public AudioSource footstepSource;
    public AudioClip[] footstepClips;
    public AudioClip landClip;
    public float stepDistance = 2.4f;
    [Range(0f, 1f)] public float footstepVolume = 0.5f;

    /// <summary>
    /// Global input gate. The GameDirector switches this off on pause and game over
    /// and back on when a scene loads, so movement and firing both stop at once.
    /// </summary>
    public static bool InputEnabled = true;

    /// <summary>
    /// This project runs with Enter Play Mode Options on and domain reload disabled,
    /// so a static keeps whatever the last play session left in it. Any run that ended
    /// paused or on the game over screen leaves this gate false, and without this hook
    /// the next run starts with a player who cannot move, look or shoot -- the game
    /// looks broken while every build check still passes.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState() => InputEnabled = true;

    // ---- state read by the weapon, sway and HUD --------------------------
    public bool IsSprinting { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool IsGrounded { get; private set; }

    /// <summary>0 when still, 1 at full sprint speed. Drives weapon spread and bob.</summary>
    public float PlanarSpeed01 { get; private set; }

    /// <summary>Multiplier on look sensitivity. Weapon drives this down while aiming.</summary>
    public float LookSensitivityMultiplier { get; set; } = 1f;

    /// <summary>
    /// While true the look is still measured and published through
    /// <see cref="LookDeltaDegrees"/>, but it is not applied to the view.
    ///
    /// This is how the mouse gets lent to something else without that thing having to
    /// read the mouse itself. <see cref="BombThrower"/> takes it while a bomb is being
    /// aimed so the same mouse motion drives a screen cursor instead of the head, and
    /// hands it back on release. Measuring and publishing regardless is the point: the
    /// borrower gets the motion in the same degrees the view would have turned, so the
    /// cursor moves exactly as far as the aim would have, and one sensitivity setting
    /// still governs both.
    ///
    /// Recoil, shake and the pitch clamp are untouched, so a player shot while placing
    /// a bomb still gets kicked.
    /// </summary>
    public bool LookCaptured { get; set; }

    /// <summary>Degrees the view turned this frame (x = yaw, y = pitch). Drives weapon sway.</summary>
    public Vector2 LookDeltaDegrees { get; private set; }

    /// <summary>Impact speed in metres per second. Useful for landing audio and effects.</summary>
    public event Action<float> Landed;

    CharacterController _cc;
    Health _health;
    Vector3 _velocity;                    // world space, includes vertical
    float _yaw, _pitch;
    float _recoilPitch, _recoilYaw;
    float _recoilRecovery = 9f;
    float _shake, _shakeSeed;
    float _lastGroundedTime, _lastJumpPressedTime;
    int _jumpsUsed;
    float _lastSprintTapTime = -99f;
    bool _sprintLatched;
    float _nextCursorAttempt;
    float _bobTimer, _stepAccumulator;
    Vector3 _bobOffset;
    float _landDip;
    bool _wasGrounded = true;
    int _ceilingMask;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _health = GetComponent<Health>();

        // The controller's own height is the source of truth; the inspector value is
        // only a fallback for a rig assembled without one.
        standHeight = _cc.height;
        _yaw = transform.eulerAngles.y;

        // Never let the stand-up check hit the player's own capsule.
        _ceilingMask = ~(1 << gameObject.layer);
        _shakeSeed = UnityEngine.Random.Range(0f, 100f);

        if (cameraHolder == null && Camera.main != null)
            cameraHolder = Camera.main.transform.parent;

        if (footstepSource == null) footstepSource = GetComponent<AudioSource>();
        if (controls == null) controls = ControlSettings.CreateDefault();
    }

    void Start()
    {
        if (lockCursor) SetCursorLocked(true);
    }

    void Update()
    {
        if (!InputEnabled)
        {
            IsSprinting = false;
            PlanarSpeed01 = 0f;
            return;
        }

        HandleCursor();
        HandleLook();
        HandleCrouch();
        HandleMove();
        HandleBobAndFootsteps();
    }

    // ======================================================================
    /// <summary>
    /// Puts the pointer lock back when it has been lost, because without it
    /// <see cref="HandleLook"/> reads no mouse at all.
    ///
    /// The lock gets dropped constantly and mostly not by the game: Escape releases it
    /// in every browser, the editor drops it the moment the Game view loses focus, and
    /// alt-tabbing drops it anywhere. What the player sees is that the keyboard still
    /// works and the mouse has died -- they can walk, they can hold the bomb key and
    /// watch the ring sit there, and turning does nothing. Nothing is logged, because
    /// nothing went wrong.
    ///
    /// This used to re-lock on a left click and nothing else, which is a poor recovery
    /// for two reasons. It is undiscoverable -- there is no reason a player would guess
    /// that clicking fixes the mouse -- and while a bomb is being aimed the left button
    /// is the one input suppressed by <see cref="Weapon"/>, so a player who did try it
    /// got no shot, no feedback, and no idea the click had done anything at all.
    ///
    /// Any key that means "I am playing" now does it, held rather than pressed, so it
    /// is back before the player has finished noticing. Escape is deliberately not on
    /// the list and neither is "any key": Escape is the pause key, and re-locking the
    /// pointer on the frame the pause menu opens would take the cursor away from the
    /// menu it just opened. That is also why this runs below the InputEnabled gate in
    /// Update -- a paused game does not reach here at all.
    /// </summary>
    void HandleCursor()
    {
        if (!lockCursor || MobileInput.Active) return;
        if (Cursor.lockState == CursorLockMode.Locked) return;
        if (!WantsToPlay()) return;

        // Rate limited because WebGL only grants a pointer lock off a real user
        // gesture: a refused request retried every frame is a console full of warnings
        // for a lock the browser was never going to give us this frame anyway.
        if (Time.unscaledTime < _nextCursorAttempt) return;

        _nextCursorAttempt = Time.unscaledTime + 0.25f;
        SetCursorLocked(true);
    }

    /// <summary>
    /// True while the player is touching anything the game is played with. Held, not
    /// pressed, so a key already down when the lock was lost still counts.
    /// </summary>
    bool WantsToPlay()
    {
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) return true;
        if (controls == null) return false;

        return controls.MoveForwardHeld || controls.MoveBackHeld ||
               controls.MoveLeftHeld || controls.MoveRightHeld || controls.CrouchHeld ||
               ControlSettings.Held(controls.fire) ||
               ControlSettings.Held(controls.aim) ||
               ControlSettings.Held(controls.jump) ||
               ControlSettings.Held(controls.reload) ||
               ControlSettings.Held(controls.sprintKey) ||
               ControlSettings.Held(controls.bomb) ||
               ControlSettings.Held(controls.useItem);
    }

    public void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // ======================================================================
    void HandleLook()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 counts = ReadMouseCounts();
            float scale = sensitivity * DegreesPerCount * LookSensitivityMultiplier;

            LookDeltaDegrees = counts * scale;

            if (!LookCaptured)
            {
                _yaw += LookDeltaDegrees.x;
                _pitch -= LookDeltaDegrees.y * (invertY ? -1f : 1f);
                _pitch = Mathf.Clamp(_pitch, -pitchClamp, pitchClamp);
            }
        }
        else
        {
            LookDeltaDegrees = Vector2.zero;
        }

        // Thumb drag from the on-screen look area, in the same degree units.
        if (MobileInput.Active)
        {
            Vector2 touch = MobileInput.ConsumeLook() * touchSensitivity * LookSensitivityMultiplier;

            if (touch.sqrMagnitude > 0f)
            {
                if (!LookCaptured)
                {
                    _yaw += touch.x;
                    _pitch -= touch.y * (invertY ? -1f : 1f);
                    _pitch = Mathf.Clamp(_pitch, -pitchClamp, pitchClamp);
                }

                LookDeltaDegrees += touch;
            }
        }

        // Optional keyboard look. Unbound by default, since the arrows move.
        float keyYaw = 0f;
        float keyPitch = 0f;

        if (ControlSettings.Held(controls.lookLeft)) keyYaw -= 1f;
        if (ControlSettings.Held(controls.lookRight)) keyYaw += 1f;
        if (ControlSettings.Held(controls.lookUp)) keyPitch -= 1f;
        if (ControlSettings.Held(controls.lookDown)) keyPitch += 1f;

        if (keyYaw != 0f || keyPitch != 0f)
        {
            Vector2 keyDelta = new Vector2(keyYaw * keyboardYawSpeed,
                                           -keyPitch * keyboardPitchSpeed) * Time.deltaTime;

            _yaw += keyDelta.x;
            _pitch -= keyDelta.y;
            _pitch = Mathf.Clamp(_pitch, -pitchClamp, pitchClamp);

            LookDeltaDegrees += keyDelta;
        }

        // Recoil decays toward zero so the view settles between bursts.
        float decay = Mathf.Clamp01(_recoilRecovery * Time.deltaTime);
        _recoilPitch = Mathf.Lerp(_recoilPitch, 0f, decay);
        _recoilYaw = Mathf.Lerp(_recoilYaw, 0f, decay);

        _shake = Mathf.MoveTowards(_shake, 0f, shakeDecay * Time.deltaTime);

        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

        if (cameraHolder == null) return;

        Vector3 shake = CurrentShake();
        cameraHolder.localRotation = Quaternion.Euler(
            _pitch - _recoilPitch + shake.x,
            _recoilYaw + shake.y,
            shake.z);
    }

    /// <summary>
    /// Perlin rather than Random: consecutive frames stay correlated, so the view
    /// swims instead of buzzing. White-noise shake reads as a broken camera.
    /// </summary>
    Vector3 CurrentShake()
    {
        if (_shake <= 0.0001f) return Vector3.zero;

        float t = Time.time * shakeFrequency + _shakeSeed;
        float amount = _shake * _shake * shakeMagnitude;   // squared: dies away fast

        return new Vector3(
            (Mathf.PerlinNoise(t, 0f) - 0.5f) * 2f * amount,
            (Mathf.PerlinNoise(0f, t) - 0.5f) * 2f * amount,
            (Mathf.PerlinNoise(t, t) - 0.5f) * 2f * amount * 0.6f);
    }

    /// <summary>
    /// Raw mouse counts since the last frame -- no smoothing, no acceleration, no
    /// Input Manager scaling. This 1:1 response is what separates a shooter that
    /// feels tight from one that feels like dragging the view through syrup.
    ///
    /// The Input System's delta is preferred and the legacy axis is the fallback, but
    /// the fallback is reached on an *empty reading* rather than only on a missing
    /// package. That is not belt and braces, it is the fix for a real and completely
    /// silent failure: on some platforms -- Linux/X11 most reliably --
    /// <c>Mouse.current.delta</c> reports zero for every frame the cursor is locked,
    /// while <c>Mouse.current</c> itself is present and every other control on it
    /// works. Written as an unconditional return, that is a game whose mouse look
    /// simply does not exist, with a live mouse device, a locked cursor, a running
    /// game at sixty frames a second and nothing logged anywhere. It was measured
    /// here: 296 consecutive frames of <see cref="LookDeltaDegrees"/> at exactly zero
    /// with the cursor locked and the window focused.
    ///
    /// Consulting the legacy axis when the delta is empty cannot double-count. The two
    /// are read in the same frame and report the same motion, and the preferred one
    /// wins whenever it has anything at all to say -- the fallback is only reached on
    /// a frame the Input System says the mouse did not move. It is
    /// <c>GetAxisRaw</c> rather than <c>GetAxis</c> for the same reason: the smoothed
    /// axis keeps reporting movement after the mouse has stopped, which would turn the
    /// view on frames that really were still.
    /// </summary>
    Vector2 ReadMouseCounts()
    {
#if ENABLE_INPUT_SYSTEM
        if (rawMouseInput && Mouse.current != null)
        {
            Vector2 raw = Mouse.current.delta.ReadValue();
            if (raw.sqrMagnitude > 0f) return raw;
        }
#endif
        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * legacyAxisScale;
    }

    /// <summary>
    /// Turns the view by an explicit amount, in the same degrees the look uses.
    ///
    /// For a borrower that has taken the look through <see cref="LookCaptured"/> and
    /// needs to give some of it back -- the bomb cursor pushing at the edge of the
    /// screen, which has to turn the head or the player could only ever place a bomb
    /// inside the frustum they started the throw in. Applied through the same clamp as
    /// the mouse, so the pitch cannot be pushed past vertical.
    /// </summary>
    public void TurnBy(Vector2 degrees)
    {
        _yaw += degrees.x;
        _pitch -= degrees.y * (invertY ? -1f : 1f);
        _pitch = Mathf.Clamp(_pitch, -pitchClamp, pitchClamp);
    }

    /// <summary>Called by Weapon on every shot. Vertical is degrees up, horizontal is plus/minus yaw.</summary>
    public void AddRecoil(float vertical, float horizontal, float recovery = 9f)
    {
        _recoilPitch += vertical;
        _recoilYaw += UnityEngine.Random.Range(-horizontal, horizontal);
        _recoilRecovery = Mathf.Max(0.1f, recovery);
    }

    /// <summary>
    /// Adds a camera shake impulse, 0 to 1. Impulses take the strongest rather than
    /// summing, so a burst of fire cannot stack into an unplayable screen.
    /// </summary>
    public void AddShake(float amount) => _shake = Mathf.Clamp01(Mathf.Max(_shake, amount));

    /// <summary>
    /// Whether the sprint key itself is down *and* is allowed to carry the player
    /// forward on its own.
    ///
    /// A player who presses sprint with no direction key down means "run", not "stand
    /// here at the ready": a key that changes a speed you do not currently have is a
    /// key that does nothing, and the only thing they can tell from the outside is that
    /// sprint works with the movement keys and not without them. So the key supplies
    /// the direction when nothing else does.
    ///
    /// It asks for the key to be *held* rather than for <see cref="IsSprinting"/>,
    /// which is why a double-tap-latched sprint (the ArrowsAndSpace preset, where the
    /// sprint key is also the fire key) does not turn into a permanent auto-run the
    /// player never asked for and cannot see how to stop.
    /// </summary>
    bool SprintDrivesForward =>
        controls != null && controls.sprintDrivesForward &&
        (MobileInput.Sprint || ControlSettings.Held(controls.sprintKey));

    /// <summary>
    /// Whether the player is sprinting this frame.
    ///
    /// Two things this deliberately does *not* require, both of which it used to:
    ///
    /// - **Moving forward.** The test was `moveInput.y > 0.1f`, so the sprint key did
    ///   nothing at all while strafing or backing up. Nothing about running is
    ///   forward-only, and a key that works on one of the four movement directions
    ///   reads as a key that intermittently fails.
    /// - **Moving at all**, unless <see cref="ControlSettings.sprintNeedsMovement"/>
    ///   asks for it. Holding the key while standing still now counts, so the first
    ///   step out of cover is already at full speed. Standing still at a sprint costs
    ///   nothing -- there is no speed to apply and <see cref="PlanarSpeed01"/> stays at
    ///   zero, so weapon spread and bob are untouched -- and it is the difference
    ///   between a control that answers when you press it and one that seems dead
    ///   until you happen to also be moving.
    ///
    /// And one thing it now supplies rather than requires: with
    /// <see cref="ControlSettings.sprintDrivesForward"/> on, the key held with no
    /// direction key down *is* the direction -- see <see cref="SprintDrivesForward"/>.
    /// So "moving" here is true whenever that key is down, because the key is about to
    /// produce the movement itself; without that the double-tap scheme drops its latch
    /// on the same frame it takes it.
    ///
    /// The double-tap scheme exists so the sprint key can double as the fire key; the
    /// first tap still fires, which is unavoidable when one key carries both jobs.
    /// </summary>
    bool EvaluateSprint(Vector2 moveInput)
    {
        bool moving = moveInput.sqrMagnitude > 0.01f || SprintDrivesForward;
        bool allowed = moving || !controls.sprintNeedsMovement;

        if (MobileInput.Sprint) return allowed;

        if (!controls.sprintByDoubleTap)
            return ControlSettings.Held(controls.sprintKey) && allowed;

        if (ControlSettings.Pressed(controls.sprintKey))
        {
            if (Time.time - _lastSprintTapTime <= controls.doubleTapWindow) _sprintLatched = true;
            _lastSprintTapTime = Time.time;
        }

        // A latched sprint does have to end on its own, or it silently survives until
        // the next double tap. Movement in any direction keeps it, so a sprint does not
        // drop the moment the player sidesteps something.
        if (controls.sprintEndsWhenNotAdvancing && !moving) _sprintLatched = false;
        if (IsCrouching) _sprintLatched = false;

        return _sprintLatched;
    }

    /// <summary>
    /// Movement reads its keys from ControlSettings rather than the Horizontal/Vertical axes,
    /// because Unity binds the arrow keys to those axes too -- and the arrows are
    /// needed for keyboard look.
    /// </summary>
    Vector2 ReadMoveInput()
    {
        float x = 0f;
        float y = 0f;

        if (controls.MoveLeftHeld) x -= 1f;
        if (controls.MoveRightHeld) x += 1f;
        if (controls.MoveBackHeld) y -= 1f;
        if (controls.MoveForwardHeld) y += 1f;

        Vector2 keyboard = new Vector2(x, y);
        if (!MobileInput.Active) return keyboard;

        // Joystick wins when it is being pushed, so the two never fight.
        return MobileInput.Move.sqrMagnitude > 0.001f ? MobileInput.Move : keyboard;
    }

    // ======================================================================
    void HandleCrouch()
    {
        bool wantsCrouch = controls.CrouchHeld || MobileInput.Crouch;

        // Never stand up into a ceiling.
        if (!wantsCrouch && IsCrouching && BlockedAbove()) wantsCrouch = true;

        IsCrouching = wantsCrouch;
        float targetHeight = IsCrouching ? crouchHeight : standHeight;

        float h = Mathf.Lerp(_cc.height, targetHeight, crouchLerpSpeed * Time.deltaTime);
        if (Mathf.Abs(h - _cc.height) > 0.0005f)
        {
            _cc.height = h;
            _cc.center = new Vector3(0f, h * 0.5f, 0f);
        }
    }

    bool BlockedAbove()
    {
        Vector3 origin = transform.position + Vector3.up * (_cc.height - _cc.radius);
        float distance = Mathf.Max(0.05f, standHeight - _cc.height + 0.05f);

        return Physics.SphereCast(origin, _cc.radius * 0.95f, Vector3.up, out _, distance,
                                  _ceilingMask, QueryTriggerInteraction.Ignore);
    }

    // ======================================================================
    void HandleMove()
    {
        IsGrounded = _cc.isGrounded;
        if (IsGrounded)
        {
            _lastGroundedTime = Time.time;
            _jumpsUsed = 0;
        }

        if (ControlSettings.Pressed(controls.jump) || MobileInput.ConsumeJump())
            _lastJumpPressedTime = Time.time;

        Vector2 input = ReadMoveInput();
        if (input.sqrMagnitude > 1f) input.Normalize();

        IsSprinting = EvaluateSprint(input) && IsGrounded && !IsCrouching;

        // The sprint key on its own is a run. A direction key always wins -- this only
        // ever fills in for no direction at all -- so nothing a player steers with is
        // overridden, and the substitution happens after the sprint is decided so that
        // it cannot talk itself into a sprint the controls did not grant.
        if (IsSprinting && input.sqrMagnitude < 0.01f && SprintDrivesForward)
            input = new Vector2(0f, 1f);

        float targetSpeed = IsCrouching ? crouchSpeed : (IsSprinting ? sprintSpeed : walkSpeed);
        targetSpeed *= Mathf.Max(0.05f, SpeedMultiplier);
        Vector3 wish = (transform.right * input.x + transform.forward * input.y) * targetSpeed;

        Vector3 planar = new Vector3(_velocity.x, 0f, _velocity.z);
        float accel = IsGrounded ? groundAcceleration : airAcceleration;

        // Acceleration is metres per second squared, full stop. The old code scaled it
        // by the target speed as well, which turned "40" into 224 m/s^2 and made every
        // ramp instantaneous -- the knob existed but could not be felt.
        if (input.sqrMagnitude > 0.001f)
            planar = Vector3.MoveTowards(planar, wish, accel * Time.deltaTime);
        else if (IsGrounded)
            planar = Vector3.MoveTowards(planar, Vector3.zero, friction * Time.deltaTime);

        _velocity.x = planar.x;
        _velocity.z = planar.z;

        // Small downward bias keeps isGrounded stable on slopes and seams.
        if (IsGrounded && _velocity.y < 0f) _velocity.y = -2f;

        bool withinCoyote = Time.time - _lastGroundedTime <= coyoteTime;

        // Walking off a ledge has to spend the ground jump. Without this the first
        // jump is gone (no ground, coyote expired) and the air jump is not available
        // either (it needs _jumpsUsed > 0) -- you fall off a crate with maxJumps = 2
        // and cannot jump at all.
        if (!IsGrounded && !withinCoyote && _jumpsUsed == 0) _jumpsUsed = 1;

        bool jumpQueued = Time.time - _lastJumpPressedTime <= jumpBuffer;
        bool canGroundJump = _jumpsUsed == 0 && withinCoyote;
        bool canAirJump = _jumpsUsed > 0 && _jumpsUsed < maxJumps;

        if (jumpQueued && !IsCrouching && (canGroundJump || canAirJump))
        {
            // v = sqrt(2 * g * h) -- the launch speed that peaks at exactly jumpHeight.
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

            _jumpsUsed++;
            _lastJumpPressedTime = -9999f;
            _lastGroundedTime = -9999f;
        }

        _velocity.y += gravity * Time.deltaTime;

        float impactSpeed = -_velocity.y;
        CollisionFlags flags = _cc.Move(_velocity * Time.deltaTime);

        // Clipping the head on an overhang has to kill the upward velocity, or you
        // hang under the ceiling for the rest of the arc still travelling up.
        if ((flags & CollisionFlags.Above) != 0 && _velocity.y > 0f) _velocity.y = 0f;

        IsGrounded = _cc.isGrounded;
        if (IsGrounded && !_wasGrounded) OnLanded(impactSpeed);
        _wasGrounded = IsGrounded;

        PlanarSpeed01 = Mathf.Clamp01(new Vector2(_velocity.x, _velocity.z).magnitude / sprintSpeed);
    }

    void OnLanded(float impactSpeed)
    {
        if (impactSpeed < landImpactThreshold) return;

        _landDip = Mathf.Min(landDipAmount, landDipAmount * impactSpeed / 12f);
        _stepAccumulator = 0f;

        if (landClip != null && footstepSource != null)
            footstepSource.PlayOneShot(landClip, footstepVolume);
        else
            PlayFootstep();

        if (fallDamage && _health != null && impactSpeed > safeFallSpeed)
            _health.ApplyDamage((impactSpeed - safeFallSpeed) * fallDamagePerSpeed, gameObject);

        Landed?.Invoke(impactSpeed);
    }

    // ======================================================================
    void HandleBobAndFootsteps()
    {
        if (cameraHolder == null) return;

        float eyeHeight = _cc.height - eyeOffsetFromTop;
        float moveAmount = IsGrounded ? PlanarSpeed01 : 0f;

        _bobTimer += Time.deltaTime * bobFrequency * (0.6f + moveAmount);
        _landDip = Mathf.MoveTowards(_landDip, 0f, landDipRecovery * landDipAmount * Time.deltaTime);

        Vector3 target = moveAmount > 0.05f
            ? new Vector3(Mathf.Cos(_bobTimer * 0.5f) * bobAmplitude * moveAmount,
                          Mathf.Sin(_bobTimer) * bobAmplitude * moveAmount, 0f)
            : Vector3.zero;

        _bobOffset = Vector3.Lerp(_bobOffset, target, bobLerpSpeed * Time.deltaTime);
        cameraHolder.localPosition = new Vector3(_bobOffset.x,
                                                 eyeHeight + _bobOffset.y - _landDip, 0f);

        if (!IsGrounded) return;

        _stepAccumulator += new Vector2(_velocity.x, _velocity.z).magnitude * Time.deltaTime;
        if (_stepAccumulator < stepDistance) return;

        _stepAccumulator = 0f;
        PlayFootstep();
    }

    void PlayFootstep()
    {
        if (footstepSource == null || footstepClips == null || footstepClips.Length == 0) return;

        var clip = footstepClips[UnityEngine.Random.Range(0, footstepClips.Length)];
        if (clip == null) return;

        footstepSource.pitch = UnityEngine.Random.Range(0.92f, 1.08f);
        footstepSource.PlayOneShot(clip, footstepVolume * (IsCrouching ? 0.4f : 1f));
    }

    public void Teleport(Vector3 position)
    {
        _cc.enabled = false;
        transform.position = position;
        _velocity = Vector3.zero;
        _cc.enabled = true;
    }
}
