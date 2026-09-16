using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Floating thumbstick. The whole region this sits on is the stick: put a thumb down
/// anywhere in it and the pad appears under that thumb.
///
/// The touch region and the visible pad are deliberately two different rects. They
/// used to be one, which meant the only place a touch counted was inside the 300px
/// ring drawn on screen -- so a thumb landing anywhere else on the left of the screen
/// did nothing, or worse, fell through to the look area behind and spun the camera.
/// A player cannot see where their thumb is on a phone, so a stick you have to find
/// is a stick you keep missing.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Tooltip("The ring drawn under the thumb. Moved to wherever the touch lands. " +
             "A child of this region, anchored at its centre.")]
    public RectTransform pad;

    [Tooltip("The knob inside the pad.")]
    public RectTransform handle;

    [Tooltip("Pixels of travel at the canvas reference resolution before the stick is " +
             "fully pushed.")]
    public float radius = 140f;

    [Tooltip("Below this the stick reads as centred, which stops thumb drift creeping " +
             "you forward.")]
    [Range(0f, 0.5f)] public float deadZone = 0.15f;

    [Tooltip("Hide the pad until a thumb is down. A ring sitting in the corner of the " +
             "screen suggests the stick only works there, which is the opposite of true.")]
    public bool hideWhenIdle = true;

    RectTransform _rect;
    Vector2 _origin;
    int _pointerId = int.MinValue;

    void Awake()
    {
        _rect = GetComponent<RectTransform>();
        ShowPad(!hideWhenIdle);
    }

    void OnDisable()
    {
        _pointerId = int.MinValue;
        MobileInput.Move = Vector2.zero;
        ShowPad(!hideWhenIdle);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_pointerId != int.MinValue) return;
        if (!LocalPoint(eventData, out Vector2 local)) return;

        _pointerId = eventData.pointerId;
        _origin = local;

        if (pad != null) pad.anchoredPosition = local;
        ShowPad(true);

        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != _pointerId) return;
        if (!LocalPoint(eventData, out Vector2 local)) return;

        Vector2 offset = Vector2.ClampMagnitude(local - _origin, radius);
        if (handle != null) handle.anchoredPosition = offset;

        Vector2 value = offset / radius;
        MobileInput.Move = value.magnitude < deadZone ? Vector2.zero : value;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != _pointerId) return;

        _pointerId = int.MinValue;
        MobileInput.Move = Vector2.zero;

        if (handle != null) handle.anchoredPosition = Vector2.zero;
        ShowPad(!hideWhenIdle);
    }

    /// <summary>
    /// The touch position in this region's own space, measured from its centre.
    ///
    /// Requires the region's pivot to be centred, which is what lets the pad -- anchored
    /// at the same centre -- be placed by simply assigning this as its anchoredPosition.
    /// </summary>
    bool LocalPoint(PointerEventData eventData, out Vector2 local)
        => RectTransformUtility.ScreenPointToLocalPointInRectangle(
               _rect, eventData.position, eventData.pressEventCamera, out local);

    void ShowPad(bool visible)
    {
        if (pad == null) return;

        // Alpha rather than SetActive: a disabled object stops receiving the drag it is
        // in the middle of, and the stick would drop out from under the thumb.
        var group = pad.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = visible ? 1f : 0f;
        else pad.gameObject.SetActive(visible);
    }
}
