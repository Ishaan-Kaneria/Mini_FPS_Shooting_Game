using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// How smooth a real build plays (smooth-animation brief, 2026-10-18: "measure in a real build, not only the editor").
/// Does nothing unless the player is started with <c>-fpskitsmooth &lt;report file&gt;</c>: it then opens Desert Outpost,
/// waits for the level, and drives the player through the touch controls' inputs (the path a phone takes) -- standing,
/// walking, jogging, sprinting, a turning jog, firing on the move -- twice: held to 60 frames a second, and unlimited.
/// Per run: frames, average and low frame rates, frame-time percentiles, how many frames took twice the median (a
/// hitch), the spread of frame times (pacing), and the CPU and GPU times where the platform reports them. Then quits.
/// Built by FPSKitSmoothBuild (FPSKitBatch.BuildSmoothPlayer). Holds no static state.
/// </summary>
public class SmoothProbe : MonoBehaviour
{
    string _out;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-fpskitsmooth");
        if (i < 0 || i + 1 >= args.Length) return;
        var go = new GameObject("SmoothProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<SmoothProbe>()._out = args[i + 1];
    }

    struct Part { public string name; public float seconds; public Vector2 move; public bool sprint, fire, turn; }

    readonly Part[] Script =
    {
        new Part { name = "standing", seconds = 3f },
        new Part { name = "walk", seconds = 4f, move = new Vector2(0f, 0.5f) },
        new Part { name = "jog", seconds = 4f, move = new Vector2(0f, 1f) },
        new Part { name = "sprint", seconds = 4f, move = new Vector2(0f, 1f), sprint = true },
        new Part { name = "turning jog", seconds = 4f, move = new Vector2(0f, 1f), turn = true },
        new Part { name = "firing on the move", seconds = 3f, move = new Vector2(0.4f, 0.6f), fire = true },
    };

    IEnumerator Start()
    {
        var sb = new StringBuilder();
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "DesertOutpost")
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("DesertOutpost");
            yield return new WaitForSecondsRealtime(1f);
        }
        sb.AppendLine($"api {SystemInfo.graphicsDeviceType} | gpu {SystemInfo.graphicsDeviceName} | {Screen.width}x{Screen.height} @ {Screen.currentResolution.refreshRateRatio.value:0} Hz | quality {QualitySettings.names[QualitySettings.GetQualityLevel()]} | dev build {Debug.isDebugBuild}");
        sb.AppendLine($"cpu {SystemInfo.processorModel} x{SystemInfo.processorCount} | ram {SystemInfo.systemMemorySize} MB");

        // Into the level: past the briefing, and a moment to settle.
        float wait = Time.realtimeSinceStartup + 40f;
        LevelManager level = null;
        while (Time.realtimeSinceStartup < wait && (level == null || level.IsBriefing || !level.IsRunning))
        {
            level = FindAnyObjectByType<LevelManager>();
            yield return null;
        }
        yield return new WaitForSecondsRealtime(3f);

        foreach (int cap in new[] { 60, 0 })
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = cap > 0 ? cap : -1;
            yield return Run(sb, cap > 0 ? $"held to {cap} fps" : "unlimited");
        }

        sb.AppendLine($"memory: total allocated {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576} MB, reserved {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576} MB, mono {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576} MB");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_out)));
        File.WriteAllText(_out, sb.ToString());
        MobileInput.Reset();
        Application.Quit();
    }

    IEnumerator Run(StringBuilder sb, string label)
    {
        var player = GameObject.FindWithTag("Player");
        var motor = player != null ? player.GetComponent<PlayerMotor>() : null;
        var health = player != null ? player.GetComponent<Health>() : null;
        MobileInput.Reset();
        MobileInput.Active = true;
        yield return new WaitForSecondsRealtime(1f);

        var dt = new List<float>(); var cpu = new List<float>(); var gpu = new List<float>();
        var timings = new FrameTiming[1];
        foreach (var part in Script)
        {
            MobileInput.Move = part.move;
            MobileInput.SetSprintButton(part.sprint);
            float end = Time.realtimeSinceStartup + part.seconds;
            float yaw = player != null ? player.transform.eulerAngles.y : 0f;
            while (Time.realtimeSinceStartup < end)
            {
                if (part.fire) MobileInput.SetFireButton(Mathf.Repeat(Time.realtimeSinceStartup, 0.8f) < 0.4f);
                if (part.turn && motor != null)
                {
                    yaw += 90f * Time.unscaledDeltaTime;
                    motor.TurnTowards(player.transform.position + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 20f + Vector3.up * 1.6f, 1f);
                }
                if (health != null) health.Heal(1000f);
                yield return null;
                dt.Add(Time.unscaledDeltaTime * 1000f);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                {
                    if (timings[0].cpuFrameTime > 0) cpu.Add((float)timings[0].cpuFrameTime);
                    if (timings[0].gpuFrameTime > 0) gpu.Add((float)timings[0].gpuFrameTime);
                }
            }
            MobileInput.SetFireButton(false);
        }
        MobileInput.Reset();
        MobileInput.Active = true;

        var s = dt.OrderBy(x => x).ToArray();
        float P(float p) => s[Mathf.Clamp((int)(p * (s.Length - 1)), 0, s.Length - 1)];
        float avg = dt.Average(), median = P(0.5f);
        float Low(float share) { int n = Mathf.Max(1, (int)(s.Length * share)); return 1000f / s.Skip(s.Length - n).Average(); }
        float sd = Mathf.Sqrt(dt.Sum(x => (x - avg) * (x - avg)) / dt.Count);
        int hitches = dt.Count(x => x > 2f * median);
        sb.AppendLine($"[{label}] {dt.Count} frames over {Script.Sum(p => p.seconds):0} s: avg {1000f / avg:0} fps, 1% low {Low(0.01f):0} fps, 0.1% low {Low(0.001f):0} fps | " +
                      $"frame time p50 {median:0.0} / p95 {P(0.95f):0.0} / p99 {P(0.99f):0.0} / worst {s[s.Length - 1]:0.0} ms, spread (sd) {sd:0.00} ms, " +
                      $"{hitches} hitch(es) over twice the median | cpu {(cpu.Count > 0 ? cpu.Average() : 0f):0.0} ms, gpu {(gpu.Count > 0 ? gpu.Average() : 0f):0.0} ms");
    }
}
