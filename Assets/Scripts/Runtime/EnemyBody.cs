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
/// - **Thrown.** A kick hands the bones to physics and throws them (<see cref="Launch"/>);
///   the body flies, lands, lies a moment, and gets up -- blended from wherever it fell
///   into its standing pose -- with the root walked to where it landed.
/// - **Hurt.** A limp dips the hips over the bad leg; a body that can no longer stand goes
///   prone and drags itself; a dead one is the RagdollController's.
///
/// No clip is needed for any of the reactions, the aim, the get-up or the crawl: they are
/// turns on the bones, made in world space, so they work on any Humanoid skeleton.
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

    [Header("Thrown")]
    [Tooltip("Seconds it lies at least, once it has stopped moving.")]
    [Range(0f, 3f)] public float lieSeconds = 0.7f;
    [Tooltip("The longest it can be down before it gets up, settled or not.")]
    [Range(1f, 8f)] public float maxDownSeconds = 3.2f;
    [Tooltip("Seconds the get-up takes, from where it lies to standing.")]
    [Range(0.3f, 2f)] public float getUpSeconds = 0.75f;

    [Header("Rage")]
    [Tooltip("Seconds it fights in a rage after getting up from a kick: rifle slung, fists up, " +
             "straight at the player.")]
    [Range(0f, 20f)] public float rageSeconds = 7f;
    [Tooltip("Seconds of the rage move (the fighter's 'charge fist') before it charges.")]
    [Range(0f, 3f)] public float rageMoveSeconds = 1.25f;

    // ======================================================================

    EnemyAI _ai;
    EnemyBrain _brain;
    Health _health;
    EnemyWounds _wounds;
    RagdollController _ragdoll;
    UnityEngine.AI.NavMeshAgent _agent;

    Transform _hips, _spine, _chest, _neck, _head;
    Transform _lUpper, _lLower, _lHand, _rUpper, _rLower, _rHand;
    Transform _lThigh, _rThigh, _lCalf, _rCalf;
    LimbIK.HandFrame _lFrame, _rFrame;
    Transform[] _poseBones;

    Vector3 _gunLocal;
    Quaternion _gunLocalRot;
    Vector3 _gunLocalScale = Vector3.one;
    float _aimWeight;
    Quaternion _aim = Quaternion.identity;
    bool _gunDropped;

    struct Kick { public Transform bone; public Vector3 axis; public float degrees; public float start; public float length; }
    readonly List<Kick> _kicks = new List<Kick>();

    enum Down { Up, Flying, GettingUp }
    Down _down;
    float _downStart, _stillSince, _getUpStart;
    Quaternion[] _snapshot;
    Vector3 _snapHipsPos;
    Quaternion _snapHipsRot;

    /// <summary>On the ground or getting up from it.</summary>
    public bool IsDown => _down != Down.Up;

    /// <summary>Up from a kick and out for blood: fists up, the rifle slung, straight at the player.</summary>
    public bool Raging => Time.time < _rageUntil && (_health == null || !_health.IsDead);

    /// <summary>Still roaring and squaring up, before the charge.</summary>
    public bool Squaring => Time.time < _rageMoveUntil;

    float _rageUntil = -1f, _rageMoveUntil = -1f, _actionUntil = -1f;
    float _actionWeight;
    int _actionLayer = -1;
    float _lastStrikeWindup = -1f;
    bool _slung;

    // ======================================================================

    void Awake()
    {
        _ai = GetComponent<EnemyAI>();
        _brain = GetComponent<EnemyBrain>();
        _health = GetComponent<Health>();
        _wounds = GetComponent<EnemyWounds>();
        _ragdoll = GetComponent<RagdollController>();
        _agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        if (animator != null && animator.avatar != null && animator.avatar.isHuman)
        {
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
            _hips = B(HumanBodyBones.Hips);
            _spine = B(HumanBodyBones.Spine);
            _chest = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest) ?? _spine;
            _neck = B(HumanBodyBones.Neck);
            _head = B(HumanBodyBones.Head);
            _lUpper = B(HumanBodyBones.LeftUpperArm); _lLower = B(HumanBodyBones.LeftLowerArm); _lHand = B(HumanBodyBones.LeftHand);
            _rUpper = B(HumanBodyBones.RightUpperArm); _rLower = B(HumanBodyBones.RightLowerArm); _rHand = B(HumanBodyBones.RightHand);
            _lThigh = B(HumanBodyBones.LeftUpperLeg); _rThigh = B(HumanBodyBones.RightUpperLeg);
            _lCalf = B(HumanBodyBones.LeftLowerLeg); _rCalf = B(HumanBodyBones.RightLowerLeg);

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

        int pick = Random.Range(0, most);
        foreach (var g in garments)
        {
            if (g == null || g.renderer == null || g.skins == null || g.skins.Length == 0) continue;
            var m = g.skins[Mathf.Min(pick, g.skins.Length - 1)];
            if (m != null) g.renderer.sharedMaterial = m;
        }
    }

    // ======================================================================
    // Hits
    // ======================================================================

    void OnDamaged(Health health, DamageInfo info)
    {
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
        _broken[bone] = side * (BodyParts.IsLeg(limb) ? 28f : 40f);
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
        if (t < 0.12f) { float a = t / 0.12f; return a * (2f - a); }
        float d = (t - 0.12f) / 0.88f;
        return (1f - d) * (1f - d) * (1f + 0.25f * Mathf.Sin(d * Mathf.PI * 2f));
    }

    void OnDied(Health health)
    {
        _kicks.Clear();
        _down = Down.Up;
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
        if (_agent != null) _agent.enabled = false;

        if (gun != null && !_gunDropped && _rHand != null) gun.SetParent(_rHand, true);

        // Thrown, not dropped: the chest takes the boot and the rest follows it, the body
        // turning over as it goes.
        foreach (var body in GetComponentsInChildren<Rigidbody>())
        {
            if (body == null || body.isKinematic) continue;
            bool core = body.transform == _chest || body.transform == _spine || body.transform == _hips;
            body.linearVelocity = velocity * (core ? Random.Range(1.05f, 1.2f) : Random.Range(0.75f, 0.95f));
            body.angularVelocity = Random.insideUnitSphere * 4f + transform.right * Random.Range(3f, 6f);
        }


        _down = Down.Flying;
        _downStart = Time.time;
        _stillSince = -1f;
        if (_ai != null)
        {
            _ai.Downed = true;
            _ai.ForceStagger(maxDownSeconds + getUpSeconds + 0.5f);
        }
        return true;
    }

    void Update()
    {
        if (_down != Down.Flying) return;
        if (_health != null && _health.IsDead) { _down = Down.Up; return; }

        // Settled when the pelvis has stopped moving for a moment; up regardless after the longest.
        var hipsBody = _hips != null ? _hips.GetComponent<Rigidbody>() : null;
        bool still = hipsBody == null || hipsBody.linearVelocity.sqrMagnitude < 0.25f;
        if (still && _stillSince < 0f) _stillSince = Time.time;
        if (!still) _stillSince = -1f;

        float down = Time.time - _downStart;
        if ((down > 0.5f && _stillSince > 0f && Time.time - _stillSince > lieSeconds) || down > maxDownSeconds)
            GetUp();
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
        if (_agent != null)
        {
            _agent.enabled = true;
            _agent.Warp(landed);
        }

        if (gun != null && !_gunDropped)
        {
            gun.SetParent(transform, false);
            gun.localPosition = _gunLocal;
            gun.localRotation = _gunLocalRot;
            gun.localScale = _gunLocalScale;
        }

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
        Sling(true);

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
        _actionWeight = Mathf.MoveTowards(_actionWeight, want, Time.deltaTime * 7f);
        animator.SetLayerWeight(_actionLayer, _actionWeight);

        // In a rage, every blow the brain lands is thrown as a fighter's strike, timed so the
        // blow lands as the wind-up ends.
        if (Raging && _ai != null && _ai.Attacking && _ai.WindupStart > _lastStrikeWindup)
        {
            _lastStrikeWindup = _ai.WindupStart;
            PlayAction(Random.value < 0.5f ? "Strike" : "StrikeB", 0.8f);
        }

        if (!Raging && _slung) Sling(false);
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
        if (_hips == null || _down == Down.Flying) return;
        if (_ragdoll != null && _ragdoll.IsRagdolled) return;

        bool alive = _health == null || !_health.IsDead;
        if (!alive) return;

        UpdateAction();

        bool crawling = _ai != null && _ai.Crawling;
        if (crawling) Crawl();
        else if (_wounds != null && _wounds.Legs == EnemyWounds.LegState.Limping) Limp();

        float up = 1f;
        if (_down == Down.GettingUp)
        {
            float t = Mathf.Clamp01((Time.time - _getUpStart) / Mathf.Max(0.1f, getUpSeconds));
            up = t * t * (3f - 2f * t);
        }

        // A fighting move owns the body while it plays; the rifle is slung during the rage.
        float free = up * (1f - _actionWeight);
        Aim(free);
        if (!_slung) HoldGun(free);
        React();
        ApplyBroken();

        if (_down == Down.GettingUp) BlendFromFallen(up);
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

        Vector3 local = pivot + turn * (rest - pivot) + extra;
        gun.position = transform.TransformPoint(local);
        gun.rotation = transform.rotation * turn;

        // The hands. The gun arm lets go when the arm is too hurt to hold anything.
        float rightHold = weight;
        float leftHold = weight;
        if (_wounds != null)
        {
            if (_wounds.Severity(BodyPart.LeftArm) > 0.85f) leftHold = 0f;
        }

        if (rightHold > 0.01f && rightGrip != null) Reach(_rUpper, _rLower, _rHand, _rFrame, rightGrip, transform.TransformDirection(new Vector3(0.6f, -0.7f, -0.3f)), rightHold);
        if (leftHold > 0.01f && leftGrip != null) Reach(_lUpper, _lLower, _lHand, _lFrame, leftGrip, transform.TransformDirection(new Vector3(-0.5f, -0.8f, 0f)), leftHold);

        if (_wounds != null && _wounds.Disarmed) DropGun(transform.forward * 1.5f + Vector3.up);
    }

    static void Reach(Transform upper, Transform lower, Transform hand, LimbIK.HandFrame frame, Transform grip, Vector3 elbow, float weight)
    {
        if (upper == null || lower == null || hand == null) return;

        Vector3 from = hand.position;
        Vector3 to = Vector3.Lerp(from, grip.position, weight);
        LimbIK.Solve(upper, lower, hand, to, upper.position + elbow);

        if (frame.valid)
            hand.rotation = Quaternion.Slerp(hand.rotation, frame.World(grip.forward, grip.up), weight);
    }

    /// <summary>Lets go of the rifle: it falls as a real object and stays where it lands.</summary>
    void DropGun(Vector3 push)
    {
        if (gun == null || _gunDropped) return;
        _gunDropped = true;

        gun.SetParent(null, true);
        var body = gun.gameObject.GetComponent<Rigidbody>();
        if (body == null) body = gun.gameObject.AddComponent<Rigidbody>();
        body.mass = 3.5f;
        body.isKinematic = false;
        if (gun.GetComponentInChildren<Collider>() == null)
        {
            var box = gun.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.03f, 0.1f);
            box.size = new Vector3(0.06f, 0.18f, 0.75f);
        }
        body.linearVelocity = push;
        body.angularVelocity = Random.insideUnitSphere * 3f;
    }

    /// <summary>The hit reactions: each a turn about its own axis, fading on its own envelope.</summary>
    void React()
    {
        for (int i = _kicks.Count - 1; i >= 0; i--)
        {
            var k = _kicks[i];
            float t = (Time.time - k.start) / k.length;
            if (t >= 1f || k.bone == null) { _kicks.RemoveAt(i); continue; }
            k.bone.rotation = Quaternion.AngleAxis(k.degrees * Envelope(t), k.axis) * k.bone.rotation;
        }
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

    /// <summary>The get-up: every bone from where it fell towards its animated pose.</summary>
    void BlendFromFallen(float w)
    {
        if (_snapshot == null) return;

        Vector3 animatedPos = _hips.position;
        Quaternion animatedRot = _hips.rotation;

        for (int i = 0; i < _poseBones.Length; i++)
        {
            if (_poseBones[i] == _hips) continue;
            _poseBones[i].localRotation = Quaternion.Slerp(_snapshot[i], _poseBones[i].localRotation, w);
        }

        _hips.position = Vector3.Lerp(_snapHipsPos, animatedPos, w);
        _hips.rotation = Quaternion.Slerp(_snapHipsRot, animatedRot, w);

        if (w >= 1f)
        {
            _down = Down.Up;
            if (_ai != null)
            {
                _ai.Downed = false;
                _ai.Recover();
            }
            StartRage();
        }
    }
}
