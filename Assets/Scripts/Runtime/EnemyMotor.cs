using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Moves a hurt body by its animation, with the NavMeshAgent following.
///
/// A sound enemy is still walked by its agent (the walk blend tree is matched to the agent's
/// speed). Once it limps, crawls or gets up off the ground, the clip moves it instead: the
/// agent plans the path and says which way, the animator says how far -- each frame's root
/// motion is laid onto the navmesh through <see cref="NavMeshAgent.nextPosition"/>, and the
/// agent's speed is set to what the clip actually covered. So a limp is exactly as fast as
/// the limp animation and the feet never slide, whatever the mind asked for; the mind only
/// picks the gait (<see cref="EnemyAI.RequestedPace"/>: a walk, or a hobbling run).
///
/// It turns the body at a hurt body's rate before it goes, and picks the backwards clip when
/// it is backing away from someone it is fighting. EnemyBody decides when it is in charge
/// (<see cref="SetRootMotion"/>). The Animator's OnAnimatorMove reaches it through
/// <see cref="EnemyRootMotion"/> on the body.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(50)] // after EnemyAI and EnemyBrain have chosen where to go this frame
public class EnemyMotor : MonoBehaviour
{
    public enum Gait { Limp, CrawlDown, CrawlUp, Still }

    [Header("Turning")]
    [Tooltip("Degrees a second it turns while limping.")]
    [Range(30f, 360f)] public float limpTurnRate = 140f;
    [Tooltip("Degrees a second it turns on the ground.")]
    [Range(10f, 180f)] public float crawlTurnRate = 55f;
    [Tooltip("Degrees off its way beyond which it turns on the spot before going on.")]
    [Range(20f, 120f)] public float turnFirstAngle = 70f;

    [Header("Pace")]
    [Tooltip("Seconds the animator's pace takes to settle: how gently it starts and stops.")]
    [Range(0.05f, 1f)] public float paceDamping = 0.3f;
    [Tooltip("The mind's pace (share of full speed) at which a limp becomes a hobbling run.")]
    [Range(0.5f, 1.5f)] public float runPaceShare = 0.95f;
    [Tooltip("Degrees between its way and the threat beyond which it backs off facing the threat.")]
    [Range(90f, 180f)] public float backOffAngle = 125f;

    /// <summary>The clip is moving the body, not the agent.</summary>
    public bool RootMotion { get; private set; }

    public Gait Current { get; private set; } = Gait.Still;

    /// <summary>Metres a second the clip covered, smoothed. The agent's speed while in charge.</summary>
    public float MeasuredSpeed { get; private set; }

    NavMeshAgent _agent;
    EnemyAI _ai;
    EnemyBrain _brain;
    Animator _animator;
    static readonly int Pace = Animator.StringToHash("Pace");

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _ai = GetComponent<EnemyAI>();
        _brain = GetComponent<EnemyBrain>();
        _animator = GetComponentInChildren<Animator>();
    }

    /// <summary>Hands the movement to the clip (true) or back to the agent (false).</summary>
    public void SetRootMotion(bool on, Gait gait)
    {
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

    void Update()
    {
        if (!RootMotion || _animator == null) return;
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

        _animator.SetFloat(Pace, pace, paceDamping, dt);
    }

    bool InFight(out Vector3 threat)
    {
        threat = Vector3.zero;
        if (_ai == null || _ai.target == null) return false;
        if (_brain != null && _brain.Mind != EnemyBrain.Awareness.Combat) return false;
        threat = _ai.target.position - transform.position;
        threat.y = 0f;
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

        MeasuredSpeed = Mathf.Lerp(MeasuredSpeed, delta.magnitude / dt, Mathf.Clamp01(dt * 8f));
        if (_agent != null && _agent.enabled) _agent.speed = Mathf.Max(0.05f, MeasuredSpeed);
    }
}
