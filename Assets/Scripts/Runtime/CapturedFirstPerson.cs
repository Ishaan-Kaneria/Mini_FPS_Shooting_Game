using UnityEngine;

/// <summary>
/// The first-person version of a captured move (Docs/DESIGN_NOTES.md "Captured animation"): where each wrist and
/// foot went, frame by frame at 60 a second, measured on our rig from the performer's motion.
///
/// The hands are said in the view's own space as the chest saw them: metres from the eye, along the chest's axes
/// (+Z where the chest faces). The chest, not the head, because the camera never turns for a strike: whatever the
/// performer's head did, the view holds still and the arms move in front of it. Each hand's rotation is a frame in the
/// sense of <see cref="LimbIK.HandFrame"/> (+Z the fingers, +Y the back of the hand). The feet are said about the
/// pelvis, for the kick legs.
///
/// <see cref="ViewModelRig"/> plays it fitted to its own framing: the captured guard lands on the game's guard and the
/// captured blow on the game's reach (fighting framing, PlayerBodySettings), so the human path, timing and hand turn
/// are kept and the view's rules are not broken. Made by FPSKitMocap.Bake; nothing in the game plays one until a move
/// is approved and assigned.
/// </summary>
public class CapturedFirstPerson : ScriptableObject
{
    public const float Rate = 60f;

    [Tooltip("The clip this was made with (for reference).")]
    public AnimationClip source;

    [Tooltip("Seconds into the full clip at which this track starts (it is the strike alone: guard, blow, guard).")]
    public float fromClip;

    [Tooltip("Seconds into this track at which the blow lands (the striking hand or foot at its furthest).")]
    public float impact;

    [Tooltip("Which side strikes: 0 left hand, 1 right hand, 2 left foot, 3 right foot, -1 none (a reload, a hit reaction).")]
    public int striker = -1;

    public Vector3[] leftHand = new Vector3[0];
    public Quaternion[] leftHandTurn = new Quaternion[0];
    public Vector3[] rightHand = new Vector3[0];
    public Quaternion[] rightHandTurn = new Quaternion[0];
    public Vector3[] leftFoot = new Vector3[0];
    public Quaternion[] leftFootTurn = new Quaternion[0];
    public Vector3[] rightFoot = new Vector3[0];
    public Quaternion[] rightFootTurn = new Quaternion[0];

    /// <summary>Seconds the track lasts.</summary>
    public float Length => rightHand.Length > 1 ? (rightHand.Length - 1) / Rate : 0f;

    public int Frames => rightHand.Length;

    /// <summary>The frame the blow lands on.</summary>
    public int ImpactFrame => Mathf.Clamp(Mathf.RoundToInt(impact * Rate), 0, Mathf.Max(0, Frames - 1));

    /// <summary>A point of a track at <paramref name="time"/> seconds, smoothly between frames (Catmull-Rom).</summary>
    public static Vector3 Sample(Vector3[] track, float time)
    {
        if (track == null || track.Length == 0) return Vector3.zero;
        float f = Mathf.Clamp(time * Rate, 0f, track.Length - 1);
        int i = Mathf.FloorToInt(f);
        float t = f - i;
        Vector3 p0 = track[Mathf.Max(i - 1, 0)], p1 = track[i];
        Vector3 p2 = track[Mathf.Min(i + 1, track.Length - 1)], p3 = track[Mathf.Min(i + 2, track.Length - 1)];
        return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (t * t) + (3f * p1 - p0 - 3f * p2 + p3) * (t * t * t));
    }

    /// <summary>A turn of a track at <paramref name="time"/> seconds, slerped between frames.</summary>
    public static Quaternion Sample(Quaternion[] track, float time)
    {
        if (track == null || track.Length == 0) return Quaternion.identity;
        float f = Mathf.Clamp(time * Rate, 0f, track.Length - 1);
        int i = Mathf.FloorToInt(f);
        return Quaternion.Slerp(track[i], track[Mathf.Min(i + 1, track.Length - 1)], f - i);
    }
}
