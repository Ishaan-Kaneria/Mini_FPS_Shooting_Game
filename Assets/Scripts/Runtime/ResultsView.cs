using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What the results screen looks like, built from the UI kit at runtime by
/// <see cref="LevelResultsUI"/>, which still owns what it does: the keys, the audio, the
/// stars landing one at a time, and where RETRY, NEXT LEVEL and MENU go.
///
///   title      LEVEL CLEARED / YOU WERE KILLED / OUT OF TIME, and where
///   stars      each star's condition, MET or MISSED -- the rule, not just the count
///   figures    time against the limit, kills, score, coins, XP
///   rank       the rank bar after this level's XP
///   progress   achievements this level moved, up to three
///   buttons    RETRY, NEXT LEVEL, MENU
///
/// <b>Built at runtime rather than by the scene builder</b>, for the reason the Loadout and
/// Info screens are: the screen is the same in all seven arenas, and a builder edit would
/// mean rebuilding every one of them to move a label. The old builder-made panel is still
/// in the scenes and stays hidden; <see cref="LevelResultsUI"/> points its fields here.
/// </summary>
public class ResultsView : MonoBehaviour
{
    public GameObject root;
    public FlatRect card;
    public TMP_Text title;
    public TMP_Text where;
    public TMP_Text summary;
    public Image[] starIcons = new Image[3];
    public TMP_Text[] conditions = new TMP_Text[3];
    public TMP_Text[] verdicts = new TMP_Text[3];
    public TMP_Text timeValue, killsValue, scoreValue, coinsValue, xpValue;
    public TMP_Text timeNote;
    public TMP_Text rankText;
    public UIProgressBar rankBar;
    public GameObject achievementsBlock;
    public TMP_Text[] achievementRows = new TMP_Text[3];
    public FlatButton retryButton, nextButton, menuButton;
    public TMP_Text hint;

    bool[] _met = new bool[3];

    public static ResultsView Create(Transform canvas)
    {
        var host = new GameObject("ResultsView", typeof(RectTransform));
        host.transform.SetParent(canvas, false);
        UIKit.Fill((RectTransform)host.transform);

        // Its own canvas, sorted over everything else in the arena. The touch layer is a
        // separate canvas above the HUD, so on a phone the move stick and the fire button
        // were drawn over this card -- and would have taken the taps meant for its buttons.
        var own = host.AddComponent<Canvas>();
        own.overrideSorting = true;
        own.sortingOrder = 500;
        host.AddComponent<GraphicRaycaster>();

        var v = host.AddComponent<ResultsView>();
        v.Build();
        return v;
    }

