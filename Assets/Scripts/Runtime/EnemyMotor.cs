using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Moves the body by its animation, with the NavMeshAgent following.
///
/// The agent plans the path and says which way; the clip says how far. Each frame's root
/// motion is laid onto the navmesh through <see cref="NavMeshAgent.nextPosition"/>, so the feet
/// and the ground always agree and nothing slides, whatever speed the mind asked for. The mind
/// picks the pace (<see cref="EnemyAI.RequestedPace"/>); the motor turns that into a velocity
/// for the body's eight-way blend (walk, run, sprint, crouched), damped so it eases into a
/// stride and out of one. From standing into a run it plays a start; pulling up out of a run, a
/// stop; turning a long way on the spot, a quarter turn.
///
/// It owns the turn, at a body's rate: towards where it is going, or towards what it is
/// fighting (strafing and backing off with the rifle up), or towards whatever the mind last
/// asked it to face (<see cref="Face"/>). The head and chest get there first (EnemyBody's look
/// targets); the body follows.
///
/// Hurt gaits (limp, crawl) and the get-ups are the "Injured" layer's (<see cref="Gait"/>);
/// EnemyBody hands each in with <see cref="SetRootMotion"/>. The Animator's OnAnimatorMove
/// reaches it through <see cref="EnemyRootMotion"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(50)] // after EnemyAI and EnemyBrain have chosen where to go this frame
public class EnemyMotor : MonoBehaviour
{
    public enum Gait { Normal, Limp, CrawlDown, CrawlUp, Still }

    [Header("Turning")]
    [Tooltip("Degrees a second it turns on the move.")]
    [Range(30f, 720f)] public float turnRate = 260f;
    [Tooltip("Degrees a second it turns standing, short of a quarter-turn step.")]
    [Range(30f, 360f)] public float standTurnRate = 110f;
    [Tooltip("Degrees off its facing beyond which, standing, it steps round with a quarter-turn clip.")]
    [Range(30f, 150f)] public float turnStepAngle = 70f;
    [Tooltip("Degrees a second it turns while limping.")]
    [Range(30f, 360f)] public float limpTurnRate = 140f;
    [Tooltip("Degrees a second it turns on the ground.")]
    [Range(10f, 180f)] public float crawlTurnRate = 55f;
    [Tooltip("Degrees off its way beyond which a hurt body turns on the spot before going on.")]
    [Range(20f, 120f)] public float turnFirstAngle = 70f;

    [Header("Pace")]
    [Tooltip("Seconds the blend's velocity takes to settle: how gently it starts, stops and changes step.")]
    [Range(0.05f, 1f)] public float moveDamping = 0.22f;
    [Tooltip("Seconds the hurt gaits' pace takes to settle.")]
    [Range(0.05f, 1f)] public float paceDamping = 0.3f;
    [Tooltip("The mind's pace (share of full speed) at which a limp becomes a hobbling run.")]
    [Range(0.5f, 1.5f)] public float runPaceShare = 0.95f;
    [Tooltip("Degrees between its way and the threat beyond which it backs off facing the threat.")]
    [Range(90f, 180f)] public float backOffAngle = 125f;
    [Tooltip("Metres a second (at the clips' size) above which it is running, for the start and stop clips.")]
    [Range(2f, 6f)] public float runSpeed = 3.5f;

    [Header("Life")]
    [Tooltip("Seconds between changes of idle when calm: picked between these.")]
    public Vector2 idleChange = new Vector2(7f, 18f);
    [Tooltip("How much its animation speed differs from the next soldier's, either way.")]
    [Range(0f, 0.15f)] public float tempoSpread = 0.05f;

    [Header("Culling")]
    [Tooltip("Metres from the player within which the body is always animated, seen or not. Culled when " +
             "unseen, its bones (and the hitboxes on them) froze where it was last seen while it walked on: " +
             "a blast or a strike aimed at where it really was found nothing. Further off, unseen bodies rest.")]
    [Min(0f)] public float animateAlwaysWithin = 35f;

    /// <summary>The clip is moving the body, not the agent.</summary>
    public bool RootMotion { get; private set; }

    public Gait Current { get; private set; } = Gait.Still;

    /// <summary>Metres a second the clip covered, smoothed.</summary>
    public float MeasuredSpeed { get; private set; }

    /// <summary>Set while it crouches to pick something up.</summary>
    [System.NonSerialized] public bool ForceCrouch;

