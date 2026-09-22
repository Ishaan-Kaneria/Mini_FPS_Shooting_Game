using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The dashboard: pick an arena, see how you have been doing, or leave.
///
/// Picking an arena no longer starts a run. It opens that arena's ladder -- see
/// <see cref="LevelSelectPanel"/> -- because an arena is now six to eight levels with
/// an unlock chain through them, and which one to play is a question only the player
/// can answer.
///
/// It is the first scene the game loads and the one every run returns to, however that
/// run ended. That is the whole reason it exists -- the kit used to boot straight into
/// an arena and offer a quit key that, in a browser, had nowhere to go, so the only way
/// out of a run was to close the tab.
///
/// The grid is built from <see cref="ArenaCatalog"/> at runtime rather than laid out in
/// the scene, so adding an arena is an entry in the catalog and never a rebuild of this
/// scene.
/// </summary>
[DisallowMultipleComponent]
public class MainMenuController : MonoBehaviour
{
    [Header("Content")]
    public ArenaCatalog catalog;

    [Tooltip("The campaign: which zones are played in what order, who holds each one, " +
             "and what falling to the player hands over.\n\n" +
             "The grid is ordered by this and gated on it. Without one every arena in " +
             "the catalog is open and in catalog order, which is what this game was " +
             "before the story -- so a dashboard missing it says so on screen rather " +
             "than quietly playing the old game.")]
    public CampaignData campaign;

    [Tooltip("Music and interface sounds. Optional -- the dashboard is silent without one.")]
    public UISounds sounds;

    [Header("Level Select")]
    [Tooltip("The screen an arena card opens. Without one the card falls back to " +
             "launching the furthest level the player has unlocked, so the dashboard " +
             "still works in a scene built before this existed.")]
    public LevelSelectPanel levelSelect;

    [Tooltip("Hidden while the level select is open, so the two screens are not drawn " +
             "over each other. The grid, the heading and the record panel.")]
    public GameObject[] dashboardOnly;

    [Header("Store")]
    [Tooltip("The shop. Opened by the Store button, and hidden with the arenas while it " +
             "is up, exactly like the level select.")]
    public StorePanel store;

    [Tooltip("What the player has done. Opened by the Career button.")]
    public AchievementsPanel achievements;

    [Tooltip("How to play, read from the live bindings. Opened by the Help button.")]
    public InstructionsPanel instructions;

    [Tooltip("The story: the opening on a fresh profile, and one card per child as they " +
             "go down. It has no button -- it opens on the way in when there is " +
             "something owed, and never otherwise.")]
    public StoryPanel story;

    [Tooltip("The eight, and what is known about each. Opened by the List button.")]
    public DossierPanel dossier;

    [Tooltip("Opens the achievements screen.")]
    public Button achievementsButton;

    [Tooltip("Opens the list of the eight.")]
    public Button listButton;

    [Tooltip("Opens the instructions screen.")]
    public Button helpButton;

    public Button storeButton;

    [Tooltip("Coins in hand, shown on the dashboard so the player can see what a level " +
             "just paid without opening the store to find out.")]
    public TMP_Text coinText;

    [Header("Arena Grid")]
    [Tooltip("Parent the cards are cloned into. A layout group on it does the placing.")]
    public RectTransform cardParent;

    [Tooltip("Hidden card cloned once per catalog entry.")]
    public ArenaCard cardTemplate;

    [Tooltip("Columns in the grid. The cell width is worked out from this and the space " +
             "actually available, so the grid fits whatever shape the window is.")]
    [Min(1)] public int gridColumns = 3;

    [Tooltip("Card height as a fraction of its width.")]
    [Min(0.1f)] public float cardAspect = 0.78f;

    [Tooltip("Heading above the grid. Moved with it when there is no result to show.")]
    public RectTransform arenaHeading;

    [Header("Chrome")]
    [Tooltip("The title bar. Compressed on a handset, where its authored height is a " +
             "sixth of the screen rather than a tenth of it.")]
    public RectTransform headerPanel;

    [Tooltip("The row of dashboard buttons along the bottom. Given more height on a " +
             "handset so each one clears the size a thumb can reliably hit.")]
    public RectTransform navRow;

    [Tooltip("Space kept clear at the top for the result strip, in reference pixels.")]
    public float topWithResult = 320f;

