using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.LowLevel;

/// <summary>The things a player does in a level, independent of what they do them with.</summary>
public enum GameAction { Move, Look, Fire, Aim, Jump, Crouch, Sprint, Reload, Bomb, UseItem, Pause, Melee }

/// <summary>What the player is holding.</summary>
public enum InputScheme { KeyboardMouse, Gamepad, Touch }

/// <summary>Which glyphs a gamepad's buttons are drawn with.</summary>
public enum PadFamily { Xbox, PlayStation, Generic }

/// <summary>
/// Every input in the game, through the Input System: one action map for play and one for
/// the interface, three control schemes, and which of them the player last touched.
///
/// <b>The keyboard bindings are written from <see cref="ControlSettings"/></b>, not typed
/// into an .inputactions file. The asset's three presets are what the instruction strip,
/// the HOW TO PLAY page and the bomb tutorial all describe, so a second list of keys would
/// be a second answer to "what does R do" -- and the one the player reads would be the one
/// that is out of date. The gamepad bindings are fixed: every shooter on a pad agrees on
/// them, and a player who has used one expects exactly these.
///
/// <b>The mouse rule survives the port.</b> Unity maps touch 0 onto the mouse in a browser,
/// and that once made the gun fire whenever a phone player moved the stick -- the worst bug
/// the game has shipped (CLAUDE.md, "A touch is also a mouse click"). So while
/// <see cref="MobileInput.Active"/>, no mouse control counts towards any action, and a mouse
/// event never switches the scheme away from touch.
///
/// <b>Mouse look is not read here.</b> <c>PlayerMotor.ReadMouseCounts</c> keeps its legacy
/// fallback, because on Linux/X11 the Input System's mouse delta reads zero for every frame
/// the cursor is locked. That is the one legacy read left, and it is why the project's
/// active input handler stays on Both.
///
/// <b>The scheme is whatever was actuated last</b>, past a threshold, so a gamepad resting
/// on a desk does not steal the prompts from the keyboard with sensor noise. Prompts, the
/// focus ring and the on-screen controls all follow <see cref="SchemeChanged"/>.
/// </summary>
public static class GameInput
{
    // ==================================================================
    // State. All of it cleared in ResetStatics: domain reload is off.
    // ==================================================================

    static InputActionAsset _asset;
    static InputActionMap _play, _ui;
    static readonly InputAction[] _actions = new InputAction[Enum.GetValues(typeof(GameAction)).Length];
    static ControlSettings _boundTo;
    static int _boundRevision = -1;
    static bool _listening;
    static InputScheme _scheme;
    static PadFamily _pad;
    static bool _schemeChosen;

