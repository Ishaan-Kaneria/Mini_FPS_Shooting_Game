using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The pointer response shared by every clickable tile in the menus -- the arena cards
/// on the dashboard, the level tiles behind them, and the store cards over both.
///
/// It existed three times, once per card, and the three copies had already drifted to
/// three different growth amounts for no reason anybody chose. That is the same
/// complaint <see cref="UIText"/> answers for labels: none of it is a bug and all of it
/// is noticeable, because the three screens sit on top of each other and a card that
/// grows by a different amount than the one it replaced reads as the interface being
/// slightly unreliable. One number now, and one place to change it.
///
/// <b>Scale, never position.</b> A GridLayoutGroup owns anchoredPosition on every child
/// it places, so a hover that wrote a position each frame fought the layout and won:
/// every card was dragged onto the same remembered spot and the grid rendered as a
/// single card with five hidden underneath it, while every structural check still
/// counted six. localScale is not layout-driven, so nothing is being argued with.
/// <c>VerifyFlow</c> fails if two cards share a position.
///
/// <b>Unscaled time</b>, because the dashboard is reachable from a frozen game -- after
/// a death, with timeScale at zero -- and a hover that only animates at timeScale 1
/// would look broken exactly then.
///
/// A subclass adds what is particular to it by overriding <see cref="ApplyHover"/> and
/// calling base, and says when the pointer should do nothing at all -- a locked level,
/// say -- by overriding <see cref="Hoverable"/>.
/// </summary>
[DisallowMultipleComponent]
public abstract class HoverCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Hover")]
    [Tooltip("The border that lights up. Optional: a card without one still grows.")]
    public Image frame;

    [Tooltip("How much the card grows under the pointer. These tiles are the only thing " +
             "on their screens you can click, so they have to look clickable.\n\n" +
             "Shared across all three kinds of card on purpose -- they are shown over " +
             "one another, and three growth amounts read as three different games.")]
    [Range(0f, 0.25f)] public float hoverScale = 0.035f;

    [Tooltip("How quickly it settles into the hovered state, in units of the animation " +
             "per second.")]
    [Min(0.1f)] public float hoverSpeed = 14f;

    public Color frameColor = new Color(0.18f, 0.20f, 0.24f, 1f);
    public Color frameHoverColor = new Color(0.95f, 0.75f, 0.35f, 1f);

    protected bool _hovered;
    protected float _hover;

    /// <summary>
    /// Whether the pointer may light this card up. False on a card that cannot be
    /// clicked, so a locked level does not highlight and then do nothing -- which reads
    /// as a broken button rather than as a locked one.
    /// </summary>
    protected virtual bool Hoverable => true;

    public void OnPointerEnter(PointerEventData eventData) => _hovered = Hoverable;
    public void OnPointerExit(PointerEventData eventData) => _hovered = false;

    /// <summary>
    /// Cleared on the way in, because these cards are cloned from a hidden template and
    /// shown and hidden as panels come and go -- a card switched off while the pointer
    /// was over it would come back still lit, with no pointer anywhere near it.
    /// </summary>
    protected virtual void OnEnable()
    {
        _hovered = false;
        ApplyHover(0f);
    }

    void Update()
    {
        float target = _hovered ? 1f : 0f;
        ApplyHover(Mathf.MoveTowards(_hover, target, hoverSpeed * Time.unscaledDeltaTime));
    }

    /// <summary>Grows the card and lights its frame. 0 is at rest, 1 is fully hovered.</summary>
    protected virtual void ApplyHover(float amount)
    {
        _hover = amount;

        transform.localScale = Vector3.one * (1f + hoverScale * amount);

        if (frame != null) frame.color = Color.Lerp(frameColor, frameHoverColor, amount);
    }
}
