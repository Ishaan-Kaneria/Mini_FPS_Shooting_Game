using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The HUD editor: the live HUD over a frozen arena, a grid, and every movable element
/// outlined so it can be picked up and put somewhere else.
///
///   drag            moves the element; it snaps to a 20-unit grid and to the safe area's edges
///   side panel      size 60-150%, opacity 20-100%, show/hide, reset; the crosshair; presets
///   footer          RESET ALL, CANCEL, SAVE
///
/// Nothing may leave the safe area or sit on the crosshair -- it is pushed back out -- and any
/// two visible elements that overlap are outlined red. Mouse and touch drag the outline; a pad
/// selects with the D-pad, moves with the left stick, sizes with the triggers, fades with the
/// right stick, hides with Y, resets with X, saves with Start and cancels with B.
///
/// It edits a copy (<see cref="HudLayout.Data"/>) and previews every change live on the real
/// HUD, so what the player sees while dragging is exactly what they will play with. Cancel
/// puts the kept layout back; Save keeps the copy for this device form.
///
/// An <see cref="OverlayPanel"/>, so Escape and B cancel it, and the same press does not also
/// resume the game underneath.
/// </summary>
public class HudEditor : OverlayPanel
{
    const float Grid = 20f;
    const float EdgeSnap = 24f;
    const float CrosshairZone = 170f;

    HUDController _hud;
    HudView _view;
    bool _fromMenu;
    UITheme _t;
    Canvas _canvas;
    RectTransform _root, _handles, _side;
    HudLayout.Data _work;
    readonly List<Handle> _all = new List<Handle>();
    Handle _selected;
    bool _navWas;

    // Side panel.
    TMP_Text _selName, _padHint;
    Slider _size, _opacity;
    TMP_Text _sizeValue, _opacityValue;
    UISwitch _visible, _secondFire;
    UIChoice _xStyle, _xColor;
    Slider _xSize, _xThick, _xGap;
    TMP_Text _xSizeValue, _xThickValue, _xGapValue;
    UISwitch _xOutline;
    readonly List<TMP_InputField> _slotNames = new List<TMP_InputField>();
    readonly List<RectTransform> _pages = new List<RectTransform>();
    bool _refreshing;

    // Moving: where the element was when this move began, and how far it has been pushed.
    Vector2 _moveStart, _moveRaw;
    bool _moving;

    class Handle
    {
        public HudLayoutTarget target;
        public FlatRect frame;
        public TMP_Text label;
        public Rect rect;
        public bool visible;
    }

    // ======================================================================

    public static HudEditor Open(HUDController hud, bool fromMenu)
    {
        if (hud == null) return null;
        var canvas = hud.GetComponentInParent<Canvas>();
        var host = new GameObject("HudEditor", typeof(RectTransform));
        host.transform.SetParent(canvas.transform, false);
        UIKit.Fill((RectTransform)host.transform);
        var own = host.AddComponent<Canvas>();
        own.overrideSorting = true;
        own.sortingOrder = 700;
        host.AddComponent<GraphicRaycaster>();
        var editor = host.AddComponent<HudEditor>();
        editor._hud = hud;
        editor._view = canvas.GetComponentInChildren<HudView>(true);
        editor._fromMenu = fromMenu;
        editor._canvas = canvas;
        if (editor._view != null) editor._view.SetEditing(true);
        editor.Build();
        editor.Open();
        return editor;
    }

    /// <summary>
    /// Settings' "Customize layout": the editor here if this is an arena, or an arena loaded
    /// frozen for it if this is the menu.
    /// </summary>
    public static void OpenAnywhere()
    {
        var hud = FindAnyObjectByType<HUDController>();
        if (hud != null)
        {
            Open(hud, fromMenu: false);
            return;
        }
        var menu = FindAnyObjectByType<MainMenuController>();
        if (menu != null) menu.LaunchHudEditor();
    }

    // ======================================================================
    // Building.
    // ======================================================================

