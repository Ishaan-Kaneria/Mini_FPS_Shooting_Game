#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The rooftop zone: a night city district on a street grid, 450 m square. The Night Rooftop arena.
    ///
    /// <b>Why a city and not a bigger roof.</b> The old Night Rooftop was a 95 m box of concrete rooms with
    /// crates on it. A rooftop reads as one because of what is around it: a district of lit windows,
    /// streets you can see down into, and other roofs a bridge away. So the arena is the district. The
    /// ground is a street grid (the industrial zone's, 100 m blocks on a 20 m tile), every block is cut into
    /// lots, every lot is a building, and the fight happens on both levels: streets and kerbs below, roofs
    /// above, joined by fire escapes up the facades and skybridges across the streets.
    ///
    /// The structure that keeps it playable is the same as the industrial zone's, and written down once:
    ///
    ///   * <b>Every walkable roof has two ways up</b>, on two different faces, so one blocked stair can never
    ///     make a roof nothing can reach. A lot too small for two escapes is not walkable.
    ///   * <b>Anything flat with no way up is kept off the navigation bake</b> (towers' roofs, parapets, rooftop
    ///     plant, car roofs, cornices), or each one bakes as an island for the spawner to put an enemy on.
    ///   * <b>Bridges join roofs of the same height</b>: the two roofs are raised to the lower of the pair, and
    ///     the deck overlaps each roof so the bake joins the surfaces.
    ///   * <b>Everything that is a facade is one texture</b> (windows on a 3.3 m grid, 16 cells to the tile) and
    ///     every wall is placed on that grid, so a window is never cut by a corner. Lit windows are an emission
    ///     map, so a building at night costs one material and no lights.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        // ==================================================================
        // Grid and plan
        // ==================================================================
        /// <summary>Floor to floor, and the width of a window bay. Fifteen 0.22 m treads make one floor of stairs.</summary>
        private const float FloorH = 3.3f;

        /// <summary>Window cells in one repeat of the facade texture.</summary>
        private const int FacadeCells = 16;

        /// <summary>The alley between two lots of one block: three bays.</summary>
        private const float Alley = 9.9f;

        private sealed class CityLot
        {
            public float x0, z0, x1, z1;
            public int floors;
            public bool walkable;
            public int facade;
            public bool bridgeLocked;
            public readonly List<(int side, float at, float width)> gaps = new List<(int, float, float)>();
            public float Height => floors * FloorH;
            public float W => x1 - x0;
            public float D => z1 - z0;
            public Vector3 Centre => new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f);
        }

        private sealed class CityBridge { public CityLot a, b; public bool alongX; public float at, from, to; }

        private static readonly List<CityLot> _lots = new List<CityLot>();
        private static readonly List<CityBridge> _bridges = new List<CityBridge>();

        /// <summary>Ground rectangles taken by stairs, bridges and signs, so nothing is placed across them. x0, z0, x1, z1.</summary>
        private static readonly List<Vector4> _cityReserved = new List<Vector4>();

        private static Material[] _cityFacade;
        private static Material _cityRoof, _cityTrim, _cityPlant, _cityDark;
        private static Material[] _cityNeon;
        private static readonly Color[] NeonColours =
        {
            new Color(1.00f, 0.18f, 0.55f), new Color(0.10f, 0.85f, 1.00f), new Color(1.00f, 0.62f, 0.10f),
            new Color(0.25f, 1.00f, 0.45f), new Color(1.00f, 0.15f, 0.12f), new Color(0.65f, 0.35f, 1.00f),
        };

        private static float Snap(float v) => Mathf.Round(v / FloorH) * FloorH;

        // ==================================================================
        // Build
        // ==================================================================
        /// <summary>Builds the whole district. Roads first: every lot is placed relative to them.</summary>
        private static void BuildCityZone()
        {
            _claimed.Clear();
            _roadTiles.Clear();
            _blocks.Clear();
            _lots.Clear();
            _bridges.Clear();
            _cityReserved.Clear();
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
            ResolveCityMaterials();

            // The player starts at the crossing at the middle of the map, looking down an avenue.
            Claim(0f, 0f, 26f);

            BuildZoneGround(root, layer, backdrop, half);

            PlanStreets(half);
            BuildStreets(root, half);

            PlanLots(rng);
            PlanBridges(rng);
            PlanEscapes(rng);

            var buildings = new GameObject("Buildings").transform;
            buildings.SetParent(root, false);
            BeginRoofLights(root);
            foreach (var lot in _lots) BuildCityBuilding(buildings, layer, rng, lot);
            EndRoofLights(root, backdrop);

            var bridges = new GameObject("Skybridges").transform;
            bridges.SetParent(root, false);
            foreach (var b in _bridges) BuildSkybridge(bridges, layer, b);

            BuildCityStreet(root, layer, rng, half);
            BuildCityPavement(root, layer);
            BuildCitySignals(root, layer, rng);
            BuildCityFurniture(root, layer, rng);
            BuildCityCars(root, layer, rng);
            BuildCitySigns(root, layer, rng);
            BuildCityLights(root, rng);

            BuildPerimeterFence(root, layer, half);
            SealNavMeshOutside(root, half - 5f, half + Mathf.Max(_theme.apronSize, 400f));

            BuildCitySkyline(root, backdrop, rng, half);

            int walk = 0, tower = 0;
            foreach (var l in _lots) { if (l.walkable) walk++; else tower++; }
            Debug.Log($"[FPSKit] rooftop zone: {_lots.Count} buildings ({walk} walkable roofs, {tower} towers), {_bridges.Count} skybridges, " +
                      $"{_blocks.Count} blocks.");
        }

        // ==================================================================
        // Lots
        // ==================================================================
        /// <summary>
        /// Cuts every block into lots and gives each a height and a facade.
        ///
        /// Four layouts, chosen by the block's turn: a cross of four lots, two lots split one way or the other,
        /// or one big lot beside two small ones. Every edge is snapped to the 3.3 m bay so the facade texture
        /// meets the corners squarely. Towers cluster to the north-east, where a district has its centre: a
        /// skyline with a direction is a skyline the player can navigate by.
        /// </summary>
        private static void PlanLots(System.Random rng)
        {
            for (int bi = 0; bi < _blocks.Count; bi++)
            {
                RectInt b = _blocks[bi];
                float wx0 = Snap(b.xMin * Tile - Tile * 0.5f), wx1 = Snap(b.xMax * Tile - Tile * 0.5f);
                float wz0 = Snap(b.yMin * Tile - Tile * 0.5f), wz1 = Snap(b.yMax * Tile - Tile * 0.5f);
                float w = wx1 - wx0, d = wz1 - wz0;

                var rects = new List<Vector4>();                   // x0, z0, x1, z1
                int layout = rng.Next(0, 4);
                if (w < 60f || d < 60f) layout = 1;

                float mx = Snap(wx0 + w * 0.5f), mz = Snap(wz0 + d * 0.5f);
                float hx = Alley * 0.5f;

                switch (layout)
                {
                    case 0:      // a cross of four lots
                        rects.Add(new Vector4(wx0, wz0, mx - hx, mz - hx));
                        rects.Add(new Vector4(mx + hx, wz0, wx1, mz - hx));
                        rects.Add(new Vector4(wx0, mz + hx, mx - hx, wz1));
                        rects.Add(new Vector4(mx + hx, mz + hx, wx1, wz1));
                        break;
                    case 1:      // two lots split along x
                        rects.Add(new Vector4(wx0, wz0, mx - hx, wz1));
                        rects.Add(new Vector4(mx + hx, wz0, wx1, wz1));
                        break;
                    case 2:      // two lots split along z
                        rects.Add(new Vector4(wx0, wz0, wx1, mz - hx));
                        rects.Add(new Vector4(wx0, mz + hx, wx1, wz1));
                        break;
                    default:     // one big lot and two small ones
                        rects.Add(new Vector4(wx0, wz0, mx - hx, wz1));
                        rects.Add(new Vector4(mx + hx, wz0, wx1, mz - hx));
                        rects.Add(new Vector4(mx + hx, mz + hx, wx1, wz1));
                        break;
                }

                foreach (var r in rects)
                {
                    var lot = new CityLot { x0 = r.x, z0 = r.y, x1 = r.z, z1 = r.w };
                    Vector3 c = lot.Centre;

                    // Towers lean north-east; low and mid rise fill the rest.
                    float toward = Mathf.Clamp01((c.x + c.z) / (_theme.arenaSize * 0.9f) + 0.5f);
                    float chance = _theme.cityTowerShare * Mathf.Lerp(0.35f, 1.9f, toward);
                    bool tower = rng.NextDouble() < chance && lot.W >= 30f && lot.D >= 30f;

                    // The two lots at the middle crossing stay low, so the first view is of streets and not of a wall.
                    if (c.magnitude < 70f) tower = false;

                    if (tower)
                    {
                        lot.floors = rng.Next(12, 19);
                        lot.walkable = false;
                        lot.facade = 3 + rng.Next(0, 3);        // dark masonry or glass
                    }
                    else
                    {
                        lot.floors = rng.NextDouble() < 0.45 ? rng.Next(3, 6) : rng.Next(6, 10);
                        lot.walkable = Mathf.Min(lot.W, lot.D) >= 30f;
                        lot.facade = rng.Next(0, 4);
                    }

                    _lots.Add(lot);
                }
            }
        }

        // ==================================================================
        // Bridges
        // ==================================================================
        /// <summary>
        /// Picks skybridges between facing roofs and raises each pair to one height.
        ///
        /// A bridge is only worth having if it is level, so the two roofs it joins are set to the lower of the
        /// pair, and a roof that two bridges join pulls its other neighbour down with it (the groups are
        /// unioned). A candidate is rejected when it would cross a building, another bridge, or a stair.
        /// </summary>
        private static void PlanBridges(System.Random rng)
        {
            var candidates = new List<CityBridge>();

            for (int i = 0; i < _lots.Count; i++)
                for (int j = 0; j < _lots.Count; j++)
                {
                    if (i == j) continue;
                    var a = _lots[i]; var b = _lots[j];
                    if (!a.walkable || !b.walkable) continue;

                    // b is east of a, across a gap of an alley or a street, with 12 m or more of shared frontage.
                    float gap = b.x0 - a.x1;
                    float lo = Mathf.Max(a.z0, b.z0) + 6f, hi = Mathf.Min(a.z1, b.z1) - 6f;
                    if (gap > 8f && gap < 26f && hi - lo >= 6f)
                        candidates.Add(new CityBridge { a = a, b = b, alongX = true, at = Snap((lo + hi) * 0.5f + (float)(rng.NextDouble() - 0.5) * (hi - lo) * 0.6f), from = a.x1, to = b.x0 });

                    gap = b.z0 - a.z1;
                    lo = Mathf.Max(a.x0, b.x0) + 6f; hi = Mathf.Min(a.x1, b.x1) - 6f;
                    if (gap > 8f && gap < 26f && hi - lo >= 6f)
                        candidates.Add(new CityBridge { a = a, b = b, alongX = false, at = Snap((lo + hi) * 0.5f + (float)(rng.NextDouble() - 0.5) * (hi - lo) * 0.6f), from = a.z1, to = b.z0 });
                }

            // Shuffle, then take what fits. Streets before alleys is not needed: both read as bridges.
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int k = rng.Next(0, i + 1);
                (candidates[i], candidates[k]) = (candidates[k], candidates[i]);
            }

            var taken = new List<Vector4>();
            foreach (var c in candidates)
            {
                if (_bridges.Count >= _theme.cityBridgeCount) break;

                // The deck's ground rectangle, three metres wide. Rejected if it crosses any other building.
                Vector4 r = c.alongX ? new Vector4(c.from - 1f, c.at - 1.9f, c.to + 1f, c.at + 1.9f)
                                     : new Vector4(c.at - 1.9f, c.from - 1f, c.at + 1.9f, c.to + 1f);
                bool bad = false;
                foreach (var lot in _lots)
                {
                    if (lot == c.a || lot == c.b) continue;
                    if (r.x < lot.x1 && r.z > lot.x0 && r.y < lot.z1 && r.w > lot.z0) { bad = true; break; }
                }
                foreach (var t in taken)
                    if (r.x < t.z + 4f && r.z > t.x - 4f && r.y < t.w + 4f && r.w > t.y - 4f) { bad = true; break; }
                // At most two bridges per lot: more would leave nowhere for its stairs.
                int uses = 0;
                foreach (var e in _bridges) if (e.a == c.a || e.b == c.a || e.a == c.b || e.b == c.b) uses++;
                if (bad || uses >= 3) continue;

                taken.Add(r);
                _bridges.Add(c);
                _cityReserved.Add(r);

                // The parapet is open where the deck lands: east face of a, west face of b (or north / south).
                if (c.alongX) { c.a.gaps.Add((2, c.at, 4.0f)); c.b.gaps.Add((3, c.at, 4.0f)); }
                else { c.a.gaps.Add((0, c.at, 4.0f)); c.b.gaps.Add((1, c.at, 4.0f)); }
            }

            // One height per connected group of roofs: the lowest, never under three floors.
            var group = new Dictionary<CityLot, CityLot>();
            CityLot Find(CityLot x) { while (group.ContainsKey(x) && group[x] != x) x = group[x]; return x; }
            foreach (var br in _bridges)
            {
                var ra = Find(br.a); var rb = Find(br.b);
                if (ra != rb) group[ra] = rb;
                if (!group.ContainsKey(br.a)) group[br.a] = br.a;
                if (!group.ContainsKey(br.b)) group[br.b] = br.b;
            }
            var min = new Dictionary<CityLot, int>();
            foreach (var lot in _lots)
            {
                if (!group.ContainsKey(lot)) continue;
                var r = Find(lot);
                min[r] = min.ContainsKey(r) ? Mathf.Min(min[r], lot.floors) : lot.floors;
            }
            foreach (var lot in _lots)
                if (group.ContainsKey(lot)) { lot.floors = Mathf.Max(3, min[Find(lot)]); lot.bridgeLocked = true; }
        }

        // ==================================================================
        // Fire escapes
        // ==================================================================
        /// <summary>The chosen escapes of each lot, each a (side, anchor) with its top landing end recorded for the parapet gap.</summary>
        private static readonly Dictionary<CityLot, List<(int side, float anchor)>> _escapes = new Dictionary<CityLot, List<(int, float)>>();

        /// <summary>Metres a fire escape takes along its face: landing, flight, landing; and out from it.</summary>
        private const float EscapeLanding = 3.0f;
        private const float EscapeAlong = EscapeLanding + 4.5f + EscapeLanding;
        private const float EscapeOut = 5.2f;

        /// <summary>
        /// Two escapes for every walkable roof, on opposite faces where the lot allows, each clear of every
        /// bridge and every other escape. A lot that cannot be given two is demoted to a tower: its roof is not
        /// walkable, and nothing on it can fail to connect.
        /// </summary>
        private static void PlanEscapes(System.Random rng)
        {
            _escapes.Clear();

            foreach (var lot in _lots)
            {
                if (!lot.walkable) continue;

                var list = new List<(int side, float anchor)>();
                var faces = new[] { 0, 1, 2, 3 };
                for (int i = 3; i > 0; i--) { int k = rng.Next(0, i + 1); (faces[i], faces[k]) = (faces[k], faces[i]); }

                // Try an opposite pair first: (0,1) or (2,3), starting from whichever the shuffle put first.
                var order = new List<int>();
                int first = faces[0];
                order.Add(first); order.Add(Opposite(first));
                foreach (int f in faces) if (!order.Contains(f)) order.Add(f);

                foreach (int side in order)
                {
                    if (list.Count >= 2) break;

                    // u is the face's own coordinate: +x on the north face, -x on the south, -z on the east, +z on the west.
                    float ulo, uhi;
                    switch (side)
                    {
                        case 0: ulo = lot.x0; uhi = lot.x1; break;
                        case 1: ulo = -lot.x1; uhi = -lot.x0; break;
                        case 2: ulo = -lot.z1; uhi = -lot.z0; break;
                        default: ulo = lot.z0; uhi = lot.z1; break;
                    }
                    if (uhi - ulo < EscapeAlong + 8f) continue;

                    for (int attempt = 0; attempt < 14; attempt++)
                    {
                        // The lane starts at u = a; the footprint is [a - 3.0, a + 7.5], kept four metres off the corners.
                        float a = Snap(Mathf.Lerp(ulo + 4f + EscapeLanding, uhi - 4f - 4.5f - EscapeLanding, (float)rng.NextDouble()));
                        Vector4 foot = EscapeFootprint(lot, side, a);
                        if (Blocked(foot, lot)) continue;
                        list.Add((side, a));
                        _cityReserved.Add(foot);

                        // The parapet gap is where the top landing is, and which end that is depends on the floor count.
                        bool topAtHigh = (lot.floors - 1) % 2 == 0;
                        float landing = topAtHigh ? a + 4.5f + EscapeLanding * 0.5f : a - EscapeLanding * 0.5f;
                        lot.gaps.Add((side, EscapeU(side, landing), EscapeLanding + 0.4f));
                        break;
                    }
                }

                if (list.Count < 2) { lot.walkable = false; lot.gaps.Clear(); }
                else _escapes[lot] = list;
            }

            // A demoted lot may have been half of a bridge. Drop those bridges: a bridge to a roof nobody can stand on is a plank.
            for (int i = _bridges.Count - 1; i >= 0; i--)
                if (!_bridges[i].a.walkable || !_bridges[i].b.walkable) _bridges.RemoveAt(i);
        }

        private static int Opposite(int side) => side ^ 1;

        /// <summary>The world coordinate along the face (x on north and south, z on east and west) of a face coordinate u.</summary>
        private static float EscapeU(int side, float u) => side == 0 || side == 3 ? u : -u;

        /// <summary>The world point on a lot's face at face coordinate u, on the ground.</summary>
        private static Vector3 FacePoint(CityLot lot, int side, float u)
        {
            switch (side)
            {
                case 0: return new Vector3(u, 0f, lot.z1);
                case 1: return new Vector3(-u, 0f, lot.z0);
                case 2: return new Vector3(lot.x1, 0f, -u);
                default: return new Vector3(lot.x0, 0f, u);
            }
        }

        /// <summary>The unit step of u along the face in world space: +x on the north face, -x on the south, -z on the east, +z on the west.</summary>
        private static Vector3 FaceU(int side) => side == 0 ? Vector3.right : side == 1 ? Vector3.left : side == 2 ? Vector3.back : Vector3.forward;
        private static Vector3 FaceOut(int side) => side == 0 ? Vector3.forward : side == 1 ? Vector3.back : side == 2 ? Vector3.right : Vector3.left;
        private static float FaceYaw(int side) => side == 0 ? 0f : side == 1 ? 180f : side == 2 ? 90f : 270f;

        /// <summary>World rectangle of an escape whose lane starts at u = <paramref name="a"/>, in the face's own coordinate.</summary>
        private static Vector4 EscapeFootprint(CityLot lot, int side, float a)
        {
            // u is a world coordinate along the face (x for north/south, z for east/west); the mapping to world is
            // direct for north and west and reversed for south and east, which is what FaceU says.
            float u0 = a - EscapeLanding, u1 = a + 4.5f + EscapeLanding;
            switch (side)
            {
                case 0: return new Vector4(u0, lot.z1, u1, lot.z1 + EscapeOut);
                case 1: return new Vector4(-u1, lot.z0 - EscapeOut, -u0, lot.z0);
                case 2: return new Vector4(lot.x1, -u1, lot.x1 + EscapeOut, -u0);
                default: return new Vector4(lot.x0 - EscapeOut, u0, lot.x0, u1);
            }
        }

        private static bool Blocked(Vector4 r, CityLot self)
        {
            foreach (var t in _cityReserved)
                if (r.x < t.z + 1f && r.z > t.x - 1f && r.y < t.w + 1f && r.w > t.y - 1f) return true;
            foreach (var lot in _lots)
                if (lot != self && r.x < lot.x1 + 0.5f && r.z > lot.x0 - 0.5f && r.y < lot.z1 + 0.5f && r.w > lot.z0 - 0.5f) return true;
            return false;
        }

        // ==================================================================
        // Materials and textures
        // ==================================================================
        private static void ResolveCityMaterials()
        {
            EnsureTextureFolder();
            WriteCityTextures();
            WriteRoadTextures();

            var masonryAlbedo = LoadDetail("City_Masonry_Albedo");
            var masonryGlow = LoadDetail("City_Masonry_Glow");
            var curtainAlbedo = LoadDetail("City_Curtain_Albedo");
            var curtainGlow = LoadDetail("City_Curtain_Glow");

            // name, tint, curtain wall?, smoothness
            var spec = new (string name, Color tint, bool curtain, float smooth)[]
            {
                ("Concrete", new Color(1.30f, 1.30f, 1.34f), false, 0.08f),
                ("Brick",    new Color(1.75f, 0.95f, 0.78f), false, 0.06f),
                ("Sandstone",new Color(1.55f, 1.38f, 1.08f), false, 0.06f),
                ("Slate",    new Color(0.85f, 0.92f, 1.10f), false, 0.14f),
                ("GlassBlue",new Color(0.80f, 1.05f, 1.45f), true, 0.38f),
                ("GlassGrey",new Color(0.95f, 1.10f, 1.05f), true, 0.34f),
            };

            _cityFacade = new Material[spec.Length];
            for (int i = 0; i < spec.Length; i++)
            {
                var m = MakeMaterial($"CityFacade_{spec[i].name}", spec[i].tint, spec[i].smooth, 0f);
                var albedo = spec[i].curtain ? curtainAlbedo : masonryAlbedo;
                var glow = spec[i].curtain ? curtainGlow : masonryGlow;
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", albedo);
                m.SetColor("_BaseColor", spec[i].tint);
                m.SetFloat("_Smoothness", spec[i].smooth);
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", glow);
                m.SetColor("_EmissionColor", Color.white * 2.4f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;   // None lets URP clear the _EMISSION keyword on import
                EditorUtility.SetDirty(m);
                _cityFacade[i] = m;
            }

            _cityRoof = MakeMaterial("CityRoof", new Color(0.13f, 0.13f, 0.14f), 0.12f, 0f);
            // Lifted off near black so a roof reads under the moon; MakeMaterial leaves an existing asset alone, so set it.
            _cityRoof.SetColor("_BaseColor", new Color(0.30f, 0.31f, 0.34f));
            EditorUtility.SetDirty(_cityRoof);
            _cityTrim = MakeMaterial("CityTrim", new Color(0.30f, 0.30f, 0.31f), 0.08f, 0f);
            _cityPlant = MakeMaterial("CityPlant", new Color(0.36f, 0.38f, 0.40f), 0.30f, 0.55f);
            _cityDark = MakeMaterial("CityDark", new Color(0.04f, 0.04f, 0.05f), 0.20f, 0.3f);
            Matte(_cityTrim, 0.08f);

            _cityNeon = new Material[NeonColours.Length];
            for (int i = 0; i < NeonColours.Length; i++)
            {
                var m = MakeMaterial($"CityNeon_{i}", NeonColours[i] * 0.35f, 0.4f, 0f);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", NeonColours[i] * 3.2f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;   // None lets URP clear the _EMISSION keyword on import
                EditorUtility.SetDirty(m);
                _cityNeon[i] = m;
            }
        }

        /// <summary>
        /// The facade textures: a masonry wall with a window in each bay, and a glass curtain wall. Both are
        /// sixteen bays across, so the lit-window pattern repeats only every 53 m, and each has an emission
        /// map of the same layout with a random share of the windows lit in warm, cool or screen-blue light.
        /// Deterministic: the same texture every build, so a rebuild changes no asset.
        /// </summary>
        private static void WriteCityTextures()
        {
            const int cellPx = 64, n = FacadeCells * cellPx;
            var rng = new System.Random(8861);

            float Hash(int x, int y, int s)
            {
                unchecked
                {
                    int h = x * 374761393 + y * 668265263 + s * 1274126177;
                    h = (h ^ (h >> 13)) * 1274126177;
                    return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
                }
            }

            Color32 Enc(float r, float g, float b) => new Color32(
                (byte)Mathf.RoundToInt(Mathf.Pow(Mathf.Clamp01(r), 1f / 2.2f) * 255f),
                (byte)Mathf.RoundToInt(Mathf.Pow(Mathf.Clamp01(g), 1f / 2.2f) * 255f),
                (byte)Mathf.RoundToInt(Mathf.Pow(Mathf.Clamp01(b), 1f / 2.2f) * 255f), 255);

            Color LitColour(float pick, float shade)
            {
                Color c = pick < 0.55f ? new Color(1.00f, 0.74f, 0.42f)       // warm lamp
                        : pick < 0.82f ? new Color(0.86f, 0.93f, 1.00f)       // cool white
                        : pick < 0.93f ? new Color(0.35f, 0.55f, 1.00f)       // a screen
                        : new Color(1.00f, 0.45f, 0.62f);                     // a sign or a party
                return c * Mathf.Lerp(0.55f, 1f, shade);
            }

            foreach (bool curtain in new[] { false, true })
            {
                string name = curtain ? "City_Curtain" : "City_Masonry";
                var albedo = new Color32[n * n];
                var glow = new Color32[n * n];

                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int cx = x / cellPx, cy = y / cellPx;
                        float u = (x % cellPx) / (float)cellPx, v = (y % cellPx) / (float)cellPx;

                        // Window rectangle inside the bay.
                        float wx0 = curtain ? 0.03f : 0.24f, wx1 = curtain ? 0.97f : 0.76f;
                        float wy0 = curtain ? 0.04f : 0.26f, wy1 = curtain ? 0.96f : 0.74f;
                        bool inWindow = u > wx0 && u < wx1 && v > wy0 && v < wy1;
                        bool frame = inWindow && (u < wx0 + 0.035f || u > wx1 - 0.035f || v < wy0 + 0.035f || v > wy1 - 0.035f);
                        bool mullion = inWindow && !frame && (Mathf.Abs(u - 0.5f) < 0.018f || (!curtain && Mathf.Abs(v - 0.5f) < 0.016f));
                        bool sill = !curtain && u > wx0 - 0.03f && u < wx1 + 0.03f && v > wy0 - 0.045f && v <= wy0;

                        // Wall: mid grey with a fine grain and rain streaks running down from each sill.
                        float grain = 0.5f + (Hash(x / 2, y / 2, 1) - 0.5f) * 0.12f;
                        float streak = (Hash(cx * 7 + (int)(u * 9f), 3, 5) - 0.5f) * 0.10f * (1f - v);
                        float wall = Mathf.Clamp01(grain + streak - (curtain ? 0.05f : 0f));

                        float lum;
                        if (frame) lum = 0.62f;
                        else if (mullion) lum = 0.5f;
                        else if (inWindow) lum = 0.07f + 0.05f * Hash(x, y, 2);
                        else if (sill) lum = 0.78f;
                        else lum = wall;

                        albedo[y * n + x] = Enc(lum, lum, lum);

                        // Lit or dark is decided per bay, not per pixel.
                        bool lit = Hash(cx, cy, curtain ? 11 : 7) < (curtain ? 0.30f : 0.26f);
                        float pick = Hash(cx, cy, 13);
                        float shade = Hash(cx, cy, 17);
                        if (lit && inWindow && !frame && !mullion)
                        {
                            Color c = LitColour(pick, shade);
                            glow[y * n + x] = Enc(c.r, c.g, c.b);
                        }
                        else glow[y * n + x] = new Color32(0, 0, 0, 255);
                    }

                WritePng($"{TextureFolder}/{name}_Albedo.png", albedo, n, imp => { imp.textureType = TextureImporterType.Default; imp.sRGBTexture = true; });
                WritePng($"{TextureFolder}/{name}_Glow.png", glow, n, imp => { imp.textureType = TextureImporterType.Default; imp.sRGBTexture = true; });
            }
        }
    }
}
#endif
