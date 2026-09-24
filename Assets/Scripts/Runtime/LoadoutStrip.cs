using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// YOUR LOADOUT: what the player will carry into the next level -- primary, secondary,
/// grenades and medkits -- read from <see cref="Loadout"/> exactly as
/// <see cref="PlayerLoadout"/> will read it when the level starts, so the strip cannot show
/// a gun the player will not be holding.
///
/// <b>The secondary slot is shown empty</b>, because the game has no secondary weapon: one
/// gun is carried. An empty slot says that truthfully; leaving the slot out would make the
/// strip disagree with every other shooter's layout for no visible reason, and filling it
/// with a second owned gun would promise a weapon switch that does not exist.
///
/// Grenades are the bomb (the campaign's first reward, so locked until then) at the charges
/// its upgrade level gives; medkits are the drink on the belt and how many are carried.
/// </summary>
public class LoadoutStrip : MonoBehaviour
{
    [System.Serializable]
    public class Slot
    {
        public Image icon;
        public TMP_Text kind;
        public TMP_Text item;
        public TMP_Text count;
    }

    public Slot primary = new Slot();
    public Slot secondary = new Slot();
    public Slot grenades = new Slot();
    public Slot medkits = new Slot();
    public FlatButton customizeButton;

    public void Bind(StoreCatalog catalog)
    {
        var t = UITheme.Active;

        var gun = catalog != null ? Loadout.SelectedGun(catalog) : null;
        Fill(t, primary, "rifle", "PRIMARY", gun != null ? gun.Label : "None", null, gun != null);

        Fill(t, secondary, "rifle", "SECONDARY", "Empty", null, false);

        var bomb = catalog != null ? Loadout.SelectedBomb(catalog) : null;
        bool bombEarned = Campaign.HasPower(Campaign.BombPower);
        if (bomb != null && bomb.data != null && bombEarned)
        {
            int level = Mathf.Min(Loadout.BombUpgradeLevel(bomb.id), bomb.upgrades.IsCapped ? bomb.upgrades.maxLevel : int.MaxValue);
            Fill(t, grenades, "grenade", "GRENADES", bomb.Label, "x" + bomb.ChargesAt(level), true);
        }
        else
        {
            Fill(t, grenades, "grenade", "GRENADES", bombEarned ? "None" : "Locked", null, false);
        }

        var drink = catalog != null ? Loadout.SelectedConsumable(catalog) : null;
        int stock = drink != null ? Loadout.Stock(drink.id) : 0;
        Fill(t, medkits, "medkit", "MEDKITS", drink != null ? drink.Label : "None", drink != null ? "x" + stock : null, stock > 0);
    }

    static void Fill(UITheme t, Slot s, string iconId, string kind, string item, string count, bool live)
    {
        if (s == null) return;
        if (s.icon != null) { s.icon.sprite = t.IconSprite(iconId); s.icon.color = live ? t.textPrimary : t.textDisabled; }
        if (s.kind != null) s.kind.text = kind;
        if (s.item != null) { s.item.text = item.ToUpperInvariant(); s.item.color = live ? t.textPrimary : t.textDisabled; }
        if (s.count != null)
        {
            s.count.text = count != null ? t.Tabular(count) : "";
            s.count.color = t.accent;
        }
    }
}
