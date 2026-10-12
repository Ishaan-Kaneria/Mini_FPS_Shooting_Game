using UnityEngine;

/// <summary>
/// Every number the player's own body and first-person view run on. The view is a hybrid:
///
/// - **What the player sees of himself holding the gun** is the first-person view model
///   (<see cref="ViewModelRig"/>): the pack's first-person arms, gloved and sleeved, locked onto
///   the gun's grip points by IK and posed in code, drawn by their own camera at their own
///   field of view. Its numbers are under "First-person view" and "Punches".
/// - **The rest of him** is a full body (<see cref="PlayerBody"/>): legs seen looking down and
///   in the kick, and the whole body (rifle included) in the shadow. Mixamo's clips move it;
///   they are third-person animation, so its arms and head are never drawn to the player's eye.
///
/// Loaded from <c>Resources/PlayerBodySettings</c>; with none, the field initialisers are the
/// defaults. The body and its kick timing are written by FPSKit > Player > Build Body.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Player Body Settings", fileName = "PlayerBodySettings")]
public class PlayerBodySettings : ScriptableObject
{
    [Header("Body (legs and shadow)")]
    [Tooltip("The player's full body (built by FPSKit > Player > Build Body). Empty turns it off: the view " +
             "model's own legs are used instead.")]
    public GameObject bodyPrefab;
    [Tooltip("Metres the eyes sit ahead of the head bone's centre.")]
    [Range(0f, 0.2f)] public float eyeAhead = 0.09f;
    [Tooltip("Metres the eyes sit above the head bone.")]
    [Range(-0.1f, 0.2f)] public float eyeUp = 0.06f;
    [Tooltip("How quickly the body slides under the camera when the head moves in its clip (per second). " +
             "The camera never follows the head: the body follows the camera, so a step never shakes the view.")]
    [Range(1f, 40f)] public float followRate = 14f;
    [Tooltip("How quickly the body is raised or lowered so its eyes meet the camera on average (per second).")]
    [Range(0.5f, 20f)] public float liftRate = 3f;
    [Tooltip("The most the body is lowered and raised for that, metres. Lowered further, its feet would be seen in the floor.")]
    public Vector2 liftLimits = new Vector2(-0.15f, 0.1f);
    [Tooltip("Metres the body stands back from where the eyes are, looking straight down. The vest's front " +
             "is 15 cm ahead of the eye standing: back this far, the eye clears it and the forward boot is seen.")]
    [Range(0f, 0.6f)] public float lookDownBack = 0.22f;
    [Tooltip("Metres a second the forward sprint clip moves at, at the body's size (measured by the builder).")]
    [Min(0.5f)] public float sprintClipSpeed = 6.8f;
    [Tooltip("Degrees the body leans back for a kick (around its feet), which lifts the kicking leg into view.")]
    [Range(0f, 30f)] public float kickLean = 14f;
    [Tooltip("The kick's clip, where in it the foot lands and which way it goes (filled by the builder).")]
    public StrikeClip kick;

    [Header("First-person view (arms and weapon)")]
    [Tooltip("Metres the eye is set back from the camera for the arms and weapon, so they are seen the way " +
             "one's own arms are, from a little behind them, rather than from inside them.")]
    [Range(0f, 0.2f)] public float eyeBack = 0.07f;
    [Tooltip("Near clip plane of the arms camera, metres.")]
    [Range(0.005f, 0.1f)] public float armsNearClip = 0.01f;
    [Tooltip("Size of the arms against life. 1 is true size.")]
    [Range(0.8f, 1.3f)] public float armsScale = 1f;
    [Tooltip("Size of the gun against life. 1 is true size.")]
    [Range(0.8f, 1.3f)] public float gunScale = 1f;
    [Tooltip("Metres added to each gun's own hip position (right, up, ahead): the whole hip carry moved at once.")]
    public Vector3 hipNudge = Vector3.zero;

    [Header("Punches (in the view's metres: right, up, ahead)")]
    [Tooltip("Where the rifle's rear sight is while the left hand carries it, low at the side, for a single punch.")]
    public Vector3 leftCarryPosition = new Vector3(-0.08f, -0.24f, 0.42f);
    [Tooltip("How the rifle is turned while the left hand carries it (degrees: muzzle down, in, rolled).")]
    public Vector3 leftCarryTurn = new Vector3(8f, -75f, -5f);
    [Tooltip("Where the right fist starts a punch from, and goes back to before taking the grip again.")]
    public Vector3 rightGuard = new Vector3(0.12f, -0.16f, 0.26f);
    [Tooltip("Where the right fist lands: on the crosshair, a forearm's length out.")]
    public Vector3 rightReach = new Vector3(0.0f, -0.05f, 0.56f);
    [Tooltip("In the double punch, how far the left jab goes before the right cross (share of a full reach).")]
    [Range(0f, 1f)] public float doubleJabReach = 0.75f;

    [System.Serializable]
    public struct StrikeClip
    {
        [Tooltip("The Animator state that plays it.")]
        public string state;
        [Tooltip("The part of the clip played, as shares of it.")]
        public Vector2 range;
        [Tooltip("Share of the whole clip at which the blow lands (inside the range).")]
        [Range(0f, 1f)] public float impact;
        [Tooltip("Degrees to the right the blow goes in the clip (calibrated by playing it): the body is turned " +
                 "the other way for it, so the blow lands ahead, on the crosshair.")]
        public float turn;
    }

    // ======================================================================

    public const string ResourcePath = "PlayerBodySettings";

    static PlayerBodySettings _active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _active = null;

    /// <summary>The asset in Resources, or the defaults when there is none.</summary>
    public static PlayerBodySettings Active
    {
        get
        {
            if (_active != null) return _active;
            _active = Resources.Load<PlayerBodySettings>(ResourcePath);
            if (_active == null)
            {
                _active = CreateInstance<PlayerBodySettings>();
                _active.name = "Default Player Body";
            }
            return _active;
        }
    }
}
