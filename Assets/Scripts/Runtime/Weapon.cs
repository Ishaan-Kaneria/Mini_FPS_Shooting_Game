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

    public event Action<Weapon> Fired;
    public event Action<Weapon> AmmoChanged;
    public event Action<Weapon, DamageInfo> DealtDamage;   // fires the hitmarker
    public event Action<Weapon> ReloadStarted;
    public event Action<Weapon> ReloadFinished;

    PlayerMotor _motor;
    ControlSettings _fallbackControls;
    Vector3 _hipPosition;
    float _baseFieldOfView;
    float _nextFireTime;
    float _spreadBonus;
    float _kickback;
    float _muzzleLightOffTime;
    bool _burstInProgress;
    bool _mobileFireWasDown;

    // ======================================================================
    void Awake()
    {
        if (fpsCamera == null) fpsCamera = Camera.main;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        _motor = GetComponentInParent<PlayerMotor>();
        _hipPosition = transform.localPosition;
        _baseFieldOfView = fpsCamera != null ? fpsCamera.fieldOfView : 75f;

        if (muzzleLight != null) muzzleLight.enabled = false;

        if (data != null)
        {
            CurrentAmmo = data.magazineSize;
            ReserveAmmo = data.reserveAmmo;
        }
    }

    void Start() => AmmoChanged?.Invoke(this);

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

        bool inputAllowed = PlayerMotor.InputEnabled;

        var controls = Controls;

        IsAiming = inputAllowed && !IsReloading &&
                   (ControlSettings.Held(controls.aim) || MobileInput.Aim);

        if (inputAllowed) HandleFireInput(controls);

        if (inputAllowed && (ControlSettings.Pressed(controls.reload) || MobileInput.ConsumeReload()))
            TryReload();

        UpdateSpread();
        UpdateAimAndKick();
        UpdateMuzzleLight();
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
                    StartCoroutine(BurstRoutine());
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
        _nextFireTime = Time.time + data.SecondsBetweenShots;
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
        _nextFireTime = Time.time + data.SecondsBetweenShots;

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
            float amount = data.DamageAtDistance(hit.distance);
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
        if (CurrentAmmo >= data.magazineSize) return;
        if (ReserveAmmo <= 0 && !data.infiniteReserve) return;

        StartCoroutine(ReloadRoutine());
    }

    IEnumerator ReloadRoutine()
    {
        IsReloading = true;
        IsAiming = false;
        ReloadStarted?.Invoke(this);
        PlayClip(data.reloadClip, 0.8f);

        yield return new WaitForSeconds(data.reloadTime);

        int needed = data.magazineSize - CurrentAmmo;

        if (data.infiniteReserve)
        {
            CurrentAmmo = data.magazineSize;
        }
        else
        {
            int taken = Mathf.Min(needed, ReserveAmmo);
            CurrentAmmo += taken;
            ReserveAmmo -= taken;
        }

        _spreadBonus = 0f;

        IsReloading = false;
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

        if (data.muzzleFlashPrefab != null && muzzlePoint != null)
        {
            var flash = Instantiate(data.muzzleFlashPrefab, muzzlePoint.position,
                                    muzzlePoint.rotation, muzzlePoint);
            Destroy(flash, 1f);
        }
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
        StartCoroutine(MoveTracer(tracer.transform, endPoint));
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
