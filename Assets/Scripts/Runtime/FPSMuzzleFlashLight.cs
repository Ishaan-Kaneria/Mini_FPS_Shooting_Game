using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The light of a muzzle flash: a hot amber point light switched on for one shot, a twentieth of a second at most,
/// bright at once and falling away, so walls and pavement round the gun jump into light and back.
///
/// Two ways to fire it. The player's gun calls <see cref="Flash"/> from <see cref="Weapon"/> (its light, on the
/// muzzle point, casts shadows). A muzzle-flash prefab (the enemies' rifles) has <see cref="flashOnEnable"/> set and
/// flashes each time it is switched on, which is how EnemyAI and Weapon replay the one instance per shot; those cast
/// no shadows -- a point light's shadow renders the scene six times, and a squad firing at once would be dozens.
///
/// Cheap when idle: fired by script, the component switches itself off between shots and costs nothing until the next;
/// on a replayed prefab it stays enabled (a disabled component gets no OnEnable when its object is switched back on)
/// and returns at once each frame. It allocates nothing. The light's shadow resolution is the pipeline's lowest tier, and point-light shadows only exist
/// on the High tier (FPSKitQualityTiers); elsewhere the flash lights without them.
/// </summary>
[RequireComponent(typeof(Light))]
[DisallowMultipleComponent]
public class FPSMuzzleFlashLight : MonoBehaviour
{
    [Header("Flash")]
    [Tooltip("The light's brightness at the moment of the shot; it falls to nothing over the flash.")]
    public float intensity = 10f;
    [Tooltip("How far the flash reaches, in metres.")]
    public float range = 11f;
    [Tooltip("Burning powder: a warm amber, a little orange.")]
    public Color color = new Color(1f, 0.70f, 0.40f);
    [Tooltip("How long one flash lasts, in seconds: each shot picks a length between these.")]
    public Vector2 duration = new Vector2(0.04f, 0.05f);
    [Tooltip("Each shot's brightness and reach vary by this share either way, so a burst does not strobe evenly.")]
    [Range(0f, 0.5f)] public float jitter = 0.15f;

    [Header("Behaviour")]
    [Tooltip("Whether the flash casts shadows. The player's gun only: a point light's shadow draws the scene six times.")]
    public bool castShadows;
    [Tooltip("Flash whenever the object is switched on: for a muzzle-flash prefab replayed once per shot.")]
    public bool flashOnEnable = true;

    Light _light;
    float _start, _end, _peak;
    bool _lit, _inFlash;

    /// <summary>The light, set up on first use (not in Awake: a prefab instance can be enabled before Awake runs elsewhere).</summary>
    Light FlashLight
    {
        get
        {
            if (_light != null) return _light;
            _light = GetComponent<Light>();
            Configure(_light);
            return _light;
        }
    }

    /// <summary>Writes the flash's settings onto <paramref name="l"/>; the builder calls it too, so the saved light matches.</summary>
    public void Configure(Light l)
    {
        if (l == null) return;
        l.type = LightType.Point;
        l.color = color;
        l.range = range;
        l.intensity = intensity;
        l.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
        // Clear of the barrel and the hands, so the gun does not shadow its own flash onto the world.
        l.shadowNearPlane = 0.25f;
        l.renderMode = castShadows ? LightRenderMode.ForcePixel : LightRenderMode.Auto;
        l.enabled = false;
        var data = l.GetComponent<UniversalAdditionalLightData>();
        if (data == null && castShadows) data = l.gameObject.AddComponent<UniversalAdditionalLightData>();
        if (data != null)
        {
            data.usePipelineSettings = true;
            // URP lets this be set only in play; the builder writes the saved value (FPSKitSceneBuilder.BuildPlayer).
            if (Application.isPlaying)
                data.additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
        }
    }

    void OnEnable()
    {
        if (_inFlash) return;
        if (flashOnEnable) Flash();
        else if (!_lit) enabled = false;    // nothing to animate
    }

    void OnDisable()
    {
        _lit = false;
        if (_light != null) _light.enabled = false;
    }

    /// <summary>One shot's flash, from full brightness. Safe to call every frame of a burst.</summary>
    public void Flash()
    {
        var l = FlashLight;
        float j = 1f + Random.Range(-jitter, jitter);
        _peak = intensity * j;
        l.range = range * (1f + Random.Range(-jitter, jitter) * 0.5f);
        l.intensity = _peak;
        l.enabled = true;
        _start = Time.time;
        _end = _start + Random.Range(duration.x, duration.y);
        _lit = true;
        _inFlash = true;
        enabled = true;     // tick until it is out (OnEnable sees _inFlash and does not flash again)
        _inFlash = false;
    }

    void LateUpdate()
    {
        if (!_lit) { if (!flashOnEnable) enabled = false; return; }
        float t = (Time.time - _start) / Mathf.Max(0.001f, _end - _start);
        if (t >= 1f) { Out(); return; }
        // Full at the shot, falling away fast at the end: the flash is violent, not a fade.
        _light.intensity = _peak * (1f - t * t * t);
    }

    /// <summary>The flash is over: the light out, and (fired by script) the component off until the next shot.</summary>
    void Out()
    {
        _lit = false;
        if (_light != null) _light.enabled = false;
        if (!flashOnEnable) enabled = false;
    }
}
