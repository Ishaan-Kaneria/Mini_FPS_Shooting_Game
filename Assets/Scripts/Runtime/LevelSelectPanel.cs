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
/// The header says how far the player has got here -- stars out of the ladder's and levels
/// cleared, the same words the arena card uses so the two screens cannot be read as
/// disagreeing -- and the zone's story opening sits under it at reading size, because this
/// is where somebody who skipped the card is standing when they wonder what it said.
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

    [Header("Header")]
    public TMP_Text arenaNameText;
    [Tooltip("The arena's own colour, as a 3px stripe beside its name -- the only place it appears.")]
    public FlatRect arenaStripe;
    public TMP_Text starsText;
    public TMP_Text clearedText;

    [Header("Story")]
    [Tooltip("The panel the zone's opening sits in. Hidden when the zone has none.")]
    public GameObject storyBlock;
    public TMP_Text storyTitleText;
    public TMP_Text hintText;

    [Tooltip("The campaign, so the hint line under the header can be this zone's own " +
             "opening rather than a generic instruction.\n\n" +
             "This is also where a story beat goes to be re-read: the panel opens on the " +
             "way into a zone, which is exactly where somebody who skipped a card is " +
             "standing when they wonder what it said.")]
    public CampaignData campaign;
    [Tooltip("Where something is wrong with the ladder itself, in words. Empty when all is well.")]
    public TMP_Text statusText;

    [Header("Grid")]
    [Tooltip("Columns at most. The cell size is worked out from this and the space actually " +
             "available, for the same reason the arena grid does it: a fixed cell is " +
             "only ever right at one window shape.")]
    [Min(1)] public int gridColumns = 4;

    [Tooltip("Tile height as a fraction of its width.")]
    [Min(0.1f)] public float tileAspect = 0.78f;

    [Tooltip("Scrolls the tiles sideways on a handset, where eight cards of this much text " +
             "cannot share one screen at a size anybody can read.")]
    public ScrollRect scroll;

    // ======================================================================
    readonly List<LevelButton> _tiles = new List<LevelButton>();

    GridLayoutGroup _layout;
    Transform _storyHome;
    int _storyIndex = 1;
    float _fittedWidth = -1f;
    float _fittedHeight = -1f;

    /// <summary>The arena on screen, or null when the panel is closed.</summary>
    public ArenaCatalog.Entry Entry { get; private set; }

    public bool IsOpen => panel != null && panel.activeSelf;

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
        if (storyBlock != null) _storyIndex = storyBlock.transform.GetSiblingIndex();

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
        if (GameInput.BackPressed) Close();
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

            if (starsText != null) starsText.text = "";
            if (clearedText != null) clearedText.text = "";
            return;
        }

        Report("");

        var t = UITheme.Active;
        string key = entry.ProgressKey;
        int next = Missions.NextLevel(entry);
        // The same level CURRENT MISSION names: the first open one without a star, else the
        // first open one short of three. Nothing is NEXT once the ladder is all three-star.
        bool nextIsNew = LevelProgress.StarsIn(key, next) < 3 && LevelProgress.IsUnlocked(key, next);
        Color arenaColor = t.StripeFor(entry.sceneName);

        for (int i = 0; i < set.Count; i++)
        {
            var tile = Instantiate(tileTemplate, tileParent);
            tile.gameObject.SetActive(true);

            int index = i;
            tile.Bind(index, set.Count, set.At(i), key, entry.preview, arenaColor,
                      LevelProgress.IsUnlocked(key, i), nextIsNew && i == next, () => Choose(index));

            _tiles.Add(tile);
        }

        int earned = LevelProgress.StarsInArena(key, set.Count);
        int cleared = LevelProgress.LevelsCleared(key, set.Count);

        if (arenaStripe != null) arenaStripe.color = arenaColor;
        if (starsText != null)
            starsText.text = t.Tabular($"{earned}/{set.Count * 3}") + " " + InputPrompts.Glyph("star-filled");
        if (clearedText != null)
            clearedText.text = t.Tabular($"{cleared}/{set.Count}") + " CLEARED";

        // On a handset the story is the first card of the sideways row rather than a panel
        // over it: a landscape phone has no height to give a paragraph above the cards, and
        // the cards' star conditions were what got squeezed out. It is read first, then
        // swiped past.
        if (storyBlock != null && _storyHome == null) _storyHome = storyBlock.transform.parent;
        if (storyBlock != null)
        {
            bool inRow = DeviceProfile.CurrentForm == DeviceProfile.Form.Handset;
            storyBlock.transform.SetParent(inRow ? tileParent : _storyHome, false);
            if (inRow) storyBlock.transform.SetAsFirstSibling();
            else storyBlock.transform.SetSiblingIndex(_storyIndex);
        }

        var zone = campaign != null ? campaign.ZoneForArena(key) : null;
        bool story = zone != null && !string.IsNullOrWhiteSpace(zone.opening);
        if (storyBlock != null) storyBlock.SetActive(story);
        if (hintText != null) hintText.text = story ? Campaign.Expand(zone.opening) : "";
        if (storyTitleText != null)
        {
            var holder = story ? campaign.HolderOf(zone) : null;
            storyTitleText.text = holder != null && !string.IsNullOrWhiteSpace(holder.displayName)
                ? holder.displayName.ToUpperInvariant()
                : "THE STORY";
        }
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
    /// Sizes the level tiles to the space there actually is, in both directions, and
    /// chooses how many go across. <see cref="UIGrid"/> owns the arithmetic, shared with
    /// the arena grid and the store.
    ///
    /// A tile is a number, a star row and a line of detail, so it survives being small
    /// better than a store card does -- four across on a handset is still four tiles a
    /// thumb can hit, and eight levels in two rows is the whole ladder without scrolling.
    /// </summary>
    void FitGrid()
    {
        if (tileParent == null) return;
        if (_layout == null) _layout = tileParent.GetComponent<GridLayoutGroup>();
        if (_layout == null) return;

        var box = scroll != null && scroll.viewport != null ? scroll.viewport : tileParent;
        float width = box.rect.width;
        float height = box.rect.height;

        if (width <= 1f || height <= 1f) return;
        if (Mathf.Abs(width - _fittedWidth) < 0.5f &&
            Mathf.Abs(height - _fittedHeight) < 0.5f) return;

        _fittedWidth = width;
        _fittedHeight = height;

        bool handset = DeviceProfile.CurrentForm == DeviceProfile.Form.Handset;

        // Centred over the view on a screen that holds the whole ladder; pinned to the
        // left edge when it scrolls, or the first card would start off screen.
        var anchor = handset ? new Vector2(0f, 1f) : new Vector2(0.5f, 1f);
        tileParent.anchorMin = tileParent.anchorMax = tileParent.pivot = anchor;
        tileParent.anchoredPosition = Vector2.zero;

        if (handset)
        {
            // One row the height of the view, scrolling sideways: a landscape phone has
            // width to spare and no height, the same call the arena row makes.
            _layout.startAxis = GridLayoutGroup.Axis.Vertical;
            _layout.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            _layout.constraintCount = 1;
            float h = height - _layout.padding.top - _layout.padding.bottom;
            _layout.cellSize = new Vector2(h / tileAspect, h);
            if (scroll != null) { scroll.horizontal = true; scroll.vertical = false; }
            return;
        }

        _layout.startAxis = GridLayoutGroup.Axis.Horizontal;
        if (scroll != null) { scroll.horizontal = false; scroll.vertical = false; }
        UIGrid.Fit(_layout, box, _tiles.Count, tileAspect, 200f, Mathf.Max(1, gridColumns));
    }

    void Report(string message)
    {
        if (statusText != null) statusText.text = message;

        if (!string.IsNullOrEmpty(message)) Debug.LogWarning($"[LevelSelect] {message}", this);
    }
}
