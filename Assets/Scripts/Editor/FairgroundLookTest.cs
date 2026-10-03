#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Step 2 proof scene: one small patch with terrain blend, floodwater (soft shoreline, depth darkening),
    /// converted trees, grass, and a few converted pack props. A neutral TEST light rig, not the night look.
    /// Output: Assets/Fairground/Scenes/LookTest.unity
    /// </summary>
    public static class FairgroundLookTest
    {
        const string ThirdParty = "Assets/Fairground/ThirdParty";
        const string ScenePath = "Assets/Fairground/Scenes/LookTest.unity";
        const float Size = 200f, Height = 14f, WaterY = 2.0f;   // 200 m like the real arena
        const int HeightRes = 257;
        const float SkyExposure = 0.12f;      // the night HDRI has light-polluted horizon values well above 1

        // ---------------------------------------------------------------- texture import settings

        [MenuItem("Tools/MiniFPS/Fairground/Set Up ThirdParty Textures")]
        public static void SetUpTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { ThirdParty }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                bool changed = false;
                // Memory: colour and normal maps are capped at 2048; everything that does not carry detail at 1024.
                bool detailMap = n.Contains("_diff") || n.Contains("_color") || n.Contains("nor_gl") || n.Contains("normalgl");
                int cap = path.EndsWith(".hdr") ? 1024 : (detailMap ? 2048 : 1024);
                if (ti.maxTextureSize != cap) { ti.maxTextureSize = cap; changed = true; }
                if (n.Contains("nor_gl") || n.EndsWith("_normalgl"))
                {
                    if (ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
                }
                else if (n.Contains("_rough") || n.EndsWith("_ao") || n.Contains("_ao_") || n.Contains("_disp")
                         || n.Contains("opacity") || n.Contains("metalness") || n.Contains("ambientocclusion"))
                {
                    if (ti.sRGBTexture) { ti.sRGBTexture = false; changed = true; }
                }
                else if (path.EndsWith(".hdr"))
                {
                    if (ti.textureShape != TextureImporterShape.TextureCube)
                    {
                        ti.textureShape = TextureImporterShape.TextureCube;
                        ti.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                        changed = true;
                    }
                }
                if (changed) ti.SaveAndReimport();
            }
        }

        /// <summary>Generated signs and decals never need more than 1024 (the arch banner) / 512.</summary>
        public static void CapGeneratedTextures()
        {
            foreach (var folder in new[] { "Assets/Fairground/Signs", "Assets/Fairground/Decals" })
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                    if (ti == null) continue;
                    int cap = folder.EndsWith("Decals") ? 512 : 1024;
                    if (ti.maxTextureSize != cap) { ti.maxTextureSize = cap; ti.SaveAndReimport(); }
                }
        }

        /// <summary>Our scenes get an explicit lighting-settings asset with Auto Generate OFF: baking only when asked.</summary>
        public static void DisableAutoBake(string name)
        {
            FairgroundMaterialConverter.EnsureFolder("Assets/Fairground/Settings");
            string path = $"Assets/Fairground/Settings/{name}.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (ls == null) { ls = new LightingSettings { name = name }; AssetDatabase.CreateAsset(ls, path); }
            ls.autoGenerate = false;
            EditorUtility.SetDirty(ls);
            Lightmapping.lightingSettings = ls;
        }

        // ---------------------------------------------------------------- scene

        [MenuItem("Tools/MiniFPS/Fairground/Build Look Test Scene")]
        public static void Build()
        {
            SetUpTextures();
            FairgroundMaterialConverter.ConvertAll();
            FairgroundMaterialConverter.EnsureFolder("Assets/Fairground/Scenes");
            FairgroundMaterialConverter.EnsureFolder("Assets/Fairground/Terrain");
            FairgroundMaterialConverter.EnsureFolder("Assets/Fairground/Meshes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- light rig (test only) ----
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.55f, 0.66f, 0.95f);          // moon
            sun.intensity = 1.0f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(38f, 200f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.16f, 0.20f, 0.29f);
            RenderSettings.ambientEquatorColor = new Color(0.11f, 0.14f, 0.20f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.05f, 0.06f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.040f, 0.052f, 0.064f);
            RenderSettings.fogStartDistance = 20f;
            RenderSettings.fogEndDistance = 220f;
            RenderSettings.skybox = MakeSkyMaterial();
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.45f;

            var terrain = BuildTerrain();
            BuildWater();
            PlaceProps(terrain);
            AddPracticalLights(terrain);
            var profile = FairgroundVolume.Night();
            var volGo = new GameObject("GlobalVolume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            var camGo = new GameObject("TestCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.farClipPlane = 600f;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camGo.transform.SetPositionAndRotation(new Vector3(52f, 6.2f, 30f), Quaternion.Euler(12f, 40f, 0f));

            DisableAutoBake("LookTest");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[FPSKit] Fairground look test built: " + ScenePath);
        }

        static Material MakeSkyMaterial()
        {
            string matPath = "Assets/Fairground/Materials/Sky_QwantaniNight.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (m != null) { m.SetFloat("_Exposure", SkyExposure); return m; }
            var hdr = AssetDatabase.LoadAssetAtPath<Cubemap>(ThirdParty + "/qwantani_night_puresky/qwantani_night_puresky_2k.hdr");
            m = new Material(Shader.Find("Skybox/Cubemap"));
            m.SetTexture("_Tex", hdr);
            m.SetFloat("_Exposure", SkyExposure);
            AssetDatabase.CreateAsset(m, matPath);
            return m;
        }

        // ---------------------------------------------------------------- terrain

        static TerrainLayer MakeLayer(string name, Texture2D diffuse, Texture2D normal, float tile)
        {
            string path = $"Assets/Fairground/Terrain/{name}.terrainlayer";
            var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (l != null) return l;
            l = new TerrainLayer { diffuseTexture = diffuse, normalMapTexture = normal, tileSize = new Vector2(tile, tile), normalScale = 1f };
            AssetDatabase.CreateAsset(l, path);
            return l;
        }

        static Texture2D FindTex(string root, string name)
        {
            foreach (var g in AssetDatabase.FindAssets(name + " t:Texture2D", new[] { root }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            return null;
        }

        static Terrain BuildTerrain()
        {
            var td = new TerrainData { heightmapResolution = HeightRes, size = new Vector3(Size, Height, Size) };

            // Gentle rolling ground with a bowl on the right that the water fills.
            var h = new float[HeightRes, HeightRes];
            for (int z = 0; z < HeightRes; z++)
                for (int x = 0; x < HeightRes; x++)
                {
                    float nx = x / (float)(HeightRes - 1), nz = z / (float)(HeightRes - 1);
                    float hills = Mathf.PerlinNoise(nx * 3.1f + 7.3f, nz * 3.1f + 2.1f) * 0.06f
                                + Mathf.PerlinNoise(nx * 9f, nz * 9f) * 0.012f;
                    float d = Vector2.Distance(new Vector2(nx, nz), new Vector2(0.66f, 0.5f));
                    float bowl = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.06f, 0.17f, d));    // 1 in the middle
                    float baseH = 0.30f + hills;
                    h[z, x] = Mathf.Lerp(baseH, 0.11f + hills * 0.4f, bowl);
                }
            td.SetHeights(0, 0, h);

            var moss = MakeLayer("FG_Moss", FindTex("Assets/Fairground/ThirdParty/FloodedGrounds/Content/Textures", "GR_Moss1_AS"), FindTex("Assets/Fairground/ThirdParty/FloodedGrounds/Content/Textures", "GR_Moss1_N"), 10f);
            var dirt = MakeLayer("FG_Dirt", FindTex("Assets/Fairground/ThirdParty/FloodedGrounds/Content/Textures", "GR_Dirt1_AS"), FindTex("Assets/Fairground/ThirdParty/FloodedGrounds/Content/Textures", "GR_Dirt1_N"), 10f);
            var asph = MakeLayer("FG_Asphalt", FindTex("Assets/Fairground/ThirdParty/FloodedGrounds/Content/Textures", "GR_Asphalt1_AS"), FindTex("Assets/Fairground/ThirdParty/FloodedGrounds/Content/Textures", "GR_Asphalt1_N"), 20f);
            var mud = MakeLayer("FG_WetMud", FindTex(ThirdParty, "brown_mud_leaves_01_diff_2k"), FindTex(ThirdParty, "brown_mud_leaves_01_nor_gl_2k"), 6f);
            td.terrainLayers = new[] { moss, dirt, asph, mud };

            // Splat map: mud near and under water, asphalt strip, dirt on slopes and patches, moss elsewhere.
            int ar = td.alphamapResolution;
            var a = new float[ar, ar, 4];
            for (int z = 0; z < ar; z++)
                for (int x = 0; x < ar; x++)
                {
                    float nx = x / (float)(ar - 1), nz = z / (float)(ar - 1);
                    float wy = td.GetInterpolatedHeight(nx, nz);
                    float slope = td.GetSteepness(nx, nz);
                    float noise = Mathf.PerlinNoise(nx * 14f, nz * 14f);
                    float wMud = Mathf.InverseLerp(WaterY + 0.9f, WaterY - 0.1f, wy);
                    float wAsph = Mathf.InverseLerp(0.045f, 0.02f, Mathf.Abs(nz - (0.22f + 0.05f * Mathf.Sin(nx * 6f)))) * (nx < 0.55f ? 1f : 0f);
                    float wDirt = Mathf.Clamp01(slope / 18f) * 0.8f + Mathf.InverseLerp(0.62f, 0.78f, noise) * 0.7f;
                    float wMoss = 1f;
                    wMud = Mathf.Clamp01(wMud); wAsph = Mathf.Clamp01(wAsph);
                    wDirt *= 1f - wMud; wMoss *= (1f - wMud) * (1f - Mathf.Clamp01(wDirt));
                    wDirt *= 1f - wAsph; wMoss *= 1f - wAsph;
                    float sum = wMoss + wDirt + wAsph + wMud + 1e-4f;
                    a[z, x, 0] = wMoss / sum; a[z, x, 1] = wDirt / sum; a[z, x, 2] = wAsph / sum; a[z, x, 3] = wMud / sum;
                }
            td.SetAlphamaps(0, 0, a);

            // Trees (converted Tree Creator meshes).
            string[] treeNames = { "TreeCreator_Small_B", "TreeCreator_Tall_C", "TreeCreator_Bush_A", "TreeCreator_Tall_C_Dead" };
            var protos = new List<TreePrototype>();
            foreach (var n in treeNames)
                protos.Add(new TreePrototype { prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Fairground/Prefabs/Trees/{n}.prefab") });
            td.treePrototypes = protos.ToArray();
            var rnd = new System.Random(7);
            var trees = new List<TreeInstance>();
            for (int i = 0; i < 200 && trees.Count < 22; i++)
            {
                float nx = (float)rnd.NextDouble(), nz = (float)rnd.NextDouble();
                float wy = td.GetInterpolatedHeight(nx, nz);
                if (wy < WaterY + 1.6f) continue;
                if (Mathf.Abs(nz - 0.22f) < 0.08f && nx < 0.6f) continue;       // keep the road clear
                int proto = rnd.NextDouble() < 0.4 ? 2 : (rnd.NextDouble() < 0.7 ? 0 : (rnd.NextDouble() < 0.7 ? 1 : 3));
                trees.Add(new TreeInstance
                {
                    prototypeIndex = proto,
                    position = new Vector3(nx, wy / Height, nz),
                    widthScale = 0.8f + (float)rnd.NextDouble() * 0.5f,
                    heightScale = 0.8f + (float)rnd.NextDouble() * 0.5f,
                    rotation = (float)rnd.NextDouble() * Mathf.PI * 2f,
                    color = Color.white, lightmapColor = Color.white
                });
            }
            td.treeInstances = trees.ToArray();

            // Grass: three mesh prototypes, dense low cover with tall clumps.
            string[] grass = { "Grass_Small_C", "Grass_Tall_C", "Grass_Med_A" };
            var dp = new List<DetailPrototype>();
            foreach (var n in grass)
                dp.Add(new DetailPrototype
                {
                    prototype = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Fairground/Prefabs/Grass/{n}.prefab"),
                    usePrototypeMesh = true, useInstancing = true,
                    renderMode = DetailRenderMode.VertexLit,
                    minWidth = 0.6f, maxWidth = 1.3f, minHeight = 0.6f, maxHeight = 1.3f, noiseSpread = 0.5f,
                    healthyColor = Color.white, dryColor = new Color(0.55f, 0.55f, 0.5f)
                });
            td.SetDetailResolution(256, 16);
            td.detailPrototypes = dp.ToArray();
            int dr = td.detailResolution;
            var low = new int[dr, dr]; var tall = new int[dr, dr]; var med = new int[dr, dr];
            for (int z = 0; z < dr; z++)
                for (int x = 0; x < dr; x++)
                {
                    float nx = x / (float)(dr - 1), nz = z / (float)(dr - 1);
                    float wy = td.GetInterpolatedHeight(nx, nz);
                    if (wy < WaterY + 0.4f) continue;
                    var w = td.GetAlphamaps((int)(nx * (ar - 1)), (int)(nz * (ar - 1)), 1, 1);
                    float onGround = w[0, 0, 0] + w[0, 0, 1] * 0.4f;         // moss + a little dirt
                    float n1 = Mathf.PerlinNoise(nx * 40f, nz * 40f);
                    low[z, x] = onGround > 0.3f ? Mathf.RoundToInt(Mathf.Lerp(1f, 3f, n1) * onGround) : 0;
                    tall[z, x] = (onGround > 0.5f && Mathf.PerlinNoise(nx * 11f + 3f, nz * 11f) > 0.8f) ? 2 : 0;
                    med[z, x] = (onGround > 0.4f && n1 > 0.78f) ? 2 : 0;
                }
            td.SetDetailLayer(0, 0, 0, low);
            td.SetDetailLayer(0, 0, 1, tall);
            td.SetDetailLayer(0, 0, 2, med);

            AssetDatabase.DeleteAsset("Assets/Fairground/Terrain/LookTestTerrain.asset");      // rebuilt from code each time
            AssetDatabase.CreateAsset(td, "Assets/Fairground/Terrain/LookTestTerrain.asset");
            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain";
            var t = go.GetComponent<Terrain>();
            t.detailObjectDistance = 80f;
            t.detailObjectDensity = 1f;
            t.treeDistance = 250f;
            t.treeBillboardDistance = 2000f;      // the arena is 180 m: no billboards (URP cannot draw the pack's)
            t.treeCrossFadeLength = 0f;
            t.treeMaximumFullLODCount = 400;
            t.drawInstanced = true;
            return t;
        }

        // ---------------------------------------------------------------- water

        static Mesh WaterGrid()
        {
            string path = "Assets/Fairground/Meshes/WaterGrid96.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            const int n = 96;
            var v = new Vector3[(n + 1) * (n + 1)];
            var uv = new Vector2[v.Length];
            var nrm = new Vector3[v.Length];
            var tan = new Vector4[v.Length];
            for (int z = 0; z <= n; z++)
                for (int x = 0; x <= n; x++)
                {
                    int i = z * (n + 1) + x;
                    v[i] = new Vector3(x / (float)n - 0.5f, 0, z / (float)n - 0.5f);
                    uv[i] = new Vector2(x / (float)n, z / (float)n);
                    nrm[i] = Vector3.up; tan[i] = new Vector4(1, 0, 0, 1);
                }
            var tri = new int[n * n * 6];
            int k = 0;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int i0 = z * (n + 1) + x, i1 = i0 + n + 1, i2 = i0 + 1, i3 = i1 + 1;
                    tri[k++] = i0; tri[k++] = i1; tri[k++] = i2;
                    tri[k++] = i2; tri[k++] = i1; tri[k++] = i3;
                }
            var m = new Mesh { name = "WaterGrid96", vertices = v, uv = uv, normals = nrm, tangents = tan, triangles = tri };
            m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void BuildWater()
        {
            string matPath = "Assets/Fairground/Materials/FG_Water_Flood.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Fairground/FG_Water_URP"));
                var normal = FindTex("Assets/Fairground/ThirdParty/FloodedGrounds/Content/Textures", "BGR_Ocean_N");
                mat.SetTexture("_BumpMap", normal);
                mat.SetTexture("_BumpMap2", normal);
                // Tuned in the night test: dark and murky, soft reflections (bright silver at smoothness 0.95).
                mat.SetFloat("_Smoothness", 0.78f); mat.SetFloat("_BumpScale", 0.55f);
                mat.SetColor("_ShallowColor", new Color(0.045f, 0.062f, 0.045f, 1)); mat.SetColor("_DeepColor", new Color(0.005f, 0.011f, 0.011f, 1));
                mat.SetFloat("_ShallowAlpha", 0.7f); mat.SetFloat("_DeepAlpha", 0.97f);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            var go = new GameObject("FloodWater");
            go.AddComponent<MeshFilter>().sharedMesh = WaterGrid();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            go.transform.position = new Vector3(0.66f * Size, WaterY, 0.5f * Size);
            go.transform.localScale = new Vector3(70f, 1f, 70f);
        }

        // ---------------------------------------------------------------- props

        static GameObject FindPackPrefab(string name)
        {
            foreach (var g in AssetDatabase.FindAssets(name + " t:Prefab", new[] { "Assets/Fairground/ThirdParty/FloodedGrounds/Prefabs" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            return null;
        }

        static void Place(Terrain t, string prefab, float nx, float nz, float yaw, float sink = 0f)
        {
            var src = FindPackPrefab(prefab);
            if (src == null) { Debug.LogWarning("[FPSKit] pack prefab not found: " + prefab); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            FairgroundMaterialConverter.Remap(go);
            float y = t.terrainData.GetInterpolatedHeight(nx, nz) - sink;
            go.transform.SetPositionAndRotation(new Vector3(nx * Size, y, nz * Size), Quaternion.Euler(0, yaw, 0));
        }

        static void AddPracticalLights(Terrain t)
        {
            // Two practical sources, so the water has something to reflect: a warm barrel fire and a cold work light.
            var fire = new GameObject("BarrelFire");
            fire.transform.position = new Vector3(0.40f * Size, t.terrainData.GetInterpolatedHeight(0.40f, 0.42f) + 1.2f, 0.42f * Size);
            var fl = fire.AddComponent<Light>();
            fl.type = LightType.Point; fl.color = new Color(1f, 0.55f, 0.18f); fl.intensity = 6f; fl.range = 14f; fl.shadows = LightShadows.Soft;
            var work = new GameObject("WorkLight");
            work.transform.position = new Vector3(0.52f * Size, t.terrainData.GetInterpolatedHeight(0.52f, 0.72f) + 5f, 0.72f * Size);
            work.transform.rotation = Quaternion.Euler(55f, 215f, 0f);
            var wl = work.AddComponent<Light>();
            wl.type = LightType.Spot; wl.color = new Color(0.85f, 0.92f, 1f); wl.intensity = 40f; wl.range = 40f; wl.spotAngle = 80f; wl.shadows = LightShadows.Soft;
        }

        static void PlaceProps(Terrain t)
        {
            Place(t, "Prop_Car_A", 0.30f, 0.25f, 20f);
            Place(t, "Prop_ParkBench_A", 0.38f, 0.45f, 200f);
            Place(t, "CobbleRock_A", 0.50f, 0.52f, 10f, 0.1f);
            Place(t, "CobbleRock_C", 0.58f, 0.60f, 80f, 0.15f);
            Place(t, "CobbleRock_E", 0.55f, 0.40f, 150f, 0.2f);
            Place(t, "Struct_Fence1_Mid_B", 0.42f, 0.60f, 90f);
            Place(t, "Struct_Fence1_Mid_B", 0.45f, 0.60f, 90f);
            Place(t, "Struct_Docking_A", 0.52f, 0.47f, 90f);
        }
    }

    /// <summary>Renders a camera through the real URP pipeline into a PNG (works in edit mode).</summary>
    public static class FairgroundShots
    {
        public static string Capture(Camera cam, string path, int w = 1280, int h = 720)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            rt.Create();
            DynamicGI.UpdateEnvironment();
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (!RenderPipeline.SupportsRenderRequest(cam, req)) { Object.DestroyImmediate(rt); return "render request not supported"; }
            RenderPipeline.SubmitRenderRequest(cam, req);
            RenderPipeline.SubmitRenderRequest(cam, req);     // second pass lets depth/shadow/reflection settle
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            return path;
        }
    }
}
#endif
