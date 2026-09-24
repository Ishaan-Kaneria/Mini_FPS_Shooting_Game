using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// An icon, a bar and a number on one line: health, shield, anything the player has a pool
/// of and watches drain.
///
/// All three are there because each answers a different glance. The icon says which pool
/// without reading, the bar says roughly how much at the edge of vision, and the number is
/// for the moment it matters exactly. The number is set in tabular figures so it does not
/// shimmy as it ticks down under fire.
///
/// Below <see cref="lowFraction"/> the bar and the number turn to the danger colour. That
/// is a state change, not an animation: nothing pulses, because the only pulsing thing in
/// the interface is the clock's last seconds, and two things pulsing at once is noise.
/// </summary>
[DisallowMultipleComponent]
public class UIStatBar : MonoBehaviour
{
    public Image icon;
    public UIProgressBar bar;
    public TMP_Text number;

    [Tooltip("Which theme to read. Empty means the active one.")]
    public UITheme theme;

    [Tooltip("The bar's colour while the pool is healthy.")]
    public Color color = new Color(0.373f, 0.702f, 0.416f, 1f);

    [Tooltip("At or below this fraction the bar and number switch to the danger colour. " +
             "Zero switches the warning off, which is right for a shield: running out of " +
             "shield is not the emergency running out of health is.")]
    [Range(0f, 1f)] public float lowFraction = 0.25f;

    UITheme Theme => theme != null ? theme : UITheme.Active;

    public void Set(float current, float max, bool instant = false)
    {
        var t = Theme;
        float f = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        bool low = lowFraction > 0f && f <= lowFraction;
        if (bar != null)
        {
            bar.FillColor = low ? t.danger : color;
            bar.Value = f;
            if (instant) bar.Snap();
        }
        if (number != null)
        {
            number.text = t.Tabular(Mathf.CeilToInt(Mathf.Max(0f, current)).ToString());
            number.color = low ? t.danger : t.textPrimary;
        }
        if (icon != null) icon.color = low ? t.danger : color;
    }
}
