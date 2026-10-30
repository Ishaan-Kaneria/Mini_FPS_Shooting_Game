using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Where the frame time goes in Desert Outpost (Part A of the 2026-10-29 brief: measure before fixing). Does nothing unless
/// the player is started with <c>-fpskitcost &lt;report file&gt;</c> (optionally <c>-fpskitquality Low|Medium|High</c>).
/// It stands the player at fixed views along WalkRoute (every 100 m, looking along the route) and at each draws the frame
/// as it is, then with one thing taken away at a time: the sun's shadows, post-processing, transparent surfaces, the
/// furniture inside rooms, roof and facade props, street props, people and cars, the facades' detail meshes, the terrain,
/// the arms and gun, everything past 150 m, props' shadows. The frame time lost with each is its cost. At each view it
/// also counts what is drawn (batches, SetPass calls, triangles, shadow casters, and by group the renderers and triangles
/// drawn for the camera or a shadow) and how many rooms' furniture is drawn while the eye is outside the room. In a
/// development build it reads the GPU and CPU time of every profiler sample (the render passes) at each view as well.
/// Refuses on battery as WalkProbe does. Holds no static state.
/// </summary>
public class FrameCostProbe : MonoBehaviour
{
    string _out, _tier;
    const float ViewSpacing = 100f, FarCut = 150f;
    const int Settle = 5, Measure = 20;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-fpskitcost");
        if (i < 0 || i + 1 >= args.Length) return;
        var go = new GameObject("FrameCostProbe");
        DontDestroyOnLoad(go);
        var probe = go.AddComponent<FrameCostProbe>();
        probe._out = args[i + 1];
        int q = Array.IndexOf(args, "-fpskitquality");
        if (q >= 0 && q + 1 < args.Length) probe._tier = args[q + 1];
    }

    sealed class Group { public string name; public int casters; public readonly List<Renderer> rs = new List<Renderer>(); public readonly List<long> tris = new List<long>(); }

    sealed class Config
    {
        public string name; public Action on, off;
        public readonly List<float> ms = new List<float>(), gpu = new List<float>();
        public readonly List<long>[] counters = WalkProbe.DrawCounters.Names.Select(_ => new List<long>()).ToArray();
    }

    IEnumerator Start()
    {
        var sb = new StringBuilder();
        if (WalkProbe.PowerSource() == "battery" && Array.IndexOf(Environment.GetCommandLineArgs(), "-fpskitallowbattery") < 0)
        {
            Write("refused: on battery; plug in, or pass -fpskitallowbattery\n");
            Application.Quit(3);
            yield break;
        }
        int savedTier = GameSettings.QualityTier;
        if (_tier != null && Enum.TryParse(_tier, true, out GameSettings.Quality wanted)) GameSettings.QualityTier = (int)wanted;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        if (SceneManager.GetActiveScene().name != "DesertOutpost")
        {
            SceneManager.LoadScene("DesertOutpost");
            yield return new WaitForSecondsRealtime(1f);
        }
        float until = Time.realtimeSinceStartup + 60f;
        LevelManager level = null;
        while (Time.realtimeSinceStartup < until && (level == null || level.IsBriefing || !level.IsRunning))
        {
            level = FindAnyObjectByType<LevelManager>();
            yield return null;
        }
        yield return new WaitForSecondsRealtime(3f);

        var player = GameObject.FindWithTag("Player");
        var motor = player != null ? player.GetComponent<PlayerMotor>() : null;
        var health = player != null ? player.GetComponent<Health>() : null;
        var cc = player != null ? player.GetComponent<CharacterController>() : null;
        var cam = Camera.main;
        if (player == null || motor == null || cam == null) { Write("no player or camera\n"); Application.Quit(1); yield break; }
        var line = WalkRoute.Line(player.transform.position, WalkRoute.Desert, new List<string>());
        var views = new List<(Vector3 pos, Vector3 dir)>();
        float along = 0f, next = 0f;
        for (int i = 1; i < line.Count; i++)
        {
            float len = Vector3.Distance(line[i - 1], line[i]);
            while (next <= along + len)
            {
                var p = Vector3.Lerp(line[i - 1], line[i], (next - along) / Mathf.Max(0.001f, len));
                views.Add((p, Vector3.ProjectOnPlane(line[i] - line[i - 1], Vector3.up).normalized));
                next += ViewSpacing;
            }
            along += len;
        }

        // The town's renderers by what they are.
        var arena = GameObject.Find("Arena");
        var rooms = FindObjectsByType<IndoorReflections>(FindObjectsInactive.Include)
            .SelectMany(ir => ir.GetComponentsInChildren<ReflectionProbe>(true)).Select(p => p.bounds).ToList();
        var groups = new Dictionary<string, Group>();
        var roomFurniture = rooms.Select(_ => new List<Renderer>()).ToList();
        Group G(string n) { if (!groups.TryGetValue(n, out var g)) groups[n] = g = new Group { name = n }; return g; }
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            string top = Top(r.transform, arena != null ? arena.transform : null);
            string path = PathOf(r.transform);
            string g;
            if (r.gameObject.layer == LayerMask.NameToLayer("FirstPerson") || path.Contains("CameraHolder")) g = "arms and gun";
            else if (r.name.StartsWith("Detail_")) g = "facade detail meshes";
            else if (top == "Buildings" && path.Contains("BuildingProps"))
            {
                int room = rooms.FindIndex(b => b.Contains(r.bounds.center));
                if (room >= 0) { g = "furniture in rooms"; roomFurniture[room].Add(r); }
                else g = "roof and facade props";
            }
            else if (top == "Buildings") g = "building shells (walls, floors, rooms)";
            else if (top == "Street" || top == "ShopCover" || path.Contains("LandmarkProps") || path.Contains("EdgeProps")) g = "street and yard props";
            else if (top == "Life") g = "people and cars";
            else if (top == "Landmarks" || top == "Edge") g = "landmarks and the edge";
            else g = top ?? "other";
            var grp = G(g);
            grp.rs.Add(r); grp.tris.Add(Tris(r));
            if (r.shadowCastingMode != ShadowCastingMode.Off) grp.casters++;
        }
        var all = groups.Values.SelectMany(g => g.rs).ToList();
        var terrains = FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
        var sun = FindObjectsByType<Light>(FindObjectsInactive.Exclude).FirstOrDefault(l => l.type == LightType.Directional && l.enabled);
        var camData = cam.GetUniversalAdditionalCameraData();
        // The camera that runs post-processing: the stack's last (QualityTiers.ApplyPost), else the world's.
        var postData = camData;
        foreach (var o in camData.cameraStack) if (o != null && o.isActiveAndEnabled && o.GetUniversalAdditionalCameraData().renderPostProcessing) postData = o.GetUniversalAdditionalCameraData();
        var transparent = all.Where(r => r != null).SelectMany(r => r.sharedMaterials).Where(m => m != null && m.renderQueue > 2500).Distinct().ToList();
        string[] passes = { "UniversalForward", "UniversalForwardOnly", "SRPDefaultUnlit", "ShadowCaster", "DepthOnly", "DepthNormals" };
        void Passes(bool on) { foreach (var m in transparent) foreach (var p in passes) m.SetShaderPassEnabled(p, on); }
        var propShadows = new[] { "furniture in rooms", "roof and facade props", "street and yard props" }.Where(groups.ContainsKey)
            .SelectMany(n => groups[n].rs).Where(r => r.shadowCastingMode == ShadowCastingMode.On).ToList();
        var arms = Camera.allCameras.Where(c => c != cam && c.GetUniversalAdditionalCameraData().renderType == CameraRenderType.Overlay).ToList();
        var features = ActiveFeatures();
        var sunShadows = sun ? sun.shadows : LightShadows.None;

        Action Off(IEnumerable<Renderer> rs) { var l = rs.ToList(); return () => { foreach (var r in l) if (r != null) r.enabled = false; }; }
        Action On(IEnumerable<Renderer> rs) { var l = rs.ToList(); return () => { foreach (var r in l) if (r != null) r.enabled = true; }; }
        var farList = new List<Renderer>();
        var configs = new List<Config>
        {
            new Config { name = "everything", on = () => { }, off = () => { } },
            new Config { name = "no sun shadows", on = () => { if (sun) sun.shadows = LightShadows.None; }, off = () => { if (sun) sun.shadows = sunShadows; } },
            new Config { name = "no post-processing", on = () => postData.renderPostProcessing = false, off = () => postData.renderPostProcessing = true },
            new Config { name = "no renderer features (" + string.Join(", ", features.Select(f => f.name)) + ")",
                         on = () => { foreach (var f in features) f.SetActive(false); }, off = () => { foreach (var f in features) f.SetActive(true); } },
            new Config { name = $"no transparent surfaces ({transparent.Count} materials)", on = () => Passes(false), off = () => Passes(true) },
            new Config { name = "props cast no shadows", on = () => { foreach (var r in propShadows) if (r != null) r.shadowCastingMode = ShadowCastingMode.Off; },
                         off = () => { foreach (var r in propShadows) if (r != null) r.shadowCastingMode = ShadowCastingMode.On; } },
            new Config { name = "no terrain", on = () => { foreach (var t in terrains) { t.drawHeightmap = false; t.drawTreesAndFoliage = false; } },
                         off = () => { foreach (var t in terrains) { t.drawHeightmap = true; t.drawTreesAndFoliage = true; } } },
            new Config { name = "no arms and gun camera", on = () => { foreach (var c in arms) c.enabled = false; }, off = () => { foreach (var c in arms) c.enabled = true; } },
            new Config { name = $"nothing past {FarCut:0} m", on = () =>
                {
                    farList.Clear();
                    var eye = cam.transform.position;
                    foreach (var r in all) if (r != null && r.enabled && Vector3.Distance(r.bounds.ClosestPoint(eye), eye) > FarCut) { r.enabled = false; farList.Add(r); }
                    foreach (var t in terrains) t.drawTreesAndFoliage = false;
                },
                off = () => { foreach (var r in farList) r.enabled = true; foreach (var t in terrains) t.drawTreesAndFoliage = true; } },
        };
        foreach (var g in groups.Values.OrderByDescending(g => g.tris.Sum()))
            if (g.name != "arms and gun" && g.rs.Count > 150) configs.Add(new Config { name = "no " + g.name, on = Off(g.rs), off = On(g.rs) });

        // Settings a tier could change (Part A's candidates), each alone.
        var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        float lodBias = QualitySettings.lodBias, shadowDist = urpAsset != null ? urpAsset.shadowDistance : 0f, scale = urpAsset != null ? urpAsset.renderScale : 1f;
        var armsData = arms.Select(c => c.GetUniversalAdditionalCameraData()).ToList();
        var bloom = FindObjectsByType<Volume>(FindObjectsInactive.Exclude).Where(v => v.isGlobal && v.sharedProfile != null)
            .Select(v => v.sharedProfile.TryGet<Bloom>(out var b) ? b : null).Where(b => b != null && b.active).ToList();
        var terrainWas = terrains.Select(t => (t.heightmapPixelError, t.basemapDistance, t.detailObjectDistance, t.detailObjectDensity)).ToList();
        configs.Add(new Config { name = $"LOD bias 1 (now {lodBias:0.##})", on = () => QualitySettings.lodBias = 1f, off = () => QualitySettings.lodBias = lodBias });
        configs.Add(new Config { name = "LOD bias 0.6", on = () => QualitySettings.lodBias = 0.6f, off = () => QualitySettings.lodBias = lodBias });
        configs.Add(new Config { name = "no MSAA", on = () => cam.allowMSAA = false, off = () => cam.allowMSAA = true });
        configs.Add(new Config { name = "arms camera without its own shadow pass", on = () => { foreach (var d in armsData) d.renderShadows = false; }, off = () => { foreach (var d in armsData) d.renderShadows = true; } });
        configs.Add(new Config { name = "no bloom", on = () => { foreach (var b in bloom) b.active = false; }, off = () => { foreach (var b in bloom) b.active = true; } });
        configs.Add(new Config { name = $"shadows to 50 m (now {shadowDist:0})", on = () => { if (urpAsset) urpAsset.shadowDistance = 50f; }, off = () => { if (urpAsset) urpAsset.shadowDistance = shadowDist; } });
        configs.Add(new Config { name = "render scale 0.85", on = () => { if (urpAsset) urpAsset.renderScale = 0.85f; }, off = () => { if (urpAsset) urpAsset.renderScale = scale; } });
        configs.Add(new Config { name = "terrain: pixel error 8, base map at 120 m", on = () => { foreach (var t in terrains) { t.heightmapPixelError = 8f; t.basemapDistance = 120f; } },
                                 off = () => { for (int i = 0; i < terrains.Length; i++) { terrains[i].heightmapPixelError = terrainWas[i].heightmapPixelError; terrains[i].basemapDistance = terrainWas[i].basemapDistance; } } });
        var terrainMats = terrains.Select(t => t.materialTemplate).Where(m => m != null).Distinct().ToList();
        var queues = terrainMats.Select(m => m.renderQueue).ToList();
        configs.Add(new Config { name = "terrain drawn after the town (queue 2010)", on = () => { foreach (var m in terrainMats) m.renderQueue = 2010; },
                                 off = () => { for (int i = 0; i < terrainMats.Count; i++) terrainMats[i].renderQueue = queues[i]; } });
        configs.Add(new Config { name = "terrain drawn first (queue 1900)", on = () => { foreach (var m in terrainMats) m.renderQueue = 1900; },
                                 off = () => { for (int i = 0; i < terrainMats.Count; i++) terrainMats[i].renderQueue = queues[i]; } });
        var aa = postData.antialiasing;
        configs.Add(new Config { name = $"no post anti-aliasing (now {aa})", on = () => postData.antialiasing = AntialiasingMode.None, off = () => postData.antialiasing = aa });
        configs.Add(new Config { name = "post anti-aliasing FXAA", on = () => postData.antialiasing = AntialiasingMode.FastApproximateAntialiasing, off = () => postData.antialiasing = aa });
        var aaq = postData.antialiasingQuality;
        configs.Add(new Config { name = "SMAA at low quality", on = () => { postData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; postData.antialiasingQuality = AntialiasingQuality.Low; },
                                 off = () => { postData.antialiasing = aa; postData.antialiasingQuality = aaq; } });
        int cascades = urpAsset != null ? urpAsset.shadowCascadeCount : 1;
        configs.Add(new Config { name = $"one shadow cascade (now {cascades})", on = () => { if (urpAsset) urpAsset.shadowCascadeCount = 1; }, off = () => { if (urpAsset) urpAsset.shadowCascadeCount = cascades; } });
        configs.Add(new Config { name = $"shadows to 60 m (now {shadowDist:0})", on = () => { if (urpAsset) urpAsset.shadowDistance = 60f; }, off = () => { if (urpAsset) urpAsset.shadowDistance = shadowDist; } });
        var grain = FindObjectsByType<Volume>(FindObjectsInactive.Exclude).Where(v => v.isGlobal && v.sharedProfile != null)
            .Select(v => v.sharedProfile.TryGet<FilmGrain>(out var f) ? f : null).Where(f => f != null && f.active).ToList();
        configs.Add(new Config { name = "no film grain", on = () => { foreach (var f in grain) f.active = false; }, off = () => { foreach (var f in grain) f.active = true; } });
        bool hdr = urpAsset != null && urpAsset.supportsHDR;
        configs.Add(new Config { name = $"HDR off (now {hdr})", on = () => { if (urpAsset) urpAsset.supportsHDR = false; }, off = () => { if (urpAsset) urpAsset.supportsHDR = hdr; } });
        configs.Add(new Config { name = "terrain: no detail meshes", on = () => { foreach (var t in terrains) t.detailObjectDensity = 0f; },
                                 off = () => { for (int i = 0; i < terrains.Length; i++) terrains[i].detailObjectDensity = terrainWas[i].detailObjectDensity; } });

        // Profiler samples (development builds only): every sample's GPU and CPU time at each view.
        var samplerNames = new List<string>();
        Sampler.GetNames(samplerNames);
        var recorders = new List<(string name, Recorder rec)>();
        foreach (var n in samplerNames)
        {
            var rec = Recorder.Get(n);
            if (rec != null && rec.isValid) recorders.Add((n, rec));
        }
        var gpuSum = new Dictionary<string, double>(); var cpuSum = new Dictionary<string, double>();
        int profiledFrames = 0;

        var census = new StringBuilder();
        var timings = new FrameTiming[1];
        using var draws = new WalkProbe.DrawCounters();
        MobileInput.Reset();
        MobileInput.Active = true;
        int roomsOutsideDrawn = 0, roomsOutsideTotal = 0;
        // The report, written after every view: a failure late in a twenty-minute run must not lose what was measured.
        var huge = new List<string>();
        string Report(int viewsDone)
        {
            var r = new StringBuilder();
            float Med(List<float> l) { if (l.Count == 0) return 0f; var o = l.OrderBy(x => x).ToArray(); return o[o.Length / 2]; }
            long MedL(List<long> l) { if (l.Count == 0) return -1; var o = l.OrderBy(x => x).ToArray(); return o[o.Length / 2]; }
            var full = configs[0];
            float baseMs = Med(full.ms);
            r.AppendLine($"frame cost: {viewsDone} of {views.Count} views {ViewSpacing:0} m apart on WalkRoute, {Measure} frames each way | api {SystemInfo.graphicsDeviceType} | {Screen.width}x{Screen.height} | " +
                          $"quality {QualitySettings.names[QualitySettings.GetQualityLevel()]} | render scale {QualityTiers.TierRenderScale:0.00} | vsync {QualitySettings.vSyncCount} | dev build {Debug.isDebugBuild} | power {WalkProbe.PowerSource()}");
            r.AppendLine($"everything on: frame {baseMs:0.0} ms median (gpu {(full.gpu.Count > 0 ? Med(full.gpu).ToString("0.0") : "not reported")}) | " +
                          string.Join(" | ", WalkProbe.DrawCounters.Names.Select((n, i) => $"{n.Replace(" Count", "").ToLowerInvariant()} {(MedL(full.counters[i]) < 0 ? "not reported" : WalkProbe.Big(MedL(full.counters[i])))}")));
            r.AppendLine("what each thing costs (frame time saved without it, median over the views; and what is drawn without it):");
            foreach (var c in configs.Skip(1).OrderBy(c => Med(c.ms)))
            {
                float saved = baseMs - Med(c.ms);
                r.AppendLine($"  {saved,6:0.0} ms ({saved / Mathf.Max(0.01f, baseMs):P0})  {c.name}  -> {Med(c.ms):0.0} ms | " +
                              string.Join(", ", new[] { 0, 1, 3, 5 }.Select(i => $"{WalkProbe.DrawCounters.Names[i].Replace(" Count", "").ToLowerInvariant()} {(MedL(c.counters[i]) < 0 ? "-" : WalkProbe.Big(MedL(c.counters[i])))}")));
            }
            // The slowest view on its own: an average hides the stretch of the walk where the 1% lows come from.
            int worst = Enumerable.Range(0, viewsDone).OrderByDescending(v => Slice(full.ms, v)).FirstOrDefault();
            if (viewsDone > 0)
            {
                float wb = Slice(full.ms, worst);
                r.AppendLine($"at the slowest view, {worst + 1} ({views[worst].pos.x:0}, {views[worst].pos.z:0}): {wb:0.0} ms with everything; saved without each:");
                foreach (var c in configs.Skip(1).OrderBy(c => Slice(c.ms, worst)).Take(12))
                    r.AppendLine($"  {wb - Slice(c.ms, worst),6:0.0} ms  {c.name}");
            }
            r.AppendLine($"rooms' furniture drawn from outside: {roomsOutsideDrawn} room-views of {roomsOutsideTotal} ({(float)roomsOutsideDrawn / Mathf.Max(1, viewsDone):0.0} rooms a view, of {rooms.Count} rooms)");
            r.AppendLine("groups in the scene: " + string.Join("; ", groups.Values.OrderByDescending(g => g.tris.Sum()).Select(g => $"{g.name} {g.rs.Count} renderers, {WalkProbe.Big(g.tris.Sum())} tris, {g.casters} cast shadows")));
            if (profiledFrames > 0)
            {
                r.AppendLine($"profiler samples, GPU ms a frame (top 30 of {gpuSum.Count}; nested samples overlap their parents):");
                foreach (var kv in gpuSum.OrderByDescending(kv => kv.Value).Take(30)) r.AppendLine($"  {kv.Value / profiledFrames,6:0.00}  {kv.Key}");
                r.AppendLine("profiler samples, CPU ms a frame (top 30):");
                foreach (var kv in cpuSum.OrderByDescending(kv => kv.Value).Take(30)) r.AppendLine($"  {kv.Value / profiledFrames,6:0.00}  {kv.Key}");
            }
            else r.AppendLine($"profiler samples: none readable ({recorders.Count} recorders; GPU and CPU per pass need a development build)");
            r.AppendLine($"objects with bounds over 10 km or not finite (Unity's \"Invalid worldAABB\"): {(huge.Count == 0 ? "none" : string.Join("; ", huge.Take(20)))}");
            r.AppendLine("what is drawn at each view (for the camera or a shadow), by group:");
            r.Append(census);
            r.AppendLine("per view, frame ms with everything on / without the sun's shadows / past 150 m: " +
                          string.Join("  ", Enumerable.Range(0, viewsDone).Select(v => $"{v + 1}: {Slice(full.ms, v):0} / {Slice(configs[1].ms, v):0} / {Slice(configs.First(c => c.name.StartsWith("nothing past")).ms, v):0}")));
            return r.ToString();
        }

        for (int v = 0; v < views.Count; v++)
        {
            var (pos, dir) = views[v];
            // Unpaced: the tier puts the player's vsync back when it is applied (the 2026-10-30 run read 17 ms, one refresh,
            // for everything), and a paced frame cannot show what anything costs.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            // Ten minutes of measuring in a level whose clock may be two and a half.
            if (level != null) level.HoldClockForBenchmark(120f);
            if (player == null) { census.AppendLine($"view {v + 1}: the player is gone (the level ended?); stopped here"); break; }
            if (cc) cc.enabled = false;
            float ground = Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : pos.y;
            player.transform.position = new Vector3(pos.x, ground + 0.1f, pos.z);
            if (cc) cc.enabled = true;
            for (int k = 0; k < 20; k++) { motor.TurnTowards(player.transform.position + dir * 20f + Vector3.up * 1.6f, 0.02f); if (health) health.Heal(1000f); yield return null; }

            // What is drawn here, everything on.
            var eye = cam.transform.position;
            census.Append($"view {v + 1} ({pos.x:0}, {pos.z:0}):");
            foreach (var g in groups.Values.OrderByDescending(g => g.tris.Sum()))
            {
                int n = 0; long t = 0;
                for (int i = 0; i < g.rs.Count; i++) if (g.rs[i] != null && g.rs[i].isVisible) { n++; t += g.tris[i]; }
                census.Append($" {g.name} {n}/{g.rs.Count} ({WalkProbe.Big(t)} tris);");
            }
            int inside = rooms.FindIndex(b => b.Contains(eye)), drawnRooms = 0;
            for (int i = 0; i < rooms.Count; i++)
                if (i != inside && roomFurniture[i].Any(r => r != null && r.isVisible)) drawnRooms++;
            roomsOutsideDrawn += drawnRooms; roomsOutsideTotal += rooms.Count - (inside >= 0 ? 1 : 0);
            census.AppendLine($" rooms with furniture drawn while the eye is outside them: {drawnRooms} of {rooms.Count - (inside >= 0 ? 1 : 0)}");

            foreach (var c in configs)
            {
                if (level != null) level.HoldClockForBenchmark(120f);
                // Things are destroyed in play (a prop shot apart): a config that throws must not end the run.
                try { c.on(); }
                catch (Exception e) { Debug.LogWarning($"[FrameCostProbe] {c.name}: {e.Message}"); }
                QualitySettings.vSyncCount = 0;
                for (int k = 0; k < Settle; k++) { if (health) health.Heal(1000f); yield return null; }
                for (int k = 0; k < Measure; k++)
                {
                    if (health) health.Heal(1000f);
                    yield return null;
                    c.ms.Add(Time.unscaledDeltaTime * 1000f);
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0) c.gpu.Add((float)timings[0].gpuFrameTime);
                    draws.Clear(); draws.Sample();
                    for (int i = 0; i < WalkProbe.DrawCounters.Names.Length; i++) if (draws.Values[i].Count > 0) c.counters[i].Add(draws.Values[i][0]);
                }
                try { c.off(); }
                catch (Exception e) { Debug.LogWarning($"[FrameCostProbe] {c.name} (undo): {e.Message}"); }
            }
            // The passes, in frames of their own: a recorder on every sample slows the frame it measures.
            if (recorders.Count > 0)
            {
                foreach (var (_, rec) in recorders) rec.enabled = true;
                for (int k = 0; k < Settle + Measure; k++)
                {
                    if (health) health.Heal(1000f);
                    yield return null;
                    if (k < Settle) continue;
                    profiledFrames++;
                    foreach (var (n, rec) in recorders)
                    {
                        if (rec.gpuElapsedNanoseconds > 0) gpuSum[n] = (gpuSum.TryGetValue(n, out var gv) ? gv : 0) + rec.gpuElapsedNanoseconds / 1e6;
                        if (rec.elapsedNanoseconds > 0) cpuSum[n] = (cpuSum.TryGetValue(n, out var cv) ? cv : 0) + rec.elapsedNanoseconds / 1e6;
                    }
                }
                foreach (var (_, rec) in recorders) rec.enabled = false;
            }
            try { Write(Report(v + 1), partial: true); }
            catch (Exception e) { Debug.LogWarning("[FrameCostProbe] report: " + e); }
        }
        MobileInput.Reset();

        // Anything whose bounds are not finite or span more than 10 km: what makes Unity log "Invalid worldAABB" in
        // development players and the editor (it names no object).
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include))
        {
            var b = r.bounds;
            bool finite = float.IsFinite(b.center.x + b.center.y + b.center.z + b.extents.x + b.extents.y + b.extents.z);
            if (!finite || b.extents.magnitude > 10000f || b.center.magnitude > 10000f)
                huge.Add($"{PathOf(r.transform)} ({r.GetType().Name}, bounds {b.center} +/- {b.extents})");
        }
        foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Include))
            if (!float.IsFinite(l.range) || !float.IsFinite(l.transform.position.x + l.transform.position.y + l.transform.position.z)) huge.Add($"{PathOf(l.transform)} (light range {l.range})");

        GameSettings.QualityTier = savedTier;
        try { Write(sb.ToString() + Report(views.Count)); }
        catch (Exception e) { Debug.LogWarning("[FrameCostProbe] report: " + e); }
        Application.Quit();
    }

    /// <summary>The active pipeline's renderer features that are on (a span cannot be walked inside the coroutine).</summary>
    static List<ScriptableRendererFeature> ActiveFeatures()
    {
        var features = new List<ScriptableRendererFeature>();
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            foreach (var d in urp.rendererDataList)
                if (d != null) foreach (var f in d.rendererFeatures) if (f != null && f.isActive && !features.Contains(f)) features.Add(f);
        return features;
    }

    static float Slice(List<float> ms, int view)
    {
        var part = ms.Skip(view * Measure).Take(Measure).OrderBy(x => x).ToArray();
        return part.Length > 0 ? part[part.Length / 2] : 0f;
    }

    /// <summary>The report; a partial one beside it (the bench script waits for the report itself to appear).</summary>
    void Write(string text, bool partial = false)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_out)));
        File.WriteAllText(partial ? _out + ".partial" : _out, text);
    }

    static long Tris(Renderer r)
    {
        Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
        if (m == null) return 0;
        long t = 0;
        for (int i = 0; i < m.subMeshCount; i++) t += m.GetIndexCount(i) / 3;
        return t;
    }

    static string Top(Transform t, Transform arena)
    {
        while (t.parent != null && t.parent != arena) t = t.parent;
        return t.parent == arena ? t.name : null;
    }

    static string PathOf(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }
}
