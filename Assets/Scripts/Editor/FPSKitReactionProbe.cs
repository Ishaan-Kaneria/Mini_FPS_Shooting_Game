#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// How fast an enemy answers each melee blow, inside the open editor (VerifyCombat measures the
    /// same thing in batch). Plays level one of the Warehouse, stands the player at arm's length
    /// from a standing enemy and throws the rifle butt, the punch, the cross (double punch) and the
    /// kick, each on a topped-up body, and reads the frames from the blow landing to the body
    /// visibly moving (<see cref="EnemyBody.ReactionLatencyFrames"/>). Game time runs at 1/60 s a
    /// frame, so one frame is 16.7 ms.
    ///
    ///   FPSKit > Enemy AI > Measure Melee Reactions
    /// </summary>
    public static class FPSKitReactionProbe
    {
        const string Scene = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";
        static readonly MeleeStrike.Strike[] Order = { MeleeStrike.Strike.Butt, MeleeStrike.Strike.Jab, MeleeStrike.Strike.Cross, MeleeStrike.Strike.Kick };

        public static bool Running { get; private set; }
        public static string Report { get; private set; } = "";

        static int _step, _stage, _frame, _lastFrame, _hitBefore, _warm;
        static EnemyAI _target;
        static EnemyBody _body;
        static StringBuilder _sb;

        [MenuItem("FPSKit/Enemy AI/Measure Melee Reactions")]
        public static void Run()
        {
            if (Running || EditorApplication.isPlaying) return;
            if (EditorSceneManager.GetActiveScene().path != Scene)
            {
                if (EditorSceneManager.GetActiveScene().isDirty) { Debug.LogError("[FPSKit] Reaction probe: save the open scene first"); return; }
                EditorSceneManager.OpenScene(Scene);
            }
            _step = _stage = _frame = _warm = 0;
            _lastFrame = -1;
            _target = null; _body = null;
            _sb = new StringBuilder();
            Report = "";
            Running = true;
            FPSKitPlayMode.SuspendStartScene();
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (Time.frameCount == _lastFrame) return;
            _lastFrame = Time.frameCount;
            // Through the briefing and the settling quickly (big steps of game time); measured at 1/60 s.
            var level = Object.FindAnyObjectByType<LevelManager>();
            bool warming = (level != null && level.IsBriefing) || _warm < 15;
            Time.captureDeltaTime = warming ? 0.25f : 1f / 60f;
            if (warming) { if (level == null || !level.IsBriefing) _warm++; return; }
            _frame++;
            try
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                var melee = player != null ? player.GetComponent<MeleeStrike>() : null;
                if (melee == null) return;
                var ph = player.GetComponent<Health>();
                if (ph != null && ph.Normalized < 0.5f) ph.ResetHealth();
                if (_step >= Order.Length || _frame > 3000) { Finish(); return; }
                var strike = Order[_step];

                if (_stage == 0)
                {
                    if (_target == null || _body == null || _body.IsDown || _target.CurrentState == EnemyAI.State.Dead)
                    {
                        _target = null; _body = null;
                        float best = float.MaxValue;
                        foreach (var ai in Object.FindObjectsByType<EnemyAI>())
                        {
                            var b = ai.GetComponent<EnemyBody>();
                            if (b == null || b.IsDown || ai.CurrentState == EnemyAI.State.Dead) continue;
                            float d = (ai.transform.position - player.transform.position).sqrMagnitude;
                            if (d < best) { best = d; _target = ai; _body = b; }
                        }
                        if (_target == null) return;
                    }
                    if (melee.Current != MeleeStrike.Strike.None || Time.time < melee.ReadyAt + 0.02f) return;
                    if (strike == MeleeStrike.Strike.Butt && melee.GunAway > 0.01f) return;

                    _target.GetComponent<Health>().ResetHealth();
                    Vector3 away = Vector3.ProjectOnPlane(player.transform.position - _target.transform.position, Vector3.up);
                    away = away.sqrMagnitude > 0.01f ? away.normalized : _target.transform.forward;
                    var cc = player.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    player.transform.position = _target.transform.position + away * 1.3f + Vector3.up * 0.05f;
                    player.transform.rotation = Quaternion.LookRotation(-away);
                    if (cc != null) cc.enabled = true;
                    Physics.SyncTransforms();

                    _hitBefore = _body.LastHitFrame;
                    if (strike == MeleeStrike.Strike.Kick) melee.TryKick(); else melee.TryPunch();
                    if (melee.Thrown != strike)
                    {
                        _sb.Append($"\n  {strike}: the chain threw {melee.Thrown}");
                        _step++;
                        return;
                    }
                    _stage = 1;
                    _frame = 0;
                    return;
                }

                bool landed = _body != null && _body.LastHitFrame > _hitBefore;
                int latency = landed ? _body.ReactionLatencyFrames : -1;
                if (latency >= 0)
                {
                    _sb.Append($"\n  {strike}: {latency} frame(s), {latency * 1000f / 60f:0} ms from the blow landing to the body moving");
                    _step++;
                    _stage = 0;
                }
                else if (_frame > 180)
                {
                    _sb.Append($"\n  {strike}: {(landed ? "landed, no visible answer in 3 s" : "never landed")}");
                    _step++;
                    _stage = 0;
                }
            }
            catch (System.Exception e) { _sb.Append("\n  failed: " + e.Message); Finish(); }
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Time.captureDeltaTime = 0f;
            Running = false;
            Report = _sb.ToString();
            Debug.Log("[FPSKit] Melee reactions:" + Report);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += FPSKitPlayMode.RestoreStartScene;
        }
    }
}
#endif
