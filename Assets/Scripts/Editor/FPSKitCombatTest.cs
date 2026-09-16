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
    /// Proves that the enemies of level one actually fight.
    ///
    /// This is the regression test for the failure that is hardest to see in a build
    /// check and most obvious to a player: enemies that arrive, gather round and then
    /// do nothing. It had a real cause. A NavMeshAgent brakes a full stoppingDistance
    /// short of its destination, and the AI aimed melee enemies at a point just inside
    /// their own attack range -- so they parked a stoppingDistance *outside* it, the
    /// range check never passed, and every enemy in the level stood a metre away from
    /// the player, facing them, forever. Nothing logged, nothing threw, every test
    /// passed.
    ///
    /// So the assertions here are behavioural rather than structural. It walks the
    /// player into the middle of level one and requires, within a few seconds, that
    /// enemies reach Attack and that the player's health actually falls. It also
    /// checks the things that make that fight readable: the level-one enemy is armed,
    /// its rifle is visible, and it is slower than the player.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyCombat
    /// </summary>
    public static class FPSKitCombatTest
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";
        const double HardTimeout = 260.0;

        /// <summary>Seconds the enemies are given to land a shot from their firing line.</summary>
        const double EngageWindow = 14.0;

        /// <summary>Seconds spent standing in an enemy's face, which is the real regression.</summary>
        const double CloseWindow = 9.0;

        /// <summary>How close the player is held during the point-blank phase.</summary>
        const float PointBlank = 1.6f;

        enum Phase { Enter, AwaitLevel, Inspect, Engage, CloseIn, Judge }

        static Phase _phase;
        static double _deadline;
        static double _startedAt;
        static readonly List<string> Errors = new List<string>();
        static readonly StringBuilder Notes = new StringBuilder();

        static int _inspected;
        static int _armed;
        static int _weaponVisible;
        static float _fastestEnemy;
        static float _playerWalkSpeed;
        // Health plus shield, not health alone. The player carries a 50-point shield
        // that soaks damage before health does (see Health and FPSKitSceneBuilder), so a
        // test watching Current reads 100 out of 100 through an entire firefight and
        // reports that nothing is hitting -- which is exactly what this one did first.
        static float _poolAtEngage = -1f;
        static float _poolAfterRanged = -1f;
        static float _poolBeforeClose = -1f;
        static float _poolAtEnd = -1f;
        static int _reachedAttack;
        static int _closeAttacks;
        static bool _sawRangedShot;

        // --- diagnostics, printed on failure so a red run says why ---------
        static float _minDistance = float.MaxValue;
        static int _framesWithSight;
        static int _framesSampled;
        static readonly Dictionary<EnemyAI.State, int> StateFrames = new Dictionary<EnemyAI.State, int>();
        static readonly Dictionary<string, int> RayHits = new Dictionary<string, int>();
        static int _tracersSeen;

        public static void VerifyCombat()
        {
            try
            {
                if (!System.IO.File.Exists(ScenePath))
                    throw new Exception($"{ScenePath} does not exist. Build it first.");

                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                Errors.Clear();
                Notes.Clear();
                _inspected = _armed = _weaponVisible = _reachedAttack = 0;
                _fastestEnemy = 0f;
                _minDistance = float.MaxValue;
                _framesWithSight = _framesSampled = 0;
                StateFrames.Clear();
                RayHits.Clear();
                _tracersSeen = 0;
                _poolAtEngage = _poolAfterRanged = _poolBeforeClose = _poolAtEnd = -1f;
                _closeAttacks = 0;
                _sawRangedShot = false;
                _startedAt = EditorApplication.timeSinceStartup;
                _phase = Phase.Enter;

                Application.logMessageReceived += OnGameLog;
                // The dashboard sets a play-mode start scene; this test needs the
                // one it just opened. Put back in Detach, including on failure.
                FPSKitPlayMode.SuspendStartScene();
                EditorApplication.update += Tick;

                Debug.Log("[FPSKitBatch] combat test: level one must arrive armed, slower than you, and fight");
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
                    throw new Exception($"combat test exceeded {HardTimeout}s in phase {_phase}");

                switch (_phase)
                {
                    case Phase.Enter:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        _phase = Phase.AwaitLevel;
                        return;

                    case Phase.AwaitLevel:
                    {
                        var level = Level();
                        if (level == null || !level.IsRunning) return;
                        if (Enemies().Count < 3) return;

                        _phase = Phase.Inspect;
                        return;
                    }

                    case Phase.Inspect:
                    {
                        Inspect();
                        Engage();

                        _poolAtEngage = Pool();
                        Notes.Append($"\n  player pool (health+shield) when the fight started: {_poolAtEngage:0.0}");

                        Wait(EngageWindow, Phase.Engage);
                        return;
                    }

                    case Phase.Engage:
                    {
                        // Watched every frame rather than sampled at the end: Attack is a
                        // state an enemy passes through, so a single reading at the close
                        // of the window would miss every swing that had already landed.
                        var player = GameObject.FindGameObjectWithTag("Player");
                        _framesSampled++;

                        foreach (var ai in Enemies())
                        {
                            if (ai.CurrentState == EnemyAI.State.Attack) _reachedAttack++;

                            StateFrames.TryGetValue(ai.CurrentState, out int seen);
                            StateFrames[ai.CurrentState] = seen + 1;

                            if (player == null) continue;

                            _minDistance = Mathf.Min(_minDistance,
                                Vector3.Distance(ai.transform.position, player.transform.position));

                            Vector3 eye = ai.eyes != null ? ai.eyes.position : ai.transform.position;
                            if (!Physics.Linecast(eye, player.transform.position + Vector3.up * 1.4f,
                                                  ai.sightBlockers, QueryTriggerInteraction.Ignore))
                                _framesWithSight++;

                            // The enemy's own shot, reproduced exactly: same origin, same
                            // aim point, same mask. Whatever this reports is what the
                            // bullet is actually meeting, which is what turned "no damage
                            // is landing" from a guess into a five-minute diagnosis.
                            // Sampled rather than run every frame -- it is a diagnostic,
                            // and a raycast per enemy per frame is not free.
                            if (_framesSampled % 12 != 0) continue;

                            Vector3 aim = ((player.transform.position + Vector3.up * 1.2f) - eye).normalized;
                            string label = Physics.Raycast(eye, aim, out RaycastHit probe,
                                                           ai.detectionRadius, ai.rangedHitMask,
                                                           QueryTriggerInteraction.Ignore)
                                ? $"{probe.collider.name}(layer {LayerMask.LayerToName(probe.collider.gameObject.layer)})"
                                : "NOTHING";

                            RayHits.TryGetValue(label, out int hits);
                            RayHits[label] = hits + 1;
                        }

                        _tracersSeen = Mathf.Max(_tracersSeen,
                            UnityEngine.Object.FindObjectsByType<TracerProjectile>(FindObjectsSortMode.None).Length);

                        _poolAfterRanged = Pool();

                        // Damage is the real assertion, so there is no reason to sit out
                        // the rest of the window once it has landed.
                        bool hurt = _poolAtEngage > 0f && _poolAfterRanged < _poolAtEngage;
                        if (!hurt && Waiting()) return;

                        _poolBeforeClose = _poolAfterRanged;
                        Notes.Append($"\n  after {EngageWindow}s at range: pool {_poolAtEngage:0.0} " +
                                     $"-> {_poolAfterRanged:0.0}");

                        Wait(CloseWindow, Phase.CloseIn);
                        return;
                    }

                    case Phase.CloseIn:
                    {
                        // The regression itself. The player is held inside an enemy's
                        // reach instead of being left at its firing line, because the
                        // symptom that started all this was a crowd standing a metre away
                        // doing nothing at all. An armed enemy is supposed to stop
                        // shooting and swing the rifle here.
                        HoldPointBlank();

                        foreach (var ai in Enemies())
                            if (ai.CurrentState == EnemyAI.State.Attack) _closeAttacks++;

                        _poolAtEnd = Pool();

                        bool hurt = _poolBeforeClose > 0f && _poolAtEnd < _poolBeforeClose;
                        if (hurt || !Waiting()) _phase = Phase.Judge;

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

        /// <summary>Records what level one actually turned up as, before anything is moved.</summary>
        static void Inspect()
        {
            var motor = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
            _playerWalkSpeed = motor != null ? motor.walkSpeed : 0f;

            foreach (var ai in Enemies())
            {
                _inspected++;
                if (ai.ranged) _armed++;

                if (ai.muzzlePoint != null && ai.muzzlePoint.gameObject.activeInHierarchy) _weaponVisible++;

                var agent = ai.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null) _fastestEnemy = Mathf.Max(_fastestEnemy, agent.speed);

                if (ai.ranged) _sawRangedShot = true;
            }

            Notes.Append($"\n  level one: {_inspected} enemies, {_armed} armed, " +
                         $"{_weaponVisible} with the rifle switched on");
            Notes.Append($"\n  fastest enemy {_fastestEnemy:0.00} m/s against a player walk of " +
                         $"{_playerWalkSpeed:0.00} m/s");
        }

        /// <summary>
        /// Puts the fight where it can be measured: the player is dropped into the
        /// middle of the crowd rather than the crowd being teleported onto the player,
        /// so every enemy keeps a valid agent and a real path and the test measures the
        /// AI rather than a pile of bodies wished into contact.
        /// </summary>
        static void Engage()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            var enemies = Enemies();
            if (player == null || enemies.Count == 0) return;

            Vector3 centre = Vector3.zero;
            foreach (var ai in enemies) centre += ai.transform.position;
            centre /= enemies.Count;

            // Just off the middle of the crowd, and lifted clear so the controller does
            // not start the frame inside the floor.
            var controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;

            player.transform.position = centre + Vector3.up * 1.1f;

            if (controller != null) controller.enabled = true;

            Notes.Append($"\n  player moved into the crowd at {centre}");
        }

        static LevelManager Level() => UnityEngine.Object.FindAnyObjectByType<LevelManager>();

        static Health PlayerHealth()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            return player != null ? player.GetComponent<Health>() : null;
        }

        /// <summary>Health plus shield. What "the player is being hurt" actually means here.</summary>
        static float Pool()
        {
            var health = PlayerHealth();
            return health != null ? health.Current + health.Shield : -1f;
        }

        /// <summary>
        /// Parks the player just inside the nearest enemy's reach, every frame.
        ///
        /// Held rather than set once, because a shooter is supposed to back away from
        /// someone closing on it -- so a single teleport would be answered by the enemy
        /// walking straight back out to its firing line, and the point-blank case would
        /// never be tested at all. Chasing it is also what a player does.
        /// </summary>
        static void HoldPointBlank()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return;

            EnemyAI nearest = null;
            float best = float.MaxValue;

            foreach (var ai in Enemies())
            {
                float distance = Vector3.Distance(ai.transform.position, player.transform.position);
                if (distance >= best) continue;

                best = distance;
                nearest = ai;
            }

            if (nearest == null) return;

            Vector3 offset = player.transform.position - nearest.transform.position;
            offset.y = 0f;
            offset = offset.sqrMagnitude > 0.01f ? offset.normalized : nearest.transform.forward;

            var controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;

            player.transform.position = nearest.transform.position + offset * PointBlank + Vector3.up * 1.1f;

            if (controller != null) controller.enabled = true;
        }

        static List<EnemyAI> Enemies()
        {
            var found = new List<EnemyAI>();

            foreach (var body in GameObject.FindGameObjectsWithTag("Enemy"))
            {
                if (body == null) continue;

                var ai = body.GetComponent<EnemyAI>();
                if (ai != null && ai.CurrentState != EnemyAI.State.Dead) found.Add(ai);
            }

            return found;
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

            if (_inspected == 0)
                problems.Append("\n  - level one produced no enemies to test");

            if (_armed == 0)
                problems.Append("\n  - no enemy in level one is armed: the opening level has no guns");

            if (_armed > 0 && _weaponVisible == 0)
                problems.Append("\n  - armed enemies are carrying an invisible rifle: the weapon " +
                                "object under the arm never switched on");

            if (_playerWalkSpeed > 0f && _fastestEnemy >= _playerWalkSpeed)
                problems.Append($"\n  - the fastest enemy ({_fastestEnemy:0.00} m/s) is not slower than " +
                                $"the player's walk ({_playerWalkSpeed:0.00} m/s), so there is no " +
                                "disengaging from one");

            if (_reachedAttack == 0)
                problems.Append("\n  - no enemy ever reached the Attack state while standing in the " +
                                "player's lap: this is the gather-round-and-do-nothing failure");

            var states = new StringBuilder();
            foreach (var pair in StateFrames) states.Append($" {pair.Key}={pair.Value}");

            Notes.Append($"\n  over {_framesSampled} frames: closest approach {_minDistance:0.0}m, " +
                         $"{_framesWithSight} enemy-frames with line of sight, states{states}");

            var rays = new StringBuilder();
            foreach (var pair in RayHits) rays.Append($"\n      {pair.Value,8} x {pair.Key}");

            Notes.Append($"\n  what an enemy's own shot ray meets:{rays}");
            Notes.Append($"\n  most tracers alive at once: {_tracersSeen}");

            if (_poolAfterRanged >= _poolAtEngage)
                problems.Append($"\n  - the player took no damage in {EngageWindow}s under fire from " +
                                $"level one's firing line (pool {_poolAtEngage:0.0} -> {_poolAfterRanged:0.0})");

            if (_closeAttacks == 0)
                problems.Append("\n  - no enemy attacked while the player stood inside its reach");

            if (_poolAtEnd >= _poolBeforeClose)
                problems.Append($"\n  - the player took no damage in {CloseWindow}s standing in an " +
                                $"enemy's face (pool {_poolBeforeClose:0.0} -> {_poolAtEnd:0.0}): this is " +
                                "the gather-round-and-do-nothing failure");

            if (problems.Length > 0)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: level one does not fight:{problems}\n{Notes}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[FPSKitBatch] verify combat passed: {_armed}/{_inspected} of level one arrived " +
                      $"armed, the fastest moved at {_fastestEnemy:0.00} m/s against a {_playerWalkSpeed:0.00} " +
                      $"m/s walk, they shot the player from {_poolAtEngage:0.0} to {_poolAfterRanged:0.0} " +
                      $"and hit them again at point-blank range for {_poolBeforeClose - _poolAtEnd:0.0} " +
                      $"more.{Notes}");
            EditorApplication.Exit(0);
        }
    }
}
#endif
