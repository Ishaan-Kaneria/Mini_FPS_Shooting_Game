#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The abandoned fairground: gently rolling ground, a planned park on it (see
    /// <c>FPSKitFairgroundPlan</c>), and holes in it that kill you.
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
        private struct ParkSite
        {
            public Vector2 Centre;
            public float Radius;
            public float Yaw;
        }

        private static ParkSite _entrancePlan, _midwayPlan, _maintenancePlan;
        private static ParkSite _hallPlan, _pondPlan, _bumperPlan;
        private static bool _hasEntrance, _hasMidway, _hasMaintenance;
        private static bool _hasHall, _hasPond, _hasBumper;

        /// <summary>How far down a shaft the kill trigger starts. Below the lip, so that
        /// standing on the edge is safe and leaning in is not.</summary>
        private const float ShaftMouthDrop = 2.2f;


        private static void BuildParkZone()
        {
            _claimed.Clear();
            _anchors.Clear();
            _sinks.Clear();
            _hasEntrance = _hasMidway = _hasMaintenance = false;
            ResetFairground();

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
            Keep(0f, 0f, 16f);

            // ---- planning: every site is fixed and its ground reserved before the heightfield
            // ---- exists, because a ride is a straight-sided thing and there is no version of that
            // ---- which follows a hill. The Plan and Build passes name the same sites.
            PlanFairground(half);

            // ---- ground ----
            BuildDuneField(root, layer, half);
            BuildApron(root, backdrop, half);
            BuildBoundary(root, layer, half);
            BuildBackdrop(root, backdrop, rng, half);

            // ---- the park, in the order a visitor meets it ----
            BuildFairgroundPaths(root, backdrop, backdrop);
            BuildSinkholes(root, layer, rng);
            BuildGate(root, layer, rng);
            BuildPlazaDressing(root, layer, backdrop, rng);
            BuildMidwayStreet(root, layer, backdrop, rng);
            BuildGrandHall(root, layer, backdrop);
            BuildRides(root, layer, rng);
            BuildBumperPavilion(root, layer, backdrop);
            BuildPond(root, layer, backdrop);
            BuildMaintenanceYard(root, layer, rng);
            BuildRideGraveyard(root, layer, rng);
            BuildFill(root, layer, backdrop, rng);
            BuildScenes(root, layer, backdrop, rng);
            BuildHoarding(root, layer, rng);
            BuildPromenadeFurniture(root, layer, backdrop);
            BuildClutter(root, layer, backdrop, half);
            BuildJungle(root, layer, backdrop, half);
            BuildGroundDecals(root, backdrop, half);
            BuildFinalDetails(root, backdrop, half);
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

        private struct RidePlan
        {
            public Vector2 Centre;
            public float Radius;
            public int Kind;   // 0 ferris, 1 big top, 2 carousel, 3 coaster
        }

        private static readonly List<RidePlan> _ridePlans = new List<RidePlan>();


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
                var marker = new GameObject("Sinkhole").transform;
                marker.SetParent(group, false);
                marker.position = new Vector3(sink.Centre.x, floorY, sink.Centre.y);

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


        private static void BuildServiceVan(Transform parent, int layer, Vector3 at)
        {
            var van = new GameObject("ServiceVan").transform;
            van.SetParent(parent, false);
            van.localPosition = at;
            van.localRotation = Quaternion.Euler(0f, 12f, 0f);

            var body = CreateBlock(van, new Vector3(0f, 1.15f, 0f), new Vector3(4.8f, 2.1f, 2.1f),
                                   0f, layer, "Metal", new Color(0.39f, 0.37f, 0.31f),
                                   0.18f, 0.24f, "Body", _parkSteel);
            NoStanding(body);

            var cab = CreateBlock(van, new Vector3(-0.75f, 2.15f, 0f), new Vector3(2.4f, 1.0f, 1.95f),
                                  0f, layer, "Metal", new Color(0.32f, 0.34f, 0.31f),
                                  0.18f, 0.2f, "Cab", _parkSteel);
            NoStanding(cab);

            for (int side = -1; side <= 1; side += 2)
                for (int axle = -1; axle <= 1; axle += 2)
                {
                    var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    wheel.name = $"Wheel{side}_{axle}";
                    wheel.transform.SetParent(van, false);
                    wheel.transform.localPosition = new Vector3(axle * 1.55f, 0.58f, side * 1.05f);
                    wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    wheel.transform.localScale = new Vector3(0.62f, 0.2f, 0.62f);
                    wheel.layer = layer;
                    wheel.tag = "Concrete";
                    wheel.GetComponent<Renderer>().sharedMaterial =
                        MakeDetailMaterial("Tyre", new Color(0.075f, 0.075f, 0.075f), "Rock",
                                           0.8f, 0.08f, 0f, 0.4f);
                    NoStanding(wheel);
                }
        }

        private static Vector2 ParkPoint(ParkSite site, float localX, float localZ)
        {
            var offset = Quaternion.Euler(0f, site.Yaw, 0f) * new Vector3(localX, 0f, localZ);
            return site.Centre + new Vector2(offset.x, offset.z);
        }

        /// <summary>A run of fence: posts and two rails, not a slab.</summary>
        private static void BuildFenceRun(Transform parent, int layer, System.Random rng,
                                          Vector2 p, float yaw, float length, string id)
        {
            var run = new GameObject($"Fence{id}").transform;
            run.SetParent(parent, false);
            run.position = new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y);
            run.localRotation = Quaternion.Euler(0f, yaw, 0f);

            int posts = Mathf.Max(2, Mathf.RoundToInt(length / 2.4f));

            for (int i = 0; i <= posts; i++)
            {
                float t = i / (float)posts;
                float x = Mathf.Lerp(-length * 0.5f, length * 0.5f, t);

                // A fence that has been standing this long has lost some of its posts.
                if (i > 0 && i < posts && rng.NextDouble() < 0.16) continue;

                CreateBlock(run, new Vector3(x, 0.85f, 0f), new Vector3(0.14f, 1.7f, 0.14f),
                            (float)(rng.NextDouble() - 0.5) * 9f, layer,
                            "Wood", Shade(_theme.bankColor, 0.7f), 0.06f, 0f,
                            $"Post{i}", _parkTimber);
            }

            for (int rail = 0; rail < 2; rail++)
            {
                if (rng.NextDouble() < 0.2) continue;   // a rail down

                CreateBlock(run, new Vector3(0f, 0.6f + rail * 0.7f, 0f),
                            new Vector3(length, 0.12f, 0.08f), 0f, layer,
                            "Wood", Shade(_theme.bankColor, 0.76f), 0.06f, 0f,
                            $"Rail{rail}", _parkTimber);
            }
        }

        /// <summary>A lamp post: column, arm and head. Lit, and the reason the park is
        /// navigable at night without the whole place being floodlit.</summary>
        private static void BuildLampPost(Transform parent, int layer, System.Random rng,
                                          Vector2 p, string id)
        {
            float y = GroundHeightAt(p.x, p.y);

            var lamp = new GameObject($"Lamp{id}").transform;
            lamp.SetParent(parent, false);
            lamp.position = new Vector3(p.x, y, p.y);
            lamp.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f,
                                                  (float)(rng.NextDouble() - 0.5) * 7f);

            const float H = 6.4f;

            CreateBlock(lamp, new Vector3(0f, H * 0.5f, 0f), new Vector3(0.22f, H, 0.22f),
                        0f, layer, "Metal", new Color(0.24f, 0.24f, 0.26f), 0.35f, 0.6f,
                        "Column", _parkSteel);

            var arm = CreateBlock(lamp, new Vector3(0.7f, H - 0.2f, 0f),
                                  new Vector3(1.6f, 0.14f, 0.14f), 0f, layer,
                                  "Metal", new Color(0.24f, 0.24f, 0.26f), 0.35f, 0.6f,
                                  "Arm", _parkSteel);
            NoStanding(arm);

            var head = CreateBlock(lamp, new Vector3(1.4f, H - 0.35f, 0f),
                                   new Vector3(0.7f, 0.3f, 0.5f), 0f, layer,
                                   "Metal", new Color(0.3f, 0.28f, 0.24f), 0.5f, 0.4f,
                                   "Head", _parkSteel);
            NoStanding(head);

            // Two in five are dead, which is what makes the live ones read as survivors
            // rather than as a lighting scheme.
            if (rng.NextDouble() < 0.4) return;

            var go = new GameObject("Bulb");
            go.transform.SetParent(lamp, false);
            go.transform.localPosition = new Vector3(1.4f, H - 0.6f, 0f);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.74f, 0.42f);
            light.intensity = 3.4f;
            light.range = 15f;
            light.shadows = LightShadows.None;
        }

        /// <summary>A bin: drum and lid.</summary>
        private static void BuildBin(Transform parent, int layer, System.Random rng,
                                     Vector2 p, string id)
        {
            float y = GroundHeightAt(p.x, p.y);

            var bin = new GameObject($"Bin{id}").transform;
            bin.SetParent(parent, false);
            bin.position = new Vector3(p.x, y, p.y);
            bin.localRotation = Quaternion.Euler((float)(rng.NextDouble() - 0.5) * 24f,
                                                 (float)rng.NextDouble() * 360f, 0f);

            CreateBlock(bin, new Vector3(0f, 0.55f, 0f), new Vector3(0.75f, 1.1f, 0.75f),
                        0f, layer, "Metal", new Color(0.27f, 0.26f, 0.24f), 0.3f, 0.5f,
                        "Drum", _parkSteel);

            var lid = CreateBlock(bin, new Vector3(0.25f, 1.16f, 0.1f),
                                  new Vector3(0.9f, 0.1f, 0.9f), 0f, layer,
                                  "Metal", new Color(0.3f, 0.29f, 0.27f), 0.4f, 0.55f,
                                  "Lid", _parkSteel);
            lid.transform.localRotation = Quaternion.Euler(0f, 0f, 22f);
            NoStanding(lid);
        }

        /// <summary>A ticket booth: box, roof, window and a step.</summary>
        private static void BuildBooth(Transform parent, int layer, System.Random rng,
                                       Vector2 p, string id)
        {
            float y = GroundHeightAt(p.x, p.y);

            var booth = new GameObject($"Booth{id}").transform;
            booth.SetParent(parent, false);
            booth.position = new Vector3(p.x, y, p.y);
            booth.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

            var paint = _theme.RandomCoverColor(rng);

            var body = CreateBlock(booth, new Vector3(0f, 1.45f, 0f),
                                   new Vector3(2.6f, 2.9f, 2.6f), 0f, layer,
                                   "Wood", paint, 0.1f, 0f, "Body", _parkPaint);
            NoEntry(body);

            var roof = CreateBlock(booth, new Vector3(0f, 3.05f, 0f),
                                   new Vector3(3.3f, 0.22f, 3.3f), 0f, layer,
                                   "Wood", Shade(_theme.wallColor, 0.74f), 0.06f, 0f,
                                   "Roof", _parkCanvas);
            NoStanding(roof);

            // The serving window, as a dark recess rather than a painted rectangle.
            var window = CreateBlock(booth, new Vector3(0f, 1.75f, 1.32f),
                                     new Vector3(1.5f, 0.9f, 0.12f), 0f, layer,
                                     "Wood", new Color(0.05f, 0.05f, 0.06f), 0f, 0f,
                                     "Window", _parkDark);
            NoStanding(window);
        }

        // ==================================================================
        private static Material _parkTimber;
        private static Material _parkPaint;
        private static Material _parkSteel;
        private static Material _parkCanvas;
        private static Material _parkDark;
        private static Material _parkConcrete;
        private static Material _parkRubble;
        private static Material _parkPath;

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

            _parkConcrete = MakeDetailMaterial("ParkConcrete", Shade(_theme.floorColor, 1.08f),
                                               "Concrete", Metres(2.6f), 0.08f, 0f, 0.9f);
            _parkRubble = MakeDetailMaterial("ParkRubble", Shade(_theme.floorColor, 0.74f),
                                             "Rock", Metres(1.8f), 0.05f, 0f, 1.4f);
            _parkPath = MakeDetailMaterial("ParkPath", new Color(0.34f, 0.32f, 0.29f),
                                           "Concrete", Metres(3.2f), 0.08f, 0f, 0.8f);

            // What the inside of a shaft is. Not black -- a hole that is pure black reads
            // as a rendering fault, and this has to read as depth.
            _parkDark = MakeMaterialAt($"{MaterialFolder}/{SafeName(_theme.themeName)}_Void.mat",
                                       new Color(0.045f, 0.04f, 0.05f), 0f, 0f);

            ResolveFairgroundMaterials();
        }
    }
}
#endif