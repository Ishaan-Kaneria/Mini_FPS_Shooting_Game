using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The rigged enemy's body: everything a person's body does in a fight that the brain
/// (<see cref="EnemyAI"/>) does not decide.
///
/// - **Moving.** An Animator plays the walk and run by the agent's speed (EnemyAI writes
///   it); the feet are the clips'.
/// - **Holding and aiming.** The rifle is carried at the shoulder and turned onto the
///   player; the spine, neck and head turn with it, and both hands are reached onto it
///   with the same two-bone IK the player's own arms use (<see cref="LimbIK"/>). It kicks
///   on every round, is drawn back for a butt stroke at close quarters, and drops to the
///   floor as a real object when the gun arm is wrecked (EnemyWounds) or the body dies.
/// - **Being hit, where it was hit.** Each hit puts a short, sharp turn on the bones of the
///   part it landed on, away from the blow: the head snaps back, the chest rocks, a punch
///   to the belly folds it over, an arm is knocked back, a knee buckles. On top of the
///   animation, so it carries on walking while it flinches.
/// - **Thrown.** A kick or a blast hands the bones to physics (<see cref="Launch"/>); the
///   body flies, lands, lies for a while that is never the same twice (longer in pain), and
///   gets up with a clip that starts the way it landed -- on its back or on its front --
///   blended out of the ragdoll's pose in under a second (<see cref="GetUps"/>).
/// - **Hurt.** Legs come from the "Injured" animator layer: a crippled leg limps on its own
///   side for good; two crippled legs throw it down, and it crawls face down or pushes itself
///   along on its back for the rest of its life. Nothing stands a crawler up again. A
///   crippled arm hangs against the body and the rifle is held low in the other hand. While
///   it limps, crawls or rises, the clip moves it (<see cref="EnemyMotor"/>).
///
/// The reactions, the aim and the grip are turns on the bones, made in world space, so they
/// work on any Humanoid skeleton. A body with no "Injured" layer falls back to the old
/// procedural limp, crawl and kneel-and-stand.
/// </summary>
[DisallowMultipleComponent]
public class EnemyBody : MonoBehaviour
{
    [System.Serializable]
    public class Garment
    {
        public Renderer renderer;
        [Tooltip("The same garment in each of the outfits. One outfit is picked per soldier.")]
        public Material[] skins;
    }

    [Header("Wiring")]
    public Animator animator;
    [Tooltip("The rifle's pivot, a child of this object. Its model, grips and muzzle hang under it.")]
    public Transform gun;
    public Transform rightGrip;
    public Transform leftGrip;
    public Garment[] garments;

    [Header("Rifle hold and aim (built by FPSKitRiggedSoldier)")]
    [Tooltip("Under the right hand bone: where the rifle sits in the hand.")]
    public Transform rifleSocket;
    [Tooltip("Under the socket: what the rifle hangs from; sprung a little behind the hand (EnemyRifle).")]
    public Transform rifleSway;
    [Tooltip("On the handguard: where the left hand goes (the hand rig's target).")]
    public Transform leftHandTarget;
    [Tooltip("Where the left elbow points while that hand is on the handguard.")]
    public Transform leftElbowHint;

    [Header("Sidearm (built by FPSKitRiggedSoldier)")]
    [Tooltip("The pistol, in its holster until drawn.")]
    public Transform pistol;
    [Tooltip("Where the pistol sits drawn, in each hand; the holster on the thigh; its muzzle.")]
    public Transform pistolSocketRight, pistolSocketLeft, pistolHolster, pistolMuzzle;
    [Tooltip("Seconds to draw the pistol once the rifle is on the back.")]
    [Range(0.1f, 1.5f)] public float drawSeconds = 0.4f;

    [Header("Holding")]
    [Tooltip("Where the rifle's pivot sits held at the ready, in the body's space (metres).")]
    public Vector3 readyPosition = new Vector3(0.14f, 1.36f, 0.3f);
    [Tooltip("The shoulder it is held from and turned about when aiming, in the body's space.")]
    public Vector3 shoulder = new Vector3(0.15f, 1.45f, 0.02f);
    [Tooltip("Degrees the muzzle is lowered while it has not seen the player: carried, not aimed.")]
    [Range(0f, 60f)] public float lowReady = 28f;
    [Tooltip("How far the body will turn its aim off its own facing, in degrees.")]
    [Range(10f, 90f)] public float aimLimit = 70f;

    [Header("Reactions")]
    [Tooltip("Scales every hit reaction. 0 turns them off.")]
    [Range(0f, 2f)] public float reactionScale = 1f;

    [Header("Thrown (the timings are the type's body profile: BodyDamageProfile, Knockdown)")]
    [Tooltip("The longest it can be in the air or rolling before it is treated as landed.")]
    [Range(1f, 8f)] public float maxDownSeconds = 4.5f;
    [Tooltip("Seconds the get-up takes without get-up clips (the old procedural kneel and stand).")]
    [Range(0.3f, 3f)] public float getUpSeconds = 1.6f;
    [Tooltip("Newtons pulling a hand to the wound while it lies hurt.")]
    [Range(0f, 80f)] public float groundClutchForce = 22f;
    [Tooltip("Turn (N m s) of the small roll it makes on the ground now and then.")]
    [Range(0f, 20f)] public float groundRollImpulse = 3.5f;

    [Header("Legs going")]
    [Tooltip("Metres a second it is thrown down at when both legs go: a fall, not a launch.")]
    [Range(0f, 4f)] public float collapseSpeed = 1.2f;
    [Tooltip("Seconds between pain stumbles on the move.")]
    [Range(0.5f, 10f)] public float stumbleCooldown = 3.5f;

    [Header("Rage")]
    [Tooltip("Seconds it fights in a rage after getting up from a kick: rifle slung, fists up, " +
             "straight at the player.")]
    [Range(0f, 20f)] public float rageSeconds = 7f;
    [Tooltip("Seconds of the rage move (the fighter's 'charge fist') before it charges.")]
    [Range(0f, 3f)] public float rageMoveSeconds = 1.25f;

    /// <summary>
    /// The get-up clips (Assets/Animations/Mixamo/GetUp, states of the same name on the
    /// "Injured" layer), trimmed to the getting up: which way up each starts, the clip time
    /// the head first moves (everything before is lying still) and the time it is 90% of the
    /// way up (the rest is standing about), and whether it is the slow, hurt one. Measured on
    /// this body (head height through the clip). Each is played at whatever speed fits the
    /// profile's rise time, within its limits.
    /// </summary>
    public struct RiseClip
    {
        public string state;
        public bool faceUp;
        public float start, end;
        public bool slow;
        public RiseClip(string state, bool faceUp, float start, float end, bool slow)
        { this.state = state; this.faceUp = faceUp; this.start = start; this.end = end; this.slow = slow; }
    }

    public static readonly RiseClip[] GetUps =
    {
        new RiseClip("GetUp_KnockedDown", true, 0.2f, 1.52f, false),
        new RiseClip("GetUp_Back", true, 2.5f, 5.54f, false),
        new RiseClip("GetUp_Back_B", true, 1.45f, 5.68f, true),
        new RiseClip("GetUp_Stomach", false, 1.72f, 5.42f, false),
        new RiseClip("GetUp_Stomach_Zombie", false, 0.5f, 2.68f, false),
        new RiseClip("GetUp_Stomach_B", false, 3.35f, 7.96f, true),
    };

    // ======================================================================

    EnemyAI _ai;
    EnemyBrain _brain;
    Health _health;
    EnemyWounds _wounds;
    RagdollController _ragdoll;
    UnityEngine.AI.NavMeshAgent _agent;

    Transform _hips, _spine, _chest, _chestLow, _neck, _head;
    Transform _lUpper, _lLower, _lHand, _rUpper, _rLower, _rHand;
    Transform _lThigh, _rThigh, _lCalf, _rCalf, _lFoot, _rFoot;
    LimbIK.HandFrame _lFrame, _rFrame;
    Transform[] _poseBones;

    Vector3 _gunLocal;
    Quaternion _gunLocalRot;
    Vector3 _gunLocalScale = Vector3.one;
    float _aimWeight;
    Quaternion _aim = Quaternion.identity;
    bool _gunDropped;
    EnemyRifle _rifle;
    int _carryLayer = -1, _sidearmLayer = -1;
    float _handWeight = 1f;
    float _sidearmWeight;
    float _carryWeight;
    string _carryState = "Low";

    /// <summary>The rifle and the rig that holds it: the hold of Stage 2. Without them, the old procedural hold.</summary>
    bool Rigged => _rifle != null && _rifle.enabled && leftHandTarget != null;

    /// <summary>How far the rifle sags in pain, 0 to 1 (EnemyRifle lays it on its spring).</summary>
    public float GunDip => _gunDip;

    /// <summary>Down, getting up, recovering, or fetching its weapon: the mind waits on the body.</summary>
    public bool Busy => IsDown || Recovering || (_rifle != null && _rifle.Busy);

    struct Kick { public Transform bone; public Vector3 axis; public float degrees; public float start; public float length; }
    readonly List<Kick> _kicks = new List<Kick>();

    enum Down { Up, Flying, GettingUp }
    Down _down;
    float _downStart, _stillSince, _getUpStart, _lieFor = -1f;
    EnemyMotor _motor;
    int _injuredLayer = -1;
    float _injuredWeight;
    string _injuredState = "Empty";
    bool _collapsed, _wasLimping, _crawlFaceUp;
    float _nextStumble;
    // The clip get-up: which state, its speed, when it is done, how long the ragdoll blend runs.
    string _riseState;
    float _riseSpeed = 1f, _riseLength = 1f, _riseBlendSeconds = 0.45f;
    bool _clipRise;
    Vector3 _snapHead;
    Vector3 _gunHandPos;
    Quaternion _gunHandRot = Quaternion.identity;
    bool _hasHandPose;
    // After a get-up: when the rifle started back from the hand to the ready position.
    float _regripStart = -1f;
    float RegripSeconds => Profile != null ? Profile.weaponRecoverSeconds : 0.5f;
    bool GunFromHand => _hasHandPose && (_down != Down.Up || Recovering || (_regripStart > 0f && Time.time - _regripStart < RegripSeconds));
    // After standing: a brief recovery (stagger, hand to the knee, a look) before the regrip.
    float _recoverStart = -1f, _recoverEnd = -1f;
    bool _rageAfterRecover;
    float _nextLife;
    float _clutchUntil;
    /// <summary>Standing again and getting itself together: no weapon yet.</summary>
    public bool Recovering => _recoverEnd > 0f && Time.time < _recoverEnd;
    BodyDamageProfile Profile => _wounds != null ? _wounds.Body : null;
    Quaternion[] _snapshot;
    Vector3 _snapHipsPos;
    Quaternion _snapHipsRot;

    /// <summary>On the ground or getting up from it.</summary>
    public bool IsDown => _down != Down.Up;

    /// <summary>Getting up (or settling into the crawl) after a fall.</summary>
    public bool Rising => _down == Down.GettingUp;

    /// <summary>The "Injured" layer's state: Empty, LimpLeft/Right, CrawlDown/Up, or a get-up. For the debug overlay and tests.</summary>
    public string InjuredState => _injuredState;

    /// <summary>The last fall: landed on its back, the seconds it chose to lie, the clip it rose with.</summary>
    public bool LastFallFaceUp { get; private set; }
    public float LastLieSeconds => _lieFor;
    public string LastRise => _riseState;

    /// <summary>Up from a kick and out for blood: fists up, the rifle slung, straight at the player.</summary>
    public bool Raging => Time.time < _rageUntil && (_health == null || !_health.IsDead);

    /// <summary>Set by the mind: close enough that it fights with its fists, rifle slung.</summary>
    [System.NonSerialized] public bool HandToHand;

    /// <summary>Still roaring and squaring up, before the charge.</summary>
    public bool Squaring => Time.time < _rageMoveUntil;

    float _rageUntil = -1f, _rageMoveUntil = -1f, _actionUntil = -1f;
    float _actionWeight;
    int _actionLayer = -1;
    float _lastStrikeWindup = -1f;
    bool _slung, _wantSlung;
    // How much the hands hold the rifle: eased off before it is slung, back on after.
    float _hold = 1f;

    // ======================================================================

