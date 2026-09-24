using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The amber outline round whatever a gamepad has selected. One per root canvas, drawn
/// last so nothing covers it, and shown only while the pad is the live scheme -- a mouse
/// player is pointing at what they want and a second marker would only disagree with them.
///
/// Drawn as a separate rectangle rather than as a state on each control, so every control
/// in the game -- the kit's buttons, the builder's older ones, a slider, a card -- gets the
/// same ring without any of them being edited. It moves to a new selection over
/// <see cref="UITheme.motionFast"/>: long enough for the eye to follow where focus went,
/// short enough that it is there before the next press.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public class UIFocusRing : MonoBehaviour
{
    [Tooltip("Clear space between the control's edge and the ring, in canvas units.")]
    public float padding = 4f;

    [Tooltip("The ring's thickness in screen pixels.")]
    public float thicknessPixels = 2f;

    FlatRect _ring;
    RectTransform _canvasRect;
    Canvas _canvas;
    GameObject _target;
    Vector2 _pos, _size;
    float _t = 1f;
    Vector2 _fromPos, _fromSize;

    void Awake()
    {
        _canvas = GetComponent<Canvas>();
        _canvasRect = (RectTransform)transform;
    }

    FlatRect Ring
    {
        get
        {
            if (_ring != null) return _ring;
            var rt = UIKit.Rect(transform, "FocusRing");
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.gameObject.AddComponent<SafeAreaBleed>();
            _ring = rt.gameObject.AddComponent<FlatRect>();
            _ring.raycastTarget = false;
            _ring.color = new Color(0, 0, 0, 0);
            _ring.borderColor = UITheme.Active.accent;
            _ring.borderPixels = thicknessPixels;
            _ring.gameObject.SetActive(false);
            return _ring;
        }
    }

    void LateUpdate()
    {
        var es = EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        bool show = GameInput.UsingGamepad && sel != null && sel.activeInHierarchy
                    && sel.transform is RectTransform && BelongsHere(sel);
        var ring = Ring;
        if (!show)
        {
            if (ring.gameObject.activeSelf) ring.gameObject.SetActive(false);
            _target = null;
            return;
        }

        // Where the selection is, in this canvas's own units.
        var rt = (RectTransform)sel.transform;
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Vector2 a = _canvasRect.InverseTransformPoint(corners[0]);
        Vector2 b = _canvasRect.InverseTransformPoint(corners[2]);
        Vector2 pos = (a + b) * 0.5f;
        Vector2 size = new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y)) + Vector2.one * padding * 2f;

        if (!ring.gameObject.activeSelf)
        {
            ring.gameObject.SetActive(true);
            _pos = pos; _size = size; _t = 1f;
        }
        else if (sel != _target)
        {
            _fromPos = _pos; _fromSize = _size; _t = 0f;
        }
        _target = sel;

        float d = Mathf.Max(0.001f, UITheme.Active.motionFast);
        _t = Mathf.Min(1f, _t + Time.unscaledDeltaTime / d);
        float e = UITheme.EaseOut(_t);
        _pos = _t >= 1f ? pos : Vector2.Lerp(_fromPos, pos, e);
        _size = _t >= 1f ? size : Vector2.Lerp(_fromSize, size, e);

        var r = ring.rectTransform;
        r.localPosition = _pos;
        r.sizeDelta = _size;
        ring.borderColor = UITheme.Active.accent;
        if (r.GetSiblingIndex() != transform.childCount - 1) r.SetAsLastSibling();
    }

    bool BelongsHere(GameObject go)
    {
        var c = go.GetComponentInParent<Canvas>();
        return c != null && c.rootCanvas == _canvas;
    }
}
