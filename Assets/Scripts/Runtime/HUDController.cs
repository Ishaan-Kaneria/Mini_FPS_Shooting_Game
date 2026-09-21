using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives every HUD element: ammo, the two-layer health bar, the level's clock and
/// kill count, banners, score and combo, a boss bar, a crosshair that opens with
/// weapon spread, directional damage arrows and the pause menu.
///
/// The screen at the *end* of a level is not here -- that is
/// <see cref="LevelResultsUI"/>, which owns the stars, the three things to do next
/// and the keys for them. This is the HUD for a level in progress.
///
/// All references are optional -- a missing one is simply skipped, so a HUD
/// assembled by hand can show as little or as much as it likes.
/// </summary>
public class HUDController : MonoBehaviour
{
    [Header("Sources")]
    public Weapon weapon;
    public Health playerHealth;
    public LevelManager levelManager;

    [Tooltip("Found on the player when empty. Drives the bomb counter and the range " +
             "readout; the whole block hides when the player owns no bomb.")]
    public BombThrower bombs;

    [Tooltip("Found on the player when empty. Drives the belt counter and the rush bar.")]
    public ConsumableBelt belt;

    [Header("Text")]
    public TMP_Text ammoText;
    public TMP_Text healthText;

    [Tooltip("The level's name and number, top centre.")]
    public TMP_Text levelText;

    [Tooltip("Kills against the level's target, and the clock. Both, in one label: the " +
             "only two numbers that decide the outcome should not be on opposite sides " +
             "of the screen from each other.")]
    public TMP_Text objectiveText;

    [Tooltip("The briefing countdown before the clock starts.")]
    public TMP_Text briefingText;

    public TMP_Text scoreText;
    public TMP_Text comboText;

    [Tooltip("Coins earned this level. Live rather than only on the results screen -- a " +
             "currency the player never sees themselves earning is one they never " +
             "connect to what they did.")]
    public TMP_Text coinText;

    [Header("Health Bars")]
    public Image healthFill;
    public Image shieldFill;

    [Tooltip("Pale bar that trails behind the real one. Seeing the gap is how you read " +
             "how much a hit actually cost, at a glance, mid-fight.")]
    public Image healthTrailFill;

    public float trailCatchUpDelay = 0.4f;
    public float trailCatchUpSpeed = 0.8f;

    [Header("Objective Bar")]
    [Tooltip("Fills with the weighted fraction of the level that is dead -- the number " +
             "the stars are cut from. Optional.")]
    public Image objectiveFill;

    [Tooltip("Ticks drawn on the bar at the one- and two-star thresholds, so the player " +
             "can see what the next star costs while there is still time to go and get " +
             "it. Order: one star, two stars.")]
    public RectTransform[] starMarkers;

    [Tooltip("The bar turns this colour once the level is cleared outright.")]
    public Color objectiveClearColor = new Color(0.55f, 0.95f, 0.65f);

    public Color objectiveColor = new Color(0.95f, 0.78f, 0.3f);

    [Header("Equipment")]
    [Tooltip("The bomb counter and its key hint. Hidden entirely when there is no bomb.")]
    public GameObject bombPanel;

    public TMP_Text bombText;

    [Tooltip("The range readout shown while a bomb is being aimed -- how far away the " +
             "ring is, and whether the distance has been locked.")]
    public TMP_Text bombRangeText;

    [Tooltip("The belt counter. Hidden when the store sells no consumable.")]
    public GameObject beltPanel;

    public TMP_Text beltText;

    [Tooltip("Drains across an energy drink's rush, so the player can see it running out.")]
    public Image boostFill;

    public Color equipmentReadyColor = new Color(0.92f, 0.91f, 0.89f);

    [Tooltip("Flashed when a key is pressed with nothing to spend. An equipment key " +
             "that silently does nothing reads as a broken key.")]
    public Color equipmentEmptyColor = new Color(1f, 0.4f, 0.35f);

    [Header("Bomb Cursor")]
    [Tooltip("The reticle drawn while a bomb is being aimed, which the mouse moves. " +
             "Built on demand when empty, so an existing or imported scene gets one " +
             "without being rebuilt.")]
    public RectTransform bombCursor;

    [Tooltip("Pixels across. Bigger than the crosshair on purpose -- it is a thing " +
             "being moved to a place, not a thing being held on a target.")]
    [Min(8f)] public float bombCursorSize = 34f;

    public Color bombCursorColor = new Color(1f, 0.72f, 0.2f);

    [Tooltip("Shown when the throw cannot be made, so the reticle and the ring on the " +
             "ground always agree about whether the bomb can go there.")]
    public Color bombCursorBlockedColor = new Color(1f, 0.3f, 0.25f);

    [Header("Crosshair")]
    public CanvasGroup crosshairGroup;

    [Tooltip("Order matters: Top, Bottom, Left, Right.")]
    public RectTransform[] crosshairArms;

    public float crosshairBaseGap = 6f;
    public float crosshairKick = 4f;
    public float crosshairSmoothing = 14f;
    public Color crosshairColor = Color.white;
    public Color hitmarkerColor = new Color(1f, 0.35f, 0.3f);
    public Color headshotColor = new Color(1f, 0.85f, 0.2f);
    public float hitmarkerDuration = 0.12f;

    [Header("Damage Feedback")]
    public Image damageVignette;
    public float vignetteFadeSpeed = 1.6f;
    public float lowHealthThreshold = 0.35f;

    [Tooltip("Parent for the directional hit arrows, anchored to the screen centre.")]
    public RectTransform damageIndicatorRoot;

    [Tooltip("One arrow, pivoting on the screen centre. Rotating it points it at whatever " +
             "hit you -- the single most useful thing a HUD can tell you in a fight " +
             "where enemies come from behind.")]
    public CanvasGroup damageIndicatorPrefab;

    public float indicatorDuration = 1.2f;

    [Header("Banner")]
    public CanvasGroup bannerGroup;
    public TMP_Text bannerTitle;
    public TMP_Text bannerSubtitle;
    public float bannerHold = 2.2f;
    public float bannerFadeSpeed = 3.5f;

    [Tooltip("Optional. When set, the rifle the level handed over is announced on the " +
             "banner after the level title has had its moment.")]
    public PlayerProgression progression;

