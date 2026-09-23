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

        /// <summary>
        /// The surfaces the big built structures wear.
        ///
        /// Separate from the four above rather than reusing them, and for one reason:
        /// those carry a tiling chosen for the thing they were made for -- the perimeter
        /// wall's concrete repeats sixty times across five hundred metres, which on a
        /// sixty-metre shed wall is a grey blur. These are all tiled one-to-one so that
        /// the scale comes from <c>MeshBuild.UVScale</c> at the point of use, where the
        /// size of the surface is actually known.
        /// </summary>
        private static Material _zoneCladding, _zonePlate, _zoneBrick,
                                _zoneLine, _zoneStain, _zoneDirt;

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
            ResetDense();

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
            ResolveDenseMaterials();

            // The spawn, before anything can be dropped on it. Wide, because the first
            // thing the player sees should be the plant and not the side of a shed.
            Claim(0f, 0f, 26f);

            BuildZoneGround(root, layer, backdrop, half);

            PlanStreets(half);
            PlanStreetTunnels(rng, half);
            BuildStreets(root, half);
            BuildStreetFurniture(root, layer, rng);
            BuildBlocks(root, layer, rng, half);

            // What makes it a place rather than a plan (FPSKitIndustrialDense). In this
            // order because each asks the scene what the one before it left free: the
            // gates take the clear ground first, the tunnels' wings next, then the infill
            // fills what is left, and the walls go up last round all of it.
            ChooseGates(rng, half);
            BuildStreetTunnels(root, layer, rng);
            BuildInfill(root, layer, rng);
            BuildCompoundWalls(root, layer, rng);

            // Last of the ground passes, because the kerbs follow the road tiles and
            // the bay markings follow the blocks, and neither exists until both of those
            // have been laid out.
            BuildYardDetail(root, layer, rng, half);

            BuildPerimeterFence(root, layer, half);

            // The four-metre strip between the fence and the boundary wall is yard slab
            // like everywhere else, so it bakes -- and it is sealed off by the fence, so
            // nothing on it can reach anything. A kilometre of navmesh nobody can use.
            SealNavMeshOutside(root, half - 5f, half + Mathf.Max(_theme.apronSize, 400f));

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
                                   new Color(1.95f, 1.92f, 1.80f))
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

            // ---- the built structures ----
            //
            // Tiled one-to-one, because a hall wall, a silo and a chimney are three very
            // different sizes and each one sets its own scale where it is built. The
            // pack's concrete wall panel has strong horizontal banding in it, which is
            // wrong laid flat over a yard and exactly right standing up as profiled
            // steel cladding on the side of a shed.
            _zoneCladding = TiledCopy(concrete, "ZoneCladding", Vector2.one)
                            ?? MakeMaterial("ZoneCladding", new Color(0.55f, 0.56f, 0.57f), 0.22f, 0.15f);

            _zonePlate = TiledCopy(steel, "ZonePlate", Vector2.one)
                         ?? MakeMaterial("ZonePlate", new Color(0.50f, 0.51f, 0.53f), 0.30f, 0.45f);

            // Brick, for the stacks. Tinted off the rusted steel map, which has the right
            // mottling in it and none of the banding concrete has -- a chimney with a
            // horizontal stripe every metre reads as a stack of tyres.
            _zoneBrick = TintedTile(rust, "ZoneBrick", Vector2.one,
                                    new Color(1.25f, 1.05f, 0.95f))
                         ?? MakeMaterial("ZoneBrick", new Color(0.44f, 0.30f, 0.24f), 0.12f, 0f);

            // ---- what is painted and spilled on the ground ----
            //
            // All three are deliberately close in value to what they sit on. Paint on a
            // working yard is faded, thin and half worn away, and the first cut of these
            // was none of those: saturated yellow hatching at full opacity read as fresh
            // road marking on a new car park, and it was the loudest thing in the arena
            // from every angle -- which is the opposite of the job, since the whole
            // point of ground detail is to be noticed without being looked at.
            _zoneLine  = MakeMaterial("ZoneLine",  new Color(0.60f, 0.58f, 0.47f), 0.06f, 0f);
            _zoneStain = MakeMaterial("ZoneStain", new Color(0.17f, 0.165f, 0.16f), 0.20f, 0f);
            _zoneDirt  = MakeMaterial("ZoneDirt",  new Color(0.43f, 0.40f, 0.35f), 0.03f, 0f);
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
                // Ten metres of carriageway on a twenty-metre tile: see CarriageHalf.
                PlaceRoadTile(streets, tile);
                Claim(tile.x * Tile, tile.y * Tile, Tile * 0.42f);
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

            int works = 0, farms = 0, yards = 0, depots = 0, power = 0;

            // Where each district ended up, so the passes that cross the site afterwards
            // have somewhere to run between. A pipe bridge has to join two things that
            // exist, and nothing knows where they are until they are built.
            var built = new List<Vector3>();

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
                bool roomy = Mathf.Min(size.x, size.y) >= 55f;

                // The power house takes the first block big enough, which -- because the
                // blocks are sorted biggest first -- is the biggest square one on the
                // site. It wants the room: a cooling tower is thirteen metres across
                // before its legs, and the whole reason it is here is to be seen over
                // everything else from the far fence.
                if (power < _theme.zonePowerHouseCount && roomy && !longThin)
                {
                    BuildPowerHouse(districts, layer, rng, centre, size);
                    power++;
                }
                else if (works < _theme.zoneWorksCount && roomy)
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

                built.Add(centre);
            }

            BuildCrossSiteBridges(districts, layer, rng, built);

            Debug.Log($"[FPSKit] industrial zone: {power} power house(s), {works} works, " +
                      $"{farms} tank farm(s), {yards} container yard(s), {depots} depot(s) " +
                      $"over {_blocks.Count} blocks.");
        }

        /// <summary>
        /// Pipe bridges from one district to the next, carried over the streets between
        /// them.
        ///
        /// <b>The streets are the emptiest thing on the site and they are a third of it.</b>
        /// Every one is a hundred metres of open carriageway with the sky directly above
        /// it, and a player walking down one is looking at nothing from the kerb to the
        /// horizon. Something crossing overhead is the cheapest possible fix -- it puts a
        /// ceiling on the view, throws a shadow across the road, and turns a corridor
        /// between two blocks into a gateway.
        ///
        /// Run only between districts that are neighbours on the grid, because a pipe
        /// bridge cutting diagonally across four blocks is a shape no plant has.
        /// </summary>
        private static void BuildCrossSiteBridges(Transform parent, int layer, System.Random rng,
                                                  List<Vector3> districts)
        {
            var group = new GameObject("PipeBridges").transform;
            group.SetParent(parent, false);

            int built = 0;

            for (int i = 0; i < districts.Count && built < 5; i++)
                for (int j = i + 1; j < districts.Count && built < 5; j++)
                {
                    Vector3 a = districts[i], b = districts[j];

                    float dx = Mathf.Abs(a.x - b.x), dz = Mathf.Abs(a.z - b.z);

                    // Neighbours along one axis and level on the other: one street apart.
                    bool alongX = dz < Tile && dx > Tile * 2f && dx < Tile * 7f;
                    bool alongZ = dx < Tile && dz > Tile * 2f && dz < Tile * 7f;
                    if (!alongX && !alongZ) continue;
                    if (rng.NextDouble() > 0.55) continue;

                    // Offset off the centre line of both blocks, so a bridge crosses the
                    // street beside the district rather than through the middle of it.
                    float shift = Rand(rng, -Tile, Tile);
                    Vector3 offset = alongX ? new Vector3(0f, 0f, shift) : new Vector3(shift, 0f, 0f);

                    // Not over a street tunnel: the building on it stands higher than the pipes.
                    bool blocked = false;
                    for (float t = 0f; t <= 1f; t += 0.05f)
                        if (UnderStreetTunnel(Vector3.Lerp(a + offset, b + offset, t))) { blocked = true; break; }
                    if (blocked) continue;

                    BuildPipeBridge(group, layer, a + offset, b + offset, Rand(rng, 9f, 13f));
                    built++;
                }
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
        /// A works: one big production hall with a stack, a silo bank and a yard full of
        /// the traffic that serves them.
        ///
        /// <b>This used to be two or three of the art pack's hangars and that was the
        /// single biggest reason the site read as artificial.</b> The pack's largest
        /// building is about twenty-five metres long and seven high; three of them spread
        /// over a hundred-metre block is three small sheds a long way apart, with nothing
        /// between them and nothing above them, and no amount of barrels and pallets
        /// changes that -- from the ground the entire arena was a flat horizon. Mass is
        /// what an industrial site is made of, and mass is the one thing that cannot be
        /// added as scatter.
        ///
        /// So the works is now built around a sixty-metre hall with a walkable roof, a
        /// forty-metre stack beside it and a bank of silos behind, and the pack's sheds
        /// are demoted to the outbuildings they are the right size for. The hall is set
        /// on the block's frontage rather than in the middle of it, because a building
        /// centred in its own plot is a model on a table -- a real one has its doors on
        /// the road and its yard behind.
        /// </summary>
        private static void BuildWorks(Transform parent, int layer, System.Random rng,
                                       Vector3 centre, Vector2 size)
        {
            var works = new GameObject("Works").transform;
            works.SetParent(parent, false);
            works.localPosition = centre;

            bool alongX = size.x >= size.y;
            float across = Mathf.Min(size.x, size.y);

            float width = Mathf.Clamp(Mathf.Max(size.x, size.y) * 0.62f, 40f, 64f);
            float depth = Mathf.Clamp(across * 0.40f, 24f, 36f);

            // Pushed to the street frontage, with the yard behind it. A building centred
            // in its own plot is a model on a table -- a real one has its doors on the
            // road and its yard behind.
            float set = across * 0.5f - depth * 0.5f - 4f;
            float front = rng.Next(2) == 0 ? set : -set;
            float back = front > 0f ? -1f : 1f;

            // Everything is placed in (along the frontage, into the yard) and turned into
            // world axes here, so the layout reads as a plan once rather than as a pair
            // of branches at every position.
            Vector3 At(float along, float into)
                => alongX ? new Vector3(along, 0f, into) : new Vector3(into, 0f, along);

            float run = Mathf.Max(size.x, size.y);
            float yard = Mathf.Min(size.x, size.y);

            Vector3 hallAt = At(Rand(rng, -4f, 4f), front);

            BuildFactoryHall(works, layer, rng, hallAt, width, depth, alongX ? 0f : 90f);

            // Claimed by the diagonal, not by the long side: the stairs stand three and a
            // half metres off each long wall, so a claim measured on the hall alone
            // leaves them outside it.
            Claim(centre.x + hallAt.x, centre.z + hallAt.z,
                  new Vector2(width, depth + 9f).magnitude * 0.5f);

            // Cover inside, so the interior is a fight rather than a shed with a roof.
            int inside = Mathf.Clamp(Mathf.RoundToInt(width / 8f), 4, 8);
            for (int c = 0; c < inside; c++)
            {
                Vector3 local = At(Rand(rng, -width * 0.38f, width * 0.38f),
                                   Rand(rng, -depth * 0.3f, depth * 0.3f));

                Place(works, c % 3 == 0 ? Pallets : c % 3 == 1 ? CrateBig : BagsPallet,
                      hallAt + local, (float)rng.NextDouble() * 360f);
            }

            // ---- the yard behind it ----
            //
            // <b>Everything out here is kept a clear span from everything else, and that
            // is a navigation requirement rather than a taste.</b> The first cut of this
            // yard packed the stack nine metres off the end of the hall with the silos
            // and an outbuilding closing the other sides, which left a sealed pocket of
            // yard fifteen metres across in every works on the site -- ground that baked,
            // that nothing could path to, and that the spawner would happily put an enemy
            // in. Nothing about a courtyard looks wrong, which is what made it expensive
            // to find.
            Vector3 stackAt = At(-run * 0.24f, back * yard * 0.12f);

            BuildChimney(works, layer, stackAt, Rand(rng, 34f, 48f), Rand(rng, 2.1f, 2.9f),
                         Mathf.Atan2(hallAt.x - stackAt.x, hallAt.z - stackAt.z) * Mathf.Rad2Deg);
            Claim(centre.x + stackAt.x, centre.z + stackAt.z, 9f);

            Vector3 siloAt = At(run * 0.22f, back * yard * 0.14f);

            BuildSiloBank(works, layer, siloAt, rng.Next(4, 7), Rand(rng, 2.8f, 3.6f),
                          Rand(rng, 15f, 22f), alongX ? 0f : 90f);
            Claim(centre.x + siloAt.x, centre.z + siloAt.z, 14f);

            // The pack's own sheds, at the size they are right for: the outbuildings
            // along the back of a works yard.
            //
            // <b>Every one of them is held off the navigation bake, including the hollow
            // ones, and that is a measurement rather than a precaution.</b> Hangar_v2 and
            // Hangar_v3 carry non-convex mesh colliders, so a player can walk inside
            // them -- but their door openings do not admit a 0.5 m agent, and what the
            // bake makes of that is a room-sized slab of walkable navmesh with no way in
            // or out of it. Sixteen of those on the site were sixteen places the spawner
            // could stand an enemy for the whole level. Their roofs are the same story
            // one storey up: three metres of flat steel with no stair to it.
            //
            // So the sheds are scenery an agent walks round and a player walks into, and
            // the interior fight belongs to the hall, whose doorways are eleven metres
            // wide and are checked.
            NoEntry(Place(works, ShedLong, At(-run * 0.3f, back * yard * 0.36f),
                          alongX ? 0f : 90f));

            NoEntry(Place(works, ShedSmall, At(run * 0.3f, back * yard * 0.36f),
                          alongX ? 0f : 90f));

            NoEntry(Place(works, OutBuild, At(run * 0.02f, back * yard * 0.42f),
                          alongX ? 180f : 270f));

            // Service clutter -- and it asks first.
            //
            // <b>Scatter that ignores the claims is scatter that lands on a staircase.</b>
            // A dumpster dropped halfway up an external flight does not read as a bug
            // from anywhere: it is a dumpster in a works yard. What it does is cut the
            // flight in two as far as the bake is concerned, so the roof above it is a
            // firing position the level's enemies can never reach, and the only symptom
            // is that nobody ever comes up after you.
            int clutter = ForArea(size, 380f, 8, 26);
            for (int i = 0; i < clutter; i++)
            {
                var at = new Vector2(centre.x + Rand(rng, -size.x * 0.44f, size.x * 0.44f),
                                     centre.z + Rand(rng, -size.y * 0.44f, size.y * 0.44f));

                if (!Free(at, 2.5f)) continue;

                string[] kit = { Dumpster, BarrelSet, Pallets, PalletOne, CrateFlat, ElecBox };
                Place(works, kit[rng.Next(kit.Length)],
                      new Vector3(at.x - centre.x, 0f, at.y - centre.z),
                      (float)rng.NextDouble() * 360f);

                Claim(at.x, at.y, 2f);
            }

            BuildSign(works, layer, new Vector3(size.x * 0.4f, 0f, 0f), 90f, SignKind.Hardhat);
        }

        /// <summary>
        /// The power house: a cooling tower, the boiler hall that feeds it, two stacks
        /// and a transformer compound.
        ///
        /// <b>Every arena needs one thing you can see from everywhere, and this is it.</b>
        /// A five-hundred-metre site with nothing over twenty metres tall has no
        /// landmarks in it, so a player who turns round twice has no idea which way they
        /// came from and the map reads as the same block repeated -- which is exactly
        /// what this one did. A cooling tower is forty metres of unmistakable silhouette:
        /// it is a compass, it is a place ("behind the tower"), and it is the single
        /// cheapest thing that makes a plant look like a plant rather than a distribution
        /// park.
        /// </summary>
        private static void BuildPowerHouse(Transform parent, int layer, System.Random rng,
                                            Vector3 centre, Vector2 size)
        {
            var house = new GameObject("PowerHouse").transform;
            house.SetParent(parent, false);
            house.localPosition = centre;

            float radius = Mathf.Clamp(Mathf.Min(size.x, size.y) * 0.16f, 10f, 15f);
            var towerAt = new Vector3(-size.x * 0.26f, 0f, size.y * 0.24f);

            BuildCoolingTower(house, layer, towerAt, radius * 3.1f, radius);
            Claim(centre.x + towerAt.x, centre.z + towerAt.z, radius * 1.5f);

            // The boiler hall, which is what the tower is attached to. Smaller than a
            // works hall and turned across it, so the two do not read as a pair of sheds.
            var hallAt = new Vector3(size.x * 0.2f, 0f, -size.y * 0.16f);
            BuildFactoryHall(house, layer, rng, hallAt,
                             Mathf.Clamp(size.x * 0.44f, 32f, 46f),
                             Mathf.Clamp(size.y * 0.3f, 22f, 30f), 90f);
            Claim(centre.x + hallAt.x, centre.z + hallAt.z, 24f);

            // Two stacks, deliberately unequal: a matched pair reads as decoration.
            BuildChimney(house, layer, new Vector3(-size.x * 0.06f, 0f, -size.y * 0.34f),
                         Rand(rng, 44f, 56f), 2.8f, 90f);
            BuildChimney(house, layer, new Vector3(-size.x * 0.06f + 11f, 0f, -size.y * 0.34f),
                         Rand(rng, 30f, 38f), 2.1f, 90f);

            // The transformer compound: a bund of its own, full of switchgear, with the
            // pipe run leaving it.
            var yardAt = new Vector3(-size.x * 0.3f, 0f, -size.y * 0.16f);

            var pen = new GameObject("Transformers").transform;
            pen.SetParent(house, false);
            pen.localPosition = yardAt;

            BuildBund(pen, layer, 11f, 8f);

            for (int i = 0; i < 4; i++)
                Place(pen, i % 2 == 0 ? Generator : ElecBox,
                      new Vector3(-7f + i * 4.6f, 0f, Rand(rng, -3f, 3f)), i * 37f);

            Claim(centre.x + yardAt.x, centre.z + yardAt.z, 13f);

            // Offset away from the stacks, and shorter than the block.
            //
            // <b>A pipe run is really three objects -- the pipes, the catwalk over them
            // and the stair up to the catwalk -- and the stair reaches seven metres
            // beyond the end of the run.</b> Laid down the middle of the block it put
            // that stair through the flue duct at the foot of a chimney, which is at
            // exactly catwalk height: the flight was still there, still climbable to
            // look at, and cut in half as far as the bake was concerned, so the walkway
            // over the whole compound was a strip of navmesh joined to nothing.
            BuildPipeRun(house, layer, rng, new Vector3(size.x * 0.02f, 0f, size.y * 0.12f),
                         size.y * 0.5f);

            int clutter = ForArea(size, 520f, 6, 18);
            for (int i = 0; i < clutter; i++)
                Place(house, i % 2 == 0 ? BarrelSet : PipeVent,
                      new Vector3(Rand(rng, -size.x * 0.42f, size.x * 0.42f), 0f,
                                  Rand(rng, -size.y * 0.42f, size.y * 0.42f)),
                      (float)rng.NextDouble() * 360f);

            BuildSign(house, layer, new Vector3(0f, 0f, -size.y * 0.42f), 0f, SignKind.Warning);
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
            float blockAcross = alongX ? size.y : size.x;

            // <b>The stacks take one side of the block, not all of it.</b> Spread evenly
            // from edge to edge, a few dozen boxes left no ground anywhere big enough to
            // build on and no space that read as a yard either -- a hundred-metre block
            // with a container every eleven metres is empty from the ground and full as
            // far as anything placed later is concerned. Packed into a band along one
            // side, it is a stacking area, and the rest of the block is left for the
            // warehouses such a yard serves (FPSKitIndustrialDense).
            float across = blockAcross * 0.55f;
            float bandShift = (blockAcross - across) * 0.5f * (rng.Next(2) == 0 ? 1f : -1f);
            rowRun *= 0.86f;

            int rows = Mathf.Clamp(Mathf.FloorToInt(across / 10f), 2, 5);
            int perRow = Mathf.Clamp(Mathf.FloorToInt(rowRun / 8.5f), 2, 8);

            for (int r = 0; r < rows; r++)
            {
                float lane = bandShift - across * 0.5f + across * (r + 0.5f) / rows;

                for (int c = 0; c < perRow; c++)
                {
                    float along = -rowRun * 0.5f + rowRun * (c + 0.5f) / perRow;
                    Vector3 at = alongX ? new Vector3(along, 0f, lane)
                                        : new Vector3(lane, 0f, along);

                    // Every third box is walk-through: the flank that makes the lanes
                    // survivable.
                    string prefab = (r + c) % 3 == 0 ? BoxThrough
                                  : (r + c) % 5 == 0 ? BoxOpen : BoxClosed;

                    // <b>Held off the bake, and the ground-level ones as much as the
                    // stacked ones.</b> Only the upper tier used to be, on the reasoning
                    // that its roof is six metres up with no ladder -- but the roof of a
                    // container standing on the ground is flat, walkable and two and a
                    // half metres up, which is just as unreachable and there are four
                    // times as many of them. They were the largest single source of
                    // orphan navmesh left on the site. The walk-through boxes keep their
                    // insides, because the floor in there is the yard slab and not the
                    // container.
                    NoStanding(Place(yard, prefab, at, alongX ? 0f : 90f));

                    // A second tier on some of them, for the height the yard needs.
                    if ((r + c) % 4 == 1)
                        NoStanding(Place(yard, BoxClosed, at + Vector3.up * 3.02f,
                                         alongX ? 0f : 90f));
                }
            }

            // The crane that put the stacks there. Spanning the rows rather than along
            // them, so it crosses every lane and gives the yard a top edge -- without it
            // a container yard from the ground is a maze of six-metre boxes with the sky
            // on top, and the fight in it has no third dimension at all.
            var craneAt = alongX ? new Vector3(0f, 0f, bandShift) : new Vector3(bandShift, 0f, 0f);
            BuildGantryCrane(yard, layer, rng, craneAt,
                             Mathf.Min(across * 0.98f, 58f), 12.5f, alongX ? 90f : 0f);

            Claim(centre.x + craneAt.x, centre.z + craneAt.z, across * 0.5f);
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

            // Turned in right angles rather than freely, because NoEntry's volume is
            // axis aligned: a box spun 45 degrees has a bounding box half again as wide
            // as it is, and the carve would take the ground round it with it.
            NoEntry(Place(depot, Silo, new Vector3(-size.x * 0.28f, 0f, size.y * 0.24f),
                          rng.Next(4) * 90f));
            NoEntry(Place(depot, ShedSmall, new Vector3(size.x * 0.26f, 0f, -size.y * 0.22f), 90f));
            Place(depot, Generator, new Vector3(size.x * 0.3f, 0f, size.y * 0.3f));

            // Stock in one laydown area rather than sprinkled over the whole block, for the
            // reason the container yard gives: sprinkled, it is too thin to read as a
            // stockyard and too wide to leave room for anything else.
            var stock = new Vector3(Rand(rng, -size.x * 0.15f, size.x * 0.15f), 0f,
                                    Rand(rng, -size.y * 0.15f, size.y * 0.15f));

            int items = ForArea(size, 260f, 10, 34);
            for (int i = 0; i < items; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Sqrt((float)rng.NextDouble()) * Mathf.Min(size.x, size.y) * 0.2f;
                Vector3 at = stock + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);

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

                // On the pavement, between the kerb and the compound wall. Placed at the old
                // twenty-metre kerb these would now stand in the wall, or -- on a street
                // running east to west -- in the middle of the carriageway.
                float kerb = CarriageHalf + 1.6f;

                if (neighbours >= 3 && rng.NextDouble() < 0.55)
                {
                    Place(street, RoadBlock, centre + new Vector3(-kerb, 0f, kerb), 45f);
                    BuildSign(street, layer, centre + new Vector3(kerb, 0f, -kerb),
                              (float)rng.NextDouble() * 360f, SignKind.Warning);
                }
                else if (neighbours == 2 && rng.NextDouble() < 0.22)
                {
                    bool runsX = _roadTiles.Contains(tile + Vector2Int.right) && _roadTiles.Contains(tile + Vector2Int.left);
                    bool runsZ = _roadTiles.Contains(tile + Vector2Int.up) && _roadTiles.Contains(tile + Vector2Int.down);
                    if (!runsX && !runsZ) continue;

                    float side = rng.Next(2) == 0 ? kerb : -kerb;
                    var offset = runsZ ? new Vector3(side, 0f, Rand(rng, -6f, 6f))
                                       : new Vector3(Rand(rng, -6f, 6f), 0f, side);

                    Place(street, rng.Next(2) == 0 ? ElecBox : PipeVent, centre + offset,
                          (float)rng.NextDouble() * 360f);
                }
            }
        }
    }
}
#endif
