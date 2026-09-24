using UnityEngine;

/// <summary>
/// Trades render resolution for frame time on a phone: when a frame runs long the scene is
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

    float _scale = 1f;
    float _smoothed;
    readonly FrameTiming[] _timings = new FrameTiming[1];

    void OnEnable() => GetComponent<Camera>().allowDynamicResolution = true;

    void Update()
    {
        FrameTimingManager.CaptureFrameTimings();
        float ms = FrameTimingManager.GetLatestTimings(1, _timings) > 0 && _timings[0].gpuFrameTime > 0
            ? (float)_timings[0].gpuFrameTime
            : Time.unscaledDeltaTime * 1000f;
        _smoothed = _smoothed <= 0f ? ms : Mathf.Lerp(_smoothed, ms, 0.1f);

        float fps = Application.targetFrameRate > 0 ? Application.targetFrameRate : 60f;
        float budget = 1000f / fps;
        float min = GameSettings.BatterySaver ? batterySaverFloor : floor;

        if (_smoothed > budget * 0.95f) _scale = Mathf.Max(min, _scale - 0.05f);
        else if (_smoothed < budget * 0.75f) _scale = Mathf.Min(1f, _scale + 0.01f);

        ScalableBufferManager.ResizeBuffers(_scale, _scale);
    }

    void OnDisable() => ScalableBufferManager.ResizeBuffers(1f, 1f);
}
