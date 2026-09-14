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
    const string BestWaveKey = "FPSKit.BestWave";
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

    [Header("Input")]
    [Tooltip("Pauses and unpauses. Also frees the cursor, because a paused game " +
             "with a captured mouse is a game you cannot click out of.")]
    public KeyCode pauseKey = KeyCode.Escape;

    [Tooltip("Restarts the level. Offered on both the pause menu and the game over screen.")]
    public KeyCode restartKey = KeyCode.R;

    [Tooltip("Quits from the pause menu. Stops play mode in the editor.")]
    public KeyCode quitKey = KeyCode.Q;

    public int Score { get; private set; }
    public int Kills { get; private set; }
    public int Headshots { get; private set; }

    /// <summary>Kills chained inside the combo window. 0 or 1 means no chain yet.</summary>
    public int Combo { get; private set; }

    public float ComboMultiplier { get; private set; } = 1f;

    /// <summary>Seconds left on the chain, for a HUD timer bar. 0 when there is no chain.</summary>
    public float ComboRemaining => Combo <= 1 ? 0f : Mathf.Max(0f, _comboExpiry - Time.time);

    public bool IsPaused { get; private set; }
    public bool IsGameOver { get; private set; }
    public int FinalWave { get; private set; }

    public int BestWave => PlayerPrefs.GetInt(BestWaveKey, 0);
    public int BestScore => PlayerPrefs.GetInt(BestScoreKey, 0);

    /// <summary>True when this run beat the stored best wave. Drives the "new record" line.</summary>
    public bool BeatBestWave { get; private set; }

    public event Action<GameDirector> ScoreChanged;
    public event Action<GameDirector> ComboChanged;
    public event Action<GameDirector, bool> PauseChanged;
    public event Action<GameDirector> GameEnded;

    /// <summary>Raised per kill with the points awarded, for floating score popups.</summary>
    public event Action<EnemyArchetype, int, Vector3> Killed;

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
        // Restart works from the pause menu as well as the game over screen, because
        // the pause menu tells the player it does.
        if ((IsGameOver || IsPaused) && Input.GetKeyDown(restartKey))
        {
            Restart();
            return;
        }

        if (IsGameOver) return;

        if (IsPaused)
        {
            if (Input.GetKeyDown(quitKey)) QuitGame();
            else if (Input.GetKeyDown(pauseKey)) TogglePause();
            return;
        }

        if (Input.GetKeyDown(pauseKey)) TogglePause();

        // From one, not two. A lone kill leaves Combo at 1, and if that never expired
        // the next kill -- minutes later -- would start the chain at 2 and hand out a
        // multiplier nothing earned.
        if (Combo > 0 && Time.time >= _comboExpiry) ResetCombo();
    }

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
    public void ReportGameOver(int waveReached)
    {
        if (IsGameOver) return;

        IsGameOver = true;
        IsPaused = false;
        FinalWave = waveReached;

        BeatBestWave = waveReached > BestWave;
        if (BeatBestWave) PlayerPrefs.SetInt(BestWaveKey, waveReached);
        if (Score > BestScore) PlayerPrefs.SetInt(BestScoreKey, Score);
        PlayerPrefs.Save();

        PlayerMotor.InputEnabled = false;
        SetCursorFree(true);
        Time.timeScale = 0f;

        GameEnded?.Invoke(this);
    }

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

    public void QuitGame()
    {
        Time.timeScale = 1f;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    static void SetCursorFree(bool free)
    {
        Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = free;
    }
}
