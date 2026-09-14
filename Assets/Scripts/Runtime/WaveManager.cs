using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Endless wave spawner. Each wave is larger and slightly tougher than the last,
/// with an intermission between them. Spawn points too close to the player are
/// skipped so nothing materialises in your face.
/// </summary>
public class WaveManager : MonoBehaviour
{
    [Serializable]
    public class EnemyType
    {
        public GameObject prefab;
        [Min(0f)] public float weight = 1f;
        [Min(1)] public int unlockWave = 1;
    }

    [Header("Content")]
    public EnemyType[] enemyTypes;
    public Transform[] spawnPoints;
    public Transform player;

    [Header("Wave Size")]
    public int baseEnemiesPerWave = 4;
    public float enemiesPerWaveGrowth = 1.35f;
    public int maxAliveAtOnce = 16;
    public float spawnInterval = 0.35f;

    [Header("Timing")]
    public float timeBeforeFirstWave = 4f;
    public float intermissionDuration = 12f;

    [Header("Placement")]
    public float minSpawnDistanceFromPlayer = 14f;
    public float maxSpawnDistanceFromPlayer = 34f;
    public float navMeshSampleRadius = 5f;

    [Tooltip("Sample fresh NavMesh positions in a ring around the player instead of " +
             "reusing eight fixed points. Enemies then come from everywhere, and the " +
             "ring follows you as you move.")]
    public bool useDynamicSpawnPoints = true;

    [Tooltip("Prefer positions the player cannot currently see, so nothing pops into " +
             "existence in front of you.")]
    public bool avoidPlayerView = true;

    [Tooltip("Geometry that counts as blocking the player's view. Set this to Environment.")]
    public LayerMask spawnSightBlockers;

    [Range(10f, 180f)] public float playerViewAngle = 70f;

    [Tooltip("Optional puff of smoke, portal, teleport flash -- spawned at each arrival.")]
    public GameObject spawnEffectPrefab;
    public float spawnEffectLifetime = 3f;

    [Header("Per-Wave Scaling")]
    public float healthGrowthPerWave = 0.12f;
    public float damageGrowthPerWave = 0.06f;
    public float speedGrowthPerWave = 0.025f;
    public float maxSpeedMultiplier = 1.6f;

    // ---- state read by the HUD -------------------------------------------
    public int CurrentWave { get; private set; }
    public bool IsIntermission { get; private set; }
    public float IntermissionRemaining { get; private set; }
    public bool GameIsOver { get; private set; }
    public int EnemiesRemaining => _aliveCount + _pendingSpawns;

    public event Action<int> WaveStarted;
    public event Action<int> WaveCleared;
    public event Action<int> GameOver;

    readonly List<GameObject> _alive = new List<GameObject>();
    int _aliveCount;
    int _pendingSpawns;
    Health _playerHealth;

    // ======================================================================
    void Start()
    {
        if (player == null)
        {
            var found = GameObject.FindGameObjectWithTag("Player");
            if (found != null) player = found.transform;
        }

        if (player != null)
        {
            _playerHealth = player.GetComponentInParent<Health>();
            if (_playerHealth != null) _playerHealth.Died += OnPlayerDied;
        }

        if (enemyTypes == null || enemyTypes.Length == 0)
        {
            Debug.LogError("[WaveManager] No enemy types assigned.", this);
            return;
        }

        StartCoroutine(RunWaves());
    }

    void OnDestroy()
    {
        if (_playerHealth != null) _playerHealth.Died -= OnPlayerDied;
    }

    // ======================================================================
    IEnumerator RunWaves()
    {
        IsIntermission = true;
        yield return Countdown(timeBeforeFirstWave);
        IsIntermission = false;

        while (!GameIsOver)
        {
            CurrentWave++;
            WaveStarted?.Invoke(CurrentWave);

            yield return SpawnWave(CurrentWave);

            // Wait until the arena is clear.
            while (!GameIsOver && EnemiesRemaining > 0)
                yield return null;

            if (GameIsOver) yield break;

            WaveCleared?.Invoke(CurrentWave);

            IsIntermission = true;
            yield return Countdown(intermissionDuration);
            IsIntermission = false;
        }
    }

    IEnumerator Countdown(float duration)
    {
        IntermissionRemaining = duration;

        while (IntermissionRemaining > 0f && !GameIsOver)
        {
            IntermissionRemaining -= Time.deltaTime;
            yield return null;
        }

        IntermissionRemaining = 0f;
    }

    IEnumerator SpawnWave(int wave)
    {
        int total = Mathf.RoundToInt(baseEnemiesPerWave * Mathf.Pow(enemiesPerWaveGrowth, wave - 1));
        _pendingSpawns = total;

        while (_pendingSpawns > 0 && !GameIsOver)
        {
            if (_aliveCount >= maxAliveAtOnce)
            {
                yield return null;
                continue;
            }

            if (SpawnOne(wave)) _pendingSpawns--;
            yield return new WaitForSeconds(spawnInterval);
        }

        _pendingSpawns = 0;
    }

    // ======================================================================
    bool SpawnOne(int wave)
    {
        var type = PickEnemyType(wave);
        if (type == null || type.prefab == null) return false;

        if (!TryGetSpawnPosition(out Vector3 position)) return false;

        var enemy = Instantiate(type.prefab, position, FacePlayerFrom(position));
        ScaleForWave(enemy, wave);

        if (spawnEffectPrefab != null)
        {
            var fx = Instantiate(spawnEffectPrefab, position, Quaternion.identity);
            if (spawnEffectLifetime > 0f) Destroy(fx, spawnEffectLifetime);
        }

        var health = enemy.GetComponent<Health>();
        if (health != null) health.Died += OnEnemyDied;
        else Debug.LogWarning($"[WaveManager] {enemy.name} has no Health; it will never be counted as dead.", enemy);

        _alive.Add(enemy);
        _aliveCount++;
        return true;
    }

