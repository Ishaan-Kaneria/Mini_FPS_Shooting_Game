#if UNITY_EDITOR
using System;
using System.Collections;
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
    /// The melee rules from the combat audit (2026-10-18), each proved on the real player:
    ///
    /// - A4: a blow lands only if the target can still be hit when it arrives -- not through a wall
    ///   that came between during the swing, not on a target that stepped out of reach (more than
    ///   <c>impactTolerance</c> past the range); but on one that stepped back a little.
    /// - A5: a swing is <c>AttackStarted</c> and a quiet whoosh; only a blow that lands is
    ///   <c>Landed</c> and the loud Violence noise; a miss is <c>Missed</c> and makes no violence.
    /// - A6: a press queued before a pause is not thrown when play resumes (and without the pause, it
    ///   is: the queue still works); in a crowd whose hitboxes overflow the old 32-collider buffer,
    ///   the enemy under the crosshair is still the one found.
    /// - A7: a kicked enemy that cannot be thrown is driven back <c>kickKnockback</c> metres.
    ///
    /// Real player from level one of the Warehouse, on a flat floor of the test's own high above it.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyMeleeRules        (or FPSKit > Player > Run Melee Rules Test)
    ///
    /// Report: Build/MeleeRules/report.txt.
    /// </summary>
    public static class FPSKitMeleeRulesTest
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";
        const string EnemyPrefab = "Assets/FPSKit_Generated/Enemy.prefab";
        const string ReportPath = "Build/MeleeRules/report.txt";
        const float FloorHeight = 400f, FloorSize = 80f;

        public static bool Running { get; private set; }

        static readonly List<string> Lines = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static readonly Stack<IEnumerator> Script = new Stack<IEnumerator>();
        static readonly List<GameObject> Spawned = new List<GameObject>();
        static int _lastFrame, _warm;
        static double _startedAt;
        static GameObject _player, _floor;
        static PlayerMotor _motor;
        static MeleeStrike _melee;
        static Vector3 _start;
        static LevelSet.Level _tunedLevel;
        static float _clockBackup = -1f;

        // What the strike said and the noise it made.
        static int _started, _landed, _missed, _noiseAfter;
        static Health _startedAt0, _landedOn;

        [MenuItem("FPSKit/Player/Run Melee Rules Test")]
        public static void Run()
        {
            if (Running || EditorApplication.isPlaying) return;
            try
            {
                if (EditorSceneManager.GetActiveScene().isDirty) throw new Exception("save the open scene first");
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Lines.Clear(); Fails.Clear(); Script.Clear(); Spawned.Clear();
                _lastFrame = -1; _warm = 0; _player = null;
                _startedAt = EditorApplication.timeSinceStartup;
                Running = true;
                SaveMigration.Apply();
                FPSKitPlayMode.SuspendStartScene();
                EditorApplication.update += Tick;
                EditorApplication.isPlaying = true;
            }
            catch (Exception e)
            {
                Debug.LogError("[FPSKitBatch] FAILED: melee rules test: " + e.Message);
                FPSKitBatch.Exit(1);
            }
        }

        public static void Batch() => Run();

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _startedAt > 900) throw new Exception("melee rules test timed out");
                if (!EditorApplication.isPlaying || Time.frameCount == _lastFrame) return;
                _lastFrame = Time.frameCount;

                var level = Object.FindAnyObjectByType<LevelManager>();
                if (level != null && level.Level != null && _tunedLevel == null)
                {
                    _tunedLevel = level.Level;
                    _clockBackup = level.Level.timeLimit;
                    level.Level.timeLimit = 900f;
                    level.ResolveLevel();
                }
                bool warming = level == null || level.IsBriefing || !level.IsRunning || _warm < 20;
                if (Time.timeScale > 0f) Time.captureDeltaTime = warming ? 0.1f : 1f / 60f;
                if (warming) { if (level != null && level.IsRunning) _warm++; return; }

                if (_player == null) { Setup(); Script.Push(Scenarios()); }
                foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                    if (!Spawned.Contains(ai.gameObject)) ai.gameObject.SetActive(false);
                var h = _player.GetComponent<Health>();
                if (h != null && h.Normalized < 0.9f) h.ResetHealth();

                while (Script.Count > 0)
                {
                    var top = Script.Peek();
                    if (!top.MoveNext()) { Script.Pop(); continue; }
                    if (top.Current is IEnumerator sub) { Script.Push(sub); continue; }
                    return;   // wait a frame
                }
                Finish();
            }
            catch (Exception e)
            {
                Fails.Add("error: " + e.Message);
                Debug.LogException(e);
                Finish();
            }
        }

        static void Setup()
        {
            _player = GameObject.FindGameObjectWithTag("Player");
            if (_player == null) throw new Exception("no player");
            _motor = _player.GetComponent<PlayerMotor>();
            _melee = _player.GetComponent<MeleeStrike>();
            _melee.AttackStarted += (m, s, t) => { _started++; _startedAt0 = t; };
            _melee.Landed += (m, s, t) => { _landed++; _landedOn = t; };
            _melee.Missed += (m, s) => _missed++;

            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "MeleeRules_Floor";
            _floor.transform.position = new Vector3(0f, FloorHeight - 0.5f, 0f);
            _floor.transform.localScale = new Vector3(FloorSize, 1f, FloorSize);
            int env = LayerMask.NameToLayer("Environment");
            if (env >= 0) _floor.layer = env;
            var surface = _floor.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            surface.collectObjects = Unity.AI.Navigation.CollectObjects.Children;
            surface.BuildNavMesh();
            _start = new Vector3(0f, FloorHeight, 0f);
        }

        static void Require(bool ok, string what)
        {
            Lines.Add((ok ? "  PASS  " : "  FAIL  ") + what);
            if (!ok) Fails.Add(what);
        }

        // ------------------------------------------------------------------

        static IEnumerator Scenarios()
        {
            var s = MeleeSettings.Active;

            // A5 / A4: in reach and staying there -- lands, loud.
            yield return Fresh();
            var t = Enemy(Ahead(1.9f));
            yield return Frames(2);
            Count();
            var aimed = _melee.TryPunch();
            yield return Seconds(0.6f);
            Require(_started == 1 && _startedAt0 == t.GetComponent<Health>() && aimed != null, $"A5 in reach: one AttackStarted, aimed at the target ({_started}, {(aimed != null ? aimed.name : "nobody")})");
            Require(_landed == 1 && _missed == 0, $"A5 in reach: the blow is Landed, not Missed (landed {_landed}, missed {_missed})");
            Require(Heard(NoiseKind.Violence) == 1, $"A5 a landed blow is heard as violence ({Heard(NoiseKind.Violence)} violence noise)");
            Require(HeardSwing(), "A5 the swing is a quiet whoosh (movement noise, 3 m)");

            // A5: nobody there -- a miss, no violence.
            yield return Fresh();
            yield return Frames(2);
            Count();
            _melee.TryPunch();
            yield return Seconds(0.6f);
            Require(_started == 1 && _startedAt0 == null, "A5 nobody there: AttackStarted with no target");
            Require(_missed == 1 && _landed == 0, $"A5 nobody there: Missed, not Landed (missed {_missed}, landed {_landed})");
            Require(Heard(NoiseKind.Violence) == 0, $"A5 a miss makes no violence noise ({Heard(NoiseKind.Violence)})");

            // A4: a wall comes between during the swing.
            yield return Fresh();
            t = Enemy(Ahead(1.9f));
            var hp = t.GetComponent<Health>();
            yield return Frames(2);
            Count();
            float before = hp.Current + hp.Shield;
            var found = _melee.TryPunch();
            yield return Frames(1);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "MeleeRules_Wall";
            wall.transform.position = Ahead(0.85f) + Vector3.up * 1.2f;
            wall.transform.rotation = Quaternion.LookRotation(Forward());
            wall.transform.localScale = new Vector3(3f, 2.6f, 0.15f);
            int env = LayerMask.NameToLayer("Environment");
            if (env >= 0) wall.layer = env;
            Spawned.Add(wall);
            Physics.SyncTransforms();
            yield return Seconds(0.6f);
            Require(found == hp, "A4 wall: the target was in reach and in sight when the swing started");
            Require(_missed == 1 && _landed == 0 && Mathf.Approximately(before, hp.Current + hp.Shield),
                    $"A4 wall: an enemy behind a wall that came up during the swing is not hit (missed {_missed}, landed {_landed}, pool {before:0} -> {hp.Current + hp.Shield:0})");
            Require(Heard(NoiseKind.Violence) == 0, "A4 wall: and nothing is heard as violence");
            Object.Destroy(wall);
            Spawned.Remove(wall);

            // A4: stepping away during the swing, past the tolerance and within it.
            foreach (var past in new[] { 0.6f, 0.1f })
            {
                yield return Fresh();
                t = Enemy(Ahead(1.9f));
                hp = t.GetComponent<Health>();
                yield return Frames(2);
                Count();
                _melee.TryPunch();
                yield return Frames(1);
                float now = ClosestDistance(t);
                MoveTarget(t, Forward() * (s.punchRange + past - now));
                float moved = ClosestDistance(t);
                yield return Seconds(0.6f);
                bool shouldLand = past <= s.impactTolerance;
                Require(shouldLand ? _landed == 1 : _missed == 1 && _landed == 0,
                        $"A4 stepped back to {moved:0.00} m (range {s.punchRange:0.0} + {past:0.0}; tolerance {s.impactTolerance:0.00}): {(shouldLand ? "still hit" : "missed")} (landed {_landed}, missed {_missed})");
            }

            // A6: a press queued during the recovery is thrown; one queued before a pause is not.
            foreach (bool pause in new[] { false, true })
            {
                yield return Fresh();
                t = Enemy(Ahead(1.9f));
                yield return Frames(2);
                Count();
                _melee.TryPunch();
                yield return Frames(2);
                MobileInput.QueueMelee();   // read next frame, while the butt is still recovering: queued
                yield return Frames(2);
                var director = Object.FindAnyObjectByType<GameDirector>();
                if (pause)
                {
                    if (director != null) director.SetPaused(true);
                    else { PlayerMotor.InputEnabled = false; Time.timeScale = 0f; }
                    yield return Frames(30);
                    if (director != null) director.SetPaused(false);
                    else { PlayerMotor.InputEnabled = true; Time.timeScale = 1f; }
                    Time.captureDeltaTime = 1f / 60f;
                }
                yield return Seconds(0.8f);
                Require(pause ? _started == 1 : _started == 2,
                        pause ? $"A6 a press queued before a pause is not thrown after it ({_started} strike(s), {(director != null ? "GameDirector pause" : "timeScale 0")})"
                              : $"A6 control: without a pause the queued press is thrown ({_started} strikes)");
            }

            // A6: a crowd overflowing the old 32-collider buffer; the one under the crosshair is still found.
            for (int order = 0; order < 2; order++)
            {
                yield return Fresh();
                GameObject target = null;
                if (order == 0) target = Enemy(Ahead(1.8f));
                for (int i = 0; i < 10; i++)
                {
                    float angle = 70f + i * 22f;   // round the sides and back, out of the 45-degree cone
                    Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * Forward();
                    Enemy(_start + dir * 2.0f);
                }
                if (order == 1) target = Enemy(Ahead(1.8f));
                yield return Frames(3);
                int inReach = Physics.OverlapSphere(_melee.fpsCamera.transform.position, s.punchRange, 1 << LayerMask.NameToLayer("Enemy"), QueryTriggerInteraction.Ignore).Length;
                var got = _melee.TryPunch();
                yield return Seconds(0.5f);
                Require(got == target.GetComponent<Health>(),
                        $"A6 crowd ({(order == 0 ? "target spawned first" : "target spawned last")}): {inReach} enemy colliders in reach (the old buffer held 32), the one ahead is the one hit ({(got != null ? got.name : "nobody")})");
            }

            // A7: a kicked enemy that cannot be thrown is driven back kickKnockback metres.
            yield return Fresh();
            t = Enemy(Ahead(2.0f));
            var ai = t.GetComponent<EnemyAI>();
            yield return Frames(2);
            ai.Knocked = null;   // as a body with no ragdoll (after its Start, which hooks EnemyBody.Launch)
            ai.target = _player.transform;
            Count();
            Vector3 from = t.transform.position;
            _melee.TryKick();
            for (int i = 0; i < 60 && _landed == 0; i++) yield return null;
            ai.enabled = true;   // staggered: it only takes the shove (EnemyAI.Update)
            yield return Seconds(0.9f);
            ai.enabled = false;
            float driven = Vector3.ProjectOnPlane(t.transform.position - from, Vector3.up).magnitude;
            Require(_landed == 1 && Mathf.Abs(driven - s.kickKnockback) < 0.5f,
                    $"A7 a kick on a body that cannot fly drives it back kickKnockback ({s.kickKnockback:0.0} m): {driven:0.00} m (landed {_landed})");
        }

        // ------------------------------------------------------------------

        static IEnumerator Fresh()
        {
            foreach (var go in Spawned) if (go != null) Object.Destroy(go);
            Spawned.Clear();
            MobileInput.Reset();
            MobileInput.Active = true;
            GameInput.SetScheme(InputScheme.Touch);
            // Cold: no strike in flight, the chain over, the gun back in the hands.
            float waited = 0f;
            while ((_melee.Current != MeleeStrike.Strike.None || _melee.GunAway > 0f || _melee.FistsOut || Time.time < _melee.ReadyAt + 1.6f) && waited < 6f)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            var cc = _player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            _player.transform.position = _start + Vector3.up * 0.05f;
            if (cc != null) cc.enabled = true;
            Physics.SyncTransforms();
            _motor.TurnTowards(_start + Vector3.up * 1.6f + Vector3.forward * 20f, 1f);
            yield return Frames(3);
        }

        static void Count()
        {
            _started = _landed = _missed = 0;
            _startedAt0 = _landedOn = null;
            _noiseAfter = NoiseSystem.Latest;
        }

        static int Heard(NoiseKind kind)
        {
            int n = 0;
            NoiseSystem.Listen(_player.transform.position, 1000f, _noiseAfter, (noise, d) => { if (noise.kind == kind && noise.source == _player) n++; });
            return n;
        }

        static bool HeardSwing()
        {
            bool swing = false;
            NoiseSystem.Listen(_player.transform.position, 1000f, _noiseAfter, (noise, d) =>
            {
                if (noise.kind == NoiseKind.Movement && noise.source == _player && noise.radius <= 3.01f && noise.radius > 0f) swing = true;
            });
            return swing;
        }

        static Vector3 Forward() => Vector3.ProjectOnPlane(_player.transform.forward, Vector3.up).normalized;
        static Vector3 Ahead(float metres) => _start + Forward() * metres;

        static GameObject Enemy(Vector3 at)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefab);
            if (NavMesh.SamplePosition(at, out var hit, 1f, NavMesh.AllAreas)) at = hit.position;
            Vector3 face = _start - at; face.y = 0f;
            var go = Object.Instantiate(prefab, at, Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face : Vector3.back));
            go.name = "MeleeRules_Enemy" + Spawned.Count;
            Spawned.Add(go);
            FPSKitEnemyRoster.GetOrCreate("Grunt").ApplyTo(go);
            var ai = go.GetComponent<EnemyAI>();
            if (ai != null) ai.enabled = false;
            var brain = go.GetComponent<EnemyBrain>();
            if (brain != null) brain.enabled = false;
            Physics.SyncTransforms();
            return go;
        }

        static float ClosestDistance(GameObject target)
        {
            Vector3 eye = _melee.fpsCamera.transform.position;
            float best = float.MaxValue;
            foreach (var c in target.GetComponentsInChildren<Collider>())
                if (!c.isTrigger && c.enabled) best = Mathf.Min(best, (c.ClosestPoint(eye) - eye).magnitude);
            return best;
        }

        static void MoveTarget(GameObject target, Vector3 by)
        {
            var agent = target.GetComponent<NavMeshAgent>();
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Warp(target.transform.position + by);
            else target.transform.position += by;
            Physics.SyncTransforms();
        }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
        static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            foreach (var go in Spawned) if (go != null) Object.Destroy(go);
            if (_floor != null) Object.Destroy(_floor);
            if (_tunedLevel != null && _clockBackup > 0f) _tunedLevel.timeLimit = _clockBackup;
            _tunedLevel = null; _clockBackup = -1f;
            MobileInput.Reset();
            MobileInput.Active = false;
            GameInput.SetScheme(InputScheme.KeyboardMouse);
            PlayerMotor.InputEnabled = true;
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            Running = false;

            string report = (Fails.Count == 0 ? "melee rules test passed\n" : $"melee rules test FAILED ({Fails.Count}):\n") + string.Join("\n", Lines);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report);
            if (Fails.Count == 0) Debug.Log("[FPSKitBatch] " + report);
            else Debug.LogError("[FPSKitBatch] FAILED: " + report);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += FPSKitPlayMode.RestoreStartScene;
            FPSKitBatch.Exit(Fails.Count == 0 ? 0 : 1);
        }
    }
}
#endif
