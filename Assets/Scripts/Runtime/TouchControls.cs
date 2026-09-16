using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Root of the on-screen control layer. Decides whether to show itself and keeps
/// the static MobileInput state clean across play sessions.
/// </summary>
public class TouchControls : MonoBehaviour
{
    public enum ShowMode { OnTouchDevicesOnly, Always, Never }

    [Tooltip("OnTouchDevicesOnly hides the controls on desktop builds but keeps them " +
             "visible in the editor so you can test without deploying. A touchscreen " +
             "laptop counts as desktop: it reports touch support, but it also has a " +
             "mouse, and claiming it is a touch device costs it mouse look entirely.")]
    public ShowMode showMode = ShowMode.OnTouchDevicesOnly;

    [Tooltip("Hidden automatically while the game over screen is up.")]
    public CanvasGroup group;

    void Awake()
    {
        if (group == null) group = GetComponent<CanvasGroup>();

        bool show = showMode switch
        {
            ShowMode.Always => true,
            ShowMode.Never => false,
            _ => Application.isMobilePlatform || Application.isEditor ||
                 (Input.touchSupported && !Input.mousePresent)
        };

        // Touch support alone is not a touch device. A touchscreen laptop reports
        // Input.touchSupported and still has a mouse -- and MobileInput.Active is what
        // gates PlayerMotor's click-to-lock recovery, so claiming that machine is
        // mobile leaves it with on-screen buttons and no mouse look at all. In a
        // browser that is the only path there is: the initial lock in Start always
        // fails, because a browser will not capture the pointer without a gesture.
        MobileInput.Reset();
        MobileInput.Active = show;

        if (group != null)
        {
            group.alpha = show ? 1f : 0f;
            group.interactable = show;
            group.blocksRaycasts = show;
        }
        else
        {
            gameObject.SetActive(show);
        }

        // A touch player has no cursor to lock, and locking it would block look input.
        if (show && Application.isMobilePlatform)
        {
            var motor = FindAnyObjectByType<PlayerMotor>();
            if (motor != null) motor.lockCursor = false;
        }
    }

    void OnDisable()
    {
        // With the touch layer gone there is nothing driving MobileInput, so it must
        // stop claiming to be active or the desktop input path stays suppressed.
        MobileInput.Active = false;
        MobileInput.Reset();
    }

    void Update()
    {
        // Fold the controls away when input is gated (game over, pause).
        if (group == null || !MobileInput.Active) return;

        bool playable = PlayerMotor.InputEnabled;
        group.alpha = playable ? 1f : 0f;
        group.blocksRaycasts = playable;

        if (!playable) MobileInput.Reset();
    }
}
