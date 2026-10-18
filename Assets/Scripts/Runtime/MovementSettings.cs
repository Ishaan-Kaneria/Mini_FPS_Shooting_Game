using UnityEngine;

/// <summary>
/// How the player moves: the weight of it. Loaded from <c>Resources/MovementSettings</c>
/// and read by <see cref="PlayerMotor"/> over the speeds a scene was built with, so the
/// feel is tuned in one asset and every arena picks it up without a rebuild.
///
/// The model is a person carrying a rifle rather than a camera on rails: a jog that takes
/// a moment to reach and a moment to stop, a sprint that builds and carves wide turns and
/// runs out, a backpedal and a sidestep slower than going forward, and a combat jog -- fire
/// or aim while sprinting and you drop to a fast jog with the gun up, the way a GTA
/// character runs and guns, instead of shooting from a rifle held across the chest.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Movement Settings", fileName = "MovementSettings")]
public class MovementSettings : ScriptableObject
{
    [Header("Speeds, metres a second")]
    [Tooltip("Moving normally: a jog with the rifle ready.")]
    [Range(1f, 8f)] public float jogSpeed = 6.0f;
    [Tooltip("Flat out. Nothing an enemy does outruns this (CLAUDE.md).")]
    [Range(4f, 12f)] public float sprintSpeed = 9.5f;
    [Tooltip("Firing or aiming while the sprint is held: a fast jog with the gun up.")]
    [Range(2f, 8f)] public float combatJogSpeed = 6.4f;
    [Range(0.5f, 5f)] public float crouchSpeed = 3.0f;
    [Tooltip("Share of speed when moving straight back.")]
    [Range(0.3f, 1f)] public float backpedal = 0.8f;
    [Tooltip("Share of speed when moving straight sideways.")]
    [Range(0.3f, 1f)] public float strafe = 0.92f;
    [Tooltip("Share of speed while aiming down the sights.")]
    [Range(0.2f, 1f)] public float aiming = 0.65f;

    [Header("Weight")]
    [Tooltip("How quickly a jog is reached, m/s². About a fifth of a second.")]
    [Range(5f, 80f)] public float acceleration = 34f;
    [Tooltip("How quickly a sprint builds once going. Lower: it takes a few strides.")]
    [Range(2f, 60f)] public float sprintAcceleration = 18f;
    [Tooltip("Braking when the keys are let go, m/s². Lower slides further.")]
    [Range(5f, 80f)] public float stopping = 26f;
    [Tooltip("Degrees a second a sprint can change direction. Low carves wide turns.")]
    [Range(60f, 1080f)] public float sprintTurnRate = 540f;

    [Header("Stamina")]
    [Tooltip("Run for ever: the sprint never runs out (his pick). Off brings the breath back.")]
    public bool infiniteSprint = true;
    [Tooltip("Seconds of flat-out sprint on a full breath.")]
    [Range(1f, 30f)] public float sprintSeconds = 7f;
    [Tooltip("Seconds to get the whole breath back, standing or jogging.")]
    [Range(1f, 30f)] public float recoverSeconds = 6f;
    [Tooltip("Once blown, the share of breath needed before the sprint comes back.")]
    [Range(0f, 1f)] public float sprintAgainAt = 0.3f;

    [Header("Steps (the view's own motion on them is EyeCameraSettings')")]
    [Tooltip("Metres a step covers at a walk, when the steps are counted by distance (no body); the footsteps and the bob both follow it.")]
    [Range(0.4f, 1.5f)] public float stepLength = 0.78f;
    [Tooltip("Metres a step covers at a sprint (unused: the step grows with speed, strideGrowth).")]
    [Range(0.6f, 2f)] public float sprintStepLength = 1.15f;
    [Tooltip("Metres the step lengthens per metre a second faster than a walk: a run is long strides, " +
             "not quick ones (about 3.5 steps a second at a jog, 4 at a sprint). Only without the body's legs.")]
    [Range(0f, 0.4f)] public float strideGrowth = 0.19f;

    [Header("Handling")]
    [Tooltip("Reload time as a share of the gun's own. Under 1 is quicker.")]
    [Range(0.2f, 1.5f)] public float reloadTimeScale = 0.6f;
    [Tooltip("How much the gun bobs and swings with the steps, as a share of the scene's sway.")]
    [Range(0f, 1f)] public float gunBob = 0.55f;

    // ======================================================================

    public const string ResourcePath = "MovementSettings";
    static MovementSettings _active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _active = null;

    public static MovementSettings Active
    {
        get
        {
            if (_active != null) return _active;
            _active = Resources.Load<MovementSettings>(ResourcePath);
            if (_active == null)
            {
                _active = CreateInstance<MovementSettings>();
                _active.name = "Default Movement";
            }
            return _active;
        }
    }
}
