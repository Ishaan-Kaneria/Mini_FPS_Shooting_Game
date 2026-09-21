#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Builds the dashboard: the scene the game boots into and every run returns to.
    ///
    /// Destructive in the same way FPSKitSceneBuilder is -- it replaces Menu.unity from
    /// scratch -- and for the same reason: the screen is generated, so it is fixed by
    /// editing this file rather than by hand-editing the scene. What it does *not*
    /// hard-code is the list of arenas. That is written into an ArenaCatalog asset and
    /// read at runtime, so adding an arena never means rebuilding this scene.
    ///
    /// The look is deliberately pixel-flat: hard borders, no gradients, a small palette
    /// and point-filtered preview images. That is partly taste and partly a constraint
    /// worth being honest about -- the project has one font (LiberationSans) and no UI
    /// art at all, so anything that leans on soft shading would look unfinished. Flat
    /// blocks of colour with crisp edges do not.
    /// </summary>
    public static class FPSKitMenuBuilder
    {
        public const string MenuScenePath = "Assets/FPSKit_Generated/Scenes/Menu.unity";
        public const string CatalogPath = "Assets/FPSKit_Generated/Arenas.asset";
        public const string PreviewFolder = "Assets/FPSKit_Generated/Previews";

        const int PreviewWidth = 512;
        const int PreviewHeight = 288;

        // ------------------------------------------------------------------
        // Palette. Small on purpose: six colours is what keeps a generated screen
        // looking designed rather than assembled.
        //
        // <b>Deeper surfaces, brighter ink.</b> Every pairing below is measured rather
        // than chosen by eye, against the WCAG contrast ratio -- 4.5:1 for text, 3:1 for
        // an edge that has to be seen. Body text now sits at 17.5:1 and the dimmed
        // secondary at 7.8:1, where it used to be 5.1:1 and was the first thing to
        // disappear on a laptop screen at an angle.
        //
        // The surfaces went down rather than the text merely going up, because the thing
        // that makes a dark interface look flat is not the darkness, it is three greys
        // close enough together that nothing reads as sitting on anything. Backdrop,
        // panel and card are now further apart, and <see cref="Border"/> carries the
        // separation the greys cannot: at 3.3:1 against a panel it draws the edge of
        // every box on its own, which is what lets the fills go this dark without the
        // layout dissolving.
        // ------------------------------------------------------------------
        // These are UITheme's, named as the builder has always named them. The ramp the
        // comment above argues for is unchanged -- three steps far enough apart to read as
        // stacked -- but it now starts at a colour instead of at near-black, and the one
        // amber has become six signals that each mean a particular arena.
        static readonly Color Backdrop = UITheme.Well;
        static readonly Color Panel = UITheme.Chassis;
        static readonly Color PanelLift = UITheme.Plate;
        static readonly Color Border = UITheme.Seam;
        static readonly Color Ink = UITheme.Ink;
        static readonly Color InkDim = UITheme.InkDim;

        /// <summary>
        /// The default signal, for chrome that belongs to no particular arena -- the title
        /// rule, the wallet, a section heading. Anything that *is* an arena takes its own
        /// from <see cref="UITheme.SignalFor"/> instead.
        /// </summary>
        static readonly Color Accent = UITheme.Hazard;

        /// <summary>Leaving is the one destructive thing on this screen, so it is the one red.</summary>
        static readonly Color Danger = new Color32(0xA8, 0x2A, 0x1B, 0xFF);
        static readonly Color DangerLift = UITheme.Warning;

        static Sprite _flat;

        // ==================================================================
        [MenuItem("FPSKit/Build Dashboard", false, 20)]
        public static void BuildMenuMenuItem()
        {
            if (!EditorUtility.DisplayDialog("Build the dashboard",
                "This rebuilds Menu.unity from scratch and refreshes the arena catalog " +
                "and preview images.\n\nAny unsaved changes in the current scene are lost.",
                "Build it", "Cancel")) return;

            Build();
        }

        /// <summary>Headless entry point. See FPSKitBatch.BuildDashboard.</summary>
        public static void Build()
        {
            EnsureFolders();

            // Previews first: capturing one means opening its arena scene, and that has
            // to happen before the menu scene is the open one.
            var rows = CapturePreviews();

            int count = WriteCatalog(rows);
            BuildScene();

            // Refreshed before the start scene is set. Saving a scene over a path the
            // database already knows leaves the SceneAsset briefly unresolvable, and
            // loading it in that window returns null -- so the setting silently did
            // nothing on exactly the run that had just rebuilt the scene.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Build Settings order decides what a *player* boots into; it has no bearing
            // on the editor, which plays whatever is in the hierarchy. FPSKitPlayMode is
            // what makes Play start here instead of in whichever arena is open.
            FPSKitPlayMode.ApplyNow();

            var startScene = UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene;

            Debug.Log($"<color=lime>[FPSKit]</color> Dashboard built with {count} arena(s). " +
                      "It is scene 0 in Build Settings, so a player boots into it, and Play " +
                      $"in the editor starts at \"{(startScene != null ? startScene.name : "<the open scene>")}\". " +
                      "Toggle that with FPSKit > Play Starts At Dashboard.");
        }

        /// <summary>
        /// One arena, described only by asset paths and plain strings.
        ///
        /// Paths rather than object references, deliberately. Importing a texture can
        /// reload the asset database, and a reload destroys the managed wrapper around
        /// every asset already loaded -- so a LevelTheme or the catalog itself, picked up
        /// before the loop and used after it, comes back as
        /// "MissingReferenceException: the object ... has been destroyed". Carrying paths
        /// through the loop and resolving them once at the end cannot hit that.
        /// </summary>
        struct Row
        {
            public string DisplayName;
            public string SceneName;
            public string Description;
            public string ThemePath;
            public string PreviewPath;
            public string LevelsPath;
        }

        // ==================================================================
        // Catalog + previews
        // ==================================================================
        static List<Row> CapturePreviews()
        {
            var rows = new List<Row>();

            bool canRender = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (!canRender)
                Debug.LogWarning("[FPSKit] No graphics device, so arena previews are being drawn " +
                                 "from theme colours instead of rendered. Re-run with " +
                                 "UNITY_GRAPHICS=1 for real screenshots.");

            foreach (string themeName in FPSKitThemes.Names)
            {
                var theme = FPSKitThemes.GetOrCreate(themeName);
                if (theme == null) continue;

                string sceneName = SafeName(themeName);
                string scenePath = $"Assets/FPSKit_Generated/Scenes/{sceneName}.unity";

                if (!File.Exists(scenePath))
                    Debug.LogWarning($"[FPSKit] {scenePath} does not exist yet, so \"{themeName}\" " +
                                     "is being listed without a preview. Build the scenes first.");

                // Everything read off the theme is read now, while the reference is
                // known good, and kept as a string from here on.
                // Created before the scene is opened for the preview, for the same
                // reason the theme is read here: opening a scene and importing a texture
                // both reload the asset database, and anything resolved across one comes
                // back destroyed. A path survives it; an object reference does not.
                var levels = FPSKitLevels.GetOrCreate(themeName);

                var row = new Row
                {
                    DisplayName = themeName,
                    SceneName = sceneName,
                    Description = Summarise(theme),
                    ThemePath = AssetDatabase.GetAssetPath(theme),
                    PreviewPath = $"{PreviewFolder}/{sceneName}.png",
                    LevelsPath = AssetDatabase.GetAssetPath(levels)
                };

                WritePreview(theme, scenePath, row.PreviewPath, canRender);
                rows.Add(row);
            }

            return rows;
        }

        /// <summary>Fills in the catalog asset from rows, and returns how many it holds.</summary>
        static int WriteCatalog(List<Row> rows)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ArenaCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ArenaCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.arenas.Clear();

            foreach (var row in rows)
            {
                catalog.arenas.Add(new ArenaCatalog.Entry
                {
                    displayName = row.DisplayName,
                    sceneName = row.SceneName,
                    description = row.Description,
                    theme = AssetDatabase.LoadAssetAtPath<LevelTheme>(row.ThemePath),
                    preview = AssetDatabase.LoadAssetAtPath<Texture2D>(row.PreviewPath),
                    levels = AssetDatabase.LoadAssetAtPath<LevelSet>(row.LevelsPath)
                });
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            return catalog.arenas.Count;
        }

        /// <summary>One line for the card, from the theme's own description where it has one.</summary>
        static string Summarise(LevelTheme theme)
        {
            if (theme == null) return "";
            if (!string.IsNullOrWhiteSpace(theme.description))
            {
                string text = theme.description.Replace("\n", " ").Trim();
                return text.Length <= 84 ? text : text.Substring(0, 81) + "...";
            }

            return $"{theme.arenaSize:0}m arena";
        }

        /// <summary>
        /// A screenshot of the arena, or a drawn stand-in when one cannot be taken.
        ///
        /// The render is verified before it is kept. A camera that renders into a null
        /// device, or before the pipeline is ready, returns a uniformly black frame
        /// rather than failing -- and a dashboard full of black rectangles looks exactly
        /// like a dashboard whose images failed to load. Measuring the variance is the
        /// cheap way to tell a picture from a void.
        /// </summary>
        static void WritePreview(LevelTheme theme, string scenePath, string previewPath, bool canRender)
        {
            if (canRender && File.Exists(scenePath))
            {
                var shot = Capture(scenePath, theme);
                if (shot != null)
                {
                    Save(shot, previewPath);
                    return;
                }
            }

            Save(Drawn(theme), previewPath);
        }

        static Texture2D Capture(string scenePath, LevelTheme theme)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid()) return null;

            // Ambient light is derived from the skybox, and a scene that has only just
            // been opened has not had that derivation run yet -- so the first arena
            // captured came back lit by nothing at all, a black floor under a sunset,
            // while every arena after it looked right. Asking for the update explicitly
            // is what makes the first one behave like the rest.
            DynamicGI.UpdateEnvironment();

            var rig = new GameObject("FPSKitPreviewCamera");
            var camera = rig.AddComponent<Camera>();

            RenderTexture target = null;
            Texture2D shot = null;

            try
            {
                float size = theme != null ? theme.arenaSize : 110f;

                // Shot from where the player stands, looking out across the arena.
                //
                // The first attempt was an aerial from a third of the arena's width up,
                // and every theme came back as the same grey plain seen through fog:
                // technically a render of the level, and useless as a picture of it. A
                // card has to answer "what is it like to be in here", and the only
                // viewpoint that answers that is the one the game is played from.
                var player = GameObject.FindGameObjectWithTag("Player");

                Vector3 eye = player != null
                    ? player.transform.position + Vector3.up * 1.65f
                    : Vector3.up * 1.75f;

                // Backed off the spawn a little so the player's own arena furniture is
                // in frame rather than in the lens, and lifted to about first-floor
                // height.
                //
                // <b>The lift scales with the arena, and that is the whole of it.</b>
                //
                // Taken from eye height and pitched seven degrees down, two thirds of the
                // card is the ground at the player's feet. That is a fair picture of a
                // hundred-metre box with cover all round the spawn, and a picture of
                // nothing at all on a four-hundred-and-fifty-metre one, where the spawn
                // is deliberately the clearest ground on the map: the desert came back as
                // a card of empty sand and the plant as a card of empty tarmac.
                //
                // A flat four-metre lift fixes those two and ruins the other four. The
                // walled arenas are five metres to the top of the wall, so a camera four
                // metres over the player's head is looking out over it -- Abandoned
                // Subway came back as a black rectangle under a strip of night sky, which
                // is what is outside a walled box at night. So the lift and the pitch are
                // both taken from <c>arenaSize</c>: the boxes keep the shot they had, and
                // only the arenas that outgrew it get the new one.
                //
                // It stays a view from inside the level either way. An aerial was the
                // first version of this and every theme came back as the same grey plain
                // seen through fog.
                float lift = Mathf.Clamp(size * 0.012f, 1.2f, 4.5f);
                float pitch = Mathf.Lerp(7f, 3.5f, Mathf.InverseLerp(150f, 420f, size));

                Vector3 bearing = Quaternion.Euler(0f, 34f, 0f) * Vector3.forward;

                rig.transform.position = eye - bearing * Mathf.Clamp(size * 0.04f, 6f, 9f)
                                             + Vector3.up * lift;

                rig.transform.rotation = Quaternion.LookRotation(
                    Quaternion.Euler(pitch, 34f, 0f) * Vector3.forward, Vector3.up);

                camera.fieldOfView = Mathf.Lerp(68f, 66f, Mathf.InverseLerp(150f, 420f, size));
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = Mathf.Max(400f, size * 4f);
                camera.clearFlags = CameraClearFlags.Skybox;

                target = new RenderTexture(PreviewWidth, PreviewHeight, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 2
                };

                // Rendered through the pipeline, not with Camera.Render().
                //
                // Camera.Render() predates scriptable pipelines: under URP it produces a
                // frame with the skybox drawn and essentially no lighting applied, so
                // every arena came back as black silhouettes against a nice sunset. The
                // render request is what asks URP to render the camera properly, and it
                // is the supported route in Unity 6. Camera.Render stays as a fallback
                // for a project that is not on a scriptable pipeline at all.
                var request = new RenderPipeline.StandardRequest { destination = target };

                if (RenderPipeline.SupportsRenderRequest(camera, request))
                {
                    // Twice, keeping the second. The first frame after a scene opens is
                    // drawn with whatever the pipeline had already warmed -- shadow maps
                    // and the environment probe land a frame late.
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                else
                {
                    camera.targetTexture = target;
                    camera.Render();
                }

                var previous = RenderTexture.active;
                RenderTexture.active = target;

                shot = new Texture2D(PreviewWidth, PreviewHeight, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0f, 0f, PreviewWidth, PreviewHeight), 0, 0);
                shot.Apply();

                RenderTexture.active = previous;

                Brighten(shot);

                if (!HasDetail(shot))
                {
                    Object.DestroyImmediate(shot);
                    shot = null;

                    Debug.LogWarning($"[FPSKit] The preview render of {scenePath} came back flat, " +
                                     "so a drawn stand-in is being used instead.");
                }
            }
            finally
            {
                camera.targetTexture = null;
                Object.DestroyImmediate(rig);

                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }
            }

            return shot;
        }

        /// <summary>
        /// Lifts a preview that is too dark to read as a thumbnail.
        ///
        /// Some themes really are night scenes -- the subway sits at a mean luminance of
        /// about 30 out of 255 -- and a card that is a black rectangle tells the player
        /// nothing except that something failed to load. This is the same adjustment a
        /// store page makes to a screenshot of a dark game, and it is deliberately
        /// bounded: it only engages below the target, it only ever raises shadows, and
        /// the gamma is clamped so a dark arena still reads as a dark arena rather than
        /// being flattened into a grey one.
        ///
        /// A preview that is already bright enough is left exactly as rendered.
        /// </summary>
        static void Brighten(Texture2D texture)
        {
            const float Target = 0.27f;     // mean luminance a card wants, 0-1
            const float FloorGamma = 0.55f; // how far it is allowed to go

            var pixels = texture.GetPixels();
            if (pixels.Length == 0) return;

            float sum = 0f;
            foreach (var pixel in pixels) sum += pixel.grayscale;

            float mean = sum / pixels.Length;
            if (mean >= Target || mean <= 0.001f) return;

            float gamma = Mathf.Max(FloorGamma, Mathf.Log(Target) / Mathf.Log(mean));

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(
                    Mathf.Pow(pixels[i].r, gamma),
                    Mathf.Pow(pixels[i].g, gamma),
                    Mathf.Pow(pixels[i].b, gamma),
                    1f);
            }

            texture.SetPixels(pixels);
            texture.Apply();
        }

        /// <summary>True if the image is more than one flat colour.</summary>
        static bool HasDetail(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            if (pixels.Length == 0) return false;

            var first = pixels[0];

            foreach (var pixel in pixels)
            {
                if (Mathf.Abs(pixel.r - first.r) > 6 ||
                    Mathf.Abs(pixel.g - first.g) > 6 ||
                    Mathf.Abs(pixel.b - first.b) > 6) return true;
            }

            return false;
        }

        /// <summary>
        /// A stand-in drawn from the theme: sky, horizon, ground and a skyline of
        /// blocks in the theme's own colours.
        ///
        /// Not a placeholder in the apologetic sense. It is deterministic, needs no
        /// graphics device, and carries the one piece of information a preview is
        /// actually for -- whether this arena is the grey one, the red one or the white
        /// one -- so a headless build still produces a dashboard worth looking at.
        /// </summary>
        static Texture2D Drawn(LevelTheme theme)
        {
            var texture = new Texture2D(PreviewWidth, PreviewHeight, TextureFormat.RGB24, false);

            Color sky = theme != null ? theme.skyTint : new Color(0.4f, 0.45f, 0.55f);
            Color ground = theme != null ? theme.floorColor : new Color(0.3f, 0.3f, 0.32f);
            Color wall = theme != null ? theme.wallColor : new Color(0.45f, 0.45f, 0.48f);
            Color accent = theme != null ? theme.accentLightColor : Accent;

            int horizon = Mathf.RoundToInt(PreviewHeight * 0.58f);

            // Deterministic per theme, so rebuilding does not reshuffle the skyline and
            // produce a diff on every run.
            var random = new System.Random(theme != null ? theme.themeName.GetHashCode() : 0);

            var pixels = new Color[PreviewWidth * PreviewHeight];

            for (int y = 0; y < PreviewHeight; y++)
            {
                for (int x = 0; x < PreviewWidth; x++)
                {
                    Color colour;

                    if (y < horizon)
                    {
                        // Ground, darkening toward the viewer.
                        float depth = 1f - y / (float)horizon;
                        colour = Color.Lerp(ground, ground * 0.45f, depth * 0.8f);

                        // Banded rather than smooth: the steps are the pixel look.
                        if (((y / 6) & 1) == 0) colour *= 1.06f;
                    }
                    else
                    {
                        float up = (y - horizon) / (float)(PreviewHeight - horizon);
                        colour = Color.Lerp(sky * 1.05f, sky * 0.62f, up);
                    }

                    pixels[y * PreviewWidth + x] = colour;
                }
            }

            // A skyline of slabs sitting on the horizon.
            int cursor = 8;
            while (cursor < PreviewWidth - 8)
            {
                int width = 18 + random.Next(46);
                int height = 14 + random.Next(58);
                Color slab = Color.Lerp(wall, sky * 0.5f, 0.35f) * (0.8f + (float)random.NextDouble() * 0.4f);

                for (int x = cursor; x < Mathf.Min(cursor + width, PreviewWidth); x++)
                {
                    for (int y = horizon - 1; y < Mathf.Min(horizon + height, PreviewHeight); y++)
                        pixels[y * PreviewWidth + x] = slab;

                    // A lit window strip, so the block reads as built rather than as a bar.
                    int lit = horizon + height / 2;
                    if (lit < PreviewHeight && ((x / 5) & 1) == 0 && random.NextDouble() > 0.55f)
                        pixels[lit * PreviewWidth + x] = accent;
                }

                cursor += width + 4 + random.Next(12);
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        static void Save(Texture2D texture, string path)
        {
            if (texture == null) return;

            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                // Point filtering and no compression: the card is meant to look like
                // pixels, and a block-compressed 512px screenshot scaled into a card is
                // mush exactly where the detail matters.
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        // ==================================================================
        // Scene
        // ==================================================================
        static void BuildScene()
        {
            // Loaded here rather than passed in: building the previews reimported a
            // texture per arena, and any one of those can reload the asset database out
            // from under a reference taken earlier.
            var catalog = AssetDatabase.LoadAssetAtPath<ArenaCatalog>(CatalogPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";

            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Backdrop;
            camera.orthographic = true;
            cameraGo.AddComponent<AudioListener>();

            EnsureEventSystem();

            var canvasGo = new GameObject("Dashboard Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            var menu = canvasGo.AddComponent<MainMenuController>();
            menu.catalog = catalog;

            menu.sounds = BuildSound(canvasGo);

            var root = (RectTransform)canvasGo.transform;

            Backdrop2D(root);
            BuildHeader(root, menu);
            BuildLastRun(root, menu);
            BuildArenaArea(root, menu);
            BuildProfile(root, menu);
            BuildFooter(root, menu);
            BuildLevelSelect(root, menu);
            BuildStore(root, menu);
            BuildAchievements(root, menu);

            // The dashboard has no player rig to read bindings off, so the panel is handed
            // the same asset the arenas use. Loaded rather than left null so the screen
            // describes the keys the game is actually bound to and not the shipped default.
            BuildInstructions(root, menu,
                AssetDatabase.LoadAssetAtPath<ControlSettings>(
                    "Assets/FPSKit_Generated/Controls.asset"));

            // Everything that belongs to the arena picker, so the level select can take
            // the screen rather than being drawn on top of it. Collected after the
            // panels exist, which is why it is not built into each of them.
            //
            // Exit Game is in the list for a reason beyond tidiness: the level select is
            // a full-screen raycast target, so a button left switched on behind it is
            // one the pointer can never reach. Drawn but unreachable is the exact
            // failure MakeButton and VerifyFlow exist to prevent.
            menu.dashboardOnly = new[]
            {
                menu.cardParent.gameObject,
                menu.arenaHeading.gameObject,
                menu.profilePanel,
                menu.lastRunPanel,
                menu.exitRow,
                menu.storeButton.gameObject,

                // Every nav button, for the reason Exit Game is here: an overlay is a
                // full-screen raycast target, so any button left switched on behind one is
                // drawn and unreachable -- which is the exact failure MakeButton and
                // VerifyFlow exist to catch.
                menu.achievementsButton.gameObject,
                menu.helpButton.gameObject,

                menu.statusText.gameObject
            };

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, MenuScenePath);

            RegisterAsFirstScene(MenuScenePath);
        }



        /// <summary>
        /// The dashboard's music bed and interface sounds.
        ///
        /// Two sources rather than one: a one-shot played on the music source would cut
        /// the bed off mid-bar, and the two want very different levels.
        /// </summary>
        static UISounds BuildSound(GameObject canvasGo)
        {
            var sounds = canvasGo.AddComponent<UISounds>();

            var effects = canvasGo.AddComponent<AudioSource>();
            effects.playOnAwake = false;
            effects.spatialBlend = 0f;

            var musicGo = new GameObject("Music", typeof(AudioSource));
            musicGo.transform.SetParent(canvasGo.transform, false);

            var music = musicGo.GetComponent<AudioSource>();
            music.playOnAwake = true;
            music.loop = true;
            music.spatialBlend = 0f;
            music.clip = Clip("Music/menu_loop.wav");

            sounds.effects = effects;
            sounds.music = music;
            sounds.click = Clip("UI/ui_click.wav");
            sounds.hover = Clip("UI/ui_hover.wav");
            sounds.back = Clip("UI/ui_back.wav");
            sounds.launch = Clip("UI/ui_launch.wav");
            sounds.purchase = Clip("UI/purchase.wav");

            return sounds;
        }

        /// <summary>
        /// Loads a clip, saying so when it cannot.
        ///
        /// Same reasoning as FPSKitSceneBuilder.Clip: every audio field here is optional,
        /// so a clip that fails to load writes a silent null into a slot that a previous
        /// build filled, and the only symptom is a menu that went quiet.
        /// </summary>
        static AudioClip Clip(string relativePath)
        {
            string path = $"Assets/Audio/{relativePath}";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);

            if (clip == null)
                Debug.LogWarning($"[FPSKit] No AudioClip at {path}; the dashboard is being " +
                                 "built without it.");

            return clip;
        }

        static void Backdrop2D(RectTransform parent)
        {
            var image = Block(parent, "Backdrop", Backdrop);
            Stretch(image.rectTransform);

            // A faint grid, which is most of what makes a flat screen look intentional.
            for (int i = 1; i < 12; i++)
            {
                var line = Block(parent, $"GridLine_{i}", new Color(1f, 1f, 1f, 0.018f));
                var rect = line.rectTransform;
                rect.anchorMin = new Vector2(i / 12f, 0f);
                rect.anchorMax = new Vector2(i / 12f, 1f);
                rect.sizeDelta = new Vector2(2f, 0f);
                rect.anchoredPosition = Vector2.zero;
            }
        }

        // ------------------------------------------------------------------
        static void BuildHeader(RectTransform parent, MainMenuController menu)
        {
            var bar = Panelled(parent, "Header", PanelLift, out RectTransform inner);
            // Stretched across the top and sized from the edges. Setting offsetMin and
            // offsetMax here instead would be fighting the same field: with the vertical
            // anchors collapsed to a line, sizeDelta *is* the offsets, and writing both
            // leaves whichever ran last -- which is how the header ended up inset by a
            // quarter of the screen.
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(-80f, 104f);
            bar.anchoredPosition = new Vector2(0f, -36f);

            var title = Label(inner, "Title", "MINI  FPS", 44, TextAlignmentOptions.MidlineLeft, Ink);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(0.6f, 1f);
            titleRect.offsetMin = new Vector2(28f, 0f);
            titleRect.offsetMax = Vector2.zero;
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            title.characterSpacing = 12f;

            var accent = Block(inner, "TitleAccent", Accent);
            var accentRect = accent.rectTransform;
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.sizeDelta = new Vector2(8f, -28f);
            accentRect.anchoredPosition = new Vector2(10f, 0f);

            var who = Label(inner, "PlayerName", "OPERATIVE", 26,
                            TextAlignmentOptions.MidlineRight, InkDim);
            var whoRect = who.rectTransform;
            whoRect.anchorMin = new Vector2(0.55f, 0f);
            whoRect.anchorMax = new Vector2(1f, 0.52f);
            whoRect.pivot = new Vector2(0.5f, 0.5f);
            whoRect.offsetMin = Vector2.zero;
            whoRect.offsetMax = new Vector2(-28f, 0f);
            who.characterSpacing = 6f;

            menu.playerNameText = who;

            // The balance sits in the header rather than only in the store, so a player
            // coming out of a level can see what it paid without going looking.
            var coins = Label(inner, "Coins", "0", 30,
                              TextAlignmentOptions.MidlineRight, Accent);
            var coinRect = coins.rectTransform;
            coinRect.anchorMin = new Vector2(0.55f, 0.48f);
            coinRect.anchorMax = new Vector2(1f, 1f);
            coinRect.pivot = new Vector2(0.5f, 0.5f);
            coinRect.offsetMin = Vector2.zero;
            coinRect.offsetMax = new Vector2(-28f, 0f);
            coins.characterSpacing = 4f;
            Autosize(coins, 16f, 30f);

            menu.coinText = coins;
        }

        // ------------------------------------------------------------------
        static void BuildLastRun(RectTransform parent, MainMenuController menu)
        {
            var strip = Panelled(parent, "LastRun", Panel, out RectTransform inner);
            strip.anchorMin = new Vector2(0f, 1f);
            strip.anchorMax = new Vector2(1f, 1f);
            strip.pivot = new Vector2(0.5f, 1f);
            strip.sizeDelta = new Vector2(-80f, 96f);
            strip.anchoredPosition = new Vector2(0f, -156f);

            var flash = Block(inner, "OutcomeAccent", new Color32(0xD9, 0x3B, 0x3B, 0xFF));
            var flashRect = flash.rectTransform;
            flashRect.anchorMin = new Vector2(0f, 0f);
            flashRect.anchorMax = new Vector2(0f, 1f);
            flashRect.pivot = new Vector2(0f, 0.5f);
            flashRect.sizeDelta = new Vector2(8f, 0f);
            flashRect.anchoredPosition = Vector2.zero;

            var title = Label(inner, "OutcomeTitle", "YOU WERE KILLED", 26,
                              TextAlignmentOptions.BottomLeft, Ink);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 0.45f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.offsetMin = new Vector2(28f, 0f);
            titleRect.offsetMax = new Vector2(-24f, -8f);
            title.characterSpacing = 8f;

            var detail = Label(inner, "OutcomeDetail", "", 20,
                               TextAlignmentOptions.TopLeft, InkDim);
            var detailRect = detail.rectTransform;
            detailRect.anchorMin = new Vector2(0f, 0f);
            detailRect.anchorMax = new Vector2(1f, 0.45f);
            detailRect.pivot = new Vector2(0.5f, 0.5f);
            detailRect.offsetMin = new Vector2(28f, 8f);
            detailRect.offsetMax = new Vector2(-24f, 0f);

            menu.lastRunPanel = strip.gameObject;
            menu.lastRunTitle = title;
            menu.lastRunDetail = detail;

            strip.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------
        static void BuildArenaArea(RectTransform parent, MainMenuController menu)
        {
            var heading = Label(parent, "ArenaHeading", "SELECT  ARENA", 22,
                                TextAlignmentOptions.MidlineLeft, InkDim);
            var headingRect = heading.rectTransform;
            headingRect.anchorMin = new Vector2(0f, 1f);
            headingRect.anchorMax = new Vector2(0.72f, 1f);
            headingRect.pivot = new Vector2(0.5f, 1f);
            headingRect.sizeDelta = new Vector2(-88f, 40f);
            headingRect.anchoredPosition = new Vector2(2f, -272f);
            heading.characterSpacing = 10f;

            var grid = new GameObject("ArenaGrid", typeof(RectTransform)).GetComponent<RectTransform>();
            grid.SetParent(parent, false);
            grid.anchorMin = new Vector2(0f, 0f);
            grid.anchorMax = new Vector2(0.72f, 1f);
            grid.offsetMin = new Vector2(40f, 120f);
            grid.offsetMax = new Vector2(0f, -320f);

            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();

            // A starting size only. MainMenuController recomputes the cell from the real
            // width of this rect every time it changes, because a fixed cell is only
            // correct at one aspect ratio -- at 4:3 three 404-wide cards overflow the
            // column and slide under the record panel, and a browser window is whatever
            // shape the player left it.
            layout.cellSize = new Vector2(404f, 286f);
            layout.spacing = new Vector2(20f, 20f);
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 3;

            menu.cardParent = grid;
            menu.arenaHeading = headingRect;
            menu.cardTemplate = BuildCardTemplate(parent);
        }

        /// <summary>
        /// The card, built once and left switched off.
        ///
        /// Parented to the canvas rather than to the grid so the layout group never sees
        /// it: a template inside the grid would be counted as a cell, and the first real
        /// arena would sit in the second slot behind an invisible hole.
        /// </summary>
        static ArenaCard BuildCardTemplate(RectTransform parent)
        {
            var root = Panelled(parent, "ArenaCardTemplate", Panel, out RectTransform inner);
            root.sizeDelta = new Vector2(404f, 286f);
            root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0.5f, 0.5f);

            var card = root.gameObject.AddComponent<ArenaCard>();

            var button = root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            card.button = button;

            // A card starts a run, so it gets the two-note launch rather than a click.
            root.gameObject.AddComponent<UIButtonSound>().voice = UIButtonSound.Voice.Launch;

            // The frame is the outer block of the panel, which is what the hover recolours.
            card.frame = root.GetComponent<Image>();
            card.frame.raycastTarget = true;

            // Everything inside the card is anchored as a fraction of it rather than in
            // pixels from the top, so the card survives being resized by the grid. Laid
            // out in pixels, a card that shrank kept a 166px preview and pushed its own
            // text out through the bottom edge.
            var preview = Block(inner, "Preview", new Color(0.1f, 0.11f, 0.13f));
            Span(preview.rectTransform, 0.465f, 1f, 0f, 0f);
            preview.type = Image.Type.Simple;
            preview.preserveAspect = false;
            card.preview = preview;

            var accent = Block(inner, "AccentBar", Accent);
            Span(accent.rectTransform, 0.448f, 0.465f, 0f, 0f);
            card.accentBar = accent;

            // Auto-sized, both of them. The cell width now depends on the window, and a
            // fixed point size only fits at one of them -- "INDUSTRIAL WAREHOUSE" came
            // out as "INDUSTRIAL WAREHOUS" the moment the grid narrowed. Shrinking to
            // fit is better than truncating the one word that says where you are going.
            var name = Label(inner, "Name", "ARENA", 25, TextAlignmentOptions.MidlineLeft, Ink);
            Span(name.rectTransform, 0.335f, 0.448f, 14f, 14f);
            name.characterSpacing = 4f;
            Autosize(name, 13f, 25f);
            card.nameText = name;

            var description = Label(inner, "Description", "", 17,
                                    TextAlignmentOptions.TopLeft, InkDim);
            Span(description.rectTransform, 0.115f, 0.335f, 14f, 14f);
            description.textWrappingMode = TextWrappingModes.Normal;

            // Ellipsis rather than Truncate: at the smallest card a long description
            // still will not fit, and trailing off reads as a summary while a glyph
            // sliced in half reads as a rendering fault.
            description.overflowMode = TextOverflowModes.Ellipsis;
            Autosize(description, 9f, 16f);
            card.descriptionText = description;

            // The smallest type on the busiest screen. On a monitor it is what tells the
            // arenas apart; on a handset it is four lines of 1.6mm text under a thumbnail
            // the player has already recognised, and dropping it is most of what turns
            // this grid from a page to read into a row of things to press.
            description.gameObject.AddComponent<HideOnPhone>();

            var best = Label(inner, "Best", "8 LEVELS", 16,
                             TextAlignmentOptions.BottomLeft, Accent);
            Span(best.rectTransform, 0.015f, 0.115f, 14f, 14f);
            best.characterSpacing = 6f;
            card.bestText = best;

            root.gameObject.SetActive(false);
            return card;
        }

        // ------------------------------------------------------------------
        static void BuildProfile(RectTransform parent, MainMenuController menu)
        {
            var panel = Panelled(parent, "Profile", Panel, out RectTransform inner);
            panel.anchorMin = new Vector2(0.72f, 0f);
            panel.anchorMax = new Vector2(1f, 1f);
            panel.offsetMin = new Vector2(20f, 120f);
            panel.offsetMax = new Vector2(-40f, -272f);

            var heading = Label(inner, "Heading", "RECORD", 22,
                                TextAlignmentOptions.MidlineLeft, InkDim);
            var headingRect = heading.rectTransform;
            headingRect.anchorMin = new Vector2(0f, 1f);
            headingRect.anchorMax = new Vector2(1f, 1f);
            headingRect.pivot = new Vector2(0.5f, 1f);
            headingRect.sizeDelta = new Vector2(-48f, 54f);
            headingRect.anchoredPosition = new Vector2(0f, -10f);
            heading.characterSpacing = 10f;

            // The stats live in their own container and divide it evenly, rather than
            // sitting at fixed pixel offsets from the top of the panel.
            //
            // The old layout placed each row a fixed distance down and pinned a block of
            // key hints to the bottom, which is fine at one window height and collides at
            // any shorter one -- "TOTAL KILLS" ran straight into the hints. Splitting a
            // measured box four ways cannot collide, whatever shape the window is.
            var rows = new GameObject("Stats", typeof(RectTransform)).GetComponent<RectTransform>();
            rows.SetParent(inner, false);
            rows.anchorMin = Vector2.zero;
            rows.anchorMax = Vector2.one;
            rows.offsetMin = new Vector2(0f, 24f);
            rows.offsetMax = new Vector2(0f, -64f);

            // Five rows now, and the count is passed to every one of them rather than
            // being a constant in Stat. Splitting a measured box n ways cannot collide at
            // any window height; hard-coding 4 in one of five calls would put two of them
            // on top of each other at exactly the size nobody tests at.
            menu.starsText = Stat(rows, "Stars", "STARS EARNED", 0, 5);
            menu.coinsEarnedText = Stat(rows, "Coins", "COINS EARNED", 1, 5);
            menu.bestScoreText = Stat(rows, "BestScore", "BEST SCORE", 2, 5);
            menu.runsText = Stat(rows, "Runs", "RUNS PLAYED", 3, 5);
            menu.killsText = Stat(rows, "Kills", "TOTAL KILLS", 4, 5);

            // The whole career panel goes on a handset. Not because the numbers stop
            // mattering, but because it is a column of five two-line rows competing with
            // the arena grid for a 147mm screen, and the wallet -- the one figure a
            // player checks before opening the store -- is already in the header. What
            // it frees is width, which the grid spends making every card bigger.
            panel.gameObject.AddComponent<HideOnPhone>();

            // The key hints that used to sit under these are gone. They were duplicated
            // from the strip that is on screen during the whole run, and this is the
            // dashboard -- nothing here is happening in a run.

            menu.profilePanel = panel.gameObject;
        }

        /// <summary>One label-over-number row, occupying its share of the stats box.</summary>
        static TMP_Text Stat(RectTransform parent, string name, string caption, int index, int count)
        {
            float height = 1f / Mathf.Max(1, count);
            float top = 1f - index * height;
            float bottom = top - height;

            var row = new GameObject($"{name}Row", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(parent, false);
            row.anchorMin = new Vector2(0f, bottom);
            row.anchorMax = new Vector2(1f, top);
            row.pivot = new Vector2(0.5f, 0.5f);
            row.offsetMin = new Vector2(24f, 0f);
            row.offsetMax = new Vector2(-24f, 0f);

            // A hairline above each row turns four stacked numbers into a list.
            var rule = Block(row, $"{name}Rule", new Color(1f, 1f, 1f, 0.07f));
            rule.rectTransform.anchorMin = new Vector2(0f, 1f);
            rule.rectTransform.anchorMax = new Vector2(1f, 1f);
            rule.rectTransform.pivot = new Vector2(0.5f, 1f);
            rule.rectTransform.sizeDelta = new Vector2(0f, 2f);
            rule.rectTransform.anchoredPosition = Vector2.zero;

            var label = Label(row, $"{name}Caption", caption, 16,
                              TextAlignmentOptions.TopLeft, InkDim);
            Span(label.rectTransform, 0.52f, 0.94f, 0f, 0f);
            label.characterSpacing = 8f;
            Autosize(label, 10f, 16f);

            var value = Label(row, $"{name}Value", "0", 38,
                              TextAlignmentOptions.TopLeft, Ink);
            Span(value.rectTransform, 0.06f, 0.54f, 0f, 0f);
            Autosize(value, 16f, 38f);

            return value;
        }

        // ------------------------------------------------------------------
        static void BuildFooter(RectTransform parent, MainMenuController menu)
        {
            var status = Label(parent, "Status", "", 18, TextAlignmentOptions.MidlineLeft,
                               new Color32(0xE0, 0x7A, 0x5F, 0xFF));
            var statusRect = status.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(0.7f, 0f);
            statusRect.pivot = new Vector2(0.5f, 0f);
            statusRect.sizeDelta = new Vector2(-88f, 72f);
            statusRect.anchoredPosition = new Vector2(2f, 34f);
            status.textWrappingMode = TextWrappingModes.Normal;
            menu.statusText = status;

            // The four things a player does that are not playing a level, in one row.
            //
            // A layout group rather than four anchored offsets, because the old pair were
            // placed at fixed pixels from the right edge and a third and fourth would have
            // reached 1,120px in -- fine at one window width and off the screen at a
            // narrower one. Same rule the record panel already follows: lay out in a
            // measured box, not in pixels from an edge.
            var navGo = new GameObject("NavRow", typeof(RectTransform));
            navGo.transform.SetParent(parent, false);
            var navRect = (RectTransform)navGo.transform;
            navRect.anchorMin = new Vector2(0f, 0f);
            navRect.anchorMax = new Vector2(1f, 0f);
            navRect.pivot = new Vector2(1f, 0f);
            navRect.offsetMin = new Vector2(40f, 28f);
            navRect.offsetMax = new Vector2(-40f, 96f);

            var nav = navGo.AddComponent<HorizontalLayoutGroup>();
            nav.childAlignment = TextAnchor.MiddleRight;
            nav.spacing = 16f;
            nav.childForceExpandWidth = false;
            nav.childForceExpandHeight = true;
            nav.childControlWidth = true;
            nav.childControlHeight = true;

            // Ordered by how often a player wants them, left to right, with the one that
            // ends the session furthest from the others.
            var help = NavButton(navRect, "HelpButton", "HOW  TO  PLAY", Accent);
            var career = NavButton(navRect, "CareerButton", "ACHIEVEMENTS", UITheme.Good);
            var store = NavButton(navRect, "StoreButton", "STORE", Accent);
            var exit = NavButton(navRect, "ExitButton", "EXIT  GAME", Danger);

            menu.helpButton = help;
            menu.achievementsButton = career;
            menu.storeButton = store;
            menu.exitButton = exit;
            menu.exitRow = exit.gameObject;

            BuildExitConfirm(parent, menu);
        }

        /// <summary>
        /// The "are you sure" over the Exit button.
        ///
        /// Exiting is the only irreversible thing on this screen -- in a browser it
        /// replaces the page, and there is no undo for a mis-click on the way past. The
        /// overlay is a full-screen raycast target on purpose: it swallows clicks meant
        /// for the dashboard underneath, so the dialog is genuinely modal rather than
        /// merely drawn on top.
        /// </summary>
        static void BuildExitConfirm(RectTransform parent, MainMenuController menu)
        {
            var shade = Block(parent, "ExitConfirm", new Color(0.02f, 0.03f, 0.04f, 0.82f));
            Stretch(shade.rectTransform);
            shade.raycastTarget = true;

            var box = Panelled(shade.rectTransform, "Dialog", Panel, out RectTransform inner);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(760f, 300f);
            box.anchoredPosition = Vector2.zero;

            var stripe = Block(inner, "Stripe", Danger);
            stripe.rectTransform.anchorMin = new Vector2(0f, 1f);
            stripe.rectTransform.anchorMax = new Vector2(1f, 1f);
            stripe.rectTransform.pivot = new Vector2(0.5f, 1f);
            stripe.rectTransform.sizeDelta = new Vector2(0f, 6f);
            stripe.rectTransform.anchoredPosition = Vector2.zero;

            var question = Label(inner, "Question", "ARE YOU REALLY EXITING THE GAME?", 30,
                                 TextAlignmentOptions.Center, Ink);
            Span(question.rectTransform, 0.52f, 0.9f, 32f, 32f);
            question.textWrappingMode = TextWrappingModes.Normal;
            question.characterSpacing = 4f;
            Autosize(question, 16f, 30f);

            var note = Label(inner, "Note", "Your record is saved. Runs are not.", 18,
                             TextAlignmentOptions.Center, InkDim);
            Span(note.rectTransform, 0.36f, 0.52f, 32f, 32f);
            Autosize(note, 12f, 18f);

            var cancel = MakeButton(inner, "Cancel", "STAY", PanelLift, Border);
            cancel.GetComponent<UIButtonSound>().voice = UIButtonSound.Voice.Back;
            var cancelRect = (RectTransform)cancel.transform;
            cancelRect.anchorMin = cancelRect.anchorMax = new Vector2(0.5f, 0f);
            cancelRect.pivot = new Vector2(1f, 0f);
            cancelRect.sizeDelta = new Vector2(240f, 64f);
            cancelRect.anchoredPosition = new Vector2(-12f, 34f);

            var confirm = MakeButton(inner, "Confirm", "EXIT", Danger, DangerLift);
            var confirmRect = (RectTransform)confirm.transform;
            confirmRect.anchorMin = confirmRect.anchorMax = new Vector2(0.5f, 0f);
            confirmRect.pivot = new Vector2(0f, 0f);
            confirmRect.sizeDelta = new Vector2(240f, 64f);
            confirmRect.anchoredPosition = new Vector2(12f, 34f);

            menu.exitConfirmPanel = shade.gameObject;
            menu.confirmExitButton = confirm;
            menu.cancelExitButton = cancel;

            shade.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// The level select: the screen an arena card opens.
        ///
        /// Built into the dashboard scene rather than being a scene of its own, because
        /// it is the same screen with the arenas swapped for that arena's ladder -- a
        /// second scene would mean a second load, a second music start and a second
        /// place for the record panel to be wired.
        ///
        /// The whole panel starts switched off and is stretched over everything, so it
        /// covers the dashboard rather than sitting in a corner of it.
        /// </summary>
        static void BuildLevelSelect(RectTransform parent, MainMenuController menu)
        {
            var shade = Block(parent, "LevelSelect", Backdrop);
            Stretch(shade.rectTransform);

            // A raycast target, so a click that misses a tile does not fall through to
            // an arena card underneath and start a different arena entirely.
            shade.raycastTarget = true;

            // The component goes on the canvas, not on the panel it switches on and off.
            //
            // A MonoBehaviour that hides its own GameObject in Awake never gets to show
            // it again: the builder leaves the panel switched off, so Awake has not run
            // yet, and the first SetActive(true) is what finally runs it -- which
            // immediately switches the object back off. The screen then simply never
            // opens, with nothing logged. Living on the canvas, which is always active,
            // means Awake runs once at scene load like anything else.
            var select = parent.gameObject.AddComponent<LevelSelectPanel>();
            select.panel = shade.gameObject;

            // The same faint grid as the dashboard, so the two read as one screen.
            for (int i = 1; i < 12; i++)
            {
                var line = Block(shade.rectTransform, $"GridLine_{i}", new Color(1f, 1f, 1f, 0.018f));
                var rect = line.rectTransform;
                rect.anchorMin = new Vector2(i / 12f, 0f);
                rect.anchorMax = new Vector2(i / 12f, 1f);
                rect.sizeDelta = new Vector2(2f, 0f);
                rect.anchoredPosition = Vector2.zero;
            }

            var header = Panelled(shade.rectTransform, "Header", PanelLift, out RectTransform inner);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(-80f, 104f);
            header.anchoredPosition = new Vector2(0f, -36f);

            var accent = Block(inner, "Accent", Accent);
            var accentRect = accent.rectTransform;
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.sizeDelta = new Vector2(8f, -28f);
            accentRect.anchoredPosition = new Vector2(10f, 0f);

            var arenaName = Label(inner, "ArenaName", "ARENA", 40,
                                  TextAlignmentOptions.MidlineLeft, Ink);
            var nameRect = arenaName.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(0.62f, 1f);
            nameRect.pivot = new Vector2(0.5f, 0.5f);
            nameRect.offsetMin = new Vector2(28f, 0f);
            nameRect.offsetMax = Vector2.zero;
            arenaName.characterSpacing = 8f;
            Autosize(arenaName, 18f, 40f);

            var progress = Label(inner, "Progress", "", 24,
                                 TextAlignmentOptions.MidlineRight, Accent);
            var progressRect = progress.rectTransform;
            progressRect.anchorMin = new Vector2(0.55f, 0f);
            progressRect.anchorMax = new Vector2(1f, 1f);
            progressRect.pivot = new Vector2(0.5f, 0.5f);
            progressRect.offsetMin = Vector2.zero;
            progressRect.offsetMax = new Vector2(-28f, 0f);
            progress.characterSpacing = 4f;
            Autosize(progress, 13f, 24f);

            var hint = Label(shade.rectTransform, "Hint", "", 20,
                             TextAlignmentOptions.MidlineLeft, InkDim);
            var hintRect = hint.rectTransform;
            hintRect.anchorMin = new Vector2(0f, 1f);
            hintRect.anchorMax = new Vector2(1f, 1f);
            hintRect.pivot = new Vector2(0.5f, 1f);
            hintRect.sizeDelta = new Vector2(-88f, 40f);
            hintRect.anchoredPosition = new Vector2(2f, -156f);
            hint.textWrappingMode = TextWrappingModes.Normal;

            var grid = new GameObject("LevelGrid", typeof(RectTransform)).GetComponent<RectTransform>();
            grid.SetParent(shade.rectTransform, false);
            grid.anchorMin = Vector2.zero;
            grid.anchorMax = Vector2.one;
            grid.offsetMin = new Vector2(40f, 120f);
            grid.offsetMax = new Vector2(-40f, -210f);

            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();

            // A starting size only, exactly like the arena grid: LevelSelectPanel
            // recomputes the cell from the real size of this rect, because a fixed cell
            // draws the bottom row of levels off the bottom of a shorter window and a
            // GridLayoutGroup neither clips nor scrolls.
            layout.cellSize = new Vector2(300f, 216f);
            layout.spacing = new Vector2(20f, 20f);
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 4;

            var status = Label(shade.rectTransform, "Status", "", 18,
                               TextAlignmentOptions.MidlineLeft,
                               new Color32(0xE0, 0x7A, 0x5F, 0xFF));
            var statusRect = status.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(0.65f, 0f);
            statusRect.pivot = new Vector2(0.5f, 0f);
            statusRect.sizeDelta = new Vector2(-88f, 72f);
            statusRect.anchoredPosition = new Vector2(2f, 34f);
            status.textWrappingMode = TextWrappingModes.Normal;

            var back = MakeButton(shade.rectTransform, "BackButton", "BACK", PanelLift, Border);
            back.GetComponent<UIButtonSound>().voice = UIButtonSound.Voice.Back;
            var backRect = (RectTransform)back.transform;
            backRect.anchorMin = new Vector2(1f, 0f);
            backRect.anchorMax = new Vector2(1f, 0f);
            backRect.pivot = new Vector2(1f, 0f);
            backRect.sizeDelta = new Vector2(260f, 68f);
            backRect.anchoredPosition = new Vector2(-40f, 34f);

            select.tileParent = grid;
            select.tileTemplate = BuildLevelTileTemplate(shade.rectTransform);
            select.backButton = back;
            select.arenaNameText = arenaName;
            select.progressText = progress;
            select.hintText = hint;
            select.statusText = status;
            select.gridColumns = 4;

            menu.levelSelect = select;

            shade.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// What the player has done, in four columns.
        ///
        /// The component goes on the canvas and points at the shade, never on the shade --
        /// put it on the object it hides and Awake has not run when the builder leaves it
        /// off, so the first SetActive(true) is what finally runs it and switches it
        /// straight back off. OverlayPanel logs that rather than failing silently.
        /// </summary>
        static void BuildAchievements(RectTransform parent, MainMenuController menu)
        {
            var panel = menu.gameObject.AddComponent<AchievementsPanel>();

            var shade = BuildOverlayShell(parent, "Achievements", "Achievements",
                                          "", out var tally, out var back, out var body);

            // The subtitle slot carries the tally, because "7 of 20" is the one number
            // somebody opening this screen is actually looking for.
            tally.text = "";
            tally.color = Accent;
            tally.fontSize = 28f;

            var columns = BuildColumns(body, 4, out var headings);

            panel.panel = shade.gameObject;
            panel.backButton = back;
            panel.tallyText = tally;
            panel.categoryColumns = columns;
            panel.categoryHeadings = headings;
            panel.rowTemplate = BuildAchievementRowTemplate(shade.rectTransform);
            panel.headingTemplate = BuildInstructionRowTemplate(shade.rectTransform);

            menu.achievements = panel;
            shade.gameObject.SetActive(false);
        }

        static AchievementRow BuildAchievementRowTemplate(RectTransform parent)
        {
            var rowGo = new GameObject("AchievementRowTemplate", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var rect = (RectTransform)rowGo.transform;
            rect.sizeDelta = new Vector2(0f, 92f);

            var element = rowGo.AddComponent<LayoutElement>();
            element.preferredHeight = 92f;
            element.minHeight = 92f;

            var plate = Block(rect, "Plate", PanelLift);
            Stretch(plate.rectTransform);
            plate.raycastTarget = false;

            var row = rowGo.AddComponent<AchievementRow>();

            row.titleText = Label(rect, "Title", "TITLE", 22, TextAlignmentOptions.TopLeft, Ink);
            Span(row.titleText.rectTransform, 0.58f, 0.98f, 14f, 90f);

            row.detailText = Label(rect, "Detail", "detail", 16,
                                   TextAlignmentOptions.TopLeft, InkDim);
            Span(row.detailText.rectTransform, 0.26f, 0.58f, 14f, 90f);
            row.detailText.textWrappingMode = TextWrappingModes.Normal;

            row.readoutText = Label(rect, "Readout", "0 / 0", 16,
                                    TextAlignmentOptions.TopRight, InkDim);
            Span(row.readoutText.rectTransform, 0.58f, 0.98f, 14f, 14f);

            var track = Block(rect, "Track", Panel);
            Span(track.rectTransform, 0.10f, 0.20f, 14f, 14f);
            track.raycastTarget = false;
            row.progressTrack = track.gameObject;

            var fill = Block(track.rectTransform, "Fill", Accent);
            Stretch(fill.rectTransform);
            fill.raycastTarget = false;
            row.progressFill = fill;

            // Drawn from two bars rather than typed as U+2713. The project's only font is
            // LiberationSans SDF, which has no tick: TMP substitutes U+25A1 and logs it, so
            // what appeared beside the word COMPLETE was an empty box -- which reads as a
            // rendering fault rather than as a mark. Two rotated rectangles need no glyph
            // and cannot fall back to anything.
            var markGo = new GameObject("Mark", typeof(RectTransform));
            markGo.transform.SetParent(rect, false);
            var markRect = (RectTransform)markGo.transform;
            markRect.anchorMin = new Vector2(1f, 0.5f);
            markRect.anchorMax = new Vector2(1f, 0.5f);
            markRect.pivot = new Vector2(1f, 0.5f);
            markRect.sizeDelta = new Vector2(30f, 30f);
            markRect.anchoredPosition = new Vector2(-14f, 14f);

            var shortArm = Block(markRect, "Short", UITheme.Good);
            shortArm.raycastTarget = false;
            var shortRect = shortArm.rectTransform;
            shortRect.anchorMin = shortRect.anchorMax = new Vector2(0.5f, 0.5f);
            shortRect.pivot = new Vector2(0.5f, 0.5f);
            shortRect.sizeDelta = new Vector2(13f, 4f);
            shortRect.anchoredPosition = new Vector2(-8f, -3f);
            shortRect.localRotation = Quaternion.Euler(0f, 0f, -45f);

            var longArm = Block(markRect, "Long", UITheme.Good);
            longArm.raycastTarget = false;
            var longRect = longArm.rectTransform;
            longRect.anchorMin = longRect.anchorMax = new Vector2(0.5f, 0.5f);
            longRect.pivot = new Vector2(0.5f, 0.5f);
            longRect.sizeDelta = new Vector2(24f, 4f);
            longRect.anchoredPosition = new Vector2(2f, 1f);
            longRect.localRotation = Quaternion.Euler(0f, 0f, 45f);

            row.earnedMark = markGo;

            rowGo.SetActive(false);
            return row;
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// How to play, in four columns, written from the live bindings at runtime.
        /// </summary>
        static void BuildInstructions(RectTransform parent, MainMenuController menu,
                                      ControlSettings controls)
        {
            var panel = menu.gameObject.AddComponent<InstructionsPanel>();

            var shade = BuildOverlayShell(parent, "Instructions", "How to play",
                                          "", out var subtitle, out var back, out var body);

            var columns = BuildColumns(body, 4, out var headings);

            panel.panel = shade.gameObject;
            panel.backButton = back;
            panel.subtitleText = subtitle;
            panel.columns = columns;
            panel.columnHeadings = headings;
            panel.controls = controls;
            panel.rowTemplate = BuildInstructionRowTemplate(shade.rectTransform);

            menu.instructions = panel;
            shade.gameObject.SetActive(false);
        }

        static InstructionRow BuildInstructionRowTemplate(RectTransform parent)
        {
            var rowGo = new GameObject("InstructionRowTemplate", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var rect = (RectTransform)rowGo.transform;
            rect.sizeDelta = new Vector2(0f, 62f);

            var element = rowGo.AddComponent<LayoutElement>();
            element.preferredHeight = 62f;
            element.minHeight = 44f;

            var row = rowGo.AddComponent<InstructionRow>();

            row.controlText = Label(rect, "Control", "KEY", 19,
                                    TextAlignmentOptions.TopLeft, Accent);
            Span(row.controlText.rectTransform, 0.52f, 1f, 4f, 4f);

            row.saysText = Label(rect, "Says", "what it does", 16,
                                 TextAlignmentOptions.TopLeft, Ink);
            Span(row.saysText.rectTransform, 0f, 0.52f, 4f, 4f);
            row.saysText.textWrappingMode = TextWrappingModes.Normal;

            rowGo.SetActive(false);
            return row;
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// The shade, the title and the way out that every overlay shares.
        ///
        /// One helper rather than a copy per screen, because the three overlays sit on top
        /// of each other and one that closes differently from the one it replaced reads as
        /// the interface being unreliable -- the same argument HoverCard settles for cards.
        /// </summary>
        static Image BuildOverlayShell(RectTransform parent, string name, string title,
                                       string subtitle, out TMP_Text subtitleLabel,
                                       out Button back, out RectTransform body)
        {
            var shade = Block(parent, name, Backdrop);
            Stretch(shade.rectTransform);

            // The shade eats clicks on purpose: an overlay that lets the dashboard behind
            // it be clicked is an overlay a player can start a level through.
            shade.raycastTarget = true;

            var heading = Label(shade.rectTransform, "Title", title.ToUpperInvariant(), 44,
                                TextAlignmentOptions.MidlineLeft, Ink);
            Span(heading.rectTransform, 0.88f, 0.97f, 48f, 48f);

            subtitleLabel = Label(shade.rectTransform, "Subtitle", subtitle, 20,
                                  TextAlignmentOptions.MidlineLeft, InkDim);
            Span(subtitleLabel.rectTransform, 0.83f, 0.88f, 48f, 48f);

            var rule = Block(shade.rectTransform, "Rule", Accent);
            Span(rule.rectTransform, 0.822f, 0.827f, 48f, 48f);
            rule.raycastTarget = false;

            back = MakeButton(shade.rectTransform, "BackButton", "BACK", PanelLift, Border);
            back.GetComponent<UIButtonSound>().voice = UIButtonSound.Voice.Back;
            var backRect = (RectTransform)back.transform;
            backRect.anchorMin = new Vector2(1f, 0f);
            backRect.anchorMax = new Vector2(1f, 0f);
            backRect.pivot = new Vector2(1f, 0f);
            backRect.sizeDelta = new Vector2(260f, 68f);
            backRect.anchoredPosition = new Vector2(-40f, 34f);

            // Everything between the rule and the back button. Fractions of the screen
            // rather than pixels from an edge, so it cannot collide at a shorter window.
            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(shade.rectTransform, false);
            body = (RectTransform)bodyGo.transform;
            Span(body, 0.10f, 0.80f, 48f, 48f);

            return shade;
        }

        /// <summary>
        /// Four columns that divide whatever width they are given.
        ///
        /// Used by both new overlays. A layout group rather than four anchored boxes for
        /// the reason the nav row is one: a fixed column width is right at one window size.
        /// </summary>
        static RectTransform[] BuildColumns(RectTransform body, int count,
                                            out TMP_Text[] headings)
        {
            var row = body.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 28f;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            row.childControlWidth = true;
            row.childControlHeight = true;

            var columns = new RectTransform[count];
            headings = new TMP_Text[count];

            for (int i = 0; i < count; i++)
            {
                var colGo = new GameObject($"Column{i}", typeof(RectTransform));
                colGo.transform.SetParent(body, false);
                var col = (RectTransform)colGo.transform;

                headings[i] = Label(col, "Heading", "", 22,
                                    TextAlignmentOptions.TopLeft, Accent);
                Span(headings[i].rectTransform, 0.93f, 1f, 0f, 0f);

                // A scroll view rather than a plain stack, and this is the fix for a bug
                // that is on screen as well as a requirement for a phone.
                //
                // A VerticalLayoutGroup neither clips nor scrolls: content that does not
                // fit is simply drawn past the edge, which is how the eighth combat
                // achievement ended up half off the bottom. Shrinking rows to fit buys one
                // window size and loses at the next. A scroll view is correct at every
                // size -- nothing to scroll when it all fits, and on a landscape handset,
                // where twenty rows are never going to fit sixty-six millimetres, it is
                // the only honest answer.
                var viewGo = new GameObject("Viewport", typeof(RectTransform));
                viewGo.transform.SetParent(col, false);
                var view = (RectTransform)viewGo.transform;
                Span(view, 0f, 0.92f, 0f, 0f);

                var mask = viewGo.AddComponent<Image>();
                mask.color = new Color(0f, 0f, 0f, 0f);
                mask.raycastTarget = true;
                viewGo.AddComponent<RectMask2D>();

                var listGo = new GameObject("Rows", typeof(RectTransform));
                listGo.transform.SetParent(view, false);
                var list = (RectTransform)listGo.transform;
                list.anchorMin = new Vector2(0f, 1f);
                list.anchorMax = new Vector2(1f, 1f);
                list.pivot = new Vector2(0.5f, 1f);
                list.offsetMin = new Vector2(0f, 0f);
                list.offsetMax = new Vector2(0f, 0f);

                var stack = listGo.AddComponent<VerticalLayoutGroup>();
                stack.spacing = 10f;
                stack.childForceExpandWidth = true;
                stack.childForceExpandHeight = false;
                stack.childControlWidth = true;
                stack.childControlHeight = true;

                // Without this the content rect never grows to hold its rows, so the
                // scroll view has nothing to scroll and clips everything below the fold.
                var fitter = listGo.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                var scroll = colGo.AddComponent<ScrollRect>();
                scroll.viewport = view;
                scroll.content = list;
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Elastic;
                scroll.elasticity = 0.08f;
                scroll.scrollSensitivity = 30f;

                // Inertia is what makes a flick feel like a flick rather than a drag, and
                // a phone is the only place anybody flicks this.
                scroll.inertia = true;
                scroll.decelerationRate = 0.135f;

                columns[i] = list;
            }

            return columns;
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// The store: four shelves of stock over the top of the dashboard.
        ///
        /// Built into this scene rather than being one of its own, for the same reason
        /// the level select is -- a second scene would mean a second load, a second music
        /// start and a second place for a screen to go wrong. Tabs rather than one long
        /// list, because a GridLayoutGroup does not scroll: a shelf has to fit the window
        /// it is drawn in, and four short shelves always do where one long one never would.
        /// </summary>
        static void BuildStore(RectTransform parent, MainMenuController menu)
        {
            var shade = Block(parent, "Store", Backdrop);
            Stretch(shade.rectTransform);
            shade.raycastTarget = true;

            // On the canvas, not on the panel it hides. A component that switches its own
            // object off in Awake never gets to switch it back on: the builder leaves the
            // panel off, so Awake has not run, and the first SetActive(true) is what
            // finally runs it. LevelSelectPanel learned this the hard way.
            var store = parent.gameObject.AddComponent<StorePanel>();
            store.panel = shade.gameObject;
            store.catalog = AssetDatabase.LoadAssetAtPath<StoreCatalog>(FPSKitStore.CatalogPath);
            store.sounds = menu.sounds;

            for (int i = 1; i < 12; i++)
            {
                var line = Block(shade.rectTransform, $"GridLine_{i}", new Color(1f, 1f, 1f, 0.018f));
                var rect = line.rectTransform;
                rect.anchorMin = new Vector2(i / 12f, 0f);
                rect.anchorMax = new Vector2(i / 12f, 1f);
                rect.sizeDelta = new Vector2(2f, 0f);
                rect.anchoredPosition = Vector2.zero;
            }

            var header = Panelled(shade.rectTransform, "Header", PanelLift, out RectTransform inner);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(-80f, 104f);
            header.anchoredPosition = new Vector2(0f, -36f);

            var accent = Block(inner, "Accent", Accent);
            var accentRect = accent.rectTransform;
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.sizeDelta = new Vector2(8f, -28f);
            accentRect.anchoredPosition = new Vector2(10f, 0f);

            var heading = Label(inner, "Heading", "WEAPONS", 38,
                                TextAlignmentOptions.MidlineLeft, Ink);
            var headingRect = heading.rectTransform;
            headingRect.anchorMin = new Vector2(0f, 0f);
            headingRect.anchorMax = new Vector2(0.55f, 1f);
            headingRect.pivot = new Vector2(0.5f, 0.5f);
            headingRect.offsetMin = new Vector2(28f, 0f);
            headingRect.offsetMax = Vector2.zero;
            heading.characterSpacing = 8f;
            Autosize(heading, 18f, 38f);

            var balance = Label(inner, "Balance", "0", 34,
                                TextAlignmentOptions.MidlineRight, Accent);
            var balanceRect = balance.rectTransform;
            balanceRect.anchorMin = new Vector2(0.5f, 0f);
            balanceRect.anchorMax = new Vector2(1f, 1f);
            balanceRect.pivot = new Vector2(0.5f, 0.5f);
            balanceRect.offsetMin = Vector2.zero;
            balanceRect.offsetMax = new Vector2(-28f, 0f);
            balance.characterSpacing = 4f;
            Autosize(balance, 18f, 34f);

            // ---- tabs ----------------------------------------------------
            // Ordered to match StorePanel.Tab, which is what lets the panel wire them in
            // a loop rather than by name.
            string[] tabs = { "WEAPONS", "EXPLOSIVES", "SUPPLIES", "UPGRADES" };
            var tabButtons = new Button[tabs.Length];

            for (int i = 0; i < tabs.Length; i++)
            {
                var tab = MakeButton(shade.rectTransform, $"Tab_{tabs[i]}", tabs[i],
                                     PanelLift, Accent);

                var rect = (RectTransform)tab.transform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(270f, 58f);
                rect.anchoredPosition = new Vector2(40f + i * 286f, -158f);

                tabButtons[i] = tab;
            }

            var grid = new GameObject("StoreGrid", typeof(RectTransform)).GetComponent<RectTransform>();
            grid.SetParent(shade.rectTransform, false);
            grid.anchorMin = Vector2.zero;
            grid.anchorMax = Vector2.one;
            grid.offsetMin = new Vector2(40f, 120f);
            grid.offsetMax = new Vector2(-40f, -232f);

            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();

            // A starting size only. StorePanel recomputes the cell from the real size of
            // this rect, because a fixed cell is only right at one window shape and a
            // GridLayoutGroup neither clips nor scrolls.
            layout.cellSize = new Vector2(420f, 344f);
            layout.spacing = new Vector2(20f, 20f);
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 3;

            var status = Label(shade.rectTransform, "Status", "", 20,
                               TextAlignmentOptions.MidlineLeft, Accent);
            var statusRect = status.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(0.72f, 0f);
            statusRect.pivot = new Vector2(0.5f, 0f);
            statusRect.sizeDelta = new Vector2(-88f, 72f);
            statusRect.anchoredPosition = new Vector2(2f, 34f);
            status.textWrappingMode = TextWrappingModes.Normal;

            var back = MakeButton(shade.rectTransform, "BackButton", "BACK", PanelLift, Border);
            back.GetComponent<UIButtonSound>().voice = UIButtonSound.Voice.Back;
            var backRect = (RectTransform)back.transform;
            backRect.anchorMin = new Vector2(1f, 0f);
            backRect.anchorMax = new Vector2(1f, 0f);
            backRect.pivot = new Vector2(1f, 0f);
            backRect.sizeDelta = new Vector2(260f, 68f);
            backRect.anchoredPosition = new Vector2(-40f, 34f);

            store.cardParent = grid;
            store.cardTemplate = BuildStoreCardTemplate(shade.rectTransform);
            store.backButton = back;
            store.tabButtons = tabButtons;
            store.balanceText = balance;
            store.headingText = heading;
            store.statusText = status;
            store.gridColumns = 3;

            menu.store = store;

            shade.gameObject.SetActive(false);
        }

        /// <summary>
        /// One store card, built once and left switched off.
        ///
        /// Parented to the panel rather than to the grid, for the same reason the arena
        /// card and the level tile are: a template inside the grid would be counted as a
        /// cell, and the first real item would sit in the second slot behind an
        /// invisible hole.
        /// </summary>
        static StoreItemCard BuildStoreCardTemplate(RectTransform parent)
        {
            var root = Panelled(parent, "StoreCardTemplate", Panel, out RectTransform inner);
            root.sizeDelta = new Vector2(420f, 344f);
            root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0.5f, 0.5f);

            var card = root.gameObject.AddComponent<StoreItemCard>();
            card.frame = root.GetComponent<Image>();

            var accent = Block(inner, "AccentBar", Accent);
            Span(accent.rectTransform, 0.955f, 0.985f, 16f, 16f);
            card.accentBar = accent;

            // The card carries four things and no more: what it is, whether you have it,
            // one line of prose and one line of numbers. It held twice that at first and
            // the effect was that nobody read any of it, so the bands below are generous
            // on purpose -- the whitespace is the feature.
            var title = Label(inner, "Title", "ITEM", 28, TextAlignmentOptions.MidlineLeft, Ink);
            Span(title.rectTransform, 0.80f, 0.95f, 16f, 130f);
            title.characterSpacing = 3f;
            Autosize(title, 14f, 28f);
            card.titleText = title;

            var state = Label(inner, "State", "", 17, TextAlignmentOptions.MidlineRight, Accent);
            Span(state.rectTransform, 0.80f, 0.95f, 250f, 16f);
            state.characterSpacing = 3f;
            Autosize(state, 9f, 17f);
            card.stateText = state;

            // The numbers sit directly under the name and above the prose, because they
            // are what a player is comparing one card against another with.
            var stats = Label(inner, "Stats", "", 22, TextAlignmentOptions.MidlineLeft, Ink);
            Span(stats.rectTransform, 0.62f, 0.78f, 16f, 16f);
            stats.overflowMode = TextOverflowModes.Ellipsis;
            Autosize(stats, 11f, 22f);
            card.statsText = stats;

            var description = Label(inner, "Description", "", 18,
                                    TextAlignmentOptions.TopLeft, InkDim);
            Span(description.rectTransform, 0.38f, 0.58f, 16f, 16f);
            description.textWrappingMode = TextWrappingModes.Normal;
            description.overflowMode = TextOverflowModes.Ellipsis;
            Autosize(description, 10f, 18f);
            card.descriptionText = description;

            // Five pips, because five is the upgrade cap on everything that has one. The
            // card hides the ones past an item's own maximum, and all of them for the
            // health upgrade, which has none.
            var pips = new Image[5];

            for (int i = 0; i < pips.Length; i++)
            {
                var pip = Block(inner, $"Pip{i + 1}", new Color(1f, 1f, 1f, 0.12f));
                var rect = pip.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(30f, 8f);
                rect.anchoredPosition = new Vector2(16f + i * 36f, 106f);

                pips[i] = pip;
            }

            card.upgradePips = pips;

            var buy = MakeButton(inner, "Primary", "BUY", PanelLift, Accent);
            var buyRect = (RectTransform)buy.transform;
            buyRect.anchorMin = buyRect.anchorMax = new Vector2(0f, 0f);
            buyRect.pivot = new Vector2(0f, 0f);
            buyRect.sizeDelta = new Vector2(186f, 58f);
            buyRect.anchoredPosition = new Vector2(16f, 20f);

            // Silent, because StorePanel plays the outcome: a purchase note when coins
            // change hands and a refusal when they do not. A click underneath would be
            // two sounds saying different things on the same frame.
            buy.GetComponent<UIButtonSound>().voice = UIButtonSound.Voice.Silent;

            card.primaryButton = buy;
            card.primaryLabelText = buy.GetComponentInChildren<TMP_Text>();

            var upgrade = MakeButton(inner, "Upgrade", "UPGRADE", PanelLift, Border);
            var upgradeRect = (RectTransform)upgrade.transform;
            upgradeRect.anchorMin = upgradeRect.anchorMax = new Vector2(1f, 0f);
            upgradeRect.pivot = new Vector2(1f, 0f);
            upgradeRect.sizeDelta = new Vector2(186f, 58f);
            upgradeRect.anchoredPosition = new Vector2(-16f, 20f);

            upgrade.GetComponent<UIButtonSound>().voice = UIButtonSound.Voice.Silent;

            card.upgradeButton = upgrade;
            card.upgradeLabelText = upgrade.GetComponentInChildren<TMP_Text>();

            root.gameObject.SetActive(false);
            return card;
        }

        /// <summary>
        /// One level tile, built once and left switched off.
        ///
        /// Parented to the panel rather than to the grid, for the same reason the arena
        /// card template is: a template inside the grid would be counted as a cell, and
        /// level one would sit in the second slot behind an invisible hole.
        /// </summary>
        static LevelButton BuildLevelTileTemplate(RectTransform parent)
        {
            var root = Panelled(parent, "LevelTileTemplate", Panel, out RectTransform inner);
            root.sizeDelta = new Vector2(300f, 216f);
            root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0.5f, 0.5f);

            var tile = root.gameObject.AddComponent<LevelButton>();

            var button = root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            tile.button = button;

            // A tile starts a level, so it gets the two-note launch rather than a click.
            root.gameObject.AddComponent<UIButtonSound>().voice = UIButtonSound.Voice.Launch;

            tile.frame = root.GetComponent<Image>();
            tile.frame.raycastTarget = true;
            tile.face = inner.GetComponent<Image>();

            var number = Label(inner, "Number", "01", 46,
                               TextAlignmentOptions.MidlineLeft, Accent);
            Span(number.rectTransform, 0.60f, 0.94f, 18f, 18f);
            Autosize(number, 22f, 46f);
            tile.numberText = number;

            var name = Label(inner, "Name", "LEVEL", 22,
                             TextAlignmentOptions.MidlineLeft, Ink);
            Span(name.rectTransform, 0.44f, 0.62f, 18f, 18f);
            name.characterSpacing = 3f;
            Autosize(name, 11f, 22f);
            tile.nameText = name;

            var detail = Label(inner, "Detail", "", 17,
                               TextAlignmentOptions.MidlineLeft, InkDim);
            Span(detail.rectTransform, 0.28f, 0.44f, 18f, 18f);
            Autosize(detail, 9f, 17f);
            tile.detailText = detail;

            // Three plates on the diagonal, the same shape the results screen uses, so
            // the stars a tile shows are visibly the stars the level awarded.
            var stars = new Image[3];

            for (int i = 0; i < stars.Length; i++)
            {
                var star = Block(inner, $"Star{i + 1}", new Color(1f, 1f, 1f, 0.10f));
                var rect = star.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(26f, 26f);
                rect.anchoredPosition = new Vector2(30f + i * 42f, 32f);
                rect.localRotation = Quaternion.Euler(0f, 0f, 45f);

                stars[i] = star;
            }

            tile.stars = stars;

            var locked = Label(inner, "Locked", "LOCKED", 18,
                               TextAlignmentOptions.MidlineLeft, InkDim);
            Span(locked.rectTransform, 0.06f, 0.24f, 18f, 18f);
            locked.characterSpacing = 6f;
            Autosize(locked, 10f, 18f);
            tile.lockText = locked;

            root.gameObject.SetActive(false);
            return tile;
        }

        /// <summary>
        /// A button that can actually be clicked.
        ///
        /// The raycastTarget line is the whole reason this helper exists. Every block
        /// this file draws has it switched off, because most of them are decoration; miss
        /// the exception on a Button's own graphic and it is inert and silent -- it
        /// highlights nothing and receives nothing, and looks exactly like a button whose
        /// handler is broken. That is what Exit Game did. Routing every button through
        /// one place is what stops it happening again.
        ///
        /// The face is left white so Button's colour tint, which multiplies, produces the
        /// state colours exactly rather than a darkened version of them.
        /// </summary>
        /// <summary>
        /// One button in the dashboard's nav row.
        ///
        /// Goes through <see cref="MakeButton"/> like everything clickable in this file --
        /// everything else the builder draws is raycastTarget = false, so a Button built by
        /// hand is inert *silently*: it highlights nothing, receives nothing, and looks
        /// exactly like a button whose handler is broken. That is what Exit Game once did.
        /// </summary>
        static Button NavButton(RectTransform parent, string name, string caption, Color accent)
        {
            var button = MakeButton(parent, name, caption, PanelLift, accent);

            // A layout group sizes its children, so the size goes here rather than on the
            // rect -- writing sizeDelta on a child of a layout group is the same mistake as
            // writing anchoredPosition on a child of a grid.
            var element = button.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 300f;
            element.minWidth = 180f;

            return button;
        }

        static Button MakeButton(RectTransform parent, string name, string caption,
                                 Color normal, Color hover)
        {
            var outer = Block(parent, name, Border);

            // <b>The hover lights the edge, not the face.</b>
            //
            // This used to tint the face, and the face went amber on hover while the
            // label stayed off-white -- 1.6:1, which is a caption that vanishes at the
            // exact moment the pointer is on the thing it names. The label cannot follow
            // the fill, because Unity's ColorBlock tints one graphic and the label is a
            // different one; so the fill is the thing that has to hold still.
            //
            // Keeping the face at a constant dark and tinting the border instead means
            // one label colour is correct in every state -- normal, hover, pressed and
            // disabled -- and it costs nothing, because the border is already a separate
            // Image sitting behind a three-pixel inset. It also reads better: an edge
            // that lights up is a cleaner signal on a dark interface than a slab that
            // changes colour.
            var face = Block(outer.rectTransform, "Face", normal);
            face.rectTransform.anchorMin = Vector2.zero;
            face.rectTransform.anchorMax = Vector2.one;
            face.rectTransform.offsetMin = new Vector2(3f, 3f);
            face.rectTransform.offsetMax = new Vector2(-3f, -3f);
            face.raycastTarget = true;

            var button = outer.gameObject.AddComponent<Button>();

            // The border is what gets tinted. Only the face is a raycast target, and it
            // is a child of this object, so the pointer still reaches the Button exactly
            // as before -- targetGraphic decides what changes colour, never what is hit.
            button.targetGraphic = outer;

            var colors = button.colors;
            colors.normalColor = Border;
            colors.highlightedColor = hover;
            colors.selectedColor = Border;
            colors.pressedColor = hover * 0.8f;
            colors.disabledColor = Border * 0.6f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var label = Label(face.rectTransform, "Label", caption, 24,
                              TextAlignmentOptions.Center, Ink);
            Stretch(label.rectTransform);
            label.characterSpacing = 6f;
            Autosize(label, 12f, 24f);

            // Added here rather than at each call site, for the same reason raycastTarget
            // is: a button that exists should sound like one without anybody remembering.
            button.gameObject.AddComponent<UIButtonSound>();

            return button;
        }

        // ==================================================================
        // Small UI helpers, pixel-flat by design
        // ==================================================================

        /// <summary>A bordered panel: an outer block and an inset inner one.</summary>
        static RectTransform Panelled(RectTransform parent, string name, Color fill,
                                      out RectTransform inner)
        {
            var outer = Block(parent, name, Border);

            var innerImage = Block((RectTransform)outer.transform, "Inner", fill);
            inner = innerImage.rectTransform;
            inner.anchorMin = Vector2.zero;
            inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(3f, 3f);
            inner.offsetMax = new Vector2(-3f, -3f);

            return outer.rectTransform;
        }

        static Image Block(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            image.sprite = Flat();
            image.type = Image.Type.Simple;

            return image;
        }

        static TMP_Text Label(RectTransform parent, string name, string content, float size,
                              TextAlignmentOptions align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.alignment = align;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;

            return text;
        }

        /// <summary>
        /// A one-pixel white sprite with no border and point filtering.
        ///
        /// Deliberately not Unity's built-in UISprite, which the HUD uses: that one is
        /// nine-sliced with rounded corners, and every panel on this screen is supposed
        /// to have hard ones.
        /// </summary>
        static Sprite Flat()
        {
            if (_flat != null) return _flat;

            string path = "Assets/FPSKit_Generated/UIFlat.png";
            _flat = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (_flat != null) return _flat;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var pixels = new Color32[4];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);

            texture.SetPixels32(pixels);
            texture.Apply();

            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            _flat = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            return _flat;
        }

        /// <summary>
        /// Lets a label shrink to fit its box rather than losing the end of the line.
        /// Truncate stays on as the last resort, for a string no size would fit.
        /// </summary>
        static void Autosize(TMP_Text text, float min, float max)
        {
            text.enableAutoSizing = true;
            text.fontSizeMin = min;
            text.fontSizeMax = max;
        }

        /// <summary>
        /// Anchors a rect to a horizontal band of its parent, from one fraction of the
        /// height to another, inset left and right by a margin in pixels.
        /// </summary>
        static void Span(RectTransform rect, float bottom, float top, float leftInset, float rightInset)
        {
            rect.anchorMin = new Vector2(0f, bottom);
            rect.anchorMax = new Vector2(1f, top);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(leftInset, 0f);
            rect.offsetMax = new Vector2(-rightInset, 0f);
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem));

#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        // ==================================================================

        /// <summary>
        /// Puts the dashboard at index 0 so the game boots into it, keeping every arena
        /// registered behind it.
        /// </summary>
        static void RegisterAsFirstScene(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/FPSKit_Generated"))
                AssetDatabase.CreateFolder("Assets", "FPSKit_Generated");

            if (!AssetDatabase.IsValidFolder("Assets/FPSKit_Generated/Scenes"))
                AssetDatabase.CreateFolder("Assets/FPSKit_Generated", "Scenes");

            if (!AssetDatabase.IsValidFolder(PreviewFolder))
                AssetDatabase.CreateFolder("Assets/FPSKit_Generated", "Previews");
        }

        static string SafeName(string name) => name.Replace(" ", "");
    }
}
#endif
