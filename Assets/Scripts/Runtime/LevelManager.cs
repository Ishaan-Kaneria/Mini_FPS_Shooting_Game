using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Runs one level: a fixed roster of enemies, a strict clock, and a score cut into
/// stars at the end.
///
/// This is what replaced the endless wave spawner. A wave survival run has no shape --
/// it ends when the player dies and the only question is how long that took -- and
/// nothing in it can be locked, practised or beaten. A level can: it states what has
/// to die and how long there is to do it, and the answer is three stars, fewer, or
/// "try again".
///
/// The mix is still data. A roster of <see cref="EnemyArchetype"/> assets is stamped
/// onto one base prefab, and which of them a level draws from is the level's
/// <see cref="LevelSet.Level.rosterStep"/>. Adding an enemy never means editing this
/// file, and neither does adding a level.
///
/// Three things are carried over from the wave spawner unchanged, because they are
/// about a level being a real place rather than about waves:
///
///   - the golden-angle spawn ring, which keeps arrivals out of the player's view and
///     spread around them rather than funnelled through one corner;
///   - the leash, which discards an enemy that has fallen out of the level or left
///     the NavMesh -- and, here, puts another one in the queue in its place, so a hole
///     in the geometry cannot quietly make three stars impossible;
///   - the clock, which is now the level's own rule rather than a safety net.
/// </summary>
public class LevelManager : MonoBehaviour
{
    [Serializable]
    public class EnemyType
    {
        [Tooltip("Leave empty to use the Base Enemy Prefab. Set it only for a variant " +
                 "that needs its own rig, like an animated character.")]
        public GameObject prefab;

        [Tooltip("The variant stamped onto the prefab. When this is set it supplies the " +
                 "weight, difficulty step and role, and the two fields below are ignored.")]
        public EnemyArchetype archetype;

        [Tooltip("Fallback selection weight for an entry with no archetype.")]
        [Min(0f)] public float weight = 1f;

        [Tooltip("Fallback difficulty step for an entry with no archetype.")]
        [Min(1)] public int unlockStep = 1;

        public float WeightAtStep(int step)
            => archetype != null ? archetype.WeightAtStep(step)
                                 : (step >= unlockStep ? Mathf.Max(0f, weight) : 0f);

        public bool IsBoss => archetype != null && archetype.role == EnemyArchetype.Role.Boss;
    }

    [Header("Content")]
    [Tooltip("Spawned for any roster entry that does not name its own prefab.")]
    public GameObject baseEnemyPrefab;

    public EnemyType[] enemyTypes;
    public Transform[] spawnPoints;
    public Transform player;

    [Tooltip("The levels this arena offers. Which one is played comes from GameSession, " +
             "so the dashboard decides and this only has to run it. With none wired, one " +
             "stand-in level is generated so a hand-built scene is still playable.")]
    public LevelSet levels;

    [Tooltip("Played when the dashboard has not chosen one -- pressing Play with an " +
             "arena open, or a scene opened directly by a test.")]
    [Min(0)] public int defaultLevelIndex;

    [Header("Drops")]
    public GameObject healthPickupPrefab;
    public GameObject shieldPickupPrefab;

    [Tooltip("Only useful once the weapon's infinite reserve is switched off.")]
    public GameObject ammoPickupPrefab;

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

    [Header("Enemy Leash")]
    [Tooltip("Enemies further away than this are removed. Deliberately large -- this is a " +
             "safety net for a real level, not a gameplay rule. It is what stops an enemy " +
             "that fell down a hole, wedged on geometry or wandered off the map from " +
             "counting against you forever.")]
    public float despawnDistance = 95f;

    [Tooltip("Seconds an enemy has to stay lost before it is removed, so one that is " +
             "legitimately taking the long way round is not deleted mid-approach.")]
    public float despawnGraceTime = 4f;

    [Tooltip("Metres below you an enemy has to fall before it counts as gone. An imported " +
             "level almost always has a gap somewhere, and whatever goes down it keeps " +
             "falling forever.")]
    public float fallKillDepth = 60f;

    [Tooltip("Treat an enemy whose agent has left the NavMesh as lost. This is the fastest " +
             "way to catch a fall, long before it is far enough away to trip the distance check.")]
    public bool despawnOffNavMesh = true;

    [Tooltip("Refuse to spawn anywhere the NavMesh cannot actually walk from to you, and " +
             "discard an enemy that ends up somewhere it cannot. A sealed shed, the inside " +
             "of a bund, the roof of a container stack -- all of them bake walkable ground " +
             "that is joined to nothing, and an enemy put on one stands there for the whole " +
             "level while the clock runs out. Off, only the distance leash catches it, and " +
             "only if it happens to be far away.")]
    public bool requireReachableSpawns = true;

    [Tooltip("Seconds between the route checks behind the setting above. A path query is " +
             "the most expensive thing per enemy here -- an unreachable one costs a search " +
             "of its whole island before it can fail -- so it is rationed and the answer is " +
             "held in between, the same way the enemy's line of sight is.")]
    public float reachCheckInterval = 1.5f;

