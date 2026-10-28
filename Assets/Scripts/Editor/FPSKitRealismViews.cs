#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>The realism audit's walk through the arena and its screen-importance pass (FPSKitRealismAudit).</summary>
    public static partial class FPSKitRealismAudit
    {
        /// <summary>One place and direction the player looks from.</summary>
        sealed class View
        {
            public Vector3 eye; public Quaternion look; public bool indoors; public string label;
        }

        static float RouteLength;
        static float _fov = 70f, _far = 1500f;

        /// <summary>
        /// Desert Outpost's walk (about 600 m, two minutes at a walk; Part 3 measures frame times along the same one): from the
        /// player's start down the avenue's east pavement, east along West Road, north up East 130, west along the boulevard to
        /// the clock square, on into the souk street. Each leg is a navmesh path (short legs: a long route's search gives up).
        /// </summary>
        static readonly Vector2[] DesertWalk =
        {
            new Vector2(11f, -124f), new Vector2(60f, -136.5f), new Vector2(124f, -136.5f), new Vector2(124.5f, -60f),
            new Vector2(124.5f, 12f), new Vector2(60f, 11.75f), new Vector2(14f, 4f), new Vector2(0f, -7f),
            new Vector2(-60f, 18f), new Vector2(-130f, 20f),
        };

        /// <summary>The walk's polyline on the navmesh, from the player's start.</summary>
        static List<Vector3> WalkLine(string theme)
        {
            var motor = Object.FindAnyObjectByType<PlayerMotor>();
            var cam = motor != null ? motor.GetComponentInChildren<Camera>(true) : null;
            if (cam != null) { _fov = cam.fieldOfView; _far = Mathf.Max(cam.farClipPlane, 600f); }
            Vector3 start = motor != null ? motor.transform.position : Vector3.zero;
            var points = new List<Vector3>();
            var waypoints = new List<Vector3> { start };
            if (theme == "Desert Outpost") waypoints.AddRange(DesertWalk.Select(w => new Vector3(w.x, start.y, w.y)));
            else
            {
                // Other arenas until they have their own walk: a seeded ring of navmesh points round the start.
                for (int i = 1; i <= 10; i++)
                {
                    float a = i * 36f * Mathf.Deg2Rad, r = 40f + 12f * (i % 3);
                    waypoints.Add(start + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                }
            }
            Vector3? last = null;
            var path = new NavMeshPath();
            foreach (var w in waypoints)
            {
                if (!NavMesh.SamplePosition(w, out var hit, 20f, NavMesh.AllAreas)) { Log($"walk: no navmesh near {w}"); continue; }
                var p = hit.position;
                if (last == null) { points.Add(p); last = p; continue; }
                if (NavMesh.CalculatePath(last.Value, p, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                    points.AddRange(path.corners.Skip(1));
                else { Log($"walk: no complete path {last.Value} -> {p} ({path.status}); straight"); points.Add(p); }
                last = p;
            }
            return points;
        }

        /// <summary>
        /// The views: every 5 m along the walk, looking ahead and 70 degrees to each side (the shop fronts and facades the
        /// player passes); then the rooms of the houses nearest the walk, looking across each room both ways.
        /// </summary>
        static List<View> Route(string theme)
        {
            var line = WalkLine(theme);
            var views = new List<View>();
            RouteLength = 0f;
            const float every = 5f, eye = 1.62f;
            float carry = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                Vector3 a = line[i - 1], b = line[i];
                float len = Vector3.Distance(a, b);
                if (len < 0.01f) continue;
                var dir = Vector3.ProjectOnPlane(b - a, Vector3.up).normalized;
                if (dir.sqrMagnitude < 0.5f) dir = Vector3.forward;
                for (float d = carry; d < len; d += every)
                {
                    var p = Vector3.Lerp(a, b, d / len) + Vector3.up * eye;
                    foreach (float yaw in new[] { 0f, -70f, 70f })
                        views.Add(new View { eye = p, look = Quaternion.LookRotation(Quaternion.Euler(0f, yaw, 0f) * dir) * Quaternion.Euler(4f, 0f, 0f), label = $"walk {RouteLength + d:0} m" });
                    carry = d + every - len;
                }
                RouteLength += len;
            }
            var walkPoints = views.Select(v => v.eye).ToList();
            var rooms = GameObject.Find("HouseRooms");
            if (rooms != null && walkPoints.Count > 0)
            {
                float Near(Transform h) => walkPoints.Min(p => (h.position - p).sqrMagnitude);
                var houses = rooms.transform.Cast<Transform>().OrderBy(Near).Take(4).ToList();
                foreach (var h in houses)
                    foreach (Transform m in h)
                    {
                        if (!m.name.StartsWith("Room ")) continue;
                        if (!NavMesh.SamplePosition(m.position, out var hit, 0.8f, NavMesh.AllAreas)) continue;
                        var p = hit.position + Vector3.up * eye;
                        Vector3 best = Vector3.forward; float far = 0f;
                        for (int k = 0; k < 8; k++)
                        {
                            var d = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
                            float reach = Physics.Raycast(p, d, out var rh, 15f, ~0, QueryTriggerInteraction.Ignore) ? rh.distance : 15f;
                            if (reach > far) { far = reach; best = d; }
                        }
                        views.Add(new View { eye = p, look = Quaternion.LookRotation(best) * Quaternion.Euler(6f, 0f, 0f), indoors = true, label = $"{h.name} {m.name}" });
                        views.Add(new View { eye = p, look = Quaternion.LookRotation(Quaternion.Euler(0f, 120f, 0f) * best) * Quaternion.Euler(6f, 0f, 0f), indoors = true, label = $"{h.name} {m.name}" });
                    }
            }
            return views;
        }

        // ------------------------------------------------------------------
        // Rendering helpers
        // ------------------------------------------------------------------
        static Camera _cam;

        static Camera AuditCamera(bool post)
        {
            if (_cam == null)
            {
                var go = new GameObject("RealismCamera") { hideFlags = HideFlags.DontSave };
                _cam = go.AddComponent<Camera>();
            }
            var data = _cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = post;
            data.renderShadows = true;
            data.antialiasing = post ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
            data.dithering = false;
            _cam.allowMSAA = false;
            _cam.nearClipPlane = 0.03f;
            _cam.farClipPlane = _far;
            _cam.fieldOfView = _fov;
            return _cam;
        }

        static void Render(Camera cam, RenderTexture rt, bool twice = false)
        {
            var req = new RenderPipeline.StandardRequest { destination = rt };
            RenderPipeline.SubmitRenderRequest(cam, req);
            if (twice) RenderPipeline.SubmitRenderRequest(cam, req);
        }

        static Color32[] Read32(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return px;
        }

        static Color[] ReadFloat(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            var px = tex.GetPixels();
            Object.DestroyImmediate(tex);
            return px;
        }

        static void SavePng(RenderTexture rt, string path, bool jpg = false)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            System.IO.File.WriteAllBytes(path, jpg ? tex.EncodeToJPG(90) : tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>An id as a colour: 32 levels a channel (32,768 ids), far enough apart that any smooth change on the way
        /// to the target (a colour-space conversion) still decodes, by the table <see cref="Calibrate"/> measured.</summary>
        static Vector4 IdColour(int id) => new Vector4((id & 31) / 31f, ((id >> 5) & 31) / 31f, ((id >> 10) & 31) / 31f, 1f);

        static readonly int[] _level = Enumerable.Range(0, 256).Select(b => Mathf.RoundToInt(b / 255f * 31f)).ToArray();

        static int IdOf(Color32 c) => _level[c.r] | (_level[c.g] << 5) | (_level[c.b] << 10);

        /// <summary>
        /// Draws the 32 levels of one channel side by side, far below the arena, and reads back what each became, so
        /// a pixel is decoded by the level whose result it is nearest (the first audit decoded ids straight from the bytes
        /// and handed the town's walls to its cushions).
        /// </summary>
        static void Calibrate()
        {
            var was = ExactPipeline();
            const int W = 320, H = 32;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            var cam = AuditCamera(false);
            var at = new Vector3(0f, -4000f, 0f);
            cam.transform.SetPositionAndRotation(at, Quaternion.identity);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.allowHDR = false;
            cam.orthographic = true; cam.orthographicSize = 0.5f;
            var quads = new List<GameObject>();
            try
            {
                for (int k = 0; k < 32; k++)
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    q.hideFlags = HideFlags.DontSave;
                    q.transform.position = at + new Vector3((k + 0.5f) / 32f * 10f - 5f, 0f, 2f);
                    q.transform.localScale = new Vector3(10f / 32f, 1f, 1f);
                    q.GetComponent<Renderer>().sharedMaterial = IdMaterial(k, null);   // level k in red
                    quads.Add(q);
                }
                Render(cam, rt);
                var px = Read32(rt);
                var got = new float[32];
                for (int k = 0; k < 32; k++) got[k] = px[(H / 2) * W + (int)((k + 0.5f) / 32f * W)].r;
                for (int b = 0; b < 256; b++)
                {
                    int best = 0;
                    for (int k = 1; k < 32; k++) if (Mathf.Abs(got[k] - b) < Mathf.Abs(got[best] - b)) best = k;
                    _level[b] = best;
                }
                Log($"id colours: levels 1, 16, 31 came back as {got[1]:0}, {got[16]:0}, {got[31]:0} (of 255; {(Mathf.Abs(got[16] - 16f / 31f * 255f) > 4f ? "converted on the way" : "as written")})");
            }
            finally
            {
                foreach (var q in quads) Object.DestroyImmediate(q);
                cam.orthographic = false;
                RestorePipeline(was); rt.Release(); Object.DestroyImmediate(rt);
            }
        }

        static Material IdMaterial(int id, Material like)
        {
            var m = new Material(Shader.Find("Hidden/FPSKit/RealismId")) { hideFlags = HideFlags.DontSave };
            m.SetVector("_Id", IdColour(id));
            if (like != null)
            {
                m.SetFloat("_Cull", Num(like, "_Cull", 2f));
                var tex = Tex(like, "_BaseMap") ?? Tex(like, "_MainTex");
                if (Num(like, "_AlphaClip", 0f) > 0.5f && tex != null)
                {
                    m.SetTexture("_MainTex", tex);
                    string st = like.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                    m.SetTextureScale("_MainTex", like.GetTextureScale(st));
                    m.SetTextureOffset("_MainTex", like.GetTextureOffset(st));
                    m.SetFloat("_Cutoff", Num(like, "_Cutoff", 0.5f));
                    m.SetFloat("_Clip", 1f);
                }
            }
            return m;
        }

        /// <summary>
        /// Every audited surface takes its object's id colour (a material per object and source material, so alpha-cut leaves
        /// keep their holes), everything else is switched off, and the terrain draws in its biggest layer's colour. The
        /// scene is reopened from disk after, so nothing here is kept.
        /// </summary>
        /// <summary>A material drawn behind everything (a weathering strip, glass): it does not hide what is behind it.</summary>
        static bool IsTransparent(Material m) => m != null && (Num(m, "_Surface", 0f) > 0.5f || m.renderQueue >= 2900);

        static Material _nothing, _depthOnly;

        /// <summary>A material that draws nothing (<paramref name="depth"/>: writes depth and no colour).</summary>
        static Material Nothing(bool depth, Material like = null)
        {
            ref Material m = ref depth ? ref _depthOnly : ref _nothing;
            if (m == null)
            {
                m = new Material(Shader.Find("Hidden/FPSKit/RealismDepthOnly")) { hideFlags = HideFlags.DontSave };
                m.SetFloat("_ZWrite", depth ? 1f : 0f);
            }
            return m;
        }

        /// <summary>
        /// Paints the scene for an id pass. <paramref name="clear"/> false: solid surfaces in their object's colour, transparent
        /// ones drawn not at all (a weathering strip over a wall's foot must not hide the wall). True: solid surfaces write depth
        /// only and the transparent ones take their colours, so glass and overlays are counted where nothing solid is in
        /// front of them.
        /// </summary>
        static void PaintIds(List<Item> items, Func<Item, bool> only = null, bool clear = false)
        {
            var byRenderer = new Dictionary<Renderer, Material[]>();
            var idMats = new Dictionary<(int, Material), Material>();
            foreach (var it in items)
                foreach (var m in it.members)
                {
                    if (m.renderer == null) continue;
                    if (!byRenderer.TryGetValue(m.renderer, out var arr))
                    {
                        arr = new Material[m.renderer.sharedMaterials.Length];
                        byRenderer[m.renderer] = arr;
                    }
                    int id = only == null || only(it) ? it.id : 0;
                    Material idm;
                    if (IsTransparent(m.mat) != clear) idm = Nothing(depth: clear);
                    else if (!idMats.TryGetValue((id, m.mat), out idm)) idMats[(id, m.mat)] = idm = IdMaterial(id, m.mat);
                    if (m.sub < arr.Length) arr[m.sub] = idm;
                }
            var black = IdMaterial(0, null);
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!byRenderer.TryGetValue(r, out var arr)) { r.enabled = false; continue; }
                    for (int i = 0; i < arr.Length; i++) if (arr[i] == null) arr[i] = black;
                    r.sharedMaterials = arr;
                }
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                var layerItem = items.Where(i => i.terrainName == t.name).OrderByDescending(i => i.layerShare).FirstOrDefault();
                int id = layerItem != null && (only == null || only(layerItem)) ? layerItem.id : 0;
                t.materialTemplate = clear ? Nothing(true) : IdMaterial(id, null);
                t.drawInstanced = false;   // instanced terrain needs a terrain shader to place its patches
                t.drawTreesAndFoliage = false;
            }
            foreach (var d in Object.FindObjectsByType<UnityEngine.Rendering.Universal.DecalProjector>(FindObjectsSortMode.None)) d.enabled = false;
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
        }

        /// <summary>The pipeline drawn at full size with no multisampling while ids are counted (a scaled or blended edge
        /// would mix two ids into a third); put back after.</summary>
        static (float, int) ExactPipeline()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) return (1f, 1);
            var was = (urp.renderScale, urp.msaaSampleCount);
            urp.renderScale = 1f; urp.msaaSampleCount = 1;
            return was;
        }

        static void RestorePipeline((float scale, int msaa) was)
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) { urp.renderScale = was.scale; urp.msaaSampleCount = was.msaa; }
        }

        /// <summary>
        /// Screen importance: every view drawn once in id colours at 480 x 270 and each object's pixels counted. An object's
        /// importance is its mean share of the screen over all views; it is near if the walk passes within 8 m of it (its
        /// renderers' bounds) or it ever fills a tenth of a view.
        /// </summary>
        static void Importance(List<Item> items, List<View> route)
        {
            var byId = items.ToDictionary(i => i.id);
            var maxShare = new Dictionary<Item, float>();
            var bestView = new Dictionary<Item, int>();
            Calibrate();
            var was = ExactPipeline();
            const int W = 480, H = 270;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            var cam = AuditCamera(false);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.allowHDR = false;
            long unknown = 0, sky = 0, total = 0;
            var counts = new Dictionary<int, int>();
            try
            {
                // Solid surfaces first (the sky is what none covers), then glass and overlays where nothing solid hides them.
                for (int pass = 0; pass < 2; pass++)
                {
                PaintIds(items, clear: pass == 1);
                for (int v = 0; v < route.Count; v++)
                {
                    cam.transform.SetPositionAndRotation(route[v].eye, route[v].look);
                    Render(cam, rt);
                    var px = Read32(rt);
                    counts.Clear();
                    foreach (var c in px) { int id = IdOf(c); counts[id] = counts.TryGetValue(id, out var n) ? n + 1 : 1; }
                    foreach (var kv in counts)
                    {
                        if (pass == 0) total += kv.Value;
                        if (kv.Key == 0) { if (pass == 0) sky += kv.Value; continue; }
                        if (!byId.TryGetValue(kv.Key, out var it)) { unknown += kv.Value; continue; }
                        float share = kv.Value / (float)(W * H);
                        it.pixels += share;
                        if (kv.Value >= 20) it.views++;
                        if (!maxShare.TryGetValue(it, out var ms) || share > ms) { maxShare[it] = share; bestView[it] = v; }
                        it.viewShares.Add((share, v));
                    }
                }
                }
            }
            finally { RestorePipeline(was); rt.Release(); Object.DestroyImmediate(rt); }
            Log($"id pass: {route.Count} views, {(double)sky / Math.Max(1, total):P1} sky or unpainted, {(double)unknown / Math.Max(1, total):P2} unreadable (edges)");

            // The sky's share is everything nothing else covered.
            foreach (var it in items.Where(i => i.skyMaterial != null)) { it.pixels = sky / (double)(W * H); it.views = route.Count; }
            // Terrain: the painted layer's pixels are the ground's; share them out by the layers' weights.
            var ground = items.Where(i => i.terrainLayer != null).ToList();
            double groundPixels = ground.Sum(i => i.pixels);
            int groundViews = ground.Count > 0 ? ground.Max(i => i.views) : 0;
            foreach (var it in ground) { it.pixels = groundPixels * it.layerShare; it.views = groundViews; }

            // Near: within 8 m of the walk (a small renderer's bounds, a merged chunk's sampled triangles), or a tenth of some view.
            var eyes = route.Select(v => v.eye).ToList();
            foreach (var it in items)
            {
                foreach (var m in it.members)
                {
                    var r = m.renderer;
                    if (r == null) continue;
                    var b = r.bounds;
                    if (b.size.magnitude < 20f)
                    {
                        foreach (var e in eyes) { float d = Vector3.Distance(b.ClosestPoint(e), e); if (d < it.minDist) it.minDist = d; }
                        continue;
                    }
                    // A merged chunk's bounds hold half a street: its sampled triangles say where its surface is.
                    var subs = Geometry(m.mesh);
                    if (m.sub >= subs.Length) continue;
                    foreach (var (c, n, area) in subs[m.sub].samples)
                    {
                        var p = m.toWorld.MultiplyPoint3x4(c);
                        foreach (var e in eyes) { float d = Vector3.Distance(p, e); if (d < it.minDist) it.minDist = d; }
                    }
                }
                if (maxShare.TryGetValue(it, out var most) && most >= 0.1f) it.minDist = Mathf.Min(it.minDist, 1.5f);
                if (bestView.TryGetValue(it, out var bv)) it.bestView = bv;
                // Keep the five views that saw most of it (the close-up looks for a spot near each in turn).
                it.viewShares.Sort((a, b) => b.share.CompareTo(a.share));
                if (it.viewShares.Count > 5) it.viewShares.RemoveRange(5, it.viewShares.Count - 5);
                if (it.terrainLayer != null) it.minDist = 1.6f;
                it.importance = 100.0 * it.pixels / Math.Max(1, route.Count);
            }
            GunImportance(items);
        }

        /// <summary>
        /// The guns' share of the screen, from each first-person model drawn in its rest pose at the eye with the world's
        /// field of view (the game lays it on the arms camera with its own; close enough to rank it). Shown only while
        /// carried, which for the starting rifle is all the time.
        /// </summary>
        static void GunImportance(List<Item> items)
        {
            var guns = items.Where(i => i.source == "player weapon").ToList();
            if (guns.Count == 0) return;
            var was = ExactPipeline();
            const int W = 480, H = 270;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            var cam = AuditCamera(false);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var at = new Vector3(0f, -3000f, 0f);
            cam.transform.SetPositionAndRotation(at, Quaternion.identity);
            try
            {
                foreach (var data in AssetDatabase.FindAssets("t:WeaponData").Select(g => AssetDatabase.LoadAssetAtPath<WeaponData>(AssetDatabase.GUIDToAssetPath(g))))
                {
                    if (data == null || data.viewModel == null) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(data.viewModel);
                    go.transform.SetPositionAndRotation(at, Quaternion.identity);
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    {
                        var gun = guns.FirstOrDefault(g => g.renderers.Any(pr => pr != null && pr.name == r.name && g.weapons.Contains(data.name)));
                        var mats = r.sharedMaterials;
                        for (int i = 0; i < mats.Length; i++) mats[i] = IdMaterial(gun != null ? gun.id : 0, mats[i]);
                        r.sharedMaterials = mats;
                        r.gameObject.layer = 0;
                    }
                    Render(cam, rt);
                    var px = Read32(rt);
                    var counts = new Dictionary<int, int>();
                    foreach (var c in px) { int id = IdOf(c); counts[id] = counts.TryGetValue(id, out var n) ? n + 1 : 1; }
                    foreach (var g in guns)
                        if (counts.TryGetValue(g.id, out var n))
                        {
                            double share = 100.0 * n / (W * H);
                            g.importance = Math.Max(g.importance, share);
                            g.measured.Add($"{share:0.0}% of the screen with the {data.name} in hand (rest pose)");
                        }
                    Object.DestroyImmediate(go);
                }
            }
            finally { RestorePipeline(was); rt.Release(); Object.DestroyImmediate(rt); }
            // The arms are posed on the gun by IK at run time, so their share is not measured: about a gun's.
            var gunShares = guns.Where(g => g.importance > 0).Select(g => g.importance).ToList();
            foreach (var g in guns.Where(g => g.prefabAsset != null && AssetDatabase.GetAssetPath(g.prefabAsset) == ArmsPrefab))
            {
                g.importance = gunShares.Count > 0 ? gunShares.Average() : 0;
                g.measured.Add($"about {g.importance:0.0}% of the screen (not measured: posed on the gun at run time)");
            }
            foreach (var g in guns) { g.minDist = 0.5f; g.views = 1; }
        }
    }
}
#endif
