using TMPro;
using UnityEngine;

/// <summary>
/// A flat track with a fill: achievement progress, level progress, a reload, a health pool.
///
/// The fill is sized by its anchors rather than by a width, so the bar is right at any size
/// its parent gives it and nothing has to be told the track's length. It moves to a new
/// value over <see cref="UITheme.motionStandard"/> rather than jumping -- long enough that a
/// change is seen, short enough that the number and the bar never visibly disagree.
/// </summary>
[DisallowMultipleComponent]
public class UIProgressBar : MonoBehaviour
{
    [Tooltip("The track. Its fill colour is the empty part of the bar.")]
    public FlatRect track;

    [Tooltip("The filled part, anchored to the track's left edge.")]
    public FlatRect fill;

    [Tooltip("Optional count shown beside the bar, e.g. 12 / 20.")]
    public TMP_Text count;

    [Tooltip("Which theme to read. Empty means the active one.")]
    public UITheme theme;

    [SerializeField, Range(0f, 1f)] float _value;

    float _shown = -1f;

    UITheme Theme => theme != null ? theme : UITheme.Active;

    /// <summary>The fraction filled, 0 to 1. Animates towards it.</summary>
    public float Value
    {
        get => _value;
        set => _value = Mathf.Clamp01(value);
    }

    public Color FillColor
    {
        get => fill != null ? fill.color : Color.clear;
        set { if (fill != null) fill.color = value; }
    }

    /// <summary>Sets the value and, if the bar has a count, writes it as "current / max".</summary>
    public void Set(float current, float max, bool instant = false)
    {
        Value = max > 0f ? current / max : 0f;
        if (count != null)
            count.text = Theme.Tabular($"{Mathf.RoundToInt(current)} / {Mathf.RoundToInt(max)}");
        if (instant) Snap();
    }

    /// <summary>Jumps straight to the value, for a bar being shown for the first time.</summary>
    public void Snap()
    {
        _shown = _value;
        Apply();
    }

    void OnEnable() => Snap();

    void Update()
    {
        if (Mathf.Approximately(_shown, _value)) return;
        // Covers the whole bar in one standard duration, so a big change and a small one
        // both settle in about the same time the rest of the interface moves in.
        float speed = 1f / Mathf.Max(0.001f, Theme.motionStandard);
        _shown = Mathf.MoveTowards(_shown, _value, speed * Time.unscaledDeltaTime);
        Apply();
    }

    void Apply()
    {
        if (fill == null) return;
        var rt = fill.rectTransform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(Mathf.Clamp01(_shown), 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        fill.enabled = _shown > 0.0001f;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying) Snap();
    }
#endif
}
