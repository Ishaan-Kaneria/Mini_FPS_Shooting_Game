#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The parts of the industrial zone the art pack does not contain.
    ///
    /// The pack is generous with sheds, tanks, containers and fencing and has none of
    /// the four things that actually make a plant fightable in three dimensions:
    /// <b>stairs</b>, <b>catwalks</b>, <b>railings</b> and <b>signage</b>. Those are
    /// built here, and they are the difference between a site you run around and a site
    /// you fight over -- every one of them exists to put a player above another player's
    /// head, or to tell them what the thing they are standing next to will do to them.
    ///
    /// <para>
    /// Everything long is welded into one mesh per run rather than left as a hundred
    /// small objects. A catwalk is forty boxes; as forty objects that is forty draw
    /// calls per walkway, which this game cannot afford in a browser tab, and as one
    /// mesh it is one. The same rule the canyon and the desert fence already follow.
    /// </para>
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        /// <summary>What a sign is warning about. Only the face colour and shape differ.</summary>
        private enum SignKind { Warning, Flammable, Hardhat, NoEntry }

        // ==================================================================
        // Stairs and catwalks
        // ==================================================================
        /// <summary>
        /// A flight of stairs up to a small landing, with railings.
        ///
        /// <b>Stairs rather than a ramp, and the reason is the agent.</b> A NavMeshAgent
        /// walks a ramp and will also walk a stair whose steps are shorter than its step
        /// height, so the steps here are 0.22m -- low enough that the bake joins them
        /// into one walkable surface and an enemy will actually follow the player up.
        /// Modelled as real treads rather than a tilted slab because a tilted slab is
        /// what the old platforms were and it reads as a skate ramp in a factory.
        /// </summary>
        private static void BuildStairTower(Transform parent, int layer, Vector3 at,
                                            float height, float yaw)
        {
            var build = new MeshBuild { UVScale = 0.5f };

            const float tread = 0.30f;
            const float rise = 0.22f;
            const float wide = 2.2f;

            int steps = Mathf.Max(2, Mathf.RoundToInt(height / rise));
            float run = steps * tread;

            for (int i = 0; i < steps; i++)
            {
                float y = (i + 0.5f) * rise;
                float z = -run * 0.5f + (i + 0.5f) * tread;
                build.Box(new Vector3(0f, y * 0.5f, z),
                          new Vector3(wide, (i + 1) * rise, tread), Quaternion.identity);
            }

            // The landing at the top, and the two railings up the flight.
            float top = steps * rise;
            build.Box(new Vector3(0f, top - 0.08f, run * 0.5f + 1.2f),
                      new Vector3(wide, 0.16f, 2.4f), Quaternion.identity);

            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (wide * 0.5f - 0.08f);

                // A rail that climbs with the flight, in short segments so it steps
                // rather than floats.
                for (int i = 0; i < steps; i += 2)
                {
                    float y = (i + 1) * rise + 1.0f;
                    float z = -run * 0.5f + (i + 1) * tread;
                    build.Box(new Vector3(x, y, z), new Vector3(0.07f, 0.07f, tread * 2.2f),
                              Quaternion.identity);
                    build.Box(new Vector3(x, y - 0.5f, z), new Vector3(0.06f, 1.0f, 0.06f),
                              Quaternion.identity);
                }

                build.Box(new Vector3(x, top + 0.5f, run * 0.5f + 1.2f),
                          new Vector3(0.07f, 1.0f, 2.4f), Quaternion.identity);
            }

            // The pool is keyed by shape, not by name: two flights of different heights
            // are different meshes, and sharing the key would silently give every tower
            // on the site the height of whichever one was built first.
            var go = MeshObject(parent, "StairTower", ToMesh(build, $"stairs{steps}"), _zoneSteel,
                                at, Quaternion.Euler(0f, yaw, 0f), Vector3.one, layer, "Metal");
            if (go != null) Mark(go, new Color(0.62f, 0.60f, 0.55f), 3);
        }

        /// <summary>
        /// An elevated walkway between two points, on legs, with grating, railings and a
        /// stair at one end.
        ///
        /// <b>This is the best firing position in the district and that is deliberate.</b>
        /// It looks down into a container lane or along a pipe run, it can only be reached
        /// by one stair, and it has a lip to shoot over -- so holding it is worth doing
        /// and taking it from somebody is a real problem to solve. An arena where the
        /// only axis is the ground plane is an arena where every fight is the same fight.
        /// </summary>
        private static void BuildCatwalk(Transform parent, int layer, Vector3 from, Vector3 to,
                                         float height)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 6f) return;

            float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            Vector3 mid = (from + to) * 0.5f;

            var build = new MeshBuild { UVScale = 0.6f };
            const float wide = 2.4f;

            // Deck.
            build.Box(new Vector3(0f, height, 0f), new Vector3(wide, 0.14f, length),
                      Quaternion.identity);

            // Legs, every six metres, in pairs.
            int legs = Mathf.Max(2, Mathf.RoundToInt(length / 6f));
            for (int i = 0; i <= legs; i++)
            {
                float z = -length * 0.5f + length * i / legs;
                for (int side = -1; side <= 1; side += 2)
                {
                    build.Box(new Vector3(side * (wide * 0.5f - 0.12f), height * 0.5f, z),
                              new Vector3(0.18f, height, 0.18f), Quaternion.identity);
                }
            }

            // Railings: a top rail, a knee rail and posts. The knee rail is what makes it
            // read as industrial rather than as a garden bridge.
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (wide * 0.5f - 0.06f);
                build.Box(new Vector3(x, height + 1.05f, 0f), new Vector3(0.07f, 0.07f, length),
                          Quaternion.identity);
                build.Box(new Vector3(x, height + 0.55f, 0f), new Vector3(0.06f, 0.06f, length),
                          Quaternion.identity);

                int posts = Mathf.Max(2, Mathf.RoundToInt(length / 2.5f));
                for (int i = 0; i <= posts; i++)
                {
                    float z = -length * 0.5f + length * i / posts;
                    build.Box(new Vector3(x, height + 0.55f, z),
                              new Vector3(0.07f, 1.1f, 0.07f), Quaternion.identity);
                }
            }

            var deck = MeshObject(parent, "Catwalk", ToMesh(build, $"catwalk{length:0}h{height:0.0}"),
                                  _zoneGrate, mid, Quaternion.Euler(0f, yaw, 0f), Vector3.one,
                                  layer, "Metal");

            // Drawn on the map above whatever it crosses, because a walkway over a
            // container lane is a different place from the lane and the map has to say so.
            if (deck != null) Mark(deck, new Color(0.70f, 0.68f, 0.60f), 6);

            BuildStairTower(parent, layer, from - delta.normalized * 2.0f, height,
                            yaw + 180f);
        }

        /// <summary>
        /// The low wall round a tank farm. Chest height, so it is cover from outside and
        /// something to shoot over from inside.
        /// </summary>
        private static void BuildBund(Transform parent, int layer, float halfX, float halfZ)
        {
            var build = new MeshBuild { UVScale = 0.7f };
            const float h = 1.35f, t = 0.5f;

            build.Box(new Vector3(0f, h * 0.5f, halfZ), new Vector3(halfX * 2f, h, t), Quaternion.identity);
            build.Box(new Vector3(0f, h * 0.5f, -halfZ), new Vector3(halfX * 2f, h, t), Quaternion.identity);
            build.Box(new Vector3(halfX, h * 0.5f, 0f), new Vector3(t, h, halfZ * 2f), Quaternion.identity);

            // The fourth side is left with a gap in it: a compound with no way in is a
            // compound the level has to route round rather than fight over.
            float gap = 5f;
            float side = (halfZ * 2f - gap) * 0.5f;
            build.Box(new Vector3(-halfX, h * 0.5f, halfZ - side * 0.5f),
                      new Vector3(t, h, side), Quaternion.identity);
            build.Box(new Vector3(-halfX, h * 0.5f, -halfZ + side * 0.5f),
                      new Vector3(t, h, side), Quaternion.identity);

            var go = MeshObject(parent, "Bund", ToMesh(build, $"bund{halfX:0}x{halfZ:0}"),
                                _zoneConcrete, Vector3.zero, Quaternion.identity, Vector3.one,
                                layer, "Concrete");
            if (go != null)
            {
                Mark(go, new Color(0.55f, 0.54f, 0.50f), 2);

                // The cap is half a metre wide and dead flat, so the bake would happily
                // lay navmesh along the top of it -- a closed loop of walkable ground
                // 1.35m up with no stair to it. Nothing can path there, but LevelManager
                // samples the mesh near the player to place a spawn and does not care,
                // so it would eventually stand an enemy on the wall for the rest of the
                // level.
                NoStanding(go);
            }
        }

        /// <summary>
        /// A run of pipes on trestles with a walkway over the top of it. The pipes are
        /// waist high, so the run itself is a long piece of cover, and the walkway above
        /// is the position that beats it.
        /// </summary>
        private static void BuildPipeRun(Transform parent, int layer, System.Random rng,
                                         Vector3 at, float length)
        {
            var build = new MeshBuild { UVScale = 0.5f };

            // Three pipes side by side, on trestles.
            for (int p = 0; p < 3; p++)
            {
                float y = 1.05f + p * 0.02f;
                float x = -0.85f + p * 0.85f;
                build.Box(new Vector3(x, y, 0f), new Vector3(0.62f, 0.62f, length),
                          Quaternion.identity);
            }

            int trestles = Mathf.Max(2, Mathf.RoundToInt(length / 7f));
            for (int i = 0; i <= trestles; i++)
            {
                float z = -length * 0.5f + length * i / trestles;
                build.Box(new Vector3(0f, 0.45f, z), new Vector3(2.6f, 0.9f, 0.3f),
                          Quaternion.identity);
            }

            var run = MeshObject(parent, "PipeRun", ToMesh(build, $"pipes{length:0}"),
                                 _zoneRust, at, Quaternion.identity, Vector3.one, layer, "Metal");
            if (run != null)
            {
                Mark(run, new Color(0.50f, 0.36f, 0.28f), 2);

                // Square pipes have flat tops. The walkway above is the way over this,
                // not the pipes themselves.
                NoStanding(run);
            }

            BuildCatwalk(parent, layer,
                         at + new Vector3(0f, 0f, -length * 0.5f),
                         at + new Vector3(0f, 0f, length * 0.5f), 4.2f);
        }

        // ==================================================================
        // Signage
        // ==================================================================
        /// <summary>
        /// A warning sign on a post.
        ///
        /// Signs are the cheapest industrial cue there is: a yellow triangle at the mouth
        /// of a tank farm does more to say "chemical plant" than another ten tanks would,
        /// and unlike another ten tanks it costs two triangles and no navigation. They
        /// also do a job for the player, which is to make the districts tell each other
        /// apart at a distance.
        /// </summary>
        private static void BuildSign(Transform parent, int layer, Vector3 at, float yaw,
                                      SignKind kind)
        {
            var build = new MeshBuild { UVScale = 1f };

            // Post.
            build.Box(new Vector3(0f, 1.1f, 0f), new Vector3(0.1f, 2.2f, 0.1f), Quaternion.identity);

            var post = MeshObject(parent, "SignPost", ToMesh(build, "signpost"), _zoneSteel,
                                  at, Quaternion.Euler(0f, yaw, 0f), Vector3.one, layer, "Metal");

            var face = new MeshBuild { UVScale = 1f };
            const float s = 0.62f;

            if (kind == SignKind.Warning || kind == SignKind.Flammable)
            {
                // A triangle, standing on its base.
                face.Tri(new Vector3(-s, 1.85f, 0.06f), new Vector3(s, 1.85f, 0.06f),
                         new Vector3(0f, 1.85f + s * 1.7f, 0.06f));
                face.Tri(new Vector3(s, 1.85f, -0.06f), new Vector3(-s, 1.85f, -0.06f),
                         new Vector3(0f, 1.85f + s * 1.7f, -0.06f));
            }
            else
            {
                face.Box(new Vector3(0f, 2.3f, 0f), new Vector3(s * 1.9f, s * 1.5f, 0.08f),
                         Quaternion.identity);
            }

            Material tint = kind == SignKind.Flammable ? _zoneHazard
                          : kind == SignKind.NoEntry ? _zoneRust : _zoneSign;

            var plate = MeshObject(parent, $"Sign_{kind}", ToMesh(face, $"sign{kind}"), tint,
                                   at, Quaternion.Euler(0f, yaw, 0f), Vector3.one, layer, "Metal",
                                   collider: false);

            // Neither part is worth a footprint on a map of a 500m site.
            Hide(post);
            Hide(plate);
        }

        // ==================================================================
        // Perimeter
        // ==================================================================
        /// <summary>
        /// The site boundary: concrete fence all the way round, with a gate wherever a
        /// road reaches the edge.
        ///
        /// <b>The fence is what makes the arena a place rather than a plane that stops.</b>
        /// It is also the only thing keeping the player on the navmesh, so it is solid
        /// for its whole length -- a gap in it is a way to walk out of the level and be
        /// discarded by the leash, which presents as the game deleting you.
        /// The gates are shut; they are there to be recognised, not used.
        /// </summary>
        private static void BuildPerimeterFence(Transform root, int layer, float half)
        {
            var fence = new GameObject("Perimeter").transform;
            fence.SetParent(root, false);

            const float module = 5.7f;           // the pack's fence section, measured
            float edge = half - 4f;
            int per = Mathf.Max(2, Mathf.RoundToInt(edge * 2f / module));

            // Which tiles the roads touch the edge at, so a gate can go there instead of
            // a panel.
            var gateAt = new HashSet<int>();
            foreach (var tile in _roadTiles)
            {
                if (Mathf.Abs(tile.x * Tile) > edge - Tile) gateAt.Add(Mathf.RoundToInt(tile.y * Tile));
                if (Mathf.Abs(tile.y * Tile) > edge - Tile) gateAt.Add(Mathf.RoundToInt(tile.x * Tile));
            }

            for (int side = 0; side < 4; side++)
            {
                for (int i = 0; i < per; i++)
                {
                    float along = -edge + module * (i + 0.5f);
                    if (along > edge) break;

                    Vector3 at;
                    float yaw;

                    switch (side)
                    {
                        case 0: at = new Vector3(along, 0f, edge); yaw = 90f; break;
                        case 1: at = new Vector3(along, 0f, -edge); yaw = 90f; break;
                        case 2: at = new Vector3(edge, 0f, along); yaw = 0f; break;
                        default: at = new Vector3(-edge, 0f, along); yaw = 0f; break;
                    }

                    bool gate = false;
                    foreach (int g in gateAt)
                    {
                        if (Mathf.Abs(along - g) < module * 0.75f) { gate = true; break; }
                    }

                    Place(fence, gate ? FenceGate : FenceRun, at, yaw);
                }
            }

            // Corners, and a guard position at each one looking down two sides.
            float c = edge;
            Place(fence, FenceCorner, new Vector3(c, 0f, c), 0f);
            Place(fence, FenceCorner, new Vector3(-c, 0f, c), 90f);
            Place(fence, FenceCorner, new Vector3(-c, 0f, -c), 180f);
            Place(fence, FenceCorner, new Vector3(c, 0f, -c), 270f);

            BuildSign(fence, layer, new Vector3(0f, 0f, edge - 3f), 180f, SignKind.NoEntry);
            BuildSign(fence, layer, new Vector3(0f, 0f, -edge + 3f), 0f, SignKind.NoEntry);

            // A solid backstop behind the fence. The pack's panels are 2.6m and a player
            // who gets on a container beside one can see over it into nothing, so this is
            // the piece that actually bounds the level.
            var wall = new MeshBuild { UVScale = 1.2f };
            const float wh = 9f;
            wall.Box(new Vector3(0f, wh * 0.5f, half), new Vector3(half * 2f, wh, 1f), Quaternion.identity);
            wall.Box(new Vector3(0f, wh * 0.5f, -half), new Vector3(half * 2f, wh, 1f), Quaternion.identity);
            wall.Box(new Vector3(half, wh * 0.5f, 0f), new Vector3(1f, wh, half * 2f), Quaternion.identity);
            wall.Box(new Vector3(-half, wh * 0.5f, 0f), new Vector3(1f, wh, half * 2f), Quaternion.identity);

            var bound = MeshObject(fence, "Boundary", ToMesh(wall, "zonebound"), _zoneConcrete,
                                   Vector3.zero, Quaternion.identity, Vector3.one, layer, "Concrete");
            if (bound != null) { Hide(bound); NoStanding(bound); }
        }

        // ==================================================================
        // Horizon
        // ==================================================================
        /// <summary>
        /// The skyline past the fence: chimneys, gasometers and shed roofs, with no
        /// colliders and off the navigation bake.
        ///
        /// A plant that ends at its own fence is a plant in a diorama. These are far
        /// enough out that they never read as somewhere to walk to, and they are what
        /// makes the site look like part of an industrial district rather than the only
        /// building in a field.
        /// </summary>
        private static void BuildZoneBackdrop(Transform root, int layer, System.Random rng,
                                              float half)
        {
            var group = new GameObject("Skyline").transform;
            group.SetParent(root, false);

            int count = Mathf.Max(12, _theme.backdropCount);
            var build = new MeshBuild { UVScale = 4f };

            for (int i = 0; i < count; i++)
            {
                float angle = (i / (float)count) * Mathf.PI * 2f + Rand(rng, -0.05f, 0.05f);
                float distance = Rand(rng, half + 180f, half + 620f);

                Vector3 at = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);

                // Either a chimney or a block. Chimneys are what say "industry" from a
                // kilometre away, so roughly one in three is one.
                if (i % 3 == 0)
                {
                    float h = Rand(rng, 60f, 140f);
                    build.Box(at + new Vector3(0f, h * 0.5f, 0f),
                              new Vector3(Rand(rng, 7f, 13f), h, Rand(rng, 7f, 13f)),
                              Quaternion.Euler(0f, Rand(rng, 0f, 90f), 0f));
                }
                else
                {
                    float h = Rand(rng, 22f, 70f);
                    build.Box(at + new Vector3(0f, h * 0.5f, 0f),
                              new Vector3(Rand(rng, 40f, 130f), h, Rand(rng, 30f, 90f)),
                              Quaternion.Euler(0f, Rand(rng, 0f, 90f), 0f));
                }
            }

            var go = MeshObject(group, "Silhouettes", ToMesh(build, "zoneskyline"), _zoneConcrete,
                                Vector3.zero, Quaternion.identity, Vector3.one, layer, null,
                                collider: false);
            if (go != null) { NoStanding(go); Hide(go); }
        }

        // ==================================================================
        // Spawns
        // ==================================================================
        /// <summary>
        /// Enemy spawn points, spread over the site and kept off the roads and out of the
        /// buildings.
        ///
        /// They are snapped onto the navmesh afterwards by
        /// <see cref="SnapSpawnPointsToNavMesh"/>, which is the only moment the answer
        /// exists -- a point placed geometrically inside a shed wall is a point the leash
        /// discards the instant an enemy arrives on it, and that presents as the level
        /// spawning fewer enemies than it says.
        /// </summary>
        private static Transform[] BuildZoneSpawnPoints()
        {
            var root = new GameObject("SpawnPoints").transform;
            var list = new List<Transform>();

            float half = _theme.arenaSize * 0.5f;
            const int count = 16;

            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                float radius = half * (0.34f + (i % 4) * 0.15f);

                float x = Mathf.Clamp(Mathf.Cos(angle) * radius, -half * 0.88f, half * 0.88f);
                float z = Mathf.Clamp(Mathf.Sin(angle) * radius, -half * 0.88f, half * 0.88f);

                var point = new GameObject($"Spawn_{i:00}").transform;
                point.SetParent(root, false);
                point.position = new Vector3(x, 0.1f, z);
                list.Add(point);
            }

            return list.ToArray();
        }

        /// <summary>
        /// Welds an accumulated build into a mesh, reusing one already made for the same
        /// key. Repeated parts -- every catwalk of the same length, every sign of the
        /// same kind -- are one mesh shared, which is the whole reason the pool exists.
        /// </summary>
        private static Mesh ToMesh(MeshBuild build, string key)
        {
            return Pooled(key, () =>
            {
                var mesh = new Mesh { name = key };
                if (build.Vertices.Count > 65000) mesh.indexFormat =
                    UnityEngine.Rendering.IndexFormat.UInt32;

                mesh.SetVertices(build.Vertices);
                mesh.SetNormals(build.Normals);
                mesh.SetUVs(0, build.UVs);
                mesh.SetTriangles(build.Triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            });
        }
    }
}
#endif
