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
                // Albedo at 2K for walls and ground (it is what reads as sharp); their normal maps at 1K and the masks
                // (occlusion, roughness) at 512 -- 428 textures at full size took 800 MB with the town open.
                Set(imp.maxTextureSize, !ground ? 1024 : normal ? 1024 : mask ? 512 : 2048, v => imp.maxTextureSize = v);
                Set(imp.mipmapEnabled, true, v => imp.mipmapEnabled = v);
                // Walls and ground are seen at grazing angles down a street: full anisotropic filtering, or brick courses
                // and stone joints blur into mush a few metres away. (The high-quality block format doubled the albedo's
                // memory for little that shows; with the town open the editor was killed for memory.)
                Set(imp.anisoLevel, ground ? 16 : 4, v => imp.anisoLevel = v);
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
        /// The sky as a cubemap for reflections, from the HDR (its latitude-longitude layout), with the sun taken out.
        /// Nothing in these scenes bakes a reflection probe, so without it glass, water and car paint reflect a grey default.
        ///
        /// A straight copy kept the sun disc, thousands of times brighter than the sky round it: even the blurred mips a
        /// rough surface reads were dominated by it, and every road and pavement facing the low sun shone as if wet --
        /// with the real-time sun's own highlight on top. So the copy is clamped: no pixel brighter than twice the
        /// brightest 0.5% of the sky, written as EXR at half the HDR's size.
        ///
        /// And the world below the sky is put in: a pure-sky HDR is brightest at its hazy horizon, which is just what a
        /// window at eye level reflects, so every pane read as a pale grey card. Below the horizon the copy is warm dark
        /// ground, and over the first 12 degrees above it a band the dull colour of the town blends into the sky: glass
        /// at street level reflects dark surroundings, and turns to sky only as you look up at it.
        /// </summary>
        public static Cubemap ReflectionCube()
        {
            foreach (var sky in SkyPaths())
            {
                string cube = sky.Replace(".hdr", "_reflections.exr");
                if (!File.Exists(cube)) WriteClampedSky(sky, cube, sky.Replace(".hdr", "_display.exr"));
                if (!File.Exists(cube)) return null;
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

        /// <summary>
        /// The sky the skybox draws: the HDR at full size with its sun clamped like the reflection cube's (and no ground or
        /// town band). The raw sun disc is 65 504 times white; every reflection probe that rendered the sky caught it, and
        /// anything inside the street probes' boxes reflected it and drew pure white (2026-10-21, after the probe bake).
        /// The disc stays the brightest thing in the sky, a few times white, and still blooms.
        /// </summary>
        public static Texture2D DisplaySky()
        {
            foreach (var sky in SkyPaths())
            {
                string display = sky.Replace(".hdr", "_display.exr");
                if (!File.Exists(display)) WriteClampedSky(sky, sky.Replace(".hdr", "_reflections.exr"), display);
                if (!File.Exists(display)) return null;
                if (AssetImporter.GetAtPath(display) is TextureImporter imp &&
                    (imp.textureShape != TextureImporterShape.Texture2D || imp.mipmapEnabled || imp.maxTextureSize != 4096 ||
                     imp.wrapModeV != TextureWrapMode.Clamp || imp.textureCompression != TextureImporterCompression.CompressedHQ))
                {
                    imp.textureShape = TextureImporterShape.Texture2D;
                    imp.mipmapEnabled = false;          // mipmaps draw a seam where the image wraps
                    imp.maxTextureSize = 4096;
                    imp.wrapModeU = TextureWrapMode.Repeat;
                    imp.wrapModeV = TextureWrapMode.Clamp;
                    imp.textureCompression = TextureImporterCompression.CompressedHQ;
                    imp.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(display);
            }
            return null;
        }

        static void WriteClampedSky(string sky, string cube, string display = null)
        {
            if (!(AssetImporter.GetAtPath(sky) is TextureImporter imp)) return;
            bool readable = imp.isReadable; var compression = imp.textureCompression; int max = imp.maxTextureSize;
            imp.isReadable = true; imp.textureCompression = TextureImporterCompression.Uncompressed; imp.maxTextureSize = 4096;
            imp.SaveAndReimport();
            try
            {
                var src = AssetDatabase.LoadAssetAtPath<Texture2D>(sky);
                if (src == null) return;
                var px = src.GetPixels();
                int w = src.width, h = src.height, ow = w / 2, oh = h / 2;
                var lum = new float[px.Length];
                for (int i = 0; i < px.Length; i++) lum[i] = Mathf.Max(px[i].r, Mathf.Max(px[i].g, px[i].b));
                var sorted = (float[])lum.Clone();
                System.Array.Sort(sorted);
                float cap = sorted[(int)(sorted.Length * 0.995f)] * 2f;
                var outPx = new Color[ow * oh];
                for (int y = 0; y < oh; y++)
                    for (int x = 0; x < ow; x++)
                    {
                        var c = Color.clear;
                        for (int dy = 0; dy < 2; dy++)
                            for (int dx = 0; dx < 2; dx++)
                            {
                                int k = (y * 2 + dy) * w + x * 2 + dx;
                                var p = px[k];
                                if (lum[k] > cap) p *= cap / lum[k];
                                c += p;
                            }
                        c *= 0.25f; c.a = 1f;
                        // Elevation of this row (EXR rows run bottom to top): the ground and the town round the horizon.
                        float elevation = ((y + 0.5f) / oh - 0.5f) * 180f;
                        var ground = new Color(0.16f, 0.13f, 0.10f, 1f);
                        var townBand = new Color(0.42f, 0.36f, 0.29f, 1f);
                        if (elevation < 0f) c = ground;
                        else if (elevation < 12f) c = Color.Lerp(Color.Lerp(ground, townBand, Mathf.Clamp01(elevation / 2f)), c, Mathf.SmoothStep(0f, 1f, elevation / 12f));
                        c.a = 1f;
                        outPx[y * ow + x] = c;
                    }
                var tex = new Texture2D(ow, oh, TextureFormat.RGBAFloat, false, true);
                tex.SetPixels(outPx);
                File.WriteAllBytes(cube, tex.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(cube);
                if (display != null)
                {
                    // The sky as drawn: full size, the sun clamped, nothing else changed (half-float EXR).
                    var shown = new Color[px.Length];
                    for (int k = 0; k < px.Length; k++) { var p = px[k]; if (lum[k] > cap) p *= cap / lum[k]; p.a = 1f; shown[k] = p; }
                    var full = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
                    full.SetPixels(shown);
                    File.WriteAllBytes(display, full.EncodeToEXR(Texture2D.EXRFlags.CompressZIP));
                    Object.DestroyImmediate(full);
                    AssetDatabase.ImportAsset(display);
                }
                Debug.Log($"[FPSKit] reflection sky written: {cube} (clamped at {cap:0.00}, brightest was {sorted[sorted.Length - 1]:0})");
            }
            finally
            {
                imp.isReadable = readable; imp.textureCompression = compression; imp.maxTextureSize = max;
                imp.SaveAndReimport();
            }
        }

        /// <summary>The town's sky: a clear Poly Haven sky with its sun 43 degrees up (2026-10-21, for a sun that
        /// leaves one side of a street in shadow and lights the other; the late-afternoon sky had it at 19).</summary>
        public const string TownSkyId = "kloofendal_43d_clear_puresky";

        /// <summary>The HDR skies on disk, the town's own first.</summary>
        public static IEnumerable<string> SkyPaths()
        {
            string dir = PolyHavenRoot + "/HDRI";
            if (!Directory.Exists(dir)) yield break;
            var files = Directory.GetFiles(dir, "*.hdr").Select(f => f.Replace('\\', '/')).ToList();
            foreach (var f in files.Where(f => Path.GetFileName(f).StartsWith(TownSkyId))) yield return f;
            foreach (var f in files.Where(f => !Path.GetFileName(f).StartsWith(TownSkyId))) yield return f;
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
                // Simpler levels of every scan, chosen by its size on screen (Unity's Mesh LOD). The scans are 160 of the
                // desert's 184.5 million triangles: an air conditioner of 9,500 on 2,838 walls, a cabinet of 29,700 in 2,840
                // rooms, a tea set of 8,400 (2026-10-30); each was drawn whole until its LOD group cut it off.
                if (!imp.generateMeshLods) { imp.generateMeshLods = true; dirty = true; }
                if (dirty) imp.SaveAndReimport();
            }
        }

        /// <summary>Batch: the Poly Haven models' import settings as the code has them (FPSKitBatch.ConfigureTownModels).</summary>
        public static int ConfigurePolyHavenModels()
        {
            ConfigureModels();
            return PolyHavenModels().Count(f => AssetImporter.GetAtPath(f) is ModelImporter m && m.generateMeshLods);
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
                // The materials as the code has them, made prefab or not (a smoothness change reached no prop before,
                // 2026-10-31): only a changed mapping reimports the model.
                if (AssetDatabase.LoadAssetAtPath<GameObject>(fbx) != null) RemapMaterials(fbx, id);
                if (!rebuild && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) { made++; continue; }

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                if (model == null) { Debug.LogWarning($"[FPSKit] Poly Haven model did not import: {fbx}"); continue; }

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
                    {
                        // From the coarsest level of detail only (every level used to get its own), and convex for a dense
                        // scan: a boulder is 60-110k triangles, and cooking those as triangle meshes took the better part
                        // of a gigabyte when the town loaded (17 M collider triangles in all).
                        var lodGroup = go.GetComponentInChildren<LODGroup>();
                        var filters = new List<MeshFilter>();
                        if (lodGroup != null)
                        {
                            var lods = lodGroup.GetLODs();
                            foreach (var r in lods[lods.Length - 1].renderers) if (r != null && r.GetComponent<MeshFilter>() != null) filters.Add(r.GetComponent<MeshFilter>());
                        }
                        if (filters.Count == 0) filters.AddRange(go.GetComponentsInChildren<MeshFilter>());
                        int colliderTris = 0;
                        foreach (var mf in filters) if (mf.sharedMesh != null) colliderTris += mf.sharedMesh.triangles.Length / 3;
                        foreach (var mf in filters)
                        {
                            if (mf.sharedMesh == null) continue;
                            var mc = mf.gameObject.AddComponent<MeshCollider>();
                            mc.sharedMesh = mf.sharedMesh;
                            mc.convex = colliderTris > 3000;
                        }
                    }
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
                var mat = LitFromMaps($"{MatFolder}/PH_{key}.mat", s.diff, s.nor, s.mask, PropTint(id), Vector2.one,
                                      IsPlant(id) || HasAlpha(s.diff), smoothness: PropSmoothness(id));
                var source = new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name);
                if (map.TryGetValue(source, out var existing) && existing == mat) continue;
                imp.AddRemap(source, mat);
                changed = true;
            }
            // Once remapped, a model lists no embedded materials: its own materials are refreshed through the remap table
            // (a change of smoothness reached none of them, 2026-10-31).
            if (embedded.Count == 0)
                foreach (var kv in map)
                {
                    if (!(kv.Value is Material mapped) || kv.Key.type != typeof(Material)) continue;
                    string key = sets.Keys.FirstOrDefault(k => string.Equals(k, kv.Key.name, System.StringComparison.OrdinalIgnoreCase))
                              ?? sets.Keys.FirstOrDefault(k => kv.Key.name.ToLowerInvariant().Contains(k.ToLowerInvariant()))
                              ?? sets.Keys.FirstOrDefault(k => k.ToLowerInvariant().Contains(kv.Key.name.ToLowerInvariant()))
                              ?? sets.Keys.OrderBy(k => k.Length).First();
                    var s = sets[key];
                    LitFromMaps(AssetDatabase.GetAssetPath(mapped), s.diff, s.nor, s.mask, PropTint(id), Vector2.one,
                                IsPlant(id) || HasAlpha(s.diff), smoothness: PropSmoothness(id));
                }
            if (changed) imp.SaveAndReimport();
        }

        /// <summary>
        /// How much of a scan's own smoothness its material keeps (the mask's smoothness is 1 - Poly Haven's roughness,
        /// scaled by this). Measured as rendered, the room props' were well over the real surfaces' (FPSKitTownLightmaps.
        /// BatchGlossAudit, 2026-10-31: cardboard 0.42, wicker 0.44-0.61, a leather suitcase 0.58, varnished cabinets 0.72-0.74),
        /// and under a room's reflection they read as plastic. Each scaled to the middle of its real range: cardboard
        /// 0.1-0.2, wicker 0.15-0.3, worn leather 0.3-0.4, varnished or painted wood 0.3-0.5, painted steel 0.35-0.45, glazed
        /// ceramic 0.7-0.85, unglazed 0.2-0.35, enamel 0.6-0.7, aged brass 0.5-0.65 (metal, from its map since 2026-10-31).
        /// </summary>
        static float PropSmoothness(string id) => id switch
        {
            "cardboard_box_01" => 0.36f,
            "wicker_basket_01" => 0.5f,
            "wicker_basket_02" => 0.36f,
            "vintage_suitcase" => 0.6f,
            "vintage_cabinet_01" => 0.62f,
            "painted_wooden_chair_01" => 0.62f,
            "wooden_stool_01" => 0.64f,
            "wooden_display_shelves_01" => 0.68f,
            "steel_frame_shelves_01" or "steel_frame_shelves_02" => 0.7f,
            "ceramic_pot" => 0.65f,
            "tea_set_01" or "jug_01" or "antique_ceramic_vase_01" => 0.88f,
            "pot_enamel_01" => 0.9f,
            "brass_pot_01" or "brass_pot_02" or "brass_vase_01" or "brass_vase_02" or "brass_vase_03" or "brass_goblets" => 0.78f,
            "brass_diya_lantern" => 0.72f,
            _ => 1f,
        };

        /// <summary>A scan darker than its real surface. The vintage cabinet's map averages 3% reflectance, close to soot; dark
        /// stained wood is around 8%, and in a lit room the cupboard read as a black hole (his play-test, 2026-11-03).</summary>
        static Color PropTint(string id) => id == "vintage_cabinet_01" ? new Color(3f, 3f, 3f, 1f) : Color.white;

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
        /// <param name="normalStrength">The normal map's strength (_BumpScale).</param>
        /// <param name="flatSmoothness">0 or more: the surface's smoothness everywhere, the mask kept for its occlusion only.
        /// URP only scales a mask's smoothness, and a scan of rough wood (0.05) cannot be made a painted door's 0.35.</param>
        public static Material LitFromMaps(string path, Texture2D diff, Texture2D nor, Texture2D mask, Color tint,
                                           Vector2 tiling, bool alphaClip, float smoothness = 1f, bool twoSided = false,
                                           float normalStrength = 1f, float flatSmoothness = -1f)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = mat == null;
            if (create) mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));

            mat.SetTexture("_BaseMap", diff);
            mat.SetTextureScale("_BaseMap", tiling);
            mat.SetColor("_BaseColor", tint);
            if (nor != null) { mat.SetTexture("_BumpMap", nor); mat.EnableKeyword("_NORMALMAP"); }
            else mat.DisableKeyword("_NORMALMAP");
            mat.SetFloat("_BumpScale", normalStrength);
            if (mask != null && flatSmoothness >= 0f)
            {
                mat.SetTexture("_MetallicGlossMap", null);
                mat.DisableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetTexture("_OcclusionMap", mask);       // the mask's green is its occlusion
                mat.EnableKeyword("_OCCLUSIONMAP");
                mat.SetFloat("_OcclusionStrength", 1f);
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Smoothness", flatSmoothness);
            }
            else if (mask != null)
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
            UnlightLamps("Car");
            SwapDiscWheels();
            CarWindowLook();
            FPSKitWorldModels.Make();
            MakeLooseProps();
            return made;
        }

        /// <summary>Car windows as glass, not black film: the pack's are 60 % black with a tinted reflection, read as a dark metal plate. A light tint, dielectric.</summary>
        public static void CarWindowLook()
        {
            foreach (var name in new[] { "RMCar26_Windows", "RMCar26_Glass", "M_Windows", "M_Glass", "glass1" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/Car/{name}.mat");
                if (m == null) continue;
                m.SetTexture("_MetallicGlossMap", null);
                m.DisableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", 0.94f);
                m.SetColor("_BaseColor", new Color(0.08f, 0.1f, 0.1f, 0.34f));
                EditorUtility.SetDirty(m);
            }
        }

        /// <summary>Kilograms of what a kick can move, by prop; what is not here stays where it stands. See LooseProp.</summary>
        static readonly Dictionary<string, (float kg, LooseProp.Stuff stuff)> Loose = new Dictionary<string, (float, LooseProp.Stuff)>
        {
            ["Barrel_01"] = (18f, LooseProp.Stuff.Metal), ["barrel_03"] = (18f, LooseProp.Stuff.Metal), ["barrel_stove"] = (25f, LooseProp.Stuff.Metal),
            ["plastic_jerrycan"] = (6f, LooseProp.Stuff.Plastic), ["small_lpg_tank"] = (15f, LooseProp.Stuff.Metal), ["propane_tank"] = (25f, LooseProp.Stuff.Metal),
            ["cardboard_box_01"] = (4f, LooseProp.Stuff.Wood), ["old_tyre"] = (12f, LooseProp.Stuff.Plastic), ["rusted_wheel_rim_01"] = (10f, LooseProp.Stuff.Metal),
            ["plastic_crate_01"] = (3f, LooseProp.Stuff.Plastic), ["plastic_crate_02"] = (3f, LooseProp.Stuff.Plastic), ["plastic_crate_03"] = (3f, LooseProp.Stuff.Plastic),
            ["wooden_crate_01"] = (20f, LooseProp.Stuff.Wood), ["wooden_crate_02"] = (20f, LooseProp.Stuff.Wood), ["wooden_military_crate"] = (25f, LooseProp.Stuff.Wood),
            ["old_military_crate"] = (25f, LooseProp.Stuff.Wood), ["ammo_box"] = (8f, LooseProp.Stuff.Metal), ["trashbag"] = (3f, LooseProp.Stuff.Plastic),
            ["plastic_bottle_gallon"] = (2f, LooseProp.Stuff.Plastic), ["cement_bag"] = (25f, LooseProp.Stuff.Plastic), ["WoodenChair_01"] = (5f, LooseProp.Stuff.Wood),
            ["painted_wooden_chair_01"] = (5f, LooseProp.Stuff.Wood), ["plastic_monobloc_chair_01"] = (3f, LooseProp.Stuff.Plastic), ["folding_wooden_stool"] = (3f, LooseProp.Stuff.Wood),
            ["wooden_stool_01"] = (3f, LooseProp.Stuff.Wood), ["wooden_stool_02"] = (3f, LooseProp.Stuff.Wood), ["wicker_basket_01"] = (2f, LooseProp.Stuff.Wood),
            ["wicker_basket_02"] = (2f, LooseProp.Stuff.Wood), ["hand_truck"] = (12f, LooseProp.Stuff.Metal), ["jug_01"] = (1f, LooseProp.Stuff.Plastic),
            ["ceramic_pot"] = (3f, LooseProp.Stuff.Plastic), ["pot_enamel_01"] = (1f, LooseProp.Stuff.Metal), ["portable_generator"] = (45f, LooseProp.Stuff.Metal),
            ["portable_searchlight"] = (15f, LooseProp.Stuff.Metal), ["vintage_suitcase"] = (6f, LooseProp.Stuff.Wood), ["WoodenTable_01"] = (20f, LooseProp.Stuff.Wood),
            ["painted_wooden_table"] = (20f, LooseProp.Stuff.Wood), ["wooden_picnic_table"] = (45f, LooseProp.Stuff.Wood), ["wooden_barrels_01"] = (40f, LooseProp.Stuff.Wood),
            ["painted_wooden_bench"] = (18f, LooseProp.Stuff.Wood), ["plastic_jerrycan_steel"] = (5f, LooseProp.Stuff.Metal),
        };

        /// <summary>
        /// Everything under about 60 kg can be moved: a rigidbody of its weight, kinematic until struck (LooseProp), colliders a body can have
        /// (convex), never static batching (a batched mesh does not move). Idempotent; prefabs are kept between builds.
        /// </summary>
        public static int MakeLooseProps()
        {
            int done = 0;
            foreach (var kv in Loose)
            {
                string path = $"{PropFolder}/{kv.Key}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                if (!root.TryGetComponent<Rigidbody>(out var rb)) rb = root.AddComponent<Rigidbody>();
                rb.mass = kv.Value.kg;
                rb.linearDamping = 0.05f;
                rb.angularDamping = 0.4f;
                rb.isKinematic = true;
                if (!root.TryGetComponent<LooseProp>(out var loose)) loose = root.AddComponent<LooseProp>();
                loose.stuff = kv.Value.stuff;
                foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true)) mc.convex = true;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                done++;
            }
            return done;
        }

        /// <summary>
        /// The pack's Wheel_C has a closed face, half of it mapped to a white patch of the car atlas: in the sun, a blank white disc on the
        /// rear axle of Car_Sedan_C. Those wheels get Wheel_B, level for level. Only the meshes change (same diameter, same transforms), so
        /// the prefab's objects, its wheels' LOD groups and what saved scenes override on them all stay valid. Run on every build, because
        /// pack prefabs are kept between builds. Traffic and drivable cars nest this prefab; a merged parked car takes only the root LOD
        /// group, which has no wheel meshes.
        /// </summary>
        public static int SwapDiscWheels()
        {
            const string replacement = "Assets/RealisticMobileCars - Pro3DModels/RMCar26/Meshes/Wheel_B.FBX";
            var meshes = AssetDatabase.LoadAllAssetsAtPath(replacement).OfType<Mesh>().ToArray();
            int swapped = 0;
            foreach (var kind in new[] { "Car_Sedan_A", "Car_Sedan_B", "Car_Sedan_C", "Car_Sedan_D" })
            {
                string path = $"{PackFolder}/{kind}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                int here = 0;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || !mf.sharedMesh.name.Contains("Wheel_C")) continue;
                    string wanted = mf.sharedMesh.name.Replace("Wheel_C", "Wheel_B");
                    var mesh = System.Array.Find(meshes, m => m.name == wanted);
                    if (mesh == null) { Debug.LogWarning($"[FPSKit] town: no {wanted} in {replacement}, {kind} keeps its disc wheel"); continue; }
                    mf.sharedMesh = mesh;
                    here++;
                }
                if (here > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                swapped += here;
            }
            return swapped;
        }

        /// <summary>The town is in daylight: a vehicle's lamps (the car pack's emission) are not lit, parked or driving.</summary>
        static bool DaylightLamps(string group) => group == "Car";

        /// <summary>
        /// The lamps of the vehicles already converted (their prefabs are kept between builds, so their materials are not
        /// made again): emission off, every build. The realism audit found parked cars' bodies glowing in shade and indoors.
        /// </summary>
        static void UnlightLamps(string group)
        {
            string folder = $"{MatFolder}/{group}";
            if (!AssetDatabase.IsValidFolder(folder)) return;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || !m.IsKeywordEnabled("_EMISSION")) continue;
                m.DisableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                EditorUtility.SetDirty(m);
            }
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

            if (src.IsKeywordEnabled("_EMISSION") && !DaylightLamps(group))
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
