using UnityEngine;

/// <summary>
/// Picking a gun up from the ground (fists when out of ammo, Part B): the player's own, dropped when it ran dry,
/// or an enemy's rifle. The nearest <see cref="GroundGun"/> in reach that the player is looking towards is the
/// <see cref="Candidate"/>; the interact key (<see cref="ControlSettings.interact"/>, E), the pad's D-pad up or,
/// on a phone, the RELOAD button while it reads PICK UP takes it (<see cref="Weapon.TakeUp"/>). Armed, the gun in
/// the hands is put down in its place. The HUD shows the prompt from <see cref="Candidate"/>.
///
/// Added by <see cref="PlayerMotor"/> at runtime, so every arena already built has it.
/// </summary>
[DisallowMultipleComponent]
public class GunPickup : MonoBehaviour
{
    [Tooltip("Metres from the player within which a gun on the ground can be picked up.")]
    [Range(0.5f, 4f)] public float reach = 2.2f;

    [Tooltip("Degrees off where the player is looking a gun may lie and still be the one picked up (closer than a " +
             "metre, any direction: it is at the player's feet).")]
    [Range(10f, 90f)] public float cone = 55f;

    PlayerMotor _motor;
    Weapon _weapon;
    float _nextLook;

    /// <summary>The gun the interact key would take now, or null.</summary>
    public GroundGun Candidate { get; private set; }

    /// <summary>Raised when the gun on offer changes (the HUD redraws its prompt).</summary>
    public event System.Action<GunPickup> CandidateChanged;

    void Awake() => _motor = GetComponent<PlayerMotor>();

    void Update()
    {
        if (_weapon == null) _weapon = GetComponentInChildren<Weapon>();
        if (_weapon == null || _motor == null) return;

        bool able = PlayerMotor.InputEnabled;
        if (Time.time >= _nextLook)
        {
            _nextLook = Time.time + 0.1f;
            var found = able ? Find() : null;
            if (found != Candidate)
            {
                Candidate = found;
                CandidateChanged?.Invoke(this);
            }
        }
        if (Candidate != null && !Candidate.Available) { Candidate = null; CandidateChanged?.Invoke(this); }

        MobileInput.PickUpReady = able && _weapon.Disarmed && Candidate != null;

        // Read every frame, so a press made with nothing in reach is not kept and spent later.
        var keys = _motor.controls;
        // The phone's queued press is only taken while the button reads PICK UP; otherwise it may be a DRIVE (DrivableCar).
        bool press = (keys != null && ControlSettings.Pressed(keys.interact))
                     | GameInput.PadPressed(GameAction.Interact) | (MobileInput.PickUpReady && MobileInput.ConsumeInteract());
        if (press && able && Candidate != null && _weapon.TakeUp(Candidate))
        {
            Candidate = null;
            CandidateChanged?.Invoke(this);
        }
    }

    /// <summary>The nearest gun in reach, among those the player is looking towards (or standing on).</summary>
    GroundGun Find()
    {
        var cam = _weapon.fpsCamera != null ? _weapon.fpsCamera.transform : transform;
        Vector3 feet = transform.position;
        GroundGun best = null;
        float bestScore = float.MaxValue;
        foreach (var g in GroundGun.All)
        {
            if (g == null || !g.Available) continue;
            // An empty gun is no use (picked up, it is dropped again a moment later), unless rounds were kept for it.
            if (g.magazine + g.reserve <= 0 && !(_weapon.StoredRounds > 0 && _weapon.StoredFor == g.data)) continue;
            Vector3 at = g.Centre;
            Vector3 flat = at - feet;
            flat.y = 0f;
            float distance = flat.magnitude;
            if (distance > reach || Mathf.Abs(at.y - feet.y) > 2.2f) continue;
            float angle = Vector3.Angle(cam.forward, at - cam.position);
            if (distance > 1f && angle > cone) continue;
            float score = distance + angle * 0.02f;
            if (score < bestScore) { bestScore = score; best = g; }
        }
        return best;
    }
}
