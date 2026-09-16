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
    private static extern int FPSKitPointerIsCoarseOnly();
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
}
