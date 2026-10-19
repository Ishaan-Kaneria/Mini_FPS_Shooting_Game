#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// What is under a pixel of a view picture: opens a scene, makes the view's camera (position, look-at, vertical field of
    /// view, picture size) and lists every mesh the ray through the pixel crosses, nearest first, with its path in the
    /// hierarchy. For "what is that thing in the screenshot" without opening the editor.
    ///   Tools/unity-batch.sh FPSKit.EditorTools.FPSKitPixelProbe.Run -probeScene Assets/.../X.unity
    ///       -probeFrom x,y,z -probeLook x,y,z -probeFov 62 -probeSize 1024x576 -probePixels 300,160;450,85
    /// </summary>
    public static class FPSKitPixelProbe
    {
        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        /// <summary>"x,y,z", or "@Name:x,y,z" for an offset from the centre of the first renderer under an object of that name.</summary>
        static Vector3 V(string s)
        {
            Vector3 at = Vector3.zero;
            if (s.StartsWith("@"))
            {
                int c = s.IndexOf(':');
                string name = s.Substring(1, c - 1);
                int nth = 0;
                if (name.Contains("#")) { nth = int.Parse(name.Substring(name.IndexOf('#') + 1)) - 1; name = name.Substring(0, name.IndexOf('#')); }
                var go = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.InstanceID).Where(t => t.name == name || t.name.StartsWith(name + "_"))
                    .OrderBy(t => t.position.x).ThenBy(t => t.position.z).ElementAt(nth);
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0) { var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds); at = b.center; }
                else at = go.position;
                s = s.Substring(c + 1);
            }
            var p = s.Split(',').Select(float.Parse).ToArray();
            return at + new Vector3(p[0], p[1], p[2]);
        }

        public static void Run()
        {
            int code = 1;
            try
            {
                EditorSceneManager.OpenScene(Arg("-probeScene"), OpenSceneMode.Single);
                var size = Arg("-probeSize").Split('x').Select(int.Parse).ToArray();

                // -probeBeacons <folder>: objective markers stood on the ground ahead of the player's start (on the
                // NavMesh, where LevelObjective would put them) and the player's own view of them, plus one from the side.
                if (Arg("-probeBeacons") != null)
                {
                    string folder = Arg("-probeBeacons");
                    System.IO.Directory.CreateDirectory(folder);
                    var eye = Camera.main;
                    var body = GameObject.FindWithTag("Player").transform;
                    foreach (var (angle, dist, colour, radius, height) in new[] { (-22f, 26f, UITheme.Coolant, 7f, 6f), (6f, 40f, UITheme.Coolant, 7f, 6f), (28f, 20f, UITheme.Plasma, 2.4f, 9f) })
                    {
                        Vector3 want = body.position + Quaternion.Euler(0f, angle, 0f) * body.forward * dist;
                        if (UnityEngine.AI.NavMesh.SamplePosition(want, out var hit, 8f, UnityEngine.AI.NavMesh.AllAreas))
                            LevelObjective.BuildBeacon("Probe", hit.position, colour, radius, height, false, false);
                    }
                    var c = new GameObject("ProbeEye").AddComponent<Camera>();
                    c.CopyFrom(eye);
                    c.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                    FairgroundShots.Capture(c, folder + "/player_view.png", size[0], size[1]);
                    c.transform.position = body.position + body.right * 22f + body.forward * 24f + Vector3.up * 3f;
                    c.transform.LookAt(body.position + body.forward * 28f);
                    FairgroundShots.Capture(c, folder + "/side_view.png", size[0], size[1]);
                    Debug.Log("[FPSKitBatch] probe beacons -> " + folder);
                    FPSKitBatch.Exit(0);
                    return;
                }

                // -probeLeash: the scene's leash against what LevelManager.Start demands of it, for every level's reach.
                if (Environment.GetCommandLineArgs().Contains("-probeLeash"))
                {
                    var lm = UnityEngine.Object.FindAnyObjectByType<LevelManager>();
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    float span = (float)typeof(LevelManager).GetMethod("SpawnPointSpan", flags).Invoke(lm, null);
                    float reach = 1f;
                    if (lm.levels != null) foreach (var l in lm.levels.levels) if (l != null) reach = Mathf.Max(reach, l.spawnReach);
                    float ring = lm.maxSpawnDistanceFromPlayer * reach;
                    if (lm.spawnRingCap > 0f) ring = Mathf.Min(ring, lm.spawnRingCap);
                    float need = Mathf.Max(ring * 1.4f, span + 20f);
                    Debug.Log($"[FPSKitBatch] probe leash: despawn {lm.despawnDistance:0} m, spawn points span {span:0} m, widest ring {ring:0} m (reach x{reach:0.0}), " +
                              $"LevelManager needs {need:0} m -> {(lm.despawnDistance >= need ? "no warning" : "WARNING")}; zones {lm.spawnZones?.Length ?? 0}, zone distance {lm.zoneSpawnDistance}, {lm.spawnPoints.Length} points");
                    FPSKitBatch.Exit(0);
                    return;
                }

                // -probeFootings: do walls and yards meet the ground? For every wall face (an upright triangle) of the
                // meshes under the named roots, the lowest edge of each upright column of wall is measured against the
                // terrain under it: above the sand by more than 15 cm is a wall standing in the air (a house over a dune).
                if (Arg("-probeFootings") != null)
                {
                    // The ground is the "Dune" chunks (FPSKitTerrainTest's definition): a ray down that meets only them.
                    var dunes = new HashSet<Collider>(UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)
                        .Where(f => f.gameObject.name == "Dune").Select(f => f.GetComponent<Collider>()).Where(c => c != null));
                    float Ground(Vector3 p)
                    {
                        var ray = new Ray(new Vector3(p.x, 400f, p.z), Vector3.down);
                        foreach (var d in dunes) if (d.Raycast(ray, out var h, 1000f)) return h.point.y;
                        return float.NaN;
                    }
                    var roots = Arg("-probeFootings").Split(',');
                    var lines = new List<string>();
                    int columnsAll = 0, airAll = 0;
                    foreach (var f in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                    {
                        string path = Path(f.transform);
                        if (!roots.Any(r => path.Contains("/" + r))) continue;
                        if (f.sharedMesh == null || !f.sharedMesh.isReadable) continue;
                        var v = f.sharedMesh.vertices; var tri = f.sharedMesh.triangles; var tr = f.transform;
                        var lowest = new Dictionary<(int, int), float>();
                        for (int i = 0; i < tri.Length; i += 3)
                        {
                            Vector3 a = tr.TransformPoint(v[tri[i]]), b = tr.TransformPoint(v[tri[i + 1]]), c = tr.TransformPoint(v[tri[i + 2]]);
                            var n = Vector3.Cross(b - a, c - a);
                            if (n.sqrMagnitude < 1e-8f || Mathf.Abs(n.normalized.y) > 0.2f) continue;   // upright faces only
                            foreach (var p in new[] { a, b, c })
                            {
                                var key = (Mathf.FloorToInt(p.x / 0.4f), Mathf.FloorToInt(p.z / 0.4f));
                                if (!lowest.TryGetValue(key, out float y) || p.y < y) lowest[key] = p.y;
                            }
                        }
                        int air = 0; float worst = 0f; Vector3 worstAt = Vector3.zero;
                        foreach (var kv in lowest)
                        {
                            var p = new Vector3((kv.Key.Item1 + 0.5f) * 0.4f, kv.Value, (kv.Key.Item2 + 0.5f) * 0.4f);
                            float gap = kv.Value - Ground(p);
                            // 0.15-1.2 m: a wall's foot off the sand. Higher is the head of a doorway (the wall over a door
                            // has nothing under it by design) or an upper storey.
                            if (!float.IsNaN(gap) && gap > 0.15f && gap < 1.2f) { air++; if (gap > worst) { worst = gap; worstAt = p; } }
                        }
                        columnsAll += lowest.Count; airAll += air;
                        if (air > 0) lines.Add($"{air,5} of {lowest.Count,6} columns in the air, worst {worst:0.00} m at {worstAt:F1} | {path}");
                    }
                    lines.Sort();
                    lines.Reverse();
                    string report = $"{airAll} of {columnsAll} wall columns have their foot 15 cm to 1.2 m above the sand\n" + string.Join("\n", lines);
                    System.IO.File.WriteAllText(Arg("-probeOut") ?? "Build/Views/footings.txt", report);
                    Debug.Log("[FPSKitBatch] probe footings: " + report.Split('\n')[0]);
                    FPSKitBatch.Exit(0);
                    return;
                }

                // -probeFloating: every renderer whose underside is well above the ground under it, with its colour.
                if (Environment.GetCommandLineArgs().Contains("-probeFloating"))
                {
                    var terrain = Terrain.activeTerrain;
                    var rows = new List<string>();
                    foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    {
                        if (!r.enabled || r is ParticleSystemRenderer) continue;
                        var b = r.bounds;
                        float ground = terrain != null ? terrain.SampleHeight(b.center) + terrain.transform.position.y : 0f;
                        float gap = b.min.y - ground;
                        if (gap < 8f && b.max.y < ground + 45f) continue;
                        var col = r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor") ? r.sharedMaterial.GetColor("_BaseColor") : Color.magenta;
                        rows.Add($"{gap:0}m up, top {b.max.y - ground:0}m | {Path(r.transform)} [{r.sharedMaterial?.name} rgb {col.r:0.00},{col.g:0.00},{col.b:0.00}] at {b.center:F0} size {b.size:F0}");
                    }
                    Debug.Log("[FPSKitBatch] probe floating: " + rows.Count + "\n" + string.Join("\n", rows.OrderBy(x => x)));
                    System.IO.File.WriteAllText("Build/Views/floating.txt", string.Join("\n", rows.OrderBy(x => x)));
                    FPSKitBatch.Exit(0);
                    return;
                }

                // -probeList "file|x,y,z|x,y,z|fov;...": several pictures in one run.
                if (Arg("-probeList") != null)
                {
                    foreach (var entry in Arg("-probeList").Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var f = entry.Split('|');
                        var c = new GameObject("ProbeShot").AddComponent<Camera>();
                        c.fieldOfView = float.Parse(f[3]);
                        c.transform.position = V(f[1]);
                        c.transform.LookAt(V(f[2]));
                        var pc = Camera.main;
                        c.farClipPlane = pc != null ? pc.farClipPlane : 1800f;
                        c.nearClipPlane = 0.3f;
                        if (pc != null) { c.clearFlags = pc.clearFlags; c.backgroundColor = pc.backgroundColor; }
                        c.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                        FairgroundShots.Capture(c, f[0], size[0], size[1]);
                        UnityEngine.Object.DestroyImmediate(c.gameObject);
                    }
                    Debug.Log($"[FPSKitBatch] probe list done (far clip {(Camera.main != null ? Camera.main.farClipPlane : -1):0})");
                    FPSKitBatch.Exit(0);
                    return;
                }
                var go = new GameObject("ProbeCam");
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = float.Parse(Arg("-probeFov"));
                cam.aspect = size[0] / (float)size[1];
                go.transform.position = V(Arg("-probeFrom"));
                go.transform.LookAt(V(Arg("-probeLook")));

                // -probeShot file: also a picture from this camera, with the scene's own player camera's far clip.
                if (Arg("-probeShot") != null)
                {
                    var player = Camera.main;
                    cam.farClipPlane = player != null ? player.farClipPlane : 1800f;
                    cam.nearClipPlane = 0.3f;
                    cam.clearFlags = CameraClearFlags.Skybox;
                    if (player != null) { cam.clearFlags = player.clearFlags; cam.backgroundColor = player.backgroundColor; }
                    var data = cam.GetUniversalAdditionalCameraData();
                    data.renderPostProcessing = true;
                    FairgroundShots.Capture(cam, Arg("-probeShot"), size[0], size[1]);
                    Debug.Log($"[FPSKitBatch] probe shot -> {Arg("-probeShot")} (far clip {cam.farClipPlane:0})");
                }

                var meshes = UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)
                    .Where(f => f.sharedMesh != null && f.GetComponent<Renderer>() != null && f.GetComponent<Renderer>().enabled).ToArray();
                var terrains = Terrain.activeTerrains;
                foreach (var px in (Arg("-probePixels") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var xy = px.Split(',').Select(float.Parse).ToArray();
                    var ray = cam.ScreenPointToRay(new Vector3(xy[0], size[1] - xy[1], 0f));
                    var hits = new List<(float d, string what)>();
                    foreach (var f in meshes)
                    {
                        var r = f.GetComponent<Renderer>();
                        if (!r.bounds.IntersectRay(ray, out float bd)) continue;
                        float d = MeshHit(f, ray);
                        if (d < 0f) continue;
                        hits.Add((d, $"{Path(f.transform)} [{r.sharedMaterial?.name}] layer {LayerMask.LayerToName(f.gameObject.layer)} bounds {r.bounds.center:F0} size {r.bounds.size:F0}"));
                    }
                    // Everything that is not a plain mesh (particles, lines, trails, sprites, skinned meshes): by its bounds.
                    foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    {
                        if (!r.enabled || r.GetComponent<MeshFilter>() != null) continue;
                        if (r.bounds.IntersectRay(ray, out float bd))
                            hits.Add((bd, $"({r.GetType().Name}) {Path(r.transform)} [{r.sharedMaterial?.name}] bounds {r.bounds.center:F0} size {r.bounds.size:F0}"));
                    }
                    foreach (var t in terrains)
                    {
                        var c = t.GetComponent<TerrainCollider>();
                        if (c != null && c.Raycast(ray, out var h, 5000f)) hits.Add((h.distance, $"TERRAIN {Path(t.transform)} at {h.point:F0}"));
                    }
                    Debug.Log($"[FPSKitBatch] probe pixel {px}: " + string.Join(" || ", hits.OrderBy(h => h.d).Take(6).Select(h => $"{h.d:0}m {h.what}")));
                }
                code = 0;
            }
            catch (Exception e) { Debug.LogError("[FPSKit] probe failed: " + e); }
            FPSKitBatch.Exit(code);
        }

        static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;

        /// <summary>Distance along the ray to the mesh's nearest triangle (either face), or -1.</summary>
        static float MeshHit(MeshFilter f, Ray world)
        {
            var m = f.sharedMesh;
            if (!m.isReadable) return f.GetComponent<Renderer>().bounds.IntersectRay(world, out float bd) ? bd : -1f;
            var tr = f.transform;
            var v = m.vertices; var tri = m.triangles;
            float best = -1f;
            for (int i = 0; i < tri.Length; i += 3)
            {
                Vector3 a = tr.TransformPoint(v[tri[i]]), b = tr.TransformPoint(v[tri[i + 1]]), c = tr.TransformPoint(v[tri[i + 2]]);
                Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(world.direction, e2);
                float det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-9f) continue;
                Vector3 s = world.origin - a;
                float u = Vector3.Dot(s, p) / det;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(s, e1);
                float w = Vector3.Dot(world.direction, q) / det;
                if (w < 0f || u + w > 1f) continue;
                float d = Vector3.Dot(e2, q) / det;
                if (d > 0f && (best < 0f || d < best)) best = d;
            }
            return best;
        }
    }
}
#endif