    public Color upgradeBannerColor = new Color(0.55f, 0.95f, 0.65f);
    public Color bossBannerColor = new Color(1f, 0.3f, 0.3f);

    [Tooltip("Seconds left when the clock starts flashing. A strict clock has to be " +
             "loud about running out or it reads as unfair rather than as tight.")]
    [Min(0f)] public float clockWarningTime = 10f;

    public Color clockWarningColor = new Color(1f, 0.35f, 0.3f);

    [Header("Boss Bar")]
    public GameObject bossPanel;
    public TMP_Text bossNameText;
    public Image bossFill;

    [Header("Pause")]
    public GameObject pausePanel;
    public TMP_Text pauseHintText;

    [Header("Pause Buttons")]
    [Tooltip("On-screen equivalents of the resume and quit keys. Not decoration: a phone " +
             "has no R or Q, so without these a touch player can reach the pause menu " +
             "and then has no way out of it.")]
    public Button resumeButton;

    public Button quitButton;

    [Header("Instruction Strip")]
    [Tooltip("The thin bar across the top that states the keys. Optional -- the HUD " +
             "works without one -- but a run that never tells the player how to pause " +
             "is one they leave by closing the tab.")]
    public TMP_Text instructionText;

    float _currentGap;
    float _kick;
    float _vignetteAlpha;
    float _hitmarkerUntil;
    Color _hitmarkerTint;
    Camera _camera;

    /// <summary>The player's live bindings, resolved once. See PlayerControls.</summary>
    ControlSettings _controls;
    GameDirector _director;

    /// <summary>
    /// The live director, or a genuine null. Everything goes through this rather than
    /// using ?. on the field directly: the null-conditional operator does a plain
    /// reference check and skips Unity's overloaded ==, so on a destroyed object it
    /// sails past the guard and calls the method anyway -- a MissingReferenceException
    /// in the console instead of the quiet no-op the ? was written to express.
    /// </summary>
    GameDirector Director => _director != null ? _director : null;

    float _trail = 1f;
    float _trailHoldUntil;
    float _bannerUntil;

    /// <summary>
    /// One queued banner, shown once the current one has finished.
    ///
    /// Starting a level produces two things worth saying -- which level this is, and
    /// what the rifle became -- and both are raised on the same frame. Shown immediately the
    /// second would overwrite the first before it had been read; queued, they land as
    /// two beats of the same moment. One slot rather than a list, because a third thing
    /// to announce in the same breath is a design problem, not a queueing problem.
    /// </summary>
    bool _hasPendingBanner;
    string _pendingTitle, _pendingSubtitle;
    Color _pendingTint;

    readonly List<Indicator> _indicators = new List<Indicator>();

    /// <summary>Cached crosshair arm Images. See CrosshairImages.</summary>
    Image[] _crosshairImages;

    struct Indicator
    {
        public CanvasGroup group;
        public float expiry;
    }

    // ======================================================================
    void Awake()
    {
        // Ensure rather than Find: this is what guarantees pause and scoring work in a
        // scene the builder never touched. Done in Awake so OnEnable can subscribe.
        _director = GameDirector.Ensure();

        // Found here for the same reason, and not in Start: OnEnable runs between the
        // two and is where these are subscribed to, so a Start-time lookup would find
        // them a frame after the only chance to hook their events.
        if (bombs == null || belt == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");

            if (player != null)
            {
                if (bombs == null) bombs = player.GetComponentInChildren<BombThrower>();
                if (belt == null) belt = player.GetComponentInChildren<ConsumableBelt>();
            }
        }

        _currentGap = crosshairBaseGap;
        _hitmarkerTint = crosshairColor;

        if (pausePanel != null) pausePanel.SetActive(false);
        if (bossPanel != null) bossPanel.SetActive(false);
        if (damageVignette != null) damageVignette.color = WithAlpha(damageVignette.color, 0f);
        if (bannerGroup != null) bannerGroup.alpha = 0f;

        // The template is a source object, not something that should render.
        if (damageIndicatorPrefab != null) damageIndicatorPrefab.gameObject.SetActive(false);
    }

