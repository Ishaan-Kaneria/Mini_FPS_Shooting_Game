using UnityEngine;

/// <summary>
/// Turns what the player bought into the player they are about to play as: the gun in
/// their hands, the bomb on their belt, the drinks in their pocket and the health they
/// upgraded.
///
/// One component does all four because they share a single failure mode. Every one of
/// them is a store purchase that has to survive a scene load, and a purchase that is
/// applied in four different places is a purchase that is eventually applied in three.
///
/// It is the seam between <see cref="Loadout"/>, which is PlayerPrefs, and the
/// components that do the work. Nothing below it knows the store exists: the weapon is
/// handed a <see cref="WeaponData"/>, the thrower a <see cref="BombData"/>, and neither
/// has to care where it came from -- which is what keeps a hand-built scene with no
/// dashboard in front of it perfectly playable.
/// </summary>
[DisallowMultipleComponent]
public class PlayerLoadout : MonoBehaviour
{
    [Header("Content")]
    [Tooltip("What the store sells. Wired by the scene builder. Without one the player " +
             "keeps whatever the builder put in their hands, which is the starter rifle.")]
    public StoreCatalog catalog;

    [Header("Wiring")]
    [Tooltip("Found below this object when empty.")]
    public Weapon weapon;

    [Tooltip("Found on this object when empty.")]
    public Health health;

    [Tooltip("Found on this object or below it when empty.")]
    public BombThrower bombs;

    public ConsumableBelt belt;

    [Tooltip("Told to recompute once the gun is in hand, because the two run in an " +
             "order neither of them controls.")]
    public PlayerProgression progression;

    [Header("Base Health")]
    [Tooltip("The player's health before any upgrade is added. Held here rather than " +
             "read off the Health component, because Health has already filled its pool " +
             "from maxHealth by the time this runs -- reading it back would compound the " +
             "upgrade every time a level reloaded.")]
    [Min(1f)] public float baseHealth = 100f;

    [Min(0f)] public float baseShield = 50f;

    // ======================================================================

    /// <summary>The gun being carried, or null when there is no catalog.</summary>
    public StoreCatalog.GunEntry Gun { get; private set; }

    /// <summary>The bomb being carried, or null when the player owns none.</summary>
    public StoreCatalog.BombEntry Bomb { get; private set; }

    /// <summary>The consumable on the belt, or null when the store sells none.</summary>
    public StoreCatalog.ConsumableEntry Consumable { get; private set; }

    /// <summary>Rounds the bought upgrades add to the magazine.</summary>
    public int StoreMagazineBonus { get; private set; }

    /// <summary>Damage multiplier from the bought upgrades. 1 on an unupgraded gun.</summary>
    public float StoreDamageMultiplier { get; private set; } = 1f;

    /// <summary>Reload multiplier from the bought upgrades. Below 1 is faster.</summary>
    public float StoreReloadMultiplier { get; private set; } = 1f;

    /// <summary>Fire rate multiplier from the bought upgrades. Above 1 is faster.</summary>
    public float StoreFireRateMultiplier { get; private set; } = 1f;

    void Awake()
    {
        if (weapon == null) weapon = GetComponentInChildren<Weapon>();
        if (health == null) health = GetComponent<Health>();
        if (bombs == null) bombs = GetComponentInChildren<BombThrower>();
        if (belt == null) belt = GetComponentInChildren<ConsumableBelt>();
        if (progression == null) progression = GetComponent<PlayerProgression>();
    }

    void Start() => Apply();

    /// <summary>
    /// Reads every purchase and hands it to whatever does the work. Public and
    /// idempotent, so the store can be left open in a second window and a level
    /// restarted without this having to be the first thing that runs.
    /// </summary>
    public void Apply()
    {
        ApplyHealth();
        ApplyGun();
        ApplyBomb();
        ApplyBelt();

        // Last, and explicitly. PlayerProgression composes the store's multipliers with
        // the level's, so it has to run after the gun is in hand -- and which of the two
        // Start methods Unity calls first is not defined, so the order is stated here
        // rather than hoped for. Apply is absolute, so its own Start calling it again is
        // harmless.
        if (progression != null) progression.Apply();
    }

    // ======================================================================
    void ApplyHealth()
    {
        if (health == null || catalog == null) return;

        int level = Loadout.HealthUpgradeLevel;
        if (level <= 0) return;

        // Refilled, because this runs at the start of a level and a bigger pool handed
        // over part-empty is an upgrade the player paid for and did not get.
        health.SetMaxHealth(baseHealth + catalog.healthPerLevel * level);

        if (baseShield > 0f || catalog.shieldPerLevel > 0f)
            health.SetMaxShield(baseShield + catalog.shieldPerLevel * level);
    }

    void ApplyGun()
    {
        StoreMagazineBonus = 0;
        StoreDamageMultiplier = 1f;
        StoreReloadMultiplier = 1f;
        StoreFireRateMultiplier = 1f;

        if (catalog == null || weapon == null) return;

        Gun = Loadout.SelectedGun(catalog);
        if (Gun == null || Gun.data == null) return;

        weapon.Equip(Gun.data);

        int level = Mathf.Min(Loadout.GunUpgradeLevel(Gun.id),
                              Gun.upgrades.IsCapped ? Gun.upgrades.maxLevel : int.MaxValue);

        if (level <= 0) return;

        StoreMagazineBonus = Gun.magazinePerLevel * level;
        StoreDamageMultiplier = 1f + Gun.damagePerLevel * level;
        StoreFireRateMultiplier = 1f + Gun.fireRatePerLevel * level;

        // Floored well above zero: five upgrades at five percent each is a quarter off,
        // and a reload that can be driven toward nothing stops being part of the game.
        StoreReloadMultiplier = Mathf.Max(0.4f, 1f - Gun.reloadPerLevel * level);
    }

    void ApplyBomb()
    {
        if (bombs == null) return;

        if (catalog == null)
        {
            // No catalog: leave whatever the builder wired, which is a working bomb.
            return;
        }

        Bomb = Loadout.SelectedBomb(catalog);

        if (Bomb == null || Bomb.data == null)
        {
            // Owning no bomb is a real state, not a failure. The thrower goes quiet and
            // the HUD hides its counter rather than offering a key that does nothing.
            bombs.Equip(null, 0);
            return;
        }

        int level = Mathf.Min(Loadout.BombUpgradeLevel(Bomb.id),
                              Bomb.upgrades.IsCapped ? Bomb.upgrades.maxLevel : int.MaxValue);

        bombs.Equip(Bomb.data, Bomb.ChargesAt(level));

        // The upgraded numbers are pushed onto the thrower rather than written into the
        // BombData asset, for exactly the reason Weapon never writes to WeaponData: the
        // asset is one shared instance, and a bonus written into it would still be there
        // after quitting and would compound on every restart.
        bombs.damageMultiplier = 1f + Bomb.damagePerLevel * level;
        bombs.radiusBonus = Bomb.radiusPerLevel * level;
    }

    void ApplyBelt()
    {
        if (belt == null || catalog == null) return;

        Consumable = Loadout.SelectedConsumable(catalog);

        if (Consumable == null || Consumable.data == null)
        {
            belt.Equip(null, "");
            return;
        }

        belt.Equip(Consumable.data, Consumable.id);
    }
}
