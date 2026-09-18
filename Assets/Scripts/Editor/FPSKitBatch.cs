#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
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

        /// <summary>Argument that overrides where <see cref="BuildWebGL"/> writes.</summary>
        private const string OutputArg = "-fpskitOutput";

        /// <summary>
        /// "-fpskitFallback false" drops the JavaScript decompressor, for a host that
        /// serves Content-Encoding itself. Defaults to keeping it.
        /// </summary>
        private const string FallbackArg = "-fpskitFallback";

        /// <summary>Turns a job that reports by default into one that acts.</summary>
        private const string ApplyArg = "-fpskitApply";

        /// <summary>
        /// Folder name under Assets/WebGLTemplates that holds the page the build ships.
        /// </summary>
        private const string WebTemplateName = "FPSKit";

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
        /// Rebuilds the dashboard scene, the arena catalog and the preview images, and
        /// registers the dashboard as scene 0 so the game boots into it.
        ///
        /// Run this after BuildAllThemes, not before: the previews are rendered from the
        /// arena scenes, so they can only be as current as the scenes are. Pass a real
        /// graphics device (UNITY_GRAPHICS=1) for screenshots -- without one it falls
        /// back to drawing each card from its theme's colours.
        /// </summary>
        public static void BuildDashboard()
        {
            Run(FPSKitMenuBuilder.Build);
        }

        /// <summary>
        /// Re-stamps the built-in numbers onto every EnemyArchetype asset.
        ///
        /// The roster assets are generated once and then left alone, so a retune written
        /// into FPSKitEnemyRoster.Configure does not reach them on its own -- this is how
        /// a change to the roster in code gets into the assets the game reads. Overwrites
        /// any Inspector tuning, by design.
        /// </summary>
        public static void ResetEnemyArchetypes()
        {
            Run(FPSKitEnemyRoster.ResetAll);
        }

        /// <summary>
        /// Re-stamps the built-in curve onto every LevelSet asset.
        ///
        /// Exactly the same trap as the archetypes, and worth stating twice because the
        /// symptom is different: the level assets are generated once and then left alone,
        /// so retuning a clock or a star threshold in FPSKitLevels.Configure does not
        /// reach the assets the game reads until this is run. Overwrites any Inspector
        /// tuning, by design. Stars the player has already earned are untouched -- those
        /// live in PlayerPrefs, not in the asset.
        /// </summary>
        public static void ResetLevelSets()
        {
            Run(FPSKitLevels.ResetAll);
        }

        /// <summary>
        /// Re-stamps the built-in stock, prices and upgrade curves onto the store.
        ///
        /// The third of the same trap, for the same reason: the store assets are created
        /// once and then left alone, so a price changed in FPSKitStore.Configure does not
        /// reach the asset the game reads until this is run. Coins the player has earned
        /// and items they have bought live in PlayerPrefs and are never touched by it.
        /// </summary>
        public static void ResetStore()
        {
            Run(FPSKitStore.ResetAll);
        }

        /// <summary>
        /// Re-applies the built-in values to every LevelTheme asset.
        ///
        /// The fourth of the same trap. A theme is generated once and then left alone --
        /// it is meant to be tuned in the Inspector and kept -- so reshaping an arena in
        /// FPSKitThemes.Configure does not reach the asset the scene builder reads until
        /// this has run, and the arena rebuilds exactly as it was. Overwrites any
        /// Inspector tuning, by design. Run it before BuildAllThemes, not after.
        /// </summary>
        public static void ResetThemes()
        {
            Run(() =>
            {
                FPSKitSceneBuilder.EnsureProjectTagsAndLayers();
                Debug.Log($"[FPSKitBatch] {FPSKitThemes.ResetAll()} theme asset(s) reset");
            });
        }

        /// <summary>
        /// Deletes generated materials nothing references.
        ///
        /// The material folder grows every time a theme colour is retuned: the builder
        /// names a material after its colour and reuses the asset at that path, so a new
        /// colour writes a new asset and the old one stays on disk, referenced by
        /// nothing and named closely enough to its replacement to look deliberate.
        ///
        /// Reports by default and deletes only with <c>-fpskitApply</c>, because it is
        /// the one maintenance job here that cannot be undone by re-running a builder.
        /// </summary>
        public static void PruneMaterials()
        {
            Run(() => FPSKitPrune.Prune(HasFlag(ApplyArg)));
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
        /// Asserts every state-carrying static has a SubsystemRegistration reset hook.
        ///
        /// VerifyReplay catches a leaked static by watching the second run break, which
        /// means playing two full sessions. This catches the same mistake structurally
        /// in seconds and without entering play mode, so it is the cheap gate to run
        /// first: a new static with no hook fails here before it ever reaches a session.
        /// </summary>
        public static void VerifyStatics()
        {
            Run(() =>
            {
                var problems = FPSKitStaticProbe.Audit(out string report);
                Debug.Log(report);

                if (problems.Count > 0)
                    throw new Exception(
                        $"{problems.Count} static(s) without a reset hook: " +
                        string.Join("; ", problems));

                Debug.Log("[FPSKitBatch] static reset audit passed");
            });
        }

        /// <summary>
        /// Plays a level to both endings: failed on the clock with the arena full of
        /// enemies that cannot be reached, then cleared outright for three stars and an
        /// unlock. Delegates to <see cref="FPSKitLevelTest"/>, which drives play mode
        /// asynchronously and pushes its own exit code, so it must not go through Run.
        /// </summary>
        public static void VerifyLevels() => FPSKitLevelTest.VerifyLevels();

        /// <summary>
        /// Plays level one and asserts that it actually fights: armed, slower than the
        /// player, and able to land a hit on someone standing in the middle of it.
        /// </summary>
        public static void VerifyCombat() => FPSKitCombatTest.VerifyCombat();

        /// <summary>
        /// Plays the loop the player sees: dashboard, into an arena, quit, back to the
        /// dashboard with the run recorded.
        /// </summary>
        public static void VerifyFlow() => FPSKitFlowTest.VerifyFlow();

        /// <summary>
        /// Checks every open-zone arena for the failures that are invisible in a build:
        /// a gorge that does not block, banks that are not joined, spawn points off the
        /// navmesh and water that is not lethal. Edit mode, so it costs seconds.
        /// </summary>
        public static void VerifyZone() => FPSKitZoneTest.VerifyZone();

        /// <summary>
        /// Plays the economy end to end: coins earned by killing, spent in the store, and
        /// carried into a level as a better gun and a bomb that goes off where it was
        /// aimed.
        /// </summary>
        public static void VerifyStore() => FPSKitStoreTest.VerifyStore();

        /// <summary>
        /// Plays a level and aims, throws and listens to a bomb: the ring has to follow
        /// the mouse smoothly across the whole throw range, a short throw has to land
        /// sooner than a long one, the bomb has to go off where the ring said, and the
        /// pin, the throw and the blast all have to be audible.
        /// </summary>
        public static void VerifyBomb() => FPSKitBombTest.VerifyBomb();

        /// <summary>
        /// Audits every binding in every control scheme for collisions and unbound
        /// actions, then plays a level and drives each action in turn: walk, sprint in
        /// all directions, crouch, jump, fire, reload and aim.
        /// </summary>
        public static void VerifyControls() => FPSKitControlsTest.VerifyControls();

        /// <summary>
        /// Builds one scene and then asserts it is actually playable.
        ///
        /// A build that throws no exception still proves very little: the builder wires
        /// dozens of references by hand, and a null one shows up as a black screen or a
        /// level that never starts rather than as an error. This checks the things that
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
                CheckLevels(problems);
                CheckHud(problems);
                CheckEquipment(problems);

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

        /// <summary>
        /// The bomb, the belt and the loadout that decides what the player is carrying.
        ///
        /// Every reference here is optional at runtime and fails silently when it is not
        /// wired -- a bomb key that does nothing, a belt counter that never appears, a
        /// gun the store sold and the player never receives. Those are exactly the
        /// failures a build check exists for.
        /// </summary>
        private static void CheckEquipment(List<string> problems)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return;

            var loadout = player.GetComponent<PlayerLoadout>();
            if (loadout == null)
            {
                problems.Add("player has no PlayerLoadout: nothing bought in the store would " +
                             "ever reach them");
                return;
            }

            if (loadout.catalog == null)
                problems.Add("PlayerLoadout has no StoreCatalog: the player would keep the " +
                             "builder's rifle whatever they bought");

            var bombs = player.GetComponent<BombThrower>();
            if (bombs == null) problems.Add("player has no BombThrower");
            else
            {
                if (bombs.indicator == null)
                    problems.Add("BombThrower has no aim indicator: bombs would be thrown blind");

                if (bombs.groundMask == 0)
                    problems.Add("BombThrower has an empty ground mask: the aim ray would " +
                                 "land on nothing and every throw would be refused");
            }

            if (player.GetComponent<ConsumableBelt>() == null)
                problems.Add("player has no ConsumableBelt: energy drinks could not be used");

            var catalog = loadout.catalog;
            if (catalog == null) return;

            if (catalog.StarterGun == null)
                problems.Add("the store has no starter gun, so a new player would go in unarmed");

            foreach (var gun in catalog.guns)
            {
                if (gun == null) continue;

                if (gun.data == null) problems.Add($"store gun \"{gun.id}\" has no WeaponData");
                else if (gun.data.impacts == null)
                    problems.Add($"store gun \"{gun.id}\" has no impact library: it would hit silently");
            }

            foreach (var bomb in catalog.bombs)
            {
                if (bomb == null) continue;

                if (bomb.data == null) problems.Add($"store bomb \"{bomb.id}\" has no BombData");
                else if (bomb.data.bombPrefab == null)
                    problems.Add($"store bomb \"{bomb.id}\" has no prefab: it would go off in your hand");
                else if (bomb.data.explosionPrefab == null)
                    problems.Add($"store bomb \"{bomb.id}\" has no explosion prefab: it would " +
                                 "damage invisibly");
            }

            foreach (var item in catalog.consumables)
                if (item != null && item.data == null)
                    problems.Add($"store item \"{item.id}\" has no ConsumableData");
        }

        private static void CheckLevels(List<string> problems)
        {
            var manager = UnityEngine.Object.FindAnyObjectByType<LevelManager>();
            if (manager == null)
            {
                problems.Add("no LevelManager");
                return;
            }

            if (manager.player == null) problems.Add("LevelManager has no player reference");
            if (manager.baseEnemyPrefab == null) problems.Add("LevelManager has no base enemy prefab");

            // A missing level set is the one failure that still plays: the manager falls
            // back to a stand-in level, so the arena looks fine and offers exactly one
            // unnamed level that the dashboard knows nothing about.
            if (manager.levels == null)
                problems.Add("LevelManager has no LevelSet: the arena would fall back to one " +
                             "stand-in level and the dashboard would show no ladder");
            else if (manager.levels.Count == 0)
                problems.Add("the arena's LevelSet is empty: there is nothing to play");

            if (manager.enemyTypes == null || manager.enemyTypes.Length == 0)
            {
                problems.Add("LevelManager roster is empty: no enemy would ever spawn");
                return;
            }

            int usable = 0;
            int bosses = 0;

            foreach (var type in manager.enemyTypes)
            {
                if (type == null || type.archetype == null) continue;

                usable++;
                if (type.IsBoss) bosses++;
            }

            if (usable == 0) problems.Add("no roster entry has an archetype");

            bool wantsBoss = false;

            if (manager.levels != null)
                foreach (var level in manager.levels.levels)
                    if (level != null && level.hasBoss) wantsBoss = true;

            if (bosses == 0 && wantsBoss)
                problems.Add("a level asks for a boss but no archetype has the Boss role");

            if (manager.healthPickupPrefab == null) problems.Add("no health pickup prefab wired");
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
            if (hud.levelManager == null) problems.Add("HUD is not bound to the level manager");
            if (hud.healthFill == null) problems.Add("HUD has no health bar fill");
            if (hud.bossPanel == null) problems.Add("HUD has no boss bar");
            if (hud.pausePanel == null) problems.Add("HUD has no pause panel");

            if (hud.crosshairArms == null || hud.crosshairArms.Length < 4)
                problems.Add("HUD crosshair needs four arms");

            if (hud.bombPanel == null) problems.Add("HUD has no bomb counter");
            if (hud.beltPanel == null) problems.Add("HUD has no belt counter");
            if (hud.bombRangeText == null) problems.Add("HUD has no bomb range readout");
            if (hud.coinText == null) problems.Add("HUD has no coin counter");

            CheckResults(problems);

            // A filled Image silently ignores fillAmount with no sprite, so the bars
            // would render as full blocks that never move.
            if (hud.healthFill != null && hud.healthFill.sprite == null)
                problems.Add("health bar has no sprite: fillAmount would do nothing");
        }

        /// <summary>
        /// The results screen is the whole reward loop, and every one of its parts is an
        /// optional reference that fails silently when it is not wired: no stars, no
        /// Next Level, or -- worst -- no panel at all, which reads as the game hanging
        /// the moment a level ends.
        /// </summary>
        private static void CheckResults(List<string> problems)
        {
            var results = UnityEngine.Object.FindAnyObjectByType<LevelResultsUI>();
            if (results == null)
            {
                problems.Add("no LevelResultsUI: nothing would appear when a level ends");
                return;
            }

            if (results.panel == null) problems.Add("the results screen has no panel to show");
            if (results.levelManager == null) problems.Add("the results screen is not bound to the level manager");

            if (results.stars == null || results.stars.Length < 3)
                problems.Add("the results screen needs three stars");

            if (results.retryButton == null) problems.Add("the results screen has no replay button");
            if (results.nextButton == null) problems.Add("the results screen has no next-level button");
            if (results.dashboardButton == null) problems.Add("the results screen has no dashboard button");
            if (results.titleText == null) problems.Add("the results screen has no title");
        }

        // ==================================================================
        /// <summary>
        /// Builds a browser-playable WebGL player, so the game can be handed to someone
        /// as a link rather than as a repository.
        ///
        ///   Tools/unity-batch.sh FPSKitBatch.BuildWebGL -buildTarget WebGL
        ///
        /// Ships the dashboard and every arena. It used to ship one arena and nothing
        /// else, on the reasoning that the game never switched level -- which was true
        /// when Restart reloaded the scene it was already in and there was no level
        /// select. There is one now, so the other five are exactly what a player is
        /// being offered, and the dashboard has to be index 0 or the build opens
        /// straight into a fight.
        ///
        /// The scene list is passed explicitly rather than read from Build Settings,
        /// which still carries Unity's empty SampleScene template.
        /// </summary>
        public static void BuildWebGL()
        {
            Run(() =>
            {
                string output = ReadArg(OutputArg) ?? "Build/WebGL";

                string menu = FPSKitMenuBuilder.MenuScenePath;
                if (!File.Exists(menu))
                    throw new Exception($"{menu} is missing. Run BuildDashboard first.");

                bool fallback = !string.Equals(ReadArg(FallbackArg), "false",
                                                StringComparison.OrdinalIgnoreCase);
                ConfigureWebGL(fallback);

                // The dashboard first: index 0 is what the player boots into.
                var scenes = new List<string> { menu };
                var staged = new List<string>();

                try
                {
                    foreach (string themeName in FPSKitThemes.Names)
                    {
                        string source = $"Assets/FPSKit_Generated/Scenes/" +
                                        $"{themeName.Replace(" ", "")}.unity";

                        if (!File.Exists(source))
                        {
                            Debug.LogWarning($"[FPSKitBatch] {source} is missing and is being " +
                                             "left out of the build. Run BuildAllThemes.");
                            continue;
                        }

                        // The touch layer goes into a throwaway copy rather than into the
                        // scene itself. Saving it back would leave a generated scene
                        // carrying something the builder does not put there, which the
                        // next BuildScene would silently wipe -- a half-state that is
                        // worse than either end of it.
                        string copy = $"Assets/FPSKit_Generated/Scenes/_WebGLStaging_" +
                                      $"{themeName.Replace(" ", "")}.unity";

                        var scene = EditorSceneManager.OpenScene(source, OpenSceneMode.Single);
                        FPSKitMobileControls.AddMobileControls(askFirst: false);
                        EditorSceneManager.SaveScene(scene, copy, saveAsCopy: true);

                        staged.Add(copy);
                        scenes.Add(copy);
                    }

                    if (scenes.Count < 2)
                        throw new Exception("no arenas were available to build.");

                    // The dashboard loads arenas by name, and a staged copy is called
                    // something else -- so the catalog has to point at the staged names
                    // for the duration of the build.
                    RemapCatalogToStaged(staged);

                    var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                    {
                        scenes = scenes.ToArray(),
                        locationPathName = output,
                        target = BuildTarget.WebGL,
                        targetGroup = BuildTargetGroup.WebGL,
                        options = BuildOptions.None,
                    });

                    var summary = report.summary;

                    if (summary.result != BuildResult.Succeeded)
                        throw new Exception($"WebGL build {summary.result} with " +
                                            $"{summary.totalErrors} error(s)");

                    WriteNetlifyHeaders(output);

                    Debug.Log($"[FPSKitBatch] WebGL build succeeded -> {output} " +
                              $"({scenes.Count} scenes, {summary.totalSize / 1048576f:0.0} MB payload, " +
                              $"{summary.totalTime.TotalMinutes:0.0} min)");
                }
                finally
                {
                    RestoreCatalog();

                    foreach (string copy in staged) AssetDatabase.DeleteAsset(copy);
                }
            });
        }

        /// <summary>Scene name each catalog entry had before the build renamed it.</summary>
        private static readonly Dictionary<string, string> OriginalSceneNames =
            new Dictionary<string, string>();

        /// <summary>
        /// Points the arena catalog at the staged scene copies.
        ///
        /// The dashboard loads an arena by scene name, and the copies carrying the touch
        /// layer are named differently -- so without this every card in the shipped build
        /// would report the arena as missing from Build Settings and refuse to start.
        /// Undone in RestoreCatalog, which the build's finally block always reaches.
        /// </summary>
        private static void RemapCatalogToStaged(List<string> staged)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ArenaCatalog>(FPSKitMenuBuilder.CatalogPath);
            if (catalog == null) return;

            OriginalSceneNames.Clear();

            foreach (string path in staged)
            {
                string stagedName = Path.GetFileNameWithoutExtension(path);
                string realName = stagedName.Replace("_WebGLStaging_", "");

                var entry = catalog.Find(realName);
                if (entry == null) continue;

                OriginalSceneNames[stagedName] = entry.sceneName;
                entry.sceneName = stagedName;
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        private static void RestoreCatalog()
        {
            if (OriginalSceneNames.Count == 0) return;

            var catalog = AssetDatabase.LoadAssetAtPath<ArenaCatalog>(FPSKitMenuBuilder.CatalogPath);

            if (catalog != null)
            {
                foreach (var pair in OriginalSceneNames)
                {
                    var entry = catalog.Find(pair.Key);
                    if (entry != null) entry.sceneName = pair.Value;
                }

                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }

            OriginalSceneNames.Clear();
        }

        /// <summary>
        /// The WebGL settings a shared link actually depends on.
        ///
        /// Decompression Fallback is the one that decides whether the page works at all.
        /// Unity compresses the build with Brotli and expects the server to answer with
        /// Content-Encoding: br. A static host that does not -- GitHub Pages never does,
        /// and itch.io only for uploads it recognises -- hands the browser a blob it
        /// cannot read, and the loading bar simply never finishes, with nothing in the
        /// console naming the cause. The fallback ships a decompressor inside the build
        /// so it stops depending on a header nobody controls.
        ///
        /// Exception support goes down to explicitly thrown only: full support costs size
        /// and speed to produce stack traces for a build nobody is going to debug.
        /// </summary>
        private static void ConfigureWebGL(bool decompressionFallback)
        {
            // Handheld players only -- WebGL ignores it -- but it costs nothing here and
            // means an Android build of the same project is landscape without a second
            // place to remember.
            FPSKitGraphics.ApplyOrientation();

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = decompressionFallback;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;

            // A returning visitor gets the payload from the browser cache instead of
            // downloading it again.
            PlayerSettings.WebGL.dataCaching = true;

            // Unity's stock page is a 960x600 canvas in the corner of a white document,
            // titled "Unity Web Player", with no hint of what any key does and an
            // alert() for a loading failure. Ours is in Assets/WebGLTemplates/FPSKit.
            // Named with the PROJECT: prefix, which is how Unity tells a template in the
            // project from one shipped with the editor.
            PlayerSettings.WebGL.template = "PROJECT:" + WebTemplateName;

            string templateDir = $"Assets/WebGLTemplates/{WebTemplateName}";
            if (!Directory.Exists(templateDir))
                throw new Exception($"{templateDir} is missing -- the build would " +
                                    "silently fall back to Unity's stock page.");

            Debug.Log($"[FPSKitBatch] WebGL: Brotli, decompression fallback " +
                      $"{(decompressionFallback ? "ON (works on any static host)" : "OFF (host must send Content-Encoding: br)")}, " +
                      "explicit-only exceptions, data caching on");
        }

        /// <summary>
        /// Drops a Netlify/Cloudflare "_headers" file beside the build.
        ///
        /// Unity writes the payload already Brotli-compressed but cannot make the server
        /// admit it. Without Content-Encoding the browser saves the compressed bytes
        /// verbatim and the loading bar never finishes -- and because it is a header
        /// problem rather than a code one, nothing appears in the console to say so. The
        /// Content-Type lines matter too: a .wasm.br served as anything but
        /// application/wasm loses the browser's streaming compiler.
        ///
        /// Harmless on a host that ignores it, and on a build that kept the fallback --
        /// those files are named .unityweb and match none of these rules.
        /// </summary>
        private static void WriteNetlifyHeaders(string output)
        {
            const string headers =
                "# Unity WebGL ships pre-compressed; these tell the host to say so.\n" +
                "/Build/*.wasm.br\n" +
                "  Content-Encoding: br\n" +
                "  Content-Type: application/wasm\n" +
                "/Build/*.js.br\n" +
                "  Content-Encoding: br\n" +
                "  Content-Type: application/javascript\n" +
                "/Build/*.data.br\n" +
                "  Content-Encoding: br\n" +
                "  Content-Type: application/octet-stream\n" +
                "/Build/*.symbols.json.br\n" +
                "  Content-Encoding: br\n" +
                "  Content-Type: application/json\n";

            File.WriteAllText(Path.Combine(output, "_headers"), headers);
            Debug.Log("[FPSKitBatch] wrote _headers for Netlify / Cloudflare Pages");
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

        /// <summary>
        /// Whether a valueless switch was passed. ReadArg cannot answer this: it returns
        /// the *next* argument, so a trailing flag reads as absent and a flag followed by
        /// another flag reads as that flag's name.
        /// </summary>
        private static bool HasFlag(string flag)
            => Array.IndexOf(Environment.GetCommandLineArgs(), flag) >= 0;

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
