using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Makes every screen playable with a gamepad without each screen having to know about one.
///
/// A pad has no pointer, so it can only press what is <i>selected</i> -- and a screen with
/// nothing selected is a screen a pad player cannot use at all, with nothing on it saying
/// why. Unity leaves the selection wherever the last click put it, including on a button
/// that is now behind an overlay, switched off, or in a scene that has been unloaded. So
/// once a frame, while the pad is the live scheme, this checks the selection is something
/// the player can actually see and press, and when it is not, picks one:
///
///   1. the highest-priority visible <see cref="UIDefaultSelection"/>, which is how a screen
///      states its own answer ("the PLAY button", "the first unlocked level");
///   2. otherwise a primary button, because the one amber button on a screen is the thing
///      the screen is for;
///   3. otherwise the top-left control, which is where a reader starts.
///
/// "Can see" is decided by a raycast at the control's centre, the same test <c>VerifyFlow</c>
/// uses for clicks. It is the one answer that knows about overlays, canvas groups, sorting
/// orders and masks all at once, because it asks the thing that routes real input.
///
/// It also turns LB/RB into the previous and next tab on whatever tab bar is visible.
/// </summary>
[DisallowMultipleComponent]
public class UINavigator : MonoBehaviour
{
    readonly List<RaycastResult> _hits = new List<RaycastResult>();
    PointerEventData _probe;
    GameObject _lastValid;

    void Update()
    {
        var es = EventSystem.current;
        if (es == null) return;

        if (GameInput.UsingGamepad)
        {
            var current = es.currentSelectedGameObject;
            if (current != _lastValid || !Usable(current))
            {
                if (Usable(current))
                {
                    _lastValid = current;
                    ScrollIntoView(current);
                }
                else
                {
                    var pick = PickDefault();
                    es.SetSelectedGameObject(pick);
                    _lastValid = pick;
                    if (pick != null) ScrollIntoView(pick);
                }
            }
            HandleTabs();
        }
    }

    /// <summary>Whether a control is active, interactable and not covered by anything.</summary>
    public bool Usable(GameObject go)
    {
        if (go == null || !go.activeInHierarchy) return false;
        var sel = go.GetComponent<Selectable>();
        if (sel == null || !sel.IsInteractable()) return false;
        return Reachable(sel);
    }

    bool Reachable(Selectable sel)
    {
        var es = EventSystem.current;
        if (es == null) return false;
        var rt = sel.transform as RectTransform;
        if (rt == null) return false;
        var canvas = sel.GetComponentInParent<Canvas>();
        if (canvas == null) return false;
        var cam = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
        _probe ??= new PointerEventData(es);
        _probe.position = screen;
        _hits.Clear();
        es.RaycastAll(_probe, _hits);
        if (_hits.Count == 0) return false;
        var top = _hits[0].gameObject;
        return top != null && (top == sel.gameObject || top.transform.IsChildOf(sel.transform));
    }

    GameObject PickDefault()
    {
        UIDefaultSelection best = null;
        foreach (var d in UIDefaultSelection.Live)
        {
            if (d == null) continue;
            if (!Usable(d.gameObject)) continue;
            if (best == null || d.priority > best.priority) best = d;
        }
        if (best != null) return best.gameObject;

        Selectable pick = null;
        bool pickPrimary = false;
        Vector2 pickPos = default;
        foreach (var s in Selectable.allSelectablesArray)
        {
            if (s == null || !s.IsInteractable() || !s.gameObject.activeInHierarchy) continue;
            if (!Reachable(s)) continue;
            bool primary = s is FlatButton fb && fb.variant == FlatButton.Variant.Primary;
            var rt = (RectTransform)s.transform;
            Vector2 pos = rt.TransformPoint(rt.rect.center);
            bool better = pick == null
                          || (primary && !pickPrimary)
                          || (primary == pickPrimary && (pos.y > pickPos.y + 1f || (Mathf.Abs(pos.y - pickPos.y) <= 1f && pos.x < pickPos.x)));
            if (better)
            {
                pick = s;
                pickPrimary = primary;
                pickPos = pos;
            }
        }
        return pick != null ? pick.gameObject : null;
    }

    /// <summary>Keeps a selection inside a scroll view on screen as the pad walks down a list.</summary>
    static void ScrollIntoView(GameObject go)
    {
        var scroll = go.GetComponentInParent<ScrollRect>();
        if (scroll == null || scroll.content == null || scroll.viewport == null) return;
        var target = (RectTransform)go.transform;
        var viewport = scroll.viewport;
        var content = scroll.content;

        Bounds item = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, target);
        Rect view = viewport.rect;
        Vector2 shift = Vector2.zero;
        if (scroll.vertical)
        {
            if (item.max.y > view.yMax) shift.y = item.max.y - view.yMax;
            else if (item.min.y < view.yMin) shift.y = item.min.y - view.yMin;
        }
        if (scroll.horizontal)
        {
            if (item.max.x > view.xMax) shift.x = item.max.x - view.xMax;
            else if (item.min.x < view.xMin) shift.x = item.min.x - view.xMin;
        }
        if (shift != Vector2.zero) content.anchoredPosition -= shift;
    }

    void HandleTabs()
    {
        int step = GameInput.UITabNext.WasPressedThisFrame() ? 1 : GameInput.UITabPrevious.WasPressedThisFrame() ? -1 : 0;
        if (step == 0) return;
        foreach (var bar in FindObjectsByType<UITabBar>(FindObjectsInactive.Exclude))
        {
            if (bar.tabs.Length == 0 || bar.tabs[0] == null || !Reachable(bar.tabs[0])) continue;
            bar.Select((bar.Selected + step + bar.tabs.Length) % bar.tabs.Length, notify: true);
            return;
        }
    }
}
