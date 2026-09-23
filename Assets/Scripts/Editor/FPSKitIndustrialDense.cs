#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// What turns a plant plan into a place: narrow roads, walled compounds, gates, tunnels,
    /// and buildings packed against the walls.
    ///
    /// Ishaan's verdict on the industrial zone once the big structures were in was that it
    /// was "very empty" and "the roads are quite big", and he asked for walls, big
    /// industries close together and small tunnels -- "looks like a real industrial zone".
    /// All three complaints were one fact seen from three sides. The layout was a grid of
    /// twenty-metre carriageways between hundred-metre yards, with the buildings stood in
    /// the middle of the yards, so from anywhere at eye level a player looked across forty
    /// metres of open asphalt at a shed. Nothing enclosed anything. A real works district
    /// is the opposite: roads a lorry wide, a wall along both sides of every one, buildings
    /// built right up to the wall, and gates -- so a street is a corridor with things
    /// looming over it, and a yard is somewhere you go <i>into</i>.
    ///
    /// <b>The roads are ten metres, not twenty.</b> The plan still lays out on the pack's
    /// twenty-metre tiles, because every district is placed on that grid, but only the
    /// middle half of each tile is carriageway. The rest is pavement and the compound wall
    /// (<see cref="CompoundLine"/>). Ten metres is still two lanes, which is what the car
    /// Ishaan is planning needs.
    ///
    /// <b>Every block is a compound, and every compound has at least two ways in.</b> The
    /// rule from the halls -- anything a player can walk into has two ways out -- applies to
    /// a walled yard exactly as it does to a building, and for the same reason: one gate is
    /// a cul-de-sac, and a compound whose only gate happens to be blocked from inside is a
    /// sealed island of navmesh the spawner will put an enemy in. So gates are chosen where
    /// the ground just inside is clear (<see cref="ChooseGates"/>), a compound that cannot
    /// get two is left unwalled rather than half-walled, and everything built afterwards
    /// keeps out of the gates' way.
    ///
    /// <b>Everything built here asks the scene whether the ground is free</b>, with a
    /// physics overlap, rather than trusting the claim circles. The districts do not claim
    /// most of what they place -- a bund, a pallet row, a pipe run -- and a building
    /// dropped on a pipe run's stair is a roof nobody reaches. The scene's colliders are
    /// the only list of what is really there.
    ///
    /// <b>Anything closed is sealed off the bake with a volume in world space</b>
    /// (<see cref="SealBox"/>), never with NoEntry parented to it -- see NoEntry for why a
    /// volume under a transformed object can be silently ignored.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        /// <summary>Half the drivable width of every road: ten metres of carriageway on a twenty-metre tile.</summary>
        private const float CarriageHalf = 5f;

        /// <summary>
        /// How far a compound wall stands from the middle of the road beside it. Leaves a
        /// three-metre pavement between the kerb and the wall, and puts the wall a little
        /// outside the block, so nothing a district built at its edge is inside the wall.
        /// </summary>
        private const float CompoundLine = 8.2f;

        private const float CompoundWallThick = 0.4f;

        private sealed class CompoundGate
        {
            public int Side;      // 0 south, 1 north, 2 west, 3 east
            public float At;      // along the side: x for south/north, z for west/east
            public float Width;
            public bool House;
        }

        private sealed class Compound
        {
            public RectInt Block;
            public float MinX, MaxX, MinZ, MaxZ;   // the wall line
            public readonly List<CompoundGate> Gates = new List<CompoundGate>();
            public int Style;
            public float Height;
            public bool Walled;
        }

        private static readonly List<Compound> _compounds = new List<Compound>();

        /// <summary>World rectangles covered by a street tunnel, road and pavements both.</summary>
        private static readonly List<Rect> _streetTunnels = new List<Rect>();

        /// <summary>Ground nothing may be built on: the approach to every gate, and a tunnel's wings.</summary>
        private static readonly List<Rect> _keepClear = new List<Rect>();

        /// <summary>Pool keys for the one-off meshes here. ToMesh pools by key, and a key two buildings share is one building drawn twice.</summary>
        private static int _denseKey;

        private static Material _denseGlass, _denseDoor, _denseConcrete, _zoneSafety, _zoneDeck;
        private static Material _lampPole, _lampHead;
        private static Material[] _denseCladding, _denseBrick;

        private static void ResetDense()
        {
            _compounds.Clear();
            _streetTunnels.Clear();
            _keepClear.Clear();
            _denseKey = 0;
        }

        private static string DenseKey(string what) => $"dense_{what}_{_denseKey++}";

        private static void ResolveDenseMaterials()
        {
            var concrete = PackMaterial("Textures/Concrete_wall/UNIConcrete_walls/UNIConcrete_wall_v1/UNIConcrete_wall_v1.mat");

            // Cladding in the colours real sheds are painted and then left to fade: a
            // street of one grey is a street of one building.
            var tints = new[]
            {
                new Color(1f, 1f, 1f),
                new Color(0.78f, 0.92f, 1.12f),   // faded blue
                new Color(0.84f, 1.00f, 0.86f),   // works green
                new Color(1.14f, 1.08f, 0.92f),   // cream
                new Color(1.18f, 0.80f, 0.70f)    // oxide red
            };

            _denseCladding = new Material[tints.Length];
            for (int i = 0; i < tints.Length; i++)
                _denseCladding[i] = (i == 0 ? _zoneCladding : TintedTile(concrete, $"ZoneCladding{i}", Vector2.one, tints[i]))
                                    ?? _zoneCladding;

            // Brick is the concrete panel tinted red, not the zone's own brick. That one is
            // the oil tank's map, which is right on a round stack and wrong on a flat
            // wall: laid along a hundred metres of compound it showed the tank's hatches
            // as a row of portholes. The panel's horizontal banding reads as coursework.
            _denseBrick = new[]
            {
                TintedTile(concrete, "ZoneBrickRed", Vector2.one, new Color(1.22f, 0.74f, 0.58f)) ?? _zoneBrick,
                TintedTile(concrete, "ZoneBrickDark", Vector2.one, new Color(0.92f, 0.60f, 0.50f)) ?? _zoneBrick
            };

            // Weathered concrete at one repeat per panel, for the compound walls, the street
            // tunnels and the cooling tower. See Matte for why none of these may shine.
            _denseConcrete = TintedTile(concrete, "ZoneWallConcrete", Vector2.one, new Color(0.84f, 0.82f, 0.78f))
                             ?? _zoneConcrete;

            foreach (var m in _denseCladding) Matte(m, 0.16f);
            foreach (var m in _denseBrick) Matte(m, 0.1f);
            Matte(_denseConcrete, 0.1f);

            _denseGlass = MakeMaterial("ZoneGlass", new Color(0.07f, 0.09f, 0.11f), 0.82f, 0.25f);
            _denseDoor = _zoneRust;

            _lampPole = MakeMaterial("ZoneLampPole", new Color(0.21f, 0.22f, 0.23f), 0.3f, 0.4f);
            _lampHead = MakeMaterial("ZoneLampHead", new Color(1f, 0.86f, 0.62f), 0.4f, 0f);
            SetEmission(_lampHead, new Color(1f, 0.72f, 0.38f) * 1.6f);

            // The steam over the stacks, the same puff the volcanic smoke uses.
            _smokeMat = ParticleMaterial("Smoke", "Puff", additive: false, Color.white);
        }

        /// <summary>
        /// Takes the sheen off a material. Nothing in this kit bakes a reflection, so any
        /// gloss reflects Unity's default grey-blue environment rather than this sky.
        /// </summary>
        private static void Matte(Material mat, float smoothness)
        {
            if (mat == null) return;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            UnityEditor.EditorUtility.SetDirty(mat);
        }

        // ==================================================================
        // Asking the ground
        // ==================================================================
        /// <summary>
        /// Whether a point is on a carriageway -- the middle ten metres of a road tile, or
        /// the junction square and its arms. The pipe bridges ask before planting a leg.
        /// </summary>
        private static bool OnCarriageway(Vector3 p)
        {
            var tile = new Vector2Int(Mathf.RoundToInt(p.x / Tile), Mathf.RoundToInt(p.z / Tile));
            if (!_roadTiles.Contains(tile)) return false;

            float dx = p.x - tile.x * Tile, dz = p.z - tile.y * Tile;
            const float c = CarriageHalf + 0.6f;

            if (Mathf.Abs(dx) <= c && Mathf.Abs(dz) <= c) return true;

            if (Mathf.Abs(dx) <= c)
            {
                if (dz > 0f && _roadTiles.Contains(tile + Vector2Int.up)) return true;
                if (dz < 0f && _roadTiles.Contains(tile + Vector2Int.down)) return true;
            }

            if (Mathf.Abs(dz) <= c)
            {
                if (dx > 0f && _roadTiles.Contains(tile + Vector2Int.right)) return true;
                if (dx < 0f && _roadTiles.Contains(tile + Vector2Int.left)) return true;
            }

            return false;
        }

        /// <summary>
        /// Whether a box of ground is free: nothing already built stands in it, and it
        /// touches no gate approach or tunnel wing. Checked from forty centimetres up, so
        /// the yard slab and the road surface do not count as being in the way.
        /// </summary>
        private static bool GroundFree(Rect area, float height = 12f)
        {
            foreach (var keep in _keepClear)
                if (keep.Overlaps(area)) return false;

            Physics.SyncTransforms();

            var centre = new Vector3(area.center.x, 0.4f + height * 0.5f, area.center.y);
            var halfSize = new Vector3(area.width * 0.5f, height * 0.5f, area.height * 0.5f);

            return !Physics.CheckBox(centre, halfSize, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// The pack props a district scatters loose over its yard. Placed without claiming
        /// ground and dropped anywhere, a single pallet was enough to veto a building --
        /// the first build of this pass found room for thirteen buildings on the whole
        /// site, and the yards were as empty as before. Loose clutter gives way to a
        /// building instead; anything structural still stops it.
        /// </summary>
        private static readonly HashSet<string> LooseClutter = new HashSet<string>
        {
            Pallets, PalletOne, BagsPallet, CrateBig, CrateFlat, BarrelSet, BarrelOne,
            Dumpster, PipeVent, ElecBox, Generator, RoadBlock
        };

        /// <summary>
        /// As <see cref="GroundFree"/>, but loose clutter in the way is cleared rather than
        /// counted: if every collider in the box belongs to a scattered prop, those props are
        /// removed and the ground is free. Anything else there and nothing is touched.
        /// </summary>
        private static bool ClearGround(Rect area, float height = 12f)
        {
            foreach (var keep in _keepClear)
                if (keep.Overlaps(area)) return false;

            Physics.SyncTransforms();

            var centre = new Vector3(area.center.x, 0.4f + height * 0.5f, area.center.y);
            var halfSize = new Vector3(area.width * 0.5f, height * 0.5f, area.height * 0.5f);
            var hits = Physics.OverlapBox(centre, halfSize, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);

            var doomed = new HashSet<GameObject>();

            foreach (var hit in hits)
            {
                var root = UnityEditor.PrefabUtility.GetOutermostPrefabInstanceRoot(hit.gameObject);
                if (root == null) return false;

                string path = UnityEditor.PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                if (string.IsNullOrEmpty(path) || !path.StartsWith(PackRoot + "/")) return false;
                if (!LooseClutter.Contains(path.Substring(PackRoot.Length + 1))) return false;

                doomed.Add(root);
            }

            foreach (var go in doomed) Object.DestroyImmediate(go);
            return true;
        }

        private static Rect Grow(Rect r, float by) => new Rect(r.xMin - by, r.yMin - by, r.width + by * 2f, r.height + by * 2f);

        /// <summary>
        /// Takes a closed box off the navigation bake. A volume on an unscaled object of
        /// its own, in world space -- see SealCone and the note on NoEntry.
        /// </summary>
        private static void SealBox(Transform parent, Vector3 centre, Vector3 size)
        {
            var go = new GameObject("Seal");
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var volume = go.AddComponent<Unity.AI.Navigation.NavMeshModifierVolume>();
            volume.size = new Vector3(Mathf.Max(0.5f, size.x - 1.2f), size.y + 4f, Mathf.Max(0.5f, size.z - 1.2f));
            volume.area = 1;   // Not Walkable
        }

        // ==================================================================
        // Roads
        // ==================================================================
        /// <summary>
        /// Lays one road tile ten metres wide. A straight is the pack's tile squeezed across
        /// its width; a junction or a bend is a square in the middle and a half-length arm
        /// out to each neighbour, which is the one shape that joins any combination of
        /// neighbours without laying tarmac over a pavement.
        /// </summary>
        private static void PlaceRoadTile(Transform streets, Vector2Int tile)
        {
            var at = new Vector3(tile.x * Tile, 0.02f, tile.y * Tile);
            float narrow = CarriageHalf * 2f / Tile;

            bool n = _roadTiles.Contains(tile + Vector2Int.up);
            bool s = _roadTiles.Contains(tile + Vector2Int.down);
            bool e = _roadTiles.Contains(tile + Vector2Int.right);
            bool w = _roadTiles.Contains(tile + Vector2Int.left);

            GameObject Piece(Vector3 position, float yaw, float across, float along)
            {
                var go = Place(streets, RoadStraight, position, yaw);
                if (go == null) return null;

                go.transform.localScale = Vector3.Scale(go.transform.localScale, new Vector3(across, 1f, along));
                Mark(go, new Color(0.24f, 0.24f, 0.26f), -8);
                return go;
            }

            if ((n && s && !e && !w) || (e && w && !n && !s))
            {
                Piece(at, e ? 90f : 0f, narrow, 1f);
                return;
            }

            Piece(at, 0f, narrow, narrow);

            float arm = Tile * 0.5f - CarriageHalf;
            float mid = CarriageHalf + arm * 0.5f;
            float armScale = arm / Tile;

            if (n) Piece(at + new Vector3(0f, 0f, mid), 0f, narrow, armScale);
            if (s) Piece(at + new Vector3(0f, 0f, -mid), 0f, narrow, armScale);
            if (e) Piece(at + new Vector3(mid, 0f, 0f), 90f, narrow, armScale);
            if (w) Piece(at + new Vector3(-mid, 0f, 0f), 90f, narrow, armScale);
        }

        /// <summary>The kerbs round one tile's carriageway: the square's open sides and both flanks of every arm.</summary>
        private static void RoadKerbs(MeshBuild kerbs, Vector3 centre, bool n, bool s, bool e, bool w)
        {
            const float c = CarriageHalf;
            float arm = Tile * 0.5f - c;
            float mid = c + arm * 0.5f;
            var y = new Vector3(0f, 0.09f, 0f);

            void Along(Vector3 at, float length) => kerbs.Box(centre + y + at, new Vector3(0.4f, 0.18f, length), Quaternion.identity);
            void Across(Vector3 at, float length) => kerbs.Box(centre + y + at, new Vector3(length, 0.18f, 0.4f), Quaternion.identity);

            if (!n) Across(new Vector3(0f, 0f, c), c * 2f);
            if (!s) Across(new Vector3(0f, 0f, -c), c * 2f);
            if (!e) Along(new Vector3(c, 0f, 0f), c * 2f);
            if (!w) Along(new Vector3(-c, 0f, 0f), c * 2f);

            if (n) { Along(new Vector3(c, 0f, mid), arm); Along(new Vector3(-c, 0f, mid), arm); }
            if (s) { Along(new Vector3(c, 0f, -mid), arm); Along(new Vector3(-c, 0f, -mid), arm); }
            if (e) { Across(new Vector3(mid, 0f, c), arm); Across(new Vector3(mid, 0f, -c), arm); }
            if (w) { Across(new Vector3(-mid, 0f, c), arm); Across(new Vector3(-mid, 0f, -c), arm); }
        }

        // ==================================================================
        // Street tunnels
        // ==================================================================
        /// <summary>
        /// Chooses where a building spans the street, so the road runs through it: two tiles
        /// of straight road, a tile clear of any junction at each end so the mouth is not on
        /// a crossroads, and away from the spawn. Chosen before the districts, so the pipe
        /// bridges can keep out of them.
        /// </summary>
        private static void PlanStreetTunnels(System.Random rng, float half)
        {
            bool Straight(Vector2Int t, out bool alongZ)
            {
                alongZ = false;
                if (!_roadTiles.Contains(t)) return false;

                bool n = _roadTiles.Contains(t + Vector2Int.up), s = _roadTiles.Contains(t + Vector2Int.down);
                bool e = _roadTiles.Contains(t + Vector2Int.right), w = _roadTiles.Contains(t + Vector2Int.left);

                if (n && s && !e && !w) { alongZ = true; return true; }
                return e && w && !n && !s;
            }

            // The ring road runs one tile inside the fence; tunnels go on the interior streets.
            int ring = Mathf.RoundToInt(_theme.arenaSize / Tile) / 2 - 1;

            var candidates = new List<(Vector2Int a, Vector2Int b, bool alongZ)>();

            foreach (var t in _roadTiles)
            {
                if (Mathf.Abs(t.x) >= ring || Mathf.Abs(t.y) >= ring) continue;
                if (!Straight(t, out bool alongZ)) continue;

                var step = alongZ ? Vector2Int.up : Vector2Int.right;
                var next = t + step;

                if (!Straight(next, out bool nextZ) || nextZ != alongZ) continue;
                if (!Straight(t - step, out _) || !Straight(next + step, out _)) continue;

                var middle = new Vector2((t.x + next.x) * 0.5f * Tile, (t.y + next.y) * 0.5f * Tile);
                if (middle.magnitude < 70f) continue;

                candidates.Add((t, next, alongZ));
            }

            // Shuffled, then taken while they keep their distance from each other.
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            var chosen = new List<Vector2>();

            foreach (var (a, b, alongZ) in candidates)
            {
                if (chosen.Count >= 3) break;

                var middle = new Vector2((a.x + b.x) * 0.5f * Tile, (a.y + b.y) * 0.5f * Tile);

                bool far = true;
                foreach (var c in chosen) if ((c - middle).magnitude < 150f) { far = false; break; }
                if (!far) continue;

                chosen.Add(middle);

                float length = Tile * 2f;
                float width = (CompoundLine + 0.8f) * 2f;

                _streetTunnels.Add(alongZ
                    ? new Rect(middle.x - width * 0.5f, middle.y - length * 0.5f, width, length)
                    : new Rect(middle.x - length * 0.5f, middle.y - width * 0.5f, length, width));
            }
        }

        /// <summary>Whether a point is under a street tunnel. The pipe bridges will not cross one.</summary>
        private static bool UnderStreetTunnel(Vector3 p, float margin = 6f)
        {
            foreach (var t in _streetTunnels)
                if (Grow(t, margin).Contains(new Vector2(p.x, p.z))) return true;
            return false;
        }

        /// <summary>
        /// A building over the road: walls either side at the compound line, a roof over the
        /// carriageway and both pavements, and a storey of building above that. Wings reach
        /// into the blocks on either side where there is room, so it reads as a works that
        /// grew across its own street rather than as a bridge.
        ///
        /// The roof and the storey above are one object held off the bake: there is no way up
        /// there, and the slab's top face sits inside the storey, where a bake would find a
        /// room-sized floor with no door.
        /// </summary>
        private static void BuildStreetTunnels(Transform root, int layer, System.Random rng)
        {
            if (_streetTunnels.Count == 0) return;

            var group = new GameObject("StreetTunnels").transform;
            group.SetParent(root, false);

            foreach (var rect in _streetTunnels)
            {
                bool alongZ = rect.height > rect.width;
                float length = alongZ ? rect.height : rect.width;
                float inner = CompoundLine;
                var centre = new Vector3(rect.center.x, 0f, rect.center.y);

                const float clear = 6.8f, slab = 0.9f;
                float upper = Rand(rng, 4f, 8f);
                var clad = _denseCladding[rng.Next(_denseCladding.Length)];

                // Local frame: "along" the road and "across" it.
                Vector3 P(float across, float y, float along)
                    => centre + (alongZ ? new Vector3(across, y, along) : new Vector3(along, y, across));
                Vector3 S(float across, float y, float along)
                    => alongZ ? new Vector3(across, y, along) : new Vector3(along, y, across);

                // ---- the walls down both sides ----
                var walls = new MeshBuild { UVScale = 0.2f };
                for (int side = -1; side <= 1; side += 2)
                {
                    walls.Box(P(side * (inner + 0.4f), clear * 0.5f, 0f), S(0.8f, clear, length), Quaternion.identity);

                    // Buttresses, inside, every eight metres: what makes a wall this long
                    // look like it is holding something up.
                    for (float a = -length * 0.5f + 4f; a <= length * 0.5f - 4f; a += 8f)
                        walls.Box(P(side * (inner - 0.15f), clear * 0.5f, a), S(0.5f, clear, 0.9f), Quaternion.identity);
                }

                MeshObject(group, "TunnelWalls", ToMesh(walls, DenseKey("tunnelwalls")), _denseConcrete,
                           Vector3.zero, Quaternion.identity, Vector3.one, layer, "Concrete");

                // ---- roof and the storey on it ----
                var top = new MeshBuild { UVScale = 0.14f };
                float span = (inner + 0.8f) * 2f;
                top.Box(P(0f, clear + slab * 0.5f, 0f), S(span, slab, length), Quaternion.identity);
                top.Box(P(0f, clear + slab + upper * 0.5f, 0f), S(span, upper, length - 1f), Quaternion.identity);

                var topGo = MeshObject(group, "TunnelBuilding", ToMesh(top, DenseKey("tunneltop")), clad,
                                       Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");
                NoStanding(topGo);
                Hide(topGo);

                // A band of windows along both faces of the storey, and a hazard-striped
                // lintel over each mouth: headroom is the first thing a driver reads.
                var glass = new MeshBuild { UVScale = 0.5f };
                for (int side = -1; side <= 1; side += 2)
                    for (float a = -length * 0.5f + 2.5f; a <= length * 0.5f - 2.5f; a += 3.4f)
                        glass.Box(P(side * (inner + 0.82f), clear + slab + upper * 0.55f, a),
                                  S(0.08f, Mathf.Min(1.6f, upper * 0.4f), 2.2f), Quaternion.identity);

                var glassGo = MeshObject(group, "TunnelWindows", ToMesh(glass, DenseKey("tunnelglass")), _denseGlass,
                                         Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false);
                Hide(glassGo);

                var lintel = new MeshBuild { UVScale = 0.5f };
                for (int end = -1; end <= 1; end += 2)
                    lintel.Box(P(0f, clear - 0.35f, end * (length * 0.5f - 0.1f)), S(inner * 2f, 0.7f, 0.25f), Quaternion.identity);

                var lintelGo = MeshObject(group, "TunnelLintels", ToMesh(lintel, DenseKey("tunnellintel")), _zoneHazard,
                                          Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false);
                Hide(lintelGo);

                // Lights down the middle. A covered road at midday is still dark inside,
                // and the contrast is most of what makes it read as a tunnel at all.
                for (float a = -length * 0.5f + 7f; a <= length * 0.5f - 6f; a += 13f)
                {
                    var lamp = new GameObject("TunnelLight");
                    lamp.transform.SetParent(group, false);
                    lamp.transform.position = P(0f, clear - 0.6f, a);

                    var light = lamp.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.range = 15f;
                    light.intensity = 1.8f;
                    light.color = new Color(1f, 0.86f, 0.62f);
                    light.shadows = LightShadows.None;
                }

                // ---- wings into the blocks either side, where the ground is free ----
                for (int side = -1; side <= 1; side += 2)
                {
                    float depth = Rand(rng, 10f, 16f);
                    float wingLength = length - Rand(rng, 0f, 8f);
                    float from = inner + 0.8f;

                    var wingCentre = P(side * (from + depth * 0.5f), 0f, 0f);
                    var wingSize = S(depth, 0f, wingLength);
                    var footprint = new Rect(wingCentre.x - Mathf.Abs(wingSize.x) * 0.5f,
                                             wingCentre.z - Mathf.Abs(wingSize.z) * 0.5f,
                                             Mathf.Abs(wingSize.x), Mathf.Abs(wingSize.z));

                    if (!GroundFree(Grow(footprint, 3f))) continue;

                    float height = clear + slab + upper;
                    var wing = new MeshBuild { UVScale = 0.14f };
                    wing.Box(wingCentre + Vector3.up * height * 0.5f,
                             new Vector3(footprint.width, height, footprint.height), Quaternion.identity);

                    var wingGo = MeshObject(group, "TunnelWing", ToMesh(wing, DenseKey("tunnelwing")), clad,
                                            Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");
                    NoStanding(wingGo);
                    SealBox(group, wingCentre + Vector3.up * height * 0.5f,
                            new Vector3(footprint.width, height, footprint.height));

                    _keepClear.Add(footprint);
                }
            }
        }

        // ==================================================================
        // Compounds
        // ==================================================================
        /// <summary>The wall rectangle round a block: <see cref="CompoundLine"/> from the middle of each road beside it.</summary>
        private static Compound CompoundFor(RectInt block)
        {
            return new Compound
            {
                Block = block,
                MinX = (block.xMin - 1) * Tile + CompoundLine,
                MaxX = block.xMax * Tile - CompoundLine,
                MinZ = (block.yMin - 1) * Tile + CompoundLine,
                MaxZ = block.yMax * Tile - CompoundLine
            };
        }

        /// <summary>Where along a side runs, its two ends, and which way is into the compound.</summary>
        private static void SideFrame(Compound c, int side, out float from, out float to, out float line, out float inward)
        {
            switch (side)
            {
                case 0: from = c.MinX; to = c.MaxX; line = c.MinZ; inward = 1f; break;
                case 1: from = c.MinX; to = c.MaxX; line = c.MaxZ; inward = -1f; break;
                case 2: from = c.MinZ; to = c.MaxZ; line = c.MinX; inward = 1f; break;
                default: from = c.MinZ; to = c.MaxZ; line = c.MaxX; inward = -1f; break;
            }
        }

        /// <summary>A rectangle standing against a side: from <paramref name="a0"/> to <paramref name="a1"/> along it, from <paramref name="d0"/> to <paramref name="d1"/> in from it.</summary>
        private static Rect SideRect(Compound c, int side, float a0, float a1, float d0, float d1)
        {
            SideFrame(c, side, out _, out _, out float line, out float inward);

            float p0 = line + inward * d0, p1 = line + inward * d1;
            float lo = Mathf.Min(p0, p1), hi = Mathf.Max(p0, p1);

            return side < 2
                ? new Rect(a0, lo, a1 - a0, hi - lo)
                : new Rect(lo, a0, hi - lo, a1 - a0);
        }

        /// <summary>Whether a stretch of a side's wall is taken by a street tunnel's own wall.</summary>
        private static bool SideUnderTunnel(Compound c, int side, float a)
        {
            SideFrame(c, side, out _, out _, out float line, out _);
            var p = side < 2 ? new Vector3(a, 0f, line) : new Vector3(line, 0f, a);
            return UnderStreetTunnel(p, 0.5f);
        }

        /// <summary>
        /// Chooses every compound's gates, before anything else is built in it.
        ///
        /// A gate goes where the ground just inside it is clear -- a gate onto the flank of a
        /// hall is a gate into a gap a metre wide. Each side gets one with a fair chance and
        /// the compound needs two on different sides; one that cannot find two is left
        /// unwalled, because a compound with one way in is a dead end the enemies queue in,
        /// and one with none is a sealed island.
        /// </summary>
        private static void ChooseGates(System.Random rng, float half)
        {
            foreach (var block in _blocks)
            {
                var c = CompoundFor(block);
                c.Style = rng.Next(3);
                c.Height = Rand(rng, 2.8f, 3.6f);

                var sides = new List<int> { 0, 1, 2, 3 };
                for (int i = 3; i > 0; i--) { int j = rng.Next(i + 1); (sides[i], sides[j]) = (sides[j], sides[i]); }

                foreach (int side in sides)
                {
                    bool mustHave = c.Gates.Count < 2 && sides.IndexOf(side) >= 2;
                    if (!mustHave && rng.NextDouble() > 0.7) continue;

                    SideFrame(c, side, out float from, out float to, out _, out _);
                    float width = Rand(rng, 8f, 11f);

                    // Every candidate along the side, tried in a random order.
                    var spots = new List<float>();
                    for (float a = from + 14f; a <= to - 14f; a += 3f) spots.Add(a);
                    for (int i = spots.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (spots[i], spots[j]) = (spots[j], spots[i]); }

                    foreach (float a in spots)
                    {
                        if (SideUnderTunnel(c, side, a)) continue;

                        var apron = SideRect(c, side, a - width * 0.5f - 1f, a + width * 0.5f + 1f, 0.5f, 12f);
                        if (!ClearGround(apron, 3f)) continue;

                        c.Gates.Add(new CompoundGate { Side = side, At = a, Width = width });
                        break;
                    }
                }

                // Two ways in, on two different sides, or no wall at all.
                var used = new HashSet<int>();
                foreach (var g in c.Gates) used.Add(g.Side);
                c.Walled = used.Count >= 2;

                if (c.Walled)
                {
                    foreach (var g in c.Gates)
                    {
                        // A gatehouse over some of them -- a short tunnel through a building
                        // -- where there is room for it inside.
                        const float depth = 14f, flank = 5f;
                        var house = SideRect(c, g.Side, g.At - g.Width * 0.5f - flank, g.At + g.Width * 0.5f + flank,
                                             0.3f, depth);

                        if (rng.NextDouble() < 0.4 && GroundFree(Grow(house, 3f)))
                        {
                            g.House = true;
                            _keepClear.Add(house);
                            _keepClear.Add(SideRect(c, g.Side, g.At - g.Width * 0.5f, g.At + g.Width * 0.5f, depth, depth + 10f));
                        }
                        else
                        {
                            _keepClear.Add(SideRect(c, g.Side, g.At - g.Width * 0.5f - 1f, g.At + g.Width * 0.5f + 1f, 0f, 14f));
                        }
                    }
                }

                _compounds.Add(c);
            }
        }

        /// <summary>
        /// Builds every compound's wall, gate posts and gatehouses: one welded mesh per
        /// compound for the wall, in one of three builds -- brick with piers, concrete
        /// panels between posts, or corrugated sheet on a rail.
        /// </summary>
        private static void BuildCompoundWalls(Transform root, int layer, System.Random rng)
        {
            var group = new GameObject("Compounds").transform;
            group.SetParent(root, false);

            foreach (var c in _compounds)
            {
                if (!c.Walled) continue;

                var wall = new MeshBuild { UVScale = 0.35f };
                float h = c.Height;

                for (int side = 0; side < 4; side++)
                {
                    SideFrame(c, side, out float from, out float to, out float line, out _);

                    // The gaps along this side: its gates, and anywhere a tunnel's own wall
                    // stands in for this one.
                    var gaps = new List<Vector2>();
                    foreach (var g in c.Gates)
                        if (g.Side == side) gaps.Add(new Vector2(g.At - g.Width * 0.5f, g.At + g.Width * 0.5f));

                    for (float a = from; a < to; a += 1f)
                        if (SideUnderTunnel(c, side, a + 0.5f)) gaps.Add(new Vector2(a, a + 1f));

                    gaps.Sort((x, y) => x.x.CompareTo(y.x));

                    float cursor = from;
                    foreach (var gap in gaps)
                    {
                        if (gap.x > cursor) WallStretch(wall, side, line, cursor, gap.x, h, c.Style);
                        cursor = Mathf.Max(cursor, gap.y);
                    }
                    if (to > cursor) WallStretch(wall, side, line, cursor, to, h, c.Style);

                    // Gate piers, taller than the wall, either side of every opening.
                    foreach (var g in c.Gates)
                    {
                        if (g.Side != side) continue;
                        for (int end = -1; end <= 1; end += 2)
                        {
                            float a = g.At + end * (g.Width * 0.5f + 0.45f);
                            var at = side < 2 ? new Vector3(a, (h + 0.8f) * 0.5f, line) : new Vector3(line, (h + 0.8f) * 0.5f, a);
                            wall.Box(at, new Vector3(0.9f, h + 0.8f, 0.9f), Quaternion.identity);
                        }
                    }
                }

                // Sheet walls are painted -- blue, green or oxide -- never bare: bare grey
                // sheet at any gloss reads as galvanised silver, and it is the one material
                // in the arena that looked like it came from somewhere else.
                int[] painted = { 1, 2, 4 };
                var material = c.Style == 0 ? _denseBrick[rng.Next(_denseBrick.Length)]
                             : c.Style == 1 ? _denseConcrete
                             : _denseCladding[painted[rng.Next(painted.Length)]];

                var go = MeshObject(group, "CompoundWall", ToMesh(wall, DenseKey("compoundwall")), material,
                                    Vector3.zero, Quaternion.identity, Vector3.one, layer,
                                    c.Style == 2 ? "Metal" : "Concrete");
                NoStanding(go);

                foreach (var g in c.Gates)
                    if (g.House) BuildGatehouse(group, layer, rng, c, g);

                // A sign on the wall by one of the gates.
                if (c.Gates.Count > 0 && rng.NextDouble() < 0.6)
                {
                    var g = c.Gates[0];
                    SideFrame(c, g.Side, out _, out _, out float line, out float inward);
                    float a = g.At + g.Width * 0.5f + 3f;
                    var at = g.Side < 2 ? new Vector3(a, 0f, line - inward * 0.6f) : new Vector3(line - inward * 0.6f, 0f, a);
                    float yaw = g.Side == 0 ? 180f : g.Side == 1 ? 0f : g.Side == 2 ? 270f : 90f;
                    BuildSign(group, layer, at, yaw, rng.Next(2) == 0 ? SignKind.Warning : SignKind.Hardhat);
                }
            }
        }

        /// <summary>One run of compound wall between two points along a side.</summary>
        private static void WallStretch(MeshBuild wall, int side, float line, float a0, float a1, float h, int style)
        {
            if (a1 - a0 < 0.3f) return;

            Vector3 At(float a, float y) => side < 2 ? new Vector3(a, y, line) : new Vector3(line, y, a);
            Vector3 Size(float along, float y, float thick) => side < 2 ? new Vector3(along, y, thick) : new Vector3(thick, y, along);

            float mid = (a0 + a1) * 0.5f, run = a1 - a0;

            switch (style)
            {
                case 0:
                    // Brick: a solid wall, a coping course, and piers every five metres.
                    wall.Box(At(mid, h * 0.5f), Size(run, h, CompoundWallThick), Quaternion.identity);
                    wall.Box(At(mid, h + 0.08f), Size(run, 0.16f, CompoundWallThick + 0.16f), Quaternion.identity);
                    for (float a = a0 + 0.3f; a < a1; a += 5f)
                        wall.Box(At(a, (h + 0.25f) * 0.5f), Size(0.6f, h + 0.25f, 0.62f), Quaternion.identity);
                    break;

                case 1:
                    // Concrete panels slotted between posts, the posts standing proud.
                    wall.Box(At(mid, h * 0.5f), Size(run, h, CompoundWallThick * 0.7f), Quaternion.identity);
                    for (float a = a0 + 0.2f; a < a1; a += 4f)
                        wall.Box(At(a, (h + 0.35f) * 0.5f), Size(0.35f, h + 0.35f, 0.5f), Quaternion.identity);
                    break;

                default:
                    // Corrugated sheet on a rail, with posts and a top rail.
                    wall.Box(At(mid, h * 0.5f + 0.1f), Size(run, h - 0.2f, 0.12f), Quaternion.identity);
                    wall.Box(At(mid, h - 0.05f), Size(run, 0.12f, 0.2f), Quaternion.identity);
                    for (float a = a0 + 0.2f; a < a1; a += 3f)
                        wall.Box(At(a, h * 0.5f), Size(0.14f, h, 0.14f), Quaternion.identity);
                    break;
            }
        }

        /// <summary>
        /// A gatehouse: a short building standing across the inside of a gate, with the way
        /// in running through it as a tunnel. The two blocks either side of the passage are
        /// closed and sealed off the bake; the passage floor is the yard, lit.
        /// </summary>
        private static void BuildGatehouse(Transform parent, int layer, System.Random rng, Compound c, CompoundGate g)
        {
            const float depth = 14f, flank = 5f, passage = 5.2f;
            float height = Rand(rng, 8f, 11f);

            SideFrame(c, g.Side, out _, out _, out float line, out float inward);

            // Local frame: a along the wall, d in from it.
            Vector3 P(float a, float y, float d) => g.Side < 2 ? new Vector3(a, y, line + inward * d) : new Vector3(line + inward * d, y, a);
            Vector3 S(float a, float y, float d) => g.Side < 2 ? new Vector3(a, y, d) : new Vector3(d, y, a);

            float hw = g.Width * 0.5f;
            var build = new MeshBuild { UVScale = 0.2f };

            for (int end = -1; end <= 1; end += 2)
            {
                float a = g.At + end * (hw + flank * 0.5f);
                var centre = P(a, height * 0.5f, depth * 0.5f + 0.3f);
                var size = S(flank, height, depth);

                build.Box(centre, size, Quaternion.identity);
                SealBox(parent, centre, new Vector3(Mathf.Abs(size.x), size.y, Mathf.Abs(size.z)));
            }

            // Over the passage.
            build.Box(P(g.At, (passage + height) * 0.5f, depth * 0.5f + 0.3f), S(g.Width, height - passage, depth), Quaternion.identity);

            var material = c.Style == 0 ? _denseBrick[0] : _denseCladding[rng.Next(_denseCladding.Length)];
            var go = MeshObject(parent, "Gatehouse", ToMesh(build, DenseKey("gatehouse")), material,
                                Vector3.zero, Quaternion.identity, Vector3.one, layer, "Concrete");
            NoStanding(go);

            // Windows on the street face and the yard face, and the hazard band over the way through.
            var glass = new MeshBuild { UVScale = 0.5f };
            for (int face = 0; face <= 1; face++)
            {
                float d = face == 0 ? -0.05f : depth + 0.65f;
                for (int end = -1; end <= 1; end += 2)
                    for (float y = 2.2f; y < height - 1.5f; y += 3.2f)
                        glass.Box(P(g.At + end * (hw + flank * 0.5f), y, d), S(1.6f, 1.4f, 0.1f), Quaternion.identity);
            }

            var glassGo = MeshObject(parent, "GatehouseWindows", ToMesh(glass, DenseKey("gateglass")), _denseGlass,
                                     Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false);
            Hide(glassGo);

            var band = new MeshBuild { UVScale = 0.5f };
            band.Box(P(g.At, passage + 0.35f, -0.1f), S(g.Width, 0.7f, 0.2f), Quaternion.identity);
            var bandGo = MeshObject(parent, "GatehouseLintel", ToMesh(band, DenseKey("gateband")), _zoneHazard,
                                    Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false);
            Hide(bandGo);

            var lamp = new GameObject("GatehouseLight");
            lamp.transform.SetParent(parent, false);
            lamp.transform.position = P(g.At, passage - 0.5f, depth * 0.5f);
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 12f;
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.88f, 0.66f);
            light.shadows = LightShadows.None;
        }

        // ==================================================================
        // Streetscape
        // ==================================================================
        /// <summary>
        /// What a works street has along it besides walls: lamp standards on the pavement,
        /// arms out over the carriageway, one a tile on alternating sides; and pipe racks
        /// carried along the top of some of the compound walls, bridging their gates.
        ///
        /// The lamps are welded into one mesh for the poles and one for the heads, and the
        /// heads glow rather than cast light -- a hundred real lights across the site is a
        /// hundred extra passes on every surface they reach, for a daytime scene. The pipes
        /// have no collider: they run above head height and only the eye needs them.
        /// </summary>
        private static void BuildStreetscape(Transform root, int layer, System.Random rng)
        {
            var group = new GameObject("Streetscape").transform;
            group.SetParent(root, false);

            // ---- lamps ----
            var poles = new MeshBuild { UVScale = 0.5f };
            var heads = new MeshBuild { UVScale = 0.5f };
            const float poleHeight = 7.5f;

            foreach (var tile in _roadTiles)
            {
                bool n = _roadTiles.Contains(tile + Vector2Int.up), s = _roadTiles.Contains(tile + Vector2Int.down);
                bool e = _roadTiles.Contains(tile + Vector2Int.right), w = _roadTiles.Contains(tile + Vector2Int.left);

                bool alongZ = n && s && !e && !w;
                bool alongX = e && w && !n && !s;
                if (!alongZ && !alongX) continue;

                var centre = new Vector3(tile.x * Tile, 0f, tile.y * Tile);
                if (centre.magnitude < 20f || UnderStreetTunnel(centre, 2f)) continue;

                // Alternate sides down the street, so the lamps stagger.
                float side = ((tile.x + tile.y) & 1) == 0 ? 1f : -1f;
                var across = alongZ ? Vector3.right : Vector3.forward;
                var foot = centre + across * side * (CarriageHalf + 0.9f);

                poles.Tube(foot, foot + Vector3.up * poleHeight, 0.14f, 0.1f, 8);
                var armEnd = foot - across * side * 2.2f + Vector3.up * poleHeight;
                poles.Box((foot + Vector3.up * poleHeight + armEnd) * 0.5f,
                          alongZ ? new Vector3(2.2f, 0.12f, 0.12f) : new Vector3(0.12f, 0.12f, 2.2f), Quaternion.identity);
                heads.Box(armEnd + Vector3.down * 0.18f,
                          alongZ ? new Vector3(0.9f, 0.18f, 0.45f) : new Vector3(0.45f, 0.18f, 0.9f), Quaternion.identity);
            }

            if (poles.Triangles.Count > 0)
            {
                var poleGo = MeshObject(group, "LampPoles", ToMesh(poles, DenseKey("lamppoles")), _lampPole,
                                        Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");
                NoStanding(poleGo);
                Hide(poleGo);

                Hide(MeshObject(group, "LampHeads", ToMesh(heads, DenseKey("lampheads")), _lampHead,
                                Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false));
            }

            // ---- pipe racks along the walls ----
            var pipes = new MeshBuild { UVScale = 0.5f };
            var brackets = new MeshBuild { UVScale = 0.5f };

            foreach (var c in _compounds)
            {
                if (!c.Walled) continue;

                for (int side = 0; side < 4; side++)
                {
                    if (rng.NextDouble() > 0.3) continue;

                    SideFrame(c, side, out float from, out float to, out float line, out float inward);

                    // Not along a side a tunnel takes a stretch of: the pipes would run into it.
                    bool tunnel = false;
                    for (float a = from; a < to; a += 2f) if (SideUnderTunnel(c, side, a)) { tunnel = true; break; }
                    if (tunnel) continue;

                    Vector3 At(float a, float y, float o) => side < 2 ? new Vector3(a, y, line - inward * o) : new Vector3(line - inward * o, y, a);

                    float y0 = c.Height + 0.7f;
                    int count = 2 + rng.Next(2);
                    float[] radii = { 0.32f, 0.22f, 0.16f };

                    for (int k = 0; k < count; k++)
                    {
                        float y = y0 + k * 0.55f;
                        pipes.Tube(At(from + 1f, y, 0.2f), At(to - 1f, y, 0.2f), radii[k], radii[k], 8);
                    }

                    // A bracket on the wall head every six metres, skipping the gates.
                    for (float a = from + 2f; a < to - 1f; a += 6f)
                    {
                        bool inGate = false;
                        foreach (var g in c.Gates)
                            if (g.Side == side && Mathf.Abs(a - g.At) < g.Width * 0.5f + 0.5f) { inGate = true; break; }
                        if (inGate) continue;

                        float h = y0 + count * 0.55f - c.Height;
                        brackets.Box(At(a, c.Height + h * 0.5f, 0.2f),
                                     side < 2 ? new Vector3(0.18f, h, 0.7f) : new Vector3(0.7f, h, 0.18f), Quaternion.identity);
                    }
                }
            }

            if (pipes.Triangles.Count > 0)
            {
                Hide(MeshObject(group, "WallPipes", ToMesh(pipes, DenseKey("wallpipes")), _zoneSteel,
                                Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false));
                Hide(MeshObject(group, "WallPipeBrackets", ToMesh(brackets, DenseKey("wallbrackets")), _lampPole,
                                Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false));
            }
        }

        // ==================================================================
        // Infill
        // ==================================================================
        /// <summary>
        /// Fills the empty ground in every block with buildings: first along the inside of
        /// its walls, backs to the wall and fronts to the yard, which is what turns the
        /// street into a corridor; then out in the yard wherever there is still a gap big
        /// enough for a shed.
        ///
        /// <b>Kept five metres from everything else</b>, and that is the same navigation
        /// requirement the works yard learned: packed tighter, three buildings and a wall
        /// close a courtyard nothing can reach, and the spawner finds it.
        /// </summary>
        private static void BuildInfill(Transform root, int layer, System.Random rng)
        {
            var group = new GameObject("Infill").transform;
            group.SetParent(root, false);

            int count = 0, roofRows = 0, warehouses = 0;

            foreach (var c in _compounds)
            {
                // The muster yard around the spawn stays open.
                var middle = new Vector2((c.MinX + c.MaxX) * 0.5f, (c.MinZ + c.MaxZ) * 0.5f);
                if (middle.magnitude < Tile * 1.6f) continue;

                // ---- along the walls ----
                bool roofRowBuilt = false;
                for (int side = 0; side < 4; side++)
                {
                    SideFrame(c, side, out float from, out float to, out _, out float inward);

                    float a = from + Rand(rng, 1f, 5f);

                    // A row of workshops joined over their roofs, on some compounds: see RoofRow.
                    // Scanned along the whole side, because the ends are where the gates'
                    // approaches usually are; the frontage below then builds round it.
                    if (!roofRowBuilt && rng.NextDouble() < 0.6)
                    {
                        for (float p = a; p < to - 30f && !roofRowBuilt; p += 4f)
                            if (RoofRow(group, layer, rng, c, side, p, to - 1f) > p) { roofRowBuilt = true; roofRows++; }
                    }

                    while (a < to - 10f)
                    {
                        // The widest that fits here, then narrower, then shallower: a
                        // frontage is built out of whatever the district left, not out of
                        // one size tried once.
                        bool placed = false;
                        float wanted = Rand(rng, 18f, 36f);

                        foreach (float scale in new[] { 1f, 0.7f, 0.5f })
                        {
                            float width = Mathf.Min(wanted * scale, to - 1f - a);
                            if (width < 10f) break;

                            foreach (float depth in new[] { Rand(rng, 13f, 18f), Rand(rng, 8f, 12f) })
                            {
                                var footprint = SideRect(c, side, a, a + width, 0.35f, 0.35f + depth);
                                var clearance = SideRect(c, side, a - 3f, a + width + 3f, 0.35f, 0.35f + depth + 4f);

                                if (SideUnderTunnel(c, side, a) || SideUnderTunnel(c, side, a + width)) continue;
                                if (!ClearGround(clearance)) continue;

                                var front = side < 2 ? new Vector3(0f, 0f, inward) : new Vector3(inward, 0f, 0f);
                                double roll = rng.NextDouble();
                                int kind = roll < 0.38 ? 0 : roll < 0.72 ? 1 : roll < 0.86 ? 3 : 2;

                                // Backed onto the wall, a warehouse loses its rear door to it and
                                // keeps the vehicle door and the side door: still two ways out.
                                if (width >= 22f && depth >= 15f && rng.NextDouble() < 0.4) { kind = 4; warehouses++; }

                                BlockBuilding(group, layer, rng, footprint, front, kind);
                                count++;
                                a += width + Rand(rng, 4f, 9f);
                                placed = true;
                                break;
                            }

                            if (placed) break;
                        }

                        if (!placed) a += 3f;
                    }
                }

                // ---- out in the yard ----
                // A scan rather than random throws: every six metres of the block, biggest
                // size first, so a gap is found wherever there is one.
                var sizes = new[] { new Vector2(42f, 28f), new Vector2(34f, 22f), new Vector2(26f, 18f),
                                    new Vector2(20f, 14f), new Vector2(15f, 11f) };
                int yardBuilt = 0;

                foreach (var size in sizes)
                {
                    for (int turn = 0; turn < 2 && yardBuilt < 6; turn++)
                    {
                        float w = turn == 0 ? size.x : size.y, d = turn == 0 ? size.y : size.x;
                        if (c.MaxX - c.MinX < w + 12f || c.MaxZ - c.MinZ < d + 12f) continue;

                        var spots = new List<Vector2>();
                        for (float x = c.MinX + 6f + w * 0.5f; x <= c.MaxX - 6f - w * 0.5f; x += 6f)
                            for (float z = c.MinZ + 6f + d * 0.5f; z <= c.MaxZ - 6f - d * 0.5f; z += 6f)
                                spots.Add(new Vector2(x, z));

                        for (int i = spots.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (spots[i], spots[j]) = (spots[j], spots[i]); }

                        foreach (var p in spots)
                        {
                            if (yardBuilt >= 6) break;

                            var footprint = new Rect(p.x - w * 0.5f, p.y - d * 0.5f, w, d);
                            if (!ClearGround(Grow(footprint, 5f))) continue;

                            // Facing whichever way is nearer the middle of the block, like
                            // everything else in a yard that is loaded from it.
                            var toMiddle = new Vector3(middle.x - p.x, 0f, middle.y - p.y);
                            var front = Mathf.Abs(toMiddle.x) > Mathf.Abs(toMiddle.z)
                                ? new Vector3(Mathf.Sign(toMiddle.x), 0f, 0f)
                                : new Vector3(0f, 0f, Mathf.Sign(toMiddle.z));

                            double roll = rng.NextDouble();
                            int kind = roll < 0.45 ? 0 : roll < 0.7 ? 2 : roll < 0.85 ? 1 : 3;

                            // The big ones are often a warehouse you can walk into.
                            if (Mathf.Min(w, d) >= 15f && Mathf.Max(w, d) >= 22f && rng.NextDouble() < 0.75) { kind = 4; warehouses++; }

                            BlockBuilding(group, layer, rng, footprint, front, kind);
                            count++;
                            yardBuilt++;
                        }
                    }
                }
            }

            Debug.Log($"[FPSKit] industrial zone: {roofRows} roof route(s), {warehouses} enterable warehouse(s).");
            Debug.Log($"[FPSKit] industrial zone: {count} infill building(s), " +
                      $"{_compounds.FindAll(x => x.Walled).Count} of {_compounds.Count} blocks walled, " +
                      $"{_streetTunnels.Count} street tunnel(s).");
        }

        /// <summary>
        /// One building in a block.
        ///
        /// Kinds: 0 a warehouse with a pitched roof and a roller door; 1 a brick workshop,
        /// flat-roofed, with rows of windows; 2 a tall process building with ducts up its side
        /// and plant on its roof; 3 an open-fronted canopy, the one kind you can walk into.
        /// The first three are closed and sealed off the bake; the canopy is cover.
        /// </summary>
        private static void BlockBuilding(Transform parent, int layer, System.Random rng, Rect footprint,
                                          Vector3 front, int kind)
        {
            if (kind == 4)
            {
                OpenWarehouse(parent, layer, rng, footprint, front);
                return;
            }

            var centre = new Vector3(footprint.center.x, 0f, footprint.center.y);
            float sx = footprint.width, sz = footprint.height;
            bool frontX = Mathf.Abs(front.x) > 0.5f;

            // The front face's centre and its width.
            float faceWidth = frontX ? sz : sx;
            float halfDepth = (frontX ? sx : sz) * 0.5f;
            Vector3 face = centre + front * halfDepth;
            Vector3 alongFace = frontX ? Vector3.forward : Vector3.right;

            var shell = new MeshBuild { UVScale = 0.16f };
            var roof = new MeshBuild { UVScale = 0.2f };
            var glass = new MeshBuild { UVScale = 0.5f };
            var door = new MeshBuild { UVScale = 0.4f };

            Material wallMat, roofMat = _zonePlate;
            float height;
            bool closed = true;

            switch (kind)
            {
                case 0:
                {
                    // Warehouse: clad box, pilasters, a shallow gable along its length.
                    height = Rand(rng, 6.5f, 10f);
                    wallMat = _denseCladding[rng.Next(_denseCladding.Length)];

                    shell.Box(centre + Vector3.up * height * 0.5f, new Vector3(sx, height, sz), Quaternion.identity);

                    bool ridgeAlongX = sx >= sz;
                    float span = ridgeAlongX ? sz : sx, run = ridgeAlongX ? sx : sz;
                    float rise = span * 0.14f;

                    Vector3 R(float across, float y, float along)
                        => centre + (ridgeAlongX ? new Vector3(along, y, across) : new Vector3(across, y, along));

                    float e = span * 0.5f + 0.4f, l = run * 0.5f + 0.4f;
                    var ridge0 = R(0f, height + rise, -l);
                    var ridge1 = R(0f, height + rise, l);

                    // Both slopes, wound up; then the gable ends closed.
                    AddUp(roof, R(-e, height, -l), R(-e, height, l), ridge1, ridge0);
                    AddUp(roof, R(e, height, l), R(e, height, -l), ridge0, ridge1);
                    AddOutward(shell, centre, R(-span * 0.5f, height, -run * 0.5f), R(span * 0.5f, height, -run * 0.5f), R(0f, height + rise, -run * 0.5f));
                    AddOutward(shell, centre, R(span * 0.5f, height, run * 0.5f), R(-span * 0.5f, height, run * 0.5f), R(0f, height + rise, run * 0.5f));

                    // Pilasters on the front, a roller door, and a strip of high windows.
                    for (float a = -faceWidth * 0.5f + 1f; a <= faceWidth * 0.5f - 1f; a += 6f)
                        shell.Box(face + alongFace * a + front * 0.2f + Vector3.up * height * 0.5f,
                                  Vec(frontX, 0.4f, height, 0.45f), Quaternion.identity);

                    float doorWidth = Mathf.Min(6f, faceWidth * 0.35f);
                    float doorAt = Rand(rng, -faceWidth * 0.25f, faceWidth * 0.25f);
                    door.Box(face + alongFace * doorAt + front * 0.08f + Vector3.up * 2.6f, Vec(frontX, doorWidth, 5.2f, 0.16f), Quaternion.identity);

                    for (float a = -faceWidth * 0.5f + 3f; a <= faceWidth * 0.5f - 3f; a += 3f)
                        if (Mathf.Abs(a - doorAt) > doorWidth * 0.5f + 1f)
                            glass.Box(face + alongFace * a + front * 0.06f + Vector3.up * (height - 1.4f),
                                      Vec(frontX, 2f, 0.9f, 0.12f), Quaternion.identity);
                    break;
                }

                case 1:
                {
                    // Brick workshop: flat roof behind a parapet, windows in rows, a door.
                    height = Rand(rng, 5.5f, 9.5f);
                    wallMat = _denseBrick[rng.Next(_denseBrick.Length)];

                    shell.Box(centre + Vector3.up * height * 0.5f, new Vector3(sx, height, sz), Quaternion.identity);
                    shell.Box(centre + Vector3.up * (height + 0.4f), new Vector3(sx + 0.3f, 0.8f, sz + 0.3f), Quaternion.identity);
                    roof.Box(centre + Vector3.up * (height + 0.05f), new Vector3(sx - 0.6f, 0.1f, sz - 0.6f), Quaternion.identity);

                    for (float y = 2.3f; y < height - 1.4f; y += 3.3f)
                        for (float a = -faceWidth * 0.5f + 2.2f; a <= faceWidth * 0.5f - 2.2f; a += 3.6f)
                            glass.Box(face + alongFace * a + front * 0.06f + Vector3.up * y, Vec(frontX, 1.6f, 1.5f, 0.12f), Quaternion.identity);

                    door.Box(face + alongFace * Rand(rng, -faceWidth * 0.3f, faceWidth * 0.3f) + front * 0.08f + Vector3.up * 1.3f,
                             Vec(frontX, 1.4f, 2.6f, 0.16f), Quaternion.identity);
                    break;
                }

                case 2:
                {
                    // Process building: tall, with a duct up one side and plant on the roof.
                    height = Rand(rng, 15f, 26f);
                    wallMat = _denseCladding[rng.Next(_denseCladding.Length)];

                    shell.Box(centre + Vector3.up * height * 0.5f, new Vector3(sx, height, sz), Quaternion.identity);

                    // Bands of glazing every storey.
                    for (float y = 3.5f; y < height - 2f; y += 4.2f)
                        glass.Box(face + front * 0.06f + Vector3.up * y, Vec(frontX, faceWidth - 3f, 0.9f, 0.12f), Quaternion.identity);

                    // A duct up the front, off-centre, and one across the roof.
                    float ductAt = faceWidth * Rand(rng, -0.35f, 0.35f);
                    door.Box(face + alongFace * ductAt + front * 0.9f + Vector3.up * (height * 0.5f + 1f),
                             Vec(frontX, 1.6f, height + 2f, 1.6f), Quaternion.identity);

                    for (int k = 0; k < 3; k++)
                        roof.Box(centre + new Vector3(Rand(rng, -sx * 0.3f, sx * 0.3f), height + 1.1f, Rand(rng, -sz * 0.3f, sz * 0.3f)),
                                 new Vector3(Rand(rng, 2.5f, 5f), 2.2f, Rand(rng, 2.5f, 5f)), Quaternion.identity);

                    // An extract fan that turns: the building is running.
                    RoofFan(parent, layer, rng, centre + new Vector3(-sx * 0.22f, height, sz * 0.22f),
                            Rand(rng, 1.1f, 1.6f));

                    if (rng.NextDouble() < 0.5)
                        roof.Tube(centre + new Vector3(sx * 0.25f, height, -sz * 0.25f),
                                  centre + new Vector3(sx * 0.25f, height + Rand(rng, 8f, 14f), -sz * 0.25f), 0.8f, 0.6f, 10);
                    break;
                }

                default:
                {
                    // Canopy: a back wall, posts along the open front, a roof sloping to it.
                    // The one building here a player can walk into, so nothing is sealed.
                    closed = false;
                    height = Rand(rng, 4.8f, 6f);
                    wallMat = _denseCladding[rng.Next(_denseCladding.Length)];

                    Vector3 back = centre - front * halfDepth;
                    shell.Box(back + front * 0.2f + Vector3.up * height * 0.5f, Vec(frontX, faceWidth, height, 0.4f), Quaternion.identity);

                    for (float a = -faceWidth * 0.5f + 0.4f; a <= faceWidth * 0.5f; a += 5f)
                        shell.Box(face - front * 0.4f + alongFace * a + Vector3.up * (height - 1f) * 0.5f,
                                  new Vector3(0.35f, height - 1f, 0.35f), Quaternion.identity);

                    var lowFront = face + Vector3.up * (height - 1f);
                    var highBack = back + Vector3.up * height;
                    Vector3 half = alongFace * (faceWidth * 0.5f + 0.3f);
                    AddUp(roof, highBack - half, highBack + half, lowFront + half + front * 0.6f, lowFront - half + front * 0.6f);
                    break;
                }
            }

            var go = MeshObject(parent, "Building", ToMesh(shell, DenseKey("building")), wallMat,
                                Vector3.zero, Quaternion.identity, Vector3.one, layer,
                                kind == 1 ? "Concrete" : "Metal");
            NoStanding(go);

            if (roof.Triangles.Count > 0)
            {
                var roofGo = MeshObject(parent, "Roof", ToMesh(roof, DenseKey("roof")), roofMat,
                                        Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");
                NoStanding(roofGo);
                Hide(roofGo);
            }

            if (glass.Triangles.Count > 0)
                Hide(MeshObject(parent, "Windows", ToMesh(glass, DenseKey("glass")), _denseGlass,
                                Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false));

            if (door.Triangles.Count > 0)
            {
                // The duct is clad, not steel: the zone's steel is the pipe set's map, whose
                // pipe ends read as a column of portholes on anything flat.
                var doorGo = MeshObject(parent, "Doors", ToMesh(door, DenseKey("door")), kind == 2 ? wallMat : _denseDoor,
                                        Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal", collider: kind == 2);
                if (kind == 2) NoStanding(doorGo);
                Hide(doorGo);
            }

            if (closed) SealBox(parent, centre + Vector3.up * height * 0.5f, new Vector3(sx, height, sz));
            else
            {
                // Something to take cover behind, under it.
                for (int k = 0; k < 2; k++)
                    Place(parent, k == 0 ? Pallets : BarrelSet,
                          centre + alongFace * Rand(rng, -faceWidth * 0.3f, faceWidth * 0.3f) - front * halfDepth * 0.2f,
                          (float)rng.NextDouble() * 360f);
            }
        }

        /// <summary>A box size given as (along the face, height, out from the face).</summary>
        private static Vector3 Vec(bool frontX, float along, float y, float out_)
            => frontX ? new Vector3(out_, y, along) : new Vector3(along, y, out_);

        /// <summary>A quad wound to face up, whichever way round its corners were given.</summary>
        private static void AddUp(MeshBuild build, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            if (Vector3.Cross(b - a, c - a).y >= 0f) build.Quad(a, b, c, d);
            else build.Quad(d, c, b, a);
        }

        /// <summary>A quad wound to face down: a ceiling.</summary>
        private static void AddDown(MeshBuild build, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            if (Vector3.Cross(b - a, c - a).y <= 0f) build.Quad(a, b, c, d);
            else build.Quad(d, c, b, a);
        }

        /// <summary>A triangle wound to face away from a point inside the shape it closes.</summary>
        private static void AddOutward(MeshBuild build, Vector3 inside, Vector3 a, Vector3 b, Vector3 c)
        {
            var normal = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(normal, (a + b + c) / 3f - inside) >= 0f) build.Tri(a, b, c);
            else build.Tri(c, b, a);
        }
    }
}
#endif
