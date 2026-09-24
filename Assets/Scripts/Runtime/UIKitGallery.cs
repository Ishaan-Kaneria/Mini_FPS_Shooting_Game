using UnityEngine;

/// <summary>
/// Drives the component gallery scene (FPSKit &gt; UI Kit &gt; Build Component Gallery):
/// puts up the toasts and the pinned tooltip that only exist at runtime, and moves one bar
/// of each kind so their motion can be seen. Nothing in the game uses it.
///
/// What it shows is read from this player's save -- coins, achievements, levels cleared --
/// rather than typed in, so the gallery is never a picture of numbers the game cannot produce.
/// </summary>
public class UIKitGallery : MonoBehaviour
{
    public UIToastStack toasts;
    public FlatButton pinnedTooltipOwner;
    public string pinnedTooltip = "Settings";
    public UIProgressBar movingBar;
    public UIStatBar movingStat;
    public TMPro.TMP_Text coinLabel;
    public UIProgressBar achievementsBar;
    public UIProgressBar levelsBar;

    [Tooltip("The arena whose levels-cleared bar is shown, by progress key.")]
    public string levelsArena = "SnowboundStation";
    public int levelsInArena = 8;

    [Tooltip("Seconds between the moving bars' steps.")]
    public float stepSeconds = 1.5f;

    float _next;
    int _step;

    System.Collections.IEnumerator Start()
    {
        var theme = UITheme.Active;
        string balance = Wallet.Format(Wallet.Balance);
        if (coinLabel != null) coinLabel.text = theme.Tabular(balance);
        if (achievementsBar != null) achievementsBar.Set(Achievements.EarnedCount, Achievements.Total, true);
        if (levelsBar != null) levelsBar.Set(LevelProgress.LevelsCleared(levelsArena, levelsInArena), levelsInArena, true);

        if (toasts != null)
        {
            var first = Achievements.Catalogue[0];
            toasts.Push(UIToast.Kind.Warning, "alert-triangle", "Not enough coins", $"You have {balance}.", 0f);
            toasts.Push(UIToast.Kind.Success, "check", "Progress saved", $"{Achievements.Readout} achievements.", 0f);
            toasts.Push(UIToast.Kind.Reward, "trophy", "Achievement: " + first.Title, first.Detail, 0f);
        }
        yield return null;
    }

    /// <summary>
    /// Re-pinned every frame rather than once: a tooltip is placed against the layout at the
    /// moment it shows, and the gallery is rendered at more than one size in one session.
    /// </summary>
    void LateUpdate()
    {
        if (pinnedTooltipOwner != null)
            UITooltipView.For(pinnedTooltipOwner).Show((RectTransform)pinnedTooltipOwner.transform, pinnedTooltip);
    }

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + stepSeconds;
        _step++;
        if (movingBar != null) movingBar.Set((_step * 3) % 21, 20);
        if (movingStat != null) movingStat.Set(100 - (_step * 17) % 100, 100);
    }
}
