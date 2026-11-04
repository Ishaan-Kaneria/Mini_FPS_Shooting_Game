using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One townsperson's day (TownHouseholds; Docs/DESERT_PEOPLE.md). A resident goes from one thing to the next about the
/// house (cooking, eating, the television, the majlis, a nap, prayer, the phone, sweeping), and now and then walks to a
/// shop near by, looks along the shelves, pays at the counter and walks home. A shopkeeper stays at the counter. At the
/// sound of shooting they drop what they are doing and cower where they are (outdoors: run for their own door first),
/// and carry on once it has been quiet for a while.
///
/// Rides on the <see cref="Civilian"/> beside it, which keeps what it already does (death, the casualty count, the
/// walk/run blend from the agent's speed). That Civilian has no director, so it never wanders or flees on its own.
/// The pose at a spot is the animator's "Activity" state (LifeActivity); a seat or a bed takes the body off the navmesh
/// for the length of it.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Civilian))]
public class CivilianLife : MonoBehaviour
{
    enum Phase { Idle, Going, Settling, Doing, Rising, Cowering }
    enum Errand { None, ToShop, Browsing, Paying, Home }

    static readonly int ActivityId = Animator.StringToHash("Activity");
    static readonly int SpeedId = Animator.StringToHash("Speed");

    [Tooltip("Chance, each time a resident chooses what to do next, that they go out to a shop instead.")]
    [Range(0f, 1f)] public float errandChance = 0.18f;
    [Tooltip("Metres a resident will walk to a shop.")]
    [Min(10f)] public float shopReach = 140f;
    [Tooltip("Seconds of quiet after the last shot before they go back to what they were doing.")]
    [Min(1f)] public float calmAfter = 25f;
    [Tooltip("How far a shot is heard from, as a share of its own reach.")]
    [Range(0f, 2f)] public float hearing = 1f;

    public bool Dead => _civ == null || _civ.Dead;

    TownHouseholds _town;
    Civilian _civ;
    NavMeshAgent _agent;
    Animator _animator;
    int _house = -1, _shop = -1, _spot = -1, _heard, _browsesLeft;
    bool _keeper;
    Phase _phase;
    Errand _errand;
    float _until, _quietUntil, _poseT, _goingSince;
    Vector3 _poseFrom, _poseTo;
    Quaternion _rotFrom, _rotTo;
    System.Action<NoiseSystem.Noise, float> _onNoise;

    void Awake()
    {
        _civ = GetComponent<Civilian>();
        _agent = GetComponent<NavMeshAgent>();
        _animator = _civ.animator != null ? _civ.animator : GetComponentInChildren<Animator>();
        _heard = NoiseSystem.Latest;
    }

    void OnEnable() { if (_civ != null && _civ.health != null) _civ.health.Died += OnDied; }
    void OnDisable() { if (_civ != null && _civ.health != null) _civ.health.Died -= OnDied; }

    // At once, before the animator next updates: an Activity left set would take the body back out of its death (the
    // everyday states are entered from any state).
    void OnDied(Health h) { SetActivity(LifeActivity.None); ReleaseSpot(); }

    public void BeginResident(TownHouseholds town, int house, int spot)
    {
        _town = town; _house = house; _keeper = false;
        if (town.Reserve(spot)) GoTo(spot); else _phase = Phase.Idle;
    }

    public void BeginKeeper(TownHouseholds town, int shop, int spot)
    {
        _town = town; _shop = shop; _keeper = true;
        if (town.Reserve(spot)) GoTo(spot); else _phase = Phase.Idle;
    }

    public void ReleaseSpot()
    {
        if (_town != null && _spot >= 0) _town.Release(_spot);
        _spot = -1;
    }

