using UnityEngine;

/// <summary>
/// The first-person arms' motion, taken from the Low Poly Shooter Pack's clips (Ishaan's pick, 2026-10-18)
/// and baked by FPSKit > Player > Bake First-Person Clips (FPSKitFirstPersonClips) into what our own arms
/// can use: not the pack's arms or gun, only how its gun and left hand move.
///
/// Each <see cref="Track"/> is sampled at the pack's 30 frames a second, as deltas from a reference pose
/// (the pack's idle, or its aimed pose for the aimed clips):
///
/// - **gun**: the rifle's turn and shift in the view's space, turned about the right hand's grip (our right
///   hand holds our gun at the grip too, so the motion carries over whatever the rifle);
/// - **hand**: the left hand's shift and turn on the rifle, from its grip on the handguard, in the rifle's
///   own frame as it lies in the reference pose.
///
/// <see cref="ViewModelRig"/> plays them on our gun and the IK keeps both hands on it; <see cref="Weapon"/>
/// still owns the timing (the reload, the draw, the shots) and its sounds land on each track's beats.
/// Loaded from <c>Resources/FirstPersonClips</c>; with none, the procedural poses are used as before.
/// </summary>
public class FirstPersonClips : ScriptableObject
{
    [System.Serializable]
    public class Track
    {
        public string source;
        [Min(0.01f)] public float length = 1f;
        public bool loop;
        [Tooltip("Samples a second.")]
        public float rate = 30f;
        public Vector3[] gunPos = new Vector3[0];
        public Quaternion[] gunRot = new Quaternion[0];
        public Vector3[] handPos = new Vector3[0];
        public Quaternion[] handRot = new Quaternion[0];

        [Header("Locomotion")]
        [Tooltip("Seconds of one stride (two steps) in the clip.")]
        public float strideSeconds;
        [Tooltip("Seconds into the clip of a footfall (the gun lowest, a moment after it).")]
        public float footfallAt;
        [Tooltip("The clip's average gun pose: the carry, as against the swing about it.")]
        public Vector3 meanPos;
        public Quaternion meanRot = Quaternion.identity;

        [Header("Beats, as shares of the clip")]
        public float magOut = -1f, magIn = -1f, bolt = -1f;

        public bool Valid => gunPos != null && gunPos.Length > 1;

        /// <summary>
        /// The gun and left hand at <paramref name="time"/> seconds (wrapped when the track loops, held at its ends
        /// when not). Between samples on a Catmull-Rom curve, not a straight line: straight lines between 30 Hz
        /// samples turn the motion in a corner thirty times a second, which a 144 Hz display shows as a shiver.
        /// </summary>
        public void Sample(float time, out Vector3 gp, out Quaternion gr, out Vector3 hp, out Quaternion hr)
        {
            gp = Vector3.zero; gr = Quaternion.identity; hp = Vector3.zero; hr = Quaternion.identity;
            if (!Valid) return;
            int n = gunPos.Length;
            float f = time * rate;
            if (loop) f = Mathf.Repeat(f, n - 1);
            else f = Mathf.Clamp(f, 0f, n - 1);
            int i = Mathf.Min((int)f, n - 2);
            float k = f - i;
            int Index(int j) => loop ? (int)Mathf.Repeat(j, n - 1) : Mathf.Clamp(j, 0, n - 1);
            int a = Index(i - 1), b = i, c = i + 1, d = Index(i + 2);
            gp = Curve(gunPos[a], gunPos[b], gunPos[c], gunPos[d], k);
            gr = Curve(gunRot[a], gunRot[b], gunRot[c], gunRot[d], k);
            if (handPos != null && handPos.Length == n)
            {
                hp = Curve(handPos[a], handPos[b], handPos[c], handPos[d], k);
                hr = Curve(handRot[a], handRot[b], handRot[c], handRot[d], k);
            }
        }

        static Vector3 Curve(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        /// <summary>The same curve through the four turns' components (each brought to the same side as the second), normalised.</summary>
        static Quaternion Curve(Quaternion q0, Quaternion q1, Quaternion q2, Quaternion q3, float t)
        {
            Vector4 V(Quaternion q) => new Vector4(q.x, q.y, q.z, q.w);
            Vector4 b = V(q1);
            Vector4 Side(Quaternion q) { var v = V(q); return Vector4.Dot(v, b) < 0f ? -v : v; }
            Vector4 a = Side(q0), c = Side(q2), d = Side(q3);
            float t2 = t * t, t3 = t2 * t;
            Vector4 r = 0.5f * (2f * b + (c - a) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (3f * b - a - 3f * c + d) * t3);
            float m = r.magnitude;
            return m < 1e-6f ? q1 : new Quaternion(r.x / m, r.y / m, r.z / m, r.w / m);
        }
    }

    [Header("Locomotion (played on the footsteps)")]
    public Track idle = new Track();
    public Track walk = new Track();
    public Track aimWalk = new Track();
    [Tooltip("Running with the rifle at the ready.")]
    public Track run = new Track();
    [Tooltip("Sprinting, the rifle carried.")]
    public Track sprint = new Track();

    [Header("Aiming (the arc between hip and sights)")]
    public Track aimIn = new Track();
    public Track aimOut = new Track();

    [Header("Firing")]
    public Track fire = new Track();
    public Track aimFire = new Track();

    [Header("Reloading")]
    public Track reload = new Track();
    public Track reloadEmpty = new Track();

    [Header("Weapon switch")]
    public Track holster = new Track();
    public Track unholster = new Track();
    public Track inspect = new Track();

    [Header("How much of each")]
    [Tooltip("Share of the clips' firing motion on top of the gun's own kick.")]
    [Range(0f, 1.5f)] public float fireWeight = 0.7f;
    [Tooltip("Share of the gun's turn and lift through the reload. The pack's raises the muzzle some 60 degrees, a game's " +
             "flourish; at 0.4 the rifle is rolled to show the magazine well and lifted a little. The left hand's path is unscaled. " +
             "(The pack's sprint carry is not used at all: it swings the rifle flat across the body. Weapon's own carry stays.)")]
    [Range(0f, 1f)] public float reloadTurn = 0.4f;
    [Tooltip("Share of the stride's swing, at the gait's own speed.")]
    [Range(0f, 1.5f)] public float strideWeight = 1f;
    [Tooltip("Share of the idle's breathing sway.")]
    [Range(0f, 1.5f)] public float idleWeight = 1f;

    // ======================================================================

    public const string ResourcePath = "FirstPersonClips";
    static FirstPersonClips _active;
    static bool _looked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { _active = null; _looked = false; }

    /// <summary>The baked clips, or null when there are none (the procedural poses are used).</summary>
    public static FirstPersonClips Active
    {
        get
        {
            if (!_looked) { _active = Resources.Load<FirstPersonClips>(ResourcePath); _looked = true; }
            return _active;
        }
    }
}
