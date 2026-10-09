using System;
using UnityEngine;

/// <summary>
/// Where an enemy has been hurt, and what that does to the way it fights.
///
/// Every hit arrives with the <see cref="BodyPart"/> its hitbox stamped on it. A hit on an
/// arm or a leg is booked to that limb's own health (<see cref="BodyDamageProfile"/>); only
/// a share of it reaches the core, and never below a floor (<see cref="CoreCapFor"/>), so
/// **a limb shot never kills**. The head, neck and torso hit the core in full.
///
/// Each limb has a state that only ever gets worse -- Healthy, Wounded, Crippled, Severed:
///
/// - **Legs.** Wounded: a little slower. One crippled: it limps for the rest of its life,
///   on the right side, with no sprinting. Both crippled or one severed: down on the ground
///   for good, crawling, still shooting. Nothing stands it back up -- not a kick, not the
///   end of a knockdown.
/// - **Arms.** Wounded: worse aim, slower between bursts. One crippled: the rifle held low
///   and one-handed, badly. Both crippled: the rifle drops and it runs or rushes in.
/// - **Head.** A headshot kills an ordinary enemy outright, an elite once its armour is
///   down, and never a boss -- a boss is thrown into a long stagger instead.
///
/// Pain rises with every hit by where it landed and fades; a crippled limb keeps some up
/// for good. High pain is groans, stumbles and a shaking aim.
///
/// How much it takes is the profile, stretched by <see cref="EnemyArchetype.woundResistance"/>.
/// A boss never crawls and never lets go of its weapon. This owns no movement and no
/// animation: <see cref="EnemyAI"/> reads the multipliers, <see cref="EnemyBody"/> the states.
/// </summary>
[DisallowMultipleComponent]
public class EnemyWounds : MonoBehaviour
{
    public enum LegState { Sound, Limping, Crawling }
    public enum LimbState { Healthy, Wounded, Crippled, Severed }

    [Tooltip("The body's rules. Set per enemy type by EnemyArchetype.ApplyTo; this is the fallback, " +
             "and the hitboxes' own profile is used when both are empty.")]
    public BodyDamageProfile profile;

    [Header("Head")]
    [Tooltip("Seconds a headshot a boss survives throws it off balance for.")]
    [Min(0f)] public float headshotStagger = 0.9f;

    [Header("Timing")]
    [Tooltip("Seconds of damage summed into RecentDamage, so a shotgun's pellets read " +
             "as one heavy blow when the body falls.")]
    [Min(0.01f)] public float burstWindow = 0.15f;

    /// <summary>0 weakest, 1 toughest. From the archetype; 0.2 for a hand-placed enemy.</summary>
    public float Resistance { get; private set; } = 0.2f;

    public EnemyArchetype.Role Role { get; private set; } = EnemyArchetype.Role.Standard;

    public LegState Legs { get; private set; }

    /// <summary>The rifle is on the floor. It fights by hand, or runs, from here on.</summary>
    public bool Disarmed { get; private set; }

    /// <summary>A pistol in a good hand (EnemyBody): one hand is enough for it, so a crippled arm does not ruin the aim.</summary>
    [System.NonSerialized] public bool Sidearm;

    /// <summary>One arm is crippled and the other is not: the rifle is held low in one hand.</summary>
    public bool OneHanded => !Disarmed && (AtLeast(BodyPart.LeftArm, LimbState.Crippled) != AtLeast(BodyPart.RightArm, LimbState.Crippled));

    /// <summary>The leg it favours: a crippled one first, otherwise the more hurt.</summary>
    public BodyPart WorseLeg
    {
        get
        {
            var l = State(BodyPart.LeftLeg);
            var r = State(BodyPart.RightLeg);
            if (l != r) return l > r ? BodyPart.LeftLeg : BodyPart.RightLeg;
            return LimbDamage(BodyPart.LeftLeg) >= LimbDamage(BodyPart.RightLeg) ? BodyPart.LeftLeg : BodyPart.RightLeg;
        }
    }

    public BodyPart LastHitPart { get; private set; } = BodyPart.Torso;
    public float LastHitTime { get; private set; } = -999f;

    /// <summary>Damage landed inside the last <see cref="burstWindow"/>, all parts.</summary>
    public float RecentDamage => Time.time - _burstStart <= burstWindow ? _burst : 0f;

    /// <summary>0 to 1. Rises with each hit, fades, never below what crippled limbs keep up.</summary>
    public float Pain { get; private set; }

    /// <summary>Pain is past the profile's high mark: groans, stumbles, a shaking aim.</summary>
    public bool InHighPain => Pain >= Profile.highPain;

