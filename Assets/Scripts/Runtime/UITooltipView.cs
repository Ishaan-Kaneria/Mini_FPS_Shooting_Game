using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The panel a <see cref="UITooltip"/> is drawn in. One per root canvas, created on demand.
/// </summary>
[DisallowMultipleComponent]
public class UITooltipView : MonoBehaviour
{
    public TMP_Text label;
    public CanvasGroup group;

    RectTransform _owner;
    float _alpha, _target;

    /// <summary>The view on this component's root canvas, created if there is none yet.</summary>
    public static UITooltipView For(Component from)
    {
        var c = from.GetComponentInParent<Canvas>();
        if (c == null) return null;
        var root = c.rootCanvas;
        var view = root.GetComponentInChildren<UITooltipView>(true);
        return view != null ? view : UIKit.TooltipView(root.transform);
    }

    /// <summary>Shows the tooltip above a control, kept inside the canvas.</summary>
    public void Show(RectTransform target, string text)
    {
        _owner = target;
        label.text = text;
        gameObject.SetActive(true);
        transform.SetAsLastSibling();

        var rt = (RectTransform)transform;
        var canvasRt = (RectTransform)rt.parent;
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

        // Above the control, centred on it; below it instead if there is no room above.
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Vector2 top = canvasRt.InverseTransformPoint((corners[1] + corners[2]) * 0.5f);
        Vector2 bottom = canvasRt.InverseTransformPoint((corners[0] + corners[3]) * 0.5f);
        const float gap = 8f;
        Vector2 size = rt.rect.size;
        Rect bounds = canvasRt.rect;

        bool above = top.y + gap + size.y <= bounds.yMax;
        rt.pivot = new Vector2(0.5f, above ? 0f : 1f);
        Vector2 at = above ? top + Vector2.up * gap : bottom - Vector2.up * gap;
        at.x = Mathf.Clamp(at.x, bounds.xMin + size.x * 0.5f + 4f, bounds.xMax - size.x * 0.5f - 4f);
        rt.localPosition = at;

        _target = 1f;
    }

    public void Hide(RectTransform target)
    {
        if (_owner != target) return;
        _target = 0f;
    }

    void Update()
    {
        var theme = UITheme.Active;
        _alpha = Mathf.MoveTowards(_alpha, _target, Time.unscaledDeltaTime / Mathf.Max(0.001f, theme.motionFast));
        group.alpha = _alpha;
        if (_alpha <= 0f && _target <= 0f) gameObject.SetActive(false);
    }
}
