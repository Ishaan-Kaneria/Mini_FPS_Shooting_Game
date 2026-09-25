using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ACHIEVEMENTS, on the UI kit:
///
///   header   "X / Y COMPLETED" and one bar for the whole list
///   filters  ALL, COMBAT, PROGRESSION, CHALLENGE, ECONOMY
///   rows     a flat icon in one colour, the name, what it asks, a bar with "28 / 50", the
///            reward (coins and XP), and -- once done -- a check, and CLAIM while the coins wait
///
/// Every achievement is derived (see <see cref="Achievements"/>): progress is read from the
/// lifetime counters the moment the screen opens, so nothing here can disagree with them. The
/// XP is automatic, because rank reads it off the earned count; the coins are claimed, because
/// a reward arriving silently is a reward nobody notices.
///
/// The builder makes only the screen (the shade this switches on); what is on it is built
/// here, at runtime, from the kit -- the same as the Loadout and Settings screens -- so the
/// layout lives in one file.
/// </summary>
public class AchievementsPanel : OverlayPanel
{
    [Tooltip("Where the screen's content goes. Built into on first open.")]
    public RectTransform content;

    UITheme _t;
    bool _built;
    TMP_Text _tally;
    UIProgressBar _overall;
    UITabBar _filters;
    RectTransform _list;
    int _filter;
    readonly List<GameObject> _rows = new List<GameObject>();

    /// <summary>Raised after a claim, so the balance shown elsewhere moves at once.</summary>
    public event System.Action Claimed;

    /// <summary>
    /// The screen on any canvas, built at runtime -- how the pause menu opens it in an arena,
    /// which has no dashboard to own one.
    /// </summary>
    public static AchievementsPanel Create(Canvas canvas)
    {
        var existing = canvas.GetComponentInChildren<AchievementsPanel>(true);
        if (existing != null) return existing;
        var t = UITheme.Active;
        var host = new GameObject("AchievementsHost", typeof(RectTransform));
        host.transform.SetParent(canvas.transform, false);
        UIKit.Fill((RectTransform)host.transform);
        var own = host.AddComponent<Canvas>();
        own.overrideSorting = true;
        own.sortingOrder = 750;
        host.AddComponent<GraphicRaycaster>();
        var p = host.AddComponent<AchievementsPanel>();
        var shade = UIKit.Panel(host.transform, "Achievements", UIKit.PanelTone.Background, t);
        shade.borderColor = new Color(0, 0, 0, 0);
        shade.raycastTarget = true;
        UIKit.Fill(shade.rectTransform);
        var content = UIKit.Rect(shade.transform, "Content");
        UIKit.Fill(content);
        p.panel = shade.gameObject;
        p.content = content;
        shade.gameObject.SetActive(false);
        return p;
    }

    protected override void OnOpened()
    {
        if (!_built) Build();
        Refresh();
    }

