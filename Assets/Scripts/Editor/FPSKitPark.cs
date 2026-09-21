#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The abandoned park: smooth sunken ground, four rides to navigate by, and holes in
    /// it that kill you.
    ///
    /// <b>Everything walkable here is the terrain, and that is the whole design.</b> The
    /// arena this replaced was a station on three levels joined by stairs, bridges and
    /// ramps, and every one of those joins was somewhere the navigation could fail -- it
    /// took a dozen build-and-verify cycles to find them and it still did not pass. A park
    /// is one continuous surface with things standing on it. There are no levels to
    /// connect, so there is nothing to fail to connect, and the rides sit on flattened pads
    /// exactly the way the desert's compounds do.
    ///
    /// <b>The ground is the desert's, subsided.</b> Same heightfield, same relaxation, same
    /// smoothing -- so it rises and falls the way sand does rather than the way noise does
    /// -- with broad hollows pulled into it. A hollow is a pad pinned below the natural
    /// height rather than carved afterwards, which means it goes through the same
    /// nearest-pad-wins arithmetic every other flattened site does and cannot fight with a
    /// ride standing next to it.
    ///
    /// <b>The pitfalls are the shafts, never the hollows.</b> This kit has made the other
    /// mistake once already: a kill trigger written as one box as wide as the corridor the
    /// river wanders about in, which killed the player on four thousand square metres of
    /// ordinary sand nowhere near any water. From inside the game that is "I walked towards
    /// the river and died". So a hollow here is a place -- you walk down into it, fight in
    /// it and walk out -- and only the opening in its floor is lethal. You can stand at the
    /// lip of a shaft.
    ///
    /// <b>They are hidden at distance and plain up close.</b> From across the park a hollow
    /// is a shadow in the ground. At its rim there is broken decking, a collapsed cover and
    /// a hole. A player dies because they were careless or because something pushed them,
    /// which is tension; a player who dies with no way of having known is just being told
    /// the game is broken.
    /// </summary>
    public partial class FPSKitSceneBuilder
    {
        private struct Sink
        {
            public Vector2 Centre;
            public float Radius;
            public float Depth;
            public bool HasShaft;
        }

        private static readonly List<Sink> _sinks = new List<Sink>();

        /// <summary>How far down a shaft the kill trigger starts. Below the lip, so that
        /// standing on the edge is safe and leaning in is not.</summary>
        private const float ShaftMouthDrop = 2.2f;

        private static void BuildParkZone()
        {
            _claimed.Clear();
            _anchors.Clear();
            _sinks.Clear();

            ClearMeshPool();
            ResetTerrain();

            var root = new GameObject("Arena").transform;
            int layer = LayerMask.NameToLayer("Environment");
            int backdrop = LayerMask.NameToLayer("Backdrop");
            if (backdrop < 0) backdrop = layer;

            var rng = new System.Random(_theme.randomSeed);
            float half = _theme.arenaSize * 0.5f;

            ResolveSurfaceMaterials();
            BuildSurfaceTextures();
            ResolveOutdoorMaterials();
            ResolveParkMaterials();

            // The spawn, before anything can be planned on top of it.
            Claim(0f, 0f, 30f);
            _anchors.Add(Vector3.zero);
            FlattenPad(0f, 0f, 20f, 30f);

            // ---- planning: sites are chosen and pads reserved before the ground exists,
            // ---- because a ride is a straight-sided thing and there is no version of that
            // ---- which follows a hill. Same two-half shape the open zone uses, and the
            // ---- Plan and Build passes must stay in the same order as each other.
            PlanRides(rng, half);
            PlanSinkholes(rng, half);

            // ---- ground ----
            BuildDuneField(root, layer, half);
            BuildApron(root, backdrop, half);
            BuildBoundary(root, layer, half);
            BuildBackdrop(root, backdrop, rng, half);

            // ---- what stands on it ----
            BuildSinkholes(root, layer, rng);
            BuildRides(root, layer, rng);
            BuildMidway(root, layer, rng, half);
            BuildParkScatter(root, layer, rng, half);
            BuildParkLights(root, rng, half);

            // The same call the open zone makes, with the same numbers. Guessed at
            // instead -- half + 260 rather than half + apronSize -- it left four hundred
            // metres of dune field outside the reach unsealed, baking happily behind the
            // boundary ridge and joined to nothing.
            // Well inside the ridge's toe, not level with it. At the toe exactly, the
            // strip of lower slope between the flat ground and the seal baked and was cut
            // off from everything -- a ring 26% of the arena's navmesh wide. Fourteen
            // metres of a 450m arena is nothing to give up for that.
            SealNavMeshOutside(root, half - BermToe - 8f, half + _theme.apronSize);
        }


        // ==================================================================
        // Planning. Sites first, ground second, structures third.
        // ==================================================================

        private struct RidePlan
        {
            public Vector2 Centre;
            public float Radius;
            public int Kind;   // 0 ferris, 1 big top, 2 carousel, 3 coaster
        }

        private static readonly List<RidePlan> _ridePlans = new List<RidePlan>();

        /// <summary>
        /// Where the four anchors go.
        ///
        /// Spread by angle rather than placed at random, because these are what a player
        /// navigates by -- four landmarks clustered in one half of the map is a park with
        /// one landmark and three things behind it. Each still gets a jittered distance and
        /// a random bearing within its quadrant, so the plan is not a cross.
        /// </summary>
        private static void PlanRides(System.Random rng, float half)
        {
            _ridePlans.Clear();

            // Radius is the flat pad each needs, which is the footprint plus room to walk
            // round it. The ferris wheel needs least -- it is tall, not wide.
            float[] radii = { 20f, 30f, 22f, 34f };

            for (int kind = 0; kind < 4; kind++)
            {
                float bearing = (kind + 0.5f) * Mathf.PI * 0.5f
                                + (float)(rng.NextDouble() - 0.5) * 0.7f;

                // Far enough out that the spawn is not inside one, near enough that none is
                // in the boundary ridge.
                float distance = Mathf.Lerp(half * 0.34f, half * 0.68f, (float)rng.NextDouble());

                var centre = new Vector2(Mathf.Cos(bearing) * distance,
                                         Mathf.Sin(bearing) * distance);

                _ridePlans.Add(new RidePlan { Centre = centre, Radius = radii[kind], Kind = kind });

                Claim(centre.x, centre.y, radii[kind] + 4f);
                FlattenPad(centre.x, centre.y, radii[kind], radii[kind] * 0.9f);
            }
        }

        /// <summary>
        /// Where the ground has given way.
        ///
        /// A hollow is a pad pinned below the natural height rather than a hole cut after
        /// the fact, so it goes through the same arithmetic as every other flattened site
        /// and the terrain smooths into it instead of ending at a wall. The blend is wide --
        /// two and a half times the radius -- because that is what makes it a subsidence
        /// rather than a crater, and because a rim the player can walk down is the whole
        /// difference between a place and an obstacle.
        ///
        /// Only some get a shaft. A park where every hollow kills teaches the player to
        /// avoid all of them, which removes the ground the fight is supposed to use.
        /// </summary>
        private static void PlanSinkholes(System.Random rng, float half)
        {
            int wanted = 7 + rng.Next(4);

            for (int i = 0; i < wanted; i++)
            {
                float radius = 13f + (float)rng.NextDouble() * 15f;

                if (!TryClaim(rng, half * 0.88f, radius + 6f, out var point, 26)) continue;

                float depth = 5f + (float)rng.NextDouble() * 6f;

                // Pinned to natural ground minus the depth, so a hollow on high ground is
                // still a hollow rather than being levelled to the same absolute height as
                // one in a basin.
                float floorY = NaturalHeightAt(point.x, point.y) - depth;
                FlattenPad(point.x, point.y, radius * 0.45f, radius * 2.5f, floorY);

                _sinks.Add(new Sink
                {
                    Centre = point,
                    Radius = radius,
                    Depth = depth,

                    // Roughly half, and never the first: the player has to be able to learn
                    // that a hollow is not automatically fatal before one of them is.
                    HasShaft = i > 0 && rng.NextDouble() < 0.55,
                });

                _anchors.Add(new Vector3(point.x, 0f, point.y));
            }
        }


        // ==================================================================
        // What stands on the ground.
        // ==================================================================

        /// <summary>
        /// The hollows, and the holes in the bottom of some of them.
        ///
        /// The hollow itself is already in the terrain by now -- it was a pad. What is
        /// built here is what tells the player what they are looking at: the broken ring of
        /// decking round a shaft, the collapsed cover over it, and the dark of the hole.
        ///
        /// <b>The trigger is the shaft and nothing else.</b> Its box is the shaft's own
        /// width, and its lid sits <see cref="ShaftMouthDrop"/> *below* the lip, so a player
        /// standing at the edge is safe and a player who steps in is not. Sized to the
        /// hollow instead it would kill across a thirty-metre bowl that is meant to be
        /// fought in -- which is the mistake the river made, and the reason it is written
        /// down.
        /// </summary>
        private static void BuildSinkholes(Transform root, int layer, System.Random rng)
        {
            if (_sinks.Count == 0) return;

            var group = new GameObject("Sinkholes").transform;
            group.SetParent(root, false);

            foreach (var sink in _sinks)
            {
                float floorY = GroundHeightAt(sink.Centre.x, sink.Centre.y);

                // Debris round the rim, which is what makes a hollow read as a collapse
                // rather than as a dip. Placed on the slope, not the floor.
                int rubble = 5 + rng.Next(6);
                for (int i = 0; i < rubble; i++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float d = sink.Radius * (0.55f + (float)rng.NextDouble() * 0.6f);

                    float x = sink.Centre.x + Mathf.Cos(a) * d;
                    float z = sink.Centre.y + Mathf.Sin(a) * d;

                    float w = 1.6f + (float)rng.NextDouble() * 3.4f;

                    var slab = CreateBlock(group,
                        new Vector3(x, GroundHeightAt(x, z) + 0.18f, z),
                        new Vector3(w, 0.4f + (float)rng.NextDouble() * 0.5f,
                                    w * (0.5f + (float)rng.NextDouble())),
                        (float)rng.NextDouble() * 360f, layer,
                        "Concrete", Shade(_theme.floorColor, 0.7f), 0.05f, 0f,
                        "Slab", _parkTimber);
                    NoStanding(slab);
                }

                if (!sink.HasShaft) continue;

                float mouth = 4.6f + (float)rng.NextDouble() * 3.4f;

                // The shaft. A dark box sunk into the floor of the hollow, with its rim
                // standing a little proud so there is an edge to see and to stand on.
                var shaft = CreateBlock(group,
                    new Vector3(sink.Centre.x, floorY - 14f, sink.Centre.y),
                    new Vector3(mouth, 28f, mouth), 0f, layer,
                    "Concrete", new Color(0.05f, 0.045f, 0.055f), 0f, 0f,
                    "Shaft", _parkDark);
                NoEntry(shaft);

                // The collar: four short walls round the mouth, so the hole has a lip
                // rather than being a rectangle painted on the floor. This is the thing a
                // player sees from five metres and not from fifty.
                for (int side = 0; side < 4; side++)
                {
                    bool alongX = side < 2;
                    float sign = (side % 2 == 0) ? 1f : -1f;

                    var pos = alongX
                        ? new Vector3(sink.Centre.x, floorY + 0.35f, sink.Centre.y + mouth * 0.5f * sign)
                        : new Vector3(sink.Centre.x + mouth * 0.5f * sign, floorY + 0.35f, sink.Centre.y);

                    var scale = alongX
                        ? new Vector3(mouth + 1.4f, 0.7f, 0.7f)
                        : new Vector3(0.7f, 0.7f, mouth + 1.4f);

                    var collar = CreateBlock(group, pos, scale, 0f, layer,
                                             "Wood", Shade(_theme.bankColor, 0.66f), 0.08f, 0f,
                                             $"Collar{side}", _parkTimber);
                    NoStanding(collar);
                }

                // Boards across part of it -- a cover that has already failed. Telegraphs
                // the hole from close by and makes it obvious it was always a hole.
                int boards = 2 + rng.Next(3);
                for (int b = 0; b < boards; b++)
                {
                    float off = (b - (boards - 1) * 0.5f) * (mouth / (boards + 1));

                    var board = CreateBlock(group,
                        new Vector3(sink.Centre.x + off, floorY + 0.5f, sink.Centre.y),
                        new Vector3(0.45f, 0.14f, mouth * (0.4f + (float)rng.NextDouble() * 0.5f)),
                        (float)(rng.NextDouble() - 0.5) * 26f, layer,
                        "Wood", Shade(_theme.bankColor, 0.6f), 0.06f, 0f,
                        $"Board{b}", _parkTimber);
                    NoStanding(board);
                }

                BuildShaftKill(group, sink.Centre, mouth, floorY);
            }
        }

        /// <summary>
        /// The trigger in one shaft.
        ///
        /// Deliberately smaller than the mouth and starting below the lip. A trigger flush
        /// with the opening catches somebody walking past the edge; one that reaches the
        /// rim catches somebody standing on it. Neither is the deal -- the deal is that
        /// going *in* kills you.
        /// </summary>
        private static void BuildShaftKill(Transform parent, Vector2 centre, float mouth, float floorY)
        {
            var go = new GameObject("Pitfall");
            go.transform.SetParent(parent, false);

            float lid = floorY - ShaftMouthDrop;
            float bottom = floorY - 26f;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(centre.x, (lid + bottom) * 0.5f, centre.y);
            box.size = new Vector3(mouth - 1.2f, lid - bottom, mouth - 1.2f);

            var kill = go.AddComponent<KillVolume>();
            kill.instantKill = true;
            kill.damagePerSecond = 200f;
        }


        /// <summary>The four anchors, each built where its pad was reserved.</summary>
        private static void BuildRides(Transform root, int layer, System.Random rng)
        {
            var group = new GameObject("Rides").transform;
            group.SetParent(root, false);

            foreach (var plan in _ridePlans)
            {
                float y = GroundHeightAt(plan.Centre.x, plan.Centre.y);
                var at = new Vector3(plan.Centre.x, y, plan.Centre.y);

                switch (plan.Kind)
                {
                    case 0: BuildFerrisWheel(group, layer, rng, at); break;
                    case 1: BuildBigTop(group, layer, rng, at); break;
                    case 2: BuildCarousel(group, layer, rng, at); break;
                    default: BuildCoaster(group, layer, rng, at, plan.Radius); break;
                }
            }
        }

        /// <summary>
        /// The tallest thing on the map, and therefore the compass.
        ///
        /// Leaning, because a wheel standing true reads as a working ride. The lean is what
        /// says the place has been left. Held off the bake entirely -- every spoke and car
        /// is a surface an agent would otherwise be spawned onto forty metres up.
        /// </summary>
        private static void BuildFerrisWheel(Transform parent, int layer, System.Random rng, Vector3 at)
        {
            var wheel = new GameObject("FerrisWheel").transform;
            wheel.SetParent(parent, false);
            wheel.position = at;
            wheel.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 180f, 6.5f);

            const float R = 21f;
            float hubY = R + 4f;

            // A-frame legs.
            for (int side = -1; side <= 1; side += 2)
                for (int lean = -1; lean <= 1; lean += 2)
                {
                    var leg = CreateBlock(wheel,
                        new Vector3(lean * 7f, hubY * 0.5f, side * 5f),
                        new Vector3(1.3f, hubY * 1.06f, 1.3f), 0f, layer,
                        "Metal", new Color(0.3f, 0.31f, 0.33f), 0.3f, 0.6f,
                        $"Leg{side}{lean}", _parkSteel);
                    leg.transform.localRotation = Quaternion.Euler(0f, 0f, -lean * 9f);
                    NoStanding(leg);
                }

            var hub = CreateBlock(wheel, new Vector3(0f, hubY, 0f), new Vector3(3f, 3f, 7f),
                                  0f, layer, "Metal", new Color(0.26f, 0.27f, 0.29f), 0.4f, 0.7f,
                                  "Hub", _parkSteel);
            NoStanding(hub);

            const int Spokes = 16;
            for (int i = 0; i < Spokes; i++)
            {
                float a = i * Mathf.PI * 2f / Spokes;

                var spoke = CreateBlock(wheel, new Vector3(Mathf.Cos(a) * R * 0.5f, hubY + Mathf.Sin(a) * R * 0.5f, 0f),
                                        new Vector3(R, 0.32f, 0.32f), 0f, layer,
                                        "Metal", new Color(0.28f, 0.29f, 0.31f), 0.35f, 0.65f,
                                        $"Spoke{i}", _parkSteel);
                spoke.transform.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg);
                NoStanding(spoke);

                // Most cars are gone. The ones left are what makes the silhouette read.
                if (rng.NextDouble() > 0.55) continue;

                var car = CreateBlock(wheel,
                    new Vector3(Mathf.Cos(a) * R, hubY + Mathf.Sin(a) * R, 0f),
                    new Vector3(2.6f, 2.4f, 3.2f), 0f, layer,
                    "Metal", _theme.RandomCoverColor(rng), 0.2f, 0.3f,
                    $"Car{i}", _parkPaint);
                NoStanding(car);
            }
        }

        /// <summary>
        /// The one place the fight goes indoors.
        ///
        /// Four openings, because anything a player can walk into needs more than one way
        /// out -- the same rule the industrial hall follows. The floor inside is the park's
        /// own ground, so nothing is sealed: this is a roof on poles, not a room.
        /// </summary>
        private static void BuildBigTop(Transform parent, int layer, System.Random rng, Vector3 at)
        {
            var tent = new GameObject("BigTop").transform;
            tent.SetParent(parent, false);
            tent.position = at;

            const float R = 24f;
            const float WallH = 5.5f;

            // The wall, in segments, with four gaps left in it.
            const int Segments = 20;
            for (int i = 0; i < Segments; i++)
            {
                if (i % 5 == 0) continue;   // four doorways

                float a0 = i * Mathf.PI * 2f / Segments;
                float a1 = (i + 1) * Mathf.PI * 2f / Segments;
                float mid = (a0 + a1) * 0.5f;

                float chord = Vector2.Distance(new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * R,
                                               new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * R);

                var panel = CreateBlock(tent,
                    new Vector3(Mathf.Cos(mid) * R, WallH * 0.5f, Mathf.Sin(mid) * R),
                    new Vector3(chord + 0.4f, WallH, 0.5f),
                    -mid * Mathf.Rad2Deg, layer,
                    "Wood", Shade(_theme.wallColor, 0.9f), 0.05f, 0f,
                    $"Panel{i}", _parkCanvas);
                Hide(panel);
            }

            // The roof, as a cone of sloped panels, and the king pole through it.
            const int Slices = 20;
            for (int i = 0; i < Slices; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / Slices;

                var flap = CreateBlock(tent,
                    new Vector3(Mathf.Cos(a) * R * 0.55f, WallH + 4.2f, Mathf.Sin(a) * R * 0.55f),
                    new Vector3(R * 1.16f, 0.35f, R * 0.36f),
                    -a * Mathf.Rad2Deg, layer,
                    "Wood", Shade(_theme.wallColor, 0.78f), 0.04f, 0f,
                    $"Roof{i}", _parkCanvas);
                flap.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 21f);
                NoStanding(flap);
                Hide(flap);
            }

            var pole = CreateBlock(tent, new Vector3(0f, (WallH + 12f) * 0.5f, 0f),
                                   new Vector3(1.1f, WallH + 12f, 1.1f), 0f, layer,
                                   "Wood", Shade(_theme.bankColor, 0.7f), 0.06f, 0f,
                                   "KingPole", _parkTimber);
            NoStanding(pole);

            // Tiered seating inside: cover, and a reason to be in here.
            for (int ring = 0; ring < 3; ring++)
            {
                float rr = R - 4f - ring * 3.2f;
                int seats = 14 - ring * 2;

                for (int i = 0; i < seats; i++)
                {
                    float a = i * Mathf.PI * 2f / seats + ring * 0.2f;

                    var bench = CreateBlock(tent,
                        new Vector3(Mathf.Cos(a) * rr, 0.45f + ring * 0.4f, Mathf.Sin(a) * rr),
                        new Vector3(4.2f, 0.9f + ring * 0.8f, 1.5f),
                        -a * Mathf.Rad2Deg, layer,
                        "Wood", Shade(_theme.bankColor, 0.75f), 0.05f, 0f,
                        $"Bench{ring}_{i}", _parkTimber);
                    NoStanding(bench);
                }
            }
        }

        /// <summary>The carousel: a canopy on a drum, with horses left on it.</summary>
        private static void BuildCarousel(Transform parent, int layer, System.Random rng, Vector3 at)
        {
            var ride = new GameObject("Carousel").transform;
            ride.SetParent(parent, false);
            ride.position = at;

            const float R = 13f;

            var deck = CreateBlock(ride, new Vector3(0f, 0.55f, 0f),
                                   new Vector3(R * 2f, 1.1f, R * 2f), 0f, layer,
                                   "Wood", Shade(_theme.bankColor, 0.88f), 0.08f, 0f,
                                   "Deck", _parkTimber);
            Mark(deck, new Color(0.46f, 0.3f, 0.28f), 1);

            var column = CreateBlock(ride, new Vector3(0f, 4.4f, 0f),
                                     new Vector3(2.2f, 8.8f, 2.2f), 0f, layer,
                                     "Metal", new Color(0.32f, 0.28f, 0.26f), 0.3f, 0.5f,
                                     "Column", _parkPaint);
            NoStanding(column);

            const int Posts = 12;
            for (int i = 0; i < Posts; i++)
            {
                float a = i * Mathf.PI * 2f / Posts;

                var post = CreateBlock(ride,
                    new Vector3(Mathf.Cos(a) * (R - 1.5f), 3.2f, Mathf.Sin(a) * (R - 1.5f)),
                    new Vector3(0.35f, 5f, 0.35f), 0f, layer,
                    "Metal", new Color(0.55f, 0.48f, 0.3f), 0.4f, 0.6f,
                    $"Post{i}", _parkSteel);
                NoStanding(post);

                if (rng.NextDouble() > 0.5) continue;

                var horse = CreateBlock(ride,
                    new Vector3(Mathf.Cos(a) * (R - 3.4f), 2.1f, Mathf.Sin(a) * (R - 3.4f)),
                    new Vector3(2.2f, 1.9f, 0.9f), -a * Mathf.Rad2Deg, layer,
                    "Wood", _theme.RandomCoverColor(rng), 0.12f, 0f,
                    $"Horse{i}", _parkPaint);
                NoStanding(horse);
            }

            var canopy = CreateBlock(ride, new Vector3(0f, 6.6f, 0f),
                                     new Vector3(R * 2.2f, 0.5f, R * 2.2f), 0f, layer,
                                     "Wood", Shade(_theme.wallColor, 0.86f), 0.05f, 0f,
                                     "Canopy", _parkCanvas);
            NoStanding(canopy);
            Hide(canopy);
        }

        /// <summary>
        /// Collapsed coaster track: long diagonals across the sky, and things to fight
        /// under. The supports are what a player actually interacts with.
        /// </summary>
        private static void BuildCoaster(Transform parent, int layer, System.Random rng,
                                         Vector3 at, float radius)
        {
            var ride = new GameObject("Coaster").transform;
            ride.SetParent(parent, false);
            ride.position = at;
            ride.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

            int bents = 7;
            float span = radius * 1.8f;

            for (int i = 0; i < bents; i++)
            {
                float t = i / (float)(bents - 1);
                float x = Mathf.Lerp(-span * 0.5f, span * 0.5f, t);

                // A profile that climbs and then falls, which is what a coaster is.
                float h = 6f + Mathf.Sin(t * Mathf.PI) * 17f;

                for (int leg = -1; leg <= 1; leg += 2)
                {
                    var post = CreateBlock(ride, new Vector3(x, h * 0.5f, leg * 2.6f),
                                           new Vector3(0.8f, h, 0.8f), 0f, layer,
                                           "Wood", Shade(_theme.bankColor, 0.72f), 0.06f, 0f,
                                           $"Bent{i}_{leg}", _parkTimber);
                    NoStanding(post);
                }

                // Cross bracing, which is most of what makes timber structure read.
                var brace = CreateBlock(ride, new Vector3(x, h * 0.55f, 0f),
                                        new Vector3(0.35f, 0.35f, 6f), 0f, layer,
                                        "Wood", Shade(_theme.bankColor, 0.68f), 0.06f, 0f,
                                        $"Brace{i}", _parkTimber);
                NoStanding(brace);

                if (i == 0) continue;

                // The track between this bent and the last, which is the diagonal.
                float prevT = (i - 1) / (float)(bents - 1);
                float prevX = Mathf.Lerp(-span * 0.5f, span * 0.5f, prevT);
                float prevH = 6f + Mathf.Sin(prevT * Mathf.PI) * 17f;

                // One span in three has fallen. A complete run reads as a working ride.
                if (rng.NextDouble() < 0.34) continue;

                var a = new Vector3(prevX, prevH, 0f);
                var b = new Vector3(x, h, 0f);
                var mid = (a + b) * 0.5f;
                float length = Vector3.Distance(a, b);
                float pitch = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;

                var track = CreateBlock(ride, mid, new Vector3(length, 0.4f, 4.4f),
                                        0f, layer, "Wood", Shade(_theme.bankColor, 0.8f),
                                        0.08f, 0f, $"Track{i}", _parkTimber);
                track.transform.localRotation = Quaternion.Euler(0f, 0f, pitch);
                NoStanding(track);
            }
        }


        /// <summary>
        /// The midway: two rows of stalls facing each other across a lane.
        ///
        /// This is the close-quarters part of the park, and it works because it is a
        /// *street* -- two solid rows with gaps, so every sightline is either down the lane
        /// or through one gap. Scattering the same stalls over the same area would give the
        /// same object count and none of the shape.
        /// </summary>
        private static void BuildMidway(Transform root, int layer, System.Random rng, float half)
        {
            if (!TryClaim(rng, half * 0.72f, 26f, out var centre, 30)) return;

            var group = new GameObject("Midway").transform;
            group.SetParent(root, false);

            float bearing = (float)rng.NextDouble() * Mathf.PI;
            var along = new Vector2(Mathf.Cos(bearing), Mathf.Sin(bearing));
            var across = new Vector2(-along.y, along.x);

            const float LaneHalf = 6.5f;
            int perSide = 7;

            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < perSide; i++)
                {
                    // A gap every few stalls, so the lane is not a corridor with two walls.
                    if (rng.NextDouble() < 0.22) continue;

                    float t = (i - (perSide - 1) * 0.5f) * 8.5f;

                    var p = centre + along * t + across * (side * (LaneHalf + 2.6f));
                    float y = GroundHeightAt(p.x, p.y);

                    float w = 5f + (float)rng.NextDouble() * 2.2f;
                    float d = 4f + (float)rng.NextDouble() * 1.6f;
                    float h = 3.2f + (float)rng.NextDouble() * 1.1f;

                    CreateBlock(group, new Vector3(p.x, y + h * 0.5f, p.y),
                                new Vector3(w, h, d), -bearing * Mathf.Rad2Deg, layer,
                                "Wood", _theme.RandomCoverColor(rng), 0.08f, 0f,
                                $"Stall{side}_{i}", _parkTimber);

                    // The awning, which is what says "stall" rather than "shed".
                    var awning = CreateBlock(group,
                        new Vector3(p.x - across.x * side * (d * 0.5f + 1.1f), y + h - 0.35f,
                                    p.y - across.y * side * (d * 0.5f + 1.1f)),
                        new Vector3(w, 0.22f, 2.4f), -bearing * Mathf.Rad2Deg, layer,
                        "Wood", Shade(_theme.wallColor, 0.9f), 0.06f, 0f,
                        $"Awning{side}_{i}", _parkCanvas);
                    NoStanding(awning);

                    _anchors.Add(new Vector3(p.x, 0f, p.y));
                }
            }
        }

        /// <summary>Litter of the park: fencing, bins, broken ride parts, ticket booths.</summary>
        private static void BuildParkScatter(Transform root, int layer, System.Random rng, float half)
        {
            var group = new GameObject("Scatter").transform;
            group.SetParent(root, false);

            int count = _theme.ScaledCount(46);

            for (int i = 0; i < count; i++)
            {
                if (!TryClaim(rng, half * 0.92f, 3.2f, out var p, 12)) continue;

                float y = GroundHeightAt(p.x, p.y);
                double roll = rng.NextDouble();

                if (roll < 0.42f)
                {
                    // A run of hoarding. Cover that reads as a park rather than as a crate.
                    float w = 5f + (float)rng.NextDouble() * 7f;
                    CreateBlock(group, new Vector3(p.x, y + 1.15f, p.y),
                                new Vector3(w, 2.3f, 0.32f),
                                (float)rng.NextDouble() * 360f, layer,
                                "Wood", Shade(_theme.bankColor, 0.78f), 0.06f, 0f,
                                $"Hoarding{i}", _parkTimber);
                }
                else if (roll < 0.72f)
                {
                    var booth = CreateBlock(group, new Vector3(p.x, y + 1.5f, p.y),
                                            new Vector3(2.8f, 3f, 2.8f),
                                            (float)rng.NextDouble() * 360f, layer,
                                            "Wood", _theme.RandomCoverColor(rng), 0.08f, 0f,
                                            $"Booth{i}", _parkPaint);
                    NoEntry(booth);
                }
                else
                {
                    // Ride wreckage: a piece of something big, lying where it fell.
                    var wreck = CreateBlock(group, new Vector3(p.x, y + 0.7f, p.y),
                                            new Vector3(1.4f + (float)rng.NextDouble() * 3f, 1.4f,
                                                        1.2f + (float)rng.NextDouble() * 2.4f),
                                            (float)rng.NextDouble() * 360f, layer,
                                            "Metal", new Color(0.3f, 0.29f, 0.3f), 0.25f, 0.45f,
                                            $"Wreck{i}", _parkSteel);
                    NoStanding(wreck);
                }
            }
        }

        /// <summary>
        /// What is still lit.
        ///
        /// <b>This gets more care than an outdoor arena, not less, and the subway is why.</b>
        /// That level was given nine short-range lamps across 450m and rendered as a large
        /// dark room with no readable structure in it -- the geometry was all there and none
        /// of it could be seen. A dark arena is a *lighting design*; darkness on its own is
        /// just an absence.
        ///
        /// Three things carry it. The moon is the base, dim but everywhere, so nothing is
        /// ever truly black. Each ride gets a light of its own, because a landmark you
        /// cannot see is not a landmark. And the sinkholes are lit from *inside*, which is
        /// the one trick that matters here: a hollow with a glow in it reads as a hole from
        /// across the park, and that is the telegraphing the hazard needs.
        /// </summary>
        private static void BuildParkLights(Transform root, System.Random rng, float half)
        {
            var group = new GameObject("ParkLights").transform;
            group.SetParent(root, false);

            foreach (var plan in _ridePlans)
            {
                float y = GroundHeightAt(plan.Centre.x, plan.Centre.y);

                var go = new GameObject($"RideLight{plan.Kind}");
                go.transform.SetParent(group, false);
                go.transform.position = new Vector3(plan.Centre.x, y + 9f, plan.Centre.y);

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;

                // Sodium, and failing. Warm against the blue of the moon, which is what
                // separates "still has power" from "does not" at a distance.
                light.color = new Color(1f, 0.72f, 0.38f);
                light.intensity = 5.5f + (float)rng.NextDouble() * 2f;
                light.range = plan.Radius * 2.6f;
                light.shadows = LightShadows.Soft;
            }

            foreach (var sink in _sinks)
            {
                if (!sink.HasShaft) continue;

                float floorY = GroundHeightAt(sink.Centre.x, sink.Centre.y);

                var go = new GameObject("ShaftGlow");
                go.transform.SetParent(group, false);
                go.transform.position = new Vector3(sink.Centre.x, floorY + 1.4f, sink.Centre.y);

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;

                // Cold and weak. Enough to pick the rim out of the dark and to say
                // something is down there; not enough to light the hollow.
                light.color = new Color(0.42f, 0.66f, 0.8f);
                light.intensity = 2.6f;
                light.range = sink.Radius * 1.5f;
                light.shadows = LightShadows.None;
            }
        }

        // ==================================================================
        private static Material _parkTimber;
        private static Material _parkPaint;
        private static Material _parkSteel;
        private static Material _parkCanvas;
        private static Material _parkDark;

        private static void ResolveParkMaterials()
        {
            _parkTimber = MakeDetailMaterial("ParkTimber", Shade(_theme.bankColor, 0.82f),
                                             "Timber", Metres(2.2f), 0.06f, 0f, 1.4f);

            // The one colour left in the place. Everything here was painted once, and what
            // survives of that is the only thing in the park that is not grey or brown --
            // so it has to be used sparingly or it stops reading as a survivor.
            _parkPaint = MakeDetailMaterial("ParkPaint", new Color(0.42f, 0.16f, 0.17f),
                                            "Timber", Metres(1.6f), 0.16f, 0f, 1.1f);

            _parkSteel = MakeDetailMaterial("ParkSteel", new Color(0.29f, 0.30f, 0.32f),
                                            "Metal", Metres(1.4f), 0.34f, 0.65f);

            _parkCanvas = MakeDetailMaterial("ParkCanvas", new Color(0.50f, 0.47f, 0.43f),
                                             "Adobe", Metres(5f), 0.04f, 0f, 0.9f);

            // What the inside of a shaft is. Not black -- a hole that is pure black reads
            // as a rendering fault, and this has to read as depth.
            _parkDark = MakeMaterialAt($"{MaterialFolder}/{SafeName(_theme.themeName)}_Void.mat",
                                       new Color(0.045f, 0.04f, 0.05f), 0f, 0f);
        }
    }
}
#endif
