using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The player's own keys, on top of <see cref="ControlSettings"/>.
///
/// <b>Overrides live in PlayerPrefs, not in the asset.</b> Controls.asset is shared and
/// generated, and a ScriptableObject changed at runtime in the editor stays changed on disk
/// the next time anything saves it. So the player's keys are written onto the live instance
/// every time one is used (<see cref="Apply"/>), the authored keys are remembered, and they
/// go back when play stops. <see cref="ControlSettings.Revision"/> is bumped so
/// <see cref="GameInput.Bind"/> rewrites the Input System actions from the new keys.
///
/// Rebinding a key that another action already uses swaps them, so no two actions ever end
/// up on one key -- the invariant VerifyControls checks for every preset.
/// </summary>
public static class KeyBindings
{
    public readonly struct Binding
    {
        public readonly string Id, Label;
        public readonly Func<ControlSettings, KeyCode> Get;
        public readonly Action<ControlSettings, KeyCode> Set;
        public Binding(string id, string label, Func<ControlSettings, KeyCode> get, Action<ControlSettings, KeyCode> set)
        { Id = id; Label = label; Get = get; Set = set; }
    }

    /// <summary>What can be rebound, in the order Settings lists it. Movement is the W/A/S/D set; the arrows stay.</summary>
    public static Binding[] All() => new[]
    {
        new Binding("forward", "Move forward", c => c.altForward, (c, k) => c.altForward = k),
        new Binding("back", "Move back", c => c.altBack, (c, k) => c.altBack = k),
        new Binding("left", "Move left", c => c.altLeft, (c, k) => c.altLeft = k),
        new Binding("right", "Move right", c => c.altRight, (c, k) => c.altRight = k),
        new Binding("fire", "Fire", c => c.fire, (c, k) => c.fire = k),
        new Binding("aim", "Aim", c => c.aim, (c, k) => c.aim = k),
        new Binding("reload", "Reload", c => c.reload, (c, k) => c.reload = k),
        new Binding("jump", "Jump", c => c.jump, (c, k) => c.jump = k),
        new Binding("sprint", "Sprint", c => c.sprintKey, (c, k) => c.sprintKey = k),
        new Binding("crouch", "Crouch", c => c.crouch, (c, k) => c.crouch = k),
        new Binding("bomb", "Grenade", c => c.bomb, (c, k) => c.bomb = k),
        new Binding("item", "Medkit", c => c.useItem, (c, k) => c.useItem = k),
    };

    const string Prefix = "settings.key.";

    static readonly Dictionary<ControlSettings, Dictionary<string, KeyCode>> _authored = new Dictionary<ControlSettings, Dictionary<string, KeyCode>>();

    /// <summary>Raised after a key changes, so prompts and hints can redraw.</summary>
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _authored.Clear();
        Changed = null;
        Application.quitting -= RestoreAuthored;
        Application.quitting += RestoreAuthored;
    }

    /// <summary>Puts the player's keys onto a live ControlSettings. Safe to call again.</summary>
    public static void Apply(ControlSettings c)
    {
        if (c == null) return;
        if (!_authored.TryGetValue(c, out var authored))
        {
            authored = new Dictionary<string, KeyCode>();
            foreach (var b in All()) authored[b.Id] = b.Get(c);
            _authored[c] = authored;
        }
        foreach (var b in All())
            b.Set(c, Override(b.Id) ?? authored[b.Id]);
        c.Revision++;
    }

    /// <summary>The key an action is on for this player.</summary>
    public static KeyCode Current(string id)
    {
        var o = Override(id);
        if (o.HasValue) return o.Value;
        foreach (var kv in _authored) if (kv.Value.TryGetValue(id, out var k)) return k;
        foreach (var b in All()) if (b.Id == id) return b.Get(ControlSettings.CreateDefault());
        return KeyCode.None;
    }

    /// <summary>Puts an action on a key, swapping with whichever action had it.</summary>
    public static void Set(string id, KeyCode key)
    {
        var was = Current(id);
        foreach (var b in All())
            if (b.Id != id && Current(b.Id) == key) PlayerPrefs.SetInt(Prefix + b.Id, (int)was);
        PlayerPrefs.SetInt(Prefix + id, (int)key);
        PlayerPrefs.Save();
        foreach (var c in new List<ControlSettings>(_authored.Keys)) Apply(c);
        Rebind();
        Changed?.Invoke();
    }

    /// <summary>
    /// The Input System actions are bound once, when the player spawns; a key changed from the
    /// pause menu has to rebind them there and then or it would take effect next level.
    /// </summary>
    static void Rebind()
    {
        var motor = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
        if (motor != null && motor.controls != null) GameInput.Bind(motor.controls);
    }

    /// <summary>Back to the keys the controls were authored with.</summary>
    public static void ResetAll()
    {
        foreach (var b in All()) PlayerPrefs.DeleteKey(Prefix + b.Id);
        foreach (var c in new List<ControlSettings>(_authored.Keys)) Apply(c);
        Rebind();
        Changed?.Invoke();
    }

    static KeyCode? Override(string id)
        => PlayerPrefs.HasKey(Prefix + id) ? (KeyCode?)PlayerPrefs.GetInt(Prefix + id) : null;

    static void RestoreAuthored()
    {
        foreach (var kv in _authored)
        {
            if (kv.Key == null) continue;
            foreach (var b in All())
                if (kv.Value.TryGetValue(b.Id, out var k)) b.Set(kv.Key, k);
        }
    }

    /// <summary>
    /// The key or mouse button pressed this frame, as the KeyCode the controls use, or None.
    /// For the rebinding row: it is listening, and the next thing pressed is the answer.
    /// </summary>
    public static KeyCode PressedThisFrame()
    {
        var mouse = Mouse.current;
        if (mouse != null)
        {
            if (mouse.leftButton.wasPressedThisFrame) return KeyCode.Mouse0;
            if (mouse.rightButton.wasPressedThisFrame) return KeyCode.Mouse1;
            if (mouse.middleButton.wasPressedThisFrame) return KeyCode.Mouse2;
        }
        var kb = Keyboard.current;
        if (kb == null || !kb.anyKey.wasPressedThisFrame) return KeyCode.None;
        foreach (var k in kb.allKeys)
            if (k != null && k.wasPressedThisFrame) return ToKeyCode(k.keyCode);
        return KeyCode.None;
    }

    static KeyCode ToKeyCode(Key key)
    {
        switch (key)
        {
            case Key.Enter: return KeyCode.Return;
            case Key.LeftCtrl: return KeyCode.LeftControl;
            case Key.RightCtrl: return KeyCode.RightControl;
            case Key.LeftAlt: return KeyCode.LeftAlt;
            case Key.RightAlt: return KeyCode.RightAlt;
            case Key.Backquote: return KeyCode.BackQuote;
            case Key.LeftBracket: return KeyCode.LeftBracket;
            case Key.RightBracket: return KeyCode.RightBracket;
        }
        if (key >= Key.Digit1 && key <= Key.Digit9) return KeyCode.Alpha1 + (key - Key.Digit1);
        if (key == Key.Digit0) return KeyCode.Alpha0;
        return Enum.TryParse(key.ToString(), true, out KeyCode code) ? code : KeyCode.None;
    }
}
