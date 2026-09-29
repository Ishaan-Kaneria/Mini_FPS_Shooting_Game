using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// How to play, written from the live bindings and drawn as cards: one per section, each
/// with its icon, and every control drawn as the keys you press next to an icon for what
/// it does.
///
/// <b>Built at runtime, like <see cref="AchievementsPanel"/>, so the same screen opens from
/// the dashboard and from the pause menu.</b> It used to be a builder-made page of text in
/// four columns, which the level briefing then repeated at the start of every level
/// because the page could not be reached from inside one. The briefing now leaves the
/// controls to this screen.
///
/// <b>Nothing here is hard-coded, and that is not tidiness.</b> The bindings live in a
/// <see cref="ControlSettings"/> asset with three presets, so a panel that spelled out
/// "WASD to move" would be wrong for two of them -- and wrong in the worst way, because a
/// player who follows written instructions and gets nothing concludes the game is broken
/// rather than that the page is stale.
///
/// <b>Touch, pad and keys get different pages, not one page with a line crossed out.</b>
/// Which page is shown is what the player is holding now (<see cref="GameInput.Scheme"/>),
/// with a touch device read as touch, and it rewrites itself if that changes while open.
/// </summary>
public class InstructionsPanel : OverlayPanel
{
    [Tooltip("Bindings to read. Left empty, the panel describes the shipped preset.")]
    public ControlSettings controls;

    UITheme _t;

    /// <summary>
    /// A laptop or desktop: two columns of larger cards rather than four narrow ones. Four
    /// across put 16pt type in strips a third of the screen tall, which read as small on a
    /// laptop; two across gives the same content room to be drawn at reading size.
    /// </summary>
    static bool Roomy => !DeviceProfile.Handheld;
    static float Grow => Roomy ? 1.25f : 1f;
    TMP_Text _subtitle;
    Image _schemeIcon;
    RectTransform _columnsRoot;
    readonly List<RectTransform> _columns = new();
    readonly List<GameObject> _cards = new();

    /// <summary>The screen on this canvas, built the first time it is asked for.</summary>
    public static InstructionsPanel Create(Canvas canvas, ControlSettings controls = null)
    {
        if (canvas == null) return null;
        var existing = canvas.GetComponentInChildren<InstructionsPanel>(true);
        if (existing != null)
        {
            if (controls != null) existing.controls = controls;
            return existing;
        }

        var host = new GameObject("InstructionsHost", typeof(RectTransform));
        host.transform.SetParent(canvas.transform, false);
        UIKit.Fill((RectTransform)host.transform);
        var own = host.AddComponent<Canvas>();
        own.overrideSorting = true;
        own.sortingOrder = 760;
        host.AddComponent<GraphicRaycaster>();

        var p = host.AddComponent<InstructionsPanel>();
        p.controls = controls;
        p.Build();
        return p;
    }

