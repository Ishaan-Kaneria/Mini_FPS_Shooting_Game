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
    /// Proves a wave cannot be stalled by an enemy that will never reach you.
    ///
    /// This is the regression test for the failure an imported level produces almost
    /// immediately: a gap in the geometry swallows an enemy, it falls forever without
    /// ever dying, and a round that waits for a total wipe waits for it forever.
    ///
    /// It plays for real, waits for wave one to arrive, drops every enemy through the
    /// floor, and then checks two things: that the leash discards them, and that the
    /// round reaches wave two anyway -- with the kill count still at zero, so there is
    /// no chance the wave simply got cleared by accident.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyWaves
    /// </summary>
    public static class FPSKitWaveTest
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";
        const double HardTimeout = 240.0;

        // The live wave manager is retuned to these the moment play starts, so the test
        // measures the mechanism rather than sitting through a full-length wave.
        const float TestWaveClock = 10f;
        const float TestGrace = 1.5f;
        const float TestIntermission = 2f;

        enum Phase { Enter, Tune, AwaitWave, Strand, AwaitDiscard, AwaitNextWave, Judge }

        static Phase _phase;
        static double _deadline;
        static double _startedAt;
        static readonly List<string> Errors = new List<string>();
        static readonly StringBuilder Notes = new StringBuilder();

        /// <summary>
        /// The exact bodies this test dropped. Asserting on a population count instead
        /// would be wrong: the wave is still arriving while the grace period runs, so
        /// the number alive legitimately goes up even as the stranded ones are removed.
        /// </summary>
        static readonly List<GameObject> Stranded = new List<GameObject>();

        static double _populationDeadline = -1.0;
        static int _waveWhenStranded;
        static int _strandedCount;
        static int _strandedStillAlive = -1;
        static int _discardedAfter = -1;
        static int _aliveAfterDiscard = -1;
        static int _waveReached;
        static int _killsAtEnd = -1;

        /// <summary>The rifle's magazine before any wave was cleared, and after.</summary>
        static int _magazineAtStart = -1;
        static int _magazineAtEnd = -1;

        public static void VerifyWaves()
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
                _magazineAtStart = _magazineAtEnd = -1;
                _startedAt = EditorApplication.timeSinceStartup;
                _phase = Phase.Enter;

                Application.logMessageReceived += OnGameLog;
                // The dashboard sets a play-mode start scene; this test needs the
                // one it just opened. Put back in Detach, including on failure.
                FPSKitPlayMode.SuspendStartScene();
                EditorApplication.update += Tick;

                Debug.Log("[FPSKitBatch] wave test: a wave that cannot be cleared must still end");
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
                    throw new Exception($"wave test exceeded {HardTimeout}s in phase {_phase}");

                switch (_phase)
                {
                    case Phase.Enter:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        _phase = Phase.Tune;
                        return;

                    case Phase.Tune:
                    {
                        var wave = Wave();
                        if (wave == null) return;

                        // Retuned during the pre-wave countdown, before RunUntilWaveEnds
                        // reads these, so the shortened clock is the one that applies.
                        wave.waveTimeLimit = TestWaveClock;
                        wave.waveTimeLimitPerEnemy = 0f;
                        wave.despawnGraceTime = TestGrace;
                        wave.intermissionDuration = TestIntermission;
                        wave.clearLeftoversOnTimeout = true;

                        // Read before a wave has been cleared, so the growth asserted at
                        // the end is measured against the rifle the run started with
                        // rather than against the asset, which upgrades never touch.
                        var gun = Weapon();
                        _magazineAtStart = gun != null ? gun.MagazineSize : -1;

                        Notes.Append($"\n  tuned: clock {TestWaveClock}s, grace {TestGrace}s, " +
                                     $"magazine {_magazineAtStart}");
                        _phase = Phase.AwaitWave;
                        return;
                    }

                    case Phase.AwaitWave:
                    {
                        var wave = Wave();
                        if (wave == null || wave.CurrentWave < 1) return;

                        int alive = CountEnemies();
                        if (alive == 0) return;

                        // Let the wave finish arriving so this strands a crowd rather
                        // than whichever body happened through the door first.
                        if (_populationDeadline < 0.0)
                            _populationDeadline = EditorApplication.timeSinceStartup + 8.0;

                        if (alive < 3 && EditorApplication.timeSinceStartup < _populationDeadline) return;

                        _waveWhenStranded = wave.CurrentWave;
                        _phase = Phase.Strand;
                        return;
                    }

                    case Phase.Strand:
                    {
                        _strandedCount = StrandEveryEnemy();
                        Notes.Append($"\n  dropped {_strandedCount} enemy(s) through the floor " +
                                     $"during wave {_waveWhenStranded}");

                        if (_strandedCount == 0)
                            throw new Exception("no enemies were alive to strand; the test proved nothing");

                        Wait(TestGrace + 3f, Phase.AwaitDiscard);
                        return;
                    }

                    case Phase.AwaitDiscard:
                    {
                        if (Waiting()) return;

                        var wave = Wave();
                        _discardedAfter = wave != null ? wave.EnemiesDiscarded : -1;
                        _aliveAfterDiscard = CountEnemies();

                        _strandedStillAlive = 0;
                        foreach (var body in Stranded)
                            if (body != null) _strandedStillAlive++;

                        Notes.Append($"\n  after the grace period: {_strandedStillAlive} of the " +
                                     $"{_strandedCount} stranded still present, {_discardedAfter} " +
                                     $"discarded by the leash ({_aliveAfterDiscard} enemies in the " +
                                     "level, the wave kept arriving)");

                        // Generous: the clock plus the intermission plus slack.
                        Wait(TestWaveClock + TestIntermission + 12f, Phase.AwaitNextWave);
                        return;
                    }

                    case Phase.AwaitNextWave:
                    {
                        var wave = Wave();
                        if (wave == null) return;

                        _waveReached = wave.CurrentWave;

                        bool advanced = _waveReached > _waveWhenStranded;
                        if (!advanced && Waiting()) return;

                        var director = GameDirector.Instance;
                        _killsAtEnd = director != null ? director.Kills : -1;

                        var gun = Weapon();
                        _magazineAtEnd = gun != null ? gun.MagazineSize : -1;

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
        /// Drops every live enemy far below the player with its agent switched off --
        /// the same state a body is in after it slips through a hole in the level.
        /// </summary>
        static int StrandEveryEnemy()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return 0;

            var enemies = GameObject.FindGameObjectsWithTag("Enemy");
            var wave = Wave();
            float depth = wave != null ? wave.fallKillDepth + 25f : 85f;

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

        static WaveManager Wave() => UnityEngine.Object.FindAnyObjectByType<WaveManager>();

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

            if (_discardedAfter <= 0)
                problems.Append("\n  - the leash discarded nothing: an enemy that fell out of the " +
                                "level is still being counted as alive");

            if (_strandedStillAlive != 0)
                problems.Append($"\n  - {_strandedStillAlive} of {_strandedCount} stranded enemies are " +
                                "still in the level after the grace period");

            if (_waveReached <= _waveWhenStranded)
                problems.Append($"\n  - the round never got past wave {_waveWhenStranded}: a wave that " +
                                "cannot be cleared still stalls it");

            // Clearing a wave is supposed to pay for itself. Checked here because this is
            // the test that already survives one, and because an upgrade that silently
            // stops arriving looks exactly like a game that is simply getting harder.
            if (_magazineAtStart > 0 && _magazineAtEnd <= _magazineAtStart)
                problems.Append($"\n  - the rifle never grew across a cleared wave " +
                                $"(magazine {_magazineAtStart} -> {_magazineAtEnd})");

            if (_killsAtEnd > 0)
                problems.Append($"\n  - {_killsAtEnd} kill(s) were scored, so the wave may simply have " +
                                "been cleared rather than timing out; the test is inconclusive");

            if (problems.Length > 0)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: a stalled wave was not recovered:{problems}\n{Notes}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[FPSKitBatch] verify waves passed: every enemy was dropped out of the level " +
                      $"and the round still advanced from wave {_waveWhenStranded} to {_waveReached} " +
                      $"with {_killsAtEnd} kills, and the rifle's magazine grew from " +
                      $"{_magazineAtStart} to {_magazineAtEnd}.{Notes}");
            EditorApplication.Exit(0);
        }
    }
}
#endif
