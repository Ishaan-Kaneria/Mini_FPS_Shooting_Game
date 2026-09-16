using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Invisible drag surface that turns thumb movement into look. Buttons sit above
/// it in the hierarchy, so tapping one never steals the camera.
/// </summary>
public class TouchLookArea : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Tooltip("Multiplier on the raw drag. Final turn rate also uses the player's Touch Sensitivity.")]
    public float sensitivity = 1f;

    [Tooltip("Tapping without dragging fires the weapon, the way most mobile shooters do.")]
    public bool tapToFire = true;
    public float tapMaxDuration = 0.25f;
    public float tapMaxDrag = 20f;
    public float tapFireHoldTime = 0.12f;

    int _pointerId = int.MinValue;
    float _pressTime;
    float _dragDistance;
    float _fireUntil;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_pointerId != int.MinValue) return;

        _pointerId = eventData.pointerId;
        _pressTime = Time.unscaledTime;
        _dragDistance = 0f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != _pointerId) return;

        _dragDistance += eventData.delta.magnitude;
        MobileInput.AddLook(eventData.delta * sensitivity);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != _pointerId) return;
        _pointerId = int.MinValue;

        if (!tapToFire) return;
        if (Time.unscaledTime - _pressTime > tapMaxDuration) return;
        if (_dragDistance > tapMaxDrag) return;

        _fireUntil = Time.unscaledTime + tapFireHoldTime;
    }

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
        if (_fireUntil <= 0f) return;

        MobileInput.SetFireTap(false);
        _fireUntil = 0f;
    }
}
