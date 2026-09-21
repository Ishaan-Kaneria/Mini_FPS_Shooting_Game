using UnityEngine;

/// <summary>
/// Every colour, size and duration the interface is built from.
///
/// <b>What this replaces was one accent.</b> The dashboard drew all six arenas, the store,
/// the level tiles and the results screen in a single amber over a dark field, so nothing
/// on any screen distinguished itself from anything else -- which is what "dull" actually
/// describes. It is also the commonest look a dark game menu can have, and being
/// indistinguishable from every other dark game menu is a property no amount of retuning
/// that one amber fixes.
///
/// The identity here is <b>industrial signage</b>, and the important thing about signage is
/// that it is bright on purpose. High-visibility colour is not decoration on a plant: it is
/// how a person crossing one knows what they are looking at before they are close enough to
/// read it. So the brightness is grounded rather than applied, and -- the part that matters
/// -- <b>it carries information</b>. Each arena owns a signal colour, and wears it on its
/// card, its level tiles and its results screen, so a player learns "the cyan one" before
/// they have learned "Snowbound Station".
///
/// <b>Colours are constants, not an asset.</b> A ScriptableObject would be the obvious
/// choice and is the wrong one here: the menu is constructed in C# by FPSKitMenuBuilder and
/// the builder regenerates the scene from scratch, so a theme asset would be one more
/// generated file to keep in step with the code that reads it -- and the kit already has
/// four generators whose assets silently fall out of date. Anything a designer would want
/// to retune lives here, in the file the builder already compiles against.
/// </summary>
public static class UITheme
{
    // ==================================================================
    // Chassis. The dark frame everything is mounted on.
    //
    // Blue-steel rather than a tinted near-black: #0B0B0B and #111 standing in for black
    // is a tell, and a neutral dark field makes a bright accent look like it is floating
    // rather than lit. A chassis with a hue in it gives every signal colour something to
    // sit against.
    // ==================================================================

    /// <summary>
    /// The deepest step, behind everything.
    ///
    /// Still a colour rather than a near-black. What made the old dashboard look flat was
    /// not that it was dark but that its base was #04060A -- black in all but name -- so
    /// every bright thing on it appeared to float rather than to be lit by anything. A
    /// base with hue in it gives the six signals something to sit against.
    /// </summary>
    public static readonly Color Well = new Color32(0x12, 0x16, 0x1F, 0xFF);

    /// <summary>A panel resting on the well: the middle step of three.</summary>
    public static readonly Color Chassis = new Color32(0x1A, 0x1F, 0x2B, 0xFF);

    /// <summary>A raised panel: headers, rails, anything bolted to the chassis.</summary>
    public static readonly Color Plate = new Color32(0x27, 0x2E, 0x3E, 0xFF);

    /// <summary>The line where two plates meet. A seam, not a decorative border.</summary>
    public static readonly Color Seam = new Color32(0x3A, 0x44, 0x5A, 0xFF);

    // ==================================================================
    // Ink. Text on the chassis.
    // ==================================================================

    /// <summary>Primary text. Not pure white -- that vibrates against a dark field.</summary>
    public static readonly Color Ink = new Color32(0xF2, 0xF5, 0xFA, 0xFF);

    /// <summary>Secondary text: descriptions, units, anything read second.</summary>
    public static readonly Color InkDim = new Color32(0x9F, 0xAC, 0xC0, 0xFF);

    /// <summary>Text on something that cannot be used yet.</summary>
    public static readonly Color InkLocked = new Color32(0x5E, 0x6A, 0x7D, 0xFF);

    // ==================================================================
    // Signal. The bright half, and the half that means something.
    //
    // Six colours for six arenas, held apart in hue so that two cards side by side are
    // never confusable, and matched in luminance so that no one arena looks more important
    // than another. Named for what an industrial site would call them rather than for the
    // hue, because the name is the point: these are signals.
    // ==================================================================

    public static readonly Color Hazard   = new Color32(0xFF, 0xC5, 0x1F, 0xFF); // yellow
    public static readonly Color Ignition = new Color32(0xFF, 0x74, 0x2C, 0xFF); // orange
    public static readonly Color Coolant  = new Color32(0x2C, 0xC8, 0xF0, 0xFF); // cyan
    public static readonly Color Biotic   = new Color32(0x4C, 0xE0, 0x72, 0xFF); // green
    public static readonly Color Alert    = new Color32(0xFF, 0x45, 0x63, 0xFF); // red
    public static readonly Color Plasma   = new Color32(0xB0, 0x76, 0xFF, 0xFF); // violet

    /// <summary>
    /// The six, in the order the arenas are listed.
    ///
    /// Index rather than name so a new arena added to the catalogue gets a colour without
    /// anybody editing this, and wraps rather than throwing if the catalogue outgrows six.
    /// </summary>
    static readonly Color[] Signals = { Hazard, Ignition, Coolant, Biotic, Alert, Plasma };

    /// <summary>The colour this arena wears everywhere it appears.</summary>
    public static Color SignalFor(int arenaIndex)
        => Signals[Mathf.Abs(arenaIndex) % Signals.Length];

    /// <summary>How many distinct signals exist before they repeat.</summary>
    public static int SignalCount => Signals.Length;

    // ==================================================================
    // State. What a thing's condition looks like, independent of its arena.
    // ==================================================================

    /// <summary>Earned, cleared, affordable.</summary>
    public static readonly Color Good = new Color32(0x4C, 0xE0, 0x72, 0xFF);

    /// <summary>Not yet reachable. Deliberately low contrast -- a locked thing that
    /// lights up reads as broken rather than as locked.</summary>
    public static readonly Color Locked = new Color32(0x47, 0x51, 0x63, 0xFF);

    /// <summary>Cannot be afforded, has run out, is about to expire.</summary>
    public static readonly Color Warning = new Color32(0xFF, 0x45, 0x63, 0xFF);

    /// <summary>A star that has been earned. Unearned stars use <see cref="Locked"/>.</summary>
    public static readonly Color Star = new Color32(0xFF, 0xC5, 0x1F, 0xFF);

    // ==================================================================
    // Motion.
    //
    // One vocabulary of durations rather than a number per animation. Scattered
    // hover-and-fade on everything is the generated-interface default and reads as noise;
    // motion here answers something the player did, and says what changed.
    // ==================================================================

    /// <summary>A press acknowledging itself. Below this and it is not perceived.</summary>
    public const float TapSeconds = 0.08f;

    /// <summary>A hover or selection settling. Long enough to see, short enough not to lag.</summary>
    public const float SettleSeconds = 0.16f;

    /// <summary>A panel opening or closing.</summary>
    public const float PanelSeconds = 0.24f;

    /// <summary>A number counting up to its new value on the results screen.</summary>
    public const float CountSeconds = 0.9f;

    /// <summary>How much a card grows under a pointer. Scale, never anchoredPosition --
    /// a GridLayoutGroup owns that property and writing it stacks every card on one spot.</summary>
    public const float HoverGrow = 1.04f;

    /// <summary>How far a card sinks when pressed.</summary>
    public const float PressShrink = 0.97f;

    /// <summary>
    /// Ease for anything arriving: fast out of the gate, settling at the end.
    /// A single curve shared by everything is most of what makes an interface feel like
    /// one object rather than a collection of separately animated parts.
    /// </summary>
    public static float EaseOut(float t) => 1f - (1f - Mathf.Clamp01(t)) * (1f - Mathf.Clamp01(t));

    /// <summary>Ease for something leaving or being pressed.</summary>
    public static float EaseIn(float t) => Mathf.Clamp01(t) * Mathf.Clamp01(t);
}
