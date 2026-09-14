using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Endless wave spawner. Each wave is larger, tougher and meaner than the last:
/// the roster drifts toward nastier variants, the survivors are tuned more
/// aggressive, every few waves brings a boss, and most waves carry a modifier
/// that changes the shape of the fight rather than just its size.
///
/// The mix is data: a roster of <see cref="EnemyArchetype"/> assets stamped onto
/// one base prefab. Adding an enemy never means editing this file.
/// </summary>
public class WaveManager : MonoBehaviour
{
    /// <summary>
    /// Changes the character of a wave, not just its size. Pure numbers growth gets
    /// boring around wave six; a wave that is announced as something specific is
    /// one the player forms a plan for.
    /// </summary>
    public enum WaveModifier
    {
        None,

        /// <summary>Many more, much weaker. Crowd control and footwork.</summary>
        Horde,

        /// <summary>Half as many, armoured and slow. Aim, not spray.</summary>
        Elite,

        /// <summary>Fast and fragile. Punishes standing still.</summary>
        Swift,

        /// <summary>Normal bodies, far shorter fuses.</summary>
        Frenzy
    }

    [Serializable]
    public class EnemyType
    {
        [Tooltip("Leave empty to use the Base Enemy Prefab. Set it only for a variant " +
                 "that needs its own rig, like an animated character.")]
        public GameObject prefab;

        [Tooltip("The variant stamped onto the prefab. When this is set it supplies the " +
                 "weight, unlock wave and role, and the two fields below are ignored.")]
        public EnemyArchetype archetype;

        [Tooltip("Fallback selection weight for an entry with no archetype.")]
        [Min(0f)] public float weight = 1f;

        [Tooltip("Fallback unlock wave for an entry with no archetype.")]
        [Min(1)] public int unlockWave = 1;

        public float WeightAtWave(int wave)
            => archetype != null ? archetype.WeightAtWave(wave)
                                 : (wave >= unlockWave ? Mathf.Max(0f, weight) : 0f);

        public bool IsBoss => archetype != null && archetype.role == EnemyArchetype.Role.Boss;
    }

    [Header("Content")]
    [Tooltip("Spawned for any roster entry that does not name its own prefab.")]
    public GameObject baseEnemyPrefab;

    public EnemyType[] enemyTypes;
    public Transform[] spawnPoints;
    public Transform player;

    [Header("Drops")]
    public GameObject healthPickupPrefab;
    public GameObject shieldPickupPrefab;

    [Tooltip("Only useful once the weapon's infinite reserve is switched off.")]
    public GameObject ammoPickupPrefab;

    [Header("Wave Size")]
    public int baseEnemiesPerWave = 4;
    public float enemiesPerWaveGrowth = 1.35f;

    [Tooltip("Hard ceiling on the count of one wave, so wave 30 does not try to spawn " +
             "four hundred enemies and take the frame rate with it.")]
    public int maxEnemiesPerWave = 60;

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

    [Tooltip("Waves taken to reach full enemy aggression -- tighter cooldowns, harder " +
             "circling, straighter shooting. Stats keep climbing after this; behaviour does not.")]
    [Min(1)] public int aggressionRampWaves = 18;

    [Header("Boss Waves")]
    [Tooltip("A boss arrives on every multiple of this. 0 disables bosses entirely.")]
    [Min(0)] public int bossWaveInterval = 5;

    [Tooltip("Regular enemies on a boss wave, as a fraction of the normal count. " +
             "The boss is the fight; a full wave alongside it is just noise.")]
    [Range(0.1f, 1f)] public float bossWaveEscortFraction = 0.55f;

    [Header("Wave Modifiers")]
    public bool useModifiers = true;

    [Tooltip("Waves are plain until here, so the opening teaches the basic fight first.")]
    [Min(1)] public int modifierStartWave = 3;

    [Range(0f, 1f)] public float modifierChance = 0.55f;

    [Header("Rewards")]
    public int waveClearBonus = 250;

    // ---- state read by the HUD -------------------------------------------
    public int CurrentWave { get; private set; }
    public bool IsIntermission { get; private set; }
    public float IntermissionRemaining { get; private set; }
    public bool GameIsOver { get; private set; }
    public WaveModifier CurrentModifier { get; private set; }
    public bool IsBossWave { get; private set; }

    /// <summary>The live boss, or null. The HUD hangs its boss bar off this.</summary>
    public Health ActiveBoss { get; private set; }
    public string ActiveBossName { get; private set; } = "";

