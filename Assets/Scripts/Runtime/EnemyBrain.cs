using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The enemy's mind: what it notices, what it makes of it, and what it does about it.
/// <see cref="EnemyAI"/> is the body it drives -- walking, turning, the wind-up and the
/// shot, the flinch and the stagger -- and asks this, every frame it is free to act, what
/// to do.
///
/// It behaves like a soldier on watch rather than a homing turret:
///
/// - **Calm.** It stands. It looks about. Now and then it walks a short beat round where
///   it was posted and stands again. Stillness is the default; a body that never stops
///   moving reads as a machine.
/// - **Noticing.** It sees in a cone, further in good light, less far at night, less of a
///   crouched player and more of a running one, and right round itself up close. What it
///   sees fills a suspicion that takes a moment to fill -- a glimpse at range is a "what
///   was that", not a bullet. It hears: footsteps near by, a shot from a long way off, a
///   blow, a body falling, a comrade calling out.
/// - **Thinking.** Suspicious, it stops, turns to the place and waits a beat; then goes to
///   look, carefully. Having seen the player, it reacts after a human pause before the
///   first shot, calls it out to anyone near, and fights.
/// - **Fighting.** Only a few shoot at once (<see cref="EnemySquad"/>); the rest take
///   cover, go round the side, or push up when the player is hidden. Badly hurt, it falls
///   back to cover. Disarmed, it rushes in with its hands. Lost the player, it goes to
///   where they were last seen and searches round, and gives up only after a while.
///
/// Rationed: it thinks a few times a second near the player and once a second far off.
/// </summary>
[DisallowMultipleComponent]
public class EnemyBrain : MonoBehaviour
{
    public enum Awareness { Calm, Suspicious, Searching, Alert, Combat }
    public enum Role { Rifleman, Flanker, Pusher }

    [Header("Eyes")]
    [Tooltip("Metres it can see a standing player in good light.")]
    [Range(10f, 150f)] public float sightRange = 55f;
    [Tooltip("Degrees of view, calm. Wider once it is hunting.")]
    [Range(40f, 200f)] public float calmFieldOfView = 120f;
    [Range(40f, 270f)] public float alertFieldOfView = 170f;
    [Tooltip("Metres within which it notices the player whatever way it is facing.")]
    [Range(0f, 6f)] public float closeSense = 2.5f;
    [Tooltip("Seconds of a clear look, at middle distance, to go from nothing to certain.")]
    [Range(0.2f, 5f)] public float noticeSeconds = 1.1f;

    [Header("Ears")]
    [Tooltip("Scales how far every sound carries to this enemy.")]
    [Range(0f, 2f)] public float hearing = 1f;

    [Header("Mind")]
    [Tooltip("Seconds between seeing the player and the first shot, at the easiest. A harder level shortens it.")]
    [Range(0.1f, 2f)] public float reactionSeconds = 0.5f;
    [Tooltip("Seconds it searches for a player it has lost before it settles down.")]
    [Range(5f, 90f)] public float searchSeconds = 28f;
    [Tooltip("Metres round its post it walks on a calm beat.")]
    [Range(0f, 40f)] public float patrolRadius = 20f;
    [Tooltip("Share of its health below which it falls back to cover.")]
    [Range(0f, 1f)] public float fallBackAt = 0.3f;

    [Tooltip("Metres inside which it stops shooting and fights hand to hand.")]
    [Range(0f, 8f)] public float closeQuarters = 3.5f;

    [Header("Moving, as shares of its full speed")]
    [Range(0.1f, 1f)] public float walkPace = 0.55f;
    [Range(0.1f, 1f)] public float carefulPace = 0.75f;
    [Range(0.3f, 1.3f)] public float runPace = 1.25f;

    // ======================================================================

    /// <summary>How aware it is right now.</summary>
    public Awareness Mind { get; private set; }

    /// <summary>What it is doing, in words, for a debug panel.</summary>
    public string Doing { get; private set; } = "standing";

    /// <summary>0 to 1: how sure it is something is there.</summary>
    public float Suspicion { get; private set; }

    /// <summary>Whether the rifle should be up and on the player (the body reads this).</summary>
    public bool WeaponUp => Mind >= Awareness.Alert;

    /// <summary>Where it is looking: the player, the noise, or whatever it is scanning.</summary>
    public Vector3 LookPoint { get; private set; }

    /// <summary>
    /// Alert and has had the player in sight this last moment: the face goes to the player's
    /// eyes, live, not to the last-known point (the body reads this).
    /// </summary>
    public bool FacesPlayer => WeaponUp && Time.time - _lastSeen < 1.5f;

    public Role Part { get; private set; }

    EnemyAI _ai;
    EnemyBody _body;
    Health _health;
    EnemyWounds _wounds;
    NavMeshAgent _agent;
    PlayerMotor _playerMotor;

    Vector3 _post;
    Vector3 _lastKnown;
    float _lastSeen = -99f;
    float _lastKnownTime = -99f;
    bool _sees;
    float _nextThink;
    float _reactAt;
    int _heardId;

    Vector3 _goal;
    bool _hasGoal;
    float _holdUntil;
    float _searchUntil;
    int _searchLeg;
    Vector3 _cover;
    bool _hasCover;
    float _nextCoverPick;
    float _flankSide;
    bool _fellBack;
    bool _crawlCoverTried, _limpCoverTried;
    bool _hasCrawlGoal;
    Vector3 _crawlGoal;
    float _callAt = -1f;
    Vector3 _callAtPoint;
    float _scanAngle;
    float _nextScan;

    static float s_light = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => s_light = -1f;

    void Awake()
    {
        _ai = GetComponent<EnemyAI>();
        _health = GetComponent<Health>();
        _wounds = GetComponent<EnemyWounds>();
        _agent = GetComponent<NavMeshAgent>();
        _body = GetComponent<EnemyBody>();
    }

    void OnEnable()
    {
        EnemySquad.Join(this);
        _heardId = NoiseSystem.Latest;
        if (_health != null)
        {
            _health.Damaged += OnDamaged;
            _health.Died += OnDied;
        }
    }

    /// <summary>A body going down is heard by anyone near: they come to see.</summary>
    void OnDied(Health h)
    {
        EnemySquad.ReleaseTurn(this);
        EnemySquad.ReportLoss(transform.position);
        NoiseSystem.Emit(transform.position, 25f, NoiseKind.Violence, gameObject);
    }

