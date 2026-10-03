using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// What the quality tier switches in the Abandoned Fairground, on top of the pipeline asset the tier selects.
///
/// The fairground is built for a PC on High: 27 realtime lights, rain, mist, light shafts, an animated
/// two-layer water surface that reads the depth texture. Handhelds and the browser get Low, and that is
/// where most of this goes:
///
///   Low     no rain, a quarter of the mist, no light shafts, only the lights that make a pool of light
///           somewhere to go (barrels, work lights, the lit booth); the water drops its depth fade and
///           its second normal map (the shader's own _FG_LOW path)
///   Medium  rain at half density, half the mist, every light
///   High    everything
///
/// The scene builder wires the lists; nothing here looks anything up by name. A tier change from the
/// settings screen is applied live. It only ever changes this scene's components and per-renderer material
/// instances, never a shared asset.
/// </summary>
public class FairgroundQuality : MonoBehaviour
{
    [Header("Weather")]
    [Tooltip("The rain particle object. Off on Low; half density on Medium.")]
    public GameObject rain;

    [Tooltip("Low mist cards over the water. A quarter kept on Low, half on Medium.")]
    public Renderer[] mistCards;

    [Tooltip("Additive light cones. Off on Low.")]
    public Renderer[] lightShafts;

    [Header("Lights")]
    [Tooltip("Lights that add atmosphere but are not what marks a place: string-light bulbs and camp fires. Off on Low.")]
    public Light[] minorLights;

    [Header("Water")]
    [Tooltip("The flood surface. On Low it switches to the cheap path of its shader.")]
    public Renderer[] water;

    [Header("Shadows")]
    [Tooltip("The most distance the sun's shadows reach here, per tier (Low, Medium, High). The pipeline asset is shared by every arena " +
             "and sets 95 m with 4 cascades; this arena has thousands of shadow casters, so it asks for less and gives it back on exit.")]
    public float[] shadowDistance = { 45f, 60f, 50f };

    [Tooltip("The most shadow cascades here, per tier (Low, Medium, High). Never raises what the pipeline asset already has.")]
    public int[] shadowCascades = { 2, 2, 3 };

    ParticleSystem _rainSystem;
    float _rainRate = -1f;

    // What the shared pipeline asset had before this arena lowered it. Not serialized: a domain reload with
    // play mode on would otherwise carry a stale pair into the next arena.
    UniversalRenderPipelineAsset _urp;
    float _savedDistance;
    int _savedCascades;
    bool _shadowsSaved;

    void OnEnable()
    {
        GameSettings.Changed += OnChanged;
        Apply();
    }

    void OnDisable()
    {
        GameSettings.Changed -= OnChanged;
        RestoreShadows();
    }

    void ApplyShadows(GameSettings.Quality tier)
    {
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp == null) return;

        // The asset can change under a tier switch (each tier has its own), so give the old one back first.
        if (_shadowsSaved && urp != _urp) RestoreShadows();
        if (!_shadowsSaved)
        {
            _urp = urp;
            _savedDistance = urp.shadowDistance;
            _savedCascades = urp.shadowCascadeCount;
            _shadowsSaved = true;
        }

        int t = (int)tier;
        if (shadowDistance != null && t < shadowDistance.Length)
            urp.shadowDistance = Mathf.Min(_savedDistance, shadowDistance[t]);
        if (shadowCascades != null && t < shadowCascades.Length)
            urp.shadowCascadeCount = Mathf.Min(_savedCascades, shadowCascades[t]);
    }

    void RestoreShadows()
    {
        if (!_shadowsSaved) return;
        if (_urp != null)
        {
            _urp.shadowDistance = _savedDistance;
            _urp.shadowCascadeCount = _savedCascades;
        }
        _shadowsSaved = false;
        _urp = null;
    }

    void OnChanged(string key)
    {
        if (key == "quality" || key == "*") Apply();
    }

    /// <summary>Applies the player's tier to this scene.</summary>
    public void Apply() => Apply(QualityTiers.Current);

    public void Apply(GameSettings.Quality tier)
    {
        bool low = tier == GameSettings.Quality.Low;
        bool high = tier == GameSettings.Quality.High;

        ApplyShadows(tier);

        if (rain != null)
        {
            rain.SetActive(!low);
            if (_rainSystem == null) _rainSystem = rain.GetComponent<ParticleSystem>();
            if (_rainSystem != null)
            {
                var em = _rainSystem.emission;
                if (_rainRate < 0f) _rainRate = em.rateOverTimeMultiplier;
                em.rateOverTimeMultiplier = high ? _rainRate : _rainRate * 0.5f;
            }
        }

        if (mistCards != null)
            for (int i = 0; i < mistCards.Length; i++)
                if (mistCards[i] != null)
                    mistCards[i].enabled = high || (tier == GameSettings.Quality.Medium ? i % 2 == 0 : i % 4 == 0);

        if (lightShafts != null)
            foreach (var r in lightShafts)
                if (r != null) r.enabled = !low;

        if (minorLights != null)
            foreach (var l in minorLights)
                if (l != null) l.enabled = !low;

        if (water != null)
            foreach (var r in water)
            {
                if (r == null) continue;
                // .material makes this renderer its own copy once; the shared asset is never touched.
                var m = r.material;
                if (low) { m.EnableKeyword("_FG_LOW"); m.DisableKeyword("_DEPTH_FADE"); }
                else { m.DisableKeyword("_FG_LOW"); m.EnableKeyword("_DEPTH_FADE"); }
            }
    }
}
