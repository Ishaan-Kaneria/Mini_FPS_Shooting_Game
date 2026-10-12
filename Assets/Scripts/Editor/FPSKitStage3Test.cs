#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Stage 3 of the enemy AI rework (Docs/ENEMY_AI_PLAN.md): real enemy prefabs in the AI test
    /// scene, hit through their real hitboxes.
    ///
    /// - A: heavy rounds (the Breacher's) into the right shin until crippled, then one more: the leg
    ///   comes off, a piece lies in the world, the stump's bone is collapsed, it crawls for good.
    /// - B: standard rounds (the Service Rifle's) into the left forearm, many past crippled: never severed.
    /// - C: Blood off, heavy rounds into the left shin: crippled, never severed. (Blood put back.)
    /// - D: pieces are capped: past the cap the oldest goes.
    /// - E: thrown face down until it fires from the ground (chance raised for the test, put back):
    ///   it fires from the ground, then gets up.
    /// - F: killed by a rifle round from the front: a death clip, then the ragdoll, with no jump in
    ///   the pelvis's speed at the hand-over. G: killed by a blast: straight into the ragdoll.
    ///
    ///   Tools/unity-batch.sh FPSKit.EditorTools.FPSKitStage3Test.Batch   (or FPSKit > Enemy AI > Run Stage 3 Test)
    /// </summary>
    public static class FPSKitStage3Test
    {
        const string Prefab = "Assets/FPSKit_Generated/Enemy.prefab";
        const float Breacher = 13f, Rifle = 24f;

        public static bool Running { get; private set; }
        public static string Report { get; private set; } = "";

        static int _step, _lastFrame = -1;
        static float _t0;
        static double _realStart;
        static readonly StringBuilder _log = new StringBuilder();
        static readonly List<string> _fails = new List<string>();
        static GameObject _player, _a, _b, _c, _e, _f, _g;
        static BodyDamageProfile _profile;
        static float _profileChance;
        static bool _bloodWas;
        static int _eKicks, _eShotsDown;
        static bool _eFired, _eUpAfter, _eWasProne;
        static float _eLastShot, _eNextKick;
        static RagdollController _fRag;
        static bool _fSawClip;
        static float _fHandoverJump = -1f, _fLastHipsSpeed;
        static Vector3 _fLastHips;
        static int _fFrames;

        [MenuItem("FPSKit/Enemy AI/Run Stage 3 Test")]
        public static void Run()
        {
            if (Running || EditorApplication.isPlaying) return;
            FPSKitAITestScene.Build();
            _log.Clear(); _fails.Clear(); Report = "";
            _step = 0; _lastFrame = -1;
            _eKicks = 0; _eShotsDown = 0; _eFired = _eUpAfter = _eWasProne = false; _eNextKick = 0f;
            _fRag = null; _fSawClip = false; _fHandoverJump = -1f; _fFrames = 0;
            _bloodWas = GameSettings.Blood;
            FPSKitPlayMode.SuspendStartScene();
            _realStart = EditorApplication.timeSinceStartup;
            Running = true;
            EditorApplication.update += Tick;
        }

        /// <summary>Batch entry: runs, then exits with 0 when everything passed.</summary>
        public static void Batch() => Run();

        static void Note(string s) => _log.AppendLine(s);
        static void Require(bool ok, string what) { Note((ok ? "  PASS  " : "  FAIL  ") + what); if (!ok) _fails.Add(what); }

        static GameObject Spawn(string name, float x)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            var at = new Vector3(x, 0f, 12f);
            var go = Object.Instantiate(prefab, at, Quaternion.LookRotation(-at.normalized));
            go.name = name;
            FPSKitEnemyRoster.GetOrCreate("Grunt").ApplyTo(go);
            return go;
        }

        /// <summary>A round into the hitbox standing for <paramref name="part"/>, from the player's side.</summary>
        static DamageInfo Shoot(GameObject enemy, BodyPart part, float amount, bool heavy)
        {
            foreach (var hb in enemy.GetComponentsInChildren<Hitbox>())
            {
                if (hb.Part != part || !hb.isActiveAndEnabled) continue;
                var col = hb.GetComponent<Collider>();
                if (col != null && !col.enabled) continue;
                Vector3 point = hb.transform.position;
                Vector3 dir = (point - (_player != null ? _player.transform.position + Vector3.up * 1.6f : point - Vector3.forward)).normalized;
                var info = new DamageInfo(amount, point, -dir, dir, _player) { heavy = heavy };
                return hb.Receive(info);
            }
            return default;
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _realStart > 900) throw new Exception("stage 3 test timed out");
                if (!EditorApplication.isPlaying)
                {
                    if (_step == 0) { EditorApplication.EnterPlaymode(); _step = 1; }
                    return;
                }
                if (Time.frameCount == _lastFrame) return;
                _lastFrame = Time.frameCount;
                Time.captureDeltaTime = 1f / 30f;

                switch (_step)
                {
                    case 1: Setup(); _step = 2; return;
                    case 2: Severing(); _step = 3; _t0 = Time.time; return;
                    case 3: if (Time.time - _t0 > 1.5f) { CheckSevering(); _step = 4; _t0 = Time.time; } return;
                    case 4: if (GroundFire()) { _step = 5; _t0 = Time.time; KillF(); } return;
                    case 5: if (WatchDeath()) { _step = 6; } return;
                    case 6: Finish(); return;
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
            GameSettings.Blood = true;
            _a = Spawn("A_HeavyShin", -8f);
            _b = Spawn("B_RifleForearm", -4f);
            _c = Spawn("C_BloodOff", 0f);
            _e = Spawn("E_GroundFire", 4f);
            _f = Spawn("F_RifleDeath", 8f);
            _g = Spawn("G_BlastDeath", 12f);
            foreach (var go in new[] { _a, _b, _c, _f, _g }) { var ai = go.GetComponent<EnemyAI>(); if (ai != null) ai.enabled = true; }
            // E holds its ground (no chasing past the player between throws): its mind off, the player its target.
            var eAi = _e.GetComponent<EnemyAI>();
            eAi.target = _player.transform;
            eAi.enabled = false;
            var eBrain = _e.GetComponent<EnemyBrain>();
            if (eBrain != null) eBrain.enabled = false;
            _profile = _e.GetComponent<EnemyWounds>().Body;
            _profileChance = _profile != null ? _profile.groundFireChance : 0f;
        }

        static void Severing()
        {
            var wa = _a.GetComponent<EnemyWounds>();
            int before = SeveredPiece.Count;
            for (int i = 0; i < 30 && wa.State(BodyPart.RightLeg) < EnemyWounds.LimbState.Crippled; i++) Shoot(_a, BodyPart.RightShin, Breacher * 2f, heavy: true);
            Note($"A: right leg {wa.State(BodyPart.RightLeg)} after heavy rounds");
            Shoot(_a, BodyPart.RightShin, Breacher * 2f, heavy: true);
            _piecesBefore = before;

            var wb = _b.GetComponent<EnemyWounds>();
            for (int i = 0; i < 40; i++) Shoot(_b, BodyPart.LeftForearm, Rifle, heavy: false);

            GameSettings.Blood = false;
            var wc = _c.GetComponent<EnemyWounds>();
            for (int i = 0; i < 30; i++) Shoot(_c, BodyPart.LeftShin, Breacher * 2f, heavy: true);
        }

        static int _piecesBefore;

        static void CheckSevering()
        {
            var wa = _a.GetComponent<EnemyWounds>();
            var anim = _a.GetComponentInChildren<Animator>();
            var shin = anim != null ? anim.GetBoneTransform(HumanBodyBones.RightLowerLeg) : null;
            Require(wa.State(BodyPart.RightLeg) == EnemyWounds.LimbState.Severed, $"A: a heavy round into a crippled shin takes the leg off (state {wa.State(BodyPart.RightLeg)})");
            Require(SeveredPiece.Count > _piecesBefore, $"A: the leg lies in the world as a piece ({SeveredPiece.Count} pieces)");
            Require(shin != null && shin.localScale.x < 0.01f, "A: the stump's bone is collapsed (no shin left on the body)");
            Require(wa.Legs == EnemyWounds.LegState.Crawling, $"A: a severed leg puts it on the ground for good ({wa.Legs})");
            Require(!_a.GetComponent<Health>().IsDead, "A: it lives on");

            var wb = _b.GetComponent<EnemyWounds>();
            Require(wb.State(BodyPart.LeftArm) == EnemyWounds.LimbState.Crippled, $"B: standard rounds cripple the arm but never sever it ({wb.State(BodyPart.LeftArm)})");

            var wc = _c.GetComponent<EnemyWounds>();
            Require(wc.State(BodyPart.LeftLeg) != EnemyWounds.LimbState.Severed, $"C: with Blood off a heavy round never takes a limb off ({wc.State(BodyPart.LeftLeg)})");
            GameSettings.Blood = true;

            // D: the cap.
            for (int i = 0; i < 24; i++) SeveredPiece.Register(new GameObject("TestPiece" + i), 60f, 16);
            Require(SeveredPiece.Count <= 16, $"D: pieces are capped at 16 ({SeveredPiece.Count} after 24 more)");
        }

        /// <summary>E: thrown over and over (face down, ahead of the player) until it fires from the ground, then watched until it is up.</summary>
        static bool GroundFire()
        {
            var body = _e.GetComponent<EnemyBody>();
            var ai = _e.GetComponent<EnemyAI>();
            if (_profile != null) _profile.groundFireChance = 1f;
            float t = Time.time - _t0;

            if (body.FiringFromGround)
            {
                _eWasProne = true;
                if (ai.LastShotTime > _eLastShot) { _eShotsDown++; _eLastShot = ai.LastShotTime; }
            }
            else if (_eWasProne && !body.IsDown)
            {
                // Up again. With no clear line from where it lay (it fires only then), throw it again.
                if (_eShotsDown > 0 || _eKicks >= 12) _eUpAfter = true;
                else { _eWasProne = false; _eNextKick = Time.time + 1f; }
            }

            if (_eUpAfter || t > 150f)
            {
                Note($"E: {_eKicks} throws; fired from the ground {_eWasProne}, {_eShotsDown} rounds lying down; got up after {_eUpAfter}; last refusal '{ai.GroundFireRefusal}'; ended by '{body.GroundFireEnd}'; muzzle {(ai.muzzlePoint != null ? ai.muzzlePoint.position.ToString() : "none")}");
                Require(_eWasProne, "E: a body thrown face down, rifle on it, the player ahead, fires from the ground");
                Require(_eShotsDown > 0, "E: it fires rounds while lying down");
                Require(_eUpAfter, "E: then gets up");
                if (_profile != null) _profile.groundFireChance = _profileChance;
                return true;
            }

            // Thrown again when it is up and still: towards the player's side, so it lands face down facing them.
            if (!body.IsDown && !_eWasProne && !_eUpAfter && Time.time >= _eNextKick && _eKicks < 12)
            {
                _e.GetComponent<Health>().ResetHealth();
                Vector3 toPlayer = (_player.transform.position - _e.transform.position); toPlayer.y = 0f;
                ai.Knock((toPlayer.normalized * 0.6f + Vector3.up * 0.2f) * 6f, 1.5f);
                _eKicks++;
                _eNextKick = Time.time + 1f;
                _eLastShot = ai.LastShotTime;
            }
            return false;
        }

        static void KillF()
        {
            _fRag = _f.GetComponent<RagdollController>();
            var anim = _f.GetComponentInChildren<Animator>();
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            _fLastHips = hips.position;
            // Rifle rounds to the chest until it drops: an ordinary kill, not one blow worth its whole life.
            var h = _f.GetComponent<Health>();
            for (int i = 0; i < 40 && !h.IsDead; i++) Shoot(_f, BodyPart.Torso, Rifle, heavy: false);

            var g = _g.GetComponent<Health>();
            g.ApplyDamage(new DamageInfo(g.Current + g.Shield + 10f, _g.transform.position + Vector3.up, Vector3.up, Vector3.up, _player) { fromBlast = true, heavy = true });
            var gr = _g.GetComponent<RagdollController>();
            Require(gr != null && gr.IsRagdolled && !gr.Dying, "G: a blast kill goes straight into the ragdoll, no clip");
        }

        /// <summary>F: a death clip, then the ragdoll; the pelvis's speed across the hand-over.</summary>
        static bool WatchDeath()
        {
            var anim = _f.GetComponentInChildren<Animator>();
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            float speed = (hips.position - _fLastHips).magnitude / dt;
            if (_fRag.Dying) _fSawClip = true;
            if (_fSawClip && _fRag.IsRagdolled && _fHandoverJump < 0f) _fHandoverJump = Mathf.Abs(speed - _fLastHipsSpeed);
            _fLastHipsSpeed = speed;
            _fLastHips = hips.position;
            _fFrames++;
            if (_fFrames < 60) return false;
            Require(_fSawClip, "F: a rifle kill plays a death clip first");
            Require(_fRag.IsRagdolled, "F: then the ragdoll has the body");
            Require(_fHandoverJump >= 0f && _fHandoverJump < 2.5f, $"F: no jump at the hand-over (pelvis speed changed by {_fHandoverJump:0.00} m/s in a frame)");
            return true;
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Time.captureDeltaTime = 0f;
            GameSettings.Blood = _bloodWas;
            if (_profile != null) _profile.groundFireChance = _profileChance;
            Running = false;
            Report = (_fails.Count == 0 ? "stage 3 test passed\n" : $"stage 3 test FAILED ({_fails.Count}):\n") + _log;
            if (_fails.Count == 0) Debug.Log("[FPSKitBatch] " + Report);
            else Debug.LogError("[FPSKitBatch] FAILED: " + Report);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () =>
            {
                FPSKitPlayMode.RestoreStartScene();
                FPSKitBatch.Exit(_fails.Count == 0 ? 0 : 1);
            };
        }
    }
}
#endif
