using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Every colour, typeface, icon, size and duration the interface is built from.
///
/// <b>The identity is flat, matte and tactical.</b> Solid panels, one-pixel borders, square
/// corners; no gradients, glows, bevels or drop shadows -- the one exception is a thin dark
/// outline on HUD text, because HUD text sits over whatever the arena is showing. It is the
/// interface the low-poly, flat-shaded arenas imply: the same economy of means, applied to
/// the screens around them.
///
/// <b>One accent.</b> Amber, the logo stripe, means "this is selected, this is the thing to
/// press, this is what the level wants, this is money, this is a star". Danger, success
/// and info are <i>functional</i>: they say what state something is in and never decorate.
/// An arena's own colour appears only as a three-pixel stripe on its card, because a colour
/// that identifies has to be rare to keep identifying -- six arenas filled with six bright
/// colours is what the previous theme did, and it made every screen shout at once.
///
/// <b>It is an asset</b> (<c>Assets/UI/Resources/UITheme.asset</c>) so the palette can be
/// retuned in the Inspector without a recompile. The field initialisers below <i>are</i> the
/// design spec: FPSKit &gt; UI Kit creates the asset from them when it is missing and never
/// overwrites it, and the Inspector's Reset puts them back. That is deliberately not the
/// generator-plus-reset arrangement the roster and the ladders use, whose values live in a
/// Configure method and silently stop reaching the asset -- here there is only one copy of
/// each number, the one on the asset.
///
/// <b>The static colours further down are the previous theme</b>, kept while the existing
/// screens are moved onto this one a screen at a time. Nothing new should read them.
/// </summary>
[CreateAssetMenu(menuName = "FPSKit/UI Theme", fileName = "UITheme")]
public class UITheme : ScriptableObject
{
    // ==================================================================
    // The active theme.
    // ==================================================================

    /// <summary>Where the one theme lives, relative to a Resources folder.</summary>
    public const string ResourcePath = "UITheme";

    static UITheme _active;

    /// <summary>
    /// The theme every component reads when it was not handed one. Loaded from Resources
    /// on first use, so a component added by hand to any canvas is styled without being
    /// wired to anything. Falls back to an in-memory instance built from the field
    /// defaults, so a missing asset produces the right colours and a warning rather than
    /// a screen of null references.
    /// </summary>
    public static UITheme Active
    {
        get
        {
            if (_active != null) return _active;
            _active = Resources.Load<UITheme>(ResourcePath);
            if (_active == null)
            {
                Debug.LogWarning("UITheme: no asset at Resources/" + ResourcePath +
                                 ". Run FPSKit > UI Kit > Import Fonts And Icons. Using defaults, with no fonts or icons.");
                _active = CreateInstance<UITheme>();
                _active.hideFlags = HideFlags.DontSave;
            }
            return _active;
        }
    }

    /// <summary>Domain reload is off, so the cached theme would otherwise outlive the session.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _active = null;

    // ==================================================================
    // Surfaces.
    // ==================================================================

    [Header("Surfaces")]
    [Tooltip("Behind everything: the dashboard, the overlays' shade.")]
    public Color background = Hex(0x14171C);

    [Tooltip("A panel resting on the background.")]
    public Color panel = Hex(0x1C2128);

    [Tooltip("A panel on a panel, and the resting face of a secondary button.")]
    public Color panelRaised = Hex(0x242A33);

    [Tooltip("A secondary button under the pointer. One step up from the raised panel.")]
    public Color panelHover = Hex(0x2C333E);

    [Tooltip("The one-pixel line round a panel. A seam, not a decoration.")]
    public Color border = Hex(0x2E3540);

    [Tooltip("A border under the pointer, on anything that is not the accent.")]
    public Color borderHover = Hex(0x46505E);

    [Tooltip("How opaque a HUD panel is. The arena has to show through a little, or the " +
             "HUD reads as a frame round a smaller game.")]
    [Range(0f, 1f)] public float hudPanelAlpha = 0.85f;

    // ==================================================================
    // Text.
    // ==================================================================

    [Header("Text")]
    [Tooltip("Primary text. Not pure white, which vibrates against a dark field.")]
    public Color textPrimary = Hex(0xE6E8EB);

    [Tooltip("Descriptions, units, labels -- anything read second.")]
    public Color textSecondary = Hex(0x8A93A0);

    [Tooltip("Anything that cannot be used yet.")]
    public Color textDisabled = Hex(0x4A525E);

