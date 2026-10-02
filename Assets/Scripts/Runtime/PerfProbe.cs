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

    /// <summary>One change at a time, each measured in two places and put back: the delta against the baseline is that change's cost.</summary>
    IEnumerator RunExperiments(StringBuilder sb, ProfilerRecorder tris, ProfilerRecorder casters, ProfilerRecorder setpass)
    {
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        var lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        var shadowed = lights.Where(l => l.shadows != LightShadows.None).ToDictionary(l => l, l => l.shadows);
        var others = lights.Where(l => l.type != LightType.Directional && l.enabled).ToArray();
        var terrains = FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
        var volumes = FindObjectsByType<Volume>(FindObjectsInactive.Exclude);
        var cam = Camera.main;
        float sd = urp.shadowDistance, far = cam != null ? cam.farClipPlane : 1000f, lod = QualitySettings.lodBias;
        int cc = urp.shadowCascadeCount, msaa = urp.msaaSampleCount;
        sb.AppendLine($"URP asset '{urp.name}': shadow distance {sd}, cascades {cc}, MSAA {msaa}, main shadow res {urp.mainLightShadowmapResolution}");

        var exps = new List<(string name, Action apply, Action revert)>
        {
            ("baseline", () => { }, () => { }),
            ("no realtime shadows", () => { foreach (var l in shadowed.Keys) l.shadows = LightShadows.None; }, () => { foreach (var kv in shadowed) kv.Key.shadows = kv.Value; }),
            ("spot shadows off (sun only)", () => { foreach (var l in shadowed.Keys) if (l.type != LightType.Directional) l.shadows = LightShadows.None; }, () => { foreach (var kv in shadowed) kv.Key.shadows = kv.Value; }),
            ("shadow distance 95 -> 70", () => urp.shadowDistance = 70f, () => urp.shadowDistance = sd),
            ("shadow distance 95 -> 50", () => urp.shadowDistance = 50f, () => urp.shadowDistance = sd),
            ("cascades 4 -> 2", () => urp.shadowCascadeCount = 2, () => urp.shadowCascadeCount = cc),
            ("distance 70 + 2 cascades", () => { urp.shadowDistance = 70f; urp.shadowCascadeCount = 2; }, () => { urp.shadowDistance = sd; urp.shadowCascadeCount = cc; }),
            ("MSAA 4x -> 2x", () => urp.msaaSampleCount = 2, () => urp.msaaSampleCount = msaa),
            ("MSAA 4x -> off", () => urp.msaaSampleCount = 1, () => urp.msaaSampleCount = msaa),
            ("all point/spot lights off", () => { foreach (var l in others) l.enabled = false; }, () => { foreach (var l in others) l.enabled = true; }),
            ("terrain trees + detail off", () => { foreach (var t in terrains) t.drawTreesAndFoliage = false; }, () => { foreach (var t in terrains) t.drawTreesAndFoliage = true; }),
            ("post-processing off", () => { foreach (var v in volumes) v.enabled = false; }, () => { foreach (var v in volumes) v.enabled = true; }),
            ("far clip 1000 -> 220", () => { if (cam != null) cam.farClipPlane = 220f; }, () => { if (cam != null) cam.farClipPlane = far; }),
            ("LOD bias 2 -> 1", () => QualitySettings.lodBias = 1f, () => QualitySettings.lodBias = lod),
        };
        foreach (var view in new[] { Views[0], Views[3] })
        {
            Teleport(view);
            yield return new WaitForSecondsRealtime(2f);
            float baseMs = 0f;
            sb.AppendLine($"== view '{view.name}'");
            foreach (var e in exps)
            {
                e.apply();
                yield return new WaitForSecondsRealtime(1.5f);
                var dt = new List<float>();
                float end = Time.realtimeSinceStartup + 4f;
                while (Time.realtimeSinceStartup < end) { yield return null; dt.Add(Time.unscaledDeltaTime * 1000f); }
                float avg = dt.Average();
                if (e.name == "baseline") baseMs = avg;
                var sorted = dt.OrderBy(x => x).ToArray();
                sb.AppendLine($"   {e.name,-30} avg {avg,5:0.0} ms ({1000f / avg,3:0} fps)  p95 {sorted[(int)(0.95f * (sorted.Length - 1))],5:0.0}  delta {avg - baseMs,+6:0.0} ms | tris {tris.LastValue / 1000}k, shadow casters {casters.LastValue}, setpass {setpass.LastValue}");
                e.revert();
            }
        }
    }
}