    [Tooltip("And what to use when there is no result -- a dashboard opened cold should " +
             "not have a strip of empty screen where a message would have been.")]
    public float topWithoutResult = 200f;

    [Header("Profile")]
    [Tooltip("The record panel. Hidden with the grid while the level select is open.")]
    public GameObject profilePanel;

    public TMP_Text playerNameText;

    [Tooltip("Stars earned across every arena in the catalog. This replaced \"best " +
             "wave\": with levels there is no best run to report, and the stars are " +
             "what the player is actually collecting.")]
    public TMP_Text starsText;

    public TMP_Text bestScoreText;
    public TMP_Text runsText;
    public TMP_Text killsText;

    [Tooltip("Coins earned across every level ever played. Not the balance -- this one " +
             "never goes down, so it measures how much has been played where the balance " +
             "only measures what has not been spent yet.")]
    public TMP_Text coinsEarnedText;

    [Header("Last Run")]
    [Tooltip("Shown only when the dashboard was reached by finishing a run, so a cold " +
             "start is not greeted by the results of a game nobody played.")]
    public GameObject lastRunPanel;

    public TMP_Text lastRunTitle;
    public TMP_Text lastRunDetail;

    [Header("Buttons")]
    public Button exitButton;

    [Tooltip("Modal shown before leaving. Exiting is the one irreversible thing here -- " +
             "in a browser it replaces the page, and there is no undo for a mis-click.")]
    public GameObject exitConfirmPanel;

    public Button confirmExitButton;
    public Button cancelExitButton;

    [Tooltip("The Exit Game row. Listed in dashboardOnly so the level select can hide " +
             "it: a button left switched on behind a full-screen panel is one the " +
             "pointer can never reach.\n\n" +
             "It is shown in a browser too, where there is no application to close -- " +
             "ExitApplication hands over to WebDevice.Exit, which closes the tab if the " +
             "browser allows it and otherwise replaces the page with a sign-off.")]
    public GameObject exitRow;

    [Header("Status")]
    [Tooltip("Where a problem is reported, rather than only to the console -- a player " +
             "clicking a card that does nothing has no console to read.")]
    public TMP_Text statusText;

    readonly List<ArenaCard> _cards = new List<ArenaCard>();

    GridLayoutGroup _layout;
    float _fittedWidth = -1f;
    float _fittedHeight = -1f;

    /// <summary>What was on screen before the level select covered it. See ShowDashboard.</summary>
    bool[] _dashboardWasShown;

    /// <summary>Whether the dashboard is currently hidden behind an overlay.</summary>
    bool _dashboardHidden;

    // ======================================================================
    /// <summary>
    /// The power the campaign handed over on the way into this screen, or null. Held so
    /// that the announcement can be made once, by whatever is built to make it -- the
    /// grant itself already happened and is recorded, so losing this only costs the
    /// fanfare and never the power.
    /// </summary>
    string _granted;

    /// <summary>What was unlocked arriving here, or null. Cleared once it is shown.</summary>
    public string TakeGrantedPower()
    {
        var id = _granted;
        _granted = null;
        return id;
    }

    void Start()
    {
        // A run that ended left these set for its own reasons. The dashboard is a menu:
        // it runs at normal speed, with a cursor, and with gameplay input off so a held
        // key from the last run cannot leak into it.
        Time.timeScale = 1f;
        PlayerMotor.InputEnabled = false;
        MobileInput.Reset();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Before anything is measured. Shrinking the canvas's reference grows every unit
        // on it, so the grid has to be fitted against the new size rather than the
        // authored one -- and FitGrid below is the first thing that measures.
        PhoneUI.Apply(GetComponentInParent<Canvas>());
        ApplyFormLayout();

        // Before anything is drawn, because a star earned in the level just finished may
        // have opened something and the dashboard is where the player is told. The call
        // is idempotent, so arriving here by any route -- booting, finishing a level,
        // walking out of one -- asks the same question and gets the same answer.
        _granted = Campaign.RefreshUnlocks(campaign, catalog,
                                           store != null ? store.catalog : null);

        // The two totals that cannot be counted as they happen -- stars are the best of
        // each level's attempts rather than the sum, and an arena is finished or it is
        // not. Recomputed here because the dashboard is both the only place that has the
        // catalogue and the only place these are read.
        PlayerStats.RefreshFromCatalog(catalog);

        BuildGrid();
        ShowProfile();
        ShowLastRun();
        FitGrid();

        if (levelSelect != null)
        {
            levelSelect.LevelChosen += Launch;
            levelSelect.Closed += OnLevelSelectClosed;
        }

        if (store != null) store.Closed += OnStoreClosed;
        if (achievements != null) achievements.Closed += OnOverlayClosed;
        if (instructions != null) instructions.Closed += OnOverlayClosed;
        if (story != null) story.Closed += OnOverlayClosed;
        if (dossier != null) dossier.Closed += OnOverlayClosed;

        Wire(storeButton, OpenStore);
        Wire(achievementsButton, OpenAchievements);
        Wire(listButton, OpenDossier);
        Wire(helpButton, OpenInstructions);
        Wire(exitButton, AskToExit);
        Wire(confirmExitButton, Exit);
        Wire(cancelExitButton, CancelExit);

        if (exitConfirmPanel != null) exitConfirmPanel.SetActive(false);

        TellStory();
    }

