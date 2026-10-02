#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Step 1 of the Abandoned Fairground rebuild: a gameplay-first gray blockout, generated from code
    /// (so it is repeatable and the later steps dress the same layout).
    ///
    /// Coordinates: x east, z north, y up, origin at the middle of the 180 x 180 m playable square.
    /// The player spawns on the high ground of the Main Gate in the north and fights south toward the
    /// Big Top. Everything is a primitive in a shade of gray; ride-sized placeholders (Ferris wheel
    /// ~36 m, carousel 15 m, Big Top 38 m) are sized so the real models can replace them later.
    ///
    /// Rules the layout is built to (and measured against at the end of Build):
    ///   three main lanes with cross paths, cover every 8-12 m along them, no empty 10 m cell,
    ///   sightlines held near 60 m, at least two elevated positions, every boundary is a real wall.
    /// Slopes on playable ground stay under 26 degrees (a vehicle is planned).
    /// </summary>
    public static class FairgroundBlockout
    {
        public const string ScenePath = "Assets/Fairground/Scenes/AbandonedFairground_Blockout.unity";
        public const float Half = 225f;               // 450 m square (was 180). The old fair is the dense core; the band outside it is abandoned open ground
        public const float Core = 175f;               // the stretched fair lives inside this square
        public const float Stretch = 2f;              // the sites sit twice as far apart as they were planned
        public const float Eye = 1.7f;
        public const float TerrainHalf = 255f;
        const float TerrainHeight = 40f, TerrainBase = -6f;
        const int HeightRes = 513;                     // 1 m cells: enough for swell, and half the memory of 1025

        // Planned positions (the sites are built here), and where the stretch puts them.
        static readonly Vector2 BigTopOld = new Vector2(-10f, -68f), CarouselOld = new Vector2(0f, -12f), FerrisOld = new Vector2(52f, -14f);
        public static Vector2 BigTopCentre => stretched ? Warp(BigTopOld) : BigTopOld;
        public static Vector2 CarouselCentre => stretched ? Warp(CarouselOld) : CarouselOld;
        public static Vector2 FerrisCentre => stretched ? Warp(FerrisOld) : FerrisOld;

        // ------------------------------------------------------------------ the stretch
        //
        // The fair was planned inside 180 m. It is spread over 350 m by moving each SITE rigidly to twice its distance from the
        // centre (a site keeps its size and its insides), while the ground, the flood, the roads and the lanes between sites
        // stretch continuously. Sites are built in planned coordinates with `stretched` off; then they move and it turns on.

        static bool stretched;

        static readonly Vector2[] SiteAnchors =
        {
            new Vector2(0, 75), new Vector2(0, 30), CarouselOld, FerrisOld, new Vector2(54, -58), new Vector2(-62, -10), new Vector2(66, 32), BigTopOld
        };

        static float WarpAxis(float a) { float m = Mathf.Abs(a); return Mathf.Sign(a) * (m <= 90f ? m * Stretch : 90f * Stretch + (m - 90f)); }
        static float UnwarpAxis(float a) { float m = Mathf.Abs(a); return Mathf.Sign(a) * (m <= 90f * Stretch ? m / Stretch : 90f + (m - 90f * Stretch)); }

        /// <summary>Planned position to stretched position, continuous: for ground, roads and lanes.</summary>
        public static Vector2 Warp(Vector2 p) => new Vector2(WarpAxis(p.x), WarpAxis(p.y));
        static Vector2 Unwarp(Vector2 p) => new Vector2(UnwarpAxis(p.x), UnwarpAxis(p.y));

        /// <summary>Which site a planned position belongs to: inside a site's own footprint first, else the nearest site.</summary>
        static int SiteOf(Vector2 p)
        {
            if (Vector2.Distance(p, CarouselOld) < 27f) return 2;
            if (Vector2.Distance(p, BigTopOld) < 28f) return 7;
            if (Vector2.Distance(p, FerrisOld) < 27f) return 3;
            if (InRect(p.x, p.y, 18, 94, -95, -28)) return 4;       // lowlands
            if (InRect(p.x, p.y, -92, -30, -64, 42)) return 5;      // coaster
            if (InRect(p.x, p.y, 44, 92, 8, 56)) return 6;          // backlot
            if (InRect(p.x, p.y, -92, 92, 56, 92)) return 0;        // gate, its plaza and the car park
            if (InRect(p.x, p.y, -16, 16, 4, 56)) return 1;         // midway
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < SiteAnchors.Length; i++) { float d = (SiteAnchors[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        /// <summary>Planned position to where the SITE it belongs to went: a point beside a booth stays beside the booth.</summary>
        public static Vector2 Shift(Vector2 p)
        {
            var a = SiteAnchors[SiteOf(p)];
            return p + (Warp(a) - a);
        }

        /// <summary>Ground height at a planned position, before the stretch.</summary>
        public static float HPlanned(float x, float z) { bool s = stretched; stretched = false; float h = H(x, z); stretched = s; return h; }

        static readonly (string group, int site)[] SiteGroups =
        {
            ("1_MainGate", 0), ("2_Midway", 1), ("3_CarouselPlaza", 2), ("4_FerrisWheel", 3),
            ("5_FloodedLowlands", 4), ("6_RollerCoaster", 5), ("7_BacklotServiceYard", 6), ("8_BigTop", 7)
        };

        /// <summary>Groups that are built in final coordinates, or belong to no site.</summary>
        static readonly HashSet<string> NotMoved = new HashSet<string> { "Terrain", "FloodWater", "FloodWater_Visual", "11_Roads", "0_Boundary", "A_Outskirts", "Zones" };

        /// <summary>
        /// The last step. Everything built so far (sites, dressing, decals, lights) is in planned coordinates: each site's group moves
        /// rigidly to its stretched place and everything else moves with the site it belongs to. Then the ground switches to the
        /// stretched heightfield and the things that are not part of a site are built: terrain, walls, outskirts, zones, cover.
        /// </summary>
        public static void FinalStretch()
        {
            if (root == null) { Debug.LogError("[FPSKit] FinalStretch: no blockout root."); return; }
            bool oldSync = Physics.autoSyncTransforms;
            Physics.autoSyncTransforms = true;
            var moves = new List<(Transform t, Vector2 to, float oldH)>();
            foreach (var sg in SiteGroups)
            {
                var t = root.Find(sg.group);
                if (t == null) continue;
                var a = SiteAnchors[sg.site];
                moves.Add((t, Warp(a), H(a.x, a.y)));              // the group sits at the origin: its offset is the anchor's
            }
            var siteNames = new HashSet<string>(SiteGroups.Select(g => g.group));
            foreach (Transform group in root)
            {
                if (siteNames.Contains(group.name) || NotMoved.Contains(group.name)) continue;
                foreach (Transform c in group)
                {
                    Vector3 pos = c.position;
                    Vector2 to;
                    float m = 88.9f, tol = 0.6f;
                    if (Mathf.Abs(Mathf.Abs(pos.x) - m) < tol) to = new Vector2(Mathf.Sign(pos.x) * (Half - 1.1f), WarpAxis(pos.z));          // a mark on the old perimeter wall
                    else if (Mathf.Abs(Mathf.Abs(pos.z) - m) < tol) to = new Vector2(WarpAxis(pos.x), Mathf.Sign(pos.z) * (Half - 1.1f));
                    else to = Shift(new Vector2(pos.x, pos.z));
                    moves.Add((c, to, HPlanned(pos.x, pos.z)));
                }
            }
            for (int i = 0; i < Elevated.Count; i++) Elevated[i] = (Elevated[i].name, Shift(Elevated[i].xz), Elevated[i].expectAbove);

            stretched = true;
            foreach (var mv in moves)
            {
                var t = mv.t;
                bool isGroup = siteNames.Contains(t.name) && t.parent == root;
                if (isGroup)
                {
                    int site = SiteGroups.First(g => g.group == t.name).site;
                    Vector2 a = SiteAnchors[site], d = mv.to - a;
                    t.position += new Vector3(d.x, H(mv.to.x, mv.to.y) - mv.oldH, d.y);
                }
                else
                {
                    Vector3 p = t.position;
                    t.position = new Vector3(mv.to.x, p.y + H(mv.to.x, mv.to.y) - mv.oldH, mv.to.y);
                }
            }
            Physics.SyncTransforms();

            BuildTerrain();
            Boundary();
            Outskirts();
            Zones();
            Physics.SyncTransforms();
            ScatterCover();
            FillEmptyCells();
            string sight = FixSightlines();
            sight += "\n" + CoverGapReport() + "\n" + ElevatedReport();
            LastReport = $"cover pieces {coverPlaced}, empty cells filled {cellsFilled}, sightline blockers {blockersAdded}\n{sight}";
            Physics.autoSyncTransforms = oldSync;
            Debug.Log("[FPSKit] Fairground stretched to " + (Half * 2f) + " m: " + LastReport);
        }

        public static string LastReport = "";

        static System.Random rng;
        static Transform root;
        static Material mStruct, mCover, mLight, mDark, mWalk, mWater, mCar;
        static int coverPlaced, cellsFilled, blockersAdded;
        static readonly List<(string name, Vector2 xz, float expectAbove)> Elevated = new List<(string, Vector2, float)>();

        // ------------------------------------------------------------------ ground height

        static float Smooth(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));
        /// <summary>The flood is gone (Ishaan did not like the water): the south-east is old-growth forest on dry ground, so nothing sinks.</summary>
        static float LowMask(float x, float z) => 0f;

        /// <summary>The old-growth wood that fills the south-east quarter, in final coordinates.</summary>
        public static bool InOldGrowth(float x, float z) => x > 40f && x < Half - 8f && z < -60f && z > -(Half - 8f);
        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        static float HInner(float x, float z)
        {
            float h = 3f * Smooth(46f, 58f, z);                                   // gate plateau, 14 deg at its steepest
            float n = Mathf.PerlinNoise(x * 0.045f + 11.3f, z * 0.045f + 4.7f);
            h -= LowMask(x, z) * (0.55f + 0.14f * (n - 0.5f));                      // flooded: ground sits ~0.5 m under the water
            return h;
        }

        /// <summary>Ground without the embankment: the planned ground read through the stretch, plus the meadow's swell.</summary>
        static float Inner(float x, float z)
        {
            Vector2 u = stretched ? Unwarp(new Vector2(x, z)) : new Vector2(x, z);
            float h = HInner(u.x, u.y);
            // The meadow band: gentle swell, ~2 m over ~80 m (about 4 degrees), fading in beyond the fair so the planned ground stays flat.
            float band = stretched ? Smooth(Core, Core + 25f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z))) : 0f;
            if (band > 0f)
                h += band * (1f - LowMask(u.x, u.y)) * (3.2f * (Mathf.PerlinNoise(x * 0.012f + 40f, z * 0.012f + 7f) - 0.5f) + 0.8f * (Mathf.PerlinNoise(x * 0.04f + 5f, z * 0.04f + 90f) - 0.5f));
            return h;
        }

        /// <summary>Ground height. Always ask this, never a hard-coded y.</summary>
        public static float H(float x, float z)
        {
            float px = Mathf.Clamp(x, -(Half + 1f), Half + 1f), pz = Mathf.Clamp(z, -(Half + 1f), Half + 1f);
            float inner = Inner(px, pz);
            float d = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - (Half + 1f);
            if (d <= 0f) return inner;
            Vector2 u = stretched ? Unwarp(new Vector2(px, pz)) : new Vector2(px, pz);
            float flood = LowMask(u.x, u.y);
            float bank = inner + Mathf.Min(d, 24f) + 2.5f * Mathf.PerlinNoise(x * 0.06f, z * 0.06f) * Smooth(0f, 10f, d);   // steep embankment
            float pit = Mathf.Lerp(inner, -3.5f, Smooth(0f, 5f, d)) + Mathf.Max(0f, d - 30f);                               // deep floodwater
            return Mathf.Lerp(bank, pit, flood);
        }

        static bool InRect(float x, float z, float x0, float x1, float z0, float z1) => x >= x0 && x <= x1 && z >= z0 && z <= z1;

        /// <summary>Grey paint for the standalone blockout only: the dress step repaints the ground from its own road list.</summary>
        static bool Paved(float x, float z)
        {
            if (stretched) { var u = Unwarp(new Vector2(x, z)); x = u.x; z = u.y; }
            return InRect(x, z, -32, 32, 56, 78) || InRect(x, z, -12, 12, 6, 56)
                || Vector2.Distance(new Vector2(x, z), CarouselOld) < 19f
                || InRect(x, z, -14, -2, -50, -28) || InRect(x, z, -52, 52, 28, 34) || InRect(x, z, -50, 50, -16, -8)
                || InRect(x, z, -48, -42, -48, 56) || InRect(x, z, 35, 41, -26, 56)
                || Vector2.Distance(new Vector2(x, z), FerrisOld) < 12f || InRect(x, z, 46, 86, 12, 52);
        }

        static bool Mud(float x, float z) => MudPlanned(stretched ? Unwarp(new Vector2(x, z)) : new Vector2(x, z));

        static bool MudPlanned(Vector2 p) => MudAt(p.x, p.y);

        static bool MudAt(float x, float z) =>
            LowMask(x, z) > 0.3f || Vector2.Distance(new Vector2(x, z), BigTopOld) < 20f || InRect(x, z, -86, -30, 60, 90);

        // ------------------------------------------------------------------ build

        [MenuItem("Tools/MiniFPS/Fairground/Build Blockout")]
        public static void Build() => Run(true);

        /// <summary>Generates into the OPEN scene: no new scene, no lighting rig, no camera, no save. For the game's scene builder.</summary>
        public static void Generate() => Run(false);

        static void Run(bool standalone)
        {
            FairgroundMaterialConverter.EnsureFolder("Assets/Fairground/Scenes");
            FairgroundMaterialConverter.EnsureFolder("Assets/Fairground/Terrain");
            FairgroundMaterialConverter.EnsureFolder("Assets/Fairground/Materials/Blockout");

            var scene = standalone ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single) : default;
            rng = new System.Random(1962);
            coverPlaced = cellsFilled = blockersAdded = 0;
            Elevated.Clear();
            bool oldSync = Physics.autoSyncTransforms;
            Physics.autoSyncTransforms = true;

            root = new GameObject("Blockout").transform;
            mStruct = Mat("Gray_Structure", 0.55f);
            mCover = Mat("Gray_Cover", 0.40f);
            mLight = Mat("Gray_Light", 0.74f);
            mDark = Mat("Gray_Dark", 0.24f);
            mCar = Mat("Gray_Car", 0.33f);
            mWalk = Mat("Gray_Boardwalk", new Color(0.46f, 0.42f, 0.38f));
            mWater = Mat("Gray_Water", new Color(0.26f, 0.36f, 0.45f), 0.7f);

            if (standalone) SetUpLighting();
            stretched = false;
            Gate();
            Midway();
            Carousel();
            Ferris();
            Lowlands();
            Coaster();
            Backlot();
            BigTop();
            Physics.SyncTransforms();
            if (standalone) FinalStretch();     // the game's builder calls it later, once the dressing and lights are placed

            if (standalone)
            {
                var cam = new GameObject("BlockoutCamera");
                cam.tag = "MainCamera";
                cam.AddComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
                cam.AddComponent<UniversalAdditionalCameraData>();
                var spawn = GameObject.Find("PlayerSpawn");
                Vector3 sp = spawn != null ? spawn.transform.position : new Vector3(0f, H(0, 87), 87f);
                cam.transform.SetPositionAndRotation(sp + Vector3.up * Eye, Quaternion.Euler(4f, 180f, 0f));
            }

            Physics.autoSyncTransforms = oldSync;
            if (standalone)
            {
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
            }

            Debug.Log("[FPSKit] Fairground blockout built: " + ScenePath + "\n" + LastReport);
        }

        // ------------------------------------------------------------------ materials, light

        static Material Mat(string name, float grey, float smooth = 0.12f) => Mat(name, new Color(grey, grey, grey), smooth);

        static Material Mat(string name, Color c, float smooth = 0.12f)
        {
            string p = $"Assets/Fairground/Materials/Blockout/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void SetUpLighting()
        {
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.98f, 0.95f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.60f);
            RenderSettings.fog = false;
        }

        // ------------------------------------------------------------------ primitives

        static Transform G(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            return go.transform;
        }

        static Vector3 P(float x, float z, float yOff = 0f) => new Vector3(x, H(x, z) + yOff, z);

        /// <summary>Axis box. y0 is the bottom (NaN = sit on the ground); yaw about y.</summary>
        static GameObject Box(Transform parent, string name, float cx, float cz, float y0, float sx, float sy, float sz, float yaw, Material m)
        {
            if (float.IsNaN(y0)) y0 = H(cx, cz) - 0.05f;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(new Vector3(cx, y0 + sy * 0.5f, cz), Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = new Vector3(sx, sy, sz);
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.isStatic = true;
            return go;
        }

        static GameObject Cyl(Transform parent, string name, float cx, float cz, float y0, float radius, float height, Material m)
        {
            if (float.IsNaN(y0)) y0 = H(cx, cz) - 0.05f;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(cx, y0 + height * 0.5f, cz);
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.isStatic = true;
            return go;
        }

        /// <summary>A box along a to b (local z). upHint picks which way the cross-section's y faces.</summary>
        static GameObject Beam(Transform parent, string name, Vector3 a, Vector3 b, float sx, float sy, Material m, Vector3? upHint = null)
        {
            Vector3 d = b - a;
            Vector3 up = upHint ?? (Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(d, up));
            go.transform.localScale = new Vector3(sx, sy, d.magnitude);
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.isStatic = true;
            return go;
        }

        /// <summary>Child box of a composite prop. pos is the centre, in the parent's space.</summary>
        static GameObject Child(Transform parent, string name, Vector3 localCentre, Vector3 size, Material m, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCentre;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = m;
            go.isStatic = true;
            return go;
        }

        static Transform Prop(Transform parent, string name, float x, float z, float yaw, float y0 = float.NaN)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(new Vector3(x, float.IsNaN(y0) ? H(x, z) - 0.05f : y0, z), Quaternion.Euler(0f, yaw, 0f));
            go.isStatic = true;
            return go.transform;
        }

        /// <summary>Straight wall or fence from a to b, in chunks of at most 6 m, with gaps given as (from,to) metres along it.</summary>
        static void Wall(Transform parent, string name, Vector2 a, Vector2 b, float height, float thick, Material m, params Vector2[] gaps)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            Vector2 dir = d / len;
            float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            var sorted = gaps.OrderBy(g => g.x).Concat(new[] { new Vector2(len, len) }).ToList();
            float cur = 0f;
            foreach (var g in sorted)
            {
                float end = Mathf.Min(g.x, len);
                for (float s = cur; s < end - 0.05f; s += 6f)
                {
                    float e = Mathf.Min(s + 6f, end);
                    Vector2 p0 = a + dir * s, p1 = a + dir * e, c = (p0 + p1) * 0.5f;
                    float h0 = H(p0.x, p0.y), h1 = H(p1.x, p1.y), hc = H(c.x, c.y);
                    float lo = Mathf.Min(h0, Mathf.Min(h1, hc)) - 0.25f, hi = Mathf.Max(h0, Mathf.Max(h1, hc));
                    Box(parent, name, c.x, c.y, lo, thick, hi - lo + height, e - s, yaw, m);
                }
                cur = Mathf.Max(cur, g.y);
            }
        }

        static void Stairs(Transform parent, string name, Vector3 edgeTop, Vector2 dirXZ, float width, float rise, Material m)
        {
            // dirXZ points UP the stairs; the top step ends at edgeTop.
            int n = Mathf.Max(1, Mathf.CeilToInt(rise / 0.22f));
            float step = rise / n, tread = 0.55f, len = n * tread;
            Vector2 dir = dirXZ.normalized;
            Vector2 baseXZ = new Vector2(edgeTop.x, edgeTop.z) - dir * len;
            float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            float y0 = edgeTop.y - rise;
            for (int i = 0; i < n; i++)
            {
                Vector2 c = baseXZ + dir * ((i + 0.5f) * tread);
                Box(parent, name, c.x, c.y, y0, width, (i + 1) * step, tread, yaw, m);
            }
        }

        // ------------------------------------------------------------------ terrain, water

        static Texture2D GridTexture(string name, float grey)
        {
            string path = $"Assets/Fairground/Terrain/{name}.png";
            var tex = new Texture2D(128, 128, TextureFormat.RGB24, false);
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    float n = 0.97f + 0.06f * Mathf.PerlinNoise(x * 0.09f, y * 0.09f);
                    float line = (x < 2 || y < 2) ? 0.78f : 1f;          // one line per 10 m tile: a scale ruler
                    float v = grey * n * line;
                    tex.SetPixel(x, y, new Color(v, v, v));
                }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static TerrainLayer Layer(string name, float grey)
        {
            string path = $"Assets/Fairground/Terrain/{name}.terrainlayer";
            var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (l == null) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, path); }
            l.diffuseTexture = GridTexture(name + "_Tex", grey);
            l.tileSize = new Vector2(10f, 10f);
            l.smoothness = 0f;
            EditorUtility.SetDirty(l);
            return l;
        }

        static void BuildTerrain()
        {
            var td = new TerrainData { heightmapResolution = HeightRes, size = new Vector3(TerrainHalf * 2f, TerrainHeight, TerrainHalf * 2f) };
            var h = new float[HeightRes, HeightRes];
            float cell = TerrainHalf * 2f / (HeightRes - 1);
            for (int iz = 0; iz < HeightRes; iz++)
                for (int ix = 0; ix < HeightRes; ix++)
                    h[iz, ix] = (H(-TerrainHalf + ix * cell, -TerrainHalf + iz * cell) - TerrainBase) / TerrainHeight;
            td.SetHeights(0, 0, h);

            td.terrainLayers = new[] { Layer("BO_Ground", 0.42f), Layer("BO_Paving", 0.64f), Layer("BO_Mud", 0.27f) };
            int ar = td.alphamapResolution;
            var a = new float[ar, ar, 3];
            float acell = TerrainHalf * 2f / (ar - 1);
            for (int iz = 0; iz < ar; iz++)
                for (int ix = 0; ix < ar; ix++)
                {
                    float x = -TerrainHalf + ix * acell, z = -TerrainHalf + iz * acell;
                    int k = Mud(x, z) ? 2 : (Paved(x, z) ? 1 : 0);
                    a[iz, ix, k] = 1f;
                }
            td.SetAlphamaps(0, 0, a);

            string path = "Assets/Fairground/Terrain/BlockoutTerrain.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(td, path);
            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain";
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(-TerrainHalf, TerrainBase, -TerrainHalf);
            go.isStatic = true;
            go.GetComponent<Terrain>().drawInstanced = true;
        }

        static void BuildWaterPlane()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "FloodWater_Visual";
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(0f, 0f, 0f);
            go.transform.localScale = new Vector3(TerrainHalf * 2f / 10f, 1f, TerrainHalf * 2f / 10f);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = mWater;
            go.isStatic = true;
        }

        // ------------------------------------------------------------------ boundary

        static void Boundary()
        {
            var g = G("0_Boundary");
            // Every edge is a real wall (5 m: wall plus fence), with the terrain climbing or dropping to deep water behind it.
            Wall(g, "BoundaryWall_N", new Vector2(-Half, Half), new Vector2(Half, Half), 5f, 0.8f, mDark);
            Wall(g, "BoundaryWall_S", new Vector2(-Half, -Half), new Vector2(Half, -Half), 5f, 0.8f, mDark);
            Wall(g, "BoundaryWall_W", new Vector2(-Half, -Half), new Vector2(-Half, Half), 5f, 0.8f, mDark);
            Wall(g, "BoundaryWall_E", new Vector2(Half, -Half), new Vector2(Half, Half), 5f, 0.8f, mDark);
        }

        // ------------------------------------------------------------------ 1 main gate

        static void Car(Transform parent, float x, float z, float yaw)
        {
            var c = Prop(parent, "Car", x, z, yaw);
            Child(c, "Body", new Vector3(0, 0.25f + 0.6f, 0), new Vector3(1.9f, 1.2f, 4.4f), mCar);
            Child(c, "Cabin", new Vector3(0, 0.25f + 1.2f + 0.4f, -0.2f), new Vector3(1.7f, 0.8f, 2.3f), mCar);
        }

        static void Gate()
        {
            var g = G("1_MainGate");
            var sp = new GameObject("PlayerSpawn");
            sp.transform.SetParent(g, false);
            sp.transform.SetPositionAndRotation(P(0, 87, 0.1f), Quaternion.Euler(0, 180, 0));
            Elevated.Add(("Main Gate plateau", new Vector2(0, 84f), 3f));

            // Broken arch sign: two pylons, a beam, letters hanging or fallen.
            float gy = H(0, 82);
            Box(g, "ArchPylon_W", -9, 82, float.NaN, 2, 10, 2, 0, mStruct);
            Box(g, "ArchPylon_E", 9, 82, float.NaN, 2, 10, 2, 0, mStruct);
            Box(g, "ArchBeam", 0, 82, gy + 8f, 22, 2.4f, 1.4f, 0, mStruct);
            for (int i = 0; i < 7; i++)
            {
                float lx = -7.5f + 2.5f * i;
                if (i == 2 || i == 5) Box(g, "FallenLetter", lx + R(-1, 1), 80.5f + R(0, 2), float.NaN, 1.3f, 0.3f, 1.8f, R(0, 180), mLight);
                else Box(g, "HangingLetter", lx, 81.6f, gy + 5.9f - (i == 3 ? 0.9f : 0f), 1.2f, 1.7f, 0.3f, 0, mLight);
            }

            for (int i = 0; i < 4; i++)
            {
                float bx = new[] { -15f, -7f, 7f, 15f }[i];
                var b = Prop(g, "TicketBooth", bx, 74, 0);
                Child(b, "Box", new Vector3(0, 1.6f, 0), new Vector3(3, 3.2f, 3), mStruct);
                Child(b, "Counter", new Vector3(0, 0.55f, 1.7f), new Vector3(2.4f, 1.1f, 0.6f), mCover);
            }
            for (float x = -14f; x <= 14f; x += 3.5f)
                if (Mathf.Abs(x) > 3f) Box(g, "Turnstile", x, 69, float.NaN, 0.9f, 1.1f, 1.0f, 0, mCover);

            // Chained gate: a fence line with a half-shut central gate and two broken gaps.
            Wall(g, "GateFence", new Vector2(-62, 63), new Vector2(62, 63), 2.3f, 0.25f, mDark,
                 new Vector2(58f, 66f), new Vector2(30f, 34f), new Vector2(90f, 94f));
            Box(g, "GateLeaf_W", -2.4f, 63, float.NaN, 3f, 2.4f, 0.2f, 40, mStruct);
            Box(g, "GateLeaf_E", 2.4f, 63, float.NaN, 3f, 2.4f, 0.2f, -40, mStruct);

            // Cracked, muddy parking lot with abandoned cars (cover).
            for (float x = -82f; x <= -34f; x += 10f)
                for (float z = 67f; z <= 88f; z += 8f)
                    if (rng.NextDouble() > 0.3) Car(g, x + R(-1.5f, 1.5f), z + R(-1.2f, 1.2f), (rng.NextDouble() < 0.5 ? 0f : 90f) + R(-12, 12));

            // Queue barriers and a ticket-machine row on the east side of the plateau.
            for (int i = 0; i < 6; i++)
                Box(g, "QueueBarrier", 14f + i * 5.5f, 71f + (i % 2) * 5f, float.NaN, 4f, 1.0f, 0.4f, i % 2 == 0 ? 0f : 90f, mCover);
        }

        // ------------------------------------------------------------------ 2 the midway

        static void Stall(Transform parent, float x, float z, int side, float width, bool wrecked)
        {
            var s = Prop(parent, "Stall", x, z, side < 0 ? 90f : -90f);        // local +z faces the avenue
            Child(s, "Counter", new Vector3(0, 0.55f, 2.2f), new Vector3(width, 1.1f, 1.0f), mCover);
            Child(s, "BackWall", new Vector3(0, 1.6f, -2.4f), new Vector3(width, 3.2f, 0.4f), mStruct);
            if (rng.NextDouble() < 0.6) Child(s, "SideWall", new Vector3(width * 0.5f, 1.5f, -0.2f), new Vector3(0.3f, 3f, 4.6f), mStruct);
            Child(s, "Roof", new Vector3(0, 3.15f + (wrecked ? 0.3f : 0f), -0.2f), new Vector3(width + 0.4f, 0.25f, 5.6f), mLight, wrecked ? new Vector3(11f, 0, 4f) : Vector3.zero);
            Child(s, "PostL", new Vector3(-width * 0.5f, 1.5f, 2.4f), new Vector3(0.3f, 3f, 0.3f), mStruct);
            Child(s, "PostR", new Vector3(width * 0.5f, 1.5f, 2.4f), new Vector3(0.3f, 3f, 0.3f), mStruct);
        }

        static void Midway()
        {
            var g = G("2_Midway");
            float[] zs = { 47f, 40f, 22f, 15f, 8f };
            foreach (float z in zs)
            {
                Stall(g, -16f, z, -1, rng.NextDouble() < 0.5 ? 6f : 7f, rng.NextDouble() < 0.3);
                Stall(g, 16f, z, 1, rng.NextDouble() < 0.5 ? 6f : 7f, rng.NextDouble() < 0.3);
            }
            // The kiosk in the middle of the avenue breaks the long sightline; fallen boards add cover.
            var k = Prop(g, "TicketKiosk", 0, 26, 0);
            Child(k, "Box", new Vector3(0, 1.7f, 0), new Vector3(7, 3.4f, 5), mStruct);
            Child(k, "Roof", new Vector3(0, 3.6f, 0), new Vector3(8, 0.3f, 6), mLight);
            Box(g, "FallenSign_W", -5.5f, 14f, float.NaN, 4f, 0.5f, 1.2f, 25, mCover).transform.Rotate(0, 0, 18);
            Box(g, "FallenSign_E", 5.5f, 36f, float.NaN, 4f, 0.5f, 1.2f, -35, mCover).transform.Rotate(0, 0, -15);
            Cyl(g, "FountainBase", 0, 44, float.NaN, 2.6f, 0.9f, mLight);
        }

        // ------------------------------------------------------------------ 3 carousel plaza

        static void Carousel()
        {
            var g = G("3_CarouselPlaza");
            Vector2 c = CarouselCentre;
            float gy = H(c.x, c.y);
            Cyl(g, "CarouselBase", c.x, c.y, gy - 0.05f, 7.5f, 0.85f, mStruct);
            Cyl(g, "CarouselColumn", c.x, c.y, gy, 0.9f, 5.2f, mStruct);
            var canopy = Cyl(g, "CarouselCanopy_Collapsed", c.x, c.y, gy + 4.7f, 7.5f, 0.5f, mLight);
            canopy.transform.rotation = Quaternion.Euler(13f, 0f, 9f);
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                float hx = c.x + Mathf.Cos(a) * 5f, hz = c.y + Mathf.Sin(a) * 5f;
                bool fallen = i % 3 == 0;
                var horse = Box(g, fallen ? "Horse_Fallen" : "Horse", hx + (fallen ? R(-2, 2) : 0), hz + (fallen ? R(-2, 2) : 0),
                    fallen ? H(hx, hz) - 0.05f : gy + 0.85f, 0.7f, 1.4f, 2.0f, R(0, 360), mCover);
                if (fallen) horse.transform.Rotate(0, 0, 70f);
            }
            // Planters ringing the plaza: round-the-clock cover for the 360 degree fight.
            for (int i = 0; i < 10; i++)
            {
                float a = (i + 0.5f) / 10f * Mathf.PI * 2f;
                if (Mathf.Abs(Mathf.Cos(a)) < 0.2f && Mathf.Sin(a) > 0f) continue;     // keep the Midway mouth open
                Box(g, "Planter", c.x + Mathf.Cos(a) * 15.5f, c.y + Mathf.Sin(a) * 15.5f, float.NaN, 3.2f, 0.9f, 1.2f, -a * Mathf.Rad2Deg + 90f, mCover);
            }
        }

        // ------------------------------------------------------------------ 4 ferris wheel

        static void Ferris()
        {
            var g = G("4_FerrisWheel");
            Vector2 c = FerrisCentre;
            float hubY = H(c.x, c.y) + 19.5f, rad = 16.5f;
            var hub = new Vector3(c.x, hubY, c.y);
            System.Func<int, Vector3> rim = i =>
            {
                float t = i / 24f * Mathf.PI * 2f;
                return new Vector3(c.x, hubY + rad * Mathf.Sin(t), c.y + rad * Mathf.Cos(t));
            };
            for (int i = 0; i < 24; i++) Beam(g, "Rim", rim(i), rim(i + 1), 0.5f, 1.2f, mStruct, Vector3.right);
            for (int i = 0; i < 8; i++) Beam(g, "Spoke", hub, rim(i * 3), 0.3f, 0.6f, mStruct, Vector3.right);
            Box(g, "Hub", c.x, c.y, hubY - 1.1f, 2.2f, 2.2f, 2.2f, 0, mDark);
            for (int i = 0; i < 12; i++)
            {
                Vector3 p = rim(i * 2);
                if (i == 3 || i == 8) Box(g, "Gondola_Fallen", c.x + R(-4, 4), c.y + (i == 3 ? -9f : 9f) + R(-3, 3), float.NaN, 1.7f, 1.6f, 2.0f, R(0, 180), mCar);
                else Box(g, "Gondola", p.x, p.z, p.y - 2.3f, 1.7f, 1.6f, 2.0f, 0, mCar);
            }
            foreach (float sx in new[] { -3f, 3f })
                foreach (float sz in new[] { -1f, 1f })
                    Beam(g, "Leg", P(c.x + sx * 1.2f, c.y + sz * 11f, -0.2f), hub + new Vector3(sx * 0.5f, 0, 0), 0.9f, 0.9f, mStruct);

            // Boarding platform: an elevated position with flush stairs.
            float py = H(46.5f, c.y) + 2f;
            Box(g, "BoardingPlatform", 46.5f, c.y, H(46.5f, c.y) - 0.1f, 6f, 2.1f, 9f, 0, mStruct);
            Stairs(g, "BoardingStairs", new Vector3(43.5f, py, c.y), Vector2.right, 3f, 2f, mStruct);
            Elevated.Add(("Ferris boarding platform", new Vector2(46.5f, c.y), 2f));
            Box(g, "WheelTicketBooth", 40.5f, -26f, float.NaN, 3, 3.2f, 3, 0, mStruct);
        }

        // ------------------------------------------------------------------ 5 flooded lowlands

        static void Boardwalk(Transform g, params Vector2[] pts)
        {
            for (int s = 0; s < pts.Length - 1; s++)
            {
                Vector2 a = pts[s], b = pts[s + 1];
                float len = Vector2.Distance(a, b);
                Vector2 dir = (b - a) / len;
                float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                int n = Mathf.CeilToInt(len / 2.5f);
                for (int i = 0; i < n; i++)
                {
                    if (i > 1 && i < n - 2 && rng.NextDouble() < 0.14) continue;                 // a missing plank
                    Vector2 c = a + dir * ((i + 0.5f) * len / n);
                    float tilt = (rng.NextDouble() < 0.12) ? R(-6f, 6f) : 0f;                  // a warped one
                    var plank = Box(g, "Plank", c.x, c.y, 0.0f, 2.2f, 0.16f, len / n - 0.05f, yaw, mWalk);
                    plank.transform.Rotate(tilt, 0, 0, Space.Self);
                }
                for (float t = 1f; t < len; t += 5f)
                {
                    Vector2 c = a + dir * t;
                    Box(g, "BoardwalkPost", c.x, c.y, H(c.x, c.y) - 0.1f, 0.3f, 1.0f - H(c.x, c.y), 0.3f, 0, mWalk);
                }
            }
        }

        static void Lowlands()
        {
            var g = G("5_FloodedLowlands");
            var vol = new GameObject("WadingZone_Lowlands");
            vol.transform.SetParent(g, false);
            vol.transform.position = new Vector3(54f, -0.28f, -58f);
            var col = vol.AddComponent<BoxCollider>();
            col.size = new Vector3(72f, 1.0f, 64f);
            col.isTrigger = true;
            var wz = vol.AddComponent<WadingZone>();
            wz.surfaceY = 0f;

            // Bumper car pavilion: a roof on posts over a half-submerged floor, with low walls and gaps.
            Vector2 pc = new Vector2(62f, -58f);
            float fy = H(pc.x, pc.y);
            Box(g, "PavilionFloor", pc.x, pc.y, fy - 0.1f, 26f, 0.35f, 20f, 0, mStruct);
            foreach (float sx in new[] { -12.5f, 12.5f })
                foreach (float sz in new[] { -9.5f, 9.5f })
                    Box(g, "PavilionPost", pc.x + sx, pc.y + sz, float.NaN, 0.8f, 5.5f - fy, 0.8f, 0, mStruct);
            Box(g, "PavilionRoof", pc.x, pc.y, 5.5f, 28f, 0.4f, 22f, 0, mLight);
            Wall(g, "PavilionWall_N", new Vector2(pc.x - 13, pc.y + 10), new Vector2(pc.x + 13, pc.y + 10), 1.3f, 0.4f, mStruct, new Vector2(11f, 15f));
            Wall(g, "PavilionWall_S", new Vector2(pc.x - 13, pc.y - 10), new Vector2(pc.x + 13, pc.y - 10), 1.3f, 0.4f, mStruct, new Vector2(4f, 8f), new Vector2(18f, 21f));
            Wall(g, "PavilionWall_W", new Vector2(pc.x - 13, pc.y - 10), new Vector2(pc.x - 13, pc.y + 10), 1.3f, 0.4f, mStruct, new Vector2(7f, 13f));
            for (int i = 0; i < 9; i++)
                Box(g, "BumperCar", pc.x + R(-10, 10), pc.y + R(-7, 7), float.NaN, 1.7f, 1.0f, 1.2f, R(0, 360), mCar);

            // Kiddie rides, half under water.
            var kc = Cyl(g, "KiddieCarousel", 30f, -44f, float.NaN, 3.2f, 1.5f, mStruct);
            Cyl(g, "KiddieCarouselRoof", 30f, -44f, H(30f, -44f) + 2.6f, 3.4f, 0.3f, mLight);
            Cyl(g, "KiddieCarouselPole", 30f, -44f, float.NaN, 0.3f, 2.8f, mStruct);
            for (int i = 0; i < 6; i++) Box(g, "KiddieTrain", 30f + Mathf.Cos(i * 1.05f) * 4f, -70f + Mathf.Sin(i * 1.05f) * 4f, float.NaN, 1.3f, 1.2f, 1.9f, i * 60f, mCar);
            for (int i = 0; i < 5; i++) Cyl(g, "Teacup", 80f + Mathf.Cos(i * 1.26f) * 3.5f, -42f + Mathf.Sin(i * 1.26f) * 3.5f, float.NaN, 1.2f, 0.8f, mCar);

            // Dry routes above the water: boardwalks with warped and missing planks.
            Boardwalk(g, new Vector2(38f, -26f), new Vector2(38f, -50f), new Vector2(38f, -68f), new Vector2(10f, -68f));
            Boardwalk(g, new Vector2(38f, -50f), new Vector2(50f, -50f));
            Boardwalk(g, new Vector2(50f, -26f), new Vector2(50f, -44f), new Vector2(62f, -47f));
            // Boardwalk ramps so the player can climb onto the first plank without jumping.
            foreach (var p in new[] { new Vector2(38f, -24.2f), new Vector2(50f, -24.2f) })
            {
                var r = Box(g, "BoardwalkRamp", p.x, p.y, -0.3f, 2.2f, 0.16f, 3.2f, 0f, mWalk);
                r.transform.Rotate(8f, 0f, 0f, Space.Self);
            }

            // Floating and half-sunk debris: pallets, crates, a drifting bin (cover in the water).
            for (int i = 0; i < 26; i++)
            {
                float x = R(18f, 86f), z = R(-86f, -28f);
                if (Vector2.Distance(new Vector2(x, z), pc) < 14f) continue;
                if (Mathf.Abs(x - 38f) < 3f || Mathf.Abs(x - 50f) < 3f && z > -46f) continue;
                if (i % 3 == 0) Box(g, "FloatingPallet", x, z, -0.1f, 1.2f, 0.15f, 1.0f, R(0, 180), mWalk);
                else Box(g, "SunkenCrate", x, z, float.NaN, R(1.1f, 2.0f), R(1.0f, 1.6f), R(1.1f, 2.0f), R(0, 180), mCover);
            }
        }

        // ------------------------------------------------------------------ 6 roller coaster

        static void Coaster()
        {
            var g = G("6_RollerCoaster");
            const int n = 64;
            System.Func<int, Vector3> tp = i =>
            {
                float t = (i % n) / (float)n * Mathf.PI * 2f;
                float x = -62f + 19f * Mathf.Cos(t), z = -10f + 42f * Mathf.Sin(t);
                return new Vector3(x, H(x, z) + 8f + 5f * Mathf.Sin(2f * t + 0.5f), z);
            };
            // Collapsed section: points 38..47 sag to the ground and lie there.
            System.Func<int, Vector3> pt = i =>
            {
                var p = tp(i);
                float gy = H(p.x, p.z) + 0.5f;
                float k = 0f;
                if (i >= 34 && i <= 38) k = Mathf.InverseLerp(34f, 38f, i);
                else if (i > 38 && i < 46) k = 1f;
                else if (i >= 46 && i <= 50) k = Mathf.InverseLerp(50f, 46f, i);
                if (k > 0f) p.y = Mathf.Lerp(p.y, gy + (i > 38 && i < 46 ? R(0f, 0.8f) : 0f), k);
                return p;
            };
            var pts = new Vector3[n + 1];
            for (int i = 0; i <= n; i++) pts[i] = pt(i);
            pts[n] = pts[0];
            for (int i = 0; i < n; i++)
            {
                Beam(g, "Track", pts[i], pts[i + 1], 1.8f, 0.7f, mStruct);
                if (i % 4 == 0 && !(i >= 36 && i <= 48))
                {
                    float gy = H(pts[i].x, pts[i].z);
                    Box(g, "Support", pts[i].x, pts[i].z, gy - 0.1f, 0.9f, pts[i].y - gy - 0.2f, 0.9f, 0, mStruct);
                }
            }
            for (int i = 0; i < 9; i++)
                Box(g, "TrackDebris", pts[40].x + R(-6, 6), pts[40].z + R(-7, 7), float.NaN, R(0.8f, 3f), R(0.5f, 1.4f), R(0.8f, 2.5f), R(0, 180), mCover);

            // Station platform: an elevated position, stairs at both ends.
            float sy = H(-62f, 24f) + 3.5f;
            Box(g, "StationPlatform", -62f, 24f, H(-62f, 24f) - 0.1f, 22f, 3.6f, 7f, 0, mStruct);
            Stairs(g, "StationStairs_E", new Vector3(-51f, sy, 24f), Vector2.left, 2.6f, 3.5f, mStruct);
            Stairs(g, "StationStairs_W", new Vector3(-73f, sy, 24f), Vector2.right, 2.6f, 3.5f, mStruct);
            Box(g, "StationRoof", -62f, 24f, sy + 3.2f, 24f, 0.3f, 8f, 0, mLight);
            Elevated.Add(("Coaster station platform", new Vector2(-66f, 24f), 3.5f));
            foreach (float px in new[] { -72.5f, -51.5f }) Box(g, "StationRoofPost", px, 24f, sy, 0.5f, 3.2f, 0.5f, 0, mStruct);
            Box(g, "RideEntranceGate", -62f, 17f, float.NaN, 5f, 1.1f, 0.5f, 0, mCover);
        }

        // ------------------------------------------------------------------ 7 backlot / service yard

        static void Backlot()
        {
            var g = G("7_BacklotServiceYard");
            Wall(g, "YardFence_W", new Vector2(46, 12), new Vector2(46, 52), 2.4f, 0.25f, mDark, new Vector2(15f, 21f));
            Wall(g, "YardFence_S", new Vector2(46, 12), new Vector2(86, 12), 2.4f, 0.25f, mDark, new Vector2(14f, 20f));
            Wall(g, "YardFence_N", new Vector2(46, 52), new Vector2(86, 52), 2.4f, 0.25f, mDark);
            Wall(g, "YardFence_E", new Vector2(86, 12), new Vector2(86, 52), 2.4f, 0.25f, mDark);

            foreach (var t in new[] { new Vector3(55, 19, 0), new Vector3(69, 19, 0), new Vector3(57, 34, 0), new Vector3(70, 33, 8) })
            {
                var tr = Prop(g, "Trailer", t.x, t.y, t.z);
                Child(tr, "Body", new Vector3(0, 1.9f, 0), new Vector3(3.2f, 2.8f, 9f), mStruct);
                Child(tr, "Door", new Vector3(0, 1.0f, 4.6f), new Vector3(1f, 2f, 0.2f), mCover);
            }
            for (int i = 0; i < 5; i++) Box(g, "Generator", R(50, 84), R(16, 48), float.NaN, 2f, 1.3f, 1.2f, R(0, 180), mCover);
            for (int i = 0; i < 10; i++) Cyl(g, "FuelDrum", 62f + R(-3, 3) + (i % 5) * 2.2f, 44f + (i / 5) * 2f + R(-.5f, .5f), float.NaN, 0.32f, 0.95f, mDark);

            // Maintenance garage: roof, back wall, openings on two sides.
            Vector2 gc = new Vector2(78f, 45f);
            var gp = Prop(g, "Garage", gc.x, gc.y, 0);
            float gh = 5f;
            Child(gp, "Roof", new Vector3(0, gh, 0), new Vector3(15f, 0.4f, 10.5f), mLight);
            Child(gp, "BackWall", new Vector3(0, gh * 0.5f, 5f), new Vector3(14f, gh, 0.5f), mStruct);
            Child(gp, "SideWall_E", new Vector3(7f, gh * 0.5f, 0), new Vector3(0.5f, gh, 9.5f), mStruct);
            Child(gp, "FrontWall_L", new Vector3(-4.5f, gh * 0.5f, -5f), new Vector3(5f, gh, 0.5f), mStruct);
            Child(gp, "FrontWall_R", new Vector3(4.5f, gh * 0.5f, -5f), new Vector3(5f, gh, 0.5f), mStruct);
            Child(gp, "SideWall_W_N", new Vector3(-7f, gh * 0.5f, 3f), new Vector3(0.5f, gh, 4f), mStruct);
            Child(gp, "SideWall_W_S", new Vector3(-7f, gh * 0.5f, -3.8f), new Vector3(0.5f, gh, 2.4f), mStruct);
            Child(gp, "Workbench", new Vector3(0, 0.5f, 3.4f), new Vector3(5f, 1.0f, 1.2f), mCover);
            Child(gp, "Pallets", new Vector3(3.5f, 0.6f, -0.5f), new Vector3(2f, 1.2f, 2f), mCover);
        }

        // ------------------------------------------------------------------ 8 big top

        static void BigTop()
        {
            var g = G("8_BigTop");
            Vector2 c = BigTopCentre;
            float gy = H(c.x, c.y);
            const float rad = 19f;
            // Low torn-canvas wall with entrances on three sides (north, east, west).
            const int segs = 28;
            for (int i = 0; i < segs; i++)
            {
                float a = (i + 0.5f) / segs * 360f;
                float dN = Mathf.Abs(Mathf.DeltaAngle(a, 90f)), dE = Mathf.Abs(Mathf.DeltaAngle(a, 0f)), dW = Mathf.Abs(Mathf.DeltaAngle(a, 180f));
                if (dN < 10.5f || dE < 10.5f || dW < 10.5f) continue;
                float x = c.x + Mathf.Cos(a * Mathf.Deg2Rad) * rad, z = c.y + Mathf.Sin(a * Mathf.Deg2Rad) * rad;
                Box(g, "TentWall", x, z, float.NaN, 0.5f, R(1.2f, 3.4f), 4.4f, -a, mLight);
            }
            foreach (var p in new[] { new Vector2(4, 4), new Vector2(-4, 4), new Vector2(4, -4), new Vector2(-4, -4) })
                Cyl(g, "CentrePole", c.x + p.x, c.y + p.y, float.NaN, 0.4f, 18f, mDark);
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                Cyl(g, "TentPost", c.x + Mathf.Cos(a) * rad, c.y + Mathf.Sin(a) * rad, float.NaN, 0.3f, 7f, mDark);
            }
            // Roof: twelve sloped slabs; four are gone (the holes the light comes through) and two lie fallen.
            for (int i = 0; i < 12; i++)
            {
                float a = (i + 0.5f) / 12f * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a) * rad, -9f, Mathf.Sin(a) * rad);
                Vector3 mid = new Vector3(c.x + Mathf.Cos(a) * rad * 0.5f, gy + 7f + 9f * 0.5f, c.y + Mathf.Sin(a) * rad * 0.5f);
                if (i == 1 || i == 4 || i == 7 || i == 10) continue;
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = "RoofSlab";
                slab.transform.SetParent(g, false);
                slab.isStatic = true;
                slab.GetComponent<Renderer>().sharedMaterial = mLight;
                if (i == 0 || i == 6)
                {
                    slab.name = "RoofSlab_Fallen";
                    slab.transform.SetPositionAndRotation(new Vector3(c.x + Mathf.Cos(a) * 9f, gy + 1.2f, c.y + Mathf.Sin(a) * 9f), Quaternion.LookRotation(new Vector3(Mathf.Cos(a), -0.2f, Mathf.Sin(a))));
                    slab.transform.localScale = new Vector3(5f, 0.25f, 12f);
                }
                else
                {
                    slab.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(dir.normalized, Vector3.up));
                    slab.transform.localScale = new Vector3(2f * Mathf.PI * rad * 0.5f / 12f * 1.6f, 0.25f, dir.magnitude);
                }
            }
            // Collapsed bleachers on the south side: stepped blocks (elevated positions), one tipped over.
            for (int i = 0; i < 4; i++)
            {
                float a = (200f + i * 46f) * Mathf.Deg2Rad;
                float bx = c.x + Mathf.Cos(a) * 14.5f, bz = c.y + Mathf.Sin(a) * 14.5f;
                float yaw = -a * Mathf.Rad2Deg + 90f + 180f;
                var b = Prop(g, i == 2 ? "Bleacher_Collapsed" : "Bleacher", bx, bz, yaw);
                for (int s = 0; s < 3; s++) Child(b, "Step", new Vector3(0, (s + 1) * 0.7f * 0.5f, 1.2f - s * 1.8f), new Vector3(8f, (s + 1) * 0.7f, 1.8f), mStruct);
                if (i == 2) b.Rotate(0, 0, 26f);
                else
                {
                    // Flush stairs run up to the tall (outer) end of the block: that is the elevated position.
                    float ry = yaw * Mathf.Deg2Rad;
                    Vector2 right = new Vector2(Mathf.Cos(ry), -Mathf.Sin(ry)), fwd = new Vector2(Mathf.Sin(ry), Mathf.Cos(ry));
                    Vector2 top = new Vector2(bx, bz) + right * 4f - fwd * 2.4f;
                    Stairs(g, "BleacherStairs", new Vector3(top.x, H(top.x, top.y) + 2.1f, top.y), -right, 2.2f, 2.1f, mStruct);
                    Elevated.Add(("Big Top bleachers " + i, new Vector2(bx, bz) - fwd * 2.4f, 2.1f));
                }
            }
            // The ring and the boss's mark.
            Cyl(g, "RingCurb", c.x, c.y, float.NaN, 8f, 0.3f, mStruct);
            var boss = new GameObject("BossSpawn");
            boss.transform.SetParent(g, false);
            boss.transform.position = P(c.x, c.y, 0.1f);
            for (int i = 0; i < 6; i++) Box(g, "RingDebris", c.x + R(-12, 12), c.y + R(-12, 8), float.NaN, R(1f, 2.2f), R(0.8f, 1.6f), R(1f, 2.2f), R(0, 180), mCover);
        }


        // ------------------------------------------------------------------ outskirts (the 450 m arena beyond the fair)

        /// <summary>
        /// What the abandoned grounds spread into once the fair was left: roads run out from the gate, and a
        /// car park, ranger lodge, camp, water tower, watch post and fallen fence lines stand in the meadows.
        /// Every enclosed building has two openings of 2.6 m or more; the one raised deck has flush stairs.
        /// </summary>
        static void Outskirts()
        {
            var g = G("A_Outskirts");

            // Overflow car park, north-east: rows of abandoned cars, some askew.
            for (int row = 0; row < 3; row++)
                for (int i = 0; i < 9; i++)
                {
                    if (rng.NextDouble() < 0.25) continue;
                    Car(g, 70f + i * 6.5f + R(-0.4f, 0.4f), 196f + row * 10f, 90f + R(-8f, 8f) + (rng.NextDouble() < 0.15 ? 40f : 0f));
                }
            Wall(g, "CarParkKerb", new Vector2(64, 190), new Vector2(130, 190), 0.5f, 0.4f, mLight);

            // Ranger lodge, east: a long hut, doors on its south and north faces.
            Vector2 lc = new Vector2(190f, 90f);
            var lp = Prop(g, "RangerLodge", lc.x, lc.y, 0);
            float lh = 3.4f;
            Child(lp, "Roof", new Vector3(0, lh, 0), new Vector3(17f, 0.35f, 9f), mLight);
            Child(lp, "WallE", new Vector3(8.4f, lh * 0.5f, 0), new Vector3(0.4f, lh, 8.4f), mStruct);
            Child(lp, "WallW", new Vector3(-8.4f, lh * 0.5f, 0), new Vector3(0.4f, lh, 8.4f), mStruct);
            foreach (float zs in new[] { -4.2f, 4.2f })
            {
                Child(lp, "WallL", new Vector3(-5.6f, lh * 0.5f, zs), new Vector3(5.6f, lh, 0.4f), mStruct);
                Child(lp, "WallR", new Vector3(5.6f, lh * 0.5f, zs), new Vector3(5.6f, lh, 0.4f), mStruct);
            }
            Child(lp, "Counter", new Vector3(0, 0.5f, 0), new Vector3(5f, 1f, 1.1f), mCover);
            for (int i = 0; i < 3; i++) Box(g, "Woodpile", lc.x + 12f, lc.y - 4f + i * 3f, float.NaN, 2.4f, 1.3f, 1.2f, R(-10, 10), mCover);

            // Campsite, south-west: tents (low wedges), a fire ring, log seats.
            Vector2 cc = new Vector2(-160f, -150f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.047f + 0.3f;
                Box(g, "CampTent", cc.x + Mathf.Cos(a) * 11f, cc.y + Mathf.Sin(a) * 11f, float.NaN, 3.4f, 1.6f, 2.6f, -a * Mathf.Rad2Deg + 90f, mCover);
            }
            Cyl(g, "FireRing", cc.x, cc.y, float.NaN, 1.2f, 0.4f, mDark);
            for (int i = 0; i < 4; i++) Box(g, "LogSeat", cc.x + Mathf.Cos(i * 1.57f) * 3.2f, cc.y + Mathf.Sin(i * 1.57f) * 3.2f, float.NaN, 2.2f, 0.5f, 0.5f, i * 90f + 90f, mCover);

            // Water tower, north-west: four legs and a tank, one dead standpipe, cover at the base.
            Vector2 wc = new Vector2(-165f, 165f);
            foreach (var o in new[] { new Vector2(-3.5f, -3.5f), new Vector2(3.5f, -3.5f), new Vector2(-3.5f, 3.5f), new Vector2(3.5f, 3.5f) })
                Box(g, "TowerLeg", wc.x + o.x, wc.y + o.y, float.NaN, 0.7f, 13f, 0.7f, 0f, mStruct);
            Cyl(g, "WaterTank", wc.x, wc.y, H(wc.x, wc.y) + 13f, 5.2f, 6f, mLight);
            Box(g, "TowerBase", wc.x, wc.y, float.NaN, 2.4f, 1.1f, 2.4f, 0f, mCover);
            for (int i = 0; i < 4; i++) Box(g, "Crate", wc.x + R(-9, 9), wc.y + R(-9, 9), float.NaN, 1.3f, 1.3f, 1.3f, R(0, 90), mCover);

            // Watch post, far east: a raised deck on posts, flush stairs from the south, a low parapet.
            Vector2 tp = new Vector2(195f, 160f);
            float th = 3.4f, base0 = H(tp.x, tp.y);
            Box(g, "WatchDeck", tp.x, tp.y, base0 + th - 0.3f, 7f, 0.3f, 7f, 0f, mStruct);
            foreach (var o in new[] { new Vector2(-3f, -3f), new Vector2(3f, -3f), new Vector2(-3f, 3f), new Vector2(3f, 3f) })
                Box(g, "WatchPost", tp.x + o.x, tp.y + o.y, float.NaN, 0.5f, th, 0.5f, 0f, mStruct);
            Box(g, "Parapet", tp.x, tp.y + 3.3f, base0 + th, 7f, 0.9f, 0.25f, 0f, mCover);
            Box(g, "Parapet", tp.x + 3.3f, tp.y, base0 + th, 0.25f, 0.9f, 6.4f, 0f, mCover);
            Stairs(g, "WatchStairs", new Vector3(tp.x - 1.5f, base0 + th, tp.y - 3.5f), Vector2.up, 2.2f, th, mStruct);
            Elevated.Add(("Watch post", tp, th));

            // Fallen fence lines: broken runs with wide gaps, for cover across the open ground.
            Wall(g, "FallenFence", new Vector2(-120, -195), new Vector2(-60, -195), 1.2f, 0.25f, mDark, new Vector2(18f, 24f), new Vector2(40f, 46f));
            Wall(g, "FallenFence", new Vector2(-212, 40), new Vector2(-212, 110), 1.2f, 0.25f, mDark, new Vector2(20f, 26f), new Vector2(48f, 54f));
            Wall(g, "FallenFence", new Vector2(120, 140), new Vector2(190, 140), 1.2f, 0.25f, mDark, new Vector2(22f, 28f), new Vector2(50f, 56f));

            // Wrecks on the service roads.
            foreach (var w in new[] { new Vector3(-195, 60, 8), new Vector3(-195, -90, -5), new Vector3(60, 185, 95), new Vector3(-120, 185, 85), new Vector3(0, 200, 4), new Vector3(-175, 0, 92) })
                Car(g, w.x, w.y, w.z);
        }

        // ------------------------------------------------------------------ zones (trigger volumes for spawns, minimap, labels)

        static readonly (string name, Vector2 centre, Vector2 size)[] ZonePlan =
        {
            ("Main Gate",       new Vector2(0, 75),    new Vector2(180, 30)),
            ("The Midway",      new Vector2(0, 30),    new Vector2(40, 50)),
            ("Carousel Plaza",  new Vector2(0, -12),   new Vector2(40, 40)),
            ("Ferris Wheel",    new Vector2(50, -14),  new Vector2(26, 38)),
            ("Old Growth",      new Vector2(54, -58),  new Vector2(72, 64)),
            ("Roller Coaster",  new Vector2(-62, -10), new Vector2(46, 96)),
            ("Backlot",         new Vector2(66, 32),   new Vector2(42, 42)),
            ("Big Top",         new Vector2(-10, -68), new Vector2(42, 42)),
        };

        /// <summary>Spawn zones. The sites' zones follow their sites out and grow 1.5x to take in the open ground around them; the meadows are new.</summary>
        public static (string name, Vector2 centre, Vector2 size)[] ZoneDefs
        {
            get
            {
                var list = new List<(string, Vector2, Vector2)>();
                foreach (var z in ZonePlan) list.Add((z.name, stretched ? Shift(z.centre) : z.centre, z.size * (stretched ? 1.5f : 1f)));
                if (stretched)
                {
                    list.Add(("West Meadow", new Vector2(-205, 0), new Vector2(40, 300)));
                    list.Add(("North Fields", new Vector2(0, 205), new Vector2(400, 40)));
                    list.Add(("South Meadow", new Vector2(-60, -205), new Vector2(300, 40)));
                    list.Add(("East Meadow", new Vector2(205, 20), new Vector2(40, 240)));
                }
                return list.ToArray();
            }
        }

        static void Zones()
        {
            var g = G("Zones");
            foreach (var z in ZoneDefs)
            {
                var go = new GameObject("Zone_" + z.name.Replace(' ', '_'));
                go.transform.SetParent(g, false);
                go.transform.position = new Vector3(z.centre.x, H(z.centre.x, z.centre.y) + 3f, z.centre.y);
                var col = go.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = new Vector3(z.size.x, 10f, z.size.y);
            }
        }

        // ------------------------------------------------------------------ cover, empty cells, sightlines

        static readonly (Vector2 a, Vector2 b, float w)[] LanesPlan =
        {
            (new Vector2(0, 54), new Vector2(0, 6), 20f),                 // Midway
            (new Vector2(-50, 31), new Vector2(50, 31), 6f),              // cross path north
            (new Vector2(-48, -12), new Vector2(50, -12), 8f),            // cross path south
            (new Vector2(-45, 54), new Vector2(-45, -46), 6f),            // west lane (coaster)
            (new Vector2(38, 54), new Vector2(38, -24), 6f),              // east lane (ferris, lowlands)
            (new Vector2(-8, -28), new Vector2(-10, -49), 10f),           // plaza to Big Top
            (new Vector2(-30, 66), new Vector2(30, 66), 16f),             // gate plaza
        };

        static bool Free(Vector2 p, float r)
        {
            var hits = Physics.OverlapBox(new Vector3(p.x, H(p.x, p.y) + 1.2f, p.y), new Vector3(r, 0.8f, r), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits) if (!(h is TerrainCollider)) return false;
            return true;
        }

        static void PlaceCover(Transform g, Vector2 p)
        {
            float yaw = R(0, 180);
            switch (rng.Next(6))
            {
                case 0: Box(g, "Crate", p.x, p.y, float.NaN, 1.2f, 1.2f, 1.2f, yaw, mCover); break;
                case 1: Box(g, "CrateStack", p.x, p.y, float.NaN, 1.4f, 2.0f, 1.4f, yaw, mCover); break;
                case 2: Box(g, "Barrier", p.x, p.y, float.NaN, 3.0f, 1.05f, 0.5f, yaw, mCover); break;
                case 3: Box(g, "Bench", p.x, p.y, float.NaN, 2.2f, 0.9f, 0.7f, yaw, mCover); break;
                case 4: Cyl(g, "Bin", p.x, p.y, float.NaN, 0.5f, 1.0f, mCover); break;
                default: Box(g, "Cart", p.x, p.y, float.NaN, 2.5f, 1.2f, 1.2f, yaw, mCover); break;
            }
            coverPlaced++;
        }

        /// <summary>The lanes between the sites, stretched with the ground (their width is the same).</summary>
        static (Vector2 a, Vector2 b, float w)[] Lanes => LanesPlan.Select(l => (stretched ? Warp(l.a) : l.a, stretched ? Warp(l.b) : l.b, l.w)).ToArray();

        static void ScatterCover()
        {
            var g = G("9_Cover");
            foreach (var lane in Lanes)
            {
                Vector2 d = lane.b - lane.a;
                float len = d.magnitude;
                Vector2 dir = d / len, perp = new Vector2(-dir.y, dir.x);
                for (float s = R(2f, 5f); s < len - 2f; s += R(7.5f, 10.5f))
                {
                    int side = rng.NextDouble() < 0.5 ? -1 : 1;
                    Vector2 p = lane.a + dir * s + perp * (side * R(0.8f, Mathf.Max(1.2f, lane.w * 0.5f - 0.6f)));
                    if (Free(p, 1.3f)) PlaceCover(g, p);
                }
            }
        }

        static void FillEmptyCells()
        {
            var g = G("9_Cover_Fill");
            float lim = Half - 8f;
            for (float cx = -lim; cx <= lim; cx += 10f)
                for (float cz = -lim; cz <= lim; cz += 10f)
                {
                    const bool outer = true;       // the ground between the sites is open: cover about every 25 m, not every 10
                    var hits = Physics.OverlapBox(new Vector3(cx, H(cx, cz) + 1.2f, cz), new Vector3(5f, 1.5f, 5f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                    if (hits.Any(h => !(h is TerrainCollider))) continue;
                    if (outer && rng.NextDouble() < 0.78) continue;
                    cellsFilled++;
                    int made = 0;
                    for (int tries = 0; tries < 12 && made < (outer ? 1 : 3); tries++)
                    {
                        var p = new Vector2(cx + R(-3.5f, 3.5f), cz + R(-3.5f, 3.5f));
                        if (!Free(p, 1.2f)) continue;
                        PlaceCover(g, p);
                        made++;
                    }
                }
        }

        /// <summary>Walks each lane and counts samples with no cover within 6 m (a gap wider than 12 m).</summary>
        static string CoverGapReport()
        {
            int samples = 0, gaps = 0; float worst = 0f;
            foreach (var lane in Lanes)
            {
                Vector2 d = lane.b - lane.a;
                float len = d.magnitude;
                Vector2 dir = d / len;
                for (float s = 0f; s <= len; s += 3f)
                {
                    Vector2 p = lane.a + dir * s;
                    samples++;
                    float best = 99f;
                    foreach (var h in Physics.OverlapSphere(new Vector3(p.x, H(p.x, p.y) + 1f, p.y), 14f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (h is TerrainCollider) continue;
                        float dist = Vector2.Distance(p, new Vector2(h.bounds.center.x, h.bounds.center.z)) - Mathf.Max(h.bounds.extents.x, h.bounds.extents.z) * 0.5f;
                        if (dist < best) best = dist;
                    }
                    if (best > 6f) gaps++;
                    worst = Mathf.Max(worst, best);
                }
            }
            return $"cover: {samples} lane samples, {gaps} with nothing within 6 m, worst nearest-cover distance {worst:F1} m";
        }

        static string ElevatedReport()
        {
            var sb = new StringBuilder("elevated positions (height above surrounding ground):");
            foreach (var e in Elevated)
            {
                if (Physics.Raycast(new Vector3(e.xz.x, 60f, e.xz.y), Vector3.down, out var hit, 100f, ~0, QueryTriggerInteraction.Ignore))
                    sb.Append($"\n   {e.name}: top at y={hit.point.y:F1}, {hit.point.y - H(e.xz.x, e.xz.y + 4f):F1} m above ground 4 m away (hit {hit.collider.name})");
            }
            return sb.ToString();
        }

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Places sightline blockers must not go: lanes, the spawn, the boss arena, and the fixed camera views.</summary>
        static bool Protected(Vector2 p)
        {
            if (Vector2.Distance(p, Shift(new Vector2(0f, 84f))) < 16f) return true;
            if (Vector2.Distance(p, BigTopCentre) < 24f) return true;
            foreach (var lane in Lanes)
                if (DistToSegment(p, lane.a, lane.b) < lane.w * 0.5f + 2.5f) return true;
            foreach (var seg in FairgroundSpots.ViewSegments())
                if (DistToSegment(p, Shift(seg.from), Shift(seg.from) + (Shift(seg.look) - Shift(seg.from)).normalized * 32f) < 7f) return true;
            return false;
        }

        static bool OpenAt(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            return !Physics.Raycast(a, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
        }

        static Vector3? RandomEye()
        {
            for (int i = 0; i < 20; i++)
            {
                float x = R(-(Core - 2f), Core - 2f), z = R(-(Core - 2f), Core - 2f);
                var p = new Vector3(x, H(x, z) + Eye, z);
                if (!Physics.CheckSphere(p, 0.4f, ~0, QueryTriggerInteraction.Ignore)) return p;
            }
            return null;
        }

        /// <summary>
        /// Breaks up ground-level sightlines longer than ~60 m with rubble walls, then reports what is left.
        /// Works in passes: gather open long lines, block each at the first free spot along it, re-test.
        /// </summary>
        static string FixSightlines()
        {
            var g = G("9_SightlineBlockers");
            float[] fractions = { 0.5f, 0.38f, 0.62f, 0.27f, 0.73f, 0.16f, 0.84f };
            for (int pass = 0; pass < 14 && blockersAdded < 80; pass++)
            {
                var open = new List<(Vector3 a, Vector3 b)>();
                for (int k = 0; k < 9000 && open.Count < 220; k++)
                {
                    var a = RandomEye(); var b = RandomEye();
                    if (a == null || b == null) continue;
                    float dist = Vector3.Distance(a.Value, b.Value);
                    if (dist < 60f || dist > 200f || !OpenAt(a.Value, b.Value)) continue;
                    open.Add((a.Value, b.Value));
                }
                if (open.Count == 0) break;
                foreach (var pair in open)
                {
                    if (blockersAdded >= 80) break;
                    if (!OpenAt(pair.a, pair.b)) continue;                      // an earlier blocker already cut this one
                    Vector3 dd = pair.b - pair.a; dd.y = 0;
                    float yaw = Mathf.Atan2(dd.x, dd.z) * Mathf.Rad2Deg + 90f;
                    foreach (float f in fractions)
                    {
                        Vector3 mid = Vector3.Lerp(pair.a, pair.b, f);
                        if (!Free(new Vector2(mid.x, mid.z), 3.5f) || Protected(new Vector2(mid.x, mid.z))) continue;
                        Box(g, "RubbleWall", mid.x, mid.z, float.NaN, 7f, 3f, 0.9f, yaw, mStruct);
                        blockersAdded++;
                        break;
                    }
                }
            }
            // Measure.
            float longest = 0f; int over60 = 0, samples = 0;
            for (int i = 0; i < 6000; i++)
            {
                var a = RandomEye(); var b = RandomEye();
                if (a == null || b == null) continue;
                float dist = Vector3.Distance(a.Value, b.Value);
                if (dist < 20f) continue;
                samples++;
                if (OpenAt(a.Value, b.Value)) { if (dist > longest) longest = dist; if (dist > 60f) over60++; }
            }
            return $"sightlines: {samples} random ground-level pairs >20 m, longest unobstructed {longest:F1} m, {over60} unobstructed pairs over 60 m";
        }
    }

    /// <summary>Fixed camera spots shared by every step, so before/after shots line up.</summary>
    public static class FairgroundSpots
    {
        public static readonly string[] Names = { "gate", "midway", "carousel", "ferris", "lowlands", "bigtop" };
        public static readonly string[] Extra = { "coaster", "backlot" };

        static readonly Dictionary<string, (Vector2 from, Vector2 look, float lookY)> Spots = new Dictionary<string, (Vector2, Vector2, float)>
        {
            { "gate",     (new Vector2(0, 87),    new Vector2(0, 60),    1.5f) },
            { "midway",   (new Vector2(0, 52),    new Vector2(0, 8),     1.5f) },
            { "carousel", (new Vector2(0, 10),    new Vector2(0, -12),   3.0f) },
            { "ferris",   (new Vector2(22, -4),   new Vector2(52, -14),  14f)  },
            { "lowlands", (new Vector2(28, -30),  new Vector2(56, -58),  0.5f) },
            { "bigtop",   (new Vector2(-10, -40), new Vector2(-10, -68), 5f)   },
            { "coaster",  (new Vector2(-45, 18),  new Vector2(-62, 5),   8f)   },
            { "backlot",  (new Vector2(46, 30),   new Vector2(70, 30),   2f)   },
        };

        public static IEnumerable<(Vector2 from, Vector2 look)> ViewSegments()
        {
            foreach (var kv in Spots) yield return (kv.Value.from, kv.Value.look);
        }

        public static bool TryGet(string name, out Vector3 pos, out Vector3 look)
        {
            pos = look = default;
            if (!Spots.TryGetValue(name, out var s)) return false;
            pos = new Vector3(s.from.x, FairgroundBlockout.H(s.from.x, s.from.y) + FairgroundBlockout.Eye, s.from.y);
            look = new Vector3(s.look.x, FairgroundBlockout.H(s.look.x, s.look.y) + s.lookY, s.look.y);
            return true;
        }
    }

    /// <summary>Renders from a temporary camera (nothing is added to the scene).</summary>
    public static class FairgroundCapture
    {
        static Camera Temp(bool solidSky, Color sky)
        {
            var go = new GameObject("__shot") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 500f;
            cam.clearFlags = solidSky ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            cam.backgroundColor = sky;
            go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            return cam;
        }

        public static string Spot(string name, string path, bool solidSky = true, float fov = 75f, int w = 1280, int h = 720)
        {
            if (!FairgroundSpots.TryGet(name, out var pos, out var look)) return "unknown spot " + name;
            var cam = Temp(solidSky, new Color(0.62f, 0.66f, 0.70f));
            cam.fieldOfView = fov;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
            string r = FairgroundShots.Capture(cam, path, w, h);
            Object.DestroyImmediate(cam.gameObject);
            return r;
        }

        public static string TopDown(string path, float orthoSize = 100f, int w = 1280, int h = 720)
        {
            var cam = Temp(true, new Color(0.62f, 0.66f, 0.70f));
            cam.orthographic = true;
            cam.orthographicSize = orthoSize;
            cam.farClipPlane = 600f;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 250f, 0f), Quaternion.Euler(90f, 0f, 0f));
            string r = FairgroundShots.Capture(cam, path, w, h);
            Object.DestroyImmediate(cam.gameObject);
            return r;
        }
    }
}
#endif