    EnemyType PickEnemyType(int wave)
    {
        float totalWeight = 0f;

        foreach (var type in enemyTypes)
            if (type != null && type.prefab != null && wave >= type.unlockWave)
                totalWeight += Mathf.Max(0f, type.weight);

        if (totalWeight <= 0f) return null;

        float roll = UnityEngine.Random.value * totalWeight;

        foreach (var type in enemyTypes)
        {
            if (type == null || type.prefab == null || wave < type.unlockWave) continue;

            roll -= Mathf.Max(0f, type.weight);
            if (roll <= 0f) return type;
        }

        return null;
    }

    bool TryGetSpawnPosition(out Vector3 position)
    {
        // Preferred: a fresh ring around the player, out of their line of sight.
        if (useDynamicSpawnPoints && player != null && TrySampleAroundPlayer(out position))
            return true;

        // Fallback: the authored spawn points.
        return TryUseFixedSpawnPoint(out position);
    }

    /// <summary>
    /// Samples the NavMesh in an annulus around the player. The first pass insists
    /// on positions the player cannot see; the second accepts any valid spot, so a
    /// cornered player still gets enemies.
    /// </summary>
    bool TrySampleAroundPlayer(out Vector3 position)
    {
        position = Vector3.zero;
        Vector3 origin = player.position;

        for (int pass = 0; pass < 2; pass++)
        {
            bool requireHidden = avoidPlayerView && pass == 0;

            for (int attempt = 0; attempt < 24; attempt++)
            {
                float angle = UnityEngine.Random.value * Mathf.PI * 2f;
                float distance = UnityEngine.Random.Range(minSpawnDistanceFromPlayer,
                                                          maxSpawnDistanceFromPlayer);

                Vector3 candidate = origin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas))
                    continue;

                if (requireHidden && IsVisibleToPlayer(hit.position)) continue;

                position = hit.position;
                return true;
            }
        }

        return false;
    }

    /// <summary>Rotation that faces the player, so nothing arrives with its back turned.</summary>
    Quaternion FacePlayerFrom(Vector3 position)
    {
        if (player == null) return Quaternion.identity;

        Vector3 flat = Vector3.ProjectOnPlane(player.position - position, Vector3.up);
        return flat.sqrMagnitude < 0.001f ? Quaternion.identity : Quaternion.LookRotation(flat);
    }

    bool IsVisibleToPlayer(Vector3 point)
    {
        Vector3 eye = player.position + Vector3.up * 1.6f;
        Vector3 target = point + Vector3.up * 1f;
        Vector3 toTarget = target - eye;

        // Behind or beside the player? Then geometry does not matter.
        if (Vector3.Angle(player.forward, toTarget) > playerViewAngle * 0.5f) return false;

        return !Physics.Linecast(eye, target, spawnSightBlockers, QueryTriggerInteraction.Ignore);
    }

    bool TryUseFixedSpawnPoint(out Vector3 position)
    {
        position = Vector3.zero;
        if (spawnPoints == null || spawnPoints.Length == 0) return false;

        var candidates = new List<Transform>();
        Transform farthest = null;
        float farthestDistance = -1f;

        foreach (var point in spawnPoints)
        {
            if (point == null) continue;

            float distance = player != null
                ? Vector3.Distance(point.position, player.position)
                : float.MaxValue;

            if (distance >= minSpawnDistanceFromPlayer) candidates.Add(point);

            if (distance > farthestDistance)
            {
                farthestDistance = distance;
                farthest = point;
            }
        }

        // If the player is standing on top of every spawn, fall back to the far one.
        Transform chosen = candidates.Count > 0
            ? candidates[UnityEngine.Random.Range(0, candidates.Count)]
            : farthest;

        if (chosen == null) return false;

        position = NavMesh.SamplePosition(chosen.position, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas)
            ? hit.position
            : chosen.position;

        return true;
    }

    void ScaleForWave(GameObject enemy, int wave)
    {
        int steps = Mathf.Max(0, wave - 1);
        if (steps == 0) return;

        var health = enemy.GetComponent<Health>();
        if (health != null)
        {
            health.maxHealth *= 1f + healthGrowthPerWave * steps;
            health.ResetHealth();   // Awake already ran, so top it back up.
        }

        var ai = enemy.GetComponent<EnemyAI>();
        if (ai != null)
        {
            ai.attackDamage *= 1f + damageGrowthPerWave * steps;
            if (player != null) ai.target = player;
        }

        var agent = enemy.GetComponent<NavMeshAgent>();
        if (agent != null)
            agent.speed *= Mathf.Min(maxSpeedMultiplier, 1f + speedGrowthPerWave * steps);
    }

    // ======================================================================
    void OnEnemyDied(Health health)
    {
        health.Died -= OnEnemyDied;

        _aliveCount = Mathf.Max(0, _aliveCount - 1);
        _alive.Remove(health.gameObject);
    }

    void OnPlayerDied(Health health)
    {
        if (GameIsOver) return;

        GameIsOver = true;
        StopAllCoroutines();
        GameOver?.Invoke(CurrentWave);
    }

    /// <summary>Clears the arena. Handy for a restart button or debugging.</summary>
    public void KillAllEnemies()
    {
        for (int i = _alive.Count - 1; i >= 0; i--)
        {
            if (_alive[i] == null) continue;

            var health = _alive[i].GetComponent<Health>();
            if (health != null) health.Kill();
        }
    }
}
