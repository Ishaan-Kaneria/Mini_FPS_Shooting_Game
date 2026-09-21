using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What the player has done, grouped, with how far along each unfinished one is.
///
/// <b>Rebuilt every time it opens, never cached.</b> <see cref="Achievements"/> derives
/// everything from the lifetime counters rather than storing an earned flag, so a row built
/// once and kept would be the only thing in the game that could disagree with the counters
/// -- and it would disagree silently, which is the worst kind.
///
/// <b>Progress is shown on the ones not yet earned, and only on those.</b> A finished
/// achievement showing "500 / 500" is spending a line to say something the tick already
/// said; an unfinished one showing nothing is a locked box with no indication whether it is
/// close or hopeless, which is what makes a list of achievements feel like a list of things
/// somebody else did.
/// </summary>
public class AchievementsPanel : OverlayPanel
{
    [Header("Content")]
    [Tooltip("Hidden row cloned once per achievement.")]
    public AchievementRow rowTemplate;

    [Tooltip("Where rows are parented. One per category, in Achievements.Category order.")]
    public RectTransform[] categoryColumns;

    [Tooltip("Heading above each column.")]
    public TMP_Text[] categoryHeadings;

    [Tooltip("\"7 / 20 COMPLETE\" across the top.")]
    public TMP_Text tallyText;

    readonly System.Collections.Generic.List<AchievementRow> _rows = new();

    protected override void OnOpened()
    {
        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);

        foreach (var row in _rows)
            if (row != null) Destroy(row.gameObject);
        _rows.Clear();

        if (tallyText != null)
            tallyText.text = $"{Achievements.EarnedCount} <size=60%>OF</size> {Achievements.Total}";

        var groups = (Achievements.Category[])System.Enum.GetValues(typeof(Achievements.Category));

        for (int g = 0; g < groups.Length; g++)
        {
            if (categoryHeadings != null && g < categoryHeadings.Length && categoryHeadings[g] != null)
                categoryHeadings[g].text = groups[g].ToString().ToUpperInvariant();

            if (categoryColumns == null || g >= categoryColumns.Length || categoryColumns[g] == null)
                continue;

            foreach (var entry in Achievements.In(groups[g]))
            {
                var row = Instantiate(rowTemplate, categoryColumns[g]);
                row.gameObject.SetActive(true);
                row.Bind(entry);
                _rows.Add(row);
            }
        }
    }
}
