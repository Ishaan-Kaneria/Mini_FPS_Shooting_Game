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
        const double EngageWindow = 30.0;

        /// <summary>Seconds spent standing in an enemy's face, which is the real regression.</summary>
        const double CloseWindow = 9.0;

        /// <summary>How close the player is held during the point-blank phase.</summary>
        const float PointBlank = 1.6f;

        enum Phase { Enter, Tune, AwaitLevel, Inspect, Engage, CloseIn, Punch, PunchCross, PunchMiss, Reactions, Judge }

        // The reaction check: each strike thrown at point-blank range, and the frames from the
        // blow landing to the body visibly answering it (EnemyBody.ReactionLatencyFrames).
        static readonly MeleeStrike.Strike[] ReactionOrder = { MeleeStrike.Strike.Butt, MeleeStrike.Strike.Jab, MeleeStrike.Strike.Cross, MeleeStrike.Strike.Kick };
        const int MaxReactionFrames = 2;
        static int _rxStep, _rxStage, _rxFrame, _rxLastFrame, _rxHitBefore;
        static EnemyAI _rxTarget;
        static EnemyBody _rxBody;

        /// <summary>
        /// Seconds the level is given while this test runs.
        ///
        /// Level one's own clock is around thirty, and this test needs most of that
        /// before it has even started measuring -- a briefing, a crowd arriving, fourteen
        /// seconds at range and nine in an enemy's face. Run against the real clock it
        /// sits one slow frame away from the level being scored underneath it, and a
        /// scored level freezes time and disables input: every enemy stops mid-Chase and
        /// the test reports that nothing ever attacked. What is being measured here is
        /// the fight, not the clock.
        /// </summary>
        const float TestClock = 240f;

        static Phase _phase;
        static double _deadline;
        static double _startedAt;
        static readonly List<string> Errors = new List<string>();
        static readonly StringBuilder Notes = new StringBuilder();

        /// <summary>The level's own clock, put back in Detach. See TestClock.</summary>
        static LevelSet.Level _tunedLevel;
        static float _clockBackup = -1f;

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

        // --- the punch ------------------------------------------------------
        static bool _punchThrown;
        static string _punchRole = "";
        static float _punchPoolBefore = -1f, _punchPoolAfter = -1f;
        static bool _punchKilled, _gunLowered, _fistShown, _punchMissed;
        static string _punchMissHit = "";
        static float _punchAt;
        static Health _punchTarget;

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
                _lastShot.Clear();
                _shots.Clear();
                _aimedRounds = 0;
                ShotLines.Clear();
                RayHits.Clear();
                _tracersSeen = 0;
                _poolAtEngage = _poolAfterRanged = _poolBeforeClose = _poolAtEnd = -1f;
                _closeAttacks = 0;
                _sawRangedShot = false;
                _punchThrown = _punchKilled = _gunLowered = _fistShown = _punchMissed = false;
                _punchRole = _punchMissHit = "";
                _punchTarget = null;
                _punchPoolBefore = _punchPoolAfter = -1f;
                _rxStep = _rxStage = _rxFrame = 0;
                _rxLastFrame = -1;
                _rxTarget = null;
                _rxBody = null;
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
                FPSKitBatch.Exit(1);
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
                        _phase = Phase.Tune;
                        return;

                    case Phase.Tune:
                    {
                        var level = Level();
                        if (level == null || level.Level == null) return;

                        // Written during the briefing, before RunLevel reads the limit, so
                        // the long clock is the one that applies. The LevelSet is a shared
                        // asset, so the old value is put back in Detach -- a test that
                        // permanently gave level one a four-minute clock would be a test
                        // that broke the game to pass.
                        _clockBackup = level.Level.timeLimit;
                        _tunedLevel = level.Level;

                        level.Level.timeLimit = TestClock;
                        level.ResolveLevel();

                        Notes.Append($"\n  level clock extended from {_clockBackup:0}s to {TestClock:0}s " +
                                     "so the fight outlives it");

                        _phase = Phase.AwaitLevel;
                        return;
                    }

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
                        // From here every frame is a sixtieth of a second of game time, however fast or slow (or hot)
                        // the machine is: the windows below are game time, and so is everything the enemies do in them.
                        Time.captureDeltaTime = 1f / 60f;
                        Inspect();
                        Engage();

                        _poolAtEngage = Pool();
                        Notes.Append($"\n  player pool (health+shield) when the fight started: {_poolAtEngage:0.0}");
                        _engageGameStart = Time.time;
                        Debug.Log("[FPSKit] combat test: the engagement window starts");

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
                            // Rounds each one fires in the window (a new LastShotTime is a new round).
                            _lastShot.TryGetValue(ai, out float last);
                            if (ai.LastShotTime > last + 1e-4f && ai.LastShotTime > _engageGameStart)
                            {
                                _shots[ai] = (_shots.TryGetValue(ai, out int k) ? k : 0) + 1;
                                // Aimed at the player along a clear line (before its spread): a round the player could take.
                                if (player != null && ai.eyes != null)
                                {
                                    Vector3 line = player.transform.position + Vector3.up * 1.2f - ai.eyes.position;
                                    bool met = Physics.Raycast(ai.eyes.position, line.normalized, out var onLine, line.magnitude + 1f, ai.rangedHitMask, QueryTriggerInteraction.Ignore);
                                    if (met && onLine.collider.transform.root == player.transform.root) _aimedRounds++;
                                    string what = !met ? "nothing" : onLine.collider.transform.root == ai.transform.root
                                        ? $"its own {onLine.collider.name} (layer {LayerMask.LayerToName(onLine.collider.gameObject.layer)})"
                                        : $"{onLine.collider.name} (layer {LayerMask.LayerToName(onLine.collider.gameObject.layer)})";
                                    ShotLines.TryGetValue(what, out int seen2);
                                    ShotLines[what] = seen2 + 1;
                                }
                            }
                            _lastShot[ai] = ai.LastShotTime;

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

                        // What each enemy's mind made of it, for when nobody fought.
                        int n = 0;
                        foreach (var ai in Enemies())
                        {
                            n++;
                            var brain = ai.GetComponent<EnemyBrain>();
                            Vector3 to = player != null ? player.transform.position - ai.transform.position : Vector3.zero;
                            float angle = Vector3.Angle(ai.transform.forward, Vector3.ProjectOnPlane(to, Vector3.up));
                            string Meets(float up)
                            {
                                if (player == null || ai.eyes == null) return "-";
                                Vector3 at = player.transform.position + Vector3.up * up - ai.eyes.position;
                                return Physics.Raycast(ai.eyes.position, at.normalized, out var hit, at.magnitude + 1f, ai.rangedHitMask, QueryTriggerInteraction.Ignore)
                                    ? hit.collider.name : "nothing";
                            }
                            if (brain != null && brain.Mind >= EnemyBrain.Awareness.Alert)
                            {
                                // Its chance a round lands from here: the game's own spread (EnemyAI.FireRangedShot), 400 times.
                                int lands = 0;
                                if (player != null && ai.eyes != null)
                                    for (int k = 0; k < 400; k++)
                                    {
                                        Vector3 dir = (player.transform.position + Vector3.up * 1.2f - ai.eyes.position).normalized;
                                        Vector2 off = UnityEngine.Random.insideUnitCircle * ai.rangedSpread;
                                        dir = Quaternion.AngleAxis(off.x, Vector3.up) * Quaternion.AngleAxis(off.y, ai.transform.right) * dir;
                                        if (Physics.Raycast(ai.eyes.position, dir, out var h, ai.detectionRadius, ai.rangedHitMask, QueryTriggerInteraction.Ignore)
                                            && h.collider.transform.root == player.transform.root) lands++;
                                    }
                                Notes.Append($"\n    its shot at the chest (1.2 m) meets {Meets(1.2f)}; at 1.4 m {Meets(1.4f)}; at the head (1.6 m) {Meets(1.6f)}; " +
                                             $"spread {ai.rangedSpread:0.0} deg, so {lands / 4f:0}% of its rounds land from here");
                            }
                            Notes.Append($"\n  enemy {n}: {(_shots.TryGetValue(ai, out int fired) ? fired : 0)} round(s) fired; {(brain != null ? brain.Mind + " (" + brain.Doing + "), suspicion " + brain.Suspicion.ToString("0.00") : ai.CurrentState.ToString())}, " +
                                         $"{to.magnitude:0.0}m, player {angle:0} deg off its facing");
                        }

                        _poolBeforeClose = _poolAfterRanged;
                        Notes.Append($"\n  after {EngageWindow}s at range: pool {_poolAtEngage:0.0} " +
                                     $"-> {_poolAfterRanged:0.0} ({Time.time - _engageGameStart:0.0} s of game time in {_framesSampled} frames)");

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
                        if (hurt || !Waiting()) _phase = Phase.Punch;

                        return;
                    }

                    case Phase.Punch:
                    {
                        // Arm's length from the nearest enemy, facing it: the one place a
                        // punch has to land. Thrown through the component rather than a
                        // key, because batch mode has no keyboard to press.
                        HoldPointBlank();
                        var player = GameObject.FindGameObjectWithTag("Player");
                        var nearest = Nearest(player);
                        var melee = player != null ? player.GetComponent<MeleeStrike>() : null;

                        if (melee == null || nearest == null)
                        {
                            Notes.Append("\n  punch: no MeleeStrike on the player, or nobody left to punch");
                            _phase = Phase.Judge;
                            return;
                        }

                        Face(player, nearest.transform.position);
                        Physics.SyncTransforms();

                        var target = nearest.GetComponent<Health>();
                        _punchPoolBefore = target.Current + target.Shield;
                        _punchRole = nearest.archetype != null ? nearest.archetype.role.ToString() : "Standard";

                        // Game time, pinned: the cooldown is measured in it, and batch frames
                        // otherwise advance it by next to nothing. Put back in Detach.
                        Time.captureDeltaTime = 1f / 60f;
                        _punchAt = Time.time;

                        // The rifle butt, the first strike of a fight. A punch follows inside the
                        // chain window (PunchCross), and the two together put an ordinary enemy down.
                        var hit = melee.TryPunch();
                        _punchThrown = true;
                        _punchTarget = target;
                        _punchPoolAfter = target.Current + target.Shield;
                        _punchKilled = target.IsDead;
                        _gunLowered = melee.weapon != null && melee.weapon.IsLowered;

                        Notes.Append($"\n  rifle butt at {Vector3.Distance(player.transform.position, nearest.transform.position):0.0}m " +
                                     $"hit {(hit != null ? hit.name : "nothing")} ({_punchRole}): pool " +
                                     $"{_punchPoolBefore:0.0} -> {_punchPoolAfter:0.0}");
                        if (hit == null) Notes.Append(WhyMissed(melee, target));

                        Wait(30.0, Phase.PunchCross);
                        return;
                    }

                    case Phase.PunchCross:
                    {
                        var player = GameObject.FindGameObjectWithTag("Player");
                        var melee = player != null ? player.GetComponent<MeleeStrike>() : null;
                        if (melee != null && (melee.FistsUp > 0.3f || (melee.weapon != null && melee.weapon.HandsRotation.magnitude > 5f)))
                            _fistShown = true;

                        // Past the jab's recovery, still inside the window for the cross.
                        // The second press of the fight: the gun goes on the back and a fist lands.
                        float ready = melee != null ? melee.ReadyAt + 0.02f : _punchAt;
                        if (Time.time < ready)
                        {
                            if (Waiting()) return;
                            Notes.Append("\n  punch: game time never reached the cross");
                        }

                        // The first blow has landed by now (a blow lands when the fist arrives).
                        if (_punchTarget != null) _punchPoolAfter = _punchTarget.Current + _punchTarget.Shield;
                        Notes.Append($"\n  after the butt: pool {_punchPoolAfter:0.0}");

                        if (melee != null && _punchTarget != null && !_punchTarget.IsDead)
                        {
                            // Still facing it, as a player keeps the enemy in the crosshair after the butt.
                            if (player != null) { Face(player, _punchTarget.transform.position); Physics.SyncTransforms(); }
                            var hit = melee.TryPunch();
                            Notes.Append($"\n  punch thrown at {(hit != null ? hit.name : "nothing")}");
                            if (hit == null)
                            {
                                Notes.Append($"\n    target {Vector3.Distance(player.transform.position, _punchTarget.transform.position):0.0} m from the player; " +
                                             $"strike ready at {Time.time:0.00}? chain {(melee.Current == MeleeStrike.Strike.None ? "idle" : melee.Current.ToString())}");
                                Notes.Append(WhyMissed(melee, _punchTarget));
                            }
                        }
                        _punchAt = Time.time;
                        Wait(30.0, Phase.PunchMiss);
                        return;
                    }

                    case Phase.PunchMiss:
                    {
                        var player = GameObject.FindGameObjectWithTag("Player");
                        var melee = player != null ? player.GetComponent<MeleeStrike>() : null;
                        // The strike is the rifle turned stock-first and driven forward (or,
                        // with a fist wired, the fist out). Either is seen, a frame on.
                        if (melee != null && (melee.FistsUp > 0.3f
                                              || (melee.weapon != null && melee.weapon.HandsRotation.magnitude > 5f)))
                            _fistShown = true;

                        // Past the cooldown, then well out of reach: a punch must not
                        // land on anyone eight metres away.
                        var ms = MeleeSettings.Active;
                        if (melee != null && Time.time < Mathf.Max(melee.ReadyAt, _punchAt + ms.crossSeconds) + 0.05f)
                        {
                            if (Waiting()) return;
                            Notes.Append("\n  punch: game time never passed the cooldown");
                        }

                        // The punch has landed (or missed) by now: judge the pair.
                        if (_punchTarget != null)
                        {
                            _punchPoolAfter = _punchTarget.Current + _punchTarget.Shield;
                            _punchKilled = _punchTarget.IsDead;
                        }

                        var nearest = Nearest(player);
                        if (melee != null && nearest != null)
                        {
                            Vector3 away = Vector3.ProjectOnPlane(player.transform.position - nearest.transform.position, Vector3.up);
                            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;

                            var controller = player.GetComponent<CharacterController>();
                            if (controller != null) controller.enabled = false;
                            player.transform.position = nearest.transform.position + away * 8f + Vector3.up * 1.1f;
                            if (controller != null) controller.enabled = true;

                            Face(player, nearest.transform.position);
                            Physics.SyncTransforms();

                            var hit = melee.TryPunch();
                            _punchMissed = hit == null;
                            _punchMissHit = hit != null ? hit.name : "";
                        }

                        _phase = Phase.Reactions;
                        return;
                    }

                    case Phase.Reactions:
                        if (Reactions()) _phase = Phase.Judge;
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
                Debug.LogError($"[FPSKitBatch] FAILED: {e}{Notes}");
                FPSKitBatch.Exit(1);
            }
        }

        // ==================================================================

        /// <summary>Records what level one actually turned up as, before anything is moved.</summary>
        static void Inspect()
        {
            var motor = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
            _playerWalkSpeed = motor != null ? motor.JogSpeedNow : 0f;

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
        /// <summary>
        /// Puts the player where an enemy can actually shoot them.
        ///
        /// This used to be the centroid of the crowd, and it made the test a coin flip.
        /// Three enemies arriving from three sides of a golden-angle spawn ring have a
        /// centroid that is nowhere near any of them, and in an arena full of crates the
        /// spot frequently had no line of sight to anybody -- so the fourteen-second
        /// window measured the warehouse furniture rather than the AI, and reported "the
        /// player took no damage" as if the enemies had refused to fight.
        ///
        /// It now stands the player at a fixed mid range in front of a chosen enemy, and
        /// the enemy chosen is one that can see that spot. If none can, it says so rather
        /// than running the window and blaming the result on the AI.
        /// </summary>
        static void Engage()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            var enemies = Enemies();
            if (player == null || enemies.Count == 0) return;

            var controller = player.GetComponent<CharacterController>();

            // Well inside the roster's detection radius and well outside its melee reach,
            // so what the window measures is shooting and not swinging.
            const float StandOff = 11f;

            Vector3 chosen = Vector3.zero;
            EnemyAI target = null;

            foreach (var ai in enemies)
            {
                Vector3 eye = ai.eyes != null ? ai.eyes.position : ai.transform.position + Vector3.up * 1.5f;

                // Straight back from the enemy along its own facing: the one bearing it
                // is guaranteed to have been able to see a moment ago.
                Vector3 bearing = Vector3.ProjectOnPlane(ai.transform.forward, Vector3.up);
                if (bearing.sqrMagnitude < 0.001f) bearing = Vector3.forward;

                Vector3 spot = ai.transform.position + bearing.normalized * StandOff;

                if (!UnityEngine.AI.NavMesh.SamplePosition(spot, out var hit, 6f,
                                                           UnityEngine.AI.NavMesh.AllAreas))
                    continue;

                Vector3 chest = hit.position + Vector3.up * 1.4f;
                if (Physics.Linecast(eye, chest, ai.sightBlockers, QueryTriggerInteraction.Ignore))
                    continue;

                chosen = hit.position;
                target = ai;
                break;
            }

            if (target == null)
            {
                // Nowhere with a clear line. Fall back to the old behaviour rather than
                // skipping the phase, and record it, so a failure that follows can be
                // read as "the geometry beat the test" instead of "the AI did nothing".
                Vector3 centre = Vector3.zero;
                foreach (var ai in enemies) centre += ai.transform.position;

                chosen = centre / enemies.Count;
                Notes.Append("\n  no enemy had a clear line to a stand-off point; " +
                             "falling back to the middle of the crowd");
            }

            if (controller != null) controller.enabled = false;

            player.transform.position = chosen + Vector3.up * 1.1f;

            // Facing the enemy, because an enemy behind the player is one the player
            // cannot be shot in the chest by and one the damage indicator would be the
            // only evidence of.
            if (target != null)
            {
                Vector3 toTarget = Vector3.ProjectOnPlane(
                    target.transform.position - player.transform.position, Vector3.up);

                if (toTarget.sqrMagnitude > 0.001f)
                    player.transform.rotation = Quaternion.LookRotation(toTarget);
            }

            if (controller != null) controller.enabled = true;

            // Called out to the squad, as a comrade who sees the player does (EnemySquad.Shout): this window is about
            // whether level one fights once it knows where the player is. Whether and how fast it notices is the
            // perception's (Stage 1); left to chance here, a teleported player sometimes stood behind every one of them.
            foreach (var ai in enemies)
            {
                var brain = ai.GetComponent<EnemyBrain>();
                if (brain != null) brain.HearCall(chosen, 0f);
            }

            Notes.Append($"\n  player moved to {chosen} -- " +
                         (target != null
                             ? $"{StandOff:0}m in front of an enemy with a clear line to it"
                             : "the middle of the crowd"));
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

        static EnemyAI Nearest(GameObject player)
        {
            if (player == null) return null;

            EnemyAI nearest = null;
            float best = float.MaxValue;
            foreach (var ai in Enemies())
            {
                float d = Vector3.Distance(ai.transform.position, player.transform.position);
                if (d < best) { best = d; nearest = ai; }
            }
            return nearest;
        }

        static void Face(GameObject player, Vector3 at)
        {
            Vector3 to = Vector3.ProjectOnPlane(at - player.transform.position, Vector3.up);
            if (to.sqrMagnitude > 0.001f) player.transform.rotation = Quaternion.LookRotation(to);
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

        static float _engageGameStart;
        static readonly Dictionary<EnemyAI, float> _lastShot = new Dictionary<EnemyAI, float>();
        static readonly Dictionary<EnemyAI, int> _shots = new Dictionary<EnemyAI, int>();
        static int _aimedRounds;
        static readonly Dictionary<string, int> ShotLines = new Dictionary<string, int>();

        /// <summary>
        /// Rounds aimed at the player along a clear line that prove level one fights, when none happens to land. A level-one
        /// Grunt spreads 5 degrees: from 12-20 m one round in five or six lands, and bounding between firing spots it fires a
        /// handful in the window, so "no damage in 14 s" was a one-in-three chance on a good run, not a fault. Whether a hit
        /// registers is proven where one cannot miss: point-blank, below.
        /// </summary>
        const int AimedRoundsThatFight = 3;

        /// <summary>
        /// Waits <paramref name="seconds"/> of game time once the level is playing, the enemies' time, not the
        /// wall clock's. The windows were measured on the wall clock, and a hot, throttled laptop (or one frozen by
        /// the batch script's heat guard) put a fraction of the time in them: a 14 s window held 4.3 s of game time,
        /// the one enemy that had seen the player was still on its first suppressing burst, and the test read it as
        /// "level one does not fight" about one run in two on a hot machine. The wall clock is only a safety net now.
        /// </summary>
        static void Wait(double seconds, Phase next)
        {
            _deadline = EditorApplication.timeSinceStartup + seconds * 8.0 + 60.0;
            _gameDeadline = EditorApplication.isPlaying ? Time.time + (float)seconds : -1f;
            _phase = next;
        }

        static float _gameDeadline = -1f;

        static bool Waiting() => EditorApplication.timeSinceStartup < _deadline
                                 && (_gameDeadline < 0f || Time.time < _gameDeadline);

        static void OnGameLog(string message, string stackTrace, LogType type)
        {
            // The editor's own log lines (the Android device probe, store packs' windows in batch mode). Not the game.
            if (FPSKitPlayMode.IsEditorNoise(message)) return;

            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Errors.Add($"[{type}] {message.Trim()}");
        }

        static void Detach()
        {
            // The LevelSet is a shared asset, so whatever the clock was before this ran
            // goes back however the test ended.
            if (_tunedLevel != null && _clockBackup > 0f) _tunedLevel.timeLimit = _clockBackup;

            _tunedLevel = null;
            _clockBackup = -1f;

            FPSKitPlayMode.RestoreStartScene();
            Time.captureDeltaTime = 0f;

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

            Notes.Append($"\n  rounds aimed at the player along a clear line in the window: {_aimedRounds}");
            foreach (var kv in ShotLines) Notes.Append($"\n    at the moment of a shot, its eye-to-chest line met {kv.Key}: {kv.Value}");
            if (_poolAfterRanged >= _poolAtEngage && _aimedRounds < AimedRoundsThatFight)
                problems.Append($"\n  - in {EngageWindow}s of game time at the firing line the player was neither hit nor shot at " +
                                $"({_aimedRounds} round(s) aimed along a clear line, {AimedRoundsThatFight} needed; pool {_poolAtEngage:0.0} -> {_poolAfterRanged:0.0})");

            if (_closeAttacks == 0)
                problems.Append("\n  - no enemy attacked while the player stood inside its reach");

            if (_poolAtEnd >= _poolBeforeClose)
                problems.Append($"\n  - the player took no damage in {CloseWindow}s standing in an " +
                                $"enemy's face (pool {_poolBeforeClose:0.0} -> {_poolAtEnd:0.0}): this is " +
                                "the gather-round-and-do-nothing failure");

            if (!_punchThrown)
                problems.Append("\n  - no punch was thrown: the player has no MeleeStrike, or nobody was left");
            else
            {
                if (_punchPoolAfter >= _punchPoolBefore)
                    problems.Append($"\n  - a punch thrown at arm's length from a {_punchRole} did no damage " +
                                    $"(pool {_punchPoolBefore:0.0} -> {_punchPoolAfter:0.0})");

                if (_punchRole == "Standard" && !_punchKilled)
                    problems.Append("\n  - the rifle butt and a punch did not put down an ordinary enemy, which " +
                                    "is the whole point of them on a level with no spare rounds");

                if (!_gunLowered)
                    problems.Append("\n  - the gun was not lowered during the punch: it could fire through the swing");

                if (!_fistShown)
                    problems.Append("\n  - the strike never showed: neither the rifle butt nor a fist moved, the punch is invisible");

                if (!_punchMissed)
                    problems.Append($"\n  - a punch thrown from eight metres hit {_punchMissHit}");
            }

            if (problems.Length > 0)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: level one does not fight:{problems}\n{Notes}");
                FPSKitBatch.Exit(1);
                return;
            }

            Debug.Log($"[FPSKitBatch] verify combat passed: {_armed}/{_inspected} of level one arrived " +
                      $"armed, the fastest moved at {_fastestEnemy:0.00} m/s against a {_playerWalkSpeed:0.00} " +
                      $"m/s walk, they shot the player from {_poolAtEngage:0.0} to {_poolAfterRanged:0.0} " +
                      $"and hit them again at point-blank range for {_poolBeforeClose - _poolAtEnd:0.0} " +
                      $"more.{Notes}");
            FPSKitBatch.Exit(0);
        }

        /// <summary>
        /// One frame of the reaction check; true when every strike has been measured. Each strike
        /// is thrown from arm's length at a living enemy (its health topped up, so the chain of
        /// butt, punch, cross runs on one body), and the frames from the blow landing to the body
        /// visibly moving for it are read off EnemyBody. Game time runs at 1/60 s a frame, so a
        /// frame is 16.7 ms: two frames is the most allowed.
        /// </summary>
        static bool Reactions()
        {
            if (Time.frameCount == _rxLastFrame) return false;   // once per game frame
            _rxLastFrame = Time.frameCount;
            _rxFrame++;

            var player = GameObject.FindGameObjectWithTag("Player");
            var melee = player != null ? player.GetComponent<MeleeStrike>() : null;
            if (melee == null || _rxStep >= ReactionOrder.Length) return true;
            var strike = ReactionOrder[_rxStep];
            Time.captureDeltaTime = 1f / 60f;
            var playerHealth = player.GetComponent<Health>();
            if (playerHealth != null && playerHealth.Normalized < 0.5f) playerHealth.ResetHealth();

            switch (_rxStage)
            {
                case 0:   // a target: the nearest living rigged enemy, standing
                {
                    if (_rxTarget == null || _rxBody == null || _rxTarget.CurrentState == EnemyAI.State.Dead || _rxBody.IsDown)
                    {
                        _rxTarget = null; _rxBody = null;
                        float best = float.MaxValue;
                        foreach (var ai in Enemies())
                        {
                            var body = ai.GetComponent<EnemyBody>();
                            if (body == null || body.IsDown || ai.CurrentState == EnemyAI.State.Dead) continue;
                            float d = Vector3.Distance(ai.transform.position, player.transform.position);
                            if (d < best) { best = d; _rxTarget = ai; _rxBody = body; }
                        }
                    }
                    if (_rxTarget == null)
                    {
                        if (_rxFrame > 600) { Notes.Append($"\n  reaction to {strike}: no standing enemy to strike"); _rxStep++; _rxFrame = 0; }
                        return false;
                    }
                    if (melee.Current != MeleeStrike.Strike.None || Time.time < melee.ReadyAt + 0.02f) return false;
                    // The butt only opens a fight: the last chain (and the fists) must have run out first.
                    if (strike == MeleeStrike.Strike.Butt && (_rxFrame < 200 || melee.GunAway > 0.01f)) return false;

                    var health = _rxTarget.GetComponent<Health>();
                    health.ResetHealth();
                    Vector3 away = Vector3.ProjectOnPlane(player.transform.position - _rxTarget.transform.position, Vector3.up);
                    away = away.sqrMagnitude > 0.01f ? away.normalized : _rxTarget.transform.forward;
                    var controller = player.GetComponent<CharacterController>();
                    if (controller != null) controller.enabled = false;
                    player.transform.position = _rxTarget.transform.position + away * 1.3f + Vector3.up * 0.05f;
                    if (controller != null) controller.enabled = true;
                    Face(player, _rxTarget.transform.position);
                    Physics.SyncTransforms();

                    _rxHitBefore = _rxBody.LastHitFrame;
                    var hit = strike == MeleeStrike.Strike.Kick ? melee.TryKick() : melee.TryPunch();
                    if (melee.Thrown != strike)
                    {
                        Notes.Append($"\n  reaction to {strike}: the chain threw {melee.Thrown} instead");
                        Errors.Add($"the reaction check could not throw a {strike} (got {melee.Thrown})");
                        _rxStep = ReactionOrder.Length;
                        return true;
                    }
                    _rxStage = 1;
                    _rxFrame = 0;
                    return false;
                }

                case 1:   // the blow lands, then the body answers it
                {
                    bool landed = _rxBody != null && _rxBody.LastHitFrame > _rxHitBefore;
                    int latency = landed ? _rxBody.ReactionLatencyFrames : -1;
                    if (latency >= 0)
                    {
                        float ms = latency * 1000f / 60f;
                        Notes.Append($"\n  reaction to {strike}: {latency} frame(s), {ms:0} ms from the blow landing to the body moving");
                        if (latency > MaxReactionFrames)
                            Errors.Add($"the enemy answered a {strike} {latency} frames ({ms:0} ms) after it landed; at most {MaxReactionFrames}");
                        _rxStep++;
                        _rxStage = 0;
                        _rxFrame = 0;
                        return _rxStep >= ReactionOrder.Length;
                    }
                    // Until the blow lands the player is held at arm's length, facing the target: an alert enemy keeps
                    // moving, and since a blow only lands in reach at impact (MeleeStrike, Part A), one that stepped back
                    // during the wind-up read as "no reaction" when the strike had simply missed. This times the reaction.
                    if (!landed && _rxTarget != null && melee.Current != MeleeStrike.Strike.None)
                    {
                        Vector3 back = Vector3.ProjectOnPlane(player.transform.position - _rxTarget.transform.position, Vector3.up);
                        back = back.sqrMagnitude > 0.01f ? back.normalized : _rxTarget.transform.forward;
                        var cc = player.GetComponent<CharacterController>();
                        if (cc != null) cc.enabled = false;
                        player.transform.position = _rxTarget.transform.position + back * 1.3f + Vector3.up * 0.05f;
                        if (cc != null) cc.enabled = true;
                        Face(player, _rxTarget.transform.position);
                        Physics.SyncTransforms();
                    }
                    if (_rxFrame > 120)
                    {
                        Notes.Append($"\n  reaction to {strike}: no blow landed, or no answer within 2 s");
                        Errors.Add($"a {strike} at arm's length never got a visible reaction");
                        _rxStep++;
                        _rxStage = 0;
                        _rxFrame = 0;
                    }
                    return false;
                }
            }
            return false;
        }

        /// <summary>When a strike hits nothing: what the strike could see from the eye, so the reason is in the report.</summary>
        static string WhyMissed(MeleeStrike melee, Health target)
        {
            var cam = melee.fpsCamera != null ? melee.fpsCamera.transform : null;
            if (cam == null) return "\n    (no camera on the strike)";
            int enemy = LayerMask.NameToLayer("Enemy"), playerLayer = LayerMask.NameToLayer("Player");
            var sb = new System.Text.StringBuilder();
            Vector3 eye = cam.position;
            var cols = Physics.OverlapSphere(eye, 3.6f, 1 << enemy, QueryTriggerInteraction.Ignore);
            sb.Append($"\n    eye {eye}, looking {cam.forward}; enemy-layer colliders within 3.6 m: {cols.Length}");
            var anim = target.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                sb.Append($"\n    enemy root {target.transform.position}; animator '{anim.name}' local {anim.transform.localPosition} " +
                          $"rootMotion {anim.applyRootMotion} enabled {anim.enabled}; hips world {(hips != null ? hips.position.ToString() : "-")} " +
                          $"local {(hips != null ? hips.localPosition.ToString() : "-")}; motor rootMotion {target.GetComponent<EnemyMotor>()?.RootMotion}");
                foreach (var rb in anim.GetComponentsInChildren<Rigidbody>())
                    if (!rb.isKinematic) { sb.Append($"\n    non-kinematic body part: {rb.name}"); break; }
            }
            foreach (var col in target.GetComponentsInChildren<Collider>())
            {
                if (!col.enabled || col.isTrigger) continue;
                Vector3 p = col.ClosestPoint(eye), to = p - eye;
                float d = to.magnitude;
                string blocked = "clear";
                int mask = ~((1 << enemy) | (1 << playerLayer));
                if (d > 0.05f && Physics.Raycast(eye, to / d, out var h, d - 0.02f, mask, QueryTriggerInteraction.Ignore))
                    blocked = $"blocked by {h.collider.name} (layer {LayerMask.LayerToName(h.collider.gameObject.layer)}) at {h.distance:0.00} m";
                sb.Append($"\n    {col.name} (layer {LayerMask.LayerToName(col.gameObject.layer)}): {d:0.00} m, {Vector3.Angle(cam.forward, to):0} deg off, {blocked}");
            }
            return sb.ToString();
        }
    }
}
#endif
