#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Lets a play-mode test enter play mode in the scene it opened.
    ///
    /// The dashboard build sets <c>EditorSceneManager.playModeStartScene</c>, so pressing
    /// Play in the editor always boots the dashboard however an arena came to be open.
    /// That is right for a person and wrong for every test here: each one opens the scene
    /// it means to exercise and then enters play mode, and with a start scene set it
    /// would be handed the dashboard instead and fail on the first assertion.
    ///
    /// So a test suspends it for the duration and puts it back. Restoring matters even
    /// on a failing run -- leaving it cleared would silently take the behaviour away from
    /// whoever next pressed Play, and nothing would say why.
    /// </summary>
    [InitializeOnLoad]
    public static class FPSKitPlayMode
    {
        /// <summary>
        /// Stores the opt-*out*, not the opt-in.
        ///
        /// EditorPrefs.GetBool(key, true) came back false for a key that had never been
        /// written, which left the feature switched off with nothing to say so. Keying on
        /// "disabled" means the value that gets read when nothing is stored -- false --
        /// is the one that leaves this on, so the failure mode lands on the default
        /// behaviour rather than silently removing it.
        /// </summary>
        const string DisabledKey = "FPSKit.PlayStartsAtDashboard.Disabled";
        const string MenuPath = "FPSKit/Play Starts At Dashboard";

        static SceneAsset _saved;
        static bool _suspended;

        /// <summary>
        /// Whether pressing Play should always boot the dashboard. On by default.
        /// Stored in EditorPrefs, so it is a preference of yours rather than of the repo.
        /// </summary>
        public static bool Enabled
        {
            get => !EditorPrefs.GetBool(DisabledKey, false);
            set
            {
                EditorPrefs.SetBool(DisabledKey, !value);
                Apply();
            }
        }

        /// <summary>
        /// Re-applies the setting every time the editor loads this assembly.
        ///
        /// Setting playModeStartScene once at build time is not enough: Unity keeps it in
        /// EditorUserSettings, which is gitignored and per-machine, so a fresh clone --
        /// or anyone else opening this project -- would press Play and drop straight into
        /// whichever arena happened to be open, which is the behaviour this exists to
        /// remove. Applying it on load makes it a property of the project instead.
        /// </summary>
        static FPSKitPlayMode()
        {
            // Deferred: the asset database is not necessarily ready inside a static
            // constructor, and loading the scene asset there returns null.
            EditorApplication.delayCall += Apply;
        }

        /// <summary>Applies the preference immediately. Used right after a dashboard build.</summary>
        public static void ApplyNow() => Apply();

        static void Apply()
        {
            if (_suspended) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            if (!Enabled)
            {
                EditorSceneManager.playModeStartScene = null;
                return;
            }


            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(FPSKitMenuBuilder.MenuScenePath);

            if (asset == null)
            {
                // The scene exists on disk but the database has not caught up -- which is
                // what happens on the run that just rebuilt it. Import and ask again.
                if (!System.IO.File.Exists(FPSKitMenuBuilder.MenuScenePath)) return;

                AssetDatabase.ImportAsset(FPSKitMenuBuilder.MenuScenePath);
                asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(FPSKitMenuBuilder.MenuScenePath);
            }

            if (asset == null || EditorSceneManager.playModeStartScene == asset) return;

            EditorSceneManager.playModeStartScene = asset;
        }

        [MenuItem(MenuPath, false, 21)]
        static void ToggleMenu() => Enabled = !Enabled;

        [MenuItem(MenuPath, true)]
        static bool ToggleMenuValidate()
        {
            UnityEditor.Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        /// <summary>Clears the play-mode start scene, remembering what it was.</summary>
        public static void SuspendStartScene()
        {
            if (_suspended) return;

            _saved = EditorSceneManager.playModeStartScene;
            _suspended = true;

            EditorSceneManager.playModeStartScene = null;
        }

        /// <summary>Puts it back. Safe to call when nothing was suspended.</summary>
        public static void RestoreStartScene()
        {
            if (!_suspended) return;

            _suspended = false;
            EditorSceneManager.playModeStartScene = _saved;
            _saved = null;

            // Asked for again rather than trusted: a test that ran from a cold editor may
            // have saved a null, and the preference is what should win afterwards.
            Apply();
        }
    }
}
#endif
