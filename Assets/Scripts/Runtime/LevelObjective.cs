using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// What a level asks for beyond killing the roster: the runner to hunt, the ground to
/// hold, the lights that are off, the way out, the charges on their fuses.
///
/// <b>It cannot end a level and it is not allowed to try.</b> The clock ends every level
/// here, without exception, because this kit is meant to run in arenas it did not build
/// and in a real level an enemy that falls through a gap stays alive forever -- so any
/// completion requirement is a guaranteed softlock. What this does instead is supply a
/// second source of <i>weight</i> beside the kills, and at most postpone the early finish
/// that a cleared roster grants. If the player never holds the ground or never reaches
/// the van, the clock still arrives and the level is still scored on what was actually
/// done.
///
/// <b>Everything it places is placed at runtime.</b> There is no marked zone in any scene
/// and no extraction point in any prefab: a site is sampled off the navmesh the same way
/// a spawn point is, and the beacon over it is built from primitives. That is not
/// economy -- it is the same reason <see cref="Minimap"/> draws the level rather than
/// photographing it. An objective that needed authored geometry would work in the six
/// arenas the builder makes and nowhere else, and adding one would mean rebuilding every
/// scene.
///
/// One component with a switch rather than six classes: they share the beacon, the
/// sampling, the weight arithmetic and the HUD line, and what differs is a dozen lines
/// each. Six subclasses would be six copies of the shared half.
/// </summary>
[DisallowMultipleComponent]
public class LevelObjective : MonoBehaviour
{
    LevelManager _manager;
    LevelSet.Level _level;

    /// <summary>What has been earned so far, on the same scale a kill is worth 1.</summary>
    public float EarnedWeight { get; private set; }

    /// <summary>
    /// Whether the extra task is done. <b>Read only to decide whether a cleared roster
    /// may finish the level early</b> -- never to decide whether the level ends, which
    /// is the clock's business alone. Objectives that are modifiers are satisfied from
    /// the start, so they cannot hold a level open.
    /// </summary>
    public bool Satisfied { get; private set; } = true;

    /// <summary>One line for the HUD, or empty on a level that asks for nothing extra.</summary>
    public string HudLine { get; private set; } = "";

    /// <summary>
    /// Extra chance that a kill drops ammunition. Only <see cref="LevelSet.Objective.OneMagazine"/>
    /// raises it, and it is read by the spawner rather than written into an archetype --
    /// an <see cref="EnemyArchetype"/> is a shared asset, so a level that edited one
    /// would change every other level that used it, permanently and on disk.
    /// </summary>
    public float AmmoDropBonus { get; private set; }

    LevelSet.Objective Kind => _level != null ? _level.objective : LevelSet.Objective.Clear;

    float Weight => _level != null ? _level.ObjectiveWeight : 0f;

    // ---- the hunt ----------------------------------------------------------------
    EnemyAI _quarry;
    Health _quarryHealth;

    bool QuarryAlive => _quarry != null && _quarryHealth != null && !_quarryHealth.IsDead;

    Transform _quarryBeacon;
    float _nextQuarryScan;

    // ---- the ground --------------------------------------------------------------
    Transform _site;
    Vector3 _sitePoint;
    float _held;
    float _holdRequired;

    /// <summary>How close counts as standing in it.</summary>
    const float HoldRadius = 7f;

    /// <summary>And how close counts as having reached a point you walk to.</summary>
    const float ReachRadius = 4.5f;

    // ---- the lights --------------------------------------------------------------
    readonly List<Light> _dimmed = new List<Light>();
    readonly List<float> _dimmedFrom = new List<float>();
    float _ambientFrom;
    bool _ambientTaken;
    Minimap _blindedMap;

    // ---- the charges -------------------------------------------------------------
    class Charge
    {
        public Transform Beacon;
        public Vector3 At;
        public float Blows;
        public bool Resolved;
        public bool Defused;
    }

    readonly List<Charge> _charges = new List<Charge>();
    int _chargesPlanned;
    int _chargesPlaced;
    float _nextCharge;

    /// <summary>Seconds of clock a charge takes with it when it goes off.</summary>
    const float ChargePenalty = 8f;

    // ==================================================================
    /// <summary>
    /// Attaches the objective for a level and starts it. Returns null for a level that
    /// asks for nothing, so every caller has one thing to null-check rather than an
    /// object that does nothing.
    /// </summary>
    public static LevelObjective Begin(LevelManager manager, LevelSet.Level level)
    {
        if (manager == null || level == null) return null;
        if (level.objective == LevelSet.Objective.Clear) return null;

        var objective = manager.gameObject.AddComponent<LevelObjective>();
        objective._manager = manager;
        objective._level = level;
        objective.BeginKind();

        return objective;
    }

