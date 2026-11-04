#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// A round through the neck, as he asked for it (EnemyNeckWound). Without the player: A takes a rifle round in the neck: alive,
    /// holding still, blood thrown in beats, the left hand on the wound within a second, on his knees before the end, dead on
    /// his own in time with the shooter's credit. B: the same with the Blood setting off: the clutch and the death, no blood.
    /// C: a boss: no clutch, no bleeding. D: a second round to a bleeding neck is an ordinary hit (it can finish him).
    /// E: a heavy round through the neck may kill at once. Frames of A go to Build/NeckWound.
    ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitNeckWoundTest.Batch
    /// </summary>
    public static class FPSKitNeckWoundTest
    {
        const string Prefab = "Assets/FPSKit_Generated/Enemy.prefab", Folder = "Build/NeckWound";
        static int _step, _lastFrame = -1, _shot;
        static float _t0, _bestHand = 999f, _handAt = -1f, _diedAt = -1f, _startFloor, _lowest;
        static double _real;
        static bool _bloodWas, _bloodDuringA;
        static GameObject _player, _a, _b, _c, _d, _e;
        static Camera _camera;
        static RenderTexture _rt;
        static readonly StringBuilder _log = new StringBuilder();
        static readonly List<string> _fails = new List<string>();

        public static void Batch()
        {
            FPSKitAITestScene.Build();
            _bloodWas = GameSettings.Blood;
            FPSKitPlayMode.SuspendStartScene();
            _real = EditorApplication.timeSinceStartup;
            _step = 0;
            EditorApplication.update += Tick;
        }

        static void Require(bool ok, string what) { _log.AppendLine((ok ? "  PASS  " : "  FAIL  ") + what); if (!ok) _fails.Add(what); }

        static GameObject Spawn(string name, float x, string kind = "Grunt")
        {
            var at = new Vector3(x, 0f, 8f);
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab), at, Quaternion.LookRotation(-at.normalized));
            go.name = name;
            FPSKitEnemyRoster.GetOrCreate(kind).ApplyTo(go);
            var ai = go.GetComponent<EnemyAI>();
            if (ai != null) ai.enabled = true;
            return go;
        }

        static DamageInfo Shoot(GameObject enemy, BodyPart part, float amount, bool heavy)
        {
            foreach (var hb in enemy.GetComponentsInChildren<Hitbox>())
            {
                if (hb.Part != part || !hb.isActiveAndEnabled) continue;
                Vector3 point = hb.transform.position, dir = (point - (_player.transform.position + Vector3.up * 1.6f)).normalized;
                return hb.Receive(new DamageInfo(amount, point - dir * 0.05f, -dir, dir, _player) { heavy = heavy });
            }
            return default;
        }

        static void Snap()
        {
            if (_camera == null) return;
            Directory.CreateDirectory(Folder);
            // In front of him and a little to his left, wherever he has got to.
            var body = _a.transform;
            var bones = _a.GetComponentInChildren<Animator>();
            Vector3 side = Vector3.ProjectOnPlane(bones.GetBoneTransform(HumanBodyBones.RightUpperArm).position - bones.GetBoneTransform(HumanBodyBones.LeftUpperArm).position, Vector3.up).normalized;
            Vector3 chest = Vector3.Cross(side, Vector3.up).normalized;
            _camera.transform.position = body.position + chest * 1.5f - side * 0.55f + Vector3.up * 1.5f;
            _camera.transform.LookAt(body.position + Vector3.up * (_a.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Neck).position.y - body.position.y - 0.2f));
            var request = new RenderPipeline.StandardRequest { destination = _rt };
            RenderPipeline.SubmitRenderRequest(_camera, request);
            RenderPipeline.SubmitRenderRequest(_camera, request);
            var was = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
            RenderTexture.active = was;
            File.WriteAllBytes($"{Folder}/neck_{_shot++:00}.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _real > 500) throw new Exception("neck wound test timed out");
                if (!EditorApplication.isPlaying) { if (_step == 0) { EditorApplication.EnterPlaymode(); _step = 1; } return; }
                if (Time.frameCount == _lastFrame) return;
                _lastFrame = Time.frameCount;
                Time.captureDeltaTime = 1f / 30f;
                switch (_step)
                {
                    case 1:
                        if (Time.frameCount < 10) return;
                        _player = GameObject.FindGameObjectWithTag("Player");
                        GameSettings.Blood = true;
                        _a = Spawn("A_Neck", 0f);
                        var aim = _a.transform.position;
                        var cam = new GameObject("NeckCamera");
                        cam.transform.position = aim + _a.transform.forward * 2.3f + _a.transform.right * 0.6f + Vector3.up * 1.55f;
                        cam.transform.LookAt(aim + Vector3.up * 1.15f);
                        _camera = cam.AddComponent<Camera>();
                        _camera.fieldOfView = 50f;
                        _camera.enabled = false;
                        _rt = new RenderTexture(760, 620, 24, RenderTextureFormat.ARGB32);
                        _step = 2; _t0 = Time.time;
                        return;
                    case 2:
                        if (Time.time - _t0 < 1f) return;
                        var hp = _a.GetComponent<Health>();
                        var hit = Shoot(_a, BodyPart.Neck, 30f, false);
                        Require(hit.amount > 0f && !hp.IsDead, $"a rifle round in the neck leaves him alive ({hp.Current:0} left)");
                        var big = Spawn("A2_BigRound", 9f);
                        Shoot(big, BodyPart.Neck, 500f, false);
                        Require(!big.GetComponent<Health>().IsDead && big.GetComponent<EnemyNeckWound>().Bleeding, "even an enormous light round leaves him alive to bleed out");
                        Require(_a.GetComponent<EnemyNeckWound>().Bleeding, "the wound is open");
                        _startFloor = _a.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Hips).position.y;
                        _lowest = _startFloor;
                        Snap();
                        _hitAt = _a.transform.position;
                        _step = 3; _t0 = Time.time;
                        return;
                    case 3:
                        WatchA();
                        return;
                    case 4:
                        GameSettings.Blood = false;
                        int before = BloodFX.JetDrops;
                        _b = Spawn("B_NoBlood", 3f);
                        Shoot(_b, BodyPart.Neck, 60f, false);
                        _c = Spawn("C_Boss", -3f, "Warden");
                        var boss = _c.GetComponent<EnemyAI>();
                        if (boss.archetype == null) _log.AppendLine("  note: boss has no archetype");
                        Shoot(_c, BodyPart.Neck, 30f, false);
                        Require(boss.archetype != null && boss.archetype.role == EnemyArchetype.Role.Boss, "C is a boss");
                        Require(!_c.GetComponent<EnemyNeckWound>().Bleeding, "a boss does not bleed out");
                        Require(!_c.GetComponent<EnemyBody>().InPain || _c.GetComponent<EnemyBody>().PainDoing != "clutching its throat", "a boss does not clutch his throat");
                        _d = Spawn("D_Second", 6f);
                        Shoot(_d, BodyPart.Neck, 60f, false);
                        var dh = _d.GetComponent<Health>();
                        Shoot(_d, BodyPart.Neck, 500f, false);
                        Require(dh.IsDead, "a second round into a bleeding neck can finish him");
                        _e = Spawn("E_Heavy", -6f);
                        Shoot(_e, BodyPart.Neck, 500f, true);
                        Require(_e.GetComponent<Health>().IsDead, "a heavy round through the neck can kill at once");
                        _beforeB = before;
                        _step = 5; _t0 = Time.time;
                        return;
                    case 5:
                        if (Time.time - _t0 < 1.5f) return;
                        Require(_b.GetComponent<EnemyBody>().PainDoing == "clutching its throat", "Blood off: he still clutches his throat");
                        Require(BloodFX.JetDrops == _beforeB, "Blood off: no blood thrown");
                        Finish();
                        return;
                }
            }
            catch (Exception e) { Debug.LogException(e); _fails.Add("error: " + e.Message); Finish(); }
        }

        static int _beforeB;
        static Vector3 _hitAt;
        static float _walked;

        static void WatchA()
        {
            float t = Time.time - _t0;
            var wound = _a.GetComponent<EnemyNeckWound>();
            var health = _a.GetComponent<Health>();
            var animator = _a.GetComponentInChildren<Animator>();
            if (wound.Bleeding)
            {
                // From the neck, not the wound: a round in the nape still sends the hand to the throat he can reach.
                Vector3 neckMiddle = Vector3.Lerp(animator.GetBoneTransform(HumanBodyBones.Neck).position, animator.GetBoneTransform(HumanBodyBones.Head).position, 0.4f);
                float d = Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftHand).position, neckMiddle);
                if (d < _bestHand) _bestHand = d;
                if (_handAt < 0f && d < 0.19f) _handAt = t;
                _lowest = Mathf.Min(_lowest, animator.GetBoneTransform(HumanBodyBones.Hips).position.y);
                // A man shot mid-run stumbles on for a second on his clip's momentum; after that he stays where he is.
                if (t < 1.3f) _hitAt = _a.transform.position;
                else _walked = Mathf.Max(_walked, Vector3.Distance(_a.transform.position, _hitAt));
                // Drawn, not just counted: this once passed with nothing on the screen.
                var stream = wound.Stream;
                if (t > 0.5f && !_bloodDuringA) _bloodDuringA = stream != null && stream.Drawn >= 10 && stream.Widest > 0.01f;
            }
            if ((t < 0.5f && Time.frameCount % 3 == 0) || (Time.frameCount % 12 == 0 && t < 10f)) Snap();
            if (health.IsDead && _diedAt < 0f)
            {
                _diedAt = t;
                Require(health.LastDamage.source == _player, "the kill is the shooter's");
                Require(health.LastDamage.bleed, "the bleeding is what killed him");
            }
            if (_diedAt >= 0f && t > _diedAt + 1.5f || t > 25f)
            {
                _log.AppendLine($"  hand nearest the wound {_bestHand:0.000} m, there at {_handAt:0.00} s; hips dropped {_startFloor - _lowest:0.00} m; died at {_diedAt:0.0} s; {BloodFX.JetDrops} drops thrown");
                Require(_handAt >= 0f && _handAt < 1.2f, "his hand is on the wound within 1.2 s");
                Require(_bloodDuringA, "the artery throws a connected stream that is drawn");
                Require(_a.GetComponent<EnemyBody>().PistolHand == 0, "he draws no sidearm: both hands are on his throat");
                Require(_walked < 0.4f, $"he gives up the fight and stays where he is, once he has stumbled to a stop (moved {_walked:0.00} m)");
                Require(_startFloor - _lowest > 0.15f, "his knees go");
                Require(_diedAt > 3f && _diedAt < 16f, "he bleeds out on his own, in 3 to 16 s");
                _step = 4;
            }
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Time.captureDeltaTime = 0f;
            GameSettings.Blood = _bloodWas;
            if (_rt != null) { _rt.Release(); Object.DestroyImmediate(_rt); }
            string report = (_fails.Count == 0 ? "neck wound test passed\n" : $"neck wound test FAILED ({_fails.Count}):\n") + _log;
            if (_fails.Count == 0) Debug.Log("[FPSKitBatch] " + report); else Debug.LogError("[FPSKitBatch] FAILED: " + report);
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
