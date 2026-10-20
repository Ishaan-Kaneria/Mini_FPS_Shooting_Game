using UnityEngine;

/// <summary>
/// A car the player can get into and drive (the desert town; the 26 degree terrain cap was kept for this). Walk up to
/// it and press interact (E, D-pad up, or the RELOAD button while it reads DRIVE on a phone); drive with the move
/// controls (forward/back throttle and brake, left/right steer), Jump is the handbrake, interact again gets out
/// beside the driver's door.
///
/// Four WheelColliders on a Rigidbody, front-wheel steering, rear-wheel drive, an anti-roll pull and a downforce so
/// it holds the dunes' slopes. While driving the player's own body is parked and hidden, the motor's input is off
/// (<see cref="PlayerMotor.InputEnabled"/>), and the player's camera rides a chase rig behind the car. The vehicle
/// controller pack the user imported (BxB MVC) cannot load in this Unity version -- its DLLs need Unity.Mathematics
/// -- so this is written on the game's own input system instead, which is also the only way it works on touch.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DrivableCar : MonoBehaviour
{
    [Header("Wheels (written by the builder)")]
    public WheelCollider frontLeft, frontRight, rearLeft, rearRight;
    public Transform[] wheelVisuals = new Transform[4];

    [Header("Drive")]
    [Tooltip("Engine torque at each driven wheel, newton-metres.")]
    public float motorTorque = 900f;
    [Tooltip("Brake torque at each wheel.")]
    public float brakeTorque = 2600f;
    [Tooltip("Steering lock at a crawl, degrees; it narrows with speed.")]
    public float maxSteer = 32f;
    [Tooltip("Top speed in metres per second (about 100 km/h).")]
    public float topSpeed = 28f;
    [Tooltip("Pull towards level when the car rolls, and a push down with speed, so it does not tip on a dune.")]
    public float antiRoll = 6000f, downforce = 60f;

    [Header("Seat and camera")]
    [Tooltip("Where the player stands after getting out, in the car's space (beside the driver's door).")]
    public Vector3 exitOffset = new Vector3(-1.9f, 0f, 0.3f);
    [Tooltip("The chase camera: behind and above the car, looking at a point ahead of it.")]
    public Vector3 cameraOffset = new Vector3(0f, 2.6f, -6.8f);
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
    Renderer[] _hidden;
    float _nextLook;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.mass = 1350f;
        _rb.centerOfMass = new Vector3(0f, 0.35f, 0.1f);
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.isKinematic = true;          // parked until someone drives it: it cannot be shoved about or roll away
    }

    void OnEnable() => _all.Add(this);
    void OnDisable() { _all.Remove(this); if (Driving == this) GetOut(); }

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

    static bool InteractPressed()
    {
        var motor = Object.FindAnyObjectByType<PlayerMotor>();
        var keys = motor != null ? motor.controls : null;
        bool press = (keys != null && ControlSettings.Pressed(keys.interact)) | GameInput.PadPressed(GameAction.Interact);
        if (!MobileInput.PickUpReady) press |= MobileInput.ConsumeInteract();
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
        // The player's body and gun are not drawn while the car is.
        _hidden = p.GetComponentsInChildren<Renderer>();
        foreach (var r in _hidden) r.enabled = false;
        _camera.SetParent(null, true);

        _rb.isKinematic = false;
        Driving = this;
        Offered = null;
        Changed?.Invoke();
    }

    void GetOut()
    {
        if (Driving != this) return;
        Driving = null;
        SetTorque(0f, brakeTorque * 2f, 0f);
        if (_player != null)
        {
            var at = transform.TransformPoint(exitOffset);
            if (Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out var hit, 8f, ~LayerMask.GetMask("Player", "PlayerBounds"), QueryTriggerInteraction.Ignore))
                at = hit.point + Vector3.up * 0.05f;
            _player.position = at;
            _player.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            if (_cc != null) _cc.enabled = true;
            if (_hidden != null) foreach (var r in _hidden) if (r != null) r.enabled = true;
        }
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
        if (Mathf.Abs(speed) > topSpeed) throttle = 0f;

        float steer = move.x * Mathf.Lerp(maxSteer, maxSteer * 0.3f, Mathf.Clamp01(Mathf.Abs(speed) / topSpeed));
        frontLeft.steerAngle = frontRight.steerAngle = steer;
        SetTorque(throttle * motorTorque, brake * brakeTorque, handbrake ? brakeTorque * 1.5f : 0f);

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
        // Wheel meshes follow their colliders.
        var cols = new[] { frontLeft, frontRight, rearLeft, rearRight };
        for (int i = 0; i < 4; i++)
        {
            if (cols[i] == null || wheelVisuals == null || i >= wheelVisuals.Length || wheelVisuals[i] == null) continue;
            cols[i].GetWorldPose(out var pos, out var rot);
            wheelVisuals[i].SetPositionAndRotation(pos, rot);
        }
        if (Driving != this || _camera == null) return;
        // Chase camera: behind and above, smoothed, looking a little ahead of the car.
        var want = transform.TransformPoint(cameraOffset);
        _camera.position = Vector3.Lerp(_camera.position, want, 1f - Mathf.Exp(-6f * Time.deltaTime));
        var look = transform.position + transform.forward * 4f + Vector3.up * 1.2f;
        _camera.rotation = Quaternion.Slerp(_camera.rotation, Quaternion.LookRotation(look - _camera.position), 1f - Mathf.Exp(-8f * Time.deltaTime));
        if (_player != null) _player.position = transform.position + Vector3.up * 0.3f;
    }
}
