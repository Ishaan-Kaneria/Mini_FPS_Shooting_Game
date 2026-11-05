#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The town's real glass (Docs/DESERT_GLASS.md): every window a thin pane in a small mesh per 64 m chunk, in front of what is behind it, and
    /// a GlassField that knows them all for breaking. Glass is a dielectric (metallic 0, F0 0.04): the old windows were 85 % metal, dark mirrors.
    /// A solid building's window keeps a dark, matte room colour behind the pane (or its curtain); an enterable one has nothing but the room.
    /// </summary>
    public static partial class FPSKitSceneBuilder
    {
        class GlassChunk { public TownMesh mesh = new TownMesh(); public List<GlassField.Pane> panes = new List<GlassField.Pane>(); public int verts; }

        const float GlassChunkSize = 64f;
        const string GlassFolder = "Assets/Resources/Glass";
        private static readonly Dictionary<Vector2Int, GlassChunk> _glassChunks = new Dictionary<Vector2Int, GlassChunk>();

        /// <summary>The materials breaking glass needs at run time, and the pane's. Run from the town's materials and by -executeMethod.</summary>
        public static Material MakeGlassMaterials()
        {
            AssetDatabase.ImportAsset(GlassFolder, ImportAssetOptions.ImportRecursive);
            foreach (var png in new[] { "Dust", "Crack", "Web" })
                if (AssetImporter.GetAtPath($"{GlassFolder}/{png}.png") is TextureImporter t && (t.alphaSource != TextureImporterAlphaSource.FromInput || t.sRGBTexture == false || t.wrapMode != TextureWrapMode.Clamp))
                {
                    t.alphaSource = TextureImporterAlphaSource.FromInput; t.wrapMode = TextureWrapMode.Clamp; t.sRGBTexture = true; t.maxTextureSize = 512; t.SaveAndReimport();
                }
            Material Make(string name, Color c, float smooth, string map)
            {
                string path = $"{GlassFolder}/{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool create = m == null;
                if (create) m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", smooth);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_BlendModePreserveSpecular", 1f);
                m.SetFloat("_AlphaClip", 0f);
                m.SetFloat("_Cull", 0f);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetShaderPassEnabled("DepthOnly", false);
                m.SetShaderPassEnabled("ShadowCaster", false);
                m.enableInstancing = true;
                m.SetTexture("_BaseMap", map != null ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{GlassFolder}/{map}.png") : null);
                if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
                return m;
            }
            var pane = Make("Pane", new Color(0.72f, 0.75f, 0.74f, 1f), 0.95f, "Dust");
            Make("Shard", new Color(0.62f, 0.68f, 0.68f, 0.55f), 0.92f, null);
            Make("Crack", new Color(0.95f, 0.97f, 0.97f, 1f), 0.6f, "Crack");
            Make("Web", new Color(0.95f, 0.97f, 0.97f, 1f), 0.6f, "Web");
            Make("Clear", new Color(0f, 0f, 0f, 0f), 0f, null);
            AssetDatabase.SaveAssets();
            return pane;
        }

        /// <summary>
        /// A pane of glass over the opening a, b, c, d (a and b along the sill, c above b, d above a), a centimetre in front of what is behind
        /// it. Its UV runs 0-1 over it, so each carries its own dust. Not in the building's mesh: this one has no collider, casts no shadow and
        /// lets the light through.
        /// </summary>
        private static void AddPane(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, GlassField.Kind kind)
        {
            var n = normal.normalized;
            a += n * 0.01f; b += n * 0.01f; c += n * 0.01f; d += n * 0.01f;
            Vector3 mid = (a + c) * 0.5f;
            var key = new Vector2Int(Mathf.FloorToInt(mid.x / GlassChunkSize), Mathf.FloorToInt(mid.z / GlassChunkSize));
            if (!_glassChunks.TryGetValue(key, out var chunk)) _glassChunks[key] = chunk = new GlassChunk();
            chunk.panes.Add(new GlassField.Pane { corner = a, u = b - a, v = d - a, normal = n, kind = kind, firstVertex = chunk.verts });
            chunk.mesh.QuadUV(_townGlass, a, b, c, d, n, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            chunk.verts += 4;
            // A shop's glass stands at ground level. It was part of a mesh with a collider, which is what the navmesh and the player stopped at.
            if (kind != GlassField.Kind.Window) _glassBlocks.Add((mid, b - a, d - a, n));
        }

        /// <summary>The chunks' meshes and the field, once everything has its panes. Past about 200 m a chunk's glass is not drawn: what is behind it stays.</summary>
        private static void BuildGlassField(Transform root)
        {
            if (_glassChunks.Count == 0) return;
            var holder = new GameObject("Glass");
            holder.transform.SetParent(root, false);
            var field = holder.AddComponent<GlassField>();
            var panes = new List<GlassField.Pane>();
            var filters = new List<MeshFilter>();
            foreach (var kv in _glassChunks)
            {
                var go = kv.Value.mesh.Build(holder.transform, $"Glass_{kv.Key.x}_{kv.Key.y}", 0, null, collider: false, shadows: ShadowCastingMode.Off, tangents: false);
                if (go == null) continue;
                GameObjectUtility.SetStaticEditorFlags(go, 0);                 // read and changed in play
                var mr = go.GetComponent<MeshRenderer>();
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                var lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.2f, new Renderer[] { mr }) });
                lod.RecalculateBounds();
                int index = filters.Count;
                filters.Add(go.GetComponent<MeshFilter>());
                foreach (var p in kv.Value.panes) { var q = p; q.chunk = index; panes.Add(q); }
            }
            field.panes = panes.ToArray();
            field.chunks = filters.ToArray();
            // On Environment for the bake; in play GlassField moves them to the layer only the player and cars meet, so rounds and sight pass.
            var blockHolder = new GameObject("GlassBlocks");
            blockHolder.transform.SetParent(holder.transform, false);
            blockHolder.layer = LayerMask.NameToLayer("Environment");
            foreach (var (mid, u, v, n) in _glassBlocks)
            {
                var block = new GameObject("Block");
                block.transform.SetParent(blockHolder.transform, false);
                block.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(n, v));
                block.layer = blockHolder.layer;
                var box = block.AddComponent<BoxCollider>();
                box.size = new Vector3(u.magnitude, v.magnitude, 0.06f);
                GameObjectUtility.SetStaticEditorFlags(block, StaticEditorFlags.NavigationStatic);
            }
            field.blocks = blockHolder.transform;
            Debug.Log($"[FPSKit] glass: {panes.Count} panes in {filters.Count} chunks");
        }

        private static Material _townGlass;

        /// <summary>The first and last thing a town build does for glass; see BuildFromTheme.</summary>
        private static void ClearGlass() { _glassChunks.Clear(); _glassBlocks.Clear(); _townGlass = null; }

        private static readonly List<(Vector3 mid, Vector3 u, Vector3 v, Vector3 n)> _glassBlocks = new List<(Vector3, Vector3, Vector3, Vector3)>();
    }
}
#endif
