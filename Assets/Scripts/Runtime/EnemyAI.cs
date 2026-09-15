using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NavMesh hunter with line-of-sight checks, a telegraphed wind-up before it
/// commits, ranged fire, crowd separation, strafing and a stagger reaction.
///
/// The behaviour is deliberately layered rather than scripted per enemy type: an
/// <see cref="EnemyArchetype"/> stamps numbers onto these knobs at spawn, and
/// <see cref="ApplyWaveTuning"/> sharpens them as the waves climb. One brain,
/// many enemies, no per-variant code.
///
/// Drop an Animator in and the float/trigger parameters below get driven automatically.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour
{
    public enum State { Idle, Chase, Attack, Stagger, Dead }

    [Header("Wiring")]
    public Transform eyes;
    public Transform target;

    [Tooltip("Assigned by the WaveManager at spawn. Read for score value and drops; " +
             "leave empty on a hand-placed enemy and it simply uses the fields below.")]
    public EnemyArchetype archetype;

    [Header("Behaviour")]
    public bool ranged;
    public float attackRange = 2f;
    public float attackDamage = 12f;
    public float attackCooldown = 1.3f;

    [Tooltip("Telegraph time before damage lands. This is the player's reaction window, " +
             "so it is the one number wave scaling is not allowed to grind away.")]
    public float attackWindup = 0.35f;

    [Tooltip("Shots fired per ranged attack.")]
    [Min(1)] public int shotsPerAttack = 1;

    [Tooltip("Seconds between the shots of one burst.")]
    public float burstInterval = 0.12f;

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

    [Header("Movement")]
    [Tooltip("How hard it sidesteps while closing. A crowd that all walks the direct line " +
             "arrives as a single-file queue you can hold with one magazine; a crowd that " +
             "circles forces you to keep moving.")]
    [Range(0f, 1f)] public float strafeAmount = 0.35f;

    [Tooltip("Seconds before it reverses its circling direction, so the weave is not a treadmill.")]
    public float strafeFlipInterval = 2.6f;

    [Tooltip("Enemies inside this radius push each other apart. Without it they converge " +
             "into one stack of overlapping bodies and the fight stops reading.")]
    public float separationRadius = 2.4f;

    [Range(0f, 4f)] public float separationStrength = 1.8f;

    [Tooltip("Speed multiplier for a short sprint once it reaches charge range. 1 disables it.")]
    [Min(1f)] public float chargeSpeedMultiplier = 1f;

    public float chargeRange = 10f;
    public float chargeDuration = 1.1f;
    public float chargeCooldown = 4f;

    [Header("Ranged Only")]
    public float rangedSpread = 2.5f;
    public float preferredRangedDistance = 12f;
    public LayerMask rangedHitMask = ~0;

    [Header("Reactions")]
    [Tooltip("Damage in one hit needed to interrupt it. This is what makes a strong weapon " +
             "feel strong: the enemy visibly flinches and loses its wind-up. 0 never staggers.")]
    [Min(0f)] public float staggerThreshold = 18f;

    public float staggerDuration = 0.45f;

    [Tooltip("Colour the body flashes while winding up an attack. The flash is the tell -- " +
             "without it a melee hit out of a crowd is unreadable.")]
    [ColorUsage(false, true)] public Color telegraphColor = new Color(2.6f, 0.5f, 0.15f);

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

    /// <summary>0 on wave 1, 1 once the difficulty curve has topped out. Read by the HUD/debug.</summary>
    public float Aggression { get; private set; }

    NavMeshAgent _agent;
    Health _health;
    Health _targetHealth;
    AudioSource _audio;
    Collider[] _colliders;
    RagdollController _ragdoll;

    Renderer[] _renderers;
    MaterialPropertyBlock _block;

    /// <summary>
    /// Always reach the block through here, never through the field. Recompiling a
    /// script while play mode is running reloads the domain without re-running Awake,
    /// and a MaterialPropertyBlock is a plain C# class that Unity's reload backup
    /// cannot carry across -- so it comes back null while _renderers and
    /// _restBodyColor, both serializable types, survive.
    ///
    /// That specific combination is what made the old code dangerous: the guards here
    /// test the two fields that survive, so they pass, and the null one goes straight
    /// into Renderer.GetPropertyBlock as "ArgumentNullException: dest", once per
    /// renderer per frame for the rest of the session. Recreating on demand costs one
    /// null check and makes the flash simply keep working after a reload.
    /// </summary>
    MaterialPropertyBlock Block
    {
        get
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            return _block;
        }
    }
    Color[] _restBodyColor;
    Color[] _restGlowColor;

    Coroutine _attackRoutine;

    float _baseSpeed = 4f;
    float _nextAttackTime;
    float _nextRepathTime;
    float _lastSeenTime = -9999f;
    float _strafeFlipTime;
    float _strafeSign = 1f;
    float _jitterAngle;
    float _staggerUntil;
    float _chargeUntil;
    float _nextChargeTime;
    float _windupStart, _windupEnd;
    bool _attacking;
    bool _hasAlerted;
    bool _flashing;

    static readonly Collider[] NeighbourBuffer = new Collider[16];

    /// <summary>Every renderer that is part of the body, excluding the floating health bar.</summary>
    Renderer[] BodyRenderers()
    {
        var all = GetComponentsInChildren<Renderer>();
        var body = new System.Collections.Generic.List<Renderer>(all.Length);

        foreach (var renderer in all)
            if (renderer != null && !EnemyHealthBar.IsBarRenderer(renderer)) body.Add(renderer);

        return body.ToArray();
    }

    // ======================================================================
    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _health = GetComponent<Health>();
        _audio = GetComponent<AudioSource>();
        _colliders = GetComponentsInChildren<Collider>();
        _ragdoll = GetComponent<RagdollController>();

        if (eyes == null) eyes = transform;
        if (animator == null) animator = GetComponentInChildren<Animator>();

        _renderers = BodyRenderers();
    }

    void OnEnable()
    {
        if (_health == null) return;

        _health.Died += OnDied;
        _health.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        if (_health == null) return;

        _health.Died -= OnDied;
        _health.Damaged -= OnDamaged;
    }

    void Start()
    {
        if (target == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
        }

        if (target != null) _targetHealth = target.GetComponentInParent<Health>();

        // Captured here rather than in Awake: the archetype and the wave's speed
        // growth are both applied after Instantiate returns, so Awake would bank
        // the unscaled prefab value and every charge would slow the enemy down.
        _baseSpeed = _agent.speed;

        _strafeSign = Random.value < 0.5f ? -1f : 1f;
        _strafeFlipTime = Time.time + Random.Range(0.5f, strafeFlipInterval);
        _jitterAngle = Random.Range(0f, Mathf.PI * 2f);

        CacheRestColors();

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

        UpdateTelegraph();

        if (Time.time < _staggerUntil)
        {
            Stop();
            CurrentState = State.Stagger;
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
                _attackRoutine = StartCoroutine(AttackRoutine());
            }
            else
            {
                CurrentState = State.Chase;
                UpdateCharge(distance);
                MoveTowardTarget(distance);
            }
        }

        if (CurrentState == State.Attack || (_attacking && target != null))
            FaceTarget();

        UpdateAnimator();
    }

    // ======================================================================
    // Movement
    // ======================================================================

    /// <summary>
    /// Picks a destination that is not simply "where the player is standing". The
    /// direct line is the worst possible approach in a crowd: everyone shares it,
    /// so they trail into a queue and die one at a time. Offsetting sideways and
    /// pushing off neighbours turns the same enemies into something that surrounds you.
    /// </summary>
    void MoveTowardTarget(float distance)
    {
        if (!_agent.isOnNavMesh) return;

        _agent.isStopped = false;
        if (Time.time < _nextRepathTime) return;

        _nextRepathTime = Time.time + repathInterval;

        if (Time.time >= _strafeFlipTime)
        {
            _strafeSign = -_strafeSign;
            _strafeFlipTime = Time.time + strafeFlipInterval * Random.Range(0.7f, 1.3f);
        }

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;

        Vector3 forward = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        // How far out it wants to sit: its firing line, or just inside melee reach.
        float standOff = ranged ? preferredRangedDistance : attackRange * 0.7f;
        Vector3 goal = target.position - forward * standOff;

        // Circle while there is still ground to cover; stop circling on arrival so
        // it actually closes instead of orbiting forever just out of reach.
        float closing = Mathf.Clamp01((distance - standOff) / 8f);
        goal += right * (_strafeSign * strafeAmount * 6f * closing);

        goal += Separation();

        if (NavMesh.SamplePosition(goal, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            _agent.SetDestination(hit.position);
        else
            _agent.SetDestination(target.position);
    }

    /// <summary>Sum of pushes away from nearby enemies, strongest when almost overlapping.</summary>
    Vector3 Separation()
    {
        if (separationStrength <= 0f || separationRadius <= 0f) return Vector3.zero;

        int count = Physics.OverlapSphereNonAlloc(transform.position, separationRadius,
                                                  NeighbourBuffer, 1 << gameObject.layer,
                                                  QueryTriggerInteraction.Ignore);
        Vector3 push = Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            var other = NeighbourBuffer[i];
            if (other == null || other.transform.root == transform.root) continue;

            Vector3 away = transform.position - other.transform.position;
            away.y = 0f;

            float distance = away.magnitude;
            if (distance < 0.01f)
            {
                // Perfectly stacked: shove along this enemy's own fixed bearing so the
                // pair separates, instead of both reading a zero vector and staying put.
                push += new Vector3(Mathf.Cos(_jitterAngle), 0f, Mathf.Sin(_jitterAngle));
                continue;
            }

            push += away / distance * (1f - distance / separationRadius);
        }

        return push * separationStrength;
    }

    /// <summary>A short sprint in mid-range, so closing the last stretch has some threat to it.</summary>
    void UpdateCharge(float distance)
    {
        if (chargeSpeedMultiplier <= 1f) return;

        if (Time.time < _chargeUntil)
        {
            _agent.speed = _baseSpeed * chargeSpeedMultiplier;
            return;
        }

        _agent.speed = _baseSpeed;

        bool inWindow = distance <= chargeRange && distance > attackRange;
        if (!inWindow || Time.time < _nextChargeTime) return;

        _chargeUntil = Time.time + chargeDuration;
        _nextChargeTime = Time.time + chargeCooldown;
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
    // Attacking
    // ======================================================================
    IEnumerator AttackRoutine()
    {
        _attacking = true;
        Stop();

        if (animator != null && !string.IsNullOrEmpty(attackTrigger))
            animator.SetTrigger(attackTrigger);

        PlayClip(attackClip);

        _windupStart = Time.time;
        _windupEnd = Time.time + attackWindup;

        yield return new WaitForSeconds(attackWindup);

        ClearTelegraph();

        if (CurrentState != State.Dead && target != null && _targetHealth != null && !_targetHealth.IsDead)
        {
            if (ranged)
            {
                for (int i = 0; i < Mathf.Max(1, shotsPerAttack); i++)
                {
                    FireRangedShot();
                    if (i < shotsPerAttack - 1) yield return new WaitForSeconds(burstInterval);
                }
            }
            else
            {
                TryMeleeHit();
            }
        }

        _nextAttackTime = Time.time + attackCooldown;
        _attacking = false;
        _attackRoutine = null;
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
        if (target == null) return;

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
    // Reactions
    // ======================================================================
    void OnDamaged(Health health, DamageInfo info)
    {
        if (CurrentState == State.Dead) return;

        // Getting shot is an introduction. Even out of its field of view, an enemy
        // that takes a hit should turn and commit rather than stand there.
        _hasAlerted = true;
        _lastSeenTime = Time.time;

        if (staggerThreshold <= 0f || info.amount < staggerThreshold) return;

        _staggerUntil = Time.time + staggerDuration;

        if (_attackRoutine != null)
        {
            StopCoroutine(_attackRoutine);
            _attackRoutine = null;
            _attacking = false;
        }

        ClearTelegraph();

        // A stagger that did not also cost it the attack would be decoration.
        _nextAttackTime = Mathf.Max(_nextAttackTime, Time.time + staggerDuration);
        _chargeUntil = 0f;
    }

    void OnDied(Health health)
    {
        CurrentState = State.Dead;
        _attacking = false;
        StopAllCoroutines();
        ClearTelegraph();

        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
            _agent.enabled = false;
        }

        // With a ragdoll present the bone colliders ARE the corpse's physics, so
        // switching them off here would drop it straight through the floor. Let the
        // RagdollController own collider state in that case.
        if (_ragdoll == null)
        {
            foreach (var col in _colliders)
                if (col != null) col.enabled = false;
        }

        if (animator != null && !string.IsNullOrEmpty(deathTrigger))
            animator.SetTrigger(deathTrigger);

        PlayClip(deathClip);
        enabled = false;
    }

    // ======================================================================
    // Difficulty
    // ======================================================================

    /// <summary>
    /// Sharpens this enemy for a later wave. Cooldowns tighten, it circles harder
    /// and it shoots straighter -- but the wind-up only compresses part way and
    /// never past a floor, because that telegraph is the whole reason a crowd is
    /// survivable. Take the reaction window away and difficulty becomes unfairness.
    /// </summary>
    public void ApplyWaveTuning(float aggression01)
    {
        Aggression = Mathf.Clamp01(aggression01);

        attackCooldown = Mathf.Max(0.35f, attackCooldown * Mathf.Lerp(1f, 0.6f, Aggression));
        attackWindup = Mathf.Max(0.18f, attackWindup * Mathf.Lerp(1f, 0.75f, Aggression));

        strafeAmount = Mathf.Clamp01(strafeAmount + 0.35f * Aggression);
        rangedSpread = Mathf.Max(0.5f, rangedSpread * Mathf.Lerp(1f, 0.55f, Aggression));
        detectionRadius *= Mathf.Lerp(1f, 1.25f, Aggression);
        loseTargetTime *= Mathf.Lerp(1f, 1.6f, Aggression);
    }

    // ======================================================================
    // Presentation
    // ======================================================================

    /// <summary>
    /// Records the colours the archetype left on each renderer, so the wind-up flash
    /// has something to return to. Runs after the archetype has been stamped on.
    /// </summary>
    void CacheRestColors()
    {
        if (_renderers == null) return;

        _restBodyColor = new Color[_renderers.Length];
        _restGlowColor = new Color[_renderers.Length];

        var block = Block;

        for (int i = 0; i < _renderers.Length; i++)
        {
            var renderer = _renderers[i];
            if (renderer == null) continue;

            // Cleared first: a renderer with no block of its own leaves whatever was
            // read last still sitting in this one, and every body part after the first
            // would inherit the first part's colour as its "rest" state.
            block.Clear();
            renderer.GetPropertyBlock(block);

            _restBodyColor[i] = block.HasColor("_BaseColor")
                ? block.GetColor("_BaseColor")
                : ReadSharedColor(renderer, "_BaseColor");

            _restGlowColor[i] = block.HasColor("_EmissionColor")
                ? block.GetColor("_EmissionColor")
                : Color.black;
        }
    }

    static Color ReadSharedColor(Renderer renderer, string property)
    {
        var material = renderer.sharedMaterial;
        if (material == null) return Color.white;

        if (material.HasProperty(property)) return material.GetColor(property);
        return material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
    }

    /// <summary>Ramps the body toward the warning colour across the wind-up.</summary>
    void UpdateTelegraph()
    {
        if (!_attacking || _windupEnd <= _windupStart) return;

        float t = Mathf.InverseLerp(_windupStart, _windupEnd, Time.time);

        // Ease in so the flash peaks right before the hit, not the moment it starts.
        SetFlash(t * t);
    }

    void SetFlash(float amount)
    {
        if (_renderers == null || _restBodyColor == null) return;

        amount = Mathf.Clamp01(amount);
        _flashing = amount > 0.001f;

        var block = Block;

        for (int i = 0; i < _renderers.Length; i++)
        {
            var renderer = _renderers[i];
            if (renderer == null) continue;

            renderer.GetPropertyBlock(block);

            Color body = Color.Lerp(_restBodyColor[i], telegraphColor, amount * 0.6f);
            Color glow = Color.Lerp(_restGlowColor[i], telegraphColor, amount);

            block.SetColor("_BaseColor", body);
            block.SetColor("_Color", body);
            block.SetColor("_EmissionColor", glow);
            renderer.SetPropertyBlock(block);
        }
    }

    void ClearTelegraph()
    {
        if (!_flashing) return;

        SetFlash(0f);
        _flashing = false;
        _windupEnd = _windupStart;
    }

    void UpdateAnimator()
    {
        if (animator == null || string.IsNullOrEmpty(speedParameter)) return;

        // enabled is checked before isOnNavMesh: OnDied disables the agent, and several
        // NavMeshAgent properties complain when read on a disabled component.
        float speed = _agent != null && _agent.enabled && _agent.isOnNavMesh
            ? _agent.velocity.magnitude
            : 0f;

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

        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, separationRadius);
    }
}
