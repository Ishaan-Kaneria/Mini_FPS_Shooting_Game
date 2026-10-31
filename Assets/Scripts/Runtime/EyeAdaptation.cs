using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The eye adapting to the dark indoors (Docs/DESIGN_NOTES.md "Real light", "The interior lighting pilot"). With the desert
/// town's bounced light baked (adaptive probe volumes, lightmaps in the pilot's rooms) a room is as dark against the street
/// as it really is -- lit only by what comes in at its door and windows, four to six stops under the street's shade -- and
/// at the street's exposure its floor read black. Eyes, and cameras, open up indoors: so does this.
///
/// It meters what is on the screen. Every few frames the graded picture is shrunk to 48 by 27 on the GPU (a pass added to
/// the camera that runs post-processing, the last of the player's stack) and read back without a stall; the room's level is
/// the middle of the view's values, the brightest fifth left out, so a door or a window stays brighter than the room it
/// lights instead of setting the exposure. Under a roof the exposure opens until that level is <see cref="roomLevel"/>, at
/// most <see cref="indoorBoost"/> stops, over <see cref="adaptInSeconds"/>, and shuts again over
/// <see cref="adaptOutSeconds"/> going back out (eyes recover from the dark slowly and from glare quickly). How far it may
/// open follows how much roof is over the eye (rays up and round the upper sky, ten times a second): none in an open street,
/// so the arena's own grade is untouched outdoors; a little under an awning.
///
/// A flat boost could not do this: +0.6 stops left a room lit by two small windows at 12 of 255, and 1.6 burned the
/// whitewashed rooms out (the lighting tour, 2026-10-24 and 2026-10-31). Only while the pipeline uses the probe volumes
/// (High, Medium): on Low the walls take the flat sky ambient indoors and out. It drives its own global volume (the scene's
/// exposure plus the adaptation), so no shared profile is ever written. Added to the town by FPSKitTownGI; the reduced copy
/// removes it.
/// </summary>
[DisallowMultipleComponent]
public class EyeAdaptation : MonoBehaviour
{
    [Header("How far the eye opens")]
    [Tooltip("Most stops of exposure added under a roof, for the darkest room. One stop doubles the light.")]
    [Range(0f, 8f)] public float indoorBoost = 6.5f;
    [Tooltip("The level a room is brought to once the eye has adapted: the middle of the view's values (its brightest fifth " +
             "left out) as a screen value, 0 black to 255 white.")]
    [Range(30f, 200f)] public float roomLevel = 100f;
    [Tooltip("Share of the view that may be white before the eye stops opening further (a sunlit patch on a wall).")]
    [Range(0f, 0.5f)] public float whiteShare = 0.06f;
    [Tooltip("Share of the upper sky blocked at which the eye starts to open (an open street sees most of it).")]
    [Range(0f, 1f)] public float openFrom = 0.45f;
    [Tooltip("Share blocked at which it may open all the way (a room: roof overhead, walls round).")]
    [Range(0f, 1f)] public float openAt = 0.95f;

    [Header("How fast")]
    [Tooltip("Seconds to adapt walking into the dark.")]
    [Range(0.05f, 5f)] public float adaptInSeconds = 1.4f;
    [Tooltip("Seconds to adapt walking back into the light.")]
    [Range(0.05f, 5f)] public float adaptOutSeconds = 0.5f;
    [Tooltip("Metres a ray goes looking for a roof before it counts as open sky.")]
    [Min(1f)] public float reach = 40f;

    const int MeterWidth = 48, MeterHeight = 27, ReadEvery = 3;

    // Zenith, then rings at 60 and 35 degrees above the horizon: the sky a roof would hide. Made on demand.
    Vector3[] _rays;
    Vector3[] Rays => _rays ??= BuildRays();

    // The picture shrunk for metering, and the pass that shrinks it. Made on demand.
    RenderTexture _meter;
    MeterPass _pass;
    MeterPass Pass => _pass ??= new MeterPass();
    List<float> _samples;
    List<float> Samples => _samples ??= new List<float>(MeterWidth * MeterHeight);

