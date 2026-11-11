using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A vehicle hitting a person (Docs/DESERT_VEHICLES.md): anyone standing in front of the bumper (behind it, reversing)
/// when the vehicle is moving takes a blow that grows with the square of the speed, as a collision's energy does. At a
/// walking car's pace it knocks them about; at town speed it kills; on the highway nobody survives it. The blow counts as
/// a heavy melee blow (an enemy killed by it goes down in the ragdoll along it, as a melee kill does).
///
/// Speed is measured from how far the vehicle moved since the last frame, so this works on the traffic and the
/// technical (moved along their routes) and on the drivable cars (physics) alike. Who it is credited to is
/// <see cref="Driver"/>: the player while he drives, else the vehicle itself (so a civilian killed by traffic is not the
/// player's casualty).
/// </summary>
[DisallowMultipleComponent]
public class VehicleStrike : MonoBehaviour
{
    [Tooltip("Half the vehicle's width and length, metres (written by the builder).")]
    public float halfWidth = 0.95f, halfLength = 2.3f;
    [Tooltip("Slowest speed, metres per second, at which a vehicle hurts someone it touches.")]
    [Min(0f)] public float minSpeed = 2.5f;
    [Tooltip("Damage at 10 m/s (36 km/h); it scales with the square of the speed above minSpeed.")]
    [Min(0f)] public float damageAt10 = 220f;
    [Tooltip("Share of that blow from the car's side as it passes, not its bumper.")]
    [Range(0f, 1f)] public float sideShare = 0.35f;

    /// <summary>Whoever is credited with a blow; null: the vehicle itself.</summary>
    public GameObject Driver { get; set; }

    /// <summary>Raised when the vehicle has hit someone, with the speed it hit them at.</summary>
    public event System.Action<VehicleStrike, float> Struck;

    /// <summary>Speed along the vehicle's forward, metres per second, this frame.</summary>
    public float Speed { get; private set; }

    TrafficCar _traffic;
    Rigidbody _body;
    int _mask;
    Collider[] _hits;
    Dictionary<Health, float> _recent;
    Dictionary<Health, float> Recent => _recent ??= new Dictionary<Health, float>();
    List<(Collider body, Collider own, float until)> _passing;
    List<(Collider body, Collider own, float until)> Passing => _passing ??= new List<(Collider, Collider, float)>();

    void OnEnable()
    {
        _mask = LayerMask.GetMask("Civilian", "Enemy", "Player");
    }

    /// <summary>What a blow from a car does to the player, against what it does to anyone else: he is knocked along and hurt, and
    /// town speed (about 124 on a pool of 100, whole) was a death every time he stepped off a kerb.</summary>
    const float PlayerShare = 0.4f;

    void FixedUpdate()
    {
        // The vehicle's own velocity. Traffic on its route is moved each frame, so its speed is the route's (worked out from the
        // position it read 12 m/s as 20 at 30 fps, and the blow grows with its square). Anything on physics, a driven car and a traffic
        // car after a crash alike, is taken from its body: a wreck shoved into a crowd read as standing still on the route's speed, and
        // a car sliding sideways read as slow on its speed along its nose, and both went through people as if they were not there.
        if (_traffic == null && _body == null) { _traffic = GetComponent<TrafficCar>(); _body = GetComponent<Rigidbody>(); }
        Vector3 velocity = _traffic != null && !_traffic.Crashed ? transform.forward * _traffic.Speed
                         : _body != null && !_body.isKinematic ? _body.linearVelocity : Vector3.zero;
        Speed = Vector3.Dot(velocity, transform.forward);
        Vector3 flat = Vector3.ProjectOnPlane(velocity, Vector3.up);
        float speed = flat.magnitude;
        RestoreCollisions();
        if (speed < minSpeed || _mask == 0) return;
        Vector3 travel = flat / speed;
        float along = Vector3.Dot(travel, transform.forward), across = Vector3.Dot(travel, transform.right);

        // The face it leads with, knee to chest high: the bumper, the tail when reversing, or a flank when it slides.
        Vector3 centre, half;
        if (Mathf.Abs(along) >= Mathf.Abs(across))
        {
            centre = transform.TransformPoint(new Vector3(0f, 0.8f, Mathf.Sign(along) * (halfLength + 0.35f)));
            half = new Vector3(halfWidth * 0.95f, 0.7f, 0.45f);
        }
        else
        {
            centre = transform.TransformPoint(new Vector3(Mathf.Sign(across) * (halfWidth + 0.35f), 0.8f, 0f));
            half = new Vector3(0.45f, 0.7f, halfLength * 0.95f);
        }
        Strike(centre, half, speed, travel, flat, 1f);
        // Then the rest of it, as it goes by: someone already struck by the leading face is not struck again.
        Strike(transform.TransformPoint(new Vector3(0f, 0.8f, 0f)), new Vector3(halfWidth + 0.08f, 0.7f, halfLength), speed, travel, flat, sideShare);
    }

