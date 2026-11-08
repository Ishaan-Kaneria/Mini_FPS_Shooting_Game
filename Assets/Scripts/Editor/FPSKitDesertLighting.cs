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
    ///      nothing is baked.
    ///
    /// Later layers (each added only once he has seen the one before): rooms darker than the street, real lamps (shop tube
    /// lights, round room lamps) that light what is around them, a gentle eye, matte materials; the bake comes last.
    ///
    /// Run on the saved scenes, no rebuild: <c>Tools/unity-batch.sh FPSKitBatch.RelightDesert</c>. The desert's build calls it
    /// too, so a rebuilt desert comes out the same. Its report is Build/Lighting/relight.txt.
    /// </summary>
    public static class FPSKitDesertLighting
    {
        /// <summary>The layers built so far (1 = sun and sky).</summary>
        public const int Layers = 1;

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

            foreach (var l in All<Light>()) if (l != null && l.name == "Bounce Light") Remove(l, "bounce light");
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
