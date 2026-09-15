#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Unity.AI.Navigation;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// One-click scene builder. Menu: FPSKit > Build Scene > [theme]
    ///
    /// Creates tags/layers, a themed arena, the player rig with a working gun,
    /// an enemy prefab with hitboxes, spawn points, a baked NavMesh, the wave
    /// manager, post processing and a functioning HUD. Press play immediately
    /// after.
    ///
    /// Put this file in Assets/Scripts/Editor/ -- it MUST be in a folder named
    /// "Editor" or the build will fail.
    /// </summary>
    public static class FPSKitSceneBuilder
    {
        private const string AssetFolder = "Assets/FPSKit_Generated";
        private const string MaterialFolder = AssetFolder + "/Materials";
        private const string SceneFolder = AssetFolder + "/Scenes";

        private static LevelTheme _theme;

        // ==================================================================
        // Menu entries -- one per built-in theme.
        // ==================================================================
        [MenuItem("FPSKit/Build Scene/Industrial Warehouse", false, 0)]
        private static void BuildIndustrial() => BuildScene("Industrial Warehouse");

        [MenuItem("FPSKit/Build Scene/Desert Outpost", false, 1)]
        private static void BuildDesert() => BuildScene("Desert Outpost");

        [MenuItem("FPSKit/Build Scene/Snowbound Station", false, 2)]
        private static void BuildSnow() => BuildScene("Snowbound Station");

        [MenuItem("FPSKit/Build Scene/Night Rooftop", false, 3)]
        private static void BuildRooftop() => BuildScene("Night Rooftop");

        [MenuItem("FPSKit/Build Scene/Abandoned Subway", false, 4)]
        private static void BuildSubway() => BuildScene("Abandoned Subway");

        [MenuItem("FPSKit/Build Scene/Mars Colony", false, 5)]
        private static void BuildMars() => BuildScene("Mars Colony");

        [MenuItem("FPSKit/Build Scene/From Selected Theme Asset", false, 19)]
        private static void BuildFromSelectedTheme()
            => BuildSceneFromTheme(Selection.activeObject as LevelTheme);

        [MenuItem("FPSKit/Build Scene/From Selected Theme Asset", true)]
        private static bool BuildFromSelectedThemeValidate()
            => Selection.activeObject is LevelTheme;

        [MenuItem("FPSKit/Build Scene/Build ALL Themes", false, 20)]
        private static void BuildAll()
        {
            if (!EditorUtility.DisplayDialog("Build every theme",
                "This builds and saves one scene per theme.\n\nUnsaved changes in the current scene will be lost.",
                "Build them", "Cancel")) return;

            foreach (var name in FPSKitThemes.Names) BuildScene(name, askFirst: false);

            EditorUtility.DisplayDialog("FPSKit",
                $"{FPSKitThemes.Names.Length} scenes saved to {SceneFolder}.\n\n" +
                "They were added to Build Settings so the in-game restart works.", "Nice");
        }

        // ==================================================================
        // Drop gameplay into a level somebody else built.
        // ==================================================================
        [MenuItem("FPSKit/Add Gameplay To Current Scene", false, 21)]
        private static void AddGameplayMenu()
        {
            if (!EditorUtility.DisplayDialog("Add gameplay to this scene",
                "Adds the player, enemies, wave manager, HUD, post processing and a baked " +
                "NavMesh to the scene that is open right now.\n\n" +
                "Your level geometry and baked lighting are left alone. Colliders are moved to " +
                "the Environment layer so AI cover and bullet hits work, and any mesh without a " +
                "collider gets one.\n\n" +
                "Use File > Save As afterwards so you keep the original map intact.",
                "Add it", "Cancel")) return;

            AddGameplayToCurrentScene("Industrial Warehouse");
        }

        public static void AddGameplayToCurrentScene(string themeName)
        {
            EnsureProjectTagsAndLayers();

            // The pipeline assets are provisioned for the same reason the tags are: a
            // generated arena assumes shadows reach across it and that its hard-edged
            // primitives are antialiased. Idempotent, and silent when nothing changed.
            FPSKitGraphics.Apply();
            EnsureFolders();

            _theme = FPSKitThemes.GetOrCreate(themeName);

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            // Running twice would leave two players and two cameras fighting.
            if (GameObject.FindGameObjectWithTag("Player") != null)
            {
                EditorUtility.DisplayDialog("Already set up",
                    "This scene already has a Player. Delete the Player, WaveManager, " +
                    "HUD Canvas, SpawnPoints and NavMesh objects first if you want to " +
                    "start over.", "OK");
                return;
            }

            int relayered = PrepareExistingGeometry(out int collidersAdded);

            BakeNavMesh();
            Vector3 spawn = FindPlayerSpawn();

            var impacts = CreateImpactLibrary();
            var weaponData = CreateWeaponData(impacts);

            var player = BuildPlayer(weaponData);
            player.transform.position = spawn;

            var enemyPrefab = LoadOrBuildEnemyPrefab();
            var spawnPoints = BuildSpawnPointsAround(spawn);
            var wave = BuildWaveManager(enemyPrefab, spawnPoints, player.transform);

            BuildGameDirector();
            BuildHUD(player, wave);
            BuildPostProcessing(player);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = player;

            // A camera is the one thing that must exist, so confirm it rather than assume.
            var camera = player.GetComponentInChildren<Camera>();
            string cameraLine = camera != null
                ? $"Camera: {camera.name} (tag {camera.tag})"
                : "WARNING: no camera was created -- the Game view will stay black.";

            string summary =
                $"Gameplay added to \"{scene.name}\".\n\n" +
                $"{cameraLine}\n" +
                $"Player spawned at {spawn}\n" +
                $"{relayered} objects moved to the Environment layer\n" +
                $"{collidersAdded} missing colliders added\n" +
                $"{spawnPoints.Length} spawn points placed on the NavMesh\n\n" +
                "Use File > Save As to keep this, then press Play.";

            Debug.Log("<color=lime>[FPSKit]</color> " + summary, player);
            EditorUtility.DisplayDialog("FPSKit", summary, "Got it");
        }

        /// <summary>
        /// Makes somebody else's level work with our systems: everything solid ends up
        /// on the Environment layer (AI line of sight and bullet masks depend on it),
        /// and meshes with no collider get one so you cannot shoot through walls.
        /// </summary>
        private static int PrepareExistingGeometry(out int collidersAdded)
        {
            int envLayer = LayerMask.NameToLayer("Environment");
            int relayered = 0;
            collidersAdded = 0;

            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.CompareTag("Player") || root.CompareTag("Enemy")) continue;
                if (root.GetComponentInChildren<Camera>() != null) continue;

                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    if (filter.GetComponent<Collider>() != null) continue;

                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    collidersAdded++;
                }

                foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                {
                    if (collider.gameObject.layer == envLayer) continue;

                    collider.gameObject.layer = envLayer;
                    relayered++;
                }
            }

            return relayered;
        }

        /// <summary>Nearest standable point to the world origin.</summary>
        private static Vector3 FindPlayerSpawn()
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(Vector3.zero, out var hit, 150f,
                                                      UnityEngine.AI.NavMesh.AllAreas))
                return hit.position + Vector3.up * 1.1f;

            if (Physics.Raycast(new Vector3(0f, 300f, 0f), Vector3.down, out var ground, 1000f))
                return ground.point + Vector3.up * 1.1f;

            return Vector3.up * 1.1f;
        }

        /// <summary>Fallback spawn ring. Dynamic spawning around the player does the real work.</summary>
        private static Transform[] BuildSpawnPointsAround(Vector3 centre)
        {
            var root = new GameObject("SpawnPoints").transform;
            var list = new List<Transform>();

            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI * 2f / 12f;
                float radius = 22f + (i % 3) * 9f;

                Vector3 candidate = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

                if (!UnityEngine.AI.NavMesh.SamplePosition(candidate, out var hit, 12f,
                                                           UnityEngine.AI.NavMesh.AllAreas)) continue;

                var point = new GameObject("Spawn_" + i).transform;
                point.SetParent(root);
                point.position = hit.position;
                list.Add(point);
            }

            if (list.Count == 0)
                Debug.LogWarning("[FPSKit] No spawn points landed on the NavMesh. " +
                                 "Check that the level bakes a walkable surface.", root);

            return list.ToArray();
        }

        /// <summary>Reuses an existing Enemy prefab so a character built in Enemy Setup survives.</summary>
        private static GameObject LoadOrBuildEnemyPrefab()
        {
            string path = AssetFolder + "/Enemy.prefab";

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return existing != null ? UpgradeEnemyPrefab(path) : BuildEnemyPrefab();
        }

        /// <summary>
        /// Adds whatever newer systems expect to a prefab that predates them, without
        /// touching anything already on it. A rigged character set up in Enemy Setup is
        /// exactly the thing we must not rebuild from scratch, so it gets patched instead.
        ///
        /// Everything here is gated on the field being empty, so a prefab that has been
        /// given its own clips or effects keeps them.
        /// </summary>
        private static GameObject UpgradeEnemyPrefab(string path)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;

            if (contents.GetComponent<EnemyHealthBar>() == null)
            {
                var bar = contents.AddComponent<EnemyHealthBar>();
                bar.barMaterial = CreateBarMaterial();
                bar.heightOffset = 2.6f;
                changed = true;
            }

            // Anything it needs to be heard. A prefab built before the kit had audio has
            // an AI wired for clips it was never given, so a ranged one shoots silently.
            var ai = contents.GetComponent<EnemyAI>();
            if (ai != null)
            {
                if (contents.GetComponent<AudioSource>() == null)
                {
                    var src = contents.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    src.spatialBlend = 1f;
                    src.maxDistance = 30f;
                    changed = true;
                }

                changed |= FillClip(ref ai.alertClip, "SFX/enemy_alert.wav");
                changed |= FillClip(ref ai.attackClip, "SFX/enemy_attack.wav");
                changed |= FillClip(ref ai.deathClip, "SFX/enemy_death.wav");
                changed |= FillClip(ref ai.fireClip, "SFX/weapon_fire.wav");

                // And anything it needs to be seen. muzzlePoint is deliberately left
                // alone: on a hand-rigged character there is no way to guess where the
                // barrel is, and EnemyAI already falls back to firing from the eyes.
                if (ai.tracerPrefab == null)
                {
                    ai.tracerPrefab = CreateTracerPrefab();
                    ai.tracerSpeed = 170f;
                    changed = true;
                }

                if (ai.muzzleFlashPrefab == null)
                {
                    ai.muzzleFlashPrefab = CreateMuzzleFlashPrefab();
                    changed = true;
                }
            }

            if (changed) PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>
        /// Fills an empty clip slot, reporting whether it actually filled one. Returns
        /// false when the slot already held something or the clip is not in the project,
        /// so neither case marks the prefab dirty.
        /// </summary>
        private static bool FillClip(ref AudioClip slot, string relativePath)
        {
            if (slot != null) return false;

            var clip = Clip(relativePath);
            if (clip == null) return false;

            slot = clip;
            return true;
        }

        // ==================================================================
        public static void BuildScene(string themeName, bool askFirst = true)
        {
            if (askFirst && !EditorUtility.DisplayDialog($"Build \"{themeName}\"",
                "This creates a new scene with a player, enemies, spawn points and HUD.\n\n" +
                "Any unsaved changes in the current scene will be lost.",
                "Build it", "Cancel")) return;

            EnsureProjectTagsAndLayers();

            // The pipeline assets are provisioned for the same reason the tags are: a
            // generated arena assumes shadows reach across it and that its hard-edged
            // primitives are antialiased. Idempotent, and silent when nothing changed.
            FPSKitGraphics.Apply();
            EnsureFolders();

            BuildFromTheme(FPSKitThemes.GetOrCreate(themeName), themeName);
        }

        /// <summary>
        /// Builds from any LevelTheme asset, whether or not it is one of the built-in
        /// six. This is the seam that makes a new arena a no-code job: duplicate a theme
        /// asset, retune sky, palette and layout counts, select it, build.
        /// </summary>
        public static void BuildSceneFromTheme(LevelTheme theme, bool askFirst = true)
        {
            if (theme == null)
            {
                Debug.LogError("[FPSKit] No LevelTheme given to build from.");
                return;
            }

            string sceneName = string.IsNullOrWhiteSpace(theme.themeName) ? theme.name : theme.themeName;

            if (askFirst && !EditorUtility.DisplayDialog($"Build \"{sceneName}\"",
                "This creates a new scene from the selected theme asset.\n\n" +
                "Any unsaved changes in the current scene will be lost.",
                "Build it", "Cancel")) return;

            EnsureProjectTagsAndLayers();

            // The pipeline assets are provisioned for the same reason the tags are: a
            // generated arena assumes shadows reach across it and that its hard-edged
            // primitives are antialiased. Idempotent, and silent when nothing changed.
            FPSKitGraphics.Apply();
            EnsureFolders();

            BuildFromTheme(theme, sceneName);
        }

        /// <summary>Everything after the theme has been resolved. Destructive: replaces the open scene.</summary>
        private static void BuildFromTheme(LevelTheme theme, string sceneName)
        {
            _theme = theme;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            BuildLighting();
            BuildArena();

            var impacts = CreateImpactLibrary();
            var weaponData = CreateWeaponData(impacts);
            var player = BuildPlayer(weaponData);
            var enemyPrefab = BuildEnemyPrefab();
            var spawnPoints = BuildSpawnPoints();

            BakeNavMesh();

            var wave = BuildWaveManager(enemyPrefab, spawnPoints, player.transform);
            BuildGameDirector();
            BuildHUD(player, wave);
            BuildPostProcessing(player);
            BuildAtmosphereExtras();

            EditorSceneManager.MarkSceneDirty(scene);
            SaveSceneAndRegister(scene, sceneName);

            Selection.activeGameObject = player;

            Debug.Log($"<color=lime>[FPSKit]</color> \"{sceneName}\" built. Press Play. " +
                      "Arrows move, Space fires, double-tap Space sprints, left click jumps, " +
                      "right click aims, R reloads, Escape pauses. " +
                      "Rebind in FPSKit_Generated/Controls.asset.");
        }

        // ==================================================================
        private static void BuildLighting()
        {
            var sunGo = GameObject.Find("Directional Light");
            if (sunGo == null) sunGo = new GameObject("Directional Light");

            var sun = sunGo.GetComponent<Light>() ?? sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;

            _theme.ApplyEnvironment(sun, CreateSkybox());
        }

        private static Material CreateSkybox()
        {
            if (_theme.skyboxOverride != null) return _theme.skyboxOverride;

            string path = $"{MaterialFolder}/Sky_{SafeName(_theme.themeName)}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null) return existing;

            var mat = existing != null ? existing : new Material(shader);
            mat.shader = shader;
            mat.SetColor("_SkyTint", _theme.skyTint);
            mat.SetColor("_GroundColor", _theme.skyGroundColor);
            mat.SetFloat("_AtmosphereThickness", _theme.atmosphereThickness);
            mat.SetFloat("_Exposure", _theme.skyExposure);
            mat.SetFloat("_SunDisk", 2f);          // 2 = high quality sun disk
            mat.SetFloat("_SunSize", 0.035f);

            if (existing == null) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);

            return mat;
        }

        // ==================================================================
        // Arena generation
        //
        // Rather than scattering loose cubes, the level is composed from five
        // primitives: roofless rooms with doorways, raised decks with ramps,
        // chest-high cover walls, crate stacks and pillars. Placement is
        // rejection-sampled against a list of claimed circles, so nothing
        // intersects and every spawn point stays clear and reachable.
        // ==================================================================

        /// <summary>Claimed footprints as (x, z, radius). Reset on every build.</summary>
        private static readonly List<Vector3> _claimed = new List<Vector3>();

        // Resolved once per build. Null means "no art-pack material, use flat colour".
        private static Material _floorMat, _perimeterMat, _roomWallMat, _coverMat, _crateMat;

        /// <summary>
        /// Builds a tiled copy of an art-pack material. The source is left untouched --
        /// editing it directly would change tiling for every prop in the pack too.
        /// </summary>
        private static Material TiledCopy(Material source, string name, Vector2 tiling)
        {
            if (source == null) return null;

            string path = $"{MaterialFolder}/{SafeName(_theme.themeName)}_{name}_Tiled.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat == null)
            {
                mat = new Material(source);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = source.shader;
                mat.CopyPropertiesFromMaterial(source);
            }

            if (mat.HasProperty("_BaseMap")) mat.SetTextureScale("_BaseMap", tiling);
            if (mat.HasProperty("_MainTex")) mat.SetTextureScale("_MainTex", tiling);
            if (mat.HasProperty("_BumpMap")) mat.SetTextureScale("_BumpMap", tiling);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void ResolveSurfaceMaterials()
        {
            float size = _theme.arenaSize;
            float floorRepeat = Mathf.Max(1f, size / Mathf.Max(0.5f, _theme.floorTextureSize));
            float wallRepeatX = Mathf.Max(1f, size / Mathf.Max(0.5f, _theme.wallTextureSize));
            float wallRepeatY = Mathf.Max(1f, _theme.wallHeight / Mathf.Max(0.5f, _theme.wallTextureSize));

            _floorMat = TiledCopy(_theme.floorMaterial, "Floor", new Vector2(floorRepeat, floorRepeat));
            _perimeterMat = TiledCopy(_theme.wallMaterial, "Perimeter", new Vector2(wallRepeatX, wallRepeatY));
            _roomWallMat = TiledCopy(_theme.wallMaterial, "RoomWall", new Vector2(3f, 1.5f));

            var coverSource = _theme.coverMaterial != null ? _theme.coverMaterial : _theme.wallMaterial;
            _coverMat = TiledCopy(coverSource, "Cover", new Vector2(2f, 1f));
            _crateMat = TiledCopy(_theme.crateMaterial, "Crate", Vector2.one);
        }

        private static void BuildArena()
        {
            _claimed.Clear();

            var root = new GameObject("Arena").transform;
            int envLayer = LayerMask.NameToLayer("Environment");
            var rng = new System.Random(_theme.randomSeed);

            float size = _theme.arenaSize;
            float half = size * 0.5f;

            ResolveSurfaceMaterials();
            BuildFloorAndPerimeter(root, envLayer, size, half);

            // Reserve the player start and all eight enemy spawn points first,
            // so no structure can ever bury one.
            Claim(0f, 0f, 9f);
            float spawnRadius = size * 0.4f;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 2f / 8f;
                Claim(Mathf.Cos(angle) * spawnRadius, Mathf.Sin(angle) * spawnRadius, 4f);
            }

            BuildRooms(root, envLayer, rng, half);
            BuildPlatforms(root, envLayer, rng, half);
            BuildCoverWalls(root, envLayer, rng, half);
            BuildCrateStacks(root, envLayer, rng, half);
            BuildPillars(root, envLayer, rng, half);
            BuildProps(root, envLayer, rng, half);
            BuildAccentLights(root, rng);
        }

        // ------------------------------------------------------------------
        private static void BuildFloorAndPerimeter(Transform root, int layer, float size, float half)
        {
            float h = _theme.wallHeight;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(root, false);
            floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(size, 1f, size);
            floor.tag = _theme.floorTag;
            floor.layer = layer;
            floor.GetComponent<Renderer>().sharedMaterial = _floorMat != null
                ? _floorMat
                : MakeMaterial("Floor", _theme.floorColor, _theme.floorSmoothness, 0f);

            CreateBlock(root, new Vector3(0f, h * 0.5f, half), new Vector3(size, h, 1f), 0f,
                        layer, _theme.wallTag, _theme.wallColor, _theme.wallSmoothness, 0f, "Perimeter", _perimeterMat);
            CreateBlock(root, new Vector3(0f, h * 0.5f, -half), new Vector3(size, h, 1f), 0f,
                        layer, _theme.wallTag, _theme.wallColor, _theme.wallSmoothness, 0f, "Perimeter", _perimeterMat);
            CreateBlock(root, new Vector3(half, h * 0.5f, 0f), new Vector3(1f, h, size), 0f,
                        layer, _theme.wallTag, _theme.wallColor, _theme.wallSmoothness, 0f, "Perimeter", _perimeterMat);
            CreateBlock(root, new Vector3(-half, h * 0.5f, 0f), new Vector3(1f, h, size), 0f,
                        layer, _theme.wallTag, _theme.wallColor, _theme.wallSmoothness, 0f, "Perimeter", _perimeterMat);
        }

        // ------------------------------------------------------------------
        private static void BuildRooms(Transform root, int layer, System.Random rng, float half)
        {
            for (int i = 0; i < _theme.ScaledCount(_theme.roomCount); i++)
            {
                float w = Rand(rng, 9f, 15f);
                float d = Rand(rng, 9f, 15f);
                float h = Rand(rng, 3.4f, 4.6f);

                if (!TryClaim(rng, half, Mathf.Max(w, d) * 0.8f, out Vector2 p)) continue;

                var group = new GameObject($"Room_{i}").transform;
                group.SetParent(root, false);
                group.localPosition = new Vector3(p.x, 0f, p.y);
                group.localRotation = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);

                const float thickness = 0.5f;
                const float door = 3.2f;

                // Opposite doorways guarantee a path straight through the room.
                BuildWall(group, layer, new Vector3(0f, h * 0.5f, d * 0.5f), w, h, thickness, true, true, door);
                BuildWall(group, layer, new Vector3(0f, h * 0.5f, -d * 0.5f), w, h, thickness, true, true, door);
                BuildWall(group, layer, new Vector3(w * 0.5f, h * 0.5f, 0f), d, h, thickness, false, rng.NextDouble() < 0.6, door);
                BuildWall(group, layer, new Vector3(-w * 0.5f, h * 0.5f, 0f), d, h, thickness, false, rng.NextDouble() < 0.6, door);

                // Something inside, so entering is worth the risk.
                if (rng.NextDouble() < 0.7) BuildCrateCluster(group, layer, rng, Vector3.zero);
            }
        }

        private static void BuildWall(Transform parent, int layer, Vector3 center, float length, float height,
                                      float thickness, bool alongX, bool hasDoor, float doorWidth)
        {
            Vector3 Scale(float span) => alongX
                ? new Vector3(span, height, thickness)
                : new Vector3(thickness, height, span);

            if (!hasDoor || length - doorWidth < 2f)
            {
                CreateBlock(parent, center, Scale(length), 0f, layer, _theme.wallTag,
                            _theme.wallColor, _theme.wallSmoothness, 0f, "Wall", _roomWallMat);
                return;
            }

            float segment = (length - doorWidth) * 0.5f;
            float offset = (doorWidth + segment) * 0.5f;
            Vector3 dir = alongX ? Vector3.right : Vector3.forward;

            CreateBlock(parent, center + dir * offset, Scale(segment), 0f, layer, _theme.wallTag,
                        _theme.wallColor, _theme.wallSmoothness, 0f, "Wall", _roomWallMat);
            CreateBlock(parent, center - dir * offset, Scale(segment), 0f, layer, _theme.wallTag,
                        _theme.wallColor, _theme.wallSmoothness, 0f, "Wall", _roomWallMat);

            // Lintel over the opening, leaving 2.6 m of headroom for the agents.
            float lintel = height - 2.6f;
            if (lintel < 0.4f) return;

            Vector3 lintelScale = alongX
                ? new Vector3(doorWidth, lintel, thickness)
                : new Vector3(thickness, lintel, doorWidth);

            CreateBlock(parent, center + Vector3.up * (height * 0.5f - lintel * 0.5f), lintelScale, 0f,
                        layer, _theme.wallTag, _theme.wallColor, _theme.wallSmoothness, 0f, "Lintel", _roomWallMat);
        }

        // ------------------------------------------------------------------
        private static void BuildPlatforms(Transform root, int layer, System.Random rng, float half)
        {
            const float rampAngle = 22f;   // shallow enough that the NavMesh bakes over it

            float sin = Mathf.Sin(rampAngle * Mathf.Deg2Rad);
            float tan = Mathf.Tan(rampAngle * Mathf.Deg2Rad);

            for (int i = 0; i < _theme.ScaledCount(_theme.platformCount); i++)
            {
                float w = Rand(rng, 7f, 11f);
                float d = Rand(rng, 7f, 11f);
                float h = Rand(rng, 2.2f, 3.2f);

                float run = h / tan;           // horizontal distance the ramp covers
                float length = h / sin;        // the ramp slab itself

                if (!TryClaim(rng, half, Mathf.Max(w, d) * 0.5f + run + 2f, out Vector2 p)) continue;

                var group = new GameObject($"Platform_{i}").transform;
                group.SetParent(root, false);
                group.localPosition = new Vector3(p.x, 0f, p.y);
                group.localRotation = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);

                Color deck = _theme.RandomCoverColor(rng);

                CreateBlock(group, new Vector3(0f, (h - 0.3f) * 0.5f, 0f),
                            new Vector3(w - 0.6f, h - 0.3f, d - 0.6f), 0f, layer, _theme.coverTag,
                            Shade(deck, 0.82f), 0.25f, _theme.coverMetallic, "Support", _coverMat);

                CreateBlock(group, new Vector3(0f, h - 0.15f, 0f), new Vector3(w, 0.3f, d), 0f,
                            layer, _theme.coverTag, deck, 0.3f, _theme.coverMetallic, "Deck", _coverMat);

                // Ramp, tilted so its high end lands exactly on the deck edge.
                var ramp = CreateBlock(group, new Vector3(0f, h * 0.5f, -(d * 0.5f + run * 0.5f)),
                                       new Vector3(4f, 0.35f, length), 0f, layer, _theme.coverTag,
                                       Shade(deck, 0.9f), 0.25f, _theme.coverMetallic, "Ramp", _coverMat);
                ramp.transform.localRotation = Quaternion.Euler(-rampAngle, 0f, 0f);

                // Waist-high lip so the deck actually works as cover.
                CreateBlock(group, new Vector3(0f, h + 0.5f, d * 0.5f - 0.2f), new Vector3(w, 1f, 0.4f),
                            0f, layer, _theme.coverTag, deck, 0.3f, _theme.coverMetallic, "Lip", _coverMat);
            }
        }

        // ------------------------------------------------------------------
        private static void BuildCoverWalls(Transform root, int layer, System.Random rng, float half)
        {
            for (int i = 0; i < _theme.ScaledCount(_theme.coverWallCount); i++)
            {
                float length = Rand(rng, _theme.coverWidthRange.x, _theme.coverWidthRange.y);
                float h = Rand(rng, _theme.coverHeightRange.x, _theme.coverHeightRange.y);

                if (!TryClaim(rng, half, length * 0.6f, out Vector2 p)) continue;

                CreateBlock(root, new Vector3(p.x, h * 0.5f, p.y), new Vector3(length, h, 0.6f),
                            Rand(rng, 0f, 360f), layer, _theme.coverTag, _theme.RandomCoverColor(rng),
                            0.3f, _theme.coverMetallic, "CoverWall", _coverMat);
            }
        }

        private static void BuildCrateStacks(Transform root, int layer, System.Random rng, float half)
        {
            for (int i = 0; i < _theme.ScaledCount(_theme.crateStackCount); i++)
            {
                if (!TryClaim(rng, half, 3f, out Vector2 p)) continue;
                BuildCrateCluster(root, layer, rng, new Vector3(p.x, 0f, p.y));
            }
        }

        private static void BuildCrateCluster(Transform parent, int layer, System.Random rng, Vector3 origin)
        {
            Color color = _theme.RandomCoverColor(rng);
            int columns = rng.Next(1, 4);

            for (int c = 0; c < columns; c++)
            {
                float crate = Rand(rng, 1.1f, 1.7f);
                int stack = rng.Next(1, 4);
                float ox = (c - (columns - 1) * 0.5f) * crate * 1.05f;
                float oz = Rand(rng, -0.4f, 0.4f);

                for (int k = 0; k < stack; k++)
                    CreateBlock(parent, origin + new Vector3(ox, crate * (k + 0.5f), oz),
                                Vector3.one * crate, Rand(rng, -12f, 12f), layer, "Wood",
                                Shade(color, 1f - k * 0.07f), 0.2f, 0f, "Crate", _crateMat);
            }
        }

        private static void BuildPillars(Transform root, int layer, System.Random rng, float half)
        {
            for (int i = 0; i < _theme.ScaledCount(_theme.pillarCount); i++)
            {
                if (!TryClaim(rng, half, 2f, out Vector2 p)) continue;

                float thickness = Rand(rng, 1f, 1.6f);
                CreateBlock(root, new Vector3(p.x, _theme.wallHeight * 0.5f, p.y),
                            new Vector3(thickness, _theme.wallHeight, thickness), Rand(rng, 0f, 45f),
                            layer, _theme.wallTag, _theme.wallColor, _theme.wallSmoothness, 0f, "Pillar", _coverMat);
            }
        }

        private static void BuildProps(Transform root, int layer, System.Random rng, float half)
        {
            if (_theme.propPrefabs == null || _theme.propPrefabs.Length == 0) return;

            int attempts = Mathf.Max(1, _theme.ScaledCount(_theme.propCount));

            for (int i = 0; i < attempts; i++)
            {
                var prefab = _theme.propPrefabs[rng.Next(_theme.propPrefabs.Length)];
                if (prefab == null) continue;

                // Claim the prop's real footprint. A fixed radius would let a 15 m
                // hangar overlap everything around it.
                if (!TryClaim(rng, half, EstimatePropRadius(prefab), out Vector2 p)) continue;

                var prop = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                prop.transform.localPosition = new Vector3(p.x, 0f, p.y);
                prop.transform.localRotation = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);

                SetLayerRecursive(prop, layer);
                SetTagRecursive(prop, _theme.coverTag);
                EnsureColliders(prop);
            }
        }

        /// <summary>Horizontal half-extent of a prefab, used as its placement footprint.</summary>
        private static float EstimatePropRadius(GameObject prefab)
        {
            var renderers = prefab.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0) return 2f;

            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            return Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z), 1f, 20f);
        }

        /// <summary>
        /// Art-pack prefabs frequently ship with no colliders, which makes them
        /// invisible to raycasts -- bullets would pass straight through them.
        /// </summary>
        private static void EnsureColliders(GameObject root)
        {
            if (root.GetComponentInChildren<Collider>() != null) return;

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;

                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
            }
        }

        // ------------------------------------------------------------------
        private static void Claim(float x, float z, float radius) => _claimed.Add(new Vector3(x, z, radius));

        /// <summary>Rejection-samples a free spot. Returns false if the arena is too crowded.</summary>
        private static bool TryClaim(System.Random rng, float half, float radius, out Vector2 point,
                                     int attempts = 60)
        {
            float limit = Mathf.Max(4f, half - radius - 3f);

            for (int i = 0; i < attempts; i++)
            {
                float x = Rand(rng, -limit, limit);
                float z = Rand(rng, -limit, limit);

                bool clear = true;
                foreach (var c in _claimed)
                {
                    float dx = c.x - x;
                    float dz = c.y - z;
                    float minimum = c.z + radius + 1.5f;

                    if (dx * dx + dz * dz < minimum * minimum) { clear = false; break; }
                }

                if (!clear) continue;

                Claim(x, z, radius);
                point = new Vector2(x, z);
                return true;
            }

            point = Vector2.zero;
            return false;
        }

        private static float Rand(System.Random rng, float min, float max)
            => min + (float)rng.NextDouble() * (max - min);

        private static Color Shade(Color c, float factor)
            => new Color(c.r * factor, c.g * factor, c.b * factor, 1f);

        private static GameObject CreateBlock(Transform parent, Vector3 localPosition, Vector3 localScale,
                                              float yaw, int layer, string tag, Color color,
                                              float smoothness, float metallic, string name,
                                              Material overrideMaterial = null)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = localPosition;
            block.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            block.transform.localScale = localScale;
            block.tag = tag;
            block.layer = layer;

            block.GetComponent<Renderer>().sharedMaterial = overrideMaterial != null
                ? overrideMaterial
                : MakeMaterial($"{name}_{ColorKey(color)}", color, smoothness, metallic);

            return block;
        }

        private static void BuildAccentLights(Transform parent, System.Random rng)
        {
            if (_theme.ScaledCount(_theme.accentLightCount) <= 0) return;

            var root = new GameObject("AccentLights").transform;
            root.SetParent(parent);

            float spread = _theme.arenaSize - 8f;

            for (int i = 0; i < _theme.ScaledCount(_theme.accentLightCount); i++)
            {
                var go = new GameObject($"AccentLight_{i}");
                go.transform.SetParent(root);
                go.transform.position = new Vector3(
                    (float)(rng.NextDouble() * spread - spread * 0.5f),
                    _theme.accentLightHeight,
                    (float)(rng.NextDouble() * spread - spread * 0.5f));

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = _theme.accentLightColor;
                light.intensity = _theme.accentLightIntensity;
                light.range = _theme.accentLightRange;
                light.shadows = LightShadows.None;   // keep the frame budget sane
            }
        }

        private static void BuildAtmosphereExtras()
        {
            if (_theme.weatherPrefab != null)
            {
                var weather = (GameObject)PrefabUtility.InstantiatePrefab(_theme.weatherPrefab);
                weather.name = "Weather";
                weather.transform.position = new Vector3(0f, _theme.wallHeight + 8f, 0f);
            }

            // The theme's own loop wins; the generated bed is the floor, so an arena is
            // never silent just because its theme predates the audio set.
            var ambience = _theme.ambienceLoop != null ? _theme.ambienceLoop : Clip("Ambience/arena_bed.wav");
            if (ambience == null) return;

            var go = new GameObject("Ambience");
            var source = go.AddComponent<AudioSource>();
            source.clip = ambience;
            source.loop = true;
            source.playOnAwake = true;
            source.volume = _theme.ambienceVolume > 0f ? _theme.ambienceVolume : 0.35f;

            // 2D on purpose: the bed has no position in the world, and this is why the
            // import policy keeps ambience in stereo while forcing every SFX to mono.
            source.spatialBlend = 0f;
        }

        // ==================================================================
        // Audio and VFX
        // ==================================================================

        private const string AudioFolder = "Assets/Audio";

        /// <summary>
        /// Loads one of the kit's clips by its path under Assets/Audio, or null when it
        /// is not there. Every audio field in the kit is optional, so a project that
        /// deleted the placeholder set still builds and still plays -- just quietly.
        /// </summary>
        private static AudioClip Clip(string relativePath)
            => AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioFolder}/{relativePath}");

        /// <summary>Loads several clips, silently dropping any that are missing.</summary>
        private static AudioClip[] Clips(params string[] relativePaths)
        {
            var found = new List<AudioClip>(relativePaths.Length);

            foreach (string relativePath in relativePaths)
            {
                var clip = Clip(relativePath);
                if (clip != null) found.Add(clip);
            }

            return found.ToArray();
        }

        /// <summary>Saves a freshly built object over its prefab and drops the scene copy.</summary>
        private static GameObject SaveGeneratedPrefab(GameObject root, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// A material that reads as hot -- tracers, muzzle flash, impact sparks. Shares
        /// MakeTintableMaterial's emission setup (see the note there on why the GI flags
        /// are not None) but keeps a real colour, since nothing drives these through a
        /// MaterialPropertyBlock the way the enemies do.
        /// </summary>
        private static Material MakeGlowMaterial(string name, Color color, float intensity)
        {
            var mat = MakeTintableMaterial(name, color, 0.3f, 0f, shared: true);
            if (mat == null) return null;

            mat.SetColor("_EmissionColor", color * intensity);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// The round in flight, and the reason you can now see your shots. Weapon.
        /// SpawnTracer has always known how to fire one, but WeaponData.tracerPrefab was
        /// never filled in, so every shot was an invisible raycast.
        ///
        /// Weapon orients the tracer with LookRotation and walks it to the impact point,
        /// so the root has to stay unrotated and the mesh has to run along +Z. A
        /// primitive cylinder's axis is Y, which is what the pitch on the child is for.
        /// </summary>
        private static GameObject CreateTracerPrefab()
        {
            var glow = MakeGlowMaterial("Tracer", new Color(1f, 0.83f, 0.45f), 8f);

            var root = new GameObject("Tracer");

            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Shaft";
            shaft.transform.SetParent(root.transform, false);
            shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // A primitive cylinder is two units tall, so 0.2 gives a 0.4m streak: long
            // enough to read as a line at speed, short enough not to look like a laser.
            shaft.transform.localScale = new Vector3(0.03f, 0.2f, 0.03f);
            Object.DestroyImmediate(shaft.GetComponent<Collider>());
            shaft.GetComponent<Renderer>().sharedMaterial = glow;

            // The streak behind it. A tracer at 240 m/s crosses the arena in a handful of
            // frames, so without a trail it is a few stills rather than a line.
            // Flies itself rather than being walked by a coroutine on whoever fired it,
            // which is what lets an enemy's tracer outlive the enemy that fired it.
            root.AddComponent<TracerProjectile>();

            var trail = root.AddComponent<TrailRenderer>();
            trail.time = 0.055f;
            trail.startWidth = 0.05f;
            trail.endWidth = 0f;
            trail.minVertexDistance = 0.08f;
            trail.sharedMaterial = glow;
            trail.alignment = LineAlignment.View;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;

            return SaveGeneratedPrefab(root, AssetFolder + "/Tracer.prefab");
        }

        /// <summary>
        /// The flash at the barrel. Weapon parents this to the muzzle and destroys it a
        /// second later, which is a cleanup timer rather than a duration -- TransientFlash
        /// is what makes it last the two frames a muzzle flash should.
        /// </summary>
        private static GameObject CreateMuzzleFlashPrefab()
        {
            var glow = MakeGlowMaterial("MuzzleFlash", new Color(1f, 0.78f, 0.38f), 12f);

            var root = new GameObject("MuzzleFlash");

            // A core plus two crossed blades. Three cheap primitives beat one sphere,
            // because the blades give the flash a shape rather than a blob -- and they
            // face the player, who is looking straight down the barrel.
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Core";
            core.transform.SetParent(root.transform, false);
            core.transform.localScale = Vector3.one * 0.07f;
            Object.DestroyImmediate(core.GetComponent<Collider>());
            core.GetComponent<Renderer>().sharedMaterial = glow;

            for (int i = 0; i < 2; i++)
            {
                var blade = GameObject.CreatePrimitive(PrimitiveType.Quad);
                blade.name = "Blade_" + i;
                blade.transform.SetParent(root.transform, false);
                blade.transform.localRotation = Quaternion.Euler(0f, 0f, i * 90f);
                blade.transform.localScale = new Vector3(0.26f, 0.045f, 1f);
                Object.DestroyImmediate(blade.GetComponent<Collider>());
                blade.GetComponent<Renderer>().sharedMaterial = glow;
            }

            var flash = root.AddComponent<TransientFlash>();
            flash.lifetime = 0.045f;
            flash.startScale = 1f;
            flash.endScale = 0.2f;

            return SaveGeneratedPrefab(root, AssetFolder + "/MuzzleFlash.prefab");
        }

        /// <summary>
        /// The spark where a round lands. ImpactLibrary spawns it facing along the surface
        /// normal, so it needs no orientation of its own.
        /// </summary>
        private static GameObject CreateImpactPrefab(string name, Color color, float scale)
        {
            var glow = MakeGlowMaterial("Impact" + name, color, 6f);

            var root = new GameObject("Impact_" + name);

            var burst = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            burst.name = "Burst";
            burst.transform.SetParent(root.transform, false);
            burst.transform.localScale = Vector3.one * scale;
            Object.DestroyImmediate(burst.GetComponent<Collider>());
            burst.GetComponent<Renderer>().sharedMaterial = glow;

            // Brief enough that even an automatic only ever has one or two of these lit
            // at a time, and TransientFlash switches the object off, taking the light
            // with it rather than leaving it burning until the library's cleanup.
            var light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = 2.5f;
            light.range = 3f;
            light.shadows = LightShadows.None;

            var flash = root.AddComponent<TransientFlash>();
            flash.lifetime = 0.09f;
            flash.startScale = 1f;
            flash.endScale = 0.1f;

            return SaveGeneratedPrefab(root, $"{AssetFolder}/Impact_{name}.prefab");
        }

        // ==================================================================
        /// <summary>
        /// Surface tag to impact effect and sound. Created once, then re-stamped on every
        /// build: the asset is deliberately kept across rebuilds, so wiring done only on
        /// the run that created it could never be corrected by building again.
        /// </summary>
        private static ImpactLibrary CreateImpactLibrary()
        {
            string path = $"{AssetFolder}/ImpactLibrary.asset";

            var library = AssetDatabase.LoadAssetAtPath<ImpactLibrary>(path);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<ImpactLibrary>();
                AssetDatabase.CreateAsset(library, path);
            }

            library.entries = new[]
            {
                Impact("Concrete", new Color(1f, 0.92f, 0.75f), 0.09f, "SFX/impact_concrete.wav", 0.7f),
                Impact("Metal", new Color(1f, 0.85f, 0.5f), 0.1f, "SFX/impact_metal.wav", 0.75f),
                Impact("Wood", new Color(0.9f, 0.65f, 0.35f), 0.09f, "SFX/impact_wood.wav", 0.7f),
                Impact("Flesh", new Color(0.85f, 0.12f, 0.12f), 0.11f, "SFX/impact_flesh.wav", 0.85f)
            };

            // Anything untagged still sparks and still ticks, so a shot into imported
            // geometry that nobody remembered to tag does not read as a miss.
            library.fallback = Impact("Concrete", new Color(1f, 0.92f, 0.75f), 0.08f,
                                     "SFX/impact_concrete.wav", 0.55f);
            library.fallback.surfaceTag = "Default";

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<ImpactLibrary>(path);
        }

        private static ImpactLibrary.Entry Impact(string surfaceTag, Color color, float scale,
                                                  string clipPath, float volume)
        {
            return new ImpactLibrary.Entry
            {
                surfaceTag = surfaceTag,
                effectPrefab = CreateImpactPrefab(surfaceTag, color, scale),
                clips = Clips(clipPath),
                volume = volume,

                // Comfortably longer than TransientFlash takes to switch itself off, so
                // the cleanup never cuts the effect short.
                effectLifetime = 1f
            };
        }

        /// <summary>Key bindings live in their own asset so they can be rebound without code.</summary>
        private static ControlSettings CreateControlSettings()
        {
            string path = AssetFolder + "/Controls.asset";

            var existing = AssetDatabase.LoadAssetAtPath<ControlSettings>(path);
            if (existing != null) return existing;

            var controls = ScriptableObject.CreateInstance<ControlSettings>();
            AssetDatabase.CreateAsset(controls, path);
            AssetDatabase.SaveAssets();

            return AssetDatabase.LoadAssetAtPath<ControlSettings>(path);
        }

        /// <summary>
        /// The rifle's tuning. The stats are only written when the asset is first
        /// created, so a retuned rifle survives a rebuild -- but the audio and VFX hookup
        /// below is re-stamped every time, because those point at generated assets this
        /// builder owns and a stale reference there is a silent loss of effect.
        /// </summary>
        private static WeaponData CreateWeaponData(ImpactLibrary impacts)
        {
            string path = AssetFolder + "/TestRifle.asset";
            var existing = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
            if (existing != null) return StampWeaponFeedback(existing, impacts);

            var data = ScriptableObject.CreateInstance<WeaponData>();
            data.weaponName = "Test Rifle";
            data.fireMode = FireMode.Auto;
            data.roundsPerMinute = 650f;
            data.damage = 24f;
            data.maxRange = 200f;
            data.magazineSize = 30;
            data.reserveAmmo = 150;
            data.reloadTime = 2.1f;
            data.baseSpread = 0.6f;
            data.spreadPerShot = 0.45f;
            data.maxSpread = 5f;
            data.recoilVertical = 1.15f;
            data.recoilHorizontal = 0.4f;
            data.adsFieldOfView = 40f;
            data.adsPosition = new Vector3(0f, -0.02f, 0.12f);
            data.infiniteReserve = true;

            AssetDatabase.CreateAsset(data, path);
            StampWeaponFeedback(data, impacts);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<WeaponData>(path);
        }

        /// <summary>
        /// Everything the rifle needs to be seen and heard. Kept apart from the stats
        /// above so it can run against an existing asset too.
        ///
        /// tracerPrefab is the one that matters most: Weapon.SpawnTracer bails when it is
        /// null, which is why the rifle fired invisible bullets for as long as it has
        /// existed. The raycast, the damage and the impact were all working -- there was
        /// simply nothing to watch.
        /// </summary>
        private static WeaponData StampWeaponFeedback(WeaponData data, ImpactLibrary impacts)
        {
            data.impacts = impacts;

            data.fireClip = Clip("SFX/weapon_fire.wav");
            data.reloadClip = Clip("SFX/weapon_reload.wav");
            data.emptyClip = Clip("SFX/weapon_empty.wav");
            data.pitchVariance = 0.06f;

            data.muzzleFlashPrefab = CreateMuzzleFlashPrefab();
            data.tracerPrefab = CreateTracerPrefab();

            // Fast enough to feel like a bullet, slow enough to actually see cross the
            // arena. Real rounds are ten times this and would simply teleport.
            data.tracerSpeed = 240f;

            EditorUtility.SetDirty(data);
            return data;
        }

        // ==================================================================
        private static GameObject BuildPlayer(WeaponData weaponData)
        {
            var player = new GameObject("Player");
            player.tag = "Player";
            player.layer = LayerMask.NameToLayer("Player");
            player.transform.position = new Vector3(0f, 1f, 0f);

            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.35f;
            cc.slopeLimit = 50f;

            var hp = player.AddComponent<Health>();
            hp.maxHealth = 100f;

            // A shield on top of health is what makes a crowd survivable: it soaks the
            // first mistake and comes back on its own, so one bad corner costs a fight
            // rather than the whole run.
            hp.maxShield = 50f;
            hp.shieldRegenDelay = 4f;
            hp.shieldRegenPerSecond = 22f;

            hp.regenerates = true;
            hp.regenDelay = 7f;
            hp.regenPerSecond = 9f;

            // Six melee enemies landing hits on the same frame would otherwise delete
            // the player with no window to react to any of them.
            hp.invulnerabilityWindow = 0.25f;

            hp.destroyOnDeath = false;

            var playerAudio = player.AddComponent<AudioSource>();
            playerAudio.playOnAwake = false;

            var holder = new GameObject("CameraHolder");
            holder.transform.SetParent(player.transform);
            holder.transform.localPosition = new Vector3(0f, 1.65f, 0f);

            // Reuse the default Main Camera if the new scene made one.
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }

            cam.transform.SetParent(holder.transform);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;
            cam.fieldOfView = 75f;
            cam.nearClipPlane = 0.02f;

            var motor = player.AddComponent<PlayerMotor>();
            motor.cameraHolder = holder.transform;
            motor.footstepSource = playerAudio;

            // Four variants rather than one: a single footstep clip retriggered at walking
            // cadence is the most obviously synthetic sound a game can make.
            motor.footstepClips = Clips("SFX/footstep_01.wav", "SFX/footstep_02.wav",
                                        "SFX/footstep_03.wav", "SFX/footstep_04.wav");
            motor.landClip = Clip("SFX/land.wav");
            motor.controls = CreateControlSettings();

            // ---- weapon rig ----
            var weaponHolder = new GameObject("WeaponHolder");
            weaponHolder.transform.SetParent(cam.transform);
            weaponHolder.transform.localPosition = Vector3.zero;
            weaponHolder.AddComponent<WeaponSway>();

            var model = BuildWeaponModel(weaponHolder.transform);

            var muzzle = new GameObject("MuzzlePoint");
            muzzle.transform.SetParent(model.transform);

            // Real metres now, not the old cube's scaled local space: the model root is
            // an empty at unit scale, so this is simply the end of the barrel -- carried
            // through the same authoring scale the parts use.
            muzzle.transform.localPosition = new Vector3(0f, 0.005f, 0.47f) * GunScale;

            var muzzleLight = muzzle.AddComponent<Light>();
            muzzleLight.type = LightType.Point;
            muzzleLight.color = new Color(1f, 0.85f, 0.5f);
            muzzleLight.intensity = 6f;
            muzzleLight.range = 8f;
            muzzleLight.shadows = LightShadows.None;
            muzzleLight.enabled = false;

            var weapon = model.AddComponent<Weapon>();
            weapon.data = weaponData;
            weapon.fpsCamera = cam;
            weapon.muzzlePoint = muzzle.transform;
            weapon.muzzleLight = muzzleLight;
            weapon.audioSource = model.AddComponent<AudioSource>();
            weapon.audioSource.playOnAwake = false;
            weapon.hitMask = ~(1 << LayerMask.NameToLayer("Player"));

            return player;
        }

        /// <summary>
        /// The first-person rifle, built from primitives into something with a barrel, a
        /// grip and sights rather than the single stretched cube it used to be.
        ///
        /// The root is an empty at unit scale and everything hangs off it, which matters
        /// for more than tidiness: Weapon caches transform.localPosition as its hip pose
        /// and punches the model back along Z for recoil, so the thing carrying the
        /// Weapon component has to be an unscaled parent. Scaling lived on the old cube,
        /// which is why the muzzle point had to be expressed in scaled units.
        /// </summary>
        private static GameObject BuildWeaponModel(Transform holder)
        {
            var metal = MakeSharedMaterial("Gun_Metal", new Color(0.11f, 0.115f, 0.125f), 0.55f, 0.85f);
            var polymer = MakeSharedMaterial("Gun_Polymer", new Color(0.16f, 0.17f, 0.18f), 0.25f, 0f);
            var accent = MakeSharedMaterial("Gun_Accent", new Color(0.32f, 0.33f, 0.35f), 0.7f, 0.9f);

            var root = new GameObject("WeaponModel");
            root.transform.SetParent(holder, false);

            // Far enough forward that the stock is not sitting on the near clip plane,
            // which is what the old single cube got away with by having no stock.
            root.transform.localPosition = new Vector3(0.20f, -0.16f, 0.62f);

            // Slight inward yaw so the weapon reads as held across the body rather than
            // bolted to the camera facing dead ahead.
            root.transform.localRotation = Quaternion.Euler(0f, -3f, 0f);

            Transform parent = root.transform;

            GunPart(parent, "Receiver", PrimitiveType.Cube, metal,
                    new Vector3(0f, 0f, 0f), new Vector3(0.072f, 0.095f, 0.30f), Vector3.zero);

            GunPart(parent, "Handguard", PrimitiveType.Cube, polymer,
                    new Vector3(0f, -0.004f, 0.21f), new Vector3(0.06f, 0.065f, 0.16f), Vector3.zero);

            // A primitive cylinder is two units tall on Y, so the pitch turns it into a
            // barrel and the Y scale is half the length.
            GunPart(parent, "Barrel", PrimitiveType.Cylinder, accent,
                    new Vector3(0f, 0.004f, 0.36f), new Vector3(0.019f, 0.09f, 0.019f),
                    new Vector3(90f, 0f, 0f));

            GunPart(parent, "Grip", PrimitiveType.Cube, polymer,
                    new Vector3(0f, -0.10f, -0.055f), new Vector3(0.048f, 0.135f, 0.062f),
                    new Vector3(-16f, 0f, 0f));

            GunPart(parent, "Magazine", PrimitiveType.Cube, polymer,
                    new Vector3(0f, -0.105f, 0.055f), new Vector3(0.042f, 0.15f, 0.085f),
                    new Vector3(7f, 0f, 0f));

            GunPart(parent, "Stock", PrimitiveType.Cube, polymer,
                    new Vector3(0f, -0.018f, -0.235f), new Vector3(0.05f, 0.085f, 0.18f),
                    new Vector3(-2f, 0f, 0f));

            GunPart(parent, "Rail", PrimitiveType.Cube, accent,
                    new Vector3(0f, 0.055f, 0.02f), new Vector3(0.028f, 0.016f, 0.26f), Vector3.zero);

            // Sights sit on the rail rather than on the receiver, so the notch and post
            // line up with each other at any ADS offset.
            GunPart(parent, "SightRear", PrimitiveType.Cube, accent,
                    new Vector3(0f, 0.077f, -0.06f), new Vector3(0.030f, 0.030f, 0.014f), Vector3.zero);

            GunPart(parent, "SightFront", PrimitiveType.Cube, accent,
                    new Vector3(0f, 0.077f, 0.27f), new Vector3(0.012f, 0.030f, 0.012f), Vector3.zero);

            GunPart(parent, "Trigger", PrimitiveType.Cube, accent,
                    new Vector3(0f, -0.055f, -0.025f), new Vector3(0.012f, 0.032f, 0.012f), Vector3.zero);

            return root;
        }

        /// <summary>
        /// Every authored dimension of the weapon is multiplied by this on its way to the
        /// transform.
        ///
        /// The parts above are written at sizes that are easy to reason about -- a 30cm
        /// receiver, a 15cm magazine -- and then the whole gun is shrunk to sit in frame.
        /// Doing it here rather than by scaling the root keeps the root at unit scale,
        /// which is what lets the muzzle point stay in real metres and keeps Weapon's
        /// recoil kickback in the same units as everything else.
        /// </summary>
        private const float GunScale = 0.78f;

        /// <summary>One primitive of the weapon, stripped of the collider it ships with.</summary>
        private static GameObject GunPart(Transform parent, string name, PrimitiveType shape,
                                          Material material, Vector3 position, Vector3 scale,
                                          Vector3 euler)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position * GunScale;
            part.transform.localRotation = Quaternion.Euler(euler);
            part.transform.localScale = scale * GunScale;

            // Nothing on the weapon is ever collided with -- it lives inside the camera's
            // near plane -- and a collider there would trip the player's own capsule.
            Object.DestroyImmediate(part.GetComponent<Collider>());

            if (material != null) part.GetComponent<Renderer>().sharedMaterial = material;
            return part;
        }

        // ==================================================================
        private static GameObject BuildEnemyPrefab()
        {
            int enemyLayer = LayerMask.NameToLayer("Enemy");

            var enemy = new GameObject("Enemy");
            enemy.tag = "Enemy";
            enemy.layer = enemyLayer;

            var flesh = MakeTintableMaterial("Enemy", new Color(0.55f, 0.18f, 0.18f), 0.2f, 0f);
            var headFlesh = MakeTintableMaterial("EnemyHead", new Color(0.75f, 0.3f, 0.25f), 0.2f, 0f);

            // The torso is an empty pivot at the hips, so EnemyLimbAnimator can lean and
            // bob the upper body without dragging the feet off the floor. Legs hang off
            // the root for the same reason.
            var torso = new GameObject("Torso");
            torso.transform.SetParent(enemy.transform, false);
            torso.transform.localPosition = new Vector3(0f, 1.05f, 0f);

            var body = EnemyPart(torso.transform, "Chest", PrimitiveType.Capsule, flesh, enemyLayer,
                                 new Vector3(0f, 0.30f, 0f), new Vector3(0.5f, 0.33f, 0.42f));

            var head = EnemyPart(torso.transform, "Head", PrimitiveType.Sphere, headFlesh, enemyLayer,
                                 new Vector3(0f, 0.80f, 0f), Vector3.one * 0.40f);

            // Shoulder and hip pivots carry the limb, so a swing rotates the whole limb
            // about its joint instead of spinning the mesh around its own middle.
            var leftArm = EnemyJoint(torso.transform, "ArmLeft", new Vector3(-0.31f, 0.55f, 0f));
            var rightArm = EnemyJoint(torso.transform, "ArmRight", new Vector3(0.31f, 0.55f, 0f));

            var leftArmMesh = EnemyPart(leftArm, "ArmLeftMesh", PrimitiveType.Capsule, flesh, enemyLayer,
                                        new Vector3(0f, -0.31f, 0f), new Vector3(0.15f, 0.31f, 0.15f));
            var rightArmMesh = EnemyPart(rightArm, "ArmRightMesh", PrimitiveType.Capsule, flesh, enemyLayer,
                                         new Vector3(0f, -0.31f, 0f), new Vector3(0.15f, 0.31f, 0.15f));

            var leftLeg = EnemyJoint(enemy.transform, "LegLeft", new Vector3(-0.14f, 1.05f, 0f));
            var rightLeg = EnemyJoint(enemy.transform, "LegRight", new Vector3(0.14f, 1.05f, 0f));

            var leftLegMesh = EnemyPart(leftLeg, "LegLeftMesh", PrimitiveType.Capsule, flesh, enemyLayer,
                                        new Vector3(0f, -0.52f, 0f), new Vector3(0.19f, 0.52f, 0.19f));
            var rightLegMesh = EnemyPart(rightLeg, "LegRightMesh", PrimitiveType.Capsule, flesh, enemyLayer,
                                         new Vector3(0f, -0.52f, 0f), new Vector3(0.19f, 0.52f, 0.19f));

            var hp = enemy.AddComponent<Health>();
            hp.maxHealth = 100f;
            hp.destroyOnDeath = true;
            hp.destroyDelay = 2f;

            // Hitboxes -- this is what gives you headshots, and now what makes a limb
            // shot worth less than a chest shot. Every visible part carries one, so no
            // part of the silhouette is a hole that swallows rounds.
            AddHitbox(body, hp, 1f, false);
            AddHitbox(head, hp, 3f, true);

            AddHitbox(leftArmMesh, hp, 0.65f, false);
            AddHitbox(rightArmMesh, hp, 0.65f, false);
            AddHitbox(leftLegMesh, hp, 0.75f, false);
            AddHitbox(rightLegMesh, hp, 0.75f, false);

            var agent = enemy.AddComponent<UnityEngine.AI.NavMeshAgent>();
            agent.speed = 4f;
            agent.angularSpeed = 400f;
            agent.acceleration = 12f;
            agent.stoppingDistance = 1.5f;
            agent.radius = 0.4f;
            agent.height = 2f;

            var eyes = new GameObject("Eyes");
            eyes.transform.SetParent(torso.transform, false);
            eyes.transform.localPosition = new Vector3(0f, 0.80f, 0.18f);

            var ai = enemy.AddComponent<EnemyAI>();
            ai.eyes = eyes.transform;
            ai.ranged = false;
            ai.attackRange = 2f;
            ai.attackDamage = 12f;
            ai.attackCooldown = 1.3f;
            ai.attackWindup = 0.35f;
            ai.detectionRadius = Mathf.Max(45f, _theme.arenaSize * 0.7f);
            ai.relentless = true;
            ai.sightBlockers = 1 << LayerMask.NameToLayer("Environment");
            ai.rangedHitMask = ~(1 << enemyLayer);

            var src = enemy.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.maxDistance = 30f;

            // Force To Mono on these is not tidiness: spatialBlend 1 above means a stereo
            // clip would play its left channel only. The import policy handles it.
            ai.alertClip = Clip("SFX/enemy_alert.wav");
            ai.attackClip = Clip("SFX/enemy_attack.wav");
            ai.deathClip = Clip("SFX/enemy_death.wav");

            // The rifle a ranged archetype carries. Hung off the right arm so the limb
            // animator swings and raises it with the hands rather than needing to know
            // it exists.
            var enemyMuzzle = BuildEnemyWeapon(rightArm, out GameObject enemyWeapon);

            ai.muzzlePoint = enemyMuzzle;
            ai.muzzleFlashPrefab = CreateMuzzleFlashPrefab();
            ai.tracerPrefab = CreateTracerPrefab();

            // Slower than the player's 240, so incoming fire is legible as something
            // arriving at you rather than an instant hit you can only infer from damage.
            ai.tracerSpeed = 170f;
            ai.fireClip = Clip("SFX/weapon_fire.wav");

            // No AnimationClips and no AnimatorController anywhere in the kit -- the walk
            // comes off the agent's own velocity. See EnemyLimbAnimator.
            var limbs = enemy.AddComponent<EnemyLimbAnimator>();
            limbs.torso = torso.transform;
            limbs.leftArm = leftArm;
            limbs.rightArm = rightArm;
            limbs.leftLeg = leftLeg;
            limbs.rightLeg = rightLeg;
            limbs.weapon = enemyWeapon;

            // Floating health bar. The material is assigned from an asset rather than
            // found at runtime, so the shader survives shader stripping in a build.
            var bar = enemy.AddComponent<EnemyHealthBar>();
            bar.barMaterial = CreateBarMaterial();
            bar.heightOffset = 2.6f;

            string path = AssetFolder + "/Enemy.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(enemy, path);
            Object.DestroyImmediate(enemy);
            return prefab;
        }

        /// <summary>
        /// The rifle a ranged enemy carries, parented to the arm that holds it.
        ///
        /// The arm is a joint that hangs straight down at rest and is pitched up to
        /// EnemyLimbAnimator.aimRaise while armed. Cancelling exactly that angle here
        /// means the weapon points along the enemy's forward once the arms come up, with
        /// no aiming code: arm pitch and weapon pitch sum to zero.
        ///
        /// Tagged Metal, which does two jobs -- it keeps the archetype's body colour off
        /// the gun (see EnemyAI.IsBodyRenderer) and, if it ever gains colliders, would
        /// spark metal rather than flesh. It has none: this is decoration, and a rifle
        /// that absorbed shots meant for the chest behind it would be a stealth nerf on
        /// every ranged enemy.
        /// </summary>
        private static Transform BuildEnemyWeapon(Transform arm, out GameObject weapon)
        {
            var metal = MakeSharedMaterial("Gun_Metal", new Color(0.11f, 0.115f, 0.125f), 0.55f, 0.85f);
            var polymer = MakeSharedMaterial("Gun_Polymer", new Color(0.16f, 0.17f, 0.18f), 0.25f, 0f);

            var root = new GameObject("Weapon");
            root.transform.SetParent(arm, false);

            // Down at the hand, and pulled back toward the chest: hung straight off the
            // right arm the rifle reads as held out at the hip by one hand, where both
            // arms raise together and this sits between them as a two-handed grip.
            root.transform.localPosition = new Vector3(-0.22f, -0.44f, 0.05f);
            root.transform.localRotation = Quaternion.Euler(EnemyAimRaise, 0f, 0f);

            EnemyGunPart(root.transform, "Receiver", PrimitiveType.Cube, metal,
                         new Vector3(0f, 0f, 0.08f), new Vector3(0.07f, 0.1f, 0.34f));

            EnemyGunPart(root.transform, "Barrel", PrimitiveType.Cylinder, metal,
                         new Vector3(0f, 0.01f, 0.36f), new Vector3(0.028f, 0.12f, 0.028f),
                         new Vector3(90f, 0f, 0f));

            EnemyGunPart(root.transform, "Magazine", PrimitiveType.Cube, polymer,
                         new Vector3(0f, -0.11f, 0.1f), new Vector3(0.05f, 0.16f, 0.09f),
                         new Vector3(8f, 0f, 0f));

            EnemyGunPart(root.transform, "Stock", PrimitiveType.Cube, polymer,
                         new Vector3(0f, -0.02f, -0.15f), new Vector3(0.055f, 0.09f, 0.16f));

            var muzzle = new GameObject("MuzzlePoint");
            muzzle.transform.SetParent(root.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 0.01f, 0.5f) * EnemyGunScale;

            weapon = root;

            // Off until an archetype says otherwise. EnemyLimbAnimator turns it on for a
            // ranged enemy, because `ranged` is stamped after the prefab is instantiated
            // and so cannot be known here.
            root.SetActive(false);

            return muzzle.transform;
        }

        /// <summary>
        /// Must match EnemyLimbAnimator.aimRaise. The weapon cancels this angle so it
        /// points forward once the arms are up, so the two have to agree -- if you
        /// retune one, retune the other.
        /// </summary>
        private const float EnemyAimRaise = 76f;

        /// <summary>
        /// The enemy's rifle is authored at the player's proportions and then enlarged,
        /// because it is read at fifteen metres rather than at arm's length. A weapon
        /// that is correctly scaled and unreadable tells the player nothing about which
        /// enemies shoot back.
        /// </summary>
        private const float EnemyGunScale = 1.18f;

        private static void EnemyGunPart(Transform parent, string name, PrimitiveType shape,
                                         Material material, Vector3 position, Vector3 scale)
            => EnemyGunPart(parent, name, shape, material, position, scale, Vector3.zero);

        private static void EnemyGunPart(Transform parent, string name, PrimitiveType shape,
                                         Material material, Vector3 position, Vector3 scale,
                                         Vector3 euler)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position * EnemyGunScale;
            part.transform.localRotation = Quaternion.Euler(euler);
            part.transform.localScale = scale * EnemyGunScale;
            part.tag = "Metal";

            Object.DestroyImmediate(part.GetComponent<Collider>());

            if (material != null) part.GetComponent<Renderer>().sharedMaterial = material;
        }

        /// <summary>A bare pivot: the joint a limb rotates about.</summary>
        private static Transform EnemyJoint(Transform parent, string name, Vector3 localPosition)
        {
            var joint = new GameObject(name);
            joint.transform.SetParent(parent, false);
            joint.transform.localPosition = localPosition;
            return joint.transform;
        }

        /// <summary>
        /// One visible piece of an enemy. Tagged Flesh so the impact library plays the
        /// wet hit, and put on the Enemy layer so the player's own shots can find it.
        /// </summary>
        private static GameObject EnemyPart(Transform parent, string name, PrimitiveType shape,
                                            Material material, int layer,
                                            Vector3 localPosition, Vector3 localScale)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.tag = "Flesh";
            part.layer = layer;

            if (material != null) part.GetComponent<Renderer>().sharedMaterial = material;
            return part;
        }

        private static void AddHitbox(GameObject part, Health owner, float multiplier, bool headshot)
        {
            var box = part.AddComponent<Hitbox>();
            box.owner = owner;
            box.damageMultiplier = multiplier;
            box.isHeadshot = headshot;
        }

        // ==================================================================
        private static Transform[] BuildSpawnPoints()
        {
            var root = new GameObject("SpawnPoints").transform;
            var list = new List<Transform>();

            float radius = _theme.arenaSize * 0.4f;

            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 2f / 8f;
                var sp = new GameObject("Spawn_" + i).transform;
                sp.SetParent(root);
                sp.position = new Vector3(Mathf.Cos(angle) * radius, 0.1f, Mathf.Sin(angle) * radius);
                list.Add(sp);
            }

            return list.ToArray();
        }

        private static void BakeNavMesh()
        {
            var go = new GameObject("NavMesh");
            var surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.BuildNavMesh();
        }

        private static WaveManager BuildWaveManager(GameObject enemyPrefab, Transform[] spawns, Transform player)
        {
            var go = new GameObject("WaveManager");
            var wm = go.AddComponent<WaveManager>();

            // One prefab, many variants. Every entry is an archetype asset, so a new
            // enemy means duplicating an asset and adding it here -- never a new prefab.
            wm.baseEnemyPrefab = enemyPrefab;
            wm.enemyTypes = BuildRoster();

            wm.healthPickupPrefab = CreatePickupPrefab("Pickup_Health", Pickup.Kind.Health,
                new Color(0.25f, 0.95f, 0.45f), PrimitiveType.Sphere);
            wm.shieldPickupPrefab = CreatePickupPrefab("Pickup_Shield", Pickup.Kind.Shield,
                new Color(0.3f, 0.7f, 1f), PrimitiveType.Cube);

            // Ammo drops are left unwired on purpose: the generated rifle has an
            // infinite reserve, so they would be pickups that do nothing. Turn that off
            // in TestRifle.asset and drop a Pickup here to bring the ammo economy back.

            wm.spawnPoints = spawns;
            wm.player = player;
            wm.baseEnemiesPerWave = 5;
            wm.enemiesPerWaveGrowth = 1.3f;
            wm.maxEnemiesPerWave = 60;
            wm.maxAliveAtOnce = 18;
            wm.spawnInterval = 0.3f;
            wm.timeBeforeFirstWave = 4f;
            wm.intermissionDuration = 10f;

            wm.aggressionRampWaves = 18;
            wm.bossWaveInterval = 5;
            wm.bossWaveEscortFraction = 0.55f;
            wm.useModifiers = true;
            wm.modifierStartWave = 3;
            wm.modifierChance = 0.55f;
            wm.waveClearBonus = 250;

            // Far enough that a wave has to cross open ground to reach you, which is the
            // breathing room between fights; close enough that it still arrives.
            wm.minSpawnDistanceFromPlayer = 20f;
            wm.maxSpawnDistanceFromPlayer = Mathf.Max(30f, _theme.arenaSize * 0.55f);

            // The leash sits well outside the spawn ring so nothing is culled on arrival.
            wm.despawnDistance = Mathf.Max(95f, wm.maxSpawnDistanceFromPlayer * 1.6f);
            wm.despawnGraceTime = 4f;
            wm.fallKillDepth = 60f;
            wm.despawnOffNavMesh = true;

            wm.waveTimeLimit = 45f;
            wm.waveTimeLimitPerEnemy = 5f;
            wm.clearLeftoversOnTimeout = true;
            wm.useDynamicSpawnPoints = true;
            wm.avoidPlayerView = true;
            wm.playerViewAngle = 70f;
            wm.spawnSightBlockers = 1 << LayerMask.NameToLayer("Environment");

            return wm;
        }

        /// <summary>
        /// Turns every archetype asset into a roster entry. Ordering does not matter --
        /// the WaveManager selects by weight and unlock wave, and bosses are drawn from
        /// their own pool.
        /// </summary>
        private static WaveManager.EnemyType[] BuildRoster()
        {
            var archetypes = FPSKitEnemyRoster.GetOrCreateAll();
            var entries = new List<WaveManager.EnemyType>(archetypes.Count);

            foreach (var archetype in archetypes)
            {
                if (archetype == null) continue;
                entries.Add(new WaveManager.EnemyType { archetype = archetype });
            }

            return entries.ToArray();
        }

        // ==================================================================
        private static void BuildPostProcessing(GameObject player)
        {
            if (!_theme.enablePostProcessing) return;

            var profile = CreateVolumeProfile();
            if (profile == null) return;

            var go = new GameObject("Global Volume");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = profile;

            var cam = player.GetComponentInChildren<Camera>();
            if (cam == null) return;

            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) return;

            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.dithering = true;
        }

        /// <summary>
        /// The theme's grade, as a Volume profile.
        ///
        /// Re-stamped rather than created once. Every value below comes from the theme,
        /// so an early return on an existing asset meant retuning bloomIntensity or
        /// saturation and rebuilding did precisely nothing -- the same trap that left the
        /// pickups silent and the rifle without a tracer.
        /// </summary>
        private static VolumeProfile CreateVolumeProfile()
        {
            string path = $"{AssetFolder}/PostFX_{SafeName(_theme.themeName)}.asset";

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            // Cleared first: Add on a profile that already holds an override of that type
            // returns the existing one in some versions and appends a second in others,
            // and a profile with two Blooms is a profile nobody can tune.
            for (int i = profile.components.Count - 1; i >= 0; i--)
            {
                var component = profile.components[i];
                profile.components.RemoveAt(i);
                Object.DestroyImmediate(component, true);
            }

            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.ACES;

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.overrideState = true;
            bloom.intensity.value = _theme.bloomIntensity;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.0f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.65f;

            var colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.postExposure.overrideState = true;
            colorAdjustments.postExposure.value = _theme.postExposure;
            colorAdjustments.contrast.overrideState = true;
            colorAdjustments.contrast.value = _theme.contrast;
            colorAdjustments.saturation.overrideState = true;
            colorAdjustments.saturation.value = _theme.saturation;
            colorAdjustments.colorFilter.overrideState = true;
            colorAdjustments.colorFilter.value = _theme.colorFilter;

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = _theme.vignetteIntensity;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.4f;

            var grain = profile.Add<FilmGrain>(true);
            grain.type.overrideState = true;
            grain.type.value = FilmGrainLookup.Medium1;
            grain.intensity.overrideState = true;
            grain.intensity.value = _theme.filmGrain;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // ==================================================================
        private static void BuildHUD(GameObject player, WaveManager wave)
        {
            var canvasGo = new GameObject("HUD Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();

            var hud = canvasGo.AddComponent<HUDController>();
            hud.weapon = player.GetComponentInChildren<Weapon>();
            hud.playerHealth = player.GetComponent<Health>();
            hud.waveManager = wave;

            var root = canvasGo.transform;

            // ---- corners -------------------------------------------------
            hud.ammoText = MakeText(root, "AmmoText", "30 / 150",
                new Vector2(1f, 0f), new Vector2(-60f, 60f), 48, TextAlignmentOptions.BottomRight);

            hud.healthText = MakeText(root, "HealthText", "100",
                new Vector2(0f, 0f), new Vector2(60f, 104f), 44, TextAlignmentOptions.BottomLeft);

            hud.scoreText = MakeText(root, "ScoreText", "0",
                new Vector2(1f, 1f), new Vector2(-60f, -45f), 44, TextAlignmentOptions.TopRight);

            hud.comboText = MakeText(root, "ComboText", "",
                new Vector2(1f, 1f), new Vector2(-60f, -105f), 30, TextAlignmentOptions.TopRight);
            hud.comboText.color = new Color(1f, 0.78f, 0.3f);

            BuildPlayerHealthBar(root, hud);

            // ---- top centre ----------------------------------------------
            hud.waveText = MakeText(root, "WaveText", "WAVE 1",
                new Vector2(0.5f, 1f), new Vector2(0f, -50f), 40, TextAlignmentOptions.Top);
            hud.enemiesLeftText = MakeText(root, "EnemiesLeft", "0 LEFT",
                new Vector2(0.5f, 1f), new Vector2(0f, -100f), 28, TextAlignmentOptions.Top);

            BuildBossBar(root, hud);

            hud.intermissionText = MakeText(root, "Intermission", "",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), 34, TextAlignmentOptions.Center);

            // ---- centre --------------------------------------------------
            BuildCrosshair(root, hud);
            BuildWaveBanner(root, hud);
            BuildDamageIndicators(root, hud);

            // ---- full-screen overlays ------------------------------------
            var vig = new GameObject("DamageVignette", typeof(RectTransform), typeof(Image));
            vig.transform.SetParent(root, false);
            Stretch(vig.GetComponent<RectTransform>());

            var vigImg = vig.GetComponent<Image>();
            vigImg.color = new Color(0.7f, 0f, 0f, 0f);
            vigImg.raycastTarget = false;
            hud.damageVignette = vigImg;

            hud.pausePanel = BuildPausePanel(root, hud);
            hud.gameOverPanel = BuildGameOverPanel(root, hud);
        }

        /// <summary>
        /// Health and shield as bars rather than a bare number. A number tells you what
        /// you have; a bar tells you how close you are to dead without reading anything.
        /// </summary>
        private static void BuildPlayerHealthBar(Transform parent, HUDController hud)
        {
            var group = new GameObject("HealthBar", typeof(RectTransform));
            group.transform.SetParent(parent, false);

            var groupRect = group.GetComponent<RectTransform>();
            groupRect.anchorMin = groupRect.anchorMax = groupRect.pivot = new Vector2(0f, 0f);
            groupRect.anchoredPosition = new Vector2(60f, 62f);
            groupRect.sizeDelta = new Vector2(380f, 40f);

            var zero = Vector2.zero;
            var size = new Vector2(360f, 16f);

            MakeImage(group.transform, "Background", zero, zero, zero, size,
                      new Color(0.04f, 0.04f, 0.05f, 0.8f));

            // Pale bar behind the real one, so the size of a hit stays visible for a beat.
            hud.healthTrailFill = MakeImage(group.transform, "Trail", zero, zero, zero, size,
                                            new Color(1f, 1f, 1f, 0.35f), filled: true);

            hud.healthFill = MakeImage(group.transform, "Fill", zero, zero, zero, size,
                                       new Color(0.85f, 0.25f, 0.25f), filled: true);

            hud.shieldFill = MakeImage(group.transform, "Shield", zero, zero,
                                       new Vector2(0f, 21f), new Vector2(360f, 8f),
                                       new Color(0.4f, 0.75f, 1f), filled: true);
        }

        private static void BuildBossBar(Transform parent, HUDController hud)
        {
            var panel = new GameObject("BossBar", typeof(RectTransform));
            panel.transform.SetParent(parent, false);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -150f);
            rect.sizeDelta = new Vector2(900f, 60f);

            hud.bossNameText = MakeText(panel.transform, "BossName", "BOSS",
                new Vector2(0.5f, 1f), Vector2.zero, 28, TextAlignmentOptions.Top);
            hud.bossNameText.color = new Color(1f, 0.5f, 0.45f);

            var barAnchor = new Vector2(0.5f, 0f);
            var barSize = new Vector2(860f, 14f);

            MakeImage(panel.transform, "BossBackground", barAnchor, barAnchor, Vector2.zero, barSize,
                      new Color(0.05f, 0.02f, 0.02f, 0.85f));

            hud.bossFill = MakeImage(panel.transform, "BossFill", barAnchor, barAnchor,
                                     Vector2.zero, barSize,
                                     new Color(0.9f, 0.2f, 0.18f), filled: true);

            hud.bossPanel = panel;
            panel.SetActive(false);
        }

        private static void BuildCrosshair(Transform parent, HUDController hud)
        {
            // Four arms, order matters -- top, bottom, left, right.
            var chRoot = new GameObject("Crosshair", typeof(RectTransform), typeof(CanvasGroup));
            chRoot.transform.SetParent(parent, false);

            var chRect = chRoot.GetComponent<RectTransform>();
            chRect.anchorMin = chRect.anchorMax = new Vector2(0.5f, 0.5f);
            chRect.anchoredPosition = Vector2.zero;
            hud.crosshairGroup = chRoot.GetComponent<CanvasGroup>();

            hud.crosshairArms = new[]
            {
                MakeArm(chRect, "Top",    new Vector2(2f, 10f)),
                MakeArm(chRect, "Bottom", new Vector2(2f, 10f)),
                MakeArm(chRect, "Left",   new Vector2(10f, 2f)),
                MakeArm(chRect, "Right",  new Vector2(10f, 2f))
            };
        }

        private static void BuildWaveBanner(Transform parent, HUDController hud)
        {
            var banner = new GameObject("WaveBanner", typeof(RectTransform), typeof(CanvasGroup));
            banner.transform.SetParent(parent, false);

            var rect = banner.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;

            hud.bannerGroup = banner.GetComponent<CanvasGroup>();
            hud.bannerGroup.alpha = 0f;

            hud.bannerTitle = MakeText(banner.transform, "BannerTitle", "WAVE 1",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), 62, TextAlignmentOptions.Center);

            hud.bannerSubtitle = MakeText(banner.transform, "BannerSubtitle", "",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), 30, TextAlignmentOptions.Center);
            hud.bannerSubtitle.color = new Color(0.85f, 0.85f, 0.85f);
        }

        /// <summary>
        /// One arrow template, pivoting on the screen centre. The HUD clones it per hit
        /// and rotates it, which is the only thing telling a surrounded player where the
        /// damage is coming from.
        /// </summary>
        private static void BuildDamageIndicators(Transform parent, HUDController hud)
        {
            var root = new GameObject("DamageIndicators", typeof(RectTransform));
            root.transform.SetParent(parent, false);

            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = Vector2.zero;
            hud.damageIndicatorRoot = rootRect;

            var template = new GameObject("IndicatorTemplate", typeof(RectTransform), typeof(CanvasGroup));
            template.transform.SetParent(parent, false);

            var templateRect = template.GetComponent<RectTransform>();
            templateRect.anchorMin = templateRect.anchorMax = new Vector2(0.5f, 0.5f);
            templateRect.anchoredPosition = Vector2.zero;
            templateRect.sizeDelta = Vector2.zero;

            var centre = new Vector2(0.5f, 0.5f);
            MakeImage(template.transform, "Arc", centre, centre, new Vector2(0f, 190f),
                      new Vector2(170f, 12f), new Color(1f, 0.2f, 0.18f, 0.9f));

            hud.damageIndicatorPrefab = template.GetComponent<CanvasGroup>();
            template.SetActive(false);
        }

        private static GameObject BuildPausePanel(Transform parent, HUDController hud)
        {
            var panel = new GameObject("PausePanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            Stretch(panel.GetComponent<RectTransform>());
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);

            MakeText(panel.transform, "PausedTitle", "PAUSED",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), 64, TextAlignmentOptions.Center);

            hud.pauseHintText = MakeText(panel.transform, "PauseHint", "",
                new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), 32, TextAlignmentOptions.Center);

            panel.SetActive(false);
            return panel;
        }

        private static GameObject BuildGameOverPanel(Transform parent, HUDController hud)
        {
            var panel = new GameObject("GameOverPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            Stretch(panel.GetComponent<RectTransform>());
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

            hud.finalWaveText = MakeText(panel.transform, "FinalWave", "You survived 0 waves",
                new Vector2(0.5f, 0.5f), Vector2.zero, 44, TextAlignmentOptions.Center);
            hud.finalWaveText.GetComponent<RectTransform>().sizeDelta = new Vector2(1200f, 400f);

            panel.SetActive(false);
            return panel;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        // ==================================================================
        // Shared UI and material helpers
        // ==================================================================

        /// <summary>
        /// A sprite for Image components that need one. Filled images -- every bar in
        /// the HUD -- ignore fillAmount entirely without a sprite, so this is not
        /// optional decoration. Unity's built-in UI sprite is used where available and
        /// a flat white one is generated as a fallback.
        /// </summary>
        private static Sprite UISprite()
        {
            if (_uiSprite != null) return _uiSprite;

            _uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (_uiSprite != null) return _uiSprite;

            string path = $"{AssetFolder}/UIWhite.png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null)
            {
                _uiSprite = existing;
                return _uiSprite;
            }

            var texture = new Texture2D(4, 4);
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();

            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.SaveAndReimport();
            }

            _uiSprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            return _uiSprite;
        }

        private static Sprite _uiSprite;

        private static Image MakeImage(Transform parent, string name, Vector2 anchor, Vector2 pivot,
                                       Vector2 offset, Vector2 size, Color color,
                                       bool filled = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            image.sprite = UISprite();

            if (filled)
            {
                image.type = Image.Type.Filled;
                image.fillMethod = Image.FillMethod.Horizontal;
                image.fillOrigin = (int)Image.OriginHorizontal.Left;
                image.fillAmount = 1f;
            }
            else
            {
                image.type = Image.Type.Sliced;
            }

            return image;
        }

        /// <summary>
        /// A material whose emission can be driven per instance by a property block.
        /// The keyword has to be on the shared material -- turning it on per renderer
        /// would need renderer.material, which instantiates a copy per enemy.
        /// </summary>
        private static Material MakeTintableMaterial(string name, Color color, float smoothness,
                                                     float metallic, bool shared = false)
        {
            var mat = shared
                ? MakeSharedMaterial(name, color, smoothness, metallic)
                : MakeMaterial(name, color, smoothness, metallic);

            if (mat == null) return null;

            mat.EnableKeyword("_EMISSION");

            // That keyword is what makes the per-instance glow possible at all: a
            // MaterialPropertyBlock can set _EmissionColor but cannot enable a shader
            // keyword, so without it EnemyAI's glow and the pickup tint render black.
            //
            // Keeping it enabled is also why these flags are not None. URP derives the
            // keyword from the GI flags alone -- BaseShaderGUI.SetMaterialKeywords does
            // shouldEmissionBeEnabled = (globalIlluminationFlags & AnyEmissive) != 0 --
            // so None strips _EMISSION the first time anyone so much as clicks the
            // material in the Inspector, silently killing every glow in the scene. It
            // survives a rebuild, because the strip happens after the builder runs.
            //
            // BakedEmissive keeps the material readable as emissive; EmissiveIsBlack is
            // the half that says "contribute nothing to GI", which was the original
            // intent -- these glows are a readability cue, not lighting. Unity's own
            // FixupEmissiveFlag maintains the pair, dropping EmissiveIsBlack once a real
            // colour is set, and nothing the builder makes is Contribute GI static, so
            // no bake sees them either way.
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive |
                                          MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Unlit material shared by every floating enemy health bar.</summary>
        public static Material GetOrCreateBarMaterial()
        {
            EnsureFolders();
            return CreateBarMaterial();
        }

        private static Material CreateBarMaterial()
        {
            string path = $"{MaterialFolder}/HealthBar.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Color") ??
                         Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ==================================================================
        private static GameObject CreatePickupPrefab(string assetName, Pickup.Kind kind,
                                                     Color color, PrimitiveType shape)
        {
            string path = $"{AssetFolder}/{assetName}.prefab";

            // Stamped before the existing-prefab check rather than inside the creation
            // path below. The prefab is deliberately kept across rebuilds, so anything
            // done only while creating it can never be corrected by building again --
            // which is how these materials kept a stale emission setup through a full
            // rebuild. Re-stamping is idempotent and the material is shared, so the
            // prefab keeps pointing at one the current builder still agrees with.
            var material = MakeTintableMaterial(assetName, color, 0.6f, 0.1f, shared: true);
            if (material != null)
            {
                // Pickups glow so they read on a dark floor across the arena.
                material.SetColor("_EmissionColor", color * 1.8f);
                EditorUtility.SetDirty(material);
            }

            // Stamped out here with the material, above the existing-prefab check, so a
            // kept prefab picks the sound up on the next build.
            var pickupClip = Clip("SFX/pickup.wav");

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                // The material above could be re-stamped from out here because it is a
                // separate asset. A component on the kept prefab cannot, so it is caught
                // here instead -- otherwise a pickup created before the audio existed
                // would stay silent through every future rebuild.
                var kept = existing.GetComponent<Pickup>();
                if (kept != null && kept.collectClip != pickupClip)
                {
                    kept.collectClip = pickupClip;
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssets();
                }

                return existing;
            }

            var root = new GameObject(assetName);

            var pickup = root.AddComponent<Pickup>();
            pickup.collectClip = pickupClip;
            pickup.kind = kind;
            pickup.amount = kind == Pickup.Kind.Shield ? 50f : 35f;
            pickup.ammoAmount = 90;

            var visual = GameObject.CreatePrimitive(shape);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = Vector3.one * 0.45f;
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            if (material != null) visual.GetComponent<Renderer>().sharedMaterial = material;

            var glow = new GameObject("Glow");
            glow.transform.SetParent(root.transform, false);

            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = 3.5f;
            light.range = 5f;
            light.shadows = LightShadows.None;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void BuildGameDirector()
        {
            if (Object.FindAnyObjectByType<GameDirector>() != null) return;

            var go = new GameObject("GameDirector");
            go.AddComponent<GameDirector>();
        }

                private static RectTransform MakeArm(RectTransform parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

            var img = go.GetComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = false;
            return rt;
        }

        private static TMP_Text MakeText(Transform parent, string name, string content,
                                         Vector2 anchor, Vector2 offset, float size, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = Color.white;
            tmp.raycastTarget = false;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = new Vector2(700f, 160f);
            rt.anchoredPosition = offset;
            return tmp;
        }

        // ==================================================================
        private static void SaveSceneAndRegister(UnityEngine.SceneManagement.Scene scene, string themeName)
        {
            string path = $"{SceneFolder}/{SafeName(themeName)}.unity";
            EditorSceneManager.SaveScene(scene, path);

            // Register in Build Settings so the in-game restart can reload it.
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == path))
            {
                scenes.Add(new EditorBuildSettingsScene(path, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }

        // ==================================================================
        private static Material MakeMaterial(string name, Color color, float smoothness, float metallic)
            => MakeMaterialAt($"{MaterialFolder}/{SafeName(_theme.themeName)}_{name}.mat",
                              color, smoothness, metallic);

        /// <summary>
        /// A material with no theme in its name, for assets that are built once and
        /// reused by every scene. Pickup prefabs are shared, so prefixing their material
        /// with whichever theme happened to be built first is a lie about its scope.
        /// </summary>
        private static Material MakeSharedMaterial(string name, Color color, float smoothness,
                                                   float metallic)
            => MakeMaterialAt($"{MaterialFolder}/{name}.mat", color, smoothness, metallic);

        private static Material MakeMaterialAt(string path, Color color, float smoothness,
                                               float metallic)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            // URP first, Standard as a fallback for the built-in pipeline.
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color };

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static string ColorKey(Color c)
            => $"{Mathf.RoundToInt(c.r * 255):X2}{Mathf.RoundToInt(c.g * 255):X2}{Mathf.RoundToInt(c.b * 255):X2}";

        private static string SafeName(string value)
            => value.Replace(" ", "").Replace("/", "").Replace("\\", "");

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }

        private static void SetTagRecursive(GameObject go, string tag)
        {
            go.tag = tag;
            foreach (Transform child in go.transform) SetTagRecursive(child.gameObject, tag);
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(AssetFolder))
                AssetDatabase.CreateFolder("Assets", "FPSKit_Generated");

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder(AssetFolder, "Materials");

            if (!AssetDatabase.IsValidFolder(SceneFolder))
                AssetDatabase.CreateFolder(AssetFolder, "Scenes");

            Directory.CreateDirectory(SceneFolder);
        }

        /// <summary>Public so the other FPSKit tools can guarantee these exist before they run.</summary>
        public static void EnsureProjectTagsAndLayers()
        {
            EnsureTags("Player", "Enemy", "Concrete", "Metal", "Wood", "Flesh");
            EnsureLayers("Player", "Enemy", "Environment");
        }

        private static void EnsureTags(params string[] tags)
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            var so = new SerializedObject(asset);
            var tagsProp = so.FindProperty("tags");

            foreach (var tag in tags)
            {
                bool found = false;
                for (int i = 0; i < tagsProp.arraySize; i++)
                    if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag) { found = true; break; }

                if (found) continue;

                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
            }

            so.ApplyModifiedProperties();
        }

        private static void EnsureLayers(params string[] layers)
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            var so = new SerializedObject(asset);
            var layersProp = so.FindProperty("layers");

            foreach (var layer in layers)
            {
                bool exists = false;
                for (int i = 0; i < layersProp.arraySize; i++)
                    if (layersProp.GetArrayElementAtIndex(i).stringValue == layer) { exists = true; break; }

                if (exists) continue;

                // Layers 0-7 are reserved by Unity.
                for (int i = 8; i < layersProp.arraySize; i++)
                {
                    var el = layersProp.GetArrayElementAtIndex(i);
                    if (string.IsNullOrEmpty(el.stringValue)) { el.stringValue = layer; break; }
                }
            }

            so.ApplyModifiedProperties();
        }
    }
}
#endif
