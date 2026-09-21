#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The dune field: the open zone's ground, as a heightfield instead of a slab.
    ///
    /// A desert is not a floor with things on it. It is the shape of the ground itself
    /// -- a crest you come over and are suddenly exposed on, a hollow between two dunes
    /// you can cross unseen, a slope that costs you a second to climb with somebody
    /// shooting at you. None of that exists on a plane, and no amount of cover scattered
    /// on a plane produces it, because cover is a thing you stand behind and terrain is
    /// a thing you are on.
    ///
    /// Four things hold this together, and all four are easy to lose:
    ///
    ///   * <b>The angle of repose is enforced, not hoped for.</b> Sand cannot stand
    ///     steeper than about thirty-three degrees; more importantly, the NavMesh will
    ///     not bake steeper than <c>agentSlope</c> (45) and the player's
    ///     <c>CharacterController</c> will not climb past its <c>slopeLimit</c> (50). A
    ///     dune field built from summed sine waves overshoots all three wherever two
    ///     waves happen to line up, and what that costs is a hole in the navmesh
    ///     somewhere nobody looks: enemies spawned on the far side of it are discarded
    ///     by the leash on arrival and the level quietly plays short. So the field is
    ///     relaxed to the repose angle after it is generated -- which is also, exactly,
    ///     what wind does to sand, and it is most of why the result looks like dunes
    ///     rather than like a sine wave.
    ///   * <b>Everything else asks the terrain where the ground is.</b>
    ///     <see cref="GroundHeightAt"/> is the single answer, and every pass that places
    ///     anything calls it. A rock placed at y=0 on a dune field is a rock buried or
    ///     a rock hovering, and which one it is changes per rebuild.
    ///   * <b>Flat ground is reserved before the terrain exists, not carved after.</b>
    ///     A walled compound and a raised deck need a level pad; a boulder does not.
    ///     So the passes that need one choose their sites first, the terrain flattens
    ///     there, and only then is anything built. Carving afterwards would mean either
    ///     re-baking the collision mesh or leaving a compound with one wall in the air.
    ///   * <b>The gorge is a hole, not a dip.</b> No quad is emitted over the water, so
    ///     the bake gets no floor there and the bridges are the only crossings for free
    ///     -- the same trick the slab version used, kept.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        // ==================================================================
        // The field
        // ==================================================================
        private static float[,] _ground;
        private static bool[,] _groundPinned;

        /// <summary>
        /// Cells the smoothing pass must not touch, as opposed to ones the erosion must
        /// not touch.
        ///
        /// The two are not the same set, and treating them as one is what leaves a crease
        /// around every flattened pad. A pad is held flat so that whatever stands on it
        /// stands level -- but its *edge* is a join between a plane and a dune, and a join
        /// no filter is allowed to touch stays a hard fold in the collider forever,
        /// however smooth the shading over it looks. Rounding a pad's edge from both
        /// sides costs nothing, because the middle of a flat region is its own five-point
        /// average and the filter's reach is three cells -- far short of anything built
        /// there. What genuinely cannot move is the rim of the canyon, whose rock lip is
        /// drawn at a fixed height, and the fade at the boundary, which has to arrive at
        /// exactly zero to meet the apron.
        /// </summary>
        private static bool[,] _groundRigid;

        /// <summary>Vertices per side. One more than the number of cells.</summary>
        private static int _groundN;

        private static float _groundStep;
        private static float _groundMin;
        private static bool _groundReady;

        /// <summary>Metres of dune the terrain may dip below the apron it meets at the edge.</summary>
        private const float TerrainSink = 4f;

        /// <summary>How far past the arena boundary the ground keeps going before the apron takes over.</summary>
        private const float TerrainOverhang = 42f;

        /// <summary>
        /// The steepest the ground is ever allowed to get.
        ///
        /// Three limits stack up here and the smallest wins. Sand's own angle of repose
        /// is about thirty-three degrees; the navigation bake refuses anything past
        /// <c>agentSlope</c>, which is forty-five; the player's CharacterController stops
        /// climbing at its <c>slopeLimit</c> of fifty. But a wheeled vehicle gives up long
        /// before any of those -- and the ground under a car is not something that can be
        /// fixed later without regenerating the terrain and everything standing on it, so
        /// the number is set for the vehicle now rather than retrofitted. Twenty-six
        /// degrees is a slipface a car can take at an angle and a slope a player never
        /// notices resisting them.
        /// </summary>
        private const float ReposeDegrees = 26f;

        /// <summary>A piece of ground some later pass needs level.</summary>
        private struct Pad
        {
            public Vector2 Centre;
            public float Radius;
            public float Blend;

            /// <summary>Height to hold it at, or <c>float.NaN</c> for "whatever the dunes say here".</summary>
            public float Height;
        }

        private static readonly List<Pad> _pads = new List<Pad>();

        /// <summary>
        /// Reserves level ground. Called during the planning passes, before
        /// <see cref="BuildDuneField"/> runs -- afterwards it does nothing, because the
        /// mesh and its collider are already built.
        /// </summary>
        private static void FlattenPad(float x, float z, float radius, float blend,
                                       float height = float.NaN)
            => _pads.Add(new Pad
            {
                Centre = new Vector2(x, z),
                Radius = radius,
                Blend = Mathf.Max(1f, blend),
                Height = height
            });

        /// <summary>
        /// The height the dunes would be at a point with nothing flattened.
        ///
        /// Public to the rest of the builder so that two pads which have to end up level
        /// with each other -- a raised deck and the foot of its own ramp -- can be pinned
        /// to one number instead of each taking its own. Pinned separately they are two
        /// flat discs at two different heights with a couple of metres of sand between
        /// them, and that sand is then the steepest ground in the arena: relaxation
        /// cannot fix it, because both sides of the step are held.
        /// </summary>
        private static float NaturalHeightAt(float x, float z)
        {
            float rimFlat = _theme.hazardWidth * 0.5f + RimBandWidth;
            float offset = Mathf.Abs(x - GorgeCentreAt(z));
            float valley = Mathf.Clamp01((offset - rimFlat) / RiverValleyRamp);

            return DuneHeightAt(x, z) * Mathf.SmoothStep(0f, 1f, valley);
        }

        private static void ResetTerrain()
        {
            _ground = null;
            _groundPinned = null;
            _groundRigid = null;
            _groundReady = false;
            _pads.Clear();
        }

        /// <summary>
        /// The height of the ground under a point, in metres.
        ///
        /// Flat outside the field and flat when there is no field, so every caller can
        /// use it unconditionally and a walled arena is unaffected by any of this.
        /// </summary>
        private static float GroundHeightAt(float x, float z)
        {
            if (!_groundReady) return 0f;

            float fx = Mathf.Clamp((x - _groundMin) / _groundStep, 0f, _groundN - 1.001f);
            float fz = Mathf.Clamp((z - _groundMin) / _groundStep, 0f, _groundN - 1.001f);

            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;

            float a = Mathf.Lerp(_ground[ix, iz], _ground[ix + 1, iz], tx);
            float b = Mathf.Lerp(_ground[ix, iz + 1], _ground[ix + 1, iz + 1], tx);

            return Mathf.Lerp(a, b, tz);
        }

        /// <summary>
        /// The lowest the ground gets anywhere under a footprint.
        ///
        /// <b>This is the height a flat-bottomed rock has to be sunk to, and asking
        /// <see cref="GroundHeightAt"/> at its centre instead is the bug that made the
        /// desert's big rocks hollow.</b> Every rock in the kit is a closed shell cut off
        /// flat underneath, which is exactly right on a plane: put the cut a little below
        /// the ground and it is buried all the way round. On a dune field the ground
        /// under a fifty-metre butte or a ten-metre boulder varies by several metres, so
        /// a cut placed a metre below the *centre* is metres <i>above</i> the sand on the
        /// downhill side. What that leaves is a hole into the inside of the rock -- and
        /// the inside of a closed mesh is invisible, because every face of it is a
        /// backface, so the player walks through a gap they can see, into a space with no
        /// visible walls, and is then wedged in geometry they cannot see or climb.
        ///
        /// Sampled on two rings rather than at the rim alone, because a dune's crest can
        /// run under the middle of a footprint as easily as under its edge.
        /// </summary>
        private static float LowestGroundIn(float x, float z, float radius)
        {
            float low = GroundHeightAt(x, z);
            if (!_groundReady || radius <= 0.01f) return low;

            // Every grid cell the footprint covers, not a ring of spokes through it.
            //
            // Spokes were the first version and they are not good enough: ten of them at
            // half a forty-metre radius are eight metres apart, and a dune drops several
            // metres in eight. A butte sunk to the lowest of *those* was still seven
            // metres clear of the sand in the hollow between two of them -- which is the
            // same hole, found by the same test, one iteration later. The grid is three
            // metres, so scanning it is exact by definition and costs a few hundred
            // lookups on the biggest rock in the arena.
            int lo = Mathf.Max(0, Mathf.FloorToInt((x - radius - _groundMin) / _groundStep));
            int hi = Mathf.Min(_groundN - 1, Mathf.CeilToInt((x + radius - _groundMin) / _groundStep));
            int lz = Mathf.Max(0, Mathf.FloorToInt((z - radius - _groundMin) / _groundStep));
            int hz = Mathf.Min(_groundN - 1, Mathf.CeilToInt((z + radius - _groundMin) / _groundStep));

            float squared = radius * radius;

            for (int i = lo; i <= hi; i++)
            {
                float px = _groundMin + i * _groundStep;

                for (int j = lz; j <= hz; j++)
                {
                    float pz = _groundMin + j * _groundStep;

                    float dx = px - x, dz = pz - z;
                    if (dx * dx + dz * dz > squared) continue;

                    low = Mathf.Min(low, _ground[i, j]);
                }
            }

            return low;
        }

        /// <summary>
        /// The ground's normal under a point, so a thing lying on the sand can lie along
        /// it. Used by the flat pieces -- a slab of rock or a plank bridge over a hollow
        /// -- and deliberately not by anything upright: a fence post leaning with the
        /// hill reads as a fence about to fall over.
        /// </summary>
        private static Vector3 GroundNormalAt(float x, float z)
        {
            if (!_groundReady) return Vector3.up;

            float d = _groundStep;

            float hx = GroundHeightAt(x + d, z) - GroundHeightAt(x - d, z);
            float hz = GroundHeightAt(x, z + d) - GroundHeightAt(x, z - d);

            return new Vector3(-hx, 2f * d, -hz).normalized;
        }

        /// <summary>Lays something flat on the sand, keeping its facing but taking the slope.</summary>
        private static Quaternion GroundAlignedRotation(float x, float z, float yaw, float blend = 1f)
        {
            var normal = Vector3.Slerp(Vector3.up, GroundNormalAt(x, z), blend);
            return Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, yaw, 0f);
        }

        // ==================================================================
        // Generation
        // ==================================================================
        /// <summary>
        /// Generates the heightfield and builds it as a grid of meshed chunks.
        ///
        /// Chunked rather than one mesh for two reasons that are both about the size of
        /// this arena: a four-hundred-and-fifty-metre field at three-metre resolution is
        /// past the sixteen-bit index limit, and a single mesh is one culling unit, so
        /// the whole desert would be drawn whenever any grain of it was on screen.
        /// </summary>
        private static void BuildDuneField(Transform root, int layer, float half)
        {
            var group = new GameObject("Dunes").transform;
            group.SetParent(root, false);

            _groundStep = 3f;
            float reach = half + TerrainOverhang;
            _groundMin = -reach;

            int cells = Mathf.CeilToInt(reach * 2f / _groundStep);
            _groundN = cells + 1;

            _ground = new float[_groundN, _groundN];
            _groundPinned = new bool[_groundN, _groundN];
            _groundRigid = new bool[_groundN, _groundN];

            // <b>Only a layout that builds a river gets a river cut out of its ground.</b>
            // This hole exists so the gorge has somewhere to be and so the bake gets no
            // floor over the water. A layout with no gorge still inherited hazardWidth from
            // the theme's defaults and had a 55m channel cut through it at hazardOffset,
            // running the whole length of the map with nothing in it -- a void that split
            // the arena in two, stranded everything on the far side, and took the ground out
            // from under whatever had been placed near it.
            float edge = _theme.openZone ? _theme.hazardWidth * 0.5f : -GorgeLipOverlap * 2f;
            float rimFlat = edge + RimBandWidth;

            // ---- raw height, then everything that pins it ----
            for (int i = 0; i < _groundN; i++)
            {
                float x = _groundMin + i * _groundStep;

                for (int j = 0; j < _groundN; j++)
                {
                    float z = _groundMin + j * _groundStep;

                    // The river runs in the low ground, the way rivers do. Without that
                    // the rim is a cliff edge with a dune sitting on it -- which is a
                    // shape, but not one water makes. NaturalHeightAt applies it.
                    float h = NaturalHeightAt(x, z);

                    float offset = Mathf.Abs(x - GorgeCentreAt(z));
                    float valley = Mathf.Clamp01((offset - rimFlat) / RiverValleyRamp);

                    bool pinned = valley <= 0.001f;
                    bool rigid = pinned;

                    // Level ground somebody reserved. The nearest claim wins outright --
                    // it is not a sum, and it is not applied in turn.
                    //
                    // Applied in turn was the first version, and it is wrong in a way
                    // that is extremely hard to see: each pad lerps the height it was
                    // handed towards its own target, so the *last* pad to mention a point
                    // decides it, however far away that pad is. A raised deck sixty
                    // metres from a bridge, planned after the bridge and therefore
                    // applied after it, reached the end of the deck through the tail of
                    // its own blend and pulled the sand there most of the way down to
                    // deck height. The bridge then ended at a step no agent could climb:
                    // the navmesh stopped at the water, the far half of the level became
                    // unreachable, and the bridge was still standing there looking
                    // exactly right. Taking the smallest t instead means the claim whose
                    // flat ground a point is most inside is the one that owns it.
                    float best = 1f;
                    float bestTarget = h;

                    foreach (var pad in _pads)
                    {
                        float distance = Vector2.Distance(new Vector2(x, z), pad.Centre);
                        if (distance > pad.Radius + pad.Blend) continue;

                        float t = Mathf.SmoothStep(0f, 1f,
                            Mathf.Clamp01((distance - pad.Radius) / pad.Blend));

                        if (t >= best) continue;

                        best = t;
                        bestTarget = float.IsNaN(pad.Height)
                            ? NaturalHeightAt(pad.Centre.x, pad.Centre.y)
                            : pad.Height;
                    }

                    if (best < 1f)
                    {
                        h = Mathf.Lerp(bestTarget, h, best);
                        if (best <= 0.001f) pinned = true;
                    }

                    // Down to nothing at the boundary, because the apron past it is flat
                    // at zero and a dune walking off the edge of the level is a cliff
                    // hanging over the horizon.
                    float fade = Mathf.Clamp01((reach - Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)))
                                               / TerrainOverhang);
                    h *= Mathf.SmoothStep(0f, 1f, fade);

                    if (fade <= 0.001f) { pinned = true; rigid = true; }

                    _ground[i, j] = h;
                    _groundPinned[i, j] = pinned;
                    _groundRigid[i, j] = rigid;
                }
            }

            RelaxToRepose();
            SmoothField();
            _groundReady = true;

            // ---- mesh it ----
            // <b>Which surface, from the theme.</b> Hard-coded to Sand this drew wind
            // ripples on every heightfield, which is right for a dune field and wrong for
            // anything else that wants relief -- an abandoned park on rippled sand reads as
            // a desert with rides standing in it. The tiling stays as it was so the desert
            // is unchanged.
            string detail = string.IsNullOrEmpty(_theme.floorDetail) ? "Sand" : _theme.floorDetail;

            var sand = MakeDetailMaterial("Ground", _theme.floorColor, detail, 0.09f,
                                          _theme.floorSmoothness * 0.4f, 0f, 1.15f);

            const int chunkCells = 30;
            int chunks = Mathf.CeilToInt(cells / (float)chunkCells);

            for (int cx = 0; cx < chunks; cx++)
                for (int cz = 0; cz < chunks; cz++)
                    BuildDuneChunk(group, layer, sand, cx * chunkCells, cz * chunkCells,
                                   Mathf.Min(chunkCells, cells - cx * chunkCells),
                                   Mathf.Min(chunkCells, cells - cz * chunkCells));
        }

        /// <summary>
        /// The width of flat ground held either side of the water, measured out from the
        /// water's edge. It has to clear <see cref="FenceStandOff"/>, because the fence
        /// is a straight run of rigid panels and a dune under it would leave every third
        /// one hanging.
        /// </summary>
        private const float RimBandWidth = 21f;

        /// <summary>
        /// How far it takes the dunes to come back up to full height away from the river.
        ///
        /// Every metre of this is a metre of flat ground, and it is spent twice -- once
        /// either side of the river -- on top of the flat rim band. The river sits ninety
        /// metres east of the middle, so the far bank is only about a hundred metres wide
        /// to begin with: at the value this started at, half of it was a plain and the
        /// arena had dunes on one side of the water and not the other.
        ///
        /// Short enough is better than smooth enough here, because the relaxation makes
        /// up the difference. Ramping twenty metres up to a dune fourteen metres tall
        /// asks for a slope far past the angle of repose, and what the erosion does with
        /// that is cut it back to exactly the repose angle -- which comes out as a
        /// terrace edge standing over the river terrace, and is a better thing to have
        /// there than a long featureless ramp.
        /// </summary>
        private const float RiverValleyRamp = 20f;

        /// <summary>
        /// The dune profile: two crossed trains of asymmetric ridges, a broad basin
        /// field under them, and wind ripples on top.
        ///
        /// The asymmetry is the point. A sine wave is a series of identical mounds and
        /// reads as corrugation from any height; a real dune has a long shallow windward
        /// back and a short steep slipface, so which way you approach it from decides
        /// whether it is a ramp or a wall. That single fact is most of what makes dune
        /// ground interesting to fight on.
        /// </summary>
        private static float DuneHeightAt(float x, float z)
        {
            float wind = _theme.duneWindAngle * Mathf.Deg2Rad;
            float cos = Mathf.Cos(wind), sin = Mathf.Sin(wind);

            float across = x * cos + z * sin;
            float along = -x * sin + z * cos;

            float amplitude = _theme.duneHeight;
            float wavelength = Mathf.Max(20f, _theme.duneWavelength);

            // The crests wander along their own length rather than running dead
            // straight from one side of the map to the other.
            float meander = Fbm2(along * 0.006f, 0f, _theme.randomSeed * 7 + 3, 3) * wavelength * 0.45f;
            float primary = DuneProfile((across + meander) / wavelength) * amplitude;

            // A second train at an angle to the first, which is what turns parallel
            // ridges into the crescents and blowouts a real dune field is made of.
            float secondAcross = x * 0.77f - z * 0.64f;
            float secondMeander = Fbm2(x * 0.004f, z * 0.004f, _theme.randomSeed * 13 + 9, 3) * 34f;
            float secondary = DuneProfile((secondAcross + secondMeander) / (wavelength * 0.62f))
                            * amplitude * 0.42f;

            // Broad relief under everything: the basins between dune ranges, and the
            // reason the map is not the same height wherever you stand.
            float basin = Fbm2(x * 0.0032f, z * 0.0032f, _theme.randomSeed * 17 + 21, 3)
                        * amplitude * 1.6f;

            // And one scale in between, at about a hundred metres. This is the octave
            // that does the most for how the level plays: too long and the map is one
            // slow tilt, too short and it is chop the player walks through without ever
            // being above or below anything. At a hundred metres a rise is a thing you
            // decide whether to go over or around.
            float hills = Fbm2(x * 0.0092f, z * 0.0092f, _theme.randomSeed * 29 + 5, 3)
                        * amplitude * 0.9f;

            // No ripples in here, deliberately, though sand obviously has them.
            //
            // A wind ripple is a couple of metres from crest to crest and the grid is
            // three, so putting one in the heightfield is sampling it below its own
            // Nyquist rate: what comes out is not ripples but a field of hard little
            // kinks at the grid spacing, in a random pattern that changes with the seed.
            // On foot it is invisible. Under a wheel it is a surface that chatters
            // everywhere and is smooth nowhere, and no amount of suspension tuning fixes
            // ground that is genuinely jagged. So the ripples live in the sand normal
            // map instead, where they cost nothing, are the right size, and are not
            // something a vehicle can drive on.
            return primary + secondary + basin + hills - amplitude * 0.9f;
        }

        /// <summary>
        /// One dune, from windward toe to slipface toe, as a function of 0..1 repeated.
        /// Smoothstep on both sides so the surface has no crease in it, and the crest
        /// deliberately off centre.
        /// </summary>
        private static float DuneProfile(float t)
        {
            t -= Mathf.Floor(t);

            const float crest = 0.74f;

            return t < crest
                ? Mathf.SmoothStep(0f, 1f, t / crest)
                : Mathf.SmoothStep(1f, 0f, (t - crest) / (1f - crest));
        }

        /// <summary>
        /// Lets sand fall downhill until nothing is steeper than the angle of repose.
        ///
        /// This is thermal erosion, run as a handful of Gauss-Seidel sweeps. It is here
        /// as a guarantee rather than as an effect: summed waves overshoot wherever two
        /// slipfaces land on top of each other, and a patch of ground at fifty degrees
        /// is a patch the navmesh will not bake and the player cannot climb, sitting in
        /// the middle of a level that looks completely normal. That it also rounds the
        /// crests and piles a toe at the bottom of each slipface -- which is what makes
        /// the field read as sand rather than as mathematics -- is the bonus.
        ///
        /// Pinned cells never move: the rim of the gorge, the pads, and the fade at the
        /// boundary all have something already built to their height.
        /// </summary>
        private static void RelaxToRepose()
        {
            float repose = Mathf.Tan(ReposeDegrees * Mathf.Deg2Rad);

            // A sweep moves sand one cell, so the number of them is a distance: thirty
            // cells is ninety metres, which is what it takes to carry the correction out
            // of the middle of a flattened pad and off the end of its blend. At ten the
            // steepest ground in the arena was still on the lip of a pad, because the
            // relaxation simply had not reached that far before it stopped.
            const int sweeps = 30;

            // Eight neighbours, not four -- and the diagonals are the whole reason.
            //
            // Constraining only north/south/east/west bounds every *edge* of the grid at
            // the repose angle and leaves the diagonal free: two cells that are each
            // 26 degrees below their shared neighbour are 35 degrees apart from each
            // other. That is not a technicality, because the mesh is triangulated along
            // one of those diagonals, so the surface a wheel actually runs on has that
            // 35 degrees in it. It cost a while to find: every grid edge measured clean
            // and the collider was measurably steeper than any of them.
            var dx = new[] { 1, -1, 0, 0, 1, 1, -1, -1 };
            var dz = new[] { 0, 0, 1, -1, 1, -1, 1, -1 };

            var maxDrop = new float[8];
            for (int k = 0; k < 8; k++)
                maxDrop[k] = repose * _groundStep * (dx[k] != 0 && dz[k] != 0 ? Mathf.Sqrt(2f) : 1f);

            for (int s = 0; s < sweeps; s++)
                for (int i = 0; i < _groundN; i++)
                    for (int j = 0; j < _groundN; j++)
                        for (int k = 0; k < 8; k++)
                        {
                            int ni = i + dx[k], nj = j + dz[k];
                            if (ni < 0 || nj < 0 || ni >= _groundN || nj >= _groundN) continue;

                            float drop = _ground[i, j] - _ground[ni, nj];
                            if (drop <= maxDrop[k]) continue;

                            float move = (drop - maxDrop[k]) * 0.3f;

                            if (!_groundPinned[i, j]) _ground[i, j] -= move;
                            if (!_groundPinned[ni, nj]) _ground[ni, nj] += move;
                        }
        }

        /// <summary>
        /// Rounds off what the relaxation leaves behind.
        ///
        /// Thermal erosion works by moving sand between neighbouring cells until no pair
        /// of them is too steep, and it stops the instant that is true -- so what it
        /// hands back is a surface made of planes meeting at the limiting angle, with a
        /// crease along every join. Smooth vertex normals hide those creases from the
        /// eye completely. They do not hide them from a collider: the surface a wheel or
        /// a capsule actually runs on is the triangles, and a crease is a bump felt at
        /// every single one.
        ///
        /// Four passes of a five-point binomial filter take the creases out while leaving
        /// the dunes -- the filter's reach is one cell per pass, so it cannot touch
        /// anything bigger than about twelve metres, and every shape on this map is
        /// measured in tens. It runs over flattened pads as well, and only
        /// <see cref="_groundRigid"/> holds it off, which is what turns the join between
        /// a pad and the open dune beside it from a fold into a curve.
        /// </summary>
        private static void SmoothField()
        {
            const int passes = 4;

            for (int p = 0; p < passes; p++)
            {
                var next = (float[,])_ground.Clone();

                for (int i = 1; i < _groundN - 1; i++)
                    for (int j = 1; j < _groundN - 1; j++)
                    {
                        if (_groundRigid[i, j]) continue;

                        next[i, j] = _ground[i, j] * 0.4f
                                   + (_ground[i - 1, j] + _ground[i + 1, j] +
                                      _ground[i, j - 1] + _ground[i, j + 1]) * 0.15f;
                    }

                _ground = next;
            }
        }

        // ==================================================================
        /// <summary>
        /// One chunk of the field. Vertices are shared inside the chunk and normals come
        /// from the height grid rather than from the triangles, so neighbouring chunks
        /// agree exactly at their shared edge -- computed per mesh they disagree by a
        /// hair, and a hair is enough for a visible lighting seam every thirty metres in
        /// both directions.
        /// </summary>
        private static void BuildDuneChunk(Transform parent, int layer, Material material,
                                           int originI, int originJ, int cellsI, int cellsJ)
        {
            if (cellsI <= 0 || cellsJ <= 0) return;

            int nx = cellsI + 1, nz = cellsJ + 1;

            var vertices = new Vector3[nx * nz];
            var normals = new Vector3[nx * nz];
            var uvs = new Vector2[nx * nz];
            var triangles = new List<int>(cellsI * cellsJ * 6);

            // <b>Only a layout that builds a river gets a river cut out of its ground.</b>
            // This hole exists so the gorge has somewhere to be and so the bake gets no
            // floor over the water. A layout with no gorge still inherited hazardWidth from
            // the theme's defaults and had a 55m channel cut through it at hazardOffset,
            // running the whole length of the map with nothing in it -- a void that split
            // the arena in two, stranded everything on the far side, and took the ground out
            // from under whatever had been placed near it.
            float edge = _theme.openZone ? _theme.hazardWidth * 0.5f : -GorgeLipOverlap * 2f;

            // The hole is cut wider than the water so that the gorge's own lip, which is
            // a smooth curve, has somewhere to overlap the grid's staircase edge.
            float holeHalf = edge + GorgeLipOverlap;

            bool Wet(int i, int j)
            {
                float x = _groundMin + i * _groundStep;
                float z = _groundMin + j * _groundStep;
                return Mathf.Abs(x - GorgeCentreAt(z)) < holeHalf;
            }

            for (int a = 0; a < nx; a++)
                for (int b = 0; b < nz; b++)
                {
                    int i = originI + a, j = originJ + b;
                    float x = _groundMin + i * _groundStep;
                    float z = _groundMin + j * _groundStep;

                    int index = a * nz + b;

                    vertices[index] = new Vector3(x, _ground[i, j], z);
                    uvs[index] = new Vector2(x, z);

                    int il = Mathf.Max(i - 1, 0), ir = Mathf.Min(i + 1, _groundN - 1);
                    int jd = Mathf.Max(j - 1, 0), ju = Mathf.Min(j + 1, _groundN - 1);

                    normals[index] = new Vector3(
                        _ground[il, j] - _ground[ir, j],
                        (ir - il) * _groundStep,
                        _ground[i, jd] - _ground[i, ju]).normalized;
                }

            for (int a = 0; a < cellsI; a++)
                for (int b = 0; b < cellsJ; b++)
                {
                    int i = originI + a, j = originJ + b;

                    if (Wet(i, j) || Wet(i + 1, j) || Wet(i, j + 1) || Wet(i + 1, j + 1)) continue;

                    int v00 = a * nz + b;
                    int v10 = (a + 1) * nz + b;
                    int v01 = a * nz + (b + 1);
                    int v11 = (a + 1) * nz + (b + 1);

                    triangles.Add(v00); triangles.Add(v01); triangles.Add(v11);
                    triangles.Add(v00); triangles.Add(v11); triangles.Add(v10);
                }

            if (triangles.Count == 0) return;

            var mesh = new Mesh { name = $"Dunes_{originI}_{originJ}" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            var chunk = MeshObject(parent, "Dune", mesh, material, Vector3.zero, Quaternion.identity,
                                   Vector3.one, layer, SandTag);

            // The map draws footprints of solid things, and a dune is both solid and the
            // size of the map. Left to the height test it is not ground -- it is metres
            // tall -- so it would be kept, and the minimap would be one grey rectangle.
            var marker = chunk.AddComponent<MinimapMarker>();
            marker.style = MinimapMarker.Style.Hidden;
        }

        /// <summary>How far the gorge's rock lip reaches out over the sand, hiding the grid's edge.</summary>
        private const float GorgeLipOverlap = 7f;
    }
}
#endif
