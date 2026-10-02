#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The parts the abandoned fairground is made of: stairs that can be climbed, lattice
    /// masts, circular rims, sagging cloth, horses and ferris-wheel cabins.
    ///
    /// <b>A fairground is steel and canvas, not cubes.</b> The first one stood every ride up
    /// out of stretched boxes, which is why it read as blocks. Everything here is built
    /// from tubes and strips between points, in the order a welder would: chords first,
    /// then braces, then the skin. Thin members are three- or four-sided, because a
    /// rail forty metres up is seen as a line and a hexagon there is just triangles.
    ///
    /// <b>Stairs are the one thing that must be real.</b> A staircase here is a stack of
    /// solid treads, each a step higher than the one before, with the same rise and tread
    /// the industrial walkways use -- the proportions the navigation bake is known to join.
    /// Its landing is flush with the deck it serves, to the millimetre: the flight ends
    /// where the deck starts, at the height its last tread reaches.
    /// </summary>
    public partial class FPSKitSceneBuilder
    {
        // ------------------------------------------------------------------
        // Stairs
        // ------------------------------------------------------------------
        /// <summary>How many whole risers reach at least this height.</summary>
        private static int FgSteps(float height) => Mathf.Max(2, Mathf.RoundToInt(height / StairRise));

        /// <summary>The exact height a flight of <paramref name="steps"/> reaches.</summary>
        private static float FgStairHeight(int steps) => steps * StairRise;

        /// <summary>Metres a flight of this many steps covers along the ground.</summary>
        private static float FgStairRun(int steps) => steps * StairTread;

        /// <summary>
        /// A flight of stairs that starts on the floor at <paramref name="foot"/> and climbs
        /// along the direction <paramref name="yaw"/> faces. The top tread is
        /// <c>steps * rise</c> above the foot, and the deck it serves begins at
        /// <c>foot + forward * steps * tread</c>.
        /// </summary>
        private static GameObject FgStair(Transform parent, int layer, Material tread, Material steel,
                                          Vector3 foot, float yaw, int steps, float width, bool rails = true)
        {
            var build = new MeshBuild { UVScale = 0.5f };
            var rail = new MeshBuild { UVScale = 0.5f };

            for (int i = 0; i < steps; i++)
            {
                float h = (i + 1) * StairRise;
                build.Box(new Vector3(0f, h * 0.5f, i * StairTread + StairTread * 0.5f),
                          new Vector3(width, h, StairTread), Quaternion.identity);
            }

            if (rails)
            {
                float run = steps * StairTread, rise = steps * StairRise;

                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side * (width * 0.5f - 0.05f);
                    var low = new Vector3(x, StairRise + 0.95f, StairTread * 0.5f);
                    var high = new Vector3(x, rise + 0.95f, run - StairTread * 0.5f);
                    rail.Tube(low, high, 0.04f, 0.04f, 4);

                    for (int i = 0; i < steps; i += 4)
                    {
                        float h = (i + 1) * StairRise;
                        rail.Tube(new Vector3(x, h, i * StairTread + 0.15f), new Vector3(x, h + 0.95f, i * StairTread + 0.15f),
                                  0.025f, 0.025f, 3);
                    }
                }
            }

            var go = MeshObject(parent, "Stairs", ToMesh(build, DenseKey("stairs")), tread, foot,
                                Quaternion.Euler(0f, yaw, 0f), Vector3.one, layer, "Concrete");

            if (rail.Triangles.Count > 0)
            {
                var r = MeshObject(go.transform, "StairRail", ToMesh(rail, DenseKey("stairrail")), steel,
                                   Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal", collider: false);
                NoStanding(r);
            }

            return go;
        }

        /// <summary>The ground-plane point a flight's far end reaches.</summary>
        private static Vector3 FgStairTop(Vector3 foot, float yaw, int steps)
            => foot + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * FgStairRun(steps) + Vector3.up * FgStairHeight(steps);

        // ------------------------------------------------------------------
        // Lattice
        // ------------------------------------------------------------------
        /// <summary>
        /// A square lattice mast between two points: four corner chords, and a zig-zag brace
        /// and a ring in every panel. Crane masts, drop towers, scaffold and trusses.
        /// </summary>
        private static void Lattice(MeshBuild b, Vector3 from, Vector3 to, float width, float chordR, float braceR,
                                    int panels, int missingFrom = -1, int missingTo = -1)
        {
            var axis = to - from;
            float length = axis.magnitude;
            if (length < 0.1f) return;

            var dir = axis / length;
            var rot = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.99f ? Vector3.forward : Vector3.up);
            var right = rot * Vector3.right;
            var up = rot * Vector3.up;

            Vector3 Corner(int i, float t)
                => from + dir * (length * t) + (right * ((i & 1) == 0 ? -1f : 1f) + up * ((i & 2) == 0 ? -1f : 1f)) * (width * 0.5f);

            for (int i = 0; i < 4; i++)
                b.Tube(Corner(i, 0f), Corner(i, 1f), chordR, chordR, 4);

            int[,] faces = { { 0, 1 }, { 1, 3 }, { 3, 2 }, { 2, 0 } };

            for (int p = 0; p < panels; p++)
            {
                if (p >= missingFrom && p < missingTo) continue;

                float t0 = p / (float)panels, t1 = (p + 1) / (float)panels;

                for (int f = 0; f < 4; f++)
                {
                    int a = faces[f, 0], c = faces[f, 1];
                    if (((p + f) & 1) == 0) b.Tube(Corner(a, t0), Corner(c, t1), braceR, braceR, 3);
                    else b.Tube(Corner(c, t0), Corner(a, t1), braceR, braceR, 3);

                    b.Tube(Corner(a, t0), Corner(c, t0), braceR, braceR, 3);
                }
            }
        }

        /// <summary>
        /// A flat truss in one plane: top and bottom chord, and a zig-zag web between them.
        /// <paramref name="side"/> is the direction the truss is deep in.
        /// </summary>
        private static void FlatTruss(MeshBuild b, Vector3 from, Vector3 to, Vector3 side, float depth,
                                      float chordR, float webR, int panels)
        {
            var dir = (to - from);
            for (int p = 0; p < panels; p++)
            {
                float t0 = p / (float)panels, t1 = (p + 1) / (float)panels;
                var a0 = from + dir * t0; var a1 = from + dir * t1;
                var b0 = a0 + side * depth; var b1 = a1 + side * depth;

                b.Tube(a0, a1, chordR, chordR, 4);
                b.Tube(b0, b1, chordR, chordR, 4);
                b.Tube(a0, b0, webR, webR, 3);
                if ((p & 1) == 0) b.Tube(a0, b1, webR, webR, 3);
                else b.Tube(b0, a1, webR, webR, 3);
            }

            b.Tube(from + dir + side * 0f, from + dir + side * depth, webR, webR, 3);
        }

        // ------------------------------------------------------------------
        // Rims, arcs and cables
        // ------------------------------------------------------------------
        /// <summary>A tube following a circle in the plane of <paramref name="u"/> and <paramref name="v"/>.</summary>
        private static void Arc(MeshBuild b, Vector3 centre, Vector3 u, Vector3 v, float radius, float fromDeg,
                                float toDeg, float tubeR, int segments, int sides = 4)
        {
            Vector3 P(float deg) => centre + (u * Mathf.Cos(deg * Mathf.Deg2Rad) + v * Mathf.Sin(deg * Mathf.Deg2Rad)) * radius;

            var previous = P(fromDeg);
            for (int i = 1; i <= segments; i++)
            {
                var p = P(Mathf.Lerp(fromDeg, toDeg, i / (float)segments));
                b.Tube(previous, p, tubeR, tubeR, sides);
                previous = p;
            }
        }

        /// <summary>A hanging cable: a catenary-ish sag between two points.</summary>
        private static void Cable(MeshBuild b, Vector3 from, Vector3 to, float sag, float radius, int segments = 6)
        {
            var previous = from;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                var p = Vector3.Lerp(from, to, t) + Vector3.down * sag * 4f * t * (1f - t);
                b.Tube(previous, p, radius, radius, 3);
                previous = p;
            }
        }

        // ------------------------------------------------------------------
        // Cloth
        // ------------------------------------------------------------------
        /// <summary>
        /// A sheet of canvas or tarpaulin stretched between an origin and two edge vectors,
        /// sagging in the middle, torn in places. Drawn from both sides because cloth has no
        /// back. <paramref name="tear"/> is the share of cells that are gone.
        /// </summary>
        private static void Cloth(MeshBuild b, Vector3 origin, Vector3 u, Vector3 v, int nu, int nv, float sag,
                                  int seed, float tear = 0f)
        {
            var grid = new Vector3[nu + 1, nv + 1];

            for (int i = 0; i <= nu; i++)
                for (int j = 0; j <= nv; j++)
                {
                    float s = i / (float)nu, t = j / (float)nv;
                    var p = origin + u * s + v * t;
                    p += Vector3.down * sag * 4f * s * (1f - s) * (0.6f + 0.4f * Mathf.Sin(t * Mathf.PI));
                    p += new Vector3(Hash01(seed, i * 31 + j) - 0.5f, (Hash01(seed, i * 17 + j + 400) - 0.5f) * 0.6f,
                                     Hash01(seed, i * 13 + j + 800) - 0.5f) * sag * 0.25f;
                    grid[i, j] = p;
                }

            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    if (tear > 0f && Hash01(seed, i * 977 + j * 131 + 5) < tear) continue;

                    b.Quad(grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]);
                    b.Quad(grid[i, j + 1], grid[i + 1, j + 1], grid[i + 1, j], grid[i, j]);
                }
        }

        // ------------------------------------------------------------------
        // Ride furniture
        // ------------------------------------------------------------------
        /// <summary>
        /// A carousel horse: barrel, neck, head, four legs and a tail, on a brass pole. The
        /// legs are set mid-stride, because a standing horse is a stool; <paramref name="broken"/>
        /// ones have lost a leg and hang from the pole at an angle.
        /// </summary>
        private static void Horse(MeshBuild body, MeshBuild pole, Vector3 at, float yaw, float scale, bool broken,
                                  float poleTop, int seed)
        {
            var rot = Quaternion.Euler(0f, yaw, broken ? Hash01(seed, 4) * 40f - 20f : 0f);
            Vector3 P(float x, float y, float z) => at + rot * (new Vector3(x, y, z) * scale);

            body.Tube(P(0f, 1.25f, -0.62f), P(0f, 1.3f, 0.62f), 0.3f * scale, 0.34f * scale, 6);
            body.Tube(P(0f, 1.38f, 0.5f), P(0f, 2.05f, 0.88f), 0.22f * scale, 0.14f * scale, 5);
            body.Box(P(0f, 2.15f, 1.08f), new Vector3(0.22f, 0.26f, 0.5f) * scale, rot * Quaternion.Euler(-25f, 0f, 0f));
            body.Tube(P(0f, 1.2f, -0.62f), P(0f, 0.7f, -0.98f), 0.07f * scale, 0.02f * scale, 4);

            float stride = Hash01(seed, 1) * 0.3f;
            body.Tube(P(0.14f, 1.05f, 0.45f), P(0.18f, 0.55f, 0.75f + stride), 0.08f * scale, 0.05f * scale, 4);
            body.Tube(P(-0.14f, 1.05f, 0.45f), P(-0.18f, 0.6f, 0.6f - stride), 0.08f * scale, 0.05f * scale, 4);
            if (!broken)
            {
                body.Tube(P(0.14f, 1.05f, -0.45f), P(0.18f, 0.55f, -0.65f), 0.08f * scale, 0.05f * scale, 4);
                body.Tube(P(-0.14f, 1.05f, -0.45f), P(-0.18f, 0.6f, -0.78f), 0.08f * scale, 0.05f * scale, 4);
            }

            pole.Tube(at + Vector3.up * 0.05f, at + Vector3.up * poleTop, 0.045f * scale, 0.045f * scale, 4);
        }

        /// <summary>
        /// A ferris-wheel cabin hanging from <paramref name="top"/>: an open car with a floor,
        /// a bench either end, rails, a canopy and the hanger arm. A <paramref name="tilt"/>
        /// of zero is hanging true; more is a cabin that has swung out of true or slipped
        /// off one hook.
        /// </summary>
        private static void Gondola(MeshBuild frame, MeshBuild canopy, Vector3 top, float yaw, float tilt)
        {
            var rot = Quaternion.Euler(tilt * 0.4f, yaw, tilt);
            Vector3 P(float x, float y, float z) => top + rot * new Vector3(x, y, z);

            frame.Tube(P(0f, 0f, 0f), P(0f, -0.9f, 0f), 0.06f, 0.06f, 4);
            frame.Tube(P(-1.1f, -0.9f, 0f), P(1.1f, -0.9f, 0f), 0.05f, 0.05f, 4);

            // Floor and benches.
            frame.Box(P(0f, -2.55f, 0f), new Vector3(2.5f, 0.12f, 1.9f), rot);
            frame.Box(P(0f, -2.1f, 0.78f), new Vector3(2.1f, 0.1f, 0.4f), rot);
            frame.Box(P(0f, -2.1f, -0.78f), new Vector3(2.1f, 0.1f, 0.4f), rot);

            // Posts at the corners and a rail round the top and middle.
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    frame.Tube(P(sx * 1.2f, -2.5f, sz * 0.92f), P(sx * 1.2f, -0.95f, sz * 0.92f), 0.04f, 0.04f, 3);

            for (int sz = -1; sz <= 1; sz += 2)
            {
                frame.Tube(P(-1.2f, -1.0f, sz * 0.92f), P(1.2f, -1.0f, sz * 0.92f), 0.035f, 0.035f, 3);
                frame.Tube(P(-1.2f, -1.7f, sz * 0.92f), P(1.2f, -1.7f, sz * 0.92f), 0.03f, 0.03f, 3);
            }

            // The canopy: a shallow tent in two colours of the park's paint.
            canopy.Box(P(0f, -0.88f, 0f), new Vector3(2.9f, 0.1f, 2.3f), rot * Quaternion.Euler(0f, 0f, 0f));
            canopy.Tube(P(0f, -0.95f, 0f), P(0f, -0.62f, 0f), 1.0f, 0.3f, 6);
        }

        /// <summary>A pallet of bricks, a stack of planks or a drum: small solid cover for the construction side.</summary>
        private static void BrickPile(MeshBuild b, Vector3 foot, float yaw, int cols, int rows, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    if (y > 0 && rng.NextDouble() < 0.12 * y) continue;
                    b.Box(foot + rot * new Vector3((x - (cols - 1) * 0.5f) * 0.62f, 0.12f + y * 0.24f, 0f),
                          new Vector3(0.6f, 0.22f, 0.9f), rot);
                }
        }
    }
}
#endif
