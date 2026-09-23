#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// What people left on the desert: a ruined mud-brick town, an oasis with fields and a
    /// nomad camp beside it, an oil field across the river with its pipeline, the wrecks of
    /// trucks and a plane, a dirt track joining it all up -- and the heat and dust over it.
    ///
    /// Ishaan's pick, all four, from a short list after he asked to "focus on the desert
    /// scene". The desert had the best ground in the game and the least on it: from the
    /// spawn it was rippled sand, a fence and three palms, and every place worth going was a
    /// low adobe compound that looked like every other.
    ///
    /// <b>Planned before the ground, built after it.</b> The town, the oasis, the fields and
    /// every oil pad are flattened sites, and a site has to be in the pad list before the
    /// dune field is generated or the ground under it is whatever the dunes were doing
    /// (see FlattenPad). So <see cref="PlanDesertLife"/> runs with the other planning passes
    /// and <see cref="BuildDesertLife"/> with the building ones, and the second reads what the
    /// first chose.
    ///
    /// <b>The town is laid out on a grid of lanes, and that is a navigation decision.</b>
    /// Houses packed at random close courtyards nothing can reach -- the works yard found that
    /// the expensive way. On a grid every lane runs through, so every door opens onto ground
    /// that leads out of town.
    ///
    /// <b>Its own materials, never the plant's.</b> The stairs, steel and paint the industrial
    /// passes use are only resolved for the industrial zone; asked for here they are null,
    /// and a null material is a magenta building.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        /// <summary>A layout that gets all this: an open zone with a river, not the volcanic one.</summary>
        private static bool DesertLife =>
            _theme != null && _theme.openZone && !_theme.volcanicZone && _theme.hazard == LevelTheme.Hazard.River;

        private static Vector2 _townCentre, _oasisCentre, _fieldCentre, _planeSite;
        private static float _townRadius;
        private static bool _hasTown, _hasOasis, _hasFields, _hasPlane;
        private static readonly List<Vector2> _oilSites = new List<Vector2>();
        private static readonly List<Vector2> _track = new List<Vector2>();
        private static readonly List<int> _trackBreaks = new List<int>();

        private static Material _earthMat, _clothRed, _clothBlue, _clothSaffron, _clothCream, _tentMat,
                                _grassMat, _cropMat, _soilMat, _alloyMat, _burntMat, _rustMat, _trackMat,
                                _shadowMat, _oilSteelMat, _dustMat;
        private static Material[] _adobeTints;

        /// <summary>Cleared with the terrain, for every arena: see ResetTerrain.</summary>
        private static void ResetDesertLife()
        {
            _hasTown = _hasOasis = _hasFields = _hasPlane = false;
            _oilSites.Clear();
            _track.Clear();
            _trackBreaks.Clear();
        }

        private static void ResolveDesertLifeMaterials()
        {
            // Made on first use by the fill; cleared here so a batch of arenas does not hand
            // one theme's materials to the next.
            _acaciaMat = _netMat = _sandbagMat = null;

            _earthMat = MakeDetailMaterial("PackedEarth", new Color(0.55f, 0.45f, 0.32f), "Adobe", 0.2f, 0.06f, 0f, 0.8f);
            _trackMat = MakeDetailMaterial("Track", new Color(0.50f, 0.41f, 0.29f), "Sand", 0.12f, 0.05f, 0f, 0.7f);
            _grassMat = MakeDetailMaterial("OasisGrass", new Color(0.30f, 0.40f, 0.16f), "Sand", 0.3f, 0.12f, 0f, 0.6f);
            _soilMat = MakeDetailMaterial("Soil", new Color(0.36f, 0.27f, 0.18f), "Sand", 0.25f, 0.08f, 0f, 0.8f);

            _adobeTints = new[]
            {
                _adobeMat,
                MakeDetailMaterial("AdobePale", Shade(_theme.wallColor, 1.12f), "Adobe", 0.28f, _theme.wallSmoothness, 0f, 0.9f),
                MakeDetailMaterial("AdobeWarm", new Color(0.72f, 0.54f, 0.38f), "Adobe", 0.28f, _theme.wallSmoothness, 0f, 0.9f),
                MakeDetailMaterial("AdobeRed", new Color(0.64f, 0.44f, 0.32f), "Adobe", 0.28f, _theme.wallSmoothness, 0f, 0.9f)
            };

            _clothRed = MakeMaterial("ClothRed", new Color(0.62f, 0.16f, 0.12f), 0.05f, 0f);
            _clothBlue = MakeMaterial("ClothBlue", new Color(0.16f, 0.24f, 0.46f), 0.05f, 0f);
            _clothSaffron = MakeMaterial("ClothSaffron", new Color(0.82f, 0.56f, 0.14f), 0.05f, 0f);
            _clothCream = MakeMaterial("ClothCream", new Color(0.82f, 0.76f, 0.62f), 0.05f, 0f);
            _tentMat = MakeMaterial("TentHair", new Color(0.20f, 0.16f, 0.12f), 0.04f, 0f);
            _cropMat = MakeMaterial("Crop", new Color(0.28f, 0.44f, 0.14f), 0.1f, 0f);
            _shadowMat = MakeMaterial("DoorShadow", new Color(0.10f, 0.08f, 0.07f), 0.05f, 0f);

            // Matte, both of them: there is no baked reflection here either, and anything glossy
            // reflects Unity's grey-blue default rather than this sky (see Matte).
            _alloyMat = MakeMaterial("Alloy", new Color(0.62f, 0.63f, 0.64f), 0.22f, 0.25f);
            _burntMat = MakeMaterial("Burnt", new Color(0.14f, 0.11f, 0.09f), 0.08f, 0f);
            _rustMat = MakeMaterial("WreckRust", new Color(0.40f, 0.23f, 0.13f), 0.1f, 0.1f);
            _oilSteelMat = MakeMaterial("OilSteel", new Color(0.30f, 0.30f, 0.31f), 0.25f, 0.3f);

            _dustMat = ParticleMaterial("Dust", "Puff", additive: false, new Color(0.86f, 0.74f, 0.55f));
        }

        // ==================================================================
        // Planning
        // ==================================================================
        /// <summary>
        /// Chooses every site before the ground exists. Its own random stream, so the choices
        /// the arena already made -- landmarks, outposts, vantages -- are not all reshuffled
        /// by adding this.
        /// </summary>
        private static void PlanDesertLife(float half)
        {
            ResetDesertLife();
            if (!DesertLife) return;

            var rng = new System.Random(_theme.randomSeed * 6151 + 7);
            float rim = _theme.hazardWidth * 0.5f + RimBandWidth;

            bool ClearOfRiver(Vector2 p, float r) => Mathf.Abs(p.x - GorgeCentreAt(p.y)) > rim + r + 14f;
            bool Inside(Vector2 p, float r) => Mathf.Abs(p.x) + r < half - 22f && Mathf.Abs(p.y) + r < half - 22f;

            // ---- the town: the spawn side of the river, a walk from the spawn ----
            _townRadius = 60f;
            for (int i = 0; i < 80 && !_hasTown; i++)
            {
                var p = new Vector2(Rand(rng, -half + 80f, 20f), Rand(rng, -half * 0.5f, half * 0.5f));
                if (p.magnitude < _townRadius + 40f || !ClearOfRiver(p, _townRadius) || !Inside(p, _townRadius)) continue;
                if (!Free(p, _townRadius + 8f)) continue;

                _townCentre = p;
                _hasTown = true;
                Claim(p.x, p.y, _townRadius + 10f);
                FlattenPad(p.x, p.y, _townRadius, 34f);
                _anchors.Add(new Vector3(p.x, 0f, p.y));
            }

            if (!_hasTown) { Debug.LogWarning("[FPSKit] desert: no room for the town."); return; }

            // ---- the oasis, beside the town, sunk into a bowl ----
            for (int i = 0; i < 16 && !_hasOasis; i++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f);
                var p = _townCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (_townRadius + 36f);
                if (!ClearOfRiver(p, 26f) || !Inside(p, 26f) || !Free(p, 24f)) continue;

                _oasisCentre = p;
                _hasOasis = true;
                Claim(p.x, p.y, 26f);

                // Pinned below the ground round it, which is what makes it a hollow water
                // runs into rather than a pond on a plateau.
                FlattenPad(p.x, p.y, 15f, 16f, NaturalHeightAt(p.x, p.y) - 1.4f);

                // ---- the fields, on the far side of the oasis from the town ----
                var away = (p - _townCentre).normalized;
                for (int k = 0; k < 30 && !_hasFields; k++)
                {
                    var f = p + (Vector2)(Quaternion.Euler(0f, 0f, Rand(rng, -120f, 120f)) * away) * Rand(rng, 44f, 60f);
                    if (!ClearOfRiver(f, 24f) || !Inside(f, 24f) || !Free(f, 22f)) continue;

                    _fieldCentre = f;
                    _hasFields = true;
                    Claim(f.x, f.y, 24f);
                    FlattenPad(f.x, f.y, 22f, 22f);
                }
            }

            // ---- the oil field, on the far bank ----
            for (int i = 0; i < 60 && _oilSites.Count < 5; i++)
            {
                float z = Rand(rng, -half * 0.8f, half * 0.8f);
                float east = GorgeCentreAt(z) + rim + 12f;
                if (east > half - 30f) continue;

                var p = new Vector2(Rand(rng, east, half - 30f), z);
                if (!Free(p, 11f)) continue;

                _oilSites.Add(p);
                Claim(p.x, p.y, 11f);
                FlattenPad(p.x, p.y, 7f, 10f);
            }

            _oilSites.Sort((a, b) => a.y.CompareTo(b.y));

            // ---- the plane, somewhere open on this bank ----
            for (int i = 0; i < 60 && !_hasPlane; i++)
            {
                var p = new Vector2(Rand(rng, -half + 50f, 10f), Rand(rng, -half + 50f, half - 50f));
                if (p.magnitude < 70f || !ClearOfRiver(p, 24f) || !Free(p, 24f)) continue;

                _planeSite = p;
                _hasPlane = true;
                Claim(p.x, p.y, 24f);

                // A crash site of its own. Sunk to the lowest sand under it on an open dune,
                // the fuselage went metres under its own middle and only the tail showed.
                FlattenPad(p.x, p.y, 13f, 18f);
            }

            PlanTrack(rng, half);
        }

        /// <summary>
        /// The dirt track: in from the west edge, down the town's main street, over the nearer
        /// bridge and up through the oil field. Claimed along its length as it is planned, so
        /// nothing placed afterwards lands on it. A car will want it.
        /// </summary>
        private static void PlanTrack(System.Random rng, float half)
        {
            var points = new List<Vector2>
            {
                new Vector2(-half + 14f, _townCentre.y + Rand(rng, -25f, 25f)),
                new Vector2(_townCentre.x - _townRadius - 12f, _townCentre.y),
                new Vector2(_townCentre.x - _townRadius * 0.5f, _townCentre.y),
                new Vector2(_townCentre.x + _townRadius * 0.5f, _townCentre.y),
                new Vector2(_townCentre.x + _townRadius + 12f, _townCentre.y)
            };

            // The bridge nearest the town, both of its landings.
            Vector3 bridge = Vector3.zero;
            float best = float.MaxValue;
            foreach (var c in _crossings)
            {
                float d = Mathf.Abs(c.z - _townCentre.y);
                if (d < best) { best = d; bridge = c; }
            }

            float deck = _theme.hazardWidth + BridgeOverhang * 2f;
            if (_crossings.Count > 0)
            {
                points.Add(new Vector2(bridge.x - deck * 0.5f - 22f, bridge.z));
                points.Add(new Vector2(bridge.x - deck * 0.5f + 2f, bridge.z));
                int west = points.Count - 1;

                points.Add(new Vector2(bridge.x + deck * 0.5f - 2f, bridge.z));
                points.Add(new Vector2(bridge.x + deck * 0.5f + 22f, bridge.z));

                // The span itself is the bridge, not track.
                _trackBreaks.Add(west);

                foreach (var site in _oilSites) points.Add(site + new Vector2(-12f, 0f));
                if (_oilSites.Count > 0)
                    points.Add(new Vector2(_oilSites[_oilSites.Count - 1].x - 12f, half - 14f));
            }

            // Smoothed through the points: a track bends, it does not corner.
            for (int i = 0; i < points.Count - 1; i++)
            {
                var p0 = points[Mathf.Max(0, i - 1)];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = points[Mathf.Min(points.Count - 1, i + 2)];

                bool broken = _trackBreaks.Contains(i);
                int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 3f));

                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps;
                    var q = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t
                                    + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);

                    // Across a break, mark it with a NaN so the drape leaves a gap.
                    _track.Add(broken ? new Vector2(float.NaN, float.NaN) : q);

                    if (!broken && _track.Count % 3 == 0 && (q - _townCentre).magnitude > _townRadius)
                        Claim(q.x, q.y, 5f);
                }
            }

            _track.Add(points[points.Count - 1]);
        }

        // ==================================================================
        // Building
        // ==================================================================
        /// <summary>Everything planned above, now that there is ground to put it on.</summary>
        private static void BuildDesertLife(Transform root, int layer, int backdrop, float half)
        {
            if (!DesertLife || !_hasTown) return;

            ResolveDesertLifeMaterials();

            var rng = new System.Random(_theme.randomSeed * 3301 + 11);
            var group = new GameObject("DesertLife").transform;
            group.SetParent(root, false);

            BuildTrack(group, backdrop);
            BuildTown(group, layer, backdrop, rng);
            if (_hasOasis) BuildOasis(group, layer, backdrop, rng);
            if (_hasFields) BuildFields(group, backdrop, rng);
            BuildOilField(group, layer, rng);
            BuildWrecks(group, layer, backdrop, rng);
            BuildDust(group, rng, half);

            Debug.Log($"[FPSKit] desert: town at {_townCentre}, oasis {_hasOasis}, fields {_hasFields}, " +
                      $"{_oilSites.Count} oil site(s), plane {_hasPlane}, track {_track.Count} points.");
        }

        // ---- shared ------------------------------------------------------
        private static GameObject Solid(Transform parent, string name, MeshBuild build, Material mat, int layer,
                                        string tag = "Concrete", bool standing = false)
        {
            if (build.Triangles.Count == 0) return null;
            var go = MeshObject(parent, name, ToMesh(build, DenseKey(name)), mat, Vector3.zero, Quaternion.identity,
                                Vector3.one, layer, tag);
            if (!standing) NoStanding(go);
            return go;
        }

        private static void Visual(Transform parent, string name, MeshBuild build, Material mat, int layer)
        {
            if (build.Triangles.Count == 0) return;
            Hide(MeshObject(parent, name, ToMesh(build, DenseKey(name)), mat, Vector3.zero, Quaternion.identity,
                            Vector3.one, layer, null, collider: false));
        }

        /// <summary>
        /// A solid mud-brick stair up the side of a building: steps as solid blocks from the
        /// ground, and a landing. <paramref name="landing"/> is where the landing's middle sits
        /// (at ground level), <paramref name="up"/> the way the flight climbs to reach it.
        /// Walkable, and meant to be.
        /// </summary>
        private static void AdobeStair(Transform parent, int layer, Material mat, Vector3 landing, Vector3 up, float height)
        {
            var build = new MeshBuild { UVScale = 0.3f };
            int steps = Mathf.Max(2, Mathf.RoundToInt(height / StairRise));
            const float wide = 1.8f;
            var across = Vector3.Cross(Vector3.up, up).normalized;

            Vector3 Size(float along, float y) => Mathf.Abs(up.x) > 0.5f ? new Vector3(along, y, wide) : new Vector3(wide, y, along);

            for (int i = 0; i < steps; i++)
            {
                float back = StairLanding * 0.5f + (steps - i - 0.5f) * StairTread;
                float h = (i + 1) * StairRise;
                build.Box(landing - up * back + Vector3.up * h * 0.5f, Size(StairTread, h), Quaternion.identity);
            }

            build.Box(landing + Vector3.up * (steps * StairRise * 0.5f), Size(StairLanding, steps * StairRise), Quaternion.identity);
            _ = across;

            MeshObject(parent, "Stair", ToMesh(build, DenseKey("adobestair")), mat, Vector3.zero, Quaternion.identity,
                       Vector3.one, layer, "Concrete");
        }

        // ==================================================================
        // The track
        // ==================================================================
        private static void BuildTrack(Transform parent, int layer)
        {
            // Runs of the planned line, broken at the bridge and wherever the ground is not
            // drapeable (the canyon's rim).
            var run = new List<Vector2>();
            int index = 0;

            void Flush()
            {
                if (run.Count >= 3)
                {
                    const int columns = 7;
                    var grid = new Vector2[run.Count, columns];

                    for (int r = 0; r < run.Count; r++)
                    {
                        var ahead = run[Mathf.Min(r + 1, run.Count - 1)] - run[Mathf.Max(r - 1, 0)];
                        var side = new Vector2(-ahead.y, ahead.x).normalized;

                        for (int c = 0; c < columns; c++)
                        {
                            float across = c / (float)(columns - 1) * 2f - 1f;
                            float ragged = 1f + Fbm2(r * 0.2f, c + index, 7100 + index, 2) * 0.15f;
                            grid[r, c] = run[r] + side * across * 3.4f * ragged;
                        }
                    }

                    // Worn in: two ruts a little lower than the crown between them.
                    var mesh = DrapeMesh($"Track_{index}", grid, (_, across) =>
                    {
                        float x = Mathf.Abs(across * 2f - 1f);
                        float rut = Mathf.Abs(x - 0.45f) < 0.12f ? -0.03f : 0f;
                        return Mathf.Lerp(0.07f, -0.12f, x * x) + rut;
                    }, 0.5f);
                    Drape(parent, "Track", mesh, _trackMat, layer);
                    index++;
                }

                run.Clear();
            }

            foreach (var p in _track)
            {
                if (float.IsNaN(p.x) || !Drapeable(p.x, p.y, 1f)) { Flush(); continue; }
                run.Add(p);
            }

            Flush();
        }

        // ==================================================================
        // The town
        // ==================================================================
        /// <summary>
        /// A ruined mud-brick town on its flattened site: houses on lots along a grid of lanes,
        /// a main street down the middle that the track runs along, a market square with
        /// awnings and a well, a minaret, and ruins open to walk through.
        ///
        /// Four of the houses have a stair up the outside to a roof you can fight from, sealed
        /// only to a metre below the roof so the roof itself stays on the bake.
        /// </summary>
        private static void BuildTown(Transform parent, int layer, int backdrop, System.Random rng)
        {
            var town = new GameObject("Town").transform;
            town.SetParent(parent, false);

            var c = _townCentre;
            float floor = GroundHeightAt(c.x, c.y);
            float R = _townRadius;

            // Packed earth under the whole town, so the streets read as streets.
            var earth = new MeshBuild { UVScale = 0.2f };
            const int Sides = 36;
            for (int i = 0; i < Sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / Sides, a1 = (i + 1) * Mathf.PI * 2f / Sides;
                float r0 = (R - 3f) * (1f + Fbm2(Mathf.Cos(a0) * 2f, Mathf.Sin(a0) * 2f, 7201, 2) * 0.12f);
                float r1 = (R - 3f) * (1f + Fbm2(Mathf.Cos(a1) * 2f, Mathf.Sin(a1) * 2f, 7201, 2) * 0.12f);
                AddUp(earth, new Vector3(c.x, floor + 0.03f, c.y), new Vector3(c.x + Mathf.Cos(a0) * r0, floor + 0.03f, c.y + Mathf.Sin(a0) * r0),
                      new Vector3(c.x + Mathf.Cos(a1) * r1, floor + 0.03f, c.y + Mathf.Sin(a1) * r1),
                      new Vector3(c.x + Mathf.Cos(a1) * r1, floor + 0.03f, c.y + Mathf.Sin(a1) * r1));
            }
            Flat(town, backdrop, "TownGround", earth, DenseKey("townground"), _earthMat);

            // ---- lots on a grid of lanes ----
            const float lot = 11f, lane = 4f, pitch = lot + lane;
            const float mainHalf = 4.5f, crossHalf = 3f;

            // The square is the four lots nearest the middle, left empty.
            float plazaX = crossHalf + lot, plazaZ = mainHalf + lot;

            var lots = new List<Vector2>();
            for (int j = -5; j <= 5; j++)
                for (int k = -5; k <= 5; k++)
                {
                    // Lanes between every lot, the main street along x through the middle
                    // and a narrower lane along z: every lot fronts onto ground that runs out.
                    if (j == 0 || k == 0) continue;
                    float x = c.x + Mathf.Sign(j) * (crossHalf + lot * 0.5f + (Mathf.Abs(j) - 1) * pitch);
                    float z = c.y + Mathf.Sign(k) * (mainHalf + lot * 0.5f + (Mathf.Abs(k) - 1) * pitch);

                    var p = new Vector2(x, z);
                    if ((p - c).magnitude + lot * 0.7f > R - 3f) continue;
                    if (Mathf.Abs(x - c.x) < plazaX && Mathf.Abs(z - c.y) < plazaZ) continue;
                    lots.Add(p);
                }

            int roofHouses = 0, houses = 0, ruins = 0;

            // The minaret takes the lot nearest a corner of the square.
            Vector2 minaretLot = lots.Count > 0 ? lots[0] : c;
            float nearest = float.MaxValue;
            var corner = c + new Vector2(plazaX, plazaZ);
            foreach (var p in lots) { float d = (p - corner).magnitude; if (d < nearest) { nearest = d; minaretLot = p; } }

            foreach (var p in lots)
            {
                if (p == minaretLot) { BuildMinaret(town, layer, new Vector3(p.x, floor, p.y)); continue; }

                double roll = rng.NextDouble();
                if (roll < 0.1) continue;                        // an empty lot: rubble and a gap
                if (roll < 0.26) { BuildRuin(town, layer, rng, new Vector3(p.x, floor, p.y), lot); ruins++; continue; }

                bool roof = roofHouses < 4 && rng.NextDouble() < 0.3;
                BuildHouse(town, layer, rng, new Vector3(p.x, floor, p.y), lot, roof);
                if (roof) roofHouses++;
                houses++;
            }

            BuildMarket(town, layer, rng, new Vector3(c.x, floor, c.y), plazaX, plazaZ);

            // Palms in the square and at the edge of town.
            for (int i = 0; i < 6; i++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f);
                var at = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Rand(rng, R + 2f, R + 12f);
                if (!Free(at, 2f)) continue;
                BuildPalm(town, layer, rng, new Vector3(at.x, GroundHeightAt(at.x, at.y) - 0.2f, at.y));
            }

            Debug.Log($"[FPSKit] desert town: {houses} house(s), {roofHouses} with a roof stair, {ruins} ruin(s).");
        }

        /// <summary>
        /// One house on its lot: a mud-brick box, sometimes a second storey stepped back on
        /// top, a parapet, the ends of roof beams through the wall, a door and small windows.
        /// </summary>
        private static void BuildHouse(Transform parent, int layer, System.Random rng, Vector3 lotCentre, float lot, bool roofStair)
        {
            const float storey = 18 * StairRise;   // a whole number of risers, for the stair
            bool twoStorey = !roofStair && rng.NextDouble() < 0.3;

            float w = Rand(rng, 7.5f, lot - 0.6f), d = Rand(rng, 7.5f, lot - 0.6f);
            if (roofStair) { w = Mathf.Min(w, lot - 3.2f); d = lot - 0.6f; }

            var centre = lotCentre + new Vector3(Rand(rng, -(lot - w) * 0.5f, (lot - w) * 0.5f) * (roofStair ? 0f : 1f), 0f,
                                                 Rand(rng, -(lot - d) * 0.5f, (lot - d) * 0.5f));
            if (roofStair) centre.x = lotCentre.x - (lot - w) * 0.5f;   // room for the stair on the +x side

            var mat = _adobeTints[rng.Next(_adobeTints.Length)];
            var shell = new MeshBuild { UVScale = 0.28f };
            shell.Box(centre + Vector3.up * storey * 0.5f, new Vector3(w, storey, d), Quaternion.identity);

            float top = storey;
            if (twoStorey)
            {
                float w2 = w * Rand(rng, 0.55f, 0.8f), d2 = d * Rand(rng, 0.55f, 0.8f);
                var up = centre + new Vector3((w - w2) * 0.5f * (rng.Next(2) == 0 ? 1 : -1), 0f, (d - d2) * 0.5f * (rng.Next(2) == 0 ? 1 : -1));
                shell.Box(up + Vector3.up * (storey + 1.6f), new Vector3(w2, 3.2f, d2), Quaternion.identity);
                top = storey + 3.2f;
            }

            // The roof of a stair house is ground; everything else about the house is not.
            var house = MeshObject(parent, "House", ToMesh(shell, DenseKey("house")), mat, Vector3.zero, Quaternion.identity,
                                   Vector3.one, layer, "Concrete");
            if (!roofStair) NoStanding(house);

            if (roofStair) SealBox(parent, centre + Vector3.up * (storey - 3f) * 0.5f, new Vector3(w, storey - 3f, d));
            else SealBox(parent, centre + Vector3.up * top * 0.5f, new Vector3(w, top, d));

            // Parapet, open where the stair lands.
            var parapet = new MeshBuild { UVScale = 0.3f };
            const float ph = 0.7f, pt = 0.28f;
            float y = storey + ph * 0.5f;
            float gapZ = centre.z + d * 0.5f - 1.6f;

            parapet.Box(new Vector3(centre.x, y, centre.z - d * 0.5f + pt * 0.5f), new Vector3(w, ph, pt), Quaternion.identity);
            parapet.Box(new Vector3(centre.x, y, centre.z + d * 0.5f - pt * 0.5f), new Vector3(w, ph, pt), Quaternion.identity);
            parapet.Box(new Vector3(centre.x - w * 0.5f + pt * 0.5f, y, centre.z), new Vector3(pt, ph, d), Quaternion.identity);

            float eastX = centre.x + w * 0.5f - pt * 0.5f;
            if (roofStair)
            {
                // The +x run, with a gap where the landing arrives near its +z end.
                float z0 = centre.z - d * 0.5f, z1 = gapZ - 1.2f, z2 = gapZ + 1.2f, z3 = centre.z + d * 0.5f;
                if (z1 > z0) parapet.Box(new Vector3(eastX, y, (z0 + z1) * 0.5f), new Vector3(pt, ph, z1 - z0), Quaternion.identity);
                if (z3 > z2) parapet.Box(new Vector3(eastX, y, (z2 + z3) * 0.5f), new Vector3(pt, ph, z3 - z2), Quaternion.identity);
            }
            else parapet.Box(new Vector3(eastX, y, centre.z), new Vector3(pt, ph, d), Quaternion.identity);

            Solid(parent, "Parapet", parapet, mat, layer);

            // Beam ends through the wall, a door, and small high windows -- on the long faces.
            var beams = new MeshBuild { UVScale = 0.5f };
            var dark = new MeshBuild { UVScale = 0.5f };
            foreach (float s in new[] { -1f, 1f })
            {
                for (float x = -w * 0.5f + 0.8f; x < w * 0.5f - 0.6f; x += 1.3f)
                    beams.Box(centre + new Vector3(x, storey - 0.45f, s * (d * 0.5f + 0.25f)), new Vector3(0.18f, 0.18f, 0.5f), Quaternion.identity);

                float doorX = Rand(rng, -w * 0.3f, w * 0.3f);
                dark.Box(centre + new Vector3(doorX, 1.15f, s * (d * 0.5f + 0.04f)), new Vector3(1.2f, 2.3f, 0.1f), Quaternion.identity);

                for (float x = -w * 0.5f + 1.5f; x < w * 0.5f - 1.2f; x += 2.6f)
                    if (Mathf.Abs(x - doorX) > 1.4f && rng.NextDouble() < 0.6)
                        dark.Box(centre + new Vector3(x, 2.4f, s * (d * 0.5f + 0.04f)), new Vector3(0.7f, 0.7f, 0.1f), Quaternion.identity);
            }
            Visual(parent, "Beams", beams, _timberMat, layer);
            Visual(parent, "Openings", dark, _shadowMat, layer);

            // The stair: along the +x side, climbing towards +z, landing beside the gap.
            if (roofStair)
                AdobeStair(parent, layer, mat, new Vector3(centre.x + w * 0.5f + 0.95f, centre.y, gapZ), Vector3.forward, storey);
        }

        /// <summary>
        /// A ruin: four broken walls with no roof, a gap in two opposite sides so it is a way
        /// through as well as cover, and rubble inside.
        /// </summary>
        private static void BuildRuin(Transform parent, int layer, System.Random rng, Vector3 lotCentre, float lot)
        {
            float w = Rand(rng, 7.5f, lot - 1f), d = Rand(rng, 7.5f, lot - 1f);
            var build = new MeshBuild { UVScale = 0.28f };
            const float t = 0.55f;

            void Run(Vector3 a, Vector3 b, bool gap)
            {
                var dir = (b - a);
                float len = dir.magnitude;
                dir /= len;
                bool alongX = Mathf.Abs(dir.x) > 0.5f;

                // In broken lengths of varying height; one gap wide enough to walk through.
                float gapAt = gap ? Rand(rng, len * 0.3f, len * 0.7f) : -99f;
                for (float s = 0f; s < len; s += 1.2f)
                {
                    if (gap && Mathf.Abs(s + 0.6f - gapAt) < 1.6f) continue;
                    float h = Mathf.Max(0.4f, Rand(rng, 0.6f, 3.4f) * (0.6f + 0.4f * Mathf.Sin(s * 0.7f + gapAt)));
                    var p = a + dir * (s + 0.6f) + Vector3.up * h * 0.5f;
                    build.Box(p, alongX ? new Vector3(1.2f, h, t) : new Vector3(t, h, 1.2f), Quaternion.identity);
                }
            }

            var o = lotCentre;
            Run(o + new Vector3(-w * 0.5f, 0f, -d * 0.5f), o + new Vector3(w * 0.5f, 0f, -d * 0.5f), true);
            Run(o + new Vector3(-w * 0.5f, 0f, d * 0.5f), o + new Vector3(w * 0.5f, 0f, d * 0.5f), true);
            Run(o + new Vector3(-w * 0.5f, 0f, -d * 0.5f), o + new Vector3(-w * 0.5f, 0f, d * 0.5f), rng.NextDouble() < 0.5);
            Run(o + new Vector3(w * 0.5f, 0f, -d * 0.5f), o + new Vector3(w * 0.5f, 0f, d * 0.5f), rng.NextDouble() < 0.5);

            // Rubble: fallen blocks, as cover.
            for (int i = 0; i < 4; i++)
                build.Box(o + new Vector3(Rand(rng, -w * 0.3f, w * 0.3f), 0.35f, Rand(rng, -d * 0.3f, d * 0.3f)),
                          new Vector3(Rand(rng, 0.8f, 1.8f), 0.7f, Rand(rng, 0.6f, 1.2f)), Quaternion.Euler(0f, Rand(rng, 0f, 90f), 0f));

            Solid(parent, "Ruin", build, _adobeTints[rng.Next(_adobeTints.Length)], layer);
        }

        /// <summary>A minaret: a square base, an eight-sided shaft, a balcony, and a cap.</summary>
        private static void BuildMinaret(Transform parent, int layer, Vector3 at)
        {
            var build = new MeshBuild { UVScale = 0.25f };
            build.Box(at + Vector3.up * 4f, new Vector3(6f, 8f, 6f), Quaternion.identity);
            build.Tube(at + Vector3.up * 8f, at + Vector3.up * 21f, 2.1f, 1.8f, 8);
            build.Tube(at + Vector3.up * 17.6f, at + Vector3.up * 18.3f, 2.8f, 2.8f, 8);
            build.Tube(at + Vector3.up * 21f, at + Vector3.up * 24.5f, 1.9f, 0.15f, 8);

            var go = Solid(parent, "Minaret", build, _adobeTints[1], layer);
            if (go != null) Mark(go, new Color(0.72f, 0.64f, 0.48f), 4);
            SealBox(parent, at + Vector3.up * 4f, new Vector3(6f, 8f, 6f));

            var dark = new MeshBuild { UVScale = 0.5f };
            for (int i = 0; i < 4; i++)
            {
                var dir = Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward;
                dark.Box(at + Vector3.up * 19.6f + dir * 1.9f, new Vector3(0.6f, 1.2f, 0.6f), Quaternion.identity);
            }
            Visual(parent, "MinaretWindows", dark, _shadowMat, layer);
        }

        /// <summary>
        /// The market square: awnings of coloured cloth on poles round its edge with stalls
        /// under them, and a well in the middle. The cloth has no collider -- it is overhead --
        /// and is drawn from both sides, since it is seen from underneath as often as not.
        /// </summary>
        private static void BuildMarket(Transform parent, int layer, System.Random rng, Vector3 centre, float halfX, float halfZ)
        {
            var poles = new MeshBuild { UVScale = 0.5f };
            var stalls = new MeshBuild { UVScale = 0.5f };
            var cloths = new[] { new MeshBuild { UVScale = 0.5f }, new MeshBuild { UVScale = 0.5f },
                                 new MeshBuild { UVScale = 0.5f }, new MeshBuild { UVScale = 0.5f } };

            // An awning in each corner of the square -- the four lots left empty to make it --
            // facing the street beside it, so both streets through the middle stay open.
            int count = 0;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    float w = Rand(rng, 5f, 7f), dp = 3.6f;
                    var edge = centre + new Vector3(sx * (halfX - 5.5f), 0f, sz * (halfZ - 4.5f));

                    // Facing the main street (along x), so the back is away from it.
                    var right = Vector3.right;
                    var inward = new Vector3(0f, 0f, -sz);

                    float hBack = 2.9f, hFront = 2.4f;
                    var back = edge - inward * dp * 0.5f;
                    var front = edge + inward * dp * 0.5f;

                    foreach (var corner in new[] { back - right * w * 0.5f, back + right * w * 0.5f })
                        poles.Box(corner + Vector3.up * hBack * 0.5f, new Vector3(0.12f, hBack, 0.12f), Quaternion.identity);
                    foreach (var corner in new[] { front - right * w * 0.5f, front + right * w * 0.5f })
                        poles.Box(corner + Vector3.up * hFront * 0.5f, new Vector3(0.12f, hFront, 0.12f), Quaternion.identity);

                    var cloth = cloths[count % cloths.Length];
                    var a = back - right * w * 0.5f + Vector3.up * hBack;
                    var b = back + right * w * 0.5f + Vector3.up * hBack;
                    var cc = front + right * w * 0.5f + Vector3.up * hFront + inward * 0.5f;
                    var dd = front - right * w * 0.5f + Vector3.up * hFront + inward * 0.5f;
                    AddUp(cloth, a, b, cc, dd);
                    AddDown(cloth, a + Vector3.down * 0.02f, b + Vector3.down * 0.02f, cc + Vector3.down * 0.02f, dd + Vector3.down * 0.02f);

                    // A stall table under it.
                    stalls.Box(back + inward * 0.9f + Vector3.up * 0.45f, new Vector3(w - 1f, 0.9f, 1f), Quaternion.identity);
                    count++;
                }

            Solid(parent, "AwningPoles", poles, _timberMat, layer, "Wood");
            Solid(parent, "Stalls", stalls, _timberMat, layer, "Wood");

            var clothMats = new[] { _clothRed, _clothBlue, _clothSaffron, _clothCream };
            for (int i = 0; i < cloths.Length; i++) Visual(parent, "Awning", cloths[i], clothMats[i], layer);

            // The well, off the crossing of the main street and the lane, where a car can pass it.
            centre += new Vector3(9f, 0f, 10f);
            var well = new MeshBuild { UVScale = 0.4f };
            well.Tube(centre, centre + Vector3.up * 0.9f, 1.4f, 1.4f, 14);
            foreach (float s in new[] { -1.2f, 1.2f })
                well.Box(centre + new Vector3(s, 1.4f, 0f), new Vector3(0.2f, 2.8f, 0.2f), Quaternion.identity);
            well.Box(centre + new Vector3(0f, 2.75f, 0f), new Vector3(2.8f, 0.2f, 0.2f), Quaternion.identity);
            Solid(parent, "Well", well, _rockPaleMat, layer);
        }

        // ==================================================================
        // The oasis, the fields, the camp
        // ==================================================================
        private static void BuildOasis(Transform parent, int layer, int backdrop, System.Random rng)
        {
            var oasis = new GameObject("Oasis").transform;
            oasis.SetParent(parent, false);

            var c = _oasisCentre;
            float bed = GroundHeightAt(c.x, c.y);

            // Grass round the water, draped on the bowl.
            Drape(oasis, "OasisGrass", BlobMesh("OasisGrass", c, 19f, 7301, 0.08f, 1f, 1.15f, Rand(rng, 0f, 3f)), _grassMat, backdrop);

            // The water: shallow, walkable, and not a hazard -- the river is the thing that kills.
            var water = new MeshBuild { UVScale = 0.2f };
            const int Sides = 28;
            for (int i = 0; i < Sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / Sides, a1 = (i + 1) * Mathf.PI * 2f / Sides;
                float r0 = 11.5f * (1f + Fbm2(Mathf.Cos(a0) * 2f, Mathf.Sin(a0) * 2f, 7302, 2) * 0.15f);
                float r1 = 11.5f * (1f + Fbm2(Mathf.Cos(a1) * 2f, Mathf.Sin(a1) * 2f, 7302, 2) * 0.15f);
                var mid = new Vector3(c.x, bed + 0.35f, c.y);
                AddUp(water, mid, new Vector3(c.x + Mathf.Cos(a0) * r0, bed + 0.35f, c.y + Mathf.Sin(a0) * r0),
                      new Vector3(c.x + Mathf.Cos(a1) * r1, bed + 0.35f, c.y + Mathf.Sin(a1) * r1),
                      new Vector3(c.x + Mathf.Cos(a1) * r1, bed + 0.35f, c.y + Mathf.Sin(a1) * r1));
            }
            var pool = MeshObject(oasis, "OasisWater", ToMesh(water, DenseKey("oasiswater")), _waterMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, backdrop, null, collider: false);
            Mark(pool, new Color(0.25f, 0.42f, 0.38f), 2);
            pool.AddComponent<ScrollingWater>().scrollSpeed = new Vector2(0.004f, 0.01f);

            // Reeds at the water's edge, palms round the rim.
            var reeds = new MeshBuild { UVScale = 0.5f };
            for (int i = 0; i < 70; i++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f);
                float r = Rand(rng, 10f, 13.5f);
                var foot = new Vector3(c.x + Mathf.Cos(a) * r, 0f, c.y + Mathf.Sin(a) * r);
                foot.y = SurfaceHeightAt(foot.x, foot.z);
                float h = Rand(rng, 1f, 2.2f);
                var lean = new Vector3(Rand(rng, -0.25f, 0.25f), 1f, Rand(rng, -0.25f, 0.25f)).normalized;
                reeds.Tube(foot, foot + lean * h, 0.03f, 0.01f, 3);
            }
            Visual(oasis, "Reeds", reeds, _foliageMat, backdrop);

            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f + Rand(rng, -0.2f, 0.2f);
                float r = Rand(rng, 14.5f, 19f);
                var at = new Vector2(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r);
                BuildPalm(oasis, layer, rng, new Vector3(at.x, GroundHeightAt(at.x, at.y) - 0.2f, at.y));
            }

            // A nomad camp on the far side from the town.
            var away = (c - _townCentre).normalized;
            for (int i = 0; i < 3; i++)
            {
                var dir = (Vector2)(Quaternion.Euler(0f, 0f, -50f + i * 50f + Rand(rng, -10f, 10f)) * away);
                var at = c + dir * Rand(rng, 25f, 29f);
                if (!Drapeable(at.x, at.y, 4f)) continue;
                BuildTent(oasis, layer, rng, at, Mathf.Atan2(-dir.x, -dir.y) * Mathf.Rad2Deg);
            }
        }

        /// <summary>
        /// A black goat-hair tent: a ridge on three poles, the cloth pitched down to low poles
        /// either side, closed at the back and open at the front, a rug inside. The cloth is
        /// drawn both ways, since you stand under it; it has no collider, the poles do.
        /// </summary>
        private static void BuildTent(Transform parent, int layer, System.Random rng, Vector2 at, float yaw)
        {
            float y = GroundHeightAt(at.x, at.y);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var o = new Vector3(at.x, y, at.y);
            Vector3 P(float x, float h, float z) => o + rot * new Vector3(x, h, z);

            const float w = 7f, d = 4.5f, ridge = 2.4f, eave = 1.2f;

            var poles = new MeshBuild { UVScale = 0.5f };
            foreach (float x in new[] { -w * 0.45f, 0f, w * 0.45f })
                poles.Box(P(x, ridge * 0.5f, 0f), new Vector3(0.12f, ridge, 0.12f), rot);
            foreach (float x in new[] { -w * 0.5f, w * 0.5f })
                foreach (float z in new[] { -d * 0.5f, d * 0.5f })
                    poles.Box(P(x, eave * 0.5f, z), new Vector3(0.1f, eave, 0.1f), rot);
            Solid(parent, "TentPoles", poles, _timberMat, layer, "Wood");

            var cloth = new MeshBuild { UVScale = 0.5f };
            var r0 = P(-w * 0.55f, ridge, 0f); var r1 = P(w * 0.55f, ridge, 0f);
            var f0 = P(-w * 0.55f, eave, d * 0.55f); var f1 = P(w * 0.55f, eave, d * 0.55f);
            var b0 = P(-w * 0.55f, eave, -d * 0.55f); var b1 = P(w * 0.55f, eave, -d * 0.55f);
            AddUp(cloth, r0, r1, f1, f0); AddDown(cloth, r0, r1, f1, f0);
            AddUp(cloth, r1, r0, b0, b1); AddDown(cloth, r1, r0, b0, b1);

            // The back wall, down to the sand.
            var g0 = P(-w * 0.55f, 0f, -d * 0.55f); var g1 = P(w * 0.55f, 0f, -d * 0.55f);
            cloth.Quad(g0, b0, b1, g1); cloth.Quad(g1, b1, b0, g0);
            Visual(parent, "Tent", cloth, _tentMat, layer);

            var rug = new MeshBuild { UVScale = 0.5f };
            rug.Box(P(0f, 0.03f, 0f), new Vector3(3.2f, 0.04f, 2.2f), rot);
            Visual(parent, "Rug", rug, rng.Next(2) == 0 ? _clothRed : _clothBlue, layer);
        }

        /// <summary>Irrigated plots: soil, rows of green crop, a channel of water along one side.</summary>
        private static void BuildFields(Transform parent, int layer, System.Random rng)
        {
            var fields = new GameObject("Fields").transform;
            fields.SetParent(parent, false);

            var c = _fieldCentre;
            float y = GroundHeightAt(c.x, c.y);
            bool alongX = rng.Next(2) == 0;

            var soil = new MeshBuild { UVScale = 0.2f };
            var crop = new MeshBuild { UVScale = 0.5f };
            var channel = new MeshBuild { UVScale = 0.2f };

            for (int plot = -1; plot <= 1; plot++)
            {
                float offset = plot * 12.5f;
                var centre = alongX ? new Vector3(c.x, y + 0.03f, c.y + offset) : new Vector3(c.x + offset, y + 0.03f, c.y);
                var size = alongX ? new Vector3(26f, 0.04f, 11f) : new Vector3(11f, 0.04f, 26f);
                soil.Box(centre, size, Quaternion.identity);

                bool fallow = rng.NextDouble() < 0.25;
                if (fallow) continue;

                for (float r = -4.6f; r <= 4.6f; r += 1.15f)
                {
                    var rowAt = alongX ? centre + new Vector3(0f, 0.18f, r) : centre + new Vector3(r, 0.18f, 0f);
                    crop.Box(rowAt, alongX ? new Vector3(24.5f, 0.32f, 0.45f) : new Vector3(0.45f, 0.32f, 24.5f), Quaternion.identity);
                }
            }

            var ch = alongX ? new Vector3(c.x, y + 0.05f, c.y - 19.5f) : new Vector3(c.x - 19.5f, y + 0.05f, c.y);
            channel.Box(ch, alongX ? new Vector3(27f, 0.04f, 1.2f) : new Vector3(1.2f, 0.04f, 27f), Quaternion.identity);

            Flat(fields, layer, "Soil", soil, DenseKey("soil"), _soilMat);
            Flat(fields, layer, "Crops", crop, DenseKey("crops"), _cropMat);
            Flat(fields, layer, "Channel", channel, DenseKey("channel"), _waterMat);
        }

        // ==================================================================
        // The oil field
        // ==================================================================
        private static void BuildOilField(Transform parent, int layer, System.Random rng)
        {
            if (_oilSites.Count == 0) return;

            var oil = new GameObject("OilField").transform;
            oil.SetParent(parent, false);

            for (int i = 0; i < _oilSites.Count; i++)
            {
                var s = _oilSites[i];
                var at = new Vector3(s.x, GroundHeightAt(s.x, s.y), s.y);

                if (i % 3 == 1) BuildDerrick(oil, layer, at, rng);
                else BuildPumpjack(oil, layer, rng, at);
            }

            BuildPipeline(oil, layer);
        }

        /// <summary>
        /// A pumpjack: the skid, the A-frame samson post, a walking beam that nods on top of it
        /// with the horse head over the well, and the crank and counterweight turning at the
        /// other end in time with it. The moving parts carry no collider.
        /// </summary>
        private static void BuildPumpjack(Transform parent, int layer, System.Random rng, Vector3 at)
        {
            float yaw = rng.Next(4) * 90f;
            var site = new GameObject("Pumpjack").transform;
            site.SetParent(parent, false);
            site.localPosition = at;
            site.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var frame = new MeshBuild { UVScale = 0.4f };
            frame.Box(new Vector3(0f, 0.25f, -0.5f), new Vector3(2.2f, 0.5f, 8f), Quaternion.identity);
            foreach (float s in new[] { -0.8f, 0.8f })
            {
                frame.Box(new Vector3(s, 2.3f, -0.6f), new Vector3(0.22f, 4.4f, 0.22f), Quaternion.Euler(-12f, 0f, 0f));
                frame.Box(new Vector3(s, 2.3f, 0.6f), new Vector3(0.22f, 4.4f, 0.22f), Quaternion.Euler(12f, 0f, 0f));
            }
            frame.Box(new Vector3(0f, 1.2f, -3.6f), new Vector3(1.4f, 1.6f, 1.2f), Quaternion.identity);   // gearbox
            frame.Box(new Vector3(0f, 0.6f, 3.6f), new Vector3(0.7f, 1.2f, 0.7f), Quaternion.identity);    // wellhead
            var frameGo = MeshObject(site, "PumpjackFrame", ToMesh(frame, "pumpjackframe"), _oilSteelMat, Vector3.zero,
                                     Quaternion.identity, Vector3.one, layer, "Metal");
            NoStanding(frameGo);

            float period = Rand(rng, 4f, 6f), phase = Rand(rng, 0f, 6f);

            // The walking beam, pivoting on top of the post.
            var beamBuild = new MeshBuild { UVScale = 0.4f };
            beamBuild.Box(new Vector3(0f, 0f, 0.3f), new Vector3(0.45f, 0.6f, 7.4f), Quaternion.identity);
            beamBuild.Box(new Vector3(0f, -0.4f, 3.9f), new Vector3(0.5f, 1.8f, 0.9f), Quaternion.identity);   // horse head
            var beam = MeshObject(site, "WalkingBeam", ToMesh(beamBuild, "walkingbeam"), _rustMat,
                                  new Vector3(0f, 4.5f, 0f), Quaternion.identity, Vector3.one, layer, null, collider: false);
            var rock = beam.AddComponent<MachineRock>();
            rock.rest = Quaternion.identity;
            rock.axis = Vector3.right;
            rock.amplitude = 16f;
            rock.period = period;
            rock.phase = phase;

            // The crank and its counterweights, turning once a nod.
            var crankBuild = new MeshBuild { UVScale = 0.4f };
            foreach (float s in new[] { -0.85f, 0.85f })
                crankBuild.Box(new Vector3(s, -0.6f, 0f), new Vector3(0.25f, 2.2f, 0.9f), Quaternion.identity);
            crankBuild.Tube(new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f), 0.2f, 0.2f, 8);
            var crank = MeshObject(site, "Crank", ToMesh(crankBuild, "pumpjackcrank"), _rustMat,
                                   new Vector3(0f, 1.9f, -3.6f), Quaternion.identity, Vector3.one, layer, null, collider: false);
            var spin = crank.AddComponent<MachineSpin>();
            spin.rest = Quaternion.identity;
            spin.axis = Vector3.right;
            spin.degreesPerSecond = 360f / period;
            spin.phase = phase * 360f / period;
        }

        /// <summary>An oil derrick: a tapering lattice tower on a drill floor, with a crown on top.</summary>
        private static void BuildDerrick(Transform parent, int layer, Vector3 at, System.Random rng)
        {
            var build = new MeshBuild { UVScale = 0.5f };
            const float height = 24f, baseHalf = 3.2f, topHalf = 0.8f;

            void Strut(Vector3 a, Vector3 b, float t)
            {
                var d = b - a;
                build.Box(at + (a + b) * 0.5f, new Vector3(t, t, d.magnitude), Quaternion.LookRotation(d.normalized, Vector3.up));
            }

            Vector3 Leg(int i, float y)
            {
                float h = Mathf.Lerp(baseHalf, topHalf, y / height);
                float sx = (i == 0 || i == 3) ? -1f : 1f, sz = (i < 2) ? -1f : 1f;
                return new Vector3(sx * h, y + 1.2f, sz * h);
            }

            for (int i = 0; i < 4; i++) Strut(Leg(i, 0f), Leg(i, height), 0.28f);

            for (float y = 0f; y < height; y += 3f)
                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;
                    Strut(Leg(i, y), Leg(j, y), 0.12f);
                    Strut(Leg(i, y), Leg(j, Mathf.Min(height, y + 3f)), 0.08f);
                }

            build.Box(at + new Vector3(0f, 0.6f, 0f), new Vector3(baseHalf * 2f + 2f, 1.2f, baseHalf * 2f + 2f), Quaternion.identity);
            build.Box(at + new Vector3(0f, height + 1.8f, 0f), new Vector3(2.2f, 1.2f, 2.2f), Quaternion.identity);

            Solid(parent, "Derrick", build, _oilSteelMat, layer, "Metal");

            // The doghouse beside it.
            var shed = new MeshBuild { UVScale = 0.3f };
            var shedAt = at + new Vector3(baseHalf + 3.5f, 1.4f, 0f);
            shed.Box(shedAt, new Vector3(3.5f, 2.8f, 5f), Quaternion.identity);
            Solid(parent, "Doghouse", shed, _rustMat, layer, "Metal");
            SealBox(parent, shedAt, new Vector3(3.5f, 2.8f, 5f));
            _ = rng;
        }

        /// <summary>
        /// A pipeline from well to well and off the edge of the map, on supports three and a half
        /// metres up: high enough to walk and drive under, so it is scenery and a landmark to
        /// follow rather than a wall across the far bank.
        /// </summary>
        private static void BuildPipeline(Transform parent, int layer)
        {
            var route = new List<Vector2>();
            foreach (var s in _oilSites) route.Add(s + new Vector2(10f, 0f));
            if (route.Count == 0) return;
            route.Add(new Vector2(route[route.Count - 1].x, _theme.arenaSize * 0.5f + 40f));

            var supports = new MeshBuild { UVScale = 0.5f };
            var pipe = new MeshBuild { UVScale = 0.5f };
            const float lift = 3.6f;

            Vector3 prev = Vector3.zero;
            bool first = true;

            for (int i = 0; i < route.Count - 1; i++)
            {
                float len = Vector2.Distance(route[i], route[i + 1]);
                int n = Mathf.Max(1, Mathf.CeilToInt(len / 9f));
                for (int k = 0; k <= n; k++)
                {
                    if (!first && k == 0) continue;
                    var p = Vector2.Lerp(route[i], route[i + 1], k / (float)n);
                    float g = GroundHeightAt(p.x, p.y);
                    var top = new Vector3(p.x, g + lift, p.y);

                    supports.Box(new Vector3(p.x, g + lift * 0.5f, p.y), new Vector3(0.25f, lift, 0.25f), Quaternion.identity);
                    supports.Box(new Vector3(p.x, g + lift - 0.1f, p.y), new Vector3(1.2f, 0.2f, 0.3f), Quaternion.identity);

                    if (!first) pipe.Tube(prev, top + Vector3.up * 0.4f, 0.35f, 0.35f, 10);
                    prev = top + Vector3.up * 0.4f;
                    first = false;
                }
            }

            Solid(parent, "PipelineSupports", supports, _oilSteelMat, layer, "Metal");
            Visual(parent, "Pipeline", pipe, _rustMat, layer);
        }

        // ==================================================================
        // Wrecks
        // ==================================================================
        private static void BuildWrecks(Transform parent, int layer, int backdrop, System.Random rng)
        {
            var wrecks = new GameObject("Wrecks").transform;
            wrecks.SetParent(parent, false);

            if (_hasPlane) BuildCrashedPlane(wrecks, layer, backdrop, rng, _planeSite);

            // Trucks abandoned beside the track, out on the dunes.
            int trucks = 0;
            for (int i = 0; i < 40 && trucks < 4; i++)
            {
                if (_track.Count < 10) break;
                var p = _track[rng.Next(_track.Count)];
                if (float.IsNaN(p.x) || (p - _townCentre).magnitude < _townRadius + 10f) continue;

                int j = Mathf.Clamp(_track.IndexOf(p), 1, _track.Count - 2);
                var ahead = _track[j + 1] - _track[j - 1];
                if (float.IsNaN(ahead.x)) continue;

                var side = new Vector2(-ahead.y, ahead.x).normalized * (rng.Next(2) == 0 ? -1f : 1f);
                var at = p + side * Rand(rng, 9f, 13f);
                if (!Free(at, 5f) || !Drapeable(at.x, at.y, 4f)) continue;

                Claim(at.x, at.y, 5f);
                BuildTruck(wrecks, layer, rng, at, Mathf.Atan2(ahead.x, ahead.y) * Mathf.Rad2Deg + Rand(rng, -30f, 30f));
                trucks++;
            }
        }

        /// <summary>A burnt-out truck: chassis, cab, bed, wheels, tipped a little into the sand.</summary>
        private static void BuildTruck(Transform parent, int layer, System.Random rng, Vector2 at, float yaw)
        {
            var build = new MeshBuild { UVScale = 0.4f };
            build.Box(new Vector3(0f, 0.9f, 0f), new Vector3(2.2f, 0.4f, 8f), Quaternion.identity);
            build.Box(new Vector3(0f, 1.9f, 2.8f), new Vector3(2.3f, 1.9f, 2f), Quaternion.identity);
            build.Box(new Vector3(0f, 1.5f, -1.3f), new Vector3(2.3f, 0.2f, 5.2f), Quaternion.identity);
            foreach (float s in new[] { -1.1f, 1.1f })
                build.Box(new Vector3(s, 1.9f, -1.3f), new Vector3(0.1f, 0.8f, 5.2f), Quaternion.identity);
            foreach (float z in new[] { 2.8f, -1f, -2.8f })
                foreach (float s in new[] { -1f, 1f })
                    build.Tube(new Vector3(s * 1.05f, 0.5f, z), new Vector3(s * 1.35f, 0.5f, z), 0.5f, 0.5f, 10);

            float y = LowestGroundIn(at.x, at.y, 4.5f) - 0.25f;
            var rot = Quaternion.Euler(Rand(rng, -4f, 4f), yaw, Rand(rng, -9f, 9f));
            var go = MeshObject(parent, "Truck", ToMesh(build, DenseKey("truck")), rng.NextDouble() < 0.5 ? _burntMat : _rustMat,
                                new Vector3(at.x, y, at.y), rot, Vector3.one, layer, "Metal");
            NoStanding(go);
            SealBox(parent, new Vector3(at.x, y + 1.4f, at.y), new Vector3(4f, 2.8f, 4f));
        }

        /// <summary>
        /// A crashed airliner-sized transport, nose down into a dune and broken: the fuselage
        /// sunk into the sand, the tail fin up, one wing torn off and lying apart, an engine
        /// thrown clear, and a black scorch under all of it. The fuselage is a closed shell,
        /// so the sand inside it is sealed off the bake.
        /// </summary>
        private static void BuildCrashedPlane(Transform parent, int layer, int backdrop, System.Random rng, Vector2 site)
        {
            float yaw = Rand(rng, 0f, 360f);
            var rot = Quaternion.Euler(Rand(rng, 5f, 9f), yaw, Rand(rng, 8f, 16f));
            // Ploughed in to about a third of its depth: the fuselage is two metres across.
            float ground = GroundHeightAt(site.x, site.y);
            var at = new Vector3(site.x, ground + 0.7f, site.y);

            var body = new MeshBuild { UVScale = 0.25f };
            body.Tube(new Vector3(0f, 0f, -12f), new Vector3(0f, 0f, 9f), 2f, 2f, 14);
            body.Tube(new Vector3(0f, 0f, 9f), new Vector3(0f, -0.3f, 13f), 2f, 0.4f, 14);
            body.Tube(new Vector3(0f, 0.2f, -17f), new Vector3(0f, 0f, -12f), 0.6f, 2f, 14);
            body.Box(new Vector3(0f, 3.4f, -15.5f), new Vector3(0.35f, 5f, 3.4f), Quaternion.Euler(-18f, 0f, 0f));
            body.Box(new Vector3(0f, 0.6f, -15.8f), new Vector3(8f, 0.25f, 2f), Quaternion.identity);
            body.Box(new Vector3(-5f, -0.8f, 0f), new Vector3(8f, 0.35f, 3.6f), Quaternion.Euler(0f, 0f, 6f));   // the wing still on
            var go = MeshObject(parent, "PlaneFuselage", ToMesh(body, DenseKey("plane")), _alloyMat, at, rot, Vector3.one, layer, "Metal");
            NoStanding(go);
            if (go != null) Mark(go, new Color(0.62f, 0.63f, 0.64f), 4);
            SealBox(parent, at, new Vector3(9f, 5f, 9f));
            SealBox(parent, at + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, -8f), new Vector3(8f, 5f, 8f));
            SealBox(parent, at + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, 8f), new Vector3(8f, 5f, 8f));

            // The other wing, torn off and lying flat on the sand.
            var wingAt = site + (Vector2)(Quaternion.Euler(0f, 0f, -yaw) * new Vector2(12f, 4f));
            var wing = new MeshBuild { UVScale = 0.25f };
            wing.Box(Vector3.zero, new Vector3(11f, 0.35f, 3.6f), Quaternion.identity);
            wing.Tube(new Vector3(-2f, -0.9f, -1.5f), new Vector3(-2f, -0.9f, 2.2f), 0.8f, 0.7f, 10);
            float wy = GroundHeightAt(wingAt.x, wingAt.y);
            var wingGo = MeshObject(parent, "PlaneWing", ToMesh(wing, DenseKey("planewing")), _alloyMat,
                                    new Vector3(wingAt.x, wy + 0.5f, wingAt.y),
                                    GroundAlignedRotation(wingAt.x, wingAt.y, yaw + Rand(rng, 20f, 60f)) * Quaternion.Euler(0f, 0f, Rand(rng, 4f, 10f)),
                                    Vector3.one, layer, "Metal");
            NoStanding(wingGo);

            // Scorch under the wreck, and debris round it.
            Drape(parent, "Scorch", BlobMesh("Scorch", site, 16f, 7401, 0.05f, 1f, 1.6f, yaw * Mathf.Deg2Rad), _burntMat, backdrop);

            var debris = new MeshBuild { UVScale = 0.5f };
            for (int i = 0; i < 26; i++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f), r = Rand(rng, 6f, 22f);
                var p = site + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                debris.Box(new Vector3(p.x, SurfaceHeightAt(p.x, p.y) + 0.1f, p.y),
                           new Vector3(Rand(rng, 0.3f, 1.4f), Rand(rng, 0.05f, 0.3f), Rand(rng, 0.3f, 1.2f)),
                           Quaternion.Euler(Rand(rng, -20f, 20f), Rand(rng, 0f, 360f), Rand(rng, -20f, 20f)));
            }
            Visual(parent, "Debris", debris, _alloyMat, backdrop);
        }

        // ==================================================================
        // Filling the empty ground
        // ==================================================================
        /// <summary>
        /// The last pass: finds the long stretches of open sand nothing else claimed and puts
        /// something in them -- groves of acacia in most, and in the biggest a fighting position
        /// under a camouflage net. Ishaan, looking at the desert after the town went in: "fill
        /// some very long unused area with some trees or a killing net space".
        ///
        /// Found by scanning the claims rather than chosen up front, because "empty" means
        /// empty after everything else has had its turn.
        /// </summary>
        private static void BuildDesertFill(Transform root, int layer, float half)
        {
            if (!DesertLife) return;

            var rng = new System.Random(_theme.randomSeed * 9973 + 5);
            var group = new GameObject("Fill").transform;
            group.SetParent(root, false);

            var spots = new List<Vector2>();
            for (float x = -half + 30f; x <= half - 30f; x += 14f)
                for (float z = -half + 30f; z <= half - 30f; z += 14f)
                {
                    var p = new Vector2(x + Rand(rng, -4f, 4f), z + Rand(rng, -4f, 4f));
                    if (p.magnitude < 40f || !Drapeable(p.x, p.y, 12f)) continue;
                    if (!OpenSand(p, 15f)) continue;
                    spots.Add(p);
                }

            for (int i = spots.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (spots[i], spots[j]) = (spots[j], spots[i]); }

            int nets = 0, groves = 0;
            var taken = new List<Vector2>();
            bool Near(Vector2 p, float r) { foreach (var t in taken) if ((t - p).magnitude < r) return true; return false; }

            foreach (var p in spots)
            {
                if (Near(p, 30f)) continue;   // an earlier one took it

                if (nets < 6 && OpenSand(p, 20f))
                {
                    NetPosition(group, layer, rng, p);
                    nets++;
                }
                else if (groves < 22)
                {
                    AcaciaGrove(group, layer, rng, p);
                    groves++;
                }
                else continue;

                taken.Add(p);
            }

            Debug.Log($"[FPSKit] desert fill: {groves} grove(s), {nets} net position(s) in {spots.Count} empty spot(s).");
        }

        /// <summary>
        /// Whether a circle of desert is bare: nothing but sand in it from knee height up, and
        /// clear of the track. Asked of the physics scene rather than of the claim circles,
        /// which by the end of a build overlap across nearly the whole map -- the first run of
        /// this found no empty ground at all on a map that is mostly empty ground.
        /// </summary>
        private static bool OpenSand(Vector2 p, float radius)
        {
            foreach (var t in _track)
                if (!float.IsNaN(t.x) && (t - p).magnitude < radius * 0.6f + 5f) return false;

            Physics.SyncTransforms();
            float g = GroundHeightAt(p.x, p.y);
            var hits = Physics.OverlapCapsule(new Vector3(p.x, g + 0.6f + radius, p.y), new Vector3(p.x, g + 30f, p.y),
                                              radius, ~0, QueryTriggerInteraction.Ignore);

            foreach (var hit in hits)
                if (!hit.CompareTag(SandTag)) return false;

            return true;
        }

        /// <summary>
        /// A handful of acacias: a short leaning trunk forking into two or three limbs, under a
        /// flat umbrella of canopy. The canopy is off the bake -- a flat crown is exactly what
        /// a NavMeshSurface bakes as an island in the sky -- and carries no collider; the
        /// trunk does.
        /// </summary>
        private static void AcaciaGrove(Transform parent, int layer, System.Random rng, Vector2 centre)
        {
            _acaciaMat ??= MakeMaterial("Acacia", new Color(0.30f, 0.34f, 0.15f), 0.08f, 0f);

            int trees = 4 + rng.Next(6);
            var trunks = new MeshBuild { UVScale = 0.5f };

            for (int i = 0; i < trees; i++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f), r = Mathf.Sqrt((float)rng.NextDouble()) * 13f;
                var p = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                var foot = new Vector3(p.x, GroundHeightAt(p.x, p.y) - 0.3f, p.y);

                float h = Rand(rng, 2.6f, 4f);
                var lean = new Vector3(Rand(rng, -0.25f, 0.25f), 1f, Rand(rng, -0.25f, 0.25f)).normalized;
                var fork = foot + lean * h;
                trunks.Tube(foot, fork, 0.24f, 0.17f, 7);

                float crown = h + Rand(rng, 1.2f, 2f);
                float spread = Rand(rng, 3.2f, 5f);
                int limbs = 2 + rng.Next(2);
                for (int k = 0; k < limbs; k++)
                {
                    float la = Rand(rng, 0f, Mathf.PI * 2f);
                    var tip = new Vector3(foot.x + Mathf.Cos(la) * spread * 0.45f, foot.y + crown, foot.z + Mathf.Sin(la) * spread * 0.45f);
                    trunks.Tube(fork, tip, 0.14f, 0.07f, 6);
                }

                var canopy = MeshObject(parent, "AcaciaCanopy", BoulderMesh(1 + rng.Next(20), 0.3f, 0.6f), _acaciaMat,
                                        new Vector3(foot.x, foot.y + crown + 0.35f, foot.z),
                                        Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f),
                                        new Vector3(spread * 0.5f, 0.32f, spread * 0.5f * Rand(rng, 0.8f, 1.2f)),
                                        layer, null, collider: false);
                NoStanding(canopy);
                Hide(canopy);
            }

            MeshObject(parent, "AcaciaTrunks", ToMesh(trunks, DenseKey("acacia")), _timberMat, Vector3.zero,
                       Quaternion.identity, Vector3.one, layer, "Wood");

            // Dry grass under them, where the shade is.
            Drape(parent, "AcaciaShade", BlobMesh("AcaciaShade", centre, 15f, 7501 + rng.Next(100), 0.06f, 1f,
                                                  Rand(rng, 1f, 1.6f), Rand(rng, 0f, 3f)), _grassMat, LayerMask.NameToLayer("Backdrop"));
        }

        /// <summary>
        /// A fighting position under a camouflage net: a ring of sandbags, chest high, broken
        /// by two gaps on opposite sides so it is never a pen; a net on poles over it, sagging;
        /// and ammunition boxes inside. A place to hold -- and, from outside, a place that
        /// somebody is holding.
        ///
        /// The sandbags follow the dune, one course at a time; the net has no collider (it is
        /// above head height) and is drawn from both sides.
        /// </summary>
        private static void NetPosition(Transform parent, int layer, System.Random rng, Vector2 centre)
        {
            _netMat ??= MakeMaterial("CamoNet", new Color(0.36f, 0.34f, 0.22f), 0.04f, 0f);
            _sandbagMat ??= MakeDetailMaterial("Sandbag", new Color(0.62f, 0.54f, 0.38f), "Timber", 0.8f, 0.05f, 0f, 1f);

            const float ring = 6f;
            float gapA = Rand(rng, 0f, Mathf.PI * 2f), gapB = gapA + Mathf.PI;

            var bags = new MeshBuild { UVScale = 0.5f };
            int segments = 26;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, gapA * Mathf.Rad2Deg)) < 20f) continue;
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, gapB * Mathf.Rad2Deg)) < 20f) continue;

                var p = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ring;
                float g = GroundHeightAt(p.x, p.y);
                var rot = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                for (int course = 0; course < 3; course++)
                    bags.Box(new Vector3(p.x, g + 0.18f + course * 0.34f, p.y), new Vector3(0.7f, 0.34f, 1.55f - course * 0.12f), rot);
            }
            NoStanding(MeshObject(parent, "Sandbags", ToMesh(bags, DenseKey("sandbags")), _sandbagMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Concrete"));

            // Poles, and the net on them: a sagging grid, highest over the poles.
            var poles = new MeshBuild { UVScale = 0.5f };
            var tops = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                float a = gapA + Mathf.PI * 0.25f + i * Mathf.PI * 0.5f;
                var p = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (ring - 1.3f);
                float g = GroundHeightAt(p.x, p.y);
                tops[i] = new Vector3(p.x, g + 3f, p.y);
                poles.Box(new Vector3(p.x, g + 1.5f, p.y), new Vector3(0.12f, 3f, 0.12f), Quaternion.identity);
            }
            MeshObject(parent, "NetPoles", ToMesh(poles, DenseKey("netpoles")), _timberMat, Vector3.zero,
                       Quaternion.identity, Vector3.one, layer, "Wood");

            float floor = GroundHeightAt(centre.x, centre.y);
            const int N = 9;
            var net = new MeshBuild { UVScale = 0.6f };
            Vector3 NetAt(int i, int j)
            {
                float u = i / (float)(N - 1) * 1.5f - 0.25f, v = j / (float)(N - 1) * 1.5f - 0.25f;
                // Bilinear across the four pole tops, stretched past them, sagging between.
                var a = Vector3.LerpUnclamped(tops[0], tops[1], u);
                var b = Vector3.LerpUnclamped(tops[3], tops[2], u);
                var p = Vector3.LerpUnclamped(a, b, v);
                float sag = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI) * Mathf.Sin(Mathf.Clamp01(v) * Mathf.PI) * 0.5f;
                float edge = Mathf.Max(Mathf.Max(-u, u - 1f), Mathf.Max(-v, v - 1f));
                p.y -= sag + Mathf.Max(0f, edge) * 3.5f;
                p.y = Mathf.Max(p.y, floor + 0.6f);
                p.y += Fbm2(i * 0.7f, j * 0.7f, 7601, 2) * 0.25f;
                return p;
            }
            for (int i = 0; i < N - 1; i++)
                for (int j = 0; j < N - 1; j++)
                {
                    AddUp(net, NetAt(i, j), NetAt(i + 1, j), NetAt(i + 1, j + 1), NetAt(i, j + 1));
                    AddDown(net, NetAt(i, j), NetAt(i + 1, j), NetAt(i + 1, j + 1), NetAt(i, j + 1));
                }
            var netGo = MeshObject(parent, "CamoNet", ToMesh(net, DenseKey("camonet")), _netMat, Vector3.zero,
                                   Quaternion.identity, Vector3.one, layer, null, collider: false);
            NoStanding(netGo);
            Hide(netGo);

            // Ammunition boxes, as cover inside.
            var boxes = new MeshBuild { UVScale = 0.5f };
            for (int i = 0; i < 4; i++)
            {
                var p = centre + new Vector2(Rand(rng, -2.5f, 2.5f), Rand(rng, -2.5f, 2.5f));
                boxes.Box(new Vector3(p.x, GroundHeightAt(p.x, p.y) + 0.35f, p.y), new Vector3(1.1f, 0.7f, 0.6f),
                          Quaternion.Euler(0f, Rand(rng, 0f, 90f), 0f));
            }
            NoStanding(MeshObject(parent, "AmmoBoxes", ToMesh(boxes, DenseKey("ammoboxes")), _netMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Wood"));
        }

        private static Material _acaciaMat, _netMat, _sandbagMat;

        // ==================================================================
        // Heat and dust
        // ==================================================================
        /// <summary>
        /// Dust blowing past at ankle height round the player, the ash component retinted, and
        /// dust devils: tall twisting columns out on the dunes, each wandering slowly back and
        /// forth. Particles only, so nothing here touches navigation or collision.
        /// </summary>
        private static void BuildDust(Transform parent, System.Random rng, float half)
        {
            var dust = new GameObject("BlowingDust");
            dust.transform.SetParent(parent, false);
            var fall = dust.AddComponent<Snowfall>();
            fall.flakeMaterial = FlakeMaterial();
            fall.flakes = 420;
            fall.ceiling = 3f;
            fall.spread = 34f;
            fall.fallSpeed = 0.25f;
            fall.wind = new Vector3(6.5f, 0f, 2.2f);
            fall.tint = new Color(0.86f, 0.76f, 0.58f, 0.55f);
            fall.tintAlt = new Color(0.72f, 0.62f, 0.46f, 0.4f);
            fall.size = new Vector2(0.04f, 0.1f);

            for (int i = 0, made = 0; i < 40 && made < 5; i++)
            {
                var p = new Vector2(Rand(rng, -half * 0.85f, half * 0.85f), Rand(rng, -half * 0.85f, half * 0.85f));
                if (p.magnitude < 70f || !Drapeable(p.x, p.y, 10f)) continue;
                DustDevil(parent, rng, new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y));
                made++;
            }
        }

        private static void DustDevil(Transform parent, System.Random rng, Vector3 at)
        {
            var go = new GameObject("DustDevil");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = true;
            main.duration = 6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 140;
            system.useAutoRandomSeed = false;
            system.randomSeed = (uint)rng.Next();

            var emission = system.emission;
            emission.rateOverTime = 28f;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = 1.2f;

            // The twist: particles orbit the column's axis as they climb.
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(2.4f, 3.6f);
            velocity.radial = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));

            var colour = system.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.84f, 0.72f, 0.52f), 0f), new GradientColorKey(new Color(0.78f, 0.68f, 0.52f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.5f, 0.15f), new GradientAlphaKey(0.3f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = _dustMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Wandering: a slow drift across the dunes and back.
            var travel = go.AddComponent<MachineTravel>();
            travel.origin = at;
            float a = Rand(rng, 0f, Mathf.PI * 2f);
            travel.travel = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Rand(rng, 25f, 50f);
            travel.speed = Rand(rng, 2f, 3.5f);
            travel.pause = 0f;
            travel.phase = Rand(rng, 0f, 40f);
        }
    }
}
#endif
