#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The desert town's ground: a Unity Terrain 1.4 km across with the 1 km arena in the middle of it.
    ///
    /// <b>Why a Terrain and not the dune chunks the old desert used.</b> The ground is what fills most of every
    /// frame, and what makes it read as real is photographic texture blended by rule -- packed earth in the town,
    /// gravel along the road shoulders, sand drifting in from the open desert, cracked mud round the oasis, dark
    /// irrigated soil under the palms. Terrain Lit does that blend per pixel, with height-based transitions so sand
    /// fills the cracks of the gravel instead of fading over it, and draws its own level of detail. The heights it
    /// carries are exactly <see cref="_ground"/>, so <see cref="GroundHeightAt"/> -- which everything placed asks --
    /// is the surface the player walks on.
    ///
    /// <b>The shape of it</b>
    ///   * The town sits on gentle ground (a couple of metres of rise over half a kilometre): roads stay level
    ///     enough to drive and a house needs no more than a step at its door.
    ///   * The citadel has the one hill, a plateau fifteen metres up with sides under twenty-one degrees, and a
    ///     ramp cut across it at about ten per cent for the road up.
    ///   * The oasis is a hollow with water in the bottom.
    ///   * Dunes rise only outside the developed area, by distance from it, and a ridge of them closes the arena
    ///     edge (the highway is cut through it, as highways are).
    ///   * The 26 degree repose cap (FPSKitTerrain) still holds: a vehicle is coming.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private const float TownTerrainHalf = 700f;
        private const int TownHeightRes = 1025;
        private const int TownSplatRes = 1024;
        private const float TownApronY = 0f;

        /// <summary>The ground before roads were carved into it: what a road's surface follows.</summary>
        private static float[,] _townBase;

        /// <summary>Metres from the nearest developed land (roads, districts, sites), on a 4 m grid.</summary>
        private static float[,] _devDist;
        private const float DevStep = 4f;
        private static int _devN;

        private static Terrain _townTerrain;

        // ==================================================================
        // Build
        // ==================================================================
        private static void BuildTownGround(Transform root, int layer, int backdrop)
        {
            ComputeDevelopedDistance();
            FillTownHeights();
            CarveTownRoads();
            CreateTownTerrain(root, layer);
            BuildTownApron(root, backdrop);
            BuildTownMountains(root, backdrop);
            BuildTownBounds(root);
        }

        // ==================================================================
        // Developed land
        // ==================================================================
        private static void ComputeDevelopedDistance()
        {
            _devN = Mathf.CeilToInt(TownTerrainHalf * 2f / DevStep) + 1;
            _devDist = new float[_devN, _devN];
            const float Inf = 1e6f;

            var rects = new List<Rect>
            {
                Rect.MinMaxRect(-15f, -415f, 465f, 215f),     // east residential grid
                Rect.MinMaxRect(-485f, -415f, 15f, -118f),    // south-west residential
                OldTownRect, OldTownWestRect, PalaceRect, MosqueRect, MosquePlaza,
                Rect.MinMaxRect(FarmsRect.xMin - 4f, FarmsRect.yMin - 12f, FarmsRect.xMax + 4f, FarmsRect.yMax + 4f),
                Rect.MinMaxRect(JunkyardRect.xMin - 6f, JunkyardRect.yMin - 6f, JunkyardRect.xMax + 6f, JunkyardRect.yMax + 6f),
                Rect.MinMaxRect(-60f, 140f, 60f, 310f),
            };
            foreach (var r in _townRoads)
            {
                float c = r.Corridor + (r.kind == RoadKind.Highway ? 26f : 10f);
                rects.Add(r.AlongX ? Rect.MinMaxRect(r.Min, r.Fixed - c, r.Max, r.Fixed + c)
                                   : Rect.MinMaxRect(r.Fixed - c, r.Min, r.Fixed + c, r.Max));
            }

            for (int i = 0; i < _devN; i++)
                for (int j = 0; j < _devN; j++)
                {
                    var p = new Vector2(-TownTerrainHalf + i * DevStep, -TownTerrainHalf + j * DevStep);
                    bool dev = false;
                    foreach (var r in rects) if (r.Contains(p)) { dev = true; break; }
                    if (!dev && Vector2.Distance(p, CitadelCentre) < CitadelFoot + 12f) dev = true;
                    _devDist[i, j] = dev ? 0f : Inf;
                }

            // Two-pass chamfer distance (3-4), in cells, then metres.
            for (int pass = 0; pass < 2; pass++)
            {
                int start = pass == 0 ? 0 : _devN - 1, end = pass == 0 ? _devN : -1, step = pass == 0 ? 1 : -1;
                for (int i = start; i != end; i += step)
                    for (int j = start; j != end; j += step)
                    {
                        float d = _devDist[i, j];
                        int pi = i - step, pj = j - step;
                        if (pi >= 0 && pi < _devN) d = Mathf.Min(d, _devDist[pi, j] + 1f);
                        if (pj >= 0 && pj < _devN) d = Mathf.Min(d, _devDist[i, pj] + 1f);
                        if (pi >= 0 && pi < _devN && pj >= 0 && pj < _devN) d = Mathf.Min(d, _devDist[pi, pj] + 1.4142f);
                        int qj = j + step;
                        if (pi >= 0 && pi < _devN && qj >= 0 && qj < _devN) d = Mathf.Min(d, _devDist[pi, qj] + 1.4142f);
                        _devDist[i, j] = d;
                    }
            }
            for (int i = 0; i < _devN; i++) for (int j = 0; j < _devN; j++) _devDist[i, j] *= DevStep;
        }

        /// <summary>Metres from developed land at a point (bilinear on the 4 m grid).</summary>
        private static float DevDistanceAt(float x, float z)
        {
            if (_devDist == null) return 0f;
            float fx = Mathf.Clamp((x + TownTerrainHalf) / DevStep, 0f, _devN - 1.001f);
            float fz = Mathf.Clamp((z + TownTerrainHalf) / DevStep, 0f, _devN - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;
            return Mathf.Lerp(Mathf.Lerp(_devDist[ix, iz], _devDist[ix + 1, iz], tx),
                              Mathf.Lerp(_devDist[ix, iz + 1], _devDist[ix + 1, iz + 1], tx), tz);
        }

        // ==================================================================
        // Heights
        // ==================================================================
        /// <summary>How far a point is into the dune field: 0 in town, 1 seventy metres out from it.</summary>
        private static float DuneWeight(float x, float z)
            => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((DevDistanceAt(x, z) - 8f) / 70f));

        private static float TownNaturalHeight(float x, float z)
        {
            int s = _theme.randomSeed;
            // Broad, gentle relief and a slight fall towards the oasis in the north-west.
            float h = 1.7f * Fbm2(x * 0.0019f + 11f, z * 0.0019f - 7f, s * 3 + 1, 3) + 0.0016f * (x * 0.5f - z * 0.35f);

            // The citadel's hill: a plateau and sides eased in and out, with a little rock-like irregularity on the slope.
            float dc = Vector2.Distance(new Vector2(x, z), CitadelCentre);
            float hill = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((CitadelFoot - dc) / (CitadelFoot - CitadelPlateau)));
            float slopeBand = hill * (1f - hill) * 4f;
            h += CitadelHeight * hill + slopeBand * 1.3f * Fbm2(x * 0.03f, z * 0.03f, s * 5 + 2, 3);

            // The oasis hollow.
            float d0 = Vector2.Distance(new Vector2(x, z), OasisCentre);
            h -= 3.4f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((OasisRadius + 45f - d0) / 50f));
            // And the pond's own basin in its floor, banked over its last six metres: the hollow alone is flat-bottomed,
            // so water set below its shore lay under the ground and the oasis showed as a disc of mud.
            h -= 1.5f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((OasisRadius + 1f - d0) / 6f));

            // Dunes, by distance from the town, and bigger past the arena edge where nobody walks.
            float w = DuneWeight(x, z);
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            float highway = Mathf.Clamp01((Mathf.Abs(z + 400f) - 22f) / 30f);
            if (w > 0f) h += w * TownDunes(x, z, edge) * Mathf.Lerp(1f, highway, Mathf.Clamp01((edge - 460f) / 40f));

            // A ridge of sand along the arena edge: the town's limit reads as desert, not as a wall.
            float ridge = Mathf.Exp(-Mathf.Pow((edge - 516f) / 20f, 2f)) * 7.5f * highway;
            h += ridge * Mathf.Clamp01(DevDistanceAt(x, z) / 10f + 0.35f);

            // Down to the apron at the terrain's own edge.
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((TownTerrainHalf - edge) / 60f));
            return Mathf.Lerp(TownApronY, h, fade);
        }

        private static float TownDunes(float x, float z, float edge)
        {
            int s = _theme.randomSeed;
            float wind = 38f * Mathf.Deg2Rad;
            float across = x * Mathf.Cos(wind) + z * Mathf.Sin(wind);
            float along = -x * Mathf.Sin(wind) + z * Mathf.Cos(wind);
            const float wl = 105f;
            float meander = Fbm2(along * 0.006f, 0f, s * 7 + 3, 3) * wl * 0.45f;
            float primary = DuneProfile((across + meander) / wl);
            float second = DuneProfile((x * 0.77f - z * 0.64f + Fbm2(x * 0.004f, z * 0.004f, s * 13 + 9, 3) * 34f) / (wl * 0.62f)) * 0.42f;
            float hills = Fbm2(x * 0.009f, z * 0.009f, s * 29 + 5, 3) * 0.6f;
            float amp = Mathf.Lerp(5.5f, 13f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((edge - 470f) / 120f)));
            return (primary + second + hills + 0.25f) * amp;
        }

        private static void FillTownHeights()
        {
            _groundStep = TownTerrainHalf * 2f / (TownHeightRes - 1);
            _groundMin = -TownTerrainHalf;
            _groundN = TownHeightRes;
            _ground = new float[_groundN, _groundN];
            _groundPinned = new bool[_groundN, _groundN];
            _groundRigid = new bool[_groundN, _groundN];

            for (int i = 0; i < _groundN; i++)
            {
                float x = _groundMin + i * _groundStep;
                for (int j = 0; j < _groundN; j++)
                {
                    float z = _groundMin + j * _groundStep;
                    _ground[i, j] = TownNaturalHeight(x, z);
                    bool edge = i < 2 || j < 2 || i > _groundN - 3 || j > _groundN - 3;
                    // Developed land is laid out level already; pinning it stops the sand relaxing into the town.
                    bool town = DevDistanceAt(x, z) <= 0.01f && Vector2.Distance(new Vector2(x, z), CitadelCentre) > CitadelFoot + 2f;
                    _groundPinned[i, j] = edge || town;
                    _groundRigid[i, j] = edge;
                }
            }

            CarveCitadelRamp();
            RelaxTown(28);
            for (int pass = 0; pass < 5; pass++) SmoothTownPass();

            _townBase = (float[,])_ground.Clone();
            _groundReady = true;
        }

        /// <summary>The citadel ramp: a straight-graded cut from the street to the plateau, pinned so the sand relaxes round it.</summary>
        private static void CarveCitadelRamp()
        {
            foreach (var t in _townTracks)
            {
                var pts = t.points;
                float total = 0f;
                var cum = new float[pts.Length];
                for (int k = 1; k < pts.Length; k++) { total += Vector2.Distance(pts[k - 1], pts[k]); cum[k] = total; }
                float h0 = TownNaturalHeight(pts[0].x, pts[0].y);
                float h1 = TownNaturalHeight(pts[pts.Length - 1].x, pts[pts.Length - 1].y);

                for (int i = 0; i < _groundN; i++)
                {
                    float x = _groundMin + i * _groundStep;
                    if (x < -100f) continue;
                    for (int j = 0; j < _groundN; j++)
                    {
                        float z = _groundMin + j * _groundStep;
                        var p = new Vector2(x, z);
                        float best = float.MaxValue, along = 0f;
                        for (int k = 0; k + 1 < pts.Length; k++)
                        {
                            Vector2 ab = pts[k + 1] - pts[k];
                            float tt = Mathf.Clamp01(Vector2.Dot(p - pts[k], ab) / ab.sqrMagnitude);
                            float d = Vector2.Distance(p, pts[k] + ab * tt);
                            if (d < best) { best = d; along = cum[k] + ab.magnitude * tt; }
                        }
                        float half = t.width * 0.5f + 1.5f;
                        if (best > half + 7f) continue;
                        float target = Mathf.Lerp(h0, h1, Mathf.SmoothStep(0f, 1f, along / total));
                        if (best <= half) { _ground[i, j] = target; _groundPinned[i, j] = true; _groundRigid[i, j] = true; }
                        else
                        {
                            float k2 = (best - half) / 7f;
                            _ground[i, j] = Mathf.Lerp(target, _ground[i, j], Mathf.SmoothStep(0f, 1f, k2));
                        }
                    }
                }
            }
        }

        /// <summary>Thermal relaxation to the 26 degree repose angle, with a fixed number of sweeps (see RelaxToRepose for why).</summary>
        private static void RelaxTown(int sweeps)
        {
            float repose = Mathf.Tan(ReposeDegrees * Mathf.Deg2Rad);
            var dx = new[] { 1, -1, 0, 0, 1, 1, -1, -1 };
            var dz = new[] { 0, 0, 1, -1, 1, -1, 1, -1 };
            var maxDrop = new float[8];
            for (int k = 0; k < 8; k++) maxDrop[k] = repose * _groundStep * (dx[k] != 0 && dz[k] != 0 ? 1.41421f : 1f);
            for (int s = 0; s < sweeps; s++)
                for (int i = 1; i < _groundN - 1; i++)
                    for (int j = 1; j < _groundN - 1; j++)
                        for (int k = 0; k < 8; k++)
                        {
                            int ni = i + dx[k], nj = j + dz[k];
                            float drop = _ground[i, j] - _ground[ni, nj];
                            if (drop <= maxDrop[k]) continue;
                            float move = (drop - maxDrop[k]) * 0.3f;
                            if (!_groundPinned[i, j]) _ground[i, j] -= move;
                            if (!_groundPinned[ni, nj]) _ground[ni, nj] += move;
                        }
        }

        private static void SmoothTownPass()
        {
            var next = (float[,])_ground.Clone();
            for (int i = 1; i < _groundN - 1; i++)
                for (int j = 1; j < _groundN - 1; j++)
                {
                    if (_groundRigid[i, j]) continue;
                    next[i, j] = _ground[i, j] * 0.4f + (_ground[i - 1, j] + _ground[i + 1, j] + _ground[i, j - 1] + _ground[i, j + 1]) * 0.15f;
                }
            _ground = next;
        }

        /// <summary>The height a road's surface sits at: the uncarved ground, plus the asphalt.</summary>
        private static float TownRoadY(float x, float z) => SampleTownBase(x, z) + 0.03f;

        private static float SampleTownBase(float x, float z)
        {
            if (_townBase == null) return GroundHeightAt(x, z);
            float fx = Mathf.Clamp((x - _groundMin) / _groundStep, 0f, _groundN - 1.001f);
            float fz = Mathf.Clamp((z - _groundMin) / _groundStep, 0f, _groundN - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;
            return Mathf.Lerp(Mathf.Lerp(_townBase[ix, iz], _townBase[ix + 1, iz], tx),
                              Mathf.Lerp(_townBase[ix, iz + 1], _townBase[ix + 1, iz + 1], tx), tz);
        }

        /// <summary>
        /// Lowers the ground under every paved corridor (asphalt and pavements) by 18 cm, so no bump of terrain can
        /// ever show through a road, and eases it back up over three metres outside. Pavements and road edges carry
        /// their own skirts down past this, so the step is never seen.
        /// </summary>
        private static void CarveTownRoads()
        {
            const float Sink = 0.18f, Ease = 3f;
            for (int i = 0; i < _groundN; i++)
            {
                float x = _groundMin + i * _groundStep;
                for (int j = 0; j < _groundN; j++)
                {
                    float z = _groundMin + j * _groundStep;
                    float best = float.MaxValue;     // metres outside the nearest paved corridor (negative inside)
                    foreach (var r in _townRoads)
                    {
                        if (!r.Paved) continue;
                        float along = r.AlongX ? x : z, across = Mathf.Abs((r.AlongX ? z : x) - r.Fixed);
                        float outAlong = Mathf.Max(r.Min - along, along - r.Max, 0f);
                        float c = r.kind == RoadKind.Highway ? r.Half + 0.6f : r.Corridor + 0.3f;
                        float d = Mathf.Max(across - c, 0f) + outAlong;
                        if (across <= c && outAlong <= 0f) d = -1f;
                        best = Mathf.Min(best, d);
                    }
                    float dr = Vector2.Distance(new Vector2(x, z), RoundaboutCentre) - (RoundaboutWalk + 0.3f);
                    best = Mathf.Min(best, dr <= 0f ? -1f : dr);
                    if (best >= Ease) continue;
                    float carved = _townBase[i, j] - Sink;
                    _ground[i, j] = best <= 0f ? carved : Mathf.Lerp(carved, _ground[i, j], Mathf.SmoothStep(0f, 1f, best / Ease));
                }
            }
        }

        // ==================================================================
        // The Terrain
        // ==================================================================
        private static void CreateTownTerrain(Transform root, int layer)
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < _groundN; i++) for (int j = 0; j < _groundN; j++) { min = Mathf.Min(min, _ground[i, j]); max = Mathf.Max(max, _ground[i, j]); }
            min -= 1f; max += 1f;

            string path = FPSKitTownAssets.Out + "/TownTerrain.asset";
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            bool create = data == null;
            if (create) data = new TerrainData();
            data.heightmapResolution = TownHeightRes;
            data.size = new Vector3(TownTerrainHalf * 2f, max - min, TownTerrainHalf * 2f);
            var heights = new float[TownHeightRes, TownHeightRes];
            for (int i = 0; i < TownHeightRes; i++)
                for (int j = 0; j < TownHeightRes; j++)
                    heights[j, i] = (_ground[i, j] - min) / (max - min);
            data.SetHeights(0, 0, heights);

            data.terrainLayers = TownTerrainLayers();
            data.alphamapResolution = TownSplatRes;
            data.baseMapResolution = 1024;
            data.SetAlphamaps(0, 0, TownSplat(data.terrainLayers.Length));

            if (create) AssetDatabase.CreateAsset(data, path);
            else EditorUtility.SetDirty(data);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "TownTerrain";
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(-TownTerrainHalf, min, -TownTerrainHalf);
            go.layer = layer;
            go.tag = SandTag;
            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = TownTerrainMaterial();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 260f;
            terrain.shadowCastingMode = ShadowCastingMode.On;
            terrain.allowAutoConnect = false;
            terrain.drawTreesAndFoliage = false;
            go.AddComponent<MinimapMarker>().style = MinimapMarker.Style.Hidden;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            _townTerrain = terrain;
        }

        /// <summary>
        /// Ground layer order; the splat code indexes by these. Four, because URP's terrain draws four layers a pass and a
        /// second pass did not draw at all here (the fifth and sixth layers never showed); four is also the cheap case on a phone.
        /// Dune sand and cracked mud are folded into the four: dunes are the sand, cracked flats the packed earth.
        /// </summary>
        private const int LSand = 0, LPacked = 1, LGravel = 2, LSoil = 3, LDune = 0, LCracked = 1;

        private static TerrainLayer[] TownTerrainLayers()
        {
            var spec = new (string tex, float tile, Color tint)[]
            {
                // The scans are orange-brown; blue lifted over red takes them to the pale beige of real desert ground.
                ("gravelly_sand", 7f, new Color(0.92f, 0.95f, 1.08f)),
                ("dry_ground_rocks", 6f, new Color(0.90f, 0.94f, 1.06f)),
                ("rock_ground", 5f, new Color(0.95f, 0.96f, 1.0f)),
                ("farm_soil", 5f, new Color(0.85f, 0.80f, 0.74f)),
            };
            var layers = new TerrainLayer[spec.Length];
            for (int k = 0; k < spec.Length; k++)
            {
                string lp = $"{FPSKitTownAssets.Out}/Layer_{spec[k].tex}.terrainlayer";
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(lp);
                bool create = l == null;
                if (create) l = new TerrainLayer();
                var (diff, nor, mask) = PolyTexture(spec[k].tex);
                l.diffuseTexture = diff;
                l.normalMapTexture = nor;
                l.maskMapTexture = mask;
                l.tileSize = new Vector2(spec[k].tile, spec[k].tile);
                l.tileOffset = new Vector2(k * 1.7f, k * 2.3f);
                l.normalScale = 1f;
                l.diffuseRemapMin = Vector4.zero;
                l.diffuseRemapMax = new Vector4(spec[k].tint.r, spec[k].tint.g, spec[k].tint.b, 1f);
                l.maskMapRemapMin = Vector4.zero;
                // Smoothness scaled down: dry ground has no sheen, and URP's terrain reads too glossy at grazing sun.
                l.maskMapRemapMax = new Vector4(1f, 1f, 1f, 0.55f);
                l.metallic = 0f;
                l.smoothness = 0.1f;
                if (create) AssetDatabase.CreateAsset(l, lp);
                else EditorUtility.SetDirty(l);
                layers[k] = l;
            }
            return layers;
        }

        private static Material TownTerrainMaterial()
        {
            string path = $"{FPSKitTownAssets.MatFolder}/TownTerrain.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = mat == null;
            if (create) mat = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
            // Set here because the terrain's shader GUI, which normally sets them, never runs in a batch build.
            mat.SetFloat("_EnableHeightBlend", 1f);
            mat.SetFloat("_HeightTransition", 0.22f);
            mat.EnableKeyword("_TERRAIN_BLEND_HEIGHT");
            mat.EnableKeyword("_NORMALMAP");
            mat.EnableKeyword("_MASKMAP");
            mat.SetFloat("_EnableInstancedPerPixelNormal", 1f);
            mat.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
            if (create) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>The three maps of a Poly Haven ground or wall texture, or nulls if it was not fetched.</summary>
        private static (Texture2D diff, Texture2D nor, Texture2D mask) PolyTexture(string id)
        {
            string d = $"{FPSKitTownAssets.PolyHavenRoot}/Textures/{id}/{id}";
            return (AssetDatabase.LoadAssetAtPath<Texture2D>(d + "_diff.jpg"),
                    AssetDatabase.LoadAssetAtPath<Texture2D>(d + "_nor.jpg"),
                    AssetDatabase.LoadAssetAtPath<Texture2D>(d + "_mask.png"));
        }

        /// <summary>
        /// The ground's paint, by rule. Every texel starts as open desert sand and takes, in order: dune sand on the
        /// dunes (more on the crests), packed earth on developed land with drifts of sand blown into it, gravel on the
        /// road shoulders and the citadel's slopes, irrigated soil on the farms, cracked mud round the oasis and in
        /// the low flats of the open desert. Noise at two scales breaks every edge.
        /// </summary>
        private static float[,,] TownSplat(int layers)
        {
            var a = new float[TownSplatRes, TownSplatRes, layers];
            float step = TownTerrainHalf * 2f / TownSplatRes;
            int s = _theme.randomSeed;
            var w = new float[layers];

            for (int iz = 0; iz < TownSplatRes; iz++)
            {
                float z = -TownTerrainHalf + (iz + 0.5f) * step;
                for (int ix = 0; ix < TownSplatRes; ix++)
                {
                    float x = -TownTerrainHalf + (ix + 0.5f) * step;
                    System.Array.Clear(w, 0, layers);
                    float dune = DuneWeight(x, z);
                    float n1 = Fbm2(x * 0.02f, z * 0.02f, s * 41 + 3, 3);      // ~50 m patches
                    float n2 = Fbm2(x * 0.11f, z * 0.11f, s * 43 + 7, 2);      // ~9 m breakup

                    // Open desert, and dune sand on the dunes (crests read paler and smoother).
                    float h = GroundHeightAt(x, z);
                    float crest = Mathf.Clamp01((h - SampleLocalMean(x, z)) * 0.25f + 0.5f);
                    w[LSand] = 1f - dune * 0.6f;
                    w[LDune] += dune * (0.4f + 0.8f * crest) + Mathf.Max(0f, n1) * dune * 0.4f;

                    // Developed land: packed earth, with sand drifted into it where the noise is high.
                    float town = 1f - dune;
                    float drift = Mathf.Clamp01(n1 * 1.6f + n2 * 0.6f);
                    w[LPacked] = town * (1f - drift * 0.7f);
                    w[LSand] += town * drift * 0.8f;

                    // Gravel shoulders: beside the highway, round every road's edge, and on the citadel's slope.
                    float shoulder = RoadShoulder(x, z);
                    w[LGravel] += shoulder * (0.8f + 0.4f * n2);
                    float dc = Vector2.Distance(new Vector2(x, z), CitadelCentre);
                    float slope = 1f - Mathf.Clamp01(Mathf.Abs(dc - (CitadelPlateau + CitadelFoot) * 0.5f) / ((CitadelFoot - CitadelPlateau) * 0.6f));
                    w[LGravel] += slope * (0.7f + 0.5f * n2);
                    if (dc < CitadelPlateau) w[LPacked] += 0.6f;

                    // Farms: dark irrigated soil inside the plots, cracked mud round the oasis water.
                    if (FarmsRect.Contains(new Vector2(x, z)))
                    {
                        float soil = FarmSoilAt(x, z);
                        w[LSoil] += soil * 2.2f;
                        w[LPacked] *= 1f - soil * 0.8f;
                    }
                    float d0 = Vector2.Distance(new Vector2(x, z), OasisCentre);
                    float ring = 1f - Mathf.Clamp01(Mathf.Abs(d0 - OasisRadius - 9f) / 14f);
                    w[LCracked] += ring * (1.4f + n2);
                    if (d0 < OasisRadius + 2f) w[LSoil] += 3f;

                    // Cracked flats in the low ground of the open desert.
                    float low = Mathf.Clamp01((SampleLocalMean(x, z) - h) * 0.6f);
                    w[LCracked] += dune * low * Mathf.Clamp01(n1 + 0.3f) * 1.2f;

                    // Tracks are worn gravel, not asphalt.
                    w[LGravel] += TrackWear(x, z) * (1.6f + n2);

                    float sum = 0f;
                    for (int k = 0; k < layers; k++) { w[k] = Mathf.Max(0f, w[k]); sum += w[k]; }
                    if (sum <= 0f) { w[LSand] = 1f; sum = 1f; }
                    for (int k = 0; k < layers; k++) a[iz, ix, k] = w[k] / sum;
                }
            }
            return a;
        }

        /// <summary>Average ground height over about 40 m, to tell a crest from a hollow.</summary>
        private static float SampleLocalMean(float x, float z)
        {
            const float r = 20f;
            return (GroundHeightAt(x + r, z) + GroundHeightAt(x - r, z) + GroundHeightAt(x, z + r) + GroundHeightAt(x, z - r)) * 0.25f;
        }

        /// <summary>1 on a road's gravel shoulder (just outside its asphalt or pavement), fading over a few metres.</summary>
        private static float RoadShoulder(float x, float z)
        {
            float best = 0f;
            foreach (var r in _townRoads)
            {
                if (!r.Paved) continue;
                float along = r.AlongX ? x : z, across = Mathf.Abs((r.AlongX ? z : x) - r.Fixed);
                if (along < r.Min - 2f || along > r.Max + 2f) continue;
                float edge = r.kind == RoadKind.Highway ? r.Half : r.Corridor;
                float width = r.kind == RoadKind.Highway ? 5f : 2.2f;
                float t = (across - edge) / width;
                if (t < -0.2f || t > 1f) continue;
                best = Mathf.Max(best, 1f - Mathf.Clamp01(t));
            }
            return best;
        }

        private static float TrackWear(float x, float z)
        {
            var p = new Vector2(x, z);
            float best = 0f;
            foreach (var r in _townRoads)
            {
                if (r.Paved) continue;
                float along = r.AlongX ? x : z, across = Mathf.Abs((r.AlongX ? z : x) - r.Fixed);
                if (along < r.Min || along > r.Max) continue;
                best = Mathf.Max(best, 1f - Mathf.Clamp01((across - r.Half + 1f) / 2.5f));
            }
            foreach (var t in _townTracks)
                for (int k = 0; k + 1 < t.points.Length; k++)
                    best = Mathf.Max(best, 1f - Mathf.Clamp01((DistanceToSegment(p, t.points[k], t.points[k + 1]) - t.width * 0.5f + 1f) / 2.5f));
            return best;
        }

        // ==================================================================
        // Beyond the terrain
        // ==================================================================
        /// <summary>Flat sand from the terrain's edge to the horizon, and the highway running on across it.</summary>
        private static void BuildTownApron(Transform root, int backdrop)
        {
            var sand = TownMat("ApronSandPale", "gravelly_sand", new Color(0.92f, 0.95f, 1.08f), 18f);
            var asphalt = TownMat("ApronAsphalt", "asphalt_02", new Color(0.92f, 0.9f, 0.88f), 6f);
            var m = new TownMesh();
            float inner = TownTerrainHalf - 0.5f, outer = 3600f;
            var up = Vector3.up;
            float y = TownApronY - 0.05f;
            // Four slabs around the terrain.
            m.Quad(sand, new Vector3(-outer, y, inner), new Vector3(outer, y, inner), new Vector3(outer, y, outer), new Vector3(-outer, y, outer), up);
            m.Quad(sand, new Vector3(-outer, y, -outer), new Vector3(outer, y, -outer), new Vector3(outer, y, -inner), new Vector3(-outer, y, -inner), up);
            m.Quad(sand, new Vector3(-outer, y, -inner), new Vector3(-inner, y, -inner), new Vector3(-inner, y, inner), new Vector3(-outer, y, inner), up);
            m.Quad(sand, new Vector3(inner, y, -inner), new Vector3(outer, y, -inner), new Vector3(outer, y, inner), new Vector3(inner, y, inner), up);
            // The highway out to the horizon, both ways.
            foreach (float sgn in new[] { -1f, 1f })
            {
                float x0 = sgn * (TownTerrainHalf - 1f), x1 = sgn * outer;
                m.Quad(asphalt, new Vector3(x0, TownApronY + 0.02f, -407f), new Vector3(x1, TownApronY + 0.02f, -407f),
                       new Vector3(x1, TownApronY + 0.02f, -393f), new Vector3(x0, TownApronY + 0.02f, -393f), up);
            }
            var go = m.Build(root, "Apron", backdrop, null, collider: false, shadows: ShadowCastingMode.Off);
            if (go != null) Hide(go);
        }

        /// <summary>
        /// The far mountains: seven massifs from real eroded heightmaps (Tools/town/make_backdrop.py), stood round the
        /// horizon from 1.3 to 2.5 km out. No colliders; the haze does the rest.
        /// </summary>
        private static void BuildTownMountains(Transform root, int backdrop)
        {
            string dir = FPSKitTownAssets.Out + "/Heights";
            if (!AssetDatabase.IsValidFolder(dir)) return;
            var group = new GameObject("Mountains").transform;
            group.SetParent(root, false);
            // Darker than the sand and a little cooler, so a massif reads as rock through the haze and not as a big dune.
            var mat = TownMat("Mountain", "rock_ground", new Color(0.62f, 0.50f, 0.42f), 26f);
            var place = new (string name, float angle, float dist, float width, float height, float yaw)[]
            {
                ("MonumentDesert02", 20f, 1700f, 1300f, 230f, 30f),
                ("TerracedCliffs06", 75f, 2100f, 1100f, 260f, 0f),
                ("LayeredRocks15", 130f, 1500f, 900f, 150f, 80f),
                ("RockyPlateaus01", 185f, 2300f, 1600f, 200f, 10f),
                ("Canyon18", 240f, 1900f, 1400f, 170f, 50f),
                ("MassiveBlocks02", 295f, 1600f, 800f, 140f, 120f),
                ("PrideRocks02", 340f, 2400f, 1500f, 210f, 200f),
            };
            foreach (var p in place)
            {
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>($"{dir}/{p.name}.bytes");
                if (asset == null) continue;
                var mesh = HeightmapMesh(asset.bytes, p.name, p.width, p.height);
                float a = p.angle * Mathf.Deg2Rad;
                var pos = new Vector3(Mathf.Sin(a) * p.dist, TownApronY - 6f, Mathf.Cos(a) * p.dist);
                var go = MeshObject(group, p.name, mesh, mat, pos, Quaternion.Euler(0f, p.yaw, 0f), Vector3.one, backdrop, null, collider: false);
                go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                Hide(go);
            }
        }

        private static Mesh HeightmapMesh(byte[] bytes, string name, float width, float height)
        {
            int n = Mathf.RoundToInt(Mathf.Sqrt(bytes.Length / 2));
            var v = new Vector3[n * n];
            var uv = new Vector2[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int k = y * n + x;
                    float h = (bytes[k * 2] | (bytes[k * 2 + 1] << 8)) / 65535f;
                    float px = (x / (float)(n - 1) - 0.5f) * width, pz = (y / (float)(n - 1) - 0.5f) * width;
                    v[k] = new Vector3(px, h * height, pz);
                    uv[k] = new Vector2(px, pz) / 1f;
                }
            var t = new int[(n - 1) * (n - 1) * 6];
            int c = 0;
            for (int y = 0; y + 1 < n; y++)
                for (int x = 0; x + 1 < n; x++)
                {
                    int i0 = y * n + x, i1 = i0 + 1, i2 = i0 + n, i3 = i2 + 1;
                    t[c++] = i0; t[c++] = i2; t[c++] = i3;
                    t[c++] = i0; t[c++] = i3; t[c++] = i1;
                }
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = v; mesh.uv = uv; mesh.triangles = t;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The arena's edge: four walls on their own layer that only the player's body collides with (bullets, bombs,
        /// enemies and civilians are unaffected), standing on the sand ridge the heights put there.
        /// </summary>
        private static void BuildTownBounds(Transform root)
        {
            int bounds = LayerMask.NameToLayer("PlayerBounds");
            if (bounds < 0) return;
            var group = new GameObject("PlayerBounds").transform;
            group.SetParent(root, false);
            float half = _theme.arenaSize * 0.5f;
            for (int side = 0; side < 4; side++)
            {
                var go = new GameObject($"Bound_{side}");
                go.transform.SetParent(group, false);
                go.layer = bounds;
                bool alongZ = side >= 2;
                float sgn = side % 2 == 0 ? 1f : -1f;
                go.transform.localPosition = alongZ ? new Vector3(sgn * (half + 1f), 40f, 0f) : new Vector3(0f, 40f, sgn * (half + 1f));
                var box = go.AddComponent<BoxCollider>();
                box.size = alongZ ? new Vector3(2f, 200f, half * 2f + 4f) : new Vector3(half * 2f + 4f, 200f, 2f);
            }
        }

        // ==================================================================
        // Sky
        // ==================================================================
        /// <summary>The Poly Haven sky on Skybox/Panoramic (no mipmaps, see ConfigureSky), or null if it was not fetched.</summary>
        private static Material TownSky()
        {
            string hdr = null;
            foreach (var p in FPSKitTownAssets.SkyPaths()) { hdr = p; break; }
            if (hdr == null) return null;
            // Drawn from the clamped copy (the raw sun disc blew out every reflection probe that caught it); the raw HDR
            // is still what the sun is aimed from.
            var tex = FPSKitTownAssets.DisplaySky() ?? AssetDatabase.LoadAssetAtPath<Texture2D>(hdr);
            if (tex == null) return null;
            string path = $"{FPSKitTownAssets.MatFolder}/TownSky.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = mat == null;
            if (create) mat = new Material(Shader.Find("Skybox/Panoramic"));
            mat.SetTexture("_MainTex", tex);
            mat.SetFloat("_Mapping", 1f);          // latitude-longitude
            mat.SetFloat("_ImageType", 0f);        // 360 degrees
            mat.SetFloat("_MirrorOnBack", 0f);
            mat.SetFloat("_Layout", 0f);
            mat.SetFloat("_Rotation", TownSkyRotation);
            mat.SetFloat("_Exposure", 1.0f);
            mat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            if (create) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            // Reflections from the same sky (saved with the scene's lighting settings).
            var cube = FPSKitTownAssets.ReflectionCube();
            if (cube != null)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cube;
                RenderSettings.reflectionIntensity = 1f;
            }
            return mat;
        }

        /// <summary>Which way the light points, as a yaw: 45 degrees to the street grid (from the south-west), so on every
        /// street one side stands in shadow and the other in sun.</summary>
        private const float TownSunYaw = 45f;

        /// <summary>
        /// Degrees the sky is turned so its own sun stands at <see cref="TownSunYaw"/>: found from the sky's brightest
        /// pixel, so the light, the shadows and the sun in the sky agree. Cached per build (cleared in ResetTown).
        /// </summary>
        private static float TownSkyRotation
        {
            get
            {
                if (!float.IsNaN(_townSkyRotation)) return _townSkyRotation;
                _townSkyRotation = 0f;
                foreach (var p in FPSKitTownAssets.SkyPaths())
                {
                    var d = HdrSunDirection(p, 0f);
                    if (d != null) _townSkyRotation = Mathf.Repeat(Mathf.Atan2(-d.Value.x, -d.Value.z) * Mathf.Rad2Deg - TownSunYaw, 360f);
                    break;
                }
                return _townSkyRotation;
            }
        }
        private static float _townSkyRotation = float.NaN;

        /// <summary>
        /// Where the sun is in a Radiance .hdr sky, as a world direction towards it under Skybox/Panoramic with the
        /// given rotation: the brightest pixel, mapped the way the shader maps a direction to a latitude-longitude
        /// coordinate (u = 0.5 - atan2(z, x) / 2pi, v = 1 - acos(y) / pi, after turning by the rotation). Null if the
        /// file cannot be read.
        /// </summary>
        private static Vector3? HdrSunDirection(string path, float rotationDegrees)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                int pos = 0;
                string Line()
                {
                    int start = pos;
                    while (pos < bytes.Length && bytes[pos] != (byte)'\n') pos++;
                    var s = System.Text.Encoding.ASCII.GetString(bytes, start, pos - start);
                    pos++;
                    return s;
                }
                string line;
                do { line = Line(); } while (line.Length > 0 && pos < bytes.Length);
                var dims = Line().Split(' ');               // "-Y H +X W"
                int H = int.Parse(dims[1]), W = int.Parse(dims[3]);
                var row = new byte[W * 4];
                float best = -1f; int bx = 0, by = 0;
                for (int y = 0; y < H; y++)
                {
                    if (bytes[pos] == 2 && bytes[pos + 1] == 2)
                    {
                        pos += 4;
                        for (int ch = 0; ch < 4; ch++)
                        {
                            int x = 0;
                            while (x < W)
                            {
                                int count = bytes[pos++];
                                if (count > 128) { count -= 128; byte val = bytes[pos++]; for (int k = 0; k < count; k++) row[(x++) * 4 + ch] = val; }
                                else for (int k = 0; k < count; k++) row[(x++) * 4 + ch] = bytes[pos++];
                            }
                        }
                    }
                    else { System.Buffer.BlockCopy(bytes, pos, row, 0, W * 4); pos += W * 4; }
                    for (int x = 0; x < W; x++)
                    {
                        int e = row[x * 4 + 3];
                        if (e == 0) continue;
                        float f = Mathf.Pow(2f, e - 136);
                        float lum = (row[x * 4] * 0.3f + row[x * 4 + 1] * 0.59f + row[x * 4 + 2] * 0.11f) * f;
                        if (lum > best) { best = lum; bx = x; by = y; }
                    }
                }
                float u = (bx + 0.5f) / W, v = 1f - (by + 0.5f) / H;
                float lat = (1f - v) * Mathf.PI, lon = (0.5f - u) * Mathf.PI * 2f;
                var sky = new Vector3(Mathf.Sin(lat) * Mathf.Cos(lon), Mathf.Cos(lat), Mathf.Sin(lat) * Mathf.Sin(lon));
                // The shader turns the view direction by +rotation before looking it up, so the world direction is turned back.
                return Quaternion.Euler(0f, -rotationDegrees, 0f) * sky;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[FPSKit] could not read the sun from {path}: {e.Message}");
                return null;
            }
        }

        /// <summary>Points the sun at where the sky's own sun is, so the shadows agree with the sky.</summary>
        private static void AimTownSun(Light sun)
        {
            if (sun == null) return;
            foreach (var p in FPSKitTownAssets.SkyPaths())
            {
                var dir = HdrSunDirection(p, TownSkyRotation);
                if (dir == null) return;
                var d = dir.Value.normalized;
                // A sun at the horizon lights nothing; hold it at a minimum, in case a lower sky is ever chosen.
                float el = Mathf.Max(Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg, 24f);
                float az = Mathf.Atan2(-d.x, -d.z) * Mathf.Rad2Deg;
                sun.transform.rotation = Quaternion.Euler(el, az, 0f);
                Debug.Log($"[FPSKit] town sun from the sky: elevation {Mathf.Asin(d.y) * Mathf.Rad2Deg:0.0} (lit at {el:0.0}), azimuth {az:0.0}");
                return;
            }
        }

        // ==================================================================
        // Materials
        // ==================================================================
        private static readonly Dictionary<string, Material> _townMats = new Dictionary<string, Material>();

        /// <summary>
        /// A URP Lit material from a Poly Haven texture, tinted, tiled at <paramref name="tileMetres"/> per repeat
        /// (meshes here carry metre UVs). Falls back to a flat colour if the texture was never fetched.
        /// </summary>
        // Smoothness scales the scan's own (from its roughness map). Low by default: plaster, stone, earth and asphalt in a dry
        // climate are matte, and with the sky reflecting (the HDRI cubemap) anything higher looks wet.
        private static Material TownMat(string name, string tex, Color tint, float tileMetres, float smoothness = 0.35f)
        {
            string key = $"{name}|{tex}|{tileMetres}";
            if (_townMats.TryGetValue(key, out var cached) && cached != null) return cached;
            var (diff, nor, mask) = PolyTexture(tex);
            string path = $"{FPSKitTownAssets.MatFolder}/Town_{name}.mat";
            Material m;
            if (diff == null)
            {
                m = MakeMaterialAt(path, tint * 0.75f, 0.1f, 0f);
            }
            else
                m = FPSKitTownAssets.LitFromMaps(path, diff, nor, mask, tint, Vector2.one / Mathf.Max(0.1f, tileMetres), false, smoothness);
            _townMats[key] = m;
            return m;
        }

        /// <summary>A flat URP Lit colour for the town (glass, paint, metal), cached by name.</summary>
        private static Material TownFlat(string name, Color c, float smoothness, float metallic = 0f, bool transparent = false)
        {
            string key = "flat|" + name;
            if (_townMats.TryGetValue(key, out var cached) && cached != null) return cached;
            string path = $"{FPSKitTownAssets.MatFolder}/Town_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = m == null;
            if (create) m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (transparent)
            {
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 1f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetFloat("_SrcBlend", 1f); m.SetFloat("_DstBlend", 10f); m.SetFloat("_ZWrite", 0f);
            }
            m.enableInstancing = true;
            if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            _townMats[key] = m;
            return m;
        }
    }
}
#endif
