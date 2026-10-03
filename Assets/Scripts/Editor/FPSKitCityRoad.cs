#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The rooftop district's road furniture, the parts a pedestrian and a driver would both read: paved
    /// footways with kerbs, zebra crossings with stop lines and tactile paving, mid-block crossings with
    /// beacons, traffic signals at every junction with a pedestrian signal on each side of each crossing, and
    /// the pools of light under the street lamps. All of it is paint or hand-sized detail: none of it has a
    /// collider or touches the navigation bake, so the street plan the reach check proved is unchanged.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static Material _cityPaving, _cityKerb, _cityPool, _tlRed, _tlAmber, _tlGreen, _tlOff, _tlWalk, _tlStop;

        /// <summary>Rectangles (x0, z0, x1, z1) of every crossing laid, so a parked car is never put across one.</summary>
        private static readonly List<Vector4> _cityCrossings = new List<Vector4>();

        private const float SignalPoleOffset = 6.4f;   // from the junction centre, on the footway corner

        // ==================================================================
        // Textures and materials
        // ==================================================================
        private static void WriteRoadTextures()
        {
            // Paving: 256 px across 2.4 m, so four 0.6 m slabs each way, a dark joint between them, a flat grey
            // per slab and a fine grain. Grayscale; the colour stays on the material.
            const int n = 256, slab = 64;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int sx = x / slab, sy = y / slab;
                    int lx = x % slab, ly = y % slab;
                    float tone = 0.60f + (CityHash(sx, sy, 3) - 0.5f) * 0.14f;
                    float grain = (CityHash(x / 2, y / 2, 9) - 0.5f) * 0.09f;
                    bool joint = lx < 2 || ly < 2;
                    float lum = joint ? 0.30f : Mathf.Clamp01(tone + grain);
                    byte b = (byte)Mathf.RoundToInt(Mathf.Pow(lum, 1f / 2.2f) * 255f);
                    px[y * n + x] = new Color32(b, b, b, 255);
                }
            WritePng($"{TextureFolder}/City_Paving.png", px, n, imp => { imp.textureType = TextureImporterType.Default; imp.sRGBTexture = true; });

            // Lamp pool: white, alpha falling off to nothing at the rim (additive, so alpha is the light).
            const int m = 128;
            var pool = new Color32[m * m];
            for (int y = 0; y < m; y++)
                for (int x = 0; x < m; x++)
                {
                    float dx = (x + 0.5f) / m * 2f - 1f, dy = (y + 0.5f) / m * 2f - 1f;
                    float r = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    byte a = (byte)Mathf.RoundToInt(Mathf.Pow(r, 2.0f) * 255f);
                    pool[y * m + x] = new Color32(255, 255, 255, a);
                }
            WritePng($"{TextureFolder}/City_LampPool.png", pool, m, imp =>
            {
                imp.textureType = TextureImporterType.Default;
                imp.sRGBTexture = true;
                imp.alphaIsTransparency = false;
                imp.mipmapEnabled = false;
            });
        }

        private static float CityHash(int x, int y, int s)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + s * 1274126177;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        private static Material Glow(string name, Color colour, float strength)
        {
            var mat = MakeMaterial(name, colour * 0.15f, 0.35f, 0f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", colour * strength);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;   // None lets URP clear the _EMISSION keyword on import
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void EnsureRoadMaterials()
        {
            if (_cityPaving != null && _tlRed != null && _cityPool != null) return;

            var paving = MakeMaterial("CityPaving", new Color(1.05f, 1.05f, 1.08f), 0.10f, 0f);
            var tex = LoadDetail("City_Paving");
            if (tex != null && paving.HasProperty("_BaseMap")) paving.SetTexture("_BaseMap", tex);
            paving.SetColor("_BaseColor", new Color(1.05f, 1.05f, 1.08f));
            Matte(paving, 0.10f);
            EditorUtility.SetDirty(paving);
            _cityPaving = paving;

            _cityKerb = MakeMaterial("CityKerb", new Color(0.46f, 0.46f, 0.47f), 0.08f, 0f);
            Matte(_cityKerb, 0.08f);

            _cityPool = ParticleMaterial("LampPool", "City_LampPool", additive: true, new Color(1.0f, 0.70f, 0.42f) * 0.60f);

            _tlRed = Glow("CitySignalRed", new Color(1.0f, 0.07f, 0.04f), 6f);
            _tlAmber = Glow("CitySignalAmber", new Color(1.0f, 0.55f, 0.04f), 5f);
            _tlGreen = Glow("CitySignalGreen", new Color(0.08f, 1.0f, 0.38f), 5f);
            _tlWalk = Glow("CitySignalWalk", new Color(0.30f, 1.0f, 0.55f), 5f);
            _tlStop = Glow("CitySignalStop", new Color(1.0f, 0.10f, 0.06f), 5f);
            _tlOff = MakeMaterial("CitySignalOff", new Color(0.04f, 0.045f, 0.05f), 0.5f, 0f);
        }

        // ==================================================================
        // Footways and kerbs
        // ==================================================================
        /// <summary>
        /// The ground beside the carriageway, paved: each road tile is twenty metres square with a ten-metre
        /// carriageway, so the other five metres each side are footway. That is the four corner squares and,
        /// on any side that is not more road, the strip along the carriageway. Paving is paint a few
        /// centimetres above the ground (no collider, off the bake), and a kerb runs along the carriageway
        /// edge, so the street reads as a street at eye level and a walker has a clear route.
        /// </summary>
        private static void BuildCityPavement(Transform root, int layer)
        {
            EnsureRoadMaterials();
            var group = new GameObject("Footways").transform;
            group.SetParent(root, false);

            var paving = new MeshBuild { UVScale = 1f / 2.4f };
            var kerbs = new MeshBuild { UVScale = 0.3f };
            const float y = 0.03f, c = CarriageHalf, h = Tile * 0.5f;

            void Pad(float x0, float z0, float x1, float z1)
            {
                float ax = Mathf.Min(x0, x1), bx = Mathf.Max(x0, x1), az = Mathf.Min(z0, z1), bz = Mathf.Max(z0, z1);
                paving.Quad(new Vector3(ax, y, az), new Vector3(ax, y, bz), new Vector3(bx, y, bz), new Vector3(bx, y, az));
            }

            foreach (var tile in _roadTiles)
            {
                float cx = tile.x * Tile, cz = tile.y * Tile;
                bool n = _roadTiles.Contains(tile + Vector2Int.up), s = _roadTiles.Contains(tile + Vector2Int.down);
                bool e = _roadTiles.Contains(tile + Vector2Int.right), w = _roadTiles.Contains(tile + Vector2Int.left);

                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        Pad(cx + sx * c, cz + sz * c, cx + sx * h, cz + sz * h);

                if (!n) Pad(cx - c, cz + c, cx + c, cz + h);
                if (!s) Pad(cx - c, cz - c, cx + c, cz - h);
                if (!e) Pad(cx + c, cz - c, cx + h, cz + c);
                if (!w) Pad(cx - c, cz - c, cx - h, cz + c);

                RoadKerbs(kerbs, new Vector3(cx, 0f, cz), n, s, e, w);
            }

            // Paint, not geometry: on the layer the bake does not look at, or the kerbs bake as ledges.
            int paint = LayerMask.NameToLayer("Backdrop");
            if (paint < 0) paint = layer;
            Flat(group, paint, "Paving", paving, "citypaving", _cityPaving);
            Flat(group, paint, "Kerbs", kerbs, "citykerbs", _cityKerb);
        }

        // ==================================================================
        // Crossings
        // ==================================================================
        /// <summary>True when a straight tile carries a mid-block crossing: not beside a junction, away from the spawn.</summary>
        private static bool MidBlockCrossing(Vector2Int tile)
        {
            bool n = _roadTiles.Contains(tile + Vector2Int.up), s = _roadTiles.Contains(tile + Vector2Int.down);
            bool e = _roadTiles.Contains(tile + Vector2Int.right), w = _roadTiles.Contains(tile + Vector2Int.left);
            bool alongZ = n && s && !e && !w, alongX = e && w && !n && !s;
            if (!alongZ && !alongX) return false;
            if (new Vector2(tile.x, tile.y).magnitude * Tile < 50f) return false;

            var step = alongZ ? Vector2Int.up : Vector2Int.right;
            foreach (var t in new[] { tile + step, tile - step, tile + step * 2, tile - step * 2 })
            {
                if (!_roadTiles.Contains(t)) return false;
                int arms = (_roadTiles.Contains(t + Vector2Int.up) ? 1 : 0) + (_roadTiles.Contains(t + Vector2Int.down) ? 1 : 0)
                         + (_roadTiles.Contains(t + Vector2Int.left) ? 1 : 0) + (_roadTiles.Contains(t + Vector2Int.right) ? 1 : 0);
                if (arms != 2) return false;
            }
            return ((tile.x * 5 + tile.y * 3) & 3) == 0;
        }

        /// <summary>True within <paramref name="margin"/> of any crossing laid.</summary>
        private static bool NearCrossing(Vector3 at, float margin)
        {
            foreach (var r in _cityCrossings)
                if (at.x > r.x - margin && at.x < r.z + margin && at.z > r.y - margin && at.z < r.w + margin) return true;
            return false;
        }

        /// <summary>
        /// One zebra: bars parallel to the traffic, stacked across the carriageway; <paramref name="along"/> is the
        /// direction of traffic, <paramref name="from"/> where the bars start along it.
        /// </summary>
        private static void Zebra(MeshBuild white, MeshBuild yellow, Vector3 centre, Vector3 along, float from, float depth, bool tactile)
        {
            const float y = 0.06f;
            var across = Vector3.Cross(Vector3.up, along);
            Vector3 At(float a, float p) => centre + along * a + across * p + Vector3.up * y;

            for (float k = -CarriageHalf + 0.4f; k < CarriageHalf - 0.45f; k += 1.0f)
                white.Quad(At(from, k), At(from + depth, k), At(from + depth, k + 0.5f), At(from, k + 0.5f));

            if (tactile)
                foreach (float side in new[] { -1f, 1f })
                {
                    float p0 = side * CarriageHalf + (side > 0 ? 0.05f : -1.35f);
                    yellow.Quad(At(from, p0), At(from + depth, p0), At(from + depth, p0 + 1.3f), At(from, p0 + 1.3f));
                }
        }

        private static void AddCrossingRect(Vector3 centre, Vector3 along, float from, float depth)
        {
            var across = Vector3.Cross(Vector3.up, along);
            var a = centre + along * from + across * -CarriageHalf;
            var b = centre + along * (from + depth) + across * CarriageHalf;
            _cityCrossings.Add(new Vector4(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z)));
        }

        private static void StopLine(MeshBuild white, Vector3 centre, Vector3 along, float at, float lanePos)
        {
            // lanePos is the lane's centre across the carriageway; the line spans that lane.
            var across = Vector3.Cross(Vector3.up, along);
            const float y = 0.06f;
            Vector3 P(float a, float p) => centre + along * a + across * p + Vector3.up * y;
            white.Quad(P(at, lanePos - 2.2f), P(at + 0.4f, lanePos - 2.2f), P(at + 0.4f, lanePos + 2.2f), P(at, lanePos + 2.2f));
        }

        // ==================================================================
        // Signals
        // ==================================================================
        private static void SignalHead(MeshBuild body, Vector3 at, Vector3 face, int lit)
        {
            var rot = Quaternion.LookRotation(face, Vector3.up);
            body.Box(at, new Vector3(0.52f, 1.35f, 0.36f), rot);
            for (int i = 0; i < 3; i++)
            {
                var lens = at + Vector3.up * (0.42f - i * 0.42f) + face * 0.19f;
                body.Box(lens + Vector3.up * 0.19f + face * 0.07f, new Vector3(0.40f, 0.03f, 0.28f), rot);   // hood
                var mesh = i == lit ? (i == 0 ? _sigRed : i == 1 ? _sigAmber : _sigGreen) : _sigOff;
                mesh.Box(lens, new Vector3(0.31f, 0.31f, 0.06f), rot);
            }
        }

        private static void PedestrianHead(MeshBuild body, Vector3 at, Vector3 face, bool walk)
        {
            var rot = Quaternion.LookRotation(face, Vector3.up);
            body.Box(at, new Vector3(0.34f, 0.62f, 0.2f), rot);
            var top = at + Vector3.up * 0.16f + face * 0.11f;
            var bottom = at - Vector3.up * 0.16f + face * 0.11f;
            (walk ? _sigOff : _sigStop).Box(top, new Vector3(0.22f, 0.22f, 0.04f), rot);
            (walk ? _sigWalk : _sigOff).Box(bottom, new Vector3(0.22f, 0.22f, 0.04f), rot);
        }

        private static MeshBuild _sigRed, _sigAmber, _sigGreen, _sigOff, _sigWalk, _sigStop;

        /// <summary>
        /// Traffic signals at every junction of three or more arms, a pinwheel of four poles on the footway
        /// corners: each carries the head for the arm whose driver keeps to that side, and a pedestrian signal
        /// for each crossing it stands beside. The phase is fixed per junction (and a few show amber): the
        /// arms across the junction are green when these are red, and a crossing shows walk while the traffic
        /// it crosses is stopped. A mid-block crossing gets a beacon on a pole at each end. Emissive only.
        /// </summary>
        private static void BuildCitySignals(Transform root, int layer, System.Random rng)
        {
            EnsureRoadMaterials();
            var group = new GameObject("Signals").transform;
            group.SetParent(root, false);
            int backdrop = LayerMask.NameToLayer("Backdrop");
            if (backdrop < 0) backdrop = layer;

            var poles = new MeshBuild { UVScale = 0.5f };
            var body = new MeshBuild { UVScale = 1f };
            _sigRed = new MeshBuild { UVScale = 1f }; _sigAmber = new MeshBuild { UVScale = 1f }; _sigGreen = new MeshBuild { UVScale = 1f };
            _sigOff = new MeshBuild { UVScale = 1f }; _sigWalk = new MeshBuild { UVScale = 1f }; _sigStop = new MeshBuild { UVScale = 1f };

            // Outward directions of each arm in a fixed order, east first, clockwise from above.
            var dirs = new[] { Vector3.right, Vector3.back, Vector3.left, Vector3.forward };
            var step = new[] { Vector2Int.right, Vector2Int.down, Vector2Int.left, Vector2Int.up };

            foreach (var tile in _roadTiles)
            {
                var centre = new Vector3(tile.x * Tile, 0f, tile.y * Tile);

                if (MidBlockCrossing(tile))
                {
                    // Beacons: a pole at each end of the crossing, on the footway, with an amber lamp on top.
                    bool alongZ = _roadTiles.Contains(tile + Vector2Int.up);
                    var along = alongZ ? Vector3.forward : Vector3.right;
                    var across = Vector3.Cross(Vector3.up, along);
                    foreach (float side in new[] { -1f, 1f })
                        foreach (float a in new[] { -2.0f, 2.0f })
                        {
                            var pole = centre + across * side * (CarriageHalf + 0.7f) + along * a;
                            poles.Tube(pole, pole + Vector3.up * 2.9f, 0.07f, 0.06f, 8);
                            _sigAmber.Box(pole + Vector3.up * 3.0f, new Vector3(0.34f, 0.34f, 0.34f), Quaternion.identity);
                        }
                    continue;
                }

                var arm = new bool[4];
                int arms = 0;
                for (int i = 0; i < 4; i++) { arm[i] = _roadTiles.Contains(tile + step[i]); if (arm[i]) arms++; }
                if (arms < 3) continue;

                // Phase 0: east-west green, 1: north-south green, 2: east-west amber.
                int phase = (tile.x * 3 + tile.y * 5 + 99) % 3;
                int Driver(int i)      // 0 red, 1 amber, 2 green
                {
                    bool ew = i == 0 || i == 2;
                    if (ew) return phase == 0 ? 2 : phase == 1 ? 0 : 1;
                    return phase == 1 ? 2 : 0;
                }

                var built = new HashSet<Vector2Int>();
                void Pole(Vector3 at)
                {
                    if (!built.Add(new Vector2Int(Mathf.RoundToInt(at.x * 2f), Mathf.RoundToInt(at.z * 2f)))) return;
                    poles.Tube(at, at + Vector3.up * 3.7f, 0.075f, 0.06f, 8);
                    poles.Tube(at, at + Vector3.up * 0.25f, 0.14f, 0.12f, 8);
                }

                for (int i = 0; i < 4; i++)
                {
                    if (!arm[i]) continue;
                    var d = dirs[i];
                    var across = Vector3.Cross(Vector3.up, d);
                    var right = -across;                                     // traffic enters going -d and keeps to this side

                    // The driver's head, on the corner on their side of the arm.
                    var headPole = centre + d * SignalPoleOffset + right * SignalPoleOffset;
                    Pole(headPole);
                    SignalHead(body, headPole + Vector3.up * 3.0f + d * 0.24f, d, Driver(i));

                    // Pedestrian signals at each end of this arm's crossing, facing across the carriageway.
                    bool walk = Driver(i) == 0;                              // walk while this arm's traffic waits
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var pole = centre + d * SignalPoleOffset + across * side * SignalPoleOffset;
                        Pole(pole);
                        PedestrianHead(body, pole + Vector3.up * 2.35f - across * side * 0.2f, -across * side, walk);
                    }
                }
            }

            Hide(MeshObject(group, "SignalPoles", ToMesh(poles, "citysigpoles"), _cityPlant, Vector3.zero, Quaternion.identity, Vector3.one, backdrop, null, collider: false));
            Hide(MeshObject(group, "SignalBodies", ToMesh(body, "citysigbodies"), _cityDark, Vector3.zero, Quaternion.identity, Vector3.one, backdrop, null, collider: false));
            AddSignalLayer(group, backdrop, _sigRed, _tlRed, "SignalRed");
            AddSignalLayer(group, backdrop, _sigAmber, _tlAmber, "SignalAmber");
            AddSignalLayer(group, backdrop, _sigGreen, _tlGreen, "SignalGreen");
            AddSignalLayer(group, backdrop, _sigWalk, _tlWalk, "SignalWalk");
            AddSignalLayer(group, backdrop, _sigStop, _tlStop, "SignalStop");
            AddSignalLayer(group, backdrop, _sigOff, _tlOff, "SignalOff");
        }

        private static void AddSignalLayer(Transform group, int layer, MeshBuild build, Material mat, string name)
        {
            if (build.Triangles.Count == 0) return;
            var go = MeshObject(group, name, ToMesh(build, "city" + name.ToLowerInvariant()), mat, Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false);
            Hide(go);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
#endif
