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

    public enum State { Idle, Walk, Flee, Down, Dead }
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
    static readonly int ActivityId = Animator.StringToHash("Activity");
    static readonly int LimpId = Animator.StringToHash("Limp");
    static readonly int HitId = Animator.StringToHash("Hit");
    static readonly int HitVariantId = Animator.StringToHash("HitVariant");
    static readonly int GetUpId = Animator.StringToHash("GetUp");
    static readonly int GetUpFaceId = Animator.StringToHash("GetUpFace");

    RagdollController _ragdoll;
    bool _limping;

    /// <summary>A leg has been hit: they limp for good, at about half pace (as a wounded soldier does).</summary>
    public bool Limping => _limping;

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        if (health == null) health = GetComponent<Health>();
        _ragdoll = GetComponent<RagdollController>();
    }

    void OnEnable()
    {
        _heard = NoiseSystem.Latest;
        _nextSense = Time.time + Random.value * 0.3f;
        if (health != null) { health.Died += OnDied; health.Damaged += OnDamaged; }
        Current = State.Idle;
        _until = Time.time + Random.Range(1f, idleTime.y);
    }

    void OnDisable()
    {
        if (health != null) { health.Died -= OnDied; health.Damaged -= OnDamaged; }
    }

    /// <summary>
    /// Hurt and still alive (2026-11-09: the town's people as real as the enemies): a flinch at every hit, a limp for good from a hit in
    /// the leg, and running from whoever did it. A body thrown by a car or a blast is handled by <see cref="KnockDown"/>.
    /// </summary>
    void OnDamaged(Health h, DamageInfo info)
    {
        if (Current == State.Dead || Current == State.Down || h.IsDead) return;
        if (animator != null && animator.enabled)
        {
            animator.SetInteger(HitVariantId, info.amount > h.maxHealth * 0.3f ? 1 : 0);
            animator.SetTrigger(HitId);
        }
        if (BodyParts.IsLeg(info.part)) Limp();
        Alarm(info.source != null ? info.source.transform.position : transform.position - transform.forward * 5f);
    }

    void Limp()
    {
        if (_limping) return;
        _limping = true;
        if (animator != null) animator.SetBool(LimpId, true);
    }

    float Pace(float speed) => _limping ? speed * 0.45f : speed;

    /// <summary>
    /// Thrown off their feet (a car, a blast): the ragdoll takes them at <paramref name="thrown"/> (VehicleStrike.Throw), and if
    /// they live they get up off the road, from the back or the front as they lie, hurt and limping, and run.
    /// </summary>
    public void KnockDown(Vector3 thrown, float lift, Vector3 danger)
    {
        if (Current == State.Dead || _ragdoll == null) return;
        if (_agent.isOnNavMesh) _agent.ResetPath();
        _agent.enabled = false;
        Current = State.Down;
        _ragdoll.SetRagdollActive(true);
        VehicleStrike.Throw(GetComponentsInChildren<Rigidbody>(), thrown, lift);
        StopAllCoroutines();
        StartCoroutine(GetUpAfter(danger));
    }

    System.Collections.IEnumerator GetUpAfter(Vector3 danger)
    {
        var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        var hipsBody = hips != null ? hips.GetComponent<Rigidbody>() : null;
        float from = Time.time;
        // Down until the body has come to rest (or four seconds), then a moment lying there.
        yield return new WaitForSeconds(0.8f);
        while (Time.time - from < 4f && hipsBody != null && hipsBody.linearVelocity.magnitude > 0.25f) yield return null;
        yield return new WaitForSeconds(Random.Range(0.6f, 1.4f));
        if (Current == State.Dead || health == null || health.IsDead || hips == null) yield break;
        // The person stands where the hips lie, facing along the body; face up or face down picks the clip.
        bool faceUp = Vector3.Dot(hips.forward, Vector3.up) > 0f;
        Vector3 along = Vector3.ProjectOnPlane(hips.up, Vector3.up);
        if (along.sqrMagnitude < 0.01f) along = transform.forward;
        Vector3 at = hips.position;
        if (NavMesh.SamplePosition(at, out var ground, 2f, NavMesh.AllAreas)) at = ground.position;
        else if (Physics.Raycast(hips.position + Vector3.up, Vector3.down, out var hit, 4f, ~LayerMask.GetMask("Civilian", "Enemy", "Player"), QueryTriggerInteraction.Ignore)) at = hit.point;
        _ragdoll.SetRagdollActive(false);
        transform.SetPositionAndRotation(at, Quaternion.LookRotation(faceUp ? -along.normalized : along.normalized, Vector3.up));
        if (animator != null)
        {
            animator.Rebind();
            animator.Update(0f);
            animator.SetInteger(GetUpFaceId, faceUp ? 0 : 1);
            animator.SetTrigger(GetUpId);
        }
        Limp();
        yield return new WaitForSeconds(2.3f);
        if (Current == State.Dead || health == null || health.IsDead) yield break;
        _agent.enabled = true;
        if (_agent.isOnNavMesh) _agent.Warp(transform.position);
        Current = State.Idle;
        Alarm(danger);
    }

    void Update()
    {
        if (Current == State.Dead || Current == State.Down) return;

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
        if (Current == State.Dead || Current == State.Down || director == null || !_agent.isOnNavMesh) return;
        if (Current == State.Flee && Time.time < _until) return;
        if (animator != null) animator.SetInteger(ActivityId, 0);
        var (target, door) = director.HideFrom(danger, transform.position);
        _toDoor = door;
        _agent.speed = Pace(runSpeed);
        _agent.SetDestination(target);
        Current = State.Flee;
        _until = Time.time + 2f;
    }

    void WalkTo(Vector3 to)
    {
        if (!_agent.isOnNavMesh) return;
        if (animator != null) animator.SetInteger(ActivityId, 0);
        _agent.speed = Pace(walkSpeed);
        _agent.SetDestination(to);
        Current = State.Walk;
    }

    /// <summary>What someone does while stood about: on the phone, talking, looking in at something. Only those a clip was made for.</summary>
    static LifeActivity StoodActivity() => Random.Range(0, 4) switch { 0 => LifeActivity.Phone, 3 => LifeActivity.Browse, _ => LifeActivity.Talk };

    void Stand()
    {
        if (_agent.isOnNavMesh) _agent.ResetPath();
        if (animator != null && Random.value < 0.75f) animator.SetInteger(ActivityId, (int)StoodActivity());
        Current = State.Idle;
        _until = Time.time + Random.Range(idleTime.x, idleTime.y);
    }

    bool Arrived() => _agent.isOnNavMesh && !_agent.pathPending && _agent.remainingDistance <= Mathf.Max(1.2f, _agent.stoppingDistance);

    void OnDied(Health h)
    {
        Current = State.Dead;
        StopAllCoroutines();
        if (_agent.isOnNavMesh) _agent.ResetPath();
        _agent.enabled = false;
        // A body with a ragdoll falls as the enemies do (RagdollController, on the same death); the old death clips only without one.
        if (animator != null && _ragdoll == null)
        {
            animator.SetInteger(DieVariantId, Random.Range(0, 3));
            animator.SetTrigger(DieId);
        }
        if (CivilianCasualties.ByPlayer(h.LastDamage.source)) CivilianCasualties.Report();   // an enemy's round, the truck's or a charge is not his to answer for
        director?.Died(this);
    }
}
