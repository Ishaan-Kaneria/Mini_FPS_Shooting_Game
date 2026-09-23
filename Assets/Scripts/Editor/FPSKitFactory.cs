#if UNITY_EDITOR
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The big structures the industrial zone is built out of: production halls,
    /// chimneys, silo banks, cooling towers, pipe bridges and gantry cranes.
    ///
    /// <b>Why these had to be built rather than bought.</b> The art pack's biggest
    /// building is a hangar about twenty-five metres long and seven high, and the plant
    /// is five hundred metres square. Filling it with hangars gives a site where nothing
    /// is taller than a lamp post: from the ground the whole arena is one flat horizon
    /// with small sheds dotted over it, and from the air it is a car park with a grid
    /// painted on it. That is the entire difference between "an industrial level" and
    /// "an empty plane with industrial props on it", and it is a question of <i>mass</i>
    /// -- a real works has a sixty-metre shed you cannot see past, a stack you can
    /// navigate the whole site by, and a pipe bridge over your head.
    ///
    /// Three rules hold these together, and all three are the same rule the rest of the
    /// kit follows:
    ///
    ///   * <b>Anything with a flat top and no way up is kept off the navigation bake.</b>
    ///     A silo cap, a cooling tower rim, a pipe-bridge deck and a roof monitor are all
    ///     shallower than the agent slope, so each one bakes as an island for the spawner
    ///     to put an enemy on. The one deliberate exception is a hall roof, which is
    ///     reached by its own stair and is meant to be fought over.
    ///   * <b>Anything a player can walk into has two ways out.</b> A hall has a roller
    ///     door at each end and a personnel door in a side wall, so an enemy that walks
    ///     in is never sealed in -- which is the failure this arena had, and it is
    ///     invisible, because a trapped enemy neither errors nor moves.
    ///   * <b>Everything long is one welded mesh.</b> A gantry crane as forty boxes is
    ///     forty draw calls; as one mesh it is one, and this game has to fit in a
    ///     browser tab.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        // ==================================================================
        // The production hall
        // ==================================================================
        /// <summary>The height a hall's roof deck sits at. Everything else is measured off it.</summary>
        private const float HallEaves = 12f;

        /// <summary>How high the parapet stands over the deck. Chest height, because it is cover.</summary>
        private const float HallParapet = 1.25f;

        /// <summary>
        /// A production hall: the biggest building on the site, hollow, with a walkable
        /// roof.
        ///
        /// <b>Flat-roofed with monitors rather than gabled, and that is a gameplay
        /// choice before it is a visual one.</b> A pitched roof is scenery -- it has to
        /// be held off the bake or it grows an island, and nothing can ever stand on it.
        /// A flat deck with a parapet round it and one external stair is the best firing
        /// position in the district: it overlooks two streets, it has chest-high cover on
        /// every side, and it can only be taken by somebody who commits to a stair. The
        /// rooftop plant -- monitors, cowls, ducts -- is what keeps the silhouette from
        /// being a shoebox, and it doubles as cover up there.
        ///
        /// <para>
        /// Returned as the transform it was built under, so the caller can hang the
        /// district's own clutter off it.
        /// </para>
        /// </summary>
        private static Transform BuildFactoryHall(Transform parent, int layer, System.Random rng,
                                                  Vector3 at, float width, float depth, float yaw)
        {
            var hall = new GameObject("FactoryHall").transform;
            hall.SetParent(parent, false);
            hall.localPosition = at;
            hall.localRotation = Quaternion.Euler(0f, yaw, 0f);

            float hw = width * 0.5f, hd = depth * 0.5f;
            const float wall = 0.55f;

            // ---- shell ----
            var shell = new MeshBuild { UVScale = 0.14f };

            // The gable ends carry the vehicle doors, because that is the end a lorry
            // reverses into; the long walls carry a personnel door each, which is what
            // stops the building being a cul-de-sac with one entrance.
            float bigDoor = Mathf.Min(11f, depth * 0.34f);
            float sideDoor = 4.2f;

            WallRun(shell, alongX: false, at: -hw, from: -hd, to: hd, height: HallEaves,
                    thickness: wall, doorAt: 0f, doorWidth: bigDoor, doorHeight: 7.2f);
            WallRun(shell, alongX: false, at: hw, from: -hd, to: hd, height: HallEaves,
                    thickness: wall, doorAt: 0f, doorWidth: bigDoor, doorHeight: 7.2f);

            WallRun(shell, alongX: true, at: -hd, from: -hw, to: hw, height: HallEaves,
                    thickness: wall, doorAt: -width * 0.26f, doorWidth: sideDoor, doorHeight: 4.6f);
            WallRun(shell, alongX: true, at: hd, from: -hw, to: hw, height: HallEaves,
                    thickness: wall, doorAt: width * 0.26f, doorWidth: sideDoor, doorHeight: 4.6f);

            // Pilasters. A blank sixty-metre wall has no scale in it at all -- ribs every
            // six metres are what tell the eye how big the building is from across the
            // site, and they are on every steel-clad shed ever built.
            for (float x = -hw + 3f; x <= hw - 3f; x += 6f)
            {
                shell.Box(new Vector3(x, HallEaves * 0.5f, -hd - 0.25f),
                          new Vector3(0.5f, HallEaves, 0.45f), Quaternion.identity);
                shell.Box(new Vector3(x, HallEaves * 0.5f, hd + 0.25f),
                          new Vector3(0.5f, HallEaves, 0.45f), Quaternion.identity);
            }

            var walls = MeshObject(hall, "Shell", ToMesh(shell, $"hallshell{width:0}x{depth:0}"),
                                   _zoneCladding, Vector3.zero, Quaternion.identity, Vector3.one,
                                   layer, "Metal");
            if (walls != null) Mark(walls, new Color(0.46f, 0.47f, 0.50f), 2);

            // ---- roof deck and parapet ----
            //
            // Two stairs, on opposite flanks at opposite ends, and the parapet leaves a
            // gap at each. Two rather than one for two reasons: a roof this good has to
            // be takeable from somebody holding it, and -- the reason it is two and not
            // one -- a hall stands on a street frontage, so one flank of it is always
            // liable to land its stair in something already built there. One blocked
            // stair with no second is a roof nothing can reach, and the only symptom is
            // a firing position the enemies never contest.
            float gapAt = -width * 0.26f;
            float farGapAt = width * 0.26f;

            var deck = new MeshBuild { UVScale = 0.2f };
            deck.Box(new Vector3(0f, HallEaves + 0.15f, 0f),
                     new Vector3(width, 0.3f, depth), Quaternion.identity);

            ParapetRun(deck, alongX: true, at: -hd + 0.3f, from: -hw, to: hw,
                       gapAt: gapAt, gapWidth: 3.2f);
            ParapetRun(deck, alongX: true, at: hd - 0.3f, from: -hw, to: hw,
                       gapAt: farGapAt, gapWidth: 3.2f);
            ParapetRun(deck, alongX: false, at: -hw + 0.3f, from: -hd, to: hd,
                       gapAt: float.NaN, gapWidth: 0f);
            ParapetRun(deck, alongX: false, at: hw - 0.3f, from: -hd, to: hd,
                       gapAt: float.NaN, gapWidth: 0f);

            MeshObject(hall, "Roof", ToMesh(deck, $"halldeck{width:0}x{depth:0}"), _zonePlate,
                       Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");

            // ---- rooftop plant ----
            var plant = new MeshBuild { UVScale = 0.3f };
            float top = HallEaves + 0.3f;

            int monitors = Mathf.Clamp(Mathf.FloorToInt(depth / 13f), 1, 3);
            for (int i = 0; i < monitors; i++)
            {
                float z = Mathf.Lerp(-hd * 0.55f, hd * 0.55f, monitors == 1 ? 0.5f : i / (float)(monitors - 1));

                plant.Box(new Vector3(0f, top + 1.9f, z),
                          new Vector3(width - 6f, 3.8f, 4.4f), Quaternion.identity);

                // The ridge cap, set back, so a monitor is two masses rather than a slab.
                plant.Box(new Vector3(0f, top + 4.1f, z),
                          new Vector3(width - 5f, 0.5f, 5.2f), Quaternion.identity);
            }

            int cowls = Mathf.Clamp(Mathf.RoundToInt(width / 11f), 2, 6);
            for (int i = 0; i < cowls; i++)
            {
                float x = Mathf.Lerp(-hw + 5f, hw - 5f, cowls == 1 ? 0.5f : i / (float)(cowls - 1));
                float z = hd * Rand(rng, -0.8f, 0.8f);

                plant.Tube(new Vector3(x, top, z), new Vector3(x, top + 2.3f, z), 0.8f, 0.8f, 10);
                plant.Box(new Vector3(x, top + 2.7f, z), new Vector3(2.2f, 0.7f, 2.2f),
                          Quaternion.identity);
            }

            var plantGo = MeshObject(hall, "RoofPlant", ToMesh(plant, $"hallplant{width:0}x{depth:0}"),
                                     _zoneSteel, Vector3.zero, Quaternion.identity, Vector3.one,
                                     layer, "Metal");
            NoStanding(plantGo);

            // ---- the ways up ----
            BuildRoofStair(hall, layer, hd, -1, gapAt, -1, HallEaves + 0.3f);
            BuildRoofStair(hall, layer, hd, 1, farGapAt, 1, HallEaves + 0.3f);

            // ---- inside ----
            //
            // Columns down the middle. A sixty-metre shed with nothing in it is a
            // corridor, and a column line is what a real one has anyway.
            var frame = new MeshBuild { UVScale = 0.35f };
            for (float x = -hw + 8f; x <= hw - 8f; x += 11f)
            {
                frame.Box(new Vector3(x, HallEaves * 0.5f, 0f), new Vector3(0.7f, HallEaves, 0.7f),
                          Quaternion.identity);
                frame.Box(new Vector3(x, HallEaves - 0.6f, 0f), new Vector3(1.6f, 1.2f, 1.6f),
                          Quaternion.identity);
            }

            MeshObject(hall, "Columns", ToMesh(frame, $"hallcols{width:0}"), _zoneSteel,
                       Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");

            // Daylight. A roofed building with no opening in the roof is black inside,
            // and a dark room is one an enemy can stand in unseen -- the same complaint
            // the desert's ambient was raised for.
            for (int i = 0; i < 2; i++)
            {
                var lamp = new GameObject("HallLight");
                lamp.transform.SetParent(hall, false);
                lamp.transform.localPosition = new Vector3(width * (i == 0 ? -0.24f : 0.24f),
                                                           HallEaves - 2.2f, 0f);

                var light = lamp.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = Mathf.Max(width, depth) * 0.62f;
                light.intensity = 2.4f;
                light.color = new Color(1f, 0.95f, 0.85f);
                light.shadows = LightShadows.None;
            }

            return hall;
        }

        /// <summary>
        /// One wall of a hall, in segments, with a doorway left in it.
        ///
        /// The doorway is the point. A hollow building whose walls are four unbroken
        /// boxes is a solid block with a hole in the navmesh inside it: nothing can get
        /// in, and anything that starts in there stands in it for the whole level.
        /// </summary>
        private static void WallRun(MeshBuild build, bool alongX, float at, float from, float to,
                                    float height, float thickness, float doorAt, float doorWidth,
                                    float doorHeight)
        {
            void Panel(float a, float b, float y0, float y1)
            {
                if (b - a < 0.05f || y1 - y0 < 0.05f) return;

                Vector3 centre = alongX
                    ? new Vector3((a + b) * 0.5f, (y0 + y1) * 0.5f, at)
                    : new Vector3(at, (y0 + y1) * 0.5f, (a + b) * 0.5f);

                Vector3 size = alongX
                    ? new Vector3(b - a, y1 - y0, thickness)
                    : new Vector3(thickness, y1 - y0, b - a);

                build.Box(centre, size, Quaternion.identity);
            }

            float d0 = doorAt - doorWidth * 0.5f;
            float d1 = doorAt + doorWidth * 0.5f;

            if (doorWidth <= 0f || d0 <= from || d1 >= to)
            {
                Panel(from, to, 0f, height);
                return;
            }

            Panel(from, d0, 0f, height);
            Panel(d1, to, 0f, height);
            Panel(d0, d1, doorHeight, height);
        }

        /// <summary>One side of a roof parapet, optionally with the stair's gap in it.</summary>
        private static void ParapetRun(MeshBuild build, bool alongX, float at, float from, float to,
                                       float gapAt, float gapWidth)
        {
            void Run(float a, float b)
            {
                if (b - a < 0.2f) return;

                Vector3 centre = alongX
                    ? new Vector3((a + b) * 0.5f, HallEaves + 0.3f + HallParapet * 0.5f, at)
                    : new Vector3(at, HallEaves + 0.3f + HallParapet * 0.5f, (a + b) * 0.5f);

                Vector3 size = alongX
                    ? new Vector3(b - a, HallParapet, 0.4f)
                    : new Vector3(0.4f, HallParapet, b - a);

                build.Box(centre, size, Quaternion.identity);
            }

            if (float.IsNaN(gapAt) || gapWidth <= 0f) { Run(from, to); return; }

            Run(from, gapAt - gapWidth * 0.5f);
            Run(gapAt + gapWidth * 0.5f, to);
        }

        /// <summary>
        /// A flight up a hall's flank to the roof, laid <b>along</b> the wall rather than
        /// out from it, with a landing that turns onto the deck.
        ///
        /// <b>The direction is the whole design and it was wrong the first time.</b> A
        /// twelve-metre climb at a walkable pitch is nineteen metres of flight; run
        /// straight out from the wall that is a staircase reaching twenty-two metres into
        /// the street, through whatever the district put there and past the edge of the
        /// block. Turned to hug the wall it projects three metres, uses twenty of a
        /// sixty-metre elevation that had nothing on it anyway, and is what an external
        /// stair on a real shed looks like.
        ///
        /// <para>
        /// The landing <b>overlaps the roof deck</b> rather than stopping level with its
        /// edge. Two surfaces at the same height with a hand's breadth of air between
        /// them are two surfaces as far as the bake is concerned -- the voxels never
        /// join, the roof stays an island, and from the ground the stair and the roof
        /// look exactly like a stair and a roof.
        /// </para>
        ///
        /// <b>The treads are 0.22 m and that number is load-bearing.</b> A NavMeshAgent
        /// will climb a stair whose steps are shorter than its step height and will
        /// refuse one whose steps are taller, so a handsome 0.3 m stair is a roof the
        /// player can hold against nobody.
        /// </summary>
        /// <param name="halfDepth">Half the hall's depth: which wall the stair runs beside.</param>
        /// <param name="side">-1 for the -Z wall, +1 for the +Z wall.</param>
        /// <param name="landAtX">Where the landing meets the roof, and where the parapet gap is.</param>
        /// <param name="direction">Which way along the wall the flight climbs.</param>
        private static void BuildRoofStair(Transform parent, int layer, float halfDepth, int side,
                                           float landAtX, int direction, float top)
        {
            var build = new MeshBuild { UVScale = 0.5f };

            const float rise = 0.22f;
            const float tread = 0.34f;
            const float wide = 2.4f;

            int steps = Mathf.Max(4, Mathf.RoundToInt(top / rise));
            float run = steps * tread;

            float z = side * (halfDepth + wide * 0.5f + 0.5f);

            // The top step has to land <b>under</b> the landing, not alongside it. Set so
            // the flight stops a metre and a half short and the landing covers the rest:
            // a ten-centimetre gap between the two is invisible from every angle and is
            // wider than a navmesh voxel, so the flight bakes as one island and the roof
            // as another and nothing can walk from one to the other.
            float x0 = landAtX + direction * (run + 1.1f);

            for (int i = 0; i < steps; i++)
            {
                float y = (i + 1) * rise;
                float x = x0 - direction * (i + 0.5f) * tread;

                build.Box(new Vector3(x, y * 0.5f, z), new Vector3(tread, y, wide),
                          Quaternion.identity);
            }

            // The landing, reaching from beside the wall to a metre and a half in over
            // the deck. One box, because the overlap is the part that matters.
            float inner = side * (halfDepth - 1.5f);
            float outer = side * (halfDepth + wide + 0.9f);

            build.Box(new Vector3(landAtX, top - 0.12f, (inner + outer) * 0.5f),
                      new Vector3(3.6f, 0.24f, Mathf.Abs(outer - inner)), Quaternion.identity);

            // Railings on the outer edge of the flight and round the landing. The wall
            // side needs none: the wall is the railing.
            float railZ = z + side * (wide * 0.5f - 0.08f);

            for (int i = 0; i < steps; i += 3)
            {
                float y = (i + 2) * rise + 1.05f;
                float x = x0 - direction * (i + 1.5f) * tread;

                build.Box(new Vector3(x, y, railZ), new Vector3(tread * 3.3f, 0.08f, 0.08f),
                          Quaternion.identity);
                build.Box(new Vector3(x, y - 0.55f, railZ), new Vector3(0.07f, 1.1f, 0.07f),
                          Quaternion.identity);
            }

            // On the far side of the landing from the flight. Put it on the near side
            // and it is a metre of handrail standing exactly where the top step meets the
            // landing: the stair still looks finished, and nothing can walk up it.
            build.Box(new Vector3(landAtX - direction * 1.55f, top + 0.55f,
                                  (inner + outer) * 0.5f),
                      new Vector3(0.08f, 1.1f, Mathf.Abs(outer - inner)), Quaternion.identity);

            // <b>Keyed by every number that shapes it, and that includes where it lands.</b>
            // The pool hands back a mesh already built for the same key, so a key of
            // "steps, side, direction" gives the second hall on the site the first hall's
            // staircase -- landing and all, at the first hall's landAtX. On a hall of a
            // different width that puts the landing beside the parapet gap instead of in
            // it, and the roof is unreachable on every building but the first one. The
            // same trap BuildStairTower's own comment warns about, walked into anyway.
            MeshObject(parent, "RoofStair",
                       ToMesh(build, $"roofstair{steps}_{side}_{direction}_{landAtX:0.0}_{halfDepth:0.0}"),
                       _zoneSteel, Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");
        }

        // ==================================================================
        // The skyline pieces
        // ==================================================================
        /// <summary>
        /// A chimney: a tapered stack with iron bands, a cap and a flue duct into the
        /// building beside it.
        ///
        /// <b>This is the single most valuable object on the site per triangle.</b> A
        /// forty-metre stack is visible from everywhere in a five-hundred-metre arena, so
        /// it is a landmark the player navigates by -- "the works with the chimney" is a
        /// place, and "the third shed on the left" is not. It is also the one shape that
        /// says heavy industry rather than warehousing from a kilometre away, which is
        /// why the horizon is full of them too.
        /// </summary>
        private static void BuildChimney(Transform parent, int layer, Vector3 at, float height,
                                         float radius, float yawToBuilding)
        {
            // Tiled small on purpose. The brick comes off the pack's oil-tank map, which
            // has hatches and manholes in it: at six metres to the tile those come out as
            // metre-wide ovals up the side of the stack, which read as portholes. At two
            // they are mottling, which is what brick at forty metres looks like anyway.
            var build = new MeshBuild { UVScale = 0.5f };

            // The plinth, which is what stops a stack looking like a pipe standing on
            // tarmac.
            build.Box(new Vector3(0f, 1.6f, 0f), new Vector3(radius * 3.4f, 3.2f, radius * 3.4f),
                      Quaternion.identity);

            build.Tube(new Vector3(0f, 2.4f, 0f), new Vector3(0f, height, 0f),
                       radius, radius * 0.46f, 16);

            // Bands, each a slightly fatter ring at its own height.
            for (int i = 1; i <= 4; i++)
            {
                float t = i / 5f;
                float y = Mathf.Lerp(4f, height - 3f, t);
                float r = Mathf.Lerp(radius, radius * 0.46f, Mathf.InverseLerp(2.4f, height, y));

                build.Tube(new Vector3(0f, y - 0.35f, 0f), new Vector3(0f, y + 0.35f, 0f),
                           r * 1.12f, r * 1.12f, 16);
            }

            // The cap, and a ladder up one side -- decoration, but it is what the eye
            // measures the diameter against.
            float topR = radius * 0.46f;
            build.Tube(new Vector3(0f, height, 0f), new Vector3(0f, height + 0.9f, 0f),
                       topR * 1.2f, topR * 1.15f, 16);

            for (float y = 4f; y < height - 1f; y += 1.4f)
                build.Box(new Vector3(radius * 0.95f, y, 0f), new Vector3(0.7f, 0.09f, 0.09f),
                          Quaternion.identity);

            // The flue, running off towards whatever is burning.
            build.Box(new Vector3(radius * 1.2f + 3f, 4.2f, 0f), new Vector3(7f, 2.4f, 2.4f),
                      Quaternion.identity);

            var go = MeshObject(parent, "Chimney", ToMesh(build, $"chimney{height:0}_{radius:0.0}"),
                                _zoneBrick, at, Quaternion.Euler(0f, yawToBuilding, 0f),
                                Vector3.one, layer, "Concrete");

            NoStanding(go);
            if (go != null) Mark(go, new Color(0.40f, 0.26f, 0.20f), 4);

            // A stack that is not smoking is a monument. Seeded off where it stands, so a
            // rebuild writes the same scene.
            Plume(parent, at + Vector3.up * (height + 1f), radius * 0.5f, Mathf.Clamp(height / 40f, 0.7f, 1.3f),
                  Mathf.RoundToInt(at.x * 31f + at.z * 17f), steam: true);
        }

        /// <summary>
        /// A cooling tower: the hyperboloid shell on its ring of legs.
        ///
        /// Built as a lathe rather than as a cylinder because the waist is the whole
        /// shape -- a straight drum of the same height reads as a silo, and the curve is
        /// the reason a cooling tower is recognisable in silhouette to people who have
        /// never been near a power station.
        /// </summary>
        private static void BuildCoolingTower(Transform parent, int layer, Vector3 at,
                                              float height, float radius)
        {
            var build = new MeshBuild { UVScale = 0.1f };

            const int rings = 14;
            const int sides = 24;

            float Profile(float t)
            {
                // Waist at two thirds up, flaring below it and a little above.
                float d = (t - 0.68f) / 0.68f;
                return radius * (0.56f + d * d * 0.62f);
            }

            float legs = height * 0.12f;

            for (int i = 0; i < rings; i++)
            {
                float t0 = i / (float)rings, t1 = (i + 1) / (float)rings;
                build.Tube(new Vector3(0f, legs + t0 * (height - legs), 0f),
                           new Vector3(0f, legs + t1 * (height - legs), 0f),
                           Profile(t0), Profile(t1), sides);
            }

            // The legs, and the plinth ring they stand on.
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                var foot = new Vector3(Mathf.Cos(a) * Profile(0f) * 1.12f, 0f,
                                       Mathf.Sin(a) * Profile(0f) * 1.12f);
                var head = new Vector3(Mathf.Cos(a) * Profile(0f), legs,
                                       Mathf.Sin(a) * Profile(0f));
                build.Tube(foot, head, 0.55f, 0.55f, 6);
            }

            build.Tube(new Vector3(0f, 0f, 0f), new Vector3(0f, 0.8f, 0f),
                       Profile(0f) * 1.2f, Profile(0f) * 1.2f, sides);

            // The panel concrete at one repeat per ten metres, not the perimeter wall's
            // material: that one is tiled sixty times across for a five-hundred-metre wall,
            // and wrapped round a tower it came out as fine grey streaks with a sheen.
            var go = MeshObject(parent, "CoolingTower",
                                ToMesh(build, $"cooler{height:0}_{radius:0}"), _denseConcrete ?? _zoneConcrete,
                                at, Quaternion.identity, Vector3.one, layer, "Concrete");

            NoStanding(go);
            if (go != null) Mark(go, new Color(0.58f, 0.58f, 0.56f), 4);

            // The steam is the landmark as much as the tower is: a white column you can
            // find the power house by from anywhere on the site.
            Plume(parent, at + Vector3.up * (height - 2f), radius * 0.45f, 2.2f,
                  Mathf.RoundToInt(at.x * 13f + at.z * 7f), steam: true);
        }

        /// <summary>
        /// A bank of silos: cylinders shoulder to shoulder under one headhouse, with the
        /// elevator leg at one end.
        ///
        /// A row of them is worth far more than the same silos scattered, because what
        /// makes a bank read as plant is the repetition -- identical vessels in a line
        /// is a thing only industry builds.
        /// </summary>
        private static void BuildSiloBank(Transform parent, int layer, Vector3 at, int count,
                                          float radius, float height, float yaw)
        {
            var build = new MeshBuild { UVScale = 0.13f };

            float span = (count - 1) * radius * 2f;

            for (int i = 0; i < count; i++)
            {
                float x = -span * 0.5f + i * radius * 2f;

                build.Tube(new Vector3(x, 0f, 0f), new Vector3(x, height, 0f), radius, radius, 16);
                build.Tube(new Vector3(x, height, 0f), new Vector3(x, height + radius * 0.8f, 0f),
                           radius, radius * 0.18f, 16);

                // A discharge cone under it, standing the vessel off the ground the way a
                // real one is -- a silo sitting flat on tarmac is a grain bin, not plant.
                build.Box(new Vector3(x, 1.1f, 0f), new Vector3(radius * 1.5f, 2.2f, radius * 1.5f),
                          Quaternion.identity);
            }

            // The headhouse and the gantry along the tops.
            float head = height + radius * 0.8f;
            build.Box(new Vector3(0f, head + 1.4f, 0f), new Vector3(span + radius * 2f, 2.8f, 3f),
                      Quaternion.identity);

            float legX = -span * 0.5f - radius * 1.6f;
            build.Box(new Vector3(legX, (head + 6f) * 0.5f, 0f),
                      new Vector3(3.4f, head + 6f, 3.4f), Quaternion.identity);
            build.Box(new Vector3(legX, head + 7f, 0f), new Vector3(4.2f, 2.6f, 4.2f),
                      Quaternion.identity);

            var go = MeshObject(parent, "SiloBank",
                                ToMesh(build, $"silos{count}_{radius:0.0}_{height:0}"), _zonePlate,
                                at, Quaternion.Euler(0f, yaw, 0f), Vector3.one, layer, "Metal");

            NoStanding(go);
            if (go != null) Mark(go, new Color(0.60f, 0.60f, 0.62f), 3);
        }

        // ==================================================================
        // The things that cross the site
        // ==================================================================
        /// <summary>
        /// A pipe bridge: a rack of pipes on portal legs, carried over a street.
        ///
        /// <b>This is the one structure the player spends the whole level underneath.</b>
        /// A site laid out on streets has long straight views down every one of them, and
        /// nothing between the ground and the sky; something crossing overhead at ten
        /// metres breaks that view, throws a shadow across the carriageway, and gives the
        /// street a ceiling -- which is most of the difference between walking through a
        /// plant and walking across a car park.
        ///
        /// Scenic on purpose: it is held off the bake, and the catwalks are what a player
        /// is meant to get onto.
        /// </summary>
        private static void BuildPipeBridge(Transform parent, int layer, Vector3 from, Vector3 to,
                                            float height)
        {
            Vector3 delta = to - from;
            float length = new Vector2(delta.x, delta.z).magnitude;
            if (length < 10f) return;

            float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;

            var build = new MeshBuild { UVScale = 0.4f };
            const float wide = 3.6f;

            // Portal legs -- but none on a carriageway. A bridge between two districts
            // spans the street between them, and a leg every sixteen metres lands one in
            // the road more often than not; on a road ten metres wide it blocks a lane.
            int bays = Mathf.Max(2, Mathf.RoundToInt(length / 16f));
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var middle = (from + to) * 0.5f;

            for (int i = 0; i <= bays; i++)
            {
                float z = -length * 0.5f + length * i / bays;

                bool inRoad = false;
                for (int side = -1; side <= 1; side += 2)
                    if (OnCarriageway(middle + rotation * new Vector3(side * wide * 0.5f, 0f, z))) inRoad = true;
                if (inRoad) continue;

                for (int side = -1; side <= 1; side += 2)
                    build.Box(new Vector3(side * wide * 0.5f, height * 0.5f, z),
                              new Vector3(0.6f, height, 0.6f), Quaternion.identity);

                build.Box(new Vector3(0f, height + 0.4f, z), new Vector3(wide + 1.2f, 0.7f, 0.7f),
                          Quaternion.identity);

                // Knee braces, which is what stops a portal frame reading as two posts.
                for (int side = -1; side <= 1; side += 2)
                    build.Box(new Vector3(side * (wide * 0.5f - 0.9f), height - 1.1f, z),
                              new Vector3(2.6f, 0.4f, 0.4f),
                              Quaternion.Euler(0f, 0f, side * 40f));
            }

            // Chords along the run, and the pipes on top of them.
            for (int side = -1; side <= 1; side += 2)
                build.Box(new Vector3(side * wide * 0.5f, height, 0f),
                          new Vector3(0.45f, 0.45f, length), Quaternion.identity);

            float[] offsets = { -1.25f, -0.35f, 0.55f, 1.35f };
            float[] radii = { 0.55f, 0.38f, 0.44f, 0.28f };

            for (int i = 0; i < offsets.Length; i++)
                build.Tube(new Vector3(offsets[i], height + 0.6f + radii[i], -length * 0.5f),
                           new Vector3(offsets[i], height + 0.6f + radii[i], length * 0.5f),
                           radii[i], radii[i], 10);

            var go = MeshObject(parent, "PipeBridge",
                                ToMesh(build, $"pipebridge{length:0}_{height:0}"), _zoneSteel,
                                (from + to) * 0.5f, Quaternion.Euler(0f, yaw, 0f), Vector3.one,
                                layer, "Metal");

            NoStanding(go);
            if (go != null) Hide(go);
        }

        // ==================================================================
        // What the ground is wearing
        // ==================================================================
        /// <summary>
        /// Kerbs, road markings, spills and worn patches over the whole site.
        ///
        /// <b>A uniform surface is the loudest artificial thing in an outdoor level, and
        /// it is the hardest to name.</b> The plant's yard is one tint of one texture
        /// over a quarter of a million square metres: from the ground it is a pale plain
        /// with no near detail at all, so the eye has nothing to judge speed or distance
        /// against and the site reads as a car park however many sheds are on it. Nothing
        /// about that looks like a bug -- every individual thing in the arena is correct
        /// -- and the only way to fix it is to put the things a working surface actually
        /// has on it.
        ///
        /// Four of them, in the order they are worth having:
        ///
        ///   * <b>kerbs</b>, which draw the street plan at eye level rather than only
        ///     from the air -- a road you can see the edge of is a road;
        ///   * <b>worn patches</b>, where hardstanding has broken back to dirt, which is
        ///     the one thing that breaks the flat tint over large areas;
        ///   * <b>spills</b>, small and dark, which give the near ground a texture at the
        ///     scale a player is actually looking at it;
        ///   * <b>markings</b> -- lane lines, laydown bays, hatching at the junctions --
        ///     which are what say somebody organises this place.
        ///
        /// All four are drawn as flat geometry with no collider, and they are kept off
        /// the navigation bake <b>by layer</b> rather than by a NavMeshModifier. That
        /// distinction cost a rebuild and is worth writing down: a modifier does not
        /// exclude geometry, it marks it <i>Not Walkable</i>, so a kerb round every road
        /// tile is an unwalkable line drawn between every carriageway and every block.
        /// Two thirds of the site came back severed from the other third -- an arena
        /// where most enemies could not reach the player from where they spawned, and
        /// where the only symptom was a level that quietly never ended. The backdrop
        /// layer is the one the NavMeshSurface already excludes, which is what the apron
        /// uses and is the right answer for anything that is drawn and is not there.
        /// </summary>
        private static void BuildYardDetail(Transform root, int layer, System.Random rng, float half)
        {
            var group = new GameObject("YardDetail").transform;
            group.SetParent(root, false);

            var kerbs = new MeshBuild { UVScale = 0.3f };
            var paint = new MeshBuild { UVScale = 0.5f };
            var spills = new MeshBuild { UVScale = 0.12f };
            var worn = new MeshBuild { UVScale = 0.06f };

            float edge = Tile * 0.5f;

            foreach (var tile in _roadTiles)
            {
                var centre = new Vector3(tile.x * Tile, 0f, tile.y * Tile);

                bool n = _roadTiles.Contains(tile + Vector2Int.up);
                bool s = _roadTiles.Contains(tile + Vector2Int.down);
                bool e = _roadTiles.Contains(tile + Vector2Int.right);
                bool w = _roadTiles.Contains(tile + Vector2Int.left);

                // A kerb on every side that is not more road. Built per tile rather than
                // per run, so a junction loses its kerbs automatically instead of having
                // them laid across the carriageway.
                //
                // Along the ten-metre carriageway, not the twenty-metre tile: see CarriageHalf.
                RoadKerbs(kerbs, centre, n, s, e, w);

                int neighbours = (n ? 1 : 0) + (s ? 1 : 0) + (e ? 1 : 0) + (w ? 1 : 0);

                if (neighbours >= 3)
                {
                    // Hatching, at some junctions and not all -- and kept to the middle
                    // of the box rather than run corner to corner. Laid across the whole
                    // tile at close spacing it covers the junction completely, and a
                    // player standing on one is looking at nothing but paint.
                    if (rng.NextDouble() > 0.4f) continue;

                    for (float o = -CarriageHalf * 0.6f; o < CarriageHalf * 0.6f; o += 2.6f)
                        Paint(paint, centre + new Vector3(o, 0f, 0f), 0.3f, CarriageHalf * 1.3f, 45f);
                }
                else
                {
                    // A dashed lane line down the middle of the carriageway.
                    bool runsX = e || w;
                    for (float o = -edge + 2f; o < edge - 1f; o += 5f)
                        Paint(paint,
                              centre + (runsX ? new Vector3(o, 0f, 0f) : new Vector3(0f, 0f, o)),
                              0.18f, 3f, runsX ? 90f : 0f);
                }
            }

            // Laydown bays: rows of parallel lines on the hardstanding, which is what a
            // yard is marked out into and reads as organised space from any height.
            foreach (var block in _blocks)
            {
                var centre = new Vector3((block.xMin + block.xMax - 1) * 0.5f * Tile, 0f,
                                         (block.yMin + block.yMax - 1) * 0.5f * Tile);

                if (rng.NextDouble() > 0.55) continue;

                float yaw = rng.Next(2) == 0 ? 0f : 90f;
                float run = Mathf.Min(block.width, block.height) * Tile * 0.34f;
                var origin = centre + new Vector3(Rand(rng, -14f, 14f), 0f, Rand(rng, -14f, 14f));

                for (int i = 0; i < 7; i++)
                {
                    float o = -run * 0.5f + run * i / 6f;
                    var offset = yaw == 0f ? new Vector3(o, 0f, 0f) : new Vector3(0f, 0f, o);
                    Paint(paint, origin + offset, 0.14f, 9f, yaw);
                }
            }

            // Spills and worn ground. Placed anywhere but the spawn, which should read as
            // clean ground so the player can tell where they started.
            int spillCount = Mathf.RoundToInt(half * half / 900f);

            for (int i = 0; i < spillCount; i++)
            {
                var at = new Vector3(Rand(rng, -half * 0.94f, half * 0.94f), 0.05f,
                                     Rand(rng, -half * 0.94f, half * 0.94f));
                if (at.magnitude < 24f) continue;

                Splash(spills, rng, at, Rand(rng, 0.7f, 2.6f));
            }

            int wornCount = Mathf.RoundToInt(half * half / 2600f);

            for (int i = 0; i < wornCount; i++)
            {
                var at = new Vector3(Rand(rng, -half * 0.94f, half * 0.94f), 0.035f,
                                     Rand(rng, -half * 0.94f, half * 0.94f));
                if (at.magnitude < 30f) continue;

                Splash(worn, rng, at, Rand(rng, 3.5f, 11f));
            }

            int paintLayer = LayerMask.NameToLayer("Backdrop");
            if (paintLayer < 0) paintLayer = layer;

            Flat(group, paintLayer, "Kerbs", kerbs, "zonekerbs", _zoneCladding);
            Flat(group, paintLayer, "Markings", paint, "zonepaint", _zoneLine);
            Flat(group, paintLayer, "Spills", spills, "zonespills", _zoneStain);
            Flat(group, paintLayer, "WornGround", worn, "zoneworn", _zoneDirt);
        }

        /// <summary>One painted stripe, lying on the ground.</summary>
        private static void Paint(MeshBuild build, Vector3 at, float wide, float run, float yaw)
        {
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            float y = 0.08f;

            Vector3 P(float a, float b) => at + new Vector3(0f, y - at.y, 0f)
                                            + rotation * new Vector3(a * wide * 0.5f, 0f, b * run * 0.5f);

            build.Quad(P(-1f, -1f), P(-1f, 1f), P(1f, 1f), P(1f, -1f));
        }

        /// <summary>
        /// An irregular blot on the ground: a fan of triangles round a centre with a
        /// noisy radius. A circle would read as a decal somebody stamped; a stain has no
        /// two sides the same.
        /// </summary>
        private static void Splash(MeshBuild build, System.Random rng, Vector3 at, float radius)
        {
            const int sides = 11;
            var radii = new float[sides];
            for (int i = 0; i < sides; i++) radii[i] = radius * Rand(rng, 0.55f, 1f);

            Vector3 Rim(int i)
            {
                float a = i * Mathf.PI * 2f / sides;
                return at + new Vector3(Mathf.Cos(a) * radii[i % sides], 0f,
                                        Mathf.Sin(a) * radii[i % sides]);
            }

            for (int i = 0; i < sides; i++)
                build.Tri(at, Rim(i + 1), Rim(i));
        }

        /// <summary>
        /// Puts one of the ground layers in the scene: no collider, off the map, and on
        /// whichever layer the bake is not looking at. Paint is not geometry as far as
        /// anything but the eye is concerned -- see the note above about what happens
        /// when it is told so with a NavMeshModifier instead.
        /// </summary>
        private static void Flat(Transform parent, int layer, string name, MeshBuild build,
                                 string key, Material material)
        {
            if (build.Triangles.Count == 0) return;

            var go = MeshObject(parent, name, ToMesh(build, key), material, Vector3.zero,
                                Quaternion.identity, Vector3.one, layer, null, collider: false);

            Hide(go);
        }

        /// <summary>
        /// A gantry crane over a yard: two portal legs on rails, a girder across them and
        /// a trolley on it.
        ///
        /// Twelve metres up and spanning a whole container row, it does for a yard what
        /// the pipe bridge does for a street -- it gives the space a top edge, and it is
        /// the thing that says the stacks below it were put there by a machine.
        /// </summary>
        private static void BuildGantryCrane(Transform parent, int layer, System.Random rng,
                                             Vector3 at, float span, float height, float yaw)
        {
            var build = new MeshBuild { UVScale = 0.4f };
            float hs = span * 0.5f;

            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * hs;

                // A-frame legs rather than posts: two uprights leaning together.
                build.Box(new Vector3(x - side * 1.6f, height * 0.5f, -2.2f),
                          new Vector3(0.75f, height, 0.75f), Quaternion.Euler(0f, 0f, side * 3.5f));
                build.Box(new Vector3(x - side * 1.6f, height * 0.5f, 2.2f),
                          new Vector3(0.75f, height, 0.75f), Quaternion.Euler(0f, 0f, side * 3.5f));

                build.Box(new Vector3(x - side * 1.6f, height * 0.62f, 0f),
                          new Vector3(0.5f, 0.5f, 4.4f), Quaternion.identity);

                // Bogies on the rail.
                build.Box(new Vector3(x - side * 1.6f, 0.6f, 0f), new Vector3(2.4f, 1.2f, 6f),
                          Quaternion.identity);
            }

            // The girder, its walkway and the trolley.
            build.Box(new Vector3(0f, height + 0.9f, 0f), new Vector3(span + 4f, 1.8f, 2.2f),
                      Quaternion.identity);
            build.Box(new Vector3(0f, height + 1.9f, 1.7f), new Vector3(span + 4f, 0.12f, 1.2f),
                      Quaternion.identity);

            float trolley = Rand(rng, -hs * 0.6f, hs * 0.6f);
            build.Box(new Vector3(trolley, height + 2.2f, 0f), new Vector3(3.2f, 2.2f, 3f),
                      Quaternion.identity);
            build.Box(new Vector3(trolley, height - 2.4f, 0f), new Vector3(0.22f, 5f, 0.22f),
                      Quaternion.identity);
            build.Box(new Vector3(trolley, height - 5.2f, 0f), new Vector3(2.4f, 0.8f, 1.4f),
                      Quaternion.identity);

            // The rails it runs on, which is what puts it in a yard rather than on one.
            for (int side = -1; side <= 1; side += 2)
                build.Box(new Vector3(side * (hs - 1.6f), 0.08f, 0f),
                          new Vector3(0.7f, 0.16f, span), Quaternion.identity);

            var go = MeshObject(parent, "GantryCrane",
                                ToMesh(build, $"gantry{span:0}_{height:0}_{trolley:0.0}"),
                                _zoneRust, at, Quaternion.Euler(0f, yaw, 0f), Vector3.one,
                                layer, "Metal");

            NoStanding(go);
            if (go != null) Hide(go);
        }
    }
}
#endif
