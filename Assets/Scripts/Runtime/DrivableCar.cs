using UnityEngine;

/// <summary>
/// A car the player can get into and drive. Walk up and press interact (E, D-pad up, or RELOAD reading DRIVE on a phone): the door opens, the view goes to the driver's seat and the door shuts. From the
/// seat: move keys drive, Jump the handbrake, the look turns the head, C swaps the seat for a chase view, Q (or just firing) brings the gun up, and E gets out (the car brakes first).
///
/// <b>Physics.</b> WheelColliders on a Rigidbody. WheelCollider tyres on their own are ice or glue, and a key is all or nothing, so the steering lock is whatever the speed can hold (a turn asks for at
/// most <see cref="cornerGrip"/>), the yaw is led towards what the wheels ask for, and sideways slide is bled off, except under the handbrake. An engine with a torque curve and a gearbox, whose revs are the
/// sound, the needle and the shifts.
///
/// <b>The gun.</b> One man cannot aim with W held on a touchpad (a held key kills the pointer: disable-while-typing), and a car with nobody's foot down would stop dead in a fight. So bringing the gun up
/// sets a cruise at the speed it was doing: the car carries on by itself, W and S nudge the speed up and down, A and D steer. The view, the aim and the hands are the same first-person view as on foot.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DrivableCar : MonoBehaviour
{
    public enum Driven { Rear, Front, All }

    [Header("Wheels (written by the builder)")]
    public WheelCollider frontLeft, frontRight, rearLeft, rearRight;
    public Transform[] wheelVisuals = new Transform[4];

    [Header("Engine and gears")]
    [Tooltip("The engine's best torque, newton-metres. With the gears and final drive this is what the car can pull.")]
    public float peakTorque = 215f;
    [Tooltip("Tick-over and the redline, revs a minute.")]
    public float idleRpm = 850f, redline = 6200f;
    [Tooltip("Up a gear at this many revs; down a gear below the other.")]
    public float shiftUp = 5500f, shiftDown = 2100f;
    public float[] gears = { 3.5f, 2.15f, 1.45f, 1.05f, 0.82f };
    public float finalDrive = 3.8f;
    public float reverseGear = 3.3f;
    [Tooltip("Seconds the drive is off through a change.")]
    public float shiftTime = 0.2f;
    public Driven driven = Driven.Rear;
    [Tooltip("Metres a second it will not go past (the engine's pull falls away into it).")]
    public float topSpeed = 40f;
    [Tooltip("Metres a second in reverse.")]
    public float reverseSpeed = 9f;
    public float mass = 1350f;

    [Header("Brakes")]
    [Tooltip("Brake torque at each wheel; the rear takes less, or it would swap ends.")]
    public float brakeTorque = 2600f;
    [Range(0.3f, 1f)] public float rearBrakeShare = 0.65f;
    [Tooltip("Share of the brake felt with the pedals up: the engine holding it back.")]
    [Range(0f, 0.3f)] public float engineBrake = 0.04f;

    [Header("Handling")]
    [Tooltip("Steering lock at a crawl, degrees.")]
    public float maxSteer = 36f;
    [Tooltip("The sideways acceleration a turn may ask for, metres a second squared. The lock narrows with speed to hold this, so full lock at 100 km/h is a firm bend, not a spin.")]
    public float cornerGrip = 8.5f;
    [Tooltip("Seconds from straight to full lock, and back. A key is all or nothing: this is what makes it a turn of the wheel.")]
    public float steerIn = 0.26f, steerBack = 0.17f;
    [Tooltip("How hard the car's turning is led to what the front wheels ask for.")]
    public float yawAssist = 7f;
    [Tooltip("How quickly a sideways slide is taken off, per second. Not under the handbrake.")]
    public float slideAssist = 2.4f;
    [Tooltip("Pull towards level when the car rolls, and a push down with speed.")]
    public float antiRoll = 5200f, downforce = 42f;
    [Tooltip("Wheel turn for one degree at the road: a real car's steering ratio.")]
    public float steerRatio = 14f;

    [Header("Seat and camera")]
    [Tooltip("Where the player stands after getting out, in the car's space (beside the driver's door).")]
    public Vector3 exitOffset = new Vector3(-1.9f, 0f, 0.3f);
    [Tooltip("The chase view (C): behind and above the car, looking at a point ahead of it.")]
    public Vector3 cameraOffset = new Vector3(0f, 2.6f, -6.8f);
    [Tooltip("Height of the point the chase camera turns about, metres over the car.")]
    public float cameraPivot = 1.4f;
    [Tooltip("Seconds the heading of the chase camera takes to follow the car's.")]
    public float cameraTurnTime = 0.25f;
    [Tooltip("Metres from the car within which it can be got into.")]
    public float reach = 3.2f;
    [Tooltip("Degrees below the horizon the seat's view rests: enough to see the top of the wheel and the bonnet.")]
    public float seatTilt = 6f;
    [Tooltip("How far the head can turn in the seat: yaw either way, up, down.")]
    public float lookYaw = 140f, lookUp = 40f, lookDown = 55f;
    [Tooltip("Seconds after the look was last moved that the head comes back to the road, gun down and moving.")]
    public float lookReturnAfter = 1.6f;

    [Header("The gun")]
    [Tooltip("Metres a second added to the cruise for each second W is held with the gun up, and taken off by S.")]
    public float cruiseUp = 6f, cruiseDown = 14f;
    [Tooltip("Seconds for the hands to go from wheel to gun.")]
    public float gunRaise = 0.3f;

    /// <summary>The car the player is in, or null.</summary>
    public static DrivableCar Driving { get; private set; }
    /// <summary>The car in reach that interact would get into, or null.</summary>
    public static DrivableCar Offered { get; private set; }
    /// <summary>Raised when <see cref="Offered"/> or <see cref="Driving"/> changes (the HUD redraws its prompt).</summary>
    public static event System.Action Changed;
    /// <summary>The gun is up in the seat and the trigger may be pulled: Weapon's one exception to input being off.</summary>
    public static bool GunUp => Driving != null && Driving._gunHold <= 0.001f && Driving._phase == Phase.Seated && Time.timeScale > 0f;
    /// <summary>The player is in a car but looking from the seat, not the chase view.</summary>
    public static bool InSeat => Driving != null && !Driving._chase;

    /// <summary>What the arms do in a car (<see cref="ViewModelRig"/> reads it each frame): where the wheel is and how much the hands are on it, 0 on the gun, 1 on the wheel.</summary>
    public struct HandsOnWheel
    {
        public float hold, turn;
        public Vector3 eye;
        public Quaternion car;
        public CarLook look;
    }
    public static HandsOnWheel Hands { get; private set; }

    static readonly System.Collections.Generic.List<DrivableCar> _all = new System.Collections.Generic.List<DrivableCar>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Driving = null;
        Offered = null;
        Changed = null;
        Hands = default;
        _all.Clear();
    }

    enum Phase { Out, GettingIn, Seated, GettingOut }

    Rigidbody _rb;
    CarLook _look;
    CarAudio _audio;
    Phase _phase = Phase.Out;
    float _phaseT;
    Vector3 _fromPos;
    Quaternion _fromRot;
    Vector3 _toPos;
    bool _engineRunning;
    Transform _player, _camera, _cameraParent;
    Vector3 _cameraLocalPos;
    Quaternion _cameraLocalRot;
    CharacterController _cc;
    PlayerMotor _motor;
    Weapon _gun;
    Vector2 _move;
    float _nextLook;
    float _steer, _camYaw, _camYawVel, _camDistance;
    float _speed;
    int _cameraMask;
    Camera _viewCamera;
    Camera[] _stacked;
    int[] _stackedMasks;
    WheelCollider[] _wheels;
    WheelCollider[] Wheels => _wheels ??= new[] { frontLeft, frontRight, rearLeft, rearRight };
    PlayerMotor _keysFrom;
    Quaternion[] _wheelRest;
    bool _chase, _gunWanted, _wantOut;
    float _gunHold = 1f, _cruise, _lookYawNow, _lookPitchNow, _lastLookAt;
    int _gear = 1;   // 0 reverse; 1.. forward gears
    float _rpm, _shiftUntil, _throttleNow, _brakeNow, _scrub;
    Vector3 _headLag, _headLagVel, _lastVelocity;
    float _bump;
    ParticleSystem _smoke, _dust;
    float _shakeUntil;

    /// <summary>The speed shown on the HUD, km/h, and the gear (0 is reverse).</summary>
    public float Kmh => Mathf.Abs(_speed) * 3.6f;
    public int Gear => _gear;
    public bool Cruising => _gunWanted && _cruise > 0.2f;
    public bool ChaseView => _chase;
    public float Rpm => _rpm;
    public float Steer => _steer;
    public float CruiseSpeed => _cruise;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _look = GetComponent<CarLook>();
        _rb.mass = mass;
        _rb.centerOfMass = new Vector3(0f, CarHeight * 0.26f, 0.05f);
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.isKinematic = true;          // parked until someone drives it: it cannot be shoved about or roll away
        _rb.maxAngularVelocity = 6f;
        _rb.linearDamping = 0.02f;
        _rb.angularDamping = 0.25f;
        if (GetComponent<CarGlass>() == null) gameObject.AddComponent<CarGlass>();
        TuneWheels();
        // Finer wheel physics at a crawl: at the default the car shivers when stopped.
        if (frontLeft != null) frontLeft.ConfigureVehicleSubsteps(5f, 12, 15);
    }

    /// <summary>Tyres and springs from the car's weight, so a minibus sits as a minibus does. About 1.5 bounces a second, damped by a third.</summary>
    void TuneWheels()
    {
        foreach (var w in Wheels)
        {
            if (w == null) continue;
            float corner = mass * 0.25f;
            const float frequency = 1.55f;
            var spring = w.suspensionSpring;
            spring.spring = corner * Mathf.Pow(frequency * 2f * Mathf.PI, 2f);
            spring.damper = 2f * 0.36f * Mathf.Sqrt(spring.spring * corner);
            spring.targetPosition = 0.45f;
            w.suspensionSpring = spring;
            w.suspensionDistance = 0.2f;
            w.forceAppPointDistance = 0.14f;
            w.wheelDampingRate = 0.4f;
            var f = w.forwardFriction;
            f.extremumSlip = 0.3f; f.extremumValue = 1.1f; f.asymptoteSlip = 0.9f; f.asymptoteValue = 0.8f; f.stiffness = 1.5f;
            w.forwardFriction = f;
            var s = w.sidewaysFriction;
            s.extremumSlip = 0.22f; s.extremumValue = 1.05f; s.asymptoteSlip = 0.6f; s.asymptoteValue = 0.8f;
            s.stiffness = w == rearLeft || w == rearRight ? 1.9f : 1.7f;       // the rear holds on: understeer is forgiving, a spin is not
            w.sidewaysFriction = s;
        }
    }

    void OnEnable() => _all.Add(this);
    void OnDisable() { _all.Remove(this); if (Driving == this) FinishOut(force: true); }

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
        if (Driving == this)
        {
            // Held off while driving, whatever else turns it back on (closing the pause menu does). The gun's one exception is DrivableCar.GunUp.
            PlayerMotor.InputEnabled = false;
            _move = ReadMove();
            if (_phase == Phase.Seated) SeatKeys();
        }
        if (!InteractPressed()) return;
        if (Driving == this) { if (_phase == Phase.Seated) AskOut(); }
        else if (Offered == this && PlayerMotor.InputEnabled) GetIn();
    }

    void SeatKeys()
    {
        if (Time.timeScale == 0f || OverlayPanel.AnyOpenThisFrame) return;
        var keys = _motor != null ? _motor.controls : null;
        if (keys == null) return;
        // The gun: its own key, or simply trying to use it. Fists and no gun: nothing to bring up.
        bool toggle = ControlSettings.Pressed(keys.carGun) || GameInput.PadPressed(GameAction.Melee) || MobileInput.ConsumeMelee();
        bool use = ControlSettings.Pressed(keys.fire) || ControlSettings.Pressed(keys.aim) || GameInput.PadPressed(GameAction.Fire) || GameInput.PadPressed(GameAction.Aim) || MobileInput.Fire;
        bool armed = _gun != null && !_gun.Disarmed;
        if (toggle) SetGun(!_gunWanted && armed);
        else if (use && !_gunWanted && armed) SetGun(true);
        if (ControlSettings.Pressed(keys.carView) || GameInput.PadPressed(GameAction.Crouch)) { if (!_gunWanted) SetView(!_chase); }
        if (!armed && _gunWanted) SetGun(false);
    }

    void SetGun(bool up)
    {
        if (up == _gunWanted) return;
        _gunWanted = up;
        if (up)
        {
            if (_chase) SetView(false);       // a gun is aimed from the seat
            _cruise = _speed > 1.5f ? _speed : 0f;
        }
        Changed?.Invoke();
    }

    void SetView(bool chase)
    {
        if (chase == _chase) return;
        _chase = chase;
        if (chase)
        {
            _camYaw = transform.eulerAngles.y; _camYawVel = 0f; _camDistance = cameraOffset.magnitude;
        }
        // The arms are in the seat's view only.
        if (_stacked != null)
            for (int i = 0; i < _stacked.Length; i++) if (_stacked[i] != null) _stacked[i].cullingMask = chase ? 0 : _stackedMasks[i];
        Changed?.Invoke();
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

    /// <summary>Whether <paramref name="c"/> belongs to the car the shooter is in: a round fired from inside goes out, it does not stop at its own door.</summary>
    public static bool IsOwn(Collider c) => Driving != null && c != null && c.attachedRigidbody == Driving._rb;

    static readonly RaycastHit[] _castHits = new RaycastHit[24];

    /// <summary>The first thing on a shot's line that is not the car being driven (the gun's and the camera's rays start inside it).</summary>
    public static bool Cast(Vector3 from, Vector3 dir, float range, int mask, out RaycastHit hit)
    {
        hit = default;
        int n = Physics.RaycastNonAlloc(from, dir, _castHits, range, mask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue; bool any = false;
        for (int i = 0; i < n; i++)
        {
            if (IsOwn(_castHits[i].collider) || _castHits[i].distance >= best) continue;
            if (Driving._player != null && _castHits[i].collider.transform.IsChildOf(Driving._player)) continue;
            best = _castHits[i].distance; hit = _castHits[i]; any = true;
        }
        return any;
    }

    void GetIn()
    {
        var p = GameObject.FindWithTag("Player");
        if (p == null) return;
        _player = p.transform;
        _cc = p.GetComponent<CharacterController>();
        _motor = p.GetComponent<PlayerMotor>();
        _gun = p.GetComponentInChildren<Weapon>();
        var cam = Camera.main;
        if (cam == null) return;
        _camera = cam.transform;
        _cameraParent = _camera.parent;
        _cameraLocalPos = _camera.localPosition;
        _cameraLocalRot = _camera.localRotation;

        PlayerMotor.InputEnabled = false;
        if (_cc != null) _cc.enabled = false;
        // The body is not drawn while the car is: left out of the camera, touching no renderer (turning them all off and on again also turned on what had been off).
        // The arms stay: their camera draws the hands on the wheel, and post-processing runs on it.
        _viewCamera = cam;
        _cameraMask = cam.cullingMask;
        cam.cullingMask &= ~LayerMask.GetMask("Player", "FirstPerson");
        _stacked = System.Array.FindAll(cam.GetComponentsInChildren<Camera>(true), c => c != cam);
        _stackedMasks = new int[_stacked.Length];
        for (int i = 0; i < _stacked.Length; i++) _stackedMasks[i] = _stacked[i].cullingMask;
        _fromPos = _camera.position; _fromRot = _camera.rotation;
        _camera.SetParent(null, true);
        // What each wheel's mesh is to its collider's pose, so laying it on the pose keeps the way it was modelled.
        if (_wheelRest == null) _wheelRest = new Quaternion[4];
        for (int i = 0; i < 4; i++)
        {
            _wheelRest[i] = Quaternion.identity;
            if (Wheels[i] == null || wheelVisuals == null || i >= wheelVisuals.Length || wheelVisuals[i] == null) continue;
            Wheels[i].GetWorldPose(out _, out var pose);
            _wheelRest[i] = Quaternion.Inverse(pose) * wheelVisuals[i].rotation;
        }
        _chase = false; _gunWanted = false; _gunHold = 1f; _wantOut = false;
        _cruise = 0f; _gear = 1; _rpm = idleRpm; _lookYawNow = 0f; _lookPitchNow = 0f;
        _lastVelocity = Vector3.zero; _headLag = _headLagVel = Vector3.zero;
        Driving = this;
        _phase = Phase.GettingIn; _phaseT = 0f;
        if (TryGetComponent<VehicleStrike>(out var strike)) strike.Driver = p;      // who runs a man down is the man driving
        Offered = null;
        if (_audio == null) { _audio = GetComponent<CarAudio>(); if (_audio == null) _audio = gameObject.AddComponent<CarAudio>(); }
        _audio.Once("CarDoorOpen", 0.8f, true);
        _rb.isKinematic = false;
        if (_look != null) _look.Lamps(false, false, false);
        Changed?.Invoke();
    }

    /// <summary>The car's height, from its box (the builder makes the box 90 % of it, top to the roof).</summary>
    float CarHeight
    {
        get
        {
            float top = 0f;
            foreach (var box in GetComponents<BoxCollider>()) if (!box.isTrigger) top = Mathf.Max(top, box.center.y + box.size.y * 0.5f);
            return top > 0f ? top : 1.4f;
        }
    }

    /// <summary>Where to shoot at this target if he is driving: the cabin. His own position plus a chest height went over the roof.</summary>
    public static bool SeatOf(Transform target, out Vector3 seat)
    {
        seat = default;
        if (Driving == null || target == null || Driving._player == null || target.root != Driving._player.root) return false;
        seat = Driving.transform.TransformPoint(Driving._look != null ? Driving._look.seatEye + Vector3.down * 0.25f : new Vector3(0f, Driving.CarHeight * 0.72f, 0f));
        return true;
    }

    /// <summary>
    /// A round that struck the car he is driving (the body stops rounds, windows do not). The collider stops at the waist and starts again at the roof: through the gap it reaches him;
    /// on the body and the roof it is stopped, and the caller marks the metal.
    /// </summary>
    public static bool TakeRound(RaycastHit hit, DamageInfo info)
    {
        if (Driving == null || Driving._player == null || !IsOwn(hit.collider)) return false;
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

    /// <summary>Out: at a walk the door opens now; moving, the car brakes to a walk first.</summary>
    void AskOut()
    {
        if (!ExitPoint(out _)) return;       // hemmed in on every side: stay in the car rather than land in a wall or on the roof
        _wantOut = true;
        SetGun(false);
    }

    void BeginOut()
    {
        if (!ExitPoint(out var at)) { _wantOut = false; return; }
        _wantOut = false;
        _toPos = at;
        _phase = Phase.GettingOut; _phaseT = 0f;
        _fromPos = _camera != null ? _camera.position : transform.position;
        _fromRot = _camera != null ? _camera.rotation : transform.rotation;
        if (_audio != null) { _audio.Once("CarDoorOpen", 0.8f, true); }
    }

    void FinishOut(bool force = false)
    {
        if (Driving != this) return;
        Vector3 at = _phase == Phase.GettingOut ? _toPos : transform.TransformPoint(exitOffset);
        if (force && ExitPoint(out var found)) at = found;
        Driving = null;
        Hands = default;
        _phase = Phase.Out;
        if (TryGetComponent<VehicleStrike>(out var strike)) strike.Driver = null;
        SetTorque(0f, brakeTorque * 2f, 0f);
        if (_player != null)
        {
            _player.position = at;
            _player.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            if (_motor != null) _motor.FaceAfterSeat(transform.eulerAngles.y);
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
        if (_look != null) { _look.Door(0, 0f); _look.Lamps(false, false, false); }
        if (_audio != null && Application.isPlaying) { _audio.Switch(false); _audio.Once("CarDoorClose", 0.9f, false); }
        _engineRunning = false;
        Changed?.Invoke();
    }

    void FixedUpdate()
    {
        if (Driving != this)
        {
            if (!_rb.isKinematic && _rb.linearVelocity.sqrMagnitude < 0.05f && _phase == Phase.Out) _rb.isKinematic = true;
            return;
        }
        float dt = Time.fixedDeltaTime;
        Vector2 move = _phase == Phase.Seated && !_wantOut ? _move : Vector2.zero;      // read in Update, where the player's own movement reads it
        bool handbrake = (_phase == Phase.Seated && GameInput.Held(GameAction.Jump)) || _phase != Phase.Seated;
        _speed = Vector3.Dot(_rb.linearVelocity, transform.forward);
        float planar = Mathf.Abs(_speed);

        // ---- pedals -----------------------------------------------------------
        float throttle = 0f, brake = 0f;
        if (_wantOut)
        {
            brake = 1f;
            if (planar < 3f) BeginOut();
        }
        else if (_gunWanted)
        {
            // Cruise: the car carries on by itself. W and S nudge it; let go, and it holds what it has.
            if (move.y > 0.05f) _cruise = Mathf.Min(topSpeed * 0.8f, _cruise + cruiseUp * move.y * dt);
            else if (move.y < -0.05f) _cruise = Mathf.Max(0f, _cruise - cruiseDown * -move.y * dt);
            float error = _cruise - _speed;
            throttle = Mathf.Clamp01(error * 0.7f);
            brake = error < -1.2f || _cruise < 0.2f ? Mathf.Clamp01(-error * 0.25f + (_cruise < 0.2f ? 0.5f : 0f)) : 0f;
            if (_speed < -0.5f) { brake = 1f; throttle = 0f; }
        }
        else if (move.y > 0.05f) { if (_speed < -0.5f) brake = move.y; else throttle = move.y; }
        else if (move.y < -0.05f) { if (_speed > 0.8f) brake = -move.y; else throttle = move.y; }
        // A key is all or nothing: the foot takes a moment.
        _throttleNow = Mathf.MoveTowards(_throttleNow, throttle, dt * (Mathf.Abs(throttle) > Mathf.Abs(_throttleNow) ? 3.2f : 7f));
        _brakeNow = Mathf.MoveTowards(_brakeNow, brake, dt * 5f);

        // ---- engine and gears --------------------------------------------------
        bool reversing = _throttleNow < 0f || _speed < -0.5f;
        int wantGear = _gear;
        if (reversing) wantGear = 0;
        else if (_gear == 0) wantGear = 1;
        float wheelRpm = Mathf.Abs(_speed) / Mathf.Max(0.1f, frontLeft != null ? frontLeft.radius : 0.33f) * 60f / (2f * Mathf.PI);
        float ratio = _gear == 0 ? reverseGear : gears[Mathf.Clamp(_gear - 1, 0, gears.Length - 1)];
        float rpmFromWheels = wheelRpm * ratio * finalDrive;
        // The clutch slips away from a standstill, so the revs are the foot's until the wheels catch up.
        float slipRpm = idleRpm + Mathf.Abs(_throttleNow) * 2600f;
        float want = Mathf.Max(rpmFromWheels, _gear <= 1 ? slipRpm * Mathf.Clamp01(1f - rpmFromWheels / 3200f) + idleRpm * (rpmFromWheels / 3200f) : idleRpm);
        _rpm = Mathf.Clamp(Mathf.MoveTowards(_rpm, want, dt * 9000f), idleRpm, redline);
        if (!reversing && Time.time >= _shiftUntil && _gear >= 1)
        {
            if (rpmFromWheels > shiftUp && _gear < gears.Length) wantGear = _gear + 1;
            else if (rpmFromWheels < shiftDown && _gear > 1 && Mathf.Abs(_throttleNow) < 0.9f || (_gear > 1 && rpmFromWheels < shiftDown * 0.7f)) wantGear = _gear - 1;
        }
        if (wantGear != _gear)
        {
            _gear = wantGear;
            _shiftUntil = Time.time + shiftTime;
            if (_audio != null && _gear != 0 && _gear != 1) _audio.Once("CarShift", 0.32f, true);
        }
        bool shifting = Time.time < _shiftUntil;
        float curve = 0.72f + 0.28f * Mathf.Sin(Mathf.Clamp01((_rpm - 1000f) / 5000f) * Mathf.PI);
        float drive = shifting ? 0f : _throttleNow * peakTorque * curve * (_gear == 0 ? reverseGear : gears[Mathf.Clamp(_gear - 1, 0, gears.Length - 1)]) * finalDrive * 0.9f;
        float limit = reversing ? reverseSpeed : topSpeed;
        drive *= Mathf.Sqrt(Mathf.Clamp01(1f - planar / Mathf.Max(0.1f, limit)));

        // ---- steering ------------------------------------------------------------
        float speedSq = Mathf.Max(1f, planar * planar);
        float lockAngle = Mathf.Min(maxSteer, Mathf.Atan(Wheelbase * cornerGrip / speedSq) * Mathf.Rad2Deg);
        float wanted = move.x * lockAngle;
        float time = Mathf.Abs(move.x) < 0.05f ? steerBack : steerIn;
        _steer = Mathf.MoveTowards(_steer, wanted, Mathf.Max(lockAngle, 10f) / Mathf.Max(0.02f, time) * dt);
        // The inside wheel turns more than the outer (Ackermann): it keeps the tyres from fighting at a crawl.
        float tangent = Mathf.Tan(_steer * Mathf.Deg2Rad);
        float inner = Mathf.Atan(Mathf.Abs(tangent) / (1f - Mathf.Abs(tangent) * 0.3f)) * Mathf.Rad2Deg;
        if (frontLeft != null && frontRight != null)
        {
            if (_steer >= 0f) { frontRight.steerAngle = inner; frontLeft.steerAngle = _steer; }
            else { frontLeft.steerAngle = -inner; frontRight.steerAngle = _steer; }
        }

        // ---- wheels ----------------------------------------------------------------
        float idleBrake = 0f;
        if (Mathf.Abs(_move.y) <= 0.05f && !_gunWanted)
            idleBrake = planar < 0.8f ? 1f : engineBrake;      // the engine holds it back; under walking pace, it is held still
        SetTorque(drive, Mathf.Max(_brakeNow, idleBrake) * brakeTorque, handbrake ? brakeTorque * 1.6f : 0f);

        // Held still: wheel friction alone lets a stopped car creep.
        bool holdStill = (idleBrake >= 1f || (_gunWanted && _cruise < 0.2f)) && planar < 0.35f;
        if (holdStill && _phase == Phase.Seated) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }

        AntiRoll(frontLeft, frontRight);
        AntiRoll(rearLeft, rearRight);
        _rb.AddForce(-transform.up * downforce * _rb.linearVelocity.magnitude);

        // ---- the car doing what the wheels ask -----------------------------------
        int grounded = 0; float scrub = 0f;
        foreach (var w in Wheels)
        {
            if (w == null || !w.GetGroundHit(out var hit)) continue;
            grounded++;
            scrub = Mathf.Max(scrub, Mathf.Abs(hit.sidewaysSlip) * 1.1f, Mathf.Abs(hit.forwardSlip) * 0.9f);
        }
        _scrub = Mathf.Clamp01((scrub - 0.3f) * 1.5f);
        if (grounded >= 3 && !(handbrake && _phase == Phase.Seated))
        {
            // The turn the front wheels ask for.
            float asked = _speed * tangent / Wheelbase;
            float most = cornerGrip / Mathf.Max(3f, planar);
            asked = Mathf.Clamp(asked, -most, most);
            float now = Vector3.Dot(_rb.angularVelocity, transform.up);
            _rb.AddTorque(transform.up * (asked - now) * yawAssist, ForceMode.Acceleration);
            // A slide bled off.
            float sideways = Vector3.Dot(_rb.linearVelocity, transform.right);
            _rb.AddForce(-transform.right * sideways * slideAssist, ForceMode.Acceleration);
        }
    }

    float Wheelbase => _look != null && _look.wheelbase > 0.5f ? _look.wheelbase : 2.7f;

    void SetTorque(float motor, float brake, float handbrake)
    {
        if (rearLeft == null || frontLeft == null) return;
        float rear = driven == Driven.Rear ? motor * 0.5f : driven == Driven.All ? motor * 0.25f : 0f;
        float front = driven == Driven.Front ? motor * 0.5f : driven == Driven.All ? motor * 0.25f : 0f;
        rearLeft.motorTorque = rearRight.motorTorque = rear;
        frontLeft.motorTorque = frontRight.motorTorque = front;
        frontLeft.brakeTorque = frontRight.brakeTorque = brake;
        rearLeft.brakeTorque = rearRight.brakeTorque = Mathf.Max(brake * rearBrakeShare, handbrake);
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

    /// <summary>
    /// The player's own movement input, keys, pad and touch alike. The Move action could not be read here: its keys are separate buttons
    /// (GameInput.Bind), so reading it as a Vector2 threw on every held key, in FixedUpdate, and the car took no input at all.
    /// </summary>
    Vector2 ReadMove()
    {
        if (_motor != null) return _motor.ReadMoveInput();
        return MobileInput.Active ? MobileInput.Move : GameInput.PadMove;
    }

    void OnCollisionEnter(Collision c)
    {
        if (Driving != this) return;
        float hard = c.relativeVelocity.magnitude;
        if (hard < 2.5f) return;
        if (_audio != null) _audio.Once(hard > 8f ? "CarCrash_1" : "CarCrash_0", Mathf.Clamp01(hard / 14f), !_chase);
        if (_motor != null) _motor.AddShake(Mathf.Clamp01(hard / 18f));
        if (hard > 11f && TryGetComponent<CarGlass>(out var glass) && c.contactCount > 0) glass.Crash(c.GetContact(0).point, hard);
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
        float dt = Time.deltaTime;

        // ---- the sequence: door open, into the seat, door shut, engine ----------------
        if (_phase == Phase.GettingIn || _phase == Phase.GettingOut) { _phaseT += dt; }
        if (_phase == Phase.GettingIn)
        {
            if (_look != null) _look.Door(0, _phaseT < 0.42f ? _phaseT / 0.3f : 1f - (_phaseT - 0.42f) / 0.3f);
            if (!_engineRunning && _phaseT >= 0.62f)
            {
                _engineRunning = true;
                if (_audio != null) { _audio.Once("CarDoorClose", 0.9f, true); _audio.Once("CarStart", 0.9f, true); _audio.Switch(true); }
            }
            if (_phaseT >= 0.8f) { _phase = Phase.Seated; if (_look != null) _look.Door(0, 0f); }
        }
        else if (_phase == Phase.GettingOut)
        {
            if (_look != null) _look.Door(0, Mathf.Clamp01(_phaseT / 0.28f));
            if (_phaseT >= 0.55f) { FinishOut(); return; }
        }
        _gunHold = Mathf.MoveTowards(_gunHold, _gunWanted ? 0f : 1f, dt / Mathf.Max(0.05f, gunRaise));

        // ---- the sound and the dash, from the physics --------------------------------
        bool inside = !_chase;
        if (_audio != null) _audio.Drive(_rpm, Mathf.Abs(_throttleNow), Mathf.Abs(_speed), _scrub, inside, dt);
        float turn = _steer * steerRatio;
        if (_look != null)
        {
            _look.Dash(turn, Kmh, _rpm);
            _look.Lamps(_brakeNow > 0.05f, _gear == 0 && _speed < 0.2f && Mathf.Abs(_throttleNow) > 0.01f, false);
        }
        Exhaust();

        if (_player != null) _player.position = transform.position + Vector3.up * 0.3f;

        // ---- the view --------------------------------------------------------------
        if (_chase && _phase == Phase.Seated) { ChaseCamera(dt); Hands = default; return; }
        SeatCamera(dt, turn);
    }

    void SeatCamera(float dt, float turn)
    {
        // The head: what the look turned it by, recoil and shake from the motor.
        Vector3 kick = Vector3.zero;
        if (_motor != null && Time.timeScale > 0f && !OverlayPanel.AnyOpenThisFrame)
        {
            Vector2 delta = _motor.SeatLook(out kick);
            if (delta.sqrMagnitude > 1e-5f) _lastLookAt = Time.time;
            _lookYawNow = Mathf.Clamp(_lookYawNow + delta.x, -lookYaw, lookYaw);
            _lookPitchNow = Mathf.Clamp(_lookPitchNow - delta.y * (_motor.InvertLook ? -1f : 1f), -lookUp, lookDown);
        }
        // Gun down and moving, the eyes go back to the road.
        if (!_gunWanted && Mathf.Abs(_speed) > 3f && Time.time - _lastLookAt > lookReturnAfter)
        {
            float back = 1f - Mathf.Exp(-3f * dt);
            _lookYawNow = Mathf.Lerp(_lookYawNow, 0f, back);
            _lookPitchNow = Mathf.Lerp(_lookPitchNow, 0f, back);
        }

        // The car's motion reaches the head, softened: its heading whole, a share of its pitch and roll. Whole, every kerb rocked the view.
        Quaternion full = transform.rotation;
        Quaternion level = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        Quaternion seat = Quaternion.Slerp(level, full, 0.55f);

        // The head sways against what the car is doing, sits back under power, nods under brakes, leans out of a turn.
        Vector3 velocity = _rb.linearVelocity;
        Vector3 accel = dt > 0f ? (velocity - _lastVelocity) / dt : Vector3.zero;
        _lastVelocity = velocity;
        Vector3 localAccel = transform.InverseTransformDirection(accel);
        Vector3 lagTarget = new Vector3(Mathf.Clamp(-localAccel.x * 0.007f, -0.05f, 0.05f), Mathf.Clamp(-localAccel.y * 0.006f, -0.035f, 0.035f), Mathf.Clamp(-localAccel.z * 0.008f, -0.05f, 0.05f));
        _headLag = Vector3.SmoothDamp(_headLag, lagTarget, ref _headLagVel, 0.12f, 2f, dt);

        Vector3 eyeLocal = _look != null ? _look.seatEye : new Vector3(-0.38f, CarHeight * 0.85f, 0f);
        // Turning the head moves it: looking round the pillar, the eyes lean with it.
        float yawRad = _lookYawNow * Mathf.Deg2Rad;
        eyeLocal += new Vector3(Mathf.Sin(yawRad) * 0.07f, 0f, (1f - Mathf.Cos(yawRad)) * -0.03f);
        eyeLocal += _headLag;

        Vector3 eye = transform.TransformPoint(eyeLocal);
        // Into the turn a little, as anyone looks where the car is going. Not with the gun up: the aim is his.
        float into = _gunWanted ? 0f : _steer * 0.3f;
        Quaternion view = seat * Quaternion.Euler(seatTilt + _lookPitchNow + kick.x, _lookYawNow + into + kick.y, kick.z);

        if (_phase == Phase.GettingIn)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_phaseT / 0.62f));
            _camera.SetPositionAndRotation(Vector3.Lerp(_fromPos, eye, t), Quaternion.Slerp(_fromRot, view, t));
        }
        else if (_phase == Phase.GettingOut)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_phaseT / 0.55f));
            Vector3 stand = _toPos + Vector3.up * (_cc != null ? _cc.height * 0.9f : 1.5f);
            _camera.SetPositionAndRotation(Vector3.Lerp(eye, stand, t), Quaternion.Slerp(view, level, t));
        }
        else
            _camera.SetPositionAndRotation(eye, view);

        Hands = new HandsOnWheel { hold = _phase == Phase.Seated || _phaseT > 0.3f ? _gunHold : 0f, turn = turn, eye = eye, car = full, look = _look };
    }

    void ChaseCamera(float dt)
    {
        // Chase camera: fixed to the car's place, its heading easing after the car's, level whatever the car's roll and pitch.
        _camYaw = Mathf.SmoothDampAngle(_camYaw, transform.eulerAngles.y, ref _camYawVel, cameraTurnTime);
        Quaternion heading = Quaternion.Euler(0f, _camYaw, 0f);
        Vector3 pivot = transform.position + Vector3.up * cameraPivot;
        Vector3 away = heading * cameraOffset + Vector3.up * (-cameraPivot);
        float full = away.magnitude;
        // Never through a wall: in at once when something is in the way, out again slowly.
        int solid = ~LayerMask.GetMask("Vehicle", "Player", "PlayerBounds", "FirstPerson", "Enemy", "Civilian", "Ignore Raycast", "UI");
        float free = Physics.SphereCast(pivot, 0.25f, away / full, out var hit, full, solid, QueryTriggerInteraction.Ignore) ? Mathf.Max(0.5f, hit.distance - 0.2f) : full;
        _camDistance = free < _camDistance ? free : Mathf.MoveTowards(_camDistance, free, full / 0.6f * dt);
        _camera.position = pivot + away / full * _camDistance;
        Vector3 look = pivot + heading * Vector3.forward * 5f;
        _camera.rotation = Quaternion.LookRotation(look - _camera.position);
    }

    /// <summary>A thin smoke at the pipe under throttle, and sand kicked up by the rear tyres. One particle system each, made when first driven.</summary>
    void Exhaust()
    {
        if (_look == null || _phase != Phase.Seated) return;
        if (_smoke == null)
        {
            var mat = Resources.Load<Material>("Car/Dust");
            if (mat == null) return;
            _smoke = MakePuffs("Exhaust", mat, 18, 0.5f, new Color(0.45f, 0.45f, 0.45f, 0.08f), 0.08f, 0.5f);
            _smoke.transform.SetLocalPositionAndRotation(_look.exhaust, Quaternion.LookRotation(Vector3.back));
            _dust = MakePuffs("Dust", mat, 40, 1.3f, new Color(0.72f, 0.6f, 0.44f, 0.16f), 0.5f, 2.2f);
            _dust.transform.SetLocalPositionAndRotation(new Vector3(0f, 0.08f, -_look.wheelbase * 0.5f), Quaternion.LookRotation(Vector3.up));
        }
        var se = _smoke.emission;
        se.rateOverTime = Mathf.Abs(_throttleNow) * 16f + 2f;
        var de = _dust.emission;
        float kick = Mathf.Clamp01(Mathf.Abs(_speed) / 18f) * 10f + _scrub * 28f;
        de.rateOverTime = kick;
    }

    ParticleSystem MakePuffs(string name, Material mat, int max, float life, Color colour, float size0, float size1)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max; main.startLifetime = life; main.startSpeed = 0.5f; main.startSize = size0; main.startColor = colour;
        main.playOnAwake = true; main.loop = true;
        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(1.4f, 0.05f, 0.1f);
        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(size1 / Mathf.Max(0.01f, size0), AnimationCurve.Linear(0f, 0.25f, 1f, 1f));
        var col = ps.colorOverLifetime; col.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = fade;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        ps.Play();
        return ps;
    }
}
