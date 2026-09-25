using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One thing for sale: a render of the item on a plain panel, its name, one line about it,
/// what it does, the price with a coin, and BUY -- or OWNED / EQUIPPED once it is the
/// player's, with EQUIP and UPGRADE in its place.
///
/// Built once by the dashboard builder as a hidden template and cloned per item at runtime,
/// the same way <see cref="ArenaCard"/> and <see cref="LevelButton"/> are. Everything it shows
/// comes from <see cref="Content"/>, which <see cref="StorePanel"/> fills from the catalog and
/// the save; the card decides nothing.
///
/// <b>A price the player cannot pay is red and its button is off</b>, rather than a button that
/// takes the click and then says no. The red is on the number, where the eye already is.
/// </summary>
public class StoreItemCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    /// <summary>
    /// Everything the card draws, filled in by <see cref="StorePanel"/>. A struct with
    /// named fields rather than a long argument list, so a call site says what it means.
    /// </summary>
    public struct Content
    {
        public string title;
        public string description;
        public string stats;
        public string state;        // OWNED, EQUIPPED, or empty
        public Color accent;
        public Texture render;      // the item's own model, rendered; null draws the icon
        public string iconId;

        public int upgradeLevel;
        public int upgradeMax;

        public string primaryLabel;
        public int primaryPrice;
        public bool primaryEnabled;
        public bool primaryShown;
        public Action primaryAction;

        public string upgradeLabel;
        public int upgradePrice;
        public bool upgradeEnabled;
        public bool upgradeShown;
        public Action upgradeAction;
    }

    [Header("Parts")]
    public FlatRect frame;
    public FlatRect stripe;
    public RawImage render;
    public Image fallbackIcon;
    public TMP_Text titleText;
    public TMP_Text descriptionText;
    public TMP_Text statsText;
    public TMP_Text stateText;
    public GameObject priceRow;
    public TMP_Text priceText;
    public Image priceIcon;
    public FlatRect[] upgradePips = new FlatRect[0];

    public FlatButton primaryButton;
    public TMP_Text primaryLabelText;
    public FlatButton upgradeButton;
    public TMP_Text upgradeLabelText;

    bool _hover;

    public void Bind(Content c)
    {
        var t = UITheme.Active;
        name = $"Store_{c.title}";

        if (titleText != null) titleText.text = (c.title ?? "").ToUpperInvariant();
        if (descriptionText != null) descriptionText.text = c.description ?? "";
        if (statsText != null) statsText.text = c.stats ?? "";
        if (stateText != null)
        {
            stateText.text = c.state ?? "";
            stateText.gameObject.SetActive(!string.IsNullOrEmpty(c.state));
            stateText.color = t.success;
        }
        if (stripe != null) stripe.color = c.accent.a > 0f ? c.accent : t.border;

        if (render != null)
        {
            render.texture = c.render;
            render.gameObject.SetActive(c.render != null);
        }
        if (fallbackIcon != null)
        {
            fallbackIcon.sprite = t.IconSprite(string.IsNullOrEmpty(c.iconId) ? "shopping-cart" : c.iconId);
            fallbackIcon.gameObject.SetActive(c.render == null);
        }

        // The price of the one purchase on the card, red when it cannot be paid.
        int price = c.primaryShown && c.primaryPrice > 0 ? c.primaryPrice
                  : c.upgradeShown && c.upgradePrice > 0 ? c.upgradePrice : 0;
        if (priceRow != null) priceRow.SetActive(price > 0);
        bool afford = Wallet.CanAfford(price);
        if (priceText != null)
        {
            priceText.text = t.Tabular(Wallet.Format(price));
            priceText.color = afford ? t.accent : t.danger;
        }
        if (priceIcon != null) priceIcon.color = afford ? t.accent : t.danger;

        for (int i = 0; i < upgradePips.Length; i++)
        {
            if (upgradePips[i] == null) continue;
            // Pips are progress on something owned; on an item not bought yet they would read
            // as levels the player already has.
            bool used = i < c.upgradeMax && (c.upgradeShown || c.upgradeLevel > 0);
            upgradePips[i].gameObject.SetActive(used);
            if (used) upgradePips[i].color = i < c.upgradeLevel ? t.accent : t.border;
        }

        BindButton(primaryButton, primaryLabelText, c.primaryShown, c.primaryLabel, c.primaryEnabled, c.primaryAction);
        BindButton(upgradeButton, upgradeLabelText, c.upgradeShown, c.upgradeLabel, c.upgradeEnabled, c.upgradeAction);
        Paint();
    }

    static void BindButton(FlatButton button, TMP_Text label, bool shown, string text, bool enabled, Action action)
    {
        if (button == null) return;
        button.gameObject.SetActive(shown);
        if (!shown) return;
        button.onClick.RemoveAllListeners();
        if (action != null) button.onClick.AddListener(() => action());
        button.interactable = enabled;
        if (label != null) label.text = (text ?? "").ToUpperInvariant();
    }

    public void OnPointerEnter(PointerEventData e) { _hover = true; Paint(); }
    public void OnPointerExit(PointerEventData e) { _hover = false; Paint(); }

    void Paint()
    {
        if (frame == null) return;
        var t = UITheme.Active;
        frame.borderColor = _hover ? t.borderHover : t.border;
        frame.color = _hover ? t.panelRaised : t.panel;
    }
}
