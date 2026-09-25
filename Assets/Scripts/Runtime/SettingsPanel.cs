using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SETTINGS: controls, touch and display, over whatever screen opened it -- the dashboard
/// or the pause menu. Every value is written the moment it changes and applied by whatever
/// owns it (<see cref="GameSettings.Changed"/>), so there is no Apply button and nothing to
/// lose by backing out.
///
/// <b>Built at runtime from the kit</b>, the first time it is asked for, onto the canvas that
/// asked. The dashboard and all seven arenas therefore get the same screen without any of
/// their scenes carrying a copy, and a new setting is one row here rather than a rebuild of
/// eight scenes.
///
/// Rows only appear where they mean something: the touch tab on a device with a touch
/// screen, frame rate and battery saver on a phone or tablet. A setting that does nothing
/// on the device in the player's hand is a setting they try, see nothing happen, and then
/// distrust the rest of the screen over.
/// </summary>
public class SettingsPanel : OverlayPanel
{
    readonly List<RectTransform> _pages = new List<RectTransform>();
    readonly List<Action> _refreshers = new List<Action>();
    UITabBar _tabs;
    UIDefaultSelection _default;

    /// <summary>Opens the settings over a canvas, building them there the first time.</summary>
    public static SettingsPanel Show(Canvas anyCanvas)
    {
        if (anyCanvas == null) return null;
        var root = anyCanvas.rootCanvas;
        var panel = root.GetComponent<SettingsPanel>();
        if (panel == null)
        {
            panel = root.gameObject.AddComponent<SettingsPanel>();
            panel.Build();
        }
        panel.Open();
        return panel;
    }

    protected override void OnOpened()
    {
        panel.transform.SetAsLastSibling();
        foreach (var r in _refreshers) r();
        ShowPage(_tabs != null ? _tabs.Selected : 0);
    }

    // ==================================================================
    // Construction.
    // ==================================================================

    void Build()
    {
        var t = UITheme.Active;
        var canvas = (RectTransform)transform;

        // The shade: full-bleed, and the raycast target that makes everything behind it
        // unreachable -- which is also what tells the navigator the screen underneath is
        // not where the focus belongs.
        var shade = UIKit.Panel(canvas, "Settings", UIKit.PanelTone.Background, t);
        shade.color = new Color(t.background.r, t.background.g, t.background.b, 0.94f);
        shade.raycastTarget = true;
        UIKit.Fill(shade.rectTransform);
        panel = shade.gameObject;

        // The card inside the safe area; the shade behind it runs to the physical edge.
        var safe = UIKit.Rect(shade.transform, "SafeArea");
        UIKit.Fill(safe);
        var fitter = safe.gameObject.AddComponent<SafeAreaFitter>();
        fitter.paddingMm = 0f;
        fitter.Apply();

        bool handset = DeviceProfile.CurrentForm == DeviceProfile.Form.Handset;
        var card = UIKit.Panel(safe, "Card", UIKit.PanelTone.Panel, t);
        UIKit.Anchor(card.rectTransform, handset ? 0.03f : 0.18f, 0.05f, handset ? 0.97f : 0.82f, 0.95f);
        var col = UIKit.Column(card, 12f, new RectOffset(32, 32, 24, 24));
        col.childForceExpandHeight = false;

        // Header.
        var header = UIKit.Rect(card.transform, "Header");
        UIKit.Row(header, 16f, null, TextAnchor.MiddleLeft);
        UIKit.Size(header, height: 48f);
        var title = UIKit.Text(header, "Title", "Settings", UIKit.TextRole.Title, t);
        UIKit.Size(title, flexWidth: 1f);
        var close = UIKit.IconButton(header, "Close", "x", "Close", UIKit.ControlHeight, t);
        close.onClick.AddListener(Close);

        // Tabs.
        var names = new List<string> { "Controls" };
        bool touch = DeviceProfile.Touched;
        if (touch) names.Add("Touch");
        names.Add("Display");
        names.Add("HUD");
        _tabs = UIKit.TabBar(card.transform, "Tabs", names.ToArray(), t);
        _tabs.onChanged.AddListener(ShowPage);

        // Pages, in one scrolling viewport.
        var viewport = UIKit.Rect(card.transform, "Viewport");
        UIKit.Size(viewport, flexHeight: 1f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.viewport = viewport;
        scroll.scrollSensitivity = 30f;
        var hit = viewport.gameObject.AddComponent<FlatRect>();
        hit.color = new Color(0, 0, 0, 0);
        hit.raycastTarget = true;

        var content = UIKit.Rect(viewport, "Content");
        content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
        content.offsetMin = content.offsetMax = Vector2.zero;
        var ccol = UIKit.Column(content, 0f);
        ccol.childForceExpandHeight = false;
        var fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;

        BuildControls(Page(content, "Controls"), t);
        if (touch) BuildTouch(Page(content, "Touch"), t);
        BuildDisplay(Page(content, "Display"), t);
        BuildHud(Page(content, "HUD"), t);

        // Footer.
        var footer = UIKit.Rect(card.transform, "Footer");
        UIKit.Row(footer, 12f, null, TextAnchor.MiddleRight);
        UIKit.Size(footer, height: UIKit.ControlHeight);
        var reset = UIKit.Button(footer, "Reset", "Reset to defaults", FlatButton.Variant.Quiet, "refresh", t);
        reset.onClick.AddListener(() =>
        {
            GameSettings.ResetAll();
            foreach (var r in _refreshers) r();
        });
        var gap = UIKit.Rect(footer, "Gap");
        UIKit.Size(gap, flexWidth: 1f);
        var done = UIKit.Button(footer, "Done", "Done", FlatButton.Variant.Primary, "check", t);
        done.onClick.AddListener(Close);
        backButton = done;

        _default = shade.gameObject.AddComponent<UIDefaultSelection>();
        _default.priority = 100;
        panel.SetActive(false);
    }

    RectTransform Page(RectTransform content, string name)
    {
        var page = UIKit.Rect(content, name);
        var c = UIKit.Column(page, 0f);
        c.childForceExpandHeight = false;
        _pages.Add(page);
        return page;
    }

    void ShowPage(int index)
    {
        for (int i = 0; i < _pages.Count; i++) _pages[i].gameObject.SetActive(i == index);

        // The pad lands on the first control of the page it is looking at.
        if (_default != null) Destroy(_default);
        if (index < 0 || index >= _pages.Count) return;
        var first = _pages[index].GetComponentInChildren<Selectable>();
        if (first == null) return;
        _default = first.gameObject.AddComponent<UIDefaultSelection>();
        _default.priority = 100;
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es != null && GameInput.UsingGamepad) es.SetSelectedGameObject(first.gameObject);
    }

