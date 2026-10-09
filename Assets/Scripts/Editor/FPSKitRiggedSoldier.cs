#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;
using Object = UnityEngine.Object;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Builds the enemy soldier from the Survivalist pack's rigged character and saves it
    /// over FPSKit_Generated/Enemy.prefab, which every arena already points at (the GUID is
    /// kept), so no scene is rebuilt.
    ///
    /// What it makes:
    /// - The body: head, arms, shirt, trousers, boots and vest, plus a cap, a pack and the
    ///   like as Kit_ pieces the archetype switches on (EnemyArchetype.Dress). Three outfits
    ///   from the pack's skins; one is picked per soldier. URP Lit copies of the pack's
    ///   materials, matte, so the archetype's colour can tint them.
    /// - Twelve hitboxes on the real bones, each a <see cref="BodyPart"/> reading its
    ///   multiplier from one <see cref="BodyDamageProfile"/>; a ragdoll on the same bones,
    ///   with joints that bend the ways a body bends.
    /// - An Animator with idle, walk and run by speed, and <see cref="EnemyBody"/> on top
    ///   for the aim, the rifle in both hands, the hit reactions, the throw and the get-up.
    /// - The AI, health, voice, gore and health bar settings copied from whatever enemy
    ///   prefab was there before, so the tuning carries over.
    ///
    ///   FPSKit > Enemy > Build Rigged Soldier      (or FPSKitBatch.BuildRiggedSoldier)
    /// </summary>
    public static class FPSKitRiggedSoldier
    {
        const string PrefabPath = "Assets/FPSKit_Generated/Enemy.prefab";
        const string Folder = "Assets/FPSKit_Generated/Soldier/Rig";
        const string Source = "Assets/Survivalist/Prefab/Survivalist (1).prefab";
        const string ModelFile = "Assets/Survivalist/Basemesh/SK_Military_Survivalist.fbx";
        const string PackMaterials = "Assets/Survivalist/Materials URP";
        const string Clips = "Assets/Survivalist/StarterAssets/ThirdPersonController/Character/Animations";
        const string GunPrefab = "Assets/FPSKit_Generated/ViewModels/VM_AK74.prefab";
        public const string ProfilePath = "Assets/FPSKit_Generated/Soldier/BodyDamage.asset";

        /// <summary>How much bigger than the pack's 1.8 m man the enemy is: his pick, enemies 10% bigger.</summary>
        const float Scale = 1.1f;

        /// <summary>
        /// The garments, what they are called on the enemy, and which pack skin each of the
        /// three outfits wears (A: olive, B: tan, C: grey and black). Kit_ pieces are worn
        /// by role; Outfit_ pieces always.
        /// </summary>
        static readonly (string mesh, string name, string[] skins)[] Wardrobe =
        {
            ("SK_Military_Head1", "Outfit_Head", null),
            ("SK_Military_Male_Arms1", "Outfit_Hands", null),
            ("SK_Military_Shirt3", "Outfit_Shirt", new[] { "Shirt3_Skin2", "Shirt3_Skin3", "Shirt3_Skin1" }),
            ("SK_Military_Pants2", "Outfit_Pants", new[] { "Pants2_Skin2", "Pants2_Skin3", "Pants2_Skin2" }),
            ("SK_Military_Boots3", "Outfit_Boots", new[] { "Boots3_Skin1", "Boots3_Skin2", "Boots3_Skin3" }),
            ("SK_Military_Vest1", "Outfit_Vest", new[] { "Vest1_Skin2", "Vest1_Skin3", "Vest1_Skin1" }),
            ("SK_Military_Cap1", "Kit_Human_Cap", new[] { "Cap1_Skin2", "Cap1_Skin3", "Cap1_Skin1" }),
            ("SK_Military_Backpack1", "Kit_Elite_Backpack", new[] { "Backpack1_Skin2", "Backpack1_Skin3", "Backpack1_Skin1" }),
        };

        [MenuItem("FPSKit/Enemy/Build Rigged Soldier", false, 44)]
        public static void Build()
        {
            FPSKitSceneBuilder.EnsureProjectTagsAndLayers();
            EnsureFolder(Folder);

            // Read/Write on for the body's meshes: cutting a severed limb out of them reads
            // their bone weights at runtime (EnemyBrutality), which a build only allows on a
            // readable mesh. The one change made to the pack's import settings.
            if (AssetImporter.GetAtPath(ModelFile) is ModelImporter importer && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            if (source == null) { Debug.LogError("[FPSKit] Rigged soldier: " + Source + " is missing"); return; }

            var old = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            int enemyLayer = LayerMask.NameToLayer("Enemy");

            var root = new GameObject("Enemy");
            try
            {
                root.tag = "Enemy";
                root.layer = enemyLayer;
                root.transform.localScale = Vector3.one * Scale;

                // ---- the body ----
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                model.name = "Body";
                model.transform.SetParent(root.transform, false);

                var animator = model.GetComponent<Animator>();
                animator.runtimeAnimatorController = LocomotionController();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                var garments = Dress(model);

                // ---- health and the parts ----
                var health = root.AddComponent<Health>();
                var oldHealth = old != null ? old.GetComponent<Health>() : null;
                if (oldHealth != null) EditorUtility.CopySerialized(oldHealth, health);
                else health.maxHealth = 100f;
                health.destroyOnDeath = false;

                var profile = Profile();
                int hitboxes = Hitboxes(animator, health, profile, enemyLayer, out Transform head);
                Ragdoll(animator);

                // ---- movement ----
                var agent = root.AddComponent<NavMeshAgent>();
                var oldAgent = old != null ? old.GetComponent<NavMeshAgent>() : null;
                if (oldAgent != null) EditorUtility.CopySerialized(oldAgent, agent);
                agent.stoppingDistance = 0.8f;       // the fixed brake; see CLAUDE.md
                agent.radius = 0.4f;
                agent.height = 2f;

                var eyes = new GameObject("Eyes").transform;
                eyes.SetParent(root.transform, false);
                eyes.position = head.position + root.transform.forward * 0.09f;

                // ---- the rifle ----
                var gun = Gun(root.transform, head, out Transform rightGrip, out Transform leftGrip, out Transform muzzle);

                // ---- the brain, copied from the enemy that was here so its tuning carries ----
                var ai = root.AddComponent<EnemyAI>();
                var oldAi = old != null ? old.GetComponent<EnemyAI>() : null;
                if (oldAi != null) EditorUtility.CopySerialized(oldAi, ai);
                ai.eyes = eyes;
                ai.animator = animator;
                ai.muzzlePoint = muzzle;
                ai.speedParameter = "Speed";
                ai.attackTrigger = "Attack";
                ai.deathTrigger = "Die";
                ai.sightBlockers = 1 << LayerMask.NameToLayer("Environment");
                ai.rangedHitMask = ~(1 << enemyLayer);

                var audio = root.AddComponent<AudioSource>();
                var oldAudio = old != null ? old.GetComponent<AudioSource>() : null;
                if (oldAudio != null) EditorUtility.CopySerialized(oldAudio, audio);
                else { audio.playOnAwake = false; audio.spatialBlend = 1f; audio.maxDistance = 30f; }

                root.AddComponent<EnemyWounds>();
                var gore = root.AddComponent<EnemyGore>();
                gore.library = FPSKitGore.GetOrCreateLibrary();

                var voiceObject = new GameObject("Voice");
                voiceObject.transform.SetParent(root.transform, false);
                voiceObject.transform.position = eyes.position;
                var voiceSource = voiceObject.AddComponent<AudioSource>();
                voiceSource.playOnAwake = false;
                voiceSource.spatialBlend = 1f;
                voiceSource.minDistance = 2.5f;
                voiceSource.maxDistance = 45f;
                voiceSource.rolloffMode = AudioRolloffMode.Linear;
                var voice = root.AddComponent<EnemyVoice>();
                voice.bank = FPSKitGore.GetOrCreateVoiceBank();
                voice.source = voiceSource;

                // No health bar: it is read by its body (no health bars anywhere).

                var ragdoll = root.AddComponent<RagdollController>();
                ragdoll.animator = animator;
                ragdoll.mainCollider = null;     // the bones are the body; nothing on the root to soak a round

                root.AddComponent<EnemyBrutality>();
                root.AddComponent<EnemyBrain>();

                var body = root.AddComponent<EnemyBody>();
                root.AddComponent<EnemyMotor>();
                root.AddComponent<EnemyRifle>();
                model.AddComponent<EnemyRootMotion>();
                body.animator = animator;
                body.gun = gun;
                body.rightGrip = rightGrip;
                body.leftGrip = leftGrip;
                body.garments = garments;

                // ---- the rifle in the right hand, the left hand on it, the aim ----
                if (gun != null) HoldRifle(animator, root.transform, gun, rightGrip, leftGrip, body);
                model.AddComponent<EnemyFootIK>();

                // Held at the shoulder: the rear sight a hand in front of the eye and a little
                // below it, the shoulder it pivots on where the arm meets the body.
                var rUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                Vector3 headLocal = root.transform.InverseTransformPoint(head.position);
                Vector3 shoulderLocal = root.transform.InverseTransformPoint(rUpper.position);
                body.shoulder = new Vector3(shoulderLocal.x * 0.6f, shoulderLocal.y, shoulderLocal.z + 0.05f);
                body.readyPosition = new Vector3(0.05f, headLocal.y - 0.07f, headLocal.z + 0.24f);

                Layer(root, enemyLayer);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[FPSKit] Rigged soldier built: {hitboxes} hitboxes, {garments.Length} garments, saved over {PrefabPath}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        static void Layer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) Layer(c.gameObject, layer);
        }

        // ==================================================================
        // Clothes
        // ==================================================================

        static EnemyBody.Garment[] Dress(GameObject model)
        {
            // The first-person arms belong to the player's view model, not to anyone seen.
            var fps = model.transform.Find("FPS_HANDS");
            if (fps != null) Object.DestroyImmediate(fps.gameObject);

            var list = new List<EnemyBody.Garment>();
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int index = System.Array.FindIndex(Wardrobe, w => w.mesh == smr.name);
                if (index < 0) { Object.DestroyImmediate(smr.gameObject); continue; }

                var entry = Wardrobe[index];
                smr.gameObject.SetActive(true);
                smr.name = entry.name;
                smr.updateWhenOffscreen = false;
                smr.quality = SkinQuality.Bone2;

                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = Lit(mats[i] != null ? mats[i].name : null);
                if (entry.skins != null) mats[0] = Lit(entry.skins[0]);
                smr.sharedMaterials = mats;

                if (entry.skins == null) continue;
                var skins = new Material[entry.skins.Length];
                for (int i = 0; i < skins.Length; i++) skins[i] = Lit(entry.skins[i]);
                list.Add(new EnemyBody.Garment { renderer = smr, skins = skins });
            }

            return list.ToArray();
        }

        /// <summary>
        /// A URP Lit copy of a pack material: its colour and normal maps, matte, white base
        /// colour so the archetype's tint multiplies the cloth, and emission enabled (black)
        /// so the wind-up flash can light it through a property block.
        /// </summary>
        static Material Lit(string packName)
        {
            if (string.IsNullOrEmpty(packName)) return null;

            string path = $"{Folder}/{packName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }

            var pack = AssetDatabase.LoadAssetAtPath<Material>($"{PackMaterials}/URP_{packName}.mat");
            if (pack != null)
            {
                if (pack.HasProperty("_MainTex")) mat.SetTexture("_BaseMap", pack.GetTexture("_MainTex"));
                if (pack.HasProperty("_BumpMap") && pack.GetTexture("_BumpMap") != null)
                {
                    mat.SetTexture("_BumpMap", pack.GetTexture("_BumpMap"));
                    mat.EnableKeyword("_NORMALMAP");
                }
            }

            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", packName.Contains("Eyes") ? 0.6f : 0.18f);
            mat.SetFloat("_Metallic", 0f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ==================================================================
        // Moving
        // ==================================================================

        /// <summary>
        /// The soldier's movement with a rifle, moved by the clips' own root motion (EnemyMotor
        /// lays it onto the navmesh and the agent follows). The base layer's states, all picked
        /// by EnemyMotor:
        ///
        /// - **Idle**: standing (or crouched) still: alert with the rifle up, or one of the calm
        ///   idles by IdleVariant, each at its own pace.
        /// - **Move**: stance (Crouch 0 stand, 1 crouch) over an eight-way freeform blend on the
        ///   body's own velocity (MoveX, MoveZ, metres a second at the clips' size). Each clip
        ///   sits at the velocity it was made at, so walk, run and sprint blend by speed and the
        ///   feet match the ground at any of them.
        /// - **Pistol**: the same for a one-handed pistol; PistolLeft is its mirror, for a body
        ///   whose right arm is gone.
        /// - **TurnLeft/TurnRight** (and crouched): ninety degrees on the spot, the root's own turn.
        /// - **RunStart/RunStop**: into a run from standing, and pulling up out of one.
        ///
        /// Rifle clips from the Pro Rifle Pack (Assets/Animations/Mixamo/Rifle). The Speed
        /// parameter is kept for EnemyAI, which still writes it.
        /// </summary>
        static RuntimeAnimatorController LocomotionController()
        {
            string path = Folder + "/Soldier.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            foreach (var name in new[] { "MoveX", "MoveZ", "Crouch", "IdleVariant", "Aim" })
                controller.AddParameter(name, AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter { name = "MoveRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

            BlendTree Tree(string name, BlendTreeType type, string x, string y = null)
            {
                var t = new BlendTree { name = name, blendType = type, blendParameter = x, useAutomaticThresholds = false };
                if (y != null) t.blendParameterY = y;
                AssetDatabase.AddObjectToAsset(t, controller);
                return t;
            }

            // A still clip at the centre of a movement blend, played fast enough to last one
            // stride (cycle seconds), so it never drags the stride out.
            void Centre(BlendTree t, AnimationClip clip, float cycle)
            {
                if (clip == null) return;
                t.AddChild(clip, Vector2.zero);
                var kids = t.children;
                kids[kids.Length - 1].timeScale = Mathf.Max(1f, clip.length / Mathf.Max(0.1f, cycle));
                t.children = kids;
            }

            // Each clip at the velocity it moves at (measured, x sideways, z forward).
            void Ring(BlendTree t, string gait)
            {
                foreach (var dir in new[] { "F", "FR", "R", "BR", "B", "BL", "L", "FL" })
                {
                    var clip = Mixamo($"Rifle/Move_{gait}_{dir}");
                    if (clip == null) continue;
                    var v = clip.averageSpeed;
                    t.AddChild(clip, new Vector2(v.x, v.z));
                }
            }

            // The idles in the middle: 0 alert, rifle up; 1 low and calm; 2-4 the calm variations
            // (unarmed clips: the Carry layer puts the rifle in their hands).
            var idles = Tree("Idles", BlendTreeType.Simple1D, "IdleVariant");
            idles.AddChild(Mixamo("Rifle/Rifle_Idle_Aim"), 0f);
            idles.AddChild(Mixamo("Rifle/Rifle_Idle_Low"), 1f);
            idles.AddChild(Mixamo("Idle/Idle_Breathing"), 2f);
            idles.AddChild(Mixamo("Idle/Idle_WeightShift"), 3f);
            idles.AddChild(Mixamo("Idle/Idle_LookAround"), 4f);

            // A blend tree keeps all its clips on one cycle, as long as their weighted average:
            // a nine-second idle at the centre of the movement blend stretched the walk to a
            // third of its speed. So standing still is its own state (Idle, with the variations
            // at their own pace), and the centre of the movement blend is the short aiming idle,
            // retimed to a stride, only ever seen in passing between standing and walking.
            var stand = Tree("Stand", BlendTreeType.FreeformDirectional2D, "MoveX", "MoveZ");
            Centre(stand, Mixamo("Rifle/Rifle_Idle_Aim"), 1f);
            Ring(stand, "Walk");
            Ring(stand, "Run");
            Ring(stand, "Sprint");

            var crouchIdle = Tree("CrouchIdles", BlendTreeType.Simple1D, "Aim");
            crouchIdle.AddChild(Mixamo("Rifle/Rifle_Idle_Crouch"), 0f);
            crouchIdle.AddChild(Mixamo("Rifle/Rifle_Idle_CrouchAim"), 1f);
            var crouch = Tree("Crouch", BlendTreeType.FreeformDirectional2D, "MoveX", "MoveZ");
            Centre(crouch, Mixamo("Rifle/Rifle_Idle_CrouchAim"), 1f);
            Ring(crouch, "Crouch");

            var stance = Tree("Stance", BlendTreeType.Simple1D, "Crouch");
            stance.AddChild(stand, 0f);
            stance.AddChild(crouch, 1f);

            var still = Tree("Still", BlendTreeType.Simple1D, "Crouch");
            still.AddChild(idles, 0f);
            still.AddChild(crouchIdle, 1f);

            BlendTree Pistol(string name, string suffix)
            {
                var t = Tree(name, BlendTreeType.FreeformDirectional2D, "MoveX", "MoveZ");
                void Add(string clip)
                {
                    var c = Mixamo("Pistol/" + clip + suffix);
                    if (c == null) return;
                    var v = c.averageSpeed;
                    t.AddChild(c, new Vector2(v.x, v.z));
                }
                Centre(t, Mixamo("Pistol/Pistol_Idle" + suffix), 0.8f);
                Add("Pistol_Walk"); Add("Pistol_Run"); Add("Pistol_Walk_Back");
                // Mirrored, left and right swap over: a mirrored strafe left goes right.
                Add("Pistol_Strafe_Left"); Add("Pistol_Strafe_Right");
                return t;
            }

            var layer = controller.layers[0];
            layer.iKPass = true;    // the feet on the ground (EnemyFootIK)
            var sm = layer.stateMachine;

            AnimatorState State(string name, Motion motion, float speed = 1f)
            {
                var st = sm.AddState(name);
                st.motion = motion;
                st.speed = speed;
                st.writeDefaultValues = false;
                return st;
            }

            sm.defaultState = State("Idle", still);
            var move = State("Move", stance);
            move.speedParameter = "MoveRate";
            move.speedParameterActive = true;
            State("Pistol", Pistol("PistolMove", ""));
            State("PistolLeft", Pistol("PistolMoveLeft", "_Mirror"));
            State("PistolIdle", Mixamo("Pistol/Pistol_Idle"));
            State("PistolIdleLeft", Mixamo("Pistol/Pistol_Idle_Mirror"));
            State("TurnLeft", Mixamo("Rifle/Rifle_Turn90_Left"));
            State("TurnRight", Mixamo("Rifle/Rifle_Turn90_Right"));
            State("CrouchTurnLeft", Mixamo("Rifle/Rifle_CrouchTurn90_Left"));
            State("CrouchTurnRight", Mixamo("Rifle/Rifle_CrouchTurn90_Right"));
            State("RunStart", Mixamo("Transitions/Rifle_Run_Start_B"));
            State("RunStop", Mixamo("Transitions/Rifle_Run_Stop"));

            // The fighting moves, over everything: the rage after being kicked down and the
            // blows it then throws. From the EEJANAI fighter set (Humanoid, so they play on
            // this body). The layer's weight is EnemyBody's to raise and drop around each one,
            // so between moves it is not there at all.
            controller.AddParameter("Rage", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Strike", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("StrikeB", AnimatorControllerParameterType.Trigger);

            var actionMachine = new AnimatorStateMachine { name = "Action" };
            AssetDatabase.AddObjectToAsset(actionMachine, controller);
            var idle = actionMachine.AddState("Idle");
            idle.motion = Clip("Stand--Idle");
            actionMachine.defaultState = idle;

            void Move(string name, string clip, string trigger, float speed)
            {
                var st = actionMachine.AddState(name);
                st.motion = Fighter(clip);
                st.speed = speed;
                var into = actionMachine.AddAnyStateTransition(st);
                into.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                into.duration = 0.08f;
                into.canTransitionToSelf = false;
                var back = st.AddTransition(idle);
                back.hasExitTime = true;
                back.exitTime = 0.92f;
                back.duration = 0.1f;
            }

            Move("Rage", "charge fist", "Rage", 1.5f);
            Move("Strike", "elbow chop", "Strike", 1.15f);
            Move("StrikeB", "knee strike", "StrikeB", 1.1f);

            // A stumble on the move when pain spikes: over the legs as well, so it plays out
            // whole and then lets go.
            controller.AddParameter("Stumble", AnimatorControllerParameterType.Trigger);
            {
                var st = actionMachine.AddState("Stumble");
                st.motion = Mixamo("Injured/Stumble_Jog");
                st.speed = 1.1f;
                var into = actionMachine.AddAnyStateTransition(st);
                into.AddCondition(AnimatorConditionMode.If, 0f, "Stumble");
                into.duration = 0.12f;
                into.canTransitionToSelf = false;
                var back = st.AddTransition(idle);
                back.hasExitTime = true;
                back.exitTime = 0.85f;
                back.duration = 0.2f;
            }

            var action = new AnimatorControllerLayer
            {
                name = "Action",
                stateMachine = actionMachine,
                defaultWeight = 0f,
                blendingMode = AnimatorLayerBlendingMode.Override,
            };
            controller.layers = new[] { layer, Injured(controller), Carry(controller), action };

            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>
        /// The hurt body, over the walk: the limp on either leg, the crawl face down or face up,
        /// and the get-ups (Mixamo clips, Assets/Animations/Mixamo). EnemyBody raises the
        /// layer's weight and picks the state; EnemyMotor drives "Pace" (-1 back, 0 still,
        /// 1 walk, 2 run) and moves the body by the clip's own root motion, so the feet and
        /// the ground agree. States are named for EnemyBody (EnemyBody.GetUps).
        /// </summary>
        static AnimatorControllerLayer Injured(AnimatorController controller)
        {
            controller.AddParameter("Pace", AnimatorControllerParameterType.Float);
            // How fast a get-up plays: quicker when it is not hurt (EnemyBody.quickRiseSpeed).
            controller.AddParameter(new AnimatorControllerParameter { name = "RiseSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

            var machine = new AnimatorStateMachine { name = "Injured" };
            AssetDatabase.AddObjectToAsset(machine, controller);
            var empty = machine.AddState("Empty");
            machine.defaultState = empty;

            BlendTree Tree(string name, params (string clip, float at)[] kids)
            {
                var tree = new BlendTree
                {
                    name = name,
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = "Pace",
                    useAutomaticThresholds = false,
                };
                AssetDatabase.AddObjectToAsset(tree, controller);
                foreach (var (clip, at) in kids) tree.AddChild(Mixamo(clip), at);
                return tree;
            }

            void State(string name, Motion motion, float speed = 1f)
            {
                var st = machine.AddState(name);
                st.motion = motion;
                st.speed = speed;
                st.writeDefaultValues = false;
            }

            // The Mixamo limps favour the right leg (Limp_Back the left); the mirrors are the
            // other side. Measured: the stiff knee and the foot that carries less weight.
            State("LimpRight", Tree("LimpRight", ("Injured/Limp_Back_Mirror", -1f), ("Injured/Injured_HoldLeg_Idle", 0f),
                                    ("Injured/Limp_Walk", 1f), ("Injured/Limp_Run", 2f)));
            State("LimpLeft", Tree("LimpLeft", ("Injured/Limp_Back", -1f), ("Injured/Injured_HoldLeg_Idle_Mirror", 0f),
                                   ("Injured/Limp_Walk_Mirror", 1f), ("Injured/Limp_Run_Mirror", 2f)));
            // Face down: dragging itself on its arms, or flat with the rifle up. The drag is
            // 0.1 m/s as made; played faster so cover is reachable in a fight.
            State("CrawlDown", Tree("CrawlDown", ("Crawl/Crawl_Back_Stomach", -1f), ("Crawl/Prone_Idle_Rifle", 0f),
                                    ("Crawl/Crawl_Drag", 1f)), 1.5f);
            // Face up: propped on its hands and pushing itself away, facing the threat.
            State("CrawlUp", Tree("CrawlUp", ("Deaths/Death_OnGround", 0f), ("Crawl/Crawl_Back_Fear", 1f)));

            foreach (var g in EnemyBody.GetUps)
            {
                State(g.state, Mixamo("GetUp/" + g.state));
                var st = System.Array.Find(machine.states, c => c.state.name == g.state).state;
                st.speedParameter = "RiseSpeed";
                st.speedParameterActive = true;
            }

            return new AnimatorControllerLayer
            {
                name = "Injured",
                stateMachine = machine,
                defaultWeight = 0f,
                blendingMode = AnimatorLayerBlendingMode.Override,
            };
        }

        /// <summary>
        /// The arms only, holding the rifle, over clips made without one: the limp, the calm idle
        /// variations, a calm walk carried low. "Low" is the rifle at low ready, "Aim" up at the
        /// shoulder; EnemyBody sets the weight and the state. Arms and fingers only, so the walk's
        /// own torso and legs come through.
        /// </summary>
        static AnimatorControllerLayer Carry(AnimatorController controller)
        {
            var maskPath = Folder + "/Arms.mask";
            if (AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath) != null) AssetDatabase.DeleteAsset(maskPath);
            var mask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var part in new[] { AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                                         AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers,
                                         AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK })
                mask.SetHumanoidBodyPartActive(part, true);
            AssetDatabase.CreateAsset(mask, maskPath);

            var machine = new AnimatorStateMachine { name = "Carry" };
            AssetDatabase.AddObjectToAsset(machine, controller);
            AnimatorState Add(string name, Motion motion)
            {
                var st = machine.AddState(name);
                st.motion = motion;
                st.writeDefaultValues = false;
                return st;
            }
            machine.defaultState = Add("Low", Mixamo("Rifle/Rifle_Idle_Low"));
            Add("Aim", Mixamo("Rifle/Rifle_Idle_Aim"));
            Add("Pistol", Mixamo("Pistol/Pistol_Idle"));
            Add("PistolLeft", Mixamo("Pistol/Pistol_Idle_Mirror"));

            return new AnimatorControllerLayer
            {
                name = "Carry",
                stateMachine = machine,
                avatarMask = mask,
                defaultWeight = 0f,
                blendingMode = AnimatorLayerBlendingMode.Override,
            };
        }

        /// <summary>A Mixamo clip by its path under Assets/Animations/Mixamo, "_Mirror" for the mirrored copy.</summary>
        static AnimationClip Mixamo(string path)
        {
            string name = System.IO.Path.GetFileName(path);
            string file = path.EndsWith("_Mirror") ? path.Substring(0, path.Length - "_Mirror".Length) : path;
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath($"{FPSKitMixamoImport.Root}{file}.fbx"))
                if (a is AnimationClip c && c.name == name) return c;
            Debug.LogWarning("[FPSKit] Rigged soldier: no Mixamo clip " + path);
            return null;
        }

        static AnimationClip Fighter(string name)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/EEJANAI_Team/FreeFighterAnimations/Animations/{name}.anim");
            if (clip == null) Debug.LogWarning("[FPSKit] Rigged soldier: no fighter clip " + name);
            return clip;
        }

        static AnimationClip Clip(string file)
        {
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath($"{Clips}/{file}.anim.fbx"))
                if (a is AnimationClip c && !c.name.StartsWith("__")) return c;
            Debug.LogWarning("[FPSKit] Rigged soldier: no clip in " + file);
            return null;
        }

        // ==================================================================
        // Parts and the ragdoll
        // ==================================================================

        static BodyDamageProfile Profile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<BodyDamageProfile>(ProfilePath);
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<BodyDamageProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            return profile;
        }

        /// <summary>Bone, part, collider radius (metres, at the pack's scale), and mass for the ragdoll.</summary>
        static readonly (HumanBodyBones bone, BodyPart part, float radius, float mass)[] Parts =
        {
            (HumanBodyBones.Hips, BodyPart.LowerTorso, 0.15f, 11f),
            (HumanBodyBones.Chest, BodyPart.LowerTorso, 0.14f, 8f),
            (HumanBodyBones.UpperChest, BodyPart.Torso, 0.16f, 10f),
            (HumanBodyBones.Neck, BodyPart.Neck, 0.06f, 1.5f),
            (HumanBodyBones.Head, BodyPart.Head, 0.11f, 4.5f),
            (HumanBodyBones.LeftUpperArm, BodyPart.LeftArm, 0.06f, 2.5f),
            (HumanBodyBones.RightUpperArm, BodyPart.RightArm, 0.06f, 2.5f),
            (HumanBodyBones.LeftLowerArm, BodyPart.LeftForearm, 0.05f, 1.6f),
            (HumanBodyBones.RightLowerArm, BodyPart.RightForearm, 0.05f, 1.6f),
            (HumanBodyBones.LeftUpperLeg, BodyPart.LeftLeg, 0.09f, 7f),
            (HumanBodyBones.RightUpperLeg, BodyPart.RightLeg, 0.09f, 7f),
            (HumanBodyBones.LeftLowerLeg, BodyPart.LeftShin, 0.065f, 4f),
            (HumanBodyBones.RightLowerLeg, BodyPart.RightShin, 0.065f, 4f),
        };

        /// <summary>
        /// A collider on each bone that matters, laid along the bone to the next joint, with a
        /// Hitbox naming the part. The head is a sphere and the only headshot.
        /// </summary>
        static int Hitboxes(Animator animator, Health owner, BodyDamageProfile profile, int layer, out Transform head)
        {
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            int built = 0;

            foreach (var (boneId, part, radius, _) in Parts)
            {
                var bone = animator.GetBoneTransform(boneId);
                if (bone == null) continue;

                Collider collider;
                if (boneId == HumanBodyBones.Head)
                {
                    var sphere = bone.gameObject.AddComponent<SphereCollider>();
                    sphere.radius = radius;
                    sphere.center = bone.InverseTransformVector(Vector3.up * 0.09f);
                    collider = sphere;
                }
                else
                {
                    Vector3 end = EndOf(animator, boneId, bone);
                    Vector3 along = bone.InverseTransformPoint(end);
                    var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
                    capsule.direction = Dominant(along);
                    capsule.radius = radius;
                    capsule.height = Mathf.Max(radius * 2f, Mathf.Abs(along[capsule.direction]) + radius);
                    capsule.center = along * 0.5f;
                    collider = capsule;
                }

                collider.gameObject.layer = layer;
                var hitbox = bone.gameObject.AddComponent<Hitbox>();
                hitbox.owner = owner;
                hitbox.part = part;
                hitbox.isHeadshot = part == BodyPart.Head;
                hitbox.profile = profile;
                hitbox.damageMultiplier = profile.For(part);
                built++;
            }

            return built;
        }

        /// <summary>Where a bone's part ends: the next joint down the limb, or a guess for the ends.</summary>
        static Vector3 EndOf(Animator a, HumanBodyBones b, Transform bone)
        {
            HumanBodyBones next = b switch
            {
                HumanBodyBones.Hips => HumanBodyBones.Chest,
                HumanBodyBones.Chest => HumanBodyBones.UpperChest,
                HumanBodyBones.UpperChest => HumanBodyBones.Neck,
                HumanBodyBones.Neck => HumanBodyBones.Head,
                HumanBodyBones.LeftUpperArm => HumanBodyBones.LeftLowerArm,
                HumanBodyBones.RightUpperArm => HumanBodyBones.RightLowerArm,
                HumanBodyBones.LeftLowerArm => HumanBodyBones.LeftHand,
                HumanBodyBones.RightLowerArm => HumanBodyBones.RightHand,
                HumanBodyBones.LeftUpperLeg => HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.RightUpperLeg => HumanBodyBones.RightLowerLeg,
                HumanBodyBones.LeftLowerLeg => HumanBodyBones.LeftFoot,
                HumanBodyBones.RightLowerLeg => HumanBodyBones.RightFoot,
                _ => HumanBodyBones.LastBone,
            };

            var t = next != HumanBodyBones.LastBone ? a.GetBoneTransform(next) : null;
            return t != null ? t.position : bone.position + Vector3.up * 0.15f;
        }

        static int Dominant(Vector3 v)
        {
            v = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            return v.x >= v.y && v.x >= v.z ? 0 : v.y >= v.z ? 1 : 2;
        }

        /// <summary>
        /// A rigidbody on each hitbox bone, joined to the nearest one above it. Kinematic
        /// until the RagdollController lets go. The limits are a body's, roughly: the spine
        /// and neck bend a little, shoulders and hips a lot, elbows and knees one way.
        /// </summary>
        static void Ragdoll(Animator animator)
        {
            var bodies = new Dictionary<Transform, Rigidbody>();
            foreach (var (boneId, _, _, mass) in Parts)
            {
                var bone = animator.GetBoneTransform(boneId);
                if (bone == null || bone.GetComponent<Collider>() == null) continue;

                var body = bone.gameObject.AddComponent<Rigidbody>();
                body.mass = mass;
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.linearDamping = 0.05f;
                body.angularDamping = 0.6f;
                bodies[bone] = body;
            }

            foreach (var (boneId, _, _, _) in Parts)
            {
                var bone = animator.GetBoneTransform(boneId);
                if (bone == null || !bodies.ContainsKey(bone)) continue;

                Rigidbody parent = null;
                for (var t = bone.parent; t != null && parent == null; t = t.parent)
                    bodies.TryGetValue(t, out parent);
                if (parent == null) continue;

                var joint = bone.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = parent;
                joint.enablePreprocessing = false;
                joint.enableProjection = true;

                (float low, float high, float swing1, float swing2) limits = boneId switch
                {
                    HumanBodyBones.LeftLowerArm or HumanBodyBones.RightLowerArm => (-5f, 120f, 10f, 10f),
                    HumanBodyBones.LeftLowerLeg or HumanBodyBones.RightLowerLeg => (-120f, 5f, 8f, 8f),
                    HumanBodyBones.LeftUpperArm or HumanBodyBones.RightUpperArm => (-60f, 70f, 70f, 50f),
                    HumanBodyBones.LeftUpperLeg or HumanBodyBones.RightUpperLeg => (-70f, 30f, 35f, 25f),
                    HumanBodyBones.Head or HumanBodyBones.Neck => (-30f, 30f, 25f, 25f),
                    _ => (-15f, 15f, 15f, 15f),
                };

                joint.lowTwistLimit = new SoftJointLimit { limit = limits.low };
                joint.highTwistLimit = new SoftJointLimit { limit = limits.high };
                joint.swing1Limit = new SoftJointLimit { limit = limits.swing1 };
                joint.swing2Limit = new SoftJointLimit { limit = limits.swing2 };

                // The twist axis across the joint, for the hinge-like ones: the side axis, so
                // the elbow and knee fold forward-back rather than twisting.
                Vector3 side = bone.InverseTransformDirection(animator.transform.right);
                Vector3 alongBone = bone.InverseTransformDirection((EndOf(animator, boneId, bone) - bone.position).normalized);
                joint.axis = side;
                joint.swingAxis = Vector3.Cross(alongBone, side);
            }
        }

        // ==================================================================
        // The rifle
        // ==================================================================

        /// <summary>
        /// The AK from the view-model set, which already looks down +Z from its rear sight
        /// with its hand sockets and muzzle placed; its own spec stripped, its parts tagged
        /// as metal so the archetype never paints it and the damage flash never lights it.
        /// </summary>
        /// <summary>
        /// The rifle goes into the right hand: a socket under the hand bone where the rifle sits
        /// in the "rifle idle, aiming" clip -- the hand at the grip, the bore along the line
        /// from the right hand to the left -- and a "sway" child the rifle hangs from, which
        /// EnemyBody springs a little behind the hand so it has weight. The left hand is reached
        /// onto the handguard by a two-bone IK constraint; the head, chest and spine turn to
        /// their look targets by aim constraints (Animation Rigging), the head furthest.
        /// The pose is sampled on this body and put back.
        /// </summary>
        static void HoldRifle(Animator animator, Transform root, Transform gun, Transform rightGrip, Transform leftGrip, EnemyBody body)
        {
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
            var rHand = B(HumanBodyBones.RightHand);
            var lHand = B(HumanBodyBones.LeftHand);
            var chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest);
            var aim = Mixamo("Rifle/Rifle_Idle_Aim");
            if (rHand == null || lHand == null || chest == null || aim == null) return;

            var bones = animator.GetComponentsInChildren<Transform>();
            var pos = new Vector3[bones.Length];
            var rot = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++) { pos[i] = bones[i].localPosition; rot[i] = bones[i].localRotation; }
            Vector3 modelPos = animator.transform.position;
            Quaternion modelRot = animator.transform.rotation;
            try
            {
                aim.SampleAnimation(animator.gameObject, 0.5f);
                animator.transform.SetPositionAndRotation(modelPos, modelRot);

                // Aiming, the rifle points straight ahead and level, its grip in the right hand.
                // (The line between the hands is not the bore: in the bladed stance it runs
                // well across the body.)
                Vector3 R = rHand.position;
                gun.rotation = Quaternion.LookRotation(root.forward, root.up);
                gun.position += R - rightGrip.position;

                var socket = new GameObject("RifleSocket").transform;
                socket.SetParent(rHand, false);
                socket.SetPositionAndRotation(gun.position, gun.rotation);
                var sway = new GameObject("RifleSway").transform;
                sway.SetParent(socket, false);
                gun.SetParent(sway, true);

                // The left hand's target: on the handguard, turned the way that hand's frame
                // wants to hold it (LimbIK.HandFrame, as the old grip did).
                var frame = LimbIK.HandFrame.Measure(lHand, B(HumanBodyBones.LeftMiddleProximal), B(HumanBodyBones.LeftIndexProximal),
                                                     B(HumanBodyBones.LeftLittleProximal), right: false);
                var target = new GameObject("LeftHandTarget").transform;
                target.SetParent(leftGrip, false);
                if (frame.valid) target.rotation = frame.World(leftGrip.forward, leftGrip.up);

                var hint = new GameObject("LeftElbowHint").transform;
                hint.SetParent(chest, false);
                var lElbow = B(HumanBodyBones.LeftLowerArm);
                hint.position = lElbow.position - root.right * 0.25f + Vector3.down * 0.25f;

                // The aim is EnemyBody's additive turn down the spine, head first: an aim
                // constraint sets a bone's rotation outright and straightens the bladed rifle
                // stance the clips are made in. The rig does the left hand.
                var rigs = new GameObject("Rigs").transform;
                rigs.SetParent(animator.transform, false);

                var handRig = new GameObject("HandRig").AddComponent<Rig>();
                handRig.transform.SetParent(rigs, false);
                var ik = new GameObject("LeftHandIK").AddComponent<TwoBoneIKConstraint>();
                ik.transform.SetParent(handRig.transform, false);
                var d = ik.data;
                d.root = B(HumanBodyBones.LeftUpperArm);
                d.mid = B(HumanBodyBones.LeftLowerArm);
                d.tip = lHand;
                d.target = target;
                d.hint = hint;
                d.targetPositionWeight = 1f;
                d.targetRotationWeight = 1f;
                d.hintWeight = 1f;
                ik.data = d;

                var builder = animator.gameObject.AddComponent<RigBuilder>();
                builder.layers.Add(new RigLayer(handRig, true));

                body.rifleSocket = socket;
                body.rifleSway = sway;
                body.leftHandTarget = target;
                body.handRig = handRig;
            }
            finally
            {
                for (int i = 0; i < bones.Length; i++) { bones[i].localPosition = pos[i]; bones[i].localRotation = rot[i]; }
            }
        }

        /// <summary>An aim constraint turning a bone's forward (as the body's forward, in the aiming pose) to a target.</summary>
        static void Aim(Transform parent, string name, Transform bone, Transform target, Transform root, float limit)
        {
            if (bone == null) return;
            var c = new GameObject(name).AddComponent<MultiAimConstraint>();
            c.transform.SetParent(parent, false);
            var d = c.data;
            d.constrainedObject = bone;
            var sources = new WeightedTransformArray(0);
            sources.Add(new WeightedTransform(target, 1f));
            d.sourceObjects = sources;
            d.aimAxis = AxisOf(bone.InverseTransformDirection(root.forward));
            d.upAxis = AxisOf(bone.InverseTransformDirection(root.up));
            d.limits = new Vector2(-limit, limit);
            d.maintainOffset = false;
            d.constrainedXAxis = d.constrainedYAxis = d.constrainedZAxis = true;
            c.data = d;
        }

        static MultiAimConstraintData.Axis AxisOf(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            if (a.x >= a.y && a.x >= a.z) return v.x >= 0f ? MultiAimConstraintData.Axis.X : MultiAimConstraintData.Axis.X_NEG;
            if (a.y >= a.z) return v.y >= 0f ? MultiAimConstraintData.Axis.Y : MultiAimConstraintData.Axis.Y_NEG;
            return v.z >= 0f ? MultiAimConstraintData.Axis.Z : MultiAimConstraintData.Axis.Z_NEG;
        }

        static Transform Gun(Transform root, Transform head, out Transform rightGrip, out Transform leftGrip, out Transform muzzle)
        {
            rightGrip = leftGrip = muzzle = null;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GunPrefab);
            if (prefab == null)
            {
                Debug.LogWarning("[FPSKit] Rigged soldier: build the view models first (" + GunPrefab + ")");
                return null;
            }

            var gun = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(gun, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            gun.name = "Rifle";
            gun.transform.SetParent(root, false);
            gun.transform.localScale = Vector3.one / Scale;   // a real-size rifle on a scaled body

            var spec = gun.GetComponent<ViewModelSpec>();
            rightGrip = spec.rightHand;
            leftGrip = spec.leftHand;
            muzzle = spec.muzzle;
            Object.DestroyImmediate(spec);

            foreach (var r in gun.GetComponentsInChildren<Renderer>(true))
            {
                r.tag = "Metal";
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            gun.transform.localPosition = root.InverseTransformPoint(head.position) + new Vector3(0.05f, -0.07f, 0.24f);
            return gun.transform;
        }
    }
}
#endif