    void OnDisable()
    {
        EnemySquad.Leave(this);
        if (_health != null)
        {
            _health.Damaged -= OnDamaged;
            _health.Died -= OnDied;
        }
    }

    void Start()
    {
        _post = transform.position;
        LookPoint = transform.position + transform.forward * 10f + Vector3.up * 1.6f;

        // A mix of temperaments in every group: most hold and shoot, some go round, some push.
        float r = Random.value;
        Part = r < 0.55f ? Role.Rifleman : r < 0.8f ? Role.Flanker : Role.Pusher;
        _flankSide = Random.value < 0.5f ? -1f : 1f;

        if (_ai != null && _ai.target != null) _playerMotor = _ai.target.GetComponent<PlayerMotor>();

        // A boss knows the player is here: it was sent for them.
        if (_ai != null && _ai.archetype != null && _ai.archetype.role == EnemyArchetype.Role.Boss && _ai.target != null)
            Learn(_ai.target.position, Awareness.Alert);
    }

    // ======================================================================
    // Being told things
    // ======================================================================

    /// <summary>Shot, or hit: it knows, and roughly from where.</summary>
    void OnDamaged(Health h, DamageInfo info)
    {
        if (_ai == null || _ai.target == null) return;
        Vector3 from = info.source != null ? info.source.transform.position : _ai.target.position;
        Learn(from, Awareness.Combat);
        _sees = true;
        _lastThreat = Time.time;
    }

    /// <summary>A comrade calling the player out. Believed after a beat.</summary>
    public void HearCall(Vector3 playerAt, float delay)
    {
        if (Mind >= Awareness.Combat) return;
        _callAt = Time.time + delay;
        _callAtPoint = playerAt;
    }

    /// <summary>The radio: the player reported somewhere near here. Worth a careful look.</summary>
    public void HearReport(Vector3 near)
    {
        if (Mind >= Awareness.Searching) return;
        Learn(near, Awareness.Searching);
        _lastKnownTime = Time.time;
        Doing = "answering the radio";
    }

    void Learn(Vector3 where, Awareness at)
    {
        _lastKnown = where;
        _lastKnownTime = Time.time;
        // A plan is dropped only when the mind changes up a level; being told again what it
        // already knows (every glance at a player in plain sight) must not wipe the move it
        // is making -- that kept every rifleman rooted to its first firing spot.
        if (at > Mind)
        {
            if (at >= Awareness.Combat && Mind < Awareness.Combat) _reactAt = Time.time + ReactionTime;
            Mind = at;
            _hasGoal = false;
        }
        if (Mind >= Awareness.Alert)
        {
            Suspicion = 1f;
            _searchUntil = Time.time + searchSeconds;
            if (_ai != null) _ai.MarkSpotted();
        }
        else
        {
            Suspicion = Mathf.Max(Suspicion, 0.5f);
            _searchUntil = Time.time + searchSeconds * 0.5f;
        }
    }

    float Aggression => _ai != null ? _ai.Aggression : 0.5f;
    float ReactionTime => Mathf.Lerp(reactionSeconds, reactionSeconds * 0.35f, Aggression) * Random.Range(0.8f, 1.25f);

    // ======================================================================
    // Each frame the body is free
    // ======================================================================

    /// <summary>Called by EnemyAI every frame it is not staggered, dead or fleeing.</summary>
    public void Tick()
    {
        if (_ai == null || _ai.target == null) return;
        EnemySquad.Tick(_ai.target);

        if (Time.time >= _nextThink)
        {
            float distance = Vector3.Distance(transform.position, _ai.target.position);
            float interval = distance < 40f ? 0.18f : distance < 90f ? 0.45f : 1f;
            _nextThink = Time.time + interval * Random.Range(0.85f, 1.15f);
            Perceive(interval);
            Decide();
        }

        if (!_ai.Attacking) _ai.HasAimPoint = false;
        // Down, getting up, or fetching its rifle: the body has it for now. It keeps noticing.
        if (_body != null && _body.Busy) return;
        _ai.WantCrouch = false;
        if (Hurting()) return;
        if (Evade()) return;
        Act();
    }

    // ---- senses -------------------------------------------------------------

    /// <summary>How well things can be seen in this arena: the sun's strength and the ambient light.</summary>
    static float Light
    {
        get
        {
            if (s_light >= 0f) return s_light;
            float sun = RenderSettings.sun != null && RenderSettings.sun.isActiveAndEnabled ? RenderSettings.sun.intensity : 0.3f;
            Color a = RenderSettings.ambientLight;
            float ambient = a.r * 0.3f + a.g * 0.59f + a.b * 0.11f;
            s_light = Mathf.Clamp01(0.25f + 0.55f * Mathf.Clamp01(sun) + 0.6f * ambient);
            return s_light;
        }
    }

