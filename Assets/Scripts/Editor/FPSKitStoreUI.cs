#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The STORE tab, built from the UI kit:
    ///
    ///   left     the shelves -- FEATURED, WEAPONS, CHARACTERS, GEAR, CONSUMABLES -- with line icons
    ///   header   the shelf's name, and the balance with a coin
    ///   grid     one card per item (see <see cref="StoreItemCard"/>), scrolling down
    ///
    /// Coins only: every price is in the game's own currency and nothing here takes money.
    /// Everything shown is bound at runtime by <see cref="StorePanel"/> from the catalog and the
    /// save, so this builds shapes with nothing typed into them.
    /// </summary>
    public static partial class FPSKitMenuBuilder
    {
        static void BuildStore(RectTransform root, MainMenuController menu)
        {
            var t = UITheme.Active;
            var shade = UIKit.Panel(root, "Store", UIKit.PanelTone.Background, t);
            shade.borderColor = new Color(0, 0, 0, 0);
            shade.raycastTarget = true;
            UIKit.Fill(shade.rectTransform);

            // On the canvas, not on the panel it hides -- see LevelSelectPanel.
            var store = root.gameObject.AddComponent<StorePanel>();
            store.panel = shade.gameObject;
            store.catalog = AssetDatabase.LoadAssetAtPath<StoreCatalog>(FPSKitStore.CatalogPath);
            store.renders = AssetDatabase.LoadAssetAtPath<ItemRenders>(FPSKitItemRenders.AssetPath);
            store.sounds = menu.sounds;

            var body = UIKit.Rect(shade.transform, "Body");
            UIKit.Fill(body, 40f, 24f, 40f, 24f);
            var bodyRow = UIKit.Row(body, 20f, null, TextAnchor.UpperLeft);
            bodyRow.childForceExpandHeight = true;
            bodyRow.childForceExpandWidth = false;

            // ---- shelves -----------------------------------------------------------------
            var rail = UIKit.Panel(body, "Shelves", UIKit.PanelTone.Panel, t);
            var railSize = UIKit.Size(rail, 240f);
            railSize.flexibleHeight = 1f;
            railSize.flexibleWidth = 0f;
            var rcol = UIKit.Column(rail, 2f, new RectOffset(0, 0, 12, 12));
            rcol.childForceExpandHeight = false;
            rcol.childControlHeight = true;
            var shelves = new[]
            {
                ("Featured", "star"), ("Weapons", "rifle"), ("Characters", "user"),
                ("Gear", "backpack"), ("Consumables", "flask"),
            };
            var tabs = new Button[shelves.Length];
            for (int i = 0; i < shelves.Length; i++)
            {
                var b = UIKit.Button(rail.transform, "Shelf_" + shelves[i].Item1, shelves[i].Item1,
                                     FlatButton.Variant.Tab, shelves[i].Item2, t);
                b.tabEdge = FlatRect.Side.Left;
                b.variant = FlatButton.Variant.Tab;
                var row = b.GetComponent<HorizontalLayoutGroup>();
                if (row != null) { row.childAlignment = TextAnchor.MiddleLeft; row.padding = new RectOffset(20, 16, 0, 0); }
                UIKit.Size(b, height: 52f);
                tabs[i] = b;
            }

            // ---- shelf -------------------------------------------------------------------
            var main = UIKit.Rect(body, "Shelf");
            UIKit.Size(main, flexWidth: 1f);
            var mcol = UIKit.Column(main, 14f);
            mcol.childForceExpandHeight = false;
            mcol.childControlHeight = true;

            var header = UIKit.Rect(main, "Header");
            UIKit.Row(header, 12f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
            UIKit.Size(header).minHeight = 48f;
            var heading = UIKit.Text(header, "Heading", "FEATURED", UIKit.TextRole.Title, t);
            UIKit.Size(heading, flexWidth: 1f);
            UIKit.Icon(header, "Coin", "coin", 24f, t.accent, t);
            var balance = UIKit.Text(header, "Balance", "0", UIKit.TextRole.Number, t);
            balance.color = t.accent;
            balance.fontSize = t.sizeTitle;

            var status = UIKit.Text(main, "Status", "", UIKit.TextRole.Label, t);
            status.color = t.textSecondary;

            var viewport = UIKit.Rect(main, "Items");
            UIKit.Size(viewport, flexHeight: 1f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<FlatRect>();
            hit.color = new Color(0, 0, 0, 0);
            hit.raycastTarget = true;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var grid = UIKit.Rect(viewport, "Grid");
            grid.anchorMin = new Vector2(0f, 1f); grid.anchorMax = new Vector2(1f, 1f); grid.pivot = new Vector2(0.5f, 1f);
            grid.offsetMin = grid.offsetMax = Vector2.zero;
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(360f, 470f);
            layout.spacing = new Vector2(16f, 16f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 3;
            grid.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = grid;

            var empty = UIKit.Panel(viewport, "Empty", UIKit.PanelTone.Panel, t);
            var ert = empty.rectTransform;
            ert.anchorMin = new Vector2(0f, 1f); ert.anchorMax = new Vector2(1f, 1f); ert.pivot = new Vector2(0.5f, 1f);
            ert.sizeDelta = new Vector2(0f, 140f);
            UIKit.Column(empty, 8f, new RectOffset(24, 24, 20, 20)).childForceExpandHeight = false;
            UIKit.Icon(empty.transform, "Icon", "user", 32f, t.textSecondary, t);
            var emptyText = UIKit.Text(empty.transform, "Text", "", UIKit.TextRole.Body, t);
            emptyText.color = t.textSecondary;
            emptyText.textWrappingMode = TextWrappingModes.Normal;
            empty.gameObject.SetActive(false);

            store.cardParent = grid;
            store.scroll = scroll;
            store.cardTemplate = BuildStoreCardTemplate(shade.rectTransform, t);
            store.tabButtons = tabs;
            store.tabOrder = new[] { StorePanel.Tab.Featured, StorePanel.Tab.Guns, StorePanel.Tab.Characters, StorePanel.Tab.Bombs, StorePanel.Tab.Items };
            store.balanceText = balance;
            store.headingText = heading;
            store.statusText = status;
            store.emptyState = empty.gameObject;
            store.emptyText = emptyText;
            store.gridColumns = 3;
            store.cardAspect = 0.92f;

            menu.store = store;
            shade.gameObject.SetActive(false);
        }

        /// <summary>One item card, built once and left off; the panel clones it per item.</summary>
        static StoreItemCard BuildStoreCardTemplate(RectTransform parent, UITheme t)
        {
            var frame = UIKit.Panel(parent, "StoreCardTemplate", UIKit.PanelTone.Panel, t);
            frame.raycastTarget = true;
            frame.rectTransform.sizeDelta = new Vector2(360f, 470f);
            var col = UIKit.Column(frame, 0f, new RectOffset(1, 1, 1, 1));
            col.childForceExpandHeight = false;
            col.childControlHeight = true;

            // The render, on a plain panel: the item and nothing behind it.
            var stage = UIKit.Panel(frame.transform, "Stage", UIKit.PanelTone.Raised, t);
            stage.borderColor = new Color(0, 0, 0, 0);
            UIKit.Size(stage).flexibleHeight = 1f;
            UIKit.Size(stage).minHeight = 90f;
            var render = UIKit.Rect(stage.transform, "Render").gameObject.AddComponent<RawImage>();
            render.raycastTarget = false;
            UIKit.Fill(render.rectTransform, 8f, 8f, 8f, 8f);
            var aspect = render.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 1.6f;
            var fallback = UIKit.Icon(stage.transform, "Icon", "shopping-cart", 64f, t.textSecondary, t);
            var frt = fallback.rectTransform;
            frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0.5f, 0.5f);
            fallback.GetComponent<LayoutElement>().ignoreLayout = true;
            var state = UIKit.Text(stage.transform, "State", "", UIKit.TextRole.Label, t);
            var srt = state.rectTransform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 1f);
            srt.anchoredPosition = new Vector2(12f, -10f);
            srt.sizeDelta = new Vector2(200f, 22f);

            var stripeRect = UIKit.Rect(frame.transform, "Stripe");
            UIKit.Size(stripeRect, height: 3f);
            var stripe = stripeRect.gameObject.AddComponent<FlatRect>();
            stripe.raycastTarget = false;

            var info = UIKit.Rect(frame.transform, "Info");
            var icol = UIKit.Column(info, 6f, new RectOffset(16, 16, 12, 14));
            icol.childForceExpandHeight = false;
            icol.childControlHeight = true;
            var title = UIKit.Text(info, "Title", "ITEM", UIKit.TextRole.Heading, t);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;
            var desc = UIKit.Text(info, "Description", "", UIKit.TextRole.Caption, t);
            desc.color = t.textSecondary;
            desc.textWrappingMode = TextWrappingModes.NoWrap;
            desc.overflowMode = TextOverflowModes.Ellipsis;
            var stats = UIKit.Text(info, "Stats", "", UIKit.TextRole.Label, t);
            stats.textWrappingMode = TextWrappingModes.NoWrap;
            stats.overflowMode = TextOverflowModes.Ellipsis;

            var pipsRow = UIKit.Rect(info, "Pips");
            UIKit.Row(pipsRow, 4f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
            UIKit.Size(pipsRow).minHeight = 6f;
            var pips = new FlatRect[5];
            for (int i = 0; i < pips.Length; i++)
            {
                pips[i] = UIKit.Rect(pipsRow, "Pip" + i).gameObject.AddComponent<FlatRect>();
                pips[i].raycastTarget = false;
                UIKit.Size(pips[i], 22f, 5f);
            }

            var buy = UIKit.Rect(info, "Buy");
            UIKit.Row(buy, 8f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
            UIKit.Size(buy).minHeight = UIKit.ControlHeight;
            var priceRow = UIKit.Rect(buy, "Price");
            UIKit.Row(priceRow, 6f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
            UIKit.Size(priceRow, flexWidth: 1f);
            var coin = UIKit.Icon(priceRow, "Coin", "coin", 20f, t.accent, t);
            var price = UIKit.Text(priceRow, "Amount", "0", UIKit.TextRole.Number, t);
            price.fontSize = t.sizeHeading;
            var upgrade = UIKit.Button(buy, "Upgrade", "Upgrade", FlatButton.Variant.Secondary, null, t);
            var primary = UIKit.Button(buy, "Primary", "Buy", FlatButton.Variant.Primary, null, t);

            var card = frame.gameObject.AddComponent<StoreItemCard>();
            card.frame = frame;
            card.stripe = stripe;
            card.render = render;
            card.fallbackIcon = fallback;
            card.titleText = title;
            card.descriptionText = desc;
            card.statsText = stats;
            card.stateText = state;
            card.priceRow = priceRow.gameObject;
            card.priceText = price;
            card.priceIcon = coin;
            card.upgradePips = pips;
            card.primaryButton = primary;
            card.primaryLabelText = primary.label;
            card.upgradeButton = upgrade;
            card.upgradeLabelText = upgrade.label;
            frame.gameObject.SetActive(false);
            return card;
        }
    }
}
#endif
