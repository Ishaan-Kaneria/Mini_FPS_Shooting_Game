using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Drives every HUD element: ammo, health, wave state, a crosshair that opens
/// with weapon spread, a damage vignette and the game over screen.
/// All references are optional -- a missing one is simply skipped.
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
    public TMP_Text finalWaveText;

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

    // ======================================================================
    void Awake()
    {
        PlayerMotor.InputEnabled = true;   // static, so reset it on scene load
        Time.timeScale = 1f;

        _camera = Camera.main;
        _currentGap = crosshairBaseGap;
        _hitmarkerTint = crosshairColor;

        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (damageVignette != null) damageVignette.color = WithAlpha(damageVignette.color, 0f);
    }

    void OnEnable()
    {
        if (weapon != null)
        {
            weapon.Fired += OnWeaponFired;
            weapon.DealtDamage += OnDealtDamage;
        }

        if (playerHealth != null) playerHealth.Damaged += OnPlayerDamaged;
        if (waveManager != null) waveManager.GameOver += OnGameOver;
    }

    void OnDisable()
    {
        if (weapon != null)
        {
            weapon.Fired -= OnWeaponFired;
            weapon.DealtDamage -= OnDealtDamage;
        }

        if (playerHealth != null) playerHealth.Damaged -= OnPlayerDamaged;
        if (waveManager != null) waveManager.GameOver -= OnGameOver;
    }

    // ======================================================================
    void Update()
    {
        UpdateTexts();
        UpdateCrosshair();
        UpdateVignette();

        if (_gameOverShown && Input.GetKeyDown(restartKey)) Restart();
    }

    void UpdateTexts()
    {
        if (ammoText != null && weapon != null)
        {
            var data = weapon.Data;

            if (weapon.IsReloading) ammoText.text = "RELOADING";
            else if (data != null && data.infiniteAmmo) ammoText.text = "∞";
            else if (data != null && data.infiniteReserve) ammoText.text = $"{weapon.CurrentAmmo} / ∞";
            else ammoText.text = $"{weapon.CurrentAmmo} / {weapon.ReserveAmmo}";
        }

        if (healthText != null && playerHealth != null)
            healthText.text = Mathf.CeilToInt(playerHealth.Current).ToString();

        if (waveManager == null) return;

        if (waveText != null)
            waveText.text = $"WAVE {waveManager.CurrentWave}";

        if (enemiesLeftText != null)
            enemiesLeftText.text = waveManager.IsIntermission
                ? string.Empty
                : $"{waveManager.EnemiesRemaining} LEFT";

        if (intermissionText != null)
            intermissionText.text = waveManager.IsIntermission && !waveManager.GameIsOver
                ? $"NEXT WAVE IN {Mathf.CeilToInt(waveManager.IntermissionRemaining)}"
                : string.Empty;
    }

    // ======================================================================
    void UpdateCrosshair()
    {
        if (crosshairArms == null || crosshairArms.Length < 4) return;

        float targetGap = crosshairBaseGap + _kick;

        if (weapon != null && _camera != null)
            targetGap += SpreadToPixels(weapon.CurrentSpread);

        _kick = Mathf.Lerp(_kick, 0f, Mathf.Clamp01(crosshairSmoothing * Time.unscaledDeltaTime));
        _currentGap = Mathf.Lerp(_currentGap, targetGap,
                                 Mathf.Clamp01(crosshairSmoothing * Time.unscaledDeltaTime));

        SetArm(0, new Vector2(0f, _currentGap));    // Top
        SetArm(1, new Vector2(0f, -_currentGap));   // Bottom
        SetArm(2, new Vector2(-_currentGap, 0f));   // Left
        SetArm(3, new Vector2(_currentGap, 0f));    // Right

        Color tint = Time.unscaledTime < _hitmarkerUntil ? _hitmarkerTint : crosshairColor;
        foreach (var arm in crosshairArms)
        {
            if (arm == null) continue;
            var image = arm.GetComponent<Image>();
            if (image != null) image.color = tint;
        }

        if (crosshairGroup != null)
        {
            float target = weapon != null ? 1f - weapon.AimProgress : 1f;
            crosshairGroup.alpha = Mathf.Lerp(crosshairGroup.alpha, target,
                                              Mathf.Clamp01(16f * Time.unscaledDeltaTime));
        }
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

        float halfFov = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        if (halfFov <= 0.001f) return 0f;

        float focalLength = referenceHeight * 0.5f / Mathf.Tan(halfFov);
        return focalLength * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
    }

    // ======================================================================
    void UpdateVignette()
    {
        if (damageVignette == null) return;

        _vignetteAlpha = Mathf.Max(0f, _vignetteAlpha - vignetteFadeSpeed * Time.deltaTime);

        float alpha = _vignetteAlpha;

        if (playerHealth != null && !playerHealth.IsDead && playerHealth.Normalized < lowHealthThreshold)
        {
            float severity = 1f - playerHealth.Normalized / lowHealthThreshold;
            float pulse = 0.18f + Mathf.Sin(Time.time * 3.2f) * 0.06f;
            alpha = Mathf.Max(alpha, severity * pulse);
        }

        damageVignette.color = WithAlpha(damageVignette.color, Mathf.Clamp01(alpha));
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
    }

    void OnGameOver(int wave)
    {
        if (_gameOverShown) return;
        _gameOverShown = true;

        if (finalWaveText != null)
            finalWaveText.text = $"You survived {wave} wave{(wave == 1 ? "" : "s")}\n\n" +
                                 $"<size=60%>Press {restartKey} to try again</size>";

        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (crosshairGroup != null) crosshairGroup.alpha = 0f;

        PlayerMotor.InputEnabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0f;
    }

    void Restart()
    {
        Time.timeScale = 1f;
        PlayerMotor.InputEnabled = true;

        var scene = SceneManager.GetActiveScene();

        // buildIndex is -1 when the scene is not in Build Settings.
        if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
        else SceneManager.LoadScene(scene.name);
    }

    static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);
}
