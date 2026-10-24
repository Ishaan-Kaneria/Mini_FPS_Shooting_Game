using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The eye adapting to the dark indoors (Docs/DESIGN_NOTES.md "Real light"). With the desert town's bounced light baked
/// (adaptive probe volumes, FPSKitTownGI) a room is as dark against the street as it really is -- lit only by what comes in
/// at its door and windows -- and at the street's exposure its floor read black. Eyes, and cameras, open up indoors: so
/// does this. Ten times a second it looks how much roof is over the eye (rays up and round the upper sky); under a roof
/// the exposure rises over <see cref="adaptInSeconds"/>, back out under the sky it falls over <see cref="adaptOutSeconds"/>
/// (eyes recover from the dark slowly and from glare quickly). Under an awning or in a covered lane it rises a little.
///
/// Only while the pipeline uses the probe volumes (High, Medium): on Low the walls take the flat sky ambient indoors and out,
/// and rooms are as bright as the street already. It drives its own global volume (the scene's exposure plus the
/// adaptation), so no shared profile is ever written. Added to the town by FPSKitTownGI; the reduced copy removes it.
/// </summary>
[DisallowMultipleComponent]
public class EyeAdaptation : MonoBehaviour
{
    [Header("How far the eye opens")]
    [Tooltip("Stops of exposure added fully under a roof (a room, a covered market). One stop doubles the light.")]
    [Range(0f, 3f)] public float indoorBoost = 0.6f;   // 1.6, then 0.85, burned whitewashed rooms out (lighting tour)
    [Tooltip("Share of the upper sky blocked at which the eye starts to open (an open street sees most of it).")]
    [Range(0f, 1f)] public float openFrom = 0.45f;
    [Tooltip("Share blocked at which it is fully open (a room: roof overhead, walls round).")]
    [Range(0f, 1f)] public float openAt = 0.95f;

    [Header("How fast")]
    [Tooltip("Seconds to adapt walking into the dark.")]
    [Range(0.05f, 5f)] public float adaptInSeconds = 1.4f;
    [Tooltip("Seconds to adapt walking back into the light.")]
    [Range(0.05f, 5f)] public float adaptOutSeconds = 0.5f;
    [Tooltip("Metres a ray goes looking for a roof before it counts as open sky.")]
    [Min(1f)] public float reach = 40f;

    // Zenith, then rings at 60 and 35 degrees above the horizon: the sky a roof would hide. Made on demand.
    Vector3[] _rays;
    Vector3[] Rays => _rays ??= BuildRays();

    Volume _volume;
    VolumeProfile _profile;
    ColorAdjustments _adjust;
    float _base, _offset, _target, _nextLook;
    int _mask;

    /// <summary>Stops added now (for the checks and the tour).</summary>
    public float Offset => _offset;

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
    }

    void OnDestroy()
    {
        if (_profile != null) Destroy(_profile);
    }

    /// <summary>The post exposure the scene's own global volumes give, so the adaptation adds to it rather than replacing it.</summary>
    float SceneExposure()
    {
        float best = float.MinValue, value = 0f;
        foreach (var v in FindObjectsByType<Volume>())
        {
            if (v == _volume || !v.isGlobal || v.sharedProfile == null || v.priority < best) continue;
            if (v.sharedProfile.TryGet<ColorAdjustments>(out var c) && c.postExposure.overrideState) { best = v.priority; value = c.postExposure.value; }
        }
        return value;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null || _adjust == null) return;

        if (!ProbeVolumesOn) _target = 0f;
        else if (Time.time >= _nextLook)
        {
            _nextLook = Time.time + 0.1f;
            Vector3 eye = cam.transform.position;
            int blocked = 0;
            foreach (var d in Rays)
                if (Physics.Raycast(eye, d, reach, _mask, QueryTriggerInteraction.Ignore)) blocked++;
            float share = blocked / (float)Rays.Length;
            _target = indoorBoost * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(openFrom, openAt, share));
        }

        float seconds = _target > _offset ? adaptInSeconds : adaptOutSeconds;
        _offset = Mathf.Lerp(_offset, _target, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, seconds)));
        _adjust.postExposure.value = _base + _offset;
        _volume.weight = 1f;
    }
}
