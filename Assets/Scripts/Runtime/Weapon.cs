using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Hitscan weapon. Lives on the weapon model so it can drive its own ADS
/// position and kickback. All tuning comes from the WeaponData asset.
///
/// Fire, aim and reload keys all come from the shared ControlSettings asset.
/// </summary>
[DisallowMultipleComponent]
public class Weapon : MonoBehaviour
{
    [Header("Wiring")]
    public WeaponData data;
    public Camera fpsCamera;
    public Transform muzzlePoint;
    public Light muzzleLight;
    public AudioSource audioSource;

    [Tooltip("Layers the bullet can hit. Exclude the Player layer.")]
    public LayerMask hitMask = ~0;

    [Header("Feedback")]
    [Tooltip("Pop the damage dealt over whatever you hit. Reading the numbers is how a " +
             "player works out that headshots are worth aiming for.")]
    public bool showDamageNumbers = true;

    public Color damageNumberColor = new Color(1f, 0.92f, 0.75f);
    public Color headshotNumberColor = new Color(1f, 0.82f, 0.2f);
    public Color killNumberColor = new Color(1f, 0.45f, 0.35f);

    // ---- state read by the HUD and sway ----------------------------------
    public WeaponData Data => data;
    public int CurrentAmmo { get; private set; }
    public int ReserveAmmo { get; private set; }
    public bool IsReloading { get; private set; }
    public bool IsAiming { get; private set; }

    /// <summary>0 at the hip, 1 fully aimed. Sway scales itself down by this.</summary>
    public float AimProgress { get; private set; }

    /// <summary>Current cone half-angle in degrees, including movement penalties.</summary>
    public float CurrentSpread { get; private set; }

    // ---- upgrades ---------------------------------------------------------
    //
    // These live on the component and never on the WeaponData asset. That is not a
    // style preference: WeaponData is a ScriptableObject, and a ScriptableObject is a
    // single shared instance. Writing a bought upgrade into it would edit the asset on
    // disk in the editor -- so a maxed rifle would still be maxed after quitting,
    // restarting and clearing the profile -- and in a build it would compound across
    // every restart for the life of the process. Every upgrade is therefore a
    // multiplier applied on the way out, and the asset stays the baseline.
    //
    // Two things set them, and PlayerProgression composes both: the permanent upgrades
    // bought in the store, and the per-level curve. A third, the energy drink, is
    // deliberately kept separate below -- it is temporary, and folding a rush into the
    // same numbers would mean the thing that puts them back has to know what they were.

    /// <summary>Rounds added to the magazine by upgrades.</summary>
    public int MagazineBonus { get; private set; }

    /// <summary>Multiplier on the asset's damage. 1 is the unupgraded gun.</summary>
    public float DamageMultiplier { get; private set; } = 1f;

    /// <summary>Multiplier on the asset's reload time. Below 1 is a faster reload.</summary>
    public float ReloadTimeMultiplier { get; private set; } = 1f;

    /// <summary>
    /// Multiplier on rounds per minute -- the bullets per second an upgrade actually
    /// buys. Above 1 is faster, and it shortens the gap between shots rather than
    /// lengthening it, which is why SecondsBetweenShots divides by it.
    /// </summary>
    public float FireRateMultiplier { get; private set; } = 1f;

    // ---- the rush ---------------------------------------------------------
    //
    // Set by ConsumableBelt while an energy drink is running and put straight back to 1
    // when it ends. Separate fields rather than folded into the upgrades above, because
    // whatever puts them back must not have to remember what the upgrades were.

    /// <summary>Temporary fire rate multiplier from a consumable. 1 when none is running.</summary>
    [System.NonSerialized] public float BoostFireRateMultiplier = 1f;

    /// <summary>Temporary reload multiplier from a consumable. Below 1 is faster.</summary>
    [System.NonSerialized] public float BoostReloadTimeMultiplier = 1f;

    /// <summary>The magazine the gun actually holds right now.</summary>
    public int MagazineSize => data == null ? 0 : Mathf.Max(1, data.magazineSize + MagazineBonus);

    /// <summary>The reload the gun actually takes right now, in seconds.</summary>
    public float ReloadTime => data == null
        ? 0f
        : Mathf.Max(0.15f, data.reloadTime * ReloadTimeMultiplier * Mathf.Max(0.05f, BoostReloadTimeMultiplier));