    public int EnemiesRemaining => _alive.Count + _pendingSpawns;

    /// <summary>0 on wave 1, 1 once the behaviour ramp has topped out.</summary>
    public float Aggression01 => Mathf.Clamp01((CurrentWave - 1f) / Mathf.Max(1, aggressionRampWaves));

    public event Action<int> WaveStarted;
    public event Action<int> WaveCleared;
    public event Action<int> GameOver;

    readonly List<GameObject> _alive = new List<GameObject>();
    int _pendingSpawns;
    Health _playerHealth;
    GameDirector _director;

    /// <summary>
    /// The live director, or a genuine null. Everything goes through this rather than
    /// using ?. on the field directly: the null-conditional operator does a plain
    /// reference check and skips Unity's overloaded ==, so on a destroyed object it
    /// sails past the guard and calls the method anyway -- a MissingReferenceException
    /// in the console instead of the quiet no-op the ? was written to express.
    /// </summary>
    GameDirector Director => _director != null ? _director : null;

    // ======================================================================
    void Start()
    {
        _director = GameDirector.Ensure();

        if (player == null)
        {
            var found = GameObject.FindGameObjectWithTag("Player");
            if (found != null) player = found.transform;
        }

        if (player != null)
        {
            _playerHealth = player.GetComponentInParent<Health>();
            if (_playerHealth != null)
            {
                _playerHealth.Died += OnPlayerDied;
                _playerHealth.Damaged += OnPlayerDamaged;
            }
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
        if (_playerHealth == null) return;

        _playerHealth.Died -= OnPlayerDied;
        _playerHealth.Damaged -= OnPlayerDamaged;
    }

    void Update() => SweepDead();

    /// <summary>
    /// Drops entries whose GameObject is gone. Counting kills through the Died event
    /// alone is not enough: an enemy destroyed any other way -- falling out of the
    /// world, a scene tear-down, anything calling Destroy directly -- would leave the
    /// tally permanently above zero and the wave loop would wait forever for a corpse
    /// that already left.
    /// </summary>
    void SweepDead()
    {
        for (int i = _alive.Count - 1; i >= 0; i--)
            if (_alive[i] == null) _alive.RemoveAt(i);
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
            IsBossWave = bossWaveInterval > 0 && CurrentWave % bossWaveInterval == 0;
            CurrentModifier = PickModifier(CurrentWave);

            WaveStarted?.Invoke(CurrentWave);

            yield return SpawnWave(CurrentWave);

            // Wait until the arena is clear.
            while (!GameIsOver && EnemiesRemaining > 0)
                yield return null;

            if (GameIsOver) yield break;

            ActiveBoss = null;
            ActiveBossName = "";

            Director?.AddScore(waveClearBonus * CurrentWave);
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
        total = Mathf.Clamp(Mathf.RoundToInt(total * CountScale(CurrentModifier)), 1, maxEnemiesPerWave);

        if (IsBossWave)
        {
            total = Mathf.Max(1, Mathf.RoundToInt(total * bossWaveEscortFraction));
            SpawnBoss(wave);
        }

        _pendingSpawns = total;

        // A spawn can legitimately fail -- a cornered player, a NavMesh with no room
        // in the ring. Retrying forever would stall the wave with nothing on screen,
        // so give up after a run of failures and let the round move on.
        int consecutiveFailures = 0;
        const int failureLimit = 40;
        const float retryInterval = 0.1f;

        while (_pendingSpawns > 0 && !GameIsOver)
        {
            if (_alive.Count >= maxAliveAtOnce)
            {
                yield return null;
                continue;
            }

            if (SpawnOne(wave))
            {
                _pendingSpawns--;
                consecutiveFailures = 0;

                yield return new WaitForSeconds(spawnInterval);
                continue;
            }

            if (++consecutiveFailures >= failureLimit)
            {
                Debug.LogWarning($"[WaveManager] Gave up on {_pendingSpawns} spawn(s) for wave {wave}: " +
                                 "no reachable position on the NavMesh. Check the bake and the " +
                                 "spawn distance range.", this);
                break;
            }

            // A failed attempt is not a spawn, so it must not cost a spawn interval.
            // Paying the full delay on each of forty retries would leave the arena
            // empty for a dozen seconds before the wave admitted defeat.
            yield return new WaitForSeconds(retryInterval);
        }

        _pendingSpawns = 0;
    }

    // ======================================================================
    bool SpawnOne(int wave)
    {
        var type = PickEnemyType(wave, boss: false);
        if (type == null) return false;

        return SpawnType(type, wave) != null;
    }

    void SpawnBoss(int wave)
    {
        var type = PickEnemyType(wave, boss: true);
        if (type == null) return;

        // Each boss wave stacks another 35% health on top of the normal curve, so the
        // fifth boss is a genuine wall rather than the first one wearing a bigger number.
        float health = 1f + 0.35f * (wave / Mathf.Max(1, bossWaveInterval));

        // The banner has already announced a boss by now, so one unlucky NavMesh sample
        // must not be allowed to turn the wave into a plain one with no explanation.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var boss = SpawnType(type, wave, extraHealthScale: health);
            if (boss == null) continue;

            ActiveBoss = boss.GetComponent<Health>();
            ActiveBossName = type.archetype != null
                ? type.archetype.displayName.ToUpperInvariant()
                : "BOSS";
            return;
        }

        Debug.LogWarning($"[WaveManager] Could not place the boss for wave {wave}: " +
                         "no reachable position on the NavMesh.", this);
    }

