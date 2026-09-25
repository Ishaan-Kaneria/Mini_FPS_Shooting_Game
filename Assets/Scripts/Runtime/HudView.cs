using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The in-game HUD, built from the UI kit at runtime and driven by the game's own events.
///
///   top centre     mission panel -- level and name, kills, clock -- and the objective strip
///   top right      run panel -- score, rank, kills against the level, coins this run
///   top left       the minimap, smaller and outlined, with N/E/S/W and red triangles
///   under the map  the kill feed: the last three, gone after four seconds
///   bottom left    the player card: health and armour, each a bar and a number
///   bottom centre  the ability slots: bomb and drink, count and key
///   bottom right   the weapon: silhouette, magazine, reserve, fire mode, and the pause key
///   centre         hit markers over <see cref="HUDController"/>'s crosshair
///   while paused   settings, info, stats and quit along the top right
///
/// <b>Nothing here polls.</b> Every readout is written when its source says it changed --
/// <see cref="Health.Changed"/>, <see cref="Weapon.AmmoChanged"/>,
/// <see cref="GameDirector.KillRegistered"/>, <see cref="LevelManager.ObjectiveChanged"/>,
/// <see cref="GameDirector.CoinsChanged"/> and the rest. The only per-frame work is the
/// fading of what is fading: pop-ups, feed rows and the hit marker.
///
/// <b>Built at runtime, not by the scene builder</b>, for the reason the results screen is:
/// it is the same in every arena, and in a level the kit did not build. It is created by
/// <see cref="HUDController"/>, which keeps the parts it still owns -- the crosshair, the
/// briefing, the banner, the boss bar, the bomb cursor, the damage indicators and vignette,
/// and the pause menu -- and hides the builder-made readouts this replaces.
/// </summary>
public class HudView : MonoBehaviour
{
    const float Margin = 16f;
    const float FeedSeconds = 4f;
    const float PopSeconds = 0.9f;
    const float ToastSeconds = 3f;

    HUDController _hud;
    GameDirector _director;
    LevelManager _level;
    Weapon _weapon;
    Health _health;
    BombThrower _bombs;
    ConsumableBelt _belt;
    Canvas _canvas;
    UITheme _t;

    // Mission.
    TMP_Text _levelText, _killsText, _clockText, _objectiveText;
    Image _clockIcon;
    GameObject _objectiveStrip;

    // Run.
    TMP_Text _scoreText, _comboText, _rankText, _runKillsText, _coinText;
    UIProgressBar _killsBar;
    RectTransform _coinPopAnchor;
    int _shownCoins;

    // Player card.
    UIProgressBar _healthBar, _armourBar;
    TMP_Text _healthText, _armourText;
    RectTransform _healthPopAnchor, _armourPopAnchor;
    float _shownShield;

    // Abilities.
    Slot _bombSlot, _drinkSlot;

    // Weapon.
    TMP_Text _weaponName, _magText, _reserveText, _modeText, _pauseHint;

    // Feed and markers.
    RectTransform _feed;
    readonly List<(CanvasGroup group, float until)> _feedRows = new List<(CanvasGroup, float)>();
    readonly List<(TMP_Text text, float born, Vector2 from)> _pops = new List<(TMP_Text, float, Vector2)>();
    FlatRect[] _hitArms;
    float _hitUntil;
    Color _hitColor;

    // Paused.
    GameObject _pauseIcons;
    HudCard _card;
    UIToastStack _toasts;

    // Achievements moving during the run: the lifetime counter plus this run's live delta.
    readonly Dictionary<string, int> _achievementStep = new Dictionary<string, int>();
    int _bossKills;

    class Slot
    {
        public FlatRect face;
        public Image icon;
        public TMP_Text count, key, name;
    }

    // ======================================================================

    public static HudView Create(HUDController hud, GameDirector director)
    {
        var canvas = hud.GetComponentInParent<Canvas>();
        if (canvas == null) return null;
        var host = new GameObject("KitHUD", typeof(RectTransform));
        host.transform.SetParent(canvas.transform, false);
        UIKit.Fill((RectTransform)host.transform);
        // Fitted to the safe area itself, so it does not depend on when UIBootstrap runs.
        var safe = host.AddComponent<SafeAreaFitter>();
        safe.paddingMm = 1f;
        var view = host.AddComponent<HudView>();
        view._hud = hud;
        view._director = director;
        view._canvas = canvas;
        view.Build();
        return view;
    }

    void Build()
    {
        _t = UITheme.Active;
        _level = _hud.levelManager;
        _weapon = _hud.weapon;
        _health = _hud.playerHealth;
        _bombs = _hud.bombs;
        _belt = _hud.belt;

        // What this replaces goes, and stops updating. What stays is HUDController's.
        _hud.legacyReadouts = false;
        _hud.showInstructionStrip = false;
        _hud.tintOnHit = false;
        HideTopLevel(_hud.ammoText); HideTopLevel(_hud.healthText); HideTopLevel(_hud.scoreText);
        HideTopLevel(_hud.comboText); HideTopLevel(_hud.coinText); HideTopLevel(_hud.levelText);
        HideTopLevel(_hud.objectiveText); HideTopLevel(_hud.objectiveFill); HideTopLevel(_hud.healthFill);
        HideTopLevel(_hud.shieldFill); HideTopLevel(_hud.bombPanel); HideTopLevel(_hud.beltPanel);
        HideTopLevel(_hud.instructionText);

        BuildMission();
        BuildRun();
        RestyleMinimap();
        BuildFeed();
        BuildPlayerCard();
        BuildAbilities();
        BuildWeapon();
        if (DeviceProfile.Touched) ArrangeForTouch();
        BuildHitMarker();
        BuildPauseMenu();

        var toastArea = UIKit.Rect(transform, "Toasts");
        toastArea.anchorMin = toastArea.anchorMax = toastArea.pivot = new Vector2(1f, 0.5f);
        toastArea.anchoredPosition = new Vector2(-Margin, 60f);
        toastArea.sizeDelta = new Vector2(380f, 300f);
        _toasts = UIKit.ToastStack(toastArea, "Stack", _t);

        _card = HudCard.Create(_canvas.transform, "HudCard");

        // The boss bar sits under the mission panel and the objective strip rather than
        // where the builder put it, which is now where the strip is.
        if (_hud.bossPanel != null && _hud.bossPanel.transform is RectTransform boss)
            boss.anchoredPosition = new Vector2(boss.anchoredPosition.x, -150f);

        foreach (var a in Achievements.Catalogue) _achievementStep[a.Id] = Step(a.Progress, a.Target);

        MarkTargets();

        _built = true;
        if (isActiveAndEnabled) Subscribe();
    }

