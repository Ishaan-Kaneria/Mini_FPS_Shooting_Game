#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

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

        /// <summary>
        /// Plays the game twice back to back and asserts the second run is as alive as
        /// the first. Delegates to <see cref="FPSKitPlayTest"/>, which drives play mode
        /// asynchronously and pushes its own exit code, so it must not go through Run.
        /// </summary>
        public static void VerifyReplay() => FPSKitPlayTest.VerifyReplay();

        /// <summary>
        /// Proves a wave that cannot be cleared still ends. Delegates to
        /// <see cref="FPSKitWaveTest"/>, which drives play mode asynchronously and
        /// pushes its own exit code, so it must not go through Run.
        /// </summary>
        public static void VerifyWaves() => FPSKitWaveTest.VerifyWaves();

        /// <summary>
        /// Builds one scene and then asserts it is actually playable.
        ///
        /// A build that throws no exception still proves very little: the builder wires
        /// dozens of references by hand, and a null one shows up as a black screen or a
        /// wave that never starts rather than as an error. This checks the things that
        /// silently break, and fails the run when any of them is missing.
        /// </summary>
        public static void VerifyBuild()
        {
            Run(() =>
            {
                const string theme = "Industrial Warehouse";

                // Built twice on purpose. The first build creates the generated assets;
                // the second has to cope with every one of them already existing, which
                // is the path anyone takes the second time they use the menu. Only the
                // second result is inspected.
                FPSKitSceneBuilder.BuildScene(theme, askFirst: false);
                FPSKitSceneBuilder.BuildScene(theme, askFirst: false);

                var problems = new List<string>();

                CheckPlayer(problems);
                CheckWaves(problems);
                CheckHud(problems);

                if (UnityEngine.Object.FindAnyObjectByType<GameDirector>() == null)
                    problems.Add("no GameDirector: score, combo and pause would not work");

                if (!NavMesh.SamplePosition(Vector3.zero, out _, 25f, NavMesh.AllAreas))
                    problems.Add("NavMesh did not bake: nothing would be able to move");

                if (problems.Count > 0)
                    throw new Exception($"\"{theme}\" built but is not playable:\n  - " +
                                        string.Join("\n  - ", problems));

                Debug.Log($"[FPSKitBatch] verify passed: \"{theme}\" is wired and playable");
            });
        }

        private static void CheckPlayer(List<string> problems)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null)
            {
                problems.Add("no Player-tagged object");
                return;
            }

            if (player.GetComponent<PlayerMotor>() == null) problems.Add("player has no PlayerMotor");
            if (player.GetComponent<CharacterController>() == null) problems.Add("player has no CharacterController");

            var health = player.GetComponent<Health>();
            if (health == null) problems.Add("player has no Health");
            else if (health.maxHealth <= 0f) problems.Add("player health pool is empty");

            if (player.GetComponentInChildren<Camera>() == null)
                problems.Add("player has no camera: the game view would stay black");

            var weapon = player.GetComponentInChildren<Weapon>();
            if (weapon == null) problems.Add("player has no Weapon");
            else if (weapon.data == null) problems.Add("weapon has no WeaponData: it could not fire");
        }

        private static void CheckWaves(List<string> problems)
        {
            var wave = UnityEngine.Object.FindAnyObjectByType<WaveManager>();
            if (wave == null)
            {
                problems.Add("no WaveManager");
                return;
            }

            if (wave.player == null) problems.Add("WaveManager has no player reference");
            if (wave.baseEnemyPrefab == null) problems.Add("WaveManager has no base enemy prefab");

            if (wave.enemyTypes == null || wave.enemyTypes.Length == 0)
            {
                problems.Add("WaveManager roster is empty: no enemy would ever spawn");
                return;
            }

            int usable = 0;
            int bosses = 0;

            foreach (var type in wave.enemyTypes)
            {
                if (type == null || type.archetype == null) continue;

                usable++;
                if (type.IsBoss) bosses++;
            }

            if (usable == 0) problems.Add("no roster entry has an archetype");
            if (bosses == 0 && wave.bossWaveInterval > 0)
                problems.Add("boss waves are enabled but no archetype has the Boss role");

            if (wave.healthPickupPrefab == null) problems.Add("no health pickup prefab wired");
        }

        private static void CheckHud(List<string> problems)
        {
            var hud = UnityEngine.Object.FindAnyObjectByType<HUDController>();
            if (hud == null)
            {
                problems.Add("no HUDController");
                return;
            }

            if (hud.playerHealth == null) problems.Add("HUD is not bound to player health");
            if (hud.weapon == null) problems.Add("HUD is not bound to the weapon");
            if (hud.waveManager == null) problems.Add("HUD is not bound to the wave manager");
            if (hud.healthFill == null) problems.Add("HUD has no health bar fill");
            if (hud.bossPanel == null) problems.Add("HUD has no boss bar");
            if (hud.pausePanel == null) problems.Add("HUD has no pause panel");
            if (hud.gameOverPanel == null) problems.Add("HUD has no game over panel");

            if (hud.crosshairArms == null || hud.crosshairArms.Length < 4)
                problems.Add("HUD crosshair needs four arms");

            // A filled Image silently ignores fillAmount with no sprite, so the bars
            // would render as full blocks that never move.
            if (hud.healthFill != null && hud.healthFill.sprite == null)
                problems.Add("health bar has no sprite: fillAmount would do nothing");
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
