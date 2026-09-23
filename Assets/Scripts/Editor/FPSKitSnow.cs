#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The frozen field: rolling snow, a lake you can walk across, igloo camps you can
    /// walk into, and pressure ridges you have to walk around.
    ///
    /// <b>One continuous walkable surface with things standing on it</b>, which is the
    /// lesson the park already paid for: a layout with levels in it is a layout with
    /// joins, and every join is somewhere `NavMeshSurface` can fail to connect. Nothing
    /// here is above anything else. The ground is the desert's heightfield with a gentler
    /// hand on it -- same relaxation, same smoothing, so it rolls the way ground does
    /// rather than the way noise does.
    ///
    /// Four things about it are worth not re-deriving.
    ///
    /// <b>The lake is the arena's one decision.</b> Everything else out here is soft,
    /// undulating and full of things to stand behind; the lake is sixty metres of flat,
    /// hard, bright, completely open ground with a clear shot across all of it. Crossing
    /// it is quick and it is the only place in the arena where there is nowhere to be.
    /// It is a pad pinned <i>below</i> the natural height rather than a disc laid on top,
    /// so the ground falls to it the way a shoreline does and the sheet has no lip to
    /// catch an agent on.
    ///
    /// <b>An igloo is a shell with a door, not a dome on the ground.</b> A solid hemisphere
    /// is a rock; what makes an igloo an igloo is that you can get inside it, and what
    /// makes getting inside it work is that the doorway is wide enough for a NavMesh agent
    /// as well as for the player. At 2.4m wide and 2.3m tall it admits both, so enemies
    /// fight over the inside of one instead of standing outside it forever -- which is
    /// what a too-small door produces, silently, with the floor inside baked and reachable
    /// by nothing. The tunnel is a second shell extruded off the door, for the same reason
    /// a real one has one: it is a porch that stops the wind, and it makes the entrance
    /// readable from across the field as an entrance rather than as a dark patch.
    ///
    /// <b>Snow and ice are two tags, not one.</b> They are the two surfaces the whole
    /// level is spent on and they sound nothing alike -- a boot in snow is a muffled
    /// crunch, a boot on lake ice is a hard knock with a ring under it. One tag for both
    /// would make the lake sound exactly like the drift beside it, which is most of what
    /// would tell a player they had just stepped onto something different.
    ///
    /// <b>The snow falls on the player, not on the arena.</b> See <see cref="Snowfall"/>:
    /// a four-hundred-metre field of particles costs hundreds of thousands of them to put
    /// a few hundred where the eye can resolve one.
    /// </summary>
    public partial class FPSKitSceneBuilder
    {
        private struct Camp
        {
            public Vector2 Centre;
            public float Radius;
            public int Domes;
        }

        private static readonly List<Camp> _camps = new List<Camp>();

        private static Vector2 _lakeAt;
        private static float _lakeRadius;
        private static float _lakeY;

        private static Material _snowMat, _iceMat, _iceBlockMat, _campTimberMat, _packedMat;

        /// <summary>
        /// How deep the lake basin sits below the ground around it. Shallow: it is a
        /// frozen lake, not a crater, and a rim the player has to climb out of would turn
        /// the one open place in the arena into a trap.
        /// </summary>
        private const float LakeDrop = 2.6f;

        /// <summary>Outer radius of an igloo dome, in metres.</summary>
        private const float IglooRadius = 6.2f;

        /// <summary>And how thick its wall is. Blocks of snow are not thin.</summary>
        private const float IglooWall = 0.75f;

        // ==================================================================
        private static void BuildSnowZone()
        {
            _claimed.Clear();
            _anchors.Clear();
            _camps.Clear();

            ClearMeshPool();
            ResetTerrain();

            var root = new GameObject("Arena").transform;
            int layer = LayerMask.NameToLayer("Environment");
            int backdrop = LayerMask.NameToLayer("Backdrop");
            if (backdrop < 0) backdrop = layer;

            var rng = new System.Random(_theme.randomSeed);
            float half = _theme.arenaSize * 0.5f;

            ResolveSurfaceMaterials();
            BuildSurfaceTextures();
            ResolveOutdoorMaterials();
            ResolveSnowMaterials();

            // The spawn, before anything can be planned on top of it.
            Claim(0f, 0f, 30f);
            _anchors.Add(Vector3.zero);
            FlattenPad(0f, 0f, 20f, 30f);

            // ---- planning: every flat thing reserves its ground before the ground
            // ---- exists. The Plan and Build passes must stay in the same order as each
            // ---- other, because the second reads the list the first wrote.
            PlanLake(rng, half);
            PlanCamps(rng, half);

            // The station, the crevasses, the caves and the icefall (FPSKitSnowLife).
            PlanSnowLife(half);

            // ---- ground ----
            BuildDuneField(root, layer, half);
            BuildApron(root, backdrop, half);
            BuildBoundary(root, layer, half);
            BuildBackdrop(root, backdrop, rng, half);

            // ---- what stands on it ----
            BuildLake(root, layer);
            BuildCamps(root, layer, rng);
            BuildPressureRidges(root, layer, rng, half);
            BuildSnowScatter(root, layer, rng, half);
            BuildSnowLife(root, layer, backdrop, half);
            BuildSnowfall(root);

            // The same call and the same numbers the open zone and the park use. Inside
            // the boundary ridge's toe rather than level with it: at the toe exactly, the
            // strip of lower slope between the flat ground and the seal bakes and is cut
            // off from everything.
            SealNavMeshOutside(root, half - BermToe - 8f, half + _theme.apronSize);
        }

        // ==================================================================
        // Planning
        // ==================================================================

        /// <summary>
        /// Puts the lake somewhere off-centre and reserves a basin for it.
        ///
        /// Off-centre on purpose: in the middle it is unavoidable, and the whole point of
        /// it is that crossing is a choice. The pad is pinned below the natural height, so
        /// the surrounding drifts fall towards it and the shore is a slope rather than a
        /// step -- a step at the edge of a sheet this big is a lip that stops an agent
        /// dead all the way round.
        /// </summary>
        private static void PlanLake(System.Random rng, float half)
        {
            _lakeRadius = Mathf.Max(20f, _theme.lakeRadius);

            float away = half * 0.42f;
            float angle = Rand(rng, 0f, Mathf.PI * 2f);

            _lakeAt = new Vector2(Mathf.Cos(angle) * away, Mathf.Sin(angle) * away);

            // The blend is the shoreline. Wide, because a lake with a sharp edge reads as
            // a disc dropped on the field.
            float blend = _lakeRadius * 0.55f;

            _lakeY = NaturalHeightAt(_lakeAt.x, _lakeAt.y) - LakeDrop;
            FlattenPad(_lakeAt.x, _lakeAt.y, _lakeRadius, blend, _lakeY);

            Claim(_lakeAt.x, _lakeAt.y, _lakeRadius + blend);
            _anchors.Add(new Vector3(_lakeAt.x, 0f, _lakeAt.y));
        }

        /// <summary>
        /// Chooses the camps and flattens a pad under each.
        ///
        /// A dome is a rigid thing and there is no version of one that follows a hill, so
        /// the pad is reserved first like every other flat site in this kit. The pad
        /// reaches radius plus blend, and that whole reach is what gets claimed -- claiming
        /// only the flat part lets a later pad's falloff lap over this one, and the nearer
        /// pad wins outright. That is the bug the desert and the park have each recorded
        /// once already.
        /// </summary>
        private static void PlanCamps(System.Random rng, float half)
        {
            int wanted = Mathf.Max(0, _theme.iglooCamps);

            for (int i = 0; i < wanted; i++)
            {
                int domes = 2 + rng.Next(2);
                float radius = 13f + domes * 3.5f;
                float reach = radius + 12f;

                if (!TryClaim(rng, half * 0.82f, reach, out var point, 40)) continue;

                FlattenPad(point.x, point.y, radius, 12f);

                _camps.Add(new Camp { Centre = point, Radius = radius, Domes = domes });
                _anchors.Add(new Vector3(point.x, 0f, point.y));
            }
        }

        // ==================================================================
        // The lake
        // ==================================================================

        private static void BuildLake(Transform root, int layer)
        {
            var group = new GameObject("FrozenLake").transform;
            group.SetParent(root, false);

            // <b>Ask the terrain, do not trust the plan.</b> _lakeY is what the pad was
            // *asked* for, computed from the natural height before the field existed; the
            // height the ground actually ends up at is what comes back out of the
            // relaxation and the smoothing afterwards, and it is a fraction of a metre
            // higher. Laid at the planned height the sheet was under the snow -- sixty
            // metres of ice, built, collidable, tagged, and invisible, with only the
            // shards around its rim showing that anything was there at all. Everything in
            // this kit that places something on the ground asks GroundHeightAt for
            // exactly this reason.
            float surface = GroundHeightAt(_lakeAt.x, _lakeAt.y) + 0.08f;

            // Laid a hair over the flattened ground rather than at it exactly: coplanar
            // surfaces z-fight, and the one place that shows worst is a large flat sheet
            // seen at a grazing angle, which is exactly what this is.
            var sheet = MeshObject(group, "Ice", LakeMesh(), _iceMat,
                                   new Vector3(_lakeAt.x, surface, _lakeAt.y),
                                   Quaternion.identity, Vector3.one, layer, IceTag);

            // The map draws what it is given, and a sixty-metre disc of ice is worth
            // drawing -- it is the one feature a player navigates this arena by.
            Mark(sheet, new Color(0.62f, 0.80f, 0.95f, 0.75f), 10);

            // Blocks shoved up where the sheet has buckled against the shore. They are
            // what stops the lake being a perfect circle of nothing, and they are the only
            // cover on it.
            var rng = new System.Random(_theme.randomSeed ^ 0x51CE);
            int shards = 14 + rng.Next(8);

            for (int i = 0; i < shards; i++)
            {
                float angle = Rand(rng, 0f, Mathf.PI * 2f);
                float away = _lakeRadius * Rand(rng, 0.55f, 0.94f);

                float x = _lakeAt.x + Mathf.Cos(angle) * away;
                float z = _lakeAt.y + Mathf.Sin(angle) * away;

                float w = Rand(rng, 1.6f, 3.4f);
                float h = Rand(rng, 1.1f, 2.6f);

                var block = CreateBlock(group, new Vector3(x, GroundHeightAt(x, z) + h * 0.32f, z),
                                        new Vector3(w, h, w * Rand(rng, 0.5f, 0.9f)),
                                        Rand(rng, 0f, 360f), layer, IceTag,
                                        Color.white, 0.6f, 0f, $"Shard_{i}", _iceBlockMat);

                // Tilted hard, so it is a slab that heaved rather than a crate, and so
                // that nothing tries to bake a walkable surface on top of it.
                block.transform.localRotation = Quaternion.Euler(Rand(rng, 18f, 46f),
                                                                 Rand(rng, 0f, 360f),
                                                                 Rand(rng, -14f, 14f));
                NoStanding(block);
            }
        }

        /// <summary>
        /// The sheet: a fan of triangles with a wobbly rim, so the shoreline is not a
        /// circle drawn with a compass.
        /// </summary>
        private static Mesh LakeMesh() => Pooled($"lake_{_lakeRadius:0.0}", () =>
        {
            var build = new MeshBuild { UVScale = 0.08f };

            const int Sides = 72;
            var rim = new Vector3[Sides];

            for (int i = 0; i < Sides; i++)
            {
                float t = i / (float)Sides * Mathf.PI * 2f;

                // Three harmonics of wobble. One is an egg, two is a peanut; three stops
                // the eye finding the pattern.
                float r = _lakeRadius * (1f
                    + 0.055f * Mathf.Sin(t * 3f + 0.7f)
                    + 0.035f * Mathf.Sin(t * 5f + 2.1f)
                    + 0.020f * Mathf.Sin(t * 8f + 4.4f));

                rim[i] = new Vector3(Mathf.Cos(t) * r, 0f, Mathf.Sin(t) * r);
            }

            // <b>Wound clockwise in XZ, which is anticlockwise seen from above.</b>
            // The rim is generated as (cos t, 0, sin t), and a fan taken round it in that
            // order has a normal of cross(a, b) -- which points straight *down*. What that
            // produces is a sixty-metre sheet that is built, collidable, tagged, walkable
            // and invisible, because every triangle in it is a backface from the only side
            // anybody ever sees it from. The shards round its rim still drew, so from
            // above the lake read as an empty ring of ice blocks lying on snow.
            for (int i = 0; i < Sides; i++)
                build.Tri(Vector3.zero, rim[(i + 1) % Sides], rim[i]);

            return build.ToMesh("FrozenLake");
        });

        // ==================================================================
        // The camps
        // ==================================================================

        private static void BuildCamps(Transform root, int layer, System.Random rng)
        {
            if (_camps.Count == 0) return;

            var group = new GameObject("Camps").transform;
            group.SetParent(root, false);

            for (int c = 0; c < _camps.Count; c++)
            {
                var camp = _camps[c];
                var here = new GameObject($"Camp_{c}").transform;
                here.SetParent(group, false);

                float spin = Rand(rng, 0f, 360f);

                for (int d = 0; d < camp.Domes; d++)
                {
                    float angle = spin + d * (360f / camp.Domes) + Rand(rng, -14f, 14f);
                    float away = camp.Radius * Rand(rng, 0.42f, 0.72f);

                    float x = camp.Centre.x + Mathf.Cos(angle * Mathf.Deg2Rad) * away;
                    float z = camp.Centre.y + Mathf.Sin(angle * Mathf.Deg2Rad) * away;

                    // Facing the middle of its own camp, so the doors look at each other
                    // and the camp reads as a place people lived rather than as domes
                    // dropped on a circle.
                    float facing = Mathf.Atan2(camp.Centre.y - z, camp.Centre.x - x) * Mathf.Rad2Deg;

                    Igloo(here, layer, rng, new Vector3(x, GroundHeightAt(x, z), z), -facing + 90f, d);
                }

                Windbreak(here, layer, rng, camp);
            }
        }

        /// <summary>
        /// One igloo: a domed shell with an arched door, and a tunnel out of it.
        ///
        /// <b>Both pieces are held off the bake</b> -- a dome and a barrel vault are both
        /// shallow enough at the top for a NavMeshSurface to call them walkable, and what
        /// that produces is a curved island of navmesh over every igloo in the arena for
        /// the spawner to find. The floor inside is the terrain, which bakes normally and
        /// connects through the door.
        /// </summary>
        private static void Igloo(Transform parent, int layer, System.Random rng, Vector3 at,
                                  float yaw, int index)
        {
            float scale = Rand(rng, 0.88f, 1.12f);
            var rotation = Quaternion.Euler(0f, yaw, 0f);

            // Sunk very slightly, so the rim of the shell meets the snow instead of
            // standing on it -- the ground here is flattened, so a few centimetres is all
            // it takes and any more would eat the doorway.
            var seat = at + Vector3.down * 0.15f;

            var dome = MeshObject(parent, $"Igloo_{index}", IglooMesh(), _snowMat,
                                  seat, rotation, Vector3.one * scale, layer, SnowTag);
            NoStanding(dome);

            var tunnel = MeshObject(parent, $"IglooTunnel_{index}", IglooTunnelMesh(), _snowMat,
                                    seat, rotation, Vector3.one * scale, layer, SnowTag);
            NoStanding(tunnel);

            // A drift banked against the back of it, which is what actually says "this has
            // been here all winter".
            var behind = rotation * Vector3.back;
            var drift = MeshObject(parent, $"IglooDrift_{index}",
                                   BoulderMesh(_theme.randomSeed + index * 31, 0.28f, 0.34f),
                                   _snowMat,
                                   at + behind * (IglooRadius * scale * 0.72f) + Vector3.down * 1.1f,
                                   Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f),
                                   new Vector3(3.4f, 1.5f, 2.6f) * scale, layer, SnowTag);
            NoStanding(drift);
        }

        /// <summary>
        /// The dome: an outer shell, an inner shell, a rim that joins them, and an arched
        /// hole at the front with its own jamb.
        ///
        /// Built as a shell rather than as a solid because the inside is a room. A solid
        /// hemisphere with a hole punched in it shows the *back* of its own far wall
        /// through the door -- every face of a closed mesh points outward, so from inside
        /// there is nothing drawn at all, and the player walks into what looks like a hole
        /// in the world. That is the same fault the desert's inside-out buttes had, arrived
        /// at from the opposite direction.
        /// </summary>
        private static Mesh IglooMesh() => Pooled("igloo", () =>
        {
            var build = new MeshBuild { UVScale = 0.55f };

            const int Rings = 10;     // top to base
            const int Sides = 28;     // around

            float inner = IglooRadius - IglooWall;

            // The doorway, as a half-angle about the +Z axis and a height up the dome.
            float doorHalf = Mathf.Atan2(1.2f, IglooRadius);
            const float DoorTop = 2.3f;

            bool InDoor(float theta, float y)
            {
                float offset = Mathf.Abs(Mathf.DeltaAngle(theta * Mathf.Rad2Deg, 0f)) * Mathf.Deg2Rad;
                return offset <= doorHalf && y <= DoorTop;
            }

            Vector3 On(float phi, float theta, float radius)
                => new Vector3(Mathf.Sin(phi) * Mathf.Sin(theta) * radius,
                               Mathf.Cos(phi) * radius,
                               Mathf.Sin(phi) * Mathf.Cos(theta) * radius);

            for (int r = 0; r < Rings; r++)
            {
                float p0 = r / (float)Rings * (Mathf.PI * 0.5f);
                float p1 = (r + 1) / (float)Rings * (Mathf.PI * 0.5f);

                for (int s = 0; s < Sides; s++)
                {
                    float t0 = s / (float)Sides * Mathf.PI * 2f;
                    float t1 = (s + 1) / (float)Sides * Mathf.PI * 2f;

                    var a = On(p0, t0, IglooRadius);
                    var b = On(p0, t1, IglooRadius);
                    var c = On(p1, t1, IglooRadius);
                    var d = On(p1, t0, IglooRadius);

                    bool cut = InDoor(t0, d.y) || InDoor(t1, c.y);

                    if (!cut)
                    {
                        // <b>a-d-c-b, not a-b-c-d.</b> Worked out on paper rather than by
                        // eye, because this is the failure that does not look like one:
                        // wound the other way every face of the dome points at its own
                        // axis, the near wall is culled, and what is drawn is the inside
                        // of the far wall lit from behind -- a perfectly convincing dome
                        // with no lit face on it at any time of day, which reads as snow
                        // in shadow rather than as a rendering fault. The desert's buttes
                        // shipped like that and cost a whole debugging session.
                        build.Quad(a, d, c, b);

                        var ai = On(p0, t0, inner);
                        var bi = On(p0, t1, inner);
                        var ci = On(p1, t1, inner);
                        var di = On(p1, t0, inner);

                        // And the inner shell is the outer one's mirror, so it takes the
                        // winding the outer one just gave up.
                        build.Quad(ai, bi, ci, di);
                    }
                    else
                    {
                        // The jamb: the wall's own thickness, shown where it has been cut
                        // through. Without it the doorway is a paper edge and the igloo
                        // reads as a shell rather than as blocks of snow.
                        var ao = a; var doo = d;
                        var ai2 = On(p0, t0, inner); var di2 = On(p1, t0, inner);

                        build.Quad(ao, doo, di2, ai2);
                    }
                }
            }

            // The base rim, joining the two shells all the way round except across the
            // door.
            for (int s = 0; s < Sides; s++)
            {
                float t0 = s / (float)Sides * Mathf.PI * 2f;
                float t1 = (s + 1) / (float)Sides * Mathf.PI * 2f;

                if (InDoor(t0, 0f) || InDoor(t1, 0f)) continue;

                var ao = On(Mathf.PI * 0.5f, t0, IglooRadius);
                var bo = On(Mathf.PI * 0.5f, t1, IglooRadius);
                var ai = On(Mathf.PI * 0.5f, t0, inner);
                var bi = On(Mathf.PI * 0.5f, t1, inner);

                build.Quad(ai, bi, bo, ao);
            }

            return build.ToMesh("Igloo");
        });

        /// <summary>
        /// The entrance tunnel: a barrel vault pushed out of the doorway, open at both
        /// ends, with its own wall thickness.
        ///
        /// It is what makes the door readable from across the field. A hole in a white
        /// dome at two hundred metres is a smudge; a porch with a shadow under it is a
        /// door, and a player who can see where the way in is will use it.
        /// </summary>
        private static Mesh IglooTunnelMesh() => Pooled("iglooTunnel", () =>
        {
            var build = new MeshBuild { UVScale = 0.5f };

            const float Length = 3.4f;
            const float Half = 1.25f;      // inner half-width
            const float Height = 2.3f;     // inner height at the crown
            const int Arc = 12;

            float wall = 0.55f;
            float z0 = IglooRadius - 1.2f;
            float z1 = z0 + Length;

            Vector3 P(float t, float z, float grow)
            {
                float angle = t * Mathf.PI;               // 0 = right, pi = left
                return new Vector3(Mathf.Cos(angle) * (Half + grow),
                                   Mathf.Sin(angle) * (Height + grow),
                                   z);
            }

            for (int i = 0; i < Arc; i++)
            {
                float t0 = i / (float)Arc;
                float t1 = (i + 1) / (float)Arc;

                // Outside. Same correction the dome needed and for the same reason --
                // a vault wound inward is a vault with no lit face on it.
                build.Quad(P(t1, z0, wall), P(t1, z1, wall), P(t0, z1, wall), P(t0, z0, wall));

                // Inside, which therefore takes the winding the outside gave up.
                build.Quad(P(t0, z0, 0f), P(t0, z1, 0f), P(t1, z1, 0f), P(t1, z0, 0f));

                // The open mouth at the far end, showing the wall's thickness
                build.Quad(P(t0, z1, 0f), P(t0, z1, wall), P(t1, z1, wall), P(t1, z1, 0f));
            }

            return build.ToMesh("IglooTunnel");
        });

        /// <summary>
        /// A run of stacked snow blocks on the windward side of a camp. Cover, and the
        /// thing that says which way the weather comes from.
        /// </summary>
        private static void Windbreak(Transform parent, int layer, System.Random rng, Camp camp)
        {
            float facing = Rand(rng, 0f, 360f);
            int blocks = 5 + rng.Next(4);

            var along = new Vector3(Mathf.Cos(facing * Mathf.Deg2Rad), 0f,
                                    Mathf.Sin(facing * Mathf.Deg2Rad));

            var out_ = new Vector3(-along.z, 0f, along.x);
            var start = new Vector3(camp.Centre.x, 0f, camp.Centre.y)
                        + out_ * camp.Radius * 0.85f
                        - along * (blocks * 0.5f * 2.2f);

            for (int i = 0; i < blocks; i++)
            {
                var at = start + along * (i * 2.2f);
                float h = Rand(rng, 1.1f, 1.7f);
                at.y = GroundHeightAt(at.x, at.z) + h * 0.5f - 0.15f;

                var block = CreateBlock(parent, at, new Vector3(2.1f, h, 0.9f),
                                        facing + Rand(rng, -6f, 6f), layer, SnowTag,
                                        Color.white, 0.12f, 0f, $"Windbreak_{i}", _packedMat);

                // Chest high and flat on top, with no way up onto a two-metre run of it.
                NoStanding(block);
            }
        }

        // ==================================================================
        // Ice, rock and the ground between them
        // ==================================================================

        /// <summary>
        /// Pressure ridges: lines of slabs heaved up where the field has moved against
        /// itself.
        ///
        /// They are the arena's long cover, and they are the reason it does not read as a
        /// flat white plain with props on it. Steeply tilted on purpose -- a slab lying
        /// shallow is a walkable surface a NavMeshSurface will happily bake an island on.
        /// </summary>
        private static void BuildPressureRidges(Transform root, int layer, System.Random rng,
                                                float half)
        {
            var group = new GameObject("PressureRidges").transform;
            group.SetParent(root, false);

            int runs = 7 + rng.Next(4);

            for (int r = 0; r < runs; r++)
            {
                if (!TryClaim(rng, half * 0.86f, 18f, out var point, 30)) continue;

                float heading = Rand(rng, 0f, 360f);
                var along = new Vector3(Mathf.Cos(heading * Mathf.Deg2Rad), 0f,
                                        Mathf.Sin(heading * Mathf.Deg2Rad));

                int slabs = 6 + rng.Next(7);
                var start = new Vector3(point.x, 0f, point.y) - along * (slabs * 1.4f);

                for (int i = 0; i < slabs; i++)
                {
                    // The run wanders rather than being a drawn line.
                    var at = start + along * (i * 2.8f)
                             + new Vector3(Rand(rng, -1.6f, 1.6f), 0f, Rand(rng, -1.6f, 1.6f));

                    float w = Rand(rng, 2.2f, 4.2f);
                    float h = Rand(rng, 1.8f, 3.6f);

                    at.y = GroundHeightAt(at.x, at.z) + h * 0.28f;

                    var slab = CreateBlock(group, at, new Vector3(w, h, Rand(rng, 0.6f, 1.2f)),
                                           0f, layer, IceTag, Color.white, 0.55f, 0f,
                                           $"Ridge_{r}_{i}", _iceBlockMat);

                    slab.transform.localRotation =
                        Quaternion.Euler(Rand(rng, 52f, 78f), heading + Rand(rng, -25f, 25f),
                                         Rand(rng, -18f, 18f));

                    NoStanding(slab);
                }
            }
        }

        /// <summary>
        /// Rock breaking through the snow, drifts, and the odd dead tree. What fills the
        /// distance between the places worth going.
        /// </summary>
        private static void BuildSnowScatter(Transform root, int layer, System.Random rng,
                                             float half)
        {
            var group = new GameObject("Scatter").transform;
            group.SetParent(root, false);

            // Outcrops. Buried to the lowest ground anywhere under them, not to the ground
            // at their centre -- a flat-bottomed shell pinned to its middle floats on the
            // downhill side of any slope, and the gap it leaves is a way inside a closed
            // mesh. The desert records what that costs.
            int rocks = 26 + rng.Next(12);

            for (int i = 0; i < rocks; i++)
            {
                float width = Rand(rng, 2.6f, 7.5f);
                if (!TryClaim(rng, half * 0.92f, width * 0.7f, out var point, 24)) continue;

                Boulder(group, layer, rng, new Vector3(point.x, 0f, point.y), width);
            }

            // Drifts: the same boulder shell, squashed flat and wearing snow. They are
            // what makes the ground uneven at a scale the heightfield cannot do -- the
            // grid is three metres, so anything smaller than that has to be an object.
            int drifts = 40 + rng.Next(20);

            for (int i = 0; i < drifts; i++)
            {
                if (!TryClaim(rng, half * 0.94f, 5f, out var point, 18)) continue;

                float w = Rand(rng, 4f, 11f);
                float h = Rand(rng, 0.7f, 1.9f);

                float y = LowestGroundIn(point.x, point.y, w * 0.5f) - h * 0.45f;

                var drift = MeshObject(group, $"Drift_{i}",
                                       BoulderMesh(4000 + i, 0.22f, 0.3f), _snowMat,
                                       new Vector3(point.x, y, point.y),
                                       Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f),
                                       new Vector3(w, h, w * Rand(rng, 0.5f, 0.85f)) / BoulderSpread,
                                       layer, SnowTag);

                // Low and shallow, which is exactly what a NavMeshSurface calls walkable.
                // Forty of these left on the bake is forty islands the spawner can choose.
                NoStanding(drift);
            }

            int trees = 10 + rng.Next(8);

            for (int i = 0; i < trees; i++)
            {
                if (!TryClaim(rng, half * 0.9f, 3f, out var point, 16)) continue;

                float y = GroundHeightAt(point.x, point.y);

                var tree = MeshObject(group, $"DeadTree_{i}", DeadTreeMesh(700 + i), _rockMat,
                                      new Vector3(point.x, y - 0.3f, point.y),
                                      Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f),
                                      Vector3.one * Rand(rng, 0.8f, 1.35f), layer, "Wood");

                NoStanding(tree);
            }
        }

        // ==================================================================
        // Weather
        // ==================================================================

        /// <summary>
        /// The falling snow. One object, one component, and a material the builder makes
        /// -- see <see cref="Snowfall"/> for why it follows the player rather than
        /// covering the arena, and why the material cannot be found by name at runtime.
        /// </summary>
        private static void BuildSnowfall(Transform root)
        {
            var go = new GameObject("Snowfall");
            go.transform.SetParent(root, false);

            var weather = go.AddComponent<Snowfall>();
            weather.flakeMaterial = FlakeMaterial();
        }

        private static Material FlakeMaterial()
        {
            string path = $"{MaterialFolder}/{SafeName(_theme.themeName)}_Flake.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            // Unlit: a flake is a speck of light in the air and shading one costs a
            // lighting evaluation per particle for an effect nobody can see at that size.
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default");

            var mat = new Material(shader);

            var flake = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/FPSKit_Generated/Textures/Flake.png");

            if (flake != null)
            {
                mat.mainTexture = flake;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", flake);
            }

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);   // transparent
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);       // alpha
            mat.renderQueue = 3000;

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ==================================================================
        private static void ResolveSnowMaterials()
        {
            // The ground, and what the igloos and drifts are made of. Slightly brighter
            // than the theme's floor so a dome reads against the field it stands on --
            // identical values would make an igloo a silhouette-less lump.
            // Four metres a tile, not seven. At seven the detail is below the size the
            // eye resolves at walking distance and the field came back as a flat sheet
            // with seams in it; the normal is pushed hard for the same reason, because on
            // a surface this bright the only thing that shows a drift at all is the way
            // it catches the light.
            _snowMat = MakeDetailMaterial("SnowField", _theme.floorColor,
                                          "Snow", Metres(4f), 0.05f, 0f, 1.9f);

            _packedMat = MakeDetailMaterial("SnowPacked", Shade(_theme.floorColor, 0.88f),
                                            "Snow", Metres(2.4f), 0.1f, 0f, 1.8f);

            // <b>Ice is darker than snow, and that is the whole of what makes it read as
            // ice.</b> The instinct is to make it brighter -- it is the shiny one -- and
            // at 0.72 white-blue the lake came back as a slightly different shade of the
            // field around it: sixty metres of the most distinctive ground in the arena,
            // invisible from the shore. Lake ice is dark because you are seeing water
            // through it, and the contrast against a 0.93 snow field is what says "this
            // is not the same surface" from two hundred metres away.
            //
            // The smoothness is the second half of it and cannot do the job alone: a
            // sheen needs something to reflect, and on an open field under a clear sky
            // there is very little up there but blue.
            _iceMat = MakeDetailMaterial("LakeIce", new Color(0.40f, 0.54f, 0.65f),
                                         "Ice", Metres(14f), 0.9f, 0f, 0.7f);

            // Broken ice is paler than the sheet, because a fracture face is full of air
            // -- which is also why a shard is the one thing on the lake you can see from
            // across it, and therefore the only cover out there.
            _iceBlockMat = MakeDetailMaterial("IceBlock", new Color(0.70f, 0.82f, 0.90f),
                                              "Ice", Metres(2.2f), 0.72f, 0f, 1.2f);

            _campTimberMat = MakeDetailMaterial("CampTimber", Shade(_theme.bankColor, 0.7f),
                                                "Timber", Metres(2f), 0.06f, 0f, 1.3f);
        }
    }
}
#endif
