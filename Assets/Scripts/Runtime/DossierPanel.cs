using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// THE LIST: the eight, who is still standing, and what each one said on the way down.
///
/// <b>Derived from the campaign and the player's stars, never stored.</b> Whether a
/// sibling is down is the same question as whether the last level of their zone has been
/// passed -- see <see cref="Campaign.ChildDefeated"/> -- so this screen reads that rather
/// than keeping its own idea of the story's state. It is the same rule
/// <see cref="AchievementsPanel"/> follows for exactly the same reason: a second record
/// is a second thing that can disagree, and it disagrees silently.
///
/// It exists because the beats are shown once. A card read on the way back from a level
/// is read in a hurry, and the one about the gate being a transfer is the hinge of the
/// whole story -- somewhere to go and read it again is the difference between a plot and
/// a thing that flashed past.
/// </summary>
public class DossierPanel : OverlayPanel
{
    [Header("Content")]
    public CampaignData campaign;

    [Tooltip("Hidden row cloned once per name.")]
    public DossierRow rowTemplate;

    [Tooltip("Where rows are parented, left to right. Rows are dealt across them.")]
    public RectTransform[] columns;

    [Tooltip("\"3 OF 8 DOWN\" across the top.")]
    public TMP_Text tallyText;

    readonly List<DossierRow> _rows = new List<DossierRow>();

    /// <summary>
    /// How many columns this screen is read in.
    ///
    /// The same answer <see cref="AchievementsPanel"/> gives and for the same reason: a
    /// row here carries a face, a name and a paragraph, and four of those side by side on
    /// a 147mm handset are four strips too narrow to hold any of it.
    /// </summary>
    int ColumnsForThisScreen() => DeviceProfile.CurrentForm switch
    {
        DeviceProfile.Form.Handset => 1,
        DeviceProfile.Form.Tablet => Mathf.Min(2, columns != null ? columns.Length : 1),
        _ => columns != null ? columns.Length : 1
    };

    protected override void OnOpened()
    {
        foreach (var row in _rows)
            if (row != null) Destroy(row.gameObject);

        _rows.Clear();

        if (campaign == null || rowTemplate == null || columns == null || columns.Length == 0)
        {
            if (tallyText != null) tallyText.text = "";
            return;
        }

        int used = Mathf.Clamp(ColumnsForThisScreen(), 1, columns.Length);

        for (int i = 0; i < columns.Length; i++)
            if (columns[i] != null) columns[i].gameObject.SetActive(i < used);

        int names = campaign.siblings != null ? campaign.siblings.Count : 0;
        int down = 0;

        for (int i = 0; i < names; i++)
        {
            var sibling = campaign.SiblingAt(i);
            if (sibling == null) continue;

            // Which zone this one holds, if any. A sibling with no zone is somebody the
            // story has not put anywhere yet -- the eldest and the last name -- and they
            // are neither met nor down until it does.
            int zone = ZoneOf(i);

            bool isDown = zone >= 0 && Campaign.ChildDefeated(campaign, zone);
            bool met = zone >= 0 && Campaign.ZoneUnlocked(campaign, zone);

            if (isDown) down++;

            var row = Instantiate(rowTemplate, columns[_rows.Count % used]);
            row.gameObject.SetActive(true);
            row.Bind(sibling, met, isDown);

            _rows.Add(row);
        }

        if (tallyText != null)
            tallyText.text = names > 0 ? $"{down} OF {names} DOWN" : "";
    }

    int ZoneOf(int siblingIndex)
    {
        for (int i = 0; i < campaign.ZoneCount; i++)
        {
            var zone = campaign.ZoneAt(i);
            if (zone != null && zone.siblingIndex == siblingIndex) return i;
        }

        return -1;
    }
}
