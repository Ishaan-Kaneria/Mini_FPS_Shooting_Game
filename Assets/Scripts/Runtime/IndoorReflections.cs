using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// What a room's floor and walls reflect: the room, not the sky (Docs/DESIGN_NOTES.md "Real light"). Every surface without a
/// reflection probe round it mirrors the scene's sky, so indoors a carpet or a tiled floor took a cold blue sheen from a sky
/// it cannot see (the lighting tour: with reflections off the floors came back red and warm). FPSKitTownGI puts a box
/// reflection probe round each house and each shop that has an inside, all sharing one small warm cubemap (a lit ceiling,
/// pale walls, a darker floor), so nothing is ever captured or rendered for them.
///
/// URP draws at most 64 reflection probes a frame, and on the Forward path (Medium) each object takes its probe from its own
/// bounds -- and a house's walls are merged into its block's chunk with the street fronts, so a house probe would turn whole
/// facades into a room. So: only under Forward+ (High), and only the rooms within <see cref="radius"/> of the eye, at most
/// <see cref="maxOn"/> of them, nearest first (a street of shops has dozens), checked twice a second.
/// </summary>
[DisallowMultipleComponent]
public class IndoorReflections : MonoBehaviour
{
    [Tooltip("Metres from the eye inside which a room's probe (a house's or a shop's) may be on.")]
    [Min(5f)] public float radius = 45f;

    [Tooltip("Most rooms' probes on at once, the nearest first. URP draws at most 64 reflection probes a frame, and the street's probes are among them.")]
    [Range(8, 56)] public int maxOn = 40;

    readonly List<ReflectionProbe> _probes = new List<ReflectionProbe>();
    float[] _d2, _near;
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
        int n = _probes.Count;
        if (_d2 == null || _d2.Length != n) { _d2 = new float[n]; _near = new float[n]; }
        // Each room's distance from the eye (past the radius, or everything when off: never on); then, if more are within
        // it than may be on, the distance of the last one that may.
        float r2 = radius * radius, cut = r2;
        Vector3 eye = on ? cam.transform.position : Vector3.zero;
        int within = 0;
        for (int i = 0; i < n; i++)
        {
            var p = _probes[i];
            float d2 = on && p != null ? (p.bounds.ClosestPoint(eye) - eye).sqrMagnitude : float.MaxValue;
            _d2[i] = d2;
            if (d2 < r2) _near[within++] = d2;
        }
        if (within > maxOn)
        {
            System.Array.Sort(_near, 0, within);
            cut = _near[maxOn - 1];
        }
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            var p = _probes[i];
            if (p == null) continue;
            bool want = _d2[i] < r2 && _d2[i] <= cut && count < maxOn;
            if (want) count++;
            if (p.enabled != want) p.enabled = want;
        }
    }
}
