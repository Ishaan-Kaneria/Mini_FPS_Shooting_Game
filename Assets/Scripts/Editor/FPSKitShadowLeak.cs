#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Where the sun gets into a room past a roof that does not cast its shadow (his play-test, 2026-10-31: upper walls sunlit,
    /// lower dark, a stair's hard sun shadow indoors). No rendering: from points over each house room's floor and up its walls
    /// a cast goes toward the sun, through back faces too. Where something is in the way (the room is covered there) at least
    /// one thing on that line must draw into the sun's shadow map: a renderer that is on, casts shadows, keeps its ShadowCaster
    /// pass, and turns a face toward the sun (or is two-sided). A covered point with none is a leak; the report names what
    /// covers it and why it casts nothing, room by room. Writes Build/Pilot/shadow_leaks.txt.
    ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitShadowLeak.Batch -force-vulkan
    /// </summary>
    public static class FPSKitShadowLeak
    {
        public static void Batch()
        {
            try
            {
                EditorSceneManager.OpenScene(FPSKitTownGI.DesertScene, OpenSceneMode.Single);
                var text = Report(out int leaky, out int rooms);
                System.IO.Directory.CreateDirectory("Build/Pilot");
                System.IO.File.WriteAllText("Build/Pilot/shadow_leaks.txt", text);
                Debug.Log($"[FPSKit] shadow leaks: {leaky} of {rooms} rooms\n" + text.Substring(0, Mathf.Min(text.Length, 6000)));
                EditorApplication.Exit(0);
            }
            catch (System.Exception e) { Debug.LogError("[FPSKit] shadow leaks FAILED: " + e); EditorApplication.Exit(1); }
        }

        /// <summary>Everything on one line to the sun from a point, with what decides its shadow (for checking the check).</summary>
        public static void BatchOneRay()
        {
            EditorSceneManager.OpenScene(FPSKitTownGI.DesertScene, OpenSceneMode.Single);
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional && l.shadows != LightShadows.None);
            Vector3 toSun = -sun.transform.forward, p = new Vector3(121f, 2.0f, -343f);
            Physics.queriesHitBackfaces = true;
            var sb = new StringBuilder($"from {p} toward {toSun}\n");
            foreach (var hit in Physics.RaycastAll(p, toSun, 300f, ~LayerMask.GetMask("Player", "FirstPerson", "Enemy", "Ignore Raycast", "UI", "Civilian"), QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                var r = hit.collider.GetComponent<Renderer>();
                sb.Append($"  {hit.distance:0.00} m {PathOf(hit.collider.transform)} normal {hit.normal} renderer {(r ? r.GetType().Name : "none")}");
                if (r) foreach (var m in r.sharedMaterials)
                        sb.Append($"\n      mat {(m ? m.name : "null")} shader {(m ? m.shader.name : "-")} passCount {(m ? m.passCount : 0)} find {(m ? m.FindPass("ShadowCaster") : -9)} enabled {(m ? m.GetShaderPassEnabled("ShadowCaster") : false)} cast {r.shadowCastingMode}");
                sb.Append($"\n      verdict {NoShadow(r, hit.normal, toSun) ?? "casts"}\n");
            }
            Debug.Log("[FPSKit] one ray:\n" + sb);
            System.IO.File.WriteAllText("Build/Pilot/one_ray.txt", sb.ToString());
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Where the upper-walls-lit band comes from: four ordinary (not lightmapped) house rooms, each seen from its way in, as
        /// it is, with the sun off, and with occlusion culling off. The band going with the sun is sun past the roof (and with
        /// culling off too, the culling dropping the roof from the shadow map); the band staying with the sun off is the
        /// probes' light. Writes Build/Pilot/leaktest/*.png and the upper and lower wall levels to leaktest.txt.
        ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitShadowLeak.BatchBand -force-vulkan
        /// </summary>
        public static void BatchBand()
        {
            try
            {
                EditorSceneManager.OpenScene(FPSKitTownGI.DesertScene, OpenSceneMode.Single);
                foreach (var district in FPSKitTownRoomScenes.ExistingPaths()) EditorSceneManager.OpenScene(district, OpenSceneMode.Additive);
                int high = System.Array.IndexOf(QualitySettings.names, "High");
                if (high >= 0) QualitySettings.SetQualityLevel(high, true);       // his tier
                DynamicGI.UpdateEnvironment();
                var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional && l.shadows != LightShadows.None);
                var pilot = GameObject.Find("LightmapPilot");
                var houses = GameObject.Find("HouseRooms").transform;
                var insides = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include).Where(r => r.name.StartsWith("Interior_")).ToArray();
                bool Lightmapped(Transform room) => insides.Where(r => r.bounds.Contains(room.position + Vector3.up * 0.5f))
                    .OrderBy(r => r.bounds.size.sqrMagnitude).FirstOrDefault() is MeshRenderer m && m.lightmapIndex >= 0 && m.lightmapIndex < 0xFFFE;
                var candidates = houses.Cast<Transform>()
                    .Select(h => h.Cast<Transform>().FirstOrDefault(r => r.name.StartsWith("Room ") && r.name.EndsWith(" f0") && r.Find("Look") != null))
                    .Where(r => r != null && (pilot == null || pilot.transform.Cast<Transform>().All(v => (v.position - r.position).sqrMagnitude > 4f)))
                    .ToList();
                // The first two of the old set (outside the district, room 1 is the banded one) and two in the district.
                var rooms = candidates.Where(r => !Lightmapped(r)).Take(2).Concat(candidates.Where(Lightmapped).Take(2)).ToList();
                const int W = 640, H = 360;
                var dir = "Build/Pilot/leaktest";
                System.IO.Directory.CreateDirectory(dir);
                var camGo = new GameObject("LeakCam") { hideFlags = HideFlags.DontSave };
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 75f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 600f;
                var data = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                data.renderPostProcessing = true;
                var sb = new StringBuilder("level of the upper and lower thirds of the view (0-255), as is / sun off / culling off\n");
                int i = 0;
                foreach (var room in rooms)
                {
                    var look = room.Find("Look").position;
                    cam.transform.position = room.position + Vector3.up * 1.6f;
                    cam.transform.LookAt(look + Vector3.up * 1.4f);
                    string Shot(string tag)
                    {
                        var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
                        var req = new RenderPipeline.StandardRequest { destination = rt };
                        RenderPipeline.SubmitRenderRequest(cam, req);
                        RenderPipeline.SubmitRenderRequest(cam, req);
                        var prev = RenderTexture.active; RenderTexture.active = rt;
                        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                        System.IO.File.WriteAllBytes($"{dir}/room{i}_{tag}.png", tex.EncodeToPNG());
                        float Band(int y0, int y1) { float s = 0; int n = 0; for (int y = y0; y < y1; y += 4) for (int x = 0; x < W; x += 4) { var c = tex.GetPixel(x, y); s += 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; n++; } return 255f * s / n; }
                        string r = $"{Band(H * 2 / 3, H):0}/{Band(H / 3, H * 2 / 3):0}";       // ReadPixels rows run bottom up: upper first
                        Object.DestroyImmediate(tex);
                        return r;
                    }
                    // The rooms' probes as the game has them on High (IndoorReflections: those within 45 m of the eye on).
                    foreach (var p in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include))
                        if (p.name.StartsWith("RoomProbe_") || p.name.StartsWith("ShopProbe_") || p.name.StartsWith("Pilot"))
                            p.enabled = (p.bounds.ClosestPoint(cam.transform.position) - cam.transform.position).sqrMagnitude < 45f * 45f;
                    string a = Shot("asis");
                    sun.enabled = false; string b = Shot("sunoff"); sun.enabled = true;
                    float was = sun.intensity; sun.intensity = was * 3f; string b3 = Shot("sun3x"); sun.intensity = was;
                    cam.useOcclusionCulling = false; string c = Shot("nocull"); cam.useOcclusionCulling = true;
                    // Off the probe layers for one shot: every renderer back on Default alone, so each reads every probe.
                    var masked = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(x => (x.renderingLayerMask & (FPSKitTownGI.IndoorLayer | FPSKitTownGI.OutdoorLayer)) != 0).Select(x => (x, x.renderingLayerMask)).ToList();
                    foreach (var (x, _) in masked) x.renderingLayerMask = 1u;
                    string d = Shot("nolayers");
                    foreach (var (x, m0) in masked) x.renderingLayerMask = m0;
                    // Every reflection off: what the reflections add to the room (his 'everything is shiny', 2026-11-03).
                    var probesOn = Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).Where(p => p.enabled).ToList();
                    float reflWas = RenderSettings.reflectionIntensity;
                    foreach (var p in probesOn) p.enabled = false;
                    RenderSettings.reflectionIntensity = 0f;
                    string e = Shot("refloff");
                    foreach (var p in probesOn) p.enabled = true;
                    RenderSettings.reflectionIntensity = reflWas;
                    var here = cam.transform.position;
                    var cover = Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include).Where(p => p.bounds.Contains(here))
                        .Select(p => $"{p.name}({(p.enabled ? "on" : "off")} {p.intensity:0.00} {(p.customBakedTexture != null ? p.customBakedTexture.name : p.bakedTexture != null ? p.bakedTexture.name : "no texture")})").ToList();
                    sb.AppendLine($"  probes over room{i}: {(cover.Count == 0 ? "none (the sky)" : string.Join(", ", cover))}; default reflection {RenderSettings.defaultReflectionMode}, intensity {RenderSettings.reflectionIntensity:0.00}");
                    sb.AppendLine($"room{i} {(Lightmapped(room) ? "LIGHTMAPPED" : "probes")} {room.parent.name}/{room.name} at {room.position}: upper/middle {a} | sun off {b} | sun 3x {b3} | culling off {c} | no layers {d} | reflections off {e}");
                    i++;
                }
                Object.DestroyImmediate(camGo);
                System.IO.File.WriteAllText($"{dir}/leaktest.txt", sb.ToString());
                Debug.Log("[FPSKit] band test:\n" + sb);
                EditorApplication.Exit(0);
            }
            catch (System.Exception e) { Debug.LogError("[FPSKit] band test FAILED: " + e); EditorApplication.Exit(1); }
        }

        /// <summary>Why a renderer draws nothing into the sun's shadow map from this face, or null when it does.</summary>
        static string NoShadow(Renderer r, Vector3 faceNormal, Vector3 toSun)
        {
            if (r == null) return "no renderer (collider only)";
            if (!r.enabled) return "renderer off";
            if (!r.gameObject.activeInHierarchy) return "object inactive";
            if (r.shadowCastingMode == ShadowCastingMode.Off) return "shadows off";
            var mats = r.sharedMaterials;
            // (Material.FindPass by name fails in batch: passCount reads 1, so only the material's own switch is asked.)
            if (mats.Length > 0 && mats.All(m => m == null || !m.GetShaderPassEnabled("ShadowCaster")))
                return "no shadow pass";
            if (r.shadowCastingMode == ShadowCastingMode.TwoSided) return null;
            bool twoSidedMat = mats.Any(m => m != null && m.HasProperty("_Cull") && m.GetFloat("_Cull") == 0f);
            if (twoSidedMat) return null;
            if (Vector3.Dot(faceNormal, toSun) <= 0f) return "back face to the sun (one-sided)";
            return null;
        }

        static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null && sb.Length < 120; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        public static string Report(out int leaky, out int roomCount)
        {
            var sb = new StringBuilder();
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional && l.shadows != LightShadows.None);
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.up;
            int mask = ~LayerMask.GetMask("Player", "FirstPerson", "Enemy", "Ignore Raycast", "UI", "Civilian");
            bool backfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            var why = new Dictionary<string, int>();
            var culprits = new Dictionary<string, int>();
            leaky = 0; roomCount = 0;
            int covered = 0, leaks = 0;
            var lines = new List<string>();
            try
            {
                var houses = GameObject.Find("HouseRooms");
                if (houses == null) return "no HouseRooms in the scene";
                foreach (Transform house in houses.transform)
                    foreach (Transform room in house)
                    {
                        if (!room.name.StartsWith("Room ")) continue;
                        var shape = FPSKitTownLightmaps.FindRoom(room.position, toSun, mask);
                        if (shape.cells.Count < 16) continue;
                        roomCount++;
                        int rc = 0, rl = 0;
                        string first = null;
                        // Every 4th cell (1 m), at the floor and at 2 m up the wall height.
                        foreach (var c in shape.cells.Where(c => ((c.x & 3) == 0) && ((c.y & 3) == 0)))
                            foreach (float h in new[] { 0.05f, 2.0f })
                            {
                                var p = shape.World(c, shape.floorY + h);
                                var hits = Physics.RaycastAll(p, toSun, 300f, mask, QueryTriggerInteraction.Ignore);
                                if (hits.Length == 0) continue;            // the sky: sun through an opening, as it should be
                                rc++;
                                bool casts = false; string reason = null; Transform by = null;
                                foreach (var hit in hits.OrderBy(x => x.distance))
                                {
                                    var r = hit.collider.GetComponent<Renderer>();
                                    var no = NoShadow(r, hit.normal, toSun);
                                    if (no == null) { casts = true; break; }
                                    if (reason == null) { reason = no; by = hit.collider.transform; }
                                }
                                if (casts) continue;
                                rl++;
                                why[reason] = why.TryGetValue(reason, out int n) ? n + 1 : 1;
                                string who = by != null ? PathOf(by) : "?";
                                culprits[who] = culprits.TryGetValue(who, out int m) ? m + 1 : 1;
                                if (first == null) first = $"{reason}: {who} at {p}";
                            }
                        covered += rc; leaks += rl;
                        if (rl > 0)
                        {
                            leaky++;
                            lines.Add($"{house.name}/{room.name} at {room.position}: {rl} of {rc} covered points with no sun shadow; first: {first}");
                        }
                    }
            }
            finally { Physics.queriesHitBackfaces = backfaces; }
            sb.AppendLine($"sun from {toSun}; {roomCount} house rooms, {covered} covered points, {leaks} with no shadow caster on the line to the sun, in {leaky} rooms");
            sb.AppendLine("why the first thing on the line casts nothing:");
            foreach (var kv in why.OrderByDescending(k => k.Value)) sb.AppendLine($"  {kv.Value,6}  {kv.Key}");
            sb.AppendLine("what it is (most points first):");
            foreach (var kv in culprits.OrderByDescending(k => k.Value).Take(25)) sb.AppendLine($"  {kv.Value,6}  {kv.Key}");
            sb.AppendLine("rooms:");
            foreach (var l in lines.Take(200)) sb.AppendLine("  " + l);
            return sb.ToString();
        }
    }
}
#endif
