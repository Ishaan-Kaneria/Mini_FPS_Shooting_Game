using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The game's own keyboard, for typing a name on a touch screen.
///
/// The phone's keyboard (Gboard on Android, the browser's on the web build) slid up over a
/// landscape game in its own colours, took half the screen, offered emoji and suggestions,
/// and on the web sometimes resized the page under it. This is the same job done in the
/// interface's own look: a full-screen sheet with what is being typed at the top, in large
/// type with an amber caret, and flat keys under it -- numbers, QWERTY, backspace, shift,
/// space and an amber DONE.
///
/// <b>Touch only.</b> With a mouse and a physical keyboard the field is typed into
/// directly, as before. <see cref="Attach"/> is what every name field calls: on a touch
/// device it stops the field opening the system keyboard
/// (<see cref="TMP_InputField.shouldHideSoftKeyboard"/>) and opens this instead when the
/// field is tapped. A hardware keyboard still types while the sheet is up, for a tablet
/// with one clipped on.
///
/// <b>The field stays the source of truth.</b> Keys write straight into
/// <see cref="TMP_InputField.text"/>, so its character limit, its listeners and whatever
/// reads it afterwards all behave as if the player had typed there. Closing the sheet
/// ends the edit (the Profile page saves then); DONE also submits, which is how the name
/// dialog confirms. Escape or B closes the sheet and keeps what was typed.
///
/// Keys act on release, like every other button in the game, and none of them takes the
/// focus: a key that did would take it from the field on every press.
/// </summary>
public class OnScreenKeyboard : OverlayPanel
{
    /// <summary>The key rows, top to bottom. A constant, split where it is used, so it carries no state.</summary>
    const string Layout = "1234567890|QWERTYUIOP|ASDFGHJKL|ZXCVBNM";

    TMP_InputField _field;
    TMP_Text _preview, _prompt, _count, _shiftLabel;
    FlatRect _caret, _shiftFace;
    readonly System.Collections.Generic.List<TMP_Text> _letterLabels = new System.Collections.Generic.List<TMP_Text>();
    bool _shift = true;
    bool _capsLock;
    float _lastShiftTap = -1f;
    float _blink;

    /// <summary>Whether a touch screen is how this player types. The one test every caller uses.</summary>
    public static bool Wanted => DeviceProfile.Touched;

    /// <summary>
    /// Makes a field use this keyboard on a touch device: the system keyboard is suppressed
    /// and a tap on the field opens this one. Harmless to call more than once, and does
    /// nothing with a mouse and keyboard.
    /// </summary>
    public static void Attach(TMP_InputField field, string prompt)
    {
        if (field == null) return;
        field.shouldHideSoftKeyboard = true;
        field.shouldHideMobileInput = true;
        var opener = field.GetComponent<KeyboardOpener>();
        if (opener == null) opener = field.gameObject.AddComponent<KeyboardOpener>();
        opener.prompt = prompt;
    }

    /// <summary>Opens the sheet over the field's canvas, typing into the field.</summary>
    public static OnScreenKeyboard Open(TMP_InputField field, string prompt)
    {
        if (field == null) return null;
        var near = field.GetComponentInParent<Canvas>();
        var canvas = near != null ? near.rootCanvas : null;
        if (canvas == null) return null;
        var k = canvas.GetComponentInChildren<OnScreenKeyboard>(true);
        if (k == null)
        {
            var host = new GameObject("OnScreenKeyboard", typeof(RectTransform));
            host.transform.SetParent(canvas.transform, false);
            UIKit.Fill((RectTransform)host.transform);
            var own = host.AddComponent<Canvas>();
            own.overrideSorting = true;
            own.sortingOrder = 950;
            host.AddComponent<GraphicRaycaster>();
            k = host.AddComponent<OnScreenKeyboard>();
            k.Build();
        }
        k._field = field;
        if (k._prompt != null) k._prompt.text = string.IsNullOrEmpty(prompt) ? "TYPE A NAME" : prompt.ToUpperInvariant();
        k.transform.SetAsLastSibling();
        if (field.isFocused) field.DeactivateInputField();
        k.Open();
        return k;
    }

    // ======================================================================
    // Build.
    // ======================================================================

