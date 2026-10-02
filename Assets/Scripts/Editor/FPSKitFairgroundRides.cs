#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The big rides: the ferris wheel, the roller coaster and the carousel. Each was a
    /// stack of stretched boxes; each is now built the way the real thing is -- a lattice
    /// of members, a rim of arcs, a spline of track -- and each is <b>broken</b>: a rim with
    /// a quarter gone, a coaster whose track fell off its supports, a carousel whose roof
    /// has come down onto the platform.
    ///
    /// <b>Two things are walkable, on purpose.</b> The coaster's station platform and the
    /// carousel deck are real decks with real stairs to them (the carousel's three, the
    /// station's two), so a ride is something you fight <i>on</i> and not only round. Everything
    /// else -- every rim, spoke, rail and canopy -- is held off the navigation bake, because a
    /// flat strip of rail forty metres up is exactly the surface that bakes as ground nobody can
    /// reach.
    ///
    /// <b>What blocks, and what does not.</b> Legs, supports and fallen wreckage are solid:
    /// they are cover. Rims, cables, spokes and canvas are not: a player cannot be stopped by
    /// a wire, and the physics scene stays small.
    /// </summary>
    public partial class FPSKitSceneBuilder
    {
        private static int BackdropLayer()
        {
            int layer = LayerMask.NameToLayer("Backdrop");
            return layer < 0 ? LayerMask.NameToLayer("Environment") : layer;
        }

        /// <summary>Part of a ride that is cover: solid, never stood on.</summary>
        private static GameObject RideSolid(Transform parent, int layer, string name, MeshBuild build, Material material,
                                            string tag = "Metal")
        {
            if (build.Triangles.Count == 0) return null;

            var go = MeshObject(parent, name, ToMesh(build, DenseKey(name.ToLowerInvariant())), material,
                                Vector3.zero, Quaternion.identity, Vector3.one, layer, tag);
            NoStanding(go);
            Hide(go);
            return go;
        }

        /// <summary>Part of a ride that is for looking at: no collider, off the bake.</summary>
        private static GameObject RideDecor(Transform parent, string name, MeshBuild build, Material material,
                                            bool shadows = true)
        {
            if (build.Triangles.Count == 0) return null;

            var go = MeshObject(parent, name, ToMesh(build, DenseKey(name.ToLowerInvariant())), material,
                                Vector3.zero, Quaternion.identity, Vector3.one, BackdropLayer(), null, collider: false);
            if (!shadows) go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            NoStanding(go);
            Hide(go);
            return go;
        }

        private static Material _rideRed, _rideCream, _rideBrass, _rideRust, _rideCanvasRed, _rideCanvasCream, _rideBlue;

        private static void ResolveRideMaterials()
        {
            _rideRed = MakeDetailMaterial("RideRed", new Color(0.50f, 0.13f, 0.12f), "Timber", Metres(1.8f), 0.18f, 0f, 0.6f);
            _rideCream = MakeDetailMaterial("RideCream", new Color(0.62f, 0.56f, 0.44f), "Timber", Metres(1.8f), 0.14f, 0f, 0.6f);
            _rideBlue = MakeDetailMaterial("RideBlue", new Color(0.13f, 0.27f, 0.36f), "Timber", Metres(1.8f), 0.16f, 0f, 0.6f);
            _rideBrass = MakeDetailMaterial("RideBrass", new Color(0.50f, 0.38f, 0.16f), "Metal", Metres(1.2f), 0.5f, 0.7f);
            _rideRust = MakeDetailMaterial("RideRust", new Color(0.36f, 0.22f, 0.15f), "Metal", Metres(1.4f), 0.14f, 0.5f);
            _rideCanvasRed = MakeDetailMaterial("CanvasRed", new Color(0.52f, 0.17f, 0.14f), "Adobe", Metres(2.5f), 0.04f, 0f, 0.8f);
            _rideCanvasCream = MakeDetailMaterial("CanvasCream", new Color(0.66f, 0.60f, 0.48f), "Adobe", Metres(2.5f), 0.04f, 0f, 0.8f);
        }

        /// <summary>The rides and their neighbours, each at the site the plan reserved for it.</summary>
        private static void BuildRides(Transform root, int layer, System.Random rng)
        {
            ResolveRideMaterials();

            var group = new GameObject("Rides").transform;
            group.SetParent(root, false);

            foreach (var plan in _ridePlans)
            {
                var at = new Vector3(plan.Centre.x, GroundHeightAt(plan.Centre.x, plan.Centre.y), plan.Centre.y);

                switch (plan.Kind)
                {
                    case 0: BuildFerrisWheel(group, layer, rng, at); break;
                    case 1: BuildBigTop(group, layer, rng, at); break;
                    case 2: BuildCarousel(group, layer, rng, at); break;
                    default: BuildCoaster(group, layer, rng, at); break;
                }
            }

            BuildDropTower(group, layer, new System.Random(_theme.randomSeed + 211), _dropPlan);
            BuildSwingRide(group, layer, new System.Random(_theme.randomSeed + 223), _swingPlan);
            BuildHauntedHouse(group, layer, new System.Random(_theme.randomSeed + 227), _hauntedPlan);
            BuildHedgeMaze(group, layer, new System.Random(_theme.randomSeed + 229), _mazePlan);
            BuildClockTower(group, layer, new System.Random(_theme.randomSeed + 233), _clockPlan);
            BuildBandstand(group, layer, new System.Random(_theme.randomSeed + 239), new Vector2(-19f, 17f));
        }

        // ==================================================================
        // The ferris wheel
        // ==================================================================
        /// <summary>
        /// The tallest thing on the map, and therefore the compass.
        ///
        /// Two rims, one each side of the cabins, joined by cross-rods; sixteen spokes a side;
        /// a rim with a quarter of its arc gone, the broken ends bent out, and the missing arc
        /// lying on the ground below it. The cabins are the park's own paint, and most are
        /// gone -- the ones left hang true, and a few have slipped off one hook.
        /// </summary>
        private static void BuildFerrisWheel(Transform parent, int layer, System.Random rng, Vector3 at)
        {
            var wheel = new GameObject("FerrisWheel").transform;
            wheel.SetParent(parent, false);
            wheel.position = at;
            wheel.localRotation = Quaternion.Euler(0f, 24f, 0f);

            const float R = 22f, hubY = 25.5f, half = 3.4f;

            var frame = new MeshBuild { UVScale = 0.5f };
            var rim = new MeshBuild { UVScale = 0.5f };
            var cabins = new MeshBuild { UVScale = 0.5f };
            var canopy = new MeshBuild { UVScale = 0.5f };
            var wreck = new MeshBuild { UVScale = 0.5f };
            var wreckCanopy = new MeshBuild { UVScale = 0.5f };
            var ground = new MeshBuild { UVScale = 0.5f };

            var right = Vector3.right;
            var up = Vector3.up;

            // ---- A-frames: two legs a side, braced, to the axle bearing ----
            for (int sz = -1; sz <= 1; sz += 2)
            {
                var bearing = new Vector3(0f, hubY, sz * half);
                var footA = new Vector3(-13f, 0f, sz * 7.5f);
                var footB = new Vector3(13f, 0f, sz * 7.5f);

                foreach (var foot in new[] { footA, footB })
                {
                    frame.Tube(foot, bearing, 0.55f, 0.38f, 6);

                    // Lattice braces up each leg: a rung every four metres.
                    for (float t = 0.16f; t < 0.95f; t += 0.16f)
                    {
                        var p = Vector3.Lerp(foot, bearing, t);
                        var q = Vector3.Lerp(foot, bearing, t + 0.08f) + Vector3.forward * -sz * 0.0f;
                        frame.Tube(p, new Vector3(-p.x, p.y, p.z), 0.12f, 0.12f, 3);
                        frame.Tube(p, new Vector3(-q.x, q.y, q.z), 0.09f, 0.09f, 3);
                    }

                    // The footing.
                    frame.Box(foot + Vector3.up * 0.3f, new Vector3(2.2f, 0.6f, 2.2f), Quaternion.identity);
                }

                // Leg splay in the other direction, to the ground behind the axle line.
            }

            frame.Tube(new Vector3(0f, hubY, -half - 2.2f), new Vector3(0f, hubY, half + 2.2f), 0.55f, 0.55f, 8);

            // ---- the rims and what is inside them ----
            float breakA = 38f, breakB = 112f;

            for (int sz = -1; sz <= 1; sz += 2)
            {
                var centre = new Vector3(0f, hubY, sz * half);

                // Outer rim, minus the broken arc; inner and hub rings whole.
                Arc(rim, centre, right, up, R, breakB, 360f + breakA, 0.3f, 40, 5);
                Arc(rim, centre, right, up, R * 0.62f, 0f, 360f, 0.16f, 28, 4);
                Arc(rim, centre, right, up, R * 0.16f, 0f, 360f, 0.28f, 12, 5);

                // The two broken ends bend out and down.
                for (int end = 0; end < 2; end++)
                {
                    float ang = (end == 0 ? breakA : breakB) * Mathf.Deg2Rad;
                    var tip = centre + (right * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * R;
                    var dir = (end == 0 ? -1f : 1f) * (-right * Mathf.Sin(ang) + up * Mathf.Cos(ang));
                    rim.Tube(tip, tip + dir * 3.2f + Vector3.forward * sz * 0.8f + Vector3.down * 1.4f, 0.3f, 0.22f, 5);
                }

                // Spokes: sixteen to the outer rim, a few snapped off short.
                for (int i = 0; i < 16; i++)
                {
                    float deg = i * 22.5f;
                    bool inBreak = deg > breakA - 2f && deg < breakB + 2f;
                    float a = deg * Mathf.Deg2Rad;
                    var dir = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                    float reach = inBreak ? R * Rand(rng, 0.35f, 0.62f) : R;
                    rim.Tube(centre + dir * (R * 0.16f), centre + dir * reach, 0.09f, 0.09f, 3);
                }
            }

            // Cross-rods between the rims at every spoke, and the cabin hangers.
            int cabinCount = 20;
            for (int i = 0; i < cabinCount; i++)
            {
                float deg = i * 360f / cabinCount;
                float a = deg * Mathf.Deg2Rad;
                var onRim = new Vector3(Mathf.Cos(a) * R, hubY + Mathf.Sin(a) * R, 0f);

                bool inBreak = deg > breakA - 4f && deg < breakB + 4f;
                if (!inBreak)
                    rim.Tube(onRim + Vector3.forward * -half, onRim + Vector3.forward * half, 0.1f, 0.1f, 4);

                if (inBreak || rng.NextDouble() < 0.34) continue;

                // Most hang true; some have swung and sit crooked on one hook.
                float tilt = rng.NextDouble() < 0.22 ? Rand(rng, 18f, 42f) * (rng.NextDouble() < 0.5 ? -1f : 1f) : Rand(rng, -3f, 3f);
                Gondola(cabins, canopy, onRim, 0f, tilt);
            }

            // ---- on the ground: the lost arc of rim, and cabins that fell ----
            Arc(ground, new Vector3(-5f, 0.3f, 14f), right, Vector3.forward, R * 0.96f, 262f, 318f, 0.3f, 14, 5);
            Arc(ground, new Vector3(-5f, 0.3f, 17f), right, Vector3.forward, R * 0.96f, 262f, 316f, 0.3f, 14, 5);
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(Rand(rng, -12f, 12f), 2.6f, Rand(rng, -16f, -9f) * (rng.NextDouble() < 0.5 ? 1f : -1f) - (i % 2) * 6f);
                var local = new Vector3(p.x, 0.15f, p.z);
                Gondola(wreck, wreckCanopy, local + Vector3.up * 2.45f, Rand(rng, 0f, 360f), Rand(rng, 55f, 100f));
            }

            var solidFrame = RideSolid(wheel, layer, "FerrisFrame", frame, _parkSteel);
            RideDecor(wheel, "FerrisRim", rim, _rideRust);
            RideDecor(wheel, "FerrisCabins", cabins, _parkSteel);
            RideDecor(wheel, "FerrisCanopies", canopy, _rideRed);
            RideSolid(wheel, layer, "FerrisFallenRim", ground, _rideRust);
            RideSolid(wheel, layer, "FerrisFallenCabins", wreck, _parkSteel);
            RideSolid(wheel, layer, "FerrisFallenCanopies", wreckCanopy, _rideRed);

            // ---- strings of bulbs on both rims and the hub, and a ticket booth at the foot ----
            int bd = BackdropLayer();
            var bulbRng = new System.Random(_theme.randomSeed + 31);
            AddRideBulbs(wheel, bd, "RimBulbs", new Vector3(0f, hubY, half + 0.4f), R, 44, true, bulbRng);
            AddRideBulbs(wheel, bd, "RimBulbsFar", new Vector3(0f, hubY, -half - 0.4f), R, 44, true, bulbRng);
            AddRideBulbs(wheel, bd, "InnerBulbs", new Vector3(0f, hubY, half + 0.4f), R * 0.62f, 24, true, bulbRng);

            // The gate, queue and booth are built where the walkway arrives: see RideGate.

            // A warm light on the hub, so the wheel is the first thing lit at dusk.
            var lightGo = new GameObject("WheelLight");
            lightGo.transform.SetParent(wheel, false);
            lightGo.transform.localPosition = new Vector3(0f, hubY, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.38f);
            light.intensity = 9f;
            light.range = 46f;
            light.shadows = LightShadows.None;
        }

        // ==================================================================
        // The roller coaster
        // ==================================================================
        private static readonly Vector3[] CoasterControl =
        {
            new Vector3(-30f, 4.0f, -30f),     // station, west end
            new Vector3(-8f, 4.0f, -30f),      // station, east end
            new Vector3(4f, 4.4f, -30f),       // foot of the lift
            new Vector3(24f, 22f, -30f),
            new Vector3(37f, 29f, -29f),       // crest
            new Vector3(46f, 28f, -17f),
            new Vector3(48f, 20f, -3f),        // the first drop, turning
            new Vector3(42f, 5f, 10f),
            new Vector3(28f, 3.5f, 20f),
            new Vector3(12f, 13f, 27f),        // camelback
            new Vector3(-4f, 6f, 29f),
            new Vector3(-22f, 14f, 24f),       // second hill
            new Vector3(-38f, 8f, 12f),
            new Vector3(-45f, 5f, -4f),
            new Vector3(-43f, 4.2f, -20f),
        };

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
            => 0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u
                       + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u);

        /// <summary>A closed spline through the control points, resampled every <paramref name="spacing"/> metres.</summary>
        private static List<Vector3> ClosedSpline(Vector3[] control, float spacing)
        {
            var dense = new List<Vector3>();
            int n = control.Length;

            for (int i = 0; i < n; i++)
                for (int s = 0; s < 24; s++)
                    dense.Add(CatmullRom(control[(i - 1 + n) % n], control[i], control[(i + 1) % n], control[(i + 2) % n], s / 24f));

            var result = new List<Vector3> { dense[0] };
            float carry = 0f;
            for (int i = 0; i < dense.Count; i++)
            {
                var a = dense[i];
                var b = dense[(i + 1) % dense.Count];
                float length = Vector3.Distance(a, b);
                float at = spacing - carry;
                while (at <= length)
                {
                    result.Add(Vector3.Lerp(a, b, at / length));
                    at += spacing;
                }

                carry = length - (at - spacing);
            }

            return result;
        }

        /// <summary>
        /// A roller coaster that has fallen apart.
        ///
        /// The track is a real spline -- two rails, ties, a spine, banked through the turns --
        /// carried on lattice supports that go to the ground wherever it is. A section of the
        /// second hill has come down: the same track, laid crumpled on the ground, with the
        /// supports under it lying round it. The station is the one part still standing and the
        /// one part you can climb: a platform with a stair at each end, the train still in it.
        /// </summary>
        private static void BuildCoaster(Transform parent, int layer, System.Random rng, Vector3 at)
        {
            var ride = new GameObject("Coaster").transform;
            ride.SetParent(parent, false);
            ride.position = at;

            var spine = CoasterRail(ride, layer, rng, at, out var samples);
            BuildCoasterStation(ride, layer, rng, at);

            _stationPlan = new ParkSite { Centre = new Vector2(at.x - 18f, at.z - 33f), Radius = 14f, Yaw = 0f };
        }

        private static List<Vector3> CoasterRail(Transform ride, int layer, System.Random rng, Vector3 at,
                                                 out List<Vector3> samplesOut)
        {
            var samples = ClosedSpline(CoasterControl, 1.7f);
            samplesOut = samples;
            int n = samples.Count;

            var rails = new MeshBuild { UVScale = 0.5f };
            var ties = new MeshBuild { UVScale = 0.5f };
            var supports = new MeshBuild { UVScale = 0.5f };
            var fallen = new MeshBuild { UVScale = 0.5f };
            var fallenTies = new MeshBuild { UVScale = 0.5f };
            var chain = new MeshBuild { UVScale = 0.5f };

            // Smooth bank from the turn rate.
            var bank = new float[n];
            for (int i = 0; i < n; i++)
            {
                var a = samples[(i - 2 + n) % n]; var b = samples[i]; var c = samples[(i + 2) % n];
                var d0 = new Vector3(b.x - a.x, 0f, b.z - a.z); var d1 = new Vector3(c.x - b.x, 0f, c.z - b.z);
                bank[i] = d0.sqrMagnitude < 1e-4f || d1.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp(Vector3.SignedAngle(d0, d1, Vector3.up) * 2.4f, -34f, 34f);
            }

            for (int pass = 0; pass < 3; pass++)
            {
                var smooth = new float[n];
                for (int i = 0; i < n; i++) smooth[i] = (bank[(i - 1 + n) % n] + bank[i] * 2f + bank[(i + 1) % n]) * 0.25f;
                bank = smooth;
            }

            // The fallen stretch: which samples, found by where on the track they are.
            int fallFrom = -1, fallTo = -1;
            for (int i = 0; i < n; i++)
            {
                var p = samples[i];
                if (fallFrom < 0 && p.x < 14f && p.z > 24f && p.y > 8f) fallFrom = i;
                if (fallFrom >= 0 && p.x < -14f) { fallTo = i; break; }
            }

            float GroundLocal(Vector3 p) => GroundHeightAt(at.x + p.x, at.z + p.z) - at.y;

            Vector3 Pos(int i, out float f)
            {
                var p = samples[(i + n) % n];
                f = 0f;
                if (fallFrom >= 0 && i > fallFrom && i < fallTo)
                {
                    f = Mathf.Sin(Mathf.PI * (i - fallFrom) / (float)(fallTo - fallFrom));
                    float g = GroundLocal(p) + 0.45f;
                    p.y = Mathf.Lerp(p.y, g, Mathf.Clamp01(f * 1.6f));
                    p.x += Mathf.Sin(i * 0.9f) * 2.2f * f;
                    p.z += Mathf.Cos(i * 1.3f) * 2.4f * f;
                }

                return p;
            }

            Vector3 prevL = default, prevR = default, firstL = default, firstR = default;
            bool havePrev = false;

            for (int i = 0; i < n; i++)
            {
                var p = Pos(i, out float f);
                var next = Pos(i + 1, out _);
                var prior = Pos(i - 1, out _);
                var tangent = (next - prior).normalized;
                if (tangent.sqrMagnitude < 0.5f) continue;

                var side = Vector3.Cross(Vector3.up, new Vector3(tangent.x, 0f, tangent.z)).normalized;
                var roll = Quaternion.AngleAxis(-bank[i] - f * 28f * Mathf.Sin(i * 0.7f), tangent);
                var rightV = roll * side;
                var upV = Vector3.Cross(tangent, rightV).normalized;
                if (upV.y < 0f) upV = -upV;

                var l = p - rightV * 0.65f;
                var r = p + rightV * 0.65f;
                bool lying = f > 0.02f;
                var railBuild = lying ? fallen : rails;
                var tieBuild = lying ? fallenTies : ties;

                if (havePrev)
                {
                    railBuild.Tube(prevL, l, 0.09f, 0.09f, 4);
                    railBuild.Tube(prevR, r, 0.09f, 0.09f, 4);
                    railBuild.Tube((prevL + prevR) * 0.5f - upV * 0.38f, p - upV * 0.38f, 0.16f, 0.16f, 4);
                }

                if (i % 2 == 0) tieBuild.Box(p - upV * 0.1f, new Vector3(1.9f, 0.1f, 0.2f), Quaternion.LookRotation(tangent, upV));

                // The lift chain: a toothed line down the middle of the climb.
                if (p.y > 5f && next.y - p.y > 0.4f && i % 2 == 0)
                    chain.Box(p + upV * 0.03f, new Vector3(0.12f, 0.08f, 0.5f), Quaternion.LookRotation(tangent, upV));

                if (!havePrev) { firstL = l; firstR = r; }
                prevL = l; prevR = r; havePrev = true;

                // ---- a support every five samples, where there is room under the track ----
                bool station = p.x < -6f && p.z < -26f && p.y < 5f;
                if (!lying && i % 5 == 0 && !station)
                {
                    float gy = GroundLocal(p);
                    float h = p.y - 0.5f - gy;
                    if (h > 2.4f)
                    {
                        var baseL = new Vector3(l.x, gy, l.z);
                        var baseR = new Vector3(r.x, gy, r.z);
                        var topL = l - upV * 0.45f;
                        var topR = r - upV * 0.45f;

                        bool toppled = rng.NextDouble() < 0.07;
                        if (toppled)
                        {
                            // Lying on the ground where it fell.
                            var dir = new Vector3(Rand(rng, -1f, 1f), 0f, Rand(rng, -1f, 1f)).normalized;
                            fallen.Tube(baseL + Vector3.up * 0.3f, baseL + Vector3.up * 0.3f + dir * h * 0.8f, 0.3f, 0.22f, 5);
                            continue;
                        }

                        if (h > 9f)
                        {
                            Lattice(supports, baseL, topL, 0.8f, 0.07f, 0.04f, Mathf.Max(3, Mathf.RoundToInt(h / 2.2f)));
                            Lattice(supports, baseR, topR, 0.8f, 0.07f, 0.04f, Mathf.Max(3, Mathf.RoundToInt(h / 2.2f)));
                        }
                        else
                        {
                            supports.Tube(baseL, topL, 0.28f, 0.2f, 5);
                            supports.Tube(baseR, topR, 0.28f, 0.2f, 5);
                        }

                        // Cross bracing: a brace at a third and two-thirds, and an X between them.
                        for (float t = 0.33f; t < 0.9f; t += 0.33f)
                            supports.Tube(Vector3.Lerp(baseL, topL, t), Vector3.Lerp(baseR, topR, t), 0.1f, 0.1f, 3);
                        supports.Tube(Vector3.Lerp(baseL, topL, 0.33f), Vector3.Lerp(baseR, topR, 0.66f), 0.07f, 0.07f, 3);
                        supports.Tube(Vector3.Lerp(baseR, topR, 0.33f), Vector3.Lerp(baseL, topL, 0.66f), 0.07f, 0.07f, 3);
                        supports.Tube(topL, topR, 0.14f, 0.14f, 4);

                        supports.Box(baseL + Vector3.up * 0.25f, new Vector3(1.1f, 0.5f, 1.1f), Quaternion.identity);
                        supports.Box(baseR + Vector3.up * 0.25f, new Vector3(1.1f, 0.5f, 1.1f), Quaternion.identity);
                    }
                }
            }

            // Close the loop: the last sample back to the first.
            if (havePrev)
            {
                rails.Tube(prevL, firstL, 0.09f, 0.09f, 4);
                rails.Tube(prevR, firstR, 0.09f, 0.09f, 4);
            }

            RideSolid(ride, layer, "CoasterSupports", supports, _parkSteel);
            RideDecor(ride, "CoasterRails", rails, _rideRust);
            RideDecor(ride, "CoasterTies", ties, _parkTimber);
            RideDecor(ride, "CoasterChain", chain, _parkSteel);
            RideSolid(ride, layer, "CoasterFallenTrack", fallen, _rideRust);
            RideSolid(ride, layer, "CoasterFallenTies", fallenTies, _parkTimber, "Wood");

            // ---- the derailed train, on the ground under the first drop ----
            var body = new MeshBuild { UVScale = 0.5f };
            var seats = new MeshBuild { UVScale = 0.5f };
            for (int c = 0; c < 3; c++)
            {
                var pos = new Vector3(36f + c * 3.6f + Rand(rng, -0.6f, 0.6f), 0.7f, 12f + c * 2.4f + Rand(rng, -1f, 1f));
                pos.y = GroundHeightAt(at.x + pos.x, at.z + pos.z) - at.y + 0.7f;
                CoasterCar(body, seats, pos, 25f * c + Rand(rng, -35f, 35f), Rand(rng, -6f, 28f) * (c == 1 ? 3f : 1f));
            }

            // And the train that never left the station.
            for (int c = 0; c < 3; c++)
            {
                var pos = new Vector3(-26f + c * 3.6f, 4.0f + 0.55f, -30f);
                CoasterCar(body, seats, pos, 90f, 0f);
            }

            RideSolid(ride, layer, "CoasterCars", body, _rideRed);
            RideDecor(ride, "CoasterSeats", seats, _parkDark);

            return samples;
        }

        /// <summary>A coaster car: tub, nose, two rows of seats and a lap bar, turned and rolled.</summary>
        private static void CoasterCar(MeshBuild body, MeshBuild seats, Vector3 at, float yaw, float roll)
        {
            var rot = Quaternion.Euler(0f, yaw, roll);
            Vector3 P(float x, float y, float z) => at + rot * new Vector3(x, y, z);

            body.Box(P(0f, 0f, 0f), new Vector3(1.55f, 0.55f, 2.9f), rot);
            body.Box(P(0f, 0.2f, 1.55f), new Vector3(1.4f, 0.4f, 0.5f), rot * Quaternion.Euler(-14f, 0f, 0f));
            body.Box(P(-0.78f, 0.45f, 0f), new Vector3(0.08f, 0.5f, 2.6f), rot);
            body.Box(P(0.78f, 0.45f, 0f), new Vector3(0.08f, 0.5f, 2.6f), rot);

            for (int row = 0; row < 2; row++)
            {
                seats.Box(P(0f, 0.42f, -0.7f + row * 1.3f), new Vector3(1.2f, 0.12f, 0.5f), rot);
                seats.Box(P(0f, 0.85f, -0.92f + row * 1.3f), new Vector3(1.2f, 0.7f, 0.12f), rot * Quaternion.Euler(-10f, 0f, 0f));
                seats.Tube(P(-0.5f, 0.7f, -0.4f + row * 1.3f), P(0.5f, 0.7f, -0.4f + row * 1.3f), 0.04f, 0.04f, 4);
            }

            // Wheels: a pair either side, as an under-slung carriage.
            for (int s = -1; s <= 1; s += 2)
                for (int e = -1; e <= 1; e += 2)
                    body.Tube(P(s * 0.8f, -0.38f, e * 0.9f), P(s * 0.8f + s * 0.12f, -0.38f, e * 0.9f), 0.2f, 0.2f, 8);
        }

        /// <summary>
        /// The one part that is still standing: a platform 3.08m up, carried on steel, with a roof
        /// of torn canvas, the whole length of the train, and a stair at each end.
        /// </summary>
        private static void BuildCoasterStation(Transform ride, int layer, System.Random rng, Vector3 at)
        {
            const int Steps = 14;
            float deckY = FgStairHeight(Steps);

            // Platform: a solid deck beside the track, walkable.
            const float x0 = -30f, x1 = -6f, zc = -33.4f, depth = 3.6f;
            CreateBlock(ride, new Vector3((x0 + x1) * 0.5f, deckY - 0.2f, zc), new Vector3(x1 - x0, 0.4f, depth), 0f, layer,
                        "Wood", Color.grey, 0.05f, 0f, "StationDeck", _parkTimber);

            // The front edge and the fascia, carried on steel posts every four metres.
            var steel = new MeshBuild { UVScale = 0.5f };
            for (float x = x0 + 1f; x <= x1; x += 4f)
            {
                steel.Tube(new Vector3(x, 0f, zc - 1.6f), new Vector3(x, deckY - 0.4f, zc - 1.6f), 0.16f, 0.16f, 4);
                steel.Tube(new Vector3(x, 0f, zc + 1.6f), new Vector3(x, deckY - 0.4f, zc + 1.6f), 0.16f, 0.16f, 4);
                steel.Tube(new Vector3(x, 0.1f, zc - 1.6f), new Vector3(x + 4f, deckY - 0.6f, zc - 1.6f), 0.07f, 0.07f, 3);

                // Roof posts, and the rail along the back edge.
                steel.Tube(new Vector3(x, deckY, zc - 1.7f), new Vector3(x, deckY + 4.6f, zc - 1.7f), 0.14f, 0.14f, 4);
                steel.Tube(new Vector3(x, deckY, zc + 1.7f), new Vector3(x, deckY + 3.6f, zc + 1.7f), 0.14f, 0.14f, 4);
            }

            // The rail along the track side only: the outer edge is where the queue stands.
            steel.Tube(new Vector3(x0, deckY + 1.0f, zc + 1.78f), new Vector3(x1, deckY + 1.0f, zc + 1.78f), 0.04f, 0.04f, 4);

            // The roof: canvas on a ridge, three of every four strips still up.
            var roof = new MeshBuild { UVScale = 0.5f };
            var roofB = new MeshBuild { UVScale = 0.5f };
            int strips = 12;
            for (int s = 0; s < strips; s++)
            {
                if (rng.NextDouble() < 0.22) continue;
                float xa = Mathf.Lerp(x0 - 0.5f, x1 + 0.5f, s / (float)strips);
                float xb = Mathf.Lerp(x0 - 0.5f, x1 + 0.5f, (s + 1f) / strips);
                var build = s % 2 == 0 ? roof : roofB;
                Cloth(build, new Vector3(xa, deckY + 4.6f, zc - 1.8f), new Vector3(xb - xa, 0f, 0f), new Vector3(0f, -0.6f, 3.9f),
                      1, 3, 0.18f, 700 + s, 0.1f);
            }

            // The tickets: an operator's cab at the east end of the platform.
            NoStanding(CreateBlock(ride, new Vector3(x1 - 1.6f, deckY + 1.2f, zc - 0.6f), new Vector3(2.2f, 2.4f, 2.0f), 0f, layer,
                                   "Wood", Color.grey, 0.1f, 0f, "OperatorCab", _rideCream));

            // ---- stairs: one at each end, climbing in toward the platform ----
            FgStair(ride, layer, _parkTimber, _parkSteel, new Vector3(x0 - FgStairRun(Steps), 0f, zc), 90f, Steps, 2.4f);
            FgStair(ride, layer, _parkTimber, _parkSteel, new Vector3(x1 + FgStairRun(Steps), 0f, zc), 270f, Steps, 2.4f);

            RideSolid(ride, layer, "StationSteel", steel, _parkSteel);
            RideDecor(ride, "StationRoofA", roof, _rideCanvasRed);
            RideDecor(ride, "StationRoofB", roofB, _rideCanvasCream);

            var lightGo = new GameObject("StationLight");
            lightGo.transform.SetParent(ride, false);
            lightGo.transform.localPosition = new Vector3(-18f, deckY + 3.4f, zc);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.70f, 0.38f);
            light.intensity = 5f;
            light.range = 26f;
            light.shadows = LightShadows.None;
        }

        // ==================================================================
        // The carousel
        // ==================================================================
        /// <summary>
        /// A carousel: a raised deck you can step onto, a mirrored drum, three rings of brass poles
        /// with horses on them, and a scalloped canopy that has come down on one side.
        ///
        /// The deck is 0.88m up, four risers, with three stairs round it. Its poles and horses are
        /// the cover; the canopy over it is the roof of a fight in a ring.
        /// </summary>
        private static void BuildCarousel(Transform parent, int layer, System.Random rng, Vector3 at)
        {
            var ride = new GameObject("Carousel").transform;
            ride.SetParent(parent, false);
            ride.position = at;

            const float R = 14f;
            const int DeckSteps = 4;
            float deckY = FgStairHeight(DeckSteps);

            // The deck: a thick disc, solid and walkable.
            var deck = new MeshBuild { UVScale = 0.2f };
            deck.Tube(new Vector3(0f, 0.15f, 0f), new Vector3(0f, deckY, 0f), R, R, 32);
            var deckGo = MeshObject(ride, "CarouselDeck", ToMesh(deck, DenseKey("carouseldeck")), _parkTimber, Vector3.zero,
                                    Quaternion.identity, Vector3.one, layer, "Wood");
            Mark(deckGo, new Color(0.46f, 0.3f, 0.28f), 1);

            // Three stairs round the rim.
            foreach (float angle in new[] { 40f, 160f, 280f })
            {
                float a = angle * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var foot = dir * (R + FgStairRun(DeckSteps) - 0.15f);
                float yaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
                FgStair(ride, layer, _parkTimber, _parkSteel, foot, yaw, DeckSteps, 2.6f, rails: false);
            }

            var drum = new MeshBuild { UVScale = 0.4f };
            var brass = new MeshBuild { UVScale = 0.5f };
            var horses = new MeshBuild { UVScale = 0.5f };
            var canopyA = new MeshBuild { UVScale = 0.5f };
            var canopyB = new MeshBuild { UVScale = 0.5f };
            var fallenA = new MeshBuild { UVScale = 0.5f };
            var trim = new MeshBuild { UVScale = 0.5f };

            const float canopyY = 7.6f, apexY = 12.2f, rimR = 14.8f;

            // The central drum, with mirror panels and a crown.
            drum.Tube(new Vector3(0f, deckY, 0f), new Vector3(0f, canopyY, 0f), 3.1f, 2.7f, 14);
            drum.Tube(new Vector3(0f, canopyY - 0.4f, 0f), new Vector3(0f, canopyY + 0.3f, 0f), 3.5f, 3.5f, 14);
            brass.Tube(new Vector3(0f, canopyY, 0f), new Vector3(0f, apexY + 1.8f, 0f), 0.2f, 0.08f, 6);
            brass.Tube(new Vector3(0f, apexY + 0.6f, 0f), new Vector3(0f, apexY + 1.1f, 0f), 0.5f, 0.1f, 8);

            // Poles and horses on three rings.
            int[] counts = { 8, 12, 16 };
            float[] radii = { 5.8f, 9.1f, 12.2f };
            int seed = 0;

            for (int ring = 0; ring < 3; ring++)
                for (int i = 0; i < counts[ring]; i++)
                {
                    float a = (i + ring * 0.5f) * Mathf.PI * 2f / counts[ring];
                    var foot = new Vector3(Mathf.Cos(a) * radii[ring], deckY, Mathf.Sin(a) * radii[ring]);

                    brass.Tube(foot, foot + Vector3.up * (canopyY - deckY), 0.045f, 0.045f, 4);

                    // Over half the horses are gone; the rest are in whatever state a decade leaves them.
                    if (rng.NextDouble() < 0.42) continue;

                    float lift = (i % 2 == 0 ? 0.5f : 1.3f);
                    float yaw = -a * Mathf.Rad2Deg + 90f + (rng.NextDouble() < 0.1 ? 180f : 0f);
                    Horse(horses, brass, foot + Vector3.up * lift, yaw, 1.0f, rng.NextDouble() < 0.28, canopyY - deckY - lift, ++seed);
                }

            // The canopy: 24 sectors, a scalloped valance, and the ones that fell.
            const int Sectors = 24;
            for (int s = 0; s < Sectors; s++)
            {
                float a0 = s * Mathf.PI * 2f / Sectors, a1 = (s + 1) * Mathf.PI * 2f / Sectors;
                var rimA = new Vector3(Mathf.Cos(a0) * rimR, canopyY, Mathf.Sin(a0) * rimR);
                var rimB = new Vector3(Mathf.Cos(a1) * rimR, canopyY, Mathf.Sin(a1) * rimR);
                var apex = new Vector3(0f, apexY, 0f);
                var build = s % 2 == 0 ? canopyA : canopyB;

                bool fell = s >= 5 && s <= 8;
                bool torn = !fell && rng.NextDouble() < 0.16;

                if (fell)
                {
                    // The sector has slid off the frame and lies across the deck.
                    float mid = (a0 + a1) * 0.5f;
                    var lay = new Vector3(Mathf.Cos(mid) * 7.2f, deckY + 0.5f + (s - 5) * 0.12f, Mathf.Sin(mid) * 7.2f);
                    var outward = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
                    var tangent = new Vector3(-Mathf.Sin(mid), 0f, Mathf.Cos(mid));
                    fallenA.Quad(lay - tangent * 2.8f - outward * 5.5f, lay + tangent * 2.8f - outward * 5.5f,
                                 lay + tangent * 1.0f + outward * 5.5f + Vector3.up * 0.9f, lay - tangent * 1.0f + outward * 5.5f + Vector3.up * 0.9f);
                    fallenA.Quad(lay - tangent * 1.0f + outward * 5.5f + Vector3.up * 0.9f, lay + tangent * 1.0f + outward * 5.5f + Vector3.up * 0.9f,
                                 lay + tangent * 2.8f - outward * 5.5f, lay - tangent * 2.8f - outward * 5.5f);
                    continue;
                }

                if (!torn)
                {
                    // Two bands per sector, so the cone sags between the ribs rather than being straight.
                    var midA = Vector3.Lerp(rimA, apex, 0.55f) + Vector3.down * 0.35f;
                    var midB = Vector3.Lerp(rimB, apex, 0.55f) + Vector3.down * 0.35f;
                    build.Quad(rimA, midA, midB, rimB);
                    build.Quad(rimB, midB, midA, rimA);
                    build.Tri(midA, apex, midB);
                    build.Tri(midB, apex, midA);
                }

                // Valance: a scalloped skirt, half of it ragged.
                if (rng.NextDouble() < 0.82)
                {
                    var dropA = rimA + Vector3.down * 0.9f; var dropB = rimB + Vector3.down * 0.9f;
                    var tip = (rimA + rimB) * 0.5f + Vector3.down * (rng.NextDouble() < 0.4 ? 1.7f : 1.3f);
                    build.Quad(rimA, dropA, dropB, rimB);
                    build.Quad(rimB, dropB, dropA, rimA);
                    build.Tri(dropA, tip, dropB);
                    build.Tri(dropB, tip, dropA);
                }

                // The rib, and a post under it at every other sector.
                trim.Tube(rimA, apex, 0.07f, 0.05f, 3);
                if (s % 2 == 0) trim.Tube(rimA - Vector3.up * 0.1f, rimA - Vector3.up * (canopyY - deckY), 0.09f, 0.09f, 4);
            }

            trim.Tube(new Vector3(0f, canopyY, 0f) + new Vector3(rimR, 0f, 0f), new Vector3(0f, canopyY, 0f) + new Vector3(rimR, 0f, 0f), 0.1f, 0.1f, 3);

            // The rim ring as a polygon of tubes.
            Arc(trim, new Vector3(0f, canopyY, 0f), Vector3.right, Vector3.forward, rimR, 0f, 360f, 0.1f, 30, 4);

            RideSolid(ride, layer, "CarouselDrum", drum, _rideCream);
            RideSolid(ride, layer, "CarouselPoles", brass, _rideBrass);
            RideSolid(ride, layer, "CarouselHorses", horses, _rideRed, "Wood");
            RideDecor(ride, "CarouselCanopyA", canopyA, _rideCanvasRed);
            RideDecor(ride, "CarouselCanopyB", canopyB, _rideCanvasCream);
            RideSolid(ride, layer, "CarouselFallen", fallenA, _rideCanvasRed, "Wood");
            RideDecor(ride, "CarouselRibs", trim, _rideBrass);

            int bd = BackdropLayer();
            var bulbRng = new System.Random(_theme.randomSeed + 47);
            AddRideBulbs(ride, bd, "CanopyBulbs", new Vector3(0f, canopyY - 0.35f, 0f), rimR, 40, false, bulbRng);
            AddRideBulbs(ride, bd, "DeckBulbs", new Vector3(0f, deckY + 0.25f, 0f), R - 0.3f, 40, false, bulbRng);

            // The horses that came off: a few lying on the ground outside the stairs.
            var loose = new MeshBuild { UVScale = 0.5f };
            var junk = new MeshBuild { UVScale = 0.5f };
            for (int i = 0; i < 4; i++)
            {
                float a = Rand(rng, 0f, 6.283f), d = Rand(rng, R + 2.5f, R + 6f);
                var pos = new Vector3(Mathf.Cos(a) * d, 0.55f, Mathf.Sin(a) * d);
                Horse(loose, junk, pos, Rand(rng, 0f, 360f), 1f, true, 0.4f, 90 + i);
            }

            var looseGo = RideSolid(ride, layer, "CarouselFallenHorses", loose, _rideRed, "Wood");

            var lightGo = new GameObject("CarouselLight");
            lightGo.transform.SetParent(ride, false);
            lightGo.transform.localPosition = new Vector3(0f, canopyY - 1f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.40f);
            light.intensity = 6f;
            light.range = 30f;
            light.shadows = LightShadows.None;
        }
    }
}
#endif
