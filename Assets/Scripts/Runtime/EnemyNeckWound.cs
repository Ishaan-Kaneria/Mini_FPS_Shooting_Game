using UnityEngine;

/// <summary>
/// A round through the neck (his brief, 2026-11-04): the artery pumps blood out in time with the heart, his left hand goes to
/// his throat to stop it, he gives up the fight, sinks to his knees as the blood goes, and bleeds out. Blood pours between
/// his fingers, weakening as the heart fails, and a little after he is dead.
///
/// The bleeding is real damage from whoever shot him (<see cref="DamageInfo.bleed"/>: no spray, flinch or cry of its own), so the
/// kill, the score and the stars count as any other, and <see cref="Hitbox.Receive"/> holds the first round short of a kill
/// so there is someone to do this. A heavy round, a blast, or a second round still finish him at once. A boss never bleeds out. With the Blood
/// setting off he still clutches his throat and dies; no blood is drawn. Added by EnemyGore, so every enemy has it. Holds no static state.
/// </summary>
[DefaultExecutionOrder(500)]    // after EnemyBody has posed the body: the hand goes on last
[DisallowMultipleComponent]
public class EnemyNeckWound : MonoBehaviour
{
    [Tooltip("Seconds a standard enemy takes to bleed out. An enemy with more wound resistance lasts longer.")]
    [Range(2f, 30f)] public float bleedSeconds = 8f;
    [Tooltip("Heartbeats a second: fast, he has been shot.")]
    [Range(0.5f, 4f)] public float heartRate = 2f;
    [Tooltip("Share of each heartbeat the artery is throwing blood.")]
    [Range(0.1f, 0.9f)] public float pulseShare = 0.4f;
    [Tooltip("How much of the stream his hand holds back once it is there.")]
    [Range(0f, 1f)] public float handStops = 0.3f;
    [Tooltip("Seconds the wound goes on pumping after he is dead, weakening.")]
    [Range(0f, 8f)] public float afterDeath = 2.5f;
    [Tooltip("Share of the bleeding at which his knees go.")]
    [Range(0f, 1f)] public float sinkAtShare = 0.4f;
    [Tooltip("Seconds between his choked sounds.")]
    [Range(0.5f, 6f)] public float groanEvery = 1.8f;

    Health _health;
    EnemyAI _ai;
    EnemyBody _body;
    EnemyVoice _voice;
    Transform _neck;
    Vector3 _localPoint, _localOut, _direction;
    GameObject _source;
    bool _open;
    float _openedAt, _diedAt, _hand, _rate, _phase, _nextTick, _nextDrip, _nextGroan;
    int _lastBeat;
    BloodStream _stream;
    float _spurtSide = 1f, _pressure;
    Vector3 _way;

    /// <summary>The stream now, for the checks.</summary>
    public BloodStream Stream => _stream;

    /// <summary>The wound is open and he is still alive: what the checks and the debug line read.</summary>
    public bool Bleeding => _open && _health != null && !_health.IsDead;
    /// <summary>A round to the neck would open it now: not already bleeding, alive, not a boss.</summary>
    public bool CanOpen => isActiveAndEnabled && !_open && _health != null && !_health.IsDead && !IsBoss;
    /// <summary>Where the wound is now.</summary>
    public Vector3 WoundPoint => _neck != null ? _neck.TransformPoint(_localPoint) : transform.position;

    bool IsBoss => _ai != null && _ai.archetype != null && _ai.archetype.role == EnemyArchetype.Role.Boss;

    void Awake()
    {
        _health = GetComponent<Health>();
        _ai = GetComponent<EnemyAI>();
        _body = GetComponent<EnemyBody>();
        _voice = GetComponentInChildren<EnemyVoice>();
    }

    void OnEnable()
    {
        _open = false;
        _hand = 0f;
        if (_ai != null) _ai.ThroatOpen = false;
        if (_health != null) _health.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        if (_health != null) _health.Damaged -= OnDamaged;
        EndStream();
    }

    void EndStream()
    {
        if (_stream != null) Destroy(_stream.gameObject);
        _stream = null;
    }

    void OnDamaged(Health health, DamageInfo info)
    {
        if (_open || info.bleed || info.amount <= 0f || info.part != BodyPart.Neck || info.fromBlast || info.fromMelee || info.isHeadshot) return;
        if (health.IsDead || IsBoss) return;
        if (_neck == null)
        {
            var animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman) _neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            if (_neck == null) return;
        }