    void Strike(Vector3 centre, Vector3 half, float speed, Vector3 travel, Vector3 flatCar, float share)
    {
        _hits ??= new Collider[16];
        int n = Physics.OverlapBoxNonAlloc(centre, half, _hits, transform.rotation, _mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = _hits[i];
            if (c == null || c.transform.IsChildOf(transform)) continue;          // its own driver
            var health = c.GetComponentInParent<Health>();
            if (health == null || health.IsDead) continue;
            if (Driver != null && health.gameObject == Driver) continue;          // the player at the wheel
            if (health.GetComponent<VehicleDriver>() != null) continue;           // a driver in another car: the cars meet, not him
            if (Recent.TryGetValue(health, out float until) && Time.time < until) continue;
            Recent[health] = Time.time + 1.5f;

            float over = speed - minSpeed * 0.6f;
            float damage = share * damageAt10 * (over * over) / (10f * 10f);
            var onFoot = health.GetComponent<PlayerMotor>();
            if (onFoot != null) damage *= PlayerShare;
            Vector3 push = (travel + Vector3.up * 0.3f).normalized;
            var info = new DamageInfo(damage, c.ClosestPoint(centre), -push, push, Driver != null ? Driver : gameObject)
            {
                heavy = true,
                fromMelee = true,
            };
            // The body goes with the car (his choice 2026-11-09: real physics, by speed): taken along at a little over the car's own speed and
            // lifted with it -- slow, knocked off their feet and up again hurt; at town speed thrown; fast, thrown far and dead. The side of a
            // car going by takes them less far.
            Vector3 thrown = flatCar * (share < 1f ? 0.6f : 1.0f);
            float lift = Mathf.Min(speed * 0.3f, 5f);
            bool lethal = damage >= health.Current + health.Shield;
            var ragdoll = health.GetComponent<RagdollController>();
            // A death by a car is the throw itself, never a death clip first.
            if (lethal && ragdoll != null && !ragdoll.IsRagdolled) ragdoll.SetRagdollActive(true);
            health.ApplyDamage(info);
            if (health.IsDead)
            {
                Throw(health.GetComponentsInChildren<Rigidbody>(), thrown, lift);
            }
            else if (health.TryGetComponent<Civilian>(out var civilian)) civilian.KnockDown(thrown, lift, transform.position);
            else if (health.TryGetComponent<EnemyAI>(out var enemy)) enemy.Knock(thrown + Vector3.up * lift, 1.2f, Mathf.Min(4f, speed * 0.4f));
            // The player on foot: carried off his feet along the car's way, so the car does not go on through where he stands.
            else if (onFoot != null) onFoot.Shove(thrown * 1.1f + Vector3.up * lift * 0.6f);
            PassThrough(health);
            NoiseSystem.Emit(c.transform.position, 30f, NoiseKind.Violence, Driver != null ? Driver : gameObject);
            Struck?.Invoke(this, speed);
        }
    }

    /// <summary>
    /// A body taken by a car: the bumper sweeps the legs, so the lower a part the faster it goes (a quarter over the car's speed at the
    /// feet, a little over half at the head), and it tips over the bonnet and turns as it flies, not off stiff and upright as a plank
    /// (the first pictures, 2026-11-09). <paramref name="lift"/> is the upward speed at the hips.
    /// </summary>
    public static void Throw(Rigidbody[] bones, Vector3 flat, float lift)
    {
        float low = float.MaxValue, high = float.MinValue;
        foreach (var b in bones) if (b != null) { low = Mathf.Min(low, b.worldCenterOfMass.y); high = Mathf.Max(high, b.worldCenterOfMass.y); }
        float span = Mathf.Max(0.5f, high - low);
        foreach (var b in bones)
        {
            if (b == null || b.isKinematic) continue;
            float t = Mathf.Clamp01((b.worldCenterOfMass.y - low) / span);
            b.linearVelocity = flat * Mathf.Lerp(1.25f, 0.55f, t) + Vector3.up * lift * Mathf.Lerp(0.7f, 1.1f, t) + Random.insideUnitSphere * 0.3f;
        }
    }

    static float BodyMass(Health h)
    {
        float m = 0f;
        foreach (var b in h.GetComponentsInChildren<Rigidbody>()) if (b != null) m += b.mass;
        return m > 1f ? m : 70f;
    }

    /// <summary>A car driven by physics rolls on through a man it has knocked aside; against his collider it stopped as if at a wall.</summary>
    void PassThrough(Health health)
    {
        if (_body == null || _body.isKinematic) return;
        var own = GetComponents<Collider>();
        foreach (var part in health.GetComponentsInChildren<Collider>(true))
            foreach (var mine in own)
            {
                Physics.IgnoreCollision(part, mine, true);
                Passing.Add((part, mine, Time.time + 1.5f));
            }
    }

    void RestoreCollisions()
    {
        if (_passing == null) return;
        for (int i = _passing.Count - 1; i >= 0; i--)
        {
            var p = _passing[i];
            if (Time.time < p.until) continue;
            if (p.body != null && p.own != null) Physics.IgnoreCollision(p.body, p.own, false);
            _passing.RemoveAt(i);
        }
    }
}
