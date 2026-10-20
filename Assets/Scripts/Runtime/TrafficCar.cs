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

    internal TrafficDirector director;
    internal int route, index;

    float _speed, _waitUntil, _panicUntil, _stopClearedAt = -1f;
    int _heard, _clearedStop = -1;
    Transform[] _wheels;
    float _wheelRadius = 0.33f;

    public float Speed => _speed;

    void Start()
    {
        _heard = NoiseSystem.Latest;
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
        _heard = NoiseSystem.Listen(transform.position, 1f, _heard, (n, d) =>
        {
            if (n.kind == NoiseKind.Gunfire || n.kind == NoiseKind.Blast) _panicUntil = Time.time + 18f;
        });

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

        float want = limit;
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
        if (Physics.SphereCast(transform.position + Vector3.up * 0.8f + transform.forward * (halfLength - 0.6f), halfWidth * 0.8f, transform.forward,
                               out var hit, look, director.obstacleMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.attachedRigidbody == null || hit.collider.attachedRigidbody.gameObject != gameObject)
                want = Mathf.Min(want, Mathf.Max(0f, (hit.distance - gap) * 0.8f));
        }

        _speed = Mathf.MoveTowards(_speed, want, (want < _speed ? brake : accel) * Time.deltaTime);

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
}
