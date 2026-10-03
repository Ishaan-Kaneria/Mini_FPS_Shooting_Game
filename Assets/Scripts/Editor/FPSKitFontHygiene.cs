#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Gives the dynamic TMP font assets back as they were when play mode ends.
    ///
    /// The UI fonts are Dynamic: a glyph is rasterised into the atlas the first time a string needs it. In the editor
    /// that writes the glyphs and atlas into the .asset, so every play session left the Barlow assets modified (thousands
    /// of lines of glyph data) and showing in git. Builds already clear this data (<c>m_ClearDynamicDataOnBuild</c>).
    ///
    /// The font is copied to Library/ before play and put back after. It is a file copy and not
    /// <c>TMP_FontAsset.ClearFontAssetData</c> because that also empties the kerning and ligature tables, which are
    /// part of the committed asset. Dynamic is kept (rather than a pre-built static atlas) because the keyboard and
    /// player names can type characters nothing pre-generated.
    /// </summary>
    [InitializeOnLoad]
    static class FPSKitFontHygiene
    {
        const string Folder = "Assets/FPSKit_Generated/UI/Fonts";
        const string Snapshot = "Library/FPSKitFontSnapshot";

        static FPSKitFontHygiene() => EditorApplication.playModeStateChanged += OnState;

        static void OnState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) Save();
            else if (state == PlayModeStateChange.EnteredEditMode) Restore();
        }

        static void Save()
        {
            Directory.CreateDirectory(Snapshot);
            foreach (var f in Directory.GetFiles(Folder, "*SDF.asset"))
                File.Copy(f, Path.Combine(Snapshot, Path.GetFileName(f)), true);
        }

        static void Restore()
        {
            if (!Directory.Exists(Snapshot)) return;
            bool any = false;
            foreach (var saved in Directory.GetFiles(Snapshot, "*SDF.asset"))
            {
                string live = Path.Combine(Folder, Path.GetFileName(saved));
                if (!File.Exists(live) || File.ReadAllBytes(live).Length == new FileInfo(saved).Length && File.ReadAllText(live) == File.ReadAllText(saved)) continue;
                File.Copy(saved, live, true);
                any = true;
            }
            if (any) AssetDatabase.Refresh();
        }
    }
}
#endif
