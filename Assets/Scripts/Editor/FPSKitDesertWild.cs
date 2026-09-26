#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The open desert between the places: walled farm compounds, abandoned nomad camps,
    /// checkpoints on the track, a power line striding across it all, dry washes, tyre
    /// ruts, bones and junk -- and scrub over every stretch of sand, thickest along the
    /// washes where the water goes when it rains. Ishaan's "wild desert detail".
    ///
    /// The scrub is the part that does the most. Sand with nothing on it reads as a
    /// texture, whatever its shape; a desert is dotted with plants a few metres apart, and
    /// that dotting is what gives the eye a scale to read distance by. It is all on the
    /// backdrop layer and has no collider -- it is looked at, walked through, and never
    /// in the way of anything the bake or a bullet has to think about.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static readonly List<List<Vector2>> _wadis = new List<List<Vector2>>();
        private static readonly List<SitePlan> _hamletPlans = new List<SitePlan>();

        // ==================================================================
        // Planning
        // ==================================================================
        /// <summary>
        /// Farms and camps after the track, so neither lands on it; the checkpoints on the
        /// track itself; the power line from the far edge through the village to the base.
        /// </summary>
        private static void PlanDesertWild(System.Random rng, float half, float rim)
        {
            _wadis.Clear();
            _hamletPlans.Clear();

            bool ClearOfRiver(Vector2 p, float r) => Mathf.Abs(p.x - GorgeCentreAt(p.y)) > rim + r + 12f;

            // ---- hamlets: a handful of houses on two lanes. The first within sight of the
            // spawn, so the first thing a player sees is somewhere and not sand. ----
            for (int i = 0; i < 600 && _hamletPlans.Count < 4; i++)
            {
                float lo = _hamletPlans.Count == 0 ? 50f : 60f, hi = _hamletPlans.Count == 0 ? 95f : half;
                var p = new Vector2(Rand(rng, -half + 45f, half - 45f), Rand(rng, -half + 45f, half - 45f));
                if (p.magnitude < lo || p.magnitude > hi || !ClearOfRiver(p, 26f) || !Free(p, 26f)) continue;
                if ((p - _townCentre).magnitude < _townRadius + 40f) continue;
                if (_hasFob && _fob.Contains(p, 30f)) continue;
                bool crowded = false;
                foreach (var h in _hamletPlans) if ((h.Centre - p).magnitude < 85f) crowded = true;
                if (crowded) continue;

                _hamletPlans.Add(new SitePlan { Centre = p, Width = 26f, Depth = 41f, Yaw = rng.Next(4) * 90f });
                Claim(p.x, p.y, 25f);
                FlattenPad(p.x, p.y, 25f, 30f, SiteHeight(p, 25f));
                _anchors.Add(new Vector3(p.x, 0f, p.y));
            }

            // ---- farm compounds, each with a field beside it ----
            for (int i = 0; i < 800 && _farmPlans.Count < 18; i++)
            {
                var p = new Vector2(Rand(rng, -half + 40f, half - 40f), Rand(rng, -half + 40f, half - 40f));
                if (p.magnitude < 45f || !ClearOfRiver(p, 18f) || !Free(p, 19f)) continue;
                if ((p - _townCentre).magnitude < _townRadius + 28f) continue;
                if (_hasFob && _fob.Contains(p, 22f)) continue;

                var plan = new SitePlan { Centre = p, Width = 26f, Depth = 22f, Yaw = rng.Next(4) * 90f };
                _farmPlans.Add(plan);
                Claim(p.x, p.y, 19f);
                FlattenPad(p.x, p.y, 17.5f, 24f, SiteHeight(p, 17.5f));
                _anchors.Add(new Vector3(p.x, 0f, p.y));
                PlanFieldFor(rng, plan);
            }

            // ---- then whatever of the groves, kilns, cemetery, mast and battle sites still fits ----
            PlanDesertMore(rng, half, rim);

            // ---- camps: on the open dunes, no pad ----
            for (int i = 0; i < 300 && _campSites.Count < 9; i++)
            {
                var p = new Vector2(Rand(rng, -half + 30f, half - 30f), Rand(rng, -half + 30f, half - 30f));
                if (p.magnitude < 50f || !ClearOfRiver(p, 10f) || !Free(p, 11f)) continue;
                if ((p - _townCentre).magnitude < _townRadius + 20f) continue;
                if (_hasFob && _fob.Contains(p, 14f)) continue;
                _campSites.Add(p);
                Claim(p.x, p.y, 10f);
            }

            // ---- checkpoints: two on the track, out of the village ----
            var candidates = new List<int>();
            for (int i = 2; i < _track.Count - 2; i++)
            {
                var p = _track[i];
                if (float.IsNaN(p.x) || float.IsNaN(_track[i - 2].x) || float.IsNaN(_track[i + 2].x)) continue;
                float fromTown = (p - _townCentre).magnitude;
                if (fromTown < _townRadius + 25f || fromTown > _townRadius + 70f) continue;
                if (_hasFob && _fob.Contains(p, 30f)) continue;
                candidates.Add(i);
            }
            for (int k = 0; k < 40 && _checkpoints.Count < 2 && candidates.Count > 0; k++)
            {
                int i = candidates[rng.Next(candidates.Count)];
                var p = _track[i];
                bool near = false;
                foreach (var c in _checkpoints) if ((new Vector2(c.x, c.y) - p).magnitude < 80f) near = true;
                if (near) continue;
                var ahead = _track[i + 2] - _track[i - 2];
                _checkpoints.Add(new Vector3(p.x, p.y, Mathf.Atan2(ahead.x, ahead.y) * Mathf.Rad2Deg));
                Claim(p.x, p.y, 12f);
            }

            // ---- the power line: in from the edge, to the village, on to the base ----
            _poleLine.Clear();
            var toEdge = new Vector2(-1f, Rand(rng, -0.4f, 0.4f)).normalized;
            var edge = _townCentre + toEdge * 400f;
            edge.x = Mathf.Max(edge.x, -half + 14f);
            edge.y = Mathf.Clamp(edge.y, -half + 14f, half - 14f);
            _poleLine.Add(edge);

            if (_hasFob)
            {
                var toFob = (_fob.Centre - _townCentre).normalized;
                _poleLine.Add(_townCentre + new Vector2(-toFob.y, toFob.x) * (_townRadius + 4f));
                _poleLine.Add(_townCentre + toFob * (_townRadius + 6f) + new Vector2(-toFob.y, toFob.x) * 12f);
                var h = _fob.HalfWorld;
                var corner = _fob.Centre + new Vector2(Mathf.Sign(_townCentre.x - _fob.Centre.x) * (h.x + 4f),
                                                       Mathf.Sign(_townCentre.y - _fob.Centre.y) * (h.y + 4f));
                _poleLine.Add(corner);
            }
            else _poleLine.Add(_townCentre + toEdge * (_townRadius + 4f));
        }

        // ==================================================================
        // Building
        // ==================================================================
        private static void BuildDesertWild(Transform parent, int layer, int backdrop, System.Random rng, float half)
        {
            var wild = new GameObject("Wild").transform;
            wild.SetParent(parent, false);

            foreach (var hamlet in _hamletPlans) BuildHamlet(wild, layer, backdrop, rng, hamlet);
            foreach (var farm in _farmPlans) BuildFarm(wild, layer, backdrop, rng, farm);
            foreach (var camp in _campSites) BuildCamp(wild, layer, backdrop, rng, camp);
            foreach (var cp in _checkpoints) BuildCheckpoint(wild, layer, rng, cp);
            BuildDesertMore(wild, layer, backdrop, rng);
            BuildPowerLine(wild, layer, backdrop);
            BuildWadis(wild, backdrop, rng, half);
            BuildRuts(wild, backdrop, rng, half);

            Debug.Log($"[FPSKit] desert wild: {_hamletPlans.Count} hamlet(s), {_farmPlans.Count} farm(s), {_campSites.Count} camp(s), " +
                      $"{_checkpoints.Count} checkpoint(s), {_wadis.Count} wadi(s).");
        }

        // ---- hamlets -----------------------------------------------------
        /// <summary>
        /// A hamlet: six lots, two across and three deep, on a lane down the middle and
        /// lanes between -- the village's grid, small. Houses, open houses and ruins, a
        /// well at the end of the lane, a palm or two and a pickup.
        /// </summary>
        private static void BuildHamlet(Transform parent, int layer, int backdrop, System.Random rng, SitePlan plan)
        {
            var hamlet = new GameObject("Hamlet").transform;
            hamlet.SetParent(parent, false);

            float floor = GroundHeightAt(plan.Centre.x, plan.Centre.y);
            var f = new Frame(new Vector3(plan.Centre.x, floor, plan.Centre.y), plan.Yaw);
            const float lot = 11f, lane = 4f;

            var ground = new MeshBuild { UVScale = 0.2f };
            float hw = plan.Width * 0.5f + 3f, hd = plan.Depth * 0.5f + 3f;
            AddUp(ground, f.P(-hw, 0.03f, -hd), f.P(-hw, 0.03f, hd), f.P(hw, 0.03f, hd), f.P(hw, 0.03f, -hd));
            Flat(hamlet, backdrop, "HamletGround", ground, DenseKey("hamletground"), _earthMat);

            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 3; j++)
                {
                    float u = (i == 0 ? -1f : 1f) * (lane * 0.5f + lot * 0.5f);
                    float v = (j - 1) * (lot + lane);
                    var at = f.P(u, 0f, v);
                    var lf = new Frame(at, rng.Next(4) * 90f);
                    double roll = rng.NextDouble();
                    if (roll < 0.3) BuildRuin(hamlet, layer, rng, at, lot);
                    else if (roll < 0.62) BuildEnterableHouse(hamlet, layer, backdrop, rng, lf, lot);
                    else BuildSolidHouse(hamlet, layer, rng, lf, lot, rng.NextDouble() < 0.5);
                }

            // A well at the head of the lane, palms, a pickup.
            var well = new MeshBuild { UVScale = 0.4f };
            var wa = f.P(0f, 0f, plan.Depth * 0.5f + 3f);
            well.Tube(wa, wa + Vector3.up * 0.9f, 1.2f, 1.2f, 14);
            foreach (float s in new[] { -1f, 1f })
                well.Box(wa + f.Axis(new Vector3(s * 1f, 1.4f, 0f)), new Vector3(0.18f, 2.8f, 0.18f), f.Rot);
            well.Box(wa + Vector3.up * 2.75f, new Vector3(2.4f, 0.18f, 0.18f), f.Rot);
            NoStanding(MeshObject(hamlet, "Well", ToMesh(well, DenseKey("hamletwell")), _rockPaleMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Concrete"));

            for (int k = 0; k < 3; k++)
            {
                var p = f.P(Rand(rng, -hw, hw) * (rng.Next(2) == 0 ? 1.15f : -1.15f), 0f, Rand(rng, -hd, hd));
                BuildPalm(hamlet, layer, rng, new Vector3(p.x, GroundHeightAt(p.x, p.z) - 0.2f, p.z));
            }

            BuildVehicle(hamlet, layer, rng, f.P2(0.6f, Rand(rng, -8f, 8f)), plan.Yaw + Rand(rng, -5f, 5f), VehicleKind.Pickup,
                         _carPaints[rng.Next(_carPaints.Length)]);
        }

        // ---- farms -------------------------------------------------------
        /// <summary>
        /// A walled farm: mud walls round a yard, a gate in the front and another in a side,
        /// a house in the back corner (a third of them open), a lean-to shed with a pickup
        /// or hay under it, a goat pen, a water tank, a palm, and a haystack.
        /// </summary>
        private static void BuildFarm(Transform parent, int layer, int backdrop, System.Random rng, SitePlan plan)
        {
            var farm = new GameObject("Farm").transform;
            farm.SetParent(parent, false);

            float floor = GroundHeightAt(plan.Centre.x, plan.Centre.y);
            var f = new Frame(new Vector3(plan.Centre.x, floor, plan.Centre.y), plan.Yaw);
            float hw = plan.Width * 0.5f, hd = plan.Depth * 0.5f;
            var mat = _adobeTints[rng.Next(_adobeTints.Length)];

            // Packed earth inside.
            var yard = new MeshBuild { UVScale = 0.2f };
            AddUp(yard, f.P(-hw, 0.03f, -hd), f.P(-hw, 0.03f, hd), f.P(hw, 0.03f, hd), f.P(hw, 0.03f, -hd));
            Flat(farm, backdrop, "FarmYard", yard, DenseKey("farmyard"), _earthMat);

            // The wall: two gates on two sides.
            var wall = new MeshBuild { UVScale = 0.28f };
            const float h = 2.4f, t = 0.45f;
            float frontGate = Rand(rng, -hw * 0.1f, hw * 0.35f);
            float sideGate = Rand(rng, -hd * 0.3f, hd * 0.1f);
            float gateSide = rng.Next(2) == 0 ? -1f : 1f;
            WallRun(wall, f, new Vector2(-hw, -hd), new Vector2(hw, -hd), 0f, h, t, new List<Opening> { new Opening(hw + frontGate, 3.4f, 0f, h + 1f) });
            WallRun(wall, f, new Vector2(-hw, hd), new Vector2(hw, hd), 0f, h, t);
            WallRun(wall, f, new Vector2(-hw, -hd + t * 0.5f), new Vector2(-hw, hd - t * 0.5f), 0f, h, t,
                    gateSide < 0 ? new List<Opening> { new Opening(hd + sideGate, 2.6f, 0f, h + 1f) } : null);
            WallRun(wall, f, new Vector2(hw, -hd + t * 0.5f), new Vector2(hw, hd - t * 0.5f), 0f, h, t,
                    gateSide > 0 ? new List<Opening> { new Opening(hd + sideGate, 2.6f, 0f, h + 1f) } : null);
            var wallGo = MeshObject(farm, "FarmWall", ToMesh(wall, DenseKey("farmwall")), mat, Vector3.zero, Quaternion.identity,
                                    Vector3.one, layer, "Concrete");
            NoStanding(wallGo);

            // A coping of flat stones on the wall top.
            var coping = new MeshBuild { UVScale = 0.4f };
            coping.Box(f.P(0f, h + 0.05f, hd), new Vector3(plan.Width + 0.1f, 0.1f, t + 0.1f), f.Rot);
            Visual(farm, "FarmCoping", coping, _plinthMat, layer);

            // The house in the back corner away from the side gate, as an 11 m lot.
            const float lot = 11f;
            float houseU = -gateSide * (hw - t - lot * 0.5f), houseV = hd - t - lot * 0.5f;
            var hf = new Frame(f.P(houseU, 0f, houseV), plan.Yaw + (gateSide > 0 ? 0f : 0f));
            if (rng.NextDouble() < 0.4) BuildEnterableHouse(farm, layer, backdrop, rng, hf, lot);
            else BuildSolidHouse(farm, layer, rng, hf, lot, yard: false);

            // A lean-to shed in the other back corner: posts, a sloping tin roof, and
            // under it a pickup or a stack of hay.
            float shedU = gateSide * (hw - t - 4f), shedV = hd - t - 3.2f;
            var posts = new MeshBuild { UVScale = 0.5f };
            foreach (float a in new[] { -3.6f, 0f, 3.6f })
                posts.Box(f.P(shedU + a, 1.4f, shedV - 2.8f), new Vector3(0.14f, 2.8f, 0.14f), f.Rot);
            NoStanding(MeshObject(farm, "ShedPosts", ToMesh(posts, DenseKey("shedposts")), _timberMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Wood"));
            var tin = new MeshBuild { UVScale = 0.5f };
            var r0 = f.P(shedU - 4f, 2.85f, shedV - 3.1f);
            var r1 = f.P(shedU + 4f, 2.85f, shedV - 3.1f);
            var r2 = f.P(shedU + 4f, 2.35f + 0.9f, shedV + 3.1f);
            var r3 = f.P(shedU - 4f, 2.35f + 0.9f, shedV + 3.1f);
            AddUp(tin, r0, r1, r2, r3);
            AddDown(tin, r0 + Vector3.down * 0.03f, r1 + Vector3.down * 0.03f, r2 + Vector3.down * 0.03f, r3 + Vector3.down * 0.03f);
            var tinGo = MeshObject(farm, "ShedRoof", ToMesh(tin, DenseKey("shedroof")), _corrugatedMat, Vector3.zero,
                                   Quaternion.identity, Vector3.one, layer, null, collider: false);
            NoStanding(tinGo);
            Hide(tinGo);
            if (rng.NextDouble() < 0.55)
                BuildVehicle(farm, layer, rng, f.P2(shedU, shedV - 0.2f), plan.Yaw + 90f, VehicleKind.Pickup, _carPaints[rng.Next(_carPaints.Length)]);
            else
            {
                var hay = new MeshBuild { UVScale = 0.5f };
                for (int i = 0; i < 6; i++)
                    hay.Box(f.P(shedU - 2.4f + (i % 3) * 1.25f, 0.3f + (i / 3) * 0.6f, shedV), new Vector3(1.2f, 0.6f, 0.9f), f.Rot);
                NoStanding(MeshObject(farm, "HayBales", ToMesh(hay, DenseKey("hay")), _grassDryMat, Vector3.zero,
                                      Quaternion.identity, Vector3.one, layer, "Wood"));
            }

            // A goat pen of posts and rails at the front, on the side away from the gate.
            float penU = -Mathf.Sign(frontGate + 0.001f) * (hw - t - 4f), penV = -hd + t + 3.2f;
            var pen = new MeshBuild { UVScale = 0.5f };
            const float pw = 6f, pd = 4.6f;
            for (float s = -pw * 0.5f; s <= pw * 0.5f + 0.01f; s += 1.5f)
                foreach (float side in new[] { -1f, 1f })
                    pen.Box(f.P(penU + s, 0.55f, penV + side * pd * 0.5f), new Vector3(0.1f, 1.1f, 0.1f), f.Rot);
            for (float s = -pd * 0.5f; s <= pd * 0.5f + 0.01f; s += 1.5f)
                pen.Box(f.P(penU + pw * 0.5f, 0.55f, penV + s), new Vector3(0.1f, 1.1f, 0.1f), f.Rot);
            foreach (float y in new[] { 0.45f, 0.95f })
            {
                foreach (float side in new[] { -1f, 1f })
                    pen.Box(f.P(penU, y, penV + side * pd * 0.5f), new Vector3(pw, 0.07f, 0.05f), f.Rot);
                pen.Box(f.P(penU + pw * 0.5f, y, penV), new Vector3(0.05f, 0.07f, pd), f.Rot);
            }
            pen.Box(f.P(penU, 0.25f, penV), new Vector3(1.6f, 0.5f, 0.5f), f.Rot);   // a trough
            NoStanding(MeshObject(farm, "GoatPen", ToMesh(pen, DenseKey("goatpen")), _deadwoodMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Wood"));

            // A water tank on a stand, a haystack, a palm.
            var tank = new MeshBuild { UVScale = 0.5f };
            var ta = f.P(gateSide * (hw - t - 1.6f), 0f, -2f);
            foreach (var o in new[] { new Vector3(-0.6f, 0f, -0.6f), new Vector3(0.6f, 0f, -0.6f), new Vector3(-0.6f, 0f, 0.6f), new Vector3(0.6f, 0f, 0.6f) })
                tank.Box(ta + f.Axis(o) + Vector3.up * 1f, new Vector3(0.12f, 2f, 0.12f), f.Rot);
            tank.Tube(ta + Vector3.up * 2f, ta + Vector3.up * 3.4f, 0.95f, 0.95f, 12);
            NoStanding(MeshObject(farm, "FarmTank", ToMesh(tank, DenseKey("farmtank")), _tankBlackMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Metal"));

            var stack = new MeshBuild { UVScale = 0.5f };
            var sa = f.P(-gateSide * 2f, 0f, -1f);
            stack.Tube(sa, sa + Vector3.up * 1.1f, 1.5f, 1.3f, 10);
            stack.Tube(sa + Vector3.up * 1.1f, sa + Vector3.up * 2.1f, 1.3f, 0.3f, 10);
            NoStanding(MeshObject(farm, "Haystack", ToMesh(stack, DenseKey("haystack")), _grassDryMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Wood"));

            var palmAt = f.P(gateSide * 3f, 0f, 3f);
            BuildPalm(farm, layer, rng, palmAt + Vector3.down * 0.2f);

            // Outside: a stack of firewood and a cart by the front gate.
            var outside = new MeshBuild { UVScale = 0.5f };
            var cart = f.P(frontGate + 3.2f, 0f, -hd - 2f);
            outside.Box(cart + Vector3.up * 0.75f, new Vector3(1.4f, 0.12f, 2.2f), f.Rot);
            outside.Box(cart + Vector3.up * 0.95f + f.Axis(new Vector3(-0.68f, 0f, 0f)), new Vector3(0.06f, 0.4f, 2.2f), f.Rot);
            outside.Box(cart + Vector3.up * 0.95f + f.Axis(new Vector3(0.68f, 0f, 0f)), new Vector3(0.06f, 0.4f, 2.2f), f.Rot);
            outside.Box(cart + Vector3.up * 0.55f + f.Axis(new Vector3(0f, 0f, 1.9f)), new Vector3(0.08f, 0.08f, 1.8f), f.Rot * Quaternion.Euler(-12f, 0f, 0f));
            foreach (float s in new[] { -0.82f, 0.82f })
                Wheel(outside, cart + Vector3.up * 0.5f + f.Axis(new Vector3(s, 0f, -0.2f)), f.Axis(Vector3.right), 0.5f, 0.1f);
            NoStanding(MeshObject(farm, "Cart", ToMesh(outside, DenseKey("cart")), _deadwoodMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Wood"));
        }

        // ---- camps -------------------------------------------------------
        /// <summary>
        /// An abandoned camp on the dunes: a low goat-hair tent sagging on its poles, a fire
        /// ring gone cold, a pot, a few crates, a fence of sticks, and a camel's bones.
        /// </summary>
        private static void BuildCamp(Transform parent, int layer, int backdrop, System.Random rng, Vector2 at)
        {
            var camp = new GameObject("Camp").transform;
            camp.SetParent(parent, false);

            float yaw = Rand(rng, 0f, 360f);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 G(float u, float v)
            {
                var p = at + (Vector2)(Quaternion.Euler(0f, 0f, -yaw) * new Vector2(u, v));
                return new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y);
            }

            // The tent: a long low roof of cloth over two rows of poles, the back wall pegged
            // to the sand, the front open.
            var poles = new MeshBuild { UVScale = 0.5f };
            var cloth = new MeshBuild { UVScale = 0.6f };
            const float tl = 7f, td = 4f;
            var ridge = new List<Vector3>();
            var front = new List<Vector3>();
            var back = new List<Vector3>();
            for (int i = 0; i <= 4; i++)
            {
                float u = -tl * 0.5f + tl * i / 4f;
                var r = G(u, 0f) + Vector3.up * (1.9f - (i % 2) * 0.25f);
                var fr = G(u, -td * 0.5f) + Vector3.up * 1.3f;
                var bk = G(u, td * 0.5f) + Vector3.up * 0.15f;
                ridge.Add(r); front.Add(fr); back.Add(bk);
                poles.Tube(G(u, 0f), r, 0.05f, 0.04f, 5);
                poles.Tube(G(u, -td * 0.5f), fr, 0.04f, 0.035f, 5);
            }
            for (int i = 0; i < 4; i++)
            {
                AddUp(cloth, front[i], front[i + 1], ridge[i + 1], ridge[i]);
                AddDown(cloth, front[i], front[i + 1], ridge[i + 1], ridge[i]);
                AddUp(cloth, ridge[i], ridge[i + 1], back[i + 1], back[i]);
                AddDown(cloth, ridge[i], ridge[i + 1], back[i + 1], back[i]);
            }
            NoStanding(MeshObject(camp, "CampPoles", ToMesh(poles, DenseKey("camppoles")), _deadwoodMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Wood"));
            var clothGo = MeshObject(camp, "CampTent", ToMesh(cloth, DenseKey("camptent")), _tentMat, Vector3.zero,
                                     Quaternion.identity, Vector3.one, layer, null, collider: false);
            NoStanding(clothGo);
            Hide(clothGo);

            // A rug under the tent, a fire ring in front of it with a pot on three stones.
            var rug = new MeshBuild { UVScale = 0.5f };
            AddUp(rug, G(-2.5f, -1.2f) + Vector3.up * 0.04f, G(-2.5f, 1.2f) + Vector3.up * 0.04f,
                  G(2.5f, 1.2f) + Vector3.up * 0.04f, G(2.5f, -1.2f) + Vector3.up * 0.04f);
            Flat(camp, backdrop, "CampRug", rug, DenseKey("camprug"), _clothPaints[rng.Next(_clothPaints.Length)]);

            var stones = new MeshBuild { UVScale = 0.5f };
            var fire = G(0f, -4.5f);
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f;
                stones.Box(fire + new Vector3(Mathf.Cos(a) * 0.8f, 0.1f, Mathf.Sin(a) * 0.8f), new Vector3(0.32f, 0.22f, 0.26f),
                           Quaternion.Euler(0f, a * Mathf.Rad2Deg + Rand(rng, -20f, 20f), 0f));
            }
            Visual(camp, "FireRing", stones, _stoneMat, backdrop);
            var ash = new MeshBuild { UVScale = 0.5f };
            for (int i = 0; i < 10; i++)
            {
                float a0 = i * Mathf.PI * 2f / 10f, a1 = (i + 1) * Mathf.PI * 2f / 10f;
                AddUp(ash, fire + Vector3.up * 0.05f, fire + new Vector3(Mathf.Cos(a0) * 0.65f, 0.05f, Mathf.Sin(a0) * 0.65f),
                      fire + new Vector3(Mathf.Cos(a1) * 0.65f, 0.05f, Mathf.Sin(a1) * 0.65f),
                      fire + new Vector3(Mathf.Cos(a1) * 0.65f, 0.05f, Mathf.Sin(a1) * 0.65f));
            }
            Flat(camp, backdrop, "Ash", ash, DenseKey("ash"), _burntMat);

            var things = new MeshBuild { UVScale = 0.5f };
            things.Tube(fire + Vector3.up * 0.2f, fire + Vector3.up * 0.55f, 0.3f, 0.26f, 10);
            Crate(things, G(3.2f, -2.6f), 0.6f, yaw + 12f);
            Crate(things, G(3.7f, -1.9f), 0.5f, yaw - 20f);
            Drum(things, G(-3.8f, -2.4f));
            NoStanding(MeshObject(camp, "CampThings", ToMesh(things, DenseKey("campthings")), _drumMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Metal"));

            // A pen of sticks for the goats, half fallen.
            var sticks = new MeshBuild { UVScale = 0.5f };
            var penAt = G(-6f, 3f);
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                if (i == 3 || i == 4) continue;   // the way in
                var foot = penAt + new Vector3(Mathf.Cos(a) * 2.6f, 0f, Mathf.Sin(a) * 2.6f);
                foot.y = GroundHeightAt(foot.x, foot.z);
                var lean = new Vector3(Rand(rng, -0.25f, 0.25f), 1f, Rand(rng, -0.25f, 0.25f)).normalized;
                sticks.Tube(foot, foot + lean * Rand(rng, 0.8f, 1.3f), 0.04f, 0.025f, 4);
            }
            Visual(camp, "StickPen", sticks, _deadwoodMat, layer);

            Skeleton(camp, backdrop, rng, G(5f, 5f), Rand(rng, 0f, 360f));
        }

        /// <summary>A camel's bones bleaching on the sand: skull, spine, ribs, leg bones.</summary>
        private static void Skeleton(Transform parent, int layer, System.Random rng, Vector3 at, float yaw)
        {
            var bones = new MeshBuild { UVScale = 0.5f };
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 P(float x, float y, float z) => at + rot * new Vector3(x, y, z);

            bones.Box(P(0f, 0.2f, 1.9f), new Vector3(0.28f, 0.26f, 0.6f), rot * Quaternion.Euler(8f, 0f, 12f));
            bones.Tube(P(0f, 0.25f, 1.5f), P(0f, 0.3f, -1.2f), 0.07f, 0.05f, 5);
            for (int i = 0; i < 7; i++)
            {
                float z = 1.1f - i * 0.28f;
                foreach (float s in new[] { -1f, 1f })
                {
                    var a = P(0f, 0.3f, z);
                    var b = P(s * 0.45f, 0.35f, z - 0.05f);
                    var c = P(s * 0.6f, 0.03f, z - 0.1f);
                    bones.Tube(a, b, 0.025f, 0.02f, 4);
                    bones.Tube(b, c, 0.02f, 0.015f, 4);
                }
            }
            for (int i = 0; i < 3; i++)
                bones.Tube(P(Rand(rng, -1.2f, 1.2f), 0.04f, Rand(rng, -1.6f, 1.6f)), P(Rand(rng, -1.2f, 1.2f), 0.04f, Rand(rng, -1.6f, 1.6f)), 0.04f, 0.035f, 5);
            Visual(parent, "Bones", bones, _boneMat, layer);
        }

        // ---- checkpoints ---------------------------------------------------
        /// <summary>
        /// A checkpoint astride the track: sandbag walls on either side of it, a booth, a
        /// barrier arm, concrete blocks staggered across the road, and a sign.
        /// </summary>
        private static void BuildCheckpoint(Transform parent, int layer, System.Random rng, Vector3 cp)
        {
            var root = new GameObject("Checkpoint").transform;
            root.SetParent(parent, false);

            var at = new Vector2(cp.x, cp.y);
            // Its own frame, turned to the road -- not a quarter-turn, so nothing in it is sealed.
            var f = new Frame(new Vector3(at.x, GroundHeightAt(at.x, at.y), at.y), cp.z);

            var bags = new MeshBuild { UVScale = 0.5f };
            foreach (float side in new[] { -1f, 1f })
            {
                for (float v = -3f; v <= 3f; v += 0.7f)
                {
                    var p = f.P(side * 5f, 0f, v);
                    float g = GroundHeightAt(p.x, p.z);
                    for (int c = 0; c < 4; c++)
                        bags.Box(new Vector3(p.x, g + 0.17f + c * 0.32f, p.z), new Vector3(0.55f, 0.32f, 0.68f), f.Rot);
                }
            }
            NoStanding(MeshObject(root, "CheckpointBags", ToMesh(bags, DenseKey("cpbags")), _sandbagMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Concrete"));

            // The booth: a small plywood hut with a window, on the right of the road.
            var booth = new MeshBuild { UVScale = 0.4f };
            var bp = f.P(7.4f, 0f, 1f);
            float bg = GroundHeightAt(bp.x, bp.z);
            booth.Box(new Vector3(bp.x, bg + 1.2f, bp.z), new Vector3(2.2f, 2.4f, 2.2f), f.Rot);
            booth.Box(new Vector3(bp.x, bg + 2.5f, bp.z), new Vector3(2.6f, 0.12f, 2.6f), f.Rot);
            NoStanding(MeshObject(root, "Booth", ToMesh(booth, DenseKey("booth")), _plywoodMat, Vector3.zero, Quaternion.identity,
                                  Vector3.one, layer, "Wood"));
            var glass = new MeshBuild { UVScale = 0.5f };
            glass.Box(new Vector3(bp.x, bg + 1.5f, bp.z) + f.Axis(new Vector3(-1.12f, 0f, 0f)), new Vector3(0.05f, 0.7f, 1.4f), f.Rot);
            glass.Box(new Vector3(bp.x, bg + 1.05f, bp.z) + f.Axis(new Vector3(0f, 0f, -1.12f)), new Vector3(0.9f, 2.0f, 0.05f), f.Rot);
            Visual(root, "BoothOpenings", glass, _glassDarkMat, layer);

            var fAtGround = new Frame(new Vector3(at.x, GroundHeightAt(at.x, at.y), at.y), cp.z);
            FobBarrier(root, layer, fAtGround, Vector2.zero, Vector3.right, 8f);

            // Concrete blocks staggered across the road beyond it.
            var blocks = new MeshBuild { UVScale = 0.4f };
            foreach (var (u, v) in new[] { (-1.8f, 8f), (1.8f, 13f), (-1.8f, -8f), (1.8f, -13f) })
            {
                var p = f.P(u, 0f, v);
                float g = GroundHeightAt(p.x, p.z);
                blocks.Box(new Vector3(p.x, g + 0.45f, p.z), new Vector3(3.2f, 0.9f, 0.8f), f.Rot);
            }
            NoStanding(MeshObject(root, "RoadBlocks", ToMesh(blocks, DenseKey("roadblocks")), _twallMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Concrete"));

            var sign = new MeshBuild { UVScale = 0.5f };
            var sp = f.P(-6.5f, 0f, -5f);
            float sg = GroundHeightAt(sp.x, sp.z);
            sign.Box(new Vector3(sp.x, sg + 1f, sp.z), new Vector3(0.08f, 2f, 0.08f), f.Rot);
            sign.Box(new Vector3(sp.x, sg + 2.1f, sp.z), new Vector3(1.4f, 0.9f, 0.05f), f.Rot);
            Visual(root, "CheckpointSign", sign, _signMat, layer);

            // Tyres stacked by the booth.
            var tyres = new MeshBuild { UVScale = 0.5f };
            var tp = f.P(7.6f, 0f, -2.2f);
            float tg = GroundHeightAt(tp.x, tp.z);
            for (int i = 0; i < 4; i++)
                tyres.Tube(new Vector3(tp.x, tg + i * 0.26f, tp.z), new Vector3(tp.x, tg + i * 0.26f + 0.24f, tp.z), 0.4f, 0.4f, 12);
            NoStanding(MeshObject(root, "TyreStack", ToMesh(tyres, DenseKey("tyrestack")), _tyreMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Metal"));
        }

        // ---- the power line -------------------------------------------------
        /// <summary>
        /// Poles every thirty-odd metres along the planned line, each moved off anything it
        /// would stand in; a pole with nowhere to stand is left out and its neighbours span
        /// the gap.
        /// </summary>
        private static void BuildPowerLine(Transform parent, int layer, int backdrop)
        {
            if (_poleLine.Count < 2) return;

            var poles = new List<Vector3>();
            for (int i = 0; i + 1 < _poleLine.Count; i++)
            {
                var a = _poleLine[i];
                var b = _poleLine[i + 1];
                float len = Vector2.Distance(a, b);
                int n = Mathf.Max(1, Mathf.RoundToInt(len / 32f));
                for (int k = (i == 0 ? 0 : 1); k <= n; k++)
                {
                    var p = Vector2.Lerp(a, b, k / (float)n);
                    if (!PoleGround(p, out var placed)) continue;
                    poles.Add(new Vector3(placed.x, GroundHeightAt(placed.x, placed.y), placed.y));
                }
            }

            BuildPoleRun(parent, layer, backdrop, poles, 9f);
        }

        /// <summary>Somewhere near <paramref name="p"/> a pole can stand: sand, off the track, clear of solid things.</summary>
        private static bool PoleGround(Vector2 p, out Vector2 placed)
        {
            Physics.SyncTransforms();
            foreach (var nudge in new[] { Vector2.zero, new Vector2(4f, 0f), new Vector2(-4f, 0f), new Vector2(0f, 4f), new Vector2(0f, -4f) })
            {
                var q = p + nudge;
                placed = q;
                if (!Drapeable(q.x, q.y, 4f)) continue;
                if ((q - _townCentre).magnitude < _townRadius - 1f) continue;
                if (_hasFob && _fob.Contains(q, 2.5f)) continue;
                if (NearTrack(q, 4.5f)) continue;

                float g = GroundHeightAt(q.x, q.y);
                bool blocked = false;
                foreach (var hit in Physics.OverlapCapsule(new Vector3(q.x, g + 0.8f, q.y), new Vector3(q.x, g + 9f, q.y), 0.6f, ~0,
                                                           QueryTriggerInteraction.Ignore))
                    if (!hit.CompareTag(SandTag)) { blocked = true; break; }
                if (!blocked) return true;
            }

            placed = p;
            return false;
        }

        private static bool NearTrack(Vector2 p, float within)
        {
            float w2 = within * within;
            foreach (var t in _track)
                if (!float.IsNaN(t.x) && (t - p).sqrMagnitude < w2) return true;
            return false;
        }

        // ---- wadis ----------------------------------------------------------
        /// <summary>
        /// Dry washes: meandering beds of gravel across the dunes, where the rain runs when
        /// it comes. A drape, not a cut -- the ground stays the car's ground -- and the scrub
        /// thickens along them afterwards (see BuildScrub).
        /// </summary>
        private static void BuildWadis(Transform parent, int backdrop, System.Random rng, float half)
        {
            for (int w = 0; w < 4; w++)
            {
                // From an edge, inwards, wandering.
                int side = rng.Next(4);
                var p = side switch
                {
                    0 => new Vector2(-half + 16f, Rand(rng, -half * 0.8f, half * 0.8f)),
                    1 => new Vector2(Rand(rng, -half * 0.8f, 0f), -half + 16f),
                    2 => new Vector2(Rand(rng, -half * 0.8f, 0f), half - 16f),
                    _ => new Vector2(Rand(rng, -half * 0.6f, 0f), Rand(rng, -half * 0.8f, half * 0.8f))
                };
                float heading = side switch { 0 => 0f, 1 => 90f, 2 => -90f, _ => Rand(rng, 0f, 360f) } * Mathf.Deg2Rad;

                var line = new List<Vector2>();
                float turn = 0f;
                for (int s = 0; s < 90; s++)
                {
                    if (!Drapeable(p.x, p.y, 8f) || InSite(p, 8f)) { if (line.Count > 0) break; }
                    else line.Add(p);

                    turn = Mathf.Clamp(turn + Rand(rng, -0.08f, 0.08f), -0.12f, 0.12f);
                    heading += turn;
                    p += new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * 3f;
                }

                if (line.Count < 12) continue;
                _wadis.Add(line);

                var grid = new Vector2[line.Count, 7];
                for (int r = 0; r < line.Count; r++)
                {
                    var ahead = line[Mathf.Min(r + 1, line.Count - 1)] - line[Mathf.Max(r - 1, 0)];
                    var across = new Vector2(-ahead.y, ahead.x).normalized;
                    float width = 4f + 3f * Mathf.PerlinNoise(r * 0.08f, w * 7.1f) * Mathf.Min(1f, r / 6f) * Mathf.Min(1f, (line.Count - r) / 6f);
                    for (int c = 0; c < 7; c++)
                    {
                        float x = c / 6f * 2f - 1f;
                        grid[r, c] = line[r] + across * x * width * (1f + Fbm2(r * 0.2f, c, 7700 + w, 2) * 0.2f);
                    }
                }

                var mesh = DrapeMesh($"Wadi_{w}", grid, (_, across) => Mathf.Lerp(0.05f, -0.1f, Mathf.Abs(across * 2f - 1f)), 0.35f);
                Drape(parent, "Wadi", mesh, _gravelMat, backdrop);

                // Stones in the bed.
                var stones = new MeshBuild { UVScale = 0.5f };
                for (int i = 0; i < line.Count * 2; i++)
                {
                    var q = line[rng.Next(line.Count)] + new Vector2(Rand(rng, -3f, 3f), Rand(rng, -3f, 3f));
                    float sz = Rand(rng, 0.12f, 0.45f);
                    stones.Box(new Vector3(q.x, SurfaceHeightAt(q.x, q.y) + sz * 0.25f, q.y), new Vector3(sz, sz * 0.5f, sz * 0.8f),
                               Quaternion.Euler(Rand(rng, -15f, 15f), Rand(rng, 0f, 360f), Rand(rng, -15f, 15f)));
                }
                Visual(parent, "WadiStones", stones, _stoneMat, backdrop);
            }
        }

        /// <summary>Whether a point is in one of the built-up places the wild things keep out of.</summary>
        private static bool InSite(Vector2 p, float margin)
        {
            if (p.magnitude < 12f + margin) return true;
            if (_hasTown && (p - _townCentre).magnitude < _townRadius + margin) return true;
            if (_hasFob && _fob.Contains(p, margin)) return true;
            foreach (var farm in _farmPlans) if (farm.Contains(p, margin)) return true;
            foreach (var hamlet in _hamletPlans) if (hamlet.Contains(p, margin)) return true;
            foreach (var site in MoreSites()) if (site.Contains(p, margin)) return true;
            foreach (var camp in _campSites) if ((camp - p).magnitude < 9f + margin) return true;
            foreach (var cp in _checkpoints) if ((new Vector2(cp.x, cp.y) - p).magnitude < 11f + margin) return true;
            return false;
        }

        // ---- tyre ruts ----------------------------------------------------------
        /// <summary>Tyre ruts: two dark lines a vehicle's width apart, wandering over the dunes and fading.</summary>
        private static void BuildRuts(Transform parent, int backdrop, System.Random rng, float half)
        {
            var build = new List<Vector2[,]>();
            for (int k = 0; k < 14; k++)
            {
                var p = new Vector2(Rand(rng, -half + 30f, half - 30f), Rand(rng, -half + 30f, half - 30f));
                float heading = Rand(rng, 0f, Mathf.PI * 2f), turn = 0f;
                var line = new List<Vector2>();
                int steps = rng.Next(25, 60);
                for (int s = 0; s < steps; s++)
                {
                    if (!Drapeable(p.x, p.y, 4f) || InSite(p, 2f)) break;
                    line.Add(p);
                    turn = Mathf.Clamp(turn + Rand(rng, -0.05f, 0.05f), -0.08f, 0.08f);
                    heading += turn;
                    p += new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * 2.5f;
                }
                if (line.Count < 8) continue;

                foreach (float side in new[] { -0.85f, 0.85f })
                {
                    var grid = new Vector2[line.Count, 2];
                    for (int r = 0; r < line.Count; r++)
                    {
                        var ahead = line[Mathf.Min(r + 1, line.Count - 1)] - line[Mathf.Max(r - 1, 0)];
                        var across = new Vector2(-ahead.y, ahead.x).normalized;
                        grid[r, 0] = line[r] + across * (side - 0.17f);
                        grid[r, 1] = line[r] + across * (side + 0.17f);
                    }
                    build.Add(grid);
                }
            }

            int i = 0;
            foreach (var grid in build)
            {
                var mesh = DrapeMesh($"Rut_{i++}", grid, (_, __) => 0.025f, 0.5f);
                Drape(parent, "Rut", mesh, _rutMat, backdrop);
            }
        }

        // ==================================================================
        // Scrub, bones and junk -- the very last pass
        // ==================================================================
        /// <summary>
        /// Plants over every open stretch of sand: low round bushes, tufts of dry grass and
        /// the odd dead bush, a few metres apart and thicker along the washes. Merged into one
        /// mesh per material per 60 m block, on the backdrop layer, no colliders.
        /// Then bones and junk: drums, tyres, a scrap of sheet metal, skeletons.
        /// </summary>
        private static void BuildScrub(Transform root, int layer, float half)
        {
            if (!DesertLife) return;

            int backdrop = LayerMask.NameToLayer("Backdrop");
            if (backdrop < 0) backdrop = layer;

            var rng = new System.Random(_theme.randomSeed * 4099 + 17);
            var group = new GameObject("Scrub").transform;
            group.SetParent(root, false);

            const float block = 60f;
            var bushes = new Dictionary<Vector2Int, MeshBuild>();
            var saltbush = new Dictionary<Vector2Int, MeshBuild>();
            var dry = new Dictionary<Vector2Int, MeshBuild>();
            var grass = new Dictionary<Vector2Int, MeshBuild>();
            MeshBuild In(Dictionary<Vector2Int, MeshBuild> d, Vector2 p)
            {
                var key = new Vector2Int(Mathf.FloorToInt(p.x / block), Mathf.FloorToInt(p.y / block));
                if (!d.TryGetValue(key, out var b)) d[key] = b = new MeshBuild { UVScale = 0.8f };
                return b;
            }

            Physics.SyncTransforms();
            int planted = 0;
            const float step = 4.2f;
            for (float x = -half + 8f; x <= half - 8f; x += step)
                for (float z = -half + 8f; z <= half - 8f; z += step)
                {
                    var p = new Vector2(x + Rand(rng, -1.8f, 1.8f), z + Rand(rng, -1.8f, 1.8f));
                    double roll = rng.NextDouble();

                    float nearWadi = float.MaxValue;
                    foreach (var line in _wadis)
                        for (int i = 0; i < line.Count; i += 3)
                            nearWadi = Mathf.Min(nearWadi, (line[i] - p).sqrMagnitude);
                    nearWadi = Mathf.Sqrt(nearWadi);

                    double chance = nearWadi < 5f ? 0.25 : nearWadi < 14f ? 0.8 : 0.34;
                    if (roll > chance) continue;
                    if (!Drapeable(p.x, p.y, 2f) || InSite(p, 1f) || NearTrack(p, 3.8f)) continue;

                    float g = SurfaceHeightAt(p.x, p.y);
                    bool blocked = false;
                    foreach (var hit in Physics.OverlapSphere(new Vector3(p.x, g + 1.2f, p.y), 0.9f, ~0, QueryTriggerInteraction.Ignore))
                        if (!hit.CompareTag(SandTag)) { blocked = true; break; }
                    if (blocked) continue;

                    var at = new Vector3(p.x, g - 0.08f, p.y);
                    int seed = rng.Next(1, 100000);
                    double kind = rng.NextDouble();
                    if (kind < 0.32) Bush(In(bushes, p), at, Rand(rng, 0.4f, 1.0f), seed);
                    else if (kind < 0.58) Bush(In(saltbush, p), at, Rand(rng, 0.35f, 0.85f), seed);
                    else if (kind < 0.93) Tuft(In(grass, p), at, Rand(rng, 0.35f, 0.7f), seed);
                    else DeadBush(In(dry, p), at, Rand(rng, 0.6f, 1.1f), seed);
                    planted++;
                }

            void Emit(Dictionary<Vector2Int, MeshBuild> d, string name, Material mat, bool shadows)
            {
                foreach (var kv in d)
                {
                    if (kv.Value.Triangles.Count == 0) continue;
                    var go = MeshObject(group, name, ToMesh(kv.Value, DenseKey(name)), mat, Vector3.zero, Quaternion.identity,
                                        Vector3.one, backdrop, null, collider: false);
                    Hide(go);
                    if (!shadows) go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            Emit(bushes, "Bushes", _scrubMat, true);
            Emit(saltbush, "Saltbush", _saltbushMat, true);
            Emit(grass, "Grass", _grassDryMat, false);
            Emit(dry, "DeadBushes", _deadwoodMat, false);

            // ---- junk and bones on the open sand ----
            var junk = new MeshBuild { UVScale = 0.5f };
            var tyres = new MeshBuild { UVScale = 0.5f };
            var sheet = new MeshBuild { UVScale = 0.5f };
            int placed = 0;
            for (int i = 0; i < 400 && placed < 46; i++)
            {
                var p = new Vector2(Rand(rng, -half + 20f, half - 20f), Rand(rng, -half + 20f, half - 20f));
                if (!Drapeable(p.x, p.y, 3f) || InSite(p, 2f) || NearTrack(p, 4f)) continue;
                float g = GroundHeightAt(p.x, p.y);
                bool blocked = false;
                foreach (var hit in Physics.OverlapSphere(new Vector3(p.x, g + 1.3f, p.y), 1.1f, ~0, QueryTriggerInteraction.Ignore))
                    if (!hit.CompareTag(SandTag)) { blocked = true; break; }
                if (blocked) continue;

                var at = new Vector3(p.x, g, p.y);
                switch (placed % 5)
                {
                    case 0:
                        Drum(junk, at + Vector3.down * 0.1f);
                        if (rng.NextDouble() < 0.5) Drum(junk, at + new Vector3(0.7f, -0.1f, 0.2f));
                        break;
                    case 1:
                        // A drum on its side, half buried.
                        junk.Tube(at + new Vector3(-0.45f, 0.18f, 0f), at + new Vector3(0.45f, 0.22f, 0f), 0.3f, 0.3f, 10);
                        break;
                    case 2:
                        tyres.Tube(at + Vector3.down * 0.05f, at + Vector3.up * 0.2f, 0.45f, 0.45f, 12);
                        break;
                    case 3:
                        sheet.Box(at + Vector3.up * 0.4f, new Vector3(1.8f, 0.03f, 1.1f),
                                  Quaternion.Euler(Rand(rng, 10f, 35f), Rand(rng, 0f, 360f), Rand(rng, -10f, 10f)));
                        break;
                    default:
                        Skeleton(group, backdrop, rng, at, Rand(rng, 0f, 360f));
                        break;
                }
                placed++;
            }
            NoStanding(MeshObject(group, "Junk", ToMesh(junk, DenseKey("junk")), _drumMat, Vector3.zero, Quaternion.identity,
                                  Vector3.one, layer, "Metal"));
            NoStanding(MeshObject(group, "JunkTyres", ToMesh(tyres, DenseKey("junktyres")), _tyreMat, Vector3.zero, Quaternion.identity,
                                  Vector3.one, layer, "Metal"));
            Hide(MeshObject(group, "ScrapSheets", ToMesh(sheet, DenseKey("scrap")), _rustMat, Vector3.zero, Quaternion.identity,
                            Vector3.one, backdrop, null, collider: false));

            Debug.Log($"[FPSKit] desert scrub: {planted} plant(s), {placed} piece(s) of junk.");
        }

        /// <summary>
        /// A desert shrub: two or three low lumps crowded together rather than one dome --
        /// one smooth dome on the sand reads as a green stone -- each a ragged ring of
        /// foliage round a point, flat-shaded.
        /// </summary>
        private static void Bush(MeshBuild build, Vector3 at, float radius, int seed)
        {
            int lumps = 2 + (int)(Hash01(seed, 90) * 2.99f);
            for (int l = 0; l < lumps; l++)
            {
                float a = Hash01(seed, 91 + l) * Mathf.PI * 2f;
                float off = l == 0 ? 0f : radius * (0.45f + 0.3f * Hash01(seed, 95 + l));
                float r = radius * (l == 0 ? 1f : 0.55f + 0.3f * Hash01(seed, 99 + l));
                Lump(build, at + new Vector3(Mathf.Cos(a) * off, 0f, Mathf.Sin(a) * off), r, seed * 7 + l);
            }
        }

        private static void Lump(MeshBuild build, Vector3 at, float radius, int seed)
        {
            const int spokes = 8;
            float height = radius * (0.65f + Hash01(seed, 1) * 0.45f);
            float[] ringY = { 0f, 0.45f, 0.85f };
            float[] ringR = { 0.8f, 1f, 0.6f };
            var rings = new Vector3[3, spokes];
            float twist = Hash01(seed, 2) * Mathf.PI * 2f;
            for (int k = 0; k < 3; k++)
                for (int i = 0; i < spokes; i++)
                {
                    float a = twist + i * Mathf.PI * 2f / spokes;
                    // Ragged: alternate spokes pushed out, so the edge is twiggy rather than round.
                    float r = radius * ringR[k] * (0.7f + 0.55f * Hash01(seed, 10 + k * spokes + i)) * (i % 2 == 0 ? 1.12f : 0.86f);
                    float y = height * ringY[k] * (0.85f + 0.3f * Hash01(seed, 40 + k * spokes + i));
                    rings[k, i] = at + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                }
            var top = at + Vector3.up * height;

            for (int k = 0; k < 2; k++)
                for (int i = 0; i < spokes; i++)
                {
                    int j = (i + 1) % spokes;
                    build.Quad(rings[k, i], rings[k + 1, i], rings[k + 1, j], rings[k, j]);
                }
            for (int i = 0; i < spokes; i++)
                build.Tri(rings[2, i], top, rings[2, (i + 1) % spokes]);
        }

        /// <summary>A tuft of dry grass: thin blades fanned out from a point, drawn both sides.</summary>
        private static void Tuft(MeshBuild build, Vector3 at, float height, int seed)
        {
            int blades = 7;
            for (int i = 0; i < blades; i++)
            {
                float a = (i + Hash01(seed, i)) * Mathf.PI * 2f / blades;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var side = Vector3.Cross(Vector3.up, dir) * 0.035f;
                var foot = at + dir * 0.05f;
                var tip = at + dir * height * (0.35f + 0.3f * Hash01(seed, 20 + i)) + Vector3.up * height * (0.7f + 0.4f * Hash01(seed, 40 + i));
                build.Tri(foot - side, tip, foot + side);
                build.Tri(foot + side, tip, foot - side);
            }
        }

        /// <summary>A dead bush: a few grey twigs fanning up and out.</summary>
        private static void DeadBush(MeshBuild build, Vector3 at, float height, int seed)
        {
            for (int i = 0; i < 6; i++)
            {
                float a = (i + Hash01(seed, i)) * Mathf.PI * 2f / 6f;
                var tip = at + new Vector3(Mathf.Cos(a) * height * 0.6f, height * (0.6f + 0.4f * Hash01(seed, 9 + i)), Mathf.Sin(a) * height * 0.6f);
                build.Tube(at, tip, 0.03f, 0.01f, 3);
            }
        }

        private static float Hash01(int seed, int n)
        {
            unchecked
            {
                uint h = (uint)seed * 374761393u + (uint)n * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }
    }
}
#endif
