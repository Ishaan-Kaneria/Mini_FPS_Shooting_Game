#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Snowbound made full (2026-10-04), after the desert's rework. Ishaan: "rebuild it like
    /// what you did on Desert". Asked what should fill it, he chose all four of an alpine
    /// village, a research outpost, wild arctic detail and a mountain pass with cabins; dense
    /// like the desert; and to keep the pines, the crevasses and ice caves, the igloo camps
    /// and the frozen lake.
    ///
    /// This file is the shared kit (materials, roofs, trim, the planning pass) and the
    /// village. FPSKitSnowBase.cs is the outpost and the lodge; FPSKitSnowPass.cs the ski
    /// lift, the mine, the pass, the cabins, the camps and the wild detail on open snow.
    ///
    /// Same rules as the desert's: sites are planned before the ground exists (so their pads
    /// are in it) and built after; everything is laid out in a <see cref="Frame"/> turned by a
    /// multiple of ninety degrees; nothing here borrows the desert's materials, which are
    /// only resolved for the desert (a borrowed one is null here, and null is magenta).
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private const float AlpStorey = 18 * StairRise;   // a whole number of risers, for the stair
        private const float AlpVillageR = 66f;

        private static bool _hasAlpVillage, _hasOutpost, _hasLodge, _hasPass, _hasMine, _hasLift;
        private static Vector2 _alpCentre;
        private static SitePlan _outpost, _lodge, _pass, _mine, _liftBottom, _liftTop;
        private static readonly List<SitePlan> _alpCabins = new List<SitePlan>();
        private static readonly List<SitePlan> _alpCamps = new List<SitePlan>();
        private static readonly List<Vector2> _liftPylons = new List<Vector2>();

        private static Material _logMat, _logDarkMat, _plasterMat, _slateMat, _shingleMat, _snowStoneMat, _ironMat,
                                _alpCanvas, _mountainRock, _plankMat, _steelPanelMat, _dullGlassMat;
        private static Material[] _chaletPaints, _tentPaints, _roofPaints;

        private static void ResetSnowFull()
        {
            _hasAlpVillage = _hasOutpost = _hasLodge = _hasPass = _hasMine = _hasLift = false;
            _alpCabins.Clear();
            _alpCamps.Clear();
            _liftPylons.Clear();
        }

        private static void ResolveSnowFullMaterials()
        {
            _logMat = MakeMaterial("AlpLog", new Color(0.38f, 0.25f, 0.15f), 0.08f, 0f);
            _logDarkMat = MakeMaterial("AlpLogDark", new Color(0.22f, 0.15f, 0.10f), 0.08f, 0f);
            _plasterMat = MakeMaterial("AlpPlaster", new Color(0.84f, 0.81f, 0.74f), 0.06f, 0f);
            _slateMat = MakeMaterial("AlpSlate", new Color(0.20f, 0.22f, 0.25f), 0.2f, 0f);
            _shingleMat = MakeMaterial("AlpShingle", new Color(0.42f, 0.21f, 0.14f), 0.1f, 0f);
            _snowStoneMat = MakeMaterial("AlpStone", new Color(0.44f, 0.44f, 0.46f), 0.05f, 0f);
            _ironMat = MakeMaterial("AlpIron", new Color(0.17f, 0.18f, 0.20f), 0.35f, 0.5f);
            _alpCanvas = MakeMaterial("AlpCanvas", new Color(0.62f, 0.52f, 0.30f), 0.05f, 0f);
            _mountainRock = MakeMaterial("MountainRock", new Color(0.34f, 0.35f, 0.38f), 0.08f, 0f);
            _plankMat = MakeMaterial("AlpPlank", new Color(0.50f, 0.38f, 0.24f), 0.1f, 0f);
            _steelPanelMat = MakeMaterial("AlpSteelPanel", new Color(0.36f, 0.42f, 0.38f), 0.3f, 0.3f);
            _dullGlassMat = MakeMaterial("AlpGlass", new Color(0.09f, 0.11f, 0.13f), 0.5f, 0.1f);

            _chaletPaints = new[]
            {
                _logMat, _logMat, _logDarkMat,
                MakeMaterial("AlpOchre", new Color(0.66f, 0.52f, 0.28f), 0.06f, 0f),
                MakeMaterial("AlpBarnRed", new Color(0.46f, 0.17f, 0.12f), 0.06f, 0f),
                MakeMaterial("AlpSlateBlue", new Color(0.30f, 0.40f, 0.50f), 0.06f, 0f),
                _plasterMat
            };
            _roofPaints = new[] { _slateMat, _shingleMat, _slateMat, _logDarkMat };
            _tentPaints = new[]
            {
                MakeMaterial("TentOrange", new Color(0.86f, 0.42f, 0.10f), 0.05f, 0f),
                MakeMaterial("TentGreen", new Color(0.20f, 0.34f, 0.24f), 0.05f, 0f),
                _alpCanvas,
                MakeMaterial("TentBlue", new Color(0.20f, 0.34f, 0.56f), 0.05f, 0f)
            };
        }

        // ==================================================================
        // Planning
        // ==================================================================
        /// <summary>
        /// A site of the given size somewhere clear, claimed and levelled. The pines keep out of
        /// it too (<see cref="_snowSites"/>). The levelling height is held within a gentle slope
        /// of every neighbouring pad (SiteHeight), or two sites a few metres apart become two
        /// shelves with a cliff between them.
        /// </summary>
        private static bool SnowSite(System.Random rng, float half, float w, float d, int tries,
                                     System.Func<Vector2, bool> where, out SitePlan plan,
                                     float blend = 18f, float gap = 5f, bool flatten = true, float spawnClear = 50f)
        {
            plan = default;
            float circum = Mathf.Sqrt(w * w + d * d) * 0.5f;
            float limit = half - 34f - circum;
            for (int i = 0; i < tries; i++)
            {
                var p = new Vector2(Rand(rng, -limit, limit), Rand(rng, -limit, limit));
                if (p.magnitude < spawnClear + circum) continue;
                if (where != null && !where(p)) continue;
                if (!Free(p, circum + gap)) continue;

                plan = new SitePlan { Centre = p, Width = w, Depth = d, Yaw = rng.Next(4) * 90f };
                Claim(p.x, p.y, circum + gap);
                if (flatten) FlattenPad(p.x, p.y, circum, blend, SiteHeight(p, circum));
                _anchors.Add(new Vector3(p.x, 0f, p.y));
                _snowSites.Add(new Vector3(p.x, p.y, circum + 7f));
                return true;
            }
            return false;
        }

        /// <summary>
        /// The big places first: the village, the outpost, the lodge with its lift, the mine, the
        /// pass; then the small ones. Runs before the station, the crevasses and the caves, which
        /// then find their own room round it.
        /// </summary>
        private static void PlanSnowFull(float half)
        {
            ResetSnowFull();
            if (!_theme.snowZone) return;

            var rng = new System.Random(_theme.randomSeed * 7919 + 11);

            // ---- the village ----
            float vr = AlpVillageR;
            for (int i = 0; i < 300 && !_hasAlpVillage; i++)
            {
                float limit = half - vr - 34f;
                var p = new Vector2(Rand(rng, -limit, limit), Rand(rng, -limit, limit));
                if (p.magnitude < 115f || !Free(p, vr + 4f)) continue;

                _alpCentre = p;
                _hasAlpVillage = true;
                Claim(p.x, p.y, vr + 10f);
                FlattenPad(p.x, p.y, vr, 32f, SiteHeight(p, vr));
                _anchors.Add(new Vector3(p.x, 0f, p.y));
                _snowSites.Add(new Vector3(p.x, p.y, vr + 16f));
            }
            if (!_hasAlpVillage) Debug.LogWarning("[FPSKit] snow: no room for the village.");

            Debug.Log($"[FPSKit] snow plan: village {_hasAlpVillage}.");
        }

        /// <summary>The outpost, lodge with lift, mine and pass: after the village and the station have their ground.</summary>
        private static void PlanSnowRest(float half)
        {
            if (!_theme.snowZone) return;
            var rng = new System.Random(_theme.randomSeed * 6841 + 29);

            // ---- the outpost: a walled compound, 76 by 54 ----
            _hasOutpost = SnowSite(rng, half, 76f, 54f, 400, null, out _outpost, blend: 24f, gap: 6f);
            if (!_hasOutpost) Debug.LogWarning("[FPSKit] snow: no room for the outpost.");

            // ---- the lodge, and a ski lift that climbs away from it ----
            _hasLodge = SnowSite(rng, half, 32f, 26f, 300, null, out _lodge, blend: 20f);
            if (_hasLodge) PlanSkiLift(rng, half);

            // ---- the mine, and the pass ----
            _hasMine = SnowSite(rng, half, 30f, 24f, 300, null, out _mine, blend: 16f);
            _hasPass = SnowSite(rng, half, 44f, 30f, 300, null, out _pass, blend: 18f);

            Debug.Log($"[FPSKit] snow plan: outpost {_hasOutpost}, lodge {_hasLodge}, lift {_hasLift}, mine {_hasMine}, pass {_hasPass}.");
        }

        /// <summary>The small sites, last: they fill what the big ones and the ice left.</summary>
        private static void PlanSnowSmall(float half)
        {
            if (!_theme.snowZone) return;
            var rng = new System.Random(_theme.randomSeed * 5483 + 17);
            for (int i = 0; i < 8; i++)
                if (SnowSite(rng, half, 15f, 13f, 160, null, out var cabin, blend: 10f, gap: 4f, spawnClear: 40f))
                    _alpCabins.Add(cabin);
            for (int i = 0; i < 6; i++)
                if (SnowSite(rng, half, 14f, 14f, 160, null, out var camp, blend: 8f, gap: 4f, spawnClear: 36f))
                    _alpCamps.Add(camp);
            Debug.Log($"[FPSKit] snow plan: {_alpCabins.Count} cabin(s), {_alpCamps.Count} camp(s).");
        }

        // ==================================================================
        // Shared kit
        // ==================================================================
        /// <summary>
        /// The point on a box's face: <paramref name="along"/> is metres along the face (local u for
        /// faces 0 and 1, local v for 2 and 3), <paramref name="outward"/> out from it. Faces: 0 is
        /// -v (the front), 1 +v, 2 -u, 3 +u. <paramref name="rot"/> turns a box so its z points out.
        /// </summary>
        private static Vector3 FaceAt(Frame f, int face, float cu, float cv, float w, float d, float along, float y,
                                      float outward, out Quaternion rot)
        {
            float hw = w * 0.5f, hd = d * 0.5f;
            switch (face)
            {
                case 0: rot = f.Rot; return f.P(cu + along, y, cv - hd - outward);
                case 1: rot = f.Rot; return f.P(cu + along, y, cv + hd + outward);
                case 2: rot = f.Rot * Quaternion.Euler(0f, 90f, 0f); return f.P(cu - hw - outward, y, cv + along);
                default: rot = f.Rot * Quaternion.Euler(0f, 90f, 0f); return f.P(cu + hw + outward, y, cv + along);
            }
        }

        /// <summary>
        /// A pitched roof over a w by d box, ridge along v: slopes, gable ends and a soffit under
        /// the eaves, plus a snow layer lying on the slopes a little short of the eave.
        /// </summary>
        private static float GableRoof(MeshBuild roof, MeshBuild snow, Frame f, float cu, float cv, float w, float d,
                                       float baseY, float pitch, float over)
        {
            float hw = w * 0.5f + over, hd = d * 0.5f + over;
            float rise = hw * pitch;
            var inside = f.P(cu, baseY + rise * 0.3f, cv);

            Vector3 L0 = f.P(cu - hw, baseY, cv - hd), L1 = f.P(cu - hw, baseY, cv + hd);
            Vector3 R0 = f.P(cu + hw, baseY, cv - hd), R1 = f.P(cu + hw, baseY, cv + hd);
            Vector3 T0 = f.P(cu, baseY + rise, cv - hd), T1 = f.P(cu, baseY + rise, cv + hd);

            AddOutward(roof, inside, L0, T0, T1); AddOutward(roof, inside, L0, T1, L1);
            AddOutward(roof, inside, R0, R1, T1); AddOutward(roof, inside, R0, T1, T0);
            AddOutward(roof, inside, L0, R0, T0); AddOutward(roof, inside, L1, T1, R1);
            AddDown(roof, L0, R0, R1, L1);

            if (snow != null)
            {
                const float lift = 0.13f, inset = 0.35f;
                float sw = hw - inset, sd = hd - inset;
                float srise = sw * pitch;
                Vector3 sL0 = f.P(cu - sw, baseY + inset * pitch + lift, cv - sd), sL1 = f.P(cu - sw, baseY + inset * pitch + lift, cv + sd);
                Vector3 sR0 = f.P(cu + sw, baseY + inset * pitch + lift, cv - sd), sR1 = f.P(cu + sw, baseY + inset * pitch + lift, cv + sd);
                Vector3 sT0 = f.P(cu, baseY + rise + lift + 0.04f, cv - sd), sT1 = f.P(cu, baseY + rise + lift + 0.04f, cv + sd);
                _ = srise;
                AddOutward(snow, inside, sL0, sT0, sT1); AddOutward(snow, inside, sL0, sT1, sL1);
                AddOutward(snow, inside, sR0, sR1, sT1); AddOutward(snow, inside, sR0, sT1, sT0);
            }
            return rise;
        }

        private static void RoofObjects(Transform parent, int layer, MeshBuild roof, MeshBuild snow, Material roofMat)
        {
            var r = Solid(parent, "Roof", roof, roofMat, layer, "Wood");
            if (r != null) NoStanding(r);
            if (snow.Triangles.Count > 0)
            {
                var s = MeshObject(parent, "RoofSnow", ToMesh(snow, DenseKey("roofsnow")), _pineSnowMat, Vector3.zero,
                                   Quaternion.identity, Vector3.one, layer, null, collider: false);
                NoStanding(s); Hide(s);
            }
        }

        /// <summary>A stone footing sunk two metres, so no house hangs over a gap where its pad meets a slope.</summary>
        private static void SnowFooting(Transform parent, int layer, Frame f, float cu, float cv, float w, float d)
        {
            var build = new MeshBuild { UVScale = 0.5f };
            build.Box(f.P(cu, -0.85f, cv), new Vector3(w + 0.4f, 2.3f, d + 0.4f), f.Rot);
            var go = MeshObject(parent, "Footing", ToMesh(build, DenseKey("footing")), _snowStoneMat, Vector3.zero,
                                Quaternion.identity, Vector3.one, layer, "Concrete", collider: false);
            NoStanding(go); Hide(go);
        }

        /// <summary>Snow banked against the foot of a wall: low soft mounds, no collider, off the bake.</summary>
        private static void DriftAgainst(Transform parent, int layer, System.Random rng, Frame f, float cu, float cv, float w, float d, int skipFace = -1)
        {
            for (int i = 0; i < 3; i++)
            {
                int face = rng.Next(4);
                if (face == skipFace) face = (face + 1) % 4;   // a mound on a stair marks its steps unwalkable
                float len = face < 2 ? w : d;
                float along = Rand(rng, -len * 0.35f, len * 0.35f);
                var p = FaceAt(f, face, cu, cv, w, d, along, 0f, 0.5f, out var rot);
                var go = MeshObject(parent, "Drift", BoulderMesh(rng.Next(1, 40), 0.1f, 0.4f), _pineSnowMat,
                                    p + Vector3.up * 0.05f, rot, new Vector3(Rand(rng, 2.4f, 3.6f), Rand(rng, 0.7f, 1.1f), Rand(rng, 1.0f, 1.5f)),
                                    layer, null, collider: false);
                NoStanding(go); Hide(go);
            }
        }

        /// <summary>
        /// Windows, doors, corner posts, a belt course, a chimney and a lantern: what makes a box a
        /// chalet. <paramref name="glassed"/> is false for an enterable house, whose windows are
        /// real openings; <paramref name="solidDoors"/> likewise puts a leaf in each doorway.
        /// </summary>
        private static void ChaletTrim(Transform parent, int layer, System.Random rng, Frame f, float cu, float cv, float w, float d,
                                       float top, List<(int face, float at)> doors, List<(int face, float at)> windows,
                                       bool glassed, bool solidDoors, int upperStoreys, Material shutter, bool chimney, float rise)
        {
            var frames = new MeshBuild { UVScale = 0.5f };
            var glass = new MeshBuild { UVScale = 0.5f };
            var shut = new MeshBuild { UVScale = 0.5f };
            var leaves = new MeshBuild { UVScale = 0.5f };

            void Win(int face, float at, float y)
            {
                var p = FaceAt(f, face, cu, cv, w, d, at, y, 0.04f, out var rot);
                frames.Box(p, new Vector3(1.4f, 1.3f, 0.1f), rot);
                if (glassed) glass.Box(FaceAt(f, face, cu, cv, w, d, at, y, 0.08f, out _), new Vector3(1.0f, 0.95f, 0.1f), rot);
                foreach (float s in new[] { -0.95f, 0.95f })
                    shut.Box(FaceAt(f, face, cu, cv, w, d, at + s, y, 0.09f, out _), new Vector3(0.5f, 1.2f, 0.08f), rot);
                frames.Box(FaceAt(f, face, cu, cv, w, d, at, y - 0.72f, 0.16f, out _), new Vector3(1.7f, 0.1f, 0.32f), rot);
            }

            foreach (var win in windows)
            {
                Win(win.face, win.at, 1.7f);
                if (upperStoreys > 0) Win(win.face, win.at, AlpStorey + 1.5f);
            }

            foreach (var door in doors)
            {
                var p = FaceAt(f, door.face, cu, cv, w, d, door.at, 1.3f, 0.05f, out var rot);
                frames.Box(p, new Vector3(1.9f, 2.7f, 0.12f), rot);
                frames.Box(FaceAt(f, door.face, cu, cv, w, d, door.at, 2.75f, 0.18f, out _), new Vector3(2.3f, 0.14f, 0.55f), rot);
                frames.Box(FaceAt(f, door.face, cu, cv, w, d, door.at, 0.08f, 0.55f, out _), new Vector3(2.0f, 0.16f, 0.9f), rot);
                if (solidDoors) leaves.Box(FaceAt(f, door.face, cu, cv, w, d, door.at, 1.1f, 0.1f, out _), new Vector3(1.4f, 2.2f, 0.12f), rot);
                glass.Box(FaceAt(f, door.face, cu, cv, w, d, door.at + 1.35f, 2.3f, 0.2f, out _), new Vector3(0.25f, 0.35f, 0.25f), rot);   // lantern
            }

            // Corner posts and a belt course between storeys.
            foreach (float su in new[] { -1f, 1f })
                foreach (float sv in new[] { -1f, 1f })
                    frames.Box(f.P(cu + su * w * 0.5f, top * 0.5f, cv + sv * d * 0.5f), new Vector3(0.34f, top, 0.34f), f.Rot);
            if (upperStoreys > 0)
                frames.Box(f.P(cu, AlpStorey, cv), new Vector3(w + 0.18f, 0.22f, d + 0.18f), f.Rot);

            if (chimney)
            {
                float ch = rise + 1.0f;
                var at = f.P(cu + w * 0.22f, top + ch * 0.5f - 0.1f, cv - d * 0.15f);
                frames.Box(at, new Vector3(0.95f, ch, 0.95f), f.Rot);
                frames.Box(at + Vector3.up * (ch * 0.5f + 0.06f), new Vector3(1.15f, 0.14f, 1.15f), f.Rot);
            }

            Visual(parent, "Trim", frames, _logDarkMat, layer);
            Visual(parent, "Glass", glass, _dullGlassMat, layer);
            Visual(parent, "Shutters", shut, shutter, layer);
            Visual(parent, "DoorLeaves", leaves, _logDarkMat, layer);
        }

        /// <summary>A roofed porch over a door on face 0 or 1: two posts, a lean-to, a bench.</summary>
        private static void Porch(Transform parent, int layer, Frame f, float cu, float cv, float w, float d, int face, float at)
        {
            var wood = new MeshBuild { UVScale = 0.5f };
            var p1 = FaceAt(f, face, cu, cv, w, d, at - 1.1f, 1.45f, 1.5f, out var rot);
            var p2 = FaceAt(f, face, cu, cv, w, d, at + 1.1f, 1.45f, 1.5f, out _);
            wood.Box(p1, new Vector3(0.22f, 2.9f, 0.22f), rot);
            wood.Box(p2, new Vector3(0.22f, 2.9f, 0.22f), rot);
            wood.Box(FaceAt(f, face, cu, cv, w, d, at, 2.95f, 0.85f, out _), new Vector3(2.9f, 0.16f, 1.9f), rot);
            wood.Box(FaceAt(f, face, cu, cv, w, d, at, 0.05f, 1.0f, out _), new Vector3(2.7f, 0.1f, 1.7f), rot);
            var go = Solid(parent, "Porch", wood, _logDarkMat, layer, "Wood");
            _ = go;
            var s = new MeshBuild { UVScale = 0.5f };
            s.Box(FaceAt(f, face, cu, cv, w, d, at, 3.08f, 0.85f, out _), new Vector3(3.0f, 0.1f, 2.0f), rot);
            SnowCap(parent, "PorchSnow", s, _pineSnowMat, layer);
        }

        /// <summary>Firewood stacked in rows against a wall, and a chopping block.</summary>
        private static void Woodpile(MeshBuild logs, Vector3 at, Quaternion rot, int rows, float length)
        {
            for (int r = 0; r < rows; r++)
            {
                float y = 0.2f + r * 0.38f;
                logs.Box(at + rot * new Vector3(0f, y, 0f), new Vector3(length, 0.36f, 0.7f), rot);
            }
        }

        private static void Sled(MeshBuild wood, Vector3 at, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            foreach (float s in new[] { -0.45f, 0.45f })
                wood.Box(at + rot * new Vector3(s, 0.1f, 0f), new Vector3(0.1f, 0.12f, 2.6f), rot);
            wood.Box(at + rot * new Vector3(0f, 0.25f, 0f), new Vector3(1.1f, 0.08f, 2.4f), rot);
            wood.Box(at + rot * new Vector3(0f, 0.55f, 0.3f), new Vector3(0.8f, 0.5f, 0.9f), rot);
        }

        /// <summary>Log-rail picket fence between two local points, posts every 2.2 m, a gap if <paramref name="gate"/> is set.</summary>
        private static void RailFence(MeshBuild wood, Frame f, Vector2 a, Vector2 b, float height, float gateAt = float.NaN, float gateWidth = 1.8f)
        {
            var d = b - a;
            float length = d.magnitude;
            if (length < 0.5f) return;
            var dir = d / length;
            var rot = f.Rot * Quaternion.Euler(0f, Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg, 0f);
            int posts = Mathf.Max(2, Mathf.CeilToInt(length / 2.2f) + 1);
            for (int i = 0; i < posts; i++)
            {
                float s = length * i / (posts - 1);
                if (!float.IsNaN(gateAt) && Mathf.Abs(s - gateAt) < gateWidth * 0.5f - 0.1f) continue;
                var p = a + dir * s;
                wood.Box(f.P(p.x, height * 0.5f - 0.1f, p.y), new Vector3(0.14f, height + 0.2f, 0.14f), rot);
            }
            for (float y = height * 0.45f; y < height; y += height * 0.4f)
            {
                float s0 = 0f;
                if (!float.IsNaN(gateAt))
                {
                    float g0 = gateAt - gateWidth * 0.5f, g1 = gateAt + gateWidth * 0.5f;
                    if (g0 > 0.3f) { var m = a + dir * (g0 * 0.5f); wood.Box(f.P(m.x, y, m.y), new Vector3(g0, 0.09f, 0.07f), rot); }
                    s0 = g1;
                    if (length - g1 > 0.3f) { var m = a + dir * ((length + g1) * 0.5f); wood.Box(f.P(m.x, y, m.y), new Vector3(length - g1, 0.09f, 0.07f), rot); }
                }
                else
                {
                    var m = a + dir * (length * 0.5f);
                    wood.Box(f.P(m.x, y, m.y), new Vector3(length, 0.09f, 0.07f), rot);
                }
                _ = s0;
            }
        }

        /// <summary>A guard rail round a flat roof, broken for the stair on the +u side (gapV, NaN for none).</summary>
        private static void RoofRail(Transform parent, int layer, Frame f, float cu, float cv, float w, float d, float roofY, float gapV)
        {
            var rail = new MeshBuild { UVScale = 0.4f };
            const float ph = 1.0f, pt = 0.2f;
            float hw = w * 0.5f, hd = d * 0.5f, y = roofY + ph * 0.5f;
            rail.Box(f.P(cu, y, cv - hd + pt * 0.5f), new Vector3(w, ph, pt), f.Rot);
            rail.Box(f.P(cu, y, cv + hd - pt * 0.5f), new Vector3(w, ph, pt), f.Rot);
            rail.Box(f.P(cu - hw + pt * 0.5f, y, cv), new Vector3(pt, ph, d - pt * 2f), f.Rot);
            float eu = cu + hw - pt * 0.5f;
            if (!float.IsNaN(gapV))
            {
                float z0 = cv - hd + pt, z1 = gapV - 1.2f, z2 = gapV + 1.2f, z3 = cv + hd - pt;
                if (z1 > z0) rail.Box(f.P(eu, y, (z0 + z1) * 0.5f), new Vector3(pt, ph, z1 - z0), f.Rot);
                if (z3 > z2) rail.Box(f.P(eu, y, (z2 + z3) * 0.5f), new Vector3(pt, ph, z3 - z2), f.Rot);
            }
            else rail.Box(f.P(eu, y, cv), new Vector3(pt, ph, d - pt * 2f), f.Rot);
            Solid(parent, "RoofRail", rail, _logDarkMat, layer, "Wood");
        }

        /// <summary>
        /// A solid timber stair like AdobeStair, but wider. At 1.8 m the bake's ledge filter and the agent
        /// radius left a sliver down the middle that did not survive, and every roof it served was cut off.
        /// </summary>
        private static void AlpStair(Transform parent, int layer, Material mat, Vector3 landing, Vector3 up, float height, float wide)
        {
            var build = new MeshBuild { UVScale = 0.3f };
            int steps = Mathf.Max(2, Mathf.RoundToInt(height / StairRise));
            Vector3 Size(float along, float y) => Mathf.Abs(up.x) > 0.5f ? new Vector3(along, y, wide) : new Vector3(wide, y, along);
            for (int i = 0; i < steps; i++)
            {
                float back = StairLanding * 0.5f + (steps - i - 0.5f) * StairTread;
                float h = (i + 1) * StairRise;
                build.Box(landing - up * back + Vector3.up * h * 0.5f, Size(StairTread, h), Quaternion.identity);
            }
            build.Box(landing + Vector3.up * (steps * StairRise * 0.5f), Size(StairLanding, steps * StairRise), Quaternion.identity);
            MeshObject(parent, "Stair", ToMesh(build, DenseKey("alpstair")), mat, Vector3.zero, Quaternion.identity,
                       Vector3.one, layer, "Wood");
        }

        /// <summary>A visual-only mesh whose top must never bake as walkable: canopies, snow caps over rock.</summary>
        private static void SnowCap(Transform parent, string name, MeshBuild build, Material mat, int layer)
        {
            if (build.Triangles.Count == 0) return;
            var go = MeshObject(parent, name, ToMesh(build, DenseKey(name)), mat, Vector3.zero, Quaternion.identity,
                                Vector3.one, layer, null, collider: false);
            NoStanding(go);
            Hide(go);
        }

        // ==================================================================
        // Chalets
        // ==================================================================
        /// <summary>
        /// A solid chalet on its lot, sealed off the bake: one storey or two, a pitched roof under
        /// snow, shuttered windows, a door, a chimney. With a fence, it takes the back of the lot and
        /// a rail fence closes the front with a gate.
        /// </summary>
        private static void BuildSolidChalet(Transform parent, int layer, System.Random rng, Frame f, float lot, bool fenced)
        {
            var paint = _chaletPaints[rng.Next(_chaletPaints.Length)];
            bool two = rng.NextDouble() < 0.4;

            float w = Rand(rng, 7f, lot - 2f), d = Rand(rng, 8f, lot - 1.6f);
            float cu = Rand(rng, -(lot - w) * 0.35f, (lot - w) * 0.35f);
            float cv = fenced ? lot * 0.5f - 0.6f - d * 0.5f : Rand(rng, -(lot - d) * 0.35f, (lot - d) * 0.35f);
            float top = AlpStorey + (two ? 2.9f : 0f);

            var shell = new MeshBuild { UVScale = 0.3f };
            shell.SoftBox(f.P(cu, top * 0.5f, cv), new Vector3(w, top, d), f.Rot, rng.Next(1, 9999), 0.08f, 3, 0.01f);
            var body = Solid(parent, "Chalet", shell, paint, layer, "Wood");
            _ = body;
            SealLocal(parent, f, cu, top * 0.5f, cv, new Vector3(w, top, d));
            SnowFooting(parent, layer, f, cu, cv, w, d);

            var roof = new MeshBuild { UVScale = 0.4f };
            var snow = new MeshBuild { UVScale = 0.4f };
            float rise = GableRoof(roof, snow, f, cu, cv, w, d, top, Rand(rng, 0.5f, 0.7f), 0.75f);
            RoofObjects(parent, layer, roof, snow, _roofPaints[rng.Next(_roofPaints.Length)]);

            int frontDoorFace = 0;
            var doors = new List<(int, float)> { (frontDoorFace, Rand(rng, -w * 0.28f, w * 0.28f)) };
            if (rng.NextDouble() < 0.4) doors.Add((2 + rng.Next(2), Rand(rng, -d * 0.2f, d * 0.2f)));
            var windows = ScatterWindows(rng, w, d, doors);
            var shutter = _chaletPaints[rng.Next(_chaletPaints.Length)];
            ChaletTrim(parent, layer, rng, f, cu, cv, w, d, top, doors, windows, glassed: true, solidDoors: true,
                       upperStoreys: two ? 1 : 0, shutter, chimney: true, rise: rise);
            Porch(parent, layer, f, cu, cv, w, d, doors[0].Item1, doors[0].Item2);
            DriftAgainst(parent, layer, rng, f, cu, cv, w, d);

            var clutter = new MeshBuild { UVScale = 0.5f };
            if (rng.NextDouble() < 0.7) Woodpile(clutter, f.P(cu + w * 0.5f + 0.5f, 0f, cv + d * 0.2f), f.Rot * Quaternion.Euler(0f, 90f, 0f), 3, 2.6f);
            Solid(parent, "Woodpile", clutter, _logMat, layer, "Wood");

            if (fenced)
            {
                var fence = new MeshBuild { UVScale = 0.5f };
                float fv = -lot * 0.5f + 0.3f;
                RailFence(fence, f, new Vector2(-lot * 0.5f + 0.3f, fv), new Vector2(lot * 0.5f - 0.3f, fv), 1.2f, lot * 0.5f + cu + doors[0].Item2, 2.0f);
                RailFence(fence, f, new Vector2(-lot * 0.5f + 0.3f, fv), new Vector2(-lot * 0.5f + 0.3f, cv), 1.2f);
                RailFence(fence, f, new Vector2(lot * 0.5f - 0.3f, fv), new Vector2(lot * 0.5f - 0.3f, cv), 1.2f);
                Solid(parent, "Fence", fence, _logMat, layer, "Wood");
            }
        }

        private static List<(int face, float at)> ScatterWindows(System.Random rng, float w, float d, List<(int, float)> doors)
        {
            var list = new List<(int face, float at)>();
            bool NearDoor(int face, float at)
            {
                foreach (var door in doors) if (door.Item1 == face && Mathf.Abs(door.Item2 - at) < 1.9f) return true;
                return false;
            }
            for (int face = 0; face < 4; face++)
            {
                float len = face < 2 ? w : d;
                int n = Mathf.Max(1, Mathf.FloorToInt(len / 3.4f));
                for (int i = 0; i < n; i++)
                {
                    float at = ((i + 0.5f) / n - 0.5f) * len;
                    if (NearDoor(face, at)) continue;
                    list.Add((face, at));
                }
            }
            return list;
        }

        /// <summary>
        /// A chalet that can be gone into: walls with real doors and windows, two rooms with a
        /// doorway between, furniture, a door out front and another out back so no room is a dead
        /// end. A pitched roof over a ceiling (nobody stands on either), or -- for the storehouse --
        /// a flat roof under snow that is ground, with a log stair up the +u side to it.
        /// </summary>
        private static void BuildEnterableChalet(Transform parent, int layer, int backdrop, System.Random rng, Frame f,
                                                 float lot, bool flatRoof, float wIn = 0f, float dIn = 0f, bool bigLodge = false)
        {
            const float t = 0.4f;
            float storey = AlpStorey;
            var paint = _chaletPaints[rng.Next(_chaletPaints.Length)];

            float w = wIn > 0f ? wIn : Rand(rng, 7f, 7.6f);
            float d = dIn > 0f ? dIn : Rand(rng, 8.8f, lot - 0.6f);
            float cu = flatRoof || wIn > 0f ? -(lot - w) * 0.5f + 0.25f : Rand(rng, -0.4f, 0.4f);
            if (wIn > 0f) cu = 0f;
            const float cv = 0f;
            float hw = w * 0.5f, hd = d * 0.5f;
            float gapV = cv + hd - 1.6f;

            float frontDoor = Rand(rng, -hw * 0.35f, hw * 0.35f);
            float backDoor = Rand(rng, -hw * 0.35f, hw * 0.35f);
            var doorList = new List<(int, float)> { (0, frontDoor), (1, backDoor) };
            if (bigLodge) doorList.Add((3, 0f));
            var wins = ScatterWindows(rng, w, d, doorList);
            // The +u wall is where the storehouse's stair climbs: no windows, shutters or sills there, or the
            // render meshes close the navmesh on the steps (the bake reads them, collider or not).
            if (flatRoof) wins.RemoveAll(win => win.face == 3);

            var front = new List<Opening> { new Opening(hw + frontDoor, 1.7f, 0f, 2.5f) };
            var back = new List<Opening> { new Opening(hw + backDoor, 1.7f, 0f, 2.5f) };
            var left = new List<Opening>();
            var right = new List<Opening>();
            foreach (var win in wins)
            {
                var o = new List<Opening>();
                switch (win.face)
                {
                    case 0: front.Add(new Opening(hw + win.at, 1.1f, 1.1f, 2.2f)); break;
                    case 1: back.Add(new Opening(hw + win.at, 1.1f, 1.1f, 2.2f)); break;
                    case 2: left.Add(new Opening(hd - t + win.at, 1.1f, 1.1f, 2.2f)); break;
                    default:
                        // The flat-roofed storehouse keeps its +u wall blank where the stair climbs it.
                        if (!flatRoof) right.Add(new Opening(hd - t + win.at, 1.1f, 1.1f, 2.2f));
                        break;
                }
                _ = o;
            }
            if (bigLodge) right.Add(new Opening(hd - t, 1.7f, 0f, 2.5f));

            var walls = new MeshBuild { UVScale = 0.28f };
            WallRun(walls, f, new Vector2(cu - hw, cv - hd + t * 0.5f), new Vector2(cu + hw, cv - hd + t * 0.5f), 0f, storey, t, front);
            WallRun(walls, f, new Vector2(cu - hw, cv + hd - t * 0.5f), new Vector2(cu + hw, cv + hd - t * 0.5f), 0f, storey, t, back);
            WallRun(walls, f, new Vector2(cu - hw + t * 0.5f, cv - hd + t), new Vector2(cu - hw + t * 0.5f, cv + hd - t), 0f, storey, t, left);
            WallRun(walls, f, new Vector2(cu + hw - t * 0.5f, cv - hd + t), new Vector2(cu + hw - t * 0.5f, cv + hd - t), 0f, storey, t, right);

            // The partition across u, with a doorway kept off the line of either outside door.
            float pv = cv + Rand(rng, -0.8f, 0.8f);
            float pDoor = Mathf.Abs(frontDoor) > hw * 0.15f ? -frontDoor : (rng.Next(2) == 0 ? -1f : 1f) * hw * 0.4f;
            float inner = Mathf.Max(0.3f, hw - t);
            WallRun(walls, f, new Vector2(cu - hw + t, pv), new Vector2(cu + hw - t, pv), 0f, storey - 0.3f, 0.2f,
                    new List<Opening> { new Opening(inner + pDoor, 1.6f, 0f, 2.4f) });

            // The storehouse's walls are left walkable-by-area: NoStanding on a wall the stair climbs beside
            // closes the navmesh on the last steps (measured: every roof unreachable with it, all five
            // reachable without). Wall tops are 0.4 m wide, too thin for an agent to bake on anyway.
            var wallGo = Solid(parent, "ChaletWalls", walls, paint, layer, "Wood", standing: flatRoof);
            _ = wallGo;

            // The ceiling, or the roof deck.
            var deck = new MeshBuild { UVScale = 0.28f };
            deck.Box(f.P(cu, storey - 0.15f, cv), new Vector3(w, 0.3f, d), f.Rot);
            if (flatRoof)
            {
                Solid(parent, "StoreRoof", deck, _pineSnowMat, layer, "Wood", standing: true);
                RoofRail(parent, layer, f, cu, cv, w, d, storey, gapV);
                const float stairWide = 2.6f;
                var upAxis = f.Axis(Vector3.forward);
                AlpStair(parent, layer, _plankMat, f.P(cu + hw + 0.05f + stairWide * 0.5f, 0f, gapV),
                         new Vector3(Mathf.Round(upAxis.x), 0f, Mathf.Round(upAxis.z)), storey, stairWide);
            }
            else
            {
                Solid(parent, "Ceiling", deck, paint, layer, "Wood");
                var roof = new MeshBuild { UVScale = 0.4f };
                var snow = new MeshBuild { UVScale = 0.4f };
                float rise = GableRoof(roof, snow, f, cu, cv, w, d, storey, 0.55f, 0.75f);
                RoofObjects(parent, layer, roof, snow, _roofPaints[rng.Next(_roofPaints.Length)]);
                ChaletTrim(parent, layer, rng, f, cu, cv, w, d, storey, doorList, wins, glassed: false, solidDoors: false,
                           upperStoreys: 0, _chaletPaints[rng.Next(_chaletPaints.Length)], chimney: true, rise: rise);
                goto interior;
            }
            ChaletTrim(parent, layer, rng, f, cu, cv, w, d, storey, doorList, wins, glassed: false, solidDoors: false,
                       upperStoreys: 0, _chaletPaints[rng.Next(_chaletPaints.Length)], chimney: false, rise: 0f);

        interior:
            SnowFooting(parent, layer, f, cu, cv, w, d);

            var floorBuild = new MeshBuild { UVScale = 0.5f };
            AddUp(floorBuild, f.P(cu - hw + t, 0.02f, cv - hd + t), f.P(cu - hw + t, 0.02f, cv + hd - t),
                  f.P(cu + hw - t, 0.02f, cv + hd - t), f.P(cu + hw - t, 0.02f, cv - hd + t));
            Flat(parent, backdrop, "ChaletFloor", floorBuild, DenseKey("chaletfloor"), _plankMat);

            ChaletFurnish(parent, layer, rng, f, cu - hw + t, cu + hw - t, cv - hd + t, pv - 0.1f, frontDoor + cu, pDoor + cu);
            ChaletFurnish(parent, layer, rng, f, cu - hw + t, cu + hw - t, pv + 0.1f, cv + hd - t, backDoor + cu, pDoor + cu);

            Porch(parent, layer, f, cu, cv, w, d, 0, frontDoor);
            DriftAgainst(parent, layer, rng, f, cu, cv, w, d, flatRoof ? 3 : -1);
        }

        /// <summary>A room's furniture: bench, table with stools, a stove with its pipe, shelves. Kept off the door lines.</summary>
        private static void ChaletFurnish(Transform parent, int layer, System.Random rng, Frame f,
                                          float u0, float u1, float v0, float v1, float doorA, float doorB)
        {
            var wood = new MeshBuild { UVScale = 0.5f };
            var iron = new MeshBuild { UVScale = 0.5f };
            var cloth = new MeshBuild { UVScale = 0.5f };
            float midV = (v0 + v1) * 0.5f, depth = v1 - v0;
            bool Clear(float u) => Mathf.Abs(u - doorA) > 1.5f && Mathf.Abs(u - doorB) > 1.5f;

            if (depth > 2.6f && Clear(u0 + 0.5f))
            {
                // A bunk along the -u wall, with a blanket on it.
                wood.Box(f.P(u0 + 0.5f, 0.3f, midV), new Vector3(0.9f, 0.6f, Mathf.Min(2.0f, depth - 1.2f)), f.Rot);
                cloth.Box(f.P(u0 + 0.5f, 0.66f, midV), new Vector3(0.84f, 0.12f, Mathf.Min(1.8f, depth - 1.4f)), f.Rot);
            }

            float tu = (u0 + u1) * 0.5f + 0.4f;
            if (Clear(tu))
            {
                wood.Box(f.P(tu, 0.42f, midV), new Vector3(1.2f, 0.08f, 0.8f), f.Rot);
                foreach (float s in new[] { -0.5f, 0.5f })
                    wood.Box(f.P(tu + s, 0.2f, midV), new Vector3(0.08f, 0.4f, 0.7f), f.Rot);
                foreach (float s in new[] { -0.7f, 0.7f })
                    wood.Box(f.P(tu, 0.22f, midV + s), new Vector3(0.4f, 0.44f, 0.36f), f.Rot);
            }

            // The stove against the +u wall, pipe to the ceiling.
            if (Clear(u1 - 0.5f) && depth > 2.2f)
            {
                iron.Box(f.P(u1 - 0.55f, 0.55f, midV), new Vector3(0.7f, 1.1f, 0.7f), f.Rot);
                iron.Tube(f.P(u1 - 0.55f, 1.1f, midV), f.P(u1 - 0.55f, AlpStorey - 0.3f, midV), 0.08f, 0.08f, 6);
            }
            if (Clear(u0 + 0.3f) == false || depth < 2.6f) { }
            else wood.Box(f.P(u0 + 0.22f, 0.9f, v1 - 0.6f), new Vector3(0.4f, 1.8f, 1.2f), f.Rot);

            if (wood.Triangles.Count > 0)
                NoStanding(MeshObject(parent, "Furniture", ToMesh(wood, DenseKey("furniture")), _plankMat, Vector3.zero,
                                      Quaternion.identity, Vector3.one, layer, "Wood"));
            if (iron.Triangles.Count > 0)
                NoStanding(MeshObject(parent, "Stove", ToMesh(iron, DenseKey("stove")), _ironMat, Vector3.zero,
                                      Quaternion.identity, Vector3.one, layer, "Metal"));
            if (cloth.Triangles.Count > 0)
                Visual(parent, "Blanket", cloth, _tentPaints[rng.Next(_tentPaints.Length)], layer);
        }

        /// <summary>An empty lot: a woodyard, a sled, barrels and a short fence. Never nothing at all.</summary>
        private static void BuildYardLot(Transform parent, int layer, System.Random rng, Frame f, float lot)
        {
            var logs = new MeshBuild { UVScale = 0.5f };
            var wood = new MeshBuild { UVScale = 0.5f };
            var iron = new MeshBuild { UVScale = 0.5f };
            for (int i = 0; i < 3; i++)
                Woodpile(logs, f.P(-lot * 0.5f + 1.2f, 0f, -lot * 0.3f + i * 2.2f), f.Rot, 3 + rng.Next(2), 3.2f);
            Sled(wood, f.P(lot * 0.2f, 0f, Rand(rng, -2f, 2f)), f.Yaw + Rand(rng, -20f, 20f));
            for (int i = 0; i < 3; i++)
            {
                var at = f.P(lot * 0.3f + i * 0.7f, 0f, lot * 0.3f);
                iron.Tube(at, at + Vector3.up * 0.9f, 0.3f, 0.3f, 8);
            }
            RailFence(wood, f, new Vector2(-lot * 0.5f + 0.3f, lot * 0.5f - 0.3f), new Vector2(lot * 0.5f - 0.3f, lot * 0.5f - 0.3f), 1.2f);
            Solid(parent, "Woodyard", logs, _logMat, layer, "Wood");
            Solid(parent, "YardWood", wood, _plankMat, layer, "Wood");
            Solid(parent, "Barrels", iron, _ironMat, layer, "Metal");
        }

        // ==================================================================
        // The village
        // ==================================================================
        private static void BuildAlpVillage(Transform parent, int layer, int backdrop, System.Random rng)
        {
            var town = new GameObject("AlpineVillage").transform;
            town.SetParent(parent, false);

            var c = _alpCentre;
            float floor = GroundHeightAt(c.x, c.y);
            float R = AlpVillageR;

            const float lot = 11f, lane = 3.6f, pitch = lot + lane;
            const float mainHalf = 4.5f, crossHalf = 3f;
            float plazaX = crossHalf + lot, plazaZ = mainHalf + lot;

            var lots = new List<Vector2>();
            for (int j = -6; j <= 6; j++)
                for (int k = -6; k <= 6; k++)
                {
                    if (j == 0 || k == 0) continue;
                    float x = c.x + Mathf.Sign(j) * (crossHalf + lot * 0.5f + (Mathf.Abs(j) - 1) * pitch);
                    float z = c.y + Mathf.Sign(k) * (mainHalf + lot * 0.5f + (Mathf.Abs(k) - 1) * pitch);
                    var p = new Vector2(x, z);
                    if ((p - c).magnitude + lot * 0.9f > R - 3f) continue;
                    if (Mathf.Abs(x - c.x) < plazaX && Mathf.Abs(z - c.y) < plazaZ) continue;
                    lots.Add(p);
                }

            Vector2 churchLot = lots.Count > 0 ? lots[0] : c, towerLot = churchLot;
            float nearest = float.MaxValue;
            var corner = c + new Vector2(plazaX, plazaZ);
            foreach (var p in lots) { float dd = (p - corner).magnitude; if (dd < nearest) { nearest = dd; churchLot = p; } }
            nearest = float.MaxValue;
            foreach (var p in lots)
            {
                if (p == churchLot || Mathf.Abs(p.y - churchLot.y) > 1f) continue;
                float dd = (p - churchLot).magnitude;
                if (dd < nearest) { nearest = dd; towerLot = p; }
            }

            int enterable = 0, stores = 0, solid = 0, fenced = 0, yards = 0;
            foreach (var p in lots)
            {
                var at = new Vector3(p.x, floor, p.y);
                if (p == churchLot) { BuildChurch(town, layer, new Frame(at, p.y > c.y ? 0f : 180f)); continue; }
                if (p == towerLot && towerLot != churchLot) { BuildClockTower(town, layer, new Frame(at, 0f)); continue; }

                var f = new Frame(at, rng.Next(4) * 90f);
                double roll = rng.NextDouble();
                if (roll < 0.10) { BuildYardLot(town, layer, rng, f, lot); yards++; continue; }
                if (roll < 0.34) { BuildEnterableChalet(town, layer, backdrop, rng, f, lot, flatRoof: false); enterable++; continue; }
                if (roll < 0.46) { BuildEnterableChalet(town, layer, backdrop, rng, f, lot, flatRoof: true); stores++; continue; }

                bool fence = rng.NextDouble() < 0.35;
                BuildSolidChalet(town, layer, rng, f, lot, fence);
                if (fence) fenced++;
                solid++;
            }

            BuildAlpSquare(town, layer, rng, new Vector3(c.x, floor, c.y), plazaX, plazaZ);

            // Lamp posts down both streets, on the lane corners.
            var lamps = new MeshBuild { UVScale = 0.5f };
            for (int j = -5; j <= 5; j++)
            {
                if (j == 0) continue;
                float x = c.x + Mathf.Sign(j) * (crossHalf + lot + lane * 0.5f + (Mathf.Abs(j) - 1) * pitch);
                if (Mathf.Abs(x - c.x) > R - 6f) continue;
                LampPost(lamps, new Vector3(x, GroundHeightAt(x, c.y + mainHalf - 0.4f), c.y + mainHalf - 0.4f));
            }
            Solid(town, "LampPosts", lamps, _ironMat, layer, "Metal");

            // Snowcats and a sled or two along the main street, off the crown.
            int parked = 0;
            for (int i = 0; i < 30 && parked < 5; i++)
            {
                float x = c.x + Rand(rng, -R + 10f, R - 10f);
                if (Mathf.Abs(x - c.x) < plazaX + 2f) continue;
                float side = rng.Next(2) == 0 ? -1f : 1f;
                var at = new Vector3(x, GroundHeightAt(x, c.y + side * 3.3f), c.y + side * 3.3f);
                Snowcat(town, layer, rng, at, 90f + (side > 0 ? 0f : 180f) + Rand(rng, -5f, 5f));
                parked++;
            }

            // Pines at the village's edge, thick enough to be a treeline and not so thick as to wall it in.
            var trunks = new MeshBuild { UVScale = 0.5f };
            var boughs = new MeshBuild { UVScale = 0.5f };
            var snow = new MeshBuild { UVScale = 0.5f };
            for (int i = 0; i < 28; i++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f);
                var at = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Rand(rng, R + 3f, R + 18f);
                if (!Free(at, 1f) && (at - c).magnitude < R + 6f) continue;
                Pine(trunks, boughs, snow, rng, new Vector3(at.x, GroundHeightAt(at.x, at.y) - 0.3f, at.y));
            }
            Solid(town, "EdgePineTrunks", trunks, _barkMat, layer, "Wood");
            Visual(town, "EdgePineBoughs", boughs, _pineMat, layer);
            Visual(town, "EdgePineSnow", snow, _pineSnowMat, layer);

            // The dashboard card, over the village from beyond its edge.
            var old = parent.parent != null ? parent.parent.Find("PreviewAnchor") : null;
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var anchor = new GameObject("PreviewAnchor").transform;
            anchor.SetParent(parent.parent != null ? parent.parent : parent, false);
            anchor.position = new Vector3(c.x - 62f, floor + 28f, c.y - 58f);
            anchor.rotation = Quaternion.LookRotation(new Vector3(c.x, floor + 3f, c.y) - anchor.position);

            Debug.Log($"[FPSKit] snow village: {enterable} enterable chalet(s), {stores} roof-walkable storehouse(s), {solid} solid ({fenced} fenced), {yards} yard(s), {parked} snowcat(s).");
        }

        private static void LampPost(MeshBuild m, Vector3 at)
        {
            m.Tube(at + Vector3.down * 0.3f, at + Vector3.up * 4.4f, 0.1f, 0.07f, 6);
            m.Box(at + Vector3.up * 4.6f, new Vector3(0.5f, 0.45f, 0.5f), Quaternion.identity);
            m.Box(at + Vector3.up * 4.9f, new Vector3(0.7f, 0.08f, 0.7f), Quaternion.identity);
        }

        /// <summary>The square: a frozen well, benches, a big pine, and a stall in each corner leaving both streets open.</summary>
        private static void BuildAlpSquare(Transform parent, int layer, System.Random rng, Vector3 centre, float halfX, float halfZ)
        {
            var stone = new MeshBuild { UVScale = 0.4f };
            var ice = new MeshBuild { UVScale = 0.4f };
            var wood = new MeshBuild { UVScale = 0.5f };
            var cloth = new[] { new MeshBuild { UVScale = 0.5f }, new MeshBuild { UVScale = 0.5f } };

            // Round well in the middle, an ice skin in the basin.
            stone.Tube(centre + Vector3.down * 0.3f, centre + Vector3.up * 0.9f, 1.8f, 1.8f, 16);
            ice.Tube(centre + Vector3.up * 0.84f, centre + Vector3.up * 0.88f, 1.55f, 1.55f, 16);
            foreach (float s in new[] { -1f, 1f })
                wood.Box(centre + new Vector3(s * 1.5f, 2.2f, 0f), new Vector3(0.2f, 4.4f, 0.2f), Quaternion.identity);
            wood.Box(centre + Vector3.up * 4.3f, new Vector3(3.6f, 0.2f, 0.2f), Quaternion.identity);

            // A stall in each corner: counter, posts, striped canopy.
            int count = 0;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    float w = Rand(rng, 4.6f, 6f), dp = 3.2f;
                    var at = centre + new Vector3(sx * (halfX - 3.6f), 0f, sz * (halfZ - 3.2f));
                    float y0 = GroundHeightAt(at.x, at.z);
                    var rot = Quaternion.Euler(0f, sz > 0 ? 180f : 0f, 0f);
                    foreach (float px in new[] { -1f, 1f })
                        foreach (float pz in new[] { -1f, 1f })
                            wood.Box(new Vector3(at.x, y0, at.z) + rot * new Vector3(px * (w * 0.5f - 0.1f), 1.3f, pz * (dp * 0.5f - 0.1f)),
                                     new Vector3(0.14f, 2.6f, 0.14f), rot);
                    wood.Box(new Vector3(at.x, y0, at.z) + rot * new Vector3(0f, 0.55f, -dp * 0.5f + 0.5f), new Vector3(w - 0.3f, 1.1f, 0.7f), rot);
                    cloth[count % 2].Box(new Vector3(at.x, y0, at.z) + rot * new Vector3(0f, 2.7f, 0f), new Vector3(w + 0.4f, 0.1f, dp + 0.6f), rot);
                    count++;
                }

            Solid(parent, "WellStone", stone, _snowStoneMat, layer, "Concrete");
            Visual(parent, "WellIce", ice, _glacierMat, layer);
            Solid(parent, "SquareWood", wood, _logDarkMat, layer, "Wood");
            SnowCap(parent, "StallClothA", cloth[0], _tentPaints[0], layer);
            SnowCap(parent, "StallClothB", cloth[1], _tentPaints[3], layer);

            // The tall pine, hung with nothing: the square's landmark from the street's end.
            var trunks = new MeshBuild { UVScale = 0.5f };
            var boughs = new MeshBuild { UVScale = 0.5f };
            var snow = new MeshBuild { UVScale = 0.5f };
            var pat = centre + new Vector3(halfX - 5.5f, 0f, -halfZ + 5.5f);
            Pine(trunks, boughs, snow, rng, new Vector3(pat.x, GroundHeightAt(pat.x, pat.z) - 0.3f, pat.z));
            Solid(parent, "SquarePineTrunk", trunks, _barkMat, layer, "Wood");
            Visual(parent, "SquarePineBoughs", boughs, _pineMat, layer);
            Visual(parent, "SquarePineSnow", snow, _pineSnowMat, layer);
        }

        private static void BuildClockTower(Transform parent, int layer, Frame f)
        {
            const float w = 6f, h = 13f;
            var shell = new MeshBuild { UVScale = 0.3f };
            shell.SoftBox(f.P(0f, h * 0.5f, 0f), new Vector3(w, h, w), f.Rot, 31, 0.1f, 3, 0.01f);
            Solid(parent, "ClockTower", shell, _plasterMat, layer, "Concrete");
            SealLocal(parent, f, 0f, h * 0.5f, 0f, new Vector3(w, h, w));
            SnowFooting(parent, layer, f, 0f, 0f, w, w);

            var roof = new MeshBuild { UVScale = 0.4f };
            var snow = new MeshBuild { UVScale = 0.4f };
            var top = f.P(0f, h + 5.2f, 0f);
            var inside = f.P(0f, h + 1.5f, 0f);
            Vector3 A = f.P(-w * 0.55f, h, -w * 0.55f), B = f.P(-w * 0.55f, h, w * 0.55f), C = f.P(w * 0.55f, h, w * 0.55f), D = f.P(w * 0.55f, h, -w * 0.55f);
            AddOutward(roof, inside, A, top, B); AddOutward(roof, inside, B, top, C);
            AddOutward(roof, inside, C, top, D); AddOutward(roof, inside, D, top, A);
            RoofObjects(parent, layer, roof, snow, _slateMat);

            var trim = new MeshBuild { UVScale = 0.5f };
            var face = new MeshBuild { UVScale = 0.5f };
            foreach (int fc in new[] { 0, 1, 2, 3 })
            {
                var p = FaceAt(f, fc, 0f, 0f, w, w, 0f, h - 3.2f, 0.06f, out var rot);
                face.Box(p, new Vector3(2.4f, 2.4f, 0.1f), rot);
                trim.Box(FaceAt(f, fc, 0f, 0f, w, w, 0f, h - 3.2f, 0.04f, out _), new Vector3(2.8f, 2.8f, 0.1f), rot);
                var q = FaceAt(f, fc, 0f, 0f, w, w, 0f, 2.1f, 0.05f, out _);
                trim.Box(q, new Vector3(1.9f, 4.2f, 0.1f), rot);
            }
            foreach (float su in new[] { -1f, 1f })
                foreach (float sv in new[] { -1f, 1f })
                    trim.Box(f.P(su * w * 0.5f, h * 0.5f, sv * w * 0.5f), new Vector3(0.4f, h, 0.4f), f.Rot);
            Visual(parent, "ClockFaces", face, _plasterMat, layer);
            Visual(parent, "TowerTrim", trim, _logDarkMat, layer);
        }

        private static void BuildChurch(Transform parent, int layer, Frame f)
        {
            const float w = 8.6f, d = 15f, tower = 5.2f, nave = 5.4f;
            var shell = new MeshBuild { UVScale = 0.3f };
            shell.SoftBox(f.P(0f, nave * 0.5f, 1.8f), new Vector3(w, nave, d), f.Rot, 17, 0.1f, 3, 0.01f);
            shell.SoftBox(f.P(0f, 14f * 0.5f, -d * 0.5f + 1.8f - tower * 0.5f + 1.6f), new Vector3(tower, 14f, tower), f.Rot, 23, 0.1f, 3, 0.01f);
            Solid(parent, "Church", shell, _plasterMat, layer, "Concrete");
            SealLocal(parent, f, 0f, nave * 0.5f, 1.8f, new Vector3(w, nave, d));
            SealLocal(parent, f, 0f, 7f, -d * 0.5f + 1.8f - tower * 0.5f + 1.6f, new Vector3(tower, 14f, tower));
            SnowFooting(parent, layer, f, 0f, 1.8f, w, d);

            var roof = new MeshBuild { UVScale = 0.4f };
            var snow = new MeshBuild { UVScale = 0.4f };
            GableRoof(roof, snow, f, 0f, 1.8f, w, d, nave, 0.75f, 0.6f);

            // The spire.
            float tz = -d * 0.5f + 1.8f - tower * 0.5f + 1.6f;
            var apex = f.P(0f, 14f + 7.5f, tz);
            var inside = f.P(0f, 14f + 2f, tz);
            float sr = tower * 0.5f + 0.35f;
            Vector3 A = f.P(-sr, 14f, tz - sr), B = f.P(-sr, 14f, tz + sr), C = f.P(sr, 14f, tz + sr), D = f.P(sr, 14f, tz - sr);
            AddOutward(roof, inside, A, apex, B); AddOutward(roof, inside, B, apex, C);
            AddOutward(roof, inside, C, apex, D); AddOutward(roof, inside, D, apex, A);
            RoofObjects(parent, layer, roof, snow, _slateMat);

            var trim = new MeshBuild { UVScale = 0.5f };
            var glass = new MeshBuild { UVScale = 0.5f };
            trim.Box(f.P(0f, 14f + 8.5f, tz), new Vector3(0.12f, 1.8f, 0.12f), f.Rot);
            trim.Box(f.P(0f, 14f + 9.0f, tz), new Vector3(0.9f, 0.12f, 0.12f), f.Rot);
            foreach (float s in new[] { -1f, 1f })
                for (float z = -d * 0.5f + 5.5f; z < d * 0.5f + 1.8f - 1.2f; z += 3.3f)
                {
                    glass.Box(f.P(s * (w * 0.5f + 0.05f), 2.6f, z), new Vector3(0.12f, 2.6f, 1.1f), f.Rot);
                    trim.Box(f.P(s * (w * 0.5f + 0.03f), 2.6f, z), new Vector3(0.1f, 2.9f, 1.4f), f.Rot);
                }
            glass.Box(f.P(0f, 1.6f, tz - tower * 0.5f - 0.05f), new Vector3(2.2f, 3.2f, 0.12f), f.Rot);
            foreach (int fc in new[] { 0, 1, 2, 3 })
                glass.Box(FaceAt(f, fc, 0f, tz, tower, tower, 0f, 11.5f, 0.06f, out _), new Vector3(1.0f, 1.8f, 0.1f),
                          fc < 2 ? f.Rot : f.Rot * Quaternion.Euler(0f, 90f, 0f));
            Visual(parent, "ChurchGlass", glass, _dullGlassMat, layer);
            Visual(parent, "ChurchTrim", trim, _logDarkMat, layer);
        }
    }
}
#endif
