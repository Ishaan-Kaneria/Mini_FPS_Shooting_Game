using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One level on the level select screen: a strip of the arena it is fought in, its number
/// and name, how many enemies and how long the clock gives, a red BOSS tag when there is
/// one, the three things each star asks for, and the best the player has done on it.
///
/// Built once by the dashboard builder as a hidden template and cloned per level at
/// runtime, the same way <see cref="ArenaCard"/> is -- so the number of levels an arena
/// offers is a property of its <see cref="LevelSet"/> and never of the menu scene.
///
/// <b>The stars are conditions, not decoration.</b> Each row says what that star asks for,
/// in the numbers <see cref="LevelResult.StarsFor"/> actually cuts with
/// (<see cref="Missions.StarConditions"/>), and is filled amber once it has been earned. A
/// star the player cannot read the rule for is a star they cannot aim at.
///
/// <b>A locked tile is still drawn in full.</b> Seeing that level 6 is a boss with a minute
/// on the clock is most of the reason to go and beat level 5; the strip is shaded, carries
/// a lock and says what opens it, and the button is not interactable, for the reason a
/// locked arena card is not: a card that lights up and then does nothing reads as broken.
///
/// <b>The strip is the arena's own screenshot</b>, a band of it panned a little further
/// across for each rung. There is one real picture per arena and eight levels in it, so
/// eight identical crops would be honest and dull and eight invented ones would not be
/// honest; a pan across the real one is both.
/// </summary>
public class LevelButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [Header("Parts")]
    public Button button;
    public FlatRect frame;
    public RawImage strip;
    public FlatRect stripe;
    public GameObject lockShade;
    public TMP_Text lockText;
    public GameObject nextTag;
    public GameObject bossTag;
    public TMP_Text numberText;
    public TMP_Text nameText;
    public TMP_Text enemiesText;
    public TMP_Text timeText;
    [Tooltip("Left to right, one per star: the icon is filled amber when earned and an outline when not.")]
    public Image[] starIcons = new Image[0];
    public TMP_Text[] conditionTexts = new TMP_Text[0];
    public TMP_Text weightText;
    public TMP_Text bestText;

    /// <summary>Zero-based position in the set. What gets handed to the session.</summary>
    public int Index { get; private set; } = -1;

    public bool Unlocked { get; private set; }

    /// <summary>Whether this is the level the player should play next. Draws the amber border.</summary>
    public bool IsNext { get; private set; }

    bool _hover;
    int _count = 1;

    public void Bind(int index, int count, LevelSet.Level level, string arena, Texture preview, Color arenaColor,
                     bool unlocked, bool isNext, UnityEngine.Events.UnityAction onChosen)
    {
        var t = UITheme.Active;
        Index = index;
        Unlocked = unlocked;
        IsNext = isNext && unlocked;
        _count = Mathf.Max(1, count);
        name = $"Level_{index + 1}";

        int earned = LevelProgress.StarsIn(arena, index);
        Color ink = unlocked ? t.textPrimary : t.textSecondary;
        Color quiet = unlocked ? t.textSecondary : t.textDisabled;

        if (numberText != null)
        {
            numberText.text = t.Tabular((index + 1).ToString("00"));
            numberText.color = unlocked ? t.accent : t.textDisabled;
        }
        if (nameText != null)
        {
            nameText.text = level != null ? level.Label(index).ToUpperInvariant() : $"LEVEL {index + 1}";
            nameText.color = ink;
        }

        if (enemiesText != null)
        {
            enemiesText.text = level == null ? "" :
                t.Tabular(level.enemyCount.ToString(), heading: false) + (level.hasBoss ? " + BOSS" : " ENEMIES");
            enemiesText.color = quiet;
        }
        if (timeText != null)
        {
            // TIME LIMIT, spelled out: it is the clock that ends the level, not a target,
            // and a bare "32s" was read as a par.
            timeText.text = level == null ? "" : "TIME LIMIT " + t.Tabular(Clock(level.timeLimit), heading: false);
            timeText.color = quiet;
        }
        if (bossTag != null) bossTag.SetActive(level != null && level.hasBoss);
        if (nextTag != null) nextTag.SetActive(IsNext);

        var conditions = Missions.StarConditions(level);
        for (int i = 0; i < 3; i++)
        {
            bool got = i < earned;
            if (i < starIcons.Length && starIcons[i] != null)
            {
                starIcons[i].sprite = t.IconSprite(got ? "star-filled" : "star");
                starIcons[i].color = got ? t.accent : unlocked ? t.textSecondary : t.textDisabled;
            }
            if (i < conditionTexts.Length && conditionTexts[i] != null)
            {
                conditionTexts[i].text = conditions[i];
                conditionTexts[i].color = got ? t.textPrimary : quiet;
            }
        }
        if (weightText != null)
        {
            weightText.text = Missions.WeightNote(level);
            weightText.color = t.textDisabled;
        }

        if (bestText != null)
        {
            int score = LevelProgress.BestScoreIn(arena, index);
            float time = LevelProgress.BestTimeIn(arena, index);
            // The time is only there once a clear has been timed -- which a save from before
            // best times were kept will not have, and a pass on the clock never does. Saying
            // NOT CLEARED beside a header that counts the level as cleared would be two
            // screens disagreeing, so it simply shows what there is.
            bestText.text = earned <= 0 && score <= 0
                ? (unlocked ? "NOT PLAYED YET" : "")
                : "BEST " + UIText.Row(time > 0f ? t.Tabular(Clock(time), heading: false) : "",
                                       score > 0 ? t.Tabular(score.ToString("N0"), heading: false) + " PTS" : "");
            bestText.color = quiet;
        }

        if (strip != null)
        {
            strip.texture = preview;
            strip.color = preview != null ? (unlocked ? Color.white : new Color(0.38f, 0.38f, 0.38f, 1f)) : t.panelRaised;
            Crop();
        }
        if (stripe != null)
            stripe.color = unlocked ? arenaColor : new Color(arenaColor.r * 0.45f, arenaColor.g * 0.45f, arenaColor.b * 0.45f, 1f);

        if (lockShade != null) lockShade.SetActive(!unlocked);
        if (lockText != null) lockText.text = index > 0 ? $"CLEAR LEVEL {index:00} FIRST" : "LOCKED";

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            if (onChosen != null) button.onClick.AddListener(onChosen);
            button.interactable = unlocked;
        }
        Paint();
    }

    /// <summary>"1:09", or "48s" under a minute -- how the HUD's clock reads it.</summary>
    public static string Clock(float seconds)
    {
        int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        return s < 60 ? UIText.Seconds(s) : $"{s / 60}:{s % 60:00}";
    }

    /// <summary>
    /// A band of the screenshot, panned further across for each rung. Never stretched: the
    /// band's shape is the strip's shape, and zoomed in enough that there is room to pan.
    /// </summary>
    void Crop()
    {
        if (strip == null || strip.texture == null) return;
        var r = strip.rectTransform.rect;
        if (r.width <= 1f || r.height <= 1f) return;
        float texAspect = strip.texture.width / (float)strip.texture.height;
        float boxAspect = r.width / r.height;
        const float zoom = 0.62f;
        float w = zoom;
        float h = Mathf.Min(1f, w * texAspect / boxAspect);
        if (h >= 1f) { h = 1f; w = Mathf.Min(1f, boxAspect / texAspect); }
        float along = _count > 1 ? Index / (float)(_count - 1) : 0.5f;
        // A little below centre: the middle of a preview is the horizon, and the ground
        // and what stands on it say more about a place than the sky does.
        float y = Mathf.Clamp(0.42f - h * 0.5f, 0f, 1f - h);
        strip.uvRect = new Rect((1f - w) * along, y, w, h);
    }

    void OnRectTransformDimensionsChange() => Crop();

    public void OnPointerEnter(PointerEventData e) { _hover = Unlocked; Paint(); }
    public void OnPointerExit(PointerEventData e) { _hover = false; Paint(); }
    public void OnSelect(BaseEventData e) => Paint();
    public void OnDeselect(BaseEventData e) => Paint();

    void Paint()
    {
        if (frame == null) return;
        var t = UITheme.Active;
        frame.borderColor = IsNext ? t.accent : _hover ? t.borderHover : t.border;
        frame.borderPixels = IsNext ? 2f : t.borderPixels;
        frame.color = _hover ? t.panelRaised : t.panel;
    }
}
