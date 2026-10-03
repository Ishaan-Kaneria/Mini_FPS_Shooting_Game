using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Performance probe for a development build. Does nothing unless the player is started with
/// <c>-fpskitperf &lt;output file&gt;</c>: it then visits five viewpoints in the open arena, records frame time (CPU and GPU where
/// the platform reports it), triangles, shadow casters, lights, enemies and memory, writes a report and quits.
/// Used by FPSKitPerfBuild; the editor-side twin is FPSKitPerfProbe. Holds no static state.
/// </summary>
public class PerfProbe : MonoBehaviour
{
    [Tooltip("Where the report is written.")]
    public string outFile;

    // The viewpoints of the stretched Abandoned Fairground: where you start, the Midway, the Ferris wheel, the old-growth wood, the Big Top.
    static readonly (string name, Vector2 xz, float yaw)[] Views =
    {
        ("spawn", new Vector2(0f, 162f), 180f), ("midway", new Vector2(0f, 70f), 180f), ("ferris", new Vector2(70f, -28f), 90f),
        ("oldgrowth", new Vector2(110f, -120f), 200f), ("bigtop", new Vector2(-20f, -108f), 180f),
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-fpskitperf");
        if (i < 0 || i + 1 >= args.Length) return;
        var go = new GameObject("PerfProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<PerfProbe>().outFile = args[i + 1];
    }

    IEnumerator Start()
    {
        // The build starts at the main menu; the probe goes straight to the arena like the one-scene build used to start there.
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "AbandonedFairground")
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("AbandonedFairground");
            yield return new WaitForSecondsRealtime(1f);
        }
        QualitySettings.vSyncCount = 0;                       // uncapped, so the numbers are the cost and not the refresh rate
        Application.targetFrameRate = -1;
        var sb = new StringBuilder();
        sb.AppendLine($"api {SystemInfo.graphicsDeviceType} | gpu {SystemInfo.graphicsDeviceName} | {Screen.width}x{Screen.height} | quality {QualitySettings.names[QualitySettings.GetQualityLevel()]} | dev build {Debug.isDebugBuild}");
        sb.AppendLine($"cpu {SystemInfo.processorModel} x{SystemInfo.processorCount} | ram {SystemInfo.systemMemorySize} MB | gpu mem {SystemInfo.graphicsMemorySize} MB");

        var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        var casters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
        var setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        var texMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Texture Memory");

        yield return new WaitForSecondsRealtime(8f);          // let the level start

        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fpskitexperiments") >= 0)
        {
            yield return RunExperiments(sb, tris, casters, setpass);
            Finish(sb);
            yield break;
        }

