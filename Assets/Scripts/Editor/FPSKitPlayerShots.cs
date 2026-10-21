#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The player's own view, in a real arena, in a real level: plays the scene, drives the
    /// player through each pose with the touch controls' inputs (the same path a phone takes,
    /// so nothing is faked), and renders the game camera with the arms camera stacked on it.
    ///
    ///   FPSKit > Player > Shots (Desert Outpost)  /  Shots (Fairground)
    ///
    /// Poses: stand, walk, run, sprint, runfire, aim, reload, punch, double, kick, down; shade, shadeoff and shadefire (in a
    /// building's shade near the start: standing, without FirstPersonShade, and on the frame of a shot's flash).
    /// Writes &lt;folder&gt;/&lt;tag&gt;_&lt;pose&gt;.png. Game time runs at a fixed step
    /// (Time.captureDeltaTime), so a pose is the same however slowly an unfocused editor draws.
    /// Runs inside the open editor and leaves it as it was (start scene, time, inputs).
    /// </summary>
    public static class FPSKitPlayerShots
    {
        public const string DefaultPoses = "stand,walk,run,sprint,runfire,aim,reload,punch,double,kick,down,hand_idle,hand_walk,hand_run,hand_fire,hand_aim";
        const float Step = 1f / 30f;

        [MenuItem("FPSKit/Player/Shots (Desert Outpost)", false, 48)]
        static void DesertMenu() => Run("Assets/FPSKit_Generated/Scenes/DesertOutpost.unity", -1, DefaultPoses, "Build/PlayerShots", "desert", 960, 540);

        [MenuItem("FPSKit/Player/Shots (Fairground)", false, 49)]
        static void FairgroundMenu() => Run("Assets/FPSKit_Generated/Scenes/AbandonedFairground.unity", -1, DefaultPoses, "Build/PlayerShots", "fairground", 960, 540);

        static readonly (string path, string tag)[] Arenas =
        {
            ("Assets/FPSKit_Generated/Scenes/DesertOutpost.unity", "desert"),
            ("Assets/FPSKit_Generated/Scenes/AbandonedFairground.unity", "fairground"),
        };

        static Queue<(string path, string tag)> _batch;

        /// <summary>
        /// Batch: both arenas, one after the other, then exit. Needs a graphics device:
        ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitPlayerShots.Batch
        /// </summary>
        public static void Batch()
        {
            _batch = new Queue<(string, string)>(Arenas);
            EditorApplication.update += NextArena;
        }

        /// <summary>Batch: Desert Outpost's level 23 ("Blackout"), standing and after a run, then exit.</summary>
        public static void BatchBlackout()
        {
            _batch = new Queue<(string, string)>();
            _blackout = true;
            EditorApplication.update += NextArena;
        }

        static bool _blackout;
        static string _onlyPoses;

        /// <summary>
        /// Batch: the movement-and-action sequence in Desert Outpost (stand, walk, run, sprint, sprint into fire,
        /// aim, reload, punch, double punch, kick), then exit. Needs a graphics device:
        ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitPlayerShots.BatchSequence
        /// </summary>
        public static void BatchSequence()
        {
            _batch = new Queue<(string, string)>(new[] { Arenas[0] });
            _onlyPoses = "stand,walk,run,sprint,runfire,aim,reload,punch,double,kick";
            EditorApplication.update += NextArena;
        }

        /// <summary>
        /// Batch: the fighting moments in Desert Outpost (hip fire, punch, double punch, rifle butt and kick, each at its
        /// moment of impact), then exit, as &lt;tag&gt;_&lt;pose&gt;.png with the tag from <c>-fpskitTag</c> (default "fight").
        ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitPlayerShots.BatchFight -fpskitTag before
        /// </summary>
        public static void BatchFight()
        {
            _batch = new Queue<(string, string)>(new[] { (Arenas[0].path, Arg("-fpskitTag", "fight")) });
            _onlyPoses = "hipfire,butt,punch,double,kick";
            _measureHud = true;
            EditorApplication.update += NextArena;
        }

        /// <summary>
        /// Batch: the same arena as a Pixel 7 (2400x1080 at 416 dpi, the touch controls on), standing, firing from the hip
        /// the rifle butt and the punches and kick, each with the fire button and the ability slots outlined and how much of them the arms and gun cover
        /// (&lt;tag&gt;_pixel7_report.txt). Nothing of the player's should be under it.
        ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitPlayerShots.BatchFightPhone -fpskitTag after
        /// </summary>
        public static void BatchFightPhone()
        {
            _phone = _measureHud = true;
            EditorSceneManager.OpenScene(Arenas[0].path, OpenSceneMode.Single);
            FPSKitMobileControls.AddMobileControls(askFirst: false);
            ScreenInfo.SimulateNextSession(new ScreenInfo.Device
            {
                name = "Pixel 7", width = 2400, height = 1080, dpi = 416f,
                safeArea = new Rect(136, 0, 2400 - 136, 1080), touch = true, mobile = true,
            });
            _batch = new Queue<(string, string)>(new[] { (Arenas[0].path, Arg("-fpskitTag", "fight") + "_pixel7") });
            _onlyPoses = "stand,hipfire,butt,punch,double,kick";
            EditorApplication.update += NextArena;
        }

        static bool _phone, _measureHud;

        static string Arg(string name, string fallback)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        /// <summary>Batch: the right-hand close-ups only, in the Desert (quick, for tuning the grip).</summary>
        public static void BatchHands()
        {
            _batch = new Queue<(string, string)>(new[] { Arenas[0] });
            _onlyPoses = "stand,hand_idle,hand_walk,hand_run,hand_fire,hand_aim";
            EditorApplication.update += NextArena;
        }

        /// <summary>
        /// Batch: the light on the gun in the Desert -- in the sun, in a building's shade (FirstPersonShade), and firing
        /// there on the frame the muzzle flash is lit (FPSMuzzleFlashLight).
        ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitPlayerShots.BatchLight
        /// </summary>
        public static void BatchLight()
        {
            _batch = new Queue<(string, string)>(new[] { Arenas[0] });
            _onlyPoses = System.Environment.GetEnvironmentVariable("FPSKIT_SHOT_POSES") ?? "stand,shade,shadeoff,shadefire,hand_idle";
            EditorApplication.update += NextArena;
        }

        /// <summary>
        /// Batch: every gun's right hand on its grip, seen from outside (red marker = the wrist socket)
        /// and from the eye, at the hip and aimed. Edit mode, so seconds.
        ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitPlayerShots.BatchGrips
        /// </summary>
        public static void BatchGrips()
        {
            // The preview adds a scratch scene, which an untitled batch scene does not allow: open a real one.
            EditorSceneManager.OpenScene("Assets/FPSKit_Generated/Scenes/Menu.unity");
            var written = FPSKitViewModels.Preview("Build/Grips", null, 640, 400, "hip,aim", "eye,rhand");
            Debug.Log("[FPSKit] Grip previews: " + string.Join(", ", written));
            FPSKitBatch.Exit(0);
        }

        static void NextArena()
        {
            if (Running || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (_batch == null || _batch.Count == 0 && !_blackout)
            {
                EditorApplication.update -= NextArena;
                FPSKitPlayMode.RestoreStartScene();
                FPSKitBatch.Exit(Report.Contains("failed") ? 1 : 0);
                return;
            }
            if (_blackout)
            {
                _blackout = false;
                Run(Arenas[0].path, 22, "stand,run,down", "Build/PlayerShots", "blackout", 960, 540);
                return;
            }
            var (path, tag) = _batch.Dequeue();
            Run(path, -1, _onlyPoses ?? DefaultPoses, "Build/PlayerShots", tag, _phone ? 1200 : 960, 540);
        }

        public static bool Running { get; private set; }
        public static string Report { get; private set; } = "";

        static Queue<string> _poses;
        static string _folder, _tag, _pose;
        static int _width, _height, _frame, _phase, _level;
        static List<string> _written;
        static PlayerMotor _motor;
        static Weapon _weapon;
        static MeleeStrike _melee;
        static Health _health;
        static FieldInfo _pitch;
        static Vector3 _startPos;
        static Quaternion _startRot;
        static bool _started;
        static int _lastGameFrame = -1;

        /// <summary>Plays <paramref name="scenePath"/> (level index, or -1 for the scene's own) and shoots each pose.</summary>
        public static void Run(string scenePath, int level, string poses, string folder, string tag, int width, int height)
        {
            if (Running || EditorApplication.isPlaying) { Debug.LogWarning("[FPSKit] Player shots: already running or in play mode"); return; }
            if (EditorSceneManager.GetActiveScene().path != scenePath)
            {
                if (EditorSceneManager.GetActiveScene().isDirty) { Debug.LogError("[FPSKit] Player shots: the open scene has unsaved changes"); return; }
                EditorSceneManager.OpenScene(scenePath);
            }
            if (level >= 0)
                foreach (var lm in Object.FindObjectsByType<LevelManager>()) lm.defaultLevelIndex = level;

            _poses = new Queue<string>(poses.Split(','));
            _folder = folder; _tag = tag; _width = width; _height = height; _level = level;
            _written = new List<string>();
            if (_batch == null) Report = "";
            Directory.CreateDirectory(folder);
            Running = true;
            FPSKitPlayMode.SuspendStartScene();
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!Running) return;
            if (!EditorApplication.isPlaying) return;   // still entering
            try
            {
                if (_motor == null)
                {
                    _motor = Object.FindAnyObjectByType<PlayerMotor>();
                    if (_motor == null) return;
                    _weapon = _motor.GetComponentInChildren<Weapon>();
                    _melee = _motor.GetComponent<MeleeStrike>();
                    _health = _motor.GetComponent<Health>();
                    _pitch = typeof(PlayerMotor).GetField("_pitch", BindingFlags.Instance | BindingFlags.NonPublic);
                    Time.captureDeltaTime = Step;
                    MobileInput.Active = true;
                    _frame = 0;
                    _pose = "warmup";
                    _phase = 0;
                    _started = false;
                }

                // Once per game frame: the editor can tick several times while one heavy frame is drawn.
                if (Time.frameCount == _lastGameFrame) return;
                _lastGameFrame = Time.frameCount;
                if (_health != null && _health.Normalized < 0.9f) _health.ResetHealth();
                _frame++;
                if (_frame > Frames(_pose == "warmup" ? 60f : 12f))
                {
                    Report += $"{_pose}: timed out (time {Time.time:0.0}, briefing {Object.FindAnyObjectByType<LevelManager>()?.IsBriefing}, paused {EditorApplication.isPaused}, timeScale {Time.timeScale}, dead {(_health != null && _health.IsDead)}, " +
                              $"lowered {(_weapon != null && _weapon.IsLowered)}, strike {(_melee != null ? _melee.Current.ToString() : "-")}, " +
                              $"melee on {(_melee != null && _melee.isActiveAndEnabled)}, ammo {(_weapon != null ? _weapon.CurrentAmmo : -1)})\n";
                    Next();
                    return;
                }
                if (Act()) Next();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Report += "failed: " + e.Message + "\n";
                Finish();
            }
        }

        static void Next()
        {
            Clear();
            // Every pose from the same spot: the runs carried the player 30 m and more into the arena.
            if (!_started) { _startPos = _motor.transform.position; _startRot = _motor.transform.rotation; _started = true; }
            else
            {
                var cc = _motor.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                _motor.transform.SetPositionAndRotation(_startPos, _startRot);
                if (cc != null) cc.enabled = true;
            }
            if (_health != null) _health.ResetHealth();
            _frame = 0;
            _phase = 0;
            if (_poses.Count == 0) { Finish(); return; }
            _pose = _poses.Dequeue().Trim();
        }

        /// <summary>Inputs off: standing still, level, nothing held.</summary>
        static void Clear()
        {
            MobileInput.Move = Vector2.zero;
            MobileInput.Aim = false;
            MobileInput.Crouch = false;
            MobileInput.SetSprintButton(false);
            MobileInput.SetFireButton(false);
            Pitch(0f);
        }

        static void Pitch(float degrees)
        {
            if (_pitch != null && _motor != null) _pitch.SetValue(_motor, degrees);
        }

        static int Frames(float seconds) => Mathf.RoundToInt(seconds / Step);

        /// <summary>One frame of the current pose; true when it is shot and done.</summary>
        static bool Act()
        {
            switch (_pose)
            {
                case "warmup":
                    // Past the level's briefing (it holds the player and lowers the gun) and settled.
                    var lm = Object.FindAnyObjectByType<LevelManager>();
                    if (lm != null && lm.IsBriefing) { _frame = Mathf.Min(_frame, Frames(1f)); return false; }
                    return _frame >= Frames(2f);
                case "stand": return _frame >= Frames(1.2f) && Shoot();
                // Close-ups of the right hand on the grip (cropped round it from a full-HD frame).
                case "hand_idle": return _frame >= Frames(1.2f) && Shoot(closeUp: true);
                case "hand_walk": MobileInput.Move = new Vector2(0f, 0.45f); return _frame >= Frames(1.6f) && Shoot(closeUp: true);
                case "hand_run": MobileInput.Move = new Vector2(0f, 1f); return _frame >= Frames(1.6f) && Shoot(closeUp: true);
                case "hand_fire": MobileInput.SetFireButton(_frame >= Frames(1f)); return _frame >= Frames(1f) + 2 && Shoot(closeUp: true);
                case "hand_aim": MobileInput.Aim = true; return _frame >= Frames(1.0f) && Shoot(closeUp: true);
                case "down": Pitch(65f); return _frame >= Frames(1.2f) && Shoot();
                // In a building's shade: standing, and firing (shot on a frame the flash is lit, which lasts one or two).
                // shadeoff: the same without FirstPersonShade, to see what it does.
                case "shade":
                case "shadeoff":
                case "shadefire":
                    if (_phase == 0) { if (!IntoShade()) { Report += $"{_pose}: no shade near the start\n"; return true; } _phase = 1; _frame = 0; return false; }
                    if (_pose != "shadefire") return _frame >= Frames(1.2f) && ShootShaded(_pose == "shade");
                    MobileInput.SetFireButton(_frame >= Frames(1.2f));
                    return _frame >= Frames(1.2f) && FlashLit() && ShootShaded(true);
                case "walk": MobileInput.Move = new Vector2(0f, 0.45f); return _frame >= Frames(1.6f) && Shoot();
                case "run": MobileInput.Move = new Vector2(0f, 1f); return _frame >= Frames(1.6f) && Shoot();
                case "sprint":
                    MobileInput.Move = new Vector2(0f, 1f);
                    MobileInput.SetSprintButton(true);
                    return _frame >= Frames(1.8f) && Shoot();
                case "runfire":
                    MobileInput.Move = new Vector2(0f, 1f);
                    MobileInput.SetSprintButton(true);
                    MobileInput.SetFireButton(_frame >= Frames(1.5f));
                    return _frame >= Frames(1.5f) + Frames(0.2f) && Shoot();
                case "aim": MobileInput.Aim = true; return _frame >= Frames(1.0f) && Shoot();
                case "hipfire": MobileInput.SetFireButton(_frame >= Frames(1f)); return _frame >= Frames(1f) + 4 && Shoot();
                case "butt":
                    // The first press is the rifle butt.
                    if (_phase == 0) { if (_frame >= Frames(1f)) { _melee.TryPunch(); _phase = 1; } return false; }
                    if (_frame > Frames(5f)) { Report += "butt: no rifle butt\n"; return true; }
                    return AtImpact(MeleeStrike.Strike.Butt) && Shoot();
                case "reload":
                    // A few rounds off first: a full magazine is not reloaded.
                    if (_phase == 0) { MobileInput.SetFireButton(_frame < Frames(0.4f)); if (_frame >= Frames(0.9f)) { MobileInput.QueueReload(); _phase = 1; } return false; }
                    return _weapon != null && _weapon.IsReloading && _weapon.ReloadProgress >= 0.45f && Shoot();
                case "punch":
                    // The first press is the rifle butt; the next, in the chain, the punch.
                    if (_phase == 0) { if (_frame >= Frames(0.5f)) { _melee.TryPunch(); _phase = 1; } return false; }
                    if (_phase == 1) { if (_melee.Current == MeleeStrike.Strike.None) { _melee.TryPunch(); _phase = 2; } return false; }
                    if (_frame > Frames(5f)) { Report += "punch: no jab\n"; return true; }
                    return AtImpact(MeleeStrike.Strike.Jab) && Shoot();
                case "double":
                    // Straight on from the punch: the next press in the chain is the cross.
                    if (_phase == 0) { if (_melee.Current == MeleeStrike.Strike.None) { _melee.TryPunch(); _phase = 1; } return false; }
                    if (_frame > Frames(4f)) { Report += "double: no cross\n"; return true; }
                    return AtImpact(MeleeStrike.Strike.Cross) && Shoot();
                case "kick":
                    if (_phase == 0) { if (_frame >= Frames(2.5f)) { _melee.TryKick(); _phase = 1; } return false; }
                    return AtImpact(MeleeStrike.Strike.Kick) && Shoot();
                default:
                    Report += $"no pose {_pose}\n";
                    return true;
            }
        }

        /// <summary>
        /// Shoots with FirstPersonShade on or off, and reports how far in shade it put the gun and the sun's brightness
        /// the arms camera was drawn with (against the sun's own).
        /// </summary>
        static bool ShootShaded(bool shadeOn)
        {
            var shade = Object.FindAnyObjectByType<FirstPersonShade>();
            var sun = Sun();
            float full = sun != null ? sun.intensity : 0f, seen = -1f;
            void Begin(ScriptableRenderContext context, Camera cam) { if (shade != null && cam == shade.armsCamera && sun != null) seen = sun.intensity; }
            if (shade != null && !shadeOn) shade.enabled = false;
            RenderPipelineManager.beginCameraRendering += Begin;     // after FirstPersonShade's own handler: sees its change
            try { Shoot(); }
            finally
            {
                RenderPipelineManager.beginCameraRendering -= Begin;
                if (shade != null) shade.enabled = true;
            }
            string line = $"{_pose}: FirstPersonShade {(shade == null ? "missing" : shadeOn ? shade.Shade.ToString("0.00") : "off")}, sun {full:0.00}, " +
                          $"drawn on the gun at {(seen < 0f ? "(arms camera not rendered)" : seen.ToString("0.00"))}";
            Report += line + "\n";
            Debug.Log("[FPSKit] " + line);
            return true;
        }

        static Light Sun()
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.shadows != LightShadows.None) return l;
            return null;
        }

        static bool FlashLit()
        {
            var flash = _weapon != null ? _weapon.muzzleFlashLight : null;
            var light = flash != null ? flash.GetComponent<Light>() : null;
            return light != null && light.enabled;
        }

        /// <summary>
        /// Puts the player on the nearest open-sky navmesh spot within 30 m of the start where the body, the gun and a
        /// little round them are all shaded from the sun; facing as at the start.
        /// </summary>
        static bool IntoShade()
        {
            var sun = Sun();
            if (sun == null || !NavMesh.SamplePosition(_startPos, out var home, 3f, NavMesh.AllAreas)) return false;
            float lift = _startPos.y - home.position.y;
            Vector3 toSun = -sun.transform.forward;
            int mask = ~LayerMask.GetMask("Player", "FirstPerson", "PlayerBounds", "Ignore Raycast", "UI", "Civilian", "Enemy");
            var around = new[] { Vector3.zero, Vector3.right * 0.6f, Vector3.left * 0.6f, Vector3.forward * 0.6f, Vector3.back * 0.6f };
            for (float r = 2f; r <= 30f; r += 1f)
                for (int a = 0; a < 24; a++)
                {
                    float angle = a * 15f * Mathf.Deg2Rad;
                    var at = _startPos + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * r;
                    if (!NavMesh.SamplePosition(at, out var hit, 1f, NavMesh.AllAreas)) continue;
                    Vector3 feet = hit.position;
                    if (Physics.Raycast(feet + Vector3.up * 2f, Vector3.up, 100f, mask, QueryTriggerInteraction.Ignore)) continue;   // indoors
                    bool shaded = true;
                    foreach (float h in new[] { 0.9f, 1.3f, 1.7f })
                        foreach (var side in around)
                            shaded &= Physics.Raycast(feet + Vector3.up * h + side, toSun, 200f, mask, QueryTriggerInteraction.Ignore);
                    if (!shaded) continue;
                    var cc = _motor.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    _motor.transform.SetPositionAndRotation(feet + Vector3.up * lift, _startRot);
                    if (cc != null) cc.enabled = true;
                    return true;
                }
            return false;
        }

        static bool AtImpact(MeleeStrike.Strike strike)
            => _melee != null && _melee.Current == strike && _melee.StrikeProgress >= MeleeStrike.ImpactAt(strike) - 0.02f;

        /// <summary>A 720x480 window of the frame centred on where the right hand's grip is seen.</summary>
        static Texture2D CropRoundHand(Texture2D full, Camera cam)
        {
            var spec = _weapon != null ? _weapon.GetComponentInChildren<ViewModelSpec>() : null;
            var arms = cam.transform.Find("ArmsCamera");
            var view = arms != null ? arms.GetComponent<Camera>() : cam;
            Vector2 at = new Vector2(full.width * 0.7f, full.height * 0.25f);
            if (spec != null && spec.rightHand != null && view != null)
            {
                Vector3 vp = view.WorldToViewportPoint(spec.rightHand.position);
                if (vp.z > 0f) at = new Vector2(vp.x * full.width, vp.y * full.height);
            }
            const int w = 720, h = 480;
            // The bottom of the frame round the grip: at the hip the hand is at the frame's lower edge.
            int x = Mathf.Clamp(Mathf.RoundToInt(at.x - w * 0.5f), 0, full.width - w);
            int y = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(at.y, h * 0.5f) - h * 0.5f), 0, full.height - h);
            var crop = new Texture2D(w, h, TextureFormat.RGB24, false);
            crop.SetPixels(full.GetPixels(x, y, w, h));
            crop.Apply();
            Object.DestroyImmediate(full);
            return crop;
        }

        /// <summary>The game camera and its stack (the arms camera on top) into a PNG.</summary>
        static bool Shoot(bool closeUp = false)
        {
            var cam = _weapon != null && _weapon.fpsCamera != null ? _weapon.fpsCamera : Camera.main;
            string path = $"{_folder}/{_tag}_{_pose}.png";
            int width = closeUp ? 1920 : _width, height = closeUp ? 1080 : _height;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = rt };
                RenderPipeline.SubmitRenderRequest(cam, request);
                RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                if (closeUp) tex = CropRoundHand(tex, cam);
                if (_measureHud) HudCover(cam, tex, rt);
                RenderTexture.active = prev;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally { RenderTexture.ReleaseTemporary(rt); }
            _written.Add(path);
            return true;
        }

        /// <summary>
        /// How much of the HUD's ability slots (and on a phone the touch fire button) the player's own arms, gun and body
        /// cover in this frame: the frame drawn again without them, the pixels that change are theirs; each rectangle from
        /// its place on its canvas, outlined on the shot in amber. Written to &lt;tag&gt;_report.txt.
        /// </summary>
        static void HudCover(Camera cam, Texture2D tex, RenderTexture rt)
        {
            var targets = new List<(string name, RectTransform rect)>();
            foreach (var b in Object.FindObjectsByType<TouchButton>(FindObjectsSortMode.None))
                if (b.action == TouchButton.ActionKind.Fire && b.isActiveAndEnabled) { targets.Add(("fire button", (RectTransform)b.transform)); break; }
            foreach (var t in Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None))
                if (t.name == "Abilities" && t.gameObject.activeInHierarchy && t.GetComponentInParent<HudView>() != null) { targets.Add(("ability slots", t)); break; }
            if (targets.Count == 0) { Report += $"{_pose}: no HUD targets on screen\n"; return; }

            // The frame without the player's own arms, gun and body.
            var mine = new List<Renderer>();
            foreach (var rend in _motor.GetComponentsInChildren<Renderer>()) if (rend.enabled) mine.Add(rend);
            foreach (var rend in mine) rend.enabled = false;
            var request = new RenderPipeline.StandardRequest { destination = rt };
            RenderPipeline.SubmitRenderRequest(cam, request);
            RenderPipeline.SubmitRenderRequest(cam, request);
            foreach (var rend in mine) rend.enabled = true;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var bare = new Texture2D(tex.width, tex.height, TextureFormat.RGB24, false);
            bare.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            bare.Apply();
            RenderTexture.active = prev;
            var full = tex.GetPixels32();
            var without = bare.GetPixels32();
            Object.DestroyImmediate(bare);

            var line = new System.Text.StringBuilder($"{_pose}:");
            var amber = new Color(1f, 0.7f, 0.1f);
            foreach (var (name, rect) in targets)
            {
                var root = (RectTransform)rect.GetComponentInParent<Canvas>().rootCanvas.transform;
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                Vector2 min = Vector2.one * 9f, max = -Vector2.one * 9f;
                foreach (var c in corners)
                {
                    Vector2 local = root.InverseTransformPoint(c);
                    Vector2 n = (local - root.rect.min) / root.rect.size;
                    min = Vector2.Min(min, n); max = Vector2.Max(max, n);
                }
                var r = new RectInt(Mathf.RoundToInt(min.x * tex.width), Mathf.RoundToInt(min.y * tex.height),
                                    Mathf.RoundToInt((max.x - min.x) * tex.width), Mathf.RoundToInt((max.y - min.y) * tex.height));
                int covered = 0, total = 0;
                for (int y = Mathf.Max(0, r.yMin); y < Mathf.Min(tex.height, r.yMax); y++)
                    for (int x = Mathf.Max(0, r.xMin); x < Mathf.Min(tex.width, r.xMax); x++)
                    {
                        total++;
                        Color32 a = full[y * tex.width + x], b2 = without[y * tex.width + x];
                        if (Mathf.Abs(a.r - b2.r) + Mathf.Abs(a.g - b2.g) + Mathf.Abs(a.b - b2.b) > 30) covered++;
                    }
                for (int x = Mathf.Max(0, r.xMin); x < Mathf.Min(tex.width, r.xMax); x++)
                    for (int t = 0; t < 2; t++) { tex.SetPixel(x, Mathf.Clamp(r.yMin + t, 0, tex.height - 1), amber); tex.SetPixel(x, Mathf.Clamp(r.yMax - 1 - t, 0, tex.height - 1), amber); }
                for (int y = Mathf.Max(0, r.yMin); y < Mathf.Min(tex.height, r.yMax); y++)
                    for (int t = 0; t < 2; t++) { tex.SetPixel(Mathf.Clamp(r.xMin + t, 0, tex.width - 1), y, amber); tex.SetPixel(Mathf.Clamp(r.xMax - 1 - t, 0, tex.width - 1), y, amber); }
                line.Append($" {name} {(total > 0 ? covered * 100f / total : 0f):0.0}% covered ({r.width}x{r.height} px at {r.x},{r.y})");
                // The right hand on its grip: a 6 cm circle round it as the arms camera sees it, and how far it is from the rectangle.
                var spec = _motor.GetComponentInChildren<ViewModelSpec>();
                var armsT = cam.transform.Find("ArmsCamera");
                var armsCam = armsT != null ? armsT.GetComponent<Camera>() : cam;
                if (spec != null && spec.rightHand != null && armsCam != null && _melee != null && _melee.FistsUp < 0.01f)
                {
                    Vector3 c = armsCam.WorldToViewportPoint(spec.rightHand.position);
                    Vector3 e = armsCam.WorldToViewportPoint(spec.rightHand.position + armsCam.transform.right * 0.06f);
                    var hand = new Vector2(c.x * tex.width, c.y * tex.height);
                    float radius = Vector2.Distance(hand, new Vector2(e.x * tex.width, e.y * tex.height));
                    float dx = Mathf.Max(r.xMin - hand.x, 0f, hand.x - r.xMax), dy = Mathf.Max(r.yMin - hand.y, 0f, hand.y - r.yMax);
                    float gap = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    line.Append(gap > 0f ? $", the right hand clear of it by {gap:0} px" : $", the right hand OVER it by {-gap:0} px");
                }
                line.Append(";");
            }
            tex.Apply();
            Report += line + "\n";
            File.AppendAllText($"{_folder}/{_tag}_report.txt", line + "\n");
        }

        static void Finish()
        {
            Clear();
            MobileInput.Active = false;
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
            EditorApplication.update -= Tick;
            _motor = null;
            Running = false;
            Report += "wrote " + string.Join(", ", _written);
            Debug.Log($"[FPSKit] Player shots ({_tag}): " + Report);
            EditorApplication.isPlaying = false;
            if (_batch == null) EditorApplication.delayCall += FPSKitPlayMode.RestoreStartScene;
        }
    }
}
#endif
