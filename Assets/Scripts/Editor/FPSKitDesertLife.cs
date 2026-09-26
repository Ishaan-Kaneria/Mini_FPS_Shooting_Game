#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// What people left on the desert: the village, the wrecks of trucks and a plane, a
    /// dirt track joining it all up to the base -- and the heat and dust over it. The base,
    /// the village's houses and the open desert's detail are in FPSKitDesertFull,
    /// FPSKitDesertVillage and FPSKitDesertWild.
    ///
    /// History: Ishaan's first pick for the desert (2026-09-23) was a ruined town, an
    /// oasis, an oil field and heat and dust. Asked to make it "full" and "real"
    /// (2026-09-26), he chose a base, a real village and wild detail, and dropped the oasis;
    /// the oil field went with it to make room for the base.
    ///
    /// <b>Planned before the ground, built after it.</b> The village, the base and every
    /// farm are flattened sites, and a site has to be in the pad list before the dune field
    /// is generated or the ground under it is whatever the dunes were doing (see
    /// FlattenPad). So <see cref="PlanDesertLife"/> runs with the other planning passes and
    /// <see cref="BuildDesertLife"/> with the building ones, and the second reads what the
    /// first chose.
    ///
    /// <b>The village is laid out on a grid of lanes, and that is a navigation decision.</b>
    /// Houses packed at random close courtyards nothing can reach -- the works yard found that
    /// the expensive way. On a grid every lane runs through, so every door opens onto ground
    /// that leads out of the village.
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

        private static Vector2 _townCentre, _planeSite;
        private static float _townRadius;
        private static bool _hasTown, _hasPlane;
        private static readonly List<Vector2> _track = new List<Vector2>();

        private static Material _earthMat, _clothRed, _clothBlue, _clothSaffron, _clothCream, _tentMat,
                                _grassMat, _alloyMat, _burntMat, _rustMat, _trackMat,
                                _shadowMat, _dustMat;
        private static Material[] _adobeTints;

        /// <summary>Cleared with the terrain, for every arena: see ResetTerrain.</summary>
        private static void ResetDesertLife()
        {
            _hasTown = _hasPlane = false;
            _track.Clear();
            _wadis.Clear();
            ResetDesertFull();
        }

        private static void ResolveDesertLifeMaterials()
        {
            // Made on first use by the fill; cleared here so a batch of arenas does not hand
            // one theme's materials to the next.
            _acaciaMat = _netMat = _sandbagMat = null;

            _earthMat = MakeDetailMaterial("PackedEarth", new Color(0.55f, 0.45f, 0.32f), "Adobe", 0.2f, 0.06f, 0f, 0.8f);
            _trackMat = MakeDetailMaterial("Track", new Color(0.50f, 0.41f, 0.29f), "Sand", 0.12f, 0.05f, 0f, 0.7f);
            _grassMat = MakeDetailMaterial("OasisGrass", new Color(0.30f, 0.40f, 0.16f), "Sand", 0.3f, 0.12f, 0f, 0.6f);

            _adobeTints = new[]
            {
                _adobeMat,
                MakeDetailMaterial("AdobePale", Shade(_theme.wallColor, 1.12f), "Adobe", 0.28f, _theme.wallSmoothness, 0f, 0.9f),
                MakeDetailMaterial("AdobeWarm", new Color(0.72f, 0.54f, 0.38f), "Adobe", 0.28f, _theme.wallSmoothness, 0f, 0.9f),
                MakeDetailMaterial("AdobeRed", new Color(0.72f, 0.52f, 0.39f), "Adobe", 0.28f, _theme.wallSmoothness, 0f, 0.9f)
            };

            _clothRed = MakeMaterial("ClothRed", new Color(0.62f, 0.16f, 0.12f), 0.05f, 0f);
            _clothBlue = MakeMaterial("ClothBlue", new Color(0.16f, 0.24f, 0.46f), 0.05f, 0f);
            _clothSaffron = MakeMaterial("ClothSaffron", new Color(0.82f, 0.56f, 0.14f), 0.05f, 0f);
            _clothCream = MakeMaterial("ClothCream", new Color(0.82f, 0.76f, 0.62f), 0.05f, 0f);
            _tentMat = MakeMaterial("TentHair", new Color(0.20f, 0.16f, 0.12f), 0.04f, 0f);
            _shadowMat = MakeMaterial("DoorShadow", new Color(0.10f, 0.08f, 0.07f), 0.05f, 0f);

            // Matte, both of them: there is no baked reflection here either, and anything glossy
            // reflects Unity's grey-blue default rather than this sky (see Matte).
            _alloyMat = MakeMaterial("Alloy", new Color(0.62f, 0.63f, 0.64f), 0.22f, 0.25f);
            _burntMat = MakeMaterial("Burnt", new Color(0.14f, 0.11f, 0.09f), 0.08f, 0f);
            _rustMat = MakeMaterial("WreckRust", new Color(0.40f, 0.23f, 0.13f), 0.1f, 0.1f);

            _dustMat = ParticleMaterial("Dust", "Puff", additive: false, new Color(0.86f, 0.74f, 0.55f));

            ResolveDesertFullMaterials();
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

            // ---- the village: the spawn side of the river, a walk from the spawn ----
            _townRadius = 72f;
            for (int i = 0; i < 160 && !_hasTown; i++)
            {
                var p = new Vector2(Rand(rng, -half + 70f, 20f), Rand(rng, -half * 0.5f, half * 0.5f));
                if (p.magnitude < _townRadius + 30f || !ClearOfRiver(p, _townRadius) || !Inside(p, _townRadius)) continue;
                if (!Free(p, _townRadius + 8f)) continue;

                _townCentre = p;
                _hasTown = true;
                Claim(p.x, p.y, _townRadius + 10f);
                FlattenPad(p.x, p.y, _townRadius, 34f);
                _anchors.Add(new Vector3(p.x, 0f, p.y));
            }

            if (!_hasTown) { Debug.LogWarning("[FPSKit] desert: no room for the village."); return; }

            // ---- the base, away from the village, gate to the middle of the map ----
            PlanFob(rng, half, rim);

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

            PlanTrack(rng, half, rim);
            PlanDesertWild(rng, half, rim);
        }

        /// <summary>
        /// The dirt track: in from the west edge, down the village's main street, over the
        /// nearer bridge and along the far bank; and a branch from the village out to the
        /// base's main gate. Claimed along its length as it is planned, so nothing placed
        /// afterwards lands on it. A car will want it.
        /// </summary>
        private static void PlanTrack(System.Random rng, float half, float rim)
        {
            var runs = new List<List<Vector2>>();

            var west = new List<Vector2>
            {
                new Vector2(-half + 14f, _townCentre.y + Rand(rng, -25f, 25f)),
                new Vector2(_townCentre.x - _townRadius - 12f, _townCentre.y),
                new Vector2(_townCentre.x - _townRadius * 0.5f, _townCentre.y),
                new Vector2(_townCentre.x + _townRadius * 0.5f, _townCentre.y),
                new Vector2(_townCentre.x + _townRadius + 12f, _townCentre.y)
            };
            runs.Add(west);

            // The bridge nearest the village, both of its landings; the span itself is the
            // bridge, not track, so the far side is a run of its own.
            if (_crossings.Count > 0)
            {
                Vector3 bridge = _crossings[0];
                float best = float.MaxValue;
                foreach (var c in _crossings)
                {
                    float d = Mathf.Abs(c.z - _townCentre.y);
                    if (d < best) { best = d; bridge = c; }
                }

                float deck = _theme.hazardWidth + BridgeOverhang * 2f;
                west.Add(new Vector2(bridge.x - deck * 0.5f - 22f, bridge.z));
                west.Add(new Vector2(bridge.x - deck * 0.5f + 2f, bridge.z));

                var east = new List<Vector2>
                {
                    new Vector2(bridge.x + deck * 0.5f - 2f, bridge.z),
                    new Vector2(bridge.x + deck * 0.5f + 22f, bridge.z)
                };
                float dir = bridge.z <= 0f ? 1f : -1f;
                for (int k = 1; k <= 6; k++)
                {
                    float z = bridge.z + dir * 42f * k;
                    if (Mathf.Abs(z) > half - 16f) { z = dir * (half - 14f); }
                    float x = Mathf.Min(GorgeCentreAt(z) + rim + 20f, half - 16f);
                    east.Add(new Vector2(x, z));
                    if (Mathf.Abs(z) >= half - 14f) break;
                }
                runs.Add(east);
            }

            // The branch to the base: from the edge of the village nearest it to the gate.
            if (_hasFob)
            {
                var toFob = (FobGate(30f) - _townCentre).normalized;
                runs.Add(new List<Vector2>
                {
                    _townCentre + toFob * (_townRadius - 2f),
                    _townCentre + toFob * (_townRadius + 12f),
                    FobGate(30f),
                    FobGate(12f),
                    FobGate(-8f)
                });
            }

            // Smoothed through the points: a track bends, it does not corner. A NaN between
            // runs tells the drape to leave a gap.
            foreach (var points in runs)
            {
                if (_track.Count > 0) _track.Add(new Vector2(float.NaN, float.NaN));

                for (int i = 0; i < points.Count - 1; i++)
                {
                    var p0 = points[Mathf.Max(0, i - 1)];
                    var p1 = points[i];
                    var p2 = points[i + 1];
                    var p3 = points[Mathf.Min(points.Count - 1, i + 2)];
                    int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 3f));

                    for (int s = 0; s < steps; s++)
                    {
                        float t = s / (float)steps;
                        var q = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t
                                        + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
                        _track.Add(q);

                        bool inBase = _hasFob && _fob.Contains(q, 0f);
                        if (_track.Count % 3 == 0 && (q - _townCentre).magnitude > _townRadius && !inBase)
                            Claim(q.x, q.y, 5f);
                    }
                }

                _track.Add(points[points.Count - 1]);
            }
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
            BuildVillage(group, layer, backdrop, rng);
            if (_hasFob) BuildFob(group, layer, backdrop, rng);
            BuildDesertWild(group, layer, backdrop, rng, half);
            BuildWrecks(group, layer, backdrop, rng);
            BuildDust(group, rng, half);

            // Where the dashboard card is taken from: outside the town on the side away from
            // the river, at minaret height, looking across the rooftops and the oasis towards
            // the water. See FPSKitMenuBuilder.Capture.
            var river = new Vector2(GorgeCentreAt(_townCentre.y), _townCentre.y);
            var toRiver = (river - _townCentre).normalized;
            var from = _townCentre - toRiver * (_townRadius + 22f) + new Vector2(-toRiver.y, toRiver.x) * 18f;
            var look = _townCentre + toRiver * 30f;
            var anchor = new GameObject("PreviewAnchor").transform;
            anchor.SetParent(group, false);
            anchor.position = new Vector3(from.x, GroundHeightAt(from.x, from.y) + 16f, from.y);
            anchor.rotation = Quaternion.LookRotation(new Vector3(look.x, GroundHeightAt(_townCentre.x, _townCentre.y) + 2f, look.y) - anchor.position);

            Debug.Log($"[FPSKit] desert: village at {_townCentre}, base {_hasFob}, plane {_hasPlane}, track {_track.Count} points.");
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
                    // Not in the village, the base, a farm or a camp: open sand only.
                    if (InSite(p, 16f)) continue;
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

            // The very last thing: plants and junk over whatever sand is still bare.
            BuildScrub(root, layer, half);
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

            for (int i = 0, made = 0; i < 120 && made < 5; i++)
            {
                var p = new Vector2(Rand(rng, -half * 0.85f, half * 0.85f), Rand(rng, -half * 0.85f, half * 0.85f));
                if (p.magnitude < 70f || !Drapeable(p.x, p.y, 10f)) continue;
                // A devil wanders up to fifty metres: kept that far off the places, where a
                // column of orange dust drifting between the tents reads as a fire.
                if (InSite(p, 55f)) continue;
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
