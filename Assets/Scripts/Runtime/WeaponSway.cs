using UnityEngine;

/// <summary>
/// Sits on the WeaponHolder (a child of the camera) and lags the gun behind the
/// camera as you look and move. Scales itself down while aiming so sights stay
/// usable. Purely cosmetic -- it never touches the raycast.
///
/// Measured in degrees a second of turning, not degrees a frame, and smoothed exponentially: the
/// same turn swings the gun the same at 30, 60 or 144 frames a second (it swung half as far at 120
/// as at 60). The arms are carried by the head, not bolted to it: the head's own bob and lean
/// (<see cref="EyeCamera"/>) reach the gun a moment late.
/// </summary>
public class WeaponSway : MonoBehaviour
{
    [Header("Look Sway")]
    [Tooltip("Metres of gun lag per degree the view turns this frame.")]
    public float positionSway = 0.004f;
    public float maxPositionSway = 0.05f;

    [Tooltip("Degrees of gun tilt per degree the view turns this frame.")]
    public float rotationSway = 0.6f;
    public float maxRotationSway = 7f;
    public float smoothing = 9f;

    [Header("Movement Bob")]
    public float bobFrequency = 7.5f;
    public float bobAmount = 0.014f;
    public float bobRotation = 1.4f;

    [Header("Idle Breathing")]
    public float breathFrequency = 1.1f;
    public float breathAmount = 0.0035f;

    [Header("Aiming")]
    [Range(0f, 1f)] public float adsSwayMultiplier = 0.25f;

    [Header("Carried by the head")]
    [Tooltip("Share of the head's bob and lean the gun gets late (it trails the head by a spring).")]
    [Range(0f, 1f)] public float headLag = 0.5f;
    [Tooltip("How quickly the gun catches the head up, per second.")]
    [Range(2f, 40f)] public float headCatchUp = 14f;

    /// <summary>The frame rate the per-degree sway amounts were tuned at.</summary>
    const float TunedAt = 60f;

    EyeCamera _eye;
    Vector3 _headSeen, _headTurnSeen;

    Vector3 _initialPosition, _swayPos;
    Quaternion _initialRotation, _swayRot = Quaternion.identity;
    PlayerMotor _motor;
    Weapon _weapon;
    float _bobTimer;

    void Awake()
    {
        _initialPosition = _swayPos = transform.localPosition;
        _initialRotation = _swayRot = transform.localRotation;
        _motor = GetComponentInParent<PlayerMotor>();
        _weapon = GetComponentInChildren<Weapon>();
        _eye = GetComponentInParent<EyeCamera>();
    }

    void LateUpdate()
    {
        float aim = _weapon != null ? _weapon.AimProgress : 0f;
        float scale = Mathf.Lerp(1f, adsSwayMultiplier, aim);

        // Driven by the motor so sway matches whatever mouse source the look uses: as a rate, put back into
        // the degrees a frame at 60 the amounts were tuned with.
        float dt = Mathf.Max(1e-5f, Time.deltaTime);
        Vector2 look = _motor != null ? _motor.LookDeltaDegrees / (dt * TunedAt) : Vector2.zero;
        float mouseX = look.x;
        float mouseY = look.y;

        // --- positional sway from looking ---------------------------------
        float px = Mathf.Clamp(-mouseX * positionSway, -maxPositionSway, maxPositionSway);
        float py = Mathf.Clamp(-mouseY * positionSway, -maxPositionSway, maxPositionSway);

        // --- bob from moving ----------------------------------------------
        // Toned down by the movement asset: a steady gun while moving, not a swinging one.
        // The movement clips (FirstPersonMoves, played by the view model) carry the gun through the
        // stride when there are any: the bob here is then off, or the two would add up.
        float clips = FirstPersonMoves.Active != null ? 0f : 1f;
        float speed01 = (_motor != null && _motor.IsGrounded ? _motor.PlanarSpeed01 : 0f) * MovementSettings.Active.gunBob * clips;
        // On the footsteps when there is a motor: a dip with every footfall, across and back over
        // a stride, so the gun moves with the steps that are heard rather than on a clock of its own.
        if (_motor != null) _bobTimer = (_motor.StepPhase - 0.25f) * Mathf.PI * 2f;   // lowest at each footfall
        else _bobTimer += Time.deltaTime * bobFrequency * (0.5f + speed01);

        float bobX = Mathf.Cos(_bobTimer * 0.5f) * bobAmount * speed01;
        float bobY = Mathf.Sin(_bobTimer) * bobAmount * speed01;

        // --- idle breathing (the first-person clips' idle breathes instead) ---
        float breath = FirstPersonClips.Active != null && _weapon != null && _weapon.UsesClips ? 0f
                     : Mathf.Sin(Time.time * breathFrequency) * breathAmount * (1f - speed01);

        // --- carried by the head: its bob and lean reach the gun late ------
        Vector3 headBack = Vector3.zero, headTurnBack = Vector3.zero;
        if (_eye == null) _eye = GetComponentInParent<EyeCamera>();
        if (_eye != null && Application.isPlaying)
        {
            float k = 1f - Mathf.Exp(-headCatchUp * dt);
            _headSeen = Vector3.Lerp(_headSeen, _eye.Offset, k);
            _headTurnSeen = Vector3.Lerp(_headTurnSeen, _eye.Turn, k);
            headBack = (_headSeen - _eye.Offset) * headLag;
            headTurnBack = (_headTurnSeen - _eye.Turn) * headLag;
        }

        Vector3 targetPosition = _initialPosition +
                                 new Vector3(px + bobX, py + bobY + breath, 0f) * scale;

        // --- rotational sway ----------------------------------------------
        float rx = Mathf.Clamp(mouseY * rotationSway, -maxRotationSway, maxRotationSway);
        float ry = Mathf.Clamp(-mouseX * rotationSway, -maxRotationSway, maxRotationSway);
        float rz = Mathf.Clamp(-mouseX * rotationSway * 0.6f, -maxRotationSway, maxRotationSway)
                   + Mathf.Sin(_bobTimer * 0.5f) * bobRotation * speed01;

        Quaternion targetRotation = _initialRotation * Quaternion.Euler(
            new Vector3(rx, ry, rz) * scale);

        float t = 1f - Mathf.Exp(-smoothing * dt);
        // The look's sway is smoothed; the head's lag is already a spring of its own, added after.
        _swayPos = Vector3.Lerp(_swayPos, targetPosition, t);
        _swayRot = Quaternion.Slerp(_swayRot, targetRotation, t);
        transform.localPosition = _swayPos + headBack;
        transform.localRotation = _swayRot * Quaternion.Euler(headTurnBack);
    }
}