    // ==================================================================
    // Pages.
    // ==================================================================

    void BuildControls(RectTransform page, UITheme t)
    {
        Heading(page, "Look", t);
        AddSwitch(page, "Invert look", "Push up to look down.", () => GameSettings.InvertY, v => GameSettings.InvertY = v, t);

        Heading(page, "Aim assist", t);
        AddSwitch(page, "Aim assist", "Gamepad and touch only. A mouse never gets any.",
                  () => GameSettings.AimAssist, v => GameSettings.AimAssist = v, t);
        AddSlider(page, "Strength", "How much the view slows near a target.", 0f, 1f,
                  () => GameSettings.AimAssistStrength, v => GameSettings.AimAssistStrength = v, Percent, t);

        Heading(page, "Gamepad", t);
        AddSlider(page, "Look sensitivity", "Right stick turn speed.", 0.2f, 3f,
                  () => GameSettings.GamepadSensitivity, v => GameSettings.GamepadSensitivity = v, Times, t);
        AddSlider(page, "Stick deadzone", "Travel ignored around the centre.", 0.05f, 0.4f,
                  () => GameSettings.StickDeadzone, v => GameSettings.StickDeadzone = v, Percent, t);
        AddSlider(page, "Response curve", "Higher is finer near the centre.", 1f, 3f,
                  () => GameSettings.StickCurve, v => GameSettings.StickCurve = v, v => v.ToString("0.0"), t);
    }

    void BuildHud(RectTransform page, UITheme t)
    {
        Heading(page, "Layout", t);
        UIKit.SettingRow(page, "Customize", "Customize layout",
            DeviceProfile.Touched ? "Move, resize and hide every panel and button, and style the crosshair."
                                  : "Move, resize and hide every panel, and style the crosshair.",
            out var slot, t);
        var open = UIKit.Button(slot, "Customize", "Customize layout", FlatButton.Variant.Primary, "layout", t);
        open.onClick.AddListener(() =>
        {
            // The editor needs the HUD under it: in an arena it opens there, from the menu it
            // loads the current mission's arena frozen.
            Close();
            HudEditor.OpenAnywhere();
        });
        UIKit.SettingRow(page, "ResetLayout", "Reset layout", "Put every element back where it started, on this device.", out var slot2, t);
        var reset = UIKit.Button(slot2, "Reset", "Reset layout", FlatButton.Variant.Secondary, "refresh", t);
        reset.onClick.AddListener(HudLayout.Clear);
    }

