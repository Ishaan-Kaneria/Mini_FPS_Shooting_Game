#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Measures before guessing. <b>StaticReport</b> reads the open scene (renderers, triangles, lights, texture memory by size);
    /// <b>Begin</b> enters play mode in the open scene, visits a few viewpoints and records frame time, batches, triangles,
    /// shadow casters, lights, enemies and memory, then leaves play mode. Reports go to Logs/Perf/. Editor numbers include
    /// editor overhead: the development build (FPSKitPerfBuild) is the number that counts.
    /// </summary>
    public static class FPSKitPerfProbe
    {
        public const string Dir = "Logs/Perf";

        // Viewpoints in the stretched arena: where you start, the Midway, the Ferris wheel, the old-growth wood, the Big Top.
        static readonly (string name, Vector3 pos, float yaw)[] Views =
        {
            ("spawn", new Vector3(0f, 0f, 162f), 180f),
            ("midway", new Vector3(0f, 0f, 70f), 180f),
            ("ferris", new Vector3(70f, 0f, -28f), 90f),
            ("oldgrowth", new Vector3(110f, 0f, -120f), 200f),
            ("bigtop", new Vector3(-20f, 0f, -108f), 180f),
        };

        // ------------------------------------------------------------------ static report

        [MenuItem("Tools/MiniFPS/Performance/Static Report (open scene)")]
        public static void StaticReportMenu() => Debug.Log("[FPSKit] " + StaticReport("manual"));

        public static string StaticReport(string tag)
        {
            Directory.CreateDirectory(Dir);
            var sb = new StringBuilder();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            sb.AppendLine($"scene {scene.name}  quality '{QualitySettings.names[QualitySettings.GetQualityLevel()]}'  api {SystemInfo.graphicsDeviceType}");

            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
            long tris = 0; int skinned = 0, shadowCasters = 0, staticR = 0, instanced = 0;
            var mats = new HashSet<Material>();
            var meshes = new HashSet<Mesh>();
            foreach (var r in renderers)
            {
                if (r.shadowCastingMode != ShadowCastingMode.Off) shadowCasters++;
                if (r.gameObject.isStatic) staticR++;
                foreach (var m in r.sharedMaterials) if (m != null) { mats.Add(m); if (m.enableInstancing) instanced++; }
                Mesh mesh = null;
                if (r is MeshRenderer) mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                else if (r is SkinnedMeshRenderer sk) { mesh = sk.sharedMesh; skinned++; }
                if (mesh != null) { tris += mesh.triangles.Length / 3; meshes.Add(mesh); }
            }
            sb.AppendLine($"renderers {renderers.Length} (static {staticR}, skinned {skinned}, shadow casting {shadowCasters}), materials {mats.Count}, unique meshes {meshes.Count}, triangles (all, no culling) {tris:N0}");

            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            sb.AppendLine($"lights {lights.Length}: " + string.Join(", ", lights.GroupBy(l => l.type + (l.shadows != LightShadows.None ? "+shadow" : "")).Select(g => $"{g.Key} x{g.Count()}")));
            sb.AppendLine($"shadow distance {QualitySettings.shadowDistance:0}, cascades {QualitySettings.shadowCascades}, pixel light count {QualitySettings.pixelLightCount}, far clip {(Camera.main != null ? Camera.main.farClipPlane : -1):0}");

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
                sb.AppendLine($"URP: shadow distance {urp.shadowDistance:0}, cascades {urp.shadowCascadeCount}, main shadow res {urp.mainLightShadowmapResolution}, additional shadow res {urp.additionalLightsShadowmapResolution}, additional lights {urp.additionalLightsRenderingMode} (max {urp.maxAdditionalLightsCount}), SRP batcher {urp.useSRPBatcher}, render scale {urp.renderScale}, MSAA {urp.msaaSampleCount}, soft shadows {urp.supportsSoftShadows}");

            foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude))
                sb.AppendLine($"terrain {t.terrainData.size}, heightmap {t.terrainData.heightmapResolution}, pixel error {t.heightmapPixelError}, base map dist {t.basemapDistance}, trees {t.terrainData.treeInstanceCount} (distance {t.treeDistance}, full LOD {t.treeMaximumFullLODCount}), detail dist {t.detailObjectDistance}, instanced {t.drawInstanced}");

            // Textures actually used by the materials in the scene: runtime size, by size class.
            var texs = new HashSet<Texture>();
            foreach (var m in mats)
            {
                if (m.shader == null) continue;
                int n = m.shader.GetPropertyCount();
                for (int i = 0; i < n; i++)
                    if (m.shader.GetPropertyType(i) == ShaderPropertyType.Texture)
                    {
                        var tex = m.GetTexture(m.shader.GetPropertyName(i));
                        if (tex != null) texs.Add(tex);
                    }
            }
            long total = 0;
            var rows = new List<(string path, int w, int h, long bytes, string fmt, bool mips)>();
            foreach (var tex in texs)
            {
                if (!(tex is Texture2D) && !(tex is Cubemap)) continue;
                long b = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
                total += b;
                string path = AssetDatabase.GetAssetPath(tex);
                string fmt = tex is Texture2D t2 ? t2.format.ToString() : "cube";
                rows.Add((string.IsNullOrEmpty(path) ? tex.name : path, tex.width, tex.height, b, fmt, tex.mipmapCount > 1));
            }
            sb.AppendLine($"textures used by scene materials: {rows.Count}, runtime memory {total / 1048576} MB");
            foreach (var g in rows.GroupBy(r => Math.Max(r.w, r.h) >= 4096 ? "4096+" : Math.Max(r.w, r.h) >= 2048 ? "2048" : Math.Max(r.w, r.h) >= 1024 ? "1024" : "<=512").OrderByDescending(g => g.Key))
                sb.AppendLine($"   {g.Key}: {g.Count()} textures, {g.Sum(r => r.bytes) / 1048576} MB");
            sb.AppendLine("   biggest:");
            foreach (var r in rows.OrderByDescending(r => r.bytes).Take(14)) sb.AppendLine($"     {r.bytes / 1048576,4} MB  {r.w}x{r.h} {r.fmt}{(r.mips ? "" : " nomips")}  {r.path}");
            File.WriteAllText($"{Dir}/static_{tag}.txt", sb.ToString());
            return sb.ToString();
        }

        // ------------------------------------------------------------------ play-mode probe

        static string _tag;
        static int _view, _phase;
        static double _phaseStart;
        static int _lastFrame;
        static readonly List<float> _dt = new List<float>();
        static ProfilerRecorder _batches, _draws, _setpass, _tris, _shadowCasters, _verts;
        static readonly StringBuilder _out = new StringBuilder();
        static readonly List<float> _cpu = new List<float>(), _gpu = new List<float>();

        [MenuItem("Tools/MiniFPS/Performance/Probe Open Scene (play mode, before)")]
        public static void BeginBefore() => Begin("before");

        [MenuItem("Tools/MiniFPS/Performance/Probe Open Scene (play mode, after)")]
        public static void BeginAfter() => Begin("after");

        public static void Begin(string tag)
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[FPSKit] Perf probe: leave play mode first."); return; }
            Directory.CreateDirectory(Dir);
            _tag = tag; _view = -1; _phase = 0; _out.Clear();
            _out.AppendLine(StaticReport(tag));
            File.WriteAllText($"{Dir}/status_{tag}.txt", "starting");
            FPSKitPlayMode.SuspendStartScene();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (EditorApplication.isPaused || EditorApplication.isCompiling) return;
            double now = EditorApplication.timeSinceStartup;
            try
            {
                if (_phase == 0)                                  // let the level start
                {
                    _phaseStart = now; _phase = 1;
                    _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                    _draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                    _setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                    _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
                    _verts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
                    _shadowCasters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
                    return;
                }
                if (_phase == 1) { if (now - _phaseStart > 8.0) NextView(now); return; }
                if (_phase == 2)                                  // sampling one view
                {
                    if (Time.frameCount != _lastFrame)
                    {
                        _lastFrame = Time.frameCount;
                        if (now - _phaseStart > 2.0) { _dt.Add(Time.unscaledDeltaTime * 1000f); SampleTimings(); }   // first 2 s: shaders and streaming settle
                    }
                    if (now - _phaseStart > 10.0) { Record(); NextView(now); }
                }
            }
            catch (Exception e)
            {
                File.WriteAllText($"{Dir}/status_{_tag}.txt", "error " + e);
                Finish();
            }
        }

        static void SampleTimings()
        {
            FrameTimingManager.CaptureFrameTimings();
            var arr = new FrameTiming[1];
            if (FrameTimingManager.GetLatestTimings(1, arr) > 0)
            {
                if (arr[0].cpuFrameTime > 0) _cpu.Add((float)arr[0].cpuFrameTime);
                if (arr[0].gpuFrameTime > 0) _gpu.Add((float)arr[0].gpuFrameTime);
            }
        }

        static void NextView(double now)
        {
            _view++;
            if (_view >= Views.Length) { Finish(); return; }
            var v = Views[_view];
            var player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                var cc = player.GetComponent<CharacterController>();
                float y = FairgroundBlockout.H(v.pos.x, v.pos.z) + 1.2f;
                if (cc != null) cc.enabled = false;
                player.transform.SetPositionAndRotation(new Vector3(v.pos.x, y, v.pos.z), Quaternion.Euler(0f, v.yaw, 0f));
                if (cc != null) cc.enabled = true;
            }
            _dt.Clear(); _cpu.Clear(); _gpu.Clear();
            _phase = 2; _phaseStart = now; _lastFrame = Time.frameCount;
        }

        static float Pct(List<float> s, float p) { if (s.Count == 0) return 0f; var a = s.OrderBy(x => x).ToList(); return a[Mathf.Clamp((int)(p * (a.Count - 1)), 0, a.Count - 1)]; }

        static void Record()
        {
            var v = Views[_view];
            float avg = _dt.Count > 0 ? _dt.Average() : 0f;
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            int enemies = UnityEngine.Object.FindObjectsByType<EnemyAI>(FindObjectsInactive.Exclude).Length;
            _out.AppendLine($"[{v.name}] frames {_dt.Count}: avg {avg:0.0} ms ({(avg > 0 ? 1000f / avg : 0):0} fps), p95 {Pct(_dt, 0.95f):0.0} ms, worst {(_dt.Count > 0 ? _dt.Max() : 0):0.0} ms | " +
                            $"frame timing cpu {(_cpu.Count > 0 ? _cpu.Average() : 0):0.0} ms, gpu {(_gpu.Count > 0 ? _gpu.Average() : 0):0.0} ms | " +
                            $"batches {_batches.LastValue}, draws {_draws.LastValue}, setpass {_setpass.LastValue}, tris {_tris.LastValue / 1000}k, verts {_verts.LastValue / 1000}k, shadow casters {_shadowCasters.LastValue} | " +
                            $"lights {lights.Length} ({lights.Count(l => l.shadows != LightShadows.None)} with shadows), enemies {enemies} | " +
                            $"mem: managed {GC.GetTotalMemory(false) / 1048576} MB, total reserved {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576} MB, textures+meshes+etc used {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576} MB");
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            _batches.Dispose(); _draws.Dispose(); _setpass.Dispose(); _tris.Dispose(); _verts.Dispose(); _shadowCasters.Dispose();
            File.WriteAllText($"{Dir}/probe_{_tag}.txt", _out.ToString());
            File.WriteAllText($"{Dir}/status_{_tag}.txt", "done");
            EditorApplication.isPlaying = false;
            FPSKitPlayMode.RestoreStartScene();
        }

        // ------------------------------------------------------------------ experiments (one change at a time, restored after)

        sealed class Experiment { public string name; public Action apply, revert; }

        static List<Experiment> _exps;
        static int _expIndex, _expView;
        static int _expPhase;                 // 0 start, 1 settle, 2 sample
        static double _expStart;
        static readonly List<(string view, string name, float avg, float p95, long tris, long casters, long setpass)> _expResults = new List<(string, string, float, float, long, long, long)>();
        static readonly string[] ExpViews = { "spawn", "oldgrowth" };

        [MenuItem("Tools/MiniFPS/Performance/Run Experiments (play mode)")]
        public static void BeginExperiments()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[FPSKit] Perf experiments: leave play mode first."); return; }
            Directory.CreateDirectory(Dir);
            _tag = "exp"; _expIndex = -1; _expView = 0; _expPhase = 0; _expResults.Clear(); _out.Clear();
            File.WriteAllText($"{Dir}/status_exp.txt", "starting");
            FPSKitPlayMode.SuspendStartScene();
            EditorApplication.update -= ExpTick;
            EditorApplication.update += ExpTick;
            EditorApplication.isPlaying = true;
        }

        static List<Experiment> BuildExperiments()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            var sunLights = lights.Where(l => l.type == LightType.Directional).ToArray();
            var shadowLights = lights.Where(l => l.shadows != LightShadows.None).ToArray();
            var shadowModes = shadowLights.ToDictionary(l => l, l => l.shadows);
            var pointLights = lights.Where(l => l.type != LightType.Directional && l.enabled).ToArray();
            var terrains = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
            var volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Exclude);
            var cam = Camera.main;
            float sd = urp.shadowDistance; int cc = urp.shadowCascadeCount; int msaa = urp.msaaSampleCount;
            float far = cam != null ? cam.farClipPlane : 1000f;
            float lod = QualitySettings.lodBias;
            return new List<Experiment>
            {
                new Experiment { name = "baseline", apply = () => { }, revert = () => { } },
                new Experiment { name = "no realtime shadows at all", apply = () => { foreach (var l in shadowLights) l.shadows = LightShadows.None; }, revert = () => { foreach (var kv in shadowModes) kv.Key.shadows = kv.Value; } },
                new Experiment { name = "sun shadows only (spot shadows off)", apply = () => { foreach (var l in shadowLights) if (l.type != LightType.Directional) l.shadows = LightShadows.None; }, revert = () => { foreach (var kv in shadowModes) kv.Key.shadows = kv.Value; } },
                new Experiment { name = "shadow distance 95 -> 60", apply = () => urp.shadowDistance = 60f, revert = () => urp.shadowDistance = sd },
                new Experiment { name = "cascades 4 -> 2", apply = () => urp.shadowCascadeCount = 2, revert = () => urp.shadowCascadeCount = cc },
                new Experiment { name = "distance 60 + 2 cascades", apply = () => { urp.shadowDistance = 60f; urp.shadowCascadeCount = 2; }, revert = () => { urp.shadowDistance = sd; urp.shadowCascadeCount = cc; } },
                new Experiment { name = "MSAA 4x -> off", apply = () => urp.msaaSampleCount = 1, revert = () => urp.msaaSampleCount = msaa },
                new Experiment { name = "all point/spot lights off", apply = () => { foreach (var l in pointLights) l.enabled = false; }, revert = () => { foreach (var l in pointLights) l.enabled = true; } },
                new Experiment { name = "terrain trees + detail off", apply = () => { foreach (var t in terrains) t.drawTreesAndFoliage = false; }, revert = () => { foreach (var t in terrains) t.drawTreesAndFoliage = true; } },
                new Experiment { name = "post-processing volumes off", apply = () => { foreach (var v in volumes) v.enabled = false; }, revert = () => { foreach (var v in volumes) v.enabled = true; } },
                new Experiment { name = "far clip 1000 -> 220", apply = () => { if (cam != null) cam.farClipPlane = 220f; }, revert = () => { if (cam != null) cam.farClipPlane = far; } },
                new Experiment { name = "LOD bias 2 -> 1", apply = () => QualitySettings.lodBias = 1f, revert = () => QualitySettings.lodBias = lod },
            };
        }

        static void ExpTick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || EditorApplication.isCompiling) return;
            double now = EditorApplication.timeSinceStartup;
            try
            {
                if (_exps == null)                                // first tick in play mode
                {
                    _exps = BuildExperiments();
                    _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
                    _tris = _batches;
                    _shadowCasters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
                    _setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                    _expStart = now; _expPhase = -1; return;
                }
                if (_expPhase == -1) { if (now - _expStart > 8.0) { _expPhase = 0; } return; }      // let the level start
                if (_expPhase == 0)                                // go to the next experiment (or the next view)
                {
                    if (_expIndex >= 0) _exps[_expIndex].revert();
                    _expIndex++;
                    if (_expIndex >= _exps.Count) { _expIndex = 0; _expView++; }
                    if (_expView >= ExpViews.Length) { FinishExperiments(); return; }
                    if (_expIndex == 0) Teleport(ExpViews[_expView]);
                    _exps[_expIndex].apply();
                    _dt.Clear(); _expPhase = 1; _expStart = now; _lastFrame = Time.frameCount;
                    return;
                }
                if (Time.frameCount != _lastFrame)
                {
                    _lastFrame = Time.frameCount;
                    if (_expPhase == 2) _dt.Add(Time.unscaledDeltaTime * 1000f);
                }
                if (_expPhase == 1 && now - _expStart > 1.5) { _expPhase = 2; _expStart = now; _dt.Clear(); return; }
                if (_expPhase == 2 && now - _expStart > 4.0)
                {
                    float avg = _dt.Count > 0 ? _dt.Average() : 0f;
                    _expResults.Add((ExpViews[_expView], _exps[_expIndex].name, avg, Pct(_dt, 0.95f), _tris.LastValue, _shadowCasters.LastValue, _setpass.LastValue));
                    _expPhase = 0;
                }
            }
            catch (Exception e)
            {
                File.WriteAllText($"{Dir}/status_exp.txt", "error " + e);
                FinishExperiments();
            }
        }

        static void Teleport(string name)
        {
            var v = Views.First(x => x.name == name);
            var player = GameObject.FindWithTag("Player");
            if (player == null) return;
            var cc = player.GetComponent<CharacterController>();
            float y = FairgroundBlockout.H(v.pos.x, v.pos.z) + 1.2f;
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(v.pos.x, y, v.pos.z), Quaternion.Euler(0f, v.yaw, 0f));
            if (cc != null) cc.enabled = true;
        }

        static void FinishExperiments()
        {
            EditorApplication.update -= ExpTick;
            if (_exps != null && _expIndex >= 0 && _expIndex < _exps.Count) _exps[_expIndex].revert();     // never leave a shared asset changed
            _tris.Dispose(); _shadowCasters.Dispose(); _setpass.Dispose();
            var sb = new StringBuilder();
            foreach (var g in _expResults.GroupBy(r => r.view))
            {
                float baseMs = g.First().avg;
                sb.AppendLine($"== view '{g.Key}' (baseline {baseMs:0.0} ms = {1000f / Mathf.Max(baseMs, 0.01f):0} fps)");
                foreach (var r in g) sb.AppendLine($"   {r.name,-46} avg {r.avg,5:0.0} ms ({1000f / Mathf.Max(r.avg, 0.01f),3:0} fps)  p95 {r.p95,5:0.0}  delta {r.avg - baseMs,+6:0.0} ms | tris {r.tris / 1000}k, shadow casters {r.casters}, setpass {r.setpass}");
            }
            File.WriteAllText($"{Dir}/experiments.txt", sb.ToString());
            File.WriteAllText($"{Dir}/status_exp.txt", "done");
            _exps = null;
            EditorApplication.isPlaying = false;
            FPSKitPlayMode.RestoreStartScene();
        }
    }
}
#endif
