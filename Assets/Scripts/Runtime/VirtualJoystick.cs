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
             "fully pushed. Overridden by the profile's physical radius when one is set.")]
    public float radius = 140f;

    [Tooltip("Below this the stick reads as centred, which stops thumb drift creeping " +
             "you forward.")]
    [Range(0f, 0.5f)] public float deadZone = 0.15f;

    [Tooltip("Optional. Supplies the stick's physical size, its dead zone and the " +
             "push-to-sprint threshold. Without one the pixel values above apply.")]
    public TouchProfile profile;

    /// <summary>
    /// Latched while a full push is sprinting, so easing off to steer does not drop it.
    /// Cleared when the stick comes back through the dead zone -- see UpdateSprint.
    /// </summary>
    bool _sprintLatched;

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
        SetSprint(false);
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

        float reach = Reach;

        Vector2 offset = Vector2.ClampMagnitude(local - _origin, reach);
        if (handle != null) handle.anchoredPosition = offset;

        Vector2 value = offset / reach;
        float push = value.magnitude;

        float zone = profile != null ? profile.stickDeadZone : deadZone;
        MobileInput.Move = push < zone ? Vector2.zero : value;

        UpdateSprint(push, zone);
    }

    /// <summary>
    /// Full travel in canvas units, from the profile's physical radius when there is one.
    ///
    /// Physical rather than pixel, because a thumb is the same size on every phone and a
    /// pixel is not. A stick tuned to 140px is a comfortable roll on one device and a
    /// reach across the palm on another, and the player feels that as the game being
    /// badly made rather than as a units problem.
    /// </summary>
    float Reach
    {
        get
        {
            if (profile == null) return Mathf.Max(1f, radius);

            var canvas = _rect != null ? _rect.GetComponentInParent<Canvas>() : null;
            float scale = canvas != null ? canvas.scaleFactor : 1f;

            return Mathf.Max(1f, TouchMetrics.MinimumTouchSize(profile.stickRadiusMm, scale));
        }
    }

    /// <summary>
    /// Sprint by pushing the stick to its edge, so running costs no button.
    ///
    /// The button still exists and still works -- they are two independent sources on
    /// MobileInput for exactly that reason -- but the stick is the one that matters. The
    /// sprint button and the look surface both want the right thumb, so a sprint that
    /// costs a button press is a sprint taken while unable to aim, which is the moment
    /// you least want to be blind.
    ///
    /// It latches because steering is not stopping. Without the latch, easing the stick
    /// off full push to turn a corner drops the sprint, and a chase becomes a rapid
    /// flicker in and out of running that reads as the game deciding on its own.
    /// </summary>
    void UpdateSprint(float push, float deadZoneFraction)
    {
        if (profile == null)
        {
            if (_sprintLatched) SetSprint(false);
            return;
        }

        if (push >= profile.sprintPush) SetSprint(true);
        else if (!profile.sprintLatches || push <= deadZoneFraction) SetSprint(false);
    }

    void SetSprint(bool on)
    {
        if (_sprintLatched == on) return;

        _sprintLatched = on;
        MobileInput.SetSprintStick(on);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != _pointerId) return;

        _pointerId = int.MinValue;
        MobileInput.Move = Vector2.zero;
        SetSprint(false);

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