    /// <summary>
    /// Seconds between shots, with both fire rate multipliers folded in. This is the
    /// single place the gun's cadence is decided -- the data asset's own
    /// SecondsBetweenShots is the baseline and is never read directly by the firing code.
    /// </summary>
    public float SecondsBetweenShots
    {
        get
        {
            if (data == null) return 0.1f;

            float rate = Mathf.Max(0.05f, FireRateMultiplier) * Mathf.Max(0.05f, BoostFireRateMultiplier);

            // Floored, not just clamped by the multipliers: a gun that can be upgraded
            // and then boosted into firing every other frame is a gun that empties its
            // magazine before the sound of the first round has finished.
            return Mathf.Max(0.02f, data.SecondsBetweenShots / rate);
        }
    }

    /// <summary>Rounds per minute as the player would read it. For the store card.</summary>
    public float RoundsPerMinute => SecondsBetweenShots <= 0f ? 0f : 60f / SecondsBetweenShots;

    /// <summary>Damage at a given range, with the upgrades folded in.</summary>
    public float DamageAtDistance(float distance)
        => data == null ? 0f : data.DamageAtDistance(distance) * DamageMultiplier;

    /// <summary>
    /// Applies a set of upgrades. Absolute rather than incremental on purpose -- the
    /// caller owns the curve, and re-applying the same values twice has to be harmless,
    /// because PlayerLoadout and PlayerProgression both call it and neither knows which
    /// of them ran first.
    /// </summary>
    public void ApplyUpgrades(int magazineBonus, float damageMultiplier,
                              float reloadTimeMultiplier, float fireRateMultiplier = 1f,
                              bool topUpMagazine = true)
    {
        MagazineBonus = Mathf.Max(0, magazineBonus);
        DamageMultiplier = Mathf.Max(0.01f, damageMultiplier);
        ReloadTimeMultiplier = Mathf.Clamp(reloadTimeMultiplier, 0.05f, 4f);
        FireRateMultiplier = Mathf.Clamp(fireRateMultiplier, 0.05f, 6f);

        // A bigger magazine that arrives empty is not a reward. Topping up here also
        // covers the case the bonus shrank -- CurrentAmmo has to come back inside it.
        if (topUpMagazine) CurrentAmmo = MagazineSize;
        else CurrentAmmo = Mathf.Min(CurrentAmmo, MagazineSize);

        // The HUD polls the ammo counter against its own last-drawn values, so this is
        // all it takes for a widened magazine to show up there.
        AmmoChanged?.Invoke(this);
    }

    /// <summary>Back to the asset's own numbers. Used when a run restarts.</summary>
    public void ResetUpgrades() => ApplyUpgrades(0, 1f, 1f, 1f);

    /// <summary>
    /// Puts a different gun in the player's hands.
    ///
    /// Called by PlayerLoadout once the store selection is known, which is after Awake
    /// has already initialised the ammo from whatever the builder wired -- so the
    /// magazine and reserve have to be re-read here rather than left at the old gun's.
    /// Upgrades are cleared rather than carried across: they are the *other* gun's, and
    /// PlayerProgression re-applies this one's immediately afterwards.
    /// </summary>
    public void Equip(WeaponData weapon)
    {
        if (weapon == null || weapon == data) return;

        data = weapon;

        MagazineBonus = 0;
        DamageMultiplier = 1f;
        ReloadTimeMultiplier = 1f;
        FireRateMultiplier = 1f;

        ClearRoutineState();

        CurrentAmmo = MagazineSize;
        ReserveAmmo = weapon.reserveAmmo;
        _spreadBonus = 0f;

        AmmoChanged?.Invoke(this);
    }

    public event Action<Weapon> Fired;
    public event Action<Weapon> AmmoChanged;
    public event Action<Weapon, DamageInfo> DealtDamage;   // fires the hitmarker
    public event Action<Weapon> ReloadStarted;
    public event Action<Weapon> ReloadFinished;

    PlayerMotor _motor;
    BombThrower _bombs;
    ControlSettings _fallbackControls;
    Vector3 _hipPosition;
    float _baseFieldOfView;
    float _nextFireTime;
    float _spreadBonus;
    float _kickback;
    float _muzzleLightOffTime;
    bool _burstInProgress;
    bool _mobileFireWasDown;

