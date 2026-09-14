using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NavMesh chaser with line-of-sight checks, a wind-up before it commits to an
/// attack, and optional ranged fire. Drop an Animator in and the float/trigger
/// parameters below get driven automatically.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour
{
    public enum State { Idle, Chase, Attack, Dead }

    [Header("Wiring")]
    public Transform eyes;
    public Transform target;

    [Header("Behaviour")]
    public bool ranged;
    public float attackRange = 2f;
    public float attackDamage = 12f;
    public float attackCooldown = 1.3f;

    [Tooltip("Telegraph time before damage lands. Gives the player a chance to back off.")]
    public float attackWindup = 0.35f;

    [Header("Senses")]
    public float detectionRadius = 35f;
    [Range(0f, 360f)] public float fieldOfView = 150f;
    public LayerMask sightBlockers;
    public float loseTargetTime = 6f;
    public float repathInterval = 0.2f;

    [Tooltip("Keep pathing toward the player even with no line of sight. Essential on a " +
             "large map -- otherwise enemies spawn far away, never see you, and stand idle. " +
             "They still need sight or range to actually attack.")]
    public bool relentless = true;

    [Header("Ranged Only")]
    public float rangedSpread = 2.5f;
    public float preferredRangedDistance = 12f;
    public LayerMask rangedHitMask = ~0;

    [Header("Audio (optional)")]
    public AudioClip alertClip;
    public AudioClip attackClip;
    public AudioClip deathClip;

    [Header("Animation (optional)")]
    public Animator animator;
    public string speedParameter = "Speed";
    public string attackTrigger = "Attack";
    public string deathTrigger = "Die";

    public State CurrentState { get; private set; } = State.Idle;

    NavMeshAgent _agent;
    Health _health;
    Health _targetHealth;
    AudioSource _audio;
    Collider[] _colliders;

    float _nextAttackTime;
    float _nextRepathTime;
    float _lastSeenTime = -9999f;
    bool _attacking;
    bool _hasAlerted;

    // ======================================================================
    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _health = GetComponent<Health>();
        _audio = GetComponent<AudioSource>();
        _colliders = GetComponentsInChildren<Collider>();

        if (eyes == null) eyes = transform;
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    void OnEnable()
    {
        if (_health != null) _health.Died += OnDied;
    }

    void OnDisable()
    {
        if (_health != null) _health.Died -= OnDied;
    }

    void Start()
    {
        if (target == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
        }

        if (target != null) _targetHealth = target.GetComponentInParent<Health>();

        if (!_agent.isOnNavMesh)
            Debug.LogWarning($"[EnemyAI] {name} spawned off the NavMesh and cannot move.", this);
    }

    // ======================================================================
    void Update()
    {
        if (CurrentState == State.Dead) return;

        if (target == null || (_targetHealth != null && _targetHealth.IsDead))
        {
            Stop();
            CurrentState = State.Idle;
            UpdateAnimator();
            return;
        }

        bool canSee = CanSeeTarget();
        if (canSee)
        {
            _lastSeenTime = Time.time;
            if (!_hasAlerted)
            {
                _hasAlerted = true;
                PlayClip(alertClip);
            }
        }

        bool aware = relentless || (_hasAlerted && Time.time - _lastSeenTime <= loseTargetTime);

        if (!aware)
        {
            Stop();
            CurrentState = State.Idle;
        }
        else if (!_attacking)
        {
            float distance = Vector3.Distance(transform.position, target.position);
            bool inAttackPosition = ranged
                ? (distance <= attackRange && canSee)
                : distance <= attackRange;

            if (inAttackPosition && Time.time >= _nextAttackTime)
            {
                CurrentState = State.Attack;
                StartCoroutine(AttackRoutine());
            }
            else
            {
                CurrentState = State.Chase;
                MoveTowardTarget(distance);
            }
        }

        if (CurrentState == State.Attack || (_attacking && target != null))
            FaceTarget();

        UpdateAnimator();
    }

    // ======================================================================
    void MoveTowardTarget(float distance)
    {
        if (!_agent.isOnNavMesh) return;
        if (Time.time < _nextRepathTime) return;

        _nextRepathTime = Time.time + repathInterval;
        _agent.isStopped = false;

        // Ranged units hold a firing line instead of walking into melee.
        if (ranged && distance < preferredRangedDistance * 0.6f)
        {
            Vector3 away = (transform.position - target.position).normalized;
            Vector3 retreat = transform.position + away * 3f;

            if (NavMesh.SamplePosition(retreat, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                _agent.SetDestination(hit.position);
                return;
            }
        }

        _agent.SetDestination(target.position);
    }

    void Stop()
    {
        if (_agent.isOnNavMesh && !_agent.isStopped) _agent.isStopped = true;
    }

    void FaceTarget()
    {
        Vector3 flat = target.position - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.001f) return;

        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(flat), 10f * Time.deltaTime);
    }

    // ======================================================================
    bool CanSeeTarget()
    {
        if (target == null) return false;

        Vector3 origin = eyes.position;
        Vector3 targetPoint = target.position + Vector3.up * 1.4f;
        Vector3 toTarget = targetPoint - origin;

        if (toTarget.magnitude > detectionRadius) return false;
        if (Vector3.Angle(transform.forward, toTarget) > fieldOfView * 0.5f && !_hasAlerted) return false;

        // Anything on the blocker layers between us breaks the line.
        return !Physics.Linecast(origin, targetPoint, sightBlockers, QueryTriggerInteraction.Ignore);
    }

    // ======================================================================
    IEnumerator AttackRoutine()
    {
        _attacking = true;
        Stop();

        if (animator != null && !string.IsNullOrEmpty(attackTrigger))
            animator.SetTrigger(attackTrigger);

        PlayClip(attackClip);

        yield return new WaitForSeconds(attackWindup);

        if (CurrentState != State.Dead && target != null && _targetHealth != null && !_targetHealth.IsDead)
        {
            if (ranged) FireRangedShot();
            else TryMeleeHit();
        }

        _nextAttackTime = Time.time + attackCooldown;
        _attacking = false;
    }

    void TryMeleeHit()
    {
        // Re-check the range -- backing off during the wind-up should work.
        float distance = Vector3.Distance(transform.position, target.position);
        if (distance > attackRange * 1.25f) return;

        _targetHealth.ApplyDamage(new DamageInfo(
            attackDamage, target.position, Vector3.up,
            (target.position - transform.position).normalized, gameObject));
    }

    void FireRangedShot()
    {
        Vector3 origin = eyes.position;
        Vector3 direction = ((target.position + Vector3.up * 1.2f) - origin).normalized;

        Vector2 offset = Random.insideUnitCircle * rangedSpread;
        direction = Quaternion.AngleAxis(offset.x, Vector3.up) *
                    Quaternion.AngleAxis(offset.y, transform.right) * direction;

        if (!Physics.Raycast(origin, direction, out RaycastHit hit, detectionRadius,
                             rangedHitMask, QueryTriggerInteraction.Ignore)) return;

        var info = new DamageInfo(attackDamage, hit.point, hit.normal, direction, gameObject);

        var hitbox = hit.collider.GetComponent<Hitbox>();
        if (hitbox != null) hitbox.Receive(info);
        else
        {
            var health = hit.collider.GetComponentInParent<Health>();
            if (health != null && health != _health) health.ApplyDamage(info);
        }
    }

    // ======================================================================
    void OnDied(Health health)
    {
        CurrentState = State.Dead;
        _attacking = false;
        StopAllCoroutines();

        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
            _agent.enabled = false;
        }

        foreach (var col in _colliders)
            if (col != null) col.enabled = false;

        if (animator != null && !string.IsNullOrEmpty(deathTrigger))
            animator.SetTrigger(deathTrigger);

        PlayClip(deathClip);
        enabled = false;
    }

    void UpdateAnimator()
    {
        if (animator == null || string.IsNullOrEmpty(speedParameter)) return;

        float speed = _agent != null && _agent.isOnNavMesh ? _agent.velocity.magnitude : 0f;
        animator.SetFloat(speedParameter, speed);
    }

    void PlayClip(AudioClip clip)
    {
        if (clip == null || _audio == null) return;

        _audio.pitch = Random.Range(0.94f, 1.06f);
        _audio.PlayOneShot(clip);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
