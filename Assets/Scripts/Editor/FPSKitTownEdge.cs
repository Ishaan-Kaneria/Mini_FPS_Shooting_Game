#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The edge of town and the land round it: the gas station at the highway turn-off, the checkpoint on the
    /// avenue, the garage, the bus stop, the junkyard, the water tower, the palm farms and the oasis, the highway's
    /// rails, poles and the roadblocks where it leaves the map, and the open desert's rocks, shrubs, dead trees and a
    /// Bedouin camp. Every one of them is a place the player can name from a distance, which is what a map needs.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static Material _corrugated, _corrugatedRust, _concreteFloor, _canopy, _stripeRed, _stripeWhite, _tent, _fence;

        private static void ResolveEdgeMaterials()
        {
            _corrugated = TownMat("Corrugated", "corrugated_iron_02", new Color(0.95f, 0.95f, 0.93f), 2f, 0.9f);
            _corrugatedRust = TownMat("CorrugatedRust", "rusty_corrugated_iron", new Color(1f, 0.95f, 0.9f), 2f, 0.9f);
            _concreteFloor = TownMat("ForecourtConcrete", "concrete_wall_008", new Color(0.96f, 0.95f, 0.92f), 5f, 0.3f);
            _canopy = TownFlat("CanopyWhite", new Color(0.88f, 0.88f, 0.86f), 0.35f);
            _stripeRed = TownFlat("StripeRed", new Color(0.70f, 0.10f, 0.10f), 0.35f);
            _stripeWhite = TownFlat("StripeWhite", new Color(0.90f, 0.90f, 0.88f), 0.35f);
            _tent = TownFlat("TentCloth", new Color(0.16f, 0.12f, 0.10f), 0.05f);
            _fence = TownFlat("FenceSteel", new Color(0.45f, 0.45f, 0.44f), 0.4f, 0.7f);
        }

        private static void BuildTownEdge(Transform root, int layer, System.Random rng)
        {
            ResolveEdgeMaterials();
            var group = new GameObject("Edge").transform;
            group.SetParent(root, false);
            var props = new GameObject("EdgeProps").transform;
            props.SetParent(group, false);

            BuildGasStation(group, layer, props, rng);
            BuildCheckpoint(group, layer, props, rng);
            BuildGarage(group, layer, props, rng);
            BuildBusStop(group, layer, props);
            BuildJunkyard(group, layer, props, rng);
            BuildWaterTower(group, layer, props);
            BuildFarms(group, layer, props, rng);
            BuildOasis(group, layer, props, rng);
            BuildHighwayDressing(group, layer, props, rng);
            BuildWildDesert(group, layer, props, rng);
            BuildBedouinCamp(group, layer, props, rng);
        }

        // ==================================================================
        private static void BuildGasStation(Transform group, int layer, Transform props, System.Random rng)
        {
            var m = new TownMesh();
            var r = GasStationRect;
            float y = TownRoadY(r.center.x, r.yMin) + 0.05f;
            // Forecourt slab, flush with the highway at its south edge.
            m.BoxMinMax(_concreteFloor, new Vector3(r.xMin, y - 0.5f, r.yMin), new Vector3(r.xMax, y, r.yMax), bottom: false);
            // Canopy over three pump islands.
            float cx0 = r.xMin + 6f, cx1 = r.xMin + 36f, cz0 = r.yMin + 8f, cz1 = r.yMin + 24f;
            const float CH = 5.6f;
            foreach (var p in new[] { new Vector2(cx0 + 2f, cz0 + 2f), new Vector2(cx1 - 2f, cz0 + 2f), new Vector2(cx0 + 2f, cz1 - 2f), new Vector2(cx1 - 2f, cz1 - 2f),
                                      new Vector2((cx0 + cx1) * 0.5f, cz0 + 2f), new Vector2((cx0 + cx1) * 0.5f, cz1 - 2f) })
                m.BoxMinMax(_canopy, new Vector3(p.x - 0.25f, y, p.y - 0.25f), new Vector3(p.x + 0.25f, y + CH, p.y + 0.25f));
            m.BoxMinMax(_canopy, new Vector3(cx0, y + CH, cz0), new Vector3(cx1, y + CH + 1.0f, cz1));
            // The fascia: green band and the sign on the highway face.
            m.BoxMinMax(TownFlat("FuelGreen", new Color(0.0f, 0.42f, 0.27f), 0.4f), new Vector3(cx0 - 0.02f, y + CH + 0.15f, cz0 - 0.02f), new Vector3(cx1 + 0.02f, y + CH + 0.55f, cz1 + 0.02f));
            var cell = SignCell("sh_fuel");
            Vector3 s0 = new Vector3((cx0 + cx1) * 0.5f - 4f, y + CH + 0.05f, cz0 - 0.05f);
            // Facing south, read from the highway: left is west, so the cell's left edge goes on the west corner.
            m.QuadUV(_townSignMat, s0, s0 + new Vector3(8f, 0, 0), s0 + new Vector3(8f, 0.95f, 0), s0 + new Vector3(0, 0.95f, 0), Vector3.back,
                     new Vector2(cell.xMin, cell.yMin), new Vector2(cell.xMax, cell.yMin), new Vector2(cell.xMax, cell.yMax), new Vector2(cell.xMin, cell.yMax));
            // Pump islands with three pumps each.
            for (int i = 0; i < 3; i++)
            {
                float ix = cx0 + 6f + i * 9f;
                m.BoxMinMax(_kerb, new Vector3(ix - 0.8f, y, cz0 + 3f), new Vector3(ix + 0.8f, y + 0.18f, cz1 - 3f));
                for (int k = 0; k < 2; k++)
                {
                    float pz = Mathf.Lerp(cz0 + 5f, cz1 - 5f, k);
                    m.BoxMinMax(_stripeWhite, new Vector3(ix - 0.45f, y + 0.18f, pz - 0.3f), new Vector3(ix + 0.45f, y + 2.0f, pz + 0.3f));
                    m.BoxMinMax(TownFlat("FuelGreen", new Color(0.0f, 0.42f, 0.27f), 0.4f), new Vector3(ix - 0.46f, y + 1.7f, pz - 0.31f), new Vector3(ix + 0.46f, y + 2.0f, pz + 0.31f));
                    m.BoxMinMax(_glassDark, new Vector3(ix - 0.3f, y + 1.15f, pz - 0.31f), new Vector3(ix + 0.3f, y + 1.5f, pz + 0.31f));
                    m.Box(_steel, new Vector3(ix + 0.5f, y + 1.0f, pz), new Vector3(0.06f, 1.2f, 0.06f), Quaternion.Euler(0, 0, 20f));
                }
                _townSolids.Add(Rect.MinMaxRect(ix - 0.8f, cz0 + 3f, ix + 0.8f, cz1 - 3f));
            }
            // The shop behind the pumps.
            var shop = Rect.MinMaxRect(r.xMin + 40f, r.yMax - 14f, r.xMax - 2f, r.yMax - 2f);
            var b = NewMass(shop, rng, 1, false);
            b.baseY = y; b.groundMin = y - 0.6f; b.groundH = 4.2f;
            b.faces[1] = FaceKind.Shops; b.faces[0] = FaceKind.Blank;
            BuildMass(b, props);
            // Price pylon by the road.
            var pc = new Vector3(r.xMax - 6f, y, r.yMin + 2.5f);
            m.BoxMinMax(_canopy, pc + new Vector3(-0.25f, 0f, -0.25f), pc + new Vector3(0.25f, 5f, 0.25f));
            m.BoxMinMax(TownFlat("FuelGreen", new Color(0.0f, 0.42f, 0.27f), 0.4f), pc + new Vector3(-1.4f, 5f, -0.3f), pc + new Vector3(1.4f, 8.6f, 0.3f));
            var c2 = SignCell("sh_fuel");
            m.QuadUV(_townSignMat, pc + new Vector3(-1.3f, 7.7f, -0.32f), pc + new Vector3(1.3f, 7.7f, -0.32f), pc + new Vector3(1.3f, 8.35f, -0.32f), pc + new Vector3(-1.3f, 8.35f, -0.32f), Vector3.back,
                     new Vector2(c2.xMin, c2.yMin), new Vector2(c2.xMax, c2.yMin), new Vector2(c2.xMax, c2.yMax), new Vector2(c2.xMin, c2.yMax));
            for (int k = 0; k < 3; k++)
                m.BoxMinMax(_glassDark, pc + new Vector3(-1.1f, 5.3f + k * 0.75f, -0.32f), pc + new Vector3(1.1f, 5.85f + k * 0.75f, -0.31f));
            var go = m.Build(group, "GasStation", layer, "Concrete");
            _ = go;
            NoEntryBox(Rect.MinMaxRect(cx0, cz0, cx1, cz1), y + CH - 0.3f, y + CH + 4f, 0f);
            _townSolids.Add(Rect.MinMaxRect(pc.x - 1.4f, pc.z - 0.3f, pc.x + 1.4f, pc.z + 0.3f));
            // Gas cylinders in a cage, tyres, an air pump, oil drums.
            for (int i = 0; i < 10; i++)
                PlaceTown(props, Prop("small_lpg_tank"), new Vector3(r.xMin + 2f + (i % 5) * 0.55f, y, r.yMax - 4f + (i / 5) * 0.6f), Rand(rng, 0f, 360f), 0.9f);
            m = new TownMesh();
            m.BoxMinMax(_fence, new Vector3(r.xMin + 1.5f, y, r.yMax - 4.6f), new Vector3(r.xMin + 4.9f, y + 1.6f, r.yMax - 4.5f));
            m.Build(group, "GasCage", layer, "Metal");
            for (int i = 0; i < 6; i++) PlaceTown(props, Prop("old_tyre"), new Vector3(r.xMin + 6f, y + i * 0.22f, r.yMax - 3f), Rand(rng, 0f, 360f));
            PlaceTown(props, Prop("Barrel_01"), new Vector3(r.xMax - 4f, y, r.yMax - 16.5f), 0f);
            PlaceTown(props, Prop("barrel_03"), new Vector3(r.xMax - 5f, y, r.yMax - 16.8f), 40f);
            PlaceTown(props, Prop("metal_trash_can"), new Vector3(r.xMin + 40f, y, r.yMax - 15f), 0f);
            _gasPumps.Clear();
            for (int i = 0; i < 3; i++) _gasPumps.Add(new Vector4(cx0 + 6f + i * 9f + 2.3f, y, (cz0 + cz1) * 0.5f, 0f));
        }

        /// <summary>Car bays beside the pumps (FPSKitTownLife parks a car or two there).</summary>
        private static readonly List<Vector4> _gasPumps = new List<Vector4>();

        // ==================================================================
        private static void BuildCheckpoint(Transform group, int layer, Transform props, System.Random rng)
        {
            var m = new TownMesh();
            var c = CheckpointAt;
            float y = TownRoadY(c.x, c.y);
            // Guard booth in the median, a sandbagged post on the west pavement, a boom across the northbound lanes.
            var booth = Rect.MinMaxRect(c.x - 1.3f, c.y - 1.6f, c.x + 1.3f, c.y + 1.6f);
            m.BoxMinMax(_stucco, new Vector3(booth.xMin, y, booth.yMin), new Vector3(booth.xMax, y + 2.6f, booth.yMax));
            m.BoxMinMax(_roof, new Vector3(booth.xMin - 0.3f, y + 2.6f, booth.yMin - 0.3f), new Vector3(booth.xMax + 0.3f, y + 2.85f, booth.yMax + 0.3f));
            foreach (var n in new[] { Vector3.right, Vector3.left })
                m.Quad(_glassDark, new Vector3(c.x + n.x * 1.31f, y + 1.1f, c.y - 0.9f), new Vector3(c.x + n.x * 1.31f, y + 1.1f, c.y + 0.9f),
                       new Vector3(c.x + n.x * 1.31f, y + 2.2f, c.y + 0.9f), new Vector3(c.x + n.x * 1.31f, y + 2.2f, c.y - 0.9f), n);
            _townSolids.Add(booth);
            // Boom: a red-and-white pole on a post at the median's edge, raised.
            var bp = new Vector3(c.x + 1.6f, y, c.y - 2.5f);
            m.BoxMinMax(_steel, bp + new Vector3(-0.15f, 0f, -0.15f), bp + new Vector3(0.15f, 1.1f, 0.15f));
            for (int k = 0; k < 7; k++)
                m.Box(k % 2 == 0 ? _stripeRed : _stripeWhite, bp + new Vector3(0.3f + k * 0.3f * 0.5f, 1.1f + k * 0.3f * 0.85f, 0f), new Vector3(0.3f, 0.09f, 0.09f), Quaternion.Euler(0, 0, 60f));
            var go = m.Build(group, "Checkpoint", layer, "Concrete");
            _ = go;
            // Barriers narrowing both carriageways into a chicane.
            var barrier = Prop("concrete_road_barrier");
            foreach (var (x, z, yaw) in new[] { (5.5f, -362f, 0f), (7.5f, -362f, 0f), (-5.5f, -374f, 0f), (-7.5f, -374f, 0f), (3.2f, -354f, 90f), (-3.2f, -382f, 90f) })
                PlaceTown(props, barrier, new Vector3(c.x + x, TownRoadY(c.x + x, z), z), yaw);
            // Sandbag post on the west pavement with a searchlight and crates.
            var sand = MakeMaterial("Sandbag", new Color(0.62f, 0.55f, 0.42f), 0.05f, 0f);
            for (int i = 0; i < 9; i++)
            {
                float a = i / 8f * Mathf.PI;
                var p = new Vector3(-11f + Mathf.Cos(a) * 2.2f, 0f, -368f + Mathf.Sin(a) * 2.2f - 1f);
                for (int row = 0; row < 3; row++)
                {
                    var bag = MeshObject(group, "Sandbag", SandbagMesh(i * 3 + row), sand, new Vector3(p.x, TownRoadY(p.x, p.z) + KerbHeight + row * 0.24f, p.z),
                                         Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90f + (row % 2) * 15f, 0), Vector3.one, layer, "Concrete");
                    NoStanding(bag);
                }
            }
            PlaceTown(props, Prop("security_light"), new Vector3(-12f, TownRoadY(-12f, -366f) + KerbHeight, -366f), 160f, 2.8f);
            PlaceTown(props, Prop("wooden_military_crate"), new Vector3(-9.5f, TownRoadY(-9.5f, -364f) + KerbHeight, -364f), 20f);
            PlaceTown(props, Prop("ammo_box"), new Vector3(-9.8f, TownRoadY(-9.8f, -365f) + KerbHeight + 0.5f, -365f), 75f);
            Flag(group, layer, new Vector3(-12.2f, TownRoadY(-12.2f, -371f) + KerbHeight, -371f), 7f);
            RoadSign(group, layer, new Vector3(c.x + 1.8f, y + 0.15f, c.y - 8f), 180f, "stop", 2.4f);
            SignPanel(group, layer, new Vector3(-12.6f, TownRoadY(-12.6f, -360f) + KerbHeight, -360f), 90f, "sh_checkpoint", 3.2f, 2.6f);
        }

        // ==================================================================
        private static void BuildGarage(Transform group, int layer, Transform props, System.Random rng)
        {
            var r = GarageRect;
            var b = NewMass(r, rng, 1, false);
            b.groundH = 5.2f;
            b.faces[1] = FaceKind.Shops; b.faces[0] = FaceKind.Blank;
            b.wall = _plasters[6];
            b.bay = 6.2f;
            BuildMass(b, props);
            // Workshop clutter in front: tyre stacks, a car on blocks (life pass), drums, a compressor.
            float y = b.baseY;
            for (int s = 0; s < 4; s++)
                for (int i = 0; i < rng.Next(3, 8); i++)
                    PlaceTown(props, Prop("old_tyre"), new Vector3(r.xMin + 2f + s * 1.2f, y + i * 0.22f, r.yMin - 2.2f), Rand(rng, 0f, 360f));
            PlaceTown(props, Prop("rusted_wheel_rim_01"), new Vector3(r.xMax - 3f, y, r.yMin - 1.5f), 30f);
            PlaceTown(props, Prop("Barrel_01"), new Vector3(r.xMax - 1.2f, y, r.yMin - 1.2f), 0f);
            PlaceTown(props, Prop("barrel_03"), new Vector3(r.xMax - 1.9f, y, r.yMin - 1.0f), 70f);
            PlaceTown(props, Prop("portable_generator"), new Vector3(r.xMin + 9f, y, r.yMin - 1.6f), 180f);
            PlaceTown(props, Prop("hand_truck"), new Vector3(r.xMin + 11f, y, r.yMin - 1.0f), 200f);
            SignPanel(group, layer, new Vector3(r.center.x - 8f, y + 5.4f, r.yMin - 0.15f), 180f, "sh_autorepair", 6f, 1.5f, standing: false);
            SignPanel(group, layer, new Vector3(r.center.x + 8f, y + 5.4f, r.yMin - 0.15f), 180f, "sh_tyres", 6f, 1.5f, standing: false);
            _garageBay = new Vector4(r.center.x, y, r.yMin - 4f, 90f);
        }

        private static Vector4 _garageBay;

        // ==================================================================
        private static void BuildBusStop(Transform group, int layer, Transform props)
        {
            var m = new TownMesh();
            var c = BusStopAt;
            float y = TownRoadY(c.x, c.y) + KerbHeight;
            // Shelter: two posts, a roof sloping back, a glass back panel, a bench, the sign.
            float x = c.x + 0.6f;
            foreach (float dz in new[] { -2.2f, 2.2f })
                m.BoxMinMax(_frameAlu, new Vector3(x + 0.9f, y, c.y + dz - 0.05f), new Vector3(x + 1.0f, y + 2.6f, c.y + dz + 0.05f));
            m.BoxMinMax(_frameAlu, new Vector3(x - 0.4f, y + 2.55f, c.y - 2.4f), new Vector3(x + 1.1f, y + 2.7f, c.y + 2.4f));
            m.Quad(_glassClear, new Vector3(x + 0.95f, y + 0.3f, c.y - 2.15f), new Vector3(x + 0.95f, y + 0.3f, c.y + 2.15f),
                   new Vector3(x + 0.95f, y + 2.4f, c.y + 2.15f), new Vector3(x + 0.95f, y + 2.4f, c.y - 2.15f), Vector3.left);
            m.BoxMinMax(_frameAlu, new Vector3(x + 0.35f, y + 0.42f, c.y - 1.8f), new Vector3(x + 0.8f, y + 0.48f, c.y + 1.8f));
            var cell = SignCell("sh_busstop");
            m.QuadUV(_townSignMat, new Vector3(x - 0.41f, y + 2.0f, c.y + 1.6f), new Vector3(x - 0.41f, y + 2.0f, c.y - 1.6f),
                     new Vector3(x - 0.41f, y + 2.55f, c.y - 1.6f), new Vector3(x - 0.41f, y + 2.55f, c.y + 1.6f), Vector3.left,
                     new Vector2(cell.xMin, cell.yMin), new Vector2(cell.xMax, cell.yMin), new Vector2(cell.xMax, cell.yMax), new Vector2(cell.xMin, cell.yMax));
            m.Build(group, "BusStop", layer, "Metal");
            RoadSign(group, layer, new Vector3(c.x - 1.6f, y, c.y + 3.4f), 270f, "busstop", 2.6f);
            PlaceTown(props, Prop("metal_trash_can"), new Vector3(c.x - 1.4f, y, c.y - 3.2f), 0f);
            _busStop = new Vector4(c.x + 0.2f, y, c.y, 270f);
        }

        private static Vector4 _busStop;

        // ==================================================================
        private static void BuildJunkyard(Transform group, int layer, Transform props, System.Random rng)
        {
            var r = JunkyardRect;
            var m = new TownMesh();
            // Fence: posts every three metres and a mesh panel (dark, semi-solid), a gate gap on the north side at the track.
            for (int s = 0; s < 4; s++)
            {
                var (a0, b0) = FaceRun(r, s);
                float len = Vector3.Distance(a0, b0);
                Vector3 u = (b0 - a0) / len;
                var n = SideOut3(s);
                for (float d = 0f; d <= len; d += 3f)
                {
                    var p = a0 + u * d;
                    if (s == 0 && Mathf.Abs(p.x - (-275f)) < 4f) continue;
                    float g = GroundHeightAt(p.x, p.z);
                    m.BoxMinMax(_fence, new Vector3(p.x - 0.04f, g - 0.3f, p.z - 0.04f), new Vector3(p.x + 0.04f, g + 2.4f, p.z + 0.04f));
                    if (d + 3f <= len + 0.01f)
                    {
                        var q = a0 + u * (d + 3f);
                        if (s == 0 && Mathf.Abs((p.x + q.x) * 0.5f - (-275f)) < 4.5f) continue;
                        float g2 = GroundHeightAt(q.x, q.z);
                        m.Quad(_corrugatedRust, new Vector3(p.x, g + 0.1f, p.z), new Vector3(q.x, g2 + 0.1f, q.z), new Vector3(q.x, g2 + 2.3f, q.z), new Vector3(p.x, g + 2.3f, p.z), n);
                        m.Quad(_corrugatedRust, new Vector3(p.x, g + 0.1f, p.z), new Vector3(q.x, g2 + 0.1f, q.z), new Vector3(q.x, g2 + 2.3f, q.z), new Vector3(p.x, g + 2.3f, p.z), -n);
                    }
                }
            }
            // A container office by the gate.
            var cont = new Vector3(-265f, GroundHeightAt(-265f, -436f), -436f);
            m.BoxMinMax(_corrugated, cont + new Vector3(-3f, -0.2f, -1.25f), cont + new Vector3(3f, 2.6f, 1.25f));
            m.Quad(_doorMetal, cont + new Vector3(-0.5f, 0.05f, -1.26f), cont + new Vector3(0.5f, 0.05f, -1.26f), cont + new Vector3(0.5f, 2.1f, -1.26f), cont + new Vector3(-0.5f, 2.1f, -1.26f), Vector3.back);
            m.Build(group, "Junkyard", layer, "Metal", shadows: ShadowCastingMode.On);
            _townSolids.Add(Rect.MinMaxRect(cont.x - 3f, cont.z - 1.25f, cont.x + 3f, cont.z + 1.25f));
            NoEntryBox(Rect.MinMaxRect(cont.x - 3f, cont.z - 1.25f, cont.x + 3f, cont.z + 1.25f), cont.y - 1f, cont.y + 6f, 0f);
            // Rows of wrecks (FPSKitTownLife stacks the cars) and piles of tyres and drums.
            _junkRows.Clear();
            for (float z = r.yMin + 6f; z < r.yMax - 10f; z += 8f) _junkRows.Add(new Vector4(r.xMin + 8f, 0f, z, r.xMax - 16f));
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector2(Rand(rng, r.xMin + 4f, r.xMax - 4f), Rand(rng, r.yMin + 3f, r.yMax - 4f));
                int n = rng.Next(3, 9);
                for (int k = 0; k < n; k++) PlaceTown(props, Prop("old_tyre"), new Vector3(p.x + Rand(rng, -0.8f, 0.8f), GroundHeightAt(p.x, p.y) + (k / 3) * 0.24f, p.y + Rand(rng, -0.8f, 0.8f)), Rand(rng, 0f, 360f));
            }
            for (int i = 0; i < 14; i++)
            {
                var p = new Vector2(Rand(rng, r.xMin + 3f, r.xMax - 3f), Rand(rng, r.yMin + 3f, r.yMax - 3f));
                PlaceTown(props, Prop(rng.NextDouble() < 0.5 ? "Barrel_01" : rng.NextDouble() < 0.5 ? "rusted_wheel_rim_01" : "barrel_03"),
                          new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y), Rand(rng, 0f, 360f));
            }
        }

        /// <summary>Rows in the junkyard where wrecks are lined up: (x start, -, z, x end).</summary>
        private static readonly List<Vector4> _junkRows = new List<Vector4>();

        // ==================================================================
        private static void BuildWaterTower(Transform group, int layer, Transform props)
        {
            var m = new TownMesh();
            var c = new Vector3(WaterTowerAt.x, GroundHeightAt(WaterTowerAt.x, WaterTowerAt.y), WaterTowerAt.y);
            const float H = 22f, R = 4.5f, TankH = 6.5f;
            var legs = new[] { new Vector2(-3.6f, -3.6f), new Vector2(3.6f, -3.6f), new Vector2(3.6f, 3.6f), new Vector2(-3.6f, 3.6f) };
            var white = TownFlat("TowerWhite", new Color(0.84f, 0.84f, 0.82f), 0.4f, 0.4f);
            var blue = TownFlat("TowerBlue", new Color(0.18f, 0.36f, 0.62f), 0.4f, 0.4f);
            foreach (var l in legs)
            {
                var foot = c + new Vector3(l.x * 1.25f, -0.5f, l.y * 1.25f);
                var head = c + new Vector3(l.x, H, l.y);
                var mid = (foot + head) * 0.5f;
                m.Box(_steel, mid, new Vector3(0.35f, Vector3.Distance(foot, head), 0.35f), Quaternion.FromToRotation(Vector3.up, (head - foot).normalized));
                m.BoxMinMax(_concreteFloor, foot + new Vector3(-0.6f, -0.2f, -0.6f), foot + new Vector3(0.6f, 0.7f, 0.6f));
            }
            // Bracing rings and crosses.
            for (float h = 5f; h < H; h += 5.5f)
                for (int i = 0; i < 4; i++)
                {
                    var a = legs[i] * Mathf.Lerp(1.25f, 1f, h / H); var b = legs[(i + 1) % 4] * Mathf.Lerp(1.25f, 1f, h / H);
                    var pa = c + new Vector3(a.x, h, a.y); var pb = c + new Vector3(b.x, h, b.y);
                    m.Box(_steel, (pa + pb) * 0.5f, new Vector3(0.12f, 0.12f, Vector3.Distance(pa, pb)), Quaternion.LookRotation(pb - pa));
                }
            m.Cylinder(white, c + Vector3.up * H, R, TankH, 32, false, true);
            m.Cylinder(blue, c + Vector3.up * (H + TankH * 0.55f), R + 0.02f, 0.9f, 32, false);
            m.Cylinder(white, c + Vector3.up * (H + TankH), R, 1.8f, 32, true, false, 0.4f);
            m.Cylinder(_steel, c + Vector3.up * (H - 0.2f), R + 0.8f, 0.2f, 32, true, true);
            // Ladder up one leg.
            for (float h = 0.3f; h < H; h += 0.35f)
                m.Box(_steel, c + new Vector3(-3.6f, h, -4.3f), new Vector3(0.5f, 0.03f, 0.03f), Quaternion.identity);
            m.Build(group, "WaterTower", layer, "Metal");
            foreach (var l in legs) _townSolids.Add(Rect.MinMaxRect(c.x + l.x * 1.25f - 0.6f, c.z + l.y * 1.25f - 0.6f, c.x + l.x * 1.25f + 0.6f, c.z + l.y * 1.25f + 0.6f));
            NoEntryBox(Rect.MinMaxRect(c.x - R - 1f, c.z - R - 1f, c.x + R + 1f, c.z + R + 1f), c.y + H - 1f, c.y + H + 12f, 0f);
            _ = props;
        }

        // ==================================================================
        /// <summary>
        /// Date-palm farms: each plot walled in low mud brick with a gap on the track side, a channel of water along
        /// its inside edge, palms in rows nine metres apart (offset row to row, as groves are planted), and here and
        /// there a pump shed, crates of dates, a ladder against a trunk.
        /// </summary>
        private static void BuildFarms(Transform group, int layer, Transform props, System.Random rng)
        {
            var palms = new GameObject("Palms").transform;
            palms.SetParent(group, false);
            var m = new TownMesh();
            var mud = TownMat("FarmWall", "clay_plaster", new Color(0.95f, 0.86f, 0.72f), 3f);
            string[] palmKinds = { "Palm_DualStraight", "Palm_SingleStraight", "Palm_Trio", "Palm_DualBended", "Palm_SingleBended" };
            foreach (var plot in _farmPlots)
            {
                // Low mud wall, open at the middle of the side facing the oasis or track.
                for (int s = 0; s < 4; s++)
                {
                    var (a0, b0) = FaceRun(plot, s);
                    float len = Vector3.Distance(a0, b0);
                    Vector3 u = (b0 - a0) / len;
                    float gap0 = len * 0.5f - 2f, gap1 = len * 0.5f + 2f;
                    foreach (var (s0, s1) in new[] { (0f, gap0), (gap1, len) })
                    {
                        if (s % 2 == 1 && s0 > 0f) { }
                        for (float d = s0; d < s1 - 0.01f; d += 4f)
                        {
                            float e = Mathf.Min(d + 4f, s1);
                            var p = a0 + u * d; var q = a0 + u * e;
                            float g = Mathf.Min(GroundHeightAt(p.x, p.z), GroundHeightAt(q.x, q.z));
                            var mid = (p + q) * 0.5f;
                            m.Box(mud, new Vector3(mid.x, g + 0.35f, mid.z) - SideOut3(s) * 0.2f, new Vector3(e - d, 1.3f, 0.4f), Quaternion.LookRotation(SideOut3(s)));
                        }
                    }
                }
                // Irrigation channel just inside the south wall: a shallow strip of water with mud lips.
                float cz = plot.yMin + 1.6f;
                float wy = GroundHeightAt(plot.center.x, cz) + 0.05f;
                m.Quad(_townWater, new Vector3(plot.xMin + 1f, wy, cz - 0.35f), new Vector3(plot.xMax - 1f, wy, cz - 0.35f), new Vector3(plot.xMax - 1f, wy, cz + 0.35f), new Vector3(plot.xMin + 1f, wy, cz + 0.35f), Vector3.up);
                m.Box(mud, new Vector3(plot.center.x, wy, cz - 0.45f), new Vector3(plot.width - 2f, 0.25f, 0.2f), Quaternion.identity);
                m.Box(mud, new Vector3(plot.center.x, wy, cz + 0.45f), new Vector3(plot.width - 2f, 0.25f, 0.2f), Quaternion.identity);
                // Palms in offset rows.
                int row = 0;
                for (float z = plot.yMin + 5f; z < plot.yMax - 3f; z += 8f, row++)
                    for (float x = plot.xMin + 4f + (row % 2) * 4.5f; x < plot.xMax - 3f; x += 9f)
                    {
                        var p = new Vector2(x + Rand(rng, -1f, 1f), z + Rand(rng, -1f, 1f));
                        PlaceTown(palms, PackPrefab(palmKinds[rng.Next(palmKinds.Length)]), new Vector3(p.x, GroundHeightAt(p.x, p.y) - 0.1f, p.y),
                                  Rand(rng, 0f, 360f), Rand(rng, 8f, 13f), navBlock: false);
                    }
                if (rng.NextDouble() < 0.35)
                {
                    var p = new Vector2(Rand(rng, plot.xMin + 5f, plot.xMax - 5f), Rand(rng, plot.yMin + 5f, plot.yMax - 5f));
                    PlaceTown(props, Prop(rng.NextDouble() < 0.5 ? "wooden_crate_01" : "wicker_basket_02"), new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y), Rand(rng, 0f, 360f));
                    PlaceTown(props, Prop("wooden_ladder"), new Vector3(p.x + 1.5f, GroundHeightAt(p.x + 1.5f, p.y), p.y + 0.4f), Rand(rng, 0f, 360f));
                }
            }
            m.Build(group, "FarmWalls", layer, "Concrete");
            // A pump house beside the oasis.
            var ph = Rect.MinMaxRect(OasisCentre.x + OasisRadius + 8f, OasisCentre.y - 4f, OasisCentre.x + OasisRadius + 14f, OasisCentre.y + 2f);
            var b = NewMass(ph, rng, 1, true);
            b.groundH = 3.0f; b.door = 2;
            BuildMass(b, props);
            PlaceTown(props, Prop("portable_generator"), new Vector3(ph.xMax + 1.2f, GroundHeightAt(ph.xMax + 1.2f, ph.center.y), ph.center.y), 90f);
        }

        // ==================================================================
        /// <summary>The oasis: a pond in the hollow, palms crowding its shore, rocks, a stone landing, jerrycans.</summary>
        private static void BuildOasis(Transform group, int layer, Transform props, System.Random rng)
        {
            float shore = float.MinValue;
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2f / 24f;
                shore = Mathf.Max(shore, GroundHeightAt(OasisCentre.x + Mathf.Cos(a) * (OasisRadius - 2f), OasisCentre.y + Mathf.Sin(a) * (OasisRadius - 2f)));
            }
            float wy = shore - 0.35f;
            var m = new TownMesh();
            m.Disc(_townWater, new Vector3(OasisCentre.x, wy, OasisCentre.y), OasisRadius + 3f, 64, Vector3.up);
            // A stone landing for drawing water.
            m.BoxMinMax(_stone, new Vector3(OasisCentre.x - 3f, wy - 1.5f, OasisCentre.y - OasisRadius - 1f), new Vector3(OasisCentre.x + 3f, wy + 0.25f, OasisCentre.y - OasisRadius + 3f));
            var pond = m.Build(group, "Oasis", layer, "Water", collider: false, shadows: ShadowCastingMode.Off);
            if (pond != null) pond.AddComponent<ScrollingWater>().scrollSpeed = new Vector2(0.004f, 0.007f);
            NoEntryBox(Rect.MinMaxRect(OasisCentre.x - OasisRadius, OasisCentre.y - OasisRadius, OasisCentre.x + OasisRadius, OasisCentre.y + OasisRadius), wy - 6f, wy + 1.5f, 2f);
            var palms = new GameObject("OasisPalms").transform;
            palms.SetParent(group, false);
            string[] kinds = { "Palm_SingleBended", "Palm_DualBended", "Palm_Trio", "Palm_SingleStraight" };
            for (int i = 0; i < 70; i++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f), d = OasisRadius + Rand(rng, 1f, 22f);
                var p = new Vector2(OasisCentre.x + Mathf.Cos(a) * d, OasisCentre.y + Mathf.Sin(a) * d);
                if (Mathf.Abs(p.x - OasisCentre.x) < 4f && p.y < OasisCentre.y) continue;
                PlaceTown(palms, PackPrefab(kinds[rng.Next(kinds.Length)]), new Vector3(p.x, GroundHeightAt(p.x, p.y) - 0.1f, p.y), Rand(rng, 0f, 360f), Rand(rng, 7f, 14f), navBlock: false);
            }
            string[] rocks = { "namaqualand_boulder_02", "namaqualand_boulder_03", "namaqualand_boulder_04", "namaqualand_boulder_05" };
            for (int i = 0; i < 16; i++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f), d = OasisRadius + Rand(rng, -1f, 6f);
                var p = new Vector2(OasisCentre.x + Mathf.Cos(a) * d, OasisCentre.y + Mathf.Sin(a) * d);
                PlaceTown(props, Prop(rocks[rng.Next(rocks.Length)]), new Vector3(p.x, GroundHeightAt(p.x, p.y) - 0.3f, p.y), Rand(rng, 0f, 360f), -1f, Rand(rng, 0.8f, 1.6f));
            }
            for (int i = 0; i < 5; i++)
                PlaceTown(props, Prop("plastic_jerrycan"), new Vector3(OasisCentre.x + Rand(rng, -2.5f, 2.5f), wy + 0.26f, OasisCentre.y - OasisRadius + Rand(rng, 0f, 2f)), Rand(rng, 0f, 360f));
            _oasisWater = wy;
        }

        private static float _oasisWater;

        // ==================================================================
        private static void BuildHighwayDressing(Transform group, int layer, Transform props, System.Random rng)
        {
            var m = new TownMesh();
            var hw = _townRoads.Find(r => r.kind == RoadKind.Highway);
            if (hw == null) return;
            var gaps = new List<Vector2>();
            foreach (var j in JunctionsOn(hw)) { var other = j.x == hw ? j.z : j.x; gaps.Add(new Vector2(j.at.x - other.Corridor - 12f, j.at.x + other.Corridor + 12f)); }
            gaps.Add(new Vector2(GasStationRect.xMin - 4f, GasStationRect.xMax + 4f));
            // Guardrails along both edges outside the town's junctions: a W-beam on posts.
            foreach (float side in new[] { -1f, 1f })
                foreach (var (a, b) in Minus(-520f, 520f, new List<Vector2>(gaps)))
                {
                    float zr = hw.Fixed + side * (hw.Half + 1.1f);
                    for (float x = a; x < b - 0.1f; x += 4f)
                    {
                        float xe = Mathf.Min(x + 4f, b);
                        float y0 = TownRoadY(x, hw.Fixed), y1 = TownRoadY(xe, hw.Fixed);
                        m.Box(_frameAlu, new Vector3(x, y0 + 0.55f, zr), new Vector3(0.12f, 1.1f, 0.12f), Quaternion.identity);
                        var p0 = new Vector3(x, y0 + 0.65f, zr); var p1 = new Vector3(xe, y1 + 0.65f, zr);
                        m.Box(_frameAlu, (p0 + p1) * 0.5f, new Vector3(Vector3.Distance(p0, p1), 0.32f, 0.05f), Quaternion.LookRotation(Vector3.forward * -side) * Quaternion.identity);
                    }
                }
            m.Build(group, "Guardrails", layer, "Metal");

            // Power line along the north side of the highway: poles every 45 m with three sagging wires.
            PowerLine(group, layer, props, new Vector3(-690f, 0, hw.Fixed + hw.Half + 7f), new Vector3(690f, 0, hw.Fixed + hw.Half + 7f), 45f, gaps);

            // Speed signs and the roadblocks where the highway leaves the map.
            RoadSign(group, layer, new Vector3(180f, TownRoadY(180f, hw.Fixed) + 0.1f, hw.Fixed - hw.Half - 2.5f), 90f, "speed80", 2.6f);
            RoadSign(group, layer, new Vector3(-180f, TownRoadY(-180f, hw.Fixed) + 0.1f, hw.Fixed + hw.Half + 2.5f), 270f, "speed80", 2.6f);
            var barrier = Prop("concrete_road_barrier_02") ?? Prop("concrete_road_barrier");
            foreach (float sx in new[] { -1f, 1f })
            {
                float x = sx * (_theme.arenaSize * 0.5f - 6f);
                for (float z = hw.Fixed - hw.Half - 1f; z <= hw.Fixed + hw.Half + 1f; z += 2.1f)
                    PlaceTown(props, barrier, new Vector3(x, TownRoadY(x, z), z), 90f);
                RoadSign(group, layer, new Vector3(x - sx * 3f, TownRoadY(x - sx * 3f, hw.Fixed + hw.Half + 2f), hw.Fixed + hw.Half + 2f), sx > 0 ? 270f : 90f, "noentry", 2.6f);
                RoadSign(group, layer, new Vector3(x - sx * 3f, TownRoadY(x - sx * 3f, hw.Fixed - hw.Half - 2f), hw.Fixed - hw.Half - 2f), sx > 0 ? 270f : 90f, "danger", 2.6f);
            }
        }

        /// <summary>A line of wooden poles with cross-arms and three wires sagging between them.</summary>
        private static void PowerLine(Transform group, int layer, Transform props, Vector3 from, Vector3 to, float spacing, List<Vector2> skipX = null)
        {
            var m = new TownMesh();
            var wood = TownMat("PoleWood", "weathered_planks", new Color(0.55f, 0.45f, 0.36f), 1.2f);
            var wire = TownFlat("Wire", new Color(0.05f, 0.05f, 0.05f), 0.3f);
            Vector3 d = to - from; float len = new Vector2(d.x, d.z).magnitude; d /= len;
            var side = Vector3.Cross(Vector3.up, d);
            Vector3? prev = null;
            for (float s = 0f; s <= len; s += spacing)
            {
                var p = from + d * s;
                float g = GroundHeightAt(p.x, p.z);
                var basePt = new Vector3(p.x, g, p.z);
                m.Cylinder(wood, basePt + Vector3.down * 0.5f, 0.14f, 9.5f, 8, true, false, 0.11f);
                m.Box(wood, basePt + Vector3.up * 8.4f, new Vector3(2.4f, 0.12f, 0.12f), Quaternion.LookRotation(d));
                var top = basePt + Vector3.up * 8.55f;
                if (prev.HasValue)
                    foreach (float o in new[] { -1.05f, 0f, 1.05f })
                    {
                        var a = prev.Value + side * o; var b = top + side * o;
                        const int segs = 8;
                        for (int k = 0; k < segs; k++)
                        {
                            float t0 = k / (float)segs, t1 = (k + 1) / (float)segs;
                            var q0 = Vector3.Lerp(a, b, t0) - Vector3.up * (4f * t0 * (1f - t0) * 1.1f);
                            var q1 = Vector3.Lerp(a, b, t1) - Vector3.up * (4f * t1 * (1f - t1) * 1.1f);
                            m.Box(wire, (q0 + q1) * 0.5f, new Vector3(0.025f, 0.025f, Vector3.Distance(q0, q1)), Quaternion.LookRotation(q1 - q0));
                        }
                    }
                prev = top;
            }
            var go = m.Build(group, "PowerLine", layer, "Wood", collider: false);
            if (go != null) Hide(go);
            _ = props; _ = skipX;
        }

        // ==================================================================
        /// <summary>
        /// The open desert: photo-scanned boulders and stone fields on the dunes, low shrubs and succulents in the
        /// hollows, dead trees, and a few big rock outcrops on the boundary ridge to be seen from town.
        /// </summary>
        private static void BuildWildDesert(Transform group, int layer, Transform props, System.Random rng)
        {
            var wild = new GameObject("WildDesert").transform;
            wild.SetParent(group, false);
            float half = _theme.arenaSize * 0.5f;
            string[] boulders = { "namaqualand_boulder_02", "namaqualand_boulder_03", "namaqualand_boulder_04", "namaqualand_boulder_05", "namaqualand_boulder_06", "namaqualand_boulders_01" };
            // Photo-scans are 60-110k triangles a rock, so they are the near accents; the bulk of the stones and scrub is
            // the kit's own light meshes (BoulderMesh, ScrubMesh) in the scanned rock and a dry-scrub colour.
            string[] small = { "namaqualand_boulder_05", "namaqualand_boulder_06" };
            string[] shrubs = { "wild_rooibos_bush", "dry_branches_medium_01" };
            var rockMat = TownMat("DesertRock", "rock_ground", new Color(0.92f, 0.78f, 0.62f), 2.5f);
            var scrubMat = TownFlat("DryScrub", new Color(0.42f, 0.40f, 0.26f), 0.08f);
            var scrubDead = TownFlat("DeadScrub", new Color(0.52f, 0.44f, 0.33f), 0.08f);
            string[] dead = { "dead_tree_trunk_02", "dead_quiver_trunk", "dead_quiver_branch_01" };
            int placed = 0;
            for (int tries = 0; tries < 6000 && placed < 700; tries++)
            {
                var p = new Vector2(Rand(rng, -half - 120f, half + 120f), Rand(rng, -half - 120f, half + 120f));
                float dev = DevDistanceAt(p.x, p.y);
                bool inside = Mathf.Abs(p.x) < half && Mathf.Abs(p.y) < half;
                if (dev < 6f || OnCarriage(p, 3f) || InSolid(p, 2f)) continue;
                if (Vector2.Distance(p, OasisCentre) < OasisRadius + 4f) continue;
                double pick = rng.NextDouble();
                var g = new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y);
                if (pick < 0.06) PlaceTown(wild, Prop(boulders[rng.Next(boulders.Length)]), g - Vector3.up * 0.35f, Rand(rng, 0f, 360f), -1f, Rand(rng, 0.8f, 2.4f));
                else if (pick < 0.12) PlaceTown(wild, Prop(small[rng.Next(small.Length)]), g - Vector3.up * 0.1f, Rand(rng, 0f, 360f), -1f, Rand(rng, 0.8f, 1.5f));
                else if (pick < 0.30)
                {
                    // Sunk to the lowest ground under it (a rock is a hollow shell: see FPSKitTerrain.LowestGroundIn).
                    float rs = Rand(rng, 0.3f, 1.1f);
                    var rock = MeshObject(wild, "Rock", BoulderMesh(rng.Next(400), 0.36f, Rand(rng, 0.45f, 0.75f)), rockMat,
                                          new Vector3(g.x, LowestGroundIn(g.x, g.z, rs * 1.2f) - 0.12f, g.z),
                                          Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f), Vector3.one * rs, LayerMask.NameToLayer("Environment"), "Concrete");
                    NoStanding(rock);
                }
                else if (pick < 0.44) PlaceTown(wild, Prop(shrubs[rng.Next(shrubs.Length)]), g, Rand(rng, 0f, 360f), -1f, Rand(rng, 0.7f, 1.4f), navBlock: false);
                else if (pick < 0.94)
                {
                    var scrub = MeshObject(wild, "Scrub", ScrubMesh(rng.Next(200)), rng.NextDouble() < 0.7 ? scrubMat : scrubDead, g,
                                           Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f), Vector3.one * Rand(rng, 0.6f, 1.5f), LayerMask.NameToLayer("Environment"), null, collider: false);
                    scrub.AddComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild = true;
                    Hide(scrub);
                }
                else PlaceTown(wild, Prop(dead[rng.Next(dead.Length)]), g - Vector3.up * 0.1f, Rand(rng, 0f, 360f), -1f, Rand(rng, 0.9f, 1.6f));
                placed++;
                _ = inside;
            }
            // Shrubs in town too: in vacant lots, along wall feet and the edges of yards.
            for (int tries = 0; tries < 2000 && placed < 1000; tries++)
            {
                var p = new Vector2(Rand(rng, -half, half), Rand(rng, -half, half));
                if (DevDistanceAt(p.x, p.y) > 0.5f || OnCarriage(p, 1.5f) || RoadAt(p, 0.5f) != null || InSolid(p, 0.6f)) continue;
                PlaceTown(wild, Prop(shrubs[rng.Next(shrubs.Length)]), new Vector3(p.x, GroundHeightAt(p.x, p.y), p.y), Rand(rng, 0f, 360f), -1f, Rand(rng, 0.5f, 1.0f), navBlock: false);
                placed++;
            }
            // (The Poly Haven cliff scans are single faces of rock, not whole outcrops: scaled up on the ridge they read as
            // a sheet of cardboard from behind, so the skyline is left to the mountains.)
        }

        // ==================================================================
        /// <summary>A Bedouin camp out in the southern dunes: a black goat-hair tent, a fire ring, rugs, a water tank.</summary>
        private static void BuildBedouinCamp(Transform group, int layer, Transform props, System.Random rng)
        {
            var c = new Vector2(330f, -462f);
            float g = GroundHeightAt(c.x, c.y);
            var m = new TownMesh();
            const float W = 10f, D = 6f, H = 2.4f;
            // Poles and a sagging cloth roof, the back and sides let down.
            for (int i = 0; i < 3; i++)
                for (int k = 0; k < 2; k++)
                {
                    var p = new Vector3(c.x - W * 0.5f + i * W * 0.5f, g, c.y - D * 0.5f + k * D);
                    m.Cylinder(_woodBeam, p, 0.05f, H - k * 0.6f, 6);
                }
            const int nx = 8, nz = 4;
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    Vector3 P(int a, int b)
                    {
                        float x = c.x - W * 0.5f + W * a / nx, z = c.y - D * 0.5f + D * b / nz;
                        float sag = 0.25f * Mathf.Sin(Mathf.PI * (a % (nx / 2)) / (nx / 2f));
                        return new Vector3(x, g + H - 0.6f * b / nz - sag, z);
                    }
                    m.Quad(_tent, P(i, k), P(i + 1, k), P(i + 1, k + 1), P(i, k + 1), Vector3.up);
                    m.Quad(_tent, P(i, k), P(i + 1, k), P(i + 1, k + 1), P(i, k + 1), Vector3.down);
                }
            m.Quad(_tent, new Vector3(c.x - W * 0.5f, g, c.y + D * 0.5f), new Vector3(c.x + W * 0.5f, g, c.y + D * 0.5f),
                   new Vector3(c.x + W * 0.5f, g + H - 0.6f, c.y + D * 0.5f), new Vector3(c.x - W * 0.5f, g + H - 0.6f, c.y + D * 0.5f), Vector3.forward);
            m.Quad(_tent, new Vector3(c.x - W * 0.5f, g, c.y + D * 0.5f), new Vector3(c.x + W * 0.5f, g, c.y + D * 0.5f),
                   new Vector3(c.x + W * 0.5f, g + H - 0.6f, c.y + D * 0.5f), new Vector3(c.x - W * 0.5f, g + H - 0.6f, c.y + D * 0.5f), Vector3.back);
            // Rugs under it.
            var cell = new Rect(0.5f, 0.0f, 0.5f, 0.5f);
            m.QuadUV(_carpet, new Vector3(c.x - 4f, g + 0.03f, c.y - 2.5f), new Vector3(c.x + 4f, g + 0.03f, c.y - 2.5f), new Vector3(c.x + 4f, g + 0.03f, c.y + 2.5f), new Vector3(c.x - 4f, g + 0.03f, c.y + 2.5f), Vector3.up,
                     new Vector2(cell.xMin, cell.yMin), new Vector2(cell.xMax, cell.yMin), new Vector2(cell.xMax, cell.yMax), new Vector2(cell.xMin, cell.yMax));
            m.Build(group, "BedouinTent", layer, "Wood", collider: false);
            // A fire ring of stones, a kettle, a water tank, jerrycans.
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f;
            {
                var stone = MeshObject(props, "FireStone", BoulderMesh(i + 7), TownMat("DesertRock", "rock_ground", new Color(0.92f, 0.78f, 0.62f), 2.5f),
                                       new Vector3(c.x + Mathf.Cos(a) * 1.0f, g, c.y - 6f + Mathf.Sin(a) * 1.0f), Quaternion.identity, Vector3.one * 0.22f, layer, "Concrete", collider: false);
                Hide(stone);
            }
            }
            PlaceTown(props, Prop("pot_enamel_01"), new Vector3(c.x, g, c.y - 6f), 0f);
            PlaceTown(props, Prop("plastic_jerrycan"), new Vector3(c.x + 5.5f, g, c.y - 1f), 30f);
            PlaceTown(props, Prop("plastic_jerrycan"), new Vector3(c.x + 5.9f, g, c.y - 0.4f), 80f);
            PlaceTown(props, Prop("propane_tank"), new Vector3(c.x - 5.6f, g, c.y), 0f);
            _campSpot = new Vector4(c.x + 8f, g, c.y - 3f, 20f);
        }

        private static Vector4 _campSpot;
    }
}
#endif