    [Tooltip("Put a fresh enemy in the queue for every one the leash discards. This is " +
             "what keeps three stars honest: the level asks for a fixed number of kills, " +
             "and a body that fell through a gap in the floor is not the player's mistake " +
             "to pay for. Bounded below so a level that cannot place anyone gives up " +
             "rather than churning.")]
    public bool replaceLostEnemies = true;

    [Header("Rewards")]
    [Tooltip("Awarded for clearing the level, multiplied by the level number.")]
    public int levelClearBonus = 400;

    [Tooltip("Points per second left on the clock when the level is cleared. Clearing it " +
             "fast has to be worth something or the strict clock is only ever a threat.")]
    public int timeBonusPerSecond = 25;

    [Tooltip("Points per star, so the results screen has a number that moves with them.")]
    public int starBonus = 500;

    // ---- state read by the HUD -------------------------------------------

    /// <summary>Zero-based position in the set. What the dashboard chose.</summary>
    public int LevelIndex { get; private set; }

    /// <summary>One-based, for anything a player reads.</summary>
    public int LevelNumber => LevelIndex + 1;

    public string LevelName { get; private set; } = "";
    public string LevelBrief { get; private set; } = "";

    /// <summary>
    /// Why the clock exists here, in the arena's own voice -- the sealing gates, the
    /// closing pass, the venting dome.
    ///
    /// <b>A strict clock with no reason given reads as an arcade timer</b>, which is
    /// the one thing this game is not meant to feel like: the level is a place somebody
    /// has to get out of. It is per-arena rather than per-level because that is what it
    /// describes, and it is on the LevelSet because that is the asset the arena already
    /// holds.
    /// </summary>
    public string LevelStakes { get; private set; } = "";

    /// <summary>The level being played. Never null once Start has run.</summary>
    public LevelSet.Level Level { get; private set; }

    /// <summary>The arena, as progress is keyed by it. The active scene unless overridden.</summary>
    public string Arena { get; private set; } = "";

    /// <summary>Counting down to the start. The clock is not running yet.</summary>
    public bool IsBriefing { get; private set; }
    public float BriefingRemaining { get; private set; }

    public bool IsRunning { get; private set; }
    public bool IsFinished { get; private set; }

    /// <summary>Seconds left. This is the level's rule, not a safety net.</summary>
    public float TimeRemaining { get; private set; }
    public float TimeLimit { get; private set; }

    /// <summary>Bodies this level asks for, boss included.</summary>
    public int TotalEnemies { get; private set; }

    public int Killed { get; private set; }

    /// <summary>Enemies removed for being lost rather than killed. Diagnostic only.</summary>
    public int EnemiesDiscarded { get; private set; }

    /// <summary>The live boss, or null. The HUD hangs its boss bar off this.</summary>
    public Health ActiveBoss { get; private set; }
    public string ActiveBossName { get; private set; } = "";
    public bool HasBoss { get; private set; }
    public bool BossKilled { get; private set; }

    /// <summary>
    /// How much of the level has been killed, weighted the way the stars count it. The
    /// HUD draws this as a bar with the star thresholds marked on it, so the player can
    /// see what the next star costs while there is still time to go and get it.
    /// </summary>
    /// <summary>
    /// What the level asks for beyond the roster, or null on a plain clear. Created at
    /// the start of every run and destroyed at the end of it.
    /// </summary>
    public LevelObjective Objective { get; private set; }

    /// <summary>One line about the objective for the HUD, or empty.</summary>
    public string ObjectiveLine => Objective != null ? Objective.HudLine : "";

    /// <summary>
    /// How much of this level has been earned, counting the objective beside the kills.
    ///
    /// The objective's weight is in <see cref="LevelSet.Level.TotalWeight"/> already, so
    /// a level with one is not three stars for the roster alone -- which is the whole
    /// reason for having one.
    /// </summary>
    public float ScoreFraction => _totalWeight <= 0f
        ? 0f
        : Mathf.Clamp01((_killedWeight + (Objective != null ? Objective.EarnedWeight : 0f))
                        / _totalWeight);

    /// <summary>Set once, when the level ends. Read by the results screen.</summary>
    public LevelResult Result { get; private set; }

    public event Action<LevelManager> LevelStarted;
    public event Action<LevelResult> LevelFinished;

    /// <summary>
    /// One live enemy plus the bookkeeping the leash needs. A class rather than a
    /// struct so the stranded clock can be updated in place while it sits in the list.
    /// </summary>
    class Tracked
    {
        public GameObject go;
        public Health health;
        public NavMeshAgent agent;
        public bool isBoss;

        /// <summary>When it was first judged lost, or -1 while it is behaving.</summary>
        public float strandedSince = -1f;

        /// <summary>The last route answer, and when it is worth asking again.</summary>
        public bool routeBlocked;
        public float nextReachCheck;
    }

    readonly List<Tracked> _alive = new List<Tracked>();

    NavMeshPath _routeScratch;

