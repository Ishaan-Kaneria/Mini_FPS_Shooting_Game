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
