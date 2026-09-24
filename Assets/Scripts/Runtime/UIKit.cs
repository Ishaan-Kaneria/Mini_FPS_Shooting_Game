using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the interface's components in code: panels, buttons, icon buttons, tab bars,
/// progress bars, stat bars and toasts.
///
/// <b>This is the reusable unit, not a prefab.</b> Every screen in the game is constructed
/// in C# by a builder that regenerates its scene from scratch, so a prefab would be one more
/// asset for the builders to keep in step with -- and a hand edit to it would be erased by
/// the next build, exactly as a hand edit to a generated scene is. A screen built from these
/// calls cannot drift from the kit, because there is only one definition of each part.
///
/// It lives in the runtime assembly because two of the parts are made while the game is
/// running: the tooltip view, created the first time a tooltip shows, and every toast.
///
/// Each part comes back sized by a <see cref="LayoutElement"/> and positioned by nobody,
/// so it drops into a layout group; a caller placing it by hand sets its anchors itself.
/// Everything that is not the part's own click target has <c>raycastTarget</c> off, for
/// the reason <c>FPSKitMenuBuilder.MakeButton</c> gives.
/// </summary>
public static class UIKit
{
    /// <summary>The text roles the type scale defines.</summary>
    public enum TextRole
    {
        /// <summary>The game's name, a results headline. Condensed, uppercase.</summary>
        Display,
        /// <summary>A screen title. Condensed, uppercase.</summary>
        Title,
        /// <summary>A panel heading. Condensed, uppercase, spaced.</summary>
        Heading,
        /// <summary>A small uppercase, letter-spaced label: a column header, a unit, a caption on a control.</summary>
        Label,
        /// <summary>Running text: a description, an objective.</summary>
        Body,
        /// <summary>Small secondary text.</summary>
        Caption,
        /// <summary>A counter: condensed, tabular.</summary>
        Number,
    }

    /// <summary>Minimum height of anything pressable, in reference units. 44 is the smallest a
    /// pointer target gets before it starts being missed at 720p.</summary>
    public const float ControlHeight = 44f;

    // ==================================================================
    // Primitives.
    // ==================================================================

    public static RectTransform Rect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent != null ? parent.gameObject.layer : 5;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    /// <summary>Stretches a rect over its parent, inset by the given margins.</summary>
    public static RectTransform Fill(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
        return rt;
    }

