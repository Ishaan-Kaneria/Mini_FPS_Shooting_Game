using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// LOADOUT: four slots -- PRIMARY, SECONDARY, GRENADE, CONSUMABLE -- each showing what is in
/// it. Clicking a slot opens what the player owns for it; picking one equips it there and then.
/// Buying stays in the store; a slot with nothing owned says where to get something.
///
/// Every choice is written to <see cref="Loadout"/> the moment it is made. That is what the
/// dashboard's loadout strip reads when the screen closes, and what the next level's
/// <see cref="PlayerLoadout"/> equips, so the HUD's weapon panel and ability slots show it.
///
/// <b>SECONDARY is empty on purpose</b>: the game carries one gun. The slot is drawn and says
/// so, rather than being left out -- Ishaan's call, 2026-09-25.
///
/// Built at runtime from the kit onto the dashboard's canvas, below the top bar, the same
/// way <see cref="SettingsPanel"/> is.
/// </summary>
public class LoadoutPanel : OverlayPanel
{
    enum SlotKind { Primary, Secondary, Grenade, Consumable }

    StoreCatalog _catalog;
    ItemRenders _renders;
    UITheme _t;
    readonly Dictionary<SlotKind, Slot> _slots = new Dictionary<SlotKind, Slot>();
    RectTransform _options;
    TMP_Text _optionsTitle;
    SlotKind _open = SlotKind.Primary;
    readonly List<GameObject> _optionRows = new List<GameObject>();

    /// <summary>Raised when the player asks for the store from here.</summary>
    public event System.Action StoreRequested;

    class Slot
    {
        public FlatRect face;
        public RawImage render;
        public Image icon;
        public TMP_Text kind, name, detail;
    }

    public static LoadoutPanel Create(Canvas canvas, StoreCatalog catalog, float topInset, ItemRenders renders = null)
    {
        var host = new GameObject("LoadoutHost", typeof(RectTransform));
        host.transform.SetParent(canvas.transform, false);
        var p = host.AddComponent<LoadoutPanel>();
        p._catalog = catalog;
        p._renders = renders;
        p.Build(canvas, topInset);
        return p;
    }

    void Build(Canvas canvas, float topInset)
    {
        _t = UITheme.Active;
        var shade = UIKit.Panel(canvas.transform, "Loadout", UIKit.PanelTone.Background, _t);
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
        col.childControlHeight = true;

        var header = UIKit.Rect(body, "Header");
        UIKit.Row(header, 16f, null, TextAnchor.MiddleLeft);
        UIKit.Size(header).minHeight = 52f;
        var title = UIKit.Text(header, "Title", "Loadout", UIKit.TextRole.Title, _t);
        UIKit.Size(title, flexWidth: 1f);
        var store = UIKit.Button(header, "ToStore", "Store", FlatButton.Variant.Secondary, "shopping-cart", _t);
        store.onClick.AddListener(() => StoreRequested?.Invoke());
        var back = UIKit.Button(header, "Back", "Back", FlatButton.Variant.Quiet, "arrow-left", _t);
        back.onClick.AddListener(Close);

        // The four slots.
        var slots = UIKit.Rect(body, "Slots");
        var srow = UIKit.Row(slots, 16f, null, TextAnchor.UpperLeft);
        srow.childForceExpandWidth = true;
        srow.childControlHeight = true;
        UIKit.Size(slots).minHeight = 250f;
        foreach (SlotKind k in System.Enum.GetValues(typeof(SlotKind))) _slots[k] = BuildSlot(slots, k);

        // What can go in the slot that is open.
        var options = UIKit.Panel(body, "Options", UIKit.PanelTone.Panel, _t);
        UIKit.Size(options, flexHeight: 1f);
        var ocol = UIKit.Column(options, 8f, new RectOffset(20, 20, 16, 16));
        ocol.childForceExpandHeight = false;
        ocol.childControlHeight = true;
        _optionsTitle = UIKit.Text(options.transform, "Title", "", UIKit.TextRole.Heading, _t);
        _options = UIKit.Rect(options.transform, "List");
        var list = UIKit.Column(_options, 8f);
        list.childForceExpandHeight = false;
        list.childControlHeight = true;

        panel.SetActive(false);
    }

