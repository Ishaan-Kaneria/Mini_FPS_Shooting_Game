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
/// <b>Choose, then play.</b> The ladder is a grid of compact tiles and the level chosen is
/// shown in full in the pane beside it (<see cref="LevelDetail"/>), with PLAY under it; the
/// next level to play is chosen when the screen opens, so PLAY is one press away. Clicking
/// the chosen tile again plays it too.
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

    [Tooltip("Scrolls the tiles down when the ladder is longer than the screen is tall.")]
    public ScrollRect scroll;

    [Header("Detail")]
    [Tooltip("The pane beside the grid: the chosen level in full, and PLAY.")]
    public LevelDetail detail;

    [Tooltip("Handset only: shows the zone's story in the pane, where the desktop has a panel over the grid.")]
    public FlatButton storyButton;

    [Tooltip("The smallest a tile may be across, in canvas units, before a column is dropped.")]
    [Min(60f)] public float minTileWidth = 132f;

    // ======================================================================
    readonly List<LevelButton> _tiles = new List<LevelButton>();

    GridLayoutGroup _layout;
    float _fittedWidth = -1f;
    float _fittedHeight = -1f;
    float _fittedNeed = -1f;
    int _chosen;

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
        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(Close);
        }
        if (detail != null && detail.playButton != null)
        {
            detail.playButton.onClick.RemoveAllListeners();
            detail.playButton.onClick.AddListener(() => Launch(_chosen));
        }
        if (storyButton != null)
        {
            storyButton.onClick.RemoveAllListeners();
            storyButton.onClick.AddListener(ToggleStory);
        }
    }

    void ToggleStory()
    {
        if (detail == null) return;
        detail.ShowStory(!detail.ShowingStory);
        if (storyButton != null && storyButton.label != null)
            storyButton.label.text = detail.ShowingStory ? "LEVEL" : "STORY";
    }

    void Update()
    {
        if (!IsOpen) return;

        FitGrid();

        // Escape and Q back out -- of the story first, when a phone is showing it in the pane.
        // A screen with no keyboard way out traps anyone whose pointer is not where they
        // expected it to be.
        if (!GameInput.BackPressed) return;
        if (detail != null && detail.ShowingStory) ToggleStory();
        else Close();
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
        if (scroll != null) scroll.verticalNormalizedPosition = 1f;

        // The focus starts on the level to play, so a pad's A plays it.
        var start = _chosen >= 0 && _chosen < _tiles.Count ? _tiles[_chosen] : null;
        if (start != null && start.button != null && start.button.interactable) start.button.Select();
        else if (backButton != null) backButton.Select();
        UISfx.Play(UISfx.Sound.Open);
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
            tile.Bind(index, set.At(i), key, LevelProgress.IsUnlocked(key, i), nextIsNew && i == next,
                      () => Clicked(index), Preview, Choose);

            _tiles.Add(tile);
        }
        _arenaColor = arenaColor;
        _nextIndex = nextIsNew ? next : -1;
        // The level to play next, else the last open one -- never a locked one.
        int start = Mathf.Clamp(next, 0, set.Count - 1);
        while (start > 0 && !LevelProgress.IsUnlocked(key, start)) start--;
        Choose(start);

        int earned = LevelProgress.StarsInArena(key, set.Count);
        int cleared = LevelProgress.LevelsCleared(key, set.Count);

        if (arenaStripe != null) arenaStripe.color = arenaColor;
        if (starsText != null)
            starsText.text = t.Tabular($"{earned}/{set.Count * 3}") + " " + InputPrompts.Glyph("star-filled");
        if (clearedText != null)
            clearedText.text = t.Tabular($"{cleared}/{set.Count}") + " CLEARED";

        var zone = campaign != null ? campaign.ZoneForArena(key) : null;
        bool story = zone != null && !string.IsNullOrWhiteSpace(zone.opening);
        string opening = story ? Campaign.Expand(zone.opening) : "";
        var holder = story ? campaign.HolderOf(zone) : null;
        string holderName = holder != null && !string.IsNullOrWhiteSpace(holder.displayName)
            ? holder.displayName.ToUpperInvariant()
            : "THE STORY";

        // A landscape phone has no height for a paragraph over the grid: there the story is
        // in the pane, behind the header's STORY button, and is read on the way past.
        bool handset = DeviceProfile.CurrentForm == DeviceProfile.Form.Handset;
        if (storyBlock != null) storyBlock.SetActive(story && !handset);
        if (hintText != null) hintText.text = opening;
        if (storyTitleText != null) storyTitleText.text = holderName;
        if (storyButton != null)
        {
            storyButton.gameObject.SetActive(story && handset);
            if (storyButton.label != null) storyButton.label.text = "STORY";
        }
        if (detail != null)
        {
            detail.ShowStory(false);
            if (detail.storyText != null) detail.storyText.text = opening;
            if (detail.storyTitleText != null) detail.storyTitleText.text = holderName;
        }
    }

    Color _arenaColor;
    int _nextIndex = -1;

    /// <summary>Makes a level the chosen one: its tile wears the amber border and the pane shows it.</summary>
    void Choose(int index)
    {
        if (index < 0 || index >= _tiles.Count) return;
        _chosen = index;
        for (int i = 0; i < _tiles.Count; i++) if (_tiles[i] != null) _tiles[i].Chosen = i == index;
        ShowDetail(index);
    }

    /// <summary>The pointer over a tile shows it in the pane; off it, the pane goes back to the chosen one.</summary>
    void Preview(int index, bool on) => ShowDetail(on ? index : _chosen);

    void ShowDetail(int index)
    {
        if (detail == null || Entry == null || Entry.levels == null) return;
        if (index < 0 || index >= Entry.levels.Count) return;
        string key = Entry.ProgressKey;
        if (detail.ShowingStory) detail.ShowStory(false);
        if (storyButton != null && storyButton.label != null) storyButton.label.text = "STORY";
        detail.Show(index, Entry.levels.Count, Entry.levels.At(index), key, Entry.preview, _arenaColor,
                    LevelProgress.IsUnlocked(key, index), index == _nextIndex);
    }

    /// <summary>A tile clicked: the chosen one plays, any other becomes the chosen one.</summary>
    void Clicked(int index)
    {
        if (index == _chosen) Launch(index);
        else
        {
            Choose(index);
            UISfx.Play(UISfx.Sound.Toggle, 1f, 0.6f);
        }
    }

    void Launch(int index)
    {
        if (Entry == null) return;

        if (!LevelProgress.IsUnlocked(Entry.ProgressKey, index))
        {
            Report($"Level {index + 1} is locked. Clear level {index} first.");
            UISfx.Play(UISfx.Sound.Error);
            return;
        }

        LevelChosen?.Invoke(Entry, index);
    }

    /// <summary>
    /// As many tiles across as fit at <see cref="minTileWidth"/> (eight at most on a desk,
    /// four on a phone), each as tall as its aspect or its content, whichever is more, in a
    /// grid that scrolls down once the ladder outgrows the screen.
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
        float need = UIGrid.ContentHeight(tileParent);
        if (Mathf.Abs(width - _fittedWidth) < 0.5f && Mathf.Abs(height - _fittedHeight) < 0.5f &&
            Mathf.Abs(need - _fittedNeed) < 0.5f) return;
        _fittedWidth = width;
        _fittedHeight = height;
        _fittedNeed = need;

        bool handset = DeviceProfile.CurrentForm == DeviceProfile.Form.Handset;
        float inner = width - _layout.padding.left - _layout.padding.right;
        // Three across on a phone, whatever the arithmetic says: the text floor makes a
        // unit bigger there, and two across was sixteen rows of scrolling for one ladder.
        int columns = handset ? 3
            : Mathf.Clamp(Mathf.FloorToInt((inner + _layout.spacing.x) / (minTileWidth + _layout.spacing.x)), 2, Mathf.Max(1, gridColumns));
        float cell = (inner - _layout.spacing.x * (columns - 1)) / columns;

        tileParent.anchorMin = tileParent.anchorMax = tileParent.pivot = new Vector2(0f, 1f);
        tileParent.anchoredPosition = Vector2.zero;
        _layout.startAxis = GridLayoutGroup.Axis.Horizontal;
        _layout.childAlignment = TextAnchor.UpperLeft;
        _layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        _layout.constraintCount = columns;
        _layout.cellSize = new Vector2(cell, Mathf.Max(cell * tileAspect, need));
        if (scroll != null) { scroll.horizontal = false; scroll.vertical = true; }
    }

    void Report(string message)
    {
        if (statusText != null) statusText.text = message;

        if (!string.IsNullOrEmpty(message)) Debug.LogWarning($"[LevelSelect] {message}", this);
    }
}
