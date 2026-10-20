using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One of the desert town's people. Wanders between the places people go -- shop fronts, the souk, squares,
/// benches, the mosque plaza -- stands about, and when shooting or an explosion is heard nearby, runs: into the
/// nearest doorway if there is one (and is gone, as people are), otherwise away from it until out of sight.
///
/// Shot like anyone (a <see cref="Health"/> on the body); a death is a casualty the level is scored against
/// (<see cref="CivilianCasualties"/>). Never an enemy: on the Civilian layer, so the aim assist and melee sweeps,
/// which look for the Enemy layer, never pick one. Spawned and recycled round the player by
/// <see cref="CivilianDirector"/>.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class Civilian : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Walking pace in metres per second. People amble; 1.3 is a normal stroll.")]
    public float walkSpeed = 1.3f;

    [Tooltip("Running pace when fleeing. Under the player's sprint, as nothing in the game outruns it.")]
    public float runSpeed = 4.8f;

    [Tooltip("How far a gunshot or a blast is heard from, as a share of its own radius.")]
    public float hearing = 1f;

    [Tooltip("Seconds stood at a spot before walking on: a range, picked fresh each time.")]
    public Vector2 idleTime = new Vector2(5f, 16f);

    [Header("Parts")]
    public Animator animator;
    public Health health;

    public enum State { Idle, Walk, Flee, Dead }
    public State Current { get; private set; }

    /// <summary>True while running from danger: the director does not recycle a fleeing civilian in view.</summary>
    public bool Fleeing => Current == State.Flee;
    public bool Dead => Current == State.Dead;
    /// <summary>Reached a doorway while fleeing: gone inside. The director removes them.</summary>
    public bool Hidden { get; private set; }

    internal CivilianDirector director;

    NavMeshAgent _agent;
    float _until, _nextSense;
    int _heard;
    bool _toDoor;

    static readonly int SpeedId = Animator.StringToHash("Speed");
    static readonly int DieId = Animator.StringToHash("Die");
    static readonly int DieVariantId = Animator.StringToHash("DieVariant");

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        if (health == null) health = GetComponent<Health>();
    }

    void OnEnable()
    {
        _heard = NoiseSystem.Latest;
        _nextSense = Time.time + Random.value * 0.3f;
        if (health != null) health.Died += OnDied;
        Current = State.Idle;
        _until = Time.time + Random.Range(1f, idleTime.y);
    }

    void OnDisable()
    {
        if (health != null) health.Died -= OnDied;
    }

    void Update()
    {
        if (Current == State.Dead) return;

        if (Time.time >= _nextSense)
        {
            _nextSense = Time.time + 0.25f;
            _heard = NoiseSystem.Listen(transform.position + Vector3.up * 1.6f, hearing, _heard, Heard);
        }

        switch (Current)
        {
            case State.Idle:
                if (Time.time >= _until && director != null) WalkTo(director.NextSpot(transform.position));
                break;
            case State.Walk:
                if (Arrived()) Stand();
                break;
            case State.Flee:
                if (Arrived())
                {
                    if (_toDoor) Hidden = true;
                    else Stand();
                }
                break;
        }

        if (animator != null && _agent.enabled)
            animator.SetFloat(SpeedId, _agent.velocity.magnitude, 0.12f, Time.deltaTime);
    }

    void Heard(NoiseSystem.Noise n, float distance)
    {
        if (n.kind != NoiseKind.Gunfire && n.kind != NoiseKind.Blast && n.kind != NoiseKind.Violence) return;
        Alarm(n.position);
    }

    /// <summary>Run from <paramref name="danger"/>: to a doorway if one is near and not towards it, else away.</summary>
    public void Alarm(Vector3 danger)
    {
        if (Current == State.Dead || director == null || !_agent.isOnNavMesh) return;
        if (Current == State.Flee && Time.time < _until) return;
        var (target, door) = director.HideFrom(danger, transform.position);
        _toDoor = door;
        _agent.speed = runSpeed;
        _agent.SetDestination(target);
        Current = State.Flee;
        _until = Time.time + 2f;
    }

    void WalkTo(Vector3 to)
    {
        if (!_agent.isOnNavMesh) return;
        _agent.speed = walkSpeed;
        _agent.SetDestination(to);
        Current = State.Walk;
    }

    void Stand()
    {
        if (_agent.isOnNavMesh) _agent.ResetPath();
        Current = State.Idle;
        _until = Time.time + Random.Range(idleTime.x, idleTime.y);
    }

    bool Arrived() => _agent.isOnNavMesh && !_agent.pathPending && _agent.remainingDistance <= Mathf.Max(1.2f, _agent.stoppingDistance);

    void OnDied(Health h)
    {
        Current = State.Dead;
        if (_agent.isOnNavMesh) _agent.ResetPath();
        _agent.enabled = false;
        if (animator != null)
        {
            animator.SetInteger(DieVariantId, Random.Range(0, 3));
            animator.SetTrigger(DieId);
        }
        CivilianCasualties.Report();
        director?.Died(this);
    }
}
