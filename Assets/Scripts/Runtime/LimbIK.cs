using UnityEngine;

/// <summary>
/// The arithmetic for posing a rigged limb by hand: a two-bone reach (shoulder, elbow,
/// wrist or hip, knee, ankle) and turning a hand to a frame described in words (which way
/// the fingers point, which way the back of the hand faces). Shared by the first-person
/// arms and legs; nothing here knows what a gun or a kick is.
///
/// Works on any rig, whatever the bone axes: a hand's own axes are measured from where
/// its fingers and thumb are (<see cref="HandFrame"/>), never assumed.
/// </summary>
public static class LimbIK
{
    /// <summary>
    /// Bends <paramref name="upper"/> and <paramref name="lower"/> so the end of the chain
    /// reaches <paramref name="target"/>, the middle joint pointing towards
    /// <paramref name="hint"/>. A target out of reach is reached for in a straight line.
    /// </summary>
    public static void Solve(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hint)
    {
        Vector3 a = upper.position;
        float ab = Vector3.Distance(a, lower.position);
        float bc = Vector3.Distance(lower.position, end.position);
        if (ab < 1e-4f || bc < 1e-4f) return;

        Vector3 toTarget = target - a;
        float dist = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(ab - bc) + 1e-3f, ab + bc - 1e-3f);
        Vector3 dir = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : upper.forward;

        // The bend plane holds the reach and the hint; the elbow goes to the hint's side.
        Vector3 toHint = hint - a;
        Vector3 bend = Vector3.ProjectOnPlane(toHint, dir);
        if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
        if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.forward, dir);
        bend.Normalize();

        float cosA = Mathf.Clamp((ab * ab + dist * dist - bc * bc) / (2f * ab * dist), -1f, 1f);
        float sinA = Mathf.Sqrt(1f - cosA * cosA);
        Vector3 elbow = a + dir * (cosA * ab) + bend * (sinA * ab);

        upper.rotation = Quaternion.FromToRotation(lower.position - a, elbow - a) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(end.position - lower.position, a + dir * dist - lower.position) * lower.rotation;
    }

    /// <summary>
    /// A hand's own axes, measured once from the bind pose: which local direction its
    /// fingers run along, and which way the back of the hand faces.
    /// </summary>
    public struct HandFrame
    {
        public Quaternion toLocal;
        public bool valid;

        /// <summary>
        /// Measured from the middle finger's base (the way the fingers run) and the index
        /// and little fingers' bases (across the knuckles, towards the thumb side). With the
        /// fingers ahead and the knuckles running towards the thumb, the back of a right hand
        /// is on one side of that pair and the back of a left hand on the other.
        /// </summary>
        public static HandFrame Measure(Transform hand, Transform middle, Transform index, Transform little, bool right)
        {
            var frame = new HandFrame();
            if (hand == null || middle == null || index == null || little == null) return frame;

            Vector3 fingers = hand.InverseTransformDirection(middle.position - hand.position);
            Vector3 across = hand.InverseTransformDirection(index.position - little.position);

            Vector3 back = Vector3.Cross(fingers, across);
            if (right) back = -back;

            if (fingers.sqrMagnitude < 1e-8f || back.sqrMagnitude < 1e-8f) return frame;

            frame.toLocal = Quaternion.Inverse(Quaternion.LookRotation(fingers.normalized, back.normalized));
            frame.valid = true;
            return frame;
        }

        /// <summary>The world rotation that points this hand's fingers and back of hand as asked.</summary>
        public Quaternion World(Vector3 fingers, Vector3 back)
            => Quaternion.LookRotation(fingers, back) * toLocal;
    }
}
