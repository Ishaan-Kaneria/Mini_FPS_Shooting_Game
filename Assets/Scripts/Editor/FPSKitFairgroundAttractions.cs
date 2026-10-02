#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The attractions that are not the three big rides: the big top, the drop tower, the
    /// swing ride, the haunted house, a hedge maze, the clock tower over the plaza and the
    /// bandstand. Each is a place to fight in or on, not only to look at.
    ///
    /// <b>What can be climbed.</b> The big top has raked seating (tiers of 0.3m that an agent
    /// steps up one by one), the bandstand a floor three steps up, and the haunted house a
    /// balcony reached by a flight at each end. Everything else that rises is held off the bake.
    ///
    /// <b>What can be walked into.</b> The haunted house and the big top have an inside, so each
    /// has more than one way in: four doorways round the tent, five openings in the house, and the
    /// maze two ends. A building with one door is a pocket the navigation bake still walks and
    /// nothing can enter, which is the stranded-patch failure this project has been bitten by.
    /// </summary>
    public partial class FPSKitSceneBuilder
    {
        private static Material _hhWall, _hhTrim, _hhGlow, _hhBone, _hedgeMat;

        private static void ResolveAttractionMaterials()
        {
            _hhWall = MakeDetailMaterial("HauntedWall", new Color(0.36f, 0.31f, 0.38f), "Adobe", Metres(4f), 0.05f, 0f, 1f);
            _hhTrim = MakeDetailMaterial("HauntedTrim", new Color(0.15f, 0.12f, 0.15f), "Timber", Metres(2f), 0.08f, 0f, 0.8f);
            _hhGlow = MakeDetailMaterial("HauntedGlow", new Color(0.10f, 0.20f, 0.12f), "Concrete", Metres(4f), 0.4f, 0f, 0.1f);
            SetEmission(_hhGlow, new Color(0.25f, 0.9f, 0.45f) * 1.2f);
            _hhBone = MakeDetailMaterial("HauntedBone", new Color(0.66f, 0.62f, 0.52f), "Adobe", Metres(2f), 0.1f, 0f, 0.5f);
            _midwayPaintB = MakeDetailMaterial("MidwayPaintB", new Color(0.14f, 0.30f, 0.34f), "Timber", Metres(1.6f), 0.16f, 0f, 1.1f);
            _hedgeMat = MakeDetailMaterial("Hedge", new Color(0.12f, 0.27f, 0.10f), "Adobe", Metres(2f), 0.06f, 0f, 0.5f);
        }

        // ==================================================================
        // The big top
        // ==================================================================
        /// <summary>
        /// A circus tent, 52m across: a wall of striped canvas with four doorways, a cone of
        /// canvas on one king pole and a ring of guys, raked seating in four blocks, and a
        /// sawdust ring in the middle. The roof is torn, and on the north-west side it has come
        /// down.
        /// </summary>
        private static void BuildBigTop(Transform parent, int layer, System.Random rng, Vector3 at)
        {
            ResolveAttractionMaterials();

            var tent = new GameObject("BigTop").transform;
            tent.SetParent(parent, false);
            tent.position = at;

            const float R = 26f, wallR = 25.5f, wallH = 6.4f, poleH = 19.5f;
            const int Sectors = 24;

            var wallA = new MeshBuild { UVScale = 0.4f };
            var wallB = new MeshBuild { UVScale = 0.4f };
            var roofA = new MeshBuild { UVScale = 0.4f };
            var roofB = new MeshBuild { UVScale = 0.4f };
            var steel = new MeshBuild { UVScale = 0.5f };
            var valance = new MeshBuild { UVScale = 0.4f };
            var fallen = new MeshBuild { UVScale = 0.4f };

            var apex = new Vector3(0f, poleH, 0f);

            for (int s = 0; s < Sectors; s++)
            {
                float a0 = s * Mathf.PI * 2f / Sectors, a1 = (s + 1) * Mathf.PI * 2f / Sectors;
                float mid = (a0 + a1) * 0.5f;
                var wall = s % 2 == 0 ? wallA : wallB;
                var roof = s % 2 == 0 ? roofA : roofB;

                bool doorway = s % 6 == 0;
                var tangent = new Vector3(-Mathf.Sin(mid), 0f, Mathf.Cos(mid));
                float chord = 2f * wallR * Mathf.Sin(Mathf.PI / Sectors);

                if (!doorway)
                    wall.Box(new Vector3(Mathf.Cos(mid) * wallR, wallH * 0.5f, Mathf.Sin(mid) * wallR),
                             new Vector3(0.28f, wallH, chord + 0.12f), Quaternion.LookRotation(tangent));
                else
                {
                    // Over each doorway the canvas is looped back and tied up.
                    wall.Box(new Vector3(Mathf.Cos(mid) * wallR, wallH - 0.7f, Mathf.Sin(mid) * wallR),
                             new Vector3(0.28f, 1.4f, chord + 0.12f), Quaternion.LookRotation(tangent));
                }

                var rimA = new Vector3(Mathf.Cos(a0) * R, wallH, Mathf.Sin(a0) * R);
                var rimB = new Vector3(Mathf.Cos(a1) * R, wallH, Mathf.Sin(a1) * R);

                // The roof comes down on the north-west, where it has let go of the pole.
                bool down = s >= 13 && s <= 16;

                for (int band = 0; band < 3; band++)
                {
                    float t0 = band / 3f, t1 = (band + 1f) / 3f;
                    var p0A = Vector3.Lerp(rimA, apex, t0); var p0B = Vector3.Lerp(rimB, apex, t0);
                    var p1A = Vector3.Lerp(rimA, apex, t1); var p1B = Vector3.Lerp(rimB, apex, t1);
                    float sag = Mathf.Sin((t0 + t1) * 0.5f * Mathf.PI) * 0.7f;
                    p0A.y -= Mathf.Sin(t0 * Mathf.PI) * 0.7f; p0B.y -= Mathf.Sin(t0 * Mathf.PI) * 0.7f;
                    p1A.y -= Mathf.Sin(t1 * Mathf.PI) * 0.7f; p1B.y -= Mathf.Sin(t1 * Mathf.PI) * 0.7f;

                    if (down && band > 0)
                    {
                        // Hanging in strips from the rim, torn off at about head height.
                        if (band == 1 && rng.NextDouble() < 0.7)
                        {
                            var lowA = Vector3.Lerp(rimA, apex, 0.2f) + Vector3.down * Rand(rng, 0f, 3.5f);
                            var lowB = Vector3.Lerp(rimB, apex, 0.2f) + Vector3.down * Rand(rng, 0f, 3.5f);
                            roof.Quad(rimA, lowA, lowB, rimB);
                            roof.Quad(rimB, lowB, lowA, rimA);
                        }

                        continue;
                    }

                    if (rng.NextDouble() < 0.12) continue;

                    roof.Quad(p0A, p1A, p1B, p0B);
                    roof.Quad(p0B, p1B, p1A, p0A);
                }

                // Scalloped valance round the eave.
                if (!down && rng.NextDouble() < 0.86)
                {
                    var dropA = rimA + Vector3.down * 1.0f; var dropB = rimB + Vector3.down * 1.0f;
                    var tip = (rimA + rimB) * 0.5f + Vector3.down * 1.7f;
                    valance.Quad(rimA, dropA, dropB, rimB); valance.Quad(rimB, dropB, dropA, rimA);
                    valance.Tri(dropA, tip, dropB); valance.Tri(dropB, tip, dropA);
                }

                // A wall pole at every bay, and a guy rope to a stake outside.
                var poleFoot = new Vector3(Mathf.Cos(a0) * (wallR + 0.4f), 0f, Mathf.Sin(a0) * (wallR + 0.4f));
                steel.Tube(poleFoot, poleFoot + Vector3.up * (wallH + 0.9f), 0.1f, 0.07f, 4);
                var stake = new Vector3(Mathf.Cos(a0) * (R + 7f), 0f, Mathf.Sin(a0) * (R + 7f));
                Cable(steel, poleFoot + Vector3.up * (wallH + 0.8f), stake + Vector3.up * 0.3f, 0.3f, 0.02f, 3);
                steel.Tube(stake, stake + Vector3.up * 0.5f, 0.05f, 0.04f, 3);

                // Ribs from the pole to every second rim point.
                if (!down && s % 2 == 0) steel.Tube(rimA, apex - Vector3.up * 0.4f, 0.06f, 0.05f, 3);
            }

            // The king pole, with its crown, and the guys that hold it.
            steel.Tube(Vector3.zero, apex + Vector3.up * 1.4f, 0.42f, 0.2f, 8);
            steel.Tube(apex + Vector3.up * 1.2f, apex + Vector3.up * 2.4f, 0.04f, 0.04f, 3);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI * 2f / 6f + 0.3f;
                Cable(steel, apex, new Vector3(Mathf.Cos(a) * R * 0.9f, wallH + 0.5f, Mathf.Sin(a) * R * 0.9f), 0.6f, 0.03f, 4);
            }

            // ---- the ring ----
            var sawdust = new MeshBuild { UVScale = 0.2f };
            sawdust.Tube(new Vector3(0f, 0.01f, 0f), new Vector3(0f, 0.06f, 0f), 8.6f, 8.6f, 28);
            var curb = new MeshBuild { UVScale = 0.5f };
            Arc(curb, new Vector3(0f, 0.18f, 0f), Vector3.right, Vector3.forward, 8.7f, 0f, 360f, 0.17f, 32, 5);

            // ---- the seating: four blocks of six tiers, each tier a step of 0.3 ----
            var seats = new MeshBuild { UVScale = 0.5f };
            const float Rise = 0.3f, Depth = 1.6f, Inner = 11f;
            for (int block = 0; block < 4; block++)
            {
                float centre = (45f + 90f * block) * Mathf.Deg2Rad;
                float span = 30f * Mathf.Deg2Rad;
                const int Cuts = 8;

                for (int tier = 0; tier < 6; tier++)
                {
                    float ri = Inner + tier * Depth, ro = ri + Depth, top = (tier + 1) * Rise;

                    for (int c = 0; c < Cuts; c++)
                    {
                        float b0 = centre - span + c * 2f * span / Cuts, b1 = centre - span + (c + 1) * 2f * span / Cuts;
                        Vector3 P(float r, float ang, float y) => new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r);

                        // The tread, and the riser below it.
                        seats.Quad(P(ri, b0, top), P(ri, b1, top), P(ro, b1, top), P(ro, b0, top));
                        seats.Quad(P(ri, b1, 0f), P(ri, b1, top), P(ri, b0, top), P(ri, b0, 0f));
                        if (tier == 5) seats.Quad(P(ro, b0, 0f), P(ro, b0, top), P(ro, b1, top), P(ro, b1, 0f));
                    }

                    // The ends of the tier.
                    float e0 = centre - span, e1 = centre + span;
                    Vector3 Q(float r, float ang, float y) => new Vector3(Mathf.Cos(ang) * r, y, Mathf.Sin(ang) * r);
                    seats.Quad(Q(ri, e0, 0f), Q(ri, e0, top), Q(ro, e0, top), Q(ro, e0, 0f));
                    seats.Quad(Q(ro, e1, 0f), Q(ro, e1, top), Q(ri, e1, top), Q(ri, e1, 0f));
                }
            }

            // ---- rigging, a net, a cannon ----
            var net = new MeshBuild { UVScale = 0.5f };
            foreach (float x in new[] { -9.8f, 9.8f })
            {
                steel.Tube(new Vector3(x, 0f, 0f), new Vector3(x, 14f, 0f), 0.2f, 0.14f, 6);
                Cable(steel, new Vector3(x, 14f, 0f), apex - Vector3.up * 1f, 1.5f, 0.03f, 6);
            }

            Cloth(net, new Vector3(-9.8f, 5.2f, -3f), new Vector3(19.6f, 0f, 0f), new Vector3(0f, 0f, 6f), 8, 3, 1.3f, 61, 0.22f);

            var cannon = new MeshBuild { UVScale = 0.5f };
            cannon.Tube(new Vector3(3f, 1.2f, -5f), new Vector3(6.2f, 2.8f, -5f), 0.55f, 0.45f, 8);
            for (int s = -1; s <= 1; s += 2)
                cannon.Tube(new Vector3(3.2f, 0.8f, -5f + s * 0.8f), new Vector3(3.2f, 0.8f, -5f + s * 0.9f), 0.8f, 0.8f, 12);

            // ---- the wagons that came with it, drawn up outside ----
            var wagons = new MeshBuild { UVScale = 0.5f };
            foreach (float angDeg in new[] { 100f, 160f, 250f })
            {
                float a = angDeg * Mathf.Deg2Rad;
                var centre = new Vector3(Mathf.Cos(a) * (R + 11f), 0f, Mathf.Sin(a) * (R + 11f));
                CageWagon(wagons, centre, -angDeg + 90f + Rand(rng, -8f, 8f), rng);
            }

            RideSolid(tent, layer, "TentWallA", wallA, _rideCanvasRed, "Wood");
            RideSolid(tent, layer, "TentWallB", wallB, _rideCanvasCream, "Wood");
            RideDecor(tent, "TentRoofA", roofA, _rideCanvasRed);
            RideDecor(tent, "TentRoofB", roofB, _rideCanvasCream);
            RideDecor(tent, "TentValance", valance, _rideCanvasRed);
            RideSolid(tent, layer, "TentSteel", steel, _parkSteel);
            RideDecor(tent, "TentSawdust", sawdust, _parkRubble, false);
            RideDecor(tent, "TentCurb", curb, _rideRed, false);
            RideDecor(tent, "TentNet", net, _parkDark);
            RideSolid(tent, layer, "TentCannon", cannon, _rideRust);
            RideSolid(tent, layer, "TentWagons", wagons, _rideRed, "Wood");

            // The seating is walkable: it keeps its place in the bake.
            var seatGo = MeshObject(tent, "TentSeating", ToMesh(seats, DenseKey("tentseating")), _parkTimber, Vector3.zero,
                                    Quaternion.identity, Vector3.one, layer, "Wood");
            Hide(seatGo);

            // The canvas that came down lies on the floor as well.
            var lying = new MeshBuild { UVScale = 0.4f };
            Cloth(lying, new Vector3(-7f, 0.3f, 6f), new Vector3(9f, 0f, 3f), new Vector3(2f, 0.2f, 9f), 5, 5, 0.5f, 90, 0.1f);
            RideDecor(tent, "TentFallenCanvas", lying, _rideCanvasRed, false);

            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject($"TentLight{i}");
                go.transform.SetParent(tent, false);
                go.transform.localPosition = new Vector3(i == 0 ? 0f : (i == 1 ? 12f : -12f), 5.5f + (i == 0 ? 6f : 0f), i == 0 ? 0f : -4f);
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.66f, 0.36f);
                light.intensity = i == 0 ? 8f : 4.2f;
                light.range = i == 0 ? 36f : 22f;
                light.shadows = LightShadows.None;
            }

            // Banners and bulbs over the east doorway, which is the one on the path.
            var banner = new MeshBuild { UVScale = 0.4f };
            Cloth(banner, new Vector3(R, wallH + 2.4f, -4.5f), new Vector3(0f, 0f, 9f), new Vector3(0f, -2.8f, 0f), 4, 2, 0.4f, 33, 0.1f);
            RideDecor(tent, "TentBanner", banner, _rideRed);
            AddRideBulbs(tent, BackdropLayer(), "TentEaveBulbs", new Vector3(0f, wallH - 0.1f, 0f), R, 48, false, new System.Random(77));
        }

        /// <summary>A circus cage wagon: a box on four wheels with bars across the open side.</summary>
        private static void CageWagon(MeshBuild b, Vector3 centre, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => centre + rot * new Vector3(x, y, z);

            b.Box(P(0f, 1.0f, 0f), new Vector3(5.2f, 0.25f, 2.4f), rot);
            b.Box(P(0f, 3.0f, 0f), new Vector3(5.4f, 0.3f, 2.6f), rot);
            b.Box(P(-2.55f, 2.0f, 0f), new Vector3(0.14f, 2.0f, 2.3f), rot);
            b.Box(P(0f, 2.0f, -1.15f), new Vector3(5.1f, 2.0f, 0.12f), rot);
            for (int i = 0; i <= 12; i++)
            {
                if (rng.NextDouble() < 0.12) continue;
                b.Tube(P(-2.45f + i * 0.41f, 1.1f, 1.18f), P(-2.45f + i * 0.41f, 2.9f, 1.18f), 0.03f, 0.03f, 3);
            }

            for (int s = -1; s <= 1; s += 2)
                for (int e = -1; e <= 1; e += 2)
                    b.Tube(P(e * 1.8f, 0.55f, s * 1.3f), P(e * 1.8f, 0.55f, s * 1.45f), 0.55f, 0.55f, 10);

            b.Tube(P(-2.6f, 0.9f, 0f), P(-5.2f, 0.75f, 0f), 0.05f, 0.05f, 3);
        }

        // ==================================================================
        // The drop tower
        // ==================================================================
        /// <summary>
        /// A 38m lattice tower, leaning, with its ring of seats stuck 14m up, cables hanging off the
        /// crown and the top section lying on the ground beside it. A silhouette first.
        /// </summary>
        private static void BuildDropTower(Transform parent, int layer, System.Random rng, ParkSite site)
        {
            var at = new Vector3(site.Centre.x, GroundHeightAt(site.Centre.x, site.Centre.y), site.Centre.y);
            var tower = new GameObject("DropTower").transform;
            tower.SetParent(parent, false);
            tower.position = at;
            tower.localRotation = Quaternion.Euler(2.2f, 0f, -3.4f);

            var steel = new MeshBuild { UVScale = 0.5f };
            var ring = new MeshBuild { UVScale = 0.5f };
            var seats = new MeshBuild { UVScale = 0.5f };
            var lying = new MeshBuild { UVScale = 0.5f };

            Lattice(steel, Vector3.zero, Vector3.up * 36f, 3.6f, 0.15f, 0.07f, 18);

            // The crown: a header block with sign boards, and the dangling cables.
            steel.Box(Vector3.up * 37.2f, new Vector3(6.2f, 2.4f, 6.2f), Quaternion.identity);
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector3(i < 2 ? -1.8f : 1.8f, 36.2f, (i & 1) == 0 ? -1.8f : 1.8f);
                Cable(steel, corner, corner + new Vector3(Rand(rng, -2f, 2f), -Rand(rng, 12f, 19f), Rand(rng, -2f, 2f)), 0.8f, 0.04f, 5);
            }

            // The ring of seats, stuck where the brakes failed: two sleeves on the legs and a hoop out to
            // eight seats on drop-down arms.
            const float RingY = 14f;
            Arc(ring, new Vector3(0f, RingY, 0f), Vector3.right, Vector3.forward, 4.6f, 0f, 360f, 0.28f, 20, 5);
            for (int c = 0; c < 4; c++)
            {
                float a = (c * 90f + 45f) * Mathf.Deg2Rad;
                ring.Tube(new Vector3(Mathf.Cos(a) * 1.9f, RingY, Mathf.Sin(a) * 1.9f), new Vector3(Mathf.Cos(a) * 4.6f, RingY, Mathf.Sin(a) * 4.6f), 0.2f, 0.2f, 4);
            }

            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f;
                var arm = new Vector3(Mathf.Cos(a) * 4.6f, RingY, Mathf.Sin(a) * 4.6f);
                if (i == 3 || i == 6) continue;   // seats that came off

                var rot = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
                seats.Box(arm + Vector3.down * 1.4f, new Vector3(1.0f, 0.12f, 0.9f), rot);
                seats.Box(arm + Vector3.down * 0.9f + rot * Vector3.back * 0.4f, new Vector3(0.9f, 1.0f, 0.1f), rot);
                seats.Tube(arm, arm + Vector3.down * 1.4f, 0.04f, 0.04f, 3);
                seats.Tube(arm + rot * Vector3.right * 0.4f, arm + rot * Vector3.right * 0.4f + Vector3.down * 1.3f, 0.025f, 0.025f, 3);
            }

            // The sleeve that carries the ring on the tower.
            steel.Box(new Vector3(0f, RingY, 0f), new Vector3(4.6f, 1.0f, 4.6f), Quaternion.identity);

            // The top section, lying where it fell with its own twisted end.
            Lattice(lying, new Vector3(9f, 0.9f, -7f), new Vector3(24f, 3.2f, 9f), 3.2f, 0.14f, 0.06f, 9);
            lying.Box(new Vector3(24.8f, 3.4f, 10f), new Vector3(6.2f, 2.4f, 6.2f), Quaternion.Euler(15f, 25f, 40f));

            // Seats and a base ring on the ground.
            for (int i = 0; i < 3; i++)
                seats.Box(new Vector3(Rand(rng, -7f, 7f), 0.3f, Rand(rng, -7f, 7f)), new Vector3(1.0f, 0.5f, 0.9f), Quaternion.Euler(Rand(rng, -20f, 20f), Rand(rng, 0f, 360f), Rand(rng, -20f, 20f)));

            RideSolid(tower, layer, "DropTowerSteel", steel, _parkSteel);
            RideDecor(tower, "DropTowerRing", ring, _rideRed);
            RideSolid(tower, layer, "DropTowerSeats", seats, _parkDark);
            RideSolid(tower, layer, "DropTowerFallen", lying, _rideRust);

            var concrete = new MeshBuild { UVScale = 0.3f };
            concrete.Tube(Vector3.zero, Vector3.up * 0.45f, 6.5f, 6.5f, 16);
            var baseGo = MeshObject(tower, "DropTowerBase", ToMesh(concrete, DenseKey("droptowerbase")), _parkConcrete, Vector3.zero,
                                    Quaternion.identity, Vector3.one, layer, "Concrete");
            NoStanding(baseGo);

            var light = new GameObject("DropTowerLight");
            light.transform.SetParent(tower, false);
            light.transform.localPosition = new Vector3(0f, RingY + 2f, 0f);
            var l = light.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.65f, 0.78f, 1f);
            l.intensity = 6f;
            l.range = 34f;
            l.shadows = LightShadows.None;

            BuildFenceRun(parent, layer, rng, new Vector2(at.x - 9f, at.z), 90f, 14f, "DropFenceW");
            BuildFenceRun(parent, layer, rng, new Vector2(at.x, at.z + 9f), 0f, 14f, "DropFenceN");
        }

        // ==================================================================
        // The swing ride
        // ==================================================================
        private static void BuildSwingRide(Transform parent, int layer, System.Random rng, ParkSite site)
        {
            var at = new Vector3(site.Centre.x, GroundHeightAt(site.Centre.x, site.Centre.y), site.Centre.y);
            var ride = new GameObject("SwingRide").transform;
            ride.SetParent(parent, false);
            ride.position = at;

            var steel = new MeshBuild { UVScale = 0.5f };
            var chains = new MeshBuild { UVScale = 0.5f };
            var seats = new MeshBuild { UVScale = 0.5f };
            var canopyA = new MeshBuild { UVScale = 0.5f };
            var canopyB = new MeshBuild { UVScale = 0.5f };
            var lying = new MeshBuild { UVScale = 0.5f };

            const float Mast = 11f, Rim = 7.2f;

            steel.Tube(Vector3.zero, Vector3.up * Mast, 0.9f, 0.55f, 10);
            steel.Tube(Vector3.up * (Mast - 0.9f), Vector3.up * (Mast + 0.2f), 3.2f, 3.2f, 14);
            Arc(steel, new Vector3(0f, Mast - 0.7f, 0f), Vector3.right, Vector3.forward, Rim, 0f, 360f, 0.14f, 28, 4);

            // Eight spokes to the rim, and the canopy over them.
            for (int i = 0; i < 16; i++)
            {
                float a0 = i * Mathf.PI * 2f / 16f, a1 = (i + 1) * Mathf.PI * 2f / 16f;
                var rimA = new Vector3(Mathf.Cos(a0) * (Rim + 0.5f), Mast - 0.3f, Mathf.Sin(a0) * (Rim + 0.5f));
                var rimB = new Vector3(Mathf.Cos(a1) * (Rim + 0.5f), Mast - 0.3f, Mathf.Sin(a1) * (Rim + 0.5f));
                var top = new Vector3(0f, Mast + 2.6f, 0f);
                var build = i % 2 == 0 ? canopyA : canopyB;

                if (i < 10 || i > 12)
                {
                    build.Tri(rimA, top, rimB);
                    build.Tri(rimB, top, rimA);
                }

                if (i % 2 == 0) steel.Tube(new Vector3(0f, Mast - 0.7f, 0f), new Vector3(Mathf.Cos(a0) * Rim, Mast - 0.7f, Mathf.Sin(a0) * Rim), 0.1f, 0.08f, 3);
            }

            // Twenty chains. Most seats hang as they stopped; a few came off and lie on the platform.
            for (int i = 0; i < 20; i++)
            {
                float a = i * Mathf.PI * 2f / 20f;
                var hook = new Vector3(Mathf.Cos(a) * Rim, Mast - 0.7f, Mathf.Sin(a) * Rim);

                if (i % 7 == 3)
                {
                    var down = hook + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.8f;
                    chains.Tube(hook, hook + Vector3.down * 2.2f, 0.016f, 0.016f, 3);
                    lying.Box(new Vector3(Mathf.Cos(a) * (Rim + 1.2f), 0.2f, Mathf.Sin(a) * (Rim + 1.2f)), new Vector3(0.6f, 0.3f, 0.6f),
                              Quaternion.Euler(Rand(rng, -40f, 40f), Rand(rng, 0f, 360f), Rand(rng, -40f, 40f)));
                    continue;
                }

                var seat = hook + Vector3.down * 7.0f + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Rand(rng, 0.3f, 1.4f);
                chains.Tube(hook, seat + Vector3.up * 0.3f, 0.016f, 0.016f, 3);
                var rot = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f);
                seats.Box(seat, new Vector3(0.7f, 0.1f, 0.55f), rot);
                seats.Box(seat + Vector3.up * 0.32f + rot * Vector3.back * 0.25f, new Vector3(0.7f, 0.5f, 0.07f), rot);
            }

            var plate = new MeshBuild { UVScale = 0.3f };
            plate.Tube(Vector3.zero, Vector3.up * 0.35f, 8.8f, 8.8f, 24);

            RideSolid(ride, layer, "SwingMast", steel, _parkSteel);
            RideDecor(ride, "SwingChains", chains, _parkSteel);
            RideSolid(ride, layer, "SwingSeats", seats, _rideRed, "Wood");
            RideSolid(ride, layer, "SwingFallen", lying, _rideRed, "Wood");
            RideDecor(ride, "SwingCanopyA", canopyA, _rideCanvasRed);
            RideDecor(ride, "SwingCanopyB", canopyB, _rideCanvasCream);

            var plateGo = MeshObject(ride, "SwingPlatform", ToMesh(plate, DenseKey("swingplate")), _parkConcrete, Vector3.zero,
                                     Quaternion.identity, Vector3.one, layer, "Concrete");
            NoStanding(plateGo);

            for (int s = 0; s < 3; s++)
            {
                float a = s * Mathf.PI * 2f / 3f + 0.5f;
                BuildFenceRun(parent, layer, rng, new Vector2(at.x + Mathf.Cos(a) * 11.5f, at.z + Mathf.Sin(a) * 11.5f),
                              -a * Mathf.Rad2Deg + 90f, 9f, $"SwingFence{s}");
            }

            AddRideBulbs(ride, BackdropLayer(), "SwingBulbs", new Vector3(0f, Mast - 0.4f, 0f), Rim + 0.5f, 32, false, new System.Random(53));
        }

        // ==================================================================
        // The haunted house
        // ==================================================================
        /// <summary>
        /// A dark-ride house, 26 x 18m, with a face for a facade: two glowing eyes, a mouth for a door
        /// full of teeth, a turret on each corner and a balcony over the porch that is a real firing
        /// position, reached by a stair at each end.
        ///
        /// Inside are five rooms, joined by openings of three metres, with the dark-ride rails
        /// running through them and the cars where they stopped. Five ways in, and the roof off.
        /// </summary>
        private static void BuildHauntedHouse(Transform parent, int layer, System.Random rng, ParkSite site)
        {
            ResolveAttractionMaterials();

            var at = new Vector3(site.Centre.x, GroundHeightAt(site.Centre.x, site.Centre.y), site.Centre.y);
            var house = new GameObject("HauntedHouse").transform;
            house.SetParent(parent, false);
            house.position = at;
            house.localRotation = Quaternion.Euler(0f, site.Yaw, 0f);

            const float W = 13f, D = 9f, H = 7.2f, T = 0.6f;

            // ---- walls, as runs with openings left in them ----
            void Wall(string name, bool alongX, float fixedCoord, float from, float to, float height, params (float c, float w)[] openings)
            {
                var list = new List<(float c, float w)>(openings);
                list.Sort((a, b) => a.c.CompareTo(b.c));
                float cursor = from;

                void Solid(float a, float b, float y0, float y1)
                {
                    if (b - a < 0.05f) return;
                    float mid = (a + b) * 0.5f;
                    var pos = alongX ? new Vector3(mid, (y0 + y1) * 0.5f, fixedCoord) : new Vector3(fixedCoord, (y0 + y1) * 0.5f, mid);
                    var size = alongX ? new Vector3(b - a, y1 - y0, T) : new Vector3(T, y1 - y0, b - a);
                    NoStanding(CreateBlock(house, pos, size, 0f, layer, "Concrete", Color.grey, 0.05f, 0f, name, _hhWall));
                }

                foreach (var o in list)
                {
                    Solid(cursor, o.c - o.w * 0.5f, 0f, height);
                    Solid(o.c - o.w * 0.5f, o.c + o.w * 0.5f, 3.6f, height);
                    cursor = o.c + o.w * 0.5f;
                }

                Solid(cursor, to, 0f, height);
            }

            Wall("WallFront", true, D, -W, W, H, (0f, 5f), (-9f, 3.2f), (9f, 3.2f));
            Wall("WallBack", true, -D, -W, W, H, (8f, 3.4f), (-8f, 3.4f));
            Wall("WallWest", false, -W, -D, D, H, (-2f, 3.2f));
            Wall("WallEast", false, W, -D, D, H, (3f, 3.2f));

            // Five rooms: two partitions across the house, one along it, each with openings.
            Wall("PartitionA", false, -4.5f, -D, D, H - 0.4f, (-3.5f, 3.2f), (4.5f, 3.2f));
            Wall("PartitionB", false, 4.5f, -D, D, H - 0.4f, (-1.5f, 3.2f), (6.5f, 3.2f));
            Wall("PartitionC", true, 0.5f, -W, -4.5f, H - 0.4f, (-8.5f, 3.2f));
            Wall("PartitionD", true, -1.5f, 4.5f, W, H - 0.4f, (9f, 3.4f));

            // ---- the facade ----
            var trim = new MeshBuild { UVScale = 0.4f };
            var glow = new MeshBuild { UVScale = 0.4f };
            var bone = new MeshBuild { UVScale = 0.4f };
            var roof = new MeshBuild { UVScale = 0.4f };
            var iron = new MeshBuild { UVScale = 0.5f };

            // The upper wall and gable of the front, built as a face.
            NoStanding(CreateBlock(house, new Vector3(0f, H + 2.2f, D), new Vector3(2f * W, 4.4f, T), 0f, layer, "Concrete", Color.grey,
                                   0.05f, 0f, "FrontUpper", _hhWall));
            trim.Tri(new Vector3(-W, H + 4.4f, D + 0.05f), new Vector3(W, H + 4.4f, D + 0.05f), new Vector3(0f, H + 8.4f, D + 0.05f));
            trim.Tri(new Vector3(W, H + 4.4f, D - 0.05f), new Vector3(-W, H + 4.4f, D - 0.05f), new Vector3(0f, H + 8.4f, D - 0.05f));

            // Eyes, nose, and the mouth's teeth.
            for (int e = -1; e <= 1; e += 2)
            {
                glow.Tube(new Vector3(e * 4.6f, H + 2.0f, D + 0.15f), new Vector3(e * 4.6f, H + 2.0f, D + 0.45f), 1.5f, 1.5f, 16);
                trim.Tube(new Vector3(e * 4.6f, H + 2.0f, D + 0.1f), new Vector3(e * 4.6f, H + 2.0f, D + 0.3f), 1.9f, 1.9f, 16);
                trim.Box(new Vector3(e * 4.6f, H + 3.9f, D + 0.3f), new Vector3(3.6f, 0.4f, 0.4f), Quaternion.Euler(0f, 0f, e * -16f));
            }

            trim.Tube(new Vector3(0f, H + 1.3f, D + 0.1f), new Vector3(0f, H + 1.3f, D + 0.4f), 0.6f, 0.2f, 3);

            for (int i = -3; i <= 3; i++)
            {
                if (rng.NextDouble() < 0.18) continue;
                bone.Box(new Vector3(i * 0.62f, 3.6f - 0.4f, D + 0.35f), new Vector3(0.4f, 0.9f - Mathf.Abs(i) * 0.1f, 0.28f), Quaternion.identity);
            }

            // The sign across the gable: raised bars where the lettering was.
            for (int i = 0; i < 12; i++)
                if (rng.NextDouble() > 0.2)
                    bone.Box(new Vector3(-5.5f + i * 1.0f, H + 5.4f, D + 0.25f), new Vector3(0.5f, 1.1f, 0.2f), Quaternion.identity);

            // Turrets at the front corners, with conical roofs.
            foreach (float x in new[] { -W, W })
            {
                trim.Tube(new Vector3(x, 0f, D), new Vector3(x, H + 5f, D), 2.5f, 2.3f, 12);
                roof.Tube(new Vector3(x, H + 5f, D), new Vector3(x, H + 10f, D), 3.0f, 0.12f, 12);
                glow.Tube(new Vector3(x, H + 3f, D + 2.2f), new Vector3(x, H + 3f, D + 2.45f), 0.55f, 0.55f, 8);
            }

            // ---- the roof: two slopes, mostly gone, and the rafters showing ----
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 9; i++)
                    for (int b = 0; b < 3; b++)
                    {
                        if (rng.NextDouble() < 0.55) continue;
                        float x0 = Mathf.Lerp(-W - 0.8f, W + 0.8f, i / 9f), x1 = Mathf.Lerp(-W - 0.8f, W + 0.8f, (i + 1f) / 9f);
                        float t0 = b / 3f, t1 = (b + 1f) / 3f;
                        float z0 = side * Mathf.Lerp(D + 1f, 0f, t0), z1 = side * Mathf.Lerp(D + 1f, 0f, t1);
                        float y0 = Mathf.Lerp(H, H + 5f, t0), y1 = Mathf.Lerp(H, H + 5f, t1);
                        roof.Quad(new Vector3(x0, y0, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y0, z0));
                        roof.Quad(new Vector3(x1, y0, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), new Vector3(x0, y0, z0));
                    }

            for (int i = 0; i < 8; i++)
            {
                float x = -W + 1f + i * (2f * W - 2f) / 7f;
                iron.Tube(new Vector3(x, H, -D - 0.5f), new Vector3(x, H + 5f, 0f), 0.1f, 0.08f, 3);
                if (i % 3 != 1) iron.Tube(new Vector3(x, H, D + 0.5f), new Vector3(x, H + 5f, 0f), 0.1f, 0.08f, 3);
            }

            // ---- the balcony over the porch ----
            const int BalconySteps = 14;
            float deckY = FgStairHeight(BalconySteps);
            CreateBlock(house, new Vector3(0f, deckY - 0.2f, D + 2.2f), new Vector3(12f, 0.4f, 3.6f), 0f, layer, "Wood", Color.grey,
                        0.05f, 0f, "Balcony", _hhTrim);
            for (int x = -1; x <= 1; x += 2)
                iron.Tube(new Vector3(x * 5.4f, 0f, D + 3.8f), new Vector3(x * 5.4f, deckY - 0.3f, D + 3.8f), 0.2f, 0.2f, 6);
            iron.Tube(new Vector3(0f, 0f, D + 3.8f), new Vector3(0f, deckY - 0.3f, D + 3.8f), 0.2f, 0.2f, 6);

            // Rail round the front and the sides, with bars missing.
            for (float x = -5.8f; x <= 5.8f; x += 0.7f)
                if (rng.NextDouble() > 0.12)
                    iron.Tube(new Vector3(x, deckY, D + 3.9f), new Vector3(x, deckY + 1.0f, D + 3.9f), 0.03f, 0.03f, 3);
            iron.Tube(new Vector3(-5.8f, deckY + 1.0f, D + 3.9f), new Vector3(5.8f, deckY + 1.0f, D + 3.9f), 0.05f, 0.05f, 4);

            FgStair(house, layer, _hhTrim, _parkSteel, new Vector3(-6f - FgStairRun(BalconySteps), 0f, D + 2.2f), 90f, BalconySteps, 2.4f);
            FgStair(house, layer, _hhTrim, _parkSteel, new Vector3(6f + FgStairRun(BalconySteps), 0f, D + 2.2f), 270f, BalconySteps, 2.4f);

            // ---- inside: the track, and the cars where they stopped ----
            var rails = new MeshBuild { UVScale = 0.5f };
            var cars = new MeshBuild { UVScale = 0.5f };
            var seats = new MeshBuild { UVScale = 0.5f };
            var webs = new MeshBuild { UVScale = 0.5f };

            var run = new List<Vector3>
            {
                new Vector3(0f, 0.08f, D - 1f), new Vector3(0f, 0.08f, 3f), new Vector3(-3.5f, 0.08f, 0f), new Vector3(-9f, 0.08f, -3.5f),
                new Vector3(-9f, 0.08f, -7f), new Vector3(-2f, 0.08f, -6.5f), new Vector3(1.5f, 0.08f, -3f), new Vector3(7f, 0.08f, -2f),
                new Vector3(9f, 0.08f, 2f), new Vector3(9f, 0.08f, D - 1f)
            };

            for (int i = 0; i < run.Count - 1; i++)
            {
                var a = run[i]; var b = run[i + 1];
                var dir = (b - a).normalized;
                var side = Vector3.Cross(Vector3.up, dir) * 0.45f;
                rails.Tube(a - side, b - side, 0.045f, 0.045f, 3);
                rails.Tube(a + side, b + side, 0.045f, 0.045f, 3);
                for (float t = 0.2f; t < Vector3.Distance(a, b); t += 1.1f)
                    rails.Box(a + dir * t, new Vector3(1.1f, 0.05f, 0.14f), Quaternion.LookRotation(dir));
            }

            CoasterCar(cars, seats, new Vector3(-9f, 0.5f, -5f), 30f, 0f);
            CoasterCar(cars, seats, new Vector3(0f, 0.5f, 5.5f), 80f, 4f);
            CoasterCar(cars, seats, new Vector3(8.6f, 0.4f, 0.5f), 12f, 14f);

            // Cobwebs, and a skeleton sat in the third room.
            for (int i = 0; i < 9; i++)
                Cloth(webs, new Vector3(Rand(rng, -W + 1f, W - 1f), Rand(rng, 4.5f, 6.4f), Rand(rng, -D + 1f, D - 1f)),
                      new Vector3(Rand(rng, 1.5f, 3f), 0f, 0f), new Vector3(0f, -Rand(rng, 1.5f, 3f), Rand(rng, -1f, 1f)), 3, 3, 0.2f, 500 + i, 0.4f);

            bone.Tube(new Vector3(-9f, 0.9f, 5f), new Vector3(-9f, 1.5f, 5f), 0.22f, 0.2f, 6);
            bone.Tube(new Vector3(-9f, 0.4f, 5f), new Vector3(-9f, 0.9f, 5f), 0.18f, 0.14f, 4);
            bone.Tube(new Vector3(-9.3f, 0.9f, 5f), new Vector3(-8.3f, 0.2f, 5.4f), 0.06f, 0.05f, 3);
            bone.Tube(new Vector3(-8.7f, 0.9f, 5f), new Vector3(-9.8f, 0.2f, 5.3f), 0.06f, 0.05f, 3);

            // ---- outside: a dead tree, stones, a fence with a gate ----
            var dead = new MeshBuild { UVScale = 0.5f };
            var tree = new Vector3(-9f, 0f, D + 8f);
            dead.Tube(tree, tree + new Vector3(0.4f, 5.5f, 0f), 0.45f, 0.18f, 6);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.1f;
                var start = tree + Vector3.up * (2.5f + i * 0.5f);
                dead.Tube(start, start + new Vector3(Mathf.Cos(a) * 2.6f, 1.3f, Mathf.Sin(a) * 2.6f), 0.12f, 0.03f, 3);
            }

            for (int i = 0; i < 9; i++)
            {
                float x = Rand(rng, -W, W), z = D + Rand(rng, 6f, 14f);
                var rot = Quaternion.Euler(Rand(rng, -12f, 12f), Rand(rng, -20f, 20f), Rand(rng, -8f, 8f));
                bone.Box(new Vector3(x, 0.55f, z), new Vector3(0.7f, 1.1f, 0.18f), rot);
            }

            for (float x = -W - 2f; x <= W + 2f; x += 2.4f)
            {
                if (Mathf.Abs(x) < 3.5f || rng.NextDouble() < 0.15) continue;
                iron.Tube(new Vector3(x, 0f, D + 17f), new Vector3(x, 1.8f, D + 17f), 0.035f, 0.035f, 3);
            }

            iron.Tube(new Vector3(-W - 2f, 1.5f, D + 17f), new Vector3(-4f, 1.5f, D + 17f), 0.03f, 0.03f, 3);
            iron.Tube(new Vector3(4f, 1.5f, D + 17f), new Vector3(W + 2f, 1.5f, D + 17f), 0.03f, 0.03f, 3);

            RideDecor(house, "HauntedTrim", trim, _hhTrim);
            RideDecor(house, "HauntedGlow", glow, _hhGlow, false);
            RideSolid(house, layer, "HauntedBones", bone, _hhBone, "Concrete");
            RideDecor(house, "HauntedRoof", roof, _hhTrim);
            RideSolid(house, layer, "HauntedIron", iron, _parkSteel);
            RideDecor(house, "HauntedRails", rails, _parkSteel, false);
            RideSolid(house, layer, "HauntedCars", cars, _parkDark);
            RideDecor(house, "HauntedSeats", seats, _hhTrim);
            RideDecor(house, "HauntedWebs", webs, _hhBone);
            RideSolid(house, layer, "HauntedDeadTree", dead, _hhTrim, "Wood");

            void Glow(string name, Vector3 local, float range, float intensity, Color colour)
            {
                var go = new GameObject(name);
                go.transform.SetParent(house, false);
                go.transform.localPosition = local;
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = colour;
                light.intensity = intensity;
                light.range = range;
                light.shadows = LightShadows.None;
            }

            var green = new Color(0.35f, 0.95f, 0.5f);
            Glow("EyeGlow", new Vector3(0f, H + 2f, D + 3f), 18f, 4.5f, green);
            Glow("RoomGlowA", new Vector3(-9f, 3.4f, 4f), 18f, 7f, green);
            Glow("RoomGlowB", new Vector3(9f, 3.4f, -4f), 18f, 7f, new Color(0.75f, 0.4f, 0.95f));
            Glow("RoomGlowC", new Vector3(0f, 3.4f, 1f), 18f, 6f, green);
            Glow("RoomGlowD", new Vector3(-9f, 3.4f, -6f), 16f, 5.5f, new Color(0.95f, 0.55f, 0.3f));
            Glow("RoomGlowE", new Vector3(9f, 3.4f, 5f), 16f, 5.5f, green);
            Glow("PorchLight", new Vector3(0f, 2.8f, D + 2.2f), 12f, 3.5f, new Color(1f, 0.6f, 0.3f));
        }

        // ==================================================================
        // The hedge maze
        // ==================================================================
        /// <summary>
        /// A hedge maze of eight by eight cells, each four metres, with three-metre aisles.
        ///
        /// <b>It is a spanning tree plus loops.</b> A recursive-backtracker carves a path to every
        /// cell, so every aisle is reachable from every other; a dozen more walls are then taken
        /// down so it has loops and a fight inside it is not one corridor. Both openings are on the
        /// outside, one on the side the path arrives from. The hedges are solid, 2.4m high and
        /// never stood on.
        /// </summary>
        private static void BuildHedgeMaze(Transform parent, int layer, System.Random rng, ParkSite site)
        {
            ResolveAttractionMaterials();

            var at = new Vector3(site.Centre.x, GroundHeightAt(site.Centre.x, site.Centre.y), site.Centre.y);
            var maze = new GameObject("HedgeMaze").transform;
            maze.SetParent(parent, false);
            maze.position = at;

            const int N = 8;
            const float Cell = 4f, Half = N * Cell * 0.5f;

            // wallEast[i,j]: the wall on the east side of cell (i,j); wallNorth[i,j]: on its north side.
            var east = new bool[N, N];
            var north = new bool[N, N];
            for (int i = 0; i < N; i++) for (int j = 0; j < N; j++) { east[i, j] = true; north[i, j] = true; }

            var seen = new bool[N, N];
            var stack = new Stack<Vector2Int>();
            stack.Push(new Vector2Int(0, 0));
            seen[0, 0] = true;

            var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

            while (stack.Count > 0)
            {
                var c = stack.Peek();
                var options = new List<Vector2Int>();
                foreach (var d in dirs)
                {
                    var n = c + d;
                    if (n.x >= 0 && n.y >= 0 && n.x < N && n.y < N && !seen[n.x, n.y]) options.Add(d);
                }

                if (options.Count == 0) { stack.Pop(); continue; }

                var pick = options[rng.Next(options.Count)];
                var next = c + pick;

                if (pick.x == 1) east[c.x, c.y] = false;
                else if (pick.x == -1) east[next.x, next.y] = false;
                else if (pick.y == 1) north[c.x, c.y] = false;
                else north[next.x, next.y] = false;

                seen[next.x, next.y] = true;
                stack.Push(next);
            }

            // Braid it: knock out a dozen interior walls.
            for (int k = 0; k < 14; k++)
            {
                int i = rng.Next(0, N - 1), j = rng.Next(0, N - 1);
                if (rng.NextDouble() < 0.5) east[i, j] = false; else north[i, j] = false;
            }

            var hedge = new MeshBuild { UVScale = 0.4f };

            void Segment(Vector3 centre, bool alongX)
            {
                var size = alongX ? new Vector3(Cell + 0.9f, 2.4f, 1.0f) : new Vector3(1.0f, 2.4f, Cell + 0.9f);
                hedge.Box(centre + Vector3.up * 1.2f, size, Quaternion.identity);

                // Rough, uneven top: a few blobs of leaf along the crest.
                for (int b = -1; b <= 1; b++)
                {
                    var off = alongX ? new Vector3(b * 1.4f, 0f, 0f) : new Vector3(0f, 0f, b * 1.4f);
                    LeafBlob(hedge, centre + off + new Vector3(Rand(rng, -0.2f, 0.2f), 2.5f, Rand(rng, -0.2f, 0.2f)),
                             Rand(rng, 0.6f, 0.9f), rng.Next(1, 99999));
                }
            }

            // Which of the outer edges is open: the south cell nearest the path, and one on the west.
            int southGate = N - 2, westGate = 3;

            for (int i = 0; i < N; i++)
                for (int j = 0; j < N; j++)
                {
                    float cx = -Half + (i + 0.5f) * Cell, cz = -Half + (j + 0.5f) * Cell;

                    if (east[i, j] && !(i == N - 1 && false)) Segment(new Vector3(cx + Cell * 0.5f, 0f, cz), false);
                    if (north[i, j]) Segment(new Vector3(cx, 0f, cz + Cell * 0.5f), true);

                    // The outer south and west edges, with a gate left in each.
                    if (j == 0 && i != southGate) Segment(new Vector3(cx, 0f, cz - Cell * 0.5f), true);
                    if (i == 0 && j != westGate) Segment(new Vector3(cx - Cell * 0.5f, 0f, cz), false);
                }

            var go = MeshObject(maze, "Hedges", ToMesh(hedge, DenseKey("hedges")), _hedgeMat, Vector3.zero, Quaternion.identity,
                                Vector3.one, layer, "Wood");
            NoStanding(go);
            Hide(go);

            // The middle: a dry bird bath and a bench, for whoever gets there.
            var centreProps = new MeshBuild { UVScale = 0.5f };
            centreProps.Tube(Vector3.zero, Vector3.up * 0.9f, 0.3f, 0.22f, 8);
            centreProps.Tube(Vector3.up * 0.9f, Vector3.up * 1.0f, 0.9f, 0.9f, 12);
            RideSolid(maze, layer, "MazeCentre", centreProps, _parkRubble, "Concrete");

            var light = new GameObject("MazeLight");
            light.transform.SetParent(maze, false);
            light.transform.localPosition = new Vector3(0f, 3.2f, 0f);
            var l = light.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.85f, 0.7f, 0.5f);
            l.intensity = 3.2f;
            l.range = 16f;
            l.shadows = LightShadows.None;
        }

        // ==================================================================
        // The clock tower
        // ==================================================================
        private static void BuildClockTower(Transform parent, int layer, System.Random rng, ParkSite site)
        {
            ResolveAttractionMaterials();

            var at = new Vector3(site.Centre.x, GroundHeightAt(site.Centre.x, site.Centre.y), site.Centre.y);
            var tower = new GameObject("ClockTower").transform;
            tower.SetParent(parent, false);
            tower.position = at;
            tower.localRotation = Quaternion.Euler(1.2f, 20f, 2.4f);

            var brick = new MeshBuild { UVScale = 0.3f };
            var face = new MeshBuild { UVScale = 0.4f };
            var hands = new MeshBuild { UVScale = 0.4f };
            var roof = new MeshBuild { UVScale = 0.4f };
            var dark = new MeshBuild { UVScale = 0.4f };

            // Shaft, clock stage, belfry and its eight columns.
            brick.Box(new Vector3(0f, 5.0f, 0f), new Vector3(5.0f, 10f, 5.0f), Quaternion.identity);
            brick.Box(new Vector3(0f, 10.2f, 0f), new Vector3(5.6f, 0.5f, 5.6f), Quaternion.identity);
            brick.Box(new Vector3(0f, 14.2f, 0f), new Vector3(4.6f, 8f, 4.6f), Quaternion.identity);
            brick.Box(new Vector3(0f, 18.5f, 0f), new Vector3(5.2f, 0.6f, 5.2f), Quaternion.identity);
            for (int i = 0; i < 4; i++)
            {
                float x = i < 2 ? -2f : 2f, z = (i & 1) == 0 ? -2f : 2f;
                brick.Box(new Vector3(x, 20.6f, z), new Vector3(0.6f, 3.6f, 0.6f), Quaternion.identity);
            }

            // Clock faces on four sides: a pale disc, twelve ticks, two hands, one face gone dark.
            for (int side = 0; side < 4; side++)
            {
                var rot = Quaternion.Euler(0f, side * 90f, 0f);
                var n = rot * Vector3.forward;
                var centre = new Vector3(0f, 14.8f, 0f) + n * 2.35f;
                var disc = side == 2 ? dark : face;
                disc.Tube(centre - n * 0.05f, centre + n * 0.15f, 1.7f, 1.7f, 20);

                for (int t = 0; t < 12; t++)
                {
                    float a = t * Mathf.PI * 2f / 12f;
                    var dir = rot * new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
                    dark.Tube(centre + n * 0.18f + dir * 1.4f, centre + n * 0.18f + dir * 1.62f, 0.04f, 0.04f, 3);
                }

                float ha = Rand(rng, 0f, 6.283f);
                hands.Tube(centre + n * 0.22f, centre + n * 0.22f + rot * new Vector3(Mathf.Sin(ha), Mathf.Cos(ha), 0f) * 1.2f, 0.05f, 0.03f, 3);
                float hb = ha + 2.3f;
                hands.Tube(centre + n * 0.22f, centre + n * 0.22f + rot * new Vector3(Mathf.Sin(hb), Mathf.Cos(hb), 0f) * 0.8f, 0.07f, 0.04f, 3);
            }

            // The roof: a pyramid on four sides, one side fallen in.
            var apex = new Vector3(0f, 26.5f, 0f);
            for (int i = 0; i < 4; i++)
            {
                if (i == 1) continue;
                float a0 = (i * 90f + 45f) * Mathf.Deg2Rad, a1 = ((i + 1) * 90f + 45f) * Mathf.Deg2Rad;
                var p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 3.8f + Vector3.up * 22.4f;
                var p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 3.8f + Vector3.up * 22.4f;
                roof.Tri(p0, apex, p1);
                roof.Tri(p1, apex, p0);
            }

            roof.Tube(apex, apex + Vector3.up * 2.4f, 0.06f, 0.03f, 3);

            // The fallen bell, and the lintel it took with it.
            var bell = new MeshBuild { UVScale = 0.4f };
            bell.Tube(new Vector3(5f, 0.7f, 3f), new Vector3(6.2f, 0.9f, 4f), 0.9f, 1.3f, 10);

            var shaft = MeshObject(tower, "ClockTowerBody", ToMesh(brick, DenseKey("clocktower")), _hallBrick, Vector3.zero,
                                   Quaternion.identity, Vector3.one, layer, "Concrete");
            NoStanding(shaft);
            Hide(shaft);

            RideDecor(tower, "ClockFaces", face, _parkCanvas);
            RideDecor(tower, "ClockDark", dark, _parkDark);
            RideDecor(tower, "ClockHands", hands, _parkSteel);
            RideDecor(tower, "ClockRoof", roof, _hallRoofMat);
            RideSolid(tower, layer, "ClockBell", bell, _rideBrass);

            var light = new GameObject("ClockLight");
            light.transform.SetParent(tower, false);
            light.transform.localPosition = new Vector3(0f, 14.8f, 4.5f);
            var l = light.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.78f, 0.5f);
            l.intensity = 4.6f;
            l.range = 22f;
            l.shadows = LightShadows.None;
        }

        // ==================================================================
        // The bandstand
        // ==================================================================
        /// <summary>
        /// An octagonal bandstand on the plaza: a floor three steps up (walkable, two stairs), eight
        /// columns and a roof of torn canvas, with the music stands still set out.
        /// </summary>
        private static void BuildBandstand(Transform parent, int layer, System.Random rng, Vector2 where)
        {
            ResolveAttractionMaterials();

            var at = new Vector3(where.x, GroundHeightAt(where.x, where.y), where.y);
            var stand = new GameObject("Bandstand").transform;
            stand.SetParent(parent, false);
            stand.position = at;

            const int Steps = 3;
            float floorY = FgStairHeight(Steps);
            const float R = 5.6f;

            var floor = new MeshBuild { UVScale = 0.3f };
            floor.Tube(new Vector3(0f, 0.1f, 0f), new Vector3(0f, floorY, 0f), R, R, 8);
            var floorGo = MeshObject(stand, "BandstandFloor", ToMesh(floor, DenseKey("bandstandfloor")), _parkTimber,
                                     Vector3.zero, Quaternion.identity, Vector3.one, layer, "Wood");
            Hide(floorGo);

            foreach (float deg in new[] { 90f, 270f })
            {
                float a = deg * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var foot = dir * (R * 0.92f + FgStairRun(Steps));
                float yaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
                FgStair(stand, layer, _parkTimber, _parkSteel, foot, yaw, Steps, 2.4f, rails: false);
            }

            var iron = new MeshBuild { UVScale = 0.5f };
            var roofA = new MeshBuild { UVScale = 0.5f };
            var roofB = new MeshBuild { UVScale = 0.5f };

            const float PostH = 4.4f;
            for (int i = 0; i < 8; i++)
            {
                float a0 = (i * 45f + 22.5f) * Mathf.Deg2Rad, a1 = ((i + 1) * 45f + 22.5f) * Mathf.Deg2Rad;
                var pa = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * (R - 0.4f);
                var pb = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * (R - 0.4f);

                // A gap on each stair side: the columns skip the two bays that face the steps.
                iron.Tube(pa + Vector3.up * floorY, pa + Vector3.up * (floorY + PostH), 0.14f, 0.1f, 6);

                bool stairBay = i == 1 || i == 5;
                if (!stairBay)
                {
                    for (float t = 0.2f; t < 0.9f; t += 0.35f)
                        iron.Tube(Vector3.Lerp(pa, pb, 0f) + Vector3.up * (floorY + 0.15f), Vector3.Lerp(pa, pb, 1f) + Vector3.up * (floorY + 0.15f), 0.03f, 0.03f, 3);
                    iron.Tube(pa + Vector3.up * (floorY + 1.0f), pb + Vector3.up * (floorY + 1.0f), 0.04f, 0.04f, 3);
                }

                var apex = new Vector3(0f, floorY + PostH + 2.4f, 0f);
                var topA = pa * 1.2f + Vector3.up * (floorY + PostH); var topB = pb * 1.2f + Vector3.up * (floorY + PostH);
                if (i != 3 && rng.NextDouble() > 0.15)
                {
                    var build = i % 2 == 0 ? roofA : roofB;
                    build.Tri(topA, apex, topB);
                    build.Tri(topB, apex, topA);
                }

                iron.Tube(topA, apex, 0.05f, 0.04f, 3);
            }

            // Music stands, left set out.
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.0f + 0.4f;
                var foot = new Vector3(Mathf.Cos(a), floorY, Mathf.Sin(a)) * 2.4f;
                foot.y = floorY;
                iron.Tube(foot, foot + Vector3.up * 1.2f, 0.02f, 0.02f, 3);
                iron.Box(foot + Vector3.up * 1.25f, new Vector3(0.5f, 0.35f, 0.04f), Quaternion.Euler(-20f, a * 57f, 0f));
            }

            RideSolid(stand, layer, "BandstandIron", iron, _parkSteel);
            RideDecor(stand, "BandstandRoofA", roofA, _rideCanvasRed);
            RideDecor(stand, "BandstandRoofB", roofB, _rideCanvasCream);
        }
    }
}
#endif