    GameObject SpawnType(EnemyType type, int wave, float extraHealthScale = 1f)
    {
        var prefab = type.prefab != null ? type.prefab : baseEnemyPrefab;
        if (prefab == null) return null;

        if (!TryGetSpawnPosition(out Vector3 position)) return null;

        var enemy = Instantiate(prefab, position, FacePlayerFrom(position));
        ConfigureForWave(enemy, type, wave, extraHealthScale);

        if (spawnEffectPrefab != null)
        {
            var fx = Instantiate(spawnEffectPrefab, position, Quaternion.identity);
            if (spawnEffectLifetime > 0f) Destroy(fx, spawnEffectLifetime);
        }

        var health = enemy.GetComponent<Health>();
        if (health != null) health.Died += OnEnemyDied;
        else Debug.LogWarning($"[WaveManager] {enemy.name} has no Health; it will never be counted as dead.", enemy);

        _alive.Add(enemy);
        return enemy;
    }

    /// <summary>
    /// Picks a variant for this wave. Bosses are drawn from their own pool so one
    /// can never wander into the regular mix, and nothing else can stand in for a boss.
    /// </summary>
    EnemyType PickEnemyType(int wave, bool boss)
    {
        float totalWeight = 0f;

        foreach (var type in enemyTypes)
        {
            if (!IsUsable(type, wave, boss)) continue;
            totalWeight += type.WeightAtWave(wave);
        }

        if (totalWeight <= 0f) return null;

        float roll = UnityEngine.Random.value * totalWeight;

        foreach (var type in enemyTypes)
        {
            if (!IsUsable(type, wave, boss)) continue;

            roll -= type.WeightAtWave(wave);
            if (roll <= 0f) return type;
        }

        return null;
    }

    bool IsUsable(EnemyType type, int wave, bool boss)
    {
        if (type == null) return false;
        if (type.prefab == null && baseEnemyPrefab == null) return false;
        if (type.IsBoss != boss) return false;

        return type.WeightAtWave(wave) > 0f;
    }

    // ======================================================================
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

    // ======================================================================
    // Difficulty
    // ======================================================================

    /// <summary>
    /// Applies the wave curve, the modifier and the archetype to one fresh enemy.
    /// The three compose: growth sets the baseline, the modifier bends the whole
    /// wave, and the archetype gives this particular body its character.
    /// </summary>
    void ConfigureForWave(GameObject enemy, EnemyType type, int wave, float extraHealthScale)
    {
        int steps = Mathf.Max(0, wave - 1);

        float healthScale = (1f + healthGrowthPerWave * steps) * HealthScale(CurrentModifier) * extraHealthScale;
        float damageScale = (1f + damageGrowthPerWave * steps) * DamageScale(CurrentModifier);
        float speedScale = Mathf.Min(maxSpeedMultiplier, 1f + speedGrowthPerWave * steps)
                           * SpeedScale(CurrentModifier);

        if (type.archetype != null)
        {
            type.archetype.ApplyTo(enemy, healthScale, damageScale, speedScale);
        }
        else
        {
            // No archetype: apply the wave curve directly to whatever the prefab has.
            var plainHealth = enemy.GetComponent<Health>();
            if (plainHealth != null) plainHealth.SetMaxHealth(plainHealth.maxHealth * healthScale);

            var plainAi = enemy.GetComponent<EnemyAI>();
            if (plainAi != null) plainAi.attackDamage *= damageScale;

            var plainAgent = enemy.GetComponent<NavMeshAgent>();
            if (plainAgent != null) plainAgent.speed *= speedScale;
        }

        var ai = enemy.GetComponent<EnemyAI>();
        if (ai != null)
        {
            if (player != null) ai.target = player;
            ai.ApplyWaveTuning(Mathf.Clamp01(Aggression01 + AggressionBonus(CurrentModifier)));
        }
    }