    void Perceive(float dt)
    {
        var target = _ai.target;
        Vector3 eye = _ai.eyes != null ? _ai.eyes.position : transform.position + Vector3.up * 1.7f;

        bool crouched = _playerMotor != null && _playerMotor.IsCrouching;
        Vector3 body = target.position + Vector3.up * (crouched ? 0.9f : 1.4f);
        Vector3 to = body - eye;
        float distance = to.magnitude;

        // How far it sees the player right now: the light, the way they move, its own state.
        float range = sightRange * Mathf.Lerp(0.5f, 1f, Light);
        if (crouched) range *= 0.6f;
        if (_playerMotor != null)
        {
            if (_playerMotor.IsSprinting) range *= 1.25f;
            else if (_playerMotor.PlanarSpeed01 < 0.05f) range *= 0.85f;
        }
        if (Mind >= Awareness.Alert) range *= 1.4f;

        float fov = Mind >= Awareness.Alert ? alertFieldOfView : calmFieldOfView;
        float angle = Vector3.Angle(transform.forward, Vector3.ProjectOnPlane(to, Vector3.up));

        bool inView = distance <= closeSense || (distance <= range && angle <= fov * 0.5f);
        _sees = inView && _ai.SeesTarget();

        if (_sees)
        {
            // Filled faster close, straight ahead and by movement; slowly at the edge of
            // sight or of the light. Certain at once once already fighting.
            float closeness = 1f - Mathf.Clamp01(distance / Mathf.Max(1f, range));
            float centre = 1f - Mathf.Clamp01(angle / Mathf.Max(1f, fov * 0.5f)) * 0.6f;
            float rate = (0.35f + 1.6f * closeness) * centre / Mathf.Max(0.1f, noticeSeconds);
            if (distance <= closeSense || Mind >= Awareness.Alert) rate = 99f;

            Suspicion = Mathf.Clamp01(Suspicion + rate * dt);
            if (Suspicion >= 1f)
            {
                bool first = Mind < Awareness.Combat;
                Learn(target.position, Awareness.Combat);
                _lastSeen = Time.time;
                if (first) EnemySquad.Shout(this, target.position, 32f);
            }
            else if (Suspicion >= 0.35f && Mind < Awareness.Suspicious)
            {
                Mind = Awareness.Suspicious;
                _lastKnown = target.position;
                _lastKnownTime = Time.time;
                _holdUntil = Time.time + Random.Range(1.2f, 2.4f);
                _hasGoal = false;
                Doing = "what was that?";
            }
        }
        else if (Mind < Awareness.Alert)
        {
            Suspicion = Mathf.Max(0f, Suspicion - dt * 0.12f);
        }

        // Ears.
        _heardId = NoiseSystem.Listen(eye, hearing, _heardId, Heard);

        // A comrade's call, believed now.
        if (_callAt > 0f && Time.time >= _callAt)
        {
            _callAt = -1f;
            Learn(_callAtPoint, Awareness.Alert);
            Doing = "called in";
        }
    }

    void Heard(NoiseSystem.Noise n, float distance)
    {
        if (n.source == gameObject) return;
        // The farther off, the vaguer: a position known to within a tenth of the distance.
        Vector3 guess = n.position + Random.insideUnitSphere.normalized * distance * 0.12f;
        guess.y = n.position.y;

        switch (n.kind)
        {
            case NoiseKind.Gunfire:
            case NoiseKind.Blast:
                Learn(guess, Awareness.Alert);
                Doing = "heard shots";
                break;
            case NoiseKind.Violence:
                Learn(guess, distance < 10f ? Awareness.Alert : Awareness.Searching);
                Doing = "heard a fight";
                break;
            case NoiseKind.Movement:
                if (Mind >= Awareness.Alert) { _lastKnown = guess; _lastKnownTime = Time.time; break; }
                Suspicion = Mathf.Min(1f, Suspicion + 0.45f);
                if (Mind < Awareness.Suspicious)
                {
                    Mind = Awareness.Suspicious;
                    _lastKnown = guess;
                    _lastKnownTime = Time.time;
                    _holdUntil = Time.time + Random.Range(0.8f, 1.8f);
                    _hasGoal = false;
                    Doing = "heard footsteps";
                }
                break;
        }
    }

    // ---- deciding -------------------------------------------------------------

    void Decide()
    {
        // Combat cools to a search once the player has been out of sight a while; a search
        // gives up after its time and it settles back to calm, still wary for a bit.
        if (Mind == Awareness.Combat && !_sees && Time.time - _lastSeen > 4f)
        {
            Mind = Awareness.Searching;
            _searchLeg = 0;
            _hasGoal = false;
            EnemySquad.ReleaseTurn(this);
        }
        if (Mind == Awareness.Alert && _sees) Mind = Awareness.Combat;
        if ((Mind == Awareness.Searching || Mind == Awareness.Alert) && Time.time > _searchUntil)
        {
            Mind = Awareness.Calm;
            Suspicion = 0.2f;
            _hasGoal = false;
            Doing = "standing down";
        }
        if (Mind == Awareness.Suspicious && Suspicion < 0.15f)
        {
            Mind = Awareness.Calm;
            _hasGoal = false;
        }
    }

    // ---- doing ----------------------------------------------------------------

    void Act()
    {
        switch (Mind)
        {
            case Awareness.Calm: Calm(); break;
            case Awareness.Suspicious: Suspicious(); break;
            case Awareness.Searching:
            case Awareness.Alert: Hunt(); break;
            case Awareness.Combat: Fight(); break;
        }
    }

    /// <summary>Stands, looks about, now and then walks a short beat and stands again.</summary>
    void Calm()
    {
        _ai.ReportState(EnemyAI.State.Idle);
        Scan(0.9f);

        if (_hasGoal)
        {
            Doing = "on a walk";
            _ai.MoveTo(_goal, walkPace);
            if (Reached(_goal))
            {
                _hasGoal = false;
                _holdUntil = Time.time + Random.Range(1.5f, 4f);
            }
            return;
        }

        _ai.Hold();
        if (Time.time < _holdUntil) { Doing = "standing watch"; return; }

        if (patrolRadius > 0f && Random.value < 0.85f && RandomPoint(_post, patrolRadius, out _goal)) _hasGoal = true;
        else _holdUntil = Time.time + Random.Range(1.5f, 3.5f);
    }

    /// <summary>Stops and looks at it, thinks, then goes to see -- carefully.</summary>
    void Suspicious()
    {
        _ai.ReportState(EnemyAI.State.Idle);
        LookPoint = _lastKnown + Vector3.up * 1.4f;

        if (Time.time < _holdUntil)
        {
            _ai.Hold();
            _ai.FaceTowards(_lastKnown, 4f);
            return;
        }

        Doing = "going to look";
        _ai.ReportState(EnemyAI.State.Chase);
        _ai.MoveTo(_lastKnown, carefulPace);
        if (Reached(_lastKnown) || _ai.Arrived && Time.time - _lastKnownTime > 4f)
        {
            // Nothing there: a look round, and the suspicion fades.
            _ai.Hold();
            Scan(2f);
            Suspicion = Mathf.Max(0f, Suspicion - Time.deltaTime * 0.25f);
            Doing = "looking round";
        }
    }

