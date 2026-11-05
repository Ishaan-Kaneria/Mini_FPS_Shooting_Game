using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Trades render resolution for frame time (a phone, and Medium and High on a desktop): when a frame runs long the scene is
/// drawn smaller and upscaled, and when there is headroom it climbs back. The interface is
/// drawn after the upscale, so text and buttons stay sharp throughout.
///
/// Driven by the GPU time the platform reports where it reports one, and by the frame time
/// otherwise. Steps down fast and up slowly, because a stutter is noticed and a slight
/// softening is not. The floor is higher than it could be, since below about two-thirds a
/// distant enemy stops being a readable shape. Battery saver lowers the floor, and the
/// target is whatever frame rate the player set.
///
/// Uses <see cref="ScalableBufferManager"/>, which works where the graphics API supports
/// dynamic resolution (Vulkan, Metal); elsewhere it is a no-op, and so is this.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class DynamicResolution : MonoBehaviour
{
    [Range(0.4f, 1f)] public float floor = 0.7f;
    [Range(0.4f, 1f)] public float batterySaverFloor = 0.55f;
    [Tooltip("Least seconds between changes of scale. On a desktop's larger screen a change every frame shows as pumping.")]
    [Min(0f)] public float stepSeconds;

    float _scale = 1f;
    float _smoothed, _stepAt, _stackAt;

    /// <summary>The share of each axis being drawn now.</summary>
    public float Scale => _scale;
    readonly FrameTiming[] _timings = new FrameTiming[1];

    void OnEnable() => GetComponent<Camera>().allowDynamicResolution = true;

    void Update()
    {
        if (Time.unscaledTime >= _stackAt) MarkStack();
        FrameTimingManager.CaptureFrameTimings();
        bool gpuTime = FrameTimingManager.GetLatestTimings(1, _timings) > 0 && _timings[0].gpuFrameTime > 0;
        float ms = gpuTime ? (float)_timings[0].gpuFrameTime : Time.unscaledDeltaTime * 1000f;
        _smoothed = _smoothed <= 0f ? ms : Mathf.Lerp(_smoothed, ms, 0.1f);

        float fps = Application.targetFrameRate > 0 ? Application.targetFrameRate : 60f;
        float budget = 1000f / fps;
        float min = GameSettings.BatterySaver ? batterySaverFloor : floor;

        if (Time.unscaledTime < _stepAt) return;
        _stepAt = Time.unscaledTime + stepSeconds;

        // The whole frame's time at a held rate is the budget, however light the GPU's work: judged like the GPU's own time it read as
        // over budget for ever, and the picture sank to the floor. Without a GPU reading, holding the rate is what headroom looks like.
        float down = gpuTime ? 0.95f : 1.15f, up = gpuTime ? 0.75f : 1.05f;
        if (_smoothed > budget * down) _scale = Mathf.Max(min, _scale - 0.05f);
        else if (_smoothed < budget * up) _scale = Mathf.Min(1f, _scale + 0.01f);

        ScalableBufferManager.ResizeBuffers(_scale, _scale);
    }

    /// <summary>The arms camera is made after this one and drawn into the same picture.</summary>
    void MarkStack()
    {
        _stackAt = Time.unscaledTime + 1f;
        var data = GetComponent<Camera>().GetUniversalAdditionalCameraData();
        if (data == null) return;
        foreach (var overlay in data.cameraStack)
            if (overlay != null) overlay.allowDynamicResolution = true;
    }

    void OnDisable() => ScalableBufferManager.ResizeBuffers(1f, 1f);
}
