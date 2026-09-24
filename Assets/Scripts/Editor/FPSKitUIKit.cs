#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The UI kit's editor side: FPSKit &gt; UI Kit.
    ///
    ///   Import Fonts And Icons  -- makes TMP font assets from the Barlow TTFs, sets the icon
    ///                              PNGs up as sprites, and wires both into the theme asset,
    ///                              creating that asset from its field defaults if missing.
    ///   Build Component Gallery -- builds UIKitGallery.unity: every component, in every
    ///                              state, on one screen.
    ///
    /// <b>The theme is created, never reset.</b> Its colours are the thing a designer tunes,
    /// so this only ever writes the references it owns -- fonts, outline materials, icons --
    /// and leaves every value alone. The field initialisers on <see cref="UITheme"/> are the
    /// spec; the Inspector's Reset restores them.
    ///
    /// <b>Font assets are made once and then reused</b>, for the reason the builder reuses
    /// materials: deleting and recreating an asset gives it a new GUID and silently unhooks
    /// every scene that pointed at the old one. Rebuild Font Assets is the deliberate way to
    /// start them over.
    /// </summary>
    public static class FPSKitUIKit
    {
        public const string FontFolder = "Assets/UI/Fonts";
        public const string IconFolder = "Assets/UI/Icons";
        public const string ThemePath = "Assets/UI/Resources/UITheme.asset";
        public const string FontAssetFolder = "Assets/FPSKit_Generated/UI/Fonts";
        public const string GalleryPath = "Assets/FPSKit_Generated/Scenes/UIKitGallery.unity";

        /// <summary>
        /// Glyphs baked into each atlas up front. The assets stay dynamic for anything else,
        /// but a WebGL player adding glyphs at runtime is a hitch on the first frame a new
        /// character appears, so everything the interface is known to print is here already.
        /// </summary>
        const string Prepopulate =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "·×—–…‘’“”•°←→↑↓ ";

        // ==================================================================
        // Menu.
        // ==================================================================

        [MenuItem("FPSKit/UI Kit/Import Fonts And Icons", priority = 300)]
        public static void ImportFontsAndIcons() => Import(rebuildFonts: false);

        [MenuItem("FPSKit/UI Kit/Rebuild Font Assets", priority = 301)]
        static void RebuildFonts()
        {
            if (!EditorUtility.DisplayDialog("Rebuild font assets",
                    "This deletes and recreates the TMP font assets. Anything that referenced the old ones loses its font until its builder runs again.",
                    "Rebuild", "Cancel")) return;
            Import(rebuildFonts: true);
        }

        [MenuItem("FPSKit/UI Kit/Build Component Gallery", priority = 320)]
        public static void BuildGalleryMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildGallery();
        }

        // ==================================================================
        // Import.
        // ==================================================================

        public static UITheme Import(bool rebuildFonts)
        {
            AssetDatabase.Refresh();
            ImportIcons();

            var heading = FontAsset("BarlowCondensed-SemiBold", rebuildFonts);
            var body = FontAsset("Barlow-Regular", rebuildFonts);
            var strong = FontAsset("Barlow-Medium", rebuildFonts);

            var theme = LoadOrCreateTheme();
            theme.headingFont = heading;
            theme.bodyFont = body;
            theme.bodyStrongFont = strong;
            theme.hudHeadingMaterial = OutlineMaterial(heading, theme);
            theme.hudBodyMaterial = OutlineMaterial(body, theme);
            theme.icons = LoadIcons();
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();

            Debug.Log($"[UI Kit] theme at {ThemePath}: fonts {Name(heading)}, {Name(body)}, {Name(strong)}; {theme.icons.Length} icons.");
            return theme;
        }

        static string Name(Object o) => o != null ? o.name : "MISSING";

        static void ImportIcons()
        {
            foreach (var path in Directory.GetFiles(IconFolder, "*.png"))
            {
                var p = path.Replace('\\', '/');
                if (AssetImporter.GetAtPath(p) is not TextureImporter ti) continue;
                bool dirty = ti.textureType != TextureImporterType.Sprite || ti.spriteImportMode != SpriteImportMode.Single ||
                             !ti.mipmapEnabled || !ti.alphaIsTransparency || ti.filterMode != FilterMode.Trilinear ||
                             ti.maxTextureSize != 64 || ti.textureCompression != TextureImporterCompression.Uncompressed;
                if (!dirty) continue;
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                // Mipmapped, because a 64px icon drawn at 20 without them aliases into a
                // shimmer -- the opposite of a clean line icon.
                ti.mipmapEnabled = true;
                ti.alphaIsTransparency = true;
                ti.filterMode = FilterMode.Trilinear;
                ti.maxTextureSize = 64;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
        }

        static UITheme.Icon[] LoadIcons()
        {
            return Directory.GetFiles(IconFolder, "*.png")
                .Select(p => p.Replace('\\', '/'))
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new UITheme.Icon
                {
                    id = Path.GetFileNameWithoutExtension(p),
                    sprite = AssetDatabase.LoadAssetAtPath<Sprite>(p),
                })
                .Where(i => i.sprite != null)
                .ToArray();
        }

        static UITheme LoadOrCreateTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<UITheme>(ThemePath);
            if (theme != null) return theme;
            Directory.CreateDirectory(Path.GetDirectoryName(ThemePath)!);
            theme = ScriptableObject.CreateInstance<UITheme>();
            AssetDatabase.CreateAsset(theme, ThemePath);
            Debug.Log($"[UI Kit] created {ThemePath} from the defaults in UITheme.cs.");
            return theme;
        }

        static TMP_FontAsset FontAsset(string ttf, bool rebuild)
        {
            string assetPath = $"{FontAssetFolder}/{ttf} SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null && !rebuild) return existing;
            if (existing != null) AssetDatabase.DeleteAsset(assetPath);

            var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontFolder}/{ttf}.ttf");
            if (font == null)
            {
                Debug.LogError($"[UI Kit] {FontFolder}/{ttf}.ttf is missing.");
                return null;
            }

            // 90pt samples with 9px padding: enough distance field for the thin outline on
            // HUD text without the atlas going soft at display sizes.
            var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                                                   AtlasPopulationMode.Dynamic, true);
            fa.name = $"{ttf} SDF";
            fa.TryAddCharacters(Prepopulate, out string missing);
            if (!string.IsNullOrEmpty(missing))
                Debug.LogWarning($"[UI Kit] {ttf} has no glyph for: {missing}");

            Directory.CreateDirectory(FontAssetFolder);
            AssetDatabase.CreateAsset(fa, assetPath);
            for (int i = 0; i < fa.atlasTextures.Length; i++)
            {
                var tex = fa.atlasTextures[i];
                if (tex == null) continue;
                tex.name = $"{ttf} Atlas {i}";
                AssetDatabase.AddObjectToAsset(tex, fa);
            }
            fa.material.name = $"{ttf} Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            return fa;
        }

        /// <summary>
        /// The font's material with a thin dark outline, for text drawn over the arena. The
        /// face is dilated by the same amount the outline eats into it, so outlined text is
        /// the same weight as plain text rather than a thinner one with a border.
        /// </summary>
        static Material OutlineMaterial(TMP_FontAsset fa, UITheme theme)
        {
            if (fa == null) return null;
            string path = $"{FontAssetFolder}/{fa.name} HUD Outline.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(fa.material) { name = $"{fa.name} HUD Outline" };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = fa.material.shader;
            mat.SetTexture(ShaderUtilities.ID_MainTex, fa.atlasTexture);
            mat.SetFloat(ShaderUtilities.ID_GradientScale, fa.atlasPadding + 1);
            mat.SetFloat(ShaderUtilities.ID_TextureWidth, fa.atlasWidth);
            mat.SetFloat(ShaderUtilities.ID_TextureHeight, fa.atlasHeight);
            mat.EnableKeyword("OUTLINE_ON");
            mat.SetColor(ShaderUtilities.ID_OutlineColor, theme.hudTextOutline);
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.14f);
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.14f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ==================================================================
        // Gallery.
        // ==================================================================

        public static void BuildGallery()
        {
            var theme = Import(rebuildFonts: false);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Camera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = theme.background;
            cam.orthographic = true;
            cam.cullingMask = 0;

            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif

            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.layer = 5;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Same scaler as the dashboard, so what is judged here is what the menus will get.
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)canvasGo.transform;

            var bg = UIKit.Panel(root, "Background", UIKit.PanelTone.Background, theme);
            UIKit.Fill(bg.rectTransform);

            var gallery = canvasGo.AddComponent<UIKitGallery>();

            // Title bar.
            var bar = UIKit.Panel(root, "TitleBar", UIKit.PanelTone.Panel, theme);
            bar.borderColor = new Color(0, 0, 0, 0);
            bar.stripeSide = FlatRect.Side.Bottom;
            bar.stripeColor = theme.border;
            bar.stripePixels = theme.borderPixels;
            var barRt = bar.rectTransform;
            barRt.anchorMin = new Vector2(0, 1); barRt.anchorMax = new Vector2(1, 1);
            barRt.pivot = new Vector2(0.5f, 1); barRt.sizeDelta = new Vector2(0, 72);
            barRt.anchoredPosition = Vector2.zero;
            UIKit.Row(bar, 18f, new RectOffset(40, 40, 0, 0), TextAnchor.MiddleLeft);
            var logoStripe = UIKit.Rect(bar.transform, "LogoStripe");
            var ls = logoStripe.gameObject.AddComponent<FlatRect>(); ls.raycastTarget = false; ls.color = theme.accent;
            UIKit.Size(ls, 6f, 32f);
            UIKit.Text(bar.transform, "Title", "Mini FPS", UIKit.TextRole.Title, theme);
            var sub = UIKit.Text(bar.transform, "Subtitle", "UI kit  ·  component gallery", UIKit.TextRole.Label, theme);
            UIKit.Size(sub, flexWidth: 1f);
            var coins = UIKit.Rect(bar.transform, "Coins");
            UIKit.Row(coins, 8f, null, TextAnchor.MiddleRight);
            UIKit.Icon(coins, "Icon", "coin", 22f, theme.accent, theme);
            var coinText = UIKit.Text(coins, "Amount", "0", UIKit.TextRole.Number, theme);
            coinText.color = theme.accent;
            gallery.coinLabel = coinText;

            // Three columns of sections.
            var cols = UIKit.Rect(root, "Columns");
            UIKit.Fill(cols, 40, 96, 40, 32);
            var colRow = UIKit.Row(cols, 24f, null, TextAnchor.UpperLeft);
            colRow.childForceExpandHeight = true;
            var a = Column(cols, "A", 1f); var b = Column(cols, "B", 1f); var c = Column(cols, "C", 0.9f);

            BuildTypeAndColour(a, theme);
            BuildButtons(a, theme);
            gallery.pinnedTooltipOwner = BuildIconButtons(b, theme);
            BuildTabs(b, theme);
            gallery.movingBar = BuildProgress(b, theme, gallery);
            BuildHudSample(c, theme);
            gallery.movingStat = BuildStats(c, theme);
            gallery.toasts = BuildToasts(c, theme);

            Directory.CreateDirectory(Path.GetDirectoryName(GalleryPath)!);
            EditorSceneManager.SaveScene(scene, GalleryPath);
            Debug.Log($"[UI Kit] gallery built at {GalleryPath}");
        }

        static RectTransform Column(RectTransform parent, string name, float weight)
        {
            var col = UIKit.Rect(parent, name);
            var g = UIKit.Column(col, 24f);
            g.childForceExpandWidth = true;
            UIKit.Size(col, 0f, flexWidth: weight);
            return col;
        }

        /// <summary>
        /// Toasts in a section of their own. In the game they sit in a corner over whatever
        /// is there; here that corner would be another component.
        /// </summary>
        static UIToastStack BuildToasts(RectTransform col, UITheme t)
        {
            UIKit.Section(col, "Toasts", "Toasts", out var body, UIKit.PanelTone.Panel, t);
            var area = UIKit.Rect(body, "Area");
            UIKit.Size(area, height: 240f);
            var stack = UIKit.ToastStack(area, "Stack", t);
            var rt = (RectTransform)stack.transform;
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return stack;
        }

        static void BuildTypeAndColour(RectTransform col, UITheme t)
        {
            UIKit.Section(col, "Type", "Type and colour", out var body, UIKit.PanelTone.Panel, t);

            var swatches = UIKit.Rect(body, "Swatches");
            var grid = swatches.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(64, 44);
            grid.spacing = new Vector2(8, 8);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 7;
            UIKit.Size(swatches, height: 96f);
            var colours = new (Color c, bool light)[]
            {
                (t.background, false), (t.panel, false), (t.panelRaised, false), (t.border, false),
                (t.textPrimary, true), (t.textSecondary, true), (t.textDisabled, false),
                (t.accent, true), (t.danger, true), (t.success, true), (t.info, true),
            };
            foreach (var (colour, _) in colours)
            {
                var s = UIKit.Panel(swatches, "Swatch", UIKit.PanelTone.Panel, t);
                s.color = colour;
                s.borderColor = t.border;
            }
            var stripes = UIKit.Rect(body, "ArenaStripes");
            UIKit.Row(stripes, 8f);
            UIKit.Size(stripes, height: 6f);
            foreach (var st in t.arenaStripes)
            {
                var s = UIKit.Rect(stripes, st.scene);
                var fr = s.gameObject.AddComponent<FlatRect>(); fr.raycastTarget = false; fr.color = st.color;
                UIKit.Size(fr, height: 3f, flexWidth: 1f);
            }

            UIKit.Text(body, "Display", "Mini FPS", UIKit.TextRole.Display, t);
            UIKit.Text(body, "Title", "Snowbound Station", UIKit.TextRole.Title, t);
            UIKit.Text(body, "Heading", "Current objective", UIKit.TextRole.Heading, t);
            UIKit.Text(body, "Label", "Enemies  ·  Time  ·  Reward", UIKit.TextRole.Label, t);
            UIKit.Text(body, "Body", "Clear the yard before the clock runs out. Barlow Regular, body copy.", UIKit.TextRole.Body, t);
            var nums = UIKit.Rect(body, "Numbers");
            UIKit.Row(nums, 24f);
            UIKit.Text(nums, "N1", "111", UIKit.TextRole.Number, t).fontSize = 30;
            UIKit.Text(nums, "N2", "888", UIKit.TextRole.Number, t).fontSize = 30;
            UIKit.Text(nums, "N3", "04:32", UIKit.TextRole.Number, t).fontSize = 30;
            UIKit.Text(nums, "Note", "tabular figures: 111 and 888 are the same width", UIKit.TextRole.Caption, t);
        }

        static void BuildButtons(RectTransform col, UITheme t)
        {
            UIKit.Section(col, "Buttons", "Buttons", out var body, UIKit.PanelTone.Panel, t);

            var header = UIKit.Rect(body, "States");
            UIKit.Row(header, 12f);
            foreach (var s in new[] { "Normal", "Hover", "Pressed", "Disabled" })
            {
                var l = UIKit.Text(header, s, s, UIKit.TextRole.Label, t);
                l.fontSize = t.sizeCaption;
                UIKit.Size(l, flexWidth: 1f);
            }

            foreach (var (variant, caption, icon) in new[]
                     {
                         (FlatButton.Variant.Primary, "Play", "player-play"),
                         (FlatButton.Variant.Secondary, "Store", "shopping-cart"),
                         (FlatButton.Variant.Quiet, "Back", "arrow-left"),
                     })
            {
                var row = UIKit.Rect(body, variant.ToString());
                UIKit.Row(row, 12f);
                foreach (FlatButton.Look look in Enum.GetValues(typeof(FlatButton.Look)))
                {
                    var btn = UIKit.Button(row, $"{variant}_{look}", caption, variant, icon, t);
                    btn.holdLook = true;
                    btn.heldLook = look;
                    btn.interactable = look != FlatButton.Look.Disabled;
                    UIKit.Size(btn, flexWidth: 1f);
                }
            }

            UIKit.Text(body, "LiveLabel", "Live -- hover and press these", UIKit.TextRole.Caption, t);
            var live = UIKit.Rect(body, "Live");
            UIKit.Row(live, 12f);
            UIKit.Button(live, "LivePrimary", "Play level 4", FlatButton.Variant.Primary, "player-play", t);
            UIKit.Button(live, "LiveSecondary", "Replay", FlatButton.Variant.Secondary, "refresh", t);
            UIKit.Button(live, "LiveQuiet", "Dashboard", FlatButton.Variant.Quiet, null, t);
        }

        static FlatButton BuildIconButtons(RectTransform col, UITheme t)
        {
            UIKit.Section(col, "IconButtons", "Icon buttons and icons", out var body, UIKit.PanelTone.Panel, t);

            var row = UIKit.Rect(body, "Row");
            UIKit.Row(row, 8f);
            UIKit.Size(row, height: 44f + 40f); // room for the pinned tooltip above
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.LowerLeft;
            var settings = UIKit.IconButton(row, "Settings", "settings", "Settings", UIKit.ControlHeight, t);
            UIKit.IconButton(row, "HowToPlay", "help-circle", "How to play", UIKit.ControlHeight, t);
            UIKit.IconButton(row, "Achievements", "trophy", "Achievements", UIKit.ControlHeight, t);
            UIKit.IconButton(row, "Stats", "chart-bar", "Career", UIKit.ControlHeight, t);
            var exit = UIKit.IconButton(row, "Exit", "power", "Exit game", UIKit.ControlHeight, t);
            exit.interactable = false;

            var icons = UIKit.Rect(body, "IconSet");
            var grid = icons.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(28, 28);
            grid.spacing = new Vector2(16, 14);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 13;
            int rows = Mathf.CeilToInt(t.icons.Length / 13f);
            UIKit.Size(icons, height: rows * 28 + (rows - 1) * 14);
            foreach (var i in t.icons)
                UIKit.Icon(icons, i.id, i.id, 24f, t.textPrimary, t);
            return settings;
        }

        static void BuildTabs(RectTransform col, UITheme t)
        {
            UIKit.Section(col, "Tabs", "Tab bar", out var body, UIKit.PanelTone.Panel, t);
            UIKit.TabBar(body, "MainTabs", new[] { "Play", "Loadout", "Achievements", "Store" }, t);
            var sub = UIKit.TabBar(body, "Filter", new[] { "All", "Combat", "Progression", "Campaign" }, t);
            sub.Selected = 1;
        }

        static UIProgressBar BuildProgress(RectTransform col, UITheme t, UIKitGallery gallery)
        {
            UIKit.Section(col, "Progress", "Progress bars", out var body, UIKit.PanelTone.Panel, t);

            UIKit.Text(body, "L1", "Achievements", UIKit.TextRole.Label, t);
            var ach = UIKit.ProgressBar(body, "Achievements", t.accent, 6f, true, t);
            gallery.achievementsBar = ach;

            UIKit.Text(body, "L2", "Snowbound Station  ·  levels", UIKit.TextRole.Label, t);
            var lvl = UIKit.ProgressBar(body, "Levels", t.textPrimary, 6f, true, t);
            gallery.levelsBar = lvl;

            UIKit.Text(body, "L3", "Animated", UIKit.TextRole.Label, t);
            var moving = UIKit.ProgressBar(body, "Moving", t.info, 6f, true, t);
            moving.Set(12, 20, true);
            return moving;
        }

        static UIStatBar BuildStats(RectTransform col, UITheme t)
        {
            UIKit.Section(col, "Stats", "Stat bars", out var body, UIKit.PanelTone.Panel, t);
            UIKit.StatBar(body, "Health", "heart", t.success, false, t).Set(100, 100, true);
            var shield = UIKit.StatBar(body, "Shield", "shield", t.info, false, t);
            shield.lowFraction = 0f;
            shield.Set(60, 100, true);
            UIKit.StatBar(body, "HealthLow", "heart", t.success, false, t).Set(18, 100, true);
            var moving = UIKit.StatBar(body, "Moving", "heart", t.success, false, t);
            moving.Set(70, 100, true);
            return moving;
        }

        /// <summary>
        /// The HUD parts over a real frame of an arena, because a HUD judged on a flat grey
        /// background is judged on the one background it will never have.
        /// </summary>
        static void BuildHudSample(RectTransform col, UITheme t)
        {
            UIKit.Section(col, "Hud", "Over the arena", out var body, UIKit.PanelTone.Panel, t);
            var frame = UIKit.Rect(body, "Frame");
            UIKit.Size(frame, height: 290f);
            var shot = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/FPSKit_Generated/Previews/SnowboundStation.png");
            var raw = frame.gameObject.AddComponent<RawImage>();
            raw.texture = shot;
            raw.raycastTarget = false;
            raw.color = shot != null ? Color.white : t.panelRaised;

            var panel = UIKit.Panel(frame, "Vitals", UIKit.PanelTone.Hud, t);
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0, 0);
            prt.anchoredPosition = new Vector2(16, 16);
            prt.sizeDelta = new Vector2(250, 80);
            UIKit.Column(panel, 8f, new RectOffset(14, 14, 12, 12));
            UIKit.StatBar(panel.transform, "Health", "heart", t.success, true, t).Set(84, 100, true);
            var sh = UIKit.StatBar(panel.transform, "Shield", "shield", t.info, true, t);
            sh.lowFraction = 0f;
            sh.Set(40, 100, true);

            var ammo = UIKit.Rect(frame, "Ammo");
            ammo.anchorMin = ammo.anchorMax = ammo.pivot = new Vector2(1, 0);
            ammo.anchoredPosition = new Vector2(-20, 14);
            ammo.sizeDelta = new Vector2(150, 60);
            UIKit.Row(ammo, 10f, null, TextAnchor.LowerRight);
            UIKit.Icon(ammo, "Icon", "magazine", 24f, t.textPrimary, t);
            var mag = UIKit.Text(ammo, "Mag", "24", UIKit.TextRole.Number, t, hud: true);
            mag.fontSize = 44;
            var reserve = UIKit.Text(ammo, "Reserve", "/ 90", UIKit.TextRole.Number, t, hud: true);
            reserve.fontSize = 24;
            reserve.color = t.textSecondary;

            var obj = UIKit.Panel(frame, "Objective", UIKit.PanelTone.Hud, t);
            obj.stripeSide = FlatRect.Side.Left;
            obj.stripeColor = t.accent;
            obj.stripePixels = t.stripePixels;
            var ort = obj.rectTransform;
            ort.anchorMin = ort.anchorMax = ort.pivot = new Vector2(0, 1);
            ort.anchoredPosition = new Vector2(16, -16);
            ort.sizeDelta = new Vector2(250, 62);
            UIKit.Column(obj, 2f, new RectOffset(16, 12, 10, 10));
            var oh = UIKit.Text(obj.transform, "Heading", "Objective", UIKit.TextRole.Label, t, hud: true);
            oh.color = t.accent;
            UIKit.Text(obj.transform, "Text", "Clear the yard  ·  12 / 24", UIKit.TextRole.Body, t, hud: true);

            var clock = UIKit.Text(frame, "Clock", "0:18", UIKit.TextRole.Number, t, hud: true);
            clock.fontSize = 40;
            clock.color = t.danger;
            clock.alignment = TextAlignmentOptions.TopRight;
            var crt = clock.rectTransform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1, 1);
            crt.anchoredPosition = new Vector2(-18, -12);
            crt.sizeDelta = new Vector2(120, 50);
        }

        // ==================================================================
        // Batch check.
        // ==================================================================

        static readonly List<string> _errors = new List<string>();
        static readonly List<string> _failures = new List<string>();
        static double _startedAt;
        static int _phase;
        static string _outDir;

        /// <summary>
        /// Builds the gallery, plays it, and checks what only shows up running: that nothing
        /// logs an error, that every button's face takes a raycast at its centre, that hover
        /// and press reach the button, that the tooltip and toasts appear. Then renders the
        /// canvas at 1920x1080 and 1280x720. Needs a real graphics device:
        ///
        ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitUIKit.VerifyGallery -fpskitOut Build/UIKit
        /// </summary>
        public static void VerifyGallery()
        {
            try
            {
                _outDir = Arg("-fpskitOut") ?? "Build/UIKit";
                BuildGallery();
                EditorSceneManager.OpenScene(GalleryPath, OpenSceneMode.Single);
                _errors.Clear(); _failures.Clear();
                _phase = 0;
                _startedAt = EditorApplication.timeSinceStartup;
                Application.logMessageReceived += OnLog;
                FPSKitPlayMode.SuspendStartScene();
                EditorApplication.update += Tick;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UI Kit] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (type is LogType.Error or LogType.Exception or LogType.Assert)
                _errors.Add(message);
        }

        static void Tick()
        {
            try
            {
                double t = EditorApplication.timeSinceStartup - _startedAt;
                if (t > 120) throw new Exception($"gallery check timed out in phase {_phase}");
                switch (_phase)
                {
                    case 0:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }
                        _startedAt = EditorApplication.timeSinceStartup;
                        _phase = 1;
                        return;
                    case 1:
                        if (t < 1.5) return;
                        CheckRunning();
                        CaptureCanvases(_outDir, 1920, 1080, 1280, 720);
                        _phase = 2;
                        return;
                    case 2:
                        if (!File.Exists(Path.Combine(_outDir, "done.txt"))) return;
                        Finish();
                        return;
                }
            }
            catch (Exception e)
            {
                _failures.Add(e.ToString());
                Finish();
            }
        }

        static void CheckRunning()
        {
            var buttons = Object.FindObjectsByType<FlatButton>(FindObjectsInactive.Exclude);
            if (buttons.Length < 20) _failures.Add($"only {buttons.Length} buttons in the gallery");

            var es = EventSystem.current;
            var hits = new List<RaycastResult>();
            int unreachable = 0;
            foreach (var b in buttons)
            {
                if (b.face == null || !b.face.raycastTarget) { _failures.Add($"{b.name}: face missing or not a raycast target"); continue; }
                var rt = (RectTransform)b.transform;
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
                hits.Clear();
                es.RaycastAll(new PointerEventData(es) { position = screen }, hits);
                if (hits.Count == 0 || hits[0].gameObject.GetComponentInParent<FlatButton>() != b)
                {
                    unreachable++;
                    _failures.Add($"{b.name}: a click at its centre lands on {(hits.Count > 0 ? hits[0].gameObject.name : "nothing")}");
                }
            }

            // Hover and press reach a live button, and leave it again.
            var live = buttons.FirstOrDefault(b => b.name == "LiveSecondary");
            if (live == null) _failures.Add("no live secondary button");
            else
            {
                var e = new PointerEventData(es);
                live.OnPointerEnter(e);
                if (live.CurrentLook != FlatButton.Look.Hover) _failures.Add($"hover gave {live.CurrentLook}");
                live.OnPointerDown(new PointerEventData(es) { button = PointerEventData.InputButton.Left });
                if (live.CurrentLook != FlatButton.Look.Pressed) _failures.Add($"press gave {live.CurrentLook}");
                live.OnPointerUp(new PointerEventData(es) { button = PointerEventData.InputButton.Left });
                live.OnPointerExit(e);
                es.SetSelectedGameObject(null);
                if (live.CurrentLook != FlatButton.Look.Normal) _failures.Add($"leaving gave {live.CurrentLook}");
            }

            // A tab click moves the selection and the amber line with it.
            var bar = Object.FindObjectsByType<UITabBar>(FindObjectsInactive.Exclude).FirstOrDefault(x => x.name == "MainTabs");
            if (bar == null) _failures.Add("no tab bar");
            else
            {
                bar.tabs[2].onClick.Invoke();
                if (bar.Selected != 2 || !bar.tabs[2].Selected || bar.tabs[0].Selected) _failures.Add("tab click did not move the selection");
                bar.Select(0, false);
            }

            var tip = Object.FindAnyObjectByType<UITooltipView>();
            if (tip == null || !tip.gameObject.activeInHierarchy) _failures.Add("the pinned tooltip is not showing");
            int toasts = Object.FindObjectsByType<UIToast>(FindObjectsInactive.Exclude).Length;
            if (toasts != 3) _failures.Add($"{toasts} toasts showing, expected 3");

            var theme = UITheme.Active;
            if (theme.headingFont == null || theme.bodyFont == null) _failures.Add("theme has no fonts");
            if (theme.icons.Length < 30) _failures.Add($"theme has {theme.icons.Length} icons");
            Debug.Log($"[UI Kit] {buttons.Length} buttons, {unreachable} unreachable, {toasts} toasts, {theme.icons.Length} icons");
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            FPSKitPlayMode.RestoreStartScene();
            foreach (var e in _errors) _failures.Add("console error: " + e);
            if (_failures.Count == 0)
            {
                Debug.Log("[UI Kit] gallery check passed; console clean.");
                EditorApplication.Exit(0);
            }
            else
            {
                foreach (var f in _failures) Debug.LogError("[UI Kit] " + f);
                Debug.LogError($"[UI Kit] FAILED: {_failures.Count} problem(s)");
                EditorApplication.Exit(1);
            }
        }

        // ==================================================================
        // Capture.
        // ==================================================================

        /// <summary>
        /// Renders every root canvas in the running game at each given size and writes PNGs.
        /// Needs Play mode. Asynchronous: a canvas lays itself out in its own update, so the
        /// render waits a few frames after each change of size. Writes <c>done.txt</c> last.
        ///
        /// It re-points the canvases at an offscreen camera rather than capturing the Game
        /// view, because the Game view is whatever size its window happens to be -- and the
        /// question is what the layout does at exactly 1920x1080 and 1280x720.
        /// </summary>
        public static void CaptureCanvases(string outDir, params int[] sizes)
        {
            if (!Application.isPlaying) { Debug.LogError("[UI Kit] capture needs Play mode."); return; }
            Directory.CreateDirectory(outDir);
            File.Delete(Path.Combine(outDir, "done.txt"));

            var queue = new Queue<Vector2Int>();
            for (int i = 0; i + 1 < sizes.Length; i += 2) queue.Enqueue(new Vector2Int(sizes[i], sizes[i + 1]));

            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude).Where(c => c.isRootCanvas).ToArray();
            var modes = canvases.Select(c => c.renderMode).ToArray();
            var camGo = new GameObject("UIKitCaptureCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = UITheme.Active.background;
            cam.cullingMask = 1 << 5;
            cam.orthographic = true;
            cam.enabled = false;

            RenderTexture rt = null;
            Vector2Int current = default;
            int wait = -1;

            void Step()
            {
                if (wait < 0)
                {
                    if (queue.Count == 0)
                    {
                        for (int i = 0; i < canvases.Length; i++) if (canvases[i] != null) canvases[i].renderMode = modes[i];
                        Object.Destroy(camGo);
                        File.WriteAllText(Path.Combine(outDir, "done.txt"), "ok");
                        EditorApplication.update -= Step;
                        return;
                    }
                    current = queue.Dequeue();
                    if (rt != null) rt.Release();
                    rt = new RenderTexture(current.x, current.y, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                    cam.targetTexture = rt;
                    foreach (var c in canvases)
                    {
                        c.renderMode = RenderMode.ScreenSpaceCamera;
                        c.worldCamera = cam;
                        c.planeDistance = 1f;
                    }
                    wait = 6;
                    return;
                }
                if (--wait > 0) return;
                wait = -1;

                Canvas.ForceUpdateCanvases();
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                {
                    RenderPipeline.SubmitRenderRequest(cam, request);
                    RenderPipeline.SubmitRenderRequest(cam, request);
                }
                else cam.Render();

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var img = new Texture2D(current.x, current.y, TextureFormat.RGB24, false);
                img.ReadPixels(new Rect(0, 0, current.x, current.y), 0, 0);
                img.Apply();
                RenderTexture.active = prev;
                string file = Path.Combine(outDir, $"ui_{current.x}x{current.y}.png");
                File.WriteAllBytes(file, img.EncodeToPNG());
                Object.DestroyImmediate(img);
                Debug.Log($"[UI Kit] wrote {file}");
            }

            EditorApplication.update += Step;
        }
    }
}
#endif
