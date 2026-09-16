#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Plays the whole loop the player sees: dashboard, into an arena, back out again.
    ///
    /// This is the regression test for a class of failure that compiles perfectly and
    /// ruins the game -- a menu whose buttons load nothing, a quit key with nowhere to
    /// go, a dashboard that never comes back. None of it is reachable from a scene
    /// build check, because every part exists; what fails is the joins between them.
    ///
    /// It asserts the four things that make the loop a loop: the dashboard offers every
    /// arena in the catalog, an arena opens its ladder of levels with the right ones
    /// locked, the arena loads and states its keys on screen, and quitting lands back on
    /// the dashboard with the attempt recorded.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyFlow
    /// </summary>
    public static class FPSKitFlowTest
    {
        const double HardTimeout = 240.0;

        enum Phase
        {
            Enter, InspectMenu, OpenDialog, ExitDialog,
            OpenLevels, InspectLevels, Launch,
            AwaitArena, InspectArena, Quit, AwaitMenu, Judge
        }

        static Phase _phase;
        static double _deadline;
        static double _startedAt;
        static readonly List<string> Errors = new List<string>();
        static readonly StringBuilder Notes = new StringBuilder();

        static int _catalogCount;
        static int _cardsShown;
        static int _cardsPlayable;
        static int _cardsOverlapping;
        static string _literalTag = "";
        static string _unclickable = "";
        static float _gridOverflow;
        static int _buttonsTested;
        static bool _dialogOpened;
        static bool _dialogClosed;
        static string _dialogUnclickable = "";
        static string _dialogQuestion = "";
        static string _launched = "";
        static int _levelsOffered;
        static int _levelsExpected;
        static int _levelsUnlocked;
        static bool _levelOneOpen;
        static bool _lockedIsInert = true;
        static string _levelUnclickable = "";
        static string _arenaScene = "";
        static string _strip = "";
        static bool _returned;
        static bool _resultShown;
        static int _runsBefore;
        static int _runsAfter;

        public static void VerifyFlow()
        {
            try
            {
                if (!System.IO.File.Exists(FPSKitMenuBuilder.MenuScenePath))
                    throw new Exception($"{FPSKitMenuBuilder.MenuScenePath} does not exist. " +
                                        "Run FPSKitBatch.BuildDashboard first.");

                EditorSceneManager.OpenScene(FPSKitMenuBuilder.MenuScenePath, OpenSceneMode.Single);

                Errors.Clear();
                Notes.Clear();
                _catalogCount = _cardsShown = _cardsPlayable = 0;
                _levelsOffered = _levelsExpected = _levelsUnlocked = 0;
                _levelOneOpen = false;
                _lockedIsInert = true;
                _levelUnclickable = "";
                _launched = _arenaScene = _strip = "";
                _returned = _resultShown = false;
                _dialogOpened = _dialogClosed = false;
                _dialogUnclickable = _dialogQuestion = "";
                _runsBefore = _runsAfter = -1;
                _startedAt = EditorApplication.timeSinceStartup;
                _phase = Phase.Enter;

                Application.logMessageReceived += OnGameLog;
                // The dashboard sets a play-mode start scene; this test needs the
                // one it just opened. Put back in Detach, including on failure.
                FPSKitPlayMode.SuspendStartScene();
                EditorApplication.update += Tick;

                Debug.Log("[FPSKitBatch] flow test: dashboard -> arena -> quit -> dashboard");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _startedAt > HardTimeout)
                    throw new Exception($"flow test exceeded {HardTimeout}s in phase {_phase}");

                switch (_phase)
                {
                    case Phase.Enter:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        _runsBefore = PlayerProfile.Runs;
                        Wait(1.5, Phase.InspectMenu);
                        return;

                    case Phase.InspectMenu:
                    {
                        if (Waiting()) return;

                        var menu = Menu();
                        if (menu == null) throw new Exception("the dashboard scene has no MainMenuController");
                        if (menu.catalog == null) throw new Exception("the dashboard has no arena catalog");

                        _catalogCount = menu.catalog.arenas.Count;

                        var placed = new List<Vector2>();

                        foreach (var card in UnityEngine.Object.FindObjectsByType<ArenaCard>(
                                     FindObjectsSortMode.None))
                        {
                            // The template is left in the scene switched off; only the
                            // clones the menu made are cards a player can see.
                            if (!card.gameObject.activeInHierarchy) continue;

                            _cardsShown++;
                            if (card.button != null && card.button.interactable) _cardsPlayable++;

                            // Six cards in one place is six cards the player cannot see
                            // five of, and it counts as six by every other measure here.
                            // It is what a hover animation writing anchoredPosition did
                            // to the layout group, and nothing else in this test noticed.
                            var at = ((RectTransform)card.transform).anchoredPosition;

                            foreach (var other in placed)
                                if (Vector2.Distance(at, other) < 1f) { _cardsOverlapping++; break; }

                            placed.Add(at);
                        }

                        _literalTag = LiteralTagIn(menu);
                        _unclickable = UnclickableButtonIn(menu);
                        _gridOverflow = GridOverflow(menu);

                        // Reported so a pass cannot be mistaken for a check that found
                        // nothing to look at.
                        _buttonsTested = menu.GetComponentsInChildren<UnityEngine.UI.Button>(false).Length;

                        Notes.Append($"\n  dashboard: {_catalogCount} in catalog, {_cardsShown} cards " +
                                     $"shown, {_cardsPlayable} playable, {_cardsOverlapping} stacked, " +
                                     $"{_buttonsTested} buttons raycast-tested, grid overflow " +
                                     $"{_gridOverflow:0}px");

                        _phase = Phase.OpenDialog;
                        return;
                    }

                    case Phase.OpenDialog:
                    {
                        var menu = Menu();

                        if (menu.exitConfirmPanel == null)
                            throw new Exception("the dashboard has no exit confirmation");

                        // Opened through the same call the button is wired to. Confirm is
                        // deliberately never clicked: it ends the application, which in
                        // the editor means stopping play mode out from under this test.
                        menu.AskToExit();
                        _dialogOpened = menu.exitConfirmPanel.activeSelf;

                        // Given a beat before anything is raycast at it. A graphic
                        // enabled this frame is not in the canvas yet, so a raycast fired
                        // immediately after finds nothing and reports a perfectly good
                        // button as unclickable.
                        Wait(0.5, Phase.ExitDialog);
                        return;
                    }

                    case Phase.ExitDialog:
                    {
                        if (Waiting()) return;

                        var menu = Menu();

                        foreach (var text in menu.exitConfirmPanel
                                     .GetComponentsInChildren<TMPro.TMP_Text>(true))
                        {
                            if (text.text.IndexOf("EXITING", StringComparison.OrdinalIgnoreCase) >= 0)
                                _dialogQuestion = Plain(text.text);
                        }

                        _dialogUnclickable = UnclickableButtonIn(menu);

                        menu.CancelExit();
                        _dialogClosed = !menu.exitConfirmPanel.activeSelf;

                        Notes.Append($"\n  exit dialog: opened={_dialogOpened} " +
                                     $"closed on cancel={_dialogClosed}, asks \"{_dialogQuestion}\"");

                        _phase = Phase.OpenLevels;
                        return;
                    }

                    case Phase.OpenLevels:
                    {
                        var menu = Menu();
                        var entry = FirstPlayable(menu);

                        if (entry == null) throw new Exception("no arena in the catalog can be loaded");

                        _launched = entry.sceneName;
                        _levelsExpected = entry.LevelCount;

                        if (menu.levelSelect == null)
                            throw new Exception("the dashboard has no level select, so an arena " +
                                                "card has no ladder to open");

                        // Through the same call the card is wired to, rather than by
                        // poking the panel: what is being tested is that clicking an
                        // arena opens its levels.
                        menu.Choose(entry);

                        // A frame before anything is raycast at it. A graphic enabled
                        // this frame is not in the canvas yet, so a raycast fired
                        // immediately reports a perfectly good tile as unclickable.
                        Wait(0.5, Phase.InspectLevels);
                        return;
                    }

                    case Phase.InspectLevels:
                    {
                        if (Waiting()) return;

                        var menu = Menu();
                        var select = menu.levelSelect;

                        if (!select.IsOpen)
                            throw new Exception($"clicking \"{_launched}\" did not open its levels");

                        foreach (var tile in UnityEngine.Object.FindObjectsByType<LevelButton>(
                                     FindObjectsSortMode.None))
                        {
                            if (!tile.gameObject.activeInHierarchy) continue;

                            _levelsOffered++;

                            if (tile.Unlocked)
                            {
                                _levelsUnlocked++;
                                if (tile.Index == 0) _levelOneOpen = true;
                            }
                            else if (tile.button != null && tile.button.interactable)
                            {
                                // A locked tile that still takes a click is a locked tile
                                // in name only, and the whole ladder is decoration.
                                _lockedIsInert = false;
                            }
                        }

                        _levelUnclickable = UnclickableButtonIn(menu);

                        Notes.Append($"\n  levels: {_levelsOffered} tiles for {_levelsExpected} in the " +
                                     $"set, {_levelsUnlocked} unlocked, level one open={_levelOneOpen}");

                        _phase = Phase.Launch;
                        return;
                    }

                    case Phase.Launch:
                    {
                        var menu = Menu();
                        var entry = FirstPlayable(menu);

                        Notes.Append($"\n  launching \"{entry.Label}\" ({entry.sceneName}) at level 1");

                        menu.Launch(entry, 0);
                        Wait(12.0, Phase.AwaitArena);
                        return;
                    }

                    case Phase.AwaitArena:
                    {
                        var active = SceneManager.GetActiveScene();
                        if (active.name != _launched)
                        {
                            if (Waiting()) return;
                            throw new Exception($"the dashboard never loaded \"{_launched}\" " +
                                                $"(still in \"{active.name}\")");
                        }

                        _arenaScene = active.name;

                        // Let the arena settle: the level manager, HUD and player all wire
                        // themselves up over the first few frames.
                        Wait(3.0, Phase.InspectArena);
                        return;
                    }

                    case Phase.InspectArena:
                    {
                        if (Waiting()) return;

                        var hud = UnityEngine.Object.FindAnyObjectByType<HUDController>();
                        if (hud == null) throw new Exception("the arena has no HUD");

                        _strip = hud.instructionText != null ? hud.instructionText.text : "";
                        Notes.Append($"\n  arena loaded, instruction strip reads: \"{Plain(_strip)}\"");

                        _phase = Phase.Quit;
                        return;
                    }

                    case Phase.Quit:
                    {
                        var director = GameDirector.Instance;
                        if (director == null) throw new Exception("the arena has no GameDirector");

                        // The same call the quit key makes. Driving the key itself would
                        // mean faking input; this tests the thing the key is wired to.
                        director.ReturnToMenu();
                        Wait(12.0, Phase.AwaitMenu);
                        return;
                    }

                    case Phase.AwaitMenu:
                    {
                        var active = SceneManager.GetActiveScene();
                        if (active.name != "Menu")
                        {
                            if (Waiting()) return;
                            throw new Exception($"quitting did not return to the dashboard " +
                                                $"(still in \"{active.name}\")");
                        }

                        _returned = true;

                        var menu = Menu();
                        if (menu != null && menu.lastRunPanel != null)
                            _resultShown = menu.lastRunPanel.activeSelf;

                        _runsAfter = PlayerProfile.Runs;

                        _phase = Phase.Judge;
                        return;
                    }

                    case Phase.Judge:
                        if (EditorApplication.isPlaying) { EditorApplication.ExitPlaymode(); return; }
                        Finish();
                        return;
                }
            }
            catch (Exception e)
            {
                Detach();
                Debug.LogError($"[FPSKitBatch] FAILED: {e}{Notes}");
                EditorApplication.Exit(1);
            }
        }

        // ==================================================================
        static MainMenuController Menu() => UnityEngine.Object.FindAnyObjectByType<MainMenuController>();

        /// <summary>
        /// Finds a closing rich-text tag that TMP does not have, drawn as literal text.
        ///
        /// &lt;alpha=#99&gt; has no closing form, so "&lt;/alpha&gt;" is not markup -- it is eight
        /// characters printed on the screen. It is invisible to every structural check
        /// because the text still contains the words it is supposed to, which is exactly
        /// why it survived into a screenshot.
        /// </summary>
        static string LiteralTagIn(MainMenuController menu)
        {
            foreach (var text in menu.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (text == null || string.IsNullOrEmpty(text.text)) continue;

                foreach (string bad in new[] { "</alpha>", "</size=", "</pos>", "</space>" })
                    if (text.text.Contains(bad)) return $"{text.name}: {bad}";
            }

            return "";
        }

        static ArenaCatalog.Entry FirstPlayable(MainMenuController menu)
        {
            if (menu == null || menu.catalog == null) return null;

            foreach (var entry in menu.catalog.arenas)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.sceneName)) continue;
                if (Application.CanStreamedLevelBeLoaded(entry.sceneName)) return entry;
            }

            return null;
        }

        /// <summary>
        /// Finds a Button the pointer cannot actually reach.
        ///
        /// Not "is a listener attached" -- that was true of the Exit button the whole
        /// time it did nothing. Its graphic had raycastTarget off, so the raycaster never
        /// saw it and no click was ever delivered. The only honest test is to fire a
        /// raycast at where the button is and see whether it comes back.
        /// </summary>
        /// <summary>Internal so FPSKitStoreTest can raycast the store the same way.</summary>
        internal static string UnclickableButtonIn(MainMenuController menu)
        {
            var raycaster = menu.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (raycaster == null) return "the dashboard canvas has no GraphicRaycaster";

            var events = UnityEngine.EventSystems.EventSystem.current;
            if (events == null) return "the dashboard scene has no EventSystem";

            foreach (var button in menu.GetComponentsInChildren<UnityEngine.UI.Button>(false))
            {
                if (button == null || !button.isActiveAndEnabled) continue;

                var rect = (RectTransform)button.transform;

                // The centre of the rect, not rect.position -- that is the pivot, and a
                // button anchored into a corner has its pivot on its own boundary, where
                // a raycast lands on the edge and may hit nothing at all. Every card has
                // a centred pivot, so this only ever misfired on the one button that did
                // not, which is exactly the button being checked.
                Vector3 centre = rect.TransformPoint(rect.rect.center);

                var pointer = new UnityEngine.EventSystems.PointerEventData(events)
                {
                    position = RectTransformUtility.WorldToScreenPoint(null, centre)
                };

                var hits = new List<UnityEngine.EventSystems.RaycastResult>();
                raycaster.Raycast(pointer, hits);

                bool reached = false;
                foreach (var hit in hits)
                {
                    if (hit.gameObject == null) continue;
                    if (hit.gameObject.transform.IsChildOf(rect)) { reached = true; break; }
                }

                if (!reached) return button.name;
            }

            return "";
        }

        /// <summary>
        /// How far the grid's content spills past the bottom of the space it was given.
        ///
        /// A GridLayoutGroup neither clips nor scrolls: content that does not fit is
        /// simply drawn past the edge, which is what cut the descriptions off the bottom
        /// row. Positive means overflowing.
        /// </summary>
        static float GridOverflow(MainMenuController menu)
        {
            if (menu.cardParent == null) return 0f;

            var layout = menu.cardParent.GetComponent<UnityEngine.UI.GridLayoutGroup>();
            if (layout == null) return 0f;

            int columns = Mathf.Max(1, layout.constraintCount);
            int cards = 0;

            foreach (var card in UnityEngine.Object.FindObjectsByType<ArenaCard>(FindObjectsSortMode.None))
                if (card.gameObject.activeInHierarchy) cards++;

            int rows = Mathf.Max(1, Mathf.CeilToInt(cards / (float)columns));

            float needed = layout.padding.top + layout.padding.bottom
                         + layout.cellSize.y * rows
                         + layout.spacing.y * (rows - 1);

            return needed - menu.cardParent.rect.height;
        }

        /// <summary>The text exactly as TMP was handed it, tags and all.</summary>
        static string Raw(string text) => text ?? "";

        /// <summary>Strips the rich-text tags so the note reads as what a player sees.</summary>
        static string Plain(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var clean = new StringBuilder(text.Length);
            bool inTag = false;

            foreach (char c in text)
            {
                if (c == '<') inTag = true;
                else if (c == '>') inTag = false;
                else if (!inTag) clean.Append(c);
            }

            return clean.ToString().Trim();
        }

        static void Wait(double seconds, Phase next)
        {
            _deadline = EditorApplication.timeSinceStartup + seconds;
            _phase = next;
        }

        static bool Waiting() => EditorApplication.timeSinceStartup < _deadline;

        static void OnGameLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Errors.Add($"[{type}] {message.Trim()}");
        }

        static void Detach()
        {
            FPSKitPlayMode.RestoreStartScene();

            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnGameLog;
        }

        static void Finish()
        {
            Detach();

            var problems = new StringBuilder();
            foreach (var error in Errors) problems.Append($"\n  - {error}");

            if (_cardsShown != _catalogCount)
                problems.Append($"\n  - the dashboard shows {_cardsShown} cards for {_catalogCount} " +
                                "arenas in the catalog");

            if (_cardsPlayable != _catalogCount)
                problems.Append($"\n  - only {_cardsPlayable} of {_catalogCount} cards can be clicked; " +
                                "an arena is missing from Build Settings");

            if (!_dialogOpened)
                problems.Append("\n  - the Exit button did not open a confirmation");

            if (!_dialogClosed)
                problems.Append("\n  - cancelling the exit dialog did not close it");

            if (string.IsNullOrWhiteSpace(_dialogQuestion))
                problems.Append("\n  - the exit dialog never asks the question");

            if (!string.IsNullOrEmpty(_dialogUnclickable))
                problems.Append($"\n  - the exit dialog's \"{_dialogUnclickable}\" button cannot be " +
                                "clicked: a raycast at it hits nothing");

            if (_levelsExpected > 0 && _levelsOffered != _levelsExpected)
                problems.Append($"\n  - the level select shows {_levelsOffered} tiles for " +
                                $"{_levelsExpected} levels in the set");

            if (_levelsExpected > 0 && !_levelOneOpen)
                problems.Append("\n  - level one is locked, so the arena cannot be started at all");

            if (!_lockedIsInert)
                problems.Append("\n  - a locked level can still be clicked: the unlock chain is " +
                                "decoration");

            if (!string.IsNullOrEmpty(_levelUnclickable))
                problems.Append($"\n  - the level select's \"{_levelUnclickable}\" button cannot be " +
                                "clicked: a raycast at it hits nothing");

            if (!string.IsNullOrEmpty(_unclickable))
                problems.Append($"\n  - the \"{_unclickable}\" button cannot be clicked: a raycast at " +
                                "it hits nothing, so no listener on it will ever run");

            if (_gridOverflow > 1f)
                problems.Append($"\n  - the arena grid overflows the space it was given by " +
                                $"{_gridOverflow:0}px, so the bottom row is cut off");

            if (_cardsOverlapping > 0)
                problems.Append($"\n  - {_cardsOverlapping} card(s) sit on top of another: the grid " +
                                "is stacked, so only the last one drawn is visible");

            if (!string.IsNullOrEmpty(_literalTag))
                problems.Append($"\n  - a closing tag TMP has no form for is being drawn as text " +
                                $"({_literalTag})");

            string arenaStrip = Raw(_strip);
            if (arenaStrip.Contains("</alpha>"))
                problems.Append("\n  - the instruction strip prints a literal </alpha> on screen");

            if (string.IsNullOrWhiteSpace(_arenaScene))
                problems.Append("\n  - the dashboard never loaded an arena");

            string strip = Plain(_strip);
            foreach (string word in new[] { "PAUSE", "RESUME", "QUIT" })
                if (strip.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0)
                    problems.Append($"\n  - the instruction strip never mentions {word}: \"{strip}\"");

            if (!_returned)
                problems.Append("\n  - quitting the run did not return to the dashboard");

            if (!_resultShown)
                problems.Append("\n  - the dashboard did not show the result of the level just played");

            if (_runsBefore >= 0 && _runsAfter <= _runsBefore)
                problems.Append($"\n  - the run was not recorded in the profile " +
                                $"(runs {_runsBefore} -> {_runsAfter})");

            if (problems.Length > 0)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: the dashboard loop is broken:{problems}\n{Notes}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[FPSKitBatch] verify flow passed: {_cardsShown} arenas offered, " +
                      $"\"{_arenaScene}\" offered {_levelsOffered} levels with {_levelsUnlocked} " +
                      $"unlocked, level one loaded and played, and quitting returned to the " +
                      $"dashboard with the attempt recorded ({_runsBefore} -> {_runsAfter}).{Notes}");
            EditorApplication.Exit(0);
        }
    }
}
#endif
