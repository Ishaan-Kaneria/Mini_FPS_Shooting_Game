using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps a whole canvas's content out from under the notch, the rounded corners and the
/// home bar, while its backgrounds still run edge to edge.
///
/// <see cref="SafeAreaFitter"/> insets one rectangle; a screen has to be built inside it to
/// benefit. This makes that true of a canvas after the fact, which is what lets the rule be
/// "every root canvas" rather than "every canvas somebody remembered". It sorts the canvas's
/// direct children into two kinds:
///
///   * <b>Full-screen layers</b> -- stretched over the canvas with no offsets. They bleed:
///     a backdrop, a shade behind an overlay, a damage flash must reach the physical edge
///     or the notch shows a strip of whatever is underneath. Their own children are then
///     sorted the same way, one level down, because the content of an overlay is exactly
///     as close to the notch as the content of the screen.
///   * <b>Placed content</b> -- anchored to a corner, an edge, a point. It is moved into a
///     "SafeArea" container fitted to the safe area, where its anchors now mean the same
///     thing relative to the part of the screen that can be seen.
///
/// Sibling order is kept by giving each run of placed siblings its own container at the
/// run's position, so nothing changes what it draws over. A full-screen layer with a layout
/// group is content, not a layer: its children are the layout's, and moving them would
/// break it. Anything marked <see cref="SafeAreaBleed"/> is left alone, and a canvas that
/// already has a <see cref="SafeAreaFitter"/> is left entirely alone, because it was built
/// for one.
///
/// Runs once, at the first frame. A notch does not move; a rotation only changes which
/// edge it is on, which the fitters inside handle.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public class SafeAreaCanvas : MonoBehaviour
{
    [Tooltip("How deep into full-screen layers to look for content. Two covers a screen and " +
             "an overlay on it.")]
    [Range(0, 4)] public int depth = 2;

    bool _done;

    // In Awake, not Start: UIBootstrap adds this after every Awake and before any Start, and
    // a screen's Start may build panels with safe areas of their own onto the canvas --
    // which, seen first, would make this skip the whole canvas.
    void Awake() => Apply();

    public void Apply()
    {
        if (_done) return;
        _done = true;
        if (GetComponentInChildren<SafeAreaFitter>(true) != null) return;
        Sort((RectTransform)transform, depth);
    }

    static void Sort(RectTransform parent, int depth)
    {
        var children = new List<RectTransform>();
        foreach (Transform c in parent)
            if (c is RectTransform rt) children.Add(rt);

        List<RectTransform> run = null;
        int runIndex = 0;
        foreach (var child in children)
        {
            bool bleed = child.GetComponent<SafeAreaBleed>() != null || child.GetComponent<SafeAreaFitter>() != null;
            bool layer = !bleed && FullScreen(child) && child.GetComponent<LayoutGroup>() == null;
            if (bleed || layer)
            {
                Flush(parent, run, runIndex);
                run = null;
                if (layer && depth > 0) Sort(child, depth - 1);
                continue;
            }
            if (run == null)
            {
                run = new List<RectTransform>();
                runIndex = child.GetSiblingIndex();
            }
            run.Add(child);
        }
        Flush(parent, run, runIndex);
    }

    static void Flush(RectTransform parent, List<RectTransform> run, int index)
    {
        if (run == null || run.Count == 0) return;
        var safe = UIKit.Rect(parent, "SafeArea");
        UIKit.Fill(safe);
        // No padding: the platform's safe area already stops short of the hazard, and a
        // menu does not need the extra thumb room the touch controls ask for.
        var fitter = safe.gameObject.AddComponent<SafeAreaFitter>();
        fitter.paddingMm = 0f;
        fitter.Apply();
        safe.SetSiblingIndex(index);
        foreach (var rt in run) rt.SetParent(safe, false);
    }

    static bool FullScreen(RectTransform rt)
        => rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one
           && rt.offsetMin.sqrMagnitude < 1f && rt.offsetMax.sqrMagnitude < 1f;
}

/// <summary>
/// Leave this object where it is, full-bleed, when <see cref="SafeAreaCanvas"/> moves a
/// canvas's content inside the safe area. For things positioned in the canvas's own
/// coordinates -- the focus ring, the tooltip -- and anything meant to reach the edge.
/// </summary>
[DisallowMultipleComponent]
public class SafeAreaBleed : MonoBehaviour { }