    /// <summary>Raised when the player switches between keyboard, gamepad and touch.</summary>
    public static event Action SchemeChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        if (_listening) InputSystem.onEvent -= OnEvent;
        _listening = false;
        if (_asset != null)
        {
            _asset.Disable();
            UnityEngine.Object.Destroy(_asset);
        }
        _asset = null;
        _play = _ui = null;
        Array.Clear(_actions, 0, _actions.Length);
        _boundTo = null;
        _boundRevision = -1;
        _schemeChosen = false;
        _scheme = InputScheme.KeyboardMouse;
        _pad = PadFamily.Xbox;
        SchemeChanged = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        Ensure();
        if (!_listening)
        {
            InputSystem.onEvent += OnEvent;
            _listening = true;
        }
        ChooseInitialScheme();
    }

    // ==================================================================
    // The asset.
    // ==================================================================

    /// <summary>The action asset, built on first use.</summary>
    public static InputActionAsset Asset => Ensure();

    public static InputActionMap GameplayMap { get { Ensure(); return _play; } }
    public static InputActionMap UIMap { get { Ensure(); return _ui; } }

    public static InputAction UINavigate => UIMap.FindAction("Navigate");
    public static InputAction UISubmit => UIMap.FindAction("Submit");
    public static InputAction UICancel => UIMap.FindAction("Cancel");
    public static InputAction UIPoint => UIMap.FindAction("Point");
    public static InputAction UIClick => UIMap.FindAction("Click");
    public static InputAction UIRightClick => UIMap.FindAction("RightClick");
    public static InputAction UIMiddleClick => UIMap.FindAction("MiddleClick");
    public static InputAction UIScroll => UIMap.FindAction("ScrollWheel");
    public static InputAction UITabPrevious => UIMap.FindAction("TabPrevious");
    public static InputAction UITabNext => UIMap.FindAction("TabNext");

    static InputActionAsset Ensure()
    {
        if (_asset != null) return _asset;

        _asset = ScriptableObject.CreateInstance<InputActionAsset>();
        _asset.name = "MiniFPS Input";
        _asset.hideFlags = HideFlags.DontSave;
        _asset.AddControlScheme("Keyboard&Mouse").WithRequiredDevice("<Keyboard>").WithOptionalDevice("<Mouse>");
        _asset.AddControlScheme("Gamepad").WithRequiredDevice("<Gamepad>");
        _asset.AddControlScheme("Touch").WithRequiredDevice("<Touchscreen>");

        _play = _asset.AddActionMap("Gameplay");
        foreach (GameAction a in Enum.GetValues(typeof(GameAction)))
        {
            var type = a is GameAction.Move or GameAction.Look ? InputActionType.Value : InputActionType.Button;
            var action = _play.AddAction(a.ToString(), type, expectedControlLayout: type == InputActionType.Value ? "Vector2" : "Button");
            _actions[(int)a] = action;
        }
        AddPadBindings();

        _ui = _asset.AddActionMap("UI");
        var navigate = _ui.AddAction("Navigate", InputActionType.PassThrough, expectedControlLayout: "Vector2");
        navigate.AddBinding("<Gamepad>/leftStick", groups: "Gamepad");
        navigate.AddBinding("<Gamepad>/dpad", groups: "Gamepad");
        navigate.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow", "Keyboard&Mouse").With("Down", "<Keyboard>/downArrow", "Keyboard&Mouse")
            .With("Left", "<Keyboard>/leftArrow", "Keyboard&Mouse").With("Right", "<Keyboard>/rightArrow", "Keyboard&Mouse");
        var submit = _ui.AddAction("Submit", InputActionType.Button);
        submit.AddBinding("<Gamepad>/buttonSouth", groups: "Gamepad");
        submit.AddBinding("<Keyboard>/enter", groups: "Keyboard&Mouse");
        submit.AddBinding("<Keyboard>/numpadEnter", groups: "Keyboard&Mouse");
        var cancel = _ui.AddAction("Cancel", InputActionType.Button);
        cancel.AddBinding("<Gamepad>/buttonEast", groups: "Gamepad");
        cancel.AddBinding("<Keyboard>/escape", groups: "Keyboard&Mouse");
        _ui.AddAction("Point", InputActionType.PassThrough, "<Pointer>/position", expectedControlLayout: "Vector2");
        _ui.AddAction("Click", InputActionType.PassThrough, "<Pointer>/press", expectedControlLayout: "Button");
        _ui.AddAction("RightClick", InputActionType.PassThrough, "<Mouse>/rightButton", expectedControlLayout: "Button");
        _ui.AddAction("MiddleClick", InputActionType.PassThrough, "<Mouse>/middleButton", expectedControlLayout: "Button");
        _ui.AddAction("ScrollWheel", InputActionType.PassThrough, "<Mouse>/scroll", expectedControlLayout: "Vector2");
        var prev = _ui.AddAction("TabPrevious", InputActionType.Button);
        prev.AddBinding("<Gamepad>/leftShoulder", groups: "Gamepad");
        var next = _ui.AddAction("TabNext", InputActionType.Button);
        next.AddBinding("<Gamepad>/rightShoulder", groups: "Gamepad");

        _asset.Enable();
        return _asset;
    }

    /// <summary>
    /// The pad layout every shooter shares. B is crouch in play and back in menus, which is
    /// also universal; the two maps are never read for the same press, because a paused
    /// game reads no gameplay input at all.
    /// </summary>
    static void AddPadBindings()
    {
        void Bind(GameAction a, string path) => _actions[(int)a].AddBinding(path, groups: "Gamepad");
        Bind(GameAction.Move, "<Gamepad>/leftStick");
        Bind(GameAction.Look, "<Gamepad>/rightStick");
        Bind(GameAction.Fire, "<Gamepad>/rightTrigger");
        Bind(GameAction.Aim, "<Gamepad>/leftTrigger");
        Bind(GameAction.Jump, "<Gamepad>/buttonSouth");
        Bind(GameAction.Crouch, "<Gamepad>/buttonEast");
        Bind(GameAction.Reload, "<Gamepad>/buttonWest");
        Bind(GameAction.Sprint, "<Gamepad>/leftStickPress");
        Bind(GameAction.Bomb, "<Gamepad>/rightShoulder");
        Bind(GameAction.UseItem, "<Gamepad>/leftShoulder");
        Bind(GameAction.Pause, "<Gamepad>/start");
        // R3, where most pad shooters put the melee: the thumb is already on that stick.
        Bind(GameAction.Melee, "<Gamepad>/rightStickPress");
    }

    /// <summary>
    /// Rewrites the keyboard and mouse bindings from a <see cref="ControlSettings"/>, if it
    /// is not the one already bound or has been edited since. Cheap when nothing changed:
    /// a reference and an integer compared.
    /// </summary>
    public static void Bind(ControlSettings c)
    {
        Ensure();
        if (c == null || (c == _boundTo && c.Revision == _boundRevision)) return;
        _boundTo = c;
        _boundRevision = c.Revision;

        _play.Disable();
        foreach (var action in _actions)
        {
            for (int i = action.bindings.Count - 1; i >= 0; i--)
                if (action.bindings[i].groups == "Keyboard&Mouse")
                    action.ChangeBinding(i).Erase();
        }

        void Key(GameAction a, KeyCode k)
        {
            string path = PathFor(k);
            if (path != null) _actions[(int)a].AddBinding(path, groups: "Keyboard&Mouse");
        }

        foreach (var k in new[] { c.moveForward, c.moveBack, c.moveLeft, c.moveRight, c.altForward, c.altBack, c.altLeft, c.altRight })
            Key(GameAction.Move, k);
        Key(GameAction.Fire, c.fire);
        Key(GameAction.Aim, c.aim);
        Key(GameAction.Jump, c.jump);
        Key(GameAction.Crouch, c.crouch);
        Key(GameAction.Crouch, c.altCrouch);
        Key(GameAction.Sprint, c.sprintKey);
        Key(GameAction.Reload, c.reload);
        Key(GameAction.Bomb, c.bomb);
        Key(GameAction.UseItem, c.useItem);
        Key(GameAction.Melee, c.melee);
        Key(GameAction.Pause, KeyCode.Escape);
        Key(GameAction.Pause, KeyCode.P);
        _play.Enable();
    }

    public static InputAction ActionFor(GameAction a)
    {
        Ensure();
        return _actions[(int)a];
    }

    // ==================================================================
    // Reading actions.
    // ==================================================================

    /// <summary>Whether any control bound to the action is held. Never a mouse button on a touch device.</summary>
    public static bool Held(GameAction a, ControlSettings bindings = null)
    {
        if (bindings != null) Bind(bindings);
        foreach (var c in ActionFor(a).controls)
            if (c is ButtonControl b && Readable(b) && b.isPressed) return true;
        return false;
    }

    /// <summary>Whether any control bound to the action went down this frame.</summary>
    public static bool Pressed(GameAction a, ControlSettings bindings = null)
    {
        if (bindings != null) Bind(bindings);
        foreach (var c in ActionFor(a).controls)
            if (c is ButtonControl b && Readable(b) && b.wasPressedThisFrame) return true;
        return false;
    }

    /// <summary>Whether a gamepad control bound to the action is held. For the few places that treat the pad differently.</summary>
    public static bool PadHeld(GameAction a)
    {
        foreach (var c in ActionFor(a).controls)
            if (c.device is Gamepad && c is ButtonControl b && b.isPressed) return true;
        return false;
    }

    public static bool PadPressed(GameAction a)
    {
        foreach (var c in ActionFor(a).controls)
            if (c.device is Gamepad && c is ButtonControl b && b.wasPressedThisFrame) return true;
        return false;
    }

    static bool Readable(InputControl c) => !(MobileInput.Active && c.device is Mouse);

    /// <summary>
    /// The left stick, after the player's deadzone. Linear past it: a curve on movement
    /// makes walking slowly harder, and nobody asks for that.
    /// </summary>
    public static Vector2 PadMove
    {
        get
        {
            var pad = Gamepad.current;
            if (pad == null) return Vector2.zero;
            return Shape(pad.leftStick.ReadValue(), GameSettings.StickDeadzone, 1f);
        }
    }

    /// <summary>The right stick, after the deadzone and the response curve, as -1..1.</summary>
    public static Vector2 PadLook
    {
        get
        {
            var pad = Gamepad.current;
            if (pad == null) return Vector2.zero;
            return Shape(pad.rightStick.ReadValue(), GameSettings.StickDeadzone, GameSettings.StickCurve);
        }
    }

    /// <summary>
    /// Radial deadzone rescaled so the stick still reaches 1 at its edge, then the curve on
    /// the magnitude only -- so the direction a player pushes is the direction they get.
    /// Public so the settings test can check the arithmetic without a pad.
    /// </summary>
    public static Vector2 Shape(Vector2 raw, float deadzone, float exponent)
    {
        float m = raw.magnitude;
        if (m <= deadzone || m <= 0.0001f) return Vector2.zero;
        float t = Mathf.Clamp01((m - deadzone) / Mathf.Max(0.0001f, 1f - deadzone));
        return raw / m * Mathf.Pow(t, Mathf.Max(1f, exponent));
    }

    // ==================================================================
    // Reading keys that are not actions: the pause keys, the results screen, look keys.
    // ==================================================================

    public static bool KeyHeld(KeyCode k)
    {
        var control = ControlFor(k);
        return control != null && Readable(control) && control.isPressed;
    }

    public static bool KeyPressed(KeyCode k)
    {
        var control = ControlFor(k);
        return control != null && Readable(control) && control.wasPressedThisFrame;
    }

    /// <summary>Back: Escape, Q, or B/Circle. What every overlay closes on.</summary>
    public static bool BackPressed =>
        UICancel.WasPressedThisFrame() || KeyPressed(KeyCode.Q);

    /// <summary>Confirm on a card that has no button: Space, Enter or A/Cross.</summary>
    public static bool ConfirmPressed =>
        UISubmit.WasPressedThisFrame() || KeyPressed(KeyCode.Space);

    static ButtonControl ControlFor(KeyCode k)
    {
        if (k == KeyCode.None) return null;
        if (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6)
        {
            var mouse = Mouse.current;
            if (mouse == null) return null;
            return k switch
            {
                KeyCode.Mouse0 => mouse.leftButton,
                KeyCode.Mouse1 => mouse.rightButton,
                KeyCode.Mouse2 => mouse.middleButton,
                KeyCode.Mouse3 => mouse.backButton,
                KeyCode.Mouse4 => mouse.forwardButton,
                _ => null,
            };
        }
        var kb = Keyboard.current;
        if (kb == null) return null;
        var key = KeyFor(k);
        return key == Key.None ? null : kb[key];
    }

    /// <summary>The binding path for a legacy key code, or null for one with no equivalent.</summary>
    public static string PathFor(KeyCode k)
    {
        switch (k)
        {
            case KeyCode.None: return null;
            case KeyCode.Mouse0: return "<Mouse>/leftButton";
            case KeyCode.Mouse1: return "<Mouse>/rightButton";
            case KeyCode.Mouse2: return "<Mouse>/middleButton";
            case KeyCode.Mouse3: return "<Mouse>/backButton";
            case KeyCode.Mouse4: return "<Mouse>/forwardButton";
        }
        var key = KeyFor(k);
        if (key == Key.None) return null;
        string name = key.ToString();
        if (name.StartsWith("Digit")) name = name.Substring(5);
        else name = char.ToLowerInvariant(name[0]) + name.Substring(1);
        return "<Keyboard>/" + name;
    }

    /// <summary>
    /// Legacy <see cref="KeyCode"/> to Input System <see cref="Key"/>. Most names match; the
    /// exceptions are listed, and anything else unmatched is None rather than a guess.
    /// </summary>
    public static Key KeyFor(KeyCode k)
    {
        switch (k)
        {
            case KeyCode.Return: return Key.Enter;
            case KeyCode.KeypadEnter: return Key.NumpadEnter;
            case KeyCode.LeftControl: return Key.LeftCtrl;
            case KeyCode.RightControl: return Key.RightCtrl;
            case KeyCode.LeftCommand: return Key.LeftMeta;
            case KeyCode.RightCommand: return Key.RightMeta;
            case KeyCode.BackQuote: return Key.Backquote;
            case KeyCode.Quote: return Key.Quote;
            case KeyCode.KeypadPlus: return Key.NumpadPlus;
            case KeyCode.KeypadMinus: return Key.NumpadMinus;
            case KeyCode.KeypadMultiply: return Key.NumpadMultiply;
            case KeyCode.KeypadDivide: return Key.NumpadDivide;
            case KeyCode.KeypadPeriod: return Key.NumpadPeriod;
            case KeyCode.KeypadEquals: return Key.NumpadEquals;
            case KeyCode.Numlock: return Key.NumLock;
            case KeyCode.CapsLock: return Key.CapsLock;
            case KeyCode.ScrollLock: return Key.ScrollLock;
        }
        string name = k.ToString();
        if (name.StartsWith("Alpha")) name = "Digit" + name.Substring(5);
        else if (name.StartsWith("Keypad")) name = "Numpad" + name.Substring(6);
        return Enum.TryParse(name, out Key key) ? key : Key.None;
    }

    // ==================================================================
    // Which scheme is live.
    // ==================================================================

    public static InputScheme Scheme => _scheme;
    public static PadFamily Pad => _pad;
    public static bool UsingGamepad => _scheme == InputScheme.Gamepad;

    /// <summary>Forces the scheme. For tests and the device captures; play switches on its own.</summary>
    public static void SetScheme(InputScheme scheme, PadFamily pad = PadFamily.Xbox)
    {
        _schemeChosen = true;
        if (_scheme == scheme && _pad == pad) return;
        _scheme = scheme;
        _pad = pad;
        SchemeChanged?.Invoke();
    }

    static void ChooseInitialScheme()
    {
        if (_schemeChosen) return;
        bool touch = ScreenInfo.SimulatedTouch ?? (Application.isMobilePlatform || WebDevice.IsTouchOnly);
        _scheme = touch ? InputScheme.Touch : InputScheme.KeyboardMouse;
        if (Gamepad.current != null && Keyboard.current == null && !touch)
        {
            _scheme = InputScheme.Gamepad;
            _pad = FamilyOf(Gamepad.current);
        }
    }

    public static PadFamily FamilyOf(InputDevice device)
    {
        if (device is DualShockGamepad) return PadFamily.PlayStation;
        string text = (device.description.product + " " + device.description.manufacturer + " " + device.layout).ToLowerInvariant();
        if (text.Contains("dualsense") || text.Contains("dualshock") || text.Contains("playstation") || text.Contains("sony"))
            return PadFamily.PlayStation;
        if (text.Contains("xbox") || text.Contains("xinput") || text.Contains("microsoft"))
            return PadFamily.Xbox;
        return PadFamily.Generic;
    }

    /// <summary>
    /// Every state event, from any device. Switches only on a real actuation -- past half
    /// travel on a stick or a trigger, a press on a button, a real move of a mouse -- so a
    /// pad's sensor noise or a resting thumb cannot flip the prompts back and forth.
    /// </summary>
    static void OnEvent(InputEventPtr eventPtr, InputDevice device)
    {
        if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>()) return;

        InputScheme scheme;
        if (device is Gamepad) scheme = InputScheme.Gamepad;
        else if (device is Touchscreen) scheme = InputScheme.Touch;
        else if (device is Keyboard || device is Mouse)
        {
            // A browser turns every touch into mouse events as well. On a touch device
            // those are not the player picking up a mouse.
            if (MobileInput.Active && device is Mouse) return;
            scheme = InputScheme.KeyboardMouse;
        }
        else return;

        PadFamily pad = scheme == InputScheme.Gamepad ? FamilyOf(device) : _pad;
        if (scheme == _scheme && pad == _pad) return;

        foreach (var control in eventPtr.EnumerateChangedControls(device, 0.5f))
        {
            if (control.noisy || control.synthetic) continue;
            // A mouse's position changes whenever the window moves under it; its delta is
            // what says a hand moved it.
            if (device is Mouse && control.name == "position") continue;
            _schemeChosen = true;
            _scheme = scheme;
            _pad = pad;
            SchemeChanged?.Invoke();
            return;
        }
    }
}
