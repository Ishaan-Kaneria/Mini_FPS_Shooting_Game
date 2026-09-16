using UnityEngine;

/// <summary>
/// The player's coins, and the only thing allowed to move them.
///
/// Static accessors over PlayerPrefs, the same shape as <see cref="LevelProgress"/> and
/// <see cref="PlayerProfile"/>: there is no state to go stale, nothing to reset between
/// play sessions, and a coin banked here is on disk before the results screen has
/// finished animating. That matters on the web, where the tab can be closed at any
/// moment and there is no shutdown to hook.
///
/// Everything that spends goes through <see cref="TrySpend"/>, which is the only method
/// that can lower the balance and refuses rather than going negative. A store that
/// checked the price itself and then subtracted would be two places that have to agree
/// about affordability, and the second one to be written is the one that gets it wrong.
/// </summary>
public static class Wallet
{
    const string BalanceKey = "FPSKit.Coins";
    const string EarnedKey = "FPSKit.CoinsEarned";

    /// <summary>Coins in hand right now.</summary>
    public static int Balance => Mathf.Max(0, PlayerPrefs.GetInt(BalanceKey, 0));

    /// <summary>
    /// Everything ever earned, which is what the dashboard's record panel shows. It
    /// never goes down, so it is a measure of how much has been played where the balance
    /// is only a measure of what has not been spent yet.
    /// </summary>
    public static int LifetimeEarned => PlayerPrefs.GetInt(EarnedKey, 0);

    public static bool CanAfford(int price) => price <= 0 || Balance >= price;

    /// <summary>Banks coins. Negative or zero amounts are ignored rather than stealing.</summary>
    public static void Add(int amount)
    {
        if (amount <= 0) return;

        PlayerPrefs.SetInt(BalanceKey, Balance + amount);
        PlayerPrefs.SetInt(EarnedKey, LifetimeEarned + amount);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Takes coins if there are enough, and says whether it did. The caller must act on
    /// the answer: a purchase that ignores a false here is a free item.
    /// </summary>
    public static bool TrySpend(int price)
    {
        if (price <= 0) return true;
        if (Balance < price) return false;

        PlayerPrefs.SetInt(BalanceKey, Balance - price);
        PlayerPrefs.Save();

        return true;
    }

    /// <summary>
    /// Sets the balance outright. For a debug menu and for a test putting back what it
    /// borrowed -- never for gameplay, which earns and spends.
    /// </summary>
    public static void SetBalance(int amount)
    {
        PlayerPrefs.SetInt(BalanceKey, Mathf.Max(0, amount));
        PlayerPrefs.Save();
    }

    public static void Clear()
    {
        PlayerPrefs.DeleteKey(BalanceKey);
        PlayerPrefs.DeleteKey(EarnedKey);
        PlayerPrefs.Save();
    }

    /// <summary>"1,240" -- one place, so every screen spells a balance the same way.</summary>
    public static string Format(int amount) => amount.ToString("N0");
}
