using UnityEngine;

/// <summary>
/// An enemy technical (the desert town): a pickup with a heavy machine gun in the back. Some way into a level it
/// drives in off the highway along a route the builder laid down, parks where it can see a stretch of the town, and
/// its gunner fires bursts at the player whenever he has a line of sight. Kill the gunner and the gun falls silent;
/// the truck stays where it stopped, as cover.
///
/// Not one of the level's enemies (it neither counts towards the clear nor holds it up -- the clock still ends every
/// level), so it is a hazard to deal with rather than a target the stars depend on. It only joins levels from the
/// third on, and resets with every level start.
/// </summary>
public class TechnicalTruck : MonoBehaviour
{
    [Header("Route (written by the builder)")]
    public Vector3[] route;
    [Tooltip("Driving speed along the route, metres per second.")]
    public float speed = 11f;

    [Header("Timing")]
    [Tooltip("Seconds into a level before it sets off: a range, picked fresh each level.")]
    public Vector2 startDelay = new Vector2(45f, 110f);
    [Tooltip("First level (1-based) it appears in.")]
    public int fromLevel = 3;

    [Header("Gun")]
    public Transform gunYaw;
    public Transform muzzle;
    public Health gunner;
    [Tooltip("The gunner's animator (the civilians' controller): falls on its Die trigger.")]
    public Animator gunnerAnimator;
    [Tooltip("Rounds per second while firing.")]
    public float fireRate = 6f;
    [Tooltip("Damage per round that hits.")]
    public float damage = 7f;
    [Tooltip("Spread in degrees: a heavy gun from a truck bed is not a rifle.")]
    public float spread = 2.6f;
    public float range = 160f;
    [Tooltip("Burst length and the pause between bursts, seconds.")]
    public float burst = 1.6f, pause = 2.4f;
    public AudioSource audioSource;
    public AudioClip shot;
    public LineRenderer tracer;

    int _point;
    float _setOff = -1f, _nextShot, _burstEnds, _pauseEnds, _tracerOff;
    bool _active;
    Vector3 _home;
    Quaternion _homeRot;
    Transform _player;
    LevelManager _levels;
    int _sightMask;

    void Start()
    {
        _home = transform.position;
        _homeRot = transform.rotation;
        _levels = FindAnyObjectByType<LevelManager>();
        if (_levels != null) _levels.LevelStarted += OnLevelStarted;
        if (gunner != null) gunner.Died += OnGunnerDied;
        _sightMask = ~LayerMask.GetMask("Player", "PlayerBounds", "Civilian", "Enemy", "FirstPerson");
        Park();
    }

    void OnDestroy()
    {
        if (_levels != null) _levels.LevelStarted -= OnLevelStarted;
        if (gunner != null) gunner.Died -= OnGunnerDied;
    }

    void OnGunnerDied(Health h)
    {
        if (gunnerAnimator == null) return;
        gunnerAnimator.SetInteger("DieVariant", 0);
        gunnerAnimator.SetTrigger("Die");
    }

    void OnLevelStarted(LevelManager lm)
    {
        Park();
        if (lm.LevelNumber >= fromLevel && route != null && route.Length > 1)
            _setOff = Time.time + Random.Range(startDelay.x, startDelay.y);
    }

    /// <summary>Back off the map, idle, gunner alive.</summary>
    void Park()
    {
        _active = false;
        _setOff = -1f;
        _point = 0;
        transform.SetPositionAndRotation(_home, _homeRot);
        if (gunner != null && gunner.IsDead) gunner.ResetHealth();
        if (gunnerAnimator != null) gunnerAnimator.Rebind();
        if (tracer != null) tracer.enabled = false;
    }

    void Update()
    {
        if (_levels != null && !_levels.IsRunning) return;
        if (!_active)
        {
            if (_setOff > 0f && Time.time >= _setOff) { _active = true; transform.position = route[0]; }
            return;
        }
        Drive();
        Shoot();
        if (tracer != null && tracer.enabled && Time.time > _tracerOff) tracer.enabled = false;
    }

    void Drive()
    {
        if (_point >= route.Length - 1) return;
        var target = route[_point + 1];
        var to = target - transform.position;
        float step = speed * Time.deltaTime;
        if (to.magnitude <= step) { transform.position = target; _point++; return; }
        transform.position += to.normalized * step;
        var flat = to; flat.y *= 0.5f;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat.normalized), Time.deltaTime * 4f);
    }

    void Shoot()
    {
        if (gunner == null || gunner.IsDead || gunYaw == null || muzzle == null) return;
        if (_player == null) { var p = GameObject.FindWithTag("Player"); if (p != null) _player = p.transform; if (_player == null) return; }
        Vector3 aim = _player.position + Vector3.up * 1.3f;
        Vector3 dir = aim - muzzle.position;
        if (dir.magnitude > range) return;
        // Traverse the gun towards him, on its pivot.
        var flat = dir; flat.y = 0f;
        gunYaw.rotation = Quaternion.RotateTowards(gunYaw.rotation, Quaternion.LookRotation(flat.normalized), 90f * Time.deltaTime);
        if (Vector3.Angle(gunYaw.forward, flat) > 8f) return;
        // Line of sight: nothing solid between the muzzle and him.
        if (Physics.Raycast(muzzle.position, dir.normalized, dir.magnitude - 0.5f, _sightMask, QueryTriggerInteraction.Ignore)) return;

        if (Time.time < _pauseEnds) return;
        if (_burstEnds < Time.time) _burstEnds = Time.time + burst;
        if (Time.time > _burstEnds - 0.01f) { _pauseEnds = Time.time + pause; return; }
        if (Time.time < _nextShot) return;
        _nextShot = Time.time + 1f / fireRate;

        var shotDir = Quaternion.Euler(Random.Range(-spread, spread), Random.Range(-spread, spread), 0f) * dir.normalized;
        Vector3 end = muzzle.position + shotDir * range;
        if (Physics.Raycast(muzzle.position, shotDir, out var hit, range, ~LayerMask.GetMask("PlayerBounds", "FirstPerson"), QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
            var h = hit.collider.GetComponentInParent<Health>();
            if (h != null && h != gunner) h.ApplyDamage(new DamageInfo(damage, hit.point, hit.normal, shotDir, gameObject));
        }
        if (audioSource != null && shot != null) audioSource.PlayOneShot(shot, 0.9f);
        NoiseSystem.Emit(muzzle.position, 90f, NoiseKind.Gunfire, gameObject);
        if (tracer != null)
        {
            tracer.enabled = true;
            tracer.SetPosition(0, muzzle.position);
            tracer.SetPosition(1, end);
            _tracerOff = Time.time + 0.04f;
        }
    }
}
