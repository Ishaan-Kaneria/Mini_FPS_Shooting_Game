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
    [Tooltip("Gunfire or a blast within this many metres and the driver stops, gets out and runs.")]
    [Min(0f)] public float fleeFrom = 70f;
    [Tooltip("Seconds between blasts of the horn at someone standing in the way.")]
    [Min(0.3f)] public float hornEvery = 1.5f;

    internal TrafficDirector director;
    internal int route, index;
    /// <summary>Which of the director's prefabs it was made from (its pool).</summary>
    internal int prefabIndex;

    float _speed, _waitUntil, _panicUntil, _stopClearedAt = -1f, _personSince = -1f, _reaction;
    int _heard, _clearedStop = -1;
    VehicleDriver _driver;
    bool _driverDead;
    VehicleStrike _strike;
    System.Action<NoiseSystem.Noise, float> _onNoise;
    Transform[] _wheels;
    float _wheelRadius = 0.33f;
    CarLook _look;
    CarAudio _audio;
    float _steerNow, _pitchNow, _rollNow, _lastSpeed, _lastYaw;
    float _hornAt, _leaveAt = -1f;
    Vector3 _danger;
    AudioSource _horn;

    public float Speed => _speed;

    /// <summary>Every traffic car on the road (what the player can take, DrivableCar.Steal).</summary>
    public static readonly System.Collections.Generic.List<TrafficCar> All = new System.Collections.Generic.List<TrafficCar>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { All.Clear(); _sliding = null; }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    // Where it is on its route, and how far to the side of it the car itself is (pulling out round something stood in the lane).
    Vector3 _routePos;
    bool _routeSet;
    float _offset, _offsetWant, _passLeft, _blockedSince = -1f, _carSince = -1f, _fleeUntil;

    /// <summary>A police car (PoliceDirector): it runs the lights at speed to <see cref="policeStop"/> on its route and stands there.</summary>
    internal bool police;
    internal int policeStop;
    /// <summary>One of the police's cars.</summary>
    public bool IsPolice => police;
    /// <summary>A police car that has reached its stop and stands.</summary>
    public bool Arrived { get; private set; }
    public float WheelRadius => _look != null ? _look.wheelRadius : _wheelRadius;

    void Start()
    {
        if (!_routeSet) { _routePos = transform.position; _routeSet = true; }
        _heard = NoiseSystem.Latest;
        _reaction = Random.Range(reactionTime.x, reactionTime.y);
        SeatDriver();
        if (GetComponent<CarGlass>() == null) gameObject.AddComponent<CarGlass>();
        // People walk round it, never through it: cut out of the navmesh whenever it stands (the prefab's obstacle did not cut).
        if (TryGetComponent<UnityEngine.AI.NavMeshObstacle>(out var obstacle)) { obstacle.carving = true; obstacle.carveOnlyStationary = true; }
        else CarObstacles.Add(gameObject, true);
        if (GetComponent<CarDents>() == null) gameObject.AddComponent<CarDents>();
        if (GetComponent<CarWreck>() == null) gameObject.AddComponent<CarWreck>();
        _strike = GetComponent<VehicleStrike>();
        if (_strike != null) _strike.Struck += OnStruck;
        _look = GetComponent<CarLook>();
        _lastYaw = transform.eulerAngles.y;
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
        if (director == null || route < 0) return;
        if (_crashed) { AfterCrash(); return; }
        if (_driverDead) { Coast(); return; }
        if (_leaveAt >= 0f && Time.time >= _leaveAt && _speed < 1f) LeaveCar();
        var r = director.routes[route];
        if (index >= r.points.Length - 1) { director.Finished(this); return; }

        // Danger: the driver gets away from it, in the car or out of it.
        _onNoise ??= OnNoise;
        _heard = NoiseSystem.Listen(transform.position, 1f, _heard, _onNoise);

        Vector3 target = r.points[index + 1];
        Vector3 to = target - _routePos;
        float limit = r.speeds != null && index < r.speeds.Length ? r.speeds[index] : 9f;
        // In a hurry: a frightened driver and a police car go well over the street's pace, and run its lights and signs.
        bool hurry = police || Time.time < _fleeUntil;
        if (hurry) limit *= police ? 1.8f : 1.6f;

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
        if (police && index >= policeStop) { want = 0f; if (_speed < 0.3f) Arrived = true; }

        // Stop lines: the next one within reach decides whether we may pass.
        int stop = NextStop(r, out float stopDist);
        if (stop >= 0 && stop != _clearedStop && !hurry)
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
        bool person = false, stood = false, playersCar = false;
        float stoodAt = 0f;
        if (Physics.SphereCast(transform.position + Vector3.up * 0.8f + transform.forward * (halfLength - 0.6f), halfWidth * 0.8f, transform.forward,
                               out var hit, look, director.obstacleMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.attachedRigidbody == null || hit.collider.attachedRigidbody.gameObject != gameObject)
            {
                // A person stepping out is braked for only once the driver has reacted; a car ahead, at once.
                int layer = hit.collider.gameObject.layer;
                person = layer == LayerMask.NameToLayer("Civilian") || layer == LayerMask.NameToLayer("Enemy") || layer == LayerMask.NameToLayer("Player");
                if (person && _personSince < 0f) _personSince = Time.time;
                if (layer == LayerMask.NameToLayer("Player") && Time.time - _personSince > 0.25f) Honk();
                // A car stood in the lane for good (its driver gone or dead, a wreck, one the player left there) is driven round, not
                // queued behind for ever; a car only waiting its turn is waited for.
                if (!person)
                {
                    var other = hit.collider.GetComponentInParent<TrafficCar>();
                    var body = hit.collider.attachedRigidbody;
                    playersCar = body != null && DrivableCar.Driving != null && body.gameObject == DrivableCar.Driving.gameObject;
                    // (A police car and a frightened driver do not wait in a queue either: they go round it.)
                    stood = other != null ? other.Abandoned || other.Crashed || other.Arrived || (hurry && other.Speed < 1f)
                          : body == null || body.isKinematic || body.linearVelocity.sqrMagnitude < 0.25f;
                    stoodAt = hit.distance;
                }
                bool pullingOut = _offsetWant != 0f && stood && hit.distance > 1.2f;
                if ((!person || Time.time - _personSince >= _reaction) && !pullingOut)
                    want = Mathf.Min(want, Mathf.Max(0f, (hit.distance - gap) * 0.8f));
            }
        }
        if (!person) _personSince = -1f;
        // The player's car across the road: the horn, as at a man stood there.
        if (playersCar && _speed < 1f) { if (_carSince < 0f) _carSince = Time.time; else if (Time.time - _carSince > 0.8f) Honk(); }
        else _carSince = -1f;
        if (stood && _speed < 0.5f && _offsetWant == 0f)
        {
            if (_blockedSince < 0f) _blockedSince = Time.time;
            else if (Time.time - _blockedSince > (hurry ? 0.4f : 2f)) TryPass(stoodAt);
        }
        else if (!stood) _blockedSince = -1f;
        // Out of the lane: slowly while pulling out, at a careful pace alongside.
        if (_offsetWant != 0f || Mathf.Abs(_offset) > 0.05f)
            want = Mathf.Min(want, Mathf.Abs(_offset - _offsetWant) > 0.4f ? 1.8f : hurry ? 9f : 5f);

        _speed = Mathf.MoveTowards(_speed, want, (want < _speed ? brake : accel) * Time.deltaTime);

        // Move along the route, turning towards the next point.
        float step = _speed * Time.deltaTime;
        if (_offsetWant != 0f) { _passLeft -= step; if (_passLeft <= 0f) _offsetWant = 0f; }
        while (step > 0f && index < r.points.Length - 1)
        {
            target = r.points[index + 1];
            to = target - _routePos;
            float d = to.magnitude;
            if (d <= step) { _routePos = target; step -= d; index++; continue; }
            _routePos += to / d * step;
            step = 0f;
        }
        // To the side of the route only as fast as a car can go sideways: by turning, so by its speed.
        Vector3 along = to; along.y = 0f;
        Vector3 right = along.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, along.normalized) : transform.right;
        float before = _offset;
        _offset = Mathf.MoveTowards(_offset, _offsetWant, _speed * 1.1f * Time.deltaTime);
        float sideways = Time.deltaTime > 0f ? (_offset - before) / Time.deltaTime : 0f;
        transform.position = _routePos + right * _offset;
        if (to.sqrMagnitude > 0.0001f)
        {
            var flat = to; flat.y *= 0.5f;
            flat = flat.normalized * Mathf.Max(_speed, 0.5f) + right * sideways;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat.normalized, Vector3.up), Mathf.Clamp01(Time.deltaTime * 6f));
        }

        SpinWheels();
        Weight();
    }

    /// <summary>
    /// The car's body on its springs, its front wheels turning, its brake lamps and its engine: the route moves a rigid thing, and this is what makes it read as a car. Near the camera only.
    /// </summary>
    void Weight()
    {
        if (_look == null) return;
        var cam = Camera.main;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        float accelNow = (_speed - _lastSpeed) / dt; _lastSpeed = _speed;
        float yaw = transform.eulerAngles.y;
        float yawRate = Mathf.DeltaAngle(_lastYaw, yaw) / dt; _lastYaw = yaw;
        bool near = cam != null && (cam.transform.position - transform.position).sqrMagnitude < 90f * 90f;
        _look.Lamps(accelNow < -1f || _speed < 0.2f, false, false);
        if (near)
        {
            if (_audio == null) { _audio = GetComponent<CarAudio>(); if (_audio == null) _audio = gameObject.AddComponent<CarAudio>(); }
            _audio.Switch(true);
            _audio.Traffic(_speed);
        }
        else if (_audio != null) _audio.Switch(false);
        if (!near) return;
        float lat = yawRate * Mathf.Deg2Rad * _speed;
        _pitchNow = Mathf.Lerp(_pitchNow, Mathf.Clamp(accelNow * 0.45f, -2.2f, 1.6f), 1f - Mathf.Exp(-5f * dt));     // nose down under brakes
        _rollNow = Mathf.Lerp(_rollNow, Mathf.Clamp(lat * 0.5f, -2.5f, 2.5f), 1f - Mathf.Exp(-5f * dt));
        _steerNow = Mathf.Lerp(_steerNow, Mathf.Clamp(yawRate * 0.42f, -32f, 32f), 1f - Mathf.Exp(-8f * dt));
        if (_look.model != null) _look.model.localRotation = Quaternion.Euler(-_pitchNow * -1f, 0f, -_rollNow);
        _look.Roll(_speed, _steerNow, dt);
        // The steering wheel turned with the front wheels (a car's ratio, about twelve to one), and the driver's hands with it.
        float wheelTurn = Mathf.Clamp(_steerNow * 12f, -200f, 200f);
        _look.Dash(wheelTurn, _speed * 3.6f, 850f + _speed * 110f);
        if (_driver != null) _driver.WheelTurn = wheelTurn;
    }

    /// <summary>
    /// Nobody driving: it rolls straight on, slowing, until it stops or meets a wall or a car (a person it runs down, VehicleStrike). No
    /// route: a dead man does not take corners, and it is not removed at a route's end in front of the player.
    /// </summary>
    void Coast()
    {
        _speed = Mathf.MoveTowards(_speed, 0f, 1.6f * Time.deltaTime);
        if (_speed <= 0.01f) { _speed = 0f; return; }
        int environment = LayerMask.GetMask("Environment");
        int solid = (director.obstacleMask | environment) & ~LayerMask.GetMask("Civilian", "Enemy", "Player");
        float step = _speed * Time.deltaTime;
        if (Physics.SphereCast(transform.position + Vector3.up + transform.forward * (halfLength - 0.6f), halfWidth * 0.8f, transform.forward,
                               out var hit, 0.6f + step, solid, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(transform)) { _speed = 0f; return; }
        Vector3 p = transform.position + transform.forward * step;
        if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var ground, 4f, environment, QueryTriggerInteraction.Ignore))
            p.y = Mathf.MoveTowards(p.y, ground.point.y, Time.deltaTime);
        transform.position = p;
        SpinWheels();
    }

    void SpinWheels()
    {
        if (_wheels == null || _look != null) return;
        float spin = _speed * Time.deltaTime / _wheelRadius * Mathf.Rad2Deg;
        foreach (var w in _wheels) w.Rotate(spin, 0f, 0f, Space.Self);
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
    /// <summary>Someone alive at its wheel (taking it from him is a theft; an empty one is only a car).</summary>
    public bool HasLiveDriver => _driver != null && !_driver.Dead;

    /// <summary>Of the drivers who hear shooting close by, the share who stop and run on foot; the rest put their foot down.</summary>
    const float BailShare = 0.25f;

    /// <summary>
    /// Shooting or a blast close by. Every driver in earshot used to stop dead, and every one within <see cref="fleeFrom"/> got out
    /// and ran: one shot left the street full of empty cars with the rest queued behind them for good. Now most drive away from it,
    /// fast and through the lights, as in GTA; a few leave the car and run. Farther off than that, they drive on.
    /// </summary>
    void OnNoise(NoiseSystem.Noise n, float d)
    {
        if (n.kind != NoiseKind.Gunfire && n.kind != NoiseKind.Blast) return;
        if (police || _driver == null || _leaveAt >= 0f || Time.time < _fleeUntil) return;
        if (Vector3.Distance(n.position, transform.position) > fleeFrom) return;
        _danger = n.position;
        _fleeUntil = Time.time + 20f;                        // one decision a scare, not one a shot
        if (Random.value >= BailShare) return;
        _panicUntil = Time.time + 8f;
        _leaveAt = Time.time + Random.Range(0.5f, 2f);       // the time it takes to take in, then the door
    }

    /// <summary>
    /// Round something stood in the lane, <paramref name="ahead"/> metres off: out to the left (the other lane) if that is empty far
    /// enough on, else to the right, and back in once past it. Both sides blocked, it waits and asks again.
    /// </summary>
    void TryPass(float ahead)
    {
        _blockedSince = Time.time;
        float reach = ahead + halfLength * 4f + 12f;
        int mask = director.obstacleMask | LayerMask.GetMask("Environment");
        Vector3 right = transform.right;
        foreach (float side in new[] { -1f, 1f })
        {
            float off = side * (halfWidth * 2f + 0.9f);
            Vector3 from = transform.position + Vector3.up * 1.1f + right * off;
            if (Physics.CheckSphere(from, 0.6f, mask, QueryTriggerInteraction.Ignore)) continue;
            if (Physics.SphereCast(from - transform.forward * halfLength, 0.6f, transform.forward, out _, reach + halfLength, mask, QueryTriggerInteraction.Ignore)) continue;
            _offsetWant = off;
            _passLeft = ahead + halfLength * 4f + 5f;
            _blockedSince = -1f;
            return;
        }
    }

    /// <summary>
    /// The driver out of the car because the player is taking it (DrivableCar.Steal): alive, he is put out of his door and runs; dead,
    /// he is taken off the seat.
    /// </summary>
    internal void Eject(Vector3 danger)
    {
        StopAllCoroutines();
        var d = _driver;
        _driver = null;
        if (d == null) return;
        d.Died -= OnDriverDied; d.Died -= OnLeavingDied;
        var people = director != null ? director.Civilians : null;
        if (!d.Dead && people != null)
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 door = transform.TransformPoint(new Vector3(side * (halfWidth + 0.7f), 0f, seatLocal.z));
                if (!UnityEngine.AI.NavMesh.SamplePosition(door, out var at, 2f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                Vector3 away = Vector3.ProjectOnPlane(at.position - danger, Vector3.up);
                if (away.sqrMagnitude < 0.01f) away = transform.right * side;
                people.Adopt(d.Source, at.position, Quaternion.LookRotation(away.normalized, Vector3.up), danger);
                break;
            }
        Destroy(d.gameObject);
    }

    /// <summary>The car is on fire (CarWreck): whoever is driving stops and gets out now.</summary>
    internal void Bail(Vector3 danger)
    {
        if (_driver == null || _driver.Dead || police) return;
        _danger = danger;
        _panicUntil = Time.time + 30f;
        if (_leaveAt < 0f) _leaveAt = Time.time + 0.4f;
    }

    /// <summary>The box a crashed car slides on (taken off again if the car is driven away).</summary>
    internal BoxCollider SkidBox { get; private set; }

    /// <summary>The horn, at someone standing in front of the car. One blast, then another while he stays there.</summary>
    void Honk()
    {
        if (Time.time < _hornAt) return;
        _hornAt = Time.time + hornEvery * Random.Range(0.85f, 1.2f);
        if (_horn == null)
        {
            _horn = gameObject.AddComponent<AudioSource>();
            _horn.spatialBlend = 1f;
            _horn.minDistance = 8f;
            _horn.maxDistance = 90f;
            _horn.rolloffMode = AudioRolloffMode.Linear;
            _horn.playOnAwake = false;
        }
        var clip = WorldSounds.Horn();
        if (clip != null) _horn.PlayOneShot(clip);
    }

    /// <summary>
    /// Out of the door side and running, as one of the town's people. Left in the road, the car stays where it stopped. Not a swap any more
    /// (his note 2026-11-09: the driver popped out beside a door already open): the door swings open, he turns in the seat towards it,
    /// stands up out of the car onto the road, and only then is he one of the running people.
    /// </summary>
    void LeaveCar()
    {
        _leaveAt = Time.time + 2f;       // nowhere to step out (a wall at the door): another try in a moment
        if (_leaving || _driver == null || _driver.Dead || director == null || director.Civilians == null) return;
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 door = transform.TransformPoint(new Vector3(side * (halfWidth + 0.7f), 0f, seatLocal.z));
            if (!UnityEngine.AI.NavMesh.SamplePosition(door, out var at, 1.6f, UnityEngine.AI.NavMesh.AllAreas)) continue;
            _leaveAt = -1f;
            StartCoroutine(GetOut(side, at.position));
            return;
        }
    }

    bool _leaving;

    System.Collections.IEnumerator GetOut(float side, Vector3 outside)
    {
        _leaving = true;
        var d = _driver;
        d.Died -= OnDriverDied;
        d.Died += OnLeavingDied;
        var body = d.transform;
        Vector3 seat = body.position;
        Quaternion seated = body.rotation;
        // Facing away from the danger once out, and running that way.
        Vector3 away = Vector3.ProjectOnPlane(outside - _danger, Vector3.up);
        if (away.sqrMagnitude < 0.01f) away = transform.right * side;
        Quaternion turned = Quaternion.LookRotation(Quaternion.AngleAxis(side * 75f, transform.up) * transform.forward, Vector3.up);
        Quaternion standing = Quaternion.LookRotation(away.normalized, Vector3.up);
        int doorIndex = side < 0f ? 0 : 1;
        // The door (0.35 s), and over it and after it the turn in the seat towards it (0.8 s in all).
        for (float t = 0f; t < 0.8f; t += Time.deltaTime)
        {
            if (d == null || d.Dead) yield break;
            if (_look != null) _look.Door(doorIndex, Mathf.SmoothStep(0f, 0.9f, Mathf.Clamp01(t / 0.35f)));
            body.rotation = Quaternion.Slerp(seated, turned, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.15f) / 0.65f)));
            yield return null;
        }
        if (_look != null) _look.Door(doorIndex, 0.9f);
        // Out: standing in the open door, facing away, and running. (Moved out in the sitting pose, he floated beside the car: the
        // clip does not stand him up that fast, 2026-11-09.) He stands where his feet would land, just outside the sill.
        d.StartLeaving();
        Vector3 door = transform.TransformPoint(new Vector3(side * (halfWidth + 0.35f), 0f, seatLocal.z));
        if (UnityEngine.AI.NavMesh.SamplePosition(door, out var near, 0.8f, UnityEngine.AI.NavMesh.AllAreas)) outside = near.position;
        yield return null;
        if (d == null || d.Dead) yield break;
        // Now one of the town's people, running.
        var people = director != null ? director.Civilians : null;
        if (people != null) people.Adopt(d.Source, outside, standing, _danger);
        Destroy(d.gameObject);
        if (_driver == d) _driver = null;
        _driverDead = true;
    }

    // Shot on the way out: he falls where he is; the car stays abandoned.
    void OnLeavingDied(VehicleDriver d) => _driverDead = true;

    void OnDriverDied(VehicleDriver d) => _driverDead = true;

    // ------------------------------------------------------------------ crashes
    bool _crashed;
    Rigidbody _rb;

    /// <summary>Hit by a car driven by physics: it is one too from here on (shoved, spun, slid to a stop).</summary>
    public bool Crashed => _crashed;

    /// <summary>
    /// A traffic car rides its route as a kinematic body, which met the player's car as a wall: it did not move and the player's car
    /// stopped dead (his note 2026-11-09, "same as in GTA"). Hit, it becomes a car of 1.3 tonnes on locked, sliding wheels: shoved and
    /// spun by the blow, it slides to a stop where it ends up. Its driver, shaken, gets out and runs, or sits on with his hazards on.
    /// </summary>
    public void Crash()
    {
        if (_crashed) return;
        _crashed = true;
        _rb ??= GetComponent<Rigidbody>();
        if (_rb == null) return;
        // Its body's box starts at the sills (a kerb is for the wheels); with no wheels now, a box down to the road for it to slide on.
        var skid = SkidBox = gameObject.AddComponent<BoxCollider>();
        skid.center = new Vector3(0f, 0.25f, 0f);
        skid.size = new Vector3(halfWidth * 1.9f, 0.5f, halfLength * 1.9f);
        skid.sharedMaterial = SlidingTyres;
        _rb.isKinematic = false;
        _rb.mass = 1300f;
        _rb.centerOfMass = new Vector3(0f, 0.45f, 0f);
        _rb.linearDamping = 0.1f;
        _rb.angularDamping = 0.6f;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.linearVelocity = transform.forward * _speed;
        _speed = 0f;
        _panicUntil = Time.time + 60f;
        if (_driver != null && !_driver.Dead && Random.value < 0.7f) { _danger = transform.position - transform.forward * 4f; _leaveAt = Time.time + Random.Range(1.2f, 2.5f); }
    }

    static PhysicsMaterial _sliding;
    static PhysicsMaterial SlidingTyres => _sliding != null ? _sliding : _sliding = new PhysicsMaterial("SlidingTyres")
        { dynamicFriction = 0.55f, staticFriction = 0.7f, frictionCombine = PhysicsMaterialCombine.Average, bounciness = 0.05f };   // GTA's slide, a little past a real car's (0.75 moved it 0.8 m from a 40 km/h side hit)

    void AfterCrash()
    {
        if (_rb == null) return;
        if (_leaveAt >= 0f && Time.time >= _leaveAt && _rb.linearVelocity.magnitude < 0.8f) LeaveCar();
    }

    void OnCollisionEnter(Collision c)
    {
        if (_crashed || c.rigidbody == null || c.rigidbody.isKinematic || c.relativeVelocity.magnitude < 1.5f) return;
        // Only something with a car's weight: a kicked drum, a dropped rifle or a chair met at 1.5 m/s made a wreck of it for good.
        if (c.rigidbody.mass < CrashMass) return;
        Crash();
    }

    /// <summary>Kilograms under which what hit a traffic car is a thing in the road, not a crash.</summary>
    const float CrashMass = 400f;

    /// <summary>Somebody at the wheel (VehicleDriver): one of the town's people, seated, and shot through the glass like anyone.</summary>
    void SeatDriver()
    {
        if (_driver != null) return;
        var people = director != null ? director.driverPrefabs : null;
        if (people == null || people.Length == 0) return;
        _driver = VehicleDriver.Seat(transform, people[Random.Range(0, people.Length)], seatLocal);
        if (_driver != null) _driver.Died += OnDriverDied;
    }

    /// <summary>Whether it can go back in the pool as it is: its driver alive at the wheel and its glass whole.</summary>
    internal bool Reusable => !_crashed && !police && !_driverDead && _driver != null && !_driver.Dead && (!TryGetComponent<CarGlass>(out var g) || !g.AnyBroken);

    /// <summary>Taken out of the pool onto a route: everything about its last drive forgotten, its driver kept.</summary>
    internal void Reuse(int newRoute, int newIndex)
    {
        route = newRoute; index = newIndex;
        _routePos = transform.position; _routeSet = true;
        _offset = _offsetWant = _passLeft = 0f; _blockedSince = _carSince = -1f; _fleeUntil = 0f;
        _speed = _lastSpeed = 0f;
        _waitUntil = _panicUntil = 0f;
        _stopClearedAt = -1f; _personSince = -1f; _leaveAt = -1f;
        _clearedStop = -1;
        _heard = NoiseSystem.Latest;
        _pitchNow = _rollNow = _steerNow = 0f;
        _lastYaw = transform.eulerAngles.y;
        _reaction = Random.Range(reactionTime.x, reactionTime.y);
        if (_look != null) { _look.Door(0, 0f); _look.Door(1, 0f); if (_look.model != null) _look.model.localRotation = Quaternion.identity; }
        SeatDriver();
    }

    // Hit someone: a hard stop, then the shock of it.
    void OnStruck(VehicleStrike s, float speed) => _panicUntil = Time.time + 25f;

    void OnDestroy()
    {
        if (_driver != null) _driver.Died -= OnDriverDied;
        if (_strike != null) _strike.Struck -= OnStruck;
    }
}
