using UnityEngine;

/// <summary>
/// What the player owns, what they have upgraded, and what they are taking into the
/// next level.
///
/// Static accessors over PlayerPrefs, like <see cref="Wallet"/> and
/// <see cref="LevelProgress"/>. Keyed by the store entry's <c>id</c> rather than by its
/// position in the catalog, so reordering the shop or inserting a gun in the middle of
/// it cannot hand somebody a weapon they did not buy.
///
/// Nothing here spends coins. <see cref="StorePanel"/> takes the money through
/// <see cref="Wallet.TrySpend"/> and only then records the purchase, so an item can
/// never be granted by a call that failed to pay for it.
/// </summary>
public static class Loadout
{
    const string SelectedGunKey = "FPSKit.Loadout.Gun";
    const string SelectedBombKey = "FPSKit.Loadout.Bomb";
    const string SelectedItemKey = "FPSKit.Loadout.Item";
    const string HealthLevelKey = "FPSKit.Upgrade.Health";

    static string GunOwnedKey(string id) => $"FPSKit.Own.Gun.{id}";
    static string GunLevelKey(string id) => $"FPSKit.Upgrade.Gun.{id}";
    static string BombOwnedKey(string id) => $"FPSKit.Own.Bomb.{id}";
    static string BombLevelKey(string id) => $"FPSKit.Upgrade.Bomb.{id}";
    static string StockKey(string id) => $"FPSKit.Stock.{id}";

    // ======================================================================
    // Guns
    // ======================================================================

    /// <summary>
    /// True when the gun is in the player's hands to use. The starter gun answers true
    /// without anything being stored, which is what makes a fresh profile playable --
    /// the alternative is granting it on first launch, and a grant that has to run once
    /// is a grant that eventually does not.
    /// </summary>
    public static bool OwnsGun(StoreCatalog.GunEntry gun)
        => gun != null && (gun.ownedFromStart || PlayerPrefs.GetInt(GunOwnedKey(gun.id), 0) == 1);

    public static void GrantGun(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        PlayerPrefs.SetInt(GunOwnedKey(id), 1);
        PlayerPrefs.Save();
    }

    public static int GunUpgradeLevel(string id)
        => string.IsNullOrEmpty(id) ? 0 : Mathf.Max(0, PlayerPrefs.GetInt(GunLevelKey(id), 0));

    public static void SetGunUpgradeLevel(string id, int level)
    {
        if (string.IsNullOrEmpty(id)) return;

        PlayerPrefs.SetInt(GunLevelKey(id), Mathf.Max(0, level));
        PlayerPrefs.Save();
    }

    /// <summary>
    /// The gun the next level is played with. Falls back to the starter rather than to
    /// nothing, and repairs a stored id that names a gun the player does not own -- a
    /// profile written by an older catalog would otherwise send them in unarmed.
    /// </summary>
    public static StoreCatalog.GunEntry SelectedGun(StoreCatalog catalog)
    {
        if (catalog == null) return null;

        var stored = catalog.FindGun(PlayerPrefs.GetString(SelectedGunKey, ""));
        if (stored != null && OwnsGun(stored) && stored.data != null) return stored;

        return catalog.StarterGun;
    }

    public static void SelectGun(string id)
    {
        PlayerPrefs.SetString(SelectedGunKey, id ?? "");
        PlayerPrefs.Save();
    }

    // ======================================================================
    // Bombs
    // ======================================================================
    public static bool OwnsBomb(StoreCatalog.BombEntry bomb)
        => bomb != null && (bomb.ownedFromStart || PlayerPrefs.GetInt(BombOwnedKey(bomb.id), 0) == 1);

    public static void GrantBomb(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        PlayerPrefs.SetInt(BombOwnedKey(id), 1);
        PlayerPrefs.Save();
    }

    public static int BombUpgradeLevel(string id)
        => string.IsNullOrEmpty(id) ? 0 : Mathf.Max(0, PlayerPrefs.GetInt(BombLevelKey(id), 0));

    public static void SetBombUpgradeLevel(string id, int level)
    {
        if (string.IsNullOrEmpty(id)) return;

        PlayerPrefs.SetInt(BombLevelKey(id), Mathf.Max(0, level));
        PlayerPrefs.Save();
    }

    /// <summary>
    /// The bomb the next level is played with, or null when the player owns none. Null
    /// is a real answer here, unlike a gun: going in without a bomb is allowed, and
    /// BombThrower hides itself when it happens.
    /// </summary>
    public static StoreCatalog.BombEntry SelectedBomb(StoreCatalog catalog)
    {
        if (catalog == null) return null;

        var stored = catalog.FindBomb(PlayerPrefs.GetString(SelectedBombKey, ""));
        if (stored != null && OwnsBomb(stored) && stored.data != null) return stored;

        // Not "the first bomb": an unowned one would arm a player who never bought it.
        foreach (var bomb in catalog.bombs)
            if (OwnsBomb(bomb) && bomb.data != null) return bomb;

        return null;
    }