    [Tooltip("The thin dark outline on HUD text, which sits over snow, sand and sky alike. " +
             "The only shadow-like thing in the interface.")]
    public Color hudTextOutline = new Color(0.03f, 0.035f, 0.045f, 0.9f);

    // ==================================================================
    // Accent and function.
    // ==================================================================

    [Header("Accent")]
    [Tooltip("The one brand colour: selection, primary buttons, objectives, coins, stars.")]
    public Color accent = Hex(0xE8A33D);

    [Tooltip("A primary button under the pointer.")]
    public Color accentHover = Hex(0xF0B253);

    [Tooltip("A primary button held down.")]
    public Color accentPressed = Hex(0xC98A2C);

    [Tooltip("Text and icons on an amber face. The background colour, so a primary button " +
             "reads as a hole cut in the accent rather than as a second palette.")]
    public Color textOnAccent = Hex(0x14171C);

    [Header("Function")]
    [Tooltip("Enemies, damage, not enough coins, the last seconds of the clock.")]
    public Color danger = Hex(0xD9534F);

    [Tooltip("Earned, cleared, healed.")]
    public Color success = Hex(0x5FB36A);

    [Tooltip("Armour, shields, and neutral information.")]
    public Color info = Hex(0x6C8EAD);

    // ==================================================================
    // Arena stripes.
    // ==================================================================

    [Serializable]
    public struct ArenaStripe
    {
        [Tooltip("The arena's scene name, e.g. SnowboundStation.")]
        public string scene;
        public Color color;
    }

    [Header("Arena stripes")]
    [Tooltip("An arena's own colour, used only as a thin stripe on its card -- never as a " +
             "fill. Keyed by scene name so reordering the campaign does not recolour anything.")]
    public ArenaStripe[] arenaStripes =
    {
        new ArenaStripe { scene = "IndustrialWarehouse", color = Hex(0xA89F5B) },
        new ArenaStripe { scene = "SnowboundStation",    color = Hex(0x8CBCD9) },
        new ArenaStripe { scene = "DesertOutpost",       color = Hex(0xCDB07A) },
        new ArenaStripe { scene = "NightRooftop",        color = Hex(0x8E7CC3) },
        new ArenaStripe { scene = "AbandonedSubway",     color = Hex(0x4FA39A) },
        new ArenaStripe { scene = "MarsColony",          color = Hex(0xC4553B) },
        new ArenaStripe { scene = "TheAugerHouse",       color = Hex(0xC9C3B6) },
    };

    [Tooltip("How thick an arena stripe is, in screen pixels.")]
    [Min(1f)] public float stripePixels = 3f;

    /// <summary>The stripe for an arena, or the border colour for one this theme does not list.</summary>
    public Color StripeFor(string scene)
    {
        if (arenaStripes != null)
            foreach (var s in arenaStripes)
                if (string.Equals(s.scene, scene, StringComparison.OrdinalIgnoreCase))
                    return s.color;
        return border;
    }

    // ==================================================================
    // Lines.
    // ==================================================================

    [Header("Lines")]
    [Tooltip("Border thickness in real screen pixels, not canvas units -- a one-unit border " +
             "on a canvas scaled to 0.67 is a smeared two-thirds of a pixel.")]
    [Min(0f)] public float borderPixels = 1f;

    [Tooltip("The amber line under a selected tab, in screen pixels.")]
    [Min(1f)] public float selectionPixels = 2f;

    // ==================================================================
    // Type.
    // ==================================================================

    [Header("Type")]
    [Tooltip("Headings and every number: Barlow Condensed SemiBold.")]
    public TMP_FontAsset headingFont;

    [Tooltip("Body copy: Barlow Regular.")]
    public TMP_FontAsset bodyFont;

    [Tooltip("Button captions and emphasised body: Barlow Medium.")]
    public TMP_FontAsset bodyStrongFont;

    [Tooltip("The heading font with the thin dark outline, for text drawn straight over the arena.")]
    public Material hudHeadingMaterial;

    [Tooltip("The body font with the thin dark outline.")]
    public Material hudBodyMaterial;

    [Tooltip("Letter spacing on uppercase labels, in TMP's units (hundredths of an em).")]
    public float labelSpacing = 8f;

