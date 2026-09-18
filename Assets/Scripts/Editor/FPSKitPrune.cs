#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Deletes generated materials nothing references any more.
    ///
    /// The builder accumulates them by design. <c>FPSKitSceneBuilder.MakeMaterialAt</c>
    /// names a material after its theme, its role and its colour
    /// (<c>DesertOutpost_Crate_8A6A44</c>) and reuses the asset at that path if it is
    /// already there -- which is what keeps a rebuild from churning every material in
    /// the project. The cost is that retuning one colour in a theme writes a *new*
    /// material and leaves the old one on disk forever, referenced by nothing, with a
    /// name close enough to its replacement that it looks deliberate.
    ///
    /// Nothing about that breaks the game, which is exactly why it is worth a tool: the
    /// folder grows every time anybody touches a theme, and by inspection there is no
    /// way to tell a live material from a dead one.
    ///
    /// <b>It asks Unity, not the file.</b> Generated scenes are saved in binary, so a
    /// GUID lives in them as sixteen raw bytes rather than as the hex string a text
    /// search would look for -- grepping the scenes finds nothing and reports every
    /// material as unused, which is a very convincing way to delete the whole arena.
    /// <c>AssetDatabase.GetDependencies</c> is the only answer that is actually true.
    ///
    /// Everything that can hold a material reference is a root: every scene, prefab and
    /// ScriptableObject in the project, not only the generated ones, so a material used
    /// by something hand-made survives.
    ///
    ///   FPSKit &gt; Prune Unused Generated Materials
    ///   Tools/unity-batch.sh FPSKitBatch.PruneMaterials            (reports only)
    ///   Tools/unity-batch.sh FPSKitBatch.PruneMaterials -fpskitApply
    /// </summary>
    public static class FPSKitPrune
    {
        private const string MaterialFolder = "Assets/FPSKit_Generated/Materials";

        [MenuItem("FPSKit/Prune Unused Generated Materials", priority = 400)]
        private static void PruneMenu()
        {
            var unused = FindUnusedMaterials(out int total);

            if (unused.Count == 0)
            {
                EditorUtility.DisplayDialog("Nothing to prune",
                    $"All {total} generated materials are referenced.", "OK");
                return;
            }

            bool go = EditorUtility.DisplayDialog("Prune unused materials",
                $"{unused.Count} of {total} materials in {MaterialFolder} are referenced by no " +
                "scene, prefab or asset in the project.\n\nDelete them?",
                "Delete", "Cancel");

            if (!go) return;

            int deleted = Delete(unused);
            Debug.Log($"[FPSKit] Pruned {deleted} unused generated material(s).");
        }

        /// <summary>Headless entry. Reports by default; deletes only when asked to.</summary>
        public static void Prune(bool apply)
        {
            var unused = FindUnusedMaterials(out int total);

            var report = new StringBuilder();
            report.Append($"{total - unused.Count} of {total} generated materials are referenced");

            if (unused.Count == 0)
            {
                Debug.Log($"[FPSKitBatch] {report}, so there is nothing to prune.");
                return;
            }

            // Named rather than counted. A prune that deletes the wrong thing is not
            // recoverable from a number, and this is the only record of what went.
            foreach (var path in unused.Take(40)) report.Append($"\n  - {path}");
            if (unused.Count > 40) report.Append($"\n  ... and {unused.Count - 40} more");

            if (!apply)
            {
                Debug.Log($"[FPSKitBatch] {report}\n\nRe-run with -fpskitApply to delete them.");
                return;
            }

            int deleted = Delete(unused);
            Debug.Log($"[FPSKitBatch] {report}\n\nDeleted {deleted}.");
        }

        // ==================================================================
        /// <summary>
        /// Every material in the generated folder that nothing depends on.
        ///
        /// The roots deliberately exclude the material folder itself: a material that is
        /// only referenced by another dead material is dead too, and treating materials
        /// as roots would keep every one of them alive by definition.
        /// </summary>
        private static List<string> FindUnusedMaterials(out int total)
        {
            var materials = AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder })
                                         .Select(AssetDatabase.GUIDToAssetPath)
                                         .Where(p => p.StartsWith(MaterialFolder, StringComparison.Ordinal))
                                         .Distinct()
                                         .ToList();

            total = materials.Count;
            if (total == 0) return new List<string>();

            var roots = AssetDatabase.FindAssets("t:Scene t:Prefab t:ScriptableObject")
                                     .Select(AssetDatabase.GUIDToAssetPath)
                                     .Where(p => !string.IsNullOrEmpty(p) &&
                                                 p.StartsWith("Assets/", StringComparison.Ordinal) &&
                                                 !p.StartsWith(MaterialFolder, StringComparison.Ordinal))
                                     .Distinct()
                                     .ToArray();

            // One call over every root. GetDependencies is not cheap, and asking it per
            // material would be a full project crawl several hundred times over.
            var reachable = new HashSet<string>(AssetDatabase.GetDependencies(roots, true),
                                                StringComparer.Ordinal);

            return materials.Where(m => !reachable.Contains(m)).OrderBy(m => m, StringComparer.Ordinal)
                            .ToList();
        }

        private static int Delete(List<string> paths)
        {
            var failed = new List<string>();
            AssetDatabase.DeleteAssets(paths.ToArray(), failed);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            foreach (var path in failed)
                Debug.LogWarning($"[FPSKit] Could not delete {path}.");

            return paths.Count - failed.Count;
        }
    }
}
#endif
