using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The dashboard: the PLAY screen, and the host every other menu screen opens over.
///
/// <b>Select, then play.</b> A card selects an arena; the CURRENT MISSION card then shows the
/// next level worth playing in it (<see cref="Missions.NextLevel"/>), and PLAY MISSION is the
/// one button on the screen that starts a level. ALL LEVELS opens the ladder for a player who
/// wants a different rung. Starting a run is two deliberate steps, and the big amber button is
/// always the second.
///
/// <b>The top bar is shared.</b> PLAY, LOADOUT, ACHIEVEMENTS and STORE swap underneath it:
/// the store and achievements are the existing screens, built into this scene below the bar;
/// loadout, info and settings are built at runtime from the kit. The dashboard's own content
/// is <see cref="dashboardOnly"/>, hidden while another screen is up so nothing behind a
/// full-screen raycast target is drawn and unreachable.
///
/// It is the first scene the game loads and the one every run returns to, however that run
/// ended. The grid is built from <see cref="ArenaCatalog"/> at runtime, so adding an arena is
/// an entry in the catalog and never a rebuild of this scene.
/// </summary>
[DisallowMultipleComponent]
public class MainMenuController : MonoBehaviour
{
    [Header("Content")]
    public ArenaCatalog catalog;

    [Tooltip("The campaign: which zones are open, in what order, and the story.")]
    public CampaignData campaign;

    public UISounds sounds;

    [Header("Screens")]
    public LevelSelectPanel levelSelect;
    public StorePanel store;
    public AchievementsPanel achievements;
    public InstructionsPanel instructions;
    public StoryPanel story;
    public DossierPanel dossier;

    [Tooltip("The dashboard's own content, hidden while another screen is up.")]
    public GameObject[] dashboardOnly;

    [Header("Top bar")]
    public MenuTopBar topBar;

    [Tooltip("How tall the top bar is, in canvas units. Screens opened under it start this far down.")]
    public float topBarHeight = 88f;

    [Header("Play")]
    public TMP_Text welcomeText;
    [Tooltip("Rank and XP on one line, shown on a handset where the top bar has no room for them.")]
    public TMP_Text rankLine;
    public ScrollRect cardScroll;
    public RectTransform cardParent;
    public ArenaCard cardTemplate;
    [Tooltip("Card width over height.")]
    public float cardAspect = 1.12f;
    public MissionCard mission;
    public NextRewardCard nextReward;
    public LoadoutStrip loadoutStrip;
    public TMP_Text footerText;
    public UIToastStack toasts;

    [Header("Handset")]
    [Tooltip("Hidden on a handset: the screen is short, and these are also reachable from a tab.")]
    public GameObject[] hideOnHandset;
    public RectTransform leftColumn;
    public RectTransform rightColumn;

    [Header("Exit")]
    public GameObject exitConfirmPanel;
    public Button confirmExitButton;
    public Button cancelExitButton;

    /// <summary>The last run's result, shown as a toast on the way back. Null until then.</summary>
    [HideInInspector] public GameObject lastRunPanel;

    readonly List<ArenaCard> _cards = new List<ArenaCard>();
    readonly HashSet<RectTransform> _railInset = new HashSet<RectTransform>();
    GridLayoutGroup _layout;
    Vector2 _fitted = new Vector2(-1f, -1f);
    bool[] _dashboardWasShown;
    bool _dashboardHidden;
    ArenaCatalog.Entry _selected;
    ArenaCatalog.Entry _afterStory;
    bool _afterStoryLaunch;
    LoadoutPanel _loadout;
    InfoPanel _info;
    string _granted;

    const string SelectedKey = "FPSKit.Dashboard.Selected";

    /// <summary>What was unlocked arriving here, or null. Cleared once it is shown.</summary>
    public string TakeGrantedPower()
    {
        var id = _granted;
        _granted = null;
        return id;
    }

    public int TotalStars() => Campaign.TotalStars(catalog);
    public int PossibleStars() => Campaign.MaxStars(catalog);

