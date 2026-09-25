using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Whether the store has something the player can now afford that they have not been shown:
/// the small amber dot on the STORE tab.
///
/// "New" means an item the player does not own, can afford, and was not affordable-and-unowned
/// the last time they opened the store. Kept as the set of ids they saw, in PlayerPrefs, so the
/// dot survives a restart and goes the moment they look. Accessors over PlayerPrefs with
/// nothing cached, like Wallet, so there is no static to go stale between play sessions.
/// </summary>
public static class StoreAlerts
{
    const string Key = "FPSKit.Store.SeenAffordable";

    public static bool HasNew(StoreCatalog catalog)
    {
        if (catalog == null) return false;
        var seen = Seen();
        foreach (var id in Affordable(catalog)) if (!seen.Contains(id)) return true;
        return false;
    }

    public static void MarkSeen(StoreCatalog catalog)
    {
        if (catalog == null) return;
        var seen = Seen();
        foreach (var id in Affordable(catalog)) seen.Add(id);
        PlayerPrefs.SetString(Key, string.Join(",", seen));
        PlayerPrefs.Save();
    }

    static HashSet<string> Seen()
    {
        var set = new HashSet<string>();
        foreach (var id in PlayerPrefs.GetString(Key, "").Split(','))
            if (!string.IsNullOrEmpty(id)) set.Add(id);
        return set;
    }

    static IEnumerable<string> Affordable(StoreCatalog catalog)
    {
        foreach (var g in catalog.guns)
            if (g != null && g.price > 0 && !Loadout.OwnsGun(g) && Wallet.CanAfford(g.price)) yield return "gun." + g.id;
        if (Campaign.HasPower(Campaign.BombPower))
            foreach (var b in catalog.bombs)
                if (b != null && b.price > 0 && !Loadout.OwnsBomb(b) && Wallet.CanAfford(b.price)) yield return "bomb." + b.id;
    }
}
