using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Gives one interactive element a voice: a soft note under the pointer and a
/// firmer one when it is activated.
///
/// Attached by the dashboard builder to everything clickable, and it finds its
/// <see cref="UISounds"/> by walking up the hierarchy -- so a new button gets sound
/// by existing under the canvas rather than by being wired to anything.
///
/// It listens for clicks through the event system rather than through Button.onClick
/// so that it never competes for that list: the dashboard rebinds onClick at runtime
/// with RemoveAllListeners, which would throw a sound listener away with the rest.
/// </summary>
[DisallowMultipleComponent]
public class UIButtonSound : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    public enum Voice
    {
        Click,
        Launch,
        Back,

        /// <summary>
        /// Hovers, but says nothing when activated. For a control whose *outcome* has a
        /// sound of its own -- a store purchase plays a different note depending on
        /// whether it went through, and a click underneath it would be two sounds
        /// saying different things at once.
        /// </summary>
        Silent
    }

    [Tooltip("Which sound this element makes when it is activated.")]
    public Voice voice = Voice.Click;

    [Tooltip("Found in a parent when empty.")]
    public UISounds sounds;

    void Awake()
    {
        if (sounds == null) sounds = GetComponentInParent<UISounds>(true);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (sounds != null) sounds.PlayHover();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (sounds == null) return;

        // A disabled control should stay silent, or an arena that cannot be loaded
        // sounds exactly like one that can.
        var selectable = GetComponent<UnityEngine.UI.Selectable>();
        if (selectable != null && !selectable.IsInteractable()) return;

        switch (voice)
        {
            case Voice.Launch: sounds.PlayLaunch(); break;
            case Voice.Back: sounds.PlayBack(); break;
            case Voice.Silent: break;
            default: sounds.PlayClick(); break;
        }
    }
}
