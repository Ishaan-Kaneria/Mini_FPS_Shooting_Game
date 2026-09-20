using UnityEngine;

/// <summary>
/// Switches this object off on a handset.
///
/// <b>Cutting content is the other half of making a phone readable, and the harder half
/// to argue for.</b> Type can be grown until it fits; a screen cannot be made bigger. A
/// dashboard that shows six arenas, a description under each and a five-row career panel
/// is a reasonable use of a monitor and is, on a 147mm screen, a wall the player has to
/// read their way through before they can press anything.
///
/// So the secondary text is marked here rather than deleted. It is genuinely useful on a
/// desktop, where there is room for it and a mouse to read it with, and it is the first
/// thing to go when there is not. What survives on a phone is what a player needs to
/// choose an arena: its name, its picture, and how they did there.
///
/// Deliberately a marker with no logic beyond the one test, so that what is hidden is a
/// decision visible in the builder beside the thing it hides, rather than a list kept
/// somewhere else that drifts out of step with the screen it describes.
/// </summary>
public class HideOnPhone : MonoBehaviour
{
    [Tooltip("Hidden on a handset. Left alone everywhere else.")]
    public bool hide = true;

    void Start()
    {
        if (hide && PhoneUI.Active) gameObject.SetActive(false);
    }
}