        // On the neck's surface, between the collarbones and the jaw, on the side the round struck: the neck bone's own
        // position is down at the shoulders, and the jet came out of his collarbone.
        var animatorHead = GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Head);
        Vector3 middle = animatorHead != null ? Vector3.Lerp(_neck.position, animatorHead.position, 0.4f) : _neck.position;
        Vector3 fromCore = Vector3.ProjectOnPlane(info.point - middle, Vector3.up);
        Vector3 outward = fromCore.sqrMagnitude > 1e-5f ? fromCore.normalized : Vector3.ProjectOnPlane(-info.direction, Vector3.up).normalized;
        _localPoint = _neck.InverseTransformPoint(middle + outward * 0.06f * transform.lossyScale.y);
        _localOut = _neck.InverseTransformDirection(outward);
        _direction = info.direction;
        _source = info.source;

        float resistance = _ai != null && _ai.archetype != null ? _ai.archetype.woundResistance : 0f;
        float seconds = bleedSeconds * (1f + resistance);
        _rate = Mathf.Max(health.Current, 1f) / seconds;
        _open = true;
        _openedAt = Time.time;
        _nextTick = Time.time + 0.25f;
        _nextDrip = Time.time;
        _nextGroan = Time.time + 0.6f;
        _phase = 0f;
        _lastBeat = -1;
        EndStream();
        _spurtSide = Random.value < 0.5f ? -1f : 1f;
        _way = Vector3.zero;
        _pressure = 0f;
        _diedAt = -1f;
        if (_ai != null) _ai.ThroatOpen = true;
        if (_body != null) _body.ClutchNeck(seconds + 2f, seconds * sinkAtShare);
    }

    void Update()
    {
        if (!_open || _health == null) return;
        bool dead = _health.IsDead;
        if (dead && _diedAt < 0f) _diedAt = Time.time;
        if (dead && Time.time - _diedAt > afterDeath) { _open = false; EndStream(); return; }

        if (!dead && Time.time >= _nextTick)
        {
            float step = Time.time - _nextTick + 0.25f;
            _nextTick = Time.time + 0.25f;
            var drain = new DamageInfo(_rate * step, WoundPoint, WorldOut, _direction, _source) { part = BodyPart.Neck, bleed = true };
            _health.ApplyDamage(drain);
            dead = _health.IsDead;
            if (dead) _diedAt = Time.time;
        }
        if (!dead && _voice != null && Time.time >= _nextGroan)
        {
            _nextGroan = Time.time + groanEvery * Random.Range(0.8f, 1.2f);
            _voice.Groan();
        }

        Pump(dead);
    }

    Vector3 WorldOut => _neck != null ? _neck.TransformDirection(_localOut) : -_direction;

    /// <summary>The artery, in beats. The heart fails as the pool does, and the hand holds some back.</summary>
    void Pump(bool dead)
    {
        if (!BloodFX.Enabled) return;
        float life = _health.maxHealth > 0f ? Mathf.Clamp01(_health.Current / _health.maxHealth) : 0f;
        float heart = dead ? 0.22f * Mathf.Clamp01(1f - (Time.time - _diedAt) / Mathf.Max(0.01f, afterDeath)) : Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(life * 3f));
        float rate = dead ? heartRate * 0.5f : heartRate;
        _phase += rate * Time.deltaTime;
        int beat = Mathf.FloorToInt(_phase);
        float within = _phase - beat;
        float pressure = heart * (1f - handStops * _hand);
        // Made here, not at the hit: on the level's first wound the blood's library arrives with the same hit, after this hears of it.
        if (_stream == null) _stream = BloodFX.NewStream();
        // It arcs off to one side of him: thrown straight along the round it came at the shooter end on, a dot.
        Vector3 at = WoundPoint, way = (WorldOut + Vector3.Cross(Vector3.up, WorldOut) * (_spurtSide * 0.8f) + Vector3.up * 0.35f).normalized;
        // One stream that never breaks: each beat rises hard and tails off, and between beats it only sags and thins.
        float shape = within < pulseShare ? 1f - within / pulseShare : 0f;
        // Eased: the bone's small shake and the beat's sharp edge, taken straight, drew the stream as a staircase.
        float ease = 1f - Mathf.Exp(-Time.deltaTime * 7f);
        _way = _way.sqrMagnitude > 0.5f ? Vector3.Slerp(_way, way, ease).normalized : way;
        _pressure = Mathf.Lerp(_pressure, pressure * Mathf.Lerp(0.62f, 1f, shape), 1f - Mathf.Exp(-Time.deltaTime * 12f));
        BloodFX.Stream(_stream, at, _way, _pressure, Time.deltaTime);
        if (beat != _lastBeat)
        {
            _lastBeat = beat;
            BloodFX.JetLanding(at, way, pressure);
        }
        // What runs down his chest and off him, between the beats.
        if (Time.time >= _nextDrip)
        {
            _nextDrip = Time.time + Random.Range(0.25f, 0.5f);
            BloodFX.Drip(at + transform.forward * 0.12f + Random.insideUnitSphere * 0.1f, Random.Range(0.1f, 0.24f));
        }
    }

    void LateUpdate()
    {
        if (!Bleeding || _body == null) { _hand = 0f; return; }
        // There by reflex in a fifth of a second or so after the shock of the hit.
        float since = Time.time - _openedAt;
        _hand = Mathf.MoveTowards(_hand, since > 0.12f ? 1f : 0f, Time.deltaTime * 4.5f);
        _body.ClutchHand(WoundPoint, Mathf.SmoothStep(0f, 1f, _hand));
    }
}