    /// <summary>
    /// A wound just changed how it fights: a leg went, or an arm did. Raised once per change,
    /// for the scream and the stumble. The part is the limb.
    /// </summary>
    public event Action<EnemyWounds, BodyPart> Crippled;

    /// <summary>Any limb's state got worse (Wounded included). Limb, then its new state.</summary>
    public event Action<EnemyWounds, BodyPart, LimbState> LimbChanged;

    /// <summary>
    /// Whether a headshot ends it. An ordinary enemy always; an elite once its armour
    /// is gone, so breaking the shield is still the job; a boss never.
    /// </summary>
    public bool HeadshotIsLethal
    {
        get
        {
            switch (Role)
            {
                case EnemyArchetype.Role.Boss: return false;
                case EnemyArchetype.Role.Elite: return _health == null || _health.Shield <= 0f;
                default: return true;
            }
        }
    }

    /// <summary>
    /// What is left of its speed. 1 when both legs are sound. A rigged body that limps or
    /// crawls by animation takes its speed from the clip instead (EnemyMotor).
    /// </summary>
    public float MoveSpeedMultiplier
    {
        get
        {
            var p = Profile;
            switch (Legs)
            {
                case LegState.Crawling: return p.crawlSpeed;
                case LegState.Limping: return Mathf.Lerp(p.limpSpeed, 0.8f, Resistance);
            }
            bool wounded = AtLeast(BodyPart.LeftLeg, LimbState.Wounded) || AtLeast(BodyPart.RightLeg, LimbState.Wounded);
            return wounded ? p.woundedLegSpeed : 1f;
        }
    }

    /// <summary>How much wider its shots spread from arm wounds and pain. 1 when unhurt.</summary>
    public float AimSpreadMultiplier
    {
        get
        {
            var p = Profile;
            float arms = 1f;
            foreach (var arm in Arms)
            {
                var s = State(arm);
                if (s >= LimbState.Crippled) arms = Mathf.Max(arms, Sidearm ? p.woundedArmSpread : p.crippledArmSpread);
                else if (s == LimbState.Wounded) arms = Mathf.Max(arms, p.woundedArmSpread);
            }
            return arms * Mathf.Lerp(1f, p.painSpread, Mathf.Clamp01(Pain));
        }
    }

    /// <summary>Time between bursts, as a multiple: a hurt arm is slower to reload and recover.</summary>
    public float FireIntervalMultiplier
    {
        get
        {
            var p = Profile;
            float m = 1f;
            foreach (var arm in Arms)
            {
                var s = State(arm);
                if (s >= LimbState.Crippled) m = Mathf.Max(m, p.crippledArmFireInterval);
                else if (s == LimbState.Wounded) m = Mathf.Max(m, p.woundedArmFireInterval);
            }
            return m;
        }
    }

    /// <summary>
    /// 0 to 1: how badly a part is hurt. A limb against the damage that cripples it, so 1
    /// means crippled; the torso and head against their own part health. What the blood
    /// on a limb, the bleeding and the way it hangs are drawn from.
    /// </summary>
    public float Severity(BodyPart part)
    {
        var limb = BodyParts.Limb(part);
        if (IsLimb(limb))
        {
            if (State(limb) == LimbState.Severed) return 1f;
            return Mathf.Clamp01(LimbDamage(limb) / Mathf.Max(1f, CrippleDamage(limb)));
        }
        bool head = part == BodyPart.Head;
        float taken = head ? _damage[(int)BodyPart.Head]
                           : _damage[(int)BodyPart.Torso] + _damage[(int)BodyPart.LowerTorso] + _damage[(int)BodyPart.Neck];
        float health = Profile.HealthOf(head ? BodyPart.Head : BodyPart.Torso) * Stretch * MaxHealth;
        return Mathf.Clamp01(taken / Mathf.Max(1f, health));
    }

    public LimbState State(BodyPart limb)
    {
        limb = BodyParts.Limb(limb);
        return IsLimb(limb) ? _limb[Slot(limb)] : LimbState.Healthy;
    }

    public bool AtLeast(BodyPart limb, LimbState state) => State(limb) >= state;

    /// <summary>Damage booked to a limb so far (both its parts), in health points.</summary>
    public float LimbDamage(BodyPart limb)
    {
        switch (BodyParts.Limb(limb))
        {
            case BodyPart.LeftArm: return _damage[(int)BodyPart.LeftArm] + _damage[(int)BodyPart.LeftForearm];
            case BodyPart.RightArm: return _damage[(int)BodyPart.RightArm] + _damage[(int)BodyPart.RightForearm];
            case BodyPart.LeftLeg: return _damage[(int)BodyPart.LeftLeg] + _damage[(int)BodyPart.LeftShin];
            case BodyPart.RightLeg: return _damage[(int)BodyPart.RightLeg] + _damage[(int)BodyPart.RightShin];
            default: return _damage[(int)BodyParts.Limb(limb)];
        }
    }

