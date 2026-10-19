#if UNITY_EDITOR
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The tuning of one captured clip (Docs/DESIGN_NOTES.md "Captured animation"): made with defaults the first time
    /// a take is baked, next to its clip, and read on every bake after, by both halves of the pipeline. The Python
    /// side (Tools/mocap) reads the tracking and smoothing numbers straight out of this file, so its fields stay flat:
    /// plain numbers, strings and one clip reference, no nested structs. Change a number, then re-bake:
    ///
    ///   Tools/mocap/mocap.sh process player_punch_01
    ///
    /// The field names under "Tracking" and "Smoothing" are the same as Tools/mocap/mocap/solve.py's DEFAULTS.
    /// </summary>
    public class CapturedClipSettings : ScriptableObject
    {
        [Header("Source (filled in by the pipeline)")]
        [Tooltip("The video this clip came from (kept outside the project, in ~/MocapVideos).")]
        public string video = "";
        [Tooltip("Player or Enemy.")] public string who = "";
        [Tooltip("The move, from the video's name (player_punch_01 is Punch).")] public string move = "";
        [Tooltip("The take number from the video's name.")] public int take;
        [Tooltip("The calibration clip used to scale it.")] public string calibration = "";

        [Header("Playback")]
        [Tooltip("1 plays the move at the speed it was acted; 1.2 is a fifth faster.")]
        [Range(0.25f, 3f)] public float speed = 1f;
        [Tooltip("How much of the acted motion is kept: 1 all of it, 0.8 a fifth calmer, 1.2 a fifth bigger. Measured from the take's own neutral pose.")]
        [Range(0f, 1.5f)] public float strength = 1f;

        [Header("Trim (seconds into the video; both 0 = found from the motion)")]
        public float trimStart;
        public float trimEnd;
        [Tooltip("Seconds over which the clip eases out of its first pose and into its last, so it starts and ends still.")]
        [Range(0f, 0.6f)] public float blendIn = 0.12f;
        [Range(0f, 0.6f)] public float blendOut = 0.18f;

        [Header("Kind")]
        [Tooltip("A cycle (walk, limp, crawl): the end is joined onto the start and the clip loops.")]
        public bool loop;
        [Tooltip("Also make a left-right mirrored copy (a limp on the other leg, a hit from the other side).")]
        public bool mirror;
        [Tooltip("A move made holding the rifle (a broom stands in for it): the hands are kept on its grip and handguard.")]
        public bool weapon;
        [Tooltip("Lock planted feet where they stand (no sliding).")]
        public bool lockFeet = true;
        [Tooltip("Hand shape where the hand model could not see the hand: 0 relaxed, 1 fist, 2 rifle grip.")]
        [Range(0, 2)] public int handPose;
        [Tooltip("Also make the first-person arms and legs version (player moves).")]
        public bool firstPerson = true;

        [Header("Tracking")]
        [Tooltip("A tracked point the tracker is less sure of than this is thrown out and filled from its neighbours.")]
        [Range(0f, 1f)] public float minVisibility = 0.35f;
        [Tooltip("Metres a point may jump off its neighbours' path in one frame before it counts as a tracking error.")]
        [Range(0.05f, 0.5f)] public float maxJump = 0.18f;
        [Tooltip("The phone lens, 35 mm equivalent: the main camera at 1x is about 24-28.")]
        [Range(12f, 80f)] public float focal35 = 26f;
        [Tooltip("1: take the phone as level and its up as the world's up. 0 (usual): up from the performer standing straight at the take's start and end, refined by the planted feet.")]
        [Range(0, 1)] public int levelPhone;

        [Header("Smoothing (One Euro: cutoff in Hz at rest, beta how fast it opens up with speed)")]
        public float torsoCutoff = 1.2f;
        public float torsoBeta = 0.4f;
        public float limbCutoff = 2f;
        public float limbBeta = 0.5f;
        [Tooltip("Hands and feet: the lightest, so a punch or a kick keeps its snap.")]
        public float endCutoff = 3f;
        public float endBeta = 0.5f;
        public float headCutoff = 1.5f;
        public float headBeta = 0.5f;
        [Tooltip("The body's distance from the phone, the noisiest number: smoothed hardest.")]
        public float depthCutoff = 0.6f;

        [Header("Feet")]
        [Tooltip("Metres above the ground a foot may be and count as planted.")]
        public float contactHeight = 0.07f;
        [Tooltip("Metres a second a planted foot may drift and still count as planted.")]
        public float contactSpeed = 0.45f;

        [Header("Preview")]
        [Tooltip("Our current animation for the same move, shown beside the captured one.")]
        public AnimationClip currentClip;
    }
}
#endif
