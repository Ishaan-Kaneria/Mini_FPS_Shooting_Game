using UnityEngine;

/// <summary>
/// A car driving the desert town's streets on a route laid down by the builder (<see cref="TrafficDirector"/>):
/// keeps to its lane, slows for turns, stops at red lights and stop signs, waits at the roundabout for a gap, and
/// brakes for anything in front of it -- another car, a person, the player. When shooting starts close by it stops
/// dead with its hazards on until things are quiet.
///
/// Kinematic: it moves itself along the route and never pushes anything, so it can never shove the player through a
/// wall; what it does instead is not move into anyone (the forward check). Bullets hit it as metal.
/// </summary>
public class TrafficCar : MonoBehaviour
{
    [Tooltip("Acceleration and braking, metres per second squared.")]
    public float accel = 2.8f, brake = 7f;

    [Tooltip("Gap kept to whatever is in front, in metres, plus a second of travel.")]
    public float gap = 3f;

    [Tooltip("Half the car's length and width in metres, measured from its body by the builder: how far ahead it looks and how wide.")]
    public float halfLength = 2.3f, halfWidth = 0.95f;

    [Tooltip("Where the driver sits, in the car's space (written by the builder: the left front seat).")]
    public Vector3 seatLocal = new Vector3(-0.38f, 0.08f, 0.05f);

    [Tooltip("Seconds a driver takes to start braking for a person who steps out in front (a range, per driver). Other cars " +
             "are braked for at once: they are seen coming. Real drivers take 0.7-1.5 s; at speed that is how people get hit.")]
    public Vector2 reactionTime = new Vector2(0.7f, 1.2f);

    internal TrafficDirector director;
    internal int route, index;

    float _speed, _waitUntil, _panicUntil, _stopClearedAt = -1f, _personSince = -1f, _reaction;
    int _heard, _clearedStop = -1;
    VehicleDriver _driver;
    bool _driverDead;
    VehicleStrike _strike;
    System.Action<NoiseSystem.Noise, float> _onNoise;
    Transform[] _wheels;
    float _wheelRadius = 0.33f;

    public float Speed => _speed;

    void Start()
    {
        _heard = NoiseSystem.Latest;
        _reaction = Random.Range(reactionTime.x, reactionTime.y);
        // Somebody at the wheel (VehicleDriver): one of the town's people, seated, and shot through the glass like anyone.
        var people = director != null ? director.driverPrefabs : null;
        if (people != null && people.Length > 0)
        {
            _driver = VehicleDriver.Seat(transform, people[Random.Range(0, people.Length)], seatLocal);
            if (_driver != null) _driver.Died += OnDriverDied;
        }
        _strike = GetComponent<VehicleStrike>();
        if (_strike != null) _strike.Struck += OnStruck;
        var list = new System.Collections.Generic.List<Transform>();
        foreach (var t in GetComponentsInChildren<Transform>())
        {
            string n = t.name.ToLowerInvariant();
            if ((n.Contains("wheel") || n.Contains("tire") || n.Contains("tyre")) && !n.Contains("steer") && t.GetComponent<Renderer>() != null) list.Add(t);
        }
        _wheels = list.ToArray();
        if (_wheels.Length > 0)
        {
            var r = _wheels[0].GetComponent<Renderer>();
            if (r != null) _wheelRadius = Mathf.Max(0.2f, r.bounds.extents.y);
        }
    }

