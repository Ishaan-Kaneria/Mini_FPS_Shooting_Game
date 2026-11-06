using UnityEngine;

/// <summary>
/// The moving parts and lamps of one of the town's Blender cars (Tools/blender/make_cars.py), found by name by the builder (FPSKitCars) and used by whoever is driving it:
/// the player (<see cref="DrivableCar"/>) or the town (<see cref="TrafficCar"/>). Its paint is a material on the body's first slot. No update: the driver calls in.
/// </summary>
[DisallowMultipleComponent]
public class CarLook : MonoBehaviour
{
    [Header("Written by the builder")]
    [Tooltip("Everything painted: slot 0 of each of these.")]
    public Renderer[] painted;
    public Transform[] wheels = new Transform[4];
    [Tooltip("FL, FR, RL, RR. Null where the model has no such door.")]
    public Transform[] doors = new Transform[4];
    [Tooltip("Which way each door swings about Y, degrees when open: out from the car.")]
    public float[] doorOpen = new float[4];
    public Transform steerWheel, needleSpeed, needleRpm;
    [Tooltip("The column's direction, towards the driver, in the car's space.")]
    public Vector3 steerAxis = new Vector3(0f, 0.3f, -1f);
    [Tooltip("Hub of the steering wheel in the car's space, and the rim's radius.")]
    public Vector3 steerHub;
    public float rimRadius = 0.185f;
    public Vector3 seatEye = new Vector3(-0.38f, 1.2f, 0f);
    public Vector3 exhaust;
    public Renderer glowBrake, glowReverse, glowHead;
    public Transform model;
    [Tooltip("This car keeps its own paint (a taxi is a taxi).")]
    public bool fixedPaint;
    public float wheelRadius = 0.33f;
    public float wheelbase = 2.7f;

    Quaternion[] _doorRest;
    Quaternion _steerRest, _needleSpeedRest, _needleRpmRest;
    bool _rested;
    float[] _spin;
    float _lastSteer = float.NaN, _lastDoor0;

    void Rest()
    {
        if (_rested) return;
        _rested = true;
        _doorRest = new Quaternion[4];
        for (int i = 0; i < 4; i++) if (doors != null && i < doors.Length && doors[i] != null) _doorRest[i] = doors[i].localRotation;
        if (steerWheel != null) _steerRest = steerWheel.localRotation;
        if (needleSpeed != null) _needleSpeedRest = needleSpeed.localRotation;
        if (needleRpm != null) _needleRpmRest = needleRpm.localRotation;
        _spin = new float[4];
    }

    public void SetPaint(Material paint)
    {
        if (paint == null || fixedPaint || painted == null) return;
        foreach (var r in painted)
        {
            if (r == null) continue;
            var mats = r.sharedMaterials;
            if (mats.Length == 0 || mats[0] == paint) continue;
            mats[0] = paint;
            r.sharedMaterials = mats;
        }
    }

    public void Lamps(bool brake, bool reverse, bool head)
    {
        if (glowBrake != null && glowBrake.enabled != brake) glowBrake.enabled = brake;
        if (glowReverse != null && glowReverse.enabled != reverse) glowReverse.enabled = reverse;
        if (glowHead != null && glowHead.enabled != head) glowHead.enabled = head;
    }

    /// <summary>The wheel's turn in degrees, clockwise as the driver sees it, with the needles.</summary>
    public void Dash(float wheelTurn, float kmh, float rpm)
    {
        Rest();
        if (steerWheel != null && wheelTurn != _lastSteer)
        {
            _lastSteer = wheelTurn;
            steerWheel.localRotation = Quaternion.AngleAxis(wheelTurn, steerAxis) * _steerRest;
        }
        // Round the dial's own face: from lower left (zero) clockwise, as printed.
        if (needleSpeed != null) needleSpeed.localRotation = Quaternion.AngleAxis(-135f + Mathf.Clamp(kmh / 160f, 0f, 1f) * 270f, steerAxisOfDials) * _needleSpeedRest;
        if (needleRpm != null) needleRpm.localRotation = Quaternion.AngleAxis(-135f + Mathf.Clamp(rpm / 8000f, 0f, 1f) * 270f, steerAxisOfDials) * _needleRpmRest;
    }

    static readonly Vector3 steerAxisOfDials = Vector3.back;       // the dials face the driver, down the car

    /// <summary>A door, 0 shut to 1 open.</summary>
    public void Door(int which, float open)
    {
        Rest();
        if (doors == null || which >= doors.Length || doors[which] == null) return;
        doors[which].localRotation = Quaternion.Euler(0f, doorOpen[which] * Mathf.SmoothStep(0f, 1f, open), 0f) * _doorRest[which];
        if (which == 0) _lastDoor0 = open;
    }

    /// <summary>A car that moves itself along a route (no wheel colliders): the wheels roll and the front ones turn. All four, every frame: only while near.</summary>
    public void Roll(float speed, float steer, float dt)
    {
        Rest();
        if (wheels == null) return;
        float degrees = speed * dt / Mathf.Max(0.1f, wheelRadius) * Mathf.Rad2Deg;
        for (int i = 0; i < 4 && i < wheels.Length; i++)
        {
            if (wheels[i] == null) continue;
            _spin[i] = Mathf.Repeat(_spin[i] + degrees, 360f);
            wheels[i].localRotation = Quaternion.Euler(0f, i < 2 ? steer : 0f, 0f) * Quaternion.Euler(_spin[i], 0f, 0f);
        }
    }

    /// <summary>A point on the rim for a hand, in the world: <paramref name="side"/> -1 the driver's left, 1 his right, turned with the wheel but only so far (hands shuffle, they do not wind up).</summary>
    public Vector3 GripPoint(float side, float wheelTurn, out Vector3 axisWorld, out Vector3 hubWorld)
    {
        hubWorld = transform.TransformPoint(steerHub);
        axisWorld = transform.TransformDirection(steerAxis).normalized;
        Vector3 across = Vector3.ProjectOnPlane(transform.right, axisWorld).normalized;
        Vector3 up = Vector3.Cross(axisWorld, across);
        if (Vector3.Dot(up, transform.up) < 0f) up = -up;
        Vector3 at = (across * side * Mathf.Cos(10f * Mathf.Deg2Rad) + up * Mathf.Sin(10f * Mathf.Deg2Rad)) * rimRadius;
        return hubWorld + Quaternion.AngleAxis(Mathf.Clamp(wheelTurn, -70f, 70f), axisWorld) * at;
    }
}
