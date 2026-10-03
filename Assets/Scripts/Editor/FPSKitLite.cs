#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Makes a lightweight working copy of whichever arena scene is open, for a laptop whose cooling cannot keep up
    /// with the real thing (the Fairground is 4,857 renderers and 3.9 M triangles across its LODs; the editor on this
    /// machine has hit 110 C and been shut down by it).
    ///
    /// The copy is saved beside the original in <c>Scenes/Lite</c> and the original is never modified: the scene is saved
    /// as a copy first and the copy is the one opened and reduced. Everything reduced is visual; colliders, the navigation
    /// bake, spawn points and the level logic are untouched, so the copy still plays. Shared assets are never written:
    /// the post-processing profile is cloned before it is edited.
    ///
    /// What it does: no shadow casters and no camera shadow pass, the strongest dozen lights only and none with shadows,
    /// particles off, vegetation and tiny props thinned, LODs that drop sooner, a shorter far plane, the expensive post
    /// effects off, and a <see cref="LiteSceneSettings"/> that caps the frame rate and quarters texture size while playing.
    /// </summary>
    public static class FPSKitLite
    {
        const string Folder = "Assets/FPSKit_Generated/Scenes/Lite";
        const string ProfileFolder = Folder + "/Profiles";

        [MenuItem("FPSKit/Lite/Make Lite Copy of Open Scene")]
        static void Menu() => Debug.Log("[FPSKit] " + MakeLiteCopy());

        public static string MakeLiteCopy()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) return "Open a saved arena scene first.";
            if (scene.name.EndsWith("_Lite")) return "This is already a Lite scene.";
            if (scene.isDirty) return "The open scene has unsaved changes: save or revert it first (the copy is made from what is open).";

            Ensure("Assets/FPSKit_Generated/Scenes", "Lite");
            Ensure(Folder, "Profiles");

            string original = scene.name;
            string path = $"{Folder}/{original}_Lite.unity";
            if (!EditorSceneManager.SaveScene(scene, path, true)) return "Could not write " + path;
            var lite = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            var sb = new StringBuilder();
            sb.AppendLine($"Lite copy of {original}: {path}");
            Reduce(lite, sb);

            EditorSceneManager.MarkSceneDirty(lite);
            EditorSceneManager.SaveScene(lite);
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        static void Ensure(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{child}")) AssetDatabase.CreateFolder(parent, child);
        }

        static void Reduce(UnityEngine.SceneManagement.Scene scene, StringBuilder sb)
        {
            // ---- shadows: casters off, receivers off (no pass to receive from), camera pass off ----
            int casters = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r is ParticleSystemRenderer) continue;
                if (r.shadowCastingMode != ShadowCastingMode.Off) casters++;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            sb.AppendLine($"  shadow casters switched off: {casters}");

            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                cam.farClipPlane = Mathf.Min(cam.farClipPlane, 160f);
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null) { data.renderShadows = false; data.antialiasing = AntialiasingMode.None; }
            }

            // ---- lights: the strongest dozen, none with shadows ----
            var lights = new List<Light>(Object.FindObjectsByType<Light>(FindObjectsSortMode.None));
            int offLights = 0;
            lights.RemoveAll(l => l.type == LightType.Directional);
            lights.Sort((a, b) => (b.intensity * b.range).CompareTo(a.intensity * a.range));
            for (int i = 0; i < lights.Count; i++)
            {
                lights[i].shadows = LightShadows.None;
                if (i >= 12 && lights[i].enabled) { lights[i].enabled = false; offLights++; }
            }
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) l.shadows = LightShadows.None;
            sb.AppendLine($"  lights: {lights.Count} point/spot, {offLights} switched off, shadows removed from all");

            // ---- the tier rigs would turn lights back on or move the shared shadow range: remove them from the copy ----
            int rigs = 0;
            foreach (var q in Object.FindObjectsByType<RooftopQuality>(FindObjectsSortMode.None)) { Object.DestroyImmediate(q.gameObject); rigs++; }
            foreach (var q in Object.FindObjectsByType<FairgroundQuality>(FindObjectsSortMode.None)) { Object.DestroyImmediate(q.gameObject); rigs++; }
            sb.AppendLine($"  tier rigs removed: {rigs}");

            // ---- particles off ----
            int ps = 0;
            foreach (var p in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) { p.gameObject.SetActive(false); ps++; }
            sb.AppendLine($"  particle systems switched off: {ps}");

            // ---- thinning: trees and tiny props, deterministic ----
            int removed = 0, kept = 0, i2 = 0;
            var doomed = new List<GameObject>();
            foreach (var t in Object.FindObjectsByType<Tree>(FindObjectsSortMode.None))
                if (i2++ % 20 < 11) doomed.Add(t.gameObject); else kept++;
            int j = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r.GetComponentInParent<Tree>() != null || r.GetComponentInParent<LODGroup>() != null || doomed.Contains(r.gameObject)) continue;
                if (r.bounds.size.magnitude < 0.8f && j++ % 2 == 0) doomed.Add(r.gameObject);
            }
            foreach (var g in doomed) if (g != null) { Object.DestroyImmediate(g); removed++; }
            sb.AppendLine($"  trees and tiny props removed: {removed} (trees kept {kept})");

            // ---- clutter: every other LOD-grouped prop, rock, fence and bush (the Fairground's tables, cabinets, benches ...) ----
            string[] clutter = { "Prop_", "CobbleRock", "MossBoulder", "Struct_Fence", "DecoBush", "Grass" };
            int cn = 0, clutterRemoved = 0;
            var clutterDoomed = new List<GameObject>();
            foreach (var lg in Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
            {
                bool hit = false;
                foreach (var pre in clutter) if (lg.name.StartsWith(pre)) { hit = true; break; }
                if (hit && cn++ % 2 == 0) clutterDoomed.Add(lg.gameObject);
            }
            foreach (var g in clutterDoomed) if (g != null) { Object.DestroyImmediate(g); clutterRemoved++; }
            sb.AppendLine($"  clutter groups removed: {clutterRemoved}");

            // ---- LODs drop sooner ----
            int lodGroups = 0;
            foreach (var lg in Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
            {
                var lods = lg.GetLODs();
                for (int i = 0; i < lods.Length; i++) lods[i].screenRelativeTransitionHeight = Mathf.Min(0.95f, lods[i].screenRelativeTransitionHeight * 1.8f);
                lg.SetLODs(lods);
                lg.fadeMode = LODFadeMode.None;
                lodGroups++;
            }
            sb.AppendLine($"  LOD groups retuned: {lodGroups}");

            // ---- terrain: the component's own distances (the terrain asset is shared and is left alone) ----
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                t.treeBillboardDistance = 50f; t.treeMaximumFullLODCount = 20; t.treeDistance = 90f;
                t.detailObjectDistance = 25f; t.detailObjectDensity = 0.3f;
                t.heightmapPixelError = 30f; t.basemapDistance = 80f;
            }

            // ---- post-processing: clone each profile, then switch the expensive effects off in the clone ----
            int vols = 0;
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (v.sharedProfile == null) continue;
                // A real copy: Instantiate would share the component objects with the original profile.
                var clone = ScriptableObject.CreateInstance<VolumeProfile>();
                clone.name = $"{scene.name}_{v.name}_{vols}";
                AssetDatabase.CreateAsset(clone, $"{ProfileFolder}/{clone.name}.asset");
                foreach (var c in v.sharedProfile.components)
                {
                    var nc = (VolumeComponent)ScriptableObject.CreateInstance(c.GetType());
                    EditorUtility.CopySerialized(c, nc);
                    nc.name = c.name;
                    string n = c.GetType().Name;
                    if (n == "DepthOfField" || n == "MotionBlur" || n == "FilmGrain" || n == "ChromaticAberration" || n == "LensDistortion" || n == "PaniniProjection")
                        nc.active = false;
                    AssetDatabase.AddObjectToAsset(nc, clone);
                    clone.components.Add(nc);
                }
                EditorUtility.SetDirty(clone);
                v.sharedProfile = clone;
                vols++;
            }
            sb.AppendLine($"  volume profiles cloned and trimmed: {vols}");

            // ---- the runtime cap ----
            new GameObject("LiteSceneSettings").AddComponent<LiteSceneSettings>();
            sb.AppendLine("  LiteSceneSettings added (30 fps cap, textures at quarter size while playing)");
        }
    }
}
#endif
