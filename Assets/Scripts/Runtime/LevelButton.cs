using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One level on the level select screen: its number, what it asks for, the stars
/// earned on it, and whether it is open at all.
///
/// Built once by the dashboard builder as a hidden template and cloned per level at
/// runtime, the same way <see cref="ArenaCard"/> is -- so the number of levels an
/// arena offers is a property of its <see cref="LevelSet"/> and never of this scene.
///
/// A locked tile is deliberately still drawn in full rather than replaced by a blank
/// square. Seeing that level 6 is a boss with forty seconds on the clock is most of
/// the reason to go and beat level 5.
/// </summary>
public class LevelButton : HoverCard
{
    [Header("Parts")]
    public Button button;
    public Image face;

    [Tooltip("Left to right. Filled for stars earned, dimmed for the rest.")]
    public Image[] stars;

    public TMP_Text numberText;
    public TMP_Text nameText;
    public TMP_Text detailText;

    [Tooltip("Shown instead of the stars on a level that has not been unlocked.")]
    public TMP_Text lockText;

    [Header("Feel")]
    [Tooltip("The face of a level that is open, and of one that is not. A locked tile " +
             "has to read as locked without anybody having to find the small text.")]
    public Color unlockedColor = new Color(0.110f, 0.133f, 0.169f, 1f);

    public Color lockedColor = new Color(0.047f, 0.059f, 0.075f, 1f);

    public Color starEarnedColor = new Color(1f, 0.729f, 0.247f);
    public Color starMissedColor = new Color(1f, 1f, 1f, 0.16f);

    public Color inkColor = new Color(0.961f, 0.973f, 0.984f, 1f);
    public Color inkLockedColor = new Color(0.502f, 0.545f, 0.596f, 1f);

    /// <summary>Zero-based position in the set. What gets handed to the session.</summary>
    public int Index { get; private set; } = -1;

    public bool Unlocked { get; private set; }

    public void Bind(int index, LevelSet.Level level, int starsEarned, bool unlocked,
                     UnityEngine.Events.UnityAction onChosen)
    {
        Index = index;
        Unlocked = unlocked;

        name = $"Level_{index + 1}";

        if (numberText != null) numberText.text = (index + 1).ToString("00");

        if (nameText != null)
            nameText.text = level != null ? level.Label(index).ToUpperInvariant() : $"LEVEL {index + 1}";

        if (detailText != null)
        {
            detailText.text = level == null
                ? ""
                : UIText.Row($"{level.enemyCount} ENEMIES",
                             level.hasBoss ? "BOSS" : "",
                             UIText.Seconds(level.timeLimit));
        }

        if (stars != null)
        {
            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i] == null) continue;

                stars[i].gameObject.SetActive(unlocked);
                stars[i].color = i < starsEarned ? starEarnedColor : starMissedColor;
            }
        }

        if (lockText != null)
        {
            lockText.gameObject.SetActive(!unlocked);
            lockText.text = "LOCKED";
        }

        Color ink = unlocked ? inkColor : inkLockedColor;
        if (numberText != null) numberText.color = unlocked ? starEarnedColor : inkLockedColor;
        if (nameText != null) nameText.color = ink;
        if (detailText != null) detailText.color = unlocked ? inkLockedColor : inkLockedColor * 0.8f;

        if (face != null) face.color = unlocked ? unlockedColor : lockedColor;

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            if (onChosen != null) button.onClick.AddListener(onChosen);

            // Not interactable rather than merely ignored: a locked tile that highlights
            // under the pointer and then does nothing reads as a broken button.
            button.interactable = unlocked;
        }

        ApplyHover(0f);
    }

    /// <summary>
    /// A locked tile does not light up. It is still drawn in full -- seeing that level 6
    /// is a boss with forty seconds on the clock is most of the reason to go and beat
    /// level 5 -- but highlighting under the pointer and then doing nothing reads as a
    /// broken button rather than a locked one.
    /// </summary>
    protected override bool Hoverable => Unlocked;
}
