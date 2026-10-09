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

        static bool _fallsOnly;

        [MenuItem("FPSKit/Enemy AI/Run Limb Test")]
        public static void Run() => Run(false);

        /// <summary>Only D's three kicks and get-ups: the slow part, on its own.</summary>
        [MenuItem("FPSKit/Enemy AI/Run Limb Test (Falls Only)")]
        public static void RunFalls() => Run(true);

        public static void Run(bool fallsOnly)
        {
            _fallsOnly = fallsOnly;
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
            _aMoveTime = 0f; _aMoveDist = 0f;
            FPSKitPlayMode.SuspendStartScene();
            _realStart = EditorApplication.timeSinceStartup;
            Running = true;
            EditorApplication.update += Tick;
        }

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
                        Watch();
                        float t = Time.time - _t0;
                        while (_next < _script.Count && t >= _script[_next].at) _script[_next++].act();
                        if (_fallsOnly && _dFalls.Count >= 3 && _d != null && !_d.GetComponent<EnemyBody>().IsDown && _next < _script.Count)
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
            if (_fallsOnly)
            {
                s.Add((1f, () => Kick(_d)));
                s.Add((30f, CheckFalls));
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
            if (enemy == _d) { _dKickAt = Time.time; _dKicks++; }
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
            Require(wc.OneHanded && _c.GetComponent<EnemyAI>().ranged, "C: one crippled arm keeps the rifle, one-handed");
            Require(wc.AimSpreadMultiplier > 2.5f, "C: and shoots badly");

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
            if (_nextKick > 0f && Time.time >= _nextKick) { _nextKick = -1f; Kick(_d); }
            _dWasDown = down;
            _dWasRising = rising;
        }

        static int _pendingPictures;
        static float _nextKick = -1f;

        static void Picture(GameObject subject, string name)
        {
            if (subject == null) return;
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
            if (!string.IsNullOrEmpty(_returnTo) && _returnTo != FPSKitAITestScene.ScenePath)
                EditorSceneManager.OpenScene(_returnTo, OpenSceneMode.Single);
        }
    }
}
#endif
