using System;
using UnityEngine;

/// <summary>
/// Makes the rifle grow with the run: every wave survived widens the magazine,
/// hardens the round and shortens the reload.
///
/// The curve exists because the enemy curve does. Waves get bigger, tougher and
/// meaner on a schedule the WaveManager owns, and a weapon that never changes turns
/// that into a slope the player slides down -- the same thirty rounds against twice
/// the bodies, fight after fight, until the arithmetic runs out. Giving the gun its
/// own curve means later waves are a harder fight rather than a longer one, and the
/// reward for clearing a wave is something the player can feel in the next one.
///
/// Nothing here is written to the WeaponData asset. That is the important part: the
/// asset is one shared ScriptableObject, so a bonus written into it would survive
/// quitting the game and start the next run already upgraded -- and would compound
/// every restart. The upgrades are multipliers held on the Weapon component, which
/// dies with the scene the way a run's state should. See Weapon.ApplyUpgrades.
/// </summary>
[DisallowMultipleComponent]
public class PlayerProgression : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Found on this object or below it when empty, so the builder does not have " +
             "to know where in the rig the weapon ended up.")]
    public Weapon weapon;

    [Tooltip("Found in the scene when empty.")]
    public WaveManager waveManager;

    [Header("Magazine")]
    [Tooltip("Rounds added to the magazine for each wave cleared. The stock rifle holds " +
             "thirty, so this is what stops a later wave from being spent reloading.")]
    [Min(0)] public int magazineBonusPerWave = 3;

    [Tooltip("Ceiling on the added rounds. Past a point a bigger magazine stops being a " +
             "reward and starts removing the reload from the game entirely.")]
    [Min(0)] public int maxMagazineBonus = 60;

    [Header("Power")]
    [Tooltip("Fraction added to damage per wave cleared. 0.07 is seven percent a wave, " +
             "which roughly keeps pace with the enemy health curve rather than beating it.")]
    [Range(0f, 1f)] public float damageBonusPerWave = 0.07f;

    [Tooltip("Ceiling on the damage multiplier. Without one the rifle eventually " +
             "one-shots a boss and the boss waves stop being fights.")]
    [Min(1f)] public float maxDamageMultiplier = 2.6f;

    [Header("Reload")]
    [Tooltip("Fraction shaved off the reload per wave cleared.")]
    [Range(0f, 0.5f)] public float reloadSpeedBonusPerWave = 0.04f;

    [Tooltip("Floor on the reload multiplier, so the animation never collapses to nothing.")]
    [Range(0.1f, 1f)] public float minReloadMultiplier = 0.5f;

    [Header("Feedback")]
    [Tooltip("Refill the magazine when an upgrade lands. On by default: a bigger " +
             "magazine handed over empty is not a reward.")]
    public bool topUpMagazineOnUpgrade = true;

    [Tooltip("Played when an upgrade lands. Optional, like every audio field in the kit.")]
    public AudioClip upgradeClip;

    public AudioSource audioSource;

    /// <summary>Waves cleared this run. 0 means the rifle is still the asset's rifle.</summary>
    public int WavesCleared { get; private set; }

    /// <summary>One line describing the gun as it now stands. The HUD prints this.</summary>
    public string Summary { get; private set; } = "";

    /// <summary>Raised after an upgrade has been applied, with the wave that earned it.</summary>
    public event Action<PlayerProgression, int> Upgraded;

    // ======================================================================
    void Start()
    {
        if (weapon == null) weapon = GetComponentInChildren<Weapon>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        if (waveManager == null) waveManager = FindAnyObjectByType<WaveManager>();

        if (weapon == null)
        {
            Debug.LogWarning("[PlayerProgression] No Weapon found, so the rifle will not " +
                             "improve between waves.", this);
            return;
        }

        if (waveManager != null) waveManager.WaveCleared += OnWaveCleared;

        // Applied rather than assumed. WavesCleared is zero on a fresh run and this is
        // simply the asset's own numbers -- but a script recompiled mid-play reloads the
        // domain without re-running Awake, and the count survives that while nothing
        // guarantees the weapon's multipliers did. Recomputing from the count means the
        // two can never drift apart.
        Apply();
    }

    void OnDestroy()
    {
        if (waveManager != null) waveManager.WaveCleared -= OnWaveCleared;
    }

    // ======================================================================

    /// <summary>
    /// Keyed off the wave number rather than a tally of calls.
    ///
    /// The WaveManager restarts its own wave loop if it ever loses it, and a restart
    /// replays WaveCleared for a wave that has already paid out. Taking the maximum
    /// makes a repeat harmless, where counting calls would quietly hand out a free
    /// upgrade for it.
    /// </summary>
    void OnWaveCleared(int wave)
    {
        int cleared = Mathf.Max(WavesCleared, wave);
        if (cleared == WavesCleared) return;

        WavesCleared = cleared;
        Apply();

        if (upgradeClip != null && audioSource != null) audioSource.PlayOneShot(upgradeClip);

        Upgraded?.Invoke(this, wave);
    }

    void Apply()
    {
        if (weapon == null) return;

        int magazine = Mathf.Min(maxMagazineBonus, magazineBonusPerWave * WavesCleared);

        float damage = Mathf.Min(maxDamageMultiplier, 1f + damageBonusPerWave * WavesCleared);

        float reload = Mathf.Max(minReloadMultiplier, 1f - reloadSpeedBonusPerWave * WavesCleared);

        weapon.ApplyUpgrades(magazine, damage, reload, topUpMagazineOnUpgrade && WavesCleared > 0);

        Summary = WavesCleared <= 0
            ? ""
            : $"MAG {weapon.MagazineSize}   DMG +{(damage - 1f) * 100f:0}%   RELOAD {weapon.ReloadTime:0.0}s";
    }
}
