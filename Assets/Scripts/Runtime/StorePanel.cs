using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The store: guns, bombs, energy drinks and the health upgrade, bought with the coins
/// kills pay out.
///
/// It is the one place coins leave the <see cref="Wallet"/>, and every purchase is the
/// same three steps in the same order -- check it is for sale, take the money through
/// <see cref="Wallet.TrySpend"/>, and only then record it in <see cref="Loadout"/>. The
/// order is the whole safety property: granting first and charging afterwards is how a
/// shop hands out an item to a player who could not pay for it.
///
/// Built into the dashboard scene alongside the level select, and shown the same way --
/// a full-screen panel over the arenas rather than a scene of its own.
///
/// A card carries three numbers and one line of prose, and that is the whole budget. It
/// held twice that at first -- the current stats, then a second line spelling out what
/// each upgrade added as four percentages -- and the effect was that nobody read any of
/// it. The numbers shown are the *current* ones with upgrades folded in, so buying one
/// visibly moves them, which teaches what an upgrade does far better than a list of
/// percentages does. The pips say how many are left.
/// </summary>
[DisallowMultipleComponent]
public class StorePanel : MonoBehaviour
{
    /// <summary>Which shelf is on screen.</summary>
    /// <summary>
    /// The shelves. The first four keep their numbers because tests and older code name them;
    /// what the rail shows is FEATURED, WEAPONS, CHARACTERS, GEAR and CONSUMABLES, where GEAR
    /// is the explosives and Vitality together (Bombs and Health both open it).
    /// </summary>
    public enum Tab { Guns, Bombs, Items, Health, Featured, Characters }

    [Header("Wiring")]
    [Tooltip("What is for sale. Wired by the dashboard builder.")]
    public StoreCatalog catalog;

    [Tooltip("The root switched on when the store is opened. Hidden at Awake.")]
    public GameObject panel;

    [Tooltip("Parent the cards are cloned into. A GridLayoutGroup on it does the placing.")]
    public RectTransform cardParent;

    [Tooltip("Hidden card cloned once per item on the current shelf.")]
    public StoreItemCard cardTemplate;

    public Button backButton;

    [Header("Tabs")]
    [Tooltip("In the order of the Tab enum: guns, bombs, items, health.")]
    public Button[] tabButtons;

    [Tooltip("Which shelf each rail button opens, in the order they are drawn.")]
    public Tab[] tabOrder = { Tab.Featured, Tab.Guns, Tab.Characters, Tab.Bombs, Tab.Items };

    [Tooltip("Renders of each item's model, keyed by the item's id. Written by FPSKit > Render Store Items.")]
    public ItemRenders renders;

    [Tooltip("Shown instead of the grid when a shelf has nothing on it.")]
    public GameObject emptyState;
    public TMP_Text emptyText;

    [Tooltip("The scroll the grid sits in. The grid is sized to its width and scrolls down.")]
    public ScrollRect scroll;

    /// <summary>Raised after every purchase, so whatever shows the balance can show the new one at once.</summary>
    public event System.Action Purchased;

    [Tooltip("The edge of the tab that is open. This tints the button's border rather " +
             "than its face -- the face stays dark so the caption on it stays legible, " +
             "which it was not when the open tab went amber under off-white text.")]
    public Color tabActiveColor = new Color(1f, 0.729f, 0.247f);

    [Tooltip("The edge of a tab that is not open.")]
    public Color tabIdleColor = new Color(0.361f, 0.412f, 0.478f);

    [Tooltip("The caption of the open tab. Carries the accent, because the face beneath " +
             "it no longer can.")]
    public Color tabActiveInk = new Color(1f, 0.729f, 0.247f);

    [Tooltip("The caption of a tab that is not open.")]
    public Color tabIdleInk = new Color(0.671f, 0.714f, 0.761f);

    [Header("Text")]
    public TMP_Text balanceText;
    public TMP_Text headingText;
    public TMP_Text statusText;

    [Header("Grid")]
    [Min(1)] public int gridColumns = 3;

