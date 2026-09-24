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
            _ => ScreenInfo.SimulatedTouch ??
                 (Application.isMobilePlatform || Application.isEditor ||
                  WebDevice.IsTouchOnly ||
                  (UnityEngine.InputSystem.Touchscreen.current != null &&
                   UnityEngine.InputSystem.Mouse.current == null))
        };

        // Touch support alone is not a touch device. A touchscreen laptop has a
        // touchscreen and still has a mouse -- and MobileInput.Active is what
        // gates PlayerMotor's click-to-lock recovery, so claiming that machine is
        // mobile leaves it with on-screen buttons and no mouse look at all. In a
        // browser that is the only path there is: the initial lock in Start always
        // fails, because a browser will not capture the pointer without a gesture.
        MobileInput.Reset();
        MobileInput.Active = show;
        if (show && GetComponent<TouchLayout>() == null) gameObject.AddComponent<TouchLayout>();

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
        if (show && (Application.isMobilePlatform || WebDevice.IsTouchOnly))
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
        // Fold the controls away when input is gated (game over, pause), and while the
        // player is on a gamepad -- a phone with a controller paired has no use for
        // buttons drawn over the fight, and they come back the moment a thumb touches
        // the glass, because that switches the scheme back to touch.
        if (group == null || !MobileInput.Active) return;

        bool playable = PlayerMotor.InputEnabled && GameInput.Scheme != InputScheme.Gamepad;
        group.alpha = playable ? GameSettings.TouchOpacity : 0f;
        group.blocksRaycasts = playable;

        if (!playable) MobileInput.Reset();
    }
}
