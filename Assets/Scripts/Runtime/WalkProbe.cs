using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The frame-time benchmark of the realism work (Part 3 of the 2026-10-28 brief: stable 60 fps on Medium, the 1% low never
/// under 50, no frame over 33 ms). Does nothing unless the player is started with <c>-fpskitwalk &lt;report file&gt;</c>
/// (optionally <c>-fpskitquality Low|Medium|High</c>, <c>-fpskitseconds N</c>, default 120). It opens Desert Outpost, waits
/// for the level, and jogs the player along WalkRoute (the walk the realism audit looks along) through the touch controls'
/// inputs, looking a little to each side as it goes, for exactly two minutes, as the game is played: at the game's own frame
/// rate cap, enemies and town life running, the player kept alive. It reports the frame rate, the 1% and 0.1% lows, the
/// worst frame and how many frames took over 33 ms (and 25), CPU and GPU time a frame, garbage collections, memory, and
/// where on the walk each of the worst frames came, then quits. The tier asked for is set as the player's own and put back
/// after. Built with FPSKitPerfBuild.BuildSmooth; run by Tools/walk-bench.sh. Holds no static state.
/// On battery it refuses (the chip runs at lower power limits: the step-1 walk ran a third slower on battery with an
/// identical scene, 2026-10-29) unless started with <c>-fpskitallowbattery</c>, and the power source is in every report.
/// It also records what is drawn each frame (batches, SetPass calls, triangles, shadow casters, where the release player
/// reports them) and the main and render threads' time, so a slow frame can be put down to the CPU or the GPU.
/// </summary>
public class WalkProbe : MonoBehaviour
{
    string _out, _tier;
    float _seconds = 120f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-fpskitwalk");
        if (i < 0 || i + 1 >= args.Length) return;
        var go = new GameObject("WalkProbe");
        DontDestroyOnLoad(go);
        var probe = go.AddComponent<WalkProbe>();
        probe._out = args[i + 1];
        int q = Array.IndexOf(args, "-fpskitquality");
        if (q >= 0 && q + 1 < args.Length) probe._tier = args[q + 1];
        int s = Array.IndexOf(args, "-fpskitseconds");
        if (s >= 0 && s + 1 < args.Length && float.TryParse(args[s + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sec)) probe._seconds = sec;
    }

    struct Slow { public float ms, at, metres; public bool gc; }

    /// <summary>The power source: "battery" when running on it. Unity's battery status first; on Linux, where it may be
    /// Unknown, the kernel's power supplies (a mains or USB input online, else a discharging battery).</summary>
    public static string PowerSource()
    {
        switch (SystemInfo.batteryStatus)
        {
            case BatteryStatus.Discharging: return "battery";
            case BatteryStatus.Charging: case BatteryStatus.Full: case BatteryStatus.NotCharging: return "mains";
        }
        try
        {
            const string root = "/sys/class/power_supply";
            if (!Directory.Exists(root)) return "mains (no battery reported)";
            bool input = false, discharging = false;
            foreach (var d in Directory.GetDirectories(root))
            {
                string Read(string f) { var p = Path.Combine(d, f); return File.Exists(p) ? File.ReadAllText(p).Trim() : ""; }
                string type = Read("type");
                if ((type == "Mains" || type == "USB") && Read("online") == "1") input = true;
                if (type == "Battery" && Read("status") == "Discharging") discharging = true;
            }
            return input ? "mains" : discharging ? "battery" : "mains (no battery reported)";
        }
        catch (Exception) { return "unknown"; }
    }

    /// <summary>The render counters a frame draws (ProfilerRecorder; some players do not report all of them).</summary>
    public sealed class DrawCounters : IDisposable
    {
        public static readonly string[] Names = { "Batches Count", "SetPass Calls Count", "Draw Calls Count", "Triangles Count", "Vertices Count", "Shadow Casters Count" };
        readonly Unity.Profiling.ProfilerRecorder[] _rec = new Unity.Profiling.ProfilerRecorder[Names.Length];
        public readonly List<long>[] Values = Names.Select(_ => new List<long>()).ToArray();
        public DrawCounters()
        {
            for (int i = 0; i < Names.Length; i++) _rec[i] = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, Names[i]);
        }
        public void Sample()
        {
            for (int i = 0; i < Names.Length; i++) if (_rec[i].Valid && _rec[i].LastValue > 0) Values[i].Add(_rec[i].LastValue);
        }
        public void Clear() { foreach (var v in Values) v.Clear(); }
        public string Report()
        {
            var parts = new List<string>();
            for (int i = 0; i < Names.Length; i++)
            {
                string n = Names[i].Replace(" Count", "").ToLowerInvariant();
                if (Values[i].Count == 0) { parts.Add($"{n} not reported"); continue; }
                var o = Values[i].OrderBy(x => x).ToArray();
                parts.Add($"{n} {Big(o[o.Length / 2])} (p95 {Big(o[(int)(o.Length * 0.95f)])})");
            }
            return string.Join(" | ", parts);
        }
        public long Median(int i) { var v = Values[i]; if (v.Count == 0) return -1; var o = v.OrderBy(x => x).ToArray(); return o[o.Length / 2]; }
        public void Dispose() { foreach (var r in _rec) r.Dispose(); }
    }

    public static string Big(long v) => v >= 1_000_000 ? $"{v / 1e6:0.0}M" : v >= 10_000 ? $"{v / 1e3:0}k" : v.ToString();

    IEnumerator Start()
    {
        var sb = new StringBuilder();
        if (PowerSource() == "battery" && Array.IndexOf(Environment.GetCommandLineArgs(), "-fpskitallowbattery") < 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_out)));
            File.WriteAllText(_out, "refused: on battery (frame times on battery do not compare with mains); plug in, or pass -fpskitallowbattery\n");
            Application.Quit(3);
            yield break;
        }
        int savedTier = GameSettings.QualityTier;
        if (_tier != null && Enum.TryParse(_tier, true, out GameSettings.Quality wanted)) GameSettings.QualityTier = (int)wanted;
        if (SceneManager.GetActiveScene().name != "DesertOutpost")
        {
            SceneManager.LoadScene("DesertOutpost");
            yield return new WaitForSecondsRealtime(1f);
        }
        float wait = Time.realtimeSinceStartup + 60f;
        LevelManager level = null;
        while (Time.realtimeSinceStartup < wait && (level == null || level.IsBriefing || !level.IsRunning))
        {
            level = FindAnyObjectByType<LevelManager>();
            yield return null;
        }
        yield return new WaitForSecondsRealtime(3f);

        var player = GameObject.FindWithTag("Player");
        var motor = player != null ? player.GetComponent<PlayerMotor>() : null;
        var health = player != null ? player.GetComponent<Health>() : null;
        var notes = new List<string>();
        var line = player != null ? WalkRoute.Line(player.transform.position, WalkRoute.Desert, notes) : new List<Vector3>();
        sb.AppendLine($"walk: {WalkRoute.Length(line):0} m of route, {_seconds:0} s at a jog | api {SystemInfo.graphicsDeviceType} | gpu {SystemInfo.graphicsDeviceName} | " +
                      $"{Screen.width}x{Screen.height} | quality {QualitySettings.names[QualitySettings.GetQualityLevel()]} | render scale {QualityTiers.TierRenderScale:0.00} | " +
                      $"cap {(Application.targetFrameRate > 0 ? Application.targetFrameRate.ToString() : "none")} vsync {QualitySettings.vSyncCount} | dev build {Debug.isDebugBuild}");
        sb.AppendLine($"cpu {SystemInfo.processorModel} x{SystemInfo.processorCount} | ram {SystemInfo.systemMemorySize} MB | power {PowerSource()} (battery {SystemInfo.batteryLevel:P0})");
        foreach (var n in notes) sb.AppendLine("route: " + n);

        MobileInput.Reset();
        MobileInput.Active = true;
        var dt = new List<float>(); var cpu = new List<float>(); var gpu = new List<float>();
        var main = new List<float>(); var render = new List<float>(); var presentWait = new List<float>();
        using var draws = new DrawCounters();
        var slow = new List<Slow>();
        var timings = new FrameTiming[1];
        int gcStart = GC.CollectionCount(0), stalls = 0, seg = 1;
        float start = Time.realtimeSinceStartup, walked = 0f, stuckSince = start;
        Vector3 lastPos = player != null ? player.transform.position : Vector3.zero, checkPos = lastPos;
        int lastGc = gcStart;
        while (player != null && line.Count > 1 && Time.realtimeSinceStartup - start < _seconds)
        {
            var pos = player.transform.position;
            // Steer for the route a few metres ahead; at its end, walk it back.
            while (seg < line.Count && Vector3.ProjectOnPlane(line[seg] - pos, Vector3.up).magnitude < 2.5f) seg++;
            if (seg >= line.Count) { line.Reverse(); seg = 1; }
            var dir = Vector3.ProjectOnPlane(line[seg] - pos, Vector3.up).normalized;
            float t = Time.realtimeSinceStartup - start;
            var look = Quaternion.Euler(0f, 30f * Mathf.Sin(t * Mathf.PI * 2f / 9f), 0f) * dir;
            motor.TurnTowards(pos + look * 20f + Vector3.up * 1.6f, 0.15f);
            // Forward along the route in the body's own frame.
            var local = player.transform.InverseTransformDirection(dir);
            MobileInput.Move = new Vector2(local.x, local.z).normalized;
            if (health != null) health.Heal(1000f);
            // Held up (a crowd, a corner): after two seconds without a metre of progress, step on along the route.
            if (Vector3.Distance(pos, checkPos) > 1f) { checkPos = pos; stuckSince = Time.realtimeSinceStartup; }
            else if (Time.realtimeSinceStartup - stuckSince > 2f)
            {
                var cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                player.transform.position = Vector3.MoveTowards(pos, line[seg], 2f) + Vector3.up * 0.1f;
                if (cc != null) cc.enabled = true;
                stalls++; checkPos = player.transform.position; stuckSince = Time.realtimeSinceStartup;
            }
            yield return null;
            float ms = Time.unscaledDeltaTime * 1000f;
            walked += Vector3.Distance(lastPos, player.transform.position);
            lastPos = player.transform.position;
            int gcNow = GC.CollectionCount(0);
            dt.Add(ms);
            if (ms > 25f) slow.Add(new Slow { ms = ms, at = Time.realtimeSinceStartup - start, metres = walked, gc = gcNow != lastGc });
            lastGc = gcNow;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                if (timings[0].cpuFrameTime > 0) cpu.Add((float)timings[0].cpuFrameTime);
                if (timings[0].gpuFrameTime > 0) gpu.Add((float)timings[0].gpuFrameTime);
                if (timings[0].cpuMainThreadFrameTime > 0) main.Add((float)timings[0].cpuMainThreadFrameTime);
                if (timings[0].cpuRenderThreadFrameTime > 0) render.Add((float)timings[0].cpuRenderThreadFrameTime);
                if (timings[0].cpuMainThreadPresentWaitTime > 0) presentWait.Add((float)timings[0].cpuMainThreadPresentWaitTime);
            }
            draws.Sample();
        }
        MobileInput.Reset();

        if (dt.Count > 0)
        {
            var s = dt.OrderBy(x => x).ToArray();
            float P(float p) => s[Mathf.Clamp((int)(p * (s.Length - 1)), 0, s.Length - 1)];
            float Low(float share) { int n = Mathf.Max(1, (int)(s.Length * share)); return 1000f / s.Skip(s.Length - n).Average(); }
            float Pc(List<float> v, float p) { if (v.Count == 0) return 0f; var o = v.OrderBy(x => x).ToArray(); return o[Mathf.Clamp((int)(p * (o.Length - 1)), 0, o.Length - 1)]; }
            sb.AppendLine($"frames {dt.Count} over {dt.Sum() / 1000f:0.0} s, walked {walked:0} m ({stalls} stall(s) stepped past)");
            sb.AppendLine($"avg {1000f / dt.Average():0.0} fps | 1% low {Low(0.01f):0.0} fps | 0.1% low {Low(0.001f):0.0} fps | worst frame {s[s.Length - 1]:0.0} ms | " +
                          $"frames over 33 ms: {dt.Count(x => x > 33.3f)} | over 25 ms: {dt.Count(x => x > 25f)}");
            sb.AppendLine($"frame time p50 {P(0.5f):0.0} / p95 {P(0.95f):0.0} / p99 {P(0.99f):0.0} ms | cpu p50 {Pc(cpu, 0.5f):0.0} / p99 {Pc(cpu, 0.99f):0.0} ms | gpu p50 {Pc(gpu, 0.5f):0.0} / p99 {Pc(gpu, 0.99f):0.0} ms");
            // The main thread waiting on present is the GPU (or vsync) holding it up; its own work is main minus the wait.
            sb.AppendLine($"threads p50: main {Pc(main, 0.5f):0.0} ms (of which waiting to present {Pc(presentWait, 0.5f):0.0}), render thread {Pc(render, 0.5f):0.0} ms" +
                          (FrameTimingManager.IsFeatureEnabled() ? "" : " (frame timing stats off in this build)"));
            sb.AppendLine("drawn a frame: " + draws.Report());
            sb.AppendLine($"garbage collections: {GC.CollectionCount(0) - gcStart} | memory: allocated {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576} MB, reserved {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576} MB, mono {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576} MB | {ProcessMemory()}");
            if (slow.Count > 0)
            {
                sb.AppendLine("worst frames (time into the walk, metres walked, a garbage collection that frame):");
                foreach (var w in slow.OrderByDescending(x => x.ms).Take(15))
                    sb.AppendLine($"  {w.ms:0.0} ms at {w.at:0.0} s, {w.metres:0} m{(w.gc ? ", gc" : "")}");
            }
        }
        else sb.AppendLine("no frames measured: " + (player == null ? "no player" : $"route of {line.Count} point(s)"));

        GameSettings.QualityTier = savedTier;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_out)));
        File.WriteAllText(_out, sb.ToString());
        Application.Quit();
    }

    /// <summary>The process's resident memory now and at its peak (Linux).</summary>
    static string ProcessMemory()
    {
        try
        {
            var lines = File.ReadAllLines("/proc/self/status");
            string Get(string key) => lines.FirstOrDefault(l => l.StartsWith(key))?.Split(':')[1].Trim() ?? "?";
            return $"process resident {Get("VmRSS")}, peak {Get("VmHWM")}";
        }
        catch (Exception) { return "process memory unavailable"; }
    }
}