    [Tooltip("The width every digit is set to, in ems of the heading font, so a counter does " +
             "not shimmy as it ticks. Barlow's figures are proportional -- a 1 is barely more " +
             "than half the width of a 0 -- and TMP has no tabular-figures switch, so this is " +
             "applied with <mspace>. Set to the widest digit.")]
    public float headingTabularEm = 0.46f;

    [Tooltip("The same, for the body font.")]
    public float bodyTabularEm = 0.57f;

    [Tooltip("Sizes in canvas units against the 1920x1080 reference. The small ones are set " +
             "by 1280x720, where the canvas is two-thirds scale: a 16-unit label is 10.7 real " +
             "pixels there, and much below that uppercase condensed type stops being read and " +
             "starts being deciphered.")]
    public float sizeDisplay = 48f, sizeTitle = 32f, sizeHeading = 22f, sizeBody = 19f,
                 sizeLabel = 16f, sizeCaption = 15f;

    // ==================================================================
    // Icons.
    // ==================================================================

    [Serializable]
    public struct Icon
    {
        public string id;
        public Sprite sprite;
    }

    [Header("Icons")]
    [Tooltip("One set of flat line icons at a 2px stroke: Tabler Icons plus the game's own " +
             "drawn to match (Tools/icons). Filled in by FPSKit > UI Kit from Assets/UI/Icons.")]
    public Icon[] icons = Array.Empty<Icon>();

    [Tooltip("An icon's resting size in canvas units.")]
    public float iconSize = 24f;

    Dictionary<string, Sprite> _iconIndex;

    /// <summary>
    /// The icon with this id (the file name without its extension), or null. A missing
    /// icon warns once rather than throwing, because a screen with one blank icon is still
    /// a screen that works.
    /// </summary>
    public Sprite IconSprite(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (_iconIndex == null || _iconIndex.Count != (icons?.Length ?? 0))
        {
            _iconIndex = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            if (icons != null)
                foreach (var i in icons)
                    if (!string.IsNullOrEmpty(i.id)) _iconIndex[i.id] = i.sprite;
        }
        if (_iconIndex.TryGetValue(id, out var sprite)) return sprite;
        Debug.LogWarning($"UITheme: no icon '{id}'.");
        _iconIndex[id] = null;
        return null;
    }

    // ==================================================================
    // Motion.
    // ==================================================================

    [Header("Motion")]
    [Tooltip("A state change on a control: hover, press, a tab switching.")]
    [Range(0.05f, 0.3f)] public float motionFast = 0.12f;

    [Tooltip("Something arriving: a tooltip, a bar filling, a toast sliding in.")]
    [Range(0.05f, 0.3f)] public float motionStandard = 0.16f;

    [Tooltip("A panel opening or closing.")]
    [Range(0.05f, 0.4f)] public float motionSlow = 0.2f;

    [Tooltip("How far something slides as it arrives, in canvas units. Short: it says where " +
             "it came from without making a show of it.")]
    public float slideDistance = 16f;

    [Tooltip("How long the pointer rests on an icon button before its tooltip appears.")]
    [Range(0f, 1.5f)] public float tooltipDelay = 0.4f;

    [Tooltip("How long a toast stays up before it leaves.")]
    [Min(0.5f)] public float toastSeconds = 3.5f;

    [Tooltip("The period of the low-timer warning, the only pulsing thing in the interface.")]
    [Min(0.2f)] public float warningPulseSeconds = 1f;

    // ==================================================================
    // Helpers.
    // ==================================================================

    /// <summary>A colour from a 0xRRGGBB literal, so the spec's hex values can be read off directly.</summary>
    public static Color Hex(uint rgb, float alpha = 1f)
        => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    /// <summary>A panel colour for the HUD: the same panel, at the HUD's opacity.</summary>
    public Color HudPanel => new Color(panel.r, panel.g, panel.b, hudPanelAlpha);

    /// <summary>
    /// Wraps every run of digits in a monospace tag so counters hold still as they change.
    /// Only the digits: a comma or a slash keeps its own width, which is what tabular
    /// figures do in a real font too.
    /// </summary>
    public string Tabular(string text, bool heading = true)
    {
        if (string.IsNullOrEmpty(text)) return text;
        float em = heading ? headingTabularEm : bodyTabularEm;
        return System.Text.RegularExpressions.Regex.Replace(
            text, "[0-9]+", m => "<mspace=" + em.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "em>" + m.Value + "</mspace>");
    }

    // ==================================================================
    // THE PREVIOUS THEME. Static, and read by the screens that have not moved yet.
    // ==================================================================

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