    /// <summary>
    /// Puts back everything borrowed from the scene.
    ///
    /// The lights matter here and nothing else does. <see cref="LevelManager.Restart"/>
    /// re-runs a level without reloading the scene -- which is what happens after a
    /// mid-play recompile -- so a blackout that only ever turned lights down would leave
    /// the arena dark for every level played after it, with nothing to say why.
    /// </summary>
    public void End()
    {
        RestoreLights();

        DestroyIfAny(ref _site);
        DestroyIfAny(ref _quarryBeacon);

        foreach (var charge in _charges) DestroyIfAny(ref charge.Beacon);
        _charges.Clear();

        if (_quarry != null) _quarry.forcedFlight = false;
        _quarry = null;
    }

    void OnDestroy() => End();

    void BeginKind()
    {
        switch (Kind)
        {
            case LevelSet.Objective.OneMagazine:
                BeginOneMagazine();
                break;

            case LevelSet.Objective.Hunt:
                Satisfied = true;
                HudLine = "HUNT: FIND THE RUNNER";
                break;

            case LevelSet.Objective.Hold:
                BeginHold();
                break;

            case LevelSet.Objective.Blackout:
                BeginBlackout();
                break;

            case LevelSet.Objective.Extraction:
                BeginExtraction();
                break;

            case LevelSet.Objective.Disposal:
                BeginDisposal();
                break;
        }
    }

    void Update()
    {
        if (_manager == null || _manager.IsFinished || !_manager.IsRunning) return;

        switch (Kind)
        {
            case LevelSet.Objective.Hunt: TickHunt(); break;
            case LevelSet.Objective.Hold: TickHold(); break;
            case LevelSet.Objective.Extraction: TickExtraction(); break;
            case LevelSet.Objective.Disposal: TickDisposal(); break;
        }
    }

    // ==================================================================
    // One magazine
    //
    // No reloading. The magazine is the level's whole supply and a kill is the only
    // way to get more, so the resource being managed is accuracy rather than ammunition
    // -- which is a different fight against exactly the same roster.
    //
    // The ammunition has to keep coming or this is not a hard level, it is an
    // unwinnable one: a player who misses enough ends up holding an empty gun in a room
    // full of enemies with no melee to fall back on and nothing to do but wait for the
    // clock. That is not difficulty, it is a level that stopped being playable and did
    // not say so.
    // ==================================================================
    void BeginOneMagazine()
    {
        AmmoDropBonus = 0.55f;
        Satisfied = true;
        HudLine = "ONE MAGAZINE - KILLS DROP AMMO";

        var weapon = _manager.player != null
            ? _manager.player.GetComponentInChildren<Weapon>()
            : null;

        if (weapon == null) return;

        // One magazine loaded and nothing in reserve: every round from here on is one a kill
        // dropped. Reloading is allowed -- it is how a dropped box becomes rounds in the gun --
        // so the limit is the ammunition, not the reload key.
        weapon.reloadLocked = false;
        weapon.Refill();
        weapon.SetReserve(0);
    }

    // ==================================================================
    // The hunt
    //
    // One of the roster runs instead of fighting, is marked so it can be found, and is
    // worth most of the level. It is still one of the enemyCount, so killing it also
    // counts as a kill -- the objective weight is what it is worth *on top* of that.
    // ==================================================================
    void TickHunt()
    {
        if (_quarry != null && QuarryAlive)
        {
            if (_quarryBeacon != null)
                _quarryBeacon.position = _quarry.transform.position + Vector3.up * 2.6f;

            HudLine = "HUNT: THE RUNNER IS MARKED";
            return;
        }

        // The quarry can die, and it can also be discarded by the leash -- a runner is
        // the enemy most likely to put itself somewhere the level takes it back. Either
        // way the hunt has to have somebody in it, or a level whose runner fell down a
        // hole is a level with an objective nobody can complete and no way to know.
        if (Time.time < _nextQuarryScan) return;

        _nextQuarryScan = Time.time + 1.5f;
        MarkAQuarry();
    }

