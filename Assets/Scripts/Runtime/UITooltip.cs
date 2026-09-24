using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A short label that appears over an icon button when the pointer rests on it.
///
/// An icon on its own is a guess, and a row of four of them is four guesses. The tooltip is
/// what makes an icon-only control honest -- it is the label, shown when it is wanted.
///
/// <b>One view per canvas, found rather than registered.</b> The first tooltip to show
/// creates a <see cref="UITooltipView"/> on the root canvas and every later one finds it
/// there, so there is no static holding a view from a destroyed scene -- which, with domain
/// reload off, is the whole bug class CLAUDE.md warns about.
///
/// Touch has no hover, so on a touch device nothing here ever fires; a control that matters
/// on a phone needs a visible label. A gamepad has no hover either, so there the tooltip
/// follows the selection instead.
/// </summary>
[DisallowMultipleComponent]
public class UITooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler,
                         ISelectHandler, IDeselectHandler
{
    [Tooltip("What the control does, in two or three words.")]
    public string text;

    [Tooltip("Which theme to read. Empty means the active one.")]
    public UITheme theme;

    float _hoverSince = -1f;
    bool _shown;

    UITheme Theme => theme != null ? theme : UITheme.Active;

    public void OnPointerEnter(PointerEventData e) => _hoverSince = Time.unscaledTime;
    public void OnPointerExit(PointerEventData e) => Hide();
    public void OnPointerDown(PointerEventData e) => Hide();

    // A pad has no hover, so selection stands in for it: resting the focus ring on an icon
    // button names it, which is the only way a pad player learns what the icon means.
    public void OnSelect(BaseEventData e)
    {
        if (GameInput.UsingGamepad) _hoverSince = Time.unscaledTime;
    }

    public void OnDeselect(BaseEventData e) => Hide();

    void OnDisable() => Hide();

    void Update()
    {
        if (_shown || _hoverSince < 0f || string.IsNullOrEmpty(text)) return;
        if (Time.unscaledTime - _hoverSince < Theme.tooltipDelay) return;
        var view = UITooltipView.For(this);
        if (view == null) return;
        view.Show((RectTransform)transform, text);
        _shown = true;
    }

    void Hide()
    {
        _hoverSince = -1f;
        if (!_shown) return;
        _shown = false;
        var view = UITooltipView.For(this);
        if (view != null) view.Hide((RectTransform)transform);
    }
}
