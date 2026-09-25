using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CURRENT MISSION: the level the player would most sensibly play next, in the arena they
/// have selected, and the big amber button that plays it.
///
/// "Next" is <see cref="Missions.NextLevel"/>: the first unlocked level without a star, so
/// a returning player is handed the level they have not cleared rather than the one they
/// just replayed. Every figure on it -- enemies, the clock, the difficulty, the coins and
/// stars still to be had -- is read from the level set and the save; nothing is written in.
///
/// PLAY MISSION is the main call to action on the dashboard, and the only button there that
/// loads a level. ALL LEVELS opens the ladder for a player who wants a different one.
/// </summary>
public class MissionCard : MonoBehaviour
{
    public RawImage thumbnail;
    public FlatRect stripe;
    public TMP_Text arenaText;
    public TMP_Text levelText;
    public TMP_Text objectiveText;
    public TMP_Text enemiesText;
    public TMP_Text timeText;
    public TMP_Text difficultyText;
    public FlatRect[] difficultyBars = new FlatRect[0];
    public TMP_Text coinsText;
    public TMP_Text starsText;
    public FlatButton playButton;
    public FlatButton levelsButton;

    [Tooltip("Hidden in the handset layout, where the column is too short for all of it.")]
    public GameObject[] compactHidden = new GameObject[0];

    /// <summary>The handset layout: the name, the level, the numbers and the button.</summary>
    public void SetCompact(bool compact)
    {
        _compact = compact;
        if (Entry != null) Bind(Entry);
        foreach (var go in compactHidden) if (go != null) go.SetActive(!compact);
    }

    bool _compact;

    public ArenaCatalog.Entry Entry { get; private set; }
    public int LevelIndex { get; private set; }

    public void Bind(ArenaCatalog.Entry entry)
    {
        var t = UITheme.Active;
        Entry = entry;
        LevelIndex = Missions.NextLevel(entry);
        var level = Missions.LevelAt(entry, LevelIndex);

        if (thumbnail != null)
        {
            thumbnail.texture = entry != null ? entry.preview : null;
            thumbnail.color = thumbnail.texture != null ? Color.white : t.panelRaised;
            Cover();
        }
        if (stripe != null) stripe.color = entry != null ? t.StripeFor(entry.sceneName) : t.border;
        if (arenaText != null) arenaText.text = entry != null ? entry.Label.ToUpperInvariant() : "NO ARENA";

        if (level == null)
        {
            if (levelText != null) levelText.text = "";
            if (objectiveText != null) objectiveText.text = "This arena has no levels.";
            if (playButton != null) playButton.interactable = false;
            return;
        }

        string accent = "#" + ColorUtility.ToHtmlStringRGB(t.accent);
        string name = string.IsNullOrWhiteSpace(level.displayName)
            ? Missions.ObjectiveTitle(level.objective)
            : level.displayName.ToUpperInvariant();
        if (levelText != null)
            levelText.text = $"LEVEL {t.Tabular((LevelIndex + 1).ToString("00"))}  /  <color={accent}>{name}</color>";

        if (objectiveText != null)
        {
            string brief = string.IsNullOrWhiteSpace(level.brief) ? "" : level.brief.Trim();
            string objective = Missions.ObjectiveTitle(level.objective);
            objectiveText.text = string.IsNullOrEmpty(brief) ? Capitalise(objective) : brief;
        }

        int enemies = level.enemyCount + (level.hasBoss ? 1 : 0);
        // Compact drops the word: the skull beside the number already says what it counts,
        // and the handset column has no room for the stats row with it spelled out.
        string counted = _compact ? "" : " ENEMIES";
        if (enemiesText != null) enemiesText.text = t.Tabular(enemies.ToString(), heading: false) + counted + (level.hasBoss ? " · BOSS" : "");
        if (timeText != null)
        {
            int s = Mathf.RoundToInt(level.timeLimit);
            timeText.text = t.Tabular($"{s / 60}:{s % 60:00}", heading: false);
        }

        var d = Missions.ForLevel(level);
        if (difficultyText != null)
        {
            difficultyText.text = Missions.Label(d);
            difficultyText.color = Missions.ColorFor(t, d);
        }
        for (int i = 0; i < difficultyBars.Length; i++)
            if (difficultyBars[i] != null)
                difficultyBars[i].color = i <= (int)d ? Missions.ColorFor(t, d) : t.border;

        int stars = Missions.StarsAvailable(entry, LevelIndex);
        if (coinsText != null) coinsText.text = "UP TO " + t.Tabular(Wallet.Format(Missions.CoinsAvailable(entry, LevelIndex))) + " COINS";
        if (starsText != null)
            starsText.text = stars > 0
                ? "+" + t.Tabular(stars.ToString()) + (stars == 1 ? " STAR" : " STARS")
                : "ALL STARS EARNED";

        if (playButton != null)
        {
            playButton.interactable = true;
            if (playButton.label != null) playButton.label.text = LevelProgress.StarsIn(entry.ProgressKey, LevelIndex) > 0 ? "REPLAY MISSION" : "PLAY MISSION";
        }
    }

    static string Capitalise(string upper)
        => string.IsNullOrEmpty(upper) ? upper : upper.Substring(0, 1) + upper.Substring(1).ToLowerInvariant() + ".";

    void Cover()
    {
        if (thumbnail == null || thumbnail.texture == null) return;
        var r = thumbnail.rectTransform.rect;
        if (r.width <= 1f || r.height <= 1f) return;
        float tex = thumbnail.texture.width / (float)thumbnail.texture.height;
        float box = r.width / r.height;
        thumbnail.uvRect = box > tex
            ? new Rect(0f, (1f - tex / box) * 0.5f, 1f, tex / box)
            : new Rect((1f - box / tex) * 0.5f, 0f, box / tex, 1f);
    }

    void OnRectTransformDimensionsChange() => Cover();
}
