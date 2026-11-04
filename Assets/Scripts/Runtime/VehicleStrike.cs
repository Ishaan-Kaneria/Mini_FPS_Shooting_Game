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

    void OnEnable()
    {
        _mask = LayerMask.GetMask("Civilian", "Enemy", "Player");
    }

    void FixedUpdate()
    {
        // The vehicle's own speed. Worked out here from the position, a traffic car (moved each frame, read each physics step) read 12 m/s
        // as 20 at 30 fps, and the blow grows with its square.
        if (_traffic == null && _body == null) { _traffic = GetComponent<TrafficCar>(); _body = GetComponent<Rigidbody>(); }
        Speed = _traffic != null ? _traffic.Speed : _body != null && !_body.isKinematic ? Vector3.Dot(_body.linearVelocity, transform.forward) : 0f;
        float speed = Mathf.Abs(Speed);
        if (speed < minSpeed || _mask == 0) return;

        // The bumper's box: across the front (or the back, reversing), knee to chest high.
        float dir = Speed >= 0f ? 1f : -1f;
        Vector3 centre = transform.TransformPoint(new Vector3(0f, 0.8f, dir * (halfLength + 0.35f)));
        Vector3 half = new Vector3(halfWidth * 0.95f, 0.7f, 0.45f);
        _hits ??= new Collider[16];
        int n = Physics.OverlapBoxNonAlloc(centre, half, _hits, transform.rotation, _mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = _hits[i];
            if (c == null || c.transform.IsChildOf(transform)) continue;          // its own driver
            var health = c.GetComponentInParent<Health>();
            if (health == null || health.IsDead) continue;
            if (health.GetComponent<VehicleDriver>() != null) continue;           // a driver in another car: the cars meet, not him
            if (Recent.TryGetValue(health, out float until) && Time.time < until) continue;
            Recent[health] = Time.time + 1.5f;

            float over = speed - minSpeed * 0.6f;
            float damage = damageAt10 * (over * over) / (10f * 10f);
            Vector3 push = (transform.forward * dir + Vector3.up * 0.3f).normalized;
            var info = new DamageInfo(damage, c.ClosestPoint(centre), -push, push, Driver != null ? Driver : gameObject)
            {
                heavy = true,
                fromMelee = true,
            };
            health.ApplyDamage(info);
            var body = c.attachedRigidbody;
            if (body != null && !body.isKinematic) body.AddForce(push * speed * 60f, ForceMode.Impulse);
            NoiseSystem.Emit(c.transform.position, 30f, NoiseKind.Violence, Driver != null ? Driver : gameObject);
            Struck?.Invoke(this, speed);
        }
    }
}
