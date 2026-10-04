#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The mountain end of Snowbound (2026-10-04; see FPSKitSnowFull.cs): a ski lift climbing
    /// away from the lodge, a mine driven through a bluff, a pass between two cliffs with a
    /// checkpoint in it, cabins scattered over the snow, expedition camps, and -- last, in the
    /// gaps -- the small wild things (cairns, dead trees, buried snowcats, sleds, fences).
    ///
    /// The wild pass asks the physics scene what is empty (<see cref="BareSnow"/>) instead of
    /// claiming sites, as the desert's scrub does: claim circles cover the map on paper long
    /// before the ground is actually full.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        // ==================================================================
        // Ski lift
        // ==================================================================
        private static void PlanSkiLift(System.Random rng, float half)
        {
            var origin = _lodge.Centre;
            for (int i = 0; i < 300 && !_hasLift; i++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f), len = Rand(rng, 105f, 160f);
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var start = origin + dir * 42f;
                var end = start + dir * len;
                if (Mathf.Abs(end.x) > half - 42f || Mathf.Abs(end.y) > half - 42f) continue;
                if (Mathf.Abs(start.x) > half - 42f || Mathf.Abs(start.y) > half - 42f) continue;
                if (!Free(start, 14f) || !Free(end, 14f)) continue;

                int n = Mathf.Max(3, Mathf.RoundToInt(len / 28f));
                var pylons = new List<Vector2>();
                bool ok = true;
                for (int k = 1; k < n && ok; k++)
                {
                    var q = Vector2.Lerp(start, end, k / (float)n);
                    if (!Free(q, 5f)) ok = false;
                    pylons.Add(q);
                }
                if (!ok) continue;

                float yaw = Mathf.Round(Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg / 90f) * 90f;
                _liftBottom = new SitePlan { Centre = start, Width = 10f, Depth = 8f, Yaw = yaw };
                _liftTop = new SitePlan { Centre = end, Width = 10f, Depth = 8f, Yaw = yaw + 180f };
                foreach (var s in new[] { start, end })
                {
                    Claim(s.x, s.y, 12f);
                    FlattenPad(s.x, s.y, 7f, 14f, SiteHeight(s, 7f));
                    _anchors.Add(new Vector3(s.x, 0f, s.y));
                    _snowSites.Add(new Vector3(s.x, s.y, 16f));
                }
                foreach (var q in pylons) { Claim(q.x, q.y, 3.5f); _snowSites.Add(new Vector3(q.x, q.y, 5f)); }
                _liftPylons.AddRange(pylons);
                _hasLift = true;
            }
        }

        private static void BuildSkiLift(Transform parent, int layer, System.Random rng)
        {
            var lift = new GameObject("SkiLift").transform;
            lift.SetParent(parent, false);

            var points = new List<Vector3>();
            var tops = new List<Vector3>();

            // Stations at each end: a block with a roof, a drive wheel on its face.
            foreach (var plan in new[] { _liftBottom, _liftTop })
            {
                var f = SiteFrame(plan);
                var shell = new MeshBuild { UVScale = 0.3f };
                shell.SoftBox(f.P(0f, 2.6f, 0f), new Vector3(7f, 5.2f, 5.2f), f.Rot, 61, 0.15f, 3, 0.01f);
                Solid(lift, "LiftStation", shell, _logDarkMat, layer, "Wood");
                SealLocal(lift, f, 0f, 2.6f, 0f, new Vector3(7f, 5.2f, 5.2f));
                SnowFooting(lift, layer, f, 0f, 0f, 7f, 5.2f);
                var roof = new MeshBuild { UVScale = 0.4f };
                var snow = new MeshBuild { UVScale = 0.4f };
                GableRoof(roof, snow, f, 0f, 0f, 7f, 5.2f, 5.2f, 0.35f, 0.8f);
                RoofObjects(lift, layer, roof, snow, _slateMat);
                var wheel = new MeshBuild { UVScale = 0.5f };
                wheel.Tube(f.P(0f, 4.4f, -2.7f), f.P(0f, 4.4f, -3.3f), 1.5f, 1.5f, 16);
                Solid(lift, "DriveWheel", wheel, _ironMat, layer, "Metal");
                var at = f.P(0f, 5.9f, 0f);
                points.Add(at);
                tops.Add(at);
            }

            // Pylons: a tapered mast with a cross-arm turned square to the line.
            var line = _liftTop.Centre - _liftBottom.Centre;
            var across = new Vector3(-line.y, 0f, line.x).normalized;
            var masts = new MeshBuild { UVScale = 0.5f };
            var inner = new List<Vector3>();
            foreach (var q in _liftPylons)
            {
                float g = GroundHeightAt(q.x, q.y);
                var foot = new Vector3(q.x, g - 0.4f, q.y);
                var top = new Vector3(q.x, g + 9.2f, q.y);
                masts.Tube(foot, top, 0.28f, 0.16f, 8);
                masts.Box(top + Vector3.down * 0.3f, new Vector3(0.2f, 0.2f, 3.2f), Quaternion.LookRotation(across));
                inner.Add(top + Vector3.up * 0.1f);
            }
            Solid(lift, "Pylons", masts, _ironMat, layer, "Metal");

            // The cable: a polyline from station to station over each pylon top, sagging between, and a chair
            // hung from it every fourteen metres.
            var all = new List<Vector3> { tops[0] };
            all.AddRange(inner);
            all.Add(tops[1]);
            var cable = new MeshBuild { UVScale = 0.5f };
            var chairs = new MeshBuild { UVScale = 0.5f };
            float carry = 0f;
            for (int i = 0; i + 1 < all.Count; i++)
            {
                var a = all[i];
                var b = all[i + 1];
                const int Seg = 6;
                Vector3 Sag(float t) => Vector3.Lerp(a, b, t) + Vector3.down * Mathf.Sin(t * Mathf.PI) * 0.7f;
                for (int s = 0; s < Seg; s++)
                    cable.Tube(Sag(s / (float)Seg), Sag((s + 1) / (float)Seg), 0.035f, 0.035f, 3);

                float span = (b - a).magnitude;
                for (float d = carry; d < span; d += 14f)
                {
                    var p = Sag(d / span);
                    var dir = (b - a).normalized;
                    var rot = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z) == Vector3.zero ? Vector3.forward : new Vector3(dir.x, 0f, dir.z));
                    chairs.Tube(p, p + Vector3.down * 2.1f, 0.03f, 0.03f, 3);
                    chairs.Box(p + Vector3.down * 2.3f + rot * new Vector3(0f, 0f, 0f), new Vector3(1.5f, 0.1f, 0.5f), rot);
                    chairs.Box(p + Vector3.down * 1.9f + rot * new Vector3(0f, 0f, -0.2f), new Vector3(1.5f, 0.7f, 0.08f), rot);
                    carry = d + 14f - span;
                }
                if (carry < 0f) carry = 0f;
            }
            Visual(lift, "LiftCable", cable, _ironMat, layer);
            Visual(lift, "LiftChairs", chairs, _moduleRed, layer);
        }

        /// <summary>
        /// Boulders half-sunk into the outside of a rock box (outer side, top, ends), so its silhouette
        /// is craggy rather than a grey block. <paramref name="portalU"/> keeps the tunnel mouths clear.
        /// </summary>
        private static void RockDress(Transform parent, int layer, System.Random rng, Frame f, Vector3 c, Vector3 size,
                                      int count, float width, float portalU)
        {
            var h = size * 0.5f;
            float sgn = c.x >= 0f ? 1f : -1f;
            for (int i = 0; i < count; i++)
            {
                int face = rng.Next(3);
                Vector3 p, outward;
                if (face == 0)
                {
                    p = new Vector3(c.x + sgn * h.x, c.y + Rand(rng, -h.y * 0.7f, h.y * 0.8f), c.z + Rand(rng, -h.z, h.z));
                    outward = new Vector3(sgn, 0f, 0f);
                }
                else if (face == 1)
                {
                    p = new Vector3(c.x + Rand(rng, -h.x, h.x), c.y + h.y, c.z + Rand(rng, -h.z, h.z));
                    outward = Vector3.up;
                }
                else
                {
                    float e = rng.Next(2) * 2f - 1f;
                    p = new Vector3(c.x + Rand(rng, -h.x, h.x), c.y + Rand(rng, -h.y * 0.5f, h.y * 0.8f), c.z + e * h.z);
                    outward = new Vector3(0f, 0f, e);
                    if (Mathf.Abs(p.x) < portalU) continue;
                }

                float w = Rand(rng, width * 0.7f, width * 1.2f);
                float sc = w / BoulderSpread;
                var world = f.P(p.x, p.y, p.z) + f.Axis(outward) * w * 0.14f;
                var go = MeshObject(parent, "CragRock", BoulderMesh(rng.Next(1, 999), Rand(rng, 0.3f, 0.46f), Rand(rng, 0.6f, 0.9f)),
                                    _mountainRock, world, Quaternion.Euler(Rand(rng, -12f, 12f), Rand(rng, 0f, 360f), Rand(rng, -12f, 12f)),
                                    new Vector3(sc, sc * Rand(rng, 0.8f, 1.35f), sc * Rand(rng, 0.8f, 1.2f)), layer, "Concrete");
                NoStanding(go);
            }
        }

        // ==================================================================
        // The mine
        // ==================================================================
        /// <summary>
        /// A bluff with a tunnel driven straight through it, open at both ends: rock masses either
        /// side and over the top, timber portals, rails and ore carts on the floor, a lamp at each
        /// mouth; and outside, a headframe, an ore bin and a hoist shed. The floor of the tunnel is
        /// the terrain, flattened for it.
        /// </summary>
        private static void BuildMine(Transform parent, int layer, int backdrop, System.Random rng, SitePlan plan)
        {
            var mine = new GameObject("Mine").transform;
            mine.SetParent(parent, false);
            var f = SiteFrame(plan);

            const float tw = 4.6f, th = 4.8f, len = 18f;
            var rock = new MeshBuild { UVScale = 0.2f };
            float sideW = 7f;
            rock.SoftBox(f.P(-(tw * 0.5f + sideW * 0.5f), 5.2f, 0f), new Vector3(sideW, 11f, len), f.Rot, 301, 1.3f, 5, 0.15f);
            rock.SoftBox(f.P(tw * 0.5f + sideW * 0.5f, 5.2f, 0f), new Vector3(sideW, 11f, len), f.Rot, 302, 1.3f, 5, 0.15f);
            rock.SoftBox(f.P(0f, th + 3.2f, 0f), new Vector3(tw + sideW * 2f, 6.4f, len), f.Rot, 303, 1.3f, 5, 0.15f);
            var rockGo = Solid(mine, "Bluff", rock, _mountainRock, layer, "Concrete");
            _ = rockGo;

            RockDress(mine, layer, rng, f, new Vector3(-(tw * 0.5f + sideW * 0.5f), 5.2f, 0f), new Vector3(sideW, 11f, len), 12, 6f, tw * 0.5f + 1.2f);
            RockDress(mine, layer, rng, f, new Vector3(tw * 0.5f + sideW * 0.5f, 5.2f, 0f), new Vector3(sideW, 11f, len), 12, 6f, tw * 0.5f + 1.2f);
            RockDress(mine, layer, rng, f, new Vector3(0f, th + 3.2f, 0f), new Vector3(tw + sideW * 2f, 6.4f, len), 8, 7f, tw * 0.5f + 1.2f);

            // The rock either side of the tunnel is solid: no navmesh under it (the tunnel itself stays open).
            SealLocal(mine, f, -(tw * 0.5f + sideW * 0.5f), 5f, 0f, new Vector3(sideW, 10f, len));
            SealLocal(mine, f, tw * 0.5f + sideW * 0.5f, 5f, 0f, new Vector3(sideW, 10f, len));

            var capped = new MeshBuild { UVScale = 0.3f };
            capped.SoftBox(f.P(0f, th + 6.5f, 0f), new Vector3(tw + sideW * 2f - 1.2f, 1.1f, len - 1.2f), f.Rot, 304, 0.5f, 4, 0.1f);
            SnowCap(mine, "BluffSnow", capped, _pineSnowMat, layer);

            // Portals at each end: two posts, a lintel, a pair of braces.
            var frames = new MeshBuild { UVScale = 0.5f };
            foreach (float sv in new[] { -1f, 1f })
            {
                float v = sv * (len * 0.5f + 0.15f);
                foreach (float su in new[] { -1f, 1f })
                {
                    frames.Box(f.P(su * (tw * 0.5f - 0.2f), th * 0.5f, v), new Vector3(0.5f, th, 0.5f), f.Rot);
                    frames.Box(f.P(su * (tw * 0.5f - 0.9f), th - 0.7f, v), new Vector3(0.18f, 1.6f, 0.18f), f.Rot * Quaternion.Euler(0f, 0f, su * 45f));
                }
                frames.Box(f.P(0f, th - 0.1f, v), new Vector3(tw + 0.5f, 0.5f, 0.55f), f.Rot);
                // Intermediate timber sets down the tunnel, like a real one.
            }
            for (float v = -len * 0.5f + 3f; v < len * 0.5f - 1f; v += 4.5f)
            {
                foreach (float su in new[] { -1f, 1f })
                    frames.Box(f.P(su * (tw * 0.5f - 0.1f), th * 0.5f, v), new Vector3(0.3f, th, 0.3f), f.Rot);
                frames.Box(f.P(0f, th - 0.1f, v), new Vector3(tw, 0.3f, 0.3f), f.Rot);
            }
            Visual(mine, "MineTimbers", frames, _logDarkMat, layer);

            // Rails and sleepers (no collider: the player runs over them), two ore carts for cover.
            var rails = new MeshBuild { UVScale = 0.5f };
            foreach (float su in new[] { -0.6f, 0.6f })
                rails.Box(f.P(su, 0.1f, 0f), f.WorldSize(new Vector3(0.1f, 0.1f, len + 10f)), Quaternion.identity);
            for (float v = -len * 0.5f - 4f; v < len * 0.5f + 5f; v += 0.9f)
                rails.Box(f.P(0f, 0.04f, v), f.WorldSize(new Vector3(1.8f, 0.08f, 0.2f)), Quaternion.identity);
            Visual(mine, "MineRails", rails, _ironMat, layer);

            var carts = new MeshBuild { UVScale = 0.4f };
            foreach (float v in new[] { -3f, 4.5f })
            {
                carts.Box(f.P(0f, 0.9f, v), f.WorldSize(new Vector3(1.4f, 0.9f, 2.2f)), Quaternion.identity);
                foreach (float su in new[] { -0.7f, 0.7f })
                    carts.Tube(f.P(su - 0.06f, 0.35f, v), f.P(su + 0.06f, 0.35f, v), 0.3f, 0.3f, 8);
            }
            Solid(mine, "OreCarts", carts, _ironMat, layer, "Metal");

            // Outside: the headframe, an ore bin, a hoist shed.
            var tower = new MeshBuild { UVScale = 0.5f };
            var at = new Vector2(-13f, 7f);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    var foot = f.P(at.x + sx * 2f, 0f, at.y + sz * 2f);
                    tower.Tube(foot + Vector3.down * 0.3f, f.P(at.x + sx * 0.5f, 11f, at.y + sz * 0.5f), 0.18f, 0.12f, 6);
                }
            for (float y = 2.5f; y < 10f; y += 2.5f)
            {
                float r = 2f - y * 0.136f;
                tower.Box(f.P(at.x, y, at.y), new Vector3(r * 2f, 0.12f, r * 2f), f.Rot);
            }
            tower.Tube(f.P(at.x - 0.5f, 11.2f, at.y), f.P(at.x + 0.5f, 11.2f, at.y), 0.9f, 0.9f, 14);
            Solid(mine, "Headframe", tower, _logDarkMat, layer, "Wood");
            SealLocal(mine, f, at.x, 3f, at.y, new Vector3(4.2f, 6f, 4.2f));

            var bin = new MeshBuild { UVScale = 0.4f };
            bin.Box(f.P(10f, 3.2f, 7f), f.WorldSize(new Vector3(4f, 2.2f, 4f)), Quaternion.identity);
            foreach (float sx in new[] { -1.6f, 1.6f })
                foreach (float sz in new[] { -1.6f, 1.6f })
                    bin.Box(f.P(10f + sx, 1.1f, 7f + sz), new Vector3(0.3f, 2.2f, 0.3f), f.Rot);
            Solid(mine, "OreBin", bin, _logMat, layer, "Wood");
            OpenShed(mine, layer, new Frame(f.P(10f, 0f, -8f), f.Yaw + 90f), 7f, 5f, 3.4f, _logMat, _slateMat);

            var heap = MeshObject(mine, "OrePile", BoulderMesh(71, 0.3f, 0.5f), _mountainRock, f.P(-8f, 0.2f, -9f), Quaternion.identity,
                                  new Vector3(3.4f, 1.6f, 3.0f), layer, "Concrete");
            NoStanding(heap);
        }

        // ==================================================================
        // The pass
        // ==================================================================
        /// <summary>
        /// Two cliffs with a twelve-metre gap between them down the middle -- a way through the
        /// mountains the map has to be fought through -- with a log barrier, sandbags and a guard
        /// hut at one end and boulders fallen at the foot of both.
        /// </summary>
        private static void BuildPass(Transform parent, int layer, int backdrop, System.Random rng, SitePlan plan)
        {
            var pass = new GameObject("Pass").transform;
            pass.SetParent(parent, false);
            var f = SiteFrame(plan);

            var cliffs = new MeshBuild { UVScale = 0.2f };
            foreach (float s in new[] { -1f, 1f })
            {
                cliffs.SoftBox(f.P(s * 12.5f, 7.5f, 0f), new Vector3(12f, 16f, 20f), f.Rot, 400 + (int)s, 2.2f, 5, 0.25f);
                cliffs.SoftBox(f.P(s * 9.6f, 4.5f, 8.5f), new Vector3(7f, 10f, 9f), f.Rot, 410 + (int)s, 1.8f, 4, 0.22f);
                cliffs.SoftBox(f.P(s * 10.2f, 4f, -8.5f), new Vector3(8f, 9f, 8f), f.Rot, 420 + (int)s, 1.8f, 4, 0.22f);
            }
            Solid(pass, "Cliffs", cliffs, _mountainRock, layer, "Concrete");
            foreach (float s in new[] { -1f, 1f })
            {
                RockDress(pass, layer, rng, f, new Vector3(s * 12.5f, 7.5f, 0f), new Vector3(12f, 16f, 20f), 22, 8f, 0f);
                RockDress(pass, layer, rng, f, new Vector3(s * 9.6f, 4.5f, 8.5f), new Vector3(7f, 10f, 9f), 6, 5f, 0f);
                RockDress(pass, layer, rng, f, new Vector3(s * 10.2f, 4f, -8.5f), new Vector3(8f, 9f, 8f), 6, 5f, 0f);
            }
            foreach (float s in new[] { -1f, 1f })
            {
                SealLocal(pass, f, s * 12.5f, 8f, 0f, new Vector3(12f, 16f, 20f));
                SealLocal(pass, f, s * 9.6f, 5f, 8.5f, new Vector3(7f, 10f, 9f));
                SealLocal(pass, f, s * 10.2f, 4.5f, -8.5f, new Vector3(8f, 9f, 8f));
            }
            var snow = new MeshBuild { UVScale = 0.3f };
            foreach (float s in new[] { -1f, 1f })
                snow.SoftBox(f.P(s * 12.5f, 15.9f, 0f), new Vector3(10.8f, 1.4f, 18.6f), f.Rot, 430 + (int)s, 0.6f, 4, 0.12f);
            SnowCap(pass, "CliffSnow", snow, _pineSnowMat, layer);

            // The barrier: posts either side, a counterweighted arm raised, sandbags on one side, a lamp.
            var wood = new MeshBuild { UVScale = 0.5f };
            var bags = new MeshBuild { UVScale = 0.5f };
            foreach (float s in new[] { -1f, 1f })
                wood.Box(f.P(s * 3.2f, 1.1f, -12.5f), new Vector3(0.4f, 2.2f, 0.4f), f.Rot);
            wood.Box(f.P(-1.5f, 3.2f, -12.5f), f.WorldSize(new Vector3(6f, 0.18f, 0.18f)), Quaternion.identity * Quaternion.Euler(0f, 0f, 0f));
            wood.Box(f.P(-3.2f, 2.9f, -12.5f), new Vector3(0.5f, 0.5f, 0.5f), f.Rot);
            for (int i = 0; i < 7; i++)
                for (int r = 0; r < 2; r++)
                    bags.SoftBox(f.P(-5.5f + i * 0.62f + r * 0.3f, 0.22f + r * 0.4f, -14f), new Vector3(0.6f, 0.38f, 0.42f), f.Rot, rng.Next(999), 0.12f, 2, 0.02f);
            Solid(pass, "Barrier", wood, _logDarkMat, layer, "Wood");
            Solid(pass, "PassSandbags", bags, _alpCanvas, layer, "Wood");

            // A guard hut on the near side.
            BuildEnterableChalet(pass, layer, backdrop, rng, new Frame(f.P(-12f, 0f, -17f), f.Yaw), 12f, flatRoof: false, wIn: 5.6f, dIn: 6.4f);

            // Fallen rock at the foot of both cliffs.
            for (int i = 0; i < 9; i++)
            {
                float s = i % 2 == 0 ? -1f : 1f;
                var p = f.P(s * Rand(rng, 6.9f, 9f), 0f, Rand(rng, -9f, 9f));
                Boulder(pass, layer, rng, p, Rand(rng, 1.4f, 3.2f));
            }
        }

        // ==================================================================
        // Cabins and camps
        // ==================================================================
        private static void BuildAlpCabin(Transform parent, int layer, int backdrop, System.Random rng, SitePlan plan)
        {
            var f = SiteFrame(plan);
            double roll = rng.NextDouble();
            if (roll < 0.45) BuildEnterableChalet(parent, layer, backdrop, rng, f, 13f, flatRoof: roll < 0.12);
            else BuildSolidChalet(parent, layer, rng, f, 13f, rng.NextDouble() < 0.4);

            var wood = new MeshBuild { UVScale = 0.5f };
            Woodpile(wood, f.P(-5.5f, 0f, -5f), f.Rot, 3, 2.4f);
            if (rng.NextDouble() < 0.6) Sled(wood, f.P(5.5f, 0f, -5.5f), f.Yaw + Rand(rng, -30f, 30f));
            Solid(parent, "CabinYard", wood, _logMat, layer, "Wood");
            double vroll = rng.NextDouble();
            if (vroll < 0.35) BuildSnowmobile(parent, layer, rng, f.P(-5.5f, 0f, 5.5f), f.Yaw + Rand(rng, 0f, 360f));
            else if (vroll < 0.6) BuildPickup(parent, layer, rng, f.P(0f, 0f, -6.4f), f.Yaw + 90f);
        }

        /// <summary>An A-frame tent, ridge along v: canvas slopes and ends, closed at the back.</summary>
        private static void Tent(MeshBuild canvas, Frame f, Vector2 at, float w, float d, float h)
        {
            var inside = f.P(at.x, h * 0.35f, at.y);
            Vector3 L0 = f.P(at.x - w * 0.5f, 0f, at.y - d * 0.5f), L1 = f.P(at.x - w * 0.5f, 0f, at.y + d * 0.5f);
            Vector3 R0 = f.P(at.x + w * 0.5f, 0f, at.y - d * 0.5f), R1 = f.P(at.x + w * 0.5f, 0f, at.y + d * 0.5f);
            Vector3 T0 = f.P(at.x, h, at.y - d * 0.5f), T1 = f.P(at.x, h, at.y + d * 0.5f);
            AddOutward(canvas, inside, L0, T0, T1); AddOutward(canvas, inside, L0, T1, L1);
            AddOutward(canvas, inside, R0, R1, T1); AddOutward(canvas, inside, R0, T1, T0);
            AddOutward(canvas, inside, L0, R0, T0); AddOutward(canvas, inside, L1, T1, R1);
        }

        private static void BuildHunterCamp(Transform parent, int layer, System.Random rng, SitePlan plan)
        {
            var camp = new GameObject("Camp").transform;
            camp.SetParent(parent, false);
            var f = SiteFrame(plan);

            for (int i = 0; i < 2; i++)
            {
                var canvas = new MeshBuild { UVScale = 0.4f };
                Tent(canvas, f, new Vector2(-4.4f + i * 8.8f, 3f), 3.2f, 4.6f, 2.3f);
                var go = Solid(camp, "Tent", canvas, _tentPaints[rng.Next(_tentPaints.Length)], layer, "Wood");
                _ = go;
            }

            // Fire ring, logs to sit on, an ash bed.
            var stones = new MeshBuild { UVScale = 0.4f };
            var logs = new MeshBuild { UVScale = 0.5f };
            var ash = new MeshBuild { UVScale = 0.5f };
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f;
                stones.SoftBox(f.P(Mathf.Cos(a) * 0.9f, 0.14f, -1.2f + Mathf.Sin(a) * 0.9f), new Vector3(0.42f, 0.3f, 0.36f),
                               Quaternion.Euler(0f, a * 57f, 0f), 11 + i, 0.1f, 2, 0.04f);
            }
            for (int i = 0; i < 3; i++)
            {
                float a = i * 2.1f + 0.4f;
                logs.Box(f.P(Mathf.Cos(a) * 2.4f, 0.22f, -1.2f + Mathf.Sin(a) * 2.4f), new Vector3(2.0f, 0.4f, 0.4f),
                         f.Rot * Quaternion.Euler(0f, -a * 57.3f + 90f, 0f));
            }
            AddUp(ash, f.P(-0.7f, 0.03f, -1.9f), f.P(-0.7f, 0.03f, -0.5f), f.P(0.7f, 0.03f, -0.5f), f.P(0.7f, 0.03f, -1.9f));
            Solid(camp, "FireStones", stones, _mountainRock, layer, "Concrete");
            Solid(camp, "CampLogs", logs, _logMat, layer, "Wood");
            var ashGo = new MeshBuild { UVScale = 0.5f };
            ashGo.Tube(f.P(0f, 0.02f, -1.2f), f.P(0f, 0.06f, -1.2f), 0.7f, 0.7f, 10);
            Visual(camp, "Ash", ashGo, _barkMat, layer);

            // A sled with crates, a drying rack, a flag.
            var wood = new MeshBuild { UVScale = 0.5f };
            Sled(wood, f.P(0f, 0f, 5.2f), f.Yaw + 95f);
            BuildSnowmobile(camp, layer, rng, f.P(4.5f, 0f, -1.5f), f.Yaw + Rand(rng, 0f, 360f));
            foreach (float s in new[] { -1f, 1f })
                wood.Box(f.P(-1.5f + s * 1.2f, 1.2f, -5.2f), new Vector3(0.1f, 2.4f, 0.1f), f.Rot);
            wood.Box(f.P(-1.5f, 2.3f, -5.2f), new Vector3(2.6f, 0.1f, 0.1f), f.Rot);
            wood.Tube(f.P(5.5f, 0f, -4f), f.P(5.5f, 4.5f, -4f), 0.06f, 0.04f, 5);
            Solid(camp, "CampWood", wood, _plankMat, layer, "Wood");
            var flag = new MeshBuild { UVScale = 0.5f };
            flag.Box(f.P(6.1f, 4.0f, -4f), new Vector3(1.2f, 0.7f, 0.04f), f.Rot);
            Visual(camp, "Flag", flag, _tentPaints[0], layer);
            var crates = new MeshBuild { UVScale = 0.5f };
            Crate(crates, f.P(0.8f, 0.4f, 5.8f), 0.9f, f.Yaw + 12f);
            Crate(crates, f.P(-0.4f, 0.4f, 6.3f), 0.8f, f.Yaw - 20f);
            Solid(camp, "CampCrates", crates, _plankMat, layer, "Wood");
        }

        // ==================================================================
        // Wild detail
        // ==================================================================
        /// <summary>
        /// What is left lying about on open snow: cairns, dead trees, half-buried snowcats, a sled
        /// and its load, a length of fence going nowhere, a boulder group at walking size. Placed
        /// wherever the physics scene says the ground is bare, until the budget or the map runs out.
        /// </summary>
        private static void BuildSnowWild(Transform parent, int layer, System.Random rng, float half)
        {
            var wild = new GameObject("Wild").transform;
            wild.SetParent(parent, false);

            var chunks = new Dictionary<(int kind, Vector2Int cell), MeshBuild>();
            MeshBuild Builds(int kind, Vector2 p)
            {
                var key = (kind, new Vector2Int(Mathf.FloorToInt(p.x / 70f), Mathf.FloorToInt(p.y / 70f)));
                if (!chunks.TryGetValue(key, out var b)) b = chunks[key] = new MeshBuild { UVScale = 0.5f };
                return b;
            }

            int cairns = 0, snags = 0, wrecks = 0, sleds = 0, fences = 0, rocks = 0, drifts = 0;
            const int Budget = 150;
            for (int i = 0, placed = 0; i < 1400 && placed < Budget; i++)
            {
                var p = new Vector2(Rand(rng, -half + 28f, half - 28f), Rand(rng, -half + 28f, half - 28f));
                if (p.magnitude < 28f || !Free(p, 3.2f) || InCrevasse(p.x, p.y, 7f) || !BareSnow(p, 1.7f)) continue;

                float g = GroundHeightAt(p.x, p.y);
                float roll = Rand(rng, 0f, 1f);
                if (roll < 0.20)
                {
                    // Cairn: stacked stones, each smaller.
                    var b = Builds(0, p);
                    for (int s = 0; s < 5; s++)
                        b.SoftBox(new Vector3(p.x + Rand(rng, -0.1f, 0.1f), g + 0.2f + s * 0.38f, p.y + Rand(rng, -0.1f, 0.1f)),
                                  new Vector3(1.0f - s * 0.14f, 0.4f, 0.9f - s * 0.12f), Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f),
                                  rng.Next(999), 0.14f, 2, 0.04f);
                    cairns++;
                }
                else if (roll < 0.40)
                {
                    // A dead tree: trunk, bare limbs.
                    var b = Builds(1, p);
                    float h = Rand(rng, 4f, 7.5f);
                    var foot = new Vector3(p.x, g - 0.3f, p.y);
                    b.Tube(foot, foot + Vector3.up * h, 0.24f, 0.05f, 6);
                    int limbs = 4 + rng.Next(3);
                    for (int l = 0; l < limbs; l++)
                    {
                        float y = h * Rand(rng, 0.35f, 0.9f);
                        float a = Rand(rng, 0f, Mathf.PI * 2f), reach = Rand(rng, 1.2f, 2.4f) * (1.2f - y / h * 0.6f);
                        var from = foot + Vector3.up * y;
                        b.Tube(from, from + new Vector3(Mathf.Cos(a) * reach, reach * 0.6f, Mathf.Sin(a) * reach), 0.07f, 0.025f, 4);
                    }
                    snags++;
                }
                else if (roll < 0.50)
                {
                    // A snowcat sunk to its tracks, with a drift over its tail.
                    if (rng.NextDouble() < 0.5) Snowcat(wild, layer, rng, new Vector3(p.x, g - 0.9f, p.y), Rand(rng, 0f, 360f));
                    else BuildPickup(wild, layer, rng, new Vector3(p.x, g - 0.55f, p.y), Rand(rng, 0f, 360f));
                    var drift = MeshObject(wild, "WreckDrift", BoulderMesh(rng.Next(1, 40), 0.12f, 0.45f), _pineSnowMat,
                                           new Vector3(p.x, g, p.y) + Vector3.up * 0.1f, Quaternion.identity, new Vector3(4.2f, 1.4f, 3.2f), layer, null, collider: false);
                    NoStanding(drift); Hide(drift);
                    Claim(p.x, p.y, 4f);
                    wrecks++; placed++;
                    continue;
                }
                else if (roll < 0.62)
                {
                    // A sled with a load.
                    var b = Builds(2, p);
                    float yaw = Rand(rng, 0f, 360f);
                    Sled(b, new Vector3(p.x, g, p.y), yaw);
                    Crate(b, new Vector3(p.x, g + 0.5f, p.y), 0.8f, yaw + 8f);
                    sleds++;
                }
                else if (roll < 0.74)
                {
                    // A length of fence, four panels, half down at one end.
                    var b = Builds(3, p);
                    float yaw = Rand(rng, 0f, 360f);
                    var f0 = new Frame(new Vector3(p.x, g, p.y), yaw);
                    RailFence(b, f0, new Vector2(-4.4f, 0f), new Vector2(4.4f, 0f), 1.3f);
                    Claim(p.x, p.y, 3.2f);
                    fences++;
                }
                else if (roll < 0.88)
                {
                    // Rocks at walking size: cover, not scenery.
                    int n = 2 + rng.Next(2);
                    for (int r = 0; r < n; r++)
                        Boulder(wild, layer, rng, new Vector3(p.x + Rand(rng, -2.2f, 2.2f), 0f, p.y + Rand(rng, -2.2f, 2.2f)), Rand(rng, 1.4f, 3.4f));
                    rocks++;
                }
                else
                {
                    var drift = MeshObject(wild, "Drift", BoulderMesh(rng.Next(1, 40), 0.1f, 0.4f), _pineSnowMat,
                                           new Vector3(p.x, g + 0.05f, p.y), Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f),
                                           new Vector3(Rand(rng, 4f, 7f), Rand(rng, 0.9f, 1.6f), Rand(rng, 2.4f, 4f)), layer, null, collider: false);
                    NoStanding(drift); Hide(drift);
                    drifts++;
                }
                Claim(p.x, p.y, 2.6f);
                placed++;
            }

            foreach (var kv in chunks)
            {
                var mat = kv.Key.kind switch { 0 => _mountainRock, 1 => _barkMat, 2 => _plankMat, _ => _logMat };
                string tag = kv.Key.kind == 0 ? "Concrete" : "Wood";
                Solid(wild, "WildPieces", kv.Value, mat, layer, tag);
            }

            Debug.Log($"[FPSKit] snow wild: {cairns} cairn(s), {snags} dead tree(s), {wrecks} wreck(s), {sleds} sled(s), " +
                      $"{fences} fence(s), {rocks} rock group(s), {drifts} drift(s).");
        }

        /// <summary>The whole of the full pass: called from BuildSnowLife after the station, before the forest.</summary>
        private static void BuildSnowFull(Transform parent, int layer, int backdrop, float half)
        {
            if (!_theme.snowZone) return;
            ResolveSnowFullMaterials();

            var rng = new System.Random(_theme.randomSeed * 3203 + 5);
            var group = new GameObject("Alpine").transform;
            group.SetParent(parent, false);

            if (_hasAlpVillage) BuildAlpVillage(group, layer, backdrop, rng);
            if (_hasOutpost) BuildOutpost(group, layer, backdrop, rng, _outpost);
            if (_hasLodge) BuildLodge(group, layer, backdrop, rng, _lodge);
            if (_hasLift) BuildSkiLift(group, layer, rng);
            if (_hasMine) BuildMine(group, layer, backdrop, rng, _mine);
            if (_hasPass) BuildPass(group, layer, backdrop, rng, _pass);
            foreach (var cabin in _alpCabins) BuildAlpCabin(group, layer, backdrop, rng, cabin);
            foreach (var camp in _alpCamps) BuildHunterCamp(group, layer, rng, camp);
            BuildSnowWild(group, layer, rng, half);
        }
    }
}
#endif
