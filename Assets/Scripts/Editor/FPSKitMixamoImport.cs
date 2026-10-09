#if UNITY_EDITOR
using System.IO;
using UnityEditor;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Import rules for the enemy's Mixamo clips (Assets/Animations/Mixamo/&lt;Group&gt;/).
    ///
    /// Mixamo files arrive as Generic rigs with one clip called "mixamo.com". The enemy is the
    /// Survivalist Humanoid, so every file here is imported Humanoid (its own avatar, made from
    /// the Mixamo skeleton in the file) and its clip is renamed after the file. Loop, root
    /// motion and mirrored copies are decided by the file name, so the settings live here and
    /// not in hand-edited .meta files: change a rule, then Reimport the folder.
    ///
    /// - Root rotation is baked into the pose except on the turns, whose rotation is the point.
    /// - Height is always baked (based on the original), so a get-up rises in the pose while
    ///   the root stays on the ground.
    /// - Horizontal root motion is kept on anything that travels (walks, crawls, starts,
    ///   stops, get-ups) and baked on anything that stands in place (idles, hits, reloads).
    /// - A limp is downloaded for one leg; a mirrored copy ("_Mirror") gives the other.
    /// </summary>
    public class FPSKitMixamoImport : AssetPostprocessor
    {
        public const string Root = "Assets/Animations/Mixamo/";

        static bool Ours(string path) => path.Replace('\\', '/').StartsWith(Root);

        void OnPreprocessModel()
        {
            if (!Ours(assetPath)) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importAnimation = true;
        }

        void OnPreprocessAnimation()
        {
            if (!Ours(assetPath)) return;
            var importer = (ModelImporter)assetImporter;
            var defaults = importer.defaultClipAnimations;
            if (defaults == null || defaults.Length == 0) return;

            string name = Path.GetFileNameWithoutExtension(assetPath);
            var clip = defaults[0];
            Configure(clip, name, false);

            if (Mirrored(name))
            {
                var mirror = Copy(clip);
                Configure(mirror, name + "_Mirror", true);
                importer.clipAnimations = new[] { clip, mirror };
            }
            else
            {
                importer.clipAnimations = new[] { clip };
            }
        }

        static void Configure(ModelImporterClipAnimation clip, string name, bool mirror)
        {
            clip.name = name;
            clip.mirror = mirror;
            clip.loopTime = Loops(name);
            clip.loopPose = clip.loopTime;

            clip.lockRootRotation = !Turns(name);
            clip.keepOriginalOrientation = true;
            clip.lockRootHeightY = true;
            clip.keepOriginalPositionY = true;
            clip.heightFromFeet = false;
            clip.lockRootPositionXZ = InPlace(name);
            clip.keepOriginalPositionXZ = true;
        }

        static ModelImporterClipAnimation Copy(ModelImporterClipAnimation c) => new ModelImporterClipAnimation
        {
            takeName = c.takeName,
            firstFrame = c.firstFrame,
            lastFrame = c.lastFrame,
        };

        static bool Has(string name, params string[] parts)
        {
            foreach (var p in parts) if (name.Contains(p)) return true;
            return false;
        }

        /// <summary>Cycles: walks, runs, crawls, idles, strafes.</summary>
        static bool Loops(string name) =>
            Has(name, "Idle", "Limp_", "Crawl_", "Pistol_Walk", "Pistol_Run", "Pistol_Strafe", "Cover_Sneak");

        /// <summary>Clips whose root turns on purpose.</summary>
        static bool Turns(string name) => Has(name, "Turn");

        /// <summary>Clips that stand on the spot: their root drift is noise, not travel.</summary>
        static bool InPlace(string name) =>
            Has(name, "Idle", "Hit_", "Reload_", "Throw_", "Signal_", "Death_", "Melee_");

        /// <summary>One-sided clips that need the other side too.</summary>
        static bool Mirrored(string name) => Has(name, "Limp_", "Injured_HoldLeg");
    }
}
#endif