    void Build()
    {
        _t = UITheme.Active;
        _root = (RectTransform)transform;
        _work = HudLayout.Current.Clone();

        // A clear sheet over the whole screen: it takes every touch, so dragging a fire
        // button in the editor moves it rather than firing it.
        var sheet = UIKit.Panel(_root, "Sheet", UIKit.PanelTone.Background, _t);
        sheet.color = new Color(0, 0, 0, 0.12f);
        sheet.borderColor = new Color(0, 0, 0, 0);
        sheet.raycastTarget = true;
        UIKit.Fill(sheet.rectTransform);
        panel = sheet.gameObject;

        BuildGrid(sheet.transform);

        var safeFrame = UIKit.Panel(sheet.transform, "SafeArea", UIKit.PanelTone.Background, _t);
        safeFrame.color = new Color(0, 0, 0, 0);
        safeFrame.borderColor = new Color(_t.info.r, _t.info.g, _t.info.b, 0.6f);
        UIKit.Fill(safeFrame.rectTransform);
        safeFrame.gameObject.AddComponent<SafeAreaFitter>().paddingMm = 0f;

        var zone = UIKit.Panel(sheet.transform, "CrosshairZone", UIKit.PanelTone.Background, _t);
        zone.color = new Color(_t.danger.r, _t.danger.g, _t.danger.b, 0.08f);
        zone.borderColor = new Color(_t.danger.r, _t.danger.g, _t.danger.b, 0.45f);
        var zrt = zone.rectTransform;
        zrt.anchorMin = zrt.anchorMax = zrt.pivot = new Vector2(0.5f, 0.5f);
        zrt.sizeDelta = Vector2.one * CrosshairZone;

        _handles = UIKit.Rect(sheet.transform, "Handles");
        UIKit.Fill(_handles);

        // The panel lives inside the safe area, like everything else the player has to reach.
        var safe = UIKit.Rect(sheet.transform, "PanelArea");
        UIKit.Fill(safe);
        safe.gameObject.AddComponent<SafeAreaFitter>().paddingMm = 1f;
        BuildSide(safe);
        BuildHandles();
        Select(_all.Count > 0 ? _all[0] : null);
        RefreshCrosshair();
        RefreshSlots();
    }