    void Build()
    {
        var t = UITheme.Active;
        var sheet = UIKit.Panel(transform, "Sheet", UIKit.PanelTone.Background, t);
        sheet.color = t.background;
        sheet.borderColor = new Color(0, 0, 0, 0);
        sheet.raycastTarget = true;
        UIKit.Fill(sheet.rectTransform);
        panel = sheet.gameObject;
        gameObject.AddComponent<SafeAreaBleed>();

        // The sheet runs edge to edge; the keys stay inside the safe area, clear of a notch
        // and the gesture bar.
        var inside = UIKit.Rect(sheet.transform, "Inside");
        UIKit.Fill(inside);
        inside.gameObject.AddComponent<SafeAreaFitter>().paddingMm = 1f;
        var col = UIKit.Column(inside, 14f, new RectOffset(40, 40, 24, 24));
        col.childForceExpandHeight = false;
        col.childControlHeight = true;

        // ---- what is being typed -----------------------------------------------
        var head = UIKit.Rect(inside, "Head");
        var hrow = UIKit.Row(head, 16f, null, TextAnchor.MiddleLeft);
        hrow.childForceExpandHeight = false;
        _prompt = UIKit.Text(head, "Prompt", "TYPE A NAME", UIKit.TextRole.Heading, t);
        _prompt.color = t.textSecondary;
        UIKit.Size(_prompt, flexWidth: 1f);
        _count = UIKit.Text(head, "Count", "0/16", UIKit.TextRole.Number, t);
        _count.color = t.textSecondary;
        _count.fontSize = t.sizeLabel + 2f;

        var box = UIKit.Panel(inside, "Field", UIKit.PanelTone.Panel, t);
        box.stripeSide = FlatRect.Side.Bottom;
        box.stripeColor = t.accent;
        box.stripePixels = t.selectionPixels;
        UIKit.Size(box, height: 76f);
        var boxRow = UIKit.Row(box, 0f, new RectOffset(22, 22, 0, 0), TextAnchor.MiddleLeft);
        boxRow.childForceExpandWidth = false;
        boxRow.childForceExpandHeight = false;
        _preview = UIKit.Text(box.transform, "Text", "", UIKit.TextRole.Title, t);
        _preview.fontSize = t.sizeTitle + 6f;
        _preview.fontStyle = FontStyles.Normal;
        _preview.characterSpacing = 2f;
        _preview.overflowMode = TextOverflowModes.Ellipsis;
        _caret = UIKit.Rect(box.transform, "Caret").gameObject.AddComponent<FlatRect>();
        _caret.raycastTarget = false;
        _caret.color = t.accent;
        UIKit.Size(_caret, 3f, 40f);

        // ---- keys ------------------------------------------------------------------
        var keys = UIKit.Rect(inside, "Keys");
        UIKit.Size(keys, flexHeight: 1f);
        var kcol = UIKit.Column(keys, 8f);
        kcol.childForceExpandHeight = true;
        kcol.childControlHeight = true;

        var rows = Layout.Split('|');
        for (int r = 0; r < rows.Length; r++)
        {
            var row = KeyRow(keys, "Row" + r);
            if (r == 2) Spacer(row, 0.5f);
            if (r == 3) _shiftFace = Key(row, "Shift", "", "arrow-up", 1.5f, ToggleShift, t, out _shiftLabel);
            foreach (char c in rows[r])
            {
                char ch = c;
                Key(row, "Key_" + c, c.ToString(), null, 1f, () => Type(ch), t, out var label);
                if (char.IsLetter(c)) _letterLabels.Add(label);
            }
            if (r == 2) Spacer(row, 0.5f);
            if (r == 3)
            {
                Key(row, "Hyphen", "-", null, 1f, () => Type('-'), t, out _);
                Key(row, "Backspace", "", "backspace", 1.5f, Backspace, t, out _);
            }
        }

        var last = KeyRow(keys, "Row4");
        Key(last, "Clear", "CLEAR", null, 1.5f, Clear, t, out _);
        Key(last, "Space", "SPACE", null, 5f, () => Type(' '), t, out _);
        var done = Key(last, "Done", "DONE", "check", 2.5f, Done, t, out var doneLabel);
        done.color = t.accent;
        done.borderColor = new Color(0, 0, 0, 0);
        doneLabel.color = t.textOnAccent;
        var doneIcon = done.transform.Find("Icon");
        if (doneIcon != null && doneIcon.TryGetComponent(out Image di)) di.color = t.textOnAccent;
        done.GetComponent<KeyboardKey>().rest = t.accent;
        done.GetComponent<KeyboardKey>().pressed = t.accentPressed;

        panel.SetActive(false);
    }

