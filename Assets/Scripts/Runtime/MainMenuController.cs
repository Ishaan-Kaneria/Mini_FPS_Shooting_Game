using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The dashboard: pick an arena, see how you have been doing, or leave.
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
    public TMP_Text playerNameText;
    public TMP_Text bestWaveText;
    public TMP_Text bestScoreText;
    public TMP_Text runsText;
    public TMP_Text killsText;

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

    [Tooltip("Hidden in a browser, where there is no application to close -- the button " +
             "still works there, but it hands over to the page rather than quitting.")]
    public GameObject exitRow;

    [Header("Status")]
    [Tooltip("Where a problem is reported, rather than only to the console -- a player " +
             "clicking a card that does nothing has no console to read.")]
    public TMP_Text statusText;

    readonly List<ArenaCard> _cards = new List<ArenaCard>();

    GridLayoutGroup _layout;
    float _fittedWidth = -1f;
    float _fittedHeight = -1f;

    // ======================================================================
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

        BuildGrid();
        ShowProfile();
        ShowLastRun();
        FitGrid();

        Wire(exitButton, AskToExit);
        Wire(confirmExitButton, Exit);
        Wire(cancelExitButton, CancelExit);

        if (exitConfirmPanel != null) exitConfirmPanel.SetActive(false);
    }

    static void Wire(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    void Update()
    {
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
            card.Bind(entry, PlayerProfile.BestWaveIn(entry.sceneName), () => Launch(captured));

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
        if (bestWaveText != null) bestWaveText.text = PlayerProfile.BestWave.ToString();
        if (bestScoreText != null) bestScoreText.text = PlayerProfile.BestScore.ToString("N0");
        if (runsText != null) runsText.text = PlayerProfile.Runs.ToString();
        if (killsText != null) killsText.text = PlayerProfile.TotalKills.ToString("N0");
    }

    void ShowLastRun()
    {
        bool show = GameSession.ReturnedFromRun;

        if (lastRunPanel != null) lastRunPanel.SetActive(show);
        LayOutForResult(show);

        if (!show) return;

        string where = string.IsNullOrEmpty(GameSession.SelectedArenaLabel)
            ? ""
            : $" in {GameSession.SelectedArenaLabel}";

        if (lastRunTitle != null)
            lastRunTitle.text = GameSession.LastOutcome == GameSession.Outcome.Died
                ? "YOU WERE KILLED"
                : "RUN ENDED";

        if (lastRunDetail != null)
            lastRunDetail.text =
                $"Wave {GameSession.LastWave}{where}   ·   " +
                $"{GameSession.LastScore:N0} points   ·   {GameSession.LastKills} kills";

        // Read once. Re-opening the dashboard later should not replay it as news.
        GameSession.ClearLastRun();
    }

    // ======================================================================
    public void Launch(ArenaCatalog.Entry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.sceneName)) return;

        if (!Application.CanStreamedLevelBeLoaded(entry.sceneName))
        {
            Report($"\"{entry.sceneName}\" is not in Build Settings, so it cannot be loaded.");
            return;
        }

        GameSession.ChooseArena(entry.sceneName, entry.Label);

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
