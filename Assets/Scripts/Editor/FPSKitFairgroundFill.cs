#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// What fills the ground between the districts: shops along the avenues, hedges and
    /// topiary lining them, and three smaller attractions -- a mini-golf course, a go-kart
    /// track and a log flume.
    ///
    /// <b>Fill is placed by plan, not by chance.</b> Every shop stands where an avenue's edge
    /// meets it, with its door on the walkway, and every shop has more than one way in and out.
    /// A shop with one door is a pocket of baked floor that nothing can enter: the stranded-patch
    /// failure that has cost this project more than once. The mini-golf, the kart track and the
    /// flume each own a flat pad reserved before the ground was made.
    ///
    /// <b>Cover at every scale.</b> The hedges are knee- to hip-high and run beside the avenues;
    /// the shops are rooms; the golf course is a field of low walls; the kart track is a loop of
    /// tyre barriers; the flume is a channel on legs. A fight in the open has something to use
    /// at each distance.
    /// </summary>
    public partial class FPSKitSceneBuilder
    {
        private struct ShopPlan
        {
            public Vector2 Centre;
            public float Yaw, Width, Depth;
            public int Kind;
        }

        private static readonly List<ShopPlan> _shops = new List<ShopPlan>();
        
        private static Material _screenMat, _mirrorMat, _greenMat, _turfMat;

        private static void ResolveFillMaterials()
        {
            _screenMat = MakeDetailMaterial("ArcadeScreen", new Color(0.05f, 0.18f, 0.2f), "Concrete", Metres(2f), 0.5f, 0f, 0.1f);
            SetEmission(_screenMat, new Color(0.2f, 0.9f, 0.85f) * 1.4f);
            _mirrorMat = MakeDetailMaterial("Mirror", new Color(0.30f, 0.36f, 0.38f), "Metal", Metres(3f), 0.92f, 0.6f, 0.05f);
            _greenMat = MakeDetailMaterial("GolfGreen", new Color(0.18f, 0.40f, 0.16f), "Adobe", Metres(2.5f), 0.04f, 0f, 0.3f);
            _turfMat = MakeDetailMaterial("GolfSand", new Color(0.55f, 0.50f, 0.36f), "Sand", Metres(3f), 0.04f, 0f, 0.4f);
        }

        // ==================================================================
        // Planning
        // ==================================================================
        /// <summary>Reserves the shops, and registers each one's door as an entrance to be checked against the walkways.</summary>
        private static void PlanFill()
        {
            _shops.Clear();

            void Shop(float x, float z, float yaw, float w, float d, int kind)
            {
                PlanSite(x, z, Mathf.Max(w, d) * 0.62f, 3f, yaw);
                _shops.Add(new ShopPlan { Centre = new Vector2(x, z), Yaw = yaw, Width = w, Depth = d, Kind = kind });

                var door = new Vector2(x, z) + new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad)) * (d * 0.5f + 1.2f);
                Entrance($"shop {_shops.Count}", door.x, door.y);
            }

            // Main avenue, north: two on the east side, doors on the walkway. The west side is the rose garden.
            Shop(10.5f, 37.5f, 270f, 8f, 6.5f, 0);
            Shop(10.5f, 47f, 270f, 8f, 6.5f, 1);

            // Cross avenue, east: arcade and mirrors north of it, a cafe and a souvenir stall south.
            Shop(44f, 9.8f, 180f, 10f, 7f, 4);
            Shop(44f, -9.8f, 0f, 10f, 7f, 6);
            Shop(60f, -9.8f, 0f, 9f, 6.5f, 0);
            Shop(118f, -9.8f, 0f, 10f, 6.5f, 1);
            Shop(118f, 9.8f, 180f, 10f, 7f, 5);

            // Cross avenue, west.
            Shop(-80f, -9.8f, 0f, 10f, 6.5f, 2);
            Shop(-100f, -9.8f, 0f, 10f, 6.5f, 6);
            Shop(-84f, 9.8f, 180f, 10f, 6.5f, 5);
            Shop(-102f, 9.8f, 180f, 11f, 7f, 4);
        }

        // ==================================================================
        // Building the fill
        // ==================================================================
        private static void BuildFill(Transform root, int layer, int backdrop, System.Random rng)
        {
            ResolveFillMaterials();
            ResolveAttractionMaterials();

            var group = new GameObject("Fill").transform;
            group.SetParent(root, false);

            foreach (var shop in _shops) BuildShop(group, layer, shop, rng);

            BuildKartTrack(group, layer, rng, _kartPlan);
            BuildAvenuePlanting(group, layer, backdrop, rng);
        }

        // ------------------------------------------------------------------
        // Shops
        // ------------------------------------------------------------------
        /// <summary>
        /// A shop: four walls with a wide door in the front, a back door and a side door, a
        /// sloping roof that is mostly gone, an awning over the walkway, a sign, and what the
        /// shop sold. Local +z is the front, facing the avenue.
        /// </summary>
        private static void BuildShop(Transform parent, int layer, ShopPlan plan, System.Random rng)
        {
            var at = new Vector3(plan.Centre.x, GroundHeightAt(plan.Centre.x, plan.Centre.y), plan.Centre.y);
            var shop = new GameObject($"Shop{plan.Kind}").transform;
            shop.SetParent(parent, false);
            shop.position = at;
            shop.localRotation = Quaternion.Euler(0f, plan.Yaw, 0f);

            float W = plan.Width * 0.5f, D = plan.Depth * 0.5f, H = 3.9f, T = 0.3f;
            var paint = _theme.RandomCoverColor(rng);
            // Painted once and faded since: the cover colour pulled towards plaster, never as dark as the paint was.
            var plaster = Color.Lerp(paint, new Color(0.62f, 0.56f, 0.46f), 0.55f);
            var wallMat = MakeDetailMaterial($"ShopWall{plan.Kind}", plaster, "Adobe", Metres(4f), 0.05f, 0f, 0.8f);

            void Wall(string name, bool alongX, float fixedCoord, float from, float to, params (float c, float w)[] openings)
            {
                var list = new List<(float c, float w)>(openings);
                list.Sort((a, b) => a.c.CompareTo(b.c));
                float cursor = from;

                void Solid(float a, float b, float y0, float y1)
                {
                    if (b - a < 0.05f) return;
                    float mid = (a + b) * 0.5f;
                    var pos = alongX ? new Vector3(mid, (y0 + y1) * 0.5f, fixedCoord) : new Vector3(fixedCoord, (y0 + y1) * 0.5f, mid);
                    var size = alongX ? new Vector3(b - a, y1 - y0, T) : new Vector3(T, y1 - y0, b - a);
                    NoStanding(CreateBlock(shop, pos, size, 0f, layer, "Wood", Color.grey, 0.05f, 0f, name, wallMat));
                }

                foreach (var o in list)
                {
                    Solid(cursor, o.c - o.w * 0.5f, 0f, H);
                    Solid(o.c - o.w * 0.5f, o.c + o.w * 0.5f, 2.9f, H);
                    cursor = o.c + o.w * 0.5f;
                }

                Solid(cursor, to, 0f, H);
            }

            Wall("ShopFront", true, D, -W, W, (-plan.Width * 0.24f, 2.8f), (plan.Width * 0.24f, 2.8f));
            Wall("ShopBack", true, -D, -W, W, (-plan.Width * 0.25f, 2.6f), (plan.Width * 0.3f, 2.6f));
            Wall("ShopLeft", false, -W, -D, D, (0f, 2.6f));
            Wall("ShopRight", false, W, -D, D, (0.5f, 2.6f));

            var wood = new MeshBuild { UVScale = 0.5f };
            var steel = new MeshBuild { UVScale = 0.5f };
            var roofB = new MeshBuild { UVScale = 0.4f };
            var cloth = new MeshBuild { UVScale = 0.4f };
            var glow = new MeshBuild { UVScale = 0.4f };
            var mirror = new MeshBuild { UVScale = 0.4f };
            var bone = new MeshBuild { UVScale = 0.4f };

            // Roof: two slopes of corrugated sheet, most of it gone, rafters left.
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 6; i++)
                {
                    float x0 = Mathf.Lerp(-W - 0.4f, W + 0.4f, i / 6f), x1 = Mathf.Lerp(-W - 0.4f, W + 0.4f, (i + 1f) / 6f);
                    steel.Tube(new Vector3(x0, H, side * (D + 0.3f)), new Vector3(x0, H + 1.3f, 0f), 0.06f, 0.05f, 3);

                    if (rng.NextDouble() < 0.5) continue;
                    roofB.Quad(new Vector3(x0, H, side * (D + 0.3f)), new Vector3(x0, H + 1.3f, 0f), new Vector3(x1, H + 1.3f, 0f), new Vector3(x1, H, side * (D + 0.3f)));
                    roofB.Quad(new Vector3(x1, H, side * (D + 0.3f)), new Vector3(x1, H + 1.3f, 0f), new Vector3(x0, H + 1.3f, 0f), new Vector3(x0, H, side * (D + 0.3f)));
                }

            // The awning out over the walkway, and the sign over the door.
            int strips = Mathf.RoundToInt(plan.Width / 0.9f);
            for (int st = 0; st < strips; st++)
            {
                if (rng.NextDouble() < 0.12) continue;
                float x0 = -W + st * plan.Width / strips, x1 = x0 + plan.Width / strips;
                var set = st % 2 == 0 ? cloth : roofB;
                set.Quad(new Vector3(x0, 3.4f, D + 0.1f), new Vector3(x0, 2.7f, D + 1.8f), new Vector3(x1, 2.7f, D + 1.8f), new Vector3(x1, 3.4f, D + 0.1f));
                set.Quad(new Vector3(x1, 3.4f, D + 0.1f), new Vector3(x1, 2.7f, D + 1.8f), new Vector3(x0, 2.7f, D + 1.8f), new Vector3(x0, 3.4f, D + 0.1f));
            }

            steel.Tube(new Vector3(-W + 0.3f, 0f, D + 1.8f), new Vector3(-W + 0.3f, 2.7f, D + 1.8f), 0.06f, 0.05f, 4);
            steel.Tube(new Vector3(W - 0.3f, 0f, D + 1.8f), new Vector3(W - 0.3f, 2.7f, D + 1.8f), 0.06f, 0.05f, 4);
            wood.Box(new Vector3(0f, H + 0.55f, D + 0.05f), new Vector3(W * 1.5f, 0.9f, 0.14f), Quaternion.identity);
            for (int i = 0; i < 8; i++)
                if (rng.NextDouble() > 0.2) bone.Box(new Vector3(-W * 0.65f + i * W * 1.3f / 7f, H + 0.55f, D + 0.16f), new Vector3(0.3f, Rand(rng, 0.4f, 0.6f), 0.06f), Quaternion.identity);

            // What the shop sold.
            switch (plan.Kind)
            {
                case 0:   // souvenirs: a counter, wall racks, a spinner of postcards
                    wood.Box(new Vector3(-W * 0.4f, 0.55f, 0.6f), new Vector3(plan.Width * 0.45f, 1.1f, 0.7f), Quaternion.identity);
                    for (int r = 0; r < 3; r++)
                        wood.Box(new Vector3(0f, 1.0f + r * 0.6f, -D + 0.45f), new Vector3(plan.Width * 0.8f, 0.05f, 0.5f), Quaternion.identity);
                    steel.Tube(new Vector3(W * 0.4f, 0f, -0.8f), new Vector3(W * 0.4f, 1.7f, -0.8f), 0.04f, 0.04f, 4);
                    steel.Tube(new Vector3(W * 0.4f, 1.3f, -0.8f), new Vector3(W * 0.4f + 0.5f, 1.3f, -0.8f), 0.02f, 0.02f, 3);
                    break;

                case 1:   // restrooms: partitions down the back, basins on the wall
                    for (int p = -2; p <= 2; p++)
                        wood.Box(new Vector3(p * W * 0.38f, 1.0f, -D + 1.3f), new Vector3(0.05f, 2.0f, 2.2f), Quaternion.identity);
                    for (int p = -2; p <= 2; p++)
                        steel.Box(new Vector3(p * W * 0.38f + W * 0.19f, 0.45f, -D + 0.55f), new Vector3(0.5f, 0.9f, 0.45f), Quaternion.identity);
                    break;

                case 2:   // first aid: two cots and a cabinet
                    for (int c = -1; c <= 1; c += 2)
                        wood.Box(new Vector3(c * W * 0.45f, 0.45f, -D + 1.4f), new Vector3(0.8f, 0.5f, 1.9f), Quaternion.identity);
                    steel.Box(new Vector3(0f, 1.0f, -D + 0.3f), new Vector3(1.0f, 2.0f, 0.4f), Quaternion.identity);
                    bone.Box(new Vector3(0f, 2.3f, -D + 0.52f), new Vector3(0.5f, 0.5f, 0.05f), Quaternion.identity);
                    break;

                case 3:   // information: a long desk and a notice board of faded maps
                    wood.Box(new Vector3(0f, 0.55f, 0.4f), new Vector3(plan.Width * 0.6f, 1.1f, 0.6f), Quaternion.identity);
                    wood.Box(new Vector3(0f, 1.9f, -D + 0.2f), new Vector3(plan.Width * 0.6f, 1.6f, 0.08f), Quaternion.identity);
                    break;

                case 4:   // arcade: rows of cabinets, screens still faintly alight
                    for (int row = 0; row < 2; row++)
                        for (int i = 0; i < 5; i++)
                        {
                            if (rng.NextDouble() < 0.2) continue;
                            float x = -W + 1.2f + i * (plan.Width - 2.4f) / 4f, z = -D + 0.9f + row * 2.3f;
                            wood.Box(new Vector3(x, 0.95f, z), new Vector3(0.8f, 1.9f, 0.7f), Quaternion.identity);
                            glow.Box(new Vector3(x, 1.35f, z + 0.36f), new Vector3(0.6f, 0.45f, 0.05f), Quaternion.Euler(-12f, 0f, 0f));
                        }
                    break;

                case 5:   // hall of mirrors: panels of glass stood in a zigzag
                    for (int i = 0; i < 6; i++)
                    {
                        float x = -W + 1.2f + i * (plan.Width - 2.4f) / 5f;
                        mirror.Box(new Vector3(x, 1.2f, -0.2f + (i % 2) * 1.4f), new Vector3(1.8f, 2.4f, 0.06f), Quaternion.Euler(0f, (i % 2 == 0 ? 25f : -25f), 0f));
                    }
                    break;

                default:  // cafe: a counter, stools, tables
                    wood.Box(new Vector3(0f, 0.55f, -D + 0.9f), new Vector3(plan.Width * 0.7f, 1.1f, 0.7f), Quaternion.identity);
                    for (int i = 0; i < 4; i++)
                        steel.Tube(new Vector3(-W * 0.6f + i * W * 0.4f, 0f, -D + 1.7f), new Vector3(-W * 0.6f + i * W * 0.4f, 0.7f, -D + 1.7f), 0.2f, 0.18f, 6);
                    HallTable(wood, steel, new Vector3(-W * 0.4f, 0f, 0.5f), rng);
                    HallTable(wood, steel, new Vector3(W * 0.4f, 0f, 0.8f), rng);
                    break;
            }

            RideSolid(shop, layer, "ShopWood", wood, _parkTimber, "Wood");
            RideSolid(shop, layer, "ShopSteel", steel, _parkSteel);
            RideDecor(shop, "ShopRoof", roofB, _parkSteel);
            RideDecor(shop, "ShopAwning", cloth, _rideCanvasRed);
            RideDecor(shop, "ShopGlow", glow, _screenMat, false);
            RideSolid(shop, layer, "ShopMirrors", mirror, _mirrorMat, "Metal");
            RideDecor(shop, "ShopSign", bone, _hhBone);

            var light = new GameObject("ShopLight");
            light.transform.SetParent(shop, false);
            light.transform.localPosition = new Vector3(0f, 2.8f, 0f);
            var l = light.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = plan.Kind == 4 ? new Color(0.4f, 0.95f, 0.9f) : new Color(1f, 0.74f, 0.45f);
            l.intensity = 3.2f;
            l.range = 13f;
            l.shadows = LightShadows.None;
        }

        // ------------------------------------------------------------------
        // Go-karts
        // ------------------------------------------------------------------
        /// <summary>
        /// A kart track: a ring of tyre barriers round an oval, with three gaps, a pit shed, and
        /// karts left on the line where the last race stopped.
        /// </summary>
        private static void BuildKartTrack(Transform parent, int layer, System.Random rng, ParkSite site)
        {
            var at = new Vector3(site.Centre.x, GroundHeightAt(site.Centre.x, site.Centre.y), site.Centre.y);
            var track = new GameObject("KartTrack").transform;
            track.SetParent(parent, false);
            track.position = at;

            var tyres = new MeshBuild { UVScale = 0.5f };
            var karts = new MeshBuild { UVScale = 0.5f };
            var steel = new MeshBuild { UVScale = 0.5f };
            var tarmac = new MeshBuild { UVScale = 0.2f };

            // Two ovals, the outer 36 x 24 and the inner 17 x 7. The outer has two gaps, a long side apart:
            // drawn as two rings with a gap each, the second filled the first's, and the infield was sealed.
            void Oval(float rx, float rz, int count, int skipFromA, int skipToA, int skipFromB, int skipToB)
            {
                for (int i = 0; i < count; i++)
                {
                    if (i >= skipFromA && i < skipToA) continue;
                    if (i >= skipFromB && i < skipToB) continue;

                    float a = i * Mathf.PI * 2f / count;
                    var p = new Vector3(Mathf.Cos(a) * rx, 0f, Mathf.Sin(a) * rz);
                    int height = rng.NextDouble() < 0.6 ? 2 : 1;
                    for (int h = 0; h < height; h++)
                        tyres.Tube(p + Vector3.up * (h * 0.25f), p + Vector3.up * (h * 0.25f + 0.23f), 0.42f, 0.42f, 9);
                }
            }

            Oval(18f, 12f, 76, 12, 22, 50, 60);
            Oval(8.5f, 3.6f, 38, 0, 5, 18, 24);

            // The surface of the track: a dark band between the ovals, laid over the grass.
            const int Segs = 36;
            for (int i = 0; i < Segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / Segs, a1 = (i + 1) * Mathf.PI * 2f / Segs;
                Vector3 Pt(float a, float rx, float rz) => new Vector3(Mathf.Cos(a) * rx, 0.05f, Mathf.Sin(a) * rz);
                tarmac.Quad(Pt(a0, 9.6f, 4.6f), Pt(a0, 17.4f, 11.4f), Pt(a1, 17.4f, 11.4f), Pt(a1, 9.6f, 4.6f));
            }

            // Karts, left where they stopped.
            for (int i = 0; i < 7; i++)
            {
                float a = Rand(rng, 0f, 6.283f);
                var p = new Vector3(Mathf.Cos(a) * 13.5f, 0.25f, Mathf.Sin(a) * 8f);
                var rot = Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f + Rand(rng, -30f, 30f), 0f);
                karts.Box(p + Vector3.up * 0.1f, new Vector3(1.0f, 0.3f, 1.8f), rot);
                karts.Box(p + rot * new Vector3(0f, 0.5f, -0.4f), new Vector3(0.7f, 0.5f, 0.5f), rot);
                steel.Tube(p + rot * new Vector3(0f, 0.3f, 0.3f), p + rot * new Vector3(0f, 0.9f, 0.2f), 0.03f, 0.03f, 3);
                for (int s = -1; s <= 1; s += 2)
                    for (int e = -1; e <= 1; e += 2)
                        steel.Tube(p + rot * new Vector3(s * 0.55f, 0f, e * 0.7f), p + rot * new Vector3(s * 0.62f, 0f, e * 0.7f), 0.2f, 0.2f, 8);
            }

            // The pit shed, open-fronted, at the north end.
            var shedAt = new Vector3(0f, 0f, -20f);
            steel.Box(shedAt + new Vector3(0f, 1.6f, 1.5f), new Vector3(10f, 3.2f, 0.2f), Quaternion.identity);
            steel.Box(shedAt + new Vector3(-5f, 1.6f, 0f), new Vector3(0.2f, 3.2f, 3.2f), Quaternion.identity);
            for (int i = -1; i <= 1; i += 2)
                steel.Tube(shedAt + new Vector3(i * 4.6f, 0f, -1.4f), shedAt + new Vector3(i * 4.6f, 3.2f, -1.4f), 0.07f, 0.07f, 4);
            steel.Box(shedAt + new Vector3(0.4f, 3.4f, 0f), new Vector3(10.4f, 0.12f, 3.6f), Quaternion.Euler(-6f, 0f, 0f));

            RideSolid(track, layer, "KartTyres", tyres, _parkDark, "Concrete");
            RideSolid(track, layer, "KartKarts", karts, _rideRed);
            RideSolid(track, layer, "KartSteel", steel, _parkSteel);
            RideDecor(track, "KartTarmac", tarmac, _fgAsphalt, false);
        }

        // ------------------------------------------------------------------
        // Hedges and topiary along the avenues
        // ------------------------------------------------------------------
        private static bool NearOtherRoute(Vector2 p, int self, float margin)
        {
            for (int d = 0; d < _fgRoutes.Count; d++)
            {
                if (d == self) continue;
                var route = _fgRoutes[d];
                for (int i = 0; i < route.Points.Count - 1; i++)
                    if (DistanceToSegment(p, route.Points[i], route.Points[i + 1]) < route.Width * 0.5f + margin) return true;
            }

            return false;
        }

        /// <summary>
        /// Low hedge along each side of the four avenues, broken wherever something meets them, with a
        /// clipped topiary ball every few runs. Knee- to hip-high: cover, not a wall.
        /// </summary>
        private static void BuildAvenuePlanting(Transform parent, int layer, int backdrop, System.Random rng)
        {
            var hedge = new MeshBuild { UVScale = 0.4f };
            var ball = new MeshBuild { UVScale = 0.4f };
            var pots = new MeshBuild { UVScale = 0.5f };
            int segments = 0;

            for (int d = 0; d < 4 && d < _fgRoutes.Count; d++)
            {
                var route = _fgRoutes[d];
                float walked = 0f, nextAt = 3f;
                int index = 0;

                for (int i = 0; i < route.Points.Count - 1; i++)
                {
                    var a = route.Points[i]; var b = route.Points[i + 1];
                    float length = Vector2.Distance(a, b);
                    if (length < 0.01f) continue;
                    var along = (b - a) / length;
                    var across = new Vector2(-along.y, along.x);

                    while (walked + length > nextAt)
                    {
                        var centre = a + along * (nextAt - walked);
                        nextAt += 3.6f;
                        index++;

                        for (int side = -1; side <= 1; side += 2)
                        {
                            var p = centre + across * side * (route.Width * 0.5f + 1.5f);
                            if (rng.NextDouble() < 0.18 || InsidePlaza(p, -2f) || NearOtherRoute(p, d, 2.2f)) continue;
                            if (!KeepClear(p, 0.5f) || !OffSinks(p, 1.5f)) continue;

                            float g = GroundHeightAt(p.x, p.y);
                            var rot = Quaternion.LookRotation(new Vector3(along.x, 0f, along.y));

                            if (index % 5 == 0)
                            {
                                pots.Box(new Vector3(p.x, g + 0.35f, p.y), new Vector3(0.9f, 0.7f, 0.9f), rot);
                                LeafBlob(ball, new Vector3(p.x, g + 1.5f, p.y), 0.85f, rng.Next(1, 99999));
                                continue;
                            }

                            float h = Rand(rng, 0.9f, 1.3f);
                            hedge.Box(new Vector3(p.x, g + h * 0.5f, p.y), new Vector3(0.8f, h, 3.5f), rot);
                            LeafBlob(hedge, new Vector3(p.x, g + h + 0.1f, p.y), Rand(rng, 0.45f, 0.65f), rng.Next(1, 99999));
                            segments++;
                        }
                    }

                    walked += length;
                }
            }

            var go = MeshObject(parent, "AvenueHedges", ToMesh(hedge, DenseKey("avenuehedges")), _hedgeMat, Vector3.zero, Quaternion.identity,
                                Vector3.one, layer, "Wood");
            NoStanding(go);
            Hide(go);
            RideSolid(parent, layer, "AvenuePots", pots, _parkRubble, "Concrete");
            RideSolid(parent, layer, "AvenueTopiary", ball, _hedgeMat, "Wood");

            Debug.Log($"[FPSKit] fairground avenues: {segments} hedge run(s).");
        }
    }
}
#endif
