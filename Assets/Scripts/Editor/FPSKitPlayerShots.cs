#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
    /// Poses: stand, walk, run, sprint, runfire, aim, reload, punch, double, kick, down.
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

        /// <summary>Batch: the right-hand close-ups only, in the Desert (quick, for tuning the grip).</summary>
        public static void BatchHands()
        {
            _batch = new Queue<(string, string)>(new[] { Arenas[0] });
            _onlyPoses = "stand,hand_idle,hand_walk,hand_run,hand_fire,hand_aim";
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
            Run(path, -1, _onlyPoses ?? DefaultPoses, "Build/PlayerShots", tag, 960, 540);
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
                RenderTexture.active = prev;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally { RenderTexture.ReleaseTemporary(rt); }
            _written.Add(path);
            return true;
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
