using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// What the quality tier switches in the Night Rooftop, on top of the pipeline asset the tier selects.
///
/// The district is built for a PC on High: a lamp every 20 m (half of them real lights), two floodlights and a
/// string of bulbs on every roof, 540 k triangles, thousands of shadow casters under the moon. Most of the look is
/// emissive materials and additive light pools, which cost almost nothing, so only the real lights are thinned:
///
///   Low     no lamp lights, one roof light in three (the pools and the emissive heads stay, so the street still
///           reads as lit); shadows as short as the pipeline allows
///   Medium  a quarter of the lamps' lights, every roof light
///   High    everything
///
/// URP lights at most four additional lights per object in this project, so a thinned lamp row is nearly
/// invisible on a wall; it is the frame time that moves. The scene builder wires the lists. A tier change from the
/// settings screen is applied live, and only this scene's components are touched, never a shared asset, except
/// the shadow range, which is saved and given back on exit.
/// </summary>
public class RooftopQuality : MonoBehaviour
{
    [Header("Lights")]
    [Tooltip("The spot light on every second street lamp. Off on Low; every other one on Medium.")]
    public Light[] lampLights;

    [Tooltip("One point light over each walkable roof. Every third on Low.")]
    public Light[] roofLights;

    [Header("Shadows")]
    [Tooltip("The most distance the moon's shadows reach here, per tier (Low, Medium, High). The pipeline asset is shared by every arena " +
             "and sets 95 m with 4 cascades; this district has thousands of shadow casters, so it asks for less and gives it back on exit.")]
    public float[] shadowDistance = { 45f, 60f, 55f };

    [Tooltip("The most shadow cascades here, per tier (Low, Medium, High). Never raises what the pipeline asset already has.")]
    public int[] shadowCascades = { 2, 2, 3 };

    // What the shared pipeline asset had before this arena lowered it. Not serialized.
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

    void OnChanged(string key)
    {
        if (key == "quality" || key == "*") Apply();
    }

    /// <summary>Applies the player's tier to this scene.</summary>
    public void Apply() => Apply(QualityTiers.Current);

    public void Apply(GameSettings.Quality tier)
    {
        ApplyShadows(tier);

        bool low = tier == GameSettings.Quality.Low;
        bool medium = tier == GameSettings.Quality.Medium;

        if (lampLights != null)
            for (int i = 0; i < lampLights.Length; i++)
                if (lampLights[i] != null) lampLights[i].enabled = !low && (!medium || i % 2 == 0);

        if (roofLights != null)
            for (int i = 0; i < roofLights.Length; i++)
                if (roofLights[i] != null) roofLights[i].enabled = !low || i % 3 == 0;
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
}
