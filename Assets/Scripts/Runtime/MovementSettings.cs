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
    [Range(1f, 8f)] public float jogSpeed = 4.6f;
    [Tooltip("Flat out. Nothing an enemy does outruns this (CLAUDE.md).")]
    [Range(4f, 10f)] public float sprintSpeed = 8.0f;
    [Tooltip("Firing or aiming while the sprint is held: a fast jog with the gun up.")]
    [Range(2f, 8f)] public float combatJogSpeed = 5.3f;
    [Range(0.5f, 5f)] public float crouchSpeed = 2.4f;
    [Tooltip("Share of speed when moving straight back.")]
    [Range(0.3f, 1f)] public float backpedal = 0.68f;
    [Tooltip("Share of speed when moving straight sideways.")]
    [Range(0.3f, 1f)] public float strafe = 0.85f;
    [Tooltip("Share of speed while aiming down the sights.")]
    [Range(0.2f, 1f)] public float aiming = 0.55f;

    [Header("Weight")]
    [Tooltip("How quickly a jog is reached, m/s². About a fifth of a second.")]
    [Range(5f, 80f)] public float acceleration = 22f;
    [Tooltip("How quickly a sprint builds once going. Lower: it takes a few strides.")]
    [Range(2f, 60f)] public float sprintAcceleration = 9f;
    [Tooltip("Braking when the keys are let go, m/s². Lower slides further.")]
    [Range(5f, 80f)] public float stopping = 17f;
    [Tooltip("Degrees a second a sprint can change direction. Low carves wide turns.")]
    [Range(60f, 1080f)] public float sprintTurnRate = 260f;

    [Header("Stamina")]
    [Tooltip("Seconds of flat-out sprint on a full breath.")]
    [Range(1f, 30f)] public float sprintSeconds = 7f;
    [Tooltip("Seconds to get the whole breath back, standing or jogging.")]
    [Range(1f, 30f)] public float recoverSeconds = 6f;
    [Tooltip("Once blown, the share of breath needed before the sprint comes back.")]
    [Range(0f, 1f)] public float sprintAgainAt = 0.3f;

    [Header("The view")]
    [Tooltip("Metres the view dips each footfall at a jog.")]
    [Range(0f, 0.1f)] public float bobDip = 0.028f;
    [Tooltip("Metres the view sways side to side over a stride.")]
    [Range(0f, 0.1f)] public float bobSway = 0.02f;
    [Tooltip("Bob at a sprint, as a multiple of the jog's.")]
    [Range(1f, 3f)] public float sprintBob = 1.7f;
    [Tooltip("Degrees the view rolls each step.")]
    [Range(0f, 3f)] public float stepRoll = 0.7f;
    [Tooltip("Degrees the view leans against a sidestep.")]
    [Range(0f, 6f)] public float strafeLean = 2.2f;
    [Tooltip("Metres a step covers at a jog; the footsteps and the bob both follow it.")]
    [Range(0.4f, 1.5f)] public float stepLength = 0.78f;
    [Tooltip("Metres a step covers at a sprint.")]
    [Range(0.6f, 2f)] public float sprintStepLength = 1.15f;

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
