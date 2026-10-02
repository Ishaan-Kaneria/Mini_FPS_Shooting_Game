#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// URP Volume profiles for the Abandoned Fairground.
    ///  * FG_Day_FloodedGrounds : a straight translation of the pack's Post Processing v1 profile
    ///    (Postprocess_FloodedGrounds), kept so the grade can be compared against the demo.
    ///  * FG_Night             : the same grade ratios, pushed to a cold, dark night.
    /// SSAO is a renderer feature in URP (PC_Renderer already has one), not a Volume override.
    /// </summary>
    public static class FairgroundVolume
    {
        public const string Folder = "Assets/Fairground/Settings";

        [MenuItem("Tools/MiniFPS/Fairground/Create Volume Profiles")]
        public static void CreateAll()
        {
            Day();
            Night();
            AssetDatabase.SaveAssets();
        }

        static VolumeProfile NewProfile(string name)
        {
            FairgroundMaterialConverter.EnsureFolder(Folder);
            string path = $"{Folder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);      // our own file: rebuilt so values stay in code
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            p.name = name;
            AssetDatabase.CreateAsset(p, path);
            return p;
        }

        static T Add<T>(VolumeProfile p) where T : VolumeComponent
        {
            var c = p.Add<T>(true);
            c.name = typeof(T).Name;
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(c))) AssetDatabase.AddObjectToAsset(c, p);
            return c;
        }

        public static VolumeProfile Day()
        {
            var p = NewProfile("FG_Day_FloodedGrounds");
            var tm = Add<Tonemapping>(p);   tm.mode.Override(TonemappingMode.Neutral);
            var ca = Add<ColorAdjustments>(p);
            ca.postExposure.Override(1.0f);      // pack: +1.0
            ca.contrast.Override(-10f);          // pack: 0.9  (URP scale is -100..100)
            ca.saturation.Override(-20f);        // pack: 0.8
            var wb = Add<WhiteBalance>(p);  wb.temperature.Override(-3f);      // pack: -3
            var vg = Add<Vignette>(p);
            vg.color.Override(Color.black);
            vg.intensity.Override(0.45f);        // pack: 0.45
            vg.smoothness.Override(0.2f);        // pack: 0.2
            vg.rounded.Override(false);
            var fg = Add<FilmGrain>(p);
            fg.type.Override(FilmGrainLookup.Medium1);
            fg.intensity.Override(0.2f);         // pack: 0.2
            fg.response.Override(0.8f);          // pack: luminance contribution 0.8
            EditorUtility.SetDirty(p);
            return p;
        }

        public static VolumeProfile Night()
        {
            var p = NewProfile("FG_Night");
            var tm = Add<Tonemapping>(p);   tm.mode.Override(TonemappingMode.ACES);
            var ca = Add<ColorAdjustments>(p);
            ca.postExposure.Override(1.5f);      // dusk: brighter than the old night (2.0 with a dim moon), not by much
            ca.contrast.Override(-4f);                                   // keeps the damp, milky blacks
            ca.saturation.Override(-14f);                                // still muted, but warmer than the night's -28
            ca.colorFilter.Override(new Color(1.0f, 0.96f, 0.93f));
            var wb = Add<WhiteBalance>(p);  wb.temperature.Override(3f); wb.tint.Override(6f);
            var lgg = Add<LiftGammaGain>(p);
            lgg.lift.Override(new Vector4(0.94f, 1.02f, 1.03f, -0.01f));   // cold blue-green shadows
            lgg.gain.Override(new Vector4(1.03f, 1.0f, 0.96f, 0f));        // a little warmth for practical lights
            var bl = Add<Bloom>(p);
            bl.threshold.Override(1.0f);
            bl.intensity.Override(0.3f);
            bl.scatter.Override(0.6f);
            var vg = Add<Vignette>(p);
            vg.color.Override(Color.black);
            vg.intensity.Override(0.40f);
            vg.smoothness.Override(0.2f);
            vg.rounded.Override(false);
            var fg = Add<FilmGrain>(p);
            fg.type.Override(FilmGrainLookup.Medium1);
            fg.intensity.Override(0.2f);
            fg.response.Override(0.8f);
            EditorUtility.SetDirty(p);
            return p;
        }
    }
}
#endif
