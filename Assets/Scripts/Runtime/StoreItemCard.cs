using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One thing for sale: what it is, what it does, what it costs, and the one or two
/// buttons that act on it.
///
/// Deliberately generic. A gun, a bomb, an energy drink and the health upgrade are four
/// very different purchases, and giving each its own card type would mean four places
/// that have to agree about what "cannot afford" looks like. They fill in a
/// <see cref="Content"/> instead, and this draws whatever it is handed -- so the one
/// thing a player learns about reading a store card is true of every card in it.
///
/// Built once by the dashboard builder as a hidden template and cloned per item at
/// runtime, the same way <see cref="ArenaCard"/> and <see cref="LevelButton"/> are.
/// </summary>
[DisallowMultipleComponent]
public class StoreItemCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    /// <summary>
    /// Everything the card draws, filled in by <see cref="StorePanel"/>. A struct with
    /// named fields rather than a dozen arguments, because half of them are optional and
    /// a call with four trailing nulls is a call nobody can read.
    /// </summary>
    public struct Content
    {
        public string title;
        public string description;

        /// <summary>The numbers line -- damage, radius, what a drink restores.</summary>
        public string stats;

        /// <summary>"OWNED", "EQUIPPED", "x3 IN BELT". Empty draws nothing.</summary>
        public string state;

        public Color accent;

        /// <summary>Upgrade pips: how many are lit, and how many there are. 0 draws none.</summary>
        public int upgradeLevel;
        public int upgradeMax;

        // ---- the buy / equip button -------------------------------------
        public string primaryLabel;

        /// <summary>Price shown on the button. 0 prints no price, for "EQUIP".</summary>
        public int primaryPrice;
        public bool primaryEnabled;
        public bool primaryShown;
        public Action primaryAction;

        // ---- the upgrade button -----------------------------------------
        public string upgradeLabel;
        public int upgradePrice;
        public bool upgradeEnabled;
        public bool upgradeShown;
        public Action upgradeAction;
    }

    [Header("Parts")]
    public Image frame;
    public Image accentBar;

    public TMP_Text titleText;
    public TMP_Text descriptionText;
    public TMP_Text statsText;
    public TMP_Text stateText;

    [Tooltip("Left to right. Lit up to the upgrade level, dimmed for what is still to buy.")]
    public Image[] upgradePips;

    [Header("Buttons")]
    public Button primaryButton;
    public TMP_Text primaryLabelText;

    public Button upgradeButton;
    public TMP_Text upgradeLabelText;

    [Header("Feel")]
    public float hoverScale = 0.03f;
    public float hoverSpeed = 14f;

    public Color frameColor = new Color(0.18f, 0.20f, 0.24f, 1f);
    public Color frameHoverColor = new Color(0.95f, 0.75f, 0.35f, 1f);

    [Tooltip("Lit when the pip has been bought.")]
    public Color pipFilledColor = new Color(1f, 0.82f, 0.25f);

    public Color pipEmptyColor = new Color(1f, 1f, 1f, 0.12f);

    [Tooltip("A price the player cannot meet. Red rather than merely dim, because " +
             "\"you cannot afford this\" and \"this is not for sale\" are different " +
             "answers and a store that greys out both is a store that explains neither.")]
    public Color unaffordableColor = new Color(0.95f, 0.42f, 0.36f);

    public Color affordableColor = new Color(0.92f, 0.91f, 0.89f);

    bool _hovered;
    float _hover;

    void OnEnable()
    {
        _hovered = false;
        ApplyHover(0f);
    }

    public void Bind(Content content)
    {
        name = $"Store_{content.title}";

        if (titleText != null) titleText.text = (content.title ?? "").ToUpperInvariant();
        if (descriptionText != null) descriptionText.text = content.description ?? "";
        if (statsText != null) statsText.text = content.stats ?? "";

        if (stateText != null)
        {
            stateText.text = content.state ?? "";
            stateText.color = content.accent;
        }

        if (accentBar != null) accentBar.color = content.accent;

        BindPips(content);
        BindButton(primaryButton, primaryLabelText, content.primaryShown, content.primaryLabel,
                   content.primaryPrice, content.primaryEnabled, content.primaryAction);
        BindButton(upgradeButton, upgradeLabelText, content.upgradeShown, content.upgradeLabel,
                   content.upgradePrice, content.upgradeEnabled, content.upgradeAction);

        ApplyHover(0f);
    }

    void BindPips(Content content)
    {
        if (upgradePips == null) return;

        for (int i = 0; i < upgradePips.Length; i++)
        {
            if (upgradePips[i] == null) continue;

            bool used = i < content.upgradeMax;
            upgradePips[i].gameObject.SetActive(used);

            if (used) upgradePips[i].color = i < content.upgradeLevel ? pipFilledColor : pipEmptyColor;
        }
    }

    /// <summary>
    /// Sets up one of the two buttons, or hides it.
    ///
    /// The price is part of the label rather than a field beside it, so a button always
    /// says what pressing it costs -- and the label is coloured by affordability, which
    /// is the difference between a store that says no and one that just does nothing.
    /// </summary>
    void BindButton(Button button, TMP_Text label, bool shown, string text, int price,
                    bool enabled, Action action)
    {
        if (button == null) return;

        button.gameObject.SetActive(shown);
        if (!shown) return;

        button.onClick.RemoveAllListeners();
        if (action != null) button.onClick.AddListener(() => action());

        button.interactable = enabled;

        if (label == null) return;

        label.text = price > 0 ? $"{text}   {Wallet.Format(price)}" : text;
        label.color = enabled || price <= 0 || Wallet.CanAfford(price)
            ? affordableColor
            : unaffordableColor;
    }

    public void OnPointerEnter(PointerEventData eventData) => _hovered = true;
    public void OnPointerExit(PointerEventData eventData) => _hovered = false;

    void Update()
    {
        // Unscaled: the dashboard can be reached from a frozen game, and a hover that
        // only animates at timeScale 1 would look broken exactly then.
        float target = _hovered ? 1f : 0f;
        ApplyHover(Mathf.MoveTowards(_hover, target, hoverSpeed * Time.unscaledDeltaTime));
    }

    /// <summary>
    /// Grows the card and lights its frame. Scale rather than position, for the same
    /// reason ArenaCard and LevelButton do it: a GridLayoutGroup owns anchoredPosition on
    /// every child it places, and a hover that wrote one each frame would drag every card
    /// onto the same spot.
    /// </summary>
    void ApplyHover(float amount)
    {
        _hover = amount;

        transform.localScale = Vector3.one * (1f + hoverScale * amount);

        if (frame != null) frame.color = Color.Lerp(frameColor, frameHoverColor, amount);
    }
}
