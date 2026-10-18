using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The first-person view as a pair of eyes in a head (smooth-animation brief, 2026-10-18). On the camera
/// under the motor's holder, after the motor has turned and placed the holder and before the arms are
/// posed, so nothing is drawn a frame behind:
///
/// - **Steps.** A dip just after each footfall, a sway over the planted foot, a roll with the weight and a
///   small nod, all on <see cref="PlayerMotor.StepPhase"/> (distance walked), so the bob and the footstep
///   sound are one event. Almost nothing at a careful step or crouched; strongest at a sprint.
/// - **Inertia.** The head is carried: it is left behind a little as the body starts or stops, pitches
///   with it and settles on a spring that overshoots a touch; it leans against a sidestep and rolls a
///   little into a turn. The aim itself never lags: only position and roll move for a turn.
/// - **Landing.** A dip and a nod, sprung back.
/// - **Breathing.** Slow at rest, fast and deep after a sprint.
/// - **Aiming down the sights** takes almost all of it away.
///
/// Every amount is in <see cref="EyeCameraSettings"/> and scaled by the Head Bob setting; shake stays the
/// motor's and the Camera Shake setting's. A strike never moves it (the camera rule of the player body):
/// a strike does not move the player, so nothing here sees it. Every spring is stepped in fixed slices,
/// so the view moves the same at 30, 60 or 144 frames a second.
///
/// Added by <see cref="PlayerMotor"/> at runtime, so every arena already built has it.
/// </summary>
[DefaultExecutionOrder(150)]   // after the motor (0), before the view model (200) and the body (300)
[DisallowMultipleComponent]
public class EyeCamera : MonoBehaviour
{
    PlayerMotor _motor;
    Transform _eye;
    Weapon _weapon;

    // Springs: the head's lag (metres, body space: right, up, ahead), its tilt (degrees: pitch down,
    // roll left), and the landing (metres down, degrees of nod).
    Vector3 _lag, _lagVel;
    Vector2 _tilt, _tiltVel;
    float _land, _landVel, _landNod, _landNodVel;

    Vector3 _lastPlanar;
    bool _haveLast;
    float _stepAmount;      // the steps' amounts, eased so a landing or a stop never cuts the bob off
    float _exertion;        // 0 rested, 1 blown
    float _breathPhase;

    Volume _blur;
    VolumeProfile _blurProfile;
    MotionBlur _blurOverride;

    static EyeCameraSettings S => EyeCameraSettings.Active;

    /// <summary>The offset and turn put on the camera this frame, in the holder's space (for tests).</summary>
    public Vector3 Offset { get; private set; }
    public Vector3 Turn { get; private set; }

    void Awake()
    {
        _motor = GetComponent<PlayerMotor>();
        _breathPhase = Random.value;
    }

    void OnEnable()
    {
        if (_motor != null) _motor.Landed += OnLanded;
    }

    void OnDisable()
    {
        if (_motor != null) _motor.Landed -= OnLanded;
        if (_eye != null) { _eye.localPosition = Vector3.zero; _eye.localRotation = Quaternion.identity; }
        if (_blur != null) Destroy(_blur.gameObject);
        if (_blurProfile != null) Destroy(_blurProfile);
        _blur = null;
        _blurProfile = null;
        _blurOverride = null;
    }

    void OnLanded(float impactSpeed)
    {
        var s = S;
        float hard = Mathf.Max(0f, impactSpeed - s.landFrom);
        if (hard <= 0f) return;
        _landVel -= hard * s.landKick;
        // About landNod degrees at its deepest for a five-metre-a-second landing.
        _landNodVel += s.landNod * s.settle * 0.8f * 1.6f * Mathf.Min(1.5f, hard / 5f);
    }

