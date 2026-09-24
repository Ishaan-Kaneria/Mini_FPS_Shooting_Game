#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Builds the on-screen control layer into a real arena and checks that every part
    /// of it is present, wired and reachable.
    ///
    /// This is the check the kit did not have, and the gap was the worst shape a gap
    /// comes in: the touch layer is added only while staging a mobile build, so it was
    /// exercised by nothing until a player had the finished app in their hands. A
    /// control that failed to wire does not throw and does not log -- it produces a
    /// game where the thumb does nothing, which is indistinguishable from the game
    /// having frozen.
    ///
    /// Edit mode only. Everything here is construction, so none of it needs play mode.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyTouch
    /// </summary>
    public static class FPSKitTouchTest
    {
        public static void VerifyTouch()
        {
            var problems = new List<string>();
            var notes = new StringBuilder();

            try
            {
                string path = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";

                if (!System.IO.File.Exists(path))
                    throw new Exception($"{path} does not exist. Run BuildAllThemes first.");

                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                FPSKitMobileControls.AddMobileControls(askFirst: false);

                Check(problems, notes);

                if (problems.Count > 0)
                {
                    var report = new StringBuilder();
                    foreach (var problem in problems) report.Append($"\n  - {problem}");

                    Debug.LogError($"[FPSKitBatch] FAILED: the touch layer is not playable:{report}{notes}");
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log($"[FPSKitBatch] verify touch passed.{notes}");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: {e}{notes}");
                EditorApplication.Exit(1);
            }
        }

        // ==================================================================
        static void Check(List<string> problems, StringBuilder notes)
        {
            // ---- an EventSystem, without which no touch reaches anything ----
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
                problems.Add("no EventSystem: not one touch would reach any control");

            var controls = UnityEngine.Object.FindAnyObjectByType<TouchControls>();
            if (controls == null)
            {
                problems.Add("no TouchControls root");
                return;
            }

            // ---- the safe area, or the pause button hides under the notch ----
            var safeArea = UnityEngine.Object.FindAnyObjectByType<SafeAreaFitter>();
            if (safeArea == null)
                problems.Add("no SafeAreaFitter: on a notched phone the pause button is " +
                             "drawn under the cutout, and it is the only way out of a level");

            // ---- the stick ----
            var stick = UnityEngine.Object.FindAnyObjectByType<VirtualJoystick>();
            if (stick == null) problems.Add("no VirtualJoystick: the player cannot move");
            else
            {
                if (stick.profile == null)
                    problems.Add("the move stick has no TouchProfile, so its size is in " +
                                 "pixels rather than millimetres and push-to-sprint is off");

                if (stick.pad == null || stick.handle == null)
                    problems.Add("the move stick has no pad or handle, so nothing appears " +
                                 "under the thumb and the stick cannot be found by feel");
            }

            // ---- the look surface ----
            var look = UnityEngine.Object.FindAnyObjectByType<TouchLookArea>();
            if (look == null) problems.Add("no TouchLookArea: the player cannot turn");
            else if (look.profile == null)
                problems.Add("the look area has no TouchProfile, so sensitivity is not " +
                             "corrected for screen density and sights share hip-fire speed");

            // ---- every action a player needs, exactly once ----
            var buttons = UnityEngine.Object.FindObjectsByType<TouchButton>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            var seen = new Dictionary<TouchButton.ActionKind, int>();
            foreach (var button in buttons)
            {
                seen.TryGetValue(button.action, out int count);
                seen[button.action] = count + 1;

                if (button.profile == null)
                    problems.Add($"the {button.action} button has no TouchProfile, so it " +
                                 "cannot enforce a minimum touch size");
            }

            // Whatever profile the layer was built against. Taken from the parts rather
            // than loaded from disk, because what matters is what this scene is wired to.
            TouchProfile profile = look != null ? look.profile : null;
            foreach (var button in buttons)
                if (button.profile != null) { profile = button.profile; break; }

            // Sprint is the one the cluster does not draw by default: the stick sprints
            // when pushed to its edge, and a sprint button would want the aiming thumb.
            // Crouch is always drawn now. Sprint is required only when the profile asks
            // for it -- and still checked for duplicates, because two controls answering
            // for one action is a bug either way.
            var optional = new HashSet<TouchButton.ActionKind>();
            if (profile == null || !profile.showSprintButton)
                optional.Add(TouchButton.ActionKind.Sprint);

            foreach (TouchButton.ActionKind kind in Enum.GetValues(typeof(TouchButton.ActionKind)))
            {
                if (!seen.ContainsKey(kind))
                {
                    if (!optional.Contains(kind)) problems.Add($"no {kind} button");
                }
                else if (seen[kind] > 1)
                {
                    problems.Add($"{seen[kind]} {kind} buttons, so two controls answer for one action");
                }
            }

            // With no sprint button, the stick is the only thing that can sprint, so the
            // thing that makes that work has to be present and configured. Dropping the
            // button and the stick's push threshold together would leave a build that
            // simply cannot run.
            if (optional.Contains(TouchButton.ActionKind.Sprint))
            {
                var sprintStick = UnityEngine.Object.FindAnyObjectByType<VirtualJoystick>();

                if (sprintStick == null)
                    problems.Add("no sprint button and no joystick either, so nothing in " +
                                 "the build can sprint");
                else if (sprintStick.profile == null)
                    problems.Add("the joystick has no TouchProfile, so pushing it to the " +
                                 "edge cannot sprint -- and there is no sprint button to " +
                                 "fall back on");
                else if (sprintStick.profile.sprintPush >= 0.999f)
                    problems.Add($"the joystick's sprintPush is {sprintStick.profile.sprintPush:0.00}, " +
                                 "which cannot be reached, and there is no sprint button");
            }

            // ---- fire must be able to aim at the same time ----
            TouchButton fire = null;
            foreach (var button in buttons)
                if (button.action == TouchButton.ActionKind.Fire) fire = button;

            if (fire != null && !fire.dragFire)
                problems.Add("the fire button is not drag-fire, so the right thumb cannot " +
                             "shoot and aim at once -- every fight becomes a choice between " +
                             "firing where the enemy was and tracking them without firing");

            // ---- nothing may overlap, or one thumb presses two things ----
            CheckOverlaps(buttons, problems);

            // ---- a touch must not also be read as a mouse click ----
            CheckMouseSuppression(problems);

            // ---- aim assist, wired to the motor rather than merely present ----
            var assist = UnityEngine.Object.FindAnyObjectByType<TouchAimAssist>();
            var motor = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();

            if (assist == null)
                problems.Add("no TouchAimAssist: a thumb resolves about a degree, so " +
                             "without it the player is being tested on a motor task " +
                             "nobody can perform");
            else
            {
                if (assist.profile == null) problems.Add("aim assist has no TouchProfile");
                if (assist.eye == null) problems.Add("aim assist has no camera to aim from");
                if (assist.sightBlockers == 0)
                    problems.Add("aim assist has an empty sight mask, so it would help " +
                                 "the player track enemies through walls");
            }

            if (motor != null && motor.aimAssist == null)
                problems.Add("the motor is not pointed at the aim assist, so it is in the " +
                             "scene doing nothing -- which looks exactly like it working");

            notes.Append($"\n  {buttons.Length} buttons, stick {(stick != null ? "yes" : "no")}, " +
                         $"look {(look != null ? "yes" : "no")}, " +
                         $"safe area {(safeArea != null ? "yes" : "no")}, " +
                         $"drag-fire {(fire != null && fire.dragFire ? "on" : "off")}");
        }

        /// <summary>
        /// Asserts that the mouse half of the legacy input is ignored while the on-screen
        /// controls are live.
        ///
        /// This is the regression test for the worst bug the touch layer ever had, and
        /// the bug was not in the touch layer. Unity maps touch 0 onto mouse button 0 on
        /// mobile and in a browser, and the default fire binding is Mouse0 -- so
        /// `Input.GetKey(KeyCode.Mouse0)` was true whenever any finger was anywhere on
        /// the glass. The gun fired continuously while the player dragged the move
        /// stick, and once for every tap on any button or on empty space. The legacy read
        /// goes straight to the device, so it made no difference that the joystick had
        /// swallowed the touch as far as the EventSystem was concerned.
        ///
        /// Written against CanRead rather than Held because batch mode has no mouse to
        /// press: a test that asked "is fire held" would pass identically with the rule
        /// removed, which is the kind of test that is worse than none.
        /// </summary>
        static void CheckMouseSuppression(List<string> problems)
        {
            bool wasActive = MobileInput.Active;

            try
            {
                MobileInput.Active = true;

                for (var key = KeyCode.Mouse0; key <= KeyCode.Mouse6; key++)
                    if (ControlSettings.CanRead(key))
                        problems.Add($"{key} is still readable while the on-screen controls " +
                                     "are live, so a touch anywhere on the screen would fire " +
                                     "every action bound to it");

                // The keyboard must survive: a tablet with one attached should work.
                if (!ControlSettings.CanRead(KeyCode.W))
                    problems.Add("keyboard bindings are suppressed on touch devices too, " +
                                 "which breaks a tablet with a keyboard attached");

                MobileInput.Active = false;

                if (!ControlSettings.CanRead(KeyCode.Mouse0))
                    problems.Add("Mouse0 is unreadable with the touch layer inactive, so " +
                                 "desktop players cannot fire");
            }
            finally
            {
                MobileInput.Active = wasActive;
            }
        }

        /// <summary>
        /// Fails when two buttons are closer together than a thumb.
        ///
        /// <b>This used to ask whether two rects intersect, and that is the wrong
        /// question.</b> Intersection is a test for zero clearance. A thumb is a contact
        /// patch about 20mm across, so two buttons that merely fail to overlap are still
        /// one button as far as the player is concerned -- and the shipped layout sat
        /// 2.4mm apart on every phone measured, passed this check, and produced exactly
        /// the complaint you would expect: the controls do the wrong thing.
        ///
        /// So the clearance is measured in millimetres on a reference phone, and it is
        /// measured edge to edge. It is deliberately less than a whole thumb -- the
        /// centres carry most of the reliability and a full 20mm of dead space between
        /// every pair would not fit on a phone -- but it is far enough that the patch
        /// has somewhere to land.
        /// </summary>
        const float RequiredGapMm = 4f;

        static void CheckOverlaps(TouchButton[] buttons, List<string> problems)
        {
            // What the layout is authored against. The cluster converts millimetres to
            // canvas units with the live canvas scale, so the test asks the same
            // question in the same units rather than re-deriving it and drifting.
            float unit = TouchMetrics.MillimetresToPixels(1f) / Mathf.Max(0.0001f, CanvasScale(buttons));
            float required = RequiredGapMm * unit;

            for (int i = 0; i < buttons.Length; i++)
            {
                var a = buttons[i].transform as RectTransform;
                if (a == null || !buttons[i].gameObject.activeInHierarchy) continue;

                for (int j = i + 1; j < buttons.Length; j++)
                {
                    var b = buttons[j].transform as RectTransform;
                    if (b == null || !buttons[j].gameObject.activeInHierarchy) continue;

                    if (a.anchorMin != b.anchorMin || a.anchorMax != b.anchorMax) continue;

                    Vector2 delta = a.anchoredPosition - b.anchoredPosition;

                    // Separated if either axis separates them, which is what a gap
                    // between two axis-aligned squares means.
                    float gapX = Mathf.Abs(delta.x) - (a.sizeDelta.x + b.sizeDelta.x) * 0.5f;
                    float gapY = Mathf.Abs(delta.y) - (a.sizeDelta.y + b.sizeDelta.y) * 0.5f;
                    float gap = Mathf.Max(gapX, gapY);

                    if (gap < required)
                        problems.Add($"the {buttons[i].action} and {buttons[j].action} buttons are " +
                                     $"{gap / unit:0.0}mm apart, under the {RequiredGapMm:0.0}mm a thumb " +
                                     "needs -- one press lands on both");
                }
            }
        }

        /// <summary>The scale the buttons' own canvas is drawn at.</summary>
        static float CanvasScale(TouchButton[] buttons)
        {
            foreach (var button in buttons)
            {
                if (button == null) continue;

                var canvas = button.GetComponentInParent<Canvas>();
                if (canvas != null) return canvas.scaleFactor;
            }

            return 1f;
        }
    }
}
#endif
