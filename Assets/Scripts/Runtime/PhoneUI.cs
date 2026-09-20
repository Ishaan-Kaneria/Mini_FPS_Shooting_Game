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
    /// A 7" tablet in landscape is about 150mm and reads fine at desktop sizing; a phone
    /// is 147mm and does not, so the line sits just above the phones.
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

    /// <summary>The screen's width in millimetres, using the same density rules the touch layer trusts.</summary>
    public static float ScreenWidthMm => Screen.width / Mathf.Max(0.001f, TouchMetrics.PixelsPerMillimetre);

    /// <summary>
    /// Whether to lay this out for a thumb at arm's length.
    ///
    /// Asked of the touch layer first, because that is the one signal that is certain --
    /// if the on-screen controls are up, this is a handset whatever the numbers say. The
    /// physical size is the fallback for the menus, which run before any of that exists.
    /// </summary>
    public static bool Active
        => MobileInput.Active || WebDevice.IsTouchOnly || ScreenWidthMm < PhoneWidthMm;

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
