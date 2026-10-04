#if UNITY_EDITOR
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Snowbound's sky, painted (2026-10-04, "make the skybox better and accurate for the snowbound theme").
    ///
    /// The procedural sky at a low sun reddens the whole horizon, which is what a desert sunset looks like and
    /// not what a polar day does -- in the Game view Snowbound had an orange band all the way round. A polar
    /// sky is pale: a deep cold blue overhead fading to the colour of the haze at the horizon (here exactly the
    /// fog colour, so the join with the fogged distance cannot be seen), a low sun with a wide warm glow,
    /// thin cirrus streaked along the wind, broken altocumulus lit from the sun's side, and -- the detail that
    /// says "ice crystals in the air" -- a faint 22-degree halo with a bright sundog either side of the sun.
    ///
    /// It is a 4096 by 2048 equirectangular texture drawn here (noise in direction space, so there is no seam)
    /// and shown with Unity's own Skybox/Panoramic shader. The sun in the picture is put where the scene's
    /// directional light says it is. Regenerated only when the sun, the fog or the recipe changes.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        private const string SnowSkyRecipe = "snowsky-v2";

        private static float SkyHash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 1442695041 + seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        private static float SkyNoise(float x, float y, float z, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
            float fx = x - xi, fy = y - yi, fz = z - zi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
            float L(float a, float b, float t) => a + (b - a) * t;
            float c00 = L(SkyHash(xi, yi, zi, seed), SkyHash(xi + 1, yi, zi, seed), fx);
            float c10 = L(SkyHash(xi, yi + 1, zi, seed), SkyHash(xi + 1, yi + 1, zi, seed), fx);
            float c01 = L(SkyHash(xi, yi, zi + 1, seed), SkyHash(xi + 1, yi, zi + 1, seed), fx);
            float c11 = L(SkyHash(xi, yi + 1, zi + 1, seed), SkyHash(xi + 1, yi + 1, zi + 1, seed), fx);
            return L(L(c00, c10, fy), L(c01, c11, fy), fz);
        }

        private static float SkyFbm(float x, float y, float z, int seed, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += SkyNoise(x, y, z, seed + o * 17) * amp;
                norm += amp; amp *= 0.5f; x *= 2.03f; y *= 2.03f; z *= 2.03f;
            }
            return sum / norm;
        }

        private static float Sstep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        private static Material SnowSky()
        {
            string texPath = "Assets/FPSKit_Generated/Textures/SnowSky.png";
            string matPath = $"{MaterialFolder}/Sky_{SafeName(_theme.themeName)}.mat";

            // The sun's direction in the sky is the opposite of where its light points.
            float el = _theme.sunAngles.x * Mathf.Deg2Rad, az = _theme.sunAngles.y * Mathf.Deg2Rad;
            var sun = new Vector3(-Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), -Mathf.Cos(az) * Mathf.Cos(el)).normalized;
            string stamp = $"{SnowSkyRecipe}|{sun.x:0.000},{sun.y:0.000},{sun.z:0.000}|{ColorKey(_theme.fogColor)}|{ColorKey(_theme.skyTint)}";

            var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
            bool fresh = System.IO.File.Exists(texPath) && importer != null && importer.userData == stamp;

            if (!fresh)
            {
                const int W = 4096, H = 2048;
                var px = new Color32[W * H];
                var fog = _theme.fogColor;
                var horizon = Color.Lerp(fog, new Color(0.84f, 0.88f, 0.95f), 0.25f);
                var zenith = new Color(0.16f, 0.33f, 0.64f);
                var upper = new Color(0.42f, 0.62f, 0.88f);
                var warm = new Color(1.00f, 0.80f, 0.55f);
                int seed = _theme.randomSeed * 31 + 7;
                var windPlane = new Vector2(Mathf.Cos(0.35f), Mathf.Sin(0.35f));
                var sunPlane = new Vector2(sun.x, sun.z) / (Mathf.Max(0f, sun.y) + 0.1f);

                Parallel.For(0, H, j =>
                {
                    float v = (j + 0.5f) / H;
                    float lat = (1f - v) * Mathf.PI;
                    float y = Mathf.Cos(lat), sl = Mathf.Sin(lat);
                    for (int i = 0; i < W; i++)
                    {
                        float u = (i + 0.5f) / W;
                        float lon = (0.5f - u) * 2f * Mathf.PI;
                        var d = new Vector3(sl * Mathf.Cos(lon), y, sl * Mathf.Sin(lon));
                        float cosT = Mathf.Clamp(Vector3.Dot(d, sun), -1f, 1f);
                        float theta = Mathf.Acos(cosT);

                        // ---- the clear sky ----
                        Color col;
                        if (y >= 0f)
                        {
                            float t = Mathf.Pow(y, 0.42f);
                            col = Color.Lerp(horizon, upper, Sstep(0f, 0.42f, t));
                            col = Color.Lerp(col, zenith, Mathf.Pow(Mathf.Clamp01(y), 0.9f) * 0.85f);
                        }
                        else col = Color.Lerp(horizon, horizon * 0.94f, Sstep(0f, 0.3f, -y));

                        // The sun's glow: tight warm core, wide soft shoulder, and the haze round it near the ground.
                        float glow = 0.55f * Mathf.Exp(-theta / 0.20f) + 0.26f * Mathf.Exp(-theta / 0.62f) + 0.10f * Mathf.Exp(-theta / 1.6f);
                        col = Color.Lerp(col, warm, Mathf.Clamp01(glow * 0.75f * (1f - 0.35f * Mathf.Clamp01(y * 2f))));
                        col += warm * (0.35f * Mathf.Exp(-theta / 0.09f));

                        // Ice-crystal optics: the 22-degree halo and the two sundogs on it, at the sun's height.
                        float ring = theta - 0.384f;
                        float halo = 0.16f * Mathf.Exp(-(ring * ring) / (2f * 0.012f * 0.012f));
                        var ringTint = Color.Lerp(new Color(1f, 0.72f, 0.62f), new Color(0.72f, 0.82f, 1f), Sstep(-0.02f, 0.02f, ring));
                        col += ringTint * halo + new Color(1f, 1f, 1f) * (halo * 0.25f);
                        float dEl = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) - el;
                        float dog = 0.26f * Mathf.Exp(-(dEl * dEl) / (2f * 0.018f * 0.018f)) * Mathf.Exp(-(ring * ring) / (2f * 0.026f * 0.026f));
                        col += Color.Lerp(new Color(1f, 0.78f, 0.62f), new Color(0.8f, 0.88f, 1f), Sstep(-0.03f, 0.05f, ring)) * dog;

                        // ---- clouds, painted onto a plane high overhead and projected: they crowd towards the horizon ----
                        if (y > 0.005f)
                        {
                            float k = 1f / (y + 0.1f);
                            float px0 = d.x * k, pz0 = d.z * k;

                            // Cirrus: streaked along the wind (stretched one way, squeezed the other) with finer wisps.
                            float ax = px0 * 0.30f, az2 = pz0 * 1.5f;
                            float c1 = SkyFbm(ax + 3f, az2, 1.5f, seed, 5);
                            float wisp = SkyFbm(px0 * 0.9f + 11f, pz0 * 4.5f, 6.1f, seed + 5, 4);
                            float cirrus = Sstep(0.50f, 0.80f, c1) * (0.45f + 0.55f * Sstep(0.35f, 0.75f, wisp)) * 0.72f;

                            // Broken altocumulus: puffs where a slow coverage field allows them.
                            float cov = Sstep(0.38f, 0.62f, SkyFbm(px0 * 0.16f + 40f, pz0 * 0.16f, 9.2f, seed + 11, 3));
                            float cu = SkyFbm(px0 * 0.85f, pz0 * 0.85f + 7f, 3.1f, seed + 23, 5);
                            float alto = Sstep(0.50f, 0.74f, cu) * cov;

                            float dens = Mathf.Max(cirrus, alto);
                            dens *= Sstep(0.005f, 0.16f, y);

                            if (dens > 0.002f)
                            {
                                // Lit from the sun's side: the same cloud sampled a little towards the sun is thicker where the
                                // sun is blocked.
                                float cuS = SkyFbm((px0 + sunPlane.x * 0.07f) * 0.85f, (pz0 + sunPlane.y * 0.07f) * 0.85f + 7f, 3.1f, seed + 23, 5);
                                float altoS = Sstep(0.50f, 0.74f, cuS) * cov;
                                float light = Mathf.Clamp(1f - (altoS - alto) * 1.8f, 0.25f, 1f);
                                light = Mathf.Lerp(light, 0.9f, Mathf.Clamp01(cirrus - alto));
                                var shadow = new Color(0.56f, 0.62f, 0.76f);
                                var lit = new Color(1.0f, 0.97f, 0.92f);
                                var cloud = Color.Lerp(shadow, lit, light);
                                // The silver lining: thin edges near the sun glow.
                                float edge = 1f - Sstep(0f, 0.5f, dens);
                                cloud += warm * (edge * 0.45f * Mathf.Exp(-theta / 0.55f));
                                col = Color.Lerp(col, cloud, Mathf.Clamp01(dens * 0.93f));
                            }
                        }

                        // A breath of dither, or the gradient bands.
                        float n = (SkyHash(i, j, 3, 91) - 0.5f) * (1.5f / 255f);
                        px[j * W + i] = new Color32(
                            (byte)Mathf.Clamp(Mathf.RoundToInt((col.r + n) * 255f), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt((col.g + n) * 255f), 0, 255),
                            (byte)Mathf.Clamp(Mathf.RoundToInt((col.b + n) * 255f), 0, 255), 255);
                    }
                });

                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                tex.SetPixels32(px);
                tex.Apply(false);
                System.IO.File.WriteAllBytes(texPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
                importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = true;
                    importer.mipmapEnabled = true;
                    importer.wrapModeU = TextureWrapMode.Repeat;
                    importer.wrapModeV = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Trilinear;
                    importer.maxTextureSize = 4096;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    importer.alphaSource = TextureImporterAlphaSource.None;
                    importer.userData = stamp;
                    importer.SaveAndReimport();
                }
            }

            var shader = Shader.Find("Skybox/Panoramic");
            var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (shader == null) return existing;
            var mat = existing != null ? existing : new Material(shader);
            mat.shader = shader;
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            mat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 1f));       // 0.5 is neutral on this shader
            mat.SetFloat("_Exposure", 1.0f);
            mat.SetFloat("_Rotation", 0f);
            mat.SetFloat("_Mapping", 1f);                                  // latitude / longitude
            mat.SetFloat("_ImageType", 0f);                                // the whole 360
            mat.SetFloat("_MirrorOnBack", 0f);
            mat.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            if (existing == null) AssetDatabase.CreateAsset(mat, matPath);
            else EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
#endif