    /// <summary>
    /// Scratch for every route question asked here. One instance rather than one per
    /// call: <see cref="NavMeshPath"/> owns native memory, and a fresh one per spawn
    /// attempt is a few hundred allocations a level for an answer read immediately and
    /// never kept.
    ///
    /// <b>Made on demand, never in a field initialiser.</b> A field initialiser runs
    /// inside the MonoBehaviour's constructor, and Unity refuses to build a NavMeshPath
    /// there -- it throws <c>InitializeNavMeshPath is not allowed to be called from a
    /// MonoBehaviour constructor</c>, leaves the field null, and the exception is
    /// swallowed into the construction of the object rather than reported against
    /// anything you wrote. What that looks like from the game is a level that spawns
    /// nothing at all: every route query throws a NullReferenceException inside the
    /// spawn coroutine, every spawn attempt fails, and the arena stays empty until the
    /// clock ends it. Same rule, and the same symptom, as <c>EnemyAI.Block</c> -- a
    /// private property with a null check, and nothing in Awake.
    /// </summary>
    NavMeshPath Route => _routeScratch ??= new NavMeshPath();

    int _pendingSpawns;
    int _replacements;
    float _spawnAngle;
    float _killedWeight;
    float _totalWeight;

    /// <summary>
    /// When the clock runs out, as a wall-clock time. A field rather than a local so a
    /// charge going off can take seconds out of it -- see <see cref="PenaliseClock"/>.
    /// </summary>
    float _deadline;
    float _startedAt;

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

