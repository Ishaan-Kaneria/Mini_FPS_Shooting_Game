#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Read-only audit of what the project actually uses. Nothing is moved or deleted here.
    ///
    /// Two tiers: <b>shipped</b> is everything the build scenes, the Resources folders, the settings and the generated
    /// content reach; <b>generator</b> is what only the scene builders load (by path or by name), so archiving it would
    /// break a rebuild of an arena. Whatever is in neither is unused. The lists are written to a report folder.
    /// </summary>
    public static class FPSKitAssetAudit
    {
        static readonly string[] RootDirs =
        {
            "Assets/Settings", "Assets/Plugins", "Assets/TextMesh Pro", "Assets/UI", "Assets/Audio",
            "Assets/WebGLTemplates", "Assets/URPDefaultResources", "Assets/Scripts", "Assets/FPSKit_Generated"
        };

        public static string ReportDir = "Logs/AssetAudit";

        [MenuItem("Tools/MiniFPS/Assets/Audit Unused (read-only)")]
        public static void Run() => Debug.Log("[FPSKit] " + Audit());

        public static string Audit()
        {
            string[] files = Directory.GetFiles("Assets", "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta")).Select(f => f.Replace('\\', '/')).ToArray();
            var fileSet = new HashSet<string>(files);

            var shipped = new HashSet<string>();
            var roots = new List<string>();
            foreach (var s in EditorBuildSettings.scenes) if (s.enabled) roots.Add(s.path);
            foreach (var f in files)
            {
                if (f.Contains("/Resources/")) { roots.Add(f); continue; }
                foreach (var rd in RootDirs) if (f.StartsWith(rd + "/")) { roots.Add(f); break; }
            }
            AddDeps(roots.Distinct().Where(fileSet.Contains), shipped);

            // What the builders load: "Assets/..." literals (cut at an interpolation) and bare names matching a file, folder or id.
            var literals = new HashSet<string>();
            var pathRoots = new HashSet<string>();
            foreach (var cs in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
                foreach (Match m in Regex.Matches(File.ReadAllText(cs), "\"([^\"\\\\\\n]{3,160})\""))
                {
                    string v = m.Groups[1].Value;
                    int brace = v.IndexOf('{');
                    if (brace > 0) v = v.Substring(0, brace);
                    if (v.StartsWith("Assets/")) pathRoots.Add(v.TrimEnd('/')); else literals.Add(v);
                }

            // Exact references only. A folder literal is a search scope (FindAssets(name, folder)), not a reference to everything in it,
            // and a folder's name is not a reference to the files inside it. Names shorter than 5 characters are too generic to count.
            var generator = new HashSet<string>();
            foreach (var f in files)
            {
                if (pathRoots.Contains(f)) { generator.Add(f); continue; }
                string bn = Path.GetFileNameWithoutExtension(f), fn = Path.GetFileName(f);
                if ((bn.Length >= 5 && literals.Contains(bn)) || (fn.Length >= 5 && literals.Contains(fn))) generator.Add(f);
            }
            var withGen = new HashSet<string>(shipped);
            AddDeps(generator.Where(fileSet.Contains), withGen);

            long Sz(string f) { try { return new FileInfo(f).Length; } catch { return 0; } }
            string Group(string f)
            {
                var p = f.Split('/');
                if (p.Length > 3 && (p[1] == "Flooded_Grounds" || p[1] == "Fairground")) return p[1] + "/" + p[2];
                return p[1];
            }

            Directory.CreateDirectory(ReportDir);
            var sb = new StringBuilder("group | files | MB | shipped MB | generator-only MB | unused files | unused MB\n");
            long totAll = 0, totUn = 0; int totUnFiles = 0;
            var unused = new List<string>();
            foreach (var g in files.GroupBy(Group).OrderByDescending(g => g.Sum(Sz)))
            {
                long all = g.Sum(Sz), s1 = g.Where(shipped.Contains).Sum(Sz), s2 = g.Where(f => !shipped.Contains(f) && withGen.Contains(f)).Sum(Sz);
                var un = g.Where(f => !withGen.Contains(f)).ToList();
                long u = un.Sum(Sz);
                totAll += all; totUn += u; totUnFiles += un.Count; unused.AddRange(un);
                sb.AppendLine($"{g.Key} | {g.Count()} | {all >> 20} | {s1 >> 20} | {s2 >> 20} | {un.Count} | {u >> 20}");
            }
            sb.AppendLine($"TOTAL | {files.Length} | {totAll >> 20} | | | {totUnFiles} | {totUn >> 20}");
            sb.AppendLine($"(literals scanned {literals.Count}, path roots {pathRoots.Count})");

            File.WriteAllLines($"{ReportDir}/unused.txt", unused.Select(f => Sz(f) + "\t" + f));
            File.WriteAllLines($"{ReportDir}/generator_only.txt", files.Where(f => !shipped.Contains(f) && withGen.Contains(f)).Select(f => Sz(f) + "\t" + f));
            File.WriteAllLines($"{ReportDir}/resources_shipped.txt", files.Where(f => f.Contains("/Resources/")).Select(f => Sz(f) + "\t" + f));
            File.WriteAllText($"{ReportDir}/summary.txt", sb.ToString());
            return sb.ToString();
        }

        /// <summary>Prefabs with a missing script or a renderer with an empty material slot. Scenes are checked one at a time by ScanScenes.</summary>
        public static string ScanPrefabs()
        {
            var sb = new StringBuilder();
            int checkedCount = 0, missingScripts = 0, missingMats = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                checkedCount++;
                int ms = 0, mm = 0;
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    ms += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    var r = t.GetComponent<Renderer>();
                    if (r != null) foreach (var m in r.sharedMaterials) if (m == null) mm++;
                }
                if (ms > 0 || mm > 0) { missingScripts += ms; missingMats += mm; sb.AppendLine($"{path}\tmissing scripts {ms}\tempty material slots {mm}"); }
            }
            Directory.CreateDirectory(ReportDir);
            File.WriteAllText($"{ReportDir}/missing_prefabs.txt", sb.ToString());
            return $"prefabs checked {checkedCount}; missing scripts {missingScripts}, empty material slots {missingMats}";
        }

        /// <summary>Opens each build scene in turn (one at a time, nothing saved) and counts missing scripts and empty material slots.</summary>
        public static string ScanScenes()
        {
            var sb = new StringBuilder();
            foreach (var bs in EditorBuildSettings.scenes)
            {
                if (!bs.enabled) continue;
                var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(bs.path, UnityEditor.SceneManagement.OpenSceneMode.Single);
                int objects = 0, ms = 0, mm = 0;
                var where = new Dictionary<string, int>();
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        objects++;
                        int m1 = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                        int m2 = 0;
                        var r = t.GetComponent<Renderer>();
                        if (r != null) foreach (var m in r.sharedMaterials) if (m == null) m2++;
                        if (m1 + m2 == 0) continue;
                        ms += m1; mm += m2;
                        string key = t.name;
                        where[key] = (where.TryGetValue(key, out int n) ? n : 0) + m1 + m2;
                    }
                sb.AppendLine($"{Path.GetFileName(bs.path)}: {objects} objects, missing scripts {ms}, empty material slots {mm}" + (where.Count > 0 ? "  e.g. " + string.Join(", ", where.OrderByDescending(k => k.Value).Take(4).Select(k => $"{k.Key} x{k.Value}")) : ""));
            }
            Directory.CreateDirectory(ReportDir);
            File.WriteAllText($"{ReportDir}/missing_scenes.txt", sb.ToString());
            return sb.ToString();
        }

        static void AddDeps(IEnumerable<string> roots, HashSet<string> into)
        {
            foreach (var r in roots)
            {
                into.Add(r);
                foreach (var d in AssetDatabase.GetDependencies(r, true)) into.Add(d);
            }
        }
    }
}
#endif
