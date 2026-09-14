using UnityEngine;

/// <summary>
/// Sits on the WeaponHolder (a child of the camera) and lags the gun behind the
/// camera as you look and move. Scales itself down while aiming so sights stay
/// usable. Purely cosmetic -- it never touches the raycast.
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

    Vector3 _initialPosition;
    Quaternion _initialRotation;
    PlayerMotor _motor;
    Weapon _weapon;
    float _bobTimer;

    void Awake()
    {
        _initialPosition = transform.localPosition;
        _initialRotation = transform.localRotation;
        _motor = GetComponentInParent<PlayerMotor>();
        _weapon = GetComponentInChildren<Weapon>();
    }

    void LateUpdate()
    {
        float aim = _weapon != null ? _weapon.AimProgress : 0f;
        float scale = Mathf.Lerp(1f, adsSwayMultiplier, aim);

        // Driven by the motor so sway matches whatever mouse source the look uses.
        Vector2 look = _motor != null ? _motor.LookDeltaDegrees : Vector2.zero;
        float mouseX = look.x;
        float mouseY = look.y;

        // --- positional sway from looking ---------------------------------
        float px = Mathf.Clamp(-mouseX * positionSway, -maxPositionSway, maxPositionSway);
        float py = Mathf.Clamp(-mouseY * positionSway, -maxPositionSway, maxPositionSway);

        // --- bob from moving ----------------------------------------------
        float speed01 = _motor != null && _motor.IsGrounded ? _motor.PlanarSpeed01 : 0f;
        _bobTimer += Time.deltaTime * bobFrequency * (0.5f + speed01);

        float bobX = Mathf.Cos(_bobTimer * 0.5f) * bobAmount * speed01;
        float bobY = Mathf.Sin(_bobTimer) * bobAmount * speed01;

        // --- idle breathing -----------------------------------------------
        float breath = Mathf.Sin(Time.time * breathFrequency) * breathAmount * (1f - speed01);

        Vector3 targetPosition = _initialPosition +
                                 new Vector3(px + bobX, py + bobY + breath, 0f) * scale;

        // --- rotational sway ----------------------------------------------
        float rx = Mathf.Clamp(mouseY * rotationSway, -maxRotationSway, maxRotationSway);
        float ry = Mathf.Clamp(-mouseX * rotationSway, -maxRotationSway, maxRotationSway);
        float rz = Mathf.Clamp(-mouseX * rotationSway * 0.6f, -maxRotationSway, maxRotationSway)
                   + Mathf.Sin(_bobTimer * 0.5f) * bobRotation * speed01;

        Quaternion targetRotation = _initialRotation * Quaternion.Euler(
            new Vector3(rx, ry, rz) * scale);

        float t = Mathf.Clamp01(smoothing * Time.deltaTime);
        transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, t);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, t);
    }
}
