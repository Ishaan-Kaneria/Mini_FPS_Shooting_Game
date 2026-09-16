using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One arena on the dashboard: a preview image, a name, and what the player has
/// managed there.
///
/// Built once by the dashboard builder as a hidden template and cloned per catalog
/// entry at runtime, so adding an arena is an entry in <see cref="ArenaCatalog"/> and
/// never a rebuild of the menu scene.
/// </summary>
[DisallowMultipleComponent]
public class ArenaCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Parts")]
    public Button button;
    public Image preview;
    public Image frame;
    public Image accentBar;
    public TMP_Text nameText;
    public TMP_Text descriptionText;
    public TMP_Text bestText;

    [Header("Feel")]
    [Tooltip("How much the card grows under the pointer. The grid is the only thing on " +
             "this screen you can click, so it has to look clickable.")]
    public float hoverScale = 0.035f;

    public float hoverSpeed = 14f;
    public Color frameColor = new Color(0.18f, 0.20f, 0.24f, 1f);
    public Color frameHoverColor = new Color(0.95f, 0.75f, 0.35f, 1f);

    [Tooltip("How far the preview is held back from full brightness at rest. The card " +
             "lighting up under the pointer is most of what says it can be clicked.")]
    [Range(0f, 0.6f)] public float previewDim = 0.22f;

    bool _hovered;
    float _hover;

    /// <summary>The preview's colour before the hover tint. Either white over a
    /// screenshot, or the theme's own colour when there is no screenshot to show.</summary>
    Color _previewBase = Color.white;

    /// <summary>The catalog entry this card is showing. Null on the template.</summary>
    public ArenaCatalog.Entry Entry { get; private set; }

    void OnEnable()
    {
        _hovered = false;
        ApplyHover(0f);
    }

    public void Bind(ArenaCatalog.Entry entry, int bestWave, UnityEngine.Events.UnityAction onChosen)
    {
        Entry = entry;
        if (entry == null) return;

        name = $"Arena_{entry.Label}";

        if (nameText != null) nameText.text = entry.Label.ToUpperInvariant();
        if (descriptionText != null) descriptionText.text = entry.description ?? "";

        if (bestText != null)
            bestText.text = bestWave > 0 ? $"BEST  WAVE {bestWave}" : "NOT PLAYED";

        if (accentBar != null) accentBar.color = entry.Accent;

        if (preview != null)
        {
            if (entry.preview != null)
            {
                preview.sprite = Sprite.Create(
                    entry.preview,
                    new Rect(0f, 0f, entry.preview.width, entry.preview.height),
                    new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);

                _previewBase = Color.white;
            }
            else
            {
                // No screenshot: the theme's own floor colour still says something about
                // the place, and a card with a blank hole in it reads as broken.
                preview.sprite = null;
                _previewBase = entry.theme != null
                    ? entry.theme.floorColor
                    : new Color(0.15f, 0.16f, 0.18f);
            }

            ApplyHover(0f);
        }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            if (onChosen != null) button.onClick.AddListener(onChosen);
        }
    }

    public void OnPointerEnter(PointerEventData eventData) => _hovered = true;
    public void OnPointerExit(PointerEventData eventData) => _hovered = false;

    void Update()
    {
        // Unscaled: the dashboard is shown with the game frozen after a death, and a
        // hover that only animates at timeScale 1 would look broken exactly then.
        float target = _hovered ? 1f : 0f;
        ApplyHover(Mathf.MoveTowards(_hover, target, hoverSpeed * Time.unscaledDeltaTime));
    }

    /// <summary>
    /// Grows the card and lights its frame.
    ///
    /// Scale rather than position, and that is not a style choice. A GridLayoutGroup
    /// owns anchoredPosition on every child it places, so a hover that wrote a position
    /// each frame fought the layout and won -- every card was dragged to the same
    /// remembered spot and the grid rendered as a single card with five hidden
    /// underneath it. localScale is not layout-driven, so nothing is being argued with.
    /// </summary>
    void ApplyHover(float amount)
    {
        _hover = amount;

        transform.localScale = Vector3.one * (1f + hoverScale * amount);

        if (frame != null) frame.color = Color.Lerp(frameColor, frameHoverColor, amount);

        if (preview != null)
            preview.color = Color.Lerp(_previewBase * (1f - previewDim), _previewBase, amount);

        if (accentBar != null)
        {
            var bar = accentBar.rectTransform;
            bar.localScale = new Vector3(1f, Mathf.Lerp(1f, 2.4f, amount), 1f);
        }
    }
}
