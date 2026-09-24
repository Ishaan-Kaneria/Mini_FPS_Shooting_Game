#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Plays the dashboard, the settings screen and an arena as a PC, a phone and a tablet,
    /// renders each at the device's exact resolution, and measures what a picture cannot
    /// prove: touch targets in dp, the crosshair left clear, controls inside the safe area,
    /// and type no smaller than a phone can read.
    ///
    ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitDeviceShots.Capture -fpskitOut Build/Devices
    ///
    /// <b>The devices are the Device Simulator's, fed in headlessly.</b> The simulator is a
    /// window, and a window cannot be driven from batch mode; what it actually does is stand a
    /// device's resolution, density, safe area and platform in for the real screen's. This does
    /// the same through <see cref="ScreenInfo"/>, which is where every measurement in the game
    /// reads the screen. The iPad is the simulator's own definition. The simulator's bundled
    /// list predates the iPhone 15 and the Pixel 7, so those two are typed from the makers'
    /// published specifications: resolution, density, and landscape safe-area insets.
    ///
    /// <b>The render is two passes and a composite</b>: the arena through its own camera, then
    /// every canvas through an interface camera with no post-processing, on black and again on
    /// white, which gives each pixel's coverage exactly without trusting the pipeline to keep
    /// an alpha channel. Tonemapping the HUD along with the scene would change the colours the
    /// shots exist to judge.
    /// </summary>
    public static class FPSKitDeviceShots
    {
        struct Shot
        {
            public string File;
            public string Scene;
            public ScreenInfo.Device Device;
            public InputScheme Scheme;
            public PadFamily Pad;
            public bool Settings;
            /// <summary>A dashboard screen to open: loadout, info, quit, store, achievements.</summary>
            public string Open;
            public bool Pause;
            public bool LeftHanded;
            public float Wait;
        }

        const string MenuScene = "Assets/FPSKit_Generated/Scenes/Menu.unity";
        const string ArenaScene = "Assets/FPSKit_Generated/Scenes/SnowboundStation.unity";

        static ScreenInfo.Device Pc(int w, int h) => new ScreenInfo.Device
        {
            name = $"PC {w}x{h}", width = w, height = h, dpi = 96f,
            safeArea = new Rect(0, 0, w, h), touch = false, mobile = false,
        };

        // Apple: 2556x1179 at 460ppi; landscape insets 59pt left and right, 21pt bottom (@3x).
        static readonly ScreenInfo.Device IPhone15 = new ScreenInfo.Device
        {
            name = "iPhone 15", width = 2556, height = 1179, dpi = 460f,
            safeArea = new Rect(177, 63, 2556 - 354, 1179 - 63), touch = true, mobile = true,
        };

        // Google: 2400x1080 at 416ppi; the punch-hole's inset on the camera side in landscape,
        // taken from the simulator's Pixel 5, which has the same cutout.
        static readonly ScreenInfo.Device Pixel7 = new ScreenInfo.Device
        {
            name = "Pixel 7", width = 2400, height = 1080, dpi = 416f,
            safeArea = new Rect(136, 0, 2400 - 136, 1080), touch = true, mobile = true,
        };

        // Device Simulator: "Apple iPad (7th gen)", 2160x1620 at 264ppi, 4:3, no insets.
        static readonly ScreenInfo.Device IPad = new ScreenInfo.Device
        {
            name = "iPad", width = 2160, height = 1620, dpi = 264f,
            safeArea = new Rect(0, 0, 2160, 1620), touch = true, mobile = true,
        };

        static List<Shot> Plan()
        {
            var pc = Pc(1920, 1080);
            var list = new List<Shot>
            {
                new Shot { File = "pc_dashboard", Scene = MenuScene, Device = pc, Scheme = InputScheme.KeyboardMouse, Wait = 2.5f },
                new Shot { File = "pc720_dashboard", Scene = MenuScene, Device = Pc(1280, 720), Scheme = InputScheme.KeyboardMouse, Wait = 2.5f },
                new Shot { File = "pc_dashboard_gamepad", Scene = MenuScene, Device = pc, Scheme = InputScheme.Gamepad, Wait = 2.5f },
                new Shot { File = "pc_loadout", Scene = MenuScene, Device = pc, Scheme = InputScheme.KeyboardMouse, Open = "loadout", Wait = 2.5f },
                new Shot { File = "pc_info", Scene = MenuScene, Device = pc, Scheme = InputScheme.KeyboardMouse, Open = "info", Wait = 2.5f },
                new Shot { File = "pc_quit", Scene = MenuScene, Device = pc, Scheme = InputScheme.Gamepad, Open = "quit", Wait = 2.5f },
                new Shot { File = "pc_store_tab", Scene = MenuScene, Device = pc, Scheme = InputScheme.KeyboardMouse, Open = "store", Wait = 2.5f },
                new Shot { File = "pc_settings_gamepad", Scene = MenuScene, Device = pc, Scheme = InputScheme.Gamepad, Settings = true, Wait = 2.5f },
                new Shot { File = "pc_hud_keyboard", Scene = ArenaScene, Device = pc, Scheme = InputScheme.KeyboardMouse, Wait = 6f },
                new Shot { File = "pc_hud_xbox", Scene = ArenaScene, Device = pc, Scheme = InputScheme.Gamepad, Pad = PadFamily.Xbox, Wait = 6f },
                new Shot { File = "pc_pause_playstation", Scene = ArenaScene, Device = pc, Scheme = InputScheme.Gamepad, Pad = PadFamily.PlayStation, Pause = true, Wait = 6f },
                new Shot { File = "pc21x9_dashboard", Scene = MenuScene, Device = Pc(2560, 1080), Scheme = InputScheme.KeyboardMouse, Wait = 2.5f },
                new Shot { File = "pc16x10_hud", Scene = ArenaScene, Device = Pc(1920, 1200), Scheme = InputScheme.KeyboardMouse, Wait = 6f },

                new Shot { File = "iphone15_dashboard", Scene = MenuScene, Device = IPhone15, Scheme = InputScheme.Touch, Wait = 2.5f },
                new Shot { File = "iphone15_settings", Scene = MenuScene, Device = IPhone15, Scheme = InputScheme.Touch, Settings = true, Wait = 2.5f },
                new Shot { File = "iphone15_hud", Scene = ArenaScene, Device = IPhone15, Scheme = InputScheme.Touch, Wait = 6f },
                new Shot { File = "pixel7_hud_lefthanded", Scene = ArenaScene, Device = Pixel7, Scheme = InputScheme.Touch, LeftHanded = true, Wait = 6f },
                new Shot { File = "pixel7_dashboard", Scene = MenuScene, Device = Pixel7, Scheme = InputScheme.Touch, Wait = 2.5f },

                new Shot { File = "ipad_dashboard", Scene = MenuScene, Device = IPad, Scheme = InputScheme.Touch, Wait = 2.5f },
                new Shot { File = "ipad_settings", Scene = MenuScene, Device = IPad, Scheme = InputScheme.Touch, Settings = true, Wait = 2.5f },
                new Shot { File = "ipad_hud", Scene = ArenaScene, Device = IPad, Scheme = InputScheme.Touch, Wait = 6f },
            };
            string only = Arg("-fpskitOnly");
            if (!string.IsNullOrEmpty(only))
                list = list.Where(s => only.Split(',').Any(o => s.File.Contains(o))).ToList();
            return list;
        }

        // ==================================================================
        // The run.
        // ==================================================================

        static List<Shot> _queue;
        static int _index;
        static int _phase;
        static double _phaseStart;
        static string _out;
        static readonly List<string> _errors = new List<string>();
        static readonly List<string> _failures = new List<string>();
        static readonly StringBuilder _report = new StringBuilder();
        static bool _leftHandedBefore;

        public static void Capture()
        {
            try
            {
                _out = Arg("-fpskitOut") ?? "Build/Devices";
                Directory.CreateDirectory(_out);
                _queue = Plan();
                _index = 0;
                _phase = 0;
                _errors.Clear(); _failures.Clear(); _report.Clear();
                _leftHandedBefore = GameSettings.LeftHanded;
                FPSKitUIKit.Import(rebuildFonts: false);
                Application.logMessageReceived += OnLog;
                FPSKitPlayMode.SuspendStartScene();
                EditorApplication.update += Tick;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Devices] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (type is LogType.Error or LogType.Exception or LogType.Assert)
                _errors.Add(message + (type == LogType.Exception ? "\n" + stack : ""));
        }

        static double Since => EditorApplication.timeSinceStartup - _phaseStart;

        static void Next(int phase)
        {
            _phase = phase;
            _phaseStart = EditorApplication.timeSinceStartup;
        }

        static void Tick()
        {
            try
            {
                if (_index >= _queue.Count) { Finish(); return; }
                var shot = _queue[_index];

                switch (_phase)
                {
                    case 0: // edit mode: open, dress, enter
                        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
                        EditorSceneManager.OpenScene(shot.Scene, OpenSceneMode.Single);
                        if (shot.Device.touch && shot.Scene == ArenaScene)
                            FPSKitMobileControls.AddMobileControls(askFirst: false);
                        GameSettings.LeftHanded = shot.LeftHanded;
                        ScreenInfo.SimulateNextSession(shot.Device);
                        EditorApplication.EnterPlaymode();
                        Next(1);
                        return;

                    case 1: // playing: set the scheme and open what the shot needs
                        if (!EditorApplication.isPlaying || Since < 0.4) return;
                        GameInput.SetScheme(shot.Scheme, shot.Pad);
                        foreach (var story in Object.FindObjectsByType<StoryPanel>(FindObjectsInactive.Include))
                            if (story.IsOpen) story.Close();
                        _uiCam = ScreenInfo.SimulationCamera;
                        if (_uiCam == null) throw new Exception("the simulated screen has no camera");
                        var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(_uiCam);
                        data.renderPostProcessing = false;
                        data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
                        Next(2);
                        return;

                    case 2:
                        if (Since < shot.Wait * 0.5) return;
                        if (shot.Settings)
                        {
                            var canvas = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude)
                                .Where(c => c.isRootCanvas && c.renderMode != RenderMode.WorldSpace)
                                .OrderBy(c => c.sortingOrder).FirstOrDefault();
                            SettingsPanel.Show(canvas);
                        }
                        if (shot.Pause && GameDirector.Instance != null) GameDirector.Instance.SetPaused(true);
                        if (!string.IsNullOrEmpty(shot.Open))
                        {
                            var menu = Object.FindAnyObjectByType<MainMenuController>();
                            if (menu == null) throw new Exception("no dashboard to open " + shot.Open + " on");
                            switch (shot.Open)
                            {
                                case "loadout": menu.OpenLoadout(); break;
                                case "info": menu.OpenInfo(); break;
                                case "quit": menu.AskToExit(); break;
                                case "store": menu.OpenStore(); break;
                                case "achievements": menu.OpenAchievements(); break;
                            }
                        }
                        Next(3);
                        return;

                    case 3:
                        if (Since < shot.Wait * 0.5) return;
                        Render(shot);
                        Measure(shot);
                        if (_uiCam != null) Object.Destroy(_uiCam.gameObject);
                        _uiCam = null;
                        EditorApplication.ExitPlaymode();
                        Next(4);
                        return;

                    case 4:
                        if (EditorApplication.isPlaying) return;
                        _index++;
                        Next(0);
                        return;
                }
            }
            catch (Exception e)
            {
                _failures.Add(e.ToString());
                Finish();
            }
        }

        // ==================================================================
        // Rendering.
        // ==================================================================

        static Camera _uiCam;
        static Texture2D Grab(Camera cam, RenderTexture rt)
        {
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
                RenderPipeline.SubmitRenderRequest(cam, request);
            }
            else
            {
                cam.targetTexture = rt;
                cam.Render();
            }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }

        static void Render(Shot shot)
        {
            int w = shot.Device.width, h = shot.Device.height;
            Canvas.ForceUpdateCanvases();

            Color32[] scene = null;
            var main = Camera.main;
            if (main != null)
            {
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                var t = Grab(main, rt);
                scene = t.GetPixels32();
                Object.DestroyImmediate(t);
                rt.Release();
            }

            var uiRt = _uiCam.targetTexture;
            _uiCam.backgroundColor = Color.black;
            var black = Grab(_uiCam, uiRt);
            _uiCam.backgroundColor = Color.white;
            var white = Grab(_uiCam, uiRt);
            var b = black.GetPixels32();
            var wpx = white.GetPixels32();
            var result = new Color32[b.Length];
            var themeBg = (Color32)UITheme.Active.background;
            for (int i = 0; i < b.Length; i++)
            {
                // Coverage from the difference between the two backgrounds, per channel,
                // averaged: C_black = aF, C_white = aF + (1 - a).
                float a = 1f - ((wpx[i].r - b[i].r) + (wpx[i].g - b[i].g) + (wpx[i].b - b[i].b)) / (3f * 255f);
                a = Mathf.Clamp01(a);
                Color32 under = scene != null ? scene[i] : themeBg;
                result[i] = new Color32(
                    (byte)Mathf.Clamp(b[i].r + (1f - a) * under.r, 0, 255),
                    (byte)Mathf.Clamp(b[i].g + (1f - a) * under.g, 0, 255),
                    (byte)Mathf.Clamp(b[i].b + (1f - a) * under.b, 0, 255), 255);
            }
            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            outTex.SetPixels32(result);
            outTex.Apply();
            File.WriteAllBytes(System.IO.Path.Combine(_out, shot.File + ".png"), outTex.EncodeToPNG());
            Object.DestroyImmediate(outTex);
            Object.DestroyImmediate(black);
            Object.DestroyImmediate(white);
            Debug.Log($"[Devices] wrote {shot.File}.png ({shot.Device.name}, {w}x{h}, {shot.Scheme})");
        }

        // ==================================================================
        // Measuring.
        // ==================================================================

        static Rect ScreenRect(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector2 a = _uiCam.WorldToScreenPoint(corners[0]);
            Vector2 b = _uiCam.WorldToScreenPoint(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        static bool Visible(Component c)
        {
            if (!c.gameObject.activeInHierarchy) return false;
            var g = c.GetComponent<Graphic>();
            if (g != null && (!g.enabled || g.canvasRenderer.GetInheritedAlpha() < 0.05f)) return false;
            foreach (var cg in c.GetComponentsInParent<CanvasGroup>())
                if (cg.alpha < 0.05f) return false;
            return true;
        }

        static void Measure(Shot shot)
        {
            var d = shot.Device;
            float dpPerPx = 160f / d.dpi;
            float pxPerMm = d.dpi / 25.4f;
            var safe = d.safeArea;
            var lines = new List<string>();

            // Touch targets and the crosshair.
            if (d.touch)
            {
                var centre = new Vector2(d.width * 0.5f, d.height * 0.5f);
                float clear = 6f * pxPerMm;
                foreach (var tb in Object.FindObjectsByType<TouchButton>(FindObjectsInactive.Exclude))
                {
                    if (!Visible(tb)) continue;
                    var r = ScreenRect((RectTransform)tb.transform);
                    float dp = Mathf.Min(r.width, r.height) * dpPerPx;
                    if (dp < 48f) _failures.Add($"{shot.File}: {tb.name} is {dp:0}dp, under 48dp");
                    var near = new Rect(centre.x - clear, centre.y - clear, clear * 2f, clear * 2f);
                    if (r.Overlaps(near)) _failures.Add($"{shot.File}: {tb.name} covers the crosshair");
                    if (!Inside(safe, r)) _failures.Add($"{shot.File}: {tb.name} is outside the safe area");
                    lines.Add($"  {tb.name}: {dp:0}dp ({Mathf.Min(r.width, r.height) / pxPerMm:0.0}mm)");
                }
            }

            // Controls inside the safe area.
            foreach (var s in Selectable.allSelectablesArray)
            {
                if (s == null || !Visible(s) || Clipped(s)) continue;
                var r = ScreenRect((RectTransform)s.transform);
                if (r.width < 1f || r.height < 1f) continue;
                if (!Inside(safe, r)) _failures.Add($"{shot.File}: {Path(s.transform)} reaches outside the safe area");
            }

            // Type size on a handheld.
            int small = 0;
            var smallest = new List<string>();
            if (d.mobile)
            {
                foreach (var t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude))
                {
                    if (t is TextMeshPro || string.IsNullOrWhiteSpace(t.text) || !Visible(t)) continue;
                    var canvas = t.canvas != null ? t.canvas.rootCanvas : null;
                    if (canvas == null || canvas.renderMode == RenderMode.WorldSpace) continue;
                    float scale = t.rectTransform.lossyScale.y / canvas.transform.lossyScale.y * canvas.scaleFactor;
                    float size = (t.enableAutoSizing ? t.fontSizeMin : t.fontSize);
                    if (t.enableAutoSizing && t.textInfo != null && t.textInfo.characterCount > 0) size = t.fontSize;
                    float dp = size * scale * dpPerPx;
                    // The kit's own text must meet the floor; the older screens are reported
                    // until they are rebuilt on the kit.
                    if (dp < 11.9f && t.GetComponent<UITextFloor>() != null)
                        _failures.Add($"{shot.File}: kit text {Path(t.transform)} is {dp:0.0}dp, under 12dp");
                    if (dp < 12f)
                    {
                        small++;
                        if (smallest.Count < 12) smallest.Add($"    {dp:0.0}dp  {Path(t.transform)}  \"{Trim(t.text)}\"");
                    }
                }
            }

            _report.AppendLine($"{shot.File}  [{d.name} {d.width}x{d.height} @{d.dpi:0}dpi, {shot.Scheme}]");
            foreach (var l in lines) _report.AppendLine(l);
            if (d.mobile) _report.AppendLine($"  text under 12dp: {small}");
            foreach (var l in smallest) _report.AppendLine(l);
        }

        /// <summary>
        /// Scrolled out of its viewport. A row further down a settings list is below the
        /// screen and masked away, which is not the same as being under the notch.
        /// </summary>
        static bool Clipped(Component c)
        {
            var mask = c.GetComponentInParent<RectMask2D>();
            if (mask == null) return false;
            var m = ScreenRect(mask.rectTransform);
            var r = ScreenRect((RectTransform)c.transform);
            return !m.Overlaps(r) || r.yMin < m.yMin - 1f || r.yMax > m.yMax + 1f;
        }

        static bool Inside(Rect safe, Rect r) =>
            r.xMin >= safe.xMin - 1f && r.xMax <= safe.xMax + 1f && r.yMin >= safe.yMin - 1f && r.yMax <= safe.yMax + 1f;

        static string Path(Transform t)
        {
            var parts = new List<string>();
            for (var p = t; p != null && parts.Count < 4; p = p.parent) parts.Insert(0, p.name);
            return string.Join("/", parts);
        }

        static string Trim(string s)
        {
            s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "").Replace("\n", " ");
            return s.Length > 40 ? s.Substring(0, 40) + "..." : s;
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            GameSettings.LeftHanded = _leftHandedBefore;
            FPSKitPlayMode.RestoreStartScene();
            foreach (var e in _errors) _failures.Add("console: " + e);
            File.WriteAllText(System.IO.Path.Combine(_out, "report.txt"), _report.ToString() +
                              (_failures.Count > 0 ? "\nFAILURES\n" + string.Join("\n", _failures) : "\nno failures\n"));
            Debug.Log("[Devices] report\n" + _report);
            if (_failures.Count == 0)
            {
                Debug.Log($"[Devices] passed: {_queue.Count} shots in {_out}");
                EditorApplication.Exit(0);
            }
            else
            {
                foreach (var f in _failures.Take(40)) Debug.LogError("[Devices] " + f);
                Debug.LogError($"[Devices] FAILED: {_failures.Count} problem(s)");
                EditorApplication.Exit(1);
            }
        }
    }
}
#endif
