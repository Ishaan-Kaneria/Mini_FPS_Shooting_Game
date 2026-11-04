using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The people who live in the town's houses and keep its shops (Docs/DESERT_PEOPLE.md). The builder writes every
/// enterable house (its front door, its rooms' activity spots: the sofa, the table, the burners, the bed, the majlis),
/// every lit shop (its door, the counter, the shelves) as data here. In play, the houses and shops near the player are
/// lived in: residents are put down at home (never in view), go about the house from spot to spot, now and then walk to
/// a shop near by to buy something and come back; shopkeepers mind their counters. Far away they are taken off again.
/// When the shooting starts they take cover where they are and stay there (they are never removed while the player is
/// close), and go back to what they were doing once it has been quiet a while.
///
/// The street's walkers stay with <see cref="CivilianDirector"/>; these are their own, so neither budget eats the other.
/// A resident killed stays dead and the house has one fewer for the rest of the level.
/// </summary>
[DisallowMultipleComponent]
public class TownHouseholds : MonoBehaviour
{
    [System.Serializable]
    public struct House
    {
        public Vector3 door;          // outside the front door
        public Vector3 centre;
        public int firstSpot, spotCount;
        public int residents;         // how many live here
    }

    [System.Serializable]
    public struct Shop
    {
        public Vector3 entrance;      // in front of its door, outside
        public int firstSpot, spotCount;
    }

    [System.Serializable]
    public struct Spot
    {
        public Vector3 pos;           // where the body is while doing it (on the seat, on the bed, at the counter)
        public Vector3 approach;      // where the agent stops on the navmesh
        public float yaw;             // which way the body faces
        public LifeActivity kind;
    }

    [Header("Content (written by the builder)")]
    public GameObject[] prefabs;
    public House[] houses = new House[0];
    public Shop[] shops = new Shop[0];
    public Spot[] spots = new Spot[0];

    [Header("Budget")]
    [Tooltip("Metres from the player within which houses and shops are lived in (High; Medium 80 %, Low and phones 60 %).")]
    [Min(10f)] public float radius = 60f;
    [Tooltip("Most residents at once on High (Medium 60 %, Low and phones 25 %).")]
    [Min(0)] public int maxResidents = 40;
    [Tooltip("Most shopkeepers at once on High (Medium 60 %, Low and phones 25 %).")]
    [Min(0)] public int maxKeepers = 14;
    [Tooltip("Seconds between looks at who should be where.")]
    [Range(0.2f, 3f)] public float interval = 1f;

    readonly struct Live
    {
        public readonly CivilianLife life;
        public readonly int house, shop;
        public Live(CivilianLife l, int h, int s) { life = l; house = h; shop = s; }
    }

    List<Live> _live;
    List<Live> LiveList => _live ??= new List<Live>();
    bool[] _taken;
    int[] _lost, _spawned, _keeperOf;
    Transform _player;
    Camera _cam;
    float _next, _radius;
    int _maxResidents, _maxKeepers;

