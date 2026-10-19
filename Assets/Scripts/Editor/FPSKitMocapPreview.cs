#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The capture pipeline's pictures: the side-by-side preview of a baked take, the synthetic test videos (our own
    /// soldier acting a move, filmed the way a phone would film Ishaan, so the whole pipeline can be run and checked
    /// against a known answer), and that check.
    /// </summary>
    public static partial class FPSKitMocap
    {
        const string SoldierPrefab = "Assets/FPSKit_Generated/Enemy.prefab";
        public const int PanelW = 960, PanelH = 540;

        // ==================================================================================================================
        // A stage to film on: a plain room far from anything, lit evenly, built in a scratch scene and thrown away.
        // ==================================================================================================================

        class Stage : IDisposable
        {
            readonly UnityEngine.SceneManagement.Scene _previous, _scene;
            readonly bool _alone;   // a batch run's untitled scene cannot have one added beside it: replaced instead
            readonly AmbientMode _ambientMode;
            readonly Color _ambient;
            readonly bool _fog;
            readonly List<PlayableGraph> _graphs = new List<PlayableGraph>();
            readonly List<(SkinnedMeshRenderer smr, GameObject copy)> _baked = new List<(SkinnedMeshRenderer, GameObject)>();
            RenderTexture _rt;
            Texture2D _frame;

            public Stage(int width, int height)
            {
                _previous = EditorSceneManager.GetActiveScene();
                _alone = string.IsNullOrEmpty(_previous.path);
                _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, _alone ? NewSceneMode.Single : NewSceneMode.Additive);
                EditorSceneManager.SetActiveScene(_scene);
                _ambientMode = RenderSettings.ambientMode;
                _ambient = RenderSettings.ambientLight;
                _fog = RenderSettings.fog;
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.62f, 0.62f, 0.64f);
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.3f;
                sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(50f, 150f, 0f);
                _rt = new RenderTexture(PanelW, PanelH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                _rt.Create();
                _frame = new Texture2D(width, height, TextureFormat.RGB24, false);
                DynamicGI.UpdateEnvironment();
            }

            /// <summary>A floor and a back wall round <paramref name="at"/>, in the colours of a plain room.</summary>
            public void Room(Vector3 at, Vector3 wallNormal)
            {
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.transform.position = at;
                floor.transform.localScale = new Vector3(3f, 1f, 3f);
                floor.GetComponent<Renderer>().sharedMaterial = Lit(new Color(0.46f, 0.45f, 0.42f));
                var wall = GameObject.CreatePrimitive(PrimitiveType.Quad);
                wall.transform.position = at - wallNormal * 6f + Vector3.up * 5f;
                wall.transform.rotation = Quaternion.LookRotation(-wallNormal, Vector3.up);
                wall.transform.localScale = new Vector3(30f, 12f, 1f);
                wall.GetComponent<Renderer>().sharedMaterial = Lit(new Color(0.72f, 0.73f, 0.74f));
            }

            public static Material Lit(Color c)
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", 0.1f);
                return m;
            }

            /// <summary>Our soldier, as the game has him, with nothing of his running: only his Animator, played by hand.</summary>
            public (GameObject go, Animator anim) Soldier(Vector3 at, float height = 0f)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SoldierPrefab);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _scene);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.transform.SetPositionAndRotation(at, Quaternion.identity);
                foreach (var b in go.GetComponentsInChildren<Behaviour>(true)) if (!(b is Animator)) b.enabled = false;
                // His guns are laid in his hands by EnemyBody in play; standing still here they would float. Hidden.
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Rifle" || t.name == "Pistol" || t.name == "PistolHolster") t.gameObject.SetActive(false);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    if (r.name.StartsWith("Kit_") || r.gameObject.name.Contains("HealthBar")) r.enabled = false;
                var anim = go.GetComponentInChildren<Animator>(true);
                anim.runtimeAnimatorController = null;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (height > 0f)
                {
                    anim.Rebind();
                    var head = anim.GetBoneTransform(HumanBodyBones.Head);
                    float now = head.position.y - at.y + 0.11f * go.transform.localScale.y;
                    go.transform.localScale *= height / now;
                }
                return (go, anim);
            }

            /// <summary>A clip on an Animator, played by hand (time set, then evaluated).</summary>
            public AnimationClipPlayable Play(Animator anim, AnimationClip clip, bool rootMotion)
            {
                var graph = PlayableGraph.Create("MocapPreview");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "pose", anim);
                var play = AnimationClipPlayable.Create(graph, clip);
                play.SetApplyFootIK(false);
                output.SetSourcePlayable(play);
                anim.applyRootMotion = rootMotion;
                _graphs.Add(graph);
                return play;
            }

            public static void Show(AnimationClipPlayable play, float time)
            {
                play.SetTime(time);
                play.GetGraph().Evaluate();
            }

            public static void Step(AnimationClipPlayable play, float dt) => play.GetGraph().Evaluate(dt);

            public Camera Camera(Vector3 position, Vector3 forward, Vector3 up, float fov)
            {
                var cam = new GameObject("Cam").AddComponent<Camera>();
                cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, up));
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.02f;
                cam.farClipPlane = 200f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.55f, 0.6f, 0.66f);
                cam.enabled = false;
                return cam;
            }

            /// <summary>Every skinned mesh as it stands now, as a plain mesh: renders made in one editor frame all see this pose.</summary>
            public void Bake()
            {
                foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
                {
                    if (!smr.enabled || !smr.gameObject.activeInHierarchy || smr.gameObject.scene != _scene) continue;
                    var mesh = new Mesh();
                    smr.BakeMesh(mesh, false);
                    var copy = new GameObject(smr.name + "_Baked");
                    copy.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                    copy.transform.localScale = Vector3.one;
                    copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = copy.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = smr.sharedMaterials;
                    SceneManager_Move(copy);
                    smr.enabled = false;
                    _baked.Add((smr, copy));
                }
            }

            void SceneManager_Move(GameObject go) => UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, _scene);

            public void Unbake()
            {
                foreach (var (smr, copy) in _baked)
                {
                    if (smr != null) smr.enabled = true;
                    if (copy != null) { Object.DestroyImmediate(copy.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(copy); }
                }
                _baked.Clear();
            }

            /// <summary>One camera's picture into the frame, its bottom-left corner at (x, y).</summary>
            public void Render(Camera cam, int x, int y)
            {
                var req = new UniversalRenderPipeline.SingleCameraRequest { destination = _rt };
                RenderPipeline.SubmitRenderRequest(cam, req);
                var prev = RenderTexture.active;
                RenderTexture.active = _rt;
                _frame.ReadPixels(new Rect(0, 0, PanelW, PanelH), x, y);
                RenderTexture.active = prev;
            }

            public void Clear(Color c)
            {
                var px = _frame.GetPixels32();
                var c32 = (Color32)c;
                for (int i = 0; i < px.Length; i++) px[i] = c32;
                _frame.SetPixels32(px);
            }

            public byte[] Pixels() { _frame.Apply(false); return _frame.GetRawTextureData(); }

            public void Dispose()
            {
                Unbake();
                foreach (var g in _graphs) if (g.IsValid()) g.Destroy();
                RenderSettings.ambientMode = _ambientMode;
                RenderSettings.ambientLight = _ambient;
                RenderSettings.fog = _fog;
                if (_rt != null) { _rt.Release(); Object.DestroyImmediate(_rt); }
                if (_frame != null) Object.DestroyImmediate(_frame);
                if (_alone) return;
                EditorSceneManager.SetActiveScene(_previous);
                EditorSceneManager.CloseScene(_scene, true);
            }
        }

        /// <summary>A Humanoid clip read straight from its curves (root and muscles) and put on a body by its pose handler.</summary>
        class ClipPoser
        {
            readonly AnimationCurve[] _root = new AnimationCurve[7];
            readonly AnimationCurve[] _muscles = new AnimationCurve[HumanTrait.MuscleCount];

            public ClipPoser(AnimationClip clip)
            {
                string[] root = { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
                for (int i = 0; i < 7; i++) _root[i] = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), root[i]));
                for (int m = 0; m < _muscles.Length; m++)
                    _muscles[m] = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), Attribute(HumanTrait.MuscleName[m])));
            }

            public void Apply(HumanPoseHandler handler, float t)
            {
                float V(AnimationCurve c, float fallback) => c != null ? c.Evaluate(t) : fallback;
                var pose = new HumanPose
                {
                    bodyPosition = new Vector3(V(_root[0], 0f), V(_root[1], 1f), V(_root[2], 0f)),
                    bodyRotation = new Quaternion(V(_root[3], 0f), V(_root[4], 0f), V(_root[5], 0f), V(_root[6], 1f)).normalized,
                    muscles = _muscles.Select(c => V(c, 0f)).ToArray()
                };
                handler.SetHumanPose(ref pose);
            }
        }

        /// <summary>Raw frames into ffmpeg (Tools/mocap's own copy), H.264 at 60 a second, bottom row first.</summary>
        class Encoder : IDisposable
        {
            readonly Process _p;
            readonly Stream _in;
            public Encoder(string ffmpeg, string output, int w, int h)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                _p = Process.Start(new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = $"-y -loglevel error -f rawvideo -pix_fmt rgb24 -s {w}x{h} -r 60 -i - -vf vflip -c:v libx264 -preset medium -crf 16 -pix_fmt yuv420p -threads 4 \"{output}\"",
                    UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true
                });
                _in = _p.StandardInput.BaseStream;
            }
            public void Write(byte[] frame) => _in.Write(frame, 0, frame.Length);
            public void Dispose() { _in.Flush(); _in.Close(); _p.WaitForExit(); _p.Dispose(); }
        }

        static string Ffmpeg => Arg("-mocapFfmpeg") ?? Directory.GetFiles("Tools/mocap/.venv", "ffmpeg-linux*", SearchOption.AllDirectories).FirstOrDefault();

        // ==================================================================================================================
        // The preview: four panels, 1920 x 1080 at 60 a second. Top left is left empty here and filled with Ishaan's own
        // video by Tools/mocap (compose.py), with the labels.
        //   top right:    the captured clip on our soldier, filmed from where the phone stood
        //   bottom left:  our current clip for the same move, the same way
        //   bottom right: a player move's first-person view (the arms as the game draws them, playing the capture);
        //                 an enemy move's captured clip from the front
        // ==================================================================================================================

        public static void PreviewBatch()
        {
            string take = Arg("-mocapTake");
            int code = 1;
            try { Preview(take, Arg("-mocapOut") ?? Path.Combine(WorkDir, take, "panels.mp4")); code = 0; }
            catch (Exception e) { Debug.LogError("[FPSKit] Mocap preview failed: " + e); }
            FPSKitBatch.Exit(code);
        }

        /// <summary>Batch entry for a whole take, in one Unity run: bake, then the preview's panels (needs UNITY_GRAPHICS=1).</summary>
        public static void ProcessBatch()
        {
            string take = Arg("-mocapTake");
            int code = 1;
            try
            {
                Bake(take);
                if (!Environment.GetCommandLineArgs().Contains("-mocapNoPreview")) Preview(take, Path.Combine(WorkDir, take, "panels.mp4"));
                code = 0;
            }
            catch (Exception e) { Debug.LogError("[FPSKit] Mocap process failed: " + e); }
            FPSKitBatch.Exit(code);
        }

        public static void Preview(string take, string output)
        {
            string folder = FolderFor(take);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{folder}/{take}.anim");
            var settings = AssetDatabase.LoadAssetAtPath<CapturedClipSettings>($"{folder}/{take}_settings.asset");
            var fp = AssetDatabase.LoadAssetAtPath<CapturedFirstPerson>($"{folder}/{take}_fp.asset");
            if (clip == null || settings == null) throw new Exception($"{take} is not baked yet");
            var m = JsonUtility.FromJson<Motion>(File.ReadAllText(Path.Combine(WorkDir, take, "motion.json")));

            // The phone's place, as the solver found it (relative to where the performer started).
            Vector3 camPos = m.cameraPosition != null && m.cameraPosition.Length == 3 ? new Vector3(m.cameraPosition[0], m.cameraPosition[1], m.cameraPosition[2]) : new Vector3(3.2f, 1.1f, 1f);
            Vector3 camFwd = m.cameraForward != null && m.cameraForward.Length == 3 ? new Vector3(m.cameraForward[0], m.cameraForward[1], m.cameraForward[2]) : (new Vector3(0, 1, 0.2f) - camPos).normalized;
            Vector3 camUp = m.cameraUp != null && m.cameraUp.Length == 3 ? new Vector3(m.cameraUp[0], m.cameraUp[1], m.cameraUp[2]) : Vector3.up;
            float fov = m.cameraFov > 1f ? m.cameraFov : 44f;
            // Framed a little wider than the phone (the soldier is a size up on Ishaan): the same angle, stepped back 15%.
            Vector3 lookAt = camPos + camFwd * Vector3.Dot(new Vector3(0, 1f, 0) - camPos, camFwd);
            Vector3 phone = lookAt + (camPos - lookAt) * 1.15f;

            float length = clip.length;
            var current = settings.currentClip;
            int frames = Mathf.RoundToInt(length * Rate) + 1;

            using var stage = new Stage(PanelW * 2, PanelH * 2);
            // Opening the stage's scene unloads assets nothing in a scene holds: everything is loaded again after it.
            clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{folder}/{take}.anim");
            settings = AssetDatabase.LoadAssetAtPath<CapturedClipSettings>($"{folder}/{take}_settings.asset");
            fp = AssetDatabase.LoadAssetAtPath<CapturedFirstPerson>($"{folder}/{take}_fp.asset");
            current = settings.currentClip;
            Vector3 aPlace = new Vector3(0f, 2000f, 0f), bPlace = new Vector3(60f, 2000f, 0f), dPlace = new Vector3(120f, 2000f, 0f);
            foreach (var p in new[] { aPlace, bPlace, dPlace }) stage.Room(p, -new Vector3(camFwd.x, 0, camFwd.z).normalized);

            var (aGo, aAnim) = stage.Soldier(aPlace);
            var poser = new ClipPoser(clip);
            var aPose = new HumanPoseHandler(aAnim.avatar, aAnim.transform);
            var camA = stage.Camera(aPlace + phone, camFwd, camUp, fov);

            Animator bAnim = null; AnimationClipPlayable bPlay = default; Camera camB = null;
            if (current != null)
            {
                (_, bAnim) = stage.Soldier(bPlace);
                bPlay = stage.Play(bAnim, current, true);
                camB = stage.Camera(bPlace + phone, camFwd, camUp, fov);
            }

            // Bottom right.
            Camera camD;
            ViewModelRig rig = null;
            HumanPoseHandler dPose = null;
            bool firstPerson = settings.firstPerson && fp != null;
            Debug.Log($"[FPSKitBatch] Mocap preview {take}: first person {settings.firstPerson}, track {(fp != null ? fp.Frames + " frames" : "missing")}");
            if (firstPerson)
            {
                // The player's eye with the arms the game draws, and a soldier standing a punch away to hit.
                var body = new GameObject("Player").transform;
                body.position = dPlace;
                var eye = new GameObject("Eye");
                eye.transform.SetParent(body, false);
                eye.transform.localPosition = new Vector3(0f, 1.65f, 0f);
                camD = eye.AddComponent<Camera>();
                camD.fieldOfView = 62f;
                camD.nearClipPlane = 0.02f;
                camD.clearFlags = CameraClearFlags.SolidColor;
                camD.backgroundColor = new Color(0.55f, 0.6f, 0.66f);
                camD.enabled = false;
                var holder = new GameObject("WeaponHolder").transform;
                holder.SetParent(eye.transform, false);
                var rigGo = new GameObject("Rig");
                rigGo.transform.SetParent(holder, false);
                rig = rigGo.AddComponent<ViewModelRig>();
                rig.PreviewBody = body;
                rig.Build(AssetDatabase.LoadAssetAtPath<GameObject>(RiflePrefab), holder, 0);
                rig.Captured = fp;
                var (tGo, tAnim) = stage.Soldier(dPlace + new Vector3(0f, 0f, 1.05f));
                tGo.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                var idle = LoadClip("Assets/Animations/Mixamo/Idle/Idle_Breathing.fbx");
                if (idle != null) Stage.Show(stage.Play(tAnim, idle, false), 0.5f);
            }
            else
            {
                (_, var dAnim) = stage.Soldier(dPlace);
                dPose = new HumanPoseHandler(dAnim.avatar, dAnim.transform);
                camD = stage.Camera(dPlace + new Vector3(1.6f, 1.25f, 3.2f), (new Vector3(0f, 0.95f, 0f) - new Vector3(1.6f, 1.25f, 3.2f)).normalized, Vector3.up, 40f);
            }

            var state = new ViewModelRig.State { fistsUp = 1f, gunAway = 1f, grounded = true, sinceShot = -1f, strikeProgress = -1f };
            if (camB != null) Stage.Show(bPlay, 0f);
            using (var enc = new Encoder(Ffmpeg, output, PanelW * 2, PanelH * 2))
            {
                for (int i = 0; i < frames; i++)
                {
                    // The captured clip straight from its curves, where it really is (not as root motion would move it).
                    poser.Apply(aPose, i / Rate);
                    if (dPose != null) poser.Apply(dPose, i / Rate);
                    if (i > 0 && camB != null && i / Rate <= current.length) Stage.Step(bPlay, 1f / Rate);
                    if (rig != null) { rig.CapturedTime = i / Rate - fp.fromClip; rig.Pose(state); }
                    // Each camera follows its soldier's travel sideways, never turning, so a step stays in the picture.
                    stage.Bake();
                    stage.Clear(new Color(0.08f, 0.08f, 0.09f));
                    stage.Render(camA, PanelW, PanelH);
                    if (camB != null) stage.Render(camB, 0, 0);
                    stage.Render(camD, PanelW, 0);
                    stage.Unbake();
                    enc.Write(stage.Pixels());
                }
            }
            if (rig != null) rig.Clear();
            aPose.Dispose();
            dPose?.Dispose();
            Debug.Log($"[FPSKitBatch] Mocap preview {take}: {frames} frames -> {output}");
        }

        // ==================================================================================================================
        // Synthetic takes: our soldier acting a move, filmed as a phone would film Ishaan (side-on, 1.1 m up, 26 mm lens,
        // 1280 x 720 at 60), so the pipeline can be run end to end before any real video exists, and its result checked
        // against the clip it was made from.
        //   UNITY_GRAPHICS=1 Tools/unity-batch.sh FPSKit.EditorTools.FPSKitMocap.SyntheticBatch -mocapOut <dir>
        // ==================================================================================================================

        /// <summary>Seconds the synthetic take holds still before and after its move (the guide's neutral pose).</summary>
        public const float SyntheticHold = 0.6f;
        public const string SyntheticClip = "Assets/Animations/Mixamo/Player/Punch_Right.fbx";

        public static void SyntheticBatch()
        {
            string dir = Arg("-mocapOut") ?? Path.Combine(WorkDir, "synthetic");
            int code = 1;
            try
            {
                Synthetic(Path.Combine(dir, "calibration_00.mp4"), null);
                Synthetic(Path.Combine(dir, "player_punch_00.mp4"), LoadClip(SyntheticClip));
                code = 0;
            }
            catch (Exception e) { Debug.LogError("[FPSKit] Mocap synthetic failed: " + e); }
            FPSKitBatch.Exit(code);
        }

        static void Synthetic(string output, AnimationClip move)
        {
            const int W = 1280, H = 720;
            using var stage = new Stage(PanelW, PanelH);
            Vector3 at = new Vector3(0f, 2000f, 0f);
            // As the guide asks: side-on, right side to the phone, turned 15 degrees towards it (the move set along +Z below).
            Vector3 phone = new Vector3(3.3f, 1.1f, 0.9f);
            Vector3 look = (new Vector3(0f, 0.95f, 0.2f) - phone).normalized;
            stage.Room(at, -new Vector3(look.x, 0, look.z).normalized);
            var (go, anim) = stage.Soldier(at, 1.75f);
            var cam = stage.Camera(at + phone, look, Vector3.up, 44.4f);   // 26 mm equivalent, 16:9, vertical
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var handler = new HumanPoseHandler(anim.avatar, anim.transform);
            try
            {
                using var enc = new Encoder(Ffmpeg, output, W, H);
                void Shoot()
                {
                    stage.Bake();
                    var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                    RenderPipeline.SubmitRenderRequest(cam, req);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    tex.Apply(false);
                    RenderTexture.active = prev;
                    stage.Unbake();
                    enc.Write(tex.GetRawTextureData());
                }

                if (move != null)
                {
                    var play = stage.Play(anim, move, false);
                    // The way the blow goes (the right hand at its furthest from the right shoulder) set along +Z, so the
                    // phone sees it cross the picture the way it would see Ishaan's.
                    var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                    var shoulder = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    Vector3 blow = Vector3.forward; float far = 0f;
                    for (float t = 0f; t <= move.length; t += 1f / 30f)
                    {
                        Stage.Show(play, t);
                        Vector3 d = hand.position - shoulder.position;
                        if (d.magnitude > far) { far = d.magnitude; blow = d; }
                    }
                    blow.y = 0f;
                    if (blow.sqrMagnitude > 1e-4f) go.transform.rotation = Quaternion.FromToRotation(blow.normalized, Vector3.forward) * go.transform.rotation;
                    int hold = Mathf.RoundToInt(SyntheticHold * Rate);
                    int frames = Mathf.RoundToInt(move.length * Rate);
                    for (int i = 0; i < hold; i++) { Stage.Show(play, 0f); Shoot(); }
                    for (int i = 0; i <= frames; i++) { Stage.Show(play, i / Rate); Shoot(); }
                    for (int i = 0; i < hold; i++) { Stage.Show(play, move.length); Shoot(); }
                }
                else
                {
                    // Calibration, as the guide asks: stand still, arms straight out to the sides for 3 s, then turn once slowly.
                    var idle = LoadClip("Assets/Animations/Mixamo/Idle/Idle_Breathing.fbx");
                    var play = stage.Play(anim, idle, false);
                    Stage.Show(play, 0f);
                    var stand = new HumanPose();
                    handler.GetHumanPose(ref stand);
                    var tpose = TPoseMuscles();
                    float total = 12.5f;
                    for (int i = 0; i <= Mathf.RoundToInt(total * Rate); i++)
                    {
                        float t = i / Rate;
                        Stage.Show(play, t % idle.length);
                        var pose = new HumanPose();
                        handler.GetHumanPose(ref pose);
                        float arms = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.5f, 2f, t)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5f, 5.5f, t)));
                        for (int j = 0; j < pose.muscles.Length; j++)
                            if (HumanTrait.MuscleName[j].Contains("Arm") || HumanTrait.MuscleName[j].Contains("Shoulder") || HumanTrait.MuscleName[j].Contains("Forearm"))
                                pose.muscles[j] = Mathf.Lerp(pose.muscles[j], tpose[j], arms);
                        handler.SetHumanPose(ref pose);
                        float turn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5.5f, 11.5f, t)) * 360f;
                        go.transform.rotation = Quaternion.Euler(0f, turn, 0f);
                        Shoot();
                    }
                }
            }
            finally
            {
                handler.Dispose();
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
            Debug.Log("[FPSKitBatch] Mocap synthetic take -> " + output);
        }

        /// <summary>The muscles of the avatar's own T-pose (arms straight out), read off the bake rig.</summary>
        static float[] TPoseMuscles()
        {
            using var rig = new Rig();
            rig.Reset();
            var p = new HumanPose();
            rig.handler.GetHumanPose(ref p);
            return p.muscles;
        }

        // ==================================================================================================================
        // The check against the known answer: the synthetic take's baked clip against the clip it was filmed from, both
        // played on the bake rig, every joint measured from the hips in the body's own frame (so where he stands and which
        // way he faces do not count), the two lined up in time by the best of half a second either way.
        //   Tools/unity-batch.sh FPSKit.EditorTools.FPSKitMocap.CheckSyntheticBatch -mocapTake player_punch_00
        // ==================================================================================================================

        public static void CheckSyntheticBatch()
        {
            int code = 1;
            try
            {
                string take = Arg("-mocapTake", "player_punch_00");
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{FolderFor(take)}/{take}.anim");
                var q = JsonUtility.FromJson<Quality>(File.ReadAllText(Path.Combine(WorkDir, take, "quality.json")));
                var report = CheckAgainst(clip, LoadClip(SyntheticClip), q.trimStart - SyntheticHold);
                File.WriteAllText(Path.Combine(WorkDir, take, "check.txt"), report);
                Debug.Log("[FPSKitBatch] Mocap check " + take + ": " + report);
                code = 0;
            }
            catch (Exception e) { Debug.LogError("[FPSKit] Mocap check failed: " + e); }
            FPSKitBatch.Exit(code);
        }

        /// <summary>
        /// The retarget alone, without a video: the synthetic take's own clip sampled on the rig, its joints written out
        /// as the tracker's 33 points would be (motion.json), baked like a take, and the result held against the clip.
        /// What is left is the retarget's own error; the rest of the synthetic check's error is the tracking's.
        ///   Tools/unity-batch.sh FPSKit.EditorTools.FPSKitMocap.CheckRetargetBatch
        /// </summary>
        public static void CheckRetargetBatch()
        {
            int code = 1;
            const string take = "player_roundtrip_00";
            try
            {
                var truth = LoadClip(SyntheticClip);
                Directory.CreateDirectory(Path.Combine(WorkDir, take));
                File.WriteAllText(Path.Combine(WorkDir, take, "motion.json"), JsonUtility.ToJson(PointsOf(truth)));
                var q = Bake(take);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{FolderFor(take)}/{take}.anim");
                string report = CheckAgainst(clip, truth, q.trimStart);
                File.WriteAllText(Path.Combine(WorkDir, take, "check.txt"), report);
                Debug.Log("[FPSKitBatch] Mocap retarget round trip: " + report);
                code = 0;
            }
            catch (Exception e) { Debug.LogError("[FPSKit] Mocap retarget check failed: " + e); }
            FPSKitBatch.Exit(code);
        }

        /// <summary>A clip's joints as the tracker would give them: 33 points a frame, metres, 60 a second.</summary>
        static Motion PointsOf(AnimationClip clip)
        {
            using var rig = new Rig();
            int n = Mathf.RoundToInt(clip.length * Rate) + 1;
            var m = new Motion { take = "roundtrip", fps = Rate, frames = n, height = 1.8f, scale = 1f, calibrated = true,
                                 positions = new float[n * 99], contactLeft = new int[n], contactRight = new int[n],
                                 autoStart = 0f, autoEnd = (n - 1) / Rate };
            var graph = PlayableGraph.Create("MocapPoints");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "pose", rig.anim);
                var play = AnimationClipPlayable.Create(graph, clip);
                play.SetApplyFootIK(false);
                output.SetSourcePlayable(play);
                Transform B(HumanBodyBones b) => rig.anim.GetBoneTransform(b);
                for (int f = 0; f < n; f++)
                {
                    rig.Reset();
                    play.SetTime(f / Rate);
                    graph.Evaluate();
                    Quaternion head = rig.HeadNow;
                    Vector3 hp = B(HumanBodyBones.Head).position;
                    void Put(int j, Vector3 v) { m.positions[(f * 33 + j) * 3] = v.x; m.positions[(f * 33 + j) * 3 + 1] = v.y; m.positions[(f * 33 + j) * 3 + 2] = v.z; }
                    Put(Nose, hp + head * new Vector3(0f, 0.06f, 0.11f));
                    Put(1, hp + head * new Vector3(-0.02f, 0.09f, 0.09f)); Put(LEye, hp + head * new Vector3(-0.035f, 0.09f, 0.09f)); Put(3, hp + head * new Vector3(-0.05f, 0.09f, 0.08f));
                    Put(4, hp + head * new Vector3(0.02f, 0.09f, 0.09f)); Put(REye, hp + head * new Vector3(0.035f, 0.09f, 0.09f)); Put(6, hp + head * new Vector3(0.05f, 0.09f, 0.08f));
                    Put(LEar, hp + head * new Vector3(-0.075f, 0.07f, 0f)); Put(REar, hp + head * new Vector3(0.075f, 0.07f, 0f));
                    Put(9, hp + head * new Vector3(-0.025f, 0.02f, 0.1f)); Put(10, hp + head * new Vector3(0.025f, 0.02f, 0.1f));
                    Put(LShoulder, B(HumanBodyBones.LeftUpperArm).position); Put(RShoulder, B(HumanBodyBones.RightUpperArm).position);
                    Put(LElbow, B(HumanBodyBones.LeftLowerArm).position); Put(RElbow, B(HumanBodyBones.RightLowerArm).position);
                    Put(LWrist, B(HumanBodyBones.LeftHand).position); Put(RWrist, B(HumanBodyBones.RightHand).position);
                    Put(17, B(HumanBodyBones.LeftLittleProximal).position); Put(18, B(HumanBodyBones.RightLittleProximal).position);
                    Put(19, B(HumanBodyBones.LeftIndexProximal).position); Put(20, B(HumanBodyBones.RightIndexProximal).position);
                    Put(21, B(HumanBodyBones.LeftThumbIntermediate).position); Put(22, B(HumanBodyBones.RightThumbIntermediate).position);
                    Put(LHip, B(HumanBodyBones.LeftUpperLeg).position); Put(RHip, B(HumanBodyBones.RightUpperLeg).position);
                    Put(LKnee, B(HumanBodyBones.LeftLowerLeg).position); Put(RKnee, B(HumanBodyBones.RightLowerLeg).position);
                    Put(LAnkle, B(HumanBodyBones.LeftFoot).position); Put(RAnkle, B(HumanBodyBones.RightFoot).position);
                    foreach (var (heel, toe, foot, toes, sole) in new[] { (LHeel, LFoot, rig.lLeg.end, rig.lFootToes, rig.lHeel), (RHeel, RFoot, rig.rLeg.end, rig.rFootToes, rig.rHeel) })
                    {
                        Put(heel, foot.TransformPoint(sole));
                        Put(toe, toes != null ? toes.position + (toes.position - foot.position).normalized * 0.06f : foot.position);
                    }
                }
            }
            finally { graph.Destroy(); }
            // Hands' frames from the wrist, index and pinky points, the way the solver makes them when the hand model is blind.
            m.handLeftFrame = new float[n * 6]; m.handRightFrame = new float[n * 6];
            for (int f = 0; f < n; f++)
                foreach (var (arr, w, ix, px) in new[] { (m.handLeftFrame, LWrist, 19, 17), (m.handRightFrame, RWrist, 20, 18) })
                {
                    Vector3 fingers = (0.5f * (m.P(f, ix) + m.P(f, px)) - m.P(f, w)).normalized, across = (m.P(f, ix) - m.P(f, px)).normalized;
                    arr[f * 6] = fingers.x; arr[f * 6 + 1] = fingers.y; arr[f * 6 + 2] = fingers.z;
                    arr[f * 6 + 3] = across.x; arr[f * 6 + 4] = across.y; arr[f * 6 + 5] = across.z;
                }
            return m;
        }

        static readonly HumanBodyBones[] Checked =
        {
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.Head
        };

        static string CheckAgainst(AnimationClip captured, AnimationClip truth, float offset)
        {
            using var rig = new Rig();
            Vector3[][] Sample(AnimationClip c, int n, float from)
            {
                var graph = PlayableGraph.Create("MocapCheck");
                var res = new Vector3[n][];
                try
                {
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var output = AnimationPlayableOutput.Create(graph, "pose", rig.anim);
                    var play = AnimationClipPlayable.Create(graph, c);
                    play.SetApplyFootIK(false);
                    output.SetSourcePlayable(play);
                    for (int i = 0; i < n; i++)
                    {
                        rig.Reset();
                        play.SetTime(Mathf.Clamp(from + i / Rate, 0f, c.length));
                        graph.Evaluate();
                        // Measured from between the hips, turned only by the heading (the hip line's yaw): a pelvis
                        // tilted a little differently must not count as every joint being out.
                        Vector3 across = rig.rLeg.upper.position - rig.lLeg.upper.position;
                        across.y = 0f;
                        Quaternion toBody = Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(across.normalized, Vector3.up), Vector3.up));
                        Vector3 hips = 0.5f * (rig.lLeg.upper.position + rig.rLeg.upper.position);
                        res[i] = Checked.Select(b => toBody * (rig.anim.GetBoneTransform(b).position - hips)).ToArray();
                    }
                }
                finally { graph.Destroy(); rig.Reset(); }
                return res;
            }

            int frames = Mathf.RoundToInt(captured.length * Rate) + 1;
            var cap = Sample(captured, frames, 0f);
            float PeakSpeed(Vector3[][] a, int j) { float v = 0f; for (int i = 1; i < a.Length; i++) v = Mathf.Max(v, (a[i][j] - a[i - 1][j]).magnitude * Rate); return v; }
            float bestMean = float.MaxValue, bestShift = 0f, bestWorst = 0f;
            Vector3[][] bestTruth = null;
            float[] perJoint = null;
            for (float shift = -0.5f; shift <= 0.5f; shift += 1f / Rate)
            {
                var tr = Sample(truth, frames, offset + shift);
                float sum = 0f, worst = 0f; int count = 0;
                var joints = new float[Checked.Length];
                for (int i = 0; i < frames; i++)
                    for (int j = 0; j < Checked.Length; j++)
                    {
                        float d = Vector3.Distance(cap[i][j], tr[i][j]);
                        sum += d; count++; worst = Mathf.Max(worst, d); joints[j] += d / frames;
                    }
                if (sum / count < bestMean) { bestMean = sum / count; bestShift = shift; bestWorst = worst; perJoint = joints; bestTruth = tr; }
            }
            // The same against the captured body mirrored left for right: near zero here would mean a mirror in the chain.
            var swapIdx = new[] { 1, 0, 3, 2, 5, 4, 7, 6, 8 };
            float mirror = 0f; int mc = 0;
            for (int i = 0; i < frames; i++)
                for (int j = 0; j < Checked.Length; j++)
                {
                    Vector3 c = cap[i][swapIdx[j]]; c.x = -c.x;
                    mirror += Vector3.Distance(c, bestTruth[i][j]); mc++;
                }
            return $"mean joint error {bestMean * 100f:0.0} cm, worst {bestWorst * 100f:0.0} cm (time shift {bestShift:+0.00;-0.00} s) | " + $"(mirrored {mirror / mc * 100f:0.0} cm) | " +
                   $"peak hand speed (from the hips) captured {PeakSpeed(cap, 1):0.0} / truth {PeakSpeed(bestTruth, 1):0.0} m/s right, {PeakSpeed(cap, 0):0.0} / {PeakSpeed(bestTruth, 0):0.0} left | " +
                   string.Join(", ", Checked.Select((b, j) => $"{b} {perJoint[j] * 100f:0.0}"));
        }
    }
}
#endif
