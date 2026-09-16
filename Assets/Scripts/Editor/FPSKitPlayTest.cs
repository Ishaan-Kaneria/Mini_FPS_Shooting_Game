#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Plays the game three times over and checks that every run after the first is as
    /// alive as the first: a fresh session, then the in-game restart, then a second
    /// fresh session after play mode has been stopped and started again.
    ///
    /// It exists because "works once, dead the next time" is the one failure this
    /// project is structurally prone to and no static check can see. Enter Play Mode
    /// Options are on with domain reload disabled, so C# statics survive a play session;
    /// SceneManager.LoadScene tears the world down mid-run; and Time.timeScale, the
    /// cursor lock and the input gate are all global state that outlives a run.
    ///
    /// Each run is measured twice. The opening snapshot, taken a beat after the world
    /// starts, is where state carried over from the previous run shows up. The settled
    /// snapshot, taken once the level is underway, is where a run that started clean
    /// but cannot actually play shows up.
    ///
    /// Run one holds the trigger down and pops damage numbers first, so the pooled and
    /// cached state a real player generates is in play before anything is torn down.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyReplay
    /// </summary>
    public static class FPSKitPlayTest
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";

        /// <summary>Real seconds a run is given before it is measured. The briefing runs four.</summary>
        const double SettleSeconds = 8.0;

        /// <summary>Real seconds after a scene load before the new world is measured.</summary>
        const double OpeningSeconds = 1.0;

        /// <summary>Whole-run ceiling, so a wedged play mode fails instead of hanging.</summary>
        const double HardTimeout = 300.0;

        static readonly string[] RunNames = { "first run", "after in-game restart", "second play session" };

        enum Phase
        {
            EnterFirst, OpenFirst, SettleFirst,
            OpenRestarted, SettleRestarted,
            ExitFirst, EnterSecond, OpenSecond, SettleSecond,
            Judge
        }

        struct Snapshot
        {
            public bool valid;
            public bool inputEnabled;
            public float timeScale;
            public bool cursorLocked;
            public bool directorExists;
            public bool hudBound;
            public bool gameOver;
            public int score;
            public int level;
            public int enemiesAlive;
            public bool playerAlive;

            public override string ToString()
                => $"input={inputEnabled} timeScale={timeScale:0.##} cursorLocked={cursorLocked} " +
                   $"director={directorExists} hudBound={hudBound} gameOver={gameOver} " +
                   $"score={score} level={level} enemies={enemiesAlive} playerAlive={playerAlive}";
        }

        /// <summary>
        /// Errors the running game logged, tagged with the run they came from. A play
        /// session that throws in Awake still satisfies every state assertion below --
        /// the objects exist, they just never got wired up -- so without watching the
        /// console this test would pass over the exact failure it is meant to catch.
        /// </summary>
        static readonly List<string> Errors = new List<string>();

        static int _currentRun;
        static Phase _phase;
        static double _deadline;
        static double _startedAt;
        static readonly Snapshot[] Opening = new Snapshot[3];
        static readonly Snapshot[] Settled = new Snapshot[3];
        /// <summary>
        /// The player's own records, put back in Detach. This test finishes a level for
        /// real, so without it a CI run would award itself stars on level one of the
        /// warehouse and count three attempts against whoever is sitting at the machine.
        /// </summary>
        static int _bestScoreBackup, _runsBackup, _killsBackup, _starsBackup, _levelScoreBackup;

        const string StarsKey = "FPSKit.Level.IndustrialWarehouse.0.Stars";
        const string LevelScoreKey = "FPSKit.Level.IndustrialWarehouse.0.Score";

        public static void VerifyReplay()
        {
            try
            {
                if (!System.IO.File.Exists(ScenePath))
                    throw new Exception($"{ScenePath} does not exist. Build it first.");

                // The run records belong to the player, not to the test.
                _bestScoreBackup = PlayerPrefs.GetInt("FPSKit.BestScore", 0);
                _runsBackup = PlayerPrefs.GetInt("FPSKit.Runs", 0);
                _killsBackup = PlayerPrefs.GetInt("FPSKit.TotalKills", 0);
                _starsBackup = PlayerPrefs.GetInt(StarsKey, 0);
                _levelScoreBackup = PlayerPrefs.GetInt(LevelScoreKey, 0);

                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                _startedAt = EditorApplication.timeSinceStartup;
                _phase = Phase.EnterFirst;

                Errors.Clear();
                _currentRun = 0;

                Application.logMessageReceived += OnGameLog;
                // The dashboard sets a play-mode start scene; this test needs the
                // one it just opened. Put back in Detach, including on failure.
                FPSKitPlayMode.SuspendStartScene();
                EditorApplication.update += Tick;

                Debug.Log("[FPSKitBatch] replay test: run 1 of 3 (fresh play session)");
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
                    throw new Exception($"replay test exceeded {HardTimeout}s in phase {_phase}");

                switch (_phase)
                {
                    // ---------------------------------------------- run 1
                    case Phase.EnterFirst:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        Wait(OpeningSeconds, Phase.OpenFirst);
                        return;

                    case Phase.OpenFirst:
                        if (Waiting()) return;

                        Opening[0] = Capture();
                        SetFiring(true);              // play it like a player would
                        Wait(SettleSeconds, Phase.SettleFirst);
                        return;

                    case Phase.SettleFirst:
                        if (Waiting()) return;

                        SeedDamageNumbers();
                        Settled[0] = Capture();
                        SetFiring(false);

                        Debug.Log($"[FPSKitBatch] run 1 settled: {Settled[0]}");
                        Debug.Log("[FPSKitBatch] replay test: run 2 of 3 (in-game restart)");
                        _currentRun = 1;

                        // The in-game restart: a full scene teardown mid-run.
                        var director = GameDirector.Instance;
                        if (director != null) director.Restart();
                        Wait(OpeningSeconds, Phase.OpenRestarted);
                        return;

                    // ---------------------------------------------- run 2
                    case Phase.OpenRestarted:
                        if (Waiting()) return;

                        Opening[1] = Capture();
                        Wait(SettleSeconds, Phase.SettleRestarted);
                        return;

                    case Phase.SettleRestarted:
                        if (Waiting()) return;

                        Settled[1] = Capture();
                        Debug.Log($"[FPSKitBatch] run 2 settled: {Settled[1]}");

                        // Leave behind the messiest state a player can: a scored level
                        // freezes time, disables input and frees the cursor.
                        var ending = GameDirector.Instance;
                        if (ending != null) ending.ReportLevelFinished(DeathResult(Settled[1]));

                        _phase = Phase.ExitFirst;
                        return;

                    case Phase.ExitFirst:
                        if (EditorApplication.isPlaying) { EditorApplication.ExitPlaymode(); return; }

                        Debug.Log("[FPSKitBatch] replay test: run 3 of 3 (second play session)");
                        _currentRun = 2;
                        _phase = Phase.EnterSecond;
                        return;

                    // ---------------------------------------------- run 3
                    case Phase.EnterSecond:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        Wait(OpeningSeconds, Phase.OpenSecond);
                        return;

                    case Phase.OpenSecond:
                        if (Waiting()) return;

                        Opening[2] = Capture();
                        Wait(SettleSeconds, Phase.SettleSecond);
                        return;

                    case Phase.SettleSecond:
                        if (Waiting()) return;

                        Settled[2] = Capture();
                        Debug.Log($"[FPSKitBatch] run 3 settled: {Settled[2]}");

                        _phase = Phase.Judge;
                        return;

                    case Phase.Judge:
                        if (EditorApplication.isPlaying) { EditorApplication.ExitPlaymode(); return; }
                        Finish();
                        return;
                }
            }
            catch (Exception e)
            {
                Detach();
                RestorePrefs();

                Debug.LogError($"[FPSKitBatch] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        // ==================================================================
        static void OnGameLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;

            string where = _currentRun < RunNames.Length ? RunNames[_currentRun] : "unknown run";
            string firstFrame = FirstGameFrame(stackTrace);

            Errors.Add($"[{type}] during {where}: {message.Trim()}{firstFrame}");
        }

        /// <summary>Pulls the first line of the stack that points back into this project.</summary>
        static string FirstGameFrame(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace)) return "";

            foreach (var line in stackTrace.Split('\n'))
                if (line.Contains("Assets/Scripts/")) return $"\n        at {line.Trim()}";

            return "";
        }

        static void Detach()
        {
            FPSKitPlayMode.RestoreStartScene();

            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnGameLog;
        }

        static void Wait(double seconds, Phase next)
        {
            _deadline = EditorApplication.timeSinceStartup + seconds;
            _phase = next;
        }

        static bool Waiting() => EditorApplication.timeSinceStartup < _deadline;

        /// <summary>
        /// Holds the trigger through MobileInput, which Weapon reads directly. That is
        /// the only seam a test has into firing without synthesising keyboard events.
        /// </summary>
        static void SetFiring(bool on) => MobileInput.SetFireButton(on);

        /// <summary>Populates the damage number pool, so the teardown has something to get wrong.</summary>
        static void SeedDamageNumbers()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return;

            for (int i = 0; i < 8; i++)
                DamageNumber.Show(player.transform.position + player.transform.forward * 3f + Vector3.up,
                                  25f, Color.white);
        }

        static Snapshot Capture()
        {
            var director = GameDirector.Instance;
            var manager = UnityEngine.Object.FindAnyObjectByType<LevelManager>();
            var hud = UnityEngine.Object.FindAnyObjectByType<HUDController>();

            var player = GameObject.FindGameObjectWithTag("Player");
            var health = player != null ? player.GetComponent<Health>() : null;

            return new Snapshot
            {
                valid = true,
                inputEnabled = PlayerMotor.InputEnabled,
                timeScale = Time.timeScale,
                cursorLocked = Cursor.lockState == CursorLockMode.Locked,
                directorExists = director != null,
                hudBound = hud != null && hud.playerHealth != null && hud.weapon != null &&
                           hud.levelManager != null,
                gameOver = director != null && director.IsGameOver,
                score = director != null ? director.Score : -1,
                level = manager != null && (manager.IsRunning || manager.IsFinished)
                    ? manager.LevelNumber
                    : -1,
                enemiesAlive = GameObject.FindGameObjectsWithTag("Enemy").Length,
                playerAlive = health != null && !health.IsDead
            };
        }

        // ==================================================================
        static void Finish()
        {
            Detach();
            RestorePrefs();

            var problems = new StringBuilder();

            foreach (var error in Errors) problems.Append($"\n  - {error}");

            // Run 1 is the control. If it did not play, the comparison means nothing, and
            // the environment is judged by what run 1 managed rather than by absolutes --
            // batch mode cannot do everything an editor with a window can.
            if (!Settled[0].valid || Settled[0].level < 1)
                problems.Append("\n  - run 1 never started a level; there is no baseline to compare against");

            for (int i = 1; i < 3; i++) Judge(i, problems);

            var detail = new StringBuilder();
            for (int i = 0; i < 3; i++)
                detail.Append($"\n  {RunNames[i]}:\n      opening {Opening[i]}\n      settled {Settled[i]}");

            if (problems.Length > 0)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: a later run is not as alive as the first:" +
                               $"{problems}\n{detail}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[FPSKitBatch] verify replay passed: fresh run, in-game restart and a " +
                      $"second play session all ran clean.{detail}");
            EditorApplication.Exit(0);
        }

        static void Judge(int index, StringBuilder problems)
        {
            var opening = Opening[index];
            var settled = Settled[index];
            var control = Settled[0];
            string who = RunNames[index];

            if (!opening.valid || !settled.valid)
            {
                problems.Append($"\n  - {who}: never measured");
                return;
            }

            // --- opening: state that leaked in from the run before ---
            if (!opening.inputEnabled)
                problems.Append($"\n  - {who} opened with PlayerMotor.InputEnabled false: " +
                                "the player cannot move, look or shoot");

            if (!Mathf.Approximately(opening.timeScale, 1f))
                problems.Append($"\n  - {who} opened with Time.timeScale {opening.timeScale}: frozen");

            if (opening.gameOver)
                problems.Append($"\n  - {who} opened already in the game over state");

            if (opening.score != 0)
                problems.Append($"\n  - {who} opened with a score of {opening.score} carried over");

            if (!opening.directorExists)
                problems.Append($"\n  - {who} has no GameDirector: no score, pause or game over");

            // The cursor is judged against run 1 rather than against Locked, because a
            // headless editor may not be able to capture a cursor at all.
            if (control.cursorLocked && !opening.cursorLocked)
                problems.Append($"\n  - {who} opened with the cursor unlocked while run 1 had it " +
                                "locked: mouse look is dead");

            // --- settled: a run that started clean but cannot actually play ---
            if (!settled.hudBound)
                problems.Append($"\n  - {who} has a HUD that is not bound to the player, weapon " +
                                "and level manager");

            if (settled.level < 1)
                problems.Append($"\n  - {who} never started a level");

            if (control.enemiesAlive > 0 && settled.enemiesAlive < 1)
                problems.Append($"\n  - {who} spawned no enemies (run 1 had {control.enemiesAlive})");

            if (!settled.playerAlive)
                problems.Append($"\n  - {who} has no living player");

            if (!settled.inputEnabled)
                problems.Append($"\n  - {who} lost input while playing");
        }

        /// <summary>
        /// A losing result for the level that is running, so the second run is handed the
        /// frozen, input-disabled, cursor-free state a real death leaves behind. Zero
        /// stars on purpose: the test must not be able to unlock anything.
        /// </summary>
        static LevelResult DeathResult(Snapshot settled)
        {
            var manager = UnityEngine.Object.FindAnyObjectByType<LevelManager>();

            return new LevelResult
            {
                arena = manager != null ? manager.Arena : "",
                levelIndex = manager != null ? manager.LevelIndex : 0,
                levelName = manager != null ? manager.LevelName : "",
                ending = LevelResult.Ending.Died,
                stars = 0,
                killed = manager != null ? manager.Killed : 0,
                total = manager != null ? manager.TotalEnemies : Mathf.Max(1, settled.level),
                timeLimit = manager != null ? manager.TimeLimit : 0f
            };
        }

        static void RestorePrefs()
        {
            PlayerPrefs.SetInt("FPSKit.BestScore", _bestScoreBackup);
            PlayerPrefs.SetInt("FPSKit.Runs", _runsBackup);
            PlayerPrefs.SetInt("FPSKit.TotalKills", _killsBackup);
            PlayerPrefs.SetInt(StarsKey, _starsBackup);
            PlayerPrefs.SetInt(LevelScoreKey, _levelScoreBackup);
            PlayerPrefs.Save();
        }
    }
}
#endif