    void Awake()
    {
        _ai = GetComponent<EnemyAI>();
        _brain = GetComponent<EnemyBrain>();
        _health = GetComponent<Health>();
        _wounds = GetComponent<EnemyWounds>();
        _ragdoll = GetComponent<RagdollController>();
        _agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        _motor = GetComponent<EnemyMotor>();
        _rifle = GetComponent<EnemyRifle>();
        _breathPhase = Random.value * 6.28f;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            _injuredLayer = animator.GetLayerIndex("Injured");
            _carryLayer = animator.GetLayerIndex("Carry");
            _sidearmLayer = animator.GetLayerIndex("Sidearm");
        }


        if (animator != null && animator.avatar != null && animator.avatar.isHuman)
        {
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
            _hips = B(HumanBodyBones.Hips);
            _spine = B(HumanBodyBones.Spine);
            _chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest) ?? _spine;
            _chestLow = B(HumanBodyBones.Chest);
            if (_chestLow == _chest) _chestLow = null;
            _neck = B(HumanBodyBones.Neck);
            _head = B(HumanBodyBones.Head);
            _lUpper = B(HumanBodyBones.LeftUpperArm); _lLower = B(HumanBodyBones.LeftLowerArm); _lHand = B(HumanBodyBones.LeftHand);
            _rUpper = B(HumanBodyBones.RightUpperArm); _rLower = B(HumanBodyBones.RightLowerArm); _rHand = B(HumanBodyBones.RightHand);
            _lThigh = B(HumanBodyBones.LeftUpperLeg); _rThigh = B(HumanBodyBones.RightUpperLeg);
            _lCalf = B(HumanBodyBones.LeftLowerLeg); _rCalf = B(HumanBodyBones.RightLowerLeg);
            _lFoot = B(HumanBodyBones.LeftFoot); _rFoot = B(HumanBodyBones.RightFoot);

            _lFrame = LimbIK.HandFrame.Measure(_lHand, B(HumanBodyBones.LeftMiddleProximal), B(HumanBodyBones.LeftIndexProximal), B(HumanBodyBones.LeftLittleProximal), right: false);
            _rFrame = LimbIK.HandFrame.Measure(_rHand, B(HumanBodyBones.RightMiddleProximal), B(HumanBodyBones.RightIndexProximal), B(HumanBodyBones.RightLittleProximal), right: true);

