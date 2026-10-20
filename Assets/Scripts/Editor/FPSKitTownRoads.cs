#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The town's roads, built from the plan (FPSKitTownPlan).
    ///
    ///   * Asphalt is draped on the uncarved ground every few metres; overlapping pieces (a road running into a
    ///     junction, two roads crossing) are the same material with world-metre texture coordinates, so they shade
    ///     identically and the overlap is invisible -- no junction needs its own geometry.
    ///   * Pavements stand 15 cm above the asphalt on a concrete kerb and are cut only where another road's
    ///     carriageway actually passes (a tee interrupts one side, a crossing both). Their outer edge drops a skirt
    ///     past the carved terrain, so the 18 cm sink under every road is never seen.
    ///   * Markings follow how a road is used: lane dashes, a solid centre on the boulevard and highway, stop lines
    ///     and zebra crossings at signalled junctions and the roundabout's mouths, nothing inside a junction box.
    ///   * The roundabout is a ring of asphalt round a raised island, with its own kerb and pavement ring cut at
    ///     each road's mouth.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private const float KerbHeight = 0.15f;
        private const float MarkLift = 0.02f;

        private static Material _asphalt, _asphaltWorn, _pavers, _paversPlaza, _kerb, _paint, _paintYellow, _medianSoil;

        private static void ResolveTownRoadMaterials()
        {
            _asphalt = TownMat("Asphalt", "asphalt_02", new Color(0.86f, 0.84f, 0.82f), 7f, 0.22f);
            // The worn asphalt scan is strewn with leaves; a desert street is the same asphalt, paler and dustier.
            _asphaltWorn = TownMat("AsphaltDusty", "asphalt_02", new Color(1.02f, 0.98f, 0.92f), 6.5f, 0.2f);
            _pavers = TownMat("PaversGrey", "square_concrete_pavers", new Color(0.86f, 0.85f, 0.83f), 2.2f, 0.28f);
            _paversPlaza = TownMat("PlazaStone", "sandstone_blocks_08", new Color(1.02f, 0.96f, 0.88f), 3.5f, 0.3f);
            _kerb = TownMat("Kerb", "concrete_wall_008", new Color(0.92f, 0.90f, 0.86f), 1.6f, 0.28f);
            _paint = TownFlat("RoadPaint", new Color(0.88f, 0.87f, 0.82f), 0.25f);
            _paintYellow = TownFlat("RoadPaintYellow", new Color(0.86f, 0.68f, 0.18f), 0.25f);
            _medianSoil = TownMat("MedianGravel", "rock_ground", new Color(0.94f, 0.90f, 0.84f), 3f, 0.25f);
        }

        private static void BuildTownRoads(Transform root, int layer)
        {
            ResolveTownRoadMaterials();
            FindJunctions();
            var group = new GameObject("Roads").transform;
            group.SetParent(root, false);

            foreach (var road in _townRoads)
            {
                if (!road.Paved) continue;
                var asphalt = new TownMesh();
                var walks = new TownMesh();
                var marks = new TownMesh();
                BuildCarriage(road, asphalt);
                BuildPavements(road, walks);
                BuildMarkings(road, marks);
                if (road.median > 0f) BuildMedian(road, walks);

                var a = asphalt.Build(group, road.name, layer, "Concrete", shadows: ShadowCastingMode.Off);
                if (a != null) Hide(a);
                var w = walks.Build(group, road.name + "_Pavement", layer, "Concrete");
                if (w != null) Hide(w);
                var m = marks.Build(group, road.name + "_Markings", layer, null, collider: false, shadows: ShadowCastingMode.Off);
                if (m != null) Hide(m);
            }

            BuildRoundabout(group, layer);
        }

        // ------------------------------------------------------------------
        /// <summary>World point at distance <paramref name="along"/> on the road's axis and <paramref name="across"/> to its left.</summary>
        private static Vector3 RoadPoint(TownRoad r, float along, float across, float lift = 0f)
        {
            float x = r.AlongX ? along : r.Fixed + across;
            float z = r.AlongX ? r.Fixed - across : along;
            return new Vector3(x, TownRoadY(x, z) + lift, z);
        }

        /// <summary>Splits [from, to] into pieces about <paramref name="step"/> long, so a surface follows the ground.</summary>
        private static IEnumerable<(float a, float b)> Pieces(float from, float to, float step)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt((to - from) / step));
            for (int i = 0; i < n; i++) yield return (from + (to - from) * i / n, from + (to - from) * (i + 1) / n);
        }

        /// <summary>A draped strip of the road between two across-offsets, lifted above the road surface.</summary>
        private static void Strip(TownMesh m, Material mat, TownRoad r, float from, float to, float acrossA, float acrossB, float lift, float step = 6f)
        {
            foreach (var (a, b) in Pieces(from, to, step))
                m.Quad(mat, RoadPoint(r, a, acrossA, lift), RoadPoint(r, b, acrossA, lift),
                       RoadPoint(r, b, acrossB, lift), RoadPoint(r, a, acrossB, lift), Vector3.up);
        }

        /// <summary>A vertical face along the road at one across-offset, from <paramref name="y0"/> to <paramref name="y1"/> above the road, facing <paramref name="facing"/> across.</summary>
        private static void Face(TownMesh m, Material mat, TownRoad r, float from, float to, float across, float y0, float y1, float facing, float step = 6f)
        {
            Vector3 normal = r.AlongX ? new Vector3(0f, 0f, -facing) : new Vector3(facing, 0f, 0f);
            foreach (var (a, b) in Pieces(from, to, step))
                m.Quad(mat, RoadPoint(r, a, across, y0), RoadPoint(r, b, across, y0),
                       RoadPoint(r, b, across, y1), RoadPoint(r, a, across, y1), normal);
        }

        private static void BuildCarriage(TownRoad r, TownMesh m)
        {
            var mat = r.kind == RoadKind.Street ? _asphaltWorn : _asphalt;
            if (r.median > 0f)
            {
                // Two carriageways either side of the median.
                Strip(m, mat, r, r.Min, r.Max, r.median * 0.5f, r.Half, 0f);
                Strip(m, mat, r, r.Min, r.Max, -r.Half, -r.median * 0.5f, 0f);
                // Under the median at junctions, so cross traffic drives on asphalt.
                foreach (var j in JunctionsOn(r))
                {
                    var other = j.x == r ? j.z : j.x;
                    float at = r.AlongX ? j.at.x : j.at.y;
                    Strip(m, mat, r, at - other.Half - 1f, at + other.Half + 1f, -r.median * 0.5f, r.median * 0.5f, 0f);
                }
            }
            else Strip(m, mat, r, r.Min, r.Max, -r.Half, r.Half, 0f);

            // A road with no kerb (the highway) shows its edge: a skirt down past the carved ground.
            if (r.sidewalk <= 0f)
            {
                Face(m, _kerb, r, r.Min, r.Max, r.Half, -0.35f, 0f, 1f);
                Face(m, _kerb, r, r.Min, r.Max, -r.Half, -0.35f, 0f, -1f);
            }
        }

        /// <summary>
        /// Intervals along a road where its pavement on one side (+1 left, -1 right) is cut by a crossing carriageway.
        /// Only a road that really continues to that side cuts it, so a tee leaves the far pavement whole.
        /// </summary>
        private static List<Vector2> PavementGaps(TownRoad r, float side)
        {
            var gaps = new List<Vector2>();
            foreach (var j in JunctionsOn(r))
            {
                var other = j.x == r ? j.z : j.x;
                float at = r.AlongX ? j.at.x : j.at.y;
                // Which way the other road runs from this junction, in this road's "across" terms.
                float otherAt = other.AlongX ? j.at.x : j.at.y;
                // For a road along x, "left" (+across) is -z; for one along z, "left" is +x.
                float toLeftMin = r.AlongX ? -(other.Max - otherAt) : (other.Min - otherAt);
                float toLeftMax = r.AlongX ? -(other.Min - otherAt) : (other.Max - otherAt);
                bool reachesLeft = Mathf.Max(toLeftMin, toLeftMax) > r.Half;
                bool reachesRight = Mathf.Min(toLeftMin, toLeftMax) < -r.Half;
                if ((side > 0f && reachesLeft) || (side < 0f && reachesRight))
                    gaps.Add(new Vector2(at - other.Half, at + other.Half));
            }
            // The roundabout's pavement ring takes over where the avenue and boulevard meet it.
            if (Vector2.Distance(r.a, RoundaboutCentre) < RoundaboutWalk + 2f || Vector2.Distance(r.b, RoundaboutCentre) < RoundaboutWalk + 2f)
            {
                float c = r.AlongX ? RoundaboutCentre.x : RoundaboutCentre.y;
                gaps.Add(new Vector2(c - RoundaboutWalk + 0.5f, c + RoundaboutWalk - 0.5f));
            }
            return gaps;
        }

        private static IEnumerable<(float a, float b)> Minus(float from, float to, List<Vector2> gaps)
        {
            gaps.Sort((p, q) => p.x.CompareTo(q.x));
            float cur = from;
            foreach (var g in gaps)
            {
                if (g.y <= cur) continue;
                if (g.x >= to) break;
                if (g.x > cur + 0.05f) yield return (cur, g.x);
                cur = Mathf.Max(cur, g.y);
            }
            if (to > cur + 0.05f) yield return (cur, to);
        }

        private static void BuildPavements(TownRoad r, TownMesh m)
        {
            if (r.sidewalk <= 0f) return;
            foreach (float side in new[] { 1f, -1f })
            {
                float inner = r.Half * side, outer = r.Corridor * side;
                foreach (var (a, b) in Minus(r.Min, r.Max, PavementGaps(r, side)))
                {
                    Strip(m, _pavers, r, a, b, inner, outer, KerbHeight);
                    // Kerb face towards the road, a thin kerb top in concrete, and the skirt on the outside.
                    Face(m, _kerb, r, a, b, inner, -0.02f, KerbHeight, -side);
                    Strip(m, _kerb, r, a, b, inner, inner + 0.18f * side, KerbHeight + 0.003f);
                    Face(m, _kerb, r, a, b, outer, -0.35f, KerbHeight, side);
                    // End caps where the pavement stops at a crossing.
                    foreach (float e in new[] { a, b })
                    {
                        if (Mathf.Abs(e - r.Min) < 0.1f || Mathf.Abs(e - r.Max) < 0.1f) continue;
                        float dir = Mathf.Abs(e - a) < 0.01f ? -1f : 1f;
                        Vector3 n = r.AlongX ? new Vector3(dir, 0, 0) : new Vector3(0, 0, dir);
                        m.Quad(_kerb, RoadPoint(r, e, inner, -0.02f), RoadPoint(r, e, outer, -0.02f),
                               RoadPoint(r, e, outer, KerbHeight), RoadPoint(r, e, inner, KerbHeight), n);
                    }
                }
            }
        }

        /// <summary>The avenue's median: a raised kerbed bed of earth for the palms and lamps, open at every junction.</summary>
        private static void BuildMedian(TownRoad r, TownMesh m)
        {
            float h = r.median * 0.5f;
            var gaps = new List<Vector2>();
            foreach (var j in JunctionsOn(r))
            {
                var other = j.x == r ? j.z : j.x;
                float at = r.AlongX ? j.at.x : j.at.y;
                gaps.Add(new Vector2(at - other.Half - 4f, at + other.Half + 4f));
            }
            // Gaps for the zebra crossings at the souk and mosque ends, and the checkpoint's booth lane.
            foreach (var (a, b) in Minus(r.Min + 6f, r.Max - 6f, gaps))
            {
                Strip(m, _medianSoil, r, a, b, -h + 0.2f, h - 0.2f, KerbHeight + 0.05f);
                Strip(m, _kerb, r, a, b, -h, -h + 0.2f, KerbHeight + 0.08f);
                Strip(m, _kerb, r, a, b, h - 0.2f, h, KerbHeight + 0.08f);
                Face(m, _kerb, r, a, b, -h, -0.02f, KerbHeight + 0.08f, -1f);
                Face(m, _kerb, r, a, b, h, -0.02f, KerbHeight + 0.08f, 1f);
                foreach (float e in new[] { a, b })
                {
                    float dir = e == a ? -1f : 1f;
                    Vector3 n = r.AlongX ? new Vector3(dir, 0, 0) : new Vector3(0, 0, dir);
                    m.Quad(_kerb, RoadPoint(r, e, -h, -0.02f), RoadPoint(r, e, h, -0.02f),
                           RoadPoint(r, e, h, KerbHeight + 0.08f), RoadPoint(r, e, -h, KerbHeight + 0.08f), n);
                }
            }
        }

        // ------------------------------------------------------------------
        private static void BuildMarkings(TownRoad r, TownMesh m)
        {
            // Where markings stop: every junction box plus three metres, and the roundabout.
            var stops = new List<Vector2>();
            foreach (var j in JunctionsOn(r))
            {
                var other = j.x == r ? j.z : j.x;
                float at = r.AlongX ? j.at.x : j.at.y;
                float clear = other.Corridor + (j.signals ? 9.5f : 3f);
                stops.Add(new Vector2(at - clear, at + clear));
            }
            if (Vector2.Distance(r.a, RoundaboutCentre) < RoundaboutOuter + 3f || Vector2.Distance(r.b, RoundaboutCentre) < RoundaboutOuter + 3f)
            {
                float c = r.AlongX ? RoundaboutCentre.x : RoundaboutCentre.y;
                stops.Add(new Vector2(c - RoundaboutOuter - 4f, c + RoundaboutOuter + 4f));
            }
            var runs = new List<(float a, float b)>(Minus(r.Min + 2f, r.Max - 2f, new List<Vector2>(stops)));

            void Dashes(float across, float dash, float gap, float width, Material mat)
            {
                foreach (var (a, b) in runs)
                    for (float s = a; s + dash <= b; s += dash + gap)
                        Strip(m, mat, r, s, s + dash, across - width * 0.5f, across + width * 0.5f, MarkLift, dash + 1f);
            }
            void Solid(float across, float width, Material mat)
            {
                foreach (var (a, b) in runs) Strip(m, mat, r, a, b, across - width * 0.5f, across + width * 0.5f, MarkLift);
            }

            switch (r.kind)
            {
                case RoadKind.Highway:
                    Solid(0.18f, 0.12f, _paintYellow); Solid(-0.18f, 0.12f, _paintYellow);
                    Dashes(3.5f, 4f, 8f, 0.15f, _paint); Dashes(-3.5f, 4f, 8f, 0.15f, _paint);
                    Solid(r.Half - 0.35f, 0.18f, _paint); Solid(-r.Half + 0.35f, 0.18f, _paint);
                    break;
                case RoadKind.Avenue:
                    float lane = (r.median * 0.5f + r.Half) * 0.5f;
                    Dashes(lane, 3f, 6f, 0.14f, _paint); Dashes(-lane, 3f, 6f, 0.14f, _paint);
                    break;
                case RoadKind.Boulevard:
                    Solid(0.15f, 0.12f, _paint); Solid(-0.15f, 0.12f, _paint);
                    Dashes(r.Half * 0.5f, 3f, 6f, 0.14f, _paint); Dashes(-r.Half * 0.5f, 3f, 6f, 0.14f, _paint);
                    break;
                default:
                    Dashes(0f, 3f, 5f, 0.12f, _paint);
                    break;
            }

            // Stop lines and zebra crossings on the approaches to signalled junctions; stop lines alone at the rest.
            foreach (var j in JunctionsOn(r))
            {
                var other = j.x == r ? j.z : j.x;
                if (other.kind == RoadKind.Track) continue;
                float at = r.AlongX ? j.at.x : j.at.y;
                foreach (float dir in new[] { -1f, 1f })
                {
                    float edge = at + dir * (other.Corridor + 0.5f);
                    if (edge < r.Min + 1f || edge > r.Max - 1f) continue;
                    if (j.signals && r.sidewalk > 0f) Zebra(m, r, edge + dir * 1.8f, dir);
                    // Stop line across the incoming lanes (on the right of a car driving towards the junction).
                    float lineAt = edge + dir * (j.signals ? 4.4f : 0.8f);
                    // A car on this side drives towards the junction (along -dir); right-hand traffic, so its lane is on
                    // its right, which for both orientations of road is +across when driving +along.
                    float side = -dir;
                    float inner = r.median > 0f ? r.median * 0.5f : 0.2f;
                    Strip(m, _paint, r, lineAt - 0.25f, lineAt + 0.25f, side * inner, side * (r.Half - 0.3f), MarkLift, 2f);
                }
            }
            // At the roundabout's mouth: a zebra a car-length back from the ring.
            float dA = Vector2.Distance(r.a, RoundaboutCentre), dB = Vector2.Distance(r.b, RoundaboutCentre);
            if (Mathf.Min(dA, dB) < RoundaboutOuter + 3f)
            {
                float centre = r.AlongX ? RoundaboutCentre.x : RoundaboutCentre.y;
                Vector2 far = dA < dB ? r.b : r.a;
                float dir = Mathf.Sign((r.AlongX ? far.x : far.y) - centre);
                Zebra(m, r, centre + dir * (RoundaboutWalk + 2.5f), dir);
            }
        }

        /// <summary>A zebra crossing across the whole carriageway at <paramref name="at"/> along the road.</summary>
        private static void Zebra(TownMesh m, TownRoad r, float at, float dir)
        {
            const float stripe = 0.5f, depth = 3.2f;
            for (float s = -r.Half + 0.4f; s + stripe <= r.Half - 0.3f; s += stripe * 2f)
            {
                if (r.median > 0f && Mathf.Abs(s + stripe * 0.5f) < r.median * 0.5f) continue;
                m.Quad(_paint, RoadPoint(r, at, s, MarkLift), RoadPoint(r, at + depth * dir, s, MarkLift),
                       RoadPoint(r, at + depth * dir, s + stripe, MarkLift), RoadPoint(r, at, s + stripe, MarkLift), Vector3.up);
            }
        }

        // ------------------------------------------------------------------
        private static void BuildRoundabout(Transform parent, int layer)
        {
            var asphalt = new TownMesh();
            var walk = new TownMesh();
            var marks = new TownMesh();
            const int seg = 72;
            Vector3 P(float radius, float a, float lift)
            {
                float x = RoundaboutCentre.x + Mathf.Cos(a) * radius, z = RoundaboutCentre.y + Mathf.Sin(a) * radius;
                return new Vector3(x, TownRoadY(x, z) + lift, z);
            }
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                asphalt.Quad(_asphalt, P(RoundaboutIsland, a0, 0f), P(RoundaboutOuter, a0, 0f), P(RoundaboutOuter, a1, 0f), P(RoundaboutIsland, a1, 0f), Vector3.up);

                // The island: kerb facing out, a stone surface (the clock tower stands on it).
                walk.Quad(_paversPlaza, P(0.01f, a0, KerbHeight + 0.1f), P(RoundaboutIsland, a0, KerbHeight + 0.1f),
                          P(RoundaboutIsland, a1, KerbHeight + 0.1f), P(0.01f, a1, KerbHeight + 0.1f), Vector3.up);
                Vector3 outward = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0f, Mathf.Sin((a0 + a1) * 0.5f));
                walk.Quad(_kerb, P(RoundaboutIsland, a0, -0.02f), P(RoundaboutIsland, a1, -0.02f),
                          P(RoundaboutIsland, a1, KerbHeight + 0.1f), P(RoundaboutIsland, a0, KerbHeight + 0.1f), outward);

                // The pavement ring outside, cut where a road comes in.
                Vector3 mid = P((RoundaboutOuter + RoundaboutWalk) * 0.5f, (a0 + a1) * 0.5f, 0f);
                if (OnRoadMouth(new Vector2(mid.x, mid.z))) continue;
                walk.Quad(_pavers, P(RoundaboutOuter, a0, KerbHeight), P(RoundaboutWalk, a0, KerbHeight),
                          P(RoundaboutWalk, a1, KerbHeight), P(RoundaboutOuter, a1, KerbHeight), Vector3.up);
                walk.Quad(_kerb, P(RoundaboutOuter, a0, -0.02f), P(RoundaboutOuter, a1, -0.02f),
                          P(RoundaboutOuter, a1, KerbHeight), P(RoundaboutOuter, a0, KerbHeight), -outward);
                walk.Quad(_kerb, P(RoundaboutWalk, a0, -0.35f), P(RoundaboutWalk, a1, -0.35f),
                          P(RoundaboutWalk, a1, KerbHeight), P(RoundaboutWalk, a0, KerbHeight), outward);

                // Lane divider on the ring.
                if (i % 2 == 0)
                    marks.Quad(_paint, P(19.0f, a0, MarkLift), P(19.15f, a0, MarkLift), P(19.15f, a1, MarkLift), P(19.0f, a1, MarkLift), Vector3.up);
            }
            var go = asphalt.Build(parent, "Roundabout", layer, "Concrete", shadows: ShadowCastingMode.Off); if (go != null) Hide(go);
            go = walk.Build(parent, "Roundabout_Pavement", layer, "Concrete"); if (go != null) Hide(go);
            go = marks.Build(parent, "Roundabout_Markings", layer, null, collider: false, shadows: ShadowCastingMode.Off); if (go != null) Hide(go);
        }

        /// <summary>Whether a point by the roundabout is where a road's carriageway comes in (the pavement ring opens there).</summary>
        private static bool OnRoadMouth(Vector2 p)
        {
            foreach (var r in _townRoads)
            {
                if (Vector2.Distance(r.a, RoundaboutCentre) > RoundaboutOuter + 3f && Vector2.Distance(r.b, RoundaboutCentre) > RoundaboutOuter + 3f) continue;
                float across = Mathf.Abs((r.AlongX ? p.y : p.x) - r.Fixed);
                if (across <= r.Half + 0.3f) return true;
            }
            return false;
        }
    }
}
#endif
