#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// How long the kick's own leg hides the enemy being kicked, in first person (combat audit,
    /// 2026-10-18: "the boot shouldn't hide the target for more than a few frames around impact").
    ///
    /// A Grunt stands 1.9 m ahead on a flat floor of the test's own; the player kicks it. Every game
    /// frame of the kick (60 a second) the view is rendered three times with shadows off: as it is,
    /// without anything of the player's (legs, arms, gun), and without the player or the Grunt. The
    /// Grunt's pixels are where the last two differ; the hidden ones are those that the player's own
    /// model changes in the first. Fails when more than <see cref="MaxHiddenFrames"/> frames hide over
    /// half of the Grunt. Needs a graphics device:
    ///
    ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.VerifyKickView    (Build/KickView: report, sheet)
    /// </summary>
    public static class FPSKitKickViewTest
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";
        const string EnemyPrefab = "Assets/FPSKit_Generated/Enemy.prefab";
        const string Folder = "Build/KickView";
        const int MaxHiddenFrames = 4;
        const int W = 480, H = 270;

        public static bool Running { get; private set; }

        static readonly Stack<IEnumerator> Script = new Stack<IEnumerator>();
        static readonly List<(float k, float hidden, Texture2D shot)> Frames = new List<(float, float, Texture2D)>();
        static int _lastFrame, _warm;
        static double _startedAt;
        static GameObject _player, _floor, _target;
        static PlayerMotor _motor;
        static MeleeStrike _melee;
        static LevelSet.Level _tunedLevel;
        static float _clockBackup = -1f;
        static string _error;

        public static void Batch() => Run();

        [MenuItem("FPSKit/Player/Measure Kick View")]
        public static void Run()
        {
            if (Running || EditorApplication.isPlaying) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Script.Clear(); Frames.Clear();
            _lastFrame = -1; _warm = 0; _player = null; _error = null;
            _startedAt = EditorApplication.timeSinceStartup;
            Running = true;
            SaveMigration.Apply();
            FPSKitPlayMode.SuspendStartScene();
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _startedAt > 600) throw new Exception("kick view test timed out");
                if (!EditorApplication.isPlaying || Time.frameCount == _lastFrame) return;
                _lastFrame = Time.frameCount;
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new Exception("needs a graphics device: run with UNITY_GRAPHICS=1");

                var level = Object.FindAnyObjectByType<LevelManager>();
                if (level != null && level.Level != null && _tunedLevel == null)
                {
                    _tunedLevel = level.Level;
                    _clockBackup = level.Level.timeLimit;
                    level.Level.timeLimit = 900f;
                    level.ResolveLevel();
                }
                bool warming = level == null || level.IsBriefing || !level.IsRunning || _warm < 20;
                Time.captureDeltaTime = warming ? 0.1f : 1f / 60f;
                if (warming) { if (level != null && level.IsRunning) _warm++; return; }

                if (_player == null) Script.Push(Kick());
                foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                    if (_target == null || ai.gameObject != _target) ai.gameObject.SetActive(false);

                while (Script.Count > 0)
                {
                    var top = Script.Peek();
                    if (!top.MoveNext()) { Script.Pop(); continue; }
                    if (top.Current is IEnumerator sub) { Script.Push(sub); continue; }
                    return;
                }
                Finish();
            }
            catch (Exception e)
            {
                _error = e.Message;
                Debug.LogException(e);
                Finish();
            }
        }

        static IEnumerator Kick()
        {
            _player = GameObject.FindGameObjectWithTag("Player");
            _motor = _player.GetComponent<PlayerMotor>();
            _melee = _player.GetComponent<MeleeStrike>();

            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "KickView_Floor";
            _floor.transform.position = new Vector3(0f, 399.5f, 0f);
            _floor.transform.localScale = new Vector3(60f, 1f, 60f);
            var surface = _floor.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            surface.collectObjects = Unity.AI.Navigation.CollectObjects.Children;
            surface.BuildNavMesh();
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) light.shadows = LightShadows.None;

            var cc = _player.GetComponent<CharacterController>();
            cc.enabled = false;
            _player.transform.position = new Vector3(0f, 400.05f, 0f);
            cc.enabled = true;
            Physics.SyncTransforms();
            _motor.TurnTowards(new Vector3(0f, 401.6f, 20f), 1f);
            for (int i = 0; i < 4; i++) yield return null;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
            Vector3 at = new Vector3(0f, 400f, 1.9f);
            if (NavMesh.SamplePosition(at, out var hit, 1f, NavMesh.AllAreas)) at = hit.position;
            _target = Object.Instantiate(prefab, at, Quaternion.LookRotation(Vector3.back));
            FPSKitEnemyRoster.GetOrCreate("Grunt").ApplyTo(_target);
            _target.GetComponent<EnemyAI>().enabled = false;
            var brain = _target.GetComponent<EnemyBrain>();
            if (brain != null) brain.enabled = false;
            for (int i = 0; i < 30; i++) yield return null;
            // Looking at its chest, as a player lining up a kick does.
            _motor.TurnTowards(_target.transform.position + Vector3.up * 1.2f, 1f);
            for (int i = 0; i < 4; i++) yield return null;

            _melee.TryKick();
            while (_melee.Current == MeleeStrike.Strike.None) yield return null;
            while (_melee.Current == MeleeStrike.Strike.Kick)
            {
                Measure(_melee.StrikeProgress);
                yield return null;
            }
        }

        /// <summary>Three renders of this frame, and the share of the Grunt the player's own model hides.</summary>
        static void Measure(float k)
        {
            var cam = _melee.fpsCamera;
            var mine = new List<Renderer>();
            foreach (var r in _player.GetComponentsInChildren<Renderer>()) if (r.enabled) mine.Add(r);
            var theirs = new List<Renderer>();
            foreach (var r in _target.GetComponentsInChildren<Renderer>()) if (r.enabled) theirs.Add(r);

            var full = Render(cam);
            foreach (var r in mine) r.enabled = false;
            var noPlayer = Render(cam);
            foreach (var r in theirs) r.enabled = false;
            var empty = Render(cam);
            foreach (var r in mine) r.enabled = true;
            foreach (var r in theirs) r.enabled = true;

            Color32[] a = full.GetPixels32(), b = noPlayer.GetPixels32(), c = empty.GetPixels32();
            int target = 0, hidden = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (!Differs(b[i], c[i])) continue;
                target++;
                if (Differs(a[i], b[i])) hidden++;
            }
            Object.DestroyImmediate(noPlayer);
            Object.DestroyImmediate(empty);
            Frames.Add((k, target > 0 ? hidden / (float)target : 1f, full));
        }

        static bool Differs(Color32 x, Color32 y) => Mathf.Abs(x.r - y.r) + Mathf.Abs(x.g - y.g) + Mathf.Abs(x.b - y.b) > 30;

        static Texture2D Render(Camera cam)
        {
            var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = rt };
                RenderPipeline.SubmitRenderRequest(cam, request);
                RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                return tex;
            }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Directory.CreateDirectory(Folder);
            int over = 0, longest = 0, run = 0;
            float impact = MeleeStrike.ImpactAt(MeleeStrike.Strike.Kick);
            var lines = new List<string>();
            foreach (var f in Frames)
            {
                bool hiddenHalf = f.hidden > 0.5f;
                if (hiddenHalf) { over++; run++; longest = Mathf.Max(longest, run); } else run = 0;
                lines.Add($"  k {f.k:0.00}{(Mathf.Abs(f.k - impact) < 0.02f ? " (impact)" : "")}: {f.hidden * 100f,5:0}% of the Grunt hidden by the player's own model");
            }

            // A contact sheet, every second frame, with the share hidden written on each.
            int n = Mathf.Min(16, (Frames.Count + 1) / 2);
            if (n > 0)
            {
                var sheet = new Texture2D(W * 4, H * ((n + 3) / 4), TextureFormat.RGB24, false);
                var fill = new Color32[sheet.width * sheet.height];
                for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(255, 255, 255, 255);
                sheet.SetPixels32(fill);
                for (int i = 0; i < n; i++)
                {
                    var f = Frames[Mathf.Min(Frames.Count - 1, i * 2)];
                    int col = i % 4, row = (n + 3) / 4 - 1 - i / 4;
                    sheet.SetPixels32(col * W, row * H, W, H, f.shot.GetPixels32());
                    // A bar along the top of each: its length is the share hidden, red past half.
                    int bar = Mathf.RoundToInt(W * f.hidden);
                    for (int x = 0; x < bar; x++) for (int y = H - 8; y < H; y++)
                        sheet.SetPixel(col * W + x, row * H + y, f.hidden > 0.5f ? Color.red : Color.yellow);
                }
                sheet.Apply();
                File.WriteAllBytes($"{Folder}/kick_sheet.png", sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
            }
            foreach (var f in Frames) Object.DestroyImmediate(f.shot);

            bool ok = _error == null && Frames.Count > 0 && over <= MaxHiddenFrames;
            string report = (ok ? "kick view test passed" : "kick view test FAILED")
                            + $": {Frames.Count} frames of kick at 60 fps; {over} hide more than half of the Grunt (allowed {MaxHiddenFrames}), longest run {longest}"
                            + (_error != null ? "\n  error: " + _error : "")
                            + "\n" + string.Join("\n", lines)
                            + $"\n  sheet: {Folder}/kick_sheet.png (every second frame; the bar on top is the share hidden, red past half)";
            File.WriteAllText($"{Folder}/report.txt", report);
            if (ok) Debug.Log("[FPSKitBatch] " + report); else Debug.LogError("[FPSKitBatch] FAILED: " + report);

            if (_floor != null) Object.Destroy(_floor);
            if (_target != null) Object.Destroy(_target);
            if (_tunedLevel != null && _clockBackup > 0f) _tunedLevel.timeLimit = _clockBackup;
            _tunedLevel = null; _clockBackup = -1f;
            Time.captureDeltaTime = 0f;
            Running = false;
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += FPSKitPlayMode.RestoreStartScene;
            FPSKitBatch.Exit(ok ? 0 : 1);
        }
    }
}
#endif
