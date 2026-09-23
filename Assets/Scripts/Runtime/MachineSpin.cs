using UnityEngine;

/// <summary>
/// Turns a part steadily about one of its own axes: a roof fan, a pulley, a ventilator
/// cowl. The simplest possible sign that a building is running.
///
/// The angle comes from the clock and the resting rotation is written by the builder, for
/// the reasons <see cref="MachineTravel"/> gives -- nothing is accumulated, so nothing can
/// go stale or drift. Scenery: give it to parts with no collider.
/// </summary>
[DisallowMultipleComponent]
public class MachineSpin : MonoBehaviour
{
    [Tooltip("The part's rotation at rest, in its parent's space. Written by the builder.")]
    public Quaternion rest = Quaternion.identity;

    [Tooltip("The axis it turns about, in its own space.")]
    public Vector3 axis = Vector3.up;

    [Tooltip("Degrees per second. An extract fan idles at a few turns a second.")]
    public float degreesPerSecond = 240f;

    [Tooltip("Degrees it starts at, so a row of fans is not turning in step.")]
    public float phase;

    void Update()
    {
        transform.localRotation = rest * Quaternion.AngleAxis(phase + Time.time * degreesPerSecond, axis);
    }
}
