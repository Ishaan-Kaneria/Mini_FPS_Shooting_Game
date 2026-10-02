#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The streets of the fairground: the gate, the midway, the back-of-house yard, the ride
    /// graveyard and the hoarding round the edge.
    ///
    /// <b>Dense, but not blocks.</b> A midway is two rows of booths facing each other across an
    /// avenue, and every booth here is built from the parts a booth has -- a back wall and shelving
    /// with prizes on it, two side walls, a counter with a side hatch left open, posts, a striped
    /// awning with a scalloped edge, a sign board with its lettering and a roof of canvas. Four
    /// kinds (a game, a food stand, a fortune teller, a shooting gallery) and a strength-tester
    /// tower at intervals, so a walk down it is not one booth repeated.
    ///
    /// <b>Where the fight goes.</b> The rows have gaps so the avenue is not a corridor; behind
    /// them is a second rank of tents, reached through the gaps; and a food court, with its tables
    /// and umbrellas, sits where the avenue widens.
    /// </summary>
    public partial class FPSKitSceneBuilder
    {
        /// <summary>Everything one booth is made of, batched by what it is made from.</summary>
        private sealed class StallSet
        {
            public readonly MeshBuild Wood = new MeshBuild { UVScale = 0.5f };
            public readonly MeshBuild Steel = new MeshBuild { UVScale = 0.5f };
            public readonly MeshBuild CanvasA = new MeshBuild { UVScale = 0.4f };
            public readonly MeshBuild CanvasB = new MeshBuild { UVScale = 0.4f };
            public readonly MeshBuild Paint = new MeshBuild { UVScale = 0.5f };
            public readonly MeshBuild Prizes = new MeshBuild { UVScale = 0.5f };
            public readonly MeshBuild Bone = new MeshBuild { UVScale = 0.5f };
        }

        private static Material _midwayPaintB;

        // ==================================================================
        // The midway
        // ==================================================================
        private static void BuildMidwayStreet(Transform root, int layer, int backdrop, System.Random rng)
        {
            ResolveAttractionMaterials();

            var group = new GameObject("Midway").transform;
            group.SetParent(root, false);

            var left = new StallSet();
            var right = new StallSet();
            int stalls = 0;

            bool NearSpur(int side, float z)
            {
                if (side > 0) return Mathf.Abs(z + 52f) < 6f || Mathf.Abs(z + 106f) < 6f;
                return Mathf.Abs(z + 64f) < 7f;
            }

            for (int side = -1; side <= 1; side += 2)
            {
                var set = side < 0 ? left : right;
                float yaw = side < 0 ? 90f : 270f;

                // Front rank: booths facing the avenue.
                for (int i = 0; i < 8; i++)
                {
                    float z = -108f + i * 9.4f + Rand(rng, -0.5f, 0.5f);
                    if (NearSpur(side, z)) continue;
                    if (side < 0 && Mathf.Abs(z + 80f) < 8f) continue;      // the food court
                    if (rng.NextDouble() < 0.14) continue;                    // a gap, so it is a street and not a wall

                    float x = side * 9.2f;
                    int kind = (i + (side > 0 ? 2 : 0)) % 5;
                    Stall(set, new Vector3(x, GroundHeightAt(x, z), z), yaw, kind, rng);
                    Keep(x, z, 4.4f);
                    stalls++;
                }

                // Second rank: bigger striped tents, set back and staggered.
                for (int i = 0; i < 7; i++)
                {
                    float z = -103f + i * 10.8f + Rand(rng, -1f, 1f);
                    if (NearSpur(side, z) || rng.NextDouble() < 0.3) continue;

                    float x = side * Rand(rng, 21f, 24f);
                    SideshowTent(set, new Vector3(x, GroundHeightAt(x, z), z), side < 0 ? 90f : 270f, rng);
                    Keep(x, z, 6.2f);
                    stalls++;
                }
            }

            EmitStalls(group, layer, "MidwayWest", left);
            EmitStalls(group, layer, "MidwayEast", right);

            // ---- the food court, west of the avenue ----
            BuildFoodCourt(group, layer, new Vector2(-17f, -80f), rng);

            // ---- strings of bulbs across the avenue, from stall to stall ----
            var wire = new MeshBuild { UVScale = 0.5f };
            var bulbs = new MeshBuild { UVScale = 0.5f };
            for (float z = -108f; z < -40f; z += 9.4f)
            {
                if (rng.NextDouble() < 0.3) continue;
                var a = new Vector3(-9.8f, GroundHeightAt(-9.8f, z) + 4.4f, z);
                var b = new Vector3(9.8f, GroundHeightAt(9.8f, z) + 4.4f, z);
                Festoon(wire, bulbs, a, b, rng);
            }

            EmitFurniture(group, layer, backdrop, "MidwayWire", wire, _parkSteel, false);
            EmitFurniture(group, layer, backdrop, "MidwayBulbs", bulbs, _fgBulb, false);

            Debug.Log($"[FPSKit] fairground midway: {stalls} booth(s) and tent(s).");
        }

        private static void EmitStalls(Transform parent, int layer, string name, StallSet s)
        {
            RideSolid(parent, layer, name + "Wood", s.Wood, _parkTimber, "Wood");
            RideSolid(parent, layer, name + "Steel", s.Steel, _parkSteel);
            RideSolid(parent, layer, name + "Paint", s.Paint, _parkPaint, "Wood");
            RideDecor(parent, name + "CanvasA", s.CanvasA, _rideCanvasRed);
            RideDecor(parent, name + "CanvasB", s.CanvasB, _rideCanvasCream);
            RideDecor(parent, name + "Prizes", s.Prizes, _midwayPaintB);
            RideDecor(parent, name + "Bone", s.Bone, _hhBone);
        }

        /// <summary>
        /// One booth. <paramref name="foot"/> is the centre of its footprint, <paramref name="yaw"/>
        /// turns its front (+z) to face the avenue; <paramref name="kind"/> is what it sold.
        /// </summary>
        private static void Stall(StallSet s, Vector3 foot, float yaw, int kind, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => foot + rot * new Vector3(x, y, z);

            float w = Rand(rng, 5.0f, 6.4f), d = Rand(rng, 3.4f, 4.0f);
            int hatch = rng.NextDouble() < 0.5 ? -1 : 1;

            // Back wall with its battens, and a side wall each end that slopes with the roof.
            s.Wood.Box(P(0f, 1.6f, -d * 0.5f), new Vector3(w, 3.2f, 0.14f), rot);
            for (int b = -2; b <= 2; b++)
                s.Wood.Box(P(b * w * 0.2f, 1.6f, -d * 0.5f + 0.1f), new Vector3(0.1f, 3.1f, 0.06f), rot);

            for (int e = -1; e <= 1; e += 2)
            {
                s.Wood.Box(P(e * w * 0.5f, 1.3f, 0f), new Vector3(0.12f, 2.6f, d), rot);
                s.Wood.Box(P(e * w * 0.5f, 3.0f, 0f), new Vector3(0.12f, 0.8f, d * 0.7f), rot * Quaternion.Euler(6f, 0f, 0f));
            }

            // The counter, in two lengths with the side hatch left between them.
            float hatchW = 1.5f;
            float length = w - hatchW;
            float cx = hatch * -hatchW * 0.5f * 0f;
            float counterStart = hatch > 0 ? -w * 0.5f : -w * 0.5f + hatchW;
            s.Paint.Box(P(counterStart + length * 0.5f, 0.55f, d * 0.5f - 0.25f), new Vector3(length, 1.1f, 0.55f), rot);
            s.Wood.Box(P(counterStart + length * 0.5f, 1.14f, d * 0.5f - 0.18f), new Vector3(length + 0.2f, 0.07f, 0.8f), rot);

            // The shelves behind, with what was on them.
            for (int shelf = 0; shelf < 2; shelf++)
            {
                float y = 1.45f + shelf * 0.8f;
                s.Wood.Box(P(0f, y, -d * 0.5f + 0.32f), new Vector3(w - 0.5f, 0.06f, 0.45f), rot);

                int count = 4 + rng.Next(4);
                for (int i = 0; i < count; i++)
                {
                    float x = -w * 0.5f + 0.6f + i * (w - 1.2f) / Mathf.Max(1, count - 1);
                    if (rng.NextDouble() < 0.25) continue;
                    float size = Rand(rng, 0.22f, 0.4f);
                    s.Prizes.Box(P(x, y + 0.03f + size * 0.5f, -d * 0.5f + 0.32f), new Vector3(size, size, size * 0.8f), rot * Quaternion.Euler(0f, Rand(rng, -30f, 30f), 0f));
                }
            }

            // Front posts, and the awning: striped strips on a slope, a few torn and one hanging.
            for (int e = -1; e <= 1; e += 2)
                s.Wood.Tube(P(e * (w * 0.5f - 0.15f), 0f, d * 0.5f + 1.3f), P(e * (w * 0.5f - 0.15f), 2.8f, d * 0.5f + 1.3f), 0.07f, 0.06f, 4);

            int strips = Mathf.RoundToInt(w / 0.8f);
            for (int st = 0; st < strips; st++)
            {
                if (rng.NextDouble() < 0.1) continue;

                float x0 = -w * 0.5f - 0.15f + st * (w + 0.3f) / strips, x1 = x0 + (w + 0.3f) / strips;
                float hang = rng.NextDouble() < 0.12 ? 1.2f : 0f;
                var a = P(x0, 3.25f, d * 0.5f - 0.1f); var b = P(x1, 3.25f, d * 0.5f - 0.1f);
                var c = P(x1, 2.6f - hang, d * 0.5f + 1.5f); var e = P(x0, 2.6f - hang, d * 0.5f + 1.5f);
                var set = st % 2 == 0 ? s.CanvasA : s.CanvasB;
                set.Quad(a, e, c, b); set.Quad(b, c, e, a);

                // The scalloped hem.
                var mid = (e + c) * 0.5f + Vector3.down * 0.3f;
                set.Tri(e, mid, c); set.Tri(c, mid, e);
            }

            s.Steel.Tube(P(-w * 0.5f, 2.62f, d * 0.5f + 1.45f), P(w * 0.5f, 2.62f, d * 0.5f + 1.45f), 0.03f, 0.03f, 3);

            // The sign board over everything, with raised bars where the lettering was.
            s.Paint.Box(P(0f, 3.85f, d * 0.5f - 0.1f), new Vector3(w * 0.92f, 0.95f, 0.14f), rot);
            for (int i = 0; i < 9; i++)
            {
                if (rng.NextDouble() < 0.25) continue;
                float h = Rand(rng, 0.4f, 0.6f);
                s.Bone.Box(P(-w * 0.38f + i * (w * 0.76f) / 8f, 3.85f, d * 0.5f), new Vector3(0.26f, h, 0.08f), rot);
            }

            // The roof canvas: a sagging sheet over the box, partly torn.
            Cloth(s.CanvasB, P(-w * 0.5f - 0.2f, 3.25f, -d * 0.5f), rot * new Vector3(w + 0.4f, 0f, 0f), rot * new Vector3(0f, -0.3f, d + 0.1f),
                  4, 3, 0.25f, rng.Next(1, 99999), 0.18f);

            // What makes it a game, or a kitchen, or a fortune teller.
            switch (kind)
            {
                case 0:   // ring toss: a row of posts on the counter
                    for (int i = 0; i < 6; i++)
                        s.Steel.Tube(P(-w * 0.35f + i * 0.5f + (hatch > 0 ? 0f : 1f), 1.15f, d * 0.5f - 0.5f),
                                     P(-w * 0.35f + i * 0.5f + (hatch > 0 ? 0f : 1f), 1.6f, d * 0.5f - 0.5f), 0.02f, 0.02f, 3);
                    s.Prizes.Tube(P(0f, 1.15f, d * 0.5f - 0.4f), P(0f, 1.3f, d * 0.5f - 0.4f), 0.4f, 0.3f, 8);
                    break;

                case 1:   // food: a chimney, a griddle, a menu
                    s.Steel.Tube(P(w * 0.3f, 3.2f, -d * 0.5f + 0.5f), P(w * 0.3f, 5.0f, -d * 0.5f + 0.5f), 0.18f, 0.14f, 6);
                    s.Steel.Box(P(-w * 0.2f, 1.0f, 0.1f), new Vector3(1.4f, 0.12f, 0.9f), rot);
                    s.Steel.Box(P(-w * 0.2f, 0.5f, 0.1f), new Vector3(1.4f, 1.0f, 0.9f), rot);
                    s.Paint.Box(P(0f, 2.5f, -d * 0.5f + 0.1f), new Vector3(w * 0.5f, 0.8f, 0.05f), rot);
                    break;

                case 2:   // fortune teller: a curtain either side, a crystal ball on the counter
                    s.CanvasB.Box(P(-w * 0.4f, 1.9f, d * 0.5f - 0.3f), new Vector3(0.9f, 2.6f, 0.06f), rot);
                    s.CanvasB.Box(P(w * 0.4f, 1.9f, d * 0.5f - 0.3f), new Vector3(0.9f, 2.6f, 0.06f), rot);
                    s.Steel.Tube(P(0f, 1.18f, d * 0.5f - 0.4f), P(0f, 1.62f, d * 0.5f - 0.4f), 0.22f, 0.22f, 8);
                    break;

                case 3:   // shooting gallery: a row of tin targets and a rifle on a chain
                    for (int i = 0; i < 5; i++)
                        s.Steel.Tube(P(-w * 0.35f + i * 0.7f, 2.3f, -d * 0.5f + 0.2f), P(-w * 0.35f + i * 0.7f, 2.3f, -d * 0.5f + 0.26f), 0.2f, 0.2f, 8);
                    s.Steel.Tube(P(-0.4f, 1.15f, d * 0.5f - 0.25f), P(0.5f, 1.35f, d * 0.5f - 0.1f), 0.03f, 0.03f, 4);
                    break;

                default:  // strength tester: a tower beside the booth, with its bell
                    float tx = (hatch > 0 ? w * 0.5f + 0.7f : -w * 0.5f - 0.7f);
                    s.Steel.Tube(P(tx, 0f, d * 0.5f - 0.2f), P(tx, 5.6f, d * 0.5f - 0.2f), 0.14f, 0.1f, 6);
                    s.Steel.Tube(P(tx, 5.6f, d * 0.5f - 0.2f), P(tx, 5.9f, d * 0.5f - 0.2f), 0.4f, 0.1f, 8);
                    s.Prizes.Box(P(tx, 0.4f, d * 0.5f - 0.2f), new Vector3(0.7f, 0.8f, 0.7f), rot);
                    break;
            }
        }

        /// <summary>A larger striped tent for the second rank: a ridge, two sides, an open front.</summary>
        private static void SideshowTent(StallSet s, Vector3 foot, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => foot + rot * new Vector3(x, y, z);

            float w = Rand(rng, 7f, 9f), d = Rand(rng, 6f, 8f), h = 3.4f, ridge = 5.4f;

            // Poles, ridge and eaves.
            for (int e = -1; e <= 1; e += 2)
                for (int f = -1; f <= 1; f += 2)
                    s.Steel.Tube(P(e * w * 0.5f, 0f, f * d * 0.5f), P(e * w * 0.5f, h, f * d * 0.5f), 0.09f, 0.08f, 4);

            s.Steel.Tube(P(0f, ridge, -d * 0.5f), P(0f, ridge, d * 0.5f), 0.09f, 0.09f, 4);
            s.Steel.Tube(P(0f, 0f, -d * 0.5f), P(0f, ridge, -d * 0.5f), 0.1f, 0.09f, 4);
            s.Steel.Tube(P(0f, 0f, d * 0.5f), P(0f, ridge, d * 0.5f), 0.1f, 0.09f, 4);

            // Canvas: both slopes in stripes along the ridge, the back wall, one open side.
            int strips = 6;
            for (int side = -1; side <= 1; side += 2)
                for (int st = 0; st < strips; st++)
                {
                    if (rng.NextDouble() < 0.12) continue;
                    float z0 = -d * 0.5f + st * d / strips, z1 = z0 + d / strips;
                    var set = st % 2 == 0 ? s.CanvasA : s.CanvasB;
                    var a = P(side * w * 0.5f, h, z0); var b = P(side * w * 0.5f, h, z1);
                    var c = P(0f, ridge, z1); var e = P(0f, ridge, z0);
                    set.Quad(a, e, c, b); set.Quad(b, c, e, a);
                }

            Cloth(s.CanvasA, P(-w * 0.5f, 0.2f, -d * 0.5f), rot * new Vector3(w, 0f, 0f), rot * new Vector3(0f, h, 0f), 4, 3, 0.2f, rng.Next(1, 99999), 0.2f);
            s.Wood.Box(P(0f, 0.55f, d * 0.5f - 0.3f), new Vector3(w * 0.5f, 1.1f, 0.4f), rot);

            // A banner over the front, and a stack of crates inside.
            s.Paint.Box(P(0f, h + 0.6f, d * 0.5f + 0.1f), new Vector3(w * 0.8f, 1.0f, 0.12f), rot);
            s.Wood.Box(P(w * 0.2f, 0.4f, -d * 0.25f), new Vector3(1.0f, 0.8f, 1.0f), rot);
            s.Wood.Box(P(w * 0.2f + 0.2f, 1.1f, -d * 0.25f), new Vector3(0.8f, 0.6f, 0.8f), rot * Quaternion.Euler(0f, 20f, 0f));
        }

        /// <summary>A food court: round tables with umbrellas and chairs, some of them over.</summary>
        private static void BuildFoodCourt(Transform parent, int layer, Vector2 centre, System.Random rng)
        {
            var wood = new MeshBuild { UVScale = 0.5f };
            var steel = new MeshBuild { UVScale = 0.5f };
            var umbrella = new MeshBuild { UVScale = 0.5f };

            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                {
                    if (rng.NextDouble() < 0.15) continue;

                    var p = centre + new Vector2(-5f + col * 5.5f, -5f + row * 5f);
                    var foot = new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y);
                    HallTable(wood, steel, foot, rng);

                    // The umbrella: a pole, and a cone of canvas -- inside out, or gone, on some.
                    steel.Tube(foot, foot + Vector3.up * 2.8f, 0.035f, 0.03f, 4);
                    if (rng.NextDouble() < 0.7)
                    {
                        var top = foot + Vector3.up * 2.9f;
                        for (int i = 0; i < 8; i++)
                        {
                            float a0 = i * Mathf.PI * 2f / 8f, a1 = (i + 1) * Mathf.PI * 2f / 8f;
                            var q0 = foot + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 1.8f + Vector3.up * 2.35f;
                            var q1 = foot + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 1.8f + Vector3.up * 2.35f;
                            if (rng.NextDouble() < 0.12) continue;
                            umbrella.Tri(q0, top, q1);
                            umbrella.Tri(q1, top, q0);
                        }
                    }

                    Keep(p.x, p.y, 2.2f);
                }

            RideSolid(parent, layer, "FoodCourtTables", wood, _parkTimber, "Wood");
            RideSolid(parent, layer, "FoodCourtSteel", steel, _parkSteel);
            RideDecor(parent, "FoodCourtUmbrellas", umbrella, _rideCanvasRed);
        }

        // ==================================================================
        // The gate
        // ==================================================================
        private static void BuildGate(Transform root, int layer, System.Random rng)
        {
            var site = _entrancePlan;
            var gate = new GameObject("EntrancePlaza").transform;
            gate.SetParent(root, false);
            gate.position = new Vector3(site.Centre.x, GroundHeightAt(site.Centre.x, site.Centre.y), site.Centre.y);
            gate.localRotation = Quaternion.Euler(0f, site.Yaw, 0f);

            var brick = new MeshBuild { UVScale = 0.3f };
            var steel = new MeshBuild { UVScale = 0.5f };
            var paint = new MeshBuild { UVScale = 0.5f };
            var bone = new MeshBuild { UVScale = 0.5f };
            var canvas = new MeshBuild { UVScale = 0.5f };

            // Two towers, with a cap each and a ball on top of it.
            foreach (int side in new[] { -1, 1 })
            {
                float x = side * 8.6f;
                brick.Box(new Vector3(x, 5.5f, 0f), new Vector3(3.4f, 11f, 3.4f), Quaternion.identity);
                brick.Box(new Vector3(x, 11.2f, 0f), new Vector3(4.2f, 0.7f, 4.2f), Quaternion.identity);
                brick.Box(new Vector3(x, 0.6f, 0f), new Vector3(4.2f, 1.2f, 4.2f), Quaternion.identity);
                paint.Tube(new Vector3(x, 11.5f, 0f), new Vector3(x, 12.9f, 0f), 1.1f, 0.05f, 10);
                bone.Box(new Vector3(x, 8.4f, 1.72f), new Vector3(1.6f, 2.0f, 0.1f), Quaternion.identity);
            }

            // The gantry between them: a truss, a sign on it, and a lintel of bars.
            FlatTruss(steel, new Vector3(-8.6f, 9.4f, 0f), new Vector3(8.6f, 9.4f, 0f), Vector3.up * 1.6f, 1.6f, 0.1f, 0.06f, 16);
            paint.Box(new Vector3(0f, 10.2f, 0.12f), new Vector3(13f, 1.0f, 0.18f), Quaternion.identity);
            for (int i = 0; i < 14; i++)
                if (rng.NextDouble() > 0.2)
                    bone.Box(new Vector3(-5.9f + i * 0.9f, 10.2f, 0.28f), new Vector3(0.42f, Rand(rng, 0.5f, 0.75f), 0.08f), Quaternion.identity);

            // Turnstiles: a pole and three arms, in two banks either side of the open middle.
            foreach (int side in new[] { -1, 1 })
                for (int i = 0; i < 3; i++)
                {
                    float x = side * (3.6f + i * 1.25f);
                    steel.Tube(new Vector3(x, 0f, 1f), new Vector3(x, 1.25f, 1f), 0.07f, 0.07f, 5);
                    float turn = Rand(rng, 0f, 120f);
                    for (int arm = 0; arm < 3; arm++)
                    {
                        float a = (turn + arm * 120f) * Mathf.Deg2Rad;
                        steel.Tube(new Vector3(x, 1.2f, 1f), new Vector3(x + Mathf.Cos(a) * 0.7f, 1.1f, 1f + Mathf.Sin(a) * 0.7f), 0.03f, 0.03f, 3);
                    }

                    steel.Box(new Vector3(x + side * 0.6f, 0.55f, 1f), new Vector3(0.1f, 1.1f, 0.6f), Quaternion.identity);
                }

            // The bunting that hangs from the gantry to the towers' feet, shredded.
            for (int i = 0; i < 6; i++)
                Cloth(canvas, new Vector3(-8f + i * 2.6f, 9.2f, 0.3f), new Vector3(2.4f, 0f, 0f), new Vector3(0f, -Rand(rng, 1.2f, 2.4f), 0f), 2, 2, 0.2f, 40 + i, 0.18f);

            RideSolid(gate, layer, "GateBrick", brick, _hallBrick, "Concrete");
            RideSolid(gate, layer, "GateSteel", steel, _parkSteel);
            RideSolid(gate, layer, "GatePaint", paint, _parkPaint, "Wood");
            RideDecor(gate, "GateBone", bone, _hhBone);
            RideDecor(gate, "GateBunting", canvas, _rideCanvasRed);

            // Fence runs either side of the gate, so the park has an edge here, with the booths behind.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int k = 0; k < 3; k++)
                    BuildFenceRun(root, layer, rng, ParkPoint(site, side * (14f + k * 9.5f), 0f), site.Yaw, 9.2f, $"GateFence{side}_{k}");

                BuildBooth(root, layer, rng, ParkPoint(site, side * 15f, 7f), $"Gate{side}A");
                BuildBooth(root, layer, rng, ParkPoint(site, side * 19.5f, 7f), $"Gate{side}B");

                // Queue lanes outside, in a switchback.
                for (int row = 0; row < 3; row++)
                    BuildFenceRun(root, layer, rng, ParkPoint(site, side * 6f, -6f - row * 3.2f), site.Yaw, 9f, $"GateQueue{side}_{row}");
            }

            // The mascot over the gantry.
            var at = ParkPoint(site, 0f, 9f);
            BuildMascotSign(gate, layer, new Vector3(at.x, GroundHeightAt(at.x, at.y), at.y), site.Yaw + 180f, 0.9f);

            // A bent barrier arm and a pile of ticket stubs.
            var barrier = CreateBlock(gate, new Vector3(2.2f, 1.1f, -3.2f), new Vector3(5.2f, 0.12f, 0.12f), -9f, layer, "Wood",
                                      new Color(0.64f, 0.16f, 0.11f), 0.06f, 0f, "BarrierArm", _parkPaint);
            NoStanding(barrier);
        }

        // ==================================================================
        // Containers, and the two yards
        // ==================================================================
        /// <summary>A shipping container: corrugated sides drawn as ribs, doors at one end.</summary>
        private static void Container(MeshBuild b, Vector3 centre, float yaw, float height = 2.64f, bool body = true)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => centre + rot * new Vector3(x, y, z);

            if (body) b.Box(P(0f, height * 0.5f, 0f), new Vector3(2.44f, height, 6.1f), rot);
            for (int i = -6; i <= 6; i++)
                for (int s = -1; s <= 1; s += 2)
                    b.Box(P(s * 1.24f, height * 0.5f, i * 0.45f), new Vector3(0.06f, height - 0.2f, 0.18f), rot);

            b.Box(P(0f, height * 0.5f, 3.08f), new Vector3(2.3f, height - 0.1f, 0.06f), rot);
            b.Box(P(0f, height * 0.5f, 3.12f), new Vector3(0.06f, height - 0.15f, 0.06f), rot);
        }

        /// <summary>
        /// A container you can climb: a solid, walkable body whose roof is the deck, with a flight
        /// of stairs up the end of it. The corrugation is drawn separately, off the bake.
        /// </summary>
        private static void LookoutContainer(Transform parent, int layer, Vector3 centre, float yaw, int steps)
        {
            float height = FgStairHeight(steps);
            var rot = Quaternion.Euler(0f, yaw, 0f);

            var block = CreateBlock(parent, centre + Vector3.up * (height * 0.5f), new Vector3(2.44f, height, 6.1f), yaw, layer,
                                    "Metal", Color.grey, 0.1f, 0.2f, "ContainerRoof", _rideRust);
            Mark(block, new Color(0.45f, 0.3f, 0.22f), 1);

            // The stair climbs towards the container from its back end.
            var foot = centre + rot * new Vector3(0f, 0f, -3.05f - FgStairRun(steps));
            FgStair(parent, layer, _rideRust, _parkSteel, foot, yaw, steps, 1.6f, rails: false);
        }

        private static void BuildMaintenanceYard(Transform root, int layer, System.Random rng)
        {
            if (!_hasMaintenance) return;

            var site = _maintenancePlan;
            var yard = new GameObject("MaintenanceYard").transform;
            yard.SetParent(root, false);
            yard.position = new Vector3(site.Centre.x, GroundHeightAt(site.Centre.x, site.Centre.y), site.Centre.y);
            yard.localRotation = Quaternion.Euler(0f, site.Yaw, 0f);

            // ---- the workshop: 16 x 10, walk-in, with a roller door, three other doors and a half roof ----
            const float WW = 8f, WD = 5f, WH = 5.2f, T = 0.3f;

            void Wall(string name, bool alongX, float fixedCoord, float from, float to, params (float c, float w)[] openings)
            {
                var list = new List<(float c, float w)>(openings);
                list.Sort((a, c) => a.c.CompareTo(c.c));
                float cursor = from;

                void Solid(float a, float b, float y0, float y1)
                {
                    if (b - a < 0.05f) return;
                    float mid = (a + b) * 0.5f;
                    var pos = alongX ? new Vector3(mid, (y0 + y1) * 0.5f, fixedCoord) : new Vector3(fixedCoord, (y0 + y1) * 0.5f, mid);
                    var size = alongX ? new Vector3(b - a, y1 - y0, T) : new Vector3(T, y1 - y0, b - a);
                    NoStanding(CreateBlock(yard, pos, size, 0f, layer, "Metal", Color.grey, 0.1f, 0.2f, name, _parkConcrete));
                }

                foreach (var o in list)
                {
                    Solid(cursor, o.c - o.w * 0.5f, 0f, WH);
                    Solid(o.c - o.w * 0.5f, o.c + o.w * 0.5f, 4.2f, WH);
                    cursor = o.c + o.w * 0.5f;
                }

                Solid(cursor, to, 0f, WH);
            }

            Wall("ShopFront", true, WD, -WW, WW, (0f, 5.6f), (-6f, 2.6f));
            Wall("ShopBack", true, -WD, -WW, WW, (4f, 2.6f));
            Wall("ShopWest", false, -WW, -WD, WD, (1f, 2.8f));
            Wall("ShopEast", false, WW, -WD, WD, (-1f, 3f));

            var steel = new MeshBuild { UVScale = 0.5f };
            var sheet = new MeshBuild { UVScale = 0.4f };
            var wood = new MeshBuild { UVScale = 0.5f };
            var rust = new MeshBuild { UVScale = 0.5f };
            var paint = new MeshBuild { UVScale = 0.5f };

            // Trusses and the corrugated roof, mostly stripped.
            for (int i = 0; i <= 4; i++)
            {
                float x = -WW + i * WW * 0.5f;
                steel.Tube(new Vector3(x, WH, -WD), new Vector3(x, WH + 1.8f, 0f), 0.09f, 0.08f, 4);
                steel.Tube(new Vector3(x, WH + 1.8f, 0f), new Vector3(x, WH, WD), 0.09f, 0.08f, 4);
                steel.Tube(new Vector3(x, WH, -WD), new Vector3(x, WH, WD), 0.07f, 0.07f, 3);
            }

            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 8; i++)
                {
                    if (rng.NextDouble() < 0.5) continue;
                    float x0 = -WW + i * WW * 0.25f, x1 = x0 + WW * 0.25f;
                    sheet.Quad(new Vector3(x0, WH, side * WD), new Vector3(x0, WH + 1.8f, 0f), new Vector3(x1, WH + 1.8f, 0f), new Vector3(x1, WH, side * WD));
                    sheet.Quad(new Vector3(x1, WH, side * WD), new Vector3(x1, WH + 1.8f, 0f), new Vector3(x0, WH + 1.8f, 0f), new Vector3(x0, WH, side * WD));
                }

            // Benches, a lathe, a hoist, shelving and what hangs from the rafters.
            wood.Box(new Vector3(-5.5f, 0.55f, -3.9f), new Vector3(4.6f, 1.1f, 1.1f), Quaternion.identity);
            wood.Box(new Vector3(0f, 0.55f, -3.9f), new Vector3(4.2f, 1.1f, 1.1f), Quaternion.identity);
            rust.Box(new Vector3(5.4f, 0.8f, -3.4f), new Vector3(2.2f, 1.6f, 1.0f), Quaternion.identity);
            for (int i = 0; i < 4; i++)
                rust.Box(new Vector3(7.6f, 0.9f + i * 0.7f, 0.5f + (i % 2) * 0.1f), new Vector3(0.5f, 0.05f, 4.2f), Quaternion.identity);
            rust.Tube(new Vector3(7.7f, 0f, -1.8f), new Vector3(7.7f, 3.4f, -1.8f), 0.06f, 0.06f, 3);
            rust.Tube(new Vector3(7.7f, 0f, 2.8f), new Vector3(7.7f, 3.4f, 2.8f), 0.06f, 0.06f, 3);

            var horses = new MeshBuild { UVScale = 0.5f };
            var poles = new MeshBuild { UVScale = 0.5f };
            Horse(horses, poles, new Vector3(-2.5f, 3.4f, -1f), 90f, 0.9f, false, 0.01f, 3);
            Horse(horses, poles, new Vector3(1.5f, 3.2f, -1.5f), 40f, 0.9f, true, 0.01f, 7);
            steel.Tube(new Vector3(-2.5f, WH, -1f), new Vector3(-2.5f, 3.4f + 1.9f, -1f), 0.02f, 0.02f, 3);
            steel.Tube(new Vector3(1.5f, WH, -1.5f), new Vector3(1.5f, 3.2f + 1.9f, -1.5f), 0.02f, 0.02f, 3);

            // ---- the water tower: four lattice legs, a tank and a conical roof ----
            var tower = new MeshBuild { UVScale = 0.5f };
            var tankOrigin = new Vector3(13f, 0f, -13f);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Lattice(tower, tankOrigin + new Vector3(sx * 2.4f, 0f, sz * 2.4f), tankOrigin + new Vector3(sx * 2.0f, 15f, sz * 2.0f), 0.5f, 0.06f, 0.035f, 7);

            for (float y = 3.5f; y < 15f; y += 3.8f)
            {
                tower.Tube(tankOrigin + new Vector3(-2.2f, y, -2.2f), tankOrigin + new Vector3(2.2f, y + 1.8f, 2.2f), 0.03f, 0.03f, 3);
                tower.Tube(tankOrigin + new Vector3(2.2f, y, -2.2f), tankOrigin + new Vector3(-2.2f, y + 1.8f, 2.2f), 0.03f, 0.03f, 3);
            }

            tower.Tube(tankOrigin + Vector3.up * 15f, tankOrigin + Vector3.up * 19f, 3.4f, 3.4f, 14);
            tower.Tube(tankOrigin + Vector3.up * 19f, tankOrigin + Vector3.up * 21f, 3.6f, 0.2f, 14);
            // The ladder on one leg, and a rung out of every five.
            for (float y = 1f; y < 15f; y += 0.45f)
                if (rng.NextDouble() > 0.2)
                    tower.Tube(tankOrigin + new Vector3(2.6f, y, -0.4f), tankOrigin + new Vector3(2.6f, y, 0.4f), 0.02f, 0.02f, 3);

            // ---- containers: two on the ground, one with a stair up its roof ----
            var red = new MeshBuild { UVScale = 0.4f };
            var blue = new MeshBuild { UVScale = 0.4f };
            Container(red, new Vector3(-14f, 0f, 6f), 90f);
            Container(blue, new Vector3(-14f, 0f, 9f), 88f);
            Container(blue, new Vector3(-14f, 0f, 12.2f), 91f);
            Container(red, new Vector3(-14f, 2.64f, 7.5f), 90f);
            Container(rust, new Vector3(-4f, 0f, 15f), 0f, 2.64f, body: false);

            // A fuel tank on cradles, and drums.
            rust.Tube(new Vector3(-14f, 1.2f, -10f), new Vector3(-8f, 1.2f, -10f), 1.3f, 1.3f, 12);
            rust.Box(new Vector3(-12.5f, 0.3f, -10f), new Vector3(0.5f, 0.6f, 2.4f), Quaternion.identity);
            rust.Box(new Vector3(-9.5f, 0.3f, -10f), new Vector3(0.5f, 0.6f, 2.4f), Quaternion.identity);
            for (int i = 0; i < 9; i++)
            {
                var p = new Vector3(-6f + (i % 3) * 0.7f, 0.5f, 10f + (i / 3) * 0.7f);
                rust.Tube(p - Vector3.up * 0.5f, p + Vector3.up * 0.45f, 0.3f, 0.3f, 8);
            }

            // Ride parts waiting for a repair that was never made: horses on pallets, in a row.
            for (int i = 0; i < 6; i++)
            {
                wood.Box(new Vector3(6f + i * 1.8f, 0.1f, 9f), new Vector3(1.4f, 0.2f, 2.4f), Quaternion.identity);
                Horse(horses, poles, new Vector3(6f + i * 1.8f, 0.25f, 9f), 90f + (i % 2) * 8f, 0.85f, i % 3 == 0, 0.01f, 20 + i);
            }

            // A gondola on trestles, half stripped.
            var cabins = new MeshBuild { UVScale = 0.5f };
            var cabinCanopy = new MeshBuild { UVScale = 0.5f };
            Gondola(cabins, cabinCanopy, new Vector3(-8f, 3.4f, 18f), 30f, 8f);
            rust.Box(new Vector3(-8.7f, 0.5f, 18f), new Vector3(0.3f, 1.0f, 2f), Quaternion.identity);
            rust.Box(new Vector3(-7.3f, 0.5f, 18f), new Vector3(0.3f, 1.0f, 2f), Quaternion.identity);

            RideSolid(yard, layer, "YardSteel", steel, _parkSteel);
            RideDecor(yard, "YardSheet", sheet, _parkSteel);
            RideSolid(yard, layer, "YardBenches", wood, _parkTimber, "Wood");
            RideSolid(yard, layer, "YardRust", rust, _rideRust);
            RideSolid(yard, layer, "YardHorses", horses, _rideRed, "Wood");
            RideDecor(yard, "YardPoles", poles, _rideBrass);
            RideSolid(yard, layer, "YardWaterTower", tower, _parkSteel);
            RideSolid(yard, layer, "YardContainersRed", red, _rideRed);
            RideSolid(yard, layer, "YardContainersBlue", blue, _rideBlue);
            RideSolid(yard, layer, "YardGondola", cabins, _parkSteel);
            RideDecor(yard, "YardGondolaCanopy", cabinCanopy, _rideRed);

            // The lookout: a container whose roof is a real deck, reached by a stair at its end. The
            // body is a walkable block, not part of the merged container mesh, which is held off the
            // bake and would swallow the roof it shares a plane with.
            const int ContainerSteps = 12;
            LookoutContainer(yard, layer, new Vector3(-4f, 0f, 15f), 0f, ContainerSteps);

            BuildServiceVan(yard, layer, new Vector3(12f, 0f, 9f));
            BuildServiceVan(yard, layer, new Vector3(-16f, 0f, -2f));

            // The yard wall: fence runs with two gates.
            var world = yard.position;
            for (int k = 0; k < 5; k++)
            {
                if (k == 2) continue;
                BuildFenceRun(root, layer, rng, ParkPoint(site, -20f + k * 10f, 22f), site.Yaw, 9.6f, $"YardFenceFront{k}");
            }

            for (int k = 0; k < 4; k++)
                BuildFenceRun(root, layer, rng, ParkPoint(site, -22f, -16f + k * 10f), site.Yaw + 90f, 9.6f, $"YardFenceW{k}");

            var floodGo = new GameObject("YardFloodlight");
            floodGo.transform.SetParent(yard, false);
            floodGo.transform.localPosition = new Vector3(0f, 6.5f, 10f);
            var light = floodGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.9f, 0.82f, 0.62f);
            light.intensity = 4.4f;
            light.range = 30f;
            light.shadows = LightShadows.None;
        }

        // ------------------------------------------------------------------
        // The ride graveyard
        // ------------------------------------------------------------------
        private static void BuildRideGraveyard(Transform root, int layer, System.Random rng)
        {
            var site = _graveyardPlan;
            var yard = new GameObject("RideGraveyard").transform;
            yard.SetParent(root, false);
            yard.position = new Vector3(site.Centre.x, GroundHeightAt(site.Centre.x, site.Centre.y), site.Centre.y);
            yard.localRotation = Quaternion.Euler(0f, site.Yaw, 0f);

            var red = new MeshBuild { UVScale = 0.4f };
            var blue = new MeshBuild { UVScale = 0.4f };
            var rust = new MeshBuild { UVScale = 0.5f };
            var horses = new MeshBuild { UVScale = 0.5f };
            var poles = new MeshBuild { UVScale = 0.5f };
            var cars = new MeshBuild { UVScale = 0.5f };
            var seats = new MeshBuild { UVScale = 0.5f };
            var tarp = new MeshBuild { UVScale = 0.4f };
            var cabins = new MeshBuild { UVScale = 0.5f };
            var cabinCanopy = new MeshBuild { UVScale = 0.5f };

            // Rows of containers with lanes between them: four rows, none a dead end.
            for (int row = 0; row < 3; row++)
                for (int i = 0; i < 3; i++)
                {
                    if (rng.NextDouble() < 0.18) continue;
                    var build = (row + i) % 2 == 0 ? red : blue;
                    float x = -18f + row * 7.5f, z = -20f + i * 8.2f + Rand(rng, -0.4f, 0.4f);
                    Container(build, new Vector3(x, 0f, z), Rand(rng, -6f, 6f));
                    if (rng.NextDouble() < 0.35) Container(build, new Vector3(x + 0.1f, 2.64f, z), Rand(rng, -4f, 4f));
                }

            // One lone container with a stair up its roof: the lookout.
            Container(rust, new Vector3(8f, 0f, -14f), 90f, 2.64f, body: false);
            LookoutContainer(yard, layer, new Vector3(8f, 0f, -14f), 90f, 12);

            // Carousel horses, a heap of them; and a flatbed with coaster cars on it.
            for (int i = 0; i < 12; i++)
                Horse(horses, poles, new Vector3(Rand(rng, 4f, 14f), 0.5f + (i / 5) * 0.5f, Rand(rng, 4f, 12f)), Rand(rng, 0f, 360f), 1f,
                      rng.NextDouble() < 0.5, 0.01f, 40 + i);

            rust.Box(new Vector3(-10f, 0.9f, 16f), new Vector3(2.6f, 0.2f, 9f), Quaternion.Euler(0f, 5f, 0f));
            for (int w = -1; w <= 1; w += 2)
                rust.Tube(new Vector3(-10f + w * 1.2f, 0.45f, 14f), new Vector3(-10f + w * 1.4f, 0.45f, 14f), 0.45f, 0.45f, 10);
            for (int c = 0; c < 3; c++)
                CoasterCar(cars, seats, new Vector3(-10f, 1.5f, 13f + c * 3.4f), 0f, 0f);

            // Bumper cars stacked up like a scrapyard.
            for (int i = 0; i < 7; i++)
            {
                var p = new Vector3(14f + (i % 3) * 1.7f, 0.4f + (i / 3) * 0.7f, -4f + (i % 2) * 0.5f);
                cars.Box(p, new Vector3(1.5f, 0.55f, 1.15f), Quaternion.Euler(Rand(rng, -8f, 8f), Rand(rng, 0f, 90f), Rand(rng, -8f, 8f)));
                rust.Tube(p + Vector3.up * 0.3f, p + Vector3.up * 2.4f, 0.03f, 0.03f, 3);
            }

            // A kiddie train on its loop of track, left where it stopped.
            var centre = new Vector3(-6f, 0.08f, -4f);
            Arc(rust, centre, Vector3.right, Vector3.forward, 5.5f, 0f, 360f, 0.05f, 28, 3);
            Arc(rust, centre, Vector3.right, Vector3.forward, 5.0f, 0f, 360f, 0.05f, 28, 3);
            for (int c = 0; c < 4; c++)
            {
                float a = (c * 28f + 20f) * Mathf.Deg2Rad;
                var p = centre + new Vector3(Mathf.Cos(a), 0.45f, Mathf.Sin(a)) * 0f + new Vector3(Mathf.Cos(a) * 5.25f, 0.5f, Mathf.Sin(a) * 5.25f);
                CoasterCar(cars, seats, p, -a * Mathf.Rad2Deg, 0f);
            }

            // Mounds under tarpaulin, and the crane that put them there.
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(Rand(rng, -16f, 14f), 0f, Rand(rng, 12f, 20f));
                rust.Box(p + Vector3.up * 0.8f, new Vector3(2.4f, 1.6f, 3f), Quaternion.Euler(0f, Rand(rng, 0f, 180f), 0f));
                Cloth(tarp, p + new Vector3(-1.7f, 1.9f, -2f), new Vector3(3.4f, 0f, 0f), new Vector3(0f, -0.3f, 4f), 4, 4, 0.3f, 200 + i, 0.06f);
            }

            var crane = new MeshBuild { UVScale = 0.5f };
            Lattice(crane, new Vector3(18f, 0f, 14f), new Vector3(18f, 12f, 14f), 1.2f, 0.07f, 0.04f, 8);
            FlatTruss(crane, new Vector3(18f, 12.3f, 14f), new Vector3(2f, 12.3f, 14f), Vector3.up * 1.0f, 1.0f, 0.06f, 0.04f, 10);
            Cable(crane, new Vector3(5f, 12.3f, 14f), new Vector3(5f, 2.5f, 14f), 0.2f, 0.03f, 4);
            Gondola(cabins, cabinCanopy, new Vector3(5f, 4.3f, 14f), 0f, 10f);

            RideSolid(yard, layer, "GraveRed", red, _rideRed);
            RideSolid(yard, layer, "GraveBlue", blue, _rideBlue);
            RideSolid(yard, layer, "GraveRustContainers", rust, _rideRust);
            RideSolid(yard, layer, "GraveHorses", horses, _rideRed, "Wood");
            RideDecor(yard, "GravePoles", poles, _rideBrass);
            RideSolid(yard, layer, "GraveCars", cars, _rideRed);
            RideDecor(yard, "GraveSeats", seats, _parkDark);
            RideDecor(yard, "GraveTarps", tarp, _hallTarp);
            RideSolid(yard, layer, "GraveCrane", crane, _parkSteel);
            RideSolid(yard, layer, "GraveCabins", cabins, _parkSteel);
            RideDecor(yard, "GraveCabinCanopy", cabinCanopy, _rideRed);

            var lightGo = new GameObject("GraveLight");
            lightGo.transform.SetParent(yard, false);
            lightGo.transform.localPosition = new Vector3(0f, 7f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.85f, 0.75f, 0.55f);
            light.intensity = 4f;
            light.range = 32f;
            light.shadows = LightShadows.None;
        }

        // ==================================================================
        // The plaza
        // ==================================================================
        private static void BuildPlazaDressing(Transform root, int layer, int backdrop, System.Random rng)
        {
            var group = new GameObject("CentralPlaza").transform;
            group.SetParent(root, false);

            // A dry fountain, off to the side of the spawn.
            var at = new Vector3(15f, GroundHeightAt(15f, -17f), -17f);
            group.position = at;
            HallFountain(group, layer, Vector3.zero, rng);

            // Four benches round the pad and four planters at its edge, each with a palm.
            var benches = new MeshBuild { UVScale = 0.5f };
            var planters = new MeshBuild { UVScale = 0.5f };
            var grove = new Grove();

            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 24f;
                if (Vector2.Distance(p, new Vector2(15f, -17f)) < 8f || Vector2.Distance(p, new Vector2(-19f, 17f)) < 9f) continue;

                var foot = new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y);
                planters.Box(foot + Vector3.up * 0.4f, new Vector3(2.2f, 0.8f, 2.2f), Quaternion.identity);
                JunglePalm(grove, rng, foot + Vector3.up * 0.7f, 0.7f);

                for (int s = -1; s <= 1; s += 2)
                {
                    var q = p + new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * s * 4.5f;
                    Bench(benches, new Vector3(q.x, GroundHeightAt(q.x, q.y), q.y), -a * Mathf.Rad2Deg + 90f, rng.NextDouble() < 0.2, rng);
                }
            }

            var worldPlanters = new GameObject("Planters").transform;
            worldPlanters.SetParent(root, false);
            EmitFurniture(worldPlanters, layer, backdrop, "PlazaPlanters", planters, _parkRubble, true);
            EmitFurniture(worldPlanters, layer, backdrop, "PlazaBenches", benches, _parkTimber, true);

            if (grove.Trunks.Triangles.Count > 0)
            {
                var trunks = MeshObject(worldPlanters, "PlazaTrunks", ToMesh(grove.Trunks, DenseKey("plazatrunks")), _fgBark,
                                        Vector3.zero, Quaternion.identity, Vector3.one, layer, "Wood");
                Hide(trunks);
            }

            EmitFoliage(worldPlanters, backdrop, "PlazaFronds", grove.LeafB, _fgLeafB);
        }

        // ==================================================================
        // The hoarding round the edge
        // ==================================================================
        /// <summary>
        /// A plank hoarding along the four edges, with a wide gap at the gate, panels missing and
        /// whole runs leaning. It is where the fairground stops and the jungle starts, and it is
        /// at 204m so the service road has the ground inside it to itself.
        /// </summary>
        private static void BuildHoarding(Transform root, int layer, System.Random rng)
        {
            var wood = new MeshBuild { UVScale = 0.5f };
            var paint = new MeshBuild { UVScale = 0.5f };
            var posts = new MeshBuild { UVScale = 0.5f };

            float Edge = _theme.arenaSize * 0.5f - 12f;

            void Run(Vector2 a, Vector2 b)
            {
                float length = Vector2.Distance(a, b);
                var dir = (b - a) / length;
                int panels = Mathf.RoundToInt(length / 2.4f);

                for (int i = 0; i < panels; i++)
                {
                    var p = a + dir * ((i + 0.5f) * length / panels);
                    if (Mathf.Abs(p.x) < 26f && p.y < -Edge + 24f) continue;     // the gate
                    if (rng.NextDouble() < 0.22) continue;                  // fallen

                    float g = GroundHeightAt(p.x, p.y);
                    float lean = rng.NextDouble() < 0.12 ? Rand(rng, 6f, 20f) : Rand(rng, -2f, 2f);
                    var look = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y));
                    var set = i % 3 == 0 ? paint : wood;
                    set.Box(new Vector3(p.x, g + 1.4f, p.y), new Vector3(0.1f, 2.8f, length / panels - 0.06f), look * Quaternion.Euler(0f, 0f, lean));
                    if (i % 2 == 0)
                        posts.Tube(new Vector3(p.x, g, p.y), new Vector3(p.x, g + 3.1f, p.y), 0.12f, 0.1f, 4);
                }
            }

            Run(new Vector2(-Edge, -Edge), new Vector2(Edge, -Edge));
            Run(new Vector2(-Edge, Edge), new Vector2(Edge, Edge));
            Run(new Vector2(-Edge, -Edge), new Vector2(-Edge, Edge));
            Run(new Vector2(Edge, -Edge), new Vector2(Edge, Edge));

            var group = new GameObject("Hoarding").transform;
            group.SetParent(root, false);
            RideDecor(group, "HoardingPlanks", wood, _parkTimber);
            RideDecor(group, "HoardingPaint", paint, _parkPaint);
            RideDecor(group, "HoardingPosts", posts, _parkSteel);
        }
    }
}
#endif
