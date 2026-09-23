#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// What the snow field was missing: the station it was named after, pine forest, ice --
    /// caves, crevasses, a frozen waterfall -- and drifting snow under a low sun.
    ///
    /// Ishaan's pick, all four, keeping the igloo camps and the lake. The field was the
    /// plainest arena in the game: white ground, white domes and white rock under a clear
    /// blue sky, with almost no contrast anywhere, so nothing on it read and no distance
    /// could be judged. Every addition here is chosen partly for what it is and partly for
    /// being <i>not white</i>: dark pines, red and orange station modules, blue ice.
    ///
    /// Same recipe as the desert's (FPSKitDesertLife): sites planned before the ground so
    /// their pads are in it, built after, emptiness asked of the physics scene. Two things
    /// are particular to snow:
    ///
    /// <b>The crevasses are real holes in the ground.</b> A crack drawn on the snow that you
    /// can walk across is a lie the first time someone does. So the terrain mesher leaves the
    /// cells out (<see cref="InCrevasse"/>), ice walls go down into the gap, a trigger at the
    /// bottom kills, and rope bridges cross. They are short enough to walk round, so they are
    /// obstacles to plan a route by, never walls that cut the map.
    ///
    /// <b>The plant's materials are borrowed on purpose.</b> The station reuses the
    /// catwalk and stair builders, which read the industrial zone's steel, deck and safety
    /// paint; those are only resolved for the plant, so they are set here too. Left null the
    /// walkways come out magenta.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private struct Crevasse
        {
            public Vector2 A, B;
            public float Half;
        }

        private static readonly List<Crevasse> _crevasses = new List<Crevasse>();
        private static readonly List<Vector3> _snowSites = new List<Vector3>();   // x, z, radius: keep-outs for the forest
        private static Vector2 _stationCentre, _icefallSite;
        private static readonly List<Vector3> _caveSites = new List<Vector3>();   // x, z, yaw
        private static bool _hasStation, _hasIcefall;

        private static Material _pineMat, _pineSnowMat, _barkMat, _moduleRed, _moduleOrange, _moduleYellow,
                                _moduleGrey, _glacierMat, _padMat, _ropeMat;

        private static void ResetSnowLife()
        {
            _crevasses.Clear();
            _snowSites.Clear();
            _caveSites.Clear();
            _hasStation = _hasIcefall = false;
        }

        /// <summary>
        /// Whether a point is inside a crevasse, widened by <paramref name="margin"/>. The terrain
        /// mesher asks with no margin to cut the hole; everything placed asks with one.
        /// </summary>
        private static bool InCrevasse(float x, float z, float margin)
        {
            foreach (var c in _crevasses)
            {
                var p = new Vector2(x, z);
                var ab = c.B - c.A;
                float t = Mathf.Clamp01(Vector2.Dot(p - c.A, ab) / ab.sqrMagnitude);
                float d = (c.A + ab * t - p).magnitude;

                // Pinched shut at both ends, so it closes the way a real crack does.
                float taper = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                if (d < c.Half * Mathf.Max(0.35f, taper) + 1.5f + margin) return true;
            }

            return false;
        }

        private static void ResolveSnowLifeMaterials()
        {
            _pineMat = MakeMaterial("Pine", new Color(0.12f, 0.20f, 0.14f), 0.06f, 0f);
            _pineSnowMat = MakeMaterial("PineSnow", new Color(0.90f, 0.93f, 0.97f), 0.1f, 0f);
            _barkMat = MakeMaterial("Bark", new Color(0.24f, 0.17f, 0.12f), 0.05f, 0f);

            // Polar stations are painted loud so they can be found in a whiteout, which is
            // exactly the contrast this field was short of.
            _moduleRed = MakeMaterial("ModuleRed", new Color(0.66f, 0.13f, 0.10f), 0.25f, 0.1f);
            _moduleOrange = MakeMaterial("ModuleOrange", new Color(0.86f, 0.42f, 0.10f), 0.25f, 0.1f);
            _moduleYellow = MakeMaterial("ModuleYellow", new Color(0.85f, 0.68f, 0.16f), 0.25f, 0.1f);
            _moduleGrey = MakeMaterial("ModuleGrey", new Color(0.42f, 0.45f, 0.49f), 0.25f, 0.2f);
            _padMat = MakeMaterial("Helipad", new Color(0.30f, 0.31f, 0.33f), 0.1f, 0f);
            _ropeMat = MakeMaterial("Rope", new Color(0.46f, 0.36f, 0.22f), 0.05f, 0f);

            // Glacier ice: deep blue, darker than the lake's sheet ice because it is thick.
            _glacierMat = MakeDetailMaterial("Glacier", new Color(0.36f, 0.58f, 0.76f), "Ice", Metres(3f), 0.6f, 0f, 1.4f);

            // Borrowed by the catwalks and stairs: see the class summary.
            _zoneSteel = _moduleGrey;
            _zoneDeck = MakeMaterial("StationDeck", new Color(0.30f, 0.31f, 0.33f), 0.2f, 0.3f);
            _zoneSafety = MakeMaterial("StationRail", new Color(0.80f, 0.60f, 0.10f), 0.28f, 0.1f);
            _denseGlass = MakeMaterial("StationGlass", new Color(0.07f, 0.09f, 0.11f), 0.6f, 0.2f);
        }

        // ==================================================================
        // Planning
        // ==================================================================
        private static void PlanSnowLife(float half)
        {
            ResetSnowLife();
            if (!_theme.snowZone) return;

            var rng = new System.Random(_theme.randomSeed * 4409 + 3);

            _snowSites.Add(new Vector3(0f, 0f, 30f));
            _snowSites.Add(new Vector3(_lakeAt.x, _lakeAt.y, _lakeRadius * 1.5f));
            foreach (var camp in _camps) _snowSites.Add(new Vector3(camp.Centre.x, camp.Centre.y, camp.Radius + 10f));

            bool Inside(Vector2 p, float r) => Mathf.Abs(p.x) + r < half - 26f && Mathf.Abs(p.y) + r < half - 26f;

            // ---- the station ----
            const float stationR = 44f;
            for (int i = 0; i < 80 && !_hasStation; i++)
            {
                var p = new Vector2(Rand(rng, -half, half), Rand(rng, -half, half));
                if (p.magnitude < 90f || !Inside(p, stationR) || !Free(p, stationR + 6f)) continue;

                _stationCentre = p;
                _hasStation = true;
                Claim(p.x, p.y, stationR + 8f);
                FlattenPad(p.x, p.y, stationR, 26f);
                _anchors.Add(new Vector3(p.x, 0f, p.y));
                _snowSites.Add(new Vector3(p.x, p.y, stationR + 12f));
            }

            // ---- the frozen waterfall, near an edge ----
            for (int i = 0; i < 60 && !_hasIcefall; i++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f);
                var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Rand(rng, half * 0.6f, half * 0.75f);
                if (!Inside(p, 22f) || !Free(p, 26f)) continue;

                _icefallSite = p;
                _hasIcefall = true;
                Claim(p.x, p.y, 26f);
                _snowSites.Add(new Vector3(p.x, p.y, 30f));
            }

            // ---- two crevasses, short, well away from everything ----
            for (int i = 0; i < 80 && _crevasses.Count < 2; i++)
            {
                var mid = new Vector2(Rand(rng, -half * 0.7f, half * 0.7f), Rand(rng, -half * 0.7f, half * 0.7f));
                float len = Rand(rng, 45f, 65f), yaw = Rand(rng, 0f, Mathf.PI);
                var dir = new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw));
                var a = mid - dir * len * 0.5f;
                var b = mid + dir * len * 0.5f;

                if (mid.magnitude < 70f || !Inside(mid, len * 0.5f + 10f)) continue;

                bool clear = true;
                for (float t = 0f; t <= 1f; t += 0.1f)
                    if (!Free(Vector2.Lerp(a, b, t), 14f)) { clear = false; break; }
                if (!clear) continue;

                _crevasses.Add(new Crevasse { A = a, B = b, Half = Rand(rng, 2.4f, 3.2f) });
                for (float t = 0f; t <= 1f; t += 0.1f) { var q = Vector2.Lerp(a, b, t); Claim(q.x, q.y, 10f); }
                _snowSites.Add(new Vector3(mid.x, mid.y, len * 0.5f + 8f));
            }

            // ---- ice caves: sites only, they stand on whatever the ground does ----
            for (int i = 0; i < 60 && _caveSites.Count < 3; i++)
            {
                var p = new Vector2(Rand(rng, -half * 0.8f, half * 0.8f), Rand(rng, -half * 0.8f, half * 0.8f));
                if (p.magnitude < 60f || !Inside(p, 18f) || !Free(p, 18f)) continue;

                _caveSites.Add(new Vector3(p.x, p.y, Rand(rng, 0f, 180f)));
                Claim(p.x, p.y, 18f);
                FlattenPad(p.x, p.y, 13f, 14f);
                _snowSites.Add(new Vector3(p.x, p.y, 20f));
            }
        }

        // ==================================================================
        // Building
        // ==================================================================
        private static void BuildSnowLife(Transform root, int layer, int backdrop, float half)
        {
            if (!_theme.snowZone) return;
            ResolveSnowLifeMaterials();

            var rng = new System.Random(_theme.randomSeed * 2957 + 1);
            var group = new GameObject("SnowLife").transform;
            group.SetParent(root, false);

            if (_hasStation) BuildStation(group, layer, backdrop, rng);
            foreach (var c in _crevasses) BuildCrevasse(group, layer, rng, c);
            foreach (var s in _caveSites) BuildIceCave(group, layer, rng, s);
            if (_hasIcefall) BuildIcefall(group, layer, rng);
            BuildPineForest(group, layer, rng, half);
            BuildSnowDrift(group);

            Debug.Log($"[FPSKit] snow: station {_hasStation}, {_crevasses.Count} crevasse(s), {_caveSites.Count} ice cave(s), icefall {_hasIcefall}.");
        }

        // ==================================================================
        // The station
        // ==================================================================
        /// <summary>
        /// The research station: a ring of prefab modules on stilts round a central yard,
        /// joined by raised walkways with a stair down at each end of the ring; a radar dome
        /// on a tower, radio masts, fuel tanks on cradles, a helipad, snowcats.
        ///
        /// The modules are sealed; the space under them is open, two and a half metres high,
        /// so an enemy can walk under a module and a player can crawl through the dark under
        /// the whole station. The walkways are ground, reached by their stairs.
        /// </summary>
        private static void BuildStation(Transform parent, int layer, int backdrop, System.Random rng)
        {
            var station = new GameObject("Station").transform;
            station.SetParent(parent, false);

            var c = _stationCentre;
            float floor = GroundHeightAt(c.x, c.y);
            const float stilts = 12 * StairRise;   // a whole number of risers for the stairs
            const float modH = 3.6f;

            var mats = new[] { _moduleRed, _moduleOrange, _moduleYellow, _moduleRed };

            // Modules round the yard, each facing it; the gaps between them are the ways in.
            int count = 6;
            var doors = new List<Vector3>();
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count + 0.3f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var at = new Vector3(c.x, floor, c.y) + dir * 24f;
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                var rot = Quaternion.Euler(0f, yaw, 0f);

                const float len = 14f, wide = 5f;
                var body = new MeshBuild { UVScale = 0.3f };
                body.Box(at + Vector3.up * (stilts + modH * 0.5f), new Vector3(len, modH, wide), rot);
                // Rounded ends read as a module rather than a shed.
                foreach (float e in new[] { -1f, 1f })
                    body.Tube(at + rot * new Vector3(e * len * 0.5f, stilts + 0.1f, 0f),
                              at + rot * new Vector3(e * (len * 0.5f + 0.9f), stilts + 0.1f, 0f), 0.1f, 0.1f, 4);
                var mod = MeshObject(station, "Module", ToMesh(body, DenseKey("module")), mats[i % mats.Length],
                                     Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");
                NoStanding(mod);
                if (mod != null) Mark(mod, new Color(0.70f, 0.24f, 0.14f), 3);
                SealBox(station, at + Vector3.up * (stilts + modH * 0.5f + 2f), new Vector3(len, modH, len));

                // Stilts, windows, and a door onto the walkway on the yard side.
                var legs = new MeshBuild { UVScale = 0.5f };
                foreach (float x in new[] { -len * 0.4f, 0f, len * 0.4f })
                    foreach (float z in new[] { -wide * 0.4f, wide * 0.4f })
                        legs.Box(at + rot * new Vector3(x, stilts * 0.5f, z), new Vector3(0.35f, stilts, 0.35f), rot);
                MeshObject(station, "Stilts", ToMesh(legs, DenseKey("stilts")), _moduleGrey,
                           Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");

                var glass = new MeshBuild { UVScale = 0.5f };
                foreach (float z in new[] { -wide * 0.5f - 0.04f, wide * 0.5f + 0.04f })
                    for (float x = -len * 0.5f + 1.5f; x <= len * 0.5f - 1.5f; x += 2.2f)
                        glass.Box(at + rot * new Vector3(x, stilts + modH * 0.55f, z), new Vector3(1.2f, 0.9f, 0.08f), rot);
                Hide(MeshObject(station, "ModuleWindows", ToMesh(glass, DenseKey("modglass")), _denseGlass,
                                Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false));

                doors.Add(at - dir * (wide * 0.5f + 1.6f));
            }

            // A landing at every door, where two walkway runs meet at an angle. Without it the
            // runs only touched at a point, the corners left gaps the navmesh broke at, and
            // stretches of the ring with no stair of their own were cut off.
            var landings = new MeshBuild { UVScale = 0.5f };
            foreach (var d in doors)
                landings.Box(d + Vector3.up * (stilts - 0.07f), new Vector3(3.4f, 0.14f, 3.4f), Quaternion.identity);
            MeshObject(station, "DoorLandings", ToMesh(landings, DenseKey("landings")), _zoneDeck,
                       Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");

            // Walkways joining the doors round the ring, at floor height; two stairs down.
            for (int i = 0; i < count; i++)
            {
                if (i == count - 1) continue;   // one gap left open, so the ring is a horseshoe with two ends
                var from = doors[i];
                var to = doors[(i + 1) % count];
                BuildCatwalk(station, layer, from, to, stilts, stair: i == 0 || i == count - 2);
            }

            // ---- the yard: radar dome, masts, tanks, helipad, snowcats ----
            var centre = new Vector3(c.x, floor, c.y);

            var radar = new MeshBuild { UVScale = 0.3f };
            var radarAt = centre + new Vector3(8f, 0f, -6f);
            foreach (float x in new[] { -1.4f, 1.4f })
                foreach (float z in new[] { -1.4f, 1.4f })
                    radar.Box(radarAt + new Vector3(x, 4f, z), new Vector3(0.3f, 8f, 0.3f), Quaternion.identity);
            radar.Box(radarAt + Vector3.up * 8.2f, new Vector3(4f, 0.4f, 4f), Quaternion.identity);
            var ball = MeshObject(station, "RadarDome", BoulderMesh(7, 0.02f, 1f), _pineSnowMat,
                                  radarAt + Vector3.up * 11f, Quaternion.identity, Vector3.one * 1.35f, layer, "Metal", collider: false);
            NoStanding(ball);
            NoStanding(MeshObject(station, "RadarTower", ToMesh(radar, DenseKey("radar")), _moduleGrey,
                                  Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal"));

            var masts = new MeshBuild { UVScale = 0.5f };
            for (int i = 0; i < 2; i++)
            {
                var m = centre + new Vector3(-9f + i * 5f, 0f, 9f);
                float h = 22f + i * 6f;
                masts.Tube(m, m + Vector3.up * h, 0.3f, 0.1f, 6);
                for (float y = 5f; y < h; y += 5f)
                    masts.Box(m + Vector3.up * y, new Vector3(1.6f, 0.08f, 0.08f), Quaternion.Euler(0f, y * 20f, 0f));
            }
            NoStanding(MeshObject(station, "RadioMasts", ToMesh(masts, DenseKey("masts")), _moduleRed,
                                  Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal"));

            var tanks = new MeshBuild { UVScale = 0.3f };
            for (int i = 0; i < 2; i++)
            {
                var t = centre + new Vector3(-10f, 1.6f, -6f + i * 4.2f);
                tanks.Tube(t + Vector3.left * 4f, t + Vector3.right * 4f, 1.5f, 1.5f, 14);
                foreach (float x in new[] { -2.6f, 2.6f })
                    tanks.Box(t + new Vector3(x, -0.9f, 0f), new Vector3(0.4f, 1.4f, 2.6f), Quaternion.identity);
            }
            NoStanding(MeshObject(station, "FuelTanks", ToMesh(tanks, DenseKey("fueltanks")), _pineSnowMat,
                                  Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal"));
            SealBox(station, centre + new Vector3(-10f, 1.6f, -3.9f), new Vector3(8f, 3.2f, 7.4f));

            // The helipad, outside the ring, with its H.
            var padAt = centre + new Vector3(0f, 0f, -44f);
            {
                var pad = new MeshBuild { UVScale = 0.2f };
                float py = GroundHeightAt(padAt.x, padAt.z) + 0.04f;
                pad.Tube(new Vector3(padAt.x, py - 0.02f, padAt.z), new Vector3(padAt.x, py, padAt.z), 9f, 9f, 24);
                Flat(station, backdrop, "Helipad", pad, DenseKey("helipad"), _padMat);

                var h = new MeshBuild { UVScale = 0.5f };
                h.Box(new Vector3(padAt.x - 2f, py + 0.02f, padAt.z), new Vector3(0.8f, 0.02f, 6f), Quaternion.identity);
                h.Box(new Vector3(padAt.x + 2f, py + 0.02f, padAt.z), new Vector3(0.8f, 0.02f, 6f), Quaternion.identity);
                h.Box(new Vector3(padAt.x, py + 0.02f, padAt.z), new Vector3(4f, 0.02f, 0.8f), Quaternion.identity);
                Flat(station, backdrop, "HelipadH", h, DenseKey("helipadh"), _moduleYellow);
            }

            // Snowcats parked in the yard.
            for (int i = 0; i < 3; i++)
            {
                var at = centre + new Vector3(Rand(rng, -6f, 6f), 0f, Rand(rng, -14f, -8f) + i * 7f);
                Snowcat(station, layer, rng, at, Rand(rng, 0f, 360f));
            }

            // The dashboard card: over the station from outside the ring.
            var anchor = new GameObject("PreviewAnchor").transform;
            anchor.SetParent(parent, false);
            anchor.position = centre + new Vector3(-48f, 14f, -40f);
            anchor.rotation = Quaternion.LookRotation(centre + new Vector3(0f, 4f, 0f) - anchor.position);
        }

        private static void Snowcat(Transform parent, int layer, System.Random rng, Vector3 at, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var body = new MeshBuild { UVScale = 0.4f };
            body.Box(at + rot * new Vector3(0f, 1.6f, 0.4f), new Vector3(2.4f, 1.6f, 3.8f), rot);
            body.Box(at + rot * new Vector3(0f, 1.2f, -2f), new Vector3(2.4f, 0.8f, 1.6f), rot);
            var tracks = new MeshBuild { UVScale = 0.4f };
            foreach (float s in new[] { -1.35f, 1.35f })
                tracks.Box(at + rot * new Vector3(s, 0.45f, 0f), new Vector3(0.7f, 0.9f, 5f), rot);

            var go = MeshObject(parent, "Snowcat", ToMesh(body, DenseKey("snowcat")), rng.Next(2) == 0 ? _moduleOrange : _moduleRed,
                                Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");
            NoStanding(go);
            NoStanding(MeshObject(parent, "SnowcatTracks", ToMesh(tracks, DenseKey("snowcattracks")), _moduleGrey,
                                  Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal"));
            SealBox(parent, at + Vector3.up * 1.2f, new Vector3(4f, 2.4f, 4f));
        }

        // ==================================================================
        // Crevasses
        // ==================================================================
        /// <summary>
        /// A crevasse: ice walls down into the hole the terrain left, a lip of ice over the
        /// staircase edge the grid cut, a kill trigger at the bottom, and a rope bridge across.
        /// </summary>
        private static void BuildCrevasse(Transform parent, int layer, System.Random rng, Crevasse c)
        {
            var group = new GameObject("Crevasse").transform;
            group.SetParent(parent, false);

            var ab = c.B - c.A;
            float len = ab.magnitude;
            var dir = ab / len;
            var side = new Vector2(-dir.y, dir.x);
            const float depth = 12f;

            var walls = new MeshBuild { UVScale = 0.3f };
            int steps = Mathf.CeilToInt(len / 2f);

            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 Lip(int i, float extra)
                {
                    float t = i / (float)steps;
                    float taper = Mathf.Max(0.35f, Mathf.Sin(t * Mathf.PI));
                    float jag = Fbm2(i * 0.4f, s * 3f, 8101, 2) * 0.6f;
                    var p = c.A + dir * (t * len) + side * s * (c.Half * taper + jag + extra);
                    return new Vector3(p.x, SurfaceHeightAt(p.x, p.y), p.y);
                }

                for (int i = 0; i < steps; i++)
                {
                    // The wall, straight down from the edge of the hole.
                    var a0 = Lip(i, 0f); var a1 = Lip(i + 1, 0f);
                    var d0 = a0 + Vector3.down * depth; var d1 = a1 + Vector3.down * depth;
                    walls.Quad(a0, a1, d1, d0); walls.Quad(d0, d1, a1, a0);

                    // The lip: a skin of ice from the edge out over the grid's staircase, lying on it.
                    var o0 = Lip(i, 6f) + Vector3.up * 0.08f; var o1 = Lip(i + 1, 6f) + Vector3.up * 0.08f;
                    var i0 = a0 + Vector3.up * 0.08f; var i1 = a1 + Vector3.up * 0.08f;
                    AddUp(walls, i0, i1, o1, o0);
                }
            }

            var wallGo = MeshObject(group, "CrevasseIce", ToMesh(walls, DenseKey("crevasse")), _glacierMat,
                                    Vector3.zero, Quaternion.identity, Vector3.one, layer, IceTag);
            if (wallGo != null) Mark(wallGo, new Color(0.30f, 0.46f, 0.62f), 2);

            // The kill trigger, below the lip, along the crack.
            for (float t = 0.05f; t < 1f; t += 0.1f)
            {
                var p = c.A + ab * t;
                var go = new GameObject("CrevasseKill");
                go.transform.SetParent(group, false);
                go.transform.position = new Vector3(p.x, SurfaceHeightAt(p.x, p.y) - 5f, p.y);
                go.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y));
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(c.Half * 2f + 1f, 4f, len * 0.11f);
                go.AddComponent<KillVolume>().instantKill = true;
            }

            // A rope bridge across the widest part: planks on two ropes, a hand rope each side.
            var mid = c.A + ab * 0.5f;
            float span = c.Half * 2f + 5f;
            var bridge = new MeshBuild { UVScale = 0.5f };
            var ropes = new MeshBuild { UVScale = 0.5f };
            var e0 = mid - side * span * 0.5f; var e1 = mid + side * span * 0.5f;
            float h0 = SurfaceHeightAt(e0.x, e0.y), h1 = SurfaceHeightAt(e1.x, e1.y);
            var rot = Quaternion.LookRotation(new Vector3(side.x, 0f, side.y));

            int planks = Mathf.CeilToInt(span / 0.5f);
            Vector3 prevL = Vector3.zero, prevR = Vector3.zero;
            for (int k = 0; k <= planks; k++)
            {
                float t = k / (float)planks;
                var q = Vector2.Lerp(e0, e1, t);
                float y = Mathf.Lerp(h0, h1, t) - Mathf.Sin(t * Mathf.PI) * 0.35f + 0.1f;
                var at = new Vector3(q.x, y, q.y);
                bridge.Box(at, new Vector3(1.8f, 0.08f, 0.42f), rot);

                var l = at + rot * new Vector3(-1f, 1f, 0f);
                var r = at + rot * new Vector3(1f, 1f, 0f);
                if (k > 0) { ropes.Tube(prevL, l, 0.03f, 0.03f, 4); ropes.Tube(prevR, r, 0.03f, 0.03f, 4); }
                if (k % 3 == 0) { ropes.Tube(at + rot * new Vector3(-1f, 0f, 0f), l, 0.02f, 0.02f, 3); ropes.Tube(at + rot * new Vector3(1f, 0f, 0f), r, 0.02f, 0.02f, 3); }
                prevL = l; prevR = r;
            }
            MeshObject(group, "RopeBridge", ToMesh(bridge, DenseKey("ropebridge")), _campTimberMat,
                       Vector3.zero, Quaternion.identity, Vector3.one, layer, "Wood");
            Hide(MeshObject(group, "BridgeRopes", ToMesh(ropes, DenseKey("bridgeropes")), _ropeMat,
                            Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false));
            _ = rng;
        }

        // ==================================================================
        // Ice caves and the frozen waterfall
        // ==================================================================
        /// <summary>
        /// An ice cave: an arched tunnel through a mound of glacier ice, open at both ends.
        /// The arch is drawn inside and out -- it is seen from inside -- and the blocks heaped
        /// on it stand clear of the passage, so the way through is never pinched.
        /// </summary>
        private static void BuildIceCave(Transform parent, int layer, System.Random rng, Vector3 site)
        {
            var at = new Vector3(site.x, GroundHeightAt(site.x, site.y), site.y);
            var rot = Quaternion.Euler(0f, site.z, 0f);
            const float len = 22f, rad = 3.8f;
            const int arcs = 10;

            var arch = new MeshBuild { UVScale = 0.3f };
            var outer = new MeshBuild { UVScale = 0.3f };
            for (int i = 0; i < arcs; i++)
            {
                float a0 = i * Mathf.PI / arcs, a1 = (i + 1) * Mathf.PI / arcs;
                Vector3 P(float a, float z, float r) => at + rot * new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 1.15f, z);

                arch.Quad(P(a0, -len * 0.5f, rad), P(a0, len * 0.5f, rad), P(a1, len * 0.5f, rad), P(a1, -len * 0.5f, rad));
                arch.Quad(P(a1, -len * 0.5f, rad), P(a1, len * 0.5f, rad), P(a0, len * 0.5f, rad), P(a0, -len * 0.5f, rad));

                // A thick shell: the outside of the mound, drawn outward.
                outer.Quad(P(a1, -len * 0.5f, rad + 2.4f), P(a1, len * 0.5f, rad + 2.4f), P(a0, len * 0.5f, rad + 2.4f), P(a0, -len * 0.5f, rad + 2.4f));
                foreach (float e in new[] { -len * 0.5f, len * 0.5f })
                {
                    // The ring face at each mouth, between the inner arch and the outer shell.
                    var q0 = P(a0, e, rad); var q1 = P(a1, e, rad); var q2 = P(a1, e, rad + 2.4f); var q3 = P(a0, e, rad + 2.4f);
                    outer.Quad(q0, q1, q2, q3); outer.Quad(q3, q2, q1, q0);
                }
            }

            var archGo = MeshObject(parent, "IceCave", ToMesh(arch, DenseKey("icecave")), _glacierMat,
                                    Vector3.zero, Quaternion.identity, Vector3.one, layer, IceTag);
            NoStanding(archGo);
            NoStanding(MeshObject(parent, "IceCaveShell", ToMesh(outer, DenseKey("icecaveshell")), _glacierMat,
                                  Vector3.zero, Quaternion.identity, Vector3.one, layer, IceTag, collider: false));

            // Blocks heaped over and beside it, clear of the passage.
            for (int k = 0; k < 7; k++)
            {
                float z = Rand(rng, -len * 0.45f, len * 0.45f);
                float x = Rand(rng, -1f, 1f) * (rad + 3f) + Mathf.Sign(Rand(rng, -1f, 1f)) * 2f;
                var p = at + rot * new Vector3(x, Mathf.Abs(x) < rad + 2f ? rad * 1.15f + 2.2f : 0.5f, z);
                var block = MeshObject(parent, "IceBlock", BoulderMesh(rng.Next(1, 30), 0.3f, 0.8f), _glacierMat, p,
                                       Quaternion.Euler(Rand(rng, -20f, 20f), Rand(rng, 0f, 360f), 0f),
                                       Vector3.one * Rand(rng, 1.4f, 2.4f), layer, IceTag, collider: false);
                NoStanding(block);
            }

            // Blue light inside, so the walls glow from within.
            var lamp = new GameObject("IceCaveLight");
            lamp.transform.SetParent(parent, false);
            lamp.transform.position = at + Vector3.up * 3f;
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 14f;
            light.intensity = 1.2f;
            light.color = new Color(0.55f, 0.78f, 1f);
            light.shadows = LightShadows.None;
        }

        /// <summary>
        /// A frozen waterfall: a rock cliff with a cascade of ice down its face into a frozen
        /// pool. The cliff is a butte, sunk and sealed like every rock in the kit.
        /// </summary>
        private static void BuildIcefall(Transform parent, int layer, System.Random rng)
        {
            var p = _icefallSite;
            float ground = LowestGroundIn(p.x, p.y, 16f);
            const float w = 30f, h = 22f;

            var cliff = MeshObject(parent, "Icefall", ButteMesh(rng.Next(1, 999), sides: 9), _rockMat,
                                   new Vector3(p.x, ground - 1.5f, p.y), Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f),
                                   new Vector3(w * 0.5f, h, w * 0.5f), layer, _theme.wallTag);
            NoStanding(cliff);
            SealBox(parent, new Vector3(p.x, ground + h * 0.5f, p.y), new Vector3(w * 0.9f, h, w * 0.9f));

            // The cascade on the face towards the middle of the map.
            var toCentre = (-p).normalized;
            var face = p + toCentre * (w * 0.46f);
            var ice = new MeshBuild { UVScale = 0.3f };
            for (int i = 0; i < 22; i++)
            {
                float across = Rand(rng, -5f, 5f);
                var foot2 = face + new Vector2(-toCentre.y, toCentre.x) * across + toCentre * Rand(rng, 0.5f, 2.5f);
                var foot = new Vector3(foot2.x, ground, foot2.y);
                float top = ground + h * Rand(rng, 0.7f, 0.98f);
                ice.Tube(foot, new Vector3(foot.x - toCentre.x * 1.5f, top, foot.z - toCentre.y * 1.5f), Rand(rng, 0.5f, 1.2f), Rand(rng, 0.2f, 0.6f), 6);
            }
            NoStanding(MeshObject(parent, "FrozenFall", ToMesh(ice, DenseKey("frozenfall")), _glacierMat,
                                  Vector3.zero, Quaternion.identity, Vector3.one, layer, IceTag));

            var pool = new MeshBuild { UVScale = 0.2f };
            var poolAt = face + toCentre * 6f;
            float py = GroundHeightAt(poolAt.x, poolAt.y) + 0.06f;
            pool.Tube(new Vector3(poolAt.x, py - 0.04f, poolAt.y), new Vector3(poolAt.x, py, poolAt.y), 7f, 7f, 20);
            Flat(parent, LayerMask.NameToLayer("Backdrop"), "FrozenPool", pool, DenseKey("frozenpool"), _iceMat);
        }

        // ==================================================================
        // Pine forest
        // ==================================================================
        /// <summary>
        /// Pine forest in bands across the field and thick towards its edges: dark, snow-laden
        /// trees, the contrast the white ground was missing and a scale to judge distance by.
        ///
        /// Placed where a noise field says forest and the physics scene says bare snow, and
        /// welded a grove at a time -- one mesh for the trunks, which block, and one for the
        /// foliage and its snow, which does not and is held off the bake.
        /// </summary>
        private static void BuildPineForest(Transform parent, int layer, System.Random rng, float half)
        {
            var groups = new Dictionary<Vector2Int, (MeshBuild trunks, MeshBuild boughs, MeshBuild snow)>();
            int trees = 0;

            for (float x = -half + 18f; x <= half - 18f; x += 6.5f)
                for (float z = -half + 18f; z <= half - 18f; z += 6.5f)
                {
                    var p = new Vector2(x + Rand(rng, -2.5f, 2.5f), z + Rand(rng, -2.5f, 2.5f));

                    float bands = Fbm2(p.x * 0.012f, p.y * 0.012f, 8201, 3) + 0.5f;
                    float edge = Mathf.InverseLerp(half * 0.6f, half - 20f, Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)));
                    float density = bands * 0.9f + edge * 0.6f;
                    if (density < 0.72f || Rand(rng, 0f, 1f) > density - 0.35f) continue;

                    bool kept = false;
                    foreach (var s in _snowSites) if ((new Vector2(s.x, s.y) - p).magnitude < s.z) { kept = true; break; }
                    if (kept || InCrevasse(p.x, p.y, 4f) || !BareSnow(p, 2.2f)) continue;

                    var key = new Vector2Int(Mathf.FloorToInt(p.x / 60f), Mathf.FloorToInt(p.y / 60f));
                    if (!groups.TryGetValue(key, out var g))
                        g = groups[key] = (new MeshBuild { UVScale = 0.5f }, new MeshBuild { UVScale = 0.5f }, new MeshBuild { UVScale = 0.5f });

                    Pine(g.trunks, g.boughs, g.snow, rng, new Vector3(p.x, GroundHeightAt(p.x, p.y) - 0.3f, p.y));
                    trees++;
                }

            foreach (var g in groups.Values)
            {
                MeshObject(parent, "PineTrunks", ToMesh(g.trunks, DenseKey("pinetrunks")), _barkMat,
                           Vector3.zero, Quaternion.identity, Vector3.one, layer, "Wood");
                var boughs = MeshObject(parent, "PineBoughs", ToMesh(g.boughs, DenseKey("pineboughs")), _pineMat,
                                        Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false);
                NoStanding(boughs); Hide(boughs);
                var snow = MeshObject(parent, "PineSnow", ToMesh(g.snow, DenseKey("pinesnow")), _pineSnowMat,
                                      Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false);
                NoStanding(snow); Hide(snow);
            }

            Debug.Log($"[FPSKit] snow: {trees} pine(s).");
        }

        /// <summary>One pine: a trunk, three or four tiers of boughs narrowing upwards, snow on each.</summary>
        private static void Pine(MeshBuild trunks, MeshBuild boughs, MeshBuild snow, System.Random rng, Vector3 foot)
        {
            float h = Rand(rng, 7f, 14f);
            float r = h * Rand(rng, 0.2f, 0.26f);
            trunks.Tube(foot, foot + Vector3.up * h * 0.35f, 0.3f, 0.22f, 6);

            int tiers = 3 + rng.Next(2);
            for (int t = 0; t < tiers; t++)
            {
                float f = t / (float)tiers;
                float y0 = h * (0.2f + f * 0.72f);
                float y1 = y0 + h * 0.36f;
                float radius = r * (1f - f * 0.62f);
                boughs.Tube(foot + Vector3.up * y0, foot + Vector3.up * y1, radius, 0.05f, 8);

                // Snow sitting on the upper half of the tier.
                snow.Tube(foot + Vector3.up * (y0 + (y1 - y0) * 0.42f), foot + Vector3.up * (y1 - 0.02f),
                          radius * 0.6f, 0.04f, 8);
            }
        }

        /// <summary>Whether a circle of snow is bare, asked of the physics scene: see OpenSand.</summary>
        private static bool BareSnow(Vector2 p, float radius)
        {
            Physics.SyncTransforms();
            float g = GroundHeightAt(p.x, p.y);
            var hits = Physics.OverlapCapsule(new Vector3(p.x, g + 0.6f + radius, p.y), new Vector3(p.x, g + 12f, p.y),
                                              radius, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
                if (!hit.CompareTag(SnowTag)) return false;
            return true;
        }

        /// <summary>Snow blown along the ground round the player, under the falling snow.</summary>
        private static void BuildSnowDrift(Transform parent)
        {
            var go = new GameObject("DriftingSnow");
            go.transform.SetParent(parent, false);
            var drift = go.AddComponent<Snowfall>();
            drift.flakeMaterial = FlakeMaterial();
            drift.flakes = 500;
            drift.ceiling = 2.5f;
            drift.spread = 30f;
            drift.fallSpeed = 0.2f;
            drift.wind = new Vector3(7f, 0f, -2.5f);
            drift.tint = new Color(1f, 1f, 1f, 0.7f);
            drift.tintAlt = new Color(0.9f, 0.94f, 1f, 0.5f);
            drift.size = new Vector2(0.04f, 0.1f);
        }
    }
}
#endif
