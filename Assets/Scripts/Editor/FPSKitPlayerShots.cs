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
    /// building's shade near the start: standing, without FirstPersonShade, and on the frame of a shot's flash);
    /// impacts, impactsafter, impactswall, impactsstone (a burst into the shop beside the start: its shutter, the sign over
    /// it, the stone pier beside it).
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

        /// <summary>
        /// Batch: the grab and punch (MeleeGrapple) in Desert Outpost, against a real enemy stood in front: the grab, held,
        /// a punch to the face and the frame after, the overhand wound up and landing, him on the floor; then the hook to the
        /// belly, him doubled over, taken bent and the overhand. Each from the player's eyes and from a camera at the side
        /// (&lt;tag&gt;_grapple_NN_&lt;beat&gt;.png and _side.png).
        ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitPlayerShots.BatchGrapple
        /// </summary>
        public static void BatchGrapple()
        {
            _batch = new Queue<(string, string)>(new[] { (Arenas[0].path, Arg("-fpskitTag", "desert")) });
            _onlyPoses = "grapple";
            EditorApplication.update += NextArena;
        }

        /// <summary>
        /// Batch: the grab and punch on film -- every frame of one scripted fight, from the player's eyes and from the side
        /// (Build/PlayerShots/&lt;tag&gt;_video/fp_NNNNN.jpg, side_NNNNN.jpg; Tools/shots/frames_to_video.py makes the mp4).
        /// He is grabbed and left a moment (he pulls and punches back), punched three times (the third into his wind-up),
        /// finished with the overhand; a second is grabbed and left alone (he wrenches free and runs).
        ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitPlayerShots.BatchGrappleVideo
        /// </summary>
        public static void BatchGrappleVideo()
        {
            _batch = new Queue<(string, string)>(new[] { (Arenas[0].path, Arg("-fpskitTag", "desert")) });
            _onlyPoses = "grapple_video";
            EditorApplication.update += NextArena;
        }

        /// <summary>
        /// Batch: each way of holding a man by the head (MeleeSettings.Grip), photographed from the eyes a moment into the hold
        /// and again as he struggles (&lt;tag&gt;_grip_&lt;style&gt;_a/_b.png), for choosing between them.
        ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitPlayerShots.BatchGrappleGrips
        /// </summary>
        public static void BatchGrappleGrips()
        {
            _batch = new Queue<(string, string)>(new[] { (Arenas[0].path, Arg("-fpskitTag", "desert")) });
            _onlyPoses = "grapple_grips";
            EditorApplication.update += NextArena;
        }

        /// <summary>
        /// Batch: a calm soldier on his beat, filmed every frame from beside him and from 15 m (&lt;tag&gt;_patrol/fp_ and
        /// side_NNNNN.jpg; Tools/shots/frames_to_video.py). He keeps his own mind and is never shown the player.
        ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitPlayerShots.BatchPatrolVideo
        /// </summary>
        public static void BatchPatrolVideo()
        {
            _batch = new Queue<(string, string)>(new[] { (Arenas[0].path, Arg("-fpskitTag", "desert")) });
            _onlyPoses = "patrol_video";
            EditorApplication.update += NextArena;
        }

        /// <summary>
        /// Batch: the light round the desert town, for judging it: the street from the start, a shopfront, a man's face in
        /// the sun and in a building's shade, the ground and upper floors of the nearest houses, and a shop's interior seen
        /// through its front (&lt;tag&gt;_light_NN_&lt;place&gt;.png).
        ///   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitPlayerShots.BatchLightingTour [-fpskitTag before]
        /// </summary>
        public static void BatchLightingTour()
        {
            _batch = new Queue<(string, string)>(new[] { (Arenas[0].path, Arg("-fpskitTag", "desert")) });
            _onlyPoses = "lighting_tour";
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
        static FieldInfo _pitch, _yaw;
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
                    _yaw = typeof(PlayerMotor).GetField("_yaw", BindingFlags.Instance | BindingFlags.NonPublic);
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
                if (_frame > Frames(_pose == "warmup" ? 60f : _pose.StartsWith("grapple") || _pose.EndsWith("_video") || _pose == "lighting_tour" ? 40f : 12f))
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
            Turn(0f);
        }

        /// <summary>Turned <paramref name="degrees"/> from the way the player faced at the start (the motor keeps its own yaw).</summary>
        static void Turn(float degrees)
        {
            if (_yaw != null && _motor != null && _started) _yaw.SetValue(_motor, _startRot.eulerAngles.y + degrees);
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
                // Impacts (FPSImpactManager): turned to the shop beside the desert's start -- its shutter (metal: dents,
                // sparks, a flash), or the stone over it -- a burst; shot while firing, or a second after (marks, dust).
                case "impacts":
                case "impactswall":
                case "impactsafter":
                case "impactsstone":
                    Turn(_pose == "impactsstone" ? 75f : 90f);
                    if (_pose == "impactswall") Pitch(-28f);
                    if (_pose == "impactsstone") Pitch(-4f);
                    MobileInput.SetFireButton(_frame >= 2 && _frame < Frames(0.6f));
                    return (_pose == "impactsafter" ? _frame >= Frames(1.5f) : _frame >= Frames(0.5f)) && Shoot();
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
                case "grapple": return Grapple();
                case "grapple_video": return GrappleVideo();
                case "grapple_grips": return GrappleGrips();
                case "patrol_video": return PatrolVideo();
                case "lighting_tour": return LightingTour();
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

        // ---- the grab and punch ------------------------------------------------------------------------------------

        static GameObject _foe;
        static EnemyAI _foeAI;
        static int _beat, _phaseFrame;
        static Camera _side;

        static void Phase(int phase) { _phase = phase; _phaseFrame = _frame; }

        /// <summary>A beat that never came: why, in the report.</summary>
        static bool GaveUp()
        {
            float d = _foe != null ? Vector3.Distance(FoeHead(), _weapon != null && _weapon.fpsCamera != null ? _weapon.fpsCamera.transform.position : _motor.transform.position) : -1f;
            float roots = _foe != null ? Vector3.ProjectOnPlane(_foe.transform.position - _motor.transform.position, Vector3.up).magnitude : -1f;
            float drift = Vector3.ProjectOnPlane(_motor.transform.position - _placedAt, Vector3.up).magnitude;
            var cam = _weapon != null && _weapon.fpsCamera != null ? _weapon.fpsCamera.transform : _motor.transform;
            float aimOff = _foe != null ? Vector3.Angle(cam.forward, FoeHead() - cam.position) : -1f;
            Vector3 toPlayer = _foe != null ? Vector3.ProjectOnPlane(cam.position - _foe.transform.position, Vector3.up) : Vector3.zero;
            float facing = _foe != null ? Vector3.Angle(Vector3.ProjectOnPlane(_foe.transform.forward, Vector3.up), toPlayer) : -1f;
            string under = Physics.Raycast(cam.position, cam.forward, out var hit, 3f) ? hit.collider.name + (hit.collider.GetComponent<Hitbox>() is Hitbox hb ? " (" + hb.Part + ")" : "") : "nothing";
            Report += $"grapple: crosshair {aimOff:0} deg off his head, he faces {facing:0} deg off the player, under the crosshair {under}, can grab {(_foe != null && _melee.CanGrab(_foe.GetComponent<Health>(), _foeAI))}\n";
            Report += $"grapple: gave up in phase {_phase} (roots {roots:0.00} m apart, player drifted {drift:0.00} m since placed, thrown {_melee.Thrown}, current {_melee.Current}, holding {_melee.Holding}, " +
                      $"target {(_melee.Target != null ? _melee.Target.name : "none")}, foe head {d:0.00} m from the eye, gun away {_melee.GunAway:0.00})\n";
            return true;
        }
        static int InPhase => _frame - _phaseFrame;
        static bool At(MeleeStrike.Strike strike, float k)
            => _melee != null && _melee.Current == strike && _melee.StrikeProgress >= k;

        /// <summary>A Grunt from the level's own prefab, on the navmesh <paramref name="metres"/> ahead, facing the player, still.</summary>
        static void Foe(float metres)
        {
            if (_foe != null) Object.Destroy(_foe);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FPSKit_Generated/Enemy.prefab");
            // Measured from the eye, and only nudged onto the navmesh: snapped from a metre away near the desert's start, he
            // stood 1.3 m off, out of grab reach.
            Vector3 ahead = Vector3.ProjectOnPlane(_motor.transform.forward, Vector3.up).normalized;
            Vector3 eye = _weapon != null && _weapon.fpsCamera != null ? _weapon.fpsCamera.transform.position : _motor.transform.position;
            Vector3 at = new Vector3(eye.x, _motor.transform.position.y, eye.z) + ahead * metres;
            if (NavMesh.SamplePosition(at, out var hit, 0.3f, NavMesh.AllAreas)) at = hit.position;
            _foe = Object.Instantiate(prefab, at, Quaternion.LookRotation(-ahead));
            _foe.name = "Shots_Foe";
            FPSKitEnemyRoster.GetOrCreate("Grunt").ApplyTo(_foe);
            _foeAI = _foe.GetComponent<EnemyAI>();
            if (_foeAI != null) { _foeAI.enabled = false; _foeAI.target = _motor.transform; }
            // Removed, not switched off: a switched-off mind keeps a stale look point (the world's origin) that the body
            // goes on aiming at, twisting up to 70 degrees off the player; with none, the body aims at its target.
            var brain = _foe.GetComponent<EnemyBrain>();
            if (brain != null) Object.DestroyImmediate(brain);
            // The player to him (the navmesh near the start is cut back, so he cannot always be stood close enough).
            var cc = _motor.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            Vector3 eyeOffset = Vector3.ProjectOnPlane(eye - _motor.transform.position, Vector3.up);
            _motor.transform.position = new Vector3(at.x, _motor.transform.position.y, at.z) + ahead * -metres - eyeOffset;
            if (cc != null) cc.enabled = true;
            // Then stepped along the line until the eye is 0.8 m from his head, measured (the eye is not where the root says).
            for (int i = 0; i < 3; i++)
            {
                Physics.SyncTransforms();
                Vector3 e = _weapon != null && _weapon.fpsCamera != null ? _weapon.fpsCamera.transform.position : _motor.transform.position + Vector3.up * 1.6f;
                float gap = Vector3.ProjectOnPlane(FoeHead() - e, Vector3.up).magnitude - 0.8f;
                if (cc != null) cc.enabled = false;
                Vector3 was = _motor.transform.position;
                _motor.transform.position += ahead * gap;
                if (cc != null) cc.enabled = true;
                Report += $"grapple: placing {i}: gap {gap:0.00} m, root moved {Vector3.Distance(was, _motor.transform.position):0.00}, eye {e}, head {FoeHead()}, foe root {_foe.transform.position}, ahead {ahead}\n";
            }
            _placedAt = _motor.transform.position;
            Physics.SyncTransforms();
        }

        static Vector3 _placedAt, _foeAt;

        static Vector3 FoeHead() { var b = _foe.GetComponent<EnemyBody>(); return b != null ? b.EyePosition : _foe.transform.position + Vector3.up * 1.6f; }

        static Vector3 FoeBelly()
        {
            foreach (var hb in _foe.GetComponentsInChildren<Hitbox>())
                if (hb.Part == BodyPart.LowerTorso) { var c = hb.GetComponent<Collider>(); if (c != null) return c.bounds.center; }
            return _foe.transform.position + Vector3.up;
        }

        /// <summary>Holds him: his AI on (staggered, it only keeps him in front of the player), aimed at the player.</summary>
        static void Hold()
        {
            if (_foeAI == null) return;
            _foeAI.target = _motor.transform;
            _foeAI.enabled = true;
        }

        /// <summary>The player's view and a side view of the same moment.</summary>
        static void Beat(string name)
        {
            _beat++;
            string pose = _pose;
            _pose = $"grapple_{_beat:00}_{name}";
            Shoot();
            ShootSide();
            _pose = pose;
        }

        static void ShootSide()
        {
            PlaceSide();
            string path = $"{_folder}/{_tag}_{_pose}_side.png";
            var rt = RenderTexture.GetTemporary(_width, _height, 24, RenderTextureFormat.ARGB32);
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = rt };
                RenderPipeline.SubmitRenderRequest(_side, request);
                RenderPipeline.SubmitRenderRequest(_side, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(_width, _height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, _width, _height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally { RenderTexture.ReleaseTemporary(rt); }
            _written.Add(path);
        }

        /// <summary>The side camera beside the player and the foe, looking across at both.</summary>
        static void PlaceSide()
        {
            var main = _weapon != null && _weapon.fpsCamera != null ? _weapon.fpsCamera : Camera.main;
            if (_side == null)
            {
                var go = new GameObject("Shots_SideCamera");
                _side = go.AddComponent<Camera>();
                _side.fieldOfView = 42f;
                _side.nearClipPlane = 0.05f;
                int fp = LayerMask.NameToLayer("FirstPerson");
                _side.cullingMask = main != null ? main.cullingMask : ~0;
                if (fp >= 0) _side.cullingMask &= ~(1 << fp);
            }
            Vector3 me = _motor.transform.position, him = _foe != null ? _foe.transform.position : me + _motor.transform.forward;
            Vector3 mid = (me + him) * 0.5f + Vector3.up * 1.2f;
            Vector3 across = Vector3.Cross(Vector3.up, him - me).normalized;
            _side.transform.position = mid + across * 2.6f + Vector3.up * 0.15f - (him - me).normalized * 0.4f;
            _side.transform.LookAt(mid);
        }

        /// <summary>
        /// The whole grab and punch as one pose, its beats photographed as they happen; then the hook to the belly and the
        /// bent hold. True when done.
        /// </summary>
        static bool Grapple()
        {
            foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                if (ai.gameObject != _foe) ai.gameObject.SetActive(false);
            switch (_phase)
            {
                case 0:
                    if (_frame >= Frames(1f))
                    {
                        if (!OntoOpenGround()) Report += "grapple: no open navmesh near the start; shot where the player stands\n";
                        _beat = 0; Foe(0.85f); Phase(1);
                    }
                    return false;
                case 1:
                    _motor.TurnTowards(FoeHead(), 1f);
                    if (InPhase == 1) _foeAt = _foe.transform.position;
                    if (InPhase >= Frames(0.5f))
                    {
                        var agent = _foe.GetComponent<NavMeshAgent>();
                        Report += $"grapple: before the press the foe moved {Vector3.Distance(_foeAt, _foe.transform.position):0.00} m (agent on {agent != null && agent.enabled}, on mesh {agent != null && agent.enabled && agent.isOnNavMesh}, " +
                                  $"root motion {(_foe.GetComponent<Animator>() != null && _foe.GetComponent<Animator>().applyRootMotion)}, spawn-to-player {Vector3.ProjectOnPlane(_foeAt - _motor.transform.position, Vector3.up).magnitude:0.00} m, " +
                                  $"eye now {(_weapon != null && _weapon.fpsCamera != null ? _weapon.fpsCamera.transform.position : Vector3.zero)}, head now {FoeHead()}, player root {_motor.transform.position})\n";
                        _melee.TryPunch(); Phase(2);
                    }
                    return false;
                case 2: if (At(MeleeStrike.Strike.Grab, MeleeStrike.ImpactAt(MeleeStrike.Strike.Grab) - 0.02f)) { Beat("grab"); Phase(3); } return InPhase > Frames(3f) && GaveUp();
                case 3: if (_melee.Holding) Hold(); if (InPhase >= Frames(0.35f)) { Beat("held"); _melee.TryPunch(); Phase(4); } return false;
                case 4: if (At(MeleeStrike.Strike.FacePunch, MeleeStrike.ImpactAt(MeleeStrike.Strike.FacePunch) - 0.02f)) { Beat("punch"); Phase(5); } return InPhase > Frames(3f) && GaveUp();
                case 5: if (InPhase == 3) Beat("punch_after"); if (InPhase > 3 && Time.time >= _melee.ReadyAt) { _melee.TryPunch(); Phase(6); } return false;
                case 6: if (At(MeleeStrike.Strike.FacePunch, MeleeStrike.ImpactAt(MeleeStrike.Strike.FacePunch) - 0.02f)) { Beat("punch2"); Phase(7); } return InPhase > Frames(3f) && GaveUp();
                // Punches until the press is the overhand (the straight, the hook, the uppercut), each photographed.
                case 7: if (Time.time >= _melee.ReadyAt) { _melee.TryPunch(); Phase(_melee.Thrown == MeleeStrike.Strike.Overhand ? 8 : 30); } return false;
                case 30: if (At(MeleeStrike.Strike.FacePunch, MeleeStrike.ImpactAt(MeleeStrike.Strike.FacePunch) - 0.02f)) { Beat($"punch{_melee.PunchKind + 1}"); Phase(7); } return InPhase > Frames(3f) && GaveUp();
                case 8: if (At(MeleeStrike.Strike.Overhand, 0.3f)) { Beat("overhand_up"); Phase(9); } return InPhase > Frames(3f) && GaveUp();
                case 9: if (At(MeleeStrike.Strike.Overhand, MeleeStrike.ImpactAt(MeleeStrike.Strike.Overhand) - 0.02f)) { Beat("overhand"); Phase(10); } return InPhase > Frames(3f) && GaveUp();
                case 10:
                    if (InPhase == Frames(0.6f)) Beat("down");
                    if (InPhase == Frames(1.6f)) Beat("down_later");
                    if (InPhase >= Frames(2.6f)) { Beat("down_last"); Phase(11); }
                    return false;
                // The hook to the belly: the fists up first, swung at nobody, then the hook, him doubled, taken bent.
                case 11:
                    if (_melee.Current != MeleeStrike.Strike.None || _melee.FistsOut || Time.time < _melee.ReadyAt + 1.5f) return false;
                    Foe(1.3f);
                    Turn(100f);
                    Phase(12);
                    return false;
                case 12: if (InPhase >= Frames(0.4f)) { _melee.TryPunch(); Phase(13); } return false;
                case 13: if (Time.time >= _melee.ReadyAt) { _melee.TryPunch(); Phase(14); } return false;
                case 14: if (Time.time >= _melee.ReadyAt && _melee.GunAway > 0.9f) { Phase(15); } return false;
                case 15: _motor.TurnTowards(FoeBelly(), 1f); if (InPhase >= 3) { _melee.TryPunch(); Phase(16); } return false;
                case 16: if (At(MeleeStrike.Strike.BodyBlow, MeleeStrike.ImpactAt(MeleeStrike.Strike.BodyBlow) - 0.02f)) { Beat("hook"); Phase(17); } return InPhase > Frames(3f) && GaveUp();
                case 17: if (InPhase >= Frames(0.35f)) { Beat("doubled"); Phase(18); } return false;
                case 18: _motor.TurnTowards(FoeHead(), 1f); if (Time.time >= _melee.ReadyAt && InPhase >= 2) { _melee.TryPunch(); Phase(19); } return false;
                case 19: if (_melee.Holding) { Hold(); if (InPhase >= Frames(0.6f)) { Beat("bent_held"); Phase(20); } } return InPhase > Frames(3f) && GaveUp();
                case 20: if (Time.time >= _melee.ReadyAt) { _melee.TryPunch(); Phase(_melee.Thrown == MeleeStrike.Strike.Overhand ? 21 : 20); } return false;
                case 21: if (At(MeleeStrike.Strike.Overhand, MeleeStrike.ImpactAt(MeleeStrike.Strike.Overhand) - 0.02f)) { Beat("bent_overhand"); Phase(22); } return InPhase > Frames(3f) && GaveUp();
                default:
                    if (InPhase < Frames(0.8f)) return false;
                    Beat("bent_down");
                    if (_foe != null) Object.Destroy(_foe);
                    if (_side != null) Object.Destroy(_side.gameObject);
                    _foe = null; _side = null;
                    return true;
            }
        }

        // ---- the grab and punch on film --------------------------------------------------------------------------

        static int _videoFrame;
        static string _videoDir;
        static EnemyAI.HeldMove _lastMove;
        static bool _wasHeldOnFilm;

        /// <summary>The frame from the eyes and from the side, as JPEGs in the video folder.</summary>
        static void Film()
        {
            if (_videoDir == null)
            {
                _videoDir = $"{_folder}/{_tag}_video";
                if (Directory.Exists(_videoDir)) Directory.Delete(_videoDir, true);
                Directory.CreateDirectory(_videoDir);
                _videoFrame = 0;
            }
            _videoFrame++;
            var main = _weapon != null && _weapon.fpsCamera != null ? _weapon.fpsCamera : Camera.main;
            RenderJpeg(main, $"{_videoDir}/fp_{_videoFrame:00000}.jpg");
            PlaceSide();
            RenderJpeg(_side, $"{_videoDir}/side_{_videoFrame:00000}.jpg");

            // What he is doing, as it changes.
            if (_foeAI != null)
            {
                bool held = _foeAI.Grappled;
                if (held && (_foeAI.Struggle != _lastMove || !_wasHeldOnFilm))
                    Report += $"video {_videoFrame}: held, {_foeAI.Struggle} (counters landed {_foeAI.CountersLanded})\n";
                if (held && (_foeAI.Struggle == EnemyAI.HeldMove.Wind || _foeAI.Struggle == EnemyAI.HeldMove.Swing || _lastMove == EnemyAI.HeldMove.Swing))
                {
                    var body = _foe.GetComponent<EnemyBody>();
                    var anim = _foe.GetComponentInChildren<Animator>();
                    var hand = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.LeftHand) : null;
                    var eye = _weapon.fpsCamera.transform.position;
                    Report += $"video {_videoFrame}: {_foeAI.Struggle} {_foeAI.StruggleTime:0.00}s, roots {Vector3.ProjectOnPlane(_foe.transform.position - _motor.transform.position, Vector3.up).magnitude:0.00} m, " +
                              $"his left hand {(hand != null ? Vector3.Distance(hand.position, eye) : -1f):0.00} m from the eye\n";
                }
                if (!held && _wasHeldOnFilm)
                    Report += $"video {_videoFrame}: let go (bolting {_foeAI.Bolting}, dead {_foeAI.CurrentState == EnemyAI.State.Dead})\n";
                if (held && _videoFrame % 10 == 0)
                {
                    var anim = _foe.GetComponentInChildren<Animator>();
                    if (anim != null && anim.isHuman)
                    {
                        Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips), head = anim.GetBoneTransform(HumanBodyBones.Head);
                        int action = anim.GetLayerIndex("Action");
                        var st = action >= 0 ? anim.GetCurrentAnimatorStateInfo(action) : default;
                        string state = action < 0 ? "-" : st.IsName("Grabbed") ? "Grabbed" : st.IsName("GrabbedHit") ? "Hit" : st.IsName("GrabbedHitB") ? "HitB" : st.IsName("GrabbedCounter") ? "Counter" : st.IsName("GrabbedWrench") ? "Wrench" : st.IsName("Idle") ? "Idle" : "other";
                        Transform pelvis = null, neck = null;
                        foreach (var t in _foe.GetComponentsInChildren<Transform>()) { if (t.name == "pelvis") pelvis = t; if (t.name == "neck_01") neck = t; }
                        Vector3 L(Transform t) => t != null ? _foe.transform.InverseTransformPoint(t.position) : Vector3.zero;
                        Report += $"video {_videoFrame}: Hips bone '{hips.name}', pelvis {L(pelvis)}, neck {L(neck)}, head {L(head)} (his frame, z towards the player), " +
                                  $"trunk {(pelvis != null && neck != null ? Vector3.Angle(neck.position - pelvis.position, Vector3.up) : -1f):0} deg, Action {state} {(action >= 0 ? anim.GetLayerWeight(action) : -1f):0.00}\n";
                    }
                }
                _lastMove = _foeAI.Struggle;
                _wasHeldOnFilm = held;
            }
        }

        /// <summary>
        /// The player onto the nearest navmesh near the start that is open ground two metres round, facing as at the start:
        /// the hold keeps the held man on the navmesh, and stood off it at the desert's start (the shop front) he was held
        /// at a metre, not at the hold's distance.
        /// </summary>
        static bool OntoOpenGround()
        {
            if (!NavMesh.SamplePosition(_startPos, out var home, 3f, NavMesh.AllAreas)) return false;
            float lift = _startPos.y - home.position.y;
            for (float r = 0f; r <= 25f; r += 1f)
                for (int a = 0; a < (r == 0f ? 1 : 16); a++)
                {
                    float angle = a * 22.5f * Mathf.Deg2Rad;
                    Vector3 at = home.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * r;
                    if (!NavMesh.SamplePosition(at, out var hit, 0.1f, NavMesh.AllAreas)) continue;
                    bool open = true;
                    for (int k = 0; k < 12 && open; k++)
                    {
                        Vector3 d = Quaternion.AngleAxis(k * 30f, Vector3.up) * Vector3.forward * 2f;
                        open = NavMesh.SamplePosition(hit.position + d, out var round, 0.1f, NavMesh.AllAreas)
                               && Mathf.Abs(round.position.y - hit.position.y) < 0.2f
                               && !NavMesh.Raycast(hit.position, round.position, out _, NavMesh.AllAreas);
                    }
                    if (!open) continue;
                    var cc = _motor.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    _motor.transform.SetPositionAndRotation(hit.position + Vector3.up * lift, _startRot);
                    if (cc != null) cc.enabled = true;
                    Physics.SyncTransforms();
                    Report += $"{_pose}: on open ground {r:0} m from the start\n";
                    return true;
                }
            return false;
        }

        static Transform _decoy;

        static readonly string[] PatrolTypes = (System.Environment.GetEnvironmentVariable("FPSKIT_PATROL_TYPES") ?? "Grunt,Runner,Screamer,Marksman,Outrider").Split(',');
        static int _patrolIndex;

        /// <summary>Each of the desert's standard soldiers walking his beat, calm, filmed in turn. True when done.</summary>
        static bool PatrolVideo()
        {
            foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                if (ai.gameObject != _foe) ai.gameObject.SetActive(false);
            switch (_phase)
            {
                case 0:
                    if (_frame < Frames(1f)) return false;
                    if (!OntoOpenGround()) Report += "patrol: no open navmesh near the start\n";
                    _patrolIndex = 0;
                    Phase(1);
                    return false;
                case 1:
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FPSKit_Generated/Enemy.prefab");
                    Vector3 ahead = Vector3.ProjectOnPlane(_startRot * Vector3.forward, Vector3.up).normalized;
                    Vector3 at = _motor.transform.position + ahead * 14f;
                    if (NavMesh.SamplePosition(at, out var hit, 4f, NavMesh.AllAreas)) at = hit.position;
                    if (_foe != null) Object.Destroy(_foe);
                    _foe = Object.Instantiate(prefab, at, Quaternion.LookRotation(Vector3.Cross(Vector3.up, ahead)));
                    _foe.name = "Shots_Patrol";
                    FPSKitEnemyRoster.GetOrCreate(PatrolTypes[_patrolIndex]).ApplyTo(_foe);
                    _foeAI = _foe.GetComponent<EnemyAI>();
                    // Somebody to hunt who is never there: far below the map, so his mind stays calm.
                    if (_decoy == null) _decoy = new GameObject("Shots_Decoy").transform;
                    _decoy.position = at + Vector3.down * 500f;
                    if (_foeAI != null) _foeAI.target = _decoy;
                    var brain = _foe.GetComponent<EnemyBrain>();
                    if (brain != null) brain.patrolRadius = 9f;
                    _videoDir = null;
                    Phase(2);
                    return false;
                }
                case 2:
                    // The squad's radio stays quiet (every 6-10 s it sends the calm to look near the player).
                    typeof(EnemySquad).GetField("_nextRadio", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, Time.time + 999f);
                    if (InPhase < Frames(1.5f)) return false;   // settled into his beat
                    FilmPatrol();
                    if (InPhase >= Frames(7f))
                    {
                        var brain = _foe.GetComponent<EnemyBrain>();
                        Report += $"patrol {PatrolTypes[_patrolIndex]}: {_videoFrame} frames, his mind {(brain != null ? brain.Mind.ToString() + " (" + brain.Doing + ")" : "none")}\n";
                        Object.Destroy(_foe); _foe = null;
                        if (++_patrolIndex < PatrolTypes.Length) { Phase(1); return false; }
                        if (_side != null) Object.Destroy(_side.gameObject);
                        _side = null;
                        return true;
                    }
                    return false;
            }
            return false;
        }

        static void FilmPatrol()
        {
            if (_videoDir == null)
            {
                _videoDir = $"{_folder}/{_tag}_patrol_{PatrolTypes[_patrolIndex]}";
                if (Directory.Exists(_videoDir)) Directory.Delete(_videoDir, true);
                Directory.CreateDirectory(_videoDir);
                _videoFrame = 0;
            }
            _videoFrame++;
            var main = _weapon != null && _weapon.fpsCamera != null ? _weapon.fpsCamera : Camera.main;
            _motor.TurnTowards(_foe.transform.position + Vector3.up * 1.3f, 1f);
            RenderJpeg(main, $"{_videoDir}/fp_{_videoFrame:00000}.jpg");
            PlaceSide();
            // Close beside him, three quarters from the front, following.
            Vector3 him = _foe.transform.position;
            Vector3 face = _foe.transform.forward;
            Vector3 across = _foe.transform.right;
            _side.transform.position = him + face * 1.4f + across * 3.0f + Vector3.up * 1.5f;
            _side.transform.LookAt(him + Vector3.up * 1.1f);
            RenderJpeg(_side, $"{_videoDir}/side_{_videoFrame:00000}.jpg");
        }

        static int _tourShot;
        static List<(Vector3 at, Vector3 look, string name)> _tourRooms;

        /// <summary>The lighting tour, one place after another. True when done.</summary>
        static bool LightingTour()
        {
            foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                if (ai.gameObject != _foe) ai.gameObject.SetActive(false);
            void Place(Vector3 feet, Vector3 look)
            {
                var cc = _motor.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                _motor.transform.position = feet + Vector3.up * 0.05f;
                if (cc != null) cc.enabled = true;
                Physics.SyncTransforms();
                _motor.TurnTowards(look, 1f);
            }
            string Name(string place) => $"light_{++_tourShot:00}_{place}";
            switch (_phase)
            {
                case 0:
                    if (_frame < Frames(1f)) return false;
                    _tourShot = 0;
                    _tourRooms = TourRooms();
                    Report += $"tour: {_tourRooms.Count} rooms picked\n";
                    Phase(1);
                    return false;
                case 1: if (InPhase >= Frames(0.5f)) { Snap1(Name("street")); Phase(2); } return false;
                case 2: Turn(90f); if (InPhase >= Frames(0.6f)) { Snap1(Name("shopfront")); Phase(3); } return false;
                case 3:
                {
                    // A man's face in the sun, 2.4 m off, facing the player.
                    Turn(0f);
                    if (InPhase == 1) FaceFoe(2.4f);
                    if (InPhase >= Frames(1.2f)) { _motor.TurnTowards(FoeHead() + Vector3.down * 0.15f, 1f); Snap1(Name("face_sun")); Phase(4); }
                    return false;
                }
                case 4:
                    if (InPhase == 1) { if (_foe != null) Object.Destroy(_foe); _foe = null; if (!IntoShade()) Report += "tour: no shade\n"; }
                    if (InPhase == 3) FaceFoe(2.4f);
                    if (InPhase >= Frames(1.4f)) { _motor.TurnTowards(FoeHead() + Vector3.down * 0.15f, 1f); Snap1(Name("face_shade")); if (_foe != null) Object.Destroy(_foe); _foe = null; Phase(5); }
                    return false;
                default:
                {
                    int i = _phase - 5;
                    if (i >= _tourRooms.Count) return true;
                    var (at, look, place) = _tourRooms[i];
                    if (InPhase == 1) Place(at, look);
                    if (InPhase >= Frames(0.8f)) { Snap1(Name(place)); Phase(_phase + 1); }
                    return false;
                }
            }
        }

        /// <summary>Only the player's view (the tour does not need a side camera).</summary>
        static void Snap1(string name)
        {
            string pose = _pose;
            _pose = name;
            Shoot();
            _pose = pose;
        }

        /// <summary>A still Grunt <paramref name="metres"/> in front of the eye, facing the player, mind removed.</summary>
        static void FaceFoe(float metres)
        {
            if (_foe != null) Object.Destroy(_foe);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FPSKit_Generated/Enemy.prefab");
            Vector3 ahead = Vector3.ProjectOnPlane(_motor.transform.forward, Vector3.up).normalized;
            Vector3 at = _motor.transform.position + ahead * metres;
            if (NavMesh.SamplePosition(at, out var hit, 1.5f, NavMesh.AllAreas)) at = hit.position;
            _foe = Object.Instantiate(prefab, at, Quaternion.LookRotation(-ahead));
            _foe.name = "Shots_Face";
            FPSKitEnemyRoster.GetOrCreate("Grunt").ApplyTo(_foe);
            _foeAI = _foe.GetComponent<EnemyAI>();
            if (_foeAI != null) { _foeAI.target = _motor.transform; _foeAI.enabled = false; }
            var brain = _foe.GetComponent<EnemyBrain>();
            if (brain != null) Object.DestroyImmediate(brain);
        }

        /// <summary>
        /// Rooms of the three houses nearest the start (FPSKitTownInterior's marks), ground floor and upstairs: stood at
        /// the room's way in, looking across it, plus one shop seen from its doorway.
        /// </summary>
        static List<(Vector3, Vector3, string)> TourRooms()
        {
            var list = new List<(Vector3, Vector3, string)>();
            var rooms = GameObject.Find("HouseRooms");
            if (rooms == null) return list;
            var houses = new List<Transform>();
            foreach (Transform h in rooms.transform) houses.Add(h);
            houses.Sort((a, b) => Dist(a).CompareTo(Dist(b)));
            float Dist(Transform h) { var d = h.Find("Door"); return d == null ? 1e9f : (d.position - _startPos).sqrMagnitude; }
            int taken = 0;
            foreach (var h in houses)
            {
                if (taken >= 3) break;
                bool any = false;
                foreach (Transform m in h)
                {
                    if (!m.name.StartsWith("Room ")) continue;
                    if (!NavMesh.SamplePosition(m.position, out var hit, 0.6f, NavMesh.AllAreas)) continue;
                    // Looking across the room: away from the nearest wall.
                    Vector3 best = Vector3.forward; float far = 0f;
                    for (int a = 0; a < 8; a++)
                    {
                        Vector3 d = Quaternion.AngleAxis(a * 45f, Vector3.up) * Vector3.forward;
                        float reach = Physics.Raycast(hit.position + Vector3.up * 1.5f, d, out var rh, 12f, ~0, QueryTriggerInteraction.Ignore) ? rh.distance : 12f;
                        if (reach > far) { far = reach; best = d; }
                    }
                    list.Add((hit.position, hit.position + Vector3.up * 1.45f + best * 4f, $"{h.name.ToLower()}{taken + 1}_{m.name.Substring(5).Replace(' ', '_').ToLower()}"));
                    any = true;
                    if (list.Count >= 8) return list;
                }
                if (any) taken++;
            }
            return list;
        }

        static int _gripIndex;
        static MeleeSettings.Grip _gripWas;

        /// <summary>Each grip in turn: a fresh foe, grabbed, photographed at 0.5 s and 1.1 s into the hold. True when done.</summary>
        static bool GrappleGrips()
        {
            foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                if (ai.gameObject != _foe) ai.gameObject.SetActive(false);
            var styles = (MeleeSettings.Grip[])System.Enum.GetValues(typeof(MeleeSettings.Grip));
            var settings = MeleeSettings.Active;
            if (_foe != null && _melee.Holding) _motor.TurnTowards(FoeHead() + Vector3.down * 0.05f, 0.12f);
            switch (_phase)
            {
                case 0:
                    if (_frame < Frames(1f)) return false;
                    if (!OntoOpenGround()) Report += "grips: no open navmesh near the start\n";
                    _gripWas = settings.grip;
                    _gripIndex = 0;
                    Phase(1);
                    return false;
                case 1:
                    if (_melee.Holding || Time.time < _melee.ReadyAt + 1.2f) return false;
                    settings.grip = styles[_gripIndex];
                    Foe(0.85f);
                    Phase(2);
                    return false;
                case 2: _motor.TurnTowards(FoeHead(), 1f); if (InPhase >= Frames(0.4f)) { _melee.TryPunch(); Phase(3); } return false;
                case 3: if (_melee.Holding) { Hold(); Phase(4); } else if (InPhase > Frames(3f)) return GaveUp(); return false;
                case 4:
                    if (InPhase == Frames(0.5f)) Snap($"grip_{styles[_gripIndex]}_a");
                    if (InPhase >= Frames(1.1f))
                    {
                        Snap($"grip_{styles[_gripIndex]}_b");
                        if (_foe != null) Object.Destroy(_foe);
                        _foe = null;
                        _gripIndex++;
                        if (_gripIndex >= styles.Length)
                        {
                            settings.grip = _gripWas;
                            if (_side != null) Object.Destroy(_side.gameObject);
                            _side = null;
                            return true;
                        }
                        Phase(1);
                    }
                    return false;
            }
            return false;
        }

        static void Snap(string name)
        {
            string pose = _pose;
            _pose = name;
            Shoot();
            ShootSide();
            _pose = pose;
        }

        static void RenderJpeg(Camera cam, string path)
        {
            var rt = RenderTexture.GetTemporary(_width, _height, 24, RenderTextureFormat.ARGB32);
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = rt };
                RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(_width, _height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, _width, _height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(path, tex.EncodeToJPG(88));
                Object.DestroyImmediate(tex);
            }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }

        /// <summary>
        /// One fight, filmed every frame. True when done.
        /// </summary>
        static bool GrappleVideo()
        {
            foreach (var ai in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
                if (ai.gameObject != _foe) ai.gameObject.SetActive(false);
            bool filming = _phase >= 1 && _phase < 99;
            // The view kept on his head the way a player keeps it, a little behind.
            if (_foe != null && _melee.Holding) _motor.TurnTowards(FoeHead() + Vector3.down * 0.05f, 0.12f);
            switch (_phase)
            {
                case 0:
                    if (_frame >= Frames(1f))
                    {
                        if (!OntoOpenGround()) Report += "video: no open navmesh near the start; filmed where the player stands\n";
                        Foe(0.85f); _videoDir = null; Phase(1);
                    }
                    return false;
                case 1:
                    _motor.TurnTowards(FoeHead(), 1f);
                    if (InPhase == 2)
                    {
                        var anim = _foe.GetComponentInChildren<Animator>();
                        var headBone = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
                        Report += $"video: his head bone {(headBone != null ? headBone.position.y - _foe.transform.position.y : -1f):0.00} m over his feet, eye point {FoeHead().y - _foe.transform.position.y:0.00}, " +
                                  $"scale {_foe.transform.lossyScale.y:0.00}, the player's eye {_weapon.fpsCamera.transform.position.y - _motor.transform.position.y:0.00}\n";
                    }
                    if (InPhase >= Frames(0.4f)) { _melee.TryPunch(); Phase(2); }
                    break;
                case 2: if (_melee.Holding) { Hold(); Phase(3); } else if (InPhase > Frames(3f)) return GaveUp(); break;
                // Left a moment: he pulls back against the hand and punches.
                case 3: if (InPhase >= Frames(1.5f) && Time.time >= _melee.ReadyAt) { _melee.TryPunch(); Phase(4); } break;
                case 4: if (Time.time >= _melee.ReadyAt + 0.25f) { _melee.TryPunch(); Phase(5); } break;
                // The third into his wind-up, which it stops.
                case 5:
                    if (Time.time >= _melee.ReadyAt && ((_foeAI.Struggle == EnemyAI.HeldMove.Wind && _foeAI.StruggleTime > 0.1f) || InPhase > Frames(2.5f)))
                    { _melee.TryPunch(); Phase(6); }
                    break;
                case 6: if (Time.time >= _melee.ReadyAt + 0.3f) { _melee.TryPunch(); Phase(7); } break;
                case 7: if (InPhase >= Frames(2.0f)) { Foe(0.85f); Phase(8); } break;
                // A second one, grabbed and left alone: he breaks free and runs.
                case 8:
                    _motor.TurnTowards(FoeHead(), 1f);
                    if (InPhase >= Frames(0.4f) && Time.time >= _melee.ReadyAt) { _melee.TryPunch(); Phase(9); }
                    break;
                case 9: if (_melee.Holding) { Hold(); Phase(10); } else if (InPhase > Frames(3f)) return GaveUp(); break;
                case 10: if (!_melee.Holding) Phase(11); else if (InPhase > Frames(6f)) Phase(11); break;
                case 11:
                    if (_foe != null) _motor.TurnTowards(FoeHead(), 0.08f);
                    if (InPhase >= Frames(2.2f))
                    {
                        float away = _foe != null ? Vector3.ProjectOnPlane(_foe.transform.position - _motor.transform.position, Vector3.up).magnitude : -1f;
                        Report += $"video: the second ran to {away:0.0} m; {_videoFrame} frames in {_videoDir}\n";
                        Film();
                        if (_foe != null) Object.Destroy(_foe);
                        if (_side != null) Object.Destroy(_side.gameObject);
                        _foe = null; _side = null;
                        return true;
                    }
                    break;
            }
            if (filming) Film();
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
