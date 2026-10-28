#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// FPSKit > Realism > Audit Arena (Docs/DESIGN_NOTES.md "The realism audit"). Screenshots were misread before: blocky,
    /// flat-coloured objects described as detailed. So every judgement here is a measurement, of three things that must all
    /// pass for an object to count as real:
    ///
    ///   * Shape: triangles, triangles per square metre, how much of the hard-edge length is bevelled, how much of the
    ///     surface is plain boxes (8 corners, 12 triangles), whether walls have thickness (casts through them).
    ///   * Surface: the maps a material has, their size, texels per metre where it is seen, how often one texture repeats
    ///     across a face, how much the albedo varies, how strongly the normal map tilts the surface.
    ///   * Light: lit shader, shadows received, probes used, albedo, smoothness and metallic against the real material's
    ///     range (URP's own validation limits too), occlusion where it is smooth, emission only on lamps.
    ///
    /// An object is every imported model (by prefab, or mesh), every generated surface (by the builder's group and material:
    /// the town's chunks are merged meshes, and a fix is made per material in the generator), every Unity primitive, each
    /// terrain layer, the sky, and each gun the player can carry. Its screen importance is measured, not guessed: along a
    /// fixed walk through the arena (FPSKitRealismAudit.Route) every view is drawn once with each object in its own id
    /// colour and the pixels counted (FPSKitRealismViews). The worst-rated and most seen get the visual evidence
    /// (FPSKitRealismEvidence): close-up, wireframe, texel density, URP albedo validation, and the five-light test.
    ///
    /// Writes Docs/REALISM_AUDIT_&lt;Arena&gt;.md, a CSV beside it, and the images under Build/Realism/&lt;Arena&gt;/.
    ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.AuditRealism -fpskitTheme "Desert Outpost" [-fpskitRealismTop 20]
    /// </summary>
    public static partial class FPSKitRealismAudit
    {
        // ------------------------------------------------------------------
        // Thresholds (the report prints them; change them here)
        // ------------------------------------------------------------------
        /// <summary>The route passes within this of an object: it is seen up close, and close-up rules apply.</summary>
        const float NearMetres = 8f;
        /// <summary>Share of the hard-edge length that is bevelled (a 10-60 degree edge beside a strip under 6 cm wide)
        /// below which an object's edges are perfectly sharp.</summary>
        const float BevelShareMin = 0.05f;
        /// <summary>A near prop with this share of its surface in plain boxes is built of boxes.</summary>
        const float BoxShareFake = 0.5f;
        /// <summary>A near prop (not a building surface) with fewer triangles per square metre than this is low-poly.</summary>
        const float PropTrisPerM2Min = 30f;
        /// <summary>Albedo variation (std / mean of luminance) under this, with no normal map, is a flat single colour.</summary>
        const float FlatVariation = 0.03f;
        /// <summary>Mean tilt of the normal map (with its strength applied) under this shows no relief in raking light.</summary>
        const float NormalTiltMin = 3f;
        /// <summary>Texels per metre wanted on a near surface (a metre away it fills about 750 screen pixels at 1080p).</summary>
        const float TexelNear = 256f;
        /// <summary>Texels per metre wanted on a gun held half a metre from the eye.</summary>
        const float TexelHeld = 512f;
        /// <summary>Texels per metre under which even a far surface is a blur.</summary>
        const float TexelFar = 64f;
        /// <summary>One texture repeated this many times across one face, with nothing to vary it, shows its grid.</summary>
        const float RepeatsMax = 6f;
        /// <summary>URP's material validation (Rendering Debugger, default preset): albedo luminance outside this is invalid.</summary>
        const float AlbedoValidMin = 0.01f, AlbedoValidMax = 0.90f;
        /// <summary>Albedo variation inside small patches of the texture (4 x 4 samples, 8-16 texels) under this is flat colour
        /// blocks: a palette texture, every face one colour, however many colours there are.</summary>
        const float FineVariationMin = 0.02f;
        /// <summary>A gun held half a metre from the eye with fewer triangles than this is low-poly (first-person guns run 20-60k).</summary>
        const int HeldTrisMin = 15000;
        /// <summary>A material on less than this share of an object's surface can make it NEEDS WORK at worst, not FAKE.</summary>
        const float MinorShare = 0.05f;

        enum Verdict { Real, Uncertain, NeedsWork, Fake }

        static string Label(Verdict v) => v == Verdict.Real ? "REAL" : v == Verdict.NeedsWork ? "NEEDS WORK" : v == Verdict.Fake ? "FAKE" : "UNCERTAIN";

        /// <summary>
        /// What a real surface of a kind measures (linear albedo, smoothness), from published measurements of real materials
        /// (fresh whitewash about 0.8 and weathered 0.5-0.6, desert sand 0.3-0.5, asphalt 0.05-0.12, leaves 0.05-0.25). A kind
        /// is found from the material's name, then its texture's, then the object's, a word at a time: first by what it is
        /// made of, then (the words marked ~) by what it is ("FrameAluminium" is aluminium, "ShopShutterBlue" a shutter's
        /// painted metal). None found is "unknown", and is shown, not assumed fine.
        /// </summary>
        static readonly (string cls, string[] words, float aMin, float aMax, float sMin, float sMax, bool metal)[] Classes =
        {
            ("decal",          new[] { "weather", "grime", "stain", "decal", "streak", "soot", "overlay" },          0f,    1f,    0f,    1f,    false),
            ("glass",          new[] { "glass", "~window", "~windscreen", "~windshield", "~pane", "~lens" },          0f,    1f,    0.80f, 1f,    false),
            ("water",          new[] { "water", "~pool", "~fountain", "~pond" },                                     0f,    1f,    0.85f, 1f,    false),
            // "Skin" is also a variant's name in packs ("Shirt3_Skin2"): only a weak word, so the shirt is the shirt's.
            ("skin",           new[] { "flesh", "~skin", "~face", "~head", "~body", "~hand", "~arm" },               0.08f, 0.55f, 0.20f, 0.60f, false),
            ("hair",           new[] { "hair", "beard" },                                                           0.02f, 0.40f, 0.20f, 0.60f, false),
            ("fabric",         new[] { "fabric", "cloth", "carpet", "rug", "canvas", "textile", "~awning", "~curtain",
                                       "~cushion", "~pillow", "~mattress", "~flag", "~tent", "~shirt", "~pants", "~trouser",
                                       "~vest", "~cap", "~boot", "~glove", "~sleeve", "~towel", "~blanket", "~sofa" }, 0.03f, 0.75f, 0f,    0.35f, false),
            ("vegetation",     new[] { "leaf", "leaves", "bark", "grass", "foliage", "frond", "~palm", "~plant", "~bush",
                                       "~tree", "~trunk", "~hedge", "~flower", "~shrub", "~cactus" },             0.03f, 0.35f, 0.05f, 0.50f, false),
            ("car paint",      new[] { "carpaint", "~vehicle" },                                                    0.03f, 0.75f, 0.55f, 0.95f, false),
            ("rubber",         new[] { "rubber", "~tyre", "~tire", "~wheel" },                                      0.02f, 0.12f, 0.10f, 0.50f, false),
            ("plastic",        new[] { "plastic", "pvc", "~crate", "~bucket", "~jerrycan", "~canister", "~bottle" }, 0.03f, 0.80f, 0.30f, 0.85f, false),
            ("polished stone", new[] { "terrazzo", "ceramic", "porcelain", "tile", "polished", "~stallriser" },     0.04f, 0.80f, 0.45f, 0.95f, false),
            ("gun finish",     new[] { "vm", "rifle", "gun", "weapon", "receiver", "magazine", "muzzle", "ak", "uzi",
                                       "benelli", "bennelli", "pistol" },                                           0.02f, 0.25f, 0.25f, 0.70f, false),
            ("bare metal",     new[] { "chrome", "galvan", "zinc", "alu", "aluminium", "aluminum", "brass", "copper",
                                       "bronze" },                                                                  0.30f, 0.95f, 0.30f, 0.95f, true),
            ("painted metal",  new[] { "metal", "steel", "iron", "=rust", "rusty", "rusted", "corrugated", "~shutter", "~tank", "~railing",
                                       "~frame", "~pipe", "~dish", "~solar", "~lamp", "~lantern", "~pole", "~fence", "~fan" }, 0.03f, 0.80f, 0.20f, 0.75f, false),
            ("wood",           new[] { "wood", "timber", "plank", "~door", "~beam", "~pallet", "~shelf", "~furniture",
                                       "~table", "~chair", "~stool", "~wardrobe", "~bed", "~cabinet", "~ladder" }, 0.05f, 0.45f, 0.10f, 0.65f, false),
            ("asphalt",        new[] { "asphalt", "tarmac", "~road", "~highway", "~carriage" },                    0.04f, 0.18f, 0.05f, 0.40f, false),
            ("sand",           new[] { "sand", "dune", "dust", "dirt", "soil", "gravel", "~desert", "~ground", "~terrain",
                                       "~apron" },                                                                  0.20f, 0.55f, 0f,    0.25f, false),
            ("mud brick",      new[] { "mud", "adobe", "clay", "earth" },                                           0.15f, 0.45f, 0f,    0.20f, false),
            ("brick",          new[] { "brick" },                                                                   0.12f, 0.45f, 0f,    0.30f, false),
            ("concrete",       new[] { "concrete", "cement", "~foundation", "~kerb", "~curb", "~pavement", "~paving",
                                       "~paver", "~slab", "~block" },                                               0.15f, 0.55f, 0f,    0.35f, false),
            ("plaster",        new[] { "plaster", "render", "stucco", "wash", "paint", "~wall", "~ceiling", "~interior",
                                       "~inside" },                                                                 0.25f, 0.80f, 0f,    0.30f, false),
            ("stone",          new[] { "stone", "rock", "limestone", "sandstone", "ashlar", "rubble", "granite", "marble",
                                       "masonry", "~quoin", "~cornice", "~trim", "~sill", "~lintel", "~coping", "~plaza" }, 0.15f, 0.65f, 0f,    0.40f, false),
            ("ceramic ware",   new[] { "~vase", "~pot", "~bowl", "~jar", "~plate" },                               0.04f, 0.80f, 0.30f, 0.90f, false),
        };

        // ------------------------------------------------------------------
        // What is measured
        // ------------------------------------------------------------------
        sealed class Member
        {
            public Renderer renderer;      // null for prefab-asset members (guns) and terrain layers
            public Mesh mesh;
            public int sub;
            public Material mat;
            public Matrix4x4 toWorld;
            public bool hasCollider;
            public int lod;                 // 0 when not in a LOD group
            public string rendererPath;     // to find the renderer again after the scene is reopened
            public Renderer prefabRenderer; // a gun's or terrain tree's renderer, in its prefab asset
        }

        sealed class Item
        {
            public string key, name, group, source, assetPath, cls = "unknown";
            public readonly List<Member> members = new List<Member>();
            public readonly HashSet<Renderer> renderers = new HashSet<Renderer>();
            public int id;                  // the colour in the id pass
            // shape
            public long tris; public double area, axisArea, bevelLen, sharpLen, openLen, boxArea, shellSamples, thickSamples;
            public readonly List<float> thickness = new List<float>();
            public bool primitive, plainBox;
            // surface (area-weighted over its materials)
            public double texelWeighted, texelArea, repeatsP95;
            public bool vertexColourVariation;
            public readonly List<MatInfo> mats = new List<MatInfo>();
            public readonly Dictionary<Material, double> matArea = new Dictionary<Material, double>();
            // importance
            public double pixels; public int views, bestView = -1; public float minDist = float.MaxValue;
            public readonly List<(float share, int view)> viewShares = new List<(float, int)>();
            // rating
            public Verdict shape = Verdict.Real, surface = Verdict.Real, light = Verdict.Real, overall;
            public readonly List<string> reasons = new List<string>();
            public double importance;       // mean share of the screen over the route, in percent
            public string evidence;         // folder of the visual evidence, when made
            public readonly List<string> measured = new List<string>();   // what the light test measured
            // the kinds that are not plain renderers
            public TerrainLayer terrainLayer; public Terrain terrain; public string terrainName; public float layerShare;
            public int terrainTrees; public Material skyMaterial;
            public GameObject prefabAsset;  // a gun's first-person model, the arms
            public readonly HashSet<string> weapons = new HashSet<string>();

            public int Instances => renderers.Count;
            public double TrisPerM2 => area > 1e-6 ? tris / area : 0;
            public double BevelShare => bevelLen + sharpLen > 1e-6 ? bevelLen / (bevelLen + sharpLen) : 1;
            public double AxisShare => area > 1e-6 ? axisArea / area : 0;
            public double BoxShare => area > 1e-6 ? boxArea / area : 0;
            public double Texel => texelArea > 1e-6 ? texelWeighted / texelArea : 0;
        }

        sealed class TexStats
        {
            public int w, h; public string format;
            public float mean, std, p2, p50, p98;      // luminance (linear), base colour applied by the caller
            public Color meanColour;
            public float tiltMean, tiltP90;            // normal maps: degrees, before strength
            public float fine;                         // variation inside 4 x 4 sample blocks (std / mean): near 0 for a palette of flat colours
            public float r, g, a;                      // channel means (mask maps)
        }

        sealed class MatInfo
        {
            public Material mat; public string shader, cls = "unknown";
            public bool lit, transparent, alphaClip, emissive, lampLike;
            public Texture baseMap, normalMap, maskMap, aoMap, emissionMap, detailAlbedo, detailNormal;
            public Color baseColour; public Vector4 st;
            public float smoothness, metallic, bumpScale, emissionLum, cull = 2f;
            public TexStats albedo, normal, mask, ao;
            public float AlbedoMean => albedo != null ? albedo.mean : Lum(baseColour);
            public float AlbedoP98 => albedo != null ? albedo.p98 : Lum(baseColour);
            public float AlbedoP2 => albedo != null ? albedo.p2 : Lum(baseColour);
            public float Variation => albedo != null && albedo.mean > 1e-4f ? albedo.std / albedo.mean : 0f;
            public float Fine => albedo != null ? albedo.fine : 0f;
            public float Tilt => normal != null ? Mathf.Atan(Mathf.Tan(normal.tiltMean * Mathf.Deg2Rad) * bumpScale) * Mathf.Rad2Deg : 0f;
            public (int, int) BaseSize => baseMap != null ? (baseMap.width, baseMap.height) : (0, 0);
        }

        static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        // ------------------------------------------------------------------
        // Entry points
        // ------------------------------------------------------------------
        [MenuItem("FPSKit/Realism/Audit Arena")]
        static void AuditOpenArena()
        {
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path)) { EditorUtility.DisplayDialog("Realism audit", "Open an arena scene first.", "OK"); return; }
            if (scene.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            try { Audit(scene.path, ArenaName(scene.path), 20); }
            finally { EditorSceneManager.OpenScene(scene.path); }   // the audit swaps materials; the saved scene is put back
        }

        /// <summary>Batch: -fpskitTheme "Desert Outpost" (default), -fpskitRealismTop N (20).</summary>
        public static void Batch()
        {
            int code = 0;
            try
            {
                string theme = Arg("-fpskitTheme") ?? "Desert Outpost";
                string path = ScenePathFor(theme);
                int top = int.TryParse(Arg("-fpskitRealismTop"), out var t) ? t : 20;
                Audit(path, theme, top);
            }
            catch (Exception e) { Debug.LogError($"[FPSKitBatch] FAILED: realism audit: {e}"); code = 1; }
            FPSKitBatch.Exit(code);
        }

        static string Arg(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == flag) return args[i + 1];
            return null;
        }

        static string ScenePathFor(string theme)
        {
            string want = theme.Replace(" ", "");
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/FPSKit_Generated/Scenes" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileNameWithoutExtension(p), want, StringComparison.OrdinalIgnoreCase)) return p;
            }
            throw new ArgumentException($"no scene for \"{theme}\" in Assets/FPSKit_Generated/Scenes");
        }

        static string ArenaName(string scenePath) => Path.GetFileNameWithoutExtension(scenePath);

        static string _arena, _out;
        static readonly StringBuilder _log = new StringBuilder();

        static void Log(string line) { _log.AppendLine(line); Debug.Log("[FPSKit] realism: " + line); }

        /// <summary>
        /// Every tier's anti-aliasing and the current tier, as found. The audit judges at Medium and counts ids with
        /// multisampling off, and URP writes its asset's sample count through to the tier's QualitySettings, which the
        /// editor then saves: the first runs left ProjectSettings with Medium current and its anti-aliasing at 0.
        /// </summary>
        static (int current, int[] antiAliasing) SnapshotQuality()
        {
            int current = QualitySettings.GetQualityLevel();
            var aa = new int[QualitySettings.names.Length];
            for (int i = 0; i < aa.Length; i++) { QualitySettings.SetQualityLevel(i, false); aa[i] = QualitySettings.antiAliasing; }
            QualitySettings.SetQualityLevel(current, false);
            return (current, aa);
        }

        static void RestoreQuality((int current, int[] antiAliasing) was)
        {
            for (int i = 0; i < was.antiAliasing.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.antiAliasing = was.antiAliasing[i]; }
            QualitySettings.SetQualityLevel(was.current, true);
        }

        static void Audit(string scenePath, string theme, int top)
        {
            var quality = SnapshotQuality();
            try { AuditScene(scenePath, theme, top); }
            finally { RestoreQuality(quality); }
        }

        static void AuditScene(string scenePath, string theme, int top)
        {
            _log.Clear();
            _texCache.Clear();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            _arena = theme.Replace(" ", "");
            _out = $"Build/Realism/{_arena}";
            if (Directory.Exists(_out)) Directory.Delete(_out, true);
            Directory.CreateDirectory(_out);

            _scenePath = scenePath;
            _geo.Clear(); _texelMats.Clear();
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            // Judged at the tier it is played on (the target: 60 fps on Medium).
            SetTier();
            Log($"{theme}: {scenePath}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, pipeline {GraphicsSettings.currentRenderPipeline?.name}");

            var items = Collect(scene);
            _all = items;
            Log($"{items.Count} objects from {items.Sum(i => i.Instances)} renderers ({clock.Elapsed.TotalSeconds:0}s)");
            MeasureGeometry(items);
            Log($"geometry measured ({clock.Elapsed.TotalSeconds:0}s)");
            MeasureMaterials(items);
            Log($"materials measured ({clock.Elapsed.TotalSeconds:0}s)");
            MeasureThickness(items);
            Log($"thickness sampled ({clock.Elapsed.TotalSeconds:0}s)");

            var route = Route(theme);
            Log($"route: {route.Count} views over {RouteLength:0} m");
            Importance(items, route);
            foreach (var it in items) Rate(it);
            Log($"importance and ratings ({clock.Elapsed.TotalSeconds:0}s)");

            var ranked = items.OrderByDescending(i => i.importance).ToList();
            // The most seen objects of the world, and every gun and the arms (one gun is held at a time: ranked among the
            // world's objects they would push it out of the list).
            var chosen = ranked.Where(i => i.overall != Verdict.Real && i.source != "player weapon").Take(top)
                               .Concat(ranked.Where(i => i.overall != Verdict.Real && i.source == "player weapon")).ToList();
            // The scene's materials were swapped for the id pass: put the saved scene back before anything is drawn as itself.
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            SetTier();
            Relink(items);
            MakeEvidence(chosen, route);
            foreach (var it in chosen) Rate(it);   // with what the light test measured
            Log($"evidence for {chosen.Count} ({clock.Elapsed.TotalSeconds:0}s)");

            WriteCsv(ranked);
            WriteReport(theme, scenePath, ranked, chosen, route);
            Log($"done in {clock.Elapsed.TotalMinutes:0.0} min");
        }

        // ------------------------------------------------------------------
        // Collecting
        // ------------------------------------------------------------------
        static List<Item> Collect(Scene scene)
        {
            var items = new Dictionary<string, Item>();
            var lodOf = new Dictionary<Renderer, int>();
            foreach (var g in Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
            {
                var lods = g.GetLODs();
                for (int i = 0; i < lods.Length; i++) foreach (var r in lods[i].renderers) if (r != null) lodOf[r] = i;
            }
            int skipped = 0, ui = LayerMask.NameToLayer("UI");
            var groups = new Dictionary<string, int>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r is BillboardRenderer || r.gameObject.layer == ui)
                    { skipped++; continue; }
                    if (r.GetComponentInParent<PlayerMotor>() != null) continue;   // the player's arms and gun: audited from the guns below
                    var mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) { skipped++; continue; }
                    var mats = r.sharedMaterials;
                    string group = GroupOf(r.transform);
                    groups[group] = groups.TryGetValue(group, out var gc) ? gc + 1 : 1;
                    bool collider = r.GetComponent<Collider>() != null;
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        var mat = s < mats.Length ? mats[s] : null;
                        if (mat == null) continue;
                        var it = Get(items, KeyFor(r, mesh, mat, group, out string source, out string name, out string path));
                        it.source ??= source; it.name ??= name; it.group ??= group; it.assetPath ??= path;
                        it.primitive |= source == "unity primitive";
                        it.members.Add(new Member { renderer = r, mesh = mesh, sub = s, mat = mat, toWorld = r.localToWorldMatrix, hasCollider = collider,
                                                    lod = lodOf.TryGetValue(r, out var l) ? l : 0, rendererPath = PathOf(r.transform) });
                        it.renderers.Add(r);
                    }
                }
            Log("groups: " + string.Join(", ", groups.OrderByDescending(g => g.Value).Select(g => $"{g.Key} {g.Value}")));
            if (skipped > 0) Log($"{skipped} renderers not audited (particles, lines, UI, disabled, no mesh)");

            CollectTerrain(items);
            CollectSky(items);
            CollectGuns(items);
            int id = 1;
            foreach (var it in items.Values) it.id = id++;
            return items.Values.ToList();
        }

        static Item Get(Dictionary<string, Item> items, string key)
        {
            if (!items.TryGetValue(key, out var it)) items[key] = it = new Item { key = key };
            return it;
        }

        /// <summary>The builder's group an object is in: the child of the arena root (and the landmark under Landmarks).</summary>
        static string GroupOf(Transform t)
        {
            var path = new List<string>();
            for (var p = t; p != null; p = p.parent) path.Insert(0, p.name);
            int a = path.IndexOf("Arena");
            int i = a >= 0 ? a + 1 : 0;
            if (i >= path.Count) return path[path.Count - 1];
            string g = path[i];
            if ((g == "Landmarks" || g == "Town") && i + 1 < path.Count - 1) g += "/" + path[i + 1];
            return g;
        }

        static string KeyFor(Renderer r, Mesh mesh, Material mat, string group, out string source, out string name, out string path)
        {
            string meshPath = AssetDatabase.GetAssetPath(mesh);
            if (meshPath == "Library/unity default resources" || meshPath.StartsWith("Resources/unity_builtin"))
            {
                source = "unity primitive"; name = $"{mesh.name} ({mat.name})"; path = meshPath;
                return $"prim|{mesh.name}|{mat.name}";
            }
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(r.gameObject);
            string prefab = root != null ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) : null;
            if (!string.IsNullOrEmpty(prefab))
            {
                source = prefab.StartsWith("Assets/FPSKit_Generated") ? "generated prefab" : r is SkinnedMeshRenderer ? "imported character" : "imported model";
                name = Path.GetFileNameWithoutExtension(prefab); path = prefab;
                return $"model|{prefab}";
            }
            if (!string.IsNullOrEmpty(meshPath) && !meshPath.StartsWith("Assets/FPSKit_Generated") && !meshPath.EndsWith(".unity"))
            {
                source = r is SkinnedMeshRenderer ? "imported character" : "imported model";
                name = $"{Path.GetFileNameWithoutExtension(meshPath)}/{mesh.name}"; path = meshPath;
                return $"model|{meshPath}|{mesh.name}";
            }
            source = "generated mesh"; name = $"{group}: {mat.name}"; path = AssetDatabase.GetAssetPath(mat);
            return $"gen|{group}|{mat.name}";
        }

        /// <summary>Each terrain layer is an object (its own texture, scale and light response); trees and details too.</summary>
        static void CollectTerrain(Dictionary<string, Item> items)
        {
            foreach (var terrain in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                var data = terrain.terrainData;
                if (data == null) continue;
                var layers = data.terrainLayers;
                // The layers' shares of the terrain, from its splat maps.
                var share = new float[layers.Length];
                int aw = data.alphamapWidth, ah = data.alphamapHeight;
                if (layers.Length > 0 && aw > 0)
                {
                    int step = Mathf.Max(1, aw / 128);
                    var maps = data.GetAlphamaps(0, 0, aw, ah);
                    double total = 0;
                    for (int y = 0; y < ah; y += step) for (int x = 0; x < aw; x += step) for (int l = 0; l < layers.Length; l++) { share[l] += maps[y, x, l]; total += maps[y, x, l]; }
                    for (int l = 0; l < layers.Length; l++) share[l] = (float)(share[l] / Math.Max(total, 1e-6));
                }
                int res = data.heightmapResolution;
                for (int l = 0; l < layers.Length; l++)
                {
                    var layer = layers[l];
                    if (layer == null) continue;
                    var it = Get(items, $"terrain|{terrain.name}|{layer.name}");
                    it.source = "terrain"; it.name = $"Ground: {layer.name}"; it.group = "Terrain"; it.assetPath = AssetDatabase.GetAssetPath(layer);
                    it.tris = (long)((res - 1) * (res - 1) * 2 * share[l]);
                    it.area = data.size.x * data.size.z * share[l];
                    it.axisArea = 0; it.bevelLen = 1; it.sharpLen = 0;   // a heightfield has no box edges to bevel
                    it.terrainLayer = layer; it.terrain = terrain; it.terrainName = terrain.name; it.layerShare = share[l];
                }
                foreach (var proto in data.treePrototypes)
                    if (proto.prefab != null)
                    {
                        var path = AssetDatabase.GetAssetPath(proto.prefab);
                        var it = Get(items, $"model|{path}");
                        it.source ??= "imported model (terrain tree)"; it.name ??= Path.GetFileNameWithoutExtension(path); it.group ??= "Terrain trees"; it.assetPath ??= path;
                        AddPrefabMembers(it, proto.prefab);
                        it.terrainTrees += data.treeInstances.Count(t => data.treePrototypes[t.prototypeIndex] == proto);
                    }
            }
        }

        static void CollectSky(Dictionary<string, Item> items)
        {
            var sky = RenderSettings.skybox;
            if (sky == null) return;
            var it = Get(items, $"sky|{sky.name}");
            it.source = "sky"; it.name = $"Sky: {sky.name}"; it.group = "Sky"; it.assetPath = AssetDatabase.GetAssetPath(sky);
            it.skyMaterial = sky;
        }

        const string ArmsPrefab = "Assets/FPSKit_Generated/ViewModels/FP_Arms.prefab";

        /// <summary>Every gun the player can carry (its first-person model), and the first-person arms ViewModelRig puts on it.</summary>
        static void CollectGuns(Dictionary<string, Item> items)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:WeaponData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<WeaponData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || data.viewModel == null) continue;
                var path = AssetDatabase.GetAssetPath(data.viewModel);
                var it = Get(items, $"weapon|{path}");
                it.source = "player weapon"; it.group = "Player"; it.assetPath = path; it.prefabAsset = data.viewModel;
                it.name ??= $"Gun: {data.name} ({Path.GetFileNameWithoutExtension(path)})";
                it.weapons.Add(data.name);
                if (it.members.Count == 0) AddPrefabMembers(it, data.viewModel);
            }
            var arms = AssetDatabase.LoadAssetAtPath<GameObject>(ArmsPrefab);
            if (arms != null)
            {
                var it = Get(items, "weapon|arms");
                it.source = "player weapon"; it.group = "Player"; it.assetPath = ArmsPrefab; it.prefabAsset = arms;
                it.name = "First-person arms (FP_Arms)";
                AddPrefabMembers(it, arms);
            }
        }

        static void AddPrefabMembers(Item it, GameObject prefab)
        {
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var mats = r.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                    if (mats[s] != null) it.members.Add(new Member { mesh = mesh, sub = s, mat = mats[s], toWorld = r.transform.localToWorldMatrix, prefabRenderer = r });
                it.renderers.Add(r);
            }
        }

        /// <summary>After the scene is opened again (the id pass swapped its materials), each member's renderer is found anew
        /// by its path and the members' materials are the saved ones.</summary>
        static void Relink(List<Item> items)
        {
            var byPath = new Dictionary<string, Renderer>();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    byPath[PathOf(r.transform)] = r;
            int lost = 0;
            foreach (var it in items)
            {
                it.renderers.Clear();
                foreach (var m in it.members)
                {
                    // A gun's or a terrain tree's renderers are in its prefab, not the scene.
                    if (m.rendererPath == null) { if (m.prefabRenderer != null) it.renderers.Add(m.prefabRenderer); continue; }
                    m.renderer = byPath.TryGetValue(m.rendererPath, out var r) ? r : null;
                    if (m.renderer == null) { lost++; continue; }
                    it.renderers.Add(m.renderer);
                    // Meshes kept in the scene file are new objects once it is reopened.
                    var mesh = m.renderer is SkinnedMeshRenderer smr ? smr.sharedMesh : m.renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh != null) m.mesh = mesh;
                    var mats = m.renderer.sharedMaterials;
                    if (m.sub < mats.Length && mats[m.sub] != null) m.mat = mats[m.sub];
                }
                if (it.terrainName != null) it.terrain = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == it.terrainName);
            }
            if (lost > 0) Log($"{lost} members not found again after reopening the scene");
        }

        /// <summary>A renderer's place in the scene by sibling indices (names repeat; indices are the same when it is reopened).</summary>
        static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.GetSiblingIndex().ToString());
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.GetSiblingIndex() + "/");
            return sb.ToString();
        }
    }
}
#endif
