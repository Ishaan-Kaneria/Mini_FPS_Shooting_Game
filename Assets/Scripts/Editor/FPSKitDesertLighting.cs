#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The desert's lighting, all of it, in one place (his order of 2026-11-08: "the lighting is very random and not
    /// controlled; delete it and make it again carefully"). It had grown to about ten layers patched over each other: a
    /// bounce light, cloud shadows, eye adaptation, two probe systems (classic and adaptive volumes), room lightmaps, 40+ room
    /// and covered-place reflection probes, the arms' own stand-in light, SSAO and a colour grade -- each tuned against the
    /// others, so one change moved everything.
    ///
    /// <see cref="Relight"/> takes every one of those out of the saved desert and its room districts, then puts back the
    /// layers that have been approved, in order, each written here with its values:
    ///
    ///   1. <b>Sun and sky</b>: one sun (the theme's colour, strength and angle, shadows at full strength), the sky's ambient
    ///      (the theme's three colours), the sky's reflection and nothing else, the theme's fog, a fixed exposure. Real time:
    ///      nothing is baked. (Approved 2026-11-08.)
    ///   2. <b>Real lamps</b> (his order, 2026-11-09: lamps and tubes only, rooms not darkened): a light at every round ceiling lamp (warm) and every shop tube (cool), found by their materials
    ///      in the room meshes; the nearest few lit at a time (LampSet). The fittings glow.
    ///
    /// Not built, set aside on branch wip/lighting-layers-2-3: rooms darker than the street (RoomAmbient). Later, each only once
    /// he has seen the one before: a gentle eye, matte materials; the bake comes last.
    ///
    /// Run on the saved scenes, no rebuild: <c>Tools/unity-batch.sh FPSKitBatch.RelightDesert</c>. The desert's build calls it
    /// too, so a rebuilt desert comes out the same. Its report is Build/Lighting/relight.txt.
    /// </summary>
    public static class FPSKitDesertLighting
    {
        /// <summary>The layers built so far (1 = sun and sky).</summary>
        public const int Layers = 2;

        /// <summary>
        /// The fixed exposure, in stops, for the whole desert (layer 1 has no eye: what the light gives is what is seen).
        /// -0.2 is the old grade's, kept so the street reads as it did.
        /// </summary>
        public const float Exposure = -0.2f;

        const string ReportPath = "Build/Lighting/relight.txt";

        /// <summary>The batch entry: the saved desert and its districts relit and saved.</summary>
        public static void BatchRelight()
        {
            FPSKitQualityTiers.Ensure();     // the tiers as the rebuild has them (no probe volumes, no SSAO yet)
            var report = Relight(FPSKitTownGI.DesertScene);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ReportPath));
            System.IO.File.WriteAllText(ReportPath, report);
            Debug.Log("[FPSKit] desert relit:\n" + report);
        }

        /// <summary>Opens the desert at <paramref name="path"/> with its room districts, strips every lighting layer, builds the
        /// approved ones and saves them all. Returns the report.</summary>
        public static string Relight(string path)
        {
            var sb = new StringBuilder();
            var main = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var scenes = new List<Scene> { main };
            for (int k = 0; k < FPSKitTownRoomScenes.Count; k++)
            {
                string district = FPSKitTownRoomScenes.ScenePath(k);
                if (System.IO.File.Exists(district)) scenes.Add(EditorSceneManager.OpenScene(district, OpenSceneMode.Additive));
            }
            sb.AppendLine($"desert lighting, layers 1..{Layers}; scenes: {string.Join(", ", scenes.Select(s => s.name))}");

            Strip(scenes, sb);
            SunAndSky(main, sb);
            Post(sb);
            Lamps(scenes, sb);
            ReportLights(scenes, sb);

            foreach (var s in scenes) { EditorSceneManager.MarkSceneDirty(s); EditorSceneManager.SaveScene(s); }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        /// <summary>Every lighting layer out: what is removed is counted in the report.</summary>
        static void Strip(List<Scene> scenes, StringBuilder sb)
        {
            var counts = new SortedDictionary<string, int>();
            void Count(string what) => counts[what] = counts.TryGetValue(what, out int n) ? n + 1 : 1;
            List<T> All<T>() where T : Component
            {
                var list = new List<T>();
                foreach (var s in scenes) foreach (var root in s.GetRootGameObjects()) list.AddRange(root.GetComponentsInChildren<T>(true));
                return list;
            }
            // A component goes; its object too when nothing else is on it.
            void Remove(Component c, string what)
            {
                if (c == null) return;
                Count(what);
                var go = c.gameObject;
                bool alone = go.GetComponents<Component>().Length == 2 && go.transform.childCount == 0;
                if (alone) Object.DestroyImmediate(go); else Object.DestroyImmediate(c);
            }

            foreach (var l in All<Light>()) if (l != null && l.name == "Bounce Light") { Count("bounce light"); Object.DestroyImmediate(l.gameObject); }
            // This file's own layers from an earlier run (they are made again below).
            foreach (var a in All<LampSet>()) if (a != null) { Count("lamp sets (earlier run)"); Object.DestroyImmediate(a.gameObject); }
            foreach (var c in All<CloudShadows>()) Remove(c, "cloud shadows");
            foreach (var e in All<EyeAdaptation>()) if (e != null) { Count("eye adaptation"); Object.DestroyImmediate(e.gameObject); }
            foreach (var r in All<IndoorReflections>()) Remove(r, "indoor reflections");
            foreach (var p in All<ReflectionProbe>()) Remove(p, "reflection probes");
            foreach (var g in All<LightProbeGroup>()) Remove(g, "light probe groups");
            foreach (var v in All<ProbeVolume>()) Remove(v, "probe volumes");
            foreach (var d in All<ProbeVolumePerSceneData>()) Remove(d, "probe volume scene data");
            // Volumes other than the desert's one global volume (a probe-light or a grade volume left by an older pass).
            foreach (var v in All<Volume>())
                if (v != null && v.name != "Global Volume") Remove(v, $"extra volumes ({v.name})");

            // Lightmaps and baked probes: each scene's lighting data unhooked, and every renderer off its lightmap.
            int lightmapped = 0;
            foreach (var s in scenes)
            {
                SceneManager.SetActiveScene(s);
                if (Lightmapping.lightingDataAsset != null) { Count("lighting data assets"); Lightmapping.lightingDataAsset = null; }
                foreach (var root in s.GetRootGameObjects())
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r.lightmapIndex < 0 || r.lightmapIndex >= 65534) continue;
                        r.lightmapIndex = -1;
                        r.lightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f);
                        lightmapped++;
                    }
            }
            SceneManager.SetActiveScene(scenes[0]);
            LightmapSettings.lightmaps = new LightmapData[0];
            if (lightmapped > 0) counts["renderers taken off a lightmap"] = lightmapped;

            sb.AppendLine("stripped:");
            foreach (var kv in counts) sb.AppendLine($"  {kv.Value} x {kv.Key}");
            if (counts.Count == 0) sb.AppendLine("  nothing (already stripped)");
        }

        // ------------------------------------------------------------------
        /// <summary>Layer 1: the theme's sun, sky ambient, sky reflection and fog. Nothing baked.</summary>
        static void SunAndSky(Scene main, StringBuilder sb)
        {
            SceneManager.SetActiveScene(main);
            var theme = AssetDatabase.FindAssets("t:LevelTheme").Select(g => AssetDatabase.LoadAssetAtPath<LevelTheme>(AssetDatabase.GUIDToAssetPath(g)))
                                     .FirstOrDefault(t => t != null && t.desertTownZone);
            var sun = main.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true))
                          .Where(l => l.type == LightType.Directional).OrderByDescending(l => l.intensity).FirstOrDefault();
            if (sun == null) { sb.AppendLine("layer 1: NO SUN FOUND"); return; }
            // The sun's angle stays as the town placed it (FPSKitSceneBuilder.AimTownSun lit its streets); the rest is the theme's.
            var aim = sun.transform.rotation;
            if (theme != null) theme.ApplyEnvironment(sun, null);
            sun.transform.rotation = aim;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1f;       // nothing else stands in for the light a shadow keeps
            sun.lightmapBakeType = LightmapBakeType.Realtime;
            RenderSettings.sun = sun;
            // The sky's reflection, from the sky itself, for everything (no probes).
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.ambientIntensity = 1f;
            DynamicGI.UpdateEnvironment();
            sb.AppendLine($"layer 1, sun and sky: sun {sun.name} {sun.intensity:0.00} {sun.color}, angles {sun.transform.eulerAngles}, shadows soft at 1; " +
                          $"ambient trilight sky {RenderSettings.ambientSkyColor} equator {RenderSettings.ambientEquatorColor} ground {RenderSettings.ambientGroundColor}; " +
                          $"fog {(RenderSettings.fog ? $"{RenderSettings.fogDensity:0.0000}" : "off")}; reflection: the sky; theme {(theme != null ? theme.name : "NOT FOUND")}");
        }

        /// <summary>The desert's one global volume: tonemapping and a fixed exposure, a little bloom; no grade, vignette or grain.</summary>
        static void Post(StringBuilder sb)
        {
            const string path = "Assets/FPSKit_Generated/PostFX_DesertOutpost.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) { sb.AppendLine("post: NO PROFILE at " + path); return; }
            for (int i = profile.components.Count - 1; i >= 0; i--)
            {
                var c = profile.components[i];
                profile.components.RemoveAt(i);
                Object.DestroyImmediate(c, true);
            }
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var adjust = profile.Add<ColorAdjustments>(true);
            adjust.postExposure.Override(Exposure);
            adjust.contrast.Override(0f);
            adjust.saturation.Override(0f);
            adjust.colorFilter.Override(Color.white);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.3f);
            bloom.threshold.Override(1.1f);
            bloom.scatter.Override(0.6f);
            foreach (var c in profile.components) { c.name = c.GetType().Name; if (!AssetDatabase.IsSubAsset(c)) AssetDatabase.AddObjectToAsset(c, profile); }
            EditorUtility.SetDirty(profile);
            sb.AppendLine($"post: tonemapping ACES, exposure fixed at {Exposure:0.0}, bloom 0.3 over 1.1; grade, vignette and grain removed");
        }

        // ------------------------------------------------------------------
        // ------------------------------------------------------------------
        /// <summary>A house's round ceiling lamp: a warm bulb under an opal shade.</summary>
        static readonly Color LampColour = new Color(1f, 0.82f, 0.62f);
        const float LampIntensity = 2.2f, LampRange = 5f;
        /// <summary>A shop's tube: cool white, a little brighter, reaching the floor and the back wall.</summary>
        static readonly Color TubeColour = new Color(0.92f, 0.96f, 1f);
        const float TubeIntensity = 2.0f, TubeRange = 5.5f;

        /// <summary>Layer 2: a real light at every lamp and tube fitting (found by material in the room meshes), the fittings glowing.</summary>
        static void Lamps(List<Scene> scenes, StringBuilder sb)
        {
            int lamps = 0, tubes = 0, unreadable = 0;
            foreach (var scene in scenes)
            {
                var lights = new List<Light>();
                Transform holder = null;
                foreach (var mr in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true)))
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    var mesh = mf != null ? mf.sharedMesh : null;
                    if (mesh == null) continue;
                    var mats = mr.sharedMaterials;
                    if (!mesh.isReadable)
                    {
                        if (mats.Any(m => m != null && (m.name.EndsWith("CeilingLampOff") || m.name.EndsWith("ShopTube")))) unreadable++;
                        continue;
                    }
                    for (int s = 0; s < mats.Length && s < mesh.subMeshCount; s++)
                    {
                        if (mats[s] == null) continue;
                        // The town's materials are saved as Town_<name> (FPSKitTownGround.TownFlat): matched by the end of the name.
                        bool lamp = mats[s].name.EndsWith("CeilingLampOff"), tube = mats[s].name.EndsWith("ShopTube");
                        if (!lamp && !tube) continue;
                        if (holder == null) { var h = new GameObject("Lamps"); SceneManager.MoveGameObjectToScene(h, scene); holder = h.transform; }
                        foreach (var (centre, size) in Fittings(mesh, s, mr.transform, lamp ? 0.5f : 0.9f))
                        {
                            var go = new GameObject(lamp ? "Lamp" : "Tube");
                            go.transform.SetParent(holder, false);
                            go.transform.position = centre - Vector3.up * (lamp ? 0.12f : 0.08f);
                            var l = go.AddComponent<Light>();
                            l.type = LightType.Point;
                            l.color = lamp ? LampColour : TubeColour;
                            l.intensity = lamp ? LampIntensity : TubeIntensity;
                            l.range = lamp ? LampRange : TubeRange;
                            l.shadows = LightShadows.None;
                            l.lightmapBakeType = LightmapBakeType.Realtime;
                            l.enabled = false;           // LampSet lights the nearest
                            lights.Add(l);
                            if (lamp) lamps++; else tubes++;
                        }
                    }
                }
                if (holder != null) holder.gameObject.AddComponent<LampSet>().Set(lights.ToArray());
            }
            // The fittings glow, as lit lamps do (the "off" shade was a lamp that lit nothing).
            var shade = AssetDatabase.FindAssets("CeilingLampOff t:Material").Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(m => m != null && m.name.EndsWith("CeilingLampOff"));
            if (shade != null)
            {
                shade.EnableKeyword("_EMISSION");
                shade.SetColor("_EmissionColor", LampColour * 1.6f);
                shade.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                EditorUtility.SetDirty(shade);
            }
            sb.AppendLine($"layer 2, real lamps: {lamps} round ceiling lamps ({LampIntensity} warm, {LampRange} m), {tubes} shop tubes ({TubeIntensity} cool, {TubeRange} m), " +
                          $"no shadows; at most {LampSet.MaxOn} lit at once within {LampSet.Radius:0} m" +
                          (unreadable > 0 ? $"; {unreadable} lamp meshes NOT READABLE (no lights for them)" : ""));
            if (lamps + tubes == 0) sb.AppendLine("layer 2: NO LAMPS FOUND -- check the fittings' material names");
        }

        /// <summary>The fittings drawn by one submesh: its triangles grouped by nearness (within <paramref name="join"/> m), each group's
        /// middle and size, in world space.</summary>
        static List<(Vector3 centre, Vector3 size)> Fittings(Mesh mesh, int sub, Transform t, float join)
        {
            var verts = mesh.vertices;
            var tris = mesh.GetTriangles(sub);
            var cells = new Dictionary<Vector3Int, List<Vector3>>();
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                var c = t.TransformPoint((verts[tris[i]] + verts[tris[i + 1]] + verts[tris[i + 2]]) / 3f);
                var key = new Vector3Int(Mathf.FloorToInt(c.x / join), Mathf.FloorToInt(c.y / join), Mathf.FloorToInt(c.z / join));
                if (!cells.TryGetValue(key, out var l)) cells[key] = l = new List<Vector3>();
                l.Add(c);
            }
            // Cells touching each other are one fitting (a tube runs across several).
            var seen = new HashSet<Vector3Int>();
            var result = new List<(Vector3, Vector3)>();
            foreach (var start in cells.Keys)
            {
                if (!seen.Add(start)) continue;
                var points = new List<Vector3>();
                var stack = new Stack<Vector3Int>(); stack.Push(start);
                while (stack.Count > 0)
                {
                    var k = stack.Pop();
                    points.AddRange(cells[k]);
                    for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                    {
                        var n = new Vector3Int(k.x + x, k.y + y, k.z + z);
                        if (cells.ContainsKey(n) && seen.Add(n)) stack.Push(n);
                    }
                }
                var b = new Bounds(points[0], Vector3.zero);
                foreach (var p in points) b.Encapsulate(p);
                result.Add((b.center, b.size));
            }
            return result;
        }

        /// <summary>Every light still in the desert (lamps, signs, fires): the next layers are planned from this list.</summary>
        static void ReportLights(List<Scene> scenes, StringBuilder sb)
        {
            var lights = scenes.SelectMany(s => s.GetRootGameObjects()).SelectMany(r => r.GetComponentsInChildren<Light>(true)).ToList();
            sb.AppendLine($"lights left: {lights.Count}");
            foreach (var g in lights.GroupBy(l => $"{l.type} {(l.enabled && l.gameObject.activeInHierarchy ? "on" : "off")} {l.lightmapBakeType} shadows {l.shadows} under {Top(l.transform)}")
                                    .OrderByDescending(g => g.Count()))
                sb.AppendLine($"  {g.Count()} x {g.Key} (e.g. {g.First().name}, intensity {g.First().intensity:0.00}, range {g.First().range:0.0})");
            string Top(Transform t) { while (t.parent != null && t.parent.parent != null) t = t.parent; return t.name; }
        }
    }
}
#endif