    /// <summary>The damage that cripples a limb, in health points, toughness included.</summary>
    public float CrippleDamage(BodyPart limb) => Profile.LimbHealth(limb) * Profile.crippledAt * Stretch * MaxHealth;

    /// <summary>
    /// What a limb hit may take off the core: its share of the damage, and never past the
    /// floor. Called by <see cref="Hitbox.Receive"/> with the hit already multiplied.
    /// </summary>
    public float CoreCapFor(float amount)
    {
        var p = Profile;
        float pool = _health != null ? _health.Current + _health.Shield : 0f;
        float floor = p.limbCoreFloor * MaxHealth;
        return Mathf.Clamp(amount * p.limbCoreShare, 0f, Mathf.Max(0f, pool - floor));
    }

    /// <summary>
    /// The limb is gone (a heavy hit with the Blood setting on; Stage 3's dismemberment).
    /// A severed leg puts it on the ground; a severed arm is a crippled arm and then some.
    /// </summary>
    public void Sever(BodyPart limb)
    {
        limb = BodyParts.Limb(limb);
        if (!IsLimb(limb) || Role == EnemyArchetype.Role.Boss) return;
        SetState(limb, LimbState.Severed);
        UpdateLegs();
        UpdateArms();
    }

    // ======================================================================

    static readonly BodyPart[] Arms = { BodyPart.LeftArm, BodyPart.RightArm };
    static readonly BodyPart[] Limbs = { BodyPart.LeftArm, BodyPart.RightArm, BodyPart.LeftLeg, BodyPart.RightLeg };

    static bool IsLimb(BodyPart limb) => limb == BodyPart.LeftArm || limb == BodyPart.RightArm
                                         || limb == BodyPart.LeftLeg || limb == BodyPart.RightLeg;

    static int Slot(BodyPart limb) => limb switch
    {
        BodyPart.LeftArm => 0,
        BodyPart.RightArm => 1,
        BodyPart.LeftLeg => 2,
        _ => 3,
    };

    Health _health;
    EnemyAI _ai;
    BodyDamageProfile _fallback;

    /// <summary>Per fine part, in health points. Indexed by BodyPart.</summary>
    readonly float[] _damage = new float[12];
    readonly LimbState[] _limb = new LimbState[4];
    float _burst;
    float _burstStart = -999f;

    /// <summary>The body's rules and timings in force: this type's profile, or the fallback.</summary>
    public BodyDamageProfile Body => Profile;

    /// <summary>Any limb at Wounded or worse.</summary>
    public bool AnyLimbHurt
    {
        get { foreach (var l in Limbs) if (State(l) >= LimbState.Wounded) return true; return false; }
    }

    BodyDamageProfile Profile
    {
        get
        {
            if (profile != null) return profile;
            if (_fallback == null)
            {
                var box = GetComponentInChildren<Hitbox>();
                _fallback = box != null && box.profile != null ? box.profile : ScriptableObject.CreateInstance<BodyDamageProfile>();
            }
            return _fallback;
        }
    }

    float MaxHealth => _health != null ? Mathf.Max(1f, _health.maxHealth) : 100f;
    float Stretch => 1f + Resistance * Profile.resistanceStretch;

    void Awake()
    {
        _health = GetComponent<Health>();
        _ai = GetComponent<EnemyAI>();
    }

    void OnEnable()
    {
        if (_health != null) _health.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        if (_health != null) _health.Damaged -= OnDamaged;
    }

    /// <summary>Stamped by <see cref="EnemyArchetype.ApplyTo"/> at spawn.</summary>
    public void Configure(EnemyArchetype archetype)
    {
        if (archetype == null) return;
        Role = archetype.role;
        Resistance = Mathf.Clamp01(archetype.woundResistance);
        if (archetype.bodyProfile != null) profile = archetype.bodyProfile;
    }

    void Update()
    {
        if (Pain <= 0f || _health == null || _health.IsDead) return;
        var p = Profile;
        float floor = 0f;
        foreach (var limb in Limbs) if (State(limb) >= LimbState.Crippled) floor += p.crippledPainFloor;
        Pain = Mathf.Max(Mathf.Min(floor, 1f), Pain - p.painFade * Time.deltaTime);
    }

