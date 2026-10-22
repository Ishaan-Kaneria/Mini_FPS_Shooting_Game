#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The town's walls as varied as a real street's (2026-10-21, "very artificial due to same texture of the wall"):
    ///
    ///   * More of each kind of wall: buff, honey and pale yellow brick, brick with its plaster coming away, sand brick;
    ///     cut limestone, pale stone blocks, warm sandstone; rough white, grey, smooth beige, cracked and
    ///     pitted plaster beside the painted ones; mud brick, pitted and cracked plaster and old stone in the old town. All
    ///     CC0 from Poly Haven, tiled at their stated size and tinted to a wanted colour over each image's mean.
    ///   * A front and its sides need not match: a street front of brick or stone often has a rendered or bare-block back
    ///     and sides (only the street face was paid for); a plaster block sometimes shows its blocks round the back.
    ///   * A shop's ground floor in one of five claddings: rusticated stone, dressed stone, coursed granite, stone-slip
    ///     cladding, small facade tiles -- plaster blocks with shops get one too, more often than not.
    ///   * Weather on every face, in the detail mesh (no collider; gone with the detail at a distance): grime splashed up
    ///     the foot of the wall, rain streaks down from window sills and from the parapet's scuppers, plaster patches
    ///     lighter (fresh) or darker (damp) on plaster and mud walls.
    ///
    /// Every choice here comes from a sequence seeded by the layout and where the building stands, never from the
    /// building's own sequence, so every building's plan, windows and dressings come out as before.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static Material[] _brickWalls, _ashlarWalls, _plasterExtra, _baseCladding, _oldExtra, _sideRenders;
        private static Material _bareBlock, _weatherDark, _weatherLight;
        private static readonly float[] BrickWeights = { 0.20f, 0.10f, 0.22f, 0.16f, 0.18f, 0.14f };
        private static readonly float[] AshlarWeights = { 0.30f, 0.20f, 0.28f, 0.22f };

        private static void ResolveWallVariety()
        {
            // Tints: the wanted colour over the image's mean (measured): yellow_brick (165, 137, 107) to buff (198, 160, 112), ...
            _brickWalls = new[]
            {
                _brickBuff, _brickSand,
                TownMat("BrickYellow", "yellow_brick", new Color(1.20f, 1.17f, 1.05f), 2.0f, 0.25f),
                TownMat("BrickYellowHoney", "yellow_brick", new Color(1.15f, 1.07f, 0.92f), 2.0f, 0.25f),
                TownMat("BrickPale", "yellow_bricks", new Color(1.36f, 1.33f, 1.36f), 2.0f, 0.25f),
                TownMat("BrickPlastered", "rough_plaster_brick", new Color(1.62f, 1.68f, 1.85f), 1.5f, 0.25f),
            };
            _ashlarWalls = new[]
            {
                _ashlar, _ashlarPale,
                TownMat("StoneWhiteBlocks", "white_sandstone_blocks_02", new Color(1.20f, 1.22f, 1.19f), 2.0f, 0.25f),
                TownMat("StoneWarm", "sandstone_blocks_04", new Color(1.07f, 1.12f, 1.14f), 3.0f, 0.25f),
                // (Not old_sandstone_02: across a whole facade its coarse orange blocks read as a cartoon; it is old-town stone.)
            };
            _plasterExtra = new[]
            {
                TownMat("PlasterRoughWhite", "white_rough_plaster", new Color(1.23f, 1.22f, 1.20f), 2.0f),
                TownMat("PlasterGreyRender", "grey_plaster_02", new Color(1.25f, 1.33f, 1.52f), 2.0f),
                TownMat("PlasterBeigeSmooth", "beige_wall_001", new Color(1.34f, 1.40f, 1.42f), 3.0f),
                TownMat("PlasterCracked", "worn_cracked_plaster", new Color(1.56f, 1.66f, 1.86f), 1.8f),
                TownMat("PlasterWhiteFlat", "white_plaster_02", new Color(1.55f, 1.59f, 1.64f), 2.0f),
                TownMat("PlasterPitted", "plaster_stone_wall_01", new Color(1.32f, 1.39f, 1.45f), 2.3f),
            };
            _baseCladding = new[]
            {
                _rustic, _rusticDressed,
                TownMat("CladGranite", "granite_wall", new Color(1.58f, 1.68f, 1.75f), 1.9f, 0.25f),
                TownMat("CladStoneSlip", "exterior_wall_cladding", new Color(1.38f, 1.38f, 1.36f), 2.0f, 0.3f),
                TownMat("CladFacadeTiles", "rectangular_facade_tiles", new Color(1.70f, 1.70f, 1.75f), 2.0f, 0.45f),
            };
            _oldExtra = new[]
            {
                TownMat("MudBrick", "clay_block_wall", new Color(1.30f, 1.35f, 1.41f), 2.0f),
                TownMat("OldPitted", "plaster_stone_wall_01", new Color(1.25f, 1.25f, 1.22f), 2.3f),
                TownMat("OldSandstone", "old_sandstone_02", new Color(1.32f, 1.52f, 1.95f), 1.53f),
                TownMat("OldCracked", "worn_cracked_plaster", new Color(1.45f, 1.40f, 1.40f), 1.8f),
            };
            // A bare block wall, the grey of a new one (the concrete block image is dark: mean 69, 61, 50).
            _bareBlock = TownMat("BareBlock", "concrete_block_wall", new Color(2.35f, 2.50f, 2.75f), 2.4f);
            _sideRenders = new[] { _plasters[0], _plasters[3], _plasters[6], _plasters[7], _plasterExtra[1], _plasterExtra[4] };
            _weatherDark = WeatherMat("WeatherDark", new Color(0.17f, 0.145f, 0.12f));
            _weatherLight = WeatherMat("WeatherLight", new Color(0.96f, 0.93f, 0.87f));
        }

        /// <summary>A building's walls from the wider palettes (after NewMass made its draws), and the finish of its sides.</summary>
        private static void VaryWalls(Mass b)
        {
            var own = new System.Random(LayoutSeed(new Vector3(b.r.center.x, 0f, b.r.center.y)) ^ 0x3A11);
            if (b.oldStyle)
            {
                if (own.NextDouble() < 0.45) b.wall = _oldExtra[own.Next(_oldExtra.Length)];
                return;
            }
            switch (b.style)
            {
                case FacadeStyle.Brick: b.wall = _brickWalls[Pick(own, BrickWeights)]; break;
                case FacadeStyle.Ashlar: b.wall = _ashlarWalls[Pick(own, AshlarWeights)]; break;
                default: if (own.NextDouble() < 0.4) b.wall = _plasterExtra[own.Next(_plasterExtra.Length)]; break;
            }
            double s = own.NextDouble();
            b.sideWall = b.style == FacadeStyle.Plaster ? (s < 0.22 ? _bareBlock : null)
                       : s < 0.45 ? _sideRenders[own.Next(_sideRenders.Length)] : s < 0.6 ? _bareBlock : null;
        }

        /// <summary>The cladding of a building's ground floor (after MaybeRusticBase made its draws): one of five, and on a
        /// plaster block with shops, more often than not, one of the four modern ones.</summary>
        private static void VaryBase(Mass b, bool shops)
        {
            if (b.oldStyle || b.floors < 2) return;
            var own = new System.Random(LayoutSeed(new Vector3(b.r.center.x, 0f, b.r.center.y)) ^ 0x5A5E);
            if (b.baseMat != null) b.baseMat = _baseCladding[own.Next(_baseCladding.Length)];
            else if (shops && b.style == FacadeStyle.Plaster && own.NextDouble() < 0.6) b.baseMat = _baseCladding[own.Next(1, 5)];
        }

        // ------------------------------------------------------------------
        // Weather
        // ------------------------------------------------------------------
        /// <summary>
        /// The weather on one face: grime up the wall's foot (broken at openings down to the floor), rain streaks under
        /// some sills and down from the parapet, patches on plaster and mud. <paramref name="holes"/> are the face's
        /// openings (u0, u1, v0, v1 from the wall's foot at <paramref name="bottom"/>); quads in the detail mesh, a
        /// centimetre off the wall.
        /// </summary>
        private static void Weather(TownMesh det, Mass b, Vector3 a0, Vector3 u, float len, Vector3 nrm, List<Vector4> holes, List<int> floorOf,
                                    float bottom, List<Vector2> floorGaps, int side)
        {
            var own = new System.Random(LayoutSeed(a0 + u * (len * 0.5f)) ^ 0x7EA7);
            Vector3 up = Vector3.up;
            void Cell(Material mat, int cell, float x0, float x1, float y0, float y1, float off)
            {
                if (x1 - x0 < 0.05f || y1 - y0 < 0.05f) return;
                Vector3 p0 = a0 + u * x0 + nrm * off, p1 = a0 + u * x1 + nrm * off;
                float c0 = (cell % 2) * 0.5f + 0.004f, r0 = (cell / 2) * 0.5f + 0.004f, c1 = c0 + 0.492f, r1 = r0 + 0.492f;
                det.QuadUV(mat, p0 + up * y0, p1 + up * y0, p1 + up * y1, p0 + up * y1, nrm,
                           new Vector2(c0, r0), new Vector2(c1, r0), new Vector2(c1, r1), new Vector2(c0, r1));
            }
            bool Clear(float x0, float x1, float y0, float y1)
            {
                foreach (var h in holes)
                    if (x1 > h.x - 0.12f && x0 < h.y + 0.12f && y1 > h.z + bottom - 0.12f && y0 < h.w + bottom + 0.12f) return false;
                return true;
            }

            // Grime splashed up the foot: above the foundation course on a modern wall, from the ground on an old one.
            float g0 = b.oldStyle ? b.groundMin : b.baseY + 0.45f;
            float g1 = (b.oldStyle ? b.baseY : g0) + Rand(own, 0.6f, b.oldStyle ? 2.0f : 1.6f);
            float from = 0f;
            var gaps = new List<Vector2>(floorGaps);
            gaps.Sort((p, q) => p.x.CompareTo(q.x));
            foreach (var g in gaps) { Cell(_weatherDark, 0, from, Mathf.Min(g.x, len), g0, g1, 0.011f); from = Mathf.Max(from, g.y); }
            Cell(_weatherDark, 0, from, len, g0, g1, 0.011f);

            // Rain streaks under a third of the sills, stopping short of anything below.
            for (int i = 0; i < holes.Count; i++)
            {
                var h = holes[i];
                float sill = h.z + bottom;
                if (sill - b.FloorY(Mathf.Clamp(floorOf[i], 0, b.floors - 1)) < 0.4f || own.NextDouble() > 0.35) continue;
                float w = (h.y - h.x) * Rand(own, 0.5f, 0.9f), mid = (h.x + h.y) * 0.5f;
                float yTop = sill - 0.05f, yLow = Mathf.Max(b.baseY + 0.5f, yTop - Rand(own, 0.5f, 1.7f));
                foreach (var o in holes)
                    if (o.y > mid - w * 0.5f && o.x < mid + w * 0.5f && o.w + bottom < yTop) yLow = Mathf.Max(yLow, o.w + bottom + 0.15f);
                Cell(_weatherDark, 1, mid - w * 0.5f, mid + w * 0.5f, yLow, yTop, 0.012f);
            }

            // Streaks down from the parapet's scuppers, between the bays.
            int scuppers = len > 4f ? own.Next(1, 4) : 0;
            float topY = b.Top + b.parapet - 0.12f;
            for (int i = 0; i < scuppers; i++)
            {
                float mid = Rand(own, 0.6f, len - 0.6f), w = Rand(own, 0.25f, 0.7f);
                float yLow = topY - Rand(own, 1.5f, 4.5f);
                foreach (var o in holes)
                    if (o.y > mid - w * 0.5f && o.x < mid + w * 0.5f && o.w + bottom < topY) yLow = Mathf.Max(yLow, o.w + bottom + 0.15f);
                Cell(_weatherDark, 1, mid - w * 0.5f, mid + w * 0.5f, yLow, topY, 0.013f);
            }

            // Patches on plaster and mud: fresh (light) or damp (dark), never across an opening.
            int patches = b.oldStyle || b.style == FacadeStyle.Plaster ? own.Next(1, 4) : 0;
            for (int i = 0, tries = 0; i < patches && tries < 12; tries++)
            {
                    float w = Rand(own, 0.6f, 2.0f), h = Rand(own, 0.5f, 1.4f);
                    float x0 = Rand(own, 0.3f, Mathf.Max(0.31f, len - 0.3f - w)), y0 = Rand(own, b.baseY + 0.6f, Mathf.Max(b.baseY + 0.61f, b.Top - 0.5f - h));
                    if (!Clear(x0, x0 + w, y0, y0 + h)) continue;
                    Cell(own.NextDouble() < 0.5 ? _weatherLight : _weatherDark, 2, x0, x0 + w, y0, y0 + h, 0.010f);
                    i++;
                }
        }

        /// <summary>A transparent overlay on the weather atlas (grayscale with alpha; the colour is the material's).</summary>
        private static Material WeatherMat(string name, Color c)
        {
            string path = $"{FPSKitTownAssets.MatFolder}/Town_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = m == null;
            if (create) m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetTexture("_BaseMap", WeatherAtlas());
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.08f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_BlendModePreserveSpecular", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent - 10;
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.enableInstancing = true;
            if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>The weather atlas (2 x 2, 1024): (0) grime, dark at its foot and fading up in ragged tongues; (1) rain
        /// streaks, darkest at the top; (2) a patch with a soft, irregular edge; (3) unused. Grayscale white with alpha.</summary>
        private static Texture2D WeatherAtlas()
        {
            string path = $"{FPSKitTownAssets.Out}/Textures/TownWeather_v2.png";
            if (File.Exists(path)) return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            const int size = 1024, cell = 512;
            var px = new Color[size * size];
            for (int k = 0; k < 3; k++)
            {
                int ox = (k % 2) * cell, oy = (k / 2) * cell;
                for (int y = 0; y < cell; y++)
                    for (int x = 0; x < cell; x++)
                    {
                        float fx = (x + 0.5f) / cell, fy = (y + 0.5f) / cell;    // fy 0 at the foot
                        float a;
                        if (k == 0)
                        {
                            float tongue = 0.55f + 0.45f * Mathf.PerlinNoise(fx * 9f, 3.1f);                 // how far up it reaches here
                            float n = Mathf.PerlinNoise(fx * 30f, fy * 12f);
                            float splash = Mathf.Clamp01((Mathf.PerlinNoise(fx * 70f, fy * 70f) - 0.55f) * 3f) * Mathf.Clamp01(1f - fy * 2.5f);
                            a = Mathf.Pow(Mathf.Clamp01(1f - fy / tongue), 0.8f) * (0.6f + 0.4f * n) * 0.88f + splash * 0.25f;
                        }
                        else if (k == 1)
                        {
                            float lines = Mathf.PerlinNoise(fx * 26f, 7.7f);                                  // which columns run
                            float reach = 0.25f + 0.75f * Mathf.PerlinNoise(fx * 13f, 1.3f);                   // how far down each runs
                            float fromTop = 1f - fy;
                            float edge = Mathf.Clamp01(1f - Mathf.Abs(fx - 0.5f) * 2.2f);                     // fading at the sides
                            a = Mathf.Clamp01((lines - 0.35f) * 2f) * Mathf.Clamp01(1f - fromTop / reach) * edge;
                            a *= 0.55f + 0.45f * Mathf.PerlinNoise(fx * 60f, fy * 5f);
                            a = Mathf.Clamp01(a * 1.3f) * 0.8f;
                        }
                        else
                        {
                            float dx = fx - 0.5f, dy = fy - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                            float edge = 0.75f + 0.25f * (Mathf.PerlinNoise(Mathf.Atan2(dy, dx) * 2f + 5f, 2f) - 0.5f) * 2f;
                            a = Mathf.Clamp01((edge - r) * 4f) * (0.6f + 0.4f * Mathf.PerlinNoise(fx * 14f, fy * 14f)) * 0.6f;
                        }
                        px[(oy + y) * size + ox + x] = new Color(1f, 1f, 1f, a);
                    }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.SetPixels(px); tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                imp.alphaSource = TextureImporterAlphaSource.FromInput; imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true; imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
#endif
