#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The second fill of the desert (2026-09-27). Ishaan, having seen the base, the village
    /// and the farms: "fill more space (use effectively as you did)". Read off a top-down
    /// render, the ground still bare was the strip between the village and the river, the
    /// west edge, most of the far bank and the south edge.
    ///
    /// What goes there is what a river runs through in a desert: date palms in irrigated
    /// rows on the flat ground near the water, fields ploughed beside the farms, a brick
    /// kiln, a walled cemetery by the village, a phone mast -- and, since there is a base,
    /// the places a fight already happened: a burnt-out tank or carrier by a sandbag
    /// trench line.
    ///
    /// Every one is a flattened, claimed site like the farms, so the generic passes after
    /// it (landmarks, outposts, cover lines) and the scrub keep out of it.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static readonly List<SitePlan> _grovePlans = new List<SitePlan>();
        private static readonly List<SitePlan> _fieldPlans = new List<SitePlan>();
        private static readonly List<SitePlan> _kilnPlans = new List<SitePlan>();
        private static readonly List<SitePlan> _cemeteryPlans = new List<SitePlan>();
        private static readonly List<SitePlan> _mastPlans = new List<SitePlan>();
        private static readonly List<SitePlan> _battlePlans = new List<SitePlan>();

        /// <summary>
        /// The height to flatten a new site at: its own dunes' height, but held within
        /// twenty degrees of every pad near it across the sand between them. Two sites a
        /// couple of metres apart, each levelled to its own dune, are two shelves twelve
        /// metres apart with a cliff between -- the claims keep them apart, the blends do not.
        /// </summary>
        private static float SiteHeight(Vector2 centre, float radius)
        {
            float h = NaturalHeightAt(centre.x, centre.y);
            for (int pass = 0; pass < 2; pass++)
                foreach (var pad in _pads)
                {
                    float gap = Vector2.Distance(centre, pad.Centre) - radius - pad.Radius;
                    if (gap > 60f) continue;
                    float other = float.IsNaN(pad.Height) ? NaturalHeightAt(pad.Centre.x, pad.Centre.y) : pad.Height;
                    float room = Mathf.Max(0.5f, gap) * 0.36f;
                    h = Mathf.Clamp(h, other - room, other + room);
                }
            return h;
        }

        private static void ResetDesertMore()
        {
            _grovePlans.Clear();
            _fieldPlans.Clear();
            _kilnPlans.Clear();
            _cemeteryPlans.Clear();
            _mastPlans.Clear();
            _battlePlans.Clear();
        }

        private static IEnumerable<SitePlan> MoreSites()
        {
            foreach (var p in _grovePlans) yield return p;
            foreach (var p in _fieldPlans) yield return p;
            foreach (var p in _kilnPlans) yield return p;
            foreach (var p in _cemeteryPlans) yield return p;
            foreach (var p in _mastPlans) yield return p;
            foreach (var p in _battlePlans) yield return p;
        }

        // ==================================================================
        // Planning
        // ==================================================================
        /// <summary>
        /// Tries to put a site of the given size somewhere that passes <paramref name="where"/>,
        /// clear of the river, the village, the base and every claim so far. Claims it and
        /// flattens it (unless <paramref name="flatten"/> is false).
        /// </summary>
        private static bool TrySite(System.Random rng, float half, float rim, float w, float d, int tries,
                                    System.Func<Vector2, bool> where, List<SitePlan> into, bool flatten = true, float blend = 22f)
        {
            float circum = Mathf.Sqrt(w * w + d * d) * 0.5f;
            for (int i = 0; i < tries; i++)
            {
                var p = new Vector2(Rand(rng, -half + 30f + circum, half - 30f - circum), Rand(rng, -half + 30f + circum, half - 30f - circum));
                if (where != null && !where(p)) continue;
                if (Mathf.Abs(p.x - GorgeCentreAt(p.y)) < rim + circum + 10f) continue;
                if (p.magnitude < 40f + circum) continue;
                if ((p - _townCentre).magnitude < _townRadius + circum + 6f) continue;
                if (_hasFob && _fob.Contains(p, circum + 6f)) continue;
                if (!Free(p, circum + 2f)) continue;

                into.Add(new SitePlan { Centre = p, Width = w, Depth = d, Yaw = rng.Next(4) * 90f });
                Claim(p.x, p.y, circum + 2f);
                if (flatten) FlattenPad(p.x, p.y, circum, blend, SiteHeight(p, circum));
                _anchors.Add(new Vector3(p.x, 0f, p.y));
                return true;
            }
            return false;
        }

        /// <summary>
        /// The big sites first -- groves by the river, the cemetery by the village -- then the
        /// small ones anywhere. Runs before the farms, which then fill round them.
        /// </summary>
        private static void PlanDesertMore(System.Random rng, float half, float rim)
        {
            ResetDesertMore();

            // Groves on the river side: within ninety metres of the rim, where the water table is.
            bool NearRiver(Vector2 p) => Mathf.Abs(p.x - GorgeCentreAt(p.y)) < rim + 95f;
            for (int i = 0; i < 4; i++) TrySite(rng, half, rim, 36f, 24f, 300, NearRiver, _grovePlans);

            // The cemetery just outside the village.
            TrySite(rng, half, rim, 26f, 18f, 400, p => (p - _townCentre).magnitude < _townRadius + 60f, _cemeteryPlans);

            TrySite(rng, half, rim, 30f, 22f, 300, null, _kilnPlans);
            TrySite(rng, half, rim, 14f, 14f, 200, null, _mastPlans);
            for (int i = 0; i < 3; i++) TrySite(rng, half, rim, 26f, 14f, 300, null, _battlePlans, flatten: false);
        }

        /// <summary>A field beside a farm, on whichever side of it is free. Called as each farm is planned.</summary>
        private static void PlanFieldFor(System.Random rng, SitePlan farm)
        {
            var sides = new[] { new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f) };
            int start = rng.Next(4);
            for (int k = 0; k < 4; k++)
            {
                var dir = sides[(start + k) % 4];
                const float w = 30f, d = 22f;
                var h = farm.HalfWorld;
                float reach = Mathf.Abs(dir.x) > 0.5f ? h.x + w * 0.5f + 5f : h.y + d * 0.5f + 5f;
                var p = farm.Centre + dir * reach;
                float circum = Mathf.Sqrt(w * w + d * d) * 0.5f;
                if (Mathf.Abs(p.x) + circum > _theme.arenaSize * 0.5f - 24f || Mathf.Abs(p.y) + circum > _theme.arenaSize * 0.5f - 24f) continue;
                if (!Drapeable(p.x, p.y, circum)) continue;
                if (!Free(p, circum - 2f) && !FreeExcept(p, circum - 2f, farm.Centre)) continue;
                if ((p - _townCentre).magnitude < _townRadius + circum || (_hasFob && _fob.Contains(p, circum))) continue;

                _fieldPlans.Add(new SitePlan { Centre = p, Width = w, Depth = d, Yaw = Mathf.Abs(dir.x) > 0.5f ? 0f : 90f });
                Claim(p.x, p.y, circum - 2f);
                // Level with its farm: flattened at its own height, the two pads a few metres apart
                // are two shelves with a cliff of sand between them.
                FlattenPad(p.x, p.y, circum - 3f, 12f, _pads[_pads.Count - 1].Height);   // the farm's, planned just before
                return;
            }
        }

        /// <summary>Free, ignoring the one claim centred on <paramref name="except"/> (the field's own farm).</summary>
        private static bool FreeExcept(Vector2 point, float radius, Vector2 except)
        {
            foreach (var claim in _claimed)
            {
                if ((new Vector2(claim.x, claim.y) - except).sqrMagnitude < 0.01f) continue;
                float min = claim.z + radius;
                if ((new Vector2(claim.x, claim.y) - point).sqrMagnitude < min * min) return false;
            }
            return true;
        }

        // ==================================================================
        // Building
        // ==================================================================
        private static void BuildDesertMore(Transform parent, int layer, int backdrop, System.Random rng)
        {
            var more = new GameObject("More").transform;
            more.SetParent(parent, false);

            foreach (var p in _grovePlans) BuildGrove(more, layer, backdrop, rng, p);
            foreach (var p in _fieldPlans) BuildField(more, layer, backdrop, rng, p);
            foreach (var p in _kilnPlans) BuildKiln(more, layer, backdrop, rng, p);
            foreach (var p in _cemeteryPlans) BuildCemetery(more, layer, backdrop, rng, p);
            foreach (var p in _mastPlans) BuildMastCompound(more, layer, rng, p);
            foreach (var p in _battlePlans) BuildBattleSite(more, layer, backdrop, rng, p);

            Debug.Log($"[FPSKit] desert more: {_grovePlans.Count} grove(s), {_fieldPlans.Count} field(s), {_kilnPlans.Count} kiln(s), " +
                      $"{_cemeteryPlans.Count} cemetery, {_mastPlans.Count} mast(s), {_battlePlans.Count} battle site(s).");
        }

        private static Frame SiteFrame(SitePlan plan)
            => new Frame(new Vector3(plan.Centre.x, GroundHeightAt(plan.Centre.x, plan.Centre.y), plan.Centre.y), plan.Yaw);

        /// <summary>
        /// A date grove: palms in rows six metres apart, an irrigation ditch down every other
        /// row (dark wet earth), a low mud bund round it with gaps, a pump shed at one end.
        /// The rows are the lanes; the trunks are cover you can see between.
        /// </summary>
        private static void BuildGrove(Transform parent, int layer, int backdrop, System.Random rng, SitePlan plan)
        {
            var grove = new GameObject("Grove").transform;
            grove.SetParent(parent, false);
            var f = SiteFrame(plan);
            float hw = plan.Width * 0.5f, hd = plan.Depth * 0.5f;

            var soil = new MeshBuild { UVScale = 0.25f };
            AddUp(soil, f.P(-hw, 0.03f, -hd), f.P(-hw, 0.03f, hd), f.P(hw, 0.03f, hd), f.P(hw, 0.03f, -hd));
            Flat(grove, backdrop, "GroveSoil", soil, DenseKey("grovesoil"), _earthMat);

            var ditches = new MeshBuild { UVScale = 0.4f };
            int row = 0;
            for (float v = -hd + 3f; v <= hd - 3f; v += 6f, row++)
            {
                for (float u = -hw + 3f; u <= hw - 3f; u += 6f)
                {
                    var p = f.P(u + Rand(rng, -0.6f, 0.6f), 0f, v + Rand(rng, -0.6f, 0.6f));
                    BuildPalm(grove, layer, rng, p + Vector3.down * 0.2f);
                }

                if (row % 2 == 0 && v + 3f < hd)
                    AddUp(ditches, f.P(-hw + 1f, 0.05f, v + 2.7f), f.P(-hw + 1f, 0.05f, v + 3.3f),
                          f.P(hw - 1f, 0.05f, v + 3.3f), f.P(hw - 1f, 0.05f, v + 2.7f));
            }
            Flat(grove, backdrop, "Ditches", ditches, DenseKey("ditches"), _rockWetMat);

            // The bund: a low mud bank round the grove, broken on every side.
            var bund = new MeshBuild { UVScale = 0.3f };
            WallRun(bund, f, new Vector2(-hw - 1f, -hd - 1f), new Vector2(hw + 1f, -hd - 1f), 0f, 0.6f, 0.8f, new List<Opening> { new Opening(hw + 1f, 4f, 0f, 1f) });
            WallRun(bund, f, new Vector2(-hw - 1f, hd + 1f), new Vector2(hw + 1f, hd + 1f), 0f, 0.6f, 0.8f, new List<Opening> { new Opening(hw * 0.6f, 4f, 0f, 1f) });
            WallRun(bund, f, new Vector2(-hw - 1f, -hd - 1f), new Vector2(-hw - 1f, hd + 1f), 0f, 0.6f, 0.8f, new List<Opening> { new Opening(hd + 1f, 4f, 0f, 1f) });
            WallRun(bund, f, new Vector2(hw + 1f, -hd - 1f), new Vector2(hw + 1f, hd + 1f), 0f, 0.6f, 0.8f, new List<Opening> { new Opening(hd * 0.7f, 4f, 0f, 1f) });
            NoStanding(MeshObject(grove, "Bund", ToMesh(bund, DenseKey("bund")), _plinthMat, Vector3.zero, Quaternion.identity,
                                  Vector3.one, layer, "Concrete"));

            // A pump shed and its pipe out to the first ditch.
            var shed = new MeshBuild { UVScale = 0.3f };
            var sp = f.P(-hw - 4.5f, 0f, -hd + 4f);
            shed.Box(sp + Vector3.up * 1.2f, new Vector3(3f, 2.4f, 2.6f), f.Rot);
            shed.Box(sp + Vector3.up * 2.45f, new Vector3(3.4f, 0.1f, 3f), f.Rot * Quaternion.Euler(0f, 0f, 5f));
            NoStanding(MeshObject(grove, "PumpShed", ToMesh(shed, DenseKey("pumpshed")), _adobeTints[rng.Next(_adobeTints.Length)],
                                  Vector3.zero, Quaternion.identity, Vector3.one, layer, "Concrete"));
            var pipe = new MeshBuild { UVScale = 0.5f };
            pipe.Tube(sp + f.Axis(new Vector3(1.5f, 0.3f, 0f)), f.P(-hw + 1f, 0.3f, -hd + 5.7f), 0.12f, 0.12f, 8);
            Visual(grove, "PumpPipe", pipe, _steelMat, layer);
        }

        /// <summary>
        /// A field: ploughed furrows, a crop in some of it (green rows) and stubble in the
        /// rest, a low mud wall round it with a gap, a scarecrow of sticks and rag.
        /// </summary>
        private static void BuildField(Transform parent, int layer, int backdrop, System.Random rng, SitePlan plan)
        {
            var field = new GameObject("Field").transform;
            field.SetParent(parent, false);
            var f = SiteFrame(plan);
            float hw = plan.Width * 0.5f, hd = plan.Depth * 0.5f;

            var soil = new MeshBuild { UVScale = 0.3f };
            AddUp(soil, f.P(-hw, 0.03f, -hd), f.P(-hw, 0.03f, hd), f.P(hw, 0.03f, hd), f.P(hw, 0.03f, -hd));
            Flat(field, backdrop, "FieldSoil", soil, DenseKey("fieldsoil"), _rutMat);

            // Furrows: low ridges along u. Green where planted, straw where cut.
            var green = new MeshBuild { UVScale = 0.5f };
            var straw = new MeshBuild { UVScale = 0.5f };
            float cut = Rand(rng, -hw * 0.4f, hw * 0.4f);
            for (float v = -hd + 1f; v <= hd - 1f; v += 1.1f)
            {
                green.Box(f.P((-hw + 1f + cut) * 0.5f, 0.12f, v), new Vector3(cut - (-hw + 1f), 0.25f, 0.35f), f.Rot);
                straw.Box(f.P((cut + hw - 1f) * 0.5f, 0.06f, v), new Vector3(hw - 1f - cut, 0.12f, 0.3f), f.Rot);
            }
            Flat(field, backdrop, "Crop", green, DenseKey("crop"), _scrubMat);
            Flat(field, backdrop, "Stubble", straw, DenseKey("stubble"), _grassDryMat);

            var wall = new MeshBuild { UVScale = 0.3f };
            float gap = Rand(rng, -hw * 0.5f, hw * 0.5f);
            WallRun(wall, f, new Vector2(-hw, -hd), new Vector2(hw, -hd), 0f, 0.9f, 0.5f, new List<Opening> { new Opening(hw + gap, 3f, 0f, 1f) });
            WallRun(wall, f, new Vector2(-hw, hd), new Vector2(hw, hd), 0f, 0.9f, 0.5f, new List<Opening> { new Opening(hw - gap, 3f, 0f, 1f) });
            WallRun(wall, f, new Vector2(-hw, -hd), new Vector2(-hw, hd), 0f, 0.9f, 0.5f);
            WallRun(wall, f, new Vector2(hw, -hd), new Vector2(hw, hd), 0f, 0.9f, 0.5f);
            NoStanding(MeshObject(field, "FieldWall", ToMesh(wall, DenseKey("fieldwall")), _plinthMat, Vector3.zero, Quaternion.identity,
                                  Vector3.one, layer, "Concrete"));

            var crow = new MeshBuild { UVScale = 0.5f };
            var cp = f.P(cut + 3f, 0f, 0f);
            crow.Tube(cp, cp + Vector3.up * 2f, 0.05f, 0.04f, 5);
            crow.Box(cp + Vector3.up * 1.6f, new Vector3(1.4f, 0.06f, 0.06f), f.Rot);
            Visual(field, "ScarecrowSticks", crow, _deadwoodMat, layer);
            var rag = new MeshBuild { UVScale = 0.5f };
            rag.Box(cp + Vector3.up * 1.35f, new Vector3(0.9f, 0.7f, 0.08f), f.Rot);
            Visual(field, "ScarecrowRag", rag, _clothPaints[rng.Next(_clothPaints.Length)], layer);
        }

        /// <summary>
        /// A brick kiln works: the kiln, a squat block with a tall tapering chimney; rows of
        /// green bricks drying and stacks of fired ones -- waist-high cover in lines -- clay
        /// pits, a shed and a cart.
        /// </summary>
        private static void BuildKiln(Transform parent, int layer, int backdrop, System.Random rng, SitePlan plan)
        {
            var kiln = new GameObject("Kiln").transform;
            kiln.SetParent(parent, false);
            var f = SiteFrame(plan);
            float hw = plan.Width * 0.5f, hd = plan.Depth * 0.5f;

            var yard = new MeshBuild { UVScale = 0.25f };
            AddUp(yard, f.P(-hw, 0.03f, -hd), f.P(-hw, 0.03f, hd), f.P(hw, 0.03f, hd), f.P(hw, 0.03f, -hd));
            Flat(kiln, backdrop, "KilnYard", yard, DenseKey("kilnyard"), _earthMat);

            var bricksMat = _adobeTints[3];
            var block = new MeshBuild { UVScale = 0.3f };
            var kp = f.P(hw - 7f, 0f, hd - 6f);
            block.Box(kp + Vector3.up * 1.6f, new Vector3(9f, 3.2f, 7f), f.Rot);
            block.Tube(kp + f.Axis(new Vector3(2.5f, 0f, 1.5f)) + Vector3.up * 3.2f, kp + f.Axis(new Vector3(2.5f, 0f, 1.5f)) + Vector3.up * 16f, 1.1f, 0.6f, 10);
            var kilnGo = MeshObject(kiln, "KilnBlock", ToMesh(block, DenseKey("kilnblock")), bricksMat, Vector3.zero, Quaternion.identity,
                                    Vector3.one, layer, "Concrete");
            NoStanding(kilnGo);
            Mark(kilnGo, new Color(0.60f, 0.42f, 0.32f), 4);
            SealLocal(kiln, f, hw - 7f, 1.6f, hd - 6f, new Vector3(9f, 3.2f, 7f));
            var mouth = new MeshBuild { UVScale = 0.5f };
            for (float s = -3f; s <= 3f; s += 2f)
                mouth.Box(kp + f.Axis(new Vector3(s, 0.6f, -3.52f)), new Vector3(0.9f, 1.2f, 0.06f), f.Rot);
            Visual(kiln, "KilnMouths", mouth, _burntMat, layer);

            // Brick stacks in rows, lanes between.
            var stacks = new MeshBuild { UVScale = 0.4f };
            for (float u = -hw + 3f; u <= hw - 14f; u += 3.6f)
                for (float v = -hd + 3f; v <= hd - 3f; v += 4.2f)
                {
                    if (rng.NextDouble() < 0.2) continue;
                    float h = Rand(rng, 0.9f, 1.5f);
                    stacks.Box(f.P(u, h * 0.5f, v), new Vector3(1.8f, h, 2.4f), f.Rot);
                }
            NoStanding(MeshObject(kiln, "BrickStacks", ToMesh(stacks, DenseKey("brickstacks")), bricksMat, Vector3.zero,
                                  Quaternion.identity, Vector3.one, layer, "Concrete"));

            // Clay pit and a heap of spoil beside the kiln.
            var pit = new MeshBuild { UVScale = 0.3f };
            var pp = f.P(hw - 6f, 0f, -hd + 5f);
            AddUp(pit, pp + f.Axis(new Vector3(-4f, 0.04f, -3f)), pp + f.Axis(new Vector3(-4f, 0.04f, 3f)),
                  pp + f.Axis(new Vector3(4f, 0.04f, 3f)), pp + f.Axis(new Vector3(4f, 0.04f, -3f)));
            Flat(kiln, backdrop, "ClayPit", pit, DenseKey("claypit"), _rockWetMat);
            var heap = new MeshBuild { UVScale = 0.4f };
            heap.Tube(pp + f.Axis(new Vector3(0f, 0f, 5f)), pp + f.Axis(new Vector3(0f, 0f, 5f)) + Vector3.up * 1.4f, 2.4f, 0.6f, 9);
            NoStanding(MeshObject(kiln, "Spoil", ToMesh(heap, DenseKey("spoil")), _plinthMat, Vector3.zero, Quaternion.identity,
                                  Vector3.one, layer, "Concrete"));

            BuildVehicle(kiln, layer, rng, f.P2(-hw + 4f, hd + 4f), plan.Yaw + 90f, VehicleKind.Truck, _carPaints[rng.Next(_carPaints.Length)]);
        }

        /// <summary>
        /// A cemetery: a low wall with a gate in two sides, rows of headstones -- slabs and
        /// low mounds -- and a small domed tomb in one corner.
        /// </summary>
        private static void BuildCemetery(Transform parent, int layer, int backdrop, System.Random rng, SitePlan plan)
        {
            var cem = new GameObject("Cemetery").transform;
            cem.SetParent(parent, false);
            var f = SiteFrame(plan);
            float hw = plan.Width * 0.5f, hd = plan.Depth * 0.5f;

            var wall = new MeshBuild { UVScale = 0.3f };
            WallRun(wall, f, new Vector2(-hw, -hd), new Vector2(hw, -hd), 0f, 1.3f, 0.4f, new List<Opening> { new Opening(hw, 2.4f, 0f, 2f) });
            WallRun(wall, f, new Vector2(-hw, hd), new Vector2(hw, hd), 0f, 1.3f, 0.4f);
            WallRun(wall, f, new Vector2(-hw, -hd), new Vector2(-hw, hd), 0f, 1.3f, 0.4f, new List<Opening> { new Opening(hd, 2.2f, 0f, 2f) });
            WallRun(wall, f, new Vector2(hw, -hd), new Vector2(hw, hd), 0f, 1.3f, 0.4f);
            NoStanding(MeshObject(cem, "CemeteryWall", ToMesh(wall, DenseKey("cemwall")), _adobeTints[1], Vector3.zero, Quaternion.identity,
                                  Vector3.one, layer, "Concrete"));

            var stones = new MeshBuild { UVScale = 0.5f };
            var mounds = new MeshBuild { UVScale = 0.5f };
            for (float u = -hw + 2.5f; u <= hw - 7f; u += 1.8f)
                for (float v = -hd + 3f; v <= hd - 2f; v += 3f)
                {
                    if (Mathf.Abs(u) < 1.6f || rng.NextDouble() < 0.15) continue;   // the path in from the gate
                    var p = f.P(u, 0f, v);
                    stones.Box(p + Vector3.up * 0.4f + f.Axis(new Vector3(0f, 0f, 0.9f)), new Vector3(0.45f, 0.8f, 0.12f),
                               f.Rot * Quaternion.Euler(Rand(rng, -6f, 6f), Rand(rng, -5f, 5f), Rand(rng, -5f, 5f)));
                    mounds.Box(p + Vector3.up * 0.12f, new Vector3(0.7f, 0.24f, 1.7f), f.Rot);
                }
            Visual(cem, "Headstones", stones, _stoneMat, layer);
            Visual(cem, "Graves", mounds, _plinthMat, backdrop);

            // The tomb: a small cube with a dome, in the far corner.
            var tomb = new MeshBuild { UVScale = 0.3f };
            var tp = f.P(hw - 3.5f, 0f, hd - 3.5f);
            tomb.Box(tp + Vector3.up * 1.6f, new Vector3(4f, 3.2f, 4f), f.Rot);
            for (int i = 0; i < 5; i++)
            {
                float a0 = i * Mathf.PI * 0.5f / 5, a1 = (i + 1) * Mathf.PI * 0.5f / 5;
                tomb.Tube(tp + Vector3.up * (3.2f + Mathf.Sin(a0) * 1.8f), tp + Vector3.up * (3.2f + Mathf.Sin(a1) * 1.8f),
                          Mathf.Max(0.05f, Mathf.Cos(a0) * 1.8f), Mathf.Max(0.05f, Mathf.Cos(a1) * 1.8f), 14);
            }
            NoStanding(MeshObject(cem, "Tomb", ToMesh(tomb, DenseKey("tomb")), _dishMat, Vector3.zero, Quaternion.identity,
                                  Vector3.one, layer, "Concrete"));
            var door = new MeshBuild { UVScale = 0.5f };
            door.Box(tp + f.Axis(new Vector3(0f, 1f, -2.03f)), new Vector3(1f, 2f, 0.06f), f.Rot);
            Visual(cem, "TombDoor", door, _doorPaints[1], layer);

            for (int i = 0; i < 2; i++)
            {
                var p = f.P(Rand(rng, -hw + 2f, hw - 8f), 0f, hd + Rand(rng, 1.5f, 3f));
                BuildPalm(cem, layer, rng, p + Vector3.down * 0.2f);
            }
        }

        /// <summary>A phone mast: a lattice tower in a fenced square, an equipment cabinet and a generator.</summary>
        private static void BuildMastCompound(Transform parent, int layer, System.Random rng, SitePlan plan)
        {
            var root = new GameObject("PhoneMast").transform;
            root.SetParent(parent, false);
            var f = SiteFrame(plan);
            float hw = plan.Width * 0.5f - 1f;

            var tower = new MeshBuild { UVScale = 0.5f };
            const float h = 32f;
            var legs = new[] { new Vector3(-1.6f, 0f, -1.6f), new Vector3(1.6f, 0f, -1.6f), new Vector3(1.6f, 0f, 1.6f), new Vector3(-1.6f, 0f, 1.6f) };
            for (int i = 0; i < 4; i++)
            {
                var a = f.P(legs[i].x, 0f, legs[i].z);
                var b = f.P(legs[i].x * 0.2f, h, legs[i].z * 0.2f);
                tower.Tube(a, b, 0.1f, 0.06f, 5);
                var a2 = f.P(legs[(i + 1) % 4].x, 0f, legs[(i + 1) % 4].z);
                var b2 = f.P(legs[(i + 1) % 4].x * 0.2f, h, legs[(i + 1) % 4].z * 0.2f);
                for (int k = 0; k < 8; k++)
                {
                    float t0 = k / 8f, t1 = (k + 1) / 8f;
                    tower.Tube(Vector3.Lerp(a, b, t0), Vector3.Lerp(a2, b2, t1), 0.035f, 0.035f, 4);
                    tower.Tube(Vector3.Lerp(a2, b2, t0), Vector3.Lerp(a, b, t1), 0.035f, 0.035f, 4);
                }
            }
            // Antenna panels near the top.
            for (int i = 0; i < 3; i++)
            {
                float ang = i * 120f;
                var dir = f.Axis(Quaternion.Euler(0f, ang, 0f) * Vector3.forward);
                tower.Box(f.P(0f, h - 2f, 0f) + dir * 0.9f, new Vector3(0.35f, 1.6f, 0.12f), Quaternion.LookRotation(dir));
            }
            var towerGo = MeshObject(root, "MastTower", ToMesh(tower, DenseKey("masttower")), _dishMat, Vector3.zero, Quaternion.identity,
                                     Vector3.one, layer, "Metal");
            NoStanding(towerGo);
            Mark(towerGo, new Color(0.8f, 0.8f, 0.8f), 5);

            // Fence: posts and three wires, a gate gap.
            var fence = new MeshBuild { UVScale = 0.5f };
            foreach (var (a, b, gap) in new[] { (new Vector2(-hw, -hw), new Vector2(hw, -hw), true), (new Vector2(hw, -hw), new Vector2(hw, hw), false),
                                                (new Vector2(hw, hw), new Vector2(-hw, hw), false), (new Vector2(-hw, hw), new Vector2(-hw, -hw), true) })
            {
                float len = Vector2.Distance(a, b);
                for (float s = 0f; s <= len + 0.01f; s += 2.5f)
                {
                    var q = Vector2.Lerp(a, b, s / len);
                    if (gap && Mathf.Abs(s - len * 0.5f) < 1.6f) continue;
                    fence.Box(f.P(q.x, 1f, q.y), new Vector3(0.07f, 2f, 0.07f), f.Rot);
                }
                foreach (float y in new[] { 0.6f, 1.2f, 1.8f })
                {
                    if (gap)
                    {
                        var m0 = Vector2.Lerp(a, b, 0.5f - 1.6f / len);
                        var m1 = Vector2.Lerp(a, b, 0.5f + 1.6f / len);
                        fence.Tube(f.P(a.x, y, a.y), f.P(m0.x, y, m0.y), 0.012f, 0.012f, 3);
                        fence.Tube(f.P(m1.x, y, m1.y), f.P(b.x, y, b.y), 0.012f, 0.012f, 3);
                    }
                    else fence.Tube(f.P(a.x, y, a.y), f.P(b.x, y, b.y), 0.012f, 0.012f, 3);
                }
            }
            Visual(root, "MastFence", fence, _steelMat, layer);

            var kit = new MeshBuild { UVScale = 0.5f };
            kit.Box(f.P(hw - 2.5f, 0.9f, 0f), new Vector3(1.2f, 1.8f, 2.4f), f.Rot);
            kit.Box(f.P(hw - 2.5f, 0.1f, -3.5f), new Vector3(1.4f, 0.2f, 2.4f), f.Rot);
            kit.Box(f.P(hw - 2.5f, 0.85f, -3.5f), new Vector3(1.2f, 1.3f, 2.2f), f.Rot);
            NoStanding(MeshObject(root, "MastKit", ToMesh(kit, DenseKey("mastkit")), _acMat, Vector3.zero, Quaternion.identity,
                                  Vector3.one, layer, "Metal"));
        }

        /// <summary>
        /// Where a fight already happened: a sandbag trench line in a zigzag across the site,
        /// a burnt-out tank or armoured carrier beside it, scorched sand, spent shell cases
        /// and a helmet or two. The trench is a line of low walls, not a cut -- the ground
        /// stays the car's ground.
        /// </summary>
        private static void BuildBattleSite(Transform parent, int layer, int backdrop, System.Random rng, SitePlan plan)
        {
            var site = new GameObject("BattleSite").transform;
            site.SetParent(parent, false);
            float yaw = plan.Yaw + Rand(rng, -20f, 20f);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 G(float u, float v)
            {
                var p = plan.Centre + (Vector2)(Quaternion.Euler(0f, 0f, -yaw) * new Vector2(u, v));
                return new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y);
            }

            // The trench line: zigzag bays of sandbags, gaps between bays.
            var bags = new MeshBuild { UVScale = 0.5f };
            float hw = plan.Width * 0.5f;
            for (int bay = 0; bay < 5; bay++)
            {
                float u0 = -hw + bay * (plan.Width / 5f) + 0.8f, u1 = u0 + plan.Width / 5f - 1.6f;
                float v = (bay % 2 == 0 ? -1f : 1f) * 1.2f + 4f;
                for (float u = u0; u < u1; u += 0.7f)
                {
                    var p = G(u, v);
                    for (int c = 0; c < 3; c++)
                        bags.Box(p + Vector3.up * (0.17f + c * 0.32f), new Vector3(0.68f, 0.32f, 0.55f), rot);
                }
            }
            NoStanding(MeshObject(site, "TrenchLine", ToMesh(bags, DenseKey("trench")), _sandbagMat, Vector3.zero, Quaternion.identity,
                                  Vector3.one, layer, "Concrete"));

            // The wreck.
            var hull = new MeshBuild { UVScale = 0.4f };
            var tracks = new MeshBuild { UVScale = 0.5f };
            bool tank = rng.NextDouble() < 0.6;
            var wp = G(Rand(rng, -4f, 4f), -5f);
            float wy = LowestGroundIn(wp.x, wp.z, 3.5f) - 0.2f;
            var wrot = Quaternion.Euler(Rand(rng, -3f, 3f), yaw + Rand(rng, -40f, 40f), Rand(rng, -6f, 6f));
            hull.Box(new Vector3(0f, 1.2f, 0f), new Vector3(3.4f, 1.1f, 7f), Quaternion.identity);
            hull.Box(new Vector3(0f, 1.0f, 3.6f), new Vector3(3.2f, 0.7f, 0.8f), Quaternion.Euler(30f, 0f, 0f));
            foreach (float s in new[] { -1.85f, 1.85f })
                tracks.Box(new Vector3(s, 0.55f, 0f), new Vector3(0.6f, 1.1f, 7.2f), Quaternion.identity);
            if (tank)
            {
                // Turret knocked round, gun drooping.
                var tr = Quaternion.Euler(0f, Rand(rng, 40f, 140f), 0f);
                hull.Box(new Vector3(0.3f, 2.15f, -0.4f), new Vector3(2.4f, 0.8f, 3f), tr);
                hull.Tube(new Vector3(0.3f, 2.2f, -0.4f) + tr * new Vector3(0f, 0f, 1.4f), new Vector3(0.3f, 2.2f, -0.4f) + tr * new Vector3(0f, -0.5f, 5.4f), 0.13f, 0.1f, 8);
            }
            else hull.Box(new Vector3(0f, 2.05f, -1f), new Vector3(3f, 0.7f, 4.4f), Quaternion.identity);
            var hullGo = MeshObject(site, "WreckHull", ToMesh(hull, DenseKey("wreckhull")), _burntMat, new Vector3(wp.x, wy, wp.z), wrot,
                                    Vector3.one, layer, "Metal");
            NoStanding(hullGo);
            Mark(hullGo, new Color(0.25f, 0.22f, 0.2f), 3);
            NoStanding(MeshObject(site, "WreckTracks", ToMesh(tracks, DenseKey("wrecktracks")), _tyreMat, new Vector3(wp.x, wy, wp.z), wrot,
                                  Vector3.one, layer, "Metal"));
            Drape(site, "Scorch", BlobMesh("Scorch", new Vector2(wp.x, wp.z), 7f, 7801 + rng.Next(100), 0.05f, 1f, 1.3f, yaw * Mathf.Deg2Rad),
                  _burntMat, backdrop);

            // Brass, helmets, an ammo box, scattered behind the line.
            var litter = new MeshBuild { UVScale = 0.5f };
            for (int i = 0; i < 20; i++)
            {
                var p = G(Rand(rng, -hw, hw), Rand(rng, 5.5f, 8f));
                litter.Tube(p, p + new Vector3(Rand(rng, -0.1f, 0.1f), 0.03f, 0.12f), 0.02f, 0.02f, 4);
            }
            for (int i = 0; i < 2; i++)
            {
                var p = G(Rand(rng, -hw, hw), Rand(rng, 5f, 7f));
                litter.Tube(p, p + Vector3.up * 0.16f, 0.16f, 0.12f, 8);
            }
            Visual(site, "BattleLitter", litter, _clothSaffron, backdrop);
        }
    }
}
#endif
