#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Builds what blood and voices are drawn and played with: the splat, pool, wound,
    /// droplet and mist textures, their materials, the two particle prefabs, the
    /// <see cref="BloodLibrary"/> asset that holds them, and the <see cref="VoiceBank"/>
    /// that holds the enemy voice clips.
    ///
    /// Written the way <see cref="FPSKitTextures"/> is: every texture is a pure function
    /// of a seed, so a rebuild writes identical bytes and git sees nothing. The textures
    /// are grayscale shapes with an alpha; the colour is on the materials, as everywhere
    /// in the kit.
    ///
    /// Called from the enemy prefab build, so there is no separate step to forget.
    /// Everything is re-stamped on every run -- the assets are kept, their wiring is not
    /// trusted -- for the reason the impact library is.
    /// </summary>
    public static class FPSKitGore
    {
        public const string Folder = "Assets/FPSKit_Generated/Gore";
        const string LibraryPath = Folder + "/Blood.asset";
        const string VoiceBankPath = "Assets/FPSKit_Generated/VoiceBank.asset";
        const string VoiceFolder = "Assets/Audio/SFX/Voice";

        public static BloodLibrary GetOrCreateLibrary()
        {
            EnsureFolder();

            var splat = Texture("BloodSplat", 512, SplatAtlas);
            var pool = Texture("BloodPool", 256, Pool);
            var wound = Texture("BloodWound", 128, Wound);
            var drop = Texture("BloodDrop", 64, Drop);
            var mist = Texture("BloodMist", 128, Mist);

            var library = AssetDatabase.LoadAssetAtPath<BloodLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<BloodLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            // Fresh blood is nearly black-red in daylight. Some smoothness, so a pool catches
            // a highlight and reads as wet -- but not much: on a flat floor a transparent
            // surface reflects the sky across its whole face, and at 0.6 every splat came
            // out a pale lilac instead of red.
            library.splatMaterial = Surface("Blood_Splat", splat, new Color(0.17f, 0.004f, 0.007f, 0.97f), 0.22f);
            library.poolMaterial = Surface("Blood_Pool", pool, new Color(0.14f, 0.003f, 0.006f, 0.98f), 0.3f);
            library.woundMaterial = Surface("Blood_Wound", wound, new Color(0.24f, 0.008f, 0.01f, 1f), 0.25f);

            library.decalBudget = new Vector3Int(64, 128, 200);

            var dropMat = Particle("Blood_Drop", drop);
            var mistMat = Particle("Blood_Mist", mist);

            library.sprayPrefab = ParticlePrefab("BloodSpray", dropMat, spray: true);
            library.mistPrefab = ParticlePrefab("BloodMist", mistMat, spray: false);

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        /// <summary>
        /// The two voices. Clips are found by name in Assets/Audio/SFX/Voice, so replacing
        /// a synthesised one with a recording is dropping a file of the same name over it.
        /// </summary>
        public static VoiceBank GetOrCreateVoiceBank()
        {
            var bank = AssetDatabase.LoadAssetAtPath<VoiceBank>(VoiceBankPath);
            if (bank == null)
            {
                bank = ScriptableObject.CreateInstance<VoiceBank>();
                AssetDatabase.CreateAsset(bank, VoiceBankPath);
            }

            bank.human = Set("human");
            bank.creature = Set("creature");

            // The creature recordings are a person's voice through filters, at a person's
            // size. Taken down a fifth they stop sounding like somebody doing a monster.
            bank.human.pitch = 1f;
            bank.creature.pitch = 0.72f;

            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            return bank;
        }

        static VoiceBank.Set Set(string who) => new VoiceBank.Set
        {
            alert = Voice(who, "alert"),
            pain = Voice(who, "pain"),
            hurt = Voice(who, "hurt"),
            death = Voice(who, "death"),
            headshotDeath = Voice(who, "headshot"),
            hunt = Voice(who, "hunt"),
            attack = Voice(who, "attack"),
            crawl = Voice(who, "crawl"),
        };

        static AudioClip[] Voice(string who, string what)
        {
            var clips = new System.Collections.Generic.List<AudioClip>();

            for (int i = 1; i <= 40; i++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{VoiceFolder}/{who}_{what}_{i:00}.wav")
                        ?? AssetDatabase.LoadAssetAtPath<AudioClip>($"{VoiceFolder}/{who}_{what}_{i:00}.ogg");
                if (clip != null) clips.Add(clip);
            }

            if (clips.Count == 0)
                Debug.LogWarning($"[FPSKit] No {who} {what} voice clips in {VoiceFolder}. " +
                                 "They are recordings, not generated: see Docs/DESIGN_NOTES.md.");

            return clips.ToArray();
        }

        // ==================================================================
        // Materials
        // ==================================================================

        /// <summary>
        /// A lit, alpha-blended surface for blood lying on something. Lit, not unlit: a
        /// splat in a dark subway corner has to be as dark as the corner, and an unlit one
        /// glows. Blend state written out for the reason FPSKitVolcanic's particles give:
        /// _Surface alone does nothing outside the inspector.
        /// </summary>
        static Material Surface(string name, Texture2D texture, Color color, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = LoadOrCreate(name, shader);

            mat.mainTexture = texture;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 1f);

            Transparent(mat);

            // No sky in it. A flat transparent surface picks up the environment reflection
            // across its whole face, and a blue sky over dark red comes out pink -- which is
            // exactly how the first splats looked. The highlight from the light stays, so a
            // pool still reads wet.
            if (mat.HasProperty("_EnvironmentReflections")) mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");

            // Blood lies on a surface; it does not cast a shadow onto it.
            mat.SetShaderPassEnabled("ShadowCaster", false);
            mat.SetShaderPassEnabled("DepthOnly", false);
            mat.SetShaderPassEnabled("DepthNormals", false);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material Particle(string name, Texture2D texture)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default");
            var mat = LoadOrCreate(name, shader);

            mat.mainTexture = texture;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);

            // White: each particle carries its own colour, set by BloodFX per emit, so the
            // Blood setting can turn the same mist grey.
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);

            Transparent(mat);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void Transparent(Material mat)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);

            // URP's transparent Lit keeps its specular at full strength by default, even
            // where alpha is zero -- so every splat drew the sky's reflection across its
            // whole square, a pale tile on the floor with a blood stain in the middle of
            // it. Off, and the blend factors follow (the material validator re-derives
            // _SrcBlend from this on import: One with it on, SrcAlpha with it off).
            if (mat.HasProperty("_BlendModePreserveSpecular")) mat.SetFloat("_BlendModePreserveSpecular", 0f);
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }

        static Material LoadOrCreate(string name, Shader shader)
        {
            string path = $"{Folder}/{name}.mat";

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            return mat;
        }

        // ==================================================================
        // Particles
        // ==================================================================

        /// <summary>
        /// A particle system that is only ever emitted into from code (see BloodFX): no
        /// emission, no shape, never played. It only decides how a particle, once made,
        /// falls and fades.
        /// </summary>
        static ParticleSystem ParticlePrefab(string name, Material material, bool spray)
        {
            string path = $"{Folder}/{name}.prefab";

            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = spray ? 800 : 160;
            main.startSpeed = 0f;
            main.startLifetime = spray ? 0.6f : 0.35f;
            main.gravityModifier = spray ? 1.6f : 0.08f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = false;

            // Mist spreads and thins; droplets shrink a little as they fly.
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, spray
                ? AnimationCurve.Linear(0f, 1f, 1f, 0.6f)
                : new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.8f)));

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(spray ? 1f : 0.6f, 0.4f),
                                 new GradientAlphaKey(0f, 1f) });
            colour.color = fade;

            if (!spray)
            {
                var drag = ps.limitVelocityOverLifetime;
                drag.enabled = true;
                drag.drag = 4f;
                drag.multiplyDragByParticleSize = false;
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.None;

            if (spray)
            {
                // Droplets are streaks, not dots: stretched along their own velocity.
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.035f;
                renderer.lengthScale = 1.2f;
            }
            else
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);

            return prefab.GetComponent<ParticleSystem>();
        }

        // ==================================================================
        // Textures
        // ==================================================================

        delegate void Paint(float[] alpha, float[] shade, int size, System.Random rng);

        /// <summary>Writes a texture if it is not there yet, and returns it.</summary>
        static Texture2D Texture(string name, int size, Paint paint)
        {
            string path = $"{Folder}/{name}.png";

            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            var alpha = new float[size * size];
            var shade = new float[size * size];
            for (int i = 0; i < shade.Length; i++) shade[i] = 1f;

            paint(alpha, shade, size, new System.Random(Seed(name)));

            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(shade[i]) * 255f);
                pixels[i] = new Color32(g, g, g, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha[i]) * 255f));
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.sRGBTexture = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.maxTextureSize = size;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// A string of names is not a seed: string.GetHashCode differs between runtimes, so
        /// the seed is taken from the name's characters instead.
        /// </summary>
        static int Seed(string name)
        {
            int h = 17;
            foreach (char c in name) h = h * 31 + c;
            return h & 0x7fffffff;
        }

        /// <summary>Four splats in a 2x2 atlas, each different: a burst, a spray, a fling and a drip.</summary>
        static void SplatAtlas(float[] alpha, float[] shade, int size, System.Random rng)
        {
            int tile = size / 2;

            for (int t = 0; t < 4; t++)
            {
                int ox = (t % 2) * tile, oy = (t / 2) * tile;
                var local = new System.Random(Seed("splat" + t));

                Blob(alpha, shade, size, ox, oy, tile, local,
                     radius: 0.2f + 0.05f * t, lobes: 0.28f, droplets: 10 + 6 * t, streaks: t == 1 || t == 2 ? 5 : 2,
                     fling: t == 2);
            }
        }

        static void Pool(float[] alpha, float[] shade, int size, System.Random rng)
        {
            var local = new System.Random(Seed("pool"));
            Blob(alpha, shade, size, 0, 0, size, local, radius: 0.36f, lobes: 0.18f, droplets: 6, streaks: 0,
                 fling: false);
        }

        /// <summary>
        /// A splat in one tile: a lumpy central blob, droplets thrown out around it, and
        /// streaks where a jet of it hit at an angle. Thicker blood is darker, so the
        /// centre of each shape is shaded down and its edges are thin.
        /// </summary>
        static void Blob(float[] alpha, float[] shade, int size, int ox, int oy, int tile, System.Random rng,
                         float radius, float lobes, int droplets, int streaks, bool fling)
        {
            // The outline: a radius that wanders with angle, from a few random harmonics.
            var phases = new float[6];
            var amps = new float[6];
            for (int h = 0; h < phases.Length; h++)
            {
                phases[h] = (float)rng.NextDouble() * Mathf.PI * 2f;
                amps[h] = lobes * (float)rng.NextDouble() / (h + 1);
            }

            Vector2 centre = new Vector2(0.5f, 0.5f);
            if (fling) centre = new Vector2(0.38f, 0.5f);

            for (int y = 0; y < tile; y++)
            {
                for (int x = 0; x < tile; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / tile, (y + 0.5f) / tile) - centre;
                    float angle = Mathf.Atan2(p.y, p.x);

                    float r = radius;
                    for (int h = 0; h < phases.Length; h++)
                        r += radius * amps[h] * Mathf.Sin(angle * (h + 2) + phases[h]);

                    if (fling) r *= 1f + 0.5f * Mathf.Max(0f, Mathf.Cos(angle));

                    float d = p.magnitude / Mathf.Max(0.01f, r);
                    float a = Mathf.Clamp01((1f - d) * 18f);

                    Put(alpha, shade, size, ox + x, oy + y, a, Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(d)));
                }
            }

            for (int i = 0; i < droplets; i++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                if (fling) angle = ((float)rng.NextDouble() - 0.5f) * 1.6f;
                float distance = radius * (1.1f + (float)rng.NextDouble() * (fling ? 1.9f : 1.1f));
                float r = radius * (0.04f + 0.14f * (float)rng.NextDouble() * (1f - (distance - radius) / (radius * 3f)));

                Disc(alpha, shade, size, ox, oy, tile,
                     centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance, r, r);
            }

            for (int i = 0; i < streaks; i++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                if (fling) angle = ((float)rng.NextDouble() - 0.5f) * 0.9f;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float length = radius * (0.8f + (float)rng.NextDouble() * 1.2f);

                // A tapering run of discs from the edge outward.
                int steps = 18;
                for (int s = 0; s < steps; s++)
                {
                    float f = s / (float)(steps - 1);
                    float w = radius * 0.1f * (1f - f * 0.85f);
                    Disc(alpha, shade, size, ox, oy, tile, centre + dir * (radius * 0.8f + length * f), w, w);
                }
            }
        }

        static void Wound(float[] alpha, float[] shade, int size, System.Random rng)
        {
            var local = new System.Random(Seed("wound"));
            var phases = new float[5];
            for (int h = 0; h < phases.Length; h++) phases[h] = (float)local.NextDouble() * Mathf.PI * 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f);
                    float angle = Mathf.Atan2(p.y, p.x);
                    float d = p.magnitude;

                    float rim = 0.3f;
                    for (int h = 0; h < phases.Length; h++)
                        rim += 0.05f / (h + 1) * Mathf.Sin(angle * (h + 3) + phases[h]);

                    // The hole: nearly black. The torn rim: red. The edge: ragged.
                    float hole = Mathf.Clamp01((0.09f - d) * 60f);
                    float a = Mathf.Clamp01((rim - d) * 30f);
                    float s = Mathf.Lerp(1f, 0.08f, hole);
                    s *= Mathf.Lerp(0.8f, 1f, Mathf.Clamp01(d / rim));

                    int i = y * size + x;
                    alpha[i] = a;
                    shade[i] = s;
                }
            }

            // Flecks around it.
            for (int i = 0; i < 14; i++)
            {
                float angle = (float)local.NextDouble() * Mathf.PI * 2f;
                float distance = 0.3f + (float)local.NextDouble() * 0.16f;
                float r = 0.01f + 0.025f * (float)local.NextDouble();
                Disc(alpha, shade, size, 0, 0, size,
                     new Vector2(0.5f, 0.5f) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance, r, r);
            }
        }

        static void Drop(float[] alpha, float[] shade, int size, System.Random rng)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f).magnitude * 2f;
                    int i = y * size + x;
                    alpha[i] = Mathf.Clamp01((1f - d) * 3f);
                    shade[i] = Mathf.Lerp(0.8f, 1f, d);
                }
        }

        static void Mist(float[] alpha, float[] shade, int size, System.Random rng)
        {
            var local = new System.Random(Seed("mist"));
            var blobs = new Vector3[9];
            for (int b = 0; b < blobs.Length; b++)
                blobs[b] = new Vector3(0.3f + 0.4f * (float)local.NextDouble(), 0.3f + 0.4f * (float)local.NextDouble(),
                                       0.12f + 0.12f * (float)local.NextDouble());

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    float a = 0f;
                    foreach (var b in blobs)
                    {
                        float d = (p - new Vector2(b.x, b.y)).magnitude / b.z;
                        a += Mathf.Exp(-d * d * 2f) * 0.35f;
                    }

                    // Nothing may reach the edge of the quad, or the square shows.
                    float edge = (p - new Vector2(0.5f, 0.5f)).magnitude * 2f;
                    a *= Mathf.Clamp01((1f - edge) * 2.5f);

                    int i = y * size + x;
                    alpha[i] = Mathf.Clamp01(a);
                    shade[i] = 1f;
                }
        }

        /// <summary>An ellipse of full alpha, soft at the edge, within one tile.</summary>
        static void Disc(float[] alpha, float[] shade, int size, int ox, int oy, int tile, Vector2 centre, float rx, float ry)
        {
            int x0 = Mathf.FloorToInt((centre.x - rx) * tile) - 1, x1 = Mathf.CeilToInt((centre.x + rx) * tile) + 1;
            int y0 = Mathf.FloorToInt((centre.y - ry) * tile) - 1, y1 = Mathf.CeilToInt((centre.y + ry) * tile) + 1;

            for (int y = Mathf.Max(0, y0); y < Mathf.Min(tile, y1); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(tile, x1); x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / tile - centre.x, (y + 0.5f) / tile - centre.y);
                    float d = Mathf.Sqrt(p.x * p.x / (rx * rx) + p.y * p.y / (ry * ry));
                    float a = Mathf.Clamp01((1f - d) * 6f);
                    Put(alpha, shade, size, ox + x, oy + y, a, 0.85f);
                }
        }

        /// <summary>Lays alpha over what is there, keeping the darker shade where two overlap.</summary>
        static void Put(float[] alpha, float[] shade, int size, int x, int y, float a, float s)
        {
            if (a <= 0f || x < 0 || y < 0 || x >= size || y >= size) return;

            int i = y * size + x;
            if (a > alpha[i]) shade[i] = alpha[i] > 0f ? Mathf.Min(shade[i], s) : s;
            alpha[i] = Mathf.Max(alpha[i], a);
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/FPSKit_Generated", "Gore");
        }
    }
}
#endif
