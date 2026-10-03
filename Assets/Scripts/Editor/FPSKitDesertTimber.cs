#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The desert's river fence and bridge furniture, made the way a village would make them:
    /// timber posts that lean, wire that sags, boards nailed up where they were needed.
    ///
    /// Ishaan, 2026-10-03: "make the bridge and fencing more random and less quality
    /// (currently it is high tech in the desert)". The fence was a concrete curb, steel
    /// posts at exactly 4.5 m and a picket every 0.75 m; the bridge a Warren steel truss.
    /// Nothing in a desert village is that even. Every height, lean and gap here comes from
    /// a seeded generator keyed on the panel, so a rebuild gives the same fence and two
    /// panels never match.
    ///
    /// <b>Collision is unchanged in kind:</b> one slab per bay and a slab for each gate post,
    /// so the river barrier holds exactly as it did while it looks like a cattle fence.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        /// <summary>A repeatable wobble in metres along the fence, so posts are not evenly spaced.</summary>
        private static float FenceJitter(int index, int side)
        {
            var r = new System.Random(index * 2654 + side * 97 + 11);
            return Rand(r, -0.9f, 0.9f);
        }

        private static void FencePanelRustic(MeshBuild build, MeshBuild solid, MeshBuild signs,
                                             Vector3 a, Vector3 b, int side, bool sign, int index)
        {
            var along = b - a;
            float length = new Vector2(along.x, along.z).magnitude;
            if (length < 0.2f) return;

            var rng = new System.Random(index * 7919 + side * 31 + 5);
            float yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
            var turn = Quaternion.Euler(0f, yaw, 0f);
            var mid = (a + b) * 0.5f;

            // ---- the post at this bay's near end: a log, sunk, leaning a little ----
            bool snapped = rng.NextDouble() < 0.11;
            float height = Rand(rng, 1.45f, 2.05f) * (snapped ? 0.55f : 1f);
            float lean = snapped ? Rand(rng, 0.45f, 0.9f) : Rand(rng, 0.02f, 0.2f);

            var foot = a + Vector3.down * 0.3f;
            var top = a + new Vector3(Rand(rng, -lean, lean), height, Rand(rng, -lean, lean));
            build.Tube(foot, top, 0.14f, 0.095f, 6);

            // A few stones heaped round the foot, to keep it from walking.
            for (int s = 0; s < 3; s++)
                build.Box(a + new Vector3(Rand(rng, -0.35f, 0.35f), 0.1f, Rand(rng, -0.35f, 0.35f)),
                          new Vector3(Rand(rng, 0.2f, 0.4f), Rand(rng, 0.16f, 0.3f), Rand(rng, 0.2f, 0.4f)),
                          Quaternion.Euler(Rand(rng, -20f, 20f), Rand(rng, 0f, 360f), Rand(rng, -20f, 20f)));

            // ---- strands of wire to the next post, sagging; some slack, some gone ----
            float nextHeight = height * Rand(rng, 0.88f, 1.1f);

            foreach (float h in new[] { 0.5f, 0.98f, 1.42f })
            {
                if (h > height * 0.95f) continue;
                if (rng.NextDouble() < 0.16) continue;

                var p0 = Vector3.Lerp(foot, top, Mathf.Clamp01((h + 0.3f) / (height + 0.3f)));
                var p1 = b + Vector3.up * (h * (nextHeight / height) + Rand(rng, -0.06f, 0.06f));
                float sag = Rand(rng, 0.04f, 0.2f);
                var m1 = Vector3.Lerp(p0, p1, 0.5f) - Vector3.up * sag;

                build.Tube(p0, m1, 0.016f, 0.014f, 4);
                build.Tube(m1, p1, 0.014f, 0.016f, 4);
            }

            // ---- sometimes a board or a pole nailed across, where somebody patched it ----
            if (rng.NextDouble() < 0.22)
            {
                float h = Rand(rng, 0.55f, 1.3f);
                build.Box(mid + Vector3.up * h, new Vector3(0.06f, Rand(rng, 0.14f, 0.24f), length * Rand(rng, 0.55f, 0.95f)),
                          turn * Quaternion.Euler(Rand(rng, -4f, 4f), Rand(rng, -3f, 3f), Rand(rng, -9f, 9f)));
            }

            if (sign)
            {
                // A painted board nailed to the post, leaning back, not a steel plate.
                var plate = Quaternion.Euler(-12f + Rand(rng, -6f, 6f), yaw + 90f + Rand(rng, -8f, 8f), Rand(rng, -6f, 6f));
                var on = a + Vector3.up * (height * 0.8f) - turn * Vector3.right * side * 0.2f;
                signs.Box(on, new Vector3(0.04f, 0.44f, 0.66f), plate);
            }

            // Same collision as the steel one: a slab per bay, a little lower now.
            solid.Box(mid + Vector3.up * 1.0f, new Vector3(0.5f, 2.0f, length), turn);
        }

        /// <summary>A gate post made of a thick log and a cairn, with a crossbar notched in.</summary>
        private static void GatePostRustic(MeshBuild build, MeshBuild solid, Vector3 at)
        {
            var rng = new System.Random(Mathf.RoundToInt(at.x * 13f + at.z * 7f));

            build.Tube(at + Vector3.down * 0.4f,
                       at + new Vector3(Rand(rng, -0.15f, 0.15f), Rand(rng, 3.0f, 3.5f), Rand(rng, -0.15f, 0.15f)),
                       0.34f, 0.26f, 7);

            build.Box(at + Vector3.up * 3.2f, new Vector3(Rand(rng, 1.1f, 1.5f), 0.18f, 0.26f),
                      Quaternion.Euler(Rand(rng, -5f, 5f), Rand(rng, 0f, 180f), Rand(rng, -7f, 7f)));

            for (int s = 0; s < 6; s++)
                build.Box(at + new Vector3(Rand(rng, -0.6f, 0.6f), 0.18f + (s / 3) * 0.28f, Rand(rng, -0.6f, 0.6f)),
                          new Vector3(Rand(rng, 0.3f, 0.6f), Rand(rng, 0.22f, 0.4f), Rand(rng, 0.3f, 0.6f)),
                          Quaternion.Euler(Rand(rng, -15f, 15f), Rand(rng, 0f, 360f), Rand(rng, -15f, 15f)));

            solid.Box(at + Vector3.up * 1.9f, new Vector3(1.2f, 3.8f, 1.2f), Quaternion.identity);
        }

        /// <summary>
        /// A handrail for the desert bridge: uneven posts, a pole run in lengths, a rope below.
        /// Along local x at <paramref name="z"/>, in the bridge's own space.
        /// </summary>
        private static void BridgeRailRustic(MeshBuild timber, System.Random rng, float halfLength, float z)
        {
            float x = -halfLength;
            Vector3 lastTop = default;
            bool haveLast = false;

            while (x <= halfLength + 0.01f)
            {
                float h = Rand(rng, 1.0f, 1.4f);
                var foot = new Vector3(x, 0.1f, z + Rand(rng, -0.05f, 0.05f));
                var top = new Vector3(x + Rand(rng, -0.1f, 0.1f), h, z + Rand(rng, -0.12f, 0.12f));
                timber.Tube(foot, top, 0.1f, 0.075f, 6);

                if (haveLast)
                {
                    // A pole between the tops, set slightly proud so the joints show, and a
                    // rope below it that hangs.
                    timber.Tube(lastTop, top + new Vector3(0f, Rand(rng, -0.04f, 0.04f), 0f), 0.06f, 0.055f, 5);

                    var r0 = lastTop - Vector3.up * 0.42f;
                    var r1 = top - Vector3.up * 0.42f;
                    var rm = (r0 + r1) * 0.5f - Vector3.up * Rand(rng, 0.05f, 0.16f);
                    timber.Tube(r0, rm, 0.022f, 0.022f, 4);
                    timber.Tube(rm, r1, 0.022f, 0.022f, 4);
                }

                lastTop = top;
                haveLast = true;
                x += Rand(rng, 1.7f, 2.9f);
            }
        }

        // ==================================================================
        // The crashed plane
        // ==================================================================
        /// <summary>
        /// A lofted fuselage: forty-four rings of eighteen sides along z from the tail cone
        /// (z = -17) to the nose (z = +13), thin at both ends and a constant two metres in the
        /// barrel, with a faint dent noise so the skin is not a machined tube. It replaced two
        /// fourteen-sided tubes and a cone ("improve the quality of everything", 2026-10-03).
        /// Wound like <c>MeshBuild.Tube</c>, so it is outward-facing and solid.
        /// </summary>
        private static void PlaneBody(MeshBuild body, int seed)
        {
            const int rings = 44, sides = 18;
            var pts = new Vector3[rings + 1, sides];

            for (int i = 0; i <= rings; i++)
            {
                float t = i / (float)rings;
                float z = Mathf.Lerp(-17f, 13f, t);

                float r;
                if (z < -10f) r = Mathf.Lerp(0.45f, 2f, Mathf.SmoothStep(0f, 1f, (z + 17f) / 7f));
                else if (z > 8f) r = Mathf.Lerp(2f, 0.38f, Mathf.SmoothStep(0f, 1f, (z - 8f) / 5f));
                else r = 2f;

                float droop = z > 8f ? -0.3f * Mathf.SmoothStep(0f, 1f, (z - 8f) / 5f) : 0f;

                for (int j = 0; j < sides; j++)
                {
                    float a = j * Mathf.PI * 2f / sides;
                    float dent = 1f + Fbm2(z * 0.35f, a * 1.7f, seed, 2) * 0.07f;
                    pts[i, j] = new Vector3(Mathf.Cos(a) * r * dent, Mathf.Sin(a) * r * 0.93f * dent + droop, z);
                }
            }

            for (int i = 0; i < rings; i++)
                for (int j = 0; j < sides; j++)
                {
                    int k = (j + 1) % sides;
                    body.Quad(pts[i, j], pts[i, k], pts[i + 1, k], pts[i + 1, j]);
                }

            for (int k = 1; k < sides - 1; k++)
            {
                body.Tri(pts[rings, 0], pts[rings, k], pts[rings, k + 1]);
                body.Tri(pts[0, 0], pts[0, k + 1], pts[0, k]);
            }
        }

        /// <summary>
        /// A swept, tapered wing with thickness: seven spanwise sections, each a four-point
        /// aerofoil box, the chord shrinking and the leading edge sweeping back towards the tip.
        /// <paramref name="dir"/> is -1 for the left wing, +1 for the right.
        /// </summary>
        private static void PlaneWing(MeshBuild build, Vector3 root, float dir, float span,
                                      float rootChord, float tipChord, float sweep, float thick)
        {
            const int sections = 7;
            var ring = new Vector3[sections + 1][];

            for (int i = 0; i <= sections; i++)
            {
                float t = i / (float)sections;
                float chord = Mathf.Lerp(rootChord, tipChord, t);
                float th = Mathf.Lerp(thick, thick * 0.35f, t);
                float x = root.x + dir * span * t;
                float z0 = root.z - sweep * t;               // leading edge sweeps back
                float y = root.y + 0.25f * t;                // a little dihedral

                ring[i] = new[]
                {
                    new Vector3(x, y + th * 0.5f, z0 + chord * 0.5f),   // top, front
                    new Vector3(x, y + th * 0.25f, z0 - chord * 0.5f),  // top, back
                    new Vector3(x, y - th * 0.25f, z0 - chord * 0.5f),  // bottom, back
                    new Vector3(x, y - th * 0.5f, z0 + chord * 0.5f)    // bottom, front
                };
            }

            for (int i = 0; i < sections; i++)
                for (int j = 0; j < 4; j++)
                {
                    int k = (j + 1) % 4;
                    if (dir > 0f) build.Quad(ring[i][j], ring[i][k], ring[i + 1][k], ring[i + 1][j]);
                    else build.Quad(ring[i][k], ring[i][j], ring[i + 1][j], ring[i + 1][k]);
                }

            // Tip cap.
            var tip = ring[sections];
            if (dir > 0f) { build.Quad(tip[3], tip[2], tip[1], tip[0]); }
            else { build.Quad(tip[0], tip[1], tip[2], tip[3]); }
        }

        /// <summary>The row of cabin windows, dark, a few missing where the skin was torn.</summary>
        private static void PlaneWindows(MeshBuild glass, System.Random rng)
        {
            for (float z = -9f; z <= 6.2f; z += 1.25f)
                foreach (float side in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() < 0.12) continue;
                    glass.Box(new Vector3(side * 1.9f, 0.5f, z), new Vector3(0.1f, 0.38f, 0.52f), Quaternion.identity);
                }

            // The cockpit glass, across the nose.
            glass.Box(new Vector3(0f, 0.42f, 10.6f), new Vector3(1.5f, 0.45f, 0.4f), Quaternion.Euler(-24f, 0f, 0f));
        }

        // ==================================================================
        // The rooftop kit
        // ==================================================================
        /// <summary>
        /// A real dish: a shallow paraboloid of four rings and twenty sides, concave towards
        /// <paramref name="face"/>, double-sided, with a feed arm and a receiver at its focus.
        /// The roofs had a flat disc on a stick; from the street that reads as a plate.
        /// </summary>
        private static void ParabolicDish(MeshBuild dish, MeshBuild arm, Vector3 centre, Vector3 face, float radius)
        {
            face = face.normalized;
            var rot = Quaternion.LookRotation(face, Mathf.Abs(face.y) > 0.95f ? Vector3.forward : Vector3.up);
            const int rings = 4, sides = 20;
            float depth = radius * 0.28f;

            Vector3 P(int ring, int sd, float zOff)
            {
                float t = ring / (float)rings;
                float a = sd * Mathf.PI * 2f / sides;
                return centre + rot * new Vector3(Mathf.Cos(a) * radius * t, Mathf.Sin(a) * radius * t,
                                                  -depth * (1f - t * t) + zOff);
            }

            for (int r = 0; r < rings; r++)
                for (int sd = 0; sd < sides; sd++)
                {
                    int n = (sd + 1) % sides;
                    dish.Quad(P(r, sd, 0f), P(r + 1, sd, 0f), P(r + 1, n, 0f), P(r, n, 0f));
                    dish.Quad(P(r, n, -0.03f), P(r + 1, n, -0.03f), P(r + 1, sd, -0.03f), P(r, sd, -0.03f));
                }

            // The feed: an arm out to the focus, and the receiver on its end.
            var back = centre + rot * new Vector3(0f, 0f, -depth);
            var focus = centre + rot * new Vector3(0f, 0f, radius * 0.5f);
            arm.Tube(back, focus, 0.018f, 0.014f, 4);
            arm.Box(focus, new Vector3(0.07f, 0.07f, 0.1f), rot);
        }

        /// <summary>
        /// A rooftop radio mast: a triangular lattice on three legs that taper towards the top,
        /// rungs and diagonals every half metre, a Yagi antenna on the head and three guy wires
        /// to the roof. <paramref name="at"/> is its foot, <paramref name="rot"/> the house's frame.
        /// </summary>
        private static void RoofMast(MeshBuild m, Vector3 at, float height, Quaternion rot)
        {
            Vector3 Leg(int i, float y)
            {
                float t = y / height;
                float r = Mathf.Lerp(0.3f, 0.11f, t);
                float a = (90f + i * 120f) * Mathf.Deg2Rad;
                return at + rot * new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            }

            for (int i = 0; i < 3; i++)
                m.Tube(Leg(i, 0f), Leg(i, height), 0.026f, 0.018f, 4);

            const float step = 0.55f;
            for (float y = 0f; y + step <= height + 0.01f; y += step)
                for (int i = 0; i < 3; i++)
                {
                    int j = (i + 1) % 3;
                    m.Tube(Leg(i, y), Leg(j, y), 0.012f, 0.012f, 3);
                    m.Tube(Leg(i, y), Leg(j, y + step), 0.01f, 0.01f, 3);
                }

            // The head: a boom and five elements, longest at the back.
            var top = at + rot * new Vector3(0f, height, 0f);
            m.Tube(top - rot * new Vector3(0f, 0f, 0.1f), top + rot * new Vector3(0f, 0f, 0.95f), 0.014f, 0.012f, 4);
            for (int e = 0; e < 5; e++)
            {
                float z = 0.05f + e * 0.2f;
                float len = 0.78f - e * 0.1f;
                m.Box(top + rot * new Vector3(0f, 0f, z), new Vector3(len, 0.012f, 0.012f), rot);
            }

            // Guy wires from two-thirds up to the roof, three ways round.
            var tie = at + rot * new Vector3(0f, height * 0.66f, 0f);
            for (int i = 0; i < 3; i++)
            {
                float a = (30f + i * 120f) * Mathf.Deg2Rad;
                var anchor = at + rot * new Vector3(Mathf.Cos(a) * 2.1f, 0.02f, Mathf.Sin(a) * 2.1f);
                m.Tube(tie, anchor, 0.006f, 0.006f, 3);
                m.Tube(anchor, anchor + Vector3.up * 0.18f, 0.02f, 0.02f, 4);
            }
        }

        /// <summary>
        /// A water tank on a braced stand: ribbed drum, domed lid and hatch, a standpipe down to
        /// the roof and a short ladder. It was a plain cylinder on four sticks.
        /// </summary>
        private static void RoofTank(MeshBuild tank, MeshBuild metal, Vector3 at, Quaternion rot)
        {
            const float r = 0.62f, h = 1.15f, y0 = 0.72f;

            // Stand: four legs splayed out at the foot, a ring of ties and cross braces.
            var feet = new Vector3[4];
            var tops = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                feet[i] = at + rot * new Vector3(Mathf.Cos(a) * 0.62f, 0f, Mathf.Sin(a) * 0.62f);
                tops[i] = at + rot * new Vector3(Mathf.Cos(a) * 0.46f, y0, Mathf.Sin(a) * 0.46f);
                metal.Tube(feet[i], tops[i], 0.032f, 0.03f, 5);
            }
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                var a = Vector3.Lerp(feet[i], tops[i], 0.45f);
                var b = Vector3.Lerp(feet[j], tops[j], 0.45f);
                metal.Tube(a, b, 0.014f, 0.014f, 4);
                metal.Tube(Vector3.Lerp(feet[i], tops[i], 0.15f), Vector3.Lerp(feet[j], tops[j], 0.75f), 0.01f, 0.01f, 3);
                metal.Tube(Vector3.Lerp(feet[j], tops[j], 0.15f), Vector3.Lerp(feet[i], tops[i], 0.75f), 0.01f, 0.01f, 3);
            }

            // Drum, three hoops round it, a domed lid and a hatch.
            var bottom = at + Vector3.up * y0;
            var topC = at + Vector3.up * (y0 + h);
            tank.Tube(bottom, topC, r, r, 40);
            foreach (float t in new[] { 0.2f, 0.5f, 0.8f })
                tank.Tube(bottom + Vector3.up * (h * t - 0.025f), bottom + Vector3.up * (h * t + 0.025f), r + 0.014f, r + 0.014f, 40);
            // A rounded lid: four stacked rings following a circular arc, not a two-step cone.
            for (int k = 0; k < 4; k++)
            {
                float a0 = k / 4f * 0.5f * Mathf.PI, a1 = (k + 1) / 4f * 0.5f * Mathf.PI;
                tank.Tube(topC + Vector3.up * (0.2f * Mathf.Sin(a0)), topC + Vector3.up * (0.2f * Mathf.Sin(a1)),
                          r * Mathf.Cos(a0), r * Mathf.Cos(a1) + 0.01f, 40);
            }
            tank.Tube(topC + rot * new Vector3(0.25f, 0.08f, 0.1f), topC + rot * new Vector3(0.25f, 0.2f, 0.1f), 0.12f, 0.12f, 20);

            // Standpipe down the side, with an elbow into the drum.
            var pipeTop = bottom + rot * new Vector3(r + 0.05f, 0.1f, 0f);
            metal.Tube(at + rot * new Vector3(r + 0.05f, 0f, 0f), pipeTop, 0.022f, 0.022f, 6);
            metal.Tube(pipeTop, bottom + rot * new Vector3(r - 0.02f, 0.1f, 0f), 0.022f, 0.022f, 6);

            // A little ladder on the far side.
            var l0 = rot * new Vector3(-r - 0.04f, 0f, -0.13f);
            var l1 = rot * new Vector3(-r - 0.04f, 0f, 0.13f);
            metal.Tube(at + l0, at + l0 + Vector3.up * (y0 + h * 0.9f), 0.014f, 0.014f, 4);
            metal.Tube(at + l1, at + l1 + Vector3.up * (y0 + h * 0.9f), 0.014f, 0.014f, 4);
            for (float y = 0.2f; y < y0 + h * 0.85f; y += 0.28f)
                metal.Tube(at + l0 + Vector3.up * y, at + l1 + Vector3.up * y, 0.01f, 0.01f, 3);
        }

        /// <summary>
        /// The things a flat roof collects: air-conditioners hung off the parapet, a pair of
        /// solar panels on a tilted frame, vent stacks, a television antenna and the odd mast.
        /// All drawn only -- no collider, off the minimap and off the navigation bake.
        /// </summary>
        private static void RoofExtras(Transform parent, int layer, System.Random rng, Frame f,
                                       float cu, float cv, float hw, float hd, float roofY)
        {
            var units = new MeshBuild { UVScale = 0.5f };
            var dark = new MeshBuild { UVScale = 0.5f };
            var metal = new MeshBuild { UVScale = 0.5f };

            Vector3 Spot() => f.P(cu + Rand(rng, -hw, hw), roofY, cv + Rand(rng, -hd, hd));

            if (rng.NextDouble() < 0.28)
                RoofMast(metal, Spot(), Rand(rng, 3.4f, 5.6f), f.Rot * Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f));

            int acs = rng.NextDouble() < 0.4 ? 1 + rng.Next(2) : 0;
            for (int i = 0; i < acs; i++)
            {
                var at = Spot() + Vector3.up * 0.3f;
                var turn = f.Rot * Quaternion.Euler(0f, rng.Next(4) * 90f, 0f);
                AirConditioner(units, dark, metal, at, turn, rng.Next(1, 999));
            }

            if (rng.NextDouble() < 0.25)
            {
                var at = Spot();
                var turn = f.Rot * Quaternion.Euler(0f, Rand(rng, -25f, 25f) + (rng.Next(2) * 180f), 0f);
                for (int k = 0; k < 2; k++)
                {
                    var c = at + turn * new Vector3((k - 0.5f) * 1.7f, 0f, 0f);
                    var tilt = turn * Quaternion.Euler(-28f, 0f, 0f);
                    metal.Box(c + Vector3.up * 0.55f, new Vector3(1.64f, 0.05f, 1.04f), tilt);
                    dark.Box(c + Vector3.up * 0.58f + tilt * new Vector3(0f, 0.03f, 0f), new Vector3(1.5f, 0.02f, 0.92f), tilt);
                    for (int g = -1; g <= 1; g += 2)
                        metal.Box(c + turn * new Vector3(g * 0.62f, 0.2f, 0.35f), new Vector3(0.04f, 0.4f, 0.04f), turn);
                }
            }

            int vents = rng.NextDouble() < 0.5 ? 1 + rng.Next(2) : 0;
            for (int i = 0; i < vents; i++)
            {
                var at = Spot();
                metal.Tube(at, at + Vector3.up * 0.7f, 0.05f, 0.05f, 8);
                metal.Tube(at + Vector3.up * 0.7f, at + Vector3.up * 0.9f + f.Rot * new Vector3(0.16f, 0f, 0f), 0.05f, 0.05f, 8);
                metal.Tube(at + Vector3.up * 0.9f + f.Rot * new Vector3(0.16f, 0f, 0f), at + Vector3.up * 0.84f + f.Rot * new Vector3(0.28f, 0f, 0f), 0.075f, 0.075f, 8);
            }

            if (rng.NextDouble() < 0.3)
            {
                var at = Spot();
                var turn = f.Rot * Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);
                metal.Tube(at, at + Vector3.up * 2.2f, 0.022f, 0.016f, 5);
                for (int i = 0; i < 4; i++)
                {
                    float y = 1.3f + i * 0.22f, len = 0.9f - i * 0.15f;
                    metal.Box(at + Vector3.up * y, new Vector3(len, 0.012f, 0.012f), turn);
                }
                metal.Box(at + Vector3.up * 1.8f, new Vector3(0.012f, 0.012f, 0.5f), turn);
            }

            Visual(parent, "RoofKit", metal, _steelMat, layer);
            Visual(parent, "RoofUnits", units, _acMat, layer);
            Visual(parent, "RoofDark", dark, _tankBlackMat, layer);
        }

        // ==================================================================
        // Watchtowers
        // ==================================================================
        private static readonly System.Collections.Generic.List<Vector2> _watchPlans = new System.Collections.Generic.List<Vector2>();

        /// <summary>
        /// A mud-brick watchtower: a battered three-stage shaft of rounded blocks on a plinth, a
        /// cornice, a parapet and a lookout hut under a corrugated roof, with a radio mast, a
        /// searchlight and a dish up top. Solid and not climbable (it is a landmark and a
        /// sightline), so the whole thing is kept off the navigation bake.
        /// </summary>
        private static void BuildWatchtower(Transform parent, int layer, System.Random rng, Vector2 at, float yaw)
        {
            var root = new GameObject("Watchtower").transform;
            root.SetParent(parent, false);

            float ground = GroundHeightAt(at.x, at.y);
            var f = new Frame(new Vector3(at.x, ground, at.y), yaw);
            var mat = _adobeTints[rng.Next(_adobeTints.Length)];
            int seed = rng.Next(1, 9999);

            var shaft = new MeshBuild { UVScale = 0.28f };
            // Plinth, then three stages, each narrower: 4.6 -> 4.0 -> 3.5 m across.
            shaft.SoftBox(f.P(0f, -0.8f, 0f), new Vector3(5.0f, 2.2f, 5.0f), f.Rot, seed, 0.12f, 4, 0.01f);
            shaft.SoftBox(f.P(0f, 2.2f, 0f), new Vector3(4.6f, 4.8f, 4.6f), f.Rot, seed + 1, 0.22f, 6, 0.025f);
            shaft.SoftBox(f.P(0f, 6.4f, 0f), new Vector3(4.0f, 3.8f, 4.0f), f.Rot, seed + 2, 0.2f, 6, 0.025f);
            shaft.SoftBox(f.P(0f, 9.6f, 0f), new Vector3(3.5f, 3.0f, 3.5f), f.Rot, seed + 3, 0.18f, 6, 0.02f);
            // Cornice and a parapet with a crenel on each face.
            shaft.SoftBox(f.P(0f, 11.25f, 0f), new Vector3(4.3f, 0.4f, 4.3f), f.Rot, seed + 4, 0.1f, 4, 0.005f);
            for (int i = 0; i < 4; i++)
            {
                var dir = Quaternion.Euler(0f, i * 90f, 0f);
                for (int k = -1; k <= 1; k += 2)
                    shaft.SoftBox(f.P(0f, 11.95f, 0f) + f.Rot * dir * new Vector3(k * 1.3f, 0f, 2.05f),
                                  new Vector3(1.1f, 1.0f, 0.4f), f.Rot * dir, seed + 5 + i, 0.06f, 3, 0.005f);
            }

            var shaftGo = MeshObject(root, "TowerShaft", ToMesh(shaft, DenseKey("watchshaft")), mat, Vector3.zero,
                                     Quaternion.identity, Vector3.one, layer, "Concrete");
            NoStanding(shaftGo);
            SealLocal(root, f, 0f, 6f, 0f, new Vector3(5.2f, 13f, 5.2f));

            // Window slits and a door-shaped shadow at the foot.
            var dark = new MeshBuild { UVScale = 0.5f };
            for (int i = 0; i < 4; i++)
            {
                var dir = Quaternion.Euler(0f, i * 90f, 0f);
                foreach (float y in new[] { 4.0f, 7.4f })
                    dark.Box(f.P(0f, y, 0f) + f.Rot * dir * new Vector3(0f, 0f, (y < 5f ? 2.31f : 2.01f)),
                             new Vector3(0.35f, 0.9f, 0.16f), f.Rot * dir);
            }
            dark.Box(f.P(0f, 1.0f, 0f) + f.Rot * new Vector3(0f, 0f, -2.33f), new Vector3(1.1f, 2.0f, 0.14f), f.Rot);
            Visual(root, "TowerOpenings", dark, _shadowMat, layer);

            // The lookout: four posts, a bench rail and a corrugated roof, canted a little.
            var timber = new MeshBuild { UVScale = 0.5f };
            var roofM = new MeshBuild { UVScale = 0.5f };
            foreach (float a in new[] { -1f, 1f })
                foreach (float b in new[] { -1f, 1f })
                    timber.Tube(f.P(a * 1.45f, 12.4f, b * 1.45f), f.P(a * 1.45f, 14.6f, b * 1.45f), 0.09f, 0.07f, 6);
            roofM.Box(f.P(0f, 14.75f, 0f), new Vector3(4.2f, 0.1f, 4.2f), f.Rot * Quaternion.Euler(5f, 0f, 0f));
            for (float x = -2f; x <= 2f; x += 0.35f)
                roofM.Box(f.P(x, 14.82f, 0f), new Vector3(0.09f, 0.06f, 4.2f), f.Rot * Quaternion.Euler(5f, 0f, 0f));
            timber.Tube(f.P(-1.45f, 13.2f, -1.45f), f.P(1.45f, 13.2f, -1.45f), 0.05f, 0.05f, 5);
            timber.Tube(f.P(-1.45f, 13.2f, 1.45f), f.P(1.45f, 13.2f, 1.45f), 0.05f, 0.05f, 5);
            Visual(root, "TowerLookout", timber, _timberMat, layer);
            Visual(root, "TowerRoof", roofM, _steelMat, layer);

            // Radio mast, a searchlight on a bracket and a dish on the parapet.
            var kit = new MeshBuild { UVScale = 0.5f };
            RoofMast(kit, f.P(1.1f, 11.4f, 1.1f), 3.4f, f.Rot);
            var lamp = f.P(-1.9f, 13.4f, 0f);
            kit.Tube(lamp, lamp + f.Rot * new Vector3(0f, 0.25f, 0f), 0.03f, 0.03f, 5);
            kit.Tube(lamp + f.Rot * new Vector3(0f, 0.25f, 0f), lamp + f.Rot * new Vector3(-0.4f, 0.3f, 0f), 0.16f, 0.2f, 10);
            var dishMesh = new MeshBuild { UVScale = 0.5f };
            ParabolicDish(dishMesh, kit, f.P(0f, 12.9f, 2.3f), f.Axis(new Vector3(0f, 0.3f, 1f)), 0.55f);
            Visual(root, "TowerKit", kit, _steelMat, layer);
            Visual(root, "TowerDish", dishMesh, _dishMat, layer);
        }

        // ==================================================================
        // Trees
        // ==================================================================
        /// <summary>
        /// Real branching: a trunk that forks, each fork forking again, each limb thinner and
        /// shorter than the one it came from, with foliage as a mass of small leaf triangles
        /// on the tips. Three kinds -- <c>0</c> an acacia (low fork, flat umbrella crown),
        /// <c>1</c> a ghaf / tamarisk (taller, rounder, denser) and <c>2</c> a dead tree (gnarled,
        /// bare). The old acacia was a squashed boulder on three sticks.
        /// </summary>
        private static void GrowTree(MeshBuild trunk, MeshBuild leaves, System.Random rng, Vector3 p, Vector3 dir,
                                     float length, float radius, int depth, int kind)
        {
            var end = p + dir * length;
            trunk.Tube(p, end, radius, Mathf.Max(0.02f, radius * 0.7f), depth >= 2 ? 8 : 5);

            if (depth == 0 || radius < 0.035f)
            {
                if (kind != 2) LeafMass(leaves, rng, end, kind);
                return;
            }

            int kids = kind == 2 ? 2 : 2 + rng.Next(2);
            for (int k = 0; k < kids; k++)
            {
                float spread = kind == 0 ? 0.75f : kind == 1 ? 0.6f : 0.55f;
                var d = (dir + new Vector3(Rand(rng, -spread, spread), Rand(rng, -0.1f, kind == 0 ? 0.25f : 0.5f), Rand(rng, -spread, spread))).normalized;
                // An acacia's limbs run out sideways, so lean the dir away from straight up.
                if (kind == 0 && d.y > 0.7f) d = new Vector3(d.x, 0.7f, d.z).normalized;
                if (kind == 2) d = (d + new Vector3(Rand(rng, -0.4f, 0.4f), 0f, Rand(rng, -0.4f, 0.4f))).normalized;
                GrowTree(trunk, leaves, rng, end, d, length * Rand(rng, 0.68f, 0.9f), radius * Rand(rng, 0.55f, 0.68f), depth - 1, kind);
            }
        }

        /// <summary>A puff of foliage: thirty-odd small double-sided leaf triangles in an ellipsoid.</summary>
        private static void LeafMass(MeshBuild leaves, System.Random rng, Vector3 c, int kind)
        {
            float rx = kind == 0 ? 1.7f : 1.15f;
            float ry = kind == 0 ? 0.42f : 0.95f;

            for (int i = 0; i < 34; i++)
            {
                var o = new Vector3(Rand(rng, -1f, 1f) * rx, Rand(rng, -0.6f, 1f) * ry, Rand(rng, -1f, 1f) * rx);
                if (o.x * o.x / (rx * rx) + o.z * o.z / (rx * rx) > 1f) o *= 0.7f;
                var q = c + o;

                var a = new Vector3(Rand(rng, -1f, 1f), Rand(rng, -0.4f, 0.4f), Rand(rng, -1f, 1f)).normalized;
                var b = Vector3.Cross(a, new Vector3(Rand(rng, -1f, 1f), Rand(rng, -1f, 1f), Rand(rng, -1f, 1f))).normalized;
                float sz = Rand(rng, 0.28f, 0.52f);

                var v0 = q + a * sz;
                var v1 = q + b * sz * 0.8f;
                var v2 = q - (a + b) * 0.5f * sz;
                leaves.Tri(v0, v1, v2);
                leaves.Tri(v0, v2, v1);
            }
        }

        private static void BuildTreeGeometry(int seed, int kind, MeshBuild trunk, MeshBuild leaves)
        {
            var rng = new System.Random(seed * 31 + kind * 977 + 5);
            float height = kind == 0 ? Rand(rng, 2.4f, 3.4f) : kind == 1 ? Rand(rng, 3.6f, 5f) : Rand(rng, 2.6f, 4f);
            float radius = kind == 1 ? 0.3f : 0.26f;

            var lean = new Vector3(Rand(rng, -0.22f, 0.22f), 1f, Rand(rng, -0.22f, 0.22f)).normalized;
            GrowTree(trunk, leaves, rng, Vector3.down * 0.25f, lean, height, radius, kind == 2 ? 4 : 3, kind);

            // A flare of roots at the foot.
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + Rand(rng, -0.3f, 0.3f);
                trunk.Tube(new Vector3(Mathf.Cos(a) * 0.5f, -0.1f, Mathf.Sin(a) * 0.5f), new Vector3(0f, 0.5f, 0f), 0.05f, radius * 0.8f, 5);
            }
        }

        private static Mesh TreeTrunkMesh(int seed, int kind)
            => Pooled($"tree_trunk_{kind}_{seed}", () =>
            {
                var t = new MeshBuild { UVScale = 0.6f };
                BuildTreeGeometry(seed, kind, t, new MeshBuild());
                return t.ToMesh($"TreeTrunk_{kind}_{seed}");
            });

        private static Mesh TreeLeafMesh(int seed, int kind)
            => Pooled($"tree_leaf_{kind}_{seed}", () =>
            {
                var l = new MeshBuild { UVScale = 0.6f };
                BuildTreeGeometry(seed, kind, new MeshBuild(), l);
                return l.ToMesh($"TreeLeaves_{kind}_{seed}");
            });

        /// <summary>Plants one of the new trees at a ground point: trunk with a collider, leaves without.</summary>
        private static void PlantTree(Transform parent, int layer, System.Random rng, Vector2 p, int kind)
        {
            var leafMat = kind == 1
                ? MakeMaterial("GhafLeaf", new Color(0.27f, 0.37f, 0.18f), 0.08f, 0f)
                : MakeMaterial("Acacia", new Color(0.30f, 0.34f, 0.15f), 0.08f, 0f);

            int seed = 1 + rng.Next(28);
            var foot = new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y);
            var turn = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);
            var scale = Vector3.one * Rand(rng, 0.85f, 1.25f);

            MeshObject(parent, kind == 2 ? "DeadTree" : "Tree", TreeTrunkMesh(seed, kind), _timberMat, foot, turn, scale, layer, "Wood");

            if (kind == 2) return;

            // The crown is off the bake -- a flat umbrella of leaves bakes as an island in the sky.
            var crown = MeshObject(parent, "TreeLeaves", TreeLeafMesh(seed, kind), leafMat, foot, turn, scale, layer, null, collider: false);
            NoStanding(crown);
            Hide(crown);
        }

        // ==================================================================
        // The ground
        // ==================================================================
        /// <summary>
        /// A colour map for the whole dune field, laid under the sand's fine detail.
        ///
        /// Ishaan, 2026-10-03: "sand, ground texture, also the ground texture shifting from one
        /// theme to another". The ground was one grey map tinted one colour, so a kilometre of
        /// dune was one flat tan. The land is not like that: crests are paler (wind-blown fine
        /// sand), basins hold pale cracked clay, steep faces are scree, the ground round a
        /// village and along the track is packed dark earth, and the banks of the river are
        /// damp silt that greens towards the water. Each of those fades into the next across
        /// a noise-warped edge, so there is no line where one "texture" stops.
        ///
        /// A colour map is what the Lit shader's base map is for; the sand's own grain and
        /// ripples ride on top of it as the detail map (see <see cref="MakeMacroGroundMaterial"/>).
        /// Computed from the same heights the mesh is built from, so it lines up with it.
        /// </summary>
        private static Color MacroColour(float wx, float wz)
        {
            int seed = _theme.randomSeed;
            Color sand = _theme.floorColor;

            float big = Fbm2(wx * 0.0035f, wz * 0.0035f, seed + 1, 3);
            float mid = Fbm2(wx * 0.018f, wz * 0.018f, seed + 2, 3);
            float fine = Fbm2(wx * 0.11f, wz * 0.11f, seed + 3, 2);

            float h = GroundHeightAt(wx, wz);
            float sx = GroundHeightAt(wx + 1.6f, wz) - GroundHeightAt(wx - 1.6f, wz);
            float sz = GroundHeightAt(wx, wz + 1.6f) - GroundHeightAt(wx, wz - 1.6f);
            float slope = Mathf.Sqrt(sx * sx + sz * sz) / 3.2f;     // rise over run

            var c = sand;
            float detail = 1f;      // how much of the sand's ripple and grain shows; packed ground is smoother

            // Regional drift: ochre-red one way, bleached the other.
            c = Color.Lerp(c, new Color(0.69f, 0.49f, 0.32f), Mathf.Clamp01(big * 1.6f) * 0.55f);
            c = Color.Lerp(c, new Color(0.77f, 0.69f, 0.51f), Mathf.Clamp01(-big * 1.6f) * 0.5f);

            // Crests, windblown and pale.
            float crest = Mathf.SmoothStep(0f, 1f, (h - 3f + mid * 3f) / 9f);
            c = Color.Lerp(c, new Color(0.80f, 0.72f, 0.54f), crest * 0.38f * (1f - Mathf.Clamp01(slope * 2f)));

            // Basins: pale clay, cracked.
            float basin = Mathf.SmoothStep(0f, 1f, (-4f - h + mid * 3f) / 7f);
            float cracks = Mathf.Pow(1f - Mathf.Abs(Fbm2(wx * 0.16f, wz * 0.16f, seed + 9, 3)) * 1.9f, 5f);
            var clay = new Color(0.75f, 0.70f, 0.59f);
            clay = Color.Lerp(clay, clay * 0.7f, Mathf.Clamp01(cracks) * 0.6f);
            c = Color.Lerp(c, clay, basin * 0.85f);
            detail = Mathf.Lerp(detail, 0.55f, basin);

            // Scree on the steep faces.
            float steep = Mathf.SmoothStep(0.34f, 0.62f, slope + mid * 0.06f);
            var scree = new Color(0.55f, 0.47f, 0.38f) * (0.92f + fine * 0.2f);
            c = Color.Lerp(c, scree, steep * 0.75f);
            detail = Mathf.Lerp(detail, 0.8f, steep * 0.5f);

            // Packed earth round a place and along the track.
            // Soft, not a yes/no: "inside a site" is a rectangle, and a blend that switches on at its edge
            // is a visible rectangle (Ishaan, 2026-10-03: "the transition ... is not done properly"). So the
            // question is asked at a run of widening margins, each with a smaller weight, and the point
            // asked about is pushed around by a noise so the edge wanders instead of running straight.
            var p2 = new Vector2(wx, wz);
            var pw = p2 + new Vector2(Fbm2(wx * 0.045f, wz * 0.045f, seed + 11, 2), Fbm2(wx * 0.045f + 40f, wz * 0.045f, seed + 12, 2)) * 8f;

            float earth = 0f;
            float[] margins = { 0f, 3f, 6f, 10f, 15f, 21f, 28f, 36f };
            float[] weights = { 0.85f, 0.72f, 0.58f, 0.44f, 0.32f, 0.22f, 0.13f, 0.06f };
            for (int i = 0; i < margins.Length; i++)
                if (InSite(pw, margins[i])) { earth = weights[i]; break; }

            float[] trackReach = { 1.5f, 3.2f, 5.5f, 8f, 11f };
            float[] trackWeight = { 0.9f, 0.7f, 0.45f, 0.22f, 0.08f };
            for (int i = 0; i < trackReach.Length; i++)
                if (NearTrack(pw, trackReach[i])) { earth = Mathf.Max(earth, trackWeight[i]); break; }

            earth *= 0.85f + mid * 0.3f;
            c = Color.Lerp(c, new Color(0.56f, 0.45f, 0.31f), Mathf.Clamp01(earth));
            detail = Mathf.Lerp(detail, 0.22f, Mathf.Clamp01(earth));

            // The river's banks: damp silt, greener towards the water.
            float bank = Mathf.Abs(wx - GorgeCentreAt(wz)) - _theme.hazardWidth * 0.5f;
            float silt = 1f - Mathf.SmoothStep(0f, 34f + mid * 10f, bank);
            c = Color.Lerp(c, new Color(0.44f, 0.37f, 0.26f), Mathf.Clamp01(silt) * 0.7f);
            detail = Mathf.Lerp(detail, 0.45f, Mathf.Clamp01(silt) * 0.8f);
            float green = 1f - Mathf.SmoothStep(14f, 34f, bank + mid * 6f);
            c = Color.Lerp(c, new Color(0.37f, 0.40f, 0.23f), Mathf.Clamp01(green * (0.4f + fine * 0.9f)) * 0.45f);

            // Stains and mottling so no stretch is clean.
            float shade = 1f + mid * 0.1f + fine * 0.05f;
            return new Color(Mathf.Clamp01(c.r * shade), Mathf.Clamp01(c.g * shade), Mathf.Clamp01(c.b * shade), Mathf.Clamp01(detail));
        }

        /// <summary>
        /// The desert's ground material: the colour map as base, the sand's grain and ripples as
        /// the Lit shader's detail albedo and detail normal (multiplied x2 around mid-grey, so
        /// they add contrast and leave the colour alone).
        /// </summary>
        private static Material MakeMacroGroundMaterial()
        {
            const int n = 768;
            float span = (_groundN - 1) * _groundStep;
            float min = _groundMin;

            // <b>Brightness bookkeeping.</b> The sand's grain map sits at mid-grey, so the old ground
            // rendered at about half its material colour. URP's detail multiply is x2 around
            // mid-grey in gamma and about x2.3 in linear, which on a full-brightness colour map would
            // come out four times too bright. So the map is stored pre-darkened by exactly that
            // ratio (in linear), and the finished ground matches what it replaced.
            const float compensate = 0.5f / 2.2974f;

            var plain = _theme.floorColor;
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float wx = min + (x + 0.5f) / n * span;
                    float wz = min + (y + 0.5f) / n * span;
                    var c = MacroColour(wx, wz);

                    // Fade to the plain sand colour over the last sixty metres to the map's edge,
                    // where the apron (still the plain material) takes over without a seam.
                    float fromEdge = Mathf.Min(Mathf.Min(x, n - 1 - x), Mathf.Min(y, n - 1 - y)) / (float)n * span;
                    c = Color.Lerp(plain, c, Mathf.SmoothStep(0f, 1f, fromEdge / 60f));

                    var lin = c.linear;
                    var px = new Color(lin.r * compensate, lin.g * compensate, lin.b * compensate, 1f).gamma;
                    px.a = c.a;     // the detail mask rides in alpha, so ripples fade out over packed earth
                    pixels[y * n + x] = px;
                }

            string texPath = $"{TextureFolder}/DesertMacro.png";
            WritePng(texPath, pixels, n, importer =>
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
            });
            var macro = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            string path = $"{MaterialFolder}/{SafeName(_theme.themeName)}_GroundMacro.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            else if (mat.shader != shader) mat.shader = shader;

            // A 1.5x gain, found by comparing renders against the ground this replaced: the colour
            // map comes out of the detail multiply about a third darker than the arithmetic says.
            var gain = new Color(1.5f, 1.5f, 1.5f, 1f);
            mat.color = gain;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", gain);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", _theme.floorSmoothness * 0.4f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);

            mat.SetTexture("_BaseMap", macro);
            mat.SetTextureScale("_BaseMap", new Vector2(1f / span, 1f / span));
            mat.SetTextureOffset("_BaseMap", new Vector2(-min / span, -min / span));
            mat.mainTexture = macro;
            mat.mainTextureScale = new Vector2(1f / span, 1f / span);
            mat.mainTextureOffset = new Vector2(-min / span, -min / span);

            var da = LoadDetail("Sand_Albedo");
            var dn = LoadDetail("Sand_Normal");
            // URP multiplies the detail tiling by the base map's, and this base map covers the whole
            // field, so the sand's 0.09 repeats per metre has to be scaled up by the span or the grain
            // is sampled at one point and the ground goes flat.
            var tile = new Vector2(0.09f * span, 0.09f * span);
            if (da != null && mat.HasProperty("_DetailAlbedoMap"))
            {
                mat.SetTexture("_DetailAlbedoMap", da);
                mat.SetTextureScale("_DetailAlbedoMap", tile);
                if (mat.HasProperty("_DetailAlbedoMapScale")) mat.SetFloat("_DetailAlbedoMapScale", 1f);
            }
            if (dn != null && mat.HasProperty("_DetailNormalMap"))
            {
                mat.SetTexture("_DetailNormalMap", dn);
                mat.SetTextureScale("_DetailNormalMap", tile);
                if (mat.HasProperty("_DetailNormalMapScale")) mat.SetFloat("_DetailNormalMapScale", 1.15f);
            }
            // The detail mask is sampled at the base tiling, which is exactly the colour map, so its
            // alpha (smoother where the ground is packed, silted or clay) fades the ripples and the
            // grain out smoothly instead of the ground changing texture at a hard edge.
            if (mat.HasProperty("_DetailMask")) mat.SetTexture("_DetailMask", macro);
            mat.EnableKeyword("_DETAIL_MULX2");

            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// A post on the east bank: a watchtower, two houses with plinths and rooftop kit, a
        /// sandbag position and a parked pickup, turned to face the river. Built on the pad
        /// <see cref="PlanDesertWild"/> flattened for it.
        /// </summary>
        private static Vector3 OnGround(Vector3 v) => new Vector3(v.x, GroundHeightAt(v.x, v.z), v.z);

        private static void BuildEastPost(Transform parent, int layer, int backdrop, System.Random rng, Vector2 at)
        {
            var post = new GameObject("EastPost").transform;
            post.SetParent(parent, false);

            float floor = GroundHeightAt(at.x, at.y);
            var f = new Frame(new Vector3(at.x, floor, at.y), 0f);

            // The tower stands nearest the river (the -x side), looking over it.
            var towerAt = f.P2(-3f, 0f);
            BuildWatchtower(post, layer, rng, towerAt, 90f + rng.Next(2) * 180f);

            const float lot = 9.6f;
            foreach (var (u, v) in new[] { (7.8f, 6.2f), (7.4f, -6.4f) })
            {
                var lf = new Frame(f.P(u, 0f, v), rng.Next(4) * 90f);
                BuildSolidHouse(post, layer, rng, lf, lot, false);
            }

            BuildSandbagWall(post, layer, rng, OnGround(f.P(-8.5f, 0f, -6f)), 90f + Rand(rng, -10f, 10f));
            BuildSandbagWall(post, layer, rng, OnGround(f.P(-8.5f, 0f, 6.5f)), 90f + Rand(rng, -10f, 10f));
            var car = f.P2(2f, -11.5f);
            BuildVehicle(post, layer, rng, car, 90f + Rand(rng, -8f, 8f), VehicleKind.Pickup, _carPaints[rng.Next(_carPaints.Length)]);

            var palm = f.P(-1f, 0f, 11f);
            BuildPalm(post, layer, rng, new Vector3(palm.x, GroundHeightAt(palm.x, palm.z) - 0.2f, palm.z));
        }

        // ==================================================================
        // The canyon walls
        // ==================================================================
        /// <summary>
        /// One band of one canyon wall as a fine, smooth-shaded grid. The builder's grid (4.5 m
        /// along the river, a handful of rows down) is interpolated three ways each direction and
        /// roughened with a fine noise below the lip, on shared vertices so the normals blend across
        /// faces. Rows on a band's border sit exactly where the neighbouring band's do (the noise is
        /// a function of position alone), so the bands still meet without a seam, and the lip rows
        /// keep their built shape because the terrain's edge lies over them.
        ///
        /// Wound per side, as the old quads were: row k steps towards the river, which is +x on the
        /// west wall and -x on the east.
        /// </summary>
        private static Mesh CliffMesh(Vector3[][] grid, int k0, int k1, int side, int seed, string name, bool worldUv = false)
        {
            const int sz = 3, sk = 3;
            int slices = grid.Length - 1;
            int rows = k1 - k0;
            int nx = slices * sz + 1, nk = rows * sk + 1;

            var verts = new Vector3[nx * nk];
            var uvs = new Vector2[verts.Length];

            for (int a = 0; a < nx; a++)
            {
                float fi = a / (float)sz;
                int i0 = Mathf.Min(Mathf.FloorToInt(fi), slices - 1);
                float ti = fi - i0;

                for (int b = 0; b < nk; b++)
                {
                    float fk = k0 + b / (float)sk;
                    int j0 = Mathf.Min(Mathf.FloorToInt(fk + 1e-4f), k1 - 1);
                    float tk = fk - j0;

                    var p = Vector3.Lerp(Vector3.Lerp(grid[i0][j0], grid[i0 + 1][j0], ti),
                                         Vector3.Lerp(grid[i0][j0 + 1], grid[i0 + 1][j0 + 1], ti), tk);

                    // Nothing on the lip rows (0 and 1); full from row two down.
                    float w = Mathf.Clamp01(fk - 1f);
                    p.x += Fbm2(p.z * 0.2f, p.y * 0.4f, seed + 501, 3) * 0.55f * w;
                    p.z += Fbm2(p.z * 0.25f + 50f, p.y * 0.45f, seed + 502, 2) * 0.3f * w;

                    verts[a * nk + b] = p;
                    // The shelf is horizontal ground and takes world x/z so it lines up with the dune map;
                    // the walls below it are vertical and take z/y.
                    uvs[a * nk + b] = worldUv ? new Vector2(p.x, p.z) : new Vector2(p.z * 0.16f, p.y * 0.16f);
                }
            }

            var tris = new System.Collections.Generic.List<int>(slices * sz * rows * sk * 6);
            for (int a = 0; a < nx - 1; a++)
                for (int b = 0; b < nk - 1; b++)
                {
                    int i00 = a * nk + b, i01 = i00 + 1, i10 = i00 + nk, i11 = i10 + 1;
                    if (side < 0)
                    {
                        tris.Add(i00); tris.Add(i10); tris.Add(i11);
                        tris.Add(i00); tris.Add(i11); tris.Add(i01);
                    }
                    else
                    {
                        tris.Add(i00); tris.Add(i01); tris.Add(i11);
                        tris.Add(i00); tris.Add(i11); tris.Add(i10);
                    }
                }

            var mesh = new Mesh { name = name };
            if (verts.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ==================================================================
        // Air-conditioners and washing
        // ==================================================================
        /// <summary>
        /// A split-unit condenser: rounded casing, a louvred front, a fan behind a ring-and-spoke
        /// guard, a control panel with its lamps, a drain pipe and two feet. Local +z is the front.
        /// </summary>
        private static void AirConditioner(MeshBuild casing, MeshBuild dark, MeshBuild metal, Vector3 at, Quaternion turn, int seed)
        {
            casing.SoftBox(at, new Vector3(0.86f, 0.56f, 0.34f), turn, seed, 0.05f, 3, 0f);

            // Louvres across the right half of the front, each one tilted.
            for (int i = 0; i < 7; i++)
            {
                float y = -0.2f + i * 0.066f;
                dark.Box(at + turn * new Vector3(0.2f, y, 0.176f), new Vector3(0.36f, 0.022f, 0.02f), turn * Quaternion.Euler(-18f, 0f, 0f));
            }

            // The fan and its guard on the left half.
            var fan = at + turn * new Vector3(-0.2f, 0f, 0.175f);
            dark.Tube(fan, fan + turn * new Vector3(0f, 0f, 0.012f), 0.205f, 0.205f, 16);
            for (int blade = 0; blade < 5; blade++)
            {
                var d = turn * Quaternion.Euler(0f, 0f, blade * 72f) * new Vector3(0.11f, 0f, 0f);
                metal.Box(fan + d * 0.5f + turn * new Vector3(0f, 0f, 0.02f), new Vector3(0.11f, 0.05f, 0.008f),
                          turn * Quaternion.Euler(0f, 0f, blade * 72f + 12f));
            }
            foreach (float r in new[] { 0.07f, 0.13f, 0.19f })
                for (int k = 0; k < 14; k++)
                {
                    float a = k * Mathf.PI * 2f / 14f;
                    var q = fan + turn * new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0.032f);
                    metal.Box(q, new Vector3(r * 0.46f, 0.011f, 0.011f), turn * Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f));
                }
            for (int k = 0; k < 4; k++)
                metal.Box(fan + turn * Quaternion.Euler(0f, 0f, k * 45f) * Vector3.zero + turn * new Vector3(0f, 0f, 0.032f),
                          new Vector3(0.4f, 0.011f, 0.011f), turn * Quaternion.Euler(0f, 0f, k * 45f));

            // Control panel on the end, with three lamps.
            dark.Box(at + turn * new Vector3(0.405f, 0.1f, 0.03f), new Vector3(0.03f, 0.2f, 0.18f), turn);
            for (int k = 0; k < 3; k++)
                metal.Box(at + turn * new Vector3(0.422f, 0.17f - k * 0.05f, 0.03f), new Vector3(0.01f, 0.02f, 0.02f), turn);

            // A drain pipe out of the bottom, and two feet.
            metal.Tube(at + turn * new Vector3(0.3f, -0.28f, -0.05f), at + turn * new Vector3(0.3f, -0.48f, -0.05f), 0.016f, 0.016f, 6);
            metal.Tube(at + turn * new Vector3(0.3f, -0.48f, -0.05f), at + turn * new Vector3(0.45f, -0.5f, -0.05f), 0.016f, 0.016f, 6);
            foreach (float x in new[] { -0.32f, 0.32f })
                metal.Box(at + turn * new Vector3(x, -0.31f, 0f), new Vector3(0.08f, 0.07f, 0.28f), turn);
        }

        /// <summary>
        /// A washing line: a sagging cord, pegs, and garments -- sheets, shirts with sleeves and
        /// trousers -- each a draped grid with folds in it, a swinging hem and a wavy edge, hanging
        /// from the line. They were flat rectangles. <paramref name="a"/> and <paramref name="b"/> are
        /// the line's ends; the garments alternate between two meshes so two colours show.
        /// </summary>
        private static void Washing(MeshBuild clothA, MeshBuild clothB, MeshBuild metal, Vector3 a, Vector3 b, System.Random rng)
        {
            var span = b - a;
            float length = span.magnitude;
            if (length < 1.5f) return;
            var along = new Vector3(span.x, 0f, span.z).normalized;
            var n = Vector3.Cross(along, Vector3.up).normalized;
            float sag = Mathf.Clamp(length * 0.025f, 0.05f, 0.16f);

            Vector3 Line(float t) => Vector3.Lerp(a, b, t) - Vector3.up * sag * (1f - (2f * t - 1f) * (2f * t - 1f));

            const int segs = 10;
            for (int i = 0; i < segs; i++)
                metal.Tube(Line(i / (float)segs), Line((i + 1) / (float)segs), 0.007f, 0.007f, 3);

            float t0 = 0.08f;
            int idx = 0;
            while (t0 < 0.9f)
            {
                int kind = rng.Next(3);
                float w = kind == 0 ? Rand(rng, 0.8f, 1.2f) : kind == 1 ? Rand(rng, 0.55f, 0.7f) : Rand(rng, 0.5f, 0.62f);
                float h = kind == 0 ? Rand(rng, 0.85f, 1.25f) : kind == 1 ? Rand(rng, 0.65f, 0.85f) : Rand(rng, 0.85f, 1.05f);
                float tw = w / length;
                if (t0 + tw > 0.94f) break;

                Garment(idx++ % 2 == 0 ? clothA : clothB, metal, Line(t0), Line(t0 + tw), along, n, w, h, kind, rng);
                t0 += tw + Rand(rng, 0.03f, 0.07f);
            }
        }

        /// <summary>Hangs one garment's grid between two points on the line, double-sided, with folds.</summary>
        private static void Garment(MeshBuild m, MeshBuild metal, Vector3 left, Vector3 right, Vector3 along, Vector3 n,
                                    float w, float h, int kind, System.Random rng)
        {
            float phase = Rand(rng, 0f, 6.28f);
            float folds = kind == 0 ? Rand(rng, 2f, 3.5f) : Rand(rng, 1.5f, 2.5f);

            Vector3 P(float u, float v, float hang)
            {
                var top = Vector3.Lerp(left, right, u);
                // A droop along the top edge between the pegs, folds that deepen towards the hem, a hem that
                // swings out, and an edge that is never straight.
                float fold = Mathf.Sin((u * folds + phase) * Mathf.PI) * 0.05f * (0.25f + v)
                           + Fbm2(u * 4f + phase, v * 3f, 7, 2) * 0.025f * v;
                float swing = v * v * 0.06f;
                float ragged = Mathf.Sin(u * 9f + phase) * 0.018f * v;
                return top + along * (Mathf.Sin(v * 3f + phase) * 0.015f * v) - Vector3.up * (v * hang + ragged) + n * (fold + swing);
            }

            void Panel(float u0, float u1, float v0, float v1, float hang, int cols, int rows)
            {
                for (int i = 0; i < cols; i++)
                    for (int j = 0; j < rows; j++)
                    {
                        float ua = Mathf.Lerp(u0, u1, i / (float)cols), ub = Mathf.Lerp(u0, u1, (i + 1) / (float)cols);
                        float va = Mathf.Lerp(v0, v1, j / (float)rows), vb = Mathf.Lerp(v0, v1, (j + 1) / (float)rows);
                        var p00 = P(ua, va, hang); var p10 = P(ub, va, hang); var p11 = P(ub, vb, hang); var p01 = P(ua, vb, hang);
                        m.Quad(p00, p10, p11, p01);
                        m.Quad(p01, p11, p10, p00);
                    }
            }

            if (kind == 0)
            {
                Panel(0f, 1f, 0f, 1f, h, 7, 8);
            }
            else if (kind == 1)
            {
                // A shirt: the body, and a sleeve out each side hanging a little way down.
                Panel(0.22f, 0.78f, 0f, 1f, h, 4, 8);
                Panel(0f, 0.22f, 0f, 0.5f, h, 2, 4);
                Panel(0.78f, 1f, 0f, 0.5f, h, 2, 4);
            }
            else
            {
                // Trousers: a waistband, then two legs with a gap between.
                Panel(0f, 1f, 0f, 0.12f, h, 5, 2);
                Panel(0f, 0.46f, 0.12f, 1f, h, 3, 8);
                Panel(0.54f, 1f, 0.12f, 1f, h, 3, 8);
            }

            // Pegs at both top corners.
            metal.Box(left + Vector3.down * 0.02f, new Vector3(0.025f, 0.06f, 0.03f), Quaternion.identity);
            metal.Box(right + Vector3.down * 0.02f, new Vector3(0.025f, 0.06f, 0.03f), Quaternion.identity);
        }

        // ==================================================================
        // Wrecks, carts, wheels
        // ==================================================================
        /// <summary>Copies one builder into another at an offset and scale (for reusing a prop built at one size).</summary>
        private static void AppendScaled(MeshBuild dst, MeshBuild src, Vector3 offset, float scale, Quaternion rot)
        {
            int start = dst.Vertices.Count;
            for (int i = 0; i < src.Vertices.Count; i++)
            {
                dst.Vertices.Add(offset + rot * (src.Vertices[i] * scale));
                dst.Normals.Add(rot * src.Normals[i]);
                dst.UVs.Add(src.UVs[i]);
            }
            foreach (int t in src.Triangles) dst.Triangles.Add(start + t);
        }

        /// <summary>
        /// A tyre with a rounded shoulder, a rim and hub caps, not a bare cylinder: a wheel on a vehicle is
        /// the one part everybody looks at, and twelve flat sides read as a nut.
        /// </summary>
        private static void TyreWheel(MeshBuild m, Vector3 centre, Vector3 axis, float radius, float width)
        {
            axis = axis.normalized;
            const int sides = 28;
            var a = centre - axis * width * 0.5f;
            var b = centre + axis * width * 0.5f;

            m.Tube(a, a + axis * width * 0.18f, radius * 0.9f, radius * 0.99f, sides);
            m.Tube(a + axis * width * 0.18f, b - axis * width * 0.18f, radius, radius, sides);
            m.Tube(b - axis * width * 0.18f, b, radius * 0.99f, radius * 0.9f, sides);

            // Rim, and a hub cap each side.
            m.Tube(a - axis * 0.01f, b + axis * 0.01f, radius * 0.62f, radius * 0.62f, 20);
            foreach (float sd in new[] { -1f, 1f })
            {
                var c0 = centre + axis * sd * (width * 0.5f + 0.012f);
                m.Tube(c0, c0 + axis * sd * 0.035f, radius * 0.34f, radius * 0.3f, 14);
            }
        }

        /// <summary>
        /// A burnt-out tank or armoured carrier, built round its own origin (ground at y = 0, front at +z):
        /// a sloped glacis and lower plate, rounded hull and fenders, an engine deck of louvre bars, road
        /// wheels, a sprocket and an idler, and a track made of individual links; on the tank a rounded
        /// turret knocked round, a mantlet, a drooping barrel with a muzzle brake, an open hatch and smoke
        /// dischargers; on the carrier a deck, an open hatch and a ring-mount gun. Replaces a stack of boxes.
        /// </summary>
        private static void WreckTank(MeshBuild hull, MeshBuild tracks, System.Random rng, bool tank)
        {
            int seed = rng.Next(1, 9999);

            // ---- hull ----
            hull.SoftBox(new Vector3(0f, 0.95f, 0f), new Vector3(3.0f, 0.95f, 6.8f), Quaternion.identity, seed, 0.12f, 5, 0f);
            hull.SoftBox(new Vector3(0f, 1.5f, -0.9f), new Vector3(3.15f, 0.5f, 4.5f), Quaternion.identity, seed + 1, 0.1f, 5, 0f);
            hull.SoftBox(new Vector3(0f, 1.28f, 3.15f), new Vector3(3.0f, 0.2f, 1.9f), Quaternion.Euler(32f, 0f, 0f), seed + 2, 0.06f, 4, 0f);
            hull.SoftBox(new Vector3(0f, 0.72f, 3.65f), new Vector3(3.0f, 0.2f, 1.0f), Quaternion.Euler(-38f, 0f, 0f), seed + 3, 0.06f, 4, 0f);

            foreach (float sd in new[] { -1f, 1f })
            {
                hull.SoftBox(new Vector3(sd * 1.78f, 1.22f, 0f), new Vector3(0.65f, 0.09f, 7.3f), Quaternion.identity, seed + 4, 0.04f, 3, 0f);
                hull.SoftBox(new Vector3(sd * 1.78f, 1.14f, 3.7f), new Vector3(0.65f, 0.09f, 0.8f), Quaternion.Euler(-24f, 0f, 0f), seed + 5, 0.04f, 3, 0f);
                // Stowage boxes on the fenders and a spare track length on the glacis.
                hull.SoftBox(new Vector3(sd * 1.72f, 1.42f, -1.6f), new Vector3(0.45f, 0.3f, 1.2f), Quaternion.identity, seed + 6, 0.05f, 3, 0f);
            }

            // Engine deck louvres and two exhaust stacks at the back.
            for (int i = 0; i < 9; i++)
                hull.Box(new Vector3(0f, 1.79f, -2.9f + i * 0.34f), new Vector3(1.9f, 0.035f, 0.17f), Quaternion.identity);
            foreach (float sd in new[] { -1f, 1f })
                hull.Tube(new Vector3(sd * 1.1f, 1.7f, -3.35f), new Vector3(sd * 1.1f, 2.05f, -3.35f), 0.11f, 0.09f, 10);

            // Tow cables looped over the front, and a headlight each side.
            hull.Tube(new Vector3(-1.2f, 1.45f, 3.0f), new Vector3(-1.2f, 0.75f, 3.9f), 0.035f, 0.035f, 5);
            hull.Tube(new Vector3(1.2f, 1.45f, 3.0f), new Vector3(1.2f, 0.75f, 3.9f), 0.035f, 0.035f, 5);
            foreach (float sd in new[] { -1f, 1f })
                hull.Tube(new Vector3(sd * 1.3f, 1.3f, 4.05f), new Vector3(sd * 1.3f, 1.3f, 4.2f), 0.1f, 0.08f, 10);

            // ---- running gear and track ----
            foreach (float sd in new[] { -1f, 1f })
            {
                float x = sd * 1.75f;
                var axis = Vector3.right * sd;

                // Six road wheels, a sprocket at the back and an idler at the front.
                for (int w = 0; w < 6; w++)
                    TyreWheel(tracks, new Vector3(x, 0.48f, -2.55f + w * 1.0f), Vector3.right, 0.4f, 0.5f);
                TyreWheel(tracks, new Vector3(x, 0.62f, -3.25f), Vector3.right, 0.46f, 0.48f);
                TyreWheel(tracks, new Vector3(x, 0.62f, 3.25f), Vector3.right, 0.42f, 0.48f);

                // The track: links round a stadium loop (top run, front arc, bottom run, rear arc).
                const float topY = 1.08f, botY = 0.06f, frontZ = 3.25f, backZ = -3.25f;
                float cy = (topY + botY) * 0.5f, rr = (topY - botY) * 0.5f;
                float run = frontZ - backZ;
                float arc = Mathf.PI * rr;
                float perimeter = 2f * run + 2f * arc;
                int links = 96;

                for (int i = 0; i < links; i++)
                {
                    float d = (i + 0.5f) / links * perimeter;
                    Vector3 pos; float angle;       // angle of the link in the y-z plane, 0 = along +z

                    if (d < run) { pos = new Vector3(x, botY, backZ + d); angle = 0f; }
                    else if (d < run + arc)
                    {
                        float t = (d - run) / arc * Mathf.PI;
                        pos = new Vector3(x, cy - Mathf.Cos(t) * rr, frontZ + Mathf.Sin(t) * rr);
                        angle = -t * Mathf.Rad2Deg;
                    }
                    else if (d < 2f * run + arc) { pos = new Vector3(x, topY, frontZ - (d - run - arc)); angle = 180f; }
                    else
                    {
                        float t = (d - 2f * run - arc) / arc * Mathf.PI;
                        pos = new Vector3(x, cy + Mathf.Cos(t) * rr, backZ - Mathf.Sin(t) * rr);
                        angle = -(180f + t * Mathf.Rad2Deg);
                    }

                    var q = Quaternion.Euler(angle, 0f, 0f);
                    tracks.Box(pos, new Vector3(0.62f, 0.07f, perimeter / links * 0.9f), q);
                    // A cleat on every second link.
                    if (i % 2 == 0) tracks.Box(pos + q * new Vector3(0f, 0.05f, 0f), new Vector3(0.62f, 0.04f, 0.05f), q);
                }

                // Return rollers on top of the run, and the guard over it.
                for (int r = 0; r < 3; r++)
                    TyreWheel(tracks, new Vector3(x, 1.2f, -1.6f + r * 1.7f), Vector3.right, 0.1f, 0.4f);
            }

            // ---- superstructure ----
            if (tank)
            {
                var tr = Quaternion.Euler(0f, Rand(rng, 40f, 140f), 0f);
                var tc = new Vector3(0.2f, 2.05f, 0.3f);

                hull.SoftBox(tc, new Vector3(2.3f, 0.75f, 2.7f), tr, seed + 7, 0.24f, 6, 0f);
                hull.SoftBox(tc + tr * new Vector3(0f, 0.12f, 1.35f), new Vector3(1.5f, 0.6f, 0.7f), tr, seed + 8, 0.2f, 5, 0f);
                hull.SoftBox(tc + tr * new Vector3(0f, 0.04f, -1.45f), new Vector3(2.0f, 0.55f, 0.6f), tr, seed + 9, 0.18f, 5, 0f);   // turret bustle

                // Mantlet, barrel, bore brake: the barrel droops where the turret was knocked.
                var mount = tc + tr * new Vector3(0f, 0.12f, 1.75f);
                var dirB = tr * Quaternion.Euler(Rand(rng, 4f, 12f), 0f, 0f) * Vector3.forward;
                hull.Tube(mount - tr * Vector3.right * 0.4f, mount + tr * Vector3.right * 0.4f, 0.34f, 0.34f, 18);
                hull.Tube(mount, mount + dirB * 1.2f, 0.17f, 0.14f, 18);
                hull.Tube(mount + dirB * 1.2f, mount + dirB * 4.7f, 0.14f, 0.1f, 18);
                hull.Tube(mount + dirB * 4.7f, mount + dirB * 5.05f, 0.17f, 0.17f, 16);
                hull.Tube(mount + dirB * 4.85f, mount + dirB * 4.98f, 0.19f, 0.19f, 16);

                // Cupola with its hatch thrown open, and a machine gun on a pintle.
                var cup = tc + tr * new Vector3(0.55f, 0.37f, -0.35f);
                hull.Tube(cup, cup + Vector3.up * 0.22f, 0.4f, 0.4f, 20);
                hull.Box(cup + tr * new Vector3(0.3f, 0.5f, -0.1f), new Vector3(0.65f, 0.05f, 0.7f), tr * Quaternion.Euler(0f, 0f, -75f));
                hull.Tube(tc + tr * new Vector3(-0.6f, 0.4f, 0.2f), tc + tr * new Vector3(-0.6f, 0.6f, 0.2f), 0.04f, 0.04f, 6);
                hull.Tube(tc + tr * new Vector3(-0.6f, 0.6f, 0.2f), tc + tr * new Vector3(-0.6f, 0.62f, 1.0f), 0.04f, 0.035f, 8);

                // Smoke dischargers, three each side of the turret front.
                foreach (float sd in new[] { -1f, 1f })
                    for (int k = 0; k < 3; k++)
                    {
                        var at = tc + tr * new Vector3(sd * 1.05f, 0.1f + k * 0.0f, 0.55f - k * 0.2f);
                        hull.Tube(at, at + tr * new Vector3(sd * 0.1f, 0.22f, 0.05f), 0.055f, 0.05f, 8);
                    }
            }
            else
            {
                hull.SoftBox(new Vector3(0f, 2.15f, -0.9f), new Vector3(3.0f, 0.95f, 4.3f), Quaternion.identity, seed + 7, 0.18f, 6, 0f);
                hull.SoftBox(new Vector3(0f, 2.35f, 1.5f), new Vector3(2.4f, 0.5f, 1.0f), Quaternion.Euler(-14f, 0f, 0f), seed + 8, 0.12f, 4, 0f);
                // An open roof hatch and a ring mount with its gun.
                hull.Box(new Vector3(-0.7f, 2.82f, -1.2f), new Vector3(0.8f, 0.05f, 0.8f), Quaternion.Euler(0f, 0f, 70f));
                hull.Tube(new Vector3(0.7f, 2.62f, -1.4f), new Vector3(0.7f, 2.78f, -1.4f), 0.5f, 0.5f, 20);
                hull.Tube(new Vector3(0.7f, 2.78f, -1.4f), new Vector3(0.7f, 3.0f, -1.2f), 0.04f, 0.04f, 6);
                hull.Tube(new Vector3(0.7f, 3.0f, -1.2f), new Vector3(0.7f, 3.0f, -0.2f), 0.045f, 0.04f, 10);
                foreach (float sd in new[] { -1f, 1f })
                    hull.SoftBox(new Vector3(sd * 1.55f, 2.2f, -0.9f), new Vector3(0.1f, 0.6f, 3.2f), Quaternion.identity, seed + 9, 0.03f, 2, 0f);
            }
        }

        /// <summary>
        /// A farm handcart: planked bed with side rails and stakes, a back board, two big spoked wheels on an
        /// axle, a pair of shafts running forward to a prop leg, and a load of sacks. Built round its origin,
        /// ground at y = 0, front towards +z. It was a plank, two rails and two discs.
        /// </summary>
        private static void HandCart(MeshBuild wood, MeshBuild metal, Vector3 at, Quaternion rot, System.Random rng)
        {
            Vector3 P(float x, float y, float z) => at + rot * new Vector3(x, y, z);

            // Bed: seven planks with gaps, two cross-bearers under them.
            for (int i = 0; i < 7; i++)
                wood.Box(P(-0.6f + i * 0.2f, 0.78f, -0.1f), new Vector3(0.18f, 0.045f, 2.2f), rot);
            foreach (float z in new[] { -0.8f, 0.7f })
                wood.Box(P(0f, 0.72f, z), new Vector3(1.5f, 0.07f, 0.09f), rot);

            // Side rails, stakes and a back board.
            foreach (float sd in new[] { -1f, 1f })
            {
                wood.Box(P(sd * 0.68f, 1.15f, -0.1f), new Vector3(0.05f, 0.07f, 2.2f), rot);
                wood.Box(P(sd * 0.68f, 0.98f, -0.1f), new Vector3(0.05f, 0.07f, 2.2f), rot);
                for (int k = 0; k < 5; k++)
                    wood.Tube(P(sd * 0.68f, 0.76f, -1.08f + k * 0.5f), P(sd * 0.68f, 1.2f, -1.08f + k * 0.5f), 0.032f, 0.026f, 6);
            }
            wood.Box(P(0f, 0.98f, -1.18f), new Vector3(1.4f, 0.3f, 0.05f), rot);

            // Axle and two spoked wheels: a rim of curved segments, twelve spokes, a hub.
            var axle0 = P(-0.82f, 0.5f, -0.2f); var axle1 = P(0.82f, 0.5f, -0.2f);
            metal.Tube(axle0, axle1, 0.04f, 0.04f, 8);
            foreach (float sd in new[] { -1f, 1f })
            {
                var c = P(sd * 0.82f, 0.5f, -0.2f);
                var ax = rot * Vector3.right;
                var up = rot * Vector3.up; var fw = rot * Vector3.forward;
                const int seg = 24;
                for (int k = 0; k < seg; k++)
                {
                    float a0 = k * Mathf.PI * 2f / seg, a1 = (k + 1) * Mathf.PI * 2f / seg;
                    var p0 = c + (up * Mathf.Cos(a0) + fw * Mathf.Sin(a0)) * 0.5f;
                    var p1 = c + (up * Mathf.Cos(a1) + fw * Mathf.Sin(a1)) * 0.5f;
                    wood.Tube(p0 - ax * 0.04f, p1 - ax * 0.04f, 0.032f, 0.032f, 5);
                    wood.Tube(p0 + ax * 0.04f, p1 + ax * 0.04f, 0.032f, 0.032f, 5);
                }
                for (int k = 0; k < 12; k++)
                {
                    float a = k * Mathf.PI * 2f / 12f;
                    wood.Tube(c, c + (up * Mathf.Cos(a) + fw * Mathf.Sin(a)) * 0.48f, 0.026f, 0.02f, 5);
                }
                wood.Tube(c - ax * 0.09f, c + ax * 0.09f, 0.07f, 0.07f, 12);
                metal.Tube(c - ax * 0.11f, c - ax * 0.07f, 0.05f, 0.05f, 8);
            }

            // Shafts: two long poles forward, rising to the (absent) animal, joined by a cross-pole, with a prop leg.
            foreach (float sd in new[] { -1f, 1f })
                wood.Tube(P(sd * 0.55f, 0.82f, -0.2f), P(sd * 0.42f, 0.62f, 2.6f), 0.04f, 0.03f, 7);
            wood.Tube(P(-0.48f, 0.7f, 1.8f), P(0.48f, 0.7f, 1.8f), 0.025f, 0.025f, 6);
            wood.Tube(P(0f, 0.7f, 1.8f), P(0f, 0f, 1.95f), 0.03f, 0.025f, 6);

            // A load: three sacks and a bundle of fodder.
            for (int i = 0; i < 3; i++)
                wood.SoftBox(P(-0.35f + i * 0.35f, 1.0f, -0.5f + (i % 2) * 0.25f), new Vector3(0.45f, 0.3f, 0.7f),
                             rot * Quaternion.Euler(0f, Rand(rng, -25f, 25f), 0f), rng.Next(1, 999), 0.12f, 4, 0f);
            wood.Tube(P(0.1f, 0.95f, 0.5f), P(0.1f, 0.95f, 1.3f), 0.18f, 0.18f, 10);
        }
    }
}
#endif

// 
