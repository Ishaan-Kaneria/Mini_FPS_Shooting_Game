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
    /// Plays one level to both of its endings and checks that each one is scored the way
    /// it is supposed to be.
    ///
    /// Two attempts, back to back in one play session:
    ///
    ///   1. A failure. Every enemy is dropped through the floor the moment the level
    ///      fills, which is the state an imported level produces almost immediately --
    ///      a gap in the geometry swallows a body and it falls forever without dying.
    ///      The leash has to discard them and put replacements in the queue, and the
    ///      clock has to end the level regardless. Nothing about a level may wait on an
    ///      enemy that is never coming: that is a softlock, and it was the failure the
    ///      wave spawner this replaced was built to avoid, and the levels inherited it.
    ///
    ///   2. A pass. The level is cleared outright, and has to award three stars, unlock
    ///      the next level and leave the first one unlocked.
    ///
    /// The second attempt is the one that proves the ladder works at all, and the first
    /// proves it cannot be jammed. The player's own stars are backed up and put back, so
    /// running this does not hand whoever is at the machine a level they did not beat.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyLevels
    /// </summary>
    public static class FPSKitLevelTest
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";
        const double HardTimeout = 420.0;

        /// <summary>
        /// The live level is retuned to these the moment play starts, so the test
        /// measures the mechanism rather than sitting through a full-length level.
        /// </summary>
        const float TestClock = 22f;
        const float TestGrace = 1.5f;
        const float TestBriefing = 1f;
        const int TestEnemies = 6;

        enum Phase
        {
            Enter, TuneFail, AwaitFill, Strand, AwaitDiscard, AwaitFailure,
            Reload, TunePass, AwaitPassFill, Clear, AwaitPass, Judge
        }

        static Phase _phase;
        static double _deadline;
        static double _startedAt;
        static readonly List<string> Errors = new List<string>();
        static readonly StringBuilder Notes = new StringBuilder();

        /// <summary>
        /// The exact bodies the first attempt dropped. Asserting on a population count
        /// instead would be wrong: the level is still arriving while the grace period
        /// runs, and the leash is putting replacements in behind them, so the number
        /// alive legitimately goes up even as the stranded ones are removed.
        /// </summary>
        static readonly List<GameObject> Stranded = new List<GameObject>();

        static double _populationDeadline = -1.0;

        static string _arena = "";
        static int _strandedCount;
        static int _strandedStillAlive = -1;
        static int _discarded = -1;

        static bool _failureScored;
        static LevelResult _failure;

        static bool _passScored;
        static LevelResult _pass;

        static int _magazineAtLevelOne = -1;
        static int _starsAfterFail = -1;
        static int _starsAfterPass = -1;
        static bool _nextUnlockedAfterFail;
        static bool _nextUnlockedAfterPass;

        /// <summary>The player's own progress, put back in Detach however this ends.</summary>
        static int _starsBackup, _scoreBackup, _nextStarsBackup, _runsBackup, _killsBackup;

        static string StarsKey(int index) => $"FPSKit.Level.{_arena}.{index}.Stars";
        static string ScoreKey(int index) => $"FPSKit.Level.{_arena}.{index}.Score";

        // ==================================================================
        public static void VerifyLevels()
        {
            try
            {
                if (!System.IO.File.Exists(ScenePath))
                    throw new Exception($"{ScenePath} does not exist. Build it first.");

                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                Errors.Clear();
                Notes.Clear();
                Stranded.Clear();

                _populationDeadline = -1.0;
                _arena = "IndustrialWarehouse";
                _strandedCount = 0;
                _strandedStillAlive = _discarded = -1;
                _failureScored = _passScored = false;
                _magazineAtLevelOne = -1;
                _starsAfterFail = _starsAfterPass = -1;
                _nextUnlockedAfterFail = _nextUnlockedAfterPass = false;

                BackUpProgress();

                // Cleared so the first attempt starts from a locked ladder, whatever the
                // person at this machine has already beaten. Without it, "the next level
                // is unlocked" would pass on a profile where it already was.
                PlayerPrefs.DeleteKey(StarsKey(0));
                PlayerPrefs.DeleteKey(StarsKey(1));
                PlayerPrefs.Save();

                _startedAt = EditorApplication.timeSinceStartup;
                _phase = Phase.Enter;

                Application.logMessageReceived += OnGameLog;

                // The dashboard sets a play-mode start scene; this test needs the
                // one it just opened. Put back in Detach, including on failure.
                FPSKitPlayMode.SuspendStartScene();
                EditorApplication.update += Tick;

                Debug.Log("[FPSKitBatch] level test: a level that cannot be cleared must still be " +
                          "scored, and one that is cleared must pay three stars");
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
                    throw new Exception($"level test exceeded {HardTimeout}s in phase {_phase}");

                switch (_phase)
                {
                    case Phase.Enter:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        _phase = Phase.TuneFail;
                        return;

                    case Phase.TuneFail:
                    {
                        var level = Level();
                        if (level == null || level.Level == null) return;

                        Tune(level);

                        _arena = level.Arena;

                        var gun = Weapon();
                        _magazineAtLevelOne = gun != null ? gun.MagazineSize : -1;

                        level.LevelFinished += OnFailureScored;

                        Notes.Append($"\n  attempt 1 (failure): \"{level.LevelName}\" in {_arena}, " +
                                     $"{level.TotalEnemies} to kill, {TestClock}s clock, " +
                                     $"magazine {_magazineAtLevelOne}");

                        _phase = Phase.AwaitFill;
                        return;
                    }

                    case Phase.AwaitFill:
                    {
                        var level = Level();
                        if (level == null || !level.IsRunning) return;

                        int alive = CountEnemies();
                        if (alive == 0) return;

                        // Let the level finish arriving so this strands a crowd rather
                        // than whichever body happened through the door first.
                        if (_populationDeadline < 0.0)
                            _populationDeadline = EditorApplication.timeSinceStartup + 8.0;

                        if (alive < 3 && EditorApplication.timeSinceStartup < _populationDeadline) return;

                        _phase = Phase.Strand;
                        return;
                    }

                    case Phase.Strand:
                    {
                        _strandedCount = StrandEveryEnemy();
                        Notes.Append($"\n  dropped {_strandedCount} enemy(s) through the floor");

                        if (_strandedCount == 0)
                            throw new Exception("no enemies were alive to strand; the test proved nothing");

                        Wait(TestGrace + 3f, Phase.AwaitDiscard);
                        return;
                    }

                    case Phase.AwaitDiscard:
                    {
                        if (Waiting()) return;

                        var level = Level();
                        _discarded = level != null ? level.EnemiesDiscarded : -1;

                        _strandedStillAlive = 0;
                        foreach (var body in Stranded)
                            if (body != null) _strandedStillAlive++;

                        Notes.Append($"\n  after the grace period: {_strandedStillAlive} of the " +
                                     $"{_strandedCount} stranded still present, {_discarded} discarded " +
                                     $"by the leash ({CountEnemies()} in the level, replacements " +
                                     "were queued)");

                        // Generous: the whole clock plus slack.
                        Wait(TestClock + 12f, Phase.AwaitFailure);
                        return;
                    }

                    case Phase.AwaitFailure:
                    {
                        if (!_failureScored)
                        {
                            if (Waiting()) return;
                            throw new Exception("the level never ended: an enemy that fell out of " +
                                                "the world stalled it, which is the softlock this " +
                                                "test exists to catch");
                        }

                        _starsAfterFail = LevelProgress.StarsIn(_arena, 0);
                        _nextUnlockedAfterFail = LevelProgress.IsUnlocked(_arena, 1);

                        Notes.Append($"\n  attempt 1 scored: {_failure.Title} - {_failure.stars} star(s), " +
                                     $"{_failure.killed}/{_failure.total} killed, stored stars " +
                                     $"{_starsAfterFail}, level 2 unlocked={_nextUnlockedAfterFail}");

                        _phase = Phase.Reload;
                        return;
                    }

                    // ------------------------------------------------ attempt 2
                    case Phase.Reload:
                    {
                        Debug.Log("[FPSKitBatch] level test: attempt 2 of 2 (cleared outright)");

                        var director = GameDirector.Instance;
                        if (director == null) throw new Exception("the arena has no GameDirector");

                        // The same call the results screen's Replay button makes.
                        GameSession.ChooseLevel(0);
                        director.Restart();

                        Wait(3.0, Phase.TunePass);
                        return;
                    }

                    case Phase.TunePass:
                    {
                        if (Waiting()) return;

                        var level = Level();
                        if (level == null || level.Level == null) return;

                        Tune(level);
                        level.LevelFinished += OnPassScored;

                        Notes.Append($"\n  attempt 2 (pass): {level.TotalEnemies} to kill");

                        _phase = Phase.AwaitPassFill;
                        return;
                    }

                    case Phase.AwaitPassFill:
                    {
                        var level = Level();
                        if (level == null || !level.IsRunning) return;

                        _phase = Phase.Clear;
                        return;
                    }

                    case Phase.Clear:
                    {
                        var level = Level();
                        if (level == null) return;

                        // Killed rather than shot. Aiming is FPSKitCombatTest's job; what
                        // is being proved here is that a level which is emptied ends as a
                        // clear and pays for it. Held down rather than fired once,
                        // because the level is still arriving: the last body spawns
                        // several seconds after the first.
                        level.KillAllEnemies();

                        if (level.IsFinished) { _phase = Phase.AwaitPass; return; }

                        // The clock is the failure mode here: if the kills are not
                        // landing, the level times out and the assertions below say so.
                        return;
                    }

                    case Phase.AwaitPass:
                    {
                        if (!_passScored) return;

                        _starsAfterPass = LevelProgress.StarsIn(_arena, 0);
                        _nextUnlockedAfterPass = LevelProgress.IsUnlocked(_arena, 1);

                        Notes.Append($"\n  attempt 2 scored: {_pass.Title} - {_pass.stars} star(s), " +
                                     $"{_pass.killed}/{_pass.total} killed with " +
                                     $"{_pass.TimeRemaining:0}s left, stored stars {_starsAfterPass}, " +
                                     $"level 2 unlocked={_nextUnlockedAfterPass}");

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

        /// <summary>
        /// Shortens the live level so the test measures the mechanism rather than
        /// sitting through a full-length fight. Written during the briefing, before the
        /// clock is read, so the shortened one is the one that applies.
        ///
        /// It writes to the LevelSet asset's own object, which is shared -- so the
        /// numbers are restored in Detach. A test that permanently shrank level one of
        /// the warehouse to six enemies would be a test that broke the game to pass.
        /// </summary>
        static void Tune(LevelManager manager)
        {
            var level = manager.Level;

            RememberLevel(level);

            level.enemyCount = TestEnemies;
            level.timeLimit = TestClock;
            level.briefingTime = TestBriefing;
            level.spawnInterval = 0.15f;
            level.maxAliveAtOnce = TestEnemies;
            level.hasBoss = false;

            manager.despawnGraceTime = TestGrace;

            // Re-read, because the manager cached the level's numbers when it resolved it.
            manager.ResolveLevel();
        }

        static LevelSet.Level _levelBackup;
        static LevelSet.Level _tunedLevel;

        static void RememberLevel(LevelSet.Level level)
        {
            if (_levelBackup != null) return;

            _tunedLevel = level;
            _levelBackup = new LevelSet.Level
            {
                enemyCount = level.enemyCount,
                timeLimit = level.timeLimit,
                briefingTime = level.briefingTime,
                spawnInterval = level.spawnInterval,
                maxAliveAtOnce = level.maxAliveAtOnce,
                hasBoss = level.hasBoss
            };
        }

        static void RestoreLevel()
        {
            if (_levelBackup == null || _tunedLevel == null) return;

            _tunedLevel.enemyCount = _levelBackup.enemyCount;
            _tunedLevel.timeLimit = _levelBackup.timeLimit;
            _tunedLevel.briefingTime = _levelBackup.briefingTime;
            _tunedLevel.spawnInterval = _levelBackup.spawnInterval;
            _tunedLevel.maxAliveAtOnce = _levelBackup.maxAliveAtOnce;
            _tunedLevel.hasBoss = _levelBackup.hasBoss;

            _levelBackup = null;
            _tunedLevel = null;
        }

        static void OnFailureScored(LevelResult result)
        {
            _failureScored = true;
            _failure = result;
        }

        static void OnPassScored(LevelResult result)
        {
            _passScored = true;
            _pass = result;
        }

        /// <summary>
        /// Drops every live enemy far below the player with its agent switched off --
        /// the same state a body is in after it slips through a hole in the level.
        /// </summary>
        static int StrandEveryEnemy()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return 0;

            var enemies = GameObject.FindGameObjectsWithTag("Enemy");
            var level = Level();
            float depth = level != null ? level.fallKillDepth + 25f : 85f;

            Stranded.Clear();

            foreach (var enemy in enemies)
            {
                if (enemy == null) continue;

                // The agent has to go first or it snaps the body back to the NavMesh.
                var agent = enemy.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null && agent.enabled) agent.enabled = false;

                enemy.transform.position = player.transform.position + Vector3.down * depth;
                Stranded.Add(enemy);
            }

            return Stranded.Count;
        }

        static LevelManager Level() => UnityEngine.Object.FindAnyObjectByType<LevelManager>();

        static Weapon Weapon() => UnityEngine.Object.FindAnyObjectByType<Weapon>();

        static int CountEnemies() => GameObject.FindGameObjectsWithTag("Enemy").Length;

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

        // ==================================================================
        static void BackUpProgress()
        {
            _starsBackup = PlayerPrefs.GetInt(StarsKey(0), 0);
            _scoreBackup = PlayerPrefs.GetInt(ScoreKey(0), 0);
            _nextStarsBackup = PlayerPrefs.GetInt(StarsKey(1), 0);
            _runsBackup = PlayerPrefs.GetInt("FPSKit.Runs", 0);
            _killsBackup = PlayerPrefs.GetInt("FPSKit.TotalKills", 0);
        }

        static void RestoreProgress()
        {
            PlayerPrefs.SetInt(StarsKey(0), _starsBackup);
            PlayerPrefs.SetInt(ScoreKey(0), _scoreBackup);
            PlayerPrefs.SetInt(StarsKey(1), _nextStarsBackup);
            PlayerPrefs.SetInt("FPSKit.Runs", _runsBackup);
            PlayerPrefs.SetInt("FPSKit.TotalKills", _killsBackup);
            PlayerPrefs.Save();
        }

        static void Detach()
        {
            RestoreLevel();
            RestoreProgress();
            FPSKitPlayMode.RestoreStartScene();

            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnGameLog;
        }

        static void Finish()
        {
            Detach();

            var problems = new StringBuilder();
            foreach (var error in Errors) problems.Append($"\n  - {error}");

            // ---- the failure ----
            if (_discarded <= 0)
                problems.Append("\n  - the leash discarded nothing: an enemy that fell out of the " +
                                "level is still being counted as alive");

            if (_strandedStillAlive != 0)
                problems.Append($"\n  - {_strandedStillAlive} of {_strandedCount} stranded enemies are " +
                                "still in the level after the grace period");

            if (_failure.stars != 0)
                problems.Append($"\n  - the failed attempt was awarded {_failure.stars} star(s) for " +
                                $"{_failure.killed}/{_failure.total} kills");

            if (_starsAfterFail != 0)
                problems.Append($"\n  - a failed level stored {_starsAfterFail} star(s)");

            if (_nextUnlockedAfterFail)
                problems.Append("\n  - level 2 was unlocked by a failed attempt, so the ladder can " +
                                "be climbed without beating anything");

            // ---- the pass ----
            if (_pass.ending != LevelResult.Ending.Cleared)
                problems.Append($"\n  - clearing the arena did not end the level as cleared " +
                                $"({_pass.ending}, {_pass.killed}/{_pass.total} killed)");

            if (_pass.stars != 3)
                problems.Append($"\n  - clearing the level awarded {_pass.stars} star(s) rather than 3");

            if (_starsAfterPass != 3)
                problems.Append($"\n  - a cleared level stored {_starsAfterPass} star(s) rather than 3");

            if (!_nextUnlockedAfterPass)
                problems.Append("\n  - level 2 is still locked after level 1 was cleared, so the " +
                                "ladder cannot be climbed at all");

            if (problems.Length > 0)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: the level loop is broken:{problems}\n{Notes}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[FPSKitBatch] verify levels passed: every enemy was dropped out of the level " +
                      $"and it was still scored ({_failure.stars} stars, level 2 locked), then the " +
                      $"level was cleared for {_pass.stars} stars and level 2 unlocked.{Notes}");
            EditorApplication.Exit(0);
        }
    }
}
#endif
