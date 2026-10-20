using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Keeps the desert town populated round the player: a budget of civilians (TownCivilians on the theme, scaled by
/// quality tier) alive at once within a ring round the player, topped up out of sight and recycled when far away or
/// once they have run indoors. A kilometre of town cannot afford a person on every street, and does not need one:
/// the streets near you are busy, which is all anyone sees.
///
/// Spots, doors and prefabs are written by the town builder (FPSKitTownLife). Dead civilians stay where they fell
/// for the rest of the level, as the enemies do; they do not count against the budget after a while.
/// </summary>
public class CivilianDirector : MonoBehaviour
{
    [Header("Population")]
    [Tooltip("Civilians alive at once on the High tier. Medium takes 60%, Low and phones 35%.")]
    [Min(0)] public int maxCivilians = 60;

    [Tooltip("Nearest and farthest a civilian is put down from the player, in metres.")]
    public Vector2 spawnRange = new Vector2(30f, 150f);

    [Tooltip("Civilians farther than this from the player, and out of view, are taken away to be put down nearer.")]
    public float recycleDistance = 190f;

    [Tooltip("A civilian never arrives closer than this to an enemy (they keep away from where the fighting is).")]
    public float keepFromEnemies = 25f;

    [Header("Content (written by the builder)")]
    public GameObject[] prefabs;
    [Tooltip("Places people go: shop fronts, the souk, squares, benches, the mosque plaza.")]
    public Vector3[] spots;
    [Tooltip("Doorways of houses that can be entered, and yard gates: where people run when shooting starts.")]
    public Vector3[] doors;

    readonly List<Civilian> _live = new List<Civilian>();
    readonly List<GameObject> _dead = new List<GameObject>();
    Transform _player;
    Camera _cam;
    float _next;
    int _budget;

    public int Alive => _live.Count;

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
        _budget = Mathf.RoundToInt(maxCivilians * share);
        // The first fill can be anywhere in range; later ones arrive out of view.
        for (int i = 0; i < _budget; i++) TrySpawn(true);
    }

    void Update()
    {
        if (_player == null || Time.time < _next) return;
        _next = Time.time + 0.5f;
        if (_cam == null) _cam = Camera.main;

        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var c = _live[i];
            if (c == null) { _live.RemoveAt(i); continue; }
            if (c.Dead) { _live.RemoveAt(i); continue; }
            float d = Vector3.Distance(c.transform.position, _player.position);
            bool gone = c.Hidden && !InView(c.transform.position + Vector3.up, 120f);
            bool far = d > recycleDistance && !InView(c.transform.position + Vector3.up, 400f);
            if (gone || far) { Destroy(c.gameObject); _live.RemoveAt(i); }
        }
        if (_live.Count < _budget) TrySpawn(false);

        // Bodies are kept, but only so many: the oldest go once there are too many.
        while (_dead.Count > 20) { if (_dead[0] != null) Destroy(_dead[0]); _dead.RemoveAt(0); }
    }

    bool InView(Vector3 p, float range)
    {
        if (_cam == null) return false;
        var v = _cam.WorldToViewportPoint(p);
        return v.z > 0f && v.z < range && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
    }

    void TrySpawn(bool anywhere)
    {
        if (prefabs == null || prefabs.Length == 0 || spots == null || spots.Length == 0 || _player == null) return;
        for (int tries = 0; tries < 12; tries++)
        {
            var s = spots[Random.Range(0, spots.Length)];
            float d = Vector3.Distance(s, _player.position);
            if (d < spawnRange.x || d > spawnRange.y) continue;
            var p = s + new Vector3(Random.Range(-2.5f, 2.5f), 0f, Random.Range(-2.5f, 2.5f));
            if (!anywhere && InView(p + Vector3.up, 160f)) continue;
            if (NearEnemy(p)) continue;
            if (!NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas)) continue;
            var go = Instantiate(prefabs[Random.Range(0, prefabs.Length)], hit.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
            var c = go.GetComponent<Civilian>();
            if (c == null) { Destroy(go); return; }
            c.director = this;
            _live.Add(c);
            return;
        }
    }

    bool NearEnemy(Vector3 p)
    {
        if (_enemyMask == 0) _enemyMask = LayerMask.GetMask("Enemy");
        return _enemyMask != 0 && Physics.CheckSphere(p, keepFromEnemies, _enemyMask, QueryTriggerInteraction.Ignore);
    }

    int _enemyMask;

    /// <summary>Another place to go, within walking distance of <paramref name="from"/>.</summary>
    public Vector3 NextSpot(Vector3 from)
    {
        if (spots == null || spots.Length == 0) return from;
        for (int tries = 0; tries < 10; tries++)
        {
            var s = spots[Random.Range(0, spots.Length)];
            float d = Vector3.Distance(s, from);
            if (d > 6f && d < 90f) return s + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f));
        }
        return from + new Vector3(Random.Range(-15f, 15f), 0f, Random.Range(-15f, 15f));
    }

    /// <summary>
    /// Where to run from danger: the nearest doorway within 45 m that does not lie towards it (true: going indoors),
    /// else a point about 70 m directly away (false).
    /// </summary>
    public (Vector3 target, bool door) HideFrom(Vector3 danger, Vector3 from)
    {
        Vector3 away = from - danger; away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere; away.y = 0f;
        away.Normalize();
        float best = 45f * 45f; Vector3? pick = null;
        if (doors != null)
            foreach (var d in doors)
            {
                var to = d - from; to.y = 0f;
                float sq = to.sqrMagnitude;
                if (sq >= best) continue;
                if (sq > 9f && Vector3.Dot(to.normalized, away) < -0.2f) continue;      // not past the shooter
                best = sq; pick = d;
            }
        if (pick.HasValue && NavMesh.SamplePosition(pick.Value, out var dh, 3f, NavMesh.AllAreas)) return (dh.position, true);
        var far = from + away * 70f;
        if (NavMesh.SamplePosition(far, out var hit, 20f, NavMesh.AllAreas)) return (hit.position, false);
        return (from + away * 20f, false);
    }

    internal void Died(Civilian c)
    {
        _live.Remove(c);
        _dead.Add(c.gameObject);
    }
}
