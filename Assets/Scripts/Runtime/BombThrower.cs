using System;
using UnityEngine;

/// <summary>
/// Aims and throws the bomb the player brought with them.
///
/// The aim is the feature. Holding the bomb key puts a ring on the ground where the
/// blast will land and a dotted arc showing how it gets there, and the throw is solved
/// to arrive in that ring in exactly the bomb's fall time -- one second by default. A
/// grenade you lob and hope about is a grenade nobody throws into a crowd; a ring you
/// can see two enemies standing inside is one they aim.
///
/// The range can be locked while aiming. Unlocked, the ring slides along the floor
/// wherever you look, which is what you want for placing one precisely. Locked, the
/// distance is pinned and turning sweeps the ring around you at that radius -- which is
/// what you want when the fight is moving and the distance is the part you have already
/// got right.
///
/// Charges come back every level rather than being spent forever: see
/// <see cref="StoreCatalog.BombEntry.ChargesAt"/>. A bomb the player is afraid to spend
/// is a bomb that never gets used.
/// </summary>
[DisallowMultipleComponent]
public class BombThrower : MonoBehaviour
{
    [Header("Wiring")]
    public Camera fpsCamera;

    [Tooltip("Where a thrown bomb leaves from. Falls back to just in front of the " +
             "camera, which is close enough for an arc that is metres long.")]
    public Transform throwPoint;

    [Tooltip("The ring and the dotted arc. Optional -- without one the bomb still " +
             "throws, it just throws blind.")]
    public BombAimIndicator indicator;

    [Tooltip("Bindings. Found from the player when empty, so both stay in sync.")]
    public ControlSettings controls;

    public AudioSource audioSource;

    [Header("Bomb")]
    [Tooltip("What is being thrown. PlayerLoadout writes this from the store selection; " +
             "set it by hand for a scene that never goes through the dashboard.")]
    public BombData data;

    [Tooltip("Bombs in hand. PlayerLoadout sets this from the bomb's upgrade level at " +
             "the start of a level.")]
    [Min(0)] public int charges = 2;

    [Tooltip("Bought upgrades, folded into the blast on the way out. They live here " +
             "rather than in the BombData asset for the same reason a gun's do not go " +
             "into its WeaponData: the asset is one shared instance, and a bonus written " +
             "into it would survive quitting the game and compound on every restart.")]
    [Min(0.01f)] public float damageMultiplier = 1f;

    [Min(0f)] public float radiusBonus;

    [Header("Targeting")]
    [Tooltip("What the aim ray can land on. Set this to Environment -- an aim that " +
             "landed on enemies would stick the ring to a body and slide off it.")]
    public LayerMask groundMask;

    [Tooltip("What the blast damages. Everything except the player's own layer would be " +
             "wrong here: self damage is deliberate and handled by the fraction on the " +
             "bomb, not by excluding the player from the sphere.")]
    public LayerMask damageMask = ~0;

    [Tooltip("What a bomb in flight can hit and go off against early. Environment only: " +
             "a bomb that detonated on the first enemy it clipped would never reach the " +
             "crowd behind them, which is the throw the ring promised.")]
    public LayerMask collisionMask;

    [Tooltip("Metres the aim ray reaches before giving up and dropping the ring at " +
             "maximum range. Well past the bomb's own range on purpose, so looking at a " +
             "far wall still gives a sensible answer.")]
    [Min(10f)] public float aimRayLength = 200f;

    [Header("Feel")]
    [Tooltip("Look sensitivity while aiming a bomb, as a multiplier. Below 1, because " +
             "placing a ring is a slower motion than tracking a target.")]
    [Range(0.2f, 1f)] public float aimSensitivity = 0.65f;

    // ======================================================================

    /// <summary>True while the ring is up. The weapon suppresses fire and ADS on this.</summary>
    public bool IsAiming { get; private set; }

    /// <summary>True when the player has pinned the throw distance.</summary>
    public bool RangeLocked { get; private set; }

    /// <summary>Metres from the player to the landing point, for the HUD readout.</summary>
    public float AimRange { get; private set; }

    /// <summary>False when the aim cannot be thrown at -- nothing under it, or too close.</summary>
    public bool AimValid { get; private set; }

    /// <summary>
    /// The blast this thrower would produce right now, with its upgrades folded in. The
    /// aiming ring is scaled to this rather than to the asset, so an upgraded bomb draws
    /// the bigger circle it is actually going to make.
    /// </summary>
    public BlastSpec Blast => data != null ? data.Resolve(damageMultiplier, radiusBonus) : default;

    /// <summary>Seconds until another bomb can be thrown. 0 when ready.</summary>
    public float CooldownRemaining => Mathf.Max(0f, _nextThrowTime - Time.time);

    /// <summary>True when there is a bomb, a charge for it, and the cooldown has run out.</summary>
    public bool CanThrow => data != null && charges > 0 && Time.time >= _nextThrowTime;

    /// <summary>Raised after a bomb leaves the hand, with the charges left.</summary>
    public event Action<BombThrower, int> Thrown;