    void Start()
    {
        // The HUD is authored against the same 1920x1080 reference as the menus, so the
        // ammo counter and the score read at about 1.6mm on a phone -- unreadable at the
        // exact moment the player has no attention to spare for them. The touch cluster
        // is unaffected: it sizes itself in millimetres and does not care what the canvas
        // is measured against.
        PhoneUI.Apply(GetComponentInParent<Canvas>());

        WriteKeyHints();
        WireButtons();

        // A phone has no Escape key, and a strip listing three of them is three lines of
        // nonsense over the top of a small screen. TouchControls makes the same call for
        // the on-screen sticks; see WebDevice.IsTouchOnly for why it is not
        // Application.isMobilePlatform.
        if (instructionText != null && (WebDevice.IsTouchOnly || Application.isMobilePlatform))
        {
            var strip = instructionText.transform.parent;
            if (strip != null) strip.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Writes the key hints, in both places they appear.
    ///
    /// Read off the live director rather than hard-coded, so rebinding a key in the
    /// Inspector cannot leave the strip advertising one that no longer does anything --
    /// which is how the pause menu ended up promising a quit key that, in a browser,
    /// did nothing at all.
    /// </summary>
    void WriteKeyHints()
    {
        var director = Director;

        KeyCode pause = director != null ? director.pauseKey : KeyCode.Escape;
        KeyCode altPause = director != null ? director.altPauseKey : KeyCode.P;
        KeyCode resume = director != null ? director.resumeKey : KeyCode.R;
        KeyCode quit = director != null ? director.quitKey : KeyCode.Q;

        string pauseLabel = altPause == KeyCode.None || altPause == pause
            ? Key(pause)
            : $"{Key(pause)}/{Key(altPause)}";

        // <color>, not <alpha>. TMP's alpha tag applies from where it appears and has no
        // closing form, so "</alpha>" is not a tag -- it renders as those eight
        // characters, on screen, three times across the strip.
        const string Dim = "<color=#B8AFA0>";

        // The equipment keys are only advertised when the player actually has that
        // equipment. A strip that lists a bomb key for somebody who owns no bomb is
        // three words of screen spent telling them about a control that does nothing.
        string equipment = "";

        if (bombs != null && bombs.data != null)
            equipment += $"{Key(BombKey())} {Dim}BOMB</color>     ";

        // "HEAL" rather than "DRINK": the strip has room for one word per key and it
        // should be the one that says what the key is *for*.
        if (belt != null && belt.data != null)
            equipment += $"{Key(ItemKey())} {Dim}HEAL</color>     ";

        if (instructionText != null)
            instructionText.text =
                equipment +
                $"{pauseLabel} {Dim}PAUSE</color>     " +
                $"{Key(resume)} {Dim}RESUME</color>     " +
                $"{Key(quit)} {Dim}QUIT</color>";

        if (pauseHintText != null)
            pauseHintText.text =
                $"<size=60%>{Key(resume)} resume     {pauseLabel} resume     " +
                $"{Key(quit)} quit to dashboard</size>";
    }

    /// <summary>
    /// Points the on-screen pause buttons at the director.
    ///
    /// Wired here rather than in the builder because the director is found at runtime --
    /// a scene can be played without one until something asks for it.
    /// </summary>
    void WireButtons()
    {
        Bind(resumeButton, () => Director?.SetPaused(false));
        Bind(quitButton, () => Director?.ReturnToMenu());
    }

    static void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    /// <summary>
    /// Short, uppercase and readable: "ESC", not "Escape", and "RMB" rather than
    /// "MOUSE1" -- which is what KeyCode.ToString gives and what nobody calls it.
    /// </summary>
    static string Key(KeyCode key) => UIText.KeyLabel(key);

    void OnEnable()
    {
        if (weapon != null)
        {
            weapon.Fired += OnWeaponFired;
            weapon.DealtDamage += OnDealtDamage;
        }

        if (playerHealth != null) playerHealth.Damaged += OnPlayerDamaged;

        if (levelManager != null) levelManager.LevelStarted += OnLevelStarted;

        if (bombs != null) bombs.Denied += OnBombDenied;
        if (belt != null) belt.Denied += OnBeltDenied;

        if (progression != null) progression.Upgraded += OnUpgraded;

        // Subscribed here rather than in Start, alongside every other source. Started in
        // Start but cancelled in OnDisable, the director's events were gone for good the
        // first time this object was toggled off and on again.
        if (_director != null)
        {
            _director.GameEnded += OnGameEnded;
            _director.PauseChanged += OnPauseChanged;
        }
    }

    void OnDisable()
    {
        if (weapon != null)
        {
            weapon.Fired -= OnWeaponFired;
            weapon.DealtDamage -= OnDealtDamage;
        }

        if (playerHealth != null) playerHealth.Damaged -= OnPlayerDamaged;

        if (levelManager != null) levelManager.LevelStarted -= OnLevelStarted;

        if (bombs != null) bombs.Denied -= OnBombDenied;
        if (belt != null) belt.Denied -= OnBeltDenied;

        if (progression != null) progression.Upgraded -= OnUpgraded;

        if (_director != null)
        {
            _director.GameEnded -= OnGameEnded;
            _director.PauseChanged -= OnPauseChanged;
        }
    }

    // ======================================================================
    void Update()
    {
        UpdateTexts();
        UpdateHealthBars();
        UpdateCrosshair();
        UpdateBombCursor();
        UpdateVignette();
        UpdateBanner();
        UpdateBossBar();
        UpdateObjectiveBar();
        UpdateEquipment();
        UpdateIndicators();
    }

    // ==================================================================
    // Text
    //
    // UpdateTexts runs every frame, and every label below used to rebuild an
    // interpolated string on each one -- up to seven allocations a frame, nearly all
    // identical to the frame before, since ammo only moves when you fire and the level
    // number never moves at all. At 60fps that is several hundred short-lived strings a
    // second to display a HUD that changes a handful of times a minute.
    //
    // So each label is keyed on the values its string is built from. Every one of them
    // is an integer, or a float that is rounded to an integer before it is shown, so the
    // key is exact: if the key has not moved, the string would have been identical.
    // Comparing finished strings instead would not help, because building them is the
    // cost being avoided.
    //
    // Hidden marks a label that should be showing nothing, and is distinct from Unset,
    // which no real key can equal -- so every label writes once on the first frame
    // rather than trusting whatever text it was created with.
    // ==================================================================

    const int Unset = int.MinValue;
    const int Hidden = -1;

    enum AmmoDisplay { Unset, Counted, Reloading, InfiniteMagazine, InfiniteReserve }

    AmmoDisplay _shownAmmoMode = AmmoDisplay.Unset;
    int _shownAmmo = Unset, _shownReserve = Unset;
    int _shownHealth = Unset, _shownShield = Unset;
    int _shownScore = Unset;
    int _shownCombo = Unset, _shownMultiplier = Unset;
    int _shownLevel = Unset;
    int _shownKilled = Unset, _shownClock = Unset;
    int _shownBriefing = Unset;
    int _shownCoins = Unset;
    int _shownBombs = Unset, _shownBeltCount = Unset;
    int _shownRange = Unset;

    /// <summary>When the equipment counters stop being flashed red. Unscaled.</summary>
    float _bombDeniedUntil, _beltDeniedUntil;

    RectTransform _cachedBombCursor, _bombCursorParent;
    Canvas _bombCursorCanvas;
    Image[] _bombCursorImages;
    int _shownBombCursorValid = Unset;

    void UpdateTexts()
    {
        if (ammoText != null && weapon != null) UpdateAmmoText();
        if (healthText != null && playerHealth != null) UpdateHealthText();

        if (_director != null)
        {
            UpdateScoreText();
            UpdateComboText();
            UpdateCoinText();
        }

        if (levelManager == null) return;

        UpdateLevelText();
        UpdateObjectiveText();
        UpdateBriefingText();
    }

    void UpdateAmmoText()
    {
        var data = weapon.Data;

        AmmoDisplay mode = weapon.IsReloading ? AmmoDisplay.Reloading
                         : data != null && data.infiniteAmmo ? AmmoDisplay.InfiniteMagazine
                         : data != null && data.infiniteReserve ? AmmoDisplay.InfiniteReserve
                         : AmmoDisplay.Counted;

        int ammo = weapon.CurrentAmmo;
        int reserve = weapon.ReserveAmmo;

        if (mode == _shownAmmoMode && ammo == _shownAmmo && reserve == _shownReserve) return;

        _shownAmmoMode = mode;
        _shownAmmo = ammo;
        _shownReserve = reserve;

        switch (mode)
        {
            case AmmoDisplay.Reloading: ammoText.text = "RELOADING"; break;
            case AmmoDisplay.InfiniteMagazine: ammoText.text = "∞"; break;
            case AmmoDisplay.InfiniteReserve: ammoText.text = $"{ammo} / ∞"; break;
            default: ammoText.text = $"{ammo} / {reserve}"; break;
        }
    }

    void UpdateHealthText()
    {
        int health = Mathf.CeilToInt(playerHealth.Current);

        // Shield folds into the key as zero when there is none, so gaining or losing it
        // is a key change like any other.
        int shield = playerHealth.Shield > 0.5f ? Mathf.CeilToInt(playerHealth.Shield) : 0;

        if (health == _shownHealth && shield == _shownShield) return;

        _shownHealth = health;
        _shownShield = shield;

        healthText.text = shield > 0
            ? $"{health} <size=60%><color=#66BFFF>+{shield}</color></size>"
            : health.ToString();
    }

    void UpdateScoreText()
    {
        if (scoreText == null || _director.Score == _shownScore) return;

        _shownScore = _director.Score;
        scoreText.text = _shownScore.ToString("N0");
    }

    void UpdateCoinText()
    {
        if (coinText == null || _director.CoinsEarned == _shownCoins) return;

        _shownCoins = _director.CoinsEarned;
        coinText.text = _shownCoins > 0 ? $"+{Wallet.Format(_shownCoins)} <size=62%>COINS</size>" : "";
    }

    void UpdateComboText()
    {
        if (comboText == null) return;

        int combo = _director.Combo;

        // Keyed at the resolution it is displayed at. Keying on the raw float would
        // rebuild the label for changes too small to see.
        int multiplier = Mathf.RoundToInt(_director.ComboMultiplier * 100f);

        if (combo == _shownCombo && multiplier == _shownMultiplier) return;

        _shownCombo = combo;
        _shownMultiplier = multiplier;

        comboText.text = combo > 1
            ? $"<size=70%>x</size>{_director.ComboMultiplier:0.##}  <size=55%>{combo} CHAIN</size>"
            : string.Empty;
    }

    void UpdateLevelText()
    {
        if (levelText == null || levelManager.LevelNumber == _shownLevel) return;

        _shownLevel = levelManager.LevelNumber;

        string name = levelManager.LevelName;
        levelText.text = string.IsNullOrEmpty(name)
            ? $"LEVEL {_shownLevel}"
            : $"LEVEL {_shownLevel}  <size=70%><color=#B8AFA0>{name.ToUpperInvariant()}</color></size>";
    }

    /// <summary>
    /// Kills against the target, and the clock. Both numbers decide the outcome, and
    /// the clock is now the level's own rule rather than a safety net -- so it is
    /// always on screen while the level is running, and it says so in red when it is
    /// nearly gone.
    /// </summary>
    void UpdateObjectiveText()
    {
        if (objectiveText == null) return;

        bool running = levelManager.IsRunning && !levelManager.IsFinished;

        int killed = running ? levelManager.Killed : Hidden;
        int clock = running ? Mathf.CeilToInt(levelManager.TimeRemaining) : Hidden;

        if (killed == _shownKilled && clock == _shownClock) return;

        _shownKilled = killed;
        _shownClock = clock;

        if (!running)
        {
            objectiveText.text = string.Empty;
            return;
        }

        string time = $"{clock / 60}:{clock % 60:00}";

        if (levelManager.TimeRemaining <= clockWarningTime)
            time = $"<color=#{ColorUtility.ToHtmlStringRGB(clockWarningColor)}>{time}</color>";

        objectiveText.text = $"{killed} / {levelManager.TotalEnemies} KILLED   <size=110%>{time}</size>";
    }

    void UpdateBriefingText()
    {
        if (briefingText == null) return;

        bool counting = levelManager.IsBriefing && !levelManager.IsFinished;
        int seconds = counting ? Mathf.CeilToInt(levelManager.BriefingRemaining) : Hidden;

        // The equipment hint is part of this label, so everything it depends on is part
        // of the key -- the counts included, because the hint prints them and a player
        // can drink during the briefing. Keying on the seconds alone would leave the
        // number stale until the countdown next ticked over.
        int charges = bombs != null && bombs.data != null ? Mathf.Clamp(bombs.charges, 0, 31) : 0;
        int drinks = belt != null && belt.data != null ? Mathf.Clamp(belt.Count, 0, 31) : 0;

        int keyed = counting ? seconds * 1024 + charges * 32 + drinks : Hidden;

        if (keyed == _shownBriefing) return;

        _shownBriefing = keyed;

        if (!counting)
        {
            briefingText.text = string.Empty;
            return;
        }

        string brief = levelManager.LevelBrief;

        briefingText.text = string.IsNullOrWhiteSpace(brief)
            ? $"GET READY - {seconds}{EquipmentHint()}"
            : $"{brief}\n<size=70%>GET READY - {seconds}</size>{EquipmentHint()}";
    }

    /// <summary>
    /// How to use whatever the player brought with them, said once at the start of the
    /// level while there is nothing else happening.
    ///
    /// The top strip names the keys for the whole level, but a key listed in a strip is
    /// something you notice on your third run. A bomb is the least guessable control in
    /// the game -- it is *held*, not tapped, and the release is the throw -- so it is
    /// worth a sentence at the one moment the player has time to read one.
    ///
    /// Only for equipment actually being carried. Teaching somebody a bomb key when they
    /// own no bomb is worse than saying nothing: they try it, nothing happens, and now
    /// they distrust the rest of the strip.
    /// </summary>
    string EquipmentHint()
    {
        bool hasBomb = bombs != null && bombs.data != null && bombs.charges > 0;
        bool hasDrink = belt != null && belt.data != null && belt.Count > 0;

        if (!hasBomb && !hasDrink) return string.Empty;

        const string Dim = "<color=#B8AFA0>";
        var hint = new System.Text.StringBuilder("\n<size=58%>");

        // How the bomb is *placed* is named too, and it is named differently for the
        // two ways of placing it. The key teaches itself the moment a ring appears on
        // the floor; what does not is whether the thing that moves it is the mouse or
        // your head, and a player who guesses wrong concludes the control is broken.
        if (hasBomb)
        {
            string place = bombs.UsingCursor
                ? "TO AIM A BOMB, MOVE THE MOUSE TO PLACE IT"
                : "TO AIM A BOMB, LOOK DOWN TO BRING THE RING IN";

            // Both ways of ending it are named. The tap is the one a laptop player
            // needs -- a held key stops their touchpad reporting motion at all -- and
            // it is the one nobody discovers on their own.
            hint.Append($"TAP {Key(BombKey())} {Dim}{place}, TAP AGAIN TO THROW</color>" +
                        $"\n<size=90%>{Dim}OR HOLD </color>{Key(BombKey())}" +
                        $"{Dim} AND RELEASE, {Key(AimKey())} LOCKS THE RANGE</color></size>");
        }

        if (hasBomb && hasDrink) hint.Append("\n");

        // Named, and with what it gives back, because "drinks an energy drink" tells a
        // player nothing they could not guess from the word on the button -- what they
        // need to know at three hit points is that this is the key that heals them.
        if (hasDrink)
        {
            string name = string.IsNullOrWhiteSpace(belt.data.displayName)
                ? "A DRINK"
                : belt.data.displayName.ToUpperInvariant();

            hint.Append($"{Key(ItemKey())} {Dim}DRINKS {name} FOR {Restores(belt.data)}, " +
                        $"{UIText.Count(belt.Count)} LEFT</color>");
        }

        hint.Append("</size>");
        return hint.ToString();
    }

    /// <summary>
    /// What a consumable gives back, in three words, read off the asset rather than
    /// written out -- so a drink retuned to restore no shield stops claiming it does.
    /// </summary>
    static string Restores(ConsumableData item)
    {
        bool health = item.healthRestore > 0f;
        bool shield = item.shieldRestore > 0f;

        if (health && shield) return "HEALTH AND SHIELD";
        if (shield) return "SHIELD";
        if (health) return "HEALTH";

        return "A SECOND WIND";
    }

    /// <summary>
    /// The bar that says how close the level is to being cleared, with the star
    /// thresholds ticked on it. Without the ticks the bar is decoration; with them it
    /// answers the only question a player has with twenty seconds left, which is
    /// whether one more kill is worth chasing.
    /// </summary>
    void UpdateObjectiveBar()
    {
        if (levelManager == null) return;

        if (objectiveFill != null)
        {
            objectiveFill.fillAmount = levelManager.ScoreFraction;
            objectiveFill.color = levelManager.ScoreFraction >= 0.999f
                ? objectiveClearColor
                : objectiveColor;
        }

        if (starMarkers == null || starMarkers.Length < 2) return;

        var level = levelManager.Level;
        if (level == null) return;

        PlaceMarker(starMarkers[0], level.oneStarScore);
        PlaceMarker(starMarkers[1], level.twoStarScore);
    }

    /// <summary>
    /// Puts one threshold tick at its fraction along the bar. Anchored rather than
    /// offset in pixels, so it stays put whatever width the bar ends up.
    /// </summary>
    static void PlaceMarker(RectTransform marker, float fraction)
    {
        if (marker == null) return;

        float x = Mathf.Clamp01(fraction);

        marker.anchorMin = new Vector2(x, marker.anchorMin.y);
        marker.anchorMax = new Vector2(x, marker.anchorMax.y);
        marker.anchoredPosition = new Vector2(0f, marker.anchoredPosition.y);
    }

    /// <summary>
    /// The two counters down the side: bombs in hand and drinks on the belt.
    ///
    /// Both hide their whole panel rather than showing a zero when the player has none
    /// of that kind of thing at all. A counter reading "0" says "you have run out"; a
    /// player who never bought a bomb has not run out of anything, and offering them a
    /// key hint for one is offering a control that does nothing.
    /// </summary>
    void UpdateEquipment()
    {
        UpdateBombs();
        UpdateBelt();
    }

    void UpdateBombs()
    {
        bool has = bombs != null && bombs.data != null;

        if (bombPanel != null && bombPanel.activeSelf != has) bombPanel.SetActive(has);
        if (!has) return;

        if (bombText != null && bombs.charges != _shownBombs)
        {
            _shownBombs = bombs.charges;
            bombText.text = $"{Key(BombKey())} <size=130%>x{_shownBombs}</size>";
        }

        if (bombText != null)
            bombText.color = Time.unscaledTime < _bombDeniedUntil
                ? equipmentEmptyColor
                : equipmentReadyColor;

        if (bombRangeText == null) return;

        // Keyed at the resolution it is drawn at, like every other label here. The two
        // flags are folded into the key as bits, so locking or losing the ground under
        // the aim redraws the label without the metres having moved.
        int range = bombs.IsAiming ? Mathf.RoundToInt(bombs.AimRange) : Hidden;
        int keyed = bombs.IsAiming
            ? range * 4 + (bombs.RangeLocked ? 2 : 0) + (bombs.AimValid ? 1 : 0)
            : Hidden;

        if (keyed == _shownRange) return;
        _shownRange = keyed;

        if (!bombs.IsAiming)
        {
            bombRangeText.text = string.Empty;
            return;
        }

        // An invalid aim says so in words as well as in the ring's colour. The ring is
        // out on the floor where the player is looking and the readout is under the
        // crosshair where they are aiming, and only one of those is somewhere they are
        // certain to see the moment a throw is about to be refused.
        if (!bombs.AimValid)
        {
            bombRangeText.text = $"<color=#{ColorUtility.ToHtmlStringRGB(clockWarningColor)}>" +
                                 "NO GROUND THERE</color>";
            return;
        }

        bombRangeText.text = bombs.RangeLocked
            ? $"<color=#66F0FF>{range} m  LOCKED</color>"
            : $"{range} m   <size=70%>{Key(AimKey())} TO LOCK</size>";
    }

    void UpdateBelt()
    {
        bool has = belt != null && belt.data != null;

        if (beltPanel != null && beltPanel.activeSelf != has) beltPanel.SetActive(has);
        if (!has) return;

        if (beltText != null && belt.Count != _shownBeltCount)
        {
            _shownBeltCount = belt.Count;
            beltText.text = $"{Key(ItemKey())} <size=130%>x{_shownBeltCount}</size>";
        }

        if (beltText != null)
            beltText.color = Time.unscaledTime < _beltDeniedUntil
                ? equipmentEmptyColor
                : belt.BoostActive ? belt.data.tint : equipmentReadyColor;

        if (boostFill == null) return;

        bool boosting = belt.BoostActive;
        if (boostFill.gameObject.activeSelf != boosting) boostFill.gameObject.SetActive(boosting);

        if (boosting)
        {
            boostFill.fillAmount = belt.BoostNormalized;
            boostFill.color = belt.data.tint;
        }
    }

    KeyCode BombKey()
    {
        var controls = PlayerControls();
        return controls != null ? controls.bomb : KeyCode.G;
    }

    KeyCode ItemKey()
    {
        var controls = PlayerControls();
        return controls != null ? controls.useItem : KeyCode.F;
    }

    KeyCode AimKey()
    {
        var controls = PlayerControls();
        return controls != null ? controls.aim : KeyCode.Mouse1;
    }

    /// <summary>
    /// The live bindings, read off whatever the player is actually using rather than
    /// off a copy. Same reason the key hints are written from the director: a hint that
    /// names a key which has been rebound is worse than no hint at all.
    /// </summary>
    ControlSettings PlayerControls()
    {
        if (_controls != null) return _controls;

        var motor = FindAnyObjectByType<PlayerMotor>();
        _controls = motor != null ? motor.controls : null;

        return _controls;
    }

    void OnBombDenied(BombThrower source) => _bombDeniedUntil = Time.unscaledTime + 0.6f;

    void OnBeltDenied(ConsumableBelt source) => _beltDeniedUntil = Time.unscaledTime + 0.6f;

    // ======================================================================
    void UpdateHealthBars()
    {
        if (playerHealth == null) return;

        float health01 = playerHealth.Normalized;

        if (healthFill != null) healthFill.fillAmount = health01;

        if (shieldFill != null)
        {
            bool hasShield = playerHealth.maxShield > 0f;
            if (shieldFill.gameObject.activeSelf != hasShield)
                shieldFill.gameObject.SetActive(hasShield);

            if (hasShield) shieldFill.fillAmount = playerHealth.ShieldNormalized;
        }

        if (healthTrailFill == null) return;

        // Snap upward on a heal, but let a hit sit visible for a beat before the pale
        // bar drains down to meet it.
        if (health01 >= _trail)
        {
            _trail = health01;
        }
        else if (Time.unscaledTime >= _trailHoldUntil)
        {
            _trail = Mathf.MoveTowards(_trail, health01, trailCatchUpSpeed * Time.unscaledDeltaTime);
        }

        healthTrailFill.fillAmount = _trail;
    }

    // ======================================================================
    /// <summary>
    /// Draws the bomb reticle where <see cref="BombThrower.AimScreenPoint"/> says, and
    /// nowhere otherwise.
    ///
    /// Built on demand rather than by the scene builder, for the reason the minimap
    /// draws itself: the kit has to come up correct in a level it did not build, and
    /// FPSKit > Add Gameplay To Current Scene cannot go back and add a child to a
    /// canvas somebody else authored. A scene that does have one wired uses that.
    ///
    /// It is a screen-space position written straight to anchoredPosition against a
    /// bottom-left anchor, which is the one arrangement where a pixel from the thrower
    /// and a pixel on the canvas are the same pixel at every resolution.
    /// </summary>
    void UpdateBombCursor()
    {
        bool wanted = bombs != null && bombs.IsAiming && bombs.UsingCursor;

        if (!wanted)
        {
            if (bombCursor != null && bombCursor.gameObject.activeSelf)
                bombCursor.gameObject.SetActive(false);

            return;
        }

        if (bombCursor == null) bombCursor = BuildBombCursor();
        if (bombCursor == null) return;

        if (!bombCursor.gameObject.activeSelf) bombCursor.gameObject.SetActive(true);

        CacheBombCursor();

        // Placed in world space rather than by anchoredPosition, which is the one way
        // that needs no assumption about the parent's anchors or pivot.
        //
        // Written the obvious way it is wrong and looks broken rather than off:
        // ScreenPointToLocalPointInRectangle measures from the parent's *pivot*, the
        // middle of a full-screen canvas, while anchoredPosition on a bottom-left
        // anchored rect measures from the corner. Assigning one to the other puts the
        // middle of the screen in the bottom-left corner and everything else off the
        // edge entirely -- so the reticle is simply not on screen, and with the view
        // deliberately held still the whole control reads as a dead mouse.
        if (_bombCursorParent != null && _bombCursorCanvas != null)
        {
            var eye = _bombCursorCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : _bombCursorCanvas.worldCamera;

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    _bombCursorParent, bombs.AimScreenPoint, eye, out Vector3 world))
                bombCursor.position = world;
        }

        // Only on the frame it changes. The cursor is up for as long as the player is
        // aiming, and repainting five images every one of those frames writes the same
        // colour it already had a hundred times for each time the answer moves.
        int valid = bombs.AimValid ? 1 : 0;
        if (valid == _shownBombCursorValid) return;

        _shownBombCursorValid = valid;

        Color tint = bombs.AimValid ? bombCursorColor : bombCursorBlockedColor;
        for (int i = 0; i < _bombCursorImages.Length; i++)
            if (_bombCursorImages[i] != null) _bombCursorImages[i].color = tint;
    }

