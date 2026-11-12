using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The desert town's traffic: routes through the street network (written by FPSKitTownLife: lanes on the right,
/// turns through junctions, the roundabout taken anticlockwise, the highway off both edges of the map) and a budget
/// of cars driving them near the player, scaled by quality tier. A car is put down on a route out of sight, drives it
/// to its end and is put down somewhere else.
/// </summary>
public class TrafficDirector : MonoBehaviour
{
    [System.Serializable]
    public class Route
    {
        public Vector3[] points;
        public float[] speeds;
        /// <summary>Per stop line: the point index it is at, its rule (1 stop sign, 2 signal, 3 give way), signal junction, approach axis.</summary>
        public int[] stopIndex, stopRule, stopJunction;
        public byte[] stopAlongX;
    }

    [Header("Traffic")]
    [Tooltip("Cars driving at once on the High tier. Medium takes 60%, Low and phones 35%.")]
    [Min(0)] public int maxCars = 15;
    [Tooltip("Cars are put down between these distances from the player, out of view.")]
    public Vector2 spawnRange = new Vector2(70f, 300f);
    [Tooltip("A car this far from the player and out of view is put down elsewhere.")]
    public float recycleDistance = 360f;
    [Tooltip("What a car brakes for: other cars, people, the player.")]
    public LayerMask obstacleMask;

    [Header("Content (written by the builder)")]
    public Route[] routes;
    public GameObject[] carPrefabs;
    [Tooltip("The town's car colours (FPSKitCars): each car takes one as it appears.")]
    public Material[] paints;
    [Tooltip("The people who drive them (VehicleDriver): the civilian prefabs.")]
    public GameObject[] driverPrefabs;
    public TownSignals[] signals;

    readonly List<TrafficCar> _cars = new List<TrafficCar>();
    /// <summary>Cars off the road, ready to go out again, by prefab: a car with its driver was made in play every time one was needed
    /// (a person's whole body and animator instantiated mid-game), which is a hitch each time (2026-11-09).</summary>
    readonly Dictionary<int, Stack<TrafficCar>> _pool = new Dictionary<int, Stack<TrafficCar>>();
    Transform _player;
    Camera _cam;
    int _budget;
    float _next;

