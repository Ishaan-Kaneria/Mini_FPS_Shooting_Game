#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The get-up, frame by frame (audit finding 4, 2026-10-18). The limb test once failed its
    /// bone-continuity check (the head turned 32.8 degrees in a frame during GetUp_Stomach) and a
    /// falls-only run reported a 12.7 m pelvis step and a 180-degree root turn after a get-up.
    ///
    /// This kicks one enemy over and over (the limb test's D: a Grunt, calm get-ups, 30 game frames
    /// a second) and samples once per *game* frame -- the limb test samples once per editor tick,
    /// and two ticks in one frame read as a stop then a double step -- from the kick to a second
    /// after it stands. Per frame: what the body is doing, the root and its yaw, the pelvis, every
    /// watched bone's turn, the agent, who moves the root, the Injured layer's weight and clip time.
    /// Any frame past the limb test's thresholds (pelvis 8 cm/frame of sudden change, a bone 25
    /// degrees/frame) fails, with the frames around it -- unless sampling the clip alone at the same
    /// clip times gives the same change (the clip's own motion, played as fast as it was; then the
    /// GetUp_Stomach_Zombie arm swing is also rendered, Build/GetUpProbe/zombie_arm_*.png, for an eye
    /// to judge). The root is not drawn: its jumps (the hand-over moves it under the pelvis on purpose,
    /// AlignRise turns it) are listed, not judged. A pelvis pop while walking off after the clip has
    /// let go is the locomotion's, listed separately.
    ///
    ///   Tools/unity-batch.sh FPSKit.EditorTools.FPSKitGetUpProbe.Batch     (Build/GetUpProbe/report.txt)
    /// </summary>
    public static class FPSKitGetUpProbe
    {
        const string ScenePath = "Assets/FPSKit_Generated/Scenes/AITest.unity";
        const string Prefab = "Assets/FPSKit_Generated/Enemy.prefab";
        const string Folder = "Build/GetUpProbe";
        /// <summary>Falls per run; <c>-fpskitFalls N</c> on the command line for more.</summary>
        static int Falls = 10;
        const float MaxJerk = 0.08f, MaxSpin = 25f, MaxRootStep = 0.3f, MaxRootTurn = 20f;

        static readonly HumanBodyBones[] Ids =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
        };

        public static bool Running { get; private set; }

        class Row
        {
            public int frame; public float t; public string state; public Vector3 root; public float yaw;
            public Vector3 hips; public float jerk; public float worstSpin; public string worstBone;
            public float rootStep, rootTurn; public string agent; public bool rootMotion; public float injured;
            public string clip; public bool align; public string rifle; public string look;
            public string clipName; public float clipTime; public int worstIndex = -1; public bool injuredFull;
        }

        static readonly List<Row> _rows = new List<Row>();
        static readonly StringBuilder _out = new StringBuilder();
        static readonly List<string> _fails = new List<string>();
        static readonly List<string> _locomotion = new List<string>();
        static readonly List<string> _rootNotes = new List<string>();
        static int _lastFrame, _kicks, _dupTicks, _skipped, _step;
        static float _nextKick, _upAt;
        static double _start;
        static GameObject _player, _d;
        static EnemyBody _body;
        static Animator _anim;
        static NavMeshAgent _agent;
        static EnemyMotor _motor;
        static Transform[] _bones;
        static Quaternion[] _lastRot;
        static float[] _lastSpin;
        static Vector3 _lastHips, _lastStep, _lastRoot;
        static float _lastYaw;
        static bool _watching, _wasDown, _first;
        static int _injuredLayer;
        static FieldInfo _alignField, _downField;
        static float _worstJerk, _worstSpin, _worstRootStep, _worstRootTurn;

        [MenuItem("FPSKit/Enemy AI/Run Get-Up Probe")]
        public static void Run()
        {
            if (Running || EditorApplication.isPlaying) return;
            if (!File.Exists(ScenePath)) FPSKitAITestScene.Build();
            else EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            _rows.Clear(); _out.Clear(); _fails.Clear(); _locomotion.Clear(); _rootNotes.Clear();
            _lastFrame = -1; _kicks = 0; _dupTicks = 0; _skipped = 0; _step = 0; _nextKick = 1f; _upAt = -1f;
            _watching = _wasDown = false;
            _stripDone = false; _stripFrames = 0;
            if (Directory.Exists(Folder)) foreach (var f in Directory.GetFiles(Folder, "zombie_arm_*.png")) File.Delete(f);
            _worstJerk = _worstSpin = _worstRootStep = _worstRootTurn = 0f;
            _alignField = typeof(EnemyBody).GetField("_alignPending", BindingFlags.NonPublic | BindingFlags.Instance);
            _downField = typeof(EnemyBody).GetField("_down", BindingFlags.NonPublic | BindingFlags.Instance);
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-fpskitFalls");
            Falls = at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out int n) ? Mathf.Clamp(n, 1, 100) : 10;
            _start = EditorApplication.timeSinceStartup;
            Running = true;
            FPSKitPlayMode.SuspendStartScene();
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        public static void Batch() => Run();

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - _start > 1500) throw new Exception("get-up probe timed out");
                if (!EditorApplication.isPlaying) return;
                if (Time.frameCount == _lastFrame) { _dupTicks++; return; }
                if (_lastFrame >= 0 && Time.frameCount - _lastFrame > 1) _skipped += Time.frameCount - _lastFrame - 1;
                _lastFrame = Time.frameCount;
                Time.captureDeltaTime = 1f / 30f;

                if (_step == 0) { Setup(); _step = 1; return; }

                bool down = _body.IsDown;
                if (!down && _wasDown) _upAt = Time.time;
                _wasDown = down;

                if (_watching) Sample();

                // Watched from the kick until a second after it is up again.
                if (_watching && !down && _upAt > 0f && Time.time - _upAt > 1f)
                {
                    EndFall();
                    _watching = false;
                    _nextKick = Time.time + 1.2f;
                }

                var rifle = _d.GetComponent<EnemyRifle>();
                bool rifleSettled = rifle == null || rifle.State == EnemyRifle.Hold.Held || rifle.Lost;
                if (!_watching && !down && Time.time >= _nextKick && rifleSettled)
                {
                    if (_kicks >= Falls) { Finish(); return; }
                    Kick();
                }
                if (Time.time > 40f * Falls + 60f) throw new Exception($"only {_kicks} falls in {Time.time:0} s of game time");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _fails.Add("error: " + e.Message);
                Finish();
            }
        }

        static void Setup()
        {
            _player = GameObject.FindGameObjectWithTag("Player");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            var at = new Vector3(4.5f, 0f, 14f);
            _d = Object.Instantiate(prefab, at, Quaternion.LookRotation(-at.normalized));
            _d.name = "D_Kicks";
            FPSKitEnemyRoster.GetOrCreate("Grunt").ApplyTo(_d);
            _body = _d.GetComponent<EnemyBody>();
            _body.rageSeconds = 0f;   // calm get-ups, as the limb test's falls
            _anim = _d.GetComponentInChildren<Animator>();
            _agent = _d.GetComponent<NavMeshAgent>();
            _motor = _d.GetComponent<EnemyMotor>();
            _injuredLayer = _anim.GetLayerIndex("Injured");
            _bones = Array.ConvertAll(Ids, b => _anim.GetBoneTransform(b));
            _lastRot = new Quaternion[_bones.Length];
            _lastSpin = new float[_bones.Length];
            var h = _d.GetComponent<Health>();
            h.maxHealth = 100000f; h.ResetHealth();
        }

        static void Kick()
        {
            var ai = _d.GetComponent<EnemyAI>();
            Vector3 away = _d.transform.position - _player.transform.position;
            away.y = 0f;
            // Thrown towards the middle when it has wandered off, so it stays on the test floor.
            if (_d.transform.position.magnitude > 18f) away = -_d.transform.position;
            ai.Knock((away.normalized + Vector3.up * 0.25f) * 7.5f, 1.5f);
            _kicks++;
            _rows.Clear();
            _first = true;
            _upAt = -1f;
            _watching = true;
        }

        static string State()
        {
            string down = _downField != null ? _downField.GetValue(_body).ToString() : (_body.IsDown ? "Down" : "Up");
            return _body.Rising ? $"{down}:{_body.LastRise}" : down;
        }

        static void Sample()
        {
            var r = new Row
            {
                frame = Time.frameCount, t = Time.time, state = State(),
                root = _d.transform.position, yaw = _d.transform.eulerAngles.y, hips = _bones[0].position,
                agent = _agent == null ? "-" : !_agent.enabled ? "off" : $"{(_agent.updatePosition ? "moves" : "follows")} {_agent.nextPosition.ToString("F2")}",
                rootMotion = _motor != null && _motor.RootMotion,
                injured = _injuredLayer >= 0 ? _anim.GetLayerWeight(_injuredLayer) : 0f,
                align = _alignField != null && (bool)_alignField.GetValue(_body),
                rifle = _d.GetComponent<EnemyRifle>() is EnemyRifle er ? er.State.ToString() : "-",
            };
            {
                float F(string n) => typeof(EnemyBody).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance) is FieldInfo f ? (float)f.GetValue(_body) : float.NaN;
                var baseInfo = _anim.GetCurrentAnimatorClipInfo(0);
                var brain = _d.GetComponent<EnemyBrain>();
                Vector3 lp = brain != null ? brain.LookPoint : Vector3.zero;
                float lookAngle = Vector3.SignedAngle(Vector3.ProjectOnPlane(_d.transform.forward, Vector3.up), Vector3.ProjectOnPlane(lp - _bones[3].position, Vector3.up), Vector3.up);
                var layers = new StringBuilder();
                for (int li = 1; li < _anim.layerCount; li++) layers.Append($" {_anim.GetLayerName(li)}:{_anim.GetLayerWeight(li):0.00}");
                r.look = layers + $" | headYaw {F("_headYaw"),6:0.0} pitch {F("_headPitch"),5:0.0} aimYaw {F("_aimYaw"),5:0.0} aimW {F("_aimWeight"):0.00} look@{lookAngle,6:0.0} base {(baseInfo.Length > 0 ? baseInfo[0].clip.name : "?")}{(_anim.IsInTransition(0) ? "(x)" : "")}";
            }
            if (_injuredLayer >= 0)
            {
                var info = _anim.GetCurrentAnimatorStateInfo(_injuredLayer);
                var clips = _anim.GetCurrentAnimatorClipInfo(_injuredLayer);
                r.clip = (clips.Length > 0 ? clips[0].clip.name : "?") + $"@{info.normalizedTime:0.00}" + (_anim.IsInTransition(_injuredLayer) ? "(x)" : "");
                r.clipName = clips.Length > 0 ? clips[0].clip.name : null;
                r.clipTime = info.normalizedTime;
                // The clip has the body: at least half the pose (fading out after the rise ends, its
                // own motion still shows), and no cross-fade to another clip under way.
                r.injuredFull = _anim.GetLayerWeight(_injuredLayer) >= 0.5f && !_anim.IsInTransition(_injuredLayer);
                if (r.clipName == "GetUp_Stomach_Zombie" && r.clipTime >= 0.36f && r.clipTime <= 0.48f && !_stripDone) Strip(r);
                else if (_stripFrames > 0) _stripDone = true;
            }

            if (_first)
            {
                for (int i = 0; i < _bones.Length; i++) { _lastRot[i] = _bones[i].rotation; _lastSpin[i] = 0f; }
                _lastHips = r.hips; _lastStep = Vector3.zero; _lastRoot = r.root; _lastYaw = r.yaw;
                _first = false;
            }
            else
            {
                // Judged as in the limb test, but only once the clip has the body: thrown, the
                // ragdoll moves as physics does. Root jumps are judged throughout.
                Vector3 step = r.hips - _lastHips;
                r.jerk = (step - _lastStep).magnitude;
                _lastStep = step;
                for (int i = 0; i < _bones.Length; i++)
                {
                    float spin = Quaternion.Angle(_bones[i].rotation, _lastRot[i]);
                    float change = Mathf.Abs(spin - _lastSpin[i]);
                    if (change > r.worstSpin) { r.worstSpin = change; r.worstBone = _bones[i].name; r.worstIndex = i; }
                    _lastSpin[i] = spin;
                    _lastRot[i] = _bones[i].rotation;
                }
                r.rootStep = Vector3.Distance(r.root, _lastRoot);
                r.rootTurn = Mathf.Abs(Mathf.DeltaAngle(r.yaw, _lastYaw));
                _lastHips = r.hips; _lastRoot = r.root; _lastYaw = r.yaw;
            }
            _rows.Add(r);
        }

        /// <summary>The root itself is moved on purpose under the pelvis when the clip takes over, and turned by AlignRise: invisible, not a pop.</summary>
        static bool HandOver(List<Row> rows, int i) => rows[i - 1].state.StartsWith("Flying") || rows[i - 1].align || rows[i].align;

        static void EndFall()
        {
            int worst = -1;
            float score = 0f;
            string why = "";
            for (int i = 1; i < _rows.Count; i++)
            {
                var r = _rows[i];
                // Judged from the hand-over to the clip on (a ragdoll in the air moves as physics does).
                bool judged = !r.state.StartsWith("Flying");
                bool handOver = HandOver(_rows, i);
                float s = 0f;
                string w = "";
                if (judged && r.jerk >= MaxJerk) { s = Mathf.Max(s, r.jerk / MaxJerk); w += $" pelvis {r.jerk * 100f:0.0} cm/frame;"; }
                if (judged && r.worstSpin >= MaxSpin)
                {
                    // The clip's own motion at those moments, played as fast as it was: a fast get-up's
                    // sharpest frame is the clip's, not a pop. Only on the clip alone (the Injured layer
                    // full, not mid-transition, nothing else posed into it).
                    float own = i >= 2 ? ClipOwnChange(_rows[i - 2], _rows[i - 1], r) : -1f;
                    if (own >= 0f && r.worstSpin <= own + 5f + r.rootTurn)
                        _out.Append($"\n    fall {_kicks} frame {r.frame}: {r.worstBone} {r.worstSpin:0.0} deg/frame is the clip's own ({r.clipName} at {r.clipTime:0.00}: {own:0.0} deg/frame at the speed it was played)");
                    else { s = Mathf.Max(s, r.worstSpin / MaxSpin); w += $" {r.worstBone} {r.worstSpin:0.0} deg/frame{(own >= 0f ? $" (the clip alone: {own:0.0})" : "")};"; }
                }
                // The root is not drawn: a root jump the pelvis does not show (the blend from the ragdoll
                // holds the body where it lay while the navmesh settles the root) is noted, not judged.
                if ((r.rootStep >= MaxRootStep || r.rootTurn >= MaxRootTurn) && !r.state.StartsWith("Flying") && !handOver)
                    _rootNotes.Add($"fall {_kicks} frame {r.frame}: root {r.rootStep:0.00} m, {r.rootTurn:0.0} deg in a frame, pelvis {r.jerk * 100f:0.0} cm/frame of change");
                if (judged) { _worstJerk = Mathf.Max(_worstJerk, r.jerk); _worstSpin = Mathf.Max(_worstSpin, r.worstSpin); }
                if (!r.state.StartsWith("Flying") && !handOver) { _worstRootStep = Mathf.Max(_worstRootStep, r.rootStep); _worstRootTurn = Mathf.Max(_worstRootTurn, r.rootTurn); }
                if (s > score) { score = s; worst = i; why = w; }
            }

            string rise = "";
            foreach (var r in _rows) if (r.state.Contains(":")) { rise = r.state.Substring(r.state.IndexOf(':') + 1); break; }
            _out.Append($"\nfall {_kicks}: rose with {rise}, {_rows.Count} frames watched");
            if (worst < 0) { _out.Append(": continuous"); return; }

            // Walking off once the get-up clip has let go entirely is the locomotion's, not the get-up's:
            // reported, not judged here (EnemyMotor's eight-way blend starting a walk).
            var wr = _rows[worst];
            bool locomotion = wr.state == "Up" && wr.injured <= 0.001f && wr.rootStep > 0.02f && why.StartsWith(" pelvis");
            if (locomotion)
            {
                _locomotion.Add($"fall {_kicks} frame {wr.frame}:{why} walking off {wr.t - _rows[0].t:0.00} s after the kick, the get-up clip already released");
                _out.Append($": continuous get-up; then, walking off,{why} (locomotion, reported below)");
                return;
            }
            _fails.Add($"fall {_kicks} ({rise}) frame {_rows[worst].frame}:{why}");
            _out.Append($": POP at frame {_rows[worst].frame} ({(_rows[worst].t - _rows[0].t):0.00} s after the kick):{why}\n");
            _out.Append("    frame  t      state                       root (x,y,z)            yaw    rstep  rturn  pelvis-jerk  worst bone change   agent                      rootMotion injured clip                     align rifle\n");
            for (int i = Mathf.Max(0, worst - 8); i < Mathf.Min(_rows.Count, worst + 6); i++)
            {
                var r = _rows[i];
                _out.Append($"  {(i == worst ? ">" : " ")} {r.frame,5} {r.t - _rows[0].t,6:0.00} {r.state,-27} {r.root.ToString("F2"),-22} {r.yaw,6:0.0} {r.rootStep,6:0.00} {r.rootTurn,6:0.0} {r.jerk * 100f,8:0.0}cm  {r.worstSpin,6:0.0} {r.worstBone,-14} {r.agent,-26} {r.rootMotion,-10} {r.injured,5:0.00} {r.clip,-24} {r.align,-5} {r.rifle,-10} {r.look}\n");
            }
        }

        static bool _stripDone;
        static int _stripFrames;

        /// <summary>
        /// GetUp_Stomach_Zombie's arm swing (0.36 to 0.48 of the clip), every frame, from the side: its
        /// own motion goes past the 25-degree bar when the rise is played fast, and that is to be judged
        /// by eye, not excused by a number. Needs a graphics device; once per run.
        /// </summary>
        static void Strip(Row r)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { _stripDone = true; return; }
            var cam = Camera.main;
            if (cam == null) { _stripDone = true; return; }
            Vector3 at = _bones[2].position;   // the chest
            Vector3 side = Vector3.ProjectOnPlane(_d.transform.right, Vector3.up).normalized;
            cam.transform.position = at + side * 3f + Vector3.up * 0.2f;
            cam.transform.LookAt(at);
            cam.fieldOfView = 50f;
            var rt = RenderTexture.GetTemporary(480, 360, 24, RenderTextureFormat.ARGB32);
            try
            {
                var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes($"{Folder}/zombie_arm_{_stripFrames:00}_t{r.clipTime:0.000}.png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                _stripFrames++;
            }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }

        static GameObject _sampler;
        static Transform[] _sampleBones;
        static PlayableGraph _graph;
        static AnimationClipPlayable _playable;
        static string _sampled;

        /// <summary>A bare copy of the skeleton and Animator: none of the enemy's scripts, colliders or bodies.</summary>
        static void MakeSampler()
        {
            var holder = new GameObject("GetUpProbe_SamplerHolder");
            holder.SetActive(false);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            var copy = Object.Instantiate(prefab, holder.transform);
            foreach (var j in copy.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(j);
            foreach (var b in copy.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(b);
            foreach (var a in copy.GetComponentsInChildren<NavMeshAgent>(true)) Object.DestroyImmediate(a);
            foreach (var rb in copy.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in copy.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            copy.transform.SetParent(null, false);
            copy.transform.SetPositionAndRotation(new Vector3(0f, -200f, 0f), Quaternion.identity);
            Object.Destroy(holder);
            var anim = copy.GetComponentInChildren<Animator>(true);
            anim.runtimeAnimatorController = null;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // its renderers are off
            anim.applyRootMotion = false;
            _sampleBones = Array.ConvertAll(Ids, b => anim.GetBoneTransform(b));
            _graph = PlayableGraph.Create("GetUpProbe");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            AnimationPlayableOutput.Create(_graph, "pose", anim);
            _sampler = copy;
        }

        static Quaternion SampleBone(string clipName, float normalized, int bone)
        {
            if (_sampler == null) MakeSampler();
            if (_sampled != clipName)
            {
                AnimationClip clip = null;
                foreach (var c in _anim.runtimeAnimatorController.animationClips) if (c != null && c.name == clipName) { clip = c; break; }
                if (clip == null) return Quaternion.identity;
                if (_playable.IsValid()) _playable.Destroy();
                _playable = AnimationClipPlayable.Create(_graph, clip);
                _graph.GetOutput(0).SetSourcePlayable(_playable);
                _sampled = clipName;
            }
            _playable.SetTime(Mathf.Clamp01(normalized) * _playable.GetAnimationClip().length);
            _graph.Evaluate();
            return Quaternion.Inverse(_sampler.transform.rotation) * _sampleBones[bone].rotation;
        }

        /// <summary>
        /// The flagged bone's change of turn in the clip alone, sampled at the three frames' clip times;
        /// -1 when the clip did not have the body. Judged against the live change with 5 degrees to
        /// spare plus that frame's turn of the root (which turns every bone with it).
        /// </summary>
        static float ClipOwnChange(Row a, Row b, Row c)
        {
            if (c.worstIndex < 0 || c.clipName == null || !a.injuredFull || !b.injuredFull || !c.injuredFull) return -1f;
            if (a.clipName != c.clipName || b.clipName != c.clipName) return -1f;
            var qa = SampleBone(c.clipName, a.clipTime, c.worstIndex);
            var qb = SampleBone(c.clipName, b.clipTime, c.worstIndex);
            var qc = SampleBone(c.clipName, c.clipTime, c.worstIndex);
            return Mathf.Abs(Quaternion.Angle(qc, qb) - Quaternion.Angle(qb, qa));
        }

        static void Finish()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (_sampler != null) Object.Destroy(_sampler);
            _sampler = null; _sampled = null;
            EditorApplication.update -= Tick;
            Time.captureDeltaTime = 0f;
            Running = false;
            string head = (_fails.Count == 0 ? $"get-up probe: {_kicks} falls, every get-up continuous" : $"get-up probe FAILED: {_fails.Count} pop(s) in {_kicks} falls")
                          + $"\n  worst once animated: pelvis change {_worstJerk * 100f:0.0} cm/frame (limit {MaxJerk * 100f:0}), bone change {_worstSpin:0.0} deg/frame (limit {MaxSpin:0});"
                          + $" root step {_worstRootStep:0.00} m (limit {MaxRootStep}), root turn {_worstRootTurn:0.0} deg (limit {MaxRootTurn})"
                          + $"\n  sampling: one sample per game frame; {_dupTicks} editor tick(s) landed on an already-sampled frame (the limb test would have read each as a stop), {_skipped} frame(s) passed unsampled";
            string report = head + _out + (_fails.Count > 0 ? "\n\n" + string.Join("\n", _fails) : "")
                            + (_rootNotes.Count > 0 ? "\n\nthe (undrawn) root moved without the pelvis showing it:\n  " + string.Join("\n  ", _rootNotes) : "")
                            + (_locomotion.Count > 0 ? "\n\nnot the get-up (locomotion starting a walk, over the limit, reported):\n  " + string.Join("\n  ", _locomotion) : "");
            Directory.CreateDirectory(Folder);
            File.WriteAllText($"{Folder}/report.txt", report);
            if (_fails.Count == 0) Debug.Log("[FPSKitBatch] " + report);
            else Debug.LogError("[FPSKitBatch] FAILED: " + report);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += FPSKitPlayMode.RestoreStartScene;
            FPSKitBatch.Exit(_fails.Count == 0 ? 0 : 1);
        }
    }
}
#endif
