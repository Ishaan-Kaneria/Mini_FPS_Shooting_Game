#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Bakes each district's rooms (FPSKitTownRoomScenes) on their own: their insides lightmapped at 20 cm, their furniture
    /// shading them, the trading shops' tubes as baked area lights. With several scenes open Unity writes one lighting data
    /// for them all, so a district is baked with only its own scene open, and what lights its rooms from outside is put into
    /// it for the bake and taken out before it is saved: the sun, the sky (the town's environment and lighting settings), the
    /// terrain, and every building and street piece within <see cref="Margin"/> of the district (their meshes copied, as the
    /// town's are kept in its scene). The light enters at the doors and windows, bounces off the street and the facades
    /// opposite, and the lightmaps keep it, as the pilot's do (daylight factor 2-6 % by the openings, 0.5-1.8 % at the back).
    /// At the probe volumes' strength (FPSKitTownGI.ApvIndirect, ApvSky), as the pilot: a lightmapped room must not be darker
    /// than the street's probe-lit things beside it.
    ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitTownRoomBake.Batch -force-vulkan [-fpskitDistricts 1,2]
    /// </summary>
    public static class FPSKitTownRoomBake
    {
        const float Margin = 40f;

        public static void Batch()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-fpskitDistricts");
            var which = i >= 0 && i + 1 < args.Length
                ? args[i + 1].Split(',').Select(s => int.Parse(s.Trim()) - 1).ToList()
                : Enumerable.Range(0, FPSKitTownRoomScenes.Count).ToList();
            bool allOk = true;
            foreach (int k in which)
            {
                try { Debug.Log("[FPSKit] room bake: " + Bake(k)); }
                catch (System.Exception e) { Debug.LogError($"[FPSKit] room bake {k + 1} FAILED: {e}"); allOk = false; }
            }
            EditorApplication.Exit(allOk ? 0 : 1);
        }

        sealed class Piece { public Mesh mesh; public Material[] mats; public Matrix4x4 m; public string name; }

        public static string Bake(int k)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            string path = FPSKitTownRoomScenes.ScenePath(k);
            if (!System.IO.File.Exists(path)) return $"district {k + 1}: no scene (split first)";

            // ---- From the town: what lights the district from outside ----
            EditorSceneManager.OpenScene(FPSKitTownGI.DesertScene, OpenSceneMode.Single);
            Rect area = DistrictRect(k);
            Rect reach = new Rect(area.xMin - Margin, area.yMin - Margin, area.width + 2 * Margin, area.height + 2 * Margin);
            var pieces = new List<Piece>();
            var arena = GameObject.Find("Arena");
            int backdrop = LayerMask.NameToLayer("Backdrop");
            foreach (var r in arena.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.gameObject.layer == backdrop) continue;
                if (r.name.StartsWith("Detail_") || r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                if (r.GetComponentInParent<Civilian>() != null || r.GetComponentInParent<EnemyAI>() != null || r.GetComponentInParent<Rigidbody>() != null) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var b = r.bounds;
                if (b.max.x < reach.xMin || b.min.x > reach.xMax || b.max.z < reach.yMin || b.min.z > reach.yMax) continue;
                // Small things (props, signs) shade nothing a room sees: the street's buildings and ground pieces only.
                if (b.size.magnitude < 3f) continue;
                pieces.Add(new Piece { mesh = AssetDatabase.Contains(mf.sharedMesh) ? mf.sharedMesh : Object.Instantiate(mf.sharedMesh), mats = r.sharedMaterials, m = r.localToWorldMatrix, name = r.name });
            }
            var sunLight = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude).FirstOrDefault(l => l.type == LightType.Directional && l.shadows != LightShadows.None);
            var sun = sunLight == null ? default : (sunLight.transform.rotation, sunLight.color, sunLight.intensity, sunLight.colorTemperature, sunLight.useColorTemperature);
            var env = (RenderSettings.skybox, RenderSettings.ambientMode, RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor,
                       RenderSettings.ambientGroundColor, RenderSettings.ambientIntensity, RenderSettings.defaultReflectionMode, RenderSettings.reflectionIntensity);
            var settings = Lightmapping.lightingSettings;
            var shops = new List<(Vector3 pos, Vector3 right, Vector3 fwd, Vector3 scale)>();
            if (GameObject.Find("ShopRooms") is GameObject marks)
                foreach (Transform s in marks.transform)
                    if (!s.name.EndsWith(" dark") && s.name.StartsWith("Shop ") && area.Contains(new Vector2(s.position.x, s.position.z)))
                        shops.Add((s.position, s.right, s.forward, s.localScale));

            // ---- The district alone, with them put in ----
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var context = new GameObject("BakeContext (removed after the bake)");
            foreach (var p in pieces)
            {
                var go = new GameObject(p.name);
                go.transform.SetParent(context.transform, false);
                go.transform.SetPositionAndRotation(p.m.GetColumn(3), p.m.rotation);
                go.transform.localScale = p.m.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = p.mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = p.mats;
                mr.receiveGI = ReceiveGI.LightProbes;
                mr.lightProbeUsage = LightProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
            }
            // The terrain is left out: made a contributor here it took a lightmap of its own (the town's ground, a kilometre);
            // the roads' and pavements' meshes round the district bounce the street's light in at the doors.
            if (sunLight != null)
            {
                var sgo = new GameObject("Sun");
                sgo.transform.SetParent(context.transform, false);
                sgo.transform.rotation = sun.rotation;
                var l = sgo.AddComponent<Light>();
                l.type = LightType.Directional; l.color = sun.color; l.intensity = sun.intensity;
                l.useColorTemperature = sun.useColorTemperature; l.colorTemperature = sun.colorTemperature;
                l.shadows = LightShadows.Soft; l.lightmapBakeType = LightmapBakeType.Mixed;   // real-time sun, baked bounce (the town's)
            }
            RenderSettings.skybox = env.skybox; RenderSettings.ambientMode = env.ambientMode;
            RenderSettings.ambientSkyColor = env.ambientSkyColor; RenderSettings.ambientEquatorColor = env.ambientEquatorColor;
            RenderSettings.ambientGroundColor = env.ambientGroundColor; RenderSettings.defaultReflectionMode = env.defaultReflectionMode;
            RenderSettings.reflectionIntensity = env.reflectionIntensity;
            RenderSettings.ambientIntensity = env.ambientIntensity * FPSKitTownGI.ApvSky;
            if (settings != null) Lightmapping.lightingSettings = settings;

            // The insides lightmapped; the furniture shading them; the trading shops' tubes lit.
            var rooms = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Rooms");
            float m2 = 0f; int insides = 0, furniture = 0;
            foreach (var r in rooms.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool inside = !r.name.StartsWith("Detail_") && (r.name.StartsWith("Interior_") || r.name.StartsWith("ShopInside_") || r.name.StartsWith("ShopWindow_"));
                if (inside)
                {
                    m2 += FPSKitTownLightmaps.Lightmapped(r);
                    r.scaleInLightmap *= FPSKitTownLightmaps.DistrictScale;
                    insides++;
                }
                else
                {
                    r.receiveGI = ReceiveGI.LightProbes;
                    if (!r.name.StartsWith("Detail_")) { GameObjectUtility.SetStaticEditorFlags(r.gameObject, GameObjectUtility.GetStaticEditorFlags(r.gameObject) | StaticEditorFlags.ContributeGI); furniture++; }
                }
            }
            var tubes = new GameObject("ShopTubes (bake)").transform;
            tubes.SetParent(context.transform, false);
            foreach (var (pos, right, fwd, scale) in shops)
                foreach (var (at, along) in FPSKitSceneBuilder.ShopTubes(pos, right, fwd, scale.x, scale.y))
                    FPSKitTownLightmaps.Tube(tubes, at, along);

            float indirect = settings != null ? settings.indirectScale : 1f;
            if (settings != null) { settings.indirectScale = FPSKitTownGI.ApvIndirect; settings.lightmapResolution = FPSKitTownLightmaps.TexelsPerMetre; settings.lightmapMaxSize = 1024; settings.directionalityMode = LightmapsMode.CombinedDirectional; settings.ao = true; settings.aoMaxDistance = 0.6f; }
            bool ok;
            try { ok = Lightmapping.Bake(); }
            finally { if (settings != null) { settings.indirectScale = indirect; EditorUtility.SetDirty(settings); } }

            // ---- Out again: only the rooms and their lightmaps are saved ----
            Object.DestroyImmediate(context);
            RenderSettings.ambientIntensity = env.ambientIntensity;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
            int maps = LightmapSettings.lightmaps.Length;
            return $"district {k + 1}: {(ok ? "baked" : "FAILED")}, {insides} insides ({m2:0} m2 at {FPSKitTownLightmaps.TexelsPerMetre * FPSKitTownLightmaps.DistrictScale:0}/m), " +
                   $"{furniture} pieces of furniture, {shops.Count} lit shops, {pieces.Count} town pieces and no terrain, {maps} lightmaps, {clock.Elapsed.TotalMinutes:0.0} min";
        }

        /// <summary>The district's ground rect (x, z), as FPSKitTownRoomScenes.DistrictOf cuts it.</summary>
        static Rect DistrictRect(int k)
        {
            const float span = 1060f;
            float w = span / FPSKitTownRoomScenes.Columns, h = span / FPSKitTownRoomScenes.Rows;
            int c = k % FPSKitTownRoomScenes.Columns, r = k / FPSKitTownRoomScenes.Columns;
            float x0 = -span * 0.5f + c * w, z0 = -span * 0.5f + r * h;
            // The grid's outer cells reach on to the town's edge (DistrictOf clamps).
            if (c == 0) { x0 -= 200f; w += 200f; } if (c == FPSKitTownRoomScenes.Columns - 1) w += 200f;
            if (r == 0) { z0 -= 200f; h += 200f; } if (r == FPSKitTownRoomScenes.Rows - 1) h += 200f;
            return new Rect(x0, z0, w, h);
        }
    }
}
#endif
