#if UNITY_EDITOR
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
    }
}
#endif

// 
