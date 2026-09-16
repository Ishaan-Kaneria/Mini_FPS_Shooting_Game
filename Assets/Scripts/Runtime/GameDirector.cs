using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The one place that owns run state: score, kills, the combo chain, pause and
/// game over. Everything else reads it or raises events at it.
///
/// It creates itself on demand, so a scene that was never touched by the scene
/// builder still gets a working pause menu and scoreboard the moment anything
/// asks for <see cref="Instance"/>. That is what keeps the HUD, the wave manager
/// and the player from each having to own a slice of the same state.
/// </summary>
[DisallowMultipleComponent]
public class GameDirector : MonoBehaviour
{
    const string BestScoreKey = "FPSKit.BestScore";

    public static GameDirector Instance { get; private set; }

    static bool _quitting;

    /// <summary>
    /// Clears everything that outlives a play session. Domain reload is disabled in
    /// this project, so these statics survive into the next run: Instance still points
    /// at last run's destroyed director, and _quitting is left true by the
    /// OnApplicationQuit that fires when play mode exits -- which makes Ensure refuse
    /// to hand anybody a director for the rest of the session.
    ///
    /// Time.timeScale is engine state rather than a static, but it persists in exactly
    /// the same way: end a run on the game over screen and the next one starts frozen.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState()
    {
        Instance = null;
        _quitting = false;
        Time.timeScale = 1f;
    }

    [Header("Scoring")]
    [Tooltip("Added on top of the enemy's own value for a headshot kill.")]
    public int headshotBonus = 50;

    [Tooltip("Seconds you have to land the next kill before the chain resets. " +
             "Short enough that camping never builds a multiplier.")]
    public float comboWindow = 3.5f;

    [Tooltip("Every kill inside the window adds this to the multiplier.")]
    public float comboStep = 0.25f;

    public float maxComboMultiplier = 4f;

    [Header("Coins")]
    [Tooltip("Coins for one kill. The store's prices are set against this, so raising it " +
             "is the same thing as making everything cheaper.")]
    [Min(0)] public int coinsPerKill = 3;

    [Tooltip("Extra coins for a headshot, on top of the kill. This is what makes aiming " +
             "worth the time it costs against a crowd -- a headshot is already worth more " +
             "score, and the score is not what buys the next gun.")]
    [Min(0)] public int coinsPerHeadshot = 4;

    [Tooltip("Coins per point of score at the end of a level, as a divisor. At 120 a " +
             "twelve thousand point level pays a hundred coins on top of the kills.")]
    [Min(1)] public int scorePerCoin = 120;

    [Tooltip("Coins per star. The largest single payment in the game, because clearing a " +
             "level outright is what the store is meant to reward.")]
    [Min(0)] public int coinsPerStar = 60;

    [Header("Input")]
    [Tooltip("Pauses and unpauses. Also frees the cursor, because a paused game " +
             "with a captured mouse is a game you cannot click out of.")]
    public KeyCode pauseKey = KeyCode.Escape;

    [Tooltip("Second key for the same thing. Escape is the convention, but a browser " +
             "eats it to leave pointer lock, so a paused web player who presses Escape " +
             "gets their cursor back and no menu. P is the one that always arrives.")]
    public KeyCode altPauseKey = KeyCode.P;

    [Tooltip("Unpauses from the pause menu. Pause toggles too, so this is the key the " +
             "menu can advertise as meaning one thing.")]
    public KeyCode resumeKey = KeyCode.R;

    [Tooltip("Leaves the run and goes back to the dashboard, from the pause menu or the " +
             "results screen. This is a scene load, so unlike quitting the application " +
             "it works everywhere -- including in a browser, which has nowhere to quit to.")]
    public KeyCode quitKey = KeyCode.Q;

    [Tooltip("Scene the dashboard lives in. Loaded when a run ends, however it ended.")]
    public string menuScene = "Menu";

    /// <summary>
    /// Whether closing the application outright is a thing this build can do.
    ///
    /// This is no longer what the pause menu's quit key does -- that returns to the
    /// dashboard, which works everywhere. It is only consulted by the dashboard's own
    /// Exit button, which really does mean "leave", and which on the web hands over to
    /// WebDevice.Exit rather than calling Application.Quit and leaving a frozen canvas
    /// on the page with no way back but a reload.
    /// </summary>
    public static bool CanQuit =>
#if UNITY_WEBGL && !UNITY_EDITOR
        false;
#else
        true;
#endif

    public int Score { get; private set; }
    public int Kills { get; private set; }
    public int Headshots { get; private set; }

