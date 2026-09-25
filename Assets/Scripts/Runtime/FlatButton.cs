using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The one button. Primary, secondary, quiet, icon and tab are variants of it rather
/// than five components, so every button in the game changes state the same way and in the
/// same time.
///
/// It is a <see cref="Button"/>, so <c>onClick</c>, navigation and every existing check
/// that looks for a Button still work. It sets the base transition to None and does the
/// work itself, because a colour tint can only multiply one graphic and a flat button has
/// three things to change: its face, its border and the colour of what is on it.
///
/// <b>Unscaled time</b>, like <see cref="HoverCard"/>: the pause menu and the results
/// screen are both shown with the clock stopped.
///
/// <b>The face is the raycast target and nothing else is.</b> The same rule
/// <c>FPSKitMenuBuilder.MakeButton</c> exists for: a button whose own graphic is not a
/// raycast target is inert without a word, and a label that is one steals the click.
/// </summary>
public class FlatButton : Button
{
    public enum Variant { Primary, Secondary, Quiet, Icon, Tab }
    public enum Look { Normal, Hover, Pressed, Disabled }

    [Header("Flat button")]
    [SerializeField] Variant _variant = Variant.Secondary;

    [Tooltip("The rectangle that is the button. Also the raycast target.")]
    public FlatRect face;

    [Tooltip("The caption. Optional: an icon button has none.")]
    public TMP_Text label;

    [Tooltip("The icon. Optional.")]
    public Image icon;

    [Tooltip("Which theme to read. Empty means the active one.")]
    public UITheme theme;

    [Tooltip("Tabs only: this is the tab that is showing.")]
    [SerializeField] bool _selected;

    [Tooltip("Tabs only: which edge carries the selection line. The bottom for a bar across " +
             "the top, the left for a rail down the side, so the line always faces the screen " +
             "the tab opens.")]
    public FlatRect.Side tabEdge = FlatRect.Side.Bottom;

    [Tooltip("Hold one look whatever the pointer does. For the component gallery, which has " +
             "to show all four states side by side.")]
    public bool holdLook;
    public Look heldLook;

    Look _look;
    Color _fill, _border, _content, _stripe;
    Color _fromFill, _fromBorder, _fromContent, _fromStripe;
    Color _toFill, _toBorder, _toContent, _toStripe;
    float _t = 1f;

    UITheme Theme => theme != null ? theme : UITheme.Active;

    public Variant variant { get => _variant; set { _variant = value; Retarget(true); } }

    /// <summary>Tabs: whether this one is showing. Other variants ignore it.</summary>
    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Retarget(!Application.isPlaying); }
    }

    /// <summary>What the button currently looks like, for tests.</summary>
    public Look CurrentLook => _look;

    protected override void Awake()
    {
        transition = Transition.None;
        base.Awake();
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        base.DoStateTransition(state, instant);
        // Selected is where the EventSystem leaves a button after a mouse click, so treating
        // it as hover would leave the last button pressed lit after the pointer had gone.
        _look = holdLook ? heldLook : state switch
        {
            SelectionState.Highlighted => Look.Hover,
            SelectionState.Pressed => Look.Pressed,
            SelectionState.Disabled => Look.Disabled,
            _ => Look.Normal,
        };
        Retarget(instant || !Application.isPlaying);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        transition = Transition.None;
        base.OnValidate();
        if (holdLook) { _look = heldLook; Retarget(true); }
    }
#endif

    /// <summary>The face, border, content and stripe colours for a variant in a look.</summary>
    public static void Colors(UITheme t, Variant v, Look look, bool selected,
                              out Color fill, out Color border, out Color content, out Color stripe)
    {
        var clear = new Color(0, 0, 0, 0);
        stripe = clear;
        switch (v)
        {
            case Variant.Primary:
                fill = look switch { Look.Hover => t.accentHover, Look.Pressed => t.accentPressed, Look.Disabled => t.panelRaised, _ => t.accent };
                border = look == Look.Disabled ? t.border : clear;
                content = look == Look.Disabled ? t.textDisabled : t.textOnAccent;
                break;

            case Variant.Quiet:
                fill = look switch { Look.Hover => t.panelRaised, Look.Pressed => t.background, _ => clear };
                border = clear;
                content = look switch { Look.Normal => t.textSecondary, Look.Disabled => t.textDisabled, _ => t.textPrimary };
                break;

            case Variant.Tab:
                fill = look == Look.Pressed ? t.panel : clear;
                border = clear;
                content = look == Look.Disabled ? t.textDisabled
                        : selected || look != Look.Normal ? t.textPrimary : t.textSecondary;
                stripe = look == Look.Disabled ? clear
                       : selected ? t.accent
                       : look == Look.Hover ? t.borderHover : clear;
                break;

            default: // Secondary and Icon share a face; an icon rests a step quieter.
                // Pressed drops to the background: a button sits on a panel, so going to the
                // panel's own colour would make it vanish into it rather than look pushed in.
                fill = look switch { Look.Hover => t.panelHover, Look.Pressed => t.background, Look.Disabled => t.panel, _ => t.panelRaised };
                border = look switch { Look.Hover => t.borderHover, Look.Pressed => t.borderHover, _ => t.border };
                content = look switch
                {
                    Look.Disabled => t.textDisabled,
                    Look.Normal when v == Variant.Icon => t.textSecondary,
                    _ => t.textPrimary,
                };
                break;
        }
    }

    void Retarget(bool instant)
    {
        var t = Theme;
        if (t == null) return;
        Colors(t, _variant, _look, _selected, out _toFill, out _toBorder, out _toContent, out _toStripe);

        if (instant || t.motionFast <= 0f)
        {
            _fill = _toFill; _border = _toBorder; _content = _toContent; _stripe = _toStripe;
            _t = 1f;
            Apply();
            return;
        }
        _fromFill = _fill; _fromBorder = _border; _fromContent = _content; _fromStripe = _stripe;
        _t = 0f;
    }

    void Update()
    {
        if (_t >= 1f) return;
        var t = Theme;
        _t = Mathf.Min(1f, _t + Time.unscaledDeltaTime / Mathf.Max(0.001f, t.motionFast));
        float e = UITheme.EaseOut(_t);
        _fill = Color.Lerp(_fromFill, _toFill, e);
        _border = Color.Lerp(_fromBorder, _toBorder, e);
        _content = Color.Lerp(_fromContent, _toContent, e);
        _stripe = Color.Lerp(_fromStripe, _toStripe, e);
        Apply();
    }

    void Apply()
    {
        if (face != null)
        {
            face.color = _fill;
            face.borderColor = _border;
            if (_variant == Variant.Tab)
            {
                face.stripeSide = tabEdge;
                face.stripePixels = Theme.selectionPixels;
                face.stripeColor = _stripe;
            }
        }
        if (label != null) label.color = _content;
        if (icon != null) icon.color = _content;
    }
}