            var bones = new List<Transform>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var t = B((HumanBodyBones)i);
                if (t != null) bones.Add(t);
            }
            _poseBones = bones.ToArray();
        }

        if (gun != null)
        {
            _gunLocal = gun.localPosition;
            _gunLocalRot = gun.localRotation;
            _gunLocalScale = gun.localScale;
        }
    }

    void OnEnable()
    {
        if (_health != null)
        {
            _health.Damaged += OnDamaged;
            _health.Died += OnDied;
        }

        if (_ai != null) _ai.Knocked = Launch;
    }

    void OnDisable()
    {
        if (_health != null)
        {
            _health.Damaged -= OnDamaged;
            _health.Died -= OnDied;
        }

        if (_ai != null && _ai.Knocked == (System.Func<Vector3, bool>)Launch) _ai.Knocked = null;
    }

    void Start() => Dress();

    /// <summary>One outfit for this soldier, the same skin across every garment that has it.</summary>
    void Dress()
    {
        if (garments == null || garments.Length == 0) return;

        int most = 0;
        foreach (var g in garments) if (g != null && g.skins != null) most = Mathf.Max(most, g.skins.Length);
        if (most == 0) return;

        // Dressed for where it is fighting, most of the time: tan in the sand, grey in the
        // snow, dark at night and on Mars, olive elsewhere. Outfits are 0 olive, 1 tan, 2 grey/black.
        int local = ArenaOutfit();
        int pick = local >= 0 && Random.value < 0.75f ? Mathf.Min(local, most - 1) : Random.Range(0, most);
        foreach (var g in garments)
        {
            if (g == null || g.renderer == null || g.skins == null || g.skins.Length == 0) continue;
            var m = g.skins[Mathf.Min(pick, g.skins.Length - 1)];
            if (m != null) g.renderer.sharedMaterial = m;
        }
    }

    static int ArenaOutfit()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene.Contains("Desert")) return 1;
        if (scene.Contains("Snow") || scene.Contains("Night") || scene.Contains("Mars") || scene.Contains("Auger")) return 2;
        if (scene.Contains("Industrial") || scene.Contains("Fairground")) return 0;
        return -1;
    }

    /// <summary>
    /// The thrown body and the player who kicked it pass through each other: it starts the
    /// throw a boot's length from them, and a body overlapping the player is shoved out of
    /// it at speed -- that is what sent kicked enemies up into the sky.
    /// </summary>
    void IgnorePlayer(bool ignore)
    {
        if (_ai == null || _ai.target == null) return;
        var player = _ai.target.GetComponentsInChildren<Collider>();
        foreach (var bone in GetComponentsInChildren<Collider>())
            foreach (var p in player)
                if (bone != null && p != null && bone.enabled && p.enabled) Physics.IgnoreCollision(bone, p, ignore);
    }

    // ======================================================================
    // Hits
    // ======================================================================

    void OnDamaged(Health health, DamageInfo info)
    {
        if (_health != null && !_health.IsDead)
        {
            _hitFrame = Time.frameCount;
            _hitTime = Time.time;
            _reactFrame = -1;
            _hitHips = _hips != null ? _hips.position : transform.position;
            if (_springs == null) BuildSprings();
            if (_springs != null)
            {
                if (AnglesAtHit.Length != _springs.Length) _anglesAtHitCache = new Vector3[_springs.Length];
                for (int i = 0; i < _springs.Length; i++) AnglesAtHit[i] = _springs[i].angle;
            }
        }
        if (_health == null || _health.IsDead || IsDown || reactionScale <= 0f) return;

        Vector3 dir = info.direction.sqrMagnitude > 0.0001f ? info.direction.normalized : -transform.forward;
        Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up);
        if (flat.sqrMagnitude < 0.0001f) flat = -transform.forward;
        flat.Normalize();

        // The axis that tips a bone away from the blow, the way the hit is travelling.
        Vector3 away = Vector3.Cross(Vector3.up, flat);

        BreakLimbs(info.part);

        float share = info.amount / Mathf.Max(1f, _health.maxHealth + _health.maxShield);
        float strength = Mathf.Clamp(share * 4f, 0.3f, 1f) * (info.fromMelee ? 1.4f : 1f) * reactionScale;

        Hurt(info.part, strength);

        // Everything below the neck is a physical reaction: the round's push on springs in the
        // upper body (HitImpulse). The head keeps its own snap.
        if (info.part != BodyPart.Head && info.part != BodyPart.Neck)
        {
            HitImpulse(info.point, dir, strength);
            return;
        }

        switch (info.part)
        {
            case BodyPart.Head:
                Add(_head, away, 28f * strength, 0.45f);
                Add(_neck, away, 12f * strength, 0.45f);
                break;
            case BodyPart.Neck:
                Add(_neck, away, 22f * strength, 0.45f);
                break;
            case BodyPart.LowerTorso:
                // A blow to the belly folds the body over it, towards the attacker.
                Add(_spine, -away, 22f * strength, 0.6f);
                Add(_chest, -away, 10f * strength, 0.6f);
                Add(_head, -away, 8f * strength, 0.6f);
                break;
            case BodyPart.LeftArm: Add(_lUpper, away, 32f * strength, 0.5f); break;
            case BodyPart.RightArm: Add(_rUpper, away, 32f * strength, 0.5f); break;
            case BodyPart.LeftForearm: Add(_lLower, away, 30f * strength, 0.45f); break;
            case BodyPart.RightForearm: Add(_rLower, away, 30f * strength, 0.45f); break;
            case BodyPart.LeftLeg: Add(_lThigh, away, 16f * strength, 0.5f); Add(_spine, Vector3.Cross(Vector3.up, transform.right), 6f * strength, 0.5f); break;
            case BodyPart.RightLeg: Add(_rThigh, away, 16f * strength, 0.5f); Add(_spine, Vector3.Cross(Vector3.up, -transform.right), 6f * strength, 0.5f); break;
            case BodyPart.LeftShin: Add(_lCalf, -away, 26f * strength, 0.45f); break;
            case BodyPart.RightShin: Add(_rCalf, -away, 26f * strength, 0.45f); break;
            default:
                // The chest rocks back off the round; the head follows a beat later.
                Add(_chest, away, 14f * strength, 0.5f);
                Add(_spine, away, 7f * strength, 0.5f);
                Add(_head, -away, 6f * strength, 0.55f);
                break;
        }
    }

    readonly Dictionary<Transform, float> _broken = new Dictionary<Transform, float>();

    /// <summary>
    /// A limb shot past the point of holding together stays bent: the knee or the elbow
    /// is kinked out sideways for the rest of its life, on top of whatever it is doing.
    /// Brutal on purpose (deform and dismember was the call); off with the blood.
    /// </summary>
    void BreakLimbs(BodyPart part)
    {
        if (_wounds == null || !BloodFX.Enabled) return;
        var limb = BodyParts.Limb(part);
        if (_wounds.Severity(limb) < 0.6f) return;

        Transform bone = limb switch
        {
            BodyPart.LeftLeg => _lCalf,
            BodyPart.RightLeg => _rCalf,
            BodyPart.LeftArm => _lLower,
            BodyPart.RightArm => _rLower,
            _ => null,
        };
        if (bone == null || _broken.ContainsKey(bone)) return;

        float side = limb == BodyPart.LeftLeg || limb == BodyPart.LeftArm ? 1f : -1f;
        // A kink, not a fold: enough to read as broken, not so much the limb looks detached.
        _broken[bone] = side * (BodyParts.IsLeg(limb) ? 14f : 20f);
    }

    void ApplyBroken()
    {
        foreach (var pair in _broken)
            if (pair.Key != null) pair.Key.rotation = Quaternion.AngleAxis(pair.Value, transform.forward) * pair.Key.rotation;
    }

    void Add(Transform bone, Vector3 axis, float degrees, float length)
    {
        if (bone == null || axis.sqrMagnitude < 1e-6f) return;
        if (_kicks.Count > 16) _kicks.RemoveAt(0);
        _kicks.Add(new Kick { bone = bone, axis = axis.normalized, degrees = degrees, start = Time.time, length = length });
    }

    /// <summary>Snaps in over a few hundredths of a second, swings a touch past, and settles.</summary>
    static float Envelope(float t)
    {
        // Up fast (6% of the reaction), so a snap is mostly there on the frame after the blow.
        if (t < 0.06f) { float a = t / 0.06f; return a * (2f - a); }
        float d = (t - 0.06f) / 0.94f;
        return (1f - d) * (1f - d) * (1f + 0.25f * Mathf.Sin(d * Mathf.PI * 2f));
    }

    void OnDied(Health health)
    {
        _kicks.Clear();
        _down = Down.Up;
        _clipRise = false;
        if (_motor != null) _motor.SetRootMotion(false, EnemyMotor.Gait.Still);
        DropGun(Vector3.zero);
    }

    // ======================================================================
    // Thrown
    // ======================================================================

    /// <summary>
    /// Throws the body: ragdoll on, every bone given the velocity, the rifle kept in the
    /// hand. Returns false when it cannot be thrown (no ragdoll, dead, already down), and
    /// the AI shoves it along the ground instead.
    /// </summary>
    public bool Launch(Vector3 velocity)
    {
        if (_ragdoll == null || _health == null || _health.IsDead || IsDown || _hips == null) return false;

        _kicks.Clear();
        _ragdoll.SetRagdollActive(true);
        IgnorePlayer(true);
        if (_agent != null) _agent.enabled = false;

        if (!Rigged && gun != null && !_gunDropped && _rHand != null)
        {
            gun.SetParent(_rHand, true);
            _gunHandPos = gun.localPosition;
            _gunHandRot = gun.localRotation;
            _hasHandPose = true;
        }

        // Thrown, not dropped: the chest takes the boot and the rest follows it, the body
        // turning over as it goes.
        foreach (var body in GetComponentsInChildren<Rigidbody>())
        {
            if (body == null || body.isKinematic) continue;
            bool core = body.transform == _chest || body.transform == _spine || body.transform == _hips;
            body.linearVelocity = velocity * (core ? Random.Range(1.05f, 1.2f) : Random.Range(0.75f, 0.95f));
            body.angularVelocity = Random.insideUnitSphere * 4f + transform.right * Random.Range(3f, 6f);
        }


        // The rifle goes with the fall: onto the sling, or out of the hands.
        if (Rigged) _rifle.OnFall(velocity);
        _handWeight = 0f;

        _down = Down.Flying;
        _downStart = Time.time;
        _groundFired = false;
        _stillSince = -1f;
        _lieFor = -1f;
        if (_motor != null) _motor.SetRootMotion(false, EnemyMotor.Gait.Still);
        if (_ai != null)
        {
            _ai.Downed = true;
            // Held until it is up again (Rise ends it with Recover).
            _ai.ForceStagger(maxDownSeconds + 30f);
        }
        return true;
    }

    void Update()
    {
        if (_health != null && _health.IsDead) { if (_down == Down.Flying) _down = Down.Up; return; }

        LegsGoing();
        UpdateInjured();
        UpdateSidearm();
        UpdateCarry();
        if (_down == Down.GettingUp && _clipRise) RiseProgress();

        if (_down != Down.Flying) return;

        // Landed when the pelvis has stopped moving for a moment, or after the longest. How
        // long it then lies is picked once, at landing: never the same twice, longer in pain.
        var hipsBody = _hips != null ? _hips.GetComponent<Rigidbody>() : null;
        bool still = hipsBody == null || hipsBody.linearVelocity.sqrMagnitude < 0.25f;
        if (still && _stillSince < 0f) _stillSince = Time.time;
        if (!still) _stillSince = -1f;

        float down = Time.time - _downStart;
        bool landed = (down > 0.5f && _stillSince > 0f) || down > maxDownSeconds;
        if (!landed) return;
        if (_lieFor < 0f)
        {
            var p = Profile;
            float pain = _wounds != null ? _wounds.Pain : 0f;
            _lieFor = p != null
                ? Random.Range(p.groundSeconds.x, Mathf.Max(p.groundSeconds.x, p.groundSeconds.y)) + pain * p.groundPainExtra
                  + (_wounds.AnyLimbHurt ? p.groundWoundedExtra : 0f)
                : Random.Range(1f, 2.5f);
            _stillSince = Time.time;
            _nextLife = Time.time + Random.Range(0.3f, 0.8f);
        }
        GroundLife();
        if (Time.time - _stillSince >= _lieFor || down > maxDownSeconds + _lieFor) Rise();
    }

    /// <summary>
    /// The legs' wounds, as they happen: both legs going throws it to the ground (a fall,
    /// by physics, then the crawl); one going, or a hit in high pain on the move, is a stumble.
    /// </summary>
    void LegsGoing()
    {
        if (_wounds == null) return;

        if (_wounds.Legs == EnemyWounds.LegState.Crawling && !_collapsed)
        {
            _collapsed = true;
            if (_down == Down.Up)
            {
                Vector3 dir = _health != null ? _health.LastDamage.direction : -transform.forward;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = -transform.forward;
                Launch(dir.normalized * collapseSpeed + Vector3.down * 0.5f);
            }
            return;
        }

        bool limping = _wounds.Legs == EnemyWounds.LegState.Limping;
        bool moving = _agent != null && _agent.enabled && _agent.velocity.sqrMagnitude > 0.25f;
        bool hitHard = _wounds.InHighPain && Time.time - _wounds.LastHitTime < 0.1f;
        if (_down == Down.Up && Time.time >= _nextStumble && ((limping && !_wasLimping) || (hitHard && moving)))
        {
            _nextStumble = Time.time + stumbleCooldown;
            PlayAction("Stumble", 1.0f);
        }
        _wasLimping = limping;
    }

    /// <summary>
    /// The "Injured" layer: the limp on its own side, the crawl the way up it lies, or the
    /// get-up. Its weight eases in and out so walking, limping and crawling blend; the clip
    /// moves the body once it is most of the pose.
    /// </summary>
    void UpdateInjured()
    {
        if (_injuredLayer < 0 || animator == null || !animator.enabled || _down == Down.Flying) return;

        bool crawling = _ai != null && _ai.Crawling;
        bool limping = !crawling && _wounds != null && _wounds.Legs == EnemyWounds.LegState.Limping;
        string want = _injuredState;
        float target = 0f;
        if (_down == Down.GettingUp && _clipRise) { want = _riseState; target = 1f; }
        else if (crawling) { want = _crawlFaceUp ? "CrawlUp" : "CrawlDown"; target = 1f; }
        else if (limping) { want = _wounds.WorseLeg == BodyPart.LeftLeg ? "LimpLeft" : "LimpRight"; target = 1f; }

        if (want != _injuredState)
        {
            animator.CrossFadeInFixedTime(want, 0.35f, _injuredLayer);
            _injuredState = want;
        }
        _injuredWeight = Mathf.MoveTowards(_injuredWeight, target, Time.deltaTime * 2.5f);
        // On an S-curve: a straight ramp starts and stops the body's change at full speed.
        animator.SetLayerWeight(_injuredLayer, Smooth(_injuredWeight));

        if (_motor != null && _down != Down.GettingUp)
        {
            bool clip = _injuredWeight > 0.5f && (crawling || limping);
            var gait = crawling ? (_crawlFaceUp ? EnemyMotor.Gait.CrawlUp : EnemyMotor.Gait.CrawlDown)
                     : limping ? EnemyMotor.Gait.Limp : EnemyMotor.Gait.Still;
            _motor.SetRootMotion(clip, gait);
        }
    }

    /// <summary>The clip get-up running: done once it is standing (or, for a crawler, once it is down in the crawl).</summary>
    void RiseProgress()
    {
        if (_riseState == "ProneFire") { GroundFire(); return; }
        float elapsed = Time.time - _getUpStart;
        bool crawl = _ai != null && _ai.Crawling;
        if (elapsed < Mathf.Max(_riseBlendSeconds, _riseLength)) return;

        _down = Down.Up;
        _clipRise = false;

        // Up, but not yet back in it: a moment to get itself together (and look for the player),
        // then the weapon comes back to the ready, then a beat before the first shot.
        var p = Profile;
        float recover = crawl ? 0f : (p != null ? Random.Range(p.recoverSeconds.x, Mathf.Max(p.recoverSeconds.x, p.recoverSeconds.y)) : 0.3f);
        _recoverStart = Time.time;
        _recoverEnd = Time.time + recover;
        _regripStart = _recoverEnd;
        float weapon = RegripSeconds;
        Vector2 hold = p != null ? p.holdFireAfterUp : new Vector2(0.4f, 0.8f);
        float aggression = _ai != null ? _ai.Aggression : 0.5f;
        float beat = Random.Range(hold.x, Mathf.Max(hold.x, hold.y)) * Mathf.Lerp(1.3f, 0.7f, aggression);
        if (_ai != null)
        {
            _ai.Downed = false;
            _ai.Recover();
            _ai.ForceStagger(recover + weapon * 0.5f);
            _ai.HoldFire(recover + weapon + beat);
        }
        // Up on two good legs it is angry; on a bad one it is not charging anybody.
        bool sound = _wounds == null || _wounds.Legs == EnemyWounds.LegState.Sound;
        _rageAfterRecover = !crawl && sound && rageSeconds > 0f;
        if (!_rageAfterRecover) _wantSlung = false;
    }

    /// <summary>The recovery once standing: a stagger forward over the knees, a hand to the left knee, eased in and out.</summary>
    void RecoverPose()
    {
        if (_recoverEnd <= 0f) return;
        if (Time.time >= _recoverEnd)
        {
            _recoverEnd = -1f;
            if (_rageAfterRecover) { _rageAfterRecover = false; StartRage(); }
            else if (Rigged) _rifle.BeginRecover();
            return;
        }
        float span = Mathf.Max(0.05f, _recoverEnd - _recoverStart);
        float t = (Time.time - _recoverStart) / span;
        float w = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);   // in and out
        if (_hips == null) return;
        Turn(_spine, transform.right, 16f * w);
        Turn(_chest, transform.right, 8f * w);
        if (_lThigh != null && _lCalf != null)
        {
            Vector3 knee = _lCalf.position + transform.forward * 0.06f * transform.lossyScale.y;
            SolveWeighted(_lUpper, _lLower, _lHand, knee, _lUpper.position + transform.TransformDirection(new Vector3(-0.6f, -0.3f, -0.2f)), w * 0.8f);
        }
    }

    /// <summary>
    /// Lying hurt is not lying still: now and then it groans, a hand goes to where it was hit,
    /// or it rolls a little. Small pushes on the ragdoll, so every one is different.
    /// </summary>
    void GroundLife()
    {
        if (Time.time >= _nextLife)
        {
            _nextLife = Time.time + Random.Range(0.7f, 1.5f);
            float r = Random.value;
            if (r < 0.35f) { var voice = GetComponent<EnemyVoice>(); if (voice != null) voice.Groan(); }
            else if (r < 0.75f) _clutchUntil = Time.time + Random.Range(0.5f, 0.9f);
            else
            {
                // A little roll about the body's long axis, one way or the other.
                Vector3 axis = (_head != null ? _head.position - _hips.position : transform.forward).normalized;
                float push = groundRollImpulse * (Random.value < 0.5f ? -1f : 1f);
                foreach (var t in new[] { _hips, _chest })
                {
                    var rb = t != null ? t.GetComponent<Rigidbody>() : null;
                    if (rb != null && !rb.isKinematic) rb.AddTorque(axis * push, ForceMode.Impulse);
                }
            }
        }

        if (Time.time < _clutchUntil)
        {
            // The hand that is not hurt goes to the wound.
            bool leftHurt = _wounds != null && _wounds.AtLeast(BodyPart.LeftArm, EnemyWounds.LimbState.Crippled);
            var forearm = leftHurt ? _rLower : _lLower;
            var rb = forearm != null ? forearm.GetComponent<Rigidbody>() : null;
            Transform wound = WoundBone(_wounds != null ? _wounds.LastHitPart : BodyPart.Torso);
            if (rb != null && !rb.isKinematic && wound != null)
            {
                Vector3 to = wound.position - forearm.position;
                if (to.sqrMagnitude > 0.01f) rb.AddForce(to.normalized * groundClutchForce, ForceMode.Force);
            }
        }
    }

    Transform WoundBone(BodyPart part) => BodyParts.Limb(part) switch
    {
        BodyPart.LeftLeg => _lThigh,
        BodyPart.RightLeg => _rThigh,
        BodyPart.LeftArm => _lUpper,
        BodyPart.RightArm => _rUpper,
        BodyPart.Head => _neck != null ? _neck : _head,
        _ => _hips,
    };

    /// <summary>
    /// Off the ground, the way it landed. Face up or face down picks the clip; the root is
    /// put under the pelvis and turned so the clip's first frame lies where the body lies;
    /// then the ragdoll's pose is blended into the clip over a fraction of a second. A body
    /// that cannot stand any more goes into the crawl instead, the same way up.
    /// </summary>
    void Rise()
    {
        if (_injuredLayer < 0 || animator == null) { GetUp(); return; }

        _snapshot ??= new Quaternion[_poseBones.Length];
        for (int i = 0; i < _poseBones.Length; i++) _snapshot[i] = _poseBones[i].localRotation;
        _snapHipsPos = _hips.position;
        _snapHipsRot = _hips.rotation;
        _snapHead = _head != null ? _head.position : _hips.position + transform.forward;

        bool faceUp = FaceUp();
        LastFallFaceUp = faceUp;
        bool crawl = _ai != null && _ai.Crawling;
        float pain = _wounds != null ? _wounds.Pain : 0f;
        var prof = Profile;
        bool slow = pain >= (prof != null ? prof.slowRisePain : 0.5f) || (_wounds != null && _wounds.Legs != EnemyWounds.LegState.Sound);

        float start = 0f;
        // Sometimes, landed face down with the rifle still on it and the player ahead of it, it
        // fires from the ground for a few seconds instead of getting straight up (ProneFire), then
        // gets up the usual way (Rise again, with _groundFired set).
        if (!crawl && !faceUp && !_groundFired && WantsGroundFire(prof))
        {
            _riseState = "ProneFire";
            _riseSpeed = 1f;
            _riseLength = 0f;
            Vector2 lie = prof.groundFireSeconds;
            _groundFireUntil = Time.time + Random.Range(Mathf.Min(lie.x, lie.y), Mathf.Max(lie.x, lie.y));
            // Firing, not brawling: a rage left from the last fall would sling the rifle it has just taken up.
            _rageUntil = _rageMoveUntil = -1f;
            _rageAfterRecover = false;
            _wantSlung = false;
        }
        else if (crawl)
        {
            _riseState = faceUp ? "CrawlUp" : "CrawlDown";
            _crawlFaceUp = faceUp;
            _riseSpeed = 1f;
            _riseLength = 0f;
        }
        else
        {
            var pick = Pick(faceUp, slow);
            _riseState = pick.state;
            // Played at whatever speed fits the rise time, within the limits.
            Vector2 seconds = prof != null ? prof.riseSeconds : new Vector2(1.5f, 2.5f);
            Vector2 limits = prof != null ? prof.riseSpeedLimits : new Vector2(0.85f, 1.8f);
            float want = Random.Range(seconds.x, Mathf.Max(seconds.x, seconds.y)) + pain * (prof != null ? prof.painRiseExtra : 0.5f);
            _riseSpeed = Mathf.Clamp((pick.end - pick.start) / Mathf.Max(0.1f, want), limits.x, Mathf.Max(limits.x, limits.y));
            start = pick.start;
            _riseLength = (pick.end - start) / _riseSpeed;
        }

        // The root under the pelvis, on walkable ground.
        Vector3 landed = new Vector3(_hips.position.x, transform.position.y, _hips.position.z);
        if (UnityEngine.AI.NavMesh.SamplePosition(landed, out var hit, 2.5f, UnityEngine.AI.NavMesh.AllAreas)) landed = hit.position;
        else landed = transform.position;
        transform.position = landed;

        _ragdoll.SetRagdollActive(false);
        IgnorePlayer(false);

        // The clip from just before the body starts to move. It is laid where the body lies
        // on the first frame it is posed (AlignRise): the Animator has only just been woken.
        animator.SetLayerWeight(_injuredLayer, 1f);
        _injuredWeight = 1f;
        _injuredState = _riseState;
        animator.SetFloat(PaceId, 0f);
        animator.SetFloat(RiseSpeedId, crawl ? 1f : _riseSpeed);
        animator.Play(_riseState, _injuredLayer, Mathf.Clamp01(start / ClipLength(_riseState)));
        _alignPending = true;

        if (_agent != null)
        {
            _agent.enabled = true;
            _agent.Warp(transform.position);
        }
        if (_motor != null) _motor.SetRootMotion(true, crawl ? (faceUp ? EnemyMotor.Gait.CrawlUp : EnemyMotor.Gait.CrawlDown) : EnemyMotor.Gait.Still);

        Vector2 blend = prof != null ? prof.riseBlendSeconds : new Vector2(0.3f, 0.5f);
        _riseBlendSeconds = Random.Range(Mathf.Min(blend.x, blend.y), Mathf.Max(blend.x, blend.y));
        _clipRise = true;
        _down = Down.GettingUp;
        _getUpStart = Time.time;
    }

    bool _alignPending;

    /// <summary>Why the last fire from the ground ended (for the Stage 3 test).</summary>
    [System.NonSerialized] public string GroundFireEnd = "";

    /// <summary>Lying face down firing from the ground (Stage 3).</summary>
    public bool FiringFromGround => _down == Down.GettingUp && _clipRise && _riseState == "ProneFire";

    // Firing from the ground (Stage 3): until when, and whether this fall has had its go.
    float _groundFireUntil;
    bool _groundFired;

    /// <summary>Whether this fall ends with firing from the ground: the chance (scaled by aggression), the rifle still on the body, a ranged mind, and the player inside the cone ahead of the head.</summary>
    bool WantsGroundFire(BodyDamageProfile prof)
    {
        if (prof == null || prof.groundFireChance <= 0f || _ai == null || !_ai.ranged || _ai.target == null || HandToHand) return false;
        if (!Rigged || _rifle == null || !_rifle.OnBody) return false;
        if (animator == null || _injuredLayer < 0 || !animator.HasState(_injuredLayer, Animator.StringToHash("ProneFire"))) return false;
        Vector3 ahead = Flat(_snapHead - _snapHipsPos);
        Vector3 toPlayer = Flat(_ai.target.position - _snapHipsPos);
        if (ahead.sqrMagnitude < 1e-4f || Vector3.Angle(ahead, toPlayer) > prof.groundFireCone) return false;
        float chance = prof.groundFireChance * Mathf.Lerp(0.6f, 1.4f, _ai.Aggression);
        return Random.value < chance && _rifle.TakeInHand();
    }

    /// <summary>Lying face down firing: after the blend in, round after round while it can see the player, then up.</summary>
    void GroundFire()
    {
        if (Time.time - _getUpStart < _riseBlendSeconds) return;
        var prof = Profile;
        bool rifle = Rigged && _rifle != null && _rifle.State == EnemyRifle.Hold.Held;
        if (Time.time >= _groundFireUntil || !rifle || (_ai != null && _ai.Crawling))
        {
            GroundFireEnd = Time.time >= _groundFireUntil ? "time" : !rifle ? $"rifle {(_rifle != null ? _rifle.State.ToString() : "none")}" : "crawling";
            _groundFired = true;
            Rise();
            return;
        }
        if (_ai != null) _ai.FireFromGround(prof != null ? prof.groundFireIntervalScale : 1.5f);
    }

    /// <summary>A get-up or crawl clip's length (the get-up states share their clip's name).</summary>
    float ClipLength(string state)
    {
        if (animator.runtimeAnimatorController != null)
            foreach (var c in animator.runtimeAnimatorController.animationClips)
                if (c != null && c.name == state) return Mathf.Max(0.01f, c.length);
        return 1f;
    }

    /// <summary>
    /// The first frame the clip is posed: turn the root so the clip's body points the way the
    /// fallen one does (head from pelvis), then shift it so the pelvises meet. The bones are
    /// the root's children, so the posed body moves with it; the blend from the ragdoll then
    /// has only the pose to cover, not a distance.
    /// </summary>
    void AlignRise()
    {
        _alignPending = false;
        Vector3 clipDir = Flat(_head.position - _hips.position);
        Vector3 bodyDir = Flat(_snapHead - _snapHipsPos);
        if (clipDir.sqrMagnitude > 1e-4f && bodyDir.sqrMagnitude > 1e-4f)
        {
            float yaw = Vector3.SignedAngle(clipDir, bodyDir, Vector3.up);
            transform.RotateAround(transform.position, Vector3.up, yaw);
        }
        Vector3 shift = Flat(_snapHipsPos - _hips.position);
        Vector3 to = transform.position + shift;
        if (UnityEngine.AI.NavMesh.SamplePosition(to, out var onMesh, 1.5f, UnityEngine.AI.NavMesh.AllAreas)) to.y = onMesh.position.y;
        transform.position = to;
        if (_agent != null && _agent.enabled)
        {
            if (_motor != null && _motor.RootMotion) _agent.nextPosition = transform.position;
            else _agent.Warp(transform.position);
        }
    }

    static readonly int PaceId = Animator.StringToHash("Pace");
    static readonly int RiseSpeedId = Animator.StringToHash("RiseSpeed");

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    /// <summary>Lying on its back: the front of the chest faces the sky.</summary>
    bool FaceUp()
    {
        if (_lThigh == null || _rThigh == null || _head == null) return true;
        Vector3 right = _rThigh.position - _lThigh.position;
        Vector3 up = _head.position - _hips.position;
        return Vector3.Cross(right, up).y > 0f;
    }

    static RiseClip Pick(bool faceUp, bool slow)
    {
        int count = 0;
        foreach (var g in GetUps) if (g.faceUp == faceUp && g.slow == slow) count++;
        if (count == 0) slow = !slow;
        int n = Random.Range(0, Mathf.Max(1, count == 0 ? 1 : count));
        foreach (var g in GetUps)
            if (g.faceUp == faceUp && g.slow == slow && n-- == 0) return g;
        foreach (var g in GetUps) if (g.faceUp == faceUp) return g;
        return GetUps[0];
    }

    /// <summary>
    /// Back to the animated body from wherever the ragdoll left it: the root is walked to
    /// the nearest ground under the pelvis, turned towards the player, and the pose is
    /// blended from the fallen one over <see cref="getUpSeconds"/>.
    /// </summary>
    void GetUp()
    {
        _snapshot ??= new Quaternion[_poseBones.Length];
        for (int i = 0; i < _poseBones.Length; i++) _snapshot[i] = _poseBones[i].localRotation;
        _snapHipsPos = _hips.position;
        _snapHipsRot = _hips.rotation;

        Vector3 landed = new Vector3(_hips.position.x, transform.position.y, _hips.position.z);
        if (UnityEngine.AI.NavMesh.SamplePosition(landed, out var hit, 2.5f, UnityEngine.AI.NavMesh.AllAreas))
            landed = hit.position;
        else
            landed = transform.position;

        Vector3 face = _ai != null && _ai.target != null ? _ai.target.position - landed : transform.forward;
        face.y = 0f;

        transform.position = landed;
        if (face.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(face);

        _ragdoll.SetRagdollActive(false);
        IgnorePlayer(false);
        if (_agent != null)
        {
            _agent.enabled = true;
            _agent.Warp(landed);
        }

        // The rifle stays in the hand while it gets up; the rage slings it after.
        _down = Down.GettingUp;
        _getUpStart = Time.time;
    }

    // ======================================================================
    // The rage
    // ======================================================================

    /// <summary>
    /// Back on its feet after a kick, it is angry: it slings the rifle, squares up with a
    /// fighter's roar, and comes at the player with its fists for a while.
    /// </summary>
    void StartRage()
    {
        if (rageSeconds <= 0f || animator == null || _health == null || _health.IsDead) return;

        _rageUntil = Time.time + rageSeconds;
        _rageMoveUntil = Time.time + rageMoveSeconds;
        PlayAction("Rage", rageMoveSeconds);
        _wantSlung = true;

        var voice = GetComponent<EnemyVoice>();
        if (voice != null) voice.OnAttack(false);
    }

    /// <summary>Plays one of the fighting moves over the body for a time.</summary>
    void PlayAction(string trigger, float seconds)
    {
        if (animator == null) return;
        if (_actionLayer < 0) _actionLayer = animator.GetLayerIndex("Action");
        if (_actionLayer < 0) return;
        animator.SetTrigger(trigger);
        _actionUntil = Time.time + seconds;
    }

    void UpdateAction()
    {
        if (_actionLayer < 0 || animator == null) return;
        float want = Time.time < _actionUntil ? 1f : 0f;
        // In over a quarter of a second, out a little faster: a move that snaps in reads as a pop.
        _actionWeight = Mathf.MoveTowards(_actionWeight, want, Time.deltaTime * (want > _actionWeight ? 4f : 6f));
        animator.SetLayerWeight(_actionLayer, _actionWeight);

        // In a rage, every blow the brain lands is thrown as a fighter's strike, timed so the
        // blow lands as the wind-up ends.
        bool fists = Raging || HandToHand;
        if (_ai != null) _ai.HandsOnly = fists;
        if (fists) _wantSlung = true;
        if (fists && _ai != null && _ai.Attacking && _ai.WindupStart > _lastStrikeWindup)
        {
            _lastStrikeWindup = _ai.WindupStart;
            PlayAction(Random.value < 0.5f ? "Strike" : "StrikeB", 0.8f);
        }

        if (!fists && _wantSlung && !Raging) _wantSlung = false;
    }

    /// <summary>The rifle across the back, or back in the hands.</summary>
    void Sling(bool onBack)
    {
        if (gun == null || _gunDropped || _chest == null) return;
        _slung = onBack;

        if (onBack)
        {
            gun.SetParent(_chest, true);
            gun.position = _chest.position - transform.forward * 0.2f + transform.up * 0.05f;
            gun.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(40f, transform.forward) * transform.up, -transform.forward);
        }
        else
        {
            gun.SetParent(transform, false);
            gun.localPosition = _gunLocal;
            gun.localRotation = _gunLocalRot;
            gun.localScale = _gunLocalScale;
        }
    }

    // ======================================================================
    // Posing, after the animation
    // ======================================================================

    void LateUpdate()
    {
        if (_hips == null) return;
        if (_down == Down.Flying || (_ragdoll != null && _ragdoll.IsRagdolled)) { TrackReaction(); return; }

        bool alive = _health == null || !_health.IsDead;
        if (!alive) return;

        if (_alignPending && _down == Down.GettingUp && _clipRise) AlignRise();

        UpdateAction();

        // The clips do the limp and the crawl; the turns on the bones are for a body without them.
        bool crawling = _ai != null && _ai.Crawling;
        if (_injuredLayer < 0)
        {
            if (crawling) Crawl();
            else if (_wounds != null && _wounds.Legs == EnemyWounds.LegState.Limping) Limp();
        }

        // Getting up from a clip, the arms are the clip's and the rifle stays in the right hand;
        // once it stands, the rifle comes up to the ready and the hands onto it over a moment.
        // Reaching for the grips while the body is still bent over flips the elbows.
        float up = 1f;
        if (_down == Down.GettingUp)
        {
            if (_clipRise) up = 0f;
            else up = Mathf.Clamp01(((Time.time - _getUpStart) / Mathf.Max(0.1f, getUpSeconds) - 0.75f) / 0.25f);
        }
        else if (_regripStart > 0f && Time.time - _regripStart < RegripSeconds)
        {
            up = Smooth((Time.time - _regripStart) / RegripSeconds);
        }

        if (Rigged)
        {
            // Stage 2's hold: the rifle rides in the right hand (EnemyRifle), the left hand and
            // the aim are the rig's, evaluated with the animation. Here only their weights and
            // targets, and the overlays on top.
            float arms = (_down == Down.Up ? 1f : 0f) * (1f - _actionWeight);
            float hurt = PainWeight();
            PainPose(hurt * arms);
            RecoverPose();
            _rifle.Follow();
            HoldPistol();
            RigAim(arms, crawling);
            RigHands(arms);
            Clutch(hurt * arms);
            HoldWoundedArms(arms);
            React();
            ApplyBroken();
            if (crawling && _down == Down.Up) Breathe();
            // The weapons onto where they are carried, after everything that moved the bones,
            // then the left hand onto the handguard.
            _rifle.Follow();
            HoldPistol();
            LeftHandOnRifle();
            if (_down == Down.GettingUp)
            {
                if (_clipRise) BlendFromRagdoll(Smooth((Time.time - _getUpStart) / Mathf.Max(0.05f, _riseBlendSeconds)));
                else BlendFromFallen(Mathf.Clamp01((Time.time - _getUpStart) / Mathf.Max(0.1f, getUpSeconds)));
            }
            return;
        }

        // A fighting move owns the body while it plays; the rifle is slung during the rage.
        float free = up * (1f - _actionWeight);
        float pain = PainWeight();
        PainPose(pain * free);
        RecoverPose();
        Aim(free * (1f - 0.7f * _gunDip));
        // The hands let go before the rifle goes over the shoulder, and take it again after it
        // comes back: the arms are never yanked off the grips in a frame.
        _hold = Mathf.MoveTowards(_hold, _wantSlung ? 0f : 1f, Time.deltaTime * 4f);
        if (_wantSlung && !_slung && _hold <= 0.001f) Sling(true);
        if (!_wantSlung && _slung) { Sling(false); _hold = 0f; }
        if (!_slung) HoldGun(free * _hold);
        Clutch(pain * free);
        HoldWoundedArms(free);
        React();
        ApplyBroken();

        if (_down == Down.GettingUp)
        {
            if (_clipRise) BlendFromRagdoll(Smooth((Time.time - _getUpStart) / Mathf.Max(0.05f, _riseBlendSeconds)));
            else BlendFromFallen(Mathf.Clamp01((Time.time - _getUpStart) / Mathf.Max(0.1f, getUpSeconds)));
        }
    }

    /// <summary>
    /// The look and the aim, on top of the clip. The head goes to what the mind is looking at
    /// first and fastest; the chest brings the rifle on after it, slower; the body comes round
    /// last (EnemyMotor). The aim is additive: it measures where the clip's rifle points and
    /// turns the spine, chest and upper chest by the difference to the target, so the clip's
    /// bladed stance is kept and the bore ends up on the player. Calm, only the head looks about.
    /// </summary>
    void RigAim(float weight, bool crawling)
    {
        bool armed = _rifle.InHands || _rifle.State == EnemyRifle.Hold.Recovering || (_pistolHand != 0 && _drawn);
        bool sees = _brain != null ? _brain.WeaponUp && armed : _ai != null && _ai.target != null && _ai.HasSpotted && armed;
        float dt = Time.deltaTime;
        _aimWeight = Mathf.MoveTowards(_aimWeight, sees && !crawling ? 1f : 0f, dt * 3f);

        Vector3 eye = _head != null ? _head.position : transform.position + Vector3.up * 1.6f;
        Vector3 look = _brain != null ? _brain.LookPoint
                     : _ai != null && _ai.target != null ? _ai.target.position + Vector3.up * 1.45f : eye + transform.forward * 10f;
        if ((look - eye).sqrMagnitude < 0.25f) look = eye + transform.forward * 10f;

        // The rifle's error from the target, measured on the clip's pose.
        float yawWant = 0f, pitchWant = 0f;
        Transform barrel = _pistolHand != 0 && _drawn && pistolMuzzle != null ? pistolMuzzle : gun;
        if (_aimWeight > 0.001f && barrel != null)
        {
            Vector3 bore = barrel.forward;
            Vector3 want = look - barrel.position;
            yawWant = Mathf.Clamp(Vector3.SignedAngle(Flat(bore), Flat(want), Vector3.up), -aimLimit, aimLimit);
            pitchWant = Mathf.Clamp(Elevation(bore) - Elevation(want), -45f, 45f);
        }
        _aimYaw = Mathf.Lerp(_aimYaw, yawWant * _aimWeight * weight, 1f - Mathf.Exp(-dt * 4f));
        _aimPitch = Mathf.Lerp(_aimPitch, pitchWant * _aimWeight * weight, 1f - Mathf.Exp(-dt * 4f));

        Turn(_spine, Vector3.up, _aimYaw * 0.3f);
        Turn(_chestLow, Vector3.up, _aimYaw * 0.3f);
        Turn(_chest, Vector3.up, _aimYaw * 0.4f);
        Vector3 side = Quaternion.AngleAxis(_aimYaw, Vector3.up) * transform.right;
        Turn(_spine, side, _aimPitch * 0.25f);
        Turn(_chestLow, side, _aimPitch * 0.3f);
        Turn(_chest, side, _aimPitch * 0.45f);

        // The head: whatever the chest has not already turned, quickly, within a neck's reach.
        float headYaw = Mathf.Clamp(Vector3.SignedAngle(Flat(transform.forward), Flat(look - eye), Vector3.up) - _aimYaw, -70f, 70f);
        float headPitch = Mathf.Clamp(-Elevation(look - eye) - _aimPitch, -40f, 40f);
        float lookWeight = weight * (crawling ? 0.6f : 1f);
        _headYaw = Mathf.Lerp(_headYaw, headYaw * lookWeight, 1f - Mathf.Exp(-dt * 9f));
        _headPitch = Mathf.Lerp(_headPitch, headPitch * lookWeight * 0.7f, 1f - Mathf.Exp(-dt * 9f));
        Turn(_neck, Vector3.up, _headYaw * 0.4f);
        Turn(_head, Vector3.up, _headYaw * 0.6f);
        Vector3 headSide = Quaternion.AngleAxis(_aimYaw + _headYaw, Vector3.up) * transform.right;
        Turn(_head, headSide, _headPitch);
    }

    float _aimYaw, _aimPitch, _headYaw, _headPitch;

    /// <summary>On the ground, alive: the chest rises and falls, faster and deeper in pain.</summary>
    void Breathe()
    {
        float pain = _wounds != null ? _wounds.Pain : 0f;
        float rate = Mathf.Lerp(0.25f, 0.55f, pain);
        float depth = Mathf.Lerp(2f, 4.5f, pain);
        float b = Mathf.Sin(Time.time * rate * Mathf.PI * 2f + _breathPhase) * depth;
        Turn(_chest, transform.right, -b);
        Turn(_neck, transform.right, b * 0.4f);
    }

    float _breathPhase;

    static float Elevation(Vector3 v) => Mathf.Asin(Mathf.Clamp(v.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;

    /// <summary>The left hand onto the rifle's handguard, at the eased weight; the elbow out to its hint.</summary>
    void LeftHandOnRifle()
    {
        if (_handWeight <= 0.001f || leftHandTarget == null || _lUpper == null || _lLower == null || _lHand == null) return;
        Vector3 pole = leftElbowHint != null ? leftElbowHint.position : _lUpper.position + transform.TransformDirection(new Vector3(-0.5f, -0.8f, 0f));
        SolveWeighted(_lUpper, _lLower, _lHand, leftHandTarget.position, pole, _handWeight);
        _lHand.rotation = Quaternion.Slerp(_lHand.rotation, leftHandTarget.rotation, _handWeight);
    }

    /// <summary>The left hand's weight on the handguard, 0 to 1 (for the debug overlay and tests).</summary>
    public float LeftHandWeight => _handWeight;

    /// <summary>
    /// The left hand on the handguard, eased on and off: never yanked. For a fist fight the hand
    /// lets go first, then the rifle goes on the back; after it, the rifle comes back off it.
    /// </summary>
    void RigHands(float arms)
    {
        if (_wounds != null && _wounds.Disarmed && _rifle.InHands) _rifle.Drop(transform.forward * 1.5f + Vector3.up);

        float want = _rifle.LeftHandWant * arms * (1f - _leftOff);
        if (_wantSlung) want = 0f;
        _handWeight = Mathf.MoveTowards(_handWeight, want, Time.deltaTime * 5f);

        if (_wantSlung && _rifle.InHands && _handWeight < 0.05f) _rifle.SetOnBack(true);
        else if (!_wantSlung && !_needSidearm && _pistolHand == 0 && _rifle.State == EnemyRifle.Hold.OnBack && _down == Down.Up && !Recovering) _rifle.SetOnBack(false);
        _slung = _rifle.State == EnemyRifle.Hold.OnBack;

        // Its rifle gone for good and no sidearm to draw: it fights with its hands.
        if (_rifle.Lost && pistol == null && _ai != null && _ai.ranged) _ai.Disarm();
    }

    // ======================================================================
    // The sidearm
    // ======================================================================

    int _pistolHand;          // 0 holstered, 1 right hand, -1 left hand
    bool _needSidearm;

    /// <summary>The pistol: 0 holstered, 1 drawn in the right hand, -1 in the left (for the debug overlay and tests).</summary>
    public int PistolHand => _drawn ? _pistolHand : 0;
    bool _drawn;
    float _drawStart = -1f;
    Vector3 _drawFrom;
    Quaternion _drawFromRot;

    /// <summary>
    /// One arm crippled, or the rifle gone for good: the rifle goes over the shoulder and the
    /// pistol comes out into the good hand -- the left, if it is the gun arm that has gone.
    /// Its muzzle becomes the one the shots and the flash come from.
    /// </summary>
    void UpdateSidearm()
    {
        _needSidearm = false;
        if (!Rigged || pistol == null || _wounds == null || _pistolHand != 0) return;
        bool rightGone = _wounds.AtLeast(BodyPart.RightArm, EnemyWounds.LimbState.Crippled);
        bool leftGone = _wounds.AtLeast(BodyPart.LeftArm, EnemyWounds.LimbState.Crippled);
        if (_wounds.Disarmed || (rightGone && leftGone)) return;
        if (!(rightGone || leftGone || _rifle.Lost)) return;
        _needSidearm = true;
        if (_down != Down.Up || Recovering || _wantSlung) return;

        // The rifle off the hands first (it is carried onto the back), then the draw.
        if (_rifle.InHands) { _rifle.SetOnBack(true); return; }
        if (_rifle.State == EnemyRifle.Hold.Stowing || _rifle.Busy) return;

        _pistolHand = rightGone ? -1 : 1;
        _drawStart = Time.time;
        _drawFrom = transform.InverseTransformPoint(pistol.position);
        _drawFromRot = Quaternion.Inverse(transform.rotation) * pistol.rotation;
        if (_motor != null) _motor.PistolHand = _pistolHand;
        _wounds.Sidearm = true;
        if (_ai != null)
        {
            _ai.muzzlePoint = pistolMuzzle;
            _ai.HoldFire(drawSeconds + 0.3f);
            if (!_ai.ranged) _ai.Disarm();   // never: a disarmed body has no good hand
        }
    }

    /// <summary>
    /// The pistol where it is: in the holster, coming up into the hand, or in it. A child of the
    /// root, laid on the holster or the hand's socket each frame (never parented into the skeleton).
    /// </summary>
    void HoldPistol()
    {
        if (pistol == null) return;
        if (_pistolHand == 0)
        {
            if (pistolHolster != null) pistol.SetPositionAndRotation(pistolHolster.position, pistolHolster.rotation);
            return;
        }
        var socket = _pistolHand > 0 ? pistolSocketRight : pistolSocketLeft;
        if (socket == null) return;
        float t = _drawn ? 1f : Smooth((Time.time - _drawStart) / Mathf.Max(0.05f, drawSeconds));
        Vector3 from = pistolHolster != null ? pistolHolster.position : transform.TransformPoint(_drawFrom);
        Quaternion fromRot = pistolHolster != null ? pistolHolster.rotation : transform.rotation * _drawFromRot;
        pistol.SetPositionAndRotation(Vector3.Lerp(from, socket.position, t), Quaternion.Slerp(fromRot, socket.rotation, t));
        if (t >= 1f) _drawn = true;
    }

    /// <summary>
    /// The arms holding the rifle over clips made without one: a limp, a calm idle, a calm walk
    /// with the rifle carried low. Off on the ground and while the arms have other work.
    /// </summary>
    void UpdateCarry()
    {
        if (_carryLayer < 0 || animator == null || !Rigged) return;
        bool crawling = _ai != null && _ai.Crawling;
        bool limping = !crawling && _wounds != null && _wounds.Legs == EnemyWounds.LegState.Limping;
        bool calm = _brain != null && !_brain.WeaponUp;
        bool held = _rifle.InHands;
        bool sidearm = _pistolHand != 0;
        bool free = _down == Down.Up && !crawling && !Recovering && _actionWeight < 0.5f;
        float want = free && !sidearm && held && (limping || calm) ? 1f : 0f;
        string state = limping && !calm ? "Aim" : "Low";
        if (state != _carryState) { animator.CrossFadeInFixedTime(state, 0.3f, _carryLayer); _carryState = state; }
        _carryWeight = Mathf.MoveTowards(_carryWeight, want, Time.deltaTime * 3f);
        animator.SetLayerWeight(_carryLayer, Smooth(_carryWeight));

        // The pistol held out: its own upper body, eased in as it is drawn.
        if (_sidearmLayer >= 0)
        {
            if (sidearm && _sidearmWeight <= 0.001f)
                animator.Play(_pistolHand > 0 ? "Pistol" : "PistolLeft", _sidearmLayer, Random.value);
            _sidearmWeight = Mathf.MoveTowards(_sidearmWeight, sidearm && free ? 1f : 0f, Time.deltaTime / Mathf.Max(0.1f, drawSeconds));
            animator.SetLayerWeight(_sidearmLayer, Smooth(_sidearmWeight));
        }
    }

    /// <summary>The spine, neck and head turned onto the player, shared down the chain.</summary>
    void Aim(float weight)
    {
        // With a mind (EnemyBrain), the rifle comes up only once it is alert, and the head
        // turns to whatever it is looking at -- a noise, a scan of the street. Without one,
        // up the moment it has spotted the player.
        bool sees = _brain != null ? _brain.WeaponUp && !_gunDropped
                  : _ai != null && _ai.target != null && _ai.HasSpotted && !_gunDropped;
        float want = sees ? 1f : 0f;
        _aimWeight = Mathf.MoveTowards(_aimWeight, want, Time.deltaTime * 3f) * weight;

        Quaternion target = Quaternion.identity;
        if (_ai != null && _ai.target != null)
        {
            Vector3 eye = transform.TransformPoint(shoulder);
            Vector3 at = _brain != null ? _brain.LookPoint : _ai.target.position + Vector3.up * 1.45f;
            Vector3 local = transform.InverseTransformDirection(at - eye);
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -aimLimit, aimLimit);
            float pitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -45f, 45f);
            target = Quaternion.Euler(pitch, yaw, 0f);
        }

        // Carried low, the head still turns to what it is looking at; aimed, the whole chain does.
        Quaternion carried = Quaternion.Euler(lowReady, target.eulerAngles.y > 180f ? target.eulerAngles.y - 360f : target.eulerAngles.y, 0f);
        if (_brain == null) carried = Quaternion.Euler(lowReady, 0f, 0f);
        _aim = Quaternion.Slerp(_aim, Quaternion.Slerp(carried, target, _aimWeight), Time.deltaTime * 6f);

        // Shares of the turn down the chain. The head takes the rest of the pitch so the
        // eyes are on the player whatever the chest manages.
        _aim.ToAngleAxis(out float angle, out Vector3 axisLocal);
        if (angle > 180f) angle -= 360f;
        Vector3 axis = transform.TransformDirection(axisLocal);
        float yawOnly = Vector3.Dot(axis, transform.up) * angle;

        Turn(_spine, transform.up, yawOnly * 0.25f * _aimWeight);
        Turn(_chest, transform.up, yawOnly * 0.35f * _aimWeight);
        // The head looks where the mind looks even with the rifle down: a sentry's head
        // turning to a noise is most of what makes it read as alert.
        float look = _brain != null ? Mathf.Max(_aimWeight, 0.9f * weight) : _aimWeight;
        float calm = Mathf.Max(0f, look - _aimWeight);
        Turn(_neck, transform.up, yawOnly * (0.15f * _aimWeight + 0.35f * calm));
        Turn(_head, transform.up, yawOnly * (0.25f * _aimWeight + 0.45f * calm));

        float pitchOnly = Vector3.Dot(axis, transform.right) * angle;
        Turn(_chest, transform.right, pitchOnly * 0.35f * _aimWeight);
        Turn(_head, transform.right, pitchOnly * 0.4f * _aimWeight);
    }

    static void Turn(Transform bone, Vector3 axis, float degrees)
    {
        if (bone == null || Mathf.Abs(degrees) < 0.01f) return;
        bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
    }

    /// <summary>
    /// The rifle at the shoulder, turned by the aim, kicked by each round and swung at
    /// close quarters; then both hands onto it.
    /// </summary>
    void HoldGun(float weight)
    {
        if (gun == null || _gunDropped) return;

        // Prone, the rifle goes with the hand rather than floating at a standing height.
        bool prone = _ai != null && _ai.Crawling;
        if (prone)
        {
            if (gun.parent != _rHand && _rHand != null) gun.SetParent(_rHand, true);
            if (_wounds != null && _wounds.Disarmed) DropGun(transform.forward + Vector3.up);
            return;
        }
        if (gun.parent != transform)
        {
            gun.SetParent(transform, false);
            gun.localPosition = _gunLocal;
            gun.localRotation = _gunLocalRot;
            gun.localScale = _gunLocalScale;
        }

        Vector3 pivot = shoulder;
        Vector3 rest = readyPosition;
        Quaternion turn = _aim;
        Vector3 extra = Vector3.zero;

        // Each round: back into the shoulder and the muzzle up, gone in a tenth of a second.
        if (_ai != null)
        {
            float since = Time.time - _ai.LastShotTime;
            if (since >= 0f && since < 0.12f)
            {
                float k = 1f - since / 0.12f;
                extra += new Vector3(0f, 0.01f, -0.05f) * k;
                turn = Quaternion.Euler(-5f * k, 0f, 0f) * turn;
            }

            // A butt stroke at whoever is in its face: drawn back through the wind-up,
            // driven forward when it lands.
            if (_ai.Attacking && !_ai.ranged || _ai.Attacking && _ai.target != null
                && Vector3.Distance(transform.position, _ai.target.position) <= _ai.meleeRange + 0.3f)
            {
                float t = Mathf.InverseLerp(_ai.WindupStart, _ai.WindupEnd, Time.time);
                extra += new Vector3(0.05f, 0.08f, -0.18f) * t;
                turn = Quaternion.Euler(-35f * t, 10f * t, 0f) * turn;
            }

            float swing = Time.time - _ai.LastMeleeTime;
            if (swing >= 0f && swing < 0.3f)
            {
                float k = Mathf.Sin(swing / 0.3f * Mathf.PI);
                extra += new Vector3(-0.05f, 0.05f, 0.35f) * k;
                turn = Quaternion.Euler(55f * k, 0f, 0f) * turn;
            }
        }

        // In pain: the rifle sags off the target, and comes down with the body when it buckles.
        // One arm gone: held low in the other hand, for good.
        float dip = _gunDip;
        if (_wounds != null && _wounds.OneHanded) dip = Mathf.Max(dip, 0.45f);
        if (dip > 0.001f) turn = Quaternion.Euler(40f * dip, -15f * dip, 0f) * turn;
        extra += Vector3.down * _bodyDrop;

        Vector3 local = pivot + turn * (rest - pivot) + extra;
        Vector3 readyPos = transform.TransformPoint(local);
        Quaternion readyRot = transform.rotation * turn;
        if (GunFromHand && _rHand != null)
        {
            // Getting up: the rifle comes up from where the hand has it, not from mid-air.
            float k = Smooth(weight);
            gun.position = Vector3.Lerp(_rHand.TransformPoint(_gunHandPos), readyPos, k);
            gun.rotation = Quaternion.Slerp(_rHand.rotation * _gunHandRot, readyRot, k);
        }
        else
        {
            gun.position = readyPos;
            gun.rotation = readyRot;
        }

        // The hands. The gun arm lets go when the arm is too hurt to hold anything.
        float rightHold = weight;
        float leftHold = weight;
        if (_wounds != null)
        {
            if (_wounds.AtLeast(BodyPart.LeftArm, EnemyWounds.LimbState.Crippled)) leftHold = 0f;
            if (_wounds.AtLeast(BodyPart.RightArm, EnemyWounds.LimbState.Crippled)) rightHold = 0f;
        }
        // The off hand leaves the rifle to clutch the wound, or because it is the wound.
        leftHold *= 1f - _leftOff;
        // Getting up, the rifle comes up from the right hand; the left reaches for it only
        // once it is most of the way there. Reaching for a grip still on its way past the
        // shoulder flips the elbow from one side to the other in a frame.
        if (GunFromHand) leftHold *= Mathf.Clamp01((weight - 0.6f) / 0.4f);

        if (rightHold > 0.01f && rightGrip != null) Reach(_rUpper, _rLower, _rHand, _rFrame, rightGrip, transform.TransformDirection(new Vector3(0.6f, -0.7f, -0.3f)), rightHold);
        if (leftHold > 0.01f && leftGrip != null) Reach(_lUpper, _lLower, _lHand, _lFrame, leftGrip, transform.TransformDirection(new Vector3(-0.5f, -0.8f, 0f)), leftHold);

        if (_wounds != null && _wounds.Disarmed) DropGun(transform.forward * 1.5f + Vector3.up);
    }

    static void Reach(Transform upper, Transform lower, Transform hand, LimbIK.HandFrame frame, Transform grip, Vector3 elbow, float weight)
    {
        if (upper == null || lower == null || hand == null) return;

        Quaternion handRest = hand.rotation;
        SolveWeighted(upper, lower, hand, grip.position, upper.position + elbow, weight);

        if (frame.valid)
            hand.rotation = Quaternion.Slerp(handRest, frame.World(grip.forward, grip.up), weight);
    }

    /// <summary>
    /// Two-bone IK at a share: solved fully, then each bone eased back towards its animated
    /// turn by the weight. Easing only the target is not enough -- the solver swings the elbow
    /// into the pole's plane at any weight above nothing, and that swing is a pop.
    /// </summary>
    static void SolveWeighted(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 pole, float weight)
    {
        if (weight <= 0.001f) return;
        Quaternion u = upper.rotation, l = lower.rotation, h = hand.rotation;
        LimbIK.Solve(upper, lower, hand, target, pole);
        if (weight >= 0.999f) return;
        upper.rotation = Quaternion.Slerp(u, upper.rotation, weight);
        lower.rotation = Quaternion.Slerp(l, lower.rotation, weight);
        hand.rotation = Quaternion.Slerp(h, hand.rotation, weight);
    }

    /// <summary>Lets go of the rifle: it falls as a real object and stays where it lands.</summary>
    void DropGun(Vector3 push)
    {
        if (Rigged) { _rifle.Drop(push); _gunDropped = true; return; }
        if (gun == null || _gunDropped) return;
        _gunDropped = true;

        gun.SetParent(null, true);
        var body = gun.gameObject.GetComponent<Rigidbody>();
        if (body == null) body = gun.gameObject.AddComponent<Rigidbody>();
        body.mass = 3.5f;
        body.linearDamping = 0.3f;
        body.angularDamping = 0.8f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.Continuous;   // swept: see EnemyRifle.Loose
        body.isKinematic = false;
        // Boxes fitted to the gun's own parts (it used to be one fixed box that left it 15 mm up on
        // its side and the magazine 11 cm into the ground upright); see GunColliders.
        if (gun.GetComponentInChildren<Collider>() == null)
        {
            var boxes = GunColliders.Fit(gun);
            GunColliders.IgnoreBody(boxes, transform);
            body.ResetCenterOfMass();
            body.ResetInertiaTensor();
        }
        body.linearVelocity = push;
        body.angularVelocity = Random.insideUnitSphere * 3f;
    }

    // ======================================================================
    // Physical hit reactions
    // ======================================================================

    [Header("Hit reactions (physical)")]
    [Tooltip("How hard the upper body springs back after a hit (per second squared). Lower is looser.")]
    [Range(20f, 600f)] public float hitStiffness = 150f;
    [Tooltip("How quickly the sway dies. About twice the square root of the stiffness stops without overshoot; less rocks back once.")]
    [Range(1f, 60f)] public float hitDamping = 15f;
    [Tooltip("Degrees a second a full-strength hit starts a bone turning at, next to where it lands.")]
    [Range(0f, 2000f)] public float hitKick = 700f;
    [Tooltip("Metres over which a hit's push fades with distance from where it lands.")]
    [Range(0.05f, 1.5f)] public float hitReach = 0.35f;
    [Tooltip("The most any bone is turned by hits, degrees.")]
    [Range(5f, 80f)] public float hitLimit = 35f;
    [Tooltip("Seconds of a hit's turn given at once, on the frame it lands, before the springs take over. " +
             "Without it the first frame of a reaction was a velocity only, and a blow read as late.")]
    [Range(0f, 0.08f)] public float hitLead = 0.03f;

    // Hit to visible reaction, measured (VerifyCombat): the frame a blow landed and the first
    // frame the body was seen to answer it.
    int _hitFrame = -1, _reactFrame = -1;
    float _hitTime;
    Vector3 _hitHips;
    Vector3[] _anglesAtHitCache;
    Vector3[] AnglesAtHit => _anglesAtHitCache ??= new Vector3[_springs != null ? _springs.Length : 0];

    /// <summary>Frames from the last blow landing to the body visibly answering it; -1 while waiting or with no blow.</summary>
    public int ReactionLatencyFrames => _hitFrame >= 0 && _reactFrame >= 0 ? _reactFrame - _hitFrame : -1;

    /// <summary>The frame the last blow landed on, or -1.</summary>
    public int LastHitFrame => _hitFrame;

    /// <summary>Notes the first frame after a blow that the body is seen to move for it: the hit springs or a head snap past 1.5 degrees, or a thrown body 2 cm off.</summary>
    void TrackReaction()
    {
        if (_hitFrame < 0 || _reactFrame >= 0) return;
        // What this blow moved: each spring against where it was when the blow landed (a body
        // still swinging from the last one is not an answer to this one), and snaps it started.
        float deg = 0f;
        if (_springs != null && AnglesAtHit.Length == _springs.Length)
            for (int i = 0; i < _springs.Length; i++) deg = Mathf.Max(deg, (_springs[i].angle - AnglesAtHit[i]).magnitude);
        foreach (var k in _kicks)
            if (k.start >= _hitTime) deg = Mathf.Max(deg, Mathf.Abs(k.degrees * Envelope(Mathf.Clamp01((Time.time - k.start) / k.length))));
        bool thrown = (_down == Down.Flying || (_ragdoll != null && _ragdoll.IsRagdolled)) && _hips != null
                      && (_hips.position - _hitHips).sqrMagnitude > 0.02f * 0.02f;
        if (deg >= 1.5f || thrown) _reactFrame = Time.frameCount;
    }

    struct Spring { public Transform bone; public Vector3 angle, velocity; public float give; }
    Spring[] _springs;

    void BuildSprings()
    {
        var bones = new List<(Transform, float)>
        {
            (_spine, 0.6f), (_chestLow, 0.8f), (_chest, 1f), (_neck, 0.5f),
            (_lUpper, 1.2f), (_lLower, 1.3f), (_rUpper, 1.2f), (_rLower, 1.3f),
        };
        var list = new List<Spring>();
        foreach (var (bone, give) in bones) if (bone != null) list.Add(new Spring { bone = bone, give = give });
        _springs = list.ToArray();
    }

    /// <summary>
    /// A round's push on the upper body: every sprung bone is set turning about the axis the
    /// push makes round it (lever from the bone to where it landed, crossed with the way the
    /// round is going), harder the nearer it landed. A leg hit pushes the hips and the trunk
    /// over the leg; the legs themselves stay the clip's. No two hits are the same, and they add up.
    /// </summary>
    void HitImpulse(Vector3 point, Vector3 dir, float strength)
    {
        if (_springs == null) BuildSprings();
        if (point == Vector3.zero) point = _chest != null ? _chest.position : transform.position + Vector3.up;
        float jitter = Random.Range(0.85f, 1.15f);
        for (int i = 0; i < _springs.Length; i++)
        {
            var sp = _springs[i];
            Vector3 lever = point - sp.bone.position;
            Vector3 axis = Vector3.Cross(lever, dir);
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(Vector3.up, dir);
            float near = Mathf.Exp(-lever.magnitude / Mathf.Max(0.05f, hitReach));
            Vector3 add = axis.normalized * (hitKick * strength * near * sp.give * jitter);
            sp.velocity += add;
            // Some of the turn at once: the body answers on the frame the blow lands.
            sp.angle = Vector3.ClampMagnitude(sp.angle + add * hitLead, hitLimit);
            _springs[i] = sp;
        }
    }

    /// <summary>The springs, stepped and laid onto the bones over the animation.</summary>
    void StepSprings()
    {
        if (_springs == null) return;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        for (int i = 0; i < _springs.Length; i++)
        {
            var sp = _springs[i];
            if (sp.bone == null) continue;
            sp.velocity += (-hitStiffness * sp.angle - hitDamping * sp.velocity) * dt;
            sp.angle = Vector3.ClampMagnitude(sp.angle + sp.velocity * dt, hitLimit);
            _springs[i] = sp;
            float a = sp.angle.magnitude;
            if (a > 0.05f) sp.bone.rotation = Quaternion.AngleAxis(a, sp.angle / a) * sp.bone.rotation;
        }
    }

    /// <summary>The hit reactions: each a turn about its own axis, fading on its own envelope.</summary>
    void React()
    {
        StepSprings();
        for (int i = _kicks.Count - 1; i >= 0; i--)
        {
            var k = _kicks[i];
            float t = (Time.time - k.start) / k.length;
            if (t >= 1f || k.bone == null) { _kicks.RemoveAt(i); continue; }
            k.bone.rotation = Quaternion.AngleAxis(k.degrees * Envelope(t), k.axis) * k.bone.rotation;
        }
        TrackReaction();
    }

    /// <summary>One leg short of good: the hips drop over it with each step and the body leans to it.</summary>
    void Limp()
    {
        if (_wounds == null) return;
        float side = _wounds.WorseLeg == BodyPart.LeftLeg ? -1f : 1f;
        float speed = _agent != null && _agent.enabled ? _agent.velocity.magnitude : 0f;
        float step = Mathf.Abs(Mathf.Sin(Time.time * (3f + speed * 1.5f)));

        _hips.position += Vector3.down * (0.05f * step * Mathf.Clamp01(speed));
        Turn(_spine, transform.forward, -side * 7f);
        Turn(_hips, transform.forward, side * 5f * step);
    }

    /// <summary>
    /// Down and dragging itself: the whole body tipped forward onto the ground about the
    /// hips, which are lowered to it; the legs trail. The aim and the arms are worked out
    /// from this pose afterwards, so it still points the rifle up at the player.
    /// </summary>
    void Crawl()
    {
        Vector3 ground = transform.position + Vector3.up * 0.22f;
        _hips.position = new Vector3(_hips.position.x, ground.y, _hips.position.z) - transform.forward * 0.35f;
        Turn(_hips, transform.right, 78f);
        Turn(_spine, transform.right, -18f);
        Turn(_head, transform.right, -40f);
    }

    /// <summary>
    /// The clip get-up's first fraction of a second: every bone from where the ragdoll left
    /// it towards the clip's pose, last of everything, so the first frame is exactly the body
    /// as it lay and nothing snaps.
    /// </summary>
    void BlendFromRagdoll(float w)
    {
        if (_snapshot == null || w >= 1f) return;
        for (int i = 0; i < _poseBones.Length; i++)
        {
            if (_poseBones[i] == _hips) continue;
            _poseBones[i].localRotation = Quaternion.Slerp(_snapshot[i], _poseBones[i].localRotation, w);
        }
        _hips.position = Vector3.Lerp(_snapHipsPos, _hips.position, w);
        _hips.rotation = Quaternion.Slerp(_snapHipsRot, _hips.rotation, w);
    }

    /// <summary>
    /// A crippled arm is held against the body, the hand at the belly: it cannot hold the
    /// rifle, and it is not left swinging.
    /// </summary>
    void HoldWoundedArms(float w)
    {
        if (_wounds == null || w <= 0.01f || _hips == null) return;
        float scale = transform.lossyScale.y;
        if (_wounds.AtLeast(BodyPart.LeftArm, EnemyWounds.LimbState.Crippled) && _lUpper != null && _lLower != null && _lHand != null)
        {
            Vector3 at = _hips.position + (transform.forward * 0.2f + Vector3.up * 0.22f - transform.right * 0.03f) * scale;
            SolveWeighted(_lUpper, _lLower, _lHand, at, _lUpper.position + transform.TransformDirection(new Vector3(-0.6f, -0.7f, 0.1f)), w);
        }
        if (_wounds.AtLeast(BodyPart.RightArm, EnemyWounds.LimbState.Crippled) && _rUpper != null && _rLower != null && _rHand != null)
        {
            Vector3 at = _hips.position + (transform.forward * 0.2f + Vector3.up * 0.22f + transform.right * 0.03f) * scale;
            SolveWeighted(_rUpper, _rLower, _rHand, at, _rUpper.position + transform.TransformDirection(new Vector3(0.6f, -0.7f, 0.1f)), w);
        }
    }

    /// <summary>The get-up: every bone from where it fell towards its animated pose.</summary>
    void BlendFromFallen(float t)
    {
        if (_snapshot == null) return;

        // The standing pose the animation wants.
        _animated ??= new Quaternion[_poseBones.Length];
        for (int i = 0; i < _poseBones.Length; i++) _animated[i] = _poseBones[i].localRotation;
        Vector3 standPos = _hips.position;
        Quaternion standRot = _hips.rotation;

        // The pose in between: down on one knee, the weight on a hand -- the animated body
        // lowered and bent over, its legs and a hand put on the ground by IK.
        Kneel(standPos, standRot);
        _kneel ??= new Quaternion[_poseBones.Length];
        for (int i = 0; i < _poseBones.Length; i++) _kneel[i] = _poseBones[i].localRotation;
        Vector3 kneelPos = _hips.position;
        Quaternion kneelRot = _hips.rotation;

        // Off the ground onto the knee and the hand first, a beat there, then up.
        const float onKnee = 0.5f, rising = 0.62f;
        Quaternion[] from, to;
        Vector3 fromPos, toPos;
        Quaternion fromRot, toRot;
        float w;
        if (t < rising)
        {
            from = _snapshot; to = _kneel; fromPos = _snapHipsPos; toPos = kneelPos; fromRot = _snapHipsRot; toRot = kneelRot;
            w = Smooth(Mathf.Clamp01(t / onKnee));
        }
        else
        {
            from = _kneel; to = _animated; fromPos = kneelPos; toPos = standPos; fromRot = kneelRot; toRot = standRot;
            w = Smooth((t - rising) / (1f - rising));
        }

        for (int i = 0; i < _poseBones.Length; i++)
        {
            if (_poseBones[i] == _hips) continue;
            _poseBones[i].localRotation = Quaternion.Slerp(from[i], to[i], w);
        }
        _hips.position = Vector3.Lerp(fromPos, toPos, w);
        _hips.rotation = Quaternion.Slerp(fromRot, toRot, w);

        if (t >= 1f)
        {
            _down = Down.Up;
            if (_ai != null)
            {
                _ai.Downed = false;
                _ai.Recover();
            }
            bool sound = _wounds == null || _wounds.Legs == EnemyWounds.LegState.Sound;
            if (rageSeconds > 0f && sound) StartRage();
            else _wantSlung = false;
        }
    }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    Quaternion[] _animated, _kneel;

    // ======================================================================
    // Pain: where it was hit decides what the body does about it
    // ======================================================================

    enum Pain { None, Leg, Arm, OffArm, Gut, Chest, Head }
    Pain _pain;
    BodyPart _painPart;
    float _painStart, _painLength = 1f, _painStrength;
    float _gunDip, _bodyDrop, _leftOff;

    /// <summary>Doubled up, buckled or clutching a wound: the mind waits on the body.</summary>
    public bool InPain => _pain != Pain.None && Time.time < _painStart + _painLength;

    /// <summary>
    /// Whether it can shoot through it: from the knee once the first shock has passed, yes;
    /// with a shot arm or a round in the gut, not until it is over.
    /// </summary>
    public bool CanFireInPain => _pain == Pain.Leg && Time.time - _painStart > 0.55f
                                 || _pain == Pain.Chest && Time.time - _painStart > 0.3f;

    /// <summary>What the pain looks like, for the debug line.</summary>
    public string PainDoing => _pain switch
    {
        Pain.Leg => Time.time - _painStart > 0.55f ? "firing from one knee" : "leg buckled",
        Pain.Arm => "clutching its arm",
        Pain.OffArm => "arm hanging",
        Pain.Gut => "doubled over",
        Pain.Head => "reeling",
        _ => "staggered",
    };

    /// <summary>
    /// A round lands somewhere: the body answers for that part. A leg buckles and drops it
    /// onto that knee, the other foot planted; an arm jerks off the rifle and the other hand
    /// grabs at it; the gut folds it over; the chest knocks it back a step; the head snaps it
    /// and it reels. A heavier hit hurts for longer. A worse pain is never cut short by a
    /// lighter one.
    /// </summary>
    void Hurt(BodyPart part, float strength)
    {
        if (_ai != null && (_ai.Crawling || _ai.Downed)) return;
        var limb = BodyParts.Limb(part);
        Pain kind = limb switch
        {
            BodyPart.LeftLeg or BodyPart.RightLeg => Pain.Leg,
            BodyPart.RightArm => Pain.Arm,
            BodyPart.LeftArm => Pain.OffArm,
            BodyPart.LowerTorso => Pain.Gut,
            BodyPart.Head or BodyPart.Neck => Pain.Head,
            _ => Pain.Chest,
        };
        if (part == BodyPart.LowerTorso) kind = Pain.Gut;
        // A leg that already limps is the limp clip's to show; the buckle is for a sound leg.
        if (kind == Pain.Leg && _wounds != null && _wounds.Legs != EnemyWounds.LegState.Sound) kind = Pain.Chest;
        // A boss shrugs most of it: only the big hits register, and briefly.
        bool boss = _ai != null && _ai.archetype != null && _ai.archetype.role == EnemyArchetype.Role.Boss;
        if (boss) { if (strength < 0.7f) return; strength *= 0.5f; }

        float length = kind switch
        {
            // A buckle, not a sit-down: a wound in a sound leg is a flinch (the crippled leg is the limp).
            Pain.Leg => 0.45f + 0.35f * strength,
            Pain.Arm or Pain.OffArm => 0.8f + 0.6f * strength,
            Pain.Gut => 0.9f + 0.7f * strength,
            Pain.Head => 0.7f + 0.5f * strength,
            _ => 0.35f + 0.35f * strength,
        };
        if (InPain && Rank(kind) < Rank(_pain)) { _painLength = Mathf.Max(_painLength, Time.time - _painStart + 0.3f); return; }
        if (InPain && kind == _pain) { _painLength = Mathf.Max(_painLength, Time.time - _painStart + length * 0.7f); _painStrength = Mathf.Max(_painStrength, strength); return; }

        _pain = kind;
        _painPart = limb;
        _painStart = Time.time;
        _painLength = length;
        _painStrength = strength;
    }

    static int Rank(Pain p) => p switch { Pain.Leg => 5, Pain.Gut => 4, Pain.Head => 3, Pain.Arm => 2, Pain.OffArm => 2, Pain.Chest => 1, _ => 0 };

    /// <summary>In fast, held, out slower: a wound grabs at once and lets go reluctantly.</summary>
    float PainWeight()
    {
        if (!InPain) { _pain = Pain.None; _gunDip = 0f; _bodyDrop = 0f; _leftOff = 0f; return 0f; }
        float t = Time.time - _painStart;
        float rise = Mathf.Clamp01(t / 0.12f);
        rise = rise * (2f - rise);
        float fall = Mathf.Clamp01((_painStart + _painLength - Time.time) / 0.45f);
        return Mathf.Min(rise, Smooth(fall));
    }

    /// <summary>The body's half of the pain, posed over the animation before the aim.</summary>
    void PainPose(float w)
    {
        _gunDip = 0f; _bodyDrop = 0f; _leftOff = 0f;
        if (w <= 0.001f || _hips == null) return;
        float t = Time.time - _painStart;
        float shock = Mathf.Clamp01(1f - t / 0.5f);   // the first half second, worst
        bool right = _painPart == BodyPart.RightLeg;

        switch (_pain)
        {
            case Pain.Leg:
            {
                // Down onto the shot leg's knee, the good foot planted ahead, upright enough
                // to bring the rifle back up once the shock passes.
                float drop = 0.16f + 0.08f * _painStrength;
                BlendKneel(w, right, drop, 10f + 14f * shock);
                _bodyDrop = drop * w;
                _gunDip = w * (0.25f + 0.6f * shock);
                _leftOff = w * shock;   // a grab at the leg, then back on the rifle
                Turn(_head, transform.right, 20f * shock * w);
                break;
            }
            case Pain.Arm:
                // The gun arm: the rifle sags away, the other hand grabs the arm.
                _gunDip = w * 0.85f;
                _leftOff = w;
                Turn(_spine, transform.up, -14f * w);
                Turn(_chest, transform.right, 10f * w);
                Turn(_head, transform.right, 15f * w * shock);
                break;
            case Pain.OffArm:
                // The support arm: it drops off the rifle and hangs; the rifle held one-handed, low.
                _gunDip = w * 0.55f;
                _leftOff = w;
                Turn(_spine, transform.up, 12f * w);
                Turn(_lUpper, transform.right, -20f * w * shock);
                break;
            case Pain.Gut:
                // Folded over the wound, knees giving, a hand pressed to it.
                Turn(_spine, transform.right, 28f * w);
                Turn(_chest, transform.right, 12f * w);
                Turn(_head, transform.right, 10f * w);
                _hips.position += (Vector3.down * 0.12f - transform.forward * 0.06f) * w * transform.lossyScale.y;
                _bodyDrop = 0.12f * w;
                _gunDip = w * 0.9f;
                _leftOff = w;
                break;
            case Pain.Head:
                // Snapped back and reeling: the knees dip, the head lolls, the aim wanders off.
                Turn(_head, transform.right, -22f * w * shock);
                Turn(_neck, transform.forward, 14f * w * Mathf.Sin(t * 7f));
                Turn(_spine, transform.forward, 6f * w * Mathf.Sin(t * 5f));
                _hips.position += Vector3.down * 0.08f * w * transform.lossyScale.y;
                _bodyDrop = 0.08f * w;
                _gunDip = w * 0.7f;
                break;
            case Pain.Chest:
                Turn(_chest, transform.right, -10f * w * shock);
                _gunDip = w * 0.35f;
                break;
        }
    }

    /// <summary>The kneel at a share: the animated pose bent part of the way towards it.</summary>
    void BlendKneel(float w, bool rightKneeDown, float drop, float tip)
    {
        int n = _poseBones.Length;
        _animated ??= new Quaternion[n];
        for (int i = 0; i < n; i++) _animated[i] = _poseBones[i].localRotation;
        Vector3 pos = _hips.position;
        Quaternion rot = _hips.rotation;

        Kneel(pos, rot, rightKneeDown, handDown: false, drop, tip);

        for (int i = 0; i < n; i++)
            if (_poseBones[i] != _hips) _poseBones[i].localRotation = Quaternion.Slerp(_animated[i], _poseBones[i].localRotation, w);
        _hips.position = Vector3.Lerp(pos, _hips.position, w);
        _hips.rotation = Quaternion.Slerp(rot, _hips.rotation, w);
    }

    /// <summary>The off hand on the wound: the thigh, the arm, the belly; or hanging if it is the wound.</summary>
    void Clutch(float w)
    {
        if (_leftOff <= 0.001f || _lUpper == null || _lLower == null || _lHand == null) return;
        float scale = transform.lossyScale.y;
        Vector3 at;
        switch (_pain)
        {
            case Pain.Leg:
            {
                Transform thigh = _painPart == BodyPart.RightLeg ? _rThigh : _lThigh;
                Transform knee = _painPart == BodyPart.RightLeg ? _rCalf : _lCalf;
                if (thigh == null || knee == null) return;
                at = Vector3.Lerp(thigh.position, knee.position, 0.55f) + transform.forward * 0.08f * scale;
                break;
            }
            case Pain.Arm:
                if (_rUpper == null || _rLower == null) return;
                at = Vector3.Lerp(_rUpper.position, _rLower.position, 0.5f) + transform.forward * 0.05f * scale;
                break;
            case Pain.Gut:
                at = _hips.position + transform.forward * 0.18f * scale + Vector3.up * 0.12f * scale;
                break;
            case Pain.OffArm:
                at = _hips.position - transform.right * 0.28f * scale - Vector3.up * 0.05f * scale;
                break;
            default: return;
        }
        SolveWeighted(_lUpper, _lLower, _lHand, at, _lUpper.position + transform.TransformDirection(new Vector3(-0.6f, -0.6f, 0.1f)), _leftOff);
    }

    /// <summary>
    /// Bends the animated pose into a kneel: the hips down most of the way to the ground and
    /// tipped forward, the left foot planted ahead with the knee up, the right knee on the
    /// ground behind, the right hand flat on the ground ahead taking the weight.
    /// </summary>
    void Kneel(Vector3 hipsPos, Quaternion hipsRot, bool rightKneeDown = true, bool handDown = true, float drop = 0.5f, float tip = 28f)
    {
        float scale = transform.lossyScale.y;
        float ground = transform.position.y;

        _hips.position = hipsPos - Vector3.up * drop * scale;
        _hips.rotation = Quaternion.AngleAxis(tip, transform.right) * hipsRot;
        Turn(_spine, transform.right, handDown ? 12f : -tip * 0.6f);
        Turn(_head, transform.right, handDown ? -25f : -tip * 0.3f);

        // One foot planted ahead with the knee up, the other knee on the ground behind.
        Transform fThigh = rightKneeDown ? _lThigh : _rThigh, fCalf = rightKneeDown ? _lCalf : _rCalf, fFoot = rightKneeDown ? _lFoot : _rFoot;
        Transform bThigh = rightKneeDown ? _rThigh : _lThigh, bCalf = rightKneeDown ? _rCalf : _lCalf, bFoot = rightKneeDown ? _rFoot : _lFoot;
        float fSide = rightKneeDown ? -1f : 1f;
        if (fThigh != null && fCalf != null && fFoot != null)
        {
            Vector3 front = transform.position + transform.forward * 0.35f * scale + transform.right * fSide * 0.12f * scale;
            front.y = ground + 0.08f * scale;
            LimbIK.Solve(fThigh, fCalf, fFoot, front, fThigh.position + transform.forward + Vector3.up);
        }
        if (bThigh != null && bCalf != null && bFoot != null)
        {
            Vector3 back = transform.position - transform.forward * 0.45f * scale - transform.right * fSide * 0.12f * scale;
            back.y = ground + 0.06f * scale;
            LimbIK.Solve(bThigh, bCalf, bFoot, back, bThigh.position + transform.forward - Vector3.up);
        }
        if (handDown && _rUpper != null && _rLower != null && _rHand != null)
        {
            Vector3 palm = transform.position + transform.forward * 0.5f * scale + transform.right * 0.28f * scale;
            palm.y = ground + 0.05f * scale;
            LimbIK.Solve(_rUpper, _rLower, _rHand, palm, _rUpper.position + transform.right - Vector3.up * 0.3f);
        }
    }
}
