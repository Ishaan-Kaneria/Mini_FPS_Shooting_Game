#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Renders every store item's model to a PNG for its store card.
    ///
    /// <b>The models are built here, from primitives, in the style of the one gun the game
    /// draws in first person</b> -- boxes and cylinders in flat matte colours. The game has one
    /// viewmodel shared by every weapon, so rendering "the" model would put six identical
    /// pictures on the weapons shelf; each item gets its own silhouette instead, shaped by what
    /// it is (a long barrel and a scope for the DMR, a box and a bipod for the LMG).
    ///
    /// Rendered through the pipeline (<c>SubmitRenderRequest</c>, twice, keeping the second) for
    /// the reasons the arena previews are, onto a transparent background so the card's own
    /// plain panel is the backdrop. Needs a real graphics device, like the previews:
    ///
    ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.RenderStoreItems
    ///
    /// Without one it warns and keeps whatever renders are already on disk.
    /// </summary>
    public static class FPSKitItemRenders
    {
        public const string Folder = "Assets/FPSKit_Generated/Store/Renders";
        public const string AssetPath = "Assets/FPSKit_Generated/Store/ItemRenders.asset";
        const int Width = 640, Height = 400;

        [MenuItem("FPSKit/Render Store Items", false, 62)]
        public static void RenderAll()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogWarning("[FPSKit] Store renders need a graphics device (UNITY_GRAPHICS=1). Keeping what is on disk.");
                return;
            }

            var catalog = AssetDatabase.LoadAssetAtPath<StoreCatalog>(FPSKitStore.CatalogPath);
            if (catalog == null) throw new Exception("no store catalog -- run ResetStore first");
            Directory.CreateDirectory(Folder);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var light = new GameObject("Key").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.transform.rotation = Quaternion.Euler(38f, -32f, 0f);
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fill.transform.rotation = Quaternion.Euler(-20f, 150f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f);

            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.fieldOfView = 22f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 50f;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
            data.renderPostProcessing = false;

            var jobs = new List<(string id, Func<Transform, GameObject> build, Vector3 view)>();
            var side = new Vector3(-0.28f, 0.22f, -1f);
            var high = new Vector3(-0.55f, 0.55f, -1f);
            foreach (var g in catalog.guns) if (g != null) { var id = g.id; jobs.Add((id, p => Gun(p, id), side)); }
            foreach (var b in catalog.bombs) if (b != null) { var id = b.id; var tint = b.data != null ? b.data.blastColor : Color.gray; jobs.Add((id, p => Bomb(p, id, tint), high)); }
            foreach (var c in catalog.consumables) if (c != null) { var id = c.id; var tint = c.data != null ? c.data.tint : Color.cyan; jobs.Add((id, p => Drink(p, id, tint), high)); }
            jobs.Add(("vitality", Medkit, high));

            var renders = AssetDatabase.LoadAssetAtPath<ItemRenders>(AssetPath);
            if (renders == null)
            {
                renders = ScriptableObject.CreateInstance<ItemRenders>();
                AssetDatabase.CreateAsset(renders, AssetPath);
            }
            renders.entries.Clear();

            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            foreach (var job in jobs)
            {
                var root = new GameObject("Item_" + job.id);
                job.build(root.transform);
                foreach (var col in root.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(col);
                Frame(cam, root, job.view);

                var request = new RenderPipeline.StandardRequest { destination = target };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                {
                    RenderPipeline.SubmitRenderRequest(cam, request);
                    RenderPipeline.SubmitRenderRequest(cam, request);
                }
                else { cam.targetTexture = target; cam.Render(); cam.targetTexture = null; }

                var prev = RenderTexture.active;
                RenderTexture.active = target;
                var shot = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                shot.Apply();
                RenderTexture.active = prev;

                string path = $"{Folder}/{job.id}.png";
                File.WriteAllBytes(path, shot.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(shot);
                UnityEngine.Object.DestroyImmediate(root);

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                {
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
                renders.entries.Add(new ItemRenders.Entry { id = job.id, texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path) });
            }
            target.Release();
            EditorUtility.SetDirty(renders);
            AssetDatabase.SaveAssets();
            Debug.Log($"<color=lime>[FPSKit]</color> Rendered {jobs.Count} store items to {Folder}.");
        }

        /// <summary>Points the camera at the model from a direction and backs off until it all fits.</summary>
        static void Frame(Camera cam, GameObject root, Vector3 view)
        {
            var bounds = new Bounds(root.transform.position, Vector3.zero);
            bool first = true;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            var dir = view.normalized;
            var rot = Quaternion.LookRotation(-dir, Vector3.up);
            Vector3 right = rot * Vector3.right, up = rot * Vector3.up, fwd = rot * Vector3.forward;

            // Fitted to what the camera actually sees of the model -- its extent across and up
            // the frame -- rather than to a bounding sphere, which leaves a long gun a thin
            // line in the middle of a mostly empty card.
            float halfW = 0f, halfH = 0f, depth = 0f;
            var e = bounds.extents;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                halfW = Mathf.Max(halfW, Mathf.Abs(Vector3.Dot(corner, right)));
                halfH = Mathf.Max(halfH, Mathf.Abs(Vector3.Dot(corner, up)));
                depth = Mathf.Max(depth, Mathf.Abs(Vector3.Dot(corner, fwd)));
            }
            float vfov = cam.fieldOfView * Mathf.Deg2Rad;
            float hfov = 2f * Mathf.Atan(Mathf.Tan(vfov * 0.5f) * Width / (float)Height);
            const float margin = 1.12f;
            float dist = Mathf.Max(halfW * margin / Mathf.Tan(hfov * 0.5f), halfH * margin / Mathf.Tan(vfov * 0.5f)) + depth;
            cam.transform.position = bounds.center + dir * dist;
            cam.transform.rotation = rot;
        }

        // ==================================================================
        // Models.
        // ==================================================================

        static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();

        static Material Mat(Color c)
        {
            if (Materials.TryGetValue(c, out var m) && m != null) return m;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(shader);
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            m.SetFloat("_Smoothness", 0.18f);
            m.SetFloat("_Metallic", 0f);
            Materials[c] = m;
            return m;
        }

        static GameObject Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color c, float rotZ = 0f)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, rotZ);
            go.GetComponent<Renderer>().sharedMaterial = Mat(c);
            return go;
        }

        static void Box(Transform p, Vector3 pos, Vector3 size, Color c, float rotZ = 0f) => Part(p, PrimitiveType.Cube, pos, size, c, rotZ);

        /// <summary>A cylinder lying along X: radius and length in metres.</summary>
        static void Tube(Transform p, Vector3 pos, float radius, float length, Color c)
            => Part(p, PrimitiveType.Cylinder, pos, new Vector3(radius * 2f, length * 0.5f, radius * 2f), c, 90f);

        static readonly Color Metal = new Color(0.16f, 0.17f, 0.19f);
        static readonly Color Polymer = new Color(0.24f, 0.25f, 0.27f);
        static readonly Color Steel = new Color(0.42f, 0.44f, 0.47f);

        static GameObject Gun(Transform root, string id)
        {
            switch (id)
            {
                case "smg":
                    Box(root, new Vector3(0f, 0f, 0f), new Vector3(0.40f, 0.12f, 0.07f), Metal);
                    Tube(root, new Vector3(0.28f, 0.02f, 0f), 0.018f, 0.16f, Steel);
                    Box(root, new Vector3(0.05f, -0.19f, 0f), new Vector3(0.05f, 0.28f, 0.04f), Polymer, -4f);
                    Box(root, new Vector3(-0.12f, -0.1f, 0f), new Vector3(0.06f, 0.13f, 0.05f), Polymer, 14f);
                    Box(root, new Vector3(-0.34f, 0.02f, 0f), new Vector3(0.28f, 0.02f, 0.02f), Steel);
                    Box(root, new Vector3(-0.47f, -0.02f, 0f), new Vector3(0.02f, 0.10f, 0.03f), Steel);
                    break;
                case "shotgun":
                {
                    var wood = new Color(0.43f, 0.28f, 0.16f);
                    Box(root, new Vector3(0f, 0f, 0f), new Vector3(0.34f, 0.11f, 0.07f), Metal);
                    Tube(root, new Vector3(0.47f, 0.02f, 0f), 0.028f, 0.62f, Steel);
                    Tube(root, new Vector3(0.40f, -0.04f, 0f), 0.022f, 0.50f, Metal);
                    Box(root, new Vector3(0.36f, -0.045f, 0f), new Vector3(0.18f, 0.07f, 0.08f), wood);
                    Box(root, new Vector3(-0.38f, -0.03f, 0f), new Vector3(0.40f, 0.12f, 0.06f), wood, 5f);
                    Box(root, new Vector3(-0.10f, -0.09f, 0f), new Vector3(0.06f, 0.10f, 0.05f), wood, 18f);
                    break;
                }
                case "burst":
                {
                    var tint = new Color(0.30f, 0.34f, 0.40f);
                    Box(root, new Vector3(0f, 0f, 0f), new Vector3(0.52f, 0.12f, 0.07f), tint);
                    Tube(root, new Vector3(0.47f, 0.02f, 0f), 0.02f, 0.40f, Steel);
                    Box(root, new Vector3(0.36f, 0.0f, 0f), new Vector3(0.26f, 0.09f, 0.075f), Polymer);
                    Box(root, new Vector3(0.36f, -0.10f, 0f), new Vector3(0.04f, 0.12f, 0.04f), Polymer);
                    Box(root, new Vector3(0.06f, -0.14f, 0f), new Vector3(0.07f, 0.18f, 0.05f), Metal);
                    Box(root, new Vector3(-0.12f, -0.10f, 0f), new Vector3(0.06f, 0.14f, 0.05f), Polymer, 15f);
                    Box(root, new Vector3(-0.42f, -0.02f, 0f), new Vector3(0.30f, 0.10f, 0.05f), Polymer);
                    Box(root, new Vector3(0.02f, 0.09f, 0f), new Vector3(0.12f, 0.06f, 0.05f), Metal);
                    Tube(root, new Vector3(0.02f, 0.10f, 0f), 0.02f, 0.13f, new Color(0.85f, 0.25f, 0.2f));
                    break;
                }
                case "dmr":
                {
                    var olive = new Color(0.33f, 0.36f, 0.24f);
                    Box(root, new Vector3(0f, 0f, 0f), new Vector3(0.62f, 0.11f, 0.07f), olive);
                    Tube(root, new Vector3(0.62f, 0.02f, 0f), 0.017f, 0.62f, Steel);
                    Tube(root, new Vector3(0.02f, 0.13f, 0f), 0.04f, 0.38f, Metal);
                    Tube(root, new Vector3(0.22f, 0.13f, 0f), 0.05f, 0.06f, Metal);
                    Box(root, new Vector3(-0.08f, 0.08f, 0f), new Vector3(0.03f, 0.05f, 0.03f), Metal);
                    Box(root, new Vector3(0.12f, 0.08f, 0f), new Vector3(0.03f, 0.05f, 0.03f), Metal);
                    Box(root, new Vector3(0.08f, -0.12f, 0f), new Vector3(0.07f, 0.12f, 0.05f), Metal);
                    Box(root, new Vector3(-0.14f, -0.10f, 0f), new Vector3(0.06f, 0.14f, 0.05f), olive, 15f);
                    Box(root, new Vector3(-0.48f, -0.02f, 0f), new Vector3(0.34f, 0.12f, 0.05f), olive);
                    Box(root, new Vector3(-0.44f, 0.06f, 0f), new Vector3(0.16f, 0.04f, 0.05f), olive);
                    break;
                }
                case "lmg":
                {
                    var green = new Color(0.22f, 0.27f, 0.20f);
                    Box(root, new Vector3(0f, 0f, 0f), new Vector3(0.62f, 0.14f, 0.09f), green);
                    Box(root, new Vector3(0.42f, 0.02f, 0f), new Vector3(0.30f, 0.09f, 0.09f), Metal);
                    Tube(root, new Vector3(0.72f, 0.02f, 0f), 0.028f, 0.40f, Steel);
                    Box(root, new Vector3(0.0f, -0.16f, 0f), new Vector3(0.18f, 0.16f, 0.13f), green);
                    Box(root, new Vector3(-0.16f, -0.11f, 0f), new Vector3(0.06f, 0.14f, 0.05f), Polymer, 15f);
                    Box(root, new Vector3(-0.46f, -0.03f, 0f), new Vector3(0.30f, 0.12f, 0.06f), Polymer);
                    Box(root, new Vector3(0.05f, 0.12f, 0f), new Vector3(0.20f, 0.03f, 0.03f), Metal);
                    Part(root, PrimitiveType.Cylinder, new Vector3(0.62f, -0.12f, 0.03f), new Vector3(0.018f, 0.12f, 0.018f), Steel, 24f);
                    Part(root, PrimitiveType.Cylinder, new Vector3(0.62f, -0.12f, -0.03f), new Vector3(0.018f, 0.12f, 0.018f), Steel, -24f);
                    break;
                }
                default: // the rifle, and anything added to the catalog later
                {
                    var tan = new Color(0.52f, 0.45f, 0.33f);
                    Box(root, new Vector3(0f, 0f, 0f), new Vector3(0.55f, 0.12f, 0.07f), Metal);
                    Tube(root, new Vector3(0.52f, 0.02f, 0f), 0.02f, 0.46f, Steel);
                    Box(root, new Vector3(0.38f, 0.0f, 0f), new Vector3(0.30f, 0.085f, 0.075f), tan);
                    Box(root, new Vector3(0.08f, -0.14f, 0f), new Vector3(0.07f, 0.18f, 0.05f), Metal, -8f);
                    Box(root, new Vector3(-0.12f, -0.10f, 0f), new Vector3(0.06f, 0.14f, 0.05f), tan, 15f);
                    Box(root, new Vector3(-0.44f, -0.02f, 0f), new Vector3(0.32f, 0.10f, 0.05f), tan);
                    Box(root, new Vector3(0.0f, 0.08f, 0f), new Vector3(0.12f, 0.03f, 0.03f), Metal);
                    break;
                }
            }
            return root.gameObject;
        }

        static GameObject Bomb(Transform root, string id, Color tint)
        {
            var body = Color.Lerp(new Color(0.25f, 0.30f, 0.20f), tint, 0.25f);
            switch (id)
            {
                case "cluster":
                    foreach (var p in new[] { new Vector3(-0.12f, 0f, 0f), new Vector3(0.12f, 0f, 0f), new Vector3(0f, 0f, 0.12f), new Vector3(0f, 0.16f, 0.04f) })
                        Part(root, PrimitiveType.Sphere, p, Vector3.one * 0.22f, body);
                    Part(root, PrimitiveType.Cylinder, new Vector3(0f, 0.05f, 0.04f), new Vector3(0.36f, 0.02f, 0.36f), Metal);
                    break;
                case "thermite":
                    Part(root, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.26f, 0.22f, 0.26f), new Color(0.72f, 0.26f, 0.16f));
                    Part(root, PrimitiveType.Cylinder, new Vector3(0f, 0.25f, 0f), new Vector3(0.18f, 0.04f, 0.18f), Steel);
                    Part(root, PrimitiveType.Cylinder, new Vector3(0f, 0.05f, 0f), new Vector3(0.27f, 0.03f, 0.27f), new Color(0.9f, 0.75f, 0.2f));
                    break;
                default:
                    Part(root, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.42f, body);
                    Part(root, PrimitiveType.Cylinder, new Vector3(0f, 0.22f, 0f), new Vector3(0.12f, 0.04f, 0.12f), Steel);
                    Box(root, new Vector3(0.06f, 0.14f, 0f), new Vector3(0.04f, 0.22f, 0.05f), Steel, -18f);
                    Part(root, PrimitiveType.Cylinder, new Vector3(-0.08f, 0.26f, 0f), new Vector3(0.08f, 0.005f, 0.08f), Steel, 90f);
                    break;
            }
            return root.gameObject;
        }

        static GameObject Drink(Transform root, string id, Color tint)
        {
            if (id == "adrenaline")
            {
                // A syringe, lying down.
                Tube(root, Vector3.zero, 0.05f, 0.34f, new Color(0.86f, 0.88f, 0.90f));
                Tube(root, new Vector3(0f, 0f, 0f), 0.053f, 0.16f, tint);
                Tube(root, new Vector3(-0.22f, 0f, 0f), 0.02f, 0.12f, Steel);
                Box(root, new Vector3(-0.29f, 0f, 0f), new Vector3(0.02f, 0.12f, 0.12f), Steel);
                Tube(root, new Vector3(0.24f, 0f, 0f), 0.006f, 0.14f, Steel);
            }
            else
            {
                // A can.
                Part(root, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.2f, 0.16f, 0.2f), tint);
                Part(root, PrimitiveType.Cylinder, new Vector3(0f, 0.16f, 0f), new Vector3(0.18f, 0.01f, 0.18f), Steel);
                Part(root, PrimitiveType.Cylinder, new Vector3(0f, -0.16f, 0f), new Vector3(0.18f, 0.01f, 0.18f), Steel);
                Part(root, PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0f), new Vector3(0.205f, 0.03f, 0.205f), new Color(0.95f, 0.95f, 0.95f));
            }
            return root.gameObject;
        }

        static GameObject Medkit(Transform root)
        {
            var red = new Color(0.8f, 0.2f, 0.2f);
            Box(root, Vector3.zero, new Vector3(0.42f, 0.30f, 0.14f), new Color(0.9f, 0.9f, 0.88f));
            Box(root, new Vector3(0f, 0f, -0.072f), new Vector3(0.18f, 0.05f, 0.01f), red);
            Box(root, new Vector3(0f, 0f, -0.072f), new Vector3(0.05f, 0.18f, 0.01f), red);
            Box(root, new Vector3(0f, 0.18f, 0f), new Vector3(0.16f, 0.04f, 0.04f), Metal);
            return root.gameObject;
        }
    }
}
#endif
