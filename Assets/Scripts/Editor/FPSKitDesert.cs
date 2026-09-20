#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// What the open zone is actually made of: the canyon and its river, the fence along
    /// the rim, the bridges, the compounds, and the rock and scrub in between.
    ///
    /// <see cref="FPSKitOpenZone"/> decides where things go. This file decides what they
    /// look like when they get there, and it exists because the answer used to be "a
    /// cube". Every structure in the kit was an axis-aligned box with one flat colour on
    /// it: a boulder was a box, a cliff was four boxes stepping down, a tree was nothing
    /// at all because there is no box that is a tree. That is a completely reasonable
    /// first pass -- it is readable, it is fast, and it is tunable -- and it has one
    /// failure it cannot be tuned out of, which is that a desert made of boxes reads as
    /// a warehouse that somebody painted sand-coloured.
    ///
    /// The rule followed here is that a shape earns its triangles by doing something a
    /// box cannot:
    ///
    ///   * a <b>canyon wall</b> has strata, a bench halfway down and an overhang, so it
    ///     reads as cut by water rather than as a hole with sides;
    ///   * a <b>boulder</b> has a flat bottom and no two faces alike, so a field of them
    ///     is a field of rocks rather than a field of one rock;
    ///   * a <b>fence</b> has footings, posts, rails and pickets, so it reads as
    ///     something built to stop you going over the edge -- which is the sentence the
    ///     old two-centimetre rail was trying and failing to say;
    ///   * a <b>palm</b> has a bent trunk and an arched crown, so the eye has something
    ///     of known size to judge two hundred metres of empty sand against.
    ///
    /// The cost is watched in two places. Anything repeated is one mesh reused
    /// (<c>_meshPool</c>), and anything drawn in a long run -- the fence, the canyon --
    /// is welded into a handful of chunked meshes rather than left as hundreds of
    /// objects, because this game has to fit in a browser tab and a draw call costs more
    /// there than a triangle does.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        // ==================================================================
        // Materials
        // ==================================================================
        private static Material _sandMat, _rockMat, _rockPaleMat, _rockDarkMat, _rockWetMat;
        private static Material _timberMat, _steelMat, _waterMat, _adobeMat, _canvasMat;
        private static Material _foliageMat, _deadwoodMat, _hessianMat, _signMat;

        /// <summary>
        /// The outdoor palette, resolved once per build.
        ///
        /// Every one of these is the theme's own colour with a detail map on it, never a
        /// colour baked into the texture -- see <see cref="MakeDetailMaterial"/>. That is
        /// what keeps a <see cref="LevelTheme"/> in charge of what an arena looks like:
        /// retune <c>bankColor</c> and the cliffs, the boulders and the boundary ridge
        /// all move together, because they are all the same sandstone.
        /// </summary>
        private static void ResolveOutdoorMaterials()
        {
            _sandMat = MakeDetailMaterial("Sand", _theme.floorColor, "Sand", 0.09f,
                                          _theme.floorSmoothness * 0.4f, 0f, 1.15f);

            // Lifted well above bankColor, and the reason is worth writing down. The
            // theme's bank colour is picked against the sky and the fog, where it looks
            // like warm stone; used raw on rock standing in full sun next to sand that
            // is a third brighter again, the same value reads as charcoal. Every boulder
            // on the map came out looking burnt. Rock in a desert is *pale* -- it is the
            // same mineral the sand is -- so these are the bank colour opened up rather
            // than a second palette, which keeps a theme retune in control of all of it.
            _rockMat = MakeDetailMaterial("Rock", Shade(_theme.bankColor, 1.18f), "Rock",
                                          0.13f, 0.1f, 0f, 1.3f);
            _rockPaleMat = MakeDetailMaterial("RockPale", Shade(_theme.bankColor, 1.52f), "Rock",
                                              0.11f, 0.08f, 0f, 1.3f);

            // Iron stain rather than shadow. The dark rock started as the bank colour
            // turned down, which on a map where everything else is pale sand read as
            // burnt; turned up to match, it vanished into the sand instead and the whole
            // arena went one flat tone. Pulling the green and blue down while leaving the
            // red gives a stone that is clearly darker without being grey -- which is
            // also what iron in sandstone actually does, and it is the only colour on
            // this map that is not a shade of the two the theme names.
            var stain = new Color(_theme.bankColor.r * 1.02f, _theme.bankColor.g * 0.74f,
                                  _theme.bankColor.b * 0.6f, 1f);

            _rockDarkMat = MakeDetailMaterial("RockDark", stain, "Rock", 0.15f, 0.12f, 0f, 1.4f);

            // Below the waterline everything is darker and shinier, which is the whole
            // reason a river bank reads as wet from the rim rather than as a change of
            // rock somebody chose.
            // Damp, not lacquered. At the smoothness a wet surface wants, sandstone with
            // a strong normal map on it came back as polished chrome -- every boulder at
            // the waterline was a mirror, which is the one material a desert has none of.
            _rockWetMat = MakeDetailMaterial("RockWet", Shade(_theme.bankColor, 0.72f), "Rock",
                                             0.18f, 0.26f, 0f, 0.8f);

            _timberMat = MakeDetailMaterial("Timber", new Color(0.44f, 0.33f, 0.22f), "Timber",
                                            0.5f, 0.12f, 0f, 1f);
            _deadwoodMat = MakeDetailMaterial("Deadwood", new Color(0.52f, 0.46f, 0.37f), "Timber",
                                              1.1f, 0.1f, 0f, 1f);
            _steelMat = MakeDetailMaterial("Steel", new Color(0.31f, 0.29f, 0.27f), "Rock",
                                           0.9f, 0.32f, 0.65f, 0.5f);
            _hessianMat = MakeDetailMaterial("Hessian", Shade(_theme.floorColor, 0.82f), "Timber",
                                             1.6f, 0.08f, 0f, 1.2f);
            _canvasMat = MakeDetailMaterial("Canvas", new Color(0.74f, 0.68f, 0.52f), "Timber",
                                            0.45f, 0.1f, 0f, 0.6f);
            _adobeMat = MakeDetailMaterial("Adobe", _theme.wallColor, "Adobe", 0.28f,
                                           _theme.wallSmoothness, 0f, 0.9f);
            _foliageMat = MakeDetailMaterial("Foliage", new Color(0.33f, 0.37f, 0.19f), "Timber",
                                             0.8f, 0.18f, 0f, 0.6f);
            _waterMat = MakeDetailMaterial("Water", _theme.hazardColor, "Water", 0.035f, 0.94f, 0.08f, 0.8f);

            // The one thing on this map that is meant to be seen before anything else it
            // is next to, so it is the one thing not wearing the theme's palette.
            _signMat = MakeDetailMaterial("HazardSign", new Color(0.86f, 0.42f, 0.08f), "Timber",
                                          0.8f, 0.25f, 0.1f, 0.4f);
        }

        // ==================================================================
        // The canyon
        // ==================================================================
        /// <summary>Metres of canyon drawn past the boundary, so the river leaves the level.</summary>
        private const float CanyonOverrun = 80f;

        /// <summary>How far the rock lip reaches out from the water's edge onto the sand.</summary>
        private const float RimLipReach = 13f;

        /// <summary>Metres from the water's edge out to the fence line.</summary>
        private const float FenceStandOff = 16f;

        /// <summary>
        /// How far a bridge deck stands above the sand at its landing. Small, and it has
        /// to be bigger than the deepest of <see cref="CanyonLipDepths"/> is shallow, or
        /// the canyon's lip comes back up through the deck.
        /// </summary>
        private const float DeckLift = 0.14f;

        /// <summary>The height of the river's surface.</summary>
        private static float WaterSurfaceY => -_theme.hazardDepth + 3f;

        /// <summary>
        /// The profile of one canyon wall, from the lip down to the bed.
        ///
        /// Read as (how far in from the lip, how far down). Three things in it are doing
        /// work and none of them are decoration:
        ///
        ///   * the first two rows are nearly flat and nearly six metres wide, because
        ///     they are what covers the staircase left where the terrain grid stopped --
        ///     a heightfield can only end on a cell boundary and the river does not;
        ///   * row five steps *out* rather than in, which is a bench. A cliff that goes
        ///     down in one clean sweep reads as a trench; a bench halfway down is what
        ///     says a river cut this over a long time, and it catches the light
        ///     differently from everything above and below it;
        ///   * the bottom two rows lie back rather than dropping, which is the talus --
        ///     the rock that has already fallen -- and it is what stops the water looking
        ///     like it is in a box.
        /// </summary>
        private static readonly float[] CanyonInsets = { 0f, 6f, 9.5f, 13f, 15f, 18.5f, 19.8f, 22.5f, 27f };

        /// <summary>Fractions of <c>hazardDepth</c> below the rim. Row zero is above it, on the sand.</summary>
        /// <remarks>
        /// The first two rows do not use this. They are the lip, they are set in absolute
        /// centimetres by <see cref="CanyonLipDepths"/>, and the reason is that they have
        /// to be threaded between two other surfaces rather than placed at a depth.
        /// </remarks>
        private static readonly float[] CanyonDrops = { 0f, 0f, 0.073f, 0.26f, 0.4f, 0.47f, 0.7f, 0.86f, 0.965f };

        /// <summary>
        /// The two lip rows, in metres below the rim -- not as a fraction of the depth.
        ///
        /// These are small numbers doing a fiddly job. The lip overlaps the sand, because
        /// a heightfield can only end on a cell boundary and the river does not, so the
        /// terrain's edge is a staircase and something has to cover it. Which of the two
        /// is on top decides what the player sees:
        ///
        ///   * above the sand, and the lip is a rock shelf standing a few centimetres
        ///     proud of the ground -- fine to look at, and a bump to drive over. Worse,
        ///     it crosses the bridge decks, which are flush with the rim, so it laid a
        ///     ridge across both approaches of every crossing.
        ///   * below the sand, and the sand hides it wherever the sand exists. The step
        ///     is visible only along the jagged line where the terrain stops, it is two
        ///     centimetres, and nothing above it is disturbed.
        ///
        /// Below, therefore -- and far enough below that the bridge deck, lifted by
        /// <see cref="DeckLift"/>, clears it by a comfortable margin.
        /// </summary>
        private static readonly float[] CanyonLipDepths = { 0.02f, 0.06f };

        /// <summary>
        /// Where the pale/mid/dark strata change. Bands share their boundary rows, so
        /// there is no crack between them.
        ///
        /// The split is also what decides where the navmesh stops, and that is the
        /// reason it falls at row two rather than anywhere prettier. Band zero is the
        /// flat shelf at the top: it has to stay walkable, because the bridge decks are
        /// flush with it and a lip laid unwalkable over the end of a bridge cuts the
        /// crossing in half -- silently, since the bridge is still visibly there and the
        /// path around it still completes. Everything below row two is held off the bake
        /// by <see cref="NoStanding"/>, because the bench and the talus are both
        /// shallower than the agent slope and would otherwise bake as islands twenty
        /// metres down, inside the kill volume, for the spawner to find.
        /// </summary>
        private static readonly int[] CanyonBands = { 0, 2, 5, 8 };

        /// <summary>
        /// The cut itself: rock down both sides, a bed at the bottom, water on top of the
        /// bed, and the trigger that makes falling in mean something.
        /// </summary>
        private static void BuildGorge(Transform root, int layer, float half)
        {
            var group = new GameObject("Gorge").transform;
            group.SetParent(root, false);

            float depth = _theme.hazardDepth;
            float edge = _theme.hazardWidth * 0.5f;
            float outer = edge + RimLipReach;
            float bedHalf = outer - CanyonInsets[CanyonInsets.Length - 1];

            bool dry = _theme.hazard == LevelTheme.Hazard.Chasm;

            float from = -half - CanyonOverrun;
            float to = half + CanyonOverrun;
            const float step = 4.5f;
            int slices = Mathf.CeilToInt((to - from) / step);

            int seed = _theme.randomSeed * 61 + 5;

            // ---- the two walls ----
            for (int side = -1; side <= 1; side += 2)
            {
                // Every row of every slice, worked out once. The bands share their
                // boundary rows, so computing them per band would leave a hairline of
                // skybox down the canyon wherever the two disagreed in the last decimal.
                var grid = new Vector3[slices + 1][];

                for (int i = 0; i <= slices; i++)
                {
                    float z = from + i * step;
                    float centre = GorgeCentreAt(z);

                    grid[i] = new Vector3[CanyonInsets.Length];

                    for (int k = 0; k < CanyonInsets.Length; k++)
                    {
                        // The lip is left alone; everything below it is eaten into. Noise
                        // on row zero would expose the terrain's grid edge, which is the
                        // one thing the lip is there to hide.
                        float bite = k < 2 ? 0f
                                   : Fbm2(z * 0.035f, k * 1.7f, seed + side * 77, 3) * 3.2f;

                        float sag = k < 2 ? 0f
                                  : Fbm2(z * 0.021f, k * 2.9f, seed + side * 31 + 9, 2) * depth * 0.045f;

                        float inset = Mathf.Min(CanyonInsets[k] + bite, outer - 2.5f);

                        float y = k < CanyonLipDepths.Length
                            ? -CanyonLipDepths[k]
                            : -depth * CanyonDrops[k] + sag;

                        grid[i][k] = new Vector3(centre + side * (outer - inset), y, z);
                    }
                }

                var materials = new[] { _rockPaleMat, _rockMat, _rockWetMat };
                string sideName = side < 0 ? "West" : "East";

                for (int b = 0; b < CanyonBands.Length - 1; b++)
                {
                    var build = new MeshBuild { UVScale = 0.16f };

                    for (int i = 0; i < slices; i++)
                        for (int k = CanyonBands[b]; k < CanyonBands[b + 1]; k++)
                            build.Quad(grid[i][k], grid[i + 1][k], grid[i + 1][k + 1], grid[i][k + 1]);

                    var wall = MeshObject(group, $"Cliff{sideName}_{b}", build.ToMesh($"Cliff_{sideName}_{b}"),
                                          materials[b], Vector3.zero, Quaternion.identity, Vector3.one,
                                          layer, _theme.wallTag);

                    // The top shelf stays on the bake; see CanyonBands.
                    if (b > 0) NoStanding(wall);

                    Hide(wall);
                }

                // Rock that has come off the wall and is lying at the foot of it, half in
                // the water. Without this the bed meets the cliff on a clean line, which
                // is the one thing erosion never leaves behind.
                var rubble = new System.Random(seed + side * 13);

                for (float z = from; z < to; z += Rand(rubble, 5f, 14f))
                {
                    float centre = GorgeCentreAt(z);
                    float offset = bedHalf * Rand(rubble, 0.62f, 1.05f);
                    float size = Rand(rubble, 1.4f, 4.4f) / BoulderSpread;

                    var boulder = MeshObject(group, "Rubble", BoulderMesh(rubble.Next(1, 999), 0.4f, 0.8f),
                                             Rand(rubble, 0f, 1f) < 0.5f ? _rockWetMat : _rockDarkMat,
                                             new Vector3(centre + side * offset,
                                                         -depth + Rand(rubble, 0.4f, 2.6f), z),
                                             Quaternion.Euler(Rand(rubble, -20f, 20f), Rand(rubble, 0f, 360f),
                                                              Rand(rubble, -20f, 20f)),
                                             Vector3.one * size, layer, _theme.wallTag);

                    NoStanding(boulder);
                    Hide(boulder);
                }
            }

            // ---- the bed ----
            BuildRiverBed(group, layer, from, to, step, slices, bedHalf, depth, seed);

            // ---- the water ----
            if (!dry) BuildRiver(group, layer, from, to, bedHalf);

            BuildKillVolume(group, half, depth);
        }

        /// <summary>
        /// The floor of the canyon: a shallow V of wet rock, noisy along its length so
        /// the water sitting on it is not a perfectly even depth everywhere.
        /// </summary>
        private static void BuildRiverBed(Transform parent, int layer, float from, float to, float step,
                                          int slices, float bedHalf, float depth, int seed)
        {
            var build = new MeshBuild { UVScale = 0.2f };
            const int across = 6;

            Vector3 Point(int i, int k)
            {
                float z = from + i * step;
                float centre = GorgeCentreAt(z);
                float t = k / (float)across * 2f - 1f;                     // -1 .. 1

                float y = -depth - 0.9f + Mathf.Abs(t) * 0.9f
                        + Fbm2(z * 0.05f, t * 2f, seed + 401, 2) * 0.8f;

                return new Vector3(centre + t * bedHalf, y, z);
            }

            for (int i = 0; i < slices; i++)
                for (int k = 0; k < across; k++)
                    build.Quad(Point(i, k), Point(i, k + 1), Point(i + 1, k + 1), Point(i + 1, k));

            var bed = MeshObject(parent, "Bed", build.ToMesh("RiverBed"), _rockWetMat,
                                 Vector3.zero, Quaternion.identity, Vector3.one, layer, _theme.wallTag);

            NoStanding(bed);
            Hide(bed);
        }

        /// <summary>
        /// The river, cut into slices that follow the meander.
        ///
        /// Sliced rather than welded into one mesh purely for the minimap, which draws
        /// the footprint of what it is given: one mesh four hundred metres long and
        /// sixty wide comes back as an axis-aligned rectangle covering a third of the
        /// map, with the bend in the river nowhere in it. A slice per fifteen metres is
        /// a chain of small rectangles that follows the water, which is the one thing
        /// about this level a player needs the map to tell them.
        /// </summary>
        private static void BuildRiver(Transform parent, int layer, float from, float to, float bedHalf)
        {
            var group = new GameObject("River").transform;
            group.SetParent(parent, false);

            const float slice = 15f;
            int count = Mathf.CeilToInt((to - from) / slice);

            var quad = Pooled("waterquad", () =>
            {
                var build = new MeshBuild { UVScale = 1f };
                build.Quad(new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f),
                           new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f));
                return build.ToMesh("WaterQuad");
            });

            for (int i = 0; i < count; i++)
            {
                float z = from + slice * (i + 0.5f);

                // Turned to follow the centre line, so consecutive slices meet along
                // their shared edge instead of stepping past each other.
                float ahead = GorgeCentreAt(z + slice * 0.5f);
                float behind = GorgeCentreAt(z - slice * 0.5f);
                float yaw = Mathf.Atan2(ahead - behind, slice) * Mathf.Rad2Deg;

                var water = MeshObject(group, "Water", quad, _waterMat,
                                       new Vector3(GorgeCentreAt(z), WaterSurfaceY, z),
                                       Quaternion.Euler(0f, yaw, 0f),
                                       new Vector3(bedHalf * 2.1f, 1f, slice + 2f),
                                       layer, "Water", collider: false);

                NoStanding(water);
                Mark(water, _theme.hazardColor, order: 1);
            }

            // One component for the whole river rather than one per slice: it collects
            // the renderers once and pushes a scrolling UV offset through a property
            // block, so nothing instances a material and nothing leaks one.
            group.gameObject.AddComponent<ScrollingWater>().scrollSpeed = new Vector2(0.012f, 0.05f);
        }

        /// <summary>The trigger is cut into slices of this many metres of z.</summary>
        /// <remarks>
        /// Short enough that the meander cannot outrun a box: the centre line moves by
        /// up to 0.62 m per metre of z, so ten metres of slice is six of drift, and the
        /// slice is sized to cover every centre line inside it rather than the one at
        /// its middle.
        /// </remarks>
        private const float KillSliceLength = 10f;

        /// <summary>
        /// The trigger that makes falling in mean something: a chain of boxes following
        /// the water, with its lid just above the surface.
        ///
        /// <b>Both of those are a fix and the same bug in two halves.</b> What was here
        /// was one box, axis aligned, as wide as the river plus two and a half times the
        /// meander and with its lid five metres under the rim. Written that way it does
        /// not describe the river at all -- it describes a hundred-and-thirty-metre
        /// corridor of the arena that the river happens to wander about inside, and the
        /// lid is five metres below <i>zero</i> rather than five metres below the ground.
        ///
        /// <para>
        /// That was survivable on a flat floor and lethal the moment the floor became a
        /// dune field, because a dune field has basins in it. The sand on the western
        /// approach bottoms out thirteen metres down -- eight metres inside a lid set at
        /// minus five -- so four thousand square metres of perfectly ordinary walkable
        /// sand, nowhere near the water, killed the player outright the instant they
        /// stepped onto it. What that reads as from inside the game is "I walked towards
        /// the river and died", which is exactly what it is, and nothing about it looks
        /// like a trigger: there is no water, no edge and no fall.
        /// </para>
        ///
        /// <para>
        /// So the volume is cut to the shape of the thing it represents. Each slice
        /// spans the bed plus the foot of the talus at that latitude, and the lid sits
        /// two and a half metres over the water -- below every walkable surface in the
        /// canyon by a wide margin, since the top shelf is a metre and a half down and
        /// the bench below it is unwalkably steep on both sides. You die by reaching the
        /// water, which is what the fence and the bridge railings have been saying all
        /// along, and you can stand on the rim and look at it.
        /// </para>
        ///
        /// <para>
        /// Several colliders on one object rather than one object each: they are a
        /// compound collider, a trigger message arrives for whichever of them was
        /// entered, and one <see cref="KillVolume"/> answers for all of them.
        /// </para>
        /// </summary>
        private static void BuildKillVolume(Transform parent, float half, float depth)
        {
            var go = new GameObject("KillVolume");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;

            // The same arithmetic BuildGorge uses for the floor of the cut, so the
            // trigger cannot disagree with the canyon about how wide the bottom is.
            float outer = _theme.hazardWidth * 0.5f + RimLipReach;
            float reach = outer - CanyonInsets[CanyonInsets.Length - 1] + 7f;

            float lid = WaterSurfaceY + 2.5f;
            float floor = -depth - 10f;

            float from = -half - CanyonOverrun;
            float to = half + CanyonOverrun;
            int slices = Mathf.CeilToInt((to - from) / KillSliceLength);

            for (int i = 0; i < slices; i++)
            {
                float z0 = from + i * KillSliceLength;
                float z1 = Mathf.Min(z0 + KillSliceLength, to);

                float west = float.MaxValue, east = float.MinValue;

                for (int s = 0; s <= 4; s++)
                {
                    float centre = GorgeCentreAt(Mathf.Lerp(z0, z1, s / 4f));
                    west = Mathf.Min(west, centre);
                    east = Mathf.Max(east, centre);
                }

                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = new Vector3((west + east) * 0.5f, (lid + floor) * 0.5f,
                                         (z0 + z1) * 0.5f);
                box.size = new Vector3(east - west + reach * 2f, lid - floor,
                                       z1 - z0 + 0.5f);
            }

            var kill = go.AddComponent<KillVolume>();
            kill.instantKill = _theme.hazard != LevelTheme.Hazard.Electrified;
            kill.damagePerSecond = 70f;

            // No splash in the audio library yet, and a wrong sound is worse than none:
            // Tools/generate-placeholder-audio.py is where one would be authored, and it
            // has to go at the end of that script or every clip below it is re-rolled.
            kill.enterClip = null;
        }

        // ==================================================================
        // The fence
        // ==================================================================
        /// <summary>
        /// The barrier along both rims, with the bridge approaches left open.
        ///
        /// It is a guard rail and it is also the level telling the truth: the edge is
        /// lethal, and an edge that looks like ordinary ground until you are past it is a
        /// level killing the player for something it never showed them. The gaps are the
        /// other half of the same sentence -- the only places the barrier stops are the
        /// places you are meant to cross, and each of those is flanked by a pillar so the
        /// opening reads as a gate rather than as a hole somebody left.
        ///
        /// <para>
        /// What was here before was two rails of eighteen-centimetre section and a post
        /// every six metres, which at any distance is invisible and close up is a thing
        /// you would step over without noticing. A barrier has to be read as a barrier
        /// from across the map, because that is where the decision to walk towards it or
        /// not is made. So: a concrete curb at the bottom, footings, posts twice the
        /// section they were, three rails and a picket every three quarters of a metre --
        /// and every one of those is geometry rather than a separate object, welded into
        /// a chunk per twenty panels. Four hundred metres of fence as individual cubes
        /// was two and a half thousand draw calls; this is twenty.
        /// </para>
        /// </summary>
        private static void BuildRiverFence(Transform root, int layer, float half)
        {
            var group = new GameObject("RiverFence").transform;
            group.SetParent(root, false);

            float edge = _theme.hazardWidth * 0.5f;
            float standOff = edge + FenceStandOff;

            const float panel = 4.5f;
            const int panelsPerChunk = 20;

            float clear = _theme.bridgeWidth * 0.5f + 9f;

            for (int side = -1; side <= 1; side += 2)
            {
                var build = new MeshBuild { UVScale = 0.55f };
                var solid = new MeshBuild();
                var signs = new MeshBuild { UVScale = 0.9f };

                int inChunk = 0, chunk = 0;
                bool wasOpen = true;
                int panelIndex = 0;

                Vector3 At(float z) => new Vector3(GorgeCentreAt(z) + side * standOff,
                                                  GroundHeightAt(GorgeCentreAt(z) + side * standOff, z), z);

                for (float z = -half; z < half; z += panel, panelIndex++)
                {
                    bool open = NearCrossing(z + panel * 0.5f, clear);

                    var a = At(z);
                    var b = At(z + panel);

                    // A pillar wherever the run starts or stops. Both ends of every gap
                    // get one, which is what turns "the fence is missing here" into "this
                    // is the way through".
                    if (open != wasOpen) GatePost(build, solid, a);
                    wasOpen = open;

                    if (!open)
                    {
                        FencePanel(build, solid, signs, a, b, side,
                                   sign: panelIndex % 7 == 3);
                        inChunk++;
                    }

                    if (inChunk < panelsPerChunk) continue;

                    FlushFence(group, layer, build, solid, signs, side, chunk++);
                    build = new MeshBuild { UVScale = 0.55f };
                    solid = new MeshBuild();
                    signs = new MeshBuild { UVScale = 0.9f };
                    inChunk = 0;
                }

                if (!wasOpen) GatePost(build, solid, At(half));

                FlushFence(group, layer, build, solid, signs, side, chunk);
            }
        }

        private static void FlushFence(Transform parent, int layer, MeshBuild build, MeshBuild solid,
                                       MeshBuild signs, int side, int chunk)
        {
            if (build.Triangles.Count == 0) return;

            string name = $"Fence{(side < 0 ? "West" : "East")}_{chunk}";

            // Rendered in full and collided against as slabs. A mesh collider over
            // pickets is thousands of triangles of collision geometry for a surface
            // nothing ever needs to resolve more finely than "there is a fence here".
            var run = MeshObject(parent, name, build.ToMesh(name), _steelMat, Vector3.zero,
                                 Quaternion.identity, Vector3.one, layer, "Metal",
                                 collider: true, collisionMesh: solid.ToMesh(name + "_Collision"));

            // Ninety metres of fence in one mesh is, to the map, one ninety-metre
            // rectangle laid over the bank -- and since the rim meanders, a fat one. The
            // river's own slices already say where the barrier is.
            Hide(run);

            // The signs are a second mesh rather than more triangles in the first, and
            // that is the whole point of them. Welded into the fence they wear the
            // fence's dark steel, which is a hazard warning the colour of the thing it is
            // warning you about -- and the material that was meant to carry the orange
            // sat in the project referenced by nothing, which is how this was noticed at
            // all.
            if (signs.Triangles.Count == 0) return;

            var plates = MeshObject(parent, name + "_Signs", signs.ToMesh(name + "_Signs"),
                                    _signMat, Vector3.zero, Quaternion.identity, Vector3.one,
                                    layer, "Metal", collider: false);
            Hide(plates);
        }

        /// <summary>One bay of fence: curb, footings, post, three rails and the pickets between them.</summary>
        private static void FencePanel(MeshBuild build, MeshBuild solid, MeshBuild signs,
                                       Vector3 a, Vector3 b, int side, bool sign)
        {
            var along = b - a;
            float length = new Vector2(along.x, along.z).magnitude;
            if (length < 0.2f) return;

            float yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
            var turn = Quaternion.Euler(0f, yaw, 0f);
            var mid = (a + b) * 0.5f;

            // Concrete curb: the part that reads from two hundred metres, because it is
            // the only continuous solid in the whole assembly.
            build.Box(mid + Vector3.up * 0.2f, new Vector3(0.45f, 0.4f, length + 0.05f), turn);

            // Footing and post, at this bay's near end.
            build.Box(a + Vector3.up * 0.28f, new Vector3(0.95f, 0.56f, 0.95f), turn);
            build.Box(a + Vector3.up * 1.35f, new Vector3(0.3f, 2.7f, 0.3f), turn);
            build.Box(a + Vector3.up * 2.76f, new Vector3(0.42f, 0.12f, 0.42f), turn);

            // Rails.
            foreach (float h in new[] { 0.62f, 1.28f, 1.94f })
                build.Box(mid + Vector3.up * h, new Vector3(0.14f, 0.2f, length), turn);

            // Pickets. Close enough that the run reads as a surface rather than as three
            // lines, which is the difference between a fence and a handrail.
            int pickets = Mathf.Max(1, Mathf.RoundToInt(length / 0.75f));

            for (int i = 1; i < pickets; i++)
            {
                var at = Vector3.Lerp(a, b, i / (float)pickets);
                build.Box(at + Vector3.up * 1.28f, new Vector3(0.07f, 1.5f, 0.07f), turn);
            }

            if (sign)
            {
                // Leaned back towards whoever is walking at it, not squared to the fence.
                var plate = Quaternion.Euler(-16f, yaw + 90f, 0f);
                var on = mid + Vector3.up * 1.65f - turn * Vector3.right * side * 0.22f;

                signs.Box(on, new Vector3(0.05f, 0.52f, 0.78f), plate);
            }

            // Collision is one slab per bay, ignoring everything above the top rail.
            solid.Box(mid + Vector3.up * 1.15f, new Vector3(0.5f, 2.3f, length), turn);
        }

        /// <summary>A gate pillar: taller, squarer and capped, so a gap reads as deliberate.</summary>
        private static void GatePost(MeshBuild build, MeshBuild solid, Vector3 at)
        {
            build.Box(at + Vector3.up * 0.3f, new Vector3(1.5f, 0.6f, 1.5f), Quaternion.identity);
            build.Box(at + Vector3.up * 1.9f, new Vector3(1f, 3.2f, 1f), Quaternion.identity);
            build.Box(at + Vector3.up * 3.6f, new Vector3(1.3f, 0.26f, 1.3f), Quaternion.identity);

            solid.Box(at + Vector3.up * 1.9f, new Vector3(1.2f, 3.8f, 1.2f), Quaternion.identity);
        }

        private static bool NearCrossing(float z, float reach)
        {
            foreach (var crossing in _crossings)
                if (Mathf.Abs(crossing.z - z) < reach) return true;

            return false;
        }

        // ==================================================================
        // The bridge
        // ==================================================================
        /// <summary>
        /// A timber trestle over the canyon: deck, kerbs, a truss either side, and legs
        /// going down to the bed.
        ///
        /// Built along local x, with the parent already placed and the canyon under it.
        /// The truss is the part worth having -- a bridge with solid parapets is a
        /// corridor, and the whole point of this crossing is that it is the most exposed
        /// ground on the map. Diagonals you can see through, and shoot through, keep it
        /// exposed while still being something to crouch behind.
        /// </summary>
        private static void BuildTrestleBridge(Transform bridge, int layer, float length, float width,
                                               float depth, int index)
        {
            var timber = new MeshBuild { UVScale = 0.5f };
            var steel = new MeshBuild { UVScale = 0.6f };
            var solid = new MeshBuild();

            float halfLength = length * 0.5f;
            float halfWidth = width * 0.5f;

            // The deck, a hand above the sand rather than exactly level with it.
            //
            // Flush was the obvious build and it is two bugs. The ground at a bridge
            // landing is pinned to exactly zero and the deck's top surface was exactly
            // zero, so the sixteen metres where they overlap were coplanar -- which is
            // z-fighting, and z-fighting on the one piece of ground every player crosses.
            // And it left no clearance over the canyon's own lip, which runs under the
            // bridge and put a ridge across both approaches.
            timber.Box(new Vector3(0f, -0.25f, 0f), new Vector3(length, 0.5f, width), Quaternion.identity);
            solid.Box(new Vector3(0f, -0.25f, 0f), new Vector3(length, 0.5f, width), Quaternion.identity);

            // And an earth ramp up onto it at each end, so the hand of height is a slope
            // and not a kerb. Three degrees: a player never feels it and a vehicle can
            // take it at speed, which a step cannot be.
            const float apron = 2.8f;
            float pitch = Mathf.Atan2(DeckLift, apron) * Mathf.Rad2Deg;

            for (int e = -1; e <= 1; e += 2)
            {
                var slope = Quaternion.Euler(0f, 0f, -e * pitch);
                var at = new Vector3(e * (halfLength + apron * 0.5f), -DeckLift * 0.5f - 0.2f, 0f);
                var size = new Vector3(apron + 0.6f, 0.4f, width);

                timber.Box(at, size, slope);
                solid.Box(at, size, slope);
            }

            // Plank courses across it, so the deck is not one flat sheet under the feet.
            for (float x = -halfLength + 0.6f; x < halfLength; x += 1.2f)
                timber.Box(new Vector3(x, 0.03f, 0f), new Vector3(0.9f, 0.1f, width - 0.2f),
                           Quaternion.identity);

            for (int s = -1; s <= 1; s += 2)
            {
                float zSide = s * (halfWidth - 0.25f);

                // Kerb and top chord.
                timber.Box(new Vector3(0f, 0.25f, zSide), new Vector3(length, 0.5f, 0.5f), Quaternion.identity);
                steel.Box(new Vector3(0f, 1.55f, zSide), new Vector3(length, 0.24f, 0.3f), Quaternion.identity);

                // Verticals and the diagonals between them: a Warren truss, which is what
                // anybody who has seen a bridge expects to be looking through.
                const float bay = 3.6f;
                int bays = Mathf.Max(2, Mathf.RoundToInt(length / bay));
                float actual = length / bays;

                for (int i = 0; i <= bays; i++)
                {
                    float x = -halfLength + actual * i;
                    steel.Box(new Vector3(x, 0.85f, zSide), new Vector3(0.26f, 1.6f, 0.26f),
                              Quaternion.identity);
                }

                for (int i = 0; i < bays; i++)
                {
                    float x = -halfLength + actual * (i + 0.5f);
                    float lean = Mathf.Atan2(1.3f, actual) * Mathf.Rad2Deg;

                    steel.Box(new Vector3(x, 0.9f, zSide),
                              new Vector3(Mathf.Sqrt(actual * actual + 1.7f), 0.16f, 0.16f),
                              Quaternion.Euler(0f, 0f, i % 2 == 0 ? 90f - lean : lean - 90f));
                }

                solid.Box(new Vector3(0f, 0.85f, zSide), new Vector3(length, 1.7f, 0.5f),
                          Quaternion.identity);
            }

            // Trestle bents, down to the bed. They are what makes it read as a bridge from
            // below and from the rim rather than as a plank lying across a hole.
            //
            // Thickness is not cosmetic here. A twenty-metre leg at the section a
            // hand-rail wants is a wire, and a bridge held up by wires reads as a bridge
            // that is about to fail -- which the player believes, and then does not
            // stand on. Timber this long is a stack of posts with sway bracing between
            // them, so that is what it is built as.
            for (int p = -1; p <= 1; p += 2)
            {
                float x = p * _theme.hazardWidth * 0.26f;

                var tops = new Vector3[2];
                var feet = new Vector3[2];

                for (int s = -1; s <= 1; s += 2)
                {
                    int leg = (s + 1) / 2;

                    tops[leg] = new Vector3(x, -0.5f, s * (halfWidth - 0.8f));
                    feet[leg] = new Vector3(x + p * 3.2f, -depth + 0.6f, s * (halfWidth + 2.4f));

                    timber.Tube(tops[leg], feet[leg], 0.62f, 0.82f, 7);
                }

                // Horizontal ties and a sway brace per storey, which is what a timber
                // trestle is: short bents stacked, never one long column.
                int storeys = Mathf.Max(2, Mathf.RoundToInt(depth / 5f));

                for (int level = 1; level <= storeys; level++)
                {
                    float t = level / (float)storeys;

                    var a = Vector3.Lerp(tops[0], feet[0], t);
                    var b = Vector3.Lerp(tops[1], feet[1], t);

                    timber.Tube(a, b, 0.3f, 0.3f, 5);

                    if (level >= storeys) continue;

                    float tNext = (level + 1) / (float)storeys;

                    timber.Tube(Vector3.Lerp(tops[0], feet[0], t),
                                Vector3.Lerp(tops[1], feet[1], tNext), 0.18f, 0.18f, 4);
                    timber.Tube(Vector3.Lerp(tops[1], feet[1], t),
                                Vector3.Lerp(tops[0], feet[0], tNext), 0.18f, 0.18f, 4);
                }
            }

            var deck = MeshObject(bridge, "Deck", timber.ToMesh($"BridgeDeck_{index}"), _timberMat,
                                  Vector3.zero, Quaternion.identity, Vector3.one, layer, "Wood",
                                  collider: true, collisionMesh: solid.ToMesh($"BridgeSolid_{index}"));

            Mark(deck, _theme.bridgeColor, order: 4);

            var truss = MeshObject(bridge, "Truss", steel.ToMesh($"BridgeTruss_{index}"), _steelMat,
                                   Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal",
                                   collider: false);
            Hide(truss);

            // A gate at each end: two battered towers and a beam across them.
            //
            // A landmark visible from across the map, which is how a player finds a
            // crossing without a map telling them -- and the beam is what turns two
            // towers into a gate. Two plain slabs standing near a bridge are two slabs;
            // joined overhead they are a thing you go *through*, which is the same
            // sentence the pillars either side of the fence gap are saying.
            var gate = new MeshBuild { UVScale = 0.3f };
            var gateSolid = new MeshBuild();
            var beams = new MeshBuild { UVScale = 0.7f };

            for (int e = -1; e <= 1; e += 2)
            {
                float x = e * (halfLength - 1.8f);

                for (int s = -1; s <= 1; s += 2)
                {
                    float zSide = s * (halfWidth + 1.4f);

                    // Battered: wider at the foot than at the head, the way anything
                    // stacked out of mud brick has to be.
                    for (int c = 0; c < 4; c++)
                    {
                        float y = 0.1f + c * 2f;
                        float span = 3.1f - c * 0.32f;

                        gate.Box(new Vector3(x, y + 1f, zSide), new Vector3(span, 2f, span),
                                 Quaternion.identity);
                    }

                    gate.Box(new Vector3(x, 8.4f, zSide), new Vector3(2.5f, 0.45f, 2.5f),
                             Quaternion.identity);

                    gateSolid.Box(new Vector3(x, 4.2f, zSide), new Vector3(3f, 8.6f, 3f),
                                  Quaternion.identity);
                }

                beams.Tube(new Vector3(x, 5.4f, -(halfWidth + 1.4f)),
                           new Vector3(x, 5.4f, halfWidth + 1.4f), 0.24f, 0.24f, 6);

                beams.Box(new Vector3(x, 6.1f, 0f), new Vector3(0.5f, 0.55f, width * 0.55f),
                          Quaternion.identity);
            }

            var towers = MeshObject(bridge, "Gates", gate.ToMesh($"BridgeGates_{index}"), _adobeMat,
                                    Vector3.zero, Quaternion.identity, Vector3.one, layer,
                                    _theme.wallTag, collider: true,
                                    collisionMesh: gateSolid.ToMesh($"BridgeGatesSolid_{index}"));

            // Both ends of the bridge are in this one mesh, so its footprint is the whole
            // span -- drawn, it is a rectangle over the crossing competing with the deck
            // marker that is actually telling the player something.
            Hide(towers);

            var lintels = MeshObject(bridge, "GateBeams", beams.ToMesh($"BridgeBeams_{index}"),
                                     _timberMat, Vector3.zero, Quaternion.identity, Vector3.one,
                                     layer, "Wood", collider: false);
            NoStanding(lintels);
            Hide(lintels);
        }

        // ==================================================================
        // Compounds
        // ==================================================================
        /// <summary>
        /// An adobe compound: mud-brick walls with a parapet, roof beams poking through
        /// them, a canopy for shade and sandbags at the corner.
        ///
        /// Two ways in, on opposite sides, and two solid walls. Four solid walls is a
        /// room nobody can enter; one way in is a trap rather than a strongpoint. Two
        /// means it can be taken, held, and flanked, which is the only version worth
        /// putting on a map.
        /// </summary>
        private static void BuildOutpost(Transform parent, int layer, System.Random rng,
                                         Vector3 position, float w, float d, float yaw, int index)
        {
            var compound = new GameObject($"Outpost_{index}").transform;
            compound.SetParent(parent, false);
            compound.localPosition = position;
            compound.localRotation = Quaternion.Euler(0f, yaw, 0f);

            float h = Rand(rng, 3.2f, 4.4f);
            float thickness = Rand(rng, 0.6f, 0.9f);
            float door = Rand(rng, 4.5f, 6.5f);

            var walls = new MeshBuild { UVScale = 0.32f };
            var solid = new MeshBuild();

            AdobeWall(walls, solid, rng, new Vector3(0f, 0f, d * 0.5f), w, h, thickness, true, true, door);
            AdobeWall(walls, solid, rng, new Vector3(0f, 0f, -d * 0.5f), w, h, thickness, true, true, door);
            AdobeWall(walls, solid, rng, new Vector3(-w * 0.5f, 0f, 0f), d, h, thickness, false,
                      rng.NextDouble() < 0.4, door);
            AdobeWall(walls, solid, rng, new Vector3(w * 0.5f, 0f, 0f), d, h, thickness, false, false, door);

            // Corner buttresses. Structural on a real mud wall and, here, the thing that
            // stops four straight walls reading as a cardboard box.
            for (int cx = -1; cx <= 1; cx += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                {
                    var at = new Vector3(cx * w * 0.5f, 0f, cz * d * 0.5f);
                    walls.Box(at + Vector3.up * h * 0.42f,
                              new Vector3(thickness * 2.4f, h * 0.84f, thickness * 2.4f),
                              Quaternion.identity);
                    solid.Box(at + Vector3.up * h * 0.42f,
                              new Vector3(thickness * 2.4f, h * 0.84f, thickness * 2.4f),
                              Quaternion.identity);
                }

            MeshObject(compound, "Walls", walls.ToMesh($"OutpostWalls_{index}"), _adobeMat,
                       Vector3.zero, Quaternion.identity, Vector3.one, layer, _theme.wallTag,
                       collider: true, collisionMesh: solid.ToMesh($"OutpostSolid_{index}"));

            // Roof beams through the top of the wall -- vigas. One detail, and it is the
            // one that says "somebody built this out of mud and poles" rather than
            // "somebody extruded a rectangle".
            var beams = new MeshBuild { UVScale = 0.8f };

            for (float x = -w * 0.5f + 1.4f; x < w * 0.5f - 1f; x += Rand(rng, 1.6f, 2.4f))
                for (int s = -1; s <= 1; s += 2)
                    beams.Tube(new Vector3(x, h - 0.55f, s * (d * 0.5f - thickness)),
                               new Vector3(x, h - 0.5f, s * (d * 0.5f + Rand(rng, 0.5f, 0.9f))),
                               0.11f, 0.1f, 5);

            var poles = MeshObject(compound, "Beams", beams.ToMesh($"OutpostBeams_{index}"), _timberMat,
                                   Vector3.zero, Quaternion.identity, Vector3.one, layer, "Wood",
                                   collider: false);
            Hide(poles);

            // Something to fight over inside, and something to fight from behind.
            BuildCrateCluster(compound, layer, rng, new Vector3(Rand(rng, -4f, 4f), 0f, Rand(rng, -3f, 3f)));

            BuildSandbagWall(compound, layer, rng,
                             new Vector3(Rand(rng, -w * 0.3f, w * 0.3f), 0f, Rand(rng, -2f, 4f)),
                             Rand(rng, 0f, 180f));

            if (rng.NextDouble() < 0.6)
                BuildCanopy(compound, layer, rng,
                            new Vector3(Rand(rng, -w * 0.25f, w * 0.25f), 0f, Rand(rng, -d * 0.25f, d * 0.25f)));
        }

        /// <summary>
        /// One run of mud-brick wall into a mesh builder, with a doorway, a lintel and a
        /// parapet course whose top edge is deliberately uneven.
        /// </summary>
        private static void AdobeWall(MeshBuild build, MeshBuild solid, System.Random rng, Vector3 centre,
                                      float length, float height, float thickness, bool alongX,
                                      bool hasDoor, float doorWidth)
        {
            Vector3 Size(float span, float h) => alongX
                ? new Vector3(span, h, thickness)
                : new Vector3(thickness, h, span);

            void Slab(Vector3 at, float span, float h)
            {
                build.Box(at, Size(span, h), Quaternion.identity);
                solid.Box(at, Size(span, h), Quaternion.identity);
            }

            var up = Vector3.up * height * 0.5f;

            if (!hasDoor || length - doorWidth < 2f)
            {
                Slab(centre + up, length, height);
            }
            else
            {
                float segment = (length - doorWidth) * 0.5f;
                float offset = (doorWidth + segment) * 0.5f;
                var dir = alongX ? Vector3.right : Vector3.forward;

                Slab(centre + up + dir * offset, segment, height);
                Slab(centre + up - dir * offset, segment, height);

                // Lintel over the opening, leaving 2.6 m of headroom for the agents.
                float lintel = height - 2.6f;

                if (lintel >= 0.4f)
                    Slab(centre + Vector3.up * (height - lintel * 0.5f), doorWidth, lintel);
            }

            // Parapet: short courses along the top, each a different height. A mud wall
            // is finished by hand and never comes out level, and that ragged top line is
            // most of what separates adobe from a concrete tilt-up panel.
            int courses = Mathf.Max(2, Mathf.RoundToInt(length / 1.8f));

            for (int i = 0; i < courses; i++)
            {
                float span = length / courses;
                float along = (i - (courses - 1) * 0.5f) * span;
                var at = centre + (alongX ? Vector3.right : Vector3.forward) * along;
                float lift = Rand(rng, 0.14f, 0.42f);

                build.Box(at + Vector3.up * (height + lift * 0.5f),
                          Size(span * 0.97f, lift) + new Vector3(alongX ? 0f : 0.12f, 0f, alongX ? 0.12f : 0f),
                          Quaternion.identity);
            }
        }

        /// <summary>A shade canopy on four poles: the only soft thing on the whole map.</summary>
        private static void BuildCanopy(Transform parent, int layer, System.Random rng, Vector3 at)
        {
            float w = Rand(rng, 3.4f, 5f), d = Rand(rng, 3f, 4.4f), h = Rand(rng, 2.5f, 3.1f);

            var poles = new MeshBuild { UVScale = 0.9f };

            for (int cx = -1; cx <= 1; cx += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                    poles.Tube(at + new Vector3(cx * w * 0.5f, 0f, cz * d * 0.5f),
                               at + new Vector3(cx * w * 0.5f, h, cz * d * 0.5f), 0.09f, 0.075f, 5);

            MeshObject(parent, "CanopyFrame", poles.ToMesh("CanopyFrame"), _timberMat,
                       Vector3.zero, Quaternion.identity, Vector3.one, layer, "Wood");

            // Sagging in the middle, because a sheet pulled tight over four poles is a
            // table and a sheet that sags is a tarpaulin.
            var sheet = new MeshBuild { UVScale = 0.4f };
            const int n = 4;

            Vector3 Cloth(int i, int j)
            {
                float u = i / (float)n - 0.5f, v = j / (float)n - 0.5f;
                float sag = (0.25f - u * u - v * v) * 1.1f;

                return at + new Vector3(u * w, h - sag, v * d);
            }

            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    sheet.Quad(Cloth(i, j), Cloth(i, j + 1), Cloth(i + 1, j + 1), Cloth(i + 1, j));
                    sheet.Quad(Cloth(i + 1, j), Cloth(i + 1, j + 1), Cloth(i, j + 1), Cloth(i, j));
                }

            var cloth = MeshObject(parent, "Canopy", sheet.ToMesh("Canopy"), _canvasMat,
                                   Vector3.zero, Quaternion.identity, Vector3.one, layer, "Wood",
                                   collider: false);
            NoStanding(cloth);
        }

        // ==================================================================
        // Vantages
        // ==================================================================
        /// <summary>
        /// A timber scaffold: four legs, cross bracing, a plank deck, a lip to shoot over
        /// and a ramp up to it.
        ///
        /// The solid box this replaced was the single most obviously fake thing in the
        /// arena -- a five-metre cube of sand-coloured nothing with a ramp leaning on it.
        /// Legs and bracing cost about eighty triangles and turn it into something built,
        /// and being able to see through it matters as much: a player at the bottom of
        /// the ramp can see the deck is occupied before they commit to the climb.
        /// </summary>
        private static void BuildScaffold(Transform deck, int layer, System.Random rng,
                                          float w, float d, float h)
        {
            const float rampAngle = 22f;
            float sin = Mathf.Sin(rampAngle * Mathf.Deg2Rad);
            float tan = Mathf.Tan(rampAngle * Mathf.Deg2Rad);

            float run = h / tan;
            float length = h / sin;

            var build = new MeshBuild { UVScale = 0.55f };
            var solid = new MeshBuild();

            float legX = w * 0.5f - 0.35f, legZ = d * 0.5f - 0.35f;

            for (int cx = -1; cx <= 1; cx += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                {
                    var foot = new Vector3(cx * (legX + 0.45f), -0.3f, cz * (legZ + 0.45f));
                    var top = new Vector3(cx * legX, h, cz * legZ);

                    build.Tube(foot, top, 0.22f, 0.18f, 6);
                    solid.Box((foot + top) * 0.5f, new Vector3(0.5f, h, 0.5f), Quaternion.identity);
                }

            // Bracing, and it starts well above head height on purpose.
            //
            // Braced all the way to the ground, the four faces of the frame close the
            // space under the deck in: a hundred square metres of sand with no way in or
            // out, which bakes as navmesh because the bake only asks whether an agent
            // fits. An enemy spawned in there is stuck under the deck for the rest of the
            // level. So the diagonals live in the top half and what is at ankle height is
            // a sill, which an agent steps over and a player walks through.
            float braceBase = Mathf.Min(h * 0.46f, h - 1.3f);
            int storeys = Mathf.Max(1, Mathf.RoundToInt((h - braceBase) / 1.7f));

            for (int side = -1; side <= 1; side += 2)
            {
                build.Tube(new Vector3(-legX, 0.32f, side * legZ),
                           new Vector3(legX, 0.32f, side * legZ), 0.09f, 0.09f, 4);
                build.Tube(new Vector3(side * legX, 0.32f, -legZ),
                           new Vector3(side * legX, 0.32f, legZ), 0.09f, 0.09f, 4);
            }

            for (int s = 0; s < storeys; s++)
            {
                float y0 = Mathf.Lerp(braceBase, h, s / (float)storeys);
                float y1 = Mathf.Lerp(braceBase, h, (s + 1) / (float)storeys);

                for (int side = -1; side <= 1; side += 2)
                {
                    bool flip = (s + side) % 2 == 0;

                    build.Tube(new Vector3(flip ? -legX : legX, y0, side * legZ),
                               new Vector3(flip ? legX : -legX, y1, side * legZ), 0.1f, 0.1f, 4);

                    build.Tube(new Vector3(side * legX, y0, flip ? -legZ : legZ),
                               new Vector3(side * legX, y1, flip ? legZ : -legZ), 0.1f, 0.1f, 4);
                }
            }

            // Deck: joists, then planks across them.
            build.Box(new Vector3(0f, h - 0.22f, 0f), new Vector3(w, 0.24f, d), Quaternion.identity);
            solid.Box(new Vector3(0f, h - 0.22f, 0f), new Vector3(w, 0.24f, d), Quaternion.identity);

            for (float x = -w * 0.5f + 0.5f; x < w * 0.5f; x += 0.85f)
                build.Box(new Vector3(x, h - 0.05f, 0f), new Vector3(0.62f, 0.1f, d - 0.1f),
                          Quaternion.identity);

            // The lip, on the side the deck was put here to watch.
            build.Box(new Vector3(0f, h + 0.5f, d * 0.5f - 0.2f), new Vector3(w, 1f, 0.4f),
                      Quaternion.identity);
            solid.Box(new Vector3(0f, h + 0.5f, d * 0.5f - 0.2f), new Vector3(w, 1f, 0.4f),
                      Quaternion.identity);

            // Ramp, with slats so it does not read as a sheet of card.
            var slope = Quaternion.Euler(-rampAngle, 0f, 0f);
            var rampCentre = new Vector3(0f, h * 0.5f, -(d * 0.5f + run * 0.5f));

            build.Box(rampCentre, new Vector3(4.5f, 0.28f, length), slope);
            solid.Box(rampCentre, new Vector3(4.5f, 0.28f, length), slope);

            for (float t = -0.45f; t < 0.5f; t += 0.1f)
                build.Box(rampCentre + slope * new Vector3(0f, 0.19f, t * length),
                          new Vector3(4.3f, 0.1f, 0.3f), slope);

            MeshObject(deck, "Scaffold", build.ToMesh("Scaffold"), _timberMat, Vector3.zero,
                       Quaternion.identity, Vector3.one, layer, "Wood",
                       collider: true, collisionMesh: solid.ToMesh("ScaffoldSolid"));
        }

        // ==================================================================
        // Cover
        // ==================================================================
        /// <summary>A stacked wall of sandbags, laid in courses and stepped in at the ends.</summary>
        private static void BuildSandbagWall(Transform parent, int layer, System.Random rng,
                                             Vector3 at, float yaw)
        {
            var build = new MeshBuild { UVScale = 1.4f };
            var turn = Quaternion.Euler(0f, yaw, 0f);

            int courses = rng.Next(4, 6);
            float bagLength = 0.86f;
            int perCourse = rng.Next(8, 11);

            for (int c = 0; c < courses; c++)
            {
                // Each course is shorter than the one below and offset by half a bag,
                // which is how they are actually laid and also the only reason a stack of
                // ellipsoids reads as masonry rather than as spilled potatoes.
                int count = Mathf.Max(2, perCourse - c);
                float shift = (c % 2) * bagLength * 0.5f;

                for (int i = 0; i < count; i++)
                {
                    float along = (i - (count - 1) * 0.5f) * bagLength + shift;

                    var local = new Vector3(along, 0.23f + c * 0.36f, Rand(rng, -0.06f, 0.06f));
                    var bag = BoulderScaleFor(rng);

                    var placed = turn * local;
                    var spin = turn * Quaternion.Euler(Rand(rng, -7f, 7f), Rand(rng, -9f, 9f),
                                                       Rand(rng, -7f, 7f));

                    AppendMesh(build, SandbagMesh(rng.Next(1, 40)), at + placed, spin, bag);
                }
            }

            MeshObject(parent, "Sandbags", build.ToMesh("Sandbags"), _hessianMat, Vector3.zero,
                       Quaternion.identity, Vector3.one, layer, _theme.coverTag);
        }

        private static Vector3 BoulderScaleFor(System.Random rng)
            => new Vector3(Rand(rng, 0.46f, 0.55f), Rand(rng, 0.38f, 0.44f), Rand(rng, 0.34f, 0.42f));

        /// <summary>Planks on angled braces, with a sandbag or two at the foot.</summary>
        private static void BuildTimberBarricade(Transform parent, int layer, System.Random rng,
                                                 Vector3 at, float yaw)
        {
            var build = new MeshBuild { UVScale = 0.6f };
            var turn = Quaternion.Euler(0f, yaw, 0f);

            float w = Rand(rng, 3.6f, 5.4f);
            float h = Rand(rng, _theme.coverHeightRange.x, _theme.coverHeightRange.y);

            for (int s = -1; s <= 1; s += 2)
            {
                var post = at + turn * new Vector3(s * w * 0.5f, h * 0.5f, 0f);
                build.Box(post, new Vector3(0.22f, h + 0.3f, 0.22f), turn);

                // The brace, which is the thing that says this was propped up rather than
                // stood up: a barricade with no back leg is a wall.
                var braceTop = at + turn * new Vector3(s * w * 0.42f, h * 0.85f, 0f);
                var braceFoot = at + turn * new Vector3(s * w * 0.42f, 0f, h * 0.8f);

                build.Tube(braceTop, braceFoot, 0.09f, 0.11f, 5);
            }

            int planks = Mathf.Max(2, Mathf.RoundToInt(h / 0.32f));

            for (int i = 0; i < planks; i++)
            {
                float y = 0.16f + i * (h / planks);
                float skew = Rand(rng, -2.5f, 2.5f);

                build.Box(at + turn * new Vector3(0f, y, 0f),
                          new Vector3(w, h / planks * 0.86f, 0.13f),
                          turn * Quaternion.Euler(0f, 0f, skew));
            }

            MeshObject(parent, "Barricade", build.ToMesh("Barricade"), _timberMat, Vector3.zero,
                       Quaternion.identity, Vector3.one, layer, "Wood");
        }

        /// <summary>A low spine of rock, half-buried, running across the approach.</summary>
        private static void BuildRockSpine(Transform parent, int layer, System.Random rng,
                                           Vector3 at, float yaw)
        {
            var build = new MeshBuild { UVScale = 0.35f };
            var turn = Quaternion.Euler(0f, yaw, 0f);

            int rocks = rng.Next(3, 6);
            float span = Rand(rng, 4f, 6.5f);

            for (int i = 0; i < rocks; i++)
            {
                float along = (i - (rocks - 1) * 0.5f) * (span / rocks);
                float width = Rand(rng, 1.6f, 3.2f);
                float scale = width / BoulderSpread;

                var local = new Vector3(along, 0f, Rand(rng, -0.5f, 0.5f));
                var world = at + turn * local;

                // Each rock buried against the lowest sand under its own footprint, not
                // against the spine's origin: a run of five metres of dune can drop a
                // metre, and a rock at the low end of it would otherwise stand clear of
                // the ground with its hollow underside open.
                float floor = LowestGroundIn(world.x, world.z, width * 0.55f)
                            - scale * Rand(rng, 0.2f, 0.4f);

                AppendMesh(build, BoulderMesh(rng.Next(1, 999), 0.38f, 0.66f),
                           new Vector3(world.x, floor + scale * 0.3f, world.z),
                           Quaternion.Euler(Rand(rng, -10f, 10f), Rand(rng, 0f, 360f), Rand(rng, -10f, 10f)),
                           new Vector3(scale, scale * Rand(rng, 0.55f, 0.8f), scale * Rand(rng, 0.7f, 1.1f)));
            }

            MeshObject(parent, "RockSpine", build.ToMesh("RockSpine"), _rockMat, Vector3.zero,
                       Quaternion.identity, Vector3.one, layer, _theme.coverTag);
        }

        /// <summary>
        /// How much wider than its nominal size <see cref="BoulderMesh"/> comes out.
        ///
        /// It is a unit icosphere pushed around by noise, so its radius is about 1.3 and
        /// its width therefore about 2.6 times whatever scale it is given. That is worth
        /// a named constant because every caller here writes a size in metres meaning the
        /// rock's <i>width</i>, and without it a "four metre boulder" is eleven metres
        /// across -- which is not a piece of cover, it is a dome, and a field of them was
        /// most of what made this arena's rock read as lumpy blobs rather than as stone.
        /// </summary>
        private const float BoulderSpread = 2.6f;

        /// <summary>One boulder, buried in the ground it was given and kept off the navmesh.</summary>
        /// <param name="width">How far across the rock should be, in metres.</param>
        private static void Boulder(Transform parent, int layer, System.Random rng, Vector3 at,
                                    float width)
        {
            float scale = width / BoulderSpread;
            float tall = Rand(rng, 0.6f, 1.1f);

            // Sunk to the lowest sand anywhere under it, not to the sand at its middle.
            // A flat-bottomed shell pinned to its centre floats on the downhill side of
            // any slope, and the gap that leaves is a way inside a closed mesh -- see
            // LowestGroundIn.
            float floor = LowestGroundIn(at.x, at.z, width * 0.55f)
                        - scale * Rand(rng, 0.12f, 0.3f);

            var rock = MeshObject(parent, "Rock", BoulderMesh(rng.Next(1, 999),
                                                              Rand(rng, 0.26f, 0.44f),
                                                              Rand(rng, 0.6f, 0.85f)),
                                  Rand(rng, 0f, 1f) < 0.3f ? _rockDarkMat : _rockMat,
                                  new Vector3(at.x, floor + scale * tall * 0.42f, at.z),
                                  Quaternion.Euler(Rand(rng, -9f, 9f), Rand(rng, 0f, 360f),
                                                   Rand(rng, -9f, 9f)),
                                  new Vector3(scale, scale * tall, scale * Rand(rng, 0.75f, 1.25f)),
                                  layer, _theme.coverTag);

            // A boulder's top is a smooth dome, and a NavMeshSurface reads any of it
            // shallower than the agent slope as walkable ground. Left alone, every rock
            // on the map grows a little island of navmesh on top that nothing can reach
            // -- and the spawner samples near the player, so it will happily put an enemy
            // up there to stand still for the rest of the level.
            if (width > 4.5f) NoStanding(rock);
        }

        /// <summary>Bakes an already-built mesh into a builder at a transform, so runs of shapes weld into one object.</summary>
        private static void AppendMesh(MeshBuild build, Mesh mesh, Vector3 position,
                                       Quaternion rotation, Vector3 scale)
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;

            Vector3 To(int i) => position + rotation * Vector3.Scale(vertices[i], scale);

            for (int t = 0; t < triangles.Length; t += 3)
                build.Tri(To(triangles[t]), To(triangles[t + 1]), To(triangles[t + 2]));
        }

        // ==================================================================
        // Vegetation
        // ==================================================================
        /// <summary>
        /// Palms along the water, dead trees out on the sand, and scrub everywhere.
        ///
        /// This pass is worth more per triangle than any other in the file, for one
        /// reason: a dune field has nothing vertical in it, so there is nothing for the
        /// eye to judge distance against and two hundred metres of sand reads as fifty.
        /// A tree is a known height. Put a few on a ridge line and the ridge line
        /// acquires a distance.
        ///
        /// The palms are on the river because that is where palms are, and because it
        /// makes the river readable from a long way off -- a green line across a
        /// sand-coloured map is a landmark before the player is close enough to see the
        /// water in it at all.
        /// </summary>
        private static void BuildVegetation(Transform root, int layer, System.Random rng, float half)
        {
            var group = new GameObject("Vegetation").transform;
            group.SetParent(root, false);

            float edge = _theme.hazardWidth * 0.5f;

            // ---- palms, in stands along both rims ----
            int stands = Mathf.RoundToInt(_theme.scatterClusterCount * 0.35f);

            for (int i = 0; i < stands; i++)
            {
                float z = Rand(rng, -half * 0.96f, half * 0.96f);
                int side = rng.NextDouble() < 0.5 ? -1 : 1;

                float offset = edge + FenceStandOff + Rand(rng, 6f, 34f);
                float x = GorgeCentreAt(z) + side * offset;

                if (Mathf.Abs(x) > half * 0.95f) continue;
                if (!Free(new Vector2(x, z), 5f)) continue;
                Claim(x, z, 5f);

                int trees = rng.Next(2, 5);

                for (int k = 0; k < trees; k++)
                {
                    float tx = x + Rand(rng, -5f, 5f);
                    float tz = z + Rand(rng, -5f, 5f);

                    BuildPalm(group, layer, rng, new Vector3(tx, GroundHeightAt(tx, tz), tz));
                }
            }

            // ---- dead trees, out on the open sand ----
            int dead = Mathf.RoundToInt(_theme.landmarkCount * 1.6f);

            for (int i = 0; i < dead; i++)
            {
                if (!TryClaim(rng, half * 0.94f, 3f, out Vector2 p)) continue;

                float height = Rand(rng, 3.4f, 6.2f);

                var tree = MeshObject(group, "DeadTree", DeadTreeMesh(rng.Next(1, 40)), _deadwoodMat,
                                      new Vector3(p.x, GroundHeightAt(p.x, p.y) - 0.15f, p.y),
                                      Quaternion.Euler(Rand(rng, -4f, 4f), Rand(rng, 0f, 360f),
                                                       Rand(rng, -4f, 4f)),
                                      new Vector3(height, height, height), layer, "Wood");

                NoStanding(tree);
            }

            // ---- scrub ----
            //
            // Welded into a handful of meshes rather than placed as objects: there are
            // several hundred, none of them has a collider, and several hundred draw
            // calls of foliage is a cost this map cannot pay for something the player
            // will never interact with.
            const int perPatch = 40;
            int patches = 9;

            for (int b = 0; b < patches; b++)
            {
                var build = new MeshBuild { UVScale = 1.2f };
                int placed = 0;

                for (int i = 0; i < perPatch * 3 && placed < perPatch; i++)
                {
                    float x = Rand(rng, -half * 0.97f, half * 0.97f);
                    float z = Rand(rng, -half * 0.97f, half * 0.97f);

                    // Never over the water, and never on the bridge approach where a bush
                    // growing out of a deck would be very hard to explain.
                    if (Mathf.Abs(x - GorgeCentreAt(z)) < edge + RimLipReach + 2f) continue;

                    float size = Rand(rng, 0.7f, 1.7f);

                    AppendMesh(build, ScrubMesh(rng.Next(1, 24)),
                               new Vector3(x, GroundHeightAt(x, z) - 0.1f, z),
                               Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f),
                               new Vector3(size, size * Rand(rng, 0.7f, 1.3f), size));
                    placed++;
                }

                if (placed == 0) continue;

                var patch = MeshObject(group, $"Scrub_{b}", build.ToMesh($"Scrub_{b}"), _foliageMat,
                                       Vector3.zero, Quaternion.identity, Vector3.one, layer,
                                       "Untagged", collider: false);
                NoStanding(patch);
                Hide(patch);
            }
        }

        private static void BuildPalm(Transform parent, int layer, System.Random rng, Vector3 at)
        {
            float height = Rand(rng, 6.5f, 11f);
            int seed = rng.Next(1, 30);

            var palm = new GameObject("Palm").transform;
            palm.SetParent(parent, false);
            palm.localPosition = at;
            palm.localRotation = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);

            MeshObject(palm, "Trunk", PalmTrunkMesh(seed), _timberMat, Vector3.zero,
                       Quaternion.identity, new Vector3(height, height, height), layer, "Wood");

            // The crown goes on the trunk's own tip rather than straight overhead. The
            // trunk leans -- that is most of what makes it read as a palm -- so a crown
            // hung above the root is a crown floating a metre off the top of the tree.
            var tip = PalmTipFor(seed) * height;

            var crown = MeshObject(palm, "Crown", PalmCrownMesh(seed), _foliageMat,
                                   tip, Quaternion.identity,
                                   Vector3.one * (height * Rand(rng, 0.32f, 0.42f)),
                                   layer, "Wood", collider: false);

            // Nine metres of navmesh island in the shape of a palm frond, otherwise.
            NoStanding(crown);
            Hide(crown);
        }

    }
}
#endif
