#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Keeps the dynamic TMP font assets from being written to disk by anything except the font generator.
    ///
    /// The UI fonts are Dynamic: a glyph is rasterised into the atlas the first time a string needs it. In the
    /// editor that dirties the asset, and the next save of the project writes the glyphs and the atlas into the
    /// .asset file -- so every play session, and every scene that rendered a TMP label in edit mode, left both
    /// Barlow assets modified (thousands of lines of glyph data) and showing in git. Builds already clear this
    /// data (<c>m_ClearDynamicDataOnBuild</c>); this does the same for the editor by refusing the write.
    ///
    /// <b>Why a save filter and not a snapshot or a clear.</b> Restoring a copy after play missed the edit-mode
    /// cases. <c>TMP_FontAsset.ClearFontAssetData</c> empties the kerning and ligature tables too, which are part
    /// of the committed asset, and cost the UI its kerning. Dynamic is kept rather than a pre-built static atlas
    /// because the keyboard and player names can type characters nothing pre-generated.
    /// </summary>
    public class FPSKitFontHygiene : AssetModificationProcessor
    {
        private const string Folder = "Assets/FPSKit_Generated/UI/Fonts/";

        /// <summary>Set by the font generator for the length of a deliberate rebuild, so its output is saved.</summary>
        public static bool AllowSave;

        static string[] OnWillSaveAssets(string[] paths)
        {
            if (AllowSave) return paths;

            List<string> keep = null;
            for (int i = 0; i < paths.Length; i++)
            {
                bool font = paths[i].StartsWith(Folder) && paths[i].EndsWith("SDF.asset");
                if (font && keep == null) { keep = new List<string>(paths.Length); for (int j = 0; j < i; j++) keep.Add(paths[j]); }
                if (!font && keep != null) keep.Add(paths[i]);
            }
            return keep == null ? paths : keep.ToArray();
        }
    }
}
#endif
