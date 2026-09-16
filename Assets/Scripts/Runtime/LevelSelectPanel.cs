using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The screen between the dashboard and the fight: pick a level in the arena that was
/// just chosen.
///
/// Clicking an arena used to start a run immediately. It cannot any more, because an
/// arena is a ladder of levels now and the player has to say which rung -- and because
/// the ladder is the progress: what is unlocked, what is still locked, and how many of
/// the three stars each cleared level gave up.
///
/// Built into the dashboard scene by the menu builder and driven by
/// <see cref="MainMenuController"/>, which owns the catalog. The tiles are cloned from
/// a template per level in the arena's <see cref="LevelSet"/>, so an arena that grows a
/// ninth level needs no rebuild of this scene.
/// </summary>
[DisallowMultipleComponent]
public class LevelSelectPanel : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("The root switched on when an arena is chosen. Hidden at Awake.")]
    public GameObject panel;

    [Tooltip("Parent the tiles are cloned into. A GridLayoutGroup on it does the placing.")]
    public RectTransform tileParent;

    [Tooltip("Hidden tile cloned once per level.")]
    public LevelButton tileTemplate;

    public Button backButton;

    [Header("Text")]
    public TMP_Text arenaNameText;
    public TMP_Text progressText;
    public TMP_Text hintText;
    public TMP_Text statusText;

    [Header("Grid")]
    [Tooltip("Columns. The cell size is worked out from this and the space actually " +
             "available, for the same reason the arena grid does it: a fixed cell is " +
             "only ever right at one window shape.")]
    [Min(1)] public int gridColumns = 4;

    [Tooltip("Tile height as a fraction of its width.")]
    [Min(0.1f)] public float tileAspect = 0.72f;

    // ======================================================================
    readonly List<LevelButton> _tiles = new List<LevelButton>();

    GridLayoutGroup _layout;
    float _fittedWidth = -1f;
    float _fittedHeight = -1f;

    /// <summary>The arena on screen, or null when the panel is closed.</summary>
    public ArenaCatalog.Entry Entry { get; private set; }

    public bool IsOpen => panel != null && panel.activeSelf;

    /// <summary>Levels currently drawn. Read by the flow test.</summary>
    public int TileCount => _tiles.Count;

    /// <summary>Raised with the level the player picked. The menu starts the run.</summary>
    public event System.Action<ArenaCatalog.Entry, int> LevelChosen;

    /// <summary>Raised when the player backs out. The menu shows the arenas again.</summary>
    public event System.Action Closed;

    void Awake()
    {
        HideAtLoad(panel, this);

        if (tileTemplate != null) tileTemplate.gameObject.SetActive(false);
    }

    /// <summary>
    /// Hides the panel at load.
    ///
    /// The panel must be a *different* GameObject from this one. A component that hides
    /// its own object here never gets to show it again: the builder leaves the panel
    /// switched off, so Awake has not run, and the first SetActive(true) is what finally
    /// runs it -- which switches the object straight back off. The screen then never
    /// opens and nothing is logged, so this says so rather than letting it happen.
    /// </summary>
    static bool HideAtLoad(GameObject panel, MonoBehaviour owner)
    {
        if (panel == null) return false;

        if (panel == owner.gameObject)
        {
            Debug.LogError($"[{owner.GetType().Name}] The panel is this object, so hiding it " +
                           "would stop it ever being shown again. Put this component on the " +
                           "canvas and point it at a child.", owner);
            return false;
        }

        panel.SetActive(false);
        return true;
    }


    void Start()
    {
        if (backButton == null) return;

        backButton.onClick.RemoveAllListeners();
        backButton.onClick.AddListener(Close);
    }

    void Update()
    {
        if (!IsOpen) return;

        FitGrid();

        // Escape and Q back out. A screen with no keyboard way out traps anyone whose
        // pointer is not where they expected it to be.
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Q)) Close();
    }

    // ======================================================================
    public void Open(ArenaCatalog.Entry entry)
    {
        if (entry == null) return;

        Entry = entry;
        Build(entry);

        if (panel != null) panel.SetActive(true);

        // The measured box has only just been shown, so the cells have to be worked out
        // again rather than trusting whatever the last arena left behind.
        _fittedWidth = _fittedHeight = -1f;

        if (backButton != null) backButton.Select();
    }

    public void Close()
    {
        if (panel != null) panel.SetActive(false);

        Entry = null;
        Closed?.Invoke();
    }

    // ======================================================================
    void Build(ArenaCatalog.Entry entry)
    {
        foreach (var tile in _tiles)
            if (tile != null) Destroy(tile.gameObject);

        _tiles.Clear();

        if (arenaNameText != null) arenaNameText.text = entry.Label.ToUpperInvariant();

        if (tileTemplate == null || tileParent == null)
        {
            Report("The level select has no tile template wired. Run FPSKit > Build Dashboard.");
            return;
        }

        var set = entry.levels;

        if (set == null || set.Count == 0)
        {
            Report($"\"{entry.Label}\" has no levels. Run FPSKit > Reset Level Sets, then " +
                   "FPSKit > Build Dashboard.");

            if (progressText != null) progressText.text = "";
            return;
        }

        Report("");

        int unlockedCount = 0;

        for (int i = 0; i < set.Count; i++)
        {
            var level = set.At(i);

            bool unlocked = LevelProgress.IsUnlocked(entry.ProgressKey, i);
            int stars = LevelProgress.StarsIn(entry.ProgressKey, i);

            if (unlocked) unlockedCount++;

            var tile = Instantiate(tileTemplate, tileParent);
            tile.gameObject.SetActive(true);

            int index = i;
            tile.Bind(index, level, stars, unlocked, () => Choose(index));

            _tiles.Add(tile);
        }

        int earned = LevelProgress.StarsInArena(entry.ProgressKey, set.Count);
        int possible = set.Count * 3;

        if (progressText != null)
            progressText.text = $"{earned} / {possible} STARS     " +
                                $"{unlockedCount} / {set.Count} UNLOCKED";

        if (hintText != null)
            hintText.text = "Clear a level to unlock the next. Kill everything before the " +
                            "clock runs out for three stars.";
    }

    void Choose(int index)
    {
        if (Entry == null) return;

        if (!LevelProgress.IsUnlocked(Entry.ProgressKey, index))
        {
            Report($"Level {index + 1} is locked. Clear level {index} first.");
            return;
        }

        LevelChosen?.Invoke(Entry, index);
    }

    /// <summary>
    /// Sizes the tiles to the space there actually is, in both directions.
    ///
    /// The same arithmetic as the arena grid, and for the same reason: a
    /// GridLayoutGroup has one fixed cell size and neither clips nor scrolls, so a cell
    /// that is only right at 16:9 draws the bottom row of levels off the bottom of a
    /// 4:3 window with nothing to say so.
    /// </summary>
    void FitGrid()
    {
        if (tileParent == null) return;
        if (_layout == null) _layout = tileParent.GetComponent<GridLayoutGroup>();
        if (_layout == null) return;

        float width = tileParent.rect.width;
        float height = tileParent.rect.height;

        if (width <= 1f || height <= 1f) return;
        if (Mathf.Abs(width - _fittedWidth) < 0.5f &&
            Mathf.Abs(height - _fittedHeight) < 0.5f) return;

        _fittedWidth = width;
        _fittedHeight = height;

        int columns = Mathf.Max(1, gridColumns);
        int rows = Mathf.Max(1, Mathf.CeilToInt(_tiles.Count / (float)columns));

        float byWidth = (width
                         - _layout.padding.left - _layout.padding.right
                         - _layout.spacing.x * (columns - 1)) / columns;

        float byHeight = (height
                          - _layout.padding.top - _layout.padding.bottom
                          - _layout.spacing.y * (rows - 1)) / rows / tileAspect;

        float cell = Mathf.Max(70f, Mathf.Min(byWidth, byHeight));

        _layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        _layout.constraintCount = columns;
        _layout.cellSize = new Vector2(cell, cell * tileAspect);
        _layout.childAlignment = TextAnchor.UpperCenter;
    }

    void Report(string message)
    {
        if (statusText != null) statusText.text = message;

        if (!string.IsNullOrEmpty(message)) Debug.LogWarning($"[LevelSelect] {message}", this);
    }
}
