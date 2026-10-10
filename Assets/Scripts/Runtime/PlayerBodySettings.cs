using UnityEngine;

/// <summary>
/// Every number the player's own body runs on: where it stands under the camera, how the
/// rifle sits and weighs in the hands, the aim and the sights, the recoil, the flinch when
/// hit, and the clips its strikes are drawn with. One asset, tuned in the Inspector.
///
/// Loaded from <c>Resources/PlayerBodySettings</c>; with none, the field initialisers are the
/// defaults. The body and its clip timings are written by FPSKit > Player > Build Body.
/// See <see cref="PlayerBody"/>.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Player Body Settings", fileName = "PlayerBodySettings")]
public class PlayerBodySettings : ScriptableObject
{
    [Header("Body")]
    [Tooltip("The player's full body (built by FPSKit > Player > Build Body). Empty turns the body off: " +
             "the old arms-and-legs view model is used instead.")]
    public GameObject bodyPrefab;
    [Tooltip("Metres the eyes sit ahead of the head bone's centre.")]
    [Range(0f, 0.2f)] public float eyeAhead = 0.09f;
    [Tooltip("Metres the eyes sit above the head bone.")]
    [Range(-0.1f, 0.2f)] public float eyeUp = 0.06f;
    [Tooltip("How quickly the body slides under the camera when the head moves in its clip (per second). " +
             "The camera never follows the head: the body follows the camera, so a step never shakes the view.")]
    [Range(1f, 40f)] public float followRate = 14f;
    [Tooltip("Metres the body stands back from where the eyes are, looking straight down, so the chest " +
             "and vest are seen rather than the inside of the collar.")]
    [Range(0f, 0.6f)] public float lookDownBack = 0.22f;
    [Tooltip("How quickly the body is raised or lowered so its eyes meet the camera on average (per second). " +
             "Slow on purpose: each stride's rise and fall moves the arms and rifle, not the view.")]
    [Range(0.5f, 20f)] public float liftRate = 3f;
    [Tooltip("The most the body is lowered and raised for that, metres. Lowered further, its feet would be seen in the floor.")]
    public Vector2 liftLimits = new Vector2(-0.15f, 0.1f);

    [Header("Arms and weapon")]
    [Tooltip("Where the arms camera sits from the eye, metres (right, up, ahead). A real body's hands are level " +
             "with its eyes when it holds a rifle, out of sight; the arms and rifle are drawn from a little behind " +
             "and below the eye instead, so they are framed the way a shooter sees them. The body, the legs and the " +
             "shadow are drawn from the eye itself.")]
    public Vector3 armsViewOffset = new Vector3(0f, -0.07f, -0.25f);
    [Tooltip("Looking down this far (0 level, 1 straight down, as the sine of the angle), the arms camera is back at the eye: " +
             "an offset behind the eye in view space would be above the shoulders.")]
    [Range(0.1f, 1f)] public float armsOffsetGoneAt = 0.7f;
    [Tooltip("Where the arms camera sits from the eye aiming down sights, metres. At the eye: the rifle is raised to it.")]
    public Vector3 aimArmsOffset = Vector3.zero;
    [Tooltip("Share of the rifle's sway left aiming down sights (the kick is all kept).")]
    [Range(0f, 1f)] public float aimSwayShare = 0.4f;
    [Tooltip("Degrees the arms camera tips down at a full sprint, so the rifle carried low across the chest " +
             "(the sprint clip's own carry) is in view at the bottom of the screen.")]
    [Range(0f, 40f)] public float sprintArmsTilt = 22f;
    [Tooltip("Metres the arms camera drops at a full sprint, for the same reason.")]
    [Range(0f, 0.4f)] public float sprintArmsDrop = 0.16f;
    [Tooltip("Shares of the sprint clip's speed between which the body goes from running (rifle on the " +
             "crosshair) to sprinting (rifle across the chest, not aimed).")]
    public Vector2 sprintBetween = new Vector2(0.7f, 0.95f);
    [Tooltip("Share of the upper arm, from the shoulder, left with the body rather than drawn with the arms. " +
             "Above 0 the cut sleeve's open end shows on an arm thrown forward.")]
    [Range(0f, 1f)] public float armsFromUpperArm = 0.4f;
    [Tooltip("Near clip plane of the arms camera, metres.")]
    [Range(0.005f, 0.1f)] public float armsNearClip = 0.01f;
    [Tooltip("Share of the rifle's aim up and down taken by bending the spine; the arms swing the rest at the " +
             "shoulders. All at the spine, looking at the floor bent the chest under the eyes.")]
    [Range(0f, 1f)] public float spinePitchShare = 0.35f;
    [Tooltip("Degrees a second the spine turns to bring the rifle onto the crosshair.")]
    [Range(1f, 40f)] public float aimRate = 14f;
    [Tooltip("The furthest the spine turns the rifle off the body's own aim, degrees.")]
    [Range(10f, 90f)] public float aimLimit = 70f;
    [Tooltip("How quickly the arms camera moves between the hip and the sights (per second).")]
    [Range(1f, 40f)] public float sightsRate = 12f;

