#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The geometry the builder draws with when a cube will not do.
    ///
    /// Everything the kit placed before this file was a <c>PrimitiveType.Cube</c> with a
    /// flat colour on it, which is the right first move -- a box is instantly readable,
    /// it costs nothing, and a level made of boxes is a level you can tune. What it
    /// cannot be is a desert. Sand does not come in rectangles, a boulder made of four
    /// stacked cubes reads as four stacked cubes from every angle, and a river bank
    /// built out of slabs looks like a swimming pool however it is coloured.
    ///
    /// So this is a small mesh library: enough to build a heightfield, a rock, a trunk,
    /// a palm frond and a run of fence, and no more. Three rules hold it together:
    ///
    ///   * <b>Meshes are built, not imported.</b> There is no art pack in this repo for
    ///     a desert, and a kit that needs one is a kit that cannot regenerate itself.
    ///     Every shape here comes out of a seed, so a rebuild reproduces it exactly.
    ///   * <b>Rock is faceted on purpose.</b> Flat normals on a noisy hull is what makes
    ///     a low-poly boulder read as stone rather than as a lumpy balloon; smooth
    ///     normals are kept for sand, which really is smooth.
    ///   * <b>A generated mesh lives in the scene, not in the asset folder.</b> It has
    ///     exactly one owner and the next <c>BuildScene</c> throws it away with
    ///     everything else, which is the same contract every other generated thing here
    ///     has. Meshes are pooled per shape so a hundred boulders cost a dozen meshes.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        // ==================================================================
        // Noise
        // ==================================================================
        /// <summary>
        /// A hash, not a random: the same coordinates and seed always give the same
        /// number, which is what lets the terrain be sampled again later -- by a rock
        /// being placed on it, by a fence post, by the spawn snapper -- without keeping
        /// the whole field in memory or caring what order anything asked in.
        /// </summary>
        private static float Hash01(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 2147483647 + seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>Bilinear value noise in 0..1.</summary>
        private static float Noise2(float x, float y, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = Smooth(x - x0), fy = Smooth(y - y0);

            float a = Mathf.Lerp(Hash01(x0, y0, 0, seed), Hash01(x0 + 1, y0, 0, seed), fx);
            float b = Mathf.Lerp(Hash01(x0, y0 + 1, 0, seed), Hash01(x0 + 1, y0 + 1, 0, seed), fx);

            return Mathf.Lerp(a, b, fy);
        }

        /// <summary>Trilinear value noise in 0..1, for displacing a hull.</summary>
        private static float Noise3(float x, float y, float z, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), z0 = Mathf.FloorToInt(z);
            float fx = Smooth(x - x0), fy = Smooth(y - y0), fz = Smooth(z - z0);

            float c00 = Mathf.Lerp(Hash01(x0, y0, z0, seed), Hash01(x0 + 1, y0, z0, seed), fx);
            float c10 = Mathf.Lerp(Hash01(x0, y0 + 1, z0, seed), Hash01(x0 + 1, y0 + 1, z0, seed), fx);
            float c01 = Mathf.Lerp(Hash01(x0, y0, z0 + 1, seed), Hash01(x0 + 1, y0, z0 + 1, seed), fx);
            float c11 = Mathf.Lerp(Hash01(x0, y0 + 1, z0 + 1, seed), Hash01(x0 + 1, y0 + 1, z0 + 1, seed), fx);

            return Mathf.Lerp(Mathf.Lerp(c00, c10, fy), Mathf.Lerp(c01, c11, fy), fz);
        }

        /// <summary>Stacked octaves of <see cref="Noise2"/>, centred on zero.</summary>
        private static float Fbm2(float x, float y, int seed, int octaves = 4)
        {
            float sum = 0f, amp = 1f, total = 0f, freq = 1f;

            for (int i = 0; i < octaves; i++)
            {
                sum += (Noise2(x * freq, y * freq, seed + i * 131) - 0.5f) * amp;
                total += amp * 0.5f;
                amp *= 0.5f;
                freq *= 2f;
            }

            return total > 0f ? sum / total : 0f;
        }

        private static float Fbm3(Vector3 p, int seed, int octaves = 3)
        {
            float sum = 0f, amp = 1f, total = 0f, freq = 1f;

            for (int i = 0; i < octaves; i++)
            {
                sum += (Noise3(p.x * freq, p.y * freq, p.z * freq, seed + i * 977) - 0.5f) * amp;
                total += amp * 0.5f;
                amp *= 0.5f;
                freq *= 2f;
            }

            return total > 0f ? sum / total : 0f;
        }

        // ==================================================================
        // Mesh assembly
        // ==================================================================
        /// <summary>
        /// A growing triangle list with the bookkeeping that every generator here would
        /// otherwise repeat: flat-shaded faces, quads wound the right way, and a UV that
        /// comes from world size rather than from the shape, so one tiling sand or rock
        /// texture stays the same scale on a dune, a boulder and a fence post.
        /// </summary>
        private sealed class MeshBuild
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> UVs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();

            /// <summary>Metres of world per tile of texture. Planar, from the face normal.</summary>
            public float UVScale = 0.5f;

            /// <summary>A flat-shaded triangle: its own three vertices and one normal.</summary>
            public void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 1e-12f) return;

                normal.Normalize();
                int start = Vertices.Count;

                Push(a, normal);
                Push(b, normal);
                Push(c, normal);

                Triangles.Add(start);
                Triangles.Add(start + 1);
                Triangles.Add(start + 2);
            }

            /// <summary>A flat-shaded quad, wound a-b-c-d around its rim.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Tri(a, b, c);
                Tri(a, c, d);
            }

            /// <summary>An axis-aligned box, turned by <paramref name="rotation"/> about its own centre.</summary>
            public void Box(Vector3 centre, Vector3 size, Quaternion rotation)
            {
                Vector3 h = size * 0.5f;

                Vector3 P(float x, float y, float z)
                    => centre + rotation * new Vector3(x * h.x, y * h.y, z * h.z);

                Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1));      // top
                Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1));  // bottom
                Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));      // +z
                Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1));  // -z
                Quad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1));      // +x
                Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1));  // -x
            }

            /// <summary>
            /// A tapered tube between two points -- a trunk, a post, a pipe, a pylon.
            /// Capped, because an open end is a hole you can see the inside of the world
            /// through the moment the camera is above it.
            /// </summary>
            public void Tube(Vector3 from, Vector3 to, float radiusFrom, float radiusTo,
                             int sides, float twist = 0f)
            {
                var axis = to - from;
                if (axis.sqrMagnitude < 1e-8f) return;

                var rotation = Quaternion.LookRotation(axis.normalized,
                    Mathf.Abs(axis.normalized.y) > 0.99f ? Vector3.forward : Vector3.up);

                Vector3 Ring(int i, bool top)
                {
                    float a = twist + i * Mathf.PI * 2f / sides;
                    var local = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (top ? radiusTo : radiusFrom);
                    return (top ? to : from) + rotation * local;
                }

                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    Quad(Ring(i, false), Ring(j, false), Ring(j, true), Ring(i, true));
                }

                for (int i = 1; i < sides - 1; i++)
                {
                    Tri(Ring(0, true), Ring(i, true), Ring(i + 1, true));
                    Tri(Ring(0, false), Ring(i + 1, false), Ring(i, false));
                }
            }

            private void Push(Vector3 p, Vector3 n)
            {
                Vertices.Add(p);
                Normals.Add(n);

                // Planar projection off whichever axis the face faces least, so a
                // vertical surface takes its UV from x/z or y and never from the axis it
                // is flat against -- that projection collapses to a line and smears the
                // texture into stripes.
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);

                Vector2 uv = ay >= ax && ay >= az ? new Vector2(p.x, p.z)
                           : ax >= az ? new Vector2(p.z, p.y)
                           : new Vector2(p.x, p.y);

                UVs.Add(uv * UVScale);
            }

            public Mesh ToMesh(string name, bool smooth = false)
            {
                var mesh = new Mesh { name = name };

                if (Vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

                mesh.SetVertices(Vertices);
                mesh.SetUVs(0, UVs);
                mesh.SetTriangles(Triangles, 0);

                if (smooth) mesh.RecalculateNormals();
                else mesh.SetNormals(Normals);

                mesh.RecalculateBounds();
                mesh.RecalculateTangents();

                return mesh;
            }
        }

        /// <summary>
        /// Puts a generated mesh in the scene with the same tag/layer/collider contract
        /// <see cref="CreateBlock"/> gives a cube, so the rest of the kit -- surface
        /// tags for impacts, the Environment layer, the navigation bake -- treats the
        /// two identically.
        /// </summary>
        private static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material material,
                                             Vector3 position, Quaternion rotation, Vector3 scale,
                                             int layer, string tag, bool collider = true,
                                             Mesh collisionMesh = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = scale;
            go.layer = layer;
            if (!string.IsNullOrEmpty(tag)) go.tag = tag;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            if (collider)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = collisionMesh != null ? collisionMesh : mesh;
            }

            return go;
        }

        // ==================================================================
        // Bookkeeping every generated object needs
        //
        // These live here rather than beside the arena that first needed them because
        // both arena generators place meshes and both have to answer the same two
        // questions about every one: may an agent stand on it, and does the map draw it.
        // ==================================================================
        /// <summary>
        /// Keeps the navigation bake off a surface.
        ///
        /// A NavMeshSurface collects render meshes, and it will happily bake the top of a
        /// boulder, the canopy of a palm or the sheet of a tarpaulin as walkable ground,
        /// because all it asks is whether the slope is shallow enough. Every one of those
        /// comes out as a scrap of navmesh floating in the air that nothing can path to
        /// -- harmless until <c>LevelManager</c> samples the mesh near the player to
        /// place a spawn, finds one, and puts an enemy on a tree.
        ///
        /// The bottom of the canyon uses it for a sharper reason than tidiness: an
        /// enemy placed on a scrap of navmesh down there is standing inside the kill
        /// volume, and it drowns on the frame it arrives and pays out a kill nobody made.
        /// </summary>
        private static void NoStanding(GameObject go)
        {
            if (go == null) return;

            var modifier = go.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = 1;   // Not Walkable
        }

        /// <summary>
        /// Carves a building out of the navigation mesh entirely, rather than merely
        /// marking its own surfaces unwalkable.
        ///
        /// <b><see cref="NoStanding"/> is not enough for anything with an inside.</b> A
        /// modifier marks the geometry it is on, and the floor inside one of the art
        /// pack's sheds is not the shed -- it is the site's own yard slab, running
        /// underneath it. So the shed's roof stops being walkable and the room inside it
        /// carries on baking: a slab of navmesh the size of a shed, walled in on four
        /// sides, joined to nothing. The pack's hangars carry non-convex colliders, so a
        /// player can walk into them; their door openings do not admit a half-metre
        /// agent, so nothing else can. Sixteen of those on the industrial site were
        /// sixteen rooms the spawner could stand an enemy in for the whole level, and the
        /// only thing the player ever saw was a clock running out on an arena that
        /// sounded empty.
        ///
        /// <para>
        /// A volume over the whole footprint asks the right question: nothing in this box
        /// is walkable, whatever it is made of or which object it belongs to. Pulled in
        /// half a metre on each side so it eats the room and not the ground against the
        /// outside of the walls.
        /// </para>
        /// </summary>
        private static void NoEntry(GameObject go)
        {
            if (go == null) return;

            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            var volume = new GameObject("NoEntry");
            volume.transform.SetParent(go.transform, worldPositionStays: false);
            volume.transform.position = bounds.center;
            volume.transform.rotation = Quaternion.identity;

            var modifier = volume.AddComponent<Unity.AI.Navigation.NavMeshModifierVolume>();
            modifier.size = new Vector3(Mathf.Max(0.5f, bounds.size.x - 1.2f),
                                        bounds.size.y + 4f,
                                        Mathf.Max(0.5f, bounds.size.z - 1.2f));
            modifier.area = 1;   // Not Walkable
        }

        /// <summary>
        /// Marks everything outside the playable boundary as unwalkable, as four volumes
        /// rather than as geometry.
        ///
        /// <b>Every arena here has ground outside the thing that stops the player.</b> The
        /// open zone's dune field runs forty metres past the boundary before it fades,
        /// the plant's yard slab runs out to the boundary wall behind its fence, and both
        /// of those bake perfectly good navmesh -- joined to nothing, because the seal or
        /// the fence is in the way. It is the most invisible failure in the kit: the
        /// spawner samples the mesh, finds a point out there, puts an enemy on it, and
        /// the enemy stands outside the level for the rest of the round while the player
        /// waits for a fight that is never coming.
        ///
        /// <para>
        /// A volume rather than a flat piece of <see cref="NoStanding"/> geometry,
        /// because a modifier marks geometry and does not remove it -- and a strip of
        /// unwalkable geometry laid on top of walkable ground is a wall through the
        /// navmesh rather than an edge to it. A volume asks the question the right way
        /// round: nothing inside this box is walkable, whatever it is made of.
        /// </para>
        /// </summary>
        private static void SealNavMeshOutside(Transform root, float inner, float reach,
                                               float floor = -80f, float ceiling = 140f)
        {
            if (reach <= inner) return;

            var group = new GameObject("NavMeshBounds").transform;
            group.SetParent(root, false);

            float mid = (inner + reach) * 0.5f;
            float span = reach - inner;
            float height = ceiling - floor;
            float across = reach * 2f;

            for (int side = 0; side < 4; side++)
            {
                bool alongZ = side >= 2;
                float sign = side % 2 == 0 ? 1f : -1f;

                var go = new GameObject($"NavMeshBound_{side}");
                go.transform.SetParent(group, false);
                go.transform.localPosition = alongZ
                    ? new Vector3(sign * mid, (floor + ceiling) * 0.5f, 0f)
                    : new Vector3(0f, (floor + ceiling) * 0.5f, sign * mid);

                var volume = go.AddComponent<Unity.AI.Navigation.NavMeshModifierVolume>();
                volume.size = alongZ ? new Vector3(span, height, across)
                                     : new Vector3(across, height, span);
                volume.area = 1;   // Not Walkable
            }
        }

        /// <summary>
        /// Keeps something off the minimap.
        ///
        /// The map draws the footprint of anything solid and tones it by height, which is
        /// exactly right for a crate and exactly wrong for a four-hundred-metre cliff
        /// mesh: it comes back as one rectangle over a third of the map with the river's
        /// meander nowhere in it. Anything long, anything that is really terrain, and
        /// anything the player cannot collide with is hidden and something more useful is
        /// drawn in its place -- the river's own slices, in the river's case.
        /// </summary>
        private static void Hide(GameObject go)
        {
            if (go == null) return;

            go.AddComponent<MinimapMarker>().style = MinimapMarker.Style.Hidden;
        }
        /// <summary>Tells the minimap what something is, since the geometry cannot.</summary>
        private static void Mark(GameObject go, Color color, int order)
        {
            var marker = go.AddComponent<MinimapMarker>();
            marker.color = color;
            marker.order = order;
        }

        // ==================================================================
        // Shapes
        // ==================================================================
        /// <summary>Meshes are shared by shape and seed; a hundred boulders cost a dozen.</summary>
        private static readonly Dictionary<string, Mesh> _meshPool = new Dictionary<string, Mesh>();

        /// <summary>
        /// Where each palm trunk actually ends, in unit-height local space.
        ///
        /// The trunk bends by an amount drawn from its own seed, so nothing outside
        /// <see cref="PalmTrunkMesh"/> can work out where its top is -- and a crown put
        /// at (0, 1, 0) instead sits beside the tree rather than on it. Recorded rather
        /// than read back off the mesh bounds, which give the extent of the bend in each
        /// axis separately and not the point the two of them meet at.
        /// </summary>
        private static readonly Dictionary<int, Vector3> _palmTips = new Dictionary<int, Vector3>();

        private static void ClearMeshPool()
        {
            _meshPool.Clear();
            _palmTips.Clear();
        }

        /// <summary>The tip of the palm trunk with this seed, building it first if need be.</summary>
        private static Vector3 PalmTipFor(int seed)
        {
            PalmTrunkMesh(seed);
            return _palmTips.TryGetValue(seed, out var tip) ? tip : Vector3.up;
        }

        private static Mesh Pooled(string key, System.Func<Mesh> make)
        {
            if (_meshPool.TryGetValue(key, out var mesh) && mesh != null) return mesh;

            mesh = make();
            _meshPool[key] = mesh;
            return mesh;
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// A boulder: a unit icosphere pushed around by 3D noise, squashed, and cut off
        /// flat underneath.
        ///
        /// The flat bottom is the part that matters. A round rock dropped on the ground
        /// either floats on its widest point or has to be sunk by eye, and sunk by eye
        /// it pops back out the moment the ground under it is a dune rather than a
        /// plane. Cut flat at its base it sits on whatever it is put on, every time.
        /// </summary>
        private static Mesh BoulderMesh(int seed, float lumpiness = 0.34f, float squash = 0.72f)
            => Pooled($"boulder_{seed}_{lumpiness:0.00}_{squash:0.00}", () =>
            {
                var (points, faces) = IcoSphere(2);
                var build = new MeshBuild { UVScale = 0.35f };

                var moved = new Vector3[points.Count];

                for (int i = 0; i < points.Count; i++)
                {
                    var dir = points[i];

                    // Two scales of lump: the big one gives the rock its silhouette, the
                    // small one keeps the facets from all being the same size, which is
                    // what makes a subdivided sphere still look like a subdivided sphere.
                    float r = 1f
                            + Fbm3(dir * 1.6f, seed, 2) * lumpiness
                            + Fbm3(dir * 5.2f, seed + 51, 2) * lumpiness * 0.35f;

                    var p = dir * r;
                    p.y *= squash;

                    // The cut. Everything below the plane is pulled onto it, which also
                    // widens the base -- rocks are wider at the bottom.
                    if (p.y < -0.42f) p.y = -0.42f;

                    moved[i] = p;
                }

                foreach (var f in faces) build.Tri(moved[f.x], moved[f.y], moved[f.z]);

                return build.ToMesh($"Boulder_{seed}");
            });

        /// <summary>
        /// A sandstone butte: an irregular column of stacked rings, each turned and
        /// resized off the one below, with the odd ring pushed out into a ledge.
        ///
        /// This is what replaced a stack of cubes as a landmark. The cubes were legible
        /// -- you could say "the big square rock" -- and that is all they were; a dozen
        /// of them across a map read as ruins of something built rather than as rock.
        /// </summary>
        private static Mesh ButteMesh(int seed, int sides = 9, int levels = 7)
            => Pooled($"butte_{seed}_{sides}_{levels}", () =>
            {
                var build = new MeshBuild { UVScale = 0.18f };

                var rings = new Vector3[levels + 1][];

                for (int l = 0; l <= levels; l++)
                {
                    float t = l / (float)levels;

                    // Straight-sided for most of the way then tapering hard at the top,
                    // which is the profile weather actually cuts: the cap rock protects
                    // what is under it until it is undercut, and then the top goes.
                    float taper = Mathf.Lerp(1f, 0.34f, Mathf.Pow(t, 2.1f));

                    // Every third level steps out. A butte is banded, and the bands are
                    // what tell you how tall it is from across the map.
                    float ledge = (l % 3 == 1) ? 1.12f : 1f;

                    rings[l] = new Vector3[sides];

                    for (int s = 0; s < sides; s++)
                    {
                        float a = s * Mathf.PI * 2f / sides;

                        float wobble = 1f + Fbm2(Mathf.Cos(a) * 2.3f + l * 0.7f,
                                                 Mathf.Sin(a) * 2.3f, seed, 2) * 0.3f;

                        float r = taper * ledge * wobble;
                        rings[l][s] = new Vector3(Mathf.Cos(a) * r, t, Mathf.Sin(a) * r);
                    }
                }

                for (int l = 0; l < levels; l++)
                    for (int s = 0; s < sides; s++)
                    {
                        int n = (s + 1) % sides;
                        build.Quad(rings[l][s], rings[l][n], rings[l + 1][n], rings[l + 1][s]);
                    }

                // Cap and floor, so it is closed from above and from below the horizon.
                for (int s = 1; s < sides - 1; s++)
                {
                    build.Tri(rings[levels][0], rings[levels][s], rings[levels][s + 1]);
                    build.Tri(rings[0][0], rings[0][s + 1], rings[0][s]);
                }

                return build.ToMesh($"Butte_{seed}");
            });

        /// <summary>
        /// A dead desert tree: a leaning tapered trunk and three or four branches that
        /// fork off it, each thinner than its parent.
        ///
        /// Cheap, and worth more per triangle than anything else on this map. A dune
        /// field has no vertical anything in it, so the eye has nothing to measure
        /// distance against and two hundred metres looks like fifty. One bare tree fixes
        /// that for the whole view around it.
        /// </summary>
        private static Mesh DeadTreeMesh(int seed)
            => Pooled($"deadtree_{seed}", () =>
            {
                var build = new MeshBuild { UVScale = 0.9f };
                var rng = new System.Random(seed);

                float height = 1f;
                float lean = Rand(rng, -0.12f, 0.12f);

                var baseP = Vector3.zero;
                var topP = new Vector3(lean, height, Rand(rng, -0.12f, 0.12f));
                var midP = Vector3.Lerp(baseP, topP, 0.45f);

                build.Tube(baseP, midP, 0.075f, 0.055f, 7);
                build.Tube(midP, topP, 0.055f, 0.022f, 6);

                int branches = rng.Next(3, 6);

                for (int i = 0; i < branches; i++)
                {
                    float t = Rand(rng, 0.42f, 0.92f);
                    var from = Vector3.Lerp(baseP, topP, t);
                    float thickness = Mathf.Lerp(0.05f, 0.016f, t);

                    float a = Rand(rng, 0f, Mathf.PI * 2f);
                    float reach = Rand(rng, 0.2f, 0.42f) * (1f - t * 0.5f);

                    var elbow = from + new Vector3(Mathf.Cos(a) * reach, Rand(rng, 0.06f, 0.2f),
                                                   Mathf.Sin(a) * reach);
                    var tip = elbow + new Vector3(Mathf.Cos(a) * reach * 0.7f, Rand(rng, 0.1f, 0.26f),
                                                  Mathf.Sin(a) * reach * 0.7f);

                    build.Tube(from, elbow, thickness, thickness * 0.6f, 5);
                    build.Tube(elbow, tip, thickness * 0.6f, thickness * 0.2f, 4);
                }

                return build.ToMesh($"DeadTree_{seed}");
            });

        /// <summary>The trunk of a palm, curved rather than straight and ringed by its old leaf scars.</summary>
        private static Mesh PalmTrunkMesh(int seed)
            => Pooled($"palmtrunk_{seed}", () =>
            {
                var build = new MeshBuild { UVScale = 1.1f };
                var rng = new System.Random(seed);

                const int segments = 9;
                float bendX = Rand(rng, -0.18f, 0.18f);
                float bendZ = Rand(rng, -0.14f, 0.14f);

                var previous = Vector3.zero;

                for (int i = 1; i <= segments; i++)
                {
                    float t = i / (float)segments;

                    // Bends away from vertical as it climbs -- a palm leans out of the
                    // ground, it does not stand up out of it like a post.
                    var next = new Vector3(bendX * t * t, t, bendZ * t * t);

                    float rFrom = Mathf.Lerp(0.055f, 0.032f, (i - 1) / (float)segments);
                    float rTo = Mathf.Lerp(0.055f, 0.032f, t);

                    // Every other segment is a touch fatter, which is the ring of scar
                    // left where a frond fell off.
                    build.Tube(previous, next, rFrom * (i % 2 == 0 ? 1.15f : 1f), rTo, 7);
                    previous = next;
                }

                _palmTips[seed] = previous;

                return build.ToMesh($"PalmTrunk_{seed}");
            });

        /// <summary>
        /// A crown of palm fronds: tapered strips that arch out and then droop.
        ///
        /// Built as solid double-sided strips rather than as an alpha-cut texture,
        /// because the kit has no foliage texture to cut and a cut-out leaf with no
        /// texture behind it is an untextured rectangle.
        /// </summary>
        private static Mesh PalmCrownMesh(int seed)
            => Pooled($"palmcrown_{seed}", () =>
            {
                var build = new MeshBuild { UVScale = 0.6f };
                var rng = new System.Random(seed);

                int fronds = rng.Next(9, 13);

                for (int f = 0; f < fronds; f++)
                {
                    float yaw = f * Mathf.PI * 2f / fronds + Rand(rng, -0.16f, 0.16f);
                    float length = Rand(rng, 0.75f, 1.1f);
                    float lift = Rand(rng, 0.2f, 0.62f);

                    var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                    var side = new Vector3(-Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));

                    const int steps = 5;
                    var previous = Vector3.zero;
                    float previousWidth = 0.02f;

                    for (int s = 1; s <= steps; s++)
                    {
                        float t = s / (float)steps;

                        // Up first, then over: the arch is the whole silhouette of a palm.
                        float y = Mathf.Sin(t * 2.1f) * lift - t * t * lift * 1.55f;
                        var next = outward * (length * t) + Vector3.up * y;

                        float width = Mathf.Sin(t * Mathf.PI) * 0.1f + 0.012f;

                        build.Quad(previous - side * previousWidth, next - side * width,
                                   next + side * width, previous + side * previousWidth);
                        build.Quad(previous + side * previousWidth, next + side * width,
                                   next - side * width, previous - side * previousWidth);

                        previous = next;
                        previousWidth = width;
                    }
                }

                return build.ToMesh($"PalmCrown_{seed}");
            });

        /// <summary>A low desert shrub: a few dozen stiff blades fanning out of one point.</summary>
        private static Mesh ScrubMesh(int seed)
            => Pooled($"scrub_{seed}", () =>
            {
                var build = new MeshBuild { UVScale = 1.4f };
                var rng = new System.Random(seed);

                int blades = rng.Next(14, 22);

                for (int i = 0; i < blades; i++)
                {
                    float yaw = Rand(rng, 0f, Mathf.PI * 2f);
                    float lean = Rand(rng, 0.35f, 0.9f);
                    float height = Rand(rng, 0.55f, 1f);

                    var tip = new Vector3(Mathf.Cos(yaw) * lean, height, Mathf.Sin(yaw) * lean);
                    var side = new Vector3(-Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * 0.035f;
                    var root = new Vector3(Mathf.Cos(yaw) * 0.06f, 0f, Mathf.Sin(yaw) * 0.06f);

                    build.Tri(root - side, root + side, tip);
                    build.Tri(root + side, root - side, tip);
                }

                return build.ToMesh($"Scrub_{seed}");
            });

        /// <summary>
        /// A sandbag: a squashed, dented pillow. Sixty of them stacked is a bunker wall,
        /// and stacking them is why they are a mesh rather than a cube -- a wall of
        /// cubes has one straight top edge and reads as a wall, while a wall of these
        /// has a lumpy one and reads as something people carried there.
        /// </summary>
        private static Mesh SandbagMesh(int seed)
            => Pooled($"sandbag_{seed}", () =>
            {
                var (points, faces) = IcoSphere(1);
                var build = new MeshBuild { UVScale = 1.6f };

                var moved = new Vector3[points.Count];

                for (int i = 0; i < points.Count; i++)
                {
                    var dir = points[i];
                    float r = 1f + Fbm3(dir * 2.4f, seed, 2) * 0.16f;

                    moved[i] = new Vector3(dir.x * r, dir.y * r * 0.46f, dir.z * r * 0.66f);
                }

                foreach (var f in faces) build.Tri(moved[f.x], moved[f.y], moved[f.z]);

                return build.ToMesh($"Sandbag_{seed}");
            });

        // ------------------------------------------------------------------
        /// <summary>
        /// A unit icosphere: twelve points and twenty faces, subdivided
        /// <paramref name="subdivisions"/> times onto the sphere.
        ///
        /// An icosahedron rather than a ring-and-segment sphere because a UV sphere has
        /// poles, and a pole is where a dozen skinny triangles meet -- displace that by
        /// noise and the rock grows a spike out of the top with a pinch under it.
        /// </summary>
        private static (List<Vector3> points, List<Vector3Int> faces) IcoSphere(int subdivisions)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;

            var points = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };

            for (int i = 0; i < points.Count; i++) points[i] = points[i].normalized;

            var faces = new List<Vector3Int>
            {
                new Vector3Int(0, 11, 5), new Vector3Int(0, 5, 1), new Vector3Int(0, 1, 7),
                new Vector3Int(0, 7, 10), new Vector3Int(0, 10, 11), new Vector3Int(1, 5, 9),
                new Vector3Int(5, 11, 4), new Vector3Int(11, 10, 2), new Vector3Int(10, 7, 6),
                new Vector3Int(7, 1, 8), new Vector3Int(3, 9, 4), new Vector3Int(3, 4, 2),
                new Vector3Int(3, 2, 6), new Vector3Int(3, 6, 8), new Vector3Int(3, 8, 9),
                new Vector3Int(4, 9, 5), new Vector3Int(2, 4, 11), new Vector3Int(6, 2, 10),
                new Vector3Int(8, 6, 7), new Vector3Int(9, 8, 1)
            };

            for (int s = 0; s < subdivisions; s++)
            {
                var split = new List<Vector3Int>(faces.Count * 4);
                var midpoints = new Dictionary<long, int>();

                int Midpoint(int a, int b)
                {
                    long key = a < b ? (long)a << 32 | (uint)b : (long)b << 32 | (uint)a;
                    if (midpoints.TryGetValue(key, out int found)) return found;

                    points.Add(((points[a] + points[b]) * 0.5f).normalized);
                    midpoints[key] = points.Count - 1;
                    return points.Count - 1;
                }

                foreach (var f in faces)
                {
                    int ab = Midpoint(f.x, f.y), bc = Midpoint(f.y, f.z), ca = Midpoint(f.z, f.x);

                    split.Add(new Vector3Int(f.x, ab, ca));
                    split.Add(new Vector3Int(f.y, bc, ab));
                    split.Add(new Vector3Int(f.z, ca, bc));
                    split.Add(new Vector3Int(ab, bc, ca));
                }

                faces = split;
            }

            return (points, faces);
        }
    }
}
#endif
