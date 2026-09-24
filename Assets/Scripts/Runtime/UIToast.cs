using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A short card that says something happened -- an achievement, coins banked, a purchase
/// refused -- and leaves on its own.
///
/// A toast never asks for anything and never blocks: it is not a raycast target, so it
/// cannot swallow the click it happens to be sitting over. What kind of news it is shows in
/// the stripe down its left edge, in the functional colours, and in the icon.
///
/// The card inside is what moves. The toast itself belongs to a layout group, which owns
/// its position -- the same trap <see cref="HoverCard"/> documents for the arena grid -- so
/// the slide is written to the card's local position, which the layout never touches.
/// </summary>
[DisallowMultipleComponent]
public class UIToast : MonoBehaviour
{
    public enum Kind { Info, Success, Warning, Reward }

    [Tooltip("The part that slides and fades. A child, so the layout group can own the toast.")]
    public RectTransform card;
    public CanvasGroup group;
    public FlatRect face;
    public Image icon;
    public TMP_Text title;
    public TMP_Text body;

    [Tooltip("Seconds on screen before it leaves. Zero or less stays until dismissed.")]
    public float holdSeconds = 3.5f;

    float _age;
    bool _leaving;
    float _leaveAge;

    /// <summary>The colour a kind of toast wears on its stripe and icon.</summary>
    public static Color ColorFor(UITheme t, Kind kind) => kind switch
    {
        Kind.Success => t.success,
        Kind.Warning => t.danger,
        Kind.Reward => t.accent,
        _ => t.info,
    };

    public void Set(UITheme t, Kind kind, string iconId, string heading, string text)
    {
        var c = ColorFor(t, kind);
        face.stripeColor = c;
        if (icon != null)
        {
            icon.sprite = t.IconSprite(iconId);
            icon.color = c;
            icon.enabled = icon.sprite != null;
        }
        title.text = heading;
        body.text = text;
        body.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    public void Dismiss()
    {
        if (_leaving) return;
        _leaving = true;
        _leaveAge = _age;
    }

    void OnEnable()
    {
        _age = 0f;
        _leaving = false;
        Pose(0f);
    }

    void Update()
    {
        var t = UITheme.Active;
        _age += Time.unscaledDeltaTime;
        float d = Mathf.Max(0.001f, t.motionStandard);

        if (!_leaving && holdSeconds > 0f && _age >= d + holdSeconds) Dismiss();

        if (_leaving)
        {
            float k = 1f - Mathf.Clamp01((_age - _leaveAge) / d);
            Pose(k);
            if (k <= 0f) Destroy(gameObject);
        }
        else
        {
            Pose(UITheme.EaseOut(_age / d));
        }
    }

    /// <summary>0 is off to the right and invisible, 1 is in place.</summary>
    void Pose(float k)
    {
        var t = UITheme.Active;
        if (group != null) group.alpha = k;
        if (card != null) card.localPosition = new Vector3((1f - k) * t.slideDistance, 0f, 0f);
    }
}
