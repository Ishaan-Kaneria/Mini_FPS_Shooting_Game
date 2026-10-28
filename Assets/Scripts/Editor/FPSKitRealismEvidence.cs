#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The realism audit's visual evidence (FPSKitRealismAudit), for every object it shows:
    ///
    ///   * close.png: 1920 x 1080 from 1-2 m, in place in the arena, as the game draws it at the audited tier.
    ///   * wire.png: the same view darkened with every triangle within 5 m drawn over it (the object's own in yellow), hidden
    ///     lines removed against a depth pass of the same triangles (Unity's wireframe switch draws nothing under URP).
    ///   * texel.png: the same view with every surface coloured by texels per metre (Hidden/FPSKit/RealismTexelDensity).
    ///   * albedo_check.png: URP's Rendering Debugger albedo validation (red below 0.01, blue above 0.90), and the share of
    ///     the object's own pixels it flags.
    ///   * light_*.jpg and light_test.jpg: the object (the patch of it round the close-up's spot, or the whole model) on a
    ///     stage in an empty scene under five lights -- midday sun, low evening sun, shade, a room lit only through its
    ///     doorway, night under one lamp -- beside an 18% grey, a white (80%) and a black (4%) card. Measured from the
    ///     unprocessed HDR render: the object's brightness against the grey card in each (a real surface keeps roughly
    ///     the same ratio in every light; one that rises in the dark is emitting), how much it varies across itself under
    ///     the raking evening sun (relief), and its shade and indoor levels against midday.
    /// </summary>
    public static partial class FPSKitRealismAudit
    {
        const int CloseW = 1920, CloseH = 1080;

        sealed class Pose
        {
            public Vector3 cam, target, normal;
            public Quaternion rot;
            public Member member;           // the surface the spot is on
            public Renderer instance;       // an imported model's instance
            public bool floor, ceiling;
            public string note;
        }

        static void MakeEvidence(List<Item> chosen, List<View> route)
        {
            var poses = new Dictionary<Item, Pose>();
            foreach (var it in chosen)
            {
                it.evidence = $"{_out}/{Safe(it.name)}";
                Directory.CreateDirectory(it.evidence);
                var pose = FindPose(it, route);
                if (pose == null) { it.measured.Add("no clear spot for a close-up found near the walk"); continue; }
                poses[it] = pose;
            }
            Log($"close-up spots for {poses.Count} of {chosen.Count}");

            // ---- 1. the object as the game draws it, and its wireframe ----
            var validation = new Dictionary<Item, Color32[]>();
            var patches = new Dictionary<Item, Patch>();
            var sun = RenderSettings.sun != null ? RenderSettings.sun : Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            var sunLook = sun != null ? (sun.intensity, sun.color, sun.useColorTemperature ? sun.colorTemperature : 6500f) : (2f, Color.white, 6500f);
            var profile = Object.FindObjectsByType<Volume>(FindObjectsSortMode.None).Where(v => v.isGlobal && v.sharedProfile != null).Select(v => v.sharedProfile).FirstOrDefault();
            var rt = new RenderTexture(CloseW, CloseH, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var debug = UniversalRenderPipelineDebugDisplaySettings.Instance.materialSettings;
            try
            {
                foreach (var kv in poses)
                {
                    var (it, pose) = (kv.Key, kv.Value);
                    using var gun = GunInScene(it, pose);
                    var cam = Aim(AuditCamera(true), pose);
                    cam.clearFlags = CameraClearFlags.Skybox;
                    cam.allowHDR = true;
                    Render(cam, rt, twice: true);
                    SavePng(rt, $"{it.evidence}/close.png");
                    DrawWire(rt, cam, it, pose, gun.renderers);
                    SavePng(rt, $"{it.evidence}/wire.png");
                }

                // ---- 2. the stage patches, cut from the saved surfaces before anything is repainted ----
                foreach (var kv in poses) { var p = CutPatch(kv.Key, kv.Value); if (p != null) patches[kv.Key] = p; }

                // ---- 3. URP's albedo validation, transparent overlays left out (the debug view draws them solid) ----
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if ((r is MeshRenderer || r is SkinnedMeshRenderer) && r.sharedMaterials.Any(IsTransparent))
                        r.sharedMaterials = r.sharedMaterials.Select(m => IsTransparent(m) ? Nothing(false) : m).ToArray();
                debug.materialValidationMode = DebugMaterialValidationMode.Albedo;
                try
                {
                    foreach (var kv in poses)
                    {
                        var (it, pose) = (kv.Key, kv.Value);
                        using var gun = GunInScene(it, pose);
                        Render(Aim(AuditCamera(false), pose), rt);
                        SavePng(rt, $"{it.evidence}/albedo_check.png");
                        validation[it] = Read32(rt);
                    }
                }
                finally { debug.materialValidationMode = DebugMaterialValidationMode.None; }
            }
            finally { rt.Release(); Object.DestroyImmediate(rt); }

            // ---- 4. the object's own pixels in each close-up (ids), and the share URP's check flags ----
            var mask = new Dictionary<Item, bool[]>();
            var was = ExactPipeline();
            rt = new RenderTexture(CloseW, CloseH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            // Glass and overlays are found in the transparent pass, everything else in the solid one.
            bool Clear(Item i) => i.mats.Count > 0 && i.mats.All(m => m.mat != null && IsTransparent(m.mat));
            try
            {
                var order = poses.Where(p => !Clear(p.Key)).Concat(poses.Where(p => Clear(p.Key))).ToList();
                bool? painted = null;
                foreach (var kv in order)
                {
                    var (it, pose) = (kv.Key, kv.Value);
                    if (painted != Clear(it)) { PaintIds(ArenaItems(), only: null, clear: Clear(it)); painted = Clear(it); }
                    using var gun = GunInScene(it, pose, ids: true);
                    var cam = Aim(AuditCamera(false), pose);
                    cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.allowHDR = false;
                    Render(cam, rt);
                    var px = Read32(rt);
                    var own = new bool[px.Length];
                    int n = 0;
                    for (int i = 0; i < px.Length; i++) if (IdOf(px[i]) == it.id) { own[i] = true; n++; }
                    mask[it] = own;
                    it.measured.Add($"fills {(double)n / px.Length:P0} of its close-up");
                    if (validation.TryGetValue(it, out var vpx) && n > 0)
                    {
                        int red = 0, blue = 0;
                        for (int i = 0; i < px.Length; i++)
                        {
                            if (!own[i]) continue;
                            var c = vpx[i];
                            if (c.r > 230 && c.g < 40 && c.b < 40) red++;
                            else if (c.b > 230 && c.r < 40 && c.g < 40) blue++;
                        }
                        it.measured.Add($"URP albedo validation flags {(double)(red + blue) / n:P1} of its pixels ({(double)red / n:P1} too dark, {(double)blue / n:P1} too bright)");
                        if ((red + blue) / (double)n > 0.10) it.measured.Add($"!URP's albedo validation flags {(double)(red + blue) / n:P0} of it ({(red > blue ? "darker" : "brighter")} than real materials)");
                    }
                }
            }
            finally { RestorePipeline(was); rt.Release(); Object.DestroyImmediate(rt); }

            // ---- 5. texel density ----
            EditorSceneManager.OpenScene(_scenePath, OpenSceneMode.Single);
            SetTier();
            Relink(ArenaItems());
            PaintTexel(ArenaItems());
            rt = new RenderTexture(CloseW, CloseH, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                foreach (var kv in poses)
                {
                    var (it, pose) = (kv.Key, kv.Value);
                    using var gun = GunInScene(it, pose, texel: true);
                    var cam = Aim(AuditCamera(false), pose);
                    cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
                    Render(cam, rt);
                    SavePng(rt, $"{it.evidence}/texel.png");
                    if (mask.TryGetValue(it, out var own))
                    {
                        var px = Read32(rt);
                        // The texel shader's colour back to a density: the ramp's hue. Reported as the share under 256 px/m.
                        int n = 0, low = 0;
                        for (int i = 0; i < px.Length; i++) { if (!own[i]) continue; n++; var c = px[i]; if (c.r > c.g + 20 && c.r > c.b + 20 && c.g < 200) low++; }
                        if (n > 0) it.measured.Add($"{(double)low / n:P0} of it under about 256 texels per metre in the close-up (red and orange in texel.png)");
                    }
                }
            }
            finally { rt.Release(); Object.DestroyImmediate(rt); }

            // ---- 6. the five-light test on a stage ----
            Stage(patches, sunLook, profile);
            foreach (var p in patches.Values) p.Dispose();
        }

        static IEnumerable<Item> _all;
        static List<Item> ArenaItems() => _all.ToList();
        static string _scenePath;

        static void SetTier()
        {
            int tier = Array.IndexOf(QualitySettings.names, "Medium");
            if (tier >= 0) QualitySettings.SetQualityLevel(tier, true);
        }

        static string Safe(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars().Concat(new[] { ' ', ':', '(', ')', ',', '/', '\\' })) s = s.Replace(c, '_');
            while (s.Contains("__")) s = s.Replace("__", "_");
            return s.Trim('_');
        }

        static Camera Aim(Camera cam, Pose p)
        {
            cam.transform.SetPositionAndRotation(p.cam, p.rot);
            cam.fieldOfView = 60f;
            return cam;
        }

        // ------------------------------------------------------------------
        // Choosing the close-up
        // ------------------------------------------------------------------
        /// <summary>
        /// Where to stand for the close-up: on the object's surface nearest the view of the walk that saw most of it, the
        /// point closest to that eye (on a wall, eye height), and 1.5 m out along its normal (a floor from above and in front);
        /// clear of anything solid, with a clear line to the spot. An imported model is framed whole from up to 2 m; a
        /// terrain layer where it is most of the ground; the sky looking up; a gun as it is held.
        /// </summary>
        static Pose FindPose(Item it, List<View> route)
        {
            // Near each of the views that saw most of it in turn, until a clear spot is found.
            var views = it.viewShares.Select(v => v.view).Where(v => v >= 0 && v < route.Count).DefaultIfEmpty(0).ToList();
            foreach (var v in views)
            {
                var pose = FindPose(it, route, route[v]);
                if (pose != null) return pose;
            }
            return null;
        }

        static Pose FindPose(Item it, List<View> route, View view)
        {
            Vector3 eye = view.eye;
            if (it.skyMaterial != null)
                return new Pose { cam = eye, target = eye + view.look * Vector3.forward * 10f, rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(view.look * Vector3.forward, Vector3.up)) * Quaternion.Euler(-25f, 0f, 0f), note = "looking up from the walk" };
            if (it.source == "player weapon")
            {
                // Side-on, 0.7 m away, in the street's light at the start of the walk.
                var rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(route[0].look * Vector3.forward, Vector3.up));
                return new Pose { cam = route[0].eye, rot = rot, target = route[0].eye + rot * Vector3.forward * 0.7f, normal = rot * Vector3.back, note = "side-on in the street at the start" };
            }
            if (it.terrainLayer != null) return TerrainPose(it, eye);
            if (it.source.StartsWith("imported") || it.source == "generated prefab") return ModelPose(it, eye);
            return SurfacePose(it, eye);
        }

        static bool Clear(Vector3 cam, Vector3 spot, Vector3 n) =>
            !Physics.CheckSphere(cam, 0.12f, ~0, QueryTriggerInteraction.Ignore) &&
            !Physics.Linecast(cam, spot + n * 0.03f, ~0, QueryTriggerInteraction.Ignore);

        static Pose SurfacePose(Item it, Vector3 eye)
        {
            var spots = new List<(float score, Vector3 p, Vector3 n, Member m)>();
            foreach (var m in it.members)
            {
                if (m.renderer == null || m.lod != 0) continue;
                if (Vector3.Distance(m.renderer.bounds.ClosestPoint(eye), eye) > 30f) continue;
                using var arr = Mesh.AcquireReadOnlyMeshData(m.mesh);
                var md = arr[0];
                if (m.sub >= md.subMeshCount) continue;
                var pos = new NativeArray<Vector3>(md.vertexCount, Allocator.Persistent);
                md.GetVertices(pos);
                var desc = md.GetSubMesh(m.sub);
                var idx = new NativeArray<int>(desc.indexCount, Allocator.Persistent);
                md.GetIndices(idx, m.sub, true);
                try
                {
                    for (int t = 0; t + 2 < idx.Length; t += 3)
                    {
                        Vector3 a = m.toWorld.MultiplyPoint3x4(pos[idx[t]]), b = m.toWorld.MultiplyPoint3x4(pos[idx[t + 1]]), c = m.toWorld.MultiplyPoint3x4(pos[idx[t + 2]]);
                        var cross = Vector3.Cross(b - a, c - a);
                        float area = cross.magnitude * 0.5f;
                        if (area < 0.03f) continue;
                        var n = cross.normalized;
                        var p = ClosestOnTriangle(eye, a, b, c);
                        float d = Vector3.Distance(p, eye);
                        if (d > 25f || Vector3.Dot(n, (eye - p).normalized) < 0.25f) continue;
                        spots.Add((Mathf.Abs(d - 4f) - Mathf.Min(area, 4f) * 0.5f, p, n, m));
                    }
                }
                finally { pos.Dispose(); idx.Dispose(); }
            }
            foreach (var s in spots.OrderBy(s => s.score).Take(60))
            {
                bool floor = s.n.y > 0.7f, ceiling = s.n.y < -0.7f;
                var toEye = Vector3.ProjectOnPlane(eye - s.p, Vector3.up);
                toEye = toEye.sqrMagnitude > 1e-4f ? toEye.normalized : Vector3.forward;
                Vector3 cam = floor ? s.p + Vector3.up * 1.3f + toEye * 0.9f
                            : ceiling ? s.p - Vector3.up * 1.3f + toEye * 0.9f
                            : s.p + s.n * 1.5f;
                if (!Clear(cam, s.p, s.n)) continue;
                return new Pose { cam = cam, target = s.p, normal = s.n, rot = Quaternion.LookRotation(s.p - cam), member = s.m, floor = floor, ceiling = ceiling };
            }
            return null;
        }

        static Pose ModelPose(Item it, Vector3 eye)
        {
            foreach (var r in it.renderers.Where(r => r != null).OrderBy(r => Vector3.Distance(r.bounds.ClosestPoint(eye), eye)).Take(12))
            {
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(r.gameObject);
                var bounds = r.bounds;
                if (root != null) foreach (var rr in root.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(rr.bounds);
                var dir = Vector3.ProjectOnPlane(eye - bounds.center, Vector3.up);
                dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
                float dist = Mathf.Clamp(bounds.extents.magnitude * 1.6f, 1.0f, 2.0f) + Vector3.ProjectOnPlane(bounds.extents, Vector3.up).magnitude * 0.5f;
                foreach (float turn in new[] { 0f, 35f, -35f, 70f, -70f })
                {
                    var d = Quaternion.Euler(0f, turn, 0f) * dir;
                    var cam = bounds.center + d * dist + Vector3.up * Mathf.Min(0.4f, bounds.extents.y);
                    if (Physics.CheckSphere(cam, 0.12f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (Physics.Linecast(cam, bounds.center, out var hit, ~0, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(root != null ? root.transform : r.transform)) continue;
                    var member = it.members.FirstOrDefault(m => m.renderer == r);
                    return new Pose { cam = cam, target = bounds.center, normal = d, rot = Quaternion.LookRotation(bounds.center - cam), member = member, instance = r };
                }
            }
            return null;
        }

        static Pose TerrainPose(Item it, Vector3 eye)
        {
            var t = it.terrain;
            if (t == null) return null;
            var data = t.terrainData;
            int li = Array.IndexOf(data.terrainLayers, it.terrainLayer);
            Vector3 best = eye; float bestW = -1f;
            for (int ring = 0; ring <= 8; ring++)
                for (int k = 0; k < 12; k++)
                {
                    var p = eye + Quaternion.Euler(0f, k * 30f, 0f) * Vector3.forward * (2f + ring * 4f);
                    var local = p - t.transform.position;
                    int ax = Mathf.Clamp((int)(local.x / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
                    int az = Mathf.Clamp((int)(local.z / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
                    float w = data.GetAlphamaps(ax, az, 1, 1)[0, 0, li];
                    p.y = t.SampleHeight(p) + t.transform.position.y;
                    // Only where the terrain is the surface (the town's roads and aprons lie over it).
                    bool onTerrain = Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore) && hit.collider is TerrainCollider;
                    if (onTerrain && w > bestW + 0.05f && !Physics.CheckSphere(p + Vector3.up * 1.3f, 0.15f, ~0, QueryTriggerInteraction.Ignore)) { bestW = w; best = p; }
                }
            var toEye = Vector3.ProjectOnPlane(eye - best, Vector3.up);
            toEye = toEye.sqrMagnitude > 1e-3f ? toEye.normalized : Vector3.back;
            var cam = best + Vector3.up * 1.3f + toEye * 0.9f;
            it.measured.Add($"close-up where the layer is {bestW:P0} of the ground");
            return new Pose { cam = cam, target = best, normal = Vector3.up, rot = Quaternion.LookRotation(best - cam), floor = true };
        }

        static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            // Ericson, Real-Time Collision Detection 5.1.5.
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        // ------------------------------------------------------------------
        // A gun, put in the scene for its shots
        // ------------------------------------------------------------------
        sealed class GunShot : IDisposable
        {
            public GameObject go;
            public readonly List<Renderer> renderers = new List<Renderer>();
            public void Dispose() { if (go != null) Object.DestroyImmediate(go); }
        }

        static GunShot GunInScene(Item it, Pose pose, bool ids = false, bool texel = false)
        {
            var shot = new GunShot();
            if (it.source != "player weapon") return shot;
            if (it.prefabAsset == null) return shot;
            shot.go = SideOn(it.prefabAsset, pose.target, pose.rot);
            foreach (var r in shot.go.GetComponentsInChildren<Renderer>(true))
            {
                shot.renderers.Add(r);
                r.gameObject.layer = 0;
                const bool mine = true;   // a gun's model is all gun, the arms' all arms
                if (ids)
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = IdMaterial(mine ? it.id : 0, mats[i]);
                    r.sharedMaterials = mats;
                }
                else if (texel) r.sharedMaterials = r.sharedMaterials.Select(TexelMaterial).ToArray();
            }
            return shot;
        }

        /// <summary>A first-person model stood side-on to <paramref name="view"/> (its barrel, +Z, to the right), its middle at
        /// <paramref name="centre"/>.</summary>
        static GameObject SideOn(GameObject prefab, Vector3 centre, Quaternion view)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetPositionAndRotation(Vector3.zero, view * Quaternion.Euler(0f, 90f, 0f));
            Bounds? b = null;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { if (b == null) b = r.bounds; else { var bb = b.Value; bb.Encapsulate(r.bounds); b = bb; } }
            go.transform.position = centre - (b?.center ?? Vector3.zero);
            return go;
        }

        // ------------------------------------------------------------------
        // Wireframe
        // ------------------------------------------------------------------
        static Material _wireMat;

        /// <summary>
        /// Over the rendered close-up: darkened, then every triangle of every renderer within 5 m of the camera drawn into
        /// depth only, then their edges with the depth test (the object's own yellow, the rest pale blue), so hidden lines stay
        /// hidden.
        /// </summary>
        static void DrawWire(RenderTexture rt, Camera cam, Item it, Pose pose, List<Renderer> extra)
        {
            if (_wireMat == null)
            {
                _wireMat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.DontSave };
                _wireMat.SetInt("_Cull", (int)CullMode.Off);
            }
            var renderers = new List<Renderer>(extra);
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer) && Vector3.Distance(r.bounds.ClosestPoint(pose.cam), pose.cam) < 5f) renderers.Add(r);
            var own = new HashSet<(Renderer, int)>(it.members.Where(m => m.renderer != null).Select(m => (m.renderer, m.sub)));
            var tris = new List<(Vector3 a, Vector3 b, Vector3 c, bool mine)>();
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            foreach (var r in renderers.Distinct())
            {
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                bool gunPart = extra.Contains(r);
                bool gunMine = gunPart && it.renderers.Any(pr => pr != null && pr.name == r.name);
                var m = r.localToWorldMatrix;
                using var arr = Mesh.AcquireReadOnlyMeshData(mesh);
                var md = arr[0];
                var pos = new NativeArray<Vector3>(md.vertexCount, Allocator.Persistent);
                md.GetVertices(pos);
                try
                {
                    for (int s = 0; s < md.subMeshCount; s++)
                    {
                        var desc = md.GetSubMesh(s);
                        if (desc.topology != MeshTopology.Triangles) continue;
                        var idx = new NativeArray<int>(desc.indexCount, Allocator.Persistent);
                        md.GetIndices(idx, s, true);
                        bool mine = gunPart ? gunMine : own.Contains((r, s));
                        for (int t = 0; t + 2 < idx.Length; t += 3)
                        {
                            Vector3 a = m.MultiplyPoint3x4(pos[idx[t]]), b = m.MultiplyPoint3x4(pos[idx[t + 1]]), c = m.MultiplyPoint3x4(pos[idx[t + 2]]);
                            var centre = (a + b + c) / 3f;
                            if ((centre - pose.cam).sqrMagnitude > 36f && (a - pose.cam).sqrMagnitude > 36f) continue;
                            var tb = new Bounds(a, Vector3.zero); tb.Encapsulate(b); tb.Encapsulate(c);
                            if (!GeometryUtility.TestPlanesAABB(planes, tb)) continue;
                            tris.Add((a, b, c, mine));
                        }
                        idx.Dispose();
                    }
                }
                finally { pos.Dispose(); }
            }

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.PushMatrix();
            // Darken the picture so the lines read.
            GL.LoadOrtho();
            _wireMat.SetInt("_ZTest", (int)CompareFunction.Always);
            _wireMat.SetInt("_ZWrite", 0);
            _wireMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _wireMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _wireMat.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(new Color(0f, 0f, 0f, 0.55f));
            GL.Vertex3(0, 0, 0); GL.Vertex3(1, 0, 0); GL.Vertex3(1, 1, 0); GL.Vertex3(0, 1, 0);
            GL.End();
            GL.Clear(true, false, Color.black);

            GL.LoadProjectionMatrix(GL.GetGPUProjectionMatrix(cam.projectionMatrix, true));
            GL.modelview = cam.worldToCameraMatrix;
            // Depth only.
            _wireMat.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            _wireMat.SetInt("_ZWrite", 1);
            _wireMat.SetInt("_SrcBlend", (int)BlendMode.Zero);
            _wireMat.SetInt("_DstBlend", (int)BlendMode.One);
            _wireMat.SetPass(0);
            GL.Begin(GL.TRIANGLES);
            foreach (var t in tris) { GL.Vertex(t.a); GL.Vertex(t.b); GL.Vertex(t.c); }
            GL.End();
            // Edges, pulled a little towards the eye so they win against their own faces.
            _wireMat.SetInt("_ZWrite", 0);
            _wireMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _wireMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _wireMat.SetPass(0);
            GL.Begin(GL.LINES);
            Vector3 Pull(Vector3 p) => p + (pose.cam - p) * 0.004f;
            foreach (var t in tris.OrderBy(t => t.mine))
            {
                GL.Color(t.mine ? new Color(1f, 0.85f, 0.1f, 1f) : new Color(0.55f, 0.8f, 1f, 0.75f));
                Vector3 a = Pull(t.a), b = Pull(t.b), c = Pull(t.c);
                GL.Vertex(a); GL.Vertex(b); GL.Vertex(b); GL.Vertex(c); GL.Vertex(c); GL.Vertex(a);
            }
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = prev;
            it.measured.Add($"wireframe: {tris.Count(t => t.mine)} of its triangles in the close-up, {tris.Count} within 5 m");
        }

        // ------------------------------------------------------------------
        // Texel density materials
        // ------------------------------------------------------------------
        static readonly Dictionary<Material, Material> _texelMats = new Dictionary<Material, Material>();

        static Material TexelMaterial(Material like)
        {
            if (like == null) return null;
            if (IsTransparent(like)) return Nothing(false);   // the wall under a weathering strip is what is measured
            if (_texelMats.TryGetValue(like, out var m)) return m;
            m = new Material(Shader.Find("Hidden/FPSKit/RealismTexelDensity")) { hideFlags = HideFlags.DontSave };
            var tex = Tex(like, "_BaseMap") ?? Tex(like, "_MainTex");
            string st = like.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            m.SetVector("_TexSize", tex != null ? new Vector4(tex.width, tex.height, 0, 0) : new Vector4(1, 1, 0, 0));
            if (like.HasProperty(st)) { var s = like.GetTextureScale(st); var o = like.GetTextureOffset(st); m.SetVector("_ST", new Vector4(s.x, s.y, o.x, o.y)); }
            m.SetFloat("_Cull", Num(like, "_Cull", 2f));
            _texelMats[like] = m;
            return m;
        }

        static void PaintTexel(List<Item> items)
        {
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    if (r is MeshRenderer || r is SkinnedMeshRenderer) r.sharedMaterials = r.sharedMaterials.Select(TexelMaterial).ToArray();
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                var data = t.terrainData;
                var layer = items.Where(i => i.terrainName == t.name).OrderByDescending(i => i.layerShare).Select(i => i.terrainLayer).FirstOrDefault();
                var m = new Material(Shader.Find("Hidden/FPSKit/RealismTexelDensity")) { hideFlags = HideFlags.DontSave };
                if (layer != null && layer.diffuseTexture != null)
                {
                    // The terrain's UV runs 0-1 over its whole size; the layer's texture repeats every tileSize metres.
                    float rx = data.size.x / Mathf.Max(0.01f, layer.tileSize.x), rz = data.size.z / Mathf.Max(0.01f, layer.tileSize.y);
                    m.SetVector("_TexSize", new Vector4(layer.diffuseTexture.width * rx, layer.diffuseTexture.height * rz, 0, 0));
                }
                t.materialTemplate = m;
                t.drawInstanced = false;
                t.drawTreesAndFoliage = false;
            }
            RenderSettings.fog = false;
        }
    }
}
#endif
