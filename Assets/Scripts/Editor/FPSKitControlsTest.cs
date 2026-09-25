#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Walks every control a keyboard-and-mouse player has and checks it does what the
    /// binding says.
    ///
    /// Two halves, because controls break in two unrelated ways.
    ///
    /// **The bindings themselves.** Two actions on one key, or an action left on None,
    /// is a defect no amount of play testing one scheme will find -- the presets in
    /// `ControlSettings.ApplyPreset` are edited by hand and nothing has ever checked
    /// them against each other. A gameplay key that collides with the *pause* key is
    /// the worst of these: it pauses the game in the middle of a fight and reads as a
    /// crash.
    ///
    /// **What the actions do.** Driven through `MobileInput`, which every gameplay
    /// component already reads as an OR beside its key, so this exercises the real
    /// movement, weapon and belt code paths a frame at a time. Batch mode cannot
    /// synthesise a legacy key press, so the alternative is testing nothing.
    ///
    /// Speeds are compared against *each other* rather than against the tuning
    /// constants -- walk versus sprint in the same direction, from the same spot.
    /// An arena has walls in it, and a test that demanded 8.2 m/s would fail on the day
    /// somebody moved a crate.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyControls
    /// </summary>
    public static class FPSKitControlsTest
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";
        const double HardTimeout = 260.0;
        const float TestClock = 240f;

        enum Phase { Enter, Tune, Await, Measure, Judge }

        static Phase _phase;
        static double _startedAt;
        static readonly List<string> Errors = new List<string>();
        static readonly StringBuilder Notes = new StringBuilder();

        static LevelSet.Level _tunedLevel;
        static float _clockBackup = -1f;

        static int _step, _frame;
        static float _peak;

        // ---- what each burst measured ----
        static float _walkAhead, _sprintAhead, _walkSide, _sprintSide, _crouchAhead;
        static float _sprintAlone = -1f;
        static bool _drivesForward = true;
        static bool _sprintedStandingStill, _crouched, _leftTheGround, _aimedDownSights;
        static int _ammoBefore = -1, _ammoAfterFiring = -1, _ammoAfterReload = -1;

        public static void VerifyControls()
        {
            try
            {
                if (!System.IO.File.Exists(ScenePath))
                    throw new Exception($"{ScenePath} does not exist. Build it first.");

                Errors.Clear();
                Notes.Clear();

                AuditBindings();

                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                _step = _frame = 0;
                _peak = 0f;
                _walkAhead = _sprintAhead = _walkSide = _sprintSide = _crouchAhead = -1f;
                _sprintAlone = -1f;
                _drivesForward = true;
                _sprintedStandingStill = _crouched = _leftTheGround = _aimedDownSights = false;
                _ammoBefore = _ammoAfterFiring = _ammoAfterReload = -1;
                _startedAt = EditorApplication.timeSinceStartup;
                _phase = Phase.Enter;

                FPSKitPlayMode.SuspendStartScene();
                EditorApplication.update += Tick;

                Debug.Log("[FPSKitBatch] controls test: every binding distinct, and every action doing its job");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        // ======================================================================
        /// <summary>
        /// The bindings, before anything is played. Checks the live asset and every
        /// preset, because a preset nobody has selected today is a preset somebody
        /// selects tomorrow.
        /// </summary>
        static void AuditBindings()
        {
            var live = AssetDatabase.LoadAssetAtPath<ControlSettings>(
                "Assets/FPSKit_Generated/Controls.asset");

            if (live != null) CheckScheme("Controls.asset", live);
            else Notes.Append("\n  no Controls.asset on disk, so only the presets were checked");

            foreach (ControlSettings.Preset preset in Enum.GetValues(typeof(ControlSettings.Preset)))
            {
                var probe = ScriptableObject.CreateInstance<ControlSettings>();
                probe.ApplyPreset(preset);
                CheckScheme(preset.ToString(), probe);
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        /// <summary>
        /// Fields that legitimately share a key with another field, because they are
        /// two names for one action rather than two actions.
        /// </summary>
        static readonly HashSet<string> Alternates = new HashSet<string>
        {
            "altForward", "altBack", "altLeft", "altRight", "altCrouch"
        };

        /// <summary>Actions a keyboard player cannot do without.</summary>
        static readonly string[] Essential =
        {
            "moveForward", "moveBack", "moveLeft", "moveRight",
            "fire", "jump", "aim", "reload", "crouch", "bomb", "useItem", "melee", "sprintKey"
        };

        static bool IsPair(string a, string b, string x, string y)
            => (a == x && b == y) || (a == y && b == x);

        static void CheckScheme(string label, ControlSettings scheme)
        {
            var bound = new Dictionary<KeyCode, string>();

            foreach (var field in typeof(ControlSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(KeyCode)) continue;

                var key = (KeyCode)field.GetValue(scheme);
                if (key == KeyCode.None) continue;

                // The pause key stops the game. A gameplay binding sharing it does not
                // conflict with another *binding*, it conflicts with the game.
                if (key == KeyCode.Escape || key == KeyCode.P)
                    Errors.Add($"{label}: \"{field.Name}\" is bound to {key}, which pauses the game");

                if (bound.TryGetValue(key, out string owner))
                {
                    bool pair = Alternates.Contains(field.Name) || Alternates.Contains(owner);

                    // Fire and sprint on one key is the whole point of the double-tap
                    // scheme -- tap to shoot, tap twice to run -- so it is a collision
                    // only when the double tap is off, at which point holding the key
                    // would fire and sprint at the same time.
                    bool sharedTap = scheme.sprintByDoubleTap &&
                                     IsPair(owner, field.Name, "fire", "sprintKey");

                    // Two names for one action sharing a key is merely pointless. Two
                    // different actions sharing one is a key that does both at once.
                    if (!pair && !sharedTap)
                        Errors.Add($"{label}: \"{owner}\" and \"{field.Name}\" are both on {key}, " +
                                   "so one key fires two different actions");
                }
                else
                {
                    bound[key] = field.Name;
                }
            }

            foreach (string name in Essential)
            {
                var field = typeof(ControlSettings).GetField(name);
                if (field == null) { Errors.Add($"ControlSettings has no field \"{name}\""); continue; }

                if ((KeyCode)field.GetValue(scheme) == KeyCode.None)
                    Errors.Add($"{label}: \"{name}\" is unbound, so that action cannot be performed");
            }

            Notes.Append($"\n  {label}: {bound.Count} distinct keys, no collisions");
        }

        // ======================================================================
        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _startedAt > HardTimeout)
                    throw new Exception($"controls test exceeded {HardTimeout}s in phase {_phase} step {_step}");

                switch (_phase)
                {
                    case Phase.Enter:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        _phase = Phase.Tune;
                        return;

                    case Phase.Tune:
                    {
                        var level = UnityEngine.Object.FindAnyObjectByType<LevelManager>();
                        if (level == null || level.Level == null) return;

                        _clockBackup = level.Level.timeLimit;
                        _tunedLevel = level.Level;
                        level.Level.timeLimit = TestClock;
                        level.ResolveLevel();

                        _phase = Phase.Await;
                        return;
                    }

                    case Phase.Await:
                    {
                        var level = UnityEngine.Object.FindAnyObjectByType<LevelManager>();
                        if (level == null || !level.IsRunning) return;

                        // Every action below is driven through the on-screen control
                        // state, which each component reads beside its own key.
                        MobileInput.Active = true;
                        MobileInput.Reset();
                        MobileInput.Active = true;

                        // The arena is full of enemies that close in, shove and shoot.
                        // A speed measured while one is leaning on the player is not a
                        // measurement of the movement code. VerifyCombat owns the
                        // fight; this test owns the controls.
                        foreach (var ai in UnityEngine.Object.FindObjectsByType<EnemyAI>(
                                     FindObjectsSortMode.None))
                        {
                            var agent = ai.GetComponent<UnityEngine.AI.NavMeshAgent>();
                            if (agent != null) agent.enabled = false;

                            ai.enabled = false;
                        }

                        // A fixed step, because batchmode -nographics runs the game at a
                        // delta time of very nearly zero. Movement ramps at 55 m/s^2, so
                        // a frame worth half a millisecond adds three centimetres a
                        // second: fifty frames of sprinting reached 1.2 m/s against a
                        // 5.6 m/s walk, and every speed in this test was measuring the
                        // batch frame rate rather than the game. Restored in Detach --
                        // it is a global, and this project disables domain reload.
                        Time.captureDeltaTime = 1f / 60f;

                        FaceOpenGround(UnityEngine.Object.FindAnyObjectByType<PlayerMotor>());

                        _phase = Phase.Measure;
                        return;
                    }

                    case Phase.Measure:
                        Measure();
                        return;
                }
            }
            catch (Exception e)
            {
                Errors.Add(e.Message);
                Finish();
            }
        }

        /// <summary>
        /// Turns the player to the bearing with the most room ahead *and* to the right.
        ///
        /// Both, because the strafe bursts move sideways from the same spot. Without
        /// this the test measures whatever the spawn happens to face: the first run
        /// reported walking forward at 1.2 m/s against a 5.6 m/s walk speed, which is
        /// not a slow player, it is a player standing against a crate.
        /// </summary>
        static void FaceOpenGround(PlayerMotor motor)
        {
            if (motor == null) return;

            Vector3 eye = motor.transform.position + Vector3.up;
            float bestYaw = motor.transform.eulerAngles.y;
            float best = -1f;

            for (int yaw = 0; yaw < 360; yaw += 10)
            {
                var facing = Quaternion.Euler(0f, yaw, 0f);
                float room = Mathf.Min(Clearance(eye, facing * Vector3.forward),
                                       Clearance(eye, facing * Vector3.right));

                if (room <= best) continue;

                best = room;
                bestYaw = yaw;
            }

            motor.TurnBy(new Vector2(Mathf.DeltaAngle(motor.transform.eulerAngles.y, bestYaw), 0f));

            Notes.Append($"\n  measured facing {bestYaw:0}deg, with {best:0.0}m of room " +
                         "ahead and to the right");
        }

        static float Clearance(Vector3 from, Vector3 direction)
            => Physics.Raycast(from, direction, out RaycastHit hit, 25f, ~0,
                               QueryTriggerInteraction.Ignore)
                ? hit.distance
                : 25f;

        /// <summary>
        /// Frames each burst runs for. Movement needs long enough to reach top speed
        /// at 55 m/s^2 and then hold it; a reload is two and a half seconds of animation
        /// and was originally given half a second, which is why it reported never
        /// refilling.
        /// </summary>
        static int FramesFor(int step) => step switch
        {
            4 => 40,     // standing still: long enough to shed the previous burst
            6 => 45,     // jump: up
            7 => 300,    // and back down, ended early on landing
            9 => 400,    // reload: ended early the moment it finishes
            _ => 50
        };

        static void Measure()
        {
            var motor = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
            var weapon = UnityEngine.Object.FindAnyObjectByType<Weapon>();

            if (motor == null) throw new Exception("the scene has no PlayerMotor");

            if (_frame == 0) StartBurst(motor, weapon);

            // The first third of every burst is thrown away. Each one starts at
            // whatever speed the last left behind, and the peak of a walk that began
            // at a sprint is the sprint: strafing reported 8.2 m/s walking and 8.2
            // sprinting, which looked exactly like the sprint key being ignored
            // sideways -- the bug this test exists to catch.
            bool settled = _frame >= FramesFor(_step) / 3;

            if (settled) _peak = Mathf.Max(_peak, motor.PlanarSpeed01);

            if (motor.IsSprinting && settled && _step == 4) _sprintedStandingStill = true;
            if (motor.IsCrouching && _step == 5) _crouched = true;
            if (!motor.IsGrounded && _step == 6) _leftTheGround = true;
            if (weapon != null && weapon.AimProgress > 0.5f && _step == 10) _aimedDownSights = true;

            // Two steps end on a condition rather than a frame count: the reload when
            // it finishes, and the jump when the player lands.
            bool reloaded = _step == 9 && weapon != null && !weapon.IsReloading &&
                            weapon.CurrentAmmo > _ammoAfterFiring;

            bool landed = _step == 7 && _frame > 5 && motor.IsGrounded;

            if (++_frame < FramesFor(_step) && !reloaded && !landed) return;

            EndBurst(motor, weapon);

            _frame = 0;
            _peak = 0f;

            if (++_step > 10) Finish();
        }

        /// <summary>
        /// Sets the input for one burst. Each pair walks and then sprints in the same
        /// direction from the same place, so a wall in the way cannot make the sprint
        /// look slow without making the walk look slow too.
        /// </summary>
        static void StartBurst(PlayerMotor motor, Weapon weapon)
        {
            MobileInput.Move = Vector2.zero;
            MobileInput.SetSprintButton(false);
            MobileInput.Crouch = MobileInput.Aim = false;
            MobileInput.SetFireButton(false);

            switch (_step)
            {
                case 0: MobileInput.Move = Vector2.up; break;
                case 1: MobileInput.Move = Vector2.up; MobileInput.SetSprintButton(true); break;
                case 2: MobileInput.Move = Vector2.right; break;
                case 3: MobileInput.Move = Vector2.right; MobileInput.SetSprintButton(true); break;

                // Standing still with the sprint key down. The player asked for this to
                // engage, so that the first step out of cover is already at full speed.
                case 4: MobileInput.SetSprintButton(true); break;

                case 5: MobileInput.Move = Vector2.up; MobileInput.Crouch = true; break;
                case 6: MobileInput.QueueJump(); break;

                // Land before anything else is measured. Firing and reloading work in
                // the air, but a test that happens to run them there is a test whose
                // next failure is a puzzle.
                case 7: break;

                case 8:
                    if (weapon != null) _ammoBefore = weapon.CurrentAmmo;
                    MobileInput.SetFireButton(true);
                    break;

                case 9:
                    if (weapon != null) _ammoAfterFiring = weapon.CurrentAmmo;
                    MobileInput.QueueReload();
                    break;

                // Last, and only once the reload has finished: Weapon refuses to aim
                // while IsReloading, so an aim burst started on the heels of a reload
                // measures the reload, not the aim.
                case 10: MobileInput.Aim = true; break;
            }
        }

        static void EndBurst(PlayerMotor motor, Weapon weapon)
        {
            float speed = _peak * motor.sprintSpeed;

            Notes.Append($"\n    step {_step}: peak {speed:0.00} m/s  grounded={motor.IsGrounded} " +
                         $"sprinting={motor.IsSprinting} crouching={motor.IsCrouching} " +
                         $"mult={motor.SpeedMultiplier:0.00} move={MobileInput.Move} " +
                         $"active={MobileInput.Active} dt={Time.deltaTime:0.000} frames={_frame}");

            switch (_step)
            {
                case 0: _walkAhead = speed; break;
                case 1: _sprintAhead = speed; break;
                case 2: _walkSide = speed; break;
                case 3: _sprintSide = speed; break;

                // The one burst measured at its end rather than at its peak. It begins
                // at whatever the sideways sprint before it left behind, and friction
                // sheds that in about ten frames, so the peak would report the previous
                // burst either way. What is being asked here is whether the player is
                // still moving two thirds of a second after the only key down was
                // sprint.
                case 4:
                    _sprintAlone = motor.PlanarSpeed01 * motor.sprintSpeed;
                    _drivesForward = motor.controls == null || motor.controls.sprintDrivesForward;
                    break;

                case 5: _crouchAhead = speed; break;
                case 9: if (weapon != null) _ammoAfterReload = weapon.CurrentAmmo; break;
            }
        }

        // ======================================================================
        static void Detach()
        {
            Time.captureDeltaTime = 0f;

            MobileInput.Reset();
            MobileInput.Active = false;

            if (_tunedLevel != null && _clockBackup > 0f) _tunedLevel.timeLimit = _clockBackup;
            _tunedLevel = null;
            _clockBackup = -1f;

            FPSKitPlayMode.RestoreStartScene();
            EditorApplication.update -= Tick;
        }

        static void Finish()
        {
            Detach();

            var problems = new StringBuilder();
            foreach (var error in Errors) problems.Append($"\n  - {error}");

            Notes.Append($"\n  forward: walk {_walkAhead:0.0} m/s, sprint {_sprintAhead:0.0} m/s");
            Notes.Append($"\n  strafing: walk {_walkSide:0.0} m/s, sprint {_sprintSide:0.0} m/s");
            Notes.Append($"\n  crouched: {_crouchAhead:0.0} m/s");
            Notes.Append($"\n  sprint key alone: {_sprintAlone:0.0} m/s (drives forward: {_drivesForward})");
            Notes.Append($"\n  ammo {_ammoBefore} -> {_ammoAfterFiring} firing -> {_ammoAfterReload} reloaded");

            if (_walkAhead < 0.5f)
                problems.Append("\n  - the player did not move forward at all");

            if (_sprintAhead < _walkAhead * 1.15f)
                problems.Append($"\n  - sprinting forward ({_sprintAhead:0.0} m/s) is no faster than " +
                                $"walking ({_walkAhead:0.0} m/s)");

            if (_walkSide < 0.5f)
                problems.Append("\n  - the player did not strafe at all");

            // The regression this test was written for.
            if (_sprintSide < _walkSide * 1.15f)
                problems.Append($"\n  - sprinting sideways ({_sprintSide:0.0} m/s) is no faster than " +
                                $"walking sideways ({_walkSide:0.0} m/s): the sprint key only works " +
                                "on one of the four directions");

            if (!_sprintedStandingStill)
                problems.Append("\n  - holding sprint while standing still does not engage the sprint, " +
                                "so the first step out of cover is at walking pace");

            // The sprint key with nothing else held has to be a run. A key that only
            // does something in combination with another key is one the player reports
            // as broken, because from the outside it is.
            if (_drivesForward && _sprintAlone < _walkAhead * 0.9f)
                problems.Append($"\n  - the sprint key on its own does not run ({_sprintAlone:0.0} m/s " +
                                $"against a {_walkAhead:0.0} m/s walk): sprint only works while a " +
                                "direction key is also held");

            if (!_crouched)
                problems.Append("\n  - the crouch key did not crouch");
            else if (_crouchAhead >= _walkAhead)
                problems.Append($"\n  - crouching ({_crouchAhead:0.0} m/s) is not slower than walking " +
                                $"({_walkAhead:0.0} m/s)");

            if (!_leftTheGround)
                problems.Append("\n  - the jump key did not get the player off the ground");

            // One round is enough. Whether the gun is single, burst or automatic is
            // WeaponData's business and changes per gun; that the fire control reaches
            // the weapon at all is this test's.
            if (_ammoBefore > 0 && _ammoAfterFiring >= _ammoBefore)
                problems.Append($"\n  - the fire control did not spend a round ({_ammoBefore} -> {_ammoAfterFiring})");

            if (_ammoAfterReload >= 0 && _ammoAfterFiring >= 0 && _ammoAfterReload <= _ammoAfterFiring)
                problems.Append($"\n  - reloading did not refill the magazine " +
                                $"({_ammoAfterFiring} -> {_ammoAfterReload})");

            if (!_aimedDownSights)
                problems.Append("\n  - the aim key did not bring the sights up");

            if (problems.Length > 0)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: the controls{problems}\n\n  what happened:{Notes}");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[FPSKitBatch] OK: every binding is distinct and every action works.{Notes}");
            EditorApplication.Exit(0);
        }
    }
}
#endif
