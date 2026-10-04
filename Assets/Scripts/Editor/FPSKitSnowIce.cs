#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Snowbound's ice, rebuilt in real geometry (2026-10-04). Ishaan, looking at his own Game view: the
    /// frozen lake, the glacier ice and the crevasse area "very bad". The lake was a 312-triangle disc with
    /// boxes on it, the icefall 140 triangles, each ice cave 40, and the crevasse a pair of smooth walls
    /// with a flat sheet of ice at the bottom. Each is now built from a grid or a pile of broken pieces,
    /// roughly twenty to a hundred times the triangles, with the faults a real one has: a lake with
    /// heaved plates and fissures, a crevasse that narrows with depth and hangs icicles from its ledges.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static Material _iceDeepMat, _iceCrackMat;

        private static void EnsureIceMats()
        {
            if (_iceDeepMat != null && _iceCrackMat != null) return;
            _iceDeepMat = MakeDetailMaterial("GlacierDeep", new Color(0.10f, 0.26f, 0.44f), "Ice", Metres(3f), 0.7f, 0f, 1.6f);
            _iceCrackMat = MakeMaterial("IceCrack", new Color(0.07f, 0.15f, 0.26f), 0.85f, 0f);
        }

        // ==================================================================
        // A broken block of ice or rock: eight jittered corners, every face a fan to a pushed-out centre.
        // ==================================================================
        private static void JaggedBlock(MeshBuild b, Vector3 c, Vector3 size, Quaternion rot, int seed)
        {
            var h = size * 0.5f;
            var p = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                float sx = (i & 1) * 2f - 1f, sy = ((i >> 1) & 1) * 2f - 1f, sz = ((i >> 2) & 1) * 2f - 1f;
                float jx = (Hash01(seed, i * 3) - 0.5f) * 0.36f, jy = (Hash01(seed, i * 3 + 1) - 0.5f) * 0.36f, jz = (Hash01(seed, i * 3 + 2) - 0.5f) * 0.36f;
                p[i] = c + rot * new Vector3(sx * h.x * (1f + jx), sy * h.y * (1f + jy), sz * h.z * (1f + jz));
            }
            var faces = new[] { new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 } };
            int f = 0;
            foreach (var q in faces)
            {
                var avg = (p[q[0]] + p[q[1]] + p[q[2]] + p[q[3]]) * 0.25f;
                var ctr = avg + (avg - c).normalized * size.magnitude * 0.05f * (Hash01(seed, 50 + f++) - 0.35f);
                for (int k = 0; k < 4; k++) AddOutward(b, c, p[q[k]], p[q[(k + 1) % 4]], ctr);
            }
        }

        // ==================================================================
        // The frozen lake
        // ==================================================================
        private static float LakeBump(float x, float z)
            => (Fbm2(x * 0.06f, z * 0.06f, _theme.randomSeed + 77, 3) + 0.5f) * 0.16f + Fbm2(x * 0.55f, z * 0.55f, _theme.randomSeed + 79, 2) * 0.025f;

        private static float LakeRim(float t)
            => _lakeRadius * (1f + 0.055f * Mathf.Sin(t * 3f + 0.7f) + 0.035f * Mathf.Sin(t * 5f + 2.1f) + 0.020f * Mathf.Sin(t * 8f + 4.4f)
                              + 0.012f * Mathf.Sin(t * 17f + 1.3f));

        /// <summary>
        /// The sheet: twenty-eight rings of a hundred and four steps (about 5,800 triangles), humped by two
        /// scales of noise, thinnest at the shore, with a ragged rim.
        /// </summary>
        private static Mesh LakeMesh() => Pooled($"lake_{_lakeRadius:0.0}_v2", () =>
        {
            var build = new MeshBuild { UVScale = 0.08f };
            const int Rings = 28, Sides = 104;
            var grid = new Vector3[Rings + 1, Sides];
            for (int k = 0; k <= Rings; k++)
                for (int i = 0; i < Sides; i++)
                {
                    float t = i / (float)Sides * Mathf.PI * 2f;
                    float u = k / (float)Rings;
                    float r = LakeRim(t) * u;
                    if (k >= Rings - 1) r *= 1f + (Hash01(i, k) - 0.5f) * 0.012f;     // a ragged edge
                    float x = Mathf.Cos(t) * r, z = Mathf.Sin(t) * r;
                    grid[k, i] = new Vector3(x, LakeBump(x, z) * (1f - Mathf.Pow(u, 5f)) - 0.04f * Mathf.Pow(u, 8f), z);
                }
            for (int k = 0; k < Rings; k++)
                for (int i = 0; i < Sides; i++)
                {
                    int j = (i + 1) % Sides;
                    AddUp(build, grid[k, i], grid[k, j], grid[k + 1, j], grid[k + 1, i]);
                }
            return build.ToMesh("FrozenLake");
        });

        private static void BuildLake(Transform root, int layer)
        {
            EnsureIceMats();
            var group = new GameObject("FrozenLake").transform;
            group.SetParent(root, false);

            // Ask the terrain, do not trust the plan (see the note this replaced): laid at the planned height
            // the sheet was under the snow.
            float surface = GroundHeightAt(_lakeAt.x, _lakeAt.y) + 0.08f;
            var centre = new Vector3(_lakeAt.x, surface, _lakeAt.y);

            var sheet = MeshObject(group, "Ice", LakeMesh(), _iceMat, centre, Quaternion.identity, Vector3.one, layer, IceTag);
            Mark(sheet, new Color(0.62f, 0.80f, 0.95f, 0.75f), 10);

            var rng = new System.Random(_theme.randomSeed ^ 0x51CE);

            // ---- fissures: dark cracks that wander, narrow and fork, lying a finger above the sheet ----
            var cracks = new MeshBuild { UVScale = 0.2f };
            void Crack(Vector2 from, float heading, int steps, float width, int depth)
            {
                var p = from;
                for (int s = 0; s < steps; s++)
                {
                    heading += Rand(rng, -0.42f, 0.42f);
                    float seg = Rand(rng, 1.4f, 3.2f);
                    var q = p + new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * seg;
                    if (q.magnitude > _lakeRadius * 0.93f) return;
                    float w0 = width * (1f - s / (float)steps * 0.7f), w1 = width * (1f - (s + 1) / (float)steps * 0.7f);
                    var n = new Vector2(-Mathf.Sin(heading), Mathf.Cos(heading));
                    Vector3 Up(Vector2 a, float side, float w) => new Vector3(a.x + n.x * side * w, LakeBump(a.x, a.y) + 0.03f, a.y + n.y * side * w);
                    AddUp(cracks, Up(p, -1f, w0), Up(p, 1f, w0), Up(q, 1f, w1), Up(q, -1f, w1));
                    if (depth > 0 && Rand(rng, 0f, 1f) < 0.16f)
                        Crack(q, heading + Rand(rng, 0.5f, 1.2f) * (rng.Next(2) * 2 - 1), Mathf.Max(3, steps / 2), width * 0.6f, depth - 1);
                    p = q;
                }
            }
            for (int i = 0; i < 16; i++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f), r = Rand(rng, 0.05f, 0.8f) * _lakeRadius;
                Crack(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r), Rand(rng, 0f, Mathf.PI * 2f), 14 + rng.Next(14), Rand(rng, 0.07f, 0.15f), 2);
            }
            var crackGo = MeshObject(group, "Fissures", ToMesh(cracks, DenseKey("lakecracks")), _iceCrackMat, centre, Quaternion.identity,
                                     Vector3.one, layer, null, collider: false);
            NoStanding(crackGo); Hide(crackGo);

            // ---- clear ice and wind-blown snow lying on it ----
            for (int i = 0; i < 46; i++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f), r = Mathf.Sqrt(Rand(rng, 0f, 1f)) * _lakeRadius * 0.88f;
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                var patch = MeshObject(group, "WindSnow", BoulderMesh(rng.Next(1, 90), 0.2f, 0.25f), _snowMat,
                                       centre + new Vector3(x, LakeBump(x, z) - 0.02f, z), Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f),
                                       new Vector3(Rand(rng, 1.6f, 4.8f), Rand(rng, 0.12f, 0.26f), Rand(rng, 0.9f, 2.6f)), layer, null, collider: false);
                NoStanding(patch);
            }

            // ---- heaved plates, in clusters where the sheet has buckled against the shore ----
            int clusters = 7 + rng.Next(3);
            for (int cl = 0; cl < clusters; cl++)
            {
                float ca = Rand(rng, 0f, Mathf.PI * 2f);
                float cr = LakeRim(ca) * Rand(rng, 0.78f, 0.95f);
                var plates = new MeshBuild { UVScale = 0.3f };
                int count = 9 + rng.Next(8);
                for (int k = 0; k < count; k++)
                {
                    float a = ca + Rand(rng, -0.16f, 0.16f);
                    float r = cr + Rand(rng, -3.5f, 3.5f);
                    float x = _lakeAt.x + Mathf.Cos(a) * r, z = _lakeAt.y + Mathf.Sin(a) * r;
                    float w = Rand(rng, 1.4f, 3.8f), d = Rand(rng, 0.9f, 2.4f), h = Rand(rng, 0.35f, 1.0f);
                    var rot = Quaternion.Euler(Rand(rng, 14f, 42f), Rand(rng, 0f, 360f), Rand(rng, -16f, 16f));
                    float y = Mathf.Max(GroundHeightAt(x, z), surface - 0.1f);
                    JaggedBlock(plates, new Vector3(x, y + Rand(rng, 0.1f, 0.6f), z), new Vector3(w, h, d), rot, rng.Next(1, 99999));
                }
                var go = MeshObject(group, "HeavedPlates", ToMesh(plates, DenseKey("lakeplates")), _iceBlockMat, Vector3.zero, Quaternion.identity,
                                    Vector3.one, layer, IceTag);
                NoStanding(go);
            }

            // ---- snow banked along the shore ----
            for (int i = 0; i < 38; i++)
            {
                float a = i / 38f * Mathf.PI * 2f + Rand(rng, -0.05f, 0.05f);
                float r = LakeRim(a) + Rand(rng, 0.5f, 3f);
                float x = _lakeAt.x + Mathf.Cos(a) * r, z = _lakeAt.y + Mathf.Sin(a) * r;
                var drift = MeshObject(group, "ShoreDrift", BoulderMesh(rng.Next(1, 99), 0.18f, 0.3f), _snowMat,
                                       new Vector3(x, GroundHeightAt(x, z) + 0.05f, z), Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f),
                                       new Vector3(Rand(rng, 3.5f, 6.5f), Rand(rng, 0.7f, 1.3f), Rand(rng, 1.8f, 3.4f)), layer, null, collider: false);
                NoStanding(drift);
            }
        }

        // ==================================================================
        // The crevasse
        // ==================================================================
        /// <summary>
        /// A crevasse that is shaped like one: it narrows with depth (a V, not a slot), its walls bulge and
        /// step in ledges every few metres, icicles hang from the ledges, the floor is a jumble of fallen
        /// ice, and the edge is a rolled snow cornice over a lip that follows the ground. The terrain mesher
        /// cuts every cell the crack touches plus a margin, so a floor goes under the whole hole and a skin
        /// of snow over its staircase edge -- left out, the sky shows through.
        /// </summary>
        private static void BuildCrevasse(Transform parent, int layer, System.Random rng, Crevasse c)
        {
            EnsureIceMats();
            var group = new GameObject("Crevasse").transform;
            group.SetParent(parent, false);

            var ab = c.B - c.A;
            float len = ab.magnitude;
            var dir = ab / len;
            var side = new Vector2(-dir.y, dir.x);
            const float depth = 14f;
            int along = Mathf.CeilToInt(len / 0.8f);
            const int rows = 16;
            float rowH = depth / rows;

            var walls = new MeshBuild { UVScale = 0.22f };
            var deep = new MeshBuild { UVScale = 0.22f };
            var floor = new MeshBuild { UVScale = 0.22f };
            var lip = new MeshBuild { UVScale = 0.3f };
            var roll = new MeshBuild { UVScale = 0.3f };

            float Taper(float t) => Mathf.Max(0.35f, Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI));
            float Wall(float t, int s) => c.Half * Taper(t) + Fbm2(t * len * 0.17f, s * 3f, 8101, 2) * 0.9f;

            // Wall vertex: on the lip line, dropped k rows, drawn in with depth, bulged by noise, with a ledge.
            Vector3 WallPt(int i, int k, int s)
            {
                float t = i / (float)along;
                float w = Wall(t, s);
                float drop = k * rowH;
                float narrow = 1f - 0.38f * (k / (float)rows);
                float bulge = Fbm2(t * len * 0.33f, k * 0.55f + s * 9f, 8113, 3) * 1.1f * Mathf.Clamp01(k / 3f);   // none at the lip, or it gaps
                bool ledge = k % 5 == 2;
                float step = ledge ? 0.45f : 0f;
                float off = w * narrow + bulge - step * (k % 5 == 2 ? 1f : 0f);
                var p = c.A + dir * (t * len) + side * s * Mathf.Max(0.25f, off);
                return new Vector3(p.x, SurfaceHeightAt(p.x, p.y) - drop, p.y);
            }

            for (int s = -1; s <= 1; s += 2)
            {
                for (int i = 0; i < along; i++)
                    for (int k = 0; k < rows; k++)
                    {
                        var a = WallPt(i, k, s); var b = WallPt(i + 1, k, s);
                        var d = WallPt(i, k + 1, s); var e = WallPt(i + 1, k + 1, s);
                        var mat = k > rows * 0.55f ? deep : walls;
                        // Facing into the crack: the side of the wall that looks at the opposite wall.
                        var inside = (a + b + d + e) * 0.25f - new Vector3(side.x, 0f, side.y) * s * 3f;
                        AddOutward(mat, inside + new Vector3(side.x, 0f, side.y) * s * 6f, a, b, d);
                        AddOutward(mat, inside + new Vector3(side.x, 0f, side.y) * s * 6f, b, e, d);
                    }

                // Icicles hanging from the ledges, longest where the wall overhangs.
                for (int i = 0; i < along; i += 2)
                    for (int k = 2; k < rows - 1; k += 5)
                    {
                        if (Hash01(i * 7 + s, k) < 0.35f) continue;
                        var top = WallPt(i, k, s);
                        float l = Rand(rng, 0.5f, 2.2f);
                        var inwards = new Vector3(side.x, 0f, side.y) * -s * 0.25f;
                        walls.Tube(top + inwards, top + inwards + Vector3.down * l, 0.13f, 0.015f, 5);
                    }
            }

            // The floor: a jumble, at the bottom and across the whole hole (and five metres past each end).
            {
                int fa = along + 8, fc = 7;
                float ext = 5f / len;
                Vector3 F(int i, int j)
                {
                    float t = Mathf.Lerp(-ext, 1f + ext, i / (float)fa);
                    float across = (j / (float)fc * 2f - 1f) * (c.Half * Taper(t) + 5f);
                    var p = c.A + dir * (t * len) + side * across;
                    float jumble = Fbm2(p.x * 0.45f, p.y * 0.45f, 8117, 3) * 1.6f;
                    return new Vector3(p.x, SurfaceHeightAt(p.x, p.y) - depth + jumble, p.y);
                }
                for (int i = 0; i < fa; i++)
                    for (int j = 0; j < fc; j++)
                        AddUp(floor, F(i, j), F(i + 1, j), F(i + 1, j + 1), F(i, j + 1));
                // Fallen blocks on it.
                for (int k = 0; k < 14; k++)
                {
                    float t = Rand(rng, 0.05f, 0.95f);
                    var p = c.A + dir * (t * len) + side * Rand(rng, -1f, 1f) * c.Half * 0.6f;
                    JaggedBlock(floor, new Vector3(p.x, SurfaceHeightAt(p.x, p.y) - depth + 0.5f, p.y),
                                new Vector3(Rand(rng, 0.8f, 2.2f), Rand(rng, 0.6f, 1.6f), Rand(rng, 0.8f, 2.2f)),
                                Quaternion.Euler(Rand(rng, 0f, 30f), Rand(rng, 0f, 360f), 0f), rng.Next(1, 9999));
                }
            }

            // Closed ends: a wall across each pinched end, in rows so it follows the ground.
            foreach (float e in new[] { 0f, 1f })
            {
                var p = c.A + dir * (e * len);
                for (int k = 0; k < rows; k++)
                {
                    float w = c.Half * 0.35f * (1f - 0.38f * k / rows);
                    var l0 = p - side * w; var r0 = p + side * w;
                    Vector3 At(Vector2 q, int row) => new Vector3(q.x, SurfaceHeightAt(q.x, q.y) - row * rowH, q.y);
                    // The rock is beyond the end, away from the crack: faces point back along the crack.
                    var rock = At(p, k) - new Vector3(dir.x, 0f, dir.y) * (e < 0.5f ? 1f : -1f) * 8f;
                    AddOutward(walls, rock, At(l0, k), At(r0, k), At(r0, k + 1));
                    AddOutward(walls, rock, At(l0, k), At(r0, k + 1), At(l0, k + 1));
                }
            }

            // ---- the cornice: strips of snow lying on the ground (heights sampled), and a roll along the edge ----
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < along; i++)
                {
                    float t0 = i / (float)along, t1 = (i + 1) / (float)along;
                    Vector3 L(float t, float extra)
                    {
                        var p = c.A + dir * (t * len) + side * s * (Wall(t, s) + extra);
                        float lump = (Fbm2(p.x * 0.8f, p.y * 0.8f, 8121, 2) + 0.5f) * 0.12f;
                        return new Vector3(p.x, SurfaceHeightAt(p.x, p.y) + 0.07f + lump * Mathf.Clamp01(extra / 1.5f), p.y);
                    }
                    for (int strip = 0; strip < 5; strip++)
                    {
                        float e0 = strip * 0.75f - 0.1f, e1 = (strip + 1) * 0.75f - 0.1f;
                        AddUp(lip, L(t0, e0), L(t1, e0), L(t1, e1), L(t0, e1));
                    }
                    roll.Tube(L(t0, 0.05f) + Vector3.down * 0.1f, L(t1, 0.05f) + Vector3.down * 0.1f, 0.33f, 0.33f, 6);
                }
            foreach (float e in new[] { 0f, 1f })
            {
                var endP = c.A + dir * (e * len);
                float outward = e < 0.5f ? -1f : 1f;
                float across = c.Half * 0.35f + 2.8f;
                const int gu = 9, gv = 6;
                Vector3 G(int iu, int iv)
                {
                    float tu = iu / (float)gu, tv = iv / (float)gv;
                    var p = endP + dir * outward * (tu * 3.4f - 0.5f) + side * (tv * 2f - 1f) * across;
                    float lump = (Fbm2(p.x * 0.8f, p.y * 0.8f, 8123, 2) + 0.5f) * 0.1f;
                    return new Vector3(p.x, SurfaceHeightAt(p.x, p.y) + 0.07f + lump, p.y);
                }
                for (int iu = 0; iu < gu; iu++)
                    for (int iv = 0; iv < gv; iv++)
                        AddUp(lip, G(iu, iv), G(iu, iv + 1), G(iu + 1, iv + 1), G(iu + 1, iv));
            }

            var wallGo = MeshObject(group, "CrevasseIce", ToMesh(walls, DenseKey("crevasse")), _glacierMat, Vector3.zero, Quaternion.identity,
                                    Vector3.one, layer, IceTag);
            if (wallGo != null) Mark(wallGo, new Color(0.30f, 0.46f, 0.62f), 2);
            MeshObject(group, "CrevasseDeepIce", ToMesh(deep, DenseKey("crevdeep")), _iceDeepMat, Vector3.zero, Quaternion.identity, Vector3.one, layer, IceTag);
            // The floor is out of reach and off the bake: a pocket of navmesh nobody can walk to is a spawn nobody can fight.
            NoStanding(MeshObject(group, "CrevasseFloor", ToMesh(floor, DenseKey("crevfloor")), _iceDeepMat, Vector3.zero, Quaternion.identity,
                                  Vector3.one, layer, IceTag));
            MeshObject(group, "CrevasseLip", ToMesh(lip, DenseKey("crevlip")), _pineSnowMat, Vector3.zero, Quaternion.identity, Vector3.one, layer, SnowTag);
            var rollGo = MeshObject(group, "CrevasseCornice", ToMesh(roll, DenseKey("crevroll")), _pineSnowMat, Vector3.zero, Quaternion.identity,
                                    Vector3.one, layer, null, collider: false);
            NoStanding(rollGo);

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

            // ---- the rope bridge: anchor posts, planks with gaps, two load ropes and two hand ropes ----
            var mid = c.A + ab * 0.5f;
            float span = c.Half * 2f + 5f;
            var bridge = new MeshBuild { UVScale = 0.5f };
            var ropes = new MeshBuild { UVScale = 0.5f };
            var posts = new MeshBuild { UVScale = 0.5f };
            var e0v = mid - side * span * 0.5f; var e1v = mid + side * span * 0.5f;
            float h0 = SurfaceHeightAt(e0v.x, e0v.y), h1 = SurfaceHeightAt(e1v.x, e1v.y);
            var rot = Quaternion.LookRotation(new Vector3(side.x, 0f, side.y));

            foreach (var ee in new[] { e0v, e1v })
                foreach (float lat in new[] { -1f, 1f })
                {
                    var q = new Vector3(ee.x + dir.x * lat * 1.0f, SurfaceHeightAt(ee.x, ee.y), ee.y + dir.y * lat * 1.0f);
                    posts.Tube(q + Vector3.down * 0.5f, q + Vector3.up * 1.7f, 0.17f, 0.14f, 8);
                    posts.Tube(q + Vector3.up * 1.6f, q + Vector3.up * 1.75f, 0.22f, 0.22f, 8);
                }

            int planks = Mathf.CeilToInt(span / 0.4f);
            Vector3 prevL = Vector3.zero, prevR = Vector3.zero, prevLo = Vector3.zero, prevRo = Vector3.zero;
            for (int k = 0; k <= planks; k++)
            {
                float t = k / (float)planks;
                var q = Vector2.Lerp(e0v, e1v, t);
                float y = Mathf.Lerp(h0, h1, t) - Mathf.Sin(t * Mathf.PI) * 0.4f + 0.1f;
                var at = new Vector3(q.x, y, q.y);
                bridge.Box(at, new Vector3(1.9f, 0.09f, 0.34f), rot * Quaternion.Euler(0f, Hash01(k, 3) * 3f - 1.5f, 0f));

                var l = at + rot * new Vector3(-1f, 1.0f, 0f);
                var r = at + rot * new Vector3(1f, 1.0f, 0f);
                var lo = at + rot * new Vector3(-0.85f, -0.05f, 0f);
                var ro = at + rot * new Vector3(0.85f, -0.05f, 0f);
                if (k > 0)
                {
                    ropes.Tube(prevL, l, 0.035f, 0.035f, 5); ropes.Tube(prevR, r, 0.035f, 0.035f, 5);
                    ropes.Tube(prevLo, lo, 0.045f, 0.045f, 5); ropes.Tube(prevRo, ro, 0.045f, 0.045f, 5);
                }
                if (k % 2 == 0) { ropes.Tube(at + rot * new Vector3(-0.95f, 0f, 0f), l, 0.02f, 0.02f, 4); ropes.Tube(at + rot * new Vector3(0.95f, 0f, 0f), r, 0.02f, 0.02f, 4); }
                prevL = l; prevR = r; prevLo = lo; prevRo = ro;
            }
            MeshObject(group, "RopeBridge", ToMesh(bridge, DenseKey("ropebridge")), _campTimberMat, Vector3.zero, Quaternion.identity, Vector3.one, layer, "Wood");
            Solid(group, "BridgePosts", posts, _logDarkMat, layer, "Wood");
            Hide(MeshObject(group, "BridgeRopes", ToMesh(ropes, DenseKey("bridgeropes")), _ropeMat, Vector3.zero, Quaternion.identity, Vector3.one,
                            layer, null, collider: false));
        }

        // ==================================================================
        // Ice caves
        // ==================================================================
        /// <summary>
        /// An ice cave: an arched tunnel through a mound of glacier ice, open at both ends. The vault is a grid
        /// of ice blocks, each pushed in or out a little (about 1,500 triangles inside, as many outside),
        /// with icicles hanging from the crown and the blocks heaped on it standing clear of the passage.
        /// </summary>
        private static void BuildIceCave(Transform parent, int layer, System.Random rng, Vector3 site)
        {
            EnsureIceMats();
            var at = new Vector3(site.x, GroundHeightAt(site.x, site.y), site.y);
            var rot = Quaternion.Euler(0f, site.z, 0f);
            const float len = 22f, rad = 3.8f;
            const int arcs = 26, runs = 24;

            var arch = new MeshBuild { UVScale = 0.28f };
            var outer = new MeshBuild { UVScale = 0.28f };

            Vector3 Inner(int i, int j)
            {
                float a = i * Mathf.PI / arcs;
                float z = -len * 0.5f + j * len / runs;
                float r = rad + Fbm2(i * 0.9f + 3f, j * 0.8f, 8201, 2) * 0.38f - ((i + j) % 2 == 0 ? 0.04f : 0f);
                return at + rot * new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 1.15f, z);
            }
            Vector3 Shell(int i, int j)
            {
                float a = i * Mathf.PI / arcs;
                float z = -len * 0.5f + j * len / runs;
                float r = rad + 2.4f + Fbm2(i * 0.6f, j * 0.55f + 9f, 8203, 3) * 1.1f;
                return at + rot * new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r * 1.15f, z);
            }
            var hub = at + Vector3.up * 1.5f;
            // The vault faces the passage: away from a point out in the rock, along the radius. The shell faces away from the axis.
            void Tris(MeshBuild b, System.Func<int, int, Vector3> P, bool towardsAxis)
            {
                for (int i = 0; i < arcs; i++)
                    for (int j = 0; j < runs; j++)
                    {
                        var a = P(i, j); var bb = P(i + 1, j); var c2 = P(i + 1, j + 1); var d2 = P(i, j + 1);
                        var cen = (a + bb + c2 + d2) * 0.25f;
                        var radial = new Vector3(cen.x - at.x, cen.y - at.y, cen.z - at.z); radial = rot * (Quaternion.Inverse(rot) * radial * 1f);
                        var from = towardsAxis ? cen + (cen - at).normalized * 10f : at + Vector3.up * 1.5f;
                        AddOutward(b, from, a, bb, c2);
                        AddOutward(b, from, a, c2, d2);
                    }
            }
            Tris(arch, Inner, true);
            Tris(outer, Shell, false);
            // The ring face at each mouth, between the inner arch and the outer shell, facing out along the axis.
            foreach (int j in new[] { 0, runs })
                for (int i = 0; i < arcs; i++)
                {
                    AddOutward(outer, hub, Inner(i, j), Inner(i + 1, j), Shell(i + 1, j));
                    AddOutward(outer, hub, Inner(i, j), Shell(i + 1, j), Shell(i, j));
                }

            var archGo = MeshObject(parent, "IceCave", ToMesh(arch, DenseKey("icecave")), _glacierMat, Vector3.zero, Quaternion.identity, Vector3.one, layer, IceTag);
            NoStanding(archGo);
            NoStanding(MeshObject(parent, "IceCaveShell", ToMesh(outer, DenseKey("icecaveshell")), _glacierMat, Vector3.zero, Quaternion.identity,
                                  Vector3.one, layer, IceTag));

            // Icicles from the crown; stalagmites on the floor edges.
            var icicles = new MeshBuild { UVScale = 0.3f };
            for (int j = 1; j < runs; j++)
                for (int i = 6; i < arcs - 5; i += 2)
                {
                    if (Hash01(j * 13, i) < 0.45f) continue;
                    var top = Inner(i, j);
                    icicles.Tube(top, top + Vector3.down * Rand(rng, 0.35f, 1.3f), 0.1f, 0.012f, 5);
                }
            NoStanding(MeshObject(parent, "Icicles", ToMesh(icicles, DenseKey("icicles")), _glacierMat, Vector3.zero, Quaternion.identity, Vector3.one,
                                  layer, null, collider: false));

            // Blocks heaped over and beside it, clear of the passage -- broken pieces now, not boulders.
            var heap = new MeshBuild { UVScale = 0.3f };
            for (int k = 0; k < 22; k++)
            {
                float z = Rand(rng, -len * 0.5f, len * 0.5f);
                float x = Rand(rng, -1f, 1f) * (rad + 4f) + Mathf.Sign(Rand(rng, -1f, 1f)) * 2f;
                float y = Mathf.Abs(x) < rad + 2.4f ? rad * 1.15f * Mathf.Sqrt(Mathf.Max(0.05f, 1f - (x / (rad + 2.4f)) * (x / (rad + 2.4f)))) + 2.2f : 0.4f;
                JaggedBlock(heap, at + rot * new Vector3(x, y, z), Vector3.one * Rand(rng, 1.2f, 2.6f) + new Vector3(0f, Rand(rng, -0.4f, 0.4f), 0f),
                            Quaternion.Euler(Rand(rng, -25f, 25f), Rand(rng, 0f, 360f), Rand(rng, -25f, 25f)), rng.Next(1, 99999));
            }
            NoStanding(MeshObject(parent, "IceBlocks", ToMesh(heap, DenseKey("iceheap")), _glacierMat, Vector3.zero, Quaternion.identity, Vector3.one,
                                  layer, IceTag, collider: false));

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

        // ==================================================================
        // The icefall
        // ==================================================================
        /// <summary>
        /// A frozen waterfall: a rock cliff with a curtain of ice down its face (a wavy grid, twenty-two
        /// columns by thirty-six rows), thick columns standing in front of it, icicles along the ledges, and a
        /// frozen pool at the foot with fissures and heaved plates.
        /// </summary>
        private static void BuildIcefall(Transform parent, int layer, System.Random rng)
        {
            EnsureIceMats();
            var p = _icefallSite;
            float ground = LowestGroundIn(p.x, p.y, 16f);
            const float w = 30f, h = 22f;

            var cliff = MeshObject(parent, "Icefall", ButteMesh(rng.Next(1, 999), sides: 36, levels: 24), _rockMat,
                                   new Vector3(p.x, ground - 1.5f, p.y), Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f),
                                   new Vector3(w * 0.5f, h, w * 0.5f), layer, _theme.wallTag);
            NoStanding(cliff);
            SealBox(parent, new Vector3(p.x, ground + h * 0.5f, p.y), new Vector3(w * 0.9f, h, w * 0.9f));

            var toCentre = (-p).normalized;
            var across = new Vector2(-toCentre.y, toCentre.x);
            var face = p + toCentre * (w * 0.46f);

            var ice = new MeshBuild { UVScale = 0.25f };
            // The curtain: a grid standing against the face, bulging where the water froze thickest.
            const int cols = 22, rowsN = 36;
            Vector3 C(int i, int j)
            {
                float u = i / (float)cols, v = j / (float)rowsN;
                float x = (u - 0.5f) * 13f;
                var q = face + across * x + toCentre * (0.5f + (Fbm2(x * 0.5f, v * 6f, 8301, 3) + 0.5f) * 2.4f * (0.4f + 0.6f * Mathf.Sin(u * Mathf.PI)));
                float y = Mathf.Lerp(ground, ground + h * 0.93f, v);
                return new Vector3(q.x, y, q.y);
            }
            var inside = new Vector3(p.x, ground + h * 0.5f, p.y);
            for (int i = 0; i < cols; i++)
                for (int j = 0; j < rowsN; j++)
                {
                    AddOutward(ice, inside, C(i, j), C(i + 1, j), C(i + 1, j + 1));
                    AddOutward(ice, inside, C(i, j), C(i + 1, j + 1), C(i, j + 1));
                }
            // Thick columns in front, each a stack of swelling tubes.
            for (int k = 0; k < 34; k++)
            {
                float x = Rand(rng, -6.5f, 6.5f);
                var q = face + across * x + toCentre * Rand(rng, 1.6f, 3.6f);
                float top = ground + h * Rand(rng, 0.55f, 0.97f);
                int segs = 7;
                for (int s = 0; s < segs; s++)
                {
                    float y0 = Mathf.Lerp(ground - 0.2f, top, s / (float)segs), y1 = Mathf.Lerp(ground - 0.2f, top, (s + 1) / (float)segs);
                    float r0 = (0.5f + Hash01(k, s) * 0.5f) * (1f - s / (float)segs * 0.7f), r1 = (0.5f + Hash01(k, s + 1) * 0.5f) * (1f - (s + 1) / (float)segs * 0.7f);
                    ice.Tube(new Vector3(q.x, y0, q.y), new Vector3(q.x, y1, q.y), r0, r1, 7);
                }
            }
            // Icicles hung from the ledges across the curtain.
            for (int k = 0; k < 90; k++)
            {
                float x = Rand(rng, -6.5f, 6.5f), v = Rand(rng, 0.25f, 0.95f);
                var q = face + across * x + toCentre * Rand(rng, 0.8f, 3.2f);
                var top = new Vector3(q.x, ground + h * v, q.y);
                ice.Tube(top, top + Vector3.down * Rand(rng, 0.6f, 2.8f), Rand(rng, 0.1f, 0.24f), 0.012f, 5);
            }
            NoStanding(MeshObject(parent, "FrozenFall", ToMesh(ice, DenseKey("frozenfall")), _glacierMat, Vector3.zero, Quaternion.identity, Vector3.one, layer, IceTag));

            // The pool: a grid disc with a humped surface, plates heaved at its edge.
            var poolAt = face + toCentre * 7f;
            float py = GroundHeightAt(poolAt.x, poolAt.y) + 0.06f;
            var pool = new MeshBuild { UVScale = 0.2f };
            const int pr = 10, ps = 40;
            Vector3 PP(int k, int i)
            {
                float t = i / (float)ps * Mathf.PI * 2f;
                float r = 7.5f * (1f + 0.09f * Mathf.Sin(t * 3f) + 0.05f * Mathf.Sin(t * 7f + 1f)) * (k / (float)pr);
                float x = poolAt.x + Mathf.Cos(t) * r, z = poolAt.y + Mathf.Sin(t) * r;
                return new Vector3(x, py + (Fbm2(x * 0.3f, z * 0.3f, 8311, 2) + 0.5f) * 0.18f * (1f - (k / (float)pr)), z);
            }
            for (int k = 0; k < pr; k++)
                for (int i = 0; i < ps; i++)
                    AddUp(pool, PP(k, i), PP(k, (i + 1) % ps), PP(k + 1, (i + 1) % ps), PP(k + 1, i));
            MeshObject(parent, "FrozenPool", ToMesh(pool, DenseKey("frozenpool")), _iceMat, Vector3.zero, Quaternion.identity, Vector3.one, layer, IceTag);

            var plates = new MeshBuild { UVScale = 0.3f };
            for (int k = 0; k < 16; k++)
            {
                float a = Rand(rng, 0f, Mathf.PI * 2f);
                float x = poolAt.x + Mathf.Cos(a) * 7.8f, z = poolAt.y + Mathf.Sin(a) * 7.8f;
                JaggedBlock(plates, new Vector3(x, GroundHeightAt(x, z) + 0.5f, z), new Vector3(Rand(rng, 1.2f, 2.8f), Rand(rng, 0.4f, 0.9f), Rand(rng, 0.9f, 2f)),
                            Quaternion.Euler(Rand(rng, 12f, 40f), Rand(rng, 0f, 360f), 0f), rng.Next(1, 99999));
            }
            NoStanding(MeshObject(parent, "PoolPlates", ToMesh(plates, DenseKey("poolplates")), _iceBlockMat, Vector3.zero, Quaternion.identity, Vector3.one,
                                  layer, IceTag));
        }

        // ==================================================================
        // The mountains
        // ==================================================================
        /// <summary>
        /// The horizon: a ring of massifs, each a grid of 64 by 64 cells (about 8,000 triangles) of ridged noise
        /// under a smooth envelope, split at a snowline into a rock mesh and a snow mesh, hazed by distance.
        /// They replace the smooth pale buttes that read as clouds of ice behind everything.
        /// </summary>
        private static void BuildSnowMountains(Transform group, int layer, System.Random rng)
        {
            var rockMats = new Material[3];
            var snowMats = new Material[3];
            for (int b = 0; b < 3; b++)
            {
                float haze = b / 2f;
                var rockCol = Color.Lerp(new Color(0.24f, 0.27f, 0.34f), _theme.fogColor, 0.04f + haze * 0.42f);
                var snowCol = Color.Lerp(new Color(0.94f, 0.96f, 1.00f), _theme.fogColor, haze * 0.35f);
                rockMats[b] = MakeDetailMaterial($"PeakRock{b}", rockCol, "Rock", 1f, 0.06f, 0f, 1.4f);
                snowMats[b] = MakeDetailMaterial($"PeakSnow{b}", snowCol, "Snow", 1f, 0.12f, 0f, 0.8f);
            }

            const int N = 64;
            int massifs = 22;
            for (int m = 0; m < massifs; m++)
            {
                float angle = m * 2.39996f + Rand(rng, -0.1f, 0.1f);
                float distance = Rand(rng, 880f, 1180f);
                float W = Rand(rng, 420f, 700f);
                float H = Rand(rng, 160f, 330f);
                int seed = rng.Next(1, 99999);
                float haze = Mathf.InverseLerp(880f, 1180f, distance);
                int bucket = Mathf.Min(2, Mathf.FloorToInt(haze * 3f));

                var rock = new MeshBuild { UVScale = 0.012f };
                var snow = new MeshBuild { UVScale = 0.012f };
                var grid = new Vector3[N + 1, N + 1];
                for (int i = 0; i <= N; i++)
                    for (int j = 0; j <= N; j++)
                    {
                        float u = i / (float)N - 0.5f, v = j / (float)N - 0.5f;
                        float x = u * W, z = v * W;
                        float env = Mathf.Clamp01(1f - (u * u + v * v) * 4f);
                        env = env * env * (3f - 2f * env);
                        float r = 0f, amp = 1f, f = 0.006f, norm = 0f;
                        for (int o = 0; o < 4; o++)
                        {
                            float n = 1f - Mathf.Abs(Fbm2(x * f + seed, z * f, seed + o, 1) * 2f);
                            r += n * n * amp; norm += amp; amp *= 0.5f; f *= 2.1f;
                        }
                        r /= norm;
                        float y = H * Mathf.Pow(env, 0.85f) * (0.3f + 0.7f * r) + Fbm2(x * 0.05f, z * 0.05f, seed + 9, 2) * 6f * env;
                        grid[i, j] = new Vector3(x, y, z);
                    }
                float snowline = H * 0.46f;
                for (int i = 0; i < N; i++)
                    for (int j = 0; j < N; j++)
                    {
                        var a = grid[i, j]; var b = grid[i, j + 1]; var c = grid[i + 1, j + 1]; var d = grid[i + 1, j];
                        float avg = (a.y + b.y + c.y + d.y) * 0.25f;
                        bool isSnow = avg > snowline + Mathf.Sin(a.x * 0.03f + a.z * 0.02f) * H * 0.05f;
                        // Steep faces shed snow: if the quad is steep it stays rock even above the line.
                        var n = Vector3.Cross(b - a, c - a).normalized;
                        if (Mathf.Abs(n.y) < 0.55f) isSnow = false;
                        AddUp(isSnow ? snow : rock, a, b, c, d);
                    }

                var pos = new Vector3(Mathf.Cos(angle) * distance, -H * 0.04f, Mathf.Sin(angle) * distance);
                var rot = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);
                MeshObject(group, "PeakRock", ToMesh(rock, DenseKey("peakrock")), rockMats[bucket], pos, rot, Vector3.one, layer, "Untagged", collider: false);
                MeshObject(group, "PeakSnow", ToMesh(snow, DenseKey("peaksnow")), snowMats[bucket], pos, rot, Vector3.one, layer, "Untagged", collider: false);
            }
        }
    }
}
#endif
