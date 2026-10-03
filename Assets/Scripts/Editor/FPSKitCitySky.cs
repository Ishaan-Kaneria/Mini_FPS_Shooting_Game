#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The rooftop district's night sky, painted into a panorama like the volcanic one: a deep blue zenith
    /// falling to the amber-violet glow of a lit city at the horizon, a few thousand stars (thinned toward the
    /// horizon and hidden behind cloud), thin cloud banks lit from the streets below, and a moon placed exactly
    /// where the directional light comes from, so the shadows fall away from it.
    /// The procedural sky this replaces had no stars and no moon, and at the exposure the night needed it
    /// came out a flat dark slate.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private static Material NightCitySky()
        {
            string name = SafeName(_theme.themeName);
            string texPath = $"{TextureFolder}/Sky_{name}.png";
            var panorama = WriteNightSky(texPath);
            ReflectSky(panorama, $"{TextureFolder}/Reflection_{name}.cubemap");

            string path = $"{MaterialFolder}/Sky_{name}.mat";
            var shader = Shader.Find("Skybox/Panoramic");
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (shader == null) return existing;

            var mat = existing != null ? existing : new Material(shader);
            mat.shader = shader;
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            mat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f));
            mat.SetFloat("_Exposure", 1f);
            mat.SetFloat("_Rotation", 0f);

            if (existing == null) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Color32[] WriteNightSky(string path)
        {
            const int W = SkyWidth, H = SkyHeight;
            var colour = new Color[W * H];

            // Same lat-long mapping as Skybox/Panoramic and ReflectSky: u from longitude, v linear in elevation.
            Vector3 Dir(int x, int y)
            {
                float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                float lon = (0.5f - u) * Mathf.PI * 2f;
                float lat = (v - 0.5f) * Mathf.PI;
                float cl = Mathf.Cos(lat);
                return new Vector3(cl * Mathf.Cos(lon), Mathf.Sin(lat), cl * Mathf.Sin(lon));
            }
            void ToPixel(Vector3 d, out float px, out float py)
            {
                px = Mathf.Repeat(0.5f - Mathf.Atan2(d.z, d.x) / (Mathf.PI * 2f), 1f) * W;
                py = (0.5f + Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) / Mathf.PI) * H;
            }

            // The moon sits opposite the light's direction of travel.
            var light = Quaternion.Euler(_theme.sunAngles.x, _theme.sunAngles.y, 0f) * Vector3.forward;
            var moon = -light;

            var zenith = new Color(0.030f, 0.060f, 0.170f);
            var high = new Color(0.060f, 0.105f, 0.250f);
            var low = new Color(0.150f, 0.150f, 0.320f);
            var glow = new Color(0.520f, 0.330f, 0.300f);      // the city's light on the haze
            var haze = Color.Lerp(_theme.fogColor, glow, 0.35f);
            var nadir = new Color(0.035f, 0.040f, 0.070f);

            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H;
                float e = (v - 0.5f) * 180f;                    // elevation in degrees
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W;
                    Color c;
                    if (e < 0f) c = Color.Lerp(haze, nadir, Mathf.SmoothStep(0f, 1f, -e / 16f));
                    else if (e < 4f) c = Color.Lerp(haze, glow, e / 4f);
                    else if (e < 14f) c = Color.Lerp(glow, low, (e - 4f) / 10f);
                    else if (e < 40f) c = Color.Lerp(low, high, (e - 14f) / 26f);
                    else c = Color.Lerp(high, zenith, Mathf.SmoothStep(0f, 1f, (e - 40f) / 50f));

                    // The glow is stronger in some directions than others: where the dense blocks are.
                    if (e > 0f && e < 30f)
                        c = Color.Lerp(c, c * 1.35f, (TileFbm(u, 0.2f, 3, 3, 7101) - 0.4f) * (1f - e / 30f) * 1.5f);

                    // Cloud: long banks, lit from underneath by the streets and from the side by the moon.
                    float band = e <= 3f ? 0f : Ramp(3f, 12f, e) * (1f - Ramp(40f, 70f, e));
                    float n1 = TileFbm(u, v * 0.45f, 6, 5, 7102);
                    float n2 = TileFbm(u, v * 1.6f, 5, 3, 7103);
                    float dense = Ramp(0.46f, 0.74f, n1 * 0.75f + n2 * 0.25f) * band;
                    var cloud = Color.Lerp(new Color(0.07f, 0.09f, 0.18f), new Color(0.42f, 0.30f, 0.32f), Mathf.Clamp01(1f - e / 34f) * (0.6f + 0.4f * n2));
                    float toMoon = Mathf.Clamp01(Vector3.Dot(Dir(x, y), moon));
                    cloud += new Color(0.30f, 0.36f, 0.50f) * Mathf.Pow(toMoon, 24f) * 1.2f;
                    c = Color.Lerp(c, cloud, dense * 0.8f);
                    c.a = 1f - dense;                                // how much of the stars survive
                    colour[y * W + x] = c;
                }
            }

            // Stars: random directions, splatted round so the stretch of the panorama toward the poles does not smear them.
            var rng = new System.Random(4242);
            for (int s = 0; s < 6500; s++)
            {
                float sy = (float)(rng.NextDouble() * 0.96 + 0.04);        // uniform in height over the upper sky
                float az = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float r = Mathf.Sqrt(1f - sy * sy);
                var d = new Vector3(r * Mathf.Cos(az), sy, r * Mathf.Sin(az));
                float bright = 0.22f + 0.78f * Mathf.Pow((float)rng.NextDouble(), 5.0f);
                float tint = (float)rng.NextDouble();
                var sc = tint < 0.6f ? new Color(1f, 1f, 1f) : tint < 0.85f ? new Color(0.78f, 0.88f, 1f) : new Color(1f, 0.90f, 0.75f);
                float elev = Mathf.Asin(sy) * Mathf.Rad2Deg;
                bright *= Ramp(3f, 22f, elev);

                ToPixel(d, out float px, out float py);
                float ry = bright > 0.75f ? 1.1f : 0.75f;
                float rx = Mathf.Min(7f, ry / Mathf.Max(0.12f, Mathf.Cos(elev * Mathf.Deg2Rad)));
                for (int oy = -2; oy <= 2; oy++)
                    for (int ox = -8; ox <= 8; ox++)
                    {
                        int xx = (int)Mathf.Repeat(Mathf.Floor(px) + ox, W), yy = (int)Mathf.Floor(py) + oy;
                        if (yy < 0 || yy >= H) continue;
                        float dx = (xx + 0.5f - px) / rx, dy = (yy + 0.5f - py) / ry;
                        float g = Mathf.Exp(-(dx * dx + dy * dy) * 2.6f) * bright * 1.5f;
                        if (g < 0.01f) continue;
                        var px0 = colour[yy * W + xx];
                        float keep = px0.a;                          // cloud hides the star
                        var lit = px0 + sc * g * keep;
                        lit.a = px0.a;
                        colour[yy * W + xx] = lit;
                    }
            }

            // The moon: a disc, a little larger than the real one so it reads at game field of view, with a pale halo.
            const float moonRadius = 1.9f;                           // degrees
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var d = Dir(x, y);
                    float ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(d, moon), -1f, 1f)) * Mathf.Rad2Deg;
                    if (ang > 30f) continue;
                    var c = colour[y * W + x];
                    float halo = Mathf.Exp(-ang / 5.5f) * 0.20f + Mathf.Exp(-ang / 16f) * 0.06f;
                    c += new Color(0.55f, 0.65f, 0.95f) * halo;
                    float disc = 1f - Sm(moonRadius - 0.12f, moonRadius, ang);
                    if (disc > 0f)
                    {
                        float mares = 0.55f * Mathf.PerlinNoise(x * 0.11f + 11f, y * 0.11f + 5f) + 0.45f * Mathf.PerlinNoise(x * 0.31f + 3f, y * 0.31f + 17f);
                        var surface = Color.Lerp(new Color(0.68f, 0.72f, 0.80f), new Color(1.0f, 0.98f, 0.94f), Mathf.Clamp01((mares - 0.35f) / 0.3f));
                        c = Color.Lerp(c, surface * 1.15f, disc);
                    }
                    colour[y * W + x] = c;
                }

            var pixels = new Color32[W * H];
            for (int i = 0; i < pixels.Length; i++)
            {
                var c = colour[i];
                pixels[i] = new Color32((byte)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f),
                                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f),
                                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f), 255);
            }

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false;                      // a panorama minified at its poles blends the seam into itself
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.maxTextureSize = W;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            return pixels;
        }
    }
}
#endif