    /// <summary>The one muzzle flash, replayed per shot. See PlayMuzzleFlash.</summary>
    GameObject _muzzleFlash;

    // Held so the flags above can be checked against the routines they describe. See
    // ClearRoutineState, and the domain reload section of CLAUDE.md.
    Coroutine _reloadRoutine;
    Coroutine _burstRoutine;

    // ======================================================================
    void Awake()
    {
        if (fpsCamera == null) fpsCamera = Camera.main;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        _motor = GetComponentInParent<PlayerMotor>();
        _bombs = GetComponentInParent<BombThrower>();
        _hipPosition = transform.localPosition;
        _baseFieldOfView = fpsCamera != null ? fpsCamera.fieldOfView : 75f;

        if (muzzleLight != null) muzzleLight.enabled = false;

        if (data != null)
        {
            CurrentAmmo = MagazineSize;
            ReserveAmmo = data.reserveAmmo;
        }
    }

    void Start() => AmmoChanged?.Invoke(this);

    /// <summary>Forgets that a reload or a burst was ever in progress.</summary>
    void ClearRoutineState()
    {
        IsReloading = false;
        _burstInProgress = false;
        _reloadRoutine = null;
        _burstRoutine = null;
    }

    /// <summary>Bindings come from the player so both stay in sync automatically.</summary>
    ControlSettings Controls
    {
        get
        {
            if (_motor != null && _motor.controls != null) return _motor.controls;
            if (_fallbackControls == null) _fallbackControls = ControlSettings.CreateDefault();
            return _fallbackControls;
        }
    }

    void Update()
    {
        if (data == null || fpsCamera == null) return;

        // A bomb being aimed takes the hands. Without this the aim key would pull the
        // sights up at the same time as it locked the throw range, and the fire button
        // would empty a magazine into the floor the player is placing a ring on.
        bool aimingBomb = _bombs != null && _bombs.IsAiming;

        bool inputAllowed = PlayerMotor.InputEnabled && !aimingBomb;

        var controls = Controls;

        IsAiming = inputAllowed && !IsReloading &&
                   (ControlSettings.Held(controls.aim) || MobileInput.Aim);

        if (inputAllowed) HandleFireInput(controls);

        if (inputAllowed && (ControlSettings.Pressed(controls.reload) || MobileInput.ConsumeReload()))
            TryReload();

        UpdateSpread();
        UpdateAimAndKick();
        UpdateMuzzleLight();

        RecoverLostRoutines();
    }

    /// <summary>
    /// Clears a flag whose coroutine is gone.
    ///
    /// IsReloading and _burstInProgress each describe a running routine, and each gates
    /// the gun: stuck on, the weapon can never fire, aim or reload again for the rest of
    /// the session. Every ordinary way a routine ends clears its own flag, and OnDisable
    /// covers the weapon being switched off -- so the one case left is a routine that
    /// vanished without either happening.
    ///
    /// In the editor that means a script was recompiled during play, which reloads the
    /// domain and kills every coroutine without running OnDisable or re-running Awake.
    /// The flags survive it, because a bool is serializable; the Coroutine handles cannot
    /// be, and come back null. That mismatch is unreachable in normal play, since each
    /// handle is assigned by the same statement that starts its routine -- so no frame
    /// can observe the gap between the two.
    /// </summary>
    void RecoverLostRoutines()
    {
        bool stranded = (IsReloading && _reloadRoutine == null)
                     || (_burstInProgress && _burstRoutine == null);

        if (stranded) ClearRoutineState();
    }

    // ======================================================================
    void HandleFireInput(ControlSettings controls)
    {
        // MobileInput.Fire is a held flag. Feeding it straight into the semi-auto and
        // burst cases made a touch screen fire every one of them like a full-auto, so
        // the press edge is derived here instead.
        bool touchHeld = MobileInput.Fire;
        bool touchPressed = touchHeld && !_mobileFireWasDown;
        _mobileFireWasDown = touchHeld;

        if (Time.time < _nextFireTime || IsReloading || _burstInProgress) return;

        switch (data.fireMode)
        {
            case FireMode.Auto:
                if (ControlSettings.Held(controls.fire) || touchHeld) FireOnce();
                break;

            case FireMode.Single:
                if (ControlSettings.Pressed(controls.fire) || touchPressed) FireOnce();
                break;

            case FireMode.Burst:
                if (ControlSettings.Pressed(controls.fire) || touchPressed)
                    _burstRoutine = StartCoroutine(BurstRoutine());
                break;
        }
    }