    /// <summary>Raised when the key is pressed with nothing to throw. The HUD flashes on it.</summary>
    public event Action<BombThrower> Denied;

    float _nextThrowTime;
    float _lockedRange;

    /// <summary>Edge tracker for the lock toggle, so holding the key toggles once.</summary>
    bool _lockWasDown;

    /// <summary>Set once per key hold, so an empty belt flashes the HUD rather than strobing it.</summary>
    bool _deniedAnnounced;
    PlayerMotor _motor;
    ControlSettings _fallbackControls;

    ControlSettings Bindings
    {
        get
        {
            if (controls != null) return controls;
            if (_motor != null && _motor.controls != null) return _motor.controls;

            return _fallbackControls ??= ControlSettings.CreateDefault();
        }
    }

    void Awake()
    {
        if (fpsCamera == null) fpsCamera = Camera.main;
        _motor = GetComponentInParent<PlayerMotor>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    void OnDisable()
    {
        // Aiming slows the look down. Leaving that behind on a disabled thrower would
        // quietly stick the player with sluggish mouse look and no way back -- the same
        // trap Weapon.OnDisable exists for.
        StopAiming();
    }

    void Update()
    {
        if (!PlayerMotor.InputEnabled || data == null || fpsCamera == null)
        {
            StopAiming();
            return;
        }

        var bindings = Bindings;

        bool wants = ControlSettings.Held(bindings.bomb) || MobileInput.BombAim;

        if (!wants)
        {
            // Released with a live aim: that is the throw.
            if (IsAiming) Release();
            else _deniedAnnounced = false;

            return;
        }

        if (!IsAiming) BeginAiming();

        // BeginAiming refuses on an empty belt, so this is what stops a ring being drawn
        // for a bomb that is not going to be thrown.
        if (!IsAiming) return;

        UpdateLockToggle(bindings);
        UpdateAim();
    }

    // ======================================================================
    void BeginAiming()
    {
        if (charges <= 0 || data == null)
        {
            // Said once per hold rather than every frame the key is down, so the HUD
            // flash is a flash and not a strobe.
            if (!_deniedAnnounced) Denied?.Invoke(this);
            _deniedAnnounced = true;
            return;
        }

        IsAiming = true;
        RangeLocked = false;
        _lockedRange = 0f;
        _lockWasDown = false;

        if (_motor != null) _motor.LookSensitivityMultiplier = aimSensitivity;
    }

    void StopAiming()
    {
        _deniedAnnounced = false;

        if (!IsAiming)
        {
            _lockWasDown = false;
            return;
        }

        IsAiming = false;
        RangeLocked = false;
        _lockWasDown = false;

        if (indicator != null) indicator.Hide();

        // Handed back rather than assumed: Weapon writes this too, and whichever of the
        // two last let go owns putting it back to 1.
        if (_motor != null) _motor.LookSensitivityMultiplier = 1f;
    }

    /// <summary>
    /// The aim key doubles as the lock while a bomb is up.
    ///
    /// Reusing it rather than adding a binding is deliberate: "aim to lock" is a sentence
    /// a player can be told in four words on the instruction strip, and the weapon is
    /// already suppressed while the ring is up so nothing is competing for it.
    /// </summary>
    void UpdateLockToggle(ControlSettings bindings)
    {
        bool down = ControlSettings.Held(bindings.aim) || MobileInput.Aim;

        if (down && !_lockWasDown)
        {
            RangeLocked = !RangeLocked;
            if (RangeLocked) _lockedRange = AimRange;
        }

        _lockWasDown = down;
    }

    // ======================================================================
    void UpdateAim()
    {
        Vector3 origin = ThrowOrigin();
        Vector3 landing = ResolveLanding(out Vector3 normal, out bool valid);

        AimValid = valid;
        AimRange = Vector3.Distance(FlatOf(origin), FlatOf(landing));

        if (indicator == null) return;

        Vector3 velocity = BombProjectile.SolveVelocity(origin, landing, data.fallTime,
                                                        data.gravityScale);

        indicator.Show(origin, landing, velocity, data.fallTime, data.gravityScale,
                       Blast.radius, normal, valid, RangeLocked);
    }

    /// <summary>
    /// Where the bomb is going to land, and whether it may be thrown there.
    ///
    /// Two modes. Unlocked, the camera ray is traced and the ring goes where it lands,
    /// clamped into the bomb's range -- so looking at your feet still gives a throw at
    /// minimum range rather than a bomb dropped on your own head. Locked, the distance
    /// is fixed and only the bearing follows the camera, so the ring sweeps around you.
    ///
    /// Either way the point is dropped onto the floor with a downward ray, which is what
    /// keeps the ring on the ground when the aim is over a gap or off the end of a
    /// walkway.
    /// </summary>
    Vector3 ResolveLanding(out Vector3 normal, out bool valid)
    {
        normal = Vector3.up;
        valid = true;

        Transform cam = fpsCamera.transform;
        Vector3 player = FlatOf(transform.position);

        Vector3 bearing = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        if (bearing.sqrMagnitude < 0.0001f) bearing = transform.forward;
        bearing.Normalize();

        float range;

        if (RangeLocked)
        {
            range = Mathf.Clamp(_lockedRange, data.minRange, data.maxRange);
        }
        else
        {
            // Where the eye is actually pointing, if it meets anything.
            Vector3 aimPoint = Physics.Raycast(cam.position, cam.forward, out RaycastHit look,
                                               aimRayLength, groundMask,
                                               QueryTriggerInteraction.Ignore)
                ? look.point
                : cam.position + cam.forward * data.maxRange;

            range = Vector3.Distance(player, FlatOf(aimPoint));

            // Out of range is not an invalid throw, it is a clamped one. A ring that
            // vanished when you looked too far would be a control that stops working
            // exactly when the fight gets interesting.
            range = Mathf.Clamp(range, data.minRange, data.maxRange);
        }

        Vector3 flat = player + bearing * range;

        // Dropped onto whatever floor is under that spot. Started well above the
        // player's head so a target up a ramp is found rather than missed from below.
        Vector3 from = flat + Vector3.up * 40f;

        if (Physics.Raycast(from, Vector3.down, out RaycastHit ground, 200f, groundMask,
                            QueryTriggerInteraction.Ignore))
        {
            normal = ground.normal;
            return ground.point;
        }

        // Nothing under the aim: a hole, or off the edge of the level. The bomb would
        // fall forever, so the throw is refused and the ring says so in red.
        valid = false;
        return new Vector3(flat.x, transform.position.y, flat.z);
    }

    Vector3 ThrowOrigin()
        => throwPoint != null
            ? throwPoint.position
            : fpsCamera.transform.position + fpsCamera.transform.forward * 0.6f;

    static Vector3 FlatOf(Vector3 point) => new Vector3(point.x, 0f, point.z);

    // ======================================================================
    void Release()
    {
        Vector3 origin = ThrowOrigin();
        Vector3 landing = ResolveLanding(out _, out bool valid);

        StopAiming();

        if (!valid || !CanThrow) return;

        Throw(origin, landing);
    }

    /// <summary>
    /// Sends one. Public so a test can throw without synthesising a key press, and so a
    /// touch button can hand in a target of its own.
    /// </summary>
    public void Throw(Vector3 origin, Vector3 landing)
    {
        if (data == null || data.bombPrefab == null || charges <= 0) return;

        landing = ClampToRange(origin, landing);

        charges--;
        _nextThrowTime = Time.time + data.throwCooldown;

        var bomb = Instantiate(data.bombPrefab, origin, UnityEngine.Random.rotation);

        var projectile = bomb.GetComponent<BombProjectile>();
        if (projectile != null)
        {
            projectile.Launch(landing, data, Blast, gameObject, damageMask, collisionMask, gameObject);
        }
        else
        {
            // A prefab with no projectile on it cannot fly, so it is detonated where it
            // stands rather than left lying in the arena as a prop.
            Debug.LogWarning($"[BombThrower] {data.bombPrefab.name} has no BombProjectile, so the " +
                             "bomb went off in your hand. Rebuild the scene.", this);

            Explosion.Blast(origin, Blast, gameObject, damageMask, gameObject);
            Destroy(bomb);
        }

        if (data.throwClip != null && audioSource != null)
            audioSource.PlayOneShot(data.throwClip, 0.8f);

        Thrown?.Invoke(this, charges);
    }

    /// <summary>
    /// Pulls a landing point back into the bomb's range.
    ///
    /// ResolveLanding already clamps what the aim can produce, so this only matters for
    /// a caller that hands in a point of its own. It matters anyway: the range is the
    /// arc's whole shape -- the throw is solved to arrive in a fixed time, so doubling
    /// the distance doubles the speed, and a bomb sent far enough will simply meet the
    /// first wall between here and there. Clamping is what keeps Throw's contract the
    /// same one the ring draws.
    /// </summary>
    Vector3 ClampToRange(Vector3 origin, Vector3 landing)
    {
        Vector3 flatOrigin = FlatOf(origin);
        Vector3 offset = FlatOf(landing) - flatOrigin;

        float distance = offset.magnitude;
        if (distance <= data.maxRange && distance >= data.minRange) return landing;

        Vector3 bearing = distance > 0.001f ? offset / distance : transform.forward;
        float clamped = Mathf.Clamp(distance, data.minRange, data.maxRange);

        Vector3 flat = flatOrigin + bearing * clamped;
        return new Vector3(flat.x, landing.y, flat.z);
    }

    /// <summary>
    /// Arms the thrower for a level. Called by PlayerLoadout once the store selection is
    /// known, which is why the fields above are only defaults.
    /// </summary>
    public void Equip(BombData bomb, int chargeCount)
    {
        StopAiming();

        data = bomb;
        charges = Mathf.Max(0, chargeCount);
        _nextThrowTime = 0f;

        // Cleared, not kept. They belong to whichever bomb was equipped before, and a
        // player who swapped to a fresh one would otherwise carry the old one's upgrades
        // onto it. PlayerLoadout writes this bomb's own immediately after.
        damageMultiplier = 1f;
        radiusBonus = 0f;
    }
}
