using TMPro;
using UnityEngine;

/// <summary>
/// Keeps a line of text from rendering smaller than a phone can read: 12dp, about 1.9mm of
/// glass, whatever the canvas scale, the device or the interface-scale setting does to it.
///
/// Sizes are authored in canvas units against a 1920x1080 reference, and a unit is a
/// different physical size on every screen -- a 15-unit caption is comfortable on a
/// monitor and 1.6mm on a landscape phone. This remembers the authored size and raises it
/// to the floor when the screen needs it, never lowers it, and puts it back when the
/// screen stops needing it. It rechecks only when the canvas scale moves.
///
/// Added by <see cref="UIKit.Text"/> to every text the kit makes. The floor is density-
/// independent pixels, so on a 96dpi monitor it is 7px and never binds.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public class UITextFloor : MonoBehaviour
{
    [Tooltip("The smallest the text may render, in density-independent pixels.")]
    public float minimumDp = 12f;

    [SerializeField] float _authored = -1f;
    TMP_Text _text;
    float _scale = -1f;
    float _lastSet = -1f;

    void OnEnable()
    {
        _text = GetComponent<TMP_Text>();
        if (_authored < 0f) _authored = _text.fontSize;
        _scale = -1f;
        Apply();
    }

    void LateUpdate()
    {
        // A size set from code after this was added is the new authored size -- the kit's
        // own factory does exactly that, sizing a label after making it.
        if (_lastSet >= 0f && !Mathf.Approximately(_text.fontSize, _lastSet)) SetAuthored(_text.fontSize);
        var canvas = _text.canvas;
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        if (!Mathf.Approximately(scale, _scale)) Apply();
    }

    /// <summary>Call after changing the size from code, so the new size is the authored one.</summary>
    public void SetAuthored(float size)
    {
        _authored = size;
        _scale = -1f;
        Apply();
    }

    void Apply()
    {
        if (_text == null) return;
        if (_authored < 0f) _authored = _text.fontSize;
        var canvas = _text.canvas;
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        _scale = scale;
        if (scale <= 0.0001f) return;
        // The screen's own reading, not TouchMetrics.Dpi: that one substitutes a typical
        // phone when a monitor reports 96, which is right for sizing a thumb target and would
        // put 30px type on every desktop here.
        float dpi = TouchMetrics.ScreenDpi > 0f ? TouchMetrics.ScreenDpi : 160f;
        float floorPixels = minimumDp * dpi / 160f;
        float floorUnits = floorPixels / scale;
        _text.fontSize = Mathf.Max(_authored, floorUnits);
        _lastSet = _text.fontSize;
    }
}
