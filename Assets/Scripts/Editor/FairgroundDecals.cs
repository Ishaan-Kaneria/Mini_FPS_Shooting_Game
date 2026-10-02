#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// URP decals for the Abandoned Fairground: cracks, potholes, oil, mud splashes, leaves, worn queue arrows and
    /// flood lines on walls. The textures are generated here (grayscale-free, alpha-driven), so there is nothing to
    /// download and the look can be retuned in code. Needs the DecalRendererFeature on the renderer (Screen Space
    /// technique: our own shaders do not write the DBuffer, but Screen Space draws onto any opaque surface).
    /// </summary>
    public static class FairgroundDecals
    {
        const string Dir = "Assets/Fairground/Decals";
        const int N = 256;

        static System.Random rng;
        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        // ------------------------------------------------------------------ textures

        static Color32[] Blank() { var c = new Color32[N * N]; return c; }

        static void Dot(Color32[] px, float cx, float cy, float r, Color col, float alpha)
        {
            int x0 = Mathf.Max(0, (int)(cx - r - 1)), x1 = Mathf.Min(N - 1, (int)(cx + r + 1));
            int y0 = Mathf.Max(0, (int)(cy - r - 1)), y1 = Mathf.Min(N - 1, (int)(cy + r + 1));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d > r) continue;
                    float a = alpha * Mathf.SmoothStep(1f, 0.55f, d / r);
                    var p = px[y * N + x];
                    float na = Mathf.Max(p.a / 255f, a);
                    px[y * N + x] = new Color32((byte)(col.r * 255), (byte)(col.g * 255), (byte)(col.b * 255), (byte)(na * 255));
                }
        }

        static void Line(Color32[] px, Vector2 a, Vector2 b, float r, Color col, float alpha)
        {
            float len = Vector2.Distance(a, b);
            for (float t = 0; t <= len; t += 0.7f) Dot(px, Mathf.Lerp(a.x, b.x, t / len), Mathf.Lerp(a.y, b.y, t / len), r, col, alpha);
        }

        static Texture2D Save(string name, Color32[] px)
        {
            Directory.CreateDirectory(Dir);
            string path = $"{Dir}/{name}.png";
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true; ti.sRGBTexture = true;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D Cracks(string name)
        {
            var px = Blank();
            for (int k = 0; k < 3; k++)
            {
                Vector2 p = new Vector2(R(60, 196), R(60, 196)); float ang = R(0, 6.28f);
                for (int s = 0; s < 34; s++)
                {
                    ang += R(-0.5f, 0.5f);
                    Vector2 q = p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * R(3f, 8f);
                    Line(px, p, q, R(0.9f, 1.6f), new Color(0.02f, 0.02f, 0.025f), 0.95f);
                    if (rng.NextDouble() < 0.18)                                  // a branch
                    {
                        Vector2 b = q; float ba = ang + R(-1.2f, 1.2f);
                        for (int bs = 0; bs < 8; bs++) { Vector2 bq = b + new Vector2(Mathf.Cos(ba), Mathf.Sin(ba)) * R(3f, 6f); Line(px, b, bq, 0.8f, new Color(0.02f, 0.02f, 0.025f), 0.85f); b = bq; ba += R(-0.5f, 0.5f); }
                    }
                    p = q;
                }
            }
            return Save(name, px);
        }

        static Texture2D Pothole(string name)
        {
            var px = Blank();
            float ox = R(0, 100), oy = R(0, 100);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x - N / 2f) / (N * 0.40f), dy = (y - N / 2f) / (N * 0.32f);
                    float ang = Mathf.Atan2(dy, dx);
                    float edge = 1f + 0.22f * (Mathf.PerlinNoise(Mathf.Cos(ang) * 1.6f + ox, Mathf.Sin(ang) * 1.6f + oy) - 0.5f) * 2f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / edge;
                    float a = 0f; Color c = new Color(0.03f, 0.03f, 0.035f);
                    if (d < 1f) a = d < 0.82f ? 0.97f : Mathf.Lerp(0.97f, 0.35f, (d - 0.82f) / 0.18f);
                    else if (d < 1.18f) { a = Mathf.Lerp(0.5f, 0f, (d - 1f) / 0.18f); c = new Color(0.30f, 0.29f, 0.28f); }   // crumbled rim
                    px[y * N + x] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(a * 255));
                }
            return Save(name, px);
        }

        static Texture2D Blobs(string name, Color col, float alpha, int count, float rMin, float rMax, float spread)
        {
            var px = Blank();
            for (int i = 0; i < count; i++)
            {
                Vector2 c = new Vector2(N / 2f + R(-spread, spread), N / 2f + R(-spread, spread));
                float r = R(rMin, rMax);
                for (int k = 0; k < 6; k++) Dot(px, c.x + R(-r * 0.6f, r * 0.6f), c.y + R(-r * 0.6f, r * 0.6f), r * R(0.4f, 0.9f), col, alpha);
            }
            // break the edge up with noise so nothing reads as a clean disc
            float ox = R(0, 50), oy = R(0, 50);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    var p = px[y * N + x];
                    if (p.a == 0) continue;
                    float n = Mathf.PerlinNoise(x * 0.06f + ox, y * 0.06f + oy);
                    p.a = (byte)Mathf.Clamp(p.a * Mathf.InverseLerp(0.25f, 0.65f, n), 0, 255);
                    px[y * N + x] = p;
                }
            return Save(name, px);
        }

        static Texture2D Leaves(string name)
        {
            var px = Blank();
            Color[] cols = { new Color(0.22f, 0.15f, 0.07f), new Color(0.30f, 0.20f, 0.08f), new Color(0.14f, 0.12f, 0.06f), new Color(0.26f, 0.10f, 0.05f) };
            for (int i = 0; i < 70; i++)
            {
                Vector2 c = new Vector2(R(12, N - 12), R(12, N - 12)); float a = R(0, 6.28f), len = R(4f, 8f);
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                for (float t = -len; t <= len; t += 0.8f) Dot(px, c.x + d.x * t, c.y + d.y * t, Mathf.Lerp(0.8f, 3.2f, 1f - Mathf.Abs(t) / len), cols[rng.Next(cols.Length)], 0.95f);
            }
            return Save(name, px);
        }

        static Texture2D WaterLine(string name)
        {
            // Vertical decal for walls: a stained band whose top edge is the flood's high-water mark, drips below it.
            var px = Blank();
            float ox = R(0, 30);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fy = y / (float)(N - 1);                       // 0 bottom .. 1 top (texture up = world up)
                    float mark = 0.80f + 0.05f * (Mathf.PerlinNoise(x * 0.05f + ox, 3f) - 0.5f) * 2f;
                    float a;
                    if (fy > mark) a = Mathf.Lerp(0.9f, 0f, (fy - mark) / 0.06f);     // crisp top edge
                    else
                    {
                        float drip = Mathf.PerlinNoise(x * 0.12f + ox, fy * 0.8f);
                        a = Mathf.Lerp(0.75f, 0.28f, Mathf.InverseLerp(mark, 0f, fy)) * Mathf.Lerp(0.55f, 1f, drip);
                    }
                    a *= Mathf.SmoothStep(0f, 1f, Mathf.Min(x, N - 1 - x) / (N * 0.12f));      // feathered ends: no seam between neighbours
                    px[y * N + x] = new Color32(58, 44, 28, (byte)(Mathf.Clamp01(a) * 255));
                }
            return Save(name, px);
        }

        static Texture2D Arrow(string name)
        {
            var px = Blank();
            Color paint = new Color(0.78f, 0.76f, 0.66f);
            // shaft + head, pointing +y
            for (int y = 30; y < 150; y++) for (int x = 108; x < 148; x++) px[y * N + x] = new Color32((byte)(paint.r * 255), (byte)(paint.g * 255), (byte)(paint.b * 255), 235);
            for (int y = 150; y < 232; y++)
            {
                float half = Mathf.Lerp(68f, 0f, (y - 150) / 82f);
                for (int x = (int)(128 - half); x <= (int)(128 + half); x++) px[y * N + x] = new Color32((byte)(paint.r * 255), (byte)(paint.g * 255), (byte)(paint.b * 255), 235);
            }
            float ox = R(0, 40), oy = R(0, 40);                           // worn away by feet and rain
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    var p = px[y * N + x]; if (p.a == 0) continue;
                    float n = Mathf.PerlinNoise(x * 0.09f + ox, y * 0.09f + oy);
                    p.a = (byte)(p.a * Mathf.InverseLerp(0.28f, 0.6f, n) * 0.7f);
                    px[y * N + x] = p;
                }
            return Save(name, px);
        }

        // ------------------------------------------------------------------ materials and placement

        static Material Mat(string name, Texture2D t)
        {
            string path = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Shader Graphs/Decal")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("Base_Map", t);
            m.SetFloat("Normal_Blend", 0f);
            EditorUtility.SetDirty(m);
            Mats[name] = m;
            return m;
        }

        static GameObject Project(Transform parent, string name, Material m, Vector3 pos, Quaternion rot, Vector3 size, float angleFade = 40f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var d = go.AddComponent<DecalProjector>();
            d.material = m;
            d.size = size;
            d.pivot = new Vector3(0, 0, size.z * 0.5f);
            d.drawDistance = 70f;
            d.fadeScale = 0.85f;
            d.startAngleFade = 180f - angleFade * 2f;
            d.endAngleFade = 180f - angleFade;
            go.isStatic = true;
            return go;
        }

        static Vector3 Ground(float x, float z, float up = 0.6f) => new Vector3(x, FairgroundBlockout.H(x, z) + up, z);

        /// <summary>Places every decal. strips = (a, b, width) asphalt lanes; lot = (x0, x1, z0, z1).</summary>
        public static string Place(Transform root, List<(Vector2 a, Vector2 b, float w)> strips, Vector4 lot, int seed = 5)
        {
            rng = new System.Random(seed);
            Mats.Clear();
            var cracks = new[] { Mat("D_Crack_A", Cracks("Crack_A")), Mat("D_Crack_B", Cracks("Crack_B")), Mat("D_Crack_C", Cracks("Crack_C")) };
            var potholes = new[] { Mat("D_Pothole_A", Pothole("Pothole_A")), Mat("D_Pothole_B", Pothole("Pothole_B")) };
            var oil = new[] { Mat("D_Oil_A", Blobs("Oil_A", new Color(0.02f, 0.025f, 0.035f), 0.62f, 3, 22, 40, 55)), Mat("D_Oil_B", Blobs("Oil_B", new Color(0.03f, 0.03f, 0.04f), 0.55f, 4, 18, 34, 60)) };
            var mud = new[] { Mat("D_Mud_A", Blobs("Mud_A", new Color(0.17f, 0.12f, 0.075f), 0.9f, 5, 26, 52, 70)), Mat("D_Mud_B", Blobs("Mud_B", new Color(0.14f, 0.10f, 0.06f), 0.85f, 6, 20, 44, 78)) };
            var leaves = Mat("D_Leaves", Leaves("Leaves"));
            var arrow = Mat("D_QueueArrow", Arrow("QueueArrow"));
            var floodLine = Mat("D_FloodLine", WaterLine("FloodLine"));

            var g = new GameObject("12_Decals").transform;
            g.SetParent(root, false);
            int counts = 0, c = 0, ph = 0, o = 0, m = 0, lv = 0, ar = 0, fl = 0;
            Quaternion down(float yaw) => Quaternion.Euler(90f, yaw, 0f);

            System.Action<Vector2, float, Vector2> onLane = null;
            foreach (var s in strips)
            {
                Vector2 dir = (s.b - s.a).normalized, left = new Vector2(-dir.y, dir.x);
                float len = Vector2.Distance(s.a, s.b);
                for (float u = R(2f, 6f); u < len - 2f; u += R(5f, 9f))
                {
                    Vector2 p = s.a + dir * u + left * R(-s.w * 0.4f, s.w * 0.4f);
                    float yaw = R(0, 360);
                    float roll = (float)rng.NextDouble();
                    if (roll < 0.55) { Project(g, "Crack", cracks[rng.Next(3)], Ground(p.x, p.y), down(yaw), new Vector3(R(3f, 5.5f), R(3f, 5.5f), 1.6f)); c++; }
                    else if (roll < 0.72) { Project(g, "Pothole", potholes[rng.Next(2)], Ground(p.x, p.y), down(yaw), new Vector3(R(1.4f, 2.6f), R(1.1f, 2.0f), 1.6f)); ph++; }
                    else if (roll < 0.88) { Project(g, "Oil", oil[rng.Next(2)], Ground(p.x, p.y), down(yaw), new Vector3(R(2.2f, 4f), R(2.2f, 4f), 1.6f)); o++; }
                    else { Project(g, "Leaves", leaves, Ground(p.x, p.y), down(yaw), new Vector3(R(3f, 4.5f), R(3f, 4.5f), 1.6f)); lv++; }
                    // leaves and mud collect along the kerb
                    if (rng.NextDouble() < 0.5)
                    {
                        Vector2 kerb = s.a + dir * (u + R(-2f, 2f)) + left * (rng.NextDouble() < 0.5 ? 1f : -1f) * (s.w * 0.5f - 0.6f);
                        Project(g, "KerbLeaves", leaves, Ground(kerb.x, kerb.y), down(R(0, 360)), new Vector3(3f, 2f, 1.6f)); lv++;
                    }
                }
            }
            // the parking lot: cracked, pitted, oily, muddy
            for (int i = 0; i < 34; i++)
            {
                float x = R(lot.x + 2, lot.y - 2), z = R(lot.z + 2, lot.w - 2); float yaw = R(0, 360);
                float roll = (float)rng.NextDouble();
                if (roll < 0.35f) { Project(g, "Crack", cracks[rng.Next(3)], Ground(x, z), down(yaw), new Vector3(R(3.5f, 6f), R(3.5f, 6f), 1.8f)); c++; }
                else if (roll < 0.55f) { Project(g, "Pothole", potholes[rng.Next(2)], Ground(x, z), down(yaw), new Vector3(R(1.6f, 3f), R(1.2f, 2.2f), 1.8f)); ph++; }
                else if (roll < 0.75f) { Project(g, "Oil", oil[rng.Next(2)], Ground(x, z), down(yaw), new Vector3(R(2.5f, 4.5f), R(2.5f, 4.5f), 1.8f)); o++; }
                else { Project(g, "Mud", mud[rng.Next(2)], Ground(x, z), down(yaw), new Vector3(R(4f, 8f), R(4f, 8f), 1.8f)); m++; }
            }
            // the flood's reach: mud splashes washed over the ground around the lowlands' edge
            for (int i = 0; i < 70; i++)
            {
                float x = R(8f, 60f), z = R(-40f, -14f);
                float low = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(14f, 24f, x)) * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-20f, -32f, z));
                if (low > 0.85f || low < 0.02f) { if (rng.NextDouble() < 0.6) continue; }
                Project(g, "MudWash", mud[rng.Next(2)], Ground(x, z), down(R(0, 360)), new Vector3(R(4f, 9f), R(4f, 9f), 2.2f)); m++;
            }
            for (int i = 0; i < 24; i++)                                   // entrances of the Big Top and the plaza edges
            {
                float a = R(0, 6.28f); float r = R(17f, 24f);
                var p = FairgroundBlockout.BigTopCentre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                Project(g, "MudWash", mud[rng.Next(2)], Ground(p.x, p.y), down(R(0, 360)), new Vector3(R(4f, 8f), R(4f, 8f), 2.2f)); m++;
            }
            // worn queue arrows on the gate plaza, pointing at the turnstiles (south)
            for (int i = 0; i < 7; i++)
            {
                float x = -21f + i * 7f; float z = 60f + R(-1f, 1f);
                if (Mathf.Abs(x) < 3f) continue;
                Project(g, "QueueArrow", arrow, Ground(x, 77f - 4f * (i % 2)), down(180f + R(-6f, 6f)), new Vector3(1.6f, 2.8f, 1.6f)); ar++;
            }
            Project(g, "QueueArrow", arrow, Ground(0f, 52f), down(180f), new Vector3(2.4f, 4.0f, 1.6f)); ar++;
            counts = c + ph + o + m + lv + ar + fl;
            return $"decals {counts}: cracks {c}, potholes {ph}, oil {o}, mud {m}, leaves {lv}, queue arrows {ar}, flood lines {fl}";
        }
    }
}
#endif
