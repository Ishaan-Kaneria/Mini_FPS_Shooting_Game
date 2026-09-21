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
/// Lowering the canvas's reference resolution makes every reference unit cover more of
/// the screen, so the type, the padding, the cards and the spacing all grow together and
/// the layout keeps its proportions. Scaling the fonts alone would grow the words inside
/// boxes that did not grow with them, which is how text starts overflowing its card.
///
/// <b>It is a floor under the type, not the answer.</b> On its own it is the desktop
/// magnified, which is what shipped and what Ishaan rejected in one line: a minimised
/// desktop dashboard is not a phone dashboard. What each screen does with its
/// <see cref="DeviceProfile.CurrentForm"/> -- how many columns, what is dropped, what
/// takes the width that frees -- is the layout. This just makes sure the words in it can
/// be read.
///
/// Nothing here is cached. Every member is a fresh read of <see cref="Screen"/> or
/// <see cref="MobileInput"/>, for the reason the rest of the kit's static helpers are
/// written that way: domain reload is off, and a cached "this is a phone" would survive
/// into the next play session in the editor and be wrong.
/// </summary>
public static class PhoneUI
{
    /// <summary>
    /// Which screen is being scaled. They want different answers and used to get the
    /// same one.
    /// </summary>
    public enum Surface
    {
        /// <summary>A menu: read, at rest, with time to spare. Type size is everything.</summary>
        Menu,

        /// <summary>The HUD: glanced at mid-fight, and already sharing the screen with
        /// controls that size themselves in millimetres and must not be pushed about.</summary>
        Hud,
    }

    /// <summary>
    /// How much of the authored reference to keep, per form and per surface.
    ///
    /// <b>This is a legibility floor, not the layout.</b> The arrangement is chosen by
    /// the screens themselves from <see cref="DeviceProfile.CurrentForm"/> -- fewer
    /// columns, no career panel, the grid taking the width that frees. What a reference
    /// shrink does on top of that is make a reference unit cover more glass, which is the
    /// only way body type on a 155mm screen reaches a size a person can read at arm's
    /// length. Used on its own, as it was, it is a magnified desktop and the reason
    /// <c>HideOnPhone</c> had to exist at all.
    ///
    /// The HUD is deliberately scaled less than the menus. It is glanced at rather than
    /// read, its largest elements are the minimap and the ammo count which are already
    /// big enough, and everything else on that canvas -- the whole touch layer -- is
    /// sized in millimetres and does not move when the reference does. Growing it as far
    /// as the menus would spend the middle of a phone screen on furniture during a fight.
    /// </summary>
    static float ReferenceScaleFor(DeviceProfile.Form form, Surface surface) => form switch
    {
        DeviceProfile.Form.Handset => surface == Surface.Hud ? 0.80f : 0.56f,
        DeviceProfile.Form.Tablet => surface == Surface.Hud ? 0.92f : 0.78f,
        _ => 1f,
    };

    /// <summary>
    /// The old single factor, kept because it names the thing this file used to be.
    /// 0.70 for every screen on every handheld, paired with deleting whatever no longer
    /// fitted.
    /// </summary>
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
    /// Idempotent per call site rather than per canvas: it reads the scaler's authored
    /// <see cref="CanvasScaler.referenceResolution"/> and multiplies, so calling it twice
    /// on one canvas would compound. Each canvas has exactly one caller, at Start.
    /// </summary>
    public static void Apply(Canvas canvas) => Apply(canvas, Surface.Menu);

    public static void Apply(Canvas canvas, Surface surface)
    {
        if (canvas == null) return;

        float scale = ReferenceScaleFor(DeviceProfile.CurrentForm, surface);
        if (Mathf.Approximately(scale, 1f)) return;

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return;

        scaler.referenceResolution = scaler.referenceResolution * scale;

        // Match the shorter edge rather than splitting the difference. A landscape phone
        // is about 2.2:1 against the authored 16:9, so a 0.5 match lets the extra width
        // pull every unit back down and undoes most of the shrink -- on the one screen
        // that needed it most.
        if (DeviceProfile.CurrentForm == DeviceProfile.Form.Handset)
            scaler.matchWidthOrHeight = 1f;
    }
}