    /// <summary>To where the player was, then round about it.</summary>
    void Hunt()
    {
        _ai.ReportState(EnemyAI.State.Chase);

        // On the ground for good, it does not go looking: it watches where the player was,
        // from cover if it can drag itself to some.
        if (_ai.Crawling)
        {
            LookPoint = _lastKnown + Vector3.up;
            if (_hasCrawlGoal && !Reached(_crawlGoal)) { Doing = "crawling to cover"; _ai.MoveTo(_crawlGoal, 1f); }
            else { Doing = "watching from the ground"; _ai.Hold(); }
            return;
        }

        if (!_hasGoal)
        {
            Vector3 centre = _lastKnown;
            if (_searchLeg == 0 || !RandomPoint(centre, 9f, out _goal))
                _goal = NavMesh.SamplePosition(centre, out var onMesh, 6f, NavMesh.AllAreas) ? onMesh.position : centre;
            _searchLeg++;
            _hasGoal = true;
            _legStart = Time.time;
            _legFrom = Vector3.Distance(transform.position, _goal);
        }

        // A route that has taken it further away than it started, for a while, is the wrong
        // one: a wall between, the way round too long. Try a point nearer instead.
        if (Time.time - _legStart > 5f && Vector3.Distance(transform.position, _goal) > _legFrom + 4f)
        {
            _hasGoal = RandomPoint(Vector3.Lerp(transform.position, _lastKnown, 0.5f), 6f, out _goal);
            _legStart = Time.time;
            _legFrom = Vector3.Distance(transform.position, _goal);
        }

        Doing = Mind == Awareness.Alert ? "moving on the player" : "searching";
        _ai.MoveTo(_goal, Mind == Awareness.Alert ? runPace * 0.85f : carefulPace);
        LookPoint = _goal + Vector3.up * 1.5f;
        Scan(1.5f);

        if (_hasGoal && Reached(_goal))
        {
            _hasGoal = false;
            _ai.Hold();
        }
    }

    float _legStart, _legFrom;

