using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Puts the first-person arms and gun in the shade when the player stands in it.
///
/// The arms and gun are drawn by their own stacked camera (ViewModelRig), which sees only the FirstPerson layer, so
/// the shadow it renders holds the gun's own parts and nothing of the town: in the shade of a building the gun stayed in
/// full sun, the brightest thing on screen. This asks the world instead: a few rays from the gun towards the sun,
/// several times a second; the share that is blocked is how much of the gun is in shade. While the arms camera renders,
/// the sun is dimmed towards <see cref="shadedSunShare"/> of itself by that share, and put back for every other camera.
/// The change eases in over a few hundredths of a second, as stepping into a shadow does.
///
/// The share in full shade was the sun's shadow strength (0.8, leaving 20%: 0.33 of the desert's 1.65), what a shadow
/// takes off a lit wall; the gun is close and its detail is what the player reads, so it keeps 30% (0.50 of 1.65).
///
/// Under a roof the sun is off them altogether and the light stands in for the room's own (<see cref="roomLight"/>).
///
/// Sits on the arms camera (ViewModelRig adds it). Costs three raycasts every 0.05 s.
/// </summary>
[DisallowMultipleComponent]
public class FirstPersonShade : MonoBehaviour
{
    [Tooltip("The arms camera whose render is shaded. Defaults to the camera on this object.")]
    public Camera armsCamera;
    [Tooltip("The eye the rays start from (the main camera). Defaults to this object's parent.")]
    public Transform eye;
    [Tooltip("Seconds between sun checks.")]
    public float interval = 0.05f;
    [Tooltip("How fast the gun goes into or out of shade, per second.")]
    public float ease = 14f;
    [Tooltip("How far towards the sun to look for anything casting shade, in metres.")]
    public float reach = 160f;
    [Tooltip("How much of the sun stays on the gun in full shade (a shadowed wall keeps 1 - the sun's shadow strength, 0.2 in the desert).")]
    [Range(0f, 1f)] public float shadedSunShare = 0.3f;

    [Header("Under a roof")]
    [Tooltip("How bright the room's own light is on the arms and gun once the eye has adapted, as a light from over the left shoulder (1 lights a white surface facing it full white before the tonemapper). No sun reaches them under a roof.")]
    [Range(0f, 2f)] public float roomLight = 0.3f;
    [Tooltip("Stops the eye has opened by which the player counts as under a roof.")]
    [Range(0.25f, 4f)] public float roofStops = 1.5f;

    Light _sun;
    EyeAdaptation _eyeAdaptation;
    float _shade, _target, _next, _saved, _nextFind, _roomShare = 1f;
    bool _dimmed;
    Quaternion _savedRotation;
    LightShadows _savedShadows;
    int _mask;
    Volume _probeVolume;
    VolumeProfile _probeProfile;
    ProbeVolumesOptions _probeLight;

    /// <summary>How much of the gun is in shade now, 0 (sun) to 1 (shade).</summary>
    public float Shade => _shade;

    void OnEnable()
    {
        if (armsCamera == null) armsCamera = GetComponent<Camera>();
        if (eye == null && transform.parent != null) eye = transform.parent;
        _mask = ~LayerMask.GetMask("Player", "FirstPerson", "PlayerBounds", "Ignore Raycast", "UI");
        EnsureProbeVolume();
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        Restore();
    }

    void OnDestroy()
    {
        if (_probeProfile != null) Destroy(_probeProfile);
    }

    /// <summary>A volume only the arms camera answers to (the FirstPerson layer; ViewModelRig sets the masks), holding how much
    /// of the probe volumes' light reaches the arms. Runtime only: no shared profile is written.</summary>
    void EnsureProbeVolume()
    {
        if (_probeVolume != null) return;
        var go = new GameObject("ArmsProbeLight") { hideFlags = HideFlags.DontSave, layer = LayerMask.NameToLayer("FirstPerson") };
        go.transform.SetParent(transform, false);
        _probeVolume = go.AddComponent<Volume>();
        _probeVolume.isGlobal = true;
        _probeVolume.priority = 1000f;
        _probeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        _probeProfile.hideFlags = HideFlags.DontSave;
        _probeLight = _probeProfile.Add<ProbeVolumesOptions>(false);
        _probeLight.intensityMultiplier.overrideState = true;
        _probeVolume.sharedProfile = _probeProfile;
    }

