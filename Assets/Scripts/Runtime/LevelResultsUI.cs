using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The screen at the end of a level: how it went, the stars it earned, and the three
/// things the player can do next.
///
/// It is the whole reward loop, so it gets its own component rather than another
/// hundred lines in <see cref="HUDController"/>. The stars land one at a time with a
/// note each, which is the entire point of a star rating -- awarded silently and all
/// at once they are a number, and a number is not a thing anybody replays a level for.
///
/// <b>What it looks like is <see cref="ResultsView"/></b>, built from the UI kit when this
/// wakes and pointed at by the fields below, so the logic here -- keys, audio, stars landing
/// in turn, where each button goes -- is unchanged and the old builder-made panel stays
/// hidden in the scene. The view adds what a star count alone never said: each star's
/// condition, met or missed; time against the limit; the XP this level earned; and the
/// achievements it moved.
///
/// Everything here runs on unscaled time. The level ends with Time.timeScale at zero,
/// so an animation driven by the scaled clock would never move at all.
/// </summary>
[DisallowMultipleComponent]
public class LevelResultsUI : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Found in the scene when empty.")]
    public LevelManager levelManager;

    [Tooltip("The root that is switched on when a level ends. Hidden at Awake.")]
    public GameObject panel;

    [Header("Text")]
    public TMP_Text titleText;
    public TMP_Text summaryText;
    public TMP_Text detailText;
    public TMP_Text hintText;

    [Header("Stars")]
    [Tooltip("Three of them, left to right. Filled one at a time as the result is read out.")]
    public Image[] stars;

    public Color starEarnedColor = new Color(1f, 0.729f, 0.247f);
    public Color starMissedColor = new Color(1f, 1f, 1f, 0.18f);

    [Tooltip("Seconds before the first star lands, so the panel is on screen and read " +
             "before anything starts moving on it.")]
    [Min(0f)] public float starDelay = 0.45f;

    [Tooltip("Seconds between stars.")]
    [Min(0.05f)] public float starInterval = 0.42f;

    [Tooltip("How much bigger a star is at the moment it lands. It settles back to " +
             "normal over the interval.")]
    [Min(1f)] public float starPunch = 1.7f;

    [Header("Buttons")]
    public Button retryButton;

    [Tooltip("Only offered when the level was passed and there is a level after it.")]
    public Button nextButton;

    public Button dashboardButton;

    [Header("Keys")]
    [Tooltip("Takes the next level. The other two keys are the director's own resume and " +
             "quit keys, so this screen can never advertise a key that has been rebound " +
             "out from under it.")]
    public KeyCode nextKey = KeyCode.Space;

    [Tooltip("Second key for the same thing, because Return is what a menu looks like it " +
             "should take.")]
    public KeyCode altNextKey = KeyCode.Return;

    [Header("Audio")]
    public AudioSource audioSource;

    [Tooltip("Played on a pass. The three-star flourish the player is actually chasing.")]
    public AudioClip clearedClip;

    [Tooltip("Played when the level was failed and stays locked.")]
    public AudioClip failedClip;

    [Tooltip("One note per star as it lands. Pitched up for each, so three stars is a " +
             "rising figure rather than the same blip three times.")]
    public AudioClip starClip;

    [Range(0f, 1f)] public float volume = 0.8f;

    // ======================================================================
    LevelResult _result;
    bool _shown;
    int _starsShown;
    float _nextStarAt;
    bool _nextAvailable;

    readonly List<Vector3> _starRest = new List<Vector3>();

    ResultsView _view;
    bool _missedMarked;

    // What the level started from, so the screen can say what this level changed.
    bool _snapshotTaken;
    int _starsBefore;
    int _xpBefore;
    int _earnedBefore;
    float _bestTimeBefore;
    readonly Dictionary<string, int> _progressBefore = new Dictionary<string, int>();

    void Awake()
    {
        HideAtLoad(panel, this);
        AdoptView();

        if (stars != null)
        {
            foreach (var star in stars)
            {
                if (star == null) continue;

                _starRest.Add(star.rectTransform.localScale);
                star.color = starMissedColor;
            }
        }
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

    /// <summary>
    /// Builds the kit view on this canvas and points the fields at it. The scene's old panel
    /// has already been hidden above, and nothing refers to it after this.
    /// </summary>
    void AdoptView()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        _view = ResultsView.Create(canvas.transform);
        panel = _view.root;
        titleText = _view.title;
        summaryText = _view.summary;
        detailText = null;
        hintText = _view.hint;
        stars = _view.starIcons;
        retryButton = _view.retryButton;
        nextButton = _view.nextButton;
        dashboardButton = _view.menuButton;

        _starRest.Clear();
        foreach (var star in stars)
            if (star != null) _starRest.Add(star.rectTransform.localScale);
    }

    /// <summary>
    /// Takes the before-picture on the level's first running frame. Not in Start: the
    /// manager decides which level and arena this is in its own Start, and the order of two
    /// Starts is not defined.
    /// </summary>
    void Snapshot()
    {
        if (_snapshotTaken || levelManager == null || string.IsNullOrEmpty(levelManager.Arena)) return;
        _snapshotTaken = true;
        _starsBefore = LevelProgress.StarsIn(levelManager.Arena, levelManager.LevelIndex);
        _bestTimeBefore = LevelProgress.BestTimeIn(levelManager.Arena, levelManager.LevelIndex);
        _xpBefore = PlayerRank.Xp;
        _earnedBefore = Achievements.EarnedCount;
        _progressBefore.Clear();
        foreach (var a in Achievements.Catalogue) _progressBefore[a.Id] = a.Progress;
    }

    void Start()
    {
        if (levelManager == null) levelManager = FindAnyObjectByType<LevelManager>();

        Bind(retryButton, Retry);
        Bind(nextButton, NextLevel);
        Bind(dashboardButton, ToDashboard);

        // Subscribed in Start rather than OnEnable because the manager is found here.
        // A level that ended before this ran is still shown: the manager keeps its
        // result, so the state is read rather than waited for.
        if (levelManager == null) return;

        levelManager.LevelFinished += Show;

        if (levelManager.IsFinished) Show(levelManager.Result);
    }

    void OnDestroy()
    {
        if (levelManager != null) levelManager.LevelFinished -= Show;
    }

    static void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    // ======================================================================
    public void Show(LevelResult result)
    {
        if (_shown) return;

        _shown = true;
        _result = result;
        _starsShown = 0;
        _nextStarAt = Time.unscaledTime + starDelay;

        _nextAvailable = result.Passed && HasNextLevel(result);

        if (titleText != null)
        {
            titleText.text = result.Title;
            titleText.color = result.Passed
                ? new Color(0.62f, 0.95f, 0.66f)
                : new Color(1f, 0.45f, 0.4f);
        }

        if (summaryText != null) summaryText.text = result.Summary;

        if (detailText != null)
        {
            // The coins are the line the player came for, so they get their own colour
            // and sit at the end where the eye lands last.
            string coins = result.coins > 0
                ? $"     <color=#FFD24A>+{Wallet.Format(result.coins)} COINS</color>"
                : "";

            detailText.text =
                $"<size=70%>{result.killed} / {result.total} KILLED     " +
                $"{result.score:N0} POINTS     " +
                $"{Mathf.CeilToInt(result.timeTaken)}s OF {Mathf.RoundToInt(result.timeLimit)}s" +
                $"{coins}</size>";
        }

        if (stars != null)
        {
            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i] == null) continue;

                stars[i].color = starMissedColor;
                stars[i].rectTransform.localScale = RestScale(i);
            }
        }

        if (retryButton != null) retryButton.gameObject.SetActive(true);
        if (nextButton != null) nextButton.gameObject.SetActive(_nextAvailable);

        if (hintText != null)
            hintText.text = _nextAvailable
                ? $"<size=80%>{Key(ReplayKey)} replay     {Key(nextKey)} next level     " +
                  $"{Key(QuitKey)} dashboard</size>"
                : $"<size=80%>{Key(ReplayKey)} try again     {Key(QuitKey)} dashboard</size>";

        if (_view != null)
        {
            Snapshot();
            _view.transform.SetAsLastSibling();
            _view.Fill(result, levelManager != null ? levelManager.Level : null, ArenaLabel(),
                       XpGained(result), _xpBefore, NewBest(result), AchievementLines(),
                       _nextAvailable,
                       // Keys mean nothing to a thumb; the three buttons say it all.
                       hintText != null && !MobileInput.Active ? hintText.text : "");
        }

        if (panel != null) panel.SetActive(true);

        Play(result.Passed ? clearedClip : failedClip);

        // Focused so Return and a gamepad reach the obvious next thing. On a pass that
        // is the next level; on a failure it is another attempt, which is the whole
        // message of the screen.
        var focus = _nextAvailable ? nextButton : retryButton;
        if (focus != null) focus.Select();
    }

    void Update()
    {
        if (!_shown)
        {
            if (levelManager != null && levelManager.IsRunning) Snapshot();
            return;
        }

        if (_view != null && !_missedMarked && _starsShown >= _result.stars && Time.unscaledTime >= _nextStarAt)
        {
            _missedMarked = true;
            _view.MarkMissed();
        }

        AnimateStars();
        ReadKeys();
    }

    /// <summary>
    /// Lands the earned stars one at a time, then settles them. The punch is applied to
    /// the star that just landed and eases out over the interval before the next.
    /// </summary>
    void AnimateStars()
    {
        if (stars == null) return;

        if (_starsShown < _result.stars && Time.unscaledTime >= _nextStarAt)
        {
            int index = _starsShown;
            _starsShown++;
            _nextStarAt = Time.unscaledTime + starInterval;

            if (index < stars.Length && stars[index] != null)
            {
                stars[index].color = starEarnedColor;
                stars[index].rectTransform.localScale = RestScale(index) * starPunch;
            }
            if (_view != null) _view.Land(index);

            // Each star a fifth higher than the last, so three of them is a rising
            // figure rather than the same note three times.
            Play(starClip, 1f + 0.2f * index);
        }

        for (int i = 0; i < stars.Length; i++)
        {
            if (stars[i] == null) continue;

            var rect = stars[i].rectTransform;
            Vector3 rest = RestScale(i);

            if ((rect.localScale - rest).sqrMagnitude < 0.0001f) continue;

            rect.localScale = Vector3.MoveTowards(rect.localScale, rest,
                                                  (starPunch - 1f) / Mathf.Max(0.05f, starInterval)
                                                  * Time.unscaledDeltaTime);
        }
    }

    Vector3 RestScale(int index)
        => index < _starRest.Count ? _starRest[index] : Vector3.one;

    /// <summary>
    /// The keyboard equivalents of the three buttons.
    ///
    /// Owned here rather than by <see cref="GameDirector"/>, which used to take any of
    /// its keys as "I have read this" and go back to the dashboard. That was right when
    /// the only thing after a run was the menu; now there are three things to do and
    /// each key has to mean one of them.
    /// </summary>
    void ReadKeys()
    {
        if (Pressed(ReplayKey)) { Retry(); return; }

        if (_nextAvailable && (Pressed(nextKey) || Pressed(altNextKey)))
        {
            NextLevel();
            return;
        }

        if (Pressed(QuitKey)) ToDashboard();
    }

    static bool Pressed(KeyCode key) => GameInput.KeyPressed(key);

    /// <summary>
    /// Replay is the director's resume key and dashboard is its quit key, read live
    /// rather than hard-coded. The hint above is written from the same two, so rebinding
    /// one cannot leave this screen promising a key that no longer does anything -- which
    /// is exactly how the pause menu once ended up advertising a quit key that, in a
    /// browser, did nothing at all.
    /// </summary>
    static KeyCode ReplayKey
    {
        get
        {
            var director = GameDirector.Instance;
            return director != null ? director.resumeKey : KeyCode.R;
        }
    }

    static KeyCode QuitKey
    {
        get
        {
            var director = GameDirector.Instance;
            return director != null ? director.quitKey : KeyCode.Q;
        }
    }

    /// <summary>Short, uppercase and readable: "ESC", not "Escape".</summary>
    static string Key(KeyCode key) => key switch
    {
        KeyCode.Escape => "ESC",
        KeyCode.Return => "ENTER",
        KeyCode.Space => "SPACE",
        _ => key.ToString().ToUpperInvariant()
    };

    // ======================================================================
    /// <summary>
    /// XP this level earned, counted the way <see cref="PlayerRank.Xp"/> counts it: kills,
    /// stars that beat this level's previous best, the boss, and achievements newly earned.
    /// Worked out from the result rather than as XP-after minus XP-before, because the star
    /// total PlayerRank reads is recomputed on the dashboard, not here.
    /// </summary>
    int XpGained(LevelResult r)
    {
        if (r.ending == LevelResult.Ending.Abandoned) return r.killed * PlayerRank.XpPerKill;
        int starGain = Mathf.Max(0, r.stars - _starsBefore);
        int newAchievements = Mathf.Max(0, Achievements.EarnedCount - _earnedBefore);
        return r.killed * PlayerRank.XpPerKill + starGain * PlayerRank.XpPerStar +
               (r.bossKilled ? PlayerRank.XpPerBoss : 0) + newAchievements * PlayerRank.XpPerAchievement;
    }

    bool NewBest(LevelResult r)
        => r.ending == LevelResult.Ending.Cleared && (_bestTimeBefore <= 0f || r.timeTaken < _bestTimeBefore);

    /// <summary>Achievements this level moved: earned ones first, then the closest to done.</summary>
    List<string> AchievementLines()
    {
        var earned = new List<string>();
        var moved = new List<(float, string)>();
        foreach (var a in Achievements.Catalogue)
        {
            if (!_progressBefore.TryGetValue(a.Id, out int before) || a.Progress <= before) continue;
            if (a.Earned && before < a.Target) earned.Add($"{a.Title.ToUpperInvariant()}  -  COMPLETE");
            else if (!a.Earned) moved.Add((a.Fraction, $"{a.Title.ToUpperInvariant()}  -  {a.Progress} / {a.Target}"));
        }
        moved.Sort((x, y) => y.Item1.CompareTo(x.Item1));
        foreach (var m in moved) earned.Add(m.Item2);
        return earned;
    }

    string ArenaLabel()
    {
        var label = GameSession.SelectedArenaLabel;
        if (!string.IsNullOrEmpty(label)) return label;
        // Played straight from the editor, with no menu to have named it: the scene's name,
        // spaced at its capitals so SnowboundStation reads as Snowbound Station.
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        return System.Text.RegularExpressions.Regex.Replace(scene, "(?<=[a-z])(?=[A-Z])", " ");
    }

    public void Retry() => Load(_result.levelIndex);

    public void NextLevel()
    {
        if (!_nextAvailable) return;
        Load(_result.levelIndex + 1);
    }

    public void ToDashboard()
    {
        var director = GameDirector.Instance;

        if (director != null) director.ReturnToMenu();
        else Debug.LogWarning("[LevelResultsUI] No GameDirector, so there is nowhere to return to.", this);
    }

    /// <summary>
    /// Replays this arena at a given level. A scene reload rather than a reset, because
    /// a level is a fixed fight from a fixed starting position and rebuilding that in
    /// place means undoing every pickup, corpse and crater by hand.
    /// </summary>
    void Load(int index)
    {
        GameSession.ChooseLevel(index);

        var director = GameDirector.Instance;
        if (director != null) director.Restart();
        else Debug.LogWarning("[LevelResultsUI] No GameDirector, so the level cannot be reloaded.", this);
    }

    bool HasNextLevel(LevelResult result)
    {
        if (levelManager == null || levelManager.levels == null) return false;
        return result.levelIndex + 1 < levelManager.levels.Count;
    }

    void Play(AudioClip clip, float pitch = 1f)
    {
        if (clip == null || audioSource == null) return;

        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip, volume);
    }
}