    IEnumerator BurstRoutine()
    {
        _burstInProgress = true;

        for (int i = 0; i < Mathf.Max(1, data.burstCount); i++)
        {
            if (CurrentAmmo <= 0 || IsReloading) break;
            FireOnce();
            yield return new WaitForSeconds(data.burstInterval);
        }

        _burstInProgress = false;
        _burstRoutine = null;
        _nextFireTime = Time.time + SecondsBetweenShots;
    }

    // ======================================================================
    void FireOnce()
    {
        if (IsReloading) return;

        if (!data.infiniteAmmo && CurrentAmmo <= 0)
        {
            PlayClip(data.emptyClip, 0.5f);
            _nextFireTime = Time.time + 0.25f;
            if (data.autoReloadWhenEmpty) TryReload();
            return;
        }

        if (!data.infiniteAmmo) CurrentAmmo--;
        _nextFireTime = Time.time + SecondsBetweenShots;

        for (int i = 0; i < Mathf.Max(1, data.pelletsPerShot); i++)
            FireRay();

        // Recoil, spread growth and the visual punch.
        float recoilScale = Mathf.Lerp(1f, data.adsRecoilMultiplier, AimProgress);
        if (_motor != null)
        {
            _motor.AddRecoil(data.recoilVertical * recoilScale,
                             data.recoilHorizontal * recoilScale,
                             data.recoilRecovery);

            _motor.AddShake(data.cameraShake * recoilScale);
        }

        float maxBonus = Mathf.Max(0f, data.maxSpread - data.baseSpread);
        _spreadBonus = Mathf.Min(maxBonus, _spreadBonus + data.spreadPerShot);
        _kickback = data.visualKickback;

        PlayFireEffects();

        AmmoChanged?.Invoke(this);
        Fired?.Invoke(this);

        if (!data.infiniteAmmo && CurrentAmmo == 0 && data.autoReloadWhenEmpty) TryReload();
    }

