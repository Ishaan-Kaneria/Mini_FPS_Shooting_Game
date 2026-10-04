#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Filling Snowbound's empty ground with things (2026-10-04). Ishaan: "fill some spaces by not just blocks
    /// but some fencing or some objects". Four passes, last in the build so "empty" means empty after
    /// everything else has had its turn:
    ///
    /// <b>The village yards</b> are fenced with picket fences that follow the ground, on three sides of each
    /// lot, the front (where the door is) left open. <b>The outpost</b> gets barrel stacks, pallets, tarped
    /// crates, jersey barriers, floodlights and wire fence. <b>The open snow</b> gets drift fences, signposts,
    /// trail markers, hay bales, benches, picket runs, barrel stacks and the odd snowman. <b>The lake</b> gets
    /// ice-fishing huts with their holes, rods and sleds.
    ///
    /// Everything is welded into one mesh per kind per 70 m cell, placed where the physics scene says the
    /// ground is bare, and the solid kinds are kept off the bake (nothing here is anything to stand on).
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private struct LotRec { public Vector3 Origin; public float Yaw; public bool Skip; }
        private static readonly List<LotRec> _alpLots = new List<LotRec>();
        private static Frame _outFrame;
        private static bool _outFrameSet;
        private static Material _strawMat;

        private sealed class FillSet
        {
            public readonly Dictionary<(int kind, Vector2Int cell), MeshBuild> Chunks = new Dictionary<(int, Vector2Int), MeshBuild>();
            public MeshBuild B(int kind, Vector3 p)
            {
                var key = (kind, new Vector2Int(Mathf.FloorToInt(p.x / 70f), Mathf.FloorToInt(p.z / 70f)));
                if (!Chunks.TryGetValue(key, out var b)) b = Chunks[key] = new MeshBuild { UVScale = 0.5f };
                return b;
            }
            public int Props;
        }

        private const int KWood = 0, KDark = 1, KIron = 2, KStraw = 3, KConcrete = 4, KTarp = 5, KSnow = 6, KOrange = 7;

        // ==================================================================
        // Entry
        // ==================================================================
        private static void BuildSnowFill(Transform parent, int layer, System.Random rng, float half)
        {
            if (!_theme.snowZone) return;
            _strawMat = MakeDetailMaterial("AlpStraw", new Color(0.74f, 0.62f, 0.30f), "Timber", 1.1f, 0.05f, 0f, 1.0f);

            var group = new GameObject("Fill").transform;
            group.SetParent(parent, false);
            _fillRoot = group;
            _fillLayer = layer;
            var set = new FillSet();

            int fenced = FillVillageYards(set, rng);
            int outpostProps = FillOutpost(set, rng);
            int open = FillOpenSnow(set, rng, half);

            foreach (var kv in set.Chunks)
            {
                if (kv.Value.Triangles.Count == 0) continue;
                switch (kv.Key.kind)
                {
                    case KWood: Solid(group, "FillWood", kv.Value, _plankMat, layer, "Wood"); break;
                    case KDark: Solid(group, "FillTimber", kv.Value, _logDarkMat, layer, "Wood"); break;
                    case KIron: Solid(group, "FillIron", kv.Value, _ironMat, layer, "Metal"); break;
                    case KStraw: Solid(group, "FillStraw", kv.Value, _strawMat, layer, "Wood"); break;
                    case KConcrete: Solid(group, "FillConcrete", kv.Value, _snowStoneMat, layer, "Concrete"); break;
                    case KTarp: Solid(group, "FillTarp", kv.Value, _tentPaints[(kv.Key.cell.x * 7 + kv.Key.cell.y * 3 & 0x7fffffff) % _tentPaints.Length], layer, "Wood"); break;
                    case KOrange: Solid(group, "FillMarkers", kv.Value, _moduleOrange, layer, "Metal"); break;
                    case KSnow: SnowCap(group, "FillSnow", kv.Value, _pineSnowMat, layer); break;
                }
            }

            FillLake(group, layer, rng);
            Debug.Log($"[FPSKit] snow fill: {fenced} fenced yard(s), {outpostProps} outpost prop(s), {open} open-snow prop(s), {set.Props} in all.");
        }

        // ==================================================================
        // Parts
        // ==================================================================
        /// <summary>
        /// A picket fence between two world points: posts with caps every 2.4 m, two rails, pointed pickets every
        /// 16 cm, every one of them stood on the ground under it. A gap of <paramref name="gate"/> metres is left
        /// at <paramref name="gateAt"/> along it (negative for none).
        /// </summary>
        private static void PicketRun(FillSet set, Vector3 a, Vector3 b, float gateAt = -1f, float gate = 2.6f)
        {
            var d = b - a; d.y = 0f;
            float len = d.magnitude;
            if (len < 1f) return;
            var dir = d / len;
            var rot = Quaternion.LookRotation(dir);
            var wood = set.B(KWood, a);
            var posts = set.B(KDark, a);

            bool InGate(float s) => gateAt >= 0f && Mathf.Abs(s - gateAt) < gate * 0.5f;
            int np = Mathf.Max(2, Mathf.CeilToInt(len / 2.4f) + 1);
            for (int i = 0; i < np; i++)
            {
                float s = len * i / (np - 1);
                if (InGate(s) && !(i == 0 || i == np - 1)) continue;
                var p = a + dir * s; p.y = GroundHeightAt(p.x, p.z);
                posts.Box(p + Vector3.up * 0.65f, new Vector3(0.14f, 1.4f, 0.14f), rot);
                posts.Box(p + Vector3.up * 1.42f, new Vector3(0.2f, 0.06f, 0.2f), rot);
            }
            for (float s = 0.1f; s < len - 0.05f; s += 0.16f)
            {
                if (InGate(s)) continue;
                var p = a + dir * s; p.y = GroundHeightAt(p.x, p.z);
                wood.Box(p + Vector3.up * 0.55f, new Vector3(0.03f, 1.0f, 0.09f), rot);
                // The point.
                var tip = p + Vector3.up * 1.06f;
                var cross = rot * Vector3.right * 0.045f;
                var thick = rot * Vector3.forward * 0.015f;
                var apex = p + Vector3.up * 1.16f;
                var ctr = p + Vector3.up * 0.9f;
                AddOutward(wood, ctr, tip - cross - thick, tip + cross - thick, apex);
                AddOutward(wood, ctr, tip + cross - thick, tip + cross + thick, apex);
                AddOutward(wood, ctr, tip + cross + thick, tip - cross + thick, apex);
                AddOutward(wood, ctr, tip - cross + thick, tip - cross - thick, apex);
            }
            // Rails follow the ground in 1.2 m pieces.
            for (float s = 0f; s < len; s += 1.2f)
            {
                float s1 = Mathf.Min(len, s + 1.2f);
                var m = a + dir * ((s + s1) * 0.5f);
                if (InGate((s + s1) * 0.5f)) continue;
                float g = GroundHeightAt(m.x, m.z);
                foreach (float h in new[] { 0.3f, 0.82f })
                    posts.Box(new Vector3(m.x, g + h, m.z), new Vector3(0.05f, 0.08f, s1 - s + 0.02f), rot);
            }
        }

        /// <summary>A snow fence: slatted panels leaning on posts, in a line across the wind.</summary>
        private static void SnowFenceRun(FillSet set, Vector3 a, Vector3 b)
        {
            var d = b - a; d.y = 0f;
            float len = d.magnitude;
            if (len < 3f) return;
            var dir = d / len;
            var rot = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, 0f);
            var wood = set.B(KWood, a);
            var posts = set.B(KDark, a);
            for (float s = 0f; s <= len + 0.01f; s += 3f)
            {
                var p = a + dir * s; p.y = GroundHeightAt(p.x, p.z);
                posts.Box(p + Vector3.up * 0.8f, new Vector3(0.1f, 1.8f, 0.1f), rot * Quaternion.Euler(8f, 0f, 0f));
                posts.Box(p + Vector3.up * 0.2f + dir * 0.0f, new Vector3(0.14f, 0.3f, 0.14f), rot);
                if (s + 3f <= len + 0.01f)
                    for (int k = 0; k < 9; k++)
                    {
                        var m = p + dir * 1.5f; m.y = GroundHeightAt(m.x, m.z);
                        wood.Box(m + Vector3.up * (0.3f + k * 0.15f), new Vector3(0.025f, 0.1f, 2.96f), rot);
                    }
            }
        }

        private static void WireRun(FillSet set, Vector3 a, Vector3 b)
        {
            var d = b - a; d.y = 0f;
            float len = d.magnitude;
            if (len < 3f) return;
            var dir = d / len;
            var posts = set.B(KDark, a);
            var wire = set.B(KIron, a);
            int np = Mathf.CeilToInt(len / 3f) + 1;
            Vector3 P(int i, float h) { var p = a + dir * (len * i / (np - 1)); return new Vector3(p.x, GroundHeightAt(p.x, p.z) + h, p.z); }
            for (int i = 0; i < np; i++) posts.Tube(P(i, -0.3f), P(i, 1.9f), 0.07f, 0.06f, 6);
            for (int i = 0; i + 1 < np; i++)
                foreach (float h in new[] { 0.35f, 0.8f, 1.25f, 1.7f })
                {
                    var p0 = P(i, h); var p1 = P(i + 1, h);
                    var mid = (p0 + p1) * 0.5f + Vector3.down * 0.08f;
                    wire.Tube(p0, mid, 0.012f, 0.012f, 3); wire.Tube(mid, p1, 0.012f, 0.012f, 3);
                }
        }

        private static void BarrelStack(FillSet set, Vector3 at, float yaw)
        {
            var iron = set.B(KIron, at);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            void Barrel(Vector3 p)
            {
                iron.Tube(p, p + Vector3.up * 0.95f, 0.3f, 0.3f, 14);
                foreach (float y in new[] { 0.12f, 0.47f, 0.82f }) iron.Tube(p + Vector3.up * y, p + Vector3.up * (y + 0.05f), 0.315f, 0.315f, 14);
                iron.Tube(p + Vector3.up * 0.95f, p + Vector3.up * 0.97f, 0.26f, 0.2f, 14);
            }
            float g = GroundHeightAt(at.x, at.z);
            for (int i = 0; i < 3; i++) Barrel(new Vector3(at.x, g, at.z) + rot * new Vector3((i - 1) * 0.64f, 0f, 0f));
            for (int i = 0; i < 2; i++) Barrel(new Vector3(at.x, g + 0.95f, at.z) + rot * new Vector3((i - 0.5f) * 0.64f, 0f, 0f) + rot * new Vector3(0f, 0f, 0.02f));
        }

        private static void PalletStack(FillSet set, Vector3 at, float yaw, int count)
        {
            var wood = set.B(KWood, at);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            float g = GroundHeightAt(at.x, at.z);
            for (int n = 0; n < count; n++)
            {
                float y = g + n * 0.15f;
                foreach (float z in new[] { -0.5f, 0f, 0.5f })
                {
                    wood.Box(new Vector3(at.x, y + 0.13f, at.z) + rot * new Vector3(0f, 0f, z), new Vector3(1.2f, 0.03f, 0.1f), rot);
                    wood.Box(new Vector3(at.x, y + 0.01f, at.z) + rot * new Vector3(0f, 0f, z), new Vector3(1.2f, 0.03f, 0.1f), rot);
                    wood.Box(new Vector3(at.x, y + 0.07f, at.z) + rot * new Vector3(-0.5f, 0f, z), new Vector3(0.12f, 0.09f, 0.1f), rot);
                    wood.Box(new Vector3(at.x, y + 0.07f, at.z) + rot * new Vector3(0.5f, 0f, z), new Vector3(0.12f, 0.09f, 0.1f), rot);
                }
                for (int x = 0; x < 6; x++)
                    wood.Box(new Vector3(at.x, y + 0.15f, at.z) + rot * new Vector3(-0.5f + x * 0.2f, 0f, 0f), new Vector3(0.1f, 0.03f, 1.1f), rot);
            }
        }

        private static void TarpedCrates(FillSet set, Vector3 at, float yaw, System.Random rng)
        {
            var wood = set.B(KWood, at);
            var tarp = set.B(KTarp, at);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            float g = GroundHeightAt(at.x, at.z);
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 2; j++)
                    wood.Box(new Vector3(at.x, g + 0.5f, at.z) + rot * new Vector3((i - 1) * 1.05f, 0f, (j - 0.5f) * 1.05f), new Vector3(1.0f, 1.0f, 1.0f), rot);
            wood.Box(new Vector3(at.x, g + 1.5f, at.z) + rot * new Vector3(0f, 0f, 0f), new Vector3(1.0f, 1.0f, 1.0f), rot);
            tarp.SoftBox(new Vector3(at.x, g + 1.25f, at.z), new Vector3(3.5f, 2.55f, 2.4f), rot, rng.Next(999), 0.35f, 5, 0.1f);
        }

        private static void HayBales(FillSet set, Vector3 at, float yaw, System.Random rng)
        {
            var straw = set.B(KStraw, at);
            var snow = set.B(KSnow, at);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            float g = GroundHeightAt(at.x, at.z);
            int rows = 2 + rng.Next(2);
            for (int r = 0; r < rows; r++)
                for (int i = 0; i < 3 - r; i++)
                {
                    var p = new Vector3(at.x, g + 0.4f + r * 0.78f, at.z) + rot * new Vector3((i - (2 - r) * 0.5f) * 1.5f, 0f, 0f);
                    straw.SoftBox(p, new Vector3(1.4f, 0.75f, 0.95f), rot, rng.Next(999), 0.12f, 4, 0.03f);
                    for (int k = 0; k < 3; k++)
                        straw.Box(p + rot * new Vector3(0f, 0.02f, (k - 1) * 0.3f), new Vector3(1.43f, 0.02f, 0.03f), rot);
                }
            snow.SoftBox(new Vector3(at.x, g + 0.4f + (rows - 1) * 0.78f + 0.43f, at.z), new Vector3(3.2f - rows * 0.7f, 0.1f, 1.0f), rot, rng.Next(999), 0.05f, 3, 0.03f);
        }

        private static void Bench(FillSet set, Vector3 at, float yaw)
        {
            var wood = set.B(KWood, at);
            var dark = set.B(KDark, at);
            var snow = set.B(KSnow, at);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            float g = GroundHeightAt(at.x, at.z);
            var c = new Vector3(at.x, g, at.z);
            for (int i = 0; i < 4; i++) wood.Box(c + rot * new Vector3(0f, 0.46f, -0.22f + i * 0.14f), new Vector3(1.7f, 0.04f, 0.12f), rot);
            for (int i = 0; i < 3; i++) wood.Box(c + rot * new Vector3(0f, 0.72f + i * 0.14f, -0.3f), new Vector3(1.7f, 0.1f, 0.03f), rot * Quaternion.Euler(-8f, 0f, 0f));
            foreach (float s in new[] { -0.75f, 0.75f })
            {
                dark.Box(c + rot * new Vector3(s, 0.22f, 0f), new Vector3(0.06f, 0.44f, 0.5f), rot);
                dark.Box(c + rot * new Vector3(s, 0.62f, -0.3f), new Vector3(0.06f, 0.95f, 0.05f), rot * Quaternion.Euler(-8f, 0f, 0f));
            }
            snow.SoftBox(c + rot * new Vector3(0f, 0.5f, 0.0f), new Vector3(1.5f, 0.08f, 0.4f), rot, 3, 0.04f, 3, 0.03f);
        }

        private static void Signpost(FillSet set, Vector3 at, float yaw, System.Random rng)
        {
            var dark = set.B(KDark, at);
            var wood = set.B(KWood, at);
            float g = GroundHeightAt(at.x, at.z);
            var c = new Vector3(at.x, g, at.z);
            dark.Tube(c + Vector3.down * 0.4f, c + Vector3.up * 3.0f, 0.1f, 0.08f, 8);
            dark.Tube(c + Vector3.up * 3.0f, c + Vector3.up * 3.1f, 0.14f, 0.14f, 8);
            for (int i = 0; i < 3; i++)
            {
                float a = yaw + i * 110f + Rand(rng, -20f, 20f);
                var rot = Quaternion.Euler(0f, a, 0f);
                var b = c + Vector3.up * (2.65f - i * 0.5f);
                wood.Box(b + rot * new Vector3(0.65f, 0f, 0.1f), new Vector3(1.3f, 0.3f, 0.04f), rot);
                // The arrow tip.
                var tip = b + rot * new Vector3(1.4f, 0f, 0.1f);
                AddOutward(wood, b + rot * new Vector3(1f, 0f, 0.1f), b + rot * new Vector3(1.3f, 0.15f, 0.12f), b + rot * new Vector3(1.3f, -0.15f, 0.12f), tip + rot * new Vector3(0.1f, 0f, 0.02f));
                AddOutward(wood, b + rot * new Vector3(1f, 0f, 0.1f), b + rot * new Vector3(1.3f, -0.15f, 0.08f), b + rot * new Vector3(1.3f, 0.15f, 0.08f), tip + rot * new Vector3(0.1f, 0f, 0.02f));
            }
        }

        private static void TrailMarker(FillSet set, Vector3 at)
        {
            var dark = set.B(KDark, at);
            var orange = set.B(KOrange, at);
            float g = GroundHeightAt(at.x, at.z);
            var c = new Vector3(at.x, g, at.z);
            dark.Tube(c + Vector3.down * 0.3f, c + Vector3.up * 3.2f, 0.045f, 0.035f, 7);
            orange.Tube(c + Vector3.up * 2.5f, c + Vector3.up * 3.3f, 0.06f, 0.05f, 7);
            orange.Box(c + Vector3.up * 3.4f, new Vector3(0.38f, 0.28f, 0.03f), Quaternion.identity);
            for (int i = 0; i < 4; i++) orange.Tube(c + Vector3.up * (1.2f + i * 0.2f), c + Vector3.up * (1.28f + i * 0.2f), 0.055f, 0.055f, 7);
        }

        private static void Snowman(Transform parent, int layer, System.Random rng, Vector3 at)
        {
            float g = GroundHeightAt(at.x, at.z);
            var c = new Vector3(at.x, g, at.z);
            float k = Rand(rng, 0.85f, 1.2f);
            Vector3 S(float r, float y) => c + Vector3.up * y * k;
            void Ball(string n, float r, float y)
            {
                var go = MeshObject(parent, n, BoulderMesh(rng.Next(1, 20), 0.05f, 1f), _pineSnowMat, c + Vector3.up * (y * k), Quaternion.identity,
                                    Vector3.one * (r * k / 1.35f), layer, "Snow");
                NoStanding(go);
            }
            Ball("SnowmanBase", 0.62f, 0.5f); Ball("SnowmanBody", 0.46f, 1.28f); Ball("SnowmanHead", 0.32f, 1.9f);
            var bits = new MeshBuild { UVScale = 0.5f };
            var face = Quaternion.Euler(0f, Rand(rng, 0f, 360f), 0f);
            bits.Tube(c + Vector3.up * 1.9f * k + face * new Vector3(0f, -0.02f, 0.3f * k), c + Vector3.up * 1.9f * k + face * new Vector3(0f, -0.05f, 0.62f * k), 0.045f, 0.01f, 6);   // carrot
            foreach (float s in new[] { -0.11f, 0.11f }) bits.Tube(c + Vector3.up * 1.98f * k + face * new Vector3(s * k, 0f, 0.29f * k), c + Vector3.up * 1.98f * k + face * new Vector3(s * k, 0f, 0.32f * k), 0.03f, 0.03f, 6);
            for (int i = 0; i < 3; i++) bits.Tube(c + Vector3.up * (1.35f - i * 0.2f) * k + face * new Vector3(0f, 0f, 0.43f * k), c + Vector3.up * (1.35f - i * 0.2f) * k + face * new Vector3(0f, 0f, 0.46f * k), 0.035f, 0.035f, 6);
            bits.Tube(c + Vector3.up * 2.2f * k, c + Vector3.up * 2.45f * k, 0.22f * k, 0.22f * k, 10);                                                                                    // hat
            bits.Tube(c + Vector3.up * 2.18f * k, c + Vector3.up * 2.2f * k, 0.34f * k, 0.34f * k, 10);
            foreach (float s in new[] { -1f, 1f }) bits.Tube(c + Vector3.up * 1.4f * k + face * new Vector3(s * 0.4f * k, 0f, 0f), c + Vector3.up * 1.7f * k + face * new Vector3(s * 0.95f * k, 0f, 0.1f), 0.025f, 0.012f, 5);
            var go2 = MeshObject(parent, "SnowmanBits", ToMesh(bits, DenseKey("snowmanbits")), _logDarkMat, Vector3.zero, Quaternion.identity, Vector3.one, layer, null, collider: false);
            NoStanding(go2);
        }

        private static void JerseyBarriers(FillSet set, Vector3 from, Vector3 to)
        {
            var d = to - from; d.y = 0f;
            float len = d.magnitude;
            var dir = d / len;
            var rot = Quaternion.LookRotation(dir);
            var concrete = set.B(KConcrete, from);
            for (float s = 0f; s + 3f <= len; s += 3.05f)
            {
                var c = from + dir * (s + 1.5f); c.y = GroundHeightAt(c.x, c.z);
                // The profile: wide foot, a slope, a steep face, a narrow top.
                var prof = new[] { new Vector2(-0.3f, 0f), new Vector2(0.3f, 0f), new Vector2(0.3f, 0.2f), new Vector2(0.12f, 0.75f), new Vector2(-0.12f, 0.75f), new Vector2(-0.3f, 0.2f) };
                var inside = c + Vector3.up * 0.35f;
                Vector3 P(int i, float z) => c + rot * new Vector3(prof[i].x, prof[i].y, z);
                for (int i = 0; i < prof.Length; i++)
                {
                    int j = (i + 1) % prof.Length;
                    AddOutward(concrete, inside, P(i, -1.5f), P(j, -1.5f), P(j, 1.5f));
                    AddOutward(concrete, inside, P(i, -1.5f), P(j, 1.5f), P(i, 1.5f));
                }
                for (int i = 1; i < prof.Length - 1; i++)
                {
                    AddOutward(concrete, inside, P(0, -1.5f), P(i, -1.5f), P(i + 1, -1.5f));
                    AddOutward(concrete, inside, P(0, 1.5f), P(i, 1.5f), P(i + 1, 1.5f));
                }
            }
        }

        private static void Floodlight(FillSet set, Vector3 at, float yaw)
        {
            var iron = set.B(KIron, at);
            var orange = set.B(KOrange, at);
            float g = GroundHeightAt(at.x, at.z);
            var c = new Vector3(at.x, g, at.z);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            iron.Tube(c + Vector3.down * 0.3f, c + Vector3.up * 7.5f, 0.14f, 0.09f, 8);
            iron.Tube(c + Vector3.down * 0.3f, c + Vector3.up * 0.6f, 0.26f, 0.2f, 8);
            iron.Box(c + Vector3.up * 7.55f + rot * new Vector3(0f, 0f, 0.5f), new Vector3(2.0f, 0.12f, 0.12f), rot);
            for (int i = 0; i < 4; i++)
                orange.Box(c + Vector3.up * 7.35f + rot * new Vector3(-0.75f + i * 0.5f, 0f, 0.55f), new Vector3(0.4f, 0.26f, 0.2f), rot * Quaternion.Euler(24f, 0f, 0f));
        }

        // ==================================================================
        // The passes
        // ==================================================================
        private static int FillVillageYards(FillSet set, System.Random rng)
        {
            if (!_hasAlpVillage) return 0;
            int yards = 0;
            const float lot = 18f;
            foreach (var l in _alpLots)
            {
                if (l.Skip) continue;
                var f = new Frame(l.Origin, l.Yaw);
                float e = lot * 0.5f - 0.35f;
                // Three sides: back, left, right; the front (-v, the door side) is left open with its gate gap.
                Vector3 W(float u, float v) { var p = f.P(u, 0f, v); return new Vector3(p.x, 0f, p.z); }
                PicketRun(set, W(-e, e), W(e, e), gateAt: rng.NextDouble() < 0.5 ? lot * 0.5f : -1f);
                PicketRun(set, W(-e, -e + 4.5f), W(-e, e));
                PicketRun(set, W(e, -e + 4.5f), W(e, e));
                // Short returns either side of the front, leaving the middle open.
                PicketRun(set, W(-e, -e + 4.5f), W(-e + 3.2f, -e + 4.5f));
                PicketRun(set, W(e - 3.2f, -e + 4.5f), W(e, -e + 4.5f));
                yards++;
                // A few things in the yard, where the physics says it is empty.
                for (int k = 0; k < 7; k++)
                {
                    var p = f.P(Rand(rng, -e + 1.5f, e - 1.5f), 0f, Rand(rng, -e + 1.5f, e - 1.5f));
                    if (!BareSnow(new Vector2(p.x, p.z), 1.6f)) continue;
                    double kind = rng.NextDouble();
                    var at = new Vector3(p.x, 0f, p.z);
                    if (kind < 0.22) BarrelStack(set, at, Rand(rng, 0f, 360f));
                    else if (kind < 0.42) HayBales(set, at, Rand(rng, 0f, 360f), rng);
                    else if (kind < 0.60) Bench(set, at, Rand(rng, 0f, 360f));
                    else if (kind < 0.76) TarpedCrates(set, at, Rand(rng, 0f, 360f), rng);
                    else if (kind < 0.90) PalletStack(set, at, Rand(rng, 0f, 360f), 2 + rng.Next(4));
                    else Signpost(set, at, Rand(rng, 0f, 360f), rng);
                    set.Props++;
                }
            }
            return yards;
        }

        private static int FillOutpost(FillSet set, System.Random rng)
        {
            if (!_hasOutpost || !_outFrameSet) return 0;
            var f = _outFrame;
            float K = OutK;
            float hu = 37.5f * K, hv = 26.5f * K;
            int n = 0;

            // A wire fence round a secure corner, jersey barriers across the lane mouths, floodlights down the lanes.
            WireRun(set, new Vector3(f.P(hu - 24f, 0f, hv - 6f).x, 0f, f.P(hu - 24f, 0f, hv - 6f).z), new Vector3(f.P(hu - 4f, 0f, hv - 6f).x, 0f, f.P(hu - 4f, 0f, hv - 6f).z));
            WireRun(set, new Vector3(f.P(hu - 4f, 0f, hv - 6f).x, 0f, f.P(hu - 4f, 0f, hv - 6f).z), new Vector3(f.P(hu - 4f, 0f, 2f).x, 0f, f.P(hu - 4f, 0f, 2f).z));
            var gateIn = f.P(-12f * K, 0f, -hv + 6f);
            JerseyBarriers(set, f.P(-12f * K - 9f, 0f, -hv + 7f), f.P(-12f * K - 3.5f, 0f, -hv + 7f));
            JerseyBarriers(set, f.P(-12f * K + 3.5f, 0f, -hv + 7f), f.P(-12f * K + 9f, 0f, -hv + 7f));
            _ = gateIn;
            foreach (var p in new[] { f.P(-12f * K - 6f, 0f, -hv + 12f), f.P(-12f * K + 6f, 0f, 2f), f.P(hu - 12f, 0f, -10f * K + 6f), f.P(14f * K, 0f, -4f * K), f.P(-6f * K, 0f, 12f * K) })
            { Floodlight(set, p, f.Yaw + Rand(rng, 0f, 360f)); n++; }

            for (int i = 0; i < 260; i++)
            {
                float u = Rand(rng, -hu + 3f, hu - 3f), v = Rand(rng, -hv + 3f, hv - 3f);
                // Keep both gate lanes clear.
                if (Mathf.Abs(u - (-12f * K)) < 5.5f && v < 0f) continue;
                if (Mathf.Abs(v - (-10f * K)) < 5.5f && u > 0f) continue;
                var w = f.P(u, 0f, v);
                if (!BareSnow(new Vector2(w.x, w.z), 2.2f)) continue;
                var at = new Vector3(w.x, 0f, w.z);
                double kind = rng.NextDouble();
                if (kind < 0.26) BarrelStack(set, at, f.Yaw + Rand(rng, 0f, 360f));
                else if (kind < 0.50) PalletStack(set, at, f.Yaw + Rand(rng, 0f, 4f) * 90f, 3 + rng.Next(5));
                else if (kind < 0.72) TarpedCrates(set, at, f.Yaw + Rand(rng, 0f, 4f) * 90f, rng);
                else if (kind < 0.86) JerseyBarriers(set, at, at + f.Axis(Vector3.right) * 6.2f);
                else if (kind < 0.94) PicketRun(set, at, at + f.Axis(Vector3.forward) * 8f);
                else Bench(set, at, f.Yaw + Rand(rng, 0f, 4f) * 90f);
                n++; set.Props++;
                if (n > 70) break;
            }
            return n;
        }

        private static int FillOpenSnow(FillSet set, System.Random rng, float half)
        {
            int n = 0;
            var wild = new GameObject("FillSnowmen").transform;
            wild.SetParent(_fillRoot, false);

            for (int i = 0; i < 1800 && n < 240; i++)
            {
                var p = new Vector2(Rand(rng, -half + 26f, half - 26f), Rand(rng, -half + 26f, half - 26f));
                if (p.magnitude < 26f || InCrevasse(p.x, p.y, 8f)) continue;
                if (!Free(p, 3f) || !BareSnow(p, 2.2f)) continue;
                var at = new Vector3(p.x, 0f, p.y);
                double kind = rng.NextDouble();
                float yaw = Rand(rng, 0f, 360f);
                Vector3 End(float length, float angle) => at + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * length;
                float ang = Rand(rng, 0f, Mathf.PI * 2f);

                if (kind < 0.16) { SnowFenceRun(set, at, End(Rand(rng, 12f, 27f), ang)); Claim(p.x, p.y, 4f); }
                else if (kind < 0.30) { PicketRun(set, at, End(Rand(rng, 6f, 14f), ang)); Claim(p.x, p.y, 3f); }
                else if (kind < 0.40) { WireRun(set, at, End(Rand(rng, 9f, 21f), ang)); Claim(p.x, p.y, 3f); }
                else if (kind < 0.52) TrailMarker(set, at);
                else if (kind < 0.60) Signpost(set, at, yaw, rng);
                else if (kind < 0.70) HayBales(set, at, yaw, rng);
                else if (kind < 0.77) Bench(set, at, yaw);
                else if (kind < 0.84) BarrelStack(set, at, yaw);
                else if (kind < 0.90) TarpedCrates(set, at, yaw, rng);
                else if (kind < 0.94) PalletStack(set, at, yaw, 2 + rng.Next(4));
                else Snowman(wild, _fillLayer, rng, at);
                Claim(p.x, p.y, 2.4f);
                n++; set.Props++;
            }
            return n;
        }

        private static Transform _fillRoot;
        private static int _fillLayer;

        /// <summary>Ice-fishing huts on the lake, each with its holes, a rod, a bucket and a sled.</summary>
        private static void FillLake(Transform group, int layer, System.Random rng)
        {
            var lake = new GameObject("LakeHuts").transform;
            lake.SetParent(group, false);
            float surface = GroundHeightAt(_lakeAt.x, _lakeAt.y) + 0.08f;
            int huts = 4;
            for (int i = 0; i < huts; i++)
            {
                float a = i * Mathf.PI * 2f / huts + Rand(rng, -0.4f, 0.4f);
                float r = _lakeRadius * Rand(rng, 0.25f, 0.6f);
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                var c = new Vector3(_lakeAt.x + x, surface + LakeBump(x, z), _lakeAt.y + z);
                var f = new Frame(c, Rand(rng, 0f, 4f) * 90f);

                var walls = new MeshBuild { UVScale = 0.4f };
                var roof = new MeshBuild { UVScale = 0.4f };
                var dark = new MeshBuild { UVScale = 0.4f };
                const float w = 3.4f, d = 2.8f, h = 2.3f;
                WallRun(walls, f, new Vector2(-w * 0.5f, -d * 0.5f), new Vector2(w * 0.5f, -d * 0.5f), 0f, h, 0.1f, new List<Opening> { new Opening(w * 0.5f, 0.9f, 0f, 1.9f) });
                WallRun(walls, f, new Vector2(-w * 0.5f, d * 0.5f), new Vector2(w * 0.5f, d * 0.5f), 0f, h, 0.1f, new List<Opening> { new Opening(w * 0.5f, 1.0f, 1.0f, 1.8f) });
                WallRun(walls, f, new Vector2(-w * 0.5f, -d * 0.5f), new Vector2(-w * 0.5f, d * 0.5f), 0f, h, 0.1f);
                WallRun(walls, f, new Vector2(w * 0.5f, -d * 0.5f), new Vector2(w * 0.5f, d * 0.5f), 0f, h, 0.1f);
                // A lean-to roof sloping to the back, with a stovepipe.
                for (int k = 0; k < 8; k++)
                    roof.Box(f.P(0f, h + 0.18f - (k - 3.5f) * 0.045f + 0.15f, -d * 0.5f + (k + 0.5f) * (d + 0.5f) / 8f - 0.1f), new Vector3(w + 0.5f, 0.05f, (d + 0.5f) / 8f * 1.1f), f.Rot * Quaternion.Euler(-7f, 0f, 0f));
                dark.Tube(f.P(0.9f, h, 0.3f), f.P(0.9f, h + 1.1f, 0.3f), 0.07f, 0.07f, 8);
                // Holes in the ice, a rod across one, a bucket.
                for (int k = 0; k < 3; k++)
                {
                    var hp = f.P(Rand(rng, -1.2f, 1.2f), 0f, -d * 0.5f - 1.1f - k * 0.9f);
                    dark.Tube(hp + Vector3.up * 0.02f, hp + Vector3.up * 0.045f, 0.21f, 0.21f, 12);
                }
                var hp0 = f.P(1.6f, 0f, -d * 0.5f - 1.3f);
                dark.Tube(hp0 + Vector3.up * 0.3f, hp0 + Vector3.up * 0.3f + f.Axis(new Vector3(-1.2f, 0.4f, 0.4f)), 0.012f, 0.008f, 4);
                dark.Tube(hp0, hp0 + Vector3.up * 0.32f, 0.16f, 0.2f, 10);
                var ws = Solid(lake, "IceHutWalls", walls, _plankMat, layer, "Wood");
                _ = ws;
                Solid(lake, "IceHutRoof", roof, _shingleMat, layer, "Wood");
                SnowCap(lake, "IceHutDetail", dark, _ironMat, layer);
                var sled = new MeshBuild { UVScale = 0.5f };
                Sled(sled, f.P(2.8f, 0f, 0f), f.Yaw + 90f);
                Solid(lake, "IceHutSled", sled, _plankMat, layer, "Wood");
                SealBox(lake, c + Vector3.up * 1.2f, new Vector3(w + 1f, 2.4f, w + 1f));
            }
        }
    }
}
#endif