    /// <summary>The fight: shoot on a turn, otherwise cover, flank or push; fall back when hurt.</summary>
    void Fight()
    {
        var target = _ai.target;
        if (_sees) { _lastKnown = target.position; _lastKnownTime = Time.time; _lastSeen = Time.time; }
        LookPoint = _lastKnown + Vector3.up * 1.4f;

        float distance = Vector3.Distance(transform.position, target.position);

        // Up from a kick and angry: square up, then straight at the player with the fists.
        if (_body != null && _body.Raging)
        {
            _ai.ReportState(EnemyAI.State.Chase);
            if (_body.Squaring)
            {
                Doing = "squaring up";
                _ai.Hold();
                _ai.FaceTowards(target.position, 10f);
                return;
            }

            Doing = "charging in a rage";
            if (distance <= _ai.meleeRange && _ai.TryAttack()) return;
            if (!_ai.Attacking) _ai.MoveTo(target.position, 1.3f);
            return;
        }

        // Close quarters: nobody fires a rifle into a man's chest at arm's length. Inside a
        // few metres it slings the gun and fights with its fists; back off and it unslings.
        bool handToHand = _ai.ranged && !_ai.Crawling && distance <= closeQuarters
                          || (_body != null && _body.HandToHand && distance <= closeQuarters + 1.5f);
        if (_body != null) _body.HandToHand = handToHand;
        if (handToHand)
        {
            _ai.ReportState(EnemyAI.State.Chase);
            Doing = "fighting hand to hand";
            _ai.FaceTowards(target.position, 12f);
            if (distance <= _ai.meleeRange && Time.time >= _reactAt && _ai.TryAttack()) return;
            if (!_ai.Attacking) _ai.MoveTo(target.position, runPace);
            return;
        }

        // Both arms gone, and the rifle with them: nothing to fight with at range. Close in,
        // it goes for the player with what it has; further off, it runs.
        var wounds = _wounds;
        if (!_ai.ranged && wounds != null && wounds.Disarmed && distance > 6f)
        {
            Doing = "fleeing";
            _ai.ReportState(EnemyAI.State.Retreat);
            if (!_hasGoal || Reached(_goal))
            {
                Vector3 away = transform.position - target.position;
                away.y = 0f;
                Vector3 run = transform.position + (away.sqrMagnitude > 0.01f ? away.normalized : -transform.forward) * 18f;
                if (FindCover(target.position, 15f, 45f, out _cover)) { _goal = _cover; _hasGoal = true; }
                else if (NavMesh.SamplePosition(run, out var far, 6f, NavMesh.AllAreas)) { _goal = far.position; _hasGoal = true; }
            }
            if (_hasGoal) _ai.MoveTo(_goal, runPace);
            return;
        }

        // Hands only: in it goes, as fast as its legs allow.
        if (!_ai.ranged)
        {
            Doing = "rushing in";
            _ai.ReportState(EnemyAI.State.Chase);
            if (distance <= _ai.attackRange && _ai.TryAttack()) return;
            _ai.MoveTo(target.position, runPace * 1.1f);
            return;
        }

        // On the floor for good: it drags itself to cover if there is some close by, and
        // shoots from the ground -- on the way when it has a shot, and from there after.
        if (_ai.Crawling)
        {
            if (!_crawlCoverTried)
            {
                _crawlCoverTried = true;
                _hasCrawlGoal = FindCover(target.position, 3f, 60f, out _crawlGoal)
                                && (_crawlGoal - transform.position).sqrMagnitude < 8f * 8f;
            }
            if (_hasCrawlGoal && Reached(_crawlGoal)) _hasCrawlGoal = false;

            if (_sees && Time.time >= _reactAt && (!_hasCrawlGoal || Random.value < 0.02f) && _ai.TryAttack())
            {
                Doing = "firing from the ground";
                return;
            }
            if (_hasCrawlGoal && !_ai.Attacking)
            {
                Doing = "crawling to cover";
                _ai.MoveTo(_crawlGoal, 1f);
                return;
            }
            Doing = "fighting from the ground";
            _ai.Hold();
            return;
        }

        // A leg just went: it gets to cover first, and fights from there.
        if (!_limpCoverTried && wounds != null && wounds.Legs == EnemyWounds.LegState.Limping)
        {
            _limpCoverTried = true;
            if (FindCover(target.position, 8f, 40f, out _cover)) { _hasCover = true; _goal = _cover; _hasGoal = true; }
        }

        // Pinned: a bullet cracked past. Break for cover hard, and keep the head down there
        // until the fire lets up -- then come up and fight again.
        if (Time.time < _suppressedUntil)
        {
            _ai.ReportState(EnemyAI.State.Chase);
            if (_hasGoal && !Reached(_goal))
            {
                Doing = _hasCover ? "breaking for cover" : "diving clear";
                _ai.MoveTo(Zigzag(_goal), runPace * 1.1f);
                return;
            }
            _hasGoal = false;
            if (_hasCover)
            {
                Doing = "keeping its head down";
                _ai.WantCrouch = true;
                _ai.Hold();
                LookPoint = _lastKnown + Vector3.up * 1.4f;
                return;
            }
        }

        // Being aimed at in the open: a sidestep dash spoils the shot.
        if (_sees && !_hasGoal && distance > 6f && AimedAt(distance) && Time.time >= _nextDodge && !_ai.Attacking)
        {
            _nextDodge = Time.time + Random.Range(2.5f, 5f) * Mathf.Lerp(1.4f, 0.8f, Aggression);
            if (Random.value < 0.7f && SideStep(target.position, out _goal))
            {
                _hasGoal = true;
                _hasCover = false;
                Doing = "dodging";
                _ai.MoveTo(_goal, runPace * 1.1f);
                return;
            }
        }

        Think(distance);

        // Badly hurt: back to cover once, and fight from there.
        float health = _health != null ? _health.Normalized : 1f;
        if (!_fellBack && health <= fallBackAt)
        {
            _fellBack = true;
            if (FindCover(target.position, 10f, 30f, out _cover)) { _hasCover = true; _goal = _cover; _hasGoal = true; }
        }

        // Bounding, the way infantry fight: run to a firing spot, stop, put a burst down,
        // run to the next. Never standing in the open for long, never still for the sake of it.
        bool turn = _sees && Time.time >= _reactAt && EnemySquad.RequestTurn(this, Aggression);


        if (_hasGoal && Reached(_goal)) _hasGoal = false;
        bool atSpot = !_hasGoal;

        if (turn && atSpot)
        {
            Doing = "firing";
            _ai.ReportState(EnemyAI.State.Attack);
            _ai.Hold();
            _ai.FaceTowards(target.position, 12f);
            if (_ai.TryAttack()) _shotsHere++;

            // A burst or two from one spot, then move: a shooter that stays put is a target.
            if (_shotsHere >= _burstsPerSpot && !_ai.Attacking)
            {
                _shotsHere = 0;
                _burstsPerSpot = _tactic == Tactic.Hold ? Random.Range(2, 5) : _tactic == Tactic.Bound ? Random.Range(1, 3) : 1;
                _hasGoal = PickFiringSpot(target.position, distance, out _goal);
                if (_hasGoal) _ai.MoveTo(_goal, runPace);
            }
            return;
        }

        _ai.ReportState(EnemyAI.State.Chase);

        // On its way somewhere: keep going, fast -- unless it has stopped getting anywhere,
        // a spot it cannot reach; then it gives up on it.
        if (_hasGoal)
        {
            if (_agent != null && _agent.enabled && _agent.velocity.sqrMagnitude < 0.04f)
            {
                if (_stuckSince < 0f) _stuckSince = Time.time;
                else if (Time.time - _stuckSince > 1.5f) { _hasGoal = false; _stuckSince = -1f; return; }
            }
            else _stuckSince = -1f;

            Doing = _hasCover ? "running to cover" : "repositioning";
            _ai.MoveTo(_goal, runPace);
            return;
        }

        // Waiting for a turn, or the player is out of sight: never more than a moment's pause.
        if (_sees)
        {
            if (Time.time >= _nextMove)
            {
                _nextMove = Time.time + (_tactic == Tactic.Hold || _tactic == Tactic.Retreat && _retreated ? Random.Range(6f, 9f)
                                       : _tactic == Tactic.Assault ? Random.Range(1.2f, 2.2f) : Random.Range(2.5f, 4f));
                _hasGoal = PickFiringSpot(target.position, distance, out _goal);
                if (_hasGoal) { _ai.MoveTo(_goal, runPace); return; }
            }
            Doing = "covering";
            _ai.Hold();
            _ai.FaceTowards(target.position, 8f);
            return;
        }

        // Just lost sight of the player: whoever has a turn puts rounds into where they went,
        // to keep them pinned while the others move.
        if (Time.time - _lastSeen < 3f && Part != Role.Flanker && EnemySquad.RequestTurn(this, Aggression)
            && Vector3.Distance(transform.position, _lastKnown) > closeQuarters + 2f)
        {
            Doing = "suppressing";
            _ai.ReportState(EnemyAI.State.Attack);
            _ai.Hold();
            _ai.FaceTowards(_lastKnown, 10f);
            _ai.HasAimPoint = true;
            _ai.AimPoint = _lastKnown + Vector3.up * Random.Range(0.6f, 1.6f) + Random.insideUnitSphere * 0.6f;
            _ai.TryAttack();
            return;
        }

        // Out of sight. Dug in or falling back: it watches the corner and lets the player come.
        if ((_tactic == Tactic.Hold || _tactic == Tactic.Retreat) && Time.time - _lastSeen < 6f)
        {
            Doing = "watching the corner";
            _ai.Hold();
            _ai.FaceTowards(_lastKnown, 6f);
            return;
        }

        // Otherwise the plan (or the role) decides how it gets a line again.
        _hasCover = false;
        Role way = _tactic == Tactic.Flank ? Role.Flanker : _tactic == Tactic.Assault ? Role.Pusher : Part;
        switch (way)
        {
            case Role.Flanker:
            {
                Doing = "going round";
                Vector3 across = Vector3.Cross(Vector3.up, (_lastKnown - transform.position).normalized) * _flankSide;
                Vector3 spot = _lastKnown + across * Mathf.Clamp(distance * 0.6f, 8f, 20f);
                if (NavMesh.SamplePosition(spot, out var hit, 6f, NavMesh.AllAreas)) _ai.MoveTo(hit.position, runPace);
                else _ai.MoveTo(_lastKnown, runPace);
                break;
            }
            case Role.Pusher:
                Doing = "pushing up";
                _ai.MoveTo(_lastKnown, runPace);
                break;
            default:
                Doing = "moving up";
                _ai.MoveTo(Vector3.Lerp(transform.position, _lastKnown, 0.7f), runPace * 0.85f);
                break;
        }
    }

    /// <summary>The fight's bookkeeping, for a debug panel and the behaviour test.</summary>
    public string FightState => $"{_tactic} bursts {_shotsHere}/{_burstsPerSpot} goal {(_hasGoal ? Vector3.Distance(transform.position, _goal).ToString("0.0") + "m" : "none")}";

