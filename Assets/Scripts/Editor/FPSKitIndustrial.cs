#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The industrial zone: a working plant laid out on a road grid, rather than a yard
    /// with crates dropped in it.
    ///
    /// <b>Why this is a separate layout mode and not more props.</b> The old Industrial
    /// Warehouse was a 110x150 box with air-conditioning units and shipping containers
    /// scattered over a flat floor, and no amount of extra scatter fixes that, because
    /// the problem is not density -- it is that nothing in it was built by anyone for
    /// any purpose. A real works reads as industrial before you can name a single object
    /// in it, and the reasons are structural:
    ///
    ///   * <b>it is organised by roads</b>, so there are streets, blocks and frontages,
    ///     and every building faces something;
    ///   * <b>it is zoned</b> -- tanks stand with tanks, containers stack in a yard,
    ///     the big sheds share a row -- because a plant is laid out by what the work
    ///     needs to be near, not by a random number generator;
    ///   * <b>it is fenced, gated and signed</b>, which is what says "you are inside
    ///     somewhere that belongs to somebody";
    ///   * <b>it has a second storey</b> -- catwalks over the pipe runs, stairs up the
    ///     shed walls -- because a plant is a place people work above your head.
    ///
    /// The art comes from the pack's own prefabs rather than from tinted boxes, which is
    /// the other half of the answer: <c>Hangar_v2</c> and <c>Hangar_v3</c> carry
    /// non-convex mesh colliders, so they are hollow and the player can walk inside
    /// them, and that one fact is worth more to this arena than any amount of greybox.
    /// Everything the pack does not have -- catwalks, stairs, railings, warning signs --
    /// is built here, because the alternative is not having them.
    ///
    /// <para>
    /// <b>Every prefab lookup is allowed to fail.</b> The pack is third-party and lives
    /// outside <c>FPSKit_Generated</c>, so a project that does not have it must still
    /// build a playable arena rather than throw halfway through. <see cref="Pack"/>
    /// returns null and warns once; every caller either falls back to a built shape or
    /// skips that piece of dressing.
    /// </para>
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        // ==================================================================
        // The pack
        // ==================================================================
        private const string PackRoot = "Assets/RPG_FPS_game_assets_industrial";

        private static readonly Dictionary<string, GameObject> _packCache =
            new Dictionary<string, GameObject>();
        private static readonly HashSet<string> _packMissing = new HashSet<string>();

        /// <summary>
        /// Loads one prefab out of the art pack, cached, warning once for anything that
        /// is not there. Null is a supported answer -- see the class summary.
        /// </summary>
        private static GameObject Pack(string relativePath)
        {
            if (_packCache.TryGetValue(relativePath, out var cached)) return cached;

            string path = $"{PackRoot}/{relativePath}";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null && _packMissing.Add(relativePath))
                Debug.LogWarning($"[FPSKit] art pack prefab missing: {path}. " +
                                 "The zone will be built without it.");

            _packCache[relativePath] = prefab;
            return prefab;
        }

        /// <summary>
        /// Drops a pack prefab into the scene, on the environment layer, marked static so
        /// it batches. Returns null if the prefab was missing, which callers treat as
        /// "skip this piece".
        /// </summary>
        private static GameObject Place(Transform parent, string relativePath, Vector3 position,
                                        float yaw = 0f, float scale = 1f)
        {
            var prefab = Pack(relativePath);
            if (prefab == null) return null;

            var go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (go == null) return null;

            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            if (!Mathf.Approximately(scale, 1f)) go.transform.localScale = Vector3.one * scale;

            SetLayerDeep(go, LayerMask.NameToLayer("Environment"));
            // Batching and occlusion only. Navigation is deliberately not flagged here:
            // the kit bakes with a NavMeshSurface, which collects by geometry rather
            // than by static flag, so the flag would be a second switch saying nothing.
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic |
                                                       StaticEditorFlags.OccluderStatic |
                                                       StaticEditorFlags.OccludeeStatic);
            return go;
        }

        private static void SetLayerDeep(GameObject go, int layer)
        {
            if (layer < 0) return;
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerDeep(child.gameObject, layer);
        }

        // Named so the layout reads as a plan rather than as a list of file paths.
        private const string ShedWide   = "Buildings/Industrial/Hangars/Hangar_v2/Hangar_v2.prefab";
        private const string ShedLong   = "Buildings/Industrial/Hangars/Hangar_v3/Hangar_v3.prefab";
        private const string ShedSmall  = "Buildings/Industrial/Hangars/Hangar_v3/Hangar_v3_basic.prefab";
        private const string Silo       = "Buildings/Industrial/Hangars/Hangar_v4/Hangar_v4.prefab";
        private const string OutBuild   = "Buildings/Industrial/Hangars/Hangar_v2/Hangar_v2_outbuilding.prefab";

        private const string TankWide   = "Oil_tanks/Oil_tank_v1/Oil_tank_v1.prefab";
        private const string TankTall   = "Oil_tanks/Oil_tank_v2/Oil_tank_v2.prefab";

        private const string RoadStraight = "Roads/Road_sets/Road_set_v1/Road_set_v1_b_floor.prefab";
        private const string RoadBendA    = "Roads/Road_sets/Road_set_v1/Road_set_v1_b_bend_1.prefab";

        private const string FenceRun   = "Fences/Concrete_fences/Concrete_fence_v2/Concrete_fence_v2_S.prefab";
        private const string FenceCorner= "Fences/Concrete_fences/Concrete_fence_v2/Concrete_fence_v2_L.prefab";
        private const string FenceGate  = "Fences/Concrete_fences/Concrete_fence_v2/Concrete_fence_v2_Gate.prefab";
        private const string RoadBlock  = "Fences/Road_blocks/Road_block_v1/Road_block_v1.prefab";

        private const string BoxClosed  = "Containers/Cargo_container_v1/Cargo_container_v1_LD2close.prefab";
        private const string BoxThrough = "Containers/Cargo_container_v1/Cargo_container_v1_LD2through.prefab";
        private const string BoxOpen    = "Containers/Cargo_container_v1/Cargo_container_v1_LD2open.prefab";

        private const string PipeTower  = "Other_props/Pipes/Industrial_pipes/Industrial_pipe_v1/Industrial_pipe_v1.prefab";
        private const string PipeVent   = "Other_props/Pipes/Pipe_sets/Pipes_set_v1/Pipes_set_v1_vent_1.prefab";
        private const string Generator  = "Other_props/Generators/Generator_v1/Generator_v1.prefab";
        private const string ElecBox    = "Other_props/Electric_box/Electric_box_v2/Electric_box_v2.prefab";
        private const string Pallets    = "Other_props/Palets/Palet_v1/Palet_v1_set.prefab";
        private const string PalletOne  = "Other_props/Palets/Palet_v1/Palet_v1_single.prefab";
        private const string BagsPallet = "Other_props/Palets/Bags_on_pallet_v1/Bags_on_pallet_v1_1.prefab";
        private const string CrateBig   = "Boxes/Wooden_box_v1/Wooden_box_v1_LD1.prefab";
        private const string CrateFlat  = "Boxes/Wooden_box_v1/Wooden_box_v1_LD1square.prefab";
        private const string BarrelSet  = "Barrels/Barrel_v3/Barrel_v3_quadro.prefab";
        private const string BarrelOne  = "Barrels/Barrel_v3/Barrel_v3_single.prefab";
        private const string Dumpster   = "Dumpsters/Dumpsters_v1/Dumpsters_v1_garbadge.prefab";

        /// <summary>Road tile edge, in metres. The pack's road set is modelled on a 20m grid.</summary>
        private const float Tile = 20f;

        /// <summary>
        /// Whether this arena is one of the big outdoor ones.
        ///
        /// The far clip plane, the spawn ring and the leash all want the same answer for
        /// an open zone and for an industrial zone, and for the opposite reason to a
        /// walled box: there is a horizon to draw, the arena is hundreds of metres
        /// across, and an enemy has a long way to come. Asking the question once stops
        /// the two modes drifting apart on three separate numbers.
        /// </summary>
        private static bool WideArena =>
            _theme != null && (_theme.openZone || _theme.industrialZone);

        // ==================================================================
        // Layout state
        // ==================================================================
        /// <summary>Tiles the road network occupies, so nothing is built on the road.</summary>
        private static readonly HashSet<Vector2Int> _roadTiles = new HashSet<Vector2Int>();

        /// <summary>Blocks between the roads, in tile coordinates, waiting for a district.</summary>
        private static readonly List<RectInt> _blocks = new List<RectInt>();

        private static Material _zoneYard, _zoneApron, _zoneConcrete, _zoneSteel,
                                _zoneRust, _zoneGrate, _zoneHazard, _zoneSign;

        // ==================================================================
        /// <summary>
        /// Builds the whole plant. Order matters: the roads are laid first because every
        /// district is placed relative to them, and the perimeter last because it has to
        /// know where the roads leave the site in order to put a gate there.
        /// </summary>
        private static void BuildIndustrialZone()
        {
            _claimed.Clear();
            _roadTiles.Clear();
            _blocks.Clear();
            _packCache.Clear();
            _packMissing.Clear();

            ClearMeshPool();
            ResetTerrain();

            var root = new GameObject("Arena").transform;
            int layer = LayerMask.NameToLayer("Environment");
            int backdrop = LayerMask.NameToLayer("Backdrop");
            if (backdrop < 0) backdrop = layer;

            var rng = new System.Random(_theme.randomSeed);
            float half = _theme.arenaSize * 0.5f;

            ResolveSurfaceMaterials();
            ResolveZoneMaterials();

            // The spawn, before anything can be dropped on it. Wide, because the first
            // thing the player sees should be the plant and not the side of a shed.
            Claim(0f, 0f, 26f);

            BuildZoneGround(root, layer, backdrop, half);

            PlanStreets(half);
            BuildStreets(root, half);
            BuildStreetFurniture(root, layer, rng);
            BuildBlocks(root, layer, rng, half);

            BuildPerimeterFence(root, layer, half);
            BuildZoneBackdrop(root, backdrop, rng, half);
            BuildAccentLights(root, rng);
        }

        // ==================================================================
        // Materials
        // ==================================================================
        /// <summary>Loads one material out of the art pack. Null is a supported answer.</summary>
        private static Material PackMaterial(string relativePath)
            => AssetDatabase.LoadAssetAtPath<Material>($"{PackRoot}/{relativePath}");

        /// <summary>
        /// A tiled copy of a pack material with its base colour shifted.
        ///
        /// Tinting one texture two ways is what separates the yard from the roads, and
        /// it beats using two different textures for a reason worth recording: the
        /// obvious move is to floor the yard in the pack's concrete, and the pack's
        /// concrete is a *wall* panel with strong horizontal banding in it. Tiled
        /// seventy times across a 500m site that reads as corrugated iron laid flat over
        /// the entire plant. The asphalt tile has no direction in it and real cracks, so
        /// the same texture pale for hardstanding and dark for carriageway gives the
        /// contrast without inventing a pattern that is not there.
        ///
        /// <para>
        /// <b>The tint multiplies the base map, so brightening needs a value above 1.</b>
        /// Asphalt's albedo is about 0.15, and a tint of 0.66 on it comes out at 0.10 --
        /// darker than it started. The first attempt at a pale yard set a light grey
        /// here and produced a yard indistinguishable from the roads: it compiled, it
        /// built, and it did nothing visible, which is the hardest kind of failure to
        /// catch from a screenshot.
        /// </para>
        /// </summary>
        private static Material TintedTile(Material source, string name, Vector2 tiling,
                                           Color tint)
        {
            var mat = TiledCopy(source, name, tiling);
            if (mat == null) return null;

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            EditorUtility.SetDirty(mat);

            return mat;
        }

        /// <summary>
        /// The surfaces the built parts are dressed in.
        ///
        /// <b>Taken from the art pack wherever the pack has one, and tiled.</b> A flat
        /// colour on a 500m yard is the single most expensive-looking shortcut available
        /// here: the sheds, tanks and containers all carry photographic texture, so
        /// untextured ground between them does not read as clean, it reads as
        /// unfinished, and it makes the pack's own props look pasted on. The tank farm
        /// bund standing in the same concrete as the pack's fences is most of what makes
        /// the built pieces and the bought pieces look like one site.
        ///
        /// <para>
        /// Always through <see cref="TiledCopy"/>, never by touching the pack's own
        /// material. Those are shared assets on disk -- setting a tiling on one would
        /// retexture every prefab that uses it, in this arena and in every other scene,
        /// and it would persist after the build. Same rule as never writing an upgrade
        /// into a WeaponData.
        /// </para>
        ///
        /// Each falls back to a flat colour, because the pack is third-party and a
        /// project without it must still build something playable.
        /// </summary>
        private static void ResolveZoneMaterials()
        {
            float size = Mathf.Max(60f, _theme.arenaSize);

            var asphalt  = PackMaterial("Textures/Asphalt/Seamless_asphalt_v1/Seamless_asphalt_v1.mat");
            var concrete = PackMaterial("Textures/Concrete_wall/UNIConcrete_walls/UNIConcrete_wall_v1/UNIConcrete_wall_v1.mat");
            var lattice  = PackMaterial("Textures/Transparent/Lattice/Lattice_v1/Lattice_v1.mat");
            var steel    = PackMaterial("Other_props/Pipes/Pipe_sets/Pipes_set_v1/Source/Pipes_set_v1.mat");
            var rust     = PackMaterial("Oil_tanks/Oil_tank_v1/Source/Oil_tank_v1.mat");

            // <b>The yard is concrete, and the roads are the pack's asphalt.</b> This
            // is the single change that made the street plan visible. Both surfaces
            // started as the same dark asphalt, and the result from the air was one
            // uniform black field with buildings dotted on it -- the grid of avenues
            // and cross streets that the entire layout is organised around could not be
            // seen at all, from any angle, so the site read as a car park with sheds
            // round the edge. Light concrete hardstanding against dark carriageways is
            // also simply what a real works looks like.
            _zoneYard = TintedTile(asphalt, "ZoneYard", new Vector2(size / 7f, size / 7f),
                                   new Color(2.6f, 2.55f, 2.35f))
                        ?? MakeMaterial("ZoneYard", new Color(0.52f, 0.51f, 0.48f), 0.10f, 0f);


            _zoneConcrete = TiledCopy(concrete, "ZoneConcrete", new Vector2(size / 8f, 3f))
                            ?? MakeMaterial("ZoneConcrete", new Color(0.52f, 0.51f, 0.48f), 0.10f, 0f);

            _zoneSteel = TiledCopy(steel, "ZoneSteel", new Vector2(2f, 2f))
                         ?? MakeMaterial("ZoneSteel", new Color(0.44f, 0.46f, 0.49f), 0.45f, 0.75f);

            _zoneRust = TiledCopy(rust, "ZoneRust", new Vector2(3f, 3f))
                        ?? MakeMaterial("ZoneRust", new Color(0.38f, 0.22f, 0.13f), 0.18f, 0.30f);

            // Grating is the one place the transparent lattice earns its cost: a walkway
            // you can see the ground through reads as industrial instantly, and it is
            // also honest about the drop, which matters when it is a firing position.
            _zoneGrate = TiledCopy(lattice, "ZoneGrate", new Vector2(4f, 12f))
                         ?? MakeMaterial("ZoneGrate", new Color(0.29f, 0.30f, 0.32f), 0.35f, 0.65f);

            // The ground outside the fence, a shade off the yard so the site has a
            // visible edge rather than running on into the haze.
            _zoneApron = MakeMaterial("ZoneApron", new Color(0.34f, 0.33f, 0.30f), 0.05f, 0f);

            // Signs stay generated. The pack has no sign face, and a flat saturated
            // yellow is exactly right for one.
            _zoneHazard = MakeMaterial("ZoneHazard", new Color(0.85f, 0.62f, 0.06f), 0.25f, 0.10f);
            _zoneSign   = MakeMaterial("ZoneSign",   new Color(0.88f, 0.76f, 0.10f), 0.20f, 0.05f);
        }

        // ==================================================================
        // Ground
        // ==================================================================
        /// <summary>
        /// The site slab, and the ground that runs past it to the horizon.
        ///
        /// The apron is the difference between standing in a level and standing in a
        /// place. It carries no collider and is off the navigation bake, so the player
        /// cannot walk out onto it -- the fence stops them -- but the eye needs something
        /// between the last building and the sky or the arena reads as a box however
        /// large it is.
        /// </summary>
        private static void BuildZoneGround(Transform root, int layer, int backdropLayer, float half)
        {
            var slab = GameObject.CreatePrimitive(PrimitiveType.Plane);
            slab.name = "Yard";
            slab.transform.SetParent(root, false);
            slab.transform.localScale = new Vector3(half * 0.2f, 1f, half * 0.2f);
            slab.layer = layer;
            slab.GetComponent<Renderer>().sharedMaterial = _zoneYard;
            GameObjectUtility.SetStaticEditorFlags(slab, StaticEditorFlags.BatchingStatic);
            Hide(slab);

            float apron = Mathf.Max(_theme.apronSize, 400f);
            var outside = GameObject.CreatePrimitive(PrimitiveType.Plane);
            outside.name = "Apron";
            outside.transform.SetParent(root, false);
            outside.transform.localPosition = new Vector3(0f, -0.35f, 0f);
            outside.transform.localScale = new Vector3((half + apron) * 0.2f, 1f, (half + apron) * 0.2f);
            outside.layer = backdropLayer;
            outside.GetComponent<Renderer>().sharedMaterial = _zoneApron;
            Object.DestroyImmediate(outside.GetComponent<Collider>());
            NoStanding(outside);
            Hide(outside);
        }

        // ==================================================================
        // Streets
        // ==================================================================
        /// <summary>
        /// Chooses the street plan and records both the tiles the roads occupy and the
        /// blocks left between them.
        ///
        /// <b>The grid is the whole design.</b> Two avenues and three cross streets over
        /// a 500m site give twelve blocks, which is enough for every district to have its
        /// own frontage and for none of them to be reachable without crossing open ground
        /// somewhere. It also means the player always knows which way is out, which is
        /// what stops a large map being a maze -- and it gives a car somewhere to drive,
        /// which is the other thing the roads are for.
        /// </summary>
        private static void PlanStreets(float half)
        {
            int span = Mathf.RoundToInt(_theme.arenaSize / Tile);      // tiles across
            int from = -span / 2;
            int to = span / 2;

            // The ring road, one tile inside the fence: every block gets a back as well
            // as a front, and the perimeter is patrollable.
            int inFrom = from + 1, inTo = to - 1;

            for (int x = inFrom; x <= inTo; x++)
            {
                _roadTiles.Add(new Vector2Int(x, inFrom));
                _roadTiles.Add(new Vector2Int(x, inTo));
            }
            for (int z = inFrom; z <= inTo; z++)
            {
                _roadTiles.Add(new Vector2Int(inFrom, z));
                _roadTiles.Add(new Vector2Int(inTo, z));
            }

            // Interior streets on a regular grid.
            //
            // <b>The block size is the single number that decides whether this reads as
            // a plant or as a car park.</b> The first cut of this used two avenues and
            // three cross streets over the whole site, which sounds like a lot and
            // produced one central block 220m wide -- and three sheds spread over 220m
            // is not a district, it is three buildings you can see from each other and
            // nothing in between. Real works are cut into blocks of eighty to a hundred
            // metres, which is also about as far as a player will walk without something
            // to look at.
            var xCuts = EvenCuts(inFrom, inTo);
            var zCuts = EvenCuts(inFrom, inTo);

            foreach (int x in xCuts)
                for (int z = inFrom; z <= inTo; z++) _roadTiles.Add(new Vector2Int(x, z));

            foreach (int z in zCuts)
                for (int x = inFrom; x <= inTo; x++) _roadTiles.Add(new Vector2Int(x, z));

            // What is left between the roads, biggest first so the big sheds get the
            // big blocks.
            for (int i = 0; i < xCuts.Count - 1; i++)
            {
                for (int j = 0; j < zCuts.Count - 1; j++)
                {
                    int x0 = xCuts[i] + 1, x1 = xCuts[i + 1] - 1;
                    int z0 = zCuts[j] + 1, z1 = zCuts[j + 1] - 1;
                    if (x1 < x0 || z1 < z0) continue;

                    _blocks.Add(new RectInt(x0, z0, x1 - x0 + 1, z1 - z0 + 1));
                }
            }

            _blocks.Sort((a, b) => (b.width * b.height).CompareTo(a.width * a.height));
        }

        /// <summary>Tiles between one street and the next. 5 tiles is a 100m block.</summary>
        private const int BlockTiles = 5;

        /// <summary>
        /// How many of something a block of this size should hold, at one per so many
        /// square metres.
        ///
        /// Counts have to come from the block's area rather than be written down, or a
        /// hundred-metre block and a forty-metre one get the same nine crates and only
        /// one of them looks occupied. This is the same reasoning as
        /// <see cref="LevelTheme.ScaledCount"/>, applied per district instead of per
        /// arena.
        /// </summary>
        private static int ForArea(Vector2 size, float squareMetresEach, int min, int max)
            => Mathf.Clamp(Mathf.RoundToInt(size.x * size.y / squareMetresEach), min, max);

        /// <summary>
        /// Street lines evenly spaced across a run, including both ends, aiming for
        /// blocks of <see cref="BlockTiles"/>. Evenly spaced rather than stepped from one
        /// end, because a remainder left at the last cut is one odd sliver of a block
        /// that gets a district sized for a full one.
        /// </summary>
        private static List<int> EvenCuts(int from, int to)
        {
            int cells = to - from;
            int bands = Mathf.Max(2, Mathf.RoundToInt(cells / (float)BlockTiles));

            var cuts = new List<int>();
            for (int i = 0; i <= bands; i++)
            {
                int at = from + Mathf.RoundToInt(cells * i / (float)bands);
                if (!cuts.Contains(at)) cuts.Add(at);
            }

            cuts.Sort();
            return cuts;
        }

        /// <summary>
        /// Lays the road surface, one pack tile per grid square, and claims it so no
        /// district can build across a carriageway.
        /// </summary>
        private static void BuildStreets(Transform root, float half)
        {
            var streets = new GameObject("Streets").transform;
            streets.SetParent(root, false);

            foreach (var tile in _roadTiles)
            {
                Vector3 at = new Vector3(tile.x * Tile, 0.02f, tile.y * Tile);

                // A bend where the run turns, a straight otherwise. Worked out from the
                // neighbours rather than stored, so the plan stays one set of tiles.
                bool n = _roadTiles.Contains(tile + Vector2Int.up);
                bool s = _roadTiles.Contains(tile + Vector2Int.down);
                bool e = _roadTiles.Contains(tile + Vector2Int.right);
                bool w = _roadTiles.Contains(tile + Vector2Int.left);

                string prefab = RoadStraight;
                float yaw = (e || w) && !(n || s) ? 90f : 0f;

                if ((n || s) && (e || w) && !((n && s) || (e && w)))
                {
                    prefab = RoadBendA;
                    if (n && e) yaw = 0f;
                    else if (e && s) yaw = 90f;
                    else if (s && w) yaw = 180f;
                    else yaw = 270f;
                }

                var go = Place(streets, prefab, at, yaw);

                // The road is drawn on the minimap as the street plan, which is most of
                // what makes a 500m site navigable from the corner of the screen.
                if (go != null) Mark(go, new Color(0.24f, 0.24f, 0.26f), -8);

                Claim(at.x, at.z, Tile * 0.42f);
            }
        }

        // ==================================================================
        // Districts
        // ==================================================================
        /// <summary>
        /// Fills each block with a district. Which district a block gets is decided by
        /// its size and its turn, not at random: the works need the deepest blocks, the
        /// tank farm needs a square one, and the container yard wants a long thin one so
        /// its rows make corridors rather than a car park.
        /// </summary>
        private static void BuildBlocks(Transform root, int layer, System.Random rng, float half)
        {
            var districts = new GameObject("Districts").transform;
            districts.SetParent(root, false);

            int works = 0, farms = 0, yards = 0, depots = 0;

            for (int i = 0; i < _blocks.Count; i++)
            {
                RectInt b = _blocks[i];
                Vector3 centre = new Vector3((b.xMin + b.xMax - 1) * 0.5f * Tile, 0f,
                                             (b.yMin + b.yMax - 1) * 0.5f * Tile);
                Vector2 size = new Vector2(b.width * Tile, b.height * Tile);

                // The spawn block is left as an open muster yard with light cover. The
                // player should be able to see where they are before they have to fight.
                if (centre.magnitude < Tile * 1.6f)
                {
                    BuildMusterYard(districts, layer, rng, centre, size);
                    continue;
                }

                bool longThin = Mathf.Max(size.x, size.y) > Mathf.Min(size.x, size.y) * 1.6f;

                if (works < _theme.zoneWorksCount && Mathf.Min(size.x, size.y) >= 55f)
                {
                    BuildWorks(districts, layer, rng, centre, size);
                    works++;
                }
                else if (farms < _theme.zoneTankFarmCount && !longThin)
                {
                    BuildTankFarm(districts, layer, rng, centre, size);
                    farms++;
                }
                else if (yards < _theme.zoneContainerYardCount)
                {
                    BuildContainerYard(districts, layer, rng, centre, size);
                    yards++;
                }
                else
                {
                    BuildDepot(districts, layer, rng, centre, size);
                    depots++;
                }
            }

            Debug.Log($"[FPSKit] industrial zone: {works} works, {farms} tank farm(s), " +
                      $"{yards} container yard(s), {depots} depot(s) over {_blocks.Count} blocks.");
        }

        /// <summary>
        /// The block the player starts in: hard standing, a few pallet stacks and a
        /// barrel group for immediate cover, and nothing tall enough to hide the rest of
        /// the site. Being able to read the level from the spawn is what makes a large
        /// map feel navigable instead of merely big.
        /// </summary>
        private static void BuildMusterYard(Transform parent, int layer, System.Random rng,
                                            Vector3 centre, Vector2 size)
        {
            var yard = new GameObject("MusterYard").transform;
            yard.SetParent(parent, false);
            yard.localPosition = centre;

            int cover = ForArea(size, 320f, 8, 20);
            for (int i = 0; i < cover; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 14f + (float)rng.NextDouble() * (Mathf.Min(size.x, size.y) * 0.44f);
                Vector3 at = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                if (at.magnitude < 12f) continue;

                Place(yard, i % 2 == 0 ? Pallets : BarrelSet, at, (float)rng.NextDouble() * 360f);
            }

            Place(yard, ElecBox, new Vector3(size.x * 0.32f, 0f, -size.y * 0.3f), 90f);
        }

        /// <summary>
        /// A works: two or three big sheds sharing a yard, doors onto the street.
        ///
        /// The sheds are the reason to come here. <c>Hangar_v2</c> and <c>Hangar_v3</c>
        /// are hollow, so each one is an interior fight with two ways in, and the crates
        /// inside are what stop that interior being an empty shoebox. A stair tower on
        /// one flank puts a firing position on the roof looking down the street, which is
        /// the best killing ground on the site and is meant to be contested.
        /// </summary>
        private static void BuildWorks(Transform parent, int layer, System.Random rng,
                                       Vector3 centre, Vector2 size)
        {
            var works = new GameObject("Works").transform;
            works.SetParent(parent, false);
            works.localPosition = centre;

            bool alongX = size.x >= size.y;
            float run = alongX ? size.x : size.y;
            int sheds = Mathf.Clamp(Mathf.FloorToInt(run / 30f), 1, 3);
            float step = run / sheds;

            for (int i = 0; i < sheds; i++)
            {
                float offset = -run * 0.5f + step * (i + 0.5f);
                Vector3 at = alongX ? new Vector3(offset, 0f, 0f) : new Vector3(0f, 0f, offset);
                float yaw = alongX ? 0f : 90f;

                string prefab = i % 2 == 0 ? ShedWide : ShedLong;
                var shed = Place(works, prefab, at, yaw);
                if (shed == null) continue;

                Claim(centre.x + at.x, centre.z + at.z, 15f);

                // Cover inside, so the interior is a fight rather than a corridor.
                for (int c = 0; c < 4; c++)
                {
                    Vector3 inside = at + new Vector3(
                        ((float)rng.NextDouble() - 0.5f) * 14f, 0f,
                        ((float)rng.NextDouble() - 0.5f) * 12f);
                    Place(works, c % 2 == 0 ? CrateBig : Pallets, inside,
                          (float)rng.NextDouble() * 360f);
                }

                // The stair tower and roof position, on the street side.
                if (i == 0)
                {
                    Vector3 stairAt = at + (alongX ? new Vector3(-14f, 0f, 11f)
                                                   : new Vector3(11f, 0f, -14f));
                    BuildStairTower(works, layer, stairAt, 9f, alongX ? 0f : 90f);
                }
            }

            // Service clutter against the shed walls: the stuff that says the building
            // is used rather than modelled.
            int clutter = ForArea(size, 420f, 6, 22);
            for (int i = 0; i < clutter; i++)
            {
                Vector3 at = new Vector3(((float)rng.NextDouble() - 0.5f) * size.x * 0.8f, 0f,
                                         ((float)rng.NextDouble() - 0.5f) * size.y * 0.8f);
                Place(works, i % 2 == 0 ? Dumpster : BarrelSet, at, (float)rng.NextDouble() * 360f);
            }

            Place(works, OutBuild, new Vector3(size.x * 0.34f, 0f, size.y * 0.32f), 180f);
            BuildSign(works, layer, new Vector3(size.x * 0.4f, 0f, 0f), 90f, SignKind.Hardhat);
        }

        /// <summary>
        /// A tank farm: big tanks inside a bund wall, thin silos beside them, and a pipe
        /// run leaving the compound with a catwalk on top of it.
        ///
        /// The bund is the low wall that would hold the contents if a tank split, and it
        /// is the reason this reads as a tank farm rather than as cylinders on tarmac.
        /// It is also chest height, which makes the whole compound fightable.
        /// </summary>
        private static void BuildTankFarm(Transform parent, int layer, System.Random rng,
                                          Vector3 centre, Vector2 size)
        {
            var farm = new GameObject("TankFarm").transform;
            farm.SetParent(parent, false);
            farm.localPosition = centre;

            float bundX = size.x * 0.36f, bundZ = size.y * 0.36f;
            BuildBund(farm, layer, bundX, bundZ);

            int tanks = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(size.x, size.y) / 22f), 2, 4);
            for (int i = 0; i < tanks; i++)
            {
                float t = tanks == 1 ? 0.5f : i / (float)(tanks - 1);
                Vector3 at = new Vector3(Mathf.Lerp(-bundX * 0.55f, bundX * 0.55f, t), 0f,
                                         (i % 2 == 0 ? -1f : 1f) * bundZ * 0.3f);
                NoStanding(Place(farm, TankWide, at, (float)rng.NextDouble() * 360f));
                Claim(centre.x + at.x, centre.z + at.z, 7f);
            }

            for (int i = 0; i < 5; i++)
            {
                Vector3 at = new Vector3(-bundX + 3f + i * 4.5f, 0f, bundZ + 6f);
                NoStanding(Place(farm, TankTall, at));
            }

            NoStanding(Place(farm, PipeTower, new Vector3(bundX + 9f, 0f, -bundZ * 0.4f)));
            Place(farm, Generator, new Vector3(-bundX - 8f, 0f, bundZ * 0.5f), 45f);

            // The pipe run out of the compound, and the walkway over it. This is the
            // elevated lane across the middle of the site.
            BuildPipeRun(farm, layer, rng, new Vector3(0f, 0f, -bundZ - 10f), size.x * 0.82f);

            BuildSign(farm, layer, new Vector3(0f, 0f, bundZ + 2.5f), 0f, SignKind.Flammable);
        }

        /// <summary>
        /// A container yard: rows of stacked boxes with lanes between them.
        ///
        /// This is the close-quarters district and the best killing ground on the site,
        /// deliberately. The lanes are two containers wide so a fight in one is a
        /// shooting gallery with no way to flank without leaving the row; the walk-through
        /// containers are the flank, and they are the reason the yard is not simply a
        /// death trap for whoever enters second.
        /// </summary>
        private static void BuildContainerYard(Transform parent, int layer, System.Random rng,
                                               Vector3 centre, Vector2 size)
        {
            var yard = new GameObject("ContainerYard").transform;
            yard.SetParent(parent, false);
            yard.localPosition = centre;

            bool alongX = size.x >= size.y;
            float rowRun = alongX ? size.x : size.y;
            float across = alongX ? size.y : size.x;

            int rows = Mathf.Clamp(Mathf.FloorToInt(across / 11f), 2, 5);
            int perRow = Mathf.Clamp(Mathf.FloorToInt(rowRun / 9f), 2, 7);

            for (int r = 0; r < rows; r++)
            {
                float lane = -across * 0.5f + across * (r + 0.5f) / rows;

                for (int c = 0; c < perRow; c++)
                {
                    float along = -rowRun * 0.5f + rowRun * (c + 0.5f) / perRow;
                    Vector3 at = alongX ? new Vector3(along, 0f, lane)
                                        : new Vector3(lane, 0f, along);

                    // Every third box is walk-through: the flank that makes the lanes
                    // survivable.
                    string prefab = (r + c) % 3 == 0 ? BoxThrough
                                  : (r + c) % 5 == 0 ? BoxOpen : BoxClosed;

                    Place(yard, prefab, at, alongX ? 0f : 90f);

                    // A second tier on some of them, for the height the yard needs.
                    //
                    // The roof of a stacked container is 6m of flat walkable surface
                    // with no ladder to it, and there are dozens of them -- easily the
                    // biggest source of orphan navmesh on the site, and each island is
                    // well over the 12 square metres minRegionArea culls. An enemy
                    // spawned on one stands there for the whole level.
                    if ((r + c) % 4 == 1)
                        NoStanding(Place(yard, BoxClosed, at + Vector3.up * 3.02f,
                                         alongX ? 0f : 90f));
                }
            }

            Claim(centre.x, centre.z, Mathf.Min(size.x, size.y) * 0.4f);
            BuildSign(yard, layer, new Vector3(0f, 0f, size.y * 0.42f), 0f, SignKind.Warning);
        }

        /// <summary>
        /// A depot: the low-rise filler between the big districts -- silos, an
        /// outbuilding, pallet stacks and a walled loading bay. Its job is to keep the
        /// ground between two landmarks from being a field, and to give the approach to
        /// each of them something to break the sightline.
        /// </summary>
        private static void BuildDepot(Transform parent, int layer, System.Random rng,
                                       Vector3 centre, Vector2 size)
        {
            var depot = new GameObject("Depot").transform;
            depot.SetParent(parent, false);
            depot.localPosition = centre;

            NoStanding(Place(depot, Silo, new Vector3(-size.x * 0.28f, 0f, size.y * 0.24f),
                             (float)rng.NextDouble() * 360f));
            Place(depot, ShedSmall, new Vector3(size.x * 0.26f, 0f, -size.y * 0.22f), 90f);
            Place(depot, Generator, new Vector3(size.x * 0.3f, 0f, size.y * 0.3f));

            int items = ForArea(size, 260f, 10, 34);
            for (int i = 0; i < items; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = (float)rng.NextDouble() * Mathf.Min(size.x, size.y) * 0.46f;
                Vector3 at = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);

                string[] clutter = { Pallets, PalletOne, BagsPallet, CrateBig, CrateFlat,
                                     BarrelSet, BarrelOne, Dumpster, PipeVent };
                Place(depot, clutter[rng.Next(clutter.Length)], at,
                      (float)rng.NextDouble() * 360f);
            }

            // A short wall of road blocks: chest-high cover laid across the open side.
            for (int i = 0; i < 5; i++)
                Place(depot, RoadBlock, new Vector3(-size.x * 0.34f + i * 2.5f, 0f, -size.y * 0.34f),
                      90f);

        }

        /// <summary>
        /// What stands along the kerbs: lamp-standard clutter, barriers on the corners
        /// and a sign at the junctions.
        ///
        /// <b>A street with nothing on it is the emptiest thing in a level.</b> The
        /// districts fill the blocks, but the carriageways are a third of the site by
        /// area, and a player spends most of their time on them because that is where
        /// the walking is. Left bare they read as a car park with sheds round it --
        /// which is exactly what the first build of this looked like.
        ///
        /// Placed only where a road meets a block edge, never mid-carriageway, so a car
        /// can still drive the whole network.
        /// </summary>
        private static void BuildStreetFurniture(Transform root, int layer, System.Random rng)
        {
            var street = new GameObject("StreetFurniture").transform;
            street.SetParent(root, false);

            foreach (var tile in _roadTiles)
            {
                // Junctions only -- a tile with road on three or more sides.
                int neighbours = 0;
                if (_roadTiles.Contains(tile + Vector2Int.up)) neighbours++;
                if (_roadTiles.Contains(tile + Vector2Int.down)) neighbours++;
                if (_roadTiles.Contains(tile + Vector2Int.right)) neighbours++;
                if (_roadTiles.Contains(tile + Vector2Int.left)) neighbours++;

                Vector3 centre = new Vector3(tile.x * Tile, 0f, tile.y * Tile);
                if (centre.magnitude < Tile * 1.2f) continue;   // keep the spawn clear

                if (neighbours >= 3 && rng.NextDouble() < 0.55)
                {
                    // Barriers pulled back to the kerb, not across the lane.
                    float kerb = Tile * 0.42f;
                    for (int i = 0; i < 3; i++)
                        Place(street, RoadBlock,
                              centre + new Vector3(-kerb + i * 2.4f, 0f, kerb), 90f);

                    BuildSign(street, layer, centre + new Vector3(kerb, 0f, -kerb),
                              (float)rng.NextDouble() * 360f, SignKind.Warning);
                }
                else if (rng.NextDouble() < 0.22)
                {
                    float kerb = Tile * 0.44f;
                    float side = rng.Next(2) == 0 ? kerb : -kerb;
                    Place(street, rng.Next(2) == 0 ? ElecBox : PipeVent,
                          centre + new Vector3(side, 0f, Rand(rng, -6f, 6f)),
                          (float)rng.NextDouble() * 360f);
                }
            }
        }
    }
}
#endif