    /// <summary>
    /// Coins earned so far this level, from kills alone -- the level-end bonuses land in
    /// <see cref="ReportLevelFinished"/>. Shown live on the HUD, because a currency the
    /// player only sees on a results screen is one they never connect to what they did.
    ///
    /// Not banked until the level is scored. Banking per kill would mean a player could
    /// farm a level to the last enemy, quit, and repeat -- and the whole point of the
    /// clock is that a level has an ending.
    /// </summary>
    public int CoinsEarned { get; private set; }

    /// <summary>Kills chained inside the combo window. 0 or 1 means no chain yet.</summary>
    public int Combo { get; private set; }

    public float ComboMultiplier { get; private set; } = 1f;

    /// <summary>Seconds left on the chain, for a HUD timer bar. 0 when there is no chain.</summary>
    public float ComboRemaining => Combo <= 1 ? 0f : Mathf.Max(0f, _comboExpiry - Time.time);

    public bool IsPaused { get; private set; }

    /// <summary>
    /// True once the level has been scored. Still called "game over" because that is
    /// what it is to everything reading it -- input off, clock stopped, a panel on
    /// screen -- whether the level was cleared or lost.
    /// </summary>
    public bool IsGameOver { get; private set; }

    /// <summary>The level just scored. Default-valued until one finishes.</summary>
    public LevelResult FinalResult { get; private set; }

    public int BestScore => PlayerPrefs.GetInt(BestScoreKey, 0);

    /// <summary>True when this level beat the stored best score. Drives the record line.</summary>
    public bool BeatBestScore { get; private set; }

    public event Action<GameDirector> ScoreChanged;
    public event Action<GameDirector> ComboChanged;
    public event Action<GameDirector, bool> PauseChanged;
    public event Action<GameDirector> GameEnded;

    /// <summary>Raised per kill with the points awarded, for floating score popups.</summary>
    public event Action<EnemyArchetype, int, Vector3> Killed;

    /// <summary>Raised whenever the coin tally moves, so the HUD can pop its counter.</summary>
    public event Action<GameDirector, int> CoinsChanged;

    float _comboExpiry;

    // ======================================================================

    /// <summary>
    /// Returns the live director, creating one if the scene has none. Callers can
    /// use this without caring whether the scene was generated or hand-built.
    /// </summary>
    public static GameDirector Ensure()
    {
        if (Instance != null) return Instance;

        // Never resurrect one while the application is tearing down -- Unity logs an
        // error for objects created during shutdown.
        if (_quitting || !Application.isPlaying) return null;

        var existing = FindAnyObjectByType<GameDirector>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        var go = new GameObject("GameDirector");
        return go.AddComponent<GameDirector>();
    }

