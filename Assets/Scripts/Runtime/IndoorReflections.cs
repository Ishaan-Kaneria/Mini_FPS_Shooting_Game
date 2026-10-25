using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// What a room's floor and walls reflect: the room, not the sky (Docs/DESIGN_NOTES.md "Real light"). Every surface without a
/// reflection probe round it mirrors the scene's sky, so indoors a carpet or a tiled floor took a cold blue sheen from a sky
/// it cannot see (the lighting tour: with reflections off the floors came back red and warm). FPSKitTownGI puts a box
/// reflection probe round each house that has an inside, all sharing one small warm cubemap (a lit ceiling, pale walls, a
/// darker floor), so nothing is ever captured or rendered for them.
///
/// URP draws at most 64 reflection probes a frame, and on the Forward path (Medium) each object takes its probe from its own
/// bounds -- and a house's walls are merged into its block's chunk with the street fronts, so a house probe would turn whole
/// facades into a room. So: only under Forward+ (High), and only the houses within <see cref="radius"/> of the eye, checked
/// twice a second.
/// </summary>
[DisallowMultipleComponent]
public class IndoorReflections : MonoBehaviour
{
    [Tooltip("Metres from the eye inside which a house's probe is on.")]
    [Min(5f)] public float radius = 45f;

    readonly List<ReflectionProbe> _probes = new List<ReflectionProbe>();
    float _next;
    bool _wasOn = true;

    void OnEnable()
    {
        _probes.Clear();
        GetComponentsInChildren(true, _probes);
        _next = 0f;
    }

    /// <summary>The active pipeline draws per pixel from a cluster of probes (Forward+ or Deferred+).</summary>
    static bool PerPixelProbes()
    {
        if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return false;
        var list = urp.rendererDataList;
        if (list.Length == 0 || !(list[0] is UniversalRendererData data)) return false;
        return data.renderingMode == RenderingMode.ForwardPlus || data.renderingMode == RenderingMode.DeferredPlus;
    }

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.5f;

        var cam = Camera.main;
        bool on = cam != null && PerPixelProbes();
        if (!on && !_wasOn) return;
        _wasOn = on;
        float r2 = radius * radius;
        Vector3 eye = on ? cam.transform.position : Vector3.zero;
        foreach (var p in _probes)
        {
            if (p == null) continue;
            bool want = on && (p.bounds.ClosestPoint(eye) - eye).sqrMagnitude < r2;
            if (p.enabled != want) p.enabled = want;
        }
    }
}
