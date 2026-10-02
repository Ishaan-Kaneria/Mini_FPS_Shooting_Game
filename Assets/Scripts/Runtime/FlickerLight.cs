using UnityEngine;

/// <summary>
/// Makes a practical light (and/or a glowing surface) misbehave: a barrel fire that breathes, a bulb that
/// stutters and drops out, an emergency beacon that pulses.
///
/// Drives the light's intensity and the renderer's emission through a MaterialPropertyBlock, so the shared
/// material is never written. Deterministic per object (the seed comes from its position), so a bulb flickers
/// the same way every run and the lights in a row never move in step.
/// </summary>
public class FlickerLight : MonoBehaviour
{
    public enum Mode { Flicker, Pulse, Dropout }

    [Header("Behaviour")]
    public Mode mode = Mode.Flicker;

    [Tooltip("Lowest and highest share of the base brightness while lit.")]
    public Vector2 range = new Vector2(0.55f, 1.15f);

    [Tooltip("How fast it changes. Flicker: noise speed. Pulse: cycles per second.")]
    [Min(0.05f)] public float speed = 6f;

    [Tooltip("Dropout only: chance per second that the light cuts out for a moment.")]
    [Range(0f, 2f)] public float dropoutsPerSecond = 0.35f;

    [Tooltip("Dropout only: how long a cut-out lasts, in seconds (min, max).")]
    public Vector2 dropoutLength = new Vector2(0.05f, 0.4f);

    [Header("Targets (either or both)")]
    public Light target;
    public Renderer emissive;

    float _baseIntensity;
    Color _baseEmission = Color.black;
    float _seed;
    float _offUntil;
    MaterialPropertyBlock _block;

    // Created on demand, never in a field initialiser: domain reload is off in this project.
    MaterialPropertyBlock Block => _block ?? (_block = new MaterialPropertyBlock());

    void OnEnable()
    {
        if (target == null) target = GetComponent<Light>();
        if (emissive == null) emissive = GetComponent<Renderer>();
        if (target != null) _baseIntensity = target.intensity;
        if (emissive != null && emissive.sharedMaterial != null && emissive.sharedMaterial.HasProperty("_EmissionColor"))
            _baseEmission = emissive.sharedMaterial.GetColor("_EmissionColor");
        _seed = (transform.position.x * 12.9898f + transform.position.z * 78.233f) % 100f;
    }

    void OnDisable()
    {
        Apply(1f);
    }

    void Update()
    {
        float t = Time.time;
        float k;
        switch (mode)
        {
            case Mode.Pulse:
                k = Mathf.Lerp(range.x, range.y, 0.5f + 0.5f * Mathf.Sin((t + _seed) * speed * 6.2832f));
                break;
            case Mode.Dropout:
                if (t < _offUntil) { k = 0f; break; }
                if (Random.value < dropoutsPerSecond * Time.deltaTime) _offUntil = t + Random.Range(dropoutLength.x, dropoutLength.y);
                k = Mathf.Lerp(range.x, range.y, Mathf.PerlinNoise(_seed, t * speed));
                break;
            default:
                k = Mathf.Lerp(range.x, range.y, Mathf.PerlinNoise(_seed + t * speed, _seed * 0.31f));
                break;
        }
        Apply(k);
    }

    void Apply(float k)
    {
        if (target != null) target.intensity = _baseIntensity * k;
        if (emissive != null)
        {
            Block.SetColor("_EmissionColor", _baseEmission * k);
            emissive.SetPropertyBlock(Block);
        }
    }
}
