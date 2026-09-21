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

    [Tooltip("Heading above each column. Used only when every category has a column to itself.")]
    public TMP_Text[] categoryHeadings;

    [Tooltip("Hidden heading cloned into the row stream when categories share a column.")]
    public InstructionRow headingTemplate;

    [Tooltip("\"7 / 20 COMPLETE\" across the top.")]
    public TMP_Text tallyText;

    readonly System.Collections.Generic.List<AchievementRow> _rows = new();
    readonly System.Collections.Generic.List<InstructionRow> _headings = new();

    /// <summary>
    /// How many columns this screen should be read in.
    ///
    /// <b>Not the desktop layout at a smaller size.</b> Four columns on a 147mm handset is
    /// four strips too narrow to hold "Clear 10 levels with half the clock left", and
    /// scrolling them makes that survivable rather than good. A handset gets one column of
    /// full-width rows and scrolls through the lot; a tablet gets two. The categories stack
    /// in the same order either way, so a player who learns the screen on a monitor finds
    /// the same list in the same sequence on a phone.
    /// </summary>
    static int ColumnsForThisScreen(int categories) => DeviceProfile.CurrentForm switch
    {
        DeviceProfile.Form.Handset => 1,
        DeviceProfile.Form.Tablet => 2,
        _ => categories,
    };

    /// <summary>Switches a whole column off, scroll view and all.</summary>
    void SetColumnShown(int index, bool shown)
    {
        var column = categoryColumns[index];

        // The column the rows are parented to is the scroll view's content, so the object
        // to switch is its grandparent -- the viewport is in between.
        var viewport = column.parent as RectTransform;
        var root = viewport != null ? viewport.parent as RectTransform : null;

        (root != null ? root.gameObject : column.gameObject).SetActive(shown);
    }

    /// <summary>
    /// Below this a row stops being readable, so the screen would rather scroll than shrink
    /// past it -- except it cannot scroll, which is why the catalogue is kept to what fits.
    /// </summary>
    const float MinRowHeight = 56f;

    /// <summary>What the row template was authored at. Never exceeded.</summary>
    const float MaxRowHeight = 92f;

    protected override void OnOpened()
    {
        if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);

        foreach (var row in _rows)
            if (row != null) Destroy(row.gameObject);
        _rows.Clear();

        foreach (var heading in _headings)
            if (heading != null) Destroy(heading.gameObject);
        _headings.Clear();

        if (tallyText != null)
            tallyText.text = $"{Achievements.EarnedCount} <size=60%>OF</size> {Achievements.Total}";

        var groups = (Achievements.Category[])System.Enum.GetValues(typeof(Achievements.Category));

        int wide = ColumnsForThisScreen(groups.Length);

        // With a column each, the heading sits above its column. Sharing a column, it has
        // to travel with its rows instead -- a heading pinned above a column that now holds
        // two categories would label only the first of them.
        bool headingsAbove = wide >= groups.Length;

        if (categoryColumns != null)
            for (int c = 0; c < categoryColumns.Length; c++)
                if (categoryColumns[c] != null)
                    SetColumnShown(c, c < wide);

        if (categoryHeadings != null)
            for (int h = 0; h < categoryHeadings.Length; h++)
                if (categoryHeadings[h] != null)
                    categoryHeadings[h].gameObject.SetActive(headingsAbove && h < wide);

        for (int g = 0; g < groups.Length; g++)
        {
            int target = wide <= 0 ? 0 : g % wide;

            if (categoryColumns == null || target >= categoryColumns.Length ||
                categoryColumns[target] == null)
                continue;

            if (headingsAbove)
            {
                if (categoryHeadings != null && g < categoryHeadings.Length &&
                    categoryHeadings[g] != null)
                    categoryHeadings[g].text = groups[g].ToString().ToUpperInvariant();
            }
            else if (headingTemplate != null)
            {
                var heading = Instantiate(headingTemplate, categoryColumns[target]);
                heading.gameObject.SetActive(true);
                heading.Bind("", groups[g].ToString().ToUpperInvariant());
                if (heading.saysText != null) heading.saysText.color = UITheme.Hazard;
                _headings.Add(heading);
            }

            foreach (var entry in Achievements.In(groups[g]))
            {
                var row = Instantiate(rowTemplate, categoryColumns[target]);
                row.gameObject.SetActive(true);
                row.Bind(entry);
                _rows.Add(row);
            }
        }

        // Not fitted here. A rect enabled this frame has not been through a layout pass,
        // so its height is still zero and dividing by it sizes every row to the minimum.
        // Update does it once the canvas has measured itself, the same way the level
        // select fits its grid.
    }

    protected override void Update()
    {
        base.Update();

        if (IsOpen) FitRows();
    }

    /// <summary>
    /// Sizes every row so the longest column fits the height it was given.
    ///
    /// <b>A VerticalLayoutGroup neither clips nor scrolls</b> -- content that does not fit
    /// is simply drawn past the edge, which is how the eighth combat achievement ended up
    /// half off the bottom of the screen. Same failure as the arena grid, and the same fix:
    /// divide a measured box rather than trusting a fixed size authored at one window
    /// height.
    ///
    /// One height for every column, taken from the longest, because rows that differ in
    /// height between columns read as four unrelated lists rather than one screen.
    ///
    /// Shrinking stops at <see cref="MinRowHeight"/> and the scroll view takes over from
    /// there. The two compose on purpose: on a monitor everything fits and nothing scrolls,
    /// and on a handset -- where twenty rows were never going to fit -- it scrolls rather
    /// than shrinking the text past reading.
    /// </summary>
    void FitRows()
    {
        if (_rows.Count == 0 || categoryColumns == null) return;

        int longest = 0;
        float available = 0f;

        foreach (var column in categoryColumns)
        {
            if (column == null) continue;

            longest = Mathf.Max(longest, column.childCount);

            // The viewport, not the column. The column now carries a ContentSizeFitter, so
            // its own height *is* the height of its rows -- measuring it and then dividing
            // by the row count would size every row to a fraction of itself, shrinking a
            // little more on every frame.
            var viewport = column.parent as RectTransform;
            if (viewport != null) available = Mathf.Max(available, viewport.rect.height);
        }

        if (longest == 0 || available <= 1f) return;

        var stack = categoryColumns[0] != null
            ? categoryColumns[0].GetComponent<VerticalLayoutGroup>()
            : null;
        float spacing = stack != null ? stack.spacing : 0f;

        float height = Mathf.Clamp((available - spacing * (longest - 1)) / longest,
                                   MinRowHeight, MaxRowHeight);

        foreach (var row in _rows)
        {
            if (row == null) continue;

            var element = row.GetComponent<LayoutElement>();
            if (element == null) continue;

            element.preferredHeight = height;
            element.minHeight = height;
        }
    }
}
