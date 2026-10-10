#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// The player's own body (see <see cref="PlayerBody"/>): the enemies' Survivalist soldier in
    /// an outfit of his own (no cap, no pack), sized so his eyes are at the camera's height,
    /// with an Animator controller of the same Mixamo rifle clips the enemies move in and the
    /// player's strikes and reload over them.
    ///
    ///   FPSKit > Player > Build Body
    ///
    /// Writes Assets/FPSKit_Generated/Player/PlayerBody.prefab and .controller, and fills the
    /// body, the strike table and the measured clip timings into PlayerBodySettings (only those:
    /// every other value there is the Inspector's).
    ///
    /// The strike clips are looked for in Assets/Animations/Mixamo/Player/ by name; a strike with
    /// no clip there is drawn as it was before the body (MeleeStrike's own rifle movement).
    /// </summary>
    public static class FPSKitPlayerBody
    {
        const string Folder = "Assets/FPSKit_Generated/Player";
        const string PrefabPath = Folder + "/PlayerBody.prefab";
        const string ControllerPath = Folder + "/PlayerBody.controller";
        const string Source = "Assets/Survivalist/Prefab/Survivalist (1).prefab";

        /// <summary>Metres from the floor to the player's eyes standing: PlayerMotor's 1.8 m height less its 0.15 m to the top.</summary>
        const float EyeHeight = 1.65f;

        /// <summary>His outfit: the same garments as the enemies, in a mix none of them wears.</summary>
        static readonly (string mesh, string skin)[] Outfit =
        {
            ("SK_Military_Head1", null),
            ("SK_Military_Male_Arms1", null),
            ("SK_Military_Shirt3", "Shirt3_Skin1"),
            ("SK_Military_Pants2", "Pants2_Skin2"),
            ("SK_Military_Boots3", "Boots3_Skin1"),
            ("SK_Military_Vest1", "Vest1_Skin2"),
        };

        /// <summary>
        /// Each strike's clip (under Mixamo/Player/, the first that exists), the bone that lands
        /// the blow, which part of the clip plays, and where the rifle is meanwhile. "Last" takes
        /// the clip's last blow (a jab-cross's cross) and plays from between the two.
        /// </summary>
        static readonly (MeleeStrike.Strike strike, string[] files, HumanBodyBones striker, bool last, PlayerBodySettings.RifleDuring rifle)[] Strikes =
        {
            (MeleeStrike.Strike.Jab, new[] { "Punch_Right", "Punch_Jab" }, HumanBodyBones.RightHand, false, PlayerBodySettings.RifleDuring.LeftHand),
            (MeleeStrike.Strike.Cross, new[] { "Punch_JabCross" }, HumanBodyBones.RightHand, true, PlayerBodySettings.RifleDuring.Sling),
            (MeleeStrike.Strike.Kick, new[] { "Kick_Front" }, HumanBodyBones.RightFoot, false, PlayerBodySettings.RifleDuring.RightHand),
            // No clip for the rifle butt: Mixamo's "Rifle Punch" (Melee_RifleButt.fbx) turns the body 77
            // degrees and lets go of the rifle; MeleeStrike's own short stock-forward jab is drawn instead.
        };

        [MenuItem("FPSKit/Player/Build Body", false, 46)]
        public static void Build()
        {
            FPSKitSceneBuilder.EnsureProjectTagsAndLayers();
            FPSKitRuntimeSettings.Ensure();
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/FPSKit_Generated", "Player");

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            var aim = FPSKitRiggedSoldier.Mixamo("Rifle/Rifle_Idle_Aim");
            if (source == null || aim == null) { Debug.LogError("[FPSKit] Player body: the Survivalist or the rifle clips are missing"); return; }

            int playerLayer = LayerMask.NameToLayer("Player");
            var strikes = new List<PlayerBodySettings.StrikeClip>();
            var controller = Controller(strikes);

            var root = new GameObject("PlayerBody");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                model.name = "Body";
                model.transform.SetParent(root.transform, false);
                Strip(model);
                Dress(model);

                var animator = model.GetComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);

                var bones = animator.GetComponentsInChildren<Transform>();
                var pos = new Vector3[bones.Length];
                var rot = new Quaternion[bones.Length];
                for (int i = 0; i < bones.Length; i++) { pos[i] = bones[i].localPosition; rot[i] = bones[i].localRotation; }

                var rig = root.AddComponent<PlayerBodyRig>();
                rig.animator = animator;
                try
                {
                    // Sized so the eyes in the aiming idle are at the camera's height.
                    Pose(animator, aim, 0.5f);
                    var settings = PlayerBodySettings.Active;
                    float eye = B(HumanBodyBones.Head).position.y + settings.eyeUp;
                    float scale = EyeHeight / Mathf.Max(1f, eye);
                    root.transform.localScale = Vector3.one * scale;

                    // The rifle's grip: in the right hand of the aiming idle, the bore straight ahead
                    // and level (the line between the hands is not the bore: the stance is bladed).
                    var grip = new GameObject("RifleGrip").transform;
                    grip.SetParent(B(HumanBodyBones.RightHand), false);
                    grip.SetPositionAndRotation(B(HumanBodyBones.RightHand).position, Quaternion.LookRotation(root.transform.forward, Vector3.up));
                    rig.rifleGrip = grip;

                    // The sling: across the back, muzzle down to the right, stock up over the left shoulder.
                    var chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest);
                    var sling = new GameObject("SlingPoint").transform;
                    sling.SetParent(chest, false);
                    Vector3 f = root.transform.forward, r = root.transform.right;
                    sling.SetPositionAndRotation(chest.position - f * 0.2f + Vector3.up * 0.05f + r * 0.08f,
                                                 Quaternion.LookRotation((Vector3.down + r * 0.75f).normalized, -f));
                    rig.slingPoint = sling;
                }
                finally
                {
                    for (int i = 0; i < bones.Length; i++) { bones[i].localPosition = pos[i]; bones[i].localRotation = rot[i]; }
                    model.transform.localPosition = Vector3.zero;
                    model.transform.localRotation = Quaternion.identity;
                }

                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = playerLayer;

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                // Read back from disk: the controller was deleted and made again above, and the
                // prefab already in memory kept the dead one (the body stood in its bind pose).
                AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                var check = prefab.GetComponentInChildren<Animator>();
                if (check == null || check.runtimeAnimatorController == null)
                {
                    Debug.LogError("[FPSKit] Player body: the saved body has no Animator controller");
                    return;
                }

                var s = PlayerBodySettings.Active;
                if (!AssetDatabase.Contains(s)) { Debug.LogError("[FPSKit] Player body: no PlayerBodySettings asset in Resources"); return; }
                s.bodyPrefab = prefab;
                s.strikes = strikes.ToArray();
                Calibrate(s);
                var sprint = FPSKitRiggedSoldier.Mixamo("Rifle/Move_Sprint_F");
                if (sprint != null) s.sprintClipSpeed = sprint.averageSpeed.magnitude * root.transform.localScale.x;
                EditorUtility.SetDirty(s);
                AssetDatabase.SaveAssets();

                var names = new List<string>();
                foreach (var st in strikes) names.Add($"{st.strike} {st.state} [{st.range.x:0.00}-{st.range.y:0.00}] impact {st.impact:0.00} turn {st.turn:0}");
                Debug.Log($"[FPSKit] Player body built: scale {root.transform.localScale.x:0.000}, sprint clip {s.sprintClipSpeed:0.0} m/s, " +
                          $"strikes with clips: {(names.Count == 0 ? "none" : string.Join("; ", names))}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Each strike played on the built body to the moment its blow lands, and the body's turn for it
        /// corrected by however far off the crosshair the fist or foot still is (twice). The clips' own
        /// headings are only a first guess: an upper-body clip is laid on the bladed rifle stance's hips,
        /// and a kick brings its own, so only playing it says where the blow goes.
        /// </summary>
        static void Calibrate(PlayerBodySettings s)
        {
            var data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/FPSKit_Generated/TestRifle.asset");
            for (int pass = 0; pass < 2; pass++)
            {
                var list = s.strikes;
                for (int i = 0; i < list.Length; i++)
                {
                    var bone = list[i].strike == MeleeStrike.Strike.Kick ? HumanBodyBones.RightFoot : HumanBodyBones.RightHand;
                    float off = OffCentre(list[i].strike, bone, data);
                    if (float.IsNaN(off)) continue;
                    list[i].turn += off;
                }
                s.strikes = list;
            }
        }

        /// <summary>Degrees right of the view the striking bone is when the strike's blow lands.</summary>
        static float OffCentre(MeleeStrike.Strike strike, HumanBodyBones bone, WeaponData data)
        {
            var root = new GameObject("CalibratePlayer");
            try
            {
                var camGo = new GameObject("Camera");
                camGo.transform.SetParent(root.transform, false);
                camGo.transform.localPosition = new Vector3(0f, EyeHeight, 0f);
                var cam = camGo.AddComponent<Camera>();
                var body = root.AddComponent<PlayerBody>();
                body.PreviewBuild(cam, data);
                var d = new PlayerBody.Drive { strike = MeleeStrike.Strike.None, strikeProgress = -1f };
                const float dt = 1f / 60f;
                body.preview = d;
                for (int i = 0; i < 30; i++) body.PreviewStep(dt);
                float impact = MeleeStrike.ImpactAt(strike);
                d.strike = strike;
                d.gunAway = strike == MeleeStrike.Strike.Jab || strike == MeleeStrike.Strike.Cross ? 1f : 0f;
                const int n = 20;
                for (int i = 1; i <= n; i++)
                {
                    d.strikeProgress = impact * i / n;
                    body.preview = d;
                    body.PreviewStep(0.35f * impact / n);
                }
                var animator = root.GetComponentInChildren<Animator>();
                Vector3 p = cam.transform.InverseTransformPoint(animator.GetBoneTransform(bone).position);
                return Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;
            }
            catch (System.Exception e) { Debug.LogException(e); return float.NaN; }
            finally { Object.DestroyImmediate(root); }
        }

        // ==================================================================
        // Preview: the body in each pose, through the player's eyes
        // ==================================================================

        [MenuItem("FPSKit/Player/Preview Body", false, 47)]
        public static void PreviewMenu() => Preview("Build/PlayerBody", "stand,down,aim,sprint,reload,jab,cross,kick,butt,fists", 640, 360);

        /// <summary>
        /// Renders the body in each named pose to PNGs in <paramref name="folder"/>, in a scratch
        /// scene (the open scene is reopened afterwards). The body is stepped by hand, so a pose
        /// is exactly the moment asked for: a strike at the instant its blow lands, the reload with
        /// the magazine out. Poses: stand, down, aim, sprint, walk, crouch, reload, jab, cross, kick, butt.
        /// </summary>
        public static List<string> Preview(string folder, string poses, int width, int height)
        {
            var written = new List<string>();
            var settings = PlayerBodySettings.Active;
            if (settings.bodyPrefab == null) { Debug.LogError("[FPSKit] Player body: build it first"); return written; }
            string reopen = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return written;
            System.IO.Directory.CreateDirectory(folder);

            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects,
                                                                   UnityEditor.SceneManagement.NewSceneMode.Single);
            try
            {
                // Loaded after the new scene: opening one unloads assets nothing in it uses.
                var data = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/FPSKit_Generated/TestRifle.asset");
                foreach (var c in Object.FindObjectsByType<Camera>()) Object.DestroyImmediate(c.gameObject);
                var sun = Object.FindAnyObjectByType<Light>();
                if (sun != null)
                {
                    sun.transform.rotation = Quaternion.Euler(50f, -150f, 0f);
                    sun.shadows = LightShadows.Soft;
                }
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.transform.localScale = Vector3.one * 4f;
                var grey = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                grey.SetColor("_BaseColor", new Color(0.42f, 0.42f, 0.4f));
                grey.SetFloat("_Smoothness", 0.1f);
                ground.GetComponent<Renderer>().sharedMaterial = grey;
                // Something to aim at, for scale.
                var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.transform.position = new Vector3(0f, 1f, 6f);
                post.transform.localScale = new Vector3(0.5f, 2f, 0.5f);

                DynamicGI.UpdateEnvironment();
                foreach (var pose in poses.Split(','))
                {
                    string name = pose.Trim();
                    if (name.Length == 0) continue;
                    // A fresh player each pose.
                    var root = new GameObject("PreviewPlayer");
                    try
                    {
                        var camGo = new GameObject("Camera");
                        camGo.transform.SetParent(root.transform, false);
                        camGo.transform.localPosition = new Vector3(0f, EyeHeight, 0f);
                        var cam = camGo.AddComponent<Camera>();
                        cam.fieldOfView = 75f;
                        cam.nearClipPlane = 0.05f;
                        var body = root.AddComponent<PlayerBody>();
                        body.PreviewBuild(cam, data);
                        if (!Act(body, cam, root.transform, name)) { Debug.LogWarning("[FPSKit] Player body preview: no pose " + name); continue; }
                        string path = $"{folder}/{name}.png";
                        Capture(cam, width, height, path);
                        written.Add(path);
                    }
                    finally { Object.DestroyImmediate(root); }
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(reopen)) UnityEditor.SceneManagement.EditorSceneManager.OpenScene(reopen);
            }
            Debug.Log("[FPSKit] Player body preview: " + string.Join(", ", written));
            return written;
        }

        /// <summary>Steps the body into a pose at 60 frames a second, the drive changing as the real parts would.</summary>
        static bool Act(PlayerBody body, Camera cam, Transform root, string pose)
        {
            const float dt = 1f / 60f;
            float pitch = 0f;
            var d = new PlayerBody.Drive { strike = MeleeStrike.Strike.None, strikeProgress = -1f };
            void Run(float seconds, System.Action<float> each = null)
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(seconds / dt));
                for (int i = 0; i < n; i++)
                {
                    each?.Invoke((i + 1) / (float)n);
                    cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                    body.preview = d;
                    body.PreviewStep(dt);
                }
            }
            // A strike from the start to the moment its blow lands.
            void Strike(MeleeStrike.Strike s, float gunAway)
            {
                Run(0.5f);
                float seconds = 0.35f, impact = MeleeStrike.ImpactAt(s);
                d.gunAway = gunAway;
                d.strike = s;
                Run(seconds * impact, k => d.strikeProgress = k * impact);
            }

            switch (pose)
            {
                case "stand": Run(1.5f); break;
                case "down": pitch = 65f; Run(1.5f); break;
                case "feet": pitch = 82f; Run(1.5f); break;
                case "aim": Run(0.5f); Run(0.6f, k => d.aim = Mathf.Clamp01(k * 2f)); break;
                case "walk": d.velocity = root.forward * 2.2f; Run(1.3f); break;
                case "sprint": d.velocity = root.forward * 8.2f; Run(1.3f); break;
                case "crouch": d.crouching = true; Run(1.5f); break;
                case "reload": Run(0.5f); d.reloading = true; Run(1.2f, k => d.reload = k * 0.45f); break;
                case "fists": d.gunAway = 1f; Run(1.2f); break;
                case "jab": Strike(MeleeStrike.Strike.Jab, 1f); break;
                case "cross": Strike(MeleeStrike.Strike.Cross, 1f); break;
                case "kick": Strike(MeleeStrike.Strike.Kick, 0f); break;
                case "butt": Strike(MeleeStrike.Strike.Butt, 0f); break;
                default: return false;
            }
            return true;
        }

        /// <summary>The camera with its stack (the arms camera on top) into a PNG.</summary>
        static void Capture(Camera cam, int width, int height, string path)
        {
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            try
            {
                var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
                if (!UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request)) { Debug.LogError("[FPSKit] Player body preview: cannot render"); return; }
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }

        /// <summary>Nothing but the skinned meshes and the Animator: no colliders (the player's own shots would hit them), no scripts.</summary>
        static void Strip(GameObject model)
        {
            var fps = model.transform.Find("FPS_HANDS");
            if (fps != null) Object.DestroyImmediate(fps.gameObject);
            foreach (var j in model.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(j);
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in model.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(r);
            foreach (var m in model.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(m);
        }

        static void Dress(GameObject model)
        {
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int index = System.Array.FindIndex(Outfit, o => o.mesh == smr.name);
                if (index < 0) { Object.DestroyImmediate(smr.gameObject); continue; }
                smr.gameObject.SetActive(true);
                smr.updateWhenOffscreen = true;
                smr.quality = SkinQuality.Bone2;
                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = FPSKitRiggedSoldier.Lit(mats[i] != null ? mats[i].name : null);
                if (Outfit[index].skin != null) mats[0] = FPSKitRiggedSoldier.Lit(Outfit[index].skin);
                smr.sharedMaterials = mats;
            }
        }

        static void Pose(Animator animator, AnimationClip clip, float time)
        {
            var t = animator.transform;
            Vector3 p = t.localPosition;
            Quaternion r = t.localRotation;
            clip.SampleAnimation(animator.gameObject, time);
            t.localPosition = p;
            t.localRotation = r;
        }

        // ==================================================================
        // The controller
        // ==================================================================

        /// <summary>
        /// - **Base**: Idle and Move, the enemies' rifle movement (FPSKitRiggedSoldier.BuildRifleMovement).
        /// - **Actions** (upper body): the reload and the punches, each played at the time PlayerBody
        ///   gives (ActionTime), so the clip follows Weapon's reload and MeleeStrike's strike exactly.
        /// - **LeftCarry** (left arm): the low-ready idle, mirrored: the rifle hanging in the left
        ///   hand while the right one punches.
        /// - **Kick** (legs and spine): the kick, at KickTime; the arms keep the rifle.
        /// Every layer's weight is PlayerBody's.
        /// </summary>
        static AnimatorController Controller(List<PlayerBodySettings.StrikeClip> strikes)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            FPSKitRiggedSoldier.BuildRifleMovement(controller, out var still, out var stance);
            controller.AddParameter("ActionTime", AnimatorControllerParameterType.Float);
            controller.AddParameter("KickTime", AnimatorControllerParameterType.Float);

            var sm = controller.layers[0].stateMachine;
            AnimatorState State(AnimatorStateMachine m, string name, Motion motion, string time = null)
            {
                var st = m.AddState(name);
                st.motion = motion;
                st.writeDefaultValues = false;
                if (time != null) { st.timeParameter = time; st.timeParameterActive = true; }
                return st;
            }
            sm.defaultState = State(sm, "Idle", still);
            var move = State(sm, "Move", stance);
            move.speedParameter = "MoveRate";
            move.speedParameterActive = true;

            var upper = Mask("UpperBody", AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head,
                             AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                             AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers);
            var leftArm = Mask("LeftArm", AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.LeftFingers);
            // The kick takes the clip's whole lower body, its turn included (Root): the legs alone, laid on
            // hips still facing ahead, kicked off to the side. The arms keep the rifle from the base layer.
            var legs = Mask("Legs", AvatarMaskBodyPart.Root, AvatarMaskBodyPart.Body, AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg);

            var actions = Layer(controller, "Actions", upper);
            var kick = Layer(controller, "Kick", legs);
            State(actions, "Reload", FPSKitRiggedSoldier.Mixamo("Actions/Reload_Stand"), "ActionTime");
            if (AssetDatabase.LoadMainAssetAtPath(FPSKitMixamoImport.Root + "Actions/Reload_Crouch.fbx") != null)
                State(actions, "ReloadCrouch", FPSKitRiggedSoldier.Mixamo("Actions/Reload_Crouch"), "ActionTime");
            // Fists up between punches, the rifle on the back: a looping fighting stance.
            if (AssetDatabase.LoadMainAssetAtPath(FPSKitMixamoImport.Root + "Player/Fists_Idle.fbx") != null)
                State(actions, "Fists", FPSKitRiggedSoldier.Mixamo("Player/Fists_Idle"));

            foreach (var (strike, files, striker, last, rifle) in Strikes)
            {
                var clip = Find(files, striker);
                if (clip == null) continue;
                var range = Blows(clip, striker, last, out float impact);
                // An upper-body clip is laid on hips facing ahead: its blow's heading is measured from its own hips.
                float turn = Heading(clip, striker, impact, relativeToHips: strike != MeleeStrike.Strike.Kick);
                string name = strike.ToString();
                State(strike == MeleeStrike.Strike.Kick ? kick : actions, name, clip, strike == MeleeStrike.Strike.Kick ? "KickTime" : "ActionTime");
                strikes.Add(new PlayerBodySettings.StrikeClip { strike = strike, state = name, range = range, impact = impact, turn = turn, rifle = rifle });
            }

            var carry = Layer(controller, "LeftCarry", leftArm);
            State(carry, "Low", FPSKitRiggedSoldier.Mixamo("Rifle/Rifle_Idle_Low_Mirror"));

            // In the order PlayerBody expects: the left arm's carry over the punches, the kick last.
            var layers = controller.layers;
            int Index(string n) => System.Array.FindIndex(layers, l => l.name == n);
            controller.layers = new[] { layers[0], layers[Index("Actions")], layers[Index("LeftCarry")], layers[Index("Kick")] };
            AssetDatabase.SaveAssets();
            return controller;
        }

        static AvatarMask Mask(string name, params AvatarMaskBodyPart[] parts)
        {
            string path = $"{Folder}/{name}.mask";
            if (AssetDatabase.LoadAssetAtPath<AvatarMask>(path) != null) AssetDatabase.DeleteAsset(path);
            var mask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var p in parts) mask.SetHumanoidBodyPartActive(p, true);
            AssetDatabase.CreateAsset(mask, path);
            return mask;
        }

        static AnimatorStateMachine Layer(AnimatorController controller, string name, AvatarMask mask)
        {
            var machine = new AnimatorStateMachine { name = name };
            AssetDatabase.AddObjectToAsset(machine, controller);
            var empty = machine.AddState("Empty");
            empty.writeDefaultValues = false;
            machine.defaultState = empty;
            controller.AddLayer(new AnimatorControllerLayer
            {
                name = name,
                stateMachine = machine,
                avatarMask = mask,
                defaultWeight = 0f,
                blendingMode = AnimatorLayerBlendingMode.Override,
            });
            return machine;
        }

        /// <summary>
        /// The first of the files that exists, as the side whose striking bone does the work: a
        /// clip thrown with the other hand (or foot) is taken mirrored, if its mirror was imported.
        /// </summary>
        static AnimationClip Find(string[] files, HumanBodyBones striker)
        {
            foreach (var file in files)
            {
                string path = $"{FPSKitMixamoImport.Root}Player/{file}.fbx";
                if (AssetDatabase.LoadMainAssetAtPath(path) == null) continue;
                AnimationClip plain = null, mirror = null;
                foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (a is AnimationClip c && !c.name.StartsWith("__")) { if (c.name.EndsWith("_Mirror")) mirror = c; else plain = c; }
                if (plain == null) continue;
                if (mirror == null) return plain;
                return Reach(plain, striker) >= Reach(mirror, striker) ? plain : mirror;
            }
            return null;
        }

        /// <summary>How far ahead of the hips the striking bone ever gets in the clip, metres.</summary>
        static float Reach(AnimationClip clip, HumanBodyBones bone)
        {
            float best = 0f;
            foreach (var d in Ahead(clip, bone)) best = Mathf.Max(best, d);
            return best;
        }

        /// <summary>The striking bone's distance ahead of the hips (and up, for a kick) through the clip, 60 samples.</summary>
        static float[] Ahead(AnimationClip clip, HumanBodyBones bone)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            var go = (GameObject)Object.Instantiate(source);
            try
            {
                var animator = go.GetComponent<Animator>();
                var b = animator.GetBoneTransform(bone);
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                var d = new float[60];
                for (int i = 0; i < d.Length; i++)
                {
                    Pose(animator, clip, clip.length * i / (d.Length - 1));
                    Vector3 v = go.transform.InverseTransformPoint(b.position) - go.transform.InverseTransformPoint(hips.position);
                    d[i] = v.z + (bone == HumanBodyBones.RightFoot || bone == HumanBodyBones.LeftFoot ? v.y * 0.5f : 0f);
                }
                return d;
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// Degrees to the right the striking bone is at the blow, from the hips: of the body's forward,
        /// or of the way the clip's own hips face (across the thighs).
        /// </summary>
        static float Heading(AnimationClip clip, HumanBodyBones bone, float impact, bool relativeToHips)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            var go = (GameObject)Object.Instantiate(source);
            try
            {
                var animator = go.GetComponent<Animator>();
                Pose(animator, clip, clip.length * impact);
                Vector3 L(HumanBodyBones b) => go.transform.InverseTransformPoint(animator.GetBoneTransform(b).position);
                Vector3 v = L(bone) - L(HumanBodyBones.Hips);
                float heading = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                if (!relativeToHips) return heading;
                Vector3 facing = Vector3.Cross(L(HumanBodyBones.RightUpperLeg) - L(HumanBodyBones.LeftUpperLeg), Vector3.up);
                return Mathf.DeltaAngle(Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg, heading);
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// Where the blow lands in the clip (the striking bone furthest out) and the part of the clip
        /// to play: all of it, or for "last" from the trough before the last blow to the end.
        /// </summary>
        static Vector2 Blows(AnimationClip clip, HumanBodyBones striker, bool last, out float impact)
        {
            // A punch's blows are either hand's (a jab-cross is left then right).
            var d = Ahead(clip, striker);
            if (striker == HumanBodyBones.RightHand || striker == HumanBodyBones.LeftHand)
            {
                var other = Ahead(clip, striker == HumanBodyBones.RightHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                for (int i = 0; i < d.Length; i++) d[i] = Mathf.Max(d[i], other[i]);
            }
            int n = d.Length;
            float max = float.MinValue, min = float.MaxValue;
            foreach (var x in d) { max = Mathf.Max(max, x); min = Mathf.Min(min, x); }
            float high = min + (max - min) * 0.8f;

            // The blows: each run of samples above 80% of the reach, at its furthest.
            var peaks = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (d[i] < high) continue;
                int best = i;
                while (i < n && d[i] >= high) { if (d[i] > d[best]) best = i; i++; }
                peaks.Add(best);
            }
            if (peaks.Count == 0) peaks.Add(n / 2);
            int peak = last ? peaks[peaks.Count - 1] : peaks[0];
            impact = peak / (float)(n - 1);

            // From where the blow starts: the last still moment before it (or, for "last", the
            // trough between the last two blows), so a long stance before it is not squeezed in.
            int from = 0;
            if (last && peaks.Count >= 2)
            {
                from = peaks[peaks.Count - 2];
                for (int i = from; i < peak; i++) if (d[i] < d[from]) from = i;
            }
            else
            {
                float low = float.MaxValue;
                for (int i = 0; i < peak; i++) low = Mathf.Min(low, d[i]);
                for (int i = 0; i < peak; i++) if (d[i] <= low + (max - min) * 0.15f) from = i;
            }
            // To where it is back: the first moment after it near its lowest again, and a little more.
            int to = n - 1;
            float after = float.MaxValue;
            for (int i = peak; i < n; i++) after = Mathf.Min(after, d[i]);
            for (int i = peak; i < n; i++) if (d[i] <= after + (max - min) * 0.2f) { to = i; break; }
            to = Mathf.Min(n - 1, to + n / 10);
            return new Vector2(from / (float)(n - 1), to / (float)(n - 1));
        }
    }
}
#endif