    void Build()
    {
        var t = UITheme.Active;

        var shade = UIKit.Panel(transform, "Shade", UIKit.PanelTone.Background, t);
        shade.color = new Color(t.background.r, t.background.g, t.background.b, 0.9f);
        shade.borderColor = new Color(0, 0, 0, 0);
        shade.raycastTarget = true;
        UIKit.Fill(shade.rectTransform);
        root = shade.gameObject;

        card = UIKit.Panel(shade.transform, "Card", UIKit.PanelTone.Panel, t);
        card.stripeSide = FlatRect.Side.Top;
        card.stripePixels = t.stripePixels;
        var crt = card.rectTransform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(900f, 0f);
        var fit = card.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var col = UIKit.Column(card, 12f, new RectOffset(36, 36, 28, 26));
        col.childForceExpandHeight = false;
        col.childControlHeight = true;
        // Fits whatever canvas it lands on: a landscape phone is short, and a card taller
        // than the screen puts its buttons below the bottom edge.
        card.gameObject.AddComponent<FitInsideParent>().margin = 24f;

        title = UIKit.Text(card.transform, "Title", "LEVEL CLEARED", UIKit.TextRole.Display, t);
        where = UIKit.Text(card.transform, "Where", "", UIKit.TextRole.Label, t);
        where.color = t.textSecondary;
        summary = UIKit.Text(card.transform, "Summary", "", UIKit.TextRole.Body, t);
        summary.color = t.textPrimary;

        Rule(card.transform, t);
        for (int i = 0; i < 3; i++)
        {
            var row = UIKit.Rect(card.transform, $"Star{i + 1}");
            UIKit.Row(row, 12f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
            UIKit.Size(row).minHeight = 32f;
            starIcons[i] = UIKit.Icon(row, "Icon", "star", 26f, t.textSecondary, t);
            conditions[i] = UIKit.Text(row, "Condition", "", UIKit.TextRole.Body, t);
            conditions[i].textWrappingMode = TextWrappingModes.NoWrap;
            conditions[i].overflowMode = TextOverflowModes.Ellipsis;
            UIKit.Size(conditions[i], flexWidth: 1f);
            verdicts[i] = UIKit.Text(row, "Verdict", "", UIKit.TextRole.Label, t);
            verdicts[i].alignment = TextAlignmentOptions.MidlineRight;
        }
        Rule(card.transform, t);

        var figures = UIKit.Rect(card.transform, "Figures");
        var frow = UIKit.Row(figures, 12f, null, TextAnchor.UpperLeft);
        frow.childForceExpandWidth = true;
        UIKit.Size(figures).minHeight = 64f;
        frow.childControlHeight = true;
        timeValue = Figure(figures, "TIME", t, out timeNote);
        killsValue = Figure(figures, "KILLS", t, out _);
        scoreValue = Figure(figures, "SCORE", t, out _);
        coinsValue = Figure(figures, "COINS", t, out _);
        coinsValue.color = t.accent;
        xpValue = Figure(figures, "XP", t, out _);
        xpValue.color = t.accent;

        var rank = UIKit.Rect(card.transform, "Rank");
        UIKit.Row(rank, 14f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
        UIKit.Size(rank).minHeight = 26f;
        rankText = UIKit.Text(rank, "Title", "", UIKit.TextRole.Label, t);
        rankText.textWrappingMode = TextWrappingModes.NoWrap;
        rankBar = UIKit.ProgressBar(rank, "Bar", t.accent, 6f, false, t);
        UIKit.Size(rankBar, flexWidth: 1f);

        var ach = UIKit.Rect(card.transform, "Achievements");
        var acol = UIKit.Column(ach, 4f);
        acol.childForceExpandHeight = false;
        acol.childControlHeight = true;
        var ahead = UIKit.Text(ach, "Heading", "ACHIEVEMENTS", UIKit.TextRole.Label, t);
        ahead.color = t.textSecondary;
        for (int i = 0; i < achievementRows.Length; i++)
        {
            achievementRows[i] = UIKit.Text(ach, $"Row{i + 1}", "", UIKit.TextRole.Caption, t);
            achievementRows[i].textWrappingMode = TextWrappingModes.NoWrap;
            achievementRows[i].overflowMode = TextOverflowModes.Ellipsis;
        }
        achievementsBlock = ach.gameObject;

        var buttons = UIKit.Rect(card.transform, "Buttons");
        var brow = UIKit.Row(buttons, 12f, null, TextAnchor.MiddleLeft);
        brow.childForceExpandWidth = true;
        UIKit.Size(buttons, height: UIKit.ControlHeight + 4f);
        retryButton = UIKit.Button(buttons, "Retry", "Retry", FlatButton.Variant.Secondary, "refresh", t);
        nextButton = UIKit.Button(buttons, "Next", "Next level", FlatButton.Variant.Primary, "chevron-right", t);
        menuButton = UIKit.Button(buttons, "Menu", "Menu", FlatButton.Variant.Secondary, "arrow-left", t);

        hint = UIKit.Text(card.transform, "Keys", "", UIKit.TextRole.Caption, t);
        hint.color = t.textDisabled;
        hint.alignment = TextAlignmentOptions.Center;

        root.SetActive(false);
    }

    static void Rule(Transform parent, UITheme t)
    {
        var r = UIKit.Rect(parent, "Rule").gameObject.AddComponent<FlatRect>();
        r.raycastTarget = false;
        r.color = t.border;
        UIKit.Size(r, height: 1f);
    }

    static TMP_Text Figure(RectTransform parent, string label, UITheme t, out TMP_Text note)
    {
        var box = UIKit.Rect(parent, label);
        var c = UIKit.Column(box, 2f);
        c.childForceExpandHeight = false;
        var l = UIKit.Text(box, "Label", label, UIKit.TextRole.Caption, t);
        l.color = t.textSecondary;
        var v = UIKit.Text(box, "Value", "", UIKit.TextRole.Number, t);
        v.fontSize = t.sizeHeading;
        v.textWrappingMode = TextWrappingModes.NoWrap;
        note = UIKit.Text(box, "Note", "", UIKit.TextRole.Caption, t);
        note.color = t.accent;
        return v;
    }

    /// <summary>Everything on the card except the stars, which land one at a time.</summary>
    public void Fill(LevelResult r, LevelSet.Level level, string arenaLabel, int xpGained, int xpBefore,
                     bool newBest, List<string> achievementLines, bool nextAvailable, string keys)
    {
        var t = UITheme.Active;
        bool passed = r.Passed;
        card.stripeColor = passed ? t.success : t.danger;
        title.text = r.Title;
        title.color = passed ? t.success : t.danger;

        string levelName = level != null ? level.Label(r.levelIndex) : r.levelName;
        where.text = UIText.Row(arenaLabel?.ToUpperInvariant() ?? "",
                                $"LEVEL {r.levelIndex + 1:00}",
                                string.IsNullOrEmpty(levelName) ? "" : levelName.ToUpperInvariant());
        summary.text = r.Summary;

        var words = Missions.StarConditions(level);
        for (int i = 0; i < 3; i++)
        {
            _met[i] = i < r.stars;
            conditions[i].text = words[i];
            conditions[i].color = t.textSecondary;
            starIcons[i].sprite = t.IconSprite("star");
            starIcons[i].color = t.textDisabled;
            verdicts[i].text = "";
        }

        // Against the limit, never a par: the clock is what ends a level.
        timeValue.text = t.Tabular(LevelButton.Clock(r.timeTaken) + " / " + LevelButton.Clock(r.timeLimit));
        timeNote.text = newBest ? "NEW BEST" : r.ending == LevelResult.Ending.Cleared ? "" : "TIME LIMIT";
        timeNote.color = newBest ? t.accent : t.textDisabled;
        killsValue.text = t.Tabular($"{r.killed}/{r.total}");
        scoreValue.text = t.Tabular(r.score.ToString("N0"));
        coinsValue.text = t.Tabular("+" + Wallet.Format(r.coins));
        xpValue.text = t.Tabular("+" + xpGained.ToString("N0"));

        int after = xpBefore + xpGained;
        int rank = PlayerRank.RankFor(after);
        int into = after - PlayerRank.XpFor(rank), span = PlayerRank.XpFor(rank + 1) - PlayerRank.XpFor(rank);
        bool rankedUp = rank > PlayerRank.RankFor(xpBefore);
        rankText.text = $"{PlayerRank.TitleFor(rank).ToUpperInvariant()}  LV {t.Tabular(rank.ToString(), heading: false)}" +
                        (rankedUp ? $"  <color=#{ColorUtility.ToHtmlStringRGB(t.accent)}>RANK UP</color>" : "");
        rankBar.Value = span > 0 ? into / (float)span : 0f;
        rankBar.Snap();

        int shown = achievementLines != null ? Mathf.Min(achievementLines.Count, achievementRows.Length) : 0;
        achievementsBlock.SetActive(shown > 0);
        for (int i = 0; i < achievementRows.Length; i++)
        {
            achievementRows[i].gameObject.SetActive(i < shown);
            if (i < shown) achievementRows[i].text = achievementLines[i];
        }

        nextButton.gameObject.SetActive(nextAvailable);
        hint.text = keys ?? "";
    }

    /// <summary>A star lands: filled amber, its condition lit and marked MET.</summary>
    public void Land(int index)
    {
        if (index < 0 || index >= 3) return;
        var t = UITheme.Active;
        starIcons[index].sprite = t.IconSprite("star-filled");
        starIcons[index].color = t.accent;
        conditions[index].color = t.textPrimary;
        verdicts[index].text = "MET";
        verdicts[index].color = t.success;
    }

    /// <summary>After the earned stars have landed, the rest say MISSED.</summary>
    public void MarkMissed()
    {
        var t = UITheme.Active;
        for (int i = 0; i < 3; i++)
        {
            if (_met[i]) continue;
            verdicts[i].text = "MISSED";
            verdicts[i].color = t.textDisabled;
        }
    }
}