    void BuildTouch(RectTransform page, UITheme t)
    {
        Heading(page, "Look", t);
        AddSlider(page, "Look sensitivity", "Drag speed on the right of the screen.", 0.2f, 3f,
                  () => GameSettings.TouchSensitivity, v => GameSettings.TouchSensitivity = v, Times, t);
        AddSwitch(page, "Gyro aiming", "Tilt the device to fine-tune your aim.",
                  () => GameSettings.Gyro, v => GameSettings.Gyro = v, t);
        AddSlider(page, "Gyro sensitivity", null, 0.2f, 3f,
                  () => GameSettings.GyroSensitivity, v => GameSettings.GyroSensitivity = v, Times, t);

        Heading(page, "Buttons", t);
        AddChoice(page, "Aim button", "Hold to aim, or tap to switch it on and off.", new[] { "Hold", "Tap" },
                  () => (int)GameSettings.TouchAimMode, i => GameSettings.TouchAimMode = (GameSettings.AimMode)i, t);
        AddSwitch(page, "Auto-fire", "Fires while the crosshair is on an enemy.",
                  () => GameSettings.AutoFire, v => GameSettings.AutoFire = v, t);
        AddSwitch(page, "Left-handed layout", "Stick on the right, buttons on the left.",
                  () => GameSettings.LeftHanded, v => GameSettings.LeftHanded = v, t);
        AddSlider(page, "Button opacity", null, 0.25f, 1f,
                  () => GameSettings.TouchOpacity, v => GameSettings.TouchOpacity = v, Percent, t);
    }

    void BuildDisplay(RectTransform page, UITheme t)
    {
        Heading(page, "Interface", t);
        var scales = new[] { 0.8f, 0.9f, 1f, 1.1f, 1.2f, 1.3f };
        // Steps rather than a slider: the interface rescales as the value changes, and a
        // slider being dragged would move out from under the finger dragging it.
        AddChoice(page, "Interface scale", "Size of menus and the HUD.",
                  Array.ConvertAll(scales, s => Mathf.RoundToInt(s * 100) + "%"),
                  () => Nearest(scales, GameSettings.UiScale), i => GameSettings.UiScale = scales[i], t);

        Heading(page, "Performance", t);
        AddChoice(page, "Quality", "Low turns off shadows and post-processing.", new[] { "Low", "Medium", "High" },
                  () => (int)QualityTiers.Current, i => GameSettings.QualityTier = i, t);
        if (QualityTiers.Handheld || ScreenInfo.SimulatedMobile == true)
        {
            AddChoice(page, "Frame rate", "30 runs cooler and lasts longer.", new[] { "30", "60" },
                      () => GameSettings.FrameRate == 30 ? 0 : 1, i => GameSettings.FrameRate = i == 0 ? 30 : 60, t);
            AddSwitch(page, "Battery saver", "30fps, lower resolution, no post-processing.",
                      () => GameSettings.BatterySaver, v => GameSettings.BatterySaver = v, t);
        }
    }

    // ==================================================================
    // Rows.
    // ==================================================================

    static string Percent(float v) => Mathf.RoundToInt(v * 100f) + "%";
    static string Times(float v) => v.ToString("0.0") + "x";

    static int Nearest(float[] values, float v)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
            if (Mathf.Abs(values[i] - v) < Mathf.Abs(values[best] - v)) best = i;
        return best;
    }

    static void Heading(RectTransform page, string text, UITheme t)
    {
        var h = UIKit.Text(page, "Heading_" + text, text, UIKit.TextRole.Label, t);
        h.margin = new Vector4(0, 18, 0, 4);
        UIKit.Size(h, height: 44f);
    }

    void AddSwitch(RectTransform page, string label, string hint, Func<bool> get, Action<bool> set, UITheme t)
    {
        UIKit.SettingRow(page, label, label, hint, out var slot, t);
        var sw = UIKit.Switch(slot, "Switch", t);
        sw.Changed += set;
        _refreshers.Add(() => sw.IsOn = get());
    }

    void AddSlider(RectTransform page, string label, string hint, float min, float max,
                   Func<float> get, Action<float> set, Func<float, string> format, UITheme t)
    {
        UIKit.SettingRow(page, label, label, hint, out var slot, t);
        var slider = UIKit.Slider(slot, "Slider", min, max, out TMP_Text value, t);
        bool refreshing = false;
        slider.onValueChanged.AddListener(v =>
        {
            value.text = t.Tabular(format(v));
            if (!refreshing) set(v);
        });
        _refreshers.Add(() =>
        {
            refreshing = true;
            slider.value = get();
            value.text = t.Tabular(format(slider.value));
            refreshing = false;
        });
    }

    void AddChoice(RectTransform page, string label, string hint, string[] options,
                   Func<int> get, Action<int> set, UITheme t)
    {
        UIKit.SettingRow(page, label, label, hint, out var slot, t);
        var choice = UIKit.Choice(slot, "Choice", options, t);
        choice.Changed += set;
        _refreshers.Add(() => choice.Index = get());
    }
}
