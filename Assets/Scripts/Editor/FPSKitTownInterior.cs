#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Houses you can go into, built inside as houses are (2026-10-21, "the house lacks any physical real interior"):
    ///
    ///   * Rooms, not a box: the front room you come in to (a hall-living room, the stair's foot in it), a partition with
    ///     doorways to the back -- a kitchen and a bedroom, or a kitchen with its table -- and in a big back room a small
    ///     bathroom. Doorways are 1.8 m with a pair of leaves folded back against the wall, the front door too (an agent is
    ///     0.5 m either side of its middle on a 17 cm grid and the jambs rasterise a voxel wide: at 1.2-1.3 m the navmesh
    ///     never got in, at 1.5-1.6 m only now and then, and the rooms behind were sealed as islands); a bathroom keeps a
    ///     real 0.9 m door, swung open into it, and nothing but the player goes in.
    ///   * Two storeys: a straight stair 1.6 m wide up a side wall, the slab cut over it, a solid balustrade round the
    ///     stairwell upstairs, and bedrooms up there; the first floor is walkable (BuildMass keeps it off the Not Walkable
    ///     volume). A stair that does not fit leaves the house one storey inside.
    ///   * Finished: the house's paint on its walls, a floor per room (tiles, terrazzo, parquet; marble mosaic in a
    ///     bathroom), white tiles behind a kitchen counter and up a bathroom's walls, skirting, a lamp or a fan on each
    ///     ceiling, glass on the inside of the upper windows.
    ///   * Furnished by room, each piece against a wall and clear of doorways, the stair and (if it is tall) the windows:
    ///     sofas, a low table on a rug, a television on a cabinet, shelves; a kitchen counter with its sink, burners, wall
    ///     cupboards and a fridge, a table and chairs; a bed made up, a wardrobe; a basin, a toilet, a water heater. The old
    ///     town's houses have a majlis instead -- floor mattresses and cushions round a carpet, a tray on a stool, brass and
    ///     lanterns -- and a whitewashed or mud-plastered inside.
    ///
    /// Planned in the house's own frame: across (A, left to right seen from the street) and depth (D, in from the front
    /// wall's inner face), so every house is the same problem whichever way it faces. Big pieces go in the chunk mesh
    /// (they are walls and floors, physical); skirting, picture frames, the stair's rail go in the detail mesh.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private const float HouseWall = 0.25f, DoorWidth = 1.8f, DoorHeight = 2.2f, StairWidth = 1.6f, HouseTread = 0.26f, PartitionThick = 0.1f;

        // The stair's foot is 1.5 m in from the front wall: an agent keeps half a metre off every wall and the rail closes the
        // flight's open side, so the first stair (its foot 0.5 m in) could only be got onto from its top and every room
        // upstairs was sealed as an island. Risers of 19 cm on 26 cm treads keep the longer approach inside the house.
        private const float HouseStairFoot = 1.5f, HouseRiser = 0.19f, HouseHeadroom = 2.45f, HouseLanding = 1.5f;

        /// <summary>Where a house's stair goes, worked out before its walls (they need to know whether there is an upstairs).</summary>
        private sealed class HousePlan
        {
            public bool upstairs;
            public bool stairRight;          // up the right-hand wall (seen from the street), else the left
            public float stairStart, run;    // depth of the first riser; the flight's length
            public int steps;
            public float holeFrom, holeTo;   // the stairwell, in depth
            public float split;              // depth of the partition between the front room and the back
        }

        private sealed class HouseFrame
        {
            public Vector3 o, A, D;
            public float W, Dp;
            public Vector3 P(float a, float d, float y) => o + A * a + D * d + Vector3.up * y;
            public Vector2 Local(Vector3 p) => new Vector2(Vector3.Dot(p - o, A), Vector3.Dot(p - o, D));
            public float Yaw(Vector2 dir) { var w = A * dir.x + D * dir.y; return Mathf.Atan2(w.x, w.z) * Mathf.Rad2Deg; }
        }

        private sealed class Room
        {
            public Rect r;                  // local (across, depth)
            public float y;                 // floor, world height
            public string kind;
            public readonly List<Rect> keep = new List<Rect>();                       // doorways, the stair, the stairwell
            public readonly List<Rect> placed = new List<Rect>();
            public readonly List<(int wall, float from, float to)> windows = new List<(int, float, float)>();
            /// <summary>Which of its walls (0 front, 1 back, 2 left, 3 right) are the house's outer walls, where windows are.</summary>
            public readonly bool[] outer = new bool[4];
            /// <summary>The stair's footprint on this floor (downstairs the flight, upstairs the stairwell), and the part of
            /// the room with no floor: the stairwell. A room's tiles laid over the stairwell put a lid on the flight 2.8 m up,
            /// and with no headroom over its upper steps every room upstairs was an island.</summary>
            public Rect flight, open;
        }

        private static Material[] _paints, _floors, _cloth;
        private static Material _paintWash, _paintMud, _wallTile, _bathFloor, _oldFloor, _oldFloor2, _cabinetWhite, _cabinetGreen, _lampGlow, _mattressMat, _interiorCarpet;

        private static void ResolveInteriorMaterials()
        {
            if (_paints != null && _paints[0] != null) return;
            // Paints: the wanted colour over painted_plaster_wall's mean (171, 163, 160).
            _paints = new[]
            {
                TownMat("PaintWhite", "painted_plaster_wall", new Color(1.36f, 1.40f, 1.36f), 2.5f),
                TownMat("PaintCream", "painted_plaster_wall", new Color(1.38f, 1.39f, 1.25f), 2.5f),
                TownMat("PaintYellow", "painted_plaster_wall", new Color(1.40f, 1.40f, 1.12f), 2.5f),
                TownMat("PaintGreen", "painted_plaster_wall", new Color(1.20f, 1.36f, 1.25f), 2.5f),
                TownMat("PaintBlue", "painted_plaster_wall", new Color(1.17f, 1.32f, 1.42f), 2.5f),
                TownMat("PaintPeach", "painted_plaster_wall", new Color(1.39f, 1.29f, 1.16f), 2.5f),
                TownMat("PaintGrey", "painted_plaster_wall", new Color(1.20f, 1.26f, 1.25f), 2.5f),
            };
            _paintWash = TownMat("PaintWhitewash", "white_stucco", new Color(1.05f, 1.03f, 0.98f), 2f);
            _paintMud = TownMat("PaintMudInside", "clay_plaster", new Color(1.75f, 1.70f, 1.62f), 3f);
            _floors = new[]
            {
                TownMat("FloorChecker", "floor_tiles_06", new Color(1.15f, 1.15f, 1.15f), 3.0f, 0.45f),
                TownMat("FloorBeigeTiles", "floor_tiles_08", new Color(1.18f, 1.15f, 1.12f), 1.5f, 0.4f),
                TownMat("FloorGreyTiles", "large_floor_tiles_02", new Color(1.25f, 1.22f, 1.20f), 3.0f, 0.45f),
                TownMat("FloorDiagonal", "interior_tiles", new Color(1.12f, 1.12f, 1.12f), 1.9f, 0.4f),
                TownMat("FloorParquet", "herringbone_parquet", new Color(1.0f, 0.95f, 0.9f), 3.4f, 0.35f),
                _terrazzo,
            };
            _wallTile = TownMat("WallTilesWhite", "long_white_tiles", new Color(1.1f, 1.1f, 1.12f), 1.27f, 0.55f);
            _bathFloor = TownMat("FloorBathMosaic", "marble_tiles", new Color(1.45f, 1.48f, 1.52f), 2.0f, 0.45f);
            _oldFloor = TownMat("FloorTerracotta", "patterned_terracotta_tiling", new Color(1.5f, 1.45f, 1.40f), 2.0f, 0.3f);
            _oldFloor2 = TownMat("FloorOldMosaic", "old_mosaic_floor", new Color(1.45f, 1.42f, 1.38f), 2.0f, 0.3f);
            _cabinetWhite = TownFlat("CabinetWhite", new Color(0.86f, 0.85f, 0.82f), 0.35f);
            _cabinetGreen = TownFlat("CabinetGreen", new Color(0.36f, 0.46f, 0.40f), 0.3f);
            _lampGlow = EmissiveFlat("LampGlow", new Color(1f, 0.92f, 0.78f), 2.2f);
            _mattressMat = TownFlat("Mattress", new Color(0.88f, 0.86f, 0.80f), 0.1f);
            _cloth = new[]
            {
                TownFlat("ClothRed", new Color(0.46f, 0.09f, 0.08f), 0.15f), TownFlat("ClothNavy", new Color(0.09f, 0.13f, 0.30f), 0.15f),
                TownFlat("ClothOlive", new Color(0.32f, 0.34f, 0.16f), 0.15f), TownFlat("ClothMaroon", new Color(0.30f, 0.08f, 0.12f), 0.15f),
                TownFlat("ClothSand", new Color(0.72f, 0.62f, 0.45f), 0.15f),
            };
            _interiorCarpet = CarpetMat();
        }

        private static HouseFrame HouseFrameOf(Mass b)
        {
            var inner = Rect.MinMaxRect(b.r.xMin + HouseWall, b.r.yMin + HouseWall, b.r.xMax - HouseWall, b.r.yMax - HouseWall);
            var (ia0, ib0) = FaceRun(inner, b.door);
            return new HouseFrame
            {
                o = ia0, A = (ib0 - ia0).normalized, D = -SideOut3(b.door),
                W = Vector3.Distance(ia0, ib0), Dp = b.door < 2 ? inner.height : inner.width,
            };
        }

        /// <summary>Where the stair goes and whether there is an upstairs, before any wall is built.</summary>
        private static HousePlan PlanHouse(Mass b)
        {
            var f = HouseFrameOf(b);
            var own = new System.Random(LayoutSeed(new Vector3(b.r.center.x, 2f, b.r.center.y)) ^ 0x4805E);
            var plan = new HousePlan { split = Mathf.Clamp(f.Dp * Rand(own, 0.5f, 0.6f), 3.2f, Mathf.Max(3.2f, f.Dp - 3.0f)) };
            if (b.floors < 2) return plan;
            float rise = b.FloorY(1) - b.FloorY(0);
            plan.steps = Mathf.CeilToInt(rise / HouseRiser);
            plan.run = plan.steps * HouseTread;
            plan.stairStart = HouseStairFoot;
            if (f.Dp < plan.stairStart + plan.run + 1.8f || f.W < StairWidth + 3.0f) return plan;
            plan.stairRight = own.NextDouble() < 0.5;
            // The slab is cut from where a step has less than the headroom under it to the stair's head.
            plan.holeFrom = plan.stairStart + plan.run * (rise - HouseHeadroom) / rise - 0.1f;
            plan.holeTo = plan.stairStart + plan.run;
            // The partition between the front and the back clear of the stair's head: across the flight (it runs on past
            // the partition into the back), or far enough past the head for a landing to turn on (half a metre off the
            // partition and off the balustrade's end each, and room to stand between); a partition 0.6 m past the head
            // left the stair arriving at a wall.
            bool crosses = plan.split > plan.stairStart + 0.3f && plan.split < plan.holeTo - 0.3f;
            if (!crosses && plan.split < plan.holeTo + HouseLanding)
                plan.split = plan.holeTo + HouseLanding <= f.Dp - 2.6f ? plan.holeTo + HouseLanding : plan.holeTo - 0.6f;
            plan.upstairs = true;
            return plan;
        }

        // ------------------------------------------------------------------
        /// <summary>The inside of a house you can enter (BuildMass calls it; the outer walls and their openings are built).</summary>
        private static void HouseInterior(TownMesh m, TownMesh det, Mass b, Transform props)
        {
            ResolveInteriorMaterials();
            // The single room this replaced made these draws from the building's sequence; made the same, every building after
            // this one comes out as it did (3-5 pieces: which, which wall, where; and the cushions' turn).
            var seq = b.rng;
            for (int i = 0, n = seq.Next(3, 6); i < n; i++) { seq.Next(12); seq.Next(4); seq.NextDouble(); }
            seq.NextDouble();
            var f = HouseFrameOf(b);
            var plan = b.plan ?? PlanHouse(b);
            var own = new System.Random(LayoutSeed(new Vector3(b.r.center.x, 3f, b.r.center.y)) ^ 0x1D00);
            bool old = b.oldStyle;
            Material paint = old ? (own.NextDouble() < 0.5 ? _paintWash : _paintMud) : _paints[own.Next(_paints.Length)];
            int floors = plan.upstairs ? 2 : 1;
            float stairA0 = plan.stairRight ? f.W - StairWidth : 0f, stairA1 = stairA0 + StairWidth;
            var hole = plan.upstairs ? Rect.MinMaxRect(stairA0, plan.holeFrom, stairA1, plan.holeTo) : Rect.zero;

            // Markers for the checks and the views: the house, its front door (outside it), each room (VerifyTown proves
            // every room reachable from the door on the navmesh).
            if (_roomMarks == null) { _roomMarks = new GameObject("HouseRooms").transform; _roomMarks.SetParent(_volumes.parent, false); }
            var house = new GameObject(old ? "OldHouse" : "House").transform;
            house.SetParent(_roomMarks, false);
            foreach (var o in b.openings)
                if (o.side == b.door && o.floor == 0 && o.h.w - o.h.z > 2f)
                {
                    var (fa, fb) = FaceRun(b.r, o.side);
                    var outside = fa + (fb - fa).normalized * ((o.h.x + o.h.y) * 0.5f) + SideOut3(o.side) * 1.0f;
                    outside.y = b.baseY;
                    Mark(house, "Door", outside);
                    break;
                }

            for (int fl = 0; fl < floors; fl++)
            {
                float y0 = b.FloorY(fl);
                float y1 = (fl + 1 < b.floors ? b.FloorY(fl + 1) : b.Top) - 0.25f;     // the ceiling, under the slab or the roof
                var all = new Rect(0f, 0f, f.W, f.Dp);

                // Floor and ceiling, the stairwell cut out of the first floor's floor and the ground floor's ceiling.
                var cut = fl == 1 ? hole : Rect.zero;
                var cutAbove = fl == 0 && plan.upstairs ? hole : Rect.zero;
                foreach (var r in Minus(all, cut)) Slab(m, f, r, y0, null, true);
                foreach (var r in Minus(all, cutAbove)) Slab(m, f, r, y1, _plasterIn, false);
                if (cutAbove.width > 0f) SlabEdges(m, f, cutAbove, y1, y1 + 0.25f, paint);

                // The inside faces of the outer walls, cut where the outer wall's openings for this floor are.
                OuterInnerFaces(m, det, b, f, fl, y0, y1, paint, old);

                // Rooms.
                var rooms = new List<Room>();
                float split = plan.split;
                // The partition stops at the stair where it crosses the flight downstairs, or the stairwell upstairs (short of
                // the stairwell, upstairs, it runs across: there is floor over the flight's foot).
                bool stairCrosses = plan.upstairs && (fl == 0 ? plan.stairStart < split && plan.holeTo > split : plan.holeFrom < split && plan.holeTo > split);
                float pa0 = plan.upstairs && stairCrosses && !plan.stairRight ? stairA1 : 0f;
                float pa1 = plan.upstairs && stairCrosses && plan.stairRight ? stairA0 : f.W;
                var front = new Room { r = Rect.MinMaxRect(0f, 0f, f.W, split), y = y0, kind = fl == 0 ? (old ? "majlis" : "living") : "bedroom" };
                rooms.Add(front);
                var backs = new List<Room>();
                float bw = pa1 - pa0;
                if (bw >= 6.0f)
                {
                    float mid = pa0 + bw * Rand(own, 0.42f, 0.58f);
                    backs.Add(new Room { r = Rect.MinMaxRect(pa0, split, mid, f.Dp), y = y0 });
                    backs.Add(new Room { r = Rect.MinMaxRect(mid, split, pa1, f.Dp), y = y0 });
                    Partition(m, det, f, new Vector2(mid, split), new Vector2(mid, f.Dp), y0, y1, paint, null);
                }
                else backs.Add(new Room { r = Rect.MinMaxRect(pa0, split, pa1, f.Dp), y = y0 });
                if (fl == 0)
                {
                    backs[0].kind = old ? "oldkitchen" : "kitchen";
                    if (backs.Count > 1) backs[1].kind = old ? "majlis" : "bedroom";
                }
                else
                {
                    backs[0].kind = "bedroom";
                    if (backs.Count > 1) backs[1].kind = own.NextDouble() < 0.5 ? "bedroom" : "living";
                }
                rooms.AddRange(backs);

                // The partition between the front room and the back, a doorway into each back room.
                var doors = new List<float>();
                foreach (var bk in backs)
                {
                    float lo = bk.r.xMin + 0.35f + DoorWidth * 0.5f, hi = bk.r.xMax - 0.35f - DoorWidth * 0.5f;
                    // Where both leaves have wall to fold back against (else, a narrow room, in its middle).
                    float lo2 = bk.r.xMin + DoorWidth + 0.12f, hi2 = bk.r.xMax - DoorWidth - 0.12f;
                    if (hi2 > lo2) doors.Add(Rand(own, lo2, hi2));
                    else if (hi > lo) doors.Add((lo + hi) * 0.5f);
                }
                Partition(m, det, f, new Vector2(pa0, split), new Vector2(pa1, split), y0, y1, paint, doors);
                foreach (float d in doors)
                {
                    var keep = Rect.MinMaxRect(d - DoorWidth * 0.5f - 0.1f, split - 1.1f, d + DoorWidth * 0.5f + 0.1f, split + 1.1f);
                    var leaves = Rect.MinMaxRect(d - DoorWidth - 0.1f, split, d + DoorWidth + 0.1f, split + 0.45f);      // folded back, on the back's side
                    foreach (var r in rooms) { r.keep.Add(keep); r.keep.Add(leaves); }
                }

                // A bathroom in the corner of a big back room on the ground floor (or of the first floor's back room).
                foreach (var bk in backs.ToList())
                    if ((fl == 0 ? bk.kind == "bedroom" || bk.kind == "kitchen" : bk.kind == "bedroom") && !old &&
                        bk.r.width >= 3.8f && bk.r.height >= 3.6f && !rooms.Any(r => r.kind == "bath" && r.y == y0))
                    {
                        bool right = bk.r.xMax >= f.W - 0.01f;                     // at the outer wall's corner
                        var bath = right ? Rect.MinMaxRect(bk.r.xMax - 1.9f, bk.r.yMax - 2.1f, bk.r.xMax, bk.r.yMax)
                                         : Rect.MinMaxRect(bk.r.xMin, bk.r.yMax - 2.1f, bk.r.xMin + 1.9f, bk.r.yMax);
                        if (plan.upstairs && bath.Overlaps(Rect.MinMaxRect(stairA0 - 0.2f, 0f, stairA1 + 0.2f, f.Dp))) continue;
                        float edge = right ? bath.xMin : bath.xMax;
                        Partition(m, det, f, new Vector2(bath.xMin, bath.yMin), new Vector2(bath.xMax, bath.yMin), y0, y1, _wallTile, new List<float> { (bath.xMin + bath.xMax) * 0.5f }, 0.9f);
                        Partition(m, det, f, new Vector2(edge, bath.yMin), new Vector2(edge, bath.yMax), y0, y1, _wallTile, null);
                        bk.placed.Add(bath);
                        bk.keep.Add(Rect.MinMaxRect(bath.center.x - 0.6f, bath.yMin - 1.0f, bath.center.x + 0.6f, bath.yMin));
                        rooms.Add(new Room { r = bath, y = y0, kind = "bath" });
                        break;
                    }

                // The stair, the stairwell's balustrade, and room kept clear at its foot and head.
                if (plan.upstairs)
                {
                    var flight = Rect.MinMaxRect(stairA0, plan.stairStart - 1.2f, stairA1, plan.stairStart + plan.run + 1.3f);
                    foreach (var r in rooms)
                    {
                        r.keep.Add(flight);
                        r.flight = fl == 0 ? Rect.MinMaxRect(stairA0, plan.stairStart, stairA1, plan.holeTo) : hole;
                        if (fl == 1) r.open = hole;
                    }
                    if (fl == 0) Stair(m, det, f, b, plan, stairA0, stairA1, y0);
                    else StairwellBalustrade(m, f, plan, stairA0, stairA1, y0, paint);
                }
                // The front door: kept clear inside it.
                if (fl == 0)
                    foreach (var o in b.openings)
                        if (o.side == b.door && o.floor == 0 && o.h.w - o.h.z > 2f)
                        {
                            var a = DoorSpan(f, b, o.side, o.h);
                            front.keep.Add(Rect.MinMaxRect(a.x - 0.4f, 0f, a.y + 0.4f, 1.6f));
                            front.keep.Add(Rect.MinMaxRect(a.x - 1.0f, 0f, a.y + 1.0f, 0.45f));                           // its leaves, folded back
                        }
                // Windows each room's tall furniture keeps out of the way of.
                foreach (var o in b.openings.Where(o => o.floor == fl))
                {
                    var span = DoorSpan(f, b, o.side, o.h);
                    int wall = LocalWall(b, o.side);
                    foreach (var r in rooms) r.windows.Add((wall, span.x - 0.15f, span.y + 0.15f));
                }

                foreach (var r in rooms)
                {
                    // The mark on the room's floor: off the stairwell, where a back room upstairs runs over it.
                    var at = r.r.center;
                    if (fl == 1 && hole.Contains(at))
                        at.x = r.r.xMax - hole.xMax > hole.xMin - r.r.xMin ? (hole.xMax + r.r.xMax) * 0.5f : (r.r.xMin + hole.xMin) * 0.5f;
                    Mark(house, $"Room {r.kind} f{fl}", f.P(at.x, at.y, r.y + 0.3f));
                    r.outer[0] = r.r.yMin < 0.01f; r.outer[1] = r.r.yMax > f.Dp - 0.01f; r.outer[2] = r.r.xMin < 0.01f; r.outer[3] = r.r.xMax > f.W - 0.01f;
                    FloorFinish(m, f, r, old, own);
                    Ceiling(m, det, props, f, r, y1, own);
                    Furnish(m, det, props, f, b, r, y1, old, own);
                }
            }
        }

        private static Transform _roomMarks;

        private static void Mark(Transform parent, string name, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
        }

        // ------------------------------------------------------------------
        // Geometry
        // ------------------------------------------------------------------
        /// <summary>The parts of <paramref name="r"/> outside <paramref name="cut"/> (none cut: r itself).</summary>
        private static IEnumerable<Rect> Minus(Rect r, Rect cut)
        {
            if (cut.width <= 0f || !r.Overlaps(cut)) { yield return r; yield break; }
            var c = Rect.MinMaxRect(Mathf.Max(r.xMin, cut.xMin), Mathf.Max(r.yMin, cut.yMin), Mathf.Min(r.xMax, cut.xMax), Mathf.Min(r.yMax, cut.yMax));
            if (c.yMin > r.yMin) yield return Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, c.yMin);
            if (c.yMax < r.yMax) yield return Rect.MinMaxRect(r.xMin, c.yMax, r.xMax, r.yMax);
            if (c.xMin > r.xMin) yield return Rect.MinMaxRect(r.xMin, c.yMin, c.xMin, c.yMax);
            if (c.xMax < r.xMax) yield return Rect.MinMaxRect(c.xMax, c.yMin, r.xMax, c.yMax);
        }

        /// <summary>A floor (facing up) or a ceiling (facing down) over a local rect; a null material is left to the rooms.</summary>
        private static void Slab(TownMesh m, HouseFrame f, Rect r, float y, Material mat, bool up)
        {
            if (r.width < 0.01f || r.height < 0.01f) return;
            if (mat == null) mat = _terrazzo;                       // under the rooms' own floors (each room lays its finish on top)
            m.Quad(mat, f.P(r.xMin, r.yMin, y), f.P(r.xMax, r.yMin, y), f.P(r.xMax, r.yMax, y), f.P(r.xMin, r.yMax, y), up ? Vector3.up : Vector3.down);
        }

        private static void SlabEdges(TownMesh m, HouseFrame f, Rect h, float y0, float y1, Material mat)
        {
            void Edge(Vector2 a, Vector2 b2, Vector2 inward)
            {
                Vector3 n = f.A * inward.x + f.D * inward.y;
                m.Quad(mat, f.P(a.x, a.y, y0), f.P(b2.x, b2.y, y0), f.P(b2.x, b2.y, y1), f.P(a.x, a.y, y1), n);
            }
            Edge(new Vector2(h.xMin, h.yMin), new Vector2(h.xMax, h.yMin), new Vector2(0f, 1f));
            Edge(new Vector2(h.xMin, h.yMax), new Vector2(h.xMax, h.yMax), new Vector2(0f, -1f));
            Edge(new Vector2(h.xMin, h.yMin), new Vector2(h.xMin, h.yMax), new Vector2(1f, 0f));
            Edge(new Vector2(h.xMax, h.yMin), new Vector2(h.xMax, h.yMax), new Vector2(-1f, 0f));
        }

        /// <summary>Which local wall (0 front, 1 back, 2 left, 3 right) an outer side is.</summary>
        private static int LocalWall(Mass b, int side) =>
            side == b.door ? 0 : side == (b.door ^ 1) ? 1 : side == StartNeighbour(b.door) ? 2 : 3;

        /// <summary>An opening's span along its local wall (across for front and back, depth for the sides).</summary>
        private static Vector2 DoorSpan(HouseFrame f, Mass b, int side, Vector4 h)
        {
            var (a0, b0) = FaceRun(b.r, side);
            Vector3 u = (b0 - a0).normalized;
            var p = f.Local(a0 + u * h.x); var q = f.Local(a0 + u * h.y);
            int wall = LocalWall(b, side);
            float s0 = wall < 2 ? p.x : p.y, s1 = wall < 2 ? q.x : q.y;
            return new Vector2(Mathf.Min(s0, s1), Mathf.Max(s0, s1));
        }

        /// <summary>
        /// The inside faces of the four outer walls on one floor, each cut where the outer wall has an opening on that
        /// floor (so every hole goes right through), with skirting along their foot; on the first floor, glass (or, in the
        /// old town, shut timber shutters) on the inside of each window.
        /// </summary>
        private static void OuterInnerFaces(TownMesh m, TownMesh det, Mass b, HouseFrame f, int fl, float y0, float y1, Material paint, bool old)
        {
            var inner = Rect.MinMaxRect(b.r.xMin + HouseWall, b.r.yMin + HouseWall, b.r.xMax - HouseWall, b.r.yMax - HouseWall);
            for (int side = 0; side < 4; side++)
            {
                var (a0, b0) = FaceRun(inner, side);
                var nrm = -SideOut3(side);
                Vector3 u = (b0 - a0).normalized;
                var holes = new List<Vector4>();
                foreach (var o in b.openings)
                {
                    if (o.side != side || o.floor != fl) continue;
                    float v0 = o.h.z + b.wallBottom - y0, v1 = Mathf.Min(o.h.w + b.wallBottom - y0, y1 - y0);
                    if (v1 <= 0f) continue;
                    var hole = new Vector4(o.h.x - HouseWall, o.h.y - HouseWall, Mathf.Max(0f, v0), v1);
                    holes.Add(hole);
                    if (fl > 0)
                    {
                        // The window from inside: its pane at the glass's plane, a sill board.
                        // (The outer glass is 0.21 m in from the outer face: 4 cm short of this inner face.)
                        Vector3 g0 = a0 + u * hole.x + Vector3.up * (y0 + hole.z) - nrm * (HouseWall - 0.21f);
                        Vector3 g1 = a0 + u * hole.y + Vector3.up * (y0 + hole.z) - nrm * (HouseWall - 0.21f);
                        float hh = hole.w - hole.z;
                        m.Quad(old ? _woodShutter : _shopGlass, g0, g1, g1 + Vector3.up * hh, g0 + Vector3.up * hh, nrm);
                        Prism(det, old ? _woodBeam : _coping, (a0 + u * ((hole.x + hole.y) * 0.5f)) + Vector3.up * (y0 + hole.z - 0.02f) + nrm * 0.06f,
                              u, Vector3.up, nrm, hole.y - hole.x + 0.1f, 0.04f, 0.3f);
                    }
                }
                m.WallWithHoles(paint, a0 + Vector3.up * y0, b0 + Vector3.up * y0, y1 - y0, nrm, holes);
                // Skirting, broken at doorways.
                float len = Vector3.Distance(a0, b0), from = 0f;
                foreach (var h in holes.Where(h => h.z < 0.05f).OrderBy(h => h.x))
                {
                    if (h.x > from + 0.05f) Prism(det, old ? _woodBeam : _frameDark, a0 + u * ((from + h.x) * 0.5f) + Vector3.up * (y0 + 0.05f) + nrm * 0.012f, u, Vector3.up, nrm, h.x - from, 0.1f, 0.012f);
                    from = h.y;
                }
                if (len > from + 0.05f) Prism(det, old ? _woodBeam : _frameDark, a0 + u * ((from + len) * 0.5f) + Vector3.up * (y0 + 0.05f) + nrm * 0.012f, u, Vector3.up, nrm, len - from, 0.1f, 0.012f);
            }
        }

        /// <summary>
        /// A partition from <paramref name="a"/> to <paramref name="c"/> (local, axis-aligned), floor to ceiling, 10 cm
        /// thick: both faces, doorways with jambs and a head, its ends capped, a door leaf swung back against it, skirting.
        /// </summary>
        private static void Partition(TownMesh m, TownMesh det, HouseFrame f, Vector2 a, Vector2 c, float y0, float y1, Material mat,
                                      List<float> doors, float doorWidth = DoorWidth)
        {
            float len = Vector2.Distance(a, c);
            if (len < 0.3f) return;
            Vector2 along = (c - a) / len, across = new Vector2(-along.y, along.x);
            Vector3 nA = f.A * across.x + f.D * across.y, u = f.A * along.x + f.D * along.y;
            float h = y1 - y0;
            var holes = new List<Vector4>();
            if (doors != null)
                foreach (float d in doors)
                {
                    float s = along.x != 0f ? (d - a.x) * Mathf.Sign(along.x) : (d - a.y) * Mathf.Sign(along.y);   // along it from a
                    if (s - doorWidth * 0.5f < 0.1f || s + doorWidth * 0.5f > len - 0.1f) continue;
                    holes.Add(new Vector4(s - doorWidth * 0.5f, s + doorWidth * 0.5f, 0f, DoorHeight));
                }
            Vector3 p0 = f.P(a.x, a.y, y0), p1 = f.P(c.x, c.y, y0), off = nA * (PartitionThick * 0.5f);
            m.WallWithHoles(mat, p0 + off, p1 + off, h, nA, holes);
            m.WallWithHoles(mat, p0 - off, p1 - off, h, -nA, holes);
            // The ends, where nothing meets them.
            m.Quad(mat, p0 - off, p0 + off, p0 + off + Vector3.up * h, p0 - off + Vector3.up * h, -u);
            m.Quad(mat, p1 - off, p1 + off, p1 + off + Vector3.up * h, p1 - off + Vector3.up * h, u);
            foreach (var hl in holes)
            {
                // Jambs and head through the thickness, a lining, a leaf swung back against the wall.
                foreach (float e in new[] { hl.x, hl.y })
                    m.Quad(mat, p0 + u * e - off, p0 + u * e + off, p0 + u * e + off + Vector3.up * DoorHeight, p0 + u * e - off + Vector3.up * DoorHeight, e == hl.x ? u : -u);
                m.Quad(mat, p0 + u * hl.x - off + Vector3.up * DoorHeight, p0 + u * hl.y - off + Vector3.up * DoorHeight, p0 + u * hl.y + off + Vector3.up * DoorHeight, p0 + u * hl.x + off + Vector3.up * DoorHeight, Vector3.down);
                foreach (var side in new[] { off, -off })
                {
                    var n = side.normalized;
                    Bar(det, _frameWood, p0 + u * (hl.x - 0.04f) + side, p0 + u * (hl.x - 0.04f) + side + Vector3.up * (DoorHeight + 0.04f), n, 0.08f, 0.02f);
                    Bar(det, _frameWood, p0 + u * (hl.y + 0.04f) + side, p0 + u * (hl.y + 0.04f) + side + Vector3.up * (DoorHeight + 0.04f), n, 0.08f, 0.02f);
                    Bar(det, _frameWood, p0 + u * (hl.x - 0.08f) + side + Vector3.up * (DoorHeight + 0.04f), p0 + u * (hl.y + 0.08f) + side + Vector3.up * (DoorHeight + 0.04f), n, 0.08f, 0.02f);
                }
                if (doorWidth >= DoorWidth - 0.01f)
                {
                    // A pair of leaves, each folded back against the wall; none where the partition ends before it (a leaf
                    // there stood through the outer wall and out of the facade).
                    float leafW = doorWidth * 0.5f - 0.02f;
                    foreach (var (e, dir) in new[] { (hl.x, -1f), (hl.y, 1f) })
                        if (e + dir * leafW > 0.02f && e + dir * leafW < len - 0.02f)
                            m.Box(_doorWood, p0 + u * (e + dir * leafW * 0.5f) + off + nA * 0.03f + Vector3.up * (DoorHeight * 0.5f),
                                  new Vector3(leafW, DoorHeight - 0.03f, 0.04f), Quaternion.LookRotation(nA));
                }
                else
                    // One leaf swung open into the room it shuts, square to the partition at its jamb (a bathroom has no wall
                    // beside its door to fold one back against).
                    m.Box(_doorWood, p0 + u * (hl.x + 0.03f) + off + nA * ((doorWidth - 0.04f) * 0.5f) + Vector3.up * (DoorHeight * 0.5f),
                          new Vector3(0.04f, DoorHeight - 0.03f, doorWidth - 0.04f), Quaternion.LookRotation(nA));
            }
            // Skirting both sides, broken at the doorways.
            float from = 0f;
            foreach (var hl in holes.OrderBy(x => x.x).Append(new Vector4(len, len, 0f, 0f)))
            {
                if (hl.x > from + 0.05f)
                    foreach (var side in new[] { off, -off })
                        Prism(det, _frameDark, p0 + u * ((from + hl.x) * 0.5f) + side + side.normalized * 0.012f + Vector3.up * 0.05f, u, Vector3.up, side.normalized, hl.x - from, 0.1f, 0.012f);
                from = hl.y;
            }
        }

        /// <summary>
        /// The stair: a solid flight up the side wall (each step a block from the floor), 1.6 m wide, risers of 19 cm at
        /// most and 26 cm treads; a steel rail on posts along its open side.
        /// </summary>
        private static void Stair(TownMesh m, TownMesh det, HouseFrame f, Mass b, HousePlan plan, float a0, float a1, float y0)
        {
            float rise = b.FloorY(1) - b.FloorY(0), r = rise / plan.steps;
            var rot = Quaternion.LookRotation(f.D);
            var mat = b.oldStyle ? _coping : _stallMarble;
            for (int i = 0; i < plan.steps; i++)
            {
                float top = y0 + r * (i + 1);
                float d = plan.stairStart + HouseTread * (i + 0.5f);
                var c = f.P((a0 + a1) * 0.5f, d, (y0 - 0.02f + top) * 0.5f);
                m.Box(mat, c, new Vector3(StairWidth, top - y0 + 0.02f, HouseTread), rot, bottom: false);
            }
            // The rail on the open side: posts and a sloping top rail.
            float open = plan.stairRight ? a0 : a1;
            Vector3 Post(float d) { float k = Mathf.Clamp01((d - plan.stairStart) / plan.run); return f.P(open, d, y0 + k * rise); }
            Vector3 prev = Vector3.zero;
            for (float d = plan.stairStart + 0.15f; d <= plan.stairStart + plan.run; d += 0.9f)
            {
                var p = Post(d);
                m.Box(_steel, p + Vector3.up * 0.5f, new Vector3(0.04f, 1.0f, 0.04f), Quaternion.identity);
                if (prev != Vector3.zero)
                {
                    var t0 = prev + Vector3.up * 1.0f; var t1 = p + Vector3.up * 1.0f;
                    m.Box(_steel, (t0 + t1) * 0.5f, new Vector3(0.05f, 0.05f, Vector3.Distance(t0, t1)), Quaternion.LookRotation(t1 - t0));
                }
                prev = p;
            }
        }

        /// <summary>The stairwell's balustrade upstairs: a solid wall a metre high along its open side and across its foot.</summary>
        private static void StairwellBalustrade(TownMesh m, HouseFrame f, HousePlan plan, float a0, float a1, float y0, Material paint)
        {
            float open = plan.stairRight ? a0 - 0.05f : a1 + 0.05f;
            var rotD = Quaternion.LookRotation(f.D);
            float len = plan.holeTo - plan.holeFrom;
            m.Box(paint, f.P(open, (plan.holeFrom + plan.holeTo) * 0.5f, y0 + 0.5f), new Vector3(0.1f, 1.0f, len + 0.1f), rotD);
            m.Box(paint, f.P((a0 + a1) * 0.5f, plan.holeFrom - 0.05f, y0 + 0.5f), new Vector3(a1 - a0 + 0.1f, 1.0f, 0.1f), rotD);
            m.Box(_coping, f.P(open, (plan.holeFrom + plan.holeTo) * 0.5f, y0 + 1.02f), new Vector3(0.16f, 0.04f, len + 0.16f), rotD);
            m.Box(_coping, f.P((a0 + a1) * 0.5f, plan.holeFrom - 0.05f, y0 + 1.02f), new Vector3(a1 - a0 + 0.16f, 0.04f, 0.16f), rotD);
        }

        // ------------------------------------------------------------------
        // Finishes
        // ------------------------------------------------------------------
        private static void FloorFinish(TownMesh m, HouseFrame f, Room r, bool old, System.Random own)
        {
            Material mat = r.kind == "bath" ? _bathFloor
                         : old ? (own.NextDouble() < 0.5 ? _oldFloor : _oldFloor2)
                         : r.kind == "kitchen" ? (own.NextDouble() < 0.5 ? _floors[1] : _floors[5])
                         : _floors[own.Next(_floors.Length)];
            foreach (var x in Minus(r.r, r.open))
                m.Quad(mat, f.P(x.xMin, x.yMin, r.y + 0.004f), f.P(x.xMax, x.yMin, r.y + 0.004f), f.P(x.xMax, x.yMax, r.y + 0.004f), f.P(x.xMin, x.yMax, r.y + 0.004f), Vector3.up);
        }

        /// <summary>A light on the ceiling: a fan in a living room or a majlis, a glowing lamp elsewhere.</summary>
        private static void Ceiling(TownMesh m, TownMesh det, Transform props, HouseFrame f, Room r, float y1, System.Random own)
        {
            var c = f.P(r.r.center.x, r.r.center.y, y1);
            if ((r.kind == "living" || r.kind == "majlis") && own.NextDouble() < 0.75)
                PlaceTown(props, Prop("ceiling_fan"), c - Vector3.up * 0.55f, Rand(own, 0f, 90f), 0.5f, navBlock: false);
            else
            {
                m.Cylinder(_frameWhite, c - Vector3.up * 0.08f, 0.16f, 0.08f, 12, false, false);
                m.Disc(_lampGlow, c - Vector3.up * 0.081f, 0.15f, 12, Vector3.down);
            }
        }

        // ------------------------------------------------------------------
        // Furniture
        // ------------------------------------------------------------------
        /// <summary>
        /// Puts a piece against one of a room's walls (preferred walls first, then the others), facing into the room,
        /// clear of everything kept and placed and, when <paramref name="tall"/>, of the windows. Size is the piece's
        /// width along the wall and depth out from it. Returns its footprint's middle, or null.
        /// </summary>
        private static (Vector2 at, int wall)? AgainstWall(Room r, System.Random own, float w, float d, bool tall, int[] walls = null, float gap = 0.07f)
        {
            var order = (walls ?? new[] { 0, 1, 2, 3 }).Concat(new[] { 1, 2, 3, 0 }).Distinct().ToArray();
            foreach (int wall in order)
                for (int tries = 0; tries < 8; tries++)
                {
                    bool alongA = wall < 2;
                    float span = alongA ? r.r.width : r.r.height;
                    if (span < w + 0.2f) break;
                    float s = Rand(own, 0.1f + w * 0.5f, span - 0.1f - w * 0.5f);
                    Rect foot;
                    if (wall == 0) foot = new Rect(r.r.xMin + s - w * 0.5f, r.r.yMin + gap, w, d);
                    else if (wall == 1) foot = new Rect(r.r.xMin + s - w * 0.5f, r.r.yMax - gap - d, w, d);
                    else if (wall == 2) foot = new Rect(r.r.xMin + gap, r.r.yMin + s - w * 0.5f, d, w);
                    else foot = new Rect(r.r.xMax - gap - d, r.r.yMin + s - w * 0.5f, d, w);
                    if (!Free(r, foot)) continue;
                    if (tall)
                    {
                        float a0 = alongA ? foot.xMin : foot.yMin, a1 = alongA ? foot.xMax : foot.yMax;
                        if (r.outer[wall] && r.windows.Any(wi => wi.wall == wall && a1 > wi.from && a0 < wi.to)) continue;
                    }
                    r.placed.Add(foot);
                    return (foot.center, wall);
                }
            return null;
        }

        private static bool Free(Room r, Rect foot)
        {
            if (foot.xMin < r.r.xMin - 0.01f || foot.yMin < r.r.yMin - 0.01f || foot.xMax > r.r.xMax + 0.01f || foot.yMax > r.r.yMax + 0.01f) return false;
            var grown = Rect.MinMaxRect(foot.xMin - 0.08f, foot.yMin - 0.08f, foot.xMax + 0.08f, foot.yMax + 0.08f);
            return !r.keep.Any(k => k.Overlaps(grown)) && !r.placed.Any(p => p.Overlaps(grown));
        }

        /// <summary>Into the room from a wall (local direction).</summary>
        private static Vector2 Inward(int wall) => wall == 0 ? Vector2.up : wall == 1 ? Vector2.down : wall == 2 ? Vector2.right : Vector2.left;

        /// <summary>A prop against a wall, facing into the room. Width and depth as it stands at yaw 0 (measured).</summary>
        private static bool PropAgainstWall(Transform props, HouseFrame f, Room r, System.Random own, string id, float w, float d, bool tall,
                                            int[] walls = null, float height = -1f)
        {
            var spot = AgainstWall(r, own, w, d, tall, walls);
            if (spot == null) return false;
            var (at, wall) = spot.Value;
            PlaceTown(props, Prop(id), f.P(at.x, at.y, r.y), f.Yaw(Inward(wall)), height);
            return true;
        }

        private static void Furnish(TownMesh m, TownMesh det, Transform props, HouseFrame f, Mass b, Room r, float y1, bool old, System.Random own)
        {
            switch (r.kind)
            {
                case "living": Living(m, det, props, f, r, own); break;
                case "kitchen": Kitchen(m, det, props, f, r, own); break;
                case "bedroom": Bedroom(m, det, props, f, r, own); break;
                case "bath": Bath(m, det, f, r, own); break;
                case "majlis": Majlis(m, det, props, f, r, own); break;
                case "oldkitchen": OldKitchen(m, props, f, r, own); break;
            }
            if (r.kind != "bath") Pictures(det, f, r, own, old);
        }

        private static void Living(TownMesh m, TownMesh det, Transform props, HouseFrame f, Room r, System.Random own)
        {
            // Two sofas facing each other across a low table on a rug, a television on a cabinet, shelves.
            var s1 = AgainstWall(r, own, 1.85f, 0.85f, false, new[] { 1, 2, 3 });
            if (s1 != null)
            {
                var (at, wall) = s1.Value;
                PlaceTown(props, Prop("sofa_02"), f.P(at.x, at.y, r.y), f.Yaw(Inward(wall)));
                if (own.NextDouble() < 0.6) PlaceTown(props, Prop("throw_pillows_01"), f.P(at.x, at.y, r.y + 0.42f) + (f.A * Inward(wall).x + f.D * Inward(wall).y) * 0.05f, f.Yaw(Inward(wall)), 0.3f, navBlock: false);
                // The table and rug in front of it.
                var into = Inward(wall);
                Vector2 tc = at + into * 1.35f;
                bool alongA = wall < 2;
                var table = alongA ? new Rect(tc.x - 0.9f, tc.y - 0.33f, 1.8f, 0.66f) : new Rect(tc.x - 0.33f, tc.y - 0.9f, 0.66f, 1.8f);
                if (Free(r, table))
                {
                    r.placed.Add(table);
                    PlaceTown(props, Prop("WoodenTable_01"), f.P(tc.x, tc.y, r.y), f.Yaw(into));
                    if (own.NextDouble() < 0.6) PlaceTown(props, Prop("tea_set_01"), f.P(tc.x, tc.y, r.y + 0.55f), f.Yaw(into) + Rand(own, -20f, 20f), navBlock: false);
                    Rug(m, f, r, tc, alongA ? new Vector2(3.0f, 2.0f) : new Vector2(2.0f, 3.0f), own);
                }
            }
            var tv = AgainstWall(r, own, 1.2f, 0.62f, false, new[] { 0, 2, 3 });
            if (tv != null)
            {
                var (at, wall) = tv.Value;
                PlaceTown(props, Prop("painted_wooden_cabinet"), f.P(at.x, at.y, r.y), f.Yaw(Inward(wall)));
                PlaceTown(props, Prop("Television_01"), f.P(at.x, at.y, r.y + 1.18f), f.Yaw(Inward(wall)), navBlock: false);
            }
            if (own.NextDouble() < 0.7) PropAgainstWall(props, f, r, own, own.NextDouble() < 0.5 ? "wooden_bookshelf_worn" : "wooden_display_shelves_01", 1.4f, 0.6f, true);
            if (own.NextDouble() < 0.6) PropAgainstWall(props, f, r, own, "sofa_02", 1.85f, 0.85f, false);
            if (own.NextDouble() < 0.5) PropAgainstWall(props, f, r, own, "vintage_oil_lamp", 0.3f, 0.3f, false);
        }

        private static void Bedroom(TownMesh m, TownMesh det, Transform props, HouseFrame f, Room r, System.Random own)
        {
            // A double bed made up, its head to a wall; a wardrobe; a chair; a rug by the bed.
            var spot = AgainstWall(r, own, 1.7f, 2.1f, false, new[] { 1, 2, 3, 0 });
            if (spot != null)
            {
                var (at, wall) = spot.Value;
                Vector2 into = Inward(wall), side = new Vector2(-into.y, into.x);
                Vector3 Iw(Vector2 v) => f.A * v.x + f.D * v.y;
                Vector3 c = f.P(at.x, at.y, r.y);
                var rot = Quaternion.LookRotation(Iw(into));
                var wood = own.NextDouble() < 0.5 ? _frameWood : _doorWood;
                m.Box(wood, c + Vector3.up * 0.18f, new Vector3(1.6f, 0.36f, 2.0f), rot);                                  // base
                m.Box(_mattressMat, c + Vector3.up * 0.46f, new Vector3(1.52f, 0.2f, 1.92f), rot);                          // mattress
                var blanket = _cloth[own.Next(_cloth.Length)];
                m.Box(blanket, c + Vector3.up * 0.57f + Iw(into) * 0.3f, new Vector3(1.58f, 0.04f, 1.3f), rot);              // blanket
                m.Box(wood, c - Iw(into) * 1.0f + Vector3.up * 0.55f, new Vector3(1.66f, 1.1f, 0.06f), rot);                 // headboard
                foreach (float s in new[] { -0.38f, 0.38f })
                    m.Box(_mattressMat, c - Iw(into) * 0.75f + Iw(side) * s + Vector3.up * 0.62f, new Vector3(0.62f, 0.12f, 0.36f), rot);   // pillows
                Rug(m, f, r, at + into * 0.4f + side * 1.25f, wall < 2 ? new Vector2(0.9f, 1.6f) : new Vector2(1.6f, 0.9f), own);
            }
            if (!PropAgainstWall(props, f, r, own, "vintage_cabinet_01", 2.05f, 0.7f, true))
                PropAgainstWall(props, f, r, own, "painted_wooden_cabinet", 1.2f, 0.64f, false);
            if (own.NextDouble() < 0.7) PropAgainstWall(props, f, r, own, "painted_wooden_chair_01", 0.5f, 0.56f, false);
            if (own.NextDouble() < 0.4) PropAgainstWall(props, f, r, own, "vintage_suitcase", 1.62f, 0.3f, false);
        }

        /// <summary>
        /// A kitchen: a counter along a wall -- cupboards, a worktop with a sink and four burners, white tiles above it,
        /// wall cupboards over it -- a fridge at its end, a table with chairs, pots on the worktop.
        /// </summary>
        private static void Kitchen(TownMesh m, TownMesh det, Transform props, HouseFrame f, Room r, System.Random own)
        {
            float len = Mathf.Min(3.2f, Mathf.Max(1.8f, (r.r.width > r.r.height ? r.r.width : r.r.height) - 1.6f));
            var spot = AgainstWall(r, own, len + 0.75f, 0.66f, false, new[] { 1, 2, 3, 0 }, 0.06f);
            if (spot != null)
            {
                var (at, wall) = spot.Value;
                Vector2 into = Inward(wall), along = new Vector2(-into.y, into.x);
                Vector3 Iw(Vector2 v) => f.A * v.x + f.D * v.y;
                var rot = Quaternion.LookRotation(Iw(into));
                Vector3 c = f.P(at.x, at.y, r.y) - Iw(along) * 0.375f;            // the counter's middle (the fridge takes 0.75 at one end)
                var cab = own.NextDouble() < 0.6 ? _cabinetWhite : own.NextDouble() < 0.5 ? _cabinetGreen : _frameWood;
                var top = own.NextDouble() < 0.5 ? _stallriser : _stallMarble;
                m.Box(cab, c + Vector3.up * 0.43f + Iw(into) * 0.0f, new Vector3(len, 0.86f, 0.6f), rot);
                m.Box(top, c + Vector3.up * 0.88f + Iw(into) * 0.02f, new Vector3(len + 0.02f, 0.04f, 0.64f), rot);
                // Cupboard doors: seams on the front.
                for (float s = -len * 0.5f + 0.5f; s < len * 0.5f - 0.1f; s += 0.5f)
                    Bar(det, _frameDark, c + Iw(along) * s + Iw(into) * 0.301f + Vector3.up * 0.06f, c + Iw(along) * s + Iw(into) * 0.301f + Vector3.up * 0.82f, Iw(into), 0.01f, 0.005f);
                // The sink and its tap, four burners at the other end.
                Vector3 sink = c - Iw(along) * (len * 0.25f) + Vector3.up * 0.905f;
                m.Quad(_steel, sink - Iw(along) * 0.3f - Iw(into) * 0.2f, sink + Iw(along) * 0.3f - Iw(into) * 0.2f, sink + Iw(along) * 0.3f + Iw(into) * 0.2f, sink - Iw(along) * 0.3f + Iw(into) * 0.2f, Vector3.up);
                m.Box(_frameAlu, sink - Iw(into) * 0.26f + Vector3.up * 0.12f, new Vector3(0.03f, 0.24f, 0.03f), rot);
                Vector3 hob = c + Iw(along) * (len * 0.3f) + Vector3.up * 0.905f;
                foreach (var (x, z) in new[] { (-0.15f, -0.12f), (0.15f, -0.12f), (-0.15f, 0.12f), (0.15f, 0.12f) })
                    m.Disc(_tankBlack, hob + Iw(along) * x + Iw(into) * z, 0.09f, 10, Vector3.up);
                // Tiles behind it, wall cupboards over it (not over the sink's window).
                Vector3 wallFoot = c - Iw(into) * 0.299f;
                m.Quad(_wallTile, wallFoot - Iw(along) * (len * 0.5f) + Vector3.up * 0.9f, wallFoot + Iw(along) * (len * 0.5f) + Vector3.up * 0.9f,
                       wallFoot + Iw(along) * (len * 0.5f) + Vector3.up * 1.5f, wallFoot - Iw(along) * (len * 0.5f) + Vector3.up * 1.5f, Iw(into));
                m.Box(cab, c - Iw(into) * 0.12f + Iw(along) * (len * 0.2f) + Vector3.up * 1.9f, new Vector3(len * 0.55f, 0.7f, 0.35f), rot);
                // The fridge.
                m.Box(_tankWhite, c + Iw(along) * (len * 0.5f + 0.375f) + Vector3.up * 0.88f, new Vector3(0.7f, 1.76f, 0.64f), rot);
                Bar(det, _steel, c + Iw(along) * (len * 0.5f + 0.1f) + Iw(into) * 0.33f + Vector3.up * 1.0f, c + Iw(along) * (len * 0.5f + 0.1f) + Iw(into) * 0.33f + Vector3.up * 1.3f, Iw(into), 0.025f, 0.02f);
                // Pots on the worktop.
                string[] pots = { "pot_enamel_01", "jug_01", "brass_pot_01", "ceramic_vase_02", "wooden_bowl_01" };
                for (int i = 0; i < 3; i++)
                    PlaceTown(props, Prop(pots[own.Next(pots.Length)]), c + Iw(along) * Rand(own, -len * 0.45f, len * 0.45f) - Iw(into) * Rand(own, 0f, 0.15f) + Vector3.up * 0.9f,
                              Rand(own, 0f, 360f), navBlock: false);
            }
            // A table with chairs in the middle, if there is room.
            var tc = r.r.center;
            var table = new Rect(tc.x - 0.65f, tc.y - 0.45f, 1.3f, 0.9f);
            var round = Rect.MinMaxRect(table.xMin - 0.55f, table.yMin - 0.55f, table.xMax + 0.55f, table.yMax + 0.55f);
            if (Free(r, round))
            {
                r.placed.Add(round);
                PlaceTown(props, Prop("painted_wooden_table"), f.P(tc.x, tc.y, r.y), f.Yaw(Vector2.up), 0.76f);
                foreach (var (dx, dy, face) in new[] { (-0.35f, -0.75f, Vector2.up), (0.35f, -0.75f, Vector2.up), (-0.35f, 0.75f, Vector2.down), (0.35f, 0.75f, Vector2.down) })
                    PlaceTown(props, Prop("painted_wooden_chair_01"), f.P(tc.x + dx, tc.y + dy, r.y), f.Yaw(face) + Rand(own, -8f, 8f));
            }
            else PropAgainstWall(props, f, r, own, "outdoor_table_chair_set_01", 0.85f, 1.85f, false);
        }

        private static void Bath(TownMesh m, TownMesh det, HouseFrame f, Room r, System.Random own)
        {
            // White tiles up the walls to 1.6 m, a basin, a toilet, a water heater, a mirror.
            var x = r.r;
            Vector3 Iw(Vector2 v) => f.A * v.x + f.D * v.y;
            foreach (int wall in new[] { 0, 1, 2, 3 })
            {
                Vector2 a = wall == 0 ? new Vector2(x.xMin, x.yMin) : wall == 1 ? new Vector2(x.xMax, x.yMax) : wall == 2 ? new Vector2(x.xMin, x.yMax) : new Vector2(x.xMax, x.yMin);
                Vector2 c = wall == 0 ? new Vector2(x.xMax, x.yMin) : wall == 1 ? new Vector2(x.xMin, x.yMax) : wall == 2 ? new Vector2(x.xMin, x.yMin) : new Vector2(x.xMax, x.yMax);
                Vector3 n = Iw(Inward(wall));
                m.Quad(_wallTile, f.P(a.x, a.y, r.y) + n * 0.065f, f.P(c.x, c.y, r.y) + n * 0.065f, f.P(c.x, c.y, r.y + 1.6f) + n * 0.065f, f.P(a.x, a.y, r.y + 1.6f) + n * 0.065f, n);
            }
            var back = Iw(Inward(1));
            Vector3 bc = f.P(x.center.x, x.yMax, r.y);
            var rot = Quaternion.LookRotation(back);
            m.Box(_tankWhite, bc + back * 0.3f - Iw(Vector2.right) * 0.45f + Vector3.up * 0.2f, new Vector3(0.4f, 0.4f, 0.55f), rot);           // toilet
            m.Box(_tankWhite, bc + back * 0.12f - Iw(Vector2.right) * 0.45f + Vector3.up * 0.65f, new Vector3(0.4f, 0.4f, 0.16f), rot);          // cistern
            m.Box(_tankWhite, bc + back * 0.25f + Iw(Vector2.right) * 0.45f + Vector3.up * 0.82f, new Vector3(0.5f, 0.14f, 0.4f), rot);          // basin
            m.Box(_tankWhite, bc + back * 0.18f + Iw(Vector2.right) * 0.45f + Vector3.up * 0.38f, new Vector3(0.16f, 0.76f, 0.16f), rot);        // pedestal
            m.Quad(_glassDark, bc + back * 0.07f + Iw(Vector2.right) * 0.2f + Vector3.up * 1.1f, bc + back * 0.07f + Iw(Vector2.right) * 0.7f + Vector3.up * 1.1f,
                   bc + back * 0.07f + Iw(Vector2.right) * 0.7f + Vector3.up * 1.6f, bc + back * 0.07f + Iw(Vector2.right) * 0.2f + Vector3.up * 1.6f, back);   // mirror
            m.Cylinder(_tankWhite, bc + back * 0.3f + Vector3.up * 1.85f, 0.22f, 0.6f, 14, true, true);                                            // water heater
        }

        /// <summary>The old town's sitting room: mattresses and back cushions round the walls on carpets, a tray on a stool, brass.</summary>
        private static void Majlis(TownMesh m, TownMesh det, Transform props, HouseFrame f, Room r, System.Random own)
        {
            Vector3 Iw(Vector2 v) => f.A * v.x + f.D * v.y;
            var cloth = _cloth[own.Next(_cloth.Length)];
            foreach (int wall in new[] { 1, 2, 3 })
            {
                bool alongA = wall < 2;
                float span = (alongA ? r.r.width : r.r.height) - 0.3f;
                var spot = AgainstWall(r, own, Mathf.Min(span, 3.4f), 0.75f, false, new[] { wall });
                if (spot == null) continue;
                var (at, w) = spot.Value;
                float L = Mathf.Min(span, 3.4f);
                var rot = Quaternion.LookRotation(Iw(Inward(w)));
                Vector3 c = f.P(at.x, at.y, r.y);
                m.Box(cloth, c + Vector3.up * 0.09f, new Vector3(L, 0.18f, 0.72f), rot);                                        // floor mattress
                m.Box(_cloth[own.Next(_cloth.Length)], c - Iw(Inward(w)) * 0.28f + Vector3.up * 0.42f, new Vector3(L, 0.48f, 0.16f), rot);   // back cushion
            }
            Rug(m, f, r, r.r.center, new Vector2(Mathf.Min(3.2f, r.r.width - 1.8f), Mathf.Min(2.4f, r.r.height - 1.8f)), own);
            var tc = r.r.center;
            PlaceTown(props, Prop("wooden_stool_01"), f.P(tc.x, tc.y, r.y), Rand(own, 0f, 360f));
            PlaceTown(props, Prop("carved_wooden_plate"), f.P(tc.x, tc.y, r.y + 0.44f), Rand(own, 0f, 360f), navBlock: false);
            if (own.NextDouble() < 0.6) PlaceTown(props, Prop("tea_set_01"), f.P(tc.x + 0.6f, tc.y, r.y), Rand(own, 0f, 360f), 0.16f, navBlock: false);
            string[] brass = { "brass_pot_01", "brass_vase_01", "vintage_oil_lamp", "brass_diya_lantern", "antique_ceramic_vase_01" };
            for (int i = 0; i < 3; i++) PropAgainstWall(props, f, r, own, brass[own.Next(brass.Length)], 0.35f, 0.35f, false);
        }

        private static void OldKitchen(TownMesh m, Transform props, HouseFrame f, Room r, System.Random own)
        {
            // A plastered bench along a wall with pots on it, a water barrel, baskets.
            var spot = AgainstWall(r, own, 2.2f, 0.6f, false, new[] { 1, 2, 3 }, 0.06f);
            if (spot != null)
            {
                var (at, wall) = spot.Value;
                Vector3 Iw(Vector2 v) => f.A * v.x + f.D * v.y;
                Vector3 c = f.P(at.x, at.y, r.y);
                m.Box(_paintMud, c + Vector3.up * 0.4f, new Vector3(2.2f, 0.8f, 0.6f), Quaternion.LookRotation(Iw(Inward(wall))));
                string[] pots = { "pot_enamel_01", "jug_01", "brass_pot_01", "wooden_bowl_01", "wicker_basket_01" };
                for (int i = 0; i < 3; i++)
                    PlaceTown(props, Prop(pots[own.Next(pots.Length)]), c + Iw(new Vector2(-Inward(wall).y, Inward(wall).x)) * Rand(own, -0.9f, 0.9f) + Vector3.up * 0.8f, Rand(own, 0f, 360f), navBlock: false);
            }
            PropAgainstWall(props, f, r, own, "Barrel_01", 0.6f, 0.6f, false);
            PropAgainstWall(props, f, r, own, "wicker_basket_02", 0.45f, 0.4f, false);
            PropAgainstWall(props, f, r, own, "painted_wooden_shelves", 0.55f, 0.4f, false);
        }

        /// <summary>A rug: a quad of the carpet atlas (one of its three rugs) on the floor.</summary>
        private static void Rug(TownMesh m, HouseFrame f, Room r, Vector2 c, Vector2 size, System.Random own)
        {
            if (size.x < 0.6f || size.y < 0.6f) return;
            if (r.flight.width > 0f && Rect.MinMaxRect(c.x - size.x * 0.5f, c.y - size.y * 0.5f, c.x + size.x * 0.5f, c.y + size.y * 0.5f).Overlaps(r.flight)) return;
            var cells = new[] { new Vector2(0.5f, 0f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f) };
            var u0 = cells[own.Next(cells.Length)];
            float y = r.y + 0.012f;
            m.QuadUV(_interiorCarpet, f.P(c.x - size.x * 0.5f, c.y - size.y * 0.5f, y), f.P(c.x + size.x * 0.5f, c.y - size.y * 0.5f, y),
                     f.P(c.x + size.x * 0.5f, c.y + size.y * 0.5f, y), f.P(c.x - size.x * 0.5f, c.y + size.y * 0.5f, y), Vector3.up,
                     u0, u0 + new Vector2(0.5f, 0f), u0 + new Vector2(0.5f, 0.5f), u0 + new Vector2(0f, 0.5f));
        }

        /// <summary>A picture or two on the walls: a framed piece of a carpet, or a framed sign (a calligraphy panel).</summary>
        private static void Pictures(TownMesh det, HouseFrame f, Room r, System.Random own, bool old)
        {
            Vector3 Iw(Vector2 v) => f.A * v.x + f.D * v.y;
            int n = own.Next(0, 3);
            for (int i = 0; i < n; i++)
            {
                int wall = own.Next(4);
                bool alongA = wall < 2;
                float span = alongA ? r.r.width : r.r.height;
                if (span < 1.4f) continue;
                float s = Rand(own, 0.6f, span - 0.6f);
                Vector2 at = wall == 0 ? new Vector2(r.r.xMin + s, r.r.yMin) : wall == 1 ? new Vector2(r.r.xMin + s, r.r.yMax)
                           : wall == 2 ? new Vector2(r.r.xMin, r.r.yMin + s) : new Vector2(r.r.xMax, r.r.yMin + s);
                if (r.outer[wall] && r.windows.Any(w => w.wall == wall && s + (alongA ? r.r.xMin : r.r.yMin) > w.from - 0.4f && s + (alongA ? r.r.xMin : r.r.yMin) < w.to + 0.4f)) continue;
                Vector3 n2 = Iw(Inward(wall)), along = Iw(new Vector2(-Inward(wall).y, Inward(wall).x));
                Vector3 c = f.P(at.x, at.y, r.y + 1.65f) + n2 * 0.07f;
                float w2 = Rand(own, 0.45f, 0.8f), h2 = w2 * Rand(own, 0.7f, 1.2f);
                Prism(det, old ? _woodBeam : _frameDark, c, along, Vector3.up, n2, w2 + 0.06f, h2 + 0.06f, 0.025f);
                var u0 = new[] { new Vector2(0.5f, 0f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f) }[own.Next(3)] + new Vector2(0.1f, 0.1f);
                det.QuadUV(_interiorCarpet, c - along * (w2 * 0.5f) - Vector3.up * (h2 * 0.5f) + n2 * 0.002f, c + along * (w2 * 0.5f) - Vector3.up * (h2 * 0.5f) + n2 * 0.002f,
                           c + along * (w2 * 0.5f) + Vector3.up * (h2 * 0.5f) + n2 * 0.002f, c - along * (w2 * 0.5f) + Vector3.up * (h2 * 0.5f) + n2 * 0.002f, n2,
                           u0, u0 + new Vector2(0.3f, 0f), u0 + new Vector2(0.3f, 0.3f), u0 + new Vector2(0f, 0.3f));
            }
        }
    }
}
#endif
