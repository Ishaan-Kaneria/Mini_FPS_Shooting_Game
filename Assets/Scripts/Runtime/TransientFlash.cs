using UnityEngine;

/// <summary>
/// Shrinks a spawned flash out of sight over a few frames, then switches it off.
///
/// Both of the places that spawn one already destroy it on a timer -- Weapon after a
/// second, ImpactLibrary after its entry's lifetime -- but those timers exist to clean
/// up, not to time the effect. A muzzle flash that stays at full size for a whole
/// second reads as a lamp bolted to the barrel. This is what makes it a flash.
///
/// Scale rather than alpha on purpose: fading needs a transparent material, and a
/// transparent muzzle flash sorts badly against the weapon model it is sitting inside.
/// </summary>
[DisallowMultipleComponent]
public class TransientFlash : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Seconds from full size to gone. Shorter than you would guess: a real " +
             "muzzle flash is a couple of frames.")]
    public float lifetime = 0.05f;

    [Header("Shape")]
    public float startScale = 1f;
    public float endScale = 0.15f;

    [Tooltip("Spins the flash around its own forward axis each time it appears, so a " +
             "held trigger does not stamp the identical shape 10 times a second.")]
    public bool randomRoll = true;

    float _age;

    // OnEnable, not Start: these are pooled or re-activated in some setups, and a flash
    // that only resets on the first spawn is a flash that never plays twice.
    void OnEnable()
    {
        _age = 0f;
        transform.localScale = Vector3.one * startScale;

        if (randomRoll)
            transform.localRotation = Quaternion.Euler(0f, 0f, Random.value * 360f);
    }

    void Update()
    {
        _age += Time.deltaTime;

        float t = lifetime <= 0f ? 1f : Mathf.Clamp01(_age / lifetime);
        transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, t);

        // Switched off rather than destroyed: whoever spawned this owns its lifetime,
        // and destroying it here would race their own Destroy call.
        if (t >= 1f) gameObject.SetActive(false);
    }
}
