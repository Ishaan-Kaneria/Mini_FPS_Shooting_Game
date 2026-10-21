#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Desert Outpost's spawns, in play (combat audit C2): its first level runs for 40 s of game time with the player kept
    /// alive where he starts, and every enemy is noted where it first appears. Passes when the level starts without
    /// LevelManager's leash warning, enemies arrive, each one within a few metres of one of the zone points (or from
    /// the ring round the player, LevelManager's own fallback) and never nearer the player than the zone band's near edge,
    /// and none is removed by the leash.
    ///
    /// The town lives through the same run: civilians appear round the player and walk, traffic drives (until the
    /// shooting stops it), and nothing the game does logs an error or an exception.
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyDesertSpawns
    /// </summary>
    public static class FPSKitDesertSpawnTest
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/DesertOutpost.unity";
        const float Seconds = 40f;

        static bool _running;
        static int _lastFrame, _warm, _frames;
        static double _startedAt;
        static LevelSet.Level _tuned;
        static float _clockBackup = -1f;
        static readonly List<string> _warnings = new List<string>();
        static readonly List<string> _errors = new List<string>();
        static readonly HashSet<Civilian> _walked = new HashSet<Civilian>();
        static int _mostCivilians, _mostCars;
        static float _driven;
        static readonly Dictionary<EnemyAI, (Vector3 at, float fromPlayer)> _seen = new Dictionary<EnemyAI, (Vector3, float)>();
        static Vector3 _playerAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _running = false; _warnings.Clear(); _seen.Clear(); _errors.Clear(); _walked.Clear(); }

        public static void Run()
        {
            if (_running || EditorApplication.isPlaying) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            _warnings.Clear(); _seen.Clear(); _errors.Clear(); _walked.Clear();
            _mostCivilians = _mostCars = 0; _driven = 0f;
            _lastFrame = -1; _warm = 0; _frames = 0; _tuned = null;
            _startedAt = EditorApplication.timeSinceStartup;
            _running = true;
            Application.logMessageReceived += Log;
            SaveMigration.Apply();
            FPSKitPlayMode.SuspendStartScene();
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void Log(string message, string stack, LogType type)
        {
            if (message.Contains("[LevelManager]")) _warnings.Add(message.Split('\n')[0]);
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && !FPSKitPlayMode.IsEditorNoise(message) && _errors.Count < 8)
                _errors.Add($"[{type}] {message.Split('\n')[0]}");
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _startedAt > 900) throw new Exception("timed out");
                if (!EditorApplication.isPlaying || Time.frameCount == _lastFrame) return;
                _lastFrame = Time.frameCount;

                var level = Object.FindAnyObjectByType<LevelManager>();
                if (level != null && level.Level != null && _tuned == null)
                {
                    _tuned = level.Level;
                    _clockBackup = level.Level.timeLimit;
                    level.Level.timeLimit = 900f;
                    level.ResolveLevel();
                }
                bool warming = level == null || level.IsBriefing || !level.IsRunning || _warm < 20;
                Time.captureDeltaTime = warming ? 0.1f : 1f / 60f;
                if (warming) { if (level != null && level.IsRunning) _warm++; return; }

                var player = level.player;
                if (_frames == 0) _playerAt = player.position;
                var health = player.GetComponentInParent<Health>();
                if (health != null && health.Normalized < 0.9f) health.ResetHealth();

                foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                {
                    if (!_seen.ContainsKey(ai)) _seen[ai] = (ai.transform.position, Vector3.Distance(ai.transform.position, player.position));
                }

                // The town's life.
                int civilians = 0;
                foreach (var c in Object.FindObjectsByType<Civilian>(FindObjectsSortMode.None))
                {
                    if (c.Dead || c.Hidden) continue;
                    civilians++;
                    if (c.Current == Civilian.State.Walk) _walked.Add(c);
                }
                _mostCivilians = Mathf.Max(_mostCivilians, civilians);
                var cars = Object.FindObjectsByType<TrafficCar>(FindObjectsSortMode.None);
                _mostCars = Mathf.Max(_mostCars, cars.Length);
                foreach (var car in cars) _driven += car.Speed * Time.deltaTime;

                if (++_frames < Seconds * 60f) return;
                Finish(level);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Finish(null, e.Message);
            }
        }

        static void Finish(LevelManager level, string error = null)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            var lines = new List<string>();
            int fails = 0;
            void Check(bool ok, string what) { lines.Add($"  {(ok ? "ok  " : "FAIL")} {what}"); if (!ok) fails++; }

            if (error == null && level != null)
            {
                var points = level.spawnPoints.Where(p => p != null).Select(p => p.position).ToArray();
                var near = _seen.Values.Select(s => points.Min(p => Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(s.at.x, 0, s.at.z)))).ToArray();
                int atZone = near.Count(d => d < 8f);
                Check(!_warnings.Any(w => w.Contains("Despawn Distance")), $"no leash warning at the start (despawn {level.despawnDistance:0} m; LevelManager said: {(_warnings.Count == 0 ? "nothing" : string.Join(" / ", _warnings))})");
                Check(_seen.Count > 0, $"{_seen.Count} enemies arrived in {Seconds:0} s");
                Check(_seen.Values.All(s => s.fromPlayer >= level.zoneSpawnDistance.x - 2f || s.fromPlayer >= level.minSpawnDistanceFromPlayer - 2f),
                      $"none nearer the player than the near edge ({level.zoneSpawnDistance.x:0} m): nearest {(_seen.Count > 0 ? _seen.Values.Min(s => s.fromPlayer) : 0f):0} m, farthest {(_seen.Count > 0 ? _seen.Values.Max(s => s.fromPlayer) : 0f):0} m");
                lines.Add($"  note {atZone} of {_seen.Count} arrived at a zone point (within 8 m), the rest from the ring round the player");
                Check(!_warnings.Any(w => w.Contains("leash") || w.Contains("Discard") || w.Contains("lost")), "none removed by the leash");
                Check(_mostCivilians >= 10 && _walked.Count >= 5, $"civilians about: {_mostCivilians} at most at once, {_walked.Count} seen walking");
                Check(_mostCars >= 3 && _driven >= 150f, $"traffic: {_mostCars} cars at most, {_driven:0} m driven between them");
                Check(_errors.Count == 0, $"no game errors{(_errors.Count > 0 ? ": " + string.Join(" / ", _errors) : "")}");
            }
            else Check(false, "ran: " + error);

            if (_tuned != null && _clockBackup >= 0f) _tuned.timeLimit = _clockBackup;
            Time.captureDeltaTime = 0f;
            _running = false;
            EditorApplication.isPlaying = false;
            FPSKitPlayMode.RestoreStartScene();
            string report = string.Join("\n", lines);
            if (fails == 0) Debug.Log("[FPSKitBatch] desert spawns passed:\n" + report);
            else Debug.LogError("[FPSKitBatch] FAILED desert spawns:\n" + report);
            FPSKitBatch.Exit(fails == 0 ? 0 : 1);
        }
    }
}
#endif
