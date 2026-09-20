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

        // Before anything is drawn, because a star earned in the level just finished may
        // have opened something and the dashboard is where the player is told. The call
        // is idempotent, so arriving here by any route -- booting, finishing a level,
        // walking out of one -- asks the same question and gets the same answer.
        _granted = Campaign.RefreshUnlocks(catalog, store != null ? store.catalog : null);

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

        Wire(storeButton, OpenStore);
        Wire(exitButton, AskToExit);
        Wire(confirmExitButton, Exit);
        Wire(cancelExitButton, CancelExit);

        if (exitConfirmPanel != null) exitConfirmPanel.SetActive(false);
    }

    void OnDestroy()
    {
        if (levelSelect != null)
        {
            levelSelect.LevelChosen -= Launch;
            levelSelect.Closed -= OnLevelSelectClosed;
        }

        if (store != null) store.Closed -= OnStoreClosed;
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
    /// Sizes the grid cells to the space there actually is, in both directions.
    ///
    /// A GridLayoutGroup has one fixed cell size, which is only ever right at one window
    /// shape. Deriving it from the width alone was still not enough: three cards sized to
    /// fill a wide dashboard are tall enough that two rows overflow the bottom of the
    /// screen, and a GridLayoutGroup does not clip or scroll -- it just draws the last
    /// row past the edge, which is how the bottom three cards ended up with their
    /// descriptions cut off.
    ///
    /// So the cell is the smaller of what the width allows and what the height allows.
    /// Whichever axis is tighter wins, and everything fits with nothing to scroll.
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

        int columns = Mathf.Max(1, gridColumns);
        int rows = Mathf.Max(1, Mathf.CeilToInt(_cards.Count / (float)columns));

        float byWidth = (width
                         - _layout.padding.left - _layout.padding.right
                         - _layout.spacing.x * (columns - 1)) / columns;

        float byHeight = (height
                          - _layout.padding.top - _layout.padding.bottom
                          - _layout.spacing.y * (rows - 1)) / rows / cardAspect;

        float cell = Mathf.Max(80f, Mathf.Min(byWidth, byHeight));

        _layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        _layout.constraintCount = columns;
        _layout.cellSize = new Vector2(cell, cell * cardAspect);

        // Centred once the height is the limit, so a narrow window leaves an even margin
        // instead of all the slack on one side.
        _layout.childAlignment = TextAnchor.UpperCenter;
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

        foreach (var entry in catalog.arenas)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.sceneName)) continue;

            var card = Instantiate(cardTemplate, cardParent);
            card.gameObject.SetActive(true);

            bool loadable = Application.CanStreamedLevelBeLoaded(entry.sceneName);
            if (!loadable) missing++;

            var captured = entry;
            card.Bind(entry, () => Choose(captured));

            if (!loadable && card.button != null) card.button.interactable = false;

            _cards.Add(card);
        }

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

        ShowDashboard(false);
        levelSelect.Open(entry);
    }

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