    /// <summary>
    /// Resolves the cursor's canvas, its parent rect and its images once per cursor
    /// rather than once per frame.
    ///
    /// GetComponentsInChildren allocates the array it returns, so calling it from the
    /// aiming path was a fresh array every frame the ring was up, for a set of images
    /// that is fixed the moment the cursor is built. Keyed on the transform rather than
    /// on a flag, so a cursor swapped in the inspector still re-resolves -- and rebuilt
    /// from scratch if the cache came back null across a mid-play domain reload, which
    /// an array of Object references survives but is cheap to prove.
    /// </summary>
    void CacheBombCursor()
    {
        if (_cachedBombCursor == bombCursor && _bombCursorImages != null) return;

        _cachedBombCursor = bombCursor;
        _bombCursorCanvas = bombCursor.GetComponentInParent<Canvas>();
        _bombCursorParent = bombCursor.parent as RectTransform;
        _bombCursorImages = bombCursor.GetComponentsInChildren<Image>();
        _shownBombCursorValid = Unset;
    }

    /// <summary>
    /// A ring of four ticks around a dot. Drawn rather than textured because the kit
    /// ships no sprites, and every part of it has raycastTarget off -- the pause menu
    /// lives on this canvas, and a reticle over the middle of the screen would sit on
    /// top of whatever the player tried to click.
    /// </summary>
    RectTransform BuildBombCursor()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return null;

