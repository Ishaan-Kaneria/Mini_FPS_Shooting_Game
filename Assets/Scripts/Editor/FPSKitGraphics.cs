#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Render settings the scene builder cannot reach, because they live on the render
    /// pipeline assets rather than in any scene.
    ///
    /// The builder calls this the same way it calls EnsureProjectTagsAndLayers: a scene
    /// is only as good as the pipeline drawing it, and a generated arena that assumes
    /// shadows reach across it looks broken when they stop at the halfway line. Running
    /// it from a scene build does mean a build touches project-wide assets, which is why
    /// every change here is idempotent and why the menu item exists to run it alone.
    ///
    /// Two things are deliberately left alone:
    ///
    ///  - Anything named Mobile. That tier exists to be cheap, and quietly enabling MSAA
    ///    and ambient occlusion on it would undo the reason it is a separate asset.
    ///  - Any setting already stronger than what is asked for here. The values are floors,
    ///    not assignments, so a project that has tuned past them keeps its tuning.
    /// </summary>
    public static class FPSKitGraphics
    {
        // ==================================================================
        // Targets
        // ==================================================================

        /// <summary>
        /// The arena is 110 units across by default and shadows were stopping at 50, so
        /// everything past the middle of the map sat in flat ambient light. This covers
        /// the whole playable area with a little to spare.
        /// </summary>
        private const float MinimumShadowDistance = 95f;

        /// <summary>
        /// Four cascades over ~95 metres, because two cascades stretched that far put
        /// visible stair-stepping on the shadow of anything close to the player.
        /// </summary>
        private const int TargetCascades = 4;

        /// <summary>
        /// The single biggest visual difference in a scene made of primitives: every
        /// object here is hard edges and flat colour, which is exactly what aliases
        /// worst. SMAA on the camera softens what it can see; MSAA fixes the geometry
        /// itself.
        /// </summary>
        private const int TargetMsaa = 4;

        /// <summary>
        /// Ambient occlusion intensity. The project shipped with 0.4, which is nearly
        /// invisible on untextured geometry -- and untextured geometry is precisely the
        /// case that needs it, since contact shadows are the only thing telling you a
        /// crate is resting on the floor rather than hovering over it.
        /// </summary>
        private const float AmbientOcclusionIntensity = 1.25f;

        /// <summary>
        /// In metres. Small enough to read as contact shadow in corners rather than a
        /// grey halo around everything, which is what large radii look like at this
        /// object scale.
        /// </summary>
        private const float AmbientOcclusionRadius = 0.28f;

        // ==================================================================
        // Entry points
        // ==================================================================

        [MenuItem("FPSKit/Graphics/Apply Quality Settings", false, 80)]
        /// <summary>
        /// Locks handheld builds to landscape.
        ///
        /// This is a first-person shooter with a thumbstick on the left and a fire
        /// button on the right; in portrait the two thumbs overlap the middle of the
        /// screen and there is nothing to aim at. It applies to Android and iOS players
        /// only -- WebGL ignores it entirely, because in a browser the page owns the
        /// viewport, which is why the template asks the player to turn the device and
        /// takes an orientation lock when one is on offer.
        /// </summary>
        public static void ApplyOrientation()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        }

        public static void ApplyFromMenu()
        {
            int changed = Apply();

            EditorUtility.DisplayDialog(
                "FPSKit Graphics",
                changed == 0
                    ? "Every render pipeline asset already meets the kit's settings. Nothing changed."
                    : $"Updated {changed} render pipeline asset(s).\n\nShadow distance, cascades, MSAA and " +
                      "ambient occlusion now match what the generated scenes expect.",
                "OK");
        }

        /// <summary>
        /// Brings every render pipeline asset in the project up to the kit's settings.
        /// Returns how many assets were actually changed, so a caller can stay silent
        /// when there was nothing to do.
        /// </summary>
        public static int Apply()
        {
            int changed = 0;

            foreach (var asset in ActivePipelineAssets())
            {
                bool lowEnd = IsLowEndTier(AssetDatabase.GetAssetPath(asset));

                if (ApplyToPipeline(asset, lowEnd)) changed++;
                if (lowEnd) continue;

                // Only the renderers this asset actually draws with. Sweeping every
                // UniversalRendererData in the project instead would quietly edit the
                // template's spare renderers, which nothing renders through -- churn in
                // the diff for no change on screen.
                foreach (var data in RendererDataOf(asset))
                    if (ApplyAmbientOcclusion(data)) changed++;
            }

            if (changed > 0) AssetDatabase.SaveAssets();
            return changed;
        }

        /// <summary>
        /// The pipeline assets the project renders with: the graphics default plus one
        /// per quality level. Deliberately not "every URP asset under Assets" -- a URP
        /// template ships spares, and editing an asset nobody renders through is a change
        /// that cannot be seen and cannot be verified.
        /// </summary>
        private static IEnumerable<UniversalRenderPipelineAsset> ActivePipelineAssets()
        {
            var seen = new HashSet<UniversalRenderPipelineAsset>();

            if (UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset fallback)
                seen.Add(fallback);

            for (int level = 0; level < QualitySettings.names.Length; level++)
                if (QualitySettings.GetRenderPipelineAssetAt(level) is UniversalRenderPipelineAsset perLevel)
                    seen.Add(perLevel);

            return seen;
        }

        /// <summary>
        /// The renderers an asset draws with. m_RendererDataList has no public accessor
        /// that survives every URP version, so it is read the same way everything else
        /// here is written.
        /// </summary>
        private static IEnumerable<ScriptableRendererData> RendererDataOf(UniversalRenderPipelineAsset asset)
        {
            var found = new List<ScriptableRendererData>();

            var list = new SerializedObject(asset).FindProperty("m_RendererDataList");
            if (list == null || !list.isArray) return found;

            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is ScriptableRendererData data)
                    found.Add(data);

            return found;
        }

        /// <summary>
        /// A tier that exists to be cheap. Matched on the asset path because that is how
        /// the URP project template names them, and because the alternative -- inspecting
        /// which build targets a quality level applies to -- is not exposed.
        /// </summary>
        private static bool IsLowEndTier(string assetPath)
            => assetPath.IndexOf("Mobile", System.StringComparison.OrdinalIgnoreCase) >= 0;

        // ==================================================================
        // Pipeline asset
        // ==================================================================

        /// <summary>
        /// Everything here goes through SerializedObject rather than the asset's own
        /// properties. Several of these have no public setter, and the serialized names
        /// are stable across the URP versions this kit runs on, so one mechanism covers
        /// all of them instead of half working and half not.
        /// </summary>
        private static bool ApplyToPipeline(UniversalRenderPipelineAsset asset, bool lowEnd)
        {
            var so = new SerializedObject(asset);
            bool changed = false;

            // Shadow reach matters on every tier: a shadow that stops mid-arena reads as
            // a bug, not as a quality setting.
            changed |= RaiseFloat(so, "m_ShadowDistance", MinimumShadowDistance);

            if (!lowEnd)
            {
                changed |= RaiseInt(so, "m_ShadowCascadeCount", TargetCascades);
                changed |= RaiseInt(so, "m_MSAA", TargetMsaa);
                changed |= SetBool(so, "m_SoftShadowsSupported", true);
                changed |= SetBool(so, "m_SupportsHDR", true);
            }

            if (changed)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(asset);
            }

            return changed;
        }

        // ==================================================================
        // Ambient occlusion
        // ==================================================================

        private static bool ApplyAmbientOcclusion(ScriptableRendererData data)
        {
            var feature = FindAmbientOcclusion(data);
            bool added = false;

            if (feature == null)
            {
                feature = AddAmbientOcclusion(data);
                if (feature == null) return false;
                added = true;
            }

            var so = new SerializedObject(feature);
            bool changed = added;

            // Set outright when we created the feature, because URP's own default is 3.0
            // and "raise to 1.25" would leave that untouched. An occlusion feature that
            // was already in the project is only ever raised, never turned down.
            changed |= added
                ? SetFloat(so, "m_Settings.Intensity", AmbientOcclusionIntensity)
                : RaiseFloat(so, "m_Settings.Intensity", AmbientOcclusionIntensity);
            changed |= SetFloat(so, "m_Settings.Radius", AmbientOcclusionRadius);

            // Before the transparent pass, so glass and effects are not darkened by the
            // occlusion of whatever is behind them.
            changed |= SetBool(so, "m_Settings.AfterOpaque", false);
            changed |= SetBool(so, "m_Settings.Downsample", false);

            if (changed)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(feature);
            }

            return changed;
        }

        private static ScriptableRendererFeature FindAmbientOcclusion(ScriptableRendererData data)
        {
            if (data.rendererFeatures == null) return null;

            foreach (var feature in data.rendererFeatures)
                if (feature is ScreenSpaceAmbientOcclusion) return feature;

            return null;
        }

        /// <summary>
        /// Adds the feature the way URP's own inspector does.
        ///
        /// A renderer feature is a sub-asset of the renderer, and the renderer keeps a
        /// parallel list of the sub-asset file IDs. Adding to rendererFeatures alone
        /// leaves that map short, and URP then drops the feature on the next reload --
        /// which looks exactly like the feature never being added at all.
        /// </summary>
        private static ScriptableRendererFeature AddAmbientOcclusion(ScriptableRendererData data)
        {
            var feature = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            feature.name = nameof(ScreenSpaceAmbientOcclusion);
            feature.hideFlags |= HideFlags.HideInHierarchy;

            AssetDatabase.AddObjectToAsset(feature, data);

            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId))
            {
                Object.DestroyImmediate(feature, true);
                Debug.LogWarning("[FPSKit Graphics] Could not resolve a file id for the new ambient " +
                                 "occlusion feature; leaving the renderer untouched.");
                return null;
            }

            var so = new SerializedObject(data);

            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            if (features == null || map == null)
            {
                Object.DestroyImmediate(feature, true);
                Debug.LogWarning("[FPSKit Graphics] This URP version does not expose m_RendererFeatures " +
                                 "as expected, so ambient occlusion was not added. Add it by hand on the " +
                                 "renderer asset.");
                return null;
            }

            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;

            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);

            return feature;
        }

        // ==================================================================
        // Serialized helpers
        //
        // Each returns whether it actually changed anything, so callers can avoid
        // dirtying assets that were already correct -- which is what keeps this safe to
        // run from every scene build.
        // ==================================================================

        private static bool RaiseFloat(SerializedObject so, string path, float minimum)
        {
            var property = so.FindProperty(path);
            if (property == null || property.floatValue >= minimum) return false;

            property.floatValue = minimum;
            return true;
        }

        private static bool SetFloat(SerializedObject so, string path, float value)
        {
            var property = so.FindProperty(path);
            if (property == null || Mathf.Approximately(property.floatValue, value)) return false;

            property.floatValue = value;
            return true;
        }

        private static bool RaiseInt(SerializedObject so, string path, int minimum)
        {
            var property = so.FindProperty(path);
            if (property == null || property.intValue >= minimum) return false;

            property.intValue = minimum;
            return true;
        }

        private static bool SetBool(SerializedObject so, string path, bool value)
        {
            var property = so.FindProperty(path);
            if (property == null) return false;

            // Some of these serialize as an int flag rather than a bool, so both shapes
            // are handled instead of assuming one and silently writing nothing.
            if (property.propertyType == SerializedPropertyType.Boolean)
            {
                if (property.boolValue == value) return false;
                property.boolValue = value;
                return true;
            }

            if (property.propertyType == SerializedPropertyType.Integer)
            {
                int target = value ? 1 : 0;
                if (property.intValue == target) return false;
                property.intValue = target;
                return true;
            }

            return false;
        }

        /// <summary>The assets <see cref="Apply"/> acts on, for reporting without acting.</summary>
        public static List<string> PipelineAssetPaths()
        {
            var paths = new List<string>();

            foreach (var asset in ActivePipelineAssets())
                paths.Add(AssetDatabase.GetAssetPath(asset));

            return paths;
        }
    }
}
#endif
