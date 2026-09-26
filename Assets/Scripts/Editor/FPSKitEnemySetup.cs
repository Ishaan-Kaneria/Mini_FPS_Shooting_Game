#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace FPSKit.EditorTools
{
    /// <summary>
    /// Turns a rigged humanoid (Mixamo, Asset Store, anything with a Humanoid
    /// avatar) into a working enemy: per-bone hitboxes with damage multipliers,
    /// NavMeshAgent, EnemyAI, Animator wiring and an optional ragdoll.
    ///
    /// Saves over FPSKit_Generated/Enemy.prefab, so the LevelManager picks it up
    /// with no other changes.
    ///
    /// Menu: FPSKit > Enemy Setup
    /// </summary>
    public class FPSKitEnemySetup : EditorWindow
    {
        private const string PrefabPath = "Assets/FPSKit_Generated/Enemy.prefab";

        private GameObject _model;
        private bool _buildRagdoll = true;
        private bool _ranged;

        private float _health = 100f;
        private float _moveSpeed = 4f;
        private float _attackDamage = 12f;
        private float _attackRange = 2f;
        private float _headshotMultiplier = 3f;

        private string _status = string.Empty;
        private Vector2 _scroll;

        [MenuItem("FPSKit/Enemy Setup", false, 61)]
        public static void Open()
        {
            var window = GetWindow<FPSKitEnemySetup>(true, "FPSKit Enemy Setup", true);
            window.minSize = new Vector2(430f, 460f);
            window.Show();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Enemy Setup", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Drop in a rigged humanoid and this builds the whole enemy: colliders and " +
                "hitboxes on the real bones, NavMeshAgent, AI, Animator hookup and a ragdoll.\n\n" +
                "The model MUST be imported as Humanoid: select the .fbx, Rig tab, " +
                "Animation Type = Humanoid, Apply.",
                MessageType.Info);

            EditorGUILayout.Space();
            _model = (GameObject)EditorGUILayout.ObjectField("Character Model", _model, typeof(GameObject), false);

            if (_model != null && !IsHumanoid(_model))
            {
                EditorGUILayout.HelpBox(
                    "This model has no Humanoid avatar, so bones cannot be found by name. " +
                    "Select the source .fbx, open the Rig tab, set Animation Type to Humanoid and Apply.",
                    MessageType.Error);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Stats", EditorStyles.boldLabel);
            _health = EditorGUILayout.FloatField("Health", _health);
            _moveSpeed = EditorGUILayout.Slider("Move Speed", _moveSpeed, 1f, 10f);
            _attackDamage = EditorGUILayout.FloatField("Attack Damage", _attackDamage);
            _ranged = EditorGUILayout.Toggle("Ranged Attacker", _ranged);
            _attackRange = EditorGUILayout.Slider("Attack Range", _attackRange, 1f, 40f);
            _headshotMultiplier = EditorGUILayout.Slider("Headshot Multiplier", _headshotMultiplier, 1f, 6f);

            EditorGUILayout.Space();
            _buildRagdoll = EditorGUILayout.Toggle("Build Ragdoll", _buildRagdoll);
            if (_buildRagdoll)
            {
                EditorGUILayout.HelpBox(
                    "Adds Rigidbodies and joints to the bones. They stay kinematic until the " +
                    "enemy dies, then it collapses under physics instead of freezing mid-pose.",
                    MessageType.None);
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_model == null || !IsHumanoid(_model)))
            {
                if (GUILayout.Button("Build Enemy Prefab", GUILayout.Height(30f)))
                    Build();
            }

            EditorGUILayout.HelpBox(
                $"Overwrites {PrefabPath}. Rebuild the scene afterwards, or just press Play -- " +
                "the LevelManager already points at that prefab.", MessageType.None);

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_status, MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        // ==================================================================
        private static bool IsHumanoid(GameObject model)
        {
            var animator = model.GetComponentInChildren<Animator>();
            return animator != null && animator.avatar != null && animator.avatar.isHuman;
        }

        private void Build()
        {
            FPSKitSceneBuilder.EnsureProjectTagsAndLayers();

            int enemyLayer = LayerMask.NameToLayer("Enemy");

            // ---- root ----
            var root = new GameObject("Enemy");
            root.tag = "Enemy";
            root.layer = enemyLayer;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(_model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            instance.transform.SetParent(root.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            var animator = instance.GetComponentInChildren<Animator>();
            SetLayerRecursive(root, enemyLayer);

            // ---- health ----
            var health = root.AddComponent<Health>();
            health.maxHealth = _health;
            health.destroyOnDeath = true;
            health.destroyDelay = _buildRagdoll ? 12f : 2.5f;

            // ---- hitboxes on real bones ----
            int hitboxCount = BuildHitboxes(animator, health, enemyLayer, out Transform head);

            // ---- movement collider the agent uses while alive ----
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.35f;
            capsule.center = new Vector3(0f, 0.9f, 0f);

            var agent = root.AddComponent<NavMeshAgent>();
            agent.speed = _moveSpeed;
            agent.angularSpeed = 400f;
            agent.acceleration = 12f;
            // Small and fixed, exactly as the scene builder sets it, and deliberately
            // not derived from the attack range. A NavMeshAgent brakes a full
            // stoppingDistance short of wherever it is sent, and EnemyAI.DesiredStandOff
            // has to give that distance back out of the reach it was going to use -- so
            // range * 0.7 spends most of the reach on the brake. At 2.2m of melee it
            // left 0.66m to stand in; at 25m of rifle it left none at all, which makes
            // DesiredStandOff return zero, preferredRangedDistance do nothing, and the
            // shooter settle wherever 17.5m of braking happened to put it. See the
            // "reach has to be shared with the brake" rule.
            agent.stoppingDistance = 0.8f;
            agent.radius = 0.4f;
            agent.height = 1.8f;

            // ---- eyes ----
            var eyes = new GameObject("Eyes");
            eyes.transform.SetParent(root.transform, false);
            eyes.transform.position = head != null
                ? head.position
                : root.transform.position + Vector3.up * 1.7f;

            // ---- ai ----
            var ai = root.AddComponent<EnemyAI>();
            ai.eyes = eyes.transform;
            ai.animator = animator;
            ai.ranged = _ranged;
            ai.attackRange = _attackRange;
            ai.attackDamage = _attackDamage;
            ai.attackCooldown = 1.3f;
            ai.attackWindup = 0.35f;
            ai.detectionRadius = 60f;
            ai.relentless = true;
            ai.sightBlockers = 1 << LayerMask.NameToLayer("Environment");
            ai.rangedHitMask = ~(1 << enemyLayer);

            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.maxDistance = 40f;

            // ---- floating health bar ----
            // Rigged enemies get the same readable health as the greybox capsule, sat
            // above the model's real height rather than a guessed one.
            var bar = root.AddComponent<EnemyHealthBar>();
            bar.barMaterial = FPSKitSceneBuilder.GetOrCreateBarMaterial();
            bar.heightOffset = capsule.height + 0.5f;

            // ---- ragdoll ----
            int boneCount = 0;
            if (_buildRagdoll)
            {
                boneCount = BuildRagdoll(animator);

                var ragdoll = root.AddComponent<RagdollController>();
                ragdoll.animator = animator;
                ragdoll.mainCollider = capsule;
            }

            // ---- wounds, blood, voice ----
            // Same components the built soldier carries. A rigged enemy falls through its
            // RagdollController, which shares EnemyDeath's rules, so it gets no EnemyDeath.
            root.AddComponent<EnemyWounds>();
            root.AddComponent<EnemyGore>().library = FPSKitGore.GetOrCreateLibrary();

            var voiceObject = new GameObject("Voice");
            voiceObject.transform.SetParent(root.transform, false);
            voiceObject.transform.position = eyes.transform.position;
            var voiceSource = voiceObject.AddComponent<AudioSource>();
            voiceSource.playOnAwake = false;
            voiceSource.spatialBlend = 1f;
            voiceSource.minDistance = 2.5f;
            voiceSource.maxDistance = 45f;
            voiceSource.rolloffMode = AudioRolloffMode.Linear;

            var voice = root.AddComponent<EnemyVoice>();
            voice.bank = FPSKitGore.GetOrCreateVoiceBank();
            voice.source = voiceSource;

            // ---- save ----
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            _status = $"Built {_model.name} as the enemy.\n" +
                      $"  {hitboxCount} hitboxes on real bones\n" +
                      $"  {(_buildRagdoll ? boneCount + " ragdoll bones" : "no ragdoll")}\n" +
                      $"  Animator: {(animator != null ? "wired" : "none found")}\n\n" +
                      "Press Play, or rebuild the scene to bake a fresh NavMesh.";
        }

        // ==================================================================
        /// <summary>
        /// Capsule/box colliders on the bones that matter, each with a Hitbox and a
        /// damage multiplier. Head is the only one flagged as a headshot.
        /// </summary>
        private int BuildHitboxes(Animator animator, Health owner, int layer, out Transform head)
        {
            head = null;
            if (animator == null) return 0;

            head = animator.GetBoneTransform(HumanBodyBones.Head);

            var plan = new (HumanBodyBones bone, float multiplier, float radius, bool headshot)[]
            {
                (HumanBodyBones.Head,          _headshotMultiplier, 0.12f, true),
                (HumanBodyBones.Chest,         1.0f,  0.20f, false),
                (HumanBodyBones.Spine,         1.0f,  0.19f, false),
                (HumanBodyBones.Hips,          0.9f,  0.18f, false),
                (HumanBodyBones.LeftUpperArm,  0.7f,  0.08f, false),
                (HumanBodyBones.RightUpperArm, 0.7f,  0.08f, false),
                (HumanBodyBones.LeftLowerArm,  0.6f,  0.07f, false),
                (HumanBodyBones.RightLowerArm, 0.6f,  0.07f, false),
                (HumanBodyBones.LeftUpperLeg,  0.8f,  0.11f, false),
                (HumanBodyBones.RightUpperLeg, 0.8f,  0.11f, false),
                (HumanBodyBones.LeftLowerLeg,  0.7f,  0.09f, false),
                (HumanBodyBones.RightLowerLeg, 0.7f,  0.09f, false),
            };

            int built = 0;

            foreach (var entry in plan)
            {
                var bone = animator.GetBoneTransform(entry.bone);
                if (bone == null) continue;

                Collider collider;

                if (entry.headshot)
                {
                    var sphere = bone.gameObject.AddComponent<SphereCollider>();
                    sphere.radius = entry.radius;
                    collider = sphere;
                }
                else
                {
                    var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
                    capsule.radius = entry.radius;
                    capsule.height = entry.radius * 5f;
                    capsule.direction = 1;   // along the bone's local Y
                    collider = capsule;
                }

                collider.gameObject.layer = layer;

                var hitbox = bone.gameObject.AddComponent<Hitbox>();
                hitbox.owner = owner;
                hitbox.damageMultiplier = entry.multiplier;
                hitbox.isHeadshot = entry.headshot;
                hitbox.part = PartOf(entry.bone);

                built++;
            }

            return built;
        }

        /// <summary>Which part of the body a bone's hitbox books its wounds to (see EnemyWounds).</summary>
        private static BodyPart PartOf(HumanBodyBones bone)
        {
            switch (bone)
            {
                case HumanBodyBones.Head: return BodyPart.Head;
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.LeftLowerArm: return BodyPart.LeftArm;
                case HumanBodyBones.RightUpperArm:
                case HumanBodyBones.RightLowerArm: return BodyPart.RightArm;
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.LeftLowerLeg: return BodyPart.LeftLeg;
                case HumanBodyBones.RightUpperLeg:
                case HumanBodyBones.RightLowerLeg: return BodyPart.RightLeg;
                default: return BodyPart.Torso;
            }
        }

        // ==================================================================
        /// <summary>
        /// Rigidbodies on every bone that has a hitbox, joined to the nearest
        /// ancestor that also has one. Kinematic until RagdollController releases them.
        /// </summary>
        private int BuildRagdoll(Animator animator)
        {
            if (animator == null) return 0;

            var bones = new List<Transform>();
            foreach (var hitbox in animator.GetComponentsInChildren<Hitbox>())
                bones.Add(hitbox.transform);

            // Rigidbodies first, so joints always have something to connect to.
            foreach (var bone in bones)
            {
                var body = bone.gameObject.GetComponent<Rigidbody>();
                if (body == null) body = bone.gameObject.AddComponent<Rigidbody>();

                body.mass = 3f;
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }

            int joints = 0;

            foreach (var bone in bones)
            {
                var parentBody = FindParentBody(bone);
                if (parentBody == null) continue;   // this is the ragdoll root

                var joint = bone.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = parentBody;
                joint.enablePreprocessing = false;

                // Conservative limits. Without these a ragdoll turns itself inside out.
                joint.lowTwistLimit = new SoftJointLimit { limit = -20f };
                joint.highTwistLimit = new SoftJointLimit { limit = 20f };
                joint.swing1Limit = new SoftJointLimit { limit = 40f };
                joint.swing2Limit = new SoftJointLimit { limit = 40f };

                joints++;
            }

            return bones.Count;
        }

        private static Rigidbody FindParentBody(Transform bone)
        {
            for (var t = bone.parent; t != null; t = t.parent)
            {
                var body = t.GetComponent<Rigidbody>();
                if (body != null) return body;
            }

            return null;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }
    }
}
#endif
