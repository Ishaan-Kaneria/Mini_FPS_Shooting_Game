using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NavMesh hunter with line-of-sight checks, a telegraphed wind-up before it
/// commits, ranged fire, crowd separation, strafing and a stagger reaction.
///
/// It reacts to being shot on three scales, which is most of what separates an
/// opponent from a spawner. A light hit is a flinch -- a fraction of a second of
/// stopped movement and a beat before it comes back at you. A heavy one staggers it
/// and costs it the attack outright. Sustained fire fills a suppression bucket and
/// sends it looking for cover, on a cooldown so it cannot kite you forever.
///
/// An armed enemy is not only a ranged enemy: it shoots at range, swings the rifle at
/// anything standing in its face, and gives ground when the player is above it so the
/// firing line clears the lip of whatever they are standing on.
///
/// The behaviour is deliberately layered rather than scripted per enemy type: an
/// <see cref="EnemyArchetype"/> stamps numbers onto these knobs at spawn, and
/// <see cref="ApplyDifficulty"/> sharpens them as the levels climb. One brain,
/// many enemies, no per-variant code.
///
/// Drop an Animator in and the float/trigger parameters below get driven automatically.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour
{
    public enum State { Idle, Chase, Attack, Stagger, Retreat, Dead }

    [Header("Wiring")]
    public Transform eyes;
    public Transform target;

    [Tooltip("Assigned by the LevelManager at spawn. Read for score value and drops; " +
             "leave empty on a hand-placed enemy and it simply uses the fields below.")]
    public EnemyArchetype archetype;

    /// <summary>
    /// Whether this enemy has seen the player at least once. It never goes back: an enemy
    /// that has found you is hunting you whether or not it can see you this frame, and the
    /// HUD's marker over it says exactly that.
    /// </summary>
    public bool HasSpotted => _hasAlerted;

    [Header("Behaviour")]
    public bool ranged;
    public float attackRange = 2f;
    public float attackDamage = 12f;
    public float attackCooldown = 1.3f;

    [Tooltip("Reach of a swing. A shooter this close stops shooting and hits you with " +
             "the rifle instead -- otherwise walking into its face is the safest place " +
             "on the map, because the shot is aimed from the eyes at a body that is no " +
             "longer in front of them.")]
    public float meleeRange = 2.4f;

    [Tooltip("Telegraph time before damage lands. This is the player's reaction window, " +
             "so it is the one number difficulty scaling is not allowed to grind away.")]
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

    [Tooltip("Seconds between the line-of-sight raycasts, which is the one thing every " +
             "enemy does every frame and the most expensive. A level's worth of them at " +
             "sixty frames a second is thousands of casts a second on a platform that " +
             "has to fit in a browser tab, for an answer that changes when somebody " +
             "walks behind a crate.\n\n" +
             "The range and field-of-view tests are still made every frame -- they are " +
             "arithmetic -- so this only delays noticing cover, never noticing you. " +
             "Set it to 0 to cast every frame.")]
    [Range(0f, 0.5f)] public float sightCheckInterval = 0.1f;

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

    [Tooltip("How much of its speed a shooter keeps while it can see you. Below 1 it " +
             "settles onto its firing line instead of jogging through the fight, which " +
             "is what makes the gun read as the thing it is doing.")]
    [Range(0.1f, 1f)] public float aimMoveSpeedMultiplier = 0.55f;

    [Tooltip("Slack left between where a melee enemy parks and the edge of its reach. " +
             "The agent brakes a full stopping distance short of wherever it is sent, so " +
             "this is the margin left over after that has been paid for.")]
    [Min(0f)] public float meleeApproachMargin = 0.35f;

    public float chargeRange = 10f;
    public float chargeDuration = 1.1f;
    public float chargeCooldown = 4f;

    [Header("Ranged Only")]
    public float rangedSpread = 2.5f;
    public float preferredRangedDistance = 12f;
    public LayerMask rangedHitMask = ~0;

    [Tooltip("What a shot is worth next to this enemy's own swing. Well under 1 on " +
             "purpose: a crowd that can all reach you from across the arena is not the " +
             "same fight as a crowd that has to close, and a rifle that hit as hard as " +
             "a fist would end a run before the player ever saw where the fire came from.")]
    [Range(0f, 2f)] public float rangedDamageMultiplier = 0.4f;

    [Tooltip("Metres the player has to be above it before height counts as height, " +
             "so a kerb or a ramp does not read as a rooftop.")]
    public float elevationDeadZone = 1.5f;

    [Tooltip("Extra standoff per metre the player is above it. A shooter that walks to " +
             "the foot of the crate you are standing on loses the angle entirely and " +
             "plinks at the underside of a ledge; backing off buys the firing line back.")]
    public float standOffPerMetreOfRise = 1.7f;

    public float maxElevationStandOff = 14f;

    [Header("Reactions")]
    [Tooltip("Damage in one hit needed to interrupt it. This is what makes a strong weapon " +
             "feel strong: the enemy visibly flinches and loses its wind-up. 0 never staggers.")]
    [Min(0f)] public float staggerThreshold = 18f;

    public float staggerDuration = 0.45f;

    [Tooltip("The hitch on a hit too light to stagger. Short -- this is a body absorbing " +
             "a round, not a stun.")]
    [Min(0f)] public float flinchDuration = 0.14f;

    [Tooltip("Minimum seconds between flinches. Without a floor here a held trigger at " +
             "600rpm lands a flinch every tenth of a second, and the reaction that was " +
             "meant to make hits feel solid becomes a stunlock that makes every enemy a " +
             "statue for as long as you keep firing.")]
    [Min(0f)] public float flinchCooldown = 0.55f;

    [Tooltip("How long being hit pushes the next attack back. This is the beat between " +
             "taking a round and coming back at you.")]
    [Min(0f)] public float flinchAttackDelay = 0.6f;

    [Header("Breaking Off")]
    [Tooltip("Let it run for cover under sustained fire. Off for a boss, which is " +
             "supposed to be the thing that does not care.")]
    public bool canRetreat = true;

    /// <summary>
    /// Runs and keeps running, whatever the suppression says. Set by a hunt level on the
    /// one enemy it marks.
    ///
    /// It goes through <see cref="UpdateRetreat"/> rather than being a state of its own
    /// because retreating is already exactly this behaviour -- break off, pick cover,
    /// re-pick it on arrival -- with a timer on it. What a hunt removes is the timer, so
    /// this is that one condition and nothing else. A seventh state would be six copies
    /// of the movement code for one missing line.
    /// </summary>
    [System.NonSerialized] public bool forcedFlight;

    [Tooltip("Damage inside the window below that sends it looking for cover.")]
    [Min(1f)] public float suppressionDamage = 45f;

    [Tooltip("The window that damage has to arrive within. Together these ask 'am I " +
             "being focused right now' rather than 'did something hurt', which is the " +
             "difference between an enemy that reacts to being shot at and one that " +
             "bolts from every stray round.")]
    [Min(0.1f)] public float suppressionWindow = 1.6f;

    public float retreatDuration = 2.2f;
    public float retreatDistance = 16f;

    [Tooltip("Speed multiplier while breaking off. Above 1: it is running, not walking.")]
    [Min(0.1f)] public float retreatSpeedMultiplier = 1.5f;

    [Tooltip("Seconds before it can break off again, so a wounded enemy cannot kite you " +
             "around the arena for the rest of the level.")]
    public float retreatCooldown = 9f;

    [Tooltip("Colour the body flashes while winding up an attack. The flash is the tell -- " +
             "without it a melee hit out of a crowd is unreadable.")]
    [ColorUsage(false, true)] public Color telegraphColor = new Color(2.6f, 0.5f, 0.15f);

    [Header("Audio (optional)")]
    public AudioClip alertClip;
    public AudioClip attackClip;
    public AudioClip deathClip;

    [Tooltip("Grunts of pain, picked at random on a hit. Several, because one voice " +
             "retriggered on every bullet is the most obviously synthetic sound in a " +
             "firefight.")]
    public AudioClip[] painClips;

    [Tooltip("Minimum seconds between grunts, so sustained fire does not machine-gun " +
             "the voice.")]
    [Min(0f)] public float painCooldown = 0.35f;

    [Tooltip("How loud a hit reaction is. Under the death, deliberately: in a crowd the " +
             "only way to tell \"I hurt it\" from \"I killed it\" without looking is that " +
             "one of the two is louder and longer than the other.")]
    [Range(0f, 1f)] public float painVolume = 0.7f;

    [Tooltip("How loud a death is. The single most useful sound in a firefight, because " +
             "it is the one that says stop shooting this and find another target.")]
    [Range(0f, 1f)] public float deathVolume = 1f;

    [Header("Ranged Feedback")]
    [Tooltip("Where the shot appears to come from. Only cosmetic -- the shot itself is " +
             "still traced from the eyes, so cover and accuracy do not change with the " +
             "weapon model. Falls back to the eyes when empty.")]
    public Transform muzzlePoint;

    [Tooltip("Spawned at the muzzle on each shot, and destroyed by its own TransientFlash.")]
    public GameObject muzzleFlashPrefab;

    [Tooltip("The streak from muzzle to impact. Without one, a ranged enemy shoots you " +
             "from across the arena with nothing on screen to say where it came from.")]
    public GameObject tracerPrefab;

    public float tracerSpeed = 200f;

    [Tooltip("The shot itself. Separate from attackClip, which is the enemy's own voice.")]
    public AudioClip fireClip;

    [Header("Animation (optional)")]
    public Animator animator;
    public string speedParameter = "Speed";
    public string attackTrigger = "Attack";
    public string deathTrigger = "Die";

    public State CurrentState { get; private set; } = State.Idle;

    /// <summary>0 on level 1, 1 once the difficulty curve has topped out. Read by the HUD/debug.</summary>
    public float Aggression { get; private set; }

    /// <summary>
    /// The last hit worth reacting to, as three values the limb animator replays into a
    /// jerk of the body. Kept here rather than pushed at the animator so an enemy with
    /// no limbs rigged -- or an imported character driven by a real Animator -- simply
    /// never reads them.
    /// </summary>
    public float LastReactionTime { get; private set; } = -999f;

    /// <summary>Which way the round was travelling, in this enemy's own local space.</summary>
    public Vector3 LastReactionLocal { get; private set; }

    /// <summary>0 to 1 severity, so a graze and a slug do not throw the body equally.</summary>
    public float LastReactionStrength { get; private set; }

    /// <summary>When it last fired a round, for the kick of the rifle into the shoulder.</summary>
    public float LastShotTime { get; private set; } = -999f;

    NavMeshAgent _agent;
    Health _health;
    Health _targetHealth;
    AudioSource _audio;
    Collider[] _colliders;
    RagdollController _ragdoll;
    EnemyDeath _death;
    EnemyWounds _wounds;
    EnemyVoice _voice;

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

    /// <summary>The archetype's colours before any blood, so a stain is measured from clean.</summary>
    Color[] _cleanBodyColor;

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
    float _flinchUntil;
    float _nextFlinchTime;
    float _nextPainTime;
    float _suppression;
    float _retreatUntil;
    float _nextRetreatTime;
    bool _attacking;
    bool _hasAlerted;
    bool _flashing;

    /// <summary>The last answer CanSeeTarget cast for, and when it may cast again.</summary>
    bool _sawTarget;
    float _nextSightCheck;

    /// <summary>The one muzzle flash, replayed per shot. See PlayMuzzleFlash.</summary>
    GameObject _muzzleFlash;

    static readonly Collider[] NeighbourBuffer = new Collider[16];

    /// <summary>Candidate cover positions tried per break-off. Low on purpose: this
    /// runs once per retreat, not per frame, and the fallback is always usable.</summary>
    const int RetreatSamples = 6;

    /// <summary>Every renderer that is part of the body, excluding the floating health bar.</summary>
    /// <summary>
    /// Whether a renderer is part of the enemy's body, as opposed to something it is
    /// carrying or wearing.
    ///
    /// Two things use this and both would be wrong without it: the damage flash here,
    /// and EnemyArchetype.Tint, which paints every renderer it finds the archetype's
    /// colour. An enemy holding a rifle would otherwise hold a flesh-coloured rifle
    /// that flashes red when the enemy is hit.
    ///
    /// The rule is the surface tag the kit already uses for impact effects: anything
    /// tagged as a hard surface is gear, not flesh. Untagged parts still count as body,
    /// so a custom enemy prefab that never set tags keeps tinting exactly as before.
    /// </summary>
    public static bool IsBodyRenderer(Renderer renderer)
    {
        if (renderer == null || EnemyHealthBar.IsBarRenderer(renderer)) return false;

        return !renderer.CompareTag("Metal")
            && !renderer.CompareTag("Wood")
            && !renderer.CompareTag("Concrete");
    }

    Renderer[] BodyRenderers()
    {
        var all = GetComponentsInChildren<Renderer>();
        var body = new System.Collections.Generic.List<Renderer>(all.Length);

        foreach (var renderer in all)
            if (IsBodyRenderer(renderer)) body.Add(renderer);

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
        _death = GetComponent<EnemyDeath>();
        _wounds = GetComponent<EnemyWounds>();
        _voice = GetComponent<EnemyVoice>();

        if (eyes == null) eyes = transform;
        if (animator == null) animator = GetComponentInChildren<Animator>();

        // Scattered rather than aligned. Every enemy in a level is spawned within a
        // frame or two of every other, so a shared interval would land all of their
        // sight casts on the same frame -- the same total work, delivered as a spike.
        _nextSightCheck = Time.time + Random.value * sightCheckInterval;

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
        // Disabling a behaviour stops its coroutines, so the flag describing one stops
        // with it. Left set, _attacking locks this enemy out of the whole state machine
        // below -- it would stand still and face the player forever.
        _attacking = false;
        _attackRoutine = null;

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

        // Captured here rather than in Awake: the archetype and the level's speed
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
        DecaySuppression();

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
                if (_voice == null) PlayClip(alertClip);
            }
        }

        RecoverLostAttack();

        // Breaking off outranks everything below it, including an attack already in
        // progress. An enemy that finishes its wind-up and *then* runs has not reacted
        // to anything.
        if (UpdateRetreat())
        {
            FaceTarget();
            ApplyAgentSpeed(canSee);
            UpdateAnimator();
            return;
        }

        float distance = Vector3.Distance(transform.position, target.position);

        bool aware = relentless || (_hasAlerted && Time.time - _lastSeenTime <= loseTargetTime);

        if (!aware)
        {
            Stop();
            CurrentState = State.Idle;
        }
        else if (!_attacking)
        {
            // A shooter commits at its firing range with line of sight, or the moment
            // you are close enough to hit with the rifle instead.
            bool inAttackPosition = ranged
                ? (distance <= meleeRange || (distance <= attackRange && canSee))
                : distance <= attackRange;

            if (inAttackPosition && Time.time >= _nextAttackTime)
            {
                CurrentState = State.Attack;
                _attackRoutine = StartCoroutine(AttackRoutine());
            }
            else if (Time.time < _flinchUntil)
            {
                // The hit itself is the pause. Nothing is cancelled and nothing is
                // queued -- it stops walking for a moment, and the attack clock that
                // OnDamaged already pushed back decides when it comes at you again.
                CurrentState = State.Chase;
                Stop();
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

        ApplyAgentSpeed(canSee);
        UpdateAnimator();
    }

    // ======================================================================
    // Breaking off
    // ======================================================================

    /// <summary>
    /// Bleeds the suppression bucket down.
    ///
    /// A leak rather than a sliding window of timestamped hits: one float and one
    /// subtraction, and it answers the question that actually matters -- is fire
    /// arriving faster than it drains. <see cref="suppressionWindow"/> is therefore
    /// the time the damage that trips it has to arrive within.
    /// </summary>
    void DecaySuppression()
    {
        if (_suppression <= 0f) return;

        float rate = suppressionDamage / Mathf.Max(0.1f, suppressionWindow);
        _suppression = Mathf.Max(0f, _suppression - rate * Time.deltaTime);
    }

    /// <summary>
    /// Runs the break-off, and returns true while it owns the frame.
    ///
    /// This is the half of the brain that answers being shot at. Standing in the open
    /// trading fire is what a spawner produces; walking out of the line of fire and
    /// coming back is what a player reads as an opponent. The cooldown is what keeps it
    /// from becoming a kite -- an enemy that could break off every time it was hit
    /// would never be killable at all.
    /// </summary>
    bool UpdateRetreat()
    {
        // The hunt's quarry. Ahead of the timer, because the timer is the only thing
        // that would ever bring it back into the fight.
        if (forcedFlight)
        {
            CurrentState = State.Retreat;

            if (_agent.isOnNavMesh)
            {
                _agent.isStopped = false;

                if (!_agent.pathPending
                    && _agent.remainingDistance <= _agent.stoppingDistance + 0.5f)
                    SetRetreatGoal();
            }

            return true;
        }

        if (Time.time < _retreatUntil)
        {
            CurrentState = State.Retreat;

            if (_agent.isOnNavMesh)
            {
                _agent.isStopped = false;

                // Re-picked on arrival, so a short hop into a corner does not leave it
                // standing still in the open for the rest of the retreat.
                if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.5f)
                    SetRetreatGoal();
            }

            return true;
        }

        if (!canRetreat || _suppression < suppressionDamage) return false;

        // Something on the floor cannot break off. It stays where it fell and fights.
        if (Crawling) return false;
        if (Time.time < _nextRetreatTime) return false;

        _suppression = 0f;
        _retreatUntil = Time.time + retreatDuration;
        _nextRetreatTime = Time.time + retreatCooldown + retreatDuration;

        CancelAttack();
        _chargeUntil = 0f;
        _nextAttackTime = Mathf.Max(_nextAttackTime, _retreatUntil);

        CurrentState = State.Retreat;
        SetRetreatGoal();
        return true;
    }

    /// <summary>
    /// Sends it somewhere the player cannot shoot it, or failing that, somewhere else.
    ///
    /// Cover is preferred over distance every time: a straight sprint away from the
    /// player is still a target, just a smaller one, and on an open arena floor it
    /// reads as the enemy giving up rather than repositioning.
    /// </summary>
    void SetRetreatGoal()
    {
        if (target == null || !_agent.isOnNavMesh) return;

        // The attack this retreat just cancelled had stopped the agent. Clearing that
        // here rather than a frame later is the difference between turning and running
        // and standing still for a beat first.
        _agent.isStopped = false;

        Vector3 away = transform.position - target.position;
        away.y = 0f;
        away = away.sqrMagnitude > 0.01f ? away.normalized : -transform.forward;

        Vector3 fallback = transform.position + away * retreatDistance;
        bool haveFallback = false;

        for (int i = 0; i < RetreatSamples; i++)
        {
            // Fanned rather than straight back, so a group breaking off does not turn
            // into one column running the same line.
            Vector3 direction = Quaternion.AngleAxis(Random.Range(-75f, 75f), Vector3.up) * away;
            Vector3 candidate = transform.position + direction * retreatDistance * Random.Range(0.65f, 1f);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas)) continue;

            bool hidden = Physics.Linecast(target.position + Vector3.up * 1.4f,
                                           hit.position + Vector3.up * 1.2f,
                                           sightBlockers, QueryTriggerInteraction.Ignore);

            if (hidden)
            {
                _agent.SetDestination(hit.position);
                return;
            }

            if (!haveFallback)
            {
                fallback = hit.position;
                haveFallback = true;
            }
        }

        _agent.SetDestination(fallback);
    }

    /// <summary>
    /// One owner for the agent's speed.
    ///
    /// Charging, breaking off and holding a firing line all want to change it, and each
    /// writing the field directly is how an enemy ends up stuck at another state's
    /// speed after that state has ended -- the old charge code left exactly that bug
    /// waiting, because it only restored the base speed on a class that could charge at
    /// all. Recomputed from scratch every frame, the states cannot leak into each other.
    /// </summary>
    void ApplyAgentSpeed(bool canSee)
    {
        if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;

        float multiplier = 1f;

        if (CurrentState == State.Retreat) multiplier = retreatSpeedMultiplier;
        else if (Time.time < _chargeUntil) multiplier = chargeSpeedMultiplier;
        else if (ranged && canSee) multiplier = aimMoveSpeedMultiplier;

        // A wound caps it rather than scaling it. A limping enemy does not get its limp
        // back by charging, and a crawler drags itself along at one pace whatever the
        // rest of the brain wanted.
        if (_wounds != null && _wounds.Legs != EnemyWounds.LegState.Sound)
            multiplier = Mathf.Min(multiplier, 1f) * _wounds.MoveSpeedMultiplier;

        _agent.speed = _baseSpeed * multiplier;
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

        float standOff = DesiredStandOff();
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

    /// <summary>
    /// The gap it aims to leave between itself and the player, already discounted by
    /// the distance the agent brakes short of wherever it is sent.
    ///
    /// That discount is the whole point. A NavMeshAgent stops a full stoppingDistance
    /// before its destination, so sending a melee enemy to "just inside its own reach"
    /// parked it a stoppingDistance *outside* that reach -- with the default 2.2m range
    /// and a 1.5m stop, it settled at roughly three metres and the attack check never
    /// passed again. It looked exactly like the enemy arriving and then deciding to do
    /// nothing, which is what it was: the reach is what has to be shared between the
    /// standoff and the brake, and only the standoff is ours to give.
    /// </summary>
    float DesiredStandOff()
    {
        float brake = _agent != null ? _agent.stoppingDistance : 0f;

        if (!ranged)
            return Mathf.Max(0f, attackRange - brake - meleeApproachMargin);

        float standOff = Mathf.Max(0f, preferredRangedDistance - brake);

        // A player standing on something is the case a flat standoff gets wrong. The
        // shooter walks to the foot of the crate, loses the angle over the lip, and
        // spends the fight firing into the underside of a ledge. Trading ground for
        // height buys the firing line back.
        if (target != null)
        {
            float rise = target.position.y - transform.position.y;
            if (rise > elevationDeadZone)
                standOff += Mathf.Min(maxElevationStandOff,
                                      (rise - elevationDeadZone) * standOffPerMetreOfRise);
        }

        // Never past its own range, or it would walk away from the shot it came to take.
        return Mathf.Min(standOff, Mathf.Max(0f, attackRange - brake - 1f));
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

    /// <summary>
    /// A short sprint in mid-range, so closing the last stretch has some threat to it.
    /// Only decides *when* to charge -- ApplyAgentSpeed owns the speed itself.
    /// </summary>
    void UpdateCharge(float distance)
    {
        if (chargeSpeedMultiplier <= 1f) return;
        if (Time.time < _chargeUntil) return;
        if (_wounds != null && _wounds.Legs != EnemyWounds.LegState.Sound) return;

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
    /// <summary>
    /// Whether the player is visible from the eyes right now.
    ///
    /// Three tests in increasing order of cost, and only the last of them is rationed:
    ///
    /// - **Range**, squared on both sides rather than taking a square root to compare
    ///   against a number that is already known.
    /// - **Field of view**, skipped entirely once alerted. The `!_hasAlerted` term used
    ///   to be written after the angle, which reads the same and computes an arc cosine
    ///   for every enemy on every frame of a fight only to throw the answer away --
    ///   `_hasAlerted` is true for almost the whole of one.
    /// - **The line itself**, which is a physics cast and is the expensive one. It is
    ///   made at most every <see cref="sightCheckInterval"/> and the answer is held
    ///   between casts, with the first one seeded at a random offset in
    ///   <see cref="Awake"/> so a level's worth of enemies does not cast on the same
    ///   frame and leave a stutter every tenth of a second.
    ///
    /// Holding the answer is safe because of what reads it: freshness feeds
    /// `loseTargetTime`, which is seconds, and an attack commit, which is already
    /// re-measured after the wind-up. What it must not do is *delay* the cast that
    /// first sees the player -- so the two cheap tests still run every frame, and a
    /// failure clears the cache rather than leaving a stale true behind it.
    /// </summary>
    bool CanSeeTarget()
    {
        if (target == null) return _sawTarget = false;

        Vector3 origin = eyes.position;
        Vector3 targetPoint = target.position + Vector3.up * 1.4f;
        Vector3 toTarget = targetPoint - origin;

        if (toTarget.sqrMagnitude > detectionRadius * detectionRadius) return _sawTarget = false;

        if (!_hasAlerted && Vector3.Angle(transform.forward, toTarget) > fieldOfView * 0.5f)
            return _sawTarget = false;

        if (Time.time < _nextSightCheck) return _sawTarget;

        _nextSightCheck = Time.time + sightCheckInterval;

        // Anything on the blocker layers between us breaks the line.
        return _sawTarget = !Physics.Linecast(origin, targetPoint, sightBlockers,
                                              QueryTriggerInteraction.Ignore);
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

        if (_voice != null) _voice.OnAttack(ranged && Vector3.Distance(transform.position, target.position) > meleeRange);
        else PlayClip(attackClip);

        _windupStart = Time.time;
        _windupEnd = Time.time + attackWindup;

        yield return new WaitForSeconds(attackWindup);

        ClearTelegraph();

        if (CurrentState != State.Dead && target != null && _targetHealth != null && !_targetHealth.IsDead)
        {
            // Re-measured after the wind-up rather than reused from the frame the
            // attack was committed on: closing during the telegraph should change what
            // lands, and a rifleman firing from the eyes at something standing inside
            // its own muzzle misses every time.
            bool shoot = ranged &&
                         Vector3.Distance(transform.position, target.position) > meleeRange;

            if (shoot)
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

    /// <summary>
    /// Clears the attack flag when its routine is gone.
    ///
    /// _attacking describes a running coroutine and survives one being killed, because a
    /// bool is serializable and a Coroutine handle is not. In the editor that happens
    /// whenever a script is recompiled during play.
    ///
    /// The result is an enemy stranded mid-attack: the state machine in Update only runs
    /// while it is not attacking, so it stops moving and stops attacking, and
    /// UpdateTelegraph
    /// keeps reading a wind-up window that ended long ago -- which pins the flash at
    /// full, leaving it standing there glowing and facing the player. Shooting it does
    /// not help either: the stagger path clears the flag only when it finds a routine
    /// to stop. See Weapon.RecoverLostRoutines and the domain reload section of CLAUDE.md.
    /// </summary>
    void RecoverLostAttack()
    {
        if (!_attacking || _attackRoutine != null) return;

        _attacking = false;
        _nextAttackTime = Time.time + attackCooldown;
        ClearTelegraph();
    }

    void TryMeleeHit()
    {
        // Re-check the range -- backing off during the wind-up should work. A shooter
        // swinging its rifle reaches only as far as the rifle, not as far as it shoots.
        float reach = ranged ? meleeRange : attackRange;

        float distance = Vector3.Distance(transform.position, target.position);
        if (distance > reach * 1.25f) return;

        _targetHealth.ApplyDamage(new DamageInfo(
            attackDamage, target.position, Vector3.up,
            (target.position - transform.position).normalized, gameObject));
    }

    void FireRangedShot()
    {
        if (target == null) return;

        Vector3 origin = eyes.position;
        Vector3 direction = ((target.position + Vector3.up * 1.2f) - origin).normalized;

        // A hurt arm shakes the aim. Wider, never tighter: a wound only ever helps you.
        float spread = rangedSpread * (_wounds != null ? _wounds.AimSpreadMultiplier : 1f);

        Vector2 offset = Random.insideUnitCircle * spread;
        direction = Quaternion.AngleAxis(offset.x, Vector3.up) *
                    Quaternion.AngleAxis(offset.y, transform.right) * direction;

        PlayMuzzleEffects();
        LastShotTime = Time.time;

        // The shot is resolved before the visuals are sent anywhere, so the tracer can be
        // drawn to the real impact point. Note the early return is gone: a shot that hits
        // nothing still gets a tracer, because "where did that come from" is the question
        // a miss most needs to answer.
        bool struck = Physics.Raycast(origin, direction, out RaycastHit hit, detectionRadius,
                                      rangedHitMask, QueryTriggerInteraction.Ignore);

        SpawnTracer(struck ? hit.point : origin + direction * detectionRadius);

        if (!struck) return;

        float damage = attackDamage * Mathf.Max(0f, rangedDamageMultiplier);
        var info = new DamageInfo(damage, hit.point, hit.normal, direction, gameObject);

        var hitbox = hit.collider.GetComponent<Hitbox>();
        if (hitbox != null) hitbox.Receive(info);
        else
        {
            var health = hit.collider.GetComponentInParent<Health>();
            if (health != null && health != _health) health.ApplyDamage(info);
        }
    }

    /// <summary>Flash and report, at the weapon rather than at the eyes.</summary>
    void PlayMuzzleEffects()
    {
        Transform from = muzzlePoint != null ? muzzlePoint : eyes;

        PlayMuzzleFlash(from);

        if (fireClip != null && _audio != null) _audio.PlayOneShot(fireClip);
    }

    /// <summary>
    /// Shows the muzzle flash, reusing the one instance rather than building another.
    ///
    /// Parented to the muzzle so it tracks the enemy while it is visible, and switched
    /// off and on again per shot instead of instantiated and destroyed. A firing squad
    /// of ranged enemies was creating one object per shot each and leaving it hanging on
    /// the barrel for a second afterwards, for an effect TransientFlash hides in a
    /// twentieth of a second. Weapon.PlayMuzzleFlash does the same thing for the player,
    /// and TransientFlash rewinds itself in OnEnable so both can replay one object.
    /// </summary>
    void PlayMuzzleFlash(Transform from)
    {
        if (muzzleFlashPrefab == null || from == null) return;

        // Only a prefab that switches itself off can be replayed. Anything else has to
        // be built and destroyed per shot, because nothing would ever hide it again --
        // reusing one would leave it lit from the first round onward. Same fallback
        // SpawnTracer keeps for a prefab that predates TracerProjectile.
        if (muzzleFlashPrefab.GetComponent<TransientFlash>() == null)
        {
            Destroy(Instantiate(muzzleFlashPrefab, from.position, from.rotation, from), 1f);
            return;
        }

        if (_muzzleFlash == null)
        {
            // Instantiated active, so OnEnable has already run it from the start.
            _muzzleFlash = Instantiate(muzzleFlashPrefab, from.position, from.rotation, from);
            return;
        }

        // Off first: a flash still playing from the previous round has to be cycled,
        // because OnEnable is the only thing that rewinds it.
        _muzzleFlash.SetActive(false);
        _muzzleFlash.SetActive(true);
    }

    /// <summary>
    /// Drawn from the weapon, not from the eyes the shot was traced from. Starting the
    /// streak at the barrel is what makes the gun look like the thing that fired.
    /// </summary>
    void SpawnTracer(Vector3 endPoint)
    {
        if (tracerPrefab == null) return;

        Transform from = muzzlePoint != null ? muzzlePoint : eyes;
        if (from == null) return;

        Vector3 travel = endPoint - from.position;
        if (travel.sqrMagnitude < 0.0001f) return;

        var tracer = Instantiate(tracerPrefab, from.position, Quaternion.LookRotation(travel));

        var projectile = tracer.GetComponent<TracerProjectile>();
        if (projectile != null) projectile.Launch(endPoint, tracerSpeed);
        else Destroy(tracer, 1f);
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

        _suppression += info.amount;
        RecordReaction(info);

        // Not on the killing blow: the death sound is what that hit makes, and both at
        // once reads as two enemies rather than one.
        if ((health == null || health.Current > 0f) && _voice == null) PlayPain();

        if (staggerThreshold <= 0f || info.amount < staggerThreshold)
        {
            // The light reaction: a hitch, not a stun. Rate-limited, because a hitch on
            // every round of a 600rpm magazine is not a flinch -- it is a stunlock, and
            // it would turn every enemy into a statue for as long as you held the
            // trigger. What the player should get for a burst is one visible stumble
            // and a beat before the enemy comes back at them.
            if (Time.time < _nextFlinchTime) return;

            _flinchUntil = Time.time + flinchDuration;
            _nextFlinchTime = Time.time + flinchCooldown;
            _nextAttackTime = Mathf.Max(_nextAttackTime, Time.time + flinchAttackDelay);
            return;
        }

        _staggerUntil = Time.time + staggerDuration;

        CancelAttack();

        // A stagger that did not also cost it the attack would be decoration.
        _nextAttackTime = Mathf.Max(_nextAttackTime,
                                    Time.time + staggerDuration + flinchAttackDelay);
        _chargeUntil = 0f;
    }

    /// <summary>On the floor, dragging itself along. See <see cref="EnemyWounds"/>.</summary>
    public bool Crawling => _wounds != null && _wounds.Legs == EnemyWounds.LegState.Crawling;

    /// <summary>
    /// Knocks it off balance for a while, whatever the hit was worth. For the wounds that
    /// matter more than their damage: a boss taking a round through the visor, a leg
    /// going out from under it. Costs it the attack exactly as a heavy hit's stagger does.
    /// </summary>
    public void ForceStagger(float seconds)
    {
        if (CurrentState == State.Dead || seconds <= 0f) return;

        _staggerUntil = Mathf.Max(_staggerUntil, Time.time + seconds);
        CancelAttack();

        _nextAttackTime = Mathf.Max(_nextAttackTime, _staggerUntil + flinchAttackDelay);
        _chargeUntil = 0f;
    }

    /// <summary>
    /// The rifle is gone: from here on it closes and fights by hand. Its reach becomes its
    /// swing, so the stand-off it chose as a shooter does not park it twenty metres from
    /// the player with nothing to do there.
    /// </summary>
    public void Disarm()
    {
        if (!ranged) return;

        ranged = false;
        attackRange = Mathf.Min(attackRange, Mathf.Max(1.6f, meleeRange - 0.2f));
        CancelAttack();
    }

    /// <summary>
    /// Abandons an attack in progress, wherever it had got to.
    ///
    /// Shared by the stagger and the break-off because both mean the same thing: this
    /// enemy is no longer doing what it was doing. Clearing the telegraph is part of it
    /// -- the flash is a promise of an incoming hit, and leaving it lit on an enemy that
    /// has stopped attacking teaches the player to dodge nothing.
    /// </summary>
    void CancelAttack()
    {
        if (_attackRoutine != null)
        {
            StopCoroutine(_attackRoutine);
            _attackRoutine = null;
        }

        _attacking = false;
        ClearTelegraph();
    }

    /// <summary>
    /// Records the hit as something the body can replay, for EnemyLimbAnimator.
    ///
    /// Stored in local space so the animator can jerk the torso without doing any
    /// transform work of its own, and so the reaction stays correct if the enemy turns
    /// while it is still playing out.
    /// </summary>
    void RecordReaction(DamageInfo info)
    {
        Vector3 push = info.direction.sqrMagnitude > 0.0001f
            ? info.direction.normalized
            : -transform.forward;

        LastReactionLocal = transform.InverseTransformDirection(push);

        // Measured against the stagger threshold, so the same weapon reads as heavier on
        // a light enemy than on an armoured one. The floor keeps a boss -- which has no
        // threshold at all, by design -- still visibly reacting to being shot.
        LastReactionStrength = Mathf.Clamp01(info.amount / Mathf.Max(8f, staggerThreshold));
        LastReactionTime = Time.time;
    }

    void PlayPain()
    {
        if (painClips == null || painClips.Length == 0) return;
        if (Time.time < _nextPainTime) return;

        _nextPainTime = Time.time + painCooldown;
        PlayClip(painClips[Random.Range(0, painClips.Length)], painVolume);
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
        if (_ragdoll == null && _death == null)
        {
            foreach (var col in _colliders)
                if (col != null) col.enabled = false;
        }

        if (animator != null && !string.IsNullOrEmpty(deathTrigger))
            animator.SetTrigger(deathTrigger);

        // Through the pool rather than through this body's own AudioSource.
        //
        // The corpse is destroyed a couple of seconds from now -- sooner with no ragdoll
        // -- and an AudioSource goes with the GameObject it is on, so a death cry longer
        // than the destroy delay is a death cry that gets cut off mid-word. The pooled
        // source is not on the body and does not care. It also survives the level being
        // scored on the same frame, which is exactly when the last enemy tends to die.
        if (_voice == null)
            OneShotAudio.Play(deathClip, transform.position, deathVolume,
                              Random.Range(0.94f, 1.06f));

        enabled = false;
    }

    // ======================================================================
    // Difficulty
    // ======================================================================

    /// <summary>
    /// Sharpens this enemy for a later level. Cooldowns tighten, it circles harder
    /// and it shoots straighter -- but the wind-up only compresses part way and
    /// never past a floor, because that telegraph is the whole reason a crowd is
    /// survivable. Take the reaction window away and difficulty becomes unfairness.
    /// </summary>
    public void ApplyDifficulty(float aggression01)
    {
        Aggression = Mathf.Clamp01(aggression01);

        attackCooldown = Mathf.Max(0.35f, attackCooldown * Mathf.Lerp(1f, 0.6f, Aggression));
        attackWindup = Mathf.Max(0.18f, attackWindup * Mathf.Lerp(1f, 0.75f, Aggression));

        strafeAmount = Mathf.Clamp01(strafeAmount + 0.35f * Aggression);
        rangedSpread = Mathf.Max(0.5f, rangedSpread * Mathf.Lerp(1f, 0.55f, Aggression));

        // Breaking off is a level-one mercy, not a permanent way out. Later levels take
        // more fire before they give ground and spend less time behind it.
        suppressionDamage *= Mathf.Lerp(1f, 1.9f, Aggression);
        retreatDuration *= Mathf.Lerp(1f, 0.65f, Aggression);
        flinchCooldown *= Mathf.Lerp(1f, 1.5f, Aggression);

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

        _cleanBodyColor = (Color[])_restBodyColor.Clone();
    }

    /// <summary>
    /// Darkens one body part toward a colour -- the blood on a limb that has been shot up.
    ///
    /// Written into the rest colour rather than onto the renderer, because the wind-up
    /// flash repaints every renderer from the rest colours each frame it runs: a stain put
    /// anywhere else would be washed off by the next attack. Measured from the clean
    /// colour, so the same amount twice is the same stain, not a darker one.
    /// </summary>
    public void Stain(Renderer renderer, Color toward, float amount)
    {
        if (_renderers == null || _restBodyColor == null || _cleanBodyColor == null) return;

        int i = System.Array.IndexOf(_renderers, renderer);
        if (i < 0 || i >= _cleanBodyColor.Length) return;

        Color clean = _cleanBodyColor[i];
        Color stained = Color.Lerp(clean, toward, Mathf.Clamp01(amount));
        stained.a = clean.a;

        _restBodyColor[i] = stained;

        if (_flashing) return;

        var block = Block;
        renderer.GetPropertyBlock(block);
        block.SetColor("_BaseColor", stained);
        block.SetColor("_Color", stained);
        renderer.SetPropertyBlock(block);
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

    void PlayClip(AudioClip clip, float volume = 1f)
    {
        if (clip == null || _audio == null) return;

        _audio.pitch = Random.Range(0.94f, 1.06f);
        _audio.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // The range it swings at, which on a shooter is nowhere near the range it fires at.
        Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, meleeRange);

        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, separationRadius);
    }
}
