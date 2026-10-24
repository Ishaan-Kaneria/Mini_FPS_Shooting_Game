#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Real bounced light for the desert town: Adaptive Probe Volumes (Ishaan, 2026-10-24: "make the shadows, lighting very
    /// realistic. Forget about the size, I want real"; Docs/DESIGN_NOTES.md "Real light: adaptive probe volumes").
    ///
    /// Until now the town's walls took the flat sky ambient whole, one colour indoors and out: a room was lit like the street
    /// with the sun taken away -- no light falling in from the windows, no darker far wall, nothing under a table -- and the
    /// light probes (FPSKitTownProbes) lit only what moves. APV fills the town with probes, dense wherever there is
    /// geometry (3 m: Unity's limit is 67 M probes a set, and at 1 m the town wanted more), baked by path tracing from the
    /// sky and the sun's bounce, and every pixel of every renderer -- wall, shelf,
    /// face, car -- reads the light where it is. No lightmap UVs, so it works on the generated meshes.
    ///
    /// - High and Medium use APV (their pipeline assets: FPSKitQualityTiers). Low -- the phones and the web build -- keeps the
    ///   classic probes, so the scene carries both: the classic bake first (BakeTownProbes), then this, APV only.
    /// - Every static mesh renderer in the arena contributes and takes its light from probes (no lightmaps are made); what
    ///   moves (people, cars, enemies) only takes it.
    /// - Baked on the GPU when there is a graphics device (UNITY_GRAPHICS=1), else on the CPU (slow).
    ///
    ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.BakeDesertLight      (the saved scene, without rebuilding it)
    /// </summary>
    public static class FPSKitTownGI
    {
        public const string DesertScene = "Assets/FPSKit_Generated/Scenes/DesertOutpost.unity";

        /// <summary>
        /// Bounce strength for the APV bake: the classic probes' 2.75 (FPSKitTownProbes) made up for walls lit by the whole
        /// sky ambient beside people lit by probes; with everything on probes the light can be what it is.
        /// </summary>
        const float ApvIndirect = 1.8f;

        /// <summary>
        /// The sky's share of the bake against the scene's own ambient strength. With everything on probes a shaded street
        /// is lit by the sky it can see and the light off the sunlit sand and walls, as it should be, but at 1.15 bounce and
        /// the plain sky a man in a building's shade read darker than the old flat ambient left him (the lighting tour,
        /// face_shade): his face was the thing asked to be more visible. 1.8 and 1.3.
        /// </summary>
        const float ApvSky = 1.3f;

        /// <summary>Metres the probe volume spans across the town (the arena is a kilometre; a little over for its edge).</summary>
        const float TownSpan = 1060f;

        /// <summary>
        /// Probes past which the bake is stopped before it starts lighting them (FPSKIT_GI_MAX_PROBES): the editor ran out of
        /// memory baking the whole terrain's worth.
        /// </summary>
        static long MaxProbes => long.TryParse(Environment.GetEnvironmentVariable("FPSKIT_GI_MAX_PROBES"), out var n) ? n : 4_000_000;

        /// <summary>Opens the saved desert scene, bakes its APV, saves it, and exits (batch).</summary>
        public static void BatchBakeSaved()
        {
            var scene = EditorSceneManager.OpenScene(DesertScene, OpenSceneMode.Single);
            Bake(scene, DesertScene, done: ok =>
            {
                EditorSceneManager.SaveScene(scene, DesertScene);
                AssetDatabase.SaveAssets();
                FPSKitBatch.Exit(ok ? 0 : 1);
            });
        }

        /// <summary>
        /// The same, finished before it returns (the scene build runs in one call and exits after it): the bake's own
        /// per-update step is called in a loop instead of on the editor's update.
        /// </summary>
        public static bool BakeNow(UnityEngine.SceneManagement.Scene scene, string path)
        {
            bool result = false;
            Bake(scene, path, ok => result = ok, synchronous: true);
            return result;
        }

        /// <summary>
        /// Prepares the open town scene for APV and bakes it (asynchronously, on the editor's update, unless
        /// <paramref name="synchronous"/>); <paramref name="done"/> is told whether it baked.
        /// </summary>
        public static void Bake(UnityEngine.SceneManagement.Scene scene, string path, Action<bool> done, bool synchronous = false)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            FPSKitQualityTiers.Ensure();

            // Textures at an eighth of their size for the bake: the GPU here shares the system's memory, so every texture
            // the editor uploads counts against the 14 GB, and with the whole town's at full size the baker was killed by
            // earlyoom with the editor itself showing only 4.4 GB. Bounce colour needs no detail.
            int mipLimit = QualitySettings.globalTextureMipmapLimit;
            QualitySettings.globalTextureMipmapLimit = 3;

            // The pipeline that bakes must be one with APV on: High, for the bake.
            int was = QualitySettings.GetQualityLevel();
            int high = Array.IndexOf(QualitySettings.names, "High");
            if (high >= 0) QualitySettings.SetQualityLevel(high, true);
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null || asset.lightProbeSystem != LightProbeSystem.ProbeVolumes)
            {
                Debug.LogError($"[FPSKit] town GI: the active pipeline asset ({(asset != null ? asset.name : "none")}) has no adaptive probe volumes");
                QualitySettings.globalTextureMipmapLimit = mipLimit;   // (it is a project setting: never left changed)
                QualitySettings.SetQualityLevel(was, true);
                done(false);
                return;
            }

            // One global volume over everything.
            var arena = GameObject.Find("Arena");
            var volume = Object.FindAnyObjectByType<ProbeVolume>();
            if (volume == null)
            {
                var go = new GameObject("TownProbeVolume");
                if (arena != null) go.transform.SetParent(arena.transform, false);
                volume = go.AddComponent<ProbeVolume>();
            }
            // Over the town only (the arena's kilometre and the minaret's height), not the 1.4 km terrain: probes are laid
            // densely along every surface, the open desert round the town included, and over all of it the bake took 10.4 GB
            // and earlyoom killed the editor (this laptop has 14).
            volume.mode = ProbeVolume.Mode.Local;
            volume.transform.position = new Vector3(0f, 30f, 0f);
            volume.transform.rotation = Quaternion.identity;
            volume.transform.localScale = Vector3.one;
            volume.size = new Vector3(TownSpan, 90f, TownSpan);
            volume.fillEmptySpaces = false;

            // The baking set: this scene alone.
            string setPath = path.Replace(".unity", "_APV.asset");
            var set = AssetDatabase.LoadAssetAtPath<ProbeVolumeBakingSet>(setPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
                set.name = System.IO.Path.GetFileNameWithoutExtension(setPath);
                typeof(ProbeVolumeBakingSet).GetMethod("SetDefaults", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(set, null);
                AssetDatabase.CreateAsset(set, setPath);
            }
            // 3 m wherever there is geometry (a room gets two or three across; the contact shading under and round small
            // things is SSAO's), up to 81 m in open air. At 1 m the town wanted more than Unity's 67,180,350 probes a set.
            set.minDistanceBetweenProbes = 3f;
            set.simplificationLevels = 3;
            set.minRendererVolumeSize = 1f;         // props smaller than a cubic metre do not subdivide the grid (they still shade)
            int backdropLayer = LayerMask.NameToLayer("Backdrop");
            set.renderersLayerMask = backdropLayer >= 0 ? ~(1 << backdropLayer) : ~0;   // the apron and the far scenery lay no probes
            set.skyOcclusion = false;
            EditorUtility.SetDirty(set);
            set.TryAddScene(AssetDatabase.AssetPathToGUID(path));   // false when it is in the set already

            // Every static mesh in the arena takes its light from the probes (nothing gets a lightmap). Only the town's own
            // geometry -- buildings, their insides, landmarks, roads, the edge's structures -- shades and bounces light in
            // the bake: the props and pack models (28,000 objects in all, 1,189 meshes, 329 materials) and the window joinery
            // took the CPU baker to 10 GB and earlyoom killed it. They still receive the light where they stand. What moves
            // is left as it is (it takes probe light anyway).
            int statics = 0, bouncing = 0;
            int backdrop = LayerMask.NameToLayer("Backdrop");
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                if ((flags & StaticEditorFlags.ContributeGI) != 0) GameObjectUtility.SetStaticEditorFlags(go, flags & ~StaticEditorFlags.ContributeGI);
            }
            if (arena != null)
                foreach (var r in arena.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.GetComponentInParent<Civilian>() != null || r.GetComponentInParent<EnemyAI>() != null || r.GetComponentInParent<Rigidbody>() != null) continue;
                    if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                    if (r.gameObject.layer == backdrop) continue;   // far scenery: the sky ambient, as before
                    r.receiveGI = ReceiveGI.LightProbes;
                    r.lightProbeUsage = LightProbeUsage.BlendProbes;
                    statics++;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || AssetDatabase.Contains(mf.sharedMesh) || r.name.StartsWith("Detail_")) continue;
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, GameObjectUtility.GetStaticEditorFlags(r.gameObject) | StaticEditorFlags.ContributeGI);
                    bouncing++;
                }
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags | StaticEditorFlags.ContributeGI);
            }

            // How the probes are read (the scene's global post-processing profile): at 3 m apart the defaults' offsets
            // (0.05 along the normal, 0.1 towards the eye) are too short, and a surface read probes buried in the wall or the
            // ceiling behind it -- black ceilings in rooms lit from the door.
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (!v.isGlobal || v.sharedProfile == null) continue;
                var options = v.sharedProfile.TryGet<ProbeVolumesOptions>(out var o) ? o : v.sharedProfile.Add<ProbeVolumesOptions>(true);
                options.normalBias.Override(0.4f);
                options.viewBias.Override(0.3f);
                options.leakReductionMode.Override(APVLeakReductionMode.Quality);
                EditorUtility.SetDirty(v.sharedProfile);
            }

            // The lighting settings: the classic probes' bake keeps its own strength; this bake its own.
            float ambientWas = RenderSettings.ambientIntensity;
            RenderSettings.ambientIntensity = ambientWas * ApvSky;
            var settings = Lightmapping.lightingSettings;
            float classicIndirect = settings != null ? settings.indirectScale : 1f;
            var lightmapper = settings != null ? settings.lightmapper : LightingSettings.Lightmapper.ProgressiveCPU;
            if (settings != null)
            {
                settings.indirectScale = ApvIndirect;
                // The GPU lightmapper needs OpenCL; this laptop's Intel Arc has none unless Intel's runtime is installed
                // (intel-opencl-icd), and without it the bake waited forever on output never written. FPSKIT_GI_GPU=1 asks
                // for the GPU once the runtime is there.
                settings.lightmapper = Environment.GetEnvironmentVariable("FPSKIT_GI_GPU") == "1" && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null
                    ? LightingSettings.Lightmapper.ProgressiveGPU : LightingSettings.Lightmapper.ProgressiveCPU;
                settings.indirectSampleCount = 128;
                settings.environmentSampleCount = 128;
                settings.maxBounces = 3;
            }

            // URP sets the probe volumes up only when it makes its pipeline, from the asset active then; one made before APV
            // was switched on (earlier in this session) has none and the bake will not start. So another tier's pipeline is
            // made first, then High's again, by rendering a camera under each.
            var cam = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
            void RenderOnce()
            {
                if (cam == null) return;
                var rt = RenderTexture.GetTemporary(64, 64, 24);
                try { RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = rt }); }
                finally { RenderTexture.ReleaseTemporary(rt); }
            }
            int lowTier = Array.IndexOf(QualitySettings.names, "Low");
            if (lowTier >= 0 && high >= 0)
            {
                QualitySettings.SetQualityLevel(lowTier, true);
                RenderOnce();
                QualitySettings.SetQualityLevel(high, true);
            }
            RenderOnce();
            if (!ProbeReferenceVolume.instance.isInitialized)
                Debug.LogWarning($"[FPSKit] town GI: the probe reference volume is not initialised (pipeline {RenderPipelineManager.currentPipeline?.GetType().Name ?? "none"})");

            // The light transport failing to start (no OpenCL device for the GPU lightmapper) is only logged: the bake then
            // reads output that was never written, every frame, for ever (458,000 exceptions before the editor fell over).
            bool transportFailed = false;
            void Watch(string message, string trace, LogType type)
            {
                if (message.Contains("Light baking could not be started") || message.Contains("PostProcessingOutput/APVBake")) transportFailed = true;
            }
            Application.logMessageReceived += Watch;
            // Realtime reflection probes stay out of any bake (headless captures drew white: FPSKitTownProbes).
            var reflection = Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).Where(p => p.enabled).ToList();
            foreach (var p in reflection) p.enabled = false;

            void Finish(bool ok)
            {
                Application.logMessageReceived -= Watch;
                QualitySettings.globalTextureMipmapLimit = mipLimit;
                RenderSettings.ambientIntensity = ambientWas;
                foreach (var p in reflection) p.enabled = true;
                if (settings != null) { settings.indirectScale = classicIndirect; settings.lightmapper = lightmapper; EditorUtility.SetDirty(settings); }
                QualitySettings.SetQualityLevel(was, true);
                var descs = typeof(ProbeVolumeBakingSet).GetField("cellDescs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(set) as System.Collections.ICollection;
                int cells = descs?.Count ?? 0;
                long bytes = 0;
                // The set's data lies beside it: <set>.Cell*.bytes and <set>-<scenario>.Cell*.bytes.
                string dir = System.IO.Path.GetDirectoryName(setPath), stem = System.IO.Path.GetFileNameWithoutExtension(setPath);
                foreach (var f in System.IO.Directory.GetFiles(dir, stem + "*.bytes")) bytes += new System.IO.FileInfo(f).Length;
                Debug.Log($"[FPSKit] town GI (APV) {(ok ? "baked" : "FAILED")}: {cells} cells, {bytes / 1048576f:0} MB of probe data, {statics} static renderers ({bouncing} bouncing), " +
                          $"{clock.Elapsed.TotalMinutes:0.0} min, {(settings != null ? settings.lightmapper.ToString() : "?")}");
                done(ok);
            }

            if (!AdaptiveProbeVolumes.BakeAsync())
            {
                Debug.LogError("[FPSKit] town GI: the APV bake would not start");
                Finish(false);
                return;
            }
            double lastReport = 0;
            var dataField = typeof(AdaptiveProbeVolumes).GetField("s_BakeData", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            string Status()
            {
                object data = dataField?.GetValue(null);
                if (data == null) return "";
                var t = data.GetType();
                object probes = t.GetField("probeCount")?.GetValue(data), step = t.GetField("step")?.GetValue(data), failed = t.GetField("failed")?.GetValue(data);
                return $", {probes:N0} probes, step {step}{(failed is bool f && f ? ", FAILED" : "")}";
            }
            // Placement is done by the time the bake starts: too many probes, and it stops here, before the lighting.
            long placed = 0;
            {
                object data = dataField?.GetValue(null);
                if (data != null && data.GetType().GetField("probeCount")?.GetValue(data) is int count) placed = count;
                Debug.Log($"[FPSKit] town GI: {placed:N0} probes placed in {clock.Elapsed.TotalMinutes:0.0} min (limit {MaxProbes:N0})");
            }
            if (placed > MaxProbes)
            {
                Debug.LogError($"[FPSKit] town GI: {placed:N0} probes is over the limit; not baked");
                AdaptiveProbeVolumes.Cancel();
                Finish(false);
                return;
            }
            bool Failed()
            {
                if (transportFailed) return true;
                object data = dataField?.GetValue(null);
                return data != null && data.GetType().GetField("failed")?.GetValue(data) is bool f && f;
            }
            if (synchronous)
            {
                var step = typeof(AdaptiveProbeVolumes).GetMethod("AsyncBakeCallback", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (step != null)
                {
                    EditorApplication.update -= (EditorApplication.CallbackFunction)Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction), step);
                    while (AdaptiveProbeVolumes.isRunning)
                    {
                        step.Invoke(null, null);
                        if (clock.Elapsed.TotalSeconds - lastReport > 60) { lastReport = clock.Elapsed.TotalSeconds; Debug.Log($"[FPSKit] town GI: baking, {clock.Elapsed.TotalMinutes:0.0} min{Status()}"); }
                        if (Failed()) { AdaptiveProbeVolumes.Cancel(); break; }
                    }
                    Finish(set.HasBakedData() && !transportFailed);
                    return;
                }
            }
            void Pump()
            {
                if (AdaptiveProbeVolumes.isRunning && !Failed())
                {
                    if (clock.Elapsed.TotalSeconds - lastReport > 60) { lastReport = clock.Elapsed.TotalSeconds; Debug.Log($"[FPSKit] town GI: baking, {clock.Elapsed.TotalMinutes:0.0} min{Status()}"); }
                    return;
                }
                if (AdaptiveProbeVolumes.isRunning) AdaptiveProbeVolumes.Cancel();
                EditorApplication.update -= Pump;
                Finish(set.HasBakedData() && !transportFailed);
            }
            EditorApplication.update += Pump;
        }
    }
}
#endif
