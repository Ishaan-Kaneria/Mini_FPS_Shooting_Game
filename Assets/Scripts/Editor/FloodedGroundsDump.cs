#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Read-only report on the Flooded Grounds demo scene, so its "look recipe" is measured
    /// rather than guessed. Opens the scene, never saves it, writes a text file.
    /// Run: Tools/unity-batch.sh FPSKit.EditorTools.FloodedGroundsDump.Run -fpskitOut path.txt
    /// </summary>
    public static class FloodedGroundsDump
    {
        const string ScenePath = "Assets/Flooded_Grounds/Scenes/Scene_A.unity";
        const string PackRoot = "Assets/Flooded_Grounds";

        [MenuItem("Tools/MiniFPS/Fairground/Dump Flooded Grounds Look")]
        public static void Run()
        {
            string outPath = "Logs/flooded-grounds-dump.txt";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-fpskitOut") outPath = args[i + 1];

            var sb = new StringBuilder();
            try
            {
                Materials(sb);
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Settings(sb);
                Lights(sb);
                Cameras(sb);
                Terrains(sb);
                Census(sb);
            }
            catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("[FPSKitBatch] Flooded Grounds dump written to " + outPath);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void Materials(StringBuilder sb)
        {
            sb.AppendLine("== PACK MATERIALS BY SHADER ==");
            var byShader = new SortedDictionary<string, List<string>>();
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { PackRoot }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null) continue;
                string sn = m.shader != null ? m.shader.name : "<null>";
                if (!byShader.TryGetValue(sn, out var l)) byShader[sn] = l = new List<string>();
                l.Add(Path.GetFileNameWithoutExtension(p));
            }
            foreach (var kv in byShader)
                sb.AppendLine($"{kv.Key}  x{kv.Value.Count}: {string.Join(", ", kv.Value)}");

            foreach (var name in new[] { "BGR_Water", "BGR_Sky1", "NAT_Grass", "NAT_Bush1", "PROP_Car1_Rusty", "BLD_Structures1" })
            {
                var g = AssetDatabase.FindAssets(name + " t:Material", new[] { PackRoot }).FirstOrDefault();
                if (g == null) continue;
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
                sb.AppendLine($"-- {m.name} ({m.shader.name}) renderQueue={m.renderQueue}");
                var sh = m.shader;
                for (int i = 0; i < sh.GetPropertyCount(); i++)
                {
                    string pn = sh.GetPropertyName(i);
                    switch (sh.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Color: sb.AppendLine($"   {pn} = {m.GetColor(pn)}"); break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range: sb.AppendLine($"   {pn} = {m.GetFloat(pn)}"); break;
                        case ShaderPropertyType.Texture:
                            var t = m.GetTexture(pn);
                            sb.AppendLine($"   {pn} = {(t ? t.name + " " + t.width + "x" + t.height : "-")}"); break;
                    }
                }
            }
        }

        static void Settings(StringBuilder sb)
        {
            sb.AppendLine("\n== RENDER SETTINGS ==");
            sb.AppendLine($"fog={RenderSettings.fog} mode={RenderSettings.fogMode} color={RenderSettings.fogColor} density={RenderSettings.fogDensity} start={RenderSettings.fogStartDistance} end={RenderSettings.fogEndDistance}");
            sb.AppendLine($"ambientMode={RenderSettings.ambientMode} ambient={RenderSettings.ambientLight} sky={RenderSettings.ambientSkyColor} equator={RenderSettings.ambientEquatorColor} ground={RenderSettings.ambientGroundColor} intensity={RenderSettings.ambientIntensity}");
            sb.AppendLine($"reflectionMode={RenderSettings.defaultReflectionMode} reflectionIntensity={RenderSettings.reflectionIntensity} bounces={RenderSettings.reflectionBounces} custom={(RenderSettings.customReflectionTexture ? RenderSettings.customReflectionTexture.name : "-")}");
            sb.AppendLine($"sun={(RenderSettings.sun ? RenderSettings.sun.name : "-")} haloStrength={RenderSettings.haloStrength} flareFade={RenderSettings.flareFadeSpeed}");
            var sky = RenderSettings.skybox;
            sb.AppendLine($"skybox={(sky ? sky.name + " (" + sky.shader.name + ")" : "-")}");
            if (sky != null)
            {
                foreach (var pn in new[] { "_Tint", "_Exposure", "_Rotation", "_RotSpeed" })
                    if (sky.HasProperty(pn)) sb.AppendLine($"   {pn} = {(pn == "_Tint" ? sky.GetColor(pn).ToString() : sky.GetFloat(pn).ToString())}");
                var cube = sky.HasProperty("_Tex") ? sky.GetTexture("_Tex") : null;
                if (cube) sb.AppendLine($"   _Tex = {cube.name} {cube.width}x{cube.height}");
            }
            sb.AppendLine($"QualitySettings: shadows={QualitySettings.shadows} distance={QualitySettings.shadowDistance} cascades={QualitySettings.shadowCascades} colorSpace={PlayerSettings.colorSpace}");
            sb.AppendLine($"Lightmapping: bakedGI={Lightmapping.bakedGI} realtimeGI={Lightmapping.realtimeGI} lightmaps={LightmapSettings.lightmaps.Length} probes={(LightmapSettings.lightProbes ? LightmapSettings.lightProbes.count : 0)}");
        }

        static void Lights(StringBuilder sb)
        {
            sb.AppendLine("\n== LIGHTS ==");
            var all = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            sb.AppendLine($"total={all.Length}  byType: " + string.Join(", ", all.GroupBy(l => l.type).Select(g => g.Key + "=" + g.Count())));
            foreach (var l in all.Where(l => l.type == LightType.Directional))
                sb.AppendLine($"DIR {l.name} color={l.color} intensity={l.intensity} shadows={l.shadows} strength={l.shadowStrength} euler={l.transform.eulerAngles} mode={l.lightmapBakeType} flare={(l.flare ? l.flare.name : "-")}");
            foreach (var l in all.Where(l => l.type != LightType.Directional).Take(25))
                sb.AppendLine($"{l.type} {l.name} color={l.color} intensity={l.intensity} range={l.range} shadows={l.shadows} mode={l.lightmapBakeType}");
        }

        static void Cameras(StringBuilder sb)
        {
            sb.AppendLine("\n== CAMERAS / POST ==");
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                sb.AppendLine($"CAM {c.name} clear={c.clearFlags} fov={c.fieldOfView} far={c.farClipPlane} hdr={c.allowHDR} pos={c.transform.position}");
                foreach (var comp in c.GetComponents<Component>())
                {
                    if (comp == null) { sb.AppendLine("   <missing script>"); continue; }
                    sb.AppendLine("   " + comp.GetType().FullName);
                }
            }
            var prof = AssetDatabase.LoadAssetAtPath<ScriptableObject>(PackRoot + "/Scenes/Postprocess_FloodedGrounds.asset");
            sb.AppendLine("PostProcess profile: " + (prof ? prof.GetType().FullName : "NOT LOADABLE (script missing)"));
            if (prof) sb.AppendLine(EditorJsonUtility.ToJson(prof, true));
        }

        static void Terrains(StringBuilder sb)
        {
            sb.AppendLine("\n== TERRAIN ==");
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                var d = t.terrainData;
                sb.AppendLine($"Terrain {t.name} size={d.size} heightRes={d.heightmapResolution} alphaRes={d.alphamapResolution} baseRes={d.baseMapResolution} detailRes={d.detailResolution}");
                sb.AppendLine($"  material={(t.materialTemplate ? t.materialTemplate.shader.name : "default")} basemapDist={t.basemapDistance} detailDist={t.detailObjectDistance} detailDensity={t.detailObjectDensity} treeDist={t.treeDistance} billboardStart={t.treeBillboardDistance} crossFade={t.treeCrossFadeLength} maxMeshTrees={t.treeMaximumFullLODCount} drawInstanced={t.drawInstanced} heightmapPixelError={t.heightmapPixelError}");
                var layers = d.terrainLayers;
                sb.AppendLine($"  layers={layers.Length}");
                foreach (var l in layers.Where(l => l != null))
                    sb.AppendLine($"    {l.name}: diffuse={(l.diffuseTexture ? l.diffuseTexture.name : "-")} normal={(l.normalMapTexture ? l.normalMapTexture.name : "-")} mask={(l.maskMapTexture ? l.maskMapTexture.name : "-")} tile={l.tileSize} normalScale={l.normalScale} smooth={l.smoothness} metallic={l.metallic}");
                sb.AppendLine($"  trees: prototypes={d.treePrototypes.Length} instances={d.treeInstanceCount}");
                for (int i = 0; i < d.treePrototypes.Length; i++)
                {
                    var tp = d.treePrototypes[i].prefab;
                    int n = d.treeInstances.Count(x => x.prototypeIndex == i);
                    sb.AppendLine($"    [{i}] {(tp ? tp.name : "-")} x{n}");
                }
                sb.AppendLine($"  detail prototypes={d.detailPrototypes.Length}");
                for (int i = 0; i < d.detailPrototypes.Length; i++)
                {
                    var dp = d.detailPrototypes[i];
                    long total = 0;
                    var map = d.GetDetailLayer(0, 0, d.detailWidth, d.detailHeight, i);
                    foreach (var v in map) total += v;
                    sb.AppendLine($"    [{i}] {(dp.usePrototypeMesh ? "mesh " + (dp.prototype ? dp.prototype.name : "-") : "tex " + (dp.prototypeTexture ? dp.prototypeTexture.name : "-"))} size {dp.minWidth}-{dp.maxWidth} x {dp.minHeight}-{dp.maxHeight} noise={dp.noiseSpread} density={dp.density} renderMode={dp.renderMode} healthy={dp.healthyColor} dry={dp.dryColor} sum={total}");
                }
            }
        }

        static void Census(StringBuilder sb)
        {
            sb.AppendLine("\n== SCENE CENSUS ==");
            var rends = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            sb.AppendLine($"renderers={rends.Length}  particleSystems={Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Length}  audioSources={Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length}  lodGroups={Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None).Length}  reflectionProbes={Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).Length}");
            sb.AppendLine("Top prefab/mesh names by count:");
            var counts = new Dictionary<string, int>();
            foreach (var r in rends)
            {
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(r.gameObject);
                string n = root ? root.name : r.gameObject.name;
                counts[n] = counts.TryGetValue(n, out var c) ? c + 1 : 1;
            }
            foreach (var kv in counts.OrderByDescending(k => k.Value).Take(45)) sb.AppendLine($"   {kv.Value,4}  {kv.Key}");
            sb.AppendLine("Scene materials by shader:");
            var sh = new Dictionary<string, int>();
            foreach (var r in rends) foreach (var m in r.sharedMaterials)
                if (m && m.shader) sh[m.shader.name] = sh.TryGetValue(m.shader.name, out var c) ? c + 1 : 1;
            foreach (var kv in sh.OrderByDescending(k => k.Value)) sb.AppendLine($"   {kv.Value,4}  {kv.Key}");

            var b = new Bounds(); bool first = true;
            foreach (var r in rends) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            sb.AppendLine("Scene bounds: " + b);
            sb.AppendLine("Water renderers:");
            foreach (var r in rends.Where(r => r.sharedMaterials.Any(m => m && m.shader && m.shader.name.Contains("Water"))))
                sb.AppendLine($"   {r.name} pos={r.transform.position} bounds={r.bounds.size}");
            sb.AppendLine("Particle systems:");
            foreach (var p in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Take(20))
                sb.AppendLine($"   {p.name} max={p.main.maxParticles} rate={p.emission.rateOverTime.constant} life={p.main.startLifetime.constant} size={p.main.startSize.constant} mat={(p.GetComponent<Renderer>() && p.GetComponent<Renderer>().sharedMaterial ? p.GetComponent<Renderer>().sharedMaterial.name : "-")}");
        }
    }
}
#endif
