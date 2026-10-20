#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The desert town's plan: where every road, district and landmark is. A fixed plan, like the
    /// Fairground's, because a town is not random -- the souk runs off the clock-tower square, the mosque
    /// closes the end of the avenue, the citadel sits on the only hill, the palms grow where the water is,
    /// and the highway passes the edge of it all with the gas station at the turn-off.
    ///
    /// <code>
    ///          z=+500 .................................................................
    ///            palm farms + oasis (NW)       dunes          citadel on its hill (NE)
    ///          z=+300            old town W     GRAND MOSQUE
    ///                                            mosque plaza          north street z=200
    ///          z=+100      old town + SOUK ===bazaar gate===(clock tower roundabout)=== boulevard
    ///                      palace compound                |avenue|   east residential grid
    ///          z=-130 ===== west road ========================+========================== z=-130
    ///                      SW residential, water tower    |      |   bus stop, shops
    ///          z=-260 ===== south street =====================+========================== z=-260
    ///                      garage                     checkpoint gas station
    ///          z=-400 ===================== highway (runs out to the horizon) =====================
    ///                      junkyard                    dunes
    ///          z=-500 .................................................................
    /// </code>
    ///
    /// Every road is axis-aligned and straight; the roundabout is the one curve. That is what lets the
    /// junction, kerb and sidewalk code stay exact (see FPSKitTownRoads).
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        // ==================================================================
        // Roads
        // ==================================================================
        private enum RoadKind { Highway, Avenue, Boulevard, Street, Track }

        private sealed class TownRoad
        {
            public string name;
            public RoadKind kind;
            /// <summary>Centreline end points; every road is parallel to x or to z.</summary>
            public Vector2 a, b;
            /// <summary>Asphalt width, kerb to kerb (for a divided road, both carriageways and the median).</summary>
            public float carriage;
            /// <summary>Pavement width each side; 0 for the highway and tracks.</summary>
            public float sidewalk;
            /// <summary>Raised median down the middle (planted, lamps on it); 0 for none.</summary>
            public float median;
            public int lanesEachWay = 1;

            public bool AlongX => Mathf.Abs(b.y - a.y) < 0.01f;
            public float Half => carriage * 0.5f;
            public float Corridor => carriage * 0.5f + sidewalk;
            public Vector2 Dir => (b - a).normalized;
            public float Length => Vector2.Distance(a, b);
            /// <summary>The fixed coordinate (z for a road along x, x for one along z).</summary>
            public float Fixed => AlongX ? a.y : a.x;
            public float Min => AlongX ? Mathf.Min(a.x, b.x) : Mathf.Min(a.y, b.y);
            public float Max => AlongX ? Mathf.Max(a.x, b.x) : Mathf.Max(a.y, b.y);
            public bool Paved => kind != RoadKind.Track;
            public float Speed => kind == RoadKind.Highway ? 22f : kind == RoadKind.Avenue ? 13f : kind == RoadKind.Boulevard ? 12f : 9f;
        }

        private static readonly List<TownRoad> _townRoads = new List<TownRoad>();

        /// <summary>The clock-tower roundabout: centre, island radius, outer kerb radius, pavement ring outer radius.</summary>
        private static readonly Vector2 RoundaboutCentre = new Vector2(0f, 20f);
        private const float RoundaboutIsland = 13f, RoundaboutOuter = 25f, RoundaboutWalk = 29.5f;

        /// <summary>Sloped tracks that are not on the axis grid (the citadel ramp), as centreline polylines.</summary>
        private static readonly List<(string name, Vector2[] points, float width)> _townTracks = new List<(string, Vector2[], float)>();

        // ==================================================================
        // Sites
        // ==================================================================
        /// <summary>A named rectangle of the plan (x0, z0, x1, z1) and what it is.</summary>
        private sealed class TownSite
        {
            public string name;
            public Rect rect;          // x = x0, y = z0
            public string kind;
            public Vector2 Centre => rect.center;
        }

        private static readonly List<TownSite> _townSites = new List<TownSite>();

        private static TownSite Site(string name) => _townSites.Find(s => s.name == name);

        // Landmarks and places, by their plan rectangle. Kept here so the ground, the spawn zones, the civilians and
        // the buildings all agree on one set of numbers.
        private static readonly Rect MosqueRect = Rect.MinMaxRect(-58f, 186f, 58f, 300f);
        private static readonly Rect MosquePlaza = Rect.MinMaxRect(-58f, 150f, 58f, 186f);
        private static readonly Vector2 CitadelCentre = new Vector2(300f, 350f);
        private const float CitadelPlateau = 78f, CitadelFoot = 140f, CitadelHeight = 15f;
        private static readonly Rect CitadelRect = Rect.MinMaxRect(245f, 300f, 355f, 395f);
        private static readonly Rect PalaceRect = Rect.MinMaxRect(-470f, -112f, -300f, 8f);
        private static readonly Rect SoukRect = Rect.MinMaxRect(-232f, 12f, -34f, 28f);
        private static readonly Rect OldTownRect = Rect.MinMaxRect(-275f, -32f, -38f, 196f);
        private static readonly Rect OldTownWestRect = Rect.MinMaxRect(-470f, 24f, -285f, 200f);
        private static readonly Rect FarmsRect = Rect.MinMaxRect(-485f, 215f, -150f, 485f);
        private static readonly Vector2 OasisCentre = new Vector2(-318f, 345f);
        private const float OasisRadius = 42f;
        private static readonly Rect GasStationRect = Rect.MinMaxRect(22f, -392f, 92f, -342f);
        private static readonly Rect JunkyardRect = Rect.MinMaxRect(-335f, -486f, -215f, -428f);
        /// <summary>The junkyard's gate (north side), where its track comes in, and the drive south from it.</summary>
        private const float JunkGateX = -275f;
        private static readonly Vector2 WaterTowerAt = new Vector2(-196f, -330f);
        private static readonly Vector2 CheckpointAt = new Vector2(0f, -368f);
        private static readonly Vector2 BusStopAt = new Vector2(11f, -205f);
        private static readonly Rect GarageRect = Rect.MinMaxRect(-300f, -160f, -262f, -142f);

        /// <summary>Where the player begins: on the avenue's east pavement south of the roundabout, facing the clock tower and the minaret.</summary>
        private static readonly Vector3 TownStart = new Vector3(10.5f, 0f, -52f);

        // ==================================================================
        // The plan
        // ==================================================================
        private static void PlanTown()
        {
            _townRoads.Clear();
            _townTracks.Clear();
            _townSites.Clear();

            void Road(string name, RoadKind kind, float ax, float az, float bx, float bz, float carriage, float sidewalk,
                      float median = 0f, int lanes = 1)
                => _townRoads.Add(new TownRoad { name = name, kind = kind, a = new Vector2(ax, az), b = new Vector2(bx, bz),
                                                 carriage = carriage, sidewalk = sidewalk, median = median, lanesEachWay = lanes });

            // The highway runs off both edges of the terrain; the apron carries it on to the horizon.
            Road("Highway", RoadKind.Highway, -700f, -400f, 700f, -400f, 14f, 0f, 0f, 2);

            // The avenue: two carriageways and a planted median, from the highway to the mosque plaza. It is cut by the
            // roundabout, so it is two roads that both end on the ring.
            Road("AvenueSouth", RoadKind.Avenue, 0f, -400f, 0f, RoundaboutCentre.y - RoundaboutOuter + 2f, 17f, 4.5f, 3f, 2);
            Road("AvenueNorth", RoadKind.Avenue, 0f, RoundaboutCentre.y + RoundaboutOuter - 2f, 0f, 150f, 17f, 4.5f, 3f, 2);

            // East of the roundabout, the boulevard. West of it is the bazaar gate and the souk, which is not a road.
            Road("Boulevard", RoadKind.Boulevard, RoundaboutCentre.x + RoundaboutOuter - 2f, 20f, 440f, 20f, 13f, 3.5f, 0f, 2);

            // Cross streets. West road and south street cross the avenue (four-way, signals); the rest are the grids.
            Road("WestRoad", RoadKind.Street, -470f, -130f, 440f, -130f, 10f, 3f);
            Road("SouthStreet", RoadKind.Street, -470f, -260f, 440f, -260f, 9.5f, 2.5f);
            // Starts at the first grid street: west of it is the mosque, which closes this end of town.
            Road("NorthStreet", RoadKind.Street, 130f, 200f, 440f, 200f, 9f, 2.5f);
            Road("East110", RoadKind.Street, 0f, 110f, 440f, 110f, 9f, 2.5f);
            Road("East130N", RoadKind.Street, 130f, -400f, 130f, 200f, 9f, 2.5f);
            Road("East230N", RoadKind.Street, 230f, -400f, 230f, 200f, 9f, 2.5f);
            Road("East330N", RoadKind.Street, 330f, -400f, 330f, 200f, 9f, 2.5f);
            Road("EastRing", RoadKind.Street, 440f, -400f, 440f, 200f, 9f, 2.5f);
            Road("West120N", RoadKind.Street, -120f, -400f, -120f, -130f, 9f, 2.5f);
            Road("West240N", RoadKind.Street, -240f, -400f, -240f, -130f, 9f, 2.5f);
            Road("West360N", RoadKind.Street, -360f, -400f, -360f, -130f, 9f, 2.5f);
            Road("West470N", RoadKind.Street, -470f, -400f, -470f, -130f, 9f, 2.5f);
            // A lane along the old town's south edge, between it and the palace, from the west road up to the souk.
            Road("PalaceLane", RoadKind.Street, -290f, -130f, -290f, 12f, 8f, 2f);

            // Dirt tracks: out to the farms past the mosque, down to the junkyard, and the ramp up to the citadel.
            Road("FarmTrack", RoadKind.Track, -470f, 205f, -58f, 205f, 6f, 0f);
            Road("JunkTrack", RoadKind.Track, JunkGateX, -428f, JunkGateX, -400f, 6f, 0f);
            _townTracks.Add(("CitadelRamp", new[] { new Vector2(200f, 200f + 6.5f), new Vector2(232f, 246f), new Vector2(300f, 268f), new Vector2(300f, 300f) }, 7f));

            // Sites.
            void S(string name, Rect r, string kind) => _townSites.Add(new TownSite { name = name, rect = r, kind = kind });
            S("Mosque", MosqueRect, "landmark");
            S("MosquePlaza", MosquePlaza, "plaza");
            S("Citadel", CitadelRect, "landmark");
            S("Palace", PalaceRect, "landmark");
            S("Souk", SoukRect, "souk");
            S("OldTown", OldTownRect, "oldtown");
            S("OldTownWest", OldTownWestRect, "oldtown");
            S("Farms", FarmsRect, "farm");
            S("GasStation", GasStationRect, "edge");
            S("Junkyard", JunkyardRect, "edge");
            S("Garage", GarageRect, "edge");
            S("ClockSquare", new Rect(RoundaboutCentre - Vector2.one * RoundaboutWalk, Vector2.one * RoundaboutWalk * 2f), "plaza");
        }

        // ==================================================================
        // Asking the plan
        // ==================================================================
        /// <summary>The road whose corridor (asphalt + pavements) contains the point, or null. Margin widens it.</summary>
        private static TownRoad RoadAt(Vector2 p, float margin = 0f)
        {
            foreach (var r in _townRoads)
            {
                float along = r.AlongX ? p.x : p.y, across = r.AlongX ? p.y : p.x;
                if (along < r.Min - margin || along > r.Max + margin) continue;
                if (Mathf.Abs(across - r.Fixed) <= r.Corridor + margin) return r;
            }
            return null;
        }

        /// <summary>Whether a point is on any asphalt or track (not pavement).</summary>
        private static bool OnCarriage(Vector2 p, float margin = 0f)
        {
            foreach (var r in _townRoads)
            {
                float along = r.AlongX ? p.x : p.y, across = r.AlongX ? p.y : p.x;
                if (along < r.Min - margin || along > r.Max + margin) continue;
                if (Mathf.Abs(across - r.Fixed) <= r.Half + margin) return true;
            }
            float d = Vector2.Distance(p, RoundaboutCentre);
            if (d >= RoundaboutIsland - margin && d <= RoundaboutOuter + margin) return true;
            foreach (var t in _townTracks)
                for (int i = 0; i + 1 < t.points.Length; i++)
                    if (DistanceToSegment(p, t.points[i], t.points[i + 1]) <= t.width * 0.5f + margin) return true;
            return false;
        }

        /// <summary>Whether a rectangle is clear of every road corridor, the roundabout and the tracks.</summary>
        private static bool ClearOfRoads(Rect r, float margin = 0.5f)
        {
            foreach (var road in _townRoads)
            {
                float c = road.Corridor + margin;
                Rect band = road.AlongX
                    ? Rect.MinMaxRect(road.Min - margin, road.Fixed - c, road.Max + margin, road.Fixed + c)
                    : Rect.MinMaxRect(road.Fixed - c, road.Min - margin, road.Fixed + c, road.Max + margin);
                if (band.Overlaps(r)) return false;
            }
            Vector2 nearest = new Vector2(Mathf.Clamp(RoundaboutCentre.x, r.xMin, r.xMax), Mathf.Clamp(RoundaboutCentre.y, r.yMin, r.yMax));
            if (Vector2.Distance(nearest, RoundaboutCentre) < RoundaboutWalk + margin) return false;
            foreach (var t in _townTracks)
                for (int i = 0; i + 1 < t.points.Length; i++)
                    if (RectSegmentDistance(r, t.points[i], t.points[i + 1]) < t.width * 0.5f + 1.5f + margin) return false;
            return true;
        }

        // DistanceToSegment is shared with the old desert (FPSKitDesertFull).

        private static float RectSegmentDistance(Rect r, Vector2 a, Vector2 b)
        {
            if (r.Contains(a) || r.Contains(b)) return 0f;
            float best = float.MaxValue;
            for (int i = 0; i <= 16; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / 16f);
                Vector2 n = new Vector2(Mathf.Clamp(p.x, r.xMin, r.xMax), Mathf.Clamp(p.y, r.yMin, r.yMax));
                best = Mathf.Min(best, Vector2.Distance(p, n));
            }
            return best;
        }

        /// <summary>
        /// Where two axis-aligned roads meet: a junction at every crossing or tee, with whether it gets signals
        /// (the avenue's crossings) or stop signs (the rest). The roundabout is not in this list.
        /// </summary>
        private sealed class TownJunction
        {
            public Vector2 at;
            public TownRoad x, z;      // the road along x and the road along z
            public bool signals;
            public bool Tee;           // one of the two ends here
            public Rect Box => Rect.MinMaxRect(at.x - z.Half, at.y - x.Half, at.x + z.Half, at.y + x.Half);
        }

        private static readonly List<TownJunction> _townJunctions = new List<TownJunction>();

        private static void FindJunctions()
        {
            _townJunctions.Clear();
            foreach (var rx in _townRoads)
            {
                if (!rx.AlongX) continue;
                foreach (var rz in _townRoads)
                {
                    if (rz.AlongX) continue;
                    float x = rz.Fixed, z = rx.Fixed;
                    if (x < rx.Min - 0.5f || x > rx.Max + 0.5f || z < rz.Min - 0.5f || z > rz.Max + 0.5f) continue;
                    bool tee = Mathf.Abs(x - rx.Min) < 1f || Mathf.Abs(x - rx.Max) < 1f || Mathf.Abs(z - rz.Min) < 1f || Mathf.Abs(z - rz.Max) < 1f;
                    bool avenue = rz.kind == RoadKind.Avenue || rx.kind == RoadKind.Avenue;
                    _townJunctions.Add(new TownJunction { at = new Vector2(x, z), x = rx, z = rz, Tee = tee,
                                                          signals = avenue && rx.kind != RoadKind.Highway && rx.kind != RoadKind.Track });
                }
            }
        }

        /// <summary>The junctions along a road, ordered by distance along it.</summary>
        private static List<TownJunction> JunctionsOn(TownRoad r)
        {
            var list = _townJunctions.FindAll(j => j.x == r || j.z == r);
            list.Sort((p, q) => (r.AlongX ? p.at.x : p.at.y).CompareTo(r.AlongX ? q.at.x : q.at.y));
            return list;
        }

        /// <summary>Whether a point is inside a district rectangle or site that the plan reserves for something other than plots.</summary>
        private static bool InReserved(Rect r)
        {
            foreach (var s in _townSites)
                if (s.kind != "oldtown" && s.rect.Overlaps(r)) return true;
            if (Vector2.Distance(r.center, CitadelCentre) < CitadelFoot + Mathf.Max(r.width, r.height) * 0.5f) return true;
            if (Vector2.Distance(r.center, WaterTowerAt) < 14f + Mathf.Max(r.width, r.height) * 0.5f) return true;
            return false;
        }
    }
}
#endif