    /// <summary>Using the pistol: the pistol's movement set, left-handed if mirrored.</summary>
    [System.NonSerialized] public int PistolHand;   // 0 rifle, 1 right hand, -1 left hand

    NavMeshAgent _agent;
    EnemyAI _ai;
    EnemyBrain _brain;
    EnemyWounds _wounds;
    Animator _animator;
    bool _hasMoveSet;
    Vector3 _faceAt;
    float _faceUntil = -1f;
    float _nextIdle, _idle = 1f;
    string _state = "Idle";
    float _stateUntil = -1f;
    float _lastSpeed;
    bool _turning;

    static readonly int Pace = Animator.StringToHash("Pace");
    static readonly int MoveX = Animator.StringToHash("MoveX");
    static readonly int MoveZ = Animator.StringToHash("MoveZ");
    static readonly int CrouchId = Animator.StringToHash("Crouch");
    static readonly int IdleVariant = Animator.StringToHash("IdleVariant");
    static readonly int AimId = Animator.StringToHash("Aim");
    static readonly int MoveRate = Animator.StringToHash("MoveRate");
    static readonly int CrawlRate = Animator.StringToHash("CrawlRate");

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _ai = GetComponent<EnemyAI>();
        _brain = GetComponent<EnemyBrain>();
        _wounds = GetComponent<EnemyWounds>();
        _animator = GetComponentInChildren<Animator>();
        if (_animator != null)
            foreach (var p in _animator.parameters) if (p.nameHash == MoveX) _hasMoveSet = true;
    }

    void Start()
    {
        if (_animator == null) return;
        // No two in step: each its own tempo, its own place in the cycle, its own idle.
        _animator.speed = 1f + Random.Range(-tempoSpread, tempoSpread);
        _animator.Play(0, 0, Random.value);
        _idle = Random.Range(1, 5);
        _nextIdle = Time.time + Random.Range(idleChange.x, idleChange.y);
        if (_hasMoveSet) SetRootMotion(true, Gait.Normal);
    }

    /// <summary>
    /// Hands the movement to the clip (true) or back to the agent (false), in a gait. A body
    /// with the eight-way set stays on root motion in Normal; one without it goes back to the agent.
    /// </summary>
    public void SetRootMotion(bool on, Gait gait)
    {
        if (!on && _hasMoveSet) { on = true; gait = Gait.Normal; }
        Current = gait;
        if (on == RootMotion) return;
        RootMotion = on;
        if (_agent == null) return;

        if (on)
        {
            _agent.updatePosition = false;
            _agent.updateRotation = false;
            if (_agent.enabled && _agent.isOnNavMesh) _agent.nextPosition = transform.position;
        }
        else
        {
            // Agree on where it is first, and start the agent from rest: its own simulation kept
            // running while the clip was in charge, and its velocity would jump the body.
            if (_agent.enabled && _agent.isOnNavMesh)
            {
                _agent.nextPosition = transform.position;
                _agent.velocity = Vector3.zero;
            }
            _agent.updatePosition = true;
            _agent.updateRotation = true;
        }
    }

    /// <summary>The mind wants it facing this point (aiming, squaring up): honoured for a moment.</summary>
    public void Face(Vector3 point)
    {
        _faceAt = point;
        _faceUntil = Time.time + 0.4f;
    }

    Transform _player;
    float _nextCullCheck;

    /// <summary>Always animated near the player, culled when unseen further off (checked four times a second).</summary>
    void UpdateCulling()
    {
        if (_animator == null || Time.time < _nextCullCheck) return;
        _nextCullCheck = Time.time + 0.25f;
        if (_player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            _player = p != null ? p.transform : null;
        }
        bool near = _player != null && (_player.position - transform.position).sqrMagnitude < animateAlwaysWithin * animateAlwaysWithin;
        var mode = near ? AnimatorCullingMode.AlwaysAnimate : AnimatorCullingMode.CullUpdateTransforms;
        if (_animator.cullingMode != mode) _animator.cullingMode = mode;
    }

    void Update()
    {
        UpdateCulling();
        if (!RootMotion || _animator == null || !_animator.enabled) return;
        if (Current == Gait.Normal) Normal();
        else Hurt();
    }

    // ======================================================================
    // Sound legs: the eight-way set
    // ======================================================================

    void Normal()
    {
        float dt = Time.deltaTime;
        bool ready = _agent != null && _agent.enabled && _agent.isOnNavMesh;
        bool moving = ready && _agent.hasPath && !_agent.isStopped && !_agent.pathPending
                      && _agent.remainingDistance > _agent.stoppingDistance + 0.05f;
        float scale = Mathf.Max(0.01f, transform.lossyScale.y);

        // How fast it wants to go, in metres a second at the clips' size.
        float speed = 0f;
        Vector3 way = Vector3.zero;
        if (moving)
        {
            way = _agent.steeringTarget - transform.position;
            way.y = 0f;
            if (way.sqrMagnitude > 0.0004f)
            {
                way.Normalize();
                float pace = _ai != null ? _ai.RequestedPace : 0.55f;
                float legs = _wounds != null ? _wounds.MoveSpeedMultiplier : 1f;
                speed = (_ai != null ? _ai.BaseSpeed : 3.2f) * pace * legs / scale;
            }
            else moving = false;
        }

        // What it faces: what it was told to, then what it is fighting, then its way.
        Vector3 face = transform.forward;
        if (Time.time < _faceUntil) face = Flat(_faceAt - transform.position);
        else if (InFight(out Vector3 threat)) face = threat;
        else if (moving) face = way;
        if (face.sqrMagnitude < 1e-4f) face = transform.forward;
        face.Normalize();
        float off = Vector3.SignedAngle(transform.forward, face, Vector3.up);

        // Standing and a long way round: a quarter-turn step, the clip's own turn.
        if (!moving && !_turning && Mathf.Abs(off) > turnStepAngle && _state == "Idle")
        {
            bool crouched = _animator.GetFloat(CrouchId) > 0.5f;
            Play(off < 0f ? (crouched ? "CrouchTurnLeft" : "TurnLeft") : (crouched ? "CrouchTurnRight" : "TurnRight"), 0.85f);
            _turning = true;
        }
        if (!_turning)
        {
            float rate = moving ? turnRate : standTurnRate;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face), rate * dt);
        }

        // The body's velocity in its own space, for the eight-way blend.
        Vector3 local = transform.InverseTransformDirection(way * speed);
        _animator.SetFloat(MoveX, local.x, moveDamping, dt);
        _animator.SetFloat(MoveZ, local.z, moveDamping, dt);
        _animator.SetFloat(MoveRate, Mathf.Max(1f, speed / 6.8f), 0.3f, dt);

        bool alert = _brain != null ? _brain.WeaponUp : (_ai != null && _ai.HasSpotted);
        bool crouch = ForceCrouch || (_ai != null && _ai.WantCrouch);
        _animator.SetFloat(CrouchId, crouch ? 1f : 0f, 0.25f, dt);
        _animator.SetFloat(AimId, alert ? 1f : 0f, 0.3f, dt);

        // Calm, it changes idle now and then; alert, rifle up.
        if (Time.time >= _nextIdle)
        {
            _idle = Random.Range(1, 5);
            _nextIdle = Time.time + Random.Range(idleChange.x, idleChange.y);
        }
        _animator.SetFloat(IdleVariant, alert ? 0f : _idle, 0.6f, dt);

        // Into a run from standing, and pulling up out of one.
        if (!_turning && PistolHand == 0 && !crouch)
        {
            float ahead = Vector3.Dot(way, transform.forward);
            if (_state == "Idle" && speed > runSpeed && ahead > 0.85f) Play("RunStart", 0.55f);
            else if (_state == "Move" && _lastSpeed > runSpeed && speed < 0.3f) Play("RunStop", 0.8f);
        }
        _lastSpeed = speed;

        // Standing still is its own state (the idles at their own pace); moving is the blend.
        // A special state (a turn, a start, a stop) hands back when it has done its work.
        float blend = new Vector2(_animator.GetFloat(MoveX), _animator.GetFloat(MoveZ)).magnitude;
        bool still = _state == Idle() ? speed < 0.05f : !moving && blend < 0.15f;
        string want = still ? Idle() : Base();
        if (_state != want && (_stateUntil < 0f || Time.time >= _stateUntil)) Play(want, -1f);
    }

    string Base() => PistolHand == 0 ? "Move" : PistolHand > 0 ? "Pistol" : "PistolLeft";
    // Standing still is the same state with any weapon: the Carry layer gives the arms.
    string Idle() => "Idle";

    /// <summary>Crossfades the base layer into a state; a special one for this many seconds.</summary>
    void Play(string state, float seconds)
    {
        _animator.CrossFadeInFixedTime(state, 0.2f, 0);
        _state = state;
        _stateUntil = seconds > 0f ? Time.time + seconds : -1f;
        _turning = state.Contains("Turn");
    }

    // ======================================================================
    // Hurt: limp and crawl on the Injured layer
    // ======================================================================

    void Hurt()
    {
        float dt = Time.deltaTime;
        float pace = 0f;

        bool ready = _agent != null && _agent.enabled && _agent.isOnNavMesh;
        bool moving = ready && Current != Gait.Still && _agent.hasPath && !_agent.isStopped && !_agent.pathPending
                      && _agent.remainingDistance > _agent.stoppingDistance + 0.05f;

        if (moving)
        {
            Vector3 way = _agent.steeringTarget - transform.position;
            way.y = 0f;
            if (way.sqrMagnitude > 0.0004f)
            {
                way.Normalize();
                pace = Current == Gait.Limp && _ai != null && _ai.RequestedPace >= runPaceShare ? 2f : 1f;
                Vector3 face = way;

                // Pushing itself away on its back goes feet first, facing what it fears.
                if (Current == Gait.CrawlUp) face = -way;

                // Backing off from someone it is fighting: eyes on them, the backwards clip.
                if (Current != Gait.CrawlUp && InFight(out Vector3 threat) && Vector3.Angle(way, threat) > backOffAngle)
                {
                    face = threat;
                    pace = -1f;
                }

                float rate = Current == Gait.Limp ? limpTurnRate : crawlTurnRate;
                float off = Vector3.Angle(transform.forward, face);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face), rate * dt);

                // A hurt body turns, then goes; it does not drag itself off sideways.
                if (off > turnFirstAngle) pace *= 0.25f;
            }
        }
        else if (Current != Gait.Still && Time.time < _faceUntil)
        {
            // Still, it turns to what it was asked to face, slowly.
            Vector3 face = Flat(_faceAt - transform.position);
            if (Current == Gait.CrawlUp) face = face.sqrMagnitude > 1e-4f ? face : transform.forward;
            float rate = Current == Gait.Limp ? limpTurnRate : crawlTurnRate;
            if (face.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face), rate * 0.6f * dt);
        }

        _animator.SetFloat(Pace, pace, paceDamping, dt);
        // On its back the crawl is one clip at a rate: barely moving when still, so it shifts
        // and struggles rather than lying like a corpse.
        _animator.SetFloat(CrawlRate, Mathf.Max(0.15f, Mathf.Abs(pace)), paceDamping, dt);
        // The base layer under the hurt one stands still, so the blend between them never walks.
        _animator.SetFloat(MoveX, 0f, moveDamping, dt);
        _animator.SetFloat(MoveZ, 0f, moveDamping, dt);
    }

    bool InFight(out Vector3 threat)
    {
        threat = Vector3.zero;
        if (_ai == null || _ai.target == null) return false;
        if (_brain != null && (_brain.Mind != EnemyBrain.Awareness.Combat || !_brain.WeaponUp)) return false;
        threat = Flat(_ai.target.position - transform.position);
        if (threat.sqrMagnitude < 0.01f) return false;
        threat.Normalize();
        return true;
    }

    /// <summary>The Animator has worked out this frame's root motion (from EnemyRootMotion).</summary>
    public void AnimatorMoved()
    {
        if (!RootMotion || _animator == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 delta = _animator.deltaPosition;
        delta.y = 0f;
        // Still on the ground, the slow shifting clip must not creep the body along.
        if ((Current == Gait.CrawlUp || Current == Gait.CrawlDown) && Mathf.Abs(_animator.GetFloat(Pace)) < 0.2f) delta = Vector3.zero;

        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            // Laid onto the mesh: the agent keeps it on walkable ground and in the crowd.
            _agent.nextPosition = transform.position + delta;
            transform.position = _agent.nextPosition;
        }
        else
        {
            transform.position += delta;
        }

        // A quarter-turn step turns the body by the clip's own turn.
        if (_turning) transform.rotation = Quaternion.Euler(0f, _animator.deltaRotation.eulerAngles.y, 0f) * transform.rotation;

        MeasuredSpeed = Mathf.Lerp(MeasuredSpeed, delta.magnitude / dt, Mathf.Clamp01(dt * 8f));
        // A hurt gait's speed is the clip's, and the agent is told so; a sound one keeps the
        // speed the mind asked for, which the blend is matching.
        if (_agent != null && _agent.enabled && Current != Gait.Normal) _agent.speed = Mathf.Max(0.05f, MeasuredSpeed);
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
