#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The town's own models from Blender (Tools/blender/world_props.py, make_civilians.sh) into prefabs: import settings, materials assigned by
    /// slot name, colliders, LOD cull. Drums go into the existing Barrel prefabs, so what is already placed takes the new model. Civilians: one
    /// prefab per body under Assets/Characters/Civilians, which FPSKitTownLife wraps as people.
    /// </summary>
    public static class FPSKitWorldModels
    {
        public const string Folder = "Assets/Models/World", PrefabFolder = "Assets/FPSKit_Generated/Town/World", Civilians = "Assets/Characters/Civilians";
        const string Mats = FPSKitTownAssets.MatFolder + "/World";

        static Material Mat(string name, Color c, float smooth, float metal = 0f, Texture2D map = null, Texture2D ms = null)
        {
            if (!AssetDatabase.IsValidFolder(Mats)) AssetDatabase.CreateFolder(FPSKitTownAssets.MatFolder, "World");
            string path = $"{Mats}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = m == null;
            if (create) m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.SetTexture("_BaseMap", map);
            m.SetTexture("_MetallicGlossMap", ms);
            if (ms != null) m.EnableKeyword("_METALLICSPECGLOSSMAP"); else m.DisableKeyword("_METALLICSPECGLOSSMAP");
            m.enableInstancing = true;
            if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D Tex(string file, bool linear = false, int size = 1024)
        {
            string path = $"{Folder}/Textures/{file}";
            if (AssetImporter.GetAtPath(path) is TextureImporter t && (t.maxTextureSize != size || t.sRGBTexture == linear))
            {
                t.maxTextureSize = size; t.sRGBTexture = !linear; t.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Town(string name, Material fallback)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{FPSKitTownAssets.MatFolder}/Town_{name}.mat");
            return m != null ? m : fallback;
        }

        static Dictionary<string, Material> _slots;

        static Dictionary<string, Material> Slots()
        {
            if (_slots != null) return _slots;
            var drumMap = Tex("Drum_Albedo.png"); var drumMs = Tex("Drum_MS.png", true);
            var plain = Mat("Plastic", new Color(0.05f, 0.05f, 0.05f), 0.45f);
            _slots = new Dictionary<string, Material>
            {
                ["DrumPaint"] = Mat("DrumOlive", new Color(0.32f, 0.36f, 0.25f), 1f, 1f, drumMap, drumMs),
                ["DrumCap"] = Mat("DrumCap", new Color(0.32f, 0.30f, 0.27f), 0.35f, 0.8f),
                ["Wood"] = Town("DoorWood", Mat("Wood", new Color(0.36f, 0.22f, 0.12f), 0.35f)),
                ["WoodDark"] = Mat("WoodDark", new Color(0.16f, 0.10f, 0.06f), 0.35f),
                ["Brass"] = Mat("Brass", new Color(0.72f, 0.56f, 0.28f), 0.55f, 1f),
                ["Mattress"] = Town("Mattress", Mat("Mattress", new Color(0.78f, 0.75f, 0.68f), 0.18f)),
                ["Blanket"] = Mat("Blanket", new Color(0.42f, 0.20f, 0.16f), 0.12f),
                ["Cloth"] = Mat("Cloth", new Color(0.33f, 0.28f, 0.22f), 0.15f),
                ["ClothLight"] = Mat("ClothLight", new Color(0.44f, 0.38f, 0.30f), 0.15f),
                ["Plinth"] = Mat("Plinth", new Color(0.13f, 0.12f, 0.11f), 0.3f),
                ["Cabinet"] = Mat("Cabinet", new Color(0.78f, 0.76f, 0.70f), 0.42f),
                ["Worktop"] = Mat("Worktop", new Color(0.30f, 0.28f, 0.26f), 0.55f),
                ["Steel"] = Mat("Steel", new Color(0.62f, 0.62f, 0.60f), 0.55f, 1f),
                ["Concrete"] = Town("ConcreteFloor", Mat("Concrete", new Color(0.5f, 0.49f, 0.46f), 0.15f)),
                ["PaintWhite"] = Mat("PaintWhite", new Color(0.86f, 0.86f, 0.83f), 0.5f),
                ["PaintGreen"] = Mat("PaintGreen", new Color(0.03f, 0.31f, 0.20f), 0.5f),
                ["PaintYellow"] = Mat("PaintYellow", new Color(0.78f, 0.58f, 0.08f), 0.42f),
                ["PaintBlack"] = Mat("PaintBlack", new Color(0.05f, 0.05f, 0.05f), 0.42f),
                ["Vent"] = Mat("Vent", new Color(0.06f, 0.06f, 0.06f), 0.25f),
                ["Plastic"] = plain,
                ["Rubber"] = Mat("Rubber", new Color(0.035f, 0.035f, 0.035f), 0.28f),
                ["Screen"] = Mat("Screen", Color.white, 0.8f, 0f, Tex("Pump_Display.png", false, 512)),
                ["LampLens"] = Mat("LampLens", new Color(0.95f, 0.95f, 0.9f), 0.7f),
            };
            return _slots;
        }

        /// <summary>A model's prefab. <paramref name="box"/>: one collider round all of it (furniture, what the navmesh stands clear of), else convex.</summary>
        static GameObject Prefab(string name, bool box, float cullHeight, Dictionary<string, Material> overrides = null, string outName = null)
        {
            string fbx = $"{Folder}/{name}.fbx";
            if (AssetImporter.GetAtPath(fbx) is ModelImporter mi)
            {
                bool change = mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.animationType != ModelImporterAnimationType.None || mi.meshCompression != ModelImporterMeshCompression.Low;
                if (change)
                {
                    mi.materialImportMode = ModelImporterMaterialImportMode.None;
                    mi.animationType = ModelImporterAnimationType.None;
                    mi.importAnimation = false;
                    mi.meshCompression = ModelImporterMeshCompression.Low;
                    mi.importTangents = ModelImporterTangents.None;
                    mi.SaveAndReimport();
                }
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (model == null) { Debug.LogWarning($"[FPSKit] world model missing: {fbx}"); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.transform.position = Vector3.zero;
            go.name = outName ?? name;
            Assign(go, overrides);
            Finish(go, box, cullHeight);
            if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder(FPSKitTownAssets.Out, "World");
            var saved = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/{outName ?? name}.prefab");
            Object.DestroyImmediate(go);
            return saved;
        }

        /// <summary>Each material slot of the imported mesh, by the slot's name in the Blender script (the mesh keeps them in order).</summary>
        static void Assign(GameObject go, Dictionary<string, Material> overrides)
        {
            var slots = Slots();
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var names = NamesOf(r);
                var mats = new Material[names.Length];
                for (int i = 0; i < names.Length; i++)
                    mats[i] = names[i] != null && overrides != null && overrides.TryGetValue(names[i], out var o) ? o : names[i] != null && slots.TryGetValue(names[i], out var m) ? m : slots["Plastic"];
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            }
        }

        /// <summary>With no imported materials the renderer holds empty slots: names come from the FBX, read back from the importer's embedded list.</summary>
        static string[] NamesOf(MeshRenderer r)
        {
            string file = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh));
            return SlotOrder.TryGetValue(file, out var names) ? names : new string[r.GetComponent<MeshFilter>().sharedMesh.subMeshCount];
        }

        /// <summary>Each model's material slots, in the order world_props.py prints them.</summary>
        static readonly Dictionary<string, string[]> SlotOrder = new Dictionary<string, string[]>
        {
            ["Drum_Steel"] = new[] { "DrumPaint", "DrumCap" },
            ["Wardrobe"] = new[] { "Wood", "Brass" },
            ["Bed_Double"] = new[] { "Wood", "Mattress", "Blanket" },
            ["BedsideTable"] = new[] { "Wood", "Brass" },
            ["Armchair"] = new[] { "WoodDark", "Cloth", "ClothLight" },
            ["KitchenBase"] = new[] { "Plinth", "Cabinet", "Steel", "Worktop" },
            ["FuelPump"] = new[] { "Concrete", "PaintWhite", "Vent", "Steel", "PaintGreen", "Plastic", "Screen", "Rubber" },
            ["CanopyLight"] = new[] { "Steel", "LampLens" },
            ["Bollard"] = new[] { "PaintYellow", "PaintBlack", "Steel" },
            ["CrashGuard"] = new[] { "PaintYellow" },
        };

        static void Finish(GameObject go, bool box, float cullHeight)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            if (box)
            {
                var b = new Bounds(); bool any = false;
                foreach (var r in rends) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                var col = go.AddComponent<BoxCollider>();
                col.center = b.center - go.transform.position; col.size = b.size;
            }
            else
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh; mc.convex = true;
                }
            var lod = go.GetComponent<LODGroup>();
            if (lod == null) lod = go.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(cullHeight, rends) });
            lod.RecalculateBounds();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                t.gameObject.layer = LayerMask.NameToLayer("Environment");
            }
        }

        /// <summary>Everything this file makes, without a town build.</summary>
        public static void Batch()
        {
            FPSKitSceneBuilder.MakeGlassMaterials();
            FPSKitTownAssets.CarWindowLook();
            Make();
            int loose = FPSKitTownAssets.MakeLooseProps();
            var bodies = MakeCivilianBodies();
            AssetDatabase.SaveAssets();
            Debug.Log($"[FPSKitBatch] PASSED: world models: {loose} loose props, {bodies.Count} bodies");
            FPSKitBatch.Exit(0);
        }

        /// <summary>Every model lit and photographed in a row (Build/WorldModels), to look at the thing itself. Needs a real graphics device.</summary>
        public static void BatchSheet()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 8f;
            ground.GetComponent<Renderer>().sharedMaterial = Mat("SheetGround", new Color(0.45f, 0.42f, 0.38f), 0.2f);
            var sunGo = new GameObject("Sun"); var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 2f; sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(45f, 150f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
            DynamicGI.UpdateEnvironment();
            var camGo = new GameObject("Cam"); var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.68f, 0.75f); cam.fieldOfView = 32f;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            void Shot(string file)
            {
                var request = new RenderPipeline.StandardRequest { destination = rt };
                RenderPipeline.SubmitRenderRequest(cam, request);
                RenderPipeline.SubmitRenderRequest(cam, request);
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                RenderTexture.active = null;
                Directory.CreateDirectory("Build/WorldModels");
                File.WriteAllBytes($"Build/WorldModels/{file}.png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            void Row(string file, IEnumerable<GameObject> items, float spacing, Vector3 camAt, Vector3 look)
            {
                var made = new List<GameObject>(); float x = 0f;
                foreach (var p in items) { if (p == null) continue; var g = (GameObject)PrefabUtility.InstantiatePrefab(p); g.transform.position = new Vector3(x, 0f, 0f); g.transform.rotation = Quaternion.Euler(0f, 180f - 25f, 0f); made.Add(g); x += spacing; }
                cam.transform.position = camAt + Vector3.right * (x - spacing) * 0.5f;
                cam.transform.LookAt(look + Vector3.right * (x - spacing) * 0.5f);
                Shot(file);
                foreach (var g in made) Object.DestroyImmediate(g);
            }
            Row("furniture", new[] { WorldPrefab("Wardrobe"), WorldPrefab("Wardrobe_White"), WorldPrefab("Bed_Double"), WorldPrefab("Armchair"), WorldPrefab("Armchair_Green"), WorldPrefab("BedsideTable"), WorldPrefab("KitchenBase") }, 2.3f, new Vector3(0f, 2.3f, -12f), new Vector3(0f, 0.8f, 0f));
            Row("station", new[] { AssetDatabase.LoadAssetAtPath<GameObject>(FPSKitTownAssets.PropFolder + "/Barrel_01.prefab"), AssetDatabase.LoadAssetAtPath<GameObject>(FPSKitTownAssets.PropFolder + "/barrel_03.prefab"), WorldPrefab("FuelPump"), WorldPrefab("Bollard"), WorldPrefab("CrashGuard") }, 1.6f, new Vector3(0f, 1.5f, -5.2f), new Vector3(0f, 0.8f, 0f));
            Row("people", MakeCivilianBodies(), 1.0f, new Vector3(0f, 1.3f, -6.5f), new Vector3(0f, 0.9f, 0f));
            Debug.Log("[FPSKitBatch] PASSED: sheet written to Build/WorldModels");
            FPSKitBatch.Exit(0);
        }

        public static GameObject WorldPrefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{name}.prefab");

        /// <summary>All the prefabs, and the new drum into the two Barrel prefabs. Idempotent.</summary>
        public static void Make()
        {
            _slots = null;
            AssetDatabase.Refresh();
            Prefab("Wardrobe", true, 0.02f);
            Prefab("Wardrobe", true, 0.02f, new Dictionary<string, Material> { ["Wood"] = Mat("CabinetPaint", new Color(0.82f, 0.80f, 0.74f), 0.42f) }, "Wardrobe_White");
            Prefab("Bed_Double", true, 0.02f);
            Prefab("Bed_Double", true, 0.02f, new Dictionary<string, Material> { ["Blanket"] = Mat("BlanketBlue", new Color(0.16f, 0.22f, 0.32f), 0.12f) }, "Bed_Double_Blue");
            Prefab("BedsideTable", true, 0.015f);
            Prefab("Armchair", true, 0.018f);
            Prefab("Armchair", true, 0.018f, new Dictionary<string, Material> { ["Cloth"] = Mat("ClothGreen", new Color(0.16f, 0.24f, 0.18f), 0.15f), ["ClothLight"] = Mat("ClothGreenLight", new Color(0.24f, 0.34f, 0.26f), 0.15f) }, "Armchair_Green");
            Prefab("KitchenBase", true, 0.018f);
            Prefab("FuelPump", false, 0.02f);
            Prefab("CanopyLight", false, 0.01f);
            Prefab("Bollard", false, 0.012f);
            Prefab("CrashGuard", false, 0.012f);
            var olive = Prefab("Drum_Steel", false, 0.012f, null, "Drum_Olive");
            var drumMap = Tex("Drum_Albedo.png"); var drumMs = Tex("Drum_MS.png", true);
            var blue = Prefab("Drum_Steel", false, 0.012f, new Dictionary<string, Material> { ["DrumPaint"] = Mat("DrumBlue", new Color(0.13f, 0.24f, 0.42f), 1f, 1f, drumMap, drumMs) }, "Drum_Blue");
            IntoBarrel("Barrel_01", blue);
            IntoBarrel("barrel_03", olive);
            AssetDatabase.SaveAssets();
            Debug.Log("[FPSKit] world models made");
        }

        /// <summary>A Barrel prefab's model replaced by the new drum: every place one stands (and its fuel paint) keeps working.</summary>
        static void IntoBarrel(string id, GameObject drum)
        {
            if (drum == null) return;
            string path = $"{FPSKitTownAssets.PropFolder}/{id}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
            var root = PrefabUtility.LoadPrefabContents(path);
            for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            foreach (var c in root.GetComponents<Collider>()) Object.DestroyImmediate(c);
            foreach (var r in root.GetComponents<Renderer>()) Object.DestroyImmediate(r);
            foreach (var f in root.GetComponents<MeshFilter>()) Object.DestroyImmediate(f);
            var lod0 = root.GetComponent<LODGroup>();
            if (lod0 != null) Object.DestroyImmediate(lod0);
            var model = Object.Instantiate(drum, root.transform);
            model.name = "Drum";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            var inner = model.GetComponent<LODGroup>();
            if (inner != null) Object.DestroyImmediate(inner);
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.012f, model.GetComponentsInChildren<Renderer>(true)) });
            lod.RecalculateBounds();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        // ---- People ------------------------------------------------------------------------------------------------------

        /// <summary>
        /// A body's prefab: Humanoid (the clips play on it as on anyone), each mesh given the image that came with it, named so a civilian
        /// reads as one. Returns the prefabs in a fixed order.
        /// </summary>
        public static List<GameObject> MakeCivilianBodies()
        {
            var made = new List<GameObject>();
            if (!AssetDatabase.IsValidFolder(Civilians)) return made;
            foreach (var guid in AssetDatabase.FindAssets("t:GameObject Civ_", new[] { Civilians }))
            {
                string fbx = AssetDatabase.GUIDToAssetPath(guid);
                if (!fbx.EndsWith(".fbx")) continue;
                if (AssetImporter.GetAtPath(fbx) is ModelImporter mi && (mi.animationType != ModelImporterAnimationType.Human || mi.materialImportMode != ModelImporterMaterialImportMode.None))
                {
                    mi.animationType = ModelImporterAnimationType.Human;
                    mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    mi.materialImportMode = ModelImporterMaterialImportMode.None;
                    mi.importAnimation = false;
                    mi.meshCompression = ModelImporterMeshCompression.Low;
                    mi.SaveAndReimport();
                }
                string name = Path.GetFileNameWithoutExtension(fbx);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                if (model == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.transform.position = Vector3.zero;
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    string part = smr.name.Split('.')[0];
                    string file = part.StartsWith("Civ_") ? $"{name}_{part}" : $"{name}_Skin";
                    var count = smr.sharedMesh.subMeshCount;
                    var mat = PersonMat(file, part);
                    var mats = new Material[count];
                    for (int i = 0; i < count; i++) mats[i] = mat;
                    smr.sharedMaterials = mats;
                    smr.updateWhenOffscreen = false;
                    smr.quality = SkinQuality.Bone2;           // two bones a vertex: most of the skinning's cost on a town of them
                }
                made.Add(PrefabUtility.SaveAsPrefabAsset(go, $"{Civilians}/{name}.prefab"));
                Object.DestroyImmediate(go);
            }
            made.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return made;
        }

        static Material PersonMat(string file, string part)
        {
            string path = $"{Civilians}/{file}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = m == null;
            if (create) m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Civilians}/{file}.png");
            if (AssetImporter.GetAtPath($"{Civilians}/{file}.png") is TextureImporter t && t.maxTextureSize != 1024) { t.maxTextureSize = 1024; t.SaveAndReimport(); }
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", part == "Civ_Eyes" ? new Color(0.14f, 0.09f, 0.06f) : part == "Civ_Brows" ? new Color(0.06f, 0.045f, 0.035f) : Color.white);
            bool hair = part == "Civ_Hair" || part == "Civ_Brows";
            if (hair)
            {
                m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.35f); m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0f);
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            m.SetFloat("_Smoothness", part.StartsWith("Civ_") ? 0.12f : 0.32f);
            m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }
    }
}
#endif
