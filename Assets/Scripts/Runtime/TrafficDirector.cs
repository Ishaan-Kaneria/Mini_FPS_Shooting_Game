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
    public TownSignals[] signals;

    readonly List<TrafficCar> _cars = new List<TrafficCar>();
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
            if (Vector3.Distance(c.transform.position, _player.position) > recycleDistance && !InView(c.transform.position, 600f))
            {
                Destroy(c.gameObject);
                _cars.RemoveAt(i);
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
            var go = Instantiate(carPrefabs[Random.Range(0, carPrefabs.Length)], p, Quaternion.LookRotation(dir.normalized), transform);
            var car = go.GetComponent<TrafficCar>();
            if (car == null) { Destroy(go); return; }
            car.director = this;
            car.route = ri;
            car.index = at;
            _cars.Add(car);
            return;
        }
    }

    internal void Finished(TrafficCar car)
    {
        // Driven off the end of its route (often off the map on the highway): gone, and another arrives.
        _cars.Remove(car);
        Destroy(car.gameObject);
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
