using UnityEngine;

/// <summary>
/// The sounds the player makes, put into the <see cref="NoiseSystem"/> for enemies to hear:
/// every shot, every footfall (loud at a sprint, nothing crouched), a landing, the whoosh of
/// a swing, and every blow that lands. Added by the PlayerMotor at runtime.
/// </summary>
[DisallowMultipleComponent]
public class PlayerNoise : MonoBehaviour
{
    [Tooltip("Metres a gunshot carries.")]
    public float gunshot = 85f;
    [Tooltip("Metres footsteps carry at a sprint, a jog, and a walk while aiming.")]
    public float sprintSteps = 16f;
    public float jogSteps = 8f;
    public float aimSteps = 4f;
    [Tooltip("Metres a blow landing carries. Heard as violence: something happened over there.")]
    public float blow = 14f;
    [Tooltip("Metres a swing carries, landed or not: the whoosh and the effort. Heard as movement, " +
             "never as violence; only a blow that lands is that.")]
    public float swing = 3f;

    PlayerMotor _motor;
    Weapon _weapon;
    MeleeStrike _melee;
    float _nextStep;

    void Awake()
    {
        _motor = GetComponent<PlayerMotor>();
        _weapon = GetComponentInChildren<Weapon>();
        _melee = GetComponent<MeleeStrike>();
    }

    void OnEnable()
    {
        if (_weapon != null) _weapon.Fired += OnFired;
        if (_melee != null) { _melee.AttackStarted += OnSwing; _melee.Landed += OnBlow; }
        if (_motor != null) _motor.Landed += OnLanded;
    }

    void OnDisable()
    {
        if (_weapon != null) _weapon.Fired -= OnFired;
        if (_melee != null) { _melee.AttackStarted -= OnSwing; _melee.Landed -= OnBlow; }
        if (_motor != null) _motor.Landed -= OnLanded;
    }

    void OnFired(Weapon w) => NoiseSystem.Emit(transform.position, gunshot, NoiseKind.Gunfire, gameObject);

    void OnSwing(MeleeStrike m, MeleeStrike.Strike s, Health target) => NoiseSystem.Emit(transform.position, swing, NoiseKind.Movement, gameObject);

    void OnBlow(MeleeStrike m, MeleeStrike.Strike s, Health target)
    {
        if (target != null) NoiseSystem.Emit(target.transform.position, blow, NoiseKind.Violence, gameObject);
    }

    void OnLanded(float speed) => NoiseSystem.Emit(transform.position, Mathf.Clamp(speed, 4f, 14f), NoiseKind.Movement, gameObject);

    void Update()
    {
        if (_motor == null || !_motor.IsGrounded || _motor.IsCrouching || Time.time < _nextStep) return;

        float speed = _motor.PlanarSpeed01;
        if (speed < 0.1f) return;

        _nextStep = Time.time + 0.45f;
        float radius = _motor.IsSprinting ? sprintSteps
                     : _weapon != null && _weapon.IsAiming ? aimSteps
                     : jogSteps;
        NoiseSystem.Emit(transform.position, radius, NoiseKind.Movement, gameObject);
    }
}