    void Build()
    {
        _t = UITheme.Active;

        var shade = UIKit.Panel(transform, "Instructions", UIKit.PanelTone.Background, _t);
        shade.borderColor = new Color(0, 0, 0, 0);
        shade.raycastTarget = true;
        UIKit.Fill(shade.rectTransform);
        panel = shade.gameObject;

        var root = UIKit.Rect(shade.transform, "Content");
        UIKit.Fill(root);
        var fit = root.gameObject.AddComponent<SafeAreaFitter>();
        fit.paddingMm = 0f;
        fit.fitVertically = false;
        fit.Apply();

        bool handset = DeviceProfile.CurrentForm == DeviceProfile.Form.Handset;
        int side = handset ? 20 : 40;
        var col = UIKit.Column(root, handset ? 10f : 16f, new RectOffset(side, side, handset ? 14 : 28, handset ? 14 : 28));
        col.childForceExpandHeight = false;
        col.childControlHeight = true;

        // Header: which controls these are, the title, the way out.
        var header = UIKit.Rect(root, "Header");
        UIKit.Row(header, 16f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
        UIKit.Size(header).minHeight = 52f;
        var badge = UIKit.Panel(header, "Scheme", UIKit.PanelTone.Raised, _t);
        UIKit.Size(badge, 52f, 52f);
        _schemeIcon = UIKit.Icon(badge.transform, "Icon", "keyboard", 30f, _t.accent, _t);
        Centre(_schemeIcon.rectTransform);
        var titles = UIKit.Rect(header, "Titles");
        var tcol = UIKit.Column(titles, 2f);
        tcol.childForceExpandHeight = false;
        UIKit.Size(titles, flexWidth: 1f);
        UIKit.Text(titles, "Title", "How to play", UIKit.TextRole.Title, _t);
        _subtitle = UIKit.Text(titles, "Subtitle", "", UIKit.TextRole.Caption, _t);
        var back = UIKit.Button(header, "Back", "Back", FlatButton.Variant.Quiet, "arrow-left", _t);
        back.onClick.AddListener(Close);
        backButton = back;

        Goal(root);

        // The cards, in columns, in a list that scrolls when a phone cannot hold them.
        var viewport = UIKit.Rect(root, "Cards");
        UIKit.Size(viewport, flexHeight: 1f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var hit = viewport.gameObject.AddComponent<FlatRect>();
        hit.color = new Color(0, 0, 0, 0);
        hit.raycastTarget = true;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        _columnsRoot = UIKit.Rect(viewport, "Columns");
        _columnsRoot.anchorMin = new Vector2(0f, 1f); _columnsRoot.anchorMax = new Vector2(1f, 1f);
        _columnsRoot.pivot = new Vector2(0.5f, 1f);
        _columnsRoot.offsetMin = _columnsRoot.offsetMax = Vector2.zero;
        var row = UIKit.Row(_columnsRoot, handset ? 10f : 16f, null, TextAnchor.UpperLeft);
        row.childForceExpandWidth = true;
        _columnsRoot.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = _columnsRoot;

        shade.gameObject.SetActive(false);
    }

    /// <summary>
    /// The rule of the game, before any key: nothing else on the screen makes sense without
    /// it, and it is the one thing the controls cannot teach.
    /// </summary>
    void Goal(RectTransform parent)
    {
        var goal = UIKit.Panel(parent, "Goal", UIKit.PanelTone.Panel, _t);
        goal.stripeSide = FlatRect.Side.Left;
        goal.stripeColor = _t.accent;
        goal.stripePixels = _t.stripePixels;
        UIKit.Row(goal, 16f, new RectOffset(20, 20, 12, 12), TextAnchor.MiddleLeft).childForceExpandWidth = false;
        UIKit.Icon(goal.transform, "Icon", "clock", 30f * Grow, _t.accent, _t);
        var text = UIKit.Text(goal.transform, "Text",
            "<b>Beat the clock.</b> Every kill scores, and stars go by how many you take down: " +
            "three for clearing the arena. The level ends when time runs out, cleared or not.",
            UIKit.TextRole.Body, _t);
        text.fontSize = _t.sizeBody * Grow;
        UIKit.Size(text, flexWidth: 1f);
    }

    protected override void OnOpened()
    {
        // The player's own keys, not the asset's: this page describes what they will press.
        KeyBindings.Apply(controls);

        foreach (var card in _cards) if (card != null) Destroy(card);
        _cards.Clear();

        var scheme = GameInput.Scheme;
        if (scheme == InputScheme.KeyboardMouse && DeviceProfile.Touched) scheme = InputScheme.Touch;

        _subtitle.text = scheme switch
        {
            InputScheme.Touch => "On-screen controls. Drag FIRE to keep shooting while you aim.",
            InputScheme.Gamepad => "Gamepad. Stick sensitivity, deadzone and aim assist are in Settings.",
            _ => "Keyboard and mouse. Keys can be changed in Settings.",
        };
        _schemeIcon.sprite = _t.IconSprite(scheme switch
        {
            InputScheme.Touch => "hand-finger",
            InputScheme.Gamepad => "device-gamepad-2",
            _ => "keyboard",
        });

        var pages = scheme switch
        {
            InputScheme.Touch => TouchPages(),
            InputScheme.Gamepad => PadPages(),
            _ => KeyPages(),
        };

        // Two across everywhere: on a handset four columns are strips too narrow to hold a
        // sentence, and on a laptop two leave room for type at reading size (Roomy).
        int wide = 2;
        while (_columns.Count < wide)
        {
            var column = UIKit.Rect(_columnsRoot, "Column" + _columns.Count);
            var c = UIKit.Column(column, 16f);
            c.childForceExpandHeight = false;
            UIKit.Size(column, flexWidth: 1f);
            _columns.Add(column);
        }
        for (int i = 0; i < _columns.Count; i++) _columns[i].gameObject.SetActive(i < wide);

        for (int i = 0; i < pages.Count; i++)
            _cards.Add(Card(_columns[i % wide], pages[i]));

        StopAllCoroutines();
        StartCoroutine(Arrive());
    }

    /// <summary>
    /// The cards pop in one after another. Scale and fade only: they are placed by a layout
    /// group, so their positions are not this code's to move. Unscaled, because the screen
    /// also opens from the pause menu, where time is stopped.
    /// </summary>
    System.Collections.IEnumerator Arrive()
    {
        var groups = new List<CanvasGroup>();
        foreach (var card in _cards)
        {
            if (card == null) continue;
            var g = card.GetComponent<CanvasGroup>();
            if (g == null) g = card.AddComponent<CanvasGroup>();
            g.alpha = 0f;
            card.transform.localScale = Vector3.one * 0.94f;
            groups.Add(g);
        }

        const float Stagger = 0.07f, Each = 0.32f;
        float start = Time.unscaledTime;
        float end = Stagger * groups.Count + Each;
        while (Time.unscaledTime - start < end)
        {
            float t = Time.unscaledTime - start;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] == null) continue;
                float k = Mathf.Clamp01((t - Stagger * i) / Each);
                float e = 1f - Mathf.Pow(1f - k, 3f);
                groups[i].alpha = e;
                groups[i].transform.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, e);
            }
            yield return null;
        }

        foreach (var g in groups)
            if (g != null) { g.alpha = 1f; g.transform.localScale = Vector3.one; }
    }

    GameObject Card(RectTransform parent, Page page)
    {
        var card = UIKit.Panel(parent, "Card_" + page.Heading, UIKit.PanelTone.Panel, _t);
        card.stripeSide = FlatRect.Side.Top;
        card.stripeColor = _t.accent;
        card.stripePixels = _t.stripePixels;
        var col = UIKit.Column(card, 12f, new RectOffset(18, 18, 16, 18));
        col.childForceExpandHeight = false;

        var head = UIKit.Rect(card.transform, "Heading");
        UIKit.Row(head, 10f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
        UIKit.Icon(head, "Icon", page.Icon, 24f * Grow, _t.accent, _t);
        var title = UIKit.Text(head, "Text", page.Heading, UIKit.TextRole.Heading, _t);
        title.color = _t.accent;
        title.fontSize = _t.sizeHeading * Grow;

        // The key column is as wide as this card's widest row of keys, so four keys for
        // walking do not run into the text, and the descriptions still line up.
        int caps = 1;
        foreach (var line in page.Lines) if (line.Keys != null) caps = Mathf.Max(caps, line.Keys.Length);
        float cap = 40f * Grow;
        float keysWidth = Mathf.Max(DeviceProfile.Handheld ? 118f : 170f, caps * cap + (caps - 1) * 6f + 8f);

        foreach (var line in page.Lines) Row(card.transform, line, keysWidth);
        return card.gameObject;
    }

    /// <summary>Keys on the left as keycaps, then the action's icon and what it does.</summary>
    void Row(Transform parent, Line line, float keysWidth)
    {
        bool continuation = line.Keys == null || line.Keys.Length == 0;

        var row = UIKit.Rect(parent, "Row");
        UIKit.Row(row, 12f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
        UIKit.Size(row).minHeight = (continuation ? 24f : 44f) * Grow;

        // A fixed-width key column, so the descriptions line up down the card.
        var keys = UIKit.Rect(row, "Keys");
        UIKit.Row(keys, 6f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
        UIKit.Size(keys, keysWidth);
        if (!continuation)
            foreach (var key in line.Keys)
                if (!string.IsNullOrEmpty(key)) Keycap(keys, key);

        if (!string.IsNullOrEmpty(line.Icon))
            UIKit.Icon(row, "Icon", line.Icon, 20f * Grow, continuation ? _t.textDisabled : _t.textSecondary, _t);

        var says = UIKit.Text(row, "Says", line.Says, continuation ? UIKit.TextRole.Caption : UIKit.TextRole.Body, _t);
        says.color = continuation ? _t.textSecondary : _t.textPrimary;
        says.fontSize = (continuation ? _t.sizeCaption : _t.sizeBody) * Grow;
        UIKit.Size(says, flexWidth: 1f);
    }

    /// <summary>
    /// One key, drawn as a key: a raised face with its legend. Sized by its legend, with a
    /// square minimum so a single letter is not a sliver. Glyph legends (mouse buttons, pad
    /// faces) get the same face, and it is tall enough for the glyph -- the old page gave
    /// them a text line that was not, and they vanished.
    /// </summary>
    void Keycap(RectTransform parent, string legend)
    {
        var cap = UIKit.Panel(parent, "Key", UIKit.PanelTone.Raised, _t);
        cap.borderColor = _t.borderHover;
        var h = UIKit.Row(cap, 0f, new RectOffset(10, 10, 4, 4), TextAnchor.MiddleCenter);
        h.childForceExpandWidth = false;
        var le = UIKit.Size(cap, height: 40f * Grow);
        le.minWidth = 40f * Grow;
        var text = UIKit.Text(cap.transform, "Legend", legend, UIKit.TextRole.Label, _t);
        text.fontSize = _t.sizeLabel * Grow;
        text.color = _t.textPrimary;
        text.alignment = TextAlignmentOptions.Center;
    }

    static void Centre(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        var le = rt.GetComponent<LayoutElement>();
        if (le != null) le.ignoreLayout = true;
    }

    // ======================================================================
    // Content.
    // ======================================================================

    /// <summary>
    /// Whether the story has handed the bomb over. <b>A page that explains a control the
    /// player does not have is worse than a page that omits it</b>, so before that the
    /// row says what the bomb is and where it comes from instead. It names no number: which
    /// zone hands it over is on <see cref="CampaignData"/> and is meant to move.
    /// </summary>
    static bool BombEarned => Campaign.HasPower(Campaign.BombPower);

    const string BombLockLine = "Locked. The story hands it over when the first of the Augers goes down.";

    readonly struct Line
    {
        public readonly string[] Keys;
        public readonly string Icon;
        public readonly string Says;
        public Line(string[] keys, string icon, string says) { Keys = keys; Icon = icon; Says = says; }
    }

    readonly struct Page
    {
        public readonly string Heading, Icon;
        public readonly List<Line> Lines;
        public Page(string heading, string icon, List<Line> lines) { Heading = heading; Icon = icon; Lines = lines; }
    }

    static Line L(string icon, string says, params string[] keys) => new Line(keys, icon, says);
    static Line More(string says) => new Line(null, null, says);

    /// <summary>
    /// The bindings to describe. The dashboard has no player rig to read them off, so the
    /// caller hands in the arenas' asset; <see cref="ControlSettings.CreateDefault"/> is the
    /// fallback, so even an unwired screen describes the shipped preset.
    /// </summary>
    ControlSettings Bindings
    {
        get
        {
            if (controls != null) return controls;
            _fallback ??= ControlSettings.CreateDefault();
            return _fallback;
        }
    }

    ControlSettings _fallback;

    static string K(KeyCode key) => InputPrompts.KeyText(key);
    static string P(GameAction a) => InputPrompts.For(a, InputScheme.Gamepad, GameInput.Pad);

    void OnEnable() => GameInput.SchemeChanged += OnSchemeChanged;
    void OnDisable() => GameInput.SchemeChanged -= OnSchemeChanged;

    void OnSchemeChanged()
    {
        if (IsOpen) OnOpened();
    }

    List<Page> KeyPages()
    {
        var c = Bindings;
        if (c == null) return new List<Page>();

        return new List<Page>
        {
            new Page("Move", "run", new List<Line>
            {
                L("hand-move", "Walk", K(c.altForward), K(c.altLeft), K(c.altBack), K(c.altRight)),
                L("mouse", "Look around", InputPrompts.Glyph("mouse_move")),
                L("run", "Sprint. Held on its own it runs forward.", K(c.sprintKey)),
                L("arrow-up", "Jump", K(c.jump)),
                L("arrow-bar-to-down", "Crouch", K(c.crouch)),
            }),

            new Page("Fight", "crosshair", new List<Line>
            {
                L("bullet", "Fire", K(c.fire)),
                L("crosshair", "Aim down sights", K(c.aim)),
                L("magazine", "Reload", K(c.reload)),
                L("fist", "Punch, up close. Saves a round.", K(c.melee)),
            }),

            // The two nobody finds by experiment.
            new Page("Equipment", "backpack", BombEarned
                ? new List<Line>
                {
                    L("bomb", "Hold to aim the bomb, let go to throw.", K(c.bomb)),
                    More("Or tap to raise the ring and tap again to throw. The mouse moves the ring."),
                    L("flask", "Drink. Restores health and shield.", K(c.useItem)),
                }
                : new List<Line>
                {
                    L("lock", BombLockLine, "BOMB"),
                    L("flask", "Drink. Restores health and shield.", K(c.useItem)),
                }),

            new Page("Level", "player-pause", new List<Line>
            {
                L("player-pause", "Pause", UIText.KeyLabel(KeyCode.Escape), "P"),
                L("player-play", "Resume", "R"),
                L("power", "Leave the level", "Q"),
            }),
        };
    }

    List<Page> PadPages()
        => new List<Page>
        {
            new Page("Move", "run", new List<Line>
            {
                L("hand-move", "Walk", P(GameAction.Move)),
                L("mouse", "Look", P(GameAction.Look)),
                L("run", "Sprint. Click once; it holds until you stop.", P(GameAction.Sprint)),
                L("arrow-up", "Jump", P(GameAction.Jump)),
                L("arrow-bar-to-down", "Crouch. Press again to stand.", P(GameAction.Crouch)),
            }),

            new Page("Fight", "crosshair", new List<Line>
            {
                L("bullet", "Fire", P(GameAction.Fire)),
                L("crosshair", "Aim down sights", P(GameAction.Aim)),
                L("magazine", "Reload", P(GameAction.Reload)),
                L("fist", "Punch, up close. Saves a round.", P(GameAction.Melee)),
            }),

            new Page("Equipment", "backpack", BombEarned
                ? new List<Line>
                {
                    L("bomb", "Tap to aim the bomb, tap again to throw.", P(GameAction.Bomb)),
                    More("The right stick moves the ring while you aim."),
                    L("flask", "Drink. Restores health and shield.", P(GameAction.UseItem)),
                }
                : new List<Line>
                {
                    L("lock", BombLockLine, "BOMB"),
                    L("flask", "Drink. Restores health and shield.", P(GameAction.UseItem)),
                }),

            new Page("Level", "player-pause", new List<Line>
            {
                L("player-pause", "Pause", P(GameAction.Pause)),
                L("arrow-left", "Back, and resume from pause", InputPrompts.Back),
            }),
        };

    List<Page> TouchPages()
    {
        static string T(GameAction a) => InputPrompts.For(a, InputScheme.Touch, GameInput.Pad);

        return new List<Page>
        {
            new Page("Move", "run", new List<Line>
            {
                L("hand-move", "Walk. Push to the edge to sprint.", "LEFT STICK"),
                L("hand-finger", "Drag anywhere on the right to look.", "DRAG"),
            }),

            new Page("Fight", "crosshair", new List<Line>
            {
                L("bullet", "Fire. Keep dragging to aim while it fires.", T(GameAction.Fire)),
                L("crosshair", "Spread two fingers to aim down sights, pinch to stop.", "PINCH"),
                L("magazine", "Reload", T(GameAction.Reload)),
                L("fist", "Punch, up close. Saves a round.", T(GameAction.Melee)),
                L("arrow-up", "Jump", T(GameAction.Jump)),
                L("arrow-bar-to-down", "Crouch", T(GameAction.Crouch)),
            }),

            new Page("Equipment", "backpack", BombEarned
                ? new List<Line>
                {
                    L("bomb", "Hold, slide to place the ring, let go to throw.", T(GameAction.Bomb)),
                    L("flask", "Drink. Restores health and shield.", T(GameAction.UseItem)),
                }
                : new List<Line>
                {
                    L("lock", BombLockLine, "BOMB"),
                    L("flask", "Drink. Restores health and shield.", T(GameAction.UseItem)),
                }),

            new Page("Level", "player-pause", new List<Line>
            {
                L("player-pause", "Top right. The only way out of a level.", T(GameAction.Pause)),
            }),
        };
    }
}
