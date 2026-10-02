#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// What turns the fairground from rides in a field into a place: paved walkways with
    /// kerbs, lamp posts and benches along them, festoons of bulbs strung across, a pond
    /// with a footbridge, a bumper-car pavilion and the giant mascot faces over the gate
    /// and the midway.
    ///
    /// <b>The walkways are paint, the furniture is not.</b> A walkway is a thin visual mesh
    /// laid over the real heightfield, with no collider and no navigation surface of its
    /// own, exactly as the worn paths before it were -- the ground under it is the
    /// terrain and was already proven reachable. A kerb is a low strip on the backdrop
    /// layer, never a navigation modifier (a modifier on a kerb carves the walkway in two).
    /// Lamp posts and benches stand beside it, never on it.
    ///
    /// <b>Everything that glows is a material, not a light.</b> A lamp is a few hundred
    /// triangles and an emissive globe; a real light is a per-pixel cost on every surface
    /// in its range. Only one lamp in four carries a light, so the walkways read as lit
    /// at dusk without turning the map into sixty light sources.
    /// </summary>
    public partial class FPSKitSceneBuilder
    {
        // ------------------------------------------------------------------
        // Materials
        // ------------------------------------------------------------------
        private static Material _fgLeafA, _fgLeafB, _fgFern, _fgBark, _fgPaving, _fgAsphalt, _fgKerb,
                                _fgBulb, _fgWater, _fgSkin, _fgClownRed, _fgClownHair, _fgBrass, _fgPaving2, _fgPavingLight, _fgLine, _fgGravel;

        private static void ResolveFairgroundMaterials()
        {
            // Dusk greens, kept off the saturated end: a leaf in this light is a dark green
            // with warmth in it, and a bright one reads as plastic against the sky.
            _fgLeafA = MakeDetailMaterial("JungleLeafA", new Color(0.21f, 0.39f, 0.14f), "Adobe",
                                          Metres(2f), 0.1f, 0f, 0.4f);
            _fgLeafB = MakeDetailMaterial("JungleLeafB", new Color(0.38f, 0.48f, 0.16f), "Adobe",
                                          Metres(2f), 0.1f, 0f, 0.4f);
            _fgFern = MakeDetailMaterial("JungleFern", new Color(0.24f, 0.45f, 0.16f), "Adobe",
                                         Metres(2f), 0.1f, 0f, 0.4f);
            _fgBark = MakeDetailMaterial("JungleBark", new Color(0.15f, 0.125f, 0.105f), "Timber",
                                         Metres(1.6f), 0.05f, 0f, 1.2f);

            // Three stones, from the same quarry: a worn mid-tone and a lighter and a darker one. Low
            // normal scale, because the sand-like relief of the concrete map is what made the first
            // paving read as ground.
            _fgPaving = MakeDetailMaterial("Paving", new Color(0.47f, 0.44f, 0.39f), "Concrete",
                                           Metres(5f), 0.1f, 0f, 0.3f);
            _fgPavingLight = MakeDetailMaterial("PavingLight", new Color(0.60f, 0.57f, 0.50f), "Concrete",
                                                Metres(5f), 0.1f, 0f, 0.3f);
            _fgGravel = MakeDetailMaterial("Gravel", new Color(0.36f, 0.33f, 0.28f), "Rock", Metres(3f), 0.04f, 0f, 0.6f);
            _fgPaving2 = MakeDetailMaterial("PavingDark", new Color(0.31f, 0.29f, 0.27f), "Concrete",
                                            Metres(5f), 0.1f, 0f, 0.3f);
            _fgLine = MakeDetailMaterial("RoadLine", new Color(0.52f, 0.43f, 0.16f), "Concrete",
                                         Metres(3.4f), 0.05f, 0f, 0.4f);
            _fgAsphalt = MakeDetailMaterial("Asphalt", new Color(0.13f, 0.13f, 0.13f), "Concrete",
                                            Metres(3.4f), 0.08f, 0f, 0.7f);
            _fgKerb = MakeDetailMaterial("Kerb", new Color(0.50f, 0.47f, 0.42f), "Concrete",
                                         Metres(2.4f), 0.06f, 0f, 0.8f);

            _fgBulb = MakeDetailMaterial("Bulb", new Color(0.25f, 0.17f, 0.08f), "Concrete",
                                         Metres(2f), 0.4f, 0f, 0.2f);
            SetEmission(_fgBulb, new Color(1f, 0.68f, 0.32f) * 2.4f);

            _fgWater = MakeDetailMaterial("PondWater", new Color(0.09f, 0.18f, 0.17f), "Water",
                                          0.06f, 0.92f, 0.05f, 0.7f);
            _fgBrass = MakeDetailMaterial("Brass", new Color(0.46f, 0.36f, 0.17f), "Metal",
                                          Metres(1.2f), 0.45f, 0.6f);

            _fgSkin = MakeDetailMaterial("ClownSkin", new Color(0.78f, 0.70f, 0.58f), "Adobe",
                                         Metres(4f), 0.1f, 0f, 0.4f);
            _fgClownRed = MakeDetailMaterial("ClownRed", new Color(0.58f, 0.12f, 0.10f), "Timber",
                                             Metres(3f), 0.18f, 0f, 0.5f);
            _fgClownHair = MakeDetailMaterial("ClownHair", new Color(0.64f, 0.29f, 0.07f), "Timber",
                                              Metres(3f), 0.1f, 0f, 0.5f);

            ResolveHallMaterials();
            ResolveRideMaterials();
            ResolveAttractionMaterials();
        }

        // ------------------------------------------------------------------
        // Walkways
        // ------------------------------------------------------------------
        private sealed class PathBatch
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();
        }

        private static void EmitPaths(Transform parent, int layer, string name, PathBatch batch, Material material)
        {
            if (batch.Vertices.Count == 0) return;

            var mesh = new Mesh { name = $"Fairground{name}" };
            mesh.SetVertices(batch.Vertices);
            mesh.SetUVs(0, batch.Uvs);
            mesh.SetTriangles(batch.Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            Hide(go);
        }

        // ------------------------------------------------------------------
        // Placement tests that ask the physics scene
        // ------------------------------------------------------------------
        /// <summary>
        /// Whether nothing solid but the ground stands in a column over a point. Asked of
        /// the physics scene, not the claim circles, because by the end of a build those
        /// overlap across most of the map -- the same call the desert makes for its groves.
        /// </summary>
        private static bool SpotEmpty(Vector2 p, float radius, float low = 0.4f, float high = 5f)
        {
            Physics.SyncTransforms();
            float g = GroundHeightAt(p.x, p.y);
            float bottom = g + low + radius;
            float top = Mathf.Max(g + high, bottom + 0.1f);

            var hits = Physics.OverlapCapsule(new Vector3(p.x, bottom, p.y), new Vector3(p.x, top, p.y),
                                              radius, ~0, QueryTriggerInteraction.Ignore);

            foreach (var hit in hits)
                if (hit.gameObject.name != "Dune") return false;

            return true;
        }

        // ------------------------------------------------------------------
        // Furniture along the walkways
        // ------------------------------------------------------------------
        /// <summary>
        /// Lamp posts, benches and bins beside the walkways, bulbs strung between the
        /// lamps, and the mascot faces over the gate and the midway.
        /// </summary>
        private static void BuildPromenadeFurniture(Transform root, int layer, int backdrop)
        {
            var group = new GameObject("Promenade").transform;
            group.SetParent(root, false);

            var rng = new System.Random(_theme.randomSeed * 3301 + 71);

            var posts = new MeshBuild { UVScale = 0.5f };
            var lit = new MeshBuild { UVScale = 0.5f };
            var dead = new MeshBuild { UVScale = 0.5f };
            var wire = new MeshBuild { UVScale = 0.5f };
            var bulbs = new MeshBuild { UVScale = 0.5f };
            var benches = new MeshBuild { UVScale = 0.5f };

            int lampCount = 0, benchCount = 0, binCount = 0;

            foreach (var route in _fgRoutes)
            {
                if (route.Width < 4f) continue;

                var lamps = new List<Vector3>();
                float walked = 0f, nextLamp = 9f, nextBench = 22f, nextBin = 38f;
                int side = 1;
                var points = route.Points;
                float offset = route.Width * 0.5f + 1.5f;

                for (int i = 0; i < points.Count - 1; i++)
                {
                    var a = points[i];
                    var b = points[i + 1];
                    float length = Vector2.Distance(a, b);
                    if (length < 0.01f) continue;
                    var along = (b - a) / length;
                    var across = new Vector2(-along.y, along.x);

                    while (walked + length > nextLamp || walked + length > nextBench || walked + length > nextBin)
                    {
                        if (walked + length > nextLamp)
                        {
                            var p = a + along * (nextLamp - walked) + across * side * offset;
                            nextLamp += 17f;
                            side = -side;

                            if (OffSinks(p, 1f) && SpotEmpty(p, 0.6f))
                            {
                                bool on = rng.NextDouble() > 0.28;
                                var top = LampPost(posts, on ? lit : dead, p, rng);
                                lamps.Add(top);

                                if (on && lampCount % 4 == 0) LampLight(group, top);
                                lampCount++;
                            }
                        }
                        else if (walked + length > nextBench)
                        {
                            var p = a + along * (nextBench - walked) - across * side * (offset + 0.3f);
                            nextBench += 34f;

                            if (OffSinks(p, 1f) && SpotEmpty(p, 1.1f, 0.2f, 1.5f))
                            {
                                // Facing the walkway; one in six has been knocked over.
                                float yaw = Mathf.Atan2(side * across.x, side * across.y) * Mathf.Rad2Deg;
                                Bench(benches, new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y), yaw,
                                      rng.NextDouble() < 0.17, rng);
                                benchCount++;
                            }
                        }
                        else
                        {
                            var p = a + along * (nextBin - walked) + across * side * (offset + 0.2f);
                            nextBin += 55f;

                            if (OffSinks(p, 1f) && SpotEmpty(p, 0.7f, 0.2f, 1.5f))
                            {
                                BuildBin(group, layer, rng, p, $"Walk{binCount}");
                                binCount++;
                            }
                        }
                    }

                    walked += length;
                }

                // Bulbs strung from lamp to lamp, so they cross the walkway on the zigzag.
                for (int i = 0; i < lamps.Count - 1; i++)
                {
                    float gap = Vector3.Distance(lamps[i], lamps[i + 1]);
                    if (gap < 7f || gap > 24f || rng.NextDouble() < 0.25) continue;
                    Festoon(wire, bulbs, lamps[i], lamps[i + 1], rng);
                }
            }

            EmitFurniture(group, layer, backdrop, "LampPosts", posts, _parkSteel, true);
            EmitFurniture(group, layer, backdrop, "LampGlobes", lit, _fgBulb, false);
            EmitFurniture(group, layer, backdrop, "LampGlobesDead", dead, _parkDark, false);
            EmitFurniture(group, layer, backdrop, "Festoon", wire, _parkSteel, false);
            EmitFurniture(group, layer, backdrop, "FestoonBulbs", bulbs, _fgBulb, false);
            EmitFurniture(group, layer, backdrop, "Benches", benches, _parkTimber, true);

            Debug.Log($"[FPSKit] fairground promenade: {lampCount} lamp(s), {benchCount} bench(es), {binCount} bin(s) on {_fgRoutes.Count} route(s).");
        }

        /// <summary>
        /// Furniture as welded meshes. <paramref name="solid"/> ones are cover and block;
        /// the rest are light and cannot be walked into or on.
        /// </summary>
        private static void EmitFurniture(Transform parent, int layer, int backdrop, string name,
                                          MeshBuild build, Material material, bool solid)
        {
            if (build.Triangles.Count == 0) return;

            var go = MeshObject(parent, name, ToMesh(build, DenseKey(name.ToLowerInvariant())), material,
                                Vector3.zero, Quaternion.identity, Vector3.one,
                                solid ? layer : backdrop, solid ? "Wood" : null, collider: solid);

            if (solid) NoStanding(go);
            Hide(go);
        }

        /// <summary>A cast-iron lamp: plinth, fluted column, collar and lantern. Returns the lantern.</summary>
        private static Vector3 LampPost(MeshBuild posts, MeshBuild globe, Vector2 p, System.Random rng)
        {
            var foot = new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y);
            const float H = 5.4f;

            posts.Tube(foot - Vector3.up * 0.1f, foot + Vector3.up * 0.7f, 0.34f, 0.2f, 6);
            posts.Tube(foot + Vector3.up * 0.6f, foot + Vector3.up * (H - 0.6f), 0.12f, 0.085f, 6);
            posts.Tube(foot + Vector3.up * (H - 0.9f), foot + Vector3.up * (H - 0.5f), 0.2f, 0.15f, 6);
            posts.Tube(foot + Vector3.up * (H - 0.1f), foot + Vector3.up * (H + 0.25f), 0.42f, 0.04f, 6);

            var lantern = foot + Vector3.up * (H - 0.45f);
            globe.Box(lantern, new Vector3(0.5f, 0.55f, 0.5f), Quaternion.identity);

            // A few are leaning; a post that has stood this long has moved.
            return lantern;
        }

        private static void LampLight(Transform parent, Vector3 at)
        {
            var go = new GameObject("LampLight");
            go.transform.SetParent(parent, false);
            go.transform.position = at;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.70f, 0.40f);
            light.intensity = 3.2f;
            light.range = 15f;
            light.shadows = LightShadows.None;
        }

        /// <summary>
        /// Bulbs on a wire between two lamp heads. The wire sags in a parabola, and one
        /// bulb in five is missing, which is what makes it a string that has been left.
        /// </summary>
        private static void Festoon(MeshBuild wire, MeshBuild bulbs, Vector3 from, Vector3 to, System.Random rng)
        {
            float length = Vector3.Distance(from, to);
            int steps = Mathf.Max(4, Mathf.RoundToInt(length / 1.1f));
            float sag = Mathf.Min(1.4f, length * 0.07f);

            var previous = from;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                var p = Vector3.Lerp(from, to, t) + Vector3.down * sag * 4f * t * (1f - t);
                wire.Tube(previous, p, 0.014f, 0.014f, 3);
                previous = p;

                if (i == steps || rng.NextDouble() < 0.2) continue;
                bulbs.Box(p + Vector3.down * 0.1f, new Vector3(0.15f, 0.2f, 0.15f), Quaternion.identity);
            }
        }

        /// <summary>A park bench: slatted seat, back and two iron ends.</summary>
        private static void Bench(MeshBuild build, Vector3 foot, float yaw, bool knockedOver, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            if (knockedOver) rot *= Quaternion.Euler(0f, 0f, rng.NextDouble() < 0.5 ? 78f : -78f);

            Vector3 P(float x, float y, float z) => foot + rot * new Vector3(x, y, z);

            build.Box(P(0f, 0.5f, 0f), new Vector3(1.9f, 0.08f, 0.5f), rot);
            build.Box(P(0f, 0.86f, -0.24f), new Vector3(1.9f, 0.4f, 0.06f), rot * Quaternion.Euler(-9f, 0f, 0f));
            for (int e = -1; e <= 1; e += 2)
            {
                build.Box(P(e * 0.85f, 0.25f, 0f), new Vector3(0.07f, 0.5f, 0.46f), rot);
                build.Box(P(e * 0.85f, 0.7f, -0.22f), new Vector3(0.07f, 0.8f, 0.07f), rot);
            }
        }

        // ------------------------------------------------------------------
        // Pond and footbridge
        // ------------------------------------------------------------------
        /// <summary>
        /// A long shallow pond with reeds and lily pads, and an arched footbridge over its
        /// narrow way. Ankle-deep and walkable, because water you cannot cross is a wall
        /// with a nicer texture and this ground is a fight, not a view.
        ///
        /// <b>The bridge is not a navigation surface.</b> Everything walkable here is the
        /// terrain, and a deck is the kind of join that fails -- so it is marked unwalkable
        /// to the bake and left for the player, who can still cross it, and the enemies
        /// wade.
        /// </summary>
        private static void BuildPond(Transform root, int layer, int backdrop)
        {
            if (!_hasPond) return;

            var site = _pondPlan;
            var group = new GameObject("Pond").transform;
            group.SetParent(root, false);
            group.position = new Vector3(site.Centre.x, GroundHeightAt(site.Centre.x, site.Centre.y), site.Centre.y);
            group.localRotation = Quaternion.Euler(0f, site.Yaw, 0f);

            var rng = new System.Random(_theme.randomSeed * 7211 + 5);

            // Three overlapping discs along the pond's length.
            var lobes = new[]
            {
                (centre: new Vector3(-8.5f, 0f, 0.8f), radius: 5.2f),
                (centre: new Vector3(0f, 0f, 0f), radius: 6.4f),
                (centre: new Vector3(8.2f, 0f, -0.9f), radius: 5.0f),
            };

            var water = new MeshBuild { UVScale = 0.08f };
            foreach (var lobe in lobes)
                water.Tube(lobe.centre - Vector3.up * 0.2f, lobe.centre + Vector3.up * 0.13f, lobe.radius, lobe.radius, 28);

            var surface = MeshObject(group, "Water", ToMesh(water, DenseKey("pondwater")), _fgWater,
                                     Vector3.zero, Quaternion.identity, Vector3.one, layer, "Concrete");
            Mark(surface, new Color(0.30f, 0.52f, 0.58f, 0.85f), 1);

            // The rim: kerb stones round the outside of the union of the discs, and reeds
            // and pads inside it.
            var rim = new MeshBuild { UVScale = 0.5f };
            var green = new MeshBuild { UVScale = 0.5f };
            var pads = new MeshBuild { UVScale = 0.5f };

            for (int l = 0; l < lobes.Length; l++)
            {
                var lobe = lobes[l];
                int segments = 22;
                for (int s = 0; s < segments; s++)
                {
                    float a0 = s * Mathf.PI * 2f / segments, a1 = (s + 1) * Mathf.PI * 2f / segments;
                    var p0 = lobe.centre + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * (lobe.radius + 0.25f);
                    var p1 = lobe.centre + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * (lobe.radius + 0.25f);
                    var mid = (p0 + p1) * 0.5f;

                    // Inside a neighbouring disc: that stretch of rim would be in the water.
                    bool inside = false;
                    for (int o = 0; o < lobes.Length; o++)
                        if (o != l && (mid - lobes[o].centre).magnitude < lobes[o].radius + 0.6f) inside = true;
                    if (inside) continue;

                    // Broken in places, because nothing here is intact.
                    if (rng.NextDouble() < 0.12) continue;

                    var wp0 = group.TransformPoint(p0);
                    var wp1 = group.TransformPoint(p1);
                    var world = (wp0 + wp1) * 0.5f;
                    float ground = GroundHeightAt(world.x, world.z);
                    var a = new Vector3(wp0.x, ground + 0.22f, wp0.z);
                    var b = new Vector3(wp1.x, ground + 0.22f, wp1.z);
                    rim.Box((a + b) * 0.5f, new Vector3(0.5f, 0.4f, Vector3.Distance(a, b) + 0.06f),
                            Quaternion.LookRotation(b - a));

                    // Reeds standing in the shallows along the rim.
                    if (rng.NextDouble() < 0.5)
                    {
                        var reed = lobe.centre + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * (lobe.radius - 0.6f);
                        var rw = group.TransformPoint(reed);
                        Tuft(green, new Vector3(rw.x, group.position.y + 0.1f, rw.z), Rand(rng, 1.4f, 2.3f), rng.Next(1, 99999));
                        Tuft(green, new Vector3(rw.x + 0.4f, group.position.y + 0.1f, rw.z + 0.3f), Rand(rng, 1.0f, 1.8f), rng.Next(1, 99999));
                    }
                }

                // Lily pads on the water.
                for (int k = 0; k < 6; k++)
                {
                    float a = Rand(rng, 0f, 6.283f), d = Rand(rng, 0f, lobe.radius - 1.4f);
                    var pad = group.TransformPoint(lobe.centre + new Vector3(Mathf.Cos(a) * d, 0.16f, Mathf.Sin(a) * d));
                    pads.Tube(pad, pad + Vector3.up * 0.02f, Rand(rng, 0.3f, 0.6f), Rand(rng, 0.3f, 0.6f), 8);
                }
            }

            if (rim.Triangles.Count > 0)
            {
                // Parented to the arena, not the pond: these were built in world space.
                var go = MeshObject(root, "PondRim", ToMesh(rim, DenseKey("pondrim")), _fgKerb, Vector3.zero,
                                    Quaternion.identity, Vector3.one, backdrop, null, collider: false);
                Hide(go);
            }

            EmitFoliage(root, backdrop, "Reeds", green, _fgFern);
            EmitFoliage(root, backdrop, "LilyPads", pads, _fgLeafA);

            // The footbridge across the narrow part, between the first two lobes.
            BuildFootbridge(group, layer, new Vector3(-4.2f, 0f, 0f));

            Keep(site.Centre.x, site.Centre.y, 17f);
        }

        /// <summary>An arched timber footbridge across the pond, running along local z.</summary>
        private static void BuildFootbridge(Transform parent, int layer, Vector3 localCentre)
        {
            const float Half = 8.2f, Rise = 1.35f, DeckWidth = 3f;
            const int Segments = 12;

            var bridge = new GameObject("Footbridge").transform;
            bridge.SetParent(parent, false);
            bridge.localPosition = localCentre;

            var build = new MeshBuild { UVScale = 0.6f };

            float Height(float s) => Rise * (1f - (s / Half) * (s / Half));

            for (int i = 0; i < Segments; i++)
            {
                float s0 = Mathf.Lerp(-Half, Half, i / (float)Segments);
                float s1 = Mathf.Lerp(-Half, Half, (i + 1) / (float)Segments);
                var a = new Vector3(0f, Height(s0), s0);
                var b = new Vector3(0f, Height(s1), s1);
                var rot = Quaternion.LookRotation(b - a);

                build.Box((a + b) * 0.5f - Vector3.up * 0.12f,
                          new Vector3(DeckWidth, 0.24f, Vector3.Distance(a, b) + 0.05f), rot);

                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side * (DeckWidth * 0.5f - 0.08f);
                    var pa = a + new Vector3(x, 0f, 0f);
                    var pb = b + new Vector3(x, 0f, 0f);

                    build.Box(pa + Vector3.up * 0.5f, new Vector3(0.1f, 1f, 0.1f), Quaternion.identity);
                    build.Box((pa + pb) * 0.5f + Vector3.up * 0.95f,
                              new Vector3(0.1f, 0.1f, Vector3.Distance(pa, pb) + 0.04f), rot);
                }
            }

            // The last post, which the loop above leaves off.
            for (int side = -1; side <= 1; side += 2)
                build.Box(new Vector3(side * (DeckWidth * 0.5f - 0.08f), 0.5f, Half), new Vector3(0.1f, 1f, 0.1f),
                          Quaternion.identity);

            // The deck is walkable: it rises 1.35m over 8.2m, which is 18 degrees, and both ends
            // land on the pond's own flat pad, so the bake joins it to the ground at each end.
            MeshObject(bridge, "BridgeDeck", ToMesh(build, DenseKey("footbridge")), _parkTimber,
                       Vector3.zero, Quaternion.identity, Vector3.one, layer, "Wood");
        }

        // ------------------------------------------------------------------
        // Bumper-car pavilion
        // ------------------------------------------------------------------
        /// <summary>
        /// A roofed rink with a low rail round it and bumper cars left where they stopped.
        ///
        /// <b>The rail has gaps.</b> Anything with an inside needs ways in and out: a rail
        /// closed all round would make the floor within a sealed pen the navigation bake
        /// still walks, which is the stranded-patch failure the industrial hall taught.
        /// There are three openings on each long side and one at each end, and each is wide
        /// enough for an agent and a player side by side.
        /// </summary>
        private static void BuildBumperPavilion(Transform root, int layer, int backdrop)
        {
            if (!_hasBumper) return;

            var site = _bumperPlan;
            var group = new GameObject("BumperPavilion").transform;
            group.SetParent(root, false);
            group.position = new Vector3(site.Centre.x, GroundHeightAt(site.Centre.x, site.Centre.y), site.Centre.y);
            group.localRotation = Quaternion.Euler(0f, site.Yaw, 0f);

            var rng = new System.Random(_theme.randomSeed * 8191 + 9);
            const float HX = 19f, HZ = 11f;

            // The floor: dark steel plates, laid over the ground.
            var floor = new MeshBuild { UVScale = 0.25f };
            float fy = 0.05f;
            floor.Quad(new Vector3(-HX, fy, -HZ), new Vector3(-HX, fy, HZ), new Vector3(HX, fy, HZ), new Vector3(HX, fy, -HZ));
            var floorGo = MeshObject(group, "RinkFloor", ToMesh(floor, DenseKey("rinkfloor")), _parkSteel,
                                     Vector3.zero, Quaternion.identity, Vector3.one, backdrop, null, collider: false);
            Hide(floorGo);

            // The rail, in runs with openings left between them.
            void Run(float x0, float z0, float x1, float z1)
            {
                var a = new Vector3(x0, 0.45f, z0);
                var b = new Vector3(x1, 0.45f, z1);
                var mid = (a + b) * 0.5f;
                var go = CreateBlock(group, mid, new Vector3(0.3f, 0.9f, Vector3.Distance(a, b)), 0f, layer,
                                     "Metal", new Color(0.36f, 0.14f, 0.12f), 0.2f, 0.3f, "RinkRail", _parkPaint);
                go.transform.localRotation = Quaternion.LookRotation(b - a);
                NoStanding(go);
            }

            // Long sides: three runs and two-and-a-bit metre... four-metre openings.
            for (int side = -1; side <= 1; side += 2)
            {
                float[] edges = { -HX, -HX + 9f, -4f, 4f, HX - 9f, HX };
                Run(edges[0], side * HZ, edges[1], side * HZ);
                Run(edges[2], side * HZ, edges[3], side * HZ);
                Run(edges[4], side * HZ, edges[5], side * HZ);
            }

            // Short ends: a run either side of a four-metre opening.
            for (int end = -1; end <= 1; end += 2)
            {
                Run(end * HX, -HZ, end * HX, -2.2f);
                Run(end * HX, 2.2f, end * HX, HZ);
            }

            // The roof: a shallow gable on eight posts, partly open.
            var roof = new MeshBuild { UVScale = 0.4f };
            const float PostY = 5.4f, RidgeY = 6.9f, Over = 1.6f;
            for (int i = 0; i <= 4; i++)
            {
                float x = Mathf.Lerp(-HX - Over, HX + Over, i / 4f);
                for (int side = -1; side <= 1; side += 2)
                {
                    var post = CreateBlock(group, new Vector3(x, PostY * 0.5f, side * (HZ + Over)),
                                           new Vector3(0.3f, PostY, 0.3f), 0f, layer, "Metal",
                                           new Color(0.28f, 0.29f, 0.3f), 0.3f, 0.5f, "RinkPost", _parkSteel);
                    NoStanding(post);
                }
            }

            for (int panel = 0; panel < 12; panel++)
            {
                if (rng.NextDouble() < 0.14) continue;
                float x0 = Mathf.Lerp(-HX - Over, HX + Over, panel / 12f);
                float x1 = Mathf.Lerp(-HX - Over, HX + Over, (panel + 1) / 12f);
                for (int side = -1; side <= 1; side += 2)
                {
                    float z = side * (HZ + Over);
                    var a = new Vector3(x0, PostY, z); var b = new Vector3(x1, PostY, z);
                    var c = new Vector3(x1, RidgeY, 0f); var d = new Vector3(x0, RidgeY, 0f);
                    roof.Quad(a, d, c, b);
                    roof.Quad(b, c, d, a);
                }
            }

            var roofGo = MeshObject(group, "RinkRoof", ToMesh(roof, DenseKey("rinkroof")), _parkCanvas, Vector3.zero,
                                    Quaternion.identity, Vector3.one, backdrop, null, collider: false);
            NoStanding(roofGo);
            Hide(roofGo);

            // A string of bulbs round the eaves.
            var bulbs = new MeshBuild { UVScale = 0.5f };
            for (int side = -1; side <= 1; side += 2)
                for (float x = -HX - Over; x <= HX + Over; x += 1.5f)
                    if (rng.NextDouble() > 0.15)
                        bulbs.Box(new Vector3(x, PostY - 0.2f, side * (HZ + Over)), new Vector3(0.16f, 0.2f, 0.16f), Quaternion.identity);
            var bulbGo = MeshObject(group, "RinkBulbs", ToMesh(bulbs, DenseKey("rinkbulbs")), _fgBulb, Vector3.zero,
                                    Quaternion.identity, Vector3.one, backdrop, null, collider: false);
            Hide(bulbGo);

            // The cars, where they stopped: a body, a seat back, a steering column and the
            // pole that once touched the ceiling grid.
            int cars = 14;
            for (int i = 0; i < cars; i++)
            {
                var at = new Vector3(Rand(rng, -HX + 3f, HX - 3f), 0f, Rand(rng, -HZ + 2.2f, HZ - 2.2f));
                float yaw = Rand(rng, 0f, 360f);
                var paint = _theme.RandomCoverColor(rng);

                var car = new GameObject($"BumperCar{i}").transform;
                car.SetParent(group, false);
                car.localPosition = at;
                car.localRotation = Quaternion.Euler(0f, yaw, 0f);

                var body = CreateBlock(car, new Vector3(0f, 0.4f, 0f), new Vector3(1.5f, 0.55f, 1.15f), 0f, layer,
                                       "Metal", paint, 0.35f, 0.2f, "Body", _parkPaint);
                NoStanding(body);
                var ring = CreateBlock(car, new Vector3(0f, 0.3f, 0f), new Vector3(1.8f, 0.3f, 1.4f), 0f, layer,
                                       "Metal", new Color(0.08f, 0.08f, 0.08f), 0.1f, 0f, "Bumper", _parkDark);
                NoStanding(ring);
                var back = CreateBlock(car, new Vector3(-0.5f, 0.85f, 0f), new Vector3(0.2f, 0.6f, 0.9f), 0f, layer,
                                       "Metal", Shade(paint, 0.8f), 0.2f, 0f, "Seat", _parkPaint);
                NoStanding(back);
                var pole = CreateBlock(car, new Vector3(-0.7f, 2.2f, 0f), new Vector3(0.06f, 3.2f, 0.06f), 0f, layer,
                                       "Metal", new Color(0.25f, 0.25f, 0.27f), 0.3f, 0.6f, "Pole", _parkSteel);
                pole.transform.localRotation = Quaternion.Euler(0f, 0f, rng.NextDouble() < 0.5 ? 3f : -8f);
                NoStanding(pole);
            }

            // Two warm lights under the roof.
            for (int i = -1; i <= 1; i += 2)
            {
                var go = new GameObject("RinkLight");
                go.transform.SetParent(group, false);
                go.transform.localPosition = new Vector3(i * 9.5f, 5f, 0f);
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.66f, 0.38f);
                light.intensity = 3.4f;
                light.range = 17f;
                light.shadows = LightShadows.None;
            }
        }

        // ------------------------------------------------------------------
        // Mascot sign
        // ------------------------------------------------------------------
        /// <summary>
        /// A giant painted clown's face on two posts: the park's mascot, and what the gate
        /// and the midway are known by from a long way off. Built from discs and cones --
        /// face, eyes, nose, smile, hat, ruff and tufts of hair -- and faded, with an eye
        /// gone dark, because a face that is entirely intact is a face somebody still
        /// repaints.
        /// </summary>
        private static void BuildMascotSign(Transform parent, int layer, Vector3 at, float yaw, float scale)
        {
            var sign = new GameObject("MascotSign").transform;
            sign.SetParent(parent, false);
            sign.position = at;
            sign.rotation = Quaternion.Euler(0f, yaw, 0f);

            float s = scale;
            float cy = 8.6f * s;

            for (int side = -1; side <= 1; side += 2)
            {
                var post = CreateBlock(sign, new Vector3(side * 3.4f * s, 4.2f * s, -0.45f * s),
                                       new Vector3(0.4f, 8.4f * s, 0.4f), 0f, layer, "Wood",
                                       Shade(_theme.bankColor, 0.6f), 0.05f, 0f, "SignPost", _parkTimber);
                NoStanding(post);
            }

            var face = new MeshBuild { UVScale = 0.3f };
            var red = new MeshBuild { UVScale = 0.3f };
            var hair = new MeshBuild { UVScale = 0.3f };
            var dark = new MeshBuild { UVScale = 0.3f };

            Vector3 P(float x, float y, float z) => new Vector3(x * s, cy + y * s, z * s);

            face.Tube(P(0f, 0f, -0.25f), P(0f, 0f, 0.25f), 3.1f * s, 3.1f * s, 28);
            hair.Tube(P(-3.0f, 1.2f, -0.1f), P(-3.0f, 1.2f, 0.4f), 1.1f * s, 1.1f * s, 14);
            hair.Tube(P(3.0f, 1.2f, -0.1f), P(3.0f, 1.2f, 0.4f), 1.1f * s, 1.1f * s, 14);
            hair.Tube(P(-3.6f, -0.2f, -0.1f), P(-3.6f, -0.2f, 0.3f), 0.8f * s, 0.8f * s, 12);
            hair.Tube(P(3.6f, -0.2f, -0.1f), P(3.6f, -0.2f, 0.3f), 0.8f * s, 0.8f * s, 12);

            // Eyes: one painted, one blacked out.
            dark.Tube(P(-1.1f, 0.9f, 0.2f), P(-1.1f, 0.9f, 0.4f), 0.5f * s, 0.5f * s, 12);
            dark.Tube(P(1.1f, 0.9f, 0.2f), P(1.1f, 0.9f, 0.45f), 0.62f * s, 0.62f * s, 12);
            red.Tube(P(-1.1f, 0.9f, 0.38f), P(-1.1f, 0.9f, 0.45f), 0.2f * s, 0.2f * s, 10);

            // Nose, and a smile of nine segments along a parabola.
            red.Tube(P(0f, -0.1f, 0.2f), P(0f, -0.1f, 0.9f), 0.62f * s, 0.2f * s, 14);
            for (int i = 0; i < 9; i++)
            {
                float t = i / 8f * 2f - 1f;
                float x = t * 1.9f;
                float y = -1.35f - 0.75f * (1f - t * t);
                red.Box(P(x, y, 0.3f), new Vector3(0.5f * s, 0.34f * s, 0.14f * s), Quaternion.Euler(0f, 0f, t * 28f));
            }

            // Hat and pom-pom.
            red.Tube(P(0f, 2.7f, -0.1f), P(0f, 6.4f, -0.1f), 2.25f * s, 0.15f * s, 16);
            face.Tube(P(0f, 6.4f, -0.1f), P(0f, 6.9f, -0.1f), 0.5f * s, 0.5f * s, 10);

            // The ruff beneath the chin.
            face.Tube(P(0f, -3.4f, -0.2f), P(0f, -3.1f, 0.2f), 2.6f * s, 3.4f * s, 20);
            red.Tube(P(0f, -3.0f, 0.2f), P(0f, -3.0f, 0.5f), 0.34f * s, 0.34f * s, 8);

            void Emit(string name, MeshBuild build, Material material)
            {
                var go = MeshObject(sign, name, ToMesh(build, DenseKey("mascot" + name)), material,
                                    Vector3.zero, Quaternion.identity, Vector3.one, layer, "Wood", collider: false);
                NoStanding(go);
                Hide(go);
            }

            Emit("Face", face, _fgSkin);
            Emit("Hat", red, _fgClownRed);
            Emit("Hair", hair, _fgClownHair);
            Emit("Eyes", dark, _parkDark);

            // A cold spot on it, so it can be read from the far end of the walkway.
            var go2 = new GameObject("MascotLight");
            go2.transform.SetParent(sign, false);
            go2.transform.localPosition = new Vector3(0f, cy, -5f * s);
            var light = go2.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.72f, 0.46f);
            light.intensity = 3.6f;
            light.range = 15f * s;
            light.shadows = LightShadows.None;
        }

        // ------------------------------------------------------------------
        // Bulbs on the rides
        // ------------------------------------------------------------------
        /// <summary>
        /// A ring of bulbs, in the plane of the given axes. A ride that has been switched
        /// off for ten years still has its bulbs, and the ones that survive are what make
        /// a silhouette against a dusk sky read as a ride rather than as scaffolding.
        /// </summary>
        private static void AddRideBulbs(Transform parent, int backdrop, string name, Vector3 centre,
                                         float radius, int count, bool vertical, System.Random rng)
        {
            var build = new MeshBuild { UVScale = 0.5f };

            for (int i = 0; i < count; i++)
            {
                if (rng.NextDouble() < 0.2) continue;

                float a = i * Mathf.PI * 2f / count;
                var p = vertical
                    ? centre + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f)
                    : centre + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);

                build.Box(p, new Vector3(0.28f, 0.28f, 0.28f), Quaternion.identity);
            }

            if (build.Triangles.Count == 0) return;

            var go = MeshObject(parent, name, ToMesh(build, DenseKey(name.ToLowerInvariant())), _fgBulb,
                                Vector3.zero, Quaternion.identity, Vector3.one, backdrop, null, collider: false);
            NoStanding(go);
            Hide(go);
        }
    }
}
#endif
