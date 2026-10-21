#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The three quality tiers the player chooses between: Low, Medium, High.
    ///
    /// Each is a quality level with its own URP asset, so the difference between them is
    /// fixed on disk rather than applied to a shared asset at runtime -- a pipeline asset
    /// edited while the game runs in the editor stays edited, and the next build ships it.
    ///
    ///   Low    -- Mobile_RPAsset: no real-time shadows, no MSAA, no HDR. Full render scale:
    ///             the page sets the pixel budget, and 80% here was the "very blurry" build.
    ///             The phone and browser default.
    ///   Medium -- Medium_RPAsset (made here from the PC asset): shadows to 60m on two
    ///             cascades, 2x MSAA, full resolution, and the cheaper renderer, with no
    ///             ambient occlusion.
    ///   High   -- PC_RPAsset: shadows to 50m on three cascades at 4096, 2x MSAA, rendered at 85% and upscaled with
    ///             FSR 1, and ambient occlusion at half resolution with eight samples and a bilateral blur. Measured on
    ///             the Intel Arc laptop: MSAA 4x -> 2x and the SSAO settings are worth 1-2 ms each; the full SSAO pass
    ///             was ~4.7 ms.
    ///
    /// Post-processing and particles are cut at runtime by <see cref="QualityTiers"/>,
    /// because they belong to the camera and the scene rather than to the pipeline.
    ///
    /// <b>No tier is excluded from any platform.</b> The template shipped the PC level
    /// excluded from Android and iOS, which on a phone removes it from
    /// <c>QualitySettings.names</c> altogether -- a tablet that could run High would have
    /// had no way to ask for it.
    /// </summary>
    public static class FPSKitQualityTiers
    {
        const string LowAsset = "Assets/Settings/Mobile_RPAsset.asset";
        const string MediumAsset = "Assets/Settings/Medium_RPAsset.asset";
        const string HighAsset = "Assets/Settings/PC_RPAsset.asset";
        const string LowRenderer = "Assets/Settings/Mobile_Renderer.asset";

        public static readonly string[] Names = { "Low", "Medium", "High" };

        /// <summary>Whether a pipeline asset is one of the three tiers, which only <see cref="Ensure"/> may write.</summary>
        public static bool IsTierAsset(string path) => path == LowAsset || path == MediumAsset || path == HighAsset;

        /// <summary>Makes the three levels exist and be right. Idempotent; returns whether anything changed.</summary>
        public static bool Ensure()
        {
            bool changed = false;
            var low = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(LowAsset);
            var high = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HighAsset);
            if (low == null || high == null)
            {
                Debug.LogWarning("[FPSKit] quality tiers: Mobile_RPAsset or PC_RPAsset is missing; tiers left as they are.");
                return false;
            }

            var medium = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MediumAsset);
            if (medium == null)
            {
                AssetDatabase.CopyAsset(HighAsset, MediumAsset);
                medium = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MediumAsset);
                changed = true;
            }

            // Shadow maps: 4096 on High over its 50 m (a texel about 1.5 cm in the nearest cascade), 2048 on Medium.
            changed |= Configure(high, shadows: true, distance: 50f, cascades: 3, msaa: 2, hdr: true, scale: 0.85f, soft: true,
                                 renderer: null, upscaling: 3, shadowResolution: 4096,        // 3 = FSR 1
                                 additionalShadows: true);                                    // the player's muzzle flash
            changed |= Ssao(high);
            changed |= Configure(low, shadows: false, distance: 0f, cascades: 1, msaa: 1, hdr: false, scale: 1f, soft: false, renderer: null);
            changed |= Configure(medium, shadows: true, distance: 60f, cascades: 2, msaa: 2, hdr: true, scale: 1f, soft: false,
                                 renderer: AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(LowRenderer), shadowResolution: 2048);
            changed |= Levels(low, medium, high);
            if (changed) AssetDatabase.SaveAssets();
            return changed;
        }

        static bool Configure(UniversalRenderPipelineAsset asset, bool shadows, float distance, int cascades,
                              int msaa, bool hdr, float scale, bool soft, ScriptableRendererData renderer, int upscaling = 0,
                              int shadowResolution = 0, bool additionalShadows = false)
        {
            var so = new SerializedObject(asset);
            bool changed = false;
            changed |= SetBool(so, "m_MainLightShadowsSupported", shadows);
            // Point and spot shadows: High only, for the one light that asks for them (the player's muzzle flash,
            // FPSMuzzleFlashLight, at the lowest per-light tier in the 2048 atlas). Off elsewhere: the flash still lights.
            changed |= SetBool(so, "m_AdditionalLightShadowsSupported", additionalShadows);
            if (additionalShadows) changed |= SetInt(so, "m_AdditionalLightsShadowmapResolution", 2048);
            // Reflection probes blend across their edges (and box-project) on every tier, so a car or a window passing
            // from one street probe to the next fades between them instead of snapping.
            changed |= SetBool(so, "m_ReflectionProbeBlending", true);
            changed |= SetBool(so, "m_ReflectionProbeBoxProjection", true);
            if (shadows)
            {
                changed |= SetFloat(so, "m_ShadowDistance", distance);
                changed |= SetInt(so, "m_ShadowCascadeCount", cascades);
                if (shadowResolution > 0) changed |= SetInt(so, "m_MainLightShadowmapResolution", shadowResolution);
            }
            changed |= SetInt(so, "m_MSAA", msaa);
            changed |= SetBool(so, "m_SupportsHDR", hdr);
            changed |= SetFloat(so, "m_RenderScale", scale);
            changed |= SetBool(so, "m_SoftShadowsSupported", soft);
            changed |= SetInt(so, "m_UpscalingFilter", upscaling);
            if (renderer != null)
            {
                var list = so.FindProperty("m_RendererDataList");
                if (list != null && list.isArray)
                {
                    if (list.arraySize != 1) { list.arraySize = 1; changed = true; }
                    var el = list.GetArrayElementAtIndex(0);
                    if (el.objectReferenceValue != renderer) { el.objectReferenceValue = renderer; changed = true; }
                    var def = so.FindProperty("m_DefaultRendererIndex");
                    if (def != null && def.intValue != 0) { def.intValue = 0; changed = true; }
                }
            }
            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
            return changed;
        }

        /// <summary>
        /// SSAO on the High renderer: half resolution, eight samples, the edge-preserving (bilateral) blur, from depth and
        /// normals. The feature keeps its settings in an internal class, so they are written through the serialized
        /// path, which also re-runs the feature's Create().
        /// </summary>
        static bool Ssao(UniversalRenderPipelineAsset high)
        {
            var list = new SerializedObject(high).FindProperty("m_RendererDataList");
            bool changed = false;
            for (int i = 0; list != null && i < list.arraySize; i++)
            {
                var data = list.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
                if (data == null) continue;
                foreach (var f in data.rendererFeatures)
                {
                    if (f == null || f.name.IndexOf("Occlusion", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var so = new SerializedObject(f);
                    var m = so.FindProperty("m_Settings");
                    if (m == null) continue;
                    bool c = false;
                    // History: on 2026-10-03, at four samples with a Gaussian blur, it was a dithered black grain on the old
                    // desert's rounded mud brick ("black grime") and was switched off. On again (2026-10-21, asked for, to
                    // ground the town's walls where they meet the road): eight samples and the bilateral blur, which
                    // keeps the occlusion off the far side of an edge, a radius that reaches the foot of a wall, and a
                    // moderate strength. Half resolution keeps the pass about 2 ms on the laptop.
                    c |= SetBool(m.FindPropertyRelative("Downsample"), true);
                    c |= SetInt(m.FindPropertyRelative("Samples"), 1);        // Medium = 8 samples
                    c |= SetInt(m.FindPropertyRelative("BlurQuality"), 0);    // High = bilateral
                    c |= SetInt(m.FindPropertyRelative("Source"), 1);         // depth and normals
                    c |= SetFloat(m.FindPropertyRelative("Intensity"), 0.65f);
                    c |= SetFloat(m.FindPropertyRelative("DirectLightingStrength"), 0.25f);
                    c |= SetFloat(m.FindPropertyRelative("Radius"), 0.35f);
                    c |= SetBool(so.FindProperty("m_Active"), true);
                    if (c) { so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(f); EditorUtility.SetDirty(data); changed = true; }
                }
            }
            return changed;
        }

        static bool SetFloat(SerializedProperty p, float v) { if (p == null || Mathf.Approximately(p.floatValue, v)) return false; p.floatValue = v; return true; }
        static bool SetBool(SerializedProperty p, bool v) { if (p == null || p.boolValue == v) return false; p.boolValue = v; return true; }
        static bool SetInt(SerializedProperty p, int v) { if (p == null || p.intValue == v) return false; p.intValue = v; return true; }

        /// <summary>Rewrites QualitySettings to exactly Low, Medium, High, in that order.</summary>
        static bool Levels(RenderPipelineAsset low, RenderPipelineAsset medium, RenderPipelineAsset high)
        {
            var qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (qs == null || qs.Length == 0) return false;
            var so = new SerializedObject(qs[0]);
            var levels = so.FindProperty("m_QualitySettings");
            if (levels == null || !levels.isArray || levels.arraySize == 0) return false;

            bool already = levels.arraySize == 3;
            for (int i = 0; already && i < 3; i++)
            {
                var l = levels.GetArrayElementAtIndex(i);
                already &= l.FindPropertyRelative("name").stringValue == Names[i]
                           && l.FindPropertyRelative("customRenderPipeline").objectReferenceValue == new[] { low, medium, high }[i]
                           && l.FindPropertyRelative("excludedTargetPlatforms").arraySize == 0;
            }
            if (already) return false;

            // Build from the existing entries so every other field keeps the template's
            // values: the old "Mobile" is the base for Low, the old "PC" for Medium and High.
            int lowIndex = 0, highIndex = levels.arraySize - 1;
            for (int i = 0; i < levels.arraySize; i++)
            {
                string n = levels.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue;
                if (n == "Mobile" || n == "Low") lowIndex = i;
                if (n == "PC" || n == "High") highIndex = i;
            }
            // Order the sources Low, High and drop anything else.
            for (int i = levels.arraySize - 1; i >= 0; i--)
                if (i != lowIndex && i != highIndex) levels.DeleteArrayElementAtIndex(i);
            if (levels.arraySize == 2 && lowIndex > highIndex) levels.MoveArrayElement(1, 0);
            if (levels.arraySize == 1) levels.InsertArrayElementAtIndex(0);
            // Medium is a copy of High, inserted between.
            levels.InsertArrayElementAtIndex(1);
            levels.MoveArrayElement(2, 1);

            var assets = new[] { low, medium, high };
            for (int i = 0; i < 3; i++)
            {
                var l = levels.GetArrayElementAtIndex(i);
                l.FindPropertyRelative("name").stringValue = Names[i];
                l.FindPropertyRelative("customRenderPipeline").objectReferenceValue = assets[i];
                l.FindPropertyRelative("excludedTargetPlatforms").arraySize = 0;
            }

            // First-launch defaults per platform. QualityTiers makes the same call at
            // runtime and saves it; these are what a build boots into before it does.
            var defaults = so.FindProperty("m_PerPlatformDefaultQuality");
            if (defaults != null)
                for (int i = 0; i < defaults.arraySize; i++)
                {
                    var pair = defaults.GetArrayElementAtIndex(i);
                    string platform = pair.FindPropertyRelative("first").stringValue;
                    pair.FindPropertyRelative("second").intValue = platform == "Standalone" ? 2 : 0;
                }

            so.FindProperty("m_CurrentQuality").intValue = 2;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[FPSKit] quality tiers: Low, Medium, High.");
            return true;
        }

        static bool SetBool(SerializedObject so, string name, bool v)
        {
            var p = so.FindProperty(name);
            if (p == null || p.boolValue == v) return false;
            p.boolValue = v;
            return true;
        }

        static bool SetInt(SerializedObject so, string name, int v)
        {
            var p = so.FindProperty(name);
            if (p == null || p.intValue == v) return false;
            p.intValue = v;
            return true;
        }

        static bool SetFloat(SerializedObject so, string name, float v)
        {
            var p = so.FindProperty(name);
            if (p == null || Mathf.Approximately(p.floatValue, v)) return false;
            p.floatValue = v;
            return true;
        }
    }
}
#endif
