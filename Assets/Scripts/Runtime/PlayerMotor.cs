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
    public float walkSpeed = 5.6f;
    public float sprintSpeed = 8.2f;
    public float crouchSpeed = 2.8f;

    [Tooltip("High values give the near-instant starts and stops of Valorant/CS. " +
             "Drop toward 15 for a heavier, Battlefield-style ramp.")]
    public float groundAcceleration = 40f;
    public float airAcceleration = 9f;
    public float friction = 35f;

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

    [Header("Footsteps (optional)")]
    public AudioSource footstepSource;
    public AudioClip[] footstepClips;
    public float stepDistance = 2.4f;
    [Range(0f, 1f)] public float footstepVolume = 0.5f;

    /// <summary>
    /// Global input gate. HUDController switches this off on game over and back
    /// on when a scene loads, so movement and firing both stop at once.
    /// </summary>
    public static bool InputEnabled = true;

    // ---- state read by the weapon, sway and HUD --------------------------
    public bool IsSprinting { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool IsGrounded { get; private set; }

    /// <summary>0 when still, 1 at full sprint speed. Drives weapon spread and bob.</summary>
    public float PlanarSpeed01 { get; private set; }

    /// <summary>Multiplier on look sensitivity. Weapon drives this down while aiming.</summary>
    public float LookSensitivityMultiplier { get; set; } = 1f;

    /// <summary>Degrees the view turned this frame (x = yaw, y = pitch). Drives weapon sway.</summary>
    public Vector2 LookDeltaDegrees { get; private set; }

    CharacterController _cc;
    Vector3 _velocity;                    // world space, includes vertical
    float _yaw, _pitch;
    float _recoilPitch, _recoilYaw;
    float _recoilRecovery = 9f;
    float _lastGroundedTime, _lastJumpPressedTime;
    int _jumpsUsed;
    float _lastSprintTapTime = -99f;
    bool _sprintLatched;
    float _bobTimer, _stepAccumulator;
    Vector3 _bobOffset;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        standHeight = _cc.height;
        _yaw = transform.eulerAngles.y;

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
    void HandleCursor()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            SetCursorLocked(false);
        else if (lockCursor && !MobileInput.Active && Input.GetMouseButtonDown(0) &&
                 Cursor.lockState != CursorLockMode.Locked)
            SetCursorLocked(true);
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

            _yaw += LookDeltaDegrees.x;
            _pitch -= LookDeltaDegrees.y * (invertY ? -1f : 1f);
            _pitch = Mathf.Clamp(_pitch, -pitchClamp, pitchClamp);
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
                _yaw += touch.x;
                _pitch -= touch.y * (invertY ? -1f : 1f);
                _pitch = Mathf.Clamp(_pitch, -pitchClamp, pitchClamp);

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

        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

        if (cameraHolder != null)
            cameraHolder.localRotation = Quaternion.Euler(_pitch - _recoilPitch, _recoilYaw, 0f);
    }

    /// <summary>
    /// Raw mouse counts since the last frame -- no smoothing, no acceleration, no
    /// Input Manager scaling. This 1:1 response is what separates a shooter that
    /// feels tight from one that feels like dragging the view through syrup.
    /// </summary>
    /// <summary>
    /// Sprint is a double-tap rather than a held key, so the sprint key can double
    /// as the fire key. The first tap still fires -- that is unavoidable when one
    /// key carries both jobs, and in practice it reads fine.
    /// </summary>
    bool EvaluateSprint(Vector2 moveInput)
    {
        if (MobileInput.Sprint && moveInput.y > 0.1f) return true;

        if (!controls.sprintByDoubleTap)
            return ControlSettings.Held(controls.sprintKey) && moveInput.y > 0.1f;

        if (ControlSettings.Pressed(controls.sprintKey))
        {
            if (Time.time - _lastSprintTapTime <= controls.doubleTapWindow) _sprintLatched = true;
            _lastSprintTapTime = Time.time;
        }

        // Drop out of the sprint once you stop advancing.
        if (controls.sprintEndsWhenNotAdvancing && moveInput.y <= 0.1f) _sprintLatched = false;

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

    Vector2 ReadMouseCounts()
    {
#if ENABLE_INPUT_SYSTEM
        if (rawMouseInput && Mouse.current != null) return Mouse.current.delta.ReadValue();
#endif
        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * legacyAxisScale;
    }

    /// <summary>Called by Weapon on every shot. Vertical is degrees up, horizontal is plus/minus yaw.</summary>
    public void AddRecoil(float vertical, float horizontal, float recovery = 9f)
    {
        _recoilPitch += vertical;
        _recoilYaw += Random.Range(-horizontal, horizontal);
        _recoilRecovery = Mathf.Max(0.1f, recovery);
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
                                  ~0, QueryTriggerInteraction.Ignore);
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

        float targetSpeed = IsCrouching ? crouchSpeed : (IsSprinting ? sprintSpeed : walkSpeed);
        Vector3 wish = (transform.right * input.x + transform.forward * input.y) * targetSpeed;

        Vector3 planar = new Vector3(_velocity.x, 0f, _velocity.z);
        float accel = IsGrounded ? groundAcceleration : airAcceleration;

        if (input.sqrMagnitude > 0.001f)
            planar = Vector3.MoveTowards(planar, wish, accel * targetSpeed * Time.deltaTime);
        else if (IsGrounded)
            planar = Vector3.MoveTowards(planar, Vector3.zero, friction * targetSpeed * Time.deltaTime);

        _velocity.x = planar.x;
        _velocity.z = planar.z;

        // Small downward bias keeps isGrounded stable on slopes and seams.
        if (IsGrounded && _velocity.y < 0f) _velocity.y = -2f;

        bool withinCoyote = Time.time - _lastGroundedTime <= coyoteTime;
        bool jumpQueued = Time.time - _lastJumpPressedTime <= jumpBuffer;

        // First jump needs ground (or coyote time); later ones are air jumps.
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
        _cc.Move(_velocity * Time.deltaTime);

        PlanarSpeed01 = Mathf.Clamp01(new Vector2(_velocity.x, _velocity.z).magnitude / sprintSpeed);
    }

    // ======================================================================
    void HandleBobAndFootsteps()
    {
        if (cameraHolder == null) return;

        float eyeHeight = _cc.height - eyeOffsetFromTop;
        float moveAmount = IsGrounded ? PlanarSpeed01 : 0f;

        _bobTimer += Time.deltaTime * bobFrequency * (0.6f + moveAmount);

        Vector3 target = moveAmount > 0.05f
            ? new Vector3(Mathf.Cos(_bobTimer * 0.5f) * bobAmplitude * moveAmount,
                          Mathf.Sin(_bobTimer) * bobAmplitude * moveAmount, 0f)
            : Vector3.zero;

        _bobOffset = Vector3.Lerp(_bobOffset, target, bobLerpSpeed * Time.deltaTime);
        cameraHolder.localPosition = new Vector3(_bobOffset.x, eyeHeight + _bobOffset.y, 0f);

        if (!IsGrounded) return;

        _stepAccumulator += new Vector2(_velocity.x, _velocity.z).magnitude * Time.deltaTime;
        if (_stepAccumulator < stepDistance) return;

        _stepAccumulator = 0f;
        PlayFootstep();
    }

    void PlayFootstep()
    {
        if (footstepSource == null || footstepClips == null || footstepClips.Length == 0) return;

        var clip = footstepClips[Random.Range(0, footstepClips.Length)];
        if (clip == null) return;

        footstepSource.pitch = Random.Range(0.92f, 1.08f);
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