    void Build()
    {
        _built = true;
        _t = UITheme.Active;
        var root = content != null ? content : (RectTransform)panel.transform;
        // Added now, not by the builder: SafeAreaCanvas skips any canvas that already holds a
        // fitter when it wakes, so one baked into the scene took the whole dashboard's top bar
        // and rail out of the safe area.
        var fit = root.gameObject.AddComponent<SafeAreaFitter>();
        fit.paddingMm = 0f;
        fit.fitVertically = false;
        fit.Apply();
        var col = UIKit.Column(root, 14f, new RectOffset(40, 40, 24, 24));
        col.childForceExpandHeight = false;
        col.childControlHeight = true;

        var header = UIKit.Rect(root, "Header");
        UIKit.Row(header, 16f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
        UIKit.Size(header).minHeight = 52f;
        var title = UIKit.Text(header, "Title", "Achievements", UIKit.TextRole.Title, _t);
        UIKit.Size(title, flexWidth: 1f);
        _tally = UIKit.Text(header, "Tally", "", UIKit.TextRole.Heading, _t);
        _tally.color = _t.accent;
        var back = UIKit.Button(header, "Back", "Back", FlatButton.Variant.Quiet, "arrow-left", _t);
        back.onClick.AddListener(Close);
        backButton = back;

        _overall = UIKit.ProgressBar(root, "Overall", _t.accent, 8f, false, _t);

        _filters = UIKit.TabBar(root, "Filters", new[] { "All", "Combat", "Progression", "Challenge", "Economy" }, _t);
        UIKit.Size(_filters).minHeight = 48f;
        _filters.onChanged.AddListener(i => { _filter = i; Refresh(); });

        var viewport = UIKit.Rect(root, "Rows");
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
        _list = UIKit.Rect(viewport, "List");
        _list.anchorMin = new Vector2(0f, 1f); _list.anchorMax = new Vector2(1f, 1f); _list.pivot = new Vector2(0.5f, 1f);
        _list.offsetMin = _list.offsetMax = Vector2.zero;
        var lcol = UIKit.Column(_list, 8f);
        lcol.childForceExpandHeight = false;
        lcol.childControlHeight = true;
        _list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = _list;
    }

    void Refresh()
    {
        foreach (var r in _rows) if (r != null) Destroy(r);
        _rows.Clear();

        int earned = Achievements.EarnedCount, total = Achievements.Total;
        _tally.text = _t.Tabular($"{earned} / {total}") + " COMPLETED";
        _overall.Value = total > 0 ? earned / (float)total : 0f;
        _overall.Snap();

        foreach (var a in Achievements.Catalogue)
        {
            if (_filter > 0 && (int)a.Group != _filter - 1) continue;
            Row(a);
        }
    }

    static string IconFor(Achievements.Category c) => c switch
    {
        Achievements.Category.Combat => "crosshair",
        Achievements.Category.Progression => "star",
        Achievements.Category.Challenge => "bolt",
        _ => "coin",
    };

    void Row(Achievements.Entry a)
    {
        bool done = a.Earned;
        bool claimed = Achievements.Claimed(a.Id);
        bool handset = DeviceProfile.CurrentForm == DeviceProfile.Form.Handset;

        var row = UIKit.Panel(_list, "Row_" + a.Id, UIKit.PanelTone.Panel, _t);
        if (done)
        {
            row.stripeSide = FlatRect.Side.Left;
            row.stripeColor = _t.accent;
            row.stripePixels = _t.stripePixels;
        }
        var h = UIKit.Row(row, 16f, new RectOffset(18, 18, 10, 10), TextAnchor.MiddleLeft);
        h.childForceExpandWidth = false;
        h.childControlHeight = true;
        UIKit.Size(row).minHeight = 76f;

        // One flat icon in one colour: amber once earned, grey until then.
        UIKit.Icon(row.transform, "Icon", IconFor(a.Group), 28f, done ? _t.accent : _t.textSecondary, _t);

        var text = UIKit.Rect(row.transform, "Text");
        var tc = UIKit.Column(text, 2f);
        tc.childForceExpandHeight = false;
        tc.childControlHeight = true;
        // Text and bar split the free width evenly, with the same fixed floor, so the
        // split never depends on how long a title is and every row's bar lines up.
        float column = handset ? 140f : 220f;
        UIKit.Size(text, column, flexWidth: 1f);
        var name = UIKit.Text(text, "Name", a.Title.ToUpperInvariant(), UIKit.TextRole.Heading, _t);
        name.textWrappingMode = TextWrappingModes.NoWrap;
        var detail = UIKit.Text(text, "Detail", a.Detail, UIKit.TextRole.Caption, _t);
        detail.color = _t.textSecondary;
        detail.textWrappingMode = TextWrappingModes.NoWrap;
        detail.overflowMode = TextOverflowModes.Ellipsis;

        var progress = UIKit.Rect(row.transform, "Progress");
        var pc = UIKit.Column(progress, 4f, null, TextAnchor.MiddleRight);
        pc.childForceExpandHeight = false;
        pc.childControlHeight = true;
        UIKit.Size(progress, column, flexWidth: 1f);
        var bar = UIKit.ProgressBar(progress, "Bar", done ? _t.accent : _t.info, 6f, false, _t);
        bar.Value = a.Fraction;
        bar.Snap();
        var count = UIKit.Text(progress, "Count", _t.Tabular($"{a.Progress:N0} / {a.Target:N0}", heading: false), UIKit.TextRole.Label, _t);
        count.alignment = TextAlignmentOptions.Right;
        count.color = done ? _t.textPrimary : _t.textSecondary;

        var reward = UIKit.Rect(row.transform, "Reward");
        var rc = UIKit.Column(reward, 2f, null, TextAnchor.MiddleLeft);
        rc.childForceExpandHeight = false;
        rc.childControlHeight = true;
        UIKit.Size(reward, handset ? 90f : 120f, flexWidth: 0f);
        var coinRow = UIKit.Rect(reward, "Coins");
        UIKit.Row(coinRow, 6f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
        UIKit.Icon(coinRow, "Coin", "coin", 16f, _t.accent, _t);
        var coins = UIKit.Text(coinRow, "Amount", _t.Tabular(Wallet.Format(Achievements.CoinReward(a.Id)), heading: false), UIKit.TextRole.Label, _t);
        coins.color = _t.accent;
        var xp = UIKit.Text(reward, "Xp", $"+{Achievements.XpReward} XP", UIKit.TextRole.Caption, _t);
        xp.color = _t.textSecondary;

        var state = UIKit.Rect(row.transform, "State");
        UIKit.Row(state, 6f, null, TextAnchor.MiddleCenter).childForceExpandWidth = false;
        UIKit.Size(state, 120f, flexWidth: 0f);
        if (done && !claimed)
        {
            var claim = UIKit.Button(state, "Claim", "Claim", FlatButton.Variant.Primary, "gift", _t);
            string id = a.Id;
            claim.onClick.AddListener(() =>
            {
                if (Achievements.Claim(id) > 0) Claimed?.Invoke();
                Refresh();
            });
        }
        else if (done)
        {
            UIKit.Icon(state, "Check", "check", 22f, _t.success, _t);
            var c = UIKit.Text(state, "Claimed", "CLAIMED", UIKit.TextRole.Label, _t);
            c.color = _t.success;
        }
        _rows.Add(row.gameObject);
    }
}
