#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Photographs the player's own view of the gun: at rest, through the reload, and
    /// through the rifle-butt strike. Those are animated in code (Weapon.ReloadPose,
    /// MeleeStrike.AnimateButt) and nothing in a scene says what a frame of them looks
    /// like, so the only check is to take the frames.
    ///
    ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKitBatch.CaptureViewModel -fpskitOut Build/ViewModel
    ///
    /// Fixed file names: read the exit code, not the folder.
    /// </summary>
    public static class FPSKitViewModelShots
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/IndustrialWarehouse.unity";
        const int Width = 1280, Height = 720;

        static string _folder;
        static int _step;
        static double _started;
        static float _mark;
        static Weapon _weapon;
        static MeleeStrike _melee;

        static readonly float[] ReloadAt = { 0.08f, 0.25f, 0.45f, 0.62f, 0.8f };
        static readonly float[] StrikeAt = { 0.1f, 0.3f, 0.45f, 0.75f };
        static int _shot;

        public static void Capture(string folder)
        {
            _folder = string.IsNullOrEmpty(folder) ? "Build/ViewModel" : folder;
            Directory.CreateDirectory(_folder);

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            _step = 0;
            _started = EditorApplication.timeSinceStartup;

            FPSKitPlayMode.SuspendStartScene();
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _started > 120) throw new Exception("view model capture timed out");

                switch (_step)
                {
                    case 0:
                        if (!EditorApplication.isPlaying) { EditorApplication.EnterPlaymode(); return; }

                        // One sixtieth a frame whatever the batch frame rate: a punch lasts a
                        // third of a second, and one slow batch frame skips all of it.
                        Time.captureDeltaTime = 1f / 60f;
                        _step = 1;
                        _mark = Time.time + 1.5f;
                        return;

                    case 1:
                    {
                        if (Time.time < _mark) return;

                        var player = GameObject.FindGameObjectWithTag("Player");
                        _weapon = player != null ? player.GetComponentInChildren<Weapon>() : null;
                        _melee = player != null ? player.GetComponent<MeleeStrike>() : null;
                        if (_weapon == null) throw new Exception("no player weapon in the scene");

                        // Nobody in the way of the picture.
                        foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                            ai.gameObject.SetActive(false);

                        Shoot("01_idle");

                        typeof(Weapon).GetProperty("CurrentAmmo").SetValue(_weapon, 0);
                        _weapon.TryReload();
                        _mark = Time.time;
                        _shot = 0;
                        _step = 2;
                        return;
                    }

                    case 2:
                    {
                        if (_shot >= ReloadAt.Length)
                        {
                            _step = 3;
                            _mark = Time.time + 0.4f;
                            return;
                        }

                        if (Time.time < _mark + ReloadAt[_shot] * _weapon.ReloadTime) return;

                        Shoot($"02_reload_{_shot + 1}_{Mathf.RoundToInt(ReloadAt[_shot] * 100)}pct");
                        _shot++;
                        return;
                    }

                    case 3:
                    {
                        if (Time.time < _mark || _weapon.IsReloading) return;

                        _melee.TryPunch();
                        _mark = Time.time;
                        _shot = 0;
                        _step = 4;
                        return;
                    }

                    case 4:
                    {
                        if (_shot >= StrikeAt.Length) { Done(0); return; }

                        if (Time.time < _mark + StrikeAt[_shot] * _melee.swingSeconds) return;

                        Shoot($"03_strike_{_shot + 1}_{Mathf.RoundToInt(StrikeAt[_shot] * 100)}pct");
                        _shot++;
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[FPSKitBatch] FAILED: {e}");
                Done(1);
            }
        }

        static void Shoot(string name)
        {
            var camera = _weapon.fpsCamera != null ? _weapon.fpsCamera : Camera.main;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };

            try
            {
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
                    camera.targetTexture = null;
                }

                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                image.Apply();
                RenderTexture.active = previous;

                File.WriteAllBytes($"{_folder}/{name}.png", image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        static void Done(int code)
        {
            Time.captureDeltaTime = 0f;
            FPSKitPlayMode.RestoreStartScene();
            EditorApplication.update -= Tick;
            if (code == 0) Debug.Log($"[FPSKitBatch] view model shots written to {_folder}");
            EditorApplication.Exit(code);
        }
    }
}
#endif
