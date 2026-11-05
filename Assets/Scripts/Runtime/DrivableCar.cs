using UnityEngine;

/// <summary>
/// A car the player can get into and drive (the desert town; the 26 degree terrain cap was kept for this). Walk up to
/// it and press interact (E, D-pad up, or the RELOAD button while it reads DRIVE on a phone); drive with the move
/// controls (forward/back throttle and brake, left/right steer), Jump is the handbrake, interact again gets out
/// beside the driver's door.
///
/// Four WheelColliders on a Rigidbody, front-wheel steering, rear-wheel drive, an anti-roll pull and a downforce so
/// it holds the dunes' slopes. While driving the player's own body is parked and hidden, the motor's input is off
/// (<see cref="PlayerMotor.InputEnabled"/>), and the player's camera rides a chase rig behind the car. Written on the
/// game's own input system, which is also the only way it works on touch; the BxB MVC vehicle pack was tried, could
/// not load in this Unity version, and was removed (2026-10-21).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DrivableCar : MonoBehaviour
{
    [Header("Wheels (written by the builder)")]
    public WheelCollider frontLeft, frontRight, rearLeft, rearRight;
    public Transform[] wheelVisuals = new Transform[4];

    [Header("Drive")]
    [Tooltip("Engine torque at each driven wheel, newton-metres.")]
    public float motorTorque = 620f;
    [Tooltip("Brake torque at each wheel.")]
    public float brakeTorque = 2600f;
    [Tooltip("Steering lock at a crawl, degrees; it narrows with speed.")]
    public float maxSteer = 32f;
    [Tooltip("Top speed in metres per second (about 100 km/h). The engine's pull falls away towards it.")]
    public float topSpeed = 28f;
    [Tooltip("Top speed in reverse, metres per second.")]
    public float reverseSpeed = 6f;
    [Tooltip("Degrees a second the wheels turn towards the lock asked for, and back to centre when let go. A key is all or nothing: straight onto full lock, the car lurched.")]
    public float steerRate = 110f, steerReturn = 190f;
    [Tooltip("Share of the brake felt with the pedals up: the engine holding it back.")]
    [Range(0f, 0.3f)] public float engineBrake = 0.05f;
    [Tooltip("Pull towards level when the car rolls, and a push down with speed, so it does not tip on a dune.")]
    public float antiRoll = 6000f, downforce = 60f;

    [Header("Seat and camera")]
    [Tooltip("Where the player stands after getting out, in the car's space (beside the driver's door).")]
    public Vector3 exitOffset = new Vector3(-1.9f, 0f, 0.3f);
    [Tooltip("The chase camera: behind and above the car, looking at a point ahead of it.")]
    public Vector3 cameraOffset = new Vector3(0f, 2.6f, -6.8f);
    [Tooltip("Height of the point the chase camera turns about, metres over the car.")]
    public float cameraPivot = 1.4f;
    [Tooltip("Seconds the heading of the chase camera takes to follow the car's.")]
    public float cameraTurnTime = 0.25f;
    [Tooltip("Metres from the car within which it can be got into.")]
    public float reach = 3.2f;

    /// <summary>The car the player is in, or null.</summary>
    public static DrivableCar Driving { get; private set; }
    /// <summary>The car in reach that interact would get into, or null.</summary>
    public static DrivableCar Offered { get; private set; }
    /// <summary>Raised when <see cref="Offered"/> or <see cref="Driving"/> changes (the HUD redraws its prompt).</summary>
    public static event System.Action Changed;

    static readonly System.Collections.Generic.List<DrivableCar> _all = new System.Collections.Generic.List<DrivableCar>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Driving = null;
        Offered = null;
        Changed = null;
        _all.Clear();
    }

    Rigidbody _rb;
    Transform _player, _camera, _cameraParent;
    Vector3 _cameraLocalPos;
    Quaternion _cameraLocalRot;
    CharacterController _cc;
    PlayerMotor _motor;
    float _nextLook;
    float _steer, _camYaw, _camYawVel, _camDistance;
    int _cameraMask;
    Camera _viewCamera;
    Camera[] _stacked;
    int[] _stackedMasks;
    WheelCollider[] _wheels;
    WheelCollider[] Wheels => _wheels ??= new[] { frontLeft, frontRight, rearLeft, rearRight };
    PlayerMotor _keysFrom;
    Quaternion[] _wheelRest;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.mass = 1350f;
        _rb.centerOfMass = new Vector3(0f, 0.35f, 0.1f);
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.isKinematic = true;          // parked until someone drives it: it cannot be shoved about or roll away
        _rb.maxAngularVelocity = 6f;
        // Finer wheel physics at a crawl: at the default the car shivers when stopped.
        if (frontLeft != null) frontLeft.ConfigureVehicleSubsteps(5f, 12, 15);
    }

    void OnEnable() => _all.Add(this);
    void OnDisable() { _all.Remove(this); if (Driving == this) GetOut(force: true); }

    void Update()
    {
        // Only the first car runs the offer search, for all of them.
        if (_all.Count > 0 && _all[0] == this && Time.time >= _nextLook)
        {
            _nextLook = Time.time + 0.15f;
            var found = Driving != null ? null : Nearest();
            if (found != Offered) { Offered = found; Changed?.Invoke(); }
        }
        if (_all.Count > 0 && _all[0] == this)
            MobileInput.DriveLabel = Driving != null ? "GET OUT" : Offered != null ? "DRIVE" : null;

        if (Offered != this && Driving != this) return;
        // Held off while driving, whatever else turns it back on (closing the pause menu does): no shooting from the seat.
        if (Driving == this) PlayerMotor.InputEnabled = false;
        if (!InteractPressed()) return;
        if (Driving == this) GetOut();
        else if (Offered == this && PlayerMotor.InputEnabled) GetIn();
    }

    bool InteractPressed()
    {
        // Not behind a menu, and one press does one thing: with a gun to pick up in reach (disarmed), the press is the pickup's.
        if (Time.timeScale == 0f || OverlayPanel.AnyOpenThisFrame) return false;
        if (MobileInput.PickUpReady && Driving == null) return false;
        if (_keysFrom == null) _keysFrom = Object.FindAnyObjectByType<PlayerMotor>();
        var keys = _keysFrom != null ? _keysFrom.controls : null;
        bool press = (keys != null && ControlSettings.Pressed(keys.interact)) | GameInput.PadPressed(GameAction.Interact);
        press |= MobileInput.ConsumeInteract();
        return press;
    }

    static DrivableCar Nearest()
    {
        var p = GameObject.FindWithTag("Player");
        if (p == null) return null;
        DrivableCar best = null; float bestD = float.MaxValue;
        foreach (var c in _all)
        {
            if (c == null) continue;
            float d = Vector3.Distance(c.transform.position, p.transform.position);
            if (d < c.reach && d < bestD) { best = c; bestD = d; }
        }
        return best;
    }

    void GetIn()
    {
        var p = GameObject.FindWithTag("Player");
        if (p == null) return;
        _player = p.transform;
        _cc = p.GetComponent<CharacterController>();
        _motor = p.GetComponent<PlayerMotor>();
        var cam = Camera.main;
        if (cam == null) return;
        _camera = cam.transform;
        _cameraParent = _camera.parent;
        _cameraLocalPos = _camera.localPosition;
        _cameraLocalRot = _camera.localRotation;

        PlayerMotor.InputEnabled = false;
        if (_cc != null) _cc.enabled = false;
        // The player's body and gun are not drawn while the car is: left out of the cameras, touching no renderer (turning them all off and
        // on again also turned on what had been off). The arms camera stays on with nothing to draw: post-processing runs on it.
        _viewCamera = cam;
        _cameraMask = cam.cullingMask;
        cam.cullingMask &= ~LayerMask.GetMask("Player", "FirstPerson");
        _stacked = System.Array.FindAll(cam.GetComponentsInChildren<Camera>(true), c => c != cam);
        _stackedMasks = new int[_stacked.Length];
        for (int i = 0; i < _stacked.Length; i++) { _stackedMasks[i] = _stacked[i].cullingMask; _stacked[i].cullingMask = 0; }
        _camera.SetParent(null, true);
        // What each wheel's mesh is to its collider's pose, so laying it on the pose keeps the way it was modelled: written straight over, a
        // wheel modelled the other way round showed its blank inner face (a white disc for a hub).
        if (_wheelRest == null) _wheelRest = new Quaternion[4];
        for (int i = 0; i < 4; i++)
        {
            _wheelRest[i] = Quaternion.identity;
            if (Wheels[i] == null || wheelVisuals == null || i >= wheelVisuals.Length || wheelVisuals[i] == null) continue;
            Wheels[i].GetWorldPose(out _, out var pose);
            _wheelRest[i] = Quaternion.Inverse(pose) * wheelVisuals[i].rotation;
        }
        _camYaw = transform.eulerAngles.y;
        _camYawVel = 0f;
        _camDistance = cameraOffset.magnitude;

        _rb.isKinematic = false;
        Driving = this;
        if (TryGetComponent<VehicleStrike>(out var strike)) strike.Driver = p;      // who runs a man down is the man driving
        Offered = null;
        Changed?.Invoke();
    }

    /// <summary>The car's height, from its box (the builder makes the box 90 % of it, top to the roof).</summary>
    float CarHeight
    {
        get
        {
            var box = GetComponent<BoxCollider>();
            return box != null ? (box.center.y + box.size.y * 0.5f) / 0.9f : 1.3f;
        }
    }

    /// <summary>Where to shoot at this target if he is driving: the cabin. His own position plus a chest height went over the roof.</summary>
    public static bool SeatOf(Transform target, out Vector3 seat)
    {
        seat = default;
        if (Driving == null || target == null || Driving._player == null || target.root != Driving._player.root) return false;
        seat = Driving.transform.TransformPoint(new Vector3(0f, Driving.CarHeight * 0.72f, 0f));
        return true;
    }

    /// <summary>
    /// A round that struck the car he is driving (Docs/DESERT_VEHICLES.md: the body stops rounds, windows do not). Through the band of
    /// glass it reaches him and this returns true; the body and the roof stop it, and the caller marks the metal.
    /// </summary>
    public static bool TakeRound(RaycastHit hit, DamageInfo info)
    {
        if (Driving == null || Driving._player == null || hit.collider.GetComponentInParent<DrivableCar>() != Driving) return false;
        float y = Driving.transform.InverseTransformPoint(hit.point).y, height = Driving.CarHeight;
        if (y < height * 0.56f || y > height * 0.86f) return false;
        if (!Driving._player.TryGetComponent<Health>(out var health)) return false;
        health.ApplyDamage(info);
        return true;
    }

    /// <summary>Where the player can stand beside the car, in order of preference; false if the car is hemmed in on every side.</summary>
    bool ExitPoint(out Vector3 at)
    {
        at = transform.position;
        float radius = _cc != null ? _cc.radius : 0.4f, height = _cc != null ? _cc.height : 1.7f;
        int ground = ~LayerMask.GetMask("Player", "PlayerBounds", "FirstPerson", "Vehicle", "Civilian", "Enemy");
        int solid = ~LayerMask.GetMask("Player", "PlayerBounds", "FirstPerson", "Ignore Raycast", "UI");
        float side = Mathf.Abs(exitOffset.x), along = 3.2f;
        var tries = new[]
        {
            exitOffset, new Vector3(side, 0f, exitOffset.z), new Vector3(0f, 0f, -along), new Vector3(0f, 0f, along),
            new Vector3(-side - 1f, 0f, exitOffset.z), new Vector3(side + 1f, 0f, exitOffset.z),
        };
        foreach (var offset in tries)
        {
            Vector3 p = transform.TransformPoint(offset);
            if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 8f, ground, QueryTriggerInteraction.Ignore)) continue;
            Vector3 feet = hit.point + Vector3.up * 0.05f;
            Vector3 low = feet + Vector3.up * (radius + 0.02f), high = feet + Vector3.up * Mathf.Max(radius + 0.02f, height - radius);
            if (Physics.CheckCapsule(low, high, radius, solid, QueryTriggerInteraction.Ignore)) continue;
            at = feet;
            return true;
        }
        return false;
    }

    void GetOut(bool force = false)
    {
        if (Driving != this) return;
        Vector3 at = transform.TransformPoint(exitOffset);
        // Hemmed in on every side: stay in the car (the press does nothing) rather than land in a wall or on the roof.
        if (!ExitPoint(out var found)) { if (!force) return; }
        else at = found;
        Driving = null;
        if (TryGetComponent<VehicleStrike>(out var strike)) strike.Driver = null;
        SetTorque(0f, brakeTorque * 2f, 0f);
        if (_player != null)
        {
            _player.position = at;
            _player.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            if (_cc != null) _cc.enabled = true;
        }
        if (_viewCamera != null) _viewCamera.cullingMask = _cameraMask;
        if (_stacked != null)
            for (int i = 0; i < _stacked.Length; i++) if (_stacked[i] != null) _stacked[i].cullingMask = _stackedMasks[i];
        if (_camera != null)
        {
            _camera.SetParent(_cameraParent, false);
            _camera.localPosition = _cameraLocalPos;
            _camera.localRotation = _cameraLocalRot;
        }
        PlayerMotor.InputEnabled = true;
        Changed?.Invoke();
    }

    void FixedUpdate()
    {
        if (Driving != this)
        {
            if (!_rb.isKinematic && _rb.linearVelocity.sqrMagnitude < 0.05f) _rb.isKinematic = true;
            return;
        }
        Vector2 move = ReadMove();
        bool handbrake = GameInput.Held(GameAction.Jump);
        float speed = Vector3.Dot(_rb.linearVelocity, transform.forward);

        float throttle = 0f, brake = 0f;
        if (move.y > 0.05f) { if (speed < -0.5f) brake = move.y; else throttle = move.y; }
        else if (move.y < -0.05f) { if (speed > 0.5f) brake = -move.y; else throttle = move.y * 0.6f; }

        // The engine's pull falls away towards the top speed instead of cutting out there; reverse has its own, lower one.
        float limit = speed < -0.5f || throttle < 0f ? reverseSpeed : topSpeed;
        float pull = Mathf.Sqrt(Mathf.Clamp01(1f - Mathf.Abs(speed) / Mathf.Max(0.1f, limit)));
        float idle = 0f;
        if (Mathf.Abs(move.y) <= 0.05f)
            idle = Mathf.Abs(speed) < 1f ? 1f : engineBrake;   // the engine holds it back; under walking pace, it is held still

        float lockAngle = Mathf.Lerp(maxSteer, maxSteer * 0.3f, Mathf.Clamp01(Mathf.Abs(speed) / topSpeed));
        float wanted = move.x * lockAngle;
        _steer = Mathf.MoveTowards(_steer, wanted, (Mathf.Abs(move.x) < 0.05f ? steerReturn : steerRate) * Time.fixedDeltaTime);
        frontLeft.steerAngle = frontRight.steerAngle = _steer;
        SetTorque(throttle * pull * motorTorque, Mathf.Max(brake, idle) * brakeTorque, handbrake ? brakeTorque * 1.5f : 0f);

        // Held still: wheel friction alone lets a stopped car creep (half a metre in two seconds).
        if (idle >= 1f && Mathf.Abs(speed) < 0.3f) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }

        AntiRoll(frontLeft, frontRight);
        AntiRoll(rearLeft, rearRight);
        _rb.AddForce(-transform.up * downforce * _rb.linearVelocity.magnitude);
    }

    void SetTorque(float motor, float brake, float handbrake)
    {
        if (rearLeft == null) return;
        rearLeft.motorTorque = rearRight.motorTorque = motor;
        frontLeft.brakeTorque = frontRight.brakeTorque = brake;
        rearLeft.brakeTorque = rearRight.brakeTorque = Mathf.Max(brake, handbrake);
    }

    void AntiRoll(WheelCollider l, WheelCollider r)
    {
        if (l == null || r == null) return;
        float tl = 1f, tr = 1f;
        bool gl = l.GetGroundHit(out var hl), gr = r.GetGroundHit(out var hr);
        if (gl) tl = (-l.transform.InverseTransformPoint(hl.point).y - l.radius) / l.suspensionDistance;
        if (gr) tr = (-r.transform.InverseTransformPoint(hr.point).y - r.radius) / r.suspensionDistance;
        float f = (tl - tr) * antiRoll;
        if (gl) _rb.AddForceAtPosition(l.transform.up * -f, l.transform.position);
        if (gr) _rb.AddForceAtPosition(r.transform.up * f, r.transform.position);
    }

    static Vector2 ReadMove()
    {
        if (MobileInput.Active) return MobileInput.Move;
        var a = GameInput.ActionFor(GameAction.Move);
        return a != null ? a.ReadValue<Vector2>() : Vector2.zero;
    }

    void LateUpdate()
    {
        // Wheel meshes follow their colliders; a parked car's stay as they are.
        if (!_rb.isKinematic)
        {
            var cols = Wheels;
            for (int i = 0; i < 4; i++)
            {
                if (cols[i] == null || wheelVisuals == null || i >= wheelVisuals.Length || wheelVisuals[i] == null) continue;
                cols[i].GetWorldPose(out var pos, out var rot);
                wheelVisuals[i].SetPositionAndRotation(pos, _wheelRest != null ? rot * _wheelRest[i] : rot);
            }
        }
        if (Driving != this || _camera == null) return;

        // Chase camera: fixed to the car's place, its heading easing after the car's, level whatever the car's roll and pitch.
        _camYaw = Mathf.SmoothDampAngle(_camYaw, transform.eulerAngles.y, ref _camYawVel, cameraTurnTime);
        Quaternion heading = Quaternion.Euler(0f, _camYaw, 0f);
        Vector3 pivot = transform.position + Vector3.up * cameraPivot;
        Vector3 away = heading * cameraOffset + Vector3.up * (-cameraPivot);
        float full = away.magnitude;
        // Never through a wall: in at once when something is in the way, out again slowly.
        int solid = ~LayerMask.GetMask("Vehicle", "Player", "PlayerBounds", "FirstPerson", "Enemy", "Civilian", "Ignore Raycast", "UI");
        float free = Physics.SphereCast(pivot, 0.25f, away / full, out var hit, full, solid, QueryTriggerInteraction.Ignore) ? Mathf.Max(0.5f, hit.distance - 0.2f) : full;
        _camDistance = free < _camDistance ? free : Mathf.MoveTowards(_camDistance, free, full / 0.6f * Time.deltaTime);
        _camera.position = pivot + away / full * _camDistance;
        Vector3 look = pivot + heading * Vector3.forward * 5f;
        _camera.rotation = Quaternion.LookRotation(look - _camera.position);
        if (_player != null) _player.position = transform.position + Vector3.up * 0.3f;
    }
}
