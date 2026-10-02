#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Makes URP copies of the Flooded Grounds materials, trees and grass in Assets/Fairground.
    /// The pack itself is never modified. Our copies still point at the pack's textures and meshes
    /// (the pack is not committed), so a fresh clone needs the pack re-imported from the Asset Store.
    ///   Standard                      -> URP Lit (Unity's own StandardUpgrader)
    ///   Flooded_Grounds/PBR_TopBlend  -> Fairground/FG_TopBlend_URP
    ///   Flooded_Grounds/PBR_Water     -> Fairground/FG_Water_URP
    ///   Flooded_Grounds/Triplanar_*   -> Fairground/FG_Triplanar_URP
    ///   Tree Creator trees            -> baked mesh kept, bark = URP Lit, leaves = URP Lit alpha-clipped
    /// Not handled yet (Step 4): legacy particle materials, the rotating skybox.
    /// </summary>
    public static class FairgroundMaterialConverter
    {
        public const string PackMaterials = "Assets/Flooded_Grounds/Content/Materials";
        public const string PackTrees = "Assets/Flooded_Grounds/Prefabs/Nature/Trees";
        public const string PackGrass = "Assets/Flooded_Grounds/Prefabs/Nature/Grass";
        public const string OutMaterials = "Assets/Fairground/Materials";
        public const string OutPrefabs = "Assets/Fairground/Prefabs";

        [MenuItem("Tools/MiniFPS/Fairground/Convert Flooded Grounds Materials")]
        public static void ConvertAll()
        {
            EnsureFolder(OutMaterials);
            int made = 0, skipped = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { PackMaterials }))
            {
                var src = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (src != null && Converted(src) != null) made++; else skipped++;
            }
            ConvertTrees();
            ConvertGrass();
            AssetDatabase.SaveAssets();
            Debug.Log($"[FPSKit] Fairground materials: {made} converted, {skipped} skipped (particles, sky, debug).");
        }

        /// <summary>Our URP copy of a pack material, made on first request. Null if it is not meant to be converted.</summary>
        public static Material Converted(Material src)
        {
            if (src == null) return null;
            EnsureFolder(OutMaterials);
            string path = $"{OutMaterials}/{src.name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            string sn = src.shader != null ? src.shader.name : "";
            Material dst = null;
            switch (sn)
            {
                case "Standard":
                    dst = new Material(src);
                    AssetDatabase.CreateAsset(dst, path);
                    MaterialUpgrader.Upgrade(dst, new StandardUpgrader("Standard"), MaterialUpgrader.UpgradeFlags.None);
                    dst.enableInstancing = true;
                    break;
                case "Flooded_Grounds/PBR_TopBlend":
                    dst = CopyInto(src, "Fairground/FG_TopBlend_URP", path);
                    dst.enableInstancing = true;
                    break;
                case "Flooded_Grounds/PBR_Water":
                    dst = CopyInto(src, "Fairground/FG_Water_URP", path);
                    break;
                case "Flooded_Grounds/Triplanar_BumpSpec":
                    dst = CopyInto(src, "Fairground/FG_Triplanar_URP", path);
                    dst.enableInstancing = true;
                    break;
                default:
                    return null;
            }
            EditorUtility.SetDirty(dst);
            return dst;
        }

        static Material CopyInto(Material src, string shaderName, string path)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new Exception("Shader not found: " + shaderName);
            var dst = new Material(shader);
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string pn = shader.GetPropertyName(i);
                if (!src.HasProperty(pn)) continue;
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color: dst.SetColor(pn, src.GetColor(pn)); break;
                    case ShaderPropertyType.Vector: dst.SetVector(pn, src.GetVector(pn)); break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range: dst.SetFloat(pn, src.GetFloat(pn)); break;
                    case ShaderPropertyType.Texture:
                        dst.SetTexture(pn, src.GetTexture(pn));
                        dst.SetTextureScale(pn, src.GetTextureScale(pn));
                        dst.SetTextureOffset(pn, src.GetTextureOffset(pn));
                        break;
                }
            }
            AssetDatabase.CreateAsset(dst, path);
            return dst;
        }

        // ------------------------------------------------------------------ trees

        /// <summary>
        /// Tree Creator shaders do not exist in URP. Each tree prefab already holds one baked mesh with two
        /// submeshes (bark, leaves) and atlas textures, so we keep the mesh, drop the Tree component and
        /// swap in URP Lit: opaque bark, alpha-clipped two-sided leaves.
        /// </summary>
        public static void ConvertTrees()
        {
            EnsureFolder(OutPrefabs + "/Trees");
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PackTrees }))
            {
                string srcPath = AssetDatabase.GUIDToAssetPath(guid);
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
                string name = src.name;
                string dstPath = $"{OutPrefabs}/Trees/{name}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(dstPath) != null) continue;

                var clone = UnityEngine.Object.Instantiate(src);
                clone.name = name;
                var tree = clone.GetComponent<Tree>();
                if (tree != null) UnityEngine.Object.DestroyImmediate(tree);
                var r = clone.GetComponent<MeshRenderer>();
                var mats = r.sharedMaterials;
                var outMats = new Material[mats.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    bool leaf = mats[i] != null && mats[i].shader.name.Contains("Leaves");
                    outMats[i] = TreeMaterial(mats[i], $"{name}_{(leaf ? "Leaf" : "Bark")}", leaf);
                }
                r.sharedMaterials = outMats;
                r.shadowCastingMode = ShadowCastingMode.On;
                PrefabUtility.SaveAsPrefabAsset(clone, dstPath);
                UnityEngine.Object.DestroyImmediate(clone);
            }
        }

        static Material TreeMaterial(Material src, string name, bool leaf)
        {
            string path = $"{OutMaterials}/Trees/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            EnsureFolder(OutMaterials + "/Trees");

            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var albedo = src != null ? src.GetTexture("_MainTex") : null;
            m.SetTexture("_BaseMap", albedo);
            // Night: the pack's leaf atlas is a bright daytime green. Darkened so the treeline sits back in the fog.
            m.SetColor("_BaseColor", leaf ? new Color(0.24f, 0.30f, 0.26f) : new Color(0.55f, 0.55f, 0.55f));
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", leaf ? 0.12f : 0.18f);
            m.enableInstancing = true;
            if (leaf)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", src != null && src.HasProperty("_Cutoff") ? src.GetFloat("_Cutoff") : 0.4f);
                m.SetFloat("_Cull", 0f);                       // two-sided
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ------------------------------------------------------------------ grass

        /// <summary>Grass prefabs for terrain mesh details: same meshes, URP Lit alpha-clip, GPU instancing on.</summary>
        public static void ConvertGrass()
        {
            EnsureFolder(OutPrefabs + "/Grass");
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PackGrass }))
            {
                string srcPath = AssetDatabase.GUIDToAssetPath(guid);
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
                CloneRemapped(src, $"{OutPrefabs}/Grass/{src.name}.prefab");
            }
        }

        /// <summary>Plain clone of a pack prefab (not a variant) with every material replaced by our URP copy.</summary>
        public static GameObject CloneRemapped(GameObject src, string dstPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(dstPath);
            if (existing != null) return existing;
            var clone = UnityEngine.Object.Instantiate(src);
            clone.name = src.name;
            Remap(clone);
            var saved = PrefabUtility.SaveAsPrefabAsset(clone, dstPath);
            UnityEngine.Object.DestroyImmediate(clone);
            return saved;
        }

        /// <summary>Swap every material on a scene object (and children) for our converted copy where one exists.</summary>
        public static void Remap(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var c = Converted(mats[i]);
                    if (c != null) { mats[i] = c; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
#endif