    void BuildGrid(Transform parent)
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "EditorGrid", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
            px[y * n + x] = x == 0 || y == 0 ? new Color32(255, 255, 255, 40) : new Color32(0, 0, 0, 0);
        tex.SetPixels32(px);
        tex.Apply();
        var grid = UIKit.Rect(parent, "Grid").gameObject.AddComponent<RawImage>();
        grid.texture = tex;
        grid.raycastTarget = false;
        UIKit.Fill(grid.rectTransform);
        // One cell per three grid steps, so the lines are there to read by and the snap is finer.
        grid.gameObject.AddComponent<GridTiler>().cell = Grid * 3f;
    }

    /// <summary>Keeps the grid texture tiled at a fixed size in canvas units, whatever the screen.</summary>
    class GridTiler : MonoBehaviour
    {
        public float cell = 60f;
        void LateUpdate()
        {
            var r = ((RectTransform)transform).rect;
            var raw = GetComponent<RawImage>();
            raw.uvRect = new Rect(0f, 0f, r.width / cell, r.height / cell);
        }
    }

    void BuildHandles()
    {
        foreach (Transform c in _handles) Destroy(c.gameObject);
        _all.Clear();
        bool touch = DeviceProfile.Touched;
        foreach (var target in HudLayout.Targets)
        {
            if (target == null || !target.gameObject.activeInHierarchy) continue;
            if (target.id.StartsWith("touch.") && !touch) continue;
            var h = new Handle { target = target };
            h.frame = UIKit.Panel(_handles, target.id, UIKit.PanelTone.Background, _t);
            h.frame.color = new Color(1, 1, 1, 0.04f);
            h.frame.raycastTarget = true;
            h.frame.rectTransform.anchorMin = h.frame.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            h.frame.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            h.label = UIKit.Text(h.frame.transform, "Label", target.label.ToUpperInvariant(), UIKit.TextRole.Caption, _t);
            h.label.alignment = TextAlignmentOptions.TopLeft;
            h.label.textWrappingMode = TextWrappingModes.NoWrap;
            h.label.raycastTarget = false;
            var lrt = h.label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 1f); lrt.anchorMax = new Vector2(1f, 1f); lrt.pivot = new Vector2(0f, 0f);
            lrt.sizeDelta = new Vector2(0f, 18f);
            lrt.anchoredPosition = new Vector2(2f, 2f);
            var drag = h.frame.gameObject.AddComponent<HandleDrag>();
            drag.editor = this;
            drag.handle = h;
            _all.Add(h);
        }
    }

    /// <summary>Pointer and finger handling on one outline.</summary>
    class HandleDrag : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public HudEditor editor;
        public Handle handle;
        Vector2 _last;

        public void OnPointerDown(PointerEventData e)
        {
            editor.Select(handle);
            _last = editor.Local(e.position, e.pressEventCamera);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            editor.Select(handle);
            editor.BeginMove();
            _last = editor.Local(e.position, e.pressEventCamera);
        }

        public void OnDrag(PointerEventData e)
        {
            var now = editor.Local(e.position, e.pressEventCamera);
            editor.MoveBy(now - _last);
            _last = now;
        }

        public void OnEndDrag(PointerEventData e) => editor.EndMove();
    }

    // ---- the side panel --------------------------------------------------------

    void BuildSide(Transform parent)
    {
        var card = UIKit.Panel(parent, "Panel", UIKit.PanelTone.Panel, _t);
        card.raycastTarget = true;
        _side = card.rectTransform;
        // As tall as what is in it and centred on the side, not the full height: the corners
        // are where the run panel and the weapon live, and a panel over them is a panel the
        // player cannot click past to pick them up.
        _side.anchorMin = _side.anchorMax = _side.pivot = new Vector2(1f, 0.5f);
        _side.sizeDelta = new Vector2(430f, 0f);
        _side.anchoredPosition = new Vector2(-20f, 0f);
        card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        card.gameObject.AddComponent<FitInsideParent>().margin = 12f;
        var col = UIKit.Column(card, 10f, new RectOffset(20, 20, 16, 16));
        col.childForceExpandHeight = false;
        col.childControlHeight = true;

        var title = UIKit.Text(card.transform, "Title", "HUD layout", UIKit.TextRole.Title, _t);
        title.textWrappingMode = TextWrappingModes.NoWrap;

        var tabs = UIKit.TabBar(card.transform, "Tabs", new[] { "Element", "Crosshair", "Presets" }, _t);
        _tabs = tabs;
        UIKit.Size(tabs).minHeight = 44f;
        tabs.onChanged.AddListener(ShowPage);

        var body = UIKit.Rect(card.transform, "Body");
        var bcol = UIKit.Column(body, 0f);
        bcol.childForceExpandHeight = false;
        bcol.childControlHeight = true;


        BuildElementPage(Page(body, "Element"));
        BuildCrosshairPage(Page(body, "Crosshair"));
        BuildPresetsPage(Page(body, "Presets"));
        ShowPage(0);

        _padHint = UIKit.Text(card.transform, "PadHint", "", UIKit.TextRole.Caption, _t);
        _padHint.color = _t.textSecondary;
        _padHint.textWrappingMode = TextWrappingModes.Normal;

        var footer = UIKit.Rect(card.transform, "Footer");
        var frow = UIKit.Row(footer, 8f, null, TextAnchor.MiddleCenter);
        frow.childForceExpandWidth = true;
        UIKit.Size(footer).minHeight = UIKit.ControlHeight;
        var resetAll = UIKit.Button(footer, "ResetAll", "Reset all", FlatButton.Variant.Quiet, "refresh", _t);
        resetAll.onClick.AddListener(ResetAll);
        var cancel = UIKit.Button(footer, "Cancel", "Cancel", FlatButton.Variant.Secondary, "x", _t);
        cancel.onClick.AddListener(Close);
        var save = UIKit.Button(footer, "Save", "Save", FlatButton.Variant.Primary, "check", _t);
        save.onClick.AddListener(Save);
        save.gameObject.AddComponent<UIDefaultSelection>().priority = 80;
        backButton = cancel;
    }

    RectTransform Page(RectTransform parent, string name)
    {
        var page = UIKit.Rect(parent, name);
        var c = UIKit.Column(page, 8f);
        c.childForceExpandHeight = false;
        c.childControlHeight = true;
        _pages.Add(page);
        return page;
    }

    UITabBar _tabs;

    /// <summary>Opens a tab of the side panel: 0 element, 1 crosshair, 2 presets.</summary>
    public void ShowTab(int index)
    {
        if (_tabs != null) _tabs.Selected = index;
        ShowPage(index);
    }

    /// <summary>Selects an element by its layout id, for a caller that knows which it wants.</summary>
    public void SelectById(string id)
    {
        foreach (var h in _all) if (h.target != null && h.target.id == id) { Select(h); return; }
    }

    void ShowPage(int index)
    {
        for (int i = 0; i < _pages.Count; i++) _pages[i].gameObject.SetActive(i == index);
    }

    /// <summary>A label over its control, for a panel too narrow for a label beside one.</summary>
    RectTransform Row(RectTransform page, string label)
    {
        var row = UIKit.Rect(page, label);
        var c = UIKit.Column(row, 2f);
        c.childForceExpandHeight = false;
        c.childControlHeight = true;
        var l = UIKit.Text(row, "Label", label, UIKit.TextRole.Label, _t);
        l.color = _t.textSecondary;
        var slot = UIKit.Rect(row, "Control");
        UIKit.Size(slot).minHeight = 40f;
        return slot;
    }

    Slider SliderRow(RectTransform page, string label, float min, float max, out TMP_Text value,
                     UnityEngine.Events.UnityAction<float> changed)
    {
        var slot = Row(page, label);
        var s = UIKit.Slider(slot, "Slider", min, max, out value, _t);
        s.onValueChanged.AddListener(v => { if (!_refreshing) changed(v); });
        return s;
    }

    void BuildElementPage(RectTransform page)
    {
        _selName = UIKit.Text(page, "Selected", "", UIKit.TextRole.Heading, _t);
        _selName.textWrappingMode = TextWrappingModes.NoWrap;
        _size = SliderRow(page, "Size", 60f, 150f, out _sizeValue, v => ChangeSelected(e => e.scale = v / 100f));
        _opacity = SliderRow(page, "Opacity", 20f, 100f, out _opacityValue, v => ChangeSelected(e => e.opacity = v / 100f));
        var vis = Row(page, "Show");
        _visible = UIKit.Switch(vis, "Switch", _t);
        _visible.Changed += on => { if (!_refreshing) ChangeSelected(e => e.hidden = !on); };
        var reset = UIKit.Button(page, "ResetElement", "Reset element", FlatButton.Variant.Secondary, "refresh", _t);
        reset.onClick.AddListener(ResetSelected);

        if (DeviceProfile.Touched)
        {
            var fire = Row(page, "Second fire button (other thumb)");
            _secondFire = UIKit.Switch(fire, "Switch", _t);
            _secondFire.Changed += on =>
            {
                if (_refreshing) return;
                _work.secondFire = on;
                PreviewAndRebuild();
            };
        }
    }

    void BuildCrosshairPage(RectTransform page)
    {
        var style = Row(page, "Style");
        _xStyle = UIKit.Choice(style, "Choice", new[] { "Cross", "Dot", "Circle" }, _t);
        _xStyle.Changed += i => ChangeCrosshair(c => c.style = (HudLayout.CrosshairStyle)i);
        var colour = Row(page, "Colour");
        var names = new string[HudLayout.Colors.Length];
        for (int i = 0; i < names.Length; i++) names[i] = HudLayout.Colors[i].name;
        _xColor = UIKit.Choice(colour, "Choice", names, _t);
        _xColor.Changed += i => ChangeCrosshair(c => c.color = i);
        _xSize = SliderRow(page, "Size", 50f, 200f, out _xSizeValue, v => ChangeCrosshair(c => c.size = v / 100f));
        _xThick = SliderRow(page, "Thickness", 50f, 300f, out _xThickValue, v => ChangeCrosshair(c => c.thickness = v / 100f));
        _xGap = SliderRow(page, "Gap", 0f, 200f, out _xGapValue, v => ChangeCrosshair(c => c.gap = v / 100f));
        var outline = Row(page, "Outline");
        _xOutline = UIKit.Switch(outline, "Switch", _t);
        _xOutline.Changed += on => ChangeCrosshair(c => c.outline = on);
    }

    void BuildPresetsPage(RectTransform page)
    {
        var heading = UIKit.Text(page, "Builtin", "Built-in", UIKit.TextRole.Label, _t);
        heading.color = _t.textSecondary;
        var names = new List<string> { "Default", "Minimal", "Competitive" };
        if (DeviceProfile.Touched) names.AddRange(new[] { "Two-Thumb", "Claw", "Left-Handed" });
        RectTransform row = null;
        for (int i = 0; i < names.Count; i++)
        {
            if (i % 3 == 0)
            {
                row = UIKit.Rect(page, "Presets" + i);
                var r = UIKit.Row(row, 6f);
                r.childForceExpandWidth = true;
                UIKit.Size(row).minHeight = UIKit.ControlHeight;
            }
            string name = names[i];
            var b = UIKit.Button(row, name, name, FlatButton.Variant.Secondary, null, _t);
            b.onClick.AddListener(() => ApplyPreset(name));
        }

        var custom = UIKit.Text(page, "Custom", "Saved layouts", UIKit.TextRole.Label, _t);
        custom.color = _t.textSecondary;
        for (int i = 0; i < HudLayout.CustomSlots; i++)
        {
            int slot = i;
            var line = UIKit.Rect(page, "Slot" + i);
            UIKit.Row(line, 6f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
            UIKit.Size(line).minHeight = UIKit.ControlHeight;
            var field = NameField(line, $"Layout {i + 1}");
            UIKit.Size(field, flexWidth: 1f);
            _slotNames.Add(field);
            var save = UIKit.IconButton(line, "Save", "arrow-bar-to-down", "Save this layout here", UIKit.ControlHeight, _t);
            save.onClick.AddListener(() => { HudLayout.SaveCustom(slot, _work, field.text); RefreshSlots(); });
            var load = UIKit.IconButton(line, "Load", "player-play", "Use this layout", UIKit.ControlHeight, _t);
            load.onClick.AddListener(() => LoadCustom(slot));
        }
    }

    /// <summary>A flat text field: the kit has none, and a layout needs a name.</summary>
    TMP_InputField NameField(RectTransform parent, string placeholder)
    {
        var face = UIKit.Panel(parent, "Name", UIKit.PanelTone.Raised, _t);
        face.raycastTarget = true;
        UIKit.Size(face, height: UIKit.ControlHeight);
        var area = UIKit.Rect(face.transform, "Text Area");
        UIKit.Fill(area, 12f, 6f, 12f, 6f);
        area.gameObject.AddComponent<RectMask2D>();
        var ph = UIKit.Text(area, "Placeholder", placeholder, UIKit.TextRole.Body, _t);
        ph.color = _t.textDisabled;
        UIKit.Fill(ph.rectTransform);
        ph.alignment = TextAlignmentOptions.MidlineLeft;
        var text = UIKit.Text(area, "Text", "", UIKit.TextRole.Body, _t);
        UIKit.Fill(text.rectTransform);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        var field = face.gameObject.AddComponent<TMP_InputField>();
        field.textViewport = area;
        field.textComponent = text;
        field.placeholder = ph;
        field.characterLimit = 20;
        field.targetGraphic = face;
        field.caretColor = _t.accent;
        field.selectionColor = new Color(_t.accent.r, _t.accent.g, _t.accent.b, 0.35f);
        return field;
    }

    // ======================================================================
    // Open / close.
    // ======================================================================

    protected override void OnOpened()
    {
        if (_view != null) _view.SetPauseMenuShown(false);
        var es = EventSystem.current;
        if (es != null)
        {
            // The D-pad chooses elements here, so it must not also walk the focus round the panel.
            _navWas = es.sendNavigationEvents;
            es.sendNavigationEvents = false;
        }
        transform.SetAsLastSibling();
    }

    protected override void OnClosed()
    {
        // Cancel -- the button, Escape or B, which all come through the base Close -- puts
        // the kept layout back; only Save keeps the copy.
        if (!_saved) HudLayout.Revert();
        if (_hud != null) _hud.ApplyCrosshairStyle(HudLayout.Current.crosshair);
        if (_view != null) _view.SetEditing(false);
        var es = EventSystem.current;
        if (es != null) es.sendNavigationEvents = _navWas;
        if (_fromMenu)
        {
            var d = GameDirector.Instance;
            if (d != null) d.ReturnToMenu();
        }
        else if (_view != null) _view.SetPauseMenuShown(true);
        Destroy(gameObject);
    }

    bool _saved;

    void Save()
    {
        _saved = true;
        HudLayout.Save(_work);
        Close();
    }

    // ======================================================================
    // Editing.
    // ======================================================================

    void Select(Handle h)
    {
        _selected = h;
        RefreshElement();
    }

    HudLayout.Entry Entry(Handle h) => h == null ? null : _work.Ensure(h.target.id);

    void ChangeSelected(System.Action<HudLayout.Entry> change)
    {
        if (_selected == null) return;
        var e = Entry(_selected);
        change(e);
        HudLayout.Preview(_work);
        RefreshElement();
    }

    void ChangeCrosshair(System.Action<HudLayout.Crosshair> change)
    {
        if (_refreshing) return;
        change(_work.crosshair);
        HudLayout.Preview(_work);
        RefreshCrosshair();
    }

    void ResetSelected()
    {
        if (_selected == null) return;
        _work.entries.RemoveAll(e => e != null && e.id == _selected.target.id);
        HudLayout.Preview(_work);
        RefreshElement();
    }

    void ResetAll()
    {
        _work = new HudLayout.Data();
        PreviewAndRebuild();
    }

    void PreviewAndRebuild()
    {
        var id = _selected != null ? _selected.target.id : null;
        HudLayout.Preview(_work);
        BuildHandles();
        Handle again = null;
        foreach (var h in _all) if (h.target.id == id) again = h;
        Select(again ?? (_all.Count > 0 ? _all[0] : null));
        RefreshCrosshair();
    }

    void LoadCustom(int slot)
    {
        var data = HudLayout.Custom(slot);
        if (data == null) return;
        _work = data.Clone();
        PreviewAndRebuild();
    }

    void ApplyPreset(string name)
    {
        _work = HudPresets.Build(name, _work);
        PreviewAndRebuild();
    }

    // ---- moving ------------------------------------------------------------------

    public Vector2 Local(Vector2 screen, Camera cam)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, cam, out var p);
        return p;
    }

    void BeginMove()
    {
        if (_selected == null) return;
        _moving = true;
        _moveStart = ElementRect(_selected.target).min;
        _moveRaw = Vector2.zero;
    }

    /// <summary>
    /// Pushes the selected element by a delta in the editor's units. The raw push is kept,
    /// and the snapped, clamped place worked out from it each time -- snapping each step
    /// instead would swallow any movement smaller than a grid cell, and a slow drag would
    /// never move at all.
    /// </summary>
    void MoveBy(Vector2 delta)
    {
        if (_selected == null) return;
        if (!_moving) BeginMove();
        _moveRaw += delta;
        var now = ElementRect(_selected.target);
        var want = new Rect(_moveStart + _moveRaw, now.size);
        want = Constrain(want);
        Vector2 shift = want.min - now.min;
        if (shift.sqrMagnitude < 0.0001f) return;
        var parent = _selected.target.Rect.parent as RectTransform;
        Vector3 world = _root.TransformVector(shift);
        Vector2 inParent = parent != null ? (Vector2)parent.InverseTransformVector(world) : shift;
        _selected.target.Nudge(inParent);
        var e = Entry(_selected);
        _selected.target.Measure(e);
    }

    void EndMove()
    {
        if (!_moving) return;
        _moving = false;
        HudLayout.Preview(_work);
    }

    /// <summary>Snaps to the grid and the safe area's edges, keeps inside it, and off the crosshair.</summary>
    Rect Constrain(Rect r)
    {
        var safe = SafeRect();
        float x = Mathf.Round((r.xMin - safe.xMin) / Grid) * Grid + safe.xMin;
        float y = Mathf.Round((r.yMin - safe.yMin) / Grid) * Grid + safe.yMin;
        if (Mathf.Abs(r.xMin - safe.xMin) < EdgeSnap) x = safe.xMin;
        if (Mathf.Abs(r.xMax - safe.xMax) < EdgeSnap) x = safe.xMax - r.width;
        if (Mathf.Abs(r.yMin - safe.yMin) < EdgeSnap) y = safe.yMin;
        if (Mathf.Abs(r.yMax - safe.yMax) < EdgeSnap) y = safe.yMax - r.height;
        x = Mathf.Clamp(x, safe.xMin, Mathf.Max(safe.xMin, safe.xMax - r.width));
        y = Mathf.Clamp(y, safe.yMin, Mathf.Max(safe.yMin, safe.yMax - r.height));
        var placed = new Rect(x, y, r.width, r.height);

        var zone = new Rect(-CrosshairZone * 0.5f, -CrosshairZone * 0.5f, CrosshairZone, CrosshairZone);
        if (placed.Overlaps(zone))
        {
            // Out along whichever side it is least far in.
            float left = placed.xMax - zone.xMin, right = zone.xMax - placed.xMin;
            float down = placed.yMax - zone.yMin, up = zone.yMax - placed.yMin;
            float m = Mathf.Min(Mathf.Min(left, right), Mathf.Min(down, up));
            if (m == left) placed.x -= left;
            else if (m == right) placed.x += right;
            else if (m == down) placed.y -= down;
            else placed.y += up;
        }
        return placed;
    }

    Rect SafeRect()
    {
        var s = ScreenInfo.SafeArea;
        var cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
        var a = Local(s.min, cam);
        var b = Local(s.max, cam);
        var r = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        // Batch mode and a simulated screen can hand back nothing; the root is the fallback.
        return r.width > 10f && r.height > 10f ? r : _root.rect;
    }

    Rect ElementRect(HudLayoutTarget t)
    {
        var corners = new Vector3[4];
        t.Rect.GetWorldCorners(corners);
        Vector2 a = _root.InverseTransformPoint(corners[0]);
        Vector2 b = _root.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    // ======================================================================
    // Per frame: the outlines, and the pad.
    // ======================================================================

    protected override void Update()
    {
        base.Update();
        if (!IsOpen) return;

        foreach (var h in _all)
        {
            if (h.target == null) continue;
            h.rect = ElementRect(h.target);
            h.visible = !h.target.Hidden;
            var rt = h.frame.rectTransform;
            rt.sizeDelta = h.rect.size;
            rt.anchoredPosition = h.rect.center;
        }
        foreach (var h in _all)
        {
            bool overlap = false;
            if (h.visible && !h.target.zone)
                foreach (var o in _all)
                    if (o != h && o.visible && !o.target.zone && h.rect.Overlaps(o.rect)) { overlap = true; break; }
            bool sel = h == _selected;
            h.frame.borderColor = overlap ? _t.danger : sel ? _t.accent : new Color(1, 1, 1, h.visible ? 0.55f : 0.25f);
            h.frame.borderPixels = sel || overlap ? 2f : 1f;
            h.frame.color = sel ? new Color(_t.accent.r, _t.accent.g, _t.accent.b, 0.10f) : new Color(1, 1, 1, h.visible ? 0.04f : 0.015f);
            h.label.color = overlap ? _t.danger : sel ? _t.accent : _t.textSecondary;
        }

        // The panel keeps out of the way: it sits on whichever side the selection is not.
        if (_selected != null)
        {
            bool right = _selected.rect.center.x > 0f;
            float x = right ? 0f : 1f;
            if (!Mathf.Approximately(_side.anchorMin.x, x))
            {
                _side.anchorMin = _side.anchorMax = _side.pivot = new Vector2(x, 0.5f);
                _side.anchoredPosition = new Vector2(right ? 20f : -20f, 0f);
            }
        }

        Pad();
    }

    void Pad()
    {
        var pad = Gamepad.current;
        bool live = GameInput.UsingGamepad && pad != null;
        _padHint.gameObject.SetActive(live);
        if (!live) return;
        _padHint.text = "D-PAD select   L-STICK move   LT/RT size   R-STICK opacity   Y show/hide   X reset   START save   B cancel";

        Vector2 dir = Vector2.zero;
        if (pad.dpad.left.wasPressedThisFrame) dir = Vector2.left;
        else if (pad.dpad.right.wasPressedThisFrame) dir = Vector2.right;
        else if (pad.dpad.up.wasPressedThisFrame) dir = Vector2.up;
        else if (pad.dpad.down.wasPressedThisFrame) dir = Vector2.down;
        if (dir != Vector2.zero) SelectToward(dir);

        var stick = pad.leftStick.ReadValue();
        if (stick.magnitude > 0.2f) MoveBy(stick * 420f * Time.unscaledDeltaTime);
        else if (_moving) EndMove();

        float grow = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
        if (Mathf.Abs(grow) > 0.1f && _selected != null)
            ChangeSelected(e => e.scale = Mathf.Clamp(e.scale + grow * 0.6f * Time.unscaledDeltaTime, 0.6f, 1.5f));
        float fade = pad.rightStick.ReadValue().y;
        if (Mathf.Abs(fade) > 0.25f && _selected != null)
            ChangeSelected(e => e.opacity = Mathf.Clamp(e.opacity + fade * 0.6f * Time.unscaledDeltaTime, 0.2f, 1f));
        if (pad.buttonNorth.wasPressedThisFrame && _selected != null) ChangeSelected(e => e.hidden = !e.hidden);
        if (pad.buttonWest.wasPressedThisFrame) ResetSelected();
        if (pad.startButton.wasPressedThisFrame) Save();
    }

    /// <summary>The nearest element whose centre lies in a direction from the selected one.</summary>
    void SelectToward(Vector2 dir)
    {
        if (_selected == null) { if (_all.Count > 0) Select(_all[0]); return; }
        Handle best = null;
        float bestScore = float.MaxValue;
        foreach (var h in _all)
        {
            if (h == _selected) continue;
            var d = h.rect.center - _selected.rect.center;
            float along = Vector2.Dot(d, dir);
            if (along <= 1f) continue;
            float score = along + Mathf.Abs(Vector2.Dot(d, new Vector2(-dir.y, dir.x))) * 2f;
            if (score < bestScore) { bestScore = score; best = h; }
        }
        if (best != null) Select(best);
    }

    // ======================================================================
    // Refresh.
    // ======================================================================

    void RefreshElement()
    {
        _refreshing = true;
        var e = _selected != null ? _work.Find(_selected.target.id) : null;
        _selName.text = _selected != null ? _selected.target.label.ToUpperInvariant() : "NOTHING SELECTED";
        float scale = e != null ? e.scale : 1f, opacity = e != null ? e.opacity : 1f;
        _size.value = scale * 100f;
        _sizeValue.text = _t.Tabular(Mathf.RoundToInt(scale * 100f) + "%");
        _opacity.value = opacity * 100f;
        _opacityValue.text = _t.Tabular(Mathf.RoundToInt(opacity * 100f) + "%");
        _visible.Set(e == null || !e.hidden, notify: false);
        if (_secondFire != null) _secondFire.Set(_work.secondFire, notify: false);
        _refreshing = false;
    }

    void RefreshCrosshair()
    {
        _refreshing = true;
        var c = _work.crosshair;
        _xStyle.Set((int)c.style, notify: false);
        _xColor.Set(c.color, notify: false);
        _xSize.value = c.size * 100f; _xSizeValue.text = _t.Tabular(Mathf.RoundToInt(c.size * 100f) + "%");
        _xThick.value = c.thickness * 100f; _xThickValue.text = _t.Tabular(Mathf.RoundToInt(c.thickness * 100f) + "%");
        _xGap.value = c.gap * 100f; _xGapValue.text = _t.Tabular(Mathf.RoundToInt(c.gap * 100f) + "%");
        _xOutline.Set(c.outline, notify: false);
        _refreshing = false;
        if (_hud != null) _hud.ApplyCrosshairStyle(c);
    }

    void RefreshSlots()
    {
        for (int i = 0; i < _slotNames.Count; i++)
        {
            var d = HudLayout.Custom(i);
            _slotNames[i].SetTextWithoutNotify(d != null ? d.name : "");
        }
    }
}
