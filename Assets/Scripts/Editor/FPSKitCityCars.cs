#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The parked cars of the rooftop district. Each is a lofted body (a ring of ten points swept down
    /// the length of the car, so the hood, deck, greenhouse and tail curve instead of stepping), a glass cabin,
    /// wheel arches with tyres and rims, head and tail lamps, bumpers, mirrors and plates: about a thousand
    /// triangles, three shapes, shared by every car, six submeshes so a car is one renderer. The pack's own cars
    /// were rusted wrecks of 8-21 k triangles each, and the first boxes read as toys.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private sealed class CarSpec
        {
            public string Name;
            public float L, W, Belt, Roof, C0, C1, Hood;   // length, width, beltline, roof height, cabin start/end (0-1), hood drop
        }

        private static readonly CarSpec[] CarTypes =
        {
            new CarSpec { Name = "Sedan", L = 4.70f, W = 1.82f, Belt = 0.92f, Roof = 1.44f, C0 = 0.30f, C1 = 0.74f, Hood = 0.28f },
            new CarSpec { Name = "Hatch", L = 4.05f, W = 1.74f, Belt = 0.90f, Roof = 1.50f, C0 = 0.27f, C1 = 0.90f, Hood = 0.26f },
            new CarSpec { Name = "Suv",   L = 4.85f, W = 1.95f, Belt = 1.06f, Roof = 1.78f, C0 = 0.24f, C1 = 0.93f, Hood = 0.20f },
        };

        private static Material[] _carPaint;
        private static Material _carGlass, _carTrim, _carRim, _carHead, _carTail;
        private static Mesh[] _carMeshes;

        private static void EnsureCarMaterials()
        {
            if (_carPaint != null && _carPaint.Length > 0 && _carPaint[0] != null && _carGlass != null && _carMeshes != null && _carMeshes[0] != null) return;

            var paint = new[]
            {
                new Color(0.55f, 0.06f, 0.06f), new Color(0.06f, 0.12f, 0.36f), new Color(0.62f, 0.64f, 0.67f), new Color(0.82f, 0.82f, 0.80f),
                new Color(0.05f, 0.05f, 0.06f), new Color(0.05f, 0.30f, 0.30f), new Color(0.72f, 0.52f, 0.08f), new Color(0.30f, 0.05f, 0.12f),
            };
            _carPaint = new Material[paint.Length];
            for (int i = 0; i < paint.Length; i++)
            {
                _carPaint[i] = MakeMaterial($"CarPaint_{i}", paint[i], 0.72f, 0.35f);
                _carPaint[i].SetColor("_BaseColor", paint[i]);
                _carPaint[i].SetFloat("_Smoothness", 0.72f);
                _carPaint[i].SetFloat("_Metallic", 0.35f);
                UnityEditor.EditorUtility.SetDirty(_carPaint[i]);
            }
            _carGlass = MakeMaterial("CarGlass", new Color(0.015f, 0.025f, 0.045f), 0.95f, 0.0f);
            _carTrim = MakeMaterial("CarTrim", new Color(0.03f, 0.03f, 0.035f), 0.28f, 0.0f);
            _carRim = MakeMaterial("CarRim", new Color(0.62f, 0.63f, 0.66f), 0.65f, 0.85f);
            _carHead = MakeMaterial("CarHead", new Color(0.70f, 0.76f, 0.88f), 0.92f, 0.1f);
            _carTail = MakeMaterial("CarTail", new Color(0.40f, 0.02f, 0.02f), 0.8f, 0.0f);
            _carTail.EnableKeyword("_EMISSION");
            _carTail.SetColor("_EmissionColor", new Color(1.0f, 0.05f, 0.03f) * 0.8f);
            _carTail.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            UnityEditor.EditorUtility.SetDirty(_carTail);

            _carMeshes = new Mesh[CarTypes.Length];
            for (int i = 0; i < CarTypes.Length; i++) _carMeshes[i] = BuildCarMesh(CarTypes[i]);
        }

        private static float Sm(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>One car as a six-submesh mesh: paint, glass, dark trim and tyres, rims, head lamps, tail lamps.</summary>
        private static Mesh BuildCarMesh(CarSpec sp)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>[6];
            for (int i = 0; i < 6; i++) tris[i] = new List<int>();
            const int PAINT = 0, GLASS = 1, TRIM = 2, RIM = 3, HEAD = 4, TAIL = 5;

            int V(Vector3 p) { verts.Add(p); return verts.Count - 1; }
            void Quad(int sub, int a, int b, int c, int d) { tris[sub].AddRange(new[] { a, b, d, b, c, d }); }

            const int N = 28;
            float L = sp.L, W2 = sp.W * 0.5f;
            float Belt(float s) => sp.Belt - sp.Hood * (1f - Sm(0f, 0.30f, s)) * 0.9f - 0.10f * Sm(0.80f, 1f, s);
            float CabinBlend(float s) => Sm(sp.C0 - 0.07f, sp.C0 + 0.07f, s) * (1f - Sm(sp.C1 - 0.02f, sp.C1 + 0.10f, s));

            Vector3[] BodyRing(float s)
            {
                float x = (s - 0.5f) * L;
                float m = Mathf.Min(s, 1f - s);
                float k = m < 0.045f ? Mathf.Sqrt(Mathf.Clamp01(1f - Mathf.Pow(1f - m / 0.045f, 2f))) : 1f;
                float hb = Belt(s), ht = hb + 0.06f, yb = 0.24f, wb = W2;
                float cy = hb * 0.55f;
                var p = new (float z, float y)[]
                {
                    (-wb * 0.78f, yb), (-wb, yb + 0.12f), (-wb, hb - 0.14f), (-wb * 0.97f, hb), (-wb * 0.84f, ht),
                    ( wb * 0.84f, ht), ( wb * 0.97f, hb), ( wb, hb - 0.14f), ( wb, yb + 0.12f), ( wb * 0.78f, yb),
                };
                var r = new Vector3[p.Length];
                for (int i = 0; i < p.Length; i++) r[i] = new Vector3(x, Mathf.Lerp(cy, p[i].y, 0.30f + 0.70f * k), p[i].z * k);
                return r;
            }

            Vector3[] GlassRing(float s)
            {
                float x = (s - 0.5f) * L;
                float hb = Belt(s), ht = hb + 0.06f;
                float hr = ht + CabinBlend(s) * (sp.Roof - ht);
                float wr = W2 * 0.80f;
                float edge = Mathf.Max(ht, hr - 0.10f);
                return new[]
                {
                    new Vector3(x, ht, -W2 * 0.95f), new Vector3(x, edge, -wr), new Vector3(x, hr, -wr * 0.86f),
                    new Vector3(x, hr, wr * 0.86f), new Vector3(x, edge, wr), new Vector3(x, ht, W2 * 0.95f),
                };
            }

            var ss = new float[N];
            for (int i = 0; i < N; i++) ss[i] = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * i / (N - 1));

            // ---- body ----
            var body = new int[N][];
            for (int i = 0; i < N; i++)
            {
                var r = BodyRing(ss[i]);
                body[i] = new int[r.Length];
                for (int j = 0; j < r.Length; j++) body[i][j] = V(r[j]);
            }
            int rl = body[0].Length;
            for (int i = 0; i < N - 1; i++)
                for (int j = 0; j < rl; j++)
                {
                    int j2 = (j + 1) % rl;
                    Quad(PAINT, body[i][j], body[i][j2], body[i + 1][j2], body[i + 1][j]);
                }

            // ---- greenhouse: glass everywhere, paint on the roof panel and its edges ----
            var gl = new int[N][];
            var gh = new float[N];
            for (int i = 0; i < N; i++)
            {
                var r = GlassRing(ss[i]);
                gl[i] = new int[r.Length];
                for (int j = 0; j < r.Length; j++) gl[i][j] = V(r[j]);
                gh[i] = CabinBlend(ss[i]) * (sp.Roof - (Belt(ss[i]) + 0.06f));
            }
            for (int i = 0; i < N - 1; i++)
            {
                if (gh[i] < 0.02f && gh[i + 1] < 0.02f) continue;
                float mid = (ss[i] + ss[i + 1]) * 0.5f;
                bool roofRange = mid > sp.C0 + 0.07f && mid < sp.C1 - 0.02f;
                for (int j = 0; j < 5; j++)
                {
                    int sub = (j == 0 || j == 4) ? GLASS : (roofRange ? PAINT : GLASS);
                    Quad(sub, gl[i][j], gl[i][j + 1], gl[i + 1][j + 1], gl[i + 1][j]);
                }
            }

            // ---- boxes and discs with their own vertices (flat) ----
            void Box(int sub, Vector3 c, Vector3 size, Quaternion rot)
            {
                Vector3 h = size * 0.5f;
                Vector3 P(float x, float y, float z) => c + rot * new Vector3(x * h.x, y * h.y, z * h.z);
                int[] q(Vector3 a, Vector3 b, Vector3 cc, Vector3 d) => new[] { V(a), V(b), V(cc), V(d) };
                void F(Vector3 a, Vector3 b, Vector3 cc, Vector3 d) { var i = q(a, b, cc, d); Quad(sub, i[0], i[1], i[2], i[3]); }
                F(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1));
                F(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1));
                F(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));
                F(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1));
                F(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1));
                F(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1));
            }

            // A cylinder along z: tyre, rim, arch disc.
            void Disc(int sub, float x, float y, float z0, float z1, float r, int seg)
            {
                var ring0 = new int[seg]; var ring1 = new int[seg];
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    var o = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
                    ring0[i] = V(new Vector3(x, y, z0) + o);
                    ring1[i] = V(new Vector3(x, y, z1) + o);
                }
                for (int i = 0; i < seg; i++)
                {
                    int j = (i + 1) % seg;
                    tris[sub].AddRange(new[] { ring0[i], ring0[j], ring1[j], ring0[i], ring1[j], ring1[i] });
                }
                int c0 = V(new Vector3(x, y, z0)), c1 = V(new Vector3(x, y, z1));
                for (int i = 0; i < seg; i++)
                {
                    int j = (i + 1) % seg;
                    tris[sub].AddRange(new[] { c1, ring1[i], ring1[j] });
                    tris[sub].AddRange(new[] { c0, ring0[j], ring0[i] });
                }
            }

            float wheelF = 0.32f * L, wheelR = -0.32f * L;
            foreach (float wx in new[] { wheelF, wheelR })
                foreach (float side in new[] { -1f, 1f })
                {
                    float zIn = side * (W2 - 0.20f), zOut = side * (W2 + 0.03f);
                    float z0 = Mathf.Min(zIn, zOut), z1 = Mathf.Max(zIn, zOut);
                    Disc(TRIM, wx, 0.34f, z0, z1, 0.34f, 16);                                  // tyre
                    Disc(RIM, wx, 0.34f, side > 0 ? z1 : z0, side > 0 ? z1 + 0.012f : z0 - 0.012f, 0.21f, 12);   // rim
                    Disc(TRIM, wx, 0.36f, side * (W2 + 0.003f) - 0.002f, side * (W2 + 0.003f) + 0.002f, 0.44f, 16);   // the dark of the arch
                }

            float hb0 = Belt(0f), hbF = Belt(0.04f), hbR = Belt(0.96f);
            // bumpers and a skirt: dark, a little proud of the body
            Box(TRIM, new Vector3(L * 0.5f - 0.05f, 0.44f, 0f), new Vector3(0.26f, 0.30f, sp.W * 0.96f), Quaternion.identity);
            Box(TRIM, new Vector3(-L * 0.5f + 0.05f, 0.46f, 0f), new Vector3(0.26f, 0.30f, sp.W * 0.96f), Quaternion.identity);
            Box(TRIM, new Vector3(0f, 0.27f, -W2 + 0.02f), new Vector3(L * 0.50f, 0.10f, 0.05f), Quaternion.identity);
            Box(TRIM, new Vector3(0f, 0.27f, W2 - 0.02f), new Vector3(L * 0.50f, 0.10f, 0.05f), Quaternion.identity);
            // grille and plates
            Box(TRIM, new Vector3(L * 0.5f - 0.02f, hbF - 0.20f, 0f), new Vector3(0.06f, 0.20f, sp.W * 0.46f), Quaternion.identity);
            Box(RIM, new Vector3(L * 0.5f + 0.09f, 0.44f, 0f), new Vector3(0.02f, 0.12f, 0.42f), Quaternion.identity);
            Box(RIM, new Vector3(-L * 0.5f - 0.09f, 0.50f, 0f), new Vector3(0.02f, 0.12f, 0.42f), Quaternion.identity);
            // lamps
            foreach (float side in new[] { -1f, 1f })
            {
                Box(HEAD, new Vector3(L * 0.5f - 0.10f, hbF - 0.06f, side * W2 * 0.68f), new Vector3(0.20f, 0.14f, 0.36f), Quaternion.identity);
                Box(TAIL, new Vector3(-L * 0.5f + 0.08f, hbR - 0.10f, side * W2 * 0.70f), new Vector3(0.14f, 0.13f, 0.40f), Quaternion.identity);
                // mirrors
                Box(TRIM, new Vector3(sp.C0 * L - L * 0.5f + 0.10f, Belt(sp.C0) + 0.12f, side * (W2 + 0.10f)), new Vector3(0.12f, 0.12f, 0.20f), Quaternion.identity);
                // door handles
                Box(RIM, new Vector3(0.05f * L, Belt(0.5f) - 0.12f, side * (W2 + 0.01f)), new Vector3(0.16f, 0.025f, 0.02f), Quaternion.identity);
                Box(RIM, new Vector3(-0.22f * L, Belt(0.3f) - 0.12f, side * (W2 + 0.01f)), new Vector3(0.16f, 0.025f, 0.02f), Quaternion.identity);
            }
            // a door line down each side: a thin dark groove
            foreach (float side in new[] { -1f, 1f })
                foreach (float dx in new[] { sp.C0 * L - L * 0.5f + 0.15f, 0.03f * L, sp.C1 * L - L * 0.5f - 0.05f })
                    Box(TRIM, new Vector3(dx, (Belt(0.5f) + 0.30f) * 0.5f, side * (W2 + 0.003f)), new Vector3(0.015f, Belt(0.5f) - 0.40f, 0.01f), Quaternion.identity);

            var mesh = new Mesh { name = $"citycar_{sp.Name}" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.subMeshCount = 6;
            for (int i = 0; i < 6; i++) mesh.SetTriangles(tris[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>
        /// Cars along the kerbs, nose to tail with gaps, on both sides of the straights, never in a junction and never across a
        /// crossing. Each is a body with box colliders (a mesh collider on a thousand triangles, seventy times, would be waste);
        /// the whole thing is off the navigation bake as a standable surface.
        /// </summary>
        private static void BuildCityCars(Transform root, int layer, System.Random rng)
        {
            EnsureStreetMaterials();
            EnsureCarMaterials();
            var group = new GameObject("ParkedCars").transform;
            group.SetParent(root, false);

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

                float cx = tile.x * Tile, cz = tile.y * Tile;
                foreach (float side in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() < 0.40) continue;
                    float slot = Rand(rng, -5.0f, 5.0f);
                    var at = alongZ ? new Vector3(cx + side * 3.4f, 0f, cz + slot) : new Vector3(cx + slot, 0f, cz + side * 3.4f);
                    if (at.magnitude < 34f) continue;
                    if (NearCrossing(at, 3.4f)) continue;
                    // Nose to the flow of the lane it is parked in (traffic keeps right).
                    float yaw = alongZ ? (side > 0 ? 270f : 90f) : (side > 0 ? 180f : 0f);
                    yaw += (float)(rng.NextDouble() - 0.5) * 3f;

                    double pick = rng.NextDouble();
                    int type = pick < 0.45 ? 0 : pick < 0.75 ? 1 : 2;
                    var sp = CarTypes[type];
                    int p = rng.Next(0, _carPaint.Length);

                    var car = new GameObject($"Car_{made}_{sp.Name}");
                    car.transform.SetParent(group, false);
                    car.transform.localPosition = at;
                    car.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                    car.layer = layer;
                    car.tag = "Metal";
                    car.AddComponent<MeshFilter>().sharedMesh = _carMeshes[type];
                    var mr = car.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = new[] { _carPaint[p], _carGlass, _carTrim, _carRim, _carHead, _carTail };
                    var low = car.AddComponent<BoxCollider>();
                    low.center = new Vector3(0f, 0.62f, 0f); low.size = new Vector3(sp.L, 0.78f, sp.W);
                    var cab = car.AddComponent<BoxCollider>();
                    cab.center = new Vector3(0.0f, (sp.Belt + sp.Roof) * 0.5f, 0f); cab.size = new Vector3(sp.L * (sp.C1 - sp.C0) * 0.9f, sp.Roof - sp.Belt, sp.W * 0.8f);
                    NoStanding(car);
                    Mark(car, new Color(0.30f, 0.30f, 0.33f), 3);
                    StaticFlags(car);
                    made++;
                    if (made >= _theme.cityParkedCars) break;
                }
            }
        }
    }
}
#endif