    void LateUpdate()
    {
        if (_motor == null || _motor.cameraHolder == null) return;
        if (_eye == null)
        {
            var cam = _motor.cameraHolder.GetComponentInChildren<Camera>();
            if (cam == null) return;
            _eye = cam.transform;
        }
        if (_weapon == null) _weapon = GetComponentInChildren<Weapon>();

        var s = S;
        float dt = Time.deltaTime;
        float comfort = GameSettings.HeadBob;
        float aim = _weapon != null ? _weapon.AimProgress : 0f;
        float keep = Mathf.Lerp(1f, s.aimedMotion, aim) * comfort;

        // ---- steps -----------------------------------------------------------------------
        Vector3 v = _motor.Velocity;
        Vector3 planar = new Vector3(v.x, 0f, v.z);
        float speed = _motor.IsGrounded ? planar.magnitude : 0f;
        s.At(speed, out float dip, out float sway, out float roll, out float nod);
        float crouch = _motor.IsCrouching ? s.crouched : 1f;
        _stepAmount = Mathf.Lerp(_stepAmount, crouch, 1f - Mathf.Exp(-dt * 8f));
        float steps = _stepAmount * keep;

        float phase = _motor.StepPhase;
        float fall = 0.5f * (1f + Mathf.Cos((phase - s.dipAfterFootfall) * Mathf.PI * 2f));   // 1 just after each footfall
        float side = Mathf.Sin(phase * Mathf.PI);                                              // over each foot in turn
        Vector3 stepOffset = new Vector3(sway * side, -dip * fall, 0f) * steps;
        Vector2 stepTurn = new Vector2(nod * fall, -roll * side) * steps;

        // ---- inertia ---------------------------------------------------------------------
        Vector3 accel = Vector3.zero;
        if (_haveLast && dt > 0f) accel = Vector3.ClampMagnitude((planar - _lastPlanar) / dt, 40f);
        _lastPlanar = planar;
        _haveLast = true;
        Vector3 local = transform.InverseTransformDirection(accel);
        Vector3 lagWant = Vector3.ClampMagnitude(new Vector3(-local.x, 0f, -local.z) * s.lagPerAccel, s.maxLag);

        float lateral = Vector3.Dot(planar, transform.right) / Mathf.Max(0.5f, MovementSettings.Active.jogSpeed);
        float yawRate = dt > 0f ? Mathf.Clamp(_motor.LookDeltaDegrees.x / dt, -720f, 720f) : 0f;
        Vector2 tiltWant = new Vector2(
            -local.z * s.pitchPerAccel,
            -s.strafeLean * Mathf.Clamp(lateral, -1f, 1f) - s.turnRoll * yawRate / 100f);
        tiltWant = Vector2.ClampMagnitude(tiltWant, s.maxTilt);
        if (!_motor.IsGrounded) tiltWant.y *= 0.5f;

        // ---- breathing -------------------------------------------------------------------
        bool blowing = _motor.IsSprinting && speed > 1f;
        _exertion = Mathf.MoveTowards(_exertion, blowing ? 1f : 0f, dt / Mathf.Max(0.1f, blowing ? s.blowSeconds : s.recoverSeconds));
        float blown = Mathf.Max(_exertion, 1f - _motor.Stamina01);
        _breathPhase = Mathf.Repeat(_breathPhase + dt * Mathf.Lerp(s.restRate, s.blownRate, blown) / 60f, 1f);
        float breath = Mathf.Sin(_breathPhase * Mathf.PI * 2f) * Mathf.Lerp(1f, s.aimedBreath, aim) * comfort;
        float breathPitch = -breath * Mathf.Lerp(s.restPitch, s.blownPitch, blown);
        float breathLift = breath * Mathf.Lerp(s.restLift, s.blownLift, blown);

        // ---- springs, in fixed slices ----------------------------------------------------
        const float Slice = 1f / 240f;
        float left = dt;
        while (left > 1e-6f)
        {
            float h = Mathf.Min(Slice, left);
            left -= h;
            Spring(ref _lag, ref _lagVel, lagWant, s.settle, s.damping, h);
            Spring2(ref _tilt, ref _tiltVel, tiltWant, s.settle, s.damping, h);
            Spring1(ref _land, ref _landVel, 0f, s.settle * 0.8f, s.damping, h);
            Spring1(ref _landNod, ref _landNodVel, 0f, s.settle * 0.8f, s.damping, h);
        }
        _land = Mathf.Clamp(_land, -0.15f, 0.05f);

        Vector3 offset = stepOffset + _lag * keep + Vector3.up * (_land * comfort + breathLift);
        Vector3 turn = new Vector3(stepTurn.x + _tilt.x * keep + _landNod * comfort + breathPitch, 0f, stepTurn.y + _tilt.y * keep);
        Offset = offset;
        Turn = turn;
        _eye.localPosition = offset;
        _eye.localRotation = Quaternion.Euler(turn);

        UpdateBlur(s);
    }

    static void Spring(ref Vector3 x, ref Vector3 v, Vector3 target, float w, float zeta, float h)
    {
        v += (w * w * (target - x) - 2f * zeta * w * v) * h;
        x += v * h;
    }

    static void Spring2(ref Vector2 x, ref Vector2 v, Vector2 target, float w, float zeta, float h)
    {
        v += (w * w * (target - x) - 2f * zeta * w * v) * h;
        x += v * h;
    }

    static void Spring1(ref float x, ref float v, float target, float w, float zeta, float h)
    {
        v += (w * w * (target - x) - 2f * zeta * w * v) * h;
        x += v * h;
    }

    /// <summary>Motion blur, only when the player has turned it on (Settings > Video).</summary>
    void UpdateBlur(EyeCameraSettings s)
    {
        bool on = GameSettings.MotionBlur;
        if (!on && _blur == null) return;
        if (_blur == null)
        {
            var go = new GameObject("EyeCameraBlur");
            go.transform.SetParent(transform, false);
            _blur = go.AddComponent<Volume>();
            _blur.isGlobal = true;
            _blur.priority = 80f;
            _blurProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _blurProfile.name = "EyeCameraBlur";
            _blur.sharedProfile = _blurProfile;
            _blurOverride = _blurProfile.Add<MotionBlur>(true);
            _blurOverride.quality.Override(MotionBlurQuality.Low);
        }
        _blur.enabled = on;
        _blurOverride.intensity.Override(on ? s.blurIntensity : 0f);
        _blurOverride.clamp.Override(s.blurClamp);
    }
}
