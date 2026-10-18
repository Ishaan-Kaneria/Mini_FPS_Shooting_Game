using UnityEngine;

/// <summary>
/// How the first-person view moves like a pair of eyes in a head (<see cref="EyeCamera"/>): the bob and
/// sway on each step, the lag and settle of starting, stopping and turning, the lean into a sidestep,
/// the dip of a landing, and breathing. Every amount is scaled by the player's Head Bob setting and
/// all but the breath nearly vanish down the sights. Loaded from <c>Resources/EyeCameraSettings</c>
/// (FPSKit > Player > Rebuild Runtime Settings writes it); a missing asset falls back to these values.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Eye Camera Settings", fileName = "EyeCameraSettings")]
public class EyeCameraSettings : ScriptableObject
{
    [System.Serializable]
    public class Gait
    {
        [Tooltip("Metres a second this gait is made for: between two gaits the amounts blend by speed.")]
        [Min(0f)] public float speed;
        [Tooltip("Metres the head drops just after each footfall.")]
        [Range(0f, 0.08f)] public float dip;
        [Tooltip("Metres the head shifts over the planted foot, side to side over a stride.")]
        [Range(0f, 0.05f)] public float sway;
        [Tooltip("Degrees the head rolls with the weight shift.")]
        [Range(0f, 3f)] public float roll;
        [Tooltip("Degrees the head nods down with each footfall.")]
        [Range(0f, 2f)] public float nod;
    }

    [Header("Steps (on the footsteps: the bob is the same event as the sound)")]
    [Tooltip("Barely moving: a careful step is almost still.")]
    public Gait creep = new Gait { speed = 1.2f, dip = 0.002f, sway = 0.001f, roll = 0.03f, nod = 0.03f };
    public Gait walk = new Gait { speed = 3.2f, dip = 0.009f, sway = 0.005f, roll = 0.18f, nod = 0.12f };
    public Gait jog = new Gait { speed = 6f, dip = 0.018f, sway = 0.008f, roll = 0.32f, nod = 0.25f };
    public Gait sprint = new Gait { speed = 9.5f, dip = 0.03f, sway = 0.012f, roll = 0.55f, nod = 0.45f };
    [Tooltip("Share of the stride, after each footfall, at which the head is lowest (the weight landing).")]
    [Range(0f, 0.4f)] public float dipAfterFootfall = 0.1f;
    [Tooltip("The bob crouched, as a share of standing.")]
    [Range(0f, 1f)] public float crouched = 0.3f;

    [Header("Inertia: the head is carried, not bolted on")]
    [Tooltip("Metres the head is left behind per m/s² of the body's acceleration (starting, stopping).")]
    [Range(0f, 0.003f)] public float lagPerAccel = 0.0009f;
    [Tooltip("Degrees the head pitches into a start (and back out of a stop) per m/s² forward.")]
    [Range(0f, 0.2f)] public float pitchPerAccel = 0.05f;
    [Tooltip("Degrees the view leans against a sidestep at a jog.")]
    [Range(0f, 4f)] public float strafeLean = 1f;
    [Tooltip("Degrees the view rolls into a turn per 100 degrees a second of turning. Never a lag in the aim.")]
    [Range(0f, 2f)] public float turnRoll = 0.35f;
    [Tooltip("How briskly the head settles after a change, per second (a spring's natural frequency).")]
    [Range(2f, 30f)] public float settle = 11f;
    [Tooltip("Under 1 the settle overshoots a little before it stops, as a neck does.")]
    [Range(0.3f, 1.2f)] public float damping = 0.72f;
    [Tooltip("The most the inertia may move the head, metres and degrees.")]
    public float maxLag = 0.03f, maxTilt = 2.5f;

    [Header("Landing")]
    [Tooltip("Metres a second the head is driven down per m/s of landing speed; it springs back.")]
    [Range(0f, 0.2f)] public float landKick = 0.1f;
    [Tooltip("Landing speeds under this are a step down, not a landing.")]
    [Range(0f, 6f)] public float landFrom = 2f;
    [Tooltip("Degrees the head nods on a hard landing.")]
    [Range(0f, 4f)] public float landNod = 1.2f;

    [Header("Breathing")]
    [Tooltip("Breaths a minute at rest, and blown.")]
    public float restRate = 14f, blownRate = 34f;
    [Tooltip("Degrees the view rises and falls with a breath at rest, and blown.")]
    public float restPitch = 0.12f, blownPitch = 0.55f;
    [Tooltip("Metres the head rises with a breath at rest, and blown.")]
    public float restLift = 0.0012f, blownLift = 0.004f;
    [Tooltip("Seconds of sprinting to be fully blown, and to get the breath back after.")]
    public float blowSeconds = 6f, recoverSeconds = 9f;

    [Header("Aiming down the sights")]
    [Tooltip("What is left of the bob, sway, roll and inertia fully aimed.")]
    [Range(0f, 1f)] public float aimedMotion = 0.15f;
    [Tooltip("What is left of the breath fully aimed (held steady on the sights).")]
    [Range(0f, 1f)] public float aimedBreath = 0.35f;

    [Header("Motion blur (Settings > Video, off by default)")]
    [Range(0f, 1f)] public float blurIntensity = 0.22f;
    [Range(0f, 0.2f)] public float blurClamp = 0.05f;

    // ======================================================================

    public const string ResourcePath = "EyeCameraSettings";
    static EyeCameraSettings _active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _active = null;

    public static EyeCameraSettings Active
    {
        get
        {
            if (_active != null) return _active;
            _active = Resources.Load<EyeCameraSettings>(ResourcePath);
            if (_active == null)
            {
                _active = CreateInstance<EyeCameraSettings>();
                _active.name = "Default Eye Camera";
            }
            return _active;
        }
    }

    /// <summary>The step amounts at a speed: blended between the two gaits either side of it (none standing still).</summary>
    public void At(float speed, out float dip, out float sway, out float roll, out float nod)
    {
        Gait a, b;
        float t;
        if (speed <= creep.speed) { a = null; b = creep; t = creep.speed > 0f ? Mathf.Clamp01(speed / creep.speed) : 1f; }
        else if (speed <= walk.speed) { a = creep; b = walk; t = Mathf.InverseLerp(creep.speed, walk.speed, speed); }
        else if (speed <= jog.speed) { a = walk; b = jog; t = Mathf.InverseLerp(walk.speed, jog.speed, speed); }
        else if (speed <= sprint.speed) { a = jog; b = sprint; t = Mathf.InverseLerp(jog.speed, sprint.speed, speed); }
        else { a = sprint; b = sprint; t = 1f; }
        dip = Mathf.Lerp(a != null ? a.dip : 0f, b.dip, t);
        sway = Mathf.Lerp(a != null ? a.sway : 0f, b.sway, t);
        roll = Mathf.Lerp(a != null ? a.roll : 0f, b.roll, t);
        nod = Mathf.Lerp(a != null ? a.nod : 0f, b.nod, t);
    }
}
