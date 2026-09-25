using TMPro;
using UnityEngine;

/// <summary>
/// The bar across the top of every menu screen: the logo, the four tabs, the player's rank
/// and XP, their coins, and Settings, Info and Quit.
///
/// It stays up while PLAY, LOADOUT, ACHIEVEMENTS and STORE swap underneath it, so a player
/// always knows which screen they are on (the amber underline) and how to reach the others.
/// The tabs are a <see cref="UITabBar"/>; <see cref="MainMenuController"/> listens to it and
/// opens the screen, and calls <see cref="ShowTab"/> when a screen is opened some other way.
/// Everything shown is read from the save each time <see cref="Refresh"/> runs.
///
/// <b>A landscape handset gets the tabs as a rail down the left edge instead.</b> Height is
/// the scarce dimension there and the left edge is under a thumb, so a row across the top or
/// the bottom spends the wrong one. Both sets are built; <see cref="UseRail"/> shows one, and
/// <see cref="ShowTab"/> keeps them agreeing so a form change never shows a stale tab.
/// </summary>
public class MenuTopBar : MonoBehaviour
{
    public enum Tab { Play = 0, Loadout = 1, Achievements = 2, Store = 3 }

    public UITabBar tabs;
    public TMP_Text rankText;
    public TMP_Text xpText;
    public UIProgressBar xpBar;
    public TMP_Text coinText;
    public FlatButton settingsButton;
    public FlatButton infoButton;
    public FlatButton quitButton;
    [Tooltip("Hidden on a handset, where the welcome line carries the rank instead.")]
    public GameObject rankBlock;

    [Header("Handset rail")]
    [Tooltip("The panel down the left edge that holds the rail. Shown only on a handset.")]
    public GameObject rail;
    public UITabBar railTabs;
    [Tooltip("How wide the rail is, in canvas units. Screens under the bar start this far in.")]
    public float railWidth = 152f;

    bool _useRail;

    [Header("Store alert")]
    [Tooltip("What the STORE tab's dot asks about: is anything newly affordable?")]
    public StoreCatalog storeCatalog;
    readonly System.Collections.Generic.List<GameObject> _storeDots = new System.Collections.Generic.List<GameObject>();

    /// <summary>Whether the tabs are on the rail. Screens under the bar inset by <see cref="railWidth"/> when they are.</summary>
    public bool RailInUse => _useRail && rail != null && railTabs != null;

    /// <summary>The tabs the player can see.</summary>
    public UITabBar VisibleTabs => RailInUse ? railTabs : tabs;

    public void ShowTab(Tab tab)
    {
        if (tabs != null) tabs.Selected = (int)tab;
        if (railTabs != null) railTabs.Selected = (int)tab;
    }

    /// <summary>Moves the tabs onto the rail, or back into the bar.</summary>
    public void UseRail(bool on)
    {
        _useRail = on;
        if (tabs != null) tabs.gameObject.SetActive(!RailInUse);
        if (rail != null) rail.SetActive(RailInUse && gameObject.activeSelf);
    }

    /// <summary>Shows or hides the bar, and the rail with it when the rail is in use.</summary>
    public void SetShown(bool shown)
    {
        gameObject.SetActive(shown);
        if (rail != null) rail.SetActive(shown && RailInUse);
    }

    public void Refresh()
    {
        var t = UITheme.Active;
        int rank = PlayerRank.Rank;
        PlayerRank.Progress(out int into, out int span);
        if (rankText != null) rankText.text = $"{PlayerRank.TitleFor(rank).ToUpperInvariant()}  <color=#{ColorUtility.ToHtmlStringRGB(t.textSecondary)}>LV {t.Tabular(rank.ToString())}</color>";
        if (xpText != null) xpText.text = t.Tabular($"{into:N0} / {span:N0}", heading: false) + " XP";
        if (xpBar != null)
        {
            xpBar.Value = span > 0 ? into / (float)span : 0f;
            xpBar.Snap();
        }
        if (coinText != null) coinText.text = t.Tabular(Wallet.Format(Wallet.Balance));
        RefreshStoreDot();
    }

    /// <summary>
    /// A small amber dot on STORE -- on the bar and on the rail -- while something the player
    /// can now afford has not been seen in the store. Opening the store clears it.
    /// </summary>
    public void RefreshStoreDot()
    {
        if (_storeDots.Count == 0)
        {
            foreach (var bar in new[] { tabs, railTabs })
            {
                if (bar == null || bar.tabs.Length <= (int)Tab.Store) continue;
                var tab = bar.tabs[(int)Tab.Store];
                var dot = UIKit.Rect(tab.transform, "NewDot").gameObject.AddComponent<FlatRect>();
                dot.raycastTarget = false;
                dot.color = UITheme.Active.accent;
                var rt = dot.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-8f, -12f);
                rt.sizeDelta = new Vector2(8f, 8f);
                dot.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
                _storeDots.Add(dot.gameObject);
            }
        }
        bool show = StoreAlerts.HasNew(storeCatalog);
        foreach (var d in _storeDots) if (d != null) d.SetActive(show);
    }

    void OnEnable() => Refresh();
}