    Slot BuildSlot(RectTransform parent, SlotKind kind)
    {
        var s = new Slot();
        s.face = UIKit.Panel(parent, kind.ToString(), UIKit.PanelTone.Panel, _t);
        s.face.raycastTarget = true;
        var col = UIKit.Column(s.face, 4f, new RectOffset(14, 14, 12, 14));
        col.childForceExpandHeight = false;
        col.childControlHeight = true;
        s.kind = UIKit.Text(s.face.transform, "Kind", Title(kind), UIKit.TextRole.Label, _t);
        s.kind.color = _t.textSecondary;

        var stage = UIKit.Panel(s.face.transform, "Stage", UIKit.PanelTone.Raised, _t);
        stage.borderColor = new Color(0, 0, 0, 0);
        UIKit.Size(stage).minHeight = 110f;
        s.render = UIKit.Rect(stage.transform, "Render").gameObject.AddComponent<RawImage>();
        s.render.raycastTarget = false;
        UIKit.Fill(s.render.rectTransform, 6f, 6f, 6f, 6f);
        var aspect = s.render.gameObject.AddComponent<AspectRatioFitter>();
        aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        aspect.aspectRatio = 1.6f;
        s.icon = UIKit.Icon(stage.transform, "Icon", IconFor(kind), 44f, _t.textDisabled, _t);
        s.icon.rectTransform.anchorMin = s.icon.rectTransform.anchorMax = s.icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        s.icon.GetComponent<LayoutElement>().ignoreLayout = true;

        s.name = UIKit.Text(s.face.transform, "Name", "", UIKit.TextRole.Heading, _t);
        s.name.textWrappingMode = TextWrappingModes.NoWrap;
        s.name.overflowMode = TextOverflowModes.Ellipsis;
        s.detail = UIKit.Text(s.face.transform, "Detail", "", UIKit.TextRole.Caption, _t);
        s.detail.color = _t.textSecondary;
        s.detail.textWrappingMode = TextWrappingModes.NoWrap;
        s.detail.overflowMode = TextOverflowModes.Ellipsis;

        var button = s.face.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = s.face;
        button.onClick.AddListener(() => { _open = kind; Refresh(); });
        return s;
    }

    static string Title(SlotKind k) => k switch
    {
        SlotKind.Primary => "PRIMARY",
        SlotKind.Secondary => "SECONDARY",
        SlotKind.Grenade => "GRENADE",
        _ => "CONSUMABLE",
    };

    static string IconFor(SlotKind k) => k switch
    {
        SlotKind.Grenade => "grenade",
        SlotKind.Consumable => "flask",
        _ => "rifle",
    };

    protected override void OnOpened() => Refresh();

    void Refresh()
    {
        if (_catalog == null) return;
        bool bombEarned = Campaign.HasPower(Campaign.BombPower);

        var gun = Loadout.SelectedGun(_catalog);
        Fill(SlotKind.Primary, gun != null ? gun.Label : "", gun != null ? $"UPGRADE {Loadout.GunUpgradeLevel(gun.id)}" : "", gun != null ? gun.id : null);
        Fill(SlotKind.Secondary, "Empty", "The game carries one gun", null);
        var bomb = bombEarned ? Loadout.SelectedBomb(_catalog) : null;
        Fill(SlotKind.Grenade, bomb != null ? bomb.Label : "Locked",
             bomb != null ? $"{bomb.ChargesAt(Loadout.BombUpgradeLevel(bomb.id))} PER LEVEL" : "The first of the Augers carries it",
             bomb != null ? bomb.id : null);
        var drink = Loadout.SelectedConsumable(_catalog);
        int stock = drink != null ? Loadout.Stock(drink.id) : 0;
        Fill(SlotKind.Consumable, drink != null && stock > 0 ? drink.Label : "Empty",
             drink != null && stock > 0 ? $"x{stock} CARRIED" : "Buy packs in the store",
             drink != null && stock > 0 ? drink.id : null);

        foreach (var kv in _slots)
        {
            bool open = kv.Key == _open;
            kv.Value.face.borderColor = open ? _t.accent : _t.border;
            kv.Value.face.borderPixels = open ? 2f : _t.borderPixels;
        }

        BuildOptions(bombEarned);
    }

    void Fill(SlotKind kind, string name, string detail, string renderId)
    {
        var s = _slots[kind];
        var tex = _renders != null ? _renders.For(renderId) : null;
        s.render.texture = tex;
        s.render.gameObject.SetActive(tex != null);
        s.icon.gameObject.SetActive(tex == null);
        s.name.text = (name ?? "").ToUpperInvariant();
        s.name.color = string.IsNullOrEmpty(renderId) ? _t.textDisabled : _t.textPrimary;
        s.detail.text = detail ?? "";
    }