        var root = new GameObject("BombCursor", typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);

        var rect = (RectTransform)root.transform;

        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(bombCursorSize, bombCursorSize);

        float tick = Mathf.Max(2f, bombCursorSize * 0.09f);
        float arm = bombCursorSize * 0.34f;

        Tick(rect, "Up", new Vector2(0f, arm), new Vector2(tick, arm * 0.8f));
        Tick(rect, "Down", new Vector2(0f, -arm), new Vector2(tick, arm * 0.8f));
        Tick(rect, "Left", new Vector2(-arm, 0f), new Vector2(arm * 0.8f, tick));
        Tick(rect, "Right", new Vector2(arm, 0f), new Vector2(arm * 0.8f, tick));
        Tick(rect, "Dot", Vector2.zero, new Vector2(tick * 1.4f, tick * 1.4f));

        return rect;
    }

    static void Tick(RectTransform parent, string name, Vector2 offset, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;

        go.GetComponent<Image>().raycastTarget = false;
    }

    void UpdateCrosshair()
    {
        if (crosshairArms == null || crosshairArms.Length < 4) return;

        float targetGap = crosshairBaseGap + _kick;

        if (weapon != null && Cam() != null)
            targetGap += SpreadToPixels(weapon.CurrentSpread);

        _kick = Mathf.Lerp(_kick, 0f, Mathf.Clamp01(crosshairSmoothing * Time.unscaledDeltaTime));
        _currentGap = Mathf.Lerp(_currentGap, targetGap,
                                 Mathf.Clamp01(crosshairSmoothing * Time.unscaledDeltaTime));

        SetArm(0, new Vector2(0f, _currentGap));    // Top
        SetArm(1, new Vector2(0f, -_currentGap));   // Bottom
        SetArm(2, new Vector2(-_currentGap, 0f));   // Left
        SetArm(3, new Vector2(_currentGap, 0f));    // Right

        Color tint = Time.unscaledTime < _hitmarkerUntil ? _hitmarkerTint : crosshairColor;
        foreach (var image in CrosshairImages())
            if (image != null) image.color = tint;

        // Hidden entirely while a bomb is being placed. Firing is suppressed anyway,
        // so it marks a shot that cannot be taken -- and a second reticle in the middle
        // of the screen is the thing most likely to be mistaken for the one the player
        // is supposed to be moving.
        if (bombs != null && bombs.IsAiming && bombs.UsingCursor)
        {
            if (crosshairGroup != null)
                crosshairGroup.alpha = Mathf.Lerp(crosshairGroup.alpha, 0f,
                                                  Mathf.Clamp01(crosshairSmoothing * Time.unscaledDeltaTime));
            return;
        }

        if (crosshairGroup != null)
        {
            float target = weapon != null ? 1f - weapon.AimProgress : 1f;
            crosshairGroup.alpha = Mathf.Lerp(crosshairGroup.alpha, target,
                                              Mathf.Clamp01(16f * Time.unscaledDeltaTime));
        }
    }

    /// <summary>
    /// The arms' Image components, resolved once instead of per frame.
    ///
    /// The tint loop called GetComponent on all four arms every frame -- 240 lookups a
    /// second to write a colour that only moves when a hitmarker lands. It sits in a
    /// method Update calls rather than in Update itself, which is the same place the
    /// HUD's per-frame string building hid.
    ///
    /// Rebuilt whenever the arm count changes, which covers both a rig rewired at
    /// runtime and the first call after a scene reload. An Image[] is a plain array of
    /// UnityEngine.Object references, so unlike a property block it does survive a
    /// mid-play domain reload -- but the arms it points at do not survive a scene load.
    /// </summary>
    Image[] CrosshairImages()
    {
        if (crosshairArms == null) return System.Array.Empty<Image>();

        if (_crosshairImages == null || _crosshairImages.Length != crosshairArms.Length)
            _crosshairImages = new Image[crosshairArms.Length];

        for (int i = 0; i < crosshairArms.Length; i++)
            if (_crosshairImages[i] == null && crosshairArms[i] != null)
                _crosshairImages[i] = crosshairArms[i].GetComponent<Image>();

        return _crosshairImages;
    }

    void SetArm(int index, Vector2 position)
    {
        if (index < crosshairArms.Length && crosshairArms[index] != null)
            crosshairArms[index].anchoredPosition = position;
    }

    /// <summary>
    /// Converts a cone half-angle into canvas pixels using the camera focal
    /// length, so the crosshair genuinely shows where rounds can land.
    /// </summary>
    float SpreadToPixels(float spreadDegrees)
    {
        const float referenceHeight = 1080f;

        float halfFov = Cam().fieldOfView * 0.5f * Mathf.Deg2Rad;
        if (halfFov <= 0.001f) return 0f;

        float focalLength = referenceHeight * 0.5f / Mathf.Tan(halfFov);
        return focalLength * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
    }

    // ======================================================================
    void UpdateVignette()
    {
        if (damageVignette == null) return;

        _vignetteAlpha = Mathf.Max(0f, _vignetteAlpha - vignetteFadeSpeed * Time.unscaledDeltaTime);

        float alpha = _vignetteAlpha;

        if (playerHealth != null && !playerHealth.IsDead && playerHealth.Normalized < lowHealthThreshold)
        {
            float severity = 1f - playerHealth.Normalized / lowHealthThreshold;
            float pulse = 0.18f + Mathf.Sin(Time.unscaledTime * 3.2f) * 0.06f;
            alpha = Mathf.Max(alpha, severity * pulse);
        }

        damageVignette.color = WithAlpha(damageVignette.color, Mathf.Clamp01(alpha));
    }

    // ======================================================================
    // Banner
    // ======================================================================
    void OnLevelStarted(LevelManager source)
    {
        if (source == null) return;

        string target = source.HasBoss
            ? $"{source.TotalEnemies - 1} ENEMIES AND A BOSS IN {Mathf.RoundToInt(source.TimeLimit)}s"
            : $"{source.TotalEnemies} ENEMIES IN {Mathf.RoundToInt(source.TimeLimit)}s";

        ShowBanner($"LEVEL {source.LevelNumber}", target,
                   source.HasBoss ? bossBannerColor : Color.white);
    }

    void OnUpgraded(PlayerProgression source, int level)
        => QueueBanner("RIFLE UPGRADED", source.Summary, upgradeBannerColor);

    /// <summary>Shows a banner now, or holds it until the one on screen has been read.</summary>
    void QueueBanner(string title, string subtitle, Color tint)
    {
        if (bannerGroup == null) return;

        if (bannerGroup.alpha <= 0f && Time.unscaledTime >= _bannerUntil)
        {
            ShowBanner(title, subtitle, tint);
            return;
        }

        _hasPendingBanner = true;
        _pendingTitle = title;
        _pendingSubtitle = subtitle;
        _pendingTint = tint;
    }

    void ShowBanner(string title, string subtitle, Color tint)
    {
        if (bannerGroup == null) return;

        if (bannerTitle != null)
        {
            bannerTitle.text = title;
            bannerTitle.color = tint;
        }

        if (bannerSubtitle != null) bannerSubtitle.text = subtitle;

        bannerGroup.alpha = 1f;
        _bannerUntil = Time.unscaledTime + bannerHold;
    }

    void UpdateBanner()
    {
        if (bannerGroup == null) return;

        if (_hasPendingBanner && Time.unscaledTime >= _bannerUntil)
        {
            _hasPendingBanner = false;
            ShowBanner(_pendingTitle, _pendingSubtitle, _pendingTint);
            return;
        }

        if (bannerGroup.alpha <= 0f) return;
        if (Time.unscaledTime < _bannerUntil) return;

        bannerGroup.alpha = Mathf.MoveTowards(bannerGroup.alpha, 0f,
                                              bannerFadeSpeed * Time.unscaledDeltaTime);
    }

    // ======================================================================
    void UpdateBossBar()
    {
        if (bossPanel == null) return;

        var boss = levelManager != null ? levelManager.ActiveBoss : null;
        bool show = boss != null && !boss.IsDead;

        if (bossPanel.activeSelf != show) bossPanel.SetActive(show);
        if (!show) return;

        if (bossNameText != null) bossNameText.text = levelManager.ActiveBossName;
        if (bossFill != null) bossFill.fillAmount = boss.TotalNormalized;
    }

    // ======================================================================
    // Directional damage
    // ======================================================================
    void SpawnIndicator(DamageInfo info)
    {
        if (damageIndicatorPrefab == null || damageIndicatorRoot == null ||
            playerHealth == null || Cam() == null) return;

        Vector3 toSource = info.source != null
            ? info.source.transform.position - playerHealth.transform.position
            : -info.direction;

        toSource.y = 0f;
        if (toSource.sqrMagnitude < 0.001f) return;

        Vector3 forward = Cam().transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) return;

        // Signed angle from where we are looking to where it came from, then applied
        // as a Z rotation -- negated because UI angles run anticlockwise.
        float angle = Vector3.SignedAngle(forward, toSource, Vector3.up);

        var group = Instantiate(damageIndicatorPrefab, damageIndicatorRoot);
        group.gameObject.SetActive(true);
        group.alpha = 1f;
        group.transform.localRotation = Quaternion.Euler(0f, 0f, -angle);

        _indicators.Add(new Indicator { group = group, expiry = Time.unscaledTime + indicatorDuration });
    }

    void UpdateIndicators()
    {
        for (int i = _indicators.Count - 1; i >= 0; i--)
        {
            var indicator = _indicators[i];

            if (indicator.group == null)
            {
                _indicators.RemoveAt(i);
                continue;
            }

            float remaining = indicator.expiry - Time.unscaledTime;
            if (remaining <= 0f)
            {
                Destroy(indicator.group.gameObject);
                _indicators.RemoveAt(i);
                continue;
            }

            indicator.group.alpha = Mathf.Clamp01(remaining / Mathf.Max(0.01f, indicatorDuration));
        }
    }

    // ======================================================================
    void OnWeaponFired(Weapon source) => _kick += crosshairKick;

    void OnDealtDamage(Weapon source, DamageInfo info)
    {
        _hitmarkerTint = info.isHeadshot ? headshotColor : hitmarkerColor;
        _hitmarkerUntil = Time.unscaledTime + hitmarkerDuration;
    }

    void OnPlayerDamaged(Health health, DamageInfo info)
    {
        float severity = health.maxHealth > 0f ? info.amount / health.maxHealth : 0.2f;

        _vignetteAlpha = Mathf.Clamp01(_vignetteAlpha + 0.35f + severity);
        _trailHoldUntil = Time.unscaledTime + trailCatchUpDelay;

        SpawnIndicator(info);
    }

    // ======================================================================
    void OnPauseChanged(GameDirector director, bool paused)
    {
        if (pausePanel != null) pausePanel.SetActive(paused);
        if (crosshairGroup != null && paused) crosshairGroup.alpha = 0f;
    }

    /// <summary>
    /// Clears the fight off the screen when the level is scored. What replaces it is
    /// LevelResultsUI, which is a separate component because the end of a level is a
    /// screen in its own right rather than one more HUD element.
    /// </summary>
    void OnGameEnded(GameDirector director)
    {
        if (pausePanel != null) pausePanel.SetActive(false);
        if (crosshairGroup != null) crosshairGroup.alpha = 0f;
        if (bossPanel != null) bossPanel.SetActive(false);

        if (objectiveText != null) objectiveText.text = string.Empty;
        if (briefingText != null) briefingText.text = string.Empty;
    }

    /// <summary>
    /// Camera.main can be null during Awake depending on script execution order, and a
    /// HUD that cached null there would silently lose crosshair spread and every damage
    /// arrow for the rest of the run.
    /// </summary>
    Camera Cam()
    {
        if (_camera == null) _camera = Camera.main;
        return _camera;
    }

    static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);
}
