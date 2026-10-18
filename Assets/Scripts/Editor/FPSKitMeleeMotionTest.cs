#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Attacking never moves the player (Ishaan, 2026-10-18: "my movement comes only from my
    /// movement input"). The rule this proves: whatever is thrown -- rifle butt, punch, double
    /// punch, kick, or a burst of fire, or fire straight after a strike -- standing, walking,
    /// jogging or sprinting, with an enemy in reach or nobody near, the player's root moves
    /// exactly as far as the motor's own velocity says, and that velocity is the one the same
    /// movement input gives with no attack at all.
    ///
    /// It used not to: a strike at a target within 3.6 m stepped the player up to 2.2 m in
    /// (<c>PlayerMotor.Lunge</c>), on top of walking, so walking into an enemy and punching it
    /// surged forward. Measured per frame: the root's horizontal step against the motor's
    /// velocity times the frame. Anything over is a push the player did not ask for.
    ///
    /// Real player from level one of the Warehouse, on a flat floor of the test's own; the level's own enemies are put away and a Grunt (mind off) is placed in reach
    /// for the "target" cases. Touch scheme, so the pad/touch strike magnetism is live too: it
    /// may turn the view and must never move the body.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyMeleeMotion      (or FPSKit > Player > Run Melee Motion Test)
    ///
    /// Report: Build/MeleeMotion/report.txt.
    /// </summary>
    public static class FPSKitMeleeMotionTest
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";
        const string EnemyPrefab = "Assets/FPSKit_Generated/Enemy.prefab";
        const string ReportPath = "Build/MeleeMotion/report.txt";
        const double HardTimeout = 900.0;

        /// <summary>Game seconds of movement before the attack, so walking and running are at speed.</summary>
        const float Accelerate = 0.5f;
        /// <summary>Game seconds measured from the first press: covers the longest chain (butt, jab, cross) and its recovery.</summary>
        const float Window = 1.6f;
        static int WindowFrames => Mathf.RoundToInt(Window * 60f);
        /// <summary>Metres from the player's root to the target's when it is placed: inside punch reach.</summary>
        const float TargetAhead = 1.9f;
        /// <summary>
        /// Metres of forward movement over the trial beyond the motor's own velocity that fail it. Forward
        /// only: walking into the target can lift the controller onto a leg's hitbox, and the motor then
        /// slides it off at 3 m/s (<c>PlayerMotor.OnControllerColliderHit</c>, the never-stand-on-an-enemy
        /// rule), which is away from the enemy and happens with no attack at all. Total excess is reported too.
        /// </summary>
        const float MaxSurge = 0.01f;
        /// <summary>
        /// Metres a second the attack may change the speed the same input gives without it. Not zero: the
        /// pad/touch magnetism turns the view onto the target, and a turn at speed dips the speed a little
        /// as the velocity swings to the new heading (measured 0.05). A lunge shows as 0.5+.
        /// </summary>
        const float MaxSpeedGap = 0.1f;

        public enum Gait { Stand, Walk, Jog, Sprint }
        public enum Act { None, Butt, Punch, DoublePunch, Kick, Fire, ButtThenFire }

        struct Trial
        {
            public Act act;
            public Gait gait;
            public bool target;
        }

        class Result
        {
            public Trial trial;
            public float moved;        // horizontal, first press to end of window
            public float commanded;    // the motor's own velocity integrated over the same frames
            public float excess;       // moved beyond the velocity, summed per frame
            public float surge;        // the same, forward only (towards where the player faces and the target stands)
            public float speedGap = -1f;  // largest per-frame speed difference from the same input with no attack
            public float worstFrame;   // the largest single frame's excess
            public int worstAt = -1;   // and the frame of the window it was in
            public float climb;        // metres of vertical travel over the window
            public string hit = "";
            public readonly List<float> speeds = new List<float>();
        }

        public static bool Running { get; private set; }

        static readonly List<Trial> Trials = new List<Trial>();
        static readonly List<Result> Results = new List<Result>();
        static readonly List<string> Errors = new List<string>();
        static readonly StringBuilder Notes = new StringBuilder();

        static int _index, _stage, _lastFrame, _warm, _presses;
        static float _stageAt, _lastStrikeAt = -99f, _fireUntil = -1f;
        static double _startedAt;
        static Vector3 _start, _dir, _lastPos;
        static GameObject _player, _target;
        static PlayerMotor _motor;
        static MeleeStrike _melee;
        static Weapon _weapon;
        static Result _current;
        static LevelSet.Level _tunedLevel;
        static float _clockBackup = -1f;

        [MenuItem("FPSKit/Player/Run Melee Motion Test")]
        public static void Run()
        {
            if (Running || EditorApplication.isPlaying) return;
            try
            {
                if (EditorSceneManager.GetActiveScene().isDirty)
                    throw new Exception("save the open scene first");
                if (!File.Exists(ScenePath)) throw new Exception($"{ScenePath} does not exist. Build it first.");
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                Trials.Clear(); Results.Clear(); Errors.Clear(); Notes.Clear();
                foreach (Gait g in Enum.GetValues(typeof(Gait)))
                    foreach (bool t in new[] { false, true })
                        foreach (var a in new[] { Act.None, Act.Butt, Act.Punch, Act.DoublePunch, Act.Kick })
                            Trials.Add(new Trial { act = a, gait = g, target = t });
                foreach (Gait g in Enum.GetValues(typeof(Gait)))
                    Trials.Add(new Trial { act = Act.Fire, gait = g });
                foreach (var g in new[] { Gait.Stand, Gait.Jog })
                    foreach (bool t in new[] { false, true })
                        Trials.Add(new Trial { act = Act.ButtThenFire, gait = g, target = t });

                _index = _stage = _warm = 0;
                _lastFrame = -1;
                _player = _target = null;
                _current = null;
                _startedAt = EditorApplication.timeSinceStartup;
                Running = true;

                SaveMigration.Apply();
                FPSKitPlayMode.SuspendStartScene();
                EditorApplication.update += Tick;
                EditorApplication.isPlaying = true;
                Debug.Log($"[FPSKitBatch] melee motion test: {Trials.Count} trials, attacking must never move the player");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: melee motion test: {e.Message}");
                Running = false;
                FPSKitBatch.Exit(1);
            }
        }

        /// <summary>Batch entry: runs, then exits 0 when every trial passed.</summary>
        public static void Batch() => Run();

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _startedAt > HardTimeout)
                    throw new Exception($"timed out at trial {_index} of {Trials.Count}");
                if (!EditorApplication.isPlaying) return;
                if (Time.frameCount == _lastFrame) return;
                int frames = _lastFrame < 0 ? 1 : Time.frameCount - _lastFrame;
                _lastFrame = Time.frameCount;

                var level = Object.FindAnyObjectByType<LevelManager>();
                if (level != null && level.Level != null && _tunedLevel == null)
                {
                    // Long enough to outlive every trial; the shared asset is put back in Detach.
                    _tunedLevel = level.Level;
                    _clockBackup = level.Level.timeLimit;
                    level.Level.timeLimit = 900f;
                    level.ResolveLevel();
                }

                bool warming = level == null || level.IsBriefing || !level.IsRunning || _warm < 20;
                Time.captureDeltaTime = warming ? 0.1f : 1f / 60f;
                if (warming) { if (level != null && level.IsRunning) _warm++; return; }

                if (_player == null && !Setup()) return;
                PutAwayLevelEnemies();
                var health = _player.GetComponent<Health>();
                if (health != null && health.Normalized < 0.9f) health.ResetHealth();

                if (_index >= Trials.Count) { Finish(); return; }
                Step(Trials[_index], frames);
            }
            catch (Exception e)
            {
                Errors.Add("error: " + e.Message);
                Debug.LogException(e);
                Finish();
            }
        }

        static bool Setup()
        {
            _player = GameObject.FindGameObjectWithTag("Player");
            if (_player == null) return false;
            _motor = _player.GetComponent<PlayerMotor>();
            _melee = _player.GetComponent<MeleeStrike>();
            _weapon = _player.GetComponentInChildren<Weapon>();
            if (_motor == null || _melee == null || _weapon == null)
                throw new Exception("the player has no PlayerMotor, MeleeStrike or Weapon");

            MobileInput.Reset();
            MobileInput.Active = true;
            GameInput.SetScheme(InputScheme.Touch);

            BuildFloor(out _start, out _dir);
            Notes.Append($"\n  floor: flat, {FloorSize:0} m square, its own navmesh, {FloorHeight:0} m above the arena; " +
                         $"start {_start:F1} towards {_dir:F2}");
            return true;
        }

        // ------------------------------------------------------------------
        // One trial: settle, place, get up to speed, attack, measure.
        // ------------------------------------------------------------------

        static void Step(Trial t, int frames)
        {
            switch (_stage)
            {
                case 0:   // settle: standing, nothing in flight, the chain gone cold so the first press is the butt
                    SetGait(Gait.Stand);
                    MobileInput.SetFireButton(false);
                    if (_target != null) { Object.Destroy(_target); _target = null; }
                    if (ClearFloor()) return;
                    if (Flat(_motor.Velocity) > 0.02f || _melee.Current != MeleeStrike.Strike.None
                        || _melee.GunAway > 0f || _melee.FistsOut || _weapon.IsLowered || _weapon.IsReloading
                        || Time.time - _lastStrikeAt < 1.6f)
                        return;
                    Place();
                    _weapon.Refill();
                    _stage = 1; _stageAt = Time.time;
                    return;

                case 1:   // let the teleport and the turn land
                    if (Time.time - _stageAt < 0.1f) return;
                    _stage = 2; _stageAt = Time.time;
                    return;

                case 2:   // up to speed
                    SetGait(t.gait);
                    if (Time.time - _stageAt < Accelerate) return;
                    if (t.target) PlaceTarget();
                    _stage = 3;
                    return;

                case 3:   // first press, then measure
                    SetGait(t.gait);
                    Physics.SyncTransforms();
                    _current = new Result { trial = t };
                    _lastPos = _player.transform.position;
                    _presses = 0;
                    _fireUntil = -1f;
                    _origin = _player.transform.position;
                    Press(t);
                    _stageAt = Time.time;
                    _stage = 4;
                    return;

                case 4:
                {
                    SetGait(t.gait);
                    Measure(frames);
                    Press(t);
                    if (_fireUntil > 0f && Time.time >= _fireUntil) MobileInput.SetFireButton(false);
                    if (_current.speeds.Count < WindowFrames) return;

                    MobileInput.SetFireButton(false);
                    _current.moved = Flat(_player.transform.position - _origin);
                    Results.Add(_current);
                    _current = null;
                    _index++;
                    _stage = 0;
                    return;
                }
            }
        }

        static Vector3 _origin;

        /// <summary>The next press of the trial's attack, as soon as the strike before it allows.</summary>
        static void Press(Trial t)
        {
            int wanted = t.act switch
            {
                Act.Butt => 1, Act.Punch => 2, Act.DoublePunch => 3, Act.Kick => 1, Act.ButtThenFire => 1,
                _ => 0,
            };

            if (_presses < wanted)
            {
                if (_presses > 0 && Time.time < _melee.ReadyAt + 0.001f) return;
                var hit = t.act == Act.Kick ? _melee.TryKick() : _melee.TryPunch();
                _current.hit += (_current.hit.Length > 0 ? "," : "") + _melee.Thrown + (hit != null ? "*" : "");
                _lastStrikeAt = Time.time;
                _presses++;
                return;
            }

            if (t.act == Act.Fire && _fireUntil < 0f)
            {
                MobileInput.SetFireButton(true);
                _fireUntil = Time.time + 0.8f;
                _current.hit = "fire";
            }

            // Fire the moment the gun is back in the hands after the butt.
            if (t.act == Act.ButtThenFire && _fireUntil < 0f && _melee.Current == MeleeStrike.Strike.None
                && !_weapon.IsLowered && _melee.GunAway <= 0f)
            {
                MobileInput.SetFireButton(true);
                _fireUntil = Time.time + 0.6f;
                _current.hit += $",fire@{Time.time - _stageAt:0.00}s";
            }
        }

        static void Measure(int frames)
        {
            Vector3 pos = _player.transform.position;
            float step = Flat(pos - _lastPos);
            float speed = Flat(_motor.Velocity);
            float allowed = speed * Time.deltaTime * frames;
            _current.commanded += allowed;
            float over = Mathf.Max(0f, step - allowed);
            _current.excess += over;
            // A push is a frame that went both further than the body's speed and further forward than
            // its velocity: either alone is ordinary collide-and-slide (a step blocked, or turned along a
            // body it ran into), and the lunge was both, 200 mm in one frame.
            Vector3 v = _motor.Velocity;
            float forward = Mathf.Max(0f, Vector3.Dot(pos - _lastPos, _dir) - Vector3.Dot(new Vector3(v.x, 0f, v.z), _dir) * Time.deltaTime * frames);
            _current.surge += Mathf.Min(forward, over);
            if (over > _current.worstFrame) { _current.worstFrame = over; _current.worstAt = _current.speeds.Count; }
            _current.climb += Mathf.Abs(pos.y - _lastPos.y);
            _current.speeds.Add(speed);
            _lastPos = pos;
        }

        static void SetGait(Gait g)
        {
            MobileInput.Move = g switch
            {
                Gait.Walk => new Vector2(0f, 0.5f),
                Gait.Jog or Gait.Sprint => new Vector2(0f, 1f),
                _ => Vector2.zero,
            };
            MobileInput.SetSprintButton(g == Gait.Sprint);
        }

        static void Place()
        {
            var cc = _player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            _player.transform.position = _start + Vector3.up * 0.05f;
            if (cc != null) cc.enabled = true;
            Physics.SyncTransforms();
            Vector3 eye = _start + Vector3.up * 1.6f;
            _motor.TurnTowards(eye + _dir * 20f, 1f);
            _lastPos = _player.transform.position;
        }

        /// <summary>A Grunt, mind off, standing <see cref="TargetAhead"/> in front of the player (where it will be next frame).</summary>
        static void PlaceTarget()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
            if (prefab == null) throw new Exception(EnemyPrefab + " is missing");
            Vector3 fwd = Vector3.ProjectOnPlane(_player.transform.forward, Vector3.up).normalized;
            Vector3 at = _player.transform.position + _motor.Velocity * Time.deltaTime + fwd * TargetAhead;
            if (NavMesh.SamplePosition(at, out var hit, 1f, NavMesh.AllAreas)) at = hit.position;
            _target = Object.Instantiate(prefab, at, Quaternion.LookRotation(-fwd));
            _target.name = "MotionTest_Target";
            FPSKitEnemyRoster.GetOrCreate("Grunt").ApplyTo(_target);
            var ai = _target.GetComponent<EnemyAI>();
            if (ai != null) ai.enabled = false;
            var brain = _target.GetComponent<EnemyBrain>();
            if (brain != null) brain.enabled = false;
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Whatever an earlier trial's target left on the floor -- its dropped rifle, a severed piece,
        /// a corpse -- is taken away before the next trial: walking over a dropped rifle lifts the
        /// controller onto it and moves the body further in a frame than its velocity. True while
        /// there was something to take (destroyed at the end of the frame, so wait one).
        /// </summary>
        static bool ClearFloor()
        {
            bool any = false;
            foreach (var root in _player.scene.GetRootGameObjects())
            {
                if (root == _player || root == _floor || root.transform.position.y < FloorHeight - 20f) continue;
                if (root.GetComponentInChildren<Camera>() != null) continue;
                Object.Destroy(root);
                any = true;
            }
            return any;
        }

        /// <summary>The level's own enemies are put away every frame, so nothing shoots or shoves the player.</summary>
        static void PutAwayLevelEnemies()
        {
            foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                if (_target == null || ai.gameObject != _target) ai.gameObject.SetActive(false);
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        const float FloorHeight = 400f;
        const float FloorSize = 80f;
        static GameObject _floor;

        /// <summary>
        /// A flat floor of the test's own, with a navmesh baked on it now, high over the arena: a built
        /// arena's ground has kerbs, seams and pads, and the controller stepping onto one moves the
        /// body further in a frame than its velocity (measured: up to 46 mm), which would hide the
        /// thing being measured. Nothing on it but the player and the target.
        /// </summary>
        static void BuildFloor(out Vector3 start, out Vector3 dir)
        {
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "MotionTest_Floor";
            _floor.transform.position = new Vector3(0f, FloorHeight - 0.5f, 0f);
            _floor.transform.localScale = new Vector3(FloorSize, 1f, FloorSize);
            int env = LayerMask.NameToLayer("Environment");
            if (env >= 0) _floor.layer = env;
            var surface = _floor.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            surface.collectObjects = Unity.AI.Navigation.CollectObjects.Children;
            surface.BuildNavMesh();
            start = new Vector3(0f, FloorHeight, -FloorSize * 0.4f);
            dir = Vector3.forward;
        }

        // ------------------------------------------------------------------

        static Result Control(Trial t)
        {
            foreach (var r in Results)
                if (r.trial.act == Act.None && r.trial.gait == t.gait && r.trial.target == t.target) return r;
            return null;
        }

        static void Finish()
        {
            var sb = new StringBuilder();
            sb.Append("Melee motion: the player's horizontal movement from the first press to ")
              .Append($"{Window:0.0} s later, against the motor's own velocity and against the same input with no attack.\n")
              .Append("surge = metres moved both further than the speed and further forward than the velocity, per frame, summed (a push the player did not ask for; judged); ")
              .Append("excess = the same in any direction (includes sliding off an enemy's hitbox); ")
              .Append("speed gap = largest per-frame speed difference from no attack. * = landed.\n")
              .Append(Notes).Append('\n')
              .Append($"\n  {"act",-13}{"gait",-8}{"target",-8}{"moved m",9}{"input-only m",14}{"diff m",9}{"surge m",9}{"excess m",10}{"speed gap",11}{"worst frame",14}{"climb",6}  strikes\n");

            int failed = 0;
            foreach (var r in Results)
            {
                var c = Control(r.trial);
                if (c != null && r.trial.act != Act.None)
                {
                    float gap = 0f;
                    for (int i = 0; i < Mathf.Min(r.speeds.Count, c.speeds.Count); i++)
                        gap = Mathf.Max(gap, Mathf.Abs(r.speeds[i] - c.speeds[i]));
                    r.speedGap = gap;
                }

                // Firing at a sprint is the combat jog by design (fire held turns the sprint into a jog
                // with the gun up), so its speed is not the no-attack sprint's; it still may not push.
                bool speedJudged = r.speedGap >= 0f && !(r.trial.gait == Gait.Sprint && (r.trial.act == Act.Fire || r.trial.act == Act.ButtThenFire));
                bool ok = r.surge <= MaxSurge && (!speedJudged || r.speedGap <= MaxSpeedGap);
                if (!ok) failed++;

                string control = c != null && r.trial.act != Act.None ? $"{c.moved,14:0.000}{r.moved - c.moved,9:+0.000;-0.000;0.000}" : $"{"",14}{"",9}";
                string gapText = r.speedGap >= 0f ? (speedJudged ? $"{r.speedGap,11:0.000}" : $"{"(jog)",11}") : $"{"",11}";
                sb.Append($"  {r.trial.act,-13}{r.trial.gait,-8}{(r.trial.target ? "yes" : "no"),-8}{r.moved,9:0.000}{control}{r.surge,9:0.000}{r.excess,10:0.000}{gapText}{r.worstFrame * 1000f,8:0.0}mm@{r.worstAt,-3}{r.climb,7:0.000}  {r.hit}{(ok ? "" : "   <-- FAIL")}\n");
            }

            foreach (var e in Errors) sb.Append("\n  ERROR ").Append(e);
            if (Results.Count < Trials.Count) { failed++; sb.Append($"\n  only {Results.Count} of {Trials.Count} trials ran"); }

            string verdict = failed == 0 && Errors.Count == 0
                ? $"melee motion test passed: {Results.Count} trials, no attack moved the player"
                : $"melee motion test FAILED: {failed} trial(s) moved the player beyond its own movement";
            string report = verdict + "\n" + sb;

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report);
            if (failed == 0 && Errors.Count == 0) Debug.Log("[FPSKitBatch] " + report);
            else Debug.LogError("[FPSKitBatch] FAILED: " + report);

            Detach();
            FPSKitBatch.Exit(failed == 0 && Errors.Count == 0 ? 0 : 1);
        }

        static void Detach()
        {
            EditorApplication.update -= Tick;
            if (_tunedLevel != null && _clockBackup > 0f) _tunedLevel.timeLimit = _clockBackup;
            _tunedLevel = null;
            _clockBackup = -1f;
            if (_floor != null) Object.Destroy(_floor);
            _floor = null;
            MobileInput.Reset();
            MobileInput.Active = false;
            GameInput.SetScheme(InputScheme.KeyboardMouse);
            Time.captureDeltaTime = 0f;
            Running = false;
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += FPSKitPlayMode.RestoreStartScene;
        }
    }
}
#endif