    void Awake()
    {
        // Done before the duplicate check, not after. These are cheap and idempotent,
        // and an early return that skipped them would leave a run frozen and deaf to
        // input with no obvious cause.
        _quitting = false;
        Time.timeScale = 1f;
        PlayerMotor.InputEnabled = true;

        if (Instance != null && Instance != this)
        {
            // Two directors in one scene: the first one owns the run. Only the extra
            // component goes -- the GameObject may well be carrying something else.
            Destroy(this);
            return;
        }

        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnApplicationQuit() => _quitting = true;

    void Update()
    {
        // Read once at the top, never inside a condition.
        //
        // Asking for pause consumes a queued tap from the on-screen button, and the
        // checks below are short-circuiting: written inline, a frame where the resume
        // key was also down would skip the consume and leave the tap queued to fire
        // again on the very next frame, which reads as the game pausing itself.
        bool pause = PausePressed();
        bool resume = Pressed(resumeKey);
        bool leave = Pressed(quitKey);

        // Only the quit key here. The results screen offers three different things --
        // replay, next level, dashboard -- and LevelResultsUI owns the keys for them,
        // so taking any key as "back to the menu" would steal two of the three.
        if (IsGameOver)
        {
            if (leave) ReturnToMenu();
            return;
        }

        if (IsPaused)
        {
            // Quit is checked first: it is the only one of the three that leaves, so a
            // key that happens to be bound to two things should still get you out.
            if (leave) ReturnToMenu();
            else if (resume || pause) SetPaused(false);
            return;
        }

        if (pause) SetPaused(true);

        // From one, not two. A lone kill leaves Combo at 1, and if that never expired
        // the next kill -- minutes later -- would start the chain at 2 and hand out a
        // multiplier nothing earned.
        if (Combo > 0 && Time.time >= _comboExpiry) ResetCombo();
    }

    /// <summary>
    /// A key press that still arrives while the game is frozen.
    ///
    /// Pausing sets Time.timeScale to 0, which stops FixedUpdate but not Update or
    /// Input -- so this is an ordinary GetKeyDown. It exists as a named method purely so
    /// the three call sites above read as intent rather than as plumbing.
    /// </summary>
    static bool Pressed(KeyCode key) => key != KeyCode.None && Input.GetKeyDown(key);

    /// <summary>
    /// Pause asked for by a key or by the on-screen button.
    ///
    /// The touch path matters more than it looks: a phone has no Escape key, so without
    /// it there is no way to pause, and therefore no way to leave a run or reach the
    /// dashboard at all. Consumed rather than polled, so one tap is one toggle.
    /// </summary>
    bool PausePressed() => Pressed(pauseKey) || Pressed(altPauseKey) || MobileInput.ConsumePause();

    // ======================================================================
    // Scoring
    // ======================================================================

    /// <summary>
    /// Banks a kill and returns the points awarded. The combo is the reason to keep
    /// pushing instead of retreating to a corner: it only survives if the next kill
    /// lands quickly, so the scoreboard rewards staying in the fight.
    /// </summary>
    public int RegisterKill(EnemyArchetype archetype, bool headshot, Vector3 position)
    {
        if (IsGameOver) return 0;

        int basePoints = archetype != null ? archetype.scoreValue : 100;
        if (headshot)
        {
            basePoints += headshotBonus;
            Headshots++;
        }

        Combo++;
        _comboExpiry = Time.time + comboWindow;
        ComboMultiplier = Mathf.Min(maxComboMultiplier, 1f + comboStep * (Combo - 1));

        int awarded = Mathf.RoundToInt(basePoints * ComboMultiplier);

        Kills++;
        Score += awarded;

        // Coins are flat per kill and not multiplied by the combo. The combo is the
        // scoreboard's reward for pushing; making it the wallet's as well would mean a
        // good chain is worth several minutes of ordinary play, and the store's prices
        // would have to be written for the chain rather than for the game.
        int coins = coinsPerKill + (headshot ? coinsPerHeadshot : 0);
        AddCoins(coins);

        ScoreChanged?.Invoke(this);
        ComboChanged?.Invoke(this);
        Killed?.Invoke(archetype, awarded, position);

        return awarded;
    }

    /// <summary>Flat points with no combo involvement -- wave clear bonuses and the like.</summary>
    public void AddScore(int points)
    {
        if (points == 0 || IsGameOver) return;

        Score += points;
        ScoreChanged?.Invoke(this);
    }

    /// <summary>
    /// Adds to the level's coin tally. Nothing reaches the <see cref="Wallet"/> from
    /// here -- that happens once, when the level is scored, so a level abandoned halfway
    /// pays for what was actually killed and a level replayed pays again from zero.
    /// </summary>
    public void AddCoins(int amount)
    {
        if (amount <= 0 || IsGameOver) return;

        CoinsEarned += amount;
        CoinsChanged?.Invoke(this, CoinsEarned);
    }

    /// <summary>Called when the player takes a hit. Dropping the chain is the cost of being hit.</summary>
    public void BreakCombo()
    {
        if (Combo <= 0) return;
        ResetCombo();
    }

    void ResetCombo()
    {
        Combo = 0;
        ComboMultiplier = 1f;
        _comboExpiry = 0f;
        ComboChanged?.Invoke(this);
    }

    // ======================================================================
    // Pause
    // ======================================================================
    public void TogglePause() => SetPaused(!IsPaused);

    public void SetPaused(bool paused)
    {
        if (IsGameOver || IsPaused == paused) return;

        IsPaused = paused;
        Time.timeScale = paused ? 0f : 1f;
        PlayerMotor.InputEnabled = !paused;

        SetCursorFree(paused);
        PauseChanged?.Invoke(this, paused);
    }

    // ======================================================================
    // End of run
    // ======================================================================
    /// <summary>
    /// Ends the level, however it ended.
    ///
    /// Called once by <see cref="LevelManager"/> with the scored result. Everything the
    /// end of a level means to the rest of the game is here: the clock stops, the cursor
    /// comes back, the result is banked, and whatever is showing a results screen is
    /// told to show it.
    /// </summary>
    public void ReportLevelFinished(LevelResult result)
    {
        if (IsGameOver) return;

        // The level-end bonuses, added before the result is banked so the number on the
        // results screen is the number that reached the wallet.
        CoinsEarned += result.stars * coinsPerStar + result.score / Mathf.Max(1, scorePerCoin);
        CoinsChanged?.Invoke(this, CoinsEarned);

        result.coins = CoinsEarned;

        IsGameOver = true;
        IsPaused = false;
        FinalResult = result;

        BeatBestScore = result.score > BestScore;
        if (BeatBestScore) PlayerPrefs.SetInt(BestScoreKey, result.score);
        PlayerPrefs.Save();

        PlayerMotor.InputEnabled = false;
        SetCursorFree(true);
        Time.timeScale = 0f;

        // Banked before the results screen is shown rather than on the way out of it,
        // so a player who closes the tab on the results still keeps the stars.
        GameSession.RecordResult(result);

        GameEnded?.Invoke(this);
    }

    /// <summary>
    /// Leaves the run and goes back to the dashboard.
    ///
    /// This is what the quit key does, and it is a plain scene load -- which is the
    /// point. Quitting used to mean Application.Quit, so on the web it did nothing at
    /// all and the pause menu hid the key rather than admit it: there was no screen to
    /// quit *to*. Now there is one, and the same key works in every build.
    /// </summary>
    public void ReturnToMenu()
    {
        // A level abandoned mid-fight still counts as an attempt. Reported here rather
        // than in ReportLevelFinished so the quit path is covered without
        // double-counting a scored one, which has already banked its result by now.
        if (!IsGameOver) GameSession.RecordResult(AbandonedResult());

        Time.timeScale = 1f;
        IsPaused = false;
        PlayerMotor.InputEnabled = true;
        MobileInput.Reset();
        SetCursorFree(true);

        LoadSceneByNameOrIndex(menuScene);
    }

    /// <summary>
    /// What to record for a level the player walked out of.
    ///
    /// Built from the live manager where there is one, so the dashboard can say how far
    /// they had got rather than showing an empty line. No stars, by construction: an
    /// abandoned level is not a passed one, and LevelProgress keeps the best of what it
    /// is given, so recording a zero can never take an earned star away.
    /// </summary>
    LevelResult AbandonedResult()
    {
        var level = FindAnyObjectByType<LevelManager>();

        return new LevelResult
        {
            arena = level != null ? level.Arena : GameSession.SelectedArena,
            levelIndex = level != null ? level.LevelIndex : GameSession.SelectedLevel,
            levelName = level != null ? level.LevelName : "",
            ending = LevelResult.Ending.Abandoned,
            stars = 0,
            killed = level != null ? level.Killed : Kills,
            total = level != null ? level.TotalEnemies : 0,
            scoreFraction = level != null ? level.ScoreFraction : 0f,
            bossKilled = level != null && level.BossKilled,
            hadBoss = level != null && level.HasBoss,
            timeTaken = level != null ? level.TimeLimit - level.TimeRemaining : 0f,
            timeLimit = level != null ? level.TimeLimit : 0f,
            score = Score,
            headshots = Headshots,

            // What was earned by killing, with no star or score bonus on top: walking
            // out is not a way to be paid for finishing.
            coins = CoinsEarned
        };
    }

    /// <summary>
    /// Ends the application, for the dashboard's Exit button.
    ///
    /// On the web there is no process to end, so this hands over to the page: see
    /// WebDevice.Exit, which closes the tab where the browser allows it and otherwise
    /// replaces the canvas with a sign-off rather than leaving a frozen one.
    /// </summary>
    public static void ExitApplication()
    {
        Time.timeScale = 1f;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
        WebDevice.Exit();
#else
        Application.Quit();
#endif
    }

    static void LoadSceneByNameOrIndex(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[GameDirector] No menu scene set, so there is nowhere to return to.");
            return;
        }

        // A scene that is not in Build Settings cannot be loaded, and the failure is a
        // silent black screen. Saying so is the difference between a five-second fix
        // and an afternoon.
        if (Application.CanStreamedLevelBeLoaded(sceneName))
        {
            SceneManager.LoadScene(sceneName);
            return;
        }

        Debug.LogError($"[GameDirector] Scene \"{sceneName}\" is not in Build Settings, so the " +
                       "dashboard cannot be loaded. Run FPSKit > Build Dashboard.");
    }

    /// <summary>
    /// Reloads the arena in place, at whatever level GameSession now names. This is
    /// what the results screen's Replay and Next Level do: same scene, different level,
    /// no trip through the dashboard.
    /// </summary>
    public void Restart()
    {
        Time.timeScale = 1f;
        PlayerMotor.InputEnabled = true;
        MobileInput.Reset();

        var scene = SceneManager.GetActiveScene();

        // buildIndex is -1 when the scene is not in Build Settings.
        if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
        else SceneManager.LoadScene(scene.name);
    }

    static void SetCursorFree(bool free)
    {
        Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = free;
    }
}
