#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Renders a built arena from a handful of fixed viewpoints and writes them out as
    /// PNGs, so what the builder produced can be looked at without opening the editor.
    ///
    /// This exists because a generated scene is write-only from the command line. It is
    /// saved in binary, so it cannot be read; its geometry is procedural, so a
    /// description of the code is not a description of the result; and every structural
    /// check in this repo answers a question about whether the level *works* --
    /// VerifyZone proves the banks are joined and the water is lethal, and would pass
    /// just as happily on an arena where the cliffs were inside out and the sand was
    /// black. A change made for how a level looks has to be checked by looking at it.
    ///
    /// Two things about the render are worth not re-deriving, and both are the same ones
    /// the dashboard's arena previews hit:
    ///
    ///   * <b>Render through the pipeline, not with <c>Camera.Render()</c>.</b> That call
    ///     predates scriptable pipelines; under URP it returns a frame with the skybox
    ///     and essentially no lighting, which reads as "the arena is a black silhouette"
    ///     rather than as "the capture is wrong".
    ///   * <b>Submit twice and keep the second.</b> The first frame after a scene opens
    ///     is drawn with whatever the pipeline had warmed already, so shadows and the
    ///     environment probe land one frame late.
    ///
    /// It needs a real graphics device, so it is run with UNITY_GRAPHICS=1:
    ///
    ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.CaptureViews \
    ///       -fpskitTheme "Desert Outpost" -fpskitOut /tmp/shots
    /// </summary>
    public static class FPSKitViews
    {
        const int Width = 1024;
        const int Height = 576;

        struct Shot
        {
            public string Name;
            public Vector3 From;
            public Vector3 Look;
            public float Fov;
        }

        public static void Capture()
        {
            string theme = Arg("-fpskitTheme") ?? "Desert Outpost";
            string outputFolder = Arg("-fpskitOut") ?? "Build/Views";

            string path = $"Assets/FPSKit_Generated/Scenes/{theme.Replace(" ", "")}.unity";

            if (!File.Exists(path))
            {
                Debug.LogError($"[FPSKitBatch] FAILED: {path} does not exist, so there is nothing to look at.");
                EditorApplication.Exit(1);
                return;
            }

            Directory.CreateDirectory(outputFolder);
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

            // Without this the first arena captured in a session is lit by no environment
            // at all, while everything after it looks right.
            DynamicGI.UpdateEnvironment();

            var settings = FPSKitThemes.GetOrCreate(theme);
            foreach (var shot in Frame(settings)) Render(shot, outputFolder);

            Debug.Log($"[FPSKitBatch] captured views of \"{theme}\" into {outputFolder}");
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// The viewpoints. Chosen to answer specific questions rather than to flatter:
        /// what the player sees on the first frame, whether the dunes read as dunes from
        /// eye height, whether the fence reads as a barrier from a distance, and what the
        /// canyon looks like from the one place the level never lets you stand.
        /// </summary>
        static IEnumerable<Shot> Frame(LevelTheme theme)
        {
            float centre = theme == null ? 92f
                : theme.hazardOffset + Mathf.Sin(1.7f) * theme.hazardMeander * 0.35f;

            float eye = 1.65f;

            yield return new Shot
            {
                Name = "01_spawn_east", From = new Vector3(0f, eye, 0f),
                Look = new Vector3(140f, 2f, 20f), Fov = 75f
            };

            yield return new Shot
            {
                Name = "02_spawn_north", From = new Vector3(0f, eye, 0f),
                Look = new Vector3(-30f, 4f, 160f), Fov = 75f
            };

            yield return new Shot
            {
                Name = "03_dune_crest", From = new Vector3(-70f, eye + 6f, -60f),
                Look = new Vector3(40f, 0f, 30f), Fov = 70f
            };

            yield return new Shot
            {
                Name = "04_rim_fence", From = new Vector3(centre - 62f, eye, -30f),
                Look = new Vector3(centre - 40f, 0f, 40f), Fov = 72f
            };

            // Right on the lip. Anywhere further back and the far rim hides the canyon
            // completely, which is correct for the level and useless for checking it.
            yield return new Shot
            {
                Name = "05_rim_over_water", From = new Vector3(centre - 38f, eye, 10f),
                Look = new Vector3(centre + 30f, -12f, 22f), Fov = 78f
            };

            yield return new Shot
            {
                Name = "06_bridge", From = new Vector3(centre - 18f, eye + 0.3f, 139f),
                Look = new Vector3(centre + 55f, -3f, 139f), Fov = 78f
            };

            yield return new Shot
            {
                Name = "07_in_canyon", From = new Vector3(centre - 6f, -16.5f, 60f),
                Look = new Vector3(centre - 24f, -12f, 30f), Fov = 80f
            };

            yield return new Shot
            {
                Name = "08_aerial", From = new Vector3(-150f, 190f, -190f),
                Look = new Vector3(60f, 0f, 20f), Fov = 62f
            };

            yield return new Shot
            {
                Name = "09_ground_close", From = new Vector3(-40f, 1.1f, 30f),
                Look = new Vector3(10f, 0.2f, 55f), Fov = 60f
            };
        }

        static void Render(Shot shot, string folder)
        {
            var rig = new GameObject("CaptureCamera");
            var camera = rig.AddComponent<Camera>();

            RenderTexture target = null;

            try
            {
                rig.transform.position = shot.From;
                rig.transform.rotation = Quaternion.LookRotation((shot.Look - shot.From).normalized,
                                                                 Vector3.up);

                camera.fieldOfView = shot.Fov;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 4000f;
                camera.clearFlags = CameraClearFlags.Skybox;

                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 2
                };

                var request = new RenderPipeline.StandardRequest { destination = target };

                if (RenderPipeline.SupportsRenderRequest(camera, request))
                {
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                else
                {
                    camera.targetTexture = target;
                    camera.Render();
                }

                var previous = RenderTexture.active;
                RenderTexture.active = target;

                var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                image.Apply();

                RenderTexture.active = previous;

                File.WriteAllBytes($"{folder}/{shot.Name}.png", image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = null;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                Object.DestroyImmediate(rig);
            }
        }

        static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];

            return null;
        }
    }
}
#endif
