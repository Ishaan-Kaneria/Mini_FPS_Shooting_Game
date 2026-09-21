using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Invisible drag surface that turns thumb movement into look. Buttons sit above it in
/// the hierarchy, so tapping one never steals the camera.
///
/// Three things here are not obvious and all three are the difference between controls
/// that feel tuned and controls that feel broken on somebody else's phone:
///
/// - <b>The drag is corrected for screen density</b> before it becomes look. Raw pixels
///   mean a swipe across a 1440p phone turns the view twice as far as the same physical
///   swipe on a 720p one, and the player has no way to understand that as anything but
///   the game being wrong on their device. See <see cref="TouchMetrics"/>.
/// - <b>Sights get their own sensitivity.</b> Aiming exists to make a small correction,
///   and a scope that swings at hip-fire speed overshoots every time.
/// - <b>A second finger takes over from the first.</b> Without that, lifting the thumb
///   mid-drag while another finger is already resting on the surface leaves the look
///   dead until every finger is off the glass -- which in a fight is never.
/// </summary>
public class TouchLookArea : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Tooltip("Multiplier on the corrected drag. The player-facing sensitivity lives on " +
             "the profile; this is for a layout that wants one surface faster than " +
             "another, and should normally stay at 1.")]
    public float sensitivity = 1f;

    [Tooltip("Optional. Supplies look sensitivity, the sights multiplier and smoothing. " +
             "Without one the surface still works, at the unscaled default.")]
    public TouchProfile profile;

    [Tooltip("Tapping without dragging fires the weapon. Off by default, and it should " +
             "stay off wherever there is a FIRE button: this surface covers most of the " +
             "screen, so with it on, every tap that is not exactly on a button shoots -- " +
             "including the ones aimed at ADS, JUMP or RUN and missed by a few pixels.")]
    public bool tapToFire;
    public float tapMaxDuration = 0.25f;
    public float tapMaxDrag = 20f;
    public float tapFireHoldTime = 0.12f;

    int _pointerId = int.MinValue;
    float _pressTime;
    float _dragDistance;
    float _fireUntil;

    /// <summary>Carried between frames only when the profile asks for smoothing.</summary>
    Vector2 _smoothed;

    public void OnPointerDown(PointerEventData eventData)
    {
        // A finger already looking keeps the surface. The handover happens on release
        // instead, which is the case that actually occurs: a thumb lifts while the other
        // hand is still resting somewhere on the right of the screen.
        if (_pointerId != int.MinValue) return;

        _pointerId = eventData.pointerId;
        _pressTime = Time.unscaledTime;
        _dragDistance = 0f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Adopt a drag from a finger that arrived while another owned the surface. The
        // owner has since let go -- OnPointerUp cleared it -- and this finger is already
        // moving, so waiting for it to lift and press again would lose the gesture.
        if (_pointerId == int.MinValue) _pointerId = eventData.pointerId;
        if (eventData.pointerId != _pointerId) return;

        _dragDistance += eventData.delta.magnitude;

        Vector2 corrected = TouchMetrics.ToReferencePixels(eventData.delta);

        // A thumb resting on glass reports tiny deltas forever. Below the dead zone the
        // view should be still, not drifting a degree at a time in whatever direction
        // the digitiser is guessing.
        float deadZone = profile != null
            ? TouchMetrics.MillimetresToReferencePixels(profile.lookDeadZoneMm)
            : 0f;

        if (corrected.magnitude < deadZone) return;

        float scale = sensitivity * (profile != null
            ? profile.LookScaleFor(MobileInput.Aim)
            : 1f);

        Vector2 look = corrected * scale;

        float smoothing = profile != null ? profile.lookSmoothing : 0f;
        if (smoothing > 0.001f)
        {
            // Latency traded for steadiness, and the trade is why it defaults to off:
            // this is the control the player judges the whole game by.
            _smoothed = Vector2.Lerp(look, _smoothed, smoothing);
            look = _smoothed;
        }
        else
        {
            _smoothed = Vector2.zero;
        }

        MobileInput.AddLook(look);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != _pointerId) return;

        _pointerId = int.MinValue;
        _smoothed = Vector2.zero;

        if (!TapFires) return;
        if (Time.unscaledTime - _pressTime > tapMaxDuration) return;
        if (_dragDistance > tapMaxDrag) return;

        _fireUntil = Time.unscaledTime + tapFireHoldTime;
    }

    /// <summary>The profile has the final say, so one asset can switch layouts over.</summary>
    bool TapFires => profile != null ? profile.tapToFire : tapToFire;

    void Update()
    {
        if (_fireUntil <= 0f) return;

        // Held briefly so a tap that lasted a single frame still registers as a shot.
        if (Time.unscaledTime < _fireUntil)
        {
            MobileInput.SetFireTap(true);
            return;
        }

        MobileInput.SetFireTap(false);
        _fireUntil = 0f;
    }

    /// <summary>
    /// Switched off with the control layer, the way the buttons do it. A tap left
    /// pending when this is disabled has nothing to clear it afterwards, and the
    /// weapon would go on reading a trigger pull that no longer has a finger behind it.
    /// </summary>
    void OnDisable()
    {
        _pointerId = int.MinValue;
        _smoothed = Vector2.zero;

        if (_fireUntil <= 0f) return;

        MobileInput.SetFireTap(false);
        _fireUntil = 0f;
    }
}