    /// <summary>Close enough to a spot to count as there, by its own position, not the agent's last path.</summary>
    bool Reached(Vector3 spot)
    {
        Vector3 d = spot - transform.position;
        d.y = 0f;
        return d.sqrMagnitude < 1.5f * 1.5f;
    }

    // ---- the body in pain ----------------------------------------------------------

    /// <summary>
    /// While the body is buckled, clutching or doubled over, the mind waits on it: it stays
    /// put and turns to the threat, and fires only where the body can (from one knee).
    /// </summary>
    bool Hurting()
    {
        if (_body == null || !_body.InPain) return false;
        Doing = _body.PainDoing;
        _ai.Hold();
        _hasGoal = false;
        if (Mind >= Awareness.Alert) _ai.FaceTowards(_lastKnown, 4f);
        if (_body.CanFireInPain && _sees && Mind >= Awareness.Combat && Time.time >= _reactAt && _ai.ranged)
        {
            _ai.ReportState(EnemyAI.State.Attack);
            _ai.TryAttack();
        }
        return true;
    }

    // ---- the plan -------------------------------------------------------------------

    /// <summary>
    /// What it is trying to do in the fight, re-weighed about once a second:
    /// <b>Bound</b> (fire and move), <b>Assault</b> (close the gap), <b>Flank</b> (go round),
    /// <b>Hold</b> (dig into cover and shoot from it), <b>Retreat</b> (back out to far cover).
    /// </summary>
    public enum Tactic { Bound, Assault, Flank, Hold, Retreat }
    public Tactic Plan => _tactic;
    Tactic _tactic = Tactic.Bound;
    float _nextPlan;
    float _lastThreat = -99f;
    bool _retreated;
    Weapon[] _playerWeapons;
    Health _playerHealth;

    // What it reads off the fight, each 0..1:
    //  0 own health, 1 legs hurt, 2 arms hurt, 3 under fire, 4 player reloading,
    //  5 comrades fighting near, 6 range (far = 1), 7 being watched, 8 sees the player,
    //  9 comrades lost near lately, 10 player hurt, 11 bias.
    const int Inputs = 12;
    static readonly float[,] s_weights =
    {
        //  hp    legs  arms  fire  rld   mates range watch sees  loss  pHurt bias
        {  0.5f, -0.6f,  0f,  -0.2f, 0.3f, 0.3f,  0f,   0.2f, 0.6f,  0f,   0f,   0.3f }, // Bound
        {  1.2f, -1.6f, -0.5f, -1.0f, 2.6f, 0.8f,  0.3f, -0.8f, 0.2f, -0.8f, 1.5f, -1.3f }, // Assault
        {  0.6f, -1.6f,  0f,  -0.3f, 0.5f, 1.0f,  0.2f, -1.0f,-0.2f,  0f,   0.3f, -0.7f }, // Flank
        { -0.2f,  1.2f,  0.4f,  0.8f,-1.0f, 0f,  -0.3f,  0.4f, 0.3f,  0.4f, -0.5f,  0f  }, // Hold
        { -2.6f,  0.6f,  0.6f,  0.8f,-1.5f,-0.8f, -0.5f,  0.3f, 0f,   1.0f, -0.8f,  1.6f }, // Retreat
    };
    float[,] _weights;
    readonly float[] _x = new float[Inputs];

    /// <summary>
    /// Weighs the fight and picks a plan: a small hand-wired network, the inputs above times
    /// a weight per plan, summed. Each soldier's weights are the table above with its own
    /// temperament mixed in (a fifth either way), so two soldiers in the same spot can
    /// choose differently. The plan it already has gets a bonus, so it does not dither.
    /// </summary>
    void Think(float distance)
    {
        if (Time.time < _nextPlan) return;
        _nextPlan = Time.time + Random.Range(0.8f, 1.2f);

        if (_weights == null)
        {
            _weights = new float[5, Inputs];
            for (int o = 0; o < 5; o++)
                for (int i = 0; i < Inputs; i++)
                    _weights[o, i] = s_weights[o, i] * Random.Range(0.8f, 1.2f);
            // The role leans it: pushers push, flankers go round, riflemen dig in.
            _weights[(int)Tactic.Assault, Inputs - 1] += Part == Role.Pusher ? 0.7f : 0f;
            _weights[(int)Tactic.Flank, Inputs - 1] += Part == Role.Flanker ? 0.9f : 0f;
            _weights[(int)Tactic.Hold, Inputs - 1] += Part == Role.Rifleman ? 0.3f : 0f;
            _weights[(int)Tactic.Assault, Inputs - 1] += Aggression * 1.0f;
            _weights[(int)Tactic.Retreat, Inputs - 1] -= Aggression * 0.8f;
        }

        var target = _ai.target;
        _playerWeapons ??= target.GetComponentsInChildren<Weapon>(true);
        if (_playerHealth == null) _playerHealth = target.GetComponent<Health>();
        bool reloading = false;
        foreach (var w in _playerWeapons)
            if (w != null && w.isActiveAndEnabled && (w.IsReloading || w.CurrentAmmo == 0)) reloading = true;

        float mates = 0f, losses = 0f;
        foreach (var other in EnemySquad.All)
            if (other != null && other != this && other.Mind == Awareness.Combat
                && (other.transform.position - transform.position).sqrMagnitude < 35f * 35f) mates += 1f;
        losses = EnemySquad.LossesNear(transform.position, 25f, 12f);

        float want = _ai.preferredRangedDistance > 0f ? _ai.preferredRangedDistance : 14f;
        _x[0] = _health != null ? _health.Normalized : 1f;
        _x[1] = _wounds != null ? Mathf.Clamp01(Mathf.Max(_wounds.Severity(BodyPart.LeftLeg), _wounds.Severity(BodyPart.RightLeg)) * 2f) : 0f;
        _x[2] = _wounds != null ? Mathf.Clamp01(Mathf.Max(_wounds.Severity(BodyPart.LeftArm), _wounds.Severity(BodyPart.RightArm)) * 2f) : 0f;
        _x[3] = Mathf.Exp(-(Time.time - _lastThreat) / 3f);
        _x[4] = reloading ? 1f : 0f;
        _x[5] = Mathf.Clamp01(mates / 3f);
        _x[6] = Mathf.Clamp01(distance / (want * 2f));
        _x[7] = Watched() ? 1f : 0f;
        _x[8] = _sees ? 1f : 0f;
        _x[9] = Mathf.Clamp01(losses / 2f);
        _x[10] = _playerHealth != null ? 1f - _playerHealth.Normalized : 0f;
        _x[11] = 1f;

        int best = (int)_tactic;
        float bestScore = float.MinValue;
        for (int o = 0; o < 5; o++)
        {
            float s = 0f;
            for (int i = 0; i < Inputs; i++) s += _weights[o, i] * _x[i];
            if (o == (int)_tactic) s += 0.25f;
            if (s > bestScore) { bestScore = s; best = o; }
        }

        var next = (Tactic)best;
        if (next == _tactic) return;
        // A new plan: what it was doing for the old one no longer applies (unless it is
        // in the middle of getting out of fire).
        if (_tactic == Tactic.Retreat) _retreated = false;
        _tactic = next;
        if (Time.time >= _suppressedUntil) { _hasGoal = false; _nextMove = 0f; _shotsHere = 0; }
    }

