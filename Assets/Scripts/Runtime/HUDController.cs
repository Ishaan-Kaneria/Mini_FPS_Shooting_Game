using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives every HUD element: ammo, the two-layer health bar, wave state and
/// modifier banners, score and combo, a boss bar, a crosshair that opens with
/// weapon spread, directional damage arrows, the pause menu and the game over
/// screen.
///
/// All references are optional -- a missing one is simply skipped, so a HUD
/// assembled by hand can show as little or as much as it likes.
/// </summary>
public class HUDController : MonoBehaviour
{
    [Header("Sources")]
    public Weapon weapon;
    public Health playerHealth;
    public WaveManager waveManager;

    [Header("Text")]
    public TMP_Text ammoText;
    public TMP_Text healthText;
    public TMP_Text waveText;
    public TMP_Text enemiesLeftText;
    public TMP_Text intermissionText;
    public TMP_Text scoreText;
    public TMP_Text comboText;
    public TMP_Text finalWaveText;

    [Header("Health Bars")]
    public Image healthFill;
    public Image shieldFill;

    [Tooltip("Pale bar that trails behind the real one. Seeing the gap is how you read " +
             "how much a hit actually cost, at a glance, mid-fight.")]
    public Image healthTrailFill;

    public float trailCatchUpDelay = 0.4f;
    public float trailCatchUpSpeed = 0.8f;

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

    [Header("Wave Banner")]
    public CanvasGroup bannerGroup;
    public TMP_Text bannerTitle;
    public TMP_Text bannerSubtitle;
    public float bannerHold = 2.2f;
    public float bannerFadeSpeed = 3.5f;
    public Color modifierBannerColor = new Color(1f, 0.6f, 0.25f);
    public Color bossBannerColor = new Color(1f, 0.3f, 0.3f);

    [Header("Boss Bar")]
    public GameObject bossPanel;
    public TMP_Text bossNameText;
    public Image bossFill;

    [Header("Pause")]
    public GameObject pausePanel;
    public TMP_Text pauseHintText;

    [Header("Game Over")]
    public GameObject gameOverPanel;
    public KeyCode restartKey = KeyCode.R;

    float _currentGap;
    float _kick;
    float _vignetteAlpha;
    float _hitmarkerUntil;
    Color _hitmarkerTint;
    bool _gameOverShown;
    Camera _camera;
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

        _currentGap = crosshairBaseGap;
        _hitmarkerTint = crosshairColor;

        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (pausePanel != null) pausePanel.SetActive(false);
        if (bossPanel != null) bossPanel.SetActive(false);
        if (damageVignette != null) damageVignette.color = WithAlpha(damageVignette.color, 0f);
        if (bannerGroup != null) bannerGroup.alpha = 0f;