    /// <summary>
    /// The level loop. Held so a lost one can be told apart from a finished one --
    /// see <see cref="RecoverLevelLoop"/>.
    /// </summary>
    Coroutine _levelRoutine;

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
            Debug.LogError("[LevelManager] No enemy types assigned.", this);
            return;
        }

        ResolveLevel();

        // A leash shorter than the spawn ring deletes enemies the instant they arrive,
        // which reads as "nothing ever spawns" and is miserable to diagnose from the
        // symptom. Push it clear of the ring and say so rather than let it happen.
        float minimumLeash = maxSpawnDistanceFromPlayer * 1.4f;
        if (despawnDistance > 0f && despawnDistance < minimumLeash)
        {
            Debug.LogWarning($"[LevelManager] Despawn Distance ({despawnDistance:0}m) is too close to " +
                             $"Max Spawn Distance ({maxSpawnDistanceFromPlayer:0}m); enemies would be " +
                             $"culled on arrival. Raised to {minimumLeash:0}m.", this);
            despawnDistance = minimumLeash;
        }

        _levelRoutine = StartCoroutine(RunLevel());
    }

    void OnDestroy()
    {
        if (_playerHealth == null) return;

        _playerHealth.Died -= OnPlayerDied;
        _playerHealth.Damaged -= OnPlayerDamaged;
    }

    void Update()
    {
        RecoverLevelLoop();
        SweepEnemies();
    }

    /// <summary>
    /// Works out which level is being played, and from what.
    ///
    /// The dashboard picks one and leaves it in <see cref="GameSession"/>; pressing
    /// Play with an arena open leaves nothing there, so the default index applies. A
    /// scene with no level set at all still gets a playable level rather than an error,
    /// for the same reason every audio field in the kit is optional: a hand-built scene
    /// that has not been through the builder should still run.
    ///
    /// Public and safe to call again, because the level's numbers are cached here: a
    /// LevelSet retuned while the game is running -- in the Inspector, or by the level
    /// test shortening a clock -- otherwise leaves the manager holding the old clock and
    /// the old target for the rest of the level. Only meaningful before the clock
    /// starts; calling it mid-level would move the goalposts under the player.
    /// </summary>
    public void ResolveLevel()
    {
        // The level set names the arena, and that name -- not the scene's -- is what
        // stars are filed under. A WebGL build plays a renamed staging copy of every
        // scene, so keying on the active scene would file a browser player's progress
        // under a different arena from everyone else's. See ArenaCatalog.Entry.ProgressKey.
        Arena = levels != null && !string.IsNullOrWhiteSpace(levels.arenaScene)
            ? levels.arenaScene
            : !string.IsNullOrEmpty(GameSession.SelectedArena)
                ? GameSession.SelectedArena
                : UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        int count = levels != null ? levels.Count : 0;

        LevelIndex = GameSession.HasSelectedLevel
            ? GameSession.SelectedLevel
            : defaultLevelIndex;

        if (count > 0) LevelIndex = Mathf.Clamp(LevelIndex, 0, count - 1);
        else LevelIndex = 0;

        Level = levels != null ? levels.At(LevelIndex) : null;

        if (Level == null)
        {
            Debug.LogWarning("[LevelManager] No LevelSet is wired, so a stand-in level is " +
                             "being used. Run FPSKit > Build Scene to get the real ones.", this);

            Level = new LevelSet.Level();
        }

        LevelName = Level.Label(LevelIndex);
        LevelBrief = Level.brief ?? "";
        LevelStakes = levels != null ? levels.stakes ?? "" : "";

        HasBoss = Level.hasBoss;
        TimeLimit = Level.timeLimit;
        TimeRemaining = TimeLimit;
        TotalEnemies = Level.enemyCount + (HasBoss ? 1 : 0);
        _totalWeight = Level.TotalWeight;
    }

    /// <summary>
    /// Restarts the level loop if it is ever lost.
    ///
    /// RunLevel *is* the level: it counts the briefing down, spawns the roster, holds
    /// the clock and scores the result. It is started once from Start, so anything that
    /// kills it leaves the arena silently empty with the HUD still showing a level in
    /// progress. In the editor the way that happens is recompiling a script during play,
    /// which reloads the domain and kills every coroutine without re-running Start.
    ///
    /// A restarted level starts over from the briefing, which is the honest thing to do:
    /// the clock and the kill count would otherwise carry across a seam the player never
    /// asked for. A finished level is never revived.
    /// </summary>
    /// <summary>
    /// Stops whatever is running and plays this level again from the briefing.
    ///
    /// It exists for the objective check, which plays six different objectives in one
    /// session rather than paying for six play-mode entries -- the same reason
    /// <see cref="KillAllEnemies"/> is public. The level is re-read on the way in, so a
    /// test that retuned the asset gets the level it just wrote rather than the one
    /// that was resolved at Start.
    ///
    /// Everything alive is discarded without being counted as lost, because this is not
    /// something that happens to a player: nothing here should reach the replacement
    /// queue or the score.
    /// </summary>
    public void RestartLevel()
    {
        StopAllCoroutines();

        _levelRoutine = null;
        _pendingSpawns = 0;

        DiscardAll(countAsLost: false);

        if (Objective != null) Objective.End();
        Destroy(Objective);
        Objective = null;

        IsFinished = false;
        IsRunning = false;
        IsBriefing = false;

        ActiveBoss = null;
        ActiveBossName = "";

        ResolveLevel();

        _levelRoutine = StartCoroutine(RunLevel());
    }

    void RecoverLevelLoop()
    {
        if (_levelRoutine != null || IsFinished) return;

        Debug.LogWarning("[LevelManager] The level loop was lost and has been restarted. In the " +
                         "editor this means a script was recompiled while play mode was running.", this);

        _levelRoutine = StartCoroutine(RunLevel());
    }

    // ======================================================================
    IEnumerator RunLevel()
    {
        Killed = 0;
        _killedWeight = 0f;
        BossKilled = false;

        // Replaced rather than reused, because Restart re-runs a level in the same scene
        // and an objective carries the ground it marked and the lights it took.
        if (Objective != null) Objective.End();
        Destroy(Objective);

        Objective = LevelObjective.Begin(this, Level);

        IsBriefing = true;
        BriefingRemaining = Level.briefingTime;

        while (BriefingRemaining > 0f && !IsFinished)
        {
            BriefingRemaining -= Time.deltaTime;
            yield return null;
        }

        BriefingRemaining = 0f;
        IsBriefing = false;

        if (IsFinished) yield break;

        IsRunning = true;
        _startedAt = Time.time;
        TimeRemaining = TimeLimit;

        LevelStarted?.Invoke(this);

        StartCoroutine(SpawnLevel());

        // The clock is the level's own rule now, so it is checked first and without
        // exception: a level that quietly ran on past its limit because one enemy was
        // still walking over would make every star meaningless.
        _deadline = Time.time + TimeLimit;

        // The objective may hold the level open past a cleared roster -- it may never
        // hold it open past the clock, and it is not consulted about that at all. A
        // level whose extraction point is unreachable runs out its clock and is scored
        // on what was actually done, which is the only behaviour that cannot softlock.
        while (!IsFinished && Time.time < _deadline
               && (Killed < TotalEnemies || !ObjectiveSatisfied))
        {
            TimeRemaining = Mathf.Max(0f, _deadline - Time.time);
            yield return null;
        }

        TimeRemaining = Mathf.Max(0f, _deadline - Time.time);

        if (IsFinished) yield break;

        Finish(Killed >= TotalEnemies && ObjectiveSatisfied
            ? LevelResult.Ending.Cleared
            : LevelResult.Ending.TimeUp);
    }

    /// <summary>
    /// True when the level's extra task is done, or when it has none. <b>Only ever used
    /// to decide whether a cleared roster may finish early</b> -- never whether the
    /// level ends.
    /// </summary>
    bool ObjectiveSatisfied => Objective == null || Objective.Satisfied;

    /// <summary>
    /// Takes seconds off the clock. The only thing in the game that does, and the whole
    /// reason a charge going off matters.
    ///
    /// It cannot end the level on its own: the loop above checks the deadline on the
    /// next frame either way, so the worst this does is bring that frame forward.
    /// </summary>
    public void PenaliseClock(float seconds)
    {
        if (seconds <= 0f || !IsRunning) return;

        _deadline -= seconds;
        TimeRemaining = Mathf.Max(0f, _deadline - Time.time);
    }

    /// <summary>
    /// Whether the player could walk to a point. Public so <see cref="LevelObjective"/>
    /// can ask it before marking ground -- an objective on an island is an objective the
    /// player is scored against and cannot reach, which is the same failure the spawner
    /// already refuses.
    /// </summary>
    public bool CanPlayerReach(Vector3 point) => CanReachPlayerFrom(point);

    IEnumerator SpawnLevel()
    {
        if (HasBoss) SpawnBoss();

        _pendingSpawns = Level.enemyCount;
        _replacements = 0;

        // A spawn can legitimately fail -- a cornered player, a NavMesh with no room
        // in the ring. Retrying forever would stall the level with nothing on screen,
        // so give up after a run of failures and let the clock decide it.
        int consecutiveFailures = 0;
        const int failureLimit = 40;
        const float retryInterval = 0.1f;

        while (_pendingSpawns > 0 && !IsFinished)
        {
            if (_alive.Count >= Mathf.Max(1, Level.maxAliveAtOnce))
            {
                yield return null;
                continue;
            }

            if (SpawnOne())
            {
                _pendingSpawns--;
                consecutiveFailures = 0;

                yield return new WaitForSeconds(Level.spawnInterval);
                continue;
            }

            if (++consecutiveFailures >= failureLimit)
            {
                Debug.LogWarning($"[LevelManager] Gave up on {_pendingSpawns} spawn(s) for " +
                                 $"\"{LevelName}\": no reachable position on the NavMesh. Check the " +
                                 "bake and the spawn distance range.", this);
                break;
            }

            // A failed attempt is not a spawn, so it must not cost a spawn interval.
            // Paying the full delay on each of forty retries would leave the arena
            // empty for a dozen seconds before the level admitted defeat.
            yield return new WaitForSeconds(retryInterval);
        }

        _pendingSpawns = 0;
    }

    // ======================================================================
    // Ending
    // ======================================================================

    /// <summary>
    /// Scores the level and hands the result to everything that reacts to it. Idempotent:
    /// the clock, the last kill and the player's death can all arrive on the same frame,
    /// and the first one to get here owns the ending.
    /// </summary>
    void Finish(LevelResult.Ending ending)
    {
        if (IsFinished) return;

        IsFinished = true;
        IsRunning = false;
        IsBriefing = false;

        StopAllCoroutines();
        _levelRoutine = null;
        _pendingSpawns = 0;

        bool cleared = ending == LevelResult.Ending.Cleared && Killed >= TotalEnemies;
        float fraction = ScoreFraction;
        int stars = LevelResult.StarsFor(fraction, cleared, Level);

        float taken = Mathf.Min(TimeLimit, Mathf.Max(0f, Time.time - _startedAt));

        // Banked before the result is built, so the score on the results screen is the
        // score that was stored. Clearing it fast is worth something or the strict clock
        // would only ever be a threat.
        if (cleared)
        {
            Director?.AddScore(levelClearBonus * LevelNumber +
                               Mathf.RoundToInt(TimeRemaining) * timeBonusPerSecond);
        }

        if (stars > 0) Director?.AddScore(starBonus * stars);

        var director = Director;

        // The coins come after the score bonuses, because part of the payout is a
        // fraction of the score -- and before the result is built, because the result
        // is a struct and everything downstream gets a copy of whatever is in it now.
        director?.AwardLevelCoins(stars);

        var result = new LevelResult
        {
            arena = Arena,
            levelIndex = LevelIndex,
            levelName = LevelName,
            ending = ending,
            stars = stars,
            killed = Killed,
            total = TotalEnemies,
            scoreFraction = fraction,
            bossKilled = BossKilled,
            hadBoss = HasBoss,
            timeTaken = taken,
            timeLimit = TimeLimit,
            score = director != null ? director.Score : 0,
            headshots = director != null ? director.Headshots : 0,
            coins = director != null ? director.CoinsEarned : 0,

            // Everything an achievement needs has to be in the result before it is passed
            // anywhere, for the same reason the coin bonus is awarded before this is
            // built: LevelResult is a struct, so every consumer downstream gets a copy.
            bombKills = director != null ? director.BombKills : 0,
            bestCombo = director != null ? director.BestCombo : 0,
            damageTaken = director != null ? director.DamageTaken : 0f
        };

        Result = result;

        // The survivors go before the results screen does, so the panel is not read
        // over the top of a fight that is still happening behind it.
        DiscardAll(countAsLost: false);

        ActiveBoss = null;
        ActiveBossName = "";

        director?.ReportLevelFinished(result);
        LevelFinished?.Invoke(result);
    }

    // ======================================================================
    // The leash
    // ======================================================================

    /// <summary>
    /// Drops entries whose GameObject is gone, and removes the ones that are still
    /// there but no longer part of the fight.
    ///
    /// Counting kills through the Died event alone is not enough. An enemy destroyed
    /// any other way would leave the tally permanently below its target, and the level
    /// could never be cleared; and in a real imported level an enemy that drops through
    /// a gap never dies at all -- it just falls, forever, alive, with three stars
    /// waiting on it.
    /// </summary>
    void SweepEnemies()
    {
        for (int i = _alive.Count - 1; i >= 0; i--)
        {
            var tracked = _alive[i];

            if (tracked.go == null)
            {
                _alive.RemoveAt(i);
                continue;
            }

            if (player == null) continue;

            if (!IsLost(tracked))
            {
                tracked.strandedSince = -1f;
                continue;
            }

            if (tracked.strandedSince < 0f)
            {
                tracked.strandedSince = Time.time;
                continue;
            }

            if (Time.time - tracked.strandedSince < despawnGraceTime) continue;

            Discard(i, countAsLost: true);
        }
    }

    /// <summary>
    /// True when an enemy has stopped being a threat you could reasonably deal with:
    /// far away, fallen out of the level, or standing somewhere it cannot path from.
    /// </summary>
    bool IsLost(Tracked tracked)
    {
        Vector3 position = tracked.go.transform.position;

        // Fallen through the floor. Checked first because it is the unrecoverable one.
        if (fallKillDepth > 0f && player.position.y - position.y > fallKillDepth) return true;

        if (despawnDistance > 0f &&
            (position - player.position).sqrMagnitude > despawnDistance * despawnDistance)
            return true;

        // An agent off the NavMesh cannot reach you by any route. This catches a fall
        // the frame it starts, long before the body is far enough away to be culled.
        if (despawnOffNavMesh && tracked.agent != null && tracked.agent.enabled &&
            !tracked.agent.isOnNavMesh)
            return true;

        // And an agent on a scrap of NavMesh joined to nothing cannot reach you either,
        // however close it is standing. The distance leash never catches this one --
        // an enemy sealed inside a shed forty metres away is well inside every other
        // limit here and will stand in it until the clock ends the level.
        return IsWalledIn(tracked);
    }

    /// <summary>
    /// Whether this enemy currently has no route to the player, cached between checks.
    ///
    /// Rationed, because a failing path query is the expensive one: a complete route is
    /// found and returned, while an impossible one costs a search of the whole island
    /// the agent is standing on before it can say so. The first check is offset per
    /// enemy so a level's worth of them does not all ask on the same frame.
    /// </summary>
    bool IsWalledIn(Tracked tracked)
    {
        if (!requireReachableSpawns || tracked.go == null) return false;
        if (Time.time < tracked.nextReachCheck) return tracked.routeBlocked;

        tracked.nextReachCheck = Time.time + Mathf.Max(0.25f, reachCheckInterval);
        tracked.routeBlocked = !CanReachPlayerFrom(tracked.go.transform.position);

        return tracked.routeBlocked;
    }

    /// <summary>
    /// Removes a tracked enemy without crediting a kill. Discarding is not killing:
    /// no score, no combo, no drop, and nothing toward a star.
    ///
    /// A discarded body is re-queued instead, up to a bound. The level asked for a
    /// fixed number of kills and the player is being scored against it, so an enemy
    /// that fell into a hole has to be replaced or the geometry has quietly taken a
    /// star away.
    /// </summary>
    void Discard(int index, bool countAsLost)
    {
        var tracked = _alive[index];
        _alive.RemoveAt(index);

        if (tracked.health != null) tracked.health.Died -= OnEnemyDied;
        if (ActiveBoss == tracked.health) ActiveBoss = null;
        if (tracked.go != null) Destroy(tracked.go);

        if (!countAsLost) return;

        EnemiesDiscarded++;

        if (!replaceLostEnemies || IsFinished) return;

        // The boss is one body and one bar; queueing "another boss" would mean two of
        // them if the first turns up again, so it is put back directly.
        if (tracked.isBoss)
        {
            SpawnBoss();
            return;
        }

        if (_replacements >= Level.enemyCount) return;

        _replacements++;
        _pendingSpawns++;
    }

    void DiscardAll(bool countAsLost)
    {
        for (int i = _alive.Count - 1; i >= 0; i--) Discard(i, countAsLost);
    }

    // ======================================================================
    // Spawning
    // ======================================================================
    bool SpawnOne()
    {
        var type = PickEnemyType(boss: false);
        if (type == null) return false;

        return SpawnType(type, boss: false) != null;
    }

    void SpawnBoss()
    {
        var type = BossType();
        if (type == null)
        {
            Debug.LogWarning($"[LevelManager] \"{LevelName}\" asks for a boss but the roster has " +
                             "no archetype with the Boss role.", this);
            return;
        }

        // The banner has already announced a boss by now, so one unlucky NavMesh sample
        // must not be allowed to turn the level into a plain one with no explanation.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var boss = SpawnType(type, boss: true);
            if (boss == null) continue;

            ActiveBoss = boss.GetComponent<Health>();
            // The level's own name wins: the campaign fights two boss chassis as eight
            // different people, and the archetype only knows which chassis it is.
            ActiveBossName = !string.IsNullOrWhiteSpace(Level.bossName)
                ? Level.bossName.ToUpperInvariant()
                : type.archetype != null
                ? type.archetype.displayName.ToUpperInvariant()
                : "BOSS";
            return;
        }

        Debug.LogWarning($"[LevelManager] Could not place the boss for \"{LevelName}\": " +
                         "no reachable position on the NavMesh.", this);
    }

    /// <summary>
    /// The boss this level names, or the best one the roster can offer.
    ///
    /// The level's own choice wins and is not filtered by the difficulty step, because
    /// a level that says which boss it has means it. The fallback ignores the step too:
    /// a boss level with no eligible boss is a level that silently becomes a plain one,
    /// which is worse than an early boss.
    /// </summary>
    EnemyType BossType()
    {
        if (Level.bossArchetype != null)
        {
            foreach (var type in enemyTypes)
                if (type != null && type.archetype == Level.bossArchetype) return type;

            // Named a boss the roster does not carry: still spawn it, on the base prefab.
            return new EnemyType { archetype = Level.bossArchetype };
        }

        var eligible = PickEnemyType(boss: true);
        if (eligible != null) return eligible;

        foreach (var type in enemyTypes)
            if (type != null && type.IsBoss) return type;

        return null;
    }

    GameObject SpawnType(EnemyType type, bool boss)
    {
        var prefab = type.prefab != null ? type.prefab : baseEnemyPrefab;
        if (prefab == null) return null;

        if (!TryGetSpawnPosition(out Vector3 position)) return null;

        var enemy = Instantiate(prefab, position, FacePlayerFrom(position));
        Configure(enemy, type, boss);

        if (spawnEffectPrefab != null)
        {
            var fx = Instantiate(spawnEffectPrefab, position, Quaternion.identity);
            if (spawnEffectLifetime > 0f) Destroy(fx, spawnEffectLifetime);
        }

        var health = enemy.GetComponent<Health>();
        if (health != null) health.Died += OnEnemyDied;
        else Debug.LogWarning($"[LevelManager] {enemy.name} has no Health; it will never be counted as dead.", enemy);

        _alive.Add(new Tracked
        {
            go = enemy,
            health = health,
            agent = enemy.GetComponent<NavMeshAgent>(),
            isBoss = boss,

            // Spread over one interval, so forty enemies do not all ask for a route on
            // the same frame and turn a rationed check back into a stutter. Seeded in
            // the future rather than at zero because the spawner has just proved this
            // one can reach the player.
            nextReachCheck = Time.time + UnityEngine.Random.Range(0.5f, 1f) * reachCheckInterval
        });

        return enemy;
    }

    /// <summary>
    /// Picks a variant for this level's difficulty step. Bosses are drawn from their own
    /// pool so one can never wander into the regular mix, and nothing else can stand in
    /// for a boss.
    /// </summary>
    EnemyType PickEnemyType(bool boss)
    {
        int step = Mathf.Max(1, Level.rosterStep);
        float totalWeight = 0f;

        foreach (var type in enemyTypes)
        {
            if (!IsUsable(type, step, boss)) continue;
            totalWeight += type.WeightAtStep(step);
        }

        if (totalWeight <= 0f) return null;

        float roll = UnityEngine.Random.value * totalWeight;

        foreach (var type in enemyTypes)
        {
            if (!IsUsable(type, step, boss)) continue;

            roll -= type.WeightAtStep(step);
            if (roll <= 0f) return type;
        }

        return null;
    }

    bool IsUsable(EnemyType type, int step, bool boss)
    {
        if (type == null) return false;
        if (type.prefab == null && baseEnemyPrefab == null) return false;
        if (type.IsBoss != boss) return false;

        return type.WeightAtStep(step) > 0f;
    }

    /// <summary>
    /// Applies the level's difficulty and the archetype to one fresh enemy. The two
    /// compose: the level sets the baseline for everything in it, and the archetype
    /// gives this particular body its character.
    /// </summary>
    void Configure(GameObject enemy, EnemyType type, bool boss)
    {
        float healthScale = Level.healthMultiplier * (boss ? Level.bossHealthMultiplier : 1f);
        float damageScale = Level.damageMultiplier;
        float speedScale = Level.speedMultiplier;

        if (type.archetype != null)
        {
            type.archetype.ApplyTo(enemy, healthScale, damageScale, speedScale);
        }
        else
        {
            // No archetype: apply the level's numbers directly to whatever the prefab has.
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
            ai.ApplyDifficulty(Level.aggression);
        }
    }

    // ======================================================================
    // Placement
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
                float angle = NextSpawnAngle();
                float distance = UnityEngine.Random.Range(minSpawnDistanceFromPlayer,
                                                          maxSpawnDistanceFromPlayer);

                Vector3 candidate = origin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas))
                    continue;

                if (requireHidden && IsVisibleToPlayer(hit.position)) continue;

                // Being on the mesh is not the same question as being able to get here
                // from there. Asked last because it is much the most expensive of the
                // three, and because the two cheap filters throw most candidates out.
                if (!CanReachPlayerFrom(hit.position)) continue;

                position = hit.position;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The compass bearing for the next spawn attempt.
    ///
    /// Independent random draws clump: roll a dozen and several land within a few
    /// degrees of each other, which is why plain randomness tends to funnel a level
    /// through one corner of the map. Advancing by the golden angle instead spaces
    /// consecutive spawns about as far apart on the circle as it is possible to get,
    /// and the jitter keeps it from reading as a pattern. Attacks then come at you
    /// from genuinely every direction.
    /// </summary>
    float NextSpawnAngle()
    {
        const float goldenAngle = 2.39996323f;   // radians; pi * (3 - sqrt 5)

        _spawnAngle = Mathf.Repeat(_spawnAngle + goldenAngle, Mathf.PI * 2f);
        return _spawnAngle + UnityEngine.Random.Range(-0.35f, 0.35f);
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

    /// <summary>
    /// Whether an agent standing at a fixed spawn point could walk to the player, with
    /// the point pulled onto the mesh first the way the spawner itself pulls it.
    /// </summary>
    bool Reachable(Vector3 point)
        => !requireReachableSpawns
        || (NavMesh.SamplePosition(point, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas)
            && CanReachPlayerFrom(hit.position));

    /// <summary>
    /// Whether there is a complete NavMesh route from a point to the player.
    ///
    /// This is the whole of the fix for enemies arriving inside somewhere they cannot
    /// leave. <see cref="NavMesh.SamplePosition"/> answers "is there walkable ground
    /// near here", and a sealed shed, the floor inside a bund and the roof of a
    /// container stack all say yes -- they are walkable ground, joined to nothing. An
    /// enemy put on one is not merely useless: the level asks for a fixed number of
    /// kills and scores the player against it, so every trapped body is a star the
    /// geometry took away, and the only thing the player sees is a clock running out
    /// with the arena apparently empty.
    ///
    /// <para>
    /// The player is snapped onto the mesh rather than used raw, because they spend a
    /// good deal of the level off it -- mid-jump, on a crate, on a catwalk -- and a
    /// destination off the mesh makes every route incomplete, which would refuse every
    /// spawn in the level. A player genuinely nowhere near the mesh returns true, since
    /// refusing on an unanswerable question spawns nothing at all.
    /// </para>
    /// </summary>
    bool CanReachPlayerFrom(Vector3 from)
    {
        if (!requireReachableSpawns || player == null) return true;

        if (!NavMesh.SamplePosition(player.position, out NavMeshHit target,
                                    Mathf.Max(navMeshSampleRadius, 8f), NavMesh.AllAreas))
            return true;

        var route = Route;

        return NavMesh.CalculatePath(from, target.position, NavMesh.AllAreas, route)
            && route.status == NavMeshPathStatus.PathComplete;
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

            if (distance >= minSpawnDistanceFromPlayer && Reachable(point.position))
                candidates.Add(point);

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
    void OnEnemyDied(Health health)
    {
        health.Died -= OnEnemyDied;

        bool wasBoss = false;

        for (int i = _alive.Count - 1; i >= 0; i--)
        {
            if (_alive[i].health != health) continue;

            wasBoss = _alive[i].isBoss;
            _alive.RemoveAt(i);
            break;
        }

        if (ActiveBoss == health)
        {
            ActiveBoss = null;
            wasBoss = true;
        }

        if (wasBoss) BossKilled = true;

        Killed++;
        _killedWeight += wasBoss ? Level.bossWeight : 1f;

        var ai = health.GetComponent<EnemyAI>();
        var archetype = ai != null ? ai.archetype : null;

        // Asked of the damage that actually killed this one, so a bomb that softened a
        // target the gun finished is not counted as a bomb kill.
        bool byBomb = health.LastDamage.fromBlast;

        Director?.RegisterKill(archetype, health.LastDamage.isHeadshot,
                               health.transform.position, byBomb);

        Objective?.NoteKilled(health.gameObject);

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

        // The level's own bonus on top of the archetype's, never written into it: an
        // EnemyArchetype is one shared asset, so a level that raised a drop chance on it
        // would raise it for every other level using that enemy, on disk, for good.
        float ammoChance = (archetype != null ? archetype.ammoDropChance : 0f)
                           + (Objective != null ? Objective.AmmoDropBonus : 0f);

        TrySpawnDrop(ammoPickupPrefab, ammoChance, spot);
    }

    bool TrySpawnDrop(GameObject prefab, float chance, Vector3 spot)
    {
        if (prefab == null || chance <= 0f || UnityEngine.Random.value >= chance) return false;

        Instantiate(prefab, spot, Quaternion.identity);
        return true;
    }

    void OnPlayerDamaged(Health health, DamageInfo info)
    {
        Director?.BreakCombo();

        // Accumulated as it lands rather than read off Health at the end, because a player
        // who was hurt and then picked up a medkit still took the damage -- a final health
        // reading would call that run flawless.
        Director?.RegisterPlayerDamage(info.amount);
    }

    void OnPlayerDied(Health health) => Finish(LevelResult.Ending.Died);

    /// <summary>
    /// Kills everything currently alive. Handy for debugging, and it is how the level
    /// test clears a level without having to aim.
    /// </summary>
    public void KillAllEnemies()
    {
        for (int i = _alive.Count - 1; i >= 0; i--)
        {
            var health = _alive[i].health;
            if (health != null && !health.IsDead) health.Kill();
        }
    }
}
