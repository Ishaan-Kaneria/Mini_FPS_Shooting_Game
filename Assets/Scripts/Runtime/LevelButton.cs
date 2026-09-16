using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
[DisallowMultipleComponent]
public class LevelButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Parts")]
    public Button button;
    public Image frame;
    public Image face;

    [Tooltip("Left to right. Filled for stars earned, dimmed for the rest.")]
    public Image[] stars;

    public TMP_Text numberText;
    public TMP_Text nameText;
    public TMP_Text detailText;

    [Tooltip("Shown instead of the stars on a level that has not been unlocked.")]
    public TMP_Text lockText;

    [Header("Feel")]
    public float hoverScale = 0.04f;
    public float hoverSpeed = 14f;

    public Color frameColor = new Color(0.18f, 0.20f, 0.24f, 1f);
    public Color frameHoverColor = new Color(0.95f, 0.75f, 0.35f, 1f);

    [Tooltip("The face of a level that is open, and of one that is not. A locked tile " +
             "has to read as locked without anybody having to find the small text.")]
    public Color unlockedColor = new Color(0.12f, 0.14f, 0.17f, 1f);

    public Color lockedColor = new Color(0.07f, 0.08f, 0.09f, 1f);

    public Color starEarnedColor = new Color(1f, 0.82f, 0.25f);
    public Color starMissedColor = new Color(1f, 1f, 1f, 0.10f);

    public Color inkColor = new Color(0.91f, 0.90f, 0.89f, 1f);
    public Color inkLockedColor = new Color(0.45f, 0.47f, 0.50f, 1f);

    bool _hovered;
    float _hover;

    /// <summary>Zero-based position in the set. What gets handed to the session.</summary>
    public int Index { get; private set; } = -1;

    public bool Unlocked { get; private set; }

    void OnEnable()
    {
        _hovered = false;
        ApplyHover(0f);
    }

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

    public void OnPointerEnter(PointerEventData eventData) => _hovered = Unlocked;
    public void OnPointerExit(PointerEventData eventData) => _hovered = false;

    void Update()
    {
        // Unscaled: the dashboard can be reached from a frozen game, and a hover that
        // only animates at timeScale 1 would look broken exactly then.
        float target = _hovered ? 1f : 0f;
        ApplyHover(Mathf.MoveTowards(_hover, target, hoverSpeed * Time.unscaledDeltaTime));
    }

    /// <summary>
    /// Grows the tile and lights its frame. Scale rather than position: a
    /// GridLayoutGroup owns anchoredPosition on every child it places, and a hover that
    /// wrote one each frame would drag every tile onto the same spot -- which is exactly
    /// what it did to the arena grid once. ArenaCard animates its hover the same way.
    /// </summary>
    void ApplyHover(float amount)
    {
        _hover = amount;

        transform.localScale = Vector3.one * (1f + hoverScale * amount);

        if (frame != null) frame.color = Color.Lerp(frameColor, frameHoverColor, amount);
    }
}