    WaveModifier PickModifier(int wave)
    {
        if (!useModifiers || wave < modifierStartWave) return WaveModifier.None;
        if (UnityEngine.Random.value > modifierChance) return WaveModifier.None;

        var options = new[] { WaveModifier.Horde, WaveModifier.Elite, WaveModifier.Swift, WaveModifier.Frenzy };
        return options[UnityEngine.Random.Range(0, options.Length)];
    }

    static float CountScale(WaveModifier m) => m switch
    {
        WaveModifier.Horde => 1.7f,
        WaveModifier.Elite => 0.55f,
        WaveModifier.Swift => 1.15f,
        _ => 1f
    };

    static float HealthScale(WaveModifier m) => m switch
    {
        WaveModifier.Horde => 0.7f,
        WaveModifier.Elite => 2.1f,
        WaveModifier.Swift => 0.8f,
        _ => 1f
    };

    static float DamageScale(WaveModifier m) => m switch
    {
        WaveModifier.Elite => 1.25f,
        WaveModifier.Frenzy => 0.85f,   // they hit far more often, so each hit lands softer
        _ => 1f
    };

    static float SpeedScale(WaveModifier m) => m switch
    {
        WaveModifier.Swift => 1.35f,
        WaveModifier.Elite => 0.85f,
        _ => 1f
    };

    static float AggressionBonus(WaveModifier m) => m == WaveModifier.Frenzy ? 0.4f : 0f;

    /// <summary>Short all-caps label for the wave banner. Empty when the wave is plain.</summary>
    public static string ModifierName(WaveModifier m) => m switch
    {
        WaveModifier.Horde => "HORDE",
        WaveModifier.Elite => "ELITE GUARD",
        WaveModifier.Swift => "SWIFT",
        WaveModifier.Frenzy => "FRENZY",
        _ => ""
    };

    /// <summary>One line telling the player what they are about to be hit with.</summary>
    public static string ModifierDescription(WaveModifier m) => m switch
    {
        WaveModifier.Horde => "Far more of them, far weaker",
        WaveModifier.Elite => "Fewer, armoured, hit harder",
        WaveModifier.Swift => "Fast and fragile - keep moving",
        WaveModifier.Frenzy => "Short fuses, relentless attacks",
        _ => ""
    };

    // ======================================================================
    void OnEnemyDied(Health health)
    {
        health.Died -= OnEnemyDied;
        _alive.Remove(health.gameObject);

        if (ActiveBoss == health) ActiveBoss = null;

        var ai = health.GetComponent<EnemyAI>();
        var archetype = ai != null ? ai.archetype : null;

        Director?.RegisterKill(archetype, health.LastDamage.isHeadshot, health.transform.position);

        TryDrop(archetype, health.transform.position);
    }

    /// <summary>
    /// Rolls for a drop. The rolls are tried in order and stop at the first hit, so an
    /// enemy drops at most one thing and health always wins the tie -- being alive is
    /// worth more than anything else on the floor.
    /// </summary>
    void TryDrop(EnemyArchetype archetype, Vector3 position)
    {
        Vector3 spot = position + Vector3.up * 0.6f;

        float healthChance = archetype != null ? archetype.healthDropChance : 0.08f;
        if (TrySpawnDrop(healthPickupPrefab, healthChance, spot)) return;

        float shieldChance = archetype != null ? archetype.shieldDropChance : 0.06f;
        if (TrySpawnDrop(shieldPickupPrefab, shieldChance, spot)) return;

        float ammoChance = archetype != null ? archetype.ammoDropChance : 0f;
        TrySpawnDrop(ammoPickupPrefab, ammoChance, spot);
    }

    bool TrySpawnDrop(GameObject prefab, float chance, Vector3 spot)
    {
        if (prefab == null || chance <= 0f || UnityEngine.Random.value >= chance) return false;

        Instantiate(prefab, spot, Quaternion.identity);
        return true;
    }

    void OnPlayerDamaged(Health health, DamageInfo info) => Director?.BreakCombo();

    void OnPlayerDied(Health health)
    {
        if (GameIsOver) return;

        GameIsOver = true;
        StopAllCoroutines();

        Director?.ReportGameOver(CurrentWave);
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
