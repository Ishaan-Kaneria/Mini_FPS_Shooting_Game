using System;
using UnityEngine;

/// <summary>
/// Where an enemy has been hurt, and what that does to the way it fights.
///
/// Every hit arrives with the <see cref="BodyPart"/> its hitbox stamped on it, and this
/// keeps a running total per part as a fraction of the enemy's health. Past a threshold
/// a wound stops being a number and becomes behaviour:
///
/// - **Legs.** A shot leg limps (slower, and the gait shows which leg). A wrecked one --
///   or two hurt ones -- puts it on the floor, where it crawls and, if it is armed, keeps
///   shooting from the ground.
/// - **Arms.** A hurt arm or shoulder shakes the aim. A wrecked gun arm drops the rifle,
///   which falls to the floor as a real object, and it comes at you with its hands.
/// - **Head.** A headshot kills an ordinary enemy outright, an elite once its armour is
///   down, and never a boss -- a boss is thrown into a long stagger instead.
///
/// How much it takes is <see cref="EnemyArchetype.woundResistance"/> and the role: every
/// threshold is stretched by the resistance, a boss never goes down to a crawl or lets go
/// of its weapon, and a strong enemy's limp costs it less speed than a weak one's. That
/// is the "depends on how strong it is" rule, in one place.
///
/// It owns no movement and no animation. <see cref="EnemyAI"/> reads the multipliers
/// below each frame and <see cref="EnemyLimbAnimator"/> reads the states, so a rigged
/// character driven by an Animator can read the same values and never touch this code.
/// </summary>
[DisallowMultipleComponent]
public class EnemyWounds : MonoBehaviour
{
    public enum LegState { Sound, Limping, Crawling }

    [Header("Legs")]
    [Tooltip("Damage to one leg, as a fraction of max health, before it limps. At a " +
             "wound resistance of 0; resistance stretches it.")]
    [Range(0.02f, 1f)] public float limpAt = 0.14f;

    [Tooltip("Damage to one leg before it goes down and crawls. Two limping legs also " +
             "put it on the floor.")]
    [Range(0.05f, 2f)] public float crawlAt = 0.34f;

    [Tooltip("Share of its speed a limp leaves it, for the weakest enemy. A tougher " +
             "one keeps more.")]
    [Range(0.1f, 1f)] public float limpSpeed = 0.6f;

    [Tooltip("Share of its speed left while crawling.")]
    [Range(0.05f, 1f)] public float crawlSpeed = 0.28f;

    [Header("Arms")]
    [Tooltip("Damage to one arm before its aim starts to shake.")]
    [Range(0.02f, 1f)] public float shakyAimAt = 0.08f;

    [Tooltip("Damage to the gun arm before it drops the rifle and fights by hand.")]
    [Range(0.05f, 2f)] public float disarmAt = 0.3f;

    [Tooltip("How much wider its shots spread with an arm wrecked, as a multiple.")]
    [Range(1f, 5f)] public float woundedSpread = 2.4f;

    [Header("Head")]
    [Tooltip("Seconds a headshot a boss survives throws it off balance for.")]
    [Min(0f)] public float headshotStagger = 0.9f;

    [Header("Toughness")]
    [Tooltip("How far a wound resistance of 1 stretches every threshold above. At 2.5 " +
             "a fully resistant enemy takes three and a half times the punishment.")]
    [Min(0f)] public float resistanceStretch = 2.5f;

    [Tooltip("Seconds of damage summed into RecentDamage, so a shotgun's pellets read " +
             "as one heavy blow when the body falls.")]
    [Min(0.01f)] public float burstWindow = 0.15f;

    /// <summary>0 weakest, 1 toughest. From the archetype; 0.2 for a hand-placed enemy.</summary>
    public float Resistance { get; private set; } = 0.2f;

    public EnemyArchetype.Role Role { get; private set; } = EnemyArchetype.Role.Standard;

    public LegState Legs { get; private set; }

    /// <summary>The rifle is on the floor. It fights by hand from here on.</summary>
    public bool Disarmed { get; private set; }

    /// <summary>The leg it favours: the worse of the two. Read by the limp.</summary>
    public BodyPart WorseLeg => _damage[(int)BodyPart.LeftLeg] >= _damage[(int)BodyPart.RightLeg]
        ? BodyPart.LeftLeg
        : BodyPart.RightLeg;

    public BodyPart LastHitPart { get; private set; } = BodyPart.Torso;
    public float LastHitTime { get; private set; } = -999f;

    /// <summary>Damage landed inside the last <see cref="burstWindow"/>, all parts.</summary>
    public float RecentDamage => Time.time - _burstStart <= burstWindow ? _burst : 0f;

    /// <summary>
    /// A wound just changed how it fights: a leg went, or the rifle did. Raised once per
    /// change, for the scream and the stumble.
    /// </summary>
    public event Action<EnemyWounds, BodyPart> Crippled;

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