    static RectTransform KeyRow(RectTransform parent, string name)
    {
        var row = UIKit.Rect(parent, name);
        var h = UIKit.Row(row, 8f, null, TextAnchor.MiddleCenter);
        h.childForceExpandHeight = true;
        h.childForceExpandWidth = false;
        UIKit.Size(row, flexHeight: 1f).minHeight = 40f;
        return row;
    }

    static void Spacer(RectTransform row, float weight)
    {
        var s = UIKit.Rect(row, "Gap");
        UIKit.Size(s, flexWidth: weight).preferredWidth = 0f;
    }

    /// <summary>One key: a raised flat face, a caption or an icon, weighted across its row.</summary>
    static FlatRect Key(RectTransform row, string name, string caption, string icon, float weight,
                        System.Action press, UITheme t, out TMP_Text label)
    {
        var face = UIKit.Panel(row, name, UIKit.PanelTone.Raised, t);
        face.raycastTarget = true;
        var le = UIKit.Size(face, flexWidth: weight);
        le.preferredWidth = 0f;
        le.minWidth = 0f;
        UIKit.Row(face, 8f, null, TextAnchor.MiddleCenter).childForceExpandWidth = false;
        if (!string.IsNullOrEmpty(icon)) UIKit.Icon(face.transform, "Icon", icon, 26f, t.textPrimary, t);
        label = UIKit.Text(face.transform, "Label", caption, UIKit.TextRole.Heading, t);
        label.fontSize = t.sizeTitle - 6f;
        label.characterSpacing = 0f;
        // Normal case, not the heading's forced capitals: shift has to be seen to work.
        label.fontStyle = FontStyles.Normal;
        label.alignment = TextAlignmentOptions.Center;
        if (string.IsNullOrEmpty(caption)) label.gameObject.SetActive(false);
        var key = face.gameObject.AddComponent<KeyboardKey>();
        key.face = face;
        key.rest = t.panelRaised;
        key.pressed = t.panelHover;
        key.press = press;
        return face;
    }

    // ======================================================================
    // Typing.
    // ======================================================================

    int Limit => _field != null && _field.characterLimit > 0 ? _field.characterLimit : 24;

