#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The master plan of the Abandoned Fairground, and the walkways that join its parts.
    ///
    /// <b>This is a plan, not a scatter.</b> The first fairground chose where things stood
    /// from a random stream and then drew paths between whatever the dice had left, which
    /// is how a fairground ends up with a ferris wheel, a hall and nothing that connects
    /// them. A real fairground is designed round an axis: a gate, a long avenue, a plaza
    /// where the avenues cross, and districts hung off them. So every site below has a
    /// fixed place, and every walkway runs from a door to a door.
    ///
    /// <pre>
    ///                        service road (asphalt, all the way round)
    ///      garden walk                    GRAND HALL (north, faces the plaza)
    ///   swing  pond  maze   drop       \                       /  coaster
    ///          \    |      tower        \       forecourt      /  + station
    ///   ----------------- cross avenue ------- ferris wheel -------- ...
    ///   carousel     CENTRAL PLAZA (spawn)         graveyard
    ///   big top   |   main avenue   |   bumper cars
    ///          midway rows both sides    haunted house
    ///                  GATE
    /// </pre>
    ///
    /// <b>Everything walkable at ground level is still the terrain.</b> The paving is paint
    /// laid a few centimetres over the heightfield, in tiles with gaps in them, with no
    /// collider and nothing the bake looks at. What is genuinely above the ground -- the hall
    /// gallery, the coaster station, the haunted balcony -- has a real stair at its end, and
    /// <c>VerifyReach</c> is what proves it.
    ///
    /// <b>The ground here is gently rolling, not dunes.</b> The theme's dune height is a few
    /// metres, so the terrain is level enough to stand a ride on yet still not a billiard
    /// table; each site flattens a pad of its own before the heightfield is made.
    /// </summary>
    public partial class FPSKitSceneBuilder
    {
        // ------------------------------------------------------------------
        // Sites
        // ------------------------------------------------------------------
        private static ParkSite _plazaPlan, _hauntedPlan, _dropPlan, _swingPlan, _mazePlan,
                                _graveyardPlan, _clockPlan, _stationPlan;

        private struct FgPathDef
        {
            public Vector2[] Points;
            public float Width;
            public bool Road;
            public bool Smooth;
            public bool Kerbs;
        }

        private struct FgPlaza
        {
            public Vector2 Centre;
            public float Radius;
        }

        private static readonly List<FgPathDef> _fgDefs = new List<FgPathDef>();
        private static readonly List<FgPlaza> _fgPlazas = new List<FgPlaza>();

        private static void ResetFairgroundPlan()
        {
            _fgDefs.Clear();
            _fgPlazas.Clear();
            _fgEntrances.Clear();
        }

        /// <summary>A fixed site: its ground is flattened, claimed and kept clear of trees.</summary>
        private static ParkSite PlanSite(float x, float z, float radius, float blend, float yaw)
        {
            Claim(x, z, radius + blend + 3f);
            Keep(x, z, radius + 2f);
            FlattenPad(x, z, radius, blend);
            _anchors.Add(new Vector3(x, 0f, z));
            return new ParkSite { Centre = new Vector2(x, z), Radius = radius, Yaw = yaw };
        }

        private static void AddPath(float width, bool road, bool smooth, bool kerbs, params float[] xz)
        {
            var points = new Vector2[xz.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector2(xz[i * 2], xz[i * 2 + 1]);

            _fgDefs.Add(new FgPathDef { Points = points, Width = width, Road = road, Smooth = smooth, Kerbs = kerbs });

            // Reserve a corridor: nothing planned afterwards (a hollow, a prop) may sit on a path.
            var samples = ResamplePath(smooth ? Chaikin(points, 2) : new List<Vector2>(points), 9f);
            foreach (var p in samples) Claim(p.x, p.y, width * 0.5f + 3f);
        }

        private static void AddPlaza(float x, float z, float radius)
        {
            _fgPlazas.Add(new FgPlaza { Centre = new Vector2(x, z), Radius = radius });
            Keep(x, z, radius);
        }

        // ------------------------------------------------------------------
        // Entrances: every place a visitor goes in
        // ------------------------------------------------------------------
        private struct FgEntrance
        {
            public string Name;
            public Vector2 Point;
        }

        private static readonly List<FgEntrance> _fgEntrances = new List<FgEntrance>();

        /// <summary>
        /// Registers a door. Every entrance is checked against the walkway network when the park is
        /// built (<see cref="CheckWalkwayGraph"/>) and written into the scene as a marker, so the
        /// verifier can ask the same question of the saved scene: is there paving at this door?
        /// </summary>
        private static void Entrance(string name, float x, float z)
            => _fgEntrances.Add(new FgEntrance { Name = name, Point = new Vector2(x, z) });

        /// <summary>
        /// Every fixed place in the park, in one table. Metres from the centre; x east, z north.
        /// The gate is at the south, the hall at the north, and the player starts in the middle of
        /// the plaza where the two avenues cross. The park is 340m across, laid out so that the next
        /// scene is never more than about forty metres from the last.
        /// </summary>
        private static void PlanFairground(float half)
        {
            ResetFairgroundPlan();
            _fgEntrances.Clear();

            // ---- the spine ----
            _plazaPlan = PlanSite(0f, 0f, 30f, 14f, 0f);
            _entrancePlan = PlanSite(0f, -132f, 20f, 10f, 0f);
            _hasEntrance = true;

            // ---- districts ----
            _hallPlan = PlanSite(0f, 104f, HallPad, 12f, 180f);
            _hasHall = true;

            _ridePlans.Clear();
            void Ride(int kind, float x, float z, float r)
            {
                _ridePlans.Add(new RidePlan { Centre = new Vector2(x, z), Radius = r, Kind = kind });
                float blend = r * 0.3f;
                Claim(x, z, r + blend + 3f);
                Keep(x, z, r + 3f);
                FlattenPad(x, z, r, blend);
                _anchors.Add(new Vector3(x, 0f, z));
            }

            Ride(0, 80f, 26f, 22f);          // ferris wheel
            Ride(1, -98f, -64f, 32f);        // big top
            Ride(2, -52f, -28f, 20f);        // carousel
            Ride(3, 100f, 92f, 48f);         // roller coaster and its station

            _bumperPlan = PlanSite(70f, -52f, 26f, 10f, 270f); _hasBumper = true;
            _hauntedPlan = PlanSite(96f, -106f, 28f, 10f, 270f);
            _pondPlan = PlanSite(-82f, 46f, 30f, 10f, 0f); _hasPond = true;
            _dropPlan = PlanSite(-128f, 62f, 18f, 8f, 90f);
            _swingPlan = PlanSite(-124f, 18f, 20f, 8f, 0f);
            _mazePlan = PlanSite(-70f, 106f, 23f, 8f, 0f);
            _graveyardPlan = PlanSite(126f, -50f, 24f, 8f, 270f);
            _maintenancePlan = PlanSite(-120f, 122f, 26f, 8f, 135f); _hasMaintenance = true;
            _clockPlan = PlanSite(24f, 22f, 7f, 5f, 0f);
            _kartPlan = PlanSite(-122f, -110f, 24f, 8f, 0f);
            _gardenPlan = PlanSite(-32f, 48f, 20f, 6f, 0f);
            _campPlan = PlanSite(-56f, 62f, 8f, 4f, 0f);

            PlanFill();

            // The midway is not a site, it is a street: flat pads down both sides of the avenue.
            _midwayPlan = new ParkSite { Centre = new Vector2(0f, -76f), Radius = 50f, Yaw = 0f };
            _hasMidway = true;
            for (float z = -108f; z <= -36f; z += 24f)
            {
                FlattenPad(0f, z, 24f, 10f);
                Claim(0f, z, 30f);
            }

            // ---- the walkway network ----
            // Laid in this order, and a later strip never overlaps an earlier one: a spur leaves an
            // avenue at its edge and ends at the edge of a plaza or at a gate.
            AddPath(10f, false, false, true, 0f, -114f, 0f, -26f);        // main avenue, gate to plaza
            AddPath(10f, false, false, true, 0f, 26f, 0f, 52f);           // main avenue, plaza to the hall's forecourt
            AddPath(8f, false, false, true, -26f, 0f, -146f, 0f);         // cross avenue, west
            AddPath(8f, false, false, true, 26f, 0f, 146f, 0f);           // cross avenue, east

            AddPath(6f, false, true, true, 5f, -52f, 28f, -52f, 46f, -52f);                // to the bumper cars
            AddPath(6f, false, true, true, 5f, -106f, 38f, -108f, 70f, -106f);             // to the haunted house
            AddPath(6f, false, true, true, -5f, -64f, -36f, -68f, -68f, -64f);             // to the big top
            AddPath(5f, false, true, true, 80f, 46f, 86f, 48f, 90f, 52f);                  // ferris plaza to the coaster
            AddPath(4f, false, true, false, -52f, -4f, -52f, -8f);                         // the carousel's gate
            AddPath(4f, false, true, true, -5f, 44f, -14f, 48f);                           // to the rose garden
            AddPath(4f, false, true, true, -14f, 48f, -50f, 48f);                          // across the garden
            AddPath(4f, false, true, true, -32f, 66f, -32f, 30f);                          // down the garden
            AddPath(4f, false, true, true, -32f, 30f, -34f, 14f, -36f, 4f);                // garden to the cross avenue
            AddPath(5f, false, true, true, -50f, 48f, -62f, 48f);                          // garden to the pond
            AddPath(5f, false, true, true, -70f, 41f, -80f, 35f, -98f, 36f, -114f, 50f, -122f, 57f);  // round the pond to the drop tower
            AddPath(4f, false, true, true, -124f, 4f, -124f, 6f);                          // the swing ride's gate
            AddPath(4f, false, true, true, -124f, 30f, -126f, 52f);                        // swings up to the drop tower
            AddPath(5f, false, true, true, -14f, 66f, -34f, 74f, -52f, 82f, -58f, 84f);    // forecourt to the maze
            AddPath(4f, false, true, true, -104f, -92f, -114f, -90f);                      // big top to the kart track
            AddPath(4f, true, true, false, 150f, -50f, 144f, -50f);                        // the graveyard's gate
            AddPath(4f, true, true, false, -149f, 122f, -144f, 122f);                      // the yard's gate

            AddPath(4f, false, true, true, 19f, 17.5f, 22f, 20f);                          // to the clock tower

            // The service road runs the whole perimeter, gate to gate.
            AddPath(6f, true, true, false,
                    18f, -132f, 60f, -140f, 110f, -138f, 146f, -112f, 152f, -50f, 152f, 0f, 152f, 60f, 150f, 120f,
                    128f, 148f, 60f, 152f, 0f, 152f, -60f, 152f, -128f, 148f, -152f, 120f, -152f, 60f, -152f, 0f,
                    -152f, -60f, -146f, -112f, -110f, -138f, -60f, -140f, -18f, -132f);
            AddPath(5f, true, true, false, 0f, 134f, 0f, 152f);                            // the hall's back door
            AddPath(5f, true, true, false, 146f, 0f, 152f, 0f);                            // east end of the cross avenue
            AddPath(5f, true, true, false, -146f, 0f, -152f, 0f);                          // west end

            // ---- plazas: where a walkway arrives somewhere ----
            AddPlaza(0f, 0f, 26f);
            AddPlaza(0f, -132f, 18f);
            AddPlaza(0f, 66f, 14f);
            AddPlaza(80f, 26f, 22f);
            AddPlaza(98f, 52f, 9f);
            AddPlaza(-52f, -28f, 21f);
            AddPlaza(-98f, -64f, 30f);
            AddPlaza(70f, -52f, 24f);
            AddPlaza(80f, -106f, 10f);
            AddPlaza(-32f, 48f, 6f);
            AddPlaza(-69f, 46f, 5f);
            AddPlaza(-124f, 18f, 12f);
            AddPlaza(-128f, 62f, 10f);
            AddPlaza(-60f, 86f, 5f);
            AddPlaza(-122f, -86f, 10f);
            AddPlaza(24f, 22f, 7f);

            // ---- where people go in: each one is checked against the network ----
            Entrance("gate", 0f, -126f);
            Entrance("hall", 0f, 76f);
            Entrance("ferris", 80f, 6f);
            Entrance("coaster station", 98f, 56f);
            Entrance("carousel", -52f, -9f);
            Entrance("big top", -68f, -64f);
            Entrance("bumper cars", 48f, -52f);
            Entrance("haunted house", 78f, -106f);
            Entrance("hedge maze", -60f, 84f);
            Entrance("rose garden", -32f, 48f);
            Entrance("pond dock", -69f, 46f);
            Entrance("drop tower", -122f, 57f);
            Entrance("swing ride", -124f, 7f);
            Entrance("kart track", -114f, -90f);
            Entrance("clock tower", 24f, 22f);
            Entrance("graveyard gate", 146f, -50f);
            Entrance("yard gate", -146f, 122f);

            // ---- hollows: three, by hand, in open ground between districts ----
            _sinks.Clear();
            void Hollow(float x, float z, float radius, bool shaft)
            {
                float reach = radius * 2.2f;
                Claim(x, z, reach + 3f);
                Keep(x, z, radius * 1.1f);
                float depth = 4.5f + radius * 0.25f;
                float floorY = NaturalHeightAt(x, z) - depth;
                FlattenPad(x, z, radius * 0.45f, reach - radius * 0.45f, floorY);
                _sinks.Add(new Sink { Centre = new Vector2(x, z), Radius = radius, Depth = depth, HasShaft = shaft });
                _anchors.Add(new Vector3(x, 0f, z));
            }

            Hollow(-58f, -106f, 10f, true);
            Hollow(48f, -100f, 9f, false);
            Hollow(132f, 30f, 9f, true);
        }

        private static ParkSite _gardenPlan, _campPlan, _kartPlan;

        // ------------------------------------------------------------------
        // Curves
        // ------------------------------------------------------------------
        private static List<Vector2> Chaikin(IList<Vector2> points, int iterations)
        {
            var current = new List<Vector2>(points);

            for (int it = 0; it < iterations && current.Count > 2; it++)
            {
                var next = new List<Vector2> { current[0] };
                for (int i = 0; i < current.Count - 1; i++)
                {
                    next.Add(Vector2.Lerp(current[i], current[i + 1], 0.25f));
                    next.Add(Vector2.Lerp(current[i], current[i + 1], 0.75f));
                }

                next.Add(current[current.Count - 1]);
                current = next;
            }

            return current;
        }

        private static List<Vector2> ResamplePath(IList<Vector2> points, float spacing)
        {
            var result = new List<Vector2> { points[0] };
            float carry = 0f;

            for (int i = 0; i < points.Count - 1; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                float length = Vector2.Distance(a, b);
                if (length < 0.001f) continue;

                float at = spacing - carry;
                while (at <= length)
                {
                    result.Add(Vector2.Lerp(a, b, at / length));
                    at += spacing;
                }

                carry = length - (at - spacing);
            }

            var last = points[points.Count - 1];
            if ((result[result.Count - 1] - last).sqrMagnitude > 0.01f) result.Add(last);
            return result;
        }

        // ------------------------------------------------------------------
        // Laying them
        // ------------------------------------------------------------------
        private static void PathQuad(PathBatch batch, Vector3 a0, Vector3 a1, Vector3 b1, Vector3 b0)
        {
            int start = batch.Vertices.Count;
            batch.Vertices.Add(a0); batch.Vertices.Add(a1); batch.Vertices.Add(b1); batch.Vertices.Add(b0);
            foreach (var v in new[] { a0, a1, b1, b0 }) batch.Uvs.Add(new Vector2(v.x, v.z) * 0.25f);

            // Clockwise seen from above, which is what Unity calls the front: the first fairground laid
            // its paving the other way up, so from the ground there was no paving at all.
            batch.Triangles.Add(start); batch.Triangles.Add(start + 3); batch.Triangles.Add(start + 2);
            batch.Triangles.Add(start); batch.Triangles.Add(start + 2); batch.Triangles.Add(start + 1);
        }

        private static Vector3 OnGround(Vector2 p, float lift) => new Vector3(p.x, GroundHeightAt(p.x, p.y) + lift, p.y);

        private static bool InsidePlaza(Vector2 p, float margin)
        {
            foreach (var plaza in _fgPlazas)
                if ((plaza.Centre - p).sqrMagnitude < (plaza.Radius - margin) * (plaza.Radius - margin)) return true;

            return false;
        }

        /// <summary>
        /// Lays every walkway and plaza: paving in tiles with gaps in them, kerbs that stop
        /// and start, a dashed line down the service road, and grass pushing up through
        /// all of it. Records each walkway as a route, which is what lamps, benches, trees
        /// and clutter key off.
        /// </summary>
        private static void BuildFairgroundPaths(Transform parent, int layer, int backdrop)
        {
            var paving = new PathBatch();
            var pavingDark = new PathBatch();
            var pavingLight = new PathBatch();
            var asphalt = new PathBatch();
            var lines = new PathBatch();
            var kerbs = new MeshBuild { UVScale = 0.5f };
            var weeds = new MeshBuild { UVScale = 0.5f };
            var verge = new PathBatch();
            var bollards = new MeshBuild { UVScale = 0.5f };
            var chains = new MeshBuild { UVScale = 0.5f };
            var rng = new System.Random(_theme.randomSeed * 2297 + 41);

            int tiles = 0, gaps = 0;

            for (int d = 0; d < _fgDefs.Count; d++)
            {
                var def = _fgDefs[d];
                var centres = ResamplePath(def.Smooth ? Chaikin(def.Points, 3) : new List<Vector2>(def.Points), 2.4f);
                var batch = def.Road ? asphalt : paving;
                _fgRoutes.Add(new FgRoute { Points = centres, Width = def.Width });

                float walked = 0f;
                float total = 0f;
                var lastPost = new Vector3?[2];
                float postAt = 3f;
                for (int i = 0; i < centres.Count - 1; i++) total += Vector2.Distance(centres[i], centres[i + 1]);

                for (int i = 0; i < centres.Count - 1; i++)
                {
                    var a = centres[i];
                    var b = centres[i + 1];
                    float length = Vector2.Distance(a, b);
                    if (length < 0.01f) continue;

                    var direction = (b - a) / length;
                    var side = new Vector2(-direction.y, direction.x);
                    var prevDirection = i > 0 ? (a - centres[i - 1]).normalized : direction;
                    var sideA = new Vector2(-prevDirection.y, prevDirection.x);

                    // Edges that wander a little: a path nobody has swept in years is not a ruler line.
                    float wobbleA = 1f + Mathf.Sin(walked * 0.21f + d * 1.9f) * 0.035f;
                    float wobbleB = 1f + Mathf.Sin((walked + length) * 0.21f + d * 1.9f) * 0.035f;
                    float hwA = def.Width * 0.5f * wobbleA, hwB = def.Width * 0.5f * wobbleB;

                    var mid = (a + b) * 0.5f;
                    walked += length;

                    // Under a plaza the plaza is the surface.
                    if (InsidePlaza(mid, 0.5f)) continue;

                    // Tiles that have gone. Never at the ends: the first and last stretch of a
                    // walkway is where it meets something, and a hole there reads as a mistake.
                    bool ends = walked < 8f || total - walked < 8f;
                    double loss = def.Road ? 0.035 : 0.075;
                    if (!ends && rng.NextDouble() < loss)
                    {
                        gaps++;
                        Tuft(weeds, OnGround(mid, 0f), Rand(rng, 0.5f, 1.1f), rng.Next(1, 99999));
                        Tuft(weeds, OnGround(mid + side * Rand(rng, -1.2f, 1.2f), 0f), Rand(rng, 0.4f, 0.9f), rng.Next(1, 99999));
                        continue;
                    }

                    float lift = def.Road ? 0.05f : 0.065f;
                    float jitter = (float)(rng.NextDouble() - 0.5) * 0.012f;

                    if (def.Road)
                    {
                        PathQuad(batch, OnGround(a + sideA * hwA, lift + jitter), OnGround(a - sideA * hwA, lift + jitter),
                                 OnGround(b - side * hwB, lift + jitter), OnGround(b + side * hwB, lift + jitter));
                        tiles++;
                    }
                    else
                    {
                        // Paving is laid in stones about the size of a person's stride: the width of the
                        // walkway cut into columns, each a different tone, with a seam of ground between.
                        int columns = Mathf.Max(1, Mathf.RoundToInt(def.Width / 2.2f));
                        for (int col = 0; col < columns; col++)
                        {
                            float f0 = col / (float)columns, f1 = (col + 1f) / columns;
                            float seam = 0.03f;
                            float l0A = Mathf.Lerp(hwA, -hwA, f0) - seam, l1A = Mathf.Lerp(hwA, -hwA, f1) + seam;
                            float l0B = Mathf.Lerp(hwB, -hwB, f0) - seam, l1B = Mathf.Lerp(hwB, -hwB, f1) + seam;

                            if (rng.NextDouble() < 0.025) continue;       // a single stone missing

                            float tone = Hash01(d * 131 + i, col + 7);
                            var target = tone < 0.18f ? pavingDark : tone > 0.78f ? pavingLight : paving;
                            // A seam of ground between stones along the walkway as well as across it.
                            var aa = a + direction * 0.03f; var bb = b - direction * 0.03f;
                            PathQuad(target, OnGround(aa + sideA * l0A, lift + jitter), OnGround(aa + sideA * l1A, lift + jitter),
                                     OnGround(bb + side * l1B, lift + jitter), OnGround(bb + side * l0B, lift + jitter));
                            tiles++;
                        }
                    }

                    // Grass in the joints along the edge.
                    if (rng.NextDouble() < 0.34)
                    {
                        int s = rng.Next(-1, 2) >= 0 ? 1 : -1;
                        var edge = mid + side * s * (def.Width * 0.5f + Rand(rng, -0.3f, 0.5f));
                        Tuft(weeds, OnGround(edge, 0f), Rand(rng, 0.5f, 1.2f), rng.Next(1, 99999));
                    }

                    // Dashes down the middle of the road.
                    if (def.Road && def.Width >= 5.5f && (i % 4) < 2 && rng.NextDouble() < 0.8)
                    {
                        float w = 0.12f;
                        PathQuad(lines,
                                 OnGround(a + sideA * w, 0.075f), OnGround(a - sideA * w, 0.075f),
                                 OnGround(b - side * w, 0.075f), OnGround(b + side * w, 0.075f));
                    }

                    // A gravel verge a metre wide down each side, so the walkway has an edge that is not
                    // simply where the grass stops; and on the narrower ones a bollard every five metres
                    // with a chain slung between them.
                    if (!def.Road && !ends)
                    {
                        const float vg = 1.2f;
                        PathQuad(verge, OnGround(a + sideA * (hwA + vg), 0.04f), OnGround(a + sideA * hwA, 0.04f),
                                 OnGround(b + side * hwB, 0.04f), OnGround(b + side * (hwB + vg), 0.04f));
                        PathQuad(verge, OnGround(a - sideA * hwA, 0.04f), OnGround(a - sideA * (hwA + vg), 0.04f),
                                 OnGround(b - side * (hwB + vg), 0.04f), OnGround(b - side * hwB, 0.04f));

                        if (def.Width < 9f && walked > postAt)
                        {
                            postAt = walked + 5f;
                            for (int sd = 0; sd < 2; sd++)
                            {
                                float sgn = sd == 0 ? 1f : -1f;
                                if (rng.NextDouble() < 0.18) { lastPost[sd] = null; continue; }

                                var pp = mid + side * sgn * (def.Width * 0.5f + 0.6f);
                                var foot = OnGround(pp, 0f);
                                bollards.Tube(foot, foot + Vector3.up * 0.95f, 0.07f, 0.06f, 5);
                                bollards.Tube(foot + Vector3.up * 0.95f, foot + Vector3.up * 1.05f, 0.1f, 0.05f, 5);

                                if (lastPost[sd].HasValue && Vector3.Distance(lastPost[sd].Value, foot) < 6f)
                                    Cable(chains, lastPost[sd].Value + Vector3.up * 0.85f, foot + Vector3.up * 0.85f, 0.12f, 0.012f, 3);

                                lastPost[sd] = foot;
                            }
                        }
                    }

                    // A kerb down each side, broken in places and absent at the ends.
                    if (def.Kerbs && !ends && rng.NextDouble() > 0.12)
                    {
                        for (int s = -1; s <= 1; s += 2)
                        {
                            var pa = a + side * s * (def.Width * 0.5f + 0.18f);
                            var pb = b + side * s * (def.Width * 0.5f + 0.18f);
                            var ka = OnGround(pa, 0.12f);
                            var kb = OnGround(pb, 0.12f);
                            kerbs.Box((ka + kb) * 0.5f, new Vector3(0.32f, 0.24f, Vector3.Distance(ka, kb) + 0.04f),
                                      Quaternion.LookRotation(kb - ka));
                        }
                    }
                }
            }

            // ---- plazas: rings of two stones, with a worn-out hole here and there ----
            foreach (var plaza in _fgPlazas)
            {
                int ring = 0;
                for (float r0 = 0f; r0 < plaza.Radius - 0.3f; r0 += 2.6f, ring++)
                {
                    float r1 = Mathf.Min(r0 + 2.6f, plaza.Radius);
                    float rm = (r0 + r1) * 0.5f;
                    int cells = Mathf.Max(1, Mathf.CeilToInt(Mathf.PI * 2f * rm / 2.8f));
                    if (r0 < 0.01f) cells = 1;

                    for (int c = 0; c < cells; c++)
                    {
                        // Missing tiles thin out the edge of a plaza the most.
                        double lost = 0.03 + 0.14 * Mathf.Clamp01((rm - plaza.Radius * 0.55f) / (plaza.Radius * 0.45f));
                        if (rng.NextDouble() < lost)
                        {
                            Tuft(weeds, OnGround(plaza.Centre + new Vector2(Mathf.Cos(c), Mathf.Sin(c)) * rm, 0f),
                                 Rand(rng, 0.5f, 1.2f), rng.Next(1, 99999));
                            continue;
                        }

                        float a0 = c * Mathf.PI * 2f / cells, a1 = (c + 1) * Mathf.PI * 2f / cells;
                        Vector2 P(float r, float a) => plaza.Centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;

                        // One quad per cell; a centre disc is a fan of them. Rings alternate light and dark,
                        // and a random few stones in each are the odd one out.
                        float tone = Hash01(ring * 977 + c, 31);
                        var target = (ring & 1) == 0
                            ? (tone < 0.15f ? pavingDark : paving)
                            : (tone < 0.15f ? paving : pavingDark);
                        if (ring % 4 == 0 && tone > 0.7f) target = pavingLight;
                        float lift = 0.1f;

                        if (r0 < 0.01f)
                        {
                            int fan = 10;
                            for (int f = 0; f < fan; f++)
                            {
                                float f0 = f * Mathf.PI * 2f / fan, f1 = (f + 1) * Mathf.PI * 2f / fan;
                                PathQuad(target, OnGround(plaza.Centre, lift), OnGround(P(r1, f0), lift),
                                         OnGround(P(r1, f1), lift), OnGround(plaza.Centre, lift));
                            }
                        }
                        else
                        {
                            PathQuad(target, OnGround(P(r0, a0), lift), OnGround(P(r1, a0), lift),
                                     OnGround(P(r1, a1), lift), OnGround(P(r0, a1), lift));
                        }

                        tiles++;
                    }
                }
            }

            EmitPaths(parent, layer, "WalkwayVerge", verge, _fgGravel);
            EmitPaths(parent, layer, "Walkways", paving, _fgPaving);
            EmitPaths(parent, layer, "WalkwaysDark", pavingDark, _fgPaving2);
            EmitPaths(parent, layer, "WalkwaysLight", pavingLight, _fgPavingLight);
            EmitPaths(parent, layer, "ServiceRoad", asphalt, _fgAsphalt);
            EmitPaths(parent, layer, "RoadLines", lines, _fgLine);

            if (kerbs.Triangles.Count > 0)
            {
                var kerb = MeshObject(parent, "Kerbs", ToMesh(kerbs, DenseKey("kerbs")), _fgKerb,
                                      Vector3.zero, Quaternion.identity, Vector3.one, backdrop, null, collider: false);
                kerb.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Hide(kerb);
            }

            EmitFoliage(parent, backdrop, "PathWeeds", weeds, _fgFern);
            RideDecor(parent, "Bollards", bollards, _parkSteel);
            RideDecor(parent, "BollardChains", chains, _parkDark);

            CheckWalkwayGraph(parent);

            Debug.Log($"[FPSKit] fairground walkways: {_fgDefs.Count} path(s), {_fgPlazas.Count} plaza(s), {tiles} tile(s), {gaps} gap(s).");
        }

        // ------------------------------------------------------------------
        // Does the network join up?
        // ------------------------------------------------------------------
        /// <summary>How far a point is from the nearest paving: negative inside a plaza or on a walkway.</summary>
        private static float DistanceToNetwork(Vector2 p)
        {
            float best = float.MaxValue;

            foreach (var route in _fgRoutes)
                for (int i = 0; i < route.Points.Count - 1; i++)
                    best = Mathf.Min(best, DistanceToSegment(p, route.Points[i], route.Points[i + 1]) - route.Width * 0.5f);

            foreach (var plaza in _fgPlazas)
                best = Mathf.Min(best, Vector2.Distance(p, plaza.Centre) - plaza.Radius);

            return best;
        }

        /// <summary>
        /// The walkways are a graph: paths and plazas are the nodes, and two are joined where their
        /// paving meets. This asks two questions of it -- is it one piece, and does every entrance
        /// stand on it -- and reports the answer rather than assuming it. The first fairground had
        /// walkways that stopped short of what they were for and nothing noticed.
        /// Also writes a marker at every entrance, so the verifier can ask the same of the saved scene.
        /// </summary>
        private static void CheckWalkwayGraph(Transform parent)
        {
            int paths = _fgRoutes.Count, nodes = paths + _fgPlazas.Count;
            var parentOf = new int[nodes];
            for (int i = 0; i < nodes; i++) parentOf[i] = i;

            int Find(int x) { while (parentOf[x] != x) { parentOf[x] = parentOf[parentOf[x]]; x = parentOf[x]; } return x; }
            void Join(int a, int b) { parentOf[Find(a)] = Find(b); }

            const float Gap = 2.5f;

            for (int i = 0; i < paths; i++)
            {
                var samples = ResamplePath(_fgRoutes[i].Points, 2f);
                float hw = _fgRoutes[i].Width * 0.5f;

                for (int j = 0; j < paths; j++)
                {
                    if (j == i) continue;
                    foreach (var p in samples)
                    {
                        float best = float.MaxValue;
                        for (int k = 0; k < _fgRoutes[j].Points.Count - 1; k++)
                            best = Mathf.Min(best, DistanceToSegment(p, _fgRoutes[j].Points[k], _fgRoutes[j].Points[k + 1]));

                        if (best - hw - _fgRoutes[j].Width * 0.5f <= Gap) { Join(i, j); break; }
                    }
                }

                for (int q = 0; q < _fgPlazas.Count; q++)
                    foreach (var p in samples)
                        if (Vector2.Distance(p, _fgPlazas[q].Centre) - _fgPlazas[q].Radius - hw <= Gap) { Join(i, paths + q); break; }
            }

            var roots = new HashSet<int>();
            for (int i = 0; i < nodes; i++) roots.Add(Find(i));

            int problems = 0;
            if (roots.Count > 1)
            {
                problems++;
                Debug.LogError($"[FPSKit] fairground walkways are {roots.Count} separate networks, not one.");
            }

            foreach (var e in _fgEntrances)
            {
                float d = DistanceToNetwork(e.Point);
                if (d > 2.5f)
                {
                    problems++;
                    Debug.LogError($"[FPSKit] fairground entrance \"{e.Name}\" at {e.Point} is {d:0.0}m from any walkway.");
                }
            }

            var group = new GameObject("Entrances").transform;
            group.SetParent(parent.parent != null ? parent.parent : parent, false);
            foreach (var e in _fgEntrances)
            {
                var marker = new GameObject($"Entrance_{e.Name}").transform;
                marker.SetParent(group, false);
                marker.position = new Vector3(e.Point.x, GroundHeightAt(e.Point.x, e.Point.y), e.Point.y);
            }

            Debug.Log($"[FPSKit] fairground walkway graph: {paths} path(s) + {_fgPlazas.Count} plaza(s) in {roots.Count} network(s), " +
                      $"{_fgEntrances.Count} entrance(s), {problems} problem(s).");
        }
    }
}
#endif
