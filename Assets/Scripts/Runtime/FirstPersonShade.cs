using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Puts the first-person arms and gun in the shade when the player stands in it.
///
/// The arms and gun are drawn by their own stacked camera (ViewModelRig), which sees only the FirstPerson layer, so
/// the shadow it renders holds the gun's own parts and nothing of the town: in the shade of a building the gun stayed in
/// full sun, the brightest thing on screen. This asks the world instead: a few rays from the gun towards the sun,
/// several times a second; the share that is blocked is how much of the gun is in shade. While the arms camera renders,
/// the sun is dimmed by that share times its shadow strength -- exactly what a shadow takes off a lit surface -- and put
/// back for every other camera. The change eases in over a few hundredths of a second, as stepping into a shadow does.
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

    Light _sun;
    float _shade, _target, _next, _saved;
    bool _dimmed;
    int _mask;

    /// <summary>How much of the gun is in shade now, 0 (sun) to 1 (shade).</summary>
    public float Shade => _shade;

    void OnEnable()
    {
        if (armsCamera == null) armsCamera = GetComponent<Camera>();
        if (eye == null && transform.parent != null) eye = transform.parent;
        _mask = ~LayerMask.GetMask("Player", "FirstPerson", "PlayerBounds", "Ignore Raycast", "UI");
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        Restore();
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
    }

    void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (cam != armsCamera || _shade <= 0.001f) return;
        var sun = Sun();
        if (sun == null) return;
        _saved = sun.intensity;
        sun.intensity = _saved * Mathf.Lerp(1f, 1f - sun.shadowStrength, _shade);
        _dimmed = true;
    }

    void OnEndCamera(ScriptableRenderContext context, Camera cam)
    {
        if (cam == armsCamera) Restore();
    }

    void Restore()
    {
        if (!_dimmed) return;
        if (_sun != null) _sun.intensity = _saved;
        _dimmed = false;
    }
}