    /// <summary>What is left of its speed. 1 when both legs are sound.</summary>
    public float MoveSpeedMultiplier
    {
        get
        {
            switch (Legs)
            {
                case LegState.Crawling: return crawlSpeed;
                case LegState.Limping: return Mathf.Lerp(limpSpeed, 0.88f, Resistance);
                default: return 1f;
            }
        }
    }

    /// <summary>How much wider its shots spread from arm wounds. 1 when both arms are sound.</summary>
    public float AimSpreadMultiplier
    {
        get
        {
            float arm = Mathf.Max(Severity(BodyPart.LeftArm) * 0.7f, Severity(BodyPart.RightArm));
            float shaky = Mathf.InverseLerp(Threshold(shakyAimAt) / Threshold(disarmAt), 1f, arm);
            return 1f + (woundedSpread - 1f) * shaky;
        }
    }

    /// <summary>
    /// 0 to 1: how badly a part is hurt, against the damage that would cripple it. What
    /// the blood on a limb and the way it hangs are drawn from. The torso and head
    /// measure against a third of the pool, since nothing cripples them.
    /// </summary>
    public float Severity(BodyPart part)
    {
        float damage = _damage[(int)part];

        switch (part)
        {
            case BodyPart.LeftLeg:
            case BodyPart.RightLeg:
                return Mathf.Clamp01(damage / Threshold(crawlAt));
            case BodyPart.LeftArm:
            case BodyPart.RightArm:
                return Mathf.Clamp01(damage / Threshold(disarmAt));
            default:
                return Mathf.Clamp01(damage / 0.33f);
        }
    }

    Health _health;
    EnemyAI _ai;

    /// <summary>Per part, as a fraction of max health. Indexed by BodyPart.</summary>
    float[] _damage = new float[6];

    float _burst;
    float _burstStart = -999f;

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
    }

    /// <summary>A threshold, stretched by how tough this enemy is.</summary>
    float Threshold(float baseFraction) => baseFraction * (1f + Resistance * resistanceStretch);

    void OnDamaged(Health health, DamageInfo info)
    {
        if (info.amount <= 0f) return;

        if (Time.time - _burstStart > burstWindow)
        {
            _burst = 0f;
            _burstStart = Time.time;
        }

        _burst += info.amount;

        LastHitPart = info.part;
        LastHitTime = Time.time;

        // A blast is everywhere at once. Spread across the limbs rather than booked to
        // the torso, so a grenade at its feet can take the legs out from under it.
        float fraction = info.amount / Mathf.Max(1f, health.maxHealth);

        if (info.fromBlast)
        {
            _damage[(int)BodyPart.LeftLeg] += fraction * 0.35f;
            _damage[(int)BodyPart.RightLeg] += fraction * 0.35f;
            _damage[(int)BodyPart.Torso] += fraction * 0.3f;
        }
        else
        {
            _damage[(int)info.part] += fraction;
        }

        // The killing blow changes nothing about how it fights: there is no fight left.
        if (health.IsDead || health.Current <= 0f) return;

        if (info.part == BodyPart.Head && !HeadshotIsLethal && _ai != null)
            _ai.ForceStagger(headshotStagger);

        UpdateLegs();
        UpdateArms();
    }

    void UpdateLegs()
    {
        float left = _damage[(int)BodyPart.LeftLeg];
        float right = _damage[(int)BodyPart.RightLeg];
        float worst = Mathf.Max(left, right);

        float limp = Threshold(limpAt);
        float crawl = Threshold(crawlAt);

        LegState next = LegState.Sound;

        if (worst >= limp) next = LegState.Limping;

        // Down on the floor: one leg gone, or both hurt. Never a boss -- a boss that
        // spends its fight crawling is not a boss fight.
        bool down = worst >= crawl || (left >= limp && right >= limp);
        if (down && Role != EnemyArchetype.Role.Boss) next = LegState.Crawling;

        if (next <= Legs) return;

        Legs = next;

        // Going down takes a moment and costs it whatever it was doing.
        if (_ai != null) _ai.ForceStagger(next == LegState.Crawling ? 0.9f : 0.35f);

        Crippled?.Invoke(this, WorseLeg);
    }

    void UpdateArms()
    {
        if (Disarmed || Role == EnemyArchetype.Role.Boss) return;
        if (_ai == null || !_ai.ranged) return;

        if (_damage[(int)BodyPart.RightArm] < Threshold(disarmAt)) return;

        Disarmed = true;
        _ai.Disarm();

        var limbs = GetComponent<EnemyLimbAnimator>();
        if (limbs != null) limbs.DropWeapon(_health != null ? _health.LastDamage.direction : Vector3.zero);

        Crippled?.Invoke(this, BodyPart.RightArm);
    }

    /// <summary>
    /// Puts it back to unhurt. For a test, or anything that recycles an enemy instead of
    /// spawning a new one.
    /// </summary>
    public void ResetWounds()
    {
        Array.Clear(_damage, 0, _damage.Length);
        Legs = LegState.Sound;
        Disarmed = false;
        _burst = 0f;
        _burstStart = -999f;
        LastHitPart = BodyPart.Torso;
        LastHitTime = -999f;
    }
}
