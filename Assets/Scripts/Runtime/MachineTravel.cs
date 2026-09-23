using UnityEngine;

/// <summary>
/// Runs a piece of plant back and forth: a crane trolley along its girder, a shuttle on a
/// rail. Eases in and out at each end and waits there, which is what separates a machine
/// doing a job from a thing sliding about.
///
/// <b>Worked out from the clock, not stepped.</b> The position is a pure function of
/// <c>Time.time</c>, the start point and the phase, so there is no velocity or direction
/// held anywhere: nothing to go stale when domain reload is off, nothing to lose when a
/// script is recompiled mid-play, and two cranes built with different phases never drift
/// into step with each other.
///
/// Scenery only. It has no collider on it by design -- a moving collider shoves the player
/// and can wedge an enemy -- so give it to parts that travel above head height.
/// </summary>
[DisallowMultipleComponent]
public class MachineTravel : MonoBehaviour
{
    [Tooltip("Where the part sits at the near end of its run, in its parent's space. " +
             "Written by the builder, so it is the same after a reload as before one.")]
    public Vector3 origin;

    [Tooltip("From the near end to the far end, in the parent's space.")]
    public Vector3 travel = new Vector3(10f, 0f, 0f);

    [Tooltip("Metres per second at full speed. A gantry trolley is a walking pace.")]
    public float speed = 1.2f;

    [Tooltip("Seconds it waits at each end -- the time a real one spends picking up or " +
             "setting down.")]
    public float pause = 3f;

    [Tooltip("Seconds into the cycle it starts at, so neighbours do not move in step.")]
    public float phase;

    void Update()
    {
        float length = travel.magnitude;
        if (length < 0.01f || speed <= 0.01f) return;

        float move = length / speed;
        float cycle = (move + pause) * 2f;
        float t = Mathf.Repeat(Time.time + phase, cycle);

        // Out, wait, back, wait.
        float along;
        if (t < move) along = t / move;
        else if (t < move + pause) along = 1f;
        else if (t < move * 2f + pause) along = 1f - (t - move - pause) / move;
        else along = 0f;

        along = along * along * (3f - 2f * along);
        transform.localPosition = origin + travel * along;
    }
}
