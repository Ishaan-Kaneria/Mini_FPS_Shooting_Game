using UnityEngine;

/// <summary>
/// The first-person arms' movement clips: how the rifle (and with it both hands, which the
/// view model keeps on it by IK) moves through one stride of walking, running, sprinting,
/// running while firing and walking while aiming. Each is a set of keyframed curves over the
/// stride -- the two footfalls fall at 0 and 0.5 -- for the rifle's offset (metres: right, up,
/// ahead, in the view) and turn (degrees: muzzle down, right, rolled right). Edit them in the
/// Inspector's curve editor; FPSKit > Player > Rebuild First-Person Moves writes the authored set.
///
/// They are played on the player's own footsteps (<see cref="PlayerMotor.StepPhase"/>, which
/// counts distance walked), so a clip can never drift out of step with the feet that are heard,
/// and they blend by gait over a moment, so walk, run, sprint and firing hand over with no pop.
/// Played by <see cref="ViewModelRig"/>. Loaded from <c>Resources/FirstPersonMoves</c>.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/First-Person Moves", fileName = "FirstPersonMoves")]
public class FirstPersonMoves : ScriptableObject
{
    [System.Serializable]
    public class Move
    {
        [Tooltip("Metres a second this clip is made for. Slower, it plays smaller; faster, no bigger.")]
        [Min(0.1f)] public float speed = 3f;
        [Tooltip("The rifle's offset through the stride, metres (0 and 0.5 are the footfalls).")]
        public AnimationCurve right = Flat(), up = Flat(), ahead = Flat();
        [Tooltip("The rifle's turn through the stride, degrees: muzzle down, muzzle right, rolled right.")]
        public AnimationCurve pitch = Flat(), yaw = Flat(), roll = Flat();

        public void Sample(float phase, out Vector3 offset, out Vector3 turn)
        {
            offset = new Vector3(right.Evaluate(phase), up.Evaluate(phase), ahead.Evaluate(phase));
            turn = new Vector3(pitch.Evaluate(phase), yaw.Evaluate(phase), roll.Evaluate(phase));
        }

        static AnimationCurve Flat() => AnimationCurve.Constant(0f, 1f, 0f);
    }

    [Header("Clips (one stride each)")]
    public Move walk = new Move { speed = 3.2f };
    public Move run = new Move { speed = 6f };
    [Tooltip("The rifle carried across the chest (Weapon's sprint pose), swinging with the arms on the steps.")]
    public Move sprint = new Move { speed = 9.5f };
    [Tooltip("Running while firing: the rifle up at the hip, steadied, still on the steps.")]
    public Move runFire = new Move { speed = 6.4f };
    [Tooltip("Walking aimed down the sights: barely moving, so the sights stay usable.")]
    public Move aimWalk = new Move { speed = 2.4f };

    [Header("Blending")]
    [Tooltip("How quickly one clip hands over to the next, per second.")]
    [Range(1f, 20f)] public float blendRate = 7f;
    [Tooltip("Seconds after a shot the run counts as running while firing.")]
    [Range(0f, 1f)] public float firingHold = 0.35f;

    // ======================================================================

    public const string ResourcePath = "FirstPersonMoves";

    static FirstPersonMoves _active;
    static bool _looked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { _active = null; _looked = false; }

    /// <summary>The asset in Resources, or null when there is none (the gun then only bobs).</summary>
    public static FirstPersonMoves Active
    {
        get
        {
            if (!_looked) { _active = Resources.Load<FirstPersonMoves>(ResourcePath); _looked = true; }
            return _active;
        }
    }
}
