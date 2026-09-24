using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// LOADOUT: choose what to carry from what is owned -- the gun, the bomb, the drink. Buying
/// stays in the store; this screen only equips, and says where to get what is missing.
///
/// Built at runtime from the kit onto the dashboard's canvas, below the top bar, the same
/// way <see cref="SettingsPanel"/> is. Every choice is written to <see cref="Loadout"/> the
/// moment it is made, which is what the next level's <see cref="PlayerLoadout"/> reads.
/// </summary>
public class LoadoutPanel : OverlayPanel
{
    StoreCatalog _catalog;
    RectTransform _guns, _bombs, _drinks;
    readonly List<GameObject> _rows = new List<GameObject>();

    /// <summary>Raised when the player asks for the store from here.</summary>
    public event System.Action StoreRequested;

    public static LoadoutPanel Create(Canvas canvas, StoreCatalog catalog, float topInset)
    {
        var host = new GameObject("LoadoutHost", typeof(RectTransform));
        host.transform.SetParent(canvas.transform, false);
        var p = host.AddComponent<LoadoutPanel>();
        p._catalog = catalog;
        p.Build(canvas, topInset);
        return p;
    }

    void Build(Canvas canvas, float topInset)
    {
        var t = UITheme.Active;
        var shade = UIKit.Panel(canvas.transform, "Loadout", UIKit.PanelTone.Background, t);
        shade.raycastTarget = true;
        UIKit.Fill(shade.rectTransform, 0, topInset, 0, 0);
        panel = shade.gameObject;

        var safe = UIKit.Rect(shade.transform, "SafeArea");
        UIKit.Fill(safe);
        var fit = safe.gameObject.AddComponent<SafeAreaFitter>();
        fit.paddingMm = 0f;
        fit.fitVertically = false;
        fit.Apply();

        var body = UIKit.Rect(safe, "Body");
        UIKit.Fill(body, 40, 24, 40, 32);
        var col = UIKit.Column(body, 16f);
        col.childForceExpandHeight = false;

        var header = UIKit.Rect(body, "Header");
        UIKit.Row(header, 16f, null, TextAnchor.MiddleLeft);
        UIKit.Size(header, height: 52f);
        var title = UIKit.Text(header, "Title", "Loadout", UIKit.TextRole.Title, t);
        UIKit.Size(title, flexWidth: 1f);
        var store = UIKit.Button(header, "ToStore", "Store", FlatButton.Variant.Secondary, "shopping-cart", t);
        store.onClick.AddListener(() => StoreRequested?.Invoke());
        var back = UIKit.Button(header, "Back", "Back", FlatButton.Variant.Quiet, "arrow-left", t);
        back.onClick.AddListener(Close);

        var columns = UIKit.Rect(body, "Columns");
        UIKit.Size(columns, flexHeight: 1f);
        var row = UIKit.Row(columns, 24f, null, TextAnchor.UpperLeft);
        row.childForceExpandHeight = true;
        row.childForceExpandWidth = true;
        _guns = Column(columns, "Primary", t);
        _bombs = Column(columns, "Grenade", t);
        _drinks = Column(columns, "Medkit", t);
        panel.SetActive(false);
    }

    static RectTransform Column(RectTransform parent, string heading, UITheme t)
    {
        UIKit.Section(parent, heading, heading, out var body, UIKit.PanelTone.Panel, t);
        return body;
    }

    protected override void OnOpened() => Refresh();

    void Refresh()
    {
        foreach (var r in _rows) if (r != null) Destroy(r);
        _rows.Clear();
        if (_catalog == null) return;
        var t = UITheme.Active;

        var equippedGun = Loadout.SelectedGun(_catalog);
        foreach (var g in _catalog.guns)
        {
            if (g == null) continue;
            bool owned = Loadout.OwnsGun(g);
            AddRow(_guns, t, "rifle", g.Label, owned ? $"Upgrade {Loadout.GunUpgradeLevel(g.id)}" : $"In the store · {Wallet.Format(g.price)} coins",
                   owned, g == equippedGun, () => { Loadout.SelectGun(g.id); Refresh(); });
        }

        bool bombEarned = Campaign.HasPower(Campaign.BombPower);
        var equippedBomb = Loadout.SelectedBomb(_catalog);
        foreach (var b in _catalog.bombs)
        {
            if (b == null) continue;
            bool owned = Loadout.OwnsBomb(b) && bombEarned;
            string detail = !bombEarned ? "Handed over by the story, when the first of the Augers falls."
                : owned ? $"{b.ChargesAt(Loadout.BombUpgradeLevel(b.id))} per level"
                : $"In the store · {Wallet.Format(b.price)} coins";
            AddRow(_bombs, t, "grenade", b.Label, detail, owned, owned && b == equippedBomb,
                   () => { Loadout.SelectBomb(b.id); Refresh(); });
        }

        var equippedDrink = Loadout.SelectedConsumable(_catalog);
        foreach (var c in _catalog.consumables)
        {
            if (c == null || c.data == null) continue;
            int stock = Loadout.Stock(c.id);
            AddRow(_drinks, t, "medkit", c.Label, stock > 0 ? $"x{stock} carried" : "None carried · buy in the store",
                   stock > 0, c == equippedDrink && stock > 0, () => { Loadout.SelectConsumable(c.id); Refresh(); });
        }
    }

    void AddRow(RectTransform parent, UITheme t, string icon, string name, string detail, bool owned, bool equipped,
                UnityEngine.Events.UnityAction equip)
    {
        var row = UIKit.Panel(parent, "Row_" + name, UIKit.PanelTone.Raised, t);
        if (equipped)
        {
            row.stripeSide = FlatRect.Side.Left;
            row.stripeColor = t.accent;
            row.stripePixels = t.stripePixels;
        }
        UIKit.Row(row, 14f, new RectOffset(16, 16, 10, 10), TextAnchor.MiddleLeft);
        UIKit.Size(row, height: 76f);
        UIKit.Icon(row.transform, "Icon", icon, 26f, owned ? t.textPrimary : t.textDisabled, t);
        var text = UIKit.Rect(row.transform, "Text");
        UIKit.Column(text, 2f);
        UIKit.Size(text, flexWidth: 1f);
        var n = UIKit.Text(text, "Name", name, UIKit.TextRole.Heading, t);
        n.fontSize = t.sizeBody + 1f;
        n.color = owned ? t.textPrimary : t.textDisabled;
        var d = UIKit.Text(text, "Detail", detail, UIKit.TextRole.Caption, t);
        d.textWrappingMode = TextWrappingModes.NoWrap;
        d.overflowMode = TextOverflowModes.Ellipsis;

        if (equipped)
        {
            var tag = UIKit.Text(row.transform, "Equipped", "Equipped", UIKit.TextRole.Label, t);
            tag.color = t.accent;
        }
        else if (owned)
        {
            var b = UIKit.Button(row.transform, "Equip", "Equip", FlatButton.Variant.Secondary, null, t);
            b.onClick.AddListener(equip);
        }
        _rows.Add(row.gameObject);
    }
}