    [Header("Weight: the rifle lags and settles")]
    [Range(20f, 600f)] public float swayStiffness = 150f;
    [Range(1f, 60f)] public float swayDamping = 18f;
    [Tooltip("Metres the rifle lags per metre-a-second-squared the hands accelerate.")]
    [Range(0f, 0.05f)] public float swayInertia = 0.005f;
    [Tooltip("Degrees the rifle lags per degree-a-second the view turns.")]
    [Range(0f, 0.1f)] public float swayTurnInertia = 0.02f;
    [Tooltip("The most it ever lags: metres, and degrees.")]
    public Vector2 swayLimit = new Vector2(0.035f, 6f);

    [Header("Recoil: the rifle and arms kick, then settle")]
    [Tooltip("Metres a shot drives the rifle back into the shoulder.")]
    [Range(0f, 0.1f)] public float recoilBack = 0.03f;
    [Tooltip("Degrees a shot kicks the muzzle up.")]
    [Range(0f, 10f)] public float recoilPitch = 3f;
    [Tooltip("Degrees it kicks sideways, either way at random.")]
    [Range(0f, 5f)] public float recoilYaw = 0.8f;
    [Range(20f, 800f)] public float recoilStiffness = 260f;
    [Range(1f, 60f)] public float recoilDamping = 24f;
    [Tooltip("Aiming down sights, the kick is this share of the hip kick.")]
    [Range(0f, 1f)] public float recoilAimShare = 0.6f;

    [Header("Hit: the arms and rifle flinch (the view does not)")]
    [Tooltip("Degrees a second a hit sets the upper body turning at.")]
    [Range(0f, 600f)] public float flinchKick = 220f;
    [Range(20f, 600f)] public float flinchStiffness = 160f;
    [Range(1f, 60f)] public float flinchDamping = 16f;

    [Header("Strikes (filled by FPSKit > Player > Build Body)")]
    [Tooltip("The clip each strike is drawn with, and where in it the blow lands (share of the clip). " +
             "The clip is played so that moment arrives exactly when MeleeStrike deals the damage.")]
    public StrikeClip[] strikes = new StrikeClip[0];
    [Tooltip("Metres a second the forward sprint clip moves at, at the body's size (measured by the builder). " +
             "Sprinting faster than that plays the clip faster, so the feet keep up.")]
    [Min(0.5f)] public float sprintClipSpeed = 6.8f;
    [Tooltip("Degrees the body leans back for a kick (around its feet), which lifts the kicking leg into view.")]
    [Range(0f, 30f)] public float kickLean = 14f;
    [Tooltip("Share of the reload clip at which the magazine leaves the rifle, and at which the new one goes in.")]
    public Vector2 reloadMagazineBeats = new Vector2(0.3f, 0.62f);

    [System.Serializable]
    public struct StrikeClip
    {
        public MeleeStrike.Strike strike;
        [Tooltip("The Animator state that plays it.")]
        public string state;
        [Tooltip("The part of the clip the strike plays, as shares of it (a jab-cross clip gives the cross alone).")]
        public Vector2 range;
        [Tooltip("Share of the whole clip at which the blow lands (inside the range).")]
        [Range(0f, 1f)] public float impact;
        [Tooltip("Degrees to the right the blow goes in the clip (measured): the body is turned the other way " +
                 "for it, so the blow lands ahead, on the crosshair.")]
        public float turn;
        [Tooltip("Where the rifle is during it: in the right hand, in the left (low, to the side), or on the sling.")]
        public RifleDuring rifle;
    }

    public enum RifleDuring { RightHand, LeftHand, Sling }

    public bool TryStrike(MeleeStrike.Strike strike, out StrikeClip clip)
    {
        foreach (var s in strikes)
            if (s.strike == strike && !string.IsNullOrEmpty(s.state)) { clip = s; return true; }
        clip = default;
        return false;
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