    void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null) _player = p.transform;
        float share = QualityTiers.Handheld ? 0.35f : QualityTiers.Current switch
        {
            GameSettings.Quality.High => 1f,
            GameSettings.Quality.Medium => 0.6f,
            _ => 0.35f,
        };
        _budget = Mathf.RoundToInt(maxCars * share);
        for (int i = 0; i < _budget; i++) TrySpawn(true);
        // Where there is traffic there are police to call.
        if (GetComponent<PoliceDirector>() == null) gameObject.AddComponent<PoliceDirector>();
        StartCoroutine(Prewarm(Mathf.Max(2, _budget / 2)));
    }

    /// <summary>A few spare cars made while the level starts, driven by nobody, put straight in the pool once their drivers are seated.</summary>
    System.Collections.IEnumerator Prewarm(int count)
    {
        if (carPrefabs == null || carPrefabs.Length == 0) yield break;
        var made = new List<TrafficCar>();
        for (int i = 0; i < count; i++)
        {
            int k = Random.Range(0, carPrefabs.Length);
            var go = Instantiate(carPrefabs[k], new Vector3(0f, -500f, 0f), Quaternion.identity, transform);
            var car = go.GetComponent<TrafficCar>();
            if (car == null) { Destroy(go); continue; }
            // A colour, as a car made on the road gets: the spares all came out in the prefab's own.
            if (paints != null && paints.Length > 0 && go.TryGetComponent<CarLook>(out var look)) look.SetPaint(paints[Random.Range(0, paints.Length)]);
            car.director = this;
            car.route = -1;               // no route: it waits (Update does nothing without one), its driver seated by Start
            car.prefabIndex = k;
            made.Add(car);
        }
        yield return null;                // Start has run: the drivers are seated
        yield return null;
        foreach (var car in made) if (car != null) Release(car, true);
    }

    /// <summary>Off the road: back in its pool, or destroyed if it cannot go out again as it is.</summary>
    void Release(TrafficCar car, bool spare = false)
    {
        if (car == null) return;
        if (!spare && !car.Reusable) { Destroy(car.gameObject); return; }
        car.gameObject.SetActive(false);
        if (!_pool.TryGetValue(car.prefabIndex, out var stack)) _pool[car.prefabIndex] = stack = new Stack<TrafficCar>();
        stack.Push(car);
    }

    void Update()
    {
        if (_player == null || Time.time < _next) return;
        _next = Time.time + 0.5f;
        if (_cam == null) _cam = Camera.main;
        for (int i = _cars.Count - 1; i >= 0; i--)
        {
            var c = _cars[i];
            if (c == null) { _cars.RemoveAt(i); continue; }
            float far = c.Abandoned ? recycleDistance * 0.5f : recycleDistance;   // a car whose driver was shot stays where it stopped while near
            if (Vector3.Distance(c.transform.position, _player.position) > far && !InView(c.transform.position, 600f))
            {
                _cars.RemoveAt(i);
                Release(c);
            }
        }
        if (_cars.Count < _budget) TrySpawn(false);
    }

    bool InView(Vector3 p, float range)
    {
        if (_cam == null) return false;
        var v = _cam.WorldToViewportPoint(p + Vector3.up);
        return v.z > 0f && v.z < range && v.x > -0.15f && v.x < 1.15f && v.y > -0.15f && v.y < 1.15f;
    }

    void TrySpawn(bool anywhere)
    {
        if (routes == null || routes.Length == 0 || carPrefabs == null || carPrefabs.Length == 0 || _player == null) return;
        for (int tries = 0; tries < 16; tries++)
        {
            int ri = Random.Range(0, routes.Length);
            var r = routes[ri];
            if (r.points == null || r.points.Length < 3) continue;
            int at = Random.Range(0, r.points.Length - 2);
            var p = r.points[at];
            float d = Vector3.Distance(p, _player.position);
            if (d < spawnRange.x || d > spawnRange.y) continue;
            if (!anywhere && InView(p, 400f)) continue;
            if (Physics.CheckSphere(p + Vector3.up, 4f, obstacleMask, QueryTriggerInteraction.Ignore)) continue;
            var dir = (r.points[at + 1] - p); dir.y = 0f;
            var rot = Quaternion.LookRotation(dir.normalized);
            int k = Random.Range(0, carPrefabs.Length);
            // From the pool when one of that kind is waiting (any kind, if not): made new only when the pool is empty.
            TrafficCar car = null;
            if (_pool.TryGetValue(k, out var stack) && stack.Count > 0) car = stack.Pop();
            else foreach (var kv in _pool) if (kv.Value.Count > 0) { car = kv.Value.Pop(); break; }
            while (car == null && stack != null && stack.Count > 0) car = stack.Pop();
            if (car != null)
            {
                car.transform.SetPositionAndRotation(p, rot);
                car.gameObject.SetActive(true);
                car.director = this;
                car.Reuse(ri, at);
                _cars.Add(car);
                return;
            }
            var go = Instantiate(carPrefabs[k], p, rot, transform);
            if (paints != null && paints.Length > 0 && go.TryGetComponent<CarLook>(out var look)) look.SetPaint(paints[Random.Range(0, paints.Length)]);
            car = go.GetComponent<TrafficCar>();
            if (car == null) { Destroy(go); return; }
            car.director = this;
            car.prefabIndex = k;
            car.route = ri;
            car.index = at;
            _cars.Add(car);
            return;
        }
    }

    /// <summary>A car no longer the traffic's (the player has taken it): not driven, recycled or counted from here on.</summary>
    internal void Forget(TrafficCar car) => _cars.Remove(car);

    /// <summary>
    /// A car of the town's put on the road for someone else to run (PoliceDirector): on a route that passes within
    /// <paramref name="within"/> metres of <paramref name="near"/>, far enough up it to arrive from out of sight, with the point on the
    /// route nearest <paramref name="near"/> as its stop. Null when no route passes that way or the place is taken.
    /// </summary>
    internal TrafficCar SpawnToward(Vector3 near, float within, float fromFar, out int stopIndex, string prefer = null)
    {
        stopIndex = -1;
        if (routes == null || carPrefabs == null || carPrefabs.Length == 0) return null;
        for (int tries = 0; tries < 12; tries++)
        {
            int ri = Random.Range(0, routes.Length);
            var r = routes[ri];
            if (r.points == null || r.points.Length < 4) continue;
            int stop = -1; float best = within * within;
            for (int i = 1; i < r.points.Length - 1; i++)
            {
                float sq = (r.points[i] - near).sqrMagnitude;
                if (sq < best) { best = sq; stop = i; }
            }
            if (stop < 0) continue;
            // Back up the route until it is far enough along the road.
            int at = stop; float back = 0f;
            while (at > 0 && back < fromFar) { back += Vector3.Distance(r.points[at], r.points[at - 1]); at--; }
            if (back < fromFar * 0.5f) continue;
            var p = r.points[at];
            if (InView(p, 400f) && Vector3.Distance(p, near) < 60f) continue;
            if (Physics.CheckSphere(p + Vector3.up, 4f, obstacleMask, QueryTriggerInteraction.Ignore)) continue;
            var dir = r.points[at + 1] - p; dir.y = 0f;
            // The make asked for if the town has it (a police car is a saloon, not a taxi or a minibus), else any.
            GameObject prefab = null;
            if (prefer != null) foreach (var c in carPrefabs) if (c != null && c.name == prefer) { prefab = c; break; }
            if (prefab == null) prefab = carPrefabs[Random.Range(0, carPrefabs.Length)];
            var go = Instantiate(prefab, p, Quaternion.LookRotation(dir.normalized), transform);
            var car = go.GetComponent<TrafficCar>();
            if (car == null) { Destroy(go); return null; }
            car.director = this;
            car.prefabIndex = -1;
            car.route = ri;
            car.index = at;
            stopIndex = stop;
            return car;
        }
        return null;
    }

    CivilianDirector _civilians;
    /// <summary>The town's people, whom a driver joins when he gets out and runs.</summary>
    internal CivilianDirector Civilians => _civilians != null ? _civilians : _civilians = FindAnyObjectByType<CivilianDirector>();

    internal void Finished(TrafficCar car)
    {
        // Driven off the end of its route (often off the map on the highway): gone, and another arrives.
        _cars.Remove(car);
        Release(car);
    }

    public TownSignals Signal(int i) => signals != null && i >= 0 && i < signals.Length ? signals[i] : null;

    /// <summary>Whether no other car is in the junction ahead of <paramref name="asking"/> (a stop sign's wait).</summary>
    public bool JunctionClear(Vector3 at, TrafficCar asking)
    {
        foreach (var c in _cars)
        {
            if (c == null || c == asking || c.Speed < 0.5f) continue;
            if ((c.transform.position - at).sqrMagnitude < 14f * 14f) return false;
        }
        return true;
    }
}
