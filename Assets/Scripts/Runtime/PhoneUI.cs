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
    /// <b>This is the approach being replaced.</b> Growing everything by one factor keeps
    /// the desktop's arrangement and density and merely magnifies them, which is why it had
    /// to be paired with <c>HideOnPhone</c> deleting whatever then overflowed. A handset
    /// needs its own arrangement, not this one enlarged. Kept until the per-form layouts
    /// land, because on a real phone it is still better than nothing.
    public const float ReferenceShrink = 0.70f;

    /// <summary>The screen's width in millimetres, or 0 when the platform will not say.</summary>
    public static float ScreenWidthMm => DeviceProfile.LongEdgeMm;

    /// <summary>
    /// Whether to lay this out for a thumb at arm's length.
    ///
    /// <b>Superseded.</b> <see cref="DeviceProfile"/> is where this is decided now, and it
    /// answers with a form, a reach and an orientation rather than one bool -- because the
    /// bool is what forced every non-desktop screen through the same magnified desktop.
    /// This remains as the one-line question, for the decisions that genuinely are binary
    /// and for the callers that already ask it.
    /// </summary>
    public static bool Active => DeviceProfile.Handheld;

    /// <summary>
    /// The decision itself, with the readings handed in. Delegates to
    /// <see cref="DeviceProfile.FormFor"/> so there is one classifier rather than two that
    /// can drift apart.
    /// </summary>
    public static bool IsHandset(float dpi, int widthPixels, bool touchSignal, bool mobilePlatform)
        => DeviceProfile.FormFor(dpi, widthPixels, 0, touchSignal, mobilePlatform)
           == DeviceProfile.Form.Handset;

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
