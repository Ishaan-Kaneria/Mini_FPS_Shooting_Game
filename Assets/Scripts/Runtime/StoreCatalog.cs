using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Everything the store sells and what it costs, as data rather than as a hard-coded
/// shop.
///
/// The same extension point the rest of the kit uses. A seventh gun is a
/// <see cref="WeaponData"/> asset and an entry in this list; a second bomb is a
/// <see cref="BombData"/> and an entry. Neither needs a line of <see cref="StorePanel"/>,
/// because the store clones a card per entry at runtime the way the dashboard clones a
/// card per arena.
///
/// Every price that escalates escalates here. Upgrades are the one place a shop can
/// accidentally hand out a free lunch -- a flat price means the tenth upgrade costs what
/// the first did, and by then coins are not scarce -- so the cost of the next level is
/// always <c>base * growth^level</c> and the growth is on the entry rather than in code.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/Store Catalog", fileName = "Store")]
public class StoreCatalog : ScriptableObject
{
    /// <summary>
    /// The pricing curve shared by everything upgradeable. Held as a type rather than
    /// as three loose fields so a new upgradeable thing cannot quietly invent its own
    /// escalation, which is how a shop ends up with one item everybody buys.
    /// </summary>
    [Serializable]
    public class UpgradeCurve
    {
        [Tooltip("What the first upgrade costs.")]
        [Min(1)] public int basePrice = 300;

        [Tooltip("What each upgrade multiplies the last one's price by. At 1.6 the fifth " +
                 "upgrade costs about six and a half times the first, which is what keeps " +
                 "a maxed gun something you saved for rather than something you drifted into.")]
        [Range(1.05f, 3f)] public float growth = 1.6f;

        [Tooltip("How many times this can be upgraded. 0 means no limit, which is only " +
                 "right for health.")]
        [Min(0)] public int maxLevel = 5;

        public bool IsCapped => maxLevel > 0;

        public bool CanUpgrade(int level) => !IsCapped || level < maxLevel;

        /// <summary>
        /// What it costs to go from <paramref name="level"/> to the next one. Returns 0
        /// at the cap, which callers must read as "not for sale" rather than as "free".
        /// </summary>
        public int PriceAt(int level)
        {
            if (!CanUpgrade(level)) return 0;

            return Mathf.Max(1, Mathf.RoundToInt(basePrice * Mathf.Pow(growth, Mathf.Max(0, level))));
        }
    }

    [Serializable]
    public class GunEntry
    {
        [Tooltip("Stable key for stored ownership and upgrades. Renaming it forgets " +
                 "every purchase of this gun, so treat it as permanent.")]
        public string id = "rifle";

        public string displayName = "Rifle";

        [TextArea(2, 3)] public string description;

        public WeaponData data;

        [Tooltip("Coins to unlock it. The starter gun is free and owned from the start.")]
        [Min(0)] public int price = 1200;

        [Tooltip("Owned without buying. Exactly one gun should have this, or the player " +
                 "can reach the store with nothing to take into a level.")]
        public bool ownedFromStart;

        public UpgradeCurve upgrades = new UpgradeCurve();

        [Header("What one upgrade does")]
        [Tooltip("Fraction added to damage per upgrade level.")]
        [Range(0f, 0.5f)] public float damagePerLevel = 0.12f;

        [Tooltip("Fraction added to rounds per minute per upgrade level -- the bullets " +
                 "per second the player actually feels.")]
        [Range(0f, 0.5f)] public float fireRatePerLevel = 0.09f;

        [Tooltip("Rounds added to the magazine per upgrade level.")]
        [Min(0)] public int magazinePerLevel = 3;

        [Tooltip("Fraction shaved off the reload per upgrade level.")]
        [Range(0f, 0.3f)] public float reloadPerLevel = 0.05f;

        public string Label => string.IsNullOrWhiteSpace(displayName) ? id : displayName;
    }

    [Serializable]
    public class BombEntry
    {
        [Tooltip("Stable key for stored ownership and upgrades. Permanent, like a gun's.")]
        public string id = "frag";

        public string displayName = "Frag Bomb";

        [TextArea(2, 3)] public string description;

        public BombData data;

        [Min(0)] public int price = 900;

        public bool ownedFromStart;

        [Tooltip("The explosive the story hands over when the campaign unlocks bombs. " +
                 "Exactly one bomb should carry this. It is not the same as owned from " +
                 "the start: the player begins with no bomb at all and is given this one " +
                 "on reaching the star gate, so that the first bomb is something the " +
                 "story grants rather than something the shop sells.")]
        public bool grantedByStory;

