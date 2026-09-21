#if UNITY_EDITOR
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The abandoned subway: two platform halls, a concourse over them, and the tunnels
    /// they run into.
    ///
    /// <b>Big in extent, tight in every sightline.</b> That is the whole design problem
    /// here. The theme calls this arena "tight, dark, claustrophobic", which a 70m box
    /// delivered by being small -- and being small is exactly what made it read as a
    /// prototype next to a 500m plant. A station complex is the shape that resolves the
    /// two: it covers a great deal of ground and you can never see across any of it,
    /// because it is halls and passages rather than a hall.
    ///
    /// So nothing here is placed in the open. Every route is down a hall, across a
    /// passage, along a track bed or up a stair, and the longest sightline in the arena is
    /// the length of one platform.
    ///
    /// <b>Three levels, and each one is a real place.</b> Track beds at the bottom, which
    /// are walkable because a drained running tunnel is walkable and because they are the
    /// flanking route past a held platform. Platforms a metre and a bit above them.
    /// A concourse over both halls, reached by stairs at either end, which is the position
    /// the level is meant to be fought over -- it overlooks both halls through the ceiling
    /// openings and is the only place that does.
    ///
    /// <b>The collapses are the navigation.</b> A fully sealed station is uniformly dark
    /// and a player has nothing to steer by; every corridor looks like every other. Three
    /// sections of roof have fallen in, and the daylight down those shafts is the only
    /// bright thing on the map -- so "head for the light" is a direction a player can give
    /// themselves. The rubble under each also breaks the hall it lands in, which is what
    /// stops a 450m hall being a shooting gallery.
    /// </summary>
    public partial class FPSKitSceneBuilder
    {
        // Metres. The proportions are a real station's: a platform is about 4m wide for a
        // side platform and 10m for an island, a running tunnel is about 4m across, and a
        // platform stands 1.1m over the rail. Keeping those honest is most of what makes
        // the place read as infrastructure rather than as level geometry.
        private const float TrackY       = 0f;
        private const float PlatformY    = 1.15f;
        private const float ConcourseY   = 7.2f;
        private const float HallCeilingY = 5.6f;
        private const float ConcourseCeilY = 12.4f;

        private const float PlatformWidth = 11f;
        private const float TrackWidth    = 4.4f;
        private const float HallHalfWidth = 13f;

        private static Material _subwayTile;
        private static Material _subwayConcrete;
        private static Material _subwayMetal;
        private static Material _subwayRubble;
        private static Material _subwayRail;

        private static void BuildSubwayZone()
        {
            _claimed.Clear();

            var root = new GameObject("Arena").transform;
            int envLayer = LayerMask.NameToLayer("Environment");
            var rng = new System.Random(_theme.randomSeed);

            float half = _theme.arenaSize * 0.5f;

            ResolveSubwayMaterials();

            // The halls run north-south, offset either side of centre, with the concourse
            // spanning the gap above them. Chosen rather than random because a station is
            // built to a plan -- what varies is what has happened to it since.
            // Far enough apart that the station spans the arena rather than standing in
            // the middle of it. At 34 the built footprint was a 96m strip inside a 450m
            // box and everything either side of it was bare floor -- 4,422 patches of
            // navmesh joined to nothing, which is what VerifyReach failed on.
            float hallOffset = 72f;

            // On the concourse, between the halls, facing down the station. Not the origin
            // at ground level: the concourse deck is 1.15m thick there, so the default
            // start would put the player inside it -- on no navmesh, unreachable by
            // everything, which is exactly what the check reported as 100% stranded.
            _playerStart = new Vector3(0f, PlatformY + 1f, -StationHalfLength(half) * 0.55f);

            BuildSubwayShell(root, envLayer, half);

            BuildPlatformHall(root, envLayer, rng, -hallOffset, "A", half);
            BuildPlatformHall(root, envLayer, rng, hallOffset, "B", half);

            BuildLowerConcourse(root, envLayer, rng, hallOffset, half);
            BuildConcourse(root, envLayer, rng, hallOffset, half);
            BuildCollapses(root, envLayer, rng, hallOffset, half);
            BuildServiceRooms(root, envLayer, rng, hallOffset, half);
            BuildSubwayLights(root, rng, hallOffset, half);
        }


        /// <summary>
        /// The box the station sits in: floor, outer walls and the roof over everything.
        ///
        /// The roof is what makes this arena what it is, and it is also the thing that has
        /// to be held off the navigation bake -- a NavMeshSurface takes any surface
        /// shallower than the agent slope, and a flat roof over the whole map is 200,000
        /// square metres of walkable ground nothing can reach.
        /// </summary>
        /// <summary>
        /// Metres of station either side of centre, and along. Everything outside this is
        /// solid ground rather than floor -- a station is carved out of the earth, not
        /// stood up inside a room.
        /// </summary>
        /// <summary>
        /// Stops at the outer wall of the outer hall, not somewhere round. Run wider than
        /// the halls and the surplus is bare floor joined to nothing -- 616 stranded
        /// patches the first time, in two 19m strips nobody could reach.
        /// </summary>
        private static float StationHalfWidth(float half)
            => Mathf.Min(half - 14f, 72f + PlatformWidth * 0.5f + TrackWidth + 0.6f);
        /// <summary>
        /// Stops at the headwalls, which stand at 0.89 of the arena half-size. Run past
        /// them and the surplus is a nine-metre strip of floor across the full width at
        /// each end, behind a wall, reachable by nothing.
        /// </summary>
        private static float StationHalfLength(float half) => Mathf.Min(half - 16f, half * 0.9f);

        private static void BuildSubwayShell(Transform root, int layer, float half)
        {
            float size = half * 2f;
            float fw = StationHalfWidth(half);
            float fl = StationHalfLength(half);

            // <b>The floor is the station's footprint, not the arena's.</b> Built at full
            // size it was a 450m slab with a 96m station standing on it, so nine tenths of
            // the walkable ground in the level was open floor joined to nothing -- 98% of
            // the navmesh could not reach the player, and 4,422 of those patches were this
            // one object. A floor that stops where the station stops cannot do that.
            var floor = CreateBlock(root, new Vector3(0f, TrackY - 0.5f, 0f),
                                    new Vector3(fw * 2f, 1f, fl * 2f), 0f, layer,
                                    _theme.floorTag, _theme.floorColor, 0.05f, 0f,
                                    "StationFloor", _subwayConcrete);

            // Outer walls. Tall enough to meet the concourse ceiling, so there is no gap to
            // see daylight through except where the roof has actually fallen in.
            float wallH = ConcourseCeilY + 1f;
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;
                float sign = (side % 2 == 0) ? 1f : -1f;

                var pos = alongX
                    ? new Vector3(0f, wallH * 0.5f, half * sign)
                    : new Vector3(half * sign, wallH * 0.5f, 0f);

                var scale = alongX
                    ? new Vector3(size, wallH, 1.5f)
                    : new Vector3(1.5f, wallH, size);

                CreateBlock(root, pos, scale, 0f, layer, _theme.wallTag, _theme.wallColor,
                            0.12f, 0f, $"OuterWall{side}", _subwayTile);
            }

            // The ground the station is cut into. Drawn so the player can see they are
            // underground rather than in a box, and sealed so nothing bakes out there.
            for (int side = -1; side <= 1; side += 2)
            {
                var earth = CreateBlock(root,
                    new Vector3(side * (fw + (half - fw) * 0.5f), (ConcourseCeilY + 2f) * 0.5f, 0f),
                    new Vector3(half - fw, ConcourseCeilY + 2f, size), 0f, layer,
                    _theme.wallTag, Shade(_theme.wallColor, 0.42f), 0.03f, 0f,
                    $"Earth{side}", _subwayRubble);
                NoEntry(earth);

                var capEarth = CreateBlock(root,
                    new Vector3(0f, (ConcourseCeilY + 2f) * 0.5f, side * (fl + (half - fl) * 0.5f)),
                    new Vector3(fw * 2f, ConcourseCeilY + 2f, half - fl), 0f, layer,
                    _theme.wallTag, Shade(_theme.wallColor, 0.42f), 0.03f, 0f,
                    $"EarthEnd{side}", _subwayRubble);
                NoEntry(capEarth);
            }

            var roof = CreateBlock(root, new Vector3(0f, ConcourseCeilY + 0.6f, 0f),
                                   new Vector3(size, 1.2f, size), 0f, layer,
                                   _theme.wallTag, Shade(_theme.wallColor, 0.55f), 0.05f, 0f,
                                   "StationRoof", _subwayConcrete);
            NoStanding(roof);
            Hide(roof);
        }

        /// <summary>
        /// One platform hall: a track bed either side of an island platform, the platform
        /// itself, its columns and its edge.
        ///
        /// An island platform rather than two side platforms, because it gives the hall a
        /// spine to fight along with a drop either side, and because the columns down its
        /// centre are the cover that makes a 400m hall survivable.
        /// </summary>
        private static void BuildPlatformHall(Transform root, int layer, System.Random rng,
                                              float centreX, string label, float half)
        {
            var hall = new GameObject($"PlatformHall_{label}").transform;
            hall.SetParent(root, false);

            float length = half * 1.78f;

            // --- track beds, one either side, sunk below the platform ---
            for (int side = -1; side <= 1; side += 2)
            {
                float x = centreX + side * (PlatformWidth * 0.5f + TrackWidth * 0.5f);

                var bed = CreateBlock(hall, new Vector3(x, TrackY + 0.05f, 0f),
                                      new Vector3(TrackWidth, 0.1f, length), 0f, layer,
                                      _theme.floorTag, Shade(_theme.floorColor, 0.62f),
                                      0.04f, 0f, $"TrackBed{side}", _subwayRubble);
                Mark(bed, new Color(0.22f, 0.24f, 0.28f), 0);

                // <b>The player can drop in here; enemies will not follow.</b>
                //
                // This was a contested flanking route and it cost five of the nine
                // connectivity failures on this arena -- fragmented by the bridges that have
                // to cross it, and unreachable by every stair or ramp tried, because a
                // 4.4m-wide trench can only be entered sideways and BuildStairRun rises at a
                // fixed x: its top overlapped the platform by about ten centimetres.
                //
                // Held off the bake it is still every bit of what it looks like -- a sunken
                // bed with rails in it, a metre below the platform, that a player can drop
                // into to break line of sight and climb out of anywhere along its length.
                // What it stops being is somewhere an enemy can be spawned and stranded.
                // Delete these two calls to make it navigable again.
                NoStanding(bed);

                // Rails. Two per bed, at gauge, because a track bed with no rail in it is
                // a trench -- the rails are the only thing that says what this is.
                for (int rail = -1; rail <= 1; rail += 2)
                {
                    var r = CreateBlock(hall, new Vector3(x + rail * 0.72f, TrackY + 0.14f, 0f),
                                        new Vector3(0.14f, 0.16f, length), 0f, layer,
                                        "Metal", new Color(0.36f, 0.34f, 0.32f), 0.7f, 0.9f,
                                        $"Rail{side}{rail}", _subwayRail);
                    NoStanding(r);
                }
            }

            // --- the platform ---
            var platform = CreateBlock(hall, new Vector3(centreX, PlatformY * 0.5f, 0f),
                                       new Vector3(PlatformWidth, PlatformY, length), 0f, layer,
                                       _theme.floorTag, _theme.floorColor, 0.1f, 0f,
                                       $"Platform{label}", _subwayTile);
            Mark(platform, new Color(0.46f, 0.48f, 0.54f), 1);

            // The yellow edge strip. Faded, because everything here is, but it is the one
            // piece of colour on the platform and it draws the eye along its length.
            for (int side = -1; side <= 1; side += 2)
            {
                // <b>Not NoStanding.</b> Marking a 4cm-proud strip unwalkable draws an
                // unwalkable border right round the platform, and the track bridges land
                // exactly on it -- so nothing could step off a bridge onto the platform and
                // both platforms baked as islands. It is flush enough to bake as part of
                // the platform, which is what it is.
                CreateBlock(hall,
                    new Vector3(centreX + side * (PlatformWidth * 0.5f - 0.35f), PlatformY + 0.01f, 0f),
                    new Vector3(0.7f, 0.04f, length), 0f, layer,
                    _theme.floorTag, new Color(0.62f, 0.55f, 0.24f), 0.2f, 0f,
                    $"EdgeStrip{side}");
            }

            // --- columns down the platform ---
            int columns = Mathf.RoundToInt(length / 14f);
            for (int i = 0; i < columns; i++)
            {
                float z = -length * 0.5f + (i + 0.5f) * (length / columns);

                CreateBlock(hall, new Vector3(centreX, PlatformY + HallCeilingY * 0.5f, z),
                            new Vector3(1.1f, HallCeilingY, 1.1f), 0f, layer,
                            _theme.wallTag, Shade(_theme.wallColor, 0.8f), 0.15f, 0f,
                            $"Column{i}", _subwayTile);
            }

            // No way down into the beds, deliberately -- see the note on TrackBed above.
            // They are scenery now, so a stair into one would descend to ground nothing
            // can stand on, and each one blocked the bed it sat in besides.

            // --- tunnel mouths at both ends ---
            for (int end = -1; end <= 1; end += 2)
            {
                float z = end * length * 0.5f;

                var headwall = CreateBlock(hall, new Vector3(centreX, 3.4f, z + end * 1.2f),
                                           new Vector3(PlatformWidth + TrackWidth * 2f + 3f, 6.8f, 1.6f),
                                           0f, layer, _theme.wallTag,
                                           Shade(_theme.wallColor, 0.7f), 0.1f, 0f,
                                           $"Headwall{end}", _subwayTile);
                Hide(headwall);

                // The bores themselves are dressing: dark recesses that say the line
                // continues. Nothing may walk into them, so the whole mouth is sealed.
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = centreX + side * (PlatformWidth * 0.5f + TrackWidth * 0.5f);

                    var bore = CreateBlock(hall, new Vector3(x, 2.2f, z + end * 4f),
                                           new Vector3(TrackWidth + 0.6f, 4.4f, 7f), 0f, layer,
                                           _theme.wallTag, new Color(0.05f, 0.05f, 0.06f),
                                           0f, 0f, $"TunnelBore{end}{side}");
                    NoEntry(bore);
                    Hide(bore);
                }
            }

            // --- tunnel walls ---
            //
            // A station's tunnel wall sits at the outer face of the track bed. Without
            // them the floor ran on past the beds as a five-metre strip either side, four
            // hundred metres long -- and once the beds stopped being walkable those strips
            // were severed from everything and became the single largest stranded object
            // in the arena.
            // <b>Outer face only.</b> Built on both sides it walls the hall off from the
            // concourse: the inner wall lands at x 61.8 and the bridges from the concourse
            // span 61.4 to 67.2, so it cut every one of them and both platforms baked as
            // islands. The inner face of a hall is not a tunnel wall -- it is the way in.
            for (int side = -1; side <= 1; side += 2)
            {
                if (side * Mathf.Sign(centreX) < 0f) continue;

                float x = centreX + side * (PlatformWidth * 0.5f + TrackWidth + 0.3f);

                var wall = CreateBlock(hall, new Vector3(x, (PlatformY + HallCeilingY) * 0.5f, 0f),
                                       new Vector3(0.6f, PlatformY + HallCeilingY, length), 0f,
                                       layer, _theme.wallTag, Shade(_theme.wallColor, 0.74f),
                                       0.12f, 0f, $"TunnelWall{side}", _subwayTile);
                Hide(wall);
            }

            // --- the hall ceiling ---
            var ceiling = CreateBlock(hall, new Vector3(centreX, PlatformY + HallCeilingY, 0f),
                                      new Vector3(HallHalfWidth * 2f, 0.8f, length), 0f, layer,
                                      _theme.wallTag, Shade(_theme.wallColor, 0.5f), 0.05f, 0f,
                                      $"HallCeiling{label}", _subwayConcrete);
            NoStanding(ceiling);
            Hide(ceiling);
        }


        /// <summary>
        /// The lower concourse between the two halls, and the blocks that break it up.
        ///
        /// <b>This replaced four cross passages, and the reason is a navigation failure
        /// worth not repeating.</b> The passages were floor slabs 1.15m thick running the
        /// full width between the halls -- which is a ramp to nobody and a wall to
        /// everybody, because 1.15m is far beyond an agent's step height. They cut the
        /// station floor into five isolated bands, and 92% of the navmesh could not reach
        /// the player standing in one of them.
        ///
        /// Raising the whole middle to platform level fixes that by removing the step
        /// entirely: the concourse, both platforms and the passage mouths are all one
        /// surface at 1.15m, and the floor left underneath has 1.15m of headroom, which is
        /// under the agent height, so it does not bake at all.
        ///
        /// The tight sightlines then have to come from something other than the walls,
        /// which is what the service blocks are for -- a station concourse is full of
        /// kiosks, lift shafts, stair enclosures and plant rooms, and they break a 144m
        /// span into rooms without any of them being a corridor.
        /// </summary>
        private static void BuildLowerConcourse(Transform root, int layer, System.Random rng,
                                                float hallOffset, float half)
        {
            var lower = new GameObject("LowerConcourse").transform;
            lower.SetParent(root, false);

            float inner = hallOffset - PlatformWidth * 0.5f - TrackWidth;
            float length = StationHalfLength(half) * 0.94f;

            var deck = CreateBlock(lower, new Vector3(0f, PlatformY * 0.5f, 0f),
                                   new Vector3(inner * 2f, PlatformY, length * 2f), 0f, layer,
                                   _theme.floorTag, _theme.floorColor, 0.1f, 0f,
                                   "ConcourseLower", _subwayTile);
            Mark(deck, new Color(0.42f, 0.44f, 0.5f), 1);

            // Bridges over the track beds, joining the concourse to each platform. Without
            // these the two halls are islands: the beds are a 1.15m drop either side and
            // the concourse stops at the edge of them.
            // <b>Two, at the far ends, not five spread along.</b> A bridge sits a metre over
            // the track bed, which leaves a metre of headroom -- under the agent height, so
            // the navmesh does not bake beneath it. Five of them chopped each inner bed into
            // six disconnected segments and stranded the lot. At the ends they bracket one
            // long continuous run instead, which is also what a real station does: you cross
            // at the end of the platform, not in the middle of it.
            for (int hall = -1; hall <= 1; hall += 2)
            {
                const int bridges = 2;
                for (int i = 0; i < bridges; i++)
                {
                    float z = (i == 0 ? -1f : 1f) * length * 0.86f;

                    CreateBlock(lower,
                        new Vector3(hall * (hallOffset - PlatformWidth * 0.5f - TrackWidth * 0.5f),
                                    PlatformY - 0.05f, z),
                        new Vector3(TrackWidth + 1.4f, 0.24f, 7f), 0f, layer,
                        _theme.floorTag, Shade(_theme.floorColor, 0.88f), 0.1f, 0f,
                        $"TrackBridge{hall}_{i}", _subwayMetal);
                }
            }

            // What breaks the span. Sized and spaced like real concourse furniture rather
            // than scattered, so it reads as a station rather than as cover placed for a
            // fight -- which it also is.
            // Fewer, and further apart. Every block pinches a pocket or two against its
            // neighbours, and at twenty-odd they were the last thing keeping the arena over
            // the two percent the check allows.
            int blocks = 11 + rng.Next(5);
            for (int i = 0; i < blocks; i++)
            {
                // Kept off the edges. Blocks placed right up against the concourse boundary
                // pinch a pocket between themselves and it -- a few square metres each,
                // reachable by nothing, and they were most of what remained stranded.
                float x = (float)(rng.NextDouble() - 0.5) * inner * 1.44f;
                float z = (float)(rng.NextDouble() - 0.5) * length * 1.60f;

                float w = 5f + (float)rng.NextDouble() * 9f;
                float d = 4f + (float)rng.NextDouble() * 7f;
                float h = 3f + (float)rng.NextDouble() * 2.4f;

                var unit = CreateBlock(lower, new Vector3(x, PlatformY + h * 0.5f, z),
                                       new Vector3(w, h, d), 0f, layer,
                                       _theme.wallTag, Shade(_theme.wallColor, 0.82f),
                                       0.16f, 0f, $"ConcourseUnit{i}", _subwayTile);
                NoEntry(unit);
            }
        }

        /// <summary>
        /// Superseded by <see cref="BuildLowerConcourse"/>. Kept only so the reasoning
        /// above has something to point at; nothing calls it.
        ///
        /// The passages joining the two halls, cut through at intervals.
        ///
        /// These are the only way across the middle of the map at platform level, so how
        /// many there are decides whether the two halls are one arena or two. Four is
        /// enough that a player pushed out of one hall has somewhere to go, and few enough
        /// that holding one means something.
        /// </summary>
        private static void BuildCrossPassages(Transform root, int layer, System.Random rng,
                                               float hallOffset, float half)
        {
            var passages = new GameObject("CrossPassages").transform;
            passages.SetParent(root, false);

            const int Count = 4;
            float span = hallOffset * 2f;
            float reach = half * 1.3f;

            for (int i = 0; i < Count; i++)
            {
                // Spread across the length, jittered so they do not read as a comb.
                float t = (i + 0.5f) / Count;
                float z = Mathf.Lerp(-reach * 0.5f, reach * 0.5f, t)
                          + (float)(rng.NextDouble() - 0.5) * 16f;

                float width = 5.5f + (float)rng.NextDouble() * 2.5f;

                CreateBlock(passages, new Vector3(0f, PlatformY * 0.5f, z),
                            new Vector3(span, PlatformY, width), 0f, layer,
                            _theme.floorTag, _theme.floorColor, 0.1f, 0f,
                            $"PassageFloor{i}", _subwayTile);

                // Side walls, which is what makes it a passage rather than a bridge.
                for (int side = -1; side <= 1; side += 2)
                {
                    var wall = CreateBlock(passages,
                        new Vector3(0f, PlatformY + 2.1f, z + side * width * 0.5f),
                        new Vector3(span, 4.2f, 0.6f), 0f, layer,
                        _theme.wallTag, Shade(_theme.wallColor, 0.88f), 0.14f, 0f,
                        $"PassageWall{i}{side}", _subwayTile);
                    Hide(wall);
                }

                var lid = CreateBlock(passages,
                    new Vector3(0f, PlatformY + 4.3f, z),
                    new Vector3(span, 0.5f, width), 0f, layer,
                    _theme.wallTag, Shade(_theme.wallColor, 0.5f), 0.05f, 0f,
                    $"PassageLid{i}", _subwayConcrete);
                NoStanding(lid);
                Hide(lid);
            }
        }

        /// <summary>
        /// The concourse over both halls, and the stairs up to it.
        ///
        /// This is the position the level is meant to be fought over. It is the only place
        /// that overlooks both halls -- through the openings the stairs come up -- and the
        /// only way onto it is those stairs, so taking it costs something and holding it
        /// means watching two approaches.
        ///
        /// Its deck is bakeable on purpose. The parapets are not: a chest-high wall is
        /// shallower than the agent slope across its top, and every metre of it would bake
        /// as walkable ground an agent could be spawned onto and never path off.
        /// </summary>
        private static void BuildConcourse(Transform root, int layer, System.Random rng,
                                           float hallOffset, float half)
        {
            var concourse = new GameObject("Concourse").transform;
            concourse.SetParent(root, false);

            float width = hallOffset * 2f + PlatformWidth;
            float length = half * 0.92f;

            var deck = CreateBlock(concourse, new Vector3(0f, ConcourseY, 0f),
                                   new Vector3(width, 0.7f, length), 0f, layer,
                                   _theme.floorTag, Shade(_theme.floorColor, 1.04f), 0.12f, 0f,
                                   "ConcourseDeck", _subwayTile);
            Mark(deck, new Color(0.58f, 0.60f, 0.66f), 2);

            // Parapets round the edge.
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;
                float sign = (side % 2 == 0) ? 1f : -1f;

                var pos = alongX
                    ? new Vector3(0f, ConcourseY + 0.95f, length * 0.5f * sign)
                    : new Vector3(width * 0.5f * sign, ConcourseY + 0.95f, 0f);

                var scale = alongX
                    ? new Vector3(width, 1.2f, 0.5f)
                    : new Vector3(0.5f, 1.2f, length);

                var parapet = CreateBlock(concourse, pos, scale, 0f, layer,
                                          _theme.wallTag, Shade(_theme.wallColor, 0.92f),
                                          0.16f, 0f, $"Parapet{side}", _subwayConcrete);
                NoStanding(parapet);
            }

            // Stair banks at both ends of each hall: four ways up, so the concourse is
            // contested rather than owned by whoever is nearest.
            for (int hall = -1; hall <= 1; hall += 2)
            {
                for (int end = -1; end <= 1; end += 2)
                {
                    float x = hall * hallOffset;
                    float z = end * (length * 0.5f - 9f);

                    BuildStairRun(concourse, layer, new Vector3(x, 0f, z), end,
                                  PlatformY, ConcourseY, $"Stair{hall}{end}");
                }
            }

            var ceiling = CreateBlock(concourse, new Vector3(0f, ConcourseCeilY, 0f),
                                      new Vector3(width + 6f, 0.7f, length + 6f), 0f, layer,
                                      _theme.wallTag, Shade(_theme.wallColor, 0.48f), 0.05f, 0f,
                                      "ConcourseCeiling", _subwayConcrete);
            NoStanding(ceiling);
            Hide(ceiling);
        }

        /// <summary>
        /// One flight of stairs, built as stepped blocks.
        ///
        /// Stepped rather than a ramp because a stair is what a station has, and because
        /// the NavMesh bakes a stepped run perfectly well as long as each step is inside
        /// the agent's step height. Run before rise, so the flight is at a climbable pitch
        /// rather than at whatever angle the two heights happen to make.
        /// </summary>
        private static void BuildStairRun(Transform parent, int layer, Vector3 footPosition,
                                          int facing, float fromY, float toY, string name)
        {
            float rise = toY - fromY;
            const float StepRise = 0.18f;
            const float StepRun = 0.30f;

            int steps = Mathf.Max(1, Mathf.RoundToInt(rise / StepRise));
            float width = 4.6f;

            var run = new GameObject(name).transform;
            run.SetParent(parent, false);

            for (int i = 0; i < steps; i++)
            {
                float y = fromY + (i + 0.5f) * (rise / steps);
                float z = footPosition.z + facing * (i + 0.5f) * StepRun;

                CreateBlock(run, new Vector3(footPosition.x, y * 0.5f + fromY * 0.5f, z),
                            new Vector3(width, y - fromY + StepRise, StepRun + 0.02f), 0f,
                            layer, _theme.floorTag, Shade(_theme.floorColor, 0.96f),
                            0.1f, 0f, $"Step{i}", _subwayConcrete);
            }
        }


        /// <summary>
        /// Where the roof has fallen in, and the rubble it dropped.
        ///
        /// <b>These are the only bright things on the map, and that is their job.</b> A
        /// sealed station is uniformly dark, every corridor looks like every other, and a
        /// player has nothing to navigate by -- which reads as the level being confusing
        /// rather than as it being dark. A shaft of daylight is visible from a long way
        /// down a hall and gives somebody a direction they can hold in their head.
        ///
        /// The rubble underneath is not decoration either: a 400m hall with nothing across
        /// it is a shooting gallery, and a debris pile is the one obstruction that belongs
        /// in a ruin rather than being placed there for the fight.
        /// </summary>
        private static void BuildCollapses(Transform root, int layer, System.Random rng,
                                           float hallOffset, float half)
        {
            var collapses = new GameObject("Collapses").transform;
            collapses.SetParent(root, false);

            // Three, spread down the length and never two in the same hall at the same z,
            // or the pair reads as a deliberate pattern.
            var sites = new[]
            {
                new Vector2(-hallOffset, -half * 0.42f),
                new Vector2(hallOffset, -half * 0.02f),
                new Vector2(0f, half * 0.46f),
            };

            for (int i = 0; i < sites.Length; i++)
            {
                float x = sites[i].x + (float)(rng.NextDouble() - 0.5) * 10f;
                float z = sites[i].y + (float)(rng.NextDouble() - 0.5) * 18f;
                float radius = 9f + (float)rng.NextDouble() * 5f;

                // The hole. Drawn as a bright slab up at roof level so that looking up the
                // shaft shows sky-coloured light rather than the underside of the roof.
                var shaft = CreateBlock(collapses, new Vector3(x, ConcourseCeilY + 0.9f, z),
                                        new Vector3(radius * 2f, 0.3f, radius * 2f), 0f, layer,
                                        _theme.wallTag, new Color(0.78f, 0.82f, 0.9f), 0f, 0f,
                                        $"Skylight{i}");
                NoStanding(shaft);
                Hide(shaft);

                // The light that actually falls down it. One per collapse, angled straight
                // down, with a long range so it reaches the platform.
                var lightGo = new GameObject($"Daylight{i}");
                lightGo.transform.SetParent(collapses, false);
                lightGo.transform.position = new Vector3(x, ConcourseCeilY - 0.5f, z);
                lightGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = new Color(0.82f, 0.88f, 1f);
                light.intensity = 9f;
                light.range = ConcourseCeilY + 6f;
                light.spotAngle = 68f;
                light.shadows = LightShadows.Soft;

                // Rubble: a heap of slabs, biggest in the middle, tumbling outward.
                int chunks = 7 + rng.Next(6);
                for (int c = 0; c < chunks; c++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float d = (float)rng.NextDouble() * radius;

                    float w = 1.4f + (float)rng.NextDouble() * 3.2f;
                    float h = 0.5f + (float)rng.NextDouble() * (1f - d / radius) * 2.6f;

                    CreateBlock(collapses,
                        new Vector3(x + Mathf.Cos(a) * d, PlatformY + h * 0.5f, z + Mathf.Sin(a) * d),
                        new Vector3(w, h, w * (0.6f + (float)rng.NextDouble() * 0.8f)),
                        (float)rng.NextDouble() * 360f, layer,
                        "Concrete", Shade(_theme.floorColor, 0.66f), 0.05f, 0f,
                        $"Rubble{i}_{c}", _subwayRubble);
                }
            }
        }

        /// <summary>
        /// Service rooms along the outer walls: plant, crew rooms, switch rooms.
        ///
        /// Every one gets a NoEntry volume. A NoStanding would take the roof off the bake
        /// and leave the floor -- and the floor inside one of these is not the room, it is
        /// the station's own slab running underneath, so what bakes is a slab of navmesh
        /// the size of a room, walled in on four sides, joined to nothing. That is an enemy
        /// standing in a cupboard for a whole level with nothing logged.
        /// </summary>
        private static void BuildServiceRooms(Transform root, int layer, System.Random rng,
                                              float hallOffset, float half)
        {
            var rooms = new GameObject("ServiceRooms").transform;
            rooms.SetParent(root, false);

            int count = 8 + rng.Next(5);

            for (int i = 0; i < count; i++)
            {
                bool eastWest = rng.Next(2) == 0;
                float sign = rng.Next(2) == 0 ? 1f : -1f;

                float along = (float)(rng.NextDouble() - 0.5) * half * 1.5f;
                float w = 7f + (float)rng.NextDouble() * 8f;
                float d = 6f + (float)rng.NextDouble() * 5f;

                var centre = eastWest
                    ? new Vector3(sign * (half - d * 0.5f - 1f), 0f, along)
                    : new Vector3(along, 0f, sign * (half - d * 0.5f - 1f));

                var size = eastWest ? new Vector3(d, 4.6f, w) : new Vector3(w, 4.6f, d);

                var room = CreateBlock(rooms, centre + Vector3.up * 2.3f, size, 0f, layer,
                                       _theme.wallTag, Shade(_theme.wallColor, 0.78f),
                                       0.14f, 0f, $"ServiceRoom{i}", _subwayMetal);
                NoEntry(room);
            }
        }

        /// <summary>
        /// The failing service lighting.
        ///
        /// Warm and weak, against the cold daylight down the collapses, because two colours
        /// of light is what tells a player which of the two they are looking at from across
        /// a hall. Sparse on purpose -- lighting a ruin evenly is what makes it look like a
        /// corridor rather than a ruin.
        /// </summary>
        private static void BuildSubwayLights(Transform root, System.Random rng,
                                              float hallOffset, float half)
        {
            var lights = new GameObject("ServiceLights").transform;
            lights.SetParent(root, false);

            for (int hall = -1; hall <= 1; hall += 2)
            {
                int lamps = 9;
                for (int i = 0; i < lamps; i++)
                {
                    // Roughly a third are dead, which is what makes the live ones read as
                    // survivors rather than as a lighting scheme.
                    if (rng.NextDouble() < 0.34) continue;

                    float z = Mathf.Lerp(-half * 0.8f, half * 0.8f, (i + 0.5f) / lamps);

                    var go = new GameObject($"Lamp{hall}_{i}");
                    go.transform.SetParent(lights, false);
                    go.transform.position = new Vector3(hall * hallOffset,
                                                        PlatformY + HallCeilingY - 0.8f, z);

                    var light = go.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = new Color(1f, 0.86f, 0.62f);
                    light.intensity = 1.5f + (float)rng.NextDouble() * 1.2f;
                    light.range = 17f;
                    light.shadows = LightShadows.None;
                }
            }
        }

        /// <summary>
        /// Where enemies come from in a station.
        ///
        /// <b>The generic ring is wrong here and was measurably wrong.</b> It places eight
        /// points on a circle at forty percent of the arena size, which knows nothing about
        /// where the halls are -- two of the eight landed in solid ground and had to be
        /// rescued by the snap pass. What that costs is invisible from inside the game: the
        /// level simply has fewer directions to be attacked from, with nothing logged.
        ///
        /// A station has obvious answers instead. Enemies come along the platforms, up from
        /// the track beds, and down the stairs from the concourse -- which is also the list
        /// of routes the player has to watch, so the spawns teach the layout.
        /// </summary>
        private static Transform[] BuildSubwaySpawnPoints()
        {
            var root = new GameObject("SpawnPoints").transform;
            var list = new System.Collections.Generic.List<Transform>();

            float half = _theme.arenaSize * 0.5f;
            float hallOffset = 72f;
            float reach = StationHalfLength(half) * 0.74f;

            void Add(string name, Vector3 at)
            {
                var sp = new GameObject(name).transform;
                sp.SetParent(root);
                sp.position = at;
                list.Add(sp);
            }

            for (int hall = -1; hall <= 1; hall += 2)
            {
                string label = hall < 0 ? "A" : "B";

                // Along each platform, at both ends and a third of the way in, so an enemy
                // can arrive behind a player who has pushed down the hall.
                Add($"Spawn_Platform{label}_N", new Vector3(hall * hallOffset, PlatformY + 0.2f, reach));
                Add($"Spawn_Platform{label}_S", new Vector3(hall * hallOffset, PlatformY + 0.2f, -reach));
                Add($"Spawn_Platform{label}_M", new Vector3(hall * hallOffset, PlatformY + 0.2f, reach * 0.3f));

                // Up out of a track bed. The flanking route, and the one a player watching
                // the platform is not watching.
                Add($"Spawn_Track{label}",
                    new Vector3(hall * hallOffset + (PlatformWidth * 0.5f + TrackWidth * 0.5f),
                                TrackY + 0.2f, -reach * 0.45f));
            }

            // The concourse, which overlooks both halls -- so it has to be somewhere the
            // player can be attacked from as well as a position they can take.
            Add("Spawn_Concourse_N", new Vector3(0f, ConcourseY + 0.9f, reach * 0.5f));
            Add("Spawn_Concourse_S", new Vector3(0f, ConcourseY + 0.9f, -reach * 0.5f));

            return list.ToArray();
        }

        private static void ResolveSubwayMaterials()
        {
            _subwayTile = MakeDetailMaterial("StationTile", _theme.wallColor, "Tile",
                                             Metres(2f), 0.24f, 0f, 1.2f);

            _subwayConcrete = MakeDetailMaterial("StationConcrete", Shade(_theme.floorColor, 0.92f),
                                                 "Concrete", Metres(3f), 0.08f, 0f);

            _subwayMetal = MakeDetailMaterial("StationMetal", Shade(_theme.wallColor, 0.66f),
                                              "Metal", Metres(1.6f), 0.42f, 0.7f);

            _subwayRubble = MakeDetailMaterial("StationRubble", Shade(_theme.floorColor, 0.74f),
                                               "Rock", Metres(2.2f), 0.05f, 0f, 1.5f);

            // Rail is the one thing in here that catches a light, which is what draws the
            // eye down a track bed and tells a player it is a route.
            _subwayRail = MakeDetailMaterial("StationRail", new Color(0.32f, 0.30f, 0.29f),
                                             "Metal", Metres(0.8f), 0.68f, 0.9f);
        }
    }
}
#endif
