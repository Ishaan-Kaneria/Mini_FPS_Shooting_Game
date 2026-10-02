using UnityEngine;

/// <summary>
/// Sways a hanging thing in the wind: torn tarps, loose tape, a dangling sign. A cheap sine on one axis
/// with a per-object phase so a row of tarps never moves in step.
///
/// Rotation only, applied on top of the pose the object was placed in, so nothing drifts and the object is
/// never written back into a shared asset. Cheap enough to leave on every tarp; turn it off with the
/// Low quality tier by disabling the component (the pose it was placed in is the rest pose).
/// </summary>
public class WindFlutter : MonoBehaviour
{
    [Header("Sway")]
    [Tooltip("Largest swing from the rest pose, in degrees.")]
    [Range(0f, 25f)] public float amplitude = 5f;

    [Tooltip("Swings per second.")]
    [Range(0.05f, 3f)] public float frequency = 0.35f;

    [Tooltip("Local axis it swings about.")]
    public Vector3 axis = Vector3.right;

    [Tooltip("Adds a faster, smaller shake on top so cloth reads as cloth, not a pendulum.")]
    [Range(0f, 1f)] public float flutter = 0.35f;

    Quaternion _rest;
    float _phase;
    bool _ready;

    void OnEnable()
    {
        _rest = transform.localRotation;
        // Position-derived, not random: the same tarp starts at the same angle every run.
        _phase = (transform.position.x * 0.37f + transform.position.z * 0.53f) * 6.28f;
        _ready = true;
    }

    void OnDisable()
    {
        if (_ready) transform.localRotation = _rest;
    }

    void Update()
    {
        float t = Time.time;
        float swing = Mathf.Sin(t * frequency * 6.28f + _phase)
                    + flutter * Mathf.Sin(t * frequency * 6.28f * 4.3f + _phase * 1.7f);
        transform.localRotation = _rest * Quaternion.AngleAxis(swing * amplitude / (1f + flutter), axis);
    }
}
