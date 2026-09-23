using UnityEngine;

/// <summary>
/// Rocks a part back and forth about one axis: a pumpjack's walking beam nodding over its
/// well. Paired with a <see cref="MachineSpin"/> on the crank at the same period, the two
/// read as one machine.
///
/// The angle is a pure function of the clock and the builder-written rest rotation, for the
/// reasons <see cref="MachineTravel"/> gives. Scenery: give it to parts with no collider.
/// </summary>
[DisallowMultipleComponent]
public class MachineRock : MonoBehaviour
{
    [Tooltip("The part's rotation at rest, in its parent's space. Written by the builder.")]
    public Quaternion rest = Quaternion.identity;

    [Tooltip("The axis it rocks about, in its own space.")]
    public Vector3 axis = Vector3.right;

    [Tooltip("Degrees either side of rest.")]
    public float amplitude = 18f;

    [Tooltip("Seconds for one full nod, down and back up.")]
    public float period = 4.5f;

    [Tooltip("Seconds into the cycle it starts at, so a field of them is not in step.")]
    public float phase;

    void Update()
    {
        if (period <= 0.01f) return;

        float angle = amplitude * Mathf.Sin((Time.time + phase) * Mathf.PI * 2f / period);
        transform.localRotation = rest * Quaternion.AngleAxis(angle, axis);
    }
}
