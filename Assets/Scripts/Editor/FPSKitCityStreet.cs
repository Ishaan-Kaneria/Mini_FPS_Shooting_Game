#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// What makes the rooftop zone's streets, and the air round its buildings, read as a lived-in district at
    /// night: crossings, lamps, parked cars, awnings, shop-window glow, neon, junction lights and the far
    /// skyline. Everything here is decoration or cover; none of it may leave a flat top that bakes (cars and
    /// signs are kept off the navigation bake).
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static Material _cityLine, _cityLineYellow, _cityLamp, _cityShop;
        private static Material[] _cityPaint, _cityAwning;

        private static void EnsureStreetMaterials()
        {
            if (_cityLine != null && _cityPaint != null) return;

            _cityLine = MakeMaterial("CityLine", new Color(0.62f, 0.62f, 0.60f), 0.05f, 0f);
            _cityLineYellow = MakeMaterial("CityLineYellow", new Color(0.60f, 0.46f, 0.10f), 0.05f, 0f);

            _cityLamp = MakeMaterial("CityLamp", new Color(0.4f, 0.35f, 0.25f), 0.3f, 0f);
            _cityLamp.EnableKeyword("_EMISSION");
            _cityLamp.SetColor("_EmissionColor", new Color(1.0f, 0.80f, 0.52f) * 3.4f);
            _cityLamp.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;   // None lets URP clear the _EMISSION keyword on import

            _cityShop = MakeMaterial("CityShop", new Color(0.35f, 0.30f, 0.22f), 0.35f, 0f);
            _cityShop.EnableKeyword("_EMISSION");
            _cityShop.SetColor("_EmissionColor", new Color(1.0f, 0.74f, 0.42f) * 0.9f);
            _cityShop.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;   // None lets URP clear the _EMISSION keyword on import

            var paint = new[]
            {
                new Color(0.34f, 0.07f, 0.07f), new Color(0.07f, 0.12f, 0.28f), new Color(0.20f, 0.21f, 0.23f),
                new Color(0.62f, 0.62f, 0.60f), new Color(0.72f, 0.54f, 0.08f), new Color(0.06f, 0.20f, 0.14f),
            };
            _cityPaint = new Material[paint.Length];
            for (int i = 0; i < paint.Length; i++) _cityPaint[i] = MakeMaterial($"CityPaint_{i}", paint[i], 0.45f, 0.35f);

            var awn = new[] { new Color(0.40f, 0.08f, 0.08f), new Color(0.07f, 0.25f, 0.16f), new Color(0.08f, 0.12f, 0.30f), new Color(0.35f, 0.26f, 0.08f) };
            _cityAwning = new Material[awn.Length];
            for (int i = 0; i < awn.Length; i++) _cityAwning[i] = MakeMaterial($"CityAwning_{i}", awn[i], 0.15f, 0f);
        }

        /// <summary>True on any road tile's cell (the cross-shaped carriageway and its pavements).</summary>
        private static bool OnRoadCell(float x, float z)
            => _roadTiles.Contains(new Vector2Int(Mathf.RoundToInt(x / Tile), Mathf.RoundToInt(z / Tile)));

        // ==================================================================
        // Crossings and markings
        // ==================================================================
        /// <summary>
        /// Zebra crossings across every arm of every junction, and a dashed yellow line down each straight.
        /// Flat quads a hand's breadth above the road, off the bake and off the minimap.
        /// </summary>
        private static void BuildCityStreet(Transform root, int layer, System.Random rng, float half)
        {
            EnsureStreetMaterials();
            var group = new GameObject("StreetMarkings").transform;
            group.SetParent(root, false);

            var white = new MeshBuild { UVScale = 1f };
            var yellow = new MeshBuild { UVScale = 1f };
            const float y = 0.05f;

            void Strip(MeshBuild m, float x0, float z0, float x1, float z1)
                => m.Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0));

            _cityCrossings.Clear();
            var dirs = new[] { Vector3.right, Vector3.back, Vector3.left, Vector3.forward };
            var step = new[] { Vector2Int.right, Vector2Int.down, Vector2Int.left, Vector2Int.up };

            foreach (var tile in _roadTiles)
            {
                bool n = _roadTiles.Contains(tile + Vector2Int.up), s = _roadTiles.Contains(tile + Vector2Int.down);
                bool e = _roadTiles.Contains(tile + Vector2Int.right), w = _roadTiles.Contains(tile + Vector2Int.left);
                float cx = tile.x * Tile, cz = tile.y * Tile;
                var centre = new Vector3(cx, 0f, cz);
                int arms = (n ? 1 : 0) + (s ? 1 : 0) + (e ? 1 : 0) + (w ? 1 : 0);

                if (arms >= 3 || (arms == 2 && !((n && s) || (e && w))))
                {
                    // A zebra on each arm just outside the junction square, a stop line for the lane that enters it
                    // (traffic keeps to the right), and tactile paving where the footway meets the crossing.
                    for (int i = 0; i < 4; i++)
                    {
                        if (!_roadTiles.Contains(tile + step[i])) continue;
                        Zebra(white, yellow, centre, dirs[i], CarriageHalf + 0.8f, 3.2f, tactile: true);
                        AddCrossingRect(centre, dirs[i], CarriageHalf + 0.8f, 3.2f);
                        StopLine(white, centre, dirs[i], CarriageHalf + 4.3f, -2.5f);
                    }
                }
                else if (MidBlockCrossing(tile))
                {
                    var along = n ? Vector3.forward : Vector3.right;
                    Zebra(white, yellow, centre, along, -1.6f, 3.2f, tactile: true);
                    AddCrossingRect(centre, along, -1.6f, 3.2f);
                    StopLine(white, centre, along, -3.2f, -2.5f);          // the lane going +along stops short of the zebra
                    StopLine(white, centre, -along, -3.2f, -2.5f);         // and the other lane
                }
                else if (arms == 2 && n && s)
                    for (float t = -9f; t < 9f; t += 4f) Strip(yellow, cx - 0.1f, cz + t, cx + 0.1f, cz + t + 2f);
                else if (arms == 2 && e && w)
                    for (float t = -9f; t < 9f; t += 4f) Strip(yellow, cx + t, cz - 0.1f, cx + t + 2f, cz + 0.1f);
            }

            int paint = LayerMask.NameToLayer("Backdrop");
            if (paint < 0) paint = layer;
            Flat(group, paint, "Crossings", white, "citycrossings", _cityLine);
            Flat(group, paint, "CentreLines", yellow, "citycentre", _cityLineYellow);
        }

        // ==================================================================
        // Furniture: lamps, alley props
        // ==================================================================
        private static void BuildCityFurniture(Transform root, int layer, System.Random rng)
        {
            var group = new GameObject("StreetFurniture").transform;
            group.SetParent(root, false);
            int backdrop = LayerMask.NameToLayer("Backdrop");
            if (backdrop < 0) backdrop = layer;

            // ---- lamps: one on every straight tile (20 m), alternating sides, each with a pool of light on the road ----
            var poles = new MeshBuild { UVScale = 0.5f };
            var heads = new MeshBuild { UVScale = 1f };
            var pools = new List<Vector4>();            // x, z, alongX (1) or alongZ (0), unused
            var lampLights = new GameObject("LampLights").transform;
            lampLights.SetParent(group, false);
            int lamp = 0;
            var ordered = new List<Vector2Int>(_roadTiles);
            ordered.Sort((p, q) => (p.x * 7919 + p.y).CompareTo(q.x * 7919 + q.y));
            foreach (var tile in ordered)
            {
                bool alongZ = _roadTiles.Contains(tile + Vector2Int.up) || _roadTiles.Contains(tile + Vector2Int.down);
                bool alongX = _roadTiles.Contains(tile + Vector2Int.left) || _roadTiles.Contains(tile + Vector2Int.right);
                float cx = tile.x * Tile, cz = tile.y * Tile;

                // Only on a straight: a pole in a junction is a pole in the road.
                if (alongZ == alongX) continue;
                float side = (alongZ ? tile.y : tile.x) % 2 == 0 ? 1f : -1f;
                var at = alongZ ? new Vector3(cx + side * 6f, 0f, cz) : new Vector3(cx, 0f, cz + side * 6f);
                var reach = alongZ ? new Vector3(-side, 0f, 0f) : new Vector3(0f, 0f, -side);

                AddLamp(poles, heads, at, reach);
                var tip = at + Vector3.up * LampHeight + reach * LampReach;
                pools.Add(new Vector4(tip.x, tip.z, alongZ ? 0f : 1f, 0f));

                if (lamp % LampLightEvery == 0)
                {
                    var go = new GameObject($"LampLight_{lamp}");
                    go.transform.SetParent(lampLights, false);
                    go.transform.position = tip + Vector3.down * 0.25f;
                    go.transform.rotation = Quaternion.LookRotation(Vector3.down, reach);
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Spot;
                    l.color = new Color(1.0f, 0.70f, 0.40f);
                    l.intensity = 30f;
                    l.range = 15f;
                    l.spotAngle = 125f;
                    l.innerSpotAngle = 55f;
                    l.shadows = LightShadows.None;
                }
                lamp++;
            }

            BuildLampPools(group, backdrop, pools);

            var poleGo = MeshObject(group, "LampPoles", ToMesh(poles, "citylamppoles"), _cityPlant, Vector3.zero, Quaternion.identity,
                                    Vector3.one, backdrop, "Metal", collider: false);
            NoStanding(poleGo); Hide(poleGo);
            var headGo = MeshObject(group, "LampHeads", ToMesh(heads, "citylampheads"), _cityLamp, Vector3.zero, Quaternion.identity,
                                    Vector3.one, backdrop, null, collider: false);
            NoStanding(headGo); Hide(headGo);

            // ---- alley props: dumpsters and crates against the walls, where the lots are one alley apart ----
            int placed = 0;
            foreach (var a in _lots)
                foreach (var b in _lots)
                {
                    if (a == b || placed > 60) continue;
                    float gx = b.x0 - a.x1;
                    if (Mathf.Abs(gx - Alley) < 0.2f && Mathf.Min(a.z1, b.z1) - Mathf.Max(a.z0, b.z0) > 12f)
                    {
                        for (float z = Mathf.Max(a.z0, b.z0) + 6f; z < Mathf.Min(a.z1, b.z1) - 6f; z += Rand(rng, 11f, 19f))
                            if (rng.NextDouble() < 0.7 && AlleyProp(group, rng, new Vector3(a.x1 + 1.3f, 0f, z), 90f)) placed++;
                    }
                    float gz = b.z0 - a.z1;
                    if (Mathf.Abs(gz - Alley) < 0.2f && Mathf.Min(a.x1, b.x1) - Mathf.Max(a.x0, b.x0) > 12f)
                    {
                        for (float x = Mathf.Max(a.x0, b.x0) + 6f; x < Mathf.Min(a.x1, b.x1) - 6f; x += Rand(rng, 11f, 19f))
                            if (rng.NextDouble() < 0.7 && AlleyProp(group, rng, new Vector3(x, 0f, a.z1 + 1.3f), 0f)) placed++;
                    }
                }
        }

        private const float LampHeight = 8.2f, LampReach = 2.7f;
        private const int LampLightEvery = 2;

        private static void AddLamp(MeshBuild poles, MeshBuild heads, Vector3 at, Vector3 reach)
        {
            poles.Tube(at, at + Vector3.up * 0.35f, 0.22f, 0.15f, 8);                       // base
            poles.Tube(at + Vector3.up * 0.3f, at + Vector3.up * LampHeight, 0.13f, 0.08f, 8);
            Vector3 top = at + Vector3.up * (LampHeight - 0.1f);
            Vector3 tip = at + Vector3.up * LampHeight + reach * LampReach;
            poles.Tube(top, tip, 0.06f, 0.05f, 6);                                          // the arm
            heads.Box(tip + Vector3.down * 0.10f + reach * 0.15f, new Vector3(0.50f, 0.16f, 1.05f), Quaternion.LookRotation(reach));   // luminaire, long axis along the arm
        }

        /// <summary>
        /// One additive soft-edged quad of warm light on the road under each lamp head, elongated along the
        /// street. Cheap: a lamp is a real light only on every second pole (and only a few reach any one wall),
        /// so these carry the look of a lit street; the ground, which takes most of the light, shows it anyway.
        /// </summary>
        private static void BuildLampPools(Transform group, int layer, List<Vector4> pools)
        {
            if (pools.Count == 0) return;
            EnsureRoadMaterials();
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            const float y = 0.075f, across = 9f, along = 13f;
            foreach (var p in pools)
            {
                float hx = p.z > 0.5f ? along * 0.5f : across * 0.5f;
                float hz = p.z > 0.5f ? across * 0.5f : along * 0.5f;
                int i0 = v.Count;
                v.Add(new Vector3(p.x - hx, y, p.y - hz)); uv.Add(new Vector2(0f, 0f));
                v.Add(new Vector3(p.x - hx, y, p.y + hz)); uv.Add(new Vector2(0f, 1f));
                v.Add(new Vector3(p.x + hx, y, p.y + hz)); uv.Add(new Vector2(1f, 1f));
                v.Add(new Vector3(p.x + hx, y, p.y - hz)); uv.Add(new Vector2(1f, 0f));
                t.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
            }
            var mesh = new Mesh { name = "citylamppools" };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = MeshObject(group, "LampPools", mesh, _cityPool, Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false);
            Hide(go);
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private static bool AlleyProp(Transform parent, System.Random rng, Vector3 at, float yaw)
        {
            if (Blocked(new Vector4(at.x - 1.6f, at.z - 1.6f, at.x + 1.6f, at.z + 1.6f), null)) return false;
            var go = Place(parent, rng.NextDouble() < 0.6 ? Dumpster : (rng.NextDouble() < 0.5 ? PalletOne : BarrelSet), at, yaw + (float)rng.NextDouble() * 20f - 10f);
            return go != null;
        }

        // ==================================================================
        // Parked cars
        // ==================================================================
        /// <summary>
        /// Cars along the kerbs, nose to tail with gaps, parked on both sides of the straights and never in a junction.
        /// A car is a body and a cabin: simple, dark and a metre and a half high, which is exactly cover. The roof of
        /// every one is kept off the bake.
        /// </summary>
        private static void BuildCityCars(Transform root, int layer, System.Random rng)
        {
            EnsureStreetMaterials();
            var group = new GameObject("ParkedCars").transform;
            group.SetParent(root, false);

            var body = new MeshBuild { UVScale = 1f };
            body.Box(new Vector3(0f, 0.62f, 0f), new Vector3(4.4f, 0.78f, 1.85f), Quaternion.identity);
            body.Box(new Vector3(-0.2f, 1.28f, 0f), new Vector3(2.35f, 0.62f, 1.62f), Quaternion.identity);
            body.Box(new Vector3(0f, 0.28f, 0f), new Vector3(4.2f, 0.2f, 1.9f), Quaternion.identity);
            var glass = new MeshBuild { UVScale = 1f };
            glass.Box(new Vector3(-0.2f, 1.30f, 0f), new Vector3(2.38f, 0.5f, 1.66f), Quaternion.identity);
            var wheels = new MeshBuild { UVScale = 1f };
            foreach (var sx in new[] { -1.4f, 1.4f })
                foreach (var sz in new[] { -0.9f, 0.9f })
                    wheels.Tube(new Vector3(sx, 0.34f, sz - 0.1f * Mathf.Sign(sz)), new Vector3(sx, 0.34f, sz + 0.1f * Mathf.Sign(sz)), 0.34f, 0.34f, 10);
            var bodyMesh = ToMesh(body, "citycarbody");
            var glassMesh = ToMesh(glass, "citycarglass");
            var wheelMesh = ToMesh(wheels, "citycarwheels");

            int made = 0;
            var tiles = new List<Vector2Int>(_roadTiles);
            tiles.Sort((p, q) => (p.x * 7919 + p.y).CompareTo(q.x * 7919 + q.y));

            foreach (var tile in tiles)
            {
                if (made >= _theme.cityParkedCars) break;
                bool n = _roadTiles.Contains(tile + Vector2Int.up), s = _roadTiles.Contains(tile + Vector2Int.down);
                bool e = _roadTiles.Contains(tile + Vector2Int.right), w = _roadTiles.Contains(tile + Vector2Int.left);
                bool alongZ = n && s && !e && !w, alongX = e && w && !n && !s;
                if (!alongZ && !alongX) continue;
                if (Mathf.Abs(tile.x) * Tile > 190f || Mathf.Abs(tile.y) * Tile > 190f) { }

                float cx = tile.x * Tile, cz = tile.y * Tile;
                foreach (float side in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() < 0.45) continue;
                    float slot = Rand(rng, -5.5f, 5.5f);
                    // The spawn crossing stays clear.
                    var at = alongZ ? new Vector3(cx + side * 3.5f, 0f, cz + slot) : new Vector3(cx + slot, 0f, cz + side * 3.5f);
                    if (at.magnitude < 34f) continue;
                    if (NearCrossing(at, 3.2f)) continue;
                    float yaw = (alongZ ? 90f : 0f) + (rng.NextDouble() < 0.5 ? 0f : 180f) + (float)(rng.NextDouble() - 0.5) * 4f;

                    int p = rng.Next(0, _cityPaint.Length);
                    var car = new GameObject($"Car_{made}").transform;
                    car.SetParent(group, false);
                    car.localPosition = at;
                    car.localRotation = Quaternion.Euler(0f, yaw, 0f);

                    var b = MeshObject(car, "Body", bodyMesh, _cityPaint[p], Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");
                    NoStanding(b);
                    Mark(b, new Color(0.30f, 0.30f, 0.33f), 3);
                    MeshObject(car, "Glass", glassMesh, _cityDark, Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false);
                    MeshObject(car, "Wheels", wheelMesh, _cityDark, Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false);
                    made++;
                    if (made >= _theme.cityParkedCars) break;
                }
            }
        }

        // ==================================================================
        // Signs: awnings, shop windows, neon blades, roof billboards
        // ==================================================================
        private static void BuildCitySigns(Transform root, int layer, System.Random rng)
        {
            EnsureStreetMaterials();
            var group = new GameObject("Signs").transform;
            group.SetParent(root, false);
            int backdrop = LayerMask.NameToLayer("Backdrop");
            if (backdrop < 0) backdrop = layer;

            var awnings = new MeshBuild[_cityAwning.Length];
            for (int i = 0; i < awnings.Length; i++) awnings[i] = new MeshBuild { UVScale = 1f };
            var shops = new MeshBuild { UVScale = 1f };
            var neon = new MeshBuild[_cityNeon.Length];
            for (int i = 0; i < neon.Length; i++) neon[i] = new MeshBuild { UVScale = 1f };
            var frames = new MeshBuild { UVScale = 0.5f };

            foreach (var lot in _lots)
            {
                for (int side = 0; side < 4; side++)
                {
                    // Only faces that look onto a street: a road cell within a few metres of the face.
                    Vector3 mid = FacePoint(lot, side, side == 0 || side == 3 ? (side == 0 ? lot.Centre.x : lot.Centre.z) : (side == 1 ? -lot.Centre.x : -lot.Centre.z));
                    Vector3 probe = mid + FaceOut(side) * 7f;
                    if (!OnRoadCell(probe.x, probe.z)) continue;

                    float ulo, uhi;
                    switch (side)
                    {
                        case 0: ulo = lot.x0; uhi = lot.x1; break;
                        case 1: ulo = -lot.x1; uhi = -lot.x0; break;
                        case 2: ulo = -lot.z1; uhi = -lot.z0; break;
                        default: ulo = lot.z0; uhi = lot.z1; break;
                    }

                    Vector3 u = FaceU(side), o = FaceOut(side);
                    var rot = Quaternion.LookRotation(o, Vector3.up);

                    // Shop windows and awnings: every ~16 m along the face at ground level.
                    for (float p = ulo + 7f; p < uhi - 7f; p += Rand(rng, 12f, 20f))
                    {
                        Vector3 at = FacePoint(lot, side, p);
                        var foot = new Vector4(Mathf.Min(at.x, at.x + o.x * 1.8f) - 3f, Mathf.Min(at.z, at.z + o.z * 1.8f) - 3f,
                                               Mathf.Max(at.x, at.x + o.x * 1.8f) + 3f, Mathf.Max(at.z, at.z + o.z * 1.8f) + 3f);
                        if (Blocked(foot, lot)) continue;

                        // The window: a lit panel proud of the wall, with a dark frame.
                        // Three panes with mullions, so a shopfront is not one cream slab.
                        for (int pane = -1; pane <= 1; pane++)
                            shops.Box(at + Vector3.up * 1.9f + o * 0.06f + rot * new Vector3(pane * 1.72f, 0f, 0f), new Vector3(1.55f, 1.7f, 0.1f), rot);
                        frames.Box(at + Vector3.up * 1.9f + o * 0.03f, new Vector3(5.3f, 2.0f, 0.06f), rot);
                        // The awning, canted out and down.
                        int ai = rng.Next(0, awnings.Length);
                        awnings[ai].Box(at + Vector3.up * 3.45f + o * 0.85f, new Vector3(5.6f, 0.1f, 1.7f), rot * Quaternion.Euler(14f, 0f, 0f));
                    }

                    // Neon blades: vertical signs standing out from the face, 5 to 9 m up, a few per face.
                    int blades = rng.Next(1, 4);
                    for (int i = 0; i < blades; i++)
                    {
                        float p = Rand(rng, ulo + 6f, uhi - 6f);
                        float yy = Rand(rng, 5.5f, Mathf.Min(10f, lot.Height - 3f));
                        Vector3 at = FacePoint(lot, side, p) + Vector3.up * yy;
                        var foot = new Vector4(Mathf.Min(at.x, at.x + o.x * 2f) - 2f, Mathf.Min(at.z, at.z + o.z * 2f) - 2f,
                                               Mathf.Max(at.x, at.x + o.x * 2f) + 2f, Mathf.Max(at.z, at.z + o.z * 2f) + 2f);
                        if (Blocked(foot, lot)) continue;

                        int c = rng.Next(0, neon.Length);
                        float len = Rand(rng, 2.4f, 4.6f);
                        frames.Box(at + o * 0.7f, new Vector3(0.12f, len + 0.3f, 1.3f), rot);
                        neon[c].Box(at + o * 0.7f, new Vector3(0.18f, len, 1.1f), rot);
                        frames.Box(at + o * 0.3f, new Vector3(0.06f, 0.06f, 0.7f), rot);
                    }
                }
            }

            for (int i = 0; i < awnings.Length; i++)
            {
                if (awnings[i].Triangles.Count == 0) continue;
                var go = MeshObject(group, $"Awnings_{i}", ToMesh(awnings[i], $"cityawn{i}"), _cityAwning[i], Vector3.zero, Quaternion.identity, Vector3.one, backdrop, null, collider: false);
                NoStanding(go);
            }
            var shopGo = MeshObject(group, "ShopWindows", ToMesh(shops, "cityshops"), _cityShop, Vector3.zero, Quaternion.identity, Vector3.one, backdrop, null, collider: false);
            NoStanding(shopGo);
            var frameGo = MeshObject(group, "SignFrames", ToMesh(frames, "cityframes"), _cityDark, Vector3.zero, Quaternion.identity, Vector3.one, backdrop, null, collider: false);
            NoStanding(frameGo);
            for (int i = 0; i < neon.Length; i++)
            {
                if (neon[i].Triangles.Count == 0) continue;
                var go = MeshObject(group, $"Neon_{i}", ToMesh(neon[i], $"cityneon{i}"), _cityNeon[i], Vector3.zero, Quaternion.identity, Vector3.one, backdrop, null, collider: false);
                NoStanding(go);
            }

            BuildRoofBillboards(group, layer, rng);
        }

        /// <summary>A few big lit panels on the street edge of walkable roofs: the glow that lights the roof, and cover on it.</summary>
        private static void BuildRoofBillboards(Transform group, int layer, System.Random rng)
        {
            var panels = new MeshBuild[_cityNeon.Length];
            for (int i = 0; i < panels.Length; i++) panels[i] = new MeshBuild { UVScale = 1f };
            var legs = new MeshBuild { UVScale = 0.5f };
            int made = 0;

            foreach (var lot in _lots)
            {
                if (!lot.walkable || made >= 9 || rng.NextDouble() < 0.55) continue;
                int side = rng.Next(0, 4);
                float ulo, uhi;
                switch (side)
                {
                    case 0: ulo = lot.x0; uhi = lot.x1; break;
                    case 1: ulo = -lot.x1; uhi = -lot.x0; break;
                    case 2: ulo = -lot.z1; uhi = -lot.z0; break;
                    default: ulo = lot.z0; uhi = lot.z1; break;
                }
                float p = Rand(rng, ulo + 12f, uhi - 12f);
                float worldAlong = EscapeU(side, p);

                // Not near a stair or bridge opening on that face.
                bool nearGap = false;
                foreach (var g in lot.gaps) if (g.side == side && Mathf.Abs(g.at - worldAlong) < 12f) nearGap = true;
                if (nearGap) continue;

                Vector3 at = FacePoint(lot, side, p) + FaceOut(side) * -2.2f + Vector3.up * lot.Height;
                var rot = Quaternion.LookRotation(FaceOut(side), Vector3.up);
                panels[rng.Next(0, panels.Length)].Box(at + Vector3.up * 4.2f, new Vector3(12f, 5f, 0.3f), rot);
                legs.Box(at + Vector3.up * 4.2f - FaceOut(side) * 0.25f, new Vector3(12.4f, 5.4f, 0.2f), rot);
                for (float dx = -5f; dx <= 5f; dx += 5f)
                    legs.Tube(at + rot * new Vector3(dx, 0f, -0.4f), at + rot * new Vector3(dx, 2.0f, -0.4f), 0.12f, 0.12f, 6);
                made++;
            }

            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i].Triangles.Count == 0) continue;
                var go = MeshObject(group, $"Billboards_{i}", ToMesh(panels[i], $"citybb{i}"), _cityNeon[i], Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");
                NoStanding(go);
            }
            if (legs.Triangles.Count > 0)
            {
                var go = MeshObject(group, "BillboardFrames", ToMesh(legs, "citybbframes"), _cityDark, Vector3.zero, Quaternion.identity, Vector3.one, layer, "Metal");
                NoStanding(go);
            }
        }

        // ==================================================================
        // Lights
        // ==================================================================
        /// <summary>
        /// Real lights at the junctions: sodium orange, no shadows, a few of them flickering. Windows, signs and
        /// lamp heads are emissive and cost nothing; these are the lights that actually light the ground and the
        /// player, so the budget is spent on them and nowhere else.
        /// </summary>
        private static void BuildCityLights(Transform root, System.Random rng)
        {
            var group = new GameObject("14_Lighting").transform;
            group.SetParent(root, false);
            int i = 0;

            foreach (var tile in _roadTiles)
            {
                int arms = (_roadTiles.Contains(tile + Vector2Int.up) ? 1 : 0) + (_roadTiles.Contains(tile + Vector2Int.down) ? 1 : 0)
                         + (_roadTiles.Contains(tile + Vector2Int.left) ? 1 : 0) + (_roadTiles.Contains(tile + Vector2Int.right) ? 1 : 0);
                if (arms < 3) continue;
                // Every junction but the ring's corners; every other one on the edge.
                if ((tile.x + tile.y) % 2 != 0 && arms == 3) continue;

                var go = new GameObject($"JunctionLight_{i}");
                go.transform.SetParent(group, false);
                go.transform.position = new Vector3(tile.x * Tile + 4f, 8.6f, tile.y * Tile + 4f);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1.0f, 0.72f, 0.42f);
                l.intensity = 9f;
                l.range = 34f;
                l.shadows = LightShadows.None;

                if (i % 4 == 1)
                {
                    var f = go.AddComponent<FlickerLight>();
                    f.mode = FlickerLight.Mode.Flicker; f.range = new Vector2(0.75f, 1.05f); f.speed = 3f; f.target = l;
                }
                i++;
            }
        }

        // ==================================================================
        // The far city
        // ==================================================================
        /// <summary>
        /// Towers beyond the fence, in the same lit facade: a few hundred metres of city before the sky. No
        /// colliders, off the bake and the minimap. A district that stops at its fence is a diorama; this is what
        /// makes it the middle of a larger place.
        /// </summary>
        private static void BuildCitySkyline(Transform root, int backdrop, System.Random rng, float half)
        {
            var group = new GameObject("Skyline").transform;
            group.SetParent(root, false);

            int count = Mathf.Max(20, _theme.backdropCount);
            var builds = new MeshBuild[_cityFacade.Length];
            for (int i = 0; i < builds.Length; i++) builds[i] = new MeshBuild { UVScale = 1f / (FacadeCells * FloorH) };
            var beacons = new MeshBuild { UVScale = 1f };

            for (int i = 0; i < count; i++)
            {
                float angle = i / (float)count * Mathf.PI * 2f + Rand(rng, -0.04f, 0.04f);
                float dist = Rand(rng, half + 90f, half + 520f);
                var at = new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);

                float h = Snap(Rand(rng, 36f, 230f));
                float w = Snap(Rand(rng, 26f, 70f)), d = Snap(Rand(rng, 26f, 60f));
                int f = rng.Next(0, builds.Length);
                if (h > 100f) f = 3 + rng.Next(0, 3);

                builds[f].Box(at + new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d), Quaternion.identity);
                if (h > 120f) beacons.Box(at + new Vector3(0f, h + 0.6f, 0f), new Vector3(1f, 1f, 1f), Quaternion.identity);
            }

            for (int i = 0; i < builds.Length; i++)
            {
                if (builds[i].Triangles.Count == 0) continue;
                var go = MeshObject(group, $"Towers_{i}", ToMesh(builds[i], $"cityskyline{i}"), _cityFacade[i], Vector3.zero, Quaternion.identity,
                                    Vector3.one, backdrop, null, collider: false);
                NoStanding(go); Hide(go);
            }
            if (beacons.Triangles.Count > 0)
            {
                var go = MeshObject(group, "Beacons", ToMesh(beacons, "cityskylinebeacons"), _cityNeon[4], Vector3.zero, Quaternion.identity, Vector3.one, backdrop, null, collider: false);
                NoStanding(go); Hide(go);
            }
        }
    }
}
#endif