        var all = new List<float>();
        foreach (var v in Views)
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                var cc = player.GetComponent<CharacterController>();
                var terrain = Terrain.activeTerrain;
                float y = terrain != null ? terrain.SampleHeight(new Vector3(v.xz.x, 0f, v.xz.y)) + terrain.transform.position.y + 1.2f : 3f;
                if (cc != null) cc.enabled = false;
                player.transform.SetPositionAndRotation(new Vector3(v.xz.x, y, v.xz.y), Quaternion.Euler(0f, v.yaw, 0f));
                if (cc != null) cc.enabled = true;
            }
            yield return new WaitForSecondsRealtime(2f);
            var dt = new List<float>(); var cpu = new List<float>(); var gpu = new List<float>();
            float end = Time.realtimeSinceStartup + 8f;
            var timings = new FrameTiming[1];
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                dt.Add(Time.unscaledDeltaTime * 1000f);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                {
                    if (timings[0].cpuFrameTime > 0) cpu.Add((float)timings[0].cpuFrameTime);
                    if (timings[0].gpuFrameTime > 0) gpu.Add((float)timings[0].gpuFrameTime);
                }
            }
            all.AddRange(dt);
            var sorted = dt.OrderBy(x => x).ToArray();
            float avg = dt.Average();
            var lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            sb.AppendLine($"[{v.name}] frames {dt.Count}: avg {avg:0.0} ms ({1000f / avg:0} fps), p95 {sorted[(int)(0.95f * (sorted.Length - 1))]:0.0} ms, worst {sorted[sorted.Length - 1]:0.0} ms | " +
                          $"cpu {(cpu.Count > 0 ? cpu.Average() : 0):0.0} ms, gpu {(gpu.Count > 0 ? gpu.Average() : 0):0.0} ms | tris {tris.LastValue / 1000}k, shadow casters {casters.LastValue}, setpass {setpass.LastValue} | " +
                          $"lights {lights.Length} ({lights.Count(l => l.shadows != LightShadows.None)} shadowed), enemies {FindObjectsByType<EnemyAI>(FindObjectsInactive.Exclude).Length}");
        }
        float mean = all.Average();
        sb.AppendLine($"OVERALL avg {mean:0.0} ms ({1000f / mean:0} fps) over {all.Count} frames");
        sb.AppendLine($"memory: texture {texMem.LastValue / 1048576} MB, total allocated {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576} MB, reserved {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576} MB, mono {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576} MB");
        Finish(sb);
    }

    void Finish(StringBuilder sb)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile)));
        File.WriteAllText(outFile, sb.ToString());
        Application.Quit();
    }

    void Teleport((string name, Vector2 xz, float yaw) v)
    {
        var player = GameObject.FindWithTag("Player");
        if (player == null) return;
        var cc = player.GetComponent<CharacterController>();
        var terrain = Terrain.activeTerrain;
        float y = terrain != null ? terrain.SampleHeight(new Vector3(v.xz.x, 0f, v.xz.y)) + terrain.transform.position.y + 1.2f : 3f;
        if (cc != null) cc.enabled = false;
        player.transform.SetPositionAndRotation(new Vector3(v.xz.x, y, v.xz.y), Quaternion.Euler(0f, v.yaw, 0f));
        if (cc != null) cc.enabled = true;
    }

    /// <summary>
    /// One change at a time, measured in three places and put back. Every row reports frame time, the main thread's CPU
    /// time, the GPU time (Vulkan reports it; OpenGL says 0) and the graphics-thread waits. A frame time well above both
    /// the CPU and the waits is the GPU. The first rows switch quality tier (Medium, High) and the rest are single changes
    /// to High, with a screenshot of each upscaler so their sharpness can be compared by eye. Baseline is measured first
    /// and last: the gap between the two is the noise, and a laptop warming up shows as a gap that grows through the run.
    /// </summary>
    IEnumerator RunExperiments(StringBuilder sb, ProfilerRecorder tris, ProfilerRecorder casters, ProfilerRecorder setpass)
    {
        UniversalRenderPipelineAsset Urp() => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        var wait1 = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Gfx.WaitForPresentOnGfxThread");
        var wait2 = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Gfx.WaitForGfxCommandsFromMainThread");
        var cam = Camera.main;
        int startLevel = QualitySettings.GetQualityLevel();
        int medium = Array.FindIndex(QualitySettings.names, n => n == "Medium"), high = Array.FindIndex(QualitySettings.names, n => n == "High");
        string dir = Path.GetDirectoryName(Path.GetFullPath(outFile));

        var u0 = Urp();
        sb.AppendLine($"URP asset '{u0.name}' at start: shadow distance {u0.shadowDistance}, cascades {u0.shadowCascadeCount}, MSAA {u0.msaaSampleCount}, render scale {u0.renderScale}, upscaling {u0.upscalingFilter}");

        // Saved per asset the first time a row touches it, restored when the row ends.
        var saved = new Dictionary<UniversalRenderPipelineAsset, (float scale, UpscalingFilterSelection filter, int msaa, float dist)>();
        void Save(UniversalRenderPipelineAsset u) { if (!saved.ContainsKey(u)) saved[u] = (u.renderScale, u.upscalingFilter, u.msaaSampleCount, u.shadowDistance); }
        void Restore() { foreach (var kv in saved) { kv.Key.renderScale = kv.Value.scale; kv.Key.upscalingFilter = kv.Value.filter; kv.Key.msaaSampleCount = kv.Value.msaa; kv.Key.shadowDistance = kv.Value.dist; } }

        IEnumerable<ScriptableRendererFeature> Ssao()
        {
            var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f != null && f.GetValue(Urp()) is ScriptableRendererData[] datas)
                foreach (var d in datas) if (d != null) foreach (var rf in d.rendererFeatures) if (rf != null && rf.name.IndexOf("Occlusion", StringComparison.OrdinalIgnoreCase) >= 0) yield return rf;
        }

        // name, apply, revert, screenshot
        var exps = new List<(string name, Action apply, Action revert, bool shot)>
        {
            ("baseline (start)", () => { }, () => { }, true),
            ("tier Medium", () => QualitySettings.SetQualityLevel(medium, true), () => QualitySettings.SetQualityLevel(startLevel, true), false),
            ("tier High", () => QualitySettings.SetQualityLevel(high, true), () => QualitySettings.SetQualityLevel(startLevel, true), false),
            ("High: render scale 1.0", () => { var u = Urp(); Save(u); u.renderScale = 1f; }, Restore, true),
            ("High: 0.85 bilinear", () => { var u = Urp(); Save(u); u.renderScale = 0.85f; u.upscalingFilter = UpscalingFilterSelection.Linear; }, Restore, true),
            ("High: 0.85 FSR 1", () => { var u = Urp(); Save(u); u.renderScale = 0.85f; u.upscalingFilter = UpscalingFilterSelection.FSR; }, Restore, true),
            ("High: 0.85 STP (MSAA off)", () => { var u = Urp(); Save(u); u.renderScale = 0.85f; u.upscalingFilter = UpscalingFilterSelection.STP; u.msaaSampleCount = 1; }, Restore, true),
            ("High: 0.7 FSR 1", () => { var u = Urp(); Save(u); u.renderScale = 0.7f; u.upscalingFilter = UpscalingFilterSelection.FSR; }, Restore, false),
            ("High: MSAA off", () => { var u = Urp(); Save(u); u.msaaSampleCount = 1; }, Restore, false),
            ("High: SSAO off", () => { foreach (var f in Ssao()) f.SetActive(false); }, () => { foreach (var f in Ssao()) f.SetActive(true); }, false),
            ("High: shadow distance 70", () => { var u = Urp(); Save(u); u.shadowDistance = 70f; }, Restore, false),
            ("High: shadow distance 30", () => { var u = Urp(); Save(u); u.shadowDistance = 30f; }, Restore, false),
            ("baseline (end)", () => { }, () => { }, false),
        };
        // The player starts on whichever tier the machine picked; measure the rows that start with "High" on High.
        sb.AppendLine($"start quality level {QualitySettings.names[startLevel]}");

        foreach (var view in new[] { Views[0], Views[2], Views[3] })
        {
            sb.AppendLine($"== view '{view.name}'");
            Teleport(view);
            yield return new WaitForSecondsRealtime(2f);
            float baseMs = 0f;
            foreach (var e in exps)
            {
                if (e.name.StartsWith("High:")) QualitySettings.SetQualityLevel(high, true);
                else if (e.name == "baseline (start)" || e.name == "baseline (end)") QualitySettings.SetQualityLevel(high, true);
                e.apply();
                yield return new WaitForSecondsRealtime(3f);
                var dt = new List<float>(); var cpu = new List<float>(); var gpu = new List<float>(); var waits = new List<double>();
                var timings = new FrameTiming[1];
                float end = Time.realtimeSinceStartup + 8f; bool shot = false, shotOk = e.shot && view.name == "oldgrowth";
                while (Time.realtimeSinceStartup < end)
                {
                    yield return null;
                    dt.Add(Time.unscaledDeltaTime * 1000f);
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                    {
                        if (timings[0].cpuMainThreadFrameTime > 0) cpu.Add((float)timings[0].cpuMainThreadFrameTime);
                        if (timings[0].gpuFrameTime > 0) gpu.Add((float)timings[0].gpuFrameTime);
                    }
                    waits.Add((wait1.Valid ? wait1.LastValue : 0) / 1e6 + (wait2.Valid ? wait2.LastValue : 0) / 1e6);
                    if (shotOk && !shot && end - Time.realtimeSinceStartup < 3f)
                    {
                        shot = true;
                        ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"shot_{Array.IndexOf(exps.ToArray(), e):00}_{e.name.Replace(' ', '_').Replace(':', '_').Replace('(', '_').Replace(')', '_')}.png"));
                    }
                }
                float avg = dt.Average();
                if (e.name == "baseline (start)") baseMs = avg;
                var sorted = dt.OrderBy(x => x).ToArray();
                sb.AppendLine($"   {e.name,-28} avg {avg,5:0.0} ms ({1000f / avg,3:0} fps)  p95 {sorted[(int)(0.95f * (sorted.Length - 1))],5:0.0}  delta {avg - baseMs,+6:0.0} | main cpu {(cpu.Count > 0 ? cpu.Average() : 0):0.0}, gpu {(gpu.Count > 0 ? gpu.Average() : 0):0.0}, gfx waits {(waits.Count > 0 ? waits.Average() : 0):0.0} | tris {tris.LastValue / 1000}k, casters {casters.LastValue}, setpass {setpass.LastValue}");
                e.revert();
                yield return null;
            }
        }
        QualitySettings.SetQualityLevel(startLevel, true);
        wait1.Dispose(); wait2.Dispose();
    }
}