    // ======================================================================

    void Start()
    {
        // A run that ended left these set for its own reasons. The dashboard is a menu.
        Time.timeScale = 1f;
        PlayerMotor.InputEnabled = false;
        MobileInput.Reset();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        PhoneUI.Apply(GetComponentInParent<Canvas>());
        ApplyFormLayout();

        _granted = Campaign.RefreshUnlocks(campaign, catalog, store != null ? store.catalog : null);
        PlayerStats.RefreshFromCatalog(catalog);

        var canvas = GetComponentInParent<Canvas>();
        _loadout = LoadoutPanel.Create(canvas, store != null ? store.catalog : null, topBarHeight,
                                      store != null ? store.renders : null);
        _loadout.Closed += OnOverlayClosed;
        _loadout.StoreRequested += () => { _loadout.Close(); OpenStore(); };
        _info = InfoPanel.Create(canvas, topBarHeight);
        _info.Closed += OnOverlayClosed;
        InsetForRail(_loadout.panel);
        InsetForRail(_info.panel);
        _info.HowToPlayRequested += () => { _info.Close(); OpenInstructions(); };
        _info.ListRequested += () => { _info.Close(); OpenDossier(); };

        BuildGrid();
        Refresh();
        ShowLastRun();

        if (levelSelect != null)
        {
            levelSelect.LevelChosen += Launch;
            levelSelect.Closed += OnOverlayClosed;
        }
        if (store != null)
        {
            store.Closed += OnOverlayClosed;
            // Coins move the moment something is bought, in the bar as well as the store.
            store.Purchased += () => { if (topBar != null) topBar.Refresh(); };
        }
        if (topBar != null && store != null) topBar.storeCatalog = store.catalog;
        if (achievements != null)
        {
            achievements.Closed += OnOverlayClosed;
            achievements.Claimed += () => { if (topBar != null) topBar.Refresh(); };
        }
        if (instructions != null) instructions.Closed += OnOverlayClosed;
        if (story != null) story.Closed += OnOverlayClosed;
        if (dossier != null) dossier.Closed += OnOverlayClosed;

        if (topBar != null)
        {
            if (topBar.tabs != null) topBar.tabs.onChanged.AddListener(OnTab);
            if (topBar.railTabs != null) topBar.railTabs.onChanged.AddListener(OnTab);
            Wire(topBar.settingsButton, () => SettingsPanel.Show(canvas));
            Wire(topBar.infoButton, OpenInfo);
            Wire(topBar.quitButton, AskToExit);
        }
        if (mission != null)
        {
            Wire(mission.playButton, PlayMission);
            Wire(mission.levelsButton, () => { if (_selected != null) Choose(_selected); });
        }
        if (nextReward != null) Wire(nextReward.actionButton, OpenStore);
        if (loadoutStrip != null) Wire(loadoutStrip.customizeButton, OpenLoadout);
        Wire(confirmExitButton, Exit);
        Wire(cancelExitButton, CancelExit);
        if (exitConfirmPanel != null) exitConfirmPanel.SetActive(false);

        if (footerText != null)
            footerText.text = $"SINGLE PLAYER CAMPAIGN   |   VERSION {Application.version}";

        TellStory();
    }

    void OnDestroy()
    {
        if (levelSelect != null)
        {
            levelSelect.LevelChosen -= Launch;
            levelSelect.Closed -= OnOverlayClosed;
        }
        if (store != null) store.Closed -= OnOverlayClosed;
        if (achievements != null) achievements.Closed -= OnOverlayClosed;
        if (instructions != null) instructions.Closed -= OnOverlayClosed;
        if (story != null) story.Closed -= OnOverlayClosed;
        if (dossier != null) dossier.Closed -= OnOverlayClosed;
    }

