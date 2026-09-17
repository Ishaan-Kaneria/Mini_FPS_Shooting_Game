#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The open-zone arena: ground that runs to a horizon, a gorge cut across it with
    /// deadly water in the bottom, bridges over the gorge, and content placed because of
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
    ///   the gorge        decides where the banks are
    ///   the banks        decide where the bridges can land
    ///   the bridges      decide where the compounds go, because a crossing is worth
    ///                    holding and a crossing nobody holds is not a crossing
    ///   the compounds    decide where the vantages go, because height is only worth
    ///                    taking if it overlooks something
    ///   all of the above decide where the cover goes, because cover is a route between
    ///                    two places and not a decoration between them
    ///
    /// That ordering is the whole design. It is also why this file is a sequence of
    /// passes that hand their results to each other rather than a list of independent
    /// spawners.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        /// <summary>The gorge's centre line, sampled along z. Everything is placed against it.</summary>
        private static float GorgeCentreAt(float z)
            => _theme.hazardOffset
             + Mathf.Sin(z / 90f) * _theme.hazardMeander
             + Mathf.Sin(z / 37f + 1.7f) * _theme.hazardMeander * 0.35f;

        /// <summary>Where the bridges landed, so later passes can place things worth defending.</summary>
        private static readonly List<Vector3> _crossings = new List<Vector3>();

        /// <summary>Compounds and landmarks, so cover can be strung between them.</summary>
        private static readonly List<Vector3> _anchors = new List<Vector3>();

        // ==================================================================
        private static void BuildOpenZone()
        {
            _claimed.Clear();
            _crossings.Clear();
            _anchors.Clear();

            var root = new GameObject("Arena").transform;
            int layer = LayerMask.NameToLayer("Environment");
            int backdrop = LayerMask.NameToLayer("Backdrop");
            if (backdrop < 0) backdrop = layer;

            var rng = new System.Random(_theme.randomSeed);
            float half = _theme.arenaSize * 0.5f;

            ResolveSurfaceMaterials();

            // The player's start, and then the gorge, before anything can be placed on
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

            BuildApron(root, backdrop, half);
            BuildGround(root, layer, half);
            BuildGroundPatches(root, backdrop, rng, half);
            BuildGorge(root, layer, half);
            BuildCrossings(root, layer, half);
            BuildRimFences(root, layer, half);
            BuildBoundary(root, layer, half);
            BuildBackdrop(root, backdrop, rng, half);

            BuildLandmarks(root, layer, rng, half);
            BuildOutposts(root, layer, rng, half);
            BuildVantages(root, layer, rng, half);
            BuildCoverLines(root, layer, rng, half);
            BuildScatter(root, layer, rng, half);
            BuildProps(root, layer, rng, half);
            BuildAccentLights(root, rng);
        }

        // ==================================================================
        // Ground
        // ==================================================================
        /// <summary>
        /// The ground past the boundary. No colliders and off the navigation bake -- it
        /// exists so that the edge of the level is not the edge of the world, which is
        /// the single biggest reason the walled arenas read as a box.
        ///
        /// Split around the gorge exactly as the banks are, and the river runs out
        /// through it. One slab across the whole thing was the first version, and it
        /// filled the canyon in from below: from the rim the gorge was a strip of ground
        /// a shade paler than the ground beside it, with a bridge over nothing and a
        /// railing guarding nothing. The river has to leave the level for the level to
        /// look like it is somewhere the river came through.
        /// </summary>
        private static void BuildApron(Transform root, int layer, float half)
        {
            if (_theme.apronSize <= 0f) return;

            var group = new GameObject("Apron").transform;
            group.SetParent(root, false);

            float reach = half + _theme.apronSize;
            int steps = 48;
            float span = reach * 2f / steps;

            float edge = _theme.hazardWidth * 0.5f;
            float depth = _theme.hazardDepth;
            bool dry = _theme.hazard == LevelTheme.Hazard.Chasm;

            var ground = MakeMaterial("Apron", Shade(_theme.floorColor, 0.94f),
                                      _theme.floorSmoothness * 0.5f, 0f);
            var bed = MakeMaterial("ApronBed", Shade(_theme.bankColor, 0.55f), 0.1f, 0f);
            var river = MakeMaterial("ApronWater", _theme.hazardColor, 0.92f, 0.1f);

            for (int i = 0; i < steps; i++)
            {
                float z = -reach + span * (i + 0.5f);
                float centre = GorgeCentreAt(z);
                float slice = span + 0.8f;

                Decor(group, layer, "Apron", new Vector3((-reach + centre - edge) * 0.5f, -3f, z),
                      new Vector3(centre - edge + reach, 6f, slice), ground);

                Decor(group, layer, "Apron", new Vector3((centre + edge + reach) * 0.5f, -3f, z),
                      new Vector3(reach - centre - edge, 6f, slice), ground);

                Decor(group, layer, "ApronBed", new Vector3(centre, -depth - 1f, z),
                      new Vector3(_theme.hazardWidth + 8f, 3f, slice), bed);

                if (!dry)
                    Decor(group, layer, "ApronWater", new Vector3(centre, -depth + 1.4f, z),
                          new Vector3(_theme.hazardWidth - 1f, 1.2f, slice), river);
            }
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
        /// Flat desert lit by one directional light is one colour everywhere, and one
        /// colour everywhere reads as a texture-less plane whatever is standing on it --
        /// it is most of why the open ground looked like a backdrop rather than a place.
        /// These are decoration, so they have no collider and sit on the layer the bake
        /// and the map both ignore.
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
        /// The two banks. Built as a pair of slabs with the gorge between them rather
        /// than as one floor with a hole cut in it, because the hole is what the
        /// navigation bake reads: no floor means no NavMesh, which means nothing can
        /// path across the water without being told, and the bridges become the only
        /// crossings for free.
        /// </summary>
        private static void BuildGround(Transform root, int layer, float half)
        {
            float depth = _theme.hazardDepth;
            int steps = 14;
            float span = _theme.arenaSize / steps;

            var group = new GameObject("Ground").transform;
            group.SetParent(root, false);

            // Sliced along z so the banks can follow a gorge that wanders. One slab per
            // slice, each ending where that slice's rim is.
            for (int i = 0; i < steps; i++)
            {
                float z = -half + span * (i + 0.5f);
                float centre = GorgeCentreAt(z);
                float edge = _theme.hazardWidth * 0.5f;

                float westEdge = centre - edge;
                float eastEdge = centre + edge;

                // A hair of overlap at each seam, or the slices show as cracks of
                // skybox when the camera is low.
                float slice = span + 0.4f;

                Bank(group, layer, "BankWest", z, slice, -half, westEdge, depth);
                Bank(group, layer, "BankEast", z, slice, eastEdge, half, depth);
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
        // The gorge
        // ==================================================================
        private static void ClaimGorge(float half)
        {
            float edge = _theme.hazardWidth * 0.5f;

            // Claimed generously -- the extra is the rim, and a compound built with one
            // wall overhanging the water is a compound with a wall nobody can stand at.
            float radius = edge + 16f;

            for (float z = -half; z <= half; z += edge)
                Claim(GorgeCentreAt(z), z, radius);
        }

        /// <summary>
        /// The cut itself: sloped rock down both sides, a bed at the bottom, the water on
        /// top of the bed, and the trigger that makes falling in mean something.
        /// </summary>
        private static void BuildGorge(Transform root, int layer, float half)
        {
            var group = new GameObject("Gorge").transform;
            group.SetParent(root, false);

            float depth = _theme.hazardDepth;
            float edge = _theme.hazardWidth * 0.5f;
            int steps = 24;
            float span = _theme.arenaSize / steps;

            bool dry = _theme.hazard == LevelTheme.Hazard.Chasm;
            var surface = dry ? _theme.bankColor : _theme.hazardColor;

            for (int i = 0; i < steps; i++)
            {
                float z = -half + span * (i + 0.5f);
                float centre = GorgeCentreAt(z);
                float slice = span + 0.4f;

                // Bed.
                Drowned(CreateBlock(group, new Vector3(centre, -depth - 1f, z),
                                    new Vector3(_theme.hazardWidth + 6f, 3f, slice), 0f, layer,
                                    _theme.floorTag, Shade(_theme.bankColor, 0.6f), 0.1f, 0f, "Bed"));

                // Two courses of rock stepping in as they go down, so the wall of the
                // gorge is not one flat face.
                for (int k = 0; k < 2; k++)
                {
                    float y = -depth * (0.3f + k * 0.38f);
                    float inset = 1.5f + k * 2.2f;
                    float thickness = depth * 0.34f;

                    Drowned(CreateBlock(group, new Vector3(centre - edge - 1.4f + inset, y, z),
                                        new Vector3(4.5f, thickness, slice), 0f, layer, _theme.floorTag,
                                        Shade(_theme.bankColor, 0.78f - k * 0.08f), 0.1f, 0f, "Ledge"));

                    Drowned(CreateBlock(group, new Vector3(centre + edge + 1.4f - inset, y, z),
                                        new Vector3(4.5f, thickness, slice), 0f, layer, _theme.floorTag,
                                        Shade(_theme.bankColor, 0.78f - k * 0.08f), 0.1f, 0f, "Ledge"));
                }

                if (dry) continue;

                // The water. Tagged so footsteps and bullet impacts know what they hit,
                // and marked so the map draws it as water rather than as one more grey
                // shape lying in a gap.
                var water = CreateBlock(group, new Vector3(centre, -depth + 1.4f, z),
                                        new Vector3(_theme.hazardWidth - 1f, 1.2f, slice), 0f,
                                        layer, "Water", surface, 0.92f, 0.1f, "Water");

                Object.DestroyImmediate(water.GetComponent<Collider>());
                Drowned(water);
                Mark(water, surface, order: 1);
            }

            BuildKillVolume(group, half, depth);
        }

        /// <summary>
        /// One trigger down the whole gorge, its top set below the rim rather than at it.
        ///
        /// At the rim it would kill somebody standing safely on the edge looking down,
        /// which is the one place the level most wants them to stand -- the whole point
        /// of a drop is being able to see it. Below the rim, you die when you are past
        /// saving and not before.
        /// </summary>
        private static void BuildKillVolume(Transform parent, float half, float depth)
        {
            var go = new GameObject("KillVolume");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(_theme.hazardOffset, -depth * 0.5f - 2f, 0f);

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(_theme.hazardWidth + _theme.hazardMeander * 2.5f,
                                   depth, _theme.arenaSize + 40f);

            var kill = go.AddComponent<KillVolume>();
            kill.instantKill = _theme.hazard != LevelTheme.Hazard.Electrified;
            kill.damagePerSecond = 70f;

            // No splash in the audio library yet, and a wrong sound is worse than none:
            // Tools/generate-placeholder-audio.py is where one would be authored, and it
            // has to go at the end of that script or every clip below it is re-rolled.
            kill.enterClip = null;
        }

        // ==================================================================
        // Crossings
        // ==================================================================
        /// <summary>
        /// The bridges, and the reason the level has a shape.
        ///
        /// Spread down the gorge rather than clustered, and each one deliberately long:
        /// the deck is the most exposed ground in the level and the whole tension of an
        /// open map is the walk across it. Railings both sides, which are cover as well
        /// as a fence -- crouching behind a rail halfway over is the fight this level is
        /// built to produce.
        /// </summary>
        private static void BuildCrossings(Transform root, int layer, float half)
        {
            var group = new GameObject("Crossings").transform;
            group.SetParent(root, false);

            int count = Mathf.Clamp(_theme.bridgeCount, 1, 3);
            float depth = _theme.hazardDepth;

            for (int i = 0; i < count; i++)
            {
                // Spread across the middle two thirds. Against the boundary a bridge is
                // a corner nobody goes to.
                float t = count == 1 ? 0.5f : i / (float)(count - 1);
                float z = Mathf.Lerp(-half * 0.62f, half * 0.62f, t);

                float centre = GorgeCentreAt(z);
                float length = _theme.hazardWidth + 46f;
                float w = _theme.bridgeWidth;

                var bridge = new GameObject($"Bridge_{i}").transform;
                bridge.SetParent(group, false);
                bridge.localPosition = new Vector3(centre, 0f, z);

                // Deck, its top flush with the banks so there is no step at either end.
                var deck = CreateBlock(bridge, new Vector3(0f, -0.4f, 0f),
                                       new Vector3(length, 0.8f, w), 0f, layer, _theme.coverTag,
                                       _theme.bridgeColor, 0.2f, 0f, "Deck");
                Mark(deck, _theme.bridgeColor, order: 4);

                // Railings. Chest high, so they stop a fall and can be fired over.
                for (int side = -1; side <= 1; side += 2)
                {
                    CreateBlock(bridge, new Vector3(0f, 0.55f, side * (w * 0.5f - 0.2f)),
                                new Vector3(length, 1.1f, 0.4f), 0f, layer, _theme.coverTag,
                                Shade(_theme.bridgeColor, 1.1f), 0.2f, 0f, "Rail");

                    // Posts, for the silhouette. Thin enough not to block a shot.
                    for (float p = -length * 0.5f + 3f; p <= length * 0.5f - 3f; p += 7f)
                        CreateBlock(bridge, new Vector3(p, 1.35f, side * (w * 0.5f - 0.2f)),
                                    new Vector3(0.45f, 2.7f, 0.45f), 0f, layer, _theme.coverTag,
                                    Shade(_theme.bridgeColor, 0.85f), 0.2f, 0f, "Post");
                }

                // Pylons down to the bed, which is what makes it read as a bridge from
                // below and from the rim rather than as a plank lying across a hole.
                for (int p = -1; p <= 1; p += 2)
                    CreateBlock(bridge, new Vector3(p * _theme.hazardWidth * 0.25f, -depth * 0.5f, 0f),
                                new Vector3(3.2f, depth, w * 0.7f), 0f, layer, _theme.coverTag,
                                Shade(_theme.bridgeColor, 0.7f), 0.2f, 0f, "Pylon");

                // Towers at both ends: a landmark visible from across the map, which is
                // how a player finds a crossing without a map telling them.
                for (int e = -1; e <= 1; e += 2)
                {
                    float x = e * (length * 0.5f - 2f);

                    for (int side = -1; side <= 1; side += 2)
                        CreateBlock(bridge, new Vector3(x, 4f, side * (w * 0.5f + 0.6f)),
                                    new Vector3(2.2f, 9f, 2.2f), 0f, layer, _theme.wallTag,
                                    _theme.wallColor, _theme.wallSmoothness, 0f, "Tower", _perimeterMat);
                }

                _crossings.Add(new Vector3(centre, 0f, z));

                // Both landings, so the passes below know the two bits of ground worth
                // fighting over and can put something there.
                _anchors.Add(new Vector3(centre - _theme.hazardWidth * 0.5f - 18f, 0f, z));
                _anchors.Add(new Vector3(centre + _theme.hazardWidth * 0.5f + 18f, 0f, z));
            }
        }

        /// <summary>
        /// Railing down both rims, with the bridge approaches left open.
        ///
        /// It is a guard rail and it is also the level telling the truth: the edge is
        /// lethal, and an edge that looks like ordinary ground until you are past it is
        /// a level killing the player for something it never showed them. The gaps are
        /// the other half of the same sentence -- the only places the rail stops are the
        /// places you are meant to cross.
        /// </summary>
        private static void BuildRimFences(Transform root, int layer, float half)
        {
            var group = new GameObject("RimFence").transform;
            group.SetParent(root, false);

            float edge = _theme.hazardWidth * 0.5f;
            const float step = 6f;

            for (float z = -half + 3f; z <= half - 3f; z += step)
            {
                if (NearCrossing(z, _theme.bridgeWidth * 0.5f + 7f)) continue;

                float centre = GorgeCentreAt(z);

                for (int side = -1; side <= 1; side += 2)
                {
                    float x = centre + side * (edge + 1.6f);

                    // The rail runs along z, so it is built as one short length per step
                    // and follows the gorge's wander for free.
                    CreateBlock(group, new Vector3(x, 0.95f, z + step * 0.5f),
                                new Vector3(0.18f, 0.16f, step + 0.3f), 0f, layer, _theme.coverTag,
                                Shade(_theme.bridgeColor, 1.15f), 0.3f, 0.4f, "Rail");

                    CreateBlock(group, new Vector3(x, 0.55f, z + step * 0.5f),
                                new Vector3(0.14f, 0.14f, step + 0.3f), 0f, layer, _theme.coverTag,
                                Shade(_theme.bridgeColor, 1.05f), 0.3f, 0.4f, "Rail");

                    CreateBlock(group, new Vector3(x, 0.5f, z),
                                new Vector3(0.28f, 1.05f, 0.28f), 0f, layer, _theme.coverTag,
                                Shade(_theme.bridgeColor, 0.9f), 0.3f, 0.4f, "FencePost");
                }
            }
        }

        private static bool NearCrossing(float z, float reach)
        {
            foreach (var crossing in _crossings)
                if (Mathf.Abs(crossing.z - z) < reach) return true;

            return false;
        }

        // ==================================================================
        // The edge of the world
        // ==================================================================
        /// <summary>
        /// The boundary, as broken ground rather than as a wall.
        ///
        /// A flat perimeter wall is exactly the thing this arena shape exists to stop
        /// being. So the visible edge is a jumbled ridge -- blocks of varying height,
        /// width and angle, sunk into the ground -- and the thing that actually holds
        /// the player in is an invisible box behind it. The ridge can then be as ragged
        /// as it likes without leaving a gap to walk through.
        /// </summary>
        private static void BuildBoundary(Transform root, int layer, float half)
        {
            var group = new GameObject("Boundary").transform;
            group.SetParent(root, false);

            var rng = new System.Random(_theme.randomSeed * 31 + 7);

            for (int side = 0; side < 4; side++)
            {
                bool alongZ = side >= 2;
                float sign = side % 2 == 0 ? 1f : -1f;

                for (float t = -half; t <= half; t += Rand(rng, 14f, 26f))
                {
                    float w = Rand(rng, 18f, 34f);
                    float h = Rand(rng, 14f, 32f);
                    float lean = Rand(rng, -14f, 14f);
                    float out0 = Rand(rng, -3f, 6f);

                    var pos = alongZ
                        ? new Vector3(sign * (half + out0), h * 0.35f - 3f, t)
                        : new Vector3(t, h * 0.35f - 3f, sign * (half + out0));

                    var scale = alongZ
                        ? new Vector3(Rand(rng, 12f, 22f), h, w)
                        : new Vector3(w, h, Rand(rng, 12f, 22f));

                    CreateBlock(group, pos, scale, lean, layer, _theme.wallTag,
                                Shade(_theme.bankColor, Rand(rng, 0.7f, 1.05f)),
                                _theme.wallSmoothness, 0f, "Ridge", _perimeterMat);
                }

                // The seal. Invisible, tall, and inside the ridge, so the player is
                // stopped by the thing they can see rather than by a gap in it.
                var wall = new GameObject($"Seal_{side}");
                wall.transform.SetParent(group, false);
                wall.layer = layer;
                wall.transform.localPosition = alongZ
                    ? new Vector3(sign * (half - 1f), 20f, 0f)
                    : new Vector3(0f, 20f, sign * (half - 1f));

                var box = wall.AddComponent<BoxCollider>();
                box.size = alongZ
                    ? new Vector3(2f, 60f, _theme.arenaSize + 60f)
                    : new Vector3(_theme.arenaSize + 60f, 60f, 2f);
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

                var pos = new Vector3(Mathf.Cos(angle) * distance, h * 0.35f - 8f,
                                      Mathf.Sin(angle) * distance);

                // Further is hazier, which is the whole of aerial perspective and most
                // of why a flat-shaded backdrop reads as distance at all.
                float haze = Mathf.InverseLerp(_theme.backdropDistance.x,
                                               _theme.backdropDistance.y, distance);
                var tint = Color.Lerp(_theme.backdropColor, _theme.fogColor, haze * 0.45f);

                var block = CreateBlock(group, pos,
                                        new Vector3(w, h, w * Rand(rng, 0.5f, 1.1f)),
                                        Rand(rng, 0f, 360f), layer, "Untagged",
                                        tint, 0.05f, 0f, "Mesa");

                Object.DestroyImmediate(block.GetComponent<Collider>());
            }
        }

        // ==================================================================
        // Content
        // ==================================================================
        /// <summary>
        /// Big solid rock inside the level. These are what the player navigates by --
        /// "go left of the tall one" is a route, and a level with nothing to say that
        /// about is a level where every direction looks the same.
        /// </summary>
        private static void BuildLandmarks(Transform root, int layer, System.Random rng, float half)
        {
            var group = new GameObject("Landmarks").transform;
            group.SetParent(root, false);

            for (int i = 0; i < _theme.landmarkCount; i++)
            {
                float w = Rand(rng, 16f, 34f);
                float h = Rand(rng, 12f, 26f);

                if (!TryClaim(rng, half * 0.94f, w * 0.75f, out Vector2 p)) continue;

                var stack = new GameObject($"Landmark_{i}").transform;
                stack.SetParent(group, false);
                stack.localPosition = new Vector3(p.x, 0f, p.y);
                stack.localRotation = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);

                // Four slabs, each turned and shoved off centre by a good fraction of
                // its own width. Stacked concentrically they came out as step pyramids --
                // regular enough that a dozen of them read as architecture, which is the
                // opposite of a landmark somebody can describe to themselves as "the big
                // rock". The irregularity is the entire point of the pass.
                int slabs = rng.Next(3, 5);

                for (int k = 0; k < slabs; k++)
                {
                    float shrink = Rand(rng, 0.94f, 1.05f) - k * Rand(rng, 0.16f, 0.3f);
                    if (shrink < 0.25f) break;

                    float lift = h * (0.22f + k * Rand(rng, 0.22f, 0.34f)) - 2.5f;
                    float wander = w * 0.22f;

                    CreateBlock(stack, new Vector3(Rand(rng, -wander, wander), lift,
                                                   Rand(rng, -wander, wander)),
                                new Vector3(w * shrink, h * Rand(rng, 0.4f, 0.75f),
                                            w * shrink * Rand(rng, 0.6f, 1.25f)),
                                Rand(rng, 0f, 360f), layer, _theme.wallTag,
                                Shade(_theme.bankColor, Rand(rng, 0.68f, 1.06f)),
                                _theme.wallSmoothness, 0f, "Rock", _perimeterMat);
                }

                _anchors.Add(new Vector3(p.x, 0f, p.y));
            }
        }

        /// <summary>
        /// Walled compounds: the strongpoints.
        ///
        /// The first ones go on the bridge landings, because that is the ground worth
        /// holding and a crossing with nothing at either end is a crossing with no
        /// reason to be crossed. The rest fill the banks.
        ///
        /// Each has a way in, a wall to fight from behind, and supplies inside -- so it
        /// is somewhere to go rather than something to look at.
        /// </summary>
        private static void BuildOutposts(Transform root, int layer, System.Random rng, float half)
        {
            var group = new GameObject("Outposts").transform;
            group.SetParent(root, false);

            int placed = 0;
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

                BuildOutpost(group, layer, rng, new Vector3(p.x, 0f, p.y), w, d,
                             Rand(rng, 0f, 360f), placed++);

                _anchors.Add(new Vector3(p.x, 0f, p.y));
            }
        }

        private static void BuildOutpost(Transform parent, int layer, System.Random rng,
                                         Vector3 position, float w, float d, float yaw, int index)
        {
            var compound = new GameObject($"Outpost_{index}").transform;
            compound.SetParent(parent, false);
            compound.localPosition = position;
            compound.localRotation = Quaternion.Euler(0f, yaw, 0f);

            float h = Rand(rng, 3.2f, 4.4f);
            float thickness = Rand(rng, 0.5f, 0.8f);
            float door = Rand(rng, 4.5f, 6.5f);

            // Two ways in, on opposite sides, and two solid walls. Four solid walls is a
            // room nobody can enter; one way in is a trap rather than a strongpoint. Two
            // means it can be taken, held, and flanked, which is the only version worth
            // putting on a map.
            BuildWall(compound, layer, new Vector3(0f, h * 0.5f, d * 0.5f), w, h, thickness,
                      alongX: true, hasDoor: true, door);
            BuildWall(compound, layer, new Vector3(0f, h * 0.5f, -d * 0.5f), w, h, thickness,
                      alongX: true, hasDoor: true, door);
            BuildWall(compound, layer, new Vector3(-w * 0.5f, h * 0.5f, 0f), d, h, thickness,
                      alongX: false, hasDoor: rng.NextDouble() < 0.4, door);
            BuildWall(compound, layer, new Vector3(w * 0.5f, h * 0.5f, 0f), d, h, thickness,
                      alongX: false, hasDoor: false, door);

            // Something to fight over inside, and something to fight from behind.
            BuildCrateCluster(compound, layer, rng, new Vector3(Rand(rng, -4f, 4f), 0f, Rand(rng, -3f, 3f)));

            CreateBlock(compound, new Vector3(Rand(rng, -w * 0.3f, w * 0.3f), 0.65f, Rand(rng, -2f, 4f)),
                        new Vector3(Rand(rng, 4f, 7f), 1.3f, 0.7f), Rand(rng, 0f, 180f), layer,
                        _theme.coverTag, _theme.RandomCoverColor(rng), 0.3f, _theme.coverMetallic,
                        "Barricade", _coverMat);
        }

        /// <summary>
        /// The raised decks: a ramp up, a deck, and a lip to shoot over.
        ///
        /// Every one of them is aimed at something -- a crossing first, then a compound
        /// -- because height that overlooks nothing is a climb with no payoff, and that
        /// is what the scattered version of this was. It is also the piece of the old
        /// arenas worth keeping: it is the only place you get to look at the level from
        /// above, and the lip means arriving there is not the same as being exposed.
        /// </summary>
        private static void BuildVantages(Transform root, int layer, System.Random rng, float half)
        {
            var group = new GameObject("Vantages").transform;
            group.SetParent(root, false);

            const float rampAngle = 22f;
            float sin = Mathf.Sin(rampAngle * Mathf.Deg2Rad);
            float tan = Mathf.Tan(rampAngle * Mathf.Deg2Rad);

            var targets = new List<Vector3>(_crossings);
            targets.AddRange(_anchors);

            for (int i = 0; i < _theme.vantageCount; i++)
            {
                float w = Rand(rng, 9f, 14f);
                float d = Rand(rng, 9f, 13f);
                float h = Rand(rng, 3.4f, 5.2f);

                float run = h / tan;
                float length = h / sin;
                float radius = Mathf.Max(w, d) * 0.5f + run + 3f;

                Vector2 p;
                Vector3 look;

                if (i < targets.Count)
                {
                    // Placed at a distance from what it overlooks: on top of a bridge it
                    // would be part of the bridge, and too far and it is a sniper's nest
                    // with nothing to answer it.
                    var target = targets[i];
                    float angle = Rand(rng, 0f, 360f) * Mathf.Deg2Rad;
                    float away = Rand(rng, 36f, 60f);

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

                var deck = new GameObject($"Vantage_{i}").transform;
                deck.SetParent(group, false);
                deck.localPosition = new Vector3(p.x, 0f, p.y);

                // The lip faces what the deck was put here to watch, so the cover is on
                // the side the shooting comes from.
                var toTarget = new Vector3(look.x - p.x, 0f, look.z - p.y);
                float yaw = toTarget.sqrMagnitude > 1f
                    ? Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg
                    : Rand(rng, 0f, 360f);

                deck.localRotation = Quaternion.Euler(0f, yaw, 0f);

                var tone = _theme.RandomCoverColor(rng);

                CreateBlock(deck, new Vector3(0f, (h - 0.3f) * 0.5f, 0f),
                            new Vector3(w - 0.6f, h - 0.3f, d - 0.6f), 0f, layer, _theme.coverTag,
                            Shade(tone, 0.82f), 0.25f, _theme.coverMetallic, "Support", _coverMat);

                CreateBlock(deck, new Vector3(0f, h - 0.15f, 0f), new Vector3(w, 0.3f, d), 0f,
                            layer, _theme.coverTag, tone, 0.3f, _theme.coverMetallic, "Deck", _coverMat);

                var ramp = CreateBlock(deck, new Vector3(0f, h * 0.5f, -(d * 0.5f + run * 0.5f)),
                                       new Vector3(4.5f, 0.35f, length), 0f, layer, _theme.coverTag,
                                       Shade(tone, 0.9f), 0.25f, _theme.coverMetallic, "Ramp", _coverMat);
                ramp.transform.localRotation = Quaternion.Euler(-rampAngle, 0f, 0f);

                CreateBlock(deck, new Vector3(0f, h + 0.5f, d * 0.5f - 0.2f),
                            new Vector3(w, 1f, 0.4f), 0f, layer, _theme.coverTag, tone, 0.3f,
                            _theme.coverMetallic, "Lip", _coverMat);
            }
        }

        /// <summary>
        /// Cover strung between two places that matter, laid across the line between
        /// them rather than along it.
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

                    float h = Rand(rng, _theme.coverHeightRange.x, _theme.coverHeightRange.y);

                    CreateBlock(group, new Vector3(pos.x, h * 0.5f, pos.z),
                                new Vector3(Rand(rng, 3.5f, 5.5f), h, 0.8f),
                                facing + 90f + Rand(rng, -12f, 12f), layer, _theme.coverTag,
                                _theme.RandomCoverColor(rng), 0.3f, _theme.coverMetallic,
                                "Barrier", _coverMat);
                }
            }
        }

        /// <summary>
        /// The filler: small clusters of rock and rubble everywhere else, so that the
        /// ground between two things that matter is never a flat killing field.
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
                    float size = Rand(rng, 1.8f, 4.2f);

                    CreateBlock(group, new Vector3(p.x + Rand(rng, -4f, 4f), size * 0.35f,
                                                   p.y + Rand(rng, -4f, 4f)),
                                new Vector3(size, size * Rand(rng, 0.6f, 1.2f), size * Rand(rng, 0.7f, 1.3f)),
                                Rand(rng, 0f, 360f), layer, _theme.coverTag,
                                Shade(_theme.bankColor, Rand(rng, 0.75f, 1.08f)),
                                0.15f, 0f, "Rock", _coverMat);
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
            float clear = _theme.hazardWidth * 0.5f + 16f;

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
                point.position = new Vector3(x, 0.1f, z);
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

        /// <summary>
        /// Marks a surface at the bottom of the gorge as somewhere nothing may walk.
        ///
        /// A NavMeshSurface collects render meshes, not colliders, so taking the collider
        /// off the water was not enough: the top of the water was baked as ground, twenty
        /// metres down, with the ledges and the bed under it. It came out as an island
        /// nothing could path onto, so every route still went over a bridge and the map
        /// looked correct -- but the spawner samples the navmesh near the player, and a
        /// player standing on the rim could put an enemy on that island, inside the
        /// trigger, to drown on the frame it arrived and pay out a kill nobody made.
        /// </summary>
        private static void Drowned(GameObject go)
        {
            if (go == null) return;

            var modifier = go.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = 1;   // Not Walkable
        }

        /// <summary>Tells the minimap what something is, since the geometry cannot.</summary>
        private static void Mark(GameObject go, Color color, int order)
        {
            var marker = go.AddComponent<MinimapMarker>();
            marker.color = color;
            marker.order = order;
        }
    }
}
#endif