    /// <summary>Places a rect by fractions of its parent -- the layout rule the menus already follow.</summary>
    public static RectTransform Anchor(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
    {
        rt.anchorMin = new Vector2(xMin, yMin);
        rt.anchorMax = new Vector2(xMax, yMax);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    public static LayoutElement Size(Component c, float width = -1, float height = -1, float flexWidth = -1, float flexHeight = -1)
    {
        var le = c.GetComponent<LayoutElement>();
        if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
        if (width >= 0) { le.minWidth = width; le.preferredWidth = width; }
        if (height >= 0) { le.minHeight = height; le.preferredHeight = height; }
        if (flexWidth >= 0) le.flexibleWidth = flexWidth;
        if (flexHeight >= 0) le.flexibleHeight = flexHeight;
        return le;
    }

    public static HorizontalLayoutGroup Row(Component c, float spacing, RectOffset padding = null,
                                            TextAnchor align = TextAnchor.MiddleLeft)
    {
        var g = c.gameObject.AddComponent<HorizontalLayoutGroup>();
        g.spacing = spacing;
        g.padding = padding ?? new RectOffset();
        g.childAlignment = align;
        g.childControlWidth = g.childControlHeight = true;
        g.childForceExpandWidth = g.childForceExpandHeight = false;
        return g;
    }

    public static VerticalLayoutGroup Column(Component c, float spacing, RectOffset padding = null,
                                             TextAnchor align = TextAnchor.UpperLeft)
    {
        var g = c.gameObject.AddComponent<VerticalLayoutGroup>();
        g.spacing = spacing;
        g.padding = padding ?? new RectOffset();
        g.childAlignment = align;
        g.childControlWidth = g.childControlHeight = true;
        g.childForceExpandWidth = true;
        g.childForceExpandHeight = false;
        return g;
    }

    // ==================================================================
    // Text and icons.
    // ==================================================================

    public static TextMeshProUGUI Text(Transform parent, string name, string text, TextRole role,
                                       UITheme t = null, bool hud = false)
    {
        t = t != null ? t : UITheme.Active;
        var rt = Rect(parent, name);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.color = t.textPrimary;

        bool condensed = role is TextRole.Display or TextRole.Title or TextRole.Heading or TextRole.Label or TextRole.Number;
        tmp.font = condensed ? t.headingFont : t.bodyFont;
        if (tmp.font == null) tmp.font = TMP_Settings.defaultFontAsset;
        if (hud)
        {
            var m = condensed ? t.hudHeadingMaterial : t.hudBodyMaterial;
            if (m != null) tmp.fontSharedMaterial = m;
        }

        switch (role)
        {
            case TextRole.Display: tmp.fontSize = t.sizeDisplay; tmp.fontStyle = FontStyles.UpperCase; tmp.characterSpacing = 2f; break;
            case TextRole.Title: tmp.fontSize = t.sizeTitle; tmp.fontStyle = FontStyles.UpperCase; tmp.characterSpacing = 3f; break;
            case TextRole.Heading: tmp.fontSize = t.sizeHeading; tmp.fontStyle = FontStyles.UpperCase; tmp.characterSpacing = t.labelSpacing * 0.6f; break;
            case TextRole.Label:
                tmp.fontSize = t.sizeLabel; tmp.fontStyle = FontStyles.UpperCase; tmp.characterSpacing = t.labelSpacing;
                tmp.color = t.textSecondary;
                break;
            case TextRole.Body: tmp.fontSize = t.sizeBody; tmp.textWrappingMode = TextWrappingModes.Normal; break;
            case TextRole.Caption: tmp.fontSize = t.sizeCaption; tmp.color = t.textSecondary; tmp.textWrappingMode = TextWrappingModes.Normal; break;
            case TextRole.Number: tmp.fontSize = t.sizeHeading; break;
        }
        tmp.text = role == TextRole.Number ? t.Tabular(text) : text;
        tmp.gameObject.AddComponent<UITextFloor>();
        return tmp;
    }

    public static Image Icon(Transform parent, string name, string iconId, float size, Color color, UITheme t = null)
    {
        t = t != null ? t : UITheme.Active;
        var rt = Rect(parent, name);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = t.IconSprite(iconId);
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.color = color;
        rt.sizeDelta = new Vector2(size, size);
        Size(img, size, size);
        return img;
    }

    // ==================================================================
    // Panels.
    // ==================================================================

    public enum PanelTone { Panel, Raised, Hud, Background }

    /// <summary>A solid panel with a one-pixel border. Not a raycast target: a panel is not a button.</summary>
    public static FlatRect Panel(Transform parent, string name, PanelTone tone = PanelTone.Panel, UITheme t = null)
    {
        t = t != null ? t : UITheme.Active;
        var rt = Rect(parent, name);
        var r = rt.gameObject.AddComponent<FlatRect>();
        r.raycastTarget = false;
        r.color = tone switch
        {
            PanelTone.Raised => t.panelRaised,
            PanelTone.Hud => t.HudPanel,
            PanelTone.Background => t.background,
            _ => t.panel,
        };
        r.borderColor = tone == PanelTone.Background ? new Color(0, 0, 0, 0) : t.border;
        r.borderPixels = t.borderPixels;
        return r;
    }

    /// <summary>A panel with an uppercase heading and a column under it for content.</summary>
    public static RectTransform Section(Transform parent, string name, string heading, out RectTransform body,
                                        PanelTone tone = PanelTone.Panel, UITheme t = null)
    {
        t = t != null ? t : UITheme.Active;
        var panel = Panel(parent, name, tone, t);
        Column(panel, 16f, new RectOffset(24, 24, 20, 24));
        Text(panel.transform, "Heading", heading, TextRole.Heading, t);
        body = Rect(panel.transform, "Body");
        Column(body, 12f);
        return panel.rectTransform;
    }

    // ==================================================================
    // Buttons.
    // ==================================================================

    /// <summary>A text button, optionally with a leading icon.</summary>
    public static FlatButton Button(Transform parent, string name, string caption,
                                    FlatButton.Variant variant = FlatButton.Variant.Secondary,
                                    string iconId = null, UITheme t = null)
    {
        t = t != null ? t : UITheme.Active;
        var face = Panel(parent, name, PanelTone.Raised, t);
        face.raycastTarget = true;
        var pad = variant == FlatButton.Variant.Tab ? new RectOffset(16, 16, 0, 0) : new RectOffset(22, 22, 0, 0);
        Row(face, 10f, pad, TextAnchor.MiddleCenter);
        Size(face, height: variant == FlatButton.Variant.Tab ? 48f : ControlHeight);

        Image icon = null;
        if (!string.IsNullOrEmpty(iconId)) icon = Icon(face.transform, "Icon", iconId, 20f, t.textPrimary, t);

        var label = Text(face.transform, "Label", caption, TextRole.Label, t);
        label.fontSize = variant == FlatButton.Variant.Tab ? t.sizeLabel + 2f : t.sizeLabel + 1f;
        label.alignment = TextAlignmentOptions.Center;

        var b = face.gameObject.AddComponent<FlatButton>();
        b.face = face;
        b.label = label;
        b.icon = icon;
        b.targetGraphic = face;
        b.variant = variant;
        return b;
    }

    /// <summary>A square button with only an icon on it, and the tooltip that names it.</summary>
    public static FlatButton IconButton(Transform parent, string name, string iconId, string tooltip,
                                        float size = ControlHeight, UITheme t = null)
    {
        t = t != null ? t : UITheme.Active;
        var face = Panel(parent, name, PanelTone.Raised, t);
        face.raycastTarget = true;
        Size(face, size, size);
        face.rectTransform.sizeDelta = new Vector2(size, size);

        var icon = Icon(face.transform, "Icon", iconId, Mathf.Round(size * 0.5f), t.textSecondary, t);
        var irt = icon.rectTransform;
        irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
        irt.anchoredPosition = Vector2.zero;

        var b = face.gameObject.AddComponent<FlatButton>();
        b.face = face;
        b.icon = icon;
        b.targetGraphic = face;
        b.variant = FlatButton.Variant.Icon;

        var tip = face.gameObject.AddComponent<UITooltip>();
        tip.text = tooltip;
        return b;
    }

    /// <summary>A row of tabs over a one-pixel rule. The first tab starts selected.</summary>
    public static UITabBar TabBar(Transform parent, string name, string[] captions, UITheme t = null)
    {
        t = t != null ? t : UITheme.Active;
        var bar = Rect(parent, name);
        var rule = bar.gameObject.AddComponent<FlatRect>();
        rule.raycastTarget = false;
        rule.color = new Color(0, 0, 0, 0);
        rule.stripeSide = FlatRect.Side.Bottom;
        rule.stripeColor = t.border;
        rule.stripePixels = t.borderPixels;
        Row(bar, 4f);
        Size(bar, height: 48f);

        var tabs = new FlatButton[captions.Length];
        for (int i = 0; i < captions.Length; i++)
            tabs[i] = Button(bar, "Tab_" + captions[i], captions[i], FlatButton.Variant.Tab, null, t);

        var tb = bar.gameObject.AddComponent<UITabBar>();
        tb.tabs = tabs;
        tb.Selected = 0;
        return tb;
    }

    // ==================================================================
    // Bars.
    // ==================================================================

    /// <summary>A flat track and fill. With a count, the count sits to the right of the bar.</summary>
    public static UIProgressBar ProgressBar(Transform parent, string name, Color fillColor,
                                            float height = 6f, bool withCount = false, UITheme t = null)
    {
        t = t != null ? t : UITheme.Active;
        var root = Rect(parent, name);
        Row(root, 12f, null, TextAnchor.MiddleLeft);
        Size(root, height: Mathf.Max(height, withCount ? 24f : height));

        var track = root.gameObject.AddComponent<UIProgressBar>();
        var trackRect = Rect(root, "Track");
        var tr = trackRect.gameObject.AddComponent<FlatRect>();
        tr.raycastTarget = false;
        tr.color = t.background;
        tr.borderColor = t.border;
        tr.borderPixels = t.borderPixels;
        Size(tr, height: height, flexWidth: 1f);

        var fillRect = Rect(trackRect, "Fill");
        var fr = fillRect.gameObject.AddComponent<FlatRect>();
        fr.raycastTarget = false;
        fr.color = fillColor;

        track.track = tr;
        track.fill = fr;
        if (withCount)
        {
            var count = Text(root, "Count", "0 / 0", TextRole.Number, t);
            count.fontSize = t.sizeLabel + 2f;
            count.color = t.textSecondary;
            count.alignment = TextAlignmentOptions.MidlineRight;
            track.count = count;
        }
        return track;
    }

    /// <summary>Icon, bar, number.</summary>
    public static UIStatBar StatBar(Transform parent, string name, string iconId, Color color,
                                    bool hud = false, UITheme t = null)
    {
        t = t != null ? t : UITheme.Active;
        var root = Rect(parent, name);
        Row(root, 12f, null, TextAnchor.MiddleLeft);
        Size(root, height: 28f);

        var sb = root.gameObject.AddComponent<UIStatBar>();
        sb.icon = Icon(root, "Icon", iconId, 22f, color, t);
        sb.bar = ProgressBar(root, "Bar", color, 8f, false, t);
        Size(sb.bar, flexWidth: 1f);
        sb.number = Text(root, "Number", "0", TextRole.Number, t, hud);
        sb.number.fontSize = t.sizeHeading + 2f;
        sb.number.alignment = TextAlignmentOptions.MidlineRight;
        Size(sb.number, width: 56f);
        sb.color = color;
        return sb;
    }

    // ==================================================================
    // Settings controls.
    // ==================================================================

    /// <summary>
    /// A settings row: the label on the left, the control on the right, one line high. The
    /// row is not itself selectable -- the control is -- so the focus ring outlines the thing
    /// that changes, and a pad walks straight down the controls.
    /// </summary>
    public static RectTransform SettingRow(Transform parent, string name, string label, string hint,
                                           out RectTransform controlSlot, UITheme t = null)
    {
        t = t != null ? t : UITheme.Active;
        var row = Rect(parent, name);
        var h = Row(row, 24f, new RectOffset(0, 0, 6, 6), TextAnchor.MiddleLeft);
        h.childForceExpandHeight = false;
        Size(row, height: string.IsNullOrEmpty(hint) ? 56f : 70f);

        var text = Rect(row, "Text");
        Column(text, 2f);
        Size(text, flexWidth: 1f);
        var l = Text(text, "Label", label, TextRole.Body, t);
        l.textWrappingMode = TextWrappingModes.NoWrap;
        if (!string.IsNullOrEmpty(hint))
        {
            var c = Text(text, "Hint", hint, TextRole.Caption, t);
            c.textWrappingMode = TextWrappingModes.NoWrap;
            c.overflowMode = TextOverflowModes.Ellipsis;
        }

        controlSlot = Rect(row, "Control");
        Size(controlSlot, 300f, 44f);
        return row;
    }

    public static UISwitch Switch(Transform parent, string name, UITheme t = null)
    {
        t = t != null ? t : UITheme.Active;
        var root = Rect(parent, name);
        Fill(root);
        var r = Row(root, 12f, null, TextAnchor.MiddleRight);
        r.childForceExpandHeight = false;

        var state = Text(root, "State", "OFF", TextRole.Label, t);
        state.alignment = TextAlignmentOptions.MidlineRight;
        Size(state, 48f);

        var track = Panel(root, "Track", PanelTone.Raised, t);
        track.raycastTarget = true;
        Size(track, 64f, 32f);
        var knob = Rect(track.transform, "Knob").gameObject.AddComponent<FlatRect>();
        knob.raycastTarget = false;

        var sw = track.gameObject.AddComponent<UISwitch>();
        sw.track = track;
        sw.knob = knob;
        sw.stateLabel = state;
        sw.targetGraphic = track;
        sw.IsOn = false;
        return sw;
    }

    /// <summary>A flat slider: a thin track, an amber fill, a square handle, and its value to the right.</summary>
    public static Slider Slider(Transform parent, string name, float min, float max, out TMP_Text valueText, UITheme t = null)
    {
        t = t != null ? t : UITheme.Active;
        var root = Rect(parent, name);
        Fill(root);
        var r = Row(root, 14f, null, TextAnchor.MiddleRight);
        r.childForceExpandHeight = false;

        var sliderRect = Rect(root, "Slider");
        Size(sliderRect, height: 32f, flexWidth: 1f);
        var hit = sliderRect.gameObject.AddComponent<FlatRect>();
        hit.color = new Color(0, 0, 0, 0);
        hit.raycastTarget = true;

        var track = Rect(sliderRect, "Track");
        track.anchorMin = new Vector2(0, 0.5f); track.anchorMax = new Vector2(1, 0.5f);
        track.sizeDelta = new Vector2(0, 6f);
        var tr = track.gameObject.AddComponent<FlatRect>();
        tr.raycastTarget = false; tr.color = t.background; tr.borderColor = t.border; tr.borderPixels = t.borderPixels;

        var fillArea = Rect(sliderRect, "FillArea");
        fillArea.anchorMin = new Vector2(0, 0.5f); fillArea.anchorMax = new Vector2(1, 0.5f);
        fillArea.sizeDelta = new Vector2(0, 6f);
        var fill = Rect(fillArea, "Fill");
        fill.sizeDelta = Vector2.zero;
        var fr = fill.gameObject.AddComponent<FlatRect>();
        fr.raycastTarget = false; fr.color = t.accent;

        var handleArea = Rect(sliderRect, "HandleArea");
        Fill(handleArea, 8f, 0f, 8f, 0f);
        var handle = Rect(handleArea, "Handle");
        handle.sizeDelta = new Vector2(16f, 24f);
        var hr = handle.gameObject.AddComponent<FlatRect>();
        hr.raycastTarget = false; hr.color = t.textPrimary;

        var slider = sliderRect.gameObject.AddComponent<Slider>();
        slider.transition = Selectable.Transition.None;
        slider.targetGraphic = hit;
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;

        valueText = Text(root, "Value", "", TextRole.Number, t);
        valueText.fontSize = t.sizeBody + 2f;
        valueText.alignment = TextAlignmentOptions.MidlineRight;
        Size(valueText, 64f);
        return slider;
    }

    public static UIChoice Choice(Transform parent, string name, string[] options, UITheme t = null)
    {
        t = t != null ? t : UITheme.Active;
        var face = Panel(parent, name, PanelTone.Background, t);
        face.raycastTarget = true;
        face.borderColor = t.border;
        face.borderPixels = t.borderPixels;
        Fill(face.rectTransform);
        Row(face, 0f, null, TextAnchor.MiddleCenter);

        Button Arrow(string id, float rotate)
        {
            var b = Rect(face.transform, id);
            Size(b, 44f, 44f);
            var hit = b.gameObject.AddComponent<FlatRect>();
            hit.color = new Color(0, 0, 0, 0);
            hit.raycastTarget = true;
            var icon = Icon(b, "Icon", "chevron-right", 20f, t.textSecondary, t);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            icon.rectTransform.localEulerAngles = new Vector3(0, 0, rotate);
            var button = b.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            // Clickable, never focusable: the row is the stop.
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return button;
        }

        var prev = Arrow("Previous", 180f);
        var value = Text(face.transform, "Value", "", TextRole.Label, t);
        value.fontSize = t.sizeLabel + 1f;
        value.alignment = TextAlignmentOptions.Center;
        Size(value, flexWidth: 1f);
        var next = Arrow("Next", 0f);

        var choice = face.gameObject.AddComponent<UIChoice>();
        choice.face = face;
        choice.valueLabel = value;
        choice.previous = prev;
        choice.next = next;
        choice.targetGraphic = face;
        choice.options = options;
        choice.Index = 0;
        return choice;
    }

    // ==================================================================
    // Toasts and tooltips.
    // ==================================================================

    /// <summary>A column for toasts, anchored to the top-right of its parent.</summary>
    public static UIToastStack ToastStack(Transform parent, string name = "Toasts", UITheme t = null)
    {
        var rt = Rect(parent, name);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(380f, 0f);
        var col = Column(rt, 8f, null, TextAnchor.UpperRight);
        col.childForceExpandWidth = true;
        var fit = rt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var s = rt.gameObject.AddComponent<UIToastStack>();
        s.theme = t;
        return s;
    }

    /// <summary>One toast card. Its contents are filled by <see cref="UIToast.Set"/>.</summary>
    public static UIToast Toast(Transform parent, UITheme t = null)
    {
        t = t != null ? t : UITheme.Active;
        var root = Rect(parent, "Toast");
        // The root is the layout's; the card inside it is the toast's to move.
        var holder = root.gameObject.AddComponent<VerticalLayoutGroup>();
        holder.childControlWidth = holder.childControlHeight = true;
        holder.childForceExpandWidth = true;
        holder.childForceExpandHeight = false;

        var face = Panel(root, "Card", PanelTone.Raised, t);
        face.stripeSide = FlatRect.Side.Left;
        face.stripePixels = t.stripePixels;
        face.stripeColor = t.info;
        var group = face.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        Row(face, 14f, new RectOffset(18, 18, 14, 14), TextAnchor.UpperLeft);

        var icon = Icon(face.transform, "Icon", "info-circle", 24f, t.info, t);
        var text = Rect(face.transform, "Text");
        Column(text, 4f);
        Size(text, flexWidth: 1f);
        var title = Text(text, "Title", "", TextRole.Heading, t);
        title.fontSize = t.sizeLabel + 3f;
        var body = Text(text, "Body", "", TextRole.Caption, t);
        body.fontSize = t.sizeCaption + 2f;

        var toast = root.gameObject.AddComponent<UIToast>();
        toast.card = face.rectTransform;
        toast.group = group;
        toast.face = face;
        toast.icon = icon;
        toast.title = title;
        toast.body = body;
        return toast;
    }

    /// <summary>The shared tooltip panel for a canvas. Called by <see cref="UITooltipView.For"/>.</summary>
    public static UITooltipView TooltipView(Transform canvasRoot, UITheme t = null)
    {
        t = t != null ? t : UITheme.Active;
        var face = Panel(canvasRoot, "Tooltip", PanelTone.Raised, t);
        face.borderColor = t.borderHover;
        var rt = face.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        Row(face, 0f, new RectOffset(10, 10, 6, 7), TextAnchor.MiddleCenter);
        var fit = face.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var group = face.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 0f;

        var label = Text(face.transform, "Label", "", TextRole.Caption, t);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.color = t.textPrimary;
        label.fontSize = t.sizeCaption + 1f;

        face.gameObject.AddComponent<SafeAreaBleed>();
        var view = face.gameObject.AddComponent<UITooltipView>();
        view.label = label;
        view.group = group;
        face.gameObject.SetActive(false);
        return view;
    }
}