    static void Wire(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    void Update()
    {
        if (!_dashboardHidden) FitGrid();

        if (exitConfirmPanel != null && exitConfirmPanel.activeSelf && GameInput.BackPressed)
            CancelExit();
    }

    /// <summary>Everything on the PLAY screen that reads the save, read again.</summary>
    void Refresh()
    {
        if (topBar != null) topBar.Refresh();
        if (welcomeText != null) welcomeText.text = $"WELCOME BACK, {PlayerProfile.Name.ToUpperInvariant()}";
        if (rankLine != null)
        {
            PlayerRank.Progress(out int into, out int span);
            rankLine.text = $"{PlayerRank.TitleFor(PlayerRank.Rank).ToUpperInvariant()}{UIText.Separator}LV {PlayerRank.Rank}{UIText.Separator}{into:N0} / {span:N0} XP";
        }
        var storeCatalog = store != null ? store.catalog : null;
        if (nextReward != null) nextReward.Bind(storeCatalog);
        if (loadoutStrip != null) loadoutStrip.Bind(storeCatalog);
        if (mission != null && _selected != null) mission.Bind(_selected);
    }

    // ======================================================================
    // The grid.
    // ======================================================================

    IEnumerable<ArenaCatalog.Entry> InCampaignOrder()
    {
        var remaining = new List<ArenaCatalog.Entry>();
        foreach (var entry in catalog.arenas)
            if (entry != null && !string.IsNullOrWhiteSpace(entry.sceneName))
                remaining.Add(entry);

        if (campaign != null)
        {
            for (int i = 0; i < campaign.ZoneCount; i++)
            {
                var zone = campaign.ZoneAt(i);
                if (zone == null) continue;
                int at = remaining.FindIndex(e => e.ProgressKey == zone.ProgressKey);
                if (at < 0) continue;
                var entry = remaining[at];
                remaining.RemoveAt(at);
                yield return entry;
            }
        }
        foreach (var entry in remaining) yield return entry;
    }

    void BuildGrid()
    {
        if (cardTemplate == null || cardParent == null)
        {
            Report("The dashboard has no card template wired. Run FPSKit > Build Dashboard.");
            return;
        }
        cardTemplate.gameObject.SetActive(false);
        foreach (var card in _cards) if (card != null) Destroy(card.gameObject);
        _cards.Clear();

        if (catalog == null || catalog.arenas == null || catalog.arenas.Count == 0)
        {
            Report("No arenas in the catalog. Run FPSKit > Build Dashboard to fill it in.");
            return;
        }

        int missing = 0;
        ArenaCatalog.Entry firstOpenUnfinished = null, lastOpen = null, remembered = null;
        string rememberedKey = PlayerPrefs.GetString(SelectedKey, GameSession.SelectedArena);

        foreach (var entry in InCampaignOrder())
        {
            var card = Instantiate(cardTemplate, cardParent);
            card.gameObject.SetActive(true);

            bool loadable = Application.CanStreamedLevelBeLoaded(entry.sceneName);
            if (!loadable) missing++;
            bool open = Campaign.ArenaUnlocked(campaign, entry.ProgressKey);

            var captured = entry;
            card.Bind(entry, _cards.Count, open, Campaign.LockNote(campaign, entry.ProgressKey), () => Select(captured), campaign);
            if (!loadable && card.button != null) card.button.interactable = false;
            _cards.Add(card);

            if (open && loadable)
            {
                lastOpen = entry;
                if (firstOpenUnfinished == null && LevelProgress.LevelsCleared(entry.ProgressKey, entry.LevelCount) < entry.LevelCount)
                    firstOpenUnfinished = entry;
                if (entry.ProgressKey == rememberedKey || entry.sceneName == rememberedKey) remembered = entry;
            }
        }

        // The arena the player last chose, while it is still open; otherwise the first open
        // one with something left to clear -- which is where the campaign is.
        Select(remembered ?? firstOpenUnfinished ?? lastOpen, remember: false);

        if (campaign == null)
            Report("This dashboard has no campaign asset, so every arena is open and the story is not being told. Run FPSKit > Build Dashboard.");
        if (missing > 0)
            Report($"{missing} arena(s) are not in Build Settings and cannot be loaded. Rebuild them with FPSKit > Build Scene > Build ALL Themes.");
    }

    /// <summary>
    /// Sizes the cards to the space there is. A monitor or tablet gets a grid that scrolls
    /// down when it outgrows the column; a handset gets a single row that scrolls sideways,
    /// because a landscape phone has width to spare and no height.
    /// </summary>
    void FitGrid()
    {
        if (cardParent == null || cardScroll == null || cardScroll.viewport == null) return;
        if (_layout == null) _layout = cardParent.GetComponent<GridLayoutGroup>();
        if (_layout == null) return;
        var view = cardScroll.viewport.rect.size;
        if (view.x <= 1f || view.y <= 1f || (view - _fitted).sqrMagnitude < 0.25f) return;
        _fitted = view;

        bool handset = DeviceProfile.CurrentForm == DeviceProfile.Form.Handset;
        float gap = _layout.spacing.x;
        if (handset)
        {
            float h = view.y;
            _layout.startAxis = GridLayoutGroup.Axis.Vertical;
            _layout.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            _layout.constraintCount = 1;
            _layout.cellSize = new Vector2(h * cardAspect, h);
            cardScroll.horizontal = true;
            cardScroll.vertical = false;
        }
        else
        {
            int columns = view.x / view.y > 2.1f ? 4 : 3;
            float w = (view.x - gap * (columns - 1)) / columns;
            _layout.startAxis = GridLayoutGroup.Axis.Horizontal;
            _layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _layout.constraintCount = columns;
            _layout.cellSize = new Vector2(w, w / cardAspect);
            cardScroll.horizontal = false;
            cardScroll.vertical = true;
        }
    }

    /// <summary>What clicking a card does: make it the arena the mission card describes.</summary>
    public void Select(ArenaCatalog.Entry entry) => Select(entry, remember: true);

    void Select(ArenaCatalog.Entry entry, bool remember)
    {
        if (entry == null) return;
        if (!Campaign.ArenaUnlocked(campaign, entry.ProgressKey))
        {
            Report($"{entry.Label} is locked. {Campaign.LockNote(campaign, entry.ProgressKey)}");
            return;
        }
        _selected = entry;
        foreach (var card in _cards) if (card != null) card.Selected = card.Entry == entry;
        if (remember)
        {
            PlayerPrefs.SetString(SelectedKey, entry.ProgressKey);
            PlayerPrefs.Save();
        }
        if (mission != null) mission.Bind(entry);
    }

    // ======================================================================
    // Playing.
    // ======================================================================

    /// <summary>PLAY MISSION: the selected arena's next level, after its opening if unread.</summary>
    /// <summary>
    /// Settings' "Customize layout" from the menu: the HUD editor needs a HUD under it, so the
    /// selected arena's current mission is loaded frozen, the editor opens on arrival, and
    /// leaving it comes straight back here without the visit being recorded as a run.
    /// </summary>
    public void LaunchHudEditor()
    {
        var entry = _selected;
        if (entry == null && catalog != null)
            foreach (var a in catalog.arenas)
                if (a != null && Campaign.ArenaUnlocked(campaign, a.ProgressKey)) { entry = a; break; }
        if (entry == null) { Report("There is no arena to lay the HUD out over."); return; }
        GameSession.BeginHudEditing();
        Launch(entry, Missions.NextLevel(entry));
    }

    public void PlayMission()
    {
        if (_selected == null || mission == null) return;
        if (ShowOpeningFirst(_selected, launch: true)) return;
        Launch(_selected, mission.LevelIndex);
    }

    /// <summary>ALL LEVELS: opens an arena's ladder, after its opening if unread.</summary>
    public void Choose(ArenaCatalog.Entry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.sceneName)) return;
        if (!Campaign.ArenaUnlocked(campaign, entry.ProgressKey))
        {
            Report($"{entry.Label} is locked. {Campaign.LockNote(campaign, entry.ProgressKey)}");
            return;
        }
        if (levelSelect == null)
        {
            Launch(entry, LevelProgress.HighestUnlocked(entry.ProgressKey, entry.LevelCount));
            return;
        }
        if (entry.LevelCount <= 0)
        {
            Report($"{entry.Label} has no levels. Run FPSKit > Reset Level Sets, then FPSKit > Build Dashboard.");
            return;
        }
        if (ShowOpeningFirst(entry, launch: false)) return;