    void Update()
    {
        if (_town == null) return;
        if (Dead) { ReleaseSpot(); enabled = false; return; }

        _onNoise ??= OnNoise;
        _heard = NoiseSystem.Listen(transform.position + Vector3.up * 1.6f, hearing, _heard, _onNoise);

        switch (_phase)
        {
            case Phase.Idle: Choose(); break;
            case Phase.Going:
                if (Arrived()) Arrive();
                else if (Time.time - _goingSince > 45f) { ReleaseSpot(); _errand = _errand == Errand.None ? Errand.None : Errand.Home; _phase = Phase.Idle; }   // no way there: give up
                break;
            case Phase.Settling: Pose(false); break;
            case Phase.Doing: if (Time.time >= _until) Finish(); break;
            case Phase.Rising: Pose(true); break;
            case Phase.Cowering: if (Time.time >= _quietUntil) Recover(); break;
        }
    }

    // ---- choosing ----------------------------------------------------------------------------------------------------

    void Choose()
    {
        if (_keeper) { int k = _town.ReserveShopSpot(_shop, LifeActivity.Keeper); if (k >= 0) GoTo(k); return; }

        switch (_errand)
        {
            case Errand.ToShop:
            case Errand.Browsing:
                if (_browsesLeft-- > 0) { int b = _town.ReserveShopSpot(_shop, LifeActivity.Browse); if (b >= 0) { _errand = Errand.Browsing; GoTo(b); return; } }
                int pay = _town.ReserveShopSpot(_shop, LifeActivity.Pay);
                if (pay >= 0) { _errand = Errand.Paying; GoTo(pay); return; }
                GoHome();
                return;
            case Errand.Paying:
                GoHome();
                return;
            case Errand.Home:
                _errand = Errand.None;
                break;
        }

        if (Random.value < errandChance && _house >= 0)
        {
            int shop = _town.ShopNear(_town.houses[_house].door, shopReach);
            if (shop >= 0 && Reachable(_town.shops[shop].entrance))
            {
                _shop = shop;
                _errand = Errand.ToShop;
                _browsesLeft = Random.Range(1, 4);
                Walk(_town.shops[shop].entrance);
                return;
            }
        }
        int spot = _town.ReserveHomeSpot(_house, _spot);
        if (spot >= 0) GoTo(spot);
        else { _until = Time.time + Random.Range(4f, 10f); _phase = Phase.Doing; }   // nothing free: stand a while
    }

    void GoHome()
    {
        _errand = Errand.Home;
        _shop = -1;
        if (_house >= 0) Walk(_town.houses[_house].door); else _phase = Phase.Idle;
    }

