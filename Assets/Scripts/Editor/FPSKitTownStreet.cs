#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// What stands in the street: lamps over the carriageways, traffic signals at the avenue's junctions (real heads the
    /// traffic obeys, see TownSignals), stop and give-way signs, speed, direction and street-name signs in Arabic and
    /// English, wooden poles carrying wires down the residential streets, palms in the avenue's median and the
    /// boulevard's pavements, benches, bins, manhole covers, the old-town square's well, and litter.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static TownMesh _signMesh;
        private static Material _signal, _signalRed, _signalAmber, _signalGreen, _signalOff;

        /// <summary>Signal heads per junction, for the runtime controller: junction centre and its heads (FPSKitTownLife wires them).</summary>
        private static readonly List<(TownJunction j, List<(GameObject head, bool alongX)> heads)> _signalHeads = new List<(TownJunction, List<(GameObject, bool)>)>();

        private static TownMesh SignMesh() => _signMesh ??= new TownMesh();

        private static void BuildTownStreet(Transform root, int layer, System.Random rng)
        {
            var group = new GameObject("Street").transform;
            group.SetParent(root, false);
            var props = new GameObject("StreetProps").transform;
            props.SetParent(group, false);
            _signalHeads.Clear();
            _signal = TownFlat("SignalBody", new Color(0.12f, 0.13f, 0.12f), 0.4f, 0.5f);
            _signalOff = TownFlat("SignalOff", new Color(0.08f, 0.08f, 0.08f), 0.7f);
            _signalRed = EmissiveFlat("SignalRed", new Color(1f, 0.08f, 0.05f), 4f);
            _signalAmber = EmissiveFlat("SignalAmber", new Color(1f, 0.6f, 0.05f), 4f);
            _signalGreen = EmissiveFlat("SignalGreen", new Color(0.1f, 1f, 0.45f), 4f);

            BuildStreetLamps(group, props, rng);
            BuildSignals(group, layer);
            BuildRoadSigns(group, layer);
            BuildStreetPoles(group, layer, props);
            BuildStreetTrees(group, props, rng);
            BuildStreetFurniture(group, layer, props, rng);

            var go = SignMesh().Build(group, "Signs", layer, "Metal");
            if (go != null) Hide(go);
            _signMesh = null;
        }

        private static Material EmissiveFlat(string name, Color c, float intensity)
        {
            var m = TownFlat(name, c * 0.6f, 0.6f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * intensity);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// Places a prefab whose shape reaches out to one side (a lamp's arm) so that side points along
        /// <paramref name="toward"/>, whatever way its author modelled it: measured from its bounds at yaw 0.
        /// </summary>
        private static GameObject PlaceReaching(Transform parent, GameObject prefab, Vector3 pos, Vector3 toward, float height)
        {
            if (prefab == null) return null;
            var go = PlaceTown(parent, prefab, pos, 0f, height);
            if (go == null) return null;
            var b = WorldBounds(go);
            var off = b.center - go.transform.position; off.y = 0f;
            if (off.sqrMagnitude < 0.01f) { go.transform.rotation = Quaternion.LookRotation(toward); return go; }
            float arm = Mathf.Atan2(off.x, off.z) * Mathf.Rad2Deg, want = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
            go.transform.RotateAround(pos, Vector3.up, want - arm);
            return go;
        }

        private static void BuildStreetLamps(Transform group, Transform props, System.Random rng)
        {
            var lamps = new GameObject("Lamps").transform;
            lamps.SetParent(group, false);
            var lampA = Prop("street_lamp_01");
            // street_lamp_02 is a wall bracket, not a pole: the pole lamp serves everywhere.
            var lampB = lampA;
            foreach (var r in _townRoads)
            {
                if (!r.Paved || r.kind == RoadKind.Highway) continue;
                var clear = new List<Vector2>();
                foreach (var j in JunctionsOn(r)) { var o = j.x == r ? j.z : j.x; float at = r.AlongX ? j.at.x : j.at.y; clear.Add(new Vector2(at - o.Corridor - 6f, at + o.Corridor + 6f)); }
                float c = r.AlongX ? RoundaboutCentre.x : RoundaboutCentre.y;
                if (Mathf.Abs(r.Fixed - (r.AlongX ? RoundaboutCentre.y : RoundaboutCentre.x)) < 1f) clear.Add(new Vector2(c - RoundaboutWalk - 4f, c + RoundaboutWalk + 4f));
                float spacing = r.kind == RoadKind.Street ? 34f : 30f;
                int n = 0;
                foreach (var (a, b) in Minus(r.Min + 8f, r.Max - 8f, clear))
                    for (float s = a + 4f; s < b - 2f; s += spacing, n++)
                    {
                        if (r.median > 0f)
                        {
                            // Double lamps on the median, one arm over each carriageway.
                            var p = RoadPoint(r, s, 0f, KerbHeight + 0.08f);
                            Vector3 left = r.AlongX ? Vector3.back : Vector3.right;
                            RoadLamp(p, left, 10f, true);
                        }
                        else
                        {
                            float side = (n % 2 == 0 || r.kind == RoadKind.Street) ? 1f : -1f;
                            if (r.kind == RoadKind.Street && r.name.StartsWith("West")) side = -1f;
                            var p = RoadPoint(r, s, side * (r.Half + 0.55f), KerbHeight);
                            Vector3 inward = (r.AlongX ? new Vector3(0, 0, side) : new Vector3(-side, 0, 0));
                            RoadLamp(p, inward, r.kind == RoadKind.Street ? 8f : 9.5f, false);
                        }
                    }
            }
            // Round the roundabout's outer pavement and on the mosque plaza.
            for (int i = 0; i < 8; i++)
            {
                float a = (i + 0.5f) * Mathf.PI / 4f;
                var p2 = RoundaboutCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (RoundaboutOuter + 0.7f);
                if (OnRoadMouth(p2)) continue;
                PlaceReaching(lamps, lampB, new Vector3(p2.x, TownRoadY(p2.x, p2.y) + KerbHeight, p2.y), -new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)), 4.2f);
            }
            foreach (float x in new[] { -40f, -14f, 14f, 40f })
                PlaceTown(lamps, lampA, new Vector3(x, SampleTownBase(x, 168f) + 0.25f, 168f), 0f, 4.2f);
            _ = props; _ = rng;
        }

        /// <summary>
        /// A road lamp of the kind these towns put up: a tapered galvanised pole, an arm reaching over the carriageway
        /// (two, back to back, on a median), and a flat LED head. Into the shared sign mesh: one draw for every lamp.
        /// </summary>
        private static void RoadLamp(Vector3 foot, Vector3 over, float height, bool both)
        {
            var m = SignMesh();
            var galv = TownFlat("Galvanised", new Color(0.58f, 0.59f, 0.58f), 0.45f, 0.7f);
            var head = TownFlat("LampHead", new Color(0.20f, 0.21f, 0.22f), 0.4f, 0.4f);
            var lens = TownFlat("LampLens", new Color(0.85f, 0.85f, 0.80f), 0.8f);
            m.BoxMinMax(galv, foot + new Vector3(-0.2f, -0.05f, -0.2f), foot + new Vector3(0.2f, 0.06f, 0.2f));
            m.Cylinder(galv, foot, 0.11f, height, 10, true, false, 0.065f);
            foreach (float sgn in both ? new[] { 1f, -1f } : new[] { 1f })
            {
                var dir = over.normalized * sgn;
                var a = foot + Vector3.up * (height - 0.25f);
                var b = a + dir * 2.2f + Vector3.up * 0.35f;
                m.Box(galv, (a + b) * 0.5f, new Vector3(0.07f, 0.07f, Vector3.Distance(a, b)), Quaternion.LookRotation(b - a));
                var rot = Quaternion.LookRotation(dir);
                m.Box(head, b + dir * 0.3f, new Vector3(0.32f, 0.12f, 0.75f), rot);
                m.Box(lens, b + dir * 0.3f - Vector3.up * 0.065f, new Vector3(0.26f, 0.01f, 0.6f), rot);
            }
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// Signal poles at the corners of every signalled junction, each with a head facing the traffic coming at it.
        /// The heads are separate objects (their lamps change at run time).
        /// </summary>
        private static void BuildSignals(Transform group, int layer)
        {
            var root = new GameObject("Signals").transform;
            root.SetParent(group, false);
            foreach (var j in _townJunctions)
            {
                if (!j.signals) continue;
                var heads = new List<(GameObject, bool)>();
                // Four approaches: from -z (northbound), +z, -x (eastbound), +x. The pole stands on the far-right corner as
                // the driver sees it before the junction (right-hand traffic: the near-right corner).
                foreach (var (dir, alongX) in new[] { (Vector3.forward, false), (Vector3.back, false), (Vector3.right, true), (Vector3.left, true) })
                {
                    var road = alongX ? j.x : j.z;
                    // Only where the road actually comes from that side.
                    float approachAt = alongX ? j.at.x - dir.x * (j.z.Corridor + 2f) : j.at.y - dir.z * (j.x.Corridor + 2f);
                    if (approachAt < road.Min || approachAt > road.Max) continue;
                    var right = Vector3.Cross(Vector3.up, dir);
                    var other = alongX ? j.z : j.x;
                    var corner = new Vector3(j.at.x, 0f, j.at.y) - dir * (other.Corridor - 0.8f) + right * (road.Half + 0.7f);
                    corner.y = TownRoadY(corner.x, corner.z) + KerbHeight;
                    var m = SignMesh();
                    m.Cylinder(_signal, corner, 0.09f, 3.2f, 10);
                    var head = new GameObject("SignalHead");
                    head.transform.SetParent(root, false);
                    head.transform.position = corner + Vector3.up * 2.7f;
                    head.transform.rotation = Quaternion.LookRotation(-dir);
                    var hm = new TownMesh();
                    hm.Box(_signal, Vector3.zero + Vector3.forward * 0.05f, new Vector3(0.36f, 1.0f, 0.26f), Quaternion.identity);
                    hm.Build(head.transform, "Body", layer, "Metal", collider: false);
                    for (int k = 0; k < 3; k++)
                    {
                        var lamp = new TownMesh();
                        lamp.Disc(_signalOff, Vector3.zero, 0.12f, 12, Vector3.forward);
                        var lgo = lamp.Build(head.transform, k == 0 ? "Red" : k == 1 ? "Amber" : "Green", layer, null, collider: false, shadows: ShadowCastingMode.Off);
                        lgo.transform.localPosition = new Vector3(0f, 0.32f - k * 0.32f, -0.085f);
                        lgo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                        lgo.GetComponent<MeshRenderer>().sharedMaterial = k == 2 ? _signalGreen : _signalOff;
                        // Visor over each lamp.
                        hm = new TownMesh();
                    }
                    heads.Add((head, alongX));
                }
                _signalHeads.Add((j, heads));
            }
        }

        // ------------------------------------------------------------------
        /// <summary>A road sign on a pole: the atlas cell on its face, the plain back behind. Faces <paramref name="yaw"/>.</summary>
        private static void RoadSign(Transform group, int layer, Vector3 basePos, float yaw, string cell, float height)
        {
            var m = SignMesh();
            var n = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var right = Vector3.Cross(Vector3.up, -n);
            m.Cylinder(_steel, basePos + Vector3.down * 0.3f, 0.035f, height + 0.3f, 8);
            const float S = 0.7f;
            var c = basePos + Vector3.up * (height - S * 0.5f) + n * 0.05f;
            var uv = RoadSignCell(cell);
            var back = RoadSignCell("back");
            Vector3 a = c - right * S * 0.5f - Vector3.up * S * 0.5f, b = c + right * S * 0.5f - Vector3.up * S * 0.5f;
            m.QuadUV(_roadSignMat, a, b, b + Vector3.up * S, a + Vector3.up * S, n,
                     new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMax, uv.yMin), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMin, uv.yMax));
            m.QuadUV(_roadSignMat, a - n * 0.01f, b - n * 0.01f, b + Vector3.up * S - n * 0.01f, a + Vector3.up * S - n * 0.01f, -n,
                     new Vector2(back.xMin, back.yMin), new Vector2(back.xMax, back.yMin), new Vector2(back.xMax, back.yMax), new Vector2(back.xMin, back.yMax));
            _ = group; _ = layer;
        }

        /// <summary>A rectangular sign (shop-sign atlas cell) on two posts, or on a wall when <paramref name="standing"/> is false.</summary>
        private static void SignPanel(Transform group, int layer, Vector3 pos, float yaw, string cell, float width, float height, bool standing = true)
        {
            var m = SignMesh();
            var n = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var right = Vector3.Cross(Vector3.up, -n);
            float h = width * 0.25f;
            var c = pos + Vector3.up * (standing ? height - h * 0.5f : 0f);
            if (standing)
                foreach (float s in new[] { -0.4f, 0.4f })
                    m.Cylinder(_steel, pos + right * (s * width) + Vector3.down * 0.3f, 0.05f, height - h + 0.35f, 8);
            m.Box(_steel, c - n * 0.03f, new Vector3(width + 0.08f, h + 0.08f, 0.04f), Quaternion.LookRotation(n));
            var uv = SignCell(cell);
            Vector3 a = c - right * width * 0.5f - Vector3.up * h * 0.5f, b = c + right * width * 0.5f - Vector3.up * h * 0.5f;
            m.QuadUV(_townSignMat, a, b, b + Vector3.up * h, a + Vector3.up * h, n,
                     new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMax, uv.yMin), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMin, uv.yMax));
            _ = group; _ = layer;
        }

        private static void BuildRoadSigns(Transform group, int layer)
        {
            // Stop signs on the minor arm of every unsignalled junction; give-way on every roundabout entry.
            foreach (var j in _townJunctions)
            {
                if (j.signals) continue;
                // The minor road: a street meeting a bigger road, or the one that ends here.
                TownRoad minor = Rank(j.x) < Rank(j.z) ? j.x : Rank(j.z) < Rank(j.x) ? j.z : (j.Tee ? (EndsAt(j.x, j.at) ? j.x : j.z) : null);
                if (minor == null || minor.kind == RoadKind.Track) continue;
                var major = minor == j.x ? j.z : j.x;
                foreach (var dir in minor.AlongX ? new[] { Vector3.right, Vector3.left } : new[] { Vector3.forward, Vector3.back })
                {
                    var approach = new Vector3(j.at.x, 0, j.at.y) - dir * (major.Corridor + 1.2f);
                    float along = minor.AlongX ? approach.x : approach.z;
                    if (along < minor.Min || along > minor.Max) continue;
                    var right = Vector3.Cross(Vector3.up, dir);
                    var p = approach + right * (minor.Half + 0.6f);
                    p.y = TownRoadY(p.x, p.z) + KerbHeight;
                    RoadSign(group, layer, p, Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg, "stop", 2.3f);
                }
            }
            foreach (var r in _townRoads)
            {
                float dA = Vector2.Distance(r.a, RoundaboutCentre), dB = Vector2.Distance(r.b, RoundaboutCentre);
                if (Mathf.Min(dA, dB) > RoundaboutOuter + 3f) continue;
                Vector2 near = dA < dB ? r.a : r.b, far = dA < dB ? r.b : r.a;
                var dir = new Vector3(near.x - far.x, 0, near.y - far.y).normalized;       // driving towards the ring
                var right = Vector3.Cross(Vector3.up, dir);
                var p = new Vector3(RoundaboutCentre.x, 0, RoundaboutCentre.y) - dir * (RoundaboutWalk + 1.5f) + right * (r.Half + 0.7f);
                p.y = TownRoadY(p.x, p.z) + KerbHeight;
                RoadSign(group, layer, p, Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg, "giveway", 2.3f);
                RoadSign(group, layer, p - dir * 8f, Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg, "roundabout", 2.3f);
            }
            // Speed limits and direction signs on the avenue, street names at the main corners.
            RoadSign(group, layer, new Vector3(13.5f, TownRoadY(13.5f, -330f) + KerbHeight, -330f), 180f, "speed50", 2.5f);
            RoadSign(group, layer, new Vector3(-13.5f, TownRoadY(-13.5f, -100f) + KerbHeight, -100f), 0f, "speed50", 2.5f);
            SignPanel(group, layer, new Vector3(13.5f, TownRoadY(13.5f, -300f) + KerbHeight, -300f), 180f, "dir_mosque", 3.0f, 3.6f);
            SignPanel(group, layer, new Vector3(14.0f, TownRoadY(14f, -292f) + KerbHeight, -292f), 180f, "dir_citadel", 3.0f, 3.6f);
            SignPanel(group, layer, new Vector3(-13.5f, TownRoadY(-13.5f, -160f) + KerbHeight, -160f), 0f, "dir_highway", 3.0f, 3.6f);
            SignPanel(group, layer, new Vector3(-13.5f, TownRoadY(-13.5f, -140f) + KerbHeight, -140f), 0f, "dir_palace", 3.0f, 3.6f);
            SignPanel(group, layer, new Vector3(21f, TownRoadY(21f, 10.5f) + KerbHeight, 10.5f), 180f, "dir_souk", 2.6f, 3.4f);
            SignPanel(group, layer, new Vector3(-60f, SampleTownBase(-60f, 205f) + 0.1f, 210f), 180f, "dir_oasis", 2.6f, 3.0f);
            foreach (var (x, z, yaw, cell) in new[] { (14f, -116f, 225f, "st_mosque"), (-14f, -144f, 45f, "st_palace"), (14f, -246f, 225f, "st_station"),
                                                      (24f, 31f, 200f, "st_palm"), (124f, 207f, 200f, "st_citadel"), (-20f, 31f, 135f, "st_souk") })
                SignPanel(group, layer, new Vector3(x, TownRoadY(x, z) + KerbHeight, z), yaw, cell, 1.4f, 2.8f);
        }

        private static int Rank(TownRoad r) => r.kind == RoadKind.Highway ? 5 : r.kind == RoadKind.Avenue ? 4 : r.kind == RoadKind.Boulevard ? 3 :
                                               r.kind == RoadKind.Street ? (r.name == "WestRoad" || r.name == "SouthStreet" ? 2 : 1) : 0;

        private static bool EndsAt(TownRoad r, Vector2 at) => Vector2.Distance(r.a, at) < 1f || Vector2.Distance(r.b, at) < 1f;

        // ------------------------------------------------------------------
        private static void BuildStreetPoles(Transform group, int layer, Transform props)
        {
            foreach (var r in _townRoads)
            {
                if (r.kind != RoadKind.Street || r.Length < 80f) continue;
                float side = r.name.StartsWith("East") ? 1f : -1f;
                float across = side * (r.Corridor - 0.35f);
                Vector3 a = RoadPoint(r, r.Min + 10f, across), b = RoadPoint(r, r.Max - 10f, across);
                PowerLine(group, layer, props, a, b, 38f);
            }
        }

        // ------------------------------------------------------------------
        private static void BuildStreetTrees(Transform group, Transform props, System.Random rng)
        {
            var trees = new GameObject("StreetPalms").transform;
            trees.SetParent(group, false);
            foreach (var r in _townRoads)
            {
                if (r.kind != RoadKind.Avenue && r.kind != RoadKind.Boulevard) continue;
                var clear = new List<Vector2>();
                foreach (var j in JunctionsOn(r)) { var o = j.x == r ? j.z : j.x; float at = r.AlongX ? j.at.x : j.at.y; clear.Add(new Vector2(at - o.Corridor - 8f, at + o.Corridor + 8f)); }
                foreach (var (a, b) in Minus(r.Min + 10f, r.Max - 10f, clear))
                    for (float s = a + 15f; s < b - 4f; s += 30f)
                    {
                        if (r.median > 0f)
                            PlaceTown(trees, PackPrefab("Palm_SingleStraight"), RoadPoint(r, s, 0f, KerbHeight + 0.08f), Rand(rng, 0f, 360f), Rand(rng, 9f, 11f), navBlock: false);
                        else
                            foreach (float side in new[] { -1f, 1f })
                            {
                                var p = RoadPoint(r, s + side * 7f, side * (r.Half + 1.4f), KerbHeight);
                                if (InSolid(new Vector2(p.x, p.z), 0.5f)) continue;
                                PlaceTown(trees, PackPrefab("Palm_SingleStraight"), p, Rand(rng, 0f, 360f), Rand(rng, 8f, 10f), navBlock: false);
                                // The tree pit.
                                SignMesh().BoxMinMax(_kerb, p + new Vector3(-0.7f, -0.05f, -0.7f), p + new Vector3(0.7f, 0.04f, 0.7f));
                            }
                    }
            }
            _ = props;
        }

        // ------------------------------------------------------------------
        private static void BuildStreetFurniture(Transform group, int layer, Transform props, System.Random rng)
        {
            var bench = Prop("modular_street_seating") ?? Prop("painted_wooden_bench");
            var bin = Prop("metal_trash_can");
            var box = Prop("utility_box_01");
            var box2 = Prop("utility_box_02") ?? box;
            var manhole = Prop("water_manhole_cover");
            foreach (var r in _townRoads)
            {
                if (!r.Paved) continue;
                // Manhole covers down the middle of each carriageway.
                for (float s = r.Min + 25f; s < r.Max - 25f; s += 55f)
                {
                    float across = r.median > 0f ? r.median * 0.5f + 3.2f : 1.6f;
                    var p = RoadPoint(r, s, across, 0.012f);
                    if (OnCarriage(new Vector2(p.x, p.z)) && !InJunction(new Vector2(p.x, p.z)))
                        PlaceTown(props, manhole, p, Rand(rng, 0f, 360f), navBlock: false);
                }
                if (r.sidewalk <= 0f) continue;
                // Benches and bins on the main roads' pavements, utility boxes at the back of the pavement here and there.
                bool main = IsMain(r);
                for (float s = r.Min + 18f; s < r.Max - 18f; s += main ? 26f : 60f)
                {
                    float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                    var p = RoadPoint(r, s, side * (r.Corridor - 0.6f), KerbHeight);
                    var g = new Vector2(p.x, p.z);
                    if (InJunction(g) || InSolid(g, 0.4f)) continue;
                    float yaw = r.AlongX ? (side > 0 ? 180f : 0f) : (side > 0 ? 270f : 90f);
                    if (main && rng.NextDouble() < 0.5) PlaceTown(props, bench, p, yaw);
                    else if (rng.NextDouble() < 0.6) PlaceTown(props, bin, p + Vector3.up * 0.0f, Rand(rng, 0f, 360f));
                    else PlaceTown(props, rng.NextDouble() < 0.5 ? box : box2, p, yaw);
                }
            }
            // Mosque plaza: benches under palms in big pots.
            foreach (float x in new[] { -30f, -18f, 18f, 30f })
            {
                float y = SampleTownBase(x, 160f) + 0.25f;
                PlaceTown(props, bench, new Vector3(x, y, 158f), 0f);
                PlaceTown(props, Prop("planter_pot_clay") ?? Prop("ceramic_pot"), new Vector3(x + 5f, y, 176f), 0f, 1.0f);
                PlaceTown(props, PackPrefab("Palm_SingleBended"), new Vector3(x + 5f, y + 0.6f, 176f), Rand(rng, 0f, 360f), 6f, navBlock: false);
            }
            // The old-town squares: a well with its timber frame, a palm, benches, a few chairs.
            foreach (var sq in _oldSquares)
            {
                var c = new Vector3(sq.center.x + sq.width * 0.2f, FloorLevel(sq) - 0.1f, sq.center.y - sq.height * 0.2f);
                var m = new TownMesh();
                m.Cylinder(_stoneWarm, c, 1.1f, 0.85f, 16, true, false);
                m.Disc(_shopDark, c + Vector3.up * 0.86f, 0.85f, 16, Vector3.up);
                foreach (float s in new[] { -1f, 1f }) m.Box(_woodBeam, c + new Vector3(s * 1.0f, 1.5f, 0f), new Vector3(0.12f, 1.6f, 0.12f), Quaternion.identity);
                m.Box(_woodBeam, c + new Vector3(0f, 2.3f, 0f), new Vector3(2.2f, 0.12f, 0.12f), Quaternion.identity);
                m.Build(group, "Well", layer, "Concrete");
                _townSolids.Add(Rect.MinMaxRect(c.x - 1.1f, c.z - 1.1f, c.x + 1.1f, c.z + 1.1f));
                PlaceTown(props, PackPrefab("Palm_DualBended"), new Vector3(sq.center.x - sq.width * 0.25f, c.y, sq.center.y + sq.height * 0.2f), Rand(rng, 0f, 360f), 10f, navBlock: false);
                PlaceTown(props, Prop("painted_wooden_bench") ?? bench, new Vector3(sq.center.x, c.y, sq.yMax - 2f), 180f);
                for (int i = 0; i < 3; i++)
                    PlaceTown(props, Prop("plastic_monobloc_chair_01"), new Vector3(sq.center.x + Rand(rng, -3f, 3f), c.y, sq.center.y + Rand(rng, -3f, 3f)), Rand(rng, 0f, 360f));
                _civicSpots.Add(new Vector4(c.x, c.y, c.z, 0f));
            }
            // Litter along the alleys and in front of shops: bags, boxes, bottles.
            string[] litter = { "trashbag", "cardboard_box_01", "plastic_bottle_gallon", "plastic_crate_03" };
            int put = 0;
            for (int tries = 0; tries < 3000 && put < 260; tries++)
            {
                var p = new Vector2(Rand(rng, OldTownRect.xMin, 440f), Rand(rng, -395f, 196f));
                if (InSolid(p, 0.3f) || OnCarriage(p, 0.5f) || !InSolid(p, 1.4f)) continue;      // at the foot of a wall
                PlaceTown(props, Prop(litter[rng.Next(litter.Length)]), new Vector3(p.x, Mathf.Max(GroundHeightAt(p.x, p.y), RoadAt(p) != null ? TownRoadY(p.x, p.y) + KerbHeight : -999f), p.y),
                          Rand(rng, 0f, 360f), navBlock: false);
                put++;
            }
            // Goods spilled out in front of the open shops on the streets: from a sequence of their own (the layout's seed
            // and where each shop is), so how many shops are open never moves anything placed after them.
            foreach (var f in _shopFronts)
            {
                var own = new System.Random(LayoutSeed(new Vector3(f.x, 0f, f.z)) ^ 0x600D5);
                if (own.NextDouble() < 0.45) continue;
                var at = new Vector3(f.x, f.y, f.z);
                var n = Quaternion.Euler(0f, f.w, 0f) * Vector3.forward;
                var side = Vector3.Cross(Vector3.up, n);
                string[] goods = { "plastic_crate_01", "plastic_crate_02", "wicker_basket_01", "wooden_crate_02", "cardboard_box_01", "plastic_monobloc_chair_01" };
                for (int i = 0, count = own.Next(1, 4); i < count; i++)
                    PlaceTown(props, Prop(goods[own.Next(goods.Length)]), at + side * Rand(own, -1.5f, 1.5f) - n * Rand(own, 0.2f, 0.8f), f.w + Rand(own, -30f, 30f));
            }
        }

        /// <summary>Old-town squares and other civic spots where civilians gather (FPSKitTownLife).</summary>
        private static readonly List<Vector4> _civicSpots = new List<Vector4>();

        private static bool InJunction(Vector2 p)
        {
            foreach (var j in _townJunctions) if (j.Box.Contains(p)) return true;
            return Vector2.Distance(p, RoundaboutCentre) < RoundaboutOuter + 1f;
        }
    }
}
#endif