    /// <summary>The player facing it, roughly: within fifteen degrees of the view.</summary>
    bool Watched()
    {
        var cam = Camera.main;
        if (cam == null) return false;
        Vector3 to = transform.position + Vector3.up * 1.2f - cam.transform.position;
        return Vector3.Angle(cam.transform.forward, to) < 15f;
    }

    // ---- danger ---------------------------------------------------------------

    float _suppressedUntil = -1f;
    float _nextDodge;
    float _aimedSince = -1f;
    float _evadeUntil = -1f;
    Vector3 _evadeGoal;
    Vector3 _evadeFrom;
    float _zigAt;
    float _zigSide = 1f;

    /// <summary>A bullet cracked past it from <paramref name="from"/>.</summary>
    public void NearMiss(Vector3 from, float miss)
    {
        if (_ai == null || _ai.target == null || _health == null || _health.IsDead) return;

        // A shot that close gives away roughly where it came from.
        Learn(_ai.target.position, Awareness.Combat);
        _lastThreat = Time.time;
        _sees = _sees || miss < 1.5f;
        if (_body != null && (_body.Raging || _body.HandToHand)) return;
        if (_ai.Crawling || !_ai.ranged) return;

        bool already = Time.time < _suppressedUntil;
        _suppressedUntil = Mathf.Max(_suppressedUntil, Time.time + Random.Range(1.8f, 3.2f));
        if (already && (_hasGoal || _hasCover)) return;

        if (FindCover(from, 4f, 80f, out _cover))
        {
            _hasCover = true;
            _goal = _cover;
            _hasGoal = true;
        }
        else
        {
            _hasCover = false;
            _hasGoal = SideStep(from, out _goal);
        }
        _shotsHere = 0;
        if (_hasGoal) _ai.MoveTo(_goal, runPace * 1.1f);
    }

    /// <summary>A bomb coming down near it: out of the blast, now.</summary>
    public void Danger(Vector3 at, float radius, float seconds)
    {
        if (_health == null || _health.IsDead) return;
        Learn(_ai != null && _ai.target != null ? _ai.target.position : at, Awareness.Combat);

        Vector3 away = transform.position - at;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
        away.y = 0f;
        away.Normalize();
        for (int i = 0; i < 5; i++)
        {
            Vector3 dir = Quaternion.AngleAxis(Random.Range(-60f, 60f) * i * 0.5f, Vector3.up) * away;
            if (NavMesh.SamplePosition(at + dir * (radius + 3f), out var hit, 3f, NavMesh.AllAreas))
            {
                _evadeGoal = hit.position;
                _evadeFrom = at;
                // It reacts on sight of the throw: a beat, then runs until well after it goes off.
                _evadeUntil = Time.time + seconds + 0.6f;
                _hasGoal = false;
                return;
            }
        }
    }

    /// <summary>Running from a bomb. Overrides anything else it was doing.</summary>
    bool Evade()
    {
        if (Time.time >= _evadeUntil) return false;
        if (Reached(_evadeGoal)) { _evadeUntil = -1f; return false; }
        Doing = "running from a grenade";
        _ai.ReportState(EnemyAI.State.Chase);
        LookPoint = _evadeFrom + Vector3.up * 0.3f;
        _ai.MoveTo(_evadeGoal, 1.3f);
        return true;
    }

    /// <summary>
    /// Whether the player's sights are on it, held a moment: the crosshair within a body's
    /// width of its chest for a third of a second.
    /// </summary>
    bool AimedAt(float distance)
    {
        var cam = Camera.main;
        if (cam == null) { _aimedSince = -1f; return false; }
        Vector3 chest = transform.position + Vector3.up * 1.2f;
        Vector3 to = chest - cam.transform.position;
        float off = Vector3.Cross(cam.transform.forward, to).magnitude;
        bool on = Vector3.Dot(cam.transform.forward, to) > 0f && off < 0.9f;
        if (!on) { _aimedSince = -1f; return false; }
        if (_aimedSince < 0f) _aimedSince = Time.time;
        return Time.time - _aimedSince > 0.35f;
    }

    /// <summary>A short dash square to the threat's line, three to five metres.</summary>
    bool SideStep(Vector3 threat, out Vector3 spot)
    {
        Vector3 from = transform.position - threat;
        from.y = 0f;
        if (from.sqrMagnitude < 0.01f) from = -transform.forward;
        Vector3 side = Vector3.Cross(Vector3.up, from.normalized) * (Random.value < 0.5f ? -1f : 1f);
        for (int i = 0; i < 2; i++)
        {
            if (NavMesh.SamplePosition(transform.position + side * Random.Range(3f, 5f), out var hit, 2f, NavMesh.AllAreas)
                && (hit.position - transform.position).sqrMagnitude > 2f * 2f)
            {
                spot = hit.position;
                return true;
            }
            side = -side;
        }
        spot = transform.position;
        return false;
    }