    bool Reachable(Vector3 to)
    {
        if (!_agent.isOnNavMesh) return false;
        var path = new NavMeshPath();
        return NavMesh.CalculatePath(transform.position, to, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
    }

    // ---- moving -------------------------------------------------------------------------------------------------------

    void GoTo(int spot)
    {
        ReleaseSpot();
        _spot = spot;
        Walk(_town.spots[spot].approach);
    }

    void Walk(Vector3 to)
    {
        SetActivity(LifeActivity.None);
        if (!_agent.enabled || !_agent.isOnNavMesh) { _phase = Phase.Idle; return; }
        _agent.updatePosition = true;
        _agent.updateRotation = true;
        _agent.isStopped = false;
        _agent.speed = _civ.walkSpeed;
        _agent.stoppingDistance = 0.15f;
        _agent.SetDestination(to);
        _goingSince = Time.time;
        _phase = Phase.Going;
    }

    bool Arrived() => _agent.isOnNavMesh && !_agent.pathPending && _agent.remainingDistance <= 0.3f;

    void Arrive()
    {
        if (_spot < 0)
        {
            // Walked to a shop's door or back to their own: on to the next thing.
            _phase = Phase.Idle;
            return;
        }
        var s = _town.spots[_spot];
        _agent.isStopped = true;
        _agent.ResetPath();
        _agent.updateRotation = false;
        _rotFrom = transform.rotation;
        _rotTo = Quaternion.Euler(0f, s.yaw, 0f);
        _poseFrom = transform.position;
        _poseTo = LifeActivities.Seated(s.kind) ? s.pos : transform.position;
        if (LifeActivities.Seated(s.kind)) _agent.updatePosition = false;
        _poseT = 0f;
        _phase = Phase.Settling;
        SetActivity(s.kind);
    }

    /// <summary>Onto the pose (or off it), over half a second.</summary>
    void Pose(bool rising)
    {
        _poseT = Mathf.Min(1f, _poseT + Time.deltaTime / 0.5f);
        float t = Mathf.SmoothStep(0f, 1f, _poseT);
        transform.position = Vector3.Lerp(_poseFrom, _poseTo, t);
        transform.rotation = Quaternion.Slerp(_rotFrom, _rotTo, t);
        if (_poseT < 1f) return;
        if (!rising)
        {
            var d = LifeActivities.Duration(_town.spots[_spot].kind);
            _until = Time.time + Random.Range(d.x, d.y);
            _phase = Phase.Doing;
            return;
        }
        // Back on the navmesh where the walk left off.
        if (_agent.enabled) { _agent.Warp(_poseTo); _agent.updatePosition = true; _agent.updateRotation = true; }
        _phase = Phase.Idle;
    }

    void Finish()
    {
        SetActivity(LifeActivity.None);
        bool seated = _spot >= 0 && LifeActivities.Seated(_town.spots[_spot].kind);
        var back = _spot >= 0 ? _town.spots[_spot].approach : transform.position;
        if (_errand == Errand.Paying || _errand == Errand.Browsing) ReleaseSpot();
        if (!seated) { _phase = Phase.Idle; return; }
        _poseFrom = transform.position;
        _poseTo = back;
        _rotFrom = _rotTo = transform.rotation;
        _poseT = 0f;
        _phase = Phase.Rising;
    }

    // ---- the shooting ---------------------------------------------------------------------------------------------------

    void OnNoise(NoiseSystem.Noise n, float distance)
    {
        if (n.kind != NoiseKind.Gunfire && n.kind != NoiseKind.Blast && n.kind != NoiseKind.Violence) return;
        _quietUntil = Time.time + calmAfter * Random.Range(0.8f, 1.4f);
        if (_phase == Phase.Cowering) return;
        // Outdoors on an errand: home first, then down. Indoors (or behind the counter): down where they are.
        if (_errand == Errand.ToShop || _errand == Errand.Home)
        {
            if (_house >= 0 && _agent.enabled && _agent.isOnNavMesh)
            {
                ReleaseSpot();
                _errand = Errand.Home;
                _agent.updatePosition = true;
                _agent.isStopped = false;
                _agent.speed = _civ.runSpeed;
                _agent.SetDestination(_town.houses[_house].door);
                _goingSince = Time.time;
                _phase = Phase.Going;
                return;
            }
        }
        if (_agent.enabled && _agent.isOnNavMesh && _agent.updatePosition) { _agent.isStopped = true; _agent.ResetPath(); }
        SetActivity(LifeActivity.Cower);
        _phase = Phase.Cowering;
    }

    void Recover()
    {
        SetActivity(LifeActivity.None);
        if (_agent.enabled && !_agent.updatePosition)
        {
            // Cowered from a seat: up off it first.
            Finish();
            if (_phase == Phase.Rising) return;
        }
        if (_agent.enabled && _agent.isOnNavMesh) _agent.isStopped = false;
        if (_errand == Errand.Paying || _errand == Errand.Browsing) { ReleaseSpot(); GoHome(); return; }
        ReleaseSpot();
        _phase = Phase.Idle;
    }

    void SetActivity(LifeActivity a)
    {
        if (_animator == null) return;
        _animator.SetInteger(ActivityId, (int)a);
        if (a != LifeActivity.None) _animator.SetFloat(SpeedId, 0f);
    }
}
