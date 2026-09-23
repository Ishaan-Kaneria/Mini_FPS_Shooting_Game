#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The volcanic dressing for the open zone: a molten river, cinder cones for
    /// landmarks, vents breathing smoke across the plain, obsidian breaking through the
    /// ash, and volcanoes on the horizon.
    ///
    /// <b>Only the dressing is new.</b> The layout is the open zone's -- the gorge, the
    /// bridges, the fence and the chain of triggers down the meander -- because that is
    /// the part that has been proven to connect and to kill only where it should, and a
    /// lava river is the same obstacle as a water one: an edge you must not cross except
    /// where the level says. Everything here is chosen by <c>LevelTheme.volcanicZone</c>
    /// and every desert pass it replaces is still there for the desert.
    ///
    /// Four things about it are worth not re-deriving.
    ///
    /// <b>The glow is emission, never colour.</b> A lava river painted bright orange under
    /// a red sky is an orange stripe; what reads as molten is a surface that stays bright
    /// where the light is not, and blooms. So the lava, the cracks in the ground and the
    /// craters carry an emission map, and the base colour under them is crust -- nearly
    /// black. Tinting the albedo instead would also have made every crack as bright as
    /// the sun lets it be, which at the bottom of a canyon is not very.
    ///
    /// <b>The emission map rides on the base map's tiling.</b> URP's Lit shader has one
    /// tiling for every map on a material, so the glow map is authored at the same
    /// repeat as the albedo it sits on and <see cref="ScrollingWater"/> drags both along
    /// together -- which is exactly what makes the river's crust and its cracks flow as
    /// one surface.
    ///
    /// <b>Going over the edge is death, not a ledge.</b> The desert's trigger has its lid
    /// just over the water, so a player who jumps the fence lands on the canyon's bench
    /// and stands there. Over lava that is wrong twice: the rock that close to a molten
    /// river is hotter than anything alive, and the player asked for stepping in to kill.
    /// <see cref="BuildKillVolume"/> raises the lid to a couple of metres under the rim for
    /// lava and widens it to the canyon's third row, so everything below the walkable top
    /// shelf is lethal -- and the shelf itself, and the bridge decks, stay well clear of it.
    ///
    /// <b>Smoke is placed, ash is carried.</b> The plumes stand where the heat is -- over
    /// the river, in the craters, at the vents -- so they say where the danger is from
    /// across the map. The ash drifts past the player the way the snow does, in a box
    /// kept over their head, because covering four hundred and fifty metres with particles
    /// costs hundreds of thousands of them to put a few hundred where anyone can see one.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static Material _lavaMat, _obsidianMat, _cinderMat, _smokeMat, _emberMat;

        /// <summary>The emission tint of anything molten. HDR: past 1 is what the bloom catches.</summary>
        private static readonly Color MoltenGlow = new Color(1f, 0.36f, 0.06f);

        // ==================================================================
        // Textures
        // ==================================================================
        /// <summary>
        /// The maps only this dressing uses: basalt and its glowing cracks, lava and its
        /// molten seams, and a puff of smoke. Written on every build like the rest, since
        /// they are a pure function of constants and come out byte-identical.
        /// </summary>
        private static void WriteVolcanicTextures()
        {
            WriteHeightPair("Basalt", BasaltHeight, albedoContrast: 0.5f, normalStrength: 3.4f);
            WriteMask("Basalt_Glow", BasaltGlow);

            WriteHeightPair("Lava", LavaHeight, albedoContrast: 0.55f, normalStrength: 2.6f);
            WriteMask("Lava_Glow", LavaGlow);

            WritePuff();
        }

        /// <summary>
        /// 0 below <paramref name="from"/>, 1 above <paramref name="to"/>, smooth between:
        /// the shader language's smoothstep.
        ///
        /// <b>Not Mathf.SmoothStep</b>, which takes the same three numbers and means
        /// something else -- it interpolates <i>between</i> the first two by the third. Every
        /// mask in this file was first written with it, and what came back was a glow map
        /// that was white from edge to edge: a crack test returning at most 0.035 was
        /// subtracted from one, and the whole plain glowed like the river.
        /// </summary>
        private static float Ramp(float from, float to, float x)
            => Smooth(Mathf.Clamp01((x - from) / (to - from)));

        /// <summary>
        /// Distance to the nearest and second-nearest point of a jittered lattice that
        /// wraps at <paramref name="period"/> -- tileable Worley noise.
        ///
        /// F2 - F1 is zero on the boundary between two cells and grows towards the middle
        /// of each, which is the whole of what makes it the right function for both
        /// surfaces here: basalt cools into polygons with cracks between them, and lava
        /// crusts into plates with the melt showing between them.
        /// </summary>
        private static Vector2 Worley(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);

            float f1 = float.MaxValue, f2 = float.MaxValue;

            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int gx = cx + dx, gy = cy + dy;
                    int wx = ((gx % period) + period) % period;
                    int wy = ((gy % period) + period) % period;

                    float px = gx + 0.1f + 0.8f * Hash01(wx, wy, 0, seed);
                    float py = gy + 0.1f + 0.8f * Hash01(wx, wy, 1, seed);

                    float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));

                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) f2 = d;
                }

            return new Vector2(f1, f2);
        }

        /// <summary>
        /// Cooled basalt: jointing polygons, vesicles, and a ropy skin over the top.
        ///
        /// The polygons are the columnar jointing a flow cracks into as it cools, and
        /// they are what says "this was liquid once" rather than "this is dark rock".
        /// </summary>
        private static float BasaltHeight(float u, float v)
        {
            var cells = Worley(u + 0.04f * (TileFbm(u, v, 3, 2, 3301) - 0.5f), v, 5, 3302);
            float joint = Ramp(0f, 0.12f, cells.y - cells.x);

            // Gas bubbles frozen in the rock. Small, dense, and sunk rather than raised.
            float bubbles = TileFbm(u, v, 40, 2, 3303);
            float pits = bubbles > 0.7f ? (bubbles - 0.7f) * 1.6f : 0f;

            // Pahoehoe: the ropy wrinkles a flow's skin drags into as it moves.
            float rope = Mathf.Sin((u * 9f + TileFbm(u, v, 4, 3, 3304) * 3.2f) * Mathf.PI * 2f)
                         * 0.5f + 0.5f;

            float grain = TileFbm(u, v, 24, 3, 3305);

            return joint * 0.42f + rope * 0.16f + grain * 0.36f - pits * 0.3f + 0.06f;
        }

        /// <summary>
        /// Which of the basalt's cracks are still hot: the joints, but only in patches.
        ///
        /// Every crack glowing is a grid of orange lines -- a pattern the eye finds in a
        /// second and then sees repeating every tile across the whole arena. A
        /// low-frequency mask lets a crack glow in one place and be dead rock a metre
        /// along, which is also what a cooling field actually looks like.
        /// </summary>
        private static float BasaltGlow(float u, float v)
        {
            var cells = Worley(u + 0.04f * (TileFbm(u, v, 3, 2, 3301) - 0.5f), v, 5, 3302);
            float crack = 1f - Ramp(0f, 0.035f, cells.y - cells.x);

            float hot = Ramp(0.56f, 0.72f, TileFbm(u, v, 2, 3, 3306));

            return crack * hot;
        }

        /// <summary>
        /// A lava river's surface: crust plates riding on the melt, with the melt showing
        /// in the seams. Plates are raised, seams sunk, so the normal map says the crust
        /// is floating on something.
        /// </summary>
        private static float LavaHeight(float u, float v)
        {
            float warp = TileFbm(u, v, 3, 3, 4401) - 0.5f;
            var cells = Worley(u + warp * 0.08f, v + warp * 0.05f, 6, 4402);

            float plate = Ramp(0f, 0.22f, cells.y - cells.x);
            float skin = TileFbm(u, v, 18, 3, 4403);

            return plate * 0.62f + skin * 0.38f;
        }

        /// <summary>
        /// Where the lava is molten: wide in the seams, a dull glow through the thinner
        /// crust, and hotter streaks where the flow is fastest.
        /// </summary>
        private static float LavaGlow(float u, float v)
        {
            float warp = TileFbm(u, v, 3, 3, 4401) - 0.5f;
            var cells = Worley(u + warp * 0.08f, v + warp * 0.05f, 6, 4402);

            float seam = 1f - Ramp(0.02f, 0.16f, cells.y - cells.x);
            float through = Ramp(0.45f, 0.9f, TileFbm(u, v, 5, 3, 4404)) * 0.35f;

            return Mathf.Clamp01(seam + through + 0.08f);
        }

        /// <summary>A grayscale mask for an emission map. sRGB, because emission is a colour.</summary>
        private static void WriteMask(string name, System.Func<float, float, float> mask)
        {
            int n = TextureSize;
            var pixels = new Color32[n * n];

            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float value = Mathf.Clamp01(mask(x / (float)n, y / (float)n));
                    byte b = (byte)Mathf.RoundToInt(Mathf.Pow(value, 1f / 2.2f) * 255f);
                    pixels[y * n + x] = new Color32(b, b, b, 255);
                }

            WritePng($"{TextureFolder}/{name}.png", pixels, n, importer =>
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
            });
        }

        /// <summary>
        /// One puff of smoke: a soft disc broken up by noise, alpha falling off to nothing.
        ///
        /// Broken up because a smooth disc drawn a few dozen times over itself is a stack
        /// of circles -- the noise is what lets overlapping puffs read as one volume.
        /// </summary>
        private static void WritePuff()
        {
            const int Size = 128;

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var pixels = new Color32[Size * Size];

            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float dx = (x + 0.5f) / Size - 0.5f;
                    float dy = (y + 0.5f) / Size - 0.5f;

                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float body = Mathf.Clamp01(1f - r);
                    body = body * body * (3f - 2f * body);

                    float billow = Fbm2(x / (float)Size * 5f, y / (float)Size * 5f, 5151, 4) + 0.5f;
                    float a = Mathf.Clamp01(body * (0.55f + billow * 0.75f) - 0.06f);

                    byte shade = (byte)Mathf.RoundToInt(Mathf.Lerp(200f, 255f, billow));
                    pixels[y * Size + x] = new Color32(shade, shade, shade,
                                                       (byte)Mathf.RoundToInt(a * 255f));
                }

            tex.SetPixels32(pixels);
            tex.Apply();

            string path = $"{TextureFolder}/Puff.png";
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

        // ==================================================================
        // The sky
        // ==================================================================
        /// <summary>
        /// A red sky, painted into a panorama rather than asked of the procedural shader.
        ///
        /// <b>The procedural sky cannot be red.</b> Its tint does not colour the sky, it
        /// sets which wavelengths the air scatters -- and air thick enough to look like a
        /// volcanic world, under a sun low enough to cast long shadows, scatters the red
        /// out on the way in. What the first build of this arena got for a tint of
        /// almost pure orange was a sky of clear sea green, lighting every surface that
        /// reflects it the same green. There is no setting of the three knobs that gives
        /// a dark red zenith over a burning horizon, which is the one sky this is.
        ///
        /// The horizon band is matched to the fog, so the far ground fades into the sky
        /// rather than stopping against a line, and the smoke is laid in long bands along
        /// the horizon because that is the direction the plumes drift.
        /// </summary>
        private static Material VolcanicSky()
        {
            string texPath = $"{TextureFolder}/Sky_{SafeName(_theme.themeName)}.png";
            WriteVolcanicSky(texPath);

            string path = $"{MaterialFolder}/Sky_{SafeName(_theme.themeName)}.mat";
            var shader = Shader.Find("Skybox/Panoramic");
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (shader == null) return existing;

            var mat = existing != null ? existing : new Material(shader);
            mat.shader = shader;
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            mat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f));
            mat.SetFloat("_Exposure", 1f);
            mat.SetFloat("_Rotation", 0f);

            if (existing == null) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);

            return mat;
        }

        private static void WriteVolcanicSky(string path)
        {
            const int W = 2048, H = 1024;
            var pixels = new Color32[W * H];

            // Ground-coloured rather than black. Nobody looks at the nadir on purpose, but
            // every gap in the world shows it -- the apron's open river channel past the
            // boundary did, as a black slot either side of the lava.
            var nadir = new Color(0.22f, 0.12f, 0.09f);
            var horizon = Color.Lerp(_theme.fogColor, Color.white, 0.12f);
            var glow = new Color(0.98f, 0.47f, 0.19f);
            var low = new Color(0.80f, 0.27f, 0.11f);
            var mid = new Color(0.46f, 0.12f, 0.07f);
            var zenith = new Color(0.14f, 0.045f, 0.045f);

            Color At(float elevation)
            {
                if (elevation < 0f) return Color.Lerp(horizon, nadir, Mathf.SmoothStep(0f, 1f, -elevation / 14f));
                if (elevation < 5f) return Color.Lerp(horizon, glow, elevation / 5f);
                if (elevation < 14f) return Color.Lerp(glow, low, (elevation - 5f) / 9f);
                if (elevation < 38f) return Color.Lerp(low, mid, (elevation - 14f) / 24f);
                return Color.Lerp(mid, zenith, Mathf.SmoothStep(0f, 1f, (elevation - 38f) / 52f));
            }

            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H;
                float elevation = (v - 0.5f) * 180f;
                var band = At(elevation);

                // Smoke lives between a few degrees and forty up: below that it is lost in
                // the glow, above it the eye is looking through clear air at the dark.
                float layer = elevation <= 1f ? 0f
                            : Ramp(1f, 8f, elevation) * (1f - Ramp(22f, 48f, elevation));

                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W;

                    // Stretched along the horizon: wrapped in u so the seam at the back of
                    // the panorama does not show, and squashed in v into streaks.
                    float smoke = TileFbm(u, v * 0.35f, 7, 5, 6601);
                    float streak = TileFbm(u * 1f, v * 2.2f, 4, 3, 6602);

                    float dense = Ramp(0.44f, 0.72f, smoke * 0.7f + streak * 0.3f) * layer;

                    // Lit from underneath by the glow of the ground, dark on top -- which is
                    // what separates a smoke bank from a cloud.
                    var smokeColour = Color.Lerp(new Color(0.20f, 0.08f, 0.06f),
                                                 new Color(0.62f, 0.24f, 0.11f),
                                                 Mathf.Clamp01(1f - elevation / 30f) * (1f - streak * 0.5f));

                    var c = Color.Lerp(band, smokeColour, dense * 0.85f);

                    pixels[y * W + x] = new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f),
                                                    (byte)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f),
                                                    (byte)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f), 255);
                }
            }

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;

                // No mips: a panorama minified at its poles blends the wrapped edge into
                // itself and draws a seam straight up the back of the sky.
                importer.mipmapEnabled = false;
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.maxTextureSize = W;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }

        // ==================================================================
        // Materials
        // ==================================================================
        /// <summary>
        /// Swaps the desert palette for a volcanic one, after <see cref="ResolveOutdoorMaterials"/>
        /// has made the desert's. Only the materials the open zone's shared passes reach
        /// for are replaced here; everything else keeps its desert role and is recoloured
        /// by the theme.
        /// </summary>
        private static void ResolveVolcanicMaterials()
        {
            // The ground, under the same role name BuildDuneField asks for, so the dune
            // field, the boundary ridge and the apron are one surface. The glow is added
            // here and survives BuildDuneField's own call, which never touches emission.
            var ground = MakeDetailMaterial("Ground", _theme.floorColor, "Basalt", 0.09f,
                                            _theme.floorSmoothness * 0.4f, 0f, 1.15f);
            Glow(ground, "Basalt_Glow", MoltenGlow * 2.4f);
            _sandMat = ground;

            // The river. Crust-dark under the glow: see the class summary.
            _lavaMat = MakeDetailMaterial("Lava", new Color(0.20f, 0.09f, 0.06f), "Lava", 0.05f,
                                          0.5f, 0f, 1.1f);
            Glow(_lavaMat, "Lava_Glow", MoltenGlow * 3.2f);
            _waterMat = _lavaMat;

            // The rock nearest the melt, lit by it. Dark, because it is lit from below and
            // only a little, and with the hot cracks showing through.
            _rockWetMat = MakeDetailMaterial("RockWet", Shade(_theme.bankColor, 0.62f), "Basalt",
                                             0.16f, 0.3f, 0f, 1.1f);
            Glow(_rockWetMat, "Basalt_Glow", MoltenGlow * 2.2f);

            // Iron rather than timber: the bridge trestles and the scaffold decks stand
            // over a river that would set wood alight.
            _timberMat = MakeDetailMaterial("Timber", new Color(0.23f, 0.21f, 0.20f), "Metal",
                                            0.5f, 0.38f, 0.55f, 1.1f);

            // The survey crews' sheeting, scorched.
            _canvasMat = MakeDetailMaterial("Canvas", new Color(0.46f, 0.25f, 0.15f), "Metal",
                                            0.45f, 0.25f, 0.3f, 0.8f);

            _obsidianMat = MakeDetailMaterial("Obsidian", new Color(0.07f, 0.06f, 0.07f), "Rock",
                                              0.3f, 0.86f, 0f, 0.5f);

            // Reddish scoria round a crater's lip, where the rock was thrown out hot.
            _cinderMat = MakeDetailMaterial("Cinder", new Color(0.38f, 0.17f, 0.11f), "Basalt",
                                            0.12f, 0.18f, 0f, 1.3f);
            Glow(_cinderMat, "Basalt_Glow", MoltenGlow * 1.8f);

            _smokeMat = ParticleMaterial("Smoke", "Puff", additive: false, Color.white);
            _emberMat = ParticleMaterial("Ember", "Flake", additive: true, MoltenGlow * 4f);
        }

        /// <summary>
        /// Turns on emission with a mask. Missing the keyword leaves the colour and the
        /// map assigned and doing nothing, the same trap the normal map has.
        /// </summary>
        private static void Glow(Material mat, string mask, Color hdr)
        {
            if (mat == null || !mat.HasProperty("_EmissionColor")) return;

            var map = LoadDetail(mask);
            if (map != null) mat.SetTexture("_EmissionMap", map);

            mat.EnableKeyword("_EMISSION");

            // Through SetEmission, for its GI flags. URP derives the keyword from those
            // flags alone, so None -- the obvious choice for something never baked --
            // strips _EMISSION again as the material is saved: the first build of this
            // arena had every emission colour and map written and not one thing glowing.
            // MakeTintableMaterial records the same trap.
            SetEmission(mat, hdr);
        }

        /// <summary>
        /// A transparent particle material, with its blend state written out rather than
        /// implied. Setting URP's <c>_Surface</c> alone does nothing outside the material
        /// inspector -- the inspector is what derives the blend factors and the keyword
        /// from it -- so a material made in code and only told it is transparent renders
        /// opaque: every puff of smoke a grey square.
        /// </summary>
        private static Material ParticleMaterial(string role, string texture, bool additive, Color tint)
        {
            string path = $"{MaterialFolder}/{SafeName(_theme.themeName)}_{role}.mat";

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{texture}.png");

            if (tex != null)
            {
                mat.mainTexture = tex;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);

            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ==================================================================
        // Cones
        // ==================================================================
        /// <summary>
        /// A volcano's profile as (radius, height), unit base and unit height, from the
        /// foot up over the rim and down into the crater.
        ///
        /// Concave: shallow at the foot where the ejecta has spread, steepening all the
        /// way to the rim. That is the shape a cinder cone actually is, and it is also
        /// the reason the upper flank is past the fifty degrees a CharacterController
        /// climbs -- a crater a player can walk up to is a sniper's perch nothing else
        /// can reach.
        /// </summary>
        private static readonly Vector2[] ConeProfile =
        {
            new Vector2(1.00f, 0.00f),
            new Vector2(0.86f, 0.09f),
            new Vector2(0.71f, 0.24f),
            new Vector2(0.56f, 0.44f),
            new Vector2(0.43f, 0.66f),
            new Vector2(0.33f, 0.87f),
            new Vector2(0.28f, 1.00f),   // the rim
            new Vector2(0.21f, 0.94f),
            new Vector2(0.15f, 0.80f)    // the crater floor's edge
        };

        private const int ConeRim = 6;
        private const float CraterFloor = 0.78f;

        /// <summary>
        /// A cone to that profile, closed top and bottom, with its flank cut by gullies.
        ///
        /// <b>Wound up the flank -- lower ring, upper ring, upper next, lower next -- which
        /// is outward.</b> The same order ButteMesh arrived at the hard way. It stays
        /// outward over the rim and down the crater wall, where "outward" means towards
        /// the axis and up, which is the side anybody looking into a crater sees.
        /// </summary>
        private const int ConeSides = 22;

        /// <summary>
        /// Every ring of a cone, worked out once and shared: the cone is meshed from these
        /// and the lava tongues are laid on them. Two copies of the noise would disagree by
        /// a few per cent of the radius, which on a cone is enough to bury a tongue inside
        /// the rock it is meant to be running down.
        /// </summary>
        private static Vector3[][] ConeRings(int seed)
        {
            int levels = ConeProfile.Length;
            var rings = new Vector3[levels][];

            for (int l = 0; l < levels; l++)
            {
                rings[l] = new Vector3[ConeSides];
                var p = ConeProfile[l];

                for (int s = 0; s < ConeSides; s++)
                {
                    float a = s * Mathf.PI * 2f / ConeSides;

                    // Lumps for the silhouette, gullies for the flank: rain and rockfall
                    // cut a cone into ribs, and the ribs are what make one read as
                    // something weathered rather than as a lathe.
                    float lump = Fbm2(Mathf.Cos(a) * 1.8f, Mathf.Sin(a) * 1.8f + l * 0.4f, seed, 2) * 0.22f;
                    float gully = l > 0 && l < ConeRim
                        ? -0.06f * Mathf.Pow(Mathf.Abs(Mathf.Sin(a * 5.5f + seed)), 3f)
                        : 0f;

                    float r = p.x * (1f + lump + gully);
                    float y = p.y + (l == ConeRim ? Fbm2(a * 2f, seed * 0.1f, seed + 9, 2) * 0.05f : 0f);

                    rings[l][s] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                }
            }

            return rings;
        }

        /// <summary>A point on ring <paramref name="l"/> at any angle, on the mesh's own chord.</summary>
        private static Vector3 OnRing(Vector3[][] rings, int l, float angle)
        {
            float at = Mathf.Repeat(angle / (Mathf.PI * 2f), 1f) * ConeSides;
            int s = Mathf.FloorToInt(at) % ConeSides;
            int n = (s + 1) % ConeSides;

            return Vector3.Lerp(rings[l][s], rings[l][n], at - Mathf.Floor(at));
        }

        private static Mesh VolcanoMesh(int seed)
            => Pooled($"volcano_{seed}", () =>
            {
                var build = new MeshBuild { UVScale = 0.2f };
                int levels = ConeProfile.Length;
                int sides = ConeSides;

                var rings = ConeRings(seed);

                for (int l = 0; l < levels - 1; l++)
                    for (int s = 0; s < sides; s++)
                    {
                        int n = (s + 1) % sides;
                        build.Quad(rings[l][s], rings[l + 1][s], rings[l + 1][n], rings[l][n]);
                    }

                // The crater floor facing up, and the base facing down, so the shape is
                // closed from every side and its centroid means something to VerifyZone.
                var floor = new Vector3(0f, CraterFloor, 0f);
                int last = levels - 1;

                for (int s = 0; s < sides; s++)
                {
                    int n = (s + 1) % sides;
                    build.Tri(floor, rings[last][n], rings[last][s]);
                    build.Tri(Vector3.zero, rings[0][s], rings[0][n]);
                }

                return build.ToMesh($"Volcano_{seed}");
            });

        /// <summary>
        /// The pool in the crater: a disc of lava just above the crater floor, facing up.
        /// Fanned as centre, next, this -- the order the frozen lake records as the one
        /// that faces the sky.
        /// </summary>
        private static Mesh CraterPoolMesh() => Pooled("craterPool", () =>
        {
            var build = new MeshBuild { UVScale = 1.6f };
            const int Sides = 20;
            float r = ConeProfile[ConeProfile.Length - 1].x * 1.12f;
            // Above the edge of the crater floor (0.80) and below where the wall rises past the
            // pool's rim, so its edge is always tucked under rock rather than hanging in air.
            float y = CraterFloor + 0.025f;

            var centre = new Vector3(0f, y, 0f);

            for (int i = 0; i < Sides; i++)
            {
                float t0 = i / (float)Sides * Mathf.PI * 2f;
                float t1 = (i + 1) / (float)Sides * Mathf.PI * 2f;

                build.Tri(centre,
                          new Vector3(Mathf.Cos(t1) * r, y, Mathf.Sin(t1) * r),
                          new Vector3(Mathf.Cos(t0) * r, y, Mathf.Sin(t0) * r));
            }

            return build.ToMesh("CraterPool");
        });

        /// <summary>
        /// A tongue of lava down the flank from the rim, laid on the cone's own rings and
        /// pushed a little proud of them. <paramref name="width"/> is a half-width in the
        /// cone's unit radius. Built with the flank's winding -- lower ring, upper ring,
        /// upper next, lower next -- so it faces out of the cone as the flank does.
        /// </summary>
        private static Mesh LavaTongueMesh(int coneSeed, int seed, float start, float width, int down)
            => Pooled($"tongue_{coneSeed}_{seed}_{start:0.000}_{width:0.000}_{down}", () =>
            {
                var build = new MeshBuild { UVScale = 2.2f };
                var rings = ConeRings(coneSeed);
                int from = Mathf.Max(0, ConeRim - down);

                Vector3 At(int l, float a)
                {
                    var p = OnRing(rings, l, a);
                    var radial = new Vector3(p.x, 0f, p.z);

                    // Out along the radius and up a touch: enough to clear the chord the
                    // cone is meshed with between two of its ribs, and no more.
                    return p + radial * 0.05f + radial.normalized * 0.006f + Vector3.up * 0.004f;
                }

                for (int l = from; l < ConeRim; l++)
                {
                    // It meanders as it goes and narrows as it cools.
                    float a0 = start + Fbm2(l * 0.7f, seed, seed, 2) * 0.35f;
                    float a1 = start + Fbm2((l + 1) * 0.7f, seed, seed, 2) * 0.35f;

                    float w0 = width / ConeProfile[l].x * Mathf.Lerp(0.55f, 1f, (l - from) / (float)down);
                    float w1 = width / ConeProfile[l + 1].x * Mathf.Lerp(0.55f, 1f, (l + 1 - from) / (float)down);

                    build.Quad(At(l, a0 - w0), At(l + 1, a1 - w1), At(l + 1, a1 + w1), At(l, a0 + w0));
                }

                return build.ToMesh("LavaTongue");
            });

        /// <summary>
        /// One cone, with its pool, and optionally lava down its side and smoke out of it.
        /// Returns the cone so the caller can decide what the bake and the map make of it.
        /// </summary>
        private static GameObject Volcano(Transform parent, int layer, System.Random rng, string name,
                                          Material rock, Vector3 foot, float width, float height,
                                          int seed, bool collider, int tongues, bool smoke,
                                          float smokeScale)
        {
            var at = new GameObject(name + "Site").transform;
            at.SetParent(parent, false);
            at.localPosition = foot;

            var scale = new Vector3(width * 0.5f, height, width * 0.5f * Rand(rng, 0.82f, 1.15f));
            var turn = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);

            var cone = MeshObject(at, name, VolcanoMesh(seed), rock,
                                  Vector3.zero, turn, scale, layer, _theme.wallTag, collider);

            var pool = MeshObject(at, "CraterPool", CraterPoolMesh(), _lavaMat, Vector3.zero, turn, scale,
                                  layer, "Untagged", collider: false);
            Hide(pool);

            for (int i = 0; i < tongues; i++)
            {
                var tongue = MeshObject(at, "LavaTongue",
                                        LavaTongueMesh(seed, i, Rand(rng, 0f, Mathf.PI * 2f),
                                                       Rand(rng, 0.03f, 0.055f), 3 + rng.Next(3)),
                                        _lavaMat, Vector3.zero, turn, scale, layer, "Untagged",
                                        collider: false);
                Hide(tongue);
            }

            if (smoke)
                Plume(at, new Vector3(0f, height * 0.9f, 0f), width * 0.045f, smokeScale, rng.Next());

            return cone;
        }

        // ==================================================================
        // The passes
        // ==================================================================
        /// <summary>
        /// The landmarks as cinder cones, from the same plans the buttes would have used.
        /// Sunk to the lowest ground under the whole footprint, for the reason every rock
        /// in this kit is -- see LowestGroundIn.
        /// </summary>
        private static void BuildVolcanoLandmarks(Transform root, int layer, System.Random rng)
        {
            var group = new GameObject("Landmarks").transform;
            group.SetParent(root, false);

            foreach (var plan in _landmarkPlans)
            {
                // The cone's widest reach: half its width, stretched by up to 1.15 on one
                // axis and lumped out by up to a tenth more.
                float radius = plan.Width * 0.66f;
                float ground = LowestGroundIn(plan.Point.x, plan.Point.y, radius);

                var foot = new Vector3(plan.Point.x, ground - 1.6f - plan.Height * 0.05f, plan.Point.y);

                var rock = Rand(rng, 0f, 1f) < 0.5f ? _rockMat : _rockDarkMat;

                var cone = Volcano(group, layer, rng, "Volcano", rock, foot, plan.Width, plan.Height,
                                   plan.Seed, collider: true, tongues: rng.Next(0, 3), smoke: true,
                                   smokeScale: 0.7f);

                // A cone's crater and its lower flanks are both shallower than the agent
                // slope; left on the bake, every landmark grows two islands of navmesh
                // nothing can reach, one of them in a crater full of lava.
                NoStanding(cone);

                // Scoria thrown out of it, lying round its foot.
                int thrown = rng.Next(3, 7);

                for (int k = 0; k < thrown; k++)
                {
                    float angle = Rand(rng, 0f, Mathf.PI * 2f);
                    float away = plan.Width * Rand(rng, 0.55f, 0.9f);

                    Boulder(group, layer, rng,
                            new Vector3(plan.Point.x + Mathf.Cos(angle) * away, 0f,
                                        plan.Point.y + Mathf.Sin(angle) * away),
                            Rand(rng, 1.6f, 4.2f));
                }
            }
        }

        /// <summary>
        /// The horizon: volcanoes rather than mesas, one in three smoking and glowing.
        /// No colliders, on the layer the bake ignores -- they are only ever looked at.
        /// </summary>
        private static void BuildVolcanicBackdrop(Transform root, int layer, System.Random rng)
        {
            if (_theme.backdropCount <= 0) return;

            var group = new GameObject("Backdrop").transform;
            group.SetParent(root, false);

            for (int i = 0; i < _theme.backdropCount; i++)
            {
                float angle = i * 2.39996f;
                float distance = Rand(rng, _theme.backdropDistance.x, _theme.backdropDistance.y);

                float h = Rand(rng, _theme.backdropHeight.x, _theme.backdropHeight.y);
                float w = Rand(rng, _theme.backdropWidth.x, _theme.backdropWidth.y);

                // Never taller than it is wide by much: a volcano that is a needle reads
                // as a spire, and the spires are what the obsidian is for.
                w = Mathf.Max(w, h * 1.5f);

                var pos = new Vector3(Mathf.Cos(angle) * distance, -h * 0.06f, Mathf.Sin(angle) * distance);

                float haze = Mathf.InverseLerp(_theme.backdropDistance.x, _theme.backdropDistance.y, distance);
                var tint = Color.Lerp(_theme.backdropColor, _theme.fogColor, haze * 0.4f);
                var mat = MakeMaterial($"Mesa_{ColorKey(tint)}", tint, 0.05f, 0f);

                bool active = i % 3 == 0;

                var cone = Volcano(group, layer, rng, "Mesa", mat, pos, w, h, rng.Next(1, 999),
                                   collider: false, tongues: active ? 2 + rng.Next(3) : 0,
                                   smoke: active, smokeScale: Mathf.Clamp(h / 45f, 1.6f, 5f));

                Hide(cone);
            }
        }

        /// <summary>
        /// Fumaroles: low cones of scoria round a hot throat, breathing smoke.
        ///
        /// They are what makes the plain itself read as hot rather than only the river --
        /// smoke rising out of the ground a hundred metres from any lava says the whole
        /// field is live. Each throat carries a small trigger over its pool: a player who
        /// climbs a vent and steps into what is plainly molten has to find it so.
        /// </summary>
        private static void BuildVents(Transform root, int layer, System.Random rng, float half)
        {
            var group = new GameObject("Vents").transform;
            group.SetParent(root, false);

            int vents = 16 + rng.Next(6);

            for (int i = 0; i < vents; i++)
            {
                float width = Rand(rng, 7f, 12f);
                if (!TryClaim(rng, half * 0.88f, width * 0.6f, out var point, 24)) continue;

                float height = Rand(rng, 1.8f, 3.2f);
                float ground = LowestGroundIn(point.x, point.y, width * 0.66f);
                var foot = new Vector3(point.x, ground - 0.5f, point.y);

                var vent = Volcano(group, layer, rng, "Vent", _cinderMat, foot, width, height, 800 + i,
                                   collider: true, tongues: 0, smoke: true, smokeScale: 0.45f);

                NoStanding(vent);

                // The pool's own trigger, the size of the pool and a little above it.
                var throat = new GameObject("VentHeat");
                throat.transform.SetParent(vent.transform.parent, false);
                throat.transform.localPosition = new Vector3(0f, height * (CraterFloor + 0.08f), 0f);

                var box = throat.AddComponent<BoxCollider>();
                box.isTrigger = true;
                // The crater floor's diameter: its unit radius is half the cone's width.
                float across = width * ConeProfile[ConeProfile.Length - 1].x;
                box.size = new Vector3(across, height * 0.2f, across);

                var kill = throat.AddComponent<KillVolume>();
                kill.instantKill = true;
            }
        }

        /// <summary>
        /// Obsidian: glassy black shards leaning out of the ash, where lava met something
        /// cold and froze before it could crystallise. Tall, thin and tilted, which is
        /// what they are for -- the rest of the field is round, and these are the only
        /// verticals in it, so they give the eye a scale at every distance.
        /// </summary>
        private static void BuildSpires(Transform root, int layer, System.Random rng, float half)
        {
            var group = new GameObject("Spires").transform;
            group.SetParent(root, false);

            int clusters = 12 + rng.Next(6);

            for (int c = 0; c < clusters; c++)
            {
                if (!TryClaim(rng, half * 0.9f, 7f, out var point, 20)) continue;

                int shards = 2 + rng.Next(4);

                for (int i = 0; i < shards; i++)
                {
                    float x = point.x + Rand(rng, -4f, 4f);
                    float z = point.y + Rand(rng, -4f, 4f);

                    float tall = Rand(rng, 3.5f, 9.5f);
                    float thick = Rand(rng, 0.5f, 1.3f);

                    var build = new MeshBuild { UVScale = 0.6f };
                    build.Tube(Vector3.zero, Vector3.up * tall, thick, thick * Rand(rng, 0.05f, 0.25f),
                               rng.Next(4, 7), Rand(rng, 0f, 3f));

                    var lean = Quaternion.Euler(Rand(rng, -24f, 24f), Rand(rng, 0f, 360f), Rand(rng, -24f, 24f));

                    // Buried a good way: a leaning shard's low side is the one that shows
                    // a gap, and a gap under a closed shell is a way inside it.
                    float y = LowestGroundIn(x, z, thick * 2f) - 1.2f;

                    var shard = MeshObject(group, "Spire", build.ToMesh("Spire"), _obsidianMat,
                                           new Vector3(x, y, z), lean, Vector3.one, layer, _theme.wallTag);
                    NoStanding(shard);
                }
            }
        }

        /// <summary>
        /// Heat along the river: lights down in the canyon so the walls are lit from the
        /// melt, and smoke rising out of it so the river can be found from anywhere on the
        /// map by the column over it.
        /// </summary>
        private static void BuildLavaGlow(Transform root, float half)
        {
            var group = new GameObject("LavaGlow").transform;
            group.SetParent(root, false);

            var rng = new System.Random(_theme.randomSeed ^ 0x1A7A);
            float y = WaterSurfaceY;

            // Lights. Few and wide: every one of these is an additional light on every
            // renderer it reaches, and the canyon walls are long meshes.
            for (float z = -half - 20f; z <= half + 20f; z += 46f)
            {
                var go = new GameObject("LavaLight");
                go.transform.SetParent(group, false);
                // High in the cut rather than down on the melt. At three metres over the
                // lava the walls were at the very edge of the range, and the upper band --
                // the one the player looks at from the rim -- came back black. Up here it
                // reaches both walls and spills a little over the lip, which is what a
                // river this hot does to the ground beside it.
                go.transform.localPosition = new Vector3(GorgeCentreAt(z), y + 6f, z);

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.46f, 0.16f);
                light.range = 52f;
                light.intensity = 22f;
                light.shadows = LightShadows.None;
            }

            for (float z = -half - 30f; z <= half + 30f; z += Rand(rng, 30f, 48f))
            {
                float x = GorgeCentreAt(z) + Rand(rng, -6f, 6f);
                Plume(group, new Vector3(x, y, z), Rand(rng, 4f, 7f), Rand(rng, 0.8f, 1.2f), rng.Next());
            }
        }

        /// <summary>
        /// One column of smoke. Born lit by what is under it and fading to ash grey as it
        /// rises and spreads -- the colour change is most of what makes it read as heat
        /// rather than as fog somebody placed.
        ///
        /// Configured in the editor and saved into the scene, so there is nothing to run
        /// at load and nothing a stripped shader can break.
        /// </summary>
        private static void Plume(Transform parent, Vector3 at, float radius, float scale, int seed)
        {
            var go = new GameObject("Smoke");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var random = new System.Random(seed);

            var main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = true;
            main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f * Mathf.Sqrt(scale), 15f * Mathf.Sqrt(scale));
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f * scale, 3.2f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(4f * scale, 8f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            main.gravityModifier = -0.02f;

            // Seeded, so a rebuild writes the same scene and two neighbouring plumes do
            // not puff in step.
            system.useAutoRandomSeed = false;
            system.randomSeed = (uint)random.Next();

            var emission = system.emission;
            emission.rateOverTime = 3.4f;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = radius;

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.6f));

            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            // Heat-lit at the bottom, ash at the top, gone at the end.
            var colour = system.colorOverLifetime;
            colour.enabled = true;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.95f, 0.46f, 0.24f), 0f),
                    new GradientColorKey(new Color(0.42f, 0.30f, 0.27f), 0.25f),
                    new GradientColorKey(new Color(0.24f, 0.21f, 0.21f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.42f, 0.12f),
                    new GradientAlphaKey(0.28f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                });
            colour.color = gradient;

            // Drift with the wind, so the columns lean together and read as one weather.
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0.9f * scale, 1.6f * scale);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0.4f * scale);
            velocity.z = new ParticleSystem.MinMaxCurve(0.3f * scale, 0.7f * scale);

            var noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.6f * scale;
            noise.frequency = 0.15f;
            noise.scrollSpeed = 0.2f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = _smokeMat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
        }

        /// <summary>
        /// Ash drifting down and embers drifting up, over the player's head wherever they
        /// go -- the snow's component, retinted. See <see cref="Snowfall"/> for why it
        /// follows the player rather than covering the arena.
        /// </summary>
        private static void BuildAshfall(Transform root)
        {
            var ash = new GameObject("Ashfall");
            ash.transform.SetParent(root, false);

            var fall = ash.AddComponent<Snowfall>();
            fall.flakeMaterial = FlakeMaterial();
            fall.flakes = 900;
            fall.fallSpeed = 1.6f;
            fall.wind = new Vector3(2.2f, 0f, 0.9f);
            fall.tint = new Color(0.34f, 0.31f, 0.30f, 0.8f);
            fall.tintAlt = new Color(0.55f, 0.50f, 0.47f, 0.7f);
            fall.size = new Vector2(0.05f, 0.14f);

            var embers = new GameObject("Embers");
            embers.transform.SetParent(root, false);

            var rise = embers.AddComponent<Snowfall>();
            rise.flakeMaterial = _emberMat;
            rise.flakes = 140;
            rise.ceiling = 2f;
            rise.fallSpeed = -1.1f;
            rise.spread = 30f;
            rise.wind = new Vector3(1.4f, 0f, 0.6f);
            rise.tint = new Color(1f, 0.8f, 0.5f, 1f);
            rise.tintAlt = new Color(1f, 0.5f, 0.2f, 0.8f);
            rise.size = new Vector2(0.04f, 0.09f);
        }
    }
}
#endif