    Light Sun()
    {
        if (_sun != null && _sun.isActiveAndEnabled) return _sun;
        _sun = RenderSettings.sun;
        if (_sun == null)
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.shadows != LightShadows.None && (_sun == null || l.intensity > _sun.intensity)) _sun = l;
        return _sun;
    }

    void Update()
    {
        var sun = Sun();
        if (sun == null || eye == null) return;
        if (Time.time >= _next)
        {
            _next = Time.time + interval;
            Vector3 toSun = -sun.transform.forward;
            // The gun, the hand on the grip and the hand on the fore-end, roughly where they are drawn.
            Vector3 o = eye.position;
            Vector3 f = eye.forward, r = eye.right, u = eye.up;
            int blocked = 0;
            if (Physics.Raycast(o + f * 0.45f + r * 0.12f - u * 0.15f, toSun, reach, _mask, QueryTriggerInteraction.Ignore)) blocked++;
            if (Physics.Raycast(o + f * 0.25f + r * 0.18f - u * 0.25f, toSun, reach, _mask, QueryTriggerInteraction.Ignore)) blocked++;
            if (Physics.Raycast(o + f * 0.65f + r * 0.05f - u * 0.12f, toSun, reach, _mask, QueryTriggerInteraction.Ignore)) blocked++;
            _target = blocked / 3f;
        }
        _shade = Mathf.MoveTowards(_shade, _target, ease * Time.deltaTime);
        if (_eyeAdaptation == null && Time.time >= _nextFind) { _nextFind = Time.time + 1f; _eyeAdaptation = FindAnyObjectByType<EyeAdaptation>(); }
        // The probes' light on the arms is not to be multiplied up by the eye opening: in a room whose walls hold almost no
        // light the eye opens all the way, and the probes (which see more than the lightmapped walls do) burned the hands
        // white. Under a roof they keep the level they would have in the street's shade.
        if (_probeLight != null)
        {
            float open = _eyeAdaptation != null ? _eyeAdaptation.Offset : 0f;
            _probeLight.intensityMultiplier.value = Mathf.Lerp(1f, Mathf.Pow(2f, -open), Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(open / roofStops)));
        }
        // How bright the room is on the screen against the level the eye aims for: a room the eye cannot open far enough for
        // stays dim, and the hands dim with it. Eased, as the meter reads every few frames and turns with the view.
        if (_eyeAdaptation != null && _eyeAdaptation.Level > 0f)
        {
            float share = Mathf.Pow(_eyeAdaptation.Level / Mathf.Max(1f, _eyeAdaptation.roomLevel), 2.2f);
            _roomShare = Mathf.MoveTowards(_roomShare, Mathf.Clamp(share, 0.2f, 1.25f), Time.deltaTime);
        }
    }

    void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (cam != armsCamera || _shade <= 0.001f) return;
        var sun = Sun();
        if (sun == null) return;
        _saved = sun.intensity;
        _savedRotation = sun.transform.rotation;
        _savedShadows = sun.shadows;
        // Under a roof no sun reaches the gun, however little: any share of it, multiplied back by the stops the eye has opened
        // (EyeAdaptation), came out as a street's shade in a dim room, hands blown white with a hard shadow edge across
        // them (his play-test, 2026-11-03). And the probes alone cannot light them there: on their 3 m grid a room holds
        // next to nothing (they are what leaves the furniture black). So for this camera the light stands in for the room's
        // own: soft, no shadow, from over the shoulder, as bright as the room measures on the screen, divided by the stops
        // the eye has opened so that the two cancel. A real patch of sun on the floor is not shade, and keeps the sun.
        float stops = _eyeAdaptation != null ? _eyeAdaptation.Offset : 0f;
        float roof = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(stops / roofStops));
        float inRoom = roof * _shade;
        float room = roomLight * _roomShare * Mathf.Pow(2f, -stops);
        sun.intensity = Mathf.Lerp(_saved, Mathf.Lerp(_saved * shadedSunShare, room, roof), _shade);
        if (inRoom > 0.001f)
        {
            var e = eye != null ? eye : transform;
            var over = Quaternion.LookRotation(e.forward * 0.55f + e.right * 0.45f - Vector3.up * 0.7f, Vector3.up);
            sun.transform.rotation = Quaternion.Slerp(_savedRotation, over, inRoom);
            if (inRoom > 0.5f) sun.shadows = LightShadows.None;
        }
        _dimmed = true;
    }

    void OnEndCamera(ScriptableRenderContext context, Camera cam)
    {
        if (cam == armsCamera) Restore();
    }

    void Restore()
    {
        if (!_dimmed) return;
        if (_sun != null)
        {
            _sun.intensity = _saved;
            _sun.transform.rotation = _savedRotation;
            _sun.shadows = _savedShadows;
        }
        _dimmed = false;
    }
}