        ShowDashboard(false);
        levelSelect.Open(entry);
    }

    /// <summary>
    /// A zone's opening, once, on the way into it -- read before the ladder or the level,
    /// which is when a line about the place the player is about to walk into means anything.
    /// </summary>
    bool ShowOpeningFirst(ArenaCatalog.Entry entry, bool launch)
    {
        int zone = campaign != null ? campaign.IndexOfArena(entry.ProgressKey) : -1;
        if (story == null || zone < 0 || Campaign.OpeningSeen(zone)) return false;
        var opening = campaign.ZoneAt(zone);
        if (opening == null || string.IsNullOrWhiteSpace(opening.opening)) return false;

        _afterStory = entry;
        _afterStoryLaunch = launch;
        story.Bind(campaign);
        story.QueueZoneOpening(zone);
        ShowDashboard(false, hideTopBar: true);
        story.Open();
        return true;
    }

    /// <summary>Starts a level. The one place a scene load can begin.</summary>
    public void Launch(ArenaCatalog.Entry entry, int levelIndex)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.sceneName)) return;
        if (!Application.CanStreamedLevelBeLoaded(entry.sceneName))
        {
            Report($"{entry.sceneName} is not in Build Settings, so it cannot be loaded.");
            return;
        }
        if (!Campaign.ArenaUnlocked(campaign, entry.ProgressKey))
        {
            Report($"{entry.Label} is locked. {Campaign.LockNote(campaign, entry.ProgressKey)}");
            return;
        }
        if (!LevelProgress.IsUnlocked(entry.ProgressKey, levelIndex))
        {
            Report($"Level {levelIndex + 1} of {entry.Label} is still locked.");
            return;
        }
        GameSession.ChooseArena(entry.sceneName, entry.Label);
        GameSession.ChooseLevel(levelIndex);
        PlayerMotor.InputEnabled = true;
        SceneManager.LoadScene(entry.sceneName);
    }

    // ======================================================================
    // Screens.
    // ======================================================================

    void OnTab(int index)
    {
        switch ((MenuTopBar.Tab)index)
        {
            case MenuTopBar.Tab.Play: CloseScreens(); break;
            case MenuTopBar.Tab.Loadout: OpenLoadout(); break;
            case MenuTopBar.Tab.Achievements: OpenAchievements(); break;
            case MenuTopBar.Tab.Store: OpenStore(); break;
        }
    }

    /// <summary>Closes whatever screen is over the dashboard, without reopening another.</summary>
    void CloseScreens()
    {
        if (store != null && store.IsOpen) store.Close();
        if (achievements != null && achievements.IsOpen) achievements.Close();
        if (_loadout != null && _loadout.IsOpen) _loadout.Close();
        if (levelSelect != null && levelSelect.IsOpen) levelSelect.Close();
        if (instructions != null && instructions.IsOpen) instructions.Close();
        if (dossier != null && dossier.IsOpen) dossier.Close();
        if (_info != null && _info.IsOpen) _info.Close();
    }

    public void OpenStore()
    {
        if (store == null) { Report("This dashboard has no store. Run FPSKit > Build Dashboard."); return; }
        CloseScreens();
        ShowDashboard(false);
        if (topBar != null) topBar.ShowTab(MenuTopBar.Tab.Store);
        store.Open();
        // Looking is what clears the dot.
        if (topBar != null) topBar.RefreshStoreDot();
    }

    public void OpenAchievements()
    {
        if (achievements == null) { Report("This dashboard has no achievements screen. Run FPSKit > Build Dashboard."); return; }
        PlayerStats.RefreshFromCatalog(catalog);
        CloseScreens();
        ShowDashboard(false);
        if (topBar != null) topBar.ShowTab(MenuTopBar.Tab.Achievements);
        achievements.Open();
    }

    public void OpenLoadout()
    {
        if (_loadout == null) return;
        CloseScreens();
        ShowDashboard(false);
        if (topBar != null) topBar.ShowTab(MenuTopBar.Tab.Loadout);
        _loadout.Open();
    }

    public void OpenInfo()
    {
        if (_info == null) return;
        CloseScreens();
        ShowDashboard(false);
        _info.Open();
    }

    public void OpenDossier()
    {
        if (dossier == null) { Report("This dashboard has no list screen. Run FPSKit > Build Dashboard."); return; }
        ShowDashboard(false);
        dossier.Open();
    }

    public void OpenInstructions()
    {
        if (instructions == null) { Report("This dashboard has no instructions screen. Run FPSKit > Build Dashboard."); return; }
        ShowDashboard(false);
        instructions.Open();
    }

    void TellStory()
    {
        if (story == null || campaign == null) return;
        story.Bind(campaign);
        if (!story.HasAnythingToSay(campaign)) return;
        ShowDashboard(false, hideTopBar: true);
        story.Open();
    }

    void OnOverlayClosed()
    {
        // A zone opening was being read: carry on to what it was shown in front of.
        if (_afterStory != null)
        {
            var entry = _afterStory;
            bool launch = _afterStoryLaunch;
            _afterStory = null;
            if (launch)
            {
                ShowDashboard(true);
                Launch(entry, Missions.NextLevel(entry));
            }
            else Choose(entry);
            return;
        }

        // Another screen may still be up (the store opened from the loadout).
        if (AnyScreenOpen()) return;

        ShowDashboard(true);
        if (topBar != null) topBar.ShowTab(MenuTopBar.Tab.Play);
        _fitted = new Vector2(-1f, -1f);
        Refresh();
    }

    bool AnyScreenOpen() =>
        (store != null && store.IsOpen) || (achievements != null && achievements.IsOpen) ||
        (_loadout != null && _loadout.IsOpen) || (levelSelect != null && levelSelect.IsOpen) ||
        (instructions != null && instructions.IsOpen) || (dossier != null && dossier.IsOpen) ||
        (_info != null && _info.IsOpen) || (story != null && story.IsOpen);

    /// <summary>
    /// Hides the dashboard's own content behind another screen, and puts it back exactly as it
    /// was. What was showing is remembered rather than assumed, so something conditional is
    /// not switched on by coming back. The top bar stays up unless the story is covering it.
    /// </summary>
    void ShowDashboard(bool shown, bool hideTopBar = false)
    {
        if (topBar != null) topBar.SetShown(shown || !hideTopBar);
        if (dashboardOnly == null) return;

        if (!shown)
        {
            if (_dashboardHidden) return;
            _dashboardHidden = true;
            if (_dashboardWasShown == null || _dashboardWasShown.Length != dashboardOnly.Length)
                _dashboardWasShown = new bool[dashboardOnly.Length];
            for (int i = 0; i < dashboardOnly.Length; i++)
            {
                if (dashboardOnly[i] == null) continue;
                _dashboardWasShown[i] = dashboardOnly[i].activeSelf;
                dashboardOnly[i].SetActive(false);
            }
            return;
        }

        if (!_dashboardHidden) return;
        _dashboardHidden = false;
        for (int i = 0; i < dashboardOnly.Length; i++)
        {
            if (dashboardOnly[i] == null) continue;
            bool was = _dashboardWasShown == null || i >= _dashboardWasShown.Length || _dashboardWasShown[i];
            dashboardOnly[i].SetActive(was);
        }
    }

    // ======================================================================
    // Layout per device.
    // ======================================================================

    /// <summary>
    /// A handset gets its own arrangement, not this one smaller: the tabs move from the bar
    /// onto a rail down the left edge, the rank moves from the bar into the welcome line, the
    /// loadout strip and next-reward card go (both are a tab away), the arenas become one row
    /// that scrolls sideways, and the mission card keeps its column so PLAY MISSION stays
    /// under the right thumb. Tablets and desktops keep what the builder authored, and this
    /// does nothing on them.
    /// </summary>
    void ApplyFormLayout()
    {
        bool handset = DeviceProfile.CurrentForm == DeviceProfile.Form.Handset;
        if (topBar != null)
        {
            if (topBar.rankBlock != null) topBar.rankBlock.SetActive(!handset);
            topBar.UseRail(handset);
        }
        if (rankLine != null) rankLine.gameObject.SetActive(handset);
        if (hideOnHandset != null)
            foreach (var go in hideOnHandset) if (go != null) go.SetActive(!handset);
        if (mission != null) mission.SetCompact(handset);
        if (!handset) return;
        if (leftColumn != null) leftColumn.anchorMax = new Vector2(0.57f, leftColumn.anchorMax.y);
        if (rightColumn != null) rightColumn.anchorMin = new Vector2(0.58f, rightColumn.anchorMin.y);

        if (dashboardOnly != null) foreach (var go in dashboardOnly) InsetForRail(go);
        if (store != null) InsetForRail(store.panel);
        if (achievements != null) InsetForRail(achievements.panel);
        if (instructions != null) InsetForRail(instructions.panel);
        if (dossier != null) InsetForRail(dossier.panel);
        if (levelSelect != null) InsetForRail(levelSelect.panel);
    }

    /// <summary>
    /// Moves a screen's left edge clear of the rail. A no-op when the rail is not in use, and
    /// only ever applied once per screen, because it adds to whatever inset was authored.
    /// </summary>
    void InsetForRail(GameObject go)
    {
        if (go == null || topBar == null || !topBar.RailInUse) return;
        if (!(go.transform is RectTransform rt) || _railInset.Contains(rt)) return;
        _railInset.Add(rt);
        rt.offsetMin = new Vector2(rt.offsetMin.x + topBar.railWidth, rt.offsetMin.y);
    }

    // ======================================================================
    // Last run, messages, exit.
    // ======================================================================

    void ShowLastRun()
    {
        if (!GameSession.ReturnedFromRun || toasts == null) return;
        var result = GameSession.LastResult;
        string where = string.IsNullOrEmpty(GameSession.SelectedArenaLabel) ? "" : GameSession.SelectedArenaLabel + ", ";
        string level = string.IsNullOrEmpty(result.levelName) ? $"level {result.levelIndex + 1}" : result.levelName;
        string body = UIText.Row($"{where}{level}", $"{result.killed}/{result.total} KILLED",
                                 result.coins > 0 ? $"+{Wallet.Format(result.coins)} COINS" : "");
        var kind = result.stars >= 3 ? UIToast.Kind.Reward : result.stars > 0 ? UIToast.Kind.Success : UIToast.Kind.Warning;
        string stars = result.stars > 0 ? $"  {result.stars}/3 STARS" : "";
        var toast = toasts.Push(kind, result.stars > 0 ? "star-filled" : "alert-triangle", result.Title + stars, body, 6f);
        lastRunPanel = toast.gameObject;
        GameSession.ClearLastRun();
    }

    void Report(string message)
    {
        if (toasts != null) toasts.Push(UIToast.Kind.Warning, "alert-triangle", "Not available", message);
        Debug.LogWarning($"[MainMenu] {message}", this);
    }

    /// <summary>Opens the confirmation. What the Quit icon does.</summary>
    public void AskToExit()
    {
        if (exitConfirmPanel == null) { Exit(); return; }
        exitConfirmPanel.SetActive(true);
        exitConfirmPanel.transform.SetAsLastSibling();
        // On Stay rather than Quit: the default of a confirmation is never the irreversible one.
        if (cancelExitButton != null) cancelExitButton.Select();
    }

    public void CancelExit()
    {
        if (exitConfirmPanel != null) exitConfirmPanel.SetActive(false);
    }

    public void Exit() => GameDirector.ExitApplication();
}
