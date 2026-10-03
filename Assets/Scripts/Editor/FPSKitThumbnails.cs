#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// One arena's thumbnail cameras: the arena card shot, and an optional shot per level.
    /// Lives beside the images in Assets/UI/Thumbnails/&lt;Arena&gt;/Shots.asset. It is hand-kept,
    /// not generated, so a scene rebuild never moves a camera.
    /// </summary>
    public class ThumbnailShots : ScriptableObject
    {
        [System.Serializable]
        public struct View
        {
            [Tooltip("Where the camera stands, in world space.")]
            public Vector3 position;
            [Tooltip("The point it looks at, in world space.")]
            public Vector3 lookAt;
            [Tooltip("Vertical field of view in degrees.")]
            [Range(20f, 100f)] public float fov;
            [Tooltip("Hours of the day are not simulated; this scales the main light so a dusk arena is not black on the card. 1 = as built.")]
            [Range(0.5f, 3f)] public float exposure;
        }

        [Header("Arena card")]
        [Tooltip("The picture on the arena card, the Current Mission card and the level select.")]
        public View arena = new View { fov = 70f, exposure = 1f };

        [Header("Per level")]
        [Tooltip("Optional. Entry n is level n+1 of the arena; a level without one uses the arena shot.")]
        public View[] levels = new View[0];
    }

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