    void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null) _player = p.transform;
        float share = QualityTiers.Handheld ? 0.25f : QualityTiers.Current switch
        {
            GameSettings.Quality.High => 1f,
            GameSettings.Quality.Medium => 0.6f,
            _ => 0.25f,
        };
        _radius = radius * (QualityTiers.Handheld || QualityTiers.Current == GameSettings.Quality.Low ? 0.6f : QualityTiers.Current == GameSettings.Quality.Medium ? 0.8f : 1f);
        _maxResidents = Mathf.RoundToInt(maxResidents * share);
        _maxKeepers = Mathf.RoundToInt(maxKeepers * share);
        _taken = new bool[spots.Length];
        _lost = new int[houses.Length];
        _spawned = new int[houses.Length];
        _keeperOf = new int[shops.Length];
        for (int i = 0; i < _keeperOf.Length; i++) _keeperOf[i] = -1;
    }

    void Update()
    {
        if (_player == null || prefabs == null || prefabs.Length == 0 || Time.time < _next) return;
        _next = Time.time + interval;
        if (_cam == null) _cam = Camera.main;
        Vector3 me = _player.position;
        float r2 = _radius * _radius, gone2 = (_radius + 25f) * (_radius + 25f);

        // Who leaves: far, out of view, alive. The dead stay where they fell.
        int residents = 0, keepers = 0;
        for (int i = LiveList.Count - 1; i >= 0; i--)
        {
            var l = LiveList[i];
            if (l.life == null) { Forget(i); continue; }
            if (l.life.Dead)
            {
                if (l.house >= 0) _lost[l.house]++;
                l.life.ReleaseSpot();
                Forget(i);
                continue;
            }
            Vector3 home = l.house >= 0 ? houses[l.house].centre : shops[l.shop].entrance;
            if ((home - me).sqrMagnitude > gone2 && (l.life.transform.position - me).sqrMagnitude > gone2 && !InView(l.life.transform.position))
            {
                l.life.ReleaseSpot();
                Destroy(l.life.gameObject);
                Forget(i);
                continue;
            }
            if (l.house >= 0) residents++; else keepers++;
        }

        // Who arrives: the nearest houses first, at home, out of the player's sight.
        for (int h = 0; h < houses.Length && residents < _maxResidents; h++)
        {
            var house = houses[h];
            if ((house.centre - me).sqrMagnitude > r2) continue;
            int want = house.residents - _lost[h] - _spawned[h];
            for (int k = 0; k < want && residents < _maxResidents; k++)
            {
                int spot = FreeSpot(house.firstSpot, house.spotCount, null);
                if (spot < 0) break;
                if (!Spawn(spots[spot].approach, out var life)) break;
                LiveList.Add(new Live(life, h, -1));
                _spawned[h]++;
                residents++;
                life.BeginResident(this, h, spot);
            }
        }
        for (int s = 0; s < shops.Length && keepers < _maxKeepers; s++)
        {
            if (_keeperOf[s] >= 0 || (shops[s].entrance - me).sqrMagnitude > r2) continue;
            int spot = FreeSpot(shops[s].firstSpot, shops[s].spotCount, LifeActivity.Keeper);
            if (spot < 0) { _keeperOf[s] = -2; continue; }      // a shop with no counter: nobody minds it
            if (!Spawn(spots[spot].approach, out var life)) break;
            LiveList.Add(new Live(life, -1, s));
            _keeperOf[s] = LiveList.Count - 1;
            keepers++;
            life.BeginKeeper(this, s, spot);
        }
    }

    void Forget(int i)
    {
        var l = LiveList[i];
        if (l.house >= 0) _spawned[l.house] = Mathf.Max(0, _spawned[l.house] - 1);
        if (l.shop >= 0 && l.shop < _keeperOf.Length) _keeperOf[l.shop] = -1;
        LiveList.RemoveAt(i);
        // Keepers' indices into the list moved: rebuild them.
        for (int s = 0; s < _keeperOf.Length; s++) if (_keeperOf[s] >= 0) _keeperOf[s] = -1;
        for (int k = 0; k < LiveList.Count; k++) if (LiveList[k].shop >= 0) _keeperOf[LiveList[k].shop] = k;
    }

    bool Spawn(Vector3 at, out CivilianLife life)
    {
        life = null;
        if (InView(at) && (at - _player.position).sqrMagnitude < 30f * 30f) return false;   // never appear in front of him
        if (!NavMesh.SamplePosition(at, out var hit, 1.2f, NavMesh.AllAreas)) return false;
        var go = Instantiate(prefabs[Random.Range(0, prefabs.Length)], hit.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
        if (go.GetComponent<Civilian>() == null) { Destroy(go); return false; }
        life = go.GetComponent<CivilianLife>();
        if (life == null) life = go.AddComponent<CivilianLife>();
        return true;
    }

    bool InView(Vector3 p)
    {
        if (_cam == null) return false;
        var v = _cam.WorldToViewportPoint(p + Vector3.up);
        return v.z > 0f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
    }

    // ---- spots -------------------------------------------------------------------------------------------------------

    int FreeSpot(int first, int count, LifeActivity? kind)
    {
        int start = count > 0 ? Random.Range(0, count) : 0;
        for (int k = 0; k < count; k++)
        {
            int i = first + (start + k) % count;
            if (i < 0 || i >= spots.Length || _taken[i]) continue;
            if (kind.HasValue && spots[i].kind != kind.Value) continue;
            if (!kind.HasValue && spots[i].kind == LifeActivity.Keeper) continue;
            return i;
        }
        return -1;
    }

    /// <summary>Takes a spot of the house for something to do, weighted by how often each thing is done. -1: none free.</summary>
    public int ReserveHomeSpot(int house, int current)
    {
        if (house < 0 || house >= houses.Length) return -1;
        var h = houses[house];
        float total = 0f;
        for (int i = h.firstSpot; i < h.firstSpot + h.spotCount; i++)
            if (i != current && !_taken[i]) total += LifeActivities.HomeWeight(spots[i].kind);
        if (total <= 0f) return -1;
        float pick = Random.value * total;
        for (int i = h.firstSpot; i < h.firstSpot + h.spotCount; i++)
        {
            if (i == current || _taken[i]) continue;
            pick -= LifeActivities.HomeWeight(spots[i].kind);
            if (pick <= 0f) { _taken[i] = true; return i; }
        }
        return -1;
    }

    /// <summary>Takes a spot of a shop of the given kind (Browse, Pay). -1: none free.</summary>
    public int ReserveShopSpot(int shop, LifeActivity kind)
    {
        if (shop < 0 || shop >= shops.Length) return -1;
        int i = FreeSpot(shops[shop].firstSpot, shops[shop].spotCount, kind);
        if (i >= 0) _taken[i] = true;
        return i;
    }

    public bool Reserve(int spot)
    {
        if (spot < 0 || spot >= _taken.Length || _taken[spot]) return false;
        _taken[spot] = true;
        return true;
    }

    public void Release(int spot)
    {
        if (_taken != null && spot >= 0 && spot < _taken.Length) _taken[spot] = false;
    }

    /// <summary>A lit shop within reach of a house, for an errand; -1 if none.</summary>
    public int ShopNear(Vector3 from, float reach)
    {
        int best = -1, seen = 0;
        float r2 = reach * reach;
        for (int s = 0; s < shops.Length; s++)
        {
            if ((shops[s].entrance - from).sqrMagnitude > r2) continue;
            // Any of the near ones, not always the nearest: people have their own shops.
            if (Random.Range(0, ++seen) == 0) best = s;
        }
        return best;
    }
}