    void Type(char c)
    {
        if (_field == null) return;
        string text = _field.text ?? "";
        if (text.Length >= Limit || (c == ' ' && (text.Length == 0 || text.EndsWith(" "))))
        {
            UISfx.Play(UISfx.Sound.Error, 1f, 0.5f);
            return;
        }
        if (char.IsLetter(c)) c = _shift || _capsLock ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);
        _field.text = text + c;
        UISfx.Play(UISfx.Sound.Key);
        // One capital, then lower case -- and a capital again after a space, because a
        // name is words. Caps lock (shift tapped twice) holds until it is tapped again.
        if (!_capsLock) SetShift(c == ' ');
        Refresh();
    }

    void Backspace()
    {
        if (_field == null) return;
        string text = _field.text ?? "";
        if (text.Length == 0) { UISfx.Play(UISfx.Sound.Error, 1f, 0.5f); return; }
        _field.text = text.Substring(0, text.Length - 1);
        UISfx.Play(UISfx.Sound.Key, 0.9f);
        if (!_capsLock) SetShift(_field.text.Length == 0 || _field.text.EndsWith(" "));
        Refresh();
    }

    void Clear()
    {
        if (_field == null) return;
        _field.text = "";
        UISfx.Play(UISfx.Sound.Back);
        _capsLock = false;
        SetShift(true);
        Refresh();
    }

    /// <summary>Tap for one capital; tap twice quickly for caps lock; tap again to release.</summary>
    void ToggleShift()
    {
        UISfx.Play(UISfx.Sound.Toggle, 1f, 0.6f);
        float now = Time.unscaledTime;
        if (_capsLock) { _capsLock = false; SetShift(false); }
        else if (_shift && now - _lastShiftTap < 0.4f) { _capsLock = true; SetShift(true); }
        else SetShift(!_shift);
        _lastShiftTap = now;
    }

    void SetShift(bool on)
    {
        _shift = on;
        var t = UITheme.Active;
        bool upper = _shift || _capsLock;
        foreach (var l in _letterLabels)
            if (l != null) l.text = upper ? l.text.ToUpperInvariant() : l.text.ToLowerInvariant();
        if (_shiftFace != null)
        {
            var key = _shiftFace.GetComponent<KeyboardKey>();
            key.rest = _capsLock ? t.accent : upper ? t.panelHover : t.panelRaised;
            key.Repaint();
            _shiftFace.borderColor = upper ? t.accent : t.border;
            var icon = _shiftFace.transform.Find("Icon");
            if (icon != null && icon.TryGetComponent(out Image img)) img.color = _capsLock ? t.textOnAccent : upper ? t.accent : t.textPrimary;
        }
    }

    void Done()
    {
        UISfx.Play(UISfx.Sound.Click);
        var field = _field;
        Close();
        if (field != null) field.onSubmit?.Invoke(field.text);
    }

    void Refresh()
    {
        if (_field == null) return;
        var t = UITheme.Active;
        string text = _field.text ?? "";
        bool empty = text.Length == 0;
        if (_preview != null)
        {
            _preview.text = empty ? (_field.placeholder is TMP_Text ph ? ph.text : "") : text;
            _preview.color = empty ? t.textDisabled : t.textPrimary;
        }
        if (_count != null)
        {
            _count.text = t.Tabular($"{text.Length}/{Limit}");
            _count.color = text.Length >= Limit ? t.accent : t.textSecondary;
        }
        if (_caret != null) _caret.transform.SetSiblingIndex(empty ? 0 : 1);
        _blink = 0f;
    }

    // ======================================================================
    // Overlay.
    // ======================================================================

    protected override void OnOpened()
    {
        _capsLock = false;
        string text = _field != null ? _field.text ?? "" : "";
        SetShift(text.Length == 0 || text.EndsWith(" "));
        Refresh();
        UISfx.Play(UISfx.Sound.Open);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null) kb.onTextInput += HardwareKey;
    }

    /// <summary>
    /// However the sheet closes -- DONE, Escape, B -- the edit has ended, and the field says
    /// so, which is when the Profile page saves. DONE also submits, which is what confirms
    /// the name dialog.
    /// </summary>
    protected override void OnClosed()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null) kb.onTextInput -= HardwareKey;
        if (_field != null) _field.onEndEdit?.Invoke(_field.text);
    }

    protected override void OnDestroy()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null) kb.onTextInput -= HardwareKey;
        base.OnDestroy();
    }

    /// <summary>A clipped-on keyboard still types while the sheet is up.</summary>
    void HardwareKey(char c)
    {
        if (!IsOpen) return;
        if (c == '\b') { Backspace(); return; }
        if (c == '\r' || c == '\n') { Done(); return; }
        if (char.IsLetterOrDigit(c) || c == ' ' || c == '-')
        {
            bool wasShift = _shift;
            _shift = char.IsUpper(c);
            Type(c);
            if (_capsLock) _shift = wasShift;
        }
    }

    protected override void Update()
    {
        base.Update();
        if (!IsOpen || _caret == null) return;
        // The caret blinks at a steady one second, the only thing on the sheet that moves
        // on its own.
        _blink += Time.unscaledDeltaTime;
        var c = _caret.color;
        c.a = (_blink % 1f) < 0.55f ? 1f : 0f;
        _caret.color = c;
    }

    // ======================================================================
    // Parts.
    // ======================================================================

    /// <summary>
    /// Opens the keyboard when a touch lands on the field. On the field itself, because the
    /// field is what the player reaches for.
    /// </summary>
    public class KeyboardOpener : MonoBehaviour, IPointerClickHandler
    {
        public string prompt;

        public void OnPointerClick(PointerEventData e)
        {
            if (!Wanted) return;
            var field = GetComponent<TMP_InputField>();
            if (field == null || !field.interactable) return;
            Open(field, prompt);
        }
    }
}

/// <summary>
/// One key on the <see cref="OnScreenKeyboard"/>. Not a Selectable, on purpose: pressing a
/// Selectable moves the focus to it, and the key would take it from the field on every
/// press. It darkens while held and pops back on release, which is when it types.
/// </summary>
public class KeyboardKey : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler, IPointerExitHandler
{
    public FlatRect face;
    public Color rest, pressed;
    public System.Action press;

    bool _down;

    public void Repaint()
    {
        if (face != null) face.color = _down ? pressed : rest;
        transform.localScale = _down ? new Vector3(0.94f, 0.94f, 1f) : Vector3.one;
    }

    public void OnPointerDown(PointerEventData e) { _down = true; Repaint(); }
    public void OnPointerUp(PointerEventData e) { _down = false; Repaint(); }
    public void OnPointerExit(PointerEventData e) { _down = false; Repaint(); }
    public void OnPointerClick(PointerEventData e) => press?.Invoke();
}