    public static void SelectBomb(string id)
    {
        PlayerPrefs.SetString(SelectedBombKey, id ?? "");
        PlayerPrefs.Save();
    }

    // ======================================================================
    // Consumables
    // ======================================================================

    /// <summary>How many of a consumable the player is carrying.</summary>
    public static int Stock(string id)
        => string.IsNullOrEmpty(id) ? 0 : Mathf.Max(0, PlayerPrefs.GetInt(StockKey(id), 0));

    /// <summary>
    /// What is on the belt for the next level. Falls back to the first consumable the
    /// player actually has some of, and then to the first one the store sells -- so a
    /// belt is never empty because of a stale stored id, and a player who has bought
    /// nothing still sees the counter they are about to fill.
    /// </summary>
    public static StoreCatalog.ConsumableEntry SelectedConsumable(StoreCatalog catalog)
    {
        if (catalog == null || catalog.consumables.Count == 0) return null;

        var stored = catalog.FindConsumable(PlayerPrefs.GetString(SelectedItemKey, ""));
        if (stored != null && stored.data != null) return stored;

        foreach (var item in catalog.consumables)
            if (item != null && item.data != null && Stock(item.id) > 0) return item;

        foreach (var item in catalog.consumables)
            if (item != null && item.data != null) return item;

        return null;
    }

    public static void SelectConsumable(string id)
    {
        PlayerPrefs.SetString(SelectedItemKey, id ?? "");
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Adds to the carried count, clamped to the entry's ceiling, and returns how many
    /// actually fitted. The store reads that return value so it can refuse a purchase
    /// that would be thrown away -- a pack bought into a full belt is coins for nothing.
    /// </summary>
    public static int AddStock(StoreCatalog.ConsumableEntry entry, int amount)
    {
        if (entry == null || amount <= 0) return 0;

        int before = Stock(entry.id);
        int after = Mathf.Clamp(before + amount, 0, Mathf.Max(1, entry.maxCarried));

        if (after == before) return 0;

        PlayerPrefs.SetInt(StockKey(entry.id), after);
        PlayerPrefs.Save();

        return after - before;
    }

    /// <summary>
    /// Spends one, and says whether there was one to spend. Called mid-level, so it
    /// writes through immediately: a drink used and then a browser tab closed must not
    /// come back.
    /// </summary>
    public static bool TryUseStock(string id)
    {
        int held = Stock(id);
        if (held <= 0) return false;

        PlayerPrefs.SetInt(StockKey(id), held - 1);
        PlayerPrefs.Save();

        return true;
    }

    // ======================================================================
    // Health
    // ======================================================================
    public static int HealthUpgradeLevel => Mathf.Max(0, PlayerPrefs.GetInt(HealthLevelKey, 0));

    public static void SetHealthUpgradeLevel(int level)
    {
        PlayerPrefs.SetInt(HealthLevelKey, Mathf.Max(0, level));
        PlayerPrefs.Save();
    }

    // ======================================================================

    /// <summary>
    /// Wipes every purchase the catalog knows about. Behind a confirm on the store, and
    /// used by the tests to put a machine back the way they found it. Takes the catalog
    /// rather than guessing at keys, so an id that is no longer sold is left alone
    /// instead of being silently orphaned.
    /// </summary>
    public static void Clear(StoreCatalog catalog)
    {
        PlayerPrefs.DeleteKey(SelectedGunKey);
        PlayerPrefs.DeleteKey(SelectedBombKey);
        PlayerPrefs.DeleteKey(SelectedItemKey);
        PlayerPrefs.DeleteKey(HealthLevelKey);

        if (catalog != null)
        {
            foreach (var gun in catalog.guns)
            {
                if (gun == null) continue;

                PlayerPrefs.DeleteKey(GunOwnedKey(gun.id));
                PlayerPrefs.DeleteKey(GunLevelKey(gun.id));
            }

            foreach (var bomb in catalog.bombs)
            {
                if (bomb == null) continue;

                PlayerPrefs.DeleteKey(BombOwnedKey(bomb.id));
                PlayerPrefs.DeleteKey(BombLevelKey(bomb.id));
            }

            foreach (var item in catalog.consumables)
                if (item != null) PlayerPrefs.DeleteKey(StockKey(item.id));
        }

        PlayerPrefs.Save();
    }
}
