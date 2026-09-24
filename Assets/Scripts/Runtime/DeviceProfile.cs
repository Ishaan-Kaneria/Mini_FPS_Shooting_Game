using UnityEngine;

/// <summary>
/// What kind of thing the game is being played on, and what that implies for how a
/// screen is arranged.
///
/// <b>The thing this replaces was a single bool and a scale factor.</b> `PhoneUI.Active`
/// answered "is this a phone", and a phone got the desktop canvas measured against a
/// smaller reference so everything on it came out 1.43x -- plus `HideOnPhone` deleting
/// whatever then no longer fitted. That is a magnified desktop with things missing, and
/// it fails for a reason no amount of retuning the scale factor reaches: <b>what breaks a
/// six-card grid on a handset is that it is a six-card grid, not that the cards are the
/// wrong size.</b> Scaling preserves arrangement and density, which is precisely what has
/// to change.
///
/// So nothing here returns a scale. It returns facts, and the screens branch on them to
/// choose an arrangement they were each authored for.
///
/// Three axes, deliberately separate, because real hardware varies them independently:
///
/// <list type="bullet">
/// <item><b>Reach</b> -- thumbs or a pointer. Not implied by size: a touchscreen laptop is
/// a pointer device in practice, and a tablet with a keyboard is still touched.</item>
/// <item><b>Form</b> -- how physically big the surface is, in millimetres rather than
/// pixels, because a hand is a fixed size and a pixel is not.</item>
/// <item><b>Orientation</b> -- which matters enormously on a handset and barely at all on
/// a monitor, and is the axis a width-only test cannot see.</item>
/// </list>
///
/// Nothing is cached. Every member is a fresh read, for the reason the rest of the kit's
/// static helpers are: domain reload is off, so a cached "this is a phone" would survive
/// into the next play session in the editor and be wrong. That also makes orientation
/// free -- a device rotated mid-session answers correctly on the next frame that asks.
/// </summary>
public static class DeviceProfile
{
    /// <summary>How the player physically reaches the interface.</summary>
    public enum Reach
    {
        /// <summary>A mouse or trackpad: pixel-accurate, hover exists, the hand is off the screen.</summary>
        Pointer,

        /// <summary>Thumbs: about 9mm of accuracy, no hover, and the hand covers what it touches.</summary>
        Touch,
    }

    /// <summary>
    /// How much surface there is, as a physical size rather than a resolution.
    ///
    /// The boundaries are where a person's relationship with the screen changes, not
    /// round numbers. Under ~165mm it is held in the hands that operate it. Under ~260mm
    /// it is a tablet -- held or propped, arm's length, still touched. Above that it sits
    /// on a desk and is looked at from a fixed distance.
    /// </summary>
    public enum Form
    {
        Handset,
        Tablet,
        Laptop,
        Desktop,
    }

    /// <summary>Millimetres. See <see cref="Form"/> for why each line is where it is.</summary>
    const float HandsetMaxMm = 165f;
    const float TabletMaxMm  = 260f;
    const float LaptopMaxMm  = 400f;

    /// <summary>
    /// Bounds of a density any platform might genuinely report, handheld or not.
    ///
    /// Wider than <see cref="TouchMetrics"/>'s handheld range on purpose: a monitor is
    /// 96 dpi and a laptop 130-220, all of which that range rejects and replaces with a
    /// phone's. Doing that here is what made every desktop measure itself as a handset.
    /// </summary>
    const float MinKnownDpi = 40f;
    const float MaxKnownDpi = 900f;

    /// <summary>Whether the platform gave a density worth measuring with at all.</summary>
    public static bool DensityKnown => IsKnownDensity(TouchMetrics.ScreenDpi);

    static bool IsKnownDensity(float dpi) => dpi >= MinKnownDpi && dpi <= MaxKnownDpi;

