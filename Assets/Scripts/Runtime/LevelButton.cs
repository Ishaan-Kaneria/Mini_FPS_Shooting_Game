using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One level on the level select: a compact tile with its number, its name, its three
/// stars, a red skull when there is a boss and an amber NEXT when it is the one to play.
///
/// <b>A tile chooses; the detail pane plays.</b> With 32 rungs a ladder, the old card --
/// strip, name, facts, three conditions, best -- could not fit six across: its best score
/// spilled into the next row, the lock note ran under the BOSS tag and the strip shrank to
/// a sliver of sky. So the tile says only what tells rungs apart at a glance, and the pane
/// beside the grid (<see cref="LevelDetail"/>) shows everything about the one chosen, with
/// PLAY under it -- the same choose-then-play the dashboard's arena cards and mission card
/// use. Clicking the chosen tile again also plays, and a pad's A on a focused tile plays,
/// because the focus already chose it.
///
/// <b>Locked is drawn, not hidden</b>, and the button is not interactable: a tile that lights
/// up and then does nothing reads as broken. Hovering a locked tile still previews it in the
/// pane, so what opens it is one glance away.
///
/// Built once by the dashboard builder as a hidden template and cloned per level at
/// runtime, so a ladder's length is a property of its <see cref="LevelSet"/>, never of the scene.
/// </summary>
public class LevelButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [Header("Parts")]
    public Button button;
    public FlatRect frame;
    public TMP_Text numberText;
    public TMP_Text nameText;
    [Tooltip("Left to right, one per star: filled amber when earned, an outline when not.")]
    public Image[] starIcons = new Image[0];
    [Tooltip("Shown instead of the stars while the level is locked.")]
    public Image lockIcon;
    public GameObject starRow;
    public GameObject bossTag;
    public GameObject nextTag;

    /// <summary>Zero-based position in the set. What gets handed to the session.</summary>
    public int Index { get; private set; } = -1;

    public bool Unlocked { get; private set; }

    /// <summary>Whether this is the level the player should play next. Carries the NEXT tag.</summary>
    public bool IsNext { get; private set; }

    /// <summary>The level the detail pane is showing. Draws the amber border.</summary>
    public bool Chosen
    {
        get => _chosen;
        set { _chosen = value; Paint(); }
    }

    bool _hover, _chosen;
    System.Action<int, bool> _preview;
    System.Action<int> _focus;

    /// <param name="onChosen">A click, tap or submit on this tile.</param>
    /// <param name="onPreview">Pointer over (true) or off (false) the tile.</param>
    /// <param name="onFocus">A pad or keyboard moved the focus here.</param>
    public void Bind(int index, LevelSet.Level level, string arena, bool unlocked, bool isNext,
                     UnityEngine.Events.UnityAction onChosen, System.Action<int, bool> onPreview,
                     System.Action<int> onFocus)
    {
        var t = UITheme.Active;
        Index = index;
        Unlocked = unlocked;
        IsNext = isNext && unlocked;
        _preview = onPreview;
        _focus = onFocus;
        name = $"Level_{index + 1}";

        int earned = LevelProgress.StarsIn(arena, index);

        if (numberText != null)
        {
            numberText.text = t.Tabular((index + 1).ToString("00"));
            numberText.color = unlocked ? (earned >= 3 ? t.accent : t.textPrimary) : t.textDisabled;
        }
        if (nameText != null)
        {
            nameText.text = level != null ? level.Label(index).ToUpperInvariant() : $"LEVEL {index + 1}";
            nameText.color = unlocked ? t.textSecondary : t.textDisabled;
        }
        if (bossTag != null) bossTag.SetActive(level != null && level.hasBoss);
        // Not on a phone: three across, the tag lay over the number. The next level is the
        // one chosen when the screen opens, so its amber border already says it.
        if (nextTag != null) nextTag.SetActive(IsNext && DeviceProfile.CurrentForm != DeviceProfile.Form.Handset);

        if (starRow != null) starRow.SetActive(unlocked);
        if (lockIcon != null) lockIcon.gameObject.SetActive(!unlocked);
        for (int i = 0; i < starIcons.Length; i++)
        {
            if (starIcons[i] == null) continue;
            bool got = i < earned;
            starIcons[i].sprite = t.IconSprite(got ? "star-filled" : "star");
            starIcons[i].color = got ? t.accent : t.textDisabled;
        }

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

    public void OnPointerEnter(PointerEventData e)
    {
        _hover = true;
        Paint();
        _preview?.Invoke(Index, true);
    }

    public void OnPointerExit(PointerEventData e)
    {
        _hover = false;
        Paint();
        _preview?.Invoke(Index, false);
    }

    /// <summary>
    /// The pad's focus arriving is a choice: the pane follows it, and A then plays. A pointer
    /// press selects the tile too, on the way to its click, and must not count -- or the
    /// first click would find the tile already chosen and start the level.
    /// </summary>
    public void OnSelect(BaseEventData e)
    {
        Paint();
        var pointer = UnityEngine.InputSystem.Pointer.current;
        if (pointer != null && pointer.press.isPressed) return;
        _focus?.Invoke(Index);
    }

    public void OnDeselect(BaseEventData e) => Paint();

    void Paint()
    {
        if (frame == null) return;
        var t = UITheme.Active;
        frame.borderColor = _chosen ? t.accent : _hover && Unlocked ? t.borderHover : t.border;
        frame.borderPixels = _chosen ? 2f : t.borderPixels;
        frame.color = _chosen || (_hover && Unlocked) ? t.panelRaised : Unlocked ? t.panel : t.background;
    }
}
