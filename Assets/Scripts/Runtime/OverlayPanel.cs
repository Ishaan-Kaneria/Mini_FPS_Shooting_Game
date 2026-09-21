using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A screen that opens over the dashboard and puts back what was showing.
///
/// <b>One base rather than a copy per panel</b>, for the reason <see cref="HoverCard"/> is
/// one base: the level select and the store already carry the same twenty lines, and the
/// three screens sit on top of each other, so a panel that closes differently from the one
/// it replaced reads as the interface being unreliable.
///
/// It also owns the trap that costs a whole screen when it is got wrong.
/// <b>The component does not live on the panel it hides.</b> Put it there and the panel can
/// never be shown: the builder leaves it off, so <c>Awake</c> has not run, and the first
/// <c>SetActive(true)</c> is what finally runs it -- which switches the object straight
/// back off. The screen then simply never opens, with nothing logged.
/// <see cref="HideAtLoad"/> says so instead of failing silently.
/// </summary>
public abstract class OverlayPanel : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("The child this switches on and off. Never this object.")]
    public GameObject panel;

    [Tooltip("Closes the panel. Every screen needs a way out that is not a keyboard.")]
    public Button backButton;

    public bool IsOpen => panel != null && panel.activeSelf;

    /// <summary>Raised after the panel closes, so the dashboard can put itself back.</summary>
    public event System.Action Closed;

    protected virtual void Awake() => HideAtLoad();

    protected virtual void Start()
    {
        if (backButton == null) return;

        backButton.onClick.RemoveAllListeners();
        backButton.onClick.AddListener(Close);
    }

    protected virtual void Update()
    {
        if (!IsOpen) return;

        // Escape and Q back out. A screen with no keyboard way out traps anyone whose
        // pointer is not where they expected it to be -- and on a browser Escape is also
        // what the player has just pressed to get their cursor back.
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Q)) Close();
    }

    public void Open()
    {
        if (panel == null) return;

        panel.SetActive(true);
        OnOpened();
    }

    public void Close()
    {
        if (panel == null) return;

        panel.SetActive(false);
        OnClosed();
        Closed?.Invoke();
    }

    /// <summary>Fill the screen in. Called every time it opens, not once.</summary>
    protected abstract void OnOpened();

    protected virtual void OnClosed() { }

    void HideAtLoad()
    {
        if (panel == null) return;

        if (panel == gameObject)
        {
            Debug.LogError($"[{GetType().Name}] The panel is this object, so hiding it would " +
                           "stop it ever being shown again. Put this component on the canvas " +
                           "and point it at a child.", this);
            return;
        }

        panel.SetActive(false);
    }
}