    void FireRay()
    {
        Transform cam = fpsCamera.transform;
        Vector3 direction = ApplySpread(cam.forward, cam);
        Vector3 origin = cam.position;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, data.maxRange,
                            hitMask, QueryTriggerInteraction.Ignore))
        {
            float amount = DamageAtDistance(hit.distance);
            var info = new DamageInfo(amount, hit.point, hit.normal, direction, gameObject);

            var hitbox = hit.collider.GetComponent<Hitbox>();
            if (hitbox != null)
            {
                var resolved = hitbox.Receive(info);
                if (resolved.amount > 0f)
                {
                    DealtDamage?.Invoke(this, resolved);
                    ShowDamage(resolved, hitbox.owner);
                }
            }
            else
            {
                var health = hit.collider.GetComponentInParent<Health>();
                if (health != null)
                {
                    info.amount = health.ApplyDamage(info);
                    if (info.amount > 0f)
                    {
                        DealtDamage?.Invoke(this, info);
                        ShowDamage(info, health);
                    }
                }
            }

            if (hit.rigidbody != null)
                hit.rigidbody.AddForceAtPosition(direction * data.impactForce, hit.point, ForceMode.Impulse);

            if (data.impacts != null)
                data.impacts.Spawn(hit.collider.tag, hit.point, hit.normal);

            SpawnTracer(hit.point);
        }
        else
        {
            SpawnTracer(origin + direction * data.maxRange);
        }
    }

    Vector3 ApplySpread(Vector3 forward, Transform cam)
    {
        if (CurrentSpread <= 0.001f) return forward;

        Vector2 offset = UnityEngine.Random.insideUnitCircle * CurrentSpread;
        return Quaternion.AngleAxis(offset.x, cam.up) *
               Quaternion.AngleAxis(offset.y, cam.right) * forward;
    }

    // ======================================================================
    public void TryReload()
    {
        if (IsReloading || data.infiniteAmmo) return;
        if (CurrentAmmo >= MagazineSize) return;
        if (ReserveAmmo <= 0 && !data.infiniteReserve) return;

        _reloadRoutine = StartCoroutine(ReloadRoutine());
    }

    IEnumerator ReloadRoutine()
    {
        IsReloading = true;
        IsAiming = false;
        ReloadStarted?.Invoke(this);
        PlayClip(data.reloadClip, 0.8f);

        yield return new WaitForSeconds(ReloadTime);

        int needed = MagazineSize - CurrentAmmo;

        if (data.infiniteReserve)
        {
            CurrentAmmo = MagazineSize;
        }
        else
        {
            int taken = Mathf.Min(needed, ReserveAmmo);
            CurrentAmmo += taken;
            ReserveAmmo -= taken;
        }

        _spreadBonus = 0f;

        IsReloading = false;
        _reloadRoutine = null;
        AmmoChanged?.Invoke(this);
        ReloadFinished?.Invoke(this);
    }

    /// <summary>
    /// Tops up the reserve and returns how much actually fitted. A pickup checks that
    /// return value so it refuses to be consumed when the reserve is already full.
    /// </summary>
    public int AddReserveAmmo(int amount)
    {
        if (amount <= 0) return 0;

        // Infinite reserve means ammo pickups have nothing to give.
        if (data != null && (data.infiniteReserve || data.infiniteAmmo)) return 0;

        int cap = data != null && data.maxReserveAmmo > 0 ? data.maxReserveAmmo : int.MaxValue;
        int before = ReserveAmmo;

        ReserveAmmo = Mathf.Clamp(ReserveAmmo + amount, 0, cap);

        int taken = ReserveAmmo - before;
        if (taken > 0) AmmoChanged?.Invoke(this);

        return taken;
    }

    /// <summary>Floats the damage dealt over the target, brighter for a headshot or a kill.</summary>
    void ShowDamage(DamageInfo info, Health target)
    {
        if (!showDamageNumbers || info.amount <= 0f) return;

        bool killed = target != null && target.IsDead;

        Color color = killed ? killNumberColor
                    : info.isHeadshot ? headshotNumberColor
                    : damageNumberColor;

        DamageNumber.Show(info.point, info.amount, color,
                          killed ? 1.35f : info.isHeadshot ? 1.15f : 1f);
    }

    void OnDisable()
    {
        // Aiming halves look sensitivity. Leaving that behind on a disabled weapon
        // would quietly stick the player with slow mouse look and no way back.
        if (_motor != null) _motor.LookSensitivityMultiplier = 1f;

        // Same idea, one level down: Unity stops a behaviour's coroutines the moment it
        // is disabled, so the flags describing one must not outlive it. A weapon hidden
        // mid-reload otherwise comes back stuck on IsReloading, which gates firing,
        // aiming and reloading alike -- a dead gun, in a player build, with nothing
        // logged to say why.
        ClearRoutineState();
    }

    // ======================================================================
    void UpdateSpread()
    {
        _spreadBonus = Mathf.Max(0f, _spreadBonus - data.spreadRecovery * Time.deltaTime);

        float spread = data.baseSpread + _spreadBonus;
        spread *= Mathf.Lerp(1f, data.adsSpreadMultiplier, AimProgress);

        if (_motor != null)
        {
            spread *= Mathf.Lerp(1f, data.movementSpreadPenalty, _motor.PlanarSpeed01);
            if (!_motor.IsGrounded) spread *= data.airborneSpreadPenalty;
            if (_motor.IsCrouching) spread *= 0.75f;
        }

        CurrentSpread = spread;
    }

    void UpdateAimAndKick()
    {
        float target = IsAiming ? 1f : 0f;
        AimProgress = Mathf.MoveTowards(AimProgress, target, data.adsSpeed * Time.deltaTime);

        // Slow the look down as the sights come up, the way every modern shooter does.
        if (_motor != null)
            _motor.LookSensitivityMultiplier =
                Mathf.Lerp(1f, _motor.adsSensitivityMultiplier, AimProgress);

        _kickback = Mathf.Lerp(_kickback, 0f, Mathf.Clamp01(12f * Time.deltaTime));

        Vector3 basePosition = Vector3.Lerp(_hipPosition, data.adsPosition, AimProgress);
        transform.localPosition = basePosition + Vector3.back * _kickback;

        float sprintFov = (_motor != null && _motor.IsSprinting) ? _baseFieldOfView * 1.05f : _baseFieldOfView;
        float targetFov = Mathf.Lerp(sprintFov, data.adsFieldOfView, AimProgress);

        fpsCamera.fieldOfView = Mathf.Lerp(fpsCamera.fieldOfView, targetFov,
                                           Mathf.Clamp01(data.adsSpeed * Time.deltaTime));
    }

    // ======================================================================
    void PlayFireEffects()
    {
        PlayClip(data.fireClip, 1f);

        if (muzzleLight != null)
        {
            muzzleLight.enabled = true;
            _muzzleLightOffTime = Time.time + data.muzzleFlashDuration;
        }

        PlayMuzzleFlash();
    }

    /// <summary>
    /// Shows the muzzle flash, reusing the one instance rather than building another.
    ///
    /// A flash was instantiated per shot and destroyed a second later. At 600 rounds a
    /// minute that is ten objects created and destroyed every second the trigger is
    /// held, with up to ten of them hanging off the muzzle at once -- for an effect
    /// TransientFlash switches off after a twentieth of a second. Only one was ever
    /// visible anyway: they all sit at the same point, so each new one appeared inside
    /// the last. One instance, switched off and on again, is the same picture.
    ///
    /// Created on demand rather than in Awake because a scene reload destroys what this
    /// points at while the reference itself survives -- and because TransientFlash resets
    /// its age, scale and roll in OnEnable precisely so it can be replayed this way.
    /// </summary>
    void PlayMuzzleFlash()
    {
        if (data.muzzleFlashPrefab == null || muzzlePoint == null) return;

        // Only a prefab that switches itself off can be replayed. Anything else has to
        // be built and destroyed per shot, because nothing would ever hide it again --
        // reusing one would leave it lit from the first round onward. Same fallback
        // SpawnTracer keeps for a prefab that predates TracerProjectile.
        if (data.muzzleFlashPrefab.GetComponent<TransientFlash>() == null)
        {
            Destroy(Instantiate(data.muzzleFlashPrefab, muzzlePoint.position,
                                muzzlePoint.rotation, muzzlePoint), 1f);
            return;
        }

        if (_muzzleFlash == null)
        {
            // Instantiated active, so OnEnable has already run it from the start.
            _muzzleFlash = Instantiate(data.muzzleFlashPrefab, muzzlePoint.position,
                                       muzzlePoint.rotation, muzzlePoint);
            return;
        }

        // Off first: a flash still playing from the previous round has to be cycled,
        // because OnEnable is the only thing that rewinds it.
        _muzzleFlash.SetActive(false);
        _muzzleFlash.SetActive(true);
    }

    void UpdateMuzzleLight()
    {
        if (muzzleLight != null && muzzleLight.enabled && Time.time >= _muzzleLightOffTime)
            muzzleLight.enabled = false;
    }

    void SpawnTracer(Vector3 endPoint)
    {
        if (data.tracerPrefab == null || muzzlePoint == null) return;

        var tracer = Instantiate(data.tracerPrefab, muzzlePoint.position,
                                 Quaternion.LookRotation(endPoint - muzzlePoint.position));

        // The generated prefab flies itself, which keeps the tracer alive even if this
        // weapon is disabled mid-flight -- a coroutine here would not. The fallback is
        // for a hand-made tracerPrefab that predates TracerProjectile, so swapping in
        // your own prefab still works.
        var projectile = tracer.GetComponent<TracerProjectile>();
        if (projectile != null)
        {
            projectile.Launch(endPoint, data.tracerSpeed);
            return;
        }

        // The fallback walks the tracer from a coroutine on this weapon, and a coroutine
        // that dies -- the weapon disabled, the domain reloaded -- leaves the tracer
        // hanging in the air forever. The backstop is the only thing that would ever
        // clean it up, so it is not optional.
        StartCoroutine(MoveTracer(tracer.transform, endPoint));
        Destroy(tracer, 5f);
    }

    IEnumerator MoveTracer(Transform tracer, Vector3 endPoint)
    {
        float speed = Mathf.Max(1f, data.tracerSpeed);

        while (tracer != null && (tracer.position - endPoint).sqrMagnitude > 0.04f)
        {
            tracer.position = Vector3.MoveTowards(tracer.position, endPoint, speed * Time.deltaTime);
            yield return null;
        }

        if (tracer != null) Destroy(tracer.gameObject);
    }

    void PlayClip(AudioClip clip, float volume)
    {
        if (clip == null || audioSource == null) return;

        audioSource.pitch = 1f + UnityEngine.Random.Range(-data.pitchVariance, data.pitchVariance);
        audioSource.PlayOneShot(clip, volume);
    }
}
