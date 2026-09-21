using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Whether this screen is a phone, and what the interface should do about it.
///
/// <b>A menu authored for a monitor is unreadable on a handset, and the reason is
/// physical rather than a matter of taste.</b> Every panel in this kit is laid out
/// against a 1920x1080 reference canvas, so a 24-unit label is 24 pixels on a 1080p
/// monitor -- about 6mm, comfortable. The same label on a 2400x1080 phone resolves to
/// roughly 27 device pixels, and that phone packs 416 of them to the inch: 1.6mm. That
/// is not small type, it is type at about the size of the text on the back of a battery,
/// and no amount of squinting makes a six-card grid of it legible at arm's length.
///
/// The fix is one number rather than a pass over every label. Lowering the canvas's
/// reference resolution makes every reference unit cover more of the screen, so the type,
/// the padding, the cards and the spacing all grow together and the layout keeps its
/// proportions. Scaling the fonts alone would grow the words inside boxes that did not
/// grow with them, which is how text starts overflowing its card.
///
/// Nothing here is cached. Every member is a fresh read of <see cref="Screen"/> or
/// <see cref="MobileInput"/>, for the reason the rest of the kit's static helpers are
/// written that way: domain reload is off, and a cached "this is a phone" would survive
/// into the next play session in the editor and be wrong.
/// </summary>
public static class PhoneUI
{
    /// <summary>
    /// Under this many millimetres across, a screen is a handset however it got here.
    ///
    /// A flagship phone in landscape is about 147mm and a 7" tablet about 151mm, which is
    /// four millimetres apart -- so no line drawn here separates them, and this one does
    /// not try: both get the larger layout, and a 10" tablet at 246mm does not. An earlier
    /// note claimed the line sat "just above the phones" and left the tablet on the desktop
    /// side, which was never true of 165 and is worth not re-deriving. FPSKitDeviceTest
    /// pins both tablets so a retune has to face the choice rather than stumble into it.
    /// </summary>
    const float PhoneWidthMm = 165f;

    /// <summary>
    /// How much of the authored reference to keep on a phone. At 0.70 everything is
    /// about 1.4x its authored size, which lifts body type from roughly 1.6mm to 2.3mm
    /// and headings clear of it. Going further starts pushing cards off the screen --
    /// the grid is fitted to the box it is given, so a bigger unit means fewer, larger
    /// cards rather than a scrollbar.
    /// </summary>
    public const float ReferenceShrink = 0.70f;

    /// <summary>
    /// Bounds of a density any platform might genuinely report, handheld or not.
    ///
    /// Deliberately much wider than <see cref="TouchMetrics"/>'s handheld range: a desktop
    /// monitor is 96 dpi and a laptop 130-220, all of which that range rejects. Anything
    /// inside this is a real reading to measure with; anything outside -- 0, which is what
    /// a platform reports when it does not know -- is not a small screen, it is no answer.
    /// </summary>
    const float MinKnownDpi = 40f;
    const float MaxKnownDpi = 900f;

    /// <summary>Whether the platform gave a density worth measuring with at all.</summary>
    static bool DensityKnown
    {
        get
        {
            float dpi = Screen.dpi;
            return dpi >= MinKnownDpi && dpi <= MaxKnownDpi;
        }
    }

    /// <summary>
    /// The screen's width in millimetres, from the platform's own density.
    ///
    /// <b>Not <see cref="TouchMetrics.PixelsPerMillimetre"/>, and that is the whole bug this
    /// guards against.</b> That property exists to size a thumb target, so when the platform
    /// gives it nothing it substitutes a typical phone -- 400 dpi -- which is exactly the
    /// right guess for its own job and a circular one for this. Asked through it, a 1920px
    /// monitor at the 96 dpi every desktop reports came back as 122mm across, under the
    /// 165mm line, and every desktop player was handed the handset layout: no arena
    /// descriptions, no career panel, everything at 1.43x. Measured on 109 dpi it was 163mm
    /// -- still under. The one machine it passed on was a high-dpi laptop, which reports a
    /// number inside the handheld range and so escapes the substitution, which is why this
    /// survived being looked at.
    /// </summary>
    public static float ScreenWidthMm
        => DensityKnown ? Screen.width / (Screen.dpi / 25.4f) : float.PositiveInfinity;

    /// <summary>
    /// Whether to lay this out for a thumb at arm's length.
    ///
    /// Asked of the touch layer first, because that is the one signal that is certain --
    /// if the on-screen controls are up, this is a handset whatever the numbers say. The
    /// physical size is the fallback for the menus, which run before any of that exists.
    /// </summary>
    public static bool Active
        => IsHandset(Screen.dpi, Screen.width,
                     MobileInput.Active || WebDevice.IsTouchOnly,
                     Application.isMobilePlatform);

    /// <summary>
    /// The decision itself, with every reading handed in rather than read.
    ///
    /// Public <b>only</b> so <c>VerifyDevices</c> can drive it across the densities real
    /// hardware reports -- same reason <see cref="ControlSettings.CanRead"/> is. Batch mode
    /// has one screen and cannot pretend to be a 416 dpi handset or a 96 dpi monitor, so a
    /// check written against <see cref="Active"/> would assert whatever the build machine
    /// happens to be and pass identically with the rule deleted.
    /// </summary>
    /// <param name="dpi">What the platform claims, <c>0</c> included.</param>
    /// <param name="widthPixels">The screen's width in real pixels.</param>
    /// <param name="touchSignal">On-screen controls exist, or the browser says finger-only.</param>
    /// <param name="mobilePlatform">This is a handheld build, whatever its screen admits.</param>
    public static bool IsHandset(float dpi, int widthPixels, bool touchSignal, bool mobilePlatform)
    {
        // Certain, in order of certainty. On-screen controls existing settles it; so does
        // the browser telling us the only pointer is a finger.
        if (touchSignal) return true;

        // With a real density, measure. This is the path a phone on the dashboard takes --
        // TouchControls has not run there, so MobileInput is still false -- and equally the
        // path a desktop takes to be told it is not a phone.
        if (dpi >= MinKnownDpi && dpi <= MaxKnownDpi)
            return widthPixels / (dpi / 25.4f) < PhoneWidthMm;

        // No density to measure with. Ask the platform rather than guessing from pixels: a
        // handheld build is a handheld however little it will admit about its screen, and
        // everything else is a desktop. Guessing "small" here is what broke desktop.
        return mobilePlatform;
    }

    /// <summary>
    /// Grows everything on a canvas by shrinking what it measures itself against.
    ///
    /// Idempotent: it stores nothing, and re-applying the same shrink to an already
    /// shrunk canvas would compound, so the authored reference is read from the scaler's
    /// own <see cref="CanvasScaler.referenceResolution"/> exactly once per call and the
    /// caller is expected to call it once, at Start.
    /// </summary>
    public static void Apply(Canvas canvas)
    {
        if (canvas == null || !Active) return;

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return;

        scaler.referenceResolution = scaler.referenceResolution * ReferenceShrink;
    }
}