    void MarkAQuarry()
    {
        EnemyAI best = null;
        float bestDistance = float.MaxValue;

        foreach (var ai in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
        {
            if (ai == null) continue;

            var health = ai.GetComponent<Health>();
            if (health == null || health.IsDead) continue;

            // Never the boss. A boss that spends the level running away is not a hunt,
            // it is a boss the player cannot fight -- and on a boss level the boss is
            // already most of the weight.
            if (_manager.ActiveBoss != null && health == _manager.ActiveBoss) continue;

            float distance = _manager.player != null
                ? Vector3.Distance(ai.transform.position, _manager.player.position)
                : 0f;

            // The furthest one, so the hunt is a hunt rather than the enemy already in
            // the player's face being relabelled.
            if (best != null && distance <= bestDistance) continue;

            best = ai;
            bestDistance = distance;
        }

        if (best == null)
        {
            HudLine = "HUNT: NO RUNNER LEFT";
            return;
        }

        _quarry = best;
        _quarryHealth = best.GetComponent<Health>();
        _quarry.forcedFlight = true;

        DestroyIfAny(ref _quarryBeacon);
        _quarryBeacon = Beacon("Quarry", best.transform.position + Vector3.up * 2.6f,
                               UITheme.Hazard, 0.7f, 3.2f, marker: true, pip: true);

        HudLine = "HUNT: THE RUNNER IS MARKED";
    }

    /// <summary>
    /// Called by the spawner for every death, so the hunt can pay out for the one that
    /// mattered. The bonus is added once and the quarry dropped, so a second call for
    /// the same body cannot pay twice.
    /// </summary>
    public void NoteKilled(GameObject body)
    {
        if (Kind != LevelSet.Objective.Hunt || body == null) return;
        if (_quarry == null || _quarry.gameObject != body) return;

        EarnedWeight += Weight;
        _quarry = null;
        _quarryHealth = null;

        DestroyIfAny(ref _quarryBeacon);
        HudLine = "HUNT: THE RUNNER IS DOWN";
    }

    // ==================================================================
    // The ground
    //
    // A marked circle, and the weight accrues while the player is standing in it. It is
    // the objective that makes the arena's raised decks and hall roofs worth anything:
    // every other level is fought wherever the player happens to be, and this one says
    // where.
    // ==================================================================
    void BeginHold()
    {
        // A fifth of the clock, floored and capped. Derived rather than authored so a
        // level retuned to twice the length does not quietly become a level where the
        // ground is held for a tenth of it.
        _holdRequired = Mathf.Clamp(_level.timeLimit * 0.22f, 12f, 40f);

        Satisfied = false;
        HudLine = "HOLD THE MARKED GROUND";

        if (!PlaceSite(out _sitePoint, 18f, 42f))
        {
            // Nowhere to put it. The level still plays and still scores its kills; what
            // it must not do is sit there asking for something that does not exist.
            Satisfied = true;
            HudLine = "";
            Debug.LogWarning("[LevelObjective] No reachable ground for the hold site, so this " +
                             "level is scored on its roster alone.", this);
            return;
        }

        _site = Beacon("HoldSite", _sitePoint, UITheme.Coolant, HoldRadius, 6f,
                       marker: true, pip: false);
    }

    void TickHold()
    {
        if (_site == null || _manager.player == null) return;

        bool inside = Flat(_manager.player.position - _sitePoint) <= HoldRadius;

        if (inside) _held += Time.deltaTime;

        float progress = Mathf.Clamp01(_held / Mathf.Max(0.01f, _holdRequired));

        EarnedWeight = Weight * progress;
        Satisfied = progress >= 1f;

        HudLine = Satisfied
            ? "GROUND HELD"
            : inside
                ? $"HOLDING  {_held:0}s / {_holdRequired:0}s"
                : $"HOLD THE MARKED GROUND  {_held:0}s / {_holdRequired:0}s";
    }

    // ==================================================================
    // The lights
    //
    // Not "make everything dark": a level with no contrast in it is not frightening, it
    // is unreadable, and the park already records what that looks like. The sun goes
    // down to a tenth and the ambient with it; the practicals are left alone, because
    // the lamps are the whole point -- what the objective changes is that the player can
    // only see where somebody left a light on.
    // ==================================================================
    void BeginBlackout()
    {
        Satisfied = true;
        HudLine = "THE LIGHTS ARE OUT";

        foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light == null || light.type != LightType.Directional) continue;

            _dimmed.Add(light);
            _dimmedFrom.Add(light.intensity);
            light.intensity *= 0.12f;
        }

        _ambientFrom = RenderSettings.ambientIntensity;
        _ambientTaken = true;
        RenderSettings.ambientIntensity = _ambientFrom * 0.35f;

        // The map is a light too, in the only sense that matters: it is how a player
        // knows where anything is. Leaving it on makes a blackout a cosmetic filter.
        // Its own frame is switched off rather than the component: disabling the
        // behaviour stops it redrawing and leaves the last frame it drew on screen,
        // which is a map of where everything was when the lights went out -- more
        // useful than a live one, and exactly the opposite of the objective.
        _blindedMap = FindAnyObjectByType<Minimap>();

