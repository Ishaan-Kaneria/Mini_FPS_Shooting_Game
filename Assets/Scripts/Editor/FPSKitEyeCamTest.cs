#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// How the first-person view moves (smooth-animation brief, 2026-10-18, "eye cam"). The player, on a
    /// floor of the test's own, goes through a fixed script -- standing, a careful step, a walk, a jog, a
    /// sprint, a stop, a sidestep, a walk aimed down the sights, a crouched walk, a jump -- three times: at
    /// 30, 60 and 144 frames a second. It reads only world transforms (the camera against the player's
    /// root), so the same test measures whatever code moves the view:
    ///
    /// - the bob, sway, roll and nod in each part (peak to peak), at 60 fps;
    /// - where the head is lowest after each footstep (share of a step);
    /// - the landing's dip and how long it takes to settle;
    /// - the same script at 30 and 144 against 60: the largest difference in any part's bob and roll, which
    ///   is how far the motion depends on the frame rate;
    /// - jitter: the camera's own acceleration in the player's space, frame to frame (99th percentile and
    ///   worst), which a frame-rate-dependent smoothing or a snap shows as a spike.
    ///
    /// Fails if the bob does not grow from a careful step to a sprint, if aiming or crouching do not cut
    /// it to under half of a walk, or if the 30 and 144 runs stray from the 60 one by more than
    /// <see cref="MaxFrameRateDrift"/>.
    ///
    ///   Tools/unity-batch.sh FPSKitBatch.VerifyEyeCam      (Build/EyeCam/report.txt)
    /// </summary>
    public static class FPSKitEyeCamTest
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";
        const string Folder = "Build/EyeCam";
        const float Floor = 400f;
        const float MaxFrameRateDrift = 0.0015f;   // metres of a part's bob between frame rates

        public static bool Running { get; private set; }

        struct Part { public string name; public float seconds; public Vector2 move; public bool sprint, aim, crouch, jump; }

        static readonly Part[] Script =
        {
            new Part { name = "standing", seconds = 3f },
            new Part { name = "careful step", seconds = 3f, move = new Vector2(0f, 0.2f) },
            new Part { name = "walk", seconds = 3f, move = new Vector2(0f, 0.5f) },
            new Part { name = "jog", seconds = 3f, move = new Vector2(0f, 1f) },
            new Part { name = "sprint", seconds = 4f, move = new Vector2(0f, 1f), sprint = true },
            new Part { name = "stop (blown)", seconds = 3f },
            new Part { name = "sidestep", seconds = 2f, move = new Vector2(1f, 0f) },
            new Part { name = "aimed walk", seconds = 3f, move = new Vector2(0f, 0.5f), aim = true },
            new Part { name = "crouched walk", seconds = 3f, move = new Vector2(0f, 1f), crouch = true },
            new Part { name = "standing again", seconds = 2f },
            new Part { name = "jump", seconds = 2f, jump = true },
        };

        struct Sample { public float t; public int part; public Vector3 head; public float pitch, roll, phase; public bool grounded; }

        static readonly Dictionary<int, List<Sample>> Runs = new Dictionary<int, List<Sample>>();
        static IEnumerator _script;
        static int _lastFrame, _warm;
        static double _startedAt;
        static GameObject _player, _floor;
        static PlayerMotor _motor;
        static Transform _cam;
        static LevelSet.Level _tunedLevel;
        static float _clockBackup = -1f, _rate = 60f;
        static string _error;

        public static void Batch() => Run();

        [MenuItem("FPSKit/Player/Measure Eye Camera")]
        public static void Run()
        {
            if (Running || EditorApplication.isPlaying) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Runs.Clear();
            _script = null; _lastFrame = -1; _warm = 0; _player = null; _error = null;
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
                if (EditorApplication.timeSinceStartup - _startedAt > 900) throw new Exception("eye camera test timed out");
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
                Time.captureDeltaTime = warming ? 0.1f : 1f / _rate;
                if (warming) { if (level != null && level.IsRunning) _warm++; return; }

                foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) ai.gameObject.SetActive(false);
                if (_script == null) _script = All();
                if (!_script.MoveNext()) Finish();
            }
            catch (Exception e)
            {
                _error = e.Message;
                Debug.LogException(e);
                Finish();
            }
        }

        static IEnumerator All()
        {
            _player = GameObject.FindGameObjectWithTag("Player");
            _motor = _player.GetComponent<PlayerMotor>();
            _cam = Camera.main.transform;
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "EyeCam_Floor";
            _floor.transform.position = new Vector3(0f, Floor - 0.5f, 0f);
            _floor.transform.localScale = new Vector3(400f, 1f, 400f);
            MobileInput.Reset();
            MobileInput.Active = true;

            foreach (float rate in new[] { 60f, 30f, 144f })
            {
                _rate = rate;
                var cc = _player.GetComponent<CharacterController>();
                cc.enabled = false;
                _player.transform.SetPositionAndRotation(new Vector3(0f, Floor + 0.05f, -150f), Quaternion.identity);
                cc.enabled = true;
                Physics.SyncTransforms();
                _motor.TurnTowards(new Vector3(0f, Floor + 1.6f, 100f), 1f);
                MobileInput.Reset();
                MobileInput.Active = true;
                for (int i = 0; i < 90; i++) yield return null;   // settle, and the breath to rest

                var samples = new List<Sample>();
                Runs[(int)rate] = samples;
                float start = Time.time;
                for (int p = 0; p < Script.Length; p++)
                {
                    var part = Script[p];
                    MobileInput.Move = part.move;
                    MobileInput.SetSprintButton(part.sprint);
                    MobileInput.Aim = part.aim;
                    MobileInput.Crouch = part.crouch;
                    if (part.jump) MobileInput.QueueJump();
                    float end = Time.time + part.seconds;
                    while (Time.time < end - 1e-4f)
                    {
                        yield return null;
                        Vector3 head = _player.transform.InverseTransformPoint(_cam.position);
                        Vector3 e = (Quaternion.Inverse(_player.transform.rotation) * _cam.rotation).eulerAngles;
                        samples.Add(new Sample { t = Time.time - start, part = p, head = head, pitch = Wrap(e.x), roll = Wrap(e.z), phase = _motor.StepPhase, grounded = _motor.IsGrounded });
                    }
                }
                MobileInput.Reset();
                MobileInput.Active = true;
            }
        }

        static float Wrap(float a) => a > 180f ? a - 360f : a;

        static float P2P(IEnumerable<float> v) { var l = v.ToList(); return l.Count == 0 ? 0f : l.Max() - l.Min(); }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Directory.CreateDirectory(Folder);
            var lines = new List<string>();
            bool ok = _error == null && Runs.ContainsKey(60) && Runs.ContainsKey(30) && Runs.ContainsKey(144);
            if (ok)
            {
                var r60 = Runs[60];
                lines.Add("At 60 fps, the last two seconds of each part (peak to peak; the head against the player's root):");
                var bob = new float[Script.Length];
                for (int p = 0; p < Script.Length; p++)
                {
                    float end = r60.Where(s => s.part == p).Max(s => s.t);
                    var w = r60.Where(s => s.part == p && s.t > end - 2f).ToList();
                    // The crouched walk's eye is lower: its own mean, not the standing one.
                    bob[p] = P2P(w.Select(s => s.head.y));
                    lines.Add($"  {Script[p].name,-14} up-down {bob[p] * 1000f,5:0.0} mm  side {P2P(w.Select(s => s.head.x)) * 1000f,5:0.0} mm  roll {P2P(w.Select(s => s.roll)),4:0.00} deg  pitch {P2P(w.Select(s => s.pitch)),4:0.00} deg");
                }
                int Index(string n) => Array.FindIndex(Script, x => x.name == n);
                bool grows = bob[Index("careful step")] < bob[Index("walk")] && bob[Index("walk")] < bob[Index("jog")] && bob[Index("jog")] < bob[Index("sprint")];
                bool calm = bob[Index("aimed walk")] < 0.5f * bob[Index("walk")] && bob[Index("crouched walk")] < 0.5f * bob[Index("jog")];
                lines.Add($"  grows from a careful step to a sprint: {(grows ? "yes" : "NO")}; aimed and crouched under half: {(calm ? "yes" : "NO")}");
                ok &= grows && calm;

                // Where the head is lowest after each footstep, in shares of a step, over the jog.
                var jog = r60.Where(s => s.part == Index("jog")).ToList();
                var lows = new List<float>();
                for (int i = 1; i < jog.Count - 1; i++)
                    if (jog[i].head.y < jog[i - 1].head.y && jog[i].head.y <= jog[i + 1].head.y) lows.Add(Mathf.Repeat(jog[i].phase, 1f));
                lines.Add(lows.Count > 0
                    ? $"  jogging, the head is lowest {lows.Average():0.00} of a step after each footstep (spread {P2P(lows):0.00}, {lows.Count} steps)"
                    : "  jogging, no low point found");

                // The landing: from the first frame back on the ground after being off it.
                var jump = r60.Where(s => s.part == Index("jump")).ToList();
                int air = jump.FindIndex(s => !s.grounded);
                int land = air < 0 ? -1 : jump.FindIndex(air, s => s.grounded);
                if (land > 0)
                {
                    float rest = jump.Last().head.y;
                    float dipMm = (rest - jump.Skip(land).Min(s => s.head.y)) * 1000f;
                    int settled = jump.FindLastIndex(s => Mathf.Abs(s.head.y - rest) > 0.002f);
                    lines.Add($"  landing: the head dips {dipMm:0.0} mm below standing and is back within 2 mm {Mathf.Max(0f, jump[Mathf.Max(settled, land)].t - jump[land].t):0.00} s after touching down");
                }
                else lines.Add("  landing: no landing found");

                // Frame rate: every part's bob and roll at 30 and 144 against 60 (the jump's arc is the controller's, not the view's).
                foreach (int rate in new[] { 30, 144 })
                {
                    var other = Runs[rate];
                    float drift = 0f, rollDrift = 0f;
                    for (int p = 0; p < Script.Length; p++)
                    {
                        if (Script[p].jump || Script[p].name == "standing again") continue;   // the controller's own arc and rise
                        List<Sample> Last2(List<Sample> r) { float end = r.Where(x => x.part == p).Max(x => x.t); return r.Where(x => x.part == p && x.t > end - 2f).ToList(); }
                        var a = Last2(r60); var b = Last2(other);
                        drift = Mathf.Max(drift, Mathf.Abs(P2P(a.Select(x => x.head.y)) - P2P(b.Select(x => x.head.y))));
                        rollDrift = Mathf.Max(rollDrift, Mathf.Abs(P2P(a.Select(x => x.roll)) - P2P(b.Select(x => x.roll))));
                    }
                    bool same = drift <= MaxFrameRateDrift;
                    ok &= same;
                    lines.Add($"  {rate} fps against 60: a part's bob differs by at most {drift * 1000f:0.0} mm, its roll by {rollDrift:0.00} deg{(same ? "" : "  <-- FAIL")}");
                }

                // Jitter: the head's acceleration in the player's space, frame to frame.
                foreach (int rate in new[] { 30, 60, 144 })
                {
                    var r = Runs[rate];
                    var acc = new List<float>();
                    float worst = 0f; int worstPart = 0;
                    for (int i = 1; i < r.Count - 1; i++)
                    {
                        if (r[i - 1].part != r[i + 1].part) continue;
                        float h = r[i].t - r[i - 1].t, h2 = r[i + 1].t - r[i].t;
                        if (h <= 0f || h2 <= 0f) continue;
                        Vector3 a = ((r[i + 1].head - r[i].head) / h2 - (r[i].head - r[i - 1].head) / h) / (0.5f * (h + h2));
                        acc.Add(a.magnitude);
                        if (a.magnitude > worst) { worst = a.magnitude; worstPart = r[i].part; }
                    }
                    acc.Sort();
                    lines.Add($"  jitter at {rate,3} fps: head acceleration 99th percentile {acc[(int)(0.99f * (acc.Count - 1))],6:0.00} m/s², worst {acc[acc.Count - 1],6:0.00} m/s² in '{Script[worstPart].name}' ({r.Count} frames)");
                }
            }

            string report = (ok ? "eye camera test passed" : "eye camera test FAILED")
                            + (_error != null ? "\n  error: " + _error : "") + "\n" + string.Join("\n", lines);
            File.WriteAllText($"{Folder}/report.txt", report);
            if (ok) Debug.Log("[FPSKitBatch] " + report); else Debug.LogError("[FPSKitBatch] FAILED: " + report);

            MobileInput.Reset();
            MobileInput.Active = false;
            if (_floor != null) Object.Destroy(_floor);
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