    /// <summary>
    /// The longer edge in millimetres, or 0 when the platform will not say.
    ///
    /// <b>Read through <see cref="TouchMetrics.ScreenDpi"/> rather than from
    /// <see cref="Screen.dpi"/> directly</b>, because in a browser the latter describes
    /// neither the glass nor the backbuffer. Taking it at face value measured a 152mm
    /// landscape handset at 242mm, which is a tablet -- so a phone got two columns of
    /// achievements, the desktop's card grid and none of the handset arrangements below.
    /// There is one density reading in the kit now and this is a consumer of it.
    /// </summary>
    public static float LongEdgeMm => LongEdgeMillimetres(TouchMetrics.ScreenDpi, ScreenInfo.Width, ScreenInfo.Height);

    static float LongEdgeMillimetres(float dpi, int width, int height)
        => IsKnownDensity(dpi) ? Mathf.Max(width, height) / (dpi / 25.4f) : 0f;

    /// <summary>Taller than it is wide. Only ever interesting on something held.</summary>
    public static bool Portrait => ScreenInfo.Height > ScreenInfo.Width;

    /// <summary>How the player is reaching this screen right now.</summary>
    public static Reach CurrentReach
        => ReachFor(TouchSignal, MobilePlatform);

    /// <summary>How much surface there is.</summary>
    public static Form CurrentForm
        => FormFor(TouchMetrics.ScreenDpi, ScreenInfo.Width, ScreenInfo.Height,
                   TouchSignal, MobilePlatform);

    static bool TouchSignal => ScreenInfo.SimulatedTouch ?? (MobileInput.Active || WebDevice.IsTouchOnly);
    static bool MobilePlatform => ScreenInfo.SimulatedMobile ?? Application.isMobilePlatform;

    /// <summary>Shorthand the gameplay layer wants far more often than the exact form.</summary>
    public static bool Touched => CurrentReach == Reach.Touch;

    /// <summary>
    /// Whether to lay out for thumbs at arm's length rather than for a pointer at a desk.
    ///
    /// This is the one question <c>PhoneUI</c> used to answer, kept because plenty of
    /// decisions really are binary -- but it is now a consequence of the axes above rather
    /// than the only thing anybody can ask.
    /// </summary>
    public static bool Handheld => CurrentForm == Form.Handset;

    // ==================================================================
    // The decisions themselves, with every reading handed in rather than read.
    //
    // Public only so FPSKitDeviceTest can drive them across the hardware that exists --
    // same reason ControlSettings.CanRead is. Batch mode has one screen and cannot
    // pretend to be a 416 dpi handset in portrait, so a check written against the
    // properties above would assert whatever the build machine is and pass identically
    // with every rule here deleted.
    // ==================================================================

    /// <param name="touchSignal">On-screen controls exist, or the browser says finger-only.</param>
    /// <param name="mobilePlatform">A handheld build, whatever its screen admits.</param>
    public static Reach ReachFor(bool touchSignal, bool mobilePlatform)
    {
        // A touch signal is evidence; the platform is only a claim. An iPad in a browser
        // calls itself a Macintosh, so the finger has to outrank the user agent.
        if (touchSignal) return Reach.Touch;
        return mobilePlatform ? Reach.Touch : Reach.Pointer;
    }

    /// <param name="dpi">What the platform claims, <c>0</c> included.</param>
    public static Form FormFor(float dpi, int width, int height, bool touchSignal, bool mobilePlatform)
    {
        float mm = LongEdgeMillimetres(dpi, width, height);

        if (mm <= 0f)
        {
            // No density to measure with. Ask what kind of device this is rather than
            // guessing from a pixel count -- guessing "small" from pixels is exactly what
            // handed every desktop the handset layout.
            return ReachFor(touchSignal, mobilePlatform) == Reach.Touch ? Form.Handset : Form.Desktop;
        }

        if (mm < HandsetMaxMm) return Form.Handset;
        if (mm < TabletMaxMm)  return Form.Tablet;
        if (mm < LaptopMaxMm)  return Form.Laptop;
        return Form.Desktop;
    }
}
