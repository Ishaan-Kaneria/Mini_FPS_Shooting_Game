#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The open-zone arena: ground that runs to a horizon, a river cut across it with
    /// deadly water in the bottom, bridges over the river, and content placed because of
    /// all three rather than sprinkled on top of them.
    ///
    /// This is the other half of <see cref="FPSKitSceneBuilder"/>, used when a
    /// <see cref="LevelTheme"/> has <c>openZone</c> set. The walled version it replaces
    /// is still there and still correct for a tight arena; what it could not do is feel
    /// like anywhere. It was a square floor, four walls at the edge of it, and every
    /// structure inside placed by rejection sampling -- which is to say placed nowhere
    /// in particular, because nothing in it knew about anything else in it.
    ///
    /// The rule here is that every piece is placed *because of* a piece already down:
    ///
    ///   the river         decides where the banks are
    ///   the banks         decide where the bridges can land
    ///   the bridges       decide where the compounds go, because a crossing is worth
    ///                     holding and a crossing nobody holds is not a crossing
    ///   the compounds     decide where the vantages go, because height is only worth
    ///                     taking if it overlooks something
    ///   all of the above  decide where the cover goes, because cover is a route between
    ///                     two places and not a decoration between them
    ///
    /// That ordering is the whole design. It is also why this file is a sequence of
    /// passes that hand their results to each other rather than a list of independent
    /// spawners.
    ///
    /// <para>
    /// <b>Planning comes before ground.</b> The arena is built in two halves: every pass
    /// that needs level ground picks its site first and says so, then
    /// <see cref="BuildDuneField"/> generates the terrain with those sites already flat
    /// in it, and only then is anything actually built. That order is forced by the
    /// terrain -- a compound cannot be dropped on a dune after the fact without either
    /// re-meshing the collider under it or leaving one of its walls three metres in the
    /// air -- and it is why <c>PlanOutposts</c> and <c>BuildOutposts</c> are two
    /// functions rather than one. They have to stay in the same order as each other,
    /// because the second reads the list the first wrote.
    /// </para>
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        /// <summary>The river's centre line, sampled along z. Everything is placed against it.</summary>
        private static float GorgeCentreAt(float z)
            => _theme.hazardOffset
             + Mathf.Sin(z / 90f) * _theme.hazardMeander
             + Mathf.Sin(z / 37f + 1.7f) * _theme.hazardMeander * 0.35f;

        /// <summary>Where the bridges landed, so later passes can place things worth defending.</summary>
        private static readonly List<Vector3> _crossings = new List<Vector3>();

        /// <summary>Compounds and landmarks, so cover can be strung between them.</summary>
        private static readonly List<Vector3> _anchors = new List<Vector3>();

        // ==================================================================
        // The plans
        // ==================================================================
        private struct LandmarkPlan
        {
            public Vector2 Point;
            public float Width, Height, Yaw;
            public int Seed;
        }

        private struct OutpostPlan
        {
            public Vector2 Point;
            public float Width, Depth, Yaw;
        }

        private struct VantagePlan
        {
            public Vector2 Point;
            public float Width, Depth, Height, Yaw;
        }

        private static readonly List<LandmarkPlan> _landmarkPlans = new List<LandmarkPlan>();
        private static readonly List<OutpostPlan> _outpostPlans = new List<OutpostPlan>();
        private static readonly List<VantagePlan> _vantagePlans = new List<VantagePlan>();

        // ==================================================================
        private static void BuildOpenZone()
        {
            _claimed.Clear();
            _crossings.Clear();
            _anchors.Clear();
            _landmarkPlans.Clear();
            _outpostPlans.Clear();
            _vantagePlans.Clear();

            ClearMeshPool();
            ResetTerrain();

            // Before anything asks how high the ground is: the flows are part of the answer.
            PlanLavaField(_theme.arenaSize * 0.5f);

            var root = new GameObject("Arena").transform;
            int layer = LayerMask.NameToLayer("Environment");
            int backdrop = LayerMask.NameToLayer("Backdrop");
            if (backdrop < 0) backdrop = layer;

            var rng = new System.Random(_theme.randomSeed);
            float half = _theme.arenaSize * 0.5f;

            ResolveSurfaceMaterials();
            BuildSurfaceTextures();
            ResolveOutdoorMaterials();

            // After the desert's palette rather than instead of it: the volcanic one only
            // replaces the handful of materials the shared passes reach for.
            if (_theme.volcanicZone) ResolveVolcanicMaterials();

            // The player's start, and then the river, before anything can be placed on
            // either. TryClaim only knows what has already been claimed.
            // Wide enough that the first thing the player sees is the level rather than
            // the side of a rock somebody dropped on the spawn.
            Claim(0f, 0f, 30f);
            ClaimGorge(half);

            // Where the player starts is a place on the map like any other, so the passes
            // that string cover between places have a reason to run something past it.
            // Without this the spawn sat in the one part of the level nothing pointed at,
            // which is sixty metres of empty ground as the first thing anybody sees.
            _anchors.Add(Vector3.zero);

            // Level ground to stand up in, held at whatever height the dunes are at
            // here rather than at zero. Pinned to zero it was a twelve-metre crater in
            // the middle of the map -- the steepest ground in the arena was its rim, and
            // the first thing the player ever saw was the inside of a bowl. BuildPlayer
            // asks the terrain where to put them instead.
            FlattenPad(0f, 0f, 22f, 34f);

            // ---- planning ----
            PlanCrossings(half);

            // The town, the oasis, the oil field and the track, before the landmarks and
            // outposts take the ground (FPSKitDesertLife). Desert only.
            PlanDesertLife(half);

            PlanLandmarks(rng, half);
            PlanOutposts(rng, half);
            PlanVantages(rng, half);

            // ---- ground ----
            if (_theme.duneHeight > 0f) BuildDuneField(root, layer, half);
            else BuildGround(root, layer, half);

            BuildApron(root, backdrop, half);
            if (_theme.duneHeight <= 0f) BuildGroundPatches(root, backdrop, rng, half);

            BuildGorge(root, layer, half);
            BuildCrossings(root, layer, half);
            BuildRiverFence(root, layer, half);
            BuildBoundary(root, layer, half);

            if (_theme.volcanicZone) BuildVolcanicBackdrop(root, backdrop, rng);
            else BuildBackdrop(root, backdrop, rng, half);

            // Nothing outside the seal is anybody's to stand on, however much sand
            // there is out there.
            SealNavMeshOutside(root, half - BermToe + 1f, half + _theme.apronSize);

            // ---- content ----
            if (_theme.volcanicZone) BuildVolcanoLandmarks(root, layer, rng);
            else BuildLandmarks(root, layer, rng);

            BuildOutposts(root, layer, rng);
            BuildVantages(root, layer, rng);
            BuildDesertLife(root, layer, backdrop, half);
            BuildCoverLines(root, layer, rng, half);
            BuildScatter(root, layer, rng, half);

            // Nothing grows here. What stands on the plain instead is what the heat made:
            // vents still breathing, and glass where the lava froze too fast to crystallise.
            if (_theme.volcanicZone)
            {
                BuildVents(root, layer, rng, half);
                BuildSpires(root, layer, rng, half);
                BuildLavaFieldSurface(root, backdrop, half);
                BuildLavaGlow(root, half);
                BuildAshfall(root);
            }
            else
            {
                BuildVegetation(root, layer, rng, half);
            }

            BuildProps(root, layer, rng, half);
            BuildAccentLights(root, rng);

            // Last, so "empty" means empty after everything else has had its turn.
            BuildDesertFill(root, layer, half);
        }

        // ==================================================================
        // Ground
        // ==================================================================
        /// <summary>
        /// The ground past the boundary. No colliders and off the navigation bake -- it
        /// exists so that the edge of the level is not the edge of the world, which is
        /// the single biggest reason the walled arenas read as a box.
        ///
        /// Split around the river exactly as the banks are, and the river runs out
        /// through it. One slab across the whole thing was the first version, and it
        /// filled the canyon in from below: from the rim the gorge was a strip of ground
        /// a shade paler than the ground beside it, with a bridge over nothing and a
        /// railing guarding nothing. The river has to leave the level for the level to
        /// look like it is somewhere the river came through.
        ///
        /// <para>
        /// It stops at the boundary rather than running under the arena, which it used
        /// to. Under a flat floor that was invisible and saved a seam; under a dune field
        /// it is a sheet of flat sand at y=0 cutting through every hollow that dips below
        /// zero, which from inside the level looks like water without the water.
        /// </para>
        /// </summary>
        private static void BuildApron(Transform root, int layer, float half)
        {
            if (_theme.apronSize <= 0f) return;

            var group = new GameObject("Apron").transform;
            group.SetParent(root, false);

            float reach = half + _theme.apronSize;

            // Starts exactly where the terrain stops. Overlapped, the two fight: the
            // heightfield fades towards zero over the overhang rather than arriving at
            // it, so a flat sheet at y=0 cuts up through every hollow in the last forty
            // metres of ground and leaves a ring of hard edges all the way round the
            // arena.
            float inner = half + TerrainOverhang;

            int steps = 72;
            float span = reach * 2f / steps;

            float edge = _theme.hazardWidth * 0.5f;
            float depth = _theme.hazardDepth;
            bool dry = _theme.hazard == LevelTheme.Hazard.Chasm;

            // World-space UVs, so the apron's sand is at the same scale as the arena's
            // and the join between them is invisible. A scaled cube cannot do this --
            // its UVs run nought to one across whichever face, so a kilometre-wide slab
            // stretches one tile of texture across the whole kilometre, which is what
            // made the horizon read as smeared streaks.
            var ground = new MeshBuild { UVScale = 1f };
            var bed = new MeshBuild { UVScale = 0.25f };
            var river = new MeshBuild { UVScale = 0.06f };

            void Strip(MeshBuild build, float from, float to, float z0, float z1, float y)
            {
                if (to - from <= 0.2f) return;

                build.Quad(new Vector3(from, y, z0), new Vector3(from, y, z1),
                           new Vector3(to, y, z1), new Vector3(to, y, z0));
            }

            for (int i = 0; i < steps; i++)
            {
                float z0 = -reach + span * i;
                float z1 = z0 + span + 0.4f;
                float z = (z0 + z1) * 0.5f;

                float centre = GorgeCentreAt(z);

                // Inside the arena's own latitudes the ground belongs to the terrain, so
                // the apron is two strips outside it rather than one slab underneath.
                bool beside = Mathf.Abs(z) <= inner;

                float westTo = beside ? -inner : centre - edge;
                float eastFrom = beside ? inner : centre + edge;

                Strip(ground, -reach, westTo, z0, z1, 0f);
                Strip(ground, eastFrom, reach, z0, z1, 0f);

                // The river only has to be drawn where the canyon mesh stops.
                if (Mathf.Abs(z) < half + CanyonOverrun - span) continue;

                Strip(bed, centre - edge - 4f, centre + edge + 4f, z0, z1, -depth - 1f);

                if (!dry)
                    Strip(river, centre - edge + 2f, centre + edge - 2f, z0, z1, WaterSurfaceY);
            }

            var sand = _sandMat != null
                ? _sandMat
                : MakeMaterial("Apron", Shade(_theme.floorColor, 0.94f),
                               _theme.floorSmoothness * 0.5f, 0f);

            Backdrop(group, layer, "Apron", ground, sand);
            Backdrop(group, layer, "ApronBed", bed,
                     _rockDarkMat ?? MakeMaterial("ApronBed", Shade(_theme.bankColor, 0.55f), 0.1f, 0f));

            if (!dry)
                Backdrop(group, layer, "ApronWater", river,
                         _waterMat ?? MakeMaterial("ApronWater", _theme.hazardColor, 0.92f, 0.1f));
        }

        /// <summary>
        /// Puts a mesh past the boundary: no collider, on the layer the navigation bake
        /// ignores, and off the minimap.
        /// </summary>
        private static void Backdrop(Transform parent, int layer, string name, MeshBuild build,
                                     Material material)
        {
            if (build.Triangles.Count == 0) return;

            var go = MeshObject(parent, name, build.ToMesh(name), material, Vector3.zero,
                                Quaternion.identity, Vector3.one, layer, "Untagged", collider: false);

            Hide(go);
        }

        /// <summary>
        /// A block that is only ever looked at: no collider, and on a layer the
        /// navigation bake ignores. Everything past the boundary is one of these.
        /// </summary>
        private static GameObject Decor(Transform parent, int layer, string name, Vector3 position,
                                        Vector3 scale, Material material)
        {
            if (scale.x <= 0.2f || scale.z <= 0.2f) return null;

            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = position;
            block.transform.localScale = scale;
            block.layer = layer;

            Object.DestroyImmediate(block.GetComponent<Collider>());
            block.GetComponent<Renderer>().sharedMaterial = material;

            return block;
        }

        /// <summary>
        /// Broad, barely-there patches of a slightly different tone laid on the ground.
        ///
        /// Only used by the flat version of this arena. Flat ground lit by one
        /// directional light is one colour everywhere, and one colour everywhere reads as
        /// a texture-less plane whatever is standing on it. A dune field does not have
        /// that problem -- it is shaded by its own shape -- and these are flat quads, so
        /// laid over dunes they cut through every crest they cross.
        /// </summary>
        private static void BuildGroundPatches(Transform root, int layer, System.Random rng, float half)
        {
            var group = new GameObject("GroundPatches").transform;
            group.SetParent(root, false);

            // Many, large and overlapping. Few and small, they read as what they are --
            // rectangles lying on the sand -- because the eye finds a hard edge on an
            // otherwise featureless plane immediately. Overlapping, each one's edge is
            // broken by the next and what is left is variation.
            const int count = 120;
            float edge = _theme.hazardWidth * 0.5f;

            for (int i = 0; i < count; i++)
            {
                float x = Rand(rng, -half * 1.6f, half * 1.6f);
                float z = Rand(rng, -half * 1.6f, half * 1.6f);
                float w = Rand(rng, 45f, 150f);

                // Clear of the water by the patch's own size, not just by its centre.
                //
                // Tested on the centre alone, a hundred-and-fifty-metre patch whose
                // middle is forty metres from the river still reaches seventy-five metres
                // across it -- and what that looks like is the gorge filled back in, a
                // sheet of sand lying over the canyon with the bridge standing on top of
                // it. Sampled at three points down the patch as well, because the river
                // wanders and one sample is only true where it was taken.
                float clearance = edge + 10f + w * 0.75f;

                if (Mathf.Abs(x - GorgeCentreAt(z)) < clearance ||
                    Mathf.Abs(x - GorgeCentreAt(z - w * 0.6f)) < clearance ||
                    Mathf.Abs(x - GorgeCentreAt(z + w * 0.6f)) < clearance) continue;

                var tone = Color.Lerp(_theme.floorColor,
                                      i % 3 == 0 ? _theme.bankColor : Shade(_theme.floorColor, 1.1f),
                                      Rand(rng, 0.1f, 0.3f));

                var patch = Decor(group, layer, "Patch", new Vector3(x, 0.03f, z),
                                  new Vector3(w, 0.06f, w * Rand(rng, 0.5f, 1.4f)),
                                  MakeMaterial($"Patch_{ColorKey(tone)}", tone,
                                               _theme.floorSmoothness * 0.5f, 0f));

                if (patch != null)
                    patch.transform.localRotation = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);
            }
        }

        /// <summary>
        /// The two banks, for a theme with no dunes: a pair of slabs with the river
        /// between them rather than one floor with a hole cut in it, because the hole is
        /// what the navigation bake reads. No floor means no NavMesh, which means nothing
        /// can path across the water without being told, and the bridges become the only
        /// crossings for free.
        ///
        /// <see cref="BuildDuneField"/> is the same idea done as a heightfield, and cuts
        /// the same hole for the same reason.
        /// </summary>
        private static void BuildGround(Transform root, int layer, float half)
        {
            float depth = _theme.hazardDepth;
            int steps = 14;
            float span = _theme.arenaSize / steps;

            var group = new GameObject("Ground").transform;
            group.SetParent(root, false);

            // Sliced along z so the banks can follow a river that wanders. One slab per
            // slice, each ending where that slice's rim is.
            for (int i = 0; i < steps; i++)
            {
                float z = -half + span * (i + 0.5f);
                float centre = GorgeCentreAt(z);
                float edge = _theme.hazardWidth * 0.5f + GorgeLipOverlap;

                // A hair of overlap at each seam, or the slices show as cracks of
                // skybox when the camera is low.
                float slice = span + 0.4f;

                Bank(group, layer, "BankWest", z, slice, -half, centre - edge, depth);
                Bank(group, layer, "BankEast", z, slice, centre + edge, half, depth);
            }
        }

        private static void Bank(Transform parent, int layer, string name, float z, float slice,
                                 float from, float to, float depth)
        {
            float width = to - from;
            if (width <= 0.5f) return;

            CreateBlock(parent, new Vector3((from + to) * 0.5f, -depth * 0.5f, z),
                        new Vector3(width, depth, slice), 0f, layer, _theme.floorTag,
                        _theme.floorColor, _theme.floorSmoothness, 0f, name, _floorMat);
        }

        // ==================================================================
        private static void ClaimGorge(float half)
        {
            float edge = _theme.hazardWidth * 0.5f;

            // Claimed out past the fence line -- the extra is the rim, and a compound
            // built with one wall overhanging the water is a compound with a wall nobody
            // can stand at.
            float radius = edge + RimBandWidth + 10f;

            for (float z = -half - 20f; z <= half + 20f; z += edge * 0.5f)
                Claim(GorgeCentreAt(z), z, radius);
        }

        // ==================================================================
        // Crossings
        // ==================================================================
        /// <summary>
        /// Where the bridges go, and the flat ground their landings need.
        ///
        /// Split out of <see cref="BuildCrossings"/> because it has to run before the
        /// terrain: the deck is a straight, level thing forty-six metres longer than the
        /// river is wide, so both its ends stand well outside the flat rim band and land
        /// wherever the dunes happen to be. Unflattened, that is a bridge whose far end
        /// is buried in a dune -- which does not read as a bug, it reads as a bridge to
        /// nowhere, and the player simply stops using that crossing.
        /// </summary>
        private static void PlanCrossings(float half)
        {
            int count = Mathf.Clamp(_theme.bridgeCount, 1, 3);

            for (int i = 0; i < count; i++)
            {
                // Spread across the middle two thirds. Against the boundary a bridge is
                // a corner nobody goes to.
                float t = count == 1 ? 0.5f : i / (float)(count - 1);
                float z = Mathf.Lerp(-half * 0.62f, half * 0.62f, t);

                float centre = GorgeCentreAt(z);
                float length = _theme.hazardWidth + BridgeOverhang * 2f;

                _crossings.Add(new Vector3(centre, 0f, z));

                // Both landings, so the passes below know the two bits of ground worth
                // fighting over and can put something there.
                _anchors.Add(new Vector3(centre - _theme.hazardWidth * 0.5f - 18f, 0f, z));
                _anchors.Add(new Vector3(centre + _theme.hazardWidth * 0.5f + 18f, 0f, z));

                // Held at zero, because the deck is: the rim of the canyon is the level's
                // datum and a bridge is flat. The blend is deliberately more than twice
                // the pad, since out here the dunes are at full height and the step down
                // to the deck can be ten metres -- spread over twenty it is a wall, and
                // over fifty it is an approach.
                for (int e = -1; e <= 1; e += 2)
                    FlattenPad(centre + e * length * 0.5f, z, 17f, 40f, 0f);
            }
        }

        /// <summary>Metres of deck past the rim at each end, so a bridge has an approach.</summary>
        private const float BridgeOverhang = 23f;

        /// <summary>
        /// The bridges, and the reason the level has a shape.
        ///
        /// Spread down the river rather than clustered, and each one deliberately long:
        /// the deck is the most exposed ground in the level and the whole tension of an
        /// open map is the walk across it. Railings both sides, which are cover as well
        /// as a fence -- crouching behind a rail halfway over is the fight this level is
        /// built to produce.
        /// </summary>
        private static void BuildCrossings(Transform root, int layer, float half)
        {
            var group = new GameObject("Crossings").transform;
            group.SetParent(root, false);

            float depth = _theme.hazardDepth;

            for (int i = 0; i < _crossings.Count; i++)
            {
                var crossing = _crossings[i];

                float length = _theme.hazardWidth + BridgeOverhang * 2f;
                float w = _theme.bridgeWidth;

                var bridge = new GameObject($"Bridge_{i}").transform;
                bridge.SetParent(group, false);
                bridge.localPosition = new Vector3(crossing.x, DeckLift, crossing.z);

                BuildTrestleBridge(bridge, layer, length, w, depth, i);
            }
        }

        // ==================================================================
        // The edge of the world
        // ==================================================================
        /// <summary>How far inside the arena the sand hill's toe begins.</summary>
        private const float BermToe = 6f;

        /// <summary>How far outside the arena its crest stands.</summary>
        private const float BermCrest = 20f;

        /// <summary>And how far out it has come back down to the ground again.</summary>
        private const float BermBack = 56f;

        /// <summary>Metres of crest, before the noise along the run varies it.</summary>
        private const float BermHeight = 23f;

        /// <summary>
        /// The boundary, as one continuous hill of sand rather than as a line of rocks.
        ///
        /// <b>This is a rewrite, and what it replaces was the worst-looking thing in the
        /// arena.</b> The edge used to be a row of buttes -- the same stepped mesa shape
        /// the horizon is made of -- each given its own random width, height and rotation
        /// and then squashed onto a footprint half as wide as it was tall. A butte has a
        /// ledge that steps *out* every third band, which at mesa proportions is a
        /// weathered bench and at these proportions is a flange sticking out sideways;
        /// six of them round a shape stretched two to one came out as black spikes.
        /// Forty of those in a row, interpenetrating, is not a ridge. The whole edge of
        /// the map read as shattered glass, and underneath every one of those flanges was
        /// a pocket at head height that a player could walk into and then not walk back
        /// out of -- a hill you cannot see, that you get stuck inside.
        ///
        /// <para>
        /// So the edge is now what a desert's edge should have been from the start: a
        /// dune ridge. It is one heightfield in its own right, wrapped round the arena as
        /// a square annulus -- a profile that rises out of the sand a few metres inside
        /// the boundary, crests twenty metres outside it and lies back down again -- so
        /// there is no overhang anywhere on it, no pocket to be caught in, and no join
        /// between one piece and the next to fall down. The corners come out right for
        /// free because the four sides are the same function of
        /// <c>max(|x|, |z|)</c> and meet exactly along the diagonal.
        /// </para>
        ///
        /// <para>
        /// <b>The seal moved, and that is half the fix.</b> It used to sit a metre inside
        /// the arena boundary while the rocks were placed on or outside it -- so the
        /// player was stopped by an invisible wall standing in open sand, with the thing
        /// that was supposed to be stopping them either twenty metres behind them or
        /// seven metres out of reach. Now it is at the toe of the hill: you walk up to
        /// the sand, the sand is what stops you, and the hill you can see carries on
        /// rising in front of you.
        /// </para>
        /// </summary>
        private static void BuildBoundary(Transform root, int layer, float half)
        {
            var group = new GameObject("Boundary").transform;
            group.SetParent(root, false);

            var rng = new System.Random(_theme.randomSeed * 31 + 7);

            float toe = half - BermToe;
            float back = half + BermBack;

            var sand = MakeDetailMaterial("Sand", _theme.floorColor, "Sand", 0.09f,
                                          _theme.floorSmoothness * 0.4f, 0f, 1.15f);

            for (int side = 0; side < 4; side++)
                BuildBoundaryRun(group, layer, sand, side, toe, back);

            BuildBoundaryOutcrops(group, layer, rng, half);

            // The seal, at the foot of the slope rather than out in the open sand.
            // Still invisible, because a collider is, but it is now flush with the face
            // of something twenty metres tall: what the player runs into and what they
            // can see are in the same place.
            for (int side = 0; side < 4; side++)
            {
                bool alongZ = side >= 2;
                float sign = side % 2 == 0 ? 1f : -1f;
                float at = toe + 2f;

                var wall = new GameObject($"Seal_{side}");
                wall.transform.SetParent(group, false);
                wall.layer = layer;
                wall.transform.localPosition = alongZ
                    ? new Vector3(sign * at, 30f, 0f)
                    : new Vector3(0f, 30f, sign * at);

                var box = wall.AddComponent<BoxCollider>();
                box.size = alongZ
                    ? new Vector3(2f, 120f, _theme.arenaSize + 140f)
                    : new Vector3(_theme.arenaSize + 140f, 120f, 2f);
            }
        }

        /// <summary>
        /// One side of the ridge, as a shared-vertex heightfield strip.
        ///
        /// Laid out as a trapezoid rather than a rectangle: every vertex is placed at
        /// <c>|along| &lt;= out</c>, so the four sides tile the square annulus exactly
        /// and meet along its diagonals without overlapping. Overlapping them was the
        /// obvious build and puts a double-height lump on each corner.
        ///
        /// Vertices are shared and the normals come from the triangles, because a
        /// flat-shaded dune is a heap of gravel -- the whole reason the ground reads as
        /// sand at all is that it has no facets in it.
        /// </summary>
        private static void BuildBoundaryRun(Transform parent, int layer, Material sand,
                                             int side, float toe, float back)
        {
            const int across = 18;
            const int along = 150;

            bool alongZ = side >= 2;
            float sign = side % 2 == 0 ? 1f : -1f;

            var vertices = new Vector3[(across + 1) * (along + 1)];
            var uvs = new Vector2[vertices.Length];
            var triangles = new List<int>(across * along * 6);

            for (int a = 0; a <= across; a++)
            {
                float u = a / (float)across;
                float outward = Mathf.Lerp(toe, back, u);

                for (int b = 0; b <= along; b++)
                {
                    float t = (b / (float)along) * 2f - 1f;          // -1 .. 1 of this row
                    float lateral = t * outward;

                    float x = alongZ ? sign * outward : lateral;
                    float z = alongZ ? lateral : sign * outward;

                    vertices[a * (along + 1) + b] = new Vector3(x, BermHeightAt(x, z, u), z);
                    uvs[a * (along + 1) + b] = new Vector2(x, z);
                }
            }

            for (int a = 0; a < across; a++)
                for (int b = 0; b < along; b++)
                {
                    int v00 = a * (along + 1) + b;
                    int v01 = v00 + 1;
                    int v10 = (a + 1) * (along + 1) + b;
                    int v11 = v10 + 1;

                    // Wound so the face is up whichever side of the arena this is.
                    //
                    // <b>Two things flip here, not one, and getting it half right is
                    // worse than getting it wrong.</b> The grid's two axes are "outward"
                    // and "along", and which world axis each of those is depends on
                    // <c>alongZ</c>, while which way outward points depends on
                    // <c>sign</c> -- so the face is up when
                    // <c>cross(alongStep, outwardStep).y</c> is positive, which is
                    // <c>sign &gt; 0</c> on the sides that run in x and <c>sign &lt; 0</c>
                    // on the sides that run in z. Keyed on <c>sign</c> alone, the north
                    // and south runs came out inside out: a twenty-metre hill that is
                    // not drawn from any angle above it and that a raycast passes
                    // straight through, with the rocks that were sunk into it left
                    // hanging in the air over the dunes.
                    if (alongZ ? sign > 0f : sign < 0f)
                    {
                        triangles.Add(v00); triangles.Add(v01); triangles.Add(v11);
                        triangles.Add(v00); triangles.Add(v11); triangles.Add(v10);
                    }
                    else
                    {
                        triangles.Add(v00); triangles.Add(v11); triangles.Add(v01);
                        triangles.Add(v00); triangles.Add(v10); triangles.Add(v11);
                    }
                }

            var mesh = new Mesh { name = $"Berm_{side}" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            var run = MeshObject(parent, "SandRidge", mesh, sand, Vector3.zero,
                                 Quaternion.identity, Vector3.one, layer, SandTag);

            // Off the bake. The outer face lies back at well under the agent slope, so
            // left in it would be a kilometre of walkable navmesh outside the level for
            // the spawner to find, joined to the arena over the seal.
            NoStanding(run);

            // The map draws it as the edge of the world, which is what it is, and the
            // footprint of a straight run is honestly a rectangle -- unlike the river,
            // whose bend is the whole point and which is hidden and sliced instead.
            Mark(run, Shade(_theme.bankColor, 0.8f), order: -6);
        }

        /// <summary>
        /// Which sandstone an outcrop is cut from. Mostly the ordinary rock, some of it
        /// bleached, and only a minority of the iron-stained dark -- a boundary made
        /// entirely of the dark stone reads as a burnt ring round the arena, and it is
        /// the one colour on this map that is not a shade of the two the theme names.
        /// </summary>
        private static Material PickOutcropRock(System.Random rng)
        {
            float roll = Rand(rng, 0f, 1f);
            return roll < 0.16f ? _rockDarkMat : roll < 0.42f ? _rockPaleMat : _rockMat;
        }

        /// <summary>
        /// The ridge's own height at a point: the ground under it, plus a crest that
        /// rises and falls along the run.
        ///
        /// Taken from world position rather than from the run's own parameter, so the
        /// four sides agree with each other at the corners without being told to. The
        /// toe is sunk half a metre so the join with the dune field it grows out of is
        /// hidden under the sand rather than showing as a ring of coincident surfaces
        /// fighting for the same pixels all the way round the arena.
        /// </summary>
        private static float BermHeightAt(float x, float z, float u)
        {
            const float crestAt = 0.42f;

            float profile = u < crestAt
                ? Mathf.SmoothStep(0f, 1f, u / crestAt)
                : Mathf.SmoothStep(1f, 0f, (u - crestAt) / (1f - crestAt));

            // Two scales of variation along the run: a long swell so the ridge has high
            // stretches and low saddles, and a shorter one so no two hundred metres of
            // it are the same shape. Neither is allowed to bring the crest below about
            // half height -- a saddle you can see the skybox through is a hole.
            float swell = 1f + Fbm2(x * 0.0045f, z * 0.0045f, _theme.randomSeed * 53 + 11, 2) * 0.42f
                             + Fbm2(x * 0.014f, z * 0.014f, _theme.randomSeed * 71 + 3, 3) * 0.2f;

            return GroundHeightAt(x, z) - 0.5f + BermHeight * Mathf.Max(0.5f, swell) * profile;
        }

        /// <summary>
        /// Rock standing out of the ridge: weathered sandstone the wind has not buried.
        ///
        /// These are what give the hill a size -- a smooth slope of sand a hundred and
        /// fifty metres away could be three metres tall or thirty, and there is nothing
        /// in the shading to say which. They are boulders rather than buttes, so they are
        /// round and have nothing to get caught under, and every one is placed outside
        /// the seal where the player can look at it and not reach it.
        /// </summary>
        private static void BuildBoundaryOutcrops(Transform parent, int layer,
                                                  System.Random rng, float half)
        {
            for (int side = 0; side < 4; side++)
            {
                bool alongZ = side >= 2;
                float sign = side % 2 == 0 ? 1f : -1f;

                for (float t = -half; t <= half; t += Rand(rng, 26f, 54f))
                {
                    float outward = half + Rand(rng, 6f, 26f);
                    float width = Rand(rng, 6f, 14f);
                    float scale = width / BoulderSpread;

                    float x = alongZ ? sign * outward : t;
                    float z = alongZ ? t : sign * outward;

                    // Buried against the lowest the ridge gets under the rock's own
                    // footprint. The ridge is a slope, so a flat base pinned to the
                    // height at its centre stands clear of the sand downhill of it.
                    float floor = float.MaxValue;

                    for (int s = 0; s < 8; s++)
                    {
                        float angle = s * Mathf.PI * 2f / 8f;
                        float px = x + Mathf.Cos(angle) * width * 0.55f;
                        float pz = z + Mathf.Sin(angle) * width * 0.55f;
                        float pu = Mathf.InverseLerp(half - BermToe, half + BermBack,
                                                     Mathf.Max(Mathf.Abs(px), Mathf.Abs(pz)));

                        floor = Mathf.Min(floor, BermHeightAt(px, pz, pu));
                    }

                    var rock = MeshObject(parent, "Outcrop",
                                          BoulderMesh(rng.Next(1, 999), 0.42f, 0.78f),
                                          PickOutcropRock(rng),
                                          new Vector3(x, floor - scale * 0.15f, z),
                                          Quaternion.Euler(Rand(rng, -8f, 8f), Rand(rng, 0f, 360f),
                                                           Rand(rng, -8f, 8f)),
                                          new Vector3(scale, scale * Rand(rng, 0.7f, 1.3f),
                                                      scale * Rand(rng, 0.7f, 1.2f)),
                                          layer, _theme.wallTag);

                    NoStanding(rock);
                    Hide(rock);
                }
            }
        }

        /// <summary>
        /// The horizon. No colliders, off the navigation bake, and far enough out that
        /// it never reads as somewhere the player failed to reach.
        /// </summary>
        private static void BuildBackdrop(Transform root, int layer, System.Random rng, float half)
        {
            if (_theme.backdropCount <= 0) return;

            var group = new GameObject("Backdrop").transform;
            group.SetParent(root, false);

            for (int i = 0; i < _theme.backdropCount; i++)
            {
                // Golden angle, so they never line up into a visible ring however many
                // there are. Same trick the level spawner uses on its own ring.
                float angle = i * 2.39996f;
                float distance = Rand(rng, _theme.backdropDistance.x, _theme.backdropDistance.y);

                float h = Rand(rng, _theme.backdropHeight.x, _theme.backdropHeight.y);
                float w = Rand(rng, _theme.backdropWidth.x, _theme.backdropWidth.y);

                var pos = new Vector3(Mathf.Cos(angle) * distance, -h * 0.1f,
                                      Mathf.Sin(angle) * distance);

                // Further is hazier, which is the whole of aerial perspective and most
                // of why a flat-shaded backdrop reads as distance at all.
                float haze = Mathf.InverseLerp(_theme.backdropDistance.x,
                                               _theme.backdropDistance.y, distance);
                var tint = Color.Lerp(_theme.backdropColor, _theme.fogColor, haze * 0.45f);

                var mesa = ButteMesh(rng.Next(1, 999), sides: rng.Next(7, 11), levels: rng.Next(5, 9));

                MeshObject(group, "Mesa", mesa,
                           MakeMaterial($"Mesa_{ColorKey(tint)}", tint, 0.05f, 0f),
                           pos, Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f),
                           new Vector3(w * 0.5f, h, w * 0.5f * Rand(rng, 0.5f, 1.1f)),
                           layer, "Untagged", collider: false);
            }
        }

        // ==================================================================
        // Content: planning
        // ==================================================================
        /// <summary>
        /// Big solid rock inside the level. These are what the player navigates by --
        /// "go left of the tall one" is a route, and a level with nothing to say that
        /// about is a level where every direction looks the same.
        ///
        /// Deliberately given no flat pad. A boulder half-buried in the side of a dune is
        /// exactly right; a boulder standing on a mown circle of level sand is a prop on
        /// a table.
        /// </summary>
        private static void PlanLandmarks(System.Random rng, float half)
        {
            for (int i = 0; i < _theme.landmarkCount; i++)
            {
                float w = Rand(rng, 16f, 34f);
                float h = Rand(rng, 12f, 26f);

                // A cinder cone is broader and taller than a butte, and never so squat that
                // its upper flank falls under the fifty degrees a player can climb -- a
                // crater somebody can walk up to is a perch nothing else can reach.
                if (_theme.volcanicZone)
                {
                    w *= 1.6f;
                    h = Mathf.Max(h * 1.35f, w * 0.48f);
                }

                if (!TryClaim(rng, half * 0.94f, w * 0.75f, out Vector2 p)) continue;

                _landmarkPlans.Add(new LandmarkPlan
                {
                    Point = p,
                    Width = w,
                    Height = h,
                    Yaw = Rand(rng, 0f, 360f),
                    Seed = rng.Next(1, 9999)
                });

                _anchors.Add(new Vector3(p.x, 0f, p.y));
            }
        }

        private static void PlanOutposts(System.Random rng, float half)
        {
            var landings = new List<Vector3>(_anchors);

            for (int i = 0; i < _theme.outpostCount; i++)
            {
                float w = Rand(rng, 18f, 30f);
                float d = Rand(rng, 16f, 26f);
                float radius = Mathf.Max(w, d) * 0.7f;

                Vector2 p;

                // The first few take the bridge landings; after that, anywhere that fits.
                if (i < landings.Count && i < Mathf.Min(4, _theme.outpostCount))
                {
                    var landing = landings[i];
                    p = new Vector2(landing.x + Rand(rng, -8f, 8f), landing.z + Rand(rng, -26f, 26f));

                    if (!Free(p, radius)) continue;
                    Claim(p.x, p.y, radius);
                }
                else if (!TryClaim(rng, half * 0.9f, radius, out p))
                {
                    continue;
                }

                _outpostPlans.Add(new OutpostPlan
                {
                    Point = p,
                    Width = w,
                    Depth = d,
                    Yaw = Rand(rng, 0f, 360f)
                });

                // A compound is four straight walls meeting at right angles, and there is
                // no version of that which follows a hill. The blend is deliberately
                // longer than the pad is wide: it is the only thing standing between a
                // flat thirty-metre platform and the dunes around it, and short, it is a
                // plateau with a cut edge -- both to look at and, more to the point, to
                // drive off.
                FlattenPad(p.x, p.y, Mathf.Max(w, d) * 0.62f, 26f);

                _anchors.Add(new Vector3(p.x, 0f, p.y));
            }
        }

        /// <summary>
        /// Where the raised decks go: the positions the level is meant to be fought from.
        ///
        /// <b>This used to place one deck in ten and the arena was poorer for it in a way
        /// nothing reported.</b> Two things conspired. The claim was a single circle big
        /// enough to hold the deck <i>and</i> the whole run of its ramp -- about
        /// twenty-three metres -- when the ramp only ever leaves in one direction, so it
        /// reserved four times the ground it needed. And each vantage got exactly one
        /// attempt, thrown thirty-six to sixty metres from a landmark that had already
        /// claimed twenty-five of those metres for itself: the arithmetic almost never
        /// came out, so nine of ten were silently dropped and the map ended up with a
        /// single raised position on four hundred and fifty metres of ground.
        ///
        /// Now the deck claims the deck, the ramp foot claims the ramp foot, and each
        /// vantage gets a handful of throws before it gives up.
        /// </summary>
        private static void PlanVantages(System.Random rng, float half)
        {
            const float rampAngle = 22f;
            float tan = Mathf.Tan(rampAngle * Mathf.Deg2Rad);

            var targets = new List<Vector3>(_crossings);
            targets.AddRange(_anchors);

            for (int i = 0; i < _theme.vantageCount; i++)
            {
                float w = Rand(rng, 9f, 14f);
                float d = Rand(rng, 9f, 13f);
                float h = Rand(rng, 3.4f, 5.2f);

                float run = h / tan;
                float radius = Mathf.Max(w, d) * 0.5f + 4f;

                Vector2 p = Vector2.zero;
                Vector3 look = Vector3.zero;
                bool placed = false;

                for (int attempt = 0; attempt < 10 && !placed; attempt++)
                {
                    if (i < targets.Count)
                    {
                        // Placed at a distance from what it overlooks: on top of a bridge
                        // it would be part of the bridge, and too far and it is a
                        // sniper's nest with nothing to answer it.
                        var target = targets[i];
                        float angle = Rand(rng, 0f, 360f) * Mathf.Deg2Rad;
                        float away = Rand(rng, 40f, 78f);

                        p = new Vector2(target.x + Mathf.Cos(angle) * away,
                                        target.z + Mathf.Sin(angle) * away);

                        if (Mathf.Abs(p.x) > half * 0.92f || Mathf.Abs(p.y) > half * 0.92f) continue;
                        if (!Free(p, radius)) continue;

                        Claim(p.x, p.y, radius);
                        look = target;
                    }
                    else
                    {
                        if (!TryClaim(rng, half * 0.9f, radius, out p)) continue;
                        look = Vector3.zero;
                    }

                    placed = true;
                }

                if (!placed) continue;

                // The lip faces what the deck was put here to watch, so the cover is on
                // the side the shooting comes from.
                var toTarget = new Vector3(look.x - p.x, 0f, look.z - p.y);
                float yaw = toTarget.sqrMagnitude > 1f
                    ? Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg
                    : Rand(rng, 0f, 360f);

                _vantagePlans.Add(new VantagePlan
                {
                    Point = p, Width = w, Depth = d, Height = h, Yaw = yaw
                });

                // The pad has to take the ramp as well as the deck, or the ramp's foot
                // lands on a slope and the climb starts with a step the player has to
                // jump. Reached along -z of the deck's own facing.
                var foot = p + new Vector2(Mathf.Sin((yaw + 180f) * Mathf.Deg2Rad),
                                           Mathf.Cos((yaw + 180f) * Mathf.Deg2Rad)) * (d * 0.5f + run);

                // The ramp's own ground, claimed separately. This is the half of the old
                // circle that was actually needed, and claiming it here rather than
                // reserving a ring around the whole deck is what lets ten of these fit on
                // a map that previously took one.
                Claim(foot.x, foot.y, 6f);

                // Both held at the *deck's* height, not each at its own. The ramp is one
                // rigid plank from the deck down to the sand, so the sand it lands on has
                // to be level with the sand the deck stands on -- and pinning the two
                // discs to whatever the dunes happened to be doing under each of them put
                // a metres-high step in the few metres between them, which was the
                // steepest ground in the whole arena and the one place relaxation could
                // not touch, since both sides of it were held.
                float level = NaturalHeightAt(p.x, p.y);

                FlattenPad(p.x, p.y, Mathf.Max(w, d) * 0.55f, 24f, level);
                FlattenPad(foot.x, foot.y, 5f, 22f, level);
            }
        }

        // ==================================================================
        // Content: building
        // ==================================================================
        private static void BuildLandmarks(Transform root, int layer, System.Random rng)
        {
            var group = new GameObject("Landmarks").transform;
            group.SetParent(root, false);

            foreach (var plan in _landmarkPlans)
            {
                float spread = plan.Width * 0.75f;

                // <b>The lowest sand anywhere under it, not the sand at its middle.</b>
                // A butte is a closed shell with a flat floor, and this one is fifty
                // metres across on ground that rises and falls by ten -- pinned to its
                // centre and sunk six per cent of its height, the floor came out metres
                // above the sand on the downhill side. That is a doorway into the inside
                // of a closed mesh, and the inside of a closed mesh has no visible walls
                // at all, because every face of it is a backface. The player walks in
                // through a gap they can see, the view fills with rock they cannot place,
                // and they are wedged in geometry that is not drawn. It was the worst
                // thing in the arena and nothing about it reads as a bug from outside.
                float ground = LowestGroundIn(plan.Point.x, plan.Point.y, spread);

                var stack = new GameObject("Landmark").transform;
                stack.SetParent(group, false);
                stack.localPosition = new Vector3(plan.Point.x, ground, plan.Point.y);

                // Sunk by a fixed depth as well as a fraction, so a short butte is buried
                // as surely as a tall one.
                var butte = MeshObject(stack, "Butte", ButteMesh(plan.Seed, sides: rng.Next(7, 11)),
                                       _rockPaleMat,
                                       new Vector3(0f, -1.5f - plan.Height * 0.06f, 0f),
                                       Quaternion.Euler(0f, plan.Yaw, 0f),
                                       new Vector3(plan.Width * 0.5f, plan.Height,
                                                   plan.Width * 0.5f * Rand(rng, 0.7f, 1.2f)),
                                       layer, _theme.wallTag);

                // A butte has a flat cap on top of it, twenty metres up, and the bake
                // reads a flat cap as walkable ground -- so every landmark on the map
                // grows an island of navmesh nothing can reach. Harmless right up until
                // the spawner samples near a player standing at the foot of one and puts
                // an enemy on the summit, where it stands still for the rest of the level
                // and the player is scored against a kill they cannot make.
                NoStanding(butte);

                // Talus: the rock that has already fallen off it. This is the detail that
                // stops a butte reading as something placed -- weathering puts a skirt of
                // its own debris around anything that has stood in a desert for long.
                int fallen = rng.Next(4, 8);

                for (int k = 0; k < fallen; k++)
                {
                    float angle = Rand(rng, 0f, Mathf.PI * 2f);
                    float away = plan.Width * Rand(rng, 0.42f, 0.78f);
                    float size = Rand(rng, 1.6f, 4.6f);

                    float bx = plan.Point.x + Mathf.Cos(angle) * away;
                    float bz = plan.Point.y + Mathf.Sin(angle) * away;

                    // Parented to the group rather than to the butte, so its position is
                    // world space and the ground height it was sampled at is the one it
                    // is placed at.
                    Boulder(group, layer, rng, new Vector3(bx, 0f, bz), size);
                }
            }
        }

        /// <summary>
        /// Walled compounds: the strongpoints.
        ///
        /// The first ones go on the bridge landings, because that is the ground worth
        /// holding and a crossing with nothing at either end is a crossing with no reason
        /// to be crossed. The rest fill the banks.
        ///
        /// Each has a way in, a wall to fight from behind, and supplies inside -- so it
        /// is somewhere to go rather than something to look at.
        /// </summary>
        private static void BuildOutposts(Transform root, int layer, System.Random rng)
        {
            var group = new GameObject("Outposts").transform;
            group.SetParent(root, false);

            for (int i = 0; i < _outpostPlans.Count; i++)
            {
                var plan = _outpostPlans[i];

                BuildOutpost(group, layer, rng,
                             new Vector3(plan.Point.x, GroundHeightAt(plan.Point.x, plan.Point.y),
                                         plan.Point.y),
                             plan.Width, plan.Depth, plan.Yaw, i);
            }
        }

        private static void BuildVantages(Transform root, int layer, System.Random rng)
        {
            var group = new GameObject("Vantages").transform;
            group.SetParent(root, false);

            for (int i = 0; i < _vantagePlans.Count; i++)
            {
                var plan = _vantagePlans[i];

                var deck = new GameObject($"Vantage_{i}").transform;
                deck.SetParent(group, false);
                deck.localPosition = new Vector3(plan.Point.x,
                                                 GroundHeightAt(plan.Point.x, plan.Point.y),
                                                 plan.Point.y);
                deck.localRotation = Quaternion.Euler(0f, plan.Yaw, 0f);

                BuildScaffold(deck, layer, rng, plan.Width, plan.Depth, plan.Height);
            }
        }

        /// <summary>
        /// Cover strung between two places that matter, laid across the line between them
        /// rather than along it.
        ///
        /// This is the pass that makes open ground crossable. A barrier facing the wrong
        /// way is scenery; a row of them across an approach is a route, and the
        /// difference is entirely in knowing what the approach was. Hence the anchors --
        /// the compounds and crossings already placed are the only reason any of this
        /// knows which way to face.
        /// </summary>
        private static void BuildCoverLines(Transform root, int layer, System.Random rng, float half)
        {
            if (_anchors.Count < 2) return;

            var group = new GameObject("CoverLines").transform;
            group.SetParent(root, false);

            for (int i = 0; i < _theme.coverLineCount; i++)
            {
                var a = _anchors[rng.Next(_anchors.Count)];
                var b = _anchors[rng.Next(_anchors.Count)];

                var along = b - a;
                along.y = 0f;

                if (along.sqrMagnitude < 900f) continue;

                float t = Rand(rng, 0.25f, 0.75f);
                var centre = a + along * t;

                if (Mathf.Abs(centre.x) > half * 0.92f || Mathf.Abs(centre.z) > half * 0.92f) continue;

                float facing = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
                int pieces = rng.Next(2, 5);
                float spread = Rand(rng, 4f, 7f);

                // One kind of cover per line. Mixed piece by piece it reads as a rubbish
                // heap; all of a kind it reads as something somebody put there, which is
                // what a defensive line is.
                int kind = rng.Next(3);

                // Across the line of travel, with a gap in it. A solid row is a wall and
                // a wall is a detour; a broken row is cover you move between.
                int gap = rng.Next(pieces);

                for (int k = 0; k < pieces; k++)
                {
                    if (k == gap) continue;

                    float offset = (k - (pieces - 1) * 0.5f) * spread;
                    var right = Quaternion.Euler(0f, facing + 90f, 0f) * Vector3.forward;
                    var pos = centre + right * offset;

                    if (!Free(new Vector2(pos.x, pos.z), 3.4f)) continue;
                    Claim(pos.x, pos.z, 3.4f);

                    float yaw = facing + 90f + Rand(rng, -12f, 12f);
                    var at = new Vector3(pos.x, GroundHeightAt(pos.x, pos.z), pos.z);

                    switch (kind)
                    {
                        case 0: BuildSandbagWall(group, layer, rng, at, yaw); break;
                        case 1: BuildTimberBarricade(group, layer, rng, at, yaw); break;
                        default: BuildRockSpine(group, layer, rng, at, yaw); break;
                    }
                }
            }
        }

        /// <summary>
        /// The filler: rock, rubble and scrub everywhere else, so that the ground between
        /// two things that matter is never a flat killing field.
        /// </summary>
        private static void BuildScatter(Transform root, int layer, System.Random rng, float half)
        {
            var group = new GameObject("Scatter").transform;
            group.SetParent(root, false);

            for (int i = 0; i < _theme.scatterClusterCount; i++)
            {
                if (!TryClaim(rng, half * 0.94f, 6f, out Vector2 p)) continue;

                int rocks = rng.Next(2, 5);

                for (int k = 0; k < rocks; k++)
                {
                    float x = p.x + Rand(rng, -4.5f, 4.5f);
                    float z = p.y + Rand(rng, -4.5f, 4.5f);

                    // A width now, not a scale -- see BoulderSpread. These are pieces of
                    // cover a player crouches behind, which is what the numbers always
                    // meant and not what they were producing.
                    Boulder(group, layer, rng, new Vector3(x, 0f, z), Rand(rng, 2.2f, 6f));
                }
            }
        }

        // ==================================================================
        /// <summary>
        /// Standing spawn points for an open zone, on both banks and none of them over
        /// the water.
        ///
        /// The walled version rings them at four tenths of the arena width, which on a
        /// map with a river through it drops two of the eight into the gorge. Nothing
        /// breaks: LevelManager samples the NavMesh before it uses a point and quietly
        /// skips one it cannot place, so the level simply has fewer directions enemies
        /// arrive from than it was written to have, and no error anywhere says so.
        ///
        /// Both banks on purpose. The player starts on one of them, and a level whose
        /// every reinforcement comes from the near side is a level where the bridge is
        /// scenery.
        /// </summary>
        private static Transform[] BuildOpenZoneSpawnPoints()
        {
            var root = new GameObject("SpawnPoints").transform;
            var list = new List<Transform>();

            float half = _theme.arenaSize * 0.5f;
            float clear = _theme.hazardWidth * 0.5f + RimBandWidth + 6f;

            const int count = 12;

            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                float radius = half * (0.38f + (i % 3) * 0.17f);

                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;

                float centre = GorgeCentreAt(z);
                float offset = x - centre;

                if (Mathf.Abs(offset) < clear)
                    x = centre + (offset < 0f ? -clear : clear);

                x = Mathf.Clamp(x, -half * 0.92f, half * 0.92f);
                z = Mathf.Clamp(z, -half * 0.92f, half * 0.92f);

                var point = new GameObject("Spawn_" + i).transform;
                point.SetParent(root);

                // On the sand rather than at sea level. SnapSpawnPointsToNavMesh would
                // pull it down afterwards anyway, but only if it lands within its search
                // radius -- and a point eight metres under a dune is inside the collider,
                // which is not somewhere NavMesh.SamplePosition looks first.
                point.position = new Vector3(x, GroundHeightAt(x, z) + 0.1f, z);
                list.Add(point);
            }

            return list.ToArray();
        }

        /// <summary>Whether a circle is clear of everything claimed so far.</summary>
        private static bool Free(Vector2 point, float radius)
        {
            foreach (var claim in _claimed)
            {
                float min = claim.z + radius;
                if ((new Vector2(claim.x, claim.y) - point).sqrMagnitude < min * min) return false;
            }

            return true;
        }

    }
}
#endif
