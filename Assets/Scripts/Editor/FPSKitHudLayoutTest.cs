#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The HUD layout regression test: a layout saved through <see cref="HudLayout"/> must move,
    /// resize, fade and hide the real elements, restyle the real crosshair, survive a second
    /// play session, and come back untouched from a cancelled preview. The editor has to open
    /// over the arena with an outline per element, and two fire buttons held at once must not
    /// be released by lifting one of them.
    ///
    /// Written against the data path rather than by dragging, for the reason VerifyBomb drives
    /// PumpAim: batch mode has no pointer, and a check that needs one would be a check that
    /// never runs. What the drag produces is an entry; what this checks is that an entry lands.
    /// The player's own layouts are backed up first and put back in Detach.
    /// </summary>
    public static class FPSKitHudLayoutTest
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";
        const double HardTimeout = 240.0;

        enum Phase { Enter, Apply, Check, Audit, Exit, Reenter, Persisted, Done }

        // The screens to open and raycast, one at a time: opened, given a frame so their
        // graphics are registered with the canvas, then audited.
        static List<(string name, Action open, Func<Transform> root, Action close)> _screens;
        static int _screen;
        static bool _opened;
        static Phase _phase;
        static double _startedAt, _until;
        static readonly List<string> Errors = new List<string>();
        static readonly StringBuilder Notes = new StringBuilder();
        static readonly Dictionary<string, string> _backup = new Dictionary<string, string>();

        static readonly string[] Keys =
        {
            "settings.hud.desktop", "settings.hud.tablet", "settings.hud.handset",
        };

        public static void VerifyHudLayout()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                SaveMigration.Apply();
                Errors.Clear();
                Notes.Clear();
                _backup.Clear();
                foreach (var k in Keys) _backup[k] = PlayerPrefs.GetString(k, null);
                foreach (var k in Keys) PlayerPrefs.DeleteKey(k);
                _phase = Phase.Enter;
                _startedAt = EditorApplication.timeSinceStartup;
                FPSKitPlayMode.SuspendStartScene();
                EditorApplication.update += Tick;
                Debug.Log("[FPSKitBatch] hud layout test: a saved layout must move, size, fade and hide the real HUD and last a restart");
            }
            catch (Exception e)
            {
                Detach();
                Debug.LogError($"[FPSKitBatch] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        static void Wait(double seconds, Phase next) { _until = EditorApplication.timeSinceStartup + seconds; _phase = next; }
        static bool Waiting() => EditorApplication.timeSinceStartup < _until;

        static HudLayout.Data Layout()
        {
            var d = new HudLayout.Data();
            var map = d.Ensure("hud.minimap");
            map.placed = true; map.ax = 1f; map.ay = 0f; map.x = -30f; map.y = 30f; map.scale = 1.3f; map.opacity = 0.5f;
            d.Ensure("hud.feed").hidden = true;
            d.crosshair.style = HudLayout.CrosshairStyle.Dot;
            d.crosshair.color = 5;
            return d;
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _startedAt > HardTimeout)
                    throw new Exception($"hud layout test exceeded {HardTimeout}s in {_phase}");

                switch (_phase)
                {
                    case Phase.Enter:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        Wait(2.5, Phase.Apply);
                        return;

                    case Phase.Apply:
                    {
                        if (Waiting()) return;
                        int marked = 0;
                        foreach (var id in new[] { "hud.minimap", "hud.mission", "hud.objective", "hud.feed", "hud.player", "hud.abilities", "hud.weapon", "hud.run" })
                            if (HudLayout.TargetById(id) != null) marked++; else Errors.Add($"no movable element \"{id}\"");
                        Notes.Append($"\n  {marked}/8 HUD elements movable");

                        HudLayout.Save(Layout());
                        Wait(0.5, Phase.Check);
                        return;
                    }

                    case Phase.Check:
                    {
                        if (Waiting()) return;
                        CheckApplied("after saving");

                        // A preview must be cancellable: shown, then put back exactly.
                        var other = new HudLayout.Data();
                        other.Ensure("hud.minimap").hidden = true;
                        HudLayout.Preview(other);
                        var map = HudLayout.TargetById("hud.minimap");
                        if (map != null && map.GetComponent<CanvasGroup>().alpha > 0.01f)
                            Errors.Add("a previewed layout that hides the minimap left it showing");
                        HudLayout.Revert();
                        CheckApplied("after a cancelled preview");

                        // The editor opens over the arena with an outline for each element.
                        var hud = UnityEngine.Object.FindAnyObjectByType<HUDController>();
                        var editor = HudEditor.Open(hud, fromMenu: false);
                        if (editor == null || !editor.IsOpen) Errors.Add("the HUD editor did not open");
                        else
                        {
                            int outlines = editor.transform.Find("Sheet/Handles")?.childCount ?? 0;
                            if (outlines < 8) Errors.Add($"the editor outlines {outlines} elements, fewer than the HUD has");
                            Notes.Append($"\n  editor opened with {outlines} outlines");
                            ClickTabs(editor.GetComponentInChildren<UITabBar>(true), "HUD editor");
                            editor.Close();
                        }
                        CheckApplied("after the editor was cancelled");

                        CheckScreensAnswerClicks(hud);

                        // Two fire buttons: lifting one must not stop the other.
                        MobileInput.Reset();
                        MobileInput.PressFire(true);
                        MobileInput.PressFire(true);
                        MobileInput.PressFire(false);
                        bool stillFiring = MobileInput.Fire;
                        MobileInput.PressFire(false);
                        if (!stillFiring) Errors.Add("with two fire buttons held, lifting one stopped the gun");
                        if (MobileInput.Fire) Errors.Add("with both fire buttons lifted, the gun kept firing");
                        MobileInput.Reset();

                        _screens = Screens(hud);
                        _screen = 0;
                        _opened = false;
                        _phase = Phase.Audit;
                        return;
                    }

                    case Phase.Audit:
                    {
                        if (Waiting()) return;
                        if (_screen >= _screens.Count)
                        {
                            if (GameDirector.Instance != null) GameDirector.Instance.SetPaused(false);
                            _phase = Phase.Exit;
                            return;
                        }
                        var step = _screens[_screen];
                        if (!_opened)
                        {
                            step.open();
                            _opened = true;
                            Wait(0.4, Phase.Audit);
                            return;
                        }
                        var root = step.root();
                        if (root == null) Errors.Add($"{step.name} did not open");
                        else Reach(root, step.name);
                        step.close?.Invoke();
                        _opened = false;
                        _screen++;
                        Wait(0.2, Phase.Audit);
                        return;
                    }

                    case Phase.Exit:
                        EditorApplication.ExitPlaymode();
                        _phase = Phase.Reenter;
                        return;

                    case Phase.Reenter:
                        if (EditorApplication.isPlaying) return;
                        EditorApplication.EnterPlaymode();
                        Wait(3.0, Phase.Persisted);
                        return;

                    case Phase.Persisted:
                        if (Waiting() || !EditorApplication.isPlaying) return;
                        CheckApplied("in a second play session");
                        EditorApplication.ExitPlaymode();
                        _phase = Phase.Done;
                        return;

                    case Phase.Done:
                        if (EditorApplication.isPlaying) return;
                        Finish();
                        return;
                }
            }
            catch (Exception e)
            {
                Errors.Add(e.ToString());
                Finish();
            }
        }

        /// <summary>
        /// Clicks -- through each button's own onClick, the way a pointer's click arrives -- every
        /// tab on the runtime-built screens and a chooser's arrows. They were wired in Awake,
        /// which ran before UIKit assigned the tabs, so on every screen built at runtime no tab
        /// answered at all; every check drove the panels through ShowTab and never noticed.
        /// </summary>
        static void CheckScreensAnswerClicks(HUDController hud)
        {
            var canvas = hud.GetComponentInParent<Canvas>();
            var settings = SettingsPanel.Show(canvas);
            if (settings == null) { Errors.Add("Settings did not open"); return; }
            var bar = settings.panel.GetComponentInChildren<UITabBar>(true);
            ClickTabs(bar, "Settings");

            // A chooser's arrows: forward then back, on the HUD page's crosshair style.
            if (bar != null)
            {
                for (int i = 0; i < bar.tabs.Length; i++)
                    if (bar.tabs[i].label != null && bar.tabs[i].label.text.ToUpperInvariant() == "HUD") bar.tabs[i].onClick.Invoke();
                UIChoice choice = null;
                foreach (var c in settings.panel.GetComponentsInChildren<UIChoice>(false)) { choice = c; break; }
                if (choice == null) Errors.Add("Settings' HUD page shows no chooser to click");
                else
                {
                    int before = choice.Index;
                    choice.next.onClick.Invoke();
                    int after = choice.Index;
                    choice.previous.onClick.Invoke();
                    if (after == before && choice.options.Length > 1)
                        Errors.Add("a Settings chooser's arrow did nothing when clicked");
                }
            }
            settings.Close();

            var achievements = AchievementsPanel.Create(canvas);
            achievements.Open();
            ClickTabs(achievements.panel.GetComponentInChildren<UITabBar>(true), "Achievements filters");
            achievements.Close();
        }

        /// <summary>Every screen built at runtime this round, each opened for real.</summary>
        static List<(string, Action, Func<Transform>, Action)> Screens(HUDController hud)
        {
            var canvas = hud.GetComponentInParent<Canvas>();
            var list = new List<(string, Action, Func<Transform>, Action)>();
            var probe = SettingsPanel.Show(canvas);
            int tabs = probe != null ? probe.panel.GetComponentInChildren<UITabBar>(true).tabs.Length : 0;
            if (probe != null) probe.Close();
            for (int i = 0; i < tabs; i++)
            {
                int tab = i;
                list.Add(($"Settings tab {tab + 1}", () =>
                {
                    var s = SettingsPanel.Show(canvas);
                    s.panel.GetComponentInChildren<UITabBar>(true).tabs[tab].onClick.Invoke();
                }, () => SettingsPanel.Show(canvas).panel.transform, () => SettingsPanel.Show(canvas).Close()));
            }
            list.Add(("Pause menu", () => GameDirector.Instance.SetPaused(true),
                      () => Find(canvas, "PauseLayer"), null));
            list.Add(("Quit confirmation", () => ClickNamed(Find(canvas, "PauseLayer"), "Quit"),
                      () => canvas.GetComponentInChildren<ConfirmDialog>(true)?.panel.transform,
                      () => canvas.GetComponentInChildren<ConfirmDialog>(true)?.Close()));
            list.Add(("HUD editor", () => HudEditor.Open(hud, fromMenu: false),
                      () => canvas.GetComponentInChildren<HudEditor>(true)?.transform.Find("Sheet/PanelArea"),
                      () => canvas.GetComponentInChildren<HudEditor>(true)?.Close()));
            list.Add(("Achievements", () => AchievementsPanel.Create(canvas).Open(),
                      () => canvas.GetComponentInChildren<AchievementsPanel>(true)?.panel.transform,
                      () => canvas.GetComponentInChildren<AchievementsPanel>(true)?.Close()));
            return list;
        }

        static Transform Find(Canvas canvas, string name)
        {
            foreach (var t in canvas.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        static void ClickNamed(Transform root, string name)
        {
            if (root == null) return;
            foreach (var b in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                if (b.name == name && b.gameObject.activeInHierarchy) { b.onClick.Invoke(); return; }
        }

        /// <summary>
        /// Every visible control under a screen must be what a click at its centre lands on --
        /// through every raycaster, so a panel from another canvas on top is caught too. A
        /// control scrolled out of its list's mask is skipped: scrolling is how it is reached.
        /// </summary>
        static void Reach(Transform root, string screen)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) { Errors.Add("no EventSystem"); return; }
            int tested = 0;
            foreach (var sel in root.GetComponentsInChildren<UnityEngine.UI.Selectable>(false))
            {
                if (!sel.isActiveAndEnabled || !sel.interactable) continue;
                var rt = (RectTransform)sel.transform;
                var canvas = sel.GetComponentInParent<Canvas>().rootCanvas;
                var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                Vector2 point = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
                var mask = sel.GetComponentInParent<UnityEngine.UI.RectMask2D>();
                if (mask != null && !RectTransformUtility.RectangleContainsScreenPoint(mask.rectTransform, point, cam)) continue;
                var hits = new List<UnityEngine.EventSystems.RaycastResult>();
                es.RaycastAll(new UnityEngine.EventSystems.PointerEventData(es) { position = point }, hits);
                tested++;
                if (hits.Count == 0 || !hits[0].gameObject.transform.IsChildOf(rt))
                    Errors.Add($"{screen}: \"{sel.name}\" cannot be clicked -- a click at it lands on " +
                               (hits.Count == 0 ? "nothing" : $"\"{hits[0].gameObject.name}\""));
            }
            Notes.Append($"\n  {screen}: {tested} controls reachable");
        }

        static void ClickTabs(UITabBar bar, string screen)
        {
            if (bar == null || bar.tabs.Length == 0) { Errors.Add($"{screen} has no tabs to click"); return; }
            for (int i = bar.tabs.Length - 1; i >= 0; i--)
            {
                bar.tabs[i].onClick.Invoke();
                if (bar.Selected != i)
                    Errors.Add($"{screen}: clicking the \"{bar.tabs[i].label?.text}\" tab did nothing");
            }
            Notes.Append($"\n  {screen}: {bar.tabs.Length} tabs answer a click");
        }

        static void CheckApplied(string when)
        {
            var map = HudLayout.TargetById("hud.minimap");
            if (map == null) { Errors.Add($"{when}: no minimap to check"); return; }
            var r = map.Rect;
            if (r.anchorMin != new Vector2(1f, 0f) || r.anchoredPosition != new Vector2(-30f, 30f))
                Errors.Add($"{when}: the minimap is anchored at {r.anchorMin} {r.anchoredPosition}, not the bottom-right corner the layout put it in");
            if (Mathf.Abs(r.localScale.x - 0.75f * 1.3f) > 0.01f)
                Errors.Add($"{when}: the minimap is at scale {r.localScale.x:0.00}, not its own 0.75 times the layout's 1.3");
            var g = map.GetComponent<CanvasGroup>();
            if (g == null || Mathf.Abs(g.alpha - 0.5f) > 0.01f) Errors.Add($"{when}: the minimap is not at the layout's 50% opacity");

            var feed = HudLayout.TargetById("hud.feed");
            if (feed == null || feed.GetComponent<CanvasGroup>().alpha > 0.01f) Errors.Add($"{when}: the kill feed the layout hides is still showing");

            var hud = UnityEngine.Object.FindAnyObjectByType<HUDController>();
            if (hud != null)
            {
                bool armsShown = false;
                foreach (var arm in hud.crosshairArms) if (arm != null && arm.gameObject.activeSelf) armsShown = true;
                if (armsShown) Errors.Add($"{when}: the crosshair is a dot in the layout and its arms are still drawn");
                if ((hud.crosshairColor - HudLayout.Colors[5].color).maxColorComponent > 0.01f)
                    Errors.Add($"{when}: the crosshair is not the layout's red");
            }
        }

        static void Finish()
        {
            Detach();
            if (Errors.Count > 0)
            {
                var report = new StringBuilder();
                foreach (var e in Errors) report.Append($"\n  - {e}");
                Debug.LogError($"[FPSKitBatch] FAILED: the HUD layout does not hold:{report}{Notes}");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log($"[FPSKitBatch] verify hud layout passed: moved, sized, faded and hid the real HUD, restyled the crosshair, " +
                      $"cancelled a preview cleanly, and kept the layout into a second session.{Notes}");
            EditorApplication.Exit(0);
        }

        static void Detach()
        {
            EditorApplication.update -= Tick;
            foreach (var kv in _backup)
            {
                if (string.IsNullOrEmpty(kv.Value)) PlayerPrefs.DeleteKey(kv.Key);
                else PlayerPrefs.SetString(kv.Key, kv.Value);
            }
            PlayerPrefs.Save();
            FPSKitPlayMode.RestoreStartScene();
        }
    }
}
#endif