        public UpgradeCurve upgrades = new UpgradeCurve();

        [Header("What one upgrade does")]
        [Range(0f, 0.5f)] public float damagePerLevel = 0.15f;

        [Tooltip("Metres added to the blast radius per upgrade level. A bigger radius is " +
                 "worth more than more damage on a crowd, so this moves slowly.")]
        [Range(0f, 3f)] public float radiusPerLevel = 0.7f;

        [Tooltip("Extra bombs per level, awarded every this many upgrades. At 2 a maxed " +
                 "bomb carries two more charges than a fresh one.")]
        [Min(1)] public int upgradesPerExtraCharge = 2;

        public string Label => string.IsNullOrWhiteSpace(displayName) ? id : displayName;

        /// <summary>Charges a bomb at this upgrade level carries into a level.</summary>
        public int ChargesAt(int level)
        {
            int baseCharges = data != null ? data.chargesPerLevel : 2;
            return baseCharges + Mathf.Max(0, level) / Mathf.Max(1, upgradesPerExtraCharge);
        }
    }

    [Serializable]
    public class ConsumableEntry
    {
        [Tooltip("Stable key for the stored count. Permanent.")]
        public string id = "energy";

        public ConsumableData data;

        [Tooltip("Coins for one pack.")]
        [Min(0)] public int price = 220;

        [Tooltip("How many the pack contains, so the store is not a clicking exercise.")]
        [Min(1)] public int packSize = 3;

        [Tooltip("Most the player may carry. A ceiling is what stops a rich player " +
                 "walking into level one holding forty heals.")]
        [Min(1)] public int maxCarried = 9;

        public string Label => data != null && !string.IsNullOrWhiteSpace(data.displayName)
            ? data.displayName
            : id;
    }

    [Header("Stock")]
    public List<GunEntry> guns = new List<GunEntry>();
    public List<BombEntry> bombs = new List<BombEntry>();
    public List<ConsumableEntry> consumables = new List<ConsumableEntry>();

    [Header("Health")]
    [Tooltip("Uncapped on purpose -- leave Max Level at 0. Health is the upgrade a " +
             "player who is stuck can always put coins into, which is what keeps a hard " +
             "level from being a wall with nothing to do about it. The price curve is " +
             "what stops that being free.")]
    public UpgradeCurve healthUpgrades = new UpgradeCurve { basePrice = 250, growth = 1.45f, maxLevel = 0 };

    [Tooltip("Maximum health added per upgrade level.")]
    [Min(1f)] public float healthPerLevel = 12f;

    [Tooltip("Maximum shield added per upgrade level.")]
    [Min(0f)] public float shieldPerLevel = 5f;

    // ======================================================================
    public GunEntry FindGun(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        foreach (var gun in guns)
            if (gun != null && gun.id == id) return gun;

        return null;
    }

    public BombEntry FindBomb(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        foreach (var bomb in bombs)
            if (bomb != null && bomb.id == id) return bomb;

        return null;
    }

    public ConsumableEntry FindConsumable(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        foreach (var item in consumables)
            if (item != null && item.id == id) return item;

        return null;
    }

    /// <summary>
    /// The gun the player has if they have never bought anything. Falls back to the
    /// first entry, because a store with no free gun would leave a new player unable to
    /// take anything into a level -- and that failure looks like a broken arena rather
    /// than like an empty wallet.
    /// </summary>
    public GunEntry StarterGun
    {
        get
        {
            foreach (var gun in guns)
                if (gun != null && gun.ownedFromStart && gun.data != null) return gun;

            foreach (var gun in guns)
                if (gun != null && gun.data != null) return gun;

            return null;
        }
    }

    /// <summary>The bomb owned from the start, or null when every bomb has to be bought.</summary>
    public BombEntry StarterBomb
    {
        get
        {
            foreach (var bomb in bombs)
                if (bomb != null && bomb.ownedFromStart && bomb.data != null) return bomb;

            return null;
        }
    }

    /// <summary>
    /// The bomb the campaign hands over at its star gate, or null when none is marked.
    /// <see cref="Campaign.RefreshUnlocks"/> reads this rather than naming an id, so
    /// which explosive the story gives stays a decision made in the catalogue.
    /// </summary>
    public BombEntry StoryBomb
    {
        get
        {
            foreach (var bomb in bombs)
                if (bomb != null && bomb.grantedByStory && bomb.data != null) return bomb;

            return null;
        }
    }
}
