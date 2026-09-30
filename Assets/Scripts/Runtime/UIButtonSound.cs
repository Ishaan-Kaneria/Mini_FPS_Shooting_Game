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

    /// <summary>The sound on a control, added if it has none. Kit buttons are born with one,
    /// so a caller that wants a different voice sets it here rather than adding a second.</summary>
    public static UIButtonSound On(GameObject go, Voice voice = Voice.Click)
    {
        var s = go.GetComponent<UIButtonSound>();
        if (s == null) s = go.AddComponent<UIButtonSound>();
        s.voice = voice;
        return s;
    }

    void Awake()
    {
        if (sounds == null) sounds = GetComponentInParent<UISounds>(true);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // A pointer, not a finger: a touch "enters" every button it lands on, and a hover
        // blip under every tap is noise.
        if (eventData != null && eventData.pointerId >= 0) return;
        if (sounds != null) sounds.PlayHover();
        else UISfx.Play(UISfx.Sound.Hover);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // A disabled control should stay silent, or an arena that cannot be loaded
        // sounds exactly like one that can.
        var selectable = GetComponent<UnityEngine.UI.Selectable>();
        if (selectable != null && !selectable.IsInteractable()) return;

        // No menu voice above this one (a pause card, a results screen, a dialog built
        // in an arena): the theme's sounds, through the one shared source.
        if (sounds == null)
        {
            switch (voice)
            {
                case Voice.Launch: UISfx.Play(UISfx.Sound.Launch); break;
                case Voice.Back: UISfx.Play(UISfx.Sound.Back); break;
                case Voice.Silent: break;
                default: UISfx.Play(UISfx.Sound.Click); break;
            }
            return;
        }

        switch (voice)
        {
            case Voice.Launch: sounds.PlayLaunch(); break;
            case Voice.Back: sounds.PlayBack(); break;
            case Voice.Silent: break;
            default: sounds.PlayClick(); break;
        }
    }
}
