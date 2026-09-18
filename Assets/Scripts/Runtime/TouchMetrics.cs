using UnityEngine;

/// <summary>
/// Turns screen pixels into physical distance, so a control can be sized and scaled by
/// how big it actually is under a thumb rather than by how many pixels it happens to
/// cover.
///
/// This is the difference between a game that feels the same on every phone and one
/// that does not. A thumb is about 20mm wide on every human being alive; a pixel is
/// not a fixed size at all. The same 300-pixel swipe is 2.5cm on a 300dpi phone and
/// 1.3cm on a 600dpi one, so a look sensitivity tuned on one device is twice as fast
/// on the other -- and nothing about that reads as a units bug to the player. It reads
/// as the game being uncontrollable on their phone specifically.
///
/// <b>Screen.dpi is not trustworthy.</b> It is 0 on plenty of Android devices, it is
/// whatever the browser feels like on WebGL, and a handful of manufacturers report the
/// panel's marketing number rather than a measured one. So the value is clamped to a
/// range real handheld screens actually occupy and falls back to a typical modern phone
/// when the platform says something impossible. An estimate that is 20% out is an
/// invisible difference in feel; trusting a 0 is a division that makes the controls
/// either dead or uncontrollable.
///
/// Nothing here is cached. <see cref="Screen.dpi"/> is a cheap property read, and a
/// cached copy would be one more static to clear between play sessions -- and the one
/// nobody would think to, since it looks like a constant.
/// </summary>
public static class TouchMetrics
{
    /// <summary>What a phone reports when it does not know, or knows wrongly.</summary>
    const float FallbackDpi = 400f;

    /// <summary>
    /// Bounds of a believable handheld screen. A 7" tablet is near the bottom and a
    /// flagship phone near the top; anything outside is the platform guessing.
    /// </summary>
    const float MinDpi = 120f;
    const float MaxDpi = 800f;

    /// <summary>
    /// The density every sensitivity in the kit is written against. A swipe on a screen
    /// of exactly this density produces the raw pixel count the tuning expects, so the
    /// numbers in <see cref="TouchProfile"/> mean what they say on a typical phone and
    /// are corrected from there rather than being corrected everywhere.
    /// </summary>
    public const float ReferenceDpi = 400f;

    /// <summary>The screen's density, or a sane stand-in when it cannot be trusted.</summary>
    public static float Dpi
    {
        get
        {
            float dpi = Screen.dpi;
            return dpi >= MinDpi && dpi <= MaxDpi ? dpi : FallbackDpi;
        }
    }

    public static float PixelsPerMillimetre => Dpi / 25.4f;

    /// <summary>Pixels covering a given physical size on this screen.</summary>
    public static float MillimetresToPixels(float millimetres) => millimetres * PixelsPerMillimetre;

    /// <summary>
    /// A raw touch delta rescaled to the pixels the same physical movement would have
    /// covered on a <see cref="ReferenceDpi"/> screen.
    ///
    /// Everything downstream -- PlayerMotor's touchSensitivity, the profile's
    /// multiplier -- then works in one unit on every device, and none of it has to know
    /// what a screen is. Which is the point: the conversion belongs in the one place
    /// that reads the hardware, not spread across every consumer of a look delta.
    /// </summary>
    public static Vector2 ToReferencePixels(Vector2 rawPixels)
        => rawPixels * (ReferenceDpi / Dpi);

    /// <summary>
    /// A canvas-space size for something that must be at least <paramref name="millimetres"/>
    /// across on the real screen.
    ///
    /// Touch targets have a floor that is a property of hands rather than of screens:
    /// below roughly 9mm the miss rate climbs sharply, and a missed FIRE button in a
    /// firefight is indistinguishable from the game ignoring you. A canvas scaled to a
    /// reference resolution will happily draw a button that is correct in canvas units
    /// and 5mm wide in life.
    /// </summary>
    public static float MinimumTouchSize(float millimetres, float canvasScale)
    {
        if (canvasScale <= 0.0001f) return MillimetresToPixels(millimetres);

        return MillimetresToPixels(millimetres) / canvasScale;
    }
}