    void OnDamaged(Health health, DamageInfo info)
    {
        if (info.amount <= 0f) return;

        if (Time.time - _burstStart > burstWindow)
        {
            _burst = 0f;
            _burstStart = Time.time;
        }
        _burst += info.amount;

        var limb = BodyParts.Limb(info.part);
        LastHitPart = limb;
        LastHitTime = Time.time;

        // A blast is everywhere at once. Spread across the legs and the body rather than
        // booked to the torso, so a grenade at its feet can take the legs out from under it.
        if (info.fromBlast)
        {
            _damage[(int)BodyPart.LeftLeg] += info.amount * 0.35f;
            _damage[(int)BodyPart.RightLeg] += info.amount * 0.35f;
            _damage[(int)BodyPart.Torso] += info.amount * 0.3f;
        }
        else
        {
            _damage[(int)info.part] += info.amount;
        }

        // Pain: by where, and by how big against a fifth of the pool.
        var p = Profile;
        float size = Mathf.Clamp(info.amount / (0.2f * MaxHealth), 0.3f, 2f);
        Pain = Mathf.Clamp01(Pain + p.PainOf(info.fromBlast ? BodyPart.Torso : info.part) * size);

        // The killing blow changes nothing about how it fights: there is no fight left.
        if (health.IsDead || health.Current <= 0f) return;

        if (limb == BodyPart.Head && !HeadshotIsLethal && _ai != null)
            _ai.ForceStagger(headshotStagger);

        foreach (var l in Limbs)
        {
            float lost = LimbDamage(l) / Mathf.Max(1f, CrippleDamage(l) / p.crippledAt);
            var next = lost >= p.crippledAt ? LimbState.Crippled : lost >= p.woundedAt ? LimbState.Wounded : LimbState.Healthy;
            if (next > State(l)) SetState(l, next);
        }

        UpdateLegs();
        UpdateArms();
    }

    void SetState(BodyPart limb, LimbState state)
    {
        if (state <= _limb[Slot(limb)]) return;
        _limb[Slot(limb)] = state;
        LimbChanged?.Invoke(this, limb, state);
    }

    void UpdateLegs()
    {
        int crippled = 0;
        bool severed = false;
        foreach (var leg in new[] { BodyPart.LeftLeg, BodyPart.RightLeg })
        {
            var s = State(leg);
            if (s >= LimbState.Crippled) crippled++;
            if (s == LimbState.Severed) severed = true;
        }

        LegState next = crippled > 0 ? LegState.Limping : LegState.Sound;
        // Down on the floor for good: both legs gone, or one taken off. Never a boss -- a
        // boss that spends its fight crawling is not a boss fight.
        if ((crippled >= 2 || severed) && Role != EnemyArchetype.Role.Boss) next = LegState.Crawling;
        if (next <= Legs) return;

        Legs = next;
        if (_ai != null) _ai.ForceStagger(next == LegState.Crawling ? 0.9f : 0.35f);
        Crippled?.Invoke(this, WorseLeg);
    }

    void UpdateArms()
    {
        foreach (var arm in Arms)
            if (State(arm) >= LimbState.Crippled && !_armAnnounced[Slot(arm)])
            {
                _armAnnounced[Slot(arm)] = true;
                Crippled?.Invoke(this, arm);
            }

        if (Disarmed || Role == EnemyArchetype.Role.Boss) return;
        if (_ai == null || !_ai.ranged) return;
        if (State(BodyPart.LeftArm) < LimbState.Crippled || State(BodyPart.RightArm) < LimbState.Crippled) return;

        // Neither arm can hold it: the rifle goes, and it runs or rushes in with what it has.
        Disarmed = true;
        _ai.Disarm();

        var limbs = GetComponent<EnemyLimbAnimator>();
        if (limbs != null) limbs.DropWeapon(_health != null ? _health.LastDamage.direction : Vector3.zero);
    }

    readonly bool[] _armAnnounced = new bool[4];

    /// <summary>
    /// Puts it back to unhurt. For a test, or anything that recycles an enemy instead of
    /// spawning a new one.
    /// </summary>
    public void ResetWounds()
    {
        Array.Clear(_damage, 0, _damage.Length);
        Array.Clear(_limb, 0, _limb.Length);
        Array.Clear(_armAnnounced, 0, _armAnnounced.Length);
        Legs = LegState.Sound;
        Disarmed = false;
        Pain = 0f;
        _burst = 0f;
        _burstStart = -999f;
        LastHitPart = BodyPart.Torso;
        LastHitTime = -999f;
    }
}