    void BuildOptions(bool bombEarned)
    {
        foreach (var r in _optionRows) if (r != null) Destroy(r);
        _optionRows.Clear();
        _optionsTitle.text = $"{Title(_open)}  -  WHAT YOU OWN";

        switch (_open)
        {
            case SlotKind.Primary:
            {
                var equipped = Loadout.SelectedGun(_catalog);
                foreach (var g in _catalog.guns)
                    if (g != null && Loadout.OwnsGun(g))
                    {
                        var gun = g;
                        Option(g.id, g.Label, $"UPGRADE {Loadout.GunUpgradeLevel(g.id)}", g == equipped,
                               () => { Loadout.SelectGun(gun.id); Refresh(); });
                    }
                break;
            }
            case SlotKind.Secondary:
                Note("The game carries one gun, so there is nothing to put here yet.");
                break;
            case SlotKind.Grenade:
            {
                if (!bombEarned) { Note("Explosives are handed over by the story, when the first of the Augers falls."); break; }
                var equipped = Loadout.SelectedBomb(_catalog);
                foreach (var b in _catalog.bombs)
                    if (b != null && Loadout.OwnsBomb(b))
                    {
                        var bomb = b;
                        Option(b.id, b.Label, $"{b.ChargesAt(Loadout.BombUpgradeLevel(b.id))} PER LEVEL", b == equipped,
                               () => { Loadout.SelectBomb(bomb.id); Refresh(); });
                    }
                break;
            }
            case SlotKind.Consumable:
            {
                var equipped = Loadout.SelectedConsumable(_catalog);
                int shown = 0;
                foreach (var c in _catalog.consumables)
                {
                    if (c == null || c.data == null || Loadout.Stock(c.id) <= 0) continue;
                    var item = c;
                    Option(c.id, c.Label, $"x{Loadout.Stock(c.id)} CARRIED", c == equipped,
                           () => { Loadout.SelectConsumable(item.id); Refresh(); });
                    shown++;
                }
                if (shown == 0) Note("Nothing carried. Packs are in the store, under CONSUMABLES.");
                break;
            }
        }
    }

    void Option(string id, string name, string detail, bool equipped, UnityEngine.Events.UnityAction equip)
    {
        var row = UIKit.Panel(_options, "Option_" + id, UIKit.PanelTone.Raised, _t);
        if (equipped)
        {
            row.stripeSide = FlatRect.Side.Left;
            row.stripeColor = _t.accent;
            row.stripePixels = _t.stripePixels;
        }
        UIKit.Row(row, 14f, new RectOffset(12, 16, 6, 6), TextAnchor.MiddleLeft).childControlHeight = true;
        UIKit.Size(row).minHeight = 64f;
        var pic = UIKit.Rect(row.transform, "Render").gameObject.AddComponent<RawImage>();
        pic.raycastTarget = false;
        pic.texture = _renders != null ? _renders.For(id) : null;
        pic.color = pic.texture != null ? Color.white : new Color(0, 0, 0, 0);
        UIKit.Size(pic, 88f, 55f);
        var text = UIKit.Rect(row.transform, "Text");
        UIKit.Column(text, 2f);
        UIKit.Size(text, flexWidth: 1f);
        UIKit.Text(text, "Name", name.ToUpperInvariant(), UIKit.TextRole.Heading, _t);
        var d = UIKit.Text(text, "Detail", detail, UIKit.TextRole.Caption, _t);
        d.color = _t.textSecondary;
        if (equipped)
        {
            var tag = UIKit.Text(row.transform, "Equipped", "Equipped", UIKit.TextRole.Label, _t);
            tag.color = _t.accent;
        }
        else
        {
            var b = UIKit.Button(row.transform, "Equip", "Equip", FlatButton.Variant.Primary, null, _t);
            b.onClick.AddListener(equip);
        }
        _optionRows.Add(row.gameObject);
    }

    void Note(string text)
    {
        var n = UIKit.Text(_options, "Note", text, UIKit.TextRole.Body, _t);
        n.color = _t.textSecondary;
        n.textWrappingMode = TextWrappingModes.Normal;
        _optionRows.Add(n.gameObject);
    }
}
