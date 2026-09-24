using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns a canvas's reference resolution, so the three things that want to change it --
/// the authored value, the device form (<see cref="PhoneUI"/>) and the player's interface
/// scale -- combine instead of overwriting one another.
///
/// The authored reference is captured the first time the binder is added and never written
/// again; everything is computed from it. That matters because the old path multiplied the
/// scaler's own value in place, so applying it twice -- a second <c>Start</c>, a settings
/// change -- compounded, and a canvas scaled twice for a phone is a canvas of enormous type.
///
/// Larger interface scale is a smaller reference resolution, which is why the setting
/// divides.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasScaler))]
public class UIScaleBinder : MonoBehaviour
{
    [SerializeField] Vector2 _authored;
    [SerializeField] float _authoredMatch;
    [SerializeField] bool _captured;

    float _formScale = 1f;
    float? _formMatch;
    CanvasScaler _scaler;

    CanvasScaler Scaler => _scaler != null ? _scaler : (_scaler = GetComponent<CanvasScaler>());

    /// <summary>The binder on a canvas, added and primed with the authored values if missing.</summary>
    public static UIScaleBinder For(Canvas canvas)
    {
        if (canvas == null) return null;
        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return null;
        var b = canvas.GetComponent<UIScaleBinder>();
        if (b == null) b = canvas.gameObject.AddComponent<UIScaleBinder>();
        b.Capture();
        return b;
    }

    void Capture()
    {
        if (_captured) return;
        _authored = Scaler.referenceResolution;
        _authoredMatch = Scaler.matchWidthOrHeight;
        _captured = true;
    }

    /// <summary>Set by <see cref="PhoneUI"/>: how much this form shrinks the reference, and its match.</summary>
    public void SetForm(float scale, float? match)
    {
        _formScale = scale;
        _formMatch = match;
        Apply();
    }

    void OnEnable()
    {
        Capture();
        GameSettings.Changed += OnSetting;
        Apply();
    }

    void OnDisable() => GameSettings.Changed -= OnSetting;

    void OnSetting(string key)
    {
        if (key == "uiScale" || key == "*") Apply();
    }

    public void Apply()
    {
        if (!_captured) return;
        Scaler.referenceResolution = _authored * _formScale / GameSettings.UiScale;
        Scaler.matchWidthOrHeight = _formMatch ?? _authoredMatch;
    }
}