    void Update()
    {
        if (director == null) return;
        var r = director.routes[route];
        if (index >= r.points.Length - 1) { director.Finished(this); return; }

        // Danger: stop with the hazards on.
        _onNoise ??= OnNoise;
        _heard = NoiseSystem.Listen(transform.position, 1f, _heard, _onNoise);

        // Nobody driving: it rolls on, slowing, until it stops (or meets something).
        if (_driverDead)
        {
            _speed = Mathf.MoveTowards(_speed, 0f, 1.6f * Time.deltaTime);
            if (_speed <= 0.01f) return;
        }

        Vector3 target = r.points[index + 1];
        Vector3 to = target - transform.position;
        float limit = r.speeds != null && index < r.speeds.Length ? r.speeds[index] : 9f;

        // Corners ahead: slow so the turn is taken at walking-car pace.
        if (index + 2 < r.points.Length)
        {
            Vector3 a = (r.points[index + 1] - r.points[index]).normalized, b = (r.points[index + 2] - r.points[index + 1]).normalized;
            float turn = Vector3.Angle(a, b);
            if (turn > 20f && to.magnitude < 14f) limit = Mathf.Min(limit, Mathf.Lerp(8f, 4f, turn / 90f));
        }

        float want = _driverDead ? _speed : limit;
        if (Time.time < _panicUntil) want = 0f;
        if (Time.time < _waitUntil) want = 0f;

        // Stop lines: the next one within reach decides whether we may pass.
        int stop = NextStop(r, out float stopDist);
        if (stop >= 0 && stop != _clearedStop)
        {
            bool go = true;
            int rule = r.stopRule[stop];
            if (rule == 2)
            {
                var sig = director.Signal(r.stopJunction[stop]);
                go = sig == null || sig.CanGo(r.stopAlongX[stop] != 0);
                // Already over the line on amber: carry on.
                if (!go && stopDist < 2f && _speed > 4f && sig != null && sig.Showing(r.stopAlongX[stop] != 0) == TownSignals.Light.Amber) go = true;
            }
            else if (rule == 1 || rule == 3)
            {
                // Stop sign: a full stop, then go when the junction is clear. Give way: slow, go when clear.
                if (stopDist < 2.5f)
                {
                    if (rule == 1 && _stopClearedAt < 0f) { _stopClearedAt = Time.time + 1.2f; }
                    bool waited = rule == 3 || Time.time >= _stopClearedAt;
                    go = waited && director.JunctionClear(r.points[r.stopIndex[stop] + 1], this);
                }
                else go = false;
            }
            if (go) { if (stopDist < 3f) { _clearedStop = stop; _stopClearedAt = -1f; } }
            else want = Mathf.Min(want, Mathf.Max(0f, (stopDist - 1.5f) * 0.9f));
        }

        // Whatever is in front.
        float look = halfLength + gap + _speed * 1.2f;
        bool person = false;
        if (Physics.SphereCast(transform.position + Vector3.up * 0.8f + transform.forward * (halfLength - 0.6f), halfWidth * 0.8f, transform.forward,
                               out var hit, look, director.obstacleMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.attachedRigidbody == null || hit.collider.attachedRigidbody.gameObject != gameObject)
            {
                // A person stepping out is braked for only once the driver has reacted; a car ahead, at once.
                int layer = hit.collider.gameObject.layer;
                person = layer == LayerMask.NameToLayer("Civilian") || layer == LayerMask.NameToLayer("Enemy") || layer == LayerMask.NameToLayer("Player");
                if (person && _personSince < 0f) _personSince = Time.time;
                if (!person || Time.time - _personSince >= _reaction)
                    want = Mathf.Min(want, Mathf.Max(0f, (hit.distance - gap) * 0.8f));
            }
        }
        if (!person) _personSince = -1f;

        if (!_driverDead) _speed = Mathf.MoveTowards(_speed, want, (want < _speed ? brake : accel) * Time.deltaTime);

        // Move along the route, turning towards the next point.
        float step = _speed * Time.deltaTime;
        while (step > 0f && index < r.points.Length - 1)
        {
            target = r.points[index + 1];
            to = target - transform.position;
            float d = to.magnitude;
            if (d <= step) { transform.position = target; step -= d; index++; continue; }
            transform.position += to / d * step;
            step = 0f;
        }
        if (to.sqrMagnitude > 0.0001f)
        {
            var flat = to; flat.y *= 0.5f;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat.normalized, Vector3.up), Mathf.Clamp01(Time.deltaTime * 6f));
        }

        if (_wheels != null)
        {
            float spin = _speed * Time.deltaTime / _wheelRadius * Mathf.Rad2Deg;
            foreach (var w in _wheels) w.Rotate(spin, 0f, 0f, Space.Self);
        }
    }

    int NextStop(TrafficDirector.Route r, out float dist)
    {
        dist = float.MaxValue;
        if (r.stopIndex == null) return -1;
        for (int i = 0; i < r.stopIndex.Length; i++)
        {
            int at = r.stopIndex[i];
            if (at < index) continue;
            // Distance along the route to the stop point.
            float d = Vector3.Distance(transform.position, r.points[Mathf.Min(index + 1, r.points.Length - 1)]);
            for (int k = index + 1; k < at && k < r.points.Length - 1; k++) d += Vector3.Distance(r.points[k], r.points[k + 1]);
            if (d > 30f) return -1;
            dist = d;
            return i;
        }
        return -1;
    }

    /// <summary>Stand still for a few seconds (the director uses it to let a car wait its turn).</summary>
    public void Wait(float seconds) => _waitUntil = Mathf.Max(_waitUntil, Time.time + seconds);

    /// <summary>Whether its driver has been killed (it rolls to a stop and stays; the director leaves it).</summary>
    public bool Abandoned => _driverDead;

    void OnNoise(NoiseSystem.Noise n, float d)
    {
        if (n.kind == NoiseKind.Gunfire || n.kind == NoiseKind.Blast) _panicUntil = Time.time + 18f;
    }

    void OnDriverDied(VehicleDriver d) => _driverDead = true;

    // Hit someone: a hard stop, then the shock of it.
    void OnStruck(VehicleStrike s, float speed) => _panicUntil = Time.time + 25f;

    void OnDestroy()
    {
        if (_driver != null) _driver.Died -= OnDriverDied;
        if (_strike != null) _strike.Struck -= OnStruck;
    }
}