    [Tooltip("Card height as a fraction of its width.")]
    [Min(0.1f)] public float cardAspect = 0.82f;

    [Header("Audio")]
    public UISounds sounds;

    // ======================================================================
    readonly List<StoreItemCard> _cards = new List<StoreItemCard>();

    GridLayoutGroup _layout;
    float _fittedWidth = -1f;
    float _fittedHeight = -1f;
    Tab _tab = Tab.Guns;

    public bool IsOpen => panel != null && panel.activeSelf;

    /// <summary>Cards currently drawn. Read by the flow test.</summary>
    public int CardCount => _cards.Count;

    /// <summary>Raised when the player backs out. The dashboard shows the arenas again.</summary>
    public event System.Action Closed;

    void Awake()
    {
        HideAtLoad();

        if (cardTemplate != null) cardTemplate.gameObject.SetActive(false);
    }

    /// <summary>
    /// Hides the panel at load.
    ///
    /// The panel must be a different GameObject from this one. A component that hides
    /// its own object here never gets to show it again: the builder leaves the panel
    /// switched off, so Awake has not run, and the first SetActive(true) is what finally
    /// runs it -- which switches the object straight back off. The screen then never
    /// opens and nothing is logged. LevelSelectPanel learned this the hard way.
    /// </summary>
    void HideAtLoad()
    {
        if (panel == null) return;

        if (panel == gameObject)
        {
            Debug.LogError("[StorePanel] The panel is this object, so hiding it would stop it " +
                           "ever being shown again. Put this component on the canvas and " +
                           "point it at a child.", this);
            return;
        }

        panel.SetActive(false);
    }

    void Start()
    {
        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(Close);
        }

