#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Builds for measuring and play-testing outside the editor. The desert ones (main menu + Desert Outpost, Linux, Vulkan):
    /// BuildSmooth (release, the walk benchmark's and the play-test's), BuildDesertDev (development); Tools/build-desert.sh.
    /// Also a development build of the main menu and the Abandoned Fairground (Linux x64, Vulkan only) for measuring outside the editor. It runs the PerfProbe
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

        /// <summary>
        /// A release build of the menu and Desert Outpost (Linux x64, Vulkan) for SmoothProbe: start it with
        /// <c>-fpskitsmooth &lt;report&gt;</c>. Build/Perf/smooth/FPSKitSmooth.x86_64.
        /// </summary>
        [MenuItem("Tools/MiniFPS/Performance/Build Desert Play-Test Player (release)")]
        public static void BuildSmooth() => Build("smooth", "Assets/FPSKit_Generated/Scenes/DesertOutpost.unity", BuildOptions.None, "FPSKitSmooth");

        /// <summary>
        /// The same menu and Desert Outpost as a development build (Build/Perf/desertdev/FPSKitDesertDev.x86_64): the Profiler
        /// can attach, errors show, and FrameCostProbe (<c>-fpskitcost &lt;report&gt;</c>) reads every render pass's GPU time.
        /// A development player runs slower than the release one: judge frame rates in the release build.
        /// </summary>
        [MenuItem("Tools/MiniFPS/Performance/Build Desert Development Player")]
        public static void BuildDesertDev() => Build("desertdev", "Assets/FPSKit_Generated/Scenes/DesertOutpost.unity", BuildOptions.Development, "FPSKitDesertDev");

        public static void Build(string tag) => Build(tag, ScenePath, BuildOptions.Development, "FPSKitPerf");

        static void Build(string tag, string scene, BuildOptions options, string name)
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
                    scenes = new[] { MenuScene, scene }.Concat(FPSKitTownRoomScenes.ExistingPaths()).ToArray(),     // the town's rooms (RoomScenes)
                    locationPathName = $"{dir}/{name}.x86_64",
                    target = BuildTarget.StandaloneLinux64,
                    options = options,
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
