using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
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
    string _out, _tier, _shots;
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
        int sh = Array.IndexOf(args, "-fpskitshots");
        if (sh >= 0 && sh + 1 < args.Length) probe._shots = args[sh + 1];
        int s = Array.IndexOf(args, "-fpskitseconds");
        if (s >= 0 && s + 1 < args.Length && float.TryParse(args[s + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sec)) probe._seconds = sec;
    }

    struct Slow { public float ms, at, metres; public bool gc; public string why; }

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

        // -fpskitpostboth: post-processing on the world's camera as well as the stack's last (as before 2026-10-30), for a
        // before/after of QualityTiers.ApplyPost in one build.
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fpskitpostboth") >= 0 && Camera.main != null)
        {
            UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(Camera.main).renderPostProcessing = true;
            sb.AppendLine("post-processing on the world's camera as well (-fpskitpostboth)");
        }
        // One build, several settings, so they compare without a rebuild each: -fpskitcap N, -fpskitdr off|<floor>, -fpskitgpuocc.
        var cli = Environment.GetCommandLineArgs();
        string Arg(string name) { int at = Array.IndexOf(cli, name); return at >= 0 && at + 1 < cli.Length ? cli[at + 1] : null; }
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = int.TryParse(Arg("-fpskitcap"), out int cap) ? cap : -1;
        var dynamicRes = Camera.main != null ? Camera.main.GetComponent<DynamicResolution>() : null;
        string drArg = Arg("-fpskitdr");
        if (dynamicRes != null && drArg == "off") dynamicRes.enabled = false;
        else if (dynamicRes != null && float.TryParse(drArg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float drFloor)) dynamicRes.floor = drFloor;
        if (float.TryParse(Arg("-fpskitscale"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float renderScale)
            && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset scaled)
        {
            scaled.renderScale = renderScale;
            sb.AppendLine($"render scale fixed at {renderScale:0.00} (-fpskitscale)");
        }
        // -fpskitoff A,B,type:C: switched off for the walk -- every object of that name, or carrying a component of that type
        // (what a part of the town costs: the walk without it).
        string offArg = Arg("-fpskitoff");
        if (offArg != null)
        {
            int n = 0;
            foreach (var want in offArg.Split(','))
            {
                if (want.StartsWith("type:"))
                {
                    string type = want.Substring(5);
                    foreach (var b in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                        if (b != null && b.GetType().Name == type && b.gameObject != player) { b.gameObject.SetActive(false); n++; }
                }
                else
                    foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                        if (t != null && t.name == want) { t.gameObject.SetActive(false); n++; }
            }
            sb.AppendLine($"switched off for the walk: {offArg} ({n} objects)");
        }
        // -fpskitgrd off: the GPU Resident Drawer off (its jobs re-light every moving renderer each frame), for an A/B.
        if (Arg("-fpskitgrd") == "off" && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset grdAsset)
        {
            grdAsset.gpuResidentDrawerMode = UnityEngine.Rendering.GPUResidentDrawerMode.Disabled;
            sb.AppendLine("GPU Resident Drawer off (-fpskitgrd off)");
        }
        if (Array.IndexOf(cli, "-fpskitgpuocc") >= 0 && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
            urp.gpuResidentDrawerEnableOcclusionCullingInCameras = true;
        // -fpskitlegacy: the lighting as it was before 2026-11-07 (no shadow budget, the arms' own shadow pass, four cascades on
        // High), for a before/after in one build.
        bool legacy = Array.IndexOf(cli, "-fpskitlegacy") >= 0;
        SetLighting(!legacy);
        sb.AppendLine(legacy ? "lighting: as before 2026-11-07 (-fpskitlegacy)" : "lighting: shadow budget, no arms shadow pass, High on three cascades");
        var budget = FindAnyObjectByType<ShadowBudget>();
        if (budget != null) sb.AppendLine(budget.Summary);
        sb.AppendLine($"settings: cap {(cap > 0 ? cap.ToString() : "none")} | dynamic resolution {(dynamicRes != null && dynamicRes.enabled ? $"on, floor {dynamicRes.floor:0.00}" : "off")} | gpu occlusion {(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset now && now.gpuResidentDrawerEnableOcclusionCullingInCameras ? "on" : "off")}");
        float scaleSum = 0f, scaleMin = 1f; int scaleN = 0;
        MobileInput.Reset();
        MobileInput.Active = true;
        var dt = new List<float>(); var cpu = new List<float>(); var gpu = new List<float>();
        var main = new List<float>(); var render = new List<float>(); var presentWait = new List<float>();
        using var draws = new DrawCounters();
        using var markers = new SpikeMarkers();
        var slow = new List<Slow>();
        var timings = new FrameTiming[1];
        int gcStart = GC.CollectionCount(0), stalls = 0, seg = 1;
        float start = Time.realtimeSinceStartup, walked = 0f, stuckSince = start;
        Vector3 lastPos = player != null ? player.transform.position : Vector3.zero, checkPos = lastPos;
        int lastGc = gcStart;
        // -fpskitshots <dir>: the screen as played at a few fixed moments (the same ones every run), to compare by eye.
        float[] shotAt = { 8f, 35f, 60f, 95f };
        int nextShot = 0; bool shotFrame = false;
        if (_shots != null) Directory.CreateDirectory(_shots);
        // The renderers that move (their transform changed in a frame), counted at 30 s and 80 s: every one of them has its
        // light probes worked out again each frame by the GPU Resident Drawer.
        float[] censusAt = { 30f, 80f }; int nextCensus = 0; MeshRenderer[] censusSet = null; bool censusFrame = false;
        var census = new List<string>();
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
            if (dynamicRes != null && dynamicRes.enabled) { scaleSum += dynamicRes.Scale; scaleMin = Mathf.Min(scaleMin, dynamicRes.Scale); scaleN++; }
            // The frame that wrote a screenshot is not a frame of play: left out of the timing.
            if (shotFrame) { shotFrame = false; continue; }
            if (censusFrame) { censusFrame = false; continue; }
            if (censusSet != null)
            {
                var moved = censusSet.Where(r => r != null && r.transform.hasChanged).ToList();
                var groups = moved.GroupBy(r => { var t = r.transform; while (t.parent != null && t.parent.parent != null) t = t.parent; return t.name; })
                                  .OrderByDescending(g => g.Count()).Take(12).Select(g => $"{g.Key} {g.Count()}");
                census.Add($"at {Time.realtimeSinceStartup - start:0} s, {moved.Count} of {censusSet.Length} mesh renderers moved in a frame: {string.Join(", ", groups)}");
                censusSet = null; censusFrame = true; continue;
            }
            if (nextCensus < censusAt.Length && Time.realtimeSinceStartup - start >= censusAt[nextCensus])
            {
                nextCensus++;
                censusSet = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
                foreach (var r in censusSet) r.transform.hasChanged = false;
                censusFrame = true; continue;
            }
            if (_shots != null && nextShot < shotAt.Length && Time.realtimeSinceStartup - start >= shotAt[nextShot])
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(_shots, $"walk_{shotAt[nextShot++]:00}s.png"));
                shotFrame = true;
            }
            walked += Vector3.Distance(lastPos, player.transform.position);
            lastPos = player.transform.position;
            int gcNow = GC.CollectionCount(0);
            dt.Add(ms);
            string switched = RoomContents.Toggled + ShadowBudget.Toggled > 0 ? $"switched: {RoomContents.Toggled} room renderers, {ShadowBudget.Toggled} shadow casters" : null;
            RoomContents.Toggled = 0; ShadowBudget.Toggled = 0;
            if (ms > 25f && !markers.JustRefreshed) slow.Add(new Slow { ms = ms, at = Time.realtimeSinceStartup - start, metres = walked, gc = gcNow != lastGc,
                why = string.Join(" | ", new[] { ms > 40f ? markers.Top() : null, switched }.Where(x => !string.IsNullOrEmpty(x))) });
            markers.Refresh(Time.realtimeSinceStartup - start);
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
            if (scaleN > 0) sb.AppendLine($"render scale averaged {scaleSum / scaleN:0.00}, lowest {scaleMin:0.00}");
            sb.AppendLine($"frames {dt.Count} over {dt.Sum() / 1000f:0.0} s, walked {walked:0} m ({stalls} stall(s) stepped past)");
            sb.AppendLine($"avg {1000f / dt.Average():0.0} fps | 1% low {Low(0.01f):0.0} fps | 0.1% low {Low(0.001f):0.0} fps | worst frame {s[s.Length - 1]:0.0} ms | " +
                          $"frames over 33 ms: {dt.Count(x => x > 33.3f)} | over 25 ms: {dt.Count(x => x > 25f)}");
            sb.AppendLine($"frame time p50 {P(0.5f):0.0} / p95 {P(0.95f):0.0} / p99 {P(0.99f):0.0} ms | cpu p50 {Pc(cpu, 0.5f):0.0} / p99 {Pc(cpu, 0.99f):0.0} ms | gpu p50 {Pc(gpu, 0.5f):0.0} / p99 {Pc(gpu, 0.99f):0.0} ms");
            // The main thread waiting on present is the GPU (or vsync) holding it up; its own work is main minus the wait.
            sb.AppendLine($"threads p50: main {Pc(main, 0.5f):0.0} ms (and waiting to present {Pc(presentWait, 0.5f):0.0} more), render thread {Pc(render, 0.5f):0.0} ms" +
                          (FrameTimingManager.IsFeatureEnabled() ? "" : " (frame timing stats off in this build)"));
            sb.AppendLine("drawn a frame: " + draws.Report());
            foreach (var c in census) sb.AppendLine("moving: " + c);
            sb.AppendLine($"garbage collections: {GC.CollectionCount(0) - gcStart} | memory: allocated {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576} MB, reserved {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576} MB, mono {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576} MB | {ProcessMemory()}");
            if (slow.Count > 0)
            {
                sb.AppendLine("worst frames (time into the walk, metres walked, a garbage collection that frame" + (markers.Count > 0 ? "; in a development build, the profiler markers that took the most of it" : "") + "):");
                foreach (var w in slow.OrderByDescending(x => x.ms).Take(15))
                    sb.AppendLine($"  {w.ms:0.0} ms at {w.at:0.0} s, {w.metres:0} m{(w.gc ? ", gc" : "")}{(string.IsNullOrEmpty(w.why) ? "" : " | " + w.why)}");
                if (markers.Count > 0)
                {
                    // Every frame over 40 ms, by the marker that took the most of it: what the freezes are made of.
                    var causes = slow.Where(x => x.ms > 40f && x.why != null && x.why.StartsWith("main: ")).GroupBy(x => x.why.Substring(6).Split(new[] { " (" }, StringSplitOptions.None)[0]).OrderByDescending(g => g.Count());
                    sb.AppendLine($"frames over 40 ms by the main thread's biggest marker ({markers.Count} markers recorded):");
                    int sw = slow.Count(x => x.ms > 40f && x.why != null && x.why.Contains("switched:"));
                    sb.AppendLine($"  of {slow.Count(x => x.ms > 40f)} frames over 40 ms, {sw} switched renderers on or off that frame");
                    foreach (var g in causes.Take(12)) sb.AppendLine($"  {g.Count()} x {g.Key} (worst frame {g.Max(x => x.ms):0} ms)");
                }
            }
        }
        else sb.AppendLine("no frames measured: " + (player == null ? "no player" : $"route of {line.Count} point(s)"));

        // -fpskitlightviews <dir>: after the walk, fixed views (gun and arms in sun, in shade and indoors; a long street) each
        // taken with the lighting as before and as now (and on High, now without SSAO), to judge side by side.
        string views = Arg("-fpskitlightviews");
        if (views != null && player != null)
        {
            Directory.CreateDirectory(views);
            var viewNotes = new List<string>();
            yield return TakeViews(views, player, motor, health, viewNotes);
            foreach (var n in viewNotes) sb.AppendLine("views: " + n);
        }
        SetLighting(!legacy);

        GameSettings.QualityTier = savedTier;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_out)));
        File.WriteAllText(_out, sb.ToString());
        Application.Quit();
    }

    /// <summary>The lighting of 2026-11-07 on (the shadow budget, no arms shadow pass, three cascades on High) or as before.</summary>
    public static void SetLighting(bool now)
    {
        ShadowBudget.Active = now;
        ViewModelRig.OwnShadows = !now;
        if (QualityTiers.Current == GameSettings.Quality.High && GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
        {
            urp.shadowCascadeCount = now ? 3 : 4;
            if (now) urp.cascade3Split = new Vector2(0.1f, 0.3f);
            else urp.cascade4Split = new Vector3(0.05f, 0.15f, 0.4f);
        }
    }

    /// <summary>Screen-space ambient occlusion on or off, on every renderer of the tier (High has it, Medium does not).</summary>
    static bool SetSsao(bool on)
    {
        bool found = false;
        if (GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
            foreach (var d in urp.rendererDataList)
                if (d != null)
                    foreach (var f in d.rendererFeatures)
                        if (f != null && f.GetType().Name.Contains("AmbientOcclusion")) { f.SetActive(on); found = true; }
        return found;
    }

    /// <summary>The fixed views: places found along the walk's route (in sun, in a building's shade, inside a room, and a long
    /// street look), each shot as before and as now. The player stands still, kept alive.</summary>
    IEnumerator TakeViews(string dir, GameObject player, PlayerMotor motor, Health health, List<string> notes)
    {
        MobileInput.Reset();
        var sun = RenderSettings.sun;
        if (sun == null) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
        int mask = ~LayerMask.GetMask("Player", "FirstPerson", "PlayerBounds", "Ignore Raycast", "UI", "Enemy", "Civilian");
        var route = WalkRoute.Line(player.transform.position, WalkRoute.Desert);
        var places = new List<(string name, Vector3 at, Vector3 look)>();
        bool Shaded(Vector3 p) => sun != null && Physics.Raycast(p + Vector3.up * 1.2f, -sun.transform.forward, 200f, mask, QueryTriggerInteraction.Ignore);
        bool Roofed(Vector3 p) => Physics.Raycast(p + Vector3.up * 1.2f, Vector3.up, 30f, mask, QueryTriggerInteraction.Ignore);
        // Along the route a metre at a time: the first place in full sun, the first in a building's shade (open sky above).
        Vector3? sunAt = null, shadeAt = null; Vector3 sunLook = Vector3.forward, shadeLook = Vector3.forward;
        for (int i = 1; i < route.Count && (sunAt == null || shadeAt == null); i++)
        {
            Vector3 a = route[i - 1], b = route[i], dirAB = Vector3.ProjectOnPlane(b - a, Vector3.up).normalized;
            for (float m = 0f; m < Vector3.Distance(a, b); m += 1f)
            {
                var p = a + dirAB * m;
                if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var ground, 8f, mask, QueryTriggerInteraction.Ignore)) continue;
                p = ground.point;
                if (Roofed(p)) continue;
                if (sunAt == null && !Shaded(p) && Vector3.Distance(p, route[0]) > 10f) { sunAt = p; sunLook = dirAB; }
                if (shadeAt == null && Shaded(p)) { shadeAt = p; shadeLook = dirAB; }
            }
        }
        if (sunAt != null) places.Add(("sun", sunAt.Value, sunLook));
        if (shadeAt != null) places.Add(("shade", shadeAt.Value, shadeLook));
        // Indoors: the room nearest the start whose floor is found and that has a roof over it.
        var rooms = FindAnyObjectByType<RoomContents>();
        if (rooms != null)
        {
            var start = route.Count > 0 ? route[0] : player.transform.position;
            foreach (var (box, _, _) in rooms.Rooms().OrderBy(r => (r.room.center - start).sqrMagnitude).Take(40))
            {
                if (box.size.x < 3f || box.size.z < 3f) continue;
                var top = new Vector3(box.center.x, box.max.y - 0.3f, box.center.z);
                if (!Physics.Raycast(top, Vector3.down, out var floor, box.size.y + 1f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (!Roofed(floor.point) || floor.point.y < box.min.y - 0.5f) continue;
                var look = box.size.x > box.size.z ? Vector3.right : Vector3.forward;
                places.Add(("indoor", floor.point - look * Mathf.Min(box.size.x, box.size.z) * 0.3f, look));
                break;
            }
        }
        // A long street: from the route's middle, along it (distant buildings' shadows).
        if (route.Count > 4)
        {
            int k = route.Count / 2;
            var look = Vector3.ProjectOnPlane(route[k + 1] - route[k], Vector3.up).normalized;
            if (Physics.Raycast(route[k] + Vector3.up * 3f, Vector3.down, out var g, 8f, mask, QueryTriggerInteraction.Ignore)) places.Add(("street", g.point, look));
        }
        notes.Add($"{places.Count} places: " + string.Join(", ", places.Select(p => $"{p.name} {p.at.x:0},{p.at.z:0}")));
        bool high = QualityTiers.Current == GameSettings.Quality.High;
        var cc = player.GetComponent<CharacterController>();
        foreach (var (name, at, look) in places)
        {
            if (cc != null) cc.enabled = false;
            player.transform.position = at + Vector3.up * 0.05f;
            if (cc != null) cc.enabled = true;
            for (int f = 0; f < 90; f++)   // settle: the eye adapts, the room's contents come on, the body stands still
            {
                motor.TurnTowards(at + look * 20f + Vector3.up * 1.1f, 0.3f);
                if (health != null) health.Heal(1000f);
                yield return null;
            }
            // Each setting shot (the ones to judge by eye) and timed standing still: 90 frames, median frame and GPU time.
            var configs = new List<(string tag, bool shot, Action set)>
            {
                ("before", true, () => SetLighting(false)),
                ("after", true, () => SetLighting(true)),
                ("after+arms_shadow_pass", false, () => { SetLighting(true); ViewModelRig.OwnShadows = true; }),
                ("after-shadow_budget", false, () => { SetLighting(true); ShadowBudget.Active = false; }),
            };
            if (high)
            {
                configs.Add(("after+4_cascades", false, () => { SetLighting(false); ShadowBudget.Active = true; ViewModelRig.OwnShadows = false; }));
                configs.Add(("after_noSSAO", true, () => { SetLighting(true); SetSsao(false); }));
            }
            var timing = new FrameTiming[1];
            foreach (var (tag, shot, set) in configs)
            {
                set();
                for (int f = 0; f < 20; f++) { motor.TurnTowards(at + look * 20f + Vector3.up * 1.1f, 1f); if (health != null) health.Heal(1000f); yield return null; }
                if (shot)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"view_{name}_{QualityTiers.Current}_{tag}.png"));
                    yield return null; yield return null;
                }
                var ft = new List<float>(); var gt = new List<float>();
                for (int f = 0; f < 90; f++)
                {
                    motor.TurnTowards(at + look * 20f + Vector3.up * 1.1f, 1f);
                    if (health != null) health.Heal(1000f);
                    yield return null;
                    ft.Add(Time.unscaledDeltaTime * 1000f);
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].gpuFrameTime > 0) gt.Add((float)timing[0].gpuFrameTime);
                }
                float Med(List<float> v) { if (v.Count == 0) return 0f; v.Sort(); return v[v.Count / 2]; }
                notes.Add($"{name} {tag}: frame {Med(ft):0.0} ms, gpu {Med(gt):0.0} ms");
                if (high) SetSsao(true);
            }
            if (high) SetSsao(true);
        }
    }

    /// <summary>
    /// In a development build, the profiler markers of the main, render and worker threads (scripts' updates, garbage
    /// collection, shader and pipeline creation, loading and uploads, physics, animation, rendering), so a slow frame can be
    /// put down to what took it. A release build records none (markers exist only in development players).
    /// </summary>
    sealed class SpikeMarkers : IDisposable
    {
        static readonly string[] Wanted = { "[Invoke]", "GC.", "Shader", "Pipeline", "PSO", "Program", "Loading.", "Upload", "Instantiate",
            "Physics", "Animat", "NavMesh", "Cull", "Shadow", "Gfx.", "Awake", "Batch", "Mesh", "Texture", "Particle",
            "Director", "Update.", "Render", "Camera", "Canvas", "Present", "Scene", "Destroy", "Light", "Probe", "Job", "Instance", "Complete" };
        // Enclosing markers and the idle waits of other threads (a worker asleep fills its frame with them).
        static readonly string[] Ignored = { "PlayerLoop", "Main Thread", "Render Thread", "Update.ScriptRunBehaviourUpdate", "BehaviourUpdate",
            "Update.ScriptRunDelayedDynamicFrameRate", "CoroutinesDelayedCalls" };
        static readonly string[] Idle = { "Semaphore", "Idle", "Audio.Thread", "WaitForCompleted", "WaitForSignal", "Gfx.WaitFor", "WaitForLastPresentation", "WaitForPresent" };
        readonly List<(string name, Unity.Profiling.ProfilerRecorder rec)> _main = new List<(string, Unity.Profiling.ProfilerRecorder)>();
        readonly List<(string name, Unity.Profiling.ProfilerRecorder rec)> _jobs = new List<(string, Unity.Profiling.ProfilerRecorder)>();
        readonly HashSet<string> _have = new HashSet<string>();
        int _refreshes;
        public int Count => _main.Count + _jobs.Count;
        /// <summary>The frame a refresh ran in (its own cost): left out of the slow frames.</summary>
        public bool JustRefreshed { get; private set; }

        public SpikeMarkers() { Refresh(0f); }

        /// <summary>Markers appear as things first run: looked for at the start and again 20 and 60 s in.</summary>
        public void Refresh(float seconds)
        {
            JustRefreshed = false;
            if (!Debug.isDebugBuild) return;
            float due = _refreshes == 0 ? 0f : _refreshes == 1 ? 20f : _refreshes == 2 ? 60f : float.MaxValue;
            if (seconds < due) return;
            _refreshes++;
            JustRefreshed = true;
            var handles = new List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
            Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var h in handles)
            {
                var d = Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != Unity.Profiling.ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                string n = d.Name;
                if (_have.Contains(n) || Array.IndexOf(Ignored, n) >= 0 || !Wanted.Any(w => n.Contains(w))) continue;
                _have.Add(n);
                // On the main thread (this coroutine's): what held the frame up there.
                _main.Add((n, Unity.Profiling.ProfilerRecorder.StartNew(d.Category, n, 1, Unity.Profiling.ProfilerRecorderOptions.Default | Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread)));
                // Jobs on any thread (the main thread may be waiting for one): the busy ones, never the idle waits.
                if ((n.Contains("Job") || n.Contains("(Burst)") || n.Contains("Instance") || n.Contains("Probe")) && !Idle.Any(w => n.Contains(w)))
                    _jobs.Add((n, Unity.Profiling.ProfilerRecorder.StartNew(d.Category, n, 1)));
            }
        }

        static string List(List<(string name, Unity.Profiling.ProfilerRecorder rec)> recs, float floor)
            => string.Join(", ", recs.Where(r => r.rec.Valid).Select(r => (r.name, ms: r.rec.LastValue / 1e6f)).Where(r => r.ms >= floor && r.ms < 10000f)
                                     .OrderByDescending(r => r.ms).Take(6).Select(r => $"{r.name} ({r.ms:0} ms)"));

        /// <summary>The markers that took the most of the frame just ended: the main thread's, then the jobs'.</summary>
        public string Top()
        {
            if (Count == 0) return null;
            var scripts = _main.Where(r => r.name.Contains("[Invoke]")).ToList();
            return $"main: {List(_main, 2f)} | scripts: {List(scripts, 1f)} | jobs: {List(_jobs, 5f)}";
        }

        public void Dispose() { foreach (var r in _main) r.rec.Dispose(); foreach (var r in _jobs) r.rec.Dispose(); }
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
