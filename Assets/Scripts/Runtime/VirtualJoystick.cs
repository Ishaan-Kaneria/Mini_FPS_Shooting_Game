using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Thumbstick. Drag from anywhere inside the pad; the handle follows, clamped to
/// the radius, and reports a normalised vector.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public RectTransform handle;

    [Tooltip("Pixels of travel at the canvas reference resolution before the stick is fully pushed.")]
    public float radius = 110f;

    [Tooltip("Below this the stick reads as centred, which stops thumb drift creeping you forward.")]
    [Range(0f, 0.5f)] public float deadZone = 0.15f;

    [Tooltip("Recentre the pad wherever the thumb lands instead of a fixed spot.")]
    public bool dynamicOrigin = true;

    RectTransform _rect;
    Vector2 _homePosition;
    Vector2 _origin;
    int _pointerId = int.MinValue;

    void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _homePosition = _rect.anchoredPosition;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_pointerId != int.MinValue) return;
        _pointerId = eventData.pointerId;

        if (dynamicOrigin &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_rect.parent, eventData.position, eventData.pressEventCamera, out var local))
        {
            _rect.anchoredPosition = local;
        }

        _origin = _rect.anchoredPosition;
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != _pointerId) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_rect.parent, eventData.position, eventData.pressEventCamera, out var local))
            return;

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
        if (dynamicOrigin) _rect.anchoredPosition = _homePosition;
    }
}
