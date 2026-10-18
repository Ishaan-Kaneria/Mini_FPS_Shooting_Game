#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The limb test (Docs/ENEMY_AI_PLAN.md, Stage 1): real enemy prefabs in the AI test
    /// scene, shot through their real hitboxes with the Service Rifle's 24 damage, kicked the
    /// way the player's kick throws them, and watched. It proves the Stage 1 rules:
    ///
    /// - A: 20 rounds in one thigh -- alive, at or above the core floor, the leg crippled,
    ///   limping on that side, still fighting; the limp moves at the clip's speed.
    /// - B: both legs crippled -- down and crawling, and still crawling after a kick: it
    ///   never stands up again.
    /// - C: the gun arm crippled -- still armed, one-handed; then the other arm -- disarmed.
    /// - D: kicked three times on sound legs -- each lie a different length, the get-up
    ///   clip matching the way it landed, no snap when the ragdoll is blended into the clip.
    /// - E: the torso still kills. A's head still kills.
    ///
    /// Runs in the open editor and never exits it. Report and small pictures in Build/LimbTest.
    /// </summary>
    public static class FPSKitLimbTest
    {
        const string Prefab = "Assets/FPSKit_Generated/Enemy.prefab";
        const string Folder = "Build/LimbTest";
        const float Rifle = 24f;

        public static bool Running { get; private set; }
        public static string Report { get; private set; } = "";

        static int _step;
        static double _realStart;
        static string _returnTo;
        static Camera _cam;
        static GameObject _player;
        static GameObject _a, _b, _c, _d, _e;
        static readonly StringBuilder _log = new StringBuilder();
        static readonly List<string> _fails = new List<string>();
        static List<(float at, Action act)> _script;
        static int _next;
        static float _t0;
        static int _shot;

        // Watching B after it is down, D's falls and rises, A's limp speed.
        static float _bMaxHips = 0f;
        static bool _bWatching;
        static int _dKicks;
        static float _dKickAt = -1f, _dRiseAt = -1f;
        static bool _dWasRising, _dWasDown;
        static float _dMaxHipsStep, _dMaxBoneStep, _dMaxJerk, _dMaxSpin, _dJerkAt, _dSpinAt;
        static string _dSpinBone = "", _dSpinRise = "", _dSpinWhen = "";
        static float _dRiseEnd = -1f;
        static Vector3 _dLastHips, _dLastStep;
        static float[] _dLastSpin;
        static Quaternion[] _dLastBones;
        static Transform[] _dBones;
        static readonly List<string> _dFalls = new List<string>();
        static float _aMoveTime, _aMoveDist;
        static Vector3 _aLast;

        static bool _fallsOnly, _poseOnly;

        /// <summary>Stage 2's quick look: the rifle held, the aim, a walk across the player, in pictures.</summary>
        [MenuItem("FPSKit/Enemy AI/Run Pose Check")]
        public static void RunPose() { _poseOnly = true; Run(false); }

        [MenuItem("FPSKit/Enemy AI/Run Limb Test")]
        public static void Run() { _poseOnly = false; Run(false); }

        /// <summary>Only D's three kicks and get-ups: the slow part, on its own.</summary>
        [MenuItem("FPSKit/Enemy AI/Run Limb Test (Falls Only)")]
        public static void RunFalls() => Run(true);

        /// <summary>
        /// Batch entries, exiting with the result: Tools/unity-batch.sh FPSKit.EditorTools.FPSKitLimbTest.BatchFalls
        /// (or .Batch for the whole test). Report in Build/LimbTest/report.txt.
        /// </summary>
        public static void BatchFalls() { _exitWhenDone = true; _poseOnly = false; Run(true); }
        public static void Batch() { _exitWhenDone = true; _poseOnly = false; Run(false); }
        static bool _exitWhenDone;

        public static void Run(bool fallsOnly)
        {
            _fallsOnly = fallsOnly;
            if (fallsOnly) _poseOnly = false;
            if (Running || EditorApplication.isPlaying) return;
            _returnTo = EditorSceneManager.GetActiveScene().path;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            FPSKitAITestScene.Build();
            Directory.CreateDirectory(Folder);
            _log.Clear(); _fails.Clear(); Report = "";
            _step = 0; _shot = 0; _next = 0;
            _bMaxHips = 0f; _bWatching = false;
            _dKicks = 0; _dKickAt = -1f; _dRiseAt = -1f; _dWasRising = false; _dWasDown = false;
            _dMaxHipsStep = 0f; _dMaxBoneStep = 0f; _dMaxJerk = 0f; _dMaxSpin = 0f; _dFalls.Clear();
            _rifleLog.Clear(); _rifleBad = 0; _rifleInHandWhileDown = false; _rifleUpAt = -1f;
            _aMoveTime = 0f; _aMoveDist = 0f;
            FPSKitPlayMode.SuspendStartScene();
            _realStart = EditorApplication.timeSinceStartup;
            _watchedFrame = -1;
            Running = true;
            EditorApplication.update += Tick;
        }

        static int _watchedFrame = -1;

        static void Note(string s) { _log.AppendLine(s); }
        static void Require(bool ok, string what) { Note((ok ? "  PASS  " : "  FAIL  ") + what); if (!ok) _fails.Add(what); }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _realStart > 2400) throw new Exception("limb test timed out (40 min)");
                switch (_step)
                {
                    case 0:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        _step = 1;
                        return;
                    case 1:
                        Setup();
                        Time.captureDeltaTime = 1f / 30f;
                        _t0 = Time.time;
                        _script = Script();
                        _step = 2;
                        return;
                    case 2:
                        // Once per game frame: two editor ticks on one frame read as a stop and then
                        // a double step, a "pop" of the sampling rather than the body (FPSKitGetUpProbe).
                        if (Time.frameCount == _watchedFrame) return;
                        _watchedFrame = Time.frameCount;
                        Watch();
                        float t = Time.time - _t0;
                        while (_next < _script.Count && t >= _script[_next].at) _script[_next++].act();
                        if (_fallsOnly && _dFalls.Count >= 3 && _rifleLog.Count >= 3 && _d != null && !_d.GetComponent<EnemyBody>().IsDown && _next < _script.Count)
                            _script[_next++].act();
                        if (_next >= _script.Count) Finish();
                        return;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _fails.Add("error: " + e.Message);
                Finish();
            }
        }

        static void Setup()
        {
            _player = GameObject.FindGameObjectWithTag("Player");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            var grunt = FPSKitEnemyRoster.GetOrCreate("Grunt");
            GameObject Spawn(string name, float x)
            {
                var at = new Vector3(x, 0f, 14f);
                var go = Object.Instantiate(prefab, at, Quaternion.LookRotation(-at.normalized));
                go.name = name;
                grunt.ApplyTo(go);
                return go;
            }
            _a = _b = _c = _e = null;
            if (_poseOnly)
            {
                _d = null;
                _a = Spawn("A_Alert", -2f);
                _c = Spawn("C_Walker", 6f);
                _b = Spawn("B_GunArmGone", 2f);
                _e = Spawn("E_OffArmGone", -6f);
                var camGo0 = new GameObject("TestCam");
                _cam = camGo0.AddComponent<Camera>();
                _cam.fieldOfView = 45f;
                _cam.enabled = false;
                return;
            }
            _d = Spawn("D_Kicks", 4.5f);
            // The rage after a kick is its own move (fists, a charge); the falls test judges
            // the get-up and the hand-over to standing, so D gets up calm.
            _d.GetComponent<EnemyBody>().rageSeconds = 0f;
            if (!_fallsOnly)
            {
                _a = Spawn("A_LegShots", -9f);
                _b = Spawn("B_BothLegs", -4.5f);
                _c = Spawn("C_Arms", 0f);
                _e = Spawn("E_Torso", 9f);
            }

            var camGo = new GameObject("TestCam");
            _cam = camGo.AddComponent<Camera>();
            _cam.fieldOfView = 50f;
            _cam.enabled = false;

            var anim = _d.GetComponentInChildren<Animator>();
            var ids = new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head,
                              HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg,
                              HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm };
            _dBones = Array.ConvertAll(ids, b => anim.GetBoneTransform(b));
            _dLastBones = new Quaternion[_dBones.Length];
            _dLastSpin = new float[_dBones.Length];
            if (_a != null) _aLast = _a.transform.position;
        }

        static List<(float, Action)> Script()
        {
            var s = new List<(float, Action)>();
            if (_poseOnly)
            {
                // A stands alert; C walks a line across in front of the player.
                s.Add((0.5f, () => { Note("Pose check"); _poseStart = _c.transform.position; _walkLastT = 0f; }));
                // The walker is sent, then left alone: its mind and its fighting rules switched
                // off so nothing else moves it. The motor walks it by the clip.
                s.Add((0.6f, () =>
                {
                    var ai = _c.GetComponent<EnemyAI>();
                    ai.MoveTo(new Vector3(-10f, 0f, 9f), 0.55f);
                    _c.GetComponent<EnemyBrain>().enabled = false;
                    ai.enabled = false;
                }));
                s.Add((3.0f, () => { PictureFrom(_a, "P_alert_front", 0f, 2.6f); PictureFrom(_a, "P_alert_side", 90f, 2.4f); }));
                s.Add((4.0f, () => { PictureFrom(_c, "P_walk_side", 90f, 3.2f); PictureFrom(_c, "P_walk_front", 20f, 3.2f); }));
                for (int i = 1; i <= 11; i++) s.Add((0.5f * i + 0.6f, LogWalker));
                for (int i = 0; i < 20; i++) { s.Add((0.6f + i * 0.05f, () => Hit(_b, BodyPart.RightArm))); s.Add((0.6f + i * 0.05f, () => Hit(_e, BodyPart.LeftArm))); }
                s.Add((5.0f, () => { PictureFrom(_b, "P_pistol_left_hand", 30f, 2.4f); PictureFrom(_e, "P_pistol_right_hand", -30f, 2.4f); }));
                s.Add((5.2f, CheckSidearms));
                s.Add((6.0f, CheckPose));
                s.Add((6.3f, () => PictureFrom(_a, "P_alert_front_late", 0f, 2.6f)));
                s.Sort((x, y) => x.Item1.CompareTo(y.Item1));
                return s;
            }
            if (_fallsOnly)
            {
                s.Add((1f, () => Kick(_d)));
                s.Add((45f, CheckFalls));
                s.Sort((x, y) => x.Item1.CompareTo(y.Item1));
                return s;
            }
            // A: twenty rounds through the left thigh, a tenth of a second apart.
            for (int i = 0; i < 20; i++) s.Add((1f + i * 0.1f, () => Hit(_a, BodyPart.LeftLeg)));
            // B: the left thigh and the right shin until both are crippled.
            for (int i = 0; i < 6; i++) s.Add((1f + i * 0.1f, () => Hit(_b, BodyPart.LeftLeg)));
            for (int i = 0; i < 7; i++) s.Add((1.7f + i * 0.1f, () => Hit(_b, BodyPart.RightShin)));
            // C: twenty through the gun arm.
            for (int i = 0; i < 20; i++) s.Add((1f + i * 0.1f, () => Hit(_c, BodyPart.RightArm)));
            // E: the chest.
            for (int i = 0; i < 6; i++) s.Add((1f + i * 0.1f, () => Hit(_e, BodyPart.Torso)));
            // D: the first kick.
            s.Add((1f, () => Kick(_d)));

            s.Add((3.6f, CheckLegsAndArms));
            for (int i = 0; i < 8; i++) s.Add((4f + i * 0.1f, () => Hit(_c, BodyPart.LeftForearm)));
            s.Add((5.2f, CheckDisarmed));
            s.Add((6.0f, () => Picture(_a, "A_limping")));
            s.Add((7.0f, () => { _bWatching = true; Picture(_b, "B_crawling"); }));
            s.Add((9.0f, () => { Kick(_b); Note($"  B kicked while crawling (t=9)"); }));
            s.Add((20.0f, CheckCrawler));
            s.Add((20.5f, () => Picture(_b, "B_after_kick")));
            s.Add((21.0f, () => { Hit(_a, BodyPart.Head); }));
            s.Add((21.5f, () => Require(_a.GetComponent<Health>().IsDead, "A: a headshot still kills a crippled grunt")));
            // D needs time for three falls; wait for them (or give up at 40 s).
            s.Add((40.0f, CheckFalls));
            // In time order: the runner fires each entry once its moment has come.
            s.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            return s;
        }

        static void Hit(GameObject enemy, BodyPart part)
        {
            if (enemy == null) return;
            foreach (var box in enemy.GetComponentsInChildren<Hitbox>())
            {
                if (box.Part != part) continue;
                var col = box.GetComponent<Collider>();
                Vector3 point = col.bounds.center;
                Vector3 dir = (point - (_player.transform.position + Vector3.up * 0.5f)).normalized;
                box.Receive(new DamageInfo(Rifle, point, -dir, dir, _player));
                return;
            }
            _fails.Add($"{enemy.name} has no {part} hitbox");
        }

        static void Kick(GameObject enemy)
        {
            var ai = enemy.GetComponent<EnemyAI>();
            Vector3 away = enemy.transform.position - _player.transform.position;
            away.y = 0f;
            ai.Knock((away.normalized + Vector3.up * 0.25f) * 7.5f, 1.5f);
            if (enemy == _d)
            {
                _dKickAt = Time.time; _dKicks++;
                var rifle = _d.GetComponent<EnemyRifle>();
                _rifleNow = $"fall {_dKicks}: rifle {(rifle != null ? rifle.State.ToString().ToLower() : "none")}";
                _rifleChestMax = 0f; _rifleRest = -1f; _rifleUpAt = -1f; _rifleShot = false;
            }
        }

        static void CheckLegsAndArms()
        {
            Note("Legs (A: 20 rifle rounds, 24 damage each, into the left thigh):");
            var h = _a.GetComponent<Health>();
            var w = _a.GetComponent<EnemyWounds>();
            var body = _a.GetComponent<EnemyBody>();
            Note($"  health {h.Current:0.0}/{h.maxHealth:0} ({h.Normalized * 100f:0}%), left leg {w.State(BodyPart.LeftLeg)}, legs {w.Legs}, pain {w.Pain:0.00}, injured state {body.InjuredState}");
            Require(!h.IsDead, "A: alive after 20 leg rounds");
            Require(h.Normalized >= 0.149f, "A: core health held at the 15% floor or above");
            Require(w.State(BodyPart.LeftLeg) >= EnemyWounds.LimbState.Crippled, "A: the left leg is crippled");
            Require(w.Legs == EnemyWounds.LegState.Limping, "A: limping (not crawling) on one crippled leg");
            Require(body.InjuredState == "LimpLeft", $"A: limping on the left (state {body.InjuredState})");
            Require(_a.GetComponent<EnemyAI>().ranged, "A: still armed and fighting");
            var brain = _a.GetComponent<EnemyBrain>();
            Note($"  A's mind: {brain.Mind}, doing '{brain.Doing}'");
            Require(brain.Mind >= EnemyBrain.Awareness.Searching, "A: alerted (searching or fighting) after being shot");

            Note("Both legs (B):");
            var wb = _b.GetComponent<EnemyWounds>();
            Note($"  left {wb.State(BodyPart.LeftLeg)}, right {wb.State(BodyPart.RightLeg)}, legs {wb.Legs}, health {_b.GetComponent<Health>().Normalized * 100f:0}%");
            Require(wb.Legs == EnemyWounds.LegState.Crawling, "B: both legs crippled puts it on the ground");

            Note("Gun arm (C: 20 rounds into the right upper arm):");
            var wc = _c.GetComponent<EnemyWounds>();
            var hc = _c.GetComponent<Health>();
            Note($"  right arm {wc.State(BodyPart.RightArm)}, one-handed {wc.OneHanded}, spread x{wc.AimSpreadMultiplier:0.0}, fire interval x{wc.FireIntervalMultiplier:0.0}, health {hc.Normalized * 100f:0}%");
            Require(!hc.IsDead && hc.Normalized >= 0.149f, "C: alive and at or above the floor after 20 arm rounds");
            Require(_c.GetComponent<EnemyAI>().ranged, "C: one crippled arm leaves it armed (the pistol in the good hand, or the rifle low)");
            Require(wc.AimSpreadMultiplier > 1.4f, "C: and shoots worse than a sound soldier");

            Note("Torso (E: 6 rounds in the chest):");
            Require(_e.GetComponent<Health>().IsDead, "E: torso rounds still kill");
        }

        static void CheckDisarmed()
        {
            var wc = _c.GetComponent<EnemyWounds>();
            Note($"  C after 8 more in the left forearm: left arm {wc.State(BodyPart.LeftArm)}, disarmed {wc.Disarmed}, ranged {_c.GetComponent<EnemyAI>().ranged}, brain: {_c.GetComponent<EnemyBrain>().Doing}");
            Require(wc.Disarmed && !_c.GetComponent<EnemyAI>().ranged, "C: both arms crippled drops the rifle");
        }

        static void CheckCrawler()
        {
            var wb = _b.GetComponent<EnemyWounds>();
            var body = _b.GetComponent<EnemyBody>();
            Note($"Crawler (B) from t=7 to t=20, kicked at t=9: legs {wb.Legs}, state {body.InjuredState}, highest pelvis {_bMaxHips:0.00} m, brain: {_b.GetComponent<EnemyBrain>().Doing}");
            Require(wb.Legs == EnemyWounds.LegState.Crawling, "B: still crawling after the kick");
            Require(body.InjuredState == "CrawlDown" || body.InjuredState == "CrawlUp", "B: back in the crawl after the kick, never a get-up");
            Require(_bMaxHips < 0.75f, $"B: never stood up (pelvis stayed under 0.75 m; highest {_bMaxHips:0.00})");
        }

        static void CheckFalls()
        {
            Note("Kicks (D):");
            foreach (var f in _dFalls) Note("  " + f);
            Note($"  each get-up, from the ragdoll to 0.6 s after standing, 30 fps: fastest pelvis {_dMaxHipsStep * 100f:0.0} cm/frame, fastest bone {_dMaxBoneStep:0.0} deg/frame");
            Note($"  biggest sudden change: pelvis {_dMaxJerk * 100f:0.0} cm/frame at {_dJerkAt:0.00} s in, a bone {_dMaxSpin:0.0} deg/frame at {_dSpinAt:0.00} s in ({_dSpinBone}, {_dSpinRise}, {_dSpinWhen})");
            Require(_dFalls.Count >= 2, $"D: got up from at least two kicks ({_dFalls.Count})");
            Note("  The rifle in each fall:");
            foreach (var r in _rifleLog) Note("    " + r);
            Require(_rifleLog.Count >= 2, $"D: the rifle came back (or was given up) after at least two falls ({_rifleLog.Count})");
            Require(_rifleBad == 0, "D: the slung rifle stayed within 60 cm of the chest and a dropped one lay within 20 cm of the ground");
            Require(!_rifleInHandWhileDown, "D: the rifle never stayed fixed to the hand while it lay on the ground");
            // 8 cm: just above the sharpest the get-up clips are themselves (Zombie Stand Up lurches
            // its pelvis 6.9 cm/frame at 30 fps, by design). Anything past it is ours.
            Require(_dMaxJerk < 0.08f, "D: no pelvis pop (its frame-to-frame speed never changes by 8 cm/frame or more, the clips' own sharpest)");
            // 25: just above the sharpest the get-up clips are themselves (GetUp_Stomach_Zombie
            // changes a bone's turn by 23.5 deg/frame at 30 fps). Anything past it is ours.
            Require(_dMaxSpin < 25f, "D: no bone pop (no bone's frame-to-frame turn changes by 25 deg/frame or more, the clips' own sharpest)");
            if (_aMoveTime > 0.5f)
                Note($"A limped {_aMoveDist:0.0} m in {_aMoveTime:0.0} s on the move: {_aMoveDist / _aMoveTime:0.00} m/s (clip at this size: ~1.24 walk, ~2.1 run)");
        }

        /// <summary>Every frame: B's pelvis once it is in the crawl, D's falls and blends, A's limp speed.</summary>
        static void Watch()
        {
            var bBody = _b != null ? _b.GetComponent<EnemyBody>() : null;
            if (_bWatching && bBody != null && !bBody.IsDown)
            {
                var hips = _b.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Hips);
                _bMaxHips = Mathf.Max(_bMaxHips, hips.position.y - _b.transform.position.y);
            }

            if (_a != null && !_a.GetComponent<Health>().IsDead)
            {
                var motor = _a.GetComponent<EnemyMotor>();
                float step = Vector3.Distance(_a.transform.position, _aLast);
                if (motor != null && motor.RootMotion && step > 0.001f) { _aMoveTime += Time.deltaTime; _aMoveDist += step; }
                _aLast = _a.transform.position;
            }

            var d = _d != null ? _d.GetComponent<EnemyBody>() : null;
            if (d == null) return;
            WatchRifle(d);
            bool rising = d.Rising;
            if (!rising && _dWasRising) _dRiseEnd = Time.time;
            if (rising && !_dWasRising) _dRiseEnd = -1f;
            if (rising && !_dWasRising)
            {
                _dRiseAt = Time.time;
                _dFalls.Add($"fall {_dKicks}: on its {(d.LastFallFaceUp ? "back" : "front")}, chose to lie {d.LastLieSeconds:0.0} s, " +
                            $"down {Time.time - _dKickAt:0.0} s in all, got up with {d.LastRise}");
                if (_dKicks == 1) _pendingPictures = 2;
                for (int i = 0; i < _dBones.Length; i++) { _dLastBones[i] = _dBones[i].rotation; _dLastSpin[i] = 0f; }
                _dLastHips = _dBones[0].position;
                _dLastStep = Vector3.zero;
            }
            else if (rising || (_dRiseEnd > 0f && Time.time - _dRiseEnd < 0.6f))
            {
                // A pop is a sudden change of speed, not speed: a quick get-up moves the pelvis
                // fast, smoothly. Measured as the change in each frame's step from the last.
                float since = Time.time - _dRiseAt;
                Vector3 step = _dBones[0].position - _dLastHips;
                _dMaxHipsStep = Mathf.Max(_dMaxHipsStep, step.magnitude);
                float jerk = (step - _dLastStep).magnitude;
                if (jerk > _dMaxJerk) { _dMaxJerk = jerk; _dJerkAt = since; }
                _dLastStep = step;
                for (int i = 0; i < _dBones.Length; i++)
                {
                    float spin = Quaternion.Angle(_dBones[i].rotation, _dLastBones[i]);
                    _dMaxBoneStep = Mathf.Max(_dMaxBoneStep, spin);
                    float change = Mathf.Abs(spin - _dLastSpin[i]);
                    if (change > _dMaxSpin) { _dMaxSpin = change; _dSpinAt = since; _dSpinBone = _dBones[i].name; _dSpinRise = d.LastRise; _dSpinWhen = rising ? "while rising" : "just after"; }
                    _dLastSpin[i] = spin;
                    _dLastBones[i] = _dBones[i].rotation;
                }
                _dLastHips = _dBones[0].position;
                if (_pendingPictures == 2 && Time.time - _dRiseAt > 0.25f) { Picture(_d, "D_getting_up_blend"); _pendingPictures = 1; }
            }
            if (_pendingPictures == 1 && rising && Time.time - _dRiseAt > 1.6f) { Picture(_d, "D_getting_up"); _pendingPictures = 0; }

            // Up again: kick it once more, after a breath.
            bool down = d.IsDown;
            if (_dWasDown && !down && _dKicks < 3) _nextKick = Time.time + 1.5f;
            var dRifle = _d.GetComponent<EnemyRifle>();
            bool rifleSettled = dRifle == null || dRifle.State == EnemyRifle.Hold.Held || dRifle.Lost;
            if (_nextKick > 0f && Time.time >= _nextKick && rifleSettled) { _nextKick = -1f; Kick(_d); }
            _dWasDown = down;
            _dWasRising = rising;
        }

        static int _pendingPictures;
        static string _rifleNow = "";
        static float _rifleChestMax, _rifleRest = -1f, _rifleUpAt = -1f;
        static bool _rifleShot, _rifleInHandWhileDown;
        static readonly List<string> _rifleLog = new List<string>();
        static int _rifleBad;

        /// <summary>D's rifle through each fall: on the sling or on the ground while it lies, then back in the hands.</summary>
        static void WatchRifle(EnemyBody d)
        {
            var rifle = _d.GetComponent<EnemyRifle>();
            if (rifle == null || d.gun == null) return;
            var anim = _d.GetComponentInChildren<Animator>();
            var chest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
            if (d.IsDown && !d.Rising)
            {
                if (rifle.State == EnemyRifle.Hold.Slung) _rifleChestMax = Mathf.Max(_rifleChestMax, Vector3.Distance(d.gun.position, chest.position));
                if (rifle.State == EnemyRifle.Hold.Dropped)
                {
                    var rb = d.gun.GetComponent<Rigidbody>();
                    if (rb != null && rb.linearVelocity.sqrMagnitude < 0.01f
                        && Physics.Raycast(d.gun.position + Vector3.up * 0.5f, Vector3.down, out var hit, 3f, 1 << LayerMask.NameToLayer("Environment")))
                        _rifleRest = d.gun.position.y - hit.point.y;
                }
                if (d.gun.IsChildOf(_d.transform) && rifle.State != EnemyRifle.Hold.OnBack) _rifleInHandWhileDown = true;
                if (!_rifleShot && Time.time - _dKickAt > 2.2f) { _rifleShot = true; Picture(_d, $"D_fall{_dKicks}_rifle_{rifle.State.ToString().ToLower()}"); }
            }
            if (!d.IsDown && _dKickAt > 0f && _rifleUpAt < 0f && (rifle.State == EnemyRifle.Hold.Held || rifle.Lost))
            {
                _rifleUpAt = Time.time;
                string rest = _rifleChestMax > 0f ? $"hung up to {_rifleChestMax * 100f:0} cm from the chest" : _rifleRest >= 0f ? $"lay {_rifleRest * 100f:0} cm above the ground" : "no reading on the ground";
                string after = rifle.Lost ? "went without it" : $"held again {Time.time - _dKickAt:0.0} s after the kick";
                _rifleLog.Add($"{_rifleNow}; {rest}; {after}");
                if (_rifleChestMax > 0.6f) _rifleBad++;
                if (_rifleRest > 0.2f) _rifleBad++;
            }
        }
        static float _nextKick = -1f;

        static Vector3 _poseStart, _walkLast;
        static float _walkLastT;

        static void LogWalker()
        {
            var anim = _c.GetComponentInChildren<Animator>();
            var agent = _c.GetComponent<UnityEngine.AI.NavMeshAgent>();
            float dt = Time.time - _walkLastT;
            float v = _walkLastT > 0f && dt > 0f ? Vector3.Distance(_c.transform.position, _walkLast) / dt : 0f;
            _walkLast = _c.transform.position; _walkLastT = Time.time;
            var st = anim.GetCurrentAnimatorStateInfo(0);
            Note($"    t={Time.time - _t0:0.0}: {v:0.00} m/s, MoveX {anim.GetFloat("MoveX"):0.00} MoveZ {anim.GetFloat("MoveZ"):0.00}, base state {(st.IsName("Move") ? "Move" : st.IsName("TurnLeft") ? "TurnLeft" : st.IsName("TurnRight") ? "TurnRight" : st.IsName("RunStart") ? "RunStart" : st.IsName("RunStop") ? "RunStop" : "other")}, remaining {agent.remainingDistance:0.0} m, stopped {agent.isStopped}, path {agent.hasPath}, animator speed {anim.speed:0.00}");
        }

        static void CheckSidearms()
        {
            var b = _b.GetComponent<EnemyBody>();
            var e = _e.GetComponent<EnemyBody>();
            Note($"  gun arm gone (B): pistol hand {b.PistolHand}, rifle {_b.GetComponent<EnemyRifle>().State}, still ranged {_b.GetComponent<EnemyAI>().ranged}, spread x{_b.GetComponent<EnemyWounds>().AimSpreadMultiplier:0.0}");
            Note($"  off arm gone (E): pistol hand {e.PistolHand}, rifle {_e.GetComponent<EnemyRifle>().State}, still ranged {_e.GetComponent<EnemyAI>().ranged}");
            Require(b.PistolHand == -1, "a crippled gun arm puts the pistol in the left hand");
            Require(e.PistolHand == 1, "a crippled off arm puts the pistol in the right hand");
            Require(_b.GetComponent<EnemyRifle>().Stowed && _e.GetComponent<EnemyRifle>().Stowed, "the rifle goes on the back for the pistol");
        }

        static void CheckPose()
        {
            var agent = _c.GetComponent<UnityEngine.AI.NavMeshAgent>();
            var motor = _c.GetComponent<EnemyMotor>();
            float moved = Vector3.Distance(_c.transform.position, _poseStart);
            float apart = Vector3.Distance(new Vector3(agent.nextPosition.x, 0f, agent.nextPosition.z), new Vector3(_c.transform.position.x, 0f, _c.transform.position.z));
            Note($"  walker moved {moved:0.0} m in 5.4 s (root motion {motor.RootMotion}, gait {motor.Current}, measured {motor.MeasuredSpeed:0.00} m/s); body and agent {apart * 100f:0} cm apart");
            Require(moved > 2f, "the walker walked (root motion moves the body)");
            Require(apart < 0.3f, "the agent follows the body");
            var rifle = _a.GetComponent<EnemyRifle>();
            Note($"  A's rifle: {rifle.State}, left hand {rifle.LeftHandWant:0.00}, left hand on the rifle {_a.GetComponent<EnemyBody>().LeftHandWeight:0.00}; B (pistol out) {_b.GetComponent<EnemyBody>().LeftHandWeight:0.00}");
            Require(rifle.State == EnemyRifle.Hold.Held, "A holds its rifle");
            Note("  " + Facings(_a, "with the aim rig"));
            
        }

        static float _aimRigWas;

        /// <summary>Which way the hips, chest, head and rifle face against the root's forward, in degrees (positive: to its right).</summary>
        static string Facings(GameObject who, string label)
        {
            var anim = who.GetComponentInChildren<Animator>();
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            Vector3 f = who.transform.forward;
            float Yaw(Vector3 v) { v.y = 0f; return Vector3.SignedAngle(f, v, Vector3.up); }
            Vector3 hips = Vector3.Cross(B(HumanBodyBones.RightUpperLeg).position - B(HumanBodyBones.LeftUpperLeg).position, Vector3.up);
            Vector3 chest = Vector3.Cross(B(HumanBodyBones.RightUpperArm).position - B(HumanBodyBones.LeftUpperArm).position, Vector3.up);
            var body = who.GetComponent<EnemyBody>();
            Vector3 toPlayer = _player.transform.position - body.gun.position;
            float aimOff = Vector3.Angle(body.gun.forward, toPlayer);
            return $"{label}: hips {Yaw(hips):0}, chest {Yaw(chest):0}, rifle {Yaw(body.gun.forward):0}, head {Yaw(B(HumanBodyBones.Head).position - B(HumanBodyBones.Neck).position):0}, player {Yaw(_player.transform.position - who.transform.position):0} (deg from the root's forward); rifle bore {aimOff:0} deg off the player";
        }

        /// <summary>A picture of the subject from an angle round it (0 = in front of it), at a distance.</summary>
        static void PictureFrom(GameObject subject, string name, float angle, float distance)
        {
            // No graphics device (a -nographics batch run): rendering one crashes the editor; the checks need no pictures.
            if (subject == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var anim = subject.GetComponentInChildren<Animator>();
            Vector3 at = anim.GetBoneTransform(HumanBodyBones.Chest).position;
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * subject.transform.forward;
            _cam.transform.position = at + dir * distance + Vector3.up * 0.15f;
            _cam.transform.LookAt(at);
            FairgroundShots.Capture(_cam, $"{Folder}/{_shot++:00}_{name}.png", 480, 270);
        }

        static void Picture(GameObject subject, string name)
        {
            // No graphics device (a -nographics batch run): rendering one crashes the editor; the checks need no pictures.
            if (subject == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var anim = subject.GetComponentInChildren<Animator>();
            Vector3 at = anim != null && anim.GetBoneTransform(HumanBodyBones.Hips) != null
                ? anim.GetBoneTransform(HumanBodyBones.Hips).position : subject.transform.position + Vector3.up;
            // The first of a few views round the body that no cover stands in front of.
            Vector3 side = Vector3.Cross(Vector3.up, subject.transform.forward).normalized;
            Vector3 fwd = subject.transform.forward;
            Vector3 pick = at + side * 4.2f + fwd * 2.2f + Vector3.up * 0.5f;
            foreach (var off in new[] { side * 4.2f + fwd * 2.2f, -side * 4.2f + fwd * 2.2f, fwd * 4.5f, -side * 4.2f - fwd * 2.2f, side * 4.2f - fwd * 2.2f })
            {
                Vector3 from = at + off + Vector3.up * 0.5f;
                if (!Physics.Linecast(from, at, 1 << LayerMask.NameToLayer("Environment"), QueryTriggerInteraction.Ignore)) { pick = from; break; }
            }
            _cam.transform.position = pick;
            _cam.transform.LookAt(at);
            FairgroundShots.Capture(_cam, $"{Folder}/{_shot++:00}_{name}.png", 480, 270);
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Time.captureDeltaTime = 0f;
            var sb = new StringBuilder();
            sb.AppendLine($"Limb test {DateTime.Now:yyyy-MM-dd HH:mm}: {(_fails.Count == 0 ? "ALL PASS" : _fails.Count + " FAILED")}  (real time {EditorApplication.timeSinceStartup - _realStart:0} s)");
            sb.Append(_log);
            if (_fails.Count > 0) { sb.AppendLine("Failures:"); foreach (var f in _fails) sb.AppendLine("  - " + f); }
            Report = sb.ToString();
            File.WriteAllText($"{Folder}/report.txt", Report);
            Debug.Log("[FPSKit] " + Report);
            Running = false;
            EditorApplication.ExitPlaymode();
            EditorApplication.delayCall += Restore;
        }

        static void Restore()
        {
            if (EditorApplication.isPlaying) { EditorApplication.delayCall += Restore; return; }
            FPSKitPlayMode.RestoreStartScene();
            if (_exitWhenDone) { _exitWhenDone = false; FPSKitBatch.Exit(_fails.Count == 0 ? 0 : 1); return; }
            if (!string.IsNullOrEmpty(_returnTo) && _returnTo != FPSKitAITestScene.ScenePath)
                EditorSceneManager.OpenScene(_returnTo, OpenSceneMode.Single);
        }
    }
}
#endif
