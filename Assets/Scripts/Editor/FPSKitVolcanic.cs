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
        private static Material _flowMat, _ashMat, _sulfurMat, _crustMat, _warmGroundMat, _hotGroundMat;

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
            // Twice the resolution of every other map, because it tiles over nearly three
            // times the ground -- see BasaltTiling.
            WriteHeightPair("Basalt", BasaltHeight, albedoContrast: 0.62f, normalStrength: 5.2f,
                            albedo: BasaltShade, size: BasaltSize);
            WriteMask("Basalt_Glow", BasaltGlow, BasaltSize);
            WriteMask("Basalt_Cracks", BasaltCracks, BasaltSize);
            WriteMacro("Basalt_Macro");

            WriteHeightPair("Pahoehoe", PahoehoeHeight, albedoContrast: 0.5f, normalStrength: 4.4f);
            WriteMask("Pahoehoe_Glow", PahoehoeGlow);

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
            => Worley(u, v, period, seed, out _);

        /// <summary>As above, and a number from 0 to 1 that is the same everywhere in the nearest cell.</summary>
        private static Vector2 Worley(float u, float v, int period, int seed, out float cellId)
        {
            cellId = 0f;
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

                    if (d < f1) { f2 = f1; f1 = d; cellId = Hash01(wx, wy, 2, seed); }
                    else if (d < f2) f2 = d;
                }

            return new Vector2(f1, f2);
        }

        /// <summary>
        /// Pixels across the basalt maps. They tile every <see cref="BasaltRepeat"/> metres,
        /// so this keeps them at about three centimetres a pixel, which is what the old
        /// eleven-metre tile had at 512.
        /// </summary>
        private const int BasaltSize = 1024;

        /// <summary>
        /// Metres of ground per repeat of the basalt maps.
        ///
        /// <b>Eleven was the first value, and it was the single most artificial thing in
        /// the arena.</b> Five cracked polygons to a tile, every crack the same width and
        /// every one of them glowing, repeated forty times across the map: from any rise
        /// it read as a tiled floor, and from the air as a printed grid. Thirty-two metres
        /// is longer than anyone looks at the ground from, and the tile now holds whole
        /// regions of different rock, so there is no one motif for the eye to catch
        /// repeating. The macro map (<see cref="WriteMacro"/>) tiles on a period that does
        /// not divide this one, so even the repeat itself never lines up twice.
        /// </summary>
        private const float BasaltRepeat = 32f;

        /// <summary>The Lit shader's tiling for <see cref="BasaltRepeat"/>, since UVs are world metres.</summary>
        private const float BasaltTiling = 1f / BasaltRepeat;

        /// <summary>
        /// Everything the basalt's three maps read, worked out once per pixel so the
        /// relief, the shade and the glow can never disagree about where a crack is.
        ///
        /// A cooled lava field is not one surface. It is <b>plates</b> -- jointing polygons
        /// of every size, some shattered into smaller ones; <b>pahoehoe</b>, smooth ropy skin
        /// with few cracks; <b>clinker</b>, the rubbly broken top of an a'a flow; and
        /// <b>ash</b> drifted over all of it, filling the cracks and burying the glow. The
        /// four are laid out by low-frequency fields inside the tile, and it is having
        /// them side by side -- rather than any cleverness in any one -- that stops the
        /// ground reading as a pattern.
        /// </summary>
        private struct BasaltSample
        {
            public float Height, Shade, Glow, Cracks;
        }

        private static BasaltSample SampleBasalt(float u, float v)
        {
            // ---- regions ----
            float kind = TileFbm(u, v, 3, 3, 3310);
            float ash = Ramp(0.54f, 0.68f, TileFbm(u, v, 2, 3, 3311));
            float ropy = 1f - Ramp(0.34f, 0.44f, kind);
            float clinker = Ramp(0.6f, 0.7f, kind);

            // ---- plates ----
            // Warped hard, so a joint wanders rather than running ruler-straight between
            // two lattice points; and the crack width drifts, so no two look alike.
            float wu = (TileFbm(u, v, 4, 3, 3301) - 0.5f) * 0.05f;
            float wv = (TileFbm(u, v, 4, 3, 3321) - 0.5f) * 0.05f;
            var cells = Worley(u + wu, v + wv, 12, 3302, out float cellId);
            float edge = cells.y - cells.x;

            float width = Mathf.Lerp(0.03f, 0.12f, TileFbm(u, v, 5, 2, 3312));
            float plate = Ramp(0f, width, edge);

            // Some plates have shattered into smaller ones as they cooled.
            float shatter = Ramp(0.52f, 0.64f, TileFbm(u, v, 4, 2, 3313));
            var fine = Worley(u + wv * 0.6f, v + wu * 0.6f, 30, 3322);
            float subEdge = fine.y - fine.x;
            float sub = Mathf.Lerp(1f, Ramp(0f, 0.07f, subEdge), shatter);

            // Each plate slightly domed and sitting at its own height, so a joint is a
            // small step as well as a crack -- which is what lets a low sun pick the
            // plates out one by one.
            float dome = Mathf.Clamp01(edge * 2.2f) * 0.25f;
            float step = (cellId - 0.5f) * 0.12f;

            float grain = TileFbm(u, v, 40, 3, 3305);
            float bubbles = TileFbm(u, v, 64, 2, 3303);
            float pits = bubbles > 0.68f ? (bubbles - 0.68f) * 1.8f : 0f;

            float plates = plate * sub * 0.5f + dome + step + grain * 0.28f - pits * 0.3f + 0.08f;

            // ---- pahoehoe: ropes dragged into arcs by the flow ----
            float drift = TileFbm(u, v, 3, 3, 3304);
            float rope = Mathf.Sin((u * 22f + v * 7f + drift * 7f) * Mathf.PI * 2f) * 0.5f + 0.5f;
            float ropes = rope * 0.34f + grain * 0.24f + dome * 0.6f + 0.22f
                        + (1f - Ramp(0f, width * 0.6f, edge)) * -0.18f;

            // ---- clinker: rough, lumpy, broken ----
            float rubble = TileFbm(u, v, 24, 4, 3314);
            float clinkers = rubble * 0.85f + grain * 0.15f;

            float h = plates;
            h = Mathf.Lerp(h, ropes, ropy);
            h = Mathf.Lerp(h, clinkers, clinker);

            // ---- ash: fills the low ground first ----
            float settle = ash * Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(h * 1.6f - 0.3f));
            float ashTop = 0.46f + TileFbm(u, v, 48, 2, 3315) * 0.08f;
            h = Mathf.Lerp(h, Mathf.Max(h, ashTop), settle);

            // ---- shade ----
            float mottle = TileFbm(u, v, 6, 3, 3317) - 0.5f;
            float shade = 0.5f + (h - 0.5f) * 0.8f + mottle * 0.3f
                        - clinker * 0.08f + ropy * 0.03f
                        - (1f - plate) * 0.2f * (1f - ropy);
            shade = Mathf.Lerp(shade, 0.78f + mottle * 0.2f, settle * 0.9f);

            // ---- glow ----
            // Two masks. Cracks is every crack that could glow -- not the ash-buried ones,
            // few in the ropy skin and the clinker -- and is what the hot ground laid over
            // the plain lights up (BuildHotGround). Glow is a sparse, faint handful of them,
            // and is all the plain itself carries.
            //
            // <b>The plain's own glow has to be almost nothing</b>, because anything in this
            // map repeats every BasaltRepeat metres. The first cut lit a third of the cracks
            // in every tile, and from any height the lit patches lined up into a lattice
            // across the whole field -- the tile made visible by the one thing designed to
            // break it up. Heat that goes where it likes has to come from geometry placed
            // where it likes, not from a texture.
            float crackLine = 1f - Ramp(0f, width * 0.32f, edge);
            float subLine = (1f - Ramp(0f, 0.022f, subEdge)) * shatter * 0.55f;
            float flicker = Mathf.Lerp(0.35f, 1f, TileFbm(u, v, 9, 2, 3318));

            float cracks = Mathf.Max(crackLine, subLine) * flicker
                         * (1f - ropy * 0.8f) * (1f - clinker * 0.75f) * (1f - Mathf.Clamp01(settle * 1.4f));

            float hot = Ramp(0.7f, 0.84f, TileFbm(u, v, 3, 3, 3306));

            return new BasaltSample { Height = h, Shade = shade, Glow = cracks * hot, Cracks = cracks };
        }

        private static float BasaltHeight(float u, float v) => SampleBasalt(u, v).Height;
        private static float BasaltShade(float u, float v) => SampleBasalt(u, v).Shade;
        private static float BasaltGlow(float u, float v) => SampleBasalt(u, v).Glow;
        private static float BasaltCracks(float u, float v) => SampleBasalt(u, v).Cracks;

        /// <summary>
        /// Fresh pahoehoe: the glossy skin of a flow that stopped yesterday. Ropes in
        /// arcs, a few toes, and hardly a crack. Laid over the raised flows on the plain
        /// (see BuildFlowSurfaces), so the lobes read as a different, newer rock from
        /// the ground they ran across.
        /// </summary>
        private static float PahoehoeHeight(float u, float v)
        {
            float drift = TileFbm(u, v, 2, 3, 5501);
            float bend = TileFbm(u, v, 3, 2, 5502);

            // Arcs, not stripes: the phase curves with distance from a wandering centre.
            float du = u - 0.5f + (bend - 0.5f) * 0.4f, dv = v - 0.5f;
            float arc = Mathf.Sqrt(du * du * 0.4f + dv * dv) * 18f;

            float rope = Mathf.Sin((u * 11f + arc + drift * 5f) * Mathf.PI * 2f) * 0.5f + 0.5f;
            var toes = Worley(u, v, 4, 5503);
            float toe = Ramp(0f, 0.3f, toes.y - toes.x);

            float skin = TileFbm(u, v, 32, 3, 5504);

            return rope * 0.36f * toe + toe * 0.34f + skin * 0.22f + 0.06f;
        }

        /// <summary>Where the skin has split and the melt shows: the seams between toes, in places.</summary>
        private static float PahoehoeGlow(float u, float v)
        {
            var toes = Worley(u, v, 4, 5503);
            float seam = 1f - Ramp(0f, 0.035f, toes.y - toes.x);
            float hot = Ramp(0.6f, 0.8f, TileFbm(u, v, 2, 3, 5505));

            return seam * hot;
        }

        /// <summary>
        /// A colour layer at landscape scale: soot, drifted ash and oxidised rock, laid
        /// over the basalt through the Lit shader's detail slot.
        ///
        /// It exists because any single tile, however varied, is still a tile, and a
        /// four-hundred-metre field is thirteen of them in a row. This one repeats every
        /// <see cref="MacroRepeat"/> metres, which does not divide
        /// <see cref="BasaltRepeat"/>, so the two only line up again after a distance
        /// longer than the arena. What the eye gets is broad patches of darker and paler
        /// ground that come from nowhere and go nowhere -- which is what a real lava field
        /// looks like from a rise.
        ///
        /// <b>Linear, and centred on 0.5.</b> The shader doubles this and multiplies, so 0.5
        /// is "no change". Imported as sRGB, 0.5 would be sampled as about 0.21 and the
        /// whole arena would go forty per cent darker with no other sign of why.
        ///
        /// The one map in this kit that carries colour, and the exception is deliberate:
        /// every value in it is a small multiple of neutral, so the theme's floor colour
        /// still decides what colour the ground is.
        /// </summary>
        private static void WriteMacro(string name)
        {
            const int N = 512;
            var pixels = new Color32[N * N];

            var neutral = new Color(0.5f, 0.5f, 0.5f);
            var ash = new Color(0.64f, 0.62f, 0.60f);
            var oxide = new Color(0.60f, 0.46f, 0.41f);
            var soot = new Color(0.36f, 0.35f, 0.35f);

            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;

                    float s = TileFbm(u, v, 3, 4, 7701);
                    float a = TileFbm(u, v, 4, 4, 7702);
                    float o = TileFbm(u, v, 2, 4, 7703);
                    float fine = TileFbm(u, v, 16, 2, 7704) - 0.5f;

                    var c = neutral;
                    c = Color.Lerp(c, soot, Ramp(0.54f, 0.70f, s) * 0.85f);
                    c = Color.Lerp(c, oxide, Ramp(0.56f, 0.70f, o) * 0.8f);
                    c = Color.Lerp(c, ash, Ramp(0.57f, 0.72f, a) * 0.9f);

                    float jitter = 1f + fine * 0.12f;

                    pixels[y * N + x] = new Color32(
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.r * jitter) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.g * jitter) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.b * jitter) * 255f), 255);
                }

            WritePng($"{TextureFolder}/{name}.png", pixels, N, importer =>
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
            });
        }

        /// <summary>Metres per repeat of the macro map. Chosen not to divide <see cref="BasaltRepeat"/>.</summary>
        private const float MacroRepeat = 173f;

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
        private static void WriteMask(string name, System.Func<float, float, float> mask, int size = 0)
        {
            int n = size > 0 ? size : TextureSize;
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
            var panorama = WriteVolcanicSky(texPath);
            ReflectSky(panorama, $"{TextureFolder}/Reflection_{SafeName(_theme.themeName)}.cubemap");

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

        private const int SkyWidth = 2048, SkyHeight = 1024;

        /// <summary>Paints the panorama, writes it, and hands the pixels back for <see cref="ReflectSky"/>.</summary>
        private static Color32[] WriteVolcanicSky(string path)
        {
            const int W = SkyWidth, H = SkyHeight;
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

            return pixels;
        }

        /// <summary>
        /// Makes the painted sky the thing every glossy surface reflects.
        ///
        /// <b>Nothing in this kit bakes lighting, so there is no reflection generated from
        /// the skybox</b>, and without one Unity reflects its own default environment -- a
        /// pale grey-blue. On sand that hardly shows. On this map it was most of what the
        /// obsidian looked like, and the fresh flows, the iron decks and the spires all came
        /// back blue-white: brushed silver and frozen streams under a red sky.
        ///
        /// A realtime reflection probe that draws only the sky was the first attempt, and it
        /// is the wrong tool for a picture that never changes -- it only renders once the
        /// level is running, so nothing checked in the editor shows whether it worked. This
        /// is the same answer computed once, at build time, and saved with the scene: a small
        /// cubemap sampled straight from the panorama's pixels, set as the environment's
        /// custom reflection. Its mips are what rougher surfaces read, so a box-filtered
        /// chain is enough for a sky that is a gradient and some smoke.
        /// </summary>
        private static void ReflectSky(Color32[] panorama, string path)
        {
            const int N = 64;
            var cube = new Cubemap(N, TextureFormat.RGBA32, true);

            Color Sample(Vector3 d)
            {
                // The same lat-long mapping Skybox/Panoramic uses (its ToRadialCoords), at
                // zero rotation, so the reflection and the sky agree about where the smoke is.
                float u = 0.5f - Mathf.Atan2(d.z, d.x) / (Mathf.PI * 2f);
                float v = 0.5f + Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) / Mathf.PI;

                int x = Mathf.Clamp((int)(Mathf.Repeat(u, 1f) * SkyWidth), 0, SkyWidth - 1);
                int y = Mathf.Clamp((int)(v * SkyHeight), 0, SkyHeight - 1);

                return panorama[y * SkyWidth + x];
            }

            var faces = new[]
            {
                CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY,
                CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ
            };

            var face = new Color[N * N];

            foreach (var f in faces)
            {
                for (int j = 0; j < N; j++)
                    for (int i = 0; i < N; i++)
                    {
                        // Cubemap faces are addressed from the top row down.
                        float a = (i + 0.5f) / N * 2f - 1f;
                        float b = (j + 0.5f) / N * 2f - 1f;

                        Vector3 d;
                        switch (f)
                        {
                            case CubemapFace.PositiveX: d = new Vector3(1f, -b, -a); break;
                            case CubemapFace.NegativeX: d = new Vector3(-1f, -b, a); break;
                            case CubemapFace.PositiveY: d = new Vector3(a, 1f, b); break;
                            case CubemapFace.NegativeY: d = new Vector3(a, -1f, -b); break;
                            case CubemapFace.PositiveZ: d = new Vector3(a, -b, 1f); break;
                            default: d = new Vector3(-a, -b, -1f); break;
                        }

                        face[j * N + i] = Sample(d.normalized);
                    }

                cube.SetPixels(face, f);
            }

            cube.Apply(true);

            var existing = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(cube, path);

            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = cube;
            RenderSettings.reflectionIntensity = 1f;
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
            var ground = MakeDetailMaterial("Ground", _theme.floorColor, "Basalt", BasaltTiling,
                                            _theme.floorSmoothness * 0.4f, 0f, 1.15f);
            Glow(ground, "Basalt_Glow", MoltenGlow * 1.4f);
            Macro(ground, "Basalt_Macro", 1f / MacroRepeat);
            _sandMat = ground;

            // The same ground, still hot: see BuildHotGround. Identical maps and tiling, so
            // a patch's cracks are the plain's own cracks, lit.
            _warmGroundMat = MakeDetailMaterial("GroundWarm", _theme.floorColor, "Basalt", BasaltTiling,
                                                _theme.floorSmoothness * 0.4f, 0f, 1.15f);
            Glow(_warmGroundMat, "Basalt_Cracks", MoltenGlow * 1.1f);
            Macro(_warmGroundMat, "Basalt_Macro", 1f / MacroRepeat);

            _hotGroundMat = MakeDetailMaterial("GroundHot", _theme.floorColor, "Basalt", BasaltTiling,
                                               _theme.floorSmoothness * 0.4f, 0f, 1.15f);
            Glow(_hotGroundMat, "Basalt_Cracks", MoltenGlow * 3.4f);
            Macro(_hotGroundMat, "Basalt_Macro", 1f / MacroRepeat);

            // The river. Crust-dark under the glow: see the class summary.
            _lavaMat = MakeDetailMaterial("Lava", new Color(0.20f, 0.09f, 0.06f), "Lava", 0.05f,
                                          0.5f, 0f, 1.1f);
            Glow(_lavaMat, "Lava_Glow", MoltenGlow * 3.2f);
            _waterMat = _lavaMat;

            // The rock nearest the melt, lit by it. Dark, because it is lit from below and
            // only a little, and with the hot cracks showing through.
            _rockWetMat = MakeDetailMaterial("RockWet", Shade(_theme.bankColor, 0.62f), "Basalt",
                                             BasaltTiling * 2f, 0.3f, 0f, 1.1f);
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
                                            BasaltTiling * 2.5f, 0.18f, 0f, 1.3f);
            Glow(_cinderMat, "Basalt_Glow", MoltenGlow * 1.8f);

            // What lies on the plain: see BuildLavaFieldSurface. The fresh flows are darker
            // and glossier than the ground they ran over, which is the whole of how a
            // player can tell a new flow from an old one at a distance.
            // Satin, not glass. At 0.62 it mirrored the environment and every flow on the
            // map came back as a blue-white sheet -- a frozen river, on a volcano.
            _flowMat = MakeDetailMaterial("Flow", new Color(0.15f, 0.12f, 0.11f), "Pahoehoe",
                                          1f / 14f, 0.32f, 0f, 1.2f);
            Glow(_flowMat, "Pahoehoe_Glow", MoltenGlow * 2.8f);

            // Ash drifts carry wind ripples, so they borrow the sand's map: which is right,
            // because they are exactly that.
            _ashMat = MakeDetailMaterial("Ash", new Color(0.40f, 0.37f, 0.35f), "Sand",
                                         0.16f, 0.08f, 0f, 0.9f);
            _sulfurMat = MakeDetailMaterial("Sulfur", new Color(0.72f, 0.60f, 0.22f), "Sand",
                                            0.3f, 0.12f, 0f, 0.6f);
            _crustMat = MakeDetailMaterial("Crust", new Color(0.17f, 0.14f, 0.13f), "Basalt",
                                           BasaltTiling * 6f, 0.3f, 0f, 1.4f);

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
        /// Lays a colour map over a material at its own tiling, through the Lit shader's
        /// detail slot -- see <see cref="WriteMacro"/> for why it exists and why it is linear.
        /// Like emission, the keyword is not implied by the texture: without
        /// <c>_DETAIL_MULX2</c> the map is assigned and never sampled.
        /// </summary>
        private static void Macro(Material mat, string map, float tiling)
        {
            if (mat == null || !mat.HasProperty("_DetailAlbedoMap")) return;

            var tex = LoadDetail(map);
            if (tex == null) return;

            mat.SetTexture("_DetailAlbedoMap", tex);
            mat.SetTextureScale("_DetailAlbedoMap", new Vector2(tiling, tiling));
            if (mat.HasProperty("_DetailAlbedoMapScale")) mat.SetFloat("_DetailAlbedoMapScale", 1f);
            mat.EnableKeyword("_DETAIL_MULX2");
            EditorUtility.SetDirty(mat);
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
        /// How one volcano departs from <see cref="ConeProfile"/>.
        ///
        /// <b>Every volcano used to be the same volcano.</b> One profile, scaled, with a
        /// little noise on the rim -- so the horizon was thirty flat-topped cones of one
        /// shape at thirty sizes, and at a distance a cone with a wide crater reads as a
        /// mesa. Real volcanic horizons are mixed: steep stratovolcanoes with a small
        /// summit, broad shields you can barely tell are mountains, cones whose rim has
        /// broken away on one side, and summits that sit off-centre because the vent moved.
        ///
        /// A value type with a key, because the meshes are pooled by it and the cone, its
        /// pool and its tongues each rebuild the same rings from it -- three copies that
        /// have to agree to the centimetre or the lava runs inside the rock.
        /// </summary>
        private struct ConeShape
        {
            /// <summary>Scale on the radius of the summit and crater: below 1 a peak, above it a shield's broad top.</summary>
            public float Top;

            /// <summary>How far the summit sits off the axis, in unit radius, and which way.</summary>
            public float Lean, LeanAngle;

            /// <summary>0 to 1: how deeply one side of the rim has broken away, and where.</summary>
            public float Breach, BreachAngle;

            /// <summary>How many gullies are cut round the flank.</summary>
            public float Ribs;

            public static ConeShape Cinder => new ConeShape { Top = 1f, Ribs = 5.5f };

            public string Key => $"{Top:0.00}_{Lean:0.000}_{LeanAngle:0.00}_{Breach:0.00}_{BreachAngle:0.00}_{Ribs:0.0}";

            /// <summary>Where the axis has drifted to at a height fraction.</summary>
            public Vector3 Offset(float height)
                => new Vector3(Mathf.Cos(LeanAngle), 0f, Mathf.Sin(LeanAngle)) * (Lean * height * height);
        }

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
        private static Vector3[][] ConeRings(int seed, ConeShape shape)
        {
            int levels = ConeProfile.Length;
            var rings = new Vector3[levels][];

            for (int l = 0; l < levels; l++)
            {
                rings[l] = new Vector3[ConeSides];
                var p = ConeProfile[l];

                // The summit scaled towards Top, eased in from the foot so the base stays
                // where it was placed.
                float scale = Mathf.Lerp(1f, shape.Top, Smooth(Mathf.Clamp01(p.y)));
                var drift = shape.Offset(p.y);

                for (int s = 0; s < ConeSides; s++)
                {
                    float a = s * Mathf.PI * 2f / ConeSides;

                    // Lumps for the silhouette, gullies for the flank: rain and rockfall
                    // cut a cone into ribs, and the ribs are what make one read as
                    // something weathered rather than as a lathe.
                    float lump = Fbm2(Mathf.Cos(a) * 1.8f, Mathf.Sin(a) * 1.8f + l * 0.4f, seed, 2) * 0.22f;
                    float gully = l > 0 && l < ConeRim
                        ? -0.06f * Mathf.Pow(Mathf.Abs(Mathf.Sin(a * shape.Ribs + seed)), 3f)
                        : 0f;

                    float r = p.x * scale * (1f + lump + gully);
                    float y = p.y + (l == ConeRim ? Fbm2(a * 2f, seed * 0.1f, seed + 9, 2) * 0.05f : 0f);

                    // A breach: the rim and the upper flank pulled down on one side towards
                    // the height of the crater floor, never below it -- lower than the floor
                    // and the crater would have a hole in its side.
                    if (shape.Breach > 0f && l >= ConeRim - 1)
                    {
                        float window = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(a - shape.BreachAngle)), 6f);
                        float notch = 0.80f + (y - 0.80f) * 0.25f;
                        y = Mathf.Lerp(y, notch, shape.Breach * window);
                    }

                    rings[l][s] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r) + drift;
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

        /// <summary>The middle of a ring and its mean radius about that middle.</summary>
        private static Vector3 RingCentre(Vector3[] ring, out float radius)
        {
            var centre = Vector3.zero;
            foreach (var p in ring) centre += p;
            centre /= ring.Length;

            radius = 0f;
            foreach (var p in ring) radius += new Vector2(p.x - centre.x, p.z - centre.z).magnitude;
            radius /= ring.Length;

            return centre;
        }

        private static Mesh VolcanoMesh(int seed, ConeShape shape)
            => Pooled($"volcano_{seed}_{shape.Key}", () =>
            {
                var build = new MeshBuild { UVScale = 0.2f };
                int levels = ConeProfile.Length;
                int sides = ConeSides;

                var rings = ConeRings(seed, shape);

                for (int l = 0; l < levels - 1; l++)
                    for (int s = 0; s < sides; s++)
                    {
                        int n = (s + 1) % sides;
                        build.Quad(rings[l][s], rings[l + 1][s], rings[l + 1][n], rings[l][n]);
                    }

                // The crater floor facing up, and the base facing down, so the shape is
                // closed from every side and its centroid means something to VerifyZone.
                // The floor follows the summit if it has drifted off the axis.
                var floor = new Vector3(0f, CraterFloor, 0f) + shape.Offset(CraterFloor);
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
        /// that faces the sky. Sized and centred on the cone's own innermost ring, so a
        /// narrow summit gets a narrow pool and a leaning one gets it under its crater.
        /// </summary>
        private static Mesh CraterPoolMesh(int seed, ConeShape shape) => Pooled($"craterPool_{seed}_{shape.Key}", () =>
        {
            var build = new MeshBuild { UVScale = 1.6f };
            const int Sides = 20;

            var rings = ConeRings(seed, shape);
            var middle = RingCentre(rings[rings.Length - 1], out float inner);

            float r = inner * 1.12f;
            // Above the edge of the crater floor (0.80) and below where the wall rises past the
            // pool's rim, so its edge is always tucked under rock rather than hanging in air.
            float y = CraterFloor + 0.025f;

            var centre = new Vector3(middle.x, y, middle.z);

            for (int i = 0; i < Sides; i++)
            {
                float t0 = i / (float)Sides * Mathf.PI * 2f;
                float t1 = (i + 1) / (float)Sides * Mathf.PI * 2f;

                build.Tri(centre,
                          centre + new Vector3(Mathf.Cos(t1) * r, 0f, Mathf.Sin(t1) * r),
                          centre + new Vector3(Mathf.Cos(t0) * r, 0f, Mathf.Sin(t0) * r));
            }

            return build.ToMesh("CraterPool");
        });

        /// <summary>
        /// A tongue of lava down the flank from the rim, laid on the cone's own rings and
        /// pushed a little proud of them. <paramref name="width"/> is a half-width in the
        /// cone's unit radius. Built with the flank's winding -- lower ring, upper ring,
        /// upper next, lower next -- so it faces out of the cone as the flank does.
        ///
        /// Pushed out from each ring's own centre rather than from the axis: on a leaning
        /// peak the axis can be outside the summit's ring altogether, and "away from the
        /// axis" on the near side is then straight into the rock.
        /// </summary>
        private static Mesh LavaTongueMesh(int coneSeed, ConeShape shape, int seed, float start, float width, int down)
            => Pooled($"tongue_{coneSeed}_{shape.Key}_{seed}_{start:0.000}_{width:0.000}_{down}", () =>
            {
                var build = new MeshBuild { UVScale = 2.2f };
                var rings = ConeRings(coneSeed, shape);
                int from = Mathf.Max(0, ConeRim - down);

                var centres = new Vector3[rings.Length];
                var radii = new float[rings.Length];
                for (int l = 0; l < rings.Length; l++) centres[l] = RingCentre(rings[l], out radii[l]);

                Vector3 At(int l, float a)
                {
                    var p = OnRing(rings, l, a);
                    var radial = new Vector3(p.x - centres[l].x, 0f, p.z - centres[l].z);

                    // Out along the radius and up a touch: enough to clear the chord the
                    // cone is meshed with between two of its ribs, and no more.
                    return p + radial * 0.05f + radial.normalized * 0.006f + Vector3.up * 0.004f;
                }

                for (int l = from; l < ConeRim; l++)
                {
                    // It meanders as it goes and narrows as it cools.
                    float a0 = start + Fbm2(l * 0.7f, seed, seed, 2) * 0.35f;
                    float a1 = start + Fbm2((l + 1) * 0.7f, seed, seed, 2) * 0.35f;

                    float w0 = width / Mathf.Max(radii[l], 0.02f) * Mathf.Lerp(0.55f, 1f, (l - from) / (float)down);
                    float w1 = width / Mathf.Max(radii[l + 1], 0.02f) * Mathf.Lerp(0.55f, 1f, (l + 1 - from) / (float)down);

                    build.Quad(At(l, a0 - w0), At(l + 1, a1 - w1), At(l + 1, a1 + w1), At(l, a0 + w0));
                }

                return build.ToMesh("LavaTongue");
            });

        /// <summary>
        /// One cone, with its pool, and optionally lava down its side and smoke out of it.
        /// Returns the cone so the caller can decide what the bake and the map make of it.
        ///
        /// A breached cone sends its first tongue out through the breach, because that is
        /// the way the lava went -- it is what broke the rim.
        /// </summary>
        private static GameObject Volcano(Transform parent, int layer, System.Random rng, string name,
                                          Material rock, Vector3 foot, float width, float height,
                                          int seed, bool collider, int tongues, bool smoke,
                                          float smokeScale, ConeShape shape)
        {
            var at = new GameObject(name + "Site").transform;
            at.SetParent(parent, false);
            at.localPosition = foot;

            var scale = new Vector3(width * 0.5f, height, width * 0.5f * Rand(rng, 0.82f, 1.15f));
            var turn = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);

            var cone = MeshObject(at, name, VolcanoMesh(seed, shape), rock,
                                  Vector3.zero, turn, scale, layer, _theme.wallTag, collider);

            var pool = MeshObject(at, "CraterPool", CraterPoolMesh(seed, shape), _lavaMat, Vector3.zero, turn, scale,
                                  layer, "Untagged", collider: false);
            Hide(pool);

            for (int i = 0; i < tongues; i++)
            {
                float start = i == 0 && shape.Breach > 0f ? shape.BreachAngle : Rand(rng, 0f, Mathf.PI * 2f);

                var tongue = MeshObject(at, "LavaTongue",
                                        LavaTongueMesh(seed, shape, i, start,
                                                       Rand(rng, 0.03f, 0.055f), 3 + rng.Next(3)),
                                        _lavaMat, Vector3.zero, turn, scale, layer, "Untagged",
                                        collider: false);
                Hide(tongue);
            }

            if (smoke)
            {
                var summit = shape.Offset(1f);
                Plume(at, turn * new Vector3(summit.x * scale.x, height * 0.9f, summit.z * scale.z),
                      width * 0.045f * shape.Top, smokeScale, rng.Next());
            }

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

                // Varied, but only a little: every one of these has to keep an upper flank
                // past the fifty degrees a player can climb, and a shield or a deep breach
                // would hand them a ramp to the crater. See ConeProfile.
                var shape = new ConeShape
                {
                    Top = Rand(rng, 0.8f, 1.15f),
                    Lean = Rand(rng, 0f, 0.035f),
                    LeanAngle = Rand(rng, 0f, Mathf.PI * 2f),
                    Breach = rng.NextDouble() < 0.4 ? Rand(rng, 0.5f, 1f) : 0f,
                    BreachAngle = Rand(rng, 0f, Mathf.PI * 2f),
                    Ribs = Rand(rng, 3.5f, 8f)
                };

                var cone = Volcano(group, layer, rng, "Volcano", rock, foot, plan.Width, plan.Height,
                                   plan.Seed, collider: true, tongues: rng.Next(0, 3) + (shape.Breach > 0f ? 1 : 0),
                                   smoke: rng.NextDouble() < 0.75, smokeScale: Rand(rng, 0.5f, 0.9f), shape: shape);

                // A cone's crater and its lower flanks are both shallower than the agent
                // slope; left on the bake, every landmark grows two islands of navmesh
                // nothing can reach, one of them in a crater full of lava.
                NoStanding(cone);

                // And it is hollow. The shell is sunk into the plain, so the plain carries
                // on underneath it, and NoStanding marks the shell rather than the ground
                // inside it: every landmark held a disc of navmesh the size of its own foot,
                // sealed in and invisible. That was 2.7 % of the arena once the cones began
                // to vary in girth -- enough for VerifyReach to find, and enough for the
                // spawner to put an enemy inside a mountain for the whole of a level.
                SealCone(group, cone);

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
        /// Takes the ground inside a cone's foot off the bake.
        ///
        /// <b>Not <see cref="NoEntry"/>, which parents its volume to the object.</b> A cone is
        /// both turned and stretched, so a child of it lives in a sheared frame -- and the
        /// navigation package rebuilds a volume's box from its lossy scale and rotation,
        /// neither of which can hold a shear. The volume was there, it contained every
        /// stranded point when asked in its own local space, and the bake ignored it
        /// completely. A volume on an unscaled object of its own, in world space, has no
        /// frame to get wrong.
        ///
        /// Square, and as wide as the foot is at the ground, which takes the corners of a
        /// few metres of flank round the outside as well. Those were NoStanding already.
        /// </summary>
        private static void SealCone(Transform parent, GameObject cone)
        {
            var renderer = cone.GetComponent<Renderer>();
            if (renderer == null) return;

            var bounds = renderer.bounds;

            var go = new GameObject("ConeSeal");
            go.transform.SetParent(parent, false);
            go.transform.position = bounds.center;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var volume = go.AddComponent<Unity.AI.Navigation.NavMeshModifierVolume>();
            volume.size = new Vector3(bounds.size.x - 1.2f, bounds.size.y + 4f, bounds.size.z - 1.2f);
            volume.area = 1;   // Not Walkable
        }

        /// <summary>
        /// The horizon: volcanoes rather than mesas, of several kinds, in ranges.
        /// No colliders, on the layer the bake ignores -- they are only ever looked at.
        ///
        /// <b>Placed in groups, not round a dial.</b> The first version spaced them by the
        /// golden angle, which is the most even spacing there is, and that was the problem:
        /// thirty evenly spaced peaks of one shape read as a fence round the arena. A real
        /// volcanic horizon is ranges and gaps -- a cluster of cones along one rift, a lone
        /// giant, a long empty stretch of plain. So a handful of ranges are chosen first and
        /// most volcanoes join one, a few stand alone, and the sizes are skewed so that a
        /// few are huge and most are not.
        /// </summary>
        private static void BuildVolcanicBackdrop(Transform root, int layer, System.Random rng)
        {
            if (_theme.backdropCount <= 0) return;

            var group = new GameObject("Backdrop").transform;
            group.SetParent(root, false);

            // ---- ranges ----
            int ranges = 5 + rng.Next(3);
            var rangeAngles = new float[ranges];
            var rangeSpread = new float[ranges];

            for (int r = 0; r < ranges; r++)
            {
                rangeAngles[r] = Rand(rng, 0f, Mathf.PI * 2f);
                rangeSpread[r] = Rand(rng, 0.08f, 0.28f);
            }

            for (int i = 0; i < _theme.backdropCount; i++)
            {
                float angle;

                if (rng.NextDouble() < 0.78)
                {
                    int r = rng.Next(ranges);
                    // Roughly normal about the range's line: two uniforms averaged.
                    float jitter = (Rand(rng, -1f, 1f) + Rand(rng, -1f, 1f)) * 0.5f;
                    angle = rangeAngles[r] + jitter * rangeSpread[r];
                }
                else
                {
                    angle = Rand(rng, 0f, Mathf.PI * 2f);
                }

                float distance = Rand(rng, _theme.backdropDistance.x, _theme.backdropDistance.y);

                // Skewed: most volcanoes are small, a few dominate their part of the sky.
                float size = Mathf.Pow((float)rng.NextDouble(), 2.2f);
                float h = Mathf.Lerp(_theme.backdropHeight.x, _theme.backdropHeight.y, size);

                var shape = new ConeShape
                {
                    Lean = 0f,
                    LeanAngle = Rand(rng, 0f, Mathf.PI * 2f),
                    Breach = rng.NextDouble() < 0.3 ? Rand(rng, 0.6f, 1f) : 0f,
                    BreachAngle = Rand(rng, 0f, Mathf.PI * 2f),
                    Ribs = Rand(rng, 3f, 9f)
                };

                float w;
                double kind = rng.NextDouble();

                if (kind < 0.4)
                {
                    // Stratovolcano: steep, with a small summit. The shape that says
                    // "volcano" to anybody, and the one the first version had none of.
                    shape.Top = Rand(rng, 0.18f, 0.4f);
                    w = h * Rand(rng, 1.5f, 2.1f);
                }
                else if (kind < 0.62)
                {
                    // Shield: broad and low, a long slope with a wide top.
                    shape.Top = Rand(rng, 1.15f, 1.45f);
                    w = h * Rand(rng, 3.2f, 4.6f);
                    h *= 0.55f;
                }
                else
                {
                    // Cinder cone, as before -- but no longer everything.
                    shape.Top = Rand(rng, 0.75f, 1.05f);
                    w = h * Rand(rng, 1.7f, 2.5f);
                }

                // A summit that has wandered off the axis, never so far it leaves its own
                // crater ring behind: see ConeShape.
                shape.Lean = Rand(rng, 0f, 0.55f) * ConeProfile[ConeRim].x * shape.Top;

                var pos = new Vector3(Mathf.Cos(angle) * distance, -h * 0.06f, Mathf.Sin(angle) * distance);

                float haze = Mathf.InverseLerp(_theme.backdropDistance.x, _theme.backdropDistance.y, distance);
                var tint = Color.Lerp(_theme.backdropColor, _theme.fogColor, haze * 0.4f);
                // Some rock is darker and some redder, so a range is not one flat colour.
                tint = Color.Lerp(tint, new Color(tint.r * 1.25f, tint.g * 0.9f, tint.b * 0.85f),
                                  (float)rng.NextDouble() * 0.6f);
                tint = Shade(tint, Rand(rng, 0.8f, 1.1f));
                var mat = MakeMaterial($"Mesa_{ColorKey(tint)}", tint, 0.05f, 0f);

                bool active = rng.NextDouble() < 0.33;

                var cone = Volcano(group, layer, rng, "Mesa", mat, pos, w, h, rng.Next(1, 999),
                                   collider: false, tongues: active ? 1 + rng.Next(3) : 0,
                                   smoke: active, smokeScale: Mathf.Clamp(h / 45f, 1.6f, 5f), shape: shape);

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

                // The plain cinder shape, always: the throat's kill trigger below is sized
                // from ConeProfile, and a vent whose crater had moved would leave it
                // guarding rock while the real pool stood unguarded beside it.
                var vent = Volcano(group, layer, rng, "Vent", _cinderMat, foot, width, height, 800 + i,
                                   collider: true, tongues: 0, smoke: true, smokeScale: 0.45f,
                                   shape: ConeShape.Cinder);

                NoStanding(vent);
                _ventSites.Add(new Vector3(point.x, point.y, width * 0.66f));

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
        private static void Plume(Transform parent, Vector3 at, float radius, float scale, int seed,
                                  bool steam = false)
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

            // Or, for a plant's stacks and cooling towers, steam: white where it leaves and
            // greying as it thins, with no fire under it to light it.
            var gradient = new Gradient();
            gradient.SetKeys(
                steam
                ? new[]
                {
                    new GradientColorKey(new Color(0.94f, 0.94f, 0.92f), 0f),
                    new GradientColorKey(new Color(0.82f, 0.82f, 0.82f), 0.3f),
                    new GradientColorKey(new Color(0.62f, 0.63f, 0.65f), 1f)
                }
                : new[]
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
