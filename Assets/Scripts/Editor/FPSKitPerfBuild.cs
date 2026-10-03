#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// A development build of the main menu and the Abandoned Fairground (Linux x64, Vulkan only) for measuring outside the editor. It runs the PerfProbe
    /// component when started with <c>-fpskitperf &lt;file&gt;</c>; <c>Tools/run-perf-build.sh</c> starts it under Vulkan and OpenGL.
    /// Output goes to Build/Perf/&lt;tag&gt;/ (Build/ is not committed). Frame timing stats are turned on for the build and put back after.
    /// </summary>
    public static class FPSKitPerfBuild
    {
        const string MenuScene = "Assets/FPSKit_Generated/Scenes/Menu.unity";
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/AbandonedFairground.unity";

        [MenuItem("Tools/MiniFPS/Performance/Build Linux Dev Player (after)")]
        public static void BuildAfter() => Build("after");

        public static void BuildBefore() => Build("before");

        public static void Build(string tag)
        {
            string dir = $"Build/Perf/{tag}";
            Directory.CreateDirectory(dir);
            File.WriteAllText($"Build/Perf/status_build_{tag}.txt", "building");
            bool oldTiming = PlayerSettings.enableFrameTimingStats;
            PlayerSettings.enableFrameTimingStats = true;
            // Vulkan only: the shader compile for a second API was most of the last build's hour, and Vulkan is the API that reports GPU time.
            bool oldAuto = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64);
            var oldApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneLinux64);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });
            try
            {
                EditorUtility.UnloadUnusedAssetsImmediate();
                System.GC.Collect();
                var opts = new BuildPlayerOptions
                {
                    scenes = new[] { MenuScene, ScenePath },
                    locationPathName = $"{dir}/FPSKitPerf.x86_64",
                    target = BuildTarget.StandaloneLinux64,
                    options = BuildOptions.Development,
                };
                var report = BuildPipeline.BuildPlayer(opts);
                string line = $"{report.summary.result}, {report.summary.totalSize / 1048576} MB, {report.summary.totalTime.TotalSeconds:0} s, errors {report.summary.totalErrors}";
                File.WriteAllText($"Build/Perf/status_build_{tag}.txt", (report.summary.result == BuildResult.Succeeded ? "done " : "failed ") + line);
                Debug.Log("[FPSKit] Perf build: " + line);
            }
            catch (System.Exception e)
            {
                File.WriteAllText($"Build/Perf/status_build_{tag}.txt", "failed " + e.Message);
            }
            finally
            {
                PlayerSettings.enableFrameTimingStats = oldTiming;
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, oldApis);
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, oldAuto);
            }
        }
    }
}
#endif
