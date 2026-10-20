#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Everything the desert town is dressed in, made ready for URP once and reused by every build.
    ///
    /// <b>Two kinds of source, one rule: the packs are never edited.</b>
    ///   * Poly Haven (CC0, Assets/ThirdParty/PolyHaven, fetched and repacked by Tools/polyhaven): photo-scanned
    ///     props and plants as FBX with one albedo/normal/mask set each, 2k ground and wall textures, one sky.
    ///     The FBX materials are remapped to URP Lit materials built from those maps, and each model becomes a
    ///     prefab under FPSKit_Generated/Town/Props with a collider and a cull distance.
    ///   * Store packs the user imported (cars, palms, the civilian man, the .50 cal): their materials are
    ///     Built-in Standard or Legacy and render pink in URP, so each prefab is cloned with URP copies of its
    ///     materials (<see cref="UrpCopy"/>) into FPSKit_Generated/Town/Packs. A pack that is missing is
    ///     skipped and the town builds without it -- the builder treats a null prefab as "leave it out".
    ///
    /// The mask map is the URP layout (R metal, G occlusion, B height, A smoothness), which Lit reads as its
    /// metallic-gloss and occlusion maps and Terrain Lit as its mask map, so one texture serves both.
    ///
    ///   FPSKit > Town > Prepare Assets      (FPSKitBatch.PrepareTownAssets; the town build also calls it)
    ///   FPSKit > Town > Probe Assets        (FPSKitBatch.ProbeTownAssets: sizes of everything, Build/TownProbe.txt)
    /// </summary>
    public static class FPSKitTownAssets
    {
        public const string PolyHavenRoot = "Assets/ThirdParty/PolyHaven";
        public const string Out = "Assets/FPSKit_Generated/Town";
        public const string PropFolder = Out + "/Props";
        public const string PackFolder = Out + "/Packs";
        public const string MatFolder = Out + "/Materials";

        // ==================================================================
        // Entry
        // ==================================================================
        [MenuItem("FPSKit/Town/Prepare Assets", false, 60)]
        public static void PrepareMenu() => Prepare();

        /// <summary>Idempotent: settings already right are left alone, prefabs already made are kept.</summary>
        public static void Prepare(bool rebuildPrefabs = false)
        {
            FairgroundMaterialConverter.EnsureFolder(PropFolder);
            FairgroundMaterialConverter.EnsureFolder(PackFolder);
            FairgroundMaterialConverter.EnsureFolder(MatFolder);

            AssetDatabase.StartAssetEditing();
            try
            {
                ConfigureTextures();
                ConfigureSky();
                ConfigureModels();
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();

            int props = MakePropPrefabs(rebuildPrefabs);
            int packs = MakePackPrefabs(rebuildPrefabs);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FPSKit] town assets ready: {props} Poly Haven props, {packs} pack prefabs (URP).");
        }

        // ==================================================================
        // Textures
        // ==================================================================
        /// <summary>Import settings for every map by its suffix: albedo sRGB, normal as a normal map, mask linear.</summary>
        static void ConfigureTextures()
        {
            if (!AssetDatabase.IsValidFolder(PolyHavenRoot)) return;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { PolyHavenRoot + "/Textures", PolyHavenRoot + "/Models" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) continue;
                string file = Path.GetFileNameWithoutExtension(path);
                bool ground = path.Contains("/Textures/");
                bool normal = file.EndsWith("_nor");
                bool mask = file.EndsWith("_mask");

                bool dirty = false;
                void Set<T>(T now, T want, System.Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(now, want)) { apply(want); dirty = true; } }

                Set(imp.textureType, normal ? TextureImporterType.NormalMap : TextureImporterType.Default, v => imp.textureType = v);
                Set(imp.sRGBTexture, !normal && !mask, v => imp.sRGBTexture = v);
                Set(imp.maxTextureSize, ground ? 2048 : 1024, v => imp.maxTextureSize = v);
                Set(imp.mipmapEnabled, true, v => imp.mipmapEnabled = v);
                Set(imp.anisoLevel, ground ? 8 : 4, v => imp.anisoLevel = v);
                Set(imp.textureCompression, TextureImporterCompression.Compressed, v => imp.textureCompression = v);
                Set(imp.wrapMode, TextureWrapMode.Repeat, v => imp.wrapMode = v);
                // An albedo with alpha is a plant's leaf cut-out: keep the alpha and stop it bleeding at the edge.
                if (!normal && !mask && path.EndsWith(".png"))
                {
                    Set(imp.alphaIsTransparency, true, v => imp.alphaIsTransparency = v);
                    Set(imp.mipMapsPreserveCoverage, true, v => imp.mipMapsPreserveCoverage = v);
                    Set(imp.alphaTestReferenceValue, 0.5f, v => imp.alphaTestReferenceValue = v);
                }
                if (dirty) imp.SaveAndReimport();
            }
        }

        /// <summary>The sky: a latitude-longitude HDR for Skybox/Panoramic. No mipmaps -- they draw a seam where the image wraps.</summary>
        static void ConfigureSky()
        {
            foreach (var path in SkyPaths())
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter imp)) continue;
                bool dirty = imp.textureShape != TextureImporterShape.Texture2D || imp.mipmapEnabled ||
                             imp.maxTextureSize != 4096 || imp.wrapModeU != TextureWrapMode.Repeat ||
                             imp.textureCompression != TextureImporterCompression.CompressedHQ;
                if (!dirty) continue;
                imp.textureShape = TextureImporterShape.Texture2D;
                imp.mipmapEnabled = false;
                imp.maxTextureSize = 4096;
                imp.wrapModeU = TextureWrapMode.Repeat;
                imp.wrapModeV = TextureWrapMode.Clamp;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
            }
        }

        /// <summary>
        /// The sky as a cubemap for reflections: a copy of the HDR imported as a cube (from its latitude-longitude layout).
        /// Nothing in these scenes bakes a reflection probe, so without it glass, water and car paint reflect a grey default.
        /// </summary>
        public static Cubemap ReflectionCube()
        {
            foreach (var sky in SkyPaths())
            {
                string cube = sky.Replace(".hdr", "_cube.hdr");
                if (!File.Exists(cube)) { File.Copy(sky, cube); AssetDatabase.ImportAsset(cube); }
                if (AssetImporter.GetAtPath(cube) is TextureImporter imp &&
                    (imp.textureShape != TextureImporterShape.TextureCube || imp.maxTextureSize != 512 || !imp.mipmapEnabled))
                {
                    imp.textureShape = TextureImporterShape.TextureCube;
                    imp.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
                    imp.mipmapEnabled = true;
                    imp.maxTextureSize = 512;
                    imp.textureCompression = TextureImporterCompression.CompressedHQ;
                    imp.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Cubemap>(cube);
            }
            return null;
        }

        public static IEnumerable<string> SkyPaths()
        {
            string dir = PolyHavenRoot + "/HDRI";
            if (!Directory.Exists(dir)) yield break;
            foreach (var f in Directory.GetFiles(dir, "*.hdr"))
                if (!f.EndsWith("_cube.hdr")) yield return f.Replace('\\', '/');
        }

        // ==================================================================
        // Poly Haven models
        // ==================================================================
        /// <summary>Model import: metres, Y up, no cameras or lights, materials remapped to ours.</summary>
        static void ConfigureModels()
        {
            foreach (var fbx in PolyHavenModels())
            {
                if (!(AssetImporter.GetAtPath(fbx) is ModelImporter imp)) continue;
                bool dirty = false;
                if (!imp.bakeAxisConversion) { imp.bakeAxisConversion = true; dirty = true; }
                if (imp.importCameras || imp.importLights) { imp.importCameras = false; imp.importLights = false; dirty = true; }
                if (imp.importAnimation) { imp.importAnimation = false; dirty = true; }
                if (imp.animationType != ModelImporterAnimationType.None) { imp.animationType = ModelImporterAnimationType.None; dirty = true; }
                if (imp.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription)
                { imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription; dirty = true; }
                if (imp.meshCompression != ModelImporterMeshCompression.Low) { imp.meshCompression = ModelImporterMeshCompression.Low; dirty = true; }
                if (dirty) imp.SaveAndReimport();
            }
        }

        public static IEnumerable<string> PolyHavenModels()
        {
            string dir = PolyHavenRoot + "/Models";
            if (!Directory.Exists(dir)) yield break;
            foreach (var d in Directory.GetDirectories(dir))
            {
                string id = Path.GetFileName(d);
                string fbx = $"{dir}/{id}/{id}.fbx";
                if (File.Exists(fbx)) yield return fbx;
            }
        }

        /// <summary>The texture sets beside a model: prefix -> (albedo, normal, mask, has alpha).</summary>
        static Dictionary<string, (Texture2D diff, Texture2D nor, Texture2D mask)> SetsFor(string fbx)
        {
            var sets = new Dictionary<string, (Texture2D, Texture2D, Texture2D)>();
            string dir = Path.GetDirectoryName(fbx).Replace('\\', '/');
            foreach (var f in Directory.GetFiles(dir))
            {
                string file = Path.GetFileNameWithoutExtension(f);
                if (!file.EndsWith("_diff") || f.EndsWith(".meta")) continue;
                string prefix = file.Substring(0, file.Length - 5);
                Texture2D Load(string suffix)
                {
                    foreach (var ext in new[] { ".png", ".jpg" })
                    {
                        var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{prefix}{suffix}{ext}");
                        if (t != null) return t;
                    }
                    return null;
                }
                sets[prefix] = (Load("_diff"), Load("_nor"), Load("_mask"));
            }
            return sets;
        }

        /// <summary>
        /// One prefab per model: our materials, a collider sized to what it is, and a cull distance so a
        /// teacup is not drawn from across the town. Props smaller than a chair get a box collider; bigger
        /// things keep their mesh (non-convex, static) so a bullet hits the shape that is drawn.
        /// </summary>
        static int MakePropPrefabs(bool rebuild)
        {
            int made = 0;
            foreach (var fbx in PolyHavenModels())
            {
                string id = Path.GetFileNameWithoutExtension(fbx);
                string prefabPath = $"{PropFolder}/{id}.prefab";
                if (!rebuild && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) { made++; continue; }

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                if (model == null) { Debug.LogWarning($"[FPSKit] Poly Haven model did not import: {fbx}"); continue; }

                RemapMaterials(fbx, id);
                model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);

                var model0 = (GameObject)PrefabUtility.InstantiatePrefab(model);
                var renderers = model0.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) { Object.DestroyImmediate(model0); continue; }
                var go = new GameObject(id);
                model0.transform.SetParent(go.transform, false);

                // Stand it up. Poly Haven models rest on the ground (z = 0 in Blender), but their FBX files do not agree on
                // the up axis: about half import lying on their side, the top pointing along -z or +z. A model whose bounds
                // do not start at y = 0 but end (or start) at z = 0 is turned a quarter about x so that face is the floor.
                var b = Bounds(renderers);
                if (Mathf.Abs(b.min.y) > 0.02f)
                {
                    if (Mathf.Abs(b.max.z) < 0.03f && b.min.z < -0.05f) model0.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    else if (Mathf.Abs(b.min.z) < 0.03f && b.max.z > 0.05f) model0.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    b = Bounds(renderers);
                }
                bool plant = IsPlant(id);

                foreach (var r in renderers)
                {
                    r.shadowCastingMode = b.size.magnitude < 0.25f ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    r.receiveShadows = true;
                }

                // Colliders. Plants are walked through (a bush that stops a bullet dead reads as a wall).
                if (!plant)
                {
                    if (b.size.magnitude < 2.2f)
                    {
                        var box = go.AddComponent<BoxCollider>();
                        box.center = go.transform.InverseTransformPoint(b.center);
                        box.size = b.size;
                    }
                    else
                        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                            mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                }

                // Culled when it covers less than this share of the screen's height.
                // Models that ship their own levels of detail (some Poly Haven ones import with an LODGroup) keep them; only
                // the last level's cut-off is set.
                float cull = b.size.magnitude < 0.5f ? 0.03f : b.size.magnitude < 2f ? 0.015f : 0.004f;
                // Dense scans (a boulder is 60-110k triangles) are not drawn from as far: a rock across the town is a
                // few pixels, and two hundred of them at full detail cost millions of triangles a frame.
                int tris = 0;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                if (tris > 150000) cull = Mathf.Max(cull, 0.05f);
                else if (tris > 40000) cull = Mathf.Max(cull, 0.025f);
                var lod = go.GetComponentInChildren<LODGroup>();
                if (lod == null)
                {
                    lod = go.AddComponent<LODGroup>();
                    lod.SetLODs(new[] { new LOD(cull, renderers) });
                }
                else
                {
                    var lods = lod.GetLODs();
                    if (lods.Length > 0)
                    {
                        lods[lods.Length - 1].screenRelativeTransitionHeight = Mathf.Min(cull, lods.Length > 1 ? lods[lods.Length - 2].screenRelativeTransitionHeight * 0.9f : cull);
                        lod.SetLODs(lods);
                    }
                }
                lod.RecalculateBounds();

                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                                                           StaticEditorFlags.OccludeeStatic);
                PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                Object.DestroyImmediate(go);
                made++;
            }
            return made;
        }

        static Bounds Bounds(Renderer[] rs)
        {
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static bool IsPlant(string id) => id.Contains("searsia") || id.Contains("rooibos") || id.Contains("succulent") ||
                                          id.Contains("didelta") || id.Contains("othonna") || id.Contains("branch");

        /// <summary>Points each FBX material at our URP Lit copy built from the best-matching texture set.</summary>
        static void RemapMaterials(string fbx, string id)
        {
            var sets = SetsFor(fbx);
            if (sets.Count == 0) return;
            var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
            var embedded = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Material>().ToList();
            var map = imp.GetExternalObjectMap();
            bool changed = false;

            foreach (var m in embedded)
            {
                string key = sets.Keys.FirstOrDefault(k => string.Equals(k, m.name, System.StringComparison.OrdinalIgnoreCase))
                          ?? sets.Keys.FirstOrDefault(k => m.name.ToLowerInvariant().Contains(k.ToLowerInvariant()))
                          ?? sets.Keys.FirstOrDefault(k => k.ToLowerInvariant().Contains(m.name.ToLowerInvariant()))
                          ?? sets.Keys.OrderBy(k => k.Length).First();
                var s = sets[key];
                var mat = LitFromMaps($"{MatFolder}/PH_{key}.mat", s.diff, s.nor, s.mask, Color.white, Vector2.one,
                                      IsPlant(id) || HasAlpha(s.diff));
                var source = new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name);
                if (map.TryGetValue(source, out var existing) && existing == mat) continue;
                imp.AddRemap(source, mat);
                changed = true;
            }
            // Materials the importer did not expose (none embedded): nothing to remap, the prefab pass assigns.
            if (changed) imp.SaveAndReimport();
        }

        static bool HasAlpha(Texture2D t)
        {
            if (t == null) return false;
            var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter;
            return imp != null && imp.DoesSourceTextureHaveAlpha() && AssetDatabase.GetAssetPath(t).EndsWith(".png");
        }

        /// <summary>
        /// A URP Lit material from an albedo, a normal and a packed mask. Kept in step with the textures on every
        /// call (so a retint or retile in code reaches the asset), and created once.
        /// </summary>
        public static Material LitFromMaps(string path, Texture2D diff, Texture2D nor, Texture2D mask, Color tint,
                                           Vector2 tiling, bool alphaClip, float smoothness = 1f, bool twoSided = false)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = mat == null;
            if (create) mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));

            mat.SetTexture("_BaseMap", diff);
            mat.SetTextureScale("_BaseMap", tiling);
            mat.SetColor("_BaseColor", tint);
            if (nor != null) { mat.SetTexture("_BumpMap", nor); mat.EnableKeyword("_NORMALMAP"); }
            else mat.DisableKeyword("_NORMALMAP");
            if (mask != null)
            {
                mat.SetTexture("_MetallicGlossMap", mask);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetTexture("_OcclusionMap", mask);
                mat.EnableKeyword("_OCCLUSIONMAP");
                mat.SetFloat("_OcclusionStrength", 1f);
                mat.SetFloat("_Smoothness", smoothness);
            }
            else
            {
                mat.SetTexture("_MetallicGlossMap", null);
                mat.DisableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetTexture("_OcclusionMap", null);
                mat.DisableKeyword("_OCCLUSIONMAP");
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Smoothness", 0.25f * smoothness);
            }
            if (alphaClip)
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetOverrideTag("RenderType", "TransparentCutout");
                mat.renderQueue = (int)RenderQueue.AlphaTest;
                twoSided = true;
            }
            mat.SetFloat("_Cull", twoSided ? 0f : 2f);
            mat.enableInstancing = true;

            if (create) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        // ==================================================================
        // Store packs
        // ==================================================================
        /// <summary>
        /// The pack prefabs the town uses, by the name the builder asks for. Each is cloned with URP copies of
        /// its materials; the pack's own files are not touched.
        /// </summary>
        public static readonly (string name, string source)[] PackPrefabs =
        {
            ("Car_Sedan_A", "Assets/RealisticMobileCars - Pro3DModels/RMCar26/Prefabs/RMCar26.prefab"),
            ("Car_Sedan_B", "Assets/RealisticMobileCars - Pro3DModels/RMCar26/Prefabs/RMCar26_B.prefab"),
            ("Car_Sedan_C", "Assets/RealisticMobileCars - Pro3DModels/RMCar26/Prefabs/RMCar26_C.prefab"),
            ("Car_Sedan_D", "Assets/RealisticMobileCars - Pro3DModels/RMCar26/Prefabs/RMCar26_D.prefab"),
            ("Car_Sport", "Assets/SportCar/Prefabs/SportCar_1.prefab"),
            ("Car_Touring", "Assets/Tourung Car Pack/Prefab/Chev666.prefab"),
            ("Palm_SingleBended", "Assets/Yughues Free Palm Trees/Prefabs/PalmTreeSingleBendedTwoSided.prefab"),
            ("Palm_SingleStraight", "Assets/Yughues Free Palm Trees/Prefabs/PalmTreeSingleStraightTwoSided.prefab"),
            ("Palm_DualBended", "Assets/Yughues Free Palm Trees/Prefabs/PalmTreeDualBendedTwoSided.prefab"),
            ("Palm_DualStraight", "Assets/Yughues Free Palm Trees/Prefabs/PalmTreeDualStraightTwoSided.prefab"),
            ("Palm_Trio", "Assets/Yughues Free Palm Trees/Prefabs/PalmTreeTrioTwoSided.prefab"),
            ("Civilian_Man_01", "Assets/Adventure_Character/Prefabs/Man_01.prefab"),
            ("Civilian_Man_02", "Assets/Adventure_Character/Prefabs/Man_02.prefab"),
            ("Civilian_Man_03", "Assets/Adventure_Character/Prefabs/Man_03.prefab"),
            ("Civilian_Man_04", "Assets/Adventure_Character/Prefabs/Man_04.prefab"),
            ("Gun_M2", "Assets/Low Poly Weapons VOL.1/Prefabs/M2_50cal.prefab"),
        };

        static int MakePackPrefabs(bool rebuild)
        {
            // The civilian's textures are 4k; a body seen from two metres needs 2k at most.
            foreach (var dir in new[] { "Assets/Adventure_Character/Textures", "Assets/Yughues Free Palm Trees/Textures" })
            {
                if (!AssetDatabase.IsValidFolder(dir)) continue;
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
                    if (AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is TextureImporter t && t.maxTextureSize > 2048)
                    { t.maxTextureSize = 2048; t.SaveAndReimport(); }
            }

            int made = 0;
            foreach (var (name, source) in PackPrefabs)
            {
                string dst = $"{PackFolder}/{name}.prefab";
                if (!rebuild && AssetDatabase.LoadAssetAtPath<GameObject>(dst) != null) { made++; continue; }
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(source);
                if (src == null) { Debug.LogWarning($"[FPSKit] town pack prefab missing, left out: {source}"); continue; }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.name = name;
                go.transform.position = Vector3.zero;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = UrpCopy(mats[i], name.Split('_')[0]);
                    r.sharedMaterials = mats;
                }
                // Cameras, lights and scripts that came with a demo prefab have no place in the town.
                foreach (var c in go.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(c);
                foreach (var l in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
                PrefabUtility.SaveAsPrefabAsset(go, dst);
                Object.DestroyImmediate(go);
                made++;
            }
            return made;
        }

        /// <summary>
        /// A URP Lit copy of a Built-in material (Standard, Standard Specular, Legacy diffuse/bumped/cutout, a pack's
        /// own surface shader): albedo, tint, normal, metallic-gloss, occlusion, emission, and its blend mode --
        /// opaque, cut-out (leaves) or transparent (glass). A material already on a URP shader is returned as it is.
        /// </summary>
        public static Material UrpCopy(Material src, string group)
        {
            if (src == null) return null;
            string sn = src.shader != null ? src.shader.name : "";
            if (sn.StartsWith("Universal Render Pipeline") || sn.StartsWith("Shader Graphs")) return src;

            string folder = $"{MatFolder}/{group}";
            FairgroundMaterialConverter.EnsureFolder(folder);
            string path = $"{folder}/{SafeFile(src.name)}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            var m = existing != null ? existing : new Material(Shader.Find("Universal Render Pipeline/Lit"));

            Texture Tex(string p) => src.HasProperty(p) ? src.GetTexture(p) : null;
            Color Col(string p, Color d) => src.HasProperty(p) ? src.GetColor(p) : d;
            float F(string p, float d) => src.HasProperty(p) ? src.GetFloat(p) : d;

            var albedo = Tex("_MainTex");
            m.SetTexture("_BaseMap", albedo);
            if (src.HasProperty("_MainTex")) { m.SetTextureScale("_BaseMap", src.GetTextureScale("_MainTex")); m.SetTextureOffset("_BaseMap", src.GetTextureOffset("_MainTex")); }
            m.SetColor("_BaseColor", Col("_Color", Color.white));

            var bump = Tex("_BumpMap");
            m.SetTexture("_BumpMap", bump);
            m.SetFloat("_BumpScale", F("_BumpScale", 1f));
            if (bump != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");

            var gloss = Tex("_MetallicGlossMap");
            m.SetTexture("_MetallicGlossMap", gloss);
            if (gloss != null) { m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", F("_GlossMapScale", 1f)); }
            else
            {
                m.DisableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Metallic", F("_Metallic", 0f));
                // Legacy shaders carry _Shininess, not smoothness; a matte default reads right in sunlight.
                m.SetFloat("_Smoothness", src.HasProperty("_Glossiness") ? F("_Glossiness", 0.3f) : 0.2f);
            }

            var occ = Tex("_OcclusionMap");
            m.SetTexture("_OcclusionMap", occ);
            if (occ != null) { m.EnableKeyword("_OCCLUSIONMAP"); m.SetFloat("_OcclusionStrength", F("_OcclusionStrength", 1f)); }

            if (src.IsKeywordEnabled("_EMISSION"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", Tex("_EmissionMap"));
                m.SetColor("_EmissionColor", Col("_EmissionColor", Color.black));
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            float mode = F("_Mode", -1f);
            string low = (src.name + " " + sn).ToLowerInvariant();
            bool cutout = mode == 1f || low.Contains("cutout") || low.Contains("leaf") || low.Contains("leaves");
            bool transparent = mode == 2f || mode == 3f || (!cutout && (low.Contains("transparent") || low.Contains("glass") || low.Contains("window")));

            m.SetFloat("_AlphaClip", 0f); m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Surface", 0f); m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Opaque");
            m.renderQueue = -1;
            m.SetFloat("_SrcBlend", 1f); m.SetFloat("_DstBlend", 0f); m.SetFloat("_ZWrite", 1f);
            m.SetFloat("_Cull", 2f);

            if (cutout)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", Mathf.Max(0.3f, F("_Cutoff", 0.5f)));
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
                // A model that ships both faces of each leaf (the "TwoSided" palms) must cull, or the flipped copy draws over
                // the lit one and the whole crown goes black.
                m.SetFloat("_Cull", group == "Palm" ? 2f : 0f);
                m.SetFloat("_Smoothness", 0.15f);
            }
            else if (transparent)
            {
                // Glass: premultiplied so the reflection stays when the tint is nearly clear.
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 1f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetFloat("_SrcBlend", 1f); m.SetFloat("_DstBlend", 10f); m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_Smoothness", 0.92f);
                var c = m.GetColor("_BaseColor");
                if (c.a > 0.6f) { c.a = 0.35f; m.SetColor("_BaseColor", c); }
            }
            // The palm pack's atlas is very dark (its leaves average about 40/55/30 of 255; the Built-in shader it was made for
            // was lit far brighter than URP's physically based one). Lifted into a dusty date-palm green and a sun-bleached trunk.
            if (group == "Palm")
                m.SetColor("_BaseColor", cutout ? new Color(1.6f, 1.55f, 1.15f) : new Color(1.55f, 1.45f, 1.3f));
            m.enableInstancing = true;

            if (existing == null) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
            return m;
        }

        static string SafeFile(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        // ==================================================================
        // MVC (BxB Multiversal Vehicle Controller) API dump
        // ==================================================================
        /// <summary>
        /// Writes the public types and members of the vehicle controller's assemblies (they ship as DLLs with no source),
        /// so the drivable car can be wired from code against what is really there.
        /// </summary>
        public static void DumpVehicleApi(string outPath)
        {
            var sb = new StringBuilder();
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                string n = asm.GetName().Name;
                if (!n.StartsWith("MVC") && !n.Contains("InputsManager") && !n.Contains("BxB") && !n.Contains("Utilities")) continue;
                sb.AppendLine($"#### {n}");
                System.Type[] types;
                try { types = asm.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (var t in types.Where(t => t.IsPublic).OrderBy(t => t.FullName))
                {
                    sb.AppendLine($"{(t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsAbstract ? "abstract class" : "class")} {t.FullName} : {t.BaseType?.Name}");
                    const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                                                            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
                    if (t.IsEnum) { sb.AppendLine("    " + string.Join(", ", System.Enum.GetNames(t))); continue; }
                    foreach (var f in t.GetFields(F)) sb.AppendLine($"    field {(f.IsStatic ? "static " : "")}{f.FieldType.Name} {f.Name}");
                    foreach (var pr in t.GetProperties(F)) sb.AppendLine($"    prop {pr.PropertyType.Name} {pr.Name} {(pr.CanWrite ? "{get;set;}" : "{get;}")}");
                    foreach (var me in t.GetMethods(F).Where(me => !me.IsSpecialName))
                        sb.AppendLine($"    method {(me.IsStatic ? "static " : "")}{me.ReturnType.Name} {me.Name}({string.Join(", ", me.GetParameters().Select(pa => pa.ParameterType.Name + " " + pa.Name))})");
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, sb.ToString());
        }

        // ==================================================================
        // Probe
        // ==================================================================
        [MenuItem("FPSKit/Town/Probe Assets", false, 61)]
        public static void ProbeMenu() => Probe("Build/TownProbe.txt");

        /// <summary>
        /// Writes the size, pivot offset, triangle count and material count of every prefab the town might
        /// place -- the Poly Haven props, the pack clones and the Mediterranean kit -- so placement code is
        /// written against measured sizes rather than guessed ones.
        /// </summary>
        public static void Probe(string outPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("name | size (x y z) | min (x y z) | tris | mats");
            var folders = new List<string> { PropFolder, PackFolder };
            const string kit = "Assets/EmbersStorm – Mediterranean Ruins Building Kit/Prefabs";
            if (AssetDatabase.IsValidFolder(kit)) folders.Add(kit);

            foreach (var folder in folders)
            {
                sb.AppendLine($"## {folder}");
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }).OrderBy(g => AssetDatabase.GUIDToAssetPath(g)))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.transform.position = Vector3.zero;
                    go.transform.rotation = Quaternion.identity;
                    var rs = go.GetComponentsInChildren<Renderer>();
                    if (rs.Length == 0) { Object.DestroyImmediate(go); continue; }
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    int tris = 0;
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                    foreach (var sm in go.GetComponentsInChildren<SkinnedMeshRenderer>()) if (sm.sharedMesh != null) tris += sm.sharedMesh.triangles.Length / 3;
                    int mats = rs.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Count();
                    string pink = rs.SelectMany(r => r.sharedMaterials).Any(m => m != null && m.shader != null &&
                                   !m.shader.name.StartsWith("Universal") && !m.shader.name.StartsWith("Shader Graphs")) ? " NON-URP" : "";
                    sb.AppendLine($"{Path.GetFileNameWithoutExtension(path)} | {b.size.x:0.00} {b.size.y:0.00} {b.size.z:0.00} | " +
                                  $"{b.min.x:0.00} {b.min.y:0.00} {b.min.z:0.00} | {tris} | {mats}{pink}");
                    Object.DestroyImmediate(go);
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log($"[FPSKit] town probe written to {outPath}");
        }
    }
}
#endif