        // The template is a source object, not something that should render.
        if (damageIndicatorPrefab != null) damageIndicatorPrefab.gameObject.SetActive(false);
    }

    void Start()
    {
        if (_director != null) restartKey = _director.restartKey;

        if (pauseHintText != null)
        {
            string hint = $"{Director?.pauseKey ?? KeyCode.Escape} resume    " +
                          $"{restartKey} restart";

            // A browser build has nowhere to quit to, so the key does nothing there --
            // and a menu advertising a key that does nothing reads as a broken game.
            if (GameDirector.CanQuit)
                hint += $"    {Director?.quitKey ?? KeyCode.Q} quit";

            pauseHintText.text = $"<size=60%>{hint}</size>";
        }
    }

    void OnEnable()
    {
        if (weapon != null)
        {
            weapon.Fired += OnWeaponFired;
            weapon.DealtDamage += OnDealtDamage;
        }

        if (playerHealth != null) playerHealth.Damaged += OnPlayerDamaged;

        if (waveManager != null)
        {
            waveManager.WaveStarted += OnWaveStarted;
            waveManager.WaveCleared += OnWaveCleared;
        }

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

        if (waveManager != null)
        {
            waveManager.WaveStarted -= OnWaveStarted;
            waveManager.WaveCleared -= OnWaveCleared;
        }

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
        UpdateVignette();
        UpdateBanner();
        UpdateBossBar();
        UpdateIndicators();
    }

    // ==================================================================
    // Text
    //
    // UpdateTexts runs every frame, and every label below used to rebuild an
    // interpolated string on each one -- up to seven allocations a frame, nearly all
    // identical to the frame before, since ammo only moves when you fire and the wave
    // number only once a wave. At 60fps that is several hundred short-lived strings a
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
    int _shownWave = Unset;
    int _shownLeft = Unset, _shownWaveClock = Unset;
    int _shownIntermission = Unset;

    void UpdateTexts()
    {
        if (ammoText != null && weapon != null) UpdateAmmoText();
        if (healthText != null && playerHealth != null) UpdateHealthText();

        if (_director != null)
        {
            UpdateScoreText();
            UpdateComboText();
        }

        if (waveManager == null) return;

        UpdateWaveText();
        UpdateEnemiesLeftText();
        UpdateIntermissionText();
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

    void UpdateWaveText()
    {
        if (waveText == null || waveManager.CurrentWave == _shownWave) return;

        _shownWave = waveManager.CurrentWave;
        waveText.text = $"WAVE {_shownWave}";
    }

    /// <summary>
    /// Enemies left, plus the wave clock once it is running. The clock matters because
    /// the wave no longer requires a total wipe: without it, a player hunting the last
    /// enemy has no way to know the round will move on by itself.
    /// </summary>
    void UpdateEnemiesLeftText()
    {
        if (enemiesLeftText == null) return;

        bool intermission = waveManager.IsIntermission;
        float remaining = waveManager.WaveTimeRemaining;

        int left = intermission ? Hidden : waveManager.EnemiesRemaining;
        int clock = intermission || remaining <= 0f ? Hidden : Mathf.CeilToInt(remaining);

        if (left == _shownLeft && clock == _shownWaveClock) return;

        _shownLeft = left;
        _shownWaveClock = clock;

        if (intermission) enemiesLeftText.text = string.Empty;
        else if (clock == Hidden) enemiesLeftText.text = $"{left} LEFT";
        else enemiesLeftText.text = $"{left} LEFT   <size=75%>{clock / 60}:{clock % 60:00}</size>";
    }

    void UpdateIntermissionText()
    {
        if (intermissionText == null) return;

        bool counting = waveManager.IsIntermission && !waveManager.GameIsOver;
        int seconds = counting ? Mathf.CeilToInt(waveManager.IntermissionRemaining) : Hidden;

        if (seconds == _shownIntermission) return;

        _shownIntermission = seconds;
        intermissionText.text = counting ? $"NEXT WAVE IN {seconds}" : string.Empty;
    }

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
    // Wave banner
    // ======================================================================
    void OnWaveStarted(int wave)
    {
        if (waveManager == null) return;

        string modifier = WaveManager.ModifierName(waveManager.CurrentModifier);

        if (waveManager.IsBossWave)
            ShowBanner($"WAVE {wave}", "BOSS INCOMING", bossBannerColor);
        else if (!string.IsNullOrEmpty(modifier))
            ShowBanner($"WAVE {wave} - {modifier}",
                       WaveManager.ModifierDescription(waveManager.CurrentModifier),
                       modifierBannerColor);
        else
            ShowBanner($"WAVE {wave}", string.Empty, Color.white);
    }

    void OnWaveCleared(int wave)
        => ShowBanner($"WAVE {wave} CLEARED", $"+{waveManager.waveClearBonus * wave:N0}", Color.white);

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
        if (bannerGroup == null || bannerGroup.alpha <= 0f) return;
        if (Time.unscaledTime < _bannerUntil) return;

        bannerGroup.alpha = Mathf.MoveTowards(bannerGroup.alpha, 0f,
                                              bannerFadeSpeed * Time.unscaledDeltaTime);
    }

    // ======================================================================
    void UpdateBossBar()
    {
        if (bossPanel == null) return;

        var boss = waveManager != null ? waveManager.ActiveBoss : null;
        bool show = boss != null && !boss.IsDead;

        if (bossPanel.activeSelf != show) bossPanel.SetActive(show);
        if (!show) return;

        if (bossNameText != null) bossNameText.text = waveManager.ActiveBossName;
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

    void OnGameEnded(GameDirector director)
    {
        if (_gameOverShown) return;
        _gameOverShown = true;

        if (pausePanel != null) pausePanel.SetActive(false);

        if (finalWaveText != null)
        {
            int wave = director.FinalWave;
            string record = director.BeatBestWave
                ? "<color=#FFD24A>NEW RECORD</color>\n"
                : $"<size=55%>Best: wave {director.BestWave}</size>\n";

            finalWaveText.text =
                $"{record}You survived {wave} wave{(wave == 1 ? "" : "s")}\n" +
                $"<size=65%>{director.Score:N0} points   {director.Kills} kills   " +
                $"{director.Headshots} headshots</size>\n\n" +
                $"<size=55%>Press {restartKey} to try again</size>";
        }

        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (crosshairGroup != null) crosshairGroup.alpha = 0f;
        if (bossPanel != null) bossPanel.SetActive(false);
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
