#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
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
                model.AddComponent<EnemyRootMotion>();
                body.animator = animator;
                body.gun = gun;
                body.rightGrip = rightGrip;
                body.leftGrip = leftGrip;
                body.garments = garments;

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
        /// Idle, walk and run, blended by the agent's speed in metres a second. The pack's
        /// clips are in place, so the thresholds are the speeds they were made for (the
        /// Starter Assets controller walks at 2 and sprints at 5.3), scaled with the body.
        /// </summary>
        static RuntimeAnimatorController LocomotionController()
        {
            string path = Folder + "/Soldier.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);

            var tree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);

            tree.AddChild(Clip("Stand--Idle"), 0f);
            tree.AddChild(Clip("Locomotion--Walk_N"), 2f * Scale);
            tree.AddChild(Clip("Locomotion--Run_N"), 5.3f * Scale);

            var layer = controller.layers[0];
            layer.iKPass = false;
            var state = layer.stateMachine.AddState("Locomotion");
            state.motion = tree;
            layer.stateMachine.defaultState = state;

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
            controller.layers = new[] { layer, Injured(controller), action };

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
