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
    [Range(0.1f, 2f)] public float reactionSeconds = 0.8f;
    [Tooltip("Seconds it searches for a player it has lost before it settles down.")]
    [Range(5f, 90f)] public float searchSeconds = 28f;
    [Tooltip("Metres round its post it walks on a calm beat.")]
    [Range(0f, 40f)] public float patrolRadius = 14f;
    [Tooltip("Share of its health below which it falls back to cover.")]
    [Range(0f, 1f)] public float fallBackAt = 0.3f;

    [Header("Moving, as shares of its full speed")]
    [Range(0.1f, 1f)] public float walkPace = 0.42f;
    [Range(0.1f, 1f)] public float carefulPace = 0.5f;
    [Range(0.3f, 1.3f)] public float runPace = 1f;

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

    public Role Part { get; private set; }

    EnemyAI _ai;
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
        if (at > Mind)
        {
            if (at >= Awareness.Combat && Mind < Awareness.Combat) _reactAt = Time.time + ReactionTime;
            Mind = at;
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
        _hasGoal = false;
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
            if (_ai.Arrived)
            {
                _hasGoal = false;
                _holdUntil = Time.time + Random.Range(3f, 9f);
            }
            return;
        }

        _ai.Hold();
        if (Time.time < _holdUntil) { Doing = "standing watch"; return; }

        if (patrolRadius > 0f && Random.value < 0.6f && RandomPoint(_post, patrolRadius, out _goal)) _hasGoal = true;
        else _holdUntil = Time.time + Random.Range(2f, 6f);
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
        if (_ai.Arrived)
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

        if (!_hasGoal)
        {
            Vector3 centre = _lastKnown;
            if (_searchLeg == 0 || !RandomPoint(centre, 9f, out _goal)) _goal = centre;
            _searchLeg++;
            _hasGoal = true;
        }

        Doing = Mind == Awareness.Alert ? "moving on the player" : "searching";
        _ai.MoveTo(_goal, Mind == Awareness.Alert ? runPace * 0.85f : carefulPace);
        LookPoint = _goal + Vector3.up * 1.5f;
        Scan(1.5f);

        if (_ai.Arrived)
        {
            _hasGoal = false;
            _ai.Hold();
        }
    }

    /// <summary>The fight: shoot on a turn, otherwise cover, flank or push; fall back when hurt.</summary>
    void Fight()
    {
        var target = _ai.target;
        if (_sees) { _lastKnown = target.position; _lastKnownTime = Time.time; _lastSeen = Time.time; }
        LookPoint = _lastKnown + Vector3.up * 1.4f;

        float distance = Vector3.Distance(transform.position, target.position);

        // Hands only: in it goes, as fast as its legs allow.
        if (!_ai.ranged)
        {
            Doing = "rushing in";
            _ai.ReportState(EnemyAI.State.Chase);
            if (distance <= _ai.attackRange && _ai.TryAttack()) return;
            _ai.MoveTo(target.position, runPace * 1.1f);
            return;
        }

        // On the floor: it stays and shoots from where it lies.
        if (_ai.Crawling)
        {
            Doing = "fighting from the ground";
            _ai.Hold();
            if (_sees && Time.time >= _reactAt) _ai.TryAttack();
            return;
        }

        // Badly hurt: back to cover once, and fight from there.
        float health = _health != null ? _health.Normalized : 1f;
        if (!_fellBack && health <= fallBackAt)
        {
            _fellBack = true;
            if (FindCover(target.position, 10f, 30f, out _cover)) { _hasCover = true; _hasGoal = false; }
        }

        bool turn = _sees && Time.time >= _reactAt && EnemySquad.RequestTurn(this, Aggression);

        // Somebody in its face: no time for turns, it fights back.
        if (_sees && distance <= _ai.meleeRange + 0.5f && Time.time >= _reactAt) turn = true;

        if (turn)
        {
            Doing = "firing";
            _ai.ReportState(EnemyAI.State.Attack);
            _ai.Hold();
            _ai.FaceTowards(target.position, 10f);
            _ai.TryAttack();
            return;
        }

        _ai.ReportState(EnemyAI.State.Chase);

        // Can see but must wait: stay in cover, or get into some.
        if (_hasCover && !_ai.Arrived)
        {
            Doing = "moving to cover";
            _ai.MoveTo(_cover, runPace);
            return;
        }

        if (_sees)
        {
            if (Time.time >= _nextCoverPick && !_hasCover)
            {
                _nextCoverPick = Time.time + 2.5f;
                if (FindCover(target.position, 6f, _ai.attackRange, out _cover)) { _hasCover = true; return; }
            }

            Doing = _hasCover ? "in cover" : "holding";
            _ai.Hold();
            _ai.FaceTowards(target.position, 6f);
            return;
        }

        // Cannot see the player: the role decides.
        _hasCover = false;
        switch (Part)
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
                // A rifleman edges up to where it last had a line, and waits for it.
                Doing = "edging up";
                _ai.MoveTo(Vector3.Lerp(transform.position, _lastKnown, 0.5f), carefulPace);
                break;
        }
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