        if (_blindedMap != null && _blindedMap.view != null)
        {
            _blindedMap.view.gameObject.SetActive(false);
            _blindedMap.enabled = false;
        }
    }

    void RestoreLights()
    {
        for (int i = 0; i < _dimmed.Count; i++)
            if (_dimmed[i] != null) _dimmed[i].intensity = _dimmedFrom[i];

        _dimmed.Clear();
        _dimmedFrom.Clear();

        if (_ambientTaken)
        {
            RenderSettings.ambientIntensity = _ambientFrom;
            _ambientTaken = false;
        }

        if (_blindedMap != null)
        {
            _blindedMap.enabled = true;
            if (_blindedMap.view != null) _blindedMap.view.gameObject.SetActive(true);
            _blindedMap = null;
        }
    }

    // ==================================================================
    // The way out
    //
    // Placed at the start and dark until the roster is down, because a marker the player
    // can run to from the first second is a level they can skip the fight in. It is the
    // one objective that is about the clock rather than about the fighting: what it asks
    // is that the arena be cleared with enough time left to cross it.
    // ==================================================================
    void BeginExtraction()
    {
        Satisfied = false;
        HudLine = "CLEAR THE AREA, THEN GET OUT";

        if (!PlaceSite(out _sitePoint, 30f, 70f))
        {
            Satisfied = true;
            HudLine = "";
            Debug.LogWarning("[LevelObjective] No reachable ground for the extraction point, so " +
                             "this level is scored on its roster alone.", this);
            return;
        }

        _site = Beacon("Extraction", _sitePoint, UITheme.Biotic, 2.2f, 7f,
                       marker: true, pip: true);

        // Drawn but unlit until it is worth anything.
        SetBeaconLit(_site, false);
    }

    void TickExtraction()
    {
        if (_site == null || _manager.player == null) return;
        if (Satisfied) return;

        bool open = _manager.Killed >= _manager.TotalEnemies;

        SetBeaconLit(_site, open);

        if (!open)
        {
            HudLine = $"CLEAR THE AREA  {_manager.Killed}/{_manager.TotalEnemies}";
            return;
        }

        float away = Flat(_manager.player.position - _sitePoint);

        if (away <= ReachRadius)
        {
            EarnedWeight = Weight;
            Satisfied = true;
            HudLine = "EXTRACTED";
            return;
        }

        HudLine = $"GET OUT  {away:0}m";
    }

    // ==================================================================
    // The charges
    //
    // A fixed number of them, arriving over the level on their own fuses. Reaching one
    // makes it safe and pays; one that goes off takes seconds off the clock, which is
    // the only thing in this game that does. There is no way to fail this into a
    // softlock: a fuse resolves itself whether or not anybody goes near it, so the
    // objective always finishes.
    // ==================================================================
    void BeginDisposal()
    {
        _chargesPlanned = 4;
        _nextCharge = Time.time + 4f;

        Satisfied = false;
        HudLine = "CHARGES ARE ARMING";
    }

    void TickDisposal()
    {
        if (_chargesPlaced < _chargesPlanned && Time.time >= _nextCharge)
        {
            // Spread across the level rather than all at once, so the player is pulled
            // away from the fight repeatedly instead of solving the objective in one trip.
            _nextCharge = Time.time + Mathf.Max(6f, _level.timeLimit / (_chargesPlanned + 1f));
            PlaceCharge();
        }

        int defused = 0, resolved = 0;
        Charge nearest = null;
        float nearestAway = float.MaxValue;

        foreach (var charge in _charges)
        {
            if (charge.Resolved)
            {
                resolved++;
                if (charge.Defused) defused++;
                continue;
            }

            float away = _manager.player != null
                ? Flat(_manager.player.position - charge.At)
                : float.MaxValue;

            if (away <= ReachRadius)
            {
                charge.Resolved = charge.Defused = true;
                EarnedWeight += Weight / _chargesPlanned;

                DestroyIfAny(ref charge.Beacon);
                resolved++;
                defused++;
                continue;
            }

            if (Time.time >= charge.Blows)
            {
                charge.Resolved = true;
                Blow(charge);
                resolved++;
                continue;
            }

            if (away < nearestAway)
            {
                nearest = charge;
                nearestAway = away;
            }
        }

        Satisfied = _chargesPlaced >= _chargesPlanned && resolved >= _chargesPlaced;

        HudLine = nearest != null
            ? $"DEFUSE  {defused}/{_chargesPlanned}  -  {nearestAway:0}m  -  {Mathf.Max(0f, nearest.Blows - Time.time):0}s"
            : $"DEFUSE  {defused}/{_chargesPlanned}";
    }

    void PlaceCharge()
    {
        if (!PlaceSite(out Vector3 at, 14f, 45f)) return;

        _chargesPlaced++;

        _charges.Add(new Charge
        {
            At = at,
            Blows = Time.time + 26f,
            Beacon = Beacon($"Charge{_chargesPlaced}", at, UITheme.Alert, 1.6f, 5f,
                            marker: true, pip: true)
        });
    }

    void Blow(Charge charge)
    {
        // Real, and it can hurt somebody standing next to it -- a charge that goes off
        // as a light show is a deadline with no teeth. The radius is small and the
        // damage modest: the point of a charge is the clock, not the kill.
        var spec = new BlastSpec
        {
            damage = 34f,
            radius = 6f,
            edgeDamageFraction = 0.25f,
            falloffPower = 1.6f,
            selfDamageFraction = 1f,
            explosionForce = 260f
        };

        Explosion.Blast(charge.At, spec, null, ~0);

        _manager.PenaliseClock(ChargePenalty);
        DestroyIfAny(ref charge.Beacon);
    }

    // ==================================================================
    // Shared
    // ==================================================================

    /// <summary>
    /// Finds somewhere to put a marker: on the navmesh, within a range of the player,
    /// and with a complete route from the player to it.
    ///
    /// <b>The route is the part that matters</b>, and it is the same lesson the spawner
    /// already carries: walkable ground is not the same question as reachable ground,
    /// and every arena is full of the first. A hold site on the roof of a container is
    /// an objective the player can see, cannot get to, and is scored against.
    /// </summary>
    bool PlaceSite(out Vector3 point, float min, float max)
    {
        point = Vector3.zero;
        if (_manager == null || _manager.player == null) return false;

        Vector3 from = _manager.player.position;

        for (int attempt = 0; attempt < 48; attempt++)
        {
            float angle = Random.value * Mathf.PI * 2f;
            float away = Mathf.Lerp(min, max, Random.value);

            var candidate = from + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * away;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                continue;

            if (!_manager.CanPlayerReach(hit.position)) continue;

            point = hit.position;
            return true;
        }

        return false;
    }

    /// <summary>
    /// A ring on the ground with a column of light over it.
    ///
    /// Built rather than instanced, for the reason at the top of this file: an objective
    /// that needed a prefab would need one in every scene. Both pieces are collider-less,
    /// because an objective marker that a player can walk into is one they can be stopped
    /// by -- and it would bake into the navmesh as an obstacle in the exact place the
    /// level is telling them to stand.
    /// </summary>
    Transform Beacon(string name, Vector3 at, Color colour, float radius, float height,
                     bool marker, bool pip)
    {
        var root = new GameObject($"Objective_{name}").transform;
        root.position = at;

        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "Ring";
        Destroy(ring.GetComponent<Collider>());
        ring.transform.SetParent(root, false);
        ring.transform.localScale = new Vector3(radius * 2f, 0.03f, radius * 2f);
        ring.transform.localPosition = new Vector3(0f, 0.04f, 0f);
        Paint(ring, colour, 0.35f);

        var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        column.name = "Column";
        Destroy(column.GetComponent<Collider>());
        column.transform.SetParent(root, false);
        column.transform.localScale = new Vector3(0.5f, height * 0.5f, 0.5f);
        column.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
        Paint(column, colour, 0.85f);

        if (marker)
        {
            var pin = root.gameObject.AddComponent<MinimapMarker>();
            pin.style = pip ? MinimapMarker.Style.Dot : MinimapMarker.Style.Footprint;
            pin.color = colour;
            pin.dotRadius = Mathf.Max(2f, radius);
            pin.moves = true;
            pin.order = 40;
        }

        return root;
    }

    static void SetBeaconLit(Transform beacon, bool lit)
    {
        if (beacon == null) return;

        foreach (var renderer in beacon.GetComponentsInChildren<Renderer>())
            renderer.enabled = lit || renderer.gameObject.name == "Ring";
    }

    static void Paint(GameObject go, Color colour, float alpha)
    {
        var renderer = go.GetComponent<Renderer>();
        if (renderer == null) return;

        var material = renderer.material;

        // Emissive, because every one of these is meant to be found from across an arena
        // and half of them are shown in a level whose lights have just been turned off.
        material.color = new Color(colour.r, colour.g, colour.b, alpha);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", colour * 2.2f);
    }

    static void DestroyIfAny(ref Transform t)
    {
        if (t != null) Destroy(t.gameObject);
        t = null;
    }

    static float Flat(Vector3 delta)
    {
        delta.y = 0f;
        return delta.magnitude;
    }
}
