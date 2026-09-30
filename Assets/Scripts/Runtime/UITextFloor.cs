using TMPro;
using UnityEngine;

/// <summary>
/// Keeps a line of text from rendering smaller than a phone can read: 14dp, about 2.2mm of
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
    public float minimumDp = 14f;

    /// <summary>
    /// The floor every text gets, whatever its own field says. The builders serialized
    /// 12dp into the dashboard and the HUD. At 12dp (about 1.9mm) a phone's body text was
    /// a strain to read, and raising the initialiser alone would only reach text made
    /// after the change.
    /// </summary>
    public const float GlobalMinimumDp = 14f;

    /// <summary>The in-level HUD's floor: see Apply.</summary>
    public const float HudMinimumDp = 12f;

    [SerializeField] float _authored = -1f;
    int _onHud = -1;
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

    [SerializeField] float _authoredMin = -1f;
    float _lastMin = -2f;
    UnityEngine.UI.LayoutElement _element;

    void LateUpdate()
    {
        KeepLines();
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
        // The HUD keeps its own field. Its panels sit at fixed places round the touch
        // cluster, and the 14dp menu floor grew their text until the weapon card lay over
        // the health bars and the run panel over the mission strip. HUD labels are glanced
        // at mid-fight, not read; the menus are where the larger floor earns its room.
        // Asked of the nearest canvas, so a screen opened over the HUD on its own canvas
        // (settings, how to play) still reads as a menu.
        if (_onHud < 0)
        {
            var nearest = GetComponentInParent<Canvas>(true);
            _onHud = nearest != null && nearest.GetComponentInChildren<HUDController>(true) != null ? 1 : 0;
        }
        float floorDp = _onHud == 1 ? HudMinimumDp : Mathf.Max(minimumDp, GlobalMinimumDp);
        float floorPixels = floorDp * dpi / 160f;
        float floorUnits = floorPixels / scale;
        _text.fontSize = Mathf.Max(_authored, floorUnits);
        _lastSet = _text.fontSize;
        KeepLines();
    }

    /// <summary>
    /// Holds a one-line (or line-capped) text at least as tall as its lines.
    ///
    /// A TMP text tells a layout group its minimum height is zero, so when a card runs short
    /// of room the group squeezes the text first -- and a squeezed text with an ellipsis
    /// draws nothing at all. That is how the arena and store cards lost their names on a
    /// phone, where the text floor had made every line taller than the card was built for.
    /// With this, a card short of room overflows visibly instead of blanking its title, and
    /// the grids size cells from the result (<see cref="UIGrid.ContentHeight"/>).
    /// Free-running paragraphs are left alone: their height is the layout's to decide.
    /// </summary>
    void KeepLines()
    {
        if (_text == null) return;
        bool single = _text.textWrappingMode == TextWrappingModes.NoWrap;
        int cap = _text.maxVisibleLines;
        if (!single && (cap <= 0 || cap > 6)) return;
        if (_element == null) _element = GetComponent<UnityEngine.UI.LayoutElement>();
        if (_element == null)
        {
            // Only inside a layout group: a text placed by anchors has no use for one.
            if (transform.parent == null || transform.parent.GetComponent<UnityEngine.UI.LayoutGroup>() == null) return;
            _element = gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        }
        // A minimum set by somebody else since the last pass is theirs, and is kept as a floor.
        if (!Mathf.Approximately(_element.minHeight, _lastMin)) _authoredMin = _element.minHeight;
        var face = _text.font != null ? _text.font.faceInfo : default;
        float ratio = face.pointSize > 0f ? face.lineHeight / face.pointSize : 1.2f;
        float lines = string.IsNullOrEmpty(_text.text) ? 0f : single ? 1f : cap;
        // An empty text keeps no line: a combo readout with nothing to say must not hold a
        // blank row open in its panel.
        float need = lines <= 0f ? 0f : Mathf.Ceil(_text.fontSize * ratio * lines + _text.margin.y + _text.margin.w);
        float min = Mathf.Max(_authoredMin, need);
        if (!Mathf.Approximately(_element.minHeight, min)) _element.minHeight = min;
        _lastMin = _element.minHeight;
    }
}
