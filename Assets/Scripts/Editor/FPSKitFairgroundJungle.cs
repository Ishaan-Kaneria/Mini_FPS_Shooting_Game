#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The overgrown part of the Abandoned Fairground: a jungle that has been let into a
    /// park. Broadleaf trees with buttressed trunks, palms, ferns, hanging vines and
    /// tufts, welded a grove at a time.
    ///
    /// <b>Trunks block, foliage does not.</b> A trunk is a collider, because a tree is
    /// cover and a player has to be able to stand behind one. Crowns, fronds and
    /// undergrowth have none and are held off the navigation bake: a flat-topped lump of
    /// leaf is exactly the kind of surface that bakes as walkable ground forty metres in
    /// the air, and an enemy spawned there is an enemy nobody can reach.
    ///
    /// <b>Where a tree may stand is a question the whole fairground answers.</b> Nothing
    /// grows on a ride's pad, in the gate plaza, inside the hall, on a walkway or in a
    /// sinkhole's bowl. Those are all recorded as keep-out circles and routes while the
    /// park is planned and built, and the jungle runs <i>last</i> so it fills what is left
    /// instead of pushing the things the player navigates by out of the way.
    ///
    /// <b>Trunks are spaced.</b> No two stand closer than 2.8m. A navigation agent is a
    /// metre wide, so that leaves every gap in the densest grove passable; without the rule
    /// a thick band of forest was pockets of ground walled in on all sides, which is what
    /// the reachability check reports as stranded.
    /// </summary>
    public partial class FPSKitSceneBuilder
    {
        // ------------------------------------------------------------------
        // Keep-outs, shared by everything the fairground adds
        // ------------------------------------------------------------------
        /// <summary>Circles nothing may grow or be placed in: x, z, radius.</summary>
        private static readonly List<Vector3> _fgKeep = new List<Vector3>();

        private struct FgRoute
        {
            public List<Vector2> Points;
            public float Width;
        }

        private static readonly List<FgRoute> _fgRoutes = new List<FgRoute>();
        private static readonly List<Vector2> _fgTrunks = new List<Vector2>();

        /// <summary>Clears everything the fairground keeps between planning and building.
        /// Called at the top of the per-arena build, never in the branch that writes it.</summary>
        private static void ResetFairground()
        {
            _fgKeep.Clear();
            _fgRoutes.Clear();
            _fgTrunks.Clear();
            _hallBlock.Clear();
            ResetFairgroundPlan();
            _hasHall = _hasPond = _hasBumper = false;
        }

        private static void Keep(float x, float z, float radius) => _fgKeep.Add(new Vector3(x, z, radius));

        private static bool KeepClear(Vector2 p, float radius)
        {
            foreach (var k in _fgKeep)
            {
                float minimum = k.z + radius;
                float dx = k.x - p.x, dz = k.y - p.y;
                if (dx * dx + dz * dz < minimum * minimum) return false;
            }

            return true;
        }

        private static bool OffSinks(Vector2 p, float radius)
        {
            foreach (var sink in _sinks)
            {
                float minimum = sink.Radius * 1.2f + radius;
                if ((sink.Centre - p).sqrMagnitude < minimum * minimum) return false;
            }

            return true;
        }

        /// <summary>Metres between a point and the nearest edge of any walkway.</summary>
        private static float RouteClearance(Vector2 p)
        {
            float best = float.MaxValue;

            foreach (var route in _fgRoutes)
            {
                var points = route.Points;
                for (int i = 0; i < points.Count - 1; i++)
                    best = Mathf.Min(best, DistanceToSegment(p, points[i], points[i + 1]) - route.Width * 0.5f);
            }

            return best;
        }

        /// <summary>How steep the ground is round a point, as rise over run.</summary>
        private static float SlopeAt(Vector2 p, float step = 2f)
        {
            float x0 = GroundHeightAt(p.x - step, p.y), x1 = GroundHeightAt(p.x + step, p.y);
            float z0 = GroundHeightAt(p.x, p.y - step), z1 = GroundHeightAt(p.x, p.y + step);
            return Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(z1 - z0)) / (step * 2f);
        }

        // ------------------------------------------------------------------
        // The jungle
        // ------------------------------------------------------------------
        private sealed class Grove
        {
            public readonly MeshBuild Trunks = new MeshBuild { UVScale = 0.5f };
            public readonly MeshBuild LeafA = new MeshBuild { UVScale = 0.5f };
            public readonly MeshBuild LeafB = new MeshBuild { UVScale = 0.5f };
            public readonly MeshBuild Under = new MeshBuild { UVScale = 0.5f };
        }

        private const float GroveCell = 60f;
        private const float TrunkSpacing = 2.8f;

        private static void BuildJungle(Transform root, int layer, int backdrop, float half)
        {
            var group = new GameObject("Jungle").transform;
            group.SetParent(root, false);

            var rng = new System.Random(_theme.randomSeed * 104729 + 11);
            var groves = new Dictionary<Vector2Int, Grove>();

            Grove GroveAt(Vector2 p)
            {
                var key = new Vector2Int(Mathf.FloorToInt(p.x / GroveCell), Mathf.FloorToInt(p.y / GroveCell));
                if (!groves.TryGetValue(key, out var grove)) grove = groves[key] = new Grove();
                return grove;
            }

            int planted = 0;

            // ---- the avenue: ornamental trees either side of every walkway ----
            foreach (var route in _fgRoutes)
            {
                if (route.Width < 4f) continue;

                float walked = 0f, nextAt = Rand(rng, 6f, 12f);
                int side = 1;
                var points = route.Points;

                for (int i = 0; i < points.Count - 1; i++)
                {
                    var a = points[i];
                    var b = points[i + 1];
                    float length = Vector2.Distance(a, b);
                    if (length < 0.01f) continue;
                    var along = (b - a) / length;
                    var across = new Vector2(-along.y, along.x);

                    while (walked + length > nextAt)
                    {
                        var p = a + along * (nextAt - walked) + across * side * (route.Width * 0.5f + 2.6f);
                        side = -side;
                        nextAt += Rand(rng, 11f, 17f);

                        if (!TreeSpotOk(p, 0.6f, -1f)) continue;

                        PlantTree(GroveAt(p), rng, p, rng.NextDouble() < 0.45, avenue: true);
                        planted++;
                    }

                    walked += length;
                }
            }

            // ---- the forest: bands of it, thickest towards the edge ----
            var candidates = new List<(Vector2 p, float weight)>();
            float expected = 0f;
            float edgeStart = half * 0.5f, edgeEnd = half - BermToe - 6f;
            int seed = _theme.randomSeed;

            for (float x = -half + 16f; x <= half - 16f; x += 4.4f)
                for (float z = -half + 16f; z <= half - 16f; z += 4.4f)
                {
                    var p = new Vector2(x + Rand(rng, -1.8f, 1.8f), z + Rand(rng, -1.8f, 1.8f));

                    float bands = Fbm2(p.x * 0.012f, p.y * 0.012f, seed + 900, 3) + 0.5f;
                    float clump = Fbm2(p.x * 0.05f, p.y * 0.05f, seed + 940, 2) + 0.5f;
                    float edge = Mathf.InverseLerp(edgeStart, edgeEnd, Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)));

                    float weight = Mathf.Clamp01((bands - 0.5f) * 2.8f) * (0.4f + clump * 0.9f) + edge * 0.7f * clump;
                    if (weight <= 0.02f) continue;

                    candidates.Add((p, weight));
                    expected += weight;
                }

            // Scale to a budget rather than hoping the noise lands on a sensible count: a
            // park with eleven hundred trees is a different game to one with five hundred.
            float budget = 300f;
            float scale = expected > 0f ? Mathf.Min(1f, budget / expected) : 0f;

            foreach (var (p, weight) in candidates)
            {
                if (rng.NextDouble() > weight * scale) continue;
                if (!TreeSpotOk(p, 0.6f, 2.4f)) continue;

                PlantTree(GroveAt(p), rng, p, rng.NextDouble() < 0.18, avenue: false);
                planted++;
            }

            // ---- ground cover, in the open as well as under the trees ----
            int cover = 0;
            for (float x = -half + 14f; x <= half - 14f; x += 10f)
                for (float z = -half + 14f; z <= half - 14f; z += 10f)
                {
                    if (rng.NextDouble() > 0.42) continue;

                    var p = new Vector2(x + Rand(rng, -4f, 4f), z + Rand(rng, -4f, 4f));
                    if (!KeepClear(p, 0.4f) || !OffSinks(p, 0.5f) || RouteClearance(p) < 0.6f) continue;

                    var foot = new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y);
                    var under = GroveAt(p).Under;
                    int s = rng.Next(1, 99999);

                    Tuft(under, foot, Rand(rng, 0.8f, 1.5f), s);
                    Tuft(under, foot + new Vector3(Rand(rng, -0.8f, 0.8f), 0f, Rand(rng, -0.8f, 0.8f)), Rand(rng, 0.6f, 1.2f), s + 1);
                    if (rng.NextDouble() < 0.3) Fern(under, foot + new Vector3(0.6f, 0f, 0.4f), Rand(rng, 1.0f, 1.7f), s + 2);
                    cover++;
                }

            int meshes = 0;
            foreach (var grove in groves.Values)
            {
                if (grove.Trunks.Triangles.Count > 0)
                {
                    var trunks = MeshObject(group, "Trunks", ToMesh(grove.Trunks, DenseKey("trunks")), _fgBark,
                                            Vector3.zero, Quaternion.identity, Vector3.one, layer, "Wood");
                    Hide(trunks);
                    meshes++;
                }

                EmitFoliage(group, backdrop, "CrownsA", grove.LeafA, _fgLeafA);
                EmitFoliage(group, backdrop, "CrownsB", grove.LeafB, _fgLeafB);
                EmitFoliage(group, backdrop, "Undergrowth", grove.Under, _fgFern);
            }

            Debug.Log($"[FPSKit] fairground jungle: {planted} tree(s), {cover} ground-cover clump(s), {meshes} grove(s).");
        }

        /// <summary>
        /// Foliage as the world sees it: drawn, never collided with, never walked on and
        /// kept off the minimap. On the backdrop layer for the same reason the desert's
        /// scrub is -- the navigation bake is not looking there.
        /// </summary>
        private static void EmitFoliage(Transform parent, int layer, string name, MeshBuild build, Material material)
        {
            if (build.Triangles.Count == 0) return;

            var go = MeshObject(parent, name, ToMesh(build, DenseKey(name.ToLowerInvariant())), material,
                                Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false);
            NoStanding(go);
            Hide(go);
        }

        private static bool TreeSpotOk(Vector2 p, float radius, float routeMargin)
        {
            float limit = _theme.arenaSize * 0.5f - BermToe - 4f;
            if (Mathf.Abs(p.x) > limit || Mathf.Abs(p.y) > limit) return false;
            if (!KeepClear(p, radius) || !OffSinks(p, radius + 1.5f)) return false;
            if (routeMargin > 0f && RouteClearance(p) < routeMargin + radius) return false;
            if (SlopeAt(p) > 0.42f) return false;

            foreach (var t in _fgTrunks)
                if ((t - p).sqrMagnitude < TrunkSpacing * TrunkSpacing) return false;

            // Last, because it is the one that asks the physics scene: anything solid that
            // is already standing here -- a stall, a fence, a lamp -- is reason enough.
            return SpotEmpty(p, radius + 0.5f, 0.3f, 6f);
        }

        private static void PlantTree(Grove grove, System.Random rng, Vector2 p, bool palm, bool avenue)
        {
            _fgTrunks.Add(p);

            // Sunk a little, so a trunk standing on a slope does not hang over the downhill side.
            var foot = new Vector3(p.x, GroundHeightAt(p.x, p.y) - 0.3f, p.y);

            if (palm) JunglePalm(grove, rng, foot);
            else JungleTree(grove, rng, foot, avenue ? 0.8f : 1f);

            // What grows round the base: bushes and ferns, thicker in the wild.
            int s = rng.Next(1, 99999);
            float undergrowth = avenue ? 0.25f : 0.8f;

            if (rng.NextDouble() < undergrowth)
            {
                float a = Rand(rng, 0f, 6.283f), d = Rand(rng, 1.2f, 2.4f);
                Bush(grove.Under, foot + new Vector3(Mathf.Cos(a) * d, 0.3f, Mathf.Sin(a) * d), Rand(rng, 0.8f, 1.5f), s);
            }

            if (rng.NextDouble() < undergrowth)
            {
                float a = Rand(rng, 0f, 6.283f), d = Rand(rng, 1.0f, 2.2f);
                Fern(grove.Under, foot + new Vector3(Mathf.Cos(a) * d, 0.3f, Mathf.Sin(a) * d), Rand(rng, 1.1f, 1.9f), s + 3);
            }
        }

        /// <summary>
        /// A broadleaf jungle tree: a flared, leaning trunk with buttress roots, and a crown
        /// of four to six ragged masses of leaf on short branches. A single dome reads as a
        /// lollipop; several masses at different heights read as a canopy.
        /// </summary>
        private static void JungleTree(Grove grove, System.Random rng, Vector3 foot, float sizeScale)
        {
            float h = Rand(rng, 9f, 16f) * sizeScale;
            var up = Vector3.up;
            var leanDir = new Vector3(Rand(rng, -1f, 1f), 0f, Rand(rng, -1f, 1f)).normalized;
            var lean = leanDir * h * Rand(rng, 0.02f, 0.1f);

            float r0 = Rand(rng, 0.38f, 0.62f) * Mathf.Lerp(0.8f, 1f, sizeScale);
            var mid = foot + up * h * 0.5f + lean * 0.4f;
            var top = foot + up * h * 0.8f + lean;

            grove.Trunks.Tube(foot - up * 0.2f, mid, r0 * 1.3f, r0 * 0.82f, 7);
            grove.Trunks.Tube(mid, top, r0 * 0.82f, r0 * 0.5f, 7);

            // Buttress roots: three flared ribs where the trunk meets the ground.
            int fins = 3 + rng.Next(2);
            float finTurn = Rand(rng, 0f, 6.283f);
            for (int i = 0; i < fins; i++)
            {
                float a = finTurn + i * Mathf.PI * 2f / fins + Rand(rng, -0.3f, 0.3f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                grove.Trunks.Tube(foot + dir * r0 * 2.1f - up * 0.15f, foot + dir * r0 * 0.5f + up * h * 0.2f,
                                  r0 * 0.34f, r0 * 0.12f, 4);
            }

            var leaf = rng.NextDouble() < 0.55 ? grove.LeafA : grove.LeafB;
            var crown = top + up * h * 0.06f;
            float spread = h / 14f;
            int blobs = 4 + rng.Next(3);

            for (int b = 0; b < blobs; b++)
            {
                float a = b * Mathf.PI * 2f / blobs + Rand(rng, -0.5f, 0.5f);
                float off = Rand(rng, 1.3f, 3.2f) * spread;
                var at = crown + new Vector3(Mathf.Cos(a) * off, Rand(rng, -2.2f, 2.4f) * spread, Mathf.Sin(a) * off);
                float radius = Rand(rng, 2.4f, 3.8f) * spread + 0.9f;

                // The branch that holds it up, then the mass of leaf.
                grove.Trunks.Tube(top - up * Rand(rng, 0.2f, 1.6f), at - up * radius * 0.2f, 0.16f, 0.07f, 4);
                LeafBlob(leaf, at, radius, rng.Next(1, 99999));
            }

            LeafBlob(leaf, crown + up * 2.2f * spread, Rand(rng, 2.8f, 3.8f) * spread + 0.8f, rng.Next(1, 99999));

            // Hanging vines, on about one tree in three.
            if (rng.NextDouble() < 0.34)
            {
                int vines = 2 + rng.Next(3);
                for (int v = 0; v < vines; v++)
                {
                    float a = Rand(rng, 0f, 6.283f);
                    var from = crown + new Vector3(Mathf.Cos(a), -0.8f * spread, Mathf.Sin(a)) * Rand(rng, 1.8f, 3.4f) * spread;
                    Vine(grove.Under, from, Rand(rng, 3.5f, 7.5f), rng.Next(1, 99999));
                }
            }
        }

        /// <summary>
        /// A ragged mass of leaf: three rings and an apex, flat-shaded, rounded underneath.
        /// Two rings and a flat base read as a plate; the third ring and a tucked-in
        /// underside are what make it a clump of foliage and not a parasol.
        /// </summary>
        private static void LeafBlob(MeshBuild build, Vector3 centre, float radius, int seed)
        {
            const int n = 7;
            var low = new Vector3[n];
            var mid = new Vector3[n];
            var high = new Vector3[n];
            float twist = Hash01(seed, 1) * Mathf.PI * 2f;

            for (int i = 0; i < n; i++)
            {
                float a = twist + i * Mathf.PI * 2f / n;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                float j0 = 0.7f + 0.5f * Hash01(seed, 10 + i);
                float j1 = 0.85f + 0.4f * Hash01(seed, 30 + i);
                float j2 = 0.65f + 0.4f * Hash01(seed, 50 + i);

                low[i] = centre + new Vector3(c * radius * 0.5f * j0, -radius * 0.42f * (0.8f + 0.4f * Hash01(seed, 70 + i)), s * radius * 0.5f * j0);
                mid[i] = centre + new Vector3(c * radius * j1, -radius * 0.04f, s * radius * j1);
                high[i] = centre + new Vector3(c * radius * 0.78f * j2, radius * 0.4f * (0.8f + 0.4f * Hash01(seed, 90 + i)), s * radius * 0.78f * j2);
            }

            var apex = centre + Vector3.up * radius * (0.68f + 0.25f * Hash01(seed, 3));
            var under = centre + Vector3.down * radius * 0.52f;

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                build.Quad(low[i], mid[i], mid[j], low[j]);
                build.Quad(mid[i], high[i], high[j], mid[j]);
                build.Tri(high[i], apex, high[j]);
                build.Tri(under, low[i], low[j]);
            }
        }

        private static void JunglePalm(Grove grove, System.Random rng, Vector3 foot, float heightScale = 1f)
        {
            float h = Rand(rng, 8f, 13f) * heightScale;
            var up = Vector3.up;
            var bendDir = new Vector3(Rand(rng, -1f, 1f), 0f, Rand(rng, -1f, 1f)).normalized;
            var bend = bendDir * Rand(rng, 0.8f, 2.8f);

            var mid = foot + up * h * 0.5f + bend * 0.3f;
            var top = foot + up * h + bend;

            grove.Trunks.Tube(foot - up * 0.2f, mid, 0.34f, 0.23f, 6);
            grove.Trunks.Tube(mid, top, 0.23f, 0.17f, 6);

            int fronds = 8 + rng.Next(3);
            float turn = Rand(rng, 0f, 6.283f);
            for (int i = 0; i < fronds; i++)
            {
                float a = turn + i * Mathf.PI * 2f / fronds + Rand(rng, -0.2f, 0.2f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Frond(grove.LeafB, top, dir, Rand(rng, 3.4f, 5.2f) * Mathf.Lerp(0.7f, 1f, heightScale), 0.5f, 0.42f, 0.3f);
            }
        }

        /// <summary>
        /// A leaf: a strip that rises from a point, arches over and droops, wide in the
        /// middle. Drawn from both sides because a leaf has no inside.
        /// </summary>
        private static void Frond(MeshBuild build, Vector3 from, Vector3 dir, float length,
                                  float halfWidth, float rise, float droop)
        {
            var side = Vector3.Cross(Vector3.up, dir).normalized;

            var spine = new[]
            {
                from,
                from + dir * length * 0.35f + Vector3.up * length * rise * 0.7f,
                from + dir * length * 0.7f + Vector3.up * length * rise * 0.55f,
                from + dir * length + Vector3.up * length * (rise * 0.3f - droop)
            };
            var width = new[] { 0.04f, 1f, 0.8f, 0.06f };

            for (int k = 0; k < spine.Length - 1; k++)
            {
                var a0 = spine[k] - side * halfWidth * width[k];
                var a1 = spine[k] + side * halfWidth * width[k];
                var b1 = spine[k + 1] + side * halfWidth * width[k + 1];
                var b0 = spine[k + 1] - side * halfWidth * width[k + 1];

                build.Quad(a0, a1, b1, b0);
                build.Quad(b0, b1, a1, a0);
            }
        }

        /// <summary>A fern: a rosette of arching fronds.</summary>
        private static void Fern(MeshBuild build, Vector3 at, float length, int seed)
        {
            int leaves = 6 + (int)(Hash01(seed, 5) * 3f);
            for (int i = 0; i < leaves; i++)
            {
                float a = (i + Hash01(seed, 10 + i) * 0.6f) * Mathf.PI * 2f / leaves;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Frond(build, at, dir, length * (0.75f + Hash01(seed, 30 + i) * 0.5f), 0.14f, 0.62f, 0.4f);
            }
        }

        /// <summary>A hanging vine: a thin strip that sways as it falls.</summary>
        private static void Vine(MeshBuild build, Vector3 from, float length, int seed)
        {
            const int segments = 3;
            var sway = new Vector3(Hash01(seed, 1) - 0.5f, 0f, Hash01(seed, 2) - 0.5f) * 1.2f;
            var side = new Vector3(Mathf.Cos(Hash01(seed, 3) * 6.283f), 0f, Mathf.Sin(Hash01(seed, 3) * 6.283f)) * 0.09f;

            var previous = from;
            for (int k = 1; k <= segments; k++)
            {
                float t = k / (float)segments;
                var next = from + Vector3.down * length * t + sway * Mathf.Sin(t * Mathf.PI) * 0.8f;
                float w = k == segments ? 0.3f : 1f;

                build.Quad(previous - side, previous + side, next + side * w, next - side * w);
                build.Quad(next - side * w, next + side * w, previous + side, previous - side);
                previous = next;
            }
        }
    }
}
#endif
