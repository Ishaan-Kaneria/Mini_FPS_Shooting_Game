using System;
using UnityEngine;

/// <summary>
/// Aims and throws the bomb the player brought with them.
///
/// The aim is the feature. Holding the bomb key puts a ring on the ground where the
/// blast will land and a dotted arc showing how it gets there, and the throw is solved
/// to arrive in that ring in exactly the time the arc was drawn for -- under half a
/// second up close, a second at the far end of the range. A grenade you lob and hope
/// about is a grenade nobody throws into a crowd; a ring you can see two enemies
/// standing inside is one they aim.
///
/// Tap the key and the ring stays up with nothing held down; tap again to throw. Hold
/// it and releasing is the throw. Both exist because the hold is the better control and
/// is impossible on a laptop: a touchpad stops reporting motion while a key is held --
/// libinput's disable-while-typing, on by default -- so "hold this and move the pointer"
/// is a gesture most laptop players physically cannot make.
///
/// Holding the key hands the mouse to a cursor. The reticle starts under the crosshair
/// and the mouse moves it across the screen instead of turning the head, and the bomb
/// goes wherever it is pointing; pushing it into the edge of the screen turns the view,
/// so nothing is out of reach. That is the control a player expects from a thrown
/// explosive -- "put it there" -- and the alternative, aiming it by turning your whole
/// body, reads as the mouse having stopped working.
///
/// A touch screen aims by looking instead, whatever `cursorAiming` says: there is no
/// pointer to move on a phone. That path maps how far below the horizon you are looking
/// to how far out the ring sits.
///
/// The range can be locked while aiming. Locked, the distance is pinned and only the
/// bearing follows the aim, so the ring sweeps around you at that radius -- which is
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

    [Tooltip("Metres the downward ray searches for floor under the aim before giving " +
             "up and calling the throw invalid. Generous on purpose: a target at the " +
             "bottom of a stairwell is still a target.")]
    [Min(10f)] public float groundRayLength = 200f;

    [Header("Feel")]
    [Tooltip("Look sensitivity while aiming a bomb, as a multiplier. Below 1, because " +
             "placing a ring is a slower motion than tracking a target.")]
    [Range(0.2f, 1f)] public float aimSensitivity = 0.65f;

    [Tooltip("How far below the horizon the player has to look for the ring to come " +
             "all the way in to minimum range. Everything between there and the " +
             "horizon slides the distance evenly, so the mouse moves the ring the way " +
             "it moves everything else.\n\n" +
             "Only used when the cursor is off, or on a touch screen, where there is " +
             "no cursor to move.")]
    [Range(15f, 85f)] public float aimPitchAtMinRange = 55f;

    [Header("Cursor")]
    [Tooltip("Hold the bomb key and the mouse moves a cursor across the screen rather " +
             "than turning your head, and the bomb goes where the cursor is pointing. " +
             "Switch it off to aim by looking instead.\n\n" +
             "A touch screen always aims by looking, whatever this says: there is no " +
             "pointer to move on a phone.")]
    public bool cursorAiming = true;

    [Tooltip("Cursor speed against look speed. 1 moves the cursor exactly as far " +
             "across the screen as the view would have turned, so the same flick of " +
             "the mouse puts the cursor where the crosshair would have ended up.")]
    [Range(0.25f, 3f)] public float cursorSensitivity = 1f;

    [Tooltip("Pixels of screen kept clear around the cursor. Pushing into the margin " +
             "turns the view instead, which is what stops the throw being limited to " +
             "whatever was already on screen when the key went down.")]
    [Min(0f)] public float cursorEdgeMargin = 48f;

    [Tooltip("Let go of the bomb key inside this many seconds and the aim stays up " +
             "instead of throwing, so the throw can be placed with no key held and " +
             "ended with a second tap. Hold it longer and releasing throws, as before.\n\n" +
             "This is not a convenience. A laptop touchpad stops reporting motion " +
             "while a key is held -- libinput calls it disable-while-typing and it is " +
             "on by default -- so hold-to-aim plus move-the-pointer is a control that " +
             "cannot be performed at all on most laptops. Tapping leaves no key down.")]
    [Range(0.05f, 1f)] public float tapToLatch = 0.25f;

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
    /// Where the aiming cursor is, in screen pixels. The HUD draws a reticle here
    /// while <see cref="IsAiming"/>. Meaningless when the cursor is not in use.
    /// </summary>
    public Vector2 AimScreenPoint { get; private set; }

    /// <summary>True while the mouse is moving a cursor rather than the view.</summary>
    public bool UsingCursor => cursorAiming && !MobileInput.Active;

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

    /// <summary>When the bomb key last went down, for telling a tap from a hold.</summary>
    float _pressTime = -99f;

    /// <summary>True when a tap is holding the aim open with no key down.</summary>
    bool _latched;
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

        PumpAim(ControlSettings.Held(bindings.bomb) || MobileInput.BombAim,
                ControlSettings.Pressed(bindings.bomb),
                ControlSettings.Pressed(bindings.fire),
                bindings);
    }

    /// <summary>
    /// One frame of the aim, from the state of the bomb key.
    ///
    /// Split out from <see cref="Update"/> and handed its input rather than reading it
    /// so that the tap-versus-hold rule below can be tested. Batch mode cannot
    /// synthesise a legacy key press, and a rule about *when* a key was released is
    /// exactly the kind that is written once and never exercised again.
    /// </summary>
    void PumpAim(bool held, bool pressed, bool fire, ControlSettings bindings)
    {
        // A press while the aim is latched open is the throw. Checked before anything
        // else, or the same press would be read as the start of a fresh aim.
        //
        // The trigger throws it too, and that is not a convenience -- it is the way out
        // of a state the player can otherwise be stuck in. A latched aim suppresses
        // firing and reloading (see Weapon), so an accidental tap leaves somebody
        // holding a gun that does nothing, in a fight, with no obvious way back. Taking
        // the trigger as a throw means the instinctive response to "my gun stopped
        // working" -- pulling it -- is also the thing that fixes it.
        if ((pressed || fire) && _latched && IsAiming)
        {
            _latched = false;
            Release();
            return;
        }

        if (pressed) _pressTime = Time.time;

        if (!held && !_latched)
        {
            if (IsAiming)
            {
                // Let go quickly and the aim stays up with no key down; hold and let
                // go and that is the throw. Both, because the hold is the better
                // control when it can be performed and cannot be performed at all on
                // a laptop touchpad -- see tapToLatch.
                bool tapped = !MobileInput.Active && Time.time - _pressTime <= tapToLatch;

                if (tapped) _latched = true;
                else Release();
            }
            else
            {
                _deniedAnnounced = false;
            }

            if (!_latched) return;
        }

        if (!IsAiming) BeginAiming();

        // BeginAiming refuses on an empty belt, so this is what stops a ring being drawn
        // for a bomb that is not going to be thrown.
        if (!IsAiming)
        {
            _latched = false;
            return;
        }

        // The aim first, then the lock. Locking pins whatever AimRange currently holds,
        // and on the first frame of a hold that is either zero or the distance left over
        // from the previous throw -- so a player who presses both keys together locked a
        // range that had nothing to do with where they were looking. Costs one frame of
        // the ring still being drawn in its unlocked colour, which is invisible.
        UpdateAim();
        UpdateLockToggle(bindings);
    }

    /// <summary>True when a tap is holding the ring up with no key down.</summary>
    public bool AimLatched => _latched;

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

        // Starts under the crosshair, so the first thing the player sees is the cursor
        // exactly where they were already pointing, and it moves from there.
        AimScreenPoint = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        if (UsingCursor && _motor != null) _motor.LookCaptured = true;

        // The pin. Aiming used to be the one control in the game that made no sound,
        // and the thing it draws is a ring on the floor -- which somebody holding the
        // key while looking at an enemy never sees. Silence there reads as a dead key.
        if (data.armClip != null && audioSource != null)
            audioSource.PlayOneShot(data.armClip, 0.85f);

        if (_motor != null) _motor.LookSensitivityMultiplier = aimSensitivity;
    }

    void StopAiming()
    {
        _deniedAnnounced = false;

        if (!IsAiming)
        {
            _lockWasDown = false;
            _latched = false;
            return;
        }

        IsAiming = false;
        RangeLocked = false;
        _lockWasDown = false;
        _latched = false;

        if (indicator != null) indicator.Hide();

        // Handed back rather than assumed: Weapon writes this too, and whichever of the
        // two last let go owns putting it back to 1.
        if (_motor != null)
        {
            _motor.LookSensitivityMultiplier = 1f;

            // Unconditionally, not only when the cursor was in use. Leaving this set
            // is a player who can never turn again -- the look would still be measured
            // and published and simply never applied, which is silent, permanent, and
            // exactly the failure OnDisable already exists to prevent.
            _motor.LookCaptured = false;
        }
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
        if (UsingCursor) MoveCursor();

        Vector3 origin = ThrowOrigin();
        Vector3 landing = ResolveLanding(out Vector3 normal, out bool valid);

        AimValid = valid;

        // Measured from the player, which is the frame everything else about the range
        // uses -- the clamp, the lock and the readout on the HUD. Measuring it from the
        // throw origin instead, which is the muzzle and is the better part of a metre
        // further forward, meant the number under the crosshair disagreed with the
        // limits it was being clamped against: a ring pinned at the minimum read 5.1m
        // on a bomb whose minimum is 6.
        AimRange = Vector3.Distance(FlatOf(transform.position), FlatOf(landing));

        if (indicator == null) return;

        float flight = data.FlightTimeFor(AimRange);

        Vector3 velocity = BombProjectile.SolveVelocity(origin, landing, flight,
                                                        data.gravityScale);

        indicator.Show(origin, landing, velocity, flight, data.gravityScale,
                       Blast.radius, normal, valid, RangeLocked);
    }

    /// <summary>
    /// Where the bomb is going to land, and whether it may be thrown there.
    ///
    /// Two modes. Unlocked, how far down the player is looking sets the distance and
    /// which way they are facing sets the bearing. Locked, the distance is pinned and
    /// only the bearing follows the camera, so the ring sweeps around you.
    ///
    /// Either way the point is dropped onto the floor with a downward ray, which is what
    /// keeps the ring on the ground when the aim is over a gap or off the end of a
    /// walkway.
    ///
    /// Nothing here shortens the throw for a wall in between, and that is deliberate
    /// twice over. The bomb's gravity is several times the world's precisely so that a
    /// throw lobs over cover rather than flying into it, so most walls between here and
    /// there are not in the way at all -- and the dotted arc is sampled from the same
    /// parabola the bomb flies, so a wall that genuinely is in the way is one the player
    /// can already see the line disappear into. A wall test on the straight eye ray got
    /// both wrong: it cut a thirty-four metre throw to seventeen because of a chest-high
    /// crate the bomb would have sailed over, and it did it on the single degree of
    /// pitch where the ray stopped clearing the crate's top edge -- putting back, in a
    /// new place, exactly the cliff this rewrite removed.
    /// </summary>
    Vector3 ResolveLanding(out Vector3 normal, out bool valid)
    {
        normal = Vector3.up;
        valid = true;

        Transform cam = fpsCamera.transform;
        Vector3 player = FlatOf(transform.position);

        // With the cursor, the aim is the ray through the reticle rather than the
        // camera's forward -- that is the whole difference, and it is what lets the
        // player place a bomb off to one side without turning to face it.
        Vector3 aim = UsingCursor
            ? fpsCamera.ScreenPointToRay(AimScreenPoint).direction
            : cam.forward;

        Vector3 bearing = Vector3.ProjectOnPlane(aim, Vector3.up);
        if (bearing.sqrMagnitude < 0.0001f) bearing = transform.forward;
        bearing.Normalize();

        float range;

        if (RangeLocked) range = _lockedRange;
        else if (UsingCursor) range = CursorRange(player);
        else range = RangeFromPitch(cam);

        // Out of range is not an invalid throw, it is a clamped one. A ring that
        // vanished when you looked too far would be a control that stops working
        // exactly when the fight gets interesting.
        range = Mathf.Clamp(range, data.minRange, data.maxRange);

        Vector3 flat = player + bearing * range;

        // Dropped onto whatever floor is under that spot. Started well above the
        // player's head so a target up a ramp is found rather than missed from below.
        Vector3 from = flat + Vector3.up * 40f;

        if (Physics.Raycast(from, Vector3.down, out RaycastHit ground, groundRayLength,
                            groundMask, QueryTriggerInteraction.Ignore))
        {
            normal = ground.normal;
            return ground.point;
        }

        // Nothing under the aim: a hole, or off the edge of the level. The bomb would
        // fall forever, so the throw is refused and the ring says so in red.
        valid = false;
        return new Vector3(flat.x, transform.position.y, flat.z);
    }

    /// <summary>
    /// Moves the aiming cursor with the mouse, and turns the view when it runs out of
    /// screen.
    ///
    /// The motion comes from <see cref="PlayerMotor.LookDeltaDegrees"/> rather than
    /// from the mouse directly, which is deliberate on three counts: this component
    /// never learns which input backend the look is coming from, the one sensitivity
    /// setting governs both the head and the cursor, and a player on a controller or
    /// the keyboard look keys gets a working cursor for free. The motor is told to
    /// stop applying it to the view for the duration -- see PlayerMotor.LookCaptured.
    ///
    /// Degrees are turned into pixels by the camera's own projection, so a cursor
    /// moved to a point on screen is looking at whatever the crosshair would have been
    /// looking at had the view turned instead. That is what makes the cursor feel like
    /// the aim rather than like a mouse pointer laid over the game.
    ///
    /// Running into the edge turns the head by the leftover. Without that, the throw
    /// could only ever be placed inside whatever happened to be on screen when the key
    /// went down, and a target behind you would mean letting go, turning, and starting
    /// the aim again.
    /// </summary>
    void MoveCursor()
    {
        float pixelsPerDegree = PixelsPerDegree();

        Vector2 moved = _motor != null ? _motor.LookDeltaDegrees : Vector2.zero;
        Vector2 wanted = AimScreenPoint + moved * (pixelsPerDegree * cursorSensitivity);

        float margin = Mathf.Min(cursorEdgeMargin, Mathf.Min(Screen.width, Screen.height) * 0.25f);

        Vector2 clamped = new Vector2(
            Mathf.Clamp(wanted.x, margin, Mathf.Max(margin, Screen.width - margin)),
            Mathf.Clamp(wanted.y, margin, Mathf.Max(margin, Screen.height - margin)));

        AimScreenPoint = clamped;

        Vector2 overshoot = wanted - clamped;

        if (overshoot.sqrMagnitude > 0.0001f && _motor != null && pixelsPerDegree > 0.0001f)
            _motor.TurnBy(overshoot / pixelsPerDegree);
    }

    /// <summary>
    /// Screen pixels per degree of view rotation, from the camera's own vertical field
    /// of view. Read every frame rather than cached because the weapon's aim-down-sights
    /// drives the FOV, and a cursor calibrated against the hip FOV would drift as the
    /// zoom changed under it.
    /// </summary>
    float PixelsPerDegree()
        => fpsCamera != null && fpsCamera.fieldOfView > 0.01f
            ? Screen.height / fpsCamera.fieldOfView
            : 10f;

    /// <summary>
    /// Where the cursor is pointing, on the ground.
    ///
    /// The ray is the camera's through the cursor, so this is literally "what is under
    /// the reticle". Out of the bomb's range it is pulled back along its own bearing
    /// rather than refused, the same rule the rest of the aim follows -- the ring stops
    /// short of the cursor and tells the truth about where the bomb will land.
    /// </summary>
    float CursorRange(Vector3 player)
    {
        Ray ray = fpsCamera.ScreenPointToRay(AimScreenPoint);

        // Nothing under the reticle -- it is pointed at the sky, or out over a drop.
        // Maximum range rather than a refusal, the same way looking up is: a control
        // that stops working when it is pointed at nothing is worse than one that
        // throws as far as it can and lets the ring say where that lands.
        return Physics.Raycast(ray, out RaycastHit hit, groundRayLength, groundMask,
                               QueryTriggerInteraction.Ignore)
            ? Vector3.Distance(player, FlatOf(hit.point))
            : data.maxRange;
    }

    /// <summary>
    /// How far out the ring sits, read straight off how far below the horizon the
    /// player is looking: level is maximum range, <see cref="aimPitchAtMinRange"/>
    /// degrees down is minimum, and it slides evenly in between.
    ///
    /// This used to be the distance to wherever the camera ray met the floor, which is
    /// the obvious build and is unusable. The eye is about a metre and a half up, so
    /// that distance is <c>height / tan(pitch)</c> -- a curve that is nearly vertical
    /// near the horizon. On a flat arena the whole six-to-thirty-four metre range lived
    /// inside about twelve degrees of pitch, so a small mouse movement threw the ring
    /// from your feet to the far wall and there was no pitch at all that produced
    /// fifteen metres; and every degree *above* the horizon produced the same maximum,
    /// so half of the mouse's travel did nothing. Aiming a bomb felt like the mouse had
    /// been taken away, which is exactly what it was.
    ///
    /// Looking up is still maximum range. There is no ground up there to aim at, and
    /// the alternative -- refusing the throw -- would be a control that stops working
    /// when the player raises their head.
    /// </summary>
    float RangeFromPitch(Transform cam)
    {
        float down = Mathf.Asin(Mathf.Clamp(-cam.forward.y, -1f, 1f)) * Mathf.Rad2Deg;

        float t = Mathf.InverseLerp(Mathf.Max(1f, aimPitchAtMinRange), 0f, down);

        return Mathf.Lerp(data.minRange, data.maxRange, t);
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

        landing = ClampToRange(landing);

        charges--;
        _nextThrowTime = Time.time + data.throwCooldown;

        // The same number the ring was drawn with, worked out from the same distance.
        float flight = data.FlightTimeFor(
            Vector3.Distance(FlatOf(transform.position), FlatOf(landing)));

        var bomb = Instantiate(data.bombPrefab, origin, UnityEngine.Random.rotation);

        var projectile = bomb.GetComponent<BombProjectile>();
        if (projectile != null)
        {
            projectile.Launch(landing, data, Blast, gameObject, damageMask, collisionMask,
                              gameObject, flight);
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
            audioSource.PlayOneShot(data.throwClip, 1f);

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
    ///
    /// Measured from the player rather than from the throw origin. The origin is the
    /// muzzle, the better part of a metre further forward, so clamping against it moved
    /// a landing point the ring had already placed exactly on the limit: the bomb went
    /// off about a metre past the ring at minimum range and a metre short of it at
    /// maximum, which is a small lie told by the one thing in the feature whose whole
    /// job is not to.
    /// </summary>
    Vector3 ClampToRange(Vector3 landing)
    {
        Vector3 flatPlayer = FlatOf(transform.position);
        Vector3 offset = FlatOf(landing) - flatPlayer;

        float distance = offset.magnitude;
        if (distance <= data.maxRange && distance >= data.minRange) return landing;

        Vector3 bearing = distance > 0.001f ? offset / distance : transform.forward;
        float clamped = Mathf.Clamp(distance, data.minRange, data.maxRange);

        Vector3 flat = flatPlayer + bearing * clamped;
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
        _latched = false;

        // Cleared, not kept. They belong to whichever bomb was equipped before, and a
        // player who swapped to a fresh one would otherwise carry the old one's upgrades
        // onto it. PlayerLoadout writes this bomb's own immediately after.
        damageMultiplier = 1f;
        radiusBonus = 0f;
    }
}