    Volume _volume;
    VolumeProfile _profile;
    ColorAdjustments _adjust;
    float _base, _offset, _cap, _metered, _nextLook;
    int _mask, _nextRead;
    bool _reading;

    /// <summary>Stops added now (for the checks and the tour).</summary>
    public float Offset => _offset;
    /// <summary>The room's level last metered, 0-255 (the middle of the view, its brightest fifth left out); -1 before any.</summary>
    public float Level { get; private set; } = -1f;

    static Vector3[] BuildRays()
    {
        var rays = new Vector3[13];
        rays[0] = Vector3.up;
        for (int i = 0; i < 6; i++)
        {
            float yaw = i * 60f;
            rays[1 + i] = Quaternion.Euler(-60f, yaw, 0f) * Vector3.forward;
            rays[7 + i] = Quaternion.Euler(-35f, yaw + 30f, 0f) * Vector3.forward;
        }
        return rays;
    }

    static bool ProbeVolumesOn => GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp && urp.lightProbeSystem == LightProbeSystem.ProbeVolumes;

    void OnEnable()
    {
        _mask = ~LayerMask.GetMask("Player", "FirstPerson", "Enemy", "Ignore Raycast", "UI", "Civilian", "PlayerBounds");
        _base = SceneExposure();
        if (_volume == null)
        {
            var go = new GameObject("EyeAdaptationVolume");
            go.transform.SetParent(transform, false);
            go.hideFlags = HideFlags.DontSave;
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 1000f;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.hideFlags = HideFlags.DontSave;
            _adjust = _profile.Add<ColorAdjustments>(false);
            _adjust.postExposure.overrideState = true;
            _volume.sharedProfile = _profile;
        }
        RenderPipelineManager.beginCameraRendering += AddMeterPass;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= AddMeterPass;
        _reading = false;
    }

    void OnDestroy()
    {
        if (_profile != null) Destroy(_profile);
        _pass?.Release();
        if (_meter != null) { _meter.Release(); Destroy(_meter); }
    }

    /// <summary>The post exposure the scene's own global volumes give, so the adaptation adds to it rather than replacing it.</summary>
    float SceneExposure()
    {
        float best = float.MinValue, value = 0f;
        foreach (var v in FindObjectsByType<Volume>())
        {
            // Not the player's hurt effect (PlayerCondition): it lowers the exposure while hurt, it is not the scene's.
            if (v == _volume || !v.isGlobal || v.sharedProfile == null || v.priority < best || v.gameObject.name == "PlayerCondition") continue;
            if (v.sharedProfile.TryGet<ColorAdjustments>(out var c) && c.postExposure.overrideState) { best = v.priority; value = c.postExposure.value; }
        }
        return value;
    }

    /// <summary>The metering pass goes on the player's camera that runs post-processing (QualityTiers.ApplyPost puts it on
    /// the stack's last), so it sees the graded picture, world and arms together.</summary>
    void AddMeterPass(ScriptableRenderContext context, Camera cam)
    {
        if (!ProbeVolumesOn || cam.cameraType != CameraType.Game) return;
        var main = Camera.main;
        if (main == null) return;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data == null || !data.renderPostProcessing) return;
        if (cam != main && !(main.GetUniversalAdditionalCameraData()?.cameraStack?.Contains(cam) ?? false)) return;
        if (_meter == null)
        {
            _meter = new RenderTexture(MeterWidth, MeterHeight, 0, GraphicsFormat.R16G16B16A16_SFloat) { name = "EyeMeter", filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            _meter.Create();
        }
        Pass.Target = _meter;
        data.scriptableRenderer.EnqueuePass(Pass);
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null || _adjust == null) return;