        BuildConfirm();
        if (tabButtons == null) return;
        for (int i = 0; i < tabButtons.Length; i++)
        {
            if (tabButtons[i] == null) continue;
            var tab = i < tabOrder.Length ? tabOrder[i] : (Tab)i;
            tabButtons[i].onClick.RemoveAllListeners();
            tabButtons[i].onClick.AddListener(() => ShowTab(tab));
        }
    }

    void Update()
    {
        if (!IsOpen) return;

        FitGrid();

        // Escape and Q back out, the same keys the level select takes. A screen with no
        // keyboard way out traps anyone whose pointer is not where they expected it.
        if (GameInput.BackPressed) Close();
    }

    // ======================================================================
    public void Open()
    {
        _tab = Tab.Featured;
        StoreAlerts.MarkSeen(catalog);

        if (panel != null) panel.SetActive(true);

        // The measured box has only just been shown, so the cells have to be worked out
        // again rather than trusting whatever the last screen left behind.
        _fittedWidth = _fittedHeight = -1f;

        ShowTab(_tab);

        if (backButton != null) backButton.Select();
    }

    public void Close()
    {
        if (panel != null) panel.SetActive(false);
        Closed?.Invoke();
    }

    public void ShowTab(Tab tab)
    {
        // A shelf the campaign has not opened yet falls back rather than showing empty.
        // This is belt and braces -- its button is hidden below -- but the tab is also
        // reachable from Open and from anything that remembers the last shelf, and an
        // empty EXPLOSIVES page reads as a broken store rather than as a locked one.
        _tab = TabAvailable(tab) ? tab : Tab.Guns;
        if (_confirm != null) _confirm.SetActive(false);

        Report("");
        Rebuild();
        PaintTabs();

        _fittedWidth = _fittedHeight = -1f;
    }

    /// <summary>
    /// Whether this shelf is open to the player yet.
    ///
    /// Explosives are the campaign's first reward, so before that gate there is no bomb
    /// to own and nothing on the shelf but upgrades for something the player has never
    /// held. Hiding the tab is the honest version: the store sells better bombs, and the
    /// story is what gives you a bomb in the first place.
    /// </summary>
    public static bool TabAvailable(Tab tab) => true;

    /// <summary>The explosives are the campaign's to hand over; before that GEAR is Vitality alone.</summary>
    static bool BombsOpen => Campaign.HasPower(Campaign.BombPower);

    void PaintTabs()
    {
        if (tabButtons == null) return;
        for (int i = 0; i < tabButtons.Length; i++)
        {
            if (tabButtons[i] == null) continue;
            var tab = i < tabOrder.Length ? tabOrder[i] : (Tab)i;
            bool active = Shelf(tab) == Shelf(_tab);
            if (tabButtons[i] is FlatButton flat) flat.Selected = active;
        }
    }

    /// <summary>Bombs and Health are one shelf, GEAR.</summary>
    static Tab Shelf(Tab tab) => tab == Tab.Health ? Tab.Bombs : tab;

    // ======================================================================

    /// <summary>
    /// Redraws the shelf from scratch.
    ///
    /// Everything on a card -- the price, whether it can be afforded, how many upgrades
    /// are lit -- is derived from the wallet and the loadout, so a purchase changes half
    /// the screen. Rebuilding is what makes that correct by construction: there is no
    /// path where a card is left showing a price the player has just paid.
    /// </summary>
    void Rebuild()
    {
        foreach (var card in _cards)
            if (card != null) Destroy(card.gameObject);

        _cards.Clear();

        ShowBalance();

        if (headingText != null) headingText.text = HeadingFor(_tab);

        if (cardTemplate == null || cardParent == null)
        {
            Report("The store has no card template wired. Run FPSKit > Build Dashboard.");
            return;
        }

        if (catalog == null)
        {
            Report("The store has no catalog. Run FPSKit > Reset Store, then FPSKit > Build Dashboard.");
            return;
        }

        switch (_tab)
        {
            case Tab.Featured: BuildFeatured(); break;
            case Tab.Guns: BuildGuns(); break;
            case Tab.Bombs:
            case Tab.Health:
                if (BombsOpen) BuildBombs();
                BuildHealth();
                break;
            case Tab.Items: BuildItems(); break;
            case Tab.Characters: break;
        }

        // An empty shelf says why, rather than being a blank panel.
        bool empty = _cards.Count == 0;
        if (emptyState != null) emptyState.SetActive(empty);
        if (emptyText != null)
            emptyText.text = _tab == Tab.Characters
                ? "No characters yet. Every operative looks the same for now; this shelf is where new ones will go."
                : _tab == Tab.Featured ? "You own everything worth featuring. Upgrades are on each item's own shelf."
                : $"Nothing in {HeadingFor(_tab)} yet.";

    }

    static string HeadingFor(Tab tab) => tab switch
    {
        Tab.Featured => "FEATURED",
        Tab.Guns => "WEAPONS",
        Tab.Characters => "CHARACTERS",
        Tab.Items => "CONSUMABLES",
        _ => "GEAR"
    };

    /// <summary>
    /// FEATURED: what the player would most sensibly buy next -- the cheapest few things
    /// they do not own yet, guns and explosives together. Read from the catalog and the save
    /// each time, so it moves as they buy; nothing is chosen by hand.
    /// </summary>
    void BuildFeatured()
    {
        var picks = new List<(int price, System.Action build)>();
        foreach (var gun in catalog.guns)
            if (gun != null && gun.data != null && !Loadout.OwnsGun(gun))
            { var g = gun; picks.Add((gun.price, () => BuildGun(g))); }
        if (BombsOpen)
            foreach (var bomb in catalog.bombs)
                if (bomb != null && bomb.data != null && !Loadout.OwnsBomb(bomb))
                { var b = bomb; picks.Add((bomb.price, () => BuildBomb(b))); }
        picks.Sort((a, b) => a.price.CompareTo(b.price));
        for (int i = 0; i < picks.Count && i < 4; i++) picks[i].build();
    }

    StoreItemCard Spawn()
    {
        var card = Instantiate(cardTemplate, cardParent);
        card.gameObject.SetActive(true);

        _cards.Add(card);
        return card;
    }

    // ======================================================================
    void BuildGuns()
    {
        foreach (var gun in catalog.guns)
            if (gun != null && gun.data != null) BuildGun(gun);
    }

    void BuildGun(StoreCatalog.GunEntry gun)
    {
        var equipped = Loadout.SelectedGun(catalog);
        {

            bool owned = Loadout.OwnsGun(gun);
            bool isEquipped = owned && equipped == gun;
            int level = Loadout.GunUpgradeLevel(gun.id);
            bool canUpgrade = owned && gun.upgrades.CanUpgrade(level);
            int upgradePrice = gun.upgrades.PriceAt(level);

            var captured = gun;

            var content = new StoreItemCard.Content
            {
                title = gun.Label,
                description = gun.description,
                stats = GunStats(gun, level),
                state = isEquipped ? "EQUIPPED" : owned ? "OWNED" : "",
                accent = new Color(1f, 0.78f, 0.3f),

                upgradeLevel = level,
                upgradeMax = gun.upgrades.maxLevel,

                primaryShown = !isEquipped,
                primaryLabel = owned ? "EQUIP" : "BUY",
                primaryPrice = owned ? 0 : gun.price,
                primaryEnabled = owned || Wallet.CanAfford(gun.price),
                primaryAction = owned ? () => EquipGun(captured)
                                      : () => Ask(captured.Label, captured.price, () => BuyGun(captured)),
                render = renders != null ? renders.For(gun.id) : null,
                iconId = "rifle",

                upgradeShown = owned,
                upgradeLabel = canUpgrade ? "UPGRADE" : "MAXED",
                upgradePrice = canUpgrade ? upgradePrice : 0,
                upgradeEnabled = canUpgrade && Wallet.CanAfford(upgradePrice),
                upgradeAction = () => Ask($"{captured.Label} upgrade", upgradePrice, () => UpgradeGun(captured))
            };

            Spawn().Bind(content);
        }
    }

    /// <summary>
    /// Three numbers, with the bought upgrades already in them. A shotgun's are its
    /// pellets times its damage, because "13 DMG" on a gun that fires nine at once is a
    /// true number that tells the player the opposite of the truth.
    /// </summary>
    static string GunStats(StoreCatalog.GunEntry gun, int level)
    {
        var data = gun.data;

        float damage = data.damage * (1f + gun.damagePerLevel * level)
                       * Mathf.Max(1, data.pelletsPerShot);

        float rpm = data.roundsPerMinute * (1f + gun.fireRatePerLevel * level);
        int magazine = data.magazineSize + gun.magazinePerLevel * level;

        return UIText.Row($"{damage:0} DMG", $"{rpm:0} RPM", $"{magazine} MAG");
    }

    // ======================================================================
    void BuildBombs()
    {
        foreach (var bomb in catalog.bombs)
            if (bomb != null && bomb.data != null) BuildBomb(bomb);
    }

    void BuildBomb(StoreCatalog.BombEntry bomb)
    {
        var equipped = Loadout.SelectedBomb(catalog);
        {

            bool owned = Loadout.OwnsBomb(bomb);
            bool isEquipped = owned && equipped == bomb;
            int level = Loadout.BombUpgradeLevel(bomb.id);
            bool canUpgrade = owned && bomb.upgrades.CanUpgrade(level);
            int upgradePrice = bomb.upgrades.PriceAt(level);

            var captured = bomb;

            var content = new StoreItemCard.Content
            {
                title = bomb.Label,
                description = bomb.description,
                stats = BombStats(bomb, level),
                state = isEquipped ? "EQUIPPED" : owned ? "OWNED" : "",
                accent = bomb.data.blastColor,

                upgradeLevel = level,
                upgradeMax = bomb.upgrades.maxLevel,

                primaryShown = !isEquipped,
                primaryLabel = owned ? "EQUIP" : "BUY",
                primaryPrice = owned ? 0 : bomb.price,
                primaryEnabled = owned || Wallet.CanAfford(bomb.price),
                primaryAction = owned ? () => EquipBomb(captured)
                                      : () => Ask(captured.Label, captured.price, () => BuyBomb(captured)),
                render = renders != null ? renders.For(bomb.id) : null,
                iconId = "grenade",

                upgradeShown = owned,
                upgradeLabel = canUpgrade ? "UPGRADE" : "MAXED",
                upgradePrice = canUpgrade ? upgradePrice : 0,
                upgradeEnabled = canUpgrade && Wallet.CanAfford(upgradePrice),
                upgradeAction = () => Ask($"{captured.Label} upgrade", upgradePrice, () => UpgradeBomb(captured))
            };

            Spawn().Bind(content);
        }
    }

    static string BombStats(StoreCatalog.BombEntry bomb, int level)
    {
        var blast = bomb.data.Resolve(1f + bomb.damagePerLevel * level, bomb.radiusPerLevel * level);

        return UIText.Row($"{blast.damage:0} DMG",
                          $"{UIText.Metres(blast.radius)} BLAST",
                          $"{bomb.ChargesAt(level)} CHARGES");
    }

    // ======================================================================
    void BuildItems()
    {
        var equipped = Loadout.SelectedConsumable(catalog);

        foreach (var item in catalog.consumables)
        {
            if (item == null || item.data == null) continue;

            int held = Loadout.Stock(item.id);
            bool full = held >= item.maxCarried;
            bool isEquipped = equipped == item;

            var captured = item;

            var content = new StoreItemCard.Content
            {
                title = item.Label,
                description = item.data.description,
                stats = item.data.Effects,
                // The same two words a gun and a bomb use, so one vocabulary covers the
                // whole shop, with the count after it because a consumable is the only
                // thing here you can have more than one of.
                state = isEquipped ? UIText.Row("EQUIPPED", UIText.Count(held))
                      : held > 0 ? UIText.Row("OWNED", UIText.Count(held))
                      : "",
                accent = item.data.tint,

                primaryShown = true,

                // The pack size lives on the button rather than in the stats, because it
                // is not a property of the drink -- it is what pressing this costs you
                // and what it gives you, which is the one place it matters.
                primaryLabel = full ? "BELT FULL" : $"BUY x{item.packSize}",
                primaryPrice = full ? 0 : item.price,
                primaryEnabled = !full && Wallet.CanAfford(item.price),
                primaryAction = () => Ask($"{captured.Label} x{captured.packSize}", captured.price, () => BuyItem(captured)),
                render = renders != null ? renders.For(item.id) : null,
                iconId = "flask",

                // Only one kind goes into a level, so the second button picks which. A
                // player who owns two of these is being asked a real question, and the
                // answer is not "whichever one is first in the catalog".
                upgradeShown = !isEquipped,
                upgradeLabel = "CARRY",
                upgradeEnabled = true,
                upgradeAction = () => EquipItem(captured)
            };

            Spawn().Bind(content);
        }
    }

    void EquipItem(StoreCatalog.ConsumableEntry item)
    {
        if (item == null) return;

        Loadout.SelectConsumable(item.id);
        Confirm($"{item.Label} is on the belt.");
    }

    // ======================================================================
    void BuildHealth()
    {
        int level = Loadout.HealthUpgradeLevel;
        bool canUpgrade = catalog.healthUpgrades.CanUpgrade(level);
        int price = catalog.healthUpgrades.PriceAt(level);

        var content = new StoreItemCard.Content
        {
            title = "Vitality",
            description = "Permanent health and shield. No cap, only a rising price.",

            // What the *next* one buys, not what the last ones did. Showing the running
            // total meant a fresh profile opened this shelf to "+0 HP   ·   +0 SHIELD",
            // which is a true statement and a useless one -- the player is reading the
            // card to find out what pressing the button gets them. The total they have
            // already bought is the level in the state line.
            stats = UIText.Row($"+{catalog.healthPerLevel:0} HP", $"+{catalog.shieldPerLevel:0} SHIELD"),

            state = level > 0
                ? UIText.Row($"LEVEL {level}",
                             $"+{catalog.healthPerLevel * level:0} HP",
                             $"+{catalog.shieldPerLevel * level:0} SHIELD")
                : "",
            accent = new Color(0.45f, 0.95f, 0.55f),

            // No pips: this is the one upgrade with no ceiling, and a row of five dots
            // would be a promise that it stops.
            upgradeMax = 0,

            primaryShown = true,
            primaryLabel = canUpgrade ? "UPGRADE" : "MAXED",
            primaryPrice = canUpgrade ? price : 0,
            primaryEnabled = canUpgrade && Wallet.CanAfford(price),
            primaryAction = () => Ask("Vitality upgrade", price, UpgradeHealth),
            render = renders != null ? renders.For("vitality") : null,
            iconId = "medkit"
        };

        Spawn().Bind(content);
    }

    // ======================================================================
    // Purchases. Every one is: refuse, take the money, then grant -- in that order.
    // ======================================================================
    void BuyGun(StoreCatalog.GunEntry gun)
    {
        if (gun == null || Loadout.OwnsGun(gun)) return;

        if (!Wallet.TrySpend(gun.price))
        {
            Deny($"{gun.Label} costs {Wallet.Format(gun.price)}. You have {Wallet.Format(Wallet.Balance)}.");
            return;
        }

        Loadout.GrantGun(gun.id);
        Loadout.SelectGun(gun.id);

        Confirm($"{gun.Label} bought and equipped.");
    }

    void EquipGun(StoreCatalog.GunEntry gun)
    {
        if (gun == null || !Loadout.OwnsGun(gun)) return;

        Loadout.SelectGun(gun.id);
        Confirm($"{gun.Label} equipped.");
    }

    void UpgradeGun(StoreCatalog.GunEntry gun)
    {
        if (gun == null || !Loadout.OwnsGun(gun)) return;

        int level = Loadout.GunUpgradeLevel(gun.id);

        if (!gun.upgrades.CanUpgrade(level))
        {
            Deny($"{gun.Label} is fully upgraded.");
            return;
        }

        int price = gun.upgrades.PriceAt(level);

        if (!Wallet.TrySpend(price))
        {
            Deny($"That upgrade costs {Wallet.Format(price)}. You have {Wallet.Format(Wallet.Balance)}.");
            return;
        }

        Loadout.SetGunUpgradeLevel(gun.id, level + 1);
        Confirm($"{gun.Label} upgraded to level {level + 1}.");
    }

    // ------------------------------------------------------------------
    void BuyBomb(StoreCatalog.BombEntry bomb)
    {
        if (bomb == null || Loadout.OwnsBomb(bomb)) return;

        if (!Wallet.TrySpend(bomb.price))
        {
            Deny($"{bomb.Label} costs {Wallet.Format(bomb.price)}. You have {Wallet.Format(Wallet.Balance)}.");
            return;
        }

        Loadout.GrantBomb(bomb.id);
        Loadout.SelectBomb(bomb.id);

        Confirm($"{bomb.Label} bought and equipped.");
    }

    void EquipBomb(StoreCatalog.BombEntry bomb)
    {
        if (bomb == null || !Loadout.OwnsBomb(bomb)) return;

        Loadout.SelectBomb(bomb.id);
        Confirm($"{bomb.Label} equipped.");
    }

    void UpgradeBomb(StoreCatalog.BombEntry bomb)
    {
        if (bomb == null || !Loadout.OwnsBomb(bomb)) return;

        int level = Loadout.BombUpgradeLevel(bomb.id);

        if (!bomb.upgrades.CanUpgrade(level))
        {
            Deny($"{bomb.Label} is fully upgraded.");
            return;
        }

        int price = bomb.upgrades.PriceAt(level);

        if (!Wallet.TrySpend(price))
        {
            Deny($"That upgrade costs {Wallet.Format(price)}. You have {Wallet.Format(Wallet.Balance)}.");
            return;
        }

        Loadout.SetBombUpgradeLevel(bomb.id, level + 1);
        Confirm($"{bomb.Label} upgraded to level {level + 1}.");
    }

    // ------------------------------------------------------------------
    void BuyItem(StoreCatalog.ConsumableEntry item)
    {
        if (item == null) return;

        if (Loadout.Stock(item.id) >= item.maxCarried)
        {
            Deny($"You cannot carry more than {item.maxCarried} {item.Label}.");
            return;
        }

        if (!Wallet.TrySpend(item.price))
        {
            Deny($"A pack costs {Wallet.Format(item.price)}. You have {Wallet.Format(Wallet.Balance)}.");
            return;
        }

        // Clamped on the way in, so a pack bought with two slots free adds two. The
        // refund is the honest thing to do with the rest: the alternative is charging
        // full price for a pack the belt could not hold.
        int added = Loadout.AddStock(item, item.packSize);
        int wasted = item.packSize - added;

        if (wasted > 0)
        {
            int refund = Mathf.RoundToInt(item.price * (wasted / (float)item.packSize));
            Wallet.Add(refund);

            Confirm($"{added} {item.Label} added -- the belt was nearly full, so " +
                    $"{Wallet.Format(refund)} was refunded.");
            return;
        }

        Confirm($"{added} {item.Label} added to the belt.");
    }

    // ------------------------------------------------------------------
    void UpgradeHealth()
    {
        int level = Loadout.HealthUpgradeLevel;

        if (!catalog.healthUpgrades.CanUpgrade(level))
        {
            Deny("Vitality is fully upgraded.");
            return;
        }

        int price = catalog.healthUpgrades.PriceAt(level);

        if (!Wallet.TrySpend(price))
        {
            Deny($"That upgrade costs {Wallet.Format(price)}. You have {Wallet.Format(Wallet.Balance)}.");
            return;
        }

        Loadout.SetHealthUpgradeLevel(level + 1);
        Confirm($"Vitality upgraded to level {level + 1}.");
    }

    // ======================================================================
    void Confirm(string message)
    {
        if (sounds != null) sounds.PlayPurchase();

        // Every buy and every upgrade reaches here and nothing else does, so this is the
        // one line that can count a purchase -- the same reasoning that puts the coin
        // payout in GameSession.RecordResult rather than at each of six spend sites, where
        // a seventh added later would silently count nothing.
        PlayerStats.RecordPurchase();
        Report(message);
        Rebuild();
        Purchased?.Invoke();
    }

    void Deny(string message)
    {
        if (sounds != null) sounds.PlayBack();

        Report(message);
        Rebuild();
    }

    void ShowBalance()
    {
        if (balanceText != null)
            balanceText.text = $"{Wallet.Format(Wallet.Balance)} <size=65%>COINS</size>";
    }

    void Report(string message)
    {
        if (statusText != null) statusText.text = message ?? "";
    }

    /// <summary>
    /// Sizes the cards to the space there actually is, in both directions, and chooses
    /// how many go across. <see cref="UIGrid"/> owns the arithmetic, shared with the
    /// arena grid and the level select.
    ///
    /// Two across on a handset at most. A store card is a name, three statistics, a
    /// sentence and a price, so the question is not how much area each one gets but
    /// whether the sentence can be read -- and on a 155mm screen the answer past two
    /// columns is no, whatever the measurement prefers.
    /// </summary>
    /// <summary>
    /// Cards as wide as the columns allow, in a grid that scrolls down. A shelf of six guns
    /// fitted to one screen made every card too small to read on a phone.
    /// </summary>
    void FitGrid()
    {
        if (cardParent == null) return;
        if (_layout == null) _layout = cardParent.GetComponent<GridLayoutGroup>();
        if (_layout == null) return;
        var box = scroll != null && scroll.viewport != null ? scroll.viewport : cardParent;
        float width = box.rect.width;
        float height = box.rect.height;
        if (width <= 1f || height <= 1f) return;
        if (Mathf.Abs(width - _fittedWidth) < 0.5f && Mathf.Abs(height - _fittedHeight) < 0.5f) return;
        _fittedWidth = width;
        _fittedHeight = height;
        int columns = DeviceProfile.CurrentForm switch
        {
            DeviceProfile.Form.Handset => 2,
            DeviceProfile.Form.Tablet => 3,
            _ => Mathf.Max(1, gridColumns),
        };
        float cell = (width - _layout.padding.left - _layout.padding.right - _layout.spacing.x * (columns - 1)) / columns;
        _layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        _layout.constraintCount = columns;
        _layout.cellSize = new Vector2(cell, cell * cardAspect);
    }

    // ======================================================================
    // Confirmation.
    // ======================================================================

    GameObject _confirm;
    TMP_Text _confirmTitle, _confirmBody;
    System.Action _pending;

    /// <summary>The dialog's BUY button, for the store test, which confirms like a player does.</summary>
    public FlatButton ConfirmBuyButton { get; private set; }

    public bool Confirming => _confirm != null && _confirm.activeSelf;

    /// <summary>
    /// Every purchase asks first: coins take a level to earn, and a mis-tap on a phone should
    /// not spend them. Equipping is free and does not ask.
    /// </summary>
    void Ask(string what, int price, System.Action buy)
    {
        if (buy == null) return;
        if (_confirm == null) BuildConfirm();
        if (_confirm == null) { buy(); return; }
        var t = UITheme.Active;
        _pending = buy;
        _confirmTitle.text = $"Buy {what}?";
        int after = Wallet.Balance - price;
        _confirmBody.text = UIText.Row($"{t.Tabular(Wallet.Format(price), heading: false)} COINS",
                                       $"{t.Tabular(Wallet.Format(Mathf.Max(0, after)), heading: false)} LEFT AFTER");
        _confirm.SetActive(true);
        _confirm.transform.SetAsLastSibling();
        if (ConfirmBuyButton != null) ConfirmBuyButton.Select();
    }

    void BuildConfirm()
    {
        if (panel == null || _confirm != null) return;
        var t = UITheme.Active;
        var shade = UIKit.Panel(panel.transform, "Confirm", UIKit.PanelTone.Background, t);
        shade.color = new Color(t.background.r, t.background.g, t.background.b, 0.85f);
        shade.borderColor = new Color(0, 0, 0, 0);
        shade.raycastTarget = true;
        UIKit.Fill(shade.rectTransform);
        var card = UIKit.Panel(shade.transform, "Dialog", UIKit.PanelTone.Panel, t);
        card.stripeSide = FlatRect.Side.Top;
        card.stripeColor = t.accent;
        card.stripePixels = t.stripePixels;
        var rt = card.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(520f, 0f);
        card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        card.gameObject.AddComponent<FitInsideParent>();
        var col = UIKit.Column(card, 14f, new RectOffset(28, 28, 24, 24));
        col.childForceExpandHeight = false;
        col.childControlHeight = true;
        _confirmTitle = UIKit.Text(card.transform, "Title", "", UIKit.TextRole.Title, t);
        _confirmBody = UIKit.Text(card.transform, "Body", "", UIKit.TextRole.Label, t);
        _confirmBody.color = t.textSecondary;
        var row = UIKit.Rect(card.transform, "Buttons");
        var r = UIKit.Row(row, 10f, null, TextAnchor.MiddleRight);
        r.childForceExpandWidth = false;
        UIKit.Size(row).minHeight = UIKit.ControlHeight;
        var cancel = UIKit.Button(row, "Cancel", "Cancel", FlatButton.Variant.Secondary, null, t);
        cancel.onClick.AddListener(() => { _pending = null; _confirm.SetActive(false); });
        var buy = UIKit.Button(row, "Buy", "Buy", FlatButton.Variant.Primary, "coin", t);
        buy.onClick.AddListener(() =>
        {
            var act = _pending;
            _pending = null;
            _confirm.SetActive(false);
            act?.Invoke();
        });
        ConfirmBuyButton = buy;
        _confirm = shade.gameObject;
        _confirm.SetActive(false);
    }
}
