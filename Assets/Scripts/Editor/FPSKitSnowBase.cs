#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Snowbound's outpost and its lodge (2026-10-04; see FPSKitSnowFull.cs).
    ///
    /// <b>The outpost is a fixed plan in its own frame</b>, like the fairground and the desert's
    /// base: a log palisade round a 76 by 54 yard with two gates on two sides, lanes from each
    /// gate, and buildings packed against the wall with the lane widths left between them. A
    /// hangar with a helicopter in it, two barracks, a mess hall with a walkable roof, a
    /// generator yard, a snowcat depot, a radio shed and mast, and a block of containers in the
    /// middle to fight round. Nothing in it is random-placed; the lanes are the point.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        // ==================================================================
        // The outpost
        // ==================================================================
        private static void BuildOutpost(Transform parent, int layer, int backdrop, System.Random rng, SitePlan plan)
        {
            var post = new GameObject("Outpost").transform;
            post.SetParent(parent, false);

            float floor = GroundHeightAt(plan.Centre.x, plan.Centre.y);
            var f = new Frame(new Vector3(plan.Centre.x, floor, plan.Centre.y), plan.Yaw);
            const float hu = 37.5f, hv = 26.5f;

            // ---- the palisade, with a gate at the front (-v) and one in the +u side ----
            var wall = new MeshBuild { UVScale = 0.3f };
            var tops = new MeshBuild { UVScale = 0.5f };
            var gateFront = new List<Opening> { new Opening(hu - 12f, 8f, 0f, 5.2f) };       // from the -u corner
            var gateSide = new List<Opening> { new Opening(hv - 10f, 8f, 0f, 5.2f) };        // from the -v corner
            Palisade(wall, tops, f, new Vector2(-hu, -hv), new Vector2(hu, -hv), gateFront);
            Palisade(wall, tops, f, new Vector2(-hu, hv), new Vector2(hu, hv), null);
            Palisade(wall, tops, f, new Vector2(-hu, -hv), new Vector2(-hu, hv), null);
            Palisade(wall, tops, f, new Vector2(hu, -hv), new Vector2(hu, hv), gateSide);
            Solid(post, "Palisade", wall, _logDarkMat, layer, "Wood");
            Solid(post, "PalisadeTops", tops, _logMat, layer, "Wood");

            // Gate posts and a lintel beam over each gate, so a gate reads as one from the lane.
            var frames = new MeshBuild { UVScale = 0.5f };
            void GateFrame(Vector2 at, bool alongU)
            {
                var across = alongU ? new Vector3(1f, 0f, 0f) : new Vector3(0f, 0f, 1f);
                foreach (float s in new[] { -1f, 1f })
                {
                    var p = f.P(at.x + (alongU ? s * 4.4f : 0f), 3.2f, at.y + (alongU ? 0f : s * 4.4f));
                    frames.Box(p, new Vector3(0.7f, 6.4f, 0.7f), f.Rot);
                }
                frames.Box(f.P(at.x, 5.9f, at.y), alongU ? new Vector3(9.6f, 0.6f, 0.7f) : new Vector3(0.7f, 0.6f, 9.6f), f.Rot);
                _ = across;
            }
            GateFrame(new Vector2(-12f, -hv), true);
            GateFrame(new Vector2(hu, -10f), false);
            Solid(post, "GateFrames", frames, _logMat, layer, "Wood");

            // Watchtowers in the four corners: stilts, a cab, a hipped roof. Decoration -- nothing stands up there.
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    OutpostTower(post, layer, f, new Vector2(sx * (hu - 2.2f), sz * (hv - 2.2f)));

            // ---- south side: mess hall (walkable roof), generator yard, depot ----
            {
                var mess = new Frame(f.P(-26f, 0f, -14f), f.Yaw);
                BuildEnterableChalet(post, layer, backdrop, rng, mess, 12f, flatRoof: true, wIn: 7.4f, dIn: 11.5f);
            }
            GeneratorYard(post, layer, rng, f, new Vector2(-1f, -17.5f));
            SnowcatDepot(post, layer, rng, new Frame(f.P(24f, 0f, -21f), f.Yaw + 180f));

            // Trucks and a pickup in the lanes; snowmobiles by the fuel.
            BuildUtilityTruck(post, layer, rng, f.P(-13.4f, 0f, -9f), f.Yaw + 2f);
            BuildUtilityTruck(post, layer, rng, f.P(-13.4f, 0f, 18f), f.Yaw + 178f);
            BuildPickup(post, layer, rng, f.P(21f, 0f, -2f), f.Yaw + 90f);
            BuildPickup(post, layer, rng, f.P(-4f, 0f, -26.5f + 9.5f), f.Yaw + 180f);
            BuildSnowmobile(post, layer, rng, f.P(32f, 0f, -17f), f.Yaw + 70f);
            BuildSnowmobile(post, layer, rng, f.P(34f, 0f, -14.5f), f.Yaw + 95f);

            // ---- north side: hangar, two barracks ----
            Hangar(post, layer, rng, new Frame(f.P(-25f, 0f, 12f), f.Yaw));
            BuildEnterableChalet(post, layer, backdrop, rng, new Frame(f.P(14f, 0f, 15f), f.Yaw), 12f, flatRoof: false, wIn: 7.4f, dIn: 20f);
            BuildEnterableChalet(post, layer, backdrop, rng, new Frame(f.P(28f, 0f, 15f), f.Yaw), 12f, flatRoof: false, wIn: 7.4f, dIn: 20f);

            // ---- the middle: radio shed and mast, a block of containers, fuel ----
            BuildEnterableChalet(post, layer, backdrop, rng, new Frame(f.P(8f, 0f, -3f), f.Yaw + 90f), 12f, flatRoof: false, wIn: 5.6f, dIn: 7.5f);
            RadioMast(post, layer, f.P(-3f, 0f, 3f));
            ContainerBlock(post, layer, rng, f);
            FuelFarm(post, layer, f, new Vector2(33f, 2f));

            // Sandbag lines and supply crates in the lanes' corners, so no lane is a bare corridor.
            var bags = new MeshBuild { UVScale = 0.5f };
            var crates = new MeshBuild { UVScale = 0.5f };
            var barrels = new MeshBuild { UVScale = 0.5f };
            foreach (var at in new[] { new Vector2(-8f, -22f), new Vector2(-16f, 2f), new Vector2(19f, -9f), new Vector2(34f, -22f), new Vector2(-33f, -2f) })
            {
                for (int i = 0; i < 6; i++)
                    for (int r = 0; r < 2; r++)
                        bags.SoftBox(f.P(at.x + i * 0.62f + r * 0.3f, 0.22f + r * 0.4f, at.y), new Vector3(0.6f, 0.38f, 0.42f), f.Rot, rng.Next(999), 0.12f, 2, 0.02f);
                Crate(crates, f.P(at.x + 0.6f, 0f, at.y + 1.2f), 1.0f, f.Yaw + Rand(rng, -15f, 15f));
                Crate(crates, f.P(at.x + 1.8f, 0f, at.y + 1.3f), 1.0f, f.Yaw + Rand(rng, -15f, 15f));
                var d = f.P(at.x + 3f, 0f, at.y + 1.0f);
                barrels.Tube(d, d + Vector3.up * 0.95f, 0.3f, 0.3f, 8);
            }
            Solid(post, "Sandbags", bags, _alpCanvas, layer, "Wood");
            Solid(post, "Crates", crates, _plankMat, layer, "Wood");
            Solid(post, "Barrels", barrels, _ironMat, layer, "Metal");

            Debug.Log($"[FPSKit] snow outpost at {plan.Centre}, yaw {plan.Yaw}.");
        }

        /// <summary>Palisade run between two local points: a wall to the height of the logs, and a row of pointed log tops.</summary>
        private static void Palisade(MeshBuild wall, MeshBuild tops, Frame f, Vector2 a, Vector2 b, List<Opening> openings)
        {
            WallRun(wall, f, a, b, 0f, 3.6f, 0.5f, openings);
            var d = b - a;
            float length = d.magnitude;
            var dir = d / length;
            var rot = f.Rot * Quaternion.Euler(0f, Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg, 0f);
            float gate0 = -1f, gate1 = -1f;
            if (openings != null && openings.Count > 0) { gate0 = openings[0].At - openings[0].Width * 0.5f; gate1 = openings[0].At + openings[0].Width * 0.5f; }
            for (float s = 0.3f; s < length - 0.2f; s += 0.62f)
            {
                if (s > gate0 && s < gate1) continue;
                float jitter = ((Mathf.RoundToInt(s * 31f) % 5) - 2) * 0.12f;
                var p = a + dir * s;
                tops.Box(f.P(p.x, 3.6f + jitter * 0.5f + 0.35f, p.y), new Vector3(0.34f, 0.9f + jitter, 0.34f), rot);
            }
        }

        private static void OutpostTower(Transform parent, int layer, Frame f, Vector2 at)
        {
            var legs = new MeshBuild { UVScale = 0.5f };
            var cab = new MeshBuild { UVScale = 0.4f };
            var roof = new MeshBuild { UVScale = 0.4f };
            const float h = 7.5f;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    legs.Box(f.P(at.x + sx * 1.5f, h * 0.5f, at.y + sz * 1.5f), new Vector3(0.36f, h, 0.36f), f.Rot);
            legs.Box(f.P(at.x, h, at.y), new Vector3(4.2f, 0.3f, 4.2f), f.Rot);
            cab.Box(f.P(at.x, h + 1.3f, at.y), new Vector3(3.6f, 2.2f, 3.6f), f.Rot);
            var top = f.P(at.x, h + 4.1f, at.y);
            var inside = f.P(at.x, h + 1.6f, at.y);
            Vector3 A = f.P(at.x - 2.5f, h + 2.4f, at.y - 2.5f), B = f.P(at.x - 2.5f, h + 2.4f, at.y + 2.5f);
            Vector3 C = f.P(at.x + 2.5f, h + 2.4f, at.y + 2.5f), D = f.P(at.x + 2.5f, h + 2.4f, at.y - 2.5f);
            AddOutward(roof, inside, A, top, B); AddOutward(roof, inside, B, top, C);
            AddOutward(roof, inside, C, top, D); AddOutward(roof, inside, D, top, A);
            Solid(parent, "TowerLegs", legs, _logDarkMat, layer, "Wood");
            Solid(parent, "TowerCab", cab, _logMat, layer, "Wood");
            Solid(parent, "TowerRoof", roof, _slateMat, layer, "Wood");
        }

        // ---- the buildings ------------------------------------------------
        /// <summary>
        /// A shed with its front (-v) open: three walls and a pitched roof. Open at the front the
        /// whole way across, so it is somewhere to drive in and shelter in and never a closed room.
        /// </summary>
        private static void OpenShed(Transform parent, int layer, Frame f, float w, float d, float h, Material wallMat, Material roofMat)
        {
            const float t = 0.3f;
            var walls = new MeshBuild { UVScale = 0.3f };
            WallRun(walls, f, new Vector2(-w * 0.5f, d * 0.5f - t * 0.5f), new Vector2(w * 0.5f, d * 0.5f - t * 0.5f), 0f, h, t);
            WallRun(walls, f, new Vector2(-w * 0.5f + t * 0.5f, -d * 0.5f), new Vector2(-w * 0.5f + t * 0.5f, d * 0.5f - t), 0f, h, t);
            WallRun(walls, f, new Vector2(w * 0.5f - t * 0.5f, -d * 0.5f), new Vector2(w * 0.5f - t * 0.5f, d * 0.5f - t), 0f, h, t);
            Solid(parent, "ShedWalls", walls, wallMat, layer, "Metal");

            var posts = new MeshBuild { UVScale = 0.5f };
            for (float u = -w * 0.5f + 0.4f; u <= w * 0.5f; u += (w - 0.8f) / Mathf.Max(1, Mathf.RoundToInt(w / 6f)))
                posts.Box(f.P(u, h * 0.5f, -d * 0.5f + 0.25f), new Vector3(0.36f, h, 0.36f), f.Rot);
            Solid(parent, "ShedPosts", posts, _logDarkMat, layer, "Wood");

            var roof = new MeshBuild { UVScale = 0.4f };
            var snow = new MeshBuild { UVScale = 0.4f };
            GableRoof(roof, snow, f, 0f, 0f, w, d, h, 0.3f, 0.6f);
            RoofObjects(parent, layer, roof, snow, roofMat);
            SnowFooting(parent, layer, f, 0f, d * 0.5f - 0.5f, w, 1f);
        }

        private static void Hangar(Transform parent, int layer, System.Random rng, Frame f)
        {
            const float w = 18f, d = 21f, h = 7.2f, t = 0.4f;
            var walls = new MeshBuild { UVScale = 0.3f };
            var op = new List<Opening> { new Opening(w * 0.5f, 11f, 0f, 6.2f) };
            WallRun(walls, f, new Vector2(-w * 0.5f, -d * 0.5f + t * 0.5f), new Vector2(w * 0.5f, -d * 0.5f + t * 0.5f), 0f, h, t, op);
            op = new List<Opening> { new Opening(w * 0.5f, 11f, 0f, 6.2f) };
            WallRun(walls, f, new Vector2(-w * 0.5f, d * 0.5f - t * 0.5f), new Vector2(w * 0.5f, d * 0.5f - t * 0.5f), 0f, h, t, op);
            var side = new List<Opening> { new Opening(d * 0.5f - t, 1.7f, 0f, 2.4f) };
            WallRun(walls, f, new Vector2(-w * 0.5f + t * 0.5f, -d * 0.5f + t), new Vector2(-w * 0.5f + t * 0.5f, d * 0.5f - t), 0f, h, t, side);
            WallRun(walls, f, new Vector2(w * 0.5f - t * 0.5f, -d * 0.5f + t), new Vector2(w * 0.5f - t * 0.5f, d * 0.5f - t), 0f, h, t);
            Solid(parent, "HangarWalls", walls, _steelPanelMat, layer, "Metal");

            var roof = new MeshBuild { UVScale = 0.4f };
            var snow = new MeshBuild { UVScale = 0.4f };
            GableRoof(roof, snow, f, 0f, 0f, w, d, h, 0.26f, 0.7f);
            RoofObjects(parent, layer, roof, snow, _steelPanelMat);
            SnowFooting(parent, layer, f, 0f, 0f, w, d);

            // Roof beams under the roof and a stripe on each door-jamb: a hangar, not a barn.
            var trim = new MeshBuild { UVScale = 0.5f };
            for (float v = -d * 0.5f + 2f; v < d * 0.5f; v += 4.2f)
                trim.Box(f.P(0f, h - 0.3f, v), new Vector3(w - 0.8f, 0.3f, 0.3f), f.Rot);
            Visual(parent, "HangarBeams", trim, _ironMat, layer);

            Helicopter(parent, layer, rng, f.P(0f, 0f, 1f), f.Yaw + 8f);

            // Tool benches and a drum stack along the -u wall.
            var wood = new MeshBuild { UVScale = 0.5f };
            var iron = new MeshBuild { UVScale = 0.5f };
            wood.Box(f.P(-w * 0.5f + 1.1f, 0.5f, -4f), new Vector3(1.0f, 1.0f, 4.2f), f.Rot);
            for (int i = 0; i < 4; i++)
            {
                var p = f.P(-w * 0.5f + 1.2f, 0f, 4f + i * 0.8f);
                iron.Tube(p, p + Vector3.up * 0.95f, 0.3f, 0.3f, 8);
            }
            Solid(parent, "HangarBench", wood, _plankMat, layer, "Wood");
            Solid(parent, "HangarDrums", iron, _ironMat, layer, "Metal");
        }

        /// <summary>A utility helicopter parked: fuselage, nose glass, tail boom and fin, skids, a rotor on its mast.</summary>
        private static void Helicopter(Transform parent, int layer, System.Random rng, Vector3 at, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var body = new MeshBuild { UVScale = 0.4f };
            var glass = new MeshBuild { UVScale = 0.4f };
            var dark = new MeshBuild { UVScale = 0.4f };
            body.SoftBox(at + rot * new Vector3(0f, 1.9f, 0f), new Vector3(2.3f, 2.0f, 4.6f), rot, 77, 0.5f, 4, 0.02f);
            body.Tube(at + rot * new Vector3(0f, 2.2f, -2f), at + rot * new Vector3(0f, 2.4f, -7.4f), 0.5f, 0.15f, 8);
            body.Box(at + rot * new Vector3(0f, 3.1f, -7.2f), new Vector3(0.12f, 1.6f, 1.1f), rot);
            glass.SoftBox(at + rot * new Vector3(0f, 2.1f, 2.2f), new Vector3(2.0f, 1.3f, 0.5f), rot, 5, 0.25f, 3, 0.01f);
            foreach (float s in new[] { -1f, 1f })
            {
                dark.Box(at + rot * new Vector3(s * 1.1f, 0.35f, 0f), new Vector3(0.12f, 0.12f, 4.4f), rot);
                foreach (float z in new[] { -1.4f, 1.4f })
                    dark.Box(at + rot * new Vector3(s * 1.0f, 0.75f, z), new Vector3(0.1f, 0.9f, 0.1f), rot * Quaternion.Euler(0f, 0f, s * 12f));
            }
            dark.Tube(at + rot * new Vector3(0f, 2.9f, 0f), at + rot * new Vector3(0f, 3.6f, 0f), 0.14f, 0.14f, 6);
            dark.Box(at + rot * new Vector3(0f, 3.65f, 0f), new Vector3(10.6f, 0.07f, 0.34f), rot * Quaternion.Euler(0f, 24f, 0f));
            Solid(parent, "Helicopter", body, _moduleOrange, layer, "Metal");
            Solid(parent, "HelicopterGear", dark, _ironMat, layer, "Metal");
            Visual(parent, "HelicopterGlass", glass, _dullGlassMat, layer);
        }

        private static void GeneratorYard(Transform parent, int layer, System.Random rng, Frame f, Vector2 at)
        {
            var units = new MeshBuild { UVScale = 0.4f };
            var stacks = new MeshBuild { UVScale = 0.5f };
            var canopy = new MeshBuild { UVScale = 0.5f };
            var posts = new MeshBuild { UVScale = 0.5f };
            for (int i = 0; i < 3; i++)
            {
                float u = at.x - 4.5f + i * 4.5f;
                units.SoftBox(f.P(u, 1.0f, at.y), new Vector3(3.2f, 2.0f, 1.9f), f.Rot, 40 + i, 0.1f, 3, 0.01f);
                stacks.Tube(f.P(u + 1.0f, 2.0f, at.y), f.P(u + 1.0f, 4.0f, at.y), 0.16f, 0.12f, 6);
            }
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    posts.Box(f.P(at.x + sx * 7.2f, 2.3f, at.y + sz * 3.0f), new Vector3(0.3f, 4.6f, 0.3f), f.Rot);
            canopy.Box(f.P(at.x, 4.7f, at.y), new Vector3(15.6f, 0.2f, 6.8f), f.Rot);
            Solid(parent, "Generators", units, _moduleYellow, layer, "Metal");
            Solid(parent, "GeneratorStacks", stacks, _ironMat, layer, "Metal");
            Solid(parent, "GeneratorPosts", posts, _ironMat, layer, "Metal");
            var c = Solid(parent, "GeneratorCanopy", canopy, _steelPanelMat, layer, "Metal");
            _ = c;
            var cs = new MeshBuild { UVScale = 0.5f };
            cs.Box(f.P(at.x, 4.9f, at.y), new Vector3(15.8f, 0.2f, 7f), f.Rot);
            SnowCap(parent, "CanopySnow", cs, _pineSnowMat, layer);
        }

        private static void SnowcatDepot(Transform parent, int layer, System.Random rng, Frame f)
        {
            OpenShed(parent, layer, f, 22f, 9f, 4.6f, _steelPanelMat, _slateMat);
            for (int i = 0; i < 3; i++)
                Snowcat(parent, layer, rng, f.P(-6.5f + i * 6.5f, 0f, -0.2f), f.Yaw + Rand(rng, -5f, 5f));
        }

        private static void RadioMast(Transform parent, int layer, Vector3 at)
        {
            var legs = new MeshBuild { UVScale = 0.5f };
            var guys = new MeshBuild { UVScale = 0.5f };
            const float h = 26f;
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                var foot = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.1f;
                var top = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.3f + Vector3.up * h;
                legs.Tube(foot + Vector3.down * 0.3f, top, 0.1f, 0.06f, 5);
                var anchor = at + new Vector3(Mathf.Cos(a + 0.5f), 0f, Mathf.Sin(a + 0.5f)) * 11f;
                guys.Tube(anchor, at + Vector3.up * (h * 0.8f), 0.025f, 0.025f, 3);
            }
            for (float y = 3f; y < h; y += 3f)
                legs.Tube(at + new Vector3(1.0f - y * 0.03f, y, 0f), at + new Vector3(-0.5f + y * 0.015f, y + 0.1f, 0.9f - y * 0.03f), 0.04f, 0.04f, 4);
            legs.Box(at + Vector3.up * (h + 0.4f), new Vector3(0.14f, 0.8f, 0.14f), Quaternion.identity);
            Solid(parent, "RadioMast", legs, _moduleRed, layer, "Metal");
            Visual(parent, "MastGuys", guys, _ironMat, layer);
        }

        private static Material[] _containerSnow;

        private static void ContainerBlock(Transform parent, int layer, System.Random rng, Frame f)
        {
            if (_containerSnow == null || _containerSnow.Length == 0 || _containerSnow[0] == null)
                _containerSnow = new[]
                {
                    MakeMaterial("BoxRed", new Color(0.52f, 0.16f, 0.12f), 0.3f, 0.3f),
                    MakeMaterial("BoxBlue", new Color(0.18f, 0.30f, 0.46f), 0.3f, 0.3f),
                    MakeMaterial("BoxGreen", new Color(0.20f, 0.36f, 0.26f), 0.3f, 0.3f),
                    MakeMaterial("BoxGrey", new Color(0.46f, 0.48f, 0.50f), 0.3f, 0.3f)
                };

            // Rows with gaps, never a wall: each is a place to stop behind and a lane to run down.
            var rows = new[]
            {
                (new Vector2(-6f, -6f), 0f, 1), (new Vector2(5f, -11f), 90f, 2), (new Vector2(-5f, 9f), 90f, 1),
                (new Vector2(12f, 3f), 0f, 1), (new Vector2(-8f, -10f), 0f, 1)
            };
            foreach (var row in rows)
            {
                var rf = new Frame(f.P(row.Item1.x, 0f, row.Item1.y), f.Yaw + row.Item2);
                for (int i = 0; i < 2; i++)
                {
                    var build = new MeshBuild { UVScale = 0.3f };
                    int stack = row.Item3;
                    for (int s = 0; s < stack; s++)
                        build.SoftBox(rf.P(i * 6.6f, 1.25f + s * 2.5f, 0f), new Vector3(6.1f, 2.4f, 2.5f), rf.Rot, rng.Next(999), 0.06f, 2, 0.01f);
                    var go = Solid(parent, "Container", build, _containerSnow[rng.Next(_containerSnow.Length)], layer, "Metal");
                    _ = go;
                }
            }
        }

        private static void FuelFarm(Transform parent, int layer, Frame f, Vector2 at)
        {
            var tanks = new MeshBuild { UVScale = 0.4f };
            var cradles = new MeshBuild { UVScale = 0.4f };
            for (int i = 0; i < 3; i++)
            {
                var c = f.P(at.x, 1.7f, at.y - 7f + i * 5.2f);
                tanks.Tube(c - f.Axis(Vector3.forward) * 2.2f, c + f.Axis(Vector3.forward) * 2.2f, 1.6f, 1.6f, 14);
                foreach (float s in new[] { -1.4f, 1.4f })
                    cradles.Box(c + f.Axis(Vector3.forward) * s + Vector3.down * 1.1f, f.WorldSize(new Vector3(3.0f, 1.2f, 0.4f)), Quaternion.identity);
            }
            Solid(parent, "FuelTanks", tanks, _pineSnowMat, layer, "Metal");
            Solid(parent, "FuelCradles", cradles, _ironMat, layer, "Metal");
        }

        // ==================================================================
        // The lodge
        // ==================================================================
        /// <summary>
        /// The ski lodge on its own pad: a great hall you can walk into from three sides, a
        /// storehouse beside it with a roof you can climb to, snowcats and sleds out front, and
        /// the bottom station of the lift (FPSKitSnowPass.cs) a short walk off.
        /// </summary>
        private static void BuildLodge(Transform parent, int layer, int backdrop, System.Random rng, SitePlan plan)
        {
            var lodge = new GameObject("Lodge").transform;
            lodge.SetParent(parent, false);

            float floor = GroundHeightAt(plan.Centre.x, plan.Centre.y);
            var f = new Frame(new Vector3(plan.Centre.x, floor, plan.Centre.y), plan.Yaw);

            BuildEnterableChalet(lodge, layer, backdrop, rng, new Frame(f.P(4f, 0f, 5f), f.Yaw), 12f, flatRoof: false, wIn: 17f, dIn: 11f, bigLodge: true);
            BuildEnterableChalet(lodge, layer, backdrop, rng, new Frame(f.P(-10f, 0f, -7f), f.Yaw), 12f, flatRoof: true);

            // An annexe: a solid two-storey wing against the back of the hall.
            var wing = new MeshBuild { UVScale = 0.3f };
            wing.SoftBox(f.P(4f, 3.6f, 15.2f), new Vector3(9f, 7.2f, 7f), f.Rot, 91, 0.1f, 3, 0.01f);
            Solid(lodge, "LodgeWing", wing, _logMat, layer, "Wood");
            SealLocal(lodge, f, 4f, 3.6f, 15.2f, new Vector3(9f, 7.2f, 7f));
            var roof = new MeshBuild { UVScale = 0.4f };
            var snow = new MeshBuild { UVScale = 0.4f };
            GableRoof(roof, snow, f, 4f, 15.2f, 9f, 7f, 7.2f, 0.6f, 0.7f);
            RoofObjects(lodge, layer, roof, snow, _slateMat);

            var stuff = new MeshBuild { UVScale = 0.5f };
            Sled(stuff, f.P(-2f, 0f, -9f), f.Yaw + 20f);
            Sled(stuff, f.P(0.2f, 0f, -9.4f), f.Yaw - 10f);
            for (int i = 0; i < 4; i++)
            {
                var p = f.P(10f + i * 0.5f, 0.0f, -9f);
                stuff.Box(p + Vector3.up * 1.2f, new Vector3(0.12f, 2.4f, 0.12f), Quaternion.identity);   // skis planted upright
                stuff.Box(p + Vector3.up * 1.2f + f.Axis(Vector3.right) * 0.3f, new Vector3(0.12f, 2.4f, 0.12f), Quaternion.identity);
            }
            Solid(lodge, "LodgeStuff", stuff, _plankMat, layer, "Wood");
            BuildPickup(lodge, layer, rng, f.P(12f, 0f, -5f), f.Yaw + 70f);
            Snowcat(lodge, layer, rng, f.P(-4f, 0f, -11f), f.Yaw + 190f);
            BuildSnowmobile(lodge, layer, rng, f.P(13.5f, 0f, -9.5f), f.Yaw + 30f);
            BuildSnowmobile(lodge, layer, rng, f.P(15.5f, 0f, -9f), f.Yaw + 55f);
        }
    }
}
#endif
