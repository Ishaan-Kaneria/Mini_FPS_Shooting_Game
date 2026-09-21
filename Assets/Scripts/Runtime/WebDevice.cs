using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
#endif

/// <summary>
/// What the browser can tell us about the machine that a build cannot work out for
/// itself.
///
/// Everything here is false off the web, so callers can ask unconditionally.
/// </summary>
public static class WebDevice
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern int FPSKitFramebufferDpi();

    [DllImport("__Internal")]
    private static extern int FPSKitPointerIsCoarseOnly();

    [DllImport("__Internal")]
    private static extern void FPSKitExit();
#endif

    /// <summary>
    /// A device whose only pointer is a finger -- a phone or a tablet.
    ///
    /// Deliberately not Application.isMobilePlatform, which on WebGL is a user-agent
    /// match that an iPad fails: it has called itself a Macintosh since iPadOS 13.
    /// Deliberately not Input.touchSupported either, which a touchscreen laptop passes
    /// while still wanting mouse look.
    /// </summary>
    public static bool IsTouchOnly
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { return FPSKitPointerIsCoarseOnly() != 0; }
            catch (EntryPointNotFoundException) { return false; }
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Whether this build is running inside a browser at all.
    ///
    /// The density below only means something on the web, and a caller that has to
    /// remember to ask this first is a caller that eventually forgets. It is here so
    /// <see cref="TouchMetrics"/> can state the branch once.
    /// </summary>
    public static bool InBrowser
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        get => true;
#else
        get => false;
#endif
    }

    /// <summary>
    /// The framebuffer's density in pixels per inch as the page measures it, or 0 off
    /// the web.
    ///
    /// <b>Screen.dpi is not this, and in a browser it is not anything.</b> Unity's WebGL
    /// runtime reports 96 times the pixel ratio the page configured, so with a phone
    /// rendered at ratio 1 it says 96: below the touch layer's handheld floor, which
    /// then substituted a typical phone's 400, and inside DeviceProfile's believable
    /// range, which then measured a 152mm handset as a 242mm tablet. The controls were
    /// sized against a density no device has and the menus were laid out for the wrong
    /// class of machine, from one reading.
    ///
    /// The page answers instead, from the CSS pixel and the ratio it renders at. See
    /// <c>FPSKitFramebufferDpi</c> in the jslib for why a CSS pixel is a better ruler
    /// than a number that claims to be one.
    /// </summary>
    public static float FramebufferDpi
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { return Mathf.Max(1f, FPSKitFramebufferDpi() / 1000f); }
            catch (System.EntryPointNotFoundException) { return 0f; }
#else
            return 0f;
#endif
        }
    }

    /// <summary>
    /// Leaves the game on the web, and does nothing anywhere else.
    ///
    /// Application.Quit() in a browser only tears down the player and leaves a dead
    /// canvas on the page, so the page has to be the thing that answers: the tab is
    /// closed where the browser permits it, and otherwise replaced with a sign-off that
    /// offers a reload. GameDirector.ExitApplication is the only caller.
    /// </summary>
    public static void Exit()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try { FPSKitExit(); }
        catch (EntryPointNotFoundException)
        {
            // An older build of the page without the export. Nothing useful to do, and
            // throwing out of "quit" would be worse than quietly staying put.
        }
#endif
    }
}
