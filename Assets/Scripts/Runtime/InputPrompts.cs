using UnityEngine;

/// <summary>
/// What to show a player for "press this": a key name on a keyboard, a drawn glyph on a
/// gamepad in that pad's own family, a gesture on a touch screen.
///
/// Everything comes back as TMP rich text, so an existing sentence -- the instruction strip,
/// the bomb tutorial under the countdown, the pause hint -- changes device by swapping one
/// substring, and a prompt can sit in the middle of a line. Glyphs are sprites in
/// <c>Resources/Sprite Assets/Glyphs</c>, drawn by <c>Tools/generate-glyphs.py</c> in the
/// icon set's line style, and tinted by the text they sit in.
///
/// Anything that shows a prompt must rewrite it on <see cref="GameInput.SchemeChanged"/>,
/// or picking up a pad leaves the screen naming keyboard keys -- the exact failure this
/// exists to prevent.
/// </summary>
public static class InputPrompts
{
    /// <summary>The TMP sprite asset the glyphs live in, by its Resources name.</summary>
    public const string SpriteAsset = "Glyphs";

    /// <summary>A glyph as inline rich text, tinted by the surrounding text.</summary>
    ///
    /// Drawn a third larger than the line's capitals: a glyph is a small picture with a
    /// letter inside it, and at cap height the letter in an Xbox A is a speck.
    public static string Glyph(string id) => $"<size=135%><sprite=\"{SpriteAsset}\" name=\"{id}\" tint=1></size>";

    /// <summary>The prompt for an action, for whatever the player is holding now.</summary>
    public static string For(GameAction action, ControlSettings keys = null)
        => For(action, GameInput.Scheme, GameInput.Pad, keys);

    public static string For(GameAction action, InputScheme scheme, PadFamily pad, ControlSettings keys = null)
    {
        switch (scheme)
        {
            case InputScheme.Gamepad:
                return Glyph(PadGlyph(action, pad));
            case InputScheme.Touch:
                return Glyph(TouchIcon(action));
            default:
                return KeyText(KeyFor(action, keys ?? DefaultKeys));
        }
    }

    /// <summary>A keyboard or mouse key: a glyph for a mouse button, the key's name otherwise.</summary>
    public static string KeyText(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.Mouse0: return Glyph("mouse_left");
            case KeyCode.Mouse1: return Glyph("mouse_right");
            case KeyCode.Mouse2: return Glyph("mouse_middle");
            default: return UIText.KeyLabel(key);
        }
    }

    /// <summary>"Back": Esc on a keyboard, B or Circle (or east) on a pad.</summary>
    public static string Back => GameInput.Scheme == InputScheme.Gamepad
        ? Glyph(Face(GameInput.Pad, "east"))
        : UIText.KeyLabel(KeyCode.Escape);

    /// <summary>"Confirm": Enter on a keyboard, A or Cross (or south) on a pad.</summary>
    public static string Confirm => GameInput.Scheme == InputScheme.Gamepad
        ? Glyph(Face(GameInput.Pad, "south"))
        : UIText.KeyLabel(KeyCode.Return);

    /// <summary>
    /// The icon on the on-screen button for an action, so "tap" on a phone points at the
    /// button the player can see. <c>FPSKitMobileControls</c> draws the buttons with these.
    /// </summary>
    public static string TouchIcon(GameAction action) => action switch
    {
        GameAction.Fire => "bullet",
        GameAction.Aim => "crosshair",
        GameAction.Jump => "arrow-up",
        GameAction.Crouch => "arrow-bar-to-down",
        GameAction.Reload => "refresh",
        GameAction.Bomb => "grenade",
        GameAction.UseItem => "medkit",
        GameAction.Sprint => "run",
        GameAction.Pause => "player-pause",
        GameAction.Move => "stick_l",
        _ => "touch_drag",
    };

    /// <summary>The glyph id for an action's pad binding. Matches <c>GameInput.AddPadBindings</c>.</summary>
    public static string PadGlyph(GameAction action, PadFamily pad) => action switch
    {
        GameAction.Move => "stick_l",
        GameAction.Look => "stick_r",
        GameAction.Fire => Trigger(pad, right: true),
        GameAction.Aim => Trigger(pad, right: false),
        GameAction.Jump => Face(pad, "south"),
        GameAction.Crouch => Face(pad, "east"),
        GameAction.Reload => Face(pad, "west"),
        GameAction.Sprint => pad == PadFamily.PlayStation ? "ps_l3" : "xbox_ls",
        GameAction.Bomb => Bumper(pad, right: true),
        GameAction.UseItem => Bumper(pad, right: false),
        GameAction.Pause => pad switch { PadFamily.PlayStation => "ps_options", PadFamily.Generic => "pad_start", _ => "xbox_menu" },
        _ => "pad_south",
    };

    static string Face(PadFamily pad, string side)
    {
        switch (pad)
        {
            case PadFamily.PlayStation:
                return side switch { "south" => "ps_cross", "east" => "ps_circle", "west" => "ps_square", _ => "ps_triangle" };
            case PadFamily.Generic:
                return "pad_" + side;
            default:
                return side switch { "south" => "xbox_a", "east" => "xbox_b", "west" => "xbox_x", _ => "xbox_y" };
        }
    }

    static string Trigger(PadFamily pad, bool right) => pad switch
    {
        PadFamily.PlayStation => right ? "ps_r2" : "ps_l2",
        PadFamily.Generic => right ? "pad_r2" : "pad_l2",
        _ => right ? "xbox_rt" : "xbox_lt",
    };

    static string Bumper(PadFamily pad, bool right) => pad switch
    {
        PadFamily.PlayStation => right ? "ps_r1" : "ps_l1",
        PadFamily.Generic => right ? "pad_r1" : "pad_l1",
        _ => right ? "xbox_rb" : "xbox_lb",
    };

    static ControlSettings _defaults;

    /// <summary>The shipped bindings, for a screen with no player in it to ask.</summary>
    static ControlSettings DefaultKeys
    {
        get
        {
            if (_defaults == null) _defaults = ControlSettings.CreateDefault();
            return _defaults;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => _defaults = null;

    static KeyCode KeyFor(GameAction action, ControlSettings c) => action switch
    {
        GameAction.Fire => c.fire,
        GameAction.Aim => c.aim,
        GameAction.Jump => c.jump,
        GameAction.Crouch => c.crouch,
        GameAction.Sprint => c.sprintKey,
        GameAction.Reload => c.reload,
        GameAction.Bomb => c.bomb,
        GameAction.UseItem => c.useItem,
        GameAction.Pause => KeyCode.Escape,
        GameAction.Move => c.altForward != KeyCode.None ? c.altForward : c.moveForward,
        _ => KeyCode.None,
    };
}