    /// <summary>
    /// Opens the story if it is owed anything -- the opening on a fresh profile, or the
    /// beat for a child who has just gone down.
    ///
    /// <b>Last in Start, and only when there is something to say.</b> Last, because it
    /// covers the dashboard and everything underneath has to be built and measured
    /// before it is hidden. Only when owed, because an overlay that opens empty is a
    /// screen the player dismisses for no reason, and this one opens at the exact moment
    /// they have come back to play the next level.
    ///
    /// It is not in <see cref="dashboardOnly"/> for the same reason the other overlays
    /// are not: that list is what gets hidden *behind* a panel, and this is a panel.
    /// </summary>
    void TellStory()
    {
        if (story == null || campaign == null) return;

        story.Bind(campaign);
        if (!story.HasAnythingToSay(campaign)) return;

        ShowDashboard(false);
        story.Open();
    }

    void OnDestroy()
    {
        if (levelSelect != null)
        {
            levelSelect.LevelChosen -= Launch;
            levelSelect.Closed -= OnLevelSelectClosed;
        }

        if (store != null) store.Closed -= OnStoreClosed;
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
        // Nothing behind a full-screen panel needs laying out while it is covering the
        // screen, and the grid it would be measuring is switched off anyway.
        if (levelSelect != null && levelSelect.IsOpen) return;
        if (store != null && store.IsOpen) return;

        FitGrid();

        // Escape backs out of the dialog. A modal with no keyboard way out is a modal
        // that traps anyone whose pointer is not where they expected it to be.
        if (exitConfirmPanel != null && exitConfirmPanel.activeSelf &&
            (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Q)))
            CancelExit();
    }

    /// <summary>
    /// Sizes the arena cards to the space there actually is, in both directions, and
    /// chooses how many go across. <see cref="UIGrid"/> owns the arithmetic; the three
    /// card screens share it rather than keeping three copies that drift.
    ///
    /// Capped at three across on a handset. The measurement on its own would go wider --
    /// a landscape phone is twice as wide as it is tall, so a single row of six wins on
    /// area -- and six cards across 155mm is six thumbnails 20mm wide with a name under
    /// each that has to shrink to fit. Two rows of three is the same screen spent on
    /// cards a thumb can hit.
    /// </summary>
    void FitGrid()
    {
        if (cardParent == null) return;
        if (_layout == null) _layout = cardParent.GetComponent<GridLayoutGroup>();
        if (_layout == null) return;

        float width = cardParent.rect.width;
        float height = cardParent.rect.height;

        if (width <= 1f || height <= 1f) return;
        if (Mathf.Abs(width - _fittedWidth) < 0.5f &&
            Mathf.Abs(height - _fittedHeight) < 0.5f) return;

        _fittedWidth = width;
        _fittedHeight = height;

        int cap = DeviceProfile.CurrentForm == DeviceProfile.Form.Handset ? 3 : 0;

        UIGrid.Fit(_layout, cardParent, _cards.Count, cardAspect, 80f, cap);
    }

    /// <summary>
    /// Rearranges the dashboard for the machine it is on.
    ///
    /// <b>A handset does not get this screen made smaller; it gets a different one.</b>
    /// Ishaan put it in one line -- a minimised version of the desktop's dashboard is
    /// not the answer -- and the reason is that what fails on a 155mm screen is the
    /// arrangement, not the size. The authored layout spends its right-hand third on a
    /// five-row career panel and about three hundred reference units at the top on a
    /// title bar, a result strip and a heading. On a monitor that is a comfortable use
    /// of the space. On a landscape phone, whose short edge is the scarce one, it is
    /// most of the screen given to furniture, with six arena cards sharing what is left.
    ///
    /// So on a handset: the career panel goes (its one figure a player checks before
    /// the store, the balance, is already in the header), the grid takes the width that
    /// frees, the chrome is compressed to what it needs, and the button row grows until
    /// each button clears a thumb. Nothing is scaled here -- <see cref="PhoneUI"/> does
    /// that, and only to keep the words readable.
    ///
    /// Everything it touches is anchored rather than positioned, so a tablet and a
    /// desktop keep exactly what the builder authored and this method does nothing at
    /// all on them.
    /// </summary>
    void ApplyFormLayout()
    {
        if (DeviceProfile.CurrentForm != DeviceProfile.Form.Handset) return;

        // The career panel. HideOnPhone marks it too, but that runs in its own Start and
        // this has to be true before the grid is measured -- the width it frees is the
        // whole point.
        if (profilePanel != null) profilePanel.SetActive(false);

        if (headerPanel != null)
        {
            headerPanel.sizeDelta = new Vector2(-32f, 74f);
            headerPanel.anchoredPosition = new Vector2(0f, -16f);
        }

        if (lastRunPanel != null && lastRunPanel.transform is RectTransform strip)
        {
            strip.sizeDelta = new Vector2(-32f, 70f);
            strip.anchoredPosition = new Vector2(0f, -100f);
        }

        // What the grid starts below, with and without a result to report. Both are
        // reference units and both were written for a 1080-unit screen; a handset's is
        // about 600 once the reference is shrunk for legibility. They have to clear the
        // header and the heading above them -- the heading is placed 48 units above the
        // grid, so a top of 104 would have put it straight through the title bar.
        topWithResult = 228f;
        topWithoutResult = 150f;

        // Wider and shorter cards. The description is the part of a card a handset does
        // not show, so the height it occupied is height the card does not need -- and on
        // a screen twice as wide as it is tall, the height is what limits the cell. At
        // the authored 0.78 two rows of three came out 26mm across with half the width
        // left as margin; at 0.6 they are 33mm and the margin is gone.
        cardAspect = 0.6f;

        if (arenaHeading != null)
        {
            arenaHeading.anchorMax = new Vector2(1f, arenaHeading.anchorMax.y);
            arenaHeading.sizeDelta = new Vector2(-40f, 34f);
        }

        if (cardParent != null)
        {
            cardParent.anchorMax = new Vector2(1f, cardParent.anchorMax.y);
            cardParent.offsetMin = new Vector2(18f, 84f);
            cardParent.offsetMax = new Vector2(-18f, cardParent.offsetMax.y);
        }

        if (navRow != null)
        {
            // Tall enough that each button clears the nine millimetres below which a
            // thumb starts missing -- the same floor the in-game touch buttons use, for
            // the same reason, on the one screen where a miss means leaving the game.
            navRow.offsetMin = new Vector2(18f, 14f);
            navRow.offsetMax = new Vector2(-18f, 78f);
        }

        // Nothing has been measured yet, but say so anyway: this runs before the first
        // fit on the way in and could be called again if the screen ever changes class.
        _fittedWidth = _fittedHeight = -1f;
    }

    /// <summary>
    /// Pulls the grid up into the space the result strip would have used.
    ///
    /// The strip only appears after a run, so reserving its height unconditionally left
    /// a band of empty screen between the header and the arenas every time the dashboard
    /// was opened cold -- which is the first thing anybody sees.
    /// </summary>
    void LayOutForResult(bool resultShown)
    {
        float top = resultShown ? topWithResult : topWithoutResult;

        if (cardParent != null)
            cardParent.offsetMax = new Vector2(cardParent.offsetMax.x, -top);

        if (arenaHeading != null)
            arenaHeading.anchoredPosition = new Vector2(arenaHeading.anchoredPosition.x,
                                                        -(top - 48f));

        // The measured height has changed, so the cells have to be worked out again.
        _fittedHeight = -1f;
    }

    // ======================================================================
    /// <summary>
    /// The arenas to show, in the order the campaign plays them.
    ///
    /// <b>The campaign owns the order, not the catalog.</b> The catalog is a list of
    /// what exists; the campaign is the sequence it is played in, and the two are
    /// separate so that adding an arena and writing it into the story stay separate
    /// jobs. An arena the campaign does not mention is still shown -- at the end, and
    /// ungated -- because the kit has to keep working for a level it did not build.
    /// </summary>
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

        foreach (var card in _cards)
            if (card != null) Destroy(card.gameObject);

        _cards.Clear();

        if (catalog == null || catalog.arenas == null || catalog.arenas.Count == 0)
        {
            Report("No arenas in the catalog. Run FPSKit > Build Dashboard to fill it in.");
            return;
        }

        int missing = 0;
        int locked = 0;

        foreach (var entry in InCampaignOrder())
        {
            var card = Instantiate(cardTemplate, cardParent);
            card.gameObject.SetActive(true);

            bool loadable = Application.CanStreamedLevelBeLoaded(entry.sceneName);
            if (!loadable) missing++;

            bool open = Campaign.ArenaUnlocked(campaign, entry.ProgressKey);
            if (!open) locked++;

            var captured = entry;
            card.Bind(entry, _cards.Count, open, Campaign.LockNote(campaign, entry.ProgressKey),
                      () => Choose(captured));

            // An arena missing from Build Settings is a separate failure from a locked
            // one, and it wins: a card that cannot load must not look merely locked,
            // because the player would go and earn the key to a door that is broken.
            if (!loadable && card.button != null) card.button.interactable = false;

            _cards.Add(card);
        }

        if (campaign == null)
            Report("This dashboard has no campaign asset, so every arena is open and the " +
                   "story is not being told. Run FPSKit > Build Dashboard.");
        else if (locked >= _cards.Count && _cards.Count > 0)
            Report("Every arena is locked, which cannot be right -- the campaign's first " +
                   "zone is always open. Run FPSKit > Reset Campaign to Defaults.");

        // A card that cannot load its scene is the one failure a player cannot diagnose:
        // it simply does nothing. Say it on the screen rather than only in a log nobody
        // in a browser will ever see.
        if (missing > 0)
            Report($"{missing} arena(s) are not in Build Settings and cannot be loaded. " +
                   "Rebuild them with FPSKit > Build Scene > Build ALL Themes.");
    }

    void ShowProfile()
    {
        if (playerNameText != null) playerNameText.text = PlayerProfile.Name;
        if (coinText != null) coinText.text = $"{Wallet.Format(Wallet.Balance)} <size=62%>COINS</size>";
        if (starsText != null) starsText.text = $"{TotalStars()} / {PossibleStars()}";
        if (bestScoreText != null) bestScoreText.text = PlayerProfile.BestScore.ToString("N0");
        if (runsText != null) runsText.text = PlayerProfile.Runs.ToString();
        if (killsText != null) killsText.text = PlayerProfile.TotalKills.ToString("N0");
        if (coinsEarnedText != null) coinsEarnedText.text = Wallet.Format(Wallet.LifetimeEarned);
    }

    /// <summary>
    /// Stars taken across every arena the catalog offers.
    ///
    /// Handed to <see cref="Campaign"/> rather than counted here, because the campaign
    /// gates its rewards on the same number and two routines that both add up the stars
    /// are two routines that can disagree about how many the player has -- which would
    /// show up as a card promising a power the gate does not think is earned.
    /// </summary>
    public int TotalStars() => Campaign.TotalStars(catalog);

    /// <summary>And how many there are to take, so the number has a denominator.</summary>
    public int PossibleStars() => Campaign.MaxStars(catalog);

    void ShowLastRun()
    {
        bool show = GameSession.ReturnedFromRun;

        if (lastRunPanel != null) lastRunPanel.SetActive(show);
        LayOutForResult(show);

        if (!show) return;

        string where = string.IsNullOrEmpty(GameSession.SelectedArenaLabel)
            ? ""
            : $" in {GameSession.SelectedArenaLabel}";

        var result = GameSession.LastResult;

        if (lastRunTitle != null)
        {
            string stars = result.stars > 0 ? new string('*', result.stars) + "  " : "";
            lastRunTitle.text = $"{stars}{result.Title}";
        }

        if (lastRunDetail != null)
        {
            string level = string.IsNullOrEmpty(result.levelName)
                ? $"Level {result.levelIndex + 1}"
                : $"Level {result.levelIndex + 1} - {result.levelName}";

            // Capitals like every other list on this screen. It read as prose while the
            // card beside it read as a row of values, which is two typefaces' worth of
            // difference between two labels six pixels apart.
            lastRunDetail.text = UIText.Row($"{level}{where}",
                                            $"{result.killed}/{result.total} KILLED",
                                            $"{result.score:N0} POINTS",
                                            result.coins > 0 ? $"+{Wallet.Format(result.coins)} COINS" : "");
        }

        // Read once. Re-opening the dashboard later should not replay it as news.
        GameSession.ClearLastRun();
    }

    // ======================================================================

    /// <summary>
    /// What clicking an arena card does: open its ladder.
    ///
    /// The card used to start a run straight away, which is why this is a separate
    /// method from <see cref="Launch"/> rather than a branch inside it -- an arena is
    /// chosen in two steps now, and the second step is the one that loads a scene.
    /// </summary>
    public void Choose(ArenaCatalog.Entry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.sceneName)) return;

        if (!Campaign.ArenaUnlocked(campaign, entry.ProgressKey))
        {
            Report($"\"{entry.Label}\" is not open yet. {Campaign.LockNote(campaign, entry.ProgressKey)}");
            return;
        }

        if (levelSelect == null)
        {
            // No level select in this scene: fall through to the furthest level the
            // player has earned, which is the least surprising thing a bare card can do.
            Launch(entry, LevelProgress.HighestUnlocked(entry.ProgressKey, entry.LevelCount));
            return;
        }

        if (entry.LevelCount <= 0)
        {
            Report($"\"{entry.Label}\" has no levels. Run FPSKit > Reset Level Sets, then " +
                   "FPSKit > Build Dashboard.");
            return;
        }

        // The zone's own opening, once, before its ladder. Read on the way in, which is
        // when a line about the place the player is about to walk into means anything.
        int zone = campaign != null ? campaign.IndexOfArena(entry.ProgressKey) : -1;

        if (story != null && zone >= 0 && !Campaign.OpeningSeen(zone))
        {
            var opening = campaign.ZoneAt(zone);

            if (opening != null && !string.IsNullOrWhiteSpace(opening.opening))
            {
                // Held rather than opened now: the ladder opens when the card closes,
                // and OnOverlayClosed is the one place that happens.
                _afterStory = entry;

                story.Bind(campaign);
                story.QueueZoneOpening(zone);

                ShowDashboard(false);
                story.Open();
                return;
            }
        }

        ShowDashboard(false);
        levelSelect.Open(entry);
    }

    /// <summary>The arena to open a ladder for once a story card has been read, or null.</summary>
    ArenaCatalog.Entry _afterStory;

    /// <summary>Opens the shop. What the Store button does.</summary>
    public void OpenStore()
    {
        if (store == null)
        {
            Report("This dashboard has no store. Run FPSKit > Build Dashboard.");
            return;
        }

        ShowDashboard(false);
        store.Open();
    }

    /// <summary>Opens the achievements screen. What the Career button does.</summary>
    public void OpenAchievements()
    {
        if (achievements == null)
        {
            Report("This dashboard has no achievements screen. Run FPSKit > Build Dashboard.");
            return;
        }

        // Recomputed on the way in, not only at Start: a player who cleared a level, came
        // back and opened this would otherwise be shown the star count from before the run.
        PlayerStats.RefreshFromCatalog(catalog);

        ShowDashboard(false);
        achievements.Open();
    }

    /// <summary>Opens the list of the eight. What the List button does.</summary>
    public void OpenDossier()
    {
        if (dossier == null)
        {
            Report("This dashboard has no list screen. Run FPSKit > Build Dashboard.");
            return;
        }

        ShowDashboard(false);
        dossier.Open();
    }

    /// <summary>Opens the instructions. What the Help button does.</summary>
    public void OpenInstructions()
    {
        if (instructions == null)
        {
            Report("This dashboard has no instructions screen. Run FPSKit > Build Dashboard.");
            return;
        }

        ShowDashboard(false);
        instructions.Open();
    }

    void OnOverlayClosed()
    {
        // A zone opening was being read. The ladder it was shown for opens now rather
        // than the dashboard coming back, or the player reads a card about a place and
        // is returned to the menu they were already leaving.
        if (_afterStory != null)
        {
            var entry = _afterStory;
            _afterStory = null;

            Choose(entry);
            return;
        }

        ShowDashboard(true);
    }

    void OnStoreClosed()
    {
        ShowDashboard(true);

        // Coins and the record panel can both have moved while the shop was open, so
        // the dashboard is re-read rather than left showing the balance from before.
        ShowProfile();

        _fittedWidth = _fittedHeight = -1f;
    }

    void OnLevelSelectClosed()
    {
        ShowDashboard(true);

        // The panel measured its grid against a box that was not on screen while the
        // arenas were hidden, so the arena grid has to be measured again on the way back.
        _fittedWidth = _fittedHeight = -1f;
    }

    /// <summary>
    /// Hides the arena picker behind the level select, and puts it back exactly as it
    /// was.
    ///
    /// What was showing is remembered rather than assumed, because some of these are
    /// conditional -- the result strip only appears after a level, and the status line
    /// only after something went wrong. Switching everything back on unconditionally
    /// would announce the result of a level that was never played, every time somebody
    /// backed out of a ladder.
    /// </summary>
    void ShowDashboard(bool shown)
    {
        if (dashboardOnly == null) return;

        if (!shown)
        {
            // Already hidden: record nothing and hide nothing. A second hide would take
            // its "what was showing" snapshot of a dashboard that is already switched
            // off, so everything would come back as false and the dashboard would never
            // be shown again -- which is exactly what one overlay opening another does.
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

        _dashboardHidden = false;

        for (int i = 0; i < dashboardOnly.Length; i++)
        {
            if (dashboardOnly[i] == null) continue;

            bool was = _dashboardWasShown == null || i >= _dashboardWasShown.Length
                       || _dashboardWasShown[i];

            dashboardOnly[i].SetActive(was);
        }
    }

    /// <summary>Starts a level. The one place a scene load can begin.</summary>
    public void Launch(ArenaCatalog.Entry entry, int levelIndex)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.sceneName)) return;

        if (!Application.CanStreamedLevelBeLoaded(entry.sceneName))
        {
            Report($"\"{entry.sceneName}\" is not in Build Settings, so it cannot be loaded.");
            return;
        }

        // Two gates, asked in the order the player meets them: the zone has to be open
        // before a level inside it can be. Both are asked here as well as on the way in,
        // because this is the one place a scene load can begin and a screen built later
        // must not be able to route around either.
        if (!Campaign.ArenaUnlocked(campaign, entry.ProgressKey))
        {
            Report($"\"{entry.Label}\" is not open yet. {Campaign.LockNote(campaign, entry.ProgressKey)}");
            return;
        }

        if (!LevelProgress.IsUnlocked(entry.ProgressKey, levelIndex))
        {
            Report($"Level {levelIndex + 1} of \"{entry.Label}\" is still locked.");
            return;
        }

        GameSession.ChooseArena(entry.sceneName, entry.Label);
        GameSession.ChooseLevel(levelIndex);

        // Handed back before the arena loads. The menu turned it off so a held key could
        // not leak in here; leaving it off would load a run the player cannot move in.
        PlayerMotor.InputEnabled = true;

        SceneManager.LoadScene(entry.sceneName);
    }

    /// <summary>Opens the confirmation. What the Exit button on the dashboard does.</summary>
    public void AskToExit()
    {
        if (exitConfirmPanel == null)
        {
            // No dialog wired -- leaving without one beats a button that does nothing.
            Exit();
            return;
        }

        exitConfirmPanel.SetActive(true);

        // Focused so Return and the gamepad reach it, but on Stay rather than Exit: the
        // default action of a confirmation should never be the irreversible one.
        if (cancelExitButton != null) cancelExitButton.Select();
    }

    public void CancelExit()
    {
        if (exitConfirmPanel != null) exitConfirmPanel.SetActive(false);
        if (exitButton != null) exitButton.Select();
    }

    public void Exit() => GameDirector.ExitApplication();

    void Report(string message)
    {
        if (statusText != null) statusText.text = message;
        Debug.LogWarning($"[MainMenu] {message}", this);
    }
}
