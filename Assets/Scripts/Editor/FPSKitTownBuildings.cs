#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The town's buildings: every block of the plan cut into lots, every lot a building of the kind its street
    /// calls for.
    ///
    ///   * On the avenue, the boulevard and the cross streets: apartment blocks of four to six floors with shops on
    ///     the ground floor -- roller shutters (some up, some down, some half), Arabic signs, awnings, goods out on
    ///     the pavement -- and balconies with split air-conditioners above.
    ///   * On the residential streets: walled villas with a gate and a yard, and row houses built to the pavement.
    ///   * In the old town and its western quarter: mud-plastered and stone houses of one to three floors in a
    ///     tangle of alleys, timber shutters, palm-beam ends under the roof, courtyard houses where a lot is big
    ///     enough, and the odd ruin.
    ///   * On every flat roof: water tanks, satellite dishes, solar heaters, a stair bulkhead on the blocks, and on
    ///     the unfinished ones rebar standing out of the top -- the single most recognisable thing on a skyline here.
    ///
    /// A third of the houses can be entered: an open door, furnished rooms, and an outside stair to a walkable roof.
    /// The rest are solid and held off the navigation bake by a volume over their footprint (the old desert's
    /// trapped-enemy bug: a closed box bakes an inside). Geometry is merged per 64 m chunk, a submesh per material.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private enum LotKind { Apartment, Commercial, Villa, Row, ShopRow, Vacant, OldHouse, Courtyard, Ruin, Compound }

        private sealed class TownLot
        {
            public Rect r;
            public int front;                 // 0 N(+z), 1 S(-z), 2 E(+x), 3 W(-x): the side facing the street
            public LotKind kind;
            public bool mainStreet;
        }

        private const float TownFloorH = 3.2f;
        private static readonly List<TownLot> _townLots = new List<TownLot>();
        private static readonly Dictionary<Vector2Int, TownMesh> _chunks = new Dictionary<Vector2Int, TownMesh>();
        private static Transform _volumes;

        /// <summary>Doors of enterable buildings and yard gates: where civilians go in and out (FPSKitTownLife).</summary>
        private static readonly List<Vector3> _townDoors = new List<Vector3>();
        /// <summary>Spots for a parked car inside a villa's yard, facing its gate: (position, yaw).</summary>
        private static readonly List<Vector4> _yardCars = new List<Vector4>();
        /// <summary>Open shop fronts: (centre on the pavement, yaw facing the street) for goods and shopkeepers.</summary>
        private static readonly List<Vector4> _shopFronts = new List<Vector4>();

        private static void ResetTownBuildings()
        {
            _townLots.Clear(); _chunks.Clear(); _townDoors.Clear(); _yardCars.Clear(); _shopFronts.Clear();
            _volumes = null; _signNames = null; _roadSignNames = null;
            _oldSquares.Clear(); _vacantLots.Clear();
        }

        private static TownMesh Chunk(Vector2 p)
        {
            var key = new Vector2Int(Mathf.FloorToInt(p.x / 64f), Mathf.FloorToInt(p.y / 64f));
            if (!_chunks.TryGetValue(key, out var m)) { m = new TownMesh(); _chunks[key] = m; }
            return m;
        }

        // ==================================================================
        // Materials
        // ==================================================================
        private static Material[] _plasters, _oldPlasters;
        private static Material _plasterIn, _band, _block, _roof, _coping, _terrazzo, _stoneAshlar;
        private static Material _glassDark, _glassBlue, _glassCurtain, _glassBlinds, _glassClear;
        private static Material _frameAlu, _frameWhite, _frameDark, _steel, _rust;
        private static Material _doorWood, _doorMetal, _gateGreen, _gateBlue, _shutterPaint, _shutterRust, _woodShutter, _woodBeam;
        private static Material _tankBlack, _tankWhite, _tankBlue, _dish, _solar, _shopDark, _townSignMat, _awningMat, _roadSignMat, _clothMat;

        private static void ResolveBuildingMaterials()
        {
            // No white_plaster_rough_01/02 or concrete_wall_008: in spite of the names they are green with algae and read as
            // moss on a desert wall.
            _plasters = new[]
            {
                TownMat("PlasterWhite", "plastered_wall_02", new Color(1.02f, 1.00f, 0.97f), 3f),
                TownMat("PlasterStucco", "white_stucco", new Color(0.98f, 0.95f, 0.90f), 3f),
                TownMat("PlasterBeige", "beige_wall_002", new Color(1.00f, 0.96f, 0.90f), 3f),
                TownMat("PlasterCream", "painted_plaster_wall", new Color(1.00f, 0.95f, 0.86f), 3f),
                TownMat("PlasterOchre", "yellow_plaster", new Color(1.00f, 0.93f, 0.82f), 3f),
                TownMat("PlasterWorn", "worn_plaster_wall", new Color(1.00f, 0.96f, 0.90f), 3.5f),
                TownMat("PlasterGrey", "plastered_wall_02", new Color(0.98f, 0.96f, 0.92f), 3f),
                TownMat("PlasterSand", "beige_wall_002", new Color(1.04f, 0.97f, 0.86f), 3f),
                TownMat("PlasterPeach", "painted_plaster_wall", new Color(1.00f, 0.86f, 0.74f), 3f),
                TownMat("PlasterMint", "white_stucco", new Color(0.86f, 0.95f, 0.88f), 3f),
                TownMat("PlasterSky", "white_stucco", new Color(0.84f, 0.92f, 0.98f), 3f),
            };
            _oldPlasters = new[]
            {
                TownMat("MudPlaster", "clay_plaster", new Color(1.00f, 0.95f, 0.88f), 3.5f),
                TownMat("MudPattern", "patterned_clay_plaster", new Color(1.00f, 0.96f, 0.90f), 3.5f),
                TownMat("Limestone", "white_sandstone_bricks", new Color(1.00f, 0.95f, 0.86f), 3.5f),
                TownMat("MudDark", "clay_plaster", new Color(0.88f, 0.78f, 0.66f), 3.5f),
                TownMat("PlasterWornOld", "damaged_plaster", new Color(1.00f, 0.95f, 0.88f), 3.5f),
            };
            _plasterIn = TownMat("PlasterInterior", "painted_plaster_wall", new Color(1.00f, 0.98f, 0.95f), 2.5f);
            _band = TownMat("ConcreteBand", "plastered_wall_02", new Color(0.90f, 0.87f, 0.82f), 2f);
            _block = TownMat("CinderBlock", "concrete_block_wall", new Color(1.35f, 1.30f, 1.22f), 2.4f);
            _roof = TownMat("RoofConcrete", "plastered_wall_02", new Color(0.86f, 0.82f, 0.76f), 4f);
            _coping = TownMat("Coping", "plastered_wall_02", new Color(0.96f, 0.93f, 0.88f), 1.5f);
            _terrazzo = TownMat("Terrazzo", "terrazzo_tiles", new Color(1.0f, 0.98f, 0.95f), 2f);
            _stoneAshlar = TownMat("Ashlar", "white_sandstone_bricks", new Color(1.02f, 0.94f, 0.82f), 3f);

            _glassDark = TownFlat("GlassDark", new Color(0.05f, 0.06f, 0.07f), 0.94f, 0.1f);
            _glassBlue = TownFlat("GlassBlue", new Color(0.07f, 0.10f, 0.13f), 0.94f, 0.15f);
            _glassCurtain = TownFlat("GlassCurtain", new Color(0.52f, 0.45f, 0.36f), 0.55f);
            _glassBlinds = TownFlat("GlassBlinds", new Color(0.62f, 0.62f, 0.60f), 0.5f);
            _glassClear = TownFlat("GlassClear", new Color(0.55f, 0.62f, 0.66f, 0.18f), 0.95f, 0f, transparent: true);
            _frameAlu = TownFlat("FrameAluminium", new Color(0.72f, 0.72f, 0.71f), 0.55f, 0.8f);
            _frameWhite = TownFlat("FrameWhite", new Color(0.86f, 0.86f, 0.84f), 0.35f);
            _frameDark = TownFlat("FrameDark", new Color(0.18f, 0.15f, 0.13f), 0.35f, 0.3f);
            _steel = TownFlat("SteelDark", new Color(0.12f, 0.12f, 0.12f), 0.4f, 0.7f);
            _rust = TownFlat("Rebar", new Color(0.30f, 0.17f, 0.10f), 0.25f, 0.6f);
            _doorWood = TownMat("DoorWood", "rough_pine_door", new Color(0.86f, 0.74f, 0.60f), 2.2f);
            _doorMetal = TownMat("DoorMetal", "painted_metal_shutter", new Color(0.55f, 0.36f, 0.26f), 2f);
            _gateGreen = TownMat("GateGreen", "painted_metal_shutter", new Color(0.30f, 0.52f, 0.40f), 2f);
            _gateBlue = TownMat("GateBlue", "painted_metal_shutter", new Color(0.32f, 0.44f, 0.62f), 2f);
            _shutterPaint = TownMat("ShopShutter", "painted_metal_shutter", new Color(0.86f, 0.86f, 0.84f), 2.5f);
            _shutterRust = TownMat("ShopShutterRust", "rusted_shutter", new Color(1f, 0.96f, 0.92f), 2.5f);
            _woodShutter = TownMat("WoodShutter", "wood_shutter", new Color(0.70f, 0.82f, 0.78f), 1.6f);
            _woodBeam = TownMat("PalmBeam", "weathered_planks", new Color(0.62f, 0.52f, 0.42f), 1.2f);
            _tankBlack = TownFlat("TankBlack", new Color(0.07f, 0.07f, 0.07f), 0.35f);
            _tankWhite = TownFlat("TankWhite", new Color(0.82f, 0.82f, 0.80f), 0.3f);
            _tankBlue = TownFlat("TankBlue", new Color(0.12f, 0.30f, 0.55f), 0.35f);
            _dish = TownFlat("DishWhite", new Color(0.86f, 0.86f, 0.84f), 0.4f, 0.2f);
            _solar = TownFlat("SolarPanel", new Color(0.05f, 0.08f, 0.14f), 0.9f, 0.3f);
            _shopDark = TownFlat("ShopInterior", new Color(0.16f, 0.14f, 0.12f), 0.2f);
            _clothMat = TownFlat("Laundry", new Color(0.85f, 0.85f, 0.82f), 0.1f);
            _townSignMat = UnlitAtlas("TownSigns", "TownSigns.png", 0.92f);
            _roadSignMat = UnlitAtlas("TownRoadSigns", "TownRoadSigns.png", 0.92f);
            _awningMat = UnlitAtlas("TownAwnings", "TownAwnings.png", 1f, lit: true);
        }

        /// <summary>A material on one of the generated atlases (Tools/town/make_signs.py). Lit so signs sit in the light like paint does.</summary>
        private static Material UnlitAtlas(string name, string file, float brightness, bool lit = true)
        {
            string tex = $"{FPSKitTownAssets.Out}/Textures/{file}";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(tex);
            if (AssetImporter.GetAtPath(tex) is TextureImporter imp && (imp.mipmapEnabled == false || imp.anisoLevel < 4 || imp.wrapMode != TextureWrapMode.Clamp))
            {
                imp.mipmapEnabled = true; imp.anisoLevel = 4; imp.wrapMode = TextureWrapMode.Clamp; imp.SaveAndReimport();
                t = AssetDatabase.LoadAssetAtPath<Texture2D>(tex);
            }
            string path = $"{FPSKitTownAssets.MatFolder}/Town_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = m == null;
            if (create) m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetTexture("_BaseMap", t);
            m.SetColor("_BaseColor", Color.white * brightness);
            m.SetFloat("_Smoothness", 0.3f);
            m.SetFloat("_Metallic", 0f);
            if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        private static string[] _signNames, _roadSignNames;

        /// <summary>The UV rectangle of a named cell on the shop-sign atlas (4 x 16 cells of 512 x 128).</summary>
        private static Rect SignCell(string name)
        {
            _signNames ??= ReadNames("TownSigns.txt");
            int k = System.Array.IndexOf(_signNames, name);
            if (k < 0) k = 0;
            return new Rect((k % 4) / 4f, 1f - (k / 4 + 1) / 16f, 0.25f, 1f / 16f);
        }

        /// <summary>The UV rectangle of a road-sign cell (4 x 4 of 256 x 256).</summary>
        private static Rect RoadSignCell(string name)
        {
            _roadSignNames ??= ReadNames("TownRoadSigns.txt");
            int k = System.Array.IndexOf(_roadSignNames, name);
            if (k < 0) k = 0;
            return new Rect((k % 4) / 4f, 1f - (k / 4 + 1) / 4f, 0.25f, 0.25f);
        }

        private static string[] ReadNames(string file)
        {
            var t = AssetDatabase.LoadAssetAtPath<TextAsset>($"{FPSKitTownAssets.Out}/Textures/{file}");
            if (t == null) { string p = $"{FPSKitTownAssets.Out}/Textures/{file}"; if (System.IO.File.Exists(p)) return System.IO.File.ReadAllLines(p); return new string[0]; }
            return t.text.Split('\n');
        }

        private static readonly string[] ShopSigns =
        {
            "sh_pharmacy", "sh_bakery", "sh_grocery", "sh_restaurant", "sh_cafe", "sh_barber", "sh_phones", "sh_clothing",
            "sh_gold", "sh_perfume", "sh_butcher", "sh_fruit", "sh_books", "sh_bank", "sh_clinic", "sh_sweets",
            "sh_spices", "sh_fabrics", "sh_housewares", "sh_electricals", "sh_laundry", "sh_shawarma", "sh_juice",
            "sh_supermarket", "sh_carpentry", "sh_gas", "sh_tailor", "sh_hotel",
        };

        // ==================================================================
        // Build
        // ==================================================================
        private static void BuildTownBuildings(Transform root, int layer, System.Random rng)
        {
            ResolveBuildingMaterials();
            var group = new GameObject("Buildings").transform;
            group.SetParent(root, false);
            _volumes = new GameObject("NoEntry").transform;
            _volumes.SetParent(group, false);
            var props = new GameObject("BuildingProps").transform;
            props.SetParent(group, false);

            PlanGridLots(rng);
            PlanOldTownLots(rng);

            int enterable = 0, solid = 0;
            foreach (var lot in _townLots)
            {
                switch (lot.kind)
                {
                    case LotKind.Apartment: BuildApartment(lot, rng, props); solid++; break;
                    case LotKind.Commercial: BuildCommercial(lot, rng, props); solid++; break;
                    case LotKind.Villa: if (BuildVilla(lot, rng, props)) enterable++; else solid++; break;
                    case LotKind.Row: if (BuildRow(lot, rng, props)) enterable++; else solid++; break;
                    case LotKind.ShopRow: BuildCommercial(lot, rng, props, floors: 1); solid++; break;
                    case LotKind.Vacant: BuildVacant(lot, rng, props); break;
                    case LotKind.OldHouse: if (BuildOldHouse(lot, rng, props)) enterable++; else solid++; break;
                    case LotKind.Courtyard: BuildCourtyardHouse(lot, rng, props); solid++; break;
                    case LotKind.Ruin: BuildRuin(lot, rng); break;
                    case LotKind.Compound: BuildCompound(lot, rng, props); solid++; break;
                }
            }

            foreach (var kv in _chunks)
            {
                var go = kv.Value.Build(group, $"Block_{kv.Key.x}_{kv.Key.y}", layer, "Concrete");
                _ = go;
            }
            Debug.Log($"[FPSKit] town buildings: {_townLots.Count} lots, {solid} solid, {enterable} enterable, {_chunks.Count} chunks");
        }

        // ==================================================================
        // Lots: the grid districts
        // ==================================================================
        private static void PlanGridLots(System.Random rng)
        {
            // The grid districts, by the roads that bound them. Each consecutive pair of parallel roads makes a band;
            // each band of N-S roads crossed with a band of E-W roads is a block.
            var east = new { xs = new[] { 0f, 130f, 230f, 330f, 440f }, zs = new[] { -400f, -260f, -130f, 20f, 110f, 200f } };
            var west = new { xs = new[] { -470f, -360f, -240f, -120f, 0f }, zs = new[] { -400f, -260f, -130f } };
            foreach (var g in new[] { east, west })
                for (int i = 0; i + 1 < g.xs.Length; i++)
                    for (int j = 0; j + 1 < g.zs.Length; j++)
                    {
                        float x0 = g.xs[i], x1 = g.xs[i + 1], z0 = g.zs[j], z1 = g.zs[j + 1];
                        var rW = RoadAlong(false, x0, (z0 + z1) * 0.5f); var rE = RoadAlong(false, x1, (z0 + z1) * 0.5f);
                        var rS = RoadAlong(true, z0, (x0 + x1) * 0.5f); var rN = RoadAlong(true, z1, (x0 + x1) * 0.5f);
                        float bx0 = x0 + Edge(rW), bx1 = x1 - Edge(rE), bz0 = z0 + Edge(rS, z0 < -390f ? 8f : 0f), bz1 = z1 - Edge(rN);
                        if (rN == null) bz1 = Mathf.Min(z1, 196f);
                        LotsForBlock(Rect.MinMaxRect(bx0, bz0, bx1, bz1), rN, rS, rE, rW, rng);
                    }

            // Between the avenue and the palace lane, south of the old town: a commercial strip.
            LotsForBlock(Rect.MinMaxRect(-284f, -122f, -13f, -36f), null, RoadAlong(true, -130f, -150f), RoadAlong(false, 0f, -80f), RoadAlong(false, -290f, -80f), rng);
        }

        private static float Edge(TownRoad r, float extra = 0f) => r == null ? 0.5f : r.Corridor + 0.4f + extra;

        /// <summary>The road along x (or along z) at that fixed coordinate whose span covers <paramref name="at"/>.</summary>
        private static TownRoad RoadAlong(bool alongX, float fixedAt, float at)
        {
            foreach (var r in _townRoads)
                if (r.AlongX == alongX && Mathf.Abs(r.Fixed - fixedAt) < 0.5f && at >= r.Min - 1f && at <= r.Max + 1f) return r;
            return null;
        }

        private static bool IsMain(TownRoad r) => r != null && (r.kind == RoadKind.Avenue || r.kind == RoadKind.Boulevard ||
                                                    r.name == "WestRoad" || r.name == "SouthStreet" || r.kind == RoadKind.Highway);

        private static void LotsForBlock(Rect blk, TownRoad rN, TownRoad rS, TownRoad rE, TownRoad rW, System.Random rng)
        {
            if (blk.width < 14f || blk.height < 14f) return;
            float H = blk.height, W = blk.width;
            float dN = rN != null ? Mathf.Min(rng.Next(24, 33), H * 0.5f - 0.5f) : 0f;
            float dS = rS != null ? Mathf.Min(rng.Next(24, 33), H * 0.5f - 0.5f) : 0f;
            if (rN == null && rS == null) { dS = Mathf.Min(30f, H); }

            void Row(float a, float b, float depth, int side, TownRoad road)
            {
                bool main = IsMain(road);
                float cur = a;
                while (b - cur >= 10f)
                {
                    float f = main ? Rand(rng, 16f, 26f) : Rand(rng, 12f, 20f);
                    if (b - (cur + f) < 10f) f = b - cur;
                    Rect r;
                    switch (side)
                    {
                        case 0: r = Rect.MinMaxRect(cur, blk.yMax - depth, cur + f, blk.yMax); break;
                        case 1: r = Rect.MinMaxRect(cur, blk.yMin, cur + f, blk.yMin + depth); break;
                        case 2: r = Rect.MinMaxRect(blk.xMax - depth, cur, blk.xMax, cur + f); break;
                        default: r = Rect.MinMaxRect(blk.xMin, cur, blk.xMin + depth, cur + f); break;
                    }
                    cur += f;
                    if (InReserved(r) || !ClearOfRoads(r, 0.2f)) continue;
                    _townLots.Add(new TownLot { r = r, front = side, mainStreet = main, kind = PickKind(main, r, rng) });
                }
            }

            if (dN > 0f) Row(blk.xMin, blk.xMax, dN, 0, rN);
            if (dS > 0f) Row(blk.xMin, blk.xMax, dS, 1, rS ?? rN);
            float mz0 = blk.yMin + dS, mz1 = blk.yMax - dN;
            if (mz1 - mz0 >= 12f)
            {
                float dE = rE != null ? Mathf.Min(rng.Next(22, 30), W * 0.5f - 0.5f) : 0f;
                float dW = rW != null ? Mathf.Min(rng.Next(22, 30), W * 0.5f - 0.5f) : 0f;
                var mid = Rect.MinMaxRect(blk.xMin, mz0, blk.xMax, mz1);
                var saved = blk; blk = mid;
                if (dE > 0f) Row(mz0, mz1, dE, 2, rE);
                if (dW > 0f) Row(mz0, mz1, dW, 3, rW);
                blk = saved;
            }
        }

        private static LotKind PickKind(bool main, Rect r, System.Random rng)
        {
            double p = rng.NextDouble();
            if (main) return p < 0.66 ? LotKind.Apartment : p < 0.90 ? LotKind.Commercial : LotKind.Vacant;
            // The south-west quarter is older and lower than the east.
            bool sw = r.center.x < 0f;
            if (sw) return p < 0.50 ? LotKind.Villa : p < 0.80 ? LotKind.Row : p < 0.90 ? LotKind.ShopRow : LotKind.Vacant;
            return p < 0.55 ? LotKind.Villa : p < 0.85 ? LotKind.Row : p < 0.93 ? LotKind.ShopRow : LotKind.Vacant;
        }

        // ==================================================================
        // Lots: the old town
        // ==================================================================
        /// <summary>
        /// The old town's alleys: a grid jittered at every crossing so no alley runs straight for long, four to five
        /// metres wide, with the souk's covered street through the middle and the bazaar gate's forecourt kept open.
        /// Each block between alleys is cut into houses of eight to sixteen metres.
        /// </summary>
        private static void PlanOldTownLots(System.Random rng)
        {
            foreach (var area in new[] { OldTownRect, OldTownWestRect })
            {
                bool west = area == OldTownWestRect;
                var xs = new List<float> { area.xMin };
                while (xs[xs.Count - 1] < area.xMax - 20f) xs.Add(xs[xs.Count - 1] + Rand(rng, west ? 40f : 30f, west ? 56f : 42f));
                xs[xs.Count - 1] = area.xMax;
                var zs = new List<float> { area.yMin };
                while (zs[zs.Count - 1] < area.yMax - 20f) zs.Add(zs[zs.Count - 1] + Rand(rng, west ? 38f : 28f, west ? 52f : 40f));
                zs[zs.Count - 1] = area.yMax;

                for (int i = 0; i + 1 < xs.Count; i++)
                    for (int j = 0; j + 1 < zs.Count; j++)
                    {
                        float aw = Rand(rng, 4f, 5.2f) * 0.5f;
                        var cell = Rect.MinMaxRect(xs[i] + (i == 0 ? 0.5f : aw), zs[j] + (j == 0 ? 0.5f : aw),
                                                   xs[i + 1] - (i + 1 == xs.Count - 1 ? 0.5f : aw), zs[j + 1] - (j + 1 == zs.Count - 1 ? 0.5f : aw));
                        // Jitter the block's edges a little so the alleys wander.
                        cell.xMin += Rand(rng, -1.2f, 1.2f); cell.xMax += Rand(rng, -1.2f, 1.2f);
                        cell.yMin += Rand(rng, -1.2f, 1.2f); cell.yMax += Rand(rng, -1.2f, 1.2f);
                        // The souk band and the gate forecourt are not houses.
                        if (!west)
                        {
                            var souk = Rect.MinMaxRect(SoukRect.xMin - 2f, SoukRect.yMin - 0.5f, SoukRect.xMax + 40f, SoukRect.yMax + 0.5f);
                            if (cell.Overlaps(souk))
                            {
                                // Keep the parts north and south of the souk.
                                var south = Rect.MinMaxRect(cell.xMin, cell.yMin, cell.xMax, souk.yMin - 4f);
                                var north = Rect.MinMaxRect(cell.xMin, souk.yMax + 4f, cell.xMax, cell.yMax);
                                if (south.height > 8f) SplitOld(south, west, rng, 0);
                                if (north.height > 8f) SplitOld(north, west, rng, 0);
                                continue;
                            }
                        }
                        // One small square in the middle of the old town, left open (a well, a tree, benches).
                        if (!west && i == xs.Count / 2 && j == zs.Count - 2) { _oldSquares.Add(cell); continue; }
                        SplitOld(cell, west, rng, 0);
                    }
            }
            // Fronts once every lot exists: the side with open ground (an alley) in front of it.
            foreach (var l in _townLots)
                if (l.kind == LotKind.OldHouse || l.kind == LotKind.Courtyard || l.kind == LotKind.Compound || l.kind == LotKind.Ruin)
                    l.front = FrontOf(l.r);
        }

        /// <summary>Open squares in the old town (filled by the street pass).</summary>
        private static readonly List<Rect> _oldSquares = new List<Rect>();

        private static void SplitOld(Rect r, bool west, System.Random rng, int depth)
        {
            float max = west ? 30f : 17f, min = west ? 14f : 8f;
            if (r.width < min || r.height < min) return;
            if ((r.width <= max && r.height <= max) || depth > 6)
            {
                if (!ClearOfRoads(r, 0.3f) || InReserved(r)) return;
                double p = rng.NextDouble();
                LotKind kind = west ? (p < 0.7 ? LotKind.Compound : p < 0.9 ? LotKind.OldHouse : LotKind.Ruin)
                                    : (r.width >= 14f && r.height >= 14f && p < 0.35 ? LotKind.Courtyard : p < 0.94 ? LotKind.OldHouse : LotKind.Ruin);
                _townLots.Add(new TownLot { r = r, kind = kind, front = FrontOf(r) });
                return;
            }
            if (r.width > r.height)
            {
                float cut = Rand(rng, r.xMin + r.width * 0.35f, r.xMax - r.width * 0.35f);
                SplitOld(Rect.MinMaxRect(r.xMin, r.yMin, cut, r.yMax), west, rng, depth + 1);
                SplitOld(Rect.MinMaxRect(cut, r.yMin, r.xMax, r.yMax), west, rng, depth + 1);
            }
            else
            {
                float cut = Rand(rng, r.yMin + r.height * 0.35f, r.yMax - r.height * 0.35f);
                SplitOld(Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, cut), west, rng, depth + 1);
                SplitOld(Rect.MinMaxRect(r.xMin, cut, r.xMax, r.yMax), west, rng, depth + 1);
            }
        }

        /// <summary>The side of an old-town lot most likely on an alley: the one with the most open ground in front of it.</summary>
        private static int FrontOf(Rect r)
        {
            int best = 1; float bestFree = -1f;
            for (int s = 0; s < 4; s++)
            {
                Vector2 c = SideCentre(r, s) + SideOut(s) * 2.5f;
                float free = 0f;
                foreach (var l in _townLots) if (l.r.Contains(c)) { free = -1f; break; }
                if (free >= 0f) free = 1f + (s == 1 ? 0.2f : 0f);
                if (free > bestFree) { bestFree = free; best = s; }
            }
            return best;
        }

        private static Vector2 SideCentre(Rect r, int s) => s == 0 ? new Vector2(r.center.x, r.yMax) : s == 1 ? new Vector2(r.center.x, r.yMin)
                                                           : s == 2 ? new Vector2(r.xMax, r.center.y) : new Vector2(r.xMin, r.center.y);
        private static Vector2 SideOut(int s) => s == 0 ? Vector2.up : s == 1 ? Vector2.down : s == 2 ? Vector2.right : Vector2.left;
        private static Vector3 SideOut3(int s) => s == 0 ? Vector3.forward : s == 1 ? Vector3.back : s == 2 ? Vector3.right : Vector3.left;

        // ==================================================================
        // The mass: walls with windows, floors, roof
        // ==================================================================
        /// <summary>What one face of a building carries.</summary>
        private enum FaceKind { Windows, Shops, Blank, OldWindows }

        private sealed class Mass
        {
            public Rect r;
            public float baseY, groundMin;
            public int floors;
            public float groundH = TownFloorH;      // ground-floor height (taller with shops)
            public float parapet = 1.0f;
            public Material wall, glass, frame;
            public FaceKind[] faces = new FaceKind[4];
            public int door = -1;               // side with the front door
            public bool enterable;
            public bool roofWalk;               // has a way up (outside stair)
            public bool unfinished;             // rebar and block on top
            public bool balconies;
            public bool oldStyle;
            public float windowW = 1.4f, windowH = 1.5f, bay = 3.3f;
            public System.Random rng;
            /// <summary>Ground-floor openings per side, (u0, u1, v0, v1) with v above the floor: what the interior cuts too.</summary>
            public readonly List<Vector4>[] groundHoles = { new List<Vector4>(), new List<Vector4>(), new List<Vector4>(), new List<Vector4>() };
            /// <summary>Where an outside stair reaches the roof: side and the span along it (parapet open there). -1 for none.</summary>
            public int stairSide = -1;
            public float stairU0, stairU1;
            public float Top => baseY + groundH + (floors - 1) * TownFloorH;
            public float FloorY(int f) => baseY + (f == 0 ? 0f : groundH + (f - 1) * TownFloorH);
        }

        /// <summary>The ground under a footprint: lowest and highest (sampled every 2 m and at the corners).</summary>
        private static (float low, float high) GroundRange(Rect r)
        {
            float low = float.MaxValue, high = float.MinValue;
            for (float x = r.xMin; x <= r.xMax + 0.01f; x += Mathf.Max(1f, Mathf.Min(2f, r.width * 0.5f)))
                for (float z = r.yMin; z <= r.yMax + 0.01f; z += Mathf.Max(1f, Mathf.Min(2f, r.height * 0.5f)))
                {
                    float h = GroundHeightAt(x, z);
                    low = Mathf.Min(low, h); high = Mathf.Max(high, h);
                }
            return (low, high);
        }

        /// <summary>The floor level for a building fronting a pavement: never below the pavement, never under the ground.</summary>
        private static float FloorLevel(Rect r)
        {
            var (low, high) = GroundRange(r);
            float walk = float.MinValue;
            for (int s = 0; s < 4; s++)
            {
                var p = SideCentre(r, s) + SideOut(s) * 1.5f;
                if (RoadAt(p) != null) walk = Mathf.Max(walk, TownRoadY(p.x, p.y) + KerbHeight);
            }
            return Mathf.Max(high + 0.12f, walk + 0.02f);
        }

        /// <summary>Corners of a face, left to right seen from outside, at ground level (y filled by caller).</summary>
        private static (Vector3 a, Vector3 b) FaceRun(Rect r, int side)
        {
            switch (side)
            {
                case 0: return (new Vector3(r.xMax, 0f, r.yMax), new Vector3(r.xMin, 0f, r.yMax));
                case 1: return (new Vector3(r.xMin, 0f, r.yMin), new Vector3(r.xMax, 0f, r.yMin));
                case 2: return (new Vector3(r.xMax, 0f, r.yMin), new Vector3(r.xMax, 0f, r.yMax));
                default: return (new Vector3(r.xMin, 0f, r.yMax), new Vector3(r.xMin, 0f, r.yMin));
            }
        }

        /// <summary>
        /// Builds a rectangular mass into its chunk: four walls from below ground to the parapet with every opening,
        /// slab bands, the roof and parapet, and (if enterable) the ground floor's inside. Returns the holes it cut in
        /// the front so the caller can add doors, shops and signs in them.
        /// </summary>
        private static void BuildMass(Mass b, Transform props)
        {
            var m = Chunk(b.r.center);
            m.UVOffset = new Vector2((float)b.rng.NextDouble() * 7f, (float)b.rng.NextDouble() * 5f);
            float bottom = b.groundMin - 0.6f;
            float top = b.Top + b.parapet;
            var rng = b.rng;

            for (int side = 0; side < 4; side++)
            {
                var (a0, b0) = FaceRun(b.r, side);
                var nrm = SideOut3(side);
                float len = Vector3.Distance(a0, b0);
                Vector3 u = (b0 - a0) / Mathf.Max(0.01f, len);
                var holes = new List<Vector4>();
                var kind = b.faces[side];
                // Balconies only over a street: a row house built to its lot line hung them 1.15 m into the villa yard
                // next door, over the outside stair there, and cut the flight off the navmesh.
                Vector2 faceMid = new Vector2((a0.x + b0.x) * 0.5f, (a0.z + b0.z) * 0.5f);
                bool streetFace = side == b.door || RoadAt(faceMid + SideOut(side) * 3f) != null;

                // ---- openings ----
                int bays = Mathf.Max(1, Mathf.FloorToInt(len / b.bay));
                float bayW = len / bays;
                for (int f = 0; f < b.floors; f++)
                {
                    float fy = b.FloorY(f) - bottom;           // floor height above the wall's base
                    for (int k = 0; k < bays; k++)
                    {
                        float cu = bayW * (k + 0.5f);
                        if (kind == FaceKind.Blank) continue;
                        if (f == 0 && kind == FaceKind.Shops)
                        {
                            float w = bayW - 0.6f;
                            holes.Add(new Vector4(cu - w * 0.5f, cu + w * 0.5f, fy, fy + Mathf.Min(3.1f, b.groundH - 0.5f)));
                            continue;
                        }
                        if (f == 0 && side == b.door && k == bays / 2)
                        {
                            holes.Add(new Vector4(cu - 0.65f, cu + 0.65f, fy, fy + 2.25f));
                            continue;
                        }
                        float ww = b.oldStyle ? Rand(rng, 0.6f, 0.9f) : b.windowW;
                        float wh = b.oldStyle ? Rand(rng, 0.8f, 1.1f) : b.windowH;
                        if (b.oldStyle && rng.NextDouble() < 0.35) continue;            // old walls have fewer windows
                        bool balcony = b.balconies && streetFace && f > 0 && rng.NextDouble() < 0.4;
                        float sill = balcony ? 0.05f : (b.oldStyle ? 1.3f : 0.95f);
                        float h = balcony ? 2.2f : wh;
                        holes.Add(new Vector4(cu - ww * 0.5f, cu + ww * 0.5f, fy + sill, fy + sill + h));
                    }
                }

                if (side == b.stairSide) holes.Add(new Vector4(b.stairU0, b.stairU1, b.Top - bottom + 0.05f, top - bottom + 0.2f));
                float floorV = b.FloorY(0) - bottom;
                foreach (var h in holes)
                    if (h.z - floorV < 0.5f + 1.2f && h.w - floorV < b.groundH + 0.1f) b.groundHoles[side].Add(new Vector4(h.x, h.y, h.z - floorV, h.w - floorV));

                var wa = a0 + Vector3.up * bottom; var wb = b0 + Vector3.up * bottom;
                m.WallWithHoles(b.wall, wa, wb, top - bottom, nrm, holes);

                // ---- reveals, glass, frames, sills ----
                foreach (var h in holes)
                {
                    if (side == b.stairSide && h.z > b.Top - bottom) continue;     // the parapet gap: no reveal, no glass
                    float depth = b.enterable && h.z - (b.FloorY(0) - bottom) < 0.5f ? 0.25f : 0.2f;
                    Vector3 p0 = wa + u * h.x + Vector3.up * h.z, p1 = wa + u * h.y + Vector3.up * h.z;
                    Vector3 q0 = wa + u * h.x + Vector3.up * h.w, q1 = wa + u * h.y + Vector3.up * h.w;
                    Vector3 inn = -nrm * depth;
                    m.Quad(b.wall, p0, p1, p1 + inn, p0 + inn, Vector3.up);          // sill reveal
                    m.Quad(b.wall, q0, q1, q1 + inn, q0 + inn, Vector3.down);        // head
                    m.Quad(b.wall, p0, q0, q0 + inn, p0 + inn, u);                   // jambs
                    m.Quad(b.wall, p1, q1, q1 + inn, p1 + inn, -u);

                    bool groundFloor = h.z - (b.FloorY(0) - bottom) < 0.5f;
                    bool isDoor = groundFloor && kind != FaceKind.Shops && h.y - h.x < 1.5f && h.w - h.z > 2f;
                    bool isShop = groundFloor && kind == FaceKind.Shops;
                    if (isShop) { ShopFront(m, b, side, p0, p1, q0, q1, inn, nrm, props); continue; }
                    if (isDoor)
                    {
                        if (b.enterable) { OpenDoor(m, b, p0, p1, q0, inn, nrm); _townDoors.Add((p0 + p1) * 0.5f + nrm * 0.8f); }
                        else m.Quad(rng.NextDouble() < 0.5 ? _doorMetal : _doorWood, p0 + inn, p1 + inn, q1 + inn, q0 + inn, nrm);
                        continue;
                    }
                    WindowIn(m, b, p0, p1, q0, q1, inn, nrm, groundFloor && b.enterable, h.w - h.z > 2f);
                }

                // ---- slab bands, plinth ----
                if (!b.oldStyle)
                    for (int f = 1; f <= b.floors; f++)
                    {
                        float y = (f == b.floors ? b.Top : b.FloorY(f));
                        m.Box(_band, (a0 + b0) * 0.5f + Vector3.up * (y - 0.11f) + nrm * 0.03f, new Vector3(len + 0.06f, 0.22f, 0.06f),
                              Quaternion.LookRotation(nrm), bottom: true);
                    }
                m.Box(b.oldStyle ? b.wall : _band, (a0 + b0) * 0.5f + Vector3.up * (b.baseY - 0.25f) + nrm * 0.02f,
                      new Vector3(len + 0.04f, Mathf.Max(0.5f, b.baseY - bottom + 0.25f) , 0.04f) , Quaternion.LookRotation(nrm));
            }

            // ---- roof, parapet, coping ----
            var rr = b.r;
            float roofY = b.Top + 0.05f;
            m.Quad(_roof, new Vector3(rr.xMin, roofY, rr.yMin), new Vector3(rr.xMax, roofY, rr.yMin), new Vector3(rr.xMax, roofY, rr.yMax), new Vector3(rr.xMin, roofY, rr.yMax), Vector3.up);
            float t = 0.22f;
            for (int side = 0; side < 4; side++)
            {
                var (a0, b0) = FaceRun(rr, side);
                var nrm = SideOut3(side);
                float len = Vector3.Distance(a0, b0);
                Vector3 u = (b0 - a0) / Mathf.Max(0.01f, len);
                var spans = new List<Vector2>();
                if (side == b.stairSide) { spans.Add(new Vector2(0f, b.stairU0)); spans.Add(new Vector2(b.stairU1, len)); }
                else spans.Add(new Vector2(0f, len));
                foreach (var sp in spans)
                {
                    if (sp.y - sp.x < 0.05f) continue;
                    Vector3 ia = a0 + u * sp.x - nrm * t, ib = a0 + u * sp.y - nrm * t;
                    // Inner face of the parapet, and its coping.
                    m.Quad(b.wall, ia + Vector3.up * roofY, ib + Vector3.up * roofY, ib + Vector3.up * top, ia + Vector3.up * top, -nrm);
                    m.Box(_coping, a0 + u * ((sp.x + sp.y) * 0.5f) - nrm * (t * 0.5f - 0.03f) + Vector3.up * (top + 0.04f),
                          new Vector3(sp.y - sp.x + 0.1f, 0.08f, t + 0.08f), Quaternion.LookRotation(nrm));
                }
                if (side == b.stairSide)
                    foreach (float e in new[] { b.stairU0, b.stairU1 })
                        m.Quad(b.wall, a0 + u * e + Vector3.up * roofY, a0 + u * e - nrm * t + Vector3.up * roofY,
                               a0 + u * e - nrm * t + Vector3.up * top, a0 + u * e + Vector3.up * top, e == b.stairU0 ? u : -u);
            }
            if (b.oldStyle && b.rng.NextDouble() < 0.4) Crenellate(m, b, top);
            if (b.oldStyle && b.rng.NextDouble() < 0.55) PalmBeams(m, b);

            RoofClutter(m, b, roofY, props);
            if (b.enterable) Interior(m, b, props);

            // ---- navigation: keep the closed parts of the building off the bake ----
            float volBottom = b.enterable ? b.FloorY(0) + b.groundH - 0.35f : b.groundMin - 1f;
            float volTop = b.roofWalk ? roofY - 0.35f : top + 3f;
            if (volTop > volBottom + 0.3f) NoEntryBox(b.r, volBottom, volTop, b.enterable ? 0.3f : 0.5f);
            _townSolids.Add(b.r);
        }

        /// <summary>A Not Walkable volume over a footprint, on its own unscaled object (CLAUDE.md: never under a scaled parent).</summary>
        private static void NoEntryBox(Rect r, float y0, float y1, float inset)
        {
            var go = new GameObject("NoEntry");
            go.transform.SetParent(_volumes, false);
            go.transform.position = new Vector3(r.center.x, (y0 + y1) * 0.5f, r.center.y);
            var v = go.AddComponent<Unity.AI.Navigation.NavMeshModifierVolume>();
            // The component's centre defaults to (0, 1, 0): left there, every volume sat a metre above where it was
            // placed, under roofs it was meant to cover and over wall-walks it was meant to stop short of.
            v.center = Vector3.zero;
            v.size = new Vector3(Mathf.Max(0.5f, r.width - inset * 2f), y1 - y0, Mathf.Max(0.5f, r.height - inset * 2f));
            v.area = 1;
        }

        // ------------------------------------------------------------------
        private static void WindowIn(TownMesh m, Mass b, Vector3 p0, Vector3 p1, Vector3 q0, Vector3 q1, Vector3 inn, Vector3 nrm,
                                     bool open, bool tall)
        {
            var rng = b.rng;
            Vector3 g0 = p0 + inn, g1 = p1 + inn, h0 = q0 + inn, h1 = q1 + inn;
            Vector3 u = (p1 - p0).normalized;
            float w = Vector3.Distance(p0, p1), hgt = Vector3.Distance(p0, q0);
            if (open)
            {
                // A ground-floor window of a house you can enter: open and barred, both faces of each bar drawn.
                for (float s = 0.12f; s < w - 0.05f; s += 0.14f)
                {
                    Strip2(m, _steel, p0 + u * s + Vector3.up * hgt * 0.5f + inn * 0.4f, u, 0.025f, hgt, nrm);
                    Strip2(m, _steel, p0 + u * s + Vector3.up * hgt * 0.5f + inn * 0.4f, u, 0.025f, hgt, -nrm);
                }
                return;
            }
            double pick = rng.NextDouble();
            Material glass = pick < 0.14 ? _glassCurtain : pick < 0.22 ? _glassBlinds : b.glass;
            if (b.oldStyle)
            {
                // Timber shutters, mostly closed; the odd one open on dark glass.
                if (rng.NextDouble() < 0.7) { m.Quad(_woodShutter, g0, g1, h1, h0, nrm); return; }
                m.Quad(_glassDark, g0, g1, h1, h0, nrm);
                m.Box(_woodShutter, p0 - u * w * 0.25f + Vector3.up * hgt * 0.5f + nrm * 0.03f, new Vector3(w * 0.5f, hgt, 0.04f), Quaternion.LookRotation(nrm));
                m.Box(_woodShutter, p1 + u * w * 0.25f + Vector3.up * hgt * 0.5f + nrm * 0.03f, new Vector3(w * 0.5f, hgt, 0.04f), Quaternion.LookRotation(nrm));
                return;
            }
            m.Quad(glass, g0, g1, h1, h0, nrm);
            // Frame: strips facing out just proud of the glass (a box per bar cost six times the triangles and the
            // sides are never seen), a mullion on wide windows, a transom on tall ones.
            const float fw = 0.055f;
            Vector3 c = (g0 + h1) * 0.5f + nrm * 0.012f;
            Strip2(m, b.frame, c + Vector3.up * (hgt * 0.5f - fw * 0.5f), u, w, fw, nrm);
            Strip2(m, b.frame, c - Vector3.up * (hgt * 0.5f - fw * 0.5f), u, w, fw, nrm);
            Strip2(m, b.frame, c - u * (w * 0.5f - fw * 0.5f), u, fw, hgt, nrm);
            Strip2(m, b.frame, c + u * (w * 0.5f - fw * 0.5f), u, fw, hgt, nrm);
            if (w > 1.1f) Strip2(m, b.frame, c, u, fw * 0.8f, hgt, nrm);
            if (tall) Strip2(m, b.frame, c + Vector3.up * (hgt * 0.5f - 0.6f), u, w, fw * 0.8f, nrm);
            // Sill: its top and its face.
            Vector3 sc = (p0 + p1) * 0.5f;
            m.Quad(_coping, sc - u * (w * 0.5f + 0.06f) + nrm * 0.1f, sc + u * (w * 0.5f + 0.06f) + nrm * 0.1f,
                   sc + u * (w * 0.5f + 0.06f) - nrm * 0.02f, sc - u * (w * 0.5f + 0.06f) - nrm * 0.02f, Vector3.up);
            Strip2(m, _coping, sc + nrm * 0.1f - Vector3.up * 0.03f, u, w + 0.12f, 0.06f, nrm);
            // Grilles on low windows.
            if (p0.y - b.baseY < 3f && rng.NextDouble() < 0.6)
            {
                Vector3 gcen = (p0 + q1) * 0.5f + nrm * 0.04f;
                for (float s = 0.1f; s < w - 0.04f; s += 0.12f) Strip2(m, _steel, p0 + u * s + Vector3.up * hgt * 0.5f + nrm * 0.04f, u, 0.022f, hgt, nrm);
                Strip2(m, _steel, gcen + Vector3.up * hgt * 0.25f, u, w, 0.03f, nrm);
                Strip2(m, _steel, gcen - Vector3.up * hgt * 0.25f, u, w, 0.03f, nrm);
            }
            if (tall && b.balconies) Balcony(m, b, p0, p1, nrm);
        }

        /// <summary>A flat rectangle centred on <paramref name="c"/>, <paramref name="w"/> along <paramref name="u"/> by <paramref name="h"/> up, facing <paramref name="nrm"/>.</summary>
        private static void Strip2(TownMesh m, Material mat, Vector3 c, Vector3 u, float w, float h, Vector3 nrm)
        {
            Vector3 a = c - u * (w * 0.5f) - Vector3.up * (h * 0.5f), b = c + u * (w * 0.5f) - Vector3.up * (h * 0.5f);
            m.Quad(mat, a, b, b + Vector3.up * h, a + Vector3.up * h, nrm);
        }

        private static void Balcony(TownMesh m, Mass b, Vector3 p0, Vector3 p1, Vector3 nrm)
        {
            var rot = Quaternion.LookRotation(nrm);
            Vector3 u = (p1 - p0).normalized;
            float w = Vector3.Distance(p0, p1) + 0.9f, d = 1.15f;
            Vector3 c = (p0 + p1) * 0.5f + nrm * (d * 0.5f);
            m.Box(_band, c - Vector3.up * 0.08f, new Vector3(w, 0.16f, d), rot);
            // Solid balustrade, plaster-faced like the building, capped.
            m.Box(b.wall, c + nrm * (d * 0.5f - 0.05f) + Vector3.up * 0.5f, new Vector3(w, 1.0f, 0.1f), rot);
            m.Box(b.wall, c - u * (w * 0.5f - 0.05f) + Vector3.up * 0.5f, new Vector3(0.1f, 1.0f, d), rot);
            m.Box(b.wall, c + u * (w * 0.5f - 0.05f) + Vector3.up * 0.5f, new Vector3(0.1f, 1.0f, d), rot);
            m.Box(_coping, c + nrm * (d * 0.5f - 0.05f) + Vector3.up * 1.03f, new Vector3(w + 0.04f, 0.06f, 0.16f), rot);
        }

        private static void OpenDoor(TownMesh m, Mass b, Vector3 p0, Vector3 p1, Vector3 q0, Vector3 inn, Vector3 nrm)
        {
            // The leaf swung in against the inside of the reveal, so the doorway is clear.
            Vector3 u = (p1 - p0).normalized;
            float w = Vector3.Distance(p0, p1), h = Vector3.Distance(p0, q0);
            var leafCentre = p0 + inn + (-nrm) * (w * 0.5f - 0.05f) + Vector3.up * (h * 0.5f) + u * 0.03f;
            m.Box(b.rng.NextDouble() < 0.5 ? _doorWood : _doorMetal, leafCentre, new Vector3(0.05f, h, w - 0.05f), Quaternion.LookRotation(nrm));
            // A step down to the pavement or yard if the floor stands above the ground.
            float g = GroundHeightAt((p0.x + p1.x) * 0.5f, (p0.z + p1.z) * 0.5f);
            if (b.baseY - g > 0.12f)
                m.Box(_coping, (p0 + p1) * 0.5f + nrm * 0.35f + Vector3.up * ((g - p0.y) * 0.5f - 0.02f),
                      new Vector3(w + 0.6f, Mathf.Max(0.05f, b.baseY - g), 0.7f), Quaternion.LookRotation(nrm));
        }

        // ------------------------------------------------------------------
        private static void ShopFront(TownMesh m, Mass b, int side, Vector3 p0, Vector3 p1, Vector3 q0, Vector3 q1, Vector3 inn, Vector3 nrm, Transform props)
        {
            var rng = b.rng;
            Vector3 u = (p1 - p0).normalized;
            float w = Vector3.Distance(p0, p1), h = Vector3.Distance(p0, q0);
            var rot = Quaternion.LookRotation(nrm);
            double state = rng.NextDouble();       // < .35 open, < .55 half, else shut
            Vector3 back = -nrm * 3.2f;
            if (state < 0.55)
            {
                // The shop behind: dark floor, walls and ceiling three metres deep.
                Vector3 a = p0 + inn, c = p1 + inn;
                m.Quad(_terrazzo, a, c, c + back, a + back, Vector3.up);
                m.Quad(_shopDark, a + back, c + back, c + back + Vector3.up * h, a + back + Vector3.up * h, nrm);
                m.Quad(_shopDark, a, a + back, a + back + Vector3.up * h, a + Vector3.up * h, u);
                m.Quad(_shopDark, c, c + back, c + back + Vector3.up * h, c + Vector3.up * h, -u);
                m.Quad(_shopDark, a + Vector3.up * h, c + Vector3.up * h, c + back + Vector3.up * h, a + back + Vector3.up * h, Vector3.down);
                if (state < 0.35)
                {
                    var centre = (p0 + p1) * 0.5f;
                    _shopFronts.Add(new Vector4(centre.x + nrm.x * 1.2f, centre.y, centre.z + nrm.z * 1.2f, Mathf.Atan2(nrm.x, nrm.z) * Mathf.Rad2Deg));
                    // Shelves inside, facing out.
                    PlaceTown(props, Prop("steel_frame_shelves_01"), (p0 + p1) * 0.5f + back * 0.85f - u * w * 0.25f, Mathf.Atan2(-nrm.x, -nrm.z) * Mathf.Rad2Deg + 180f, 2.0f);
                    PlaceTown(props, Prop(rng.NextDouble() < 0.5 ? "cardboard_box_01" : "plastic_crate_02"), (p0 + p1) * 0.5f + back * 0.5f + u * w * 0.25f, (float)rng.NextDouble() * 360f, 0.45f);
                }
                if (state >= 0.35)
                    m.Quad(rng.NextDouble() < 0.4 ? _shutterRust : _shutterPaint, q0 + inn * 0.3f - Vector3.up * h * 0.45f, q1 + inn * 0.3f - Vector3.up * h * 0.45f, q1 + inn * 0.3f, q0 + inn * 0.3f, nrm);
            }
            else
                m.Quad(rng.NextDouble() < 0.4 ? _shutterRust : _shutterPaint, p0 + inn * 0.3f, p1 + inn * 0.3f, q1 + inn * 0.3f, q0 + inn * 0.3f, nrm);
            // The shutter box above the opening.
            m.Box(_frameAlu, (q0 + q1) * 0.5f + nrm * 0.12f + Vector3.up * 0.15f, new Vector3(w, 0.3f, 0.3f), rot);

            // Sign above, on a backing board.
            var cell = SignCell(ShopSigns[rng.Next(ShopSigns.Length)]);
            float sh = Mathf.Min(1.0f, w * 0.25f), sw = sh * 4f;
            if (sw > w) { sw = w; sh = w * 0.25f; }
            Vector3 sc = (q0 + q1) * 0.5f + Vector3.up * (0.45f + sh * 0.5f) + nrm * 0.07f;
            m.Box(_steel, sc - nrm * 0.03f, new Vector3(sw + 0.08f, sh + 0.08f, 0.04f), rot);
            Vector3 s0 = sc - u * sw * 0.5f - Vector3.up * sh * 0.5f, s1 = sc + u * sw * 0.5f - Vector3.up * sh * 0.5f;
            m.QuadUV(_townSignMat, s0, s1, s1 + Vector3.up * sh, s0 + Vector3.up * sh, nrm,
                     new Vector2(cell.xMin, cell.yMin), new Vector2(cell.xMax, cell.yMin), new Vector2(cell.xMax, cell.yMax), new Vector2(cell.xMin, cell.yMax));

            // An awning on some.
            if (rng.NextDouble() < 0.35)
            {
                int k = rng.Next(4);
                var uv0 = new Vector2((k % 2) * 0.5f, (k / 2) * 0.5f);
                float d = 1.3f, drop = 0.6f;
                Vector3 top0 = q0 + Vector3.up * 0.05f + nrm * 0.05f, top1 = q1 + Vector3.up * 0.05f + nrm * 0.05f;
                Vector3 bot0 = top0 + nrm * d - Vector3.up * drop, bot1 = top1 + nrm * d - Vector3.up * drop;
                var an = Vector3.Cross(top1 - top0, bot0 - top0).normalized; if (an.y < 0f) an = -an;
                m.QuadUV(_awningMat, top0, top1, bot1, bot0, an, uv0, uv0 + new Vector2(0.5f, 0f), uv0 + new Vector2(0.5f, 0.5f), uv0 + new Vector2(0f, 0.5f));
                m.QuadUV(_awningMat, top0, top1, bot1, bot0, -an, uv0, uv0 + new Vector2(0.5f, 0f), uv0 + new Vector2(0.5f, 0.5f), uv0 + new Vector2(0f, 0.5f));
            }
        }

        // ------------------------------------------------------------------
        private static void Crenellate(TownMesh m, Mass b, float top)
        {
            // Stepped merlons along the parapet, the old houses' roofline.
            for (int side = 0; side < 4; side++)
            {
                var (a0, b0) = FaceRun(b.r, side);
                var nrm = SideOut3(side);
                float len = Vector3.Distance(a0, b0);
                Vector3 u = (b0 - a0) / len;
                for (float s = 0.4f; s < len - 0.3f; s += 1.1f)
                    m.Box(b.wall, a0 + u * s + Vector3.up * (top + 0.28f) - nrm * 0.11f, new Vector3(0.5f, 0.45f, 0.22f), Quaternion.LookRotation(nrm));
            }
        }

        private static void PalmBeams(TownMesh m, Mass b)
        {
            // Palm-trunk joists showing through the wall under the roof.
            for (int side = 0; side < 4; side++)
            {
                if (b.faces[side] == FaceKind.Blank) continue;
                var (a0, b0) = FaceRun(b.r, side);
                var nrm = SideOut3(side);
                float len = Vector3.Distance(a0, b0);
                Vector3 u = (b0 - a0) / len;
                for (float s = 0.5f; s < len - 0.4f; s += 0.75f)
                    m.Box(_woodBeam, a0 + u * s + Vector3.up * (b.Top - 0.15f) + nrm * 0.18f, new Vector3(0.16f, 0.16f, 0.36f), Quaternion.LookRotation(nrm));
            }
        }

        /// <summary>What stands on a flat roof here: water tanks, dishes, solar heaters, the stair bulkhead, rebar.</summary>
        private static void RoofClutter(TownMesh m, Mass b, float roofY, Transform props)
        {
            var rng = b.rng;
            var r = b.r;
            Vector3 P(float fx, float fz) => new Vector3(Mathf.Lerp(r.xMin + 1.2f, r.xMax - 1.2f, fx), roofY, Mathf.Lerp(r.yMin + 1.2f, r.yMax - 1.2f, fz));
            // Nothing stands in the way in from the stair: behind its parapet gap the roof is kept clear for 3.5 m. A course
            // of block 1.2 m in from the edge walled the gap off on a dozen roofs.
            var keep = StairClearing(b);
            bool Clear(Vector3 at, float half) => keep.width <= 0f || !keep.Overlaps(Rect.MinMaxRect(at.x - half, at.z - half, at.x + half, at.z + half));

            int tanks = b.oldStyle ? rng.Next(0, 2) : rng.Next(1, 4);
            for (int i = 0; i < tanks; i++)
            {
                var at = P((float)rng.NextDouble(), (float)rng.NextDouble());
                var mat = rng.NextDouble() < 0.55 ? _tankBlack : rng.NextDouble() < 0.6 ? _tankWhite : _tankBlue;
                if (!Clear(at, 0.8f)) continue;
                // On a little steel stand, with the ribbed shape of a moulded tank.
                m.BoxMinMax(_steel, at + new Vector3(-0.7f, 0f, -0.7f), at + new Vector3(0.7f, 0.5f, 0.7f));
                var prof = new List<Vector2> { new Vector2(0.62f, 0f), new Vector2(0.66f, 0.12f), new Vector2(0.62f, 0.3f), new Vector2(0.66f, 0.5f),
                                               new Vector2(0.62f, 0.7f), new Vector2(0.66f, 0.9f), new Vector2(0.6f, 1.1f), new Vector2(0.35f, 1.25f), new Vector2(0.12f, 1.3f), new Vector2(0f, 1.3f) };
                m.Revolve(mat, at + Vector3.up * 0.5f, prof, 14);
            }
            int dishes = b.oldStyle ? rng.Next(0, 2) : rng.Next(1, b.floors >= 4 ? 6 : 3);
            for (int i = 0; i < dishes; i++)
            {
                var at = P((float)rng.NextDouble(), (float)rng.NextDouble());
                float yaw = (float)rng.NextDouble() * 60f + 120f;
                if (Clear(at, 0.5f)) Dish(m, at, yaw);
            }
            if (!b.oldStyle && rng.NextDouble() < 0.35)
            {
                var at = P((float)rng.NextDouble(), (float)rng.NextDouble());
                if (Clear(at, 1.4f)) SolarHeater(m, at);
            }
            if (!b.oldStyle && b.floors >= 3 && r.width > 9f && r.height > 9f)
            {
                // Stair bulkhead: a little room with a door, which is how the blocks reach their roofs.
                var at = P(0.15f, 0.2f);
                if (!Clear(at, 1.8f)) at = P(0.85f, 0.8f);
                if (Clear(at, 1.8f))
                {
                m.BoxMinMax(b.wall, at + new Vector3(-1.4f, 0f, -1.6f), at + new Vector3(1.4f, 2.5f, 1.6f), bottom: false);
                m.BoxMinMax(_coping, at + new Vector3(-1.5f, 2.5f, -1.7f), at + new Vector3(1.5f, 2.62f, 1.7f));
                m.Quad(_doorMetal, at + new Vector3(-0.45f, 0.02f, 1.61f), at + new Vector3(0.45f, 0.02f, 1.61f), at + new Vector3(0.45f, 2.05f, 1.61f), at + new Vector3(-0.45f, 2.05f, 1.61f), Vector3.forward);
                }
            }
            if (!b.oldStyle && rng.NextDouble() < 0.5)
            {
                var at = P((float)rng.NextDouble(), (float)rng.NextDouble());
                float yaw = rng.Next(4) * 90f;
                if (Clear(at, 0.7f)) PlaceTown(props, Prop("exterior_aircon_unit"), at, yaw, 0.75f);
            }
            if (b.unfinished)
            {
                // Columns started for the next floor: rebar standing out of stub columns at the corners and along the walls.
                foreach (var c in new[] { P(0, 0), P(1, 0), P(0, 1), P(1, 1), P(0.5f, 0), P(0.5f, 1) })
                {
                    if (!Clear(c, 0.3f)) continue;
                    m.BoxMinMax(_band, c + new Vector3(-0.2f, 0f, -0.2f), c + new Vector3(0.2f, 0.45f, 0.2f));
                    for (int k = 0; k < 4; k++)
                    {
                        var o = new Vector3((k % 2 - 0.5f) * 0.24f, 0f, (k / 2 - 0.5f) * 0.24f);
                        float hh = Rand(rng, 0.9f, 1.6f);
                        m.Box(_rust, c + o + Vector3.up * (0.45f + hh * 0.5f), new Vector3(0.016f, hh, 0.016f), Quaternion.Euler(Rand(rng, -4f, 4f), 0f, Rand(rng, -4f, 4f)));
                    }
                }
                // A half-built course of block along one edge (the far one, when the stair comes up the near one).
                float reach = Rand(rng, 0.3f, 0.7f), high = Rand(rng, 0.6f, 1.6f);
                foreach (float fz in new[] { 1f, 0f })
                {
                    var s0 = P(0, fz); var s1 = P(1, fz);
                    var c0 = new Vector3(s0.x, roofY, s0.z - 0.1f); var c1 = new Vector3(Mathf.Lerp(s0.x, s1.x, reach), roofY + high, s0.z + 0.1f);
                    if (keep.width > 0f && keep.Overlaps(Rect.MinMaxRect(c0.x, c0.z, c1.x, c1.z))) continue;
                    m.BoxMinMax(_block, c0, c1);
                    break;
                }
            }
            // Washing on a line, now and then: its own mesh on the backdrop layer, without a collider. In the bake, the cloth
            // hanging from 1 m to 1.75 m left an agent no headroom under the line, and it cut the roof in two.
            if (rng.NextDouble() < 0.3 && r.width > 6f)
            {
                var wm = new TownMesh();
                var a = P(0.1f, 0.6f); var c2 = P(0.9f, 0.6f);
                wm.Box(_steel, a + Vector3.up * 0.9f, new Vector3(0.04f, 1.8f, 0.04f), Quaternion.identity);
                wm.Box(_steel, c2 + Vector3.up * 0.9f, new Vector3(0.04f, 1.8f, 0.04f), Quaternion.identity);
                float len = Vector3.Distance(a, c2);
                for (float s = 0.4f; s < len - 0.4f; s += Rand(rng, 0.5f, 0.9f))
                {
                    var p = Vector3.Lerp(a, c2, s / len) + Vector3.up * 1.75f;
                    wm.Box(Cloth(rng.Next(ClothColours.Length)), p - Vector3.up * 0.35f,
                           new Vector3(Rand(rng, 0.3f, 0.6f), Rand(rng, 0.5f, 0.8f), 0.01f), Quaternion.LookRotation((c2 - a).normalized) * Quaternion.Euler(0, 90, 0));
                }
                wm.Build(props, "Washing", LayerMask.NameToLayer("Backdrop"), "Untagged", collider: false);
            }
        }

        /// <summary>The patch of roof behind a stair's parapet gap that is kept clear (empty when the roof has no stair).</summary>
        private static Rect StairClearing(Mass b)
        {
            if (b.stairSide < 0) return Rect.zero;
            var (a0, b0) = FaceRun(b.r, b.stairSide);
            var u = (b0 - a0).normalized; var inward = -SideOut3(b.stairSide);
            Vector3 p = a0 + u * (b.stairU0 - 0.6f), q = a0 + u * (b.stairU1 + 0.6f) + inward * 3.5f;
            return Rect.MinMaxRect(Mathf.Min(p.x, q.x), Mathf.Min(p.z, q.z), Mathf.Max(p.x, q.x), Mathf.Max(p.z, q.z));
        }

        private static readonly Color[] ClothColours =
        {
            new Color(0.92f, 0.92f, 0.90f), new Color(0.20f, 0.30f, 0.55f), new Color(0.70f, 0.18f, 0.16f), new Color(0.85f, 0.75f, 0.45f),
            new Color(0.25f, 0.45f, 0.30f), new Color(0.15f, 0.15f, 0.16f), new Color(0.80f, 0.55f, 0.65f), new Color(0.55f, 0.70f, 0.85f),
        };

        private static Material Cloth(int i) => TownFlat($"Cloth{i}", ClothColours[i], 0.08f);

        private static void Dish(TownMesh m, Vector3 at, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            m.Box(_steel, at + Vector3.up * 0.45f, new Vector3(0.05f, 0.9f, 0.05f), Quaternion.identity);
            var prof = new List<Vector2> { new Vector2(0f, 0f), new Vector2(0.18f, 0.01f), new Vector2(0.32f, 0.04f), new Vector2(0.42f, 0.08f) };
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tris = new List<int>();
            const int seg = 12;
            var dishRot = rot * Quaternion.Euler(-60f, 0f, 0f);
            Vector3 c = at + Vector3.up * 1.0f;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                foreach (var p in prof)
                {
                    var local = new Vector3(Mathf.Cos(a) * p.x, p.y, Mathf.Sin(a) * p.x);
                    verts.Add(c + dishRot * local);
                    norms.Add(dishRot * new Vector3(-Mathf.Cos(a) * p.x * 0.4f, 1f, -Mathf.Sin(a) * p.x * 0.4f).normalized);
                }
            }
            int rows = prof.Count;
            for (int i = 0; i < seg; i++)
                for (int k = 0; k + 1 < rows; k++)
                {
                    int a0 = i * rows + k, a1 = a0 + 1, b0 = a0 + rows, b1 = b0 + 1;
                    tris.Add(a0); tris.Add(b1); tris.Add(a1); tris.Add(a0); tris.Add(b0); tris.Add(b1);
                    tris.Add(a0); tris.Add(a1); tris.Add(b1); tris.Add(a0); tris.Add(b1); tris.Add(b0);   // both sides
                }
            var n2 = new List<Vector3>(norms);
            m.Smooth(_dish, verts, n2, null, tris);
            m.Box(_steel, c + dishRot * new Vector3(0f, 0.3f, 0.25f), new Vector3(0.03f, 0.03f, 0.55f), dishRot * Quaternion.Euler(50f, 0f, 0f));
        }

        private static void SolarHeater(TownMesh m, Vector3 at)
        {
            var rot = Quaternion.Euler(-35f, 180f, 0f);
            m.Box(_solar, at + new Vector3(0f, 0.7f, 0f), new Vector3(1.0f, 0.06f, 2.0f), rot);
            m.Box(_steel, at + new Vector3(0f, 0.35f, 0.6f), new Vector3(1.0f, 0.7f, 0.05f), Quaternion.identity);
            var tank = new List<Vector2> { new Vector2(0.25f, 0f), new Vector2(0.25f, 1.2f), new Vector2(0f, 1.2f) };
            var p = at + new Vector3(-0.6f, 1.45f, -0.75f);
            // Lying on its side across the top of the panel.
            m.Box(_tankWhite, at + new Vector3(0f, 1.42f, -0.8f), new Vector3(1.3f, 0.45f, 0.45f), Quaternion.identity);
            _ = tank; _ = p;
        }

        // ------------------------------------------------------------------
        /// <summary>The ground floor of a house you can enter: floor, ceiling, the inside of the walls, a partition and furniture.</summary>
        private static void Interior(TownMesh m, Mass b, Transform props)
        {
            var rng = b.rng;
            var r = b.r;
            const float t = 0.25f;
            float y0 = b.FloorY(0), y1 = y0 + b.groundH - 0.25f;
            var inner = Rect.MinMaxRect(r.xMin + t, r.yMin + t, r.xMax - t, r.yMax - t);
            m.Quad(_terrazzo, new Vector3(inner.xMin, y0, inner.yMin), new Vector3(inner.xMax, y0, inner.yMin), new Vector3(inner.xMax, y0, inner.yMax), new Vector3(inner.xMin, y0, inner.yMax), Vector3.up);
            m.Quad(_plasterIn, new Vector3(inner.xMin, y1, inner.yMin), new Vector3(inner.xMax, y1, inner.yMin), new Vector3(inner.xMax, y1, inner.yMax), new Vector3(inner.xMin, y1, inner.yMax), Vector3.down);
            // Inside faces of the four walls, cut exactly where the outer wall was (the openings it recorded), so every
            // hole goes right through and nothing seen from inside is the back of a face.
            for (int side = 0; side < 4; side++)
            {
                var (a0, b0) = FaceRun(inner, side);
                var nrm = -SideOut3(side);
                var holes = new List<Vector4>();
                foreach (var h in b.groundHoles[side]) holes.Add(new Vector4(h.x - t, h.y - t, h.z, Mathf.Min(h.w, y1 - y0)));
                m.WallWithHoles(_plasterIn, a0 + Vector3.up * y0, b0 + Vector3.up * y0, y1 - y0, nrm, holes);
            }
            // A partition with a doorway across the longer axis when the room is big.
            if (inner.width > 7f || inner.height > 7f)
            {
                bool alongX = inner.width < inner.height;
                float at = alongX ? Mathf.Lerp(inner.yMin, inner.yMax, 0.55f) : Mathf.Lerp(inner.xMin, inner.xMax, 0.55f);
                float span = alongX ? inner.width : inner.height;
                float door = span * 0.5f;
                Vector3 A = alongX ? new Vector3(inner.xMin, y0, at) : new Vector3(at, y0, inner.yMin);
                Vector3 B = alongX ? new Vector3(inner.xMax, y0, at) : new Vector3(at, y0, inner.yMax);
                var hole = new List<Vector4> { new Vector4(door - 0.65f, door + 0.65f, 0f, 2.2f) };
                Vector3 n = alongX ? Vector3.forward : Vector3.right;
                m.WallWithHoles(_plasterIn, A - n * 0.06f, B - n * 0.06f, y1 - y0, -n, hole);
                m.WallWithHoles(_plasterIn, A + n * 0.06f, B + n * 0.06f, y1 - y0, n, hole);
            }
            // Furniture against the walls, clear of the door.
            string[] pool = { "WoodenTable_01", "painted_wooden_chair_01", "painted_wooden_cabinet", "painted_wooden_shelves", "sofa_02", "old_bed_frame",
                              "Television_01", "wooden_bookshelf_worn", "vintage_cabinet_01", "painted_wooden_chair_01", "Shelf_01", "vintage_suitcase" };
            int n2 = rng.Next(3, 6);
            for (int i = 0; i < n2; i++)
            {
                string id = pool[rng.Next(pool.Length)];
                int side = rng.Next(4);
                if (side == b.door) side = (side + 2) % 4;
                var (a0, b0) = FaceRun(inner, side);
                var at = Vector3.Lerp(a0, b0, Rand(rng, 0.2f, 0.8f)) - SideOut3(side) * 0.55f;
                at.y = y0;
                PlaceTown(props, Prop(id), at, Mathf.Atan2(-SideOut3(side).x, -SideOut3(side).z) * Mathf.Rad2Deg);
            }
            PlaceTown(props, Prop("ceiling_fan"), new Vector3(inner.center.x, y1 - 0.6f, inner.center.y), 0f, 0.5f, navBlock: false);
            PlaceTown(props, Prop("throw_pillows_01"), new Vector3(inner.center.x + 0.8f, y0, inner.center.y - 0.6f), (float)rng.NextDouble() * 360f, navBlock: false);
        }

        // ==================================================================
        // Building kinds
        // ==================================================================
        private static Mass NewMass(Rect r, System.Random rng, int floors, bool old)
        {
            var (low, _) = GroundRange(r);
            var b = new Mass
            {
                r = r, rng = rng, floors = floors, oldStyle = old,
                baseY = FloorLevel(r), groundMin = low,
                wall = old ? _oldPlasters[rng.Next(_oldPlasters.Length)] : PickPlaster(rng),
                glass = rng.NextDouble() < 0.5 ? _glassDark : _glassBlue,
                frame = rng.NextDouble() < 0.6 ? _frameAlu : rng.NextDouble() < 0.6 ? _frameWhite : _frameDark,
                bay = old ? Rand(rng, 2.6f, 3.6f) : Rand(rng, 3.0f, 3.8f),
                windowW = Rand(rng, 1.1f, 1.6f), windowH = Rand(rng, 1.3f, 1.6f),
                parapet = old ? Rand(rng, 0.5f, 1.0f) : Rand(rng, 0.9f, 1.2f),
            };
            for (int s = 0; s < 4; s++) b.faces[s] = old ? FaceKind.OldWindows : FaceKind.Windows;
            return b;
        }

        private static Material PickPlaster(System.Random rng)
        {
            double p = rng.NextDouble();
            // Mostly white, cream and sand; a few pastel ones, as a real street has.
            int i = p < 0.18 ? 0 : p < 0.32 ? 1 : p < 0.48 ? 2 : p < 0.60 ? 3 : p < 0.70 ? 4 : p < 0.78 ? 5 : p < 0.86 ? 6 : p < 0.94 ? 7 : p < 0.97 ? 8 : rng.NextDouble() < 0.5 ? 9 : 10;
            return _plasters[i];
        }

        /// <summary>Marks the faces of a mass that touch another building (no windows in a party wall).</summary>
        private static void PartyWalls(Mass b)
        {
            for (int s = 0; s < 4; s++)
            {
                Vector2 c = SideCentre(b.r, s) + SideOut(s) * 0.4f;
                foreach (var l in _townLots)
                {
                    if (l.r.Overlaps(b.r) || l.kind == LotKind.Vacant || l.kind == LotKind.Villa || l.kind == LotKind.Compound || l.kind == LotKind.Ruin) continue;
                    if (l.r.Contains(c)) { b.faces[s] = FaceKind.Blank; break; }
                }
            }
        }

        private static void BuildApartment(TownLot lot, System.Random rng, Transform props)
        {
            var b = NewMass(lot.r, rng, rng.Next(4, 7), false);
            b.groundH = 4.2f;
            b.balconies = true;
            b.unfinished = rng.NextDouble() < 0.28;
            PartyWalls(b);
            b.faces[lot.front] = FaceKind.Shops;
            BuildMass(b, props);
            FacadeAC(b, props, lot.front);
        }

        private static void BuildCommercial(TownLot lot, System.Random rng, Transform props, int floors = -1)
        {
            var b = NewMass(lot.r, rng, floors > 0 ? floors : rng.Next(2, 4), false);
            b.groundH = 4.0f;
            b.unfinished = rng.NextDouble() < 0.35;
            PartyWalls(b);
            b.faces[lot.front] = FaceKind.Shops;
            BuildMass(b, props);
            FacadeAC(b, props, lot.front);
        }

        /// <summary>Split air-conditioners hung on a facade under some of its windows.</summary>
        private static void FacadeAC(Mass b, Transform props, int side)
        {
            var prefab = Prop("exterior_aircon_unit");
            if (prefab == null) return;
            var rng = b.rng;
            var (a0, b0) = FaceRun(b.r, side);
            var nrm = SideOut3(side);
            float len = Vector3.Distance(a0, b0);
            for (int f = 1; f < b.floors; f++)
                for (float s = b.bay * 0.5f; s < len; s += b.bay)
                {
                    if (rng.NextDouble() > 0.28) continue;
                    var p = Vector3.Lerp(a0, b0, (s + 0.95f) / len) + nrm * 0.36f;
                    p.y = b.FloorY(f) + 0.25f;
                    PlaceTown(props, prefab, p, Mathf.Atan2(nrm.x, nrm.z) * Mathf.Rad2Deg + 180f, 0.62f);
                }
        }

        /// <summary>A walled villa: yard, gate, the house set back. Returns whether it can be entered.</summary>
        private static bool BuildVilla(TownLot lot, System.Random rng, Transform props)
        {
            var r = lot.r;
            int f = lot.front;
            // The side gaps take the outside stair (1.6 m) with room to spare before the compound wall.
            float front = Rand(rng, 4f, 6.5f), sideGap = Rand(rng, 2.0f, 2.8f), rear = Rand(rng, 1.5f, 2.5f);
            Rect house = f == 0 ? Rect.MinMaxRect(r.xMin + sideGap, r.yMin + rear, r.xMax - sideGap, r.yMax - front)
                       : f == 1 ? Rect.MinMaxRect(r.xMin + sideGap, r.yMin + front, r.xMax - sideGap, r.yMax - rear)
                       : f == 2 ? Rect.MinMaxRect(r.xMin + rear, r.yMin + sideGap, r.xMax - front, r.yMax - sideGap)
                                : Rect.MinMaxRect(r.xMin + front, r.yMin + sideGap, r.xMax - rear, r.yMax - sideGap);
            if (house.width < 7f || house.height < 7f) { lot.kind = LotKind.Row; return BuildRow(lot, rng, props); }
            var b = NewMass(house, rng, rng.Next(1, 4), false);
            b.door = f;
            b.unfinished = rng.NextDouble() < 0.3;
            b.enterable = b.floors <= 2 && rng.NextDouble() < 0.7;
            // The way up: an outside stair along a side wall with room for it (planned first: the parapet is cut for it).
            if (b.enterable) PlanStair(b, f);
            BuildMass(b, props);
            BuildStair(b);
            // A house that can be entered has its gate open: shut, the yard, the stair and the roof baked as one sealed island.
            var gate = CompoundWall(r, f, rng, gate: true, canShut: !b.enterable);
            // A car in the yard on some, parked along the wall beside the gate (on the side away from the house door in the
            // wall), close in to the wall. Parked across the back of the gate it closed the yard off; parked mid-yard in a
            // 4.5 m yard it left 1.3 m either side, too narrow for an agent, and cut the yard in two.
            if (gate.built && rng.NextDouble() < 0.55)
            {
                float side = gate.doorSide > 0f ? -1f : 1f;
                float d = gate.at + side * 4.6f;
                if (d - 2.8f > 0.3f && d + 2.8f < gate.len - 0.3f)
                {
                    Vector2 cp = gate.start + gate.along * d - SideOut(f) * 1.5f;
                    _yardCars.Add(new Vector4(cp.x, 0f, cp.y, Mathf.Atan2(gate.along.x, gate.along.y) * Mathf.Rad2Deg + (rng.NextDouble() < 0.5 ? 0f : 180f)));
                }
            }
            // A palm or two in the yard.
            if (rng.NextDouble() < 0.6)
            {
                Vector2 pc = SideCentre(r, f) - SideOut(f) * (front * 0.5f) + Perp(f) * (r.width * 0.3f * (rng.NextDouble() < 0.5 ? -1f : 1f));
                PlaceTown(props, PackPrefab(rng.NextDouble() < 0.5 ? "Palm_SingleBended" : "Palm_SingleStraight"), new Vector3(pc.x, GroundHeightAt(pc.x, pc.y), pc.y),
                          (float)rng.NextDouble() * 360f, Rand(rng, 6f, 9f), navBlock: false);
            }
            return b.enterable;
        }

        private static Vector2 Perp(int side) => side < 2 ? Vector2.right : Vector2.up;

        /// <summary>
        /// Chooses where a concrete stair goes up a side wall to the roof: a side perpendicular to the front, long enough
        /// for the flight's run plus its landing. Sets the parapet gap on the mass (BuildMass cuts it), or leaves the roof
        /// off the bake when no wall is long enough.
        /// </summary>
        // 1.6 m wide with a 1.8 m gap in the parapet: at 1.2 and 1.4 an agent (0.5 m radius) had a strip of one voxel up the
        // flight, and every villa roof with a stair baked as an island nothing could reach.
        private const float TownStairWidth = 1.6f, TownTread = 0.28f, TownRiser = 0.18f, TownStairStart = 0.4f, TownLandingLen = 1.8f;

        private static void PlanStair(Mass b, int front)
        {
            int[] sides = front < 2 ? new[] { 2, 3 } : new[] { 0, 1 };
            float rise = b.Top - b.baseY + 0.05f;
            float run = Mathf.CeilToInt(rise / TownRiser) * TownTread;
            foreach (int s in sides)
            {
                var (a0, b0) = FaceRun(b.r, s);
                float len = Vector3.Distance(a0, b0);
                if (len < TownStairStart + run + TownLandingLen + 0.3f) continue;
                b.stairSide = s;
                b.stairU0 = TownStairStart + run - 0.05f;
                b.stairU1 = b.stairU0 + TownLandingLen;
                b.roofWalk = true;
                return;
            }
            b.roofWalk = false;
        }

        /// <summary>The flight, its landing level with the roof at the parapet gap, and posts for a rail on the open side.</summary>
        private static void BuildStair(Mass b)
        {
            if (b.stairSide < 0) return;
            var m = Chunk(b.r.center);
            int s = b.stairSide;
            var (a0, b0) = FaceRun(b.r, s);
            float len = Vector3.Distance(a0, b0);
            Vector3 u = (b0 - a0) / len, nrm = SideOut3(s);
            float rise = b.Top - b.baseY + 0.05f;
            int steps = Mathf.CeilToInt(rise / TownRiser);
            float r = rise / steps;
            var rot = Quaternion.LookRotation(u);
            Vector3 lane = a0 + nrm * (TownStairWidth * 0.5f + 0.02f);
            float ground = GroundHeightAt(lane.x, lane.z);
            float baseLow = Mathf.Min(ground, b.baseY) - 0.25f;
            for (int i = 0; i < steps; i++)
            {
                float topY = b.baseY + r * (i + 1);
                var c = lane + u * (TownStairStart + TownTread * (i + 0.5f));
                m.Box(_coping, new Vector3(c.x, (baseLow + topY) * 0.5f, c.z), new Vector3(TownStairWidth, topY - baseLow, TownTread), rot, bottom: false);
            }
            // Landing: from the end of the flight across the parapet gap, its top at the roof's level.
            float roofTop = b.Top + 0.05f;
            var lc = lane + u * (b.stairU0 + TownLandingLen * 0.5f);
            m.Box(_coping, new Vector3(lc.x, (baseLow + roofTop) * 0.5f, lc.z), new Vector3(TownStairWidth, roofTop - baseLow, TownLandingLen), rot, bottom: false);
            // Rail posts and a top rail on the open side of the flight.
            Vector3 edge = a0 + nrm * (TownStairWidth + 0.02f);
            for (float d = TownStairStart; d <= b.stairU1; d += 1.0f)
            {
                float y = Mathf.Min(roofTop, b.baseY + Mathf.Max(0f, d - TownStairStart) / TownTread * r);
                var p = edge + u * d;
                m.Box(_steel, new Vector3(p.x, y + 0.5f, p.z), new Vector3(0.04f, 1.0f, 0.04f), Quaternion.identity);
            }
            _townDoors.Add(lane + u * (TownStairStart - 0.6f) + Vector3.up * ground);
        }

        /// <summary>The wall round a plot, 2.2 m, with a car gate and a door on the street side.</summary>
        /// <summary>Where a compound wall's gate went: its centre <see cref="at"/> metres along the front from
        /// <see cref="start"/>, and the side of it the door in the wall is on.</summary>
        private struct GateSpot { public bool built, shut; public Vector2 start, along; public float at, len, doorSide; }

        /// <summary>
        /// The wall round a plot, 2.2 m, with a car gate and a door on the street side. A shut gate (only where
        /// <paramref name="canShut"/>) closes the plot to the navmesh altogether: everything inside the wall is put off
        /// the bake, or the yard and anything on it would be an island no agent could reach.
        /// </summary>
        private static GateSpot CompoundWall(Rect r, int front, System.Random rng, bool gate, Material wallMat = null, bool canShut = true)
        {
            var spot = new GateSpot();
            var m = Chunk(r.center);
            var wall = wallMat ?? PickPlaster(rng);
            const float H = 2.2f, T = 0.22f;
            for (int s = 0; s < 4; s++)
            {
                var (a0, b0) = FaceRun(r, s);
                var nrm = SideOut3(s);
                float len = Vector3.Distance(a0, b0);
                Vector3 u = (b0 - a0) / len;
                var holes = new List<Vector4>();
                float y0 = Mathf.Min(GroundHeightAt(a0.x, a0.z), GroundHeightAt(b0.x, b0.z), GroundHeightAt((a0.x + b0.x) * 0.5f, (a0.z + b0.z) * 0.5f)) - 0.4f;
                float yTop = Mathf.Max(GroundHeightAt(a0.x, a0.z), GroundHeightAt(b0.x, b0.z)) + H;
                if (s == front && gate)
                {
                    float gc = len * Rand(rng, 0.35f, 0.65f);
                    holes.Add(new Vector4(gc - 1.7f, gc + 1.7f, 0f, H + 0.4f + (yTop - H - y0 - 0.4f)));
                    float dc = gc + (gc > len * 0.5f ? -2.8f : 2.8f);
                    holes.Add(new Vector4(dc - 0.6f, dc + 0.6f, 0f, 2.3f + 0.4f));
                    // Gate leaves: open or shut.
                    var gmat = rng.NextDouble() < 0.5 ? _gateGreen : _gateBlue;
                    var gp = a0 + u * gc;
                    float gy = GroundHeightAt(gp.x, gp.z);
                    spot = new GateSpot { built = true, start = new Vector2(a0.x, a0.z), along = new Vector2(u.x, u.z), at = gc, len = len, doorSide = Mathf.Sign(dc - gc) };
                    if (rng.NextDouble() < 0.4 && canShut)
                    {
                        spot.shut = true;
                        m.Box(gmat, gp - u * 0.85f + Vector3.up * (gy + 1.1f), new Vector3(1.7f, 2.0f, 0.06f), Quaternion.LookRotation(nrm));
                        m.Box(gmat, gp + u * 0.85f + Vector3.up * (gy + 1.1f), new Vector3(1.7f, 2.0f, 0.06f), Quaternion.LookRotation(nrm));
                    }
                    else
                        m.Box(gmat, gp - u * 1.7f - nrm * 0.85f + Vector3.up * (gy + 1.1f), new Vector3(0.06f, 2.0f, 1.7f), Quaternion.LookRotation(nrm));
                    var door = new Vector3(dc, 0, 0);
                    _townDoors.Add(a0 + u * dc + nrm * 0.8f + Vector3.up * gy);
                    _ = door;
                }
                m.WallWithHoles(wall, a0 + Vector3.up * y0, b0 + Vector3.up * y0, yTop - y0, nrm, holes);
                m.WallWithHoles(wall, a0 - nrm * T + Vector3.up * y0, b0 - nrm * T + Vector3.up * y0, yTop - y0, -nrm, holes);
                // Coping, broken at the openings.
                float cur = 0f;
                var cuts = new List<Vector4>(holes); cuts.Sort((p, q) => p.x.CompareTo(q.x));
                foreach (var h in cuts)
                {
                    if (h.x > cur) m.Box(_coping, a0 + u * ((cur + h.x) * 0.5f) - nrm * T * 0.5f + Vector3.up * (yTop + 0.04f), new Vector3(h.x - cur, 0.08f, T + 0.08f), Quaternion.LookRotation(nrm));
                    // Jambs through the wall's thickness.
                    foreach (float e in new[] { h.x, h.y })
                        m.Quad(wall, a0 + u * e + Vector3.up * y0, a0 + u * e - nrm * T + Vector3.up * y0, a0 + u * e - nrm * T + Vector3.up * yTop, a0 + u * e + Vector3.up * yTop, e == h.x ? u : -u);
                    cur = h.y;
                }
                if (len > cur) m.Box(_coping, a0 + u * ((cur + len) * 0.5f) - nrm * T * 0.5f + Vector3.up * (yTop + 0.04f), new Vector3(len - cur, 0.08f, T + 0.08f), Quaternion.LookRotation(nrm));
            }
            if (spot.shut)
            {
                var (low, high) = GroundRange(r);
                NoEntryBox(r, low - 1f, high + 16f, T + 0.05f);
            }
            return spot;
        }

        /// <summary>A row house built to the pavement: two to four floors, a door in the middle. Returns whether it can be entered.</summary>
        private static bool BuildRow(TownLot lot, System.Random rng, Transform props)
        {
            var b = NewMass(lot.r, rng, rng.Next(2, 5), false);
            b.door = lot.front;
            b.unfinished = rng.NextDouble() < 0.33;
            b.balconies = rng.NextDouble() < 0.4;
            PartyWalls(b);
            b.enterable = b.floors <= 2 && rng.NextDouble() < 0.55;
            BuildMass(b, props);
            if (rng.NextDouble() < 0.5) FacadeAC(b, props, lot.front);
            return b.enterable;
        }

        /// <summary>An empty lot: rubble, a broken wall, a burnt-out car or a few parked ones, weeds.</summary>
        private static void BuildVacant(TownLot lot, System.Random rng, Transform props)
        {
            var r = lot.r;
            var m = Chunk(r.center);
            if (rng.NextDouble() < 0.6)
            {
                // What is left of a wall.
                var (a0, b0) = FaceRun(r, (lot.front + 2) % 4);
                float y = GroundHeightAt((a0.x + b0.x) * 0.5f, (a0.z + b0.z) * 0.5f);
                float len = Vector3.Distance(a0, b0) * Rand(rng, 0.3f, 0.7f);
                m.Box(_block, Vector3.Lerp(a0, b0, 0.3f) + Vector3.up * (y + 0.6f), new Vector3(len, 1.6f, 0.2f), Quaternion.LookRotation(SideOut3(lot.front)));
            }
            int junk = rng.Next(2, 6);
            string[] pool = { "old_tyre", "cement_bag", "Barrel_01", "barrel_03", "trashbag", "cardboard_box_01", "rusted_wheel_rim_01", "plastic_crate_03", "concrete_road_barrier" };
            for (int i = 0; i < junk; i++)
            {
                var p = new Vector2(Rand(rng, r.xMin + 1.5f, r.xMax - 1.5f), Rand(rng, r.yMin + 1.5f, r.yMax - 1.5f));
                PlaceTown(props, Prop(pool[rng.Next(pool.Length)]), new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y), (float)rng.NextDouble() * 360f);
            }
            _vacantLots.Add(r);
        }

        /// <summary>Empty lots, for parked cars and the occasional wreck (FPSKitTownLife).</summary>
        private static readonly List<Rect> _vacantLots = new List<Rect>();

        // ------------------------------------------------------------------ old town
        private static bool BuildOldHouse(TownLot lot, System.Random rng, Transform props)
        {
            var r = lot.r;
            var b = NewMass(r, rng, rng.NextDouble() < 0.45 ? 1 : rng.NextDouble() < 0.75 ? 2 : 3, true);
            b.groundH = 3.0f;
            b.door = lot.front;
            PartyWalls(b);
            b.faces[lot.front] = FaceKind.OldWindows;
            b.enterable = b.floors <= 2 && r.width >= 7f && r.height >= 7f && rng.NextDouble() < 0.5;
            if (b.enterable) b.windowW = 0.9f;
            BuildMass(b, props);
            return b.enterable;
        }

        private static void BuildCourtyardHouse(TownLot lot, System.Random rng, Transform props)
        {
            var r = lot.r;
            float wing = Mathf.Min(5f, Mathf.Min(r.width, r.height) * 0.3f);
            int floors = rng.NextDouble() < 0.5 ? 2 : 1;
            var wall = _oldPlasters[rng.Next(_oldPlasters.Length)];
            var wings = new[]
            {
                Rect.MinMaxRect(r.xMin, r.yMax - wing, r.xMax, r.yMax),
                Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, r.yMin + wing),
                Rect.MinMaxRect(r.xMax - wing, r.yMin + wing, r.xMax, r.yMax - wing),
                Rect.MinMaxRect(r.xMin, r.yMin + wing, r.xMin + wing, r.yMax - wing),
            };
            float level = FloorLevel(r);
            for (int i = 0; i < 4; i++)
            {
                var b = NewMass(wings[i], rng, floors, true);
                b.wall = wall; b.baseY = level; b.groundH = 3.0f;
                // The wings' inner faces look into the courtyard; where wings meet, no windows.
                if (i < 2) { b.faces[2] = FaceKind.OldWindows; b.faces[3] = FaceKind.OldWindows; }
                else { b.faces[0] = FaceKind.Blank; b.faces[1] = FaceKind.Blank; }
                if (i == lot.front) b.door = lot.front;
                BuildMass(b, props);
            }
            // The courtyard: a paved floor, a tree, a well.
            var court = Rect.MinMaxRect(r.xMin + wing, r.yMin + wing, r.xMax - wing, r.yMax - wing);
            // Closed on four sides (its doors are shut): kept off the bake, or it is an island nothing can reach.
            NoEntryBox(court, level - 2f, level + 8f, 0f);
            var m = Chunk(r.center);
            float y = level - 0.02f;
            m.Quad(_paversPlaza, new Vector3(court.xMin, y, court.yMin), new Vector3(court.xMax, y, court.yMin), new Vector3(court.xMax, y, court.yMax), new Vector3(court.xMin, y, court.yMax), Vector3.up);
            if (rng.NextDouble() < 0.6)
                PlaceTown(props, PackPrefab("Palm_SingleStraight"), new Vector3(court.center.x, y, court.center.y), (float)rng.NextDouble() * 360f, Rand(rng, 6f, 8f), navBlock: false);
            else PlaceTown(props, Prop("ceramic_pot"), new Vector3(court.center.x, y, court.center.y), 0f, 0.8f);
        }

        private static void BuildRuin(TownLot lot, System.Random rng)
        {
            var r = lot.r;
            var m = Chunk(r.center);
            var wall = _oldPlasters[rng.Next(_oldPlasters.Length)];
            // Broken walls of uneven height on two or three sides, rubble mounded inside.
            for (int s = 0; s < 4; s++)
            {
                if (rng.NextDouble() < 0.3) continue;
                var (a0, b0) = FaceRun(r, s);
                float len = Vector3.Distance(a0, b0);
                Vector3 u = (b0 - a0) / len;
                var nrm = SideOut3(s);
                for (float d = 0f; d < len; d += 1.2f)
                {
                    float h = Rand(rng, 0.4f, 3.2f) * (rng.NextDouble() < 0.2 ? 0.2f : 1f);
                    var p = a0 + u * (d + 0.6f) - nrm * 0.15f;
                    float g = GroundHeightAt(p.x, p.z);
                    m.Box(wall, p + Vector3.up * (g + h * 0.5f - 0.3f), new Vector3(Mathf.Min(1.2f, len - d), h + 0.6f, 0.3f), Quaternion.LookRotation(nrm));
                }
            }
            var rubble = TownMat("Rubble", "rubble", new Color(1f, 0.95f, 0.88f), 3f);
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector2(Rand(rng, r.xMin + 1f, r.xMax - 1f), Rand(rng, r.yMin + 1f, r.yMax - 1f));
                var mesh = BoulderMesh(rng.Next(1000), 0.4f, 0.35f);
                float sx = Rand(rng, 2f, 3.5f), sz = Rand(rng, 2f, 3.5f);
                var go = MeshObject(_volumes.parent, "Rubble", mesh, rubble, new Vector3(p.x, LowestGroundIn(p.x, p.y, Mathf.Max(sx, sz) * 0.6f) - 0.15f, p.y),
                                    Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), new Vector3(sx, Rand(rng, 0.6f, 1.2f), sz),
                                    LayerMask.NameToLayer("Environment"), "Concrete");
                NoStanding(go);
            }
        }

        /// <summary>A farmstead in the western quarter: a walled yard with a low house, a store and palms.</summary>
        private static void BuildCompound(TownLot lot, System.Random rng, Transform props)
        {
            var r = lot.r;
            int f = lot.front;
            var wall = _oldPlasters[rng.Next(_oldPlasters.Length)];
            CompoundWall(r, f, rng, gate: true, wallMat: wall);
            // The house against the back wall.
            float depth = Mathf.Min(9f, r.height * 0.4f);
            int back = f ^ 1;
            Rect house = back == 0 ? Rect.MinMaxRect(r.xMin + 0.4f, r.yMax - depth, r.xMax - 0.4f, r.yMax - 0.4f)
                       : back == 1 ? Rect.MinMaxRect(r.xMin + 0.4f, r.yMin + 0.4f, r.xMax - 0.4f, r.yMin + depth)
                       : back == 2 ? Rect.MinMaxRect(r.xMax - depth, r.yMin + 0.4f, r.xMax - 0.4f, r.yMax - 0.4f)
                                   : Rect.MinMaxRect(r.xMin + 0.4f, r.yMin + 0.4f, r.xMin + depth, r.yMax - 0.4f);
            var b = NewMass(house, rng, rng.NextDouble() < 0.7 ? 1 : 2, true);
            b.wall = wall; b.groundH = 3.0f; b.door = f;
            b.faces[back] = FaceKind.Blank;
            BuildMass(b, props);
            for (int i = 0; i < rng.Next(1, 4); i++)
            {
                var p = new Vector2(Rand(rng, r.xMin + 3f, r.xMax - 3f), Rand(rng, r.yMin + 3f, r.yMax - 3f));
                if (house.Contains(p)) continue;
                PlaceTown(props, PackPrefab(rng.NextDouble() < 0.5 ? "Palm_DualStraight" : "Palm_SingleBended"), new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y),
                          (float)rng.NextDouble() * 360f, Rand(rng, 7f, 10f), navBlock: false);
            }
            for (int i = 0; i < rng.Next(2, 5); i++)
            {
                var p = new Vector2(Rand(rng, r.xMin + 2f, r.xMax - 2f), Rand(rng, r.yMin + 2f, r.yMax - 2f));
                if (house.Contains(p)) continue;
                string[] pool = { "wooden_crate_01", "Barrel_01", "wicker_basket_01", "plastic_jerrycan", "wooden_barrels_01", "propane_tank", "wooden_ladder" };
                PlaceTown(props, Prop(pool[rng.Next(pool.Length)]), new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y), (float)rng.NextDouble() * 360f);
            }
            _townSolids.Add(Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, r.yMin + 0.3f));
        }
    }
}
#endif
