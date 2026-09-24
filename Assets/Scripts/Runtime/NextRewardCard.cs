using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// NEXT REWARD: what the player is working towards.
///
/// The next rank's unlock when there is one (<see cref="PlayerRank.UnlockAt"/>). There are
/// none yet, so it falls back, honestly, to the store: the cheapest gun or bomb the player
/// does not own and can afford now, or failing that the cheapest they are saving for, with a
/// bar of their coins against its price. With every gun and bomb owned, it is the next
/// upgrade to the equipped gun, and after that the next health upgrade, which never runs out.
/// </summary>
public class NextRewardCard : MonoBehaviour
{
    public TMP_Text headingText;
    public Image icon;
    public TMP_Text nameText;
    public TMP_Text detailText;
    public UIProgressBar bar;
    public TMP_Text barText;
    public FlatButton actionButton;

    /// <summary>What the card is showing, for the button: "store" or "" for a rank unlock.</summary>
    public string Target { get; private set; } = "";

    public void Bind(StoreCatalog catalog)
    {
        var t = UITheme.Active;
        int rank = PlayerRank.Rank;
        string unlock = PlayerRank.UnlockAt(rank + 1);
        if (!string.IsNullOrEmpty(unlock))
        {
            PlayerRank.Progress(out int into, out int span);
            Show(t, "NEXT REWARD", "trophy", unlock, $"RANK {rank + 1} · {PlayerRank.TitleFor(rank + 1).ToUpperInvariant()}",
                 into, span, t.Tabular($"{into:N0} / {span:N0}", false) + " XP", null);
            return;
        }

        int balance = Wallet.Balance;
        string name = null, iconId = "shopping-cart", kind = "";
        int price = 0;

        if (catalog != null)
        {
            // Guns and bombs the player does not own: cheapest affordable, else cheapest.
            int bestAffordable = int.MaxValue, bestAny = int.MaxValue;
            string affordableName = null, anyName = null, affordableIcon = null, anyIcon = null, affordableKind = null, anyKind = null;
            void Consider(string n, int p, string ic, string k)
            {
                if (p <= balance && p < bestAffordable) { bestAffordable = p; affordableName = n; affordableIcon = ic; affordableKind = k; }
                if (p < bestAny) { bestAny = p; anyName = n; anyIcon = ic; anyKind = k; }
            }
            foreach (var g in catalog.guns)
                if (g != null && !Loadout.OwnsGun(g)) Consider(g.Label, g.price, "rifle", "WEAPON");
            foreach (var b in catalog.bombs)
                if (b != null && !Loadout.OwnsBomb(b) && !b.grantedByStory) Consider(b.Label, b.price, "grenade", "EXPLOSIVE");

            if (affordableName != null) { name = affordableName; price = bestAffordable; iconId = affordableIcon; kind = affordableKind; }
            else if (anyName != null) { name = anyName; price = bestAny; iconId = anyIcon; kind = anyKind; }
            else
            {
                var gun = Loadout.SelectedGun(catalog);
                int level = gun != null ? Loadout.GunUpgradeLevel(gun.id) : 0;
                if (gun != null && gun.upgrades.CanUpgrade(level))
                {
                    name = $"{gun.Label} upgrade {level + 1}";
                    price = gun.upgrades.PriceAt(level);
                    iconId = "rifle";
                    kind = "UPGRADE";
                }
                else
                {
                    int h = Loadout.HealthUpgradeLevel;
                    name = $"Health upgrade {h + 1}";
                    price = catalog.healthUpgrades.PriceAt(h);
                    iconId = "heart";
                    kind = "UPGRADE";
                }
            }
        }

        if (name == null)
        {
            Show(t, "NEXT REWARD", "shopping-cart", "Nothing to buy", "", 0, 1, "", null);
            return;
        }

        bool afford = balance >= price;
        string detail = kind + UIText.Separator + t.Tabular(Wallet.Format(price), false) + " COINS";
        string barLabel = afford
            ? "YOU CAN AFFORD THIS"
            : t.Tabular($"{Wallet.Format(balance)} / {Wallet.Format(price)}", false);
        Show(t, "NEXT IN STORE", iconId, name, detail, Mathf.Min(balance, price), Mathf.Max(1, price), barLabel, "store");
        if (barText != null) barText.color = afford ? t.success : t.textSecondary;
    }

    void Show(UITheme t, string heading, string iconId, string name, string detail, int value, int max,
              string barLabel, string target)
    {
        Target = target ?? "";
        if (headingText != null) headingText.text = heading;
        if (icon != null) { icon.sprite = t.IconSprite(iconId); icon.color = t.textPrimary; }
        if (nameText != null) nameText.text = name.ToUpperInvariant();
        if (detailText != null) detailText.text = detail;
        if (bar != null)
        {
            bar.Value = max > 0 ? value / (float)max : 0f;
            bar.Snap();
        }
        if (barText != null) { barText.text = barLabel; barText.color = t.textSecondary; }
        if (actionButton != null) actionButton.gameObject.SetActive(Target == "store");
    }
}
