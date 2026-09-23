#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The volcanic plain's ground: what shape it is, and what is lying on it.
    ///
    /// The first build of the Unknown Planet reused the desert's dune generator with the
    /// sand swapped for basalt, and the verdict was "too predictable and very artificial".
    /// Both were true and both had the same cause: a dune field is <i>regular</i> by
    /// nature -- trains of ridges at one wavelength, because one wind made them -- and a
    /// lava field is the opposite. It is the sum of a lot of separate accidents: this flow
    /// went here and stopped, that one ran over it, the crust broke and tilted, a roof
    /// collapsed into a hollow, ash blew into the low places. Nothing on it is repeated,
    /// because nothing on it happened twice.
    ///
    /// So this file makes accidents, and makes each one on purpose.
    ///
    /// <b>The shape</b> (<see cref="VolcanicHeightAt"/>) is the desert's broad basins and
    /// hundred-metre rises, with the dune trains taken out and three things put in: raised
    /// <b>flow lobes</b> that meander across the plain and stop with a rounded front,
    /// <b>tumuli</b> -- the low swellings a flow's crust is pushed up into from below --
    /// and <b>collapse pits</b> where a lava tube's roof fell in. All of it is sized in
    /// tens of metres, for the reason the desert's ripples are not in the heightfield: the
    /// grid is three metres, and the ground is held to the same 26 degrees and the same
    /// chatter limit as the desert's, because the vehicle cap is not a desert rule.
    ///
    /// <b>The surface</b> (<see cref="BuildLavaFieldSurface"/>) is what the heightfield is
    /// too coarse to carry: the glossy skin of the newer flows, laid over the lobes the
    /// heightfield raised; drifts of ash in the hollows; sulphur round the vents; fissures
    /// still glowing; and rubble and broken crust in fields. All of it is draped on the
    /// ground rather than placed on it, on the layer the navigation bake ignores, with no
    /// collider -- it is what the ground is wearing, not something to stand on.
    ///
    /// <b>A drape sinks at its edge rather than floating over the ground.</b> Laid exactly
    /// on the surface, a decal fights the terrain for every pixel past a couple of hundred
    /// metres, which is a shimmer across the whole far half of the map. Raised a few
    /// centimetres in the middle and sunk at the rim, it genuinely crosses the ground
    /// along its outline and is clear of it everywhere else, and the outline is a clean
    /// line where one surface goes under the other -- which is also what a drift of ash
    /// or the edge of a newer flow actually looks like.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        /// <summary>One raised lobe of cooled lava, as a meandering path with a width along it.</summary>
        private sealed class LavaFlowPlan
        {
            public Vector2[] Path;
            public float[] HalfWidth;
            public float Thickness;
            public float Edge;
            public bool Fresh;
            public Vector4 Bounds;   // min x, min z, max x, max z, padded by the widest half-width
        }

        private static readonly List<LavaFlowPlan> _lavaFlows = new List<LavaFlowPlan>();

        /// <summary>Collapse pits as (x, z, radius, depth).</summary>
        private static readonly List<Vector4> _collapsePits = new List<Vector4>();

        /// <summary>Where each vent stands, so the sulphur can be laid round it.</summary>
        private static readonly List<Vector3> _ventSites = new List<Vector3>();

        /// <summary>
        /// Cleared beside the terrain for every arena, for the reason ResetTerrain gives:
        /// a six-arena batch would otherwise hand the desert the last arena's lava flows.
        /// </summary>
        private static void ResetLavaField()
        {
            _lavaFlows.Clear();
            _collapsePits.Clear();
            _ventSites.Clear();
        }

        // ==================================================================
        // Planning
        // ==================================================================
        /// <summary>
        /// Chooses the flows and the pits. Has to run before anything asks the ground how
        /// high it is -- the crossings pin their decks to NaturalHeightAt during planning,
        /// and a deck pinned before the flows existed is a deck at the wrong height.
        ///
        /// Seeded on its own stream so that it does not shift every choice made after it
        /// on the arena's main one.
        /// </summary>
        private static void PlanLavaField(float half)
        {
            ResetLavaField();
            if (!_theme.volcanicZone) return;

            var rng = new System.Random(_theme.randomSeed * 7919 + 17);

            // ---- flows ----
            int flows = 11 + rng.Next(4);

            for (int f = 0; f < flows; f++)
            {
                // Some come in from past the boundary -- from the volcanoes on the horizon --
                // and some rise out of the plain itself, where a vent fed them.
                Vector2 start;
                float heading;

                if (rng.NextDouble() < 0.45)
                {
                    float side = Rand(rng, 0f, Mathf.PI * 2f);
                    start = new Vector2(Mathf.Cos(side), Mathf.Sin(side)) * (half + 30f);
                    heading = side + Mathf.PI + Rand(rng, -0.7f, 0.7f);
                }
                else
                {
                    start = new Vector2(Rand(rng, -half * 0.85f, half * 0.85f), Rand(rng, -half * 0.85f, half * 0.85f));
                    heading = Rand(rng, 0f, Mathf.PI * 2f);
                }

                float length = Rand(rng, 90f, 260f);
                const float Stride = 10f;
                int steps = Mathf.Max(3, Mathf.RoundToInt(length / Stride));

                float width = Rand(rng, 14f, 28f);
                float bend = 0f;

                var path = new Vector2[steps + 1];
                var halves = new float[steps + 1];
                path[0] = start;

                for (int i = 0; i <= steps; i++)
                {
                    if (i > 0)
                    {
                        // The turn itself wanders, so a flow swings in long curves rather
                        // than zig-zagging a step at a time.
                        bend = Mathf.Clamp(bend + Rand(rng, -0.09f, 0.09f), -0.16f, 0.16f);
                        heading += bend;
                        path[i] = path[i - 1] + new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * Stride;
                    }

                    // Wider where it pooled, narrower where it ran, and a little narrower
                    // towards the front as it ran out of heat.
                    float along = i / (float)steps;
                    float swell = 0.75f + 0.5f * (Fbm2(i * 0.35f, f * 3.1f, 6100 + f, 2) + 0.5f);
                    halves[i] = width * swell * Mathf.Lerp(1f, 0.8f, along);
                }

                float edge = Rand(rng, 13f, 17f);
                var plan = new LavaFlowPlan
                {
                    Path = path,
                    HalfWidth = halves,
                    Thickness = Rand(rng, 1.3f, 2.4f),
                    Edge = edge,
                    Fresh = rng.NextDouble() < 0.6
                };

                float pad = 0f;
                foreach (var h in halves) pad = Mathf.Max(pad, h);

                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                foreach (var p in path)
                {
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y);
                }

                plan.Bounds = new Vector4(minX - pad, minZ - pad, maxX + pad, maxZ + pad);
                _lavaFlows.Add(plan);
            }

            // ---- collapse pits ----
            // Clear of the spawn and of the river's rim, both of which are held flat and
            // have things built on them.
            float rimClear = _theme.hazardWidth * 0.5f + RimBandWidth + RiverValleyRamp;
            int pits = 7 + rng.Next(4);

            for (int i = 0, tries = 0; i < pits && tries < 200; tries++)
            {
                float radius = Rand(rng, 10f, 17f);
                var at = new Vector2(Rand(rng, -half * 0.85f, half * 0.85f), Rand(rng, -half * 0.85f, half * 0.85f));

                if (at.magnitude < 45f + radius) continue;
                if (Mathf.Abs(at.x - GorgeCentreAt(at.y)) < rimClear + radius) continue;

                _collapsePits.Add(new Vector4(at.x, at.y, radius, Rand(rng, 1.2f, 2.3f)));
                i++;
            }
        }

        // ==================================================================
        // The shape
        // ==================================================================
        /// <summary>
        /// The raw height of the volcanic plain at a point, before the river's valley, the
        /// pads, the relaxation and the smoothing -- the volcanic counterpart of
        /// <see cref="DuneHeightAt"/>, which calls it.
        /// </summary>
        private static float VolcanicHeightAt(float x, float z)
        {
            int seed = _theme.randomSeed;
            float amplitude = _theme.duneHeight;

            // Broad relief, as the desert has it: the basins between flows, and a rise
            // every hundred metres or so to decide whether to go over or round.
            float basin = Fbm2(x * 0.0032f, z * 0.0032f, seed * 17 + 21, 3) * amplitude * 1.6f;
            float hills = Fbm2(x * 0.0092f, z * 0.0092f, seed * 29 + 5, 3) * amplitude * 0.9f;

            // Flow lobes. Where two overlap the later one rides up over the earlier, so
            // the second counts for less than its full thickness rather than nothing.
            float top = 0f, under = 0f;

            foreach (var flow in _lavaFlows)
            {
                float lobe = FlowLobeAt(flow, x, z);
                if (lobe <= 0f) continue;

                if (lobe > top) { under = top; top = lobe; }
                else if (lobe > under) under = lobe;
            }

            float flows = top + under * 0.4f;

            // Tumuli: the crust of a flow pushed up from below into low swellings about
            // thirty metres across. Taller on the flows, where there was crust to lift.
            float swell = Mathf.Max(0f, Fbm2(x * 0.031f, z * 0.031f, seed * 43 + 7, 2) - 0.1f);
            float tumuli = swell * (1.6f + top * 0.7f);

            // Pits: a cosine bowl each, so the rim has no lip to trip on.
            float pits = 0f;

            foreach (var pit in _collapsePits)
            {
                float d = new Vector2(x - pit.x, z - pit.y).magnitude;
                if (d >= pit.z) continue;

                pits = Mathf.Max(pits, pit.w * 0.5f * (1f + Mathf.Cos(Mathf.PI * d / pit.z)));
            }

            return basin + hills + flows + tumuli - pits - amplitude * 0.35f;
        }

        /// <summary>
        /// How far a flow lifts the ground at a point: its thickness across the flat top,
        /// falling to nothing over <see cref="LavaFlowPlan.Edge"/> metres at the sides and
        /// at the front. The front is rounded for free, because the distance to a path
        /// that has ended is the distance to its last point.
        /// </summary>
        private static float FlowLobeAt(LavaFlowPlan flow, float x, float z)
        {
            var b = flow.Bounds;
            if (x < b.x || z < b.y || x > b.z || z > b.w) return 0f;

            float d = DistanceToFlow(flow, new Vector2(x, z), out float halfWidth);
            if (d >= halfWidth) return 0f;

            // The edge can never be wider than the flow, or a narrow one has no top.
            float edge = Mathf.Min(flow.Edge, halfWidth * 0.9f);

            return flow.Thickness * (1f - Ramp(halfWidth - edge, halfWidth, d));
        }

        /// <summary>Distance from a point to a flow's centre line, and the flow's half-width there.</summary>
        private static float DistanceToFlow(LavaFlowPlan flow, Vector2 p, out float halfWidth)
        {
            float best = float.MaxValue;
            halfWidth = flow.HalfWidth[0];

            for (int i = 0; i < flow.Path.Length - 1; i++)
            {
                var a = flow.Path[i];
                var ab = flow.Path[i + 1] - a;

                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                float d = (a + ab * t - p).magnitude;

                if (d < best)
                {
                    best = d;
                    halfWidth = Mathf.Lerp(flow.HalfWidth[i], flow.HalfWidth[i + 1], t);
                }
            }

            return best;
        }

        // ==================================================================
        // Where the ground actually is
        // ==================================================================
        /// <summary>
        /// The height of the ground <i>as drawn</i>: the same two triangles per cell the
        /// chunk mesher cuts, rather than the bilinear blend <see cref="GroundHeightAt"/>
        /// gives. On a curved cell the two differ by several centimetres, which is more
        /// than a drape is lifted by -- so a drape laid on the bilinear height dips under
        /// the ground in the middle of every cell and shows as a mesh of holes.
        /// </summary>
        private static float SurfaceHeightAt(float x, float z)
        {
            if (!_groundReady) return 0f;

            float fx = Mathf.Clamp((x - _groundMin) / _groundStep, 0f, _groundN - 1.001f);
            float fz = Mathf.Clamp((z - _groundMin) / _groundStep, 0f, _groundN - 1.001f);

            int i = (int)fx, j = (int)fz;
            float tx = fx - i, tz = fz - j;

            float h00 = _ground[i, j], h10 = _ground[i + 1, j];
            float h01 = _ground[i, j + 1], h11 = _ground[i + 1, j + 1];

            // BuildDuneChunk splits every cell on the (i, j) - (i + 1, j + 1) diagonal.
            return tz >= tx
                ? h00 + tz * (h01 - h00) + tx * (h11 - h01)
                : h00 + tx * (h10 - h00) + tz * (h11 - h10);
        }

        /// <summary>
        /// Whether a point is ground the drapes may lie on: inside the arena, and clear of
        /// the canyon -- the terrain there is a hole, and a drape laid over the hole's
        /// recorded heights is a sheet of rock hanging over the lava.
        /// </summary>
        private static bool Drapeable(float x, float z, float margin)
        {
            float half = _theme.arenaSize * 0.5f;
            if (Mathf.Abs(x) > half - margin || Mathf.Abs(z) > half - margin) return false;

            float clear = _theme.hazardWidth * 0.5f + GorgeLipOverlap + RimBandWidth * 0.6f + margin;
            return Mathf.Abs(x - GorgeCentreAt(z)) > clear;
        }

        /// <summary>
        /// A mesh laid on the ground from a grid of points, rows along and columns across.
        /// Each point is lifted by <paramref name="lift"/>, which is given the row's
        /// fraction along and the column's fraction across (each 0 at one edge, 1 at the
        /// other) so that the middle can stand proud while the rim sinks. Normals are the ground's own, so it is lit exactly as
        /// the terrain beside it is.
        ///
        /// Wound upward by measurement rather than by convention: the rows may run either
        /// way round, and a drape wound down is culled from every angle anybody sees it from.
        /// </summary>
        private static Mesh DrapeMesh(string name, Vector2[,] grid, System.Func<float, float, float> lift, float uvScale)
        {
            int rows = grid.GetLength(0), cols = grid.GetLength(1);

            var vertices = new Vector3[rows * cols];
            var normals = new Vector3[rows * cols];
            var uvs = new Vector2[rows * cols];

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var p = grid[r, c];
                    float along = rows > 1 ? r / (float)(rows - 1) : 0.5f;
                    float across = cols > 1 ? c / (float)(cols - 1) : 0.5f;

                    int index = r * cols + c;
                    vertices[index] = new Vector3(p.x, SurfaceHeightAt(p.x, p.y) + lift(along, across), p.y);
                    normals[index] = GroundNormalAt(p.x, p.y);
                    uvs[index] = p * uvScale;
                }

            // Measure which way round the quads go, and wind them all up.
            float facing = 0f;
            for (int r = 0; r < rows - 1; r++)
                for (int c = 0; c < cols - 1; c++)
                {
                    var a = vertices[r * cols + c];
                    var b = vertices[(r + 1) * cols + c];
                    var d = vertices[r * cols + c + 1];
                    facing += Vector3.Cross(b - a, d - a).y;
                }

            bool flip = facing < 0f;
            var triangles = new List<int>((rows - 1) * (cols - 1) * 6);

            for (int r = 0; r < rows - 1; r++)
                for (int c = 0; c < cols - 1; c++)
                {
                    int a = r * cols + c, b = (r + 1) * cols + c;
                    int d = r * cols + c + 1, e = (r + 1) * cols + c + 1;

                    if (!flip)
                    {
                        triangles.Add(a); triangles.Add(b); triangles.Add(e);
                        triangles.Add(a); triangles.Add(e); triangles.Add(d);
                    }
                    else
                    {
                        triangles.Add(a); triangles.Add(e); triangles.Add(b);
                        triangles.Add(a); triangles.Add(d); triangles.Add(e);
                    }
                }

            var mesh = new Mesh { name = name };
            if (vertices.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            return mesh;
        }

        /// <summary>Puts a drape in the scene: no collider, off the bake, off the map.</summary>
        private static GameObject Drape(Transform parent, string name, Mesh mesh, Material material, int layer)
        {
            var go = MeshObject(parent, name, mesh, material, Vector3.zero, Quaternion.identity,
                                Vector3.one, layer, "Untagged", collider: false);
            Hide(go);

            // It is the ground, and the ground already casts what the ground casts.
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            return go;
        }

        // ==================================================================
        // The surface
        // ==================================================================
        /// <summary>
        /// Everything lying on the plain. Runs after the vents are built, so the sulphur
        /// knows where they are. <paramref name="layer"/> is the layer the navigation
        /// bake ignores: none of this is ground anyone stands on.
        /// </summary>
        private static void BuildLavaFieldSurface(Transform root, int layer, float half)
        {
            if (!_groundReady) return;

            var group = new GameObject("LavaField").transform;
            group.SetParent(root, false);

            var rng = new System.Random(_theme.randomSeed * 104729 + 3);

            BuildFlowSurfaces(group, layer);
            BuildHotGround(group, layer, rng, half);
            BuildAshDrifts(group, layer, rng, half);
            BuildSulfur(group, layer, rng);
            BuildFissures(group, layer, rng, half);
            BuildRubbleFields(group, layer, rng, half);
        }

        /// <summary>
        /// The glossy skin of the newer flows, over the lobes the heightfield raised.
        ///
        /// Only the fresh ones: the old flows are the same basalt as the plain, which is
        /// right -- a flow a century old has weathered into the ground it covered, and the
        /// only thing left saying it was ever a flow is the step at its edge. Two kinds
        /// side by side is what tells the eye there was more than one eruption here.
        ///
        /// Cut into runs wherever the path leaves drapeable ground, so a flow that
        /// crosses the river's rim does not hang its skin over the canyon.
        /// </summary>
        private static void BuildFlowSurfaces(Transform parent, int layer)
        {
            int index = 0;

            foreach (var flow in _lavaFlows)
            {
                if (!flow.Fresh) continue;

                // Resample the path finer than it was planned, so the drape follows the
                // ground's curvature along its length rather than chording across it.
                var points = new List<Vector2>();
                var widths = new List<float>();

                for (int i = 0; i < flow.Path.Length - 1; i++)
                    for (int k = 0; k < 5; k++)
                    {
                        float t = k / 5f;
                        points.Add(Vector2.Lerp(flow.Path[i], flow.Path[i + 1], t));
                        widths.Add(Mathf.Lerp(flow.HalfWidth[i], flow.HalfWidth[i + 1], t));
                    }

                points.Add(flow.Path[flow.Path.Length - 1]);
                widths.Add(flow.HalfWidth[flow.HalfWidth.Length - 1]);

                int from = 0;

                for (int i = 0; i <= points.Count; i++)
                {
                    bool ok = i < points.Count && Drapeable(points[i].x, points[i].y, widths[i] + 4f);
                    if (ok) continue;

                    if (i - from >= 4)
                        FlowRun(parent, layer, flow, points, widths, from, i, index++);

                    from = i + 1;
                }
            }
        }

        private static void FlowRun(Transform parent, int layer, LavaFlowPlan flow, List<Vector2> points,
                                    List<float> widths, int from, int to, int index)
        {
            const int Columns = 9;
            int rows = to - from;
            var grid = new Vector2[rows, Columns];

            for (int r = 0; r < rows; r++)
            {
                int i = from + r;
                var ahead = points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(i - 1, 0)];
                var side = new Vector2(-ahead.y, ahead.x).normalized;

                // Out to the shoulder, not the toe: the skin covers the flow's top and the
                // start of its edge, and the slope below that is the old ground showing.
                // Ragged, because a flow's edge is never a clean offset of its centre line.
                float reach = widths[i] - flow.Edge * 0.45f;

                for (int c = 0; c < Columns; c++)
                {
                    float across = c / (float)(Columns - 1) * 2f - 1f;
                    float ragged = 1f + Fbm2(i * 0.3f, across * 2f + index * 7f, 6200 + index, 2) * 0.35f;
                    grid[r, c] = points[i] + side * (across * Mathf.Max(3f, reach) * ragged);
                }
            }

            // Proud in the middle by a hand's width, sunk at both rims.
            var mesh = DrapeMesh($"Flow_{index}", grid,
                                 (_, across) => Mathf.Lerp(-0.18f, 0.14f, Mathf.Sin(across * Mathf.PI)), 1f);
            Drape(parent, "FreshFlow", mesh, _flowMat, layer);
        }

        /// <summary>
        /// Ground that is still hot: patches of the plain with its cracks lit, warm at the
        /// edge and glowing at the heart, wherever they happen to fall.
        ///
        /// This is where the plain's glow lives, rather than in its texture -- see
        /// SampleBasalt for why a texture cannot do it. Each patch is the plain's own
        /// surface laid back over itself with a different emission, sharing its maps and
        /// its world-space UVs, so the cracks that light up are exactly the cracks already
        /// there and the patch's outline, where it sinks back under the ground, is
        /// invisible. What shows is the heat and nothing else.
        ///
        /// Two rings rather than one, because a patch of glowing cracks that stops dead at
        /// a line is a decal. Some fall at the fronts of the fresh flows, which are the
        /// newest rock on the map and the likeliest to still be hot; the rest anywhere.
        /// </summary>
        private static void BuildHotGround(Transform parent, int layer, System.Random rng, float half)
        {
            var sites = new List<Vector2>();

            foreach (var flow in _lavaFlows)
                if (flow.Fresh && rng.NextDouble() < 0.7)
                    sites.Add(flow.Path[flow.Path.Length - 1]);

            int loose = 22 + rng.Next(10);
            for (int k = 0; k < loose; k++)
                sites.Add(new Vector2(Rand(rng, -half, half), Rand(rng, -half, half)));

            int index = 0;

            foreach (var at in sites)
            {
                float radius = Rand(rng, 7f, 22f);
                if (!Drapeable(at.x, at.y, radius * 0.6f)) continue;

                // Not under a fresh flow's skin, which stands higher and would bury it.
                bool covered = false;
                foreach (var flow in _lavaFlows)
                    if (flow.Fresh && FlowLobeAt(flow, at.x, at.y) > flow.Thickness * 0.5f) { covered = true; break; }
                if (covered) continue;

                float stretch = Rand(rng, 1f, 2.4f), angle = Rand(rng, 0f, Mathf.PI);
                int seed = rng.Next(1, 9999);

                var warm = BlobMesh($"Warm_{index}", at, radius, seed, 0.07f, 1f, stretch, angle);
                Drape(parent, "WarmGround", warm, _warmGroundMat, layer);

                // The heart, off-centre inside the warm ring and higher, so it lies on top.
                var heart = at + new Vector2(Rand(rng, -0.25f, 0.25f), Rand(rng, -0.25f, 0.25f)) * radius;
                var hot = BlobMesh($"Hot_{index}", heart, radius * Rand(rng, 0.35f, 0.55f), seed + 1, 0.12f, 1f,
                                   stretch, angle + Rand(rng, -0.4f, 0.4f));
                Drape(parent, "HotGround", hot, _hotGroundMat, layer);

                index++;
            }
        }

        /// <summary>
        /// A ragged disc of ground cover: rings out from a centre, each point's radius
        /// pushed about by noise so no two are the same shape.
        /// </summary>
        private static Mesh BlobMesh(string name, Vector2 centre, float radius, int seed,
                                     float raise, float uvScale, float stretch, float angle)
        {
            const int Rings = 5, Spokes = 28;
            var grid = new Vector2[Rings + 1, Spokes + 1];

            var along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var across = new Vector2(-along.y, along.x);

            for (int r = 0; r <= Rings; r++)
                for (int s = 0; s <= Spokes; s++)
                {
                    float a = (s % Spokes) / (float)Spokes * Mathf.PI * 2f;
                    float edge = 1f + Fbm2(Mathf.Cos(a) * 1.6f + seed, Mathf.Sin(a) * 1.6f, seed, 3) * 0.7f;
                    float t = r / (float)Rings;

                    // The first ring is a point, which is what makes this a disc and not a ring.
                    float d = radius * edge * Mathf.Max(t, 0.001f);
                    grid[r, s] = centre + along * (Mathf.Cos(a) * d * stretch) + across * (Mathf.Sin(a) * d);
                }

            // Rows are the rings here, so "along" runs from the centre (0) to the rim (1).
            return DrapeMesh(name, grid, (ring, _) => Mathf.Lerp(raise, -0.14f, ring * ring), uvScale);
        }

        /// <summary>
        /// Drifted ash, in the low ground. Placed by asking where the low ground is --
        /// a handful of candidates, keep the lowest -- because ash that lies on a crest
        /// is ash that was put there.
        /// </summary>
        private static void BuildAshDrifts(Transform parent, int layer, System.Random rng, float half)
        {
            int drifts = 30 + rng.Next(10);

            for (int i = 0; i < drifts; i++)
            {
                Vector2 best = Vector2.zero;
                float lowest = float.MaxValue;

                for (int k = 0; k < 6; k++)
                {
                    var p = new Vector2(Rand(rng, -half, half), Rand(rng, -half, half));
                    if (!Drapeable(p.x, p.y, 20f)) continue;

                    float h = SurfaceHeightAt(p.x, p.y);
                    if (h < lowest) { lowest = h; best = p; }
                }

                if (lowest == float.MaxValue) continue;

                float radius = Rand(rng, 6f, 18f);
                var mesh = BlobMesh($"Ash_{i}", best, radius, rng.Next(1, 9999), 0.1f, 1f,
                                    Rand(rng, 1.2f, 2.4f), Rand(rng, 0f, Mathf.PI));
                Drape(parent, "Ash", mesh, _ashMat, layer);
            }
        }

        /// <summary>
        /// Sulphur round the vents, where the gas comes out -- the one thing on this map
        /// that is not a shade of red or black, and so the thing that says "vent" before
        /// the smoke does.
        /// </summary>
        private static void BuildSulfur(Transform parent, int layer, System.Random rng)
        {
            int index = 0;

            foreach (var vent in _ventSites)
            {
                int stains = 1 + rng.Next(3);

                for (int k = 0; k < stains; k++)
                {
                    float angle = Rand(rng, 0f, Mathf.PI * 2f);
                    float away = vent.z * Rand(rng, 0.5f, 0.95f);
                    var at = new Vector2(vent.x + Mathf.Cos(angle) * away, vent.y + Mathf.Sin(angle) * away);

                    if (!Drapeable(at.x, at.y, 6f)) continue;

                    var mesh = BlobMesh($"Sulfur_{index++}", at, Rand(rng, 1.6f, 4f), rng.Next(1, 9999),
                                        0.06f, 1f, Rand(rng, 1f, 1.8f), angle);
                    Drape(parent, "Sulfur", mesh, _sulfurMat, layer);
                }
            }
        }

        /// <summary>
        /// Fissures: long jagged cracks in the plain with the melt showing at the bottom,
        /// and crust heaved up along both lips.
        ///
        /// Cracks in the ground are the one thing the basalt's own glowing joints cannot
        /// do, because those are the size of a paving slab and these run for tens of
        /// metres in one direction. They are decoration and never lethal -- the lethal
        /// things in this arena are the river and the vents' throats, and a player has to
        /// be able to tell the two apart at a glance. Hence thin, and hence lipped.
        /// </summary>
        private static void BuildFissures(Transform parent, int layer, System.Random rng, float half)
        {
            int fissures = 16 + rng.Next(8);

            for (int f = 0; f < fissures; f++)
            {
                var at = new Vector2(Rand(rng, -half * 0.9f, half * 0.9f), Rand(rng, -half * 0.9f, half * 0.9f));
                if (at.magnitude < 20f) continue;

                float heading = Rand(rng, 0f, Mathf.PI * 2f);
                int segments = 5 + rng.Next(12);
                float width = Rand(rng, 0.25f, 0.8f);

                var points = new List<Vector2> { at };

                for (int s = 0; s < segments; s++)
                {
                    // Jagged: a crack changes direction abruptly, then keeps on.
                    heading += Rand(rng, -0.55f, 0.55f);
                    var next = points[points.Count - 1]
                             + new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * Rand(rng, 2.5f, 6f);

                    if (!Drapeable(next.x, next.y, 4f)) break;
                    points.Add(next);
                }

                if (points.Count < 3) continue;

                // Subdivided so a long straight segment still follows the ground.
                var fine = new List<Vector2>();
                for (int i = 0; i < points.Count - 1; i++)
                    for (int k = 0; k < 3; k++)
                        fine.Add(Vector2.Lerp(points[i], points[i + 1], k / 3f));
                fine.Add(points[points.Count - 1]);

                int rows = fine.Count;
                var core = new Vector2[rows, 2];
                var lips = new Vector2[rows, 6];

                for (int r = 0; r < rows; r++)
                {
                    var ahead = fine[Mathf.Min(r + 1, rows - 1)] - fine[Mathf.Max(r - 1, 0)];
                    var side = new Vector2(-ahead.y, ahead.x).normalized;

                    // Pinched shut at both ends, widest somewhere along.
                    float t = r / (float)(rows - 1);
                    float w = width * Mathf.Sin(t * Mathf.PI) * (0.7f + 0.6f * (Fbm2(r * 0.4f, f, 6300 + f, 2) + 0.5f));
                    w = Mathf.Max(w, 0.04f);

                    core[r, 0] = fine[r] - side * w * 0.5f;
                    core[r, 1] = fine[r] + side * w * 0.5f;

                    float lip = w * 0.8f + 0.35f;
                    lips[r, 0] = fine[r] - side * (w * 0.5f + lip);
                    lips[r, 1] = fine[r] - side * (w * 0.5f + lip * 0.35f);
                    lips[r, 2] = fine[r] - side * (w * 0.5f);
                    lips[r, 3] = fine[r] + side * (w * 0.5f);
                    lips[r, 4] = fine[r] + side * (w * 0.5f + lip * 0.35f);
                    lips[r, 5] = fine[r] + side * (w * 0.5f + lip);
                }

                // The glow sits a little above the ground so the ground cannot hide it, and
                // the lips stand higher still, so from the side a fissure is a dark ridge
                // with light coming out of it.
                var glow = DrapeMesh($"Fissure_{f}", core, (_, _) => 0.05f, 0.8f);
                Drape(parent, "Fissure", glow, _lavaMat, layer);

                var rim = DrapeMesh($"FissureLip_{f}", lips, (_, across) =>
                {
                    // 0 and 1 are the outer edges, the middle pair the crack's own edges.
                    float fromCrack = Mathf.Abs(across - 0.5f) * 2f;
                    return fromCrack < 0.3f ? 0.26f : Mathf.Lerp(0.26f, -0.12f, (fromCrack - 0.3f) / 0.7f);
                }, 1.2f);

                // The lip mesh spans the crack too; its middle quad would roof the glow over,
                // so it is dropped out of the triangle list below.
                OpenFissureLip(rim);
                Drape(parent, "FissureLip", rim, _crustMat, layer);
            }
        }

        /// <summary>Removes the column of quads between the two crack edges of a lip mesh.</summary>
        private static void OpenFissureLip(Mesh mesh)
        {
            const int Columns = 6;
            var triangles = mesh.triangles;
            var kept = new List<int>(triangles.Length);

            for (int t = 0; t < triangles.Length; t += 3)
            {
                // Every vertex of a triangle in the middle column is in column 2 or 3.
                bool middle = true;
                for (int k = 0; k < 3; k++)
                {
                    int column = triangles[t + k] % Columns;
                    if (column != 2 && column != 3) { middle = false; break; }
                }

                if (middle) continue;
                kept.Add(triangles[t]); kept.Add(triangles[t + 1]); kept.Add(triangles[t + 2]);
            }

            mesh.SetTriangles(kept, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// Rubble and broken crust, in fields rather than singly: clinker at the toes of
        /// the flows, where a real a'a flow sheds it, and patches of heaved-up crust and
        /// scattered stones out on the plain.
        ///
        /// Welded, one mesh to a field, because a field is forty stones and forty objects
        /// is forty draw calls for something nobody looks at twice. Knee-high at most and
        /// with no collider, the same as the scrub on the desert: a stone that stops a
        /// player is cover and has to be placed as cover; these are texture with depth.
        /// </summary>
        private static void BuildRubbleFields(Transform parent, int layer, System.Random rng, float half)
        {
            int index = 0;

            // ---- at the flows' edges ----
            foreach (var flow in _lavaFlows)
            {
                int fields = 2 + rng.Next(4);

                for (int k = 0; k < fields; k++)
                {
                    int i = rng.Next(flow.Path.Length);
                    var p = flow.Path[i];

                    var ahead = flow.Path[Mathf.Min(i + 1, flow.Path.Length - 1)] - flow.Path[Mathf.Max(i - 1, 0)];
                    var side = new Vector2(-ahead.y, ahead.x).normalized * (rng.Next(2) == 0 ? -1f : 1f);

                    // At the front if it is the last point, at a side otherwise: both are
                    // where a flow's crust breaks off and tumbles.
                    var at = i == flow.Path.Length - 1
                        ? p + ahead.normalized * (flow.HalfWidth[i] - flow.Edge * 0.5f)
                        : p + side * (flow.HalfWidth[i] - flow.Edge * 0.5f);

                    if (!Drapeable(at.x, at.y, 6f)) continue;
                    RubbleField(parent, layer, rng, at, Rand(rng, 5f, 11f), index++, slabs: false);
                }
            }

            // ---- out on the plain ----
            int loose = 44 + rng.Next(16);

            for (int k = 0; k < loose; k++)
            {
                var at = new Vector2(Rand(rng, -half * 0.92f, half * 0.92f), Rand(rng, -half * 0.92f, half * 0.92f));
                if (at.magnitude < 16f || !Drapeable(at.x, at.y, 6f)) continue;

                RubbleField(parent, layer, rng, at, Rand(rng, 3f, 9f), index++, slabs: rng.NextDouble() < 0.45);
            }
        }

        /// <summary>
        /// A stone for a rubble field: a bare icosahedron with its corners pushed about.
        ///
        /// <b>Not <see cref="BoulderMesh"/>.</b> That is a twice-subdivided sphere, 320
        /// faces and nearly a thousand vertices once flat-shaded, which is right for a rock
        /// somebody crouches behind and absurd for a pebble. Welded into the fields a couple
        /// of thousand times it came to two and a half million vertices, saved into the
        /// scene: the arena went from 39 MB to 117 MB, GitHub refused the push, and the web
        /// build would have carried every byte of it. Twenty faces is a stone at this size.
        /// </summary>
        private static Mesh PebbleMesh(int seed) => Pooled($"pebble_{seed}", () =>
        {
            var (points, faces) = IcoSphere(0);
            var pushed = new Vector3[points.Count];

            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i].normalized;
                float bump = 1f + Fbm3(p * 1.7f, seed * 31 + 5, 2) * 0.7f;
                pushed[i] = new Vector3(p.x * bump, p.y * bump * 0.75f, p.z * bump);
            }

            var build = new MeshBuild { UVScale = 0.5f };
            foreach (var f in faces) build.Tri(pushed[f.x], pushed[f.y], pushed[f.z]);

            return build.ToMesh($"Pebble_{seed}");
        });

        private static void RubbleField(Transform parent, int layer, System.Random rng, Vector2 centre,
                                         float radius, int index, bool slabs)
        {
            var build = new MeshBuild { UVScale = 0.5f };
            int pieces = slabs ? 4 + rng.Next(6) : 14 + rng.Next(22);

            for (int p = 0; p < pieces; p++)
            {
                // Denser in the middle, thinning out: a field, not a scatter.
                float r = radius * Mathf.Sqrt((float)rng.NextDouble()) * ((float)rng.NextDouble() * 0.5f + 0.5f);
                float a = Rand(rng, 0f, Mathf.PI * 2f);
                float x = centre.x + Mathf.Cos(a) * r, z = centre.y + Mathf.Sin(a) * r;

                if (slabs)
                {
                    // A plate of crust, broken off and heaved up on one edge.
                    float w = Rand(rng, 1f, 3.2f), d = Rand(rng, 0.8f, 2.4f), t = Rand(rng, 0.14f, 0.3f);
                    float pitch = Rand(rng, 8f, 32f);

                    // How high the raised edge ends up, so it can be held under knee height.
                    float rise = Mathf.Sin(pitch * Mathf.Deg2Rad) * d * 0.5f;
                    if (rise > 0.4f) pitch = Mathf.Asin(0.4f / (d * 0.5f)) * Mathf.Rad2Deg;

                    var turn = GroundAlignedRotation(x, z, Rand(rng, 0f, 360f))
                             * Quaternion.Euler(pitch, 0f, Rand(rng, -8f, 8f));

                    var at = new Vector3(x, SurfaceHeightAt(x, z) + t * 0.2f, z);
                    build.Box(at, new Vector3(w, t, d), turn);
                }
                else
                {
                    float size = Rand(rng, 0.2f, 0.75f);
                    var mesh = PebbleMesh(1 + rng.Next(24));

                    // Unit icosphere, so scale is a radius; sunk by a third of its height.
                    var scale = new Vector3(size, size * Rand(rng, 0.5f, 0.9f), size * Rand(rng, 0.7f, 1.2f)) * 0.5f;
                    var at = new Vector3(x, SurfaceHeightAt(x, z) + scale.y * 0.3f, z);

                    AppendMesh(build, mesh, at, Quaternion.Euler(Rand(rng, -20f, 20f), Rand(rng, 0f, 360f),
                                                                 Rand(rng, -20f, 20f)), scale);
                }
            }

            if (build.Vertices.Count == 0) return;

            // Named as a field, not a rock: VerifyZone checks the winding of every "Rubble" by
            // counting faces that point away from the mesh's centre, and forty stones welded
            // together have a centre in the empty ground between them.
            var go = MeshObject(parent, slabs ? "CrustField" : "RubbleField", build.ToMesh($"RubbleField_{index}"),
                                slabs ? _crustMat : _rockDarkMat, Vector3.zero, Quaternion.identity,
                                Vector3.one, layer, "Untagged", collider: false);
            Hide(go);
        }
    }
}
#endif
