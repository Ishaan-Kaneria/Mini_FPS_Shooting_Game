using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The pane beside the level grid: everything about the one level chosen, and PLAY.
///
/// A picture of the arena (a band of its real screenshot, panned a little further across
/// for each rung, as the old cards did), the number and name, how many enemies and how long
/// the clock gives, the three things each star asks for -- filled amber once earned, because
/// a star the player cannot read the rule for is a star they cannot aim at -- the best run,
/// and the button. A locked level says what opens it where PLAY would be.
///
/// On a handset the same pane also carries the zone's story, behind the header's STORY
/// button: a landscape phone has no height for a paragraph over the grid.
/// </summary>
public class LevelDetail : MonoBehaviour
{
    [Header("Level")]
    public GameObject levelView;
    public RawImage picture;
    public FlatRect stripe;
    public GameObject bossTag;
    public GameObject nextTag;
    public TMP_Text numberText;
    public TMP_Text nameText;
    public TMP_Text enemiesText;
    public TMP_Text timeText;
    public Image[] starIcons = new Image[0];
    public TMP_Text[] conditionTexts = new TMP_Text[0];
    public TMP_Text weightText;
    public TMP_Text bestText;
    public FlatButton playButton;

    [Header("Story")]
    public GameObject storyView;
    public TMP_Text storyTitleText;
    public TMP_Text storyText;

    int _index, _count = 1;

    /// <summary>Whether the story is showing instead of the level.</summary>
    public bool ShowingStory => storyView != null && storyView.activeSelf;

    public void ShowStory(bool on)
    {
        if (storyView != null) storyView.SetActive(on);
        if (levelView != null) levelView.SetActive(!on);
    }

    public void Show(int index, int count, LevelSet.Level level, string arena, Texture preview,
                     Color arenaColor, bool unlocked, bool isNext)
    {
        var t = UITheme.Active;
        _index = index;
        _count = Mathf.Max(1, count);
        int earned = LevelProgress.StarsIn(arena, index);
        Color quiet = unlocked ? t.textSecondary : t.textDisabled;

        if (numberText != null)
        {
            numberText.text = "LEVEL " + t.Tabular((index + 1).ToString("00"));
            numberText.color = unlocked ? t.accent : t.textSecondary;
        }
        if (nameText != null)
            nameText.text = level != null ? level.Label(index).ToUpperInvariant() : $"LEVEL {index + 1}";

        if (enemiesText != null)
            enemiesText.text = level == null ? "" :
                t.Tabular(level.enemyCount.ToString(), heading: false) + (level.hasBoss ? " + BOSS" : " ENEMIES");
        if (timeText != null)
            // TIME LIMIT, spelled out: it is the clock that ends the level, not a target,
            // and a bare "32s" was read as a par.
            timeText.text = level == null ? "" : "TIME LIMIT " + t.Tabular(LevelButton.Clock(level.timeLimit), heading: false);

        if (bossTag != null) bossTag.SetActive(level != null && level.hasBoss);
        if (nextTag != null) nextTag.SetActive(isNext && unlocked);

        var conditions = Missions.StarConditions(level);
        for (int i = 0; i < 3; i++)
        {
            bool got = i < earned;
            if (i < starIcons.Length && starIcons[i] != null)
            {
                starIcons[i].sprite = t.IconSprite(got ? "star-filled" : "star");
                starIcons[i].color = got ? t.accent : t.textSecondary;
            }
            if (i < conditionTexts.Length && conditionTexts[i] != null)
            {
                conditionTexts[i].text = conditions[i];
                conditionTexts[i].color = got ? t.textPrimary : t.textSecondary;
            }
        }
        if (weightText != null)
        {
            string note = Missions.WeightNote(level);
            weightText.text = note;
            weightText.gameObject.SetActive(!string.IsNullOrEmpty(note) &&
                                            DeviceProfile.CurrentForm != DeviceProfile.Form.Handset);
        }

        if (bestText != null)
        {
            int score = LevelProgress.BestScoreIn(arena, index);
            float time = LevelProgress.BestTimeIn(arena, index);
            // The time is only there once a clear has been timed, which a pass on the clock
            // never is; it shows what there is rather than contradict the header's count.
            bestText.text = earned <= 0 && score <= 0
                ? (unlocked ? "NOT PLAYED YET" : "")
                : "BEST " + UIText.Row(time > 0f ? t.Tabular(LevelButton.Clock(time), heading: false) : "",
                                       score > 0 ? t.Tabular(score.ToString("N0"), heading: false) + " PTS" : "");
            bestText.color = quiet;
        }

        // A phone's pane has no height for the picture once the text is in: it came out as a
        // sliver with the corner tags cut in half. The tiles carry NEXT and the boss there.
        if (picture != null && picture.transform.parent != null)
            picture.transform.parent.gameObject.SetActive(DeviceProfile.CurrentForm != DeviceProfile.Form.Handset);
        if (picture != null)
        {
            picture.texture = preview;
            picture.color = preview != null ? (unlocked ? Color.white : new Color(0.42f, 0.42f, 0.42f, 1f)) : t.panelRaised;
            Crop();
        }
        if (stripe != null)
            stripe.color = unlocked ? arenaColor : new Color(arenaColor.r * 0.45f, arenaColor.g * 0.45f, arenaColor.b * 0.45f, 1f);

        if (playButton != null)
        {
            playButton.interactable = unlocked;
            if (playButton.label != null)
                playButton.label.text = unlocked
                    ? (earned > 0 ? "PLAY AGAIN" : "PLAY LEVEL")
                    : index > 0 ? $"CLEAR LEVEL {index:00} FIRST" : "LOCKED";
            if (playButton.icon != null)
                playButton.icon.sprite = t.IconSprite(unlocked ? "player-play" : "lock");
        }
    }

    /// <summary>
    /// A band of the screenshot, panned further across for each rung. Never stretched: the
    /// band's shape is the picture's shape, zoomed in enough that there is room to pan.
    /// </summary>
    void Crop()
    {
        if (picture == null || picture.texture == null) return;
        var r = picture.rectTransform.rect;
        if (r.width <= 1f || r.height <= 1f) return;
        float texAspect = picture.texture.width / (float)picture.texture.height;
        float boxAspect = r.width / r.height;
        const float zoom = 0.72f;
        float w = zoom;
        float h = Mathf.Min(1f, w * texAspect / boxAspect);
        if (h >= 1f) { h = 1f; w = Mathf.Min(1f, boxAspect / texAspect); }
        float along = _count > 1 ? _index / (float)(_count - 1) : 0.5f;
        // A little below centre: the middle of a preview is the horizon, and the ground and
        // what stands on it say more about a place than the sky does.
        float y = Mathf.Clamp(0.42f - h * 0.5f, 0f, 1f - h);
        picture.uvRect = new Rect((1f - w) * along, y, w, h);
    }

    Vector2 _cropped;

    /// <summary>The picture is sized by the pane's layout, after Show: crop again when it moves.</summary>
    void LateUpdate()
    {
        if (picture == null) return;
        var size = picture.rectTransform.rect.size;
        if ((size - _cropped).sqrMagnitude < 0.25f) return;
        _cropped = size;
        Crop();
    }
}