    bool _built, _subscribed;
    Minimap _map;

    void OnSetting(string key)
    {
        if (_map != null) _map.rotateWithPlayer = GameSettings.MinimapRotates;
        if (key == "*" || key.StartsWith("key")) OnScheme();
    }

    /// <summary>
    /// Subscribes once the sources are known. Not simply in OnEnable: that runs inside
    /// AddComponent, before <see cref="Create"/> has handed this view its director and
    /// sources, so a subscription there subscribes to nothing -- the panels showed the right
    /// numbers once, from Start, and then never moved again.
    /// </summary>
    void OnEnable()
    {
        if (_built) Subscribe();
    }

    void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        if (_level != null)
        {
            _level.LevelStarted += OnLevelStarted;
            _level.ProgressChanged += OnProgress;
            _level.ObjectiveChanged += OnObjective;
            _level.ClockTicked += OnClock;
        }
        if (_director != null)
        {
            _director.ScoreChanged += OnScore;
            _director.ComboChanged += OnScore;
            _director.CoinsChanged += OnCoins;
            _director.KillRegistered += OnKill;
            _director.PauseChanged += OnPause;
            _director.GameEnded += OnEnded;
        }
        if (_weapon != null)
        {
            _weapon.AmmoChanged += OnAmmo;
            _weapon.ReloadStarted += OnAmmo;
            _weapon.ReloadFinished += OnAmmo;
            _weapon.DealtDamage += OnHit;
        }
        if (_health != null)
        {
            _health.Changed += OnHealth;
            _health.Healed += OnHealed;
        }
        if (_bombs != null) _bombs.ChargesChanged += OnBombs;
        if (_belt != null) _belt.Changed += OnBelt;
        GameInput.SchemeChanged += OnScheme;
        GameSettings.Changed += OnSetting;
        KeyBindings.Changed += OnScheme;
    }

    void OnDisable()
    {
        if (!_subscribed) return;
        _subscribed = false;
        if (_level != null)
        {
            _level.LevelStarted -= OnLevelStarted;
            _level.ProgressChanged -= OnProgress;
            _level.ObjectiveChanged -= OnObjective;
            _level.ClockTicked -= OnClock;
        }
        if (_director != null)
        {
            _director.ScoreChanged -= OnScore;
            _director.ComboChanged -= OnScore;
            _director.CoinsChanged -= OnCoins;
            _director.KillRegistered -= OnKill;
            _director.PauseChanged -= OnPause;
            _director.GameEnded -= OnEnded;
        }
        if (_weapon != null)
        {
            _weapon.AmmoChanged -= OnAmmo;
            _weapon.ReloadStarted -= OnAmmo;
            _weapon.ReloadFinished -= OnAmmo;
            _weapon.DealtDamage -= OnHit;
        }
        if (_health != null)
        {
            _health.Changed -= OnHealth;
            _health.Healed -= OnHealed;
        }
        if (_bombs != null) _bombs.ChargesChanged -= OnBombs;
        if (_belt != null) _belt.Changed -= OnBelt;
        GameInput.SchemeChanged -= OnScheme;
        GameSettings.Changed -= OnSetting;
        KeyBindings.Changed -= OnScheme;
    }

    /// <summary>
    /// Everything once, from the current state: the sources may have raised their first
    /// events before this subscribed, and the order of two Starts is not defined.
    /// </summary>
    void Start()
    {
        OnLevelStarted(_level);
        OnProgress(_level);
        OnObjective(_level);
        OnClock(_level, _level != null && _level.IsRunning ? Mathf.CeilToInt(_level.TimeRemaining) : -1);
        OnScore(_director);
        OnCoins(_director, _director != null ? _director.CoinsEarned : 0);
        OnAmmo(_weapon);
        OnHealth(_health);
        OnBombs(_bombs);
        OnBelt(_belt);
        OnScheme();
        OnPause(_director, _director != null && _director.IsPaused);

        // Loaded from the menu's Settings only to lay the HUD out: freeze it and open the editor.
        if (GameSession.EditingHud && _director != null)
        {
            _director.SetPaused(true);
            HudEditor.Open(_hud, fromMenu: true);
        }
    }

    // ======================================================================
    // Building.
    // ======================================================================

    FlatRect HudPanel(Transform parent, string name)
    {
        var p = UIKit.Panel(parent, name, UIKit.PanelTone.Hud, _t);
        p.raycastTarget = false;
        return p;
    }

    TMP_Text Label(Transform parent, string name, string text, UIKit.TextRole role, Color color)
    {
        var l = UIKit.Text(parent, name, text, role, _t);
        l.color = color;
        l.textWrappingMode = TextWrappingModes.NoWrap;
        l.raycastTarget = false;
        return l;
    }

    Image Icon(Transform parent, string id, float size, Color color) => UIKit.Icon(parent, id, id, size, color, _t);

    FlatRect Divider(Transform parent, float height)
    {
        var d = UIKit.Rect(parent, "Divider").gameObject.AddComponent<FlatRect>();
        d.raycastTarget = false;
        d.color = _t.border;
        UIKit.Size(d, 1f, height);
        return d;
    }

    static ContentSizeFitter Hug(Component c, bool width = true, bool height = true)
    {
        var f = c.gameObject.AddComponent<ContentSizeFitter>();
        if (width) f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        if (height) f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return f;
    }

    static void Corner(RectTransform rt, Vector2 corner, Vector2 offset)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = corner;
        rt.anchoredPosition = offset;
    }

    void BuildMission()
    {
        var stack = UIKit.Rect(transform, "Mission");
        Corner(stack, new Vector2(0.5f, 1f), new Vector2(0f, -Margin));
        var col = UIKit.Column(stack, 6f, null, TextAnchor.UpperCenter);
        col.childForceExpandWidth = false;
        col.childControlHeight = true;
        Hug(stack);

        var panel = HudPanel(stack, "Panel");
        var row = UIKit.Row(panel, 14f, new RectOffset(18, 18, 8, 8), TextAnchor.MiddleCenter);
        row.childControlHeight = true;
        Hug(panel);
        _levelText = Label(panel.transform, "Level", "LEVEL 01", UIKit.TextRole.Heading, _t.textPrimary);
        Divider(panel.transform, 24f);
        Icon(panel.transform, "skull", 20f, _t.textSecondary);
        _killsText = Label(panel.transform, "Kills", "0 / 0", UIKit.TextRole.Number, _t.textPrimary);
        _killsText.fontSize = _t.sizeHeading;
        Divider(panel.transform, 24f);
        _clockIcon = Icon(panel.transform, "clock", 20f, _t.textSecondary);
        _clockText = Label(panel.transform, "Clock", "0:00", UIKit.TextRole.Number, _t.textPrimary);
        _clockText.fontSize = _t.sizeHeading;

        _mission = stack;

        // The task, on its own strip under the panel -- never behind a progress bar, which
        // is where the old HUD drew the star thresholds over it. Its own root, so a layout
        // can move it away from the mission panel.
        var strip = HudPanel(transform, "Objective");
        Corner(strip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -(Margin + 58f)));
        strip.stripeSide = FlatRect.Side.Left;
        strip.stripeColor = _t.accent;
        strip.stripePixels = _t.stripePixels;
        UIKit.Row(strip, 8f, new RectOffset(14, 16, 5, 5), TextAnchor.MiddleLeft);
        Hug(strip);
        Icon(strip.transform, "target", 16f, _t.accent);
        _objectiveText = Label(strip.transform, "Text", "", UIKit.TextRole.Label, _t.textPrimary);
        _objectiveStrip = strip.gameObject;
    }

    void BuildRun()
    {
        var panel = HudPanel(transform, "Run");
        _runPanel = panel.rectTransform;
        Corner(panel.rectTransform, new Vector2(1f, 1f), new Vector2(-Margin, -Margin));
        panel.rectTransform.sizeDelta = new Vector2(250f, 0f);
        var col = UIKit.Column(panel, 6f, new RectOffset(16, 16, 10, 12));
        col.childForceExpandHeight = false;
        col.childControlHeight = true;
        Hug(panel, width: false);

        var scoreRow = UIKit.Rect(panel.transform, "Score");
        UIKit.Row(scoreRow, 8f, null, TextAnchor.LowerLeft).childControlHeight = true;
        var scoreLabel = Label(scoreRow, "Label", "SCORE", UIKit.TextRole.Caption, _t.textSecondary);
        UIKit.Size(scoreLabel, flexWidth: 1f);
        _scoreText = Label(scoreRow, "Value", "0", UIKit.TextRole.Number, _t.textPrimary);
        _scoreText.fontSize = _t.sizeTitle;
        _scoreText.alignment = TextAlignmentOptions.BottomRight;

        _comboText = Label(panel.transform, "Combo", "", UIKit.TextRole.Label, _t.accent);
        _comboText.alignment = TextAlignmentOptions.Right;

        var rankRow = UIKit.Rect(panel.transform, "Rank");
        UIKit.Row(rankRow, 8f, null, TextAnchor.MiddleLeft).childControlHeight = true;
        var rankLabel = Label(rankRow, "Label", "RANK", UIKit.TextRole.Caption, _t.textSecondary);
        UIKit.Size(rankLabel, flexWidth: 1f);
        _rankText = Label(rankRow, "Value", "", UIKit.TextRole.Label, _t.textPrimary);

        var killsRow = UIKit.Rect(panel.transform, "Kills");
        UIKit.Row(killsRow, 8f, null, TextAnchor.MiddleLeft).childControlHeight = true;
        var killsLabel = Label(killsRow, "Label", "KILLS", UIKit.TextRole.Caption, _t.textSecondary);
        UIKit.Size(killsLabel, flexWidth: 1f);
        _runKillsText = Label(killsRow, "Value", "0 / 0", UIKit.TextRole.Label, _t.textPrimary);
        _killsBar = UIKit.ProgressBar(panel.transform, "KillsBar", _t.accent, 4f, false, _t);

        var coinRow = UIKit.Rect(panel.transform, "Coins");
        UIKit.Row(coinRow, 8f, null, TextAnchor.MiddleLeft).childControlHeight = true;
        var coinLabel = Label(coinRow, "Label", "COINS", UIKit.TextRole.Caption, _t.textSecondary);
        UIKit.Size(coinLabel, flexWidth: 1f);
        Icon(coinRow, "coin", 18f, _t.accent);
        _coinText = Label(coinRow, "Value", "0", UIKit.TextRole.Number, _t.accent);
        _coinText.fontSize = _t.sizeHeading;
        _coinPopAnchor = (RectTransform)coinRow;
    }

    void RestyleMinimap()
    {
        var map = FindAnyObjectByType<Minimap>();
        if (map == null || !(map.transform is RectTransform group)) return;
        _minimap = group;

        // A quarter smaller, into the corner margin every other panel keeps.
        group.localScale = Vector3.one * 0.75f;
        group.anchoredPosition = new Vector2(Margin, -Margin);

        foreach (Transform child in group)
        {
            var image = child.GetComponent<Image>();
            if (image == null) continue;
            if (child.name == "Backdrop") image.color = new Color(_t.panel.r, _t.panel.g, _t.panel.b, 0.85f);
            else if (child.name.StartsWith("Edge"))
            {
                image.color = _t.border;
                var r = (RectTransform)child;
                // Thin: the builder drew two units, which at this scale is still a frame.
                r.sizeDelta = new Vector2(Mathf.Min(r.sizeDelta.x, 1.5f) < 2f && r.sizeDelta.x < 3f ? 1.4f : r.sizeDelta.x,
                                          r.sizeDelta.y < 3f ? 1.4f : r.sizeDelta.y);
            }
        }

        _map = map;
        map.rotateWithPlayer = GameSettings.MinimapRotates;
        map.uniformEnemyColor = true;
        map.enemyColor = _t.danger;
        map.bossColor = _t.accent;
        map.minPipPixels = 16f;

        if (map.northPip != null)
        {
            var n = map.northPip.GetComponentInChildren<TMP_Text>();
            if (n != null) StyleCompass(n, _t.accent);
            map.eastPip = Compass(map, "E");
            map.southPip = Compass(map, "S");
            map.westPip = Compass(map, "W");
        }

        // A clearer arrow: brighter, and bigger than the enemies around it.
        if (map.playerMarker != null)
        {
            map.playerMarker.localScale = Vector3.one * 1.8f;
            foreach (var image in map.playerMarker.GetComponentsInChildren<Image>())
                image.color = Color.white;
        }
    }

    RectTransform Compass(Minimap map, string letter)
    {
        var pip = Instantiate(map.northPip.gameObject, map.northPip.parent);
        pip.name = letter + "Pip";
        var text = pip.GetComponentInChildren<TMP_Text>();
        if (text != null)
        {
            text.text = letter;
            StyleCompass(text, _t.textSecondary);
        }
        return (RectTransform)pip.transform;
    }

    void StyleCompass(TMP_Text text, Color color)
    {
        text.font = _t.headingFont != null ? _t.headingFont : text.font;
        text.fontSize = 22f;
        text.color = color;
        text.raycastTarget = false;
    }

    void BuildFeed()
    {
        _feed = UIKit.Rect(transform, "KillFeed");
        // Under the minimap: 300 units scaled to 225, plus the margin, plus a gap.
        Corner(_feed, new Vector2(0f, 1f), new Vector2(Margin, -(Margin + 225f + 10f)));
        _feed.sizeDelta = new Vector2(320f, 0f);
        var col = UIKit.Column(_feed, 4f);
        col.childForceExpandHeight = false;
        col.childForceExpandWidth = false;
        col.childControlHeight = true;
        col.childControlWidth = true;
        Hug(_feed, width: false);
    }

    void BuildPlayerCard()
    {
        var panel = HudPanel(transform, "Player");
        _playerCard = panel.rectTransform;
        Corner(panel.rectTransform, Vector2.zero, new Vector2(Margin, Margin));
        panel.rectTransform.sizeDelta = new Vector2(360f, 0f);
        var col = UIKit.Column(panel, 8f, new RectOffset(16, 16, 12, 12));
        col.childForceExpandHeight = false;
        col.childControlHeight = true;
        Hug(panel, width: false);

        _healthBar = Vital(panel.transform, "Health", "medkit", _t.danger, out _healthText, out _healthPopAnchor);
        _armourBar = Vital(panel.transform, "Armour", "shield", _t.info, out _armourText, out _armourPopAnchor);
    }

    UIProgressBar Vital(Transform parent, string name, string icon, Color color, out TMP_Text number, out RectTransform anchor)
    {
        var row = UIKit.Rect(parent, name);
        var r = UIKit.Row(row, 10f, null, TextAnchor.MiddleLeft);
        r.childControlHeight = true;
        UIKit.Size(row).minHeight = 28f;
        Icon(row, icon, 22f, color);
        var bar = UIKit.ProgressBar(row, "Bar", color, 10f, false, _t);
        UIKit.Size(bar, flexWidth: 1f);
        number = Label(row, "Value", "0", UIKit.TextRole.Number, _t.textPrimary);
        number.fontSize = _t.sizeHeading;
        number.alignment = TextAlignmentOptions.MidlineRight;
        UIKit.Size(number, 56f);
        anchor = (RectTransform)row;
        return bar;
    }

    void BuildAbilities()
    {
        var row = UIKit.Rect(transform, "Abilities");
        Corner(row, new Vector2(0.5f, 0f), new Vector2(0f, Margin));
        var h = UIKit.Row(row, 10f, null, TextAnchor.LowerCenter);
        h.childControlHeight = true;
        Hug(row);
        _abilities = row;
        _bombSlot = AbilitySlot(row, "Bomb", "grenade");
        _drinkSlot = AbilitySlot(row, "Drink", "medkit");
    }

    Slot AbilitySlot(Transform parent, string name, string icon)
    {
        var s = new Slot();
        s.face = HudPanel(parent, name);
        UIKit.Size(s.face, 76f, 76f);
        s.icon = Icon(s.face.transform, icon, 34f, _t.textPrimary);
        var irt = s.icon.rectTransform;
        irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
        irt.anchoredPosition = new Vector2(0f, 6f);
        s.icon.GetComponent<LayoutElement>().ignoreLayout = true;

        s.count = Label(s.face.transform, "Count", "0", UIKit.TextRole.Number, _t.textOnAccent);
        var badge = HudPanel(s.face.transform, "Badge");
        badge.color = _t.accent;
        badge.borderColor = new Color(0, 0, 0, 0);
        Corner(badge.rectTransform, new Vector2(1f, 1f), new Vector2(-3f, -3f));
        UIKit.Row(badge, 0f, new RectOffset(6, 6, 1, 1), TextAnchor.MiddleCenter);
        Hug(badge);
        s.count.transform.SetParent(badge.transform, false);
        s.count.fontSize = _t.sizeLabel;

        s.key = Label(s.face.transform, "Key", "", UIKit.TextRole.Caption, _t.textSecondary);
        s.key.alignment = TextAlignmentOptions.Bottom;
        var krt = s.key.rectTransform;
        krt.anchorMin = new Vector2(0f, 0f); krt.anchorMax = new Vector2(1f, 0f); krt.pivot = new Vector2(0.5f, 0f);
        krt.anchoredPosition = new Vector2(0f, 4f);
        krt.sizeDelta = new Vector2(0f, 20f);
        return s;
    }

    void BuildWeapon()
    {
        var stack = UIKit.Rect(transform, "WeaponStack");
        _weaponStack = stack;
        Corner(stack, new Vector2(1f, 0f), new Vector2(-Margin, Margin));
        var col = UIKit.Column(stack, 6f, null, TextAnchor.LowerRight);
        col.childForceExpandWidth = false;
        col.childControlHeight = true;
        Hug(stack);

        var hint = HudPanel(stack, "PauseHint");
        hint.borderColor = new Color(0, 0, 0, 0);
        UIKit.Row(hint, 0f, new RectOffset(10, 10, 3, 3), TextAnchor.MiddleRight);
        Hug(hint);
        _pauseHint = Label(hint.transform, "Text", "", UIKit.TextRole.Label, _t.textPrimary);
        _pauseHint.alignment = TextAlignmentOptions.Right;

        var panel = HudPanel(stack, "Weapon");
        _weaponPanel = panel.rectTransform;
        var row = UIKit.Row(panel, 16f, new RectOffset(16, 18, 10, 10), TextAnchor.MiddleRight);
        row.childControlHeight = true;
        Hug(panel);

        var left = UIKit.Rect(panel.transform, "Kind");
        var lcol = UIKit.Column(left, 2f, null, TextAnchor.MiddleLeft);
        lcol.childForceExpandHeight = false;
        lcol.childControlHeight = true;
        Icon(left, "rifle", 60f, _t.textPrimary);
        _weaponName = Label(left, "Name", "", UIKit.TextRole.Caption, _t.textSecondary);

        Divider(panel.transform, 44f);

        var ammo = UIKit.Rect(panel.transform, "Ammo");
        var acol = UIKit.Column(ammo, 0f, null, TextAnchor.MiddleRight);
        acol.childForceExpandHeight = false;
        acol.childControlHeight = true;
        var counts = UIKit.Rect(ammo, "Counts");
        UIKit.Row(counts, 6f, null, TextAnchor.LowerRight).childControlHeight = true;
        _magText = Label(counts, "Magazine", "0", UIKit.TextRole.Number, _t.textPrimary);
        _magText.fontSize = _t.sizeDisplay;
        _reserveText = Label(counts, "Reserve", "/ 0", UIKit.TextRole.Number, _t.textSecondary);
        _reserveText.fontSize = _t.sizeHeading;
        _modeText = Label(ammo, "Mode", "", UIKit.TextRole.Caption, _t.accent);
        _modeText.alignment = TextAlignmentOptions.Right;
    }

    RectTransform _abilities, _weaponPanel, _runPanel, _mission, _minimap, _playerCard, _weaponStack;

    /// <summary>
    /// A touch screen has its controls where the pointer layout has panels: the pause button
    /// in the top-right corner and the fire cluster down the right edge (the left, mirrored).
    /// So the run panel moves beside the minimap, into the top-left space a phone leaves free,
    /// and the weapon joins the ability slots in one row along the bottom centre, under the
    /// thumbs rather than beneath one of them.
    /// </summary>
    void ArrangeForTouch()
    {
        if (_runPanel != null) Corner(_runPanel, new Vector2(0f, 1f), new Vector2(Margin + 225f + 12f, -Margin));
        // Beside the slots rather than inside their row: a child of a layout group is placed
        // by the group, and the player has to be able to move the two apart.
        if (_weaponPanel != null)
        {
            _weaponPanel.SetParent(transform, false);
            _weaponPanel.anchorMin = _weaponPanel.anchorMax = new Vector2(0.5f, 0f);
            _weaponPanel.pivot = new Vector2(1f, 0f);
            _weaponPanel.anchoredPosition = new Vector2(-(76f + 10f + 10f), Margin);
        }
    }

    void BuildHitMarker()
    {
        var root = UIKit.Rect(transform.parent, "HitMarker");
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = Vector2.zero;
        _hitArms = new FlatRect[4];
        for (int i = 0; i < 4; i++)
        {
            float angle = 45f + 90f * i;
            var arm = UIKit.Rect(root, "Arm" + i).gameObject.AddComponent<FlatRect>();
            arm.raycastTarget = false;
            arm.borderColor = new Color(0, 0, 0, 0.8f);
            arm.borderPixels = 1f;
            var rt = arm.rectTransform;
            rt.sizeDelta = new Vector2(3f, 12f);
            var dir = Quaternion.Euler(0f, 0f, angle) * Vector3.up;
            rt.anchoredPosition = (Vector2)dir * 16f;
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
            arm.color = new Color(1, 1, 1, 0);
            arm.borderColor = new Color(0, 0, 0, 0);
            _hitArms[i] = arm;
        }
    }

    GameObject _pauseShade, _pauseLayer;

    /// <summary>
    /// The pause menu, flat: a shade over the whole screen, and on it a card with RESUME,
    /// SETTINGS and QUIT TO DASHBOARD, plus the icon row in the corner. HUDController still
    /// owns showing it and what the buttons do -- its pausePanel, resumeButton, quitButton
    /// and pauseHintText are pointed here, and its own builder-made menu stays hidden.
    /// </summary>
    void BuildPauseMenu()
    {
        _pauseShade = UIKit.Panel(_canvas.transform, "PauseShade", UIKit.PanelTone.Background, _t).gameObject;
        var shade = _pauseShade.GetComponent<FlatRect>();
        shade.color = new Color(_t.background.r, _t.background.g, _t.background.b, 0.72f);
        shade.borderColor = new Color(0, 0, 0, 0);
        shade.raycastTarget = true;
        UIKit.Fill(shade.rectTransform);

        var layer = UIKit.Rect(_canvas.transform, "PauseLayer");
        UIKit.Fill(layer);
        layer.gameObject.AddComponent<SafeAreaFitter>().paddingMm = 1f;
        _pauseLayer = layer.gameObject;

        var card = UIKit.Panel(layer, "Card", UIKit.PanelTone.Panel, _t);
        card.stripeSide = FlatRect.Side.Top;
        card.stripeColor = _t.accent;
        card.stripePixels = _t.stripePixels;
        var rt = card.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(520f, 0f);
        Hug(card, width: false);
        card.gameObject.AddComponent<FitInsideParent>();
        var col = UIKit.Column(card, 12f, new RectOffset(32, 32, 26, 26));
        col.childForceExpandHeight = false;
        col.childControlHeight = true;
        UIKit.Text(card.transform, "Title", "Paused", UIKit.TextRole.Display, _t);
        var hint = Label(card.transform, "Hint", "", UIKit.TextRole.Label, _t.textSecondary);
        var resume = UIKit.Button(card.transform, "Resume", "Resume", FlatButton.Variant.Primary, "player-play", _t);
        resume.gameObject.AddComponent<UIDefaultSelection>().priority = 10;
        var restart = UIKit.Button(card.transform, "Restart", "Restart", FlatButton.Variant.Secondary, "refresh", _t);
        restart.onClick.AddListener(() => { if (_director != null) _director.RestartRun(); });
        var settings = UIKit.Button(card.transform, "Settings", "Settings", FlatButton.Variant.Secondary, "settings", _t);
        settings.gameObject.AddComponent<OpenSettingsButton>();
        var achievements = UIKit.Button(card.transform, "Achievements", "Achievements", FlatButton.Variant.Secondary, "trophy", _t);
        achievements.onClick.AddListener(() => AchievementsPanel.Create(_canvas).Open());
        // Its own button, not the HUDController's quit: leaving asks first.
        var quit = UIKit.Button(card.transform, "Quit", "Quit to menu", FlatButton.Variant.Secondary, "power", _t);
        quit.onClick.AddListener(AskToQuit);

        if (_hud.pausePanel != null) _hud.pausePanel.SetActive(false);
        _hud.pausePanel = _pauseShade;
        _hud.resumeButton = resume;
        _hud.quitButton = null;
        _hud.pauseHintText = hint;

        BuildPauseIcons(layer);
        _pauseShade.SetActive(false);
        _pauseLayer.SetActive(false);
    }

    void BuildPauseIcons(Transform parent)
    {
        var row = UIKit.Rect(parent, "PauseIcons");
        Corner(row, new Vector2(1f, 1f), new Vector2(-Margin, -Margin));
        UIKit.Row(row, 8f, null, TextAnchor.MiddleRight);
        Hug(row);
        var settings = UIKit.IconButton(row, "Settings", "settings", "Settings", UIKit.ControlHeight, _t);
        settings.onClick.AddListener(() => SettingsPanel.Show(_canvas));
        var info = UIKit.IconButton(row, "Info", "info-circle", "This level", UIKit.ControlHeight, _t);
        info.onClick.AddListener(ShowLevelInfo);
        var stats = UIKit.IconButton(row, "Stats", "chart-bar", "This run", UIKit.ControlHeight, _t);
        stats.onClick.AddListener(ShowRunStats);
        var quit = UIKit.IconButton(row, "Quit", "power", "Quit to dashboard", UIKit.ControlHeight, _t);
        quit.onClick.AddListener(AskToQuit);
        _pauseIcons = row.gameObject;
        _pauseIcons.SetActive(false);
    }

    /// <summary>
    /// Names every panel for the HUD editor and settles it where this view put it, which
    /// applies the player's saved layout on top.
    /// </summary>
    void MarkTargets()
    {
        Settle(_minimap, "hud.minimap", "Minimap");
        Settle(_mission, "hud.mission", "Mission panel");
        Settle(_objectiveStrip != null ? _objectiveStrip.transform : null, "hud.objective", "Objective strip");
        Settle(_feed, "hud.feed", "Kill feed");
        Settle(_playerCard, "hud.player", "Player card");
        Settle(_abilities, "hud.abilities", "Ability slots");
        Settle(_weaponPanel != null && _weaponPanel.parent == transform ? _weaponPanel : _weaponStack, "hud.weapon", "Weapon panel");
        Settle(_runPanel, "hud.run", "Run panel");
    }

    static void Settle(Component c, string id, string label)
    {
        if (c == null) return;
        HudLayoutTarget.Mark(c, id, label).Settle();
    }

    // ======================================================================
    // Events.
    // ======================================================================

    void OnLevelStarted(LevelManager level)
    {
        if (level == null) return;
        string name = level.LevelName;
        _levelText.text = $"LEVEL {_t.Tabular(level.LevelNumber.ToString("00"))}" +
                          (string.IsNullOrEmpty(name) ? "" : UIText.Separator + name.ToUpperInvariant());
        _rankText.text = $"LV {_t.Tabular(PlayerRank.Rank.ToString(), heading: false)}";
    }

    void OnProgress(LevelManager level)
    {
        if (level == null) return;
        string k = _t.Tabular($"{level.Killed} / {level.TotalEnemies}");
        _killsText.text = k;
        _runKillsText.text = k;
        _killsBar.Value = level.TotalEnemies > 0 ? level.Killed / (float)level.TotalEnemies : 0f;
    }

    void OnObjective(LevelManager level)
    {
        string line = level != null ? level.ObjectiveLine : "";
        _objectiveStrip.SetActive(!string.IsNullOrEmpty(line));
        _objectiveText.text = line ?? "";
    }

    void OnClock(LevelManager level, int seconds)
    {
        if (seconds < 0)
        {
            // Before the level runs, the clock shows what it will start from.
            float limit = level != null ? level.TimeLimit : 0f;
            seconds = Mathf.CeilToInt(limit);
        }
        _clockText.text = _t.Tabular($"{seconds / 60}:{seconds % 60:00}");
        bool warn = level != null && level.IsRunning && seconds < 10;
        _clockText.color = warn ? _t.danger : _t.textPrimary;
        _clockIcon.color = warn ? _t.danger : _t.textSecondary;
    }

    void OnScore(GameDirector d)
    {
        if (d == null) return;
        _scoreText.text = _t.Tabular(d.Score.ToString("N0"));
        _comboText.text = d.Combo > 1 ? $"x{d.ComboMultiplier:0.##}  {d.Combo} CHAIN" : "";
        _comboText.gameObject.SetActive(d.Combo > 1);
    }

    void OnCoins(GameDirector d, int total)
    {
        int gain = total - _shownCoins;
        _shownCoins = total;
        _coinText.text = _t.Tabular(Wallet.Format(total));
        // Under the coin count rather than beside it: the panel is against the right edge,
        // and to its right is off the screen.
        if (gain > 0 && d != null) Pop(_coinPopAnchor, "+" + gain, _t.accent, new Vector2(-30f, -40f));
        CheckAchievements();
    }

    void OnKill(GameDirector.KillInfo kill)
    {
        if (kill.boss) _bossKills++;

        // Red on a kill by the gun. A bomb kill is not a hit under the crosshair.
        if (!kill.byBomb) FlashHit(_t.danger, 0.25f);

        var row = HudPanel(_feed, "Kill");
        UIKit.Row(row, 8f, new RectOffset(10, 12, 4, 4), TextAnchor.MiddleLeft).childControlHeight = true;
        Hug(row);
        Color ink = kill.boss ? _t.accent : _t.textPrimary;
        Label(row.transform, "You", "YOU", UIKit.TextRole.Label, _t.textSecondary);
        Icon(row.transform, kill.byBomb ? "grenade" : "rifle", 18f, ink);
        if (kill.headshot) Icon(row.transform, "crosshair", 14f, ink);
        string who = kill.archetype != null && !string.IsNullOrEmpty(kill.archetype.displayName)
            ? kill.archetype.displayName : kill.archetype != null ? kill.archetype.name : "Enemy";
        Label(row.transform, "Who", who.ToUpperInvariant(), UIKit.TextRole.Label, ink);
        row.transform.SetAsFirstSibling();
        var group = row.gameObject.AddComponent<CanvasGroup>();
        _feedRows.Add((group, Time.unscaledTime + FeedSeconds));
        while (_feedRows.Count > 3)
        {
            if (_feedRows[0].group != null) Destroy(_feedRows[0].group.gameObject);
            _feedRows.RemoveAt(0);
        }

        CheckAchievements();
    }

    void OnHit(Weapon w, DamageInfo info) => FlashHit(Color.white, 0.12f);

    void OnAmmo(Weapon w)
    {
        if (w == null) return;
        var data = w.Data;
        _weaponName.text = data != null ? data.weaponName.ToUpperInvariant() : "";
        _modeText.text = data != null ? data.fireMode.ToString().ToUpperInvariant() : "";
        if (w.IsReloading)
        {
            _magText.text = "--";
            _reserveText.text = "RELOADING";
            return;
        }
        bool infiniteMag = data != null && data.infiniteAmmo;
        bool infiniteReserve = w.InfiniteReserve;
        _magText.text = infiniteMag ? "∞" : _t.Tabular(w.CurrentAmmo.ToString());
        _reserveText.text = infiniteMag ? "" : "/ " + (infiniteReserve ? "∞" : _t.Tabular(w.ReserveAmmo.ToString()));
        _magText.color = !infiniteMag && w.MagazineSize > 0 && w.CurrentAmmo <= w.MagazineSize / 5 ? _t.danger : _t.textPrimary;
    }

    void OnHealth(Health h)
    {
        if (h == null) return;
        _healthBar.Value = h.Normalized;
        _healthText.text = _t.Tabular(Mathf.CeilToInt(h.Current).ToString());
        bool armour = h.maxShield > 0f;
        _armourBar.Value = h.ShieldNormalized;
        _armourText.text = _t.Tabular(Mathf.CeilToInt(h.Shield).ToString());
        _armourBar.transform.parent.gameObject.SetActive(armour);

        // Armour has no gain event of its own; a rise between two changes is one.
        if (h.Shield > _shownShield + 0.5f && _shownShield >= 0f && Time.timeSinceLevelLoad > 0.5f)
            Pop(_armourPopAnchor, "+" + Mathf.RoundToInt(h.Shield - _shownShield), _t.info);
        _shownShield = h.Shield;
    }

    void OnHealed(Health h, float amount)
    {
        if (amount >= 0.5f) Pop(_healthPopAnchor, "+" + Mathf.RoundToInt(amount), _t.success);
    }

    void OnBombs(BombThrower b)
    {
        bool armed = b != null && b.data != null;
        int count = armed ? b.charges : 0;
        FillSlot(_bombSlot, armed, count, GameAction.Bomb);
    }

    void OnBelt(ConsumableBelt b)
    {
        bool equipped = b != null && b.data != null;
        int count = equipped ? b.Count : 0;
        FillSlot(_drinkSlot, equipped, count, GameAction.UseItem);
    }

    void FillSlot(Slot s, bool present, int count, GameAction action)
    {
        bool live = present && count > 0;
        s.icon.color = live ? _t.textPrimary : _t.textDisabled;
        s.face.color = new Color(_t.panel.r, _t.panel.g, _t.panel.b, live ? 0.85f : 0.45f);
        s.count.text = _t.Tabular(count.ToString(), heading: false);
        s.count.transform.parent.gameObject.SetActive(present);
        // No key on a touch screen: the prompt there is the on-screen button's own icon, which
        // drawn under the slot's icon reads as the icon twice -- and the button is right there.
        s.key.text = present && GameInput.Scheme != InputScheme.Touch ? InputPrompts.For(action) : "";
        s.key.color = live ? _t.textSecondary : _t.textDisabled;
    }

    void OnScheme()
    {
        // A phone has the pause button on screen, and a key named in a corner means nothing.
        bool touch = GameInput.Scheme == InputScheme.Touch;
        _pauseHint.transform.parent.gameObject.SetActive(!touch);
        _pauseHint.text = $"PAUSE ({InputPrompts.For(GameAction.Pause)})";
        OnBombs(_bombs);
        OnBelt(_belt);
    }

    void OnPause(GameDirector d, bool paused)
    {
        if (_pauseIcons == null) return;
        _pauseIcons.SetActive(paused);
        _pauseLayer.SetActive(paused);
        // The icons take the run panel's corner while paused, so the panel steps aside --
        // except on a touch screen, where it lives beside the minimap instead.
        if (_runPanel != null && !DeviceProfile.Touched) _runPanel.gameObject.SetActive(!paused);
        if (paused)
        {
            // Over everything on the canvas, the shade and then the menu on it. HUDController
            // switches the shade on (it is its pausePanel); the order is this view's.
            _pauseShade.transform.SetAsLastSibling();
            _pauseLayer.transform.SetAsLastSibling();
        }
        else if (_card != null && _card.IsOpen) _card.Close();
    }

    void OnEnded(GameDirector d)
    {
        _pauseIcons.SetActive(false);
        _pauseLayer.SetActive(false);
        foreach (var row in _feedRows) if (row.group != null) row.group.alpha = 0f;
    }

    // ======================================================================
    // The HUD editor.
    // ======================================================================

    /// <summary>The editor sits on the paused arena; the pause menu steps out from under it and back.</summary>
    public void SetPauseMenuShown(bool shown)
    {
        bool paused = _director != null && _director.IsPaused;
        if (_pauseShade != null) _pauseShade.SetActive(shown && paused);
        if (_pauseLayer != null) _pauseLayer.SetActive(shown && paused);
        if (_runPanel != null && !DeviceProfile.Touched) _runPanel.gameObject.SetActive(!(shown && paused));
    }

    GameObject _feedSample;

    /// <summary>
    /// While the editor is open every element has something in it, so it can be seen and
    /// picked up: the objective strip says OBJECTIVE when the level has none, and the kill
    /// feed shows one sample row. Both go the moment the editor closes.
    /// </summary>
    public void SetEditing(bool editing)
    {
        if (editing)
        {
            // The briefing and banner are words over the middle of the screen, which is where the
            // player is arranging things; they come back when the editor closes.
            if (_hud.briefingText != null) _hud.briefingText.gameObject.SetActive(false);
            if (_hud.bannerGroup != null) _hud.bannerGroup.gameObject.SetActive(false);
            if (string.IsNullOrEmpty(_objectiveText.text)) _objectiveText.text = "OBJECTIVE";
            _objectiveStrip.SetActive(true);
            if (_feedSample == null && _feedRows.Count == 0)
            {
                var row = HudPanel(_feed, "Sample");
                UIKit.Row(row, 8f, new RectOffset(10, 12, 4, 4), TextAnchor.MiddleLeft).childControlHeight = true;
                Hug(row);
                Label(row.transform, "You", "YOU", UIKit.TextRole.Label, _t.textSecondary);
                Icon(row.transform, "rifle", 18f, _t.textPrimary);
                Label(row.transform, "Who", "KILL FEED", UIKit.TextRole.Label, _t.textPrimary);
                _feedSample = row.gameObject;
            }
            Canvas.ForceUpdateCanvases();
            return;
        }
        if (_feedSample != null) Destroy(_feedSample);
        _feedSample = null;
        if (_hud.briefingText != null) _hud.briefingText.gameObject.SetActive(true);
        if (_hud.bannerGroup != null) _hud.bannerGroup.gameObject.SetActive(true);
        OnObjective(_level);
    }

    // ======================================================================
    // Pause cards.
    // ======================================================================

    /// <summary>Leaving a level mid-fight throws it away, so it asks first.</summary>
    void AskToQuit()
    {
        ConfirmDialog.Show(_canvas, "Quit to menu?",
            "This level ends here. Your kills still count, and you keep the coins they paid.",
            "Quit", () => { if (_director != null) _director.ReturnToMenu(); });
    }

    void ShowLevelInfo()
    {
        var level = _level != null ? _level.Level : null;
        if (level == null) return;
        var words = Missions.StarConditions(level);
        string body = string.IsNullOrWhiteSpace(level.brief) ? "" : level.brief.Trim() + "\n\n";
        for (int i = 0; i < 3; i++) body += $"{new string('*', i + 1)}  {words[i]}\n";
        string weight = Missions.WeightNote(level);
        if (!string.IsNullOrEmpty(weight)) body += $"<color=#{ColorUtility.ToHtmlStringRGB(_t.textSecondary)}>{weight}</color>\n";
        body += $"\nTIME LIMIT {LevelButton.Clock(level.timeLimit)}";
        _card.Show($"Level {_level.LevelNumber:00}  {level.Label(_level.LevelIndex)}", body);
    }

    void ShowRunStats()
    {
        if (_director == null) return;
        var d = _director;
        string body = UIText.Row($"{d.Kills} KILLS", $"{d.Headshots} HEADSHOTS", $"{d.BombKills} BY BOMB") + "\n" +
                      UIText.Row($"{d.Score:N0} SCORE", $"BEST CHAIN {d.BestCombo}") + "\n" +
                      UIText.Row($"+{Wallet.Format(d.CoinsEarned)} COINS", $"{Mathf.RoundToInt(d.DamageTaken)} DAMAGE TAKEN");
        _card.Show("This run", body);
    }

    // ======================================================================
    // Achievements that move during the run.
    // ======================================================================

    /// <summary>
    /// The lifetime counters only move when the level is scored, so progress during a run is
    /// the counter plus what this run has added to it. A toast at each quarter of a target
    /// and at the finish -- not on every kill, which would be a toast a second.
    /// </summary>
    void CheckAchievements()
    {
        if (_director == null) return;
        foreach (var a in Achievements.Catalogue)
        {
            int live = a.Progress + RunDelta(a.Id);
            int step = Step(live, a.Target);
            if (!_achievementStep.TryGetValue(a.Id, out int before) || step <= before) continue;
            _achievementStep[a.Id] = step;
            bool done = live >= a.Target;
            _toasts.Push(done ? UIToast.Kind.Reward : UIToast.Kind.Info, done ? "trophy" : "chart-bar",
                         a.Title.ToUpperInvariant(),
                         done ? "COMPLETE" : $"{Mathf.Min(live, a.Target):N0} / {a.Target:N0}", ToastSeconds);
        }
    }

    int RunDelta(string id)
    {
        var d = _director;
        if (id.StartsWith("kills.")) return d.Kills;
        if (id.StartsWith("head.")) return d.Headshots;
        if (id.StartsWith("bomb.")) return d.BombKills;
        if (id.StartsWith("boss.")) return _bossKills;
        if (id.StartsWith("coin.")) return d.CoinsEarned;
        if (id.StartsWith("combo.")) return Mathf.Max(0, d.BestCombo - PlayerStats.BestCombo);
        return 0;
    }

    /// <summary>Quarters of the target reached: 0 to 4.</summary>
    static int Step(int progress, int target) => target <= 0 ? 0 : Mathf.Clamp(progress * 4 / target, 0, 4);

    // ======================================================================
    // Motion: pop-ups, feed rows, the hit marker.
    // ======================================================================

    void Pop(RectTransform anchor, string text, Color color) => Pop(anchor, text, color, new Vector2(50f, 0f));

    void Pop(RectTransform anchor, string text, Color color, Vector2 offset)
    {
        if (anchor == null) return;
        var label = Label(transform, "Pop", text, UIKit.TextRole.Number, color);
        label.fontSize = _t.sizeHeading;
        label.alignment = TextAlignmentOptions.Center;
        var rt = label.rectTransform;
        rt.sizeDelta = new Vector2(120f, 30f);
        // Beside the thing that changed: to the right of it, a little above.
        Vector3 world = anchor.TransformPoint(new Vector3(anchor.rect.xMax, anchor.rect.center.y, 0f));
        rt.position = world;
        Vector2 from = rt.anchoredPosition + offset;
        rt.anchoredPosition = from;
        _pops.Add((label, Time.unscaledTime, from));
    }

    void FlashHit(Color color, float seconds)
    {
        _hitColor = color;
        _hitUntil = Time.unscaledTime + seconds;
    }

    bool _primed;

    void Update()
    {
        // The level's numbers exist once LevelManager has chosen the level, which is in its
        // own Start and so after or before this one's, undefined. Read once, the first
        // frame they are there; everything after that arrives as an event.
        if (!_primed && _level != null && _level.TotalEnemies > 0)
        {
            _primed = true;
            OnLevelStarted(_level);
            OnProgress(_level);
            OnClock(_level, _level.IsRunning ? Mathf.CeilToInt(_level.TimeRemaining) : -1);
        }

        float now = Time.unscaledTime;

        for (int i = _pops.Count - 1; i >= 0; i--)
        {
            var (text, born, from) = _pops[i];
            float k = (now - born) / PopSeconds;
            if (text == null || k >= 1f)
            {
                if (text != null) Destroy(text.gameObject);
                _pops.RemoveAt(i);
                continue;
            }
            text.rectTransform.anchoredPosition = from + new Vector2(0f, 28f * UITheme.EaseOut(k));
            text.alpha = 1f - k * k;
        }

        for (int i = _feedRows.Count - 1; i >= 0; i--)
        {
            var (group, until) = _feedRows[i];
            if (group == null) { _feedRows.RemoveAt(i); continue; }
            float left = until - now;
            if (left <= 0f)
            {
                Destroy(group.gameObject);
                _feedRows.RemoveAt(i);
                continue;
            }
            group.alpha = Mathf.Clamp01(left / 0.4f);
        }

        if (_hitArms != null)
        {
            float a = now < _hitUntil ? 1f : 0f;
            foreach (var arm in _hitArms)
            {
                if (arm == null) continue;
                var c = _hitColor; c.a = a;
                if (arm.color != c)
                {
                    arm.color = c;
                    arm.borderColor = new Color(0, 0, 0, 0.8f * a);
                }
            }
        }
    }

    // ======================================================================

    /// <summary>
    /// Hides what the builder put on the canvas for something this view now shows: the
    /// object directly under the canvas (or the safe-area container), so a label's panel
    /// goes with it rather than leaving an empty frame.
    /// </summary>
    static void HideTopLevel(Component c)
    {
        if (c == null) return;
        var t = c.transform;
        while (t.parent != null && t.parent.GetComponent<Canvas>() == null && t.parent.name != "SafeArea")
            t = t.parent;
        t.gameObject.SetActive(false);
    }

    static void HideTopLevel(GameObject go)
    {
        if (go != null) HideTopLevel(go.transform);
    }
}
