#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The input layer, checked with devices that exist only for the test.
    ///
    /// Batch mode has no keyboard, mouse or pad to press, and the legacy input API had no way
    /// to fake one -- which is why the earlier checks drive <see cref="MobileInput"/> and
    /// private methods. The Input System does: <c>InputSystem.AddDevice</c> makes a real
    /// device and <c>QueueStateEvent</c> presses its buttons, through exactly the path a
    /// player's would take. So this presses them, and checks what the player would see:
    ///
    ///   * every key in all three presets has an Input System binding, and the stick shaping
    ///     does the arithmetic it says;
    ///   * every prompt, for every action, on every pad family, names a glyph that exists;
    ///   * pressing a pad button switches the scheme, lands the focus on something reachable,
    ///     and shows the ring; B closes the settings; a key press switches back;
    ///   * a DualShock is recognised as PlayStation and prompted with a cross;
    ///   * the mouse rule: a mouse button is not fire on a touch device;
    ///   * the interface scale reaches the canvas.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyInput
    /// </summary>
    public static class FPSKitInputTest
    {
        static readonly List<string> _failures = new List<string>();
        static readonly List<string> _errors = new List<string>();
        static readonly List<InputDevice> _added = new List<InputDevice>();
        static int _phase;
        static double _t0;
        static float _uiScaleBefore;
        static Gamepad _pad;
        static Keyboard _keys;
        static Mouse _mouse;

        public static void VerifyInput()
        {
            try
            {
                _failures.Clear(); _errors.Clear(); _added.Clear();
                FPSKitUIKit.Import(rebuildFonts: false);
                StaticChecks();
                EditorSceneManager.OpenScene(FPSKitUIKit.GalleryPath, OpenSceneMode.Single);
                _uiScaleBefore = GameSettings.UiScale;
                Application.logMessageReceived += OnLog;
                FPSKitPlayMode.SuspendStartScene();
                _phase = 0;
                _t0 = EditorApplication.timeSinceStartup;
                EditorApplication.update += Tick;
            }
            catch (Exception e)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        static void OnLog(string m, string s, LogType t)
        {
            // The editor probes for an Android device on entering play mode when the Android
            // build profile is active, and logs the failure as an exception. Not the game.
            if (m.Contains("Unity Remote requirements check failed")) return;

            if (t is LogType.Error or LogType.Exception or LogType.Assert) _errors.Add(m);
        }

        static void Fail(string what) => _failures.Add(what);

        // ==================================================================
        // Without play mode.
        // ==================================================================

        static void StaticChecks()
        {
            var c = ScriptableObject.CreateInstance<ControlSettings>();
            foreach (ControlSettings.Preset preset in Enum.GetValues(typeof(ControlSettings.Preset)))
            {
                c.ApplyPreset(preset);
                foreach (var k in new[] { c.moveForward, c.moveBack, c.moveLeft, c.moveRight, c.altForward, c.altBack,
                                          c.altLeft, c.altRight, c.fire, c.jump, c.aim, c.reload, c.crouch, c.altCrouch,
                                          c.bomb, c.useItem, c.melee, c.sprintKey })
                {
                    if (k == KeyCode.None) continue;
                    if (GameInput.PathFor(k) == null) Fail($"{preset}: {k} has no Input System binding");
                }
            }
            Object.DestroyImmediate(c);

            // The stick shaping: nothing inside the deadzone, full at the edge, direction kept.
            if (GameInput.Shape(new Vector2(0.1f, 0f), 0.15f, 2f) != Vector2.zero) Fail("a push inside the deadzone moved");
            var edge = GameInput.Shape(new Vector2(0f, 1f), 0.15f, 2f);
            if (Mathf.Abs(edge.y - 1f) > 0.001f) Fail($"a full push reads {edge.y}, not 1");
            var half = GameInput.Shape(new Vector2(0.575f, 0f), 0.15f, 2f);
            if (Mathf.Abs(half.x - 0.25f) > 0.01f) Fail($"half travel past the deadzone on a squared curve reads {half.x}, not 0.25");
            var diag = GameInput.Shape(new Vector2(0.6f, 0.6f), 0.15f, 1.8f);
            if (Mathf.Abs(diag.x - diag.y) > 0.0001f) Fail("the curve bent a diagonal");

            // Every prompt names a glyph that is in the sprite asset.
            var sprites = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(FPSKitUIKit.GlyphSpriteAssetPath);
            if (sprites == null) { Fail("no glyph sprite asset"); return; }
            var names = new HashSet<string>(sprites.spriteCharacterTable.Select(ch => ch.name));
            foreach (GameAction a in Enum.GetValues(typeof(GameAction)))
            {
                foreach (PadFamily f in Enum.GetValues(typeof(PadFamily)))
                {
                    string id = InputPrompts.PadGlyph(a, f);
                    if (!names.Contains(id)) Fail($"{a} on {f} prompts glyph '{id}', which does not exist");
                }
                if (!names.Contains(InputPrompts.TouchIcon(a))) Fail($"{a} on touch prompts '{InputPrompts.TouchIcon(a)}', which does not exist");
            }
            foreach (var id in new[] { "mouse_left", "mouse_right", "mouse_middle" })
                if (!names.Contains(id)) Fail($"no '{id}' glyph");
        }

        // ==================================================================
        // In play mode.
        // ==================================================================

        static void Press(InputDevice device, Action<InputEventPtr> set)
        {
            using (StateEvent.From(device, out var ptr))
            {
                set(ptr);
                InputSystem.QueueEvent(ptr);
            }
            InputSystem.Update();
        }

        static void Tick()
        {
            try
            {
                double t = EditorApplication.timeSinceStartup - _t0;
                if (t > 90) throw new Exception($"input test timed out in phase {_phase}");
                var es = EventSystem.current;

                switch (_phase)
                {
                    case 0:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        _t0 = EditorApplication.timeSinceStartup;
                        _phase = 1;
                        return;

                    case 1:
                        if (t < 1.0) return;
                        _keys = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
                        _pad = InputSystem.AddDevice<Gamepad>();
                        _added.Add(_pad);
                        GameInput.SetScheme(InputScheme.KeyboardMouse);
                        Press(_pad, p => _pad.buttonSouth.WriteValueIntoEvent(1f, p));
                        Press(_pad, p => _pad.buttonSouth.WriteValueIntoEvent(0f, p));
                        _phase = 2;
                        _t0 = EditorApplication.timeSinceStartup;
                        return;

                    case 2:
                        if (t < 0.5) return;
                        if (GameInput.Scheme != InputScheme.Gamepad) Fail($"a pad press left the scheme on {GameInput.Scheme}");
                        var sel = es != null ? es.currentSelectedGameObject : null;
                        if (sel == null) Fail("on a pad, nothing was selected");
                        else if (!es.GetComponent<UINavigator>().Usable(sel)) Fail($"on a pad, the selection {sel.name} cannot be pressed");
                        var ring = Object.FindObjectsByType<FlatRect>(FindObjectsInactive.Exclude).FirstOrDefault(r => r.name == "FocusRing");
                        if (ring == null) Fail("on a pad, no focus ring is showing");
                        Debug.Log($"[FPSKitBatch] pad selected {(sel != null ? sel.name : "nothing")}");

                        SettingsPanel.Show(Object.FindAnyObjectByType<Canvas>());
                        _phase = 3;
                        _t0 = EditorApplication.timeSinceStartup;
                        return;

                    case 3:
                        if (t < 0.5) return;
                        var settings = Object.FindAnyObjectByType<SettingsPanel>();
                        var inSettings = es.currentSelectedGameObject;
                        if (inSettings == null || settings == null || !inSettings.transform.IsChildOf(settings.panel.transform))
                            Fail($"opening settings on a pad left the focus on {(inSettings != null ? inSettings.name : "nothing")}");
                        Press(_pad, p => _pad.buttonEast.WriteValueIntoEvent(1f, p));
                        _phase = 4;
                        _t0 = EditorApplication.timeSinceStartup;
                        return;

                    case 4:
                        if (t < 0.3) return;
                        Press(_pad, p => _pad.buttonEast.WriteValueIntoEvent(0f, p));
                        var s = Object.FindAnyObjectByType<SettingsPanel>();
                        if (s != null && s.IsOpen) Fail("B did not close the settings");

                        // Back to the keyboard: a key press is enough.
                        Press(_keys, p => _keys.spaceKey.WriteValueIntoEvent(1f, p));
                        Press(_keys, p => _keys.spaceKey.WriteValueIntoEvent(0f, p));
                        _phase = 5;
                        _t0 = EditorApplication.timeSinceStartup;
                        return;

                    case 5:
                        if (t < 0.3) return;
                        if (GameInput.Scheme != InputScheme.KeyboardMouse) Fail($"a key press left the scheme on {GameInput.Scheme}");
                        var ringAfter = Object.FindObjectsByType<FlatRect>(FindObjectsInactive.Exclude).FirstOrDefault(r => r.name == "FocusRing");
                        if (ringAfter != null) Fail("the focus ring stayed up on the keyboard");

                        // A PlayStation pad is a PlayStation pad.
                        var ds = InputSystem.AddDevice<DualShock4GamepadHID>();
                        _added.Add(ds);
                        Press(ds, p => ds.buttonSouth.WriteValueIntoEvent(1f, p));
                        Press(ds, p => ds.buttonSouth.WriteValueIntoEvent(0f, p));
                        _phase = 6;
                        _t0 = EditorApplication.timeSinceStartup;
                        return;

                    case 6:
                        if (t < 0.3) return;
                        if (GameInput.Pad != PadFamily.PlayStation) Fail($"a DualShock was taken for {GameInput.Pad}");
                        if (!InputPrompts.For(GameAction.Jump).Contains("ps_cross")) Fail("a DualShock's jump is not prompted with a cross");

                        // The mouse rule, through the new path.
                        var controls = ControlSettings.CreateDefault();
                        GameInput.Bind(controls);
                        _mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
                        Press(_mouse, p => _mouse.leftButton.WriteValueIntoEvent(1f, p));
                        MobileInput.Active = false;
                        bool desktopFires = GameInput.Held(GameAction.Fire);
                        MobileInput.Active = true;
                        bool touchFires = GameInput.Held(GameAction.Fire);
                        MobileInput.Active = false;
                        Press(_mouse, p => _mouse.leftButton.WriteValueIntoEvent(0f, p));
                        if (!desktopFires) Fail("a mouse click is not fire on a desktop");
                        if (touchFires) Fail("a mouse click is fire on a touch device -- the touch-is-a-click bug is back");

                        // The interface scale reaches the canvas.
                        var canvas = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude).First(c => c.isRootCanvas);
                        var scaler = canvas.GetComponent<CanvasScaler>();
                        var before = scaler.referenceResolution;
                        GameSettings.UiScale = 1.25f;
                        var after = scaler.referenceResolution;
                        // Larger interface, smaller reference: the ratio is the ratio of the scales.
                        if (Mathf.Abs(before.x / after.x - 1.25f / _uiScaleBefore) > 0.01f)
                            Fail($"interface scale 125% moved the reference from {before.x} to {after.x}");
                        GameSettings.UiScale = _uiScaleBefore;
                        Finish();
                        return;
                }
            }
            catch (Exception e)
            {
                Fail(e.ToString());
                Finish();
            }
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            foreach (var d in _added) if (d != null && d.added) InputSystem.RemoveDevice(d);
            GameSettings.UiScale = _uiScaleBefore;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            FPSKitPlayMode.RestoreStartScene();
            foreach (var e in _errors) Fail("console: " + e);
            if (_failures.Count == 0)
            {
                Debug.Log("[FPSKitBatch] verify input passed: presets bound, prompts drawn, pad and keyboard switch, B backs out, PlayStation recognised, mouse rule held, interface scale applied");
                EditorApplication.Exit(0);
            }
            else
            {
                foreach (var f in _failures) Debug.LogError("[FPSKitBatch] " + f);
                Debug.LogError($"[FPSKitBatch] FAILED: {_failures.Count} problem(s)");
                EditorApplication.Exit(1);
            }
        }
    }
}
#endif
