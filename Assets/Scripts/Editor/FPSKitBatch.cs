#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Command-line entry points for the scene builder.
    ///
    /// The FPSKit menu items are private and BuildScene takes a theme name, so
    /// neither can be reached by Unity's -executeMethod, which only calls public
    /// static parameterless methods. These wrappers are that missing seam: they
    /// let CI or a headless shell rebuild scenes without opening the editor.
    ///
    /// Every entry point pushes its own exit code, because batchmode otherwise
    /// reports success even when a build threw -- a green CI run on a scene that
    /// never got written is worse than no CI at all.
    ///
    ///   Unity -batchmode -nographics -projectPath . \
    ///         -executeMethod FPSKit.EditorTools.FPSKitBatch.BuildAllThemes \
    ///         -logFile -
    ///
    ///   ... FPSKitBatch.BuildTheme -fpskitTheme "Mars Colony"
    /// </summary>
    public static class FPSKitBatch
    {
        /// <summary>Argument that names the theme for <see cref="BuildTheme"/>.</summary>
        private const string ThemeArg = "-fpskitTheme";

        /// <summary>
        /// Rebuilds and saves one scene per theme, registering each in Build
        /// Settings. Destructive: it overwrites every generated scene.
        /// </summary>
        public static void BuildAllThemes()
        {
            Run(() =>
            {
                foreach (var name in FPSKitThemes.Names)
                {
                    Debug.Log($"[FPSKitBatch] building \"{name}\"");
                    FPSKitSceneBuilder.BuildScene(name, askFirst: false);
                }

                Debug.Log($"[FPSKitBatch] built {FPSKitThemes.Names.Length} scenes");
            });
        }

        /// <summary>
        /// Rebuilds the single theme named by -fpskitTheme. Unknown names fail
        /// loudly rather than silently building nothing.
        /// </summary>
        public static void BuildTheme()
        {
            Run(() =>
            {
                string theme = ReadArg(ThemeArg);

                if (string.IsNullOrEmpty(theme))
                    throw new ArgumentException($"{ThemeArg} \"<theme name>\" is required.");

                if (Array.IndexOf(FPSKitThemes.Names, theme) < 0)
                    throw new ArgumentException(
                        $"Unknown theme \"{theme}\". Known: {string.Join(", ", FPSKitThemes.Names)}");

                Debug.Log($"[FPSKitBatch] building \"{theme}\"");
                FPSKitSceneBuilder.BuildScene(theme, askFirst: false);
            });
        }

        /// <summary>
        /// Does nothing on purpose. Reaching it at all means every script in the
        /// project compiled, which is the cheapest pre-commit check there is.
        /// </summary>
        public static void CompileCheck()
        {
            Run(() => Debug.Log("[FPSKitBatch] compile check passed"));
        }

        // ==================================================================
        private static void Run(Action work)
        {
            try
            {
                work();
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                // Log before exiting -- an unflushed exception reads as a hang.
                Debug.LogError($"[FPSKitBatch] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static string ReadArg(string flag)
        {
            var args = Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];

            return null;
        }
    }
}
#endif
