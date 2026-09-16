using System;
using UnityEngine;

/// <summary>
/// Makes the rifle grow with the ladder: every level you are cleared to enter widens
/// the magazine, hardens the round and shortens the reload.
///
/// The curve exists because the level curve does. Level eight throws four times the
/// bodies of level one at you inside a clock that has not grown as fast, and a weapon
/// that never changes turns that into a slope the player slides down -- the same thirty
/// rounds against twice the enemies, attempt after attempt, until the arithmetic runs
/// out. Giving the gun its own curve means a later level is a harder fight rather than
/// a longer one.
///
/// It is keyed off the level *number* rather than a tally of anything, which is the
/// change the level system forced and an improvement anyway: a level is one scene load,
/// so there is no run-long accumulation to lose, replaying level six always hands you
/// the same rifle, and a player who beat level six is never sent back into it with the
/// level-one gun because they quit to the dashboard in between.
///
/// It composes with the store rather than competing with it. What the player bought is
/// permanent and lives on <see cref="PlayerLoadout"/>; what the level hands over is this
/// curve; and the two multiply, because a maxed rifle on level eight should be a maxed
/// rifle *and* a level-eight one. Both go through the same
/// <see cref="Weapon.ApplyUpgrades"/> call, which is absolute, so whichever of the two
/// components runs first cannot leave the other's half behind.
///
/// Nothing here is written to the WeaponData asset. That is the important part: the
/// asset is one shared ScriptableObject, so a bonus written into it would survive
/// quitting the game and start the next attempt already upgraded -- and would compound
/// every restart. The upgrades are multipliers held on the Weapon component, which
/// dies with the scene the way a level's state should. See Weapon.ApplyUpgrades.
/// </summary>
[DisallowMultipleComponent]
public class PlayerProgression : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("Found on this object or below it when empty, so the builder does not have " +
             "to know where in the rig the weapon ended up.")]
    public Weapon weapon;

    [Tooltip("Found in the scene when empty.")]
    public LevelManager levelManager;

    [Tooltip("Where the bought upgrades come from. Found on this object when empty. " +
             "Without one the level curve is the only curve, which is what a scene that " +
             "never went through the store should get.")]
    public PlayerLoadout loadout;

    [Header("Magazine")]
    [Tooltip("Rounds added to the magazine for each level below this one. The stock " +
             "rifle holds thirty, so this is what stops a later level being spent " +
             "reloading while the clock runs.")]
    [Min(0)] public int magazineBonusPerLevel = 4;

    [Tooltip("Ceiling on the added rounds. Past a point a bigger magazine stops being a " +
             "reward and starts removing the reload from the game entirely.")]
    [Min(0)] public int maxMagazineBonus = 60;

    [Header("Power")]
    [Tooltip("Fraction added to damage per level. 0.09 is nine percent a level, which " +
             "roughly keeps pace with the difficulty curve rather than beating it.")]
    [Range(0f, 1f)] public float damageBonusPerLevel = 0.09f;

    [Tooltip("Ceiling on the damage multiplier. Without one the rifle eventually " +
             "one-shots a boss and the boss levels stop being fights.")]
    [Min(1f)] public float maxDamageMultiplier = 2.6f;

    [Header("Reload")]
    [Tooltip("Fraction shaved off the reload per level.")]
    [Range(0f, 0.5f)] public float reloadSpeedBonusPerLevel = 0.05f;

    [Tooltip("Floor on the reload multiplier, so the animation never collapses to nothing.")]
    [Range(0.1f, 1f)] public float minReloadMultiplier = 0.5f;

    [Header("Feedback")]
    [Tooltip("Played when the rifle is handed over upgraded, at the start of a level " +
             "past the first. Optional, like every audio field in the kit.")]
    public AudioClip upgradeClip;

    public AudioSource audioSource;

    /// <summary>Levels below this one. 0 means the rifle is still the asset's rifle.</summary>
    public int Steps { get; private set; }

    /// <summary>One line describing the gun as it now stands. The HUD prints this.</summary>
    public string Summary { get; private set; } = "";

    /// <summary>Raised once the upgraded rifle is in hand, with the level that earned it.</summary>
    public event Action<PlayerProgression, int> Upgraded;

    bool _announced;

    // ======================================================================
    void Start()
    {
        if (weapon == null) weapon = GetComponentInChildren<Weapon>();
        if (loadout == null) loadout = GetComponent<PlayerLoadout>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        if (levelManager == null) levelManager = FindAnyObjectByType<LevelManager>();

        if (weapon == null)
        {
            Debug.LogWarning("[PlayerProgression] No Weapon found, so the rifle will not " +
                             "keep up with the levels.", this);
            return;
        }

        if (levelManager != null) levelManager.LevelStarted += OnLevelStarted;

        // Which of the two Starts runs first is not defined, so both orders are covered:
        // the manager resolves its level in Start, and if it got there first the level
        // is already known and the rifle can be handed over now rather than waiting for
        // an event that has already been raised.
        if (levelManager != null && levelManager.Level != null) OnLevelStarted(levelManager);

        // Applied rather than assumed. Steps is zero before the level announces itself
        // and this is simply the asset's own numbers -- but a script recompiled mid-play
        // reloads the domain without re-running Awake, and the count survives that while
        // nothing guarantees the weapon's multipliers did. Recomputing from the count
        // means the two can never drift apart.
        Apply();
    }

    void OnDestroy()
    {
        if (levelManager != null) levelManager.LevelStarted -= OnLevelStarted;
    }

    // ======================================================================

    /// <summary>
    /// Hands over the rifle the level is owed.
    ///
    /// Taken from the level number rather than counted, so it is idempotent: the level
    /// loop restarts itself if it is ever lost, and a repeat of this must not be able to
    /// hand out a second upgrade for the same level.
    /// </summary>
    void OnLevelStarted(LevelManager source)
    {
        if (source == null) return;

        int steps = Mathf.Max(0, source.LevelNumber - 1);
        if (steps == Steps && _announced) return;

        Steps = steps;
        Apply();

        if (steps <= 0) return;

        if (!_announced)
        {
            if (upgradeClip != null && audioSource != null) audioSource.PlayOneShot(upgradeClip);
            Upgraded?.Invoke(this, source.LevelNumber);
        }

        _announced = true;
    }

    /// <summary>
    /// Hands the composed rifle to the weapon: what was bought, times what the level
    /// gives. Public and absolute, because PlayerLoadout calls it too -- the two run in
    /// an order neither of them controls, and an absolute call cannot double-apply.
    ///
    /// The caps are on the level's half only. A store upgrade the player paid coins for
    /// must not be silently swallowed by a ceiling that exists to stop the *level* curve
    /// running away with itself.
    /// </summary>
    public void Apply()
    {
        if (weapon == null) return;

        int magazine = Mathf.Min(maxMagazineBonus, magazineBonusPerLevel * Steps);

        float damage = Mathf.Min(maxDamageMultiplier, 1f + damageBonusPerLevel * Steps);

        float reload = Mathf.Max(minReloadMultiplier, 1f - reloadSpeedBonusPerLevel * Steps);

        float fireRate = 1f;

        if (loadout != null)
        {
            magazine += loadout.StoreMagazineBonus;
            damage *= loadout.StoreDamageMultiplier;
            reload *= loadout.StoreReloadMultiplier;
            fireRate = loadout.StoreFireRateMultiplier;
        }

        weapon.ApplyUpgrades(magazine, damage, reload, fireRate, topUpMagazine: true);

        Summary = Steps <= 0
            ? ""
            : $"MAG {weapon.MagazineSize}   DMG +{(damage - 1f) * 100f:0}%   RELOAD {weapon.ReloadTime:0.0}s";
    }
}
