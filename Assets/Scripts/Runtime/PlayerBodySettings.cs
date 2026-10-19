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
    [Tooltip("Share of the body's speed the legs' movement clips are asked for, at a walk (3 m/s), a jog (6) and a sprint " +
             "(9.5), blended between. The clips' feet do not move at their measured speeds (the planted foot slid forward " +
             "at a walk and back 18-20% at a jog and a sprint, FPSKitEyeCamTest); these put it back under the body.")]
    public Vector3 legPace = new Vector3(1.17f, 0.92f, 0.81f);

    /// <summary>The legs' pace at a speed, metres a second: <see cref="legPace"/> blended between walk, jog and sprint.</summary>
    public float LegPaceAt(float speed) => speed <= 3f ? legPace.x
        : speed <= 6f ? Mathf.Lerp(legPace.x, legPace.y, (speed - 3f) / 3f)
        : Mathf.Lerp(legPace.y, legPace.z, Mathf.Clamp01((speed - 6f) / 3.5f));
    [Tooltip("Degrees the body leans back for a kick (around its feet), which lifts the kicking leg into view.")]
    [Range(0f, 30f)] public float kickLean = 14f;
    [Tooltip("Degrees right of the crosshair the boot lands, so at the moment of the kick it is beside the " +
             "enemy's head and chest rather than over them (measured by FPSKitKickViewTest). The blow still " +
             "lands on whoever is under the crosshair.")]
    [Range(0f, 25f)] public float kickAside = 8f;
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
    public Vector3 hipNudge = new Vector3(0.04f, 0.05f, 0.06f);
    [Tooltip("Metres added to the hip carry on a phone (right, up, ahead): its own framing, clear of the fire " +
             "button, which sits over the lower right of the screen where the rifle is on a desktop.")]
    public Vector3 handsetHipNudge = new Vector3(-0.10f, -0.02f, 0f);
    [Tooltip("Degrees added to each gun's own hip turn (muzzle down, muzzle right, rolled right). Positive right " +
             "turns the muzzle out of its angle in towards the middle, so a carry moved right still points ahead.")]
    public Vector3 hipTurnNudge = new Vector3(0f, 2f, 0f);

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

    [Header("Fighting: more of the body and the gun in view (only while it happens)")]
    [Tooltip("Seconds each of these blends in as the action starts, and out again after it.")]
    [Range(0.02f, 0.5f)] public float fightBlend = 0.1f;
    [Tooltip("Metres the rifle comes up and in while firing from the hip (right, up, ahead), so the gun and its kick are " +
             "seen. Never down the sights. It also lifts the stock off the ability slots.")]
    public Vector3 hipFireLift = new Vector3(-0.035f, 0.04f, -0.01f);
    [Tooltip("Degrees the rifle turns with that lift (muzzle down, right, rolled right).")]
    public Vector3 hipFireTurn = new Vector3(0f, -2f, 0f);
    [Tooltip("Seconds after the last shot the rifle stays lifted, so a burst is one lift, not one per round.")]
    [Range(0f, 1.5f)] public float fireHold = 0.4f;
    [Tooltip("Metres the rifle comes up and in for the rifle-butt strike.")]
    public Vector3 buttLift = new Vector3(-0.025f, 0.035f, 0f);
    [Tooltip("Metres the shoulders drop and come forward while the fists are up (right, up, ahead): more forearm in view.")]
    public Vector3 punchArms = new Vector3(0f, -0.04f, 0.05f);
    [Tooltip("Metres each fist is thrown wider, to its own side, and lower: it comes in from the edge of the view and the " +
             "whole forearm is seen at the blow. The guard is held half as wide and no lower.")]
    [Range(0f, 0.12f)] public float punchWiden = 0.045f;
    [Range(0f, 0.12f)] public float punchLower = 0.035f;
    [Tooltip("Metres the rifle slides through a kick on a phone (right, up, ahead): carried flat along the bottom, its stock " +
             "lay under the touch fire button (31% of it on a Pixel 7).")]
    public Vector3 handsetKickGun = new Vector3(-0.12f, -0.05f, 0f);
    [Tooltip("Extra degrees the body leans back for a kick, on top of kickLean: the leg rises higher into view.")]
    [Range(0f, 15f)] public float kickRise = 4f;
    [Tooltip("Metres the body comes forward under the eye through a kick: more of the leg in view.")]
    [Range(0f, 0.3f)] public float kickReach = 0.08f;

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
