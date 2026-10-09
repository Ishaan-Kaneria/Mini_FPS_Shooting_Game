#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The enemy AI's performance check: a fixed number of enemies fighting the player in the
    /// AbandonedFairground at Medium quality, measured in play mode, so a "before" and an
    /// "after" are the same test (Docs/ENEMY_AI_PLAN.md, Stage 1 baseline and Stage 6).
    ///
    /// What it reads is CPU time per frame from profiler markers, not the editor's frame rate:
    /// an editor that is not focused is throttled to a few frames a second, which says nothing
    /// about the game, but the time each frame spends in the player loop, the scripts, the
    /// animators and physics is the same either way. The level's own spawning is switched off,
    /// the player is kept alive, and the run is abandoned if the machine runs low on memory.
    ///
    /// Runs inside the open editor (it never exits it). Report: Build/Perf/enemies_&lt;label&gt;.txt.
    /// </summary>
    public static class FPSKitEnemyPerf
    {
        const string Scene = "Assets/FPSKit_Generated/Scenes/AbandonedFairground.unity";
        const string Prefab = "Assets/FPSKit_Generated/Enemy.prefab";
        const long LowMemoryMB = 900;

        static string _label = "baseline";
        static int _count = 20;
        static double _seconds = 25, _warmup = 6;
        static int _step;
        static double _mark;
        static string _returnTo;
        static int _quality;
        static Transform _player;
        static Health _playerHealth;
        static readonly List<GameObject> _spawned = new List<GameObject>();
        static readonly Dictionary<string, ProfilerRecorder> _rec = new Dictionary<string, ProfilerRecorder>();
        static readonly Dictionary<string, List<double>> _samples = new Dictionary<string, List<double>>();
        static readonly List<int> _alive = new List<int>();
        static long _minFreeMB = long.MaxValue;
        static string _abort;

        /// <summary>Profiler markers read each frame, in milliseconds. Missing ones are skipped.</summary>
        static readonly (ProfilerCategory cat, string name)[] Markers =
        {
            (ProfilerCategory.Internal, "Main Thread"),
            (ProfilerCategory.Internal, "PlayerLoop"),
            (ProfilerCategory.Scripts, "BehaviourUpdate"),
            (ProfilerCategory.Scripts, "LateBehaviourUpdate"),
            (ProfilerCategory.Scripts, "FixedBehaviourUpdate"),
            (ProfilerCategory.Animation, "PreLateUpdate.DirectorUpdateAnimationBegin"),
            (ProfilerCategory.Animation, "PreLateUpdate.DirectorUpdateAnimationEnd"),
            (ProfilerCategory.Physics, "FixedUpdate.PhysicsFixedUpdate"),
            (ProfilerCategory.Ai, "PreUpdate.AIUpdatePostScript"),
            (ProfilerCategory.Ai, "PreLateUpdate.AIUpdatePostScript"),
            (ProfilerCategory.Render, "PostLateUpdate.FinishFrameRendering"),
            (ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread"),
        };

        [MenuItem("FPSKit/Enemy AI/Measure 20 Enemies")]
        static void Menu() => Run("manual");

        public static bool Running { get; private set; }
        public static string Report { get; private set; } = "";

        public static void Run(string label, int count = 20, double seconds = 25)
        {
            if (Running || EditorApplication.isPlaying) { Debug.LogWarning("[FPSKit] Enemy perf: already running or in play mode."); return; }
            _label = string.IsNullOrEmpty(label) ? "run" : label;
            _count = Mathf.Clamp(count, 1, 60);
            _seconds = seconds;
            _returnTo = EditorSceneManager.GetActiveScene().path;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);

            _quality = QualitySettings.GetQualityLevel();
            int medium = Array.IndexOf(QualitySettings.names, "Medium");
            if (medium >= 0) QualitySettings.SetQualityLevel(medium, true);

            FPSKitPlayMode.SuspendStartScene();
            _spawned.Clear();
            _samples.Clear();
            _alive.Clear();
            _minFreeMB = long.MaxValue;
            _abort = null;
            Report = "";
            _step = 0;
            Running = true;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            try
            {
                long free = FreeMemoryMB();
                if (free > 0) _minFreeMB = Math.Min(_minFreeMB, free);
                if (free > 0 && free < LowMemoryMB) { _abort = $"stopped: only {free} MB of memory left"; Finish(); return; }

                switch (_step)
                {
                    case 0:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        _mark = EditorApplication.timeSinceStartup + 3.0;
                        _step = 1;
                        return;

                    case 1:
                        if (EditorApplication.timeSinceStartup < _mark) return;
                        if (!Stage()) { _abort = "no player or no navmesh to spawn on"; Finish(); return; }
                        _mark = EditorApplication.timeSinceStartup + _warmup;
                        _step = 2;
                        return;

                    case 2:
                        KeepPlayerUp();
                        if (EditorApplication.timeSinceStartup < _mark) return;
                        foreach (var (cat, name) in Markers)
                        {
                            var r = ProfilerRecorder.StartNew(cat, name, 1);
                            _rec[name] = r;
                            _samples[name] = new List<double>();
                        }
                        _mark = EditorApplication.timeSinceStartup + _seconds;
                        _step = 3;
                        return;

                    case 3:
                        KeepPlayerUp();
                        foreach (var pair in _rec)
                            if (pair.Value.Valid && pair.Value.Count > 0)
                                _samples[pair.Key].Add(pair.Value.LastValue * 1e-6);
                        _alive.Add(Object.FindObjectsByType<EnemyAI>().Length);
                        if (EditorApplication.timeSinceStartup >= _mark) Finish();
                        return;
                }
            }
            catch (Exception e)
            {
                _abort = "error: " + e.Message;
                Debug.LogException(e);
                Finish();
            }
        }

        /// <summary>The level's own spawning off, its enemies gone, ours placed round the player.</summary>
        static bool Stage()
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) return false;
            _player = p.transform;
            _playerHealth = p.GetComponentInParent<Health>() ?? p.GetComponentInChildren<Health>();

            foreach (var lm in Object.FindObjectsByType<LevelManager>()) lm.enabled = false;
            foreach (var ai in Object.FindObjectsByType<EnemyAI>()) Object.Destroy(ai.gameObject);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            if (prefab == null) return false;

            var path = new NavMeshPath();
            NavMesh.SamplePosition(_player.position, out var me, 4f, NavMesh.AllAreas);
            var rng = new System.Random(7);
            for (int tries = 0; tries < _count * 30 && _spawned.Count < _count; tries++)
            {
                double a = rng.NextDouble() * Math.PI * 2, d = 12 + rng.NextDouble() * 28;
                var at = _player.position + new Vector3((float)Math.Cos(a), 0f, (float)Math.Sin(a)) * (float)d;
                if (!NavMesh.SamplePosition(at, out var hit, 3f, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(hit.position, me.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                var face = _player.position - hit.position; face.y = 0f;
                _spawned.Add(Object.Instantiate(prefab, hit.position, Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face : Vector3.forward)));
            }
            return _spawned.Count > 0;
        }

        static void KeepPlayerUp()
        {
            if (_playerHealth != null && _playerHealth.Normalized < 0.6f) _playerHealth.ResetHealth();
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            foreach (var r in _rec.Values) r.Dispose();
            _rec.Clear();

            var sb = new StringBuilder();
            sb.AppendLine($"Enemy perf '{_label}' {DateTime.Now:yyyy-MM-dd HH:mm}  scene {Path.GetFileNameWithoutExtension(Scene)}  quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            sb.AppendLine($"enemies spawned {_spawned.Count}/{_count}; alive during capture min {(_alive.Count > 0 ? Min(_alive) : 0)} max {(_alive.Count > 0 ? Max(_alive) : 0)}; frames {(_samples.TryGetValue("PlayerLoop", out var pl) ? pl.Count : 0)}");
            sb.AppendLine($"lowest free system memory {(_minFreeMB == long.MaxValue ? -1 : _minFreeMB)} MB");
            if (_abort != null) sb.AppendLine(_abort);
            sb.AppendLine("marker (ms per frame): mean / median / p95 / max");
            foreach (var (_, name) in Markers)
            {
                if (!_samples.TryGetValue(name, out var s) || s.Count == 0) continue;
                s.Sort();
                double mean = 0; foreach (var v in s) mean += v; mean /= s.Count;
                sb.AppendLine($"  {name,-46} {mean,7:0.00} {s[s.Count / 2],7:0.00} {s[(int)(s.Count * 0.95)],7:0.00} {s[s.Count - 1],7:0.00}");
            }
            sb.AppendLine("Editor play mode: compare runs with each other, not with a built player.");
            Report = sb.ToString();
            Directory.CreateDirectory("Build/Perf");
            File.WriteAllText($"Build/Perf/enemies_{_label}.txt", Report);
            Debug.Log("[FPSKit] " + Report);

            Running = false;
            EditorApplication.ExitPlaymode();
            EditorApplication.delayCall += Restore;
        }

        static void Restore()
        {
            if (EditorApplication.isPlaying) { EditorApplication.delayCall += Restore; return; }
            QualitySettings.SetQualityLevel(_quality, true);
            FPSKitPlayMode.RestoreStartScene();
            if (!string.IsNullOrEmpty(_returnTo) && _returnTo != Scene) EditorSceneManager.OpenScene(_returnTo, OpenSceneMode.Single);
        }

        static int Min(List<int> l) { int m = int.MaxValue; foreach (var v in l) m = Math.Min(m, v); return m; }
        static int Max(List<int> l) { int m = 0; foreach (var v in l) m = Math.Max(m, v); return m; }

        /// <summary>MemAvailable from /proc/meminfo, in MB; -1 where there is no such file.</summary>
        static long FreeMemoryMB()
        {
            try
            {
                foreach (var line in File.ReadLines("/proc/meminfo"))
                    if (line.StartsWith("MemAvailable:"))
                        return long.Parse(line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)[1]) / 1024;
            }
            catch (Exception) { }
            return -1;
        }
    }
}
#endif
