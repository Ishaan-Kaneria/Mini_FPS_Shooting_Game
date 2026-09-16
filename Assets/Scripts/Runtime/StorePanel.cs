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
    public enum Tab { Guns, Bombs, Items, Health }

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

    public Color tabActiveColor = new Color(0.94f, 0.66f, 0.19f);
    public Color tabIdleColor = new Color(0.12f, 0.14f, 0.17f);

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

    public Tab CurrentTab => _tab;

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

        if (tabButtons == null) return;

        for (int i = 0; i < tabButtons.Length; i++)
        {
            if (tabButtons[i] == null) continue;

            var tab = (Tab)i;
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
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Q)) Close();
    }

    // ======================================================================
    public void Open()
    {
        _tab = Tab.Guns;

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
        _tab = tab;

        Report("");
        Rebuild();
        PaintTabs();

        _fittedWidth = _fittedHeight = -1f;
    }

    void PaintTabs()
    {
        if (tabButtons == null) return;

        for (int i = 0; i < tabButtons.Length; i++)
        {
            if (tabButtons[i] == null) continue;

            var colors = tabButtons[i].colors;
            colors.normalColor = (Tab)i == _tab ? tabActiveColor : tabIdleColor;
            colors.selectedColor = colors.normalColor;
            tabButtons[i].colors = colors;
        }
    }

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
            case Tab.Guns: BuildGuns(); break;
            case Tab.Bombs: BuildBombs(); break;
            case Tab.Items: BuildItems(); break;
            case Tab.Health: BuildHealth(); break;
        }

        if (_cards.Count == 0)
            Report($"Nothing in {HeadingFor(_tab)} yet. Add an entry to the store catalog.");
    }

    static string HeadingFor(Tab tab) => tab switch
    {
        Tab.Guns => "WEAPONS",
        Tab.Bombs => "EXPLOSIVES",
        Tab.Items => "SUPPLIES",
        _ => "UPGRADES"
    };

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
        var equipped = Loadout.SelectedGun(catalog);

        foreach (var gun in catalog.guns)
        {
            if (gun == null || gun.data == null) continue;

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
                primaryAction = owned ? () => EquipGun(captured) : () => BuyGun(captured),

                upgradeShown = owned,
                upgradeLabel = canUpgrade ? "UPGRADE" : "MAXED",
                upgradePrice = canUpgrade ? upgradePrice : 0,
                upgradeEnabled = canUpgrade && Wallet.CanAfford(upgradePrice),
                upgradeAction = () => UpgradeGun(captured)
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

        return $"{damage:0} DMG   ·   {rpm:0} RPM   ·   {magazine} MAG";
    }

    // ======================================================================
    void BuildBombs()
    {
        var equipped = Loadout.SelectedBomb(catalog);

        foreach (var bomb in catalog.bombs)
        {
            if (bomb == null || bomb.data == null) continue;

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
                primaryAction = owned ? () => EquipBomb(captured) : () => BuyBomb(captured),

                upgradeShown = owned,
                upgradeLabel = canUpgrade ? "UPGRADE" : "MAXED",
                upgradePrice = canUpgrade ? upgradePrice : 0,
                upgradeEnabled = canUpgrade && Wallet.CanAfford(upgradePrice),
                upgradeAction = () => UpgradeBomb(captured)
            };

            Spawn().Bind(content);
        }
    }

    static string BombStats(StoreCatalog.BombEntry bomb, int level)
    {
        var blast = bomb.data.Resolve(1f + bomb.damagePerLevel * level, bomb.radiusPerLevel * level);

        return $"{blast.damage:0} DMG   ·   {blast.radius:0.#} m   ·   x{bomb.ChargesAt(level)}";
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
                state = isEquipped ? $"ON BELT  x{held}" : held > 0 ? $"x{held} STORED" : "",
                accent = item.data.tint,

                primaryShown = true,

                // The pack size lives on the button rather than in the stats, because it
                // is not a property of the drink -- it is what pressing this costs you
                // and what it gives you, which is the one place it matters.
                primaryLabel = full ? "BELT FULL" : $"BUY x{item.packSize}",
                primaryPrice = full ? 0 : item.price,
                primaryEnabled = !full && Wallet.CanAfford(item.price),
                primaryAction = () => BuyItem(captured),

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
            stats = $"+{catalog.healthPerLevel * level:0} HP   ·   " +
                    $"+{catalog.shieldPerLevel * level:0} SHIELD",
            state = level > 0 ? $"LEVEL {level}" : "",
            accent = new Color(0.45f, 0.95f, 0.55f),

            // No pips: this is the one upgrade with no ceiling, and a row of five dots
            // would be a promise that it stops.
            upgradeMax = 0,

            primaryShown = true,
            primaryLabel = canUpgrade ? "UPGRADE" : "MAXED",
            primaryPrice = canUpgrade ? price : 0,
            primaryEnabled = canUpgrade && Wallet.CanAfford(price),
            primaryAction = UpgradeHealth
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

        Report(message);
        Rebuild();
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
    /// Sizes the cards to the space there actually is, in both directions. The same
    /// arithmetic as the arena grid and the level select, and for the same reason: a
    /// GridLayoutGroup has one fixed cell size and neither clips nor scrolls, so a cell
    /// that is only right at 16:9 draws the bottom row off the bottom of the window.
    /// </summary>
    void FitGrid()
    {
        if (cardParent == null) return;
        if (_layout == null) _layout = cardParent.GetComponent<GridLayoutGroup>();
        if (_layout == null) return;

        float width = cardParent.rect.width;
        float height = cardParent.rect.height;

        if (width <= 1f || height <= 1f) return;
        if (Mathf.Abs(width - _fittedWidth) < 0.5f &&
            Mathf.Abs(height - _fittedHeight) < 0.5f) return;

        _fittedWidth = width;
        _fittedHeight = height;

        int columns = Mathf.Max(1, gridColumns);
        int rows = Mathf.Max(1, Mathf.CeilToInt(_cards.Count / (float)columns));

        float byWidth = (width
                         - _layout.padding.left - _layout.padding.right
                         - _layout.spacing.x * (columns - 1)) / columns;

        float byHeight = (height
                          - _layout.padding.top - _layout.padding.bottom
                          - _layout.spacing.y * (rows - 1)) / rows / cardAspect;

        float cell = Mathf.Max(90f, Mathf.Min(byWidth, byHeight));

        _layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        _layout.constraintCount = columns;
        _layout.cellSize = new Vector2(cell, cell * cardAspect);
        _layout.childAlignment = TextAnchor.UpperCenter;
    }
}
