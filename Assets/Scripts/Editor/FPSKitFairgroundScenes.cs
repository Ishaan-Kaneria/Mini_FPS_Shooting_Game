#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The things that make the land mean something: every ride has a gate, a queue and a closed
    /// sign; every junction has a signpost; the park has a map; and a few places have a story.
    ///
    /// <b>A fairground is a set of routes to things, and the things have entrances.</b> The first
    /// two versions put rides on a lawn and drew paths near them. A visitor arrived at a ride and
    /// found a fence. Here each ride's gate is built where its walkway arrives, with the queue
    /// running in from it, the ticket booth beside it, a height gauge, a hanging chain across half
    /// of it and a sign with the ride's own pictogram on it -- a wheel, a skull, a chequered flag.
    ///
    /// <b>The stories are small and placed.</b> A camp where somebody sheltered by the hall; a
    /// sandbag barricade across its doors; a security hut at the gate with its camera still
    /// pointing at the turnstiles; a rose garden in four parterres with the beds gone to seed;
    /// pedal boats at the pond's dock, one sunk. Nothing is a random scatter: each piece stands
    /// where the thing it belongs to would have.
    ///
    /// <b>The map board is drawn from the plan itself.</b> Its paths, plazas and attractions are the
    /// real ones at 1:106, so it cannot disagree with the park.
    /// </summary>
    public partial class FPSKitSceneBuilder
    {
        private static StallSet NewSet() => new StallSet();

        // ==================================================================
        // Orchestration
        // ==================================================================
        private static void BuildScenes(Transform root, int layer, int backdrop, System.Random rng)
        {
            ResolveAttractionMaterials();
            ResolveFillMaterials();

            var group = new GameObject("Scenes").transform;
            group.SetParent(root, false);

            var set = NewSet();

            // ---- ride gates: each where its walkway arrives, turned to face the ride ----
            RideGate(group, layer, set, new Vector2(80f, 9f), 0f, 0, rng, queue: true);                  // ferris wheel
            RideGate(group, layer, set, new Vector2(-52f, -10f), 180f, 1, rng, queue: true);           // carousel
            RideGate(group, layer, set, new Vector2(-69f, -64f), 270f, 2, rng, queue: false);          // big top
            RideGate(group, layer, set, new Vector2(56f, -45f), 90f, 3, rng, queue: false);            // bumper cars, north opening
            RideGate(group, layer, set, new Vector2(56f, -59f), 90f, 3, rng, queue: false);            // bumper cars, south opening
            RideGate(group, layer, set, new Vector2(76f, -106f), 90f, 4, rng, queue: true, lane: 3);            // haunted house
            RideGate(group, layer, set, new Vector2(-60f, 83f), 0f, 5, rng, queue: false);             // hedge maze
            RideGate(group, layer, set, new Vector2(-120f, 58f), 297f, 6, rng, queue: false);          // drop tower
            RideGate(group, layer, set, new Vector2(-124f, 9f), 0f, 7, rng, queue: true);              // swing ride
            RideGate(group, layer, set, new Vector2(-117f, -93f), 192f, 8, rng, queue: false);         // kart track
            RideGate(group, layer, set, new Vector2(98f, 47f), 0f, 9, rng, queue: true, lane: 6);              // coaster

            // ---- signposts at the junctions, and the map ----
            Post(set, new Vector2(8f, 8f), 180f, new Vector2(0f, 76f), new Vector2(98f, 52f), new Vector2(80f, 26f), new Vector2(70f, -52f), new Vector2(80f, -106f), new Vector2(0f, -126f));
            Post(set, new Vector2(-8f, 8f), 180f, new Vector2(-52f, -8f), new Vector2(-68f, -64f), new Vector2(-69f, 46f), new Vector2(-124f, 7f), new Vector2(-122f, 57f), new Vector2(-114f, -90f));
            Post(set, new Vector2(7f, -120f), 180f, new Vector2(0f, 0f), new Vector2(70f, -52f), new Vector2(80f, -106f), new Vector2(-68f, -64f), new Vector2(0f, 76f));
            Post(set, new Vector2(9f, 60f), 180f, new Vector2(-60f, 84f), new Vector2(-32f, 48f), new Vector2(0f, 0f), new Vector2(0f, 76f));
            Post(set, new Vector2(-62f, 40f), 90f, new Vector2(-124f, 7f), new Vector2(-122f, 57f), new Vector2(-32f, 48f));
            Post(set, new Vector2(88f, 4f), 180f, new Vector2(80f, 26f), new Vector2(98f, 52f), new Vector2(0f, 0f), new Vector2(126f, -50f));

            MapBoard(set, new Vector2(8.6f, -119f), 180f);
            MapBoard(set, new Vector2(-11f, -11f), 180f);
            MapBoard(set, new Vector2(11f, 62f), 180f);

            EmitStalls(group, layer, "SceneGates", set);

            // ---- stories ----
            BuildGateHouse(group, layer, rng);
            BuildPlazaCarts(group, layer, rng);
            BuildRoseGarden(group, layer, backdrop, rng);
            BuildBoatDock(group, layer, rng);
            BuildCamp(group, layer, rng);
            BuildHallBarricade(group, layer, rng);
            BuildCoinHorses(group, layer, rng);
            BuildBreakRoom(group, layer, rng);
        }

        // ==================================================================
        // Ride gates
        // ==================================================================
        /// <summary>
        /// A ride's gate: two posts and a beam under a sign carrying the ride's pictogram, a hanging
        /// chain across half the way in, a height gauge, a ticket booth, and -- for the rides that
        /// have one -- a queue lane of posts and rope running in. <paramref name="yaw"/> turns the
        /// gate to face the ride; local +z is in.
        /// </summary>
        private static void RideGate(Transform parent, int layer, StallSet s, Vector2 where, float yaw, int kind,
                                     System.Random rng, bool queue, int lane = 6)
        {
            var foot = new Vector3(where.x, GroundHeightAt(where.x, where.y), where.y);
            // Local +z is towards the visitor: the sign is read from the walkway, and the queue runs back along it.
            var rot = Quaternion.Euler(0f, yaw + 180f, 0f);
            Vector3 P(float x, float y, float z) => foot + rot * new Vector3(x, y, z);

            const float Half = 3.0f;

            // Posts, beam, sign board and its frame.
            for (int side = -1; side <= 1; side += 2)
            {
                s.Steel.Tube(P(side * Half, 0f, 0f), P(side * Half, 4.3f, 0f), 0.14f, 0.11f, 6);
                s.Steel.Box(P(side * Half, 0.15f, 0f), new Vector3(0.7f, 0.3f, 0.7f), rot);
                s.Steel.Tube(P(side * Half, 4.3f, 0f), P(side * Half, 4.8f, 0f), 0.2f, 0.06f, 6);
            }

            s.Steel.Box(P(0f, 4.15f, 0f), new Vector3(2f * Half, 0.18f, 0.2f), rot);
            s.Paint.Box(P(0f, 5.2f, 0.05f), new Vector3(2f * Half - 0.5f, 1.7f, 0.14f), rot);
            s.Steel.Box(P(0f, 6.1f, 0.05f), new Vector3(2f * Half - 0.3f, 0.08f, 0.2f), rot);
            s.Steel.Box(P(0f, 4.3f, 0.05f), new Vector3(2f * Half - 0.3f, 0.08f, 0.2f), rot);

            Pictogram(s, P(0f, 5.2f, 0.17f), rot, kind);

            // Lettering that has gone: raised bars either side of the pictogram.
            for (int i = 0; i < 8; i++)
            {
                if (rng.NextDouble() < 0.25) continue;
                float x = (i < 4 ? -1f : 1f) * (1.0f + (i % 4) * 0.35f);
                s.Bone.Box(P(x, 5.2f, 0.16f), new Vector3(0.22f, Rand(rng, 0.4f, 0.7f), 0.05f), rot);
            }

            // Bunting off each post.
            for (int side = -1; side <= 1; side += 2)
                Cloth(s.CanvasA, P(side * Half, 4.2f, 0.1f), rot * new Vector3(side * -1.6f, -0.3f, 1.4f), rot * new Vector3(0f, -0.6f, 0f), 2, 1, 0.15f, rng.Next(1, 99999), 0.12f);

            // The chain across half of the way in, with its sign hanging from it.
            Cable(s.Steel, P(-Half + 0.2f, 1.0f, 0.4f), P(0.2f, 1.0f, 0.4f), 0.25f, 0.02f, 5);
            s.Paint.Box(P(-1.4f, 0.65f, 0.45f), new Vector3(0.9f, 0.5f, 0.04f), rot * Quaternion.Euler(0f, 0f, 4f));
            s.Bone.Box(P(-1.4f, 0.65f, 0.48f), new Vector3(0.7f, 0.07f, 0.03f), rot * Quaternion.Euler(0f, 0f, 24f));
            s.Bone.Box(P(-1.4f, 0.65f, 0.48f), new Vector3(0.7f, 0.07f, 0.03f), rot * Quaternion.Euler(0f, 0f, -24f));

            // The height gauge: a board with its marks, and the stop at the rider's height.
            s.Wood.Box(P(Half + 1.6f, 1.05f, 0.1f), new Vector3(0.12f, 2.1f, 0.5f), rot);
            s.Paint.Box(P(Half + 1.6f, 1.2f, 0.35f), new Vector3(0.9f, 0.06f, 0.05f), rot);
            for (int m = 0; m < 6; m++)
                s.Bone.Box(P(Half + 1.6f, 0.4f + m * 0.3f, 0.36f), new Vector3(0.4f, 0.03f, 0.03f), rot);
            s.Steel.Tube(P(Half + 1.6f, 2.1f, 0.1f), P(Half + 1.6f, 2.6f, 0.1f), 0.03f, 0.03f, 3);

            // Booth, if a gate has one on the left.
            BuildBooth(parent, layer, rng, new Vector2(P(-Half - 2.4f, 0f, 0f).x, P(-Half - 2.4f, 0f, 0f).z), $"Gate{kind}_{(int)where.x}");

            // The queue: two rows of posts with rope, a lane 3.6m wide leading in.
            if (queue)
            {
                for (int row = -1; row <= 1; row += 2)
                {
                    Vector3 prev = default;
                    for (int i = 0; i < lane; i++)
                    {
                        var post = P(row * 1.8f, 0f, 1.5f + i * 2.2f);
                        s.Steel.Tube(post, post + Vector3.up * 1.0f, 0.045f, 0.04f, 4);
                        s.Steel.Tube(post + Vector3.up * 1.0f, post + Vector3.up * 1.08f, 0.07f, 0.03f, 4);
                        if (i > 0 && rng.NextDouble() > 0.15) Cable(s.Paint, prev + Vector3.up * 0.88f, post + Vector3.up * 0.88f, 0.22f, 0.025f, 3);
                        prev = post;
                    }
                }
            }

            // A light on the gate, so each ride is findable at dusk.
            var go = new GameObject($"GateLight{kind}");
            go.transform.SetParent(parent, false);
            go.transform.position = P(0f, 3.4f, 1.2f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.74f, 0.44f);
            light.intensity = 3.4f;
            light.range = 14f;
            light.shadows = LightShadows.None;
        }

        /// <summary>The pictogram of a ride, built flat on the face of its sign.</summary>
        private static void Pictogram(StallSet s, Vector3 c, Quaternion rot, int kind)
        {
            Vector3 P(float x, float y) => c + rot * new Vector3(x, y, 0f);
            var u = rot * Vector3.right; var v = rot * Vector3.up;

            switch (kind)
            {
                case 0:   // ferris wheel: a ring and its spokes
                    Arc(s.Bone, c, u, v, 0.55f, 0f, 360f, 0.035f, 14, 3);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        s.Bone.Tube(c, c + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * 0.55f, 0.02f, 0.02f, 3);
                    }
                    break;

                case 1:   // carousel: a canopy on a pole, with its scallops
                    s.Bone.Tri(P(-0.6f, 0.1f), P(0.6f, 0.1f), P(0f, 0.65f));
                    s.Bone.Tube(P(0f, -0.6f), P(0f, 0.1f), 0.04f, 0.04f, 3);
                    for (int i = -2; i <= 2; i++) s.Bone.Box(P(i * 0.24f, 0.02f), new Vector3(0.18f, 0.12f, 0.03f), rot);
                    break;

                case 2:   // big top: a tent
                    s.Bone.Tri(P(-0.7f, -0.45f), P(0.7f, -0.45f), P(0f, 0.55f));
                    s.Bone.Box(P(0f, -0.58f), new Vector3(1.4f, 0.18f, 0.03f), rot);
                    break;

                case 3:   // bumper cars: two little cars
                    s.Bone.Box(P(-0.4f, -0.1f), new Vector3(0.6f, 0.3f, 0.03f), rot);
                    s.Bone.Box(P(0.4f, 0.1f), new Vector3(0.6f, 0.3f, 0.03f), rot * Quaternion.Euler(0f, 0f, 15f));
                    break;

                case 4:   // haunted: a skull
                    Arc(s.Prizes, c + v * 0.1f, u, v, 0.42f, 0f, 360f, 0.045f, 14, 3);
                    s.Prizes.Box(P(-0.16f, 0.15f), new Vector3(0.14f, 0.14f, 0.03f), rot);
                    s.Prizes.Box(P(0.16f, 0.15f), new Vector3(0.14f, 0.14f, 0.03f), rot);
                    for (int i = -2; i <= 2; i++) s.Prizes.Box(P(i * 0.1f, -0.42f), new Vector3(0.05f, 0.14f, 0.03f), rot);
                    break;

                case 5:   // maze: a square spiral
                    foreach (float r in new[] { 0.6f, 0.4f, 0.2f })
                    {
                        s.Bone.Box(P(0f, r), new Vector3(2f * r, 0.04f, 0.03f), rot);
                        s.Bone.Box(P(0f, -r), new Vector3(2f * r - 0.1f, 0.04f, 0.03f), rot);
                        s.Bone.Box(P(r, 0f), new Vector3(0.04f, 2f * r, 0.03f), rot);
                        s.Bone.Box(P(-r, 0.05f), new Vector3(0.04f, 2f * r - 0.2f, 0.03f), rot);
                    }
                    break;

                case 6:   // drop tower: a tall arrow pointing down
                    s.Bone.Box(P(0f, 0.15f), new Vector3(0.12f, 0.9f, 0.03f), rot);
                    s.Bone.Tri(P(-0.28f, -0.3f), P(0.28f, -0.3f), P(0f, -0.62f));
                    break;

                case 7:   // swings: a crown with three chains and seats
                    Arc(s.Bone, P(0f, 0.4f), u, v, 0.45f, 0f, 180f, 0.035f, 8, 3);
                    for (int i = -1; i <= 1; i++)
                    {
                        s.Bone.Tube(P(i * 0.3f, 0.35f), P(i * 0.3f, -0.4f), 0.015f, 0.015f, 3);
                        s.Bone.Box(P(i * 0.3f, -0.45f), new Vector3(0.18f, 0.05f, 0.03f), rot);
                    }
                    break;

                case 8:   // karts: a chequered flag
                    for (int x = 0; x < 4; x++)
                        for (int y = 0; y < 3; y++)
                            if ((x + y) % 2 == 0) s.Bone.Box(P(-0.4f + x * 0.27f, -0.25f + y * 0.27f), new Vector3(0.25f, 0.25f, 0.03f), rot);
                    s.Bone.Tube(P(-0.55f, -0.6f), P(-0.55f, 0.7f), 0.025f, 0.025f, 3);
                    break;

                default:  // coaster: a hill and a drop
                    for (int i = 0; i < 9; i++)
                    {
                        float x = -0.7f + i * 0.175f;
                        float y = Mathf.Sin(i / 8f * Mathf.PI * 1.6f) * 0.45f;
                        s.Bone.Box(P(x, y), new Vector3(0.2f, 0.06f, 0.03f), rot * Quaternion.Euler(0f, 0f, Mathf.Cos(i / 8f * Mathf.PI * 1.6f) * 35f));
                    }
                    break;
            }
        }

        // ==================================================================
        // Signposts and the map
        // ==================================================================
        /// <summary>
        /// A signpost with an arrow board for each destination, each turned to point at the thing it
        /// names. Boards alternate red and teal so two neighbouring arrows read as two.
        /// </summary>
        private static void Post(StallSet s, Vector2 where, float yaw, params Vector2[] destinations)
        {
            var foot = new Vector3(where.x, GroundHeightAt(where.x, where.y), where.y);
            s.Steel.Tube(foot, foot + Vector3.up * (2.6f + destinations.Length * 0.42f), 0.07f, 0.055f, 6);
            s.Steel.Tube(foot + Vector3.up * (2.6f + destinations.Length * 0.42f), foot + Vector3.up * (2.8f + destinations.Length * 0.42f), 0.12f, 0.04f, 6);
            s.Steel.Box(foot + Vector3.up * 0.12f, new Vector3(0.6f, 0.24f, 0.6f), Quaternion.identity);

            for (int i = 0; i < destinations.Length; i++)
            {
                var d = destinations[i] - where;
                float bearing = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                var rot = Quaternion.Euler(0f, bearing - 90f, 0f);
                var at = foot + Vector3.up * (2.4f + i * 0.42f - 0.35f);
                var set = i % 2 == 0 ? s.Paint : s.Prizes;

                var boardCentre = at + Quaternion.Euler(0f, bearing, 0f) * Vector3.forward * 0.85f;
                set.Box(boardCentre, new Vector3(1.4f, 0.32f, 0.05f), Quaternion.Euler(0f, bearing + 90f, 0f));
                var tip = at + Quaternion.Euler(0f, bearing, 0f) * Vector3.forward * 1.7f;
                var side = Quaternion.Euler(0f, bearing + 90f, 0f) * Vector3.forward * 0.2f;
                set.Tri(tip, boardCentre - Quaternion.Euler(0f, bearing, 0f) * Vector3.forward * 0.6f + side, boardCentre - Quaternion.Euler(0f, bearing, 0f) * Vector3.forward * 0.6f - side);
                set.Tri(boardCentre - Quaternion.Euler(0f, bearing, 0f) * Vector3.forward * 0.6f - side, boardCentre - Quaternion.Euler(0f, bearing, 0f) * Vector3.forward * 0.6f + side, tip);
                s.Bone.Box(boardCentre + Vector3.up * 0.01f, new Vector3(1.0f, 0.06f, 0.06f), Quaternion.Euler(0f, bearing + 90f, 0f));
            }
        }

        /// <summary>
        /// A map of the park, drawn from the plan: the walkways, the plazas and every attraction at
        /// their real places, with a red dot where the board stands. Faces south, so north is up.
        /// </summary>
        private static void MapBoard(StallSet s, Vector2 where, float yaw)
        {
            var foot = new Vector3(where.x, GroundHeightAt(where.x, where.y), where.y);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => foot + rot * new Vector3(x, y, z);

            const float W = 5.4f, H = 3.7f;
            for (int side = -1; side <= 1; side += 2)
                s.Steel.Tube(P(side * (W * 0.5f - 0.1f), 0f, 0f), P(side * (W * 0.5f - 0.1f), H + 1.1f, 0f), 0.09f, 0.08f, 5);

            s.Paint.Box(P(0f, 1.1f + H * 0.5f, 0.04f), new Vector3(W, H, 0.1f), rot);
            s.Steel.Box(P(0f, 1.1f + H + 0.06f, 0.04f), new Vector3(W + 0.2f, 0.12f, 0.2f), rot);
            s.Steel.Box(P(0f, 1.1f - 0.06f, 0.04f), new Vector3(W + 0.2f, 0.12f, 0.2f), rot);

            // The map: the park is 340m across and the board's face 3.3, so 1:103.
            float k = 3.3f / 340f;
            var origin = P(0f, 1.1f + H * 0.5f, 0.11f);

            // The board faces south, so a viewer looks north and east is on their right, which is the
            // board's local -x: the map is drawn mirrored in x for that reason and no other.
            Vector3 M(Vector2 w) => origin + rot * new Vector3(-w.x * k, w.y * k, 0f);

            // Walkways.
            foreach (var route in _fgRoutes)
            {
                var pts = ResamplePath(route.Points, 6f);
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var a = M(pts[i]); var b = M(pts[i + 1]);
                    float len = Vector3.Distance(a, b);
                    if (len < 0.002f) continue;
                    s.Bone.Box((a + b) * 0.5f, new Vector3(Mathf.Max(0.025f, route.Width * k * 0.9f), len + 0.003f, 0.02f), Quaternion.LookRotation(rot * Vector3.forward, b - a) * Quaternion.Euler(0f, 0f, 0f));
                }
            }

            // Plazas as rings, attractions as dots.
            foreach (var plaza in _fgPlazas)
                Arc(s.Bone, M(plaza.Centre), rot * Vector3.right, rot * Vector3.up, plaza.Radius * k, 0f, 360f, 0.012f, 10, 3);

            foreach (var ride in _ridePlans) s.Prizes.Box(M(ride.Centre) + rot * Vector3.forward * 0.02f, new Vector3(0.14f, 0.14f, 0.03f), rot);
            s.Prizes.Box(M(_hallPlan.Centre) + rot * Vector3.forward * 0.02f, new Vector3(0.32f, 0.2f, 0.03f), rot);
            foreach (var site in new[] { _bumperPlan, _hauntedPlan, _pondPlan, _dropPlan, _swingPlan, _mazePlan, _kartPlan, _gardenPlan })
                s.Prizes.Box(M(site.Centre) + rot * Vector3.forward * 0.02f, new Vector3(0.1f, 0.1f, 0.03f), rot);

            // You are here.
            s.Paint.Tube(M(where) + rot * Vector3.forward * 0.02f, M(where) + rot * Vector3.forward * 0.06f, 0.09f, 0.09f, 8);
        }

        // ==================================================================
        // The gate house
        // ==================================================================
        private static void BuildGateHouse(Transform parent, int layer, System.Random rng)
        {
            var s = NewSet();
            var site = _entrancePlan;

            // The security hut: a box with a counter window, a camera on a mast still pointing at the
            // turnstiles, and the barrier arm snapped.
            var hut = ParkPoint(site, 24f, 6f);
            var foot = new Vector3(hut.x, GroundHeightAt(hut.x, hut.y), hut.y);
            var rot = Quaternion.Euler(0f, site.Yaw + 90f, 0f);
            Vector3 P(float x, float y, float z) => foot + rot * new Vector3(x, y, z);

            s.Wood.Box(P(0f, 1.4f, 0f), new Vector3(3.4f, 2.8f, 2.6f), rot);
            s.Paint.Box(P(0f, 2.9f, 0f), new Vector3(3.8f, 0.2f, 3.0f), rot);
            s.Prizes.Box(P(0f, 1.7f, 1.32f), new Vector3(1.8f, 0.9f, 0.06f), rot);
            s.Steel.Tube(P(1.8f, 0f, 0.8f), P(1.8f, 4.8f, 0.8f), 0.07f, 0.06f, 5);
            s.Steel.Box(P(1.8f, 4.9f, 0.5f), new Vector3(0.3f, 0.25f, 0.5f), rot * Quaternion.Euler(18f, 0f, 0f));
            s.Steel.Tube(P(-1.9f, 1.0f, 1.8f), P(1.0f, 1.2f, 4.5f), 0.04f, 0.03f, 4);
            s.Paint.Box(P(0f, 0.2f, 1.4f), new Vector3(3.0f, 0.4f, 0.3f), rot);

            // Notice board by the gate, with its flyers.
            var nb = ParkPoint(site, -9f, 14f);
            var nfoot = new Vector3(nb.x, GroundHeightAt(nb.x, nb.y), nb.y);
            var nrot = Quaternion.Euler(0f, site.Yaw + 180f, 0f);
            Vector3 N(float x, float y, float z) => nfoot + nrot * new Vector3(x, y, z);
            for (int side = -1; side <= 1; side += 2) s.Steel.Tube(N(side * 1.6f, 0f, 0f), N(side * 1.6f, 2.8f, 0f), 0.06f, 0.05f, 4);
            s.Wood.Box(N(0f, 1.9f, 0.05f), new Vector3(3.5f, 1.7f, 0.1f), nrot);
            for (int i = 0; i < 15; i++)
            {
                float x = -1.4f + (i % 5) * 0.7f, y = 1.35f + (i / 5) * 0.55f;
                (rng.NextDouble() < 0.4 ? s.Prizes : s.Bone).Box(N(x + Rand(rng, -0.08f, 0.08f), y, 0.12f), new Vector3(0.45f, 0.4f, 0.015f),
                                                                  nrot * Quaternion.Euler(0f, 0f, Rand(rng, -9f, 9f)));
            }

            // A wreath and a few candles stood under it: somebody did not want to forget.
            s.CanvasA.Tube(N(0f, 0.2f, 0.3f), N(0f, 0.28f, 0.3f), 0.35f, 0.35f, 10);
            for (int i = 0; i < 4; i++) s.Bone.Tube(N(-0.5f + i * 0.34f, 0f, 0.6f), N(-0.5f + i * 0.34f, 0.18f, 0.6f), 0.04f, 0.04f, 4);

            EmitStalls(parent, layer, "GateHouse", s);

            var go = new GameObject("CandleLight");
            go.transform.SetParent(parent, false);
            go.transform.position = N(0f, 0.5f, 0.7f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.28f);
            light.intensity = 1.6f;
            light.range = 6f;
            light.shadows = LightShadows.None;
        }

        // ==================================================================
        // Carts on the plaza
        // ==================================================================
        private static void BuildPlazaCarts(Transform parent, int layer, System.Random rng)
        {
            var s = NewSet();
            var balloons = new MeshBuild { UVScale = 0.5f };

            void Cart(Vector2 at, float yaw, int kind)
            {
                var foot = new Vector3(at.x, GroundHeightAt(at.x, at.y), at.y);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                Vector3 P(float x, float y, float z) => foot + rot * new Vector3(x, y, z);

                s.Paint.Box(P(0f, 0.85f, 0f), new Vector3(1.7f, 0.9f, 1.1f), rot);
                s.Wood.Box(P(0f, 1.34f, 0f), new Vector3(1.8f, 0.08f, 1.2f), rot);
                for (int side = -1; side <= 1; side += 2)
                    s.Steel.Tube(P(side * 0.95f, 0.4f, 0.2f), P(side * 1.05f, 0.4f, 0.2f), 0.4f, 0.4f, 10);
                s.Steel.Tube(P(-0.6f, 0.8f, -0.55f), P(-1.3f, 0.7f, -0.9f), 0.03f, 0.03f, 3);

                if (kind == 0)       // popcorn: a glass box on top, a kettle, a scoop
                {
                    s.Prizes.Box(P(0f, 1.8f, 0f), new Vector3(1.4f, 0.8f, 0.9f), rot);
                    s.Steel.Tube(P(0.5f, 1.4f, 0.4f), P(0.5f, 1.9f, 0.4f), 0.2f, 0.2f, 8);
                    s.Steel.Tube(P(0f, 2.3f, 0f), P(0f, 3.2f, 0f), 0.025f, 0.025f, 3);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        s.CanvasA.Tri(P(0f, 3.2f, 0f), P(Mathf.Cos(a) * 1.5f, 2.4f, Mathf.Sin(a) * 1.5f), P(Mathf.Cos(a + 1.0f) * 1.5f, 2.4f, Mathf.Sin(a + 1.0f) * 1.5f));
                    }
                }
                else if (kind == 1)  // candy floss: a drum under a dome, with its paper cones in a rack
                {
                    s.Steel.Tube(P(0f, 1.38f, 0f), P(0f, 1.5f, 0f), 0.6f, 0.6f, 12);
                    s.Prizes.Tube(P(0f, 1.5f, 0f), P(0f, 2.0f, 0f), 0.6f, 0.4f, 12);
                    for (int i = 0; i < 5; i++) s.Bone.Tri(P(0.6f + i * 0.08f, 1.38f, -0.3f), P(0.75f + i * 0.08f, 1.38f, -0.3f), P(0.67f + i * 0.08f, 1.9f, -0.3f));
                }
                else                 // balloons: the bunch, tied to the cart and tangled in whatever is overhead
                {
                    for (int i = 0; i < 16; i++)
                    {
                        var top = P(Rand(rng, -0.8f, 0.8f), Rand(rng, 3.4f, 5.4f), Rand(rng, -0.8f, 0.8f));
                        balloons.Tube(top - Vector3.up * 0.28f, top + Vector3.up * 0.3f, 0.2f, 0.07f, 6);
                        balloons.Tube(top - Vector3.up * 0.28f, P(0f, 1.4f, 0f), 0.006f, 0.006f, 3);
                    }
                }
            }

            Cart(new Vector2(-12f, -14f), 20f, 0);
            Cart(new Vector2(6f, -19f), -25f, 1);
            Cart(new Vector2(12f, 13f), 200f, 2);

            // A photo booth by the bandstand: a box, a curtain and a lamp over it.
            var pb = new Vector2(-12f, 8f);
            var pfoot = new Vector3(pb.x, GroundHeightAt(pb.x, pb.y), pb.y);
            s.Paint.Box(pfoot + new Vector3(0f, 1.3f, 0f), new Vector3(1.5f, 2.6f, 1.5f), Quaternion.identity);
            s.CanvasB.Box(pfoot + new Vector3(0f, 1.1f, 0.78f), new Vector3(1.1f, 1.7f, 0.05f), Quaternion.identity);
            s.Prizes.Box(pfoot + new Vector3(0f, 2.8f, 0.5f), new Vector3(1.0f, 0.5f, 0.1f), Quaternion.identity);

            EmitStalls(parent, layer, "PlazaCarts", s);
            RideDecor(parent, "PlazaBalloons", balloons, _rideCanvasRed);
        }

        // ==================================================================
        // The rose garden
        // ==================================================================
        /// <summary>
        /// Four parterres round a fountain, their hedges low and square with a gap in the middle of every
        /// side, their beds ringed in colour that has gone to seed, a statue at the head of each walk and
        /// a bench facing it. The cross walks are real walkways: the garden is somewhere you pass through.
        /// </summary>
        private static void BuildRoseGarden(Transform parent, int layer, int backdrop, System.Random rng)
        {
            var site = _gardenPlan;
            var hedge = new MeshBuild { UVScale = 0.4f };
            var beds = new MeshBuild { UVScale = 0.4f };
            var bloomRed = new MeshBuild { UVScale = 0.4f };
            var bloomYellow = new MeshBuild { UVScale = 0.4f };
            var bloomViolet = new MeshBuild { UVScale = 0.4f };
            var stones = new MeshBuild { UVScale = 0.5f };
            var weeds = new MeshBuild { UVScale = 0.5f };
            var wood = new MeshBuild { UVScale = 0.5f };

            for (int qx = -1; qx <= 1; qx += 2)
                for (int qz = -1; qz <= 1; qz += 2)
                {
                    var c = site.Centre + new Vector2(qx * 10.5f, qz * 10.5f);
                    const float R = 5.5f;

                    // The hedge square, each side in two lengths with a gap between.
                    for (int side = 0; side < 4; side++)
                    {
                        float ang = side * 90f;
                        var rot = Quaternion.Euler(0f, ang, 0f);
                        for (int part = -1; part <= 1; part += 2)
                        {
                            var local = new Vector3(part * 3.7f, 0f, R);
                            var w = rot * local;
                            var p = new Vector2(c.x + w.x, c.y + w.z);
                            float g = GroundHeightAt(p.x, p.y);
                            if (rng.NextDouble() < 0.1) continue;
                            hedge.Box(new Vector3(p.x, g + 0.4f, p.y), new Vector3(3.2f, 0.8f, 0.7f), rot);
                            LeafBlob(hedge, new Vector3(p.x, g + 0.85f, p.y), 0.45f, rng.Next(1, 99999));
                        }
                    }

                    // The beds inside: three nested squares of blooms round a centre stone.
                    for (int ring = 0; ring < 3; ring++)
                    {
                        float r = 1.0f + ring * 1.3f;
                        var build = ring == 0 ? bloomRed : ring == 1 ? bloomYellow : bloomViolet;
                        int per = 8 + ring * 6;
                        for (int i = 0; i < per; i++)
                        {
                            float t = i / (float)per * 4f;
                            int sd = Mathf.FloorToInt(t); float f = t - sd;
                            Vector2 corner(int k) => new Vector2((k == 0 || k == 3) ? -r : r, k < 2 ? -r : r);
                            var p0 = corner(sd % 4); var p1 = corner((sd + 1) % 4);
                            var q = c + Vector2.Lerp(p0, p1, f);
                            if (rng.NextDouble() < 0.2) continue;
                            float g = GroundHeightAt(q.x, q.y);
                            beds.Tube(new Vector3(q.x, g, q.y), new Vector3(q.x, g + 0.35f, q.y), 0.02f, 0.02f, 3);
                            build.Box(new Vector3(q.x, g + 0.42f, q.y), new Vector3(0.24f, 0.2f, 0.24f), Quaternion.Euler(0f, Rand(rng, 0f, 90f), 0f));
                        }
                    }

                    stones.Box(new Vector3(c.x, GroundHeightAt(c.x, c.y) + 0.25f, c.y), new Vector3(0.8f, 0.5f, 0.8f), Quaternion.Euler(0f, 20f, 0f));
                    Tuft(weeds, new Vector3(c.x + 0.8f, GroundHeightAt(c.x, c.y), c.y), Rand(rng, 0.8f, 1.3f), rng.Next(1, 99999));
                }

            // The fountain in the middle and a bench facing each walk.
            var fc = new Vector3(site.Centre.x, GroundHeightAt(site.Centre.x, site.Centre.y), site.Centre.y);
            var fg = new GameObject("GardenFountain").transform;
            fg.SetParent(parent, false);
            fg.position = fc;
            HallFountain(fg, layer, Vector3.zero, rng);

            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad;
                var p = site.Centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 8f + new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * 3f;
                Bench(wood, new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y), -a * Mathf.Rad2Deg + 90f, rng.NextDouble() < 0.2, rng);
            }

            var g1 = new GameObject("RoseGarden").transform;
            g1.SetParent(parent, false);
            var gHedge = MeshObject(g1, "GardenHedges", ToMesh(hedge, DenseKey("gardenhedges")), _hedgeMat, Vector3.zero, Quaternion.identity, Vector3.one, layer, "Wood");
            NoStanding(gHedge); Hide(gHedge);
            RideDecor(g1, "GardenStems", beds, _fgFern);
            RideDecor(g1, "GardenRed", bloomRed, _parkPaint, false);
            RideDecor(g1, "GardenYellow", bloomYellow, _fgBrass, false);
            RideDecor(g1, "GardenViolet", bloomViolet, _midwayPaintB, false);
            RideSolid(g1, layer, "GardenStones", stones, _parkRubble, "Concrete");
            RideSolid(g1, layer, "GardenBenches", wood, _parkTimber, "Wood");
            EmitFoliage(g1, backdrop, "GardenWeeds", weeds, _fgFern);
        }

        // ==================================================================
        // The boat dock
        // ==================================================================
        /// <summary>
        /// Pedal swans at a pier on the pond: a white hull, a neck and a head, a canopy; two still tied up,
        /// one sunk to the gunwale and one upside down, with a lifebelt on a post.
        /// </summary>
        private static void BuildBoatDock(Transform parent, int layer, System.Random rng)
        {
            var s = NewSet();
            var site = _pondPlan;
            var dock = new Vector2(-69f, 46f);
            float g = GroundHeightAt(dock.x, dock.y);

            // The pier: a deck on piles running west into the water, with a rail down one side.
            for (int i = 0; i < 8; i++)
            {
                s.Wood.Box(new Vector3(dock.x - 0.8f - i * 0.9f, g + 0.45f, dock.y), new Vector3(0.82f, 0.08f, 2.6f), Quaternion.identity);
                if (i % 2 == 0)
                    for (int side = -1; side <= 1; side += 2)
                        s.Steel.Tube(new Vector3(dock.x - 0.8f - i * 0.9f, g - 0.4f, dock.y + side * 1.2f), new Vector3(dock.x - 0.8f - i * 0.9f, g + 1.05f, dock.y + side * 1.2f), 0.07f, 0.06f, 4);
            }

            s.Steel.Tube(new Vector3(dock.x - 0.8f, g + 1.0f, dock.y + 1.25f), new Vector3(dock.x - 7.4f, g + 1.0f, dock.y + 1.25f), 0.03f, 0.03f, 3);
            s.Paint.Tube(new Vector3(dock.x - 7.6f, g + 0.6f, dock.y - 1.2f), new Vector3(dock.x - 7.6f, g + 0.66f, dock.y - 1.2f), 0.34f, 0.34f, 12);

            // The swans.
            var boats = new MeshBuild { UVScale = 0.5f };
            var trim = new MeshBuild { UVScale = 0.5f };
            void Swan(Vector3 at, float yaw, float roll, float sink)
            {
                var rot = Quaternion.Euler(0f, yaw, roll);
                Vector3 P(float x, float y, float z) => at + rot * new Vector3(x, y - sink, z);
                boats.Tube(P(0f, 0.25f, -1.1f), P(0f, 0.3f, 1.0f), 0.55f, 0.5f, 8);
                boats.Tube(P(0f, 0.5f, 0.9f), P(0f, 1.5f, 1.4f), 0.16f, 0.12f, 5);
                boats.Tube(P(0f, 1.5f, 1.4f), P(0f, 1.62f, 1.75f), 0.12f, 0.09f, 5);
                trim.Box(P(0f, 1.57f, 1.9f), new Vector3(0.1f, 0.06f, 0.24f), rot);
                trim.Box(P(0f, 1.3f, -0.4f), new Vector3(1.0f, 0.06f, 0.9f), rot * Quaternion.Euler(-4f, 0f, 0f));
                trim.Tube(P(-0.4f, 0.5f, -0.4f), P(-0.4f, 1.3f, -0.4f), 0.03f, 0.03f, 3);
                trim.Tube(P(0.4f, 0.5f, -0.4f), P(0.4f, 1.3f, -0.4f), 0.03f, 0.03f, 3);
            }

            // Lobe 3 of the pond is the nearest to the dock.
            var lake = new Vector3(site.Centre.x + 8.2f, g + 0.1f, site.Centre.y - 0.9f);
            Swan(lake + new Vector3(-1.8f, 0f, 2.6f), 80f, 0f, 0f);
            Swan(lake + new Vector3(-1.2f, 0f, -3.0f), 98f, 0f, 0f);
            Swan(lake + new Vector3(-3.0f, 0f, 0f), 20f, 0f, 0.5f);
            Swan(lake + new Vector3(0.5f, 0.1f, 0.2f), 140f, 180f, 0.3f);

            RideDecor(parent, "DockPier", s.Wood, _parkTimber);
            RideDecor(parent, "DockPiles", s.Steel, _parkSteel);
            RideDecor(parent, "DockLifebelt", s.Paint, _rideRed);
            RideDecor(parent, "PedalSwans", boats, _hhBone);
            RideDecor(parent, "PedalSwanTrim", trim, _rideRed);
        }

        // ==================================================================
        // The camp
        // ==================================================================
        /// <summary>
        /// Where somebody sheltered: three tents, a fire ring with embers still warm, log seats, a
        /// washing line, a lean-to of tarpaulin and a trolley of salvage. A warm light, and nothing
        /// else warm for a hundred metres.
        /// </summary>
        private static void BuildCamp(Transform parent, int layer, System.Random rng)
        {
            var site = _campPlan;
            var c = new Vector3(site.Centre.x, GroundHeightAt(site.Centre.x, site.Centre.y), site.Centre.y);
            var steel = new MeshBuild { UVScale = 0.5f };
            var cloth = new MeshBuild { UVScale = 0.4f };
            var clothB = new MeshBuild { UVScale = 0.4f };
            var wood = new MeshBuild { UVScale = 0.5f };
            var embers = new MeshBuild { UVScale = 0.4f };
            var stones = new MeshBuild { UVScale = 0.5f };

            // Fire ring.
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f;
                stones.Box(c + new Vector3(Mathf.Cos(a) * 0.8f, 0.14f, Mathf.Sin(a) * 0.8f), new Vector3(0.38f, 0.28f, 0.3f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f));
            }

            embers.Tube(c + Vector3.up * 0.05f, c + Vector3.up * 0.16f, 0.6f, 0.45f, 8);
            for (int i = 0; i < 4; i++) wood.Tube(c + new Vector3(0f, 0.3f, 0f), c + new Vector3(Mathf.Cos(i * 1.7f) * 0.8f, 0.4f, Mathf.Sin(i * 1.7f) * 0.8f), 0.07f, 0.06f, 4);

            // Tents: ridge, two canvas slopes and the poles.
            for (int t = 0; t < 3; t++)
            {
                float a = t * 2.2f + 0.4f;
                var at = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 5.2f;
                var rot = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
                Vector3 P(float x, float y, float z) => at + rot * new Vector3(x, y, z);
                var build = t == 1 ? clothB : cloth;

                steel.Tube(P(0f, 0f, -1.5f), P(0f, 1.8f, -1.5f), 0.03f, 0.03f, 3);
                steel.Tube(P(0f, 0f, 1.5f), P(0f, 1.8f, 1.5f), 0.03f, 0.03f, 3);
                steel.Tube(P(0f, 1.8f, -1.5f), P(0f, 1.8f, 1.5f), 0.025f, 0.025f, 3);
                foreach (int side in new[] { -1, 1 })
                {
                    build.Quad(P(side * 1.3f, 0f, -1.5f), P(0f, 1.8f, -1.5f), P(0f, 1.8f, 1.5f), P(side * 1.3f, 0f, 1.5f));
                    build.Quad(P(side * 1.3f, 0f, 1.5f), P(0f, 1.8f, 1.5f), P(0f, 1.8f, -1.5f), P(side * 1.3f, 0f, -1.5f));
                }
            }

            // Log seats, a lean-to, a washing line and the trolley.
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.6f + 0.2f;
                wood.Tube(c + new Vector3(Mathf.Cos(a) * 2.2f - 0.5f, 0.2f, Mathf.Sin(a) * 2.2f), c + new Vector3(Mathf.Cos(a) * 2.2f + 0.5f, 0.2f, Mathf.Sin(a) * 2.2f), 0.2f, 0.2f, 7);
            }

            Cloth(clothB, c + new Vector3(3f, 2.4f, -6.4f), Vector3.right * 4f, new Vector3(0f, -1.7f, -1.6f), 4, 2, 0.2f, 12, 0.05f);
            steel.Tube(c + new Vector3(3f, 0f, -6.4f), c + new Vector3(3f, 2.4f, -6.4f), 0.04f, 0.04f, 3);
            steel.Tube(c + new Vector3(7f, 0f, -6.4f), c + new Vector3(7f, 2.4f, -6.4f), 0.04f, 0.04f, 3);

            steel.Tube(c + new Vector3(-6f, 2.4f, 2f), c + new Vector3(-6f, 0f, 2f), 0.04f, 0.04f, 3);
            steel.Tube(c + new Vector3(-6f, 2.4f, 7f), c + new Vector3(-6f, 0f, 7f), 0.04f, 0.04f, 3);
            Cable(steel, c + new Vector3(-6f, 2.3f, 2f), c + new Vector3(-6f, 2.3f, 7f), 0.4f, 0.01f, 5);
            for (int i = 0; i < 4; i++)
                Cloth(cloth, c + new Vector3(-6f, 2.28f, 2.6f + i * 1.1f), new Vector3(0f, 0f, 0.6f), new Vector3(0f, -0.9f, 0f), 1, 1, 0.05f, 50 + i, 0f);

            steel.Box(c + new Vector3(4f, 0.55f, 5f), new Vector3(0.7f, 0.12f, 1.1f), Quaternion.Euler(0f, 30f, 0f));
            steel.Tube(c + new Vector3(3.6f, 0.55f, 5.4f), c + new Vector3(3.6f, 1.2f, 5.4f), 0.03f, 0.03f, 3);
            for (int i = 0; i < 3; i++) wood.Box(c + new Vector3(3.9f + i * 0.1f, 0.8f + i * 0.3f, 5f), new Vector3(0.5f, 0.3f, 0.5f), Quaternion.Euler(0f, 30f + i * 10f, 0f));

            var g = new GameObject("Camp").transform;
            g.SetParent(parent, false);
            RideSolid(g, layer, "CampSteel", steel, _parkSteel);
            RideDecor(g, "CampTentsA", cloth, _hallTarp);
            RideDecor(g, "CampTentsB", clothB, _rideCanvasRed);
            RideSolid(g, layer, "CampWood", wood, _parkTimber, "Wood");
            RideSolid(g, layer, "CampStones", stones, _parkRubble, "Concrete");
            RideDecor(g, "CampEmbers", embers, _fgBulb, false);

            var go = new GameObject("CampFire");
            go.transform.SetParent(g, false);
            go.transform.position = c + Vector3.up * 0.8f;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.5f, 0.2f);
            light.intensity = 5f;
            light.range = 15f;
            light.shadows = LightShadows.None;
        }

        // ==================================================================
        // The hall's barricade
        // ==================================================================
        /// <summary>
        /// Two crescents of sandbags across the forecourt, with overturned benches and drums between
        /// them, leaving the door lane clear. Somebody held the hall; the line they held is still here.
        /// </summary>
        private static void BuildHallBarricade(Transform parent, int layer, System.Random rng)
        {
            var bags = new MeshBuild { UVScale = 0.5f };
            var junk = new MeshBuild { UVScale = 0.5f };

            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 9; i++)
                {
                    float arc = (i - 4f) * 0.34f;
                    float x = side * (9.5f + Mathf.Sin(arc) * 3.8f);
                    float z = 70f + (1f - Mathf.Cos(arc)) * -3f;
                    var rot = Quaternion.Euler(0f, side * arc * 55f + 90f, 0f);
                    float g = GroundHeightAt(x, z);

                    for (int row = 0; row < 3; row++)
                    {
                        if (row > 0 && rng.NextDouble() < 0.18 * row) continue;
                        bags.Box(new Vector3(x, g + 0.18f + row * 0.34f, z), new Vector3(0.9f, 0.34f, 0.5f), rot * Quaternion.Euler(0f, Rand(rng, -6f, 6f) + row * 20f, 0f));
                    }
                }

            // Benches on their backs and drums where the crescents end.
            for (int i = 0; i < 4; i++)
            {
                float x = (i < 2 ? -1f : 1f) * (4f + (i % 2) * 14f), z = 71f + (i % 2) * 1.5f;
                Bench(junk, new Vector3(x, GroundHeightAt(x, z), z), Rand(rng, 0f, 360f), true, rng);
            }

            for (int i = 0; i < 5; i++)
            {
                float x = Rand(rng, -16f, 16f); if (Mathf.Abs(x) < 4.5f) continue;
                var at = new Vector3(x, GroundHeightAt(x, 69.5f), 69.5f);
                junk.Tube(at, at + Vector3.up * 0.9f, 0.3f, 0.3f, 8);
            }

            var g1 = new GameObject("HallBarricade").transform;
            g1.SetParent(parent, false);
            RideSolid(g1, layer, "Sandbags", bags, _parkCanvas, "Concrete");
            RideSolid(g1, layer, "BarricadeJunk", junk, _parkTimber, "Wood");
        }

        // ==================================================================
        // Coin horses at the carousel
        // ==================================================================
        private static void BuildCoinHorses(Transform parent, int layer, System.Random rng)
        {
            var horses = new MeshBuild { UVScale = 0.5f };
            var poles = new MeshBuild { UVScale = 0.5f };
            var bases = new MeshBuild { UVScale = 0.5f };

            for (int i = 0; i < 6; i++)
            {
                float a = (i < 3 ? 100f + i * 40f : 280f + (i - 3) * 30f) * Mathf.Deg2Rad;
                float r = 18.4f;
                var c = new Vector2(-52f + Mathf.Cos(a) * r, -28f + Mathf.Sin(a) * r);
                float g = GroundHeightAt(c.x, c.y);
                bases.Box(new Vector3(c.x, g + 0.2f, c.y), new Vector3(1.4f, 0.4f, 0.8f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f));
                Horse(horses, poles, new Vector3(c.x, g + 0.4f, c.y), -a * Mathf.Rad2Deg + 90f + (rng.NextDouble() < 0.3 ? 180f : 0f), 0.6f, rng.NextDouble() < 0.3, 0.01f, 80 + i);
            }

            var group = new GameObject("CoinHorses").transform;
            group.SetParent(parent, false);
            RideSolid(group, layer, "CoinHorseBases", bases, _rideCream, "Wood");
            RideSolid(group, layer, "CoinHorses", horses, _rideRed, "Wood");
        }

        // ==================================================================
        // The break room in the yard
        // ==================================================================
        private static void BuildBreakRoom(Transform parent, int layer, System.Random rng)
        {
            var site = _maintenancePlan;
            var at = ParkPoint(site, 3f, -18f);
            var foot = new Vector3(at.x, GroundHeightAt(at.x, at.y), at.y);
            var rot = Quaternion.Euler(0f, site.Yaw, 0f);
            var room = new GameObject("BreakRoom").transform;
            room.SetParent(parent, false);
            room.position = foot;
            room.localRotation = rot;

            const float W = 3.2f, D = 2.2f, H = 2.8f, T = 0.2f;
            var mat = _parkConcrete;

            void Wall(string name, bool alongX, float fixedCoord, float from, float to, float openC, float openW)
            {
                void Solid(float a, float b, float y0, float y1)
                {
                    if (b - a < 0.05f) return;
                    float mid = (a + b) * 0.5f;
                    var pos = alongX ? new Vector3(mid, (y0 + y1) * 0.5f, fixedCoord) : new Vector3(fixedCoord, (y0 + y1) * 0.5f, mid);
                    var size = alongX ? new Vector3(b - a, y1 - y0, T) : new Vector3(T, y1 - y0, b - a);
                    NoStanding(CreateBlock(room, pos, size, 0f, layer, "Metal", Color.grey, 0.1f, 0.2f, name, mat));
                }

                if (openW <= 0f) { Solid(from, to, 0f, H); return; }
                Solid(from, openC - openW * 0.5f, 0f, H);
                Solid(openC - openW * 0.5f, openC + openW * 0.5f, 2.4f, H);
                Solid(openC + openW * 0.5f, to, 0f, H);
            }

            Wall("BreakFront", true, D, -W, W, -0.6f, 1.6f);
            Wall("BreakBack", true, -D, -W, W, 1.2f, 1.6f);
            Wall("BreakLeft", false, -W, -D, D, 0f, 0f);
            Wall("BreakRight", false, W, -D, D, 0.3f, 1.4f);

            var s = NewSet();
            for (int i = 0; i < 4; i++) s.Steel.Box(new Vector3(-W + 0.4f + i * 0.5f, 1.0f, -D + 0.35f), new Vector3(0.45f, 2.0f, 0.4f), Quaternion.identity);
            s.Wood.Box(new Vector3(0.5f, 0.45f, -0.2f), new Vector3(1.8f, 0.08f, 0.9f), Quaternion.identity);
            s.Steel.Tube(new Vector3(0.5f, 0f, -0.2f), new Vector3(0.5f, 0.4f, -0.2f), 0.06f, 0.06f, 4);
            for (int i = 0; i < 3; i++) s.Wood.Box(new Vector3(-0.2f + i * 0.7f, 0.25f, 0.6f), new Vector3(0.4f, 0.5f, 0.4f), Quaternion.Euler(0f, i * 17f, i == 2 ? 80f : 0f));
            s.Paint.Box(new Vector3(W - 0.15f, 1.5f, -0.3f), new Vector3(0.05f, 0.9f, 1.4f), Quaternion.identity);
            for (int i = 0; i < 6; i++) s.Bone.Box(new Vector3(W - 0.2f, 1.2f + (i / 3) * 0.4f, -0.8f + (i % 3) * 0.5f), new Vector3(0.02f, 0.3f, 0.35f), Quaternion.identity);
            s.Paint.Box(new Vector3(0f, H + 0.1f, 0f), new Vector3(2f * W + 0.4f, 0.2f, 2f * D + 0.4f), Quaternion.identity);
            EmitStalls(room, layer, "BreakRoomFit", s);

            // A row of service carts at the yard's front.
            var carts = new MeshBuild { UVScale = 0.5f };
            var roofs = new MeshBuild { UVScale = 0.5f };
            for (int i = 0; i < 3; i++)
            {
                var p = ParkPoint(site, 8f + i * 3.2f, 17f);
                var f = new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y);
                var r2 = Quaternion.Euler(0f, site.Yaw + 90f + Rand(rng, -6f, 6f), 0f);
                Vector3 Q(float x, float y, float z) => f + r2 * new Vector3(x, y, z);
                carts.Box(Q(0f, 0.55f, 0f), new Vector3(1.2f, 0.4f, 2.4f), r2);
                carts.Box(Q(0f, 0.95f, -0.9f), new Vector3(1.1f, 0.5f, 0.5f), r2);
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        carts.Tube(Q(sx * 0.6f, 0.25f, sz * 0.8f), Q(sx * 0.7f, 0.25f, sz * 0.8f), 0.25f, 0.25f, 8);
                        roofs.Tube(Q(sx * 0.55f, 0.7f, sz * 0.85f), Q(sx * 0.55f, 2.0f, sz * 0.85f), 0.03f, 0.03f, 3);
                    }

                roofs.Box(Q(0f, 2.05f, 0f), new Vector3(1.4f, 0.08f, 2.6f), r2);
            }

            RideSolid(parent, layer, "ServiceCarts", carts, _rideBlue);
            RideSolid(parent, layer, "ServiceCartRoofs", roofs, _parkSteel);
        }
    }
}
#endif
