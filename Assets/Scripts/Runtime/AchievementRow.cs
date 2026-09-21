using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One achievement on the achievements screen.</summary>
public class AchievementRow : MonoBehaviour
{
    public TMP_Text titleText;
    public TMP_Text detailText;
    public TMP_Text readoutText;

    [Tooltip("Fills left to right with the fraction done. Hidden once earned.")]
    public Image progressFill;

    [Tooltip("The track the fill runs along.")]
    public GameObject progressTrack;

    [Tooltip("Shown only once the achievement is earned.")]
    public GameObject earnedMark;

    public void Bind(Achievements.Entry entry)
    {
        name = $"Achievement_{entry.Id}";

        bool earned = entry.Earned;

        if (titleText != null)
        {
            titleText.text = entry.Title.ToUpperInvariant();

            // Earned rows take the arena-agnostic "good" signal; unearned stay in ink so
            // the screen reads as a list with some of it lit rather than as a wall of
            // colour with no shape.
            titleText.color = earned ? UITheme.Good : UITheme.Ink;
        }

        if (detailText != null)
        {
            detailText.text = entry.Detail;
            detailText.color = earned ? UITheme.InkDim : UITheme.InkLocked;
        }

        if (readoutText != null)
        {
            readoutText.text = entry.Readout;
            readoutText.color = earned ? UITheme.Good : UITheme.InkDim;
        }

        if (earnedMark != null) earnedMark.SetActive(earned);

        // The bar is the whole reason an unfinished row is worth reading, and the whole
        // reason a finished one is not.
        if (progressTrack != null) progressTrack.SetActive(!earned);

        if (progressFill != null && !earned)
        {
            progressFill.color = UITheme.Hazard;

            var rect = progressFill.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(Mathf.Clamp01(entry.Fraction), 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
