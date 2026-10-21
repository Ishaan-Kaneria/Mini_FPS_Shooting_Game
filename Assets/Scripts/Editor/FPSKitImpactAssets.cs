#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// What FPSImpactManager draws with, made here and kept in FPSKit_Generated: the marks' atlas (a hole in concrete, a
    /// dent in metal, a dark scorch, a second hole; grayscale with alpha, the colour on the vertex as the project's
    /// generated textures have it), a soft puff for dust and sparks, their three materials (URP particle shaders: lit and
    /// alpha-blended for the marks and dust, unlit and additive for the sparks), the surface physics materials the town's
    /// colliders carry (Surface_Concrete, _Metal, ...: Unity's default friction and bounce, so nothing slides or bounces any
    /// differently, only named), and the ImpactSettings asset in Resources, its materials re-stamped on every build and
    /// its numbers never overwritten.
    /// </summary>
    public static class FPSKitImpactAssets
    {
        const string Folder = "Assets/FPSKit_Generated/Impacts";
        const string ResourcesFolder = "Assets/FPSKit_Generated/Resources";
        static readonly Dictionary<string, PhysicsMaterial> _surfaces = new Dictionary<string, PhysicsMaterial>();

        [MenuItem("FPSKit/Settings/Ensure Impact Assets", false, 71)]
        public static ImpactSettings Ensure()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/FPSKit_Generated", "Impacts");
            if (!AssetDatabase.IsValidFolder(ResourcesFolder)) AssetDatabase.CreateFolder("Assets/FPSKit_Generated", "Resources");

            var atlas = Texture($"{Folder}/ImpactMarks.png", 512, DrawMarks);
            var puff = Texture($"{Folder}/ImpactPuff.png", 64, DrawPuff);

            var marks = ParticleMaterial($"{Folder}/ImpactMarks.mat", "Universal Render Pipeline/Particles/Simple Lit", atlas, Color.white,
                                         additive: false, cullOff: true, queue: 2990, smoothness: 0.1f);
            var dust = ParticleMaterial($"{Folder}/ImpactDust.mat", "Universal Render Pipeline/Particles/Simple Lit", puff, Color.white,
                                        additive: false, cullOff: false, queue: 3000, smoothness: 0f);

            string path = $"{ResourcesFolder}/{ImpactSettings.ResourcePath}.asset";
            var settings = AssetDatabase.LoadAssetAtPath<ImpactSettings>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<ImpactSettings>();
                AssetDatabase.CreateAsset(settings, path);
            }
            var sparks = ParticleMaterial($"{Folder}/ImpactSparks.mat", "Universal Render Pipeline/Particles/Unlit", puff, settings.sparkColor,
                                          additive: true, cullOff: false, queue: 3010, smoothness: 0f);
            settings.decalMaterial = marks;
            settings.dustMaterial = dust;
            settings.sparkMaterial = sparks;
            EditorUtility.SetDirty(settings);
            foreach (var tag in new[] { "Concrete", "Metal", "Wood", "Glass", "Sand", "Snow", "Ice" }) Surface(tag);
            AssetDatabase.SaveAssets();
            return settings;
        }

        /// <summary>
        /// The physics material for a surface tag (Surface_Concrete, ...), made once; null for a tag without one. FPSImpactManager
        /// reads the name. Default friction and bounce: Unity's own default material, so a car, a dropped gun or a body
        /// behaves on it exactly as before.
        /// </summary>
        public static PhysicsMaterial Surface(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            if (tag != "Concrete" && tag != "Metal" && tag != "Wood" && tag != "Glass" && tag != "Sand" && tag != "Snow" && tag != "Ice") return null;
            if (_surfaces.TryGetValue(tag, out var cached) && cached != null) return cached;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/FPSKit_Generated", "Impacts");
            string path = $"{Folder}/Surface_{tag}.physicsMaterial";
            var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (m == null)
            {
                m = new PhysicsMaterial($"Surface_{tag}")
                {
                    dynamicFriction = 0.6f, staticFriction = 0.6f, bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Average,
                };
                AssetDatabase.CreateAsset(m, path);
            }
            _surfaces[tag] = m;
            return m;
        }

        // ------------------------------------------------------------------
        static Texture2D Texture(string path, int size, System.Action<Color[], int> draw)
        {
            var px = new Color[size * size];
            draw(px, size);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            bool changed = !File.Exists(path) || !System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(path), png);
            if (changed) { File.WriteAllBytes(path, png); AssetDatabase.ImportAsset(path); }
            if (AssetImporter.GetAtPath(path) is TextureImporter imp &&
                (!imp.alphaIsTransparency || imp.wrapMode != TextureWrapMode.Clamp || !imp.mipmapEnabled || imp.textureCompression != TextureImporterCompression.CompressedHQ))
            {
                imp.textureType = TextureImporterType.Default;
                imp.alphaSource = TextureImporterAlphaSource.FromInput;
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.mipmapEnabled = true;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>The marks' 2 x 2 atlas: (0) a hole in concrete, (1) a dent in metal, (2) a dark scorch, (3) another hole.</summary>
        static void DrawMarks(Color[] px, int size)
        {
            int cell = size / 2;
            for (int k = 0; k < 4; k++)
            {
                int ox = (k % 2) * cell, oy = (k / 2) * cell;
                float seed = 17.3f * (k + 1);
                for (int y = 0; y < cell; y++)
                    for (int x = 0; x < cell; x++)
                    {
                        float fx = (x + 0.5f) / cell * 2f - 1f, fy = (y + 0.5f) / cell * 2f - 1f;
                        float r = Mathf.Sqrt(fx * fx + fy * fy), ang = Mathf.Atan2(fy, fx);
                        float edge = Mathf.PerlinNoise(seed + Mathf.Cos(ang) * 1.7f, seed + Mathf.Sin(ang) * 1.7f) - 0.5f;   // jagged rim
                        float grain = Mathf.PerlinNoise(seed + fx * 9f, seed - fy * 9f) - 0.5f;
                        float lum, alpha;
                        switch (k)
                        {
                            case 1: Dent(r, ang, edge, grain, seed, out lum, out alpha); break;
                            case 2: Scorch(r, edge, grain, out lum, out alpha); break;
                            default: Hole(r, ang, edge, grain, seed, k == 3, out lum, out alpha); break;
                        }
                        px[(oy + y) * size + ox + x] = new Color(lum, lum, lum, Mathf.Clamp01(alpha));
                    }
            }
        }

        static void Hole(float r, float ang, float edge, float grain, float seed, bool second, out float lum, out float alpha)
        {
            float core = (second ? 0.12f : 0.15f) * (1f + edge * 0.5f);
            float crater = (second ? 0.42f : 0.36f) * (1f + edge * 0.6f);
            float halo = (second ? 0.66f : 0.6f) * (1f + edge * 0.4f);
            if (r < core) { lum = 0.035f; alpha = 1f; }
            else if (r < crater)
            {
                float t = (r - core) / (crater - core);
                lum = 0.16f + 0.26f * t + grain * 0.18f;
                alpha = 0.97f - 0.15f * t;
            }
            else if (r < halo)
            {
                float t = (r - crater) / (halo - crater);
                lum = 0.58f + grain * 0.15f;
                alpha = 0.32f * Mathf.Pow(1f - t, 1.6f) * (0.7f + grain);
            }
            else { lum = 0.5f; alpha = 0f; }
            // Hairline cracks running out of the crater.
            for (int i = 0; i < (second ? 6 : 4); i++)
            {
                float a = Mathf.Repeat(seed * (i + 1) * 2.399f, Mathf.PI * 2f) - Mathf.PI;
                float d = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, a * Mathf.Rad2Deg)) * Mathf.Deg2Rad * r;
                float reach = 0.55f + 0.25f * Mathf.PerlinNoise(seed, i);
                if (r > core && r < reach && d < 0.012f * (1f - r / reach) + 0.004f)
                {
                    lum = Mathf.Min(lum, 0.12f);
                    alpha = Mathf.Max(alpha, 0.85f * (1f - r / reach));
                }
            }
        }

        static void Dent(float r, float ang, float edge, float grain, float seed, out float lum, out float alpha)
        {
            float core = 0.09f * (1f + edge * 0.4f), bowl = 0.3f * (1f + edge * 0.25f), lip = bowl + 0.06f, scuff = 0.56f * (1f + edge * 0.5f);
            if (r < core) { lum = 0.03f; alpha = 1f; }
            else if (r < bowl)
            {
                // A bowl lit from one side: bright where it faces the light, dark where it falls away.
                float shade = 0.5f + 0.5f * Mathf.Cos(ang - 0.8f) * (r / bowl);
                lum = 0.28f + 0.5f * shade + grain * 0.12f;
                alpha = 0.92f;
            }
            else if (r < lip) { lum = 0.9f + grain * 0.08f; alpha = 0.85f; }
            else if (r < scuff)
            {
                float t = (r - lip) / (scuff - lip);
                float streak = Mathf.PerlinNoise(seed + ang * 6f, seed) ;
                lum = 0.72f + grain * 0.15f;
                alpha = 0.34f * (1f - t) * streak;
            }
            else { lum = 0.7f; alpha = 0f; }
        }

        static void Scorch(float r, float edge, float grain, out float lum, out float alpha)
        {
            float outer = 0.46f * (1f + edge * 0.6f);
            if (r >= outer) { lum = 0.1f; alpha = 0f; return; }
            float t = r / outer;
            lum = 0.04f + 0.14f * t + grain * 0.06f;
            alpha = (1f - t * t) * 0.92f * (0.8f + grain * 0.4f);
        }

        /// <summary>A soft, slightly lumpy puff: white, the alpha falling off from the middle.</summary>
        static void DrawPuff(Color[] px, int size)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fx = (x + 0.5f) / size * 2f - 1f, fy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(fx * fx + fy * fy);
                    float lump = 0.75f + 0.25f * Mathf.PerlinNoise(fx * 3f + 5f, fy * 3f + 9f);
                    float a = r >= 1f ? 0f : Mathf.Pow(1f - r, 1.6f) * lump;
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
        }

        /// <summary>A URP particle material: transparent, alpha-blended (or additive), depth not written; vertex colour
        /// multiplies the texture, which is what lets a mark fade and a puff take its colour.</summary>
        static Material ParticleMaterial(string path, string shaderName, Texture2D tex, Color color, bool additive, bool cullOff, int queue, float smoothness)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) { Debug.LogError($"[FPSKit] impacts: shader '{shaderName}' not found"); return null; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = m == null;
            if (create) m = new Material(shader);
            else if (m.shader != shader) m.shader = shader;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", cullOff ? 0f : 2f);
            m.SetFloat("_ColorMode", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHAMODULATE_ON");
            m.DisableKeyword("_RECEIVE_SHADOWS_OFF");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = queue;
            m.enableInstancing = true;
            if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }
    }
}
#endif
