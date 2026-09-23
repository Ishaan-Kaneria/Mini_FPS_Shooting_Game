#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The surface detail: tiling grayscale albedo and normal maps, generated rather
    /// than imported.
    ///
    /// Every generated arena in this kit was flat-shaded -- one solid colour per
    /// material, no texture anywhere. Under one hard sun that is readable and completely
    /// scaleless: a dune the size of a house and a dune the size of a stadium are the
    /// same smooth wash of orange, and a player walking over the second one cannot tell
    /// they are moving. Detail at a known metre-size is what makes ground read as
    /// ground, and it is most of the difference between "a shape coloured like sand"
    /// and sand.
    ///
    /// Three decisions worth not re-deriving:
    ///
    ///   * <b>The maps are grayscale and the colour stays on the material.</b> Tinting a
    ///     grey detail map with <c>_BaseColor</c> keeps <see cref="LevelTheme"/> in
    ///     charge of what colour an arena is, which is the whole point of the theme
    ///     being an asset. A coloured texture would quietly override it and a theme
    ///     retune would then do nothing.
    ///   * <b>They are regenerated on every build, not cached.</b> The generator is a
    ///     pure function of constants, so the PNG bytes are identical every time and
    ///     git sees no change -- which means there is no "reset the textures" step to
    ///     forget, unlike the theme, roster, level and store generators.
    ///   * <b>They are written as PNGs and imported, not created as Texture2D assets.</b>
    ///     A normal map has to go through the importer to be encoded the way the shader
    ///     unpacks it; a raw Texture2D written straight to a .asset comes back as an
    ///     ordinary colour texture and lights the entire surface inside out.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private const string TextureFolder = AssetFolder + "/Textures";
        private const int TextureSize = 512;

        /// <summary>Regenerated once per build; the key is the file name.</summary>
        private static void EnsureTextureFolder()
        {
            if (!AssetDatabase.IsValidFolder(TextureFolder))
                AssetDatabase.CreateFolder(AssetFolder, "Textures");

            Directory.CreateDirectory(TextureFolder);
        }

        // ==================================================================
        // Tileable noise
        // ==================================================================
        /// <summary>
        /// Value noise on a lattice that wraps at <paramref name="period"/>.
        ///
        /// Wrapping is the whole job. A texture tiled across four hundred metres of
        /// ground shows its seam as a hard line every few metres, and a hard line
        /// repeated in a grid is the single most obvious artefact there is -- far more
        /// visible than having no texture at all.
        /// </summary>
        private static float TileNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = Smooth(x - x0), fy = Smooth(y - y0);

            int Wrap(int v) => ((v % period) + period) % period;

            int xa = Wrap(x0), xb = Wrap(x0 + 1), ya = Wrap(y0), yb = Wrap(y0 + 1);

            float a = Mathf.Lerp(Hash01(xa, ya, 0, seed), Hash01(xb, ya, 0, seed), fx);
            float b = Mathf.Lerp(Hash01(xa, yb, 0, seed), Hash01(xb, yb, 0, seed), fx);

            return Mathf.Lerp(a, b, fy);
        }

        /// <summary>Octaves of <see cref="TileNoise"/>, each wrapping on its own lattice.</summary>
        private static float TileFbm(float u, float v, int basePeriod, int octaves, int seed,
                                     float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, total = 0f;
            int period = basePeriod;

            for (int i = 0; i < octaves; i++)
            {
                sum += TileNoise(u * period, v * period, period, seed + i * 613) * amp;
                total += amp;
                amp *= gain;
                period *= 2;
            }

            return total > 0f ? sum / total : 0f;
        }

        // ==================================================================
        // The maps
        // ==================================================================
        /// <summary>
        /// Builds every detail map this theme needs and returns nothing -- the callers
        /// ask for materials, not textures.
        /// </summary>
        private static void BuildSurfaceTextures()
        {
            EnsureTextureFolder();

            WriteHeightPair("Sand", SandHeight, albedoContrast: 0.42f, normalStrength: 2.6f);
            WriteHeightPair("Rock", RockHeight, albedoContrast: 0.72f, normalStrength: 4.2f);
            WriteHeightPair("Timber", TimberHeight, albedoContrast: 0.55f, normalStrength: 2.2f);
            WriteHeightPair("Adobe", AdobeHeight, albedoContrast: 0.34f, normalStrength: 2.4f);
            WriteHeightPair("Water", WaterHeight, albedoContrast: 0.12f, normalStrength: 1.4f);

            // The four walled-box arenas are built out of these. Until they existed those
            // arenas were flat colour on every surface -- readable and completely
            // scaleless, which is the whole reason a dune field the size of a house and one
            // the size of a stadium look identical without detail.
            WriteHeightPair("Concrete", ConcreteHeight, albedoContrast: 0.30f, normalStrength: 2.0f);
            WriteHeightPair("Metal", MetalHeight, albedoContrast: 0.26f, normalStrength: 1.8f);
            WriteHeightPair("Snow", SnowHeight, albedoContrast: 0.18f, normalStrength: 1.6f);
            WriteHeightPair("Tile", TileHeight, albedoContrast: 0.46f, normalStrength: 3.0f);

            // Ice is the one surface whose detail has to be *almost nothing*. What reads
            // as ice is a flat sheet with a few long cracks in it and no grain at all --
            // give it the pitting that makes concrete read as concrete and it comes back
            // as wet tarmac. The contrast is the lowest in the set for the same reason.
            WriteHeightPair("Ice", IceHeight, albedoContrast: 0.10f, normalStrength: 1.2f);

            WriteFlake();

            // Basalt, lava and smoke, for the volcanic dressing. Written for every arena
            // like the rest -- they are a pure function of constants, so an arena that
            // does not use them costs a few hundred milliseconds and no git churn.
            WriteVolcanicTextures();

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Poured concrete: aggregate under a floated surface, with shrinkage cracks.
        ///
        /// The pinholes matter more than the cracks. A concrete slab reads as concrete
        /// because of a fine, dense pitting that never repeats visibly; cracks are the
        /// thing the eye notices and the thing that tiles badly, so they are kept sparse.
        /// </summary>
        private static float ConcreteHeight(float u, float v)
        {
            float aggregate = TileFbm(u, v, 28, 3, 4401);
            float float_ = TileFbm(u, v, 5, 3, 4402);

            // Pinholes: sharp, small, and only where the noise peaks, so they scatter
            // rather than covering the surface evenly.
            float pits = TileFbm(u, v, 96, 1, 4403);
            pits = Mathf.Max(0f, pits - 0.62f) * 2.6f;

            // One faint crack network, cut rather than added -- a crack is a groove.
            float crack = Mathf.Abs(TileFbm(u, v, 4, 3, 4404) - 0.5f);
            crack = Mathf.Max(0f, 0.06f - crack) * 8f;

            return Mathf.Clamp01(float_ * 0.5f + aggregate * 0.32f - pits * 0.22f - crack * 0.3f + 0.2f);
        }

        /// <summary>
        /// Rolled steel plate: a faint directional grain, dents, and streaked corrosion.
        ///
        /// Anisotropic on purpose. Plate is rolled, so its grain runs one way, and a metal
        /// surface with isotropic noise on it reads as stone painted grey.
        /// </summary>
        private static float MetalHeight(float u, float v)
        {
            // Stretched along u so the grain runs across the plate rather than swirling.
            float grain = TileFbm(u * 0.18f, v * 6f, 8, 3, 5501);

            float dents = TileFbm(u, v, 6, 2, 5502);
            dents = Mathf.Max(0f, dents - 0.58f) * 1.8f;

            float corrosion = TileFbm(u, v, 20, 4, 5503);
            corrosion = Mathf.Max(0f, corrosion - 0.55f) * 1.6f;

            return Mathf.Clamp01(0.55f + grain * 0.18f - dents * 0.16f - corrosion * 0.22f);
        }

        /// <summary>
        /// Wind-packed snow: broad drift relief with a crust, and no sharp detail at all.
        ///
        /// Deliberately the softest map here. Snow has almost no high-frequency structure
        /// -- what reads as snow is the *absence* of it against broad soft relief -- so
        /// sharpening this is what makes generated snow look like pale sand.
        /// </summary>
        /// <summary>
        /// Lake ice: a flat sheet, a long crack system, and a scatter of trapped bubbles.
        ///
        /// The cracks are drawn as distance to a few wandering lines rather than as noise,
        /// because what the eye reads as ice is that the flaws are *long and few*. Noise at
        /// any amplitude gives frosted glass, which is a different material and, on a
        /// forty-metre sheet, an obviously tiling one.
        /// </summary>
        private static float IceHeight(float u, float v)
        {
            float h = 0.5f;

            // Three crack systems at different angles, each a wrapping sine so the sheet
            // tiles. The narrow ones are the ones that read; the wide one is the ridge
            // the others hang off.
            h -= 0.34f * Crack(u * 1f + v * 0.35f + 0.11f * TileFbm(u, v, 3, 2, 7701), 0.010f);
            h -= 0.22f * Crack(v * 1f - u * 0.62f + 0.09f * TileFbm(u, v, 5, 2, 7702), 0.007f);
            h -= 0.14f * Crack(u * 0.45f + v * 0.9f + 0.14f * TileFbm(u, v, 2, 2, 7703), 0.005f);

            // Trapped air. Small, round, and shallow -- they catch the light rather than
            // break the surface.
            float bubbles = TileFbm(u, v, 22, 1, 7704);
            if (bubbles > 0.82f) h += (bubbles - 0.82f) * 1.1f;

            // The faintest possible swell, so the sheet is not mathematically flat.
            h += 0.035f * (TileFbm(u, v, 2, 2, 7705) - 0.5f);

            return Mathf.Clamp01(h);
        }

        /// <summary>
        /// How close a value is to a wrapped zero crossing -- 1 on the line, 0 away from
        /// it. What turns a smooth field into a crack.
        /// </summary>
        private static float Crack(float t, float width)
        {
            float wrapped = t - Mathf.Floor(t);
            float distance = Mathf.Min(wrapped, 1f - wrapped);

            return Mathf.Clamp01(1f - distance / Mathf.Max(0.0005f, width));
        }

        /// <summary>
        /// One snowflake, as a soft round dot with an alpha falloff.
        ///
        /// A flake at the size this is drawn on screen is two or three pixels, so it is a
        /// dot and nothing else -- six-armed crystal geometry would be invisible at every
        /// distance the player ever sees one. What matters is the falloff: a hard-edged
        /// dot reads as dirt on the lens, and a flake has to read as something in the air
        /// between the player and the world.
        /// </summary>
        private static void WriteFlake()
        {
            const int Size = 32;

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var pixels = new Color32[Size * Size];

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = (x + 0.5f) / Size - 0.5f;
                float dy = (y + 0.5f) / Size - 0.5f;

                float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                float a = Mathf.Clamp01(1f - r);

                // Squared, so the edge fades rather than stopping.
                a *= a;

                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            string path = $"{TextureFolder}/Flake.png";
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
        }

        private static float SnowHeight(float u, float v)
        {
            float drift = TileFbm(u, v, 2, 4, 6601);
            float sastrugi = TileFbm(u * 1.4f, v * 0.5f, 6, 3, 6602);

            // A crust breaks in plates rather than crumbling, so the few hard edges there
            // are come from a thresholded band, not from noise.
            float crust = TileFbm(u, v, 9, 2, 6603);
            crust = Mathf.Max(0f, crust - 0.68f) * 1.4f;

            return Mathf.Clamp01(drift * 0.6f + sastrugi * 0.28f + crust * 0.12f);
        }

        /// <summary>
        /// Station tiling: a hard grid of grouted tiles, each one faintly uneven.
        ///
        /// The grid is the point -- it is the one surface here with a man-made rhythm, and
        /// that rhythm is what tells a player how far away a subway wall is. The tiles are
        /// given individual height so the grid does not read as a printed pattern.
        /// </summary>
        private static float TileHeight(float u, float v)
        {
            const float Across = 8f;

            float tu = u * Across;
            float tv = v * Across;

            float fu = tu - Mathf.Floor(tu);
            float fv = tv - Mathf.Floor(tv);

            // Distance to the nearest grout line, in tile units.
            float edge = Mathf.Min(Mathf.Min(fu, 1f - fu), Mathf.Min(fv, 1f - fv));
            float grout = Mathf.Clamp01(edge / 0.06f);

            // Per-tile variation, keyed off the tile's own index so it is constant across
            // that tile and different from its neighbours.
            float key = TileFbm(Mathf.Floor(tu) / Across, Mathf.Floor(tv) / Across, 4, 1, 7701);

            float grime = TileFbm(u, v, 24, 3, 7702);

            return Mathf.Clamp01(grout * (0.62f + key * 0.22f) + grime * 0.16f);
        }

        /// <summary>Wind ripples over soft dune relief, with grains on top.</summary>
        private static float SandHeight(float u, float v)
        {
            // Ripples run across the wind and are what gives sand its scale. Warped by
            // noise so they meander rather than running dead straight, which is what
            // they do and also what stops the tile reading as corduroy.
            float warp = TileFbm(u, v, 3, 3, 8101) - 0.5f;
            float ripple = Mathf.Sin((u * 18f + v * 5f + warp * 2.4f) * Mathf.PI * 2f) * 0.5f + 0.5f;

            // Sharpened: a ripple has a rounded crest and a flatter trough.
            ripple = Mathf.Pow(ripple, 1.6f);

            float relief = TileFbm(u, v, 2, 4, 8102);
            float grain = TileFbm(u, v, 64, 2, 8103);

            return relief * 0.42f + ripple * 0.4f + grain * 0.18f;
        }

        /// <summary>Horizontal strata, warped, with cracks cut across them.</summary>
        private static float RockHeight(float u, float v)
        {
            float warp = (TileFbm(u, v, 3, 4, 9201) - 0.5f) * 0.16f;

            // Bands. Sandstone is laid down flat and weathers band by band, so the
            // banding is the thing that says "rock" before any amount of roughness does.
            float bands = Mathf.Sin((v + warp) * Mathf.PI * 2f * 7f) * 0.5f + 0.5f;
            bands = Mathf.SmoothStep(0.15f, 0.85f, bands);

            // Ridged noise reads as cracks: the fold puts a sharp crease where ordinary
            // noise has a smooth minimum.
            float ridged = 1f - Mathf.Abs(TileFbm(u, v, 6, 4, 9202) * 2f - 1f);
            float cracks = Mathf.Pow(ridged, 6f);

            float grain = TileFbm(u, v, 48, 2, 9203);

            return bands * 0.4f + (1f - cracks) * 0.32f + grain * 0.28f;
        }

        /// <summary>
        /// Mud brick under a coat of plaster: staggered courses, a wandering bed joint,
        /// and blotches over the top.
        ///
        /// A compound wall needs its own map rather than borrowing the sand one. Sand's
        /// detail is wind ripples, which are parallel, evenly spaced and all the same
        /// size, and on a vertical face that is not a mud wall -- it is corrugated iron.
        /// The fix is not subtlety, it is irregularity: the course height wanders, the
        /// stagger per course comes out of a hash, and the plaster on top is bigger than
        /// any of the bricks so nothing repeats at brick scale.
        /// </summary>
        private static float AdobeHeight(float u, float v)
        {
            float warp = TileFbm(u, v, 4, 3, 5501) - 0.5f;

            const int courses = 9;
            const int bricks = 12;

            float course = (v + warp * 0.045f) * courses;
            float row = Mathf.Floor(course);
            float downRow = course - row;

            // Every course is offset from the one below by its own amount, wrapped on the
            // course count so the top of the tile still lines up with the bottom.
            float stagger = TileNoise(row, 0f, courses, 5502);
            float brick = (u + stagger + warp * 0.03f) * bricks;
            float alongBrick = brick - Mathf.Floor(brick);

            float Joint(float t, float width)
                => Mathf.SmoothStep(0f, width, t) * Mathf.SmoothStep(0f, width, 1f - t);

            float face = Joint(downRow, 0.1f) * Joint(alongBrick, 0.07f);

            float plaster = TileFbm(u, v, 5, 4, 5503);
            float grit = TileFbm(u, v, 40, 2, 5504);

            return face * 0.4f + plaster * 0.44f + grit * 0.16f;
        }

        /// <summary>Straight grain along u, with knots and split ends.</summary>
        private static float TimberHeight(float u, float v)
        {
            // Stretched hard along the plank so the noise becomes streaks. Wood is
            // anisotropic and isotropic noise on a plank reads as concrete.
            float streak = TileFbm(u * 0.18f, v * 6f, 8, 4, 7301);
            float rings = Mathf.Sin((streak * 6f + v * 3f) * Mathf.PI * 2f) * 0.5f + 0.5f;
            float grain = TileFbm(u * 0.4f, v * 8f, 32, 2, 7302);

            return rings * 0.45f + streak * 0.3f + grain * 0.25f;
        }

        /// <summary>Overlapping wavelets, for a normal map rather than for a colour.</summary>
        private static float WaterHeight(float u, float v)
        {
            float a = Mathf.Sin((u * 6f + TileFbm(u, v, 4, 2, 6401) * 3f) * Mathf.PI * 2f);
            float b = Mathf.Sin((v * 9f - u * 3f + TileFbm(u, v, 5, 2, 6402) * 3f) * Mathf.PI * 2f);

            return (a * 0.5f + b * 0.5f) * 0.25f + 0.5f + TileFbm(u, v, 16, 3, 6403) * 0.2f;
        }

        // ==================================================================
        /// <summary>
        /// Renders one height function into an albedo and a normal map and imports both.
        ///
        /// <paramref name="albedo"/>, when given, supplies the shade separately from the
        /// relief, for a surface whose colour and shape disagree -- ash lies pale and flat
        /// over rock that is dark and cracked, and a single field cannot say both.
        /// <paramref name="size"/> defaults to <see cref="TextureSize"/>; a map meant to
        /// tile over a wider patch of ground needs more pixels to keep the same detail.
        /// </summary>
        private static void WriteHeightPair(string name, System.Func<float, float, float> height,
                                            float albedoContrast, float normalStrength,
                                            System.Func<float, float, float> albedo = null,
                                            int size = 0)
        {
            int n = size > 0 ? size : TextureSize;
            var field = new float[n * n];

            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    field[y * n + x] = Mathf.Clamp01(height(x / (float)n, y / (float)n));

            // ---- albedo: the height field as light and shade around mid grey ----
            var albedoPixels = new Color32[n * n];

            for (int i = 0; i < field.Length; i++)
            {
                float shade = albedo != null ? albedo((i % n) / (float)n, (i / n) / (float)n) : field[i];
                float value = Mathf.Clamp01(0.5f + (shade - 0.5f) * albedoContrast);

                // Gamma, because the PNG is sRGB and the shader works in linear. Written
                // straight, every one of these came out noticeably too dark.
                byte b = (byte)Mathf.RoundToInt(Mathf.Pow(value, 1f / 2.2f) * 255f);
                albedoPixels[i] = new Color32(b, b, b, 255);
            }

            WritePng($"{TextureFolder}/{name}_Albedo.png", albedoPixels, n, importer =>
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
            });

            // ---- normal: central differences on the same field ----
            var normal = new Color32[n * n];

            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // Wrapped, or the normal map has a seam even though the height does not.
                    float left = field[y * n + (x - 1 + n) % n];
                    float right = field[y * n + (x + 1) % n];
                    float down = field[((y - 1 + n) % n) * n + x];
                    float up = field[((y + 1) % n) * n + x];

                    var v = new Vector3((left - right) * normalStrength,
                                        (down - up) * normalStrength, 1f).normalized;

                    normal[y * n + x] = new Color32(
                        (byte)Mathf.RoundToInt((v.x * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((v.y * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((v.z * 0.5f + 0.5f) * 255f), 255);
                }

            WritePng($"{TextureFolder}/{name}_Normal.png", normal, n, importer =>
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
            });
        }

        private static void WritePng(string path, Color32[] pixels, int size,
                                     System.Action<TextureImporter> configure)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels);
            texture.Apply();

            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                configure(importer);
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                importer.maxTextureSize = Mathf.Max(size, TextureSize);
                importer.SaveAndReimport();
            }
        }

        private static Texture2D LoadDetail(string name)
            => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{name}.png");

        // ==================================================================
        /// <summary>
        /// A material carrying one of the detail maps above, created or updated in place.
        ///
        /// Deliberately not routed through <see cref="MakeMaterialAt"/>, which returns an
        /// existing asset untouched and names the file after the colour. That is right
        /// for the hundreds of one-off tinted blocks it serves -- reusing the asset at a
        /// path is what stops a rebuild dirtying every material in the project -- but
        /// these are a fixed handful of named surfaces, so they are keyed by role and
        /// rewritten. Retuning sand then actually changes the sand instead of leaving a
        /// second sand material on disk next to the first, which is the growth
        /// FPSKitPrune exists to clear up.
        /// </summary>
        private static Material MakeDetailMaterial(string role, Color color, string detail,
                                                   float tiling, float smoothness, float metallic,
                                                   float normalScale = 1f)
        {
            string path = $"{MaterialFolder}/{SafeName(_theme.themeName)}_{role}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);

            var albedo = LoadDetail($"{detail}_Albedo");
            var normal = LoadDetail($"{detail}_Normal");
            var scale = new Vector2(tiling, tiling);

            if (albedo != null)
            {
                if (mat.HasProperty("_BaseMap"))
                {
                    mat.SetTexture("_BaseMap", albedo);
                    mat.SetTextureScale("_BaseMap", scale);
                }

                mat.mainTexture = albedo;
                mat.mainTextureScale = scale;
            }

            if (normal != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetTextureScale("_BumpMap", scale);
                if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", normalScale);

                // URP's Lit shader only samples the normal map when this keyword is on,
                // and setting the texture does not set it. Missed, the map is assigned,
                // visible in the inspector, and doing nothing at all.
                mat.EnableKeyword("_NORMALMAP");
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
#endif
