using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One arena on the dashboard: a preview image, a name, and what the player has
/// managed there.
///
/// Built once by the dashboard builder as a hidden template and cloned per catalog
/// entry at runtime, so adding an arena is an entry in <see cref="ArenaCatalog"/> and
/// never a rebuild of the menu scene.
/// </summary>
public class ArenaCard : HoverCard
{
    [Header("Parts")]
    public Button button;
    public Image preview;
    public Image accentBar;
    public TMP_Text nameText;
    public TMP_Text descriptionText;
    public TMP_Text bestText;

    [Header("Feel")]
    [Tooltip("How far the preview is held back from full brightness at rest. The card " +
             "lighting up under the pointer is most of what says it can be clicked.")]
    [Range(0f, 0.6f)] public float previewDim = 0.22f;

    /// <summary>The preview's colour before the hover tint. Either white over a
    /// screenshot, or the theme's own colour when there is no screenshot to show.</summary>
    Color _previewBase = Color.white;

    /// <summary>The catalog entry this card is showing. Null on the template.</summary>
    public ArenaCatalog.Entry Entry { get; private set; }

    /// <summary>
    /// Fills the card in. The progress line is the arena's ladder rather than a single
    /// best run: how many of its levels have been cleared, and how many of the stars
    /// they were worth have actually been taken. "3/8 LEVELS - 7 STARS" says both what
    /// there is left to do here and how well it has been done, which one best-ever
    /// number never could.
    /// </summary>
    /// <summary>
    /// Vertical bands of the card, as fractions of its height: where the name sits on a
    /// desktop, and where it sits on a phone once the description below it is gone.
    /// </summary>
    const float NameBottom = 0.335f, NameTop = 0.448f, DescriptionBottom = 0.115f;

    /// <summary>
    /// Hands the name the space the description gave up.
    ///
    /// <b>Hiding the description does not, on its own, make anything else bigger.</b>
    /// Every band on this card is a fraction of the card, and the name's band is 11% of
    /// it whether or not there is anything underneath. The name auto-sizes to the box it
    /// is given, so the box is the thing that has to change -- leave it alone and the
    /// phone gets the same small title with a gap under it, which is the worst of both.
    ///
    /// Tripling the band is what actually turns the title into something readable at
    /// arm's length, and it is why the description is hidden rather than merely made
    /// shorter: the space is worth more spent on the one word that says where you are
    /// going.
    /// </summary>
    void ApplyPhoneLayout()
    {
        if (nameText == null || !PhoneUI.Active) return;

        var rect = nameText.rectTransform;
        rect.anchorMin = new Vector2(rect.anchorMin.x, DescriptionBottom);
        rect.anchorMax = new Vector2(rect.anchorMax.x, NameTop);

        // The ceiling has to come up with the box, or auto-sizing stops at the number
        // that was chosen for a band a third the height.
        nameText.fontSizeMax = nameText.fontSizeMax * ((NameTop - DescriptionBottom) /
                                                       (NameTop - NameBottom));
    }

    /// <summary>
    /// The colour this arena wears on every screen it appears on.
    ///
    /// <b>Not the theme's accent, and that distinction is the whole of why this grid used
    /// to read as one colour.</b> <c>LevelTheme.accentLightColor</c> is a *lighting* value
    /// -- what the practicals in that arena glow -- so it is muted by the job it actually
    /// does, and three of the six themes never set it at all and fell back to the same
    /// orange. A card's colour is an identity: it has to be bright, and it has to be
    /// unmistakable beside the other five. Those are different requirements and they now
    /// come from different places.
    /// </summary>
    public Color Signal { get; private set; } = UITheme.Hazard;

    public void Bind(ArenaCatalog.Entry entry, int arenaIndex,
                     UnityEngine.Events.UnityAction onChosen)
    {
        Signal = UITheme.SignalFor(arenaIndex);

        ApplyPhoneLayout();

        Entry = entry;
        if (entry == null) return;

        name = $"Arena_{entry.Label}";

        if (nameText != null) nameText.text = entry.Label.ToUpperInvariant();
        if (descriptionText != null) descriptionText.text = entry.description ?? "";

        if (bestText != null) bestText.text = ProgressLine(entry);

        if (accentBar != null) accentBar.color = Signal;

        if (preview != null)
        {
            if (entry.preview != null)
            {
                preview.sprite = Sprite.Create(
                    entry.preview,
                    new Rect(0f, 0f, entry.preview.width, entry.preview.height),
                    new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);

                _previewBase = Color.white;
            }
            else
            {
                // No screenshot: the theme's own floor colour still says something about
                // the place, and a card with a blank hole in it reads as broken.
                preview.sprite = null;
                _previewBase = entry.theme != null
                    ? entry.theme.floorColor
                    : new Color(0.110f, 0.133f, 0.169f);
            }

            ApplyHover(0f);
        }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            if (onChosen != null) button.onClick.AddListener(onChosen);
        }
    }

    static string ProgressLine(ArenaCatalog.Entry entry)
    {
        int count = entry.LevelCount;
        if (count <= 0) return "NO LEVELS";

        int cleared = LevelProgress.LevelsCleared(entry.ProgressKey, count);
        int stars = LevelProgress.StarsInArena(entry.ProgressKey, count);

        return cleared <= 0
            ? UIText.Row($"{count} LEVELS", "NOT PLAYED")
            : UIText.Row($"{cleared}/{count} LEVELS",
                         $"{stars} STAR{(stars == 1 ? "" : "S")}");
    }

    /// <summary>Adds the preview brightening and the accent bar to the shared growth.</summary>
    protected override void ApplyHover(float amount)
    {
        base.ApplyHover(amount);

        if (preview != null)
            preview.color = Color.Lerp(_previewBase * (1f - previewDim), _previewBase, amount);

        if (accentBar != null)
        {
            var bar = accentBar.rectTransform;
            bar.localScale = new Vector3(1f, Mathf.Lerp(1f, 2.4f, amount), 1f);
        }
    }
}
