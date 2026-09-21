using System.Collections.Generic;

/// <summary>
/// The handful of formatting rules every screen in the game shares, in one place so
/// they cannot drift apart.
///
/// They had drifted. Six different labels built the same kind of list -- a short row of
/// values with a dot between them -- and between them used two spacings, two casings and
/// three different ways of writing a count, so an arena card, a level tile and a store
/// card read as three games. None of that is a bug and all of it is noticeable.
///
/// The rules are: values are joined by <see cref="Separator"/>; a value is a number and
/// then a label in capitals; a unit is lowercase and attached to its number (<c>38s</c>,
/// <c>7m</c>) because it is part of the number rather than a word of its own.
///
/// Same reasoning as <see cref="Wallet.Format"/>, which exists so that every screen
/// spells a coin balance the same way.
/// </summary>
public static class UIText
{
    /// <summary>
    /// What goes between the values of a row. Wide on purpose: at the sizes these are
    /// drawn, a tighter dot reads as a decimal point.
    /// </summary>
    public const string Separator = "   ·   ";

    /// <summary>
    /// Joins a row of values, skipping any that are empty. Skipping is what lets a
    /// caller pass a value it may not have -- a drink with no shield component, a bomb
    /// level with no boss -- without having to build the string conditionally and get
    /// the separators wrong at the seams.
    /// </summary>
    public static string Row(params string[] values)
    {
        if (values == null || values.Length == 0) return "";

        var kept = new List<string>(values.Length);

        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value)) kept.Add(value.Trim());

        return string.Join(Separator, kept);
    }

    /// <summary>
    /// A count of things carried, written the one way: <c>x3</c>. Distinct from a
    /// quantity with a unit, which is why it is not "3 BOMBS" -- the belt counter on the
    /// HUD has no room for the noun and the two would then disagree.
    /// </summary>
    public static string Count(int amount) => $"x{amount}";

    /// <summary>Seconds, as a player reads them: <c>38s</c>.</summary>
    public static string Seconds(float seconds) => $"{seconds:0}s";

    /// <summary>Metres: <c>7m</c>, or <c>7.5m</c> where the half matters.</summary>
    public static string Metres(float metres) => $"{metres:0.#}m";

    /// <summary>
    /// What a key is called on screen.
    ///
    /// <c>KeyCode.ToString</c> gives "Alpha1", "LeftShift" and "Mouse0" -- none of which
    /// anybody calls them. Here rather than on any one screen because the instruction
    /// strip, the pause hint and the instructions panel must not disagree about what the
    /// fire button is named: a player who reads "LMB" in one place and "Mouse0" in another
    /// has to work out that they are the same thing.
    /// </summary>
    public static string KeyLabel(UnityEngine.KeyCode key) => key switch
    {
        UnityEngine.KeyCode.Escape => "ESC",
        UnityEngine.KeyCode.Return => "ENTER",
        UnityEngine.KeyCode.Space => "SPACE",
        UnityEngine.KeyCode.Mouse0 => "LMB",
        UnityEngine.KeyCode.Mouse1 => "RMB",
        UnityEngine.KeyCode.Mouse2 => "MMB",
        UnityEngine.KeyCode.LeftShift => "SHIFT",
        UnityEngine.KeyCode.RightShift => "SHIFT",
        UnityEngine.KeyCode.LeftControl => "CTRL",
        UnityEngine.KeyCode.RightControl => "CTRL",
        UnityEngine.KeyCode.LeftArrow => "LEFT",
        UnityEngine.KeyCode.RightArrow => "RIGHT",
        UnityEngine.KeyCode.UpArrow => "UP",
        UnityEngine.KeyCode.DownArrow => "DOWN",
        _ => key.ToString().ToUpperInvariant(),
    };
}
