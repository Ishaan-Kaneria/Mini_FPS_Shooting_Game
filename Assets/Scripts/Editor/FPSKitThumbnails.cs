#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Renders the card images: Tools &gt; MiniFPS &gt; Thumbnails &gt; Capture.
    ///
    /// Same two rules as <see cref="FPSKitViews"/>: render through the pipeline (SubmitRenderRequest)
    /// and submit twice. The scene is opened in edit mode, so no enemy or HUD exists; anything
    /// that would be one at runtime is hidden anyway. Rendered at 1280x720, saved at 640x360.
    /// </summary>
    public static class FPSKitThumbnails
    {
        const int RenderW = 1280, RenderH = 720, SaveW = 640, SaveH = 360;
        const string Root = "Assets/UI/Thumbnails";

        [MenuItem("Tools/MiniFPS/Thumbnails/Capture Fairground")]
        public static void CaptureFairground() => CaptureArena("Abandoned Fairground");

        /// <summary>Renders the arena card image of one arena and assigns it to the catalog entry.</summary>
        public static void CaptureArena(string arenaName)
        {
            string folder = $"{Root}/{arenaName.Replace(" ", "")}";
            Directory.CreateDirectory(folder);

            var shots = AssetDatabase.LoadAssetAtPath<ThumbnailShots>($"{folder}/Shots.asset");
            if (shots == null)
            {
                shots = ScriptableObject.CreateInstance<ThumbnailShots>();
                AssetDatabase.CreateAsset(shots, $"{folder}/Shots.asset");
            }

            string scene = $"Assets/FPSKit_Generated/Scenes/{arenaName.Replace(" ", "")}.unity";
            if (EditorApplication.isPlaying) { Debug.LogError("[Thumbnails] Leave play mode first."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!EditorSceneManager.GetActiveScene().path.Equals(scene))
                EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);

            HideRuntimeOnly();
            DynamicGI.UpdateEnvironment();
            foreach (var p in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                p.Simulate(14f, true, true);

            string png = $"{folder}/Arena.png";
            Render(shots.arena, png);
            AssetDatabase.ImportAsset(png);
            AssignToCatalog(arenaName, AssetDatabase.LoadAssetAtPath<Texture2D>(png));
            Debug.Log($"[Thumbnails] {png}");
        }

        /// <summary>
        /// One look at the open scene from a camera, written as a PNG of the given size. For checking a built arena
        /// from the editor without batch mode: the scene must already be open.
        /// </summary>
        public static void Snap(Vector3 position, Vector3 lookAt, float fov, string path, int width = 1280, int height = 720)
        {
            DynamicGI.UpdateEnvironment();
            var rig = new GameObject("SnapCamera");
            var cam = rig.AddComponent<Camera>();
            RenderTexture rt = null;
            try
            {
                rig.transform.position = position;
                rig.transform.rotation = Quaternion.LookRotation((lookAt - position).normalized, Vector3.up);
                cam.fieldOfView = fov; cam.nearClipPlane = 0.1f; cam.farClipPlane = 3000f; cam.clearFlags = CameraClearFlags.Skybox;
                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
                var request = new RenderPipeline.StandardRequest { destination = rt };
                RenderPipeline.SubmitRenderRequest(cam, request);
                RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var img = new Texture2D(width, height, TextureFormat.RGB24, false);
                img.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                img.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(path, img.EncodeToPNG());
                Object.DestroyImmediate(img);
            }
            finally
            {
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                Object.DestroyImmediate(rig);
            }
        }

        static void HideRuntimeOnly()
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) c.gameObject.SetActive(false);
            foreach (var e in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None)) e.gameObject.SetActive(false);
        }

        static void Render(ThumbnailShots.View v, string path)
        {
            var rig = new GameObject("ThumbnailCamera");
            var cam = rig.AddComponent<Camera>();
            RenderTexture rt = null, small = null;
            var sun = RenderSettings.sun;
            float sunIntensity = sun != null ? sun.intensity : 0f;
            try
            {
                if (sun != null) sun.intensity = sunIntensity * Mathf.Max(0.5f, v.exposure);
                rig.transform.position = v.position;
                rig.transform.rotation = Quaternion.LookRotation((v.lookAt - v.position).normalized, Vector3.up);
                cam.fieldOfView = v.fov > 1f ? v.fov : 70f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 4000f;
                cam.clearFlags = CameraClearFlags.Skybox;

                rt = new RenderTexture(RenderW, RenderH, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (!RenderPipeline.SupportsRenderRequest(cam, request)) { Debug.LogError("[Thumbnails] Pipeline cannot render requests."); return; }
                RenderPipeline.SubmitRenderRequest(cam, request);
                RenderPipeline.SubmitRenderRequest(cam, request);

                small = new RenderTexture(SaveW, SaveH, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(rt, small);
                var prev = RenderTexture.active;
                RenderTexture.active = small;
                var img = new Texture2D(SaveW, SaveH, TextureFormat.RGB24, false);
                img.ReadPixels(new Rect(0, 0, SaveW, SaveH), 0, 0);
                img.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(path, img.EncodeToPNG());
                Object.DestroyImmediate(img);
            }
            finally
            {
                if (sun != null) sun.intensity = sunIntensity;
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (small != null) { small.Release(); Object.DestroyImmediate(small); }
                Object.DestroyImmediate(rig);
            }
        }

        static void AssignToCatalog(string arenaName, Texture2D tex)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ArenaCatalog>("Assets/FPSKit_Generated/Arenas.asset");
            if (catalog == null || tex == null) return;
            foreach (var e in catalog.arenas)
                if (e.displayName == arenaName) e.preview = tex;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        /// <summary>The hand-made card image for an arena, if one has been captured. The dashboard prefers it.</summary>
        public static Texture2D CardImage(string arenaName) =>
            AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/{arenaName.Replace(" ", "")}/Arena.png");
    }
}
#endif