        if (!ProbeVolumesOn) { _cap = 0f; _metered = 0f; }
        else
        {
            if (Time.time >= _nextLook)
            {
                _nextLook = Time.time + 0.1f;
                Vector3 eye = cam.transform.position;
                int blocked = 0;
                foreach (var d in Rays)
                    if (Physics.Raycast(eye, d, reach, _mask, QueryTriggerInteraction.Ignore)) blocked++;
                float share = blocked / (float)Rays.Length;
                _cap = indoorBoost * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(openFrom, openAt, share));
            }
            if (_meter != null && _meter.IsCreated() && !_reading && Time.frameCount >= _nextRead && SystemInfo.supportsAsyncGPUReadback)
            {
                _reading = true;
                _nextRead = Time.frameCount + ReadEvery;
                AsyncGPUReadback.Request(_meter, 0, OnMetered);
            }
        }

        float target = Mathf.Min(_metered, _cap);
        float seconds = target > _offset ? adaptInSeconds : adaptOutSeconds;
        _offset = Mathf.Lerp(_offset, target, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, seconds)));
        _adjust.postExposure.value = _base + _offset;
        _volume.weight = 1f;
    }

    /// <summary>
    /// The shrunk picture back from the GPU: its values as screen values (0-255), the player's gun and arms in the lower
    /// right left out; the room's level is the mean of the middle three quarters (the darkest twentieth and the brightest
    /// fifth left out); the stops that would bring it to <see cref="roomLevel"/>, held back while more than
    /// <see cref="whiteShare"/> of the view is white.
    /// </summary>
    void OnMetered(AsyncGPUReadbackRequest request)
    {
        _reading = false;
        if (this == null || request.hasError || !request.done) return;
        var data = request.GetData<ushort>();
        var samples = Samples;
        samples.Clear();
        int white = 0;
        for (int y = 0; y < MeterHeight; y++)
            for (int x = 0; x < MeterWidth; x++)
            {
                // Row 0 is the bottom of the picture; the gun and the right hand fill its lower right.
                if (x >= MeterWidth / 2 && y < MeterHeight / 2) continue;
                int i = (y * MeterWidth + x) * 4;
                float lum = 0.2126f * Mathf.HalfToFloat(data[i]) + 0.7152f * Mathf.HalfToFloat(data[i + 1]) + 0.0722f * Mathf.HalfToFloat(data[i + 2]);
                float shown = 255f * Mathf.LinearToGammaSpace(Mathf.Clamp01(lum));
                if (shown >= 250f) white++;
                samples.Add(shown);
            }
        if (samples.Count == 0) return;
        samples.Sort();
        int from = samples.Count / 20, to = Mathf.Max(from + 1, samples.Count * 4 / 5);
        float sum = 0f;
        for (int i = from; i < to; i++) sum += samples[i];
        Level = sum / (to - from);
        // Stops between the level and the wanted one, in light (the screen values are gamma-encoded).
        float stops = Mathf.Log(Mathf.GammaToLinearSpace(roomLevel / 255f) / Mathf.GammaToLinearSpace(Mathf.Max(1f, Level) / 255f), 2f);
        if (white > samples.Count * whiteShare) stops = Mathf.Min(stops, -0.25f);
        _metered = Mathf.Clamp(_offset + stops, 0f, indoorBoost);
    }

    /// <summary>Shrinks the graded picture to the metering texture in two bilinear steps (to a tenth, then to 48 by 27).</summary>
    sealed class MeterPass : ScriptableRenderPass
    {
        public RenderTexture Target;
        RTHandle _handle;
        RenderTexture _wrapped;

        public MeterPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;
        }

        public void Release() { _handle?.Release(); _handle = null; _wrapped = null; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (Target == null || resources.isActiveTargetBackBuffer) return;
            if (_wrapped != Target) { _handle?.Release(); _handle = RTHandles.Alloc(Target); _wrapped = Target; }
            var mid = renderGraph.CreateTexture(new TextureDesc(192, 108)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat, filterMode = FilterMode.Bilinear, name = "EyeMeterHalfway"
            });
            renderGraph.AddBlitPass(resources.activeColorTexture, mid, Vector2.one, Vector2.zero, passName: "Eye meter: to a tenth");
            renderGraph.AddBlitPass(mid, renderGraph.ImportTexture(_handle), Vector2.one, Vector2.zero, passName: "Eye meter: to 48 x 27");
        }
    }
}