    /// <summary>
    /// Running under fire, not in a straight line: the target point is pushed a little to
    /// one side, then the other, every half second or so, while it is still a way off.
    /// </summary>
    Vector3 Zigzag(Vector3 goal)
    {
        Vector3 to = goal - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 5f * 5f) return goal;
        if (Time.time >= _zigAt)
        {
            _zigAt = Time.time + Random.Range(0.4f, 0.7f);
            _zigSide = -_zigSide;
        }
        Vector3 side = Vector3.Cross(Vector3.up, to.normalized) * _zigSide * 1.6f;
        Vector3 near = transform.position + to.normalized * 4f + side;
        return NavMesh.SamplePosition(near, out var hit, 1.5f, NavMesh.AllAreas) ? hit.position : goal;
    }

    float _stuckSince = -1f;
    int _shotsHere;
    int _burstsPerSpot = 1;
    float _nextMove;

    /// <summary>
    /// Where to fire from next: cover if there is some near, otherwise a spot a few metres
    /// off to one side or the other at a fighting distance -- moving across the player's
    /// aim rather than along it, and keeping the range it fights best at.
    /// </summary>
    bool PickFiringSpot(Vector3 threat, float distance, out Vector3 spot)
    {
        switch (_tactic)
        {
            case Tactic.Assault:
            {
                // Straight in while the player is busy: half the gap, never into their arms.
                Vector3 toward = threat - transform.position;
                toward.y = 0f;
                float step = Mathf.Clamp(distance * 0.5f, 3f, Mathf.Max(3f, distance - closeQuarters - 1f));
                Vector3 goal = transform.position + toward.normalized * step + Vector3.Cross(Vector3.up, toward.normalized) * Random.Range(-2f, 2f);
                _hasCover = false;
                if (NavMesh.SamplePosition(goal, out var hit, 3f, NavMesh.AllAreas)) { spot = hit.position; return true; }
                break;
            }
            case Tactic.Flank:
            {
                // Round the player's side, keeping the range: across their aim, to their flank.
                Vector3 off = transform.position - threat;
                off.y = 0f;
                float r = Mathf.Clamp(off.magnitude, 8f, _ai.attackRange * 0.8f);
                Vector3 round = Quaternion.AngleAxis(_flankSide * Random.Range(35f, 55f), Vector3.up) * off.normalized * r;
                _hasCover = false;
                if (NavMesh.SamplePosition(threat + round, out var hit, 4f, NavMesh.AllAreas)) { spot = hit.position; return true; }
                break;
            }
            case Tactic.Retreat:
                if (!_retreated && FindCover(threat, 14f, _ai.attackRange * 0.95f, out spot)) { _hasCover = true; _retreated = true; return true; }
                goto case Tactic.Hold;
            case Tactic.Hold:
                if (FindCover(threat, 6f, _ai.attackRange * 0.95f, out spot) && (spot - transform.position).sqrMagnitude < 10f * 10f) { _hasCover = true; return true; }
                break;
        }

        float want = Mathf.Clamp(_ai.preferredRangedDistance > 0f ? _ai.preferredRangedDistance : 14f, 6f, _ai.attackRange * 0.9f);
        if (Random.value < 0.35f && FindCover(threat, 6f, _ai.attackRange * 0.95f, out spot)
            && (spot - transform.position).sqrMagnitude < 9f * 9f)
        {
            _hasCover = true;
            return true;
        }
        _hasCover = false;

        Vector3 from = (transform.position - threat);
        from.y = 0f;
        if (from.sqrMagnitude < 0.01f) from = -transform.forward;
        from.Normalize();

        // Across: a sidestep of four to nine metres, and in or out towards the range it wants.
        Vector3 side = Vector3.Cross(Vector3.up, from) * (Random.value < 0.5f ? -1f : 1f);
        // Only part of the way to the range it likes in one move: a dash, not a relocation.
        want = Mathf.Lerp(distance, want, 0.35f);
        for (int i = 0; i < 3; i++)
        {
            Vector3 goal = threat + from * want + side * Random.Range(3f, 6f);
            if (NavMesh.SamplePosition(goal, out var hit, 4f, NavMesh.AllAreas)
                && (hit.position - transform.position).sqrMagnitude >= 3f * 3f)
            {
                spot = hit.position;
                return true;
            }
            side = -side;
        }
        spot = transform.position;
        return false;
    }

    // ---- helpers ----------------------------------------------------------------

    /// <summary>The head turning about while it stands: a look one way, a pause, another way.</summary>
    void Scan(float seconds)
    {
        if (Time.time < _nextScan) return;
        _nextScan = Time.time + Random.Range(seconds, seconds * 2.5f);
        _scanAngle = Random.Range(-70f, 70f);
        Vector3 dir = Quaternion.AngleAxis(_scanAngle, Vector3.up) * transform.forward;
        LookPoint = transform.position + dir * 12f + Vector3.up * 1.5f;
    }

    static bool RandomPoint(Vector3 around, float radius, out Vector3 point)
    {
        for (int i = 0; i < 4; i++)
        {
            Vector2 r = Random.insideUnitCircle * radius;
            if (NavMesh.SamplePosition(around + new Vector3(r.x, 0f, r.y), out var hit, 3f, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }
        }
        point = around;
        return false;
    }

    /// <summary>
    /// A spot within reach that the player cannot see, at a sensible distance from them:
    /// a few random tries round itself, the nearest hidden one kept. Cheap on purpose.
    /// </summary>
    bool FindCover(Vector3 threat, float minRange, float maxRange, out Vector3 cover)
    {
        cover = transform.position;
        float best = float.MaxValue;
        bool found = false;
        Vector3 threatEye = threat + Vector3.up * 1.5f;

        for (int i = 0; i < 8; i++)
        {
            Vector2 r = Random.insideUnitCircle * 10f;
            Vector3 candidate = transform.position + new Vector3(r.x, 0f, r.y);
            if (!NavMesh.SamplePosition(candidate, out var hit, 2.5f, NavMesh.AllAreas)) continue;

            float range = Vector3.Distance(hit.position, threat);
            if (range < minRange || range > maxRange) continue;

            // A move worth making: a step to the side of where it stands is not cover-seeking.
            if ((hit.position - transform.position).sqrMagnitude < 3f * 3f) continue;

            bool hidden = Physics.Linecast(threatEye, hit.position + Vector3.up * 1.2f, _ai.sightBlockers, QueryTriggerInteraction.Ignore);
            if (!hidden) continue;

            float cost = (hit.position - transform.position).sqrMagnitude;
            if (cost >= best) continue;
            best = cost;
            cover = hit.position;
            found = true;
        }
        return found;
    }
}
