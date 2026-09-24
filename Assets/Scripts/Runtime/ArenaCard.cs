using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One arena on the dashboard: a real screenshot of it, its name, one line about it, how far
/// the player has got ("5/8 CLEARED · 12/24 ★"), how hard it is, and a thin stripe of the
/// arena's own colour -- the only place that colour appears.
///
/// <b>Clicking selects; it does not start anything.</b> The selected arena is the one the
/// CURRENT MISSION card describes, and PLAY MISSION is the one button that launches -- so
/// the thing the player presses to play is always the big amber one, and a card is only
/// ever a choice. The selected card wears the amber 2px border.
///
/// <b>Locked is drawn, not hidden.</b> A locked zone is dimmed with a lock and says what
/// opens it. Its button is not interactable, for the reason a locked level tile is not: a
/// card that lights up and then does nothing reads as broken rather than as locked.
///
/// Built by <c>FPSKitMenuBuilder</c> as a template and cloned per arena; everything inside
/// is anchored in fractions of the card, because the grid sizes it.
/// </summary>
public class ArenaCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [Header("Parts")]
    public Button button;
    public FlatRect frame;
    public RawImage thumbnail;
    public FlatRect stripe;
    public GameObject lockShade;
    public TMP_Text lockText;
    public TMP_Text nameText;
    public TMP_Text descriptionText;
    public TMP_Text progressText;
    public TMP_Text difficultyText;
    public FlatRect[] difficultyBars = new FlatRect[0];

    public ArenaCatalog.Entry Entry { get; private set; }
    public bool Unlocked { get; private set; } = true;

    bool _selected, _hover;

    /// <summary>Whether this is the arena the mission card describes. Draws the amber border.</summary>
    public bool Selected
    {
        get => _selected;
        set { _selected = value; Paint(); }
    }

    public void Bind(ArenaCatalog.Entry entry, int arenaIndex, bool unlocked, string lockNote,
                     UnityEngine.Events.UnityAction onChosen, CampaignData campaign = null)
    {
        var t = UITheme.Active;
        Entry = entry;
        Unlocked = unlocked;
        if (entry == null) return;
        name = $"Arena_{entry.Label}";

        if (nameText != null)
        {
            nameText.text = entry.Label.ToUpperInvariant();
            nameText.color = unlocked ? t.textPrimary : t.textSecondary;
        }
        if (descriptionText != null)
        {
            descriptionText.text = entry.description ?? "";
            descriptionText.color = unlocked ? t.textSecondary : t.textDisabled;
        }
        if (progressText != null)
        {
            progressText.text = ProgressLine(entry, t);
            progressText.color = unlocked ? t.textSecondary : t.textDisabled;
        }

        var difficulty = Missions.ForArena(entry, campaign);
        if (difficultyText != null)
        {
            difficultyText.text = Missions.Label(difficulty);
            difficultyText.color = unlocked ? Missions.ColorFor(t, difficulty) : t.textDisabled;
        }
        for (int i = 0; i < difficultyBars.Length; i++)
        {
            if (difficultyBars[i] == null) continue;
            bool lit = i <= (int)difficulty;
            difficultyBars[i].color = !lit ? t.border : unlocked ? Missions.ColorFor(t, difficulty) : t.textDisabled;
        }

        if (stripe != null)
        {
            var c = t.StripeFor(entry.sceneName);
            stripe.color = unlocked ? c : new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.45f, 1f);
        }

        if (thumbnail != null)
        {
            thumbnail.texture = entry.preview;
            // No screenshot: the theme's floor colour still says something about the place,
            // and a card with a hole in it reads as broken.
            thumbnail.color = entry.preview != null
                ? (unlocked ? Color.white : new Color(0.38f, 0.38f, 0.38f, 1f))
                : entry.theme != null ? entry.theme.floorColor : t.panelRaised;
            Cover();
        }

        if (lockShade != null) lockShade.SetActive(!unlocked);
        if (lockText != null) lockText.text = string.IsNullOrEmpty(lockNote) ? "LOCKED" : lockNote.ToUpperInvariant();

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            if (onChosen != null) button.onClick.AddListener(onChosen);
            button.interactable = unlocked;
        }
        Paint();
    }

    /// <summary>"5/8 CLEARED · 12/24 ★", with the star drawn from the icon set.</summary>
    static string ProgressLine(ArenaCatalog.Entry entry, UITheme t)
    {
        int count = entry.LevelCount;
        if (count <= 0) return "NO LEVELS";
        int cleared = LevelProgress.LevelsCleared(entry.ProgressKey, count);
        int stars = LevelProgress.StarsInArena(entry.ProgressKey, count);
        return t.Tabular($"{cleared}/{count}", heading: false) + " CLEARED" + UIText.Separator +
               t.Tabular($"{stars}/{count * 3}", heading: false) + " " + InputPrompts.Glyph("star-filled");
    }

    /// <summary>Crops the 16:9 screenshot to fill whatever shape the card gives it, never stretched.</summary>
    void Cover()
    {
        if (thumbnail == null || thumbnail.texture == null) return;
        var r = thumbnail.rectTransform.rect;
        if (r.width <= 1f || r.height <= 1f) return;
        float texAspect = thumbnail.texture.width / (float)thumbnail.texture.height;
        float boxAspect = r.width / r.height;
        thumbnail.uvRect = boxAspect > texAspect
            ? new Rect(0f, (1f - texAspect / boxAspect) * 0.5f, 1f, texAspect / boxAspect)
            : new Rect((1f - boxAspect / texAspect) * 0.5f, 0f, boxAspect / texAspect, 1f);
    }

    void OnRectTransformDimensionsChange() => Cover();

    public void OnPointerEnter(PointerEventData e) { _hover = Unlocked; Paint(); }
    public void OnPointerExit(PointerEventData e) { _hover = false; Paint(); }
    public void OnSelect(BaseEventData e) => Paint();
    public void OnDeselect(BaseEventData e) => Paint();

    void Paint()
    {
        if (frame == null) return;
        var t = UITheme.Active;
        frame.borderColor = _selected ? t.accent : _hover ? t.borderHover : t.border;
        frame.borderPixels = _selected ? 2f : t.borderPixels;
        frame.color = _hover && !_selected ? t.panelRaised : t.panel;
    }
}
