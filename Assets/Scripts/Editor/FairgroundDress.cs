#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Step 2 of the Abandoned Fairground rebuild: ground, roads, water, props and a night look, laid over the
    /// approved gray blockout. Runs the blockout generator first (same layout, same seed), then
    ///   * swaps the box cover and the rubble-wall sightline blockers for the pack's props
    ///     (fences, wrecked cars, a beached boat, benches, furniture),
    ///   * puts real CC0 PBR materials on every remaining primitive (FG_WorldPBR_URP, world-space triplanar),
    ///   * paints the terrain with real layers and plants a dark treeline on the embankment,
    ///   * lays asphalt and concrete road meshes with curbs,
    ///   * replaces the blue plane with FG_Water_URP, and sets the night light, fog, sky and Volume.
    /// Output: Assets/Fairground/Scenes/AbandonedFairground_Step2.unity. The blockout scene is left as it was.
    /// </summary>
    public static class FairgroundDress
    {
        public const string ScenePath = "Assets/Fairground/Scenes/AbandonedFairground_Step2.unity";
        const string TP = "Assets/Fairground/ThirdParty";
        const string MatDir = "Assets/Fairground/Materials/Dressed";

        public static string LastReport = "";

        static System.Random rng;
        static Transform root, props, roads;
        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        static readonly Dictionary<string, GameObject> PackCache = new Dictionary<string, GameObject>();
        static int swappedBlockers, swappedCover, swappedCars, swappedOther;

        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        static float H(float x, float z) => FairgroundBlockout.H(x, z);

        [MenuItem("Tools/MiniFPS/Fairground/Build Step 2 Dressed Scene")]
        public static void Build()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Generate();
            Finish(true);
        }

        /// <summary>
        /// Stage one, in the open scene (the game's scene builder): the sites, their dressing and decals, in PLANNED coordinates.
        /// The lights go in next (FairgroundLighting.ApplyWorld), then Finish stretches everything out to the big arena.
        /// </summary>
        public static void Generate()
        {
            FairgroundMaterialConverter.ConvertAll();
            FairgroundLookTest.SetUpTextures();
            FairgroundBlockout.Generate();

            rng = new System.Random(77);
            root = GameObject.Find("Blockout").transform;
            props = new GameObject("10_PackProps").transform; props.SetParent(root, false);
            roads = new GameObject("11_Roads").transform; roads.SetParent(root, false);
            Mats.Clear(); PackCache.Clear();
            swappedBlockers = swappedCover = swappedCars = swappedOther = 0;

            FairgroundLookTest.CapGeneratedTextures();
            MakeMaterials();
            SetUpNight(false);
            Release();
            // The lanes as planned: the tape, litter and signs follow them, then move with the site they sit beside.
            var strips = PlannedStrips.Select(r => (r.a, r.b, r.w)).ToList();
            plannedDressing = FairgroundDressing.Place(root, strips);
            plannedDecals = FairgroundDecals.Place(root, strips, new Vector4(-86f, -30f, 60f, 90f));
            Release();
        }

        static string plannedDressing = "", plannedDecals = "";

        /// <summary>Stage two: stretch the fair out, then everything that belongs to the big arena rather than to a site.</summary>
        public static void Finish(bool standalone)
        {
            FairgroundBlockout.FinalStretch();
            SwapProps();
            Retexture();
            BuildRoads();
            Release();
            PaintTerrainAndTrees();
            BuildOldGrowth();
            Release();
            FairgroundLookTest.DisableAutoBake(standalone ? "Step2" : "Fairground");
            if (standalone)
            {
                var scene = EditorSceneManager.GetActiveScene();
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
            }
            LastReport = $"swapped: {swappedBlockers} blockers, {swappedCover} cover pieces, {swappedCars} cars, {swappedOther} other props; {plannedDressing}; {plannedDecals}";
            Debug.Log("[FPSKit] Fairground step 2 built: " + ScenePath + "\n" + LastReport);
        }

        /// <summary>Drops whatever the last stage loaded and no longer needs: this build runs close to the machine's memory limit.</summary>
        static void Release()
        {
            EditorUtility.UnloadUnusedAssetsImmediate();
            System.GC.Collect();
        }

        // ------------------------------------------------------------------ materials

        static Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        static Material World(string name, string folder, string stem, string style, float tiling, Color tint, float wet = 0.25f, float smoothScale = 1f)
        {
            FairgroundMaterialConverter.EnsureFolder(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Fairground/FG_WorldPBR_URP")); AssetDatabase.CreateAsset(m, path); }
            string d = $"{TP}/{folder}/{stem}";
            // Poly Haven: slug_diff_2k.jpg ...  ambientCG: Id_2K-JPG_Color.jpg ...
            string[] names = style == "ph"
                ? new[] { d + "_diff_2k.jpg", d + "_nor_gl_2k.jpg", d + "_rough_2k.jpg", d + "_ao_2k.jpg" }
                : new[] { d + "_2K-JPG_Color.jpg", d + "_2K-JPG_NormalGL.jpg", d + "_2K-JPG_Roughness.jpg", d + "_2K-JPG_AmbientOcclusion.jpg" };
            m.SetTexture("_BaseMap", Tex(names[0]));
            m.SetTexture("_BumpMap", Tex(names[1]));
            m.SetTexture("_RoughMap", Tex(names[2]));
            m.SetTexture("_AOMap", Tex(names[3]));
            m.SetColor("_Tint", tint);
            m.SetFloat("_Tiling", tiling);
            m.SetFloat("_Wetness", wet);
            m.SetFloat("_SmoothScale", smoothScale);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            Mats[name] = m;
            return m;
        }

        static void MakeMaterials()
        {
            World("Concrete", "concrete_wall_006", "concrete_wall_006", "ph", 3f, new Color(0.80f, 0.82f, 0.84f));
            World("ConcreteDark", "concrete_wall_006", "concrete_wall_006", "ph", 3f, new Color(0.50f, 0.52f, 0.54f), 0.35f);
            World("Rust", "rusty_painted_metal", "rusty_painted_metal", "ph", 2.5f, new Color(0.85f, 0.83f, 0.80f), 0.2f);
            World("RustDark", "rusty_painted_metal", "rusty_painted_metal", "ph", 2.5f, new Color(0.50f, 0.50f, 0.52f), 0.3f);
            World("CarRust", "rusty_painted_metal", "rusty_painted_metal", "ph", 2f, new Color(0.72f, 0.62f, 0.56f), 0.3f);
            World("PeelWood", "wood_peeling_paint_weathered", "wood_peeling_paint_weathered", "ph", 3f, new Color(0.88f, 0.86f, 0.82f));
            World("Planks", "weathered_planks", "weathered_planks", "ph", 2.5f, new Color(0.82f, 0.80f, 0.76f), 0.4f);
            World("Fabric", "Fabric061", "Fabric061", "acg", 2f, new Color(0.62f, 0.60f, 0.54f), 0.15f, 0.5f);
            World("Asphalt", "Asphalt012", "Asphalt012", "acg", 5f, new Color(0.62f, 0.64f, 0.66f), 0.45f);
            World("RoadConcrete", "concrete_wall_006", "concrete_wall_006", "ph", 4f, new Color(0.66f, 0.68f, 0.70f), 0.4f);
            World("Curb", "concrete_wall_006", "concrete_wall_006", "ph", 2f, new Color(0.60f, 0.62f, 0.64f), 0.3f);
        }

        // ------------------------------------------------------------------ pack props

        static GameObject Pack(string name)
        {
            if (PackCache.TryGetValue(name, out var g)) return g;
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Prefab", new[] { "Assets/Flooded_Grounds/Prefabs" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == name) { g = AssetDatabase.LoadAssetAtPath<GameObject>(p); break; }
            }
            if (g == null) Debug.LogWarning("[FPSKit] pack prefab missing: " + name);
            PackCache[name] = g;
            return g;
        }

        static GameObject Spawn(string prefab, Vector3 pos, float yaw, bool snap = true, float pitch = 0f, float roll = 0f)
        {
            var src = Pack(prefab);
            if (src == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            FairgroundMaterialConverter.Remap(go);
            go.transform.SetParent(props, false);
            if (snap) pos.y = H(pos.x, pos.z);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(pitch, yaw, roll));
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
            return go;
        }

        static string Pick(params string[] names) => names[rng.Next(names.Length)];

        static List<Transform> ChildrenNamed(Transform parent, params string[] names)
        {
            var list = new List<Transform>();
            if (parent == null) return list;
            foreach (Transform c in parent) if (names.Contains(c.name)) list.Add(c);
            return list;
        }

        static void SwapProps()
        {
            // --- sightline blockers: fence runs, wrecked cars, a beached boat (never a floating slab)
            var bl = root.Find("9_SightlineBlockers");
            if (bl != null)
                foreach (var t in bl.Cast<Transform>().ToList())
                {
                    Vector3 p = t.position; float yaw = t.eulerAngles.y;
                    Vector3 along = new Vector3(Mathf.Cos(yaw * Mathf.Deg2Rad), 0, -Mathf.Sin(yaw * Mathf.Deg2Rad));
                    Object.DestroyImmediate(t.gameObject);
                    int k = rng.Next(10);
                    if (k < 6) { Spawn("Struct_Fence1_Mid_B", p - along * 3.2f, yaw); Spawn("Struct_Fence1_Mid_B", p + along * 3.2f, yaw + R(-6, 6)); }
                    else if (k < 9) { Spawn(Pick("Prop_Car_A", "Prop_Car1_DM"), p - along * 2.6f, yaw + 90f + R(-15, 15)); Spawn("Prop_Cabinet_A", p + along * 2.8f, R(0, 360), true, 0, R(-8, 8)); swappedCars++; }
                    else Spawn("Prop_Boat_A", p, yaw + 90f, true, R(-3, 3), R(-6, 6));
                    swappedBlockers++;
                }

            // --- box cover -> real things people left behind
            foreach (var group in new[] { root.Find("9_Cover"), root.Find("9_Cover_Fill") })
            {
                if (group == null) continue;
                foreach (var t in group.Cast<Transform>().ToList())
                {
                    Vector3 p = t.position; float yaw = t.eulerAngles.y; string n = t.name;
                    Object.DestroyImmediate(t.gameObject);
                    switch (n)
                    {
                        case "Crate": Spawn(Pick("Prop_Sofa_A", "Prop_LargeTable_A"), p, yaw, true, 0, R(-4, 4)); break;
                        case "CrateStack": Spawn(Pick("Prop_Cabinet_A", "Prop_Cabinet_B"), p, yaw, true, 0, R(-10, 10)); break;
                        case "Barrier": Spawn(Pick("Struct_Fence2_Mid_A", "Struct_FlowerBox_A", "Struct_Fence3_Mid_A"), p, yaw); break;
                        case "Bench": Spawn("Prop_ParkBench_A", p, yaw); break;
                        case "Bin": Spawn(Pick("CobbleRock_A", "CobbleRock_B", "CobbleRock_C"), p, yaw); break;
                        default: Spawn("Prop_LargeTable_A", p, yaw); break;
                    }
                    swappedCover++;
                }
            }

            // --- the arch's old block "letters": the banner texture carries the broken lettering now
            foreach (var t in ChildrenNamed(root.Find("1_MainGate"), "HangingLetter", "FallenLetter").ToList()) Object.DestroyImmediate(t.gameObject);

            // --- cars in the parking lot
            var gate = root.Find("1_MainGate");
            foreach (var t in ChildrenNamed(gate, "Car").Concat(ChildrenNamed(root.Find("A_Outskirts"), "Car")).ToList())
            {
                Vector3 p = t.position; float yaw = t.eulerAngles.y;
                Object.DestroyImmediate(t.gameObject);
                Spawn(rng.NextDouble() < 0.55 ? "Prop_Car_A" : "Prop_Car1_DM", p, yaw);
                swappedCars++;
            }
            foreach (var t in ChildrenNamed(gate, "QueueBarrier"))
            {
                Vector3 p = t.position; float yaw = t.eulerAngles.y;
                Object.DestroyImmediate(t.gameObject);
                Spawn("Struct_Fence2_Mid_A", p, yaw); swappedOther++;
            }

            // --- flood debris: planks and furniture floating or half-sunk
            var low = root.Find("5_FloodedLowlands");
            foreach (var t in ChildrenNamed(low, "FloatingPallet", "SunkenCrate"))
            {
                Vector3 p = t.position; float yaw = t.eulerAngles.y; bool pal = t.name == "FloatingPallet";
                Object.DestroyImmediate(t.gameObject);
                if (pal) Spawn("Struct_WoodBoard_A", new Vector3(p.x, 0.02f, p.z), yaw, false, 90f);
                else Spawn(Pick("Prop_Cabinet_A", "Prop_Sofa_A", "Prop_Chair_A", "Prop_SmallTable_A", "Prop_Cabinet_B"), p, yaw, true, R(-12, 12), R(-12, 12));
                swappedOther++;
            }
            foreach (var t in ChildrenNamed(root.Find("3_CarouselPlaza"), "Planter"))
            {
                Vector3 p = t.position; float yaw = t.eulerAngles.y;
                Object.DestroyImmediate(t.gameObject);
                Spawn("Struct_FlowerBox_A", p, yaw);
                swappedOther++;
            }
            foreach (var t in ChildrenNamed(root.Find("6_RollerCoaster"), "TrackDebris"))
            {
                Vector3 p = t.position; float yaw = t.eulerAngles.y;
                Object.DestroyImmediate(t.gameObject);
                Spawn(Pick("Prop_Cabinet_B", "CobbleRock_A", "CobbleRock_C", "Struct_Fence3_Mid_A", "CobbleRock_E"), p, yaw, true, R(-8, 8), R(-8, 8));
                swappedOther++;
            }
            foreach (var t in ChildrenNamed(root.Find("8_BigTop"), "RingDebris"))
            {
                Vector3 p = t.position; float yaw = t.eulerAngles.y;
                Object.DestroyImmediate(t.gameObject);
                Spawn(Pick("Prop_ChurchBench_A", "Prop_ParkBench_A", "Prop_LargeTable_A", "Prop_Sofa_A"), p, yaw, true, 0, R(-6, 6));
                swappedOther++;
            }
        }

        // ------------------------------------------------------------------ retexture the primitives

        static Material PickMat(Renderer r)
        {
            string n = r.name, m = r.sharedMaterial != null ? r.sharedMaterial.name : "";
            if (n.Contains("Tent") || n == "RoofSlab" || n == "RoofSlab_Fallen") return Mats["Fabric"];
            if (n.StartsWith("BoundaryWall")) return Mats["ConcreteDark"];
            if (n == "Rim" || n == "Spoke" || n == "Hub" || n == "Leg" || n == "Track" || n == "Support" || n.StartsWith("Gondola") || n.StartsWith("Station") && n != "StationPlatform")
                return Mats["Rust"];
            if (n.Contains("Roof")) return Mats["RustDark"];
            if (n.Contains("Fence")) return Mats["RustDark"];
            switch (m)
            {
                case "Gray_Structure": return Mats["Concrete"];
                case "Gray_Cover": return Mats["PeelWood"];
                case "Gray_Light": return Mats["PeelWood"];
                case "Gray_Dark": return Mats["RustDark"];
                case "Gray_Car": return Mats["CarRust"];
                case "Gray_Boardwalk": return Mats["Planks"];
            }
            return null;
        }

        static void Retexture()
        {
            int n = 0;
            // Only the blockout's own groups: the dressing, decals and lights carry materials of their own.
            var blockoutGroups = new HashSet<string> { "0_Boundary", "1_MainGate", "2_Midway", "3_CarouselPlaza", "4_FerrisWheel", "5_FloodedLowlands", "6_RollerCoaster", "7_BacklotServiceYard", "8_BigTop", "9_Cover", "9_Cover_Fill", "9_SightlineBlockers", "A_Outskirts" };
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var top = r.transform; while (top.parent != null && top.parent != root) top = top.parent;
                if (!blockoutGroups.Contains(top.name)) continue;
                if (PrefabUtility.IsPartOfPrefabInstance(r.gameObject)) continue;      // pack props keep their own (converted) materials
                var m = PickMat(r);
                if (m == null) continue;
                r.sharedMaterial = m;
                n++;
            }
            swappedOther += n;
        }

        // ------------------------------------------------------------------ roads

        struct Road { public int kind; public Vector2 a, b, c; public float w, r; public float x0, x1, z0, z1; public bool concrete; public float off; }

        static readonly List<Road> RoadList = new List<Road>();

        static Road Strip(Vector2 a, Vector2 b, float w, float off) => new Road { kind = 0, a = a, b = b, w = w, off = off };
        static Road Disc(Vector2 c, float r, float off) => new Road { kind = 1, c = c, r = r, concrete = true, off = off };
        static Road Rect(float x0, float x1, float z0, float z1, bool concrete, float off) => new Road { kind = 2, x0 = x0, x1 = x1, z0 = z0, z1 = z1, concrete = concrete, off = off };

        /// <summary>Signed distance from p to a road shape (negative inside).</summary>
        static float Sdf(Road s, Vector2 p)
        {
            switch (s.kind)
            {
                case 0:
                {
                    Vector2 ab = s.b - s.a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - s.a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                    return Vector2.Distance(p, s.a + ab * t) - s.w * 0.5f;
                }
                case 1: return Vector2.Distance(p, s.c) - s.r;
                default:
                {
                    Vector2 c = new Vector2((s.x0 + s.x1) * 0.5f, (s.z0 + s.z1) * 0.5f), h = new Vector2((s.x1 - s.x0) * 0.5f, (s.z1 - s.z0) * 0.5f);
                    Vector2 d = new Vector2(Mathf.Abs(p.x - c.x) - h.x, Mathf.Abs(p.y - c.y) - h.y);
                    return Vector2.Max(d, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f);
                }
            }
        }

        static float SdfAll(Vector2 p, bool? concrete = null)
        {
            float best = 99f;
            foreach (var s in RoadList)
            {
                bool isConcrete = s.kind == 1 || (s.kind == 2 && s.concrete);
                if (concrete.HasValue && isConcrete != concrete.Value) continue;
                best = Mathf.Min(best, Sdf(s, p));
            }
            return best;
        }

        /// <summary>The lanes where they were planned, before the stretch: the dressing is placed along these.</summary>
        static readonly List<Road> PlannedStrips = new List<Road>
        {
            new Road { kind = 0, a = new Vector2(-45, 56), b = new Vector2(-45, -48), w = 6f },
            new Road { kind = 0, a = new Vector2(38, 56), b = new Vector2(38, -26), w = 6f },
            new Road { kind = 0, a = new Vector2(-52, 31), b = new Vector2(52, 31), w = 6f },
            new Road { kind = 0, a = new Vector2(-48, -12), b = new Vector2(50, -12), w = 8f },
            new Road { kind = 0, a = new Vector2(-8, -28), b = new Vector2(-10, -49), w = 8f },
        };

        static void AddRoadShapes()
        {
            RoadList.Clear();
            // The sites moved out (FairgroundBlockout.StretchSites); the roads are laid between them in the new coordinates.
            RoadList.Add(Rect(-86, -30, 135, 165, false, 0.030f));                                     // parking lot (moved with the gate)
            RoadList.Add(Strip(new Vector2(-90, 112), new Vector2(-90, -96), 6f, 0.045f));             // west lane
            RoadList.Add(Strip(new Vector2(76, 112), new Vector2(76, -52), 6f, 0.045f));               // east lane
            RoadList.Add(Strip(new Vector2(-104, 62), new Vector2(104, 62), 6f, 0.040f));              // north cross path
            RoadList.Add(Strip(new Vector2(-96, -24), new Vector2(100, -24), 8f, 0.040f));             // south cross path
            RoadList.Add(Strip(new Vector2(-16, -56), new Vector2(-20, -116), 8f, 0.045f));            // plaza to Big Top
            RoadList.Add(Strip(new Vector2(76, 64), new Vector2(112, 64), 6f, 0.040f));                // east lane to the backlot
            RoadList.Add(Rect(-32, 32, 131, 153, true, 0.060f));                                       // gate plaza
            RoadList.Add(Rect(-12, 12, -5, 131, true, 0.065f));                                        // the Midway, carousel to gate
            RoadList.Add(Disc(FairgroundBlockout.CarouselCentre, 19f, 0.070f));                        // carousel plaza
            RoadList.Add(Disc(FairgroundBlockout.FerrisCentre, 12f, 0.060f));                          // Ferris plaza
            // Beyond the fair: the gate road runs on, and service roads ring the north and west.
            RoadList.Add(Rect(-8, 8, 165, 226, false, 0.035f));
            RoadList.Add(Strip(new Vector2(-200, 185), new Vector2(200, 185), 8f, 0.035f));
            RoadList.Add(Strip(new Vector2(-195, -200), new Vector2(-195, 200), 8f, 0.035f));
            RoadList.Add(Strip(new Vector2(-205, 0), new Vector2(-150, 0), 6f, 0.035f));
            RoadList.Add(Strip(new Vector2(150, 140), new Vector2(215, 140), 6f, 0.035f));
        }

        static Mesh NewMesh(string name, List<Vector3> v, List<int> t)
        {
            var m = new Mesh { name = name, indexFormat = v.Count > 60000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            var tan = new Vector4[v.Count];
            for (int i = 0; i < tan.Length; i++) tan[i] = new Vector4(1, 0, 0, 1);
            m.tangents = tan;
            m.uv = new Vector2[v.Count];
            return m;
        }

        static float TopY(float x, float z, float off) => H(x, z) + off;

        static GameObject StripObject(Road s)
        {
            var pts = new List<Vector2>();
            float len = Vector2.Distance(s.a, s.b);
            int n = Mathf.Max(2, Mathf.CeilToInt(len / 2f));
            for (int i = 0; i <= n; i++) pts.Add(Vector2.Lerp(s.a, s.b, i / (float)n));
            Vector2 dir = (s.b - s.a).normalized, left = new Vector2(-dir.y, dir.x);
            var v = new List<Vector3>(); var t = new List<int>();
            foreach (var c in pts)
            {
                Vector2 L = c + left * s.w * 0.5f, Rr = c - left * s.w * 0.5f;
                v.Add(new Vector3(L.x, TopY(L.x, L.y, s.off), L.y));
                v.Add(new Vector3(Rr.x, TopY(Rr.x, Rr.y, s.off), Rr.y));
            }
            for (int i = 0; i < pts.Count - 1; i++)
            {
                int l0 = i * 2, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
                t.AddRange(new[] { l0, l1, r0, r0, l1, r1 });
            }
            return MakeRoadObject("Road_Asphalt", NewMesh("Road", v, t), Mats["Asphalt"]);
        }

        static GameObject RectObject(Road s)
        {
            int nx = Mathf.CeilToInt((s.x1 - s.x0) / 2f), nz = Mathf.CeilToInt((s.z1 - s.z0) / 2f);
            var v = new List<Vector3>(); var t = new List<int>();
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float x = Mathf.Lerp(s.x0, s.x1, i / (float)nx), z = Mathf.Lerp(s.z0, s.z1, j / (float)nz);
                    v.Add(new Vector3(x, TopY(x, z, s.off), z));
                }
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + nx + 1, c = a + 1, d = b + 1;
                    t.AddRange(new[] { a, b, c, c, b, d });
                }
            return MakeRoadObject(s.concrete ? "Road_Concrete" : "Road_Asphalt_Lot", NewMesh("Slab", v, t), s.concrete ? Mats["RoadConcrete"] : Mats["Asphalt"]);
        }

        static GameObject DiscObject(Road s)
        {
            const int sectors = 56;
            int rings = Mathf.CeilToInt(s.r / 2f);
            var v = new List<Vector3>(); var t = new List<int>();
            v.Add(new Vector3(s.c.x, TopY(s.c.x, s.c.y, s.off), s.c.y));
            for (int r = 1; r <= rings; r++)
                for (int k = 0; k < sectors; k++)
                {
                    float a = k / (float)sectors * Mathf.PI * 2f, rr = s.r * r / rings;
                    float x = s.c.x + Mathf.Cos(a) * rr, z = s.c.y + Mathf.Sin(a) * rr;
                    v.Add(new Vector3(x, TopY(x, z, s.off), z));
                }
            System.Func<int, int, int> idx = (r, k) => 1 + (r - 1) * sectors + (k % sectors);
            for (int k = 0; k < sectors; k++)
                t.AddRange(new[] { 0, idx(1, k + 1), idx(1, k) });      // clockwise seen from above = front face
            for (int r = 1; r < rings; r++)
                for (int k = 0; k < sectors; k++)
                {
                    int a = idx(r, k), b = idx(r, k + 1), c = idx(r + 1, k), d = idx(r + 1, k + 1);
                    t.AddRange(new[] { a, b, c, c, b, d });
                }
            return MakeRoadObject("Road_Plaza", NewMesh("Plaza", v, t), Mats["RoadConcrete"]);
        }

        static GameObject MakeRoadObject(string name, Mesh mesh, Material m)
        {
            string path = $"Assets/Fairground/Meshes/{name}_{roads.childCount}.asset";
            FairgroundMaterialConverter.EnsureFolder("Assets/Fairground/Meshes");
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(name);
            go.transform.SetParent(roads, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            go.isStatic = true;
            return go;
        }

        static void Curb(Vector2 a, Vector2 b, Road self)
        {
            Vector2 mid = (a + b) * 0.5f;
            foreach (var o in RoadList)
            {
                if (o.Equals(self)) continue;
                if (Sdf(o, mid) < -0.2f) return;                  // a junction: leave the kerb off so the lane stays open
            }
            Vector2 d = b - a; float len = d.magnitude;
            float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Curb";
            go.transform.SetParent(roads, false);
            float hy = H(mid.x, mid.y);
            go.transform.SetPositionAndRotation(new Vector3(mid.x, hy - 0.05f + 0.16f, mid.y), Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = new Vector3(0.3f, 0.32f, len + 0.05f);
            go.GetComponent<Renderer>().sharedMaterial = Mats["Curb"];
            go.isStatic = true;
        }

        static void BuildRoads()
        {
            AddRoadShapes();
            foreach (var s in RoadList)
            {
                if (s.kind == 0) StripObject(s);
                else if (s.kind == 1) DiscObject(s);
                else RectObject(s);

                // kerbs along the outline (not on the outer service roads and the lot: they are worn-out dirt tracks)
                if (s.off <= 0.0351f) continue;
                if (s.kind == 0)
                {
                    Vector2 dir = (s.b - s.a).normalized, left = new Vector2(-dir.y, dir.x);
                    float len = Vector2.Distance(s.a, s.b);
                    for (float u = 0; u < len - 0.1f; u += 4f)
                        foreach (float side in new[] { 1f, -1f })
                        {
                            Vector2 p0 = s.a + dir * u + left * side * (s.w * 0.5f + 0.05f), p1 = s.a + dir * Mathf.Min(u + 4f, len) + left * side * (s.w * 0.5f + 0.05f);
                            Curb(p0, p1, s);
                        }
                }
                else if (s.kind == 1)
                {
                    const int seg = 64;
                    for (int k = 0; k < seg; k++)
                    {
                        float a0 = k / (float)seg * Mathf.PI * 2f, a1 = (k + 1) / (float)seg * Mathf.PI * 2f;
                        Curb(s.c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * (s.r + 0.05f), s.c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * (s.r + 0.05f), s);
                    }
                }
                else
                {
                    Vector2[] corners = { new Vector2(s.x0, s.z0), new Vector2(s.x1, s.z0), new Vector2(s.x1, s.z1), new Vector2(s.x0, s.z1) };
                    for (int c = 0; c < 4; c++)
                    {
                        Vector2 a = corners[c], b = corners[(c + 1) % 4];
                        float len = Vector2.Distance(a, b);
                        for (float u = 0; u < len - 0.1f; u += 2.5f)
                            Curb(Vector2.Lerp(a, b, u / len), Vector2.Lerp(a, b, Mathf.Min(u + 2.5f, len) / len), s);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ water

        static void BuildWater()
        {
            var old = GameObject.Find("FloodWater_Visual");
            if (old != null) Object.DestroyImmediate(old);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Fairground/Meshes/WaterGrid96.asset");
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Fairground/Materials/FG_Water_Flood.mat");
            if (mesh == null || mat == null) { Debug.LogWarning("[FPSKit] water mesh/material missing; run Build Look Test Scene once"); return; }
            var go = new GameObject("FloodWater");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            go.transform.position = Vector3.zero;
            go.transform.localScale = new Vector3(FairgroundBlockout.TerrainHalf * 2f, 1f, FairgroundBlockout.TerrainHalf * 2f);
        }

        // ------------------------------------------------------------------ terrain paint and treeline

        static TerrainLayer Layer(string name, Texture2D d, Texture2D n, float tile)
        {
            string path = $"Assets/Fairground/Terrain/{name}.terrainlayer";
            var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (l == null) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, path); }
            l.diffuseTexture = d; l.normalMapTexture = n; l.tileSize = new Vector2(tile, tile); l.normalScale = 1f; l.smoothness = 0f;
            EditorUtility.SetDirty(l);
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

        static void PaintTerrainAndTrees()
        {
            var terr = Terrain.activeTerrain;
            string src = AssetDatabase.GetAssetPath(terr.terrainData), dst = "Assets/Fairground/Terrain/Step2Terrain.asset";
            AssetDatabase.DeleteAsset(dst);
            AssetDatabase.CopyAsset(src, dst);                       // the blockout keeps its own terrain
            var td = AssetDatabase.LoadAssetAtPath<TerrainData>(dst);
            terr.terrainData = td;
            terr.GetComponent<TerrainCollider>().terrainData = td;

            const string pack = "Assets/Flooded_Grounds/Content/Textures";
            td.terrainLayers = new[]
            {
                Layer("D_DeadGround", FindTex(pack, "GR_Dirt1_AS"), FindTex(pack, "GR_Dirt1_N"), 9f),
                Layer("D_DeadGrass", FindTex(pack, "GR_Moss1_AS"), FindTex(pack, "GR_Moss1_N"), 9f),
                Layer("D_Asphalt", FindTex(TP + "/Asphalt012", "Asphalt012_2K-JPG_Color"), FindTex(TP + "/Asphalt012", "Asphalt012_2K-JPG_NormalGL"), 5f),
                Layer("D_WetMud", FindTex(TP + "/brown_mud_leaves_01", "brown_mud_leaves_01_diff_2k"), FindTex(TP + "/brown_mud_leaves_01", "brown_mud_leaves_01_nor_gl_2k"), 5f),
                Layer("D_Concrete", FindTex(TP + "/concrete_wall_006", "concrete_wall_006_diff_2k"), FindTex(TP + "/concrete_wall_006", "concrete_wall_006_nor_gl_2k"), 4f),
            };

            int ar = td.alphamapResolution;
            var a = new float[ar, ar, 5];
            float half = td.size.x * 0.5f, cell = td.size.x / (ar - 1);
            var bigTop = FairgroundBlockout.BigTopCentre;
            for (int iz = 0; iz < ar; iz++)
                for (int ix = 0; ix < ar; ix++)
                {
                    float x = -half + ix * cell, z = -half + iz * cell;
                    var p = new Vector2(x, z);
                    float n1 = Mathf.PerlinNoise(x * 0.07f + 3f, z * 0.07f + 9f), n2 = Mathf.PerlinNoise(x * 0.21f, z * 0.21f + 5f);
                    float low = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, -0.15f, H(x, z)));
                    float mud = Mathf.Clamp01(low + (Vector2.Distance(p, bigTop) < 20f ? 0.9f : 0f) + (n2 > 0.7f ? 0.35f : 0f) + (x > -86 && x < -30 && z > 135 && z < 170 ? 0.25f * n2 : 0f));
                    float dAsph = SdfAll(p, false), dConc = SdfAll(p, true);
                    // mud washes over the asphalt edges near the flood
                    float edgeBleed = Mathf.Clamp01(Mathf.InverseLerp(0.4f, 0.9f, Mathf.PerlinNoise(x * 0.3f, z * 0.3f))) * Mathf.Clamp01(low * 3f + 0.1f);
                    float wA = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.2f, -0.6f, dAsph)) * (1f - edgeBleed * 0.6f);
                    float wC = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.2f, -0.6f, dConc));
                    if (FairgroundBlockout.InOldGrowth(x, z)) mud = Mathf.Min(mud, 0.08f + 0.2f * n2);              // forest floor: moss and leaf litter, not mud
                    float grass = Mathf.Clamp01(Mathf.InverseLerp(0.45f, 0.65f, n1) + (FairgroundBlockout.InOldGrowth(x, z) ? 0.7f : 0f)) * (1f - mud);
                    float ground = (1f - grass) * (1f - mud);
                    float[] w = { ground, grass, 0f, mud, 0f };
                    for (int k = 0; k < 5; k++) w[k] *= (1f - wA) * (1f - wC);
                    w[2] = wA * (1f - wC); w[4] = wC;
                    float sum = w.Sum() + 1e-4f;
                    for (int k = 0; k < 5; k++) a[iz, ix, k] = w[k] / sum;
                }
            td.SetAlphamaps(0, 0, a);

            // Dark treeline on the embankment behind the walls: Flooded Grounds trees, converted to URP Lit.
            string[] trees = { "TreeCreator_Tall_C", "TreeCreator_Tall_A", "TreeCreator_Small_B", "TreeCreator_Tall_C_Dead", "TreeCreator_Bush_A" };
            td.treePrototypes = trees.Select(n => new TreePrototype { prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Fairground/Prefabs/Trees/{n}.prefab") }).ToArray();
            var inst = new List<TreeInstance>();
            // Abandoned ground: a few groves that have taken the open ground (noise-clustered, strays between), and a treeline beyond
            // the walls. Kept light on purpose (about 360 in all, plus the giants) so phones and WebGL can draw it.
            var placed = new List<Vector2>();
            Physics.SyncTransforms();
            for (int i = 0; i < 30000 && inst.Count < 360; i++)
            {
                float x = R(-250f, 250f), z = R(-250f, 250f);
                if (FairgroundBlockout.InOldGrowth(x, z)) continue;                                       // the wood has its own, much bigger, trees
                float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                float d = edge - (FairgroundBlockout.Half + 1f);
                bool inside = d < 0f;
                var q = new Vector2(x, z);
                if (inside)
                {
                    if (edge > FairgroundBlockout.Half - 6f) continue;                                   // keep off the walls
                    float grove = Mathf.PerlinNoise(x * 0.016f + 70f, z * 0.016f + 12f);
                    if (grove < 0.55f && rng.NextDouble() > 0.03) continue;
                    if (SdfAll(q) < 5f) continue;                                                         // off the roads and plazas
                    if (Physics.OverlapSphere(new Vector3(x, H(x, z) + 2f, z), 4f, ~0, QueryTriggerInteraction.Ignore).Any(c => !(c is TerrainCollider))) continue;   // clear of anything built
                }
                else if (d < 4f) continue;                                                                // clear of the walls
                float y = H(x, z);
                if (y < (inside ? 0.0f : 1.5f)) continue;                                                 // not in the flood, nor its pit
                if (placed.Any(o => (o - q).sqrMagnitude < 25f)) continue;                                // trunks 5 m apart
                placed.Add(q);
                int proto = d > 12f || inside ? rng.Next(0, 5) : (rng.NextDouble() < 0.5 ? 4 : 2);
                inst.Add(new TreeInstance
                {
                    prototypeIndex = proto,
                    position = new Vector3((x + half) / td.size.x, (y - (-6f)) / td.size.y, (z + half) / td.size.z),
                    widthScale = R(0.8f, 1.3f), heightScale = R(0.8f, 1.3f), rotation = R(0f, 6.28f),
                    color = Color.white, lightmapColor = Color.white
                });
            }
            td.treeInstances = inst.ToArray();
            terr.treeDistance = 170f;
            terr.heightmapPixelError = 8f;                                   // the 510 m terrain is mostly gentle: fewer triangles
            terr.basemapDistance = 120f;
            terr.treeBillboardDistance = 2000f;                              // URP cannot draw the pack's billboards
            terr.treeCrossFadeLength = 0f;
            terr.treeMaximumFullLODCount = 120;
        }

        // ------------------------------------------------------------------ old growth

        /// <summary>
        /// The south-east, where the flood was: huge, very thick trees about 20 m apart (a jittered grid, so it is a wood and not an
        /// orchard), each with a real trunk collider so the NavMesh goes round it, with fallen giant logs and mossy boulders between.
        /// </summary>
        static void BuildOldGrowth()
        {
            var g = new GameObject("15_OldGrowth").transform;
            g.SetParent(root, false);
            string[] kinds = { "TreeCreator_Tall_C", "TreeCreator_Tall_A", "TreeCreator_Tall_B", "TreeCreator_Crinkly_A" };
            var prefabs = kinds.Select(n => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Fairground/Prefabs/Trees/{n}.prefab")).Where(p => p != null).ToArray();
            if (prefabs.Length == 0) { Debug.LogWarning("[FPSKit] old growth: no tree prefabs"); return; }

            Physics.SyncTransforms();
            float x0 = 46f, x1 = FairgroundBlockout.Half - 10f, z0 = -(FairgroundBlockout.Half - 10f), z1 = -66f;
            const float Spacing = 20f;
            int trees = 0, logs = 0, rocks = 0;
            var spots = new List<Vector2>();
            for (float gx = x0; gx <= x1; gx += Spacing)
                for (float gz = z0; gz <= z1; gz += Spacing)
                {
                    float x = gx + R(-3.5f, 3.5f), z = gz + R(-3.5f, 3.5f);
                    var q = new Vector2(x, z);
                    if (SdfAll(q) < 5f) continue;                                                        // off the roads
                    float y = H(x, z);
                    if (Physics.OverlapSphere(new Vector3(x, y + 3f, z), 5f, ~0, QueryTriggerInteraction.Ignore).Any(c => !(c is TerrainCollider))) continue;   // clear of the site's buildings
                    var src = prefabs[rng.Next(prefabs.Length)];
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
                    go.name = "GiantTree";
                    go.transform.SetParent(g, false);
                    float wide = R(3.4f, 4.8f), tall = R(2.6f, 3.4f);                                     // thick: wider than it is tall, against the pack's tree
                    go.transform.SetPositionAndRotation(new Vector3(x, y - 0.3f, z), Quaternion.Euler(0f, R(0f, 360f), 0f));
                    go.transform.localScale = new Vector3(wide, tall, wide);
                    float h0 = 0f;
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null) h0 = Mathf.Max(h0, mf.sharedMesh.bounds.size.y);
                    if (h0 < 2f) h0 = 12f;
                    var cap = go.AddComponent<CapsuleCollider>();
                    cap.direction = 1; cap.radius = Mathf.Clamp(0.03f * h0, 0.28f, 0.7f); cap.height = h0 * 0.8f; cap.center = new Vector3(0f, h0 * 0.4f, 0f);
                    foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
                    spots.Add(q);
                    trees++;
                }

            // Fallen giants: huge trunks lying where they came down, a few metres thick, good cover.
            var bark = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/Bark.mat");
            if (bark == null)
            {
                bark = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                bark.SetColor("_BaseColor", new Color(0.17f, 0.13f, 0.10f)); bark.SetFloat("_Smoothness", 0.12f);
                AssetDatabase.CreateAsset(bark, $"{MatDir}/Bark.mat");
            }
            for (int i = 0; i < 400 && logs < 16 && spots.Count > 0; i++)
            {
                var c = spots[rng.Next(spots.Count)] + new Vector2(R(-7f, 7f), R(-7f, 7f));
                if (!FairgroundBlockout.InOldGrowth(c.x, c.y) || SdfAll(c) < 8f) continue;
                float len = R(12f, 22f), r = R(1.1f, 1.9f), yaw = R(0f, 180f);
                float y = H(c.x, c.y);
                if (Physics.OverlapBox(new Vector3(c.x, y + r, c.y), new Vector3(len * 0.5f, r, r + 0.4f), Quaternion.Euler(0f, yaw, 0f), ~0, QueryTriggerInteraction.Ignore).Any(h => !(h is TerrainCollider))) continue;
                var log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                log.name = "FallenGiant";
                log.transform.SetParent(g, false);
                log.transform.SetPositionAndRotation(new Vector3(c.x, y + r * 0.75f, c.y), Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, 90f));
                log.transform.localScale = new Vector3(r * 2f, len * 0.5f, r * 2f);
                log.GetComponent<Renderer>().sharedMaterial = bark;
                log.isStatic = true;
                var nm = log.AddComponent<Unity.AI.Navigation.NavMeshModifier>();      // the round top is not ground: it would be an island nobody can reach
                nm.overrideArea = true; nm.area = 1;                                    // 1 = Not Walkable
                logs++;
            }
            // Mossy boulders the roots have heaved up.
            for (int i = 0; i < 80 && rocks < 28 && spots.Count > 0; i++)
            {
                var c = spots[rng.Next(spots.Count)] + new Vector2(R(-8f, 8f), R(-8f, 8f));
                if (!FairgroundBlockout.InOldGrowth(c.x, c.y) || SdfAll(c) < 6f) continue;
                float y = H(c.x, c.y);
                if (Physics.OverlapSphere(new Vector3(c.x, y + 2f, c.y), 3.5f, ~0, QueryTriggerInteraction.Ignore).Any(h => !(h is TerrainCollider))) continue;
                var rock = Pack(Pick("CobbleRock_A", "CobbleRock_B", "CobbleRock_C"));
                if (rock == null) break;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(rock);
                FairgroundMaterialConverter.Remap(go);
                go.name = "MossBoulder";
                go.transform.SetParent(g, false);
                float s = R(2.6f, 5f);
                go.transform.SetPositionAndRotation(new Vector3(c.x, y - s * 0.12f, c.y), Quaternion.Euler(R(-8f, 8f), R(0f, 360f), R(-8f, 8f)));
                go.transform.localScale = Vector3.one * s;
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
                rocks++;
            }
            Debug.Log($"[FPSKit] Old growth: {trees} giant trees, {logs} fallen giants, {rocks} boulders");
        }

        // ------------------------------------------------------------------ night

        static void SetUpNight(bool standalone)
        {
            var sun = RenderSettings.sun;
            sun.color = new Color(1.0f, 0.74f, 0.58f);                  // dusk: a low warm sun (the lighting step sets the same)
            sun.intensity = 0.95f;
            sun.transform.rotation = Quaternion.Euler(22f, 208f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.26f, 0.33f, 0.48f);
            RenderSettings.ambientEquatorColor = new Color(0.19f, 0.24f, 0.35f);
            RenderSettings.ambientGroundColor = new Color(0.09f, 0.09f, 0.11f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.040f, 0.052f, 0.064f);
            RenderSettings.fogStartDistance = 20f;
            RenderSettings.fogEndDistance = 220f;
            var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Fairground/Materials/Sky_QwantaniNight.mat");
            if (sky != null) { sky.SetFloat("_Exposure", 0.12f); RenderSettings.skybox = sky; }
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.45f;

            if (!standalone) return;                           // the game's scene builder adds its own Volume and camera
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Fairground/Settings/FG_Night.asset") ?? FairgroundVolume.Night();
            var vg = new GameObject("GlobalVolume");
            var vol = vg.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
            var cam = GameObject.Find("BlockoutCamera");
            if (cam != null) cam.GetComponent<Camera>().clearFlags = CameraClearFlags.Skybox;
        }
    }
}
#endif
